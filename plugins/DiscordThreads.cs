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
using static UnityEngine.Rendering.PostProcessing.HistogramMonitor;

namespace Oxide.Plugins
{
    [Info("Discord Threads", "OdsScott", "1.0.0")]
    [Description("Allows chatting between discord and game server")]
    public partial class DiscordThreads : CovalencePlugin, IDiscordPlugin
    {
        public DiscordClient Client { get; set; }

        private PluginConfig _pluginConfig;
        private DiscordGuild _actionGuild;
        private DiscordChannel _actionChannel;
        private readonly DiscordLink _link = GetLibrary<DiscordLink>();

        private void OnServerInitialized()
        {
            if (string.IsNullOrEmpty(_pluginConfig.DiscordApiKey))
            {
                PrintWarning("Please set the Discord Bot Token and reload the plugin");
                return;
            }

            Client.Connect(new BotConnection
            {
                Intents = GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
                ApiToken = _pluginConfig.DiscordApiKey,
                LogLevel = _pluginConfig.ExtensionDebugging
            });
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
            _actionGuild = guild;

            DiscordChannel channel = guild.GetChannel((Snowflake)"1226690977352388758");
            if (channel != null)
            {
                _actionChannel = channel;
            }
        }

        private IPromise<string> CreateThreadForUsers(string name, List<ulong> users)
        {
            Puts(name);
            return _actionChannel.StartThreadWithoutMessage(Client, new ThreadCreate() { Name = name, Type = ChannelType.GuildPrivateThread }).Then(channel => {
                Puts("channel1" + channel.Id.ToString());
                foreach (var user in users)
                {
                    Puts("user:" + user);
                    var discordUser = _link.GetDiscordId(Convert.ToString(user));
                    if (discordUser != 0)
                    {
                        channel.AddThreadMember(Client, discordUser);
                    }
                }
                //AddUsersToThread(channel.Id.ToString(), users);
                return channel.Id.ToString();
            });
        }

        private void AddUsersToThread(string threadSnowflake, List<ulong> users)
        {
            var snowflake = (Snowflake)threadSnowflake;
            Puts("snowflake" + snowflake.Id);
            var channel = _actionGuild.GetChannel(snowflake);
            Puts("channel2" + channel.Id.ToString());
            foreach (var user in users)
            {
                Puts("user:" + user);
                var discordUser = _link.GetDiscordId(Convert.ToString(user));
                if (discordUser != 0)
                {
                    channel.AddThreadMember(Client, discordUser);
                }
            }
        }

        private void RemoveUsersFromThread(string threadSnowflake, List<ulong> users)
        {
            var channel = _actionGuild.GetChannel((Snowflake)threadSnowflake);
            foreach (var user in users)
            {
                var discordUser = _link.GetDiscordId(Convert.ToString(user));
                if (discordUser != 0)
                {
                    channel.RemoveThreadMember(Client, discordUser);
                }
            }
        }

        private void MessageThread(string threadSnowflake, string message)
        {
            var channel = _actionGuild.GetChannel((Snowflake)threadSnowflake);
            channel.CreateMessage(Client, message);
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Loading Default Config");
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Config.Settings.DefaultValueHandling = DefaultValueHandling.Populate;
            _pluginConfig = Config.ReadObject<PluginConfig>();
            Config.WriteObject(_pluginConfig);
        }

        public class PluginConfig
        {
            [JsonProperty(PropertyName = "Discord Bot Token")]
            public string DiscordApiKey { get; set; } = string.Empty;


            [JsonConverter(typeof(StringEnumConverter))]
            [DefaultValue(DiscordLogLevel.Info)]
            [JsonProperty(PropertyName = "Discord Extension Log Level (Verbose, Debug, Info, Warning, Error, Exception, Off)")]
            public DiscordLogLevel ExtensionDebugging { get; set; } = DiscordLogLevel.Info;
        }
    }
}