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

public static  class Program
{
    public static DiscordSocketClient Client;

    public static ManualResetEvent StoppedEvent = new(false);

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
                DenizenForum = new Forum(Client.GetGuild(315163488085475337ul).GetForumChannel(1026104994149171200ul));
                CitizensForum = new Forum(Client.GetGuild(315163488085475337ul).GetForumChannel(1027028179908558918ul));
                SentinelForum = new Forum(Client.GetGuild(315163488085475337ul).GetForumChannel(1024101613905920052ul));
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

        public ForumTag Bug, Feature, HelpSupport, Discussion;

        public ForumTag NeedsHelper, NeedsDev, NeedsUser;

        public ForumTag Resolved, Invalid;

        public Forum(IForumChannel channel)
        {
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
            Forums.Add(channel.Id, this);
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

    private static ulong LastThreadCreated;

    public static LockObject Lockable = new();

    public static Task Client_ThreadCreated(SocketThreadChannel thread)
    {
        Console.WriteLine($"Thread create {thread.Id} == {thread.Name}, wait");
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
            Forum forum = Forums.GetValueOrDefault(forumChannel.Id);
            if (forum is not null)
            {
                List<ulong> tags = new(thread.AppliedTags);
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
                    thread.ModifyAsync(t => t.AppliedTags = tags);
                }
            }
        }
    }

    public static Task Client_ThreadUpdated(Cacheable<SocketThreadChannel, ulong> oldThread, SocketThreadChannel newThread)
    {
        lock (Lockable)
        {
            if (newThread.ParentChannel is SocketForumChannel forum)
            {

            }
        }
        return Task.CompletedTask;
    }

    public static Task Client_MessageReceived(SocketMessage message)
    {
        return Task.CompletedTask;
    }
}
