namespace DenizenSpecializedBot;

using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Discord.Net;
using Discord.WebSocket;
using Discord;
using System.Net.Http.Headers;
using FreneticUtilities.FreneticToolkit;
using FreneticUtilities.FreneticExtensions;
using System.Threading.Channels;

public static  class Program
{
    public static DiscordSocketClient Client;

    public static ManualResetEvent StoppedEvent = new(false);

    public static ulong GuildID = 315163488085475337ul;

    public static void Main()
    {
        DiscordSocketConfig config = new()
        {
            MessageCacheSize = 50,
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMessages | GatewayIntents.GuildMessageReactions | GatewayIntents.MessageContent | GatewayIntents.GuildMembers
        };
        Client = new DiscordSocketClient(config);
        Client.Ready += () =>
        {
            Console.WriteLine("Bot is ready.");
            try
            {
                DenizenForum = new Forum(Client.GetGuild(GuildID).GetForumChannel(1026104994149171200ul));
                CitizensForum = new Forum(Client.GetGuild(GuildID).GetForumChannel(1027028179908558918ul));
                SentinelForum = new Forum(Client.GetGuild(GuildID).GetForumChannel(1024101613905920052ul));
                Forums.Add(DenizenForum.ID, DenizenForum);
                Forums.Add(CitizensForum.ID, CitizensForum);
                Forums.Add(SentinelForum.ID, SentinelForum);
                ScripterHiringForum = new HiringForum(Client.GetGuild(GuildID).GetForumChannel(1023545298640982056ul));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            Console.WriteLine("Loaded.");
            return Task.CompletedTask;
        };
        Client.ThreadCreated += Client_ThreadCreated;
        Client.ThreadUpdated += Client_ThreadUpdated;
        Client.MessageReceived += Client_MessageReceived;
        Console.WriteLine("Logging in...");
        Client.LoginAsync(TokenType.Bot, File.ReadAllText("config/token.txt")).Wait();
        Console.WriteLine("Starting...");
        Client.StartAsync().Wait();
        Console.WriteLine("Started!");
        StoppedEvent.WaitOne();
    }

    public static Forum DenizenForum, CitizensForum, SentinelForum;

    public static HiringForum ScripterHiringForum;

    public static Dictionary<ulong, Forum> Forums = new();

    public enum TaggedType
    {
        None, Bug, Feature, HelpSupport, Discussion
    }

    public enum TaggedNeed
    {
        None, Helper, Dev, User
    }

    public class Forum
    {
        public Dictionary<string, ForumTag> Tags = new();

        public ulong ID;

        public ForumTag Bug, Feature, HelpSupport, Discussion;

        public ForumTag NeedsHelper, NeedsDev, NeedsUser;

        public ForumTag Resolved, Invalid;

        public Forum(IForumChannel channel)
        {
            ID = channel.Id;
            foreach (ForumTag tag in channel.Tags)
            {
                if (Tags.ContainsKey(tag.Name.ToLowerFast()))
                {
                    Console.WriteLine($"Warning: tag {tag.Name} duplicated for {channel.Name}");
                }
                Tags[tag.Name.ToLowerFast()] = tag;
            }
            Console.WriteLine($"Channel {channel.Name} has tags: {string.Join(", ", Tags.Select(pair => $"{pair.Key} = {pair.Value.Id} = {pair.Value.Name}"))}");
            Bug = Tags.GetValueOrDefault("bug");
            Feature = Tags.GetValueOrDefault("feature request");
            HelpSupport = Tags.GetValueOrDefault("help/support");
            Discussion = Tags.GetValueOrDefault("discussion");
            NeedsHelper = Tags.GetValueOrDefault("needs helper");
            NeedsDev = Tags.GetValueOrDefault("needs dev");
            NeedsUser = Tags.GetValueOrDefault("needs user reply");
            Resolved = Tags.GetValueOrDefault("resolved");
            Invalid = Tags.GetValueOrDefault("invalid");
        }

        public TaggedType GetTaggedType(IReadOnlyCollection<ulong> tags)
        {
            if (tags.Contains(Bug.Id)) { return TaggedType.Bug; }
            if (tags.Contains(Feature.Id)) { return TaggedType.Feature; }
            if (tags.Contains(HelpSupport.Id)) { return TaggedType.HelpSupport; }
            if (tags.Contains(Discussion.Id)) { return TaggedType.Discussion; }
            return TaggedType.None;
        }

        public TaggedNeed GetTaggedNeed(IReadOnlyCollection<ulong> tags)
        {
            if (tags.Contains(NeedsHelper.Id)) { return TaggedNeed.Helper; }
            if (tags.Contains(NeedsDev.Id)) { return TaggedNeed.Dev; }
            if (tags.Contains(NeedsUser.Id)) { return TaggedNeed.User; }
            return TaggedNeed.None;
        }
    }

    public class HiringForum : Forum
    {
        public ForumTag Hiring, Scripter, Information, Completed, Scam, Cancelled;

        public enum TaggedHandledState
        {
            None, Information, Completed, Invalid, Scam, Cancelled
        }

        public HiringForum(IForumChannel channel) : base(channel)
        {
            Hiring = Tags.GetValueOrDefault("hiring");
            Scripter = Tags.GetValueOrDefault("scripter");
            Information = Tags.GetValueOrDefault("information");
            Completed = Tags.GetValueOrDefault("completed");
            Scam = Tags.GetValueOrDefault("scam");
            Cancelled = Tags.GetValueOrDefault("cancelled");
        }

        public TaggedHandledState GetTaggedHandledState(IReadOnlyCollection<ulong> tags)
        {
            if (tags.Contains(Information.Id)) { return TaggedHandledState.Information; }
            if (tags.Contains(Completed.Id)) { return TaggedHandledState.Completed; }
            if (tags.Contains(Invalid.Id)) { return TaggedHandledState.Invalid; }
            if (tags.Contains(Scam.Id)) { return TaggedHandledState.Scam; }
            if (tags.Contains(Cancelled.Id)) { return TaggedHandledState.Cancelled; }
            return TaggedHandledState.None;
        }
    }

    private static ulong LastThreadCreated;

    public static LockObject Lockable = new();

    public static Task Client_ThreadCreated(SocketThreadChannel thread)
    {
        double minutes = DateTimeOffset.Now.Subtract(thread.CreatedAt).TotalMinutes;
        Console.WriteLine($"Thread create {thread.Id} == {thread.Name} created at {thread.CreatedAt}, offset {minutes} min");
        if (Math.Abs(minutes) > 2)
        {
            Console.WriteLine($"Thread ignored due to time offset.");
            return Task.CompletedTask;
        }
        Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(_ =>
        {
            lock (Lockable)
            {
                if (LastThreadCreated == thread.Id)
                {
                    return;
                }
                LastThreadCreated = thread.Id;
                if (Client.GetChannel(thread.Id) is SocketThreadChannel verifiedThread)
                {
                    Console.WriteLine($"Thread verified {thread.Id} == {thread.Name}, will proc");
                    try
                    {
                        HandleNewThreadSync(verifiedThread);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }
            }
        });
        return Task.CompletedTask;
    }

    public static void HandleNewThreadSync(SocketThreadChannel thread)
    {
        if (thread.ParentChannel is SocketForumChannel forumChannel)
        {
            List<ulong> tags = new(thread.AppliedTags);
            if (Forums.TryGetValue(forumChannel.Id, out Forum forum))
            {
                TaggedType type = forum.GetTaggedType(tags);
                TaggedNeed need = forum.GetTaggedNeed(tags);
                bool doModifyTags = false;
                if (type == TaggedType.None)
                {
                    doModifyTags = true;
                    tags.Add(forum.HelpSupport.Id);
                }
                if (need == TaggedNeed.None)
                {
                    doModifyTags = true;
                    tags.Add(forum.NeedsHelper.Id);
                }
                Console.WriteLine($"Thread has type {type} and need {need}, doModify={doModifyTags}");
                if (doModifyTags)
                {
                    thread.ModifyAsync(t => t.AppliedTags = tags).Wait();
                }
            }
        }
    }

    public static bool LastMessageWasMe(SocketThreadChannel channel)
    {
        List<IMessage> messages = new(channel.GetMessagesAsync(1).FlattenAsync().Result);
        if (messages.IsEmpty())
        {
            return false;
        }
        return messages[0].Author.Id == Client.CurrentUser.Id;
    }

    public static Task Client_ThreadUpdated(Cacheable<SocketThreadChannel, ulong> oldThread, SocketThreadChannel newThread)
    {
        lock (Lockable)
        {
            try
            {
                if (newThread.ParentChannel is SocketForumChannel forumChannel)
                {
                    if (Forums.TryGetValue(forumChannel.Id, out Forum forum))
                    {
                        if (newThread.IsArchived)
                        {
                            TaggedNeed need = forum.GetTaggedNeed(newThread.AppliedTags);
                            if (need != TaggedNeed.None)
                            {
                                newThread.ModifyAsync(t => t.Archived = false).Wait();
                                if (!LastMessageWasMe(newThread))
                                {
                                    newThread.SendMessageAsync(embed: new EmbedBuilder().WithTitle("Thread Close Blocked").WithDescription(
                                        $"Thread was closed, but still has a **Needs {need}** tag. If closing was intentional, please remove the **Need** tag and add the :white_check_mark: **Resolved** tag"
                                        ).Build()).Wait();
                                }
                            }
                        }
                    }
                    else if (forumChannel.Id == ScripterHiringForum.ID)
                    {
                        HiringForum.TaggedHandledState handled = ScripterHiringForum.GetTaggedHandledState(newThread.AppliedTags);
                        Console.WriteLine($"In hiring channel and state is {handled}");
                        if (handled == HiringForum.TaggedHandledState.None && newThread.IsArchived)
                        {
                            Console.WriteLine($"Force unarchive");
                            newThread.ModifyAsync(t => t.Archived = false).Wait();
                            if (!LastMessageWasMe(newThread))
                            {
                                Console.WriteLine($"Send message");
                                newThread.SendMessageAsync(embed: new EmbedBuilder().WithTitle("Thread Close Blocked").WithDescription(
                                    $"Thread was closed, but has no resolution tag. If closing was intentional, please add a **Cancelled**, **Invalid**, or **Completed** tag"
                                    ).Build()).Wait();
                            }
                            else
                            {
                                Console.WriteLine($"Don't send message, would duplicate");
                            }
                        }
                    }
                    if (oldThread.HasValue) // TODO: Move this section to ModBot after discord.net update
                    {
                        List<ulong> oldTags = oldThread.Value.AppliedTags.Except(newThread.AppliedTags).ToList();
                        List<ulong> newTags = newThread.AppliedTags.Except(oldThread.Value.AppliedTags).ToList();
                        StringBuilder message = new();
                        if (newTags.Any())
                        {
                            message.Append("Tags added: ");
                            foreach (ulong id in newTags)
                            {
                                message.Append($"**{forumChannel.Tags.First(t => t.Id == id).Name}**, ");
                            }
                        }
                        if (oldTags.Any())
                        {
                            message.Append("Tags removed: ");
                            foreach (ulong id in oldTags)
                            {
                                message.Append($"**{forumChannel.Tags.First(t => t.Id == id).Name}**, ");
                            }
                        }
                        if (message.Length > 0)
                        {
                            Client.GetGuild(GuildID).GetTextChannel(925393831023747072ul).SendMessageAsync($"Tags changed in thread <#{newThread.Id}> `{newThread.Id}`: {message}").Wait();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        return Task.CompletedTask;
    }

    public static Task Client_MessageReceived(SocketMessage message)
    {
        try
        {
            lock (Lockable)
            {
                if (message.Channel is SocketThreadChannel thread && thread.ParentChannel is SocketForumChannel forumChannel && Forums.TryGetValue(forumChannel.Id, out Forum forum))
                {
                    List<ulong> tags = new(thread.AppliedTags);
                    TaggedNeed need = forum.GetTaggedNeed(tags);
                    Console.WriteLine($"{message.Author.Id} wrote a message in {thread.Id} which is owned by {thread.Owner.Id}");
                    if (message.Author.Id == thread.Owner.Id)
                    {
                        if (need == TaggedNeed.User)
                        {
                            Console.WriteLine($"Change from need User to Helper");
                            tags.Remove(forum.NeedsUser.Id);
                            tags.Add(forum.NeedsHelper.Id);
                            thread.ModifyAsync(t => t.AppliedTags = tags).Wait();
                        }
                    }
                    else if (need == TaggedNeed.Helper)
                    {
                        if (message.Author is SocketGuildUser guildUser && guildUser.Roles is not null && guildUser.Roles.Any(r => r.Id == 315163935139692545ul))
                        {
                            Console.WriteLine($"Change from need Helper to User");
                            tags.Remove(forum.NeedsHelper.Id);
                            tags.Add(forum.NeedsUser.Id);
                            thread.ModifyAsync(t => t.AppliedTags = tags).Wait();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
        return Task.CompletedTask;
    }
}
