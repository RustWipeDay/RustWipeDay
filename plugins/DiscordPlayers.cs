using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Ext.Discord.Attributes;
using Oxide.Ext.Discord.Builders;
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
using ProtoBuf;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using UnityEngine;

//DiscordPlayers created with PluginMerge v(1.0.7.0) by MJSU @ https://github.com/dassjosh/Plugin.Merge
namespace Oxide.Plugins
{
    [Info("Discord Players", "MJSU", "3.0.0")]
    [Description("Displays online players in discord")]
    public partial class DiscordPlayers : CovalencePlugin, IDiscordPlugin, IDiscordPool
    {
        #region Plugins\DiscordPlayers.Config.cs
        protected override void LoadDefaultConfig() { }
        
        protected override void LoadConfig()
        {
            base.LoadConfig();
            Config.Settings.DefaultValueHandling = DefaultValueHandling.Populate;
            _pluginConfig = AdditionalConfig(Config.ReadObject<PluginConfig>());
            Config.WriteObject(_pluginConfig);
        }
        
        private PluginConfig AdditionalConfig(PluginConfig config)
        {
            config.CommandMessages ??= new List<CommandSettings>();
            if (config.CommandMessages.Count == 0)
            {
                config.CommandMessages.Add(new CommandSettings
                {
                    Command = "players",
                    ShowAdmins = true,
                    AllowInDm = true,
                    EmbedFieldLimit = 25
                });
                
                config.CommandMessages.Add(new CommandSettings
                {
                    Command = "playersadmin",
                    ShowAdmins = true,
                    AllowInDm = true,
                    EmbedFieldLimit = 25
                });
            }
            
            config.Permanent ??= new List<PermanentMessageSettings>
            {
                new()
                {
                    Enabled = false,
                    ChannelId = new Snowflake(0),
                    UpdateRate = 1f,
                    EmbedFieldLimit = 25
                }
            };
            
            for (int index = 0; index < config.CommandMessages.Count; index++)
            {
                CommandSettings settings = new(config.CommandMessages[index]);
                config.CommandMessages[index] = settings;
            }
            
            for (int index = 0; index < config.Permanent.Count; index++)
            {
                PermanentMessageSettings settings = new(config.Permanent[index]);
                config.Permanent[index] = settings;
            }
            
            return config;
        }
        #endregion

        #region Plugins\DiscordPlayers.DiscordHooks.cs
        [HookMethod(DiscordExtHooks.OnDiscordGatewayReady)]
        private void OnDiscordGatewayReady(GatewayReadyEvent ready)
        {
            DiscordApplication app = Client.Bot.Application;
            
            foreach (CommandSettings command in _pluginConfig.CommandMessages)
            {
                CreateApplicationCommand(command);
            }
            
            foreach (KeyValuePair<string, Snowflake> command in _pluginData.RegisteredCommands.ToList())
            {
                if (_pluginConfig.CommandMessages.All(c => c.Command != command.Key))
                {
                    if (command.Value.IsValid())
                    {
                        app.GetGlobalCommand(Client, command.Value).Then(oldCommand => oldCommand.Delete(Client).Then(() =>
                        {
                            _pluginData.RegisteredCommands.Remove(command);
                            SaveData();
                        }).Catch<ResponseError>(error =>
                        {
                            if (error.DiscordError?.Code == 10063)
                            {
                                _pluginData.RegisteredCommands.Remove(command);
                                SaveData();
                                error.SuppressErrorMessage();
                            }
                        }));
                    }
                }
            }
            
            Puts($"{Title} Ready");
        }
        
        public void CreateApplicationCommand(CommandSettings settings)
        {
            string command = settings.Command;
            if (string.IsNullOrEmpty(command))
            {
                return;
            }
            
            ApplicationCommandBuilder builder = new(command, "Shows players currently on the server", ApplicationCommandType.ChatInput);
            builder.AllowInDirectMessages(settings.AllowInDm);
            builder.AddDefaultPermissions(PermissionFlags.None);
            
            CommandCreate cmd = builder.Build();
            DiscordCommandLocalization loc = builder.BuildCommandLocalization();
            
            _commandCache[command] = settings;
            
            _localizations.RegisterCommandLocalizationAsync(this, settings.GetTemplateName(), loc, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0)).Then(_ =>
            {
                _localizations.ApplyCommandLocalizationsAsync(this, cmd, settings.GetTemplateName()).Then(() =>
                {
                    Client.Bot.Application.CreateGlobalCommand(Client, builder.Build()).Then(appCommand =>
                    {
                        _pluginData.RegisteredCommands[command] = appCommand.Id;
                        SaveData();
                    });
                });
            });
            
            _appCommand.AddApplicationCommand(this, Client.Bot.Application.Id, HandleApplicationCommand, command);
        }
        
        [HookMethod(DiscordExtHooks.OnDiscordGuildCreated)]
        private void OnDiscordGuildCreated(DiscordGuild created)
        {
            foreach (PermanentMessageSettings config in _pluginConfig.Permanent)
            {
                if (!config.Enabled || !config.ChannelId.IsValid())
                {
                    continue;
                }
                
                DiscordChannel channel = created.GetChannel(config.ChannelId);
                if (channel == null)
                {
                    PrintWarning($"Failed to find channel ID: {config.ChannelId} in Guild: {created.Name}");
                    continue;
                }
                
                _commandCache[config.GetTemplateName().Name] = config;
                PermanentMessageData existing = _pluginData.GetPermanentMessage(config);
                if (existing != null)
                {
                    channel.GetMessage(Client, existing.MessageId)
                    .Then(message =>
                    {
                        _permanentHandler[message.Id] = new PermanentMessageHandler(Client, new MessageCache(config), config.UpdateRate, message);
                    })
                    .Catch<ResponseError>(error =>
                    {
                        if (error.HttpStatusCode == DiscordHttpStatusCode.NotFound)
                        {
                            CreatePermanentMessage(config, channel);
                            error.SuppressErrorMessage();
                        }
                    });
                }
                else
                {
                    CreatePermanentMessage(config, channel);
                }
            }
        }
        
        private void CreatePermanentMessage(PermanentMessageSettings config, DiscordChannel channel)
        {
            MessageCache cache = new(config);
            
            CreateMessage<MessageCreate>(cache, null, null, create =>
            {
                channel.CreateMessage(Client, create).Then(message =>
                {
                    _pluginData.SetPermanentMessage(config, new PermanentMessageData
                    {
                        MessageId = message.Id
                    });
                    _permanentHandler[message.Id] = new PermanentMessageHandler(Client, cache, config.UpdateRate, message);
                    SaveData();
                });
            });
        }
        
        private void HandleApplicationCommand(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            MessageCache cache = GetCache(interaction);
            if (cache == null)
            {
                PrintError("Cache is null!!");
                return;
            }
            
            CreateMessage<InteractionCallbackData>(cache, interaction, null, create =>
            {
                interaction.CreateResponse(Client, new InteractionResponse
                {
                    Type = InteractionResponseType.ChannelMessageWithSource,
                    Data = create
                });
            });
        }
        
        [DiscordMessageComponentCommand(BackCommand)]
        private void HandleBackCommand(DiscordInteraction interaction)
        {
            MessageCache cache = GetCache(interaction);
            if (cache == null)
            {
                return;
            }
            
            cache.State.PreviousPage();
            HandleUpdate(interaction, cache);
        }
        
        [DiscordMessageComponentCommand(RefreshCommand)]
        private void HandleRefreshCommand(DiscordInteraction interaction)
        {
            MessageCache cache = GetCache(interaction);
            if (cache == null)
            {
                return;
            }
            
            HandleUpdate(interaction, cache);
        }
        
        [DiscordMessageComponentCommand(ForwardCommand)]
        private void HandleForwardCommand(DiscordInteraction interaction)
        {
            MessageCache cache = GetCache(interaction);
            if (cache == null)
            {
                return;
            }
            
            cache.State.NextPage();
            HandleUpdate(interaction, cache);
        }
        
        [DiscordMessageComponentCommand(ChangeSort)]
        private void HandleChangeSortCommand(DiscordInteraction interaction)
        {
            MessageCache cache = GetCache(interaction);
            if (cache == null)
            {
                return;
            }
            
            cache.State.NextSort();
            HandleUpdate(interaction, cache);
        }
        
        private void HandleUpdate(DiscordInteraction interaction, MessageCache cache)
        {
            CreateMessage<InteractionCallbackData>(cache, interaction, null, create =>
            {
                interaction.CreateResponse(Client, new InteractionResponse
                {
                    Type = InteractionResponseType.UpdateMessage,
                    Data = create
                });
            });
        }
        #endregion

        #region Plugins\DiscordPlayers.Fields.cs
        public DiscordClient Client { get; set; }
        public DiscordPluginPool Pool { get; set; }
        
        private PluginConfig _pluginConfig; //Plugin Config
        private PluginData _pluginData;
        
        private readonly DiscordAppCommand _appCommand = GetLibrary<DiscordAppCommand>();
        private readonly DiscordPlaceholders _placeholders = GetLibrary<DiscordPlaceholders>();
        private readonly DiscordMessageTemplates _templates = GetLibrary<DiscordMessageTemplates>();
        private readonly DiscordEmbedTemplates _embed = GetLibrary<DiscordEmbedTemplates>();
        private readonly DiscordEmbedFieldTemplates _field = GetLibrary<DiscordEmbedFieldTemplates>();
        private readonly DiscordCommandLocalizations _localizations = GetLibrary<DiscordCommandLocalizations>();
        
        private readonly BotConnection _discordSettings = new();
        
        private readonly Hash<Snowflake, MessageCache> _messageCache = new();
        private readonly Hash<string, BaseMessageSettings> _commandCache = new();
        private readonly OnlinePlayerCache _playerCache = new();
        
        private readonly Hash<Snowflake, PermanentMessageHandler> _permanentHandler = new();
        
        private const string BaseCommand = nameof(DiscordPlayers) + ".";
        private const string BackCommand = BaseCommand + "B";
        private const string RefreshCommand = BaseCommand + "R";
        private const string ForwardCommand = BaseCommand + "F";
        private const string ChangeSort = BaseCommand + "S";
        
        public static DiscordPlayers Instance;
        
        public PluginTimers Timer => timer;
        #endregion

        #region Plugins\DiscordPlayers.Helpers.cs
        private const string PluginIcon = "https://assets.umod.org/images/icons/plugin/61354f8bd5faf.png";
        
        public MessageCache GetCache(DiscordInteraction interaction)
        {
            DiscordMessage message = interaction.Message;
            BaseMessageSettings command;
            if (interaction.Type == InteractionType.ApplicationCommand)
            {
                InteractionDataParsed args = interaction.Parsed;
                command = _commandCache[args.Command];
                return command != null ? new MessageCache(command) : null;
            }
            
            string customId = interaction.Data.CustomId;
            MessageCache cache = _messageCache[message.Id];
            if (cache != null)
            {
                return cache;
            }
            
            ReadOnlySpan<char> base64 = customId.AsSpan()[(customId.LastIndexOf(" ", StringComparison.Ordinal) + 1)..];
            MessageState state = MessageState.Create(base64);
            if (state == null)
            {
                SendResponse(interaction, TemplateKeys.Errors.UnknownState, GetDefault(interaction));
                return null;
            }
            
            command = _commandCache[state.Command];
            if (command == null)
            {
                SendResponse(interaction, TemplateKeys.Errors.UnknownCommand, GetDefault(interaction).Add(PlaceholderDataKeys.CommandName, state.Command));
                return null;
            }
            
            cache = new MessageCache(command, state);
            _messageCache[message.Id] = cache;
            return cache;
        }
        
        public void SendResponse(DiscordInteraction interaction, TemplateKey templateName, PlaceholderData data, MessageFlags flags = MessageFlags.Ephemeral)
        {
            interaction.CreateTemplateResponse(Client, InteractionResponseType.ChannelMessageWithSource, templateName, new InteractionCallbackData { Flags = flags }, data);
        }
        
        public string Lang(string key)
        {
            return lang.GetMessage(key, this);
        }
        
        public string Lang(string key, params object[] args)
        {
            try
            {
                return string.Format(Lang(key), args);
            }
            catch(Exception ex)
            {
                PrintError($"Lang Key '{key}' threw exception\n:{ex}");
                throw;
            }
        }
        
        private void SaveData()
        {
            if (_pluginData != null)
            {
                Interface.Oxide.DataFileSystem.WriteObject(Name, _pluginData);
            }
        }
        
        public new void PrintError(string format, params object[] args) => base.PrintError(format, args);
        #endregion

        #region Plugins\DiscordPlayers.Lang.cs
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangKeys.SortByEnumName] = "Name",
                [LangKeys.SortByEnumTime] = "Time",
            }, this);
        }
        #endregion

        #region Plugins\DiscordPlayers.MessageHandling.cs
        public void CreateMessage<T>(MessageCache cache, DiscordInteraction interaction, T create, Action<T> callback) where T : class, IDiscordMessageTemplate, new()
        {
            List<IPlayer> allList = GetPlayerList(cache);
            int perPage = cache.Settings.EmbedFieldLimit;
            List<IPlayer> pageList = allList.Skip(cache.State.Page * perPage).Take(perPage).ToPooledList(Pool);
            
            int maxPage = (allList.Count - 1) / cache.Settings.EmbedFieldLimit;
            cache.State.ClampPage((short)maxPage);
            
            PlaceholderData data = GetDefault(cache, interaction, maxPage + 1);
            data.ManualPool();
            
            T message = CreateMessage(cache.Settings, data, interaction, create);
            message.AllowedMentions = AllowedMentions.None;
            SetButtonState(message, BackCommand, cache.State.Page > 0);
            SetButtonState(message, ForwardCommand, cache.State.Page < maxPage);
            
            DiscordEmbed embed = CreateEmbeds(cache.Settings, data, interaction);
            
            message.Embeds = new List<DiscordEmbed>{embed};
            CreateFields(cache, data, interaction, pageList).Then(fields =>
            {
                ProcessEmbeds(embed, fields);
            }).Finally(() =>
            {
                callback.Invoke(message);
                data.Dispose();
                Pool.FreeList(pageList);
            });
        }
        
        public List<IPlayer> GetPlayerList(MessageCache cache)
        {
            return _playerCache.GetList(cache.State.Sort, cache.Settings.ShowAdmins);
        }
        
        public T CreateMessage<T>(BaseMessageSettings settings, PlaceholderData data, DiscordInteraction interaction, T message) where T : class, IDiscordMessageTemplate, new()
        {
            if (settings.IsPermanent())
            {
                return _templates.GetGlobalTemplate(this, settings.GetTemplateName()).ToMessage(data, message);
            }
            
            return _templates.GetLocalizedTemplate(this, settings.GetTemplateName(), interaction).ToMessage(data, message);
        }
        
        public void SetButtonState(IDiscordMessageTemplate message, string command, bool enabled)
        {
            for (int index = 0; index < message.Components.Count; index++)
            {
                ActionRowComponent row = message.Components[index];
                for (int i = 0; i < row.Components.Count; i++)
                {
                    BaseComponent component = row.Components[i];
                    if (component is ButtonComponent)
                    {
                        ButtonComponent button = (ButtonComponent)component;
                        if (button.CustomId.StartsWith(command))
                        {
                            button.Disabled = !enabled;
                            return;
                        }
                    }
                }
            }
        }
        
        public DiscordEmbed CreateEmbeds(BaseMessageSettings settings, PlaceholderData data, DiscordInteraction interaction)
        {
            TemplateKey name = settings.GetTemplateName();
            return settings.IsPermanent() ? _embed.GetGlobalTemplate(this, name).ToEntity(data) : _embed.GetLocalizedTemplate(this, name, interaction).ToEntity(data);
        }
        
        public IPromise<List<EmbedField>> CreateFields(MessageCache cache, PlaceholderData data, DiscordInteraction interaction, List<IPlayer> onlineList)
        {
            DiscordEmbedFieldTemplate template;
            if (cache.Settings.IsPermanent())
            {
                template = _field.GetGlobalTemplate(this, cache.Settings.GetTemplateName());
            }
            else
            {
                template = _field.GetLocalizedTemplate(this, cache.Settings.GetTemplateName(), interaction);
            }
            
            List<PlaceholderData> placeholders = new();
            
            for (int index = 0; index < onlineList.Count; index++)
            {
                PlaceholderData playerData = CloneForPlayer(data, onlineList[index], cache.State.Page * cache.Settings.EmbedFieldLimit + index + 1);
                placeholders.Add(playerData);
            }
            
            return template.ToEntityBulk(placeholders);
        }
        
        public void ProcessEmbeds(DiscordEmbed embed, List<EmbedField> fields)
        {
            embed.Fields ??= new List<EmbedField>();
            embed.Fields.AddRange(fields);
        }
        #endregion

        #region Plugins\DiscordPlayers.Placeholders.cs
        public void RegisterPlaceholders()
        {
            _placeholders.RegisterPlaceholder<int>(this,PlaceholderKeys.PlayerIndex, PlaceholderDataKeys.PlayerIndex);
            _placeholders.RegisterPlaceholder<MessageState, int>(this, PlaceholderKeys.Page, PlaceholderDataKeys.MessageState, GetPage);
            _placeholders.RegisterPlaceholder<MessageState, string>(this, PlaceholderKeys.SortState, PlaceholderDataKeys.MessageState, GetSort);
            _placeholders.RegisterPlaceholder<string>(this, PlaceholderKeys.CommandId, PlaceholderDataKeys.CommandId);
            _placeholders.RegisterPlaceholder<string>(this,PlaceholderKeys.CommandName, PlaceholderDataKeys.CommandName);
            _placeholders.RegisterPlaceholder<int>(this, PlaceholderKeys.MaxPage, PlaceholderDataKeys.MaxPage);
        }
        
        public int GetPage(MessageState embed) => embed.Page + 1;
        public string GetSort(PlaceholderState state, MessageState embed)
        {
            DiscordInteraction interaction = state.Data.Get<DiscordInteraction>();
            string key = embed.Sort == SortBy.Name ? LangKeys.SortByEnumName : LangKeys.SortByEnumTime;
            return interaction != null ? interaction.GetLangMessage(this, key) : Lang(key);
        }
        
        public PlaceholderData CloneForPlayer(PlaceholderData source, IPlayer player, int index)
        {
            DiscordUser user = player.GetDiscordUser();
            TimeSpan onlineDuration = _playerCache.GetOnlineDuration(player);
            return source.Clone()
            .RemoveUser()
            .AddUser(user)
            .AddPlayer(player)
            .Add(PlaceholderDataKeys.PlayerIndex, index)
            .Add(PlaceholderDataKeys.PlayerDuration, onlineDuration)
            .AddTimestamp(DateTimeOffset.UtcNow - onlineDuration);
        }
        
        public PlaceholderData GetDefault(DiscordInteraction interaction)
        {
            return _placeholders.CreateData(this).AddInteraction(interaction);
        }
        
        public PlaceholderData GetDefault(MessageCache cache, DiscordInteraction interaction)
        {
            return GetDefault(interaction)
            .Add(PlaceholderDataKeys.MessageState, cache.State);
        }
        
        public PlaceholderData GetDefault(MessageCache cache, DiscordInteraction interaction, int maxPage)
        {
            return GetDefault(cache, interaction)
            .Add(PlaceholderDataKeys.MaxPage, maxPage)
            .Add(PlaceholderDataKeys.CommandId, cache.State.CreateBase64String());
        }
        #endregion

        #region Plugins\DiscordPlayers.Setup.cs
        private void Init()
        {
            Instance = this;
            _discordSettings.ApiToken = _pluginConfig.DiscordApiKey;
            _discordSettings.LogLevel = _pluginConfig.ExtensionDebugging;
            _discordSettings.Intents = GatewayIntents.Guilds;
            
            _pluginData = Interface.Oxide.DataFileSystem.ReadObject<PluginData>(Name) ?? new PluginData();
        }
        
        private void OnServerInitialized()
        {
            if (string.IsNullOrEmpty(_pluginConfig.DiscordApiKey))
            {
                PrintWarning("Please set the Discord Bot Token and reload the plugin");
                return;
            }
            
            _playerCache.Initialize(players.Connected);
            
            RegisterPlaceholders();
            RegisterTemplates();
            
            foreach (CommandSettings message in _pluginConfig.CommandMessages)
            {
                if (message.EmbedFieldLimit > 25)
                {
                    PrintWarning($"Players For Embed cannot be greater than 25 for command {message.Command}");
                }
                else if (message.EmbedFieldLimit < 0)
                {
                    PrintWarning($"Players For Embed cannot be less than 0 for command {message.Command}");
                }
                
                message.EmbedFieldLimit = Mathf.Clamp(message.EmbedFieldLimit, 0, 25);
            }
            
            Client.Connect(_discordSettings);
        }
        
        private void OnUserConnected(IPlayer player)
        {
            _playerCache.OnUserConnected(player);
        }
        
        private void OnUserDisconnected(IPlayer player)
        {
            _playerCache.OnUserDisconnected(player);
        }
        
        private void Unload()
        {
            SaveData();
            Instance = null;
        }
        #endregion

        #region Plugins\DiscordPlayers.Templates.cs
        public void RegisterTemplates()
        {
            foreach (CommandSettings command in _pluginConfig.CommandMessages)
            {
                DiscordEmbedFieldTemplate embed = command.Command == "playersadmin" ? GetDefaultAdminFieldTemplate() : GetDefaultFieldTemplate();
                CreateCommandTemplates(command, embed, false);
            }
            
            foreach (PermanentMessageSettings permanent in _pluginConfig.Permanent)
            {
                DiscordEmbedFieldTemplate embed = permanent.TemplateName == "PermanentAdmin" ? GetDefaultAdminFieldTemplate() : GetDefaultFieldTemplate();
                CreateCommandTemplates(permanent, embed, true);
            }
            
            DiscordMessageTemplate unknownState = CreateTemplateEmbed("Error: Failed to find a state for this message. Please create a new message.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Errors.UnknownState, unknownState, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate unknownCommand = CreateTemplateEmbed($"Error: Command not found '{PlaceholderKeys.CommandName}'. Please create a new message", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Errors.UnknownCommand, unknownCommand, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
        }
        
        private void CreateCommandTemplates(BaseMessageSettings command, DiscordEmbedFieldTemplate @default, bool isGlobal)
        {
            DiscordMessageTemplate template = CreateBaseMessage();
            TemplateKey name = command.GetTemplateName();
            RegisterTemplate(_templates, name, template, isGlobal, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            RegisterTemplate(_field, name, @default, isGlobal, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordEmbedTemplate embed = GetDefaultEmbedTemplate();
            RegisterTemplate(_embed, name, embed, isGlobal, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
        }
        
        public void RegisterTemplate<TTemplate>(BaseMessageTemplateLibrary<TTemplate> library, TemplateKey name, TTemplate template, bool isGlobal, TemplateVersion version, TemplateVersion minVersion) where TTemplate : class, new()
        {
            if (isGlobal)
            {
                library.RegisterGlobalTemplateAsync(this, name, template, version, minVersion);
            }
            else
            {
                library.RegisterLocalizedTemplateAsync(this, name, template, version, minVersion);
            }
        }
        
        public DiscordMessageTemplate CreateBaseMessage()
        {
            return new DiscordMessageTemplate
            {
                Content = string.Empty,
                Components =
                {
                    new ButtonTemplate("Back", ButtonStyle.Primary, $"{BackCommand} {PlaceholderKeys.CommandId}", "⬅"),
                    new ButtonTemplate($"Page: {PlaceholderKeys.Page}/{PlaceholderKeys.MaxPage}", ButtonStyle.Primary, "PAGE", false),
                    new ButtonTemplate("Next", ButtonStyle.Primary, $"{ForwardCommand} {PlaceholderKeys.CommandId}", "➡"),
                    new ButtonTemplate("Refresh", ButtonStyle.Primary, $"{RefreshCommand} {PlaceholderKeys.CommandId}", "🔄"),
                    new ButtonTemplate($"Sorted By: {PlaceholderKeys.SortState}", ButtonStyle.Primary, $"{ChangeSort} {PlaceholderKeys.CommandId}")
                }
            };
        }
        
        public DiscordEmbedTemplate GetDefaultEmbedTemplate()
        {
            return new DiscordEmbedTemplate
            {
                Title = $"{DefaultKeys.Server.Name}",
                Description = $"{DefaultKeys.Server.Players}/{DefaultKeys.Server.MaxPlayers} Online Players | {{server.players.loading}} Loading | {{server.players.queued}} Queued",
                Color = DiscordColor.Blurple.ToHex(),
                TimeStamp = true,
                Footer =
                {
                    Enabled = true,
                    Text = $"{DefaultKeys.Plugin.Name} V{DefaultKeys.Plugin.Version} by {DefaultKeys.Plugin.Author}",
                    IconUrl = PluginIcon
                }
            };
        }
        
        public DiscordEmbedFieldTemplate GetDefaultFieldTemplate()
        {
            return new DiscordEmbedFieldTemplate($"#{PlaceholderKeys.PlayerIndex} {DefaultKeys.Player.NameClan}", $"**Connected:** {DefaultKeys.Timespan.Hours}h {DefaultKeys.Timespan.Minutes}m {DefaultKeys.Timespan.Seconds}s");
        }
        
        public DiscordEmbedFieldTemplate GetDefaultAdminFieldTemplate()
        {
            return new DiscordEmbedFieldTemplate($"#{PlaceholderKeys.PlayerIndex} {DefaultKeys.Player.NameClan}",
            $"**Steam ID:**{DefaultKeys.Player.Id}\n" +
            $"**Connected:** {DefaultKeys.Timespan.Hours}h {DefaultKeys.Timespan.Minutes}m {DefaultKeys.Timespan.Seconds}s\n" +
            $"**Ping:** {DefaultKeys.Player.Ping}ms\n" +
            $"**Country:** {DefaultKeys.Player.Country}\n" +
            $"**User:** {DefaultKeys.User.Mention}");
        }
        
        public DiscordMessageTemplate CreateTemplateEmbed(string description, DiscordColor color)
        {
            return new DiscordMessageTemplate
            {
                Embeds = new List<DiscordEmbedTemplate>
                {
                    new()
                    {
                        Description = $"[{DefaultKeys.Plugin.Title}] {description}",
                        Color = color.ToHex()
                    }
                }
            };
        }
        #endregion

        #region Cache\MessageCache.cs
        public class MessageCache
        {
            public readonly BaseMessageSettings Settings;
            public readonly MessageState State;
            
            public MessageCache(BaseMessageSettings settings, MessageState state = null)
            {
                Settings = settings;
                State = state ?? MessageState.CreateNew(Settings.GetTemplateName());
            }
        }
        #endregion

        #region Cache\OnlinePlayerCache.cs
        public class OnlinePlayerCache
        {
            private readonly PlayerListCache _byNameCache = new(new NameComparer());
            private readonly PlayerListCache _byOnlineTime;
            private readonly Hash<string, DateTime> _onlineSince = new();
            
            public OnlinePlayerCache()
            {
                _byOnlineTime = new PlayerListCache(new OnlineSinceComparer(_onlineSince));
            }
            
            public void Initialize(IEnumerable<IPlayer> connected)
            {
                #if RUST
                foreach (Network.Connection connection in Network.Net.sv.connections)
                {
                    _onlineSince[connection.ownerid.ToString()] = DateTime.UtcNow - TimeSpan.FromSeconds(connection.GetSecondsConnected());
                }
                #endif
                
                foreach (IPlayer player in connected)
                {
                    OnUserConnected(player);
                }
            }
            
            public TimeSpan GetOnlineDuration(IPlayer player)
            {
                return DateTime.UtcNow - _onlineSince[player.Id];
            }
            
            public List<IPlayer> GetList(SortBy sort, bool includeAdmin)
            {
                List<IPlayer> list = sort == SortBy.Time ? _byOnlineTime.GetList(includeAdmin) : _byNameCache.GetList(includeAdmin);
                //return Enumerable.Range(0, 100).Select(i => list[0]).ToList();
                return list;
            }
            
            public void OnUserConnected(IPlayer player)
            {
                _onlineSince.TryAdd(player.Id, DateTime.UtcNow);
                _byNameCache.Add(player);
                _byOnlineTime.Add(player);
            }
            
            public void OnUserDisconnected(IPlayer player)
            {
                _onlineSince.Remove(player.Id);
                _byNameCache.Remove(player);
                _byOnlineTime.Remove(player);
            }
            
            class NameComparer : IComparer<IPlayer>
            {
                public int Compare(IPlayer x, IPlayer y)
                {
                    return string.Compare(x?.Name, y?.Name, StringComparison.Ordinal);
                }
            }
            
            class OnlineSinceComparer : IComparer<IPlayer>
            {
                private readonly Hash<string, DateTime> _onlineSince;
                
                public OnlineSinceComparer(Hash<string, DateTime> onlineSince)
                {
                    _onlineSince = onlineSince;
                }
                
                public int Compare(IPlayer x, IPlayer y)
                {
                    return _onlineSince[x.Id].CompareTo(_onlineSince[y.Id]);
                }
            }
        }
        #endregion

        #region Cache\PlayerListCache.cs
        public class PlayerListCache
        {
            private readonly List<IPlayer> _allList = new();
            private readonly List<IPlayer> _nonAdminList = new();
            
            private readonly IComparer<IPlayer> _comparer;
            
            public PlayerListCache(IComparer<IPlayer> comparer)
            {
                _comparer = comparer;
            }
            
            public void Add(IPlayer player)
            {
                Remove(player);
                Insert(_allList, player);
                Insert(_nonAdminList, player);
            }
            
            public void Insert(List<IPlayer> list, IPlayer player)
            {
                int index = list.BinarySearch(player, _comparer);
                if (index < 0)
                {
                    list.Insert(~index, player);
                }
                else
                {
                    list[index] = player;
                }
            }
            
            public void Remove(IPlayer player)
            {
                _allList.Remove(player);
                _nonAdminList.Remove(player);
            }
            
            public List<IPlayer> GetList(bool includeAdmin)
            {
                return includeAdmin ? _allList : _nonAdminList;
            }
        }
        #endregion

        #region Configuration\BaseMessageSettings.cs
        public abstract class BaseMessageSettings
        {
            [JsonProperty(PropertyName = "Display Admins In The Player List", Order = 1001)]
            public bool ShowAdmins { get; set; }
            
            [DefaultValue(25)]
            [JsonProperty(PropertyName = "Players Per Embed (0 - 25)", Order = 1002)]
            public int EmbedFieldLimit { get; set; }
            
            public abstract bool IsPermanent();
            public abstract TemplateKey GetTemplateName();
            
            [JsonConstructor]
            public BaseMessageSettings() { }
            
            public BaseMessageSettings(BaseMessageSettings settings)
            {
                ShowAdmins = settings?.ShowAdmins ?? true;
                EmbedFieldLimit = settings?.EmbedFieldLimit ?? 25;
            }
        }
        #endregion

        #region Configuration\CommandSettings.cs
        public class CommandSettings : BaseMessageSettings
        {
            [JsonProperty(PropertyName = "Command Name (Must Be Unique)")]
            public string Command { get; set; }
            
            [JsonProperty(PropertyName = "Allow Command In Direct Messages")]
            public bool AllowInDm { get; set; }
            
            [JsonConstructor]
            public CommandSettings() { }
            
            public CommandSettings(CommandSettings settings) : base(settings)
            {
                Command = settings?.Command ?? "players";
                AllowInDm = settings?.AllowInDm ?? true;
            }
            
            public override bool IsPermanent() => false;
            public override TemplateKey GetTemplateName() => new(Command);
        }
        #endregion

        #region Configuration\PermanentMessageSettings.cs
        public class PermanentMessageSettings : BaseMessageSettings
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled { get; set; }
            
            [JsonProperty(PropertyName = "Template Name (Must Be Unique)")]
            public string TemplateName { get; set; }
            
            [JsonProperty(PropertyName = "Permanent Message Channel ID")]
            public Snowflake ChannelId { get; set; }
            
            [JsonProperty(PropertyName = "Update Rate (Minutes)")]
            public float UpdateRate { get; set; }
            
            [JsonConstructor]
            public PermanentMessageSettings() { }
            
            public PermanentMessageSettings(PermanentMessageSettings settings) : base(settings)
            {
                Enabled = settings?.Enabled ?? false;
                TemplateName = settings?.TemplateName ?? "Permanent";
                ChannelId = settings?.ChannelId ?? default(Snowflake);
                UpdateRate = settings?.UpdateRate ?? 1f;
            }
            
            public override bool IsPermanent() => true;
            public override TemplateKey GetTemplateName() => new(TemplateName);
        }
        #endregion

        #region Configuration\PluginConfig.cs
        public class PluginConfig
        {
            [DefaultValue("")]
            [JsonProperty(PropertyName = "Discord Bot Token")]
            public string DiscordApiKey { get; set; }
            
            [JsonProperty(PropertyName = "Command Messages")]
            public List<CommandSettings> CommandMessages { get; set; }
            
            [JsonProperty(PropertyName = "Permanent Messages")]
            public List<PermanentMessageSettings> Permanent { get; set; }
            
            [JsonConverter(typeof(StringEnumConverter))]
            [DefaultValue(DiscordLogLevel.Info)]
            [JsonProperty(PropertyName = "Discord Extension Log Level (Verbose, Debug, Info, Warning, Error, Exception, Off)")]
            public DiscordLogLevel ExtensionDebugging { get; set; }
        }
        #endregion

        #region Data\PermanentMessageData.cs
        public class PermanentMessageData
        {
            public Snowflake MessageId { get; set; }
        }
        #endregion

        #region Data\PluginData.cs
        public class PluginData
        {
            public Hash<string, PermanentMessageData> PermanentMessageIds = new();
            public Hash<string, Snowflake> RegisteredCommands = new();
            
            public PermanentMessageData GetPermanentMessage(PermanentMessageSettings config)
            {
                return PermanentMessageIds[config.TemplateName];
            }
            
            public void SetPermanentMessage(PermanentMessageSettings config, PermanentMessageData data)
            {
                PermanentMessageIds[config.TemplateName] = data;
            }
        }
        #endregion

        #region Enums\SortBy.cs
        public enum SortBy : byte
        {
            Name,
            Time
        }
        #endregion

        #region Handlers\PermanentMessageHandler.cs
        public class PermanentMessageHandler
        {
            private readonly DiscordClient _client;
            private readonly MessageCache _cache;
            private readonly DiscordMessage _message;
            private readonly MessageUpdate _update = new();
            private readonly Timer _timer;
            private DateTime _lastUpdate;
            
            public PermanentMessageHandler(DiscordClient client, MessageCache cache, float updateRate, DiscordMessage message)
            {
                _client = client;
                _cache = cache;
                _message = message;
                _timer = DiscordPlayers.Instance.Timer.Every(updateRate * 60f, SendUpdate);
                SendUpdate();
            }
            
            private void SendUpdate()
            {
                if (_lastUpdate + TimeSpan.FromSeconds(5) > DateTime.UtcNow)
                {
                    return;
                }
                
                _lastUpdate = DateTime.UtcNow;
                
                DiscordPlayers.Instance.CreateMessage(_cache, null, _update, message =>
                {
                    _message.Edit(_client, message).Catch<ResponseError>(error =>
                    {
                        if (error.HttpStatusCode == DiscordHttpStatusCode.NotFound)
                        {
                            _timer?.Destroy();
                        }
                    });
                });
            }
        }
        #endregion

        #region Lang\LangKeys.cs
        public static class LangKeys
        {
            public const string SortByEnumName = nameof(SortByEnumName);
            public const string SortByEnumTime = nameof(SortByEnumTime);
        }
        #endregion

        #region Placeholders\PlaceholderDataKeys.cs
        public static class PlaceholderDataKeys
        {
            public static readonly PlaceholderDataKey CommandId = new("command.id");
            public static readonly PlaceholderDataKey CommandName = new("command.name");
            public static readonly PlaceholderDataKey PlayerIndex = new("player.index");
            public static readonly PlaceholderDataKey PlayerDuration = new("timespan");
            public static readonly PlaceholderDataKey MaxPage = new("page.max");
            public static readonly PlaceholderDataKey MessageState = new("message.state");
        }
        #endregion

        #region Placeholders\PlaceholderKeys.cs
        public class PlaceholderKeys
        {
            public static readonly PlaceholderKey PlayerIndex = new(nameof(DiscordPlayers), "player.index");
            public static readonly PlaceholderKey Page = new(nameof(DiscordPlayers), "state.page");
            public static readonly PlaceholderKey SortState = new(nameof(DiscordPlayers), "state.sort");
            public static readonly PlaceholderKey CommandId = new(nameof(DiscordPlayers), "command.id");
            public static readonly PlaceholderKey CommandName = new(nameof(DiscordPlayers), "command.name");
            public static readonly PlaceholderKey MaxPage = new(nameof(DiscordPlayers), "page.max");
        }
        #endregion

        #region State\MessageState.cs
        [ProtoContract]
        public class MessageState
        {
            [ProtoMember(1)]
            public short Page;
            
            [ProtoMember(2)]
            public SortBy Sort;
            
            [ProtoMember(3)]
            public string Command;
            
            private MessageState() { }
            
            public static MessageState CreateNew(TemplateKey command)
            {
                return new MessageState
                {
                    Command = command.Name
                };
            }
            
            public static MessageState Create(ReadOnlySpan<char> base64)
            {
                try
                {
                    Span<byte> buffer = stackalloc byte[64];
                    Convert.TryFromBase64Chars(base64, buffer, out int written);
                    MemoryStream stream = DiscordPlayers.Instance.Pool.GetMemoryStream();
                    stream.Write(buffer[..written]);
                    stream.Flush();
                    stream.Position = 0;
                    MessageState state = Serializer.Deserialize<MessageState>(stream);
                    DiscordPlayers.Instance.Pool.FreeMemoryStream(stream);
                    return state;
                }
                catch (Exception ex)
                {
                    DiscordPlayers.Instance.PrintError($"An error occured parsing state. State: {base64.ToString()}. Exception:\n{ex}");
                    return null;
                }
            }
            
            public string CreateBase64String()
            {
                MemoryStream stream = DiscordPlayers.Instance.Pool.GetMemoryStream();
                Serializer.Serialize(stream, this);
                stream.TryGetBuffer(out ArraySegment<byte> buffer);
                string base64 = Convert.ToBase64String(buffer.AsSpan());
                DiscordPlayers.Instance.Pool.FreeMemoryStream(stream);
                return base64;
            }
            
            public void NextPage() => Page++;
            
            public void PreviousPage() => Page--;
            
            public void ClampPage(short maxPage) => Page = Page.Clamp((short)0, maxPage);
            
            public void NextSort() => Sort = EnumCache<SortBy>.Instance.Next(Sort);
            
            public override string ToString()
            {
                return $"{{ Command = '{Command}' Sort = {Sort.ToString()} Page = {Page} }}";
            }
        }
        #endregion

        #region Templates\TemplateKeys.cs
        public static class TemplateKeys
        {
            public static class Errors
            {
                private const string Base = nameof(Errors) + ".";
                
                public static readonly TemplateKey UnknownState = new(Base + nameof(UnknownState));
                public static readonly TemplateKey UnknownCommand = new(Base + nameof(UnknownCommand));
            }
        }
        #endregion

    }

}
