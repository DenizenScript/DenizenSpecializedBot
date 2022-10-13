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
using Discord.Rest;

public static  class Program
{
    public static DiscordSocketClient Client;

    public static ManualResetEvent StoppedEvent = new(false);

    public static ulong GuildID = 315163488085475337ul;

    public static ulong HelperRoleID = 315163935139692545ul;

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
                SocketGuild guild = Client.GetGuild(GuildID);
                guild.DownloadUsersAsync();
                DenizenForum = new Forum(guild.GetForumChannel(1026104994149171200ul));
                CitizensForum = new Forum(guild.GetForumChannel(1027028179908558918ul));
                SentinelForum = new Forum(guild.GetForumChannel(1024101613905920052ul));
                Forums.Add(DenizenForum.ID, DenizenForum);
                Forums.Add(CitizensForum.ID, CitizensForum);
                Forums.Add(SentinelForum.ID, SentinelForum);
                ScripterHiringForum = new HiringForum(guild.GetForumChannel(1023545298640982056ul));
                int cmdvers = 3;
                if (!File.Exists("config/cmdvers.txt") || File.ReadAllText("config/cmdvers.txt") != cmdvers.ToString())
                {
                    RegisterCommands();
                    File.WriteAllText("config/cmdvers.txt", cmdvers.ToString());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            Console.WriteLine("Loaded.");
            return Task.CompletedTask;
        };
        Client.ThreadCreated += (thread) => Task.Run(() => Client_ThreadCreated(thread));
        Client.ThreadUpdated += (oldThread, newThread) => Task.Run(() => Client_ThreadUpdated(oldThread, newThread));
        Client.MessageReceived += (message) => Task.Run(() => Client_MessageReceived(message));
        Client.SlashCommandExecuted += (args) => Task.Run(() => Client_SlashCommandExecuted(args));
        Console.WriteLine("Logging in...");
        Client.LoginAsync(TokenType.Bot, File.ReadAllText("config/token.txt")).Wait();
        Console.WriteLine("Starting...");
        Client.StartAsync().Wait();
        Console.WriteLine("Started!");
        StoppedEvent.WaitOne();
    }

    public static void RegisterCommands()
    {
        Console.WriteLine("Re-register commands");
        try
        {
            Client.GetGuild(GuildID).CreateApplicationCommandAsync(new SlashCommandBuilder() { Name = "resolved", Description = "Mark the thread as resolved and close it.", IsDMEnabled = false }.Build()).Wait();
            Client.GetGuild(GuildID).CreateApplicationCommandAsync(new SlashCommandBuilder() { Name = "invalid", Description = "Mark the thread as invalid and close it.", IsDMEnabled = false }.Build()).Wait();
            Client.GetGuild(GuildID).CreateApplicationCommandAsync(new SlashCommandBuilder() { Name = "featurethread", Description = "Mark the thread as a Feature Request. Make sure this is really a Feature Request first.", IsDMEnabled = false }.Build()).Wait();
            Client.GetGuild(GuildID).CreateApplicationCommandAsync(new SlashCommandBuilder() { Name = "bugthread", Description = "Mark the thread as a code Bug that a developer must fix. Do not use this if you're not 100% sure.", IsDMEnabled = false }.Build()).Wait();
            Client.GetGuild(GuildID).CreateApplicationCommandAsync(new SlashCommandBuilder() { Name = "helpthread", Description = "Mark the thread as a Help/Support request thread.", IsDMEnabled = false }.Build()).Wait();
            Client.GetGuild(GuildID).CreateApplicationCommandAsync(new SlashCommandBuilder() { Name = "discussionthread", Description = "Mark the thread as a Discussion thread. Do not use this if you are asking for help with something.", IsDMEnabled = false }.Build()).Wait();
            Client.GetGuild(GuildID).CreateApplicationCommandAsync(new SlashCommandBuilder() { Name = "pleaseclose", Description = "Sends a message to reminder users to close threads.", IsDMEnabled = false }.Build()).Wait();
            Client.GetGuild(GuildID).CreateApplicationCommandAsync(new SlashCommandBuilder() { Name = "specializedbot", Description = "Shows info about the DenizenSpecializedBot.", IsDMEnabled = false }.Build()).Wait();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
        Console.WriteLine("Command registration complete.");
    }

    public static void Client_SlashCommandExecuted(SocketSlashCommand arg)
    {
        Console.WriteLine($"User {arg.User.Id} / {arg.User.Username} tried command {arg.CommandName} in {arg.Channel.Id} / {arg.Channel.Name}");
        try
        {
            SlashCommandHandle(arg);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    public static void SlashCommandHandle(SocketSlashCommand arg)
    {
        bool didDefer = false;
        void Accept(string title, string desc)
        {
            if (didDefer)
            {
                arg.ModifyOriginalResponseAsync(m => m.Embed = new EmbedBuilder() { Title = title, Description = desc }.Build()).Wait();
            }
            else
            {
                arg.RespondAsync(embed: new EmbedBuilder() { Title = title, Description = desc }.Build()).Wait();
            }
        }
        void Refuse(string title, string desc)
        {
            arg.RespondAsync(embed: new EmbedBuilder() { Title = title, Description = desc }.Build(), ephemeral: true).Wait();
        }
        if (arg.CommandName == "specializedbot")
        {
            Accept("DenizenSpecializedBot", "Hello! I'm the Denizen specialized Discord bot. I do things unique to the Denizen discord.\n\nYou can view my source code at: https://github.com/DenizenScript/DenizenSpecializedBot");
            return;
        }
        if (arg.Channel is not SocketThreadChannel thread || thread.ParentChannel is not SocketForumChannel forumChannel || !Forums.TryGetValue(forumChannel.Id, out Forum forum))
        {
            Refuse("Invalid Channel", "That's not valid here. Only in the relevant support forum channels.");
            return;
        }
        thread.DownloadUsersAsync().Wait();
        if (!((thread.Owner is not null && arg.User.Id == thread.Owner.Id) || (arg.User is SocketGuildUser user && user.Roles.Any(r => r.Id == HelperRoleID))))
        {
            Refuse("Not Allowed", "Only helpers or thread owners can use this command.");
            return;
        }
        arg.DeferAsync().Wait();
        didDefer = true;
        try
        {
            List<ulong> tags = new(thread.AppliedTags);
            void RemoveNeedTags()
            {
                tags.Remove(forum.NeedsDev.Id);
                tags.Remove(forum.NeedsHelper.Id);
                tags.Remove(forum.NeedsUser.Id);
            }
            void RemoveTypeTags()
            {
                tags.Remove(forum.Bug.Id);
                tags.Remove(forum.HelpSupport.Id);
                tags.Remove(forum.Feature.Id);
                tags.Remove(forum.Discussion.Id);
            }
            void RemoveResolutionTags()
            {
                tags.Remove(forum.Resolved.Id);
                tags.Remove(forum.Invalid.Id);
            }
            void PublishTags()
            {
                thread.ModifyAsync(t => t.AppliedTags = tags).Wait();
            }
            void CloseThread()
            {
                thread.ModifyAsync(t => t.Archived = true).Wait();
            }
            switch (arg.CommandName)
            {
                case "resolved":
                    {
                        RemoveNeedTags();
                        RemoveResolutionTags();
                        tags.Add(forum.Resolved.Id);
                        PublishTags();
                        Accept("Resolved", "Thread closed as resolved.");
                        CloseThread();
                    }
                    break;
                case "invalid":
                    {
                        RemoveNeedTags();
                        RemoveResolutionTags();
                        tags.Add(forum.Invalid.Id);
                        PublishTags();
                        Accept("Marked Invalid", "Thread closed as invalid.");
                        CloseThread();
                    }
                    break;
                case "featurethread":
                    {
                        RemoveNeedTags();
                        RemoveTypeTags();
                        RemoveResolutionTags();
                        tags.Add(forum.Feature.Id);
                        tags.Add(forum.NeedsDev.Id);
                        Accept("Changed to Feature", "Thread is now a Feature thread. This indicates a request for a new feature to the plugin, that both (A) does not already exist and (B) reasonably can be added. If you are unsure whether this applies, use `/helpthread` to change back to a normal help thread.");
                        PublishTags();
                    }
                    break;
                case "bugthread":
                    {
                        RemoveNeedTags();
                        RemoveTypeTags();
                        RemoveResolutionTags();
                        tags.Add(forum.Bug.Id);
                        tags.Add(forum.NeedsDev.Id);
                        Accept("Changed to Bug", "Thread is now a Bug thread. This indicates a core code bug that a developer must resolved, not an error message or other support topic. Please do not misuse the Bug label. Use `/helpthread` to switch the thread back to a normal help thread if you are not 100% confident it is a code bug.");
                        PublishTags();
                    }
                    break;
                case "helpthread":
                    {
                        RemoveNeedTags();
                        RemoveTypeTags();
                        RemoveResolutionTags();
                        tags.Add(forum.HelpSupport.Id);
                        tags.Add(forum.NeedsHelper.Id);
                        Accept("Changed to Help/Support", "Thread is now a Help/Support thread. A helper will check your thread when available.");
                        PublishTags();
                    }
                    break;
                case "discussionthread":
                    {
                        RemoveNeedTags();
                        RemoveTypeTags();
                        RemoveResolutionTags();
                        tags.Add(forum.Discussion.Id);
                        Accept("Changed to Discussion", "Thread is now a Discussion thread. This indicates that the thread is not requesting help in any way, and is just discussing a broad topic openly. If you need help with something, use `/helpthread` to switch the thread back to a normal help thread.");
                        PublishTags();
                    }
                    break;
                case "pleaseclose":
                    {
                        arg.ModifyOriginalResponseAsync(m => m.Embed = new EmbedBuilder()
                        {
                            Title = "Thread Closing Reminder",
                            Description = "Has your issue been resolved, or your question been answered?\nIf so, please type </resolved:1028673926114594866> to close your thread.\nOr </invalid:1028673926898909185> if it's not possible to resolve.\n\nIf not yet resolved, please reply below to tell us what you still need.\n\n(Note that if there is no reply for a few days, this thread will eventually close itself.)"
                        }.Build()).Wait();
                        thread.SendMessageAsync(thread.Owner is null ? "Error: Missing Owner" : $"<@{thread.Owner.Id}>").Wait();
                    }
                    break;
                default:
                    Console.WriteLine($"Error: invalid command {arg.CommandName}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
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

    public static void Client_ThreadCreated(SocketThreadChannel thread)
    {
        double minutes = DateTimeOffset.Now.Subtract(thread.CreatedAt).TotalMinutes;
        Console.WriteLine($"Thread create {thread.Id} == {thread.Name} created at {thread.CreatedAt}, offset {minutes} min");
        if (Math.Abs(minutes) > 2)
        {
            Console.WriteLine($"Thread ignored due to time offset.");
            return;
        }
        Task.Delay(TimeSpan.FromSeconds(2)).Wait();
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

    public static void Client_ThreadUpdated(Cacheable<SocketThreadChannel, ulong> oldThread, SocketThreadChannel newThread)
    {
        lock (Lockable)
        {
            try
            {
                if (newThread.ParentChannel is SocketForumChannel forumChannel)
                {
                    Console.WriteLine($"Thread {newThread.Id} was updated in a forum");
                    if (Forums.TryGetValue(forumChannel.Id, out Forum forum))
                    {
                        if (newThread.IsArchived)
                        {
                            TaggedNeed need = forum.GetTaggedNeed(newThread.AppliedTags);
                            Console.WriteLine($"Thread {newThread.Id} was closed in a tracked forum, need = {need}");
                            if (need != TaggedNeed.None)
                            {
                                newThread.ModifyAsync(t => t.Archived = false).Wait();
                                if (!LastMessageWasMe(newThread))
                                {
                                    newThread.SendMessageAsync(embed: new EmbedBuilder().WithTitle("Thread Close Blocked").WithDescription(
                                        $"Thread was closed, but still has a **Needs {need}** tag. If closing was intentional, please use `/resolved` or `/invalid`."
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
    }

    public static DateTimeOffset LastScan;

    public static void ScanAllThreads()
    {
        Task.Delay(TimeSpan.FromSeconds(5)).Wait();
        Console.WriteLine("Performing thread scan");
        try
        {
            Scan(DenizenForum);
            Scan(SentinelForum);
            Scan(CitizensForum);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    public static void Scan(Forum forum)
    {
        Task.Delay(TimeSpan.FromSeconds(5)).Wait();
        Console.WriteLine($"Scan channel {forum.ID}");
        SocketForumChannel forumChannel = Client.GetGuild(GuildID).GetForumChannel(forum.ID);
        foreach (RestThreadChannel thread in forumChannel.GetActiveThreadsAsync().Result)
        {
            CheckForThreadClose(thread, forum);
        }
    }

    public static void CheckForThreadClose(RestThreadChannel thread, Forum forum)
    {
        List<IMessage> messages = new(thread.GetMessagesAsync(1).FlattenAsync().Result);
        if (messages.IsEmpty())
        {
            return;
        }
        IMessage last = messages[0];
        double days = Math.Abs(DateTimeOffset.Now.Subtract(last.Timestamp).TotalDays);
        if (last.Author.Id == Client.CurrentUser.Id && last.Embeds.Count == 1 && days > 3)
        {
            IEmbed embed = last.Embeds.ToList()[0];
            if (embed.Title == "Thread Closing Reminder")
            {
                Console.WriteLine($"Apply auto-close to thread {thread.Id} / {thread.Name}");
                thread.SendMessageAsync(embed: new EmbedBuilder() { Title = "Auto-Close Timeout", Description = "No response to request to close thread after 3 days. Automatically closing." }.Build()).Wait();
                List<ulong> tags = new(thread.AppliedTags);
                tags.Remove(forum.NeedsDev.Id);
                tags.Remove(forum.NeedsHelper.Id);
                tags.Remove(forum.NeedsUser.Id);
                thread.ModifyAsync(t => t.AppliedTags = tags).Wait();
                thread.ModifyAsync(t => t.Archived = true).Wait();
            }
        }
    }

    public static void Client_MessageReceived(SocketMessage message)
    {
        try
        {
            lock (Lockable)
            {
                if (Math.Abs(DateTimeOffset.UtcNow.Subtract(LastScan).TotalHours) > 2)
                {
                    LastScan = DateTimeOffset.UtcNow;
                    Task.Run(ScanAllThreads);
                }
                if (message.Channel is not SocketThreadChannel thread)
                {
                    return;
                }
                thread.DownloadUsersAsync().Wait();
                if (thread.ParentChannel is SocketForumChannel forumChannel && Forums.TryGetValue(forumChannel.Id, out Forum forum) && thread.Owner is not null)
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
                        if (message.Author is SocketGuildUser guildUser && guildUser.Roles is not null && guildUser.Roles.Any(r => r.Id == HelperRoleID))
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
    }
}
