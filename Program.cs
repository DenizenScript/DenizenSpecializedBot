﻿namespace DenizenSpecializedBot;

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

    public static ulong HelperRoleID = 315163935139692545ul, HelperLiteRoleID = 1238884088174084117ul;

    public static bool HasInited = false;

    public static SocketGuild MainGuild;

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
                MainGuild = Client.GetGuild(GuildID);
                Task.Run(() => MainGuild.DownloadUsersAsync().Wait());
                if (HasInited)
                {
                    Console.WriteLine("Re-readied.");
                    return Task.CompletedTask;
                }
                DenizenForum = new Forum(MainGuild.GetForumChannel(1026104994149171200ul));
                CitizensForum = new Forum(MainGuild.GetForumChannel(1027028179908558918ul)) { CloseWaitDays = 2 };
                SentinelForum = new Forum(MainGuild.GetForumChannel(1024101613905920052ul));
                ClientizenForum = new Forum(MainGuild.GetForumChannel(1131872289688928266ul));
                Forums.Add(DenizenForum.ID, DenizenForum);
                Forums.Add(CitizensForum.ID, CitizensForum);
                Forums.Add(SentinelForum.ID, SentinelForum);
                Forums.Add(ClientizenForum.ID, ClientizenForum);
                ScripterHiringForum = new HiringForum(MainGuild.GetForumChannel(1023545298640982056ul));
                NonPluginSupportForum = new Forum(MainGuild.GetForumChannel(1027976885520584814ul));
                CitizensContribForum = new Forum(MainGuild.GetForumChannel(1101521266105667716ul));
                int cmdvers = 3;
                if (!File.Exists("config/cmdvers.txt") || File.ReadAllText("config/cmdvers.txt") != cmdvers.ToString())
                {
                    RegisterCommands();
                    File.WriteAllText("config/cmdvers.txt", cmdvers.ToString());
                }
                HasInited = true;
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
        if (arg.Channel is not SocketThreadChannel thread)
        {
            Refuse("Invalid Channel", "That's not valid here. Only valid in threads.");
            return;
        }
        if (arg.User is not SocketGuildUser user)
        {
            return;
        }
        thread.DownloadUsersAsync().Wait();
        if (!((thread.Owner is not null && arg.User.Id == thread.Owner.Id) || (user.Roles.Any(r => r.Id == HelperRoleID || r.Id == HelperLiteRoleID))))
        {
            Refuse("Not Allowed", "Only helpers or thread owners can use this command.");
            return;
        }
        void CloseThread()
        {
            thread.ModifyAsync(t => t.Archived = true).Wait();
        }
        if (thread.ParentChannel is not SocketForumChannel forumChannel || !Forums.TryGetValue(forumChannel.Id, out Forum forum))
        {
            if (thread.ParentChannel.Id == NonPluginSupportForum.ID)
            {
                forum = NonPluginSupportForum;
            }
            else if (thread.ParentChannel.Id == ScripterHiringForum.ID)
            {
                forum = ScripterHiringForum;
            }
            else if (thread.ParentChannel.Id == CitizensContribForum.ID)
            {
                forum = CitizensContribForum;
            }
            else
            {
                if (arg.CommandName == "resolved" || arg.CommandName == "invalid")
                {
                    Accept("Closed", "Thread closed as requested by command.");
                    CloseThread();
                    return;
                }
                Refuse("Invalid Channel", "That's not valid here. Only valid in the relevant support forum channels.");
                return;
            }
            List<ulong> tags = new(thread.AppliedTags);
            if (arg.CommandName == "resolved" || arg.CommandName == "invalid" || arg.CommandName == "pleaseclose")
            {
                // Fall through to normal handling
            }
            else
            {
                Refuse("Invalid Channel", "That's not valid here. Only valid in the relevant support forum channels.");
                return;
            }
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
                tags.Remove(forum.NeedsClose.Id);
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
            ThreadArchiveDuration duration = thread.AutoArchiveDuration;
            void PublishTags()
            {
                if (tags.Count > 5)
                {
                    tags = tags.Skip(tags.Count - 5).ToList();
                }
                thread.ModifyAsync(t => { t.AppliedTags = tags; t.AutoArchiveDuration = duration; }).Wait();
            }
            switch (arg.CommandName)
            {
                case "resolved":
                    {
                        if (forum != NonPluginSupportForum && forum != ScripterHiringForum)
                        {
                            if ((tags.Contains(forum.Bug.Id) || tags.Contains(forum.Feature.Id)) && !tags.Contains(forum.NeedsClose.Id) && !user.Roles.Any(r => r.Id == HelperRoleID))
                            {
                                if (!thread.GetMessagesAsync(50).FlattenAsync().Result.Any(m => m.Author.Id == Client.CurrentUser.Id && m.Embeds is not null && m.Embeds.Any(e => e.Title == "Thread Closing Reminder")))
                                {
                                    Accept("Not Yet Resolved", "This thread does not appear to be resolved. If you're sure it is resolved, please use `/pleaseclose` to mark it as needing to be closed first.");
                                    return;
                                }
                            }
                            RemoveNeedTags();
                            RemoveResolutionTags();
                        }
                        if (!tags.Contains(forum.Resolved.Id))
                        {
                            tags.Add(forum.Resolved.Id);
                        }
                        duration = ThreadArchiveDuration.OneDay;
                        PublishTags();
                        Accept("Resolved", "Thread closed as resolved.");
                        CloseThread();
                    }
                    break;
                case "invalid":
                    {
                        if (forum != NonPluginSupportForum && forum != ScripterHiringForum)
                        {
                            RemoveNeedTags();
                            RemoveResolutionTags();
                        }
                        if (!tags.Contains(forum.Invalid.Id))
                        {
                            tags.Add(forum.Invalid.Id);
                        }
                        duration = ThreadArchiveDuration.OneDay;
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
                        duration = ThreadArchiveDuration.OneDay;
                        Accept("Changed to Feature", "Thread is now a Feature thread. This indicates a request for a new feature to the plugin, that both (A) does not already exist and (B) reasonably can be added. If you are unsure whether this applies, use </helpthread:1028674284870180883> to change back to a normal help thread.");
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
                        duration = ThreadArchiveDuration.OneDay;
                        Accept("Changed to Bug", "Thread is now a Bug thread. This indicates a core code bug that a developer must resolved, not an error message or other support topic. Please do not misuse the Bug label. Use </helpthread:1028674284870180883> to switch the thread back to a normal help thread if you are not 100% confident it is a code bug.");
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
                        duration = ThreadArchiveDuration.OneDay;
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
                        duration = ThreadArchiveDuration.OneDay;
                        Accept("Changed to Discussion", "Thread is now a Discussion thread. This indicates that the thread is not requesting help in any way, and is just discussing a broad topic openly. If you need help with something, use </helpthread:1028674284870180883> to switch the thread back to a normal help thread.");
                        PublishTags();
                    }
                    break;
                case "pleaseclose":
                    {
                        arg.ModifyOriginalResponseAsync(m => m.Embed = new EmbedBuilder()
                        {
                            Title = "Thread Closing Reminder",
                            Description = "Has your issue been resolved, or your question been answered?\nIf so, please use the </resolved:1028673926114594866> command to close your thread.\nOr </invalid:1028673926898909185> if it's not possible to resolve.\n\nIf not yet resolved, please reply below to tell us what you still need.\n\n(Note that if there is no reply for a few days, this thread will eventually close itself.)"
                        }.Build()).Wait();
                        thread.SendMessageAsync(thread.Owner is null ? "Error: Missing thread owner. Did they leave the Discord? If so, just use </resolved:1028673926114594866> yourself." : $"<@{thread.Owner.Id}>").Wait();
                        if (forum.NeedsClose.Id != 0)
                        {
                            RemoveNeedTags();
                            tags.Add(forum.NeedsClose.Id);
                        }
                        duration = ThreadArchiveDuration.OneHour;
                        PublishTags();
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

    public static Forum DenizenForum, CitizensForum, SentinelForum, NonPluginSupportForum, CitizensContribForum, ClientizenForum;

    public static HiringForum ScripterHiringForum;

    public static Dictionary<ulong, Forum> Forums = [];

    public enum TaggedType
    {
        None, Bug, Feature, HelpSupport, Discussion
    }

    public enum TaggedNeed
    {
        None, Helper, Dev, User, Close
    }

    public class Forum
    {
        public Dictionary<string, ForumTag> Tags = [];

        public ulong ID;

        public ForumTag Bug, Feature, HelpSupport, Discussion;

        public ForumTag NeedsHelper, NeedsDev, NeedsUser, NeedsClose;

        public ForumTag Resolved, Invalid;

        public int CloseWaitDays = 3;

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
            NeedsClose = Tags.GetValueOrDefault("needs close");
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
            if (tags.Contains(NeedsClose.Id)) { return TaggedNeed.Close; }
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
            Resolved = Completed;
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
            bool doModifyTags = false;
            if (Forums.TryGetValue(forumChannel.Id, out Forum forum))
            {
                TaggedType type = forum.GetTaggedType(tags);
                TaggedNeed need = forum.GetTaggedNeed(tags);
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
            }
            else if (forumChannel.Id == CitizensContribForum.ID)
            {
                TaggedNeed need = CitizensContribForum.GetTaggedNeed(tags);
                if (need == TaggedNeed.None)
                {
                    doModifyTags = true;
                    tags.Add(CitizensContribForum.NeedsDev.Id);
                }
                Console.WriteLine($"Thread has need {need}, doModify={doModifyTags}");
            }
            if (doModifyTags)
            {
                thread.ModifyAsync(t => { t.AppliedTags = tags; t.AutoArchiveDuration = ThreadArchiveDuration.OneDay; }).Wait();
            }
        }
    }

    public static void SendCloseButtonNotice(SocketThreadChannel thread, string message)
    {
        Console.WriteLine($"Cancel archive of {thread.Id}");
        thread.ModifyAsync(t => t.Archived = false).Wait();
        thread.Guild.GetAuditLogsAsync(10).AggregateAsync((x, y) => x.Union(y).ToList()).AsTask().ContinueWith(list =>
        {
            try
            {
                RestAuditLogEntry audit = list.Result.Where(a => a.Action == ActionType.ThreadUpdate && (a.Data as ThreadUpdateAuditLogData).Thread?.Id == thread.Id && thread.Guild.GetUser(a.User.Id) is SocketGuildUser user && !user.IsBot).FirstOrDefault();
                if (audit is null)
                {
                    Console.WriteLine($"No message for {thread.Id} because no log");
                }
                else
                {
                    SocketGuildUser user = thread.Guild.GetUser(audit.User.Id);
                    Console.WriteLine($"Send message for {thread.Id} because {user.Id} tried to close");
                    thread.SendMessageAsync(text: $"<@{user.Id}>", embed: new EmbedBuilder() { Title = "Thread Close Blocked", Description = message }.Build()).Wait();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to check thread {thread.Id} because {ex}");
            }
        });
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
                    if (Forums.TryGetValue(forumChannel.Id, out Forum forum) || forumChannel.Id == CitizensContribForum.ID)
                    {
                        if (forumChannel.Id == CitizensContribForum.ID)
                        {
                            forum = CitizensContribForum;
                        }
                        if (newThread.IsArchived)
                        {
                            TaggedNeed need = forum.GetTaggedNeed(newThread.AppliedTags);
                            Console.WriteLine($"In a tracked support forum, need = {need}");
                            if (need != TaggedNeed.None)
                            {
                                SendCloseButtonNotice(newThread, $"Thread was closed either automatically by timeout or by the Discord manual close button. If closing was intentional, please use </resolved:1028673926114594866> or </invalid:1028673926898909185>.");
                            }
                        }
                    }
                    else if (forumChannel.Id == ScripterHiringForum.ID)
                    {
                        HiringForum.TaggedHandledState handled = ScripterHiringForum.GetTaggedHandledState(newThread.AppliedTags);
                        Console.WriteLine($"In hiring channel and state is {handled}");
                        if (handled == HiringForum.TaggedHandledState.None && newThread.IsArchived)
                        {
                            SendCloseButtonNotice(newThread, $"Thread was closed, but has no resolution tag. If closing was intentional, please add a **Cancelled**, **Invalid**, or **Completed** tag.");
                        }
                    }
                    if (!newThread.IsArchived && (!oldThread.HasValue || oldThread.Value.IsArchived))
                    {
                        newThread.Guild.GetAuditLogsAsync(5).AggregateAsync((x, y) => x.Union(y).ToList()).AsTask().ContinueWith(list =>
                        {
                            RestAuditLogEntry audit = list.Result.Where(a => a.Action == ActionType.ThreadUpdate && a.Data is ThreadUpdateAuditLogData data && data.Before.IsArchived && !data.After.IsArchived && data.Thread?.Id == newThread.Id && newThread.Guild.GetUser(a.User.Id) is SocketGuildUser user).FirstOrDefault();
                            if (audit is null)
                            {
                                Console.WriteLine($"No reopen message for {newThread.Id} because no log");
                            }
                            else
                            {
                                SocketGuildUser user = newThread.Guild.GetUser(audit.User.Id);
                                if (user.IsBot)
                                {
                                    Console.WriteLine($"No reopen message for {newThread.Id} because was a bot");
                                }
                                else
                                {
                                    Console.WriteLine($"Send message for {newThread.Id} because {user.Id} tried to close");
                                    newThread.SendMessageAsync(embed: new EmbedBuilder() { Title = "Thread Reopened", Description = $"Thread was manually reopened by <@{user.Id}>." }.Build()).Wait();
                                }
                            }
                        });
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
            Scan(ClientizenForum);
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
        List<IMessage> messages = new(thread.GetMessagesAsync(2).FlattenAsync().Result);
        if (messages.Count < 2)
        {
            return;
        }
        if (!Forums.TryGetValue(thread.ParentChannelId, out forum)) // re-get the forum from actual ID because Discord returns threads in other forums for some reason
        {
            return;
        }
        IMessage last = messages[0];
        IMessage secondLast = messages[1];
        double days = Math.Abs(DateTimeOffset.Now.Subtract(last.Timestamp).TotalDays);
        int maxDays = forum.CloseWaitDays;
        if (last.Author.Id == Client.CurrentUser.Id && secondLast.Author.Id == Client.CurrentUser.Id && days > maxDays)
        {
            if (secondLast.Embeds.Count == 1 && last.Embeds.Count == 0)
            {
                last = secondLast;
            }
            if (last.Embeds.Count == 1)
            {
                IEmbed embed = last.Embeds.ToList()[0];
                if (embed.Title == "Thread Closing Reminder")
                {
                    Console.WriteLine($"Apply auto-close to thread {thread.Id} / {thread.Name}, in forum {forum.ID}");
                    thread.SendMessageAsync(embed: new EmbedBuilder() { Title = "Auto-Close Timeout", Description = $"No response to request to close thread after {maxDays} days. Automatically closing." }.Build()).Wait();
                    List<ulong> tags = new(thread.AppliedTags);
                    for (int i = 0; i < 5; i++) // Backup because might have borked duplicates
                    {
                        tags.Remove(forum.NeedsDev.Id);
                        tags.Remove(forum.NeedsHelper.Id);
                        tags.Remove(forum.NeedsUser.Id);
                        tags.Remove(forum.NeedsClose.Id);
                    }
                    Console.WriteLine($"Setting tags to {string.Join(',', tags)}, exclude {forum.NeedsDev.Id} and {forum.NeedsHelper.Id} and {forum.NeedsUser.Id}");
                    thread.ModifyAsync(t => t.AppliedTags = tags).Wait();
                    thread.ModifyAsync(t => t.Archived = true).Wait();
                    try
                    {
                        Task.Delay(TimeSpan.FromSeconds(1)).Wait();
                        thread.UpdateAsync().Wait();
                        if (!thread.IsArchived)
                        {
                            thread.ModifyAsync(t => t.Archived = true).Wait();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }
            }
        }
    }

    public static ulong LastUserNotifiedChannelRedir = 0;

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
                if (message.Channel is not IGuildChannel guildChannel || guildChannel.GuildId != GuildID)
                {
                    return;
                }
                if (message.Channel.Id == 477340871927398400ul) // chatter
                {
                    if (message.Author.IsBot || message.Author.IsWebhook)
                    {
                        return;
                    }
                    Console.WriteLine($"Chatter message from {message.Author.Id} (last notified = {LastUserNotifiedChannelRedir}), (roles = {(message.Author is SocketGuildUser udebug ? string.Join(",", udebug.Roles.Select(s => s.Id)) : "")} of {message.Content}");
                    if (message.Author is not SocketGuildUser user || user.Roles.Any(r => r.Id == 521680043685052418ul)) // NotSilent role
                    {
                        return;
                    }
                    if (message.Author.Id == LastUserNotifiedChannelRedir)
                    {
                        return;
                    }
                    string text = message.Content.ToLowerFast();
                    if (text.Contains("citizen") || text.Contains(" npc") || text.Contains("npc "))
                    {
                        LastUserNotifiedChannelRedir = message.Author.Id;
                        (message as IUserMessage).ReplyAsync($"Hey! if you're here to ask about Citizens, please make a post in the <#1027028179908558918> channel.").Wait();
                    }
                    return;
                }
                if (message.Channel.Id == 1158042970214379611ul) // do-not-use
                {
                    if (message.Author is not SocketGuildUser user || user.IsBot || user.IsWebhook || user.Roles.Any(r => r.Id == HelperRoleID))
                    {
                        return;
                    }
                    SocketThreadChannel botSpam = MainGuild.GetThreadChannel(1134366605321699388ul); // mod bot spam
                    botSpam.SendMessageAsync($"<@492222895058059274> ban <@{user.Id}> 7d Automatic ban - Posted in do-not-use channel").Wait();
                    message.DeleteAsync().Wait();
                }
                if (message.Content.Contains("<@&315163832861589505>") || message.Content.Contains("<@&318268857787875329>"))
                {
                    if (message.Author is not SocketGuildUser user || user.IsBot || user.IsWebhook || user.Roles.Any(r => r.Id == HelperRoleID))
                    {
                        return;
                    }
                    message.Channel.SendMessageAsync($"<@492222895058059274> timeout <@{user.Id}> 12h Automatic Timeout (Rule 3) - Abusive role pings. Please read and follow the <#593363346124963850>").Wait();
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
                        if (need == TaggedNeed.User || need == TaggedNeed.Close)
                        {
                            Console.WriteLine($"Change from need {need} to Helper");
                            tags.Remove(forum.NeedsUser.Id);
                            tags.Remove(forum.NeedsClose.Id);
                            tags.Add(forum.NeedsHelper.Id);
                            thread.ModifyAsync(t => { t.AppliedTags = tags; t.AutoArchiveDuration = ThreadArchiveDuration.OneDay; }).Wait();
                        }
                    }
                    else if (need == TaggedNeed.Helper || need == TaggedNeed.Close)
                    {
                        if (message.Author is SocketGuildUser guildUser && guildUser.Roles is not null && guildUser.Roles.Any(r => r.Id == HelperRoleID || r.Id == HelperLiteRoleID))
                        {
                            Console.WriteLine($"Change from need {need} to User");
                            tags.Remove(forum.NeedsHelper.Id);
                            tags.Remove(forum.NeedsClose.Id);
                            tags.Add(forum.NeedsUser.Id);
                            thread.ModifyAsync(t => { t.AppliedTags = tags; t.AutoArchiveDuration = ThreadArchiveDuration.OneHour; }).Wait();
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
