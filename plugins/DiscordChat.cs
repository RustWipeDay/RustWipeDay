#define RUST
using ConVar;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Ext.Discord.Cache;
using Oxide.Ext.Discord.Clients;
using Oxide.Ext.Discord.Connections;
using Oxide.Ext.Discord.Constants;
using Oxide.Ext.Discord.Entities;
using Oxide.Ext.Discord.Extensions;
using Oxide.Ext.Discord.Interfaces;
using Oxide.Ext.Discord.Libraries;
using Oxide.Ext.Discord.Logging;
using Oxide.Ext.Discord.Types;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

//DiscordChat created with PluginMerge v(1.0.7.0) by MJSU @ https://github.com/dassjosh/Plugin.Merge
namespace Oxide.Plugins
{
    [Info("Discord Chat", "MJSU", "3.0.0")]
    [Description("Allows chatting between discord and game server")]
    public partial class DiscordChat : CovalencePlugin, IDiscordPlugin, IDiscordPool
    {
        #region Plugins\DiscordChat.AdminChat.cs
        private AdminChatSettings _adminChatSettings;
        
        private const string AdminChatPermission = "adminchat.use";
        
        //Hook called from AdminChat Plugin
        [HookMethod(nameof(OnAdminChat))]
        public void OnAdminChat(IPlayer player, string message)
        {
            if (IsAdminChatEnabled())
            {
                HandleMessage(message, player, player.GetDiscordUser(), MessageSource.PluginAdminChat, null);
            }
        }
        
        //Message sent in admin chat channel. Process bot replace and sending to server
        public void HandleAdminChatDiscordMessage(DiscordMessage message)
        {
            IPlayer player = message.Author.Player;
            if (player == null)
            {
                message.ReplyWithGlobalTemplate(Client, TemplateKeys.Error.AdminChat.NotLinked, null, GetDefault().AddMessage(message));
                return;
            }
            
            if (!CanPlayerAdminChat(player))
            {
                message.ReplyWithGlobalTemplate(Client, TemplateKeys.Error.AdminChat.NoPermission, null, GetDefault().AddPlayer(player).AddMessage(message));
                return;
            }
            
            HandleMessage(message.Content, player, player.GetDiscordUser(), MessageSource.PluginAdminChat, message);
        }
        
        public bool IsAdminChatEnabled() => _adminChatSettings.Enabled && Sends.ContainsKey(MessageSource.PluginAdminChat);
        public bool CanPlayerAdminChat(IPlayer player) => player != null && _adminChatSettings.Enabled && player.HasPermission(AdminChatPermission);
        #endregion

        #region Plugins\DiscordChat.BetterChat.cs
        public bool SendBetterChatMessage(IPlayer player, string message, MessageSource source)
        {
            if (!IsPluginLoaded(BetterChat))
            {
                return false;
            }
            
            Dictionary<string, object> data = BetterChat.Call<Dictionary<string, object>>("API_GetMessageData", player, message);
            if (source == MessageSource.Discord && !string.IsNullOrEmpty(_pluginConfig.ChatSettings.DiscordTag))
            {
                BetterChatSettings settings = _pluginConfig.PluginSupport.BetterChat;
                if (data["Titles"] is List<string> titles)
                {
                    titles.Add(_pluginConfig.ChatSettings.DiscordTag);
                    while (titles.Count > settings.MaxTags)
                    {
                        titles.RemoveAt(0);
                    }
                }
            }
            BetterChat.Call("API_SendMessage", data);
            return true;
        }
        #endregion

        #region Plugins\DiscordChat.Clans.cs
        private void OnClanChat(IPlayer player, string message)
        {
            Sends[MessageSource.PluginClan]?.QueueMessage(Lang(LangKeys.Discord.PluginClans.ClanMessage, GetClanPlaceholders(player, message)));
        }
        
        private void OnAllianceChat(IPlayer player, string message)
        {
            Sends[MessageSource.PluginAlliance]?.QueueMessage(Lang(LangKeys.Discord.PluginClans.AllianceMessage, GetClanPlaceholders(player, message)));
        }
        
        public PlaceholderData GetClanPlaceholders(IPlayer player, string message)
        {
            return GetDefault().AddPlayer(player).Add(PlaceholderDataKeys.PlayerMessage, message);
        }
        #endregion

        #region Plugins\DiscordChat.DiscordHooks.cs
        [HookMethod(DiscordExtHooks.OnDiscordClientCreated)]
        private void OnDiscordClientCreated()
        {
            if (!string.IsNullOrEmpty(_pluginConfig.DiscordApiKey))
            {
                RegisterPlaceholders();
                RegisterTemplates();
                Client.Connect(new BotConnection
                {
                    Intents = GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
                    ApiToken = _pluginConfig.DiscordApiKey,
                    LogLevel = _pluginConfig.ExtensionDebugging
                });
            }
        }
        
        [HookMethod(DiscordExtHooks.OnDiscordGatewayReady)]
        private void OnDiscordGatewayReady(GatewayReadyEvent ready)
        {
            if (ready.Guilds.Count == 0)
            {
                PrintError("Your bot was not found in any discord servers. Please invite it to a server and reload the plugin.");
                return;
            }
            
            DiscordApplication app = Client.Bot.Application;
            if (!app.HasApplicationFlag(ApplicationFlags.GatewayMessageContentLimited))
            {
                PrintWarning($"You will need to enable \"Message Content Intent\" for {Client.Bot.BotUser.Username} @ https://discord.com/developers/applications\n by April 2022" +
                $"{Name} will stop function correctly after that date until that is fixed. Once updated please reload {Name}.");
            }
            
            Puts($"{Title} Ready");
        }
        
        [HookMethod(DiscordExtHooks.OnDiscordGuildCreated)]
        private void OnDiscordGuildCreated(DiscordGuild guild)
        {
            if (_pluginConfig.ChatSettings.DiscordToServer)
            {
                SetupChannel(guild, MessageSource.Server, _pluginConfig.ChatSettings.ChatChannel, _pluginConfig.ChatSettings.UseBotToDisplayChat, HandleDiscordChatMessage);
            }
            else
            {
                SetupChannel(guild, MessageSource.Server, _pluginConfig.ChatSettings.ChatChannel, _pluginConfig.ChatSettings.UseBotToDisplayChat);
            }
            
            SetupChannel(guild, MessageSource.Discord, _pluginConfig.ChatSettings.ChatChannel, _pluginConfig.ChatSettings.UseBotToDisplayChat);
            
            SetupChannel(guild, MessageSource.Connecting, _pluginConfig.PlayerStateSettings.PlayerStateChannel);
            SetupChannel(guild, MessageSource.Connected, _pluginConfig.PlayerStateSettings.PlayerStateChannel);
            SetupChannel(guild, MessageSource.Disconnected, _pluginConfig.PlayerStateSettings.PlayerStateChannel);
            SetupChannel(guild, MessageSource.PluginAdminChat, _pluginConfig.PluginSupport.AdminChat.ChatChannel, _pluginConfig.ChatSettings.UseBotToDisplayChat, HandleAdminChatDiscordMessage);
            SetupChannel(guild, MessageSource.PluginClan, _pluginConfig.PluginSupport.Clans.ClansChatChannel, _pluginConfig.ChatSettings.UseBotToDisplayChat);
            SetupChannel(guild, MessageSource.PluginAlliance, _pluginConfig.PluginSupport.Clans.AllianceChatChannel, _pluginConfig.ChatSettings.UseBotToDisplayChat);
            
            #if RUST
            SetupChannel(guild, MessageSource.Team, _pluginConfig.ChatSettings.TeamChannel);
            SetupChannel(guild, MessageSource.Cards, _pluginConfig.ChatSettings.CardsChannel);
            SetupChannel(guild, MessageSource.Clan, _pluginConfig.ChatSettings.ClansChannel);
            #endif
            
            if (_pluginConfig.ChatSettings.ChatChannel.IsValid()
            #if RUST
            || _pluginConfig.ChatSettings.TeamChannel.IsValid()
            || _pluginConfig.ChatSettings.CardsChannel.IsValid()
            #endif
            )
            {
                #if RUST
                Subscribe(nameof(OnPlayerChat));
                #else
                Subscribe(nameof(OnUserChat));
                #endif
            }
            
            if (_pluginConfig.PlayerStateSettings.PlayerStateChannel.IsValid())
            {
                if (_pluginConfig.PlayerStateSettings.SendConnectingMessage)
                {
                    Subscribe(nameof(OnUserApproved));
                }
                
                if (_pluginConfig.PlayerStateSettings.SendConnectedMessage)
                {
                    Subscribe(nameof(OnUserConnected));
                }
                
                if (_pluginConfig.PlayerStateSettings.SendDisconnectedMessage)
                {
                    Subscribe(nameof(OnUserDisconnected));
                }
            }
            
            if (_pluginConfig.ServerStateSettings.ServerStateChannel.IsValid())
            {
                Subscribe(nameof(OnServerShutdown));
            }
            
            if (_pluginConfig.PluginSupport.Clans.ClansChatChannel.IsValid())
            {
                Subscribe(nameof(OnClanChat));
            }
            
            if (_pluginConfig.PluginSupport.Clans.AllianceChatChannel.IsValid())
            {
                Subscribe(nameof(OnAllianceChat));
            }
            
            timer.In(0.1f, () =>
            {
                if (!_serverInitCalled && _pluginConfig.ServerStateSettings.SendBootingMessage)
                {
                    SendGlobalTemplateMessage(TemplateKeys.Server.Booting, FindChannel(_pluginConfig.ServerStateSettings.ServerStateChannel));
                }
            });
        }
        
        public void SetupChannel(DiscordGuild guild, MessageSource source, Snowflake id, bool wipeNonBotMessages = false, Action<DiscordMessage> callback = null)
        {
            if (!id.IsValid())
            {
                return;
            }
            
            DiscordChannel channel = guild.Channels[id];
            if (channel == null)
            {
                //PrintWarning($"Channel with ID: '{id}' not found in guild");
                return;
            }
            
            if (callback != null)
            {
                _subscriptions.AddChannelSubscription(Client, id, callback);
            }
            
            if (wipeNonBotMessages)
            {
                channel.GetMessages(Client, new ChannelMessagesRequest{Limit = 100})
                .Then(messages =>
                {
                    OnGetChannelMessages(messages, callback);
                });
            }
            
            Sends[source] = new DiscordSendQueue(channel, GetTemplateName(source), timer);;
            Puts($"Setup Channel {source} With ID: {id}");
        }
        
        private void OnGetChannelMessages(List<DiscordMessage> messages, Action<DiscordMessage> callback)
        {
            if (messages.Count == 0 || callback == null)
            {
                return;
            }
            
            foreach (DiscordMessage message in messages
            .Where(m => !m.Author.IsBot
            && (DateTimeOffset.UtcNow - m.Id.GetCreationDate()).TotalDays < 14
            && CanSendMessage(m.Content, m.Author.Player, m.Author, MessageSource.Discord, m)))
            {
                callback.Invoke(message);
            }
        }
        
        public void HandleDiscordChatMessage(DiscordMessage message)
        {
            IPlayer player = message.Author.Player;
            if (Interface.Oxide.CallHook("OnDiscordChatMessage", player, message.Content, message.Author) != null)
            {
                return;
            }
            
            HandleMessage(message.Content, player, message.Author, MessageSource.Discord, message);
            
            if (_pluginConfig.ChatSettings.UseBotToDisplayChat)
            {
                message.Delete(Client);
            }
        }
        #endregion

        #region Plugins\DiscordChat.Fields.cs
        [PluginReference]
        private Plugin BetterChat;
        
        public DiscordClient Client { get; set; }
        public DiscordPluginPool Pool { get; set; }
        
        private PluginConfig _pluginConfig;
        
        private readonly DiscordSubscriptions _subscriptions = GetLibrary<DiscordSubscriptions>();
        private readonly DiscordPlaceholders _placeholders = GetLibrary<DiscordPlaceholders>();
        private readonly DiscordMessageTemplates _templates = GetLibrary<DiscordMessageTemplates>();
        
        private bool _serverInitCalled;
        
        public readonly Hash<MessageSource, DiscordSendQueue> Sends = new();
        private readonly List<IPluginHandler> _plugins = new();
        
        public static DiscordChat Instance;
        
        private readonly object _true = true;
        #endregion

        #region Plugins\DiscordChat.Helpers.cs
        public MessageSource GetSourceFromServerChannel(int channel)
        {
            switch (channel)
            {
                case 1:
                return MessageSource.Team;
                case 3:
                return MessageSource.Cards;
                case 5:
                return MessageSource.Clan;
            }
            
            return MessageSource.Server;
        }
        
        public DiscordChannel FindChannel(Snowflake channelId)
        {
            if (!channelId.IsValid())
            {
                return null;
            }
            
            foreach (DiscordGuild guild in Client.Bot.Servers.Values)
            {
                DiscordChannel channel = guild.Channels[channelId];
                if (channel != null)
                {
                    return channel;
                }
            }
            
            return null;
        }
        
        public bool IsPluginLoaded(Plugin plugin) => plugin != null && plugin.IsLoaded;
        
        public new void Subscribe(string hook)
        {
            base.Subscribe(hook);
        }
        
        public new void Unsubscribe(string hook)
        {
            base.Unsubscribe(hook);
        }
        
        public void Puts(string message)
        {
            base.Puts(message);
        }
        #endregion

        #region Plugins\DiscordChat.Hooks.cs
        private void OnUserApproved(string name, string id, string ip)
        {
            IPlayer player = players.FindPlayerById(id) ?? PlayerExt.CreateDummyPlayer(id, name, ip);
            if (_pluginConfig.PlayerStateSettings.ShowAdmins || !player.IsAdmin)
            {
                PlaceholderData placeholders = GetDefault().AddPlayer(player).AddIp(ip);
                ProcessPlayerState(MessageSource.Connecting, LangKeys.Discord.Player.Connecting, placeholders);
            }
        }
        
        private void OnUserConnected(IPlayer player)
        {
            if (_pluginConfig.PlayerStateSettings.ShowAdmins || !player.IsAdmin)
            {
                PlaceholderData placeholders = GetDefault().AddPlayer(player);
                ProcessPlayerState(MessageSource.Connected, LangKeys.Discord.Player.Connected, placeholders);
            }
        }
        
        private void OnUserDisconnected(IPlayer player, string reason)
        {
            if (_pluginConfig.PlayerStateSettings.ShowAdmins || !player.IsAdmin)
            {
                PlaceholderData placeholders = GetDefault().AddPlayer(player).Add(PlaceholderDataKeys.DisconnectReason, reason);
                ProcessPlayerState(MessageSource.Disconnected, LangKeys.Discord.Player.Disconnected, placeholders);
            }
        }
        
        public void ProcessPlayerState(MessageSource source, string langKey, PlaceholderData data)
        {
            string message = Lang(langKey, data);
            Sends[source]?.QueueMessage(message);
        }
        
        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == null)
            {
                return;
            }
            
            OnPluginUnloaded(plugin);
            
            switch (plugin.Name)
            {
                case "AdminChat":
                AddHandler(new AdminChatHandler(Client, this, _pluginConfig.PluginSupport.AdminChat, plugin));
                break;
                
                case "AdminDeepCover":
                AddHandler(new AdminDeepCoverHandler(this, plugin));
                break;
                
                case "AntiSpam":
                if (plugin.Version < new VersionNumber(2, 0, 0))
                {
                    PrintError("AntiSpam plugin must be version 2.0.0 or higher");
                    break;
                }
                
                AddHandler(new AntiSpamHandler(this, _pluginConfig.PluginSupport.AntiSpam, plugin));
                break;
                
                case "BetterChatMute":
                BetterChatMuteSettings muteSettings = _pluginConfig.PluginSupport.BetterChatMute;
                if (muteSettings.IgnoreMuted)
                {
                    AddHandler(new BetterChatMuteHandler(this, muteSettings, plugin));
                }
                break;
                
                case "TranslationAPI":
                AddHandler(new TranslationApiHandler(this, _pluginConfig.PluginSupport.ChatTranslator, plugin));
                break;
                
                case "UFilter":
                AddHandler(new UFilterHandler(this, _pluginConfig.PluginSupport.UFilter, plugin));
                break;
            }
        }
        
        public void AddHandler(IPluginHandler handler)
        {
            _plugins.Add(handler);
        }
        
        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin.Name != Name)
            {
                _plugins.RemoveAll(h => h.GetPluginName() == plugin.Name);
            }
        }
        
        #if RUST
        private void OnPlayerChat(BasePlayer rustPlayer, string message, Chat.ChatChannel chatChannel)
        {
            HandleChat(rustPlayer.IPlayer, message, (int)chatChannel);
        }
        #else
        private void OnUserChat(IPlayer player, string message)
        {
            HandleChat(player, message, 0);
        }
        #endif
        
        public void HandleChat(IPlayer player, string message, int channel)
        {
            DiscordUser user = player.GetDiscordUser();
            MessageSource source = GetSourceFromServerChannel(channel);
            
            if (Sends.ContainsKey(source))
            {
                HandleMessage(message, player, user, source, null);
            }
        }
        #endregion

        #region Plugins\DiscordChat.Lang.cs
        public string Lang(string key, PlaceholderData data)
        {
            string message = lang.GetMessage(key, this);
            message = _placeholders.ProcessPlaceholders(message, data);
            return message;
        }
        
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangKeys.Discord.Player.Connecting] = $":yellow_circle: {DefaultKeys.TimestampNow.ShortTime} {DefaultKeys.Ip.CountryEmoji} **{PlaceholderKeys.PlayerName}** is connecting",
                [LangKeys.Discord.Player.Connected] = $":white_check_mark: {DefaultKeys.TimestampNow.ShortTime} {DefaultKeys.Player.CountryEmoji} **{PlaceholderKeys.PlayerName}** has joined.",
                [LangKeys.Discord.Player.Disconnected] = $":x: {DefaultKeys.TimestampNow.ShortTime} **{PlaceholderKeys.PlayerName}** has disconnected. ({PlaceholderKeys.DisconnectReason})",
                [LangKeys.Discord.Chat.Server] = $":desktop: {DefaultKeys.TimestampNow.ShortTime} **{PlaceholderKeys.PlayerName}**: {PlaceholderKeys.PlayerMessage}",
                [LangKeys.Discord.Chat.LinkedMessage] = $":speech_left: {DefaultKeys.TimestampNow.ShortTime} **{PlaceholderKeys.PlayerName}**: {PlaceholderKeys.PlayerMessage}",
                [LangKeys.Discord.Chat.UnlinkedMessage] = $":chains: {DefaultKeys.TimestampNow.ShortTime} {DefaultKeys.User.Mention}: {PlaceholderKeys.PlayerMessage}",
                [LangKeys.Discord.Chat.PlayerName] = $"{DefaultKeys.Player.NameClan}",
                [LangKeys.Discord.Team.Message] = $":busts_in_silhouette: {DefaultKeys.TimestampNow.ShortTime} **{PlaceholderKeys.PlayerName}**: {PlaceholderKeys.PlayerMessage}",
                [LangKeys.Discord.Cards.Message] = $":black_joker: {DefaultKeys.TimestampNow.ShortTime} **{PlaceholderKeys.PlayerName}**: {PlaceholderKeys.PlayerMessage}",
                [LangKeys.Discord.Clans.Message] = $":shield: {DefaultKeys.TimestampNow.ShortTime} **{PlaceholderKeys.PlayerName}**: {PlaceholderKeys.PlayerMessage}",
                [LangKeys.Discord.AdminChat.ServerMessage] = $":mechanic: {DefaultKeys.TimestampNow.ShortTime} **{PlaceholderKeys.PlayerName}**: {PlaceholderKeys.PlayerMessage}",
                [LangKeys.Discord.AdminChat.DiscordMessage] = $":mechanic: {DefaultKeys.TimestampNow.ShortTime} **{PlaceholderKeys.PlayerName}**: {PlaceholderKeys.PlayerMessage}",
                [LangKeys.Discord.PluginClans.ClanMessage] = $"{DefaultKeys.TimestampNow.ShortTime} [Clan] **{PlaceholderKeys.PlayerName}**: {PlaceholderKeys.PlayerMessage}",
                [LangKeys.Discord.PluginClans.AllianceMessage] = $"{DefaultKeys.TimestampNow.ShortTime} [Alliance] **{PlaceholderKeys.PlayerName}**: {PlaceholderKeys.PlayerMessage}",
                [LangKeys.Server.UnlinkedMessage] = $"{PlaceholderKeys.DiscordTag} [#5f79d6]{DefaultKeys.Member.Name}[/#]: {PlaceholderKeys.PlayerMessage}",
                [LangKeys.Server.LinkedMessage] = $"{PlaceholderKeys.DiscordTag} [#5f79d6]{PlaceholderKeys.PlayerName}[/#]: {PlaceholderKeys.PlayerMessage}"
            }, this);
        }
        #endregion

        #region Plugins\DiscordChat.MessageHandling.cs
        private readonly Regex _channelMention = new(@"(<#\d+>)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        public void HandleMessage(string content, IPlayer player, DiscordUser user, MessageSource source, DiscordMessage sourceMessage)
        {
            if (!CanSendMessage(content, player, user, source, sourceMessage))
            {
                return;
            }
            
            #if RUST
            content = EmojiCache.Instance.ReplaceEmojiWithText(content);
            #endif
            
            ProcessCallbackMessages(content, player, user, source, processedMessage =>
            {
                StringBuilder sb = Pool.GetStringBuilder(processedMessage);
                
                if (sourceMessage != null)
                {
                    ProcessMentions(sourceMessage, sb);
                }
                
                ProcessMessage(sb, player, user, source);
                SendMessage(Pool.ToStringAndFree(sb), player, user, source, sourceMessage);
            });
        }
        
        public void ProcessMentions(DiscordMessage message, StringBuilder sb)
        {
            DiscordGuild guild = Client.Bot.GetGuild(message.GuildId);
            if (message.Mentions != null)
            {
                foreach (KeyValuePair<Snowflake, DiscordUser> mention in message.Mentions)
                {
                    GuildMember member = guild.Members[mention.Key];
                    sb.Replace($"<@{mention.Key.ToString()}>", $"@{member?.DisplayName ?? mention.Value.DisplayName}");
                }
                
                foreach (KeyValuePair<Snowflake, DiscordUser> mention in message.Mentions)
                {
                    GuildMember member = guild.Members[mention.Key];
                    sb.Replace($"<@!{mention.Key.ToString()}>", $"@{member?.DisplayName ?? mention.Value.DisplayName}");
                }
            }
            
            if (message.MentionsChannels != null)
            {
                foreach (KeyValuePair<Snowflake, ChannelMention> mention in message.MentionsChannels)
                {
                    sb.Replace($"<#{mention.Key.ToString()}>", $"#{mention.Value.Name}");
                }
            }
            
            foreach (Match match in _channelMention.Matches(message.Content))
            {
                string value = match.Value;
                Snowflake id = new(new System.ReadOnlySpan<char>(value.ToCharArray()).Slice(2, value.Length - 3));
                DiscordChannel channel = guild.Channels[id];
                if (channel != null)
                {
                    sb.Replace(value, $"#{channel.Name}");
                }
            }
            
            if (message.MentionRoles != null)
            {
                foreach (Snowflake roleId in message.MentionRoles)
                {
                    DiscordRole role = guild.Roles[roleId];
                    sb.Replace($"<@&{roleId.ToString()}>", $"@{role.Name ?? roleId}");
                }
            }
        }
        
        public bool CanSendMessage(string message, IPlayer player, DiscordUser user, MessageSource source, DiscordMessage sourceMessage)
        {
            for (int index = 0; index < _plugins.Count; index++)
            {
                if (!_plugins[index].CanSendMessage(message, player, user, source, sourceMessage))
                {
                    return false;
                }
            }
            
            return true;
        }
        
        public void ProcessCallbackMessages(string message, IPlayer player, DiscordUser user, MessageSource source, Action<string> completed, int index = 0)
        {
            for (; index < _plugins.Count; index++)
            {
                IPluginHandler handler = _plugins[index];
                if (handler.HasCallbackMessage())
                {
                    handler.ProcessCallbackMessage(message, player, user, source, callbackMessage =>
                    {
                        ProcessCallbackMessages(callbackMessage, player, user, source, completed, index + 1);
                    });
                    return;
                }
            }
            
            completed.Invoke(message);
        }
        
        public void ProcessMessage(StringBuilder message, IPlayer player, DiscordUser user, MessageSource source)
        {
            for (int index = 0; index < _plugins.Count; index++)
            {
                _plugins[index].ProcessMessage(message, player, user, source);
            }
        }
        
        public void SendMessage(string message, IPlayer player, DiscordUser user, MessageSource source, DiscordMessage sourceMessage)
        {
            using PlaceholderData data = GetPlaceholders(message, player, user, sourceMessage);
            data.ManualPool();
            for (int index = 0; index < _plugins.Count; index++)
            {
                IPluginHandler plugin = _plugins[index];
                if (plugin.SendMessage(message, player, user, source, sourceMessage, data))
                {
                    return;
                }
            }
        }
        
        private PlaceholderData GetPlaceholders(string message, IPlayer player, DiscordUser user, DiscordMessage sourceMessage)
        {
            PlaceholderData placeholders = GetDefault().AddPlayer(player).AddUser(user).AddMessage(sourceMessage).Add(PlaceholderDataKeys.PlayerMessage, message);
            if (sourceMessage != null)
            {
                placeholders.AddGuildMember(Client.Bot.GetGuild(sourceMessage.GuildId)?.Members[sourceMessage.Author.Id]);
            }
            
            return placeholders;
        }
        #endregion

        #region Plugins\DiscordChat.Placeholders.cs
        public void RegisterPlaceholders()
        {
            _placeholders.RegisterPlaceholder<string>(this, PlaceholderKeys.TemplateMessage, PlaceholderDataKeys.TemplateMessage);
            _placeholders.RegisterPlaceholder<string>(this, PlaceholderKeys.PlayerMessage, PlaceholderDataKeys.PlayerMessage);
            _placeholders.RegisterPlaceholder<string>(this, PlaceholderKeys.DisconnectReason, PlaceholderDataKeys.DisconnectReason);
            _placeholders.RegisterPlaceholder<IPlayer, string>(this, PlaceholderKeys.PlayerName, GetPlayerName);
            _placeholders.RegisterPlaceholder(this, PlaceholderKeys.DiscordTag, _pluginConfig.ChatSettings.DiscordTag);
        }
        
        public string GetPlayerName(IPlayer player)
        {
            string name = Lang(LangKeys.Discord.Chat.PlayerName, GetDefault().AddPlayer(player));
            StringBuilder sb = Pool.GetStringBuilder(name);
            for (int index = 0; index < _plugins.Count; index++)
            {
                _plugins[index].ProcessPlayerName(sb, player);
            }
            
            return Pool.ToStringAndFree(sb);
        }
        
        public PlaceholderData GetDefault()
        {
            return _placeholders.CreateData(this);
        }
        #endregion

        #region Plugins\DiscordChat.Setup.cs
        private void Init()
        {
            Instance = this;
            
            _adminChatSettings = _pluginConfig.PluginSupport.AdminChat;
            
            #if RUST
            Unsubscribe(nameof(OnPlayerChat));
            #else
            Unsubscribe(nameof(OnUserChat));
            #endif
            
            Unsubscribe(nameof(OnUserApproved));
            Unsubscribe(nameof(OnUserConnected));
            Unsubscribe(nameof(OnUserDisconnected));
            Unsubscribe(nameof(OnServerShutdown));
            Unsubscribe(nameof(OnClanChat));
            Unsubscribe(nameof(OnAllianceChat));
            
            _plugins.Add(new DiscordChatHandler(this, _pluginConfig.ChatSettings, this, server));
        }
        
        protected override void LoadDefaultConfig()
        {
            PrintWarning("Loading Default Config");
        }
        
        protected override void LoadConfig()
        {
            base.LoadConfig();
            _pluginConfig = AdditionalConfig(Config.ReadObject<PluginConfig>());
            Config.WriteObject(_pluginConfig);
        }
        
        private PluginConfig AdditionalConfig(PluginConfig config)
        {
            config.ChatSettings = new ChatSettings(config.ChatSettings);
            config.PlayerStateSettings = new PlayerStateSettings(config.PlayerStateSettings);
            config.ServerStateSettings = new ServerStateSettings(config.ServerStateSettings);
            config.PluginSupport = new PluginSupport(config.PluginSupport);
            return config;
        }
        
        private void OnServerInitialized(bool startup)
        {
            _serverInitCalled = true;
            if (IsPluginLoaded(BetterChat))
            {
                if (BetterChat.Version < new VersionNumber(5, 2, 7))
                {
                    PrintWarning("Please update your version of BetterChat to version >= 5.2.7");
                }
            }
            
            if (string.IsNullOrEmpty(_pluginConfig.DiscordApiKey))
            {
                PrintWarning("Please set the Discord Bot Token and reload the plugin");
                return;
            }
            
            OnPluginLoaded(plugins.Find("AdminChat"));
            OnPluginLoaded(plugins.Find("AdminDeepCover"));
            OnPluginLoaded(plugins.Find("AntiSpam"));
            OnPluginLoaded(plugins.Find("BetterChatMute"));
            OnPluginLoaded(plugins.Find("TranslationAPI"));
            OnPluginLoaded(plugins.Find("UFilter"));
            
            if (startup && _pluginConfig.ServerStateSettings.SendOnlineMessage)
            {
                SendGlobalTemplateMessage(TemplateKeys.Server.Online, FindChannel(_pluginConfig.ServerStateSettings.ServerStateChannel));
            }
        }
        
        private void OnServerShutdown()
        {
            if(_pluginConfig.ServerStateSettings.SendShutdownMessage)
            {
                SendGlobalTemplateMessage(TemplateKeys.Server.Shutdown, FindChannel(_pluginConfig.ServerStateSettings.ServerStateChannel));
            }
        }
        
        private void Unload()
        {
            Instance = null;
        }
        #endregion

        #region Plugins\DiscordChat.Templates.cs
        public void RegisterTemplates()
        {
            DiscordMessageTemplate connecting = CreateTemplateEmbed($"{PlaceholderKeys.TemplateMessage}",  DiscordColor.Warning);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Player.Connecting, connecting, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate connected = CreateTemplateEmbed($"{PlaceholderKeys.TemplateMessage}",  DiscordColor.Success);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Player.Connected, connected, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate disconnected = CreateTemplateEmbed($"{PlaceholderKeys.TemplateMessage}",  DiscordColor.Danger);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Player.Disconnected, disconnected, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate online = CreateTemplateEmbed($":green_circle: {DefaultKeys.TimestampNow.ShortTime} The server is now online", DiscordColor.Success);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Server.Online, online, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate shutdown = CreateTemplateEmbed($":red_circle: {DefaultKeys.TimestampNow.ShortTime} The server has shutdown", DiscordColor.Danger);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Server.Shutdown, shutdown, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate booting = CreateTemplateEmbed($":yellow_circle: {DefaultKeys.TimestampNow.ShortTime} The server is now booting", DiscordColor.Warning);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Server.Booting, booting, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate serverChat = CreateTemplateEmbed($"{PlaceholderKeys.TemplateMessage}", DiscordColor.Blurple);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Chat.General, serverChat, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate teamChat = CreateTemplateEmbed($"{PlaceholderKeys.TemplateMessage}", DiscordColor.Success);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Chat.Teams, teamChat, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate clanChat = CreateTemplateEmbed($"{PlaceholderKeys.TemplateMessage}", DiscordColor.Success);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Chat.Clan, clanChat, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate cardsChat = CreateTemplateEmbed($"{PlaceholderKeys.TemplateMessage}", DiscordColor.Danger);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Chat.Cards, cardsChat, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate pluginClanChat = CreateTemplateEmbed($"{PlaceholderKeys.TemplateMessage}", new DiscordColor("a1ff46"));
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Chat.Clans.Clan, pluginClanChat, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate pluginAllianceChat = CreateTemplateEmbed($"{PlaceholderKeys.TemplateMessage}",  new DiscordColor("80cc38"));
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Chat.Clans.Alliance, pluginAllianceChat, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate errorNotLinked = CreatePrefixedTemplateEmbed("You're not allowed to chat with the server unless you are linked.", DiscordColor.Danger);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Error.NotLinked, errorNotLinked, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate errorAdminChatNotLinked = CreatePrefixedTemplateEmbed("You're not allowed to use admin chat because you have not linked your Discord and game server accounts", DiscordColor.Danger);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Error.AdminChat.NotLinked, errorAdminChatNotLinked, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate errorAdminChatNotPermission = CreatePrefixedTemplateEmbed(":no_entry: You're not allowed to use admin chat channel because you do not have permission.", DiscordColor.Danger);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Error.AdminChat.NoPermission, errorAdminChatNotPermission, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
        }
        
        public DiscordMessageTemplate CreateTemplateEmbed(string description, DiscordColor color)
        {
            return new DiscordMessageTemplate
            {
                Embeds = new List<DiscordEmbedTemplate>
                {
                    new()
                    {
                        Description = description,
                        Color = color.ToHex()
                    }
                }
            };
        }
        
        public DiscordMessageTemplate CreatePrefixedTemplateEmbed(string description, DiscordColor color)
        {
            return new DiscordMessageTemplate
            {
                Embeds = new List<DiscordEmbedTemplate>
                {
                    new()
                    {
                        Description = $"[{DefaultKeys.Plugin.Name}] {description}",
                        Color = color.ToHex()
                    }
                }
            };
        }
        
        public void SendGlobalTemplateMessage(TemplateKey templateName, DiscordChannel channel, PlaceholderData placeholders = null)
        {
            if (channel == null)
            {
                return;
            }
            
            MessageCreate create = new()
            {
                AllowedMentions = AllowedMentions.None
            };
            channel.CreateGlobalTemplateMessage(Client, templateName, create, placeholders);
        }
        
        public TemplateKey GetTemplateName(MessageSource source)
        {
            switch (source)
            {
                case MessageSource.Discord:
                case MessageSource.Server:
                return TemplateKeys.Chat.General;
                case MessageSource.Team:
                return TemplateKeys.Chat.Teams;
                case MessageSource.Cards:
                return TemplateKeys.Chat.Cards;
                case MessageSource.Clan:
                return TemplateKeys.Chat.Clan;
                case MessageSource.Connecting:
                return TemplateKeys.Player.Connecting;
                case MessageSource.Connected:
                return TemplateKeys.Player.Connected;
                case MessageSource.Disconnected:
                return TemplateKeys.Player.Disconnected;
                case MessageSource.PluginAdminChat:
                return TemplateKeys.Chat.AdminChat.Message;
                case MessageSource.PluginClan:
                return TemplateKeys.Chat.Clans.Clan;
                case MessageSource.PluginAlliance:
                return TemplateKeys.Chat.Clans.Alliance;
            }
            
            return default;
        }
        #endregion

        #region Configuration\ChatSettings.cs
        public class ChatSettings
        {
            [JsonProperty("Chat Channel ID")]
            public Snowflake ChatChannel { get; set; }
            
            #if RUST
            [JsonProperty("Team Channel ID")]
            public Snowflake TeamChannel { get; set; }
            
            [JsonProperty("Cards Channel ID")]
            public Snowflake CardsChannel { get; set; }
            
            [JsonProperty("Clans Channel ID")]
            public Snowflake ClansChannel { get; set; }
            #endif
            
            [JsonProperty("Replace Discord User Message With Bot Message")]
            public bool UseBotToDisplayChat { get; set; }
            
            [JsonProperty("Send Messages From Server Chat To Discord Channel")]
            public bool ServerToDiscord { get; set; }
            
            [JsonProperty("Send Messages From Discord Channel To Server Chat")]
            public bool DiscordToServer { get; set; }
            
            [JsonProperty("Add Discord Tag To In Game Messages When Sent From Discord")]
            public string DiscordTag { get; set; }
            
            [JsonProperty("Allow plugins to process Discord to Server Chat Messages")]
            public bool AllowPluginProcessing { get; set; }
            
            [JsonProperty("Text Replacements")]
            public Hash<string, string> TextReplacements { get; set; }
            
            [JsonProperty("Unlinked Settings")]
            public UnlinkedSettings UnlinkedSettings { get; set; }
            
            [JsonProperty("Message Filter Settings")]
            public MessageFilterSettings Filter { get; set; }
            
            public ChatSettings(ChatSettings settings)
            {
                ChatChannel = settings?.ChatChannel ?? default(Snowflake);
                #if RUST
                TeamChannel = settings?.TeamChannel ?? default(Snowflake);
                CardsChannel = settings?.CardsChannel ?? default(Snowflake);
                #endif
                UseBotToDisplayChat = settings?.UseBotToDisplayChat ?? true;
                ServerToDiscord = settings?.ServerToDiscord ?? true;
                DiscordToServer = settings?.DiscordToServer ?? true;
                DiscordTag = settings?.DiscordTag ?? "[#5f79d6][Discord][/#]";
                AllowPluginProcessing = settings?.AllowPluginProcessing ?? true;
                TextReplacements = settings?.TextReplacements ?? new Hash<string, string> { ["TextToBeReplaced"] = "ReplacedText" };
                UnlinkedSettings = new UnlinkedSettings(settings?.UnlinkedSettings);
                Filter = new MessageFilterSettings(settings?.Filter);
            }
        }
        #endregion

        #region Configuration\MessageFilterSettings.cs
        public class MessageFilterSettings
        {
            [JsonProperty("Ignore messages from users in this list (Discord ID)")]
            public List<Snowflake> IgnoreUsers { get; set; }
            
            [JsonProperty("Ignore messages from users in this role (Role ID)")]
            public List<Snowflake> IgnoreRoles { get; set; }
            
            [JsonProperty("Ignored Prefixes")]
            public List<string> IgnoredPrefixes { get; set; }
            
            public MessageFilterSettings(MessageFilterSettings settings)
            {
                IgnoreUsers = settings?.IgnoreUsers ?? new List<Snowflake>();
                IgnoreRoles = settings?.IgnoreRoles ?? new List<Snowflake>();
                IgnoredPrefixes = settings?.IgnoredPrefixes ?? new List<string>();
            }
            
            public bool IgnoreMessage(DiscordMessage message, GuildMember member)
            {
                return IsIgnoredUser(message.Author, member) || IsIgnoredPrefix(message.Content);
            }
            
            public bool IsIgnoredUser(DiscordUser user, GuildMember member)
            {
                if (user.IsBot)
                {
                    return true;
                }
                
                if (IgnoreUsers.Contains(user.Id))
                {
                    return true;
                }
                
                return member != null && IsRoleIgnoredMember(member);
            }
            
            public bool IsRoleIgnoredMember(GuildMember member)
            {
                for (int index = 0; index < IgnoreRoles.Count; index++)
                {
                    Snowflake role = IgnoreRoles[index];
                    if (member.Roles.Contains(role))
                    {
                        return true;
                    }
                }
                
                return false;
            }
            
            public bool IsIgnoredPrefix(string content)
            {
                for (int index = 0; index < IgnoredPrefixes.Count; index++)
                {
                    string prefix = IgnoredPrefixes[index];
                    if (content.StartsWith(prefix))
                    {
                        return true;
                    }
                }
                
                return false;
            }
        }
        #endregion

        #region Configuration\PlayerStateSettings.cs
        public class PlayerStateSettings
        {
            [JsonProperty("Player State Channel ID")]
            public Snowflake PlayerStateChannel { get; set; }
            
            [JsonProperty("Show Admins")]
            public bool ShowAdmins { get; set; }
            
            [JsonProperty("Send Connecting Message")]
            public bool SendConnectingMessage { get; set; }
            
            [JsonProperty("Send Connected Message")]
            public bool SendConnectedMessage { get; set; }
            
            [JsonProperty("Send Disconnected Message")]
            public bool SendDisconnectedMessage { get; set; }
            
            public PlayerStateSettings(PlayerStateSettings settings)
            {
                PlayerStateChannel = settings?.PlayerStateChannel ?? default(Snowflake);
                ShowAdmins = settings?.ShowAdmins ?? true;
                SendConnectingMessage = settings?.SendConnectingMessage ?? true;
                SendConnectedMessage = settings?.SendConnectedMessage ?? true;
                SendDisconnectedMessage = settings?.SendDisconnectedMessage ?? true;
            }
        }
        #endregion

        #region Configuration\PluginConfig.cs
        public class PluginConfig
        {
            [JsonProperty(PropertyName = "Discord Bot Token")]
            public string DiscordApiKey { get; set; } = string.Empty;
            
            [JsonProperty("Chat Settings")]
            public ChatSettings ChatSettings { get; set; }
            
            [JsonProperty("Player State Settings")]
            public PlayerStateSettings PlayerStateSettings { get; set; }
            
            [JsonProperty("Server State Settings")]
            public ServerStateSettings ServerStateSettings { get; set; }
            
            [JsonProperty("Plugin Support")]
            public PluginSupport PluginSupport { get; set; }
            
            [JsonConverter(typeof(StringEnumConverter))]
            [DefaultValue(DiscordLogLevel.Info)]
            [JsonProperty(PropertyName = "Discord Extension Log Level (Verbose, Debug, Info, Warning, Error, Exception, Off)")]
            public DiscordLogLevel ExtensionDebugging { get; set; } = DiscordLogLevel.Info;
        }
        #endregion

        #region Configuration\ServerStateSettings.cs
        public class ServerStateSettings
        {
            [JsonProperty("Server State Channel ID")]
            public Snowflake ServerStateChannel { get; set; }
            
            [JsonProperty("Send Booting Message")]
            public bool SendBootingMessage { get; set; }
            
            [JsonProperty("Send Online Message")]
            public bool SendOnlineMessage { get; set; }
            
            [JsonProperty("Send Shutdown Message")]
            public bool SendShutdownMessage { get; set; }
            
            public ServerStateSettings(ServerStateSettings settings)
            {
                ServerStateChannel = settings?.ServerStateChannel ?? default(Snowflake);
                SendBootingMessage = settings?.SendBootingMessage ?? true;
                SendOnlineMessage = settings?.SendOnlineMessage ?? true;
                SendShutdownMessage = settings?.SendShutdownMessage ?? true;
            }
        }
        #endregion

        #region Configuration\UnlinkedSettings.cs
        public class UnlinkedSettings
        {
            [JsonProperty("Allow Unlinked Players To Chat With Server")]
            public bool AllowedUnlinked { get; set; }
            
            #if RUST
            [JsonProperty("Steam Icon ID")]
            public ulong SteamIcon { get; set; }
            #endif
            
            public UnlinkedSettings(UnlinkedSettings settings)
            {
                AllowedUnlinked = settings?.AllowedUnlinked ?? true;
                #if RUST
                SteamIcon = settings?.SteamIcon ?? 76561199144296099;
                #endif
            }
        }
        #endregion

        #region Enums\MessageSource.cs
        public enum MessageSource : byte
        {
            Connecting,
            Connected,
            Disconnected,
            Server,
            Discord,
            Team,
            Cards,
            Clan,
            PluginClan,
            PluginAlliance,
            PluginAdminChat
        }
        #endregion

        #region Helpers\DiscordSendQueue.cs
        public class DiscordSendQueue
        {
            private readonly StringBuilder _message = new();
            private Timer _sendTimer;
            private readonly DiscordChannel _channel;
            private readonly TemplateKey _templateId;
            private readonly Action _callback;
            private readonly PluginTimers _timer;
            
            public DiscordSendQueue(DiscordChannel channel, TemplateKey templateId, PluginTimers timers)
            {
                _channel = channel;
                _templateId = templateId;
                _callback = Send;
                _timer = timers;
            }
            
            public void QueueMessage(string message)
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    return;
                }
                
                if (_message.Length + message.Length > 2000)
                {
                    Send();
                }
                
                _sendTimer ??= _timer.In(1f, _callback);
                
                _message.AppendLine(message);
            }
            
            public void Send()
            {
                if (_message.Length > 2000)
                {
                    _message.Length = 2000;
                }
                
                PlaceholderData placeholders = DiscordChat.Instance.GetDefault().Add(PlaceholderDataKeys.TemplateMessage, _message.ToString());
                _message.Length = 0;
                DiscordChat.Instance.SendGlobalTemplateMessage(_templateId, _channel, placeholders);
                _sendTimer?.Destroy();
                _sendTimer = null;
            }
        }
        #endregion

        #region Localization\LangKeys.cs
        public static class LangKeys
        {
            public const string Root = "V3.";
            
            public static class Discord
            {
                private const string Base = Root + nameof(Discord) + ".";
                
                public static class Chat
                {
                    private const string Base = Discord.Base + nameof(Chat) + ".";
                    
                    public const string Server = Base + nameof(Server);
                    public const string LinkedMessage = Base + nameof(LinkedMessage);
                    public const string UnlinkedMessage = Base + nameof(UnlinkedMessage);
                    public const string PlayerName = Base + nameof(PlayerName);
                }
                
                public static class Team
                {
                    private const string Base = Discord.Base + nameof(Team) + ".";
                    
                    public const string Message = Base + nameof(Message);
                }
                
                public static class Cards
                {
                    private const string Base = Discord.Base + nameof(Cards) + ".";
                    
                    public const string Message = Base + nameof(Message);
                }
                
                public static class Clans
                {
                    private const string Base = Discord.Base + nameof(Clans) + ".";
                    
                    public const string Message = Base + nameof(Message);
                }
                
                public static class Player
                {
                    private const string Base = Discord.Base + nameof(Player) + ".";
                    
                    public const string Connecting = Base + nameof(Connecting);
                    public const string Connected = Base + nameof(Connected);
                    public const string Disconnected = Base + nameof(Disconnected);
                }
                
                public static class AdminChat
                {
                    private const string Base = Discord.Base + nameof(AdminChat) + ".";
                    
                    public const string ServerMessage = Base + nameof(ServerMessage);
                    public const string DiscordMessage = Base + nameof(DiscordMessage);
                }
                
                public static class PluginClans
                {
                    private const string Base = Discord.Base + nameof(PluginClans) + ".";
                    
                    public const string ClanMessage = Base + nameof(ClanMessage);
                    public const string AllianceMessage = Base + nameof(AllianceMessage);
                }
            }
            
            public static class Server
            {
                private const string Base = Root + nameof(Server) + ".";
                
                public const string LinkedMessage = Base + nameof(LinkedMessage);
                public const string UnlinkedMessage = Base + nameof(UnlinkedMessage);
            }
        }
        #endregion

        #region Placeholders\PlaceholderDataKeys.cs
        public class PlaceholderDataKeys
        {
            public static readonly PlaceholderDataKey TemplateMessage = new("message");
            public static readonly PlaceholderDataKey PlayerMessage = new("player.message");
            public static readonly PlaceholderDataKey DisconnectReason = new("reason");
        }
        #endregion

        #region Placeholders\PlaceholderKeys.cs
        public class PlaceholderKeys
        {
            public static readonly PlaceholderKey TemplateMessage = new(nameof(DiscordChat), "message");
            public static readonly PlaceholderKey PlayerMessage = new(nameof(DiscordChat), "player.message");
            public static readonly PlaceholderKey DisconnectReason = new(nameof(DiscordChat), "disconnect.reason");
            public static readonly PlaceholderKey PlayerName = new(nameof(DiscordChat), "player.name");
            public static readonly PlaceholderKey DiscordTag = new(nameof(DiscordChat), "discord.tag");
        }
        #endregion

        #region PluginHandlers\AdminChatHandler.cs
        public class AdminChatHandler : BasePluginHandler
        {
            private readonly AdminChatSettings _settings;
            private readonly DiscordClient _client;
            
            public AdminChatHandler(DiscordClient client, DiscordChat chat, AdminChatSettings settings, Plugin plugin) : base(chat, plugin)
            {
                _client = client;
                _settings = settings;
            }
            
            public override bool CanSendMessage(string message, IPlayer player, DiscordUser user, MessageSource source, DiscordMessage sourceMessage)
            {
                return source == MessageSource.PluginAdminChat ? !_settings.Enabled : !IsAdminChatMessage(player, message);
            }
            
            public override bool SendMessage(string message, IPlayer player, DiscordUser user, MessageSource source, DiscordMessage sourceMessage, PlaceholderData data)
            {
                if (source != MessageSource.PluginAdminChat)
                {
                    return false;
                }
                
                if (sourceMessage != null)
                {
                    if (_settings.ReplaceWithBot)
                    {
                        sourceMessage.Delete(_client);
                        Chat.Sends[source]?.QueueMessage(Chat.Lang(LangKeys.Discord.AdminChat.DiscordMessage, data));
                    }
                    
                    Plugin.Call("SendAdminMessage", player, message);
                }
                else
                {
                    Chat.Sends[source]?.QueueMessage(Chat.Lang(LangKeys.Discord.AdminChat.ServerMessage, data));
                }
                
                return true;
            }
            
            private bool IsAdminChatMessage(IPlayer player, string message) => Chat.CanPlayerAdminChat(player) && (message.StartsWith(_settings.AdminChatPrefix) || Plugin.Call<bool>("HasAdminChatEnabled", player));
        }
        #endregion

        #region PluginHandlers\AdminDeepCoverHandler.cs
        public class AdminDeepCoverHandler : BasePluginHandler
        {
            public AdminDeepCoverHandler(DiscordChat chat, Plugin plugin) : base(chat, plugin) { }
            
            public override bool CanSendMessage(string message, IPlayer player, DiscordUser user, MessageSource type, DiscordMessage sourceMessage)
            {
                return player?.Object != null
                && (type == MessageSource.Discord || type == MessageSource.Server)
                && !Plugin.Call<bool>("API_IsDeepCovered", player.Object);
            }
        }
        #endregion

        #region PluginHandlers\AntiSpamHandler.cs
        public class AntiSpamHandler : BasePluginHandler
        {
            private readonly AntiSpamSettings _settings;
            
            public AntiSpamHandler(DiscordChat chat, AntiSpamSettings settings, Plugin plugin) : base(chat, plugin)
            {
                _settings = settings;
            }
            
            public override void ProcessPlayerName(StringBuilder name, IPlayer player)
            {
                if (!_settings.PlayerName || player == null)
                {
                    return;
                }
                
                string builtName = name.ToString();
                builtName = Plugin.Call<string>("GetSpamFreeText", builtName);
                builtName = Plugin.Call<string>("GetImpersonationFreeText", builtName);
                name.Length = 0;
                name.Append(builtName);
            }
            
            public override void ProcessMessage(StringBuilder message, IPlayer player, DiscordUser user, MessageSource source)
            {
                if (CanFilterMessage(source))
                {
                    string clearMessage = Plugin.Call<string>("GetSpamFreeText", message.ToString());
                    message.Length = 0;
                    message.Append(clearMessage);
                }
            }
            
            private bool CanFilterMessage(MessageSource source)
            {
                switch (source)
                {
                    case MessageSource.Discord:
                    return _settings.DiscordMessage;
                    case MessageSource.Server:
                    return _settings.ServerMessage;
                    case MessageSource.Team:
                    return _settings.TeamMessage;
                    case MessageSource.Cards:
                    return _settings.CardMessages;
                    case MessageSource.Clan:
                    return _settings.ClanMessages;
                    case MessageSource.PluginClan:
                    case MessageSource.PluginAlliance:
                    return _settings.PluginMessage;
                }
                
                return false;
            }
        }
        #endregion

        #region PluginHandlers\BasePluginHandler.cs
        public abstract class BasePluginHandler : IPluginHandler
        {
            protected readonly DiscordChat Chat;
            protected readonly Plugin Plugin;
            private readonly string _pluginName;
            
            protected BasePluginHandler(DiscordChat chat, Plugin plugin)
            {
                Chat = chat;
                Plugin = plugin;
                _pluginName = plugin.Name;
            }
            
            public virtual bool CanSendMessage(string message, IPlayer player, DiscordUser user, MessageSource source, DiscordMessage sourceMessage) => true;
            
            public virtual void ProcessPlayerName(StringBuilder name, IPlayer player) { }
            
            public virtual bool HasCallbackMessage() => false;
            
            public virtual void ProcessCallbackMessage(string message, IPlayer player, DiscordUser user, MessageSource source, Action<string> callback) { }
            
            public virtual void ProcessMessage(StringBuilder message, IPlayer player, DiscordUser user, MessageSource source) { }
            
            public virtual bool SendMessage(string message, IPlayer player, DiscordUser user, MessageSource source, DiscordMessage sourceMessage, PlaceholderData data) => false;
            
            public string GetPluginName() => _pluginName;
        }
        #endregion

        #region PluginHandlers\BetterChatMuteHandler.cs
        public class BetterChatMuteHandler : BasePluginHandler
        {
            private readonly BetterChatMuteSettings _settings;
            
            public BetterChatMuteHandler(DiscordChat chat, BetterChatMuteSettings settings, Plugin plugin) : base(chat, plugin)
            {
                _settings = settings;
            }
            
            public override bool CanSendMessage(string message, IPlayer player, DiscordUser user, MessageSource source, DiscordMessage sourceMessage)
            {
                return player != null && !Plugin.Call<bool>("API_IsMuted", player);
            }
        }
        #endregion

        #region PluginHandlers\DiscordChatHandler.cs
        public class DiscordChatHandler : BasePluginHandler
        {
            private readonly ChatSettings _settings;
            private readonly IServer _server;
            private readonly object[] _unlinkedArgs = new object[3];
            
            public DiscordChatHandler(DiscordChat chat, ChatSettings settings, Plugin plugin, IServer server) : base(chat, plugin)
            {
                _settings = settings;
                _server = server;
                _unlinkedArgs[0] = 2;
                _unlinkedArgs[1] = settings.UnlinkedSettings.SteamIcon;
            }
            
            public override bool CanSendMessage(string message, IPlayer player, DiscordUser user, MessageSource source, DiscordMessage sourceMessage)
            {
                if (sourceMessage != null)
                {
                    if (_settings.Filter.IgnoreMessage(sourceMessage, Chat.Client.Bot.GetGuild(sourceMessage.GuildId)?.Members[sourceMessage.Author.Id]))
                    {
                        return false;
                    }
                }
                
                switch (source)
                {
                    case MessageSource.Discord:
                    return _settings.DiscordToServer && (_settings.UnlinkedSettings.AllowedUnlinked || (player != null && player.IsLinked()));
                    
                    case MessageSource.Server:
                    return _settings.ServerToDiscord;
                }
                
                return true;
            }
            
            public override bool SendMessage(string message, IPlayer player, DiscordUser user, MessageSource source, DiscordMessage sourceMessage, PlaceholderData data)
            {
                switch (source)
                {
                    case MessageSource.Discord:
                    if (_settings.UseBotToDisplayChat)
                    {
                        if (player.IsLinked())
                        {
                            Chat.Sends[MessageSource.Discord]?.QueueMessage(Chat.Lang(LangKeys.Discord.Chat.LinkedMessage, data));
                        }
                        else
                        {
                            Chat.Sends[MessageSource.Discord]?.QueueMessage(Chat.Lang(LangKeys.Discord.Chat.UnlinkedMessage, data));
                        }
                    }
                    
                    if (player.IsLinked())
                    {
                        SendLinkedToServer(player, message, data, source);
                    }
                    else
                    {
                        SendUnlinkedToServer(data);
                    }
                    
                    return true;
                    
                    case MessageSource.Server:
                    Chat.Sends[MessageSource.Discord]?.QueueMessage(Chat.Lang(LangKeys.Discord.Chat.Server, data));
                    return true;
                    case MessageSource.Team:
                    Chat.Sends[MessageSource.Team]?.QueueMessage(Chat.Lang(LangKeys.Discord.Team.Message, data));
                    return true;
                    case MessageSource.Cards:
                    Chat.Sends[MessageSource.Cards]?.QueueMessage(Chat.Lang(LangKeys.Discord.Cards.Message, data));
                    return true;
                    case MessageSource.Clan:
                    Chat.Sends[MessageSource.Clan]?.QueueMessage(Chat.Lang(LangKeys.Discord.Clans.Message, data));
                    return true;
                    case MessageSource.PluginAdminChat:
                    Chat.Sends[MessageSource.PluginAdminChat]?.QueueMessage(Chat.Lang(LangKeys.Discord.AdminChat.DiscordMessage, data));
                    return true;
                    case MessageSource.PluginClan:
                    Chat.Sends[MessageSource.PluginClan]?.QueueMessage(Chat.Lang(LangKeys.Discord.PluginClans.ClanMessage, data));
                    return true;
                    case MessageSource.PluginAlliance:
                    Chat.Sends[MessageSource.PluginAlliance]?.QueueMessage(Chat.Lang(LangKeys.Discord.PluginClans.AllianceMessage, data));
                    return true;
                }
                
                return false;
            }
            
            public void SendLinkedToServer(IPlayer player, string message, PlaceholderData placeholders, MessageSource source)
            {
                if (_settings.AllowPluginProcessing)
                {
                    if (Chat.SendBetterChatMessage(player, message, source))
                    {
                        return;
                    }
                    
                    bool playerReturn = false;
                    #if RUST
                    //Let other chat plugins process first
                    if (player.Object != null)
                    {
                        Chat.Unsubscribe("OnPlayerChat");
                        playerReturn = Interface.Call("OnPlayerChat", player.Object, message, ConVar.Chat.ChatChannel.Global) != null;
                        Chat.Subscribe("OnPlayerChat");
                    }
                    #endif
                    
                    //Let other chat plugins process first
                    Chat.Unsubscribe("OnUserChat");
                    bool userReturn = Interface.Call("OnUserChat", player, message) != null;
                    Chat.Subscribe("OnUserChat");
                    
                    if (playerReturn || userReturn)
                    {
                        return;
                    }
                }
                
                message = Chat.Lang(LangKeys.Server.LinkedMessage, placeholders);
                _server.Broadcast(message);
                Chat.Puts(Formatter.ToPlaintext(message));
            }
            
            public void SendUnlinkedToServer(PlaceholderData placeholders)
            {
                string serverMessage = Chat.Lang(LangKeys.Server.UnlinkedMessage, placeholders);
                #if RUST
                _unlinkedArgs[2] = Formatter.ToUnity(serverMessage);
                ConsoleNetwork.BroadcastToAllClients("chat.add", _unlinkedArgs);
                #else
                _server.Broadcast(serverMessage);
                #endif
                
                Chat.Puts(Formatter.ToPlaintext(serverMessage));
            }
            
            public override void ProcessMessage(StringBuilder message, IPlayer player, DiscordUser user, MessageSource source)
            {
                foreach (KeyValuePair<string, string> replacement in _settings.TextReplacements)
                {
                    message.Replace(replacement.Key, replacement.Value);
                }
            }
        }
        #endregion

        #region PluginHandlers\IPluginHandler.cs
        public interface IPluginHandler
        {
            bool CanSendMessage(string message, IPlayer player, DiscordUser user, MessageSource source, DiscordMessage sourceMessage);
            void ProcessPlayerName(StringBuilder name, IPlayer player);
            bool HasCallbackMessage();
            void ProcessCallbackMessage(string message, IPlayer player, DiscordUser user, MessageSource source, Action<string> callback);
            void ProcessMessage(StringBuilder message, IPlayer player, DiscordUser user, MessageSource source);
            bool SendMessage(string message, IPlayer player, DiscordUser user, MessageSource source, DiscordMessage sourceMessage, PlaceholderData data);
            string GetPluginName();
        }
        #endregion

        #region PluginHandlers\TranslationApiHandler.cs
        public class TranslationApiHandler : BasePluginHandler
        {
            private readonly ChatTranslatorSettings _settings;
            
            public TranslationApiHandler(DiscordChat chat, ChatTranslatorSettings settings, Plugin plugin) : base(chat, plugin)
            {
                _settings = settings;
            }
            
            public override bool HasCallbackMessage() => true;
            
            public override void ProcessCallbackMessage(string message, IPlayer player, DiscordUser user, MessageSource source, Action<string> callback)
            {
                if (CanChatTranslatorSource(source))
                {
                    Plugin.Call("Translate", message, _settings.DiscordServerLanguage, "auto", callback);
                    return;
                }
                
                callback.Invoke(message);
            }
            
            public bool CanChatTranslatorSource(MessageSource source)
            {
                if (!_settings.Enabled)
                {
                    return false;
                }
                
                switch (source)
                {
                    case MessageSource.Server:
                    return _settings.ServerMessage;
                    
                    case MessageSource.Discord:
                    return _settings.DiscordMessage;
                    
                    case MessageSource.PluginClan:
                    case MessageSource.PluginAlliance:
                    return _settings.PluginMessage;
                    
                    #if RUST
                    case MessageSource.Team:
                    return _settings.TeamMessage;
                    
                    case MessageSource.Cards:
                    return _settings.CardMessages;
                    
                    case MessageSource.Clan:
                    return _settings.ClanMessages;
                    #endif
                }
                
                return false;
            }
        }
        #endregion

        #region PluginHandlers\UFilterHandler.cs
        public class UFilterHandler : BasePluginHandler
        {
            private readonly UFilterSettings _settings;
            private readonly List<string> _replacements = new();
            
            public UFilterHandler(DiscordChat chat, UFilterSettings settings, Plugin plugin) : base(chat, plugin)
            {
                _settings = settings;
            }
            
            public override void ProcessPlayerName(StringBuilder name, IPlayer player)
            {
                if (_settings.PlayerNames)
                {
                    UFilterText(name);
                }
            }
            
            public override void ProcessMessage(StringBuilder message, IPlayer player, DiscordUser user, MessageSource source)
            {
                if (CanFilterMessage(source))
                {
                    UFilterText(message);
                }
            }
            
            private bool CanFilterMessage(MessageSource source)
            {
                switch (source)
                {
                    case MessageSource.Discord:
                    return _settings.DiscordMessages;
                    case MessageSource.Server:
                    return _settings.ServerMessage;
                    case MessageSource.Team:
                    return _settings.TeamMessage;
                    case MessageSource.Cards:
                    return _settings.CardMessage;
                    case MessageSource.Clan:
                    return _settings.ClanMessage;
                    case MessageSource.PluginClan:
                    case MessageSource.PluginAlliance:
                    return _settings.PluginMessages;
                }
                
                return false;
            }
            
            private void UFilterText(StringBuilder text)
            {
                string[] profanities = Plugin.Call<string[]>("Profanities", text.ToString());
                for (int index = 0; index < profanities.Length; index++)
                {
                    string profanity = profanities[index];
                    text.Replace(profanity, GetProfanityReplacement(profanity));
                }
            }
            
            private string GetProfanityReplacement(string profanity)
            {
                if (string.IsNullOrEmpty(profanity))
                {
                    return string.Empty;
                }
                
                for (int i = _replacements.Count; i <= profanity.Length; i++)
                {
                    _replacements.Add(new string(_settings.ReplacementCharacter, i));
                }
                
                return _replacements[profanity.Length];
            }
        }
        #endregion

        #region Templates\TemplateKeys.cs
        public static class TemplateKeys
        {
            public static class Player
            {
                private const string Base = nameof(Player) + ".";
                
                public static readonly TemplateKey Connecting = new(Base + nameof(Connecting));
                public static readonly TemplateKey Connected = new(Base + nameof(Connected));
                public static readonly TemplateKey Disconnected = new(Base + nameof(Disconnected));
            }
            
            public static class Server
            {
                private const string Base = nameof(Server) + ".";
                
                public static readonly TemplateKey Online = new(Base + nameof(Online));
                public static readonly TemplateKey Shutdown = new(Base + nameof(Shutdown));
                public static readonly TemplateKey Booting = new(Base + nameof(Booting));
            }
            
            public static class Chat
            {
                private const string Base = nameof(Chat) + ".";
                
                public static readonly TemplateKey General = new(Base + nameof(General));
                public static readonly TemplateKey Teams = new(Base + nameof(Teams));
                public static readonly TemplateKey Cards = new(Base + nameof(Cards));
                public static readonly TemplateKey Clan = new(Base + nameof(Clan));
                
                public static class Clans
                {
                    private const string Base = Chat.Base + nameof(Clans) + ".";
                    
                    public static readonly TemplateKey Clan = new(Base + nameof(Clan));
                    public static readonly TemplateKey Alliance = new(Base + nameof(Alliance));
                }
                
                public static class AdminChat
                {
                    private const string Base = Chat.Base + nameof(AdminChat) + ".";
                    
                    public static readonly TemplateKey Message = new(Base + nameof(Message));
                }
            }
            
            public static class Error
            {
                private const string Base = nameof(Error) + ".";
                
                public static readonly TemplateKey NotLinked = new(Base + nameof(NotLinked));
                
                public static class AdminChat
                {
                    private const string Base = Error.Base + nameof(AdminChat) + ".";
                    
                    public static readonly TemplateKey NotLinked = new(Base + nameof(NotLinked));
                    public static readonly TemplateKey NoPermission = new(Base + nameof(NoPermission));
                }
            }
        }
        #endregion

        #region Configuration\Plugins\AdminChatSettings.cs
        public class AdminChatSettings
        {
            [JsonProperty("Enable AdminChat Plugin Support")]
            public bool Enabled { get; set; }
            
            [JsonProperty("Chat Channel ID")]
            public Snowflake ChatChannel { get; set; }
            
            [JsonProperty("Chat Prefix")]
            public string AdminChatPrefix { get; set; }
            
            [JsonProperty("Replace Discord Message With Bot")]
            public bool ReplaceWithBot { get; set; }
            
            public AdminChatSettings(AdminChatSettings settings)
            {
                Enabled = settings?.Enabled ?? false;
                ChatChannel = settings?.ChatChannel ?? default(Snowflake);
                AdminChatPrefix = settings?.AdminChatPrefix ?? "@";
                ReplaceWithBot = settings?.ReplaceWithBot ?? true;
            }
        }
        #endregion

        #region Configuration\Plugins\AntiSpamSettings.cs
        public class AntiSpamSettings
        {
            [JsonProperty("Use AntiSpam On Player Names")]
            public bool PlayerName { get; set; }
            
            [JsonProperty("Use AntiSpam On Server Messages")]
            public bool ServerMessage { get; set; }
            
            [JsonProperty("Use AntiSpam On Chat Messages")]
            public bool DiscordMessage { get; set; }
            
            [JsonProperty("Use AntiSpam On Plugin Messages")]
            public bool PluginMessage { get; set; }
            
            #if RUST
            [JsonProperty("Use AntiSpam On Team Messages")]
            public bool TeamMessage { get; set; }
            
            [JsonProperty("Use AntiSpam On Card Messages")]
            public bool CardMessages { get; set; }
            
            [JsonProperty("Use AntiSpam On Clan Messages")]
            public bool ClanMessages { get; set; }
            #endif
            
            public AntiSpamSettings(AntiSpamSettings settings)
            {
                PlayerName = settings?.PlayerName ?? false;
                ServerMessage = settings?.ServerMessage ?? false;
                DiscordMessage = settings?.DiscordMessage ?? false;
                PluginMessage = settings?.PluginMessage ?? false;
                #if RUST
                TeamMessage = settings?.TeamMessage ?? false;
                CardMessages = settings?.CardMessages ?? false;
                ClanMessages = settings?.ClanMessages ?? false;
                #endif
            }
        }
        #endregion

        #region Configuration\Plugins\BetterChatMuteSettings.cs
        public class BetterChatMuteSettings
        {
            [JsonProperty("Ignore Muted Players")]
            public bool IgnoreMuted { get; set; }
            
            public BetterChatMuteSettings(BetterChatMuteSettings settings)
            {
                IgnoreMuted = settings?.IgnoreMuted ?? true;
            }
        }
        #endregion

        #region Configuration\Plugins\BetterChatSettings.cs
        public class BetterChatSettings
        {
            [JsonProperty("Max BetterChat Tags To Show When Sent From Discord")]
            public byte MaxTags { get; set; }
            
            public BetterChatSettings(BetterChatSettings settings)
            {
                MaxTags = settings?.MaxTags ?? 3;
            }
        }
        #endregion

        #region Configuration\Plugins\ChatTranslatorSettings.cs
        public class ChatTranslatorSettings
        {
            [JsonProperty("Enable Chat Translator")]
            public bool Enabled { get; set; }
            
            [JsonProperty("Use ChatTranslator On Server Messages")]
            public bool ServerMessage { get; set; }
            
            [JsonProperty("Use ChatTranslator On Chat Messages")]
            public bool DiscordMessage { get; set; }
            
            [JsonProperty("Use ChatTranslator On Plugin Messages")]
            public bool PluginMessage { get; set; }
            
            #if RUST
            [JsonProperty("Use ChatTranslator On Team Messages")]
            public bool TeamMessage { get; set; }
            
            [JsonProperty("Use ChatTranslator On Card Messages")]
            public bool CardMessages { get; set; }
            
            [JsonProperty("Use ChatTranslator On Clan Messages")]
            public bool ClanMessages { get; set; }
            #endif
            
            [JsonProperty("Discord Server Chat Language")]
            public string DiscordServerLanguage { get; set; }
            
            public ChatTranslatorSettings(ChatTranslatorSettings settings)
            {
                Enabled = settings?.Enabled ?? false;
                ServerMessage = settings?.ServerMessage ?? false;
                DiscordMessage = settings?.DiscordMessage ?? false;
                #if RUST
                TeamMessage = settings?.TeamMessage ?? false;
                CardMessages = settings?.CardMessages ?? false;
                ClanMessages = settings?.ClanMessages ?? false;
                #endif
                DiscordServerLanguage = settings?.DiscordServerLanguage ?? Interface.Oxide.GetLibrary<Lang>().GetServerLanguage();
            }
        }
        #endregion

        #region Configuration\Plugins\ClansSettings.cs
        public class ClansSettings
        {
            [JsonProperty("Clans Chat Channel ID")]
            public Snowflake ClansChatChannel { get; set; }
            
            [JsonProperty("Alliance Chat Channel ID")]
            public Snowflake AllianceChatChannel { get; set; }
            
            public ClansSettings(ClansSettings settings)
            {
                ClansChatChannel = settings?.ClansChatChannel ?? default(Snowflake);
                AllianceChatChannel = settings?.AllianceChatChannel ?? default(Snowflake);
            }
        }
        #endregion

        #region Configuration\Plugins\PluginSupport.cs
        public class PluginSupport
        {
            [JsonProperty("AdminChat Settings")]
            public AdminChatSettings AdminChat { get; set; }
            
            [JsonProperty("AntiSpam Settings")]
            public AntiSpamSettings AntiSpam { get; set; }
            
            [JsonProperty("BetterChat Settings")]
            public BetterChatSettings BetterChat { get; set; }
            
            [JsonProperty("BetterChatMute Settings")]
            public BetterChatMuteSettings BetterChatMute { get; set; }
            
            [JsonProperty("ChatTranslator Settings")]
            public ChatTranslatorSettings ChatTranslator { get; set; }
            
            [JsonProperty("Clan Settings")]
            public ClansSettings Clans { get; set; }
            
            [JsonProperty("UFilter Settings")]
            public UFilterSettings UFilter { get; set; }
            
            public PluginSupport(PluginSupport settings)
            {
                AdminChat = new AdminChatSettings(settings?.AdminChat);
                AntiSpam = new AntiSpamSettings(settings?.AntiSpam);
                BetterChat = new BetterChatSettings(settings?.BetterChat);
                BetterChatMute = new BetterChatMuteSettings(settings?.BetterChatMute);
                ChatTranslator = new ChatTranslatorSettings(settings?.ChatTranslator);
                Clans = new ClansSettings(settings?.Clans);
                UFilter = new UFilterSettings(settings?.UFilter);
            }
        }
        #endregion

        #region Configuration\Plugins\UFilterSettings.cs
        public class UFilterSettings
        {
            [JsonProperty("Use UFilter On Player Names")]
            public bool PlayerNames { get; set; }
            
            [JsonProperty("Use UFilter On Server Messages")]
            public bool ServerMessage { get; set; }
            
            [JsonProperty("Use UFilter On Discord Messages")]
            public bool DiscordMessages { get; set; }
            
            [JsonProperty("Use UFilter On Plugin Messages")]
            public bool PluginMessages { get; set; }
            
            #if RUST
            [JsonProperty("Use UFilter On Team Messages")]
            public bool TeamMessage { get; set; }
            
            [JsonProperty("Use UFilter On Card Messages")]
            public bool CardMessage { get; set; }
            
            [JsonProperty("Use UFilter On Clan Messages")]
            public bool ClanMessage { get; set; }
            #endif
            
            [JsonProperty("Replacement Character")]
            public char ReplacementCharacter { get; set; }
            
            public UFilterSettings(UFilterSettings settings)
            {
                PlayerNames = settings?.PlayerNames ?? false;
                ServerMessage = settings?.ServerMessage ?? false;
                DiscordMessages = settings?.DiscordMessages ?? false;
                PluginMessages = settings?.PluginMessages ?? false;
                #if RUST
                TeamMessage = settings?.TeamMessage ?? false;
                CardMessage = settings?.CardMessage ?? false;
                ClanMessage = settings?.ClanMessage ?? false;
                #endif
                
                ReplacementCharacter = settings?.ReplacementCharacter ?? '＊';
            }
        }
        #endregion

    }

}
