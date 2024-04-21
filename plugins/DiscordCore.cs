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
using Oxide.Ext.Discord.Helpers;
using Oxide.Ext.Discord.Interfaces;
using Oxide.Ext.Discord.Libraries;
using Oxide.Ext.Discord.Logging;
using Oxide.Ext.Discord.Types;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

//DiscordCore created with PluginMerge v(1.0.8.0) by MJSU @ https://github.com/dassjosh/Plugin.Merge
namespace Oxide.Plugins
{
    [Info("Discord Core", "MJSU", "3.0.0")]
    [Description("Creates a link between a player and discord")]
    public partial class DiscordCore : CovalencePlugin, IDiscordPlugin, IDiscordLink
    {
        #region Plugins\DiscordCore.Fields.cs
        public DiscordClient Client { get; set; }
        
        private PluginData _pluginData;
        private PluginConfig _pluginConfig;
        
        private DiscordUser _bot;
        
        public DiscordGuild Guild;
        
        private readonly BotConnection _discordSettings = new()
        {
            Intents = GatewayIntents.Guilds | GatewayIntents.GuildMembers
        };
        
        private readonly DiscordLink _link = GetLibrary<DiscordLink>();
        private readonly DiscordMessageTemplates _templates = GetLibrary<DiscordMessageTemplates>();
        private readonly DiscordPlaceholders _placeholders = GetLibrary<DiscordPlaceholders>();
        private readonly DiscordLocales _lang = GetLibrary<DiscordLocales>();
        private readonly DiscordCommandLocalizations _local = GetLibrary<DiscordCommandLocalizations>();
        private readonly StringBuilder _sb = new();
        
        private JoinHandler _joinHandler;
        private JoinBanHandler _banHandler;
        private LinkHandler _linkHandler;
        
        private const string UsePermission = "discordcore.use";
        private static readonly DiscordColor AccentColor = new("de8732");
        private static readonly DiscordColor Success = new("43b581");
        private static readonly DiscordColor Danger = new("f04747");
        private const string PlayerArg = "player";
        private const string UserArg = "user";
        private const string CodeArg = "code";
        
        private DiscordApplicationCommand _appCommand;
        private string _allowedChannels;
        
        public static DiscordCore Instance;
        #endregion

        #region Plugins\DiscordCore.Setup.cs
        // ReSharper disable once UnusedMember.Local
        private void Init()
        {
            Instance = this;
            _pluginData = Interface.Oxide.DataFileSystem.ReadObject<PluginData>(Name);
            
            permission.RegisterPermission(UsePermission, this);
            
            _banHandler = new JoinBanHandler(_pluginConfig.LinkBanSettings);
            _linkHandler = new LinkHandler(_pluginData, _pluginConfig);
            _joinHandler = new JoinHandler(_pluginConfig.LinkSettings, _linkHandler, _banHandler);
            
            _discordSettings.ApiToken = _pluginConfig.ApiKey;
            _discordSettings.LogLevel = _pluginConfig.ExtensionDebugging;
        }
        
        protected override void LoadDefaultConfig()
        {
            PrintWarning("Loading Default Config");
        }
        
        protected override void LoadConfig()
        {
            base.LoadConfig();
            Config.Settings.DefaultValueHandling = DefaultValueHandling.Populate;
            _pluginConfig = AdditionalConfig(Config.ReadObject<PluginConfig>());
            Config.WriteObject(_pluginConfig);
        }
        
        public PluginConfig AdditionalConfig(PluginConfig config)
        {
            config.LinkSettings = new LinkSettings(config.LinkSettings);
            config.WelcomeMessageSettings = new WelcomeMessageSettings(config.WelcomeMessageSettings);
            config.LinkMessageSettings = new GuildMessageSettings(config.LinkMessageSettings);
            config.PermissionSettings = new LinkPermissionSettings(config.PermissionSettings);
            config.LinkBanSettings = new LinkBanSettings(config.LinkBanSettings);
            return config;
        }
        
        // ReSharper disable once UnusedMember.Local
        private void OnServerInitialized()
        {
            RegisterChatLangCommand(nameof(DiscordCoreChatCommand), ServerLang.Commands.DcCommand);
            
            if (string.IsNullOrEmpty(_pluginConfig.ApiKey))
            {
                PrintWarning("Please set the Discord Bot Token in the config and reload the plugin");
                return;
            }
            
            foreach (DiscordInfo info in _pluginData.PlayerDiscordInfo.Values)
            {
                if (info.LastOnline == DateTime.MinValue)
                {
                    info.LastOnline = DateTime.UtcNow;
                }
            }
            
            _link.AddLinkPlugin(this);
            RegisterPlaceholders();
            ValidateGroups();
            
            Client.Connect(_discordSettings);
        }
        
        public void ValidateGroups()
        {
            foreach (string group in _pluginConfig.PermissionSettings.LinkGroups)
            {
                if (!permission.GroupExists(group))
                {
                    PrintWarning($"`{group}` is set as the link group but group does not exist");
                }
            }
            
            foreach (string group in _pluginConfig.PermissionSettings.UnlinkGroups)
            {
                if (!permission.GroupExists(group))
                {
                    PrintWarning($"`{group}` is set as the unlink group but group does not exist");
                }
            }
            
            foreach (string perm in _pluginConfig.PermissionSettings.LinkPermissions)
            {
                if (!permission.PermissionExists(perm))
                {
                    PrintWarning($"`{perm}` is set as the link permission but group does not exist");
                }
            }
            
            foreach (string perm in _pluginConfig.PermissionSettings.UnlinkPermissions)
            {
                if (!permission.PermissionExists(perm))
                {
                    PrintWarning($"`{perm}` is set as the unlink permission but group does not exist");
                }
            }
        }
        
        // ReSharper disable once UnusedMember.Local
        private void Unload()
        {
            SaveData();
            Instance = null;
        }
        #endregion

        #region Plugins\DiscordCore.Lang.cs
        public void Chat(IPlayer player, string key, PlaceholderData data = null)
        {
            if (player.IsConnected)
            {
                player.Reply(string.Format(Lang(ServerLang.Format, player), Lang(key, player, data)));
            }
        }
        
        public void BroadcastMessage(string key, PlaceholderData data)
        {
            string message = Lang(key);
            message = _placeholders.ProcessPlaceholders(message, data);
            if (string.IsNullOrEmpty(message))
            {
                return;
            }
            
            message = string.Format(Lang(ServerLang.Format), message);
            server.Broadcast(message);
        }
        
        public string Lang(string key, IPlayer player = null, PlaceholderData data = null)
        {
            string message = lang.GetMessage(key, this, player?.Id);
            if (data != null)
            {
                message = _placeholders.ProcessPlaceholders(message, data);
            }
            
            return message;
        }
        
        public void RegisterChatLangCommand(string command, string langKey)
        {
            HashSet<string> registeredCommands = new();
            foreach (string langType in lang.GetLanguages(this))
            {
                Dictionary<string, string> langKeys = lang.GetMessages(langType, this);
                string commandValue;
                if (langKeys.TryGetValue(langKey, out commandValue) && !string.IsNullOrEmpty(commandValue) && registeredCommands.Add(commandValue))
                {
                    AddCovalenceCommand(commandValue, command);
                }
            }
        }
        
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [ServerLang.Format] = $"[#CCCCCC][[{AccentColor.ToHex()}]{Title}[/#]] {{0}}[/#]",
                [ServerLang.NoPermission] = "You do not have permission to use this command",
                
                [ServerLang.Commands.DcCommand] = "dc",
                [ServerLang.Commands.CodeCommand] = "code",
                [ServerLang.Commands.UserCommand] = "user",
                [ServerLang.Commands.LeaveCommand] = "leave",
                [ServerLang.Commands.AcceptCommand] = "accept",
                [ServerLang.Commands.DeclineCommand] = "decline",
                [ServerLang.Commands.LinkCommand] = "link",
                
                [ServerLang.Commands.Code.LinkInfo] = $"To complete your activation please open Discord use the following command: [{AccentColor.ToHex()}]/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Discord.DiscordCommand)} {DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Discord.LinkCommand)} {PlaceholderKeys.LinkCode}[/#].\n",
                [ServerLang.Commands.Code.LinkServer] = $"In order to use this command you must be in the {DefaultKeys.Guild.Name.Color(AccentColor)} discord server. " +
                $"You can join @ {ServerFormatting.Color($"{PlaceholderKeys.InviteUrl}", AccentColor)}.\n",
                [ServerLang.Commands.Code.LinkInGuild] = $"This command can be used in the following guild channels {PlaceholderKeys.CommandChannels}.\n",
                [ServerLang.Commands.Code.LinkInDm] = $"This command can be used in the following in a direct message to {DefaultKeys.User.Fullname.Color(AccentColor)} bot",
                
                [ServerLang.Commands.User.MatchFound] = $"We found a match by username. " +
                $"We have a sent a discord message to {DefaultKeys.User.Fullname.Color(AccentColor)} to complete the link.\n" +
                $"If you haven't received a message make sure you allow DM's from {DefaultKeys.Bot.Fullname.Color(AccentColor)}.",
                [ServerLang.Commands.User.Errors.InvalidSyntax] = "Invalid User Join Syntax.\n" +
                $"[{AccentColor.ToHex()}]/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} {DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.UserCommand)} username[/#] to start the link process by your discord username\n" +
                $"[{AccentColor.ToHex()}]/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} {DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.UserCommand)} userid[/#] to start the link process by your discord user ID",
                [ServerLang.Commands.User.Errors.UserIdNotFound] = $"Failed to find a discord user in the {DefaultKeys.Guild.Name.Color(AccentColor)} Discord server with user ID {DefaultKeys.Snowflake.Id.Color(Danger)}",
                [ServerLang.Commands.User.Errors.UserNotFound] = $"Failed to find a any discord users in the {DefaultKeys.Guild.Name.Color(AccentColor)} Discord server with the username {PlaceholderKeys.NotFound.Color(Danger)}",
                [ServerLang.Commands.User.Errors.MultipleUsersFound] = $"Multiple discord users found in the the {DefaultKeys.Guild.Name.Color(AccentColor)} Discord server matching {PlaceholderKeys.NotFound.Color(Danger)}. " +
                "Please include more of the username and/or the discriminator in your search.",
                [ServerLang.Commands.User.Errors.SearchError] = "An error occured while trying to search by username. " +
                "Please try a different username or try again later. " +
                "If the issue persists please notify an admin.",
                
                [ServerLang.Commands.Leave.Errors.NotLinked] = "We were unable to unlink your account as you do not appear to have been linked.",
                
                [ServerLang.Announcements.Link.Command] = $"{DefaultKeys.Player.Name.Color(AccentColor)} has successfully linked their game account with their discord user {DefaultKeys.User.Fullname.Color(AccentColor)}. If you would would like to be linked type /{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} to learn more.",
                [ServerLang.Announcements.Link.Admin] = $"{DefaultKeys.Player.Name.Color(AccentColor)} has successfully been linked by an admin to discord user {DefaultKeys.User.Fullname.Color(AccentColor)}.",
                [ServerLang.Announcements.Link.Api] = $"{DefaultKeys.Player.Name.Color(AccentColor)} has successfully linked their game account with their discord user {DefaultKeys.User.Fullname.Color(AccentColor)}. If you would would like to be linked type /{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} to learn more.",
                [ServerLang.Announcements.Link.GuildRejoin] = $"{DefaultKeys.Player.Name.Color(AccentColor)} has been relinked with discord user {DefaultKeys.User.Fullname.Color(AccentColor)} for rejoining the {DefaultKeys.Guild.Name.Color(AccentColor)} discord server",
                [ServerLang.Announcements.Link.InactiveRejoin] = $"{DefaultKeys.Player.Name.Color(AccentColor)} has been relinked with discord user {DefaultKeys.User.Fullname.Color(AccentColor)} for rejoining the {DefaultKeys.Server.Name.Color(AccentColor)} game server",
                [ServerLang.Announcements.Unlink.Command] = $"{DefaultKeys.Player.Name.Color(AccentColor)} has successfully unlinked their game account from their discord user {DefaultKeys.User.Fullname.Color(AccentColor)}.",
                [ServerLang.Announcements.Unlink.Admin] = $"{DefaultKeys.Player.Name.Color(AccentColor)} has successfully been unlinked by an admin from discord user {DefaultKeys.User.Fullname.Color(AccentColor)}.",
                [ServerLang.Announcements.Unlink.Api] = $"{DefaultKeys.Player.Name.Color(AccentColor)} has successfully unlinked their game account from their discord user {DefaultKeys.User.Fullname.Color(AccentColor)}.",
                [ServerLang.Announcements.Unlink.LeftGuild] = $"{DefaultKeys.Player.Name.Color(AccentColor)} has been unlinked from discord user {DefaultKeys.User.Fullname.Color(AccentColor)} they left the {DefaultKeys.Guild.Name.Color(AccentColor)} Discord server",
                [ServerLang.Announcements.Unlink.Inactive] = $"{DefaultKeys.Player.Name.Color(AccentColor)} has been unlinked from discord user {DefaultKeys.User.Fullname.Color(AccentColor)} because they haven't been active on {DefaultKeys.Server.Name.Color(AccentColor)} game server for {DefaultKeys.Timespan.TotalDays.Color(AccentColor)} days",
                
                [ServerLang.Link.Completed.Command] = $"You have successfully linked your player {DefaultKeys.Player.Name.Color(AccentColor)} with discord user {DefaultKeys.User.Fullname.Color(AccentColor)}",
                [ServerLang.Link.Completed.Admin] = $"You have been successfully linked by an admin with player {DefaultKeys.Player.Name.Color(AccentColor)} and discord user {DefaultKeys.User.Fullname.Color(AccentColor)}",
                [ServerLang.Link.Completed.Api] = $"You have successfully linked your player {DefaultKeys.Player.Name.Color(AccentColor)} with discord user {DefaultKeys.User.Fullname.Color(AccentColor)}",
                [ServerLang.Link.Completed.GuildRejoin] = $"Your player {DefaultKeys.Player.Name.Color(AccentColor)} has been relinked with discord user {DefaultKeys.User.Fullname.Color(AccentColor)} because rejoined the {DefaultKeys.Guild.Name.Color(AccentColor)} Discord server",
                [ServerLang.Link.Completed.InactiveRejoin] = $"Your player {DefaultKeys.Player.Name.Color(AccentColor)} has been relinked with discord user {DefaultKeys.User.Fullname.Color(AccentColor)} because rejoined {DefaultKeys.Server.Name.Color(AccentColor)} server",
                [ServerLang.Unlink.Completed.Command] = $"You have successfully unlinked your player {DefaultKeys.Player.Name.Color(AccentColor)} from discord user {DefaultKeys.User.Fullname.Color(AccentColor)}",
                [ServerLang.Unlink.Completed.Admin] = $"You have been successfully unlinked by an admin from discord user {DefaultKeys.User.Fullname.Color(AccentColor)}",
                [ServerLang.Unlink.Completed.Api] = $"You have successfully unlinked your player {DefaultKeys.Player.Name.Color(AccentColor)} from discord user {DefaultKeys.User.Fullname.Color(AccentColor)}",
                [ServerLang.Unlink.Completed.LeftGuild] = $"Your player {DefaultKeys.Player.Name.Color(AccentColor)} has been unlinked from discord user {DefaultKeys.User.Fullname.Color(AccentColor)} because you left the {DefaultKeys.Guild.Name.Color(AccentColor)} Discord server",
                
                [ServerLang.Link.Declined.JoinWithPlayer] = $"We have declined the discord link between {DefaultKeys.Player.Name.Color(AccentColor)} and {DefaultKeys.User.Fullname.Color(AccentColor)}",
                [ServerLang.Link.Declined.JoinWithUser] = $"{DefaultKeys.User.Fullname.Color(AccentColor)} has declined your link to {DefaultKeys.Player.Name.Color(AccentColor)}",
                
                [ServerLang.Link.Errors.InvalidSyntax] = "Invalid Link Syntax. Please type the command you were given in Discord. " +
                "Command should be in the following format:" +
                $"[{AccentColor.ToHex()}]/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} {DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.LinkCommand)} {{code}}[/#] where {{code}} is the code sent to you in Discord.",
                
                [ServerLang.Banned.IsUserBanned] = "You have been banned from joining by Discord user due to multiple declined join attempts. " +
                $"Your ban will end in {DefaultKeys.Timespan.Days} days {DefaultKeys.Timespan.Hours} hours {DefaultKeys.Timespan.Minutes} minutes {DefaultKeys.Timespan.Seconds} Seconds.",
                
                [ServerLang.Join.ByPlayer] = $"{DefaultKeys.User.Fullname.Color(AccentColor)} is trying to link their Discord account with your game account. " +
                $"If you wish to [{Success.ToHex()}]accept[/#] this link please type [{Success.ToHex()}]/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} {DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.AcceptCommand)}[/#]. " +
                $"If you wish to [{Danger.ToHex()}]decline[/#] this link please type [{Danger.ToHex()}]/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} {DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DeclineCommand)}[/#]",
                [ServerLang.Discord.DiscordCommand] = "dc",
                [ServerLang.Discord.LinkCommand] = "link",
                
                [ServerLang.Join.Errors.PlayerJoinActivationNotFound] = "There are no pending joins in progress for this game account. Please start the link in Discord and try again.",
                
                [ServerLang.Errors.PlayerAlreadyLinked] = $"This player is already linked to Discord user {DefaultKeys.User.Fullname.Color(AccentColor)}. " +
                $"If you wish to link yourself to another account please type [{AccentColor.ToHex()}]/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} {DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.LeaveCommand)}[/#]",
                [ServerLang.Errors.DiscordAlreadyLinked] = $"This Discord user is already linked to player {DefaultKeys.Player.Name.Color(AccentColor)}. " +
                $"If you wish to link yourself to another account please type [{AccentColor.ToHex()}]/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} {DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.LeaveCommand)}[/#]",
                [ServerLang.Errors.ActivationNotFound] = $"We failed to find any pending joins with code [{AccentColor.ToHex()}]/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)}[/#]. " +
                "Please verify the code is correct and try again.",
                [ServerLang.Errors.MustBeCompletedInDiscord] = "You need to complete the steps provided in Discord since you started the link from the game server.",
                [ServerLang.Errors.ConsolePlayerNotSupported] = "This command cannot be ran in the server console. ",
                
                [ServerLang.Commands.HelpMessage] = "Allows players to link their player and discord accounts together. " +
                $"Players must first join the {DefaultKeys.Guild.Name.Color(AccentColor)} Discord @ [{AccentColor.ToHex()}]{PlaceholderKeys.InviteUrl}[/#]\n" +
                $"[{AccentColor.ToHex()}]/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} {DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.CodeCommand)}[/#] to start the link process using a code\n" +
                $"[{AccentColor.ToHex()}]/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} {DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.UserCommand)} username[/#] to start the link process by your discord username\n" +
                $"[{AccentColor.ToHex()}]/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} {DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.UserCommand)} userid[/#] to start the link process by your discord user ID\n" +
                $"[{AccentColor.ToHex()}]/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} {DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.LeaveCommand)}[/#] to to unlink yourself from discord\n" +
                $"[{AccentColor.ToHex()}]/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)}[/#] to see this message again",
            }, this);
        }
        #endregion

        #region Plugins\DiscordCore.ChatCommands.cs
        // ReSharper disable once UnusedParameter.Local
        private void DiscordCoreChatCommand(IPlayer player, string cmd, string[] args)
        {
            if (!player.HasPermission(UsePermission))
            {
                Chat(player, ServerLang.NoPermission);
                return;
            }
            
            if (player.Id == "server_console")
            {
                Chat(player, ServerLang.Errors.ConsolePlayerNotSupported, GetDefault(player));
                return;
            }
            
            if (args.Length == 0)
            {
                DisplayHelp(player);
                return;
            }
            
            string subCommand = args[0];
            if (subCommand.Equals(Lang(ServerLang.Commands.CodeCommand, player), StringComparison.OrdinalIgnoreCase))
            {
                HandleServerCodeJoin(player);
                return;
            }
            
            if (subCommand.Equals(Lang(ServerLang.Commands.UserCommand, player), StringComparison.OrdinalIgnoreCase))
            {
                HandleServerUserJoin(player, args);
                return;
            }
            
            if (subCommand.Equals(Lang(ServerLang.Commands.LeaveCommand, player), StringComparison.OrdinalIgnoreCase))
            {
                HandleServerLeave(player);
                return;
            }
            
            if (subCommand.Equals(Lang(ServerLang.Commands.AcceptCommand, player), StringComparison.OrdinalIgnoreCase))
            {
                HandleUserJoinAccept(player);
                return;
            }
            
            if (subCommand.Equals(Lang(ServerLang.Commands.DeclineCommand, player), StringComparison.OrdinalIgnoreCase))
            {
                HandleUserJoinDecline(player);
                return;
            }
            
            if (subCommand.Equals(Lang(ServerLang.Commands.LinkCommand, player), StringComparison.OrdinalIgnoreCase))
            {
                HandleServerCompleteLink(player, args);
                return;
            }
            
            DisplayHelp(player);
        }
        
        public void DisplayHelp(IPlayer player)
        {
            Chat(player, ServerLang.Commands.HelpMessage, GetDefault(player));
        }
        
        public void HandleServerCodeJoin(IPlayer player)
        {
            if (player.IsLinked())
            {
                Chat(player, ServerLang.Errors.PlayerAlreadyLinked, GetDefault(player, player.GetDiscordUser()));
                return;
            }
            
            //Puts("A");
            
            JoinData join = _joinHandler.CreateActivation(player);
            //Puts("B");
            using (PlaceholderData data = GetDefault(player).AddUser(_bot).Add(PlaceholderDataKeys.Code, join.Code))
            {
                data.ManualPool();
                _sb.Clear();
                _sb.Append(LangPlaceholder(ServerLang.Commands.Code.LinkInfo, data));
                _sb.Append(LangPlaceholder(ServerLang.Commands.Code.LinkServer, data));
                if (!string.IsNullOrEmpty(_allowedChannels))
                {
                    _sb.Append(LangPlaceholder(ServerLang.Commands.Code.LinkInGuild, data));
                }
                
                if (_appCommand?.DmPermission != null && _appCommand.DmPermission.Value)
                {
                    _sb.Append(LangPlaceholder(ServerLang.Commands.Code.LinkInDm, data));
                }
            }
            
            Chat(player, _sb.ToString());
        }
        
        public void HandleServerUserJoin(IPlayer player, string[] args)
        {
            if (player.IsLinked())
            {
                Chat(player, ServerLang.Errors.PlayerAlreadyLinked, GetDefault(player, player.GetDiscordUser()));
                return;
            }
            
            if (_banHandler.IsBanned(player))
            {
                Chat(player, ServerLang.Banned.IsUserBanned, GetDefault(player).AddTimeSpan(_banHandler.GetRemainingDuration(player)));
                return;
            }
            
            if (args.Length < 2)
            {
                Chat(player, ServerLang.Commands.User.Errors.InvalidSyntax, GetDefault(player));
                return;
            }
            
            string search = args[1];
            
            Snowflake id;
            if (Snowflake.TryParse(search, out id))
            {
                GuildMember member = Guild.Members[id];
                if (member == null)
                {
                    Chat(player, ServerLang.Commands.User.Errors.UserIdNotFound, GetDefault(player).AddSnowflake(id));
                    return;
                }
                
                UserSearchMatchFound(player, member.User);
                return;
            }
            
            int discriminatorIndex = search.LastIndexOf('#');
            string userName;
            string discriminator;
            if (discriminatorIndex == -1)
            {
                userName = search;
                discriminator = null;
            }
            else
            {
                userName = search.Substring(0, discriminatorIndex);
                discriminator = search.Substring(discriminatorIndex, search.Length - discriminatorIndex);
            }
            
            GuildSearchMembers guildSearch = new()
            {
                Query = userName,
                Limit = 1000
            };
            
            Guild.SearchMembers(Client, guildSearch).Then(members =>
            {
                HandleChatJoinUserResults(player, members, userName, discriminator);
            }).Catch(error =>
            {
                Chat(player, ServerLang.Commands.User.Errors.SearchError, GetDefault(player));
            });
        }
        
        public void HandleChatJoinUserResults(IPlayer player, List<GuildMember> members, string userName, string discriminator)
        {
            if (members.Count == 0)
            {
                string name = !string.IsNullOrEmpty(discriminator) ? $"{userName}#{discriminator}" : userName;
                Chat(player, ServerLang.Commands.User.Errors.UserNotFound, GetDefault(player).Add(PlaceholderDataKeys.NotFound, name));
                return;
            }
            
            if (members.Count == 1)
            {
                UserSearchMatchFound(player, members[0].User);
                return;
            }
            
            DiscordUser user = null;
            
            int count = 0;
            for (int index = 0; index < members.Count; index++)
            {
                GuildMember member = members[index];
                DiscordUser searchUser = member.User;
                if (discriminator == null)
                {
                    if (searchUser.Username.StartsWith(userName, StringComparison.OrdinalIgnoreCase))
                    {
                        user = searchUser;
                        count++;
                        if (count > 1)
                        {
                            break;
                        }
                    }
                }
                #pragma warning disable CS0618
                else if (searchUser.Username.Equals(userName, StringComparison.OrdinalIgnoreCase) && (searchUser.HasUpdatedUsername || searchUser.Discriminator.Equals(discriminator)))
                #pragma warning restore CS0618
                {
                    user = searchUser;
                    break;
                }
            }
            
            if (user == null || count > 1)
            {
                string name = !string.IsNullOrEmpty(discriminator) ? $"{userName}#{discriminator}" : userName;
                Chat(player, ServerLang.Commands.User.Errors.MultipleUsersFound, GetDefault(player).Add(PlaceholderDataKeys.NotFound, name));
                return;
            }
            
            UserSearchMatchFound(player, user);
        }
        
        public void UserSearchMatchFound(IPlayer player, DiscordUser user)
        {
            _joinHandler.CreateActivation(player, user, JoinSource.Server);
            using (PlaceholderData data = GetDefault(player, user))
            {
                data.ManualPool();
                Chat(player, ServerLang.Commands.User.MatchFound, data);
                SendTemplateMessage(TemplateKeys.Join.CompleteLink, user, player, data);
            }
        }
        
        public void HandleServerLeave(IPlayer player)
        {
            DiscordUser user = player.GetDiscordUser();
            if (user == null)
            {
                Chat(player, ServerLang.Commands.Leave.Errors.NotLinked, GetDefault(player));
                return;
            }
            
            _linkHandler.HandleUnlink(player, user, UnlinkedReason.Command, null);
        }
        
        public void HandleUserJoinAccept(IPlayer player)
        {
            JoinData join = _joinHandler.FindCompletedByPlayer(player);
            if (join == null)
            {
                Chat(player, ServerLang.Join.Errors.PlayerJoinActivationNotFound, GetDefault(player));
                return;
            }
            
            if (join.From == JoinSource.Server)
            {
                Chat(player, ServerLang.Errors.MustBeCompletedInDiscord, GetDefault(player));
                return;
            }
            
            _joinHandler.CompleteLink(join, null);
        }
        
        public void HandleUserJoinDecline(IPlayer player)
        {
            JoinData join = _joinHandler.FindCompletedByPlayer(player);
            if (join == null)
            {
                Chat(player, ServerLang.Join.Errors.PlayerJoinActivationNotFound, GetDefault(player));
                return;
            }
            
            _joinHandler.DeclineLink(join, null);
        }
        
        public void HandleServerCompleteLink(IPlayer player, string[] args)
        {
            if (player.IsLinked())
            {
                Chat(player, ServerLang.Errors.PlayerAlreadyLinked, GetDefault(player, player.GetDiscordUser()));
                return;
            }
            
            if (args.Length < 2)
            {
                Chat(player, ServerLang.Link.Errors.InvalidSyntax, GetDefault(player));
                return;
            }
            
            string code = args[1];
            JoinData join = _joinHandler.FindByCode(code);
            if (join == null)
            {
                Chat(player, ServerLang.Errors.MustBeCompletedInDiscord, GetDefault(player));
                return;
            }
            
            if (join.From == JoinSource.Server)
            {
                Chat(player, ServerLang.Errors.MustBeCompletedInDiscord, GetDefault(player));
                return;
            }
            
            if (join.Discord.IsLinked())
            {
                Chat(player, ServerLang.Errors.DiscordAlreadyLinked, GetDefault(player, join.Discord));
                return;
            }
            
            join.Player = player;
            _joinHandler.CompleteLink(join, null);
        }
        #endregion

        #region Plugins\DiscordCore.Hooks.cs
        // ReSharper disable once UnusedMember.Local
        private void OnUserConnected(IPlayer player)
        {
            _linkHandler.OnUserConnected(player);
        }
        #endregion

        #region Plugins\DiscordCore.DiscordHooks.cs
        // ReSharper disable once UnusedMember.Local
        [HookMethod(DiscordExtHooks.OnDiscordGatewayReady)]
        private void OnDiscordGatewayReady(GatewayReadyEvent ready)
        {
            _bot = ready.User;
            
            DiscordGuild guild = null;
            if (ready.Guilds.Count == 1 && !_pluginConfig.GuildId.IsValid())
            {
                guild = ready.Guilds.Values.FirstOrDefault();
            }
            
            if (guild == null)
            {
                guild = ready.Guilds[_pluginConfig.GuildId];
                if (guild == null)
                {
                    PrintError("Failed to find a matching guild for the Discord Server Id. " +
                    "Please make sure your guild Id is correct and the bot is in the discord server.");
                    return;
                }
            }
            
            Guild = guild;
            
            DiscordApplication app = Client.Bot.Application;
            if (!app.HasApplicationFlag(ApplicationFlags.GatewayGuildMembersLimited) && !app.HasApplicationFlag(ApplicationFlags.GatewayGuildMembers))
            {
                PrintError($"You need to enable \"Server Members Intent\" for {Client.Bot.BotUser.Username} @ https://discord.com/developers/applications\n" +
                $"{Name} will not function correctly until that is fixed. Once updated please reload {Name}.");
                return;
            }
            
            Puts($"Connected to bot: {_bot.Username}");
        }
        
        // ReSharper disable once UnusedMember.Local
        [HookMethod(DiscordExtHooks.OnDiscordBotFullyLoaded)]
        private void OnDiscordBotFullyLoaded()
        {
            RegisterTemplates();
            RegisterUserApplicationCommands();
            RegisterAdminApplicationCommands();
            _linkHandler.ProcessLeaveAndRejoin();
            SetupGuildWelcomeMessage();
            foreach (Snowflake role in _pluginConfig.PermissionSettings.LinkRoles)
            {
                if (!Guild?.Roles.ContainsKey(role) ?? false)
                {
                    PrintWarning($"`{role}` is set as the link role but role does not exist");
                }
            }
            
            foreach (Snowflake role in _pluginConfig.PermissionSettings.UnlinkRoles)
            {
                if (!Guild?.Roles.ContainsKey(role) ?? false)
                {
                    PrintWarning($"`{role}` is set as the unlink role but role does not exist");
                }
            }
        }
        
        // ReSharper disable once UnusedMember.Local
        [HookMethod(DiscordExtHooks.OnDiscordGuildMemberAdded)]
        private void OnDiscordGuildMemberAdded(GuildMemberAddedEvent member, DiscordGuild guild)
        {
            if (Guild?.Id != guild.Id)
            {
                return;
            }
            
            _linkHandler.OnGuildMemberJoin(member.User);
            if (!_pluginConfig.WelcomeMessageSettings.EnableWelcomeMessage || !_pluginConfig.WelcomeMessageSettings.SendOnGuildJoin)
            {
                return;
            }
            
            if (member.User.IsLinked())
            {
                return;
            }
            
            SendGlobalTemplateMessage(TemplateKeys.WelcomeMessage.PmWelcomeMessage, member.User);
        }
        
        // ReSharper disable once UnusedMember.Local
        [HookMethod(DiscordExtHooks.OnDiscordGuildMemberRemoved)]
        private void OnDiscordGuildMemberRemoved(GuildMemberRemovedEvent member, DiscordGuild guild)
        {
            if (Guild?.Id != guild.Id)
            {
                return;
            }
            
            _linkHandler.OnGuildMemberLeft(member.User);
        }
        
        // ReSharper disable once UnusedMember.Local
        [HookMethod(DiscordExtHooks.OnDiscordGuildMemberRoleAdded)]
        private void OnDiscordGuildMemberRoleAdded(GuildMember member, Snowflake roleId, DiscordGuild guild)
        {
            if (Guild?.Id != guild.Id)
            {
                return;
            }
            
            if (!_pluginConfig.WelcomeMessageSettings.EnableWelcomeMessage || !_pluginConfig.WelcomeMessageSettings.SendOnRoleAdded.Contains(roleId))
            {
                return;
            }
            
            if (member.User.IsLinked())
            {
                return;
            }
            
            SendGlobalTemplateMessage(TemplateKeys.WelcomeMessage.PmWelcomeMessage, member.User);
        }
        #endregion

        #region Plugins\DiscordCore.UserAppCommands.cs
        public void RegisterUserApplicationCommands()
        {
            ApplicationCommandBuilder builder = new ApplicationCommandBuilder(UserAppCommands.Command, "Discord Core Commands", ApplicationCommandType.ChatInput)
            .AddDefaultPermissions(PermissionFlags.None);
            
            AddUserCodeCommand(builder);
            AddUserUserCommand(builder);
            AddUserLeaveCommand(builder);
            AddUserLinkCommand(builder);
            
            CommandCreate build = builder.Build();
            DiscordCommandLocalization localization = builder.BuildCommandLocalization();
            
            TemplateKey template = new("User");
            _local.RegisterCommandLocalizationAsync(this, template, localization, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0)).Then(_ =>
            {
                _local.ApplyCommandLocalizationsAsync(this, build, template).Then(() =>
                {
                    Client.Bot.Application.CreateGlobalCommand(Client, build).Then(command =>
                    {
                        _appCommand = command;
                        CreateAllowedChannels(command);
                    });
                });
            });
        }
        
        public void AddUserCodeCommand(ApplicationCommandBuilder builder)
        {
            builder.AddSubCommand(UserAppCommands.CodeCommand, "Start the link between discord and the game server using a link code");
        }
        
        public void AddUserUserCommand(ApplicationCommandBuilder builder)
        {
            builder.AddSubCommand(UserAppCommands.UserCommand, "Start the link between discord and the game server by game server player name", sub =>
            {
                sub.AddOption(CommandOptionType.String, PlayerArg, "Player name on the game server",
                options => options.AutoComplete().Required());
            });
        }
        
        public void AddUserLeaveCommand(ApplicationCommandBuilder builder)
        {
            builder.AddSubCommand(UserAppCommands.LeaveCommand, "Unlink your discord and game server accounts");
        }
        
        public void AddUserLinkCommand(ApplicationCommandBuilder builder)
        {
            builder.AddSubCommand(UserAppCommands.LinkCommand, "Complete the link using the given link code", sub =>
            {
                sub.AddOption(CommandOptionType.String, CodeArg, "Code to complete the link",
                options => options.Required()
                .MinLength(_pluginConfig.LinkSettings.LinkCodeLength)
                .MaxLength(_pluginConfig.LinkSettings.LinkCodeLength));
            });
        }
        
        public void CreateAllowedChannels(DiscordApplicationCommand command, int attempts = 0)
        {
            if (attempts >= 3)
            {
                return;
            }
            
            command.GetPermissions(Client, Guild.Id)
            .Then(CreateAllowedChannels)
            .Catch<ResponseError>(error =>
            {
                timer.In(1f, () =>
                {
                    attempts++;
                    if (attempts < 3)
                    {
                        error.SuppressErrorMessage();
                        CreateAllowedChannels(command, attempts);
                    }
                });
            });
        }
        
        public void CreateAllowedChannels(GuildCommandPermissions permissions)
        {
            List<string> channels = new();
            for (int index = 0; index < permissions.Permissions.Count; index++)
            {
                CommandPermissions perm = permissions.Permissions[index];
                if (perm.Type == CommandPermissionType.Channel)
                {
                    string name = Guild.Channels[perm.Id]?.Name;
                    if (!string.IsNullOrEmpty(name))
                    {
                        channels.Add(name);
                    }
                }
            }
            
            _allowedChannels = string.Join(", ", channels);
            _placeholders.RegisterPlaceholder(this, PlaceholderKeys.CommandChannels, _allowedChannels);
        }
        
        // ReSharper disable once UnusedMember.Local
        // ReSharper disable once UnusedParameter.Local
        [DiscordApplicationCommand(UserAppCommands.Command, UserAppCommands.CodeCommand)]
        private void DiscordCodeCommand(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            DiscordUser user = interaction.User;
            if (user.IsLinked())
            {
                SendTemplateMessage(TemplateKeys.Errors.UserAlreadyLinked, interaction, GetDefault(user.Player, user));
                return;
            }
            
            JoinData join = _joinHandler.CreateActivation(user);
            SendTemplateMessage(TemplateKeys.Commands.Code.Success, interaction, GetDefault(user).Add(PlaceholderDataKeys.Code, join.Code));
        }
        
        // ReSharper disable once UnusedMember.Local
        [DiscordApplicationCommand(UserAppCommands.Command, UserAppCommands.UserCommand)]
        private void DiscordUserCommand(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            DiscordUser user = interaction.User;
            if (user.IsLinked())
            {
                SendTemplateMessage(TemplateKeys.Errors.UserAlreadyLinked, interaction, GetDefault(user));
                return;
            }
            
            if (_banHandler.IsBanned(user))
            {
                SendTemplateMessage(TemplateKeys.Banned.PlayerBanned, interaction,GetDefault(user).AddTimestamp(_banHandler.GetBannedEndDate(user)));
                return;
            }
            
            string playerId = parsed.Args.GetString(PlayerArg);
            IPlayer player = covalence.Players.FindPlayerById(playerId);
            if (player == null)
            {
                SendTemplateMessage(TemplateKeys.Commands.User.Error.PlayerIsInvalid, interaction, GetDefault(user));
                return;
            }
            
            if (player.IsLinked())
            {
                SendTemplateMessage(TemplateKeys.Errors.PlayerAlreadyLinked, interaction, GetDefault(player, user));
                return;
            }
            
            if (!player.IsConnected)
            {
                SendTemplateMessage(TemplateKeys.Commands.User.Error.PlayerNotConnected, interaction, GetDefault(player, user));
                return;
            }
            
            _joinHandler.CreateActivation(player, user, JoinSource.Discord);
            
            using (PlaceholderData data = GetDefault(player, user))
            {
                data.ManualPool();
                Chat(player, ServerLang.Join.ByPlayer, data);
                SendTemplateMessage(TemplateKeys.Commands.User.Success, interaction, data);
            }
        }
        
        // ReSharper disable once UnusedMember.Local
        [DiscordAutoCompleteCommand(UserAppCommands.Command, PlayerArg, UserAppCommands.UserCommand)]
        private void HandleNameAutoComplete(DiscordInteraction interaction, InteractionDataOption focused)
        {
            string search = focused.GetString();
            InteractionAutoCompleteBuilder response = interaction.GetAutoCompleteBuilder();
            response.AddAllOnlineFirstPlayers(search, PlayerNameFormatter.ClanName);
            interaction.CreateResponse(Client, response);
        }
        
        // ReSharper disable once UnusedMember.Local
        // ReSharper disable once UnusedParameter.Local
        [DiscordApplicationCommand(UserAppCommands.Command, UserAppCommands.LeaveCommand)]
        private void DiscordLeaveCommand(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            DiscordUser user = interaction.User;
            if (!user.IsLinked())
            {
                SendTemplateMessage(TemplateKeys.Commands.Leave.Error.UserNotLinked, interaction, GetDefault(user));
                return;
            }
            
            IPlayer player = user.Player;
            _linkHandler.HandleUnlink(player, user, UnlinkedReason.Command, interaction);
        }
        
        // ReSharper disable once UnusedMember.Local
        [DiscordApplicationCommand(UserAppCommands.Command, UserAppCommands.LinkCommand)]
        private void DiscordLinkCommand(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            DiscordUser user = interaction.User;
            if (user.IsLinked())
            {
                SendTemplateMessage(TemplateKeys.Errors.UserAlreadyLinked, interaction, GetDefault(user.Player, user));
                return;
            }
            
            string code = parsed.Args.GetString(CodeArg);
            JoinData join = _joinHandler.FindByCode(code);
            if (join == null)
            {
                SendTemplateMessage(TemplateKeys.Errors.CodeActivationNotFound, interaction, GetDefault(user).Add(PlaceholderDataKeys.Code, code));
                return;
            }
            
            if (join.From == JoinSource.Discord)
            {
                SendTemplateMessage(TemplateKeys.Errors.MustBeCompletedInServer, interaction, GetDefault(user).Add(PlaceholderDataKeys.Code, code));
                return;
            }
            
            join.Discord = user;
            
            _joinHandler.CompleteLink(join, interaction);
        }
        #endregion

        #region Plugins\DiscordCore.MessageComponentCommands.cs
        private const string WelcomeMessageLinkAccountsButtonId = nameof(DiscordCore) + "_PmLinkAccounts";
        private const string GuildWelcomeMessageLinkAccountsButtonId = nameof(DiscordCore) + "_GuildLinkAccounts";
        private const string AcceptLinkButtonId = nameof(DiscordCore) + "_AcceptLink";
        private const string DeclineLinkButtonId = nameof(DiscordCore) + "_DeclineLink";
        
        // ReSharper disable once UnusedMember.Local
        [DiscordMessageComponentCommand(WelcomeMessageLinkAccountsButtonId)]
        private void HandleWelcomeMessageLinkAccounts(DiscordInteraction interaction)
        {
            DiscordUser user = interaction.User;
            if (user.IsLinked())
            {
                SendTemplateMessage(TemplateKeys.Errors.UserAlreadyLinked,interaction, GetDefault(user.Player, user));
                return;
            }
            
            JoinData join = _joinHandler.CreateActivation(user);
            SendTemplateMessage(TemplateKeys.Link.WelcomeMessage.DmLinkAccounts, interaction, GetDefault(user).Add(PlaceholderDataKeys.Code, join.Code));
        }
        
        // ReSharper disable once UnusedMember.Local
        [DiscordMessageComponentCommand(GuildWelcomeMessageLinkAccountsButtonId)]
        private void HandleGuildWelcomeMessageLinkAccounts(DiscordInteraction interaction)
        {
            DiscordUser user = interaction.User;
            if (user.IsLinked())
            {
                SendTemplateMessage(TemplateKeys.Errors.UserAlreadyLinked, interaction, GetDefault(user.Player, user));
                return;
            }
            
            JoinData join = _joinHandler.CreateActivation(user);
            SendTemplateMessage(TemplateKeys.Link.WelcomeMessage.GuildLinkAccounts, interaction, GetDefault(user).Add(PlaceholderDataKeys.Code, join.Code));
        }
        
        // ReSharper disable once UnusedMember.Local
        [DiscordMessageComponentCommand(AcceptLinkButtonId)]
        private void HandleAcceptLinkButton(DiscordInteraction interaction)
        {
            DiscordUser user = interaction.User;
            if (user.IsLinked())
            {
                SendTemplateMessage(TemplateKeys.Errors.UserAlreadyLinked, interaction, GetDefault(user.Player, user));
                return;
            }
            
            JoinData join = _joinHandler.FindCompletedByUser(user);
            if (join == null)
            {
                SendTemplateMessage(TemplateKeys.Errors.LookupActivationNotFound, interaction, GetDefault(user));
                return;
            }
            
            _joinHandler.CompleteLink(join, interaction);
        }
        
        // ReSharper disable once UnusedMember.Local
        [DiscordMessageComponentCommand(DeclineLinkButtonId)]
        private void HandleDeclineLinkButton(DiscordInteraction interaction)
        {
            DiscordUser user = interaction.User;
            if (user.IsLinked())
            {
                SendTemplateMessage(TemplateKeys.Errors.UserAlreadyLinked, interaction, GetDefault(user.Player, user));
                return;
            }
            
            JoinData join = _joinHandler.FindCompletedByUser(user);
            if (join == null)
            {
                SendTemplateMessage(TemplateKeys.Errors.LookupActivationNotFound, interaction, GetDefault(user));
                return;
            }
            
            _joinHandler.DeclineLink(join, interaction);
        }
        #endregion

        #region Plugins\DiscordCore.AdminAppCommands.cs
        public void RegisterAdminApplicationCommands()
        {
            ApplicationCommandBuilder builder = new ApplicationCommandBuilder(AdminAppCommands.Command, "Discord Core Admin Commands", ApplicationCommandType.ChatInput)
            .AddDefaultPermissions(PermissionFlags.None);
            builder.AllowInDirectMessages(false);
            
            AddAdminLinkCommand(builder);
            AddAdminUnlinkCommand(builder);
            AddAdminSearchGroupCommand(builder);
            AddAdminUnbanGroupCommand(builder);
            
            CommandCreate build = builder.Build();
            DiscordCommandLocalization localization = builder.BuildCommandLocalization();
            
            TemplateKey template = new("Admin");
            _local.RegisterCommandLocalizationAsync(this, template, localization, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0)).Then(_ =>
            {
                _local.ApplyCommandLocalizationsAsync(this, build, template).Then(() =>
                {
                    Client.Bot.Application.CreateGlobalCommand(Client, build);
                });
            });
        }
        
        public void AddAdminLinkCommand(ApplicationCommandBuilder builder)
        {
            builder.AddSubCommand(AdminAppCommands.LinkCommand, "admin link player game account and Discord user", sub =>
            {
                sub.AddOption(CommandOptionType.String, PlayerArg, "player to link",
                options => options.AutoComplete().Required());
                
                sub.AddOption(CommandOptionType.User, UserArg, "user to link",
                options => options.Required());
            });
        }
        
        public void AddAdminUnlinkCommand(ApplicationCommandBuilder builder)
        {
            builder.AddSubCommand(AdminAppCommands.UnlinkCommand, "admin unlink player game account and Discord user", sub =>
            {
                sub.AddOption(CommandOptionType.String, PlayerArg, "player to unlink",
                options => options.AutoComplete());
                
                sub.AddOption(CommandOptionType.User, UserArg, "user to unlink");
            });
        }
        
        public void AddAdminSearchGroupCommand(ApplicationCommandBuilder builder)
        {
            builder.AddSubCommandGroup(AdminAppCommands.SearchCommand, "search linked accounts by discord or player", group =>
            {
                AddAdminSearchByPlayerCommand(group);
                AddAdminSearchByUserCommand(group);
            });
        }
        
        public void AddAdminSearchByPlayerCommand(ApplicationCommandGroupBuilder builder)
        {
            builder.AddSubCommand(AdminAppCommands.PlayerCommand, "search by player", sub =>
            {
                sub.AddOption(CommandOptionType.String, PlayerArg, "player to search",
                options => options.AutoComplete());
            });
        }
        
        public void AddAdminSearchByUserCommand(ApplicationCommandGroupBuilder builder)
        {
            builder.AddSubCommand(AdminAppCommands.UserCommand, "search by user", sub =>
            {
                sub.AddOption(CommandOptionType.User, UserArg, "user to search");
            });
        }
        
        public void AddAdminUnbanGroupCommand(ApplicationCommandBuilder builder)
        {
            builder.AddSubCommandGroup(AdminAppCommands.Unban, "unban player who is link banned", group =>
            {
                AddAdminUnbanByPlayerCommand(group);
                AddAdminUnbanByUserCommand(group);
            });
        }
        
        public void AddAdminUnbanByPlayerCommand(ApplicationCommandGroupBuilder builder)
        {
            builder.AddSubCommand(AdminAppCommands.PlayerCommand, "unban by player", sub =>
            {
                sub.AddOption(CommandOptionType.String, PlayerArg, "player to unban",
                options => options.AutoComplete());
            });
        }
        
        public void AddAdminUnbanByUserCommand(ApplicationCommandGroupBuilder builder)
        {
            builder.AddSubCommand(AdminAppCommands.UserCommand, "unban by user", sub =>
            {
                sub.AddOption(CommandOptionType.User, UserArg, "user to unban");
            });
        }
        
        // ReSharper disable once UnusedMember.Local
        [DiscordApplicationCommand(AdminAppCommands.Command, AdminAppCommands.LinkCommand)]
        private void DiscordAdminLinkCommand(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            string playerId = parsed.Args.GetString(PlayerArg);
            DiscordUser user = parsed.Args.GetUser(UserArg);
            IPlayer player = players.FindPlayerById(playerId);
            if (player == null)
            {
                SendTemplateMessage(TemplateKeys.Commands.Admin.Link.Error.PlayerNotFound, interaction, GetDefault(ServerPlayerCache.Instance.GetPlayerById(playerId), user));
                return;
            }
            
            if (player.IsLinked())
            {
                SendTemplateMessage(TemplateKeys.Commands.Admin.Link.Error.PlayerAlreadyLinked, interaction, GetDefault(player, user));
                return;
            }
            
            if (user.IsLinked())
            {
                SendTemplateMessage(TemplateKeys.Commands.Admin.Link.Error.UserAlreadyLinked, interaction, GetDefault(player, user));
                return;
            }
            
            _linkHandler.HandleLink(player, user, LinkReason.Admin, null);
            SendTemplateMessage(TemplateKeys.Commands.Admin.Link.Success, interaction, GetDefault(player, user));
        }
        
        // ReSharper disable once UnusedMember.Local
        [DiscordApplicationCommand(AdminAppCommands.Command, AdminAppCommands.UnlinkCommand)]
        private void DiscordAdminUnlinkCommand(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            string playerId = parsed.Args.GetString(PlayerArg);
            IPlayer player = players.FindPlayerById(playerId);
            DiscordUser user = parsed.Args.GetUser(UserArg);
            
            if (player == null && user == null)
            {
                SendTemplateMessage(TemplateKeys.Commands.Admin.Unlink.Error.MustSpecifyOne, interaction, GetDefault(ServerPlayerCache.Instance.GetPlayerById(playerId)));
                return;
            }
            
            bool isPlayerLinked = player?.IsLinked() ?? false;
            bool isUserLinked = user?.IsLinked() ?? false;
            
            if (player != null && !isPlayerLinked)
            {
                SendTemplateMessage(TemplateKeys.Commands.Admin.Unlink.Error.PlayerIsNotLinked, interaction, GetDefault(player));
                return;
            }
            
            if (user != null && !isUserLinked)
            {
                SendTemplateMessage(TemplateKeys.Commands.Admin.Unlink.Error.UserIsNotLinked, interaction, GetDefault(user));
                return;
            }
            
            DiscordUser linkedUser = player.GetDiscordUser();
            if (player != null && user != null && linkedUser.Id != user.Id)
            {
                IPlayer otherPlayer = user.Player;
                SendTemplateMessage(TemplateKeys.Commands.Admin.Unlink.Error.LinkNotSame, interaction, GetDefault(player, user).AddTarget(otherPlayer).AddUserTarget(linkedUser));
                return;
            }
            
            if (player != null && user == null)
            {
                user = player.GetDiscordUser();
            }
            else if (user != null && player == null)
            {
                player = user.Player;
            }
            
            _linkHandler.HandleUnlink(player, user, UnlinkedReason.Admin, null);
            SendTemplateMessage(TemplateKeys.Commands.Admin.Unlink.Success, interaction, GetDefault(player, user));
        }
        
        // ReSharper disable once UnusedMember.Local
        [DiscordApplicationCommand(AdminAppCommands.Command, AdminAppCommands.PlayerCommand, AdminAppCommands.SearchCommand)]
        private void DiscordAdminSearchByPlayer(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            string playerId = parsed.Args.GetString(PlayerArg);
            IPlayer player = !string.IsNullOrEmpty(playerId) ? players.FindPlayerById(playerId) : null;
            if (player == null)
            {
                SendTemplateMessage(TemplateKeys.Commands.Admin.Search.Error.PlayerNotFound, interaction, GetDefault().Add(PlaceholderDataKeys.NotFound, playerId));
                return;
            }
            
            DiscordUser user = player.GetDiscordUser();
            SendTemplateMessage(TemplateKeys.Commands.Admin.Search.Success, interaction, GetDefault(player, user));
        }
        
        // ReSharper disable once UnusedMember.Local
        [DiscordApplicationCommand(AdminAppCommands.Command, AdminAppCommands.UserCommand, AdminAppCommands.Unban)]
        private void DiscordAdminUnbanByUser(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            DiscordUser user = parsed.Args.GetUser(UserArg);
            if (!_banHandler.Unban(user))
            {
                SendTemplateMessage(TemplateKeys.Commands.Admin.Unban.Error.UserNotBanned, interaction, GetDefault(user));
                return;
            }
            
            SendTemplateMessage(TemplateKeys.Commands.Admin.Unban.User, interaction, GetDefault(user));
        }
        
        // ReSharper disable once UnusedMember.Local
        [DiscordApplicationCommand(AdminAppCommands.Command, AdminAppCommands.PlayerCommand, AdminAppCommands.Unban)]
        private void DiscordAdminUnbanByPlayer(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            string playerId = parsed.Args.GetString(PlayerArg);
            IPlayer player = !string.IsNullOrEmpty(playerId) ? players.FindPlayerById(playerId) : null;
            if (player == null)
            {
                SendTemplateMessage(TemplateKeys.Commands.Admin.Unban.Error.PlayerNotFound, interaction, GetDefault().Add(PlaceholderDataKeys.NotFound, playerId));
                return;
            }
            
            if (!_banHandler.Unban(player))
            {
                SendTemplateMessage(TemplateKeys.Commands.Admin.Unban.Error.PlayerNotBanned, interaction, GetDefault(player));
                return;
            }
            
            SendTemplateMessage(TemplateKeys.Commands.Admin.Unban.Player, interaction, GetDefault(player));
        }
        
        // ReSharper disable once UnusedMember.Local
        [DiscordApplicationCommand(AdminAppCommands.Command, AdminAppCommands.UserCommand, AdminAppCommands.SearchCommand)]
        private void DiscordAdminSearchByUser(DiscordInteraction interaction, InteractionDataParsed parsed)
        {
            DiscordUser user = parsed.Args.GetUser(UserArg);
            IPlayer player = user.Player;
            SendTemplateMessage(TemplateKeys.Commands.Admin.Search.Success, interaction, GetDefault(player, user));
        }
        
        // ReSharper disable once UnusedMember.Local
        [DiscordAutoCompleteCommand(AdminAppCommands.Command, PlayerArg, AdminAppCommands.PlayerCommand, AdminAppCommands.Unban)]
        [DiscordAutoCompleteCommand(AdminAppCommands.Command, PlayerArg, AdminAppCommands.PlayerCommand, AdminAppCommands.SearchCommand)]
        [DiscordAutoCompleteCommand(AdminAppCommands.Command, PlayerArg, AdminAppCommands.LinkCommand)]
        [DiscordAutoCompleteCommand(AdminAppCommands.Command, PlayerArg, AdminAppCommands.UnlinkCommand)]
        private void HandleAdminNameAutoComplete(DiscordInteraction interaction, InteractionDataOption focused)
        {
            string search = focused.GetString();
            //Puts($"HandleAdminNameAutoComplete - {search}");
            InteractionAutoCompleteBuilder response = interaction.GetAutoCompleteBuilder();
            response.AddAllOnlineFirstPlayers(search, PlayerNameFormatter.All);
            interaction.CreateResponse(Client, response);
        }
        #endregion

        #region Plugins\DiscordCore.Templates.cs
        private const string AcceptEmoji = "✅";
        private const string DeclineEmoji = "❌";
        
        public void RegisterTemplates()
        {
            RegisterAnnouncements();
            RegisterWelcomeMessages();
            RegisterCommandMessages();
            RegisterAdminCommandMessages();
            RegisterLinkMessages();
            RegisterBanMessages();
            RegisterJoinMessages();
            RegisterErrorMessages();
        }
        
        public void RegisterAnnouncements()
        {
            DiscordMessageTemplate linkCommand = CreateTemplateEmbed($"Player {DefaultKeys.Player.NamePlayerId} has {DiscordFormatting.Bold("linked")} with discord {DefaultKeys.User.Mention}", DiscordColor.Success);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Announcements.Link.Command, linkCommand, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate linkAdmin = CreateTemplateEmbed($"Player {DefaultKeys.Player.NamePlayerId} was {DiscordFormatting.Bold("linked")} with discord {DefaultKeys.User.Mention} by an admin", DiscordColor.Success);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Announcements.Link.Admin, linkAdmin, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate linkApi = CreateTemplateEmbed($"Player {DefaultKeys.Player.NamePlayerId} has {DiscordFormatting.Bold("linked")} with discord {DefaultKeys.User.Mention}", DiscordColor.Success);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Announcements.Link.Api, linkApi, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate linkGuildRejoin = CreateTemplateEmbed($"Player {DefaultKeys.Player.NamePlayerId} has {DiscordFormatting.Bold("linked")} with discord {DefaultKeys.User.Mention} because they rejoined the {DiscordFormatting.Bold(DefaultKeys.Guild.Name)} Discord server", DiscordColor.Success);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Announcements.Link.GuildRejoin, linkGuildRejoin, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate linkInactiveRejoin = CreateTemplateEmbed($"Player {DefaultKeys.Player.NamePlayerId} has {DiscordFormatting.Bold("linked")} with discord {DefaultKeys.User.Mention} because they rejoined the {DiscordFormatting.Bold(DefaultKeys.Server.Name)} game server", DiscordColor.Success);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Announcements.Link.InactiveRejoin, linkInactiveRejoin, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate unlinkCommand = CreateTemplateEmbed($"Player {DefaultKeys.Player.NamePlayerId} has {DiscordFormatting.Bold("unlinked")} from discord {DefaultKeys.User.Mention}", DiscordColor.Danger);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Announcements.Unlink.Command, unlinkCommand, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate unlinkAdmin = CreateTemplateEmbed($"Player {DefaultKeys.Player.NamePlayerId} was {DiscordFormatting.Bold("unlinked")} from discord {DefaultKeys.User.Mention} by an admin", DiscordColor.Danger);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Announcements.Unlink.Admin, unlinkAdmin, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate unlinkApi = CreateTemplateEmbed($"Player {DefaultKeys.Player.NamePlayerId}has {DiscordFormatting.Bold("unlinked")} from discord {DefaultKeys.User.Mention}", DiscordColor.Danger);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Announcements.Unlink.Api, unlinkApi, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate unlinkLeftGuild = CreateTemplateEmbed($"Player {DefaultKeys.Player.NamePlayerId} has {DiscordFormatting.Bold("unlinked")} from discord {DefaultKeys.User.Fullname}({DefaultKeys.User.Id}) because they left the {DiscordFormatting.Bold(DefaultKeys.Guild.Name)} Discord server", DiscordColor.Danger);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Announcements.Unlink.LeftGuild, unlinkLeftGuild, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate unlinkInactive = CreateTemplateEmbed($"Player {DefaultKeys.Player.NamePlayerId} has {DiscordFormatting.Bold("unlinked")} from discord {DefaultKeys.User.Fullname}({DefaultKeys.User.Id}) because they were inactive since {DefaultKeys.Timestamp.LongDateTime}", DiscordColor.Danger);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Announcements.Unlink.Inactive, unlinkInactive, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate playerBanned = CreateTemplateEmbed($"Player {DefaultKeys.Player.NamePlayerId} has been linked banned for too many declined link attempts. The players ban will end on {DefaultKeys.Timestamp.LongDateTime}.", DiscordColor.Danger);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Announcements.Ban.PlayerBanned, playerBanned, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate userBanned = CreateTemplateEmbed($"User {DefaultKeys.User.Mention} has been linked banned for too many declined link attempts. The players ban will end on {DefaultKeys.Timestamp.LongDateTime}.", DiscordColor.Danger);
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.Announcements.Ban.UserBanned, userBanned, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
        }
        
        public void RegisterWelcomeMessages()
        {
            DiscordMessageTemplate pmWelcomeMessage = CreateTemplateEmbed($"Welcome to the {DiscordFormatting.Bold(DefaultKeys.Guild.Name)} Discord server. " +
            $"If you would like to link your player and Discord accounts please click on the {DiscordFormatting.Bold("Link Accounts")} button below to start the process." +
            $"{DiscordFormatting.Underline("\nNote: You must be in game to complete the link.")}", DiscordColor.Success);
            pmWelcomeMessage.Components = new List<BaseComponentTemplate>
            {
                new ButtonTemplate("Link Accounts", ButtonStyle.Success, WelcomeMessageLinkAccountsButtonId, AcceptEmoji)
            };
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.WelcomeMessage.PmWelcomeMessage, pmWelcomeMessage, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate guildWelcomeMessage = CreateTemplateEmbed($"Welcome to the {DiscordFormatting.Bold(DefaultKeys.Guild.Name)} Discord server. " +
            "This server supports linking your Discord and in game accounts. " +
            $"If you would like to link your player and Discord accounts please click on the {DiscordFormatting.Bold("Link Accounts")} button below to start the process." +
            $"{DiscordFormatting.Underline("\nNote: You must be in game to complete the link.")}", DiscordColor.Success);
            guildWelcomeMessage.Components = new List<BaseComponentTemplate>
            {
                new ButtonTemplate("Link Accounts", ButtonStyle.Success, GuildWelcomeMessageLinkAccountsButtonId, AcceptEmoji)
            };
            _templates.RegisterGlobalTemplateAsync(this, TemplateKeys.WelcomeMessage.GuildWelcomeMessage, guildWelcomeMessage, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate welcomeMessageAlreadyLinked = CreateTemplateEmbed($"You are unable to link your {DefaultKeys.User.Mention} Discord user because you're already linked to {DefaultKeys.Player.Name}", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.WelcomeMessage.Error.AlreadyLinked, welcomeMessageAlreadyLinked, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
        }
        
        public void RegisterCommandMessages()
        {
            DiscordMessageTemplate codeSuccess = CreateTemplateEmbed($"Please join the {DiscordFormatting.Bold(DefaultKeys.Server.Name)} game server and type {DiscordFormatting.Bold($"/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} {DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.LinkCommand)} {PlaceholderKeys.LinkCode}")} in server chat.", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Code.Success, codeSuccess, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate userSuccess = CreateTemplateEmbed($"We have sent a message to {DiscordFormatting.Bold(DefaultKeys.Player.Name)} on the {DiscordFormatting.Bold(DefaultKeys.Server.Name)} server. Please follow the directions to complete your link.", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.User.Success, userSuccess, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate userInvalidPlayer = CreateTemplateEmbed($"You have not selected a valid player from the dropdown. Please try the command again.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.User.Error.PlayerIsInvalid, userInvalidPlayer, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate userNotConnected = CreateTemplateEmbed($"Player {DiscordFormatting.Bold(DefaultKeys.Player.Name)} is not connected to the {DiscordFormatting.Bold(DefaultKeys.Server.Name)} server. Please join the server and try the command again.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.User.Error.PlayerNotConnected, userNotConnected, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate leaveNotLinked = CreateTemplateEmbed($"You are not able to unlink because you are not currently linked.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Leave.Error.UserNotLinked, leaveNotLinked, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
        }
        
        public void RegisterAdminCommandMessages()
        {
            DiscordMessageTemplate playerNotFound = CreateTemplateEmbed($"Failed to link. Player with '{DiscordFormatting.Bold(DefaultKeys.Player.Name)}' ID was not found.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Link.Error.PlayerNotFound, playerNotFound, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate playerAlreadyLinked = CreateTemplateEmbed($"Failed to link. Player '{DiscordFormatting.Bold($"{DefaultKeys.Player.NamePlayerId}")}' is already linked to {DefaultKeys.User.Mention}. If you would like to link this player please unlink first.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Link.Error.PlayerAlreadyLinked, playerAlreadyLinked, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate userAlreadyLinked = CreateTemplateEmbed($"Failed to link. User {DefaultKeys.User.Mention} is already linked to {DefaultKeys.Player.NamePlayerId}. If you would like to link this user please unlink them first.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Link.Error.UserAlreadyLinked, userAlreadyLinked, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate adminLinkSuccess = CreateTemplateEmbed($"You have successfully linked Player '{DiscordFormatting.Bold($"{DefaultKeys.Player.NamePlayerId}")}' to {DefaultKeys.User.Mention}", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Link.Success, adminLinkSuccess, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate unlinkMustSpecify = CreateTemplateEmbed($"Failed to unlink. You must specify either player or user or both.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Unlink.Error.MustSpecifyOne, unlinkMustSpecify, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate unlinkPlayerNotLinked = CreateTemplateEmbed($"Failed to unlink.'{DiscordFormatting.Bold($"{DefaultKeys.Player.NamePlayerId}")}' is not linked.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Unlink.Error.PlayerIsNotLinked, unlinkPlayerNotLinked, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate unlinkUserNotLinked = CreateTemplateEmbed($"Failed to unlink. {DefaultKeys.User.Mention} is not linked.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Unlink.Error.UserIsNotLinked, unlinkUserNotLinked, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate unlinkNotSame = CreateTemplateEmbed($"Failed to unlink. The specified player and user are not linked to each other.\n" +
            $"Player '{DiscordFormatting.Bold($"{DefaultKeys.Player.NamePlayerId}")}' is linked to {DefaultKeys.UserTarget.Mention}.\n" +
            $"User {DefaultKeys.User.Mention} is linked to '{DiscordFormatting.Bold($"{DefaultKeys.PlayerTarget.Name}({DefaultKeys.PlayerTarget.Id})")}'", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Unlink.Error.LinkNotSame, unlinkNotSame, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate adminUnlinkSuccess = CreateTemplateEmbed($"You have successfully unlink Player '{DiscordFormatting.Bold($"{DefaultKeys.Player.NamePlayerId}")}' from {DefaultKeys.User.Mention}", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Unlink.Success, adminUnlinkSuccess, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate playerUnbanSuccess = CreateTemplateEmbed($"You have successfully unbanned Player '{DiscordFormatting.Bold($"{DefaultKeys.Player.NamePlayerId}")}'. The player can now link again.", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Unban.Player, playerUnbanSuccess, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate userUnbanSuccess = CreateTemplateEmbed($"You have successfully unbanned User {DefaultKeys.User.Mention}. The user can now link again.", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Unban.User, userUnbanSuccess, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate playerUnbanNotFound = CreateTemplateEmbed($"Failed to find Player with '{DiscordFormatting.Bold(PlaceholderKeys.NotFound)}' ID", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Unban.Error.PlayerNotFound, playerUnbanNotFound, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate playerUnbanNotBanned = CreateTemplateEmbed($"Failed to find unban player '{DiscordFormatting.Bold(DefaultKeys.Player.NamePlayerId)}' because they are not banned", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Unban.Error.PlayerNotBanned, playerUnbanNotBanned, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate userUnbanNotBanned = CreateTemplateEmbed($"Failed to find unban user {DefaultKeys.User.Mention} because they are not banned", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Unban.Error.UserNotBanned, userUnbanNotBanned, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate playerSearchNotFound = CreateTemplateEmbed($"Failed to find Player with '{DiscordFormatting.Bold(PlaceholderKeys.NotFound)}' ID", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Search.Error.PlayerNotFound, playerSearchNotFound, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate searchSuccess = new()
            {
                Embeds = new List<DiscordEmbedTemplate>
                {
                    new()
                    {
                        Color = DiscordColor.Danger.ToHex(),
                        Fields =
                        {
                            new DiscordEmbedFieldTemplate("Player", DefaultKeys.Player.NameClan),
                            new DiscordEmbedFieldTemplate("Player ID", DefaultKeys.Player.Id),
                            new DiscordEmbedFieldTemplate("User", DefaultKeys.User.Fullname),
                            new DiscordEmbedFieldTemplate("Is Linked", DefaultKeys.Player.IsLinked),
                        }
                    }
                },
                Components =
                {
                    new ButtonTemplate("Steam Profile", ButtonStyle.Link, DefaultKeys.Player.SteamProfile),
                    new ButtonTemplate("BattleMetrics Profile", ButtonStyle.Link, DefaultKeys.Player.BattleMetricsPlayerId),
                    new ButtonTemplate("Server Armor", ButtonStyle.Link, DefaultKeys.Player.ServerArmorProfile),
                }
            };
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Commands.Admin.Search.Success, searchSuccess, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
        }
        
        public void RegisterLinkMessages()
        {
            DiscordMessageTemplate linkCommand = CreateTemplateEmbed($"You have successfully linked {DiscordFormatting.Bold(DefaultKeys.Player.Name)} with your Discord user {DefaultKeys.User.Mention}", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Link.Completed.Command, linkCommand, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate linkAdmin = CreateTemplateEmbed($"You have been successfully linked with {DiscordFormatting.Bold(DefaultKeys.Player.Name)} and Discord user {DefaultKeys.User.Mention} by an admin", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Link.Completed.Admin, linkAdmin, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate linkApi = CreateTemplateEmbed($"You have successfully linked {DiscordFormatting.Bold(DefaultKeys.Player.Name)} with your Discord user {DefaultKeys.User.Mention}", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Link.Completed.Api, linkApi, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate linkRejoin = CreateTemplateEmbed($"Your {DiscordFormatting.Bold(DefaultKeys.Player.Name)} game account has been relinked with your Discord user {DefaultKeys.User.Mention} because you rejoined the {DiscordFormatting.Bold(DefaultKeys.Guild.Name)} Discord server", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Link.Completed.GuildRejoin, linkRejoin, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate linkInactive = CreateTemplateEmbed($"Your {DiscordFormatting.Bold(DefaultKeys.Player.Name)} game account has been relinked with your Discord user {DefaultKeys.User.Mention} because you rejoined the {DiscordFormatting.Bold(DefaultKeys.Server.Name)} game server", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Link.Completed.InactiveRejoin, linkInactive, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate unlinkCommand = CreateTemplateEmbed($"You have successfully unlinked {DiscordFormatting.Bold(DefaultKeys.Player.Name)} from your Discord user {DefaultKeys.User.Mention}", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Unlink.Completed.Command, unlinkCommand, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate unlinkAdmin = CreateTemplateEmbed($"You have successfully been unlinked {DiscordFormatting.Bold(DefaultKeys.Player.Name)} from your Discord user {DefaultKeys.User.Mention} by an admin", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Unlink.Completed.Admin, unlinkAdmin, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate unlinkApi = CreateTemplateEmbed($"You have successfully unlinked {DiscordFormatting.Bold(DefaultKeys.Player.Name)} from your Discord user {DefaultKeys.User.Mention}", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Unlink.Completed.Api, unlinkApi, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate unlinkInactive = CreateTemplateEmbed($"You have been successfully unlinked from {DiscordFormatting.Bold(DefaultKeys.Player.Name)} and Discord user {DefaultKeys.User.Mention} because you have been inactive since {DefaultKeys.Timestamp.LongDateTime}", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Unlink.Completed.Inactive, unlinkInactive, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate declineUser = CreateTemplateEmbed($"We have successfully declined the link request from {DefaultKeys.Player.Name}. We're sorry for the inconvenience.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Link.Declined.JoinWithUser, declineUser, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate declinePlayer = CreateTemplateEmbed($"{DefaultKeys.Player.Name} has declined your link request. Repeated declined attempts may result in a link ban.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Link.Declined.JoinWithPlayer, declinePlayer, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate dmLinkAccounts = CreateTemplateEmbed($"To complete the link process please join the {DiscordFormatting.Bold(DefaultKeys.Server.Name)} game server and type {DiscordFormatting.Bold($"/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} {DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.LinkCommand)} {PlaceholderKeys.LinkCode}")} in server chat.", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Link.WelcomeMessage.DmLinkAccounts, dmLinkAccounts, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate guildLinkAccounts = CreateTemplateEmbed($"To complete the link process please join the {DiscordFormatting.Bold(DefaultKeys.Server.Name)} game server and type {DiscordFormatting.Bold($"/{DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.DcCommand)} {DefaultKeys.Plugin.Lang.WithFormat(ServerLang.Commands.LinkCommand)} {PlaceholderKeys.LinkCode}")} in server chat.", DiscordColor.Success);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Link.WelcomeMessage.GuildLinkAccounts, guildLinkAccounts, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
        }
        
        public void RegisterBanMessages()
        {
            DiscordMessageTemplate banned = CreateTemplateEmbed($"You have been banned from making any more player link requests for until {DefaultKeys.Timestamp.LongDateTime} due to multiple declined requests.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Banned.PlayerBanned, banned, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
        }
        
        public void RegisterJoinMessages()
        {
            DiscordMessageTemplate byUsername = CreateTemplateEmbed($"The player {DiscordFormatting.Bold(DefaultKeys.Player.Name)} is trying to link their game account to this discord user.\n" +
            $"If you would like to accept please click on the {DiscordFormatting.Bold("Accept")} button.\n" +
            $"If you did not initiate this link please click on the {DiscordFormatting.Bold("Decline")} button", DiscordColor.Success);
            byUsername.Components = new List<BaseComponentTemplate>
            {
                new ButtonTemplate("Accept", ButtonStyle.Success, AcceptLinkButtonId, AcceptEmoji),
                new ButtonTemplate("Decline", ButtonStyle.Danger, DeclineLinkButtonId, DeclineEmoji)
            };
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Join.CompleteLink, byUsername, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
        }
        
        public void RegisterErrorMessages()
        {
            DiscordMessageTemplate userAlreadyLinked = CreateTemplateEmbed($"You are unable to link because you are already linked to player {DiscordFormatting.Bold(DefaultKeys.Player.Name)}", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Errors.UserAlreadyLinked, userAlreadyLinked, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate playerAlreadyLinked = CreateTemplateEmbed($"You are unable to link to player {DiscordFormatting.Bold(DefaultKeys.Player.Name)} because they are already linked", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Errors.PlayerAlreadyLinked, playerAlreadyLinked, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate codeActivationNotFound = CreateTemplateEmbed($"We failed to find a pending link activation for the code {DiscordFormatting.Bold(PlaceholderKeys.LinkCode.Placeholder)}. Please confirm you have the correct code and try again.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Errors.CodeActivationNotFound, codeActivationNotFound, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate lookupActivationNotFound = CreateTemplateEmbed($"We failed to find a pending link activation for user {DiscordFormatting.Bold(DefaultKeys.User.Fullname)}. Please confirm you have started that activation from the game server for this user.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Errors.LookupActivationNotFound, lookupActivationNotFound, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
            
            DiscordMessageTemplate mustBeCompletedInServer = CreateTemplateEmbed($"The link must be completed on the game server. Please join the server and use the command in came to complete the link.", DiscordColor.Danger);
            _templates.RegisterLocalizedTemplateAsync(this, TemplateKeys.Errors.MustBeCompletedInServer, mustBeCompletedInServer, new TemplateVersion(1, 0, 0), new TemplateVersion(1, 0, 0));
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
        
        public void SendTemplateMessage(TemplateKey templateName, DiscordInteraction interaction, PlaceholderData placeholders = null)
        {
            InteractionCallbackData response = new()
            {
                AllowedMentions = AllowedMentions.None
            };
            if (interaction.GuildId.HasValue)
            {
                response.Flags = MessageFlags.Ephemeral;
            }
            
            interaction.CreateTemplateResponse(Client, InteractionResponseType.ChannelMessageWithSource, templateName, response, placeholders);
        }
        
        public void SendTemplateMessage(TemplateKey templateName, DiscordUser user, IPlayer player = null, PlaceholderData placeholders = null)
        {
            AddDefaultPlaceholders(ref placeholders, user, player);
            user.SendTemplateDirectMessage(Client, templateName, _lang.GetPlayerLanguage(player).Id, new MessageCreate
            {
                AllowedMentions = AllowedMentions.None
            }, placeholders);
        }
        
        public void SendGlobalTemplateMessage(TemplateKey templateName, DiscordUser user, IPlayer player = null, PlaceholderData placeholders = null)
        {
            AddDefaultPlaceholders(ref placeholders, user, player);
            user.SendGlobalTemplateDirectMessage(Client, templateName, new MessageCreate
            {
                AllowedMentions = AllowedMentions.None
            }, placeholders);
        }
        
        public IPromise<DiscordMessage> SendGlobalTemplateMessage(TemplateKey templateName, Snowflake channelId, DiscordUser user = null, IPlayer player = null, PlaceholderData placeholders = null)
        {
            DiscordChannel channel = Guild.Channels[channelId];
            if (channel != null)
            {
                AddDefaultPlaceholders(ref placeholders, user, player);
                return channel.CreateGlobalTemplateMessage(Client, templateName, new MessageCreate
                {
                    AllowedMentions = AllowedMentions.None
                }, placeholders);
            }
            
            return Promise<DiscordMessage>.Rejected(new Exception("Channel Not Found"));
        }
        
        public void UpdateGuildTemplateMessage(TemplateKey templateName, DiscordMessage message, PlaceholderData placeholders = null)
        {
            AddDefaultPlaceholders(ref placeholders, null, null);
            message.EditGlobalTemplateMessage(Client, templateName, placeholders);
        }
        
        private void AddDefaultPlaceholders(ref PlaceholderData placeholders, DiscordUser user, IPlayer player)
        {
            placeholders = placeholders ?? GetDefault();
            placeholders.AddUser(user).AddPlayer(player).AddGuild(Guild);
        }
        #endregion

        #region Plugins\DiscordCore.Placeholders.cs
        public void RegisterPlaceholders()
        {
            if (!string.IsNullOrEmpty(_pluginConfig.ServerNameOverride))
            {
                _placeholders.RegisterPlaceholder(this, new PlaceholderKey("guild.name"), _pluginConfig.ServerNameOverride);
            }
            
            _placeholders.RegisterPlaceholder(this, PlaceholderKeys.InviteUrl, _pluginConfig.InviteUrl);
            _placeholders.RegisterPlaceholder<string>(this, PlaceholderKeys.LinkCode, PlaceholderDataKeys.Code);
            _placeholders.RegisterPlaceholder<string>(this, PlaceholderKeys.NotFound, PlaceholderDataKeys.NotFound);
        }
        
        public string LangPlaceholder(string key, PlaceholderData data)
        {
            return _placeholders.ProcessPlaceholders(Lang(key), data);
        }
        
        public PlaceholderData GetDefault()
        {
            return _placeholders.CreateData(this).AddGuild(Guild);
        }
        
        public PlaceholderData GetDefault(IPlayer player)
        {
            return GetDefault().AddPlayer(player);
        }
        
        public PlaceholderData GetDefault(DiscordUser user)
        {
            return GetDefault().AddUser(user);
        }
        
        public PlaceholderData GetDefault(IPlayer player, DiscordUser user)
        {
            return GetDefault(player).AddUser(user);
        }
        #endregion

        #region Plugins\DiscordCore.Link.cs
        public IDictionary<PlayerId, Snowflake> GetPlayerIdToDiscordIds()
        {
            Hash<PlayerId, Snowflake> data = new();
            foreach (DiscordInfo info in _pluginData.PlayerDiscordInfo.Values)
            {
                data[new PlayerId(info.PlayerId)] = info.DiscordId;
            }
            
            return data;
        }
        #endregion

        #region Plugins\DiscordCore.API.cs
        // ReSharper disable once UnusedMember.Local
        private string API_Link(IPlayer player, DiscordUser user)
        {
            if (player.IsLinked())
            {
                return ApiErrorCodes.PlayerIsLinked;
            }
            
            if (user.IsLinked())
            {
                return  ApiErrorCodes.UserIsLinked;
            }
            
            _linkHandler.HandleLink(player, user, LinkReason.Api, null);
            return null;
        }
        
        // ReSharper disable once UnusedMember.Local
        private string API_Unlink(IPlayer player, DiscordUser user)
        {
            if (!player.IsLinked())
            {
                return  ApiErrorCodes.PlayerIsNotLinked;
            }
            
            if (!user.IsLinked())
            {
                return ApiErrorCodes.UserIsNotLinked;
            }
            
            _linkHandler.HandleUnlink(player, user, UnlinkedReason.Api, null);
            return null;
        }
        #endregion

        #region Plugins\DiscordCore.Helpers.cs
        public void SaveData()
        {
            if (_pluginData != null)
            {
                Interface.Oxide.DataFileSystem.WriteObject(Name, _pluginData);
            }
        }
        #endregion

        #region Plugins\DiscordCore.DiscordSetup.cs
        //Define:FileOrder=25
        public void SetupGuildWelcomeMessage()
        {
            GuildMessageSettings settings = _pluginConfig.LinkMessageSettings;
            if (!settings.Enabled)
            {
                return;
            }
            
            if (!settings.ChannelId.IsValid())
            {
                PrintWarning("Link message is enabled but link message channel ID is not valid");
                return;
            }
            
            DiscordChannel channel = Guild.Channels[settings.ChannelId];
            if (channel == null)
            {
                PrintWarning($"Link message failed to find channel with ID {settings.ChannelId}");
                return;
            }
            
            if (_pluginData.MessageData == null)
            {
                CreateGuildWelcomeMessage(settings);
                return;
            }
            
            channel.GetMessage(Client, _pluginData.MessageData.MessageId).Then(message =>
            {
                UpdateGuildTemplateMessage(TemplateKeys.WelcomeMessage.GuildWelcomeMessage, message);
            }).Catch<ResponseError>(error =>
            {
                if (error.HttpStatusCode == DiscordHttpStatusCode.NotFound)
                {
                    error.SuppressErrorMessage();
                    PrintWarning("The previous link message has been removed. Recreating the message.");
                    CreateGuildWelcomeMessage(settings);
                }
            });
        }
        
        private void CreateGuildWelcomeMessage(GuildMessageSettings settings)
        {
            SendGlobalTemplateMessage(TemplateKeys.WelcomeMessage.GuildWelcomeMessage, settings.ChannelId).Then(message =>
            {
                _pluginData.MessageData = new LinkMessageData(message.ChannelId, message.Id);
            });
        }
        #endregion

        #region Api\ApiErrorCodes.cs
        public static class ApiErrorCodes
        {
            public const string PlayerIsLinked = "Error.Player.IsLinked";
            public const string PlayerIsNotLinked = "Error.Player.IsNotLinked";
            public const string UserIsLinked = "Error.User.IsLinked";
            public const string UserIsNotLinked = "Error.User.IsNotLinked";
        }
        #endregion

        #region AppCommands\AdminAppCommands.cs
        public static class AdminAppCommands
        {
            public const string Command = "dca";
            public const string LinkCommand = "link";
            public const string UnlinkCommand = "unlink";
            public const string SearchCommand = "search";
            public const string PlayerCommand = "player";
            public const string UserCommand = "user";
            public const string Unban = "unban";
        }
        #endregion

        #region AppCommands\UserAppCommands.cs
        public static class UserAppCommands
        {
            public const string Command = "dc";
            public const string CodeCommand = "code";
            public const string UserCommand = "user";
            public const string LeaveCommand = "leave";
            public const string LinkCommand = "link";
        }
        #endregion

        #region Configuration\GuildMessageSettings.cs
        public class GuildMessageSettings
        {
            [JsonProperty(PropertyName = "Enable Guild Link Message")]
            public bool Enabled { get; set; }
            
            [JsonProperty(PropertyName = "Message Channel ID")]
            public Snowflake ChannelId { get; set; }
            
            public GuildMessageSettings(GuildMessageSettings settings)
            {
                Enabled = settings?.Enabled ?? false;
                ChannelId = settings?.ChannelId ?? default(Snowflake);
            }
        }
        #endregion

        #region Configuration\InactiveSettings.cs
        public class InactiveSettings
        {
            [JsonProperty(PropertyName = "Automatically Unlink Inactive Players")]
            public bool UnlinkInactive { get; set; }
            
            [JsonProperty(PropertyName = "Player Considered Inactive After X (Days)")]
            public float UnlinkInactiveDays { get; set; }
            
            [JsonProperty(PropertyName = "Automatically Relink Inactive Players On Game Server Join")]
            public bool AutoRelinkInactive { get; set; }
            
            public InactiveSettings(InactiveSettings settings)
            {
                UnlinkInactive = settings?.UnlinkInactive ?? false;
                UnlinkInactiveDays = settings?.UnlinkInactiveDays ?? 90;
                AutoRelinkInactive = settings?.AutoRelinkInactive ?? true;
            }
        }
        #endregion

        #region Configuration\LinkBanSettings.cs
        public class LinkBanSettings
        {
            [JsonProperty(PropertyName = "Enable Link Ban")]
            public bool EnableLinkBanning { get; set; }
            
            [JsonProperty(PropertyName = "Ban Announcement Channel ID")]
            public Snowflake BanAnnouncementChannel { get; set; }
            
            [JsonProperty(PropertyName = "Ban Link After X Join Declines")]
            public int BanDeclineAmount { get; set; }
            
            [JsonProperty(PropertyName = "Ban Duration (Hours)")]
            public int BanDuration { get; set; }
            
            public LinkBanSettings(LinkBanSettings settings)
            {
                EnableLinkBanning = settings?.EnableLinkBanning ?? true;
                BanAnnouncementChannel = settings?.BanAnnouncementChannel ?? default(Snowflake);
                BanDeclineAmount = settings?.BanDeclineAmount ?? 3;
                BanDuration = settings?.BanDuration ?? 24;
            }
        }
        #endregion

        #region Configuration\LinkPermissionSettings.cs
        public class LinkPermissionSettings
        {
            [JsonProperty(PropertyName = "On Link Server Permissions To Add")]
            public List<string> LinkPermissions { get; set; }
            
            [JsonProperty(PropertyName = "On Unlink Server Permissions To Remove")]
            public List<string> UnlinkPermissions { get; set; }
            
            [JsonProperty(PropertyName = "On Link Server Groups To Add")]
            public List<string> LinkGroups { get; set; }
            
            [JsonProperty(PropertyName = "On Unlink Server Groups To Remove")]
            public List<string> UnlinkGroups { get; set; }
            
            [JsonProperty(PropertyName = "On Link Discord Roles To Add")]
            public List<Snowflake> LinkRoles { get; set; }
            
            [JsonProperty(PropertyName = "On Unlink Discord Roles To Remove")]
            public List<Snowflake> UnlinkRoles { get; set; }
            
            public LinkPermissionSettings(LinkPermissionSettings settings)
            {
                LinkPermissions = settings?.LinkPermissions ?? new List<string>();
                LinkGroups = settings?.LinkGroups ?? new List<string>();
                LinkRoles = settings?.LinkRoles ?? new List<Snowflake>();
                UnlinkPermissions = settings?.UnlinkPermissions ?? new List<string>();
                UnlinkGroups = settings?.UnlinkGroups ?? new List<string>();
                UnlinkRoles = settings?.UnlinkRoles ?? new List<Snowflake>();
            }
        }
        #endregion

        #region Configuration\LinkSettings.cs
        public class LinkSettings
        {
            [JsonProperty(PropertyName = "Announcement Channel Id")]
            public Snowflake AnnouncementChannel { get; set; }
            
            [JsonProperty(PropertyName = "Link Code Generator Characters")]
            public string LinkCodeCharacters { get; set; }
            
            [JsonProperty(PropertyName = "Link Code Generator Length")]
            public int LinkCodeLength { get; set; }
            
            [JsonProperty(PropertyName = "Automatically Relink A Player If They Leave And Rejoin The Discord Server")]
            public bool AutoRelinkPlayer { get; set; }
            
            [JsonProperty(PropertyName = "Inactive Settings")]
            public InactiveSettings InactiveSettings { get; set; }
            
            public LinkSettings(LinkSettings settings)
            {
                AnnouncementChannel = settings?.AnnouncementChannel ?? default(Snowflake);
                LinkCodeCharacters = settings?.LinkCodeCharacters ?? "123456789";
                LinkCodeLength = settings?.LinkCodeLength ?? 6;
                if (LinkCodeLength <= 0)
                {
                    LinkCodeLength = 6;
                }
                AutoRelinkPlayer = settings?.AutoRelinkPlayer ?? true;
                InactiveSettings = new InactiveSettings(settings?.InactiveSettings);
            }
        }
        #endregion

        #region Configuration\PluginConfig.cs
        public class PluginConfig
        {
            [DefaultValue("")]
            [JsonProperty(PropertyName = "Discord Bot Token")]
            public string ApiKey { get; set; }
            
            [JsonProperty(PropertyName = "Discord Server ID (Optional if bot only in 1 guild)")]
            public Snowflake GuildId { get; set; }
            
            [DefaultValue("")]
            [JsonProperty(PropertyName = "Discord Server Name Override")]
            public string ServerNameOverride { get; set; }
            
            [DefaultValue("")]
            [JsonProperty(PropertyName = "Discord Server Invite Url")]
            public string InviteUrl { get; set; }
            
            [JsonProperty(PropertyName = "Link Settings")]
            public LinkSettings LinkSettings { get; set; }
            
            [JsonProperty(PropertyName = "Welcome Message Settings")]
            public WelcomeMessageSettings WelcomeMessageSettings { get; set; }
            
            [JsonProperty(PropertyName = "Guild Link Message Settings")]
            public GuildMessageSettings LinkMessageSettings { get; set; }
            
            [JsonProperty(PropertyName = "Link Permission Settings")]
            public LinkPermissionSettings PermissionSettings { get; set; }
            
            [JsonProperty(PropertyName = "Link Ban Settings")]
            public LinkBanSettings LinkBanSettings { get; set; }
            
            [JsonConverter(typeof(StringEnumConverter))]
            [DefaultValue(DiscordLogLevel.Info)]
            [JsonProperty(PropertyName = "Discord Extension Log Level (Verbose, Debug, Info, Warning, Error, Exception, Off)")]
            public DiscordLogLevel ExtensionDebugging { get; set; }
        }
        #endregion

        #region Configuration\WelcomeMessageSettings.cs
        public class WelcomeMessageSettings
        {
            [JsonProperty(PropertyName = "Enable Welcome DM Message")]
            public bool EnableWelcomeMessage { get; set; }
            
            [JsonProperty(PropertyName = "Send Welcome Message On Discord Server Join")]
            public bool SendOnGuildJoin { get; set; }
            
            [JsonProperty(PropertyName = "Send Welcome Message On Role ID Added")]
            public List<Snowflake> SendOnRoleAdded { get; set; }
            
            [JsonProperty(PropertyName = "Add Link Accounts Button In Welcome Message")]
            public bool EnableLinkButton { get; set; }
            
            public WelcomeMessageSettings(WelcomeMessageSettings settings)
            {
                EnableWelcomeMessage = settings?.EnableWelcomeMessage ?? true;
                SendOnGuildJoin = settings?.SendOnGuildJoin ?? false;
                SendOnRoleAdded = settings?.SendOnRoleAdded ?? new List<Snowflake> {new(1234567890)};
                EnableLinkButton = settings?.EnableLinkButton ?? true;
            }
        }
        #endregion

        #region Data\DiscordInfo.cs
        public class DiscordInfo
        {
            public Snowflake DiscordId { get; set; }
            public string PlayerId { get; set; }
            public DateTime LastOnline { get; set; } = DateTime.UtcNow;
            
            [JsonConstructor]
            public DiscordInfo() { }
            
            public DiscordInfo(IPlayer player, DiscordUser user)
            {
                PlayerId = player.Id;
                DiscordId = user.Id;
            }
        }
        #endregion

        #region Data\LinkMessageData.cs
        public class LinkMessageData
        {
            public Snowflake ChannelId { get; set; }
            public Snowflake MessageId { get; set; }
            
            [JsonConstructor]
            public LinkMessageData() { }
            
            public LinkMessageData(Snowflake channelId, Snowflake messageId)
            {
                ChannelId = channelId;
                MessageId = messageId;
            }
        }
        #endregion

        #region Data\PluginData.cs
        public class PluginData
        {
            public Hash<string, DiscordInfo> PlayerDiscordInfo = new();
            public Hash<Snowflake, DiscordInfo> LeftPlayerInfo = new();
            public Hash<string, DiscordInfo> InactivePlayerInfo = new();
            public LinkMessageData MessageData;
        }
        #endregion

        #region Enums\JoinSource.cs
        public enum JoinSource
        {
            Server,
            Discord
        }
        #endregion

        #region Enums\LinkReason.cs
        public enum LinkReason
        {
            Command,
            Admin,
            Api,
            GuildRejoin,
            InactiveRejoin
        }
        #endregion

        #region Enums\UnlinkedReason.cs
        public enum UnlinkedReason
        {
            Command,
            Admin,
            Api,
            LeftGuild,
            Inactive
        }
        #endregion

        #region Link\JoinBanData.cs
        public class JoinBanData
        {
            public int Times { get; private set; }
            private DateTime _bannedUntil;
            
            public void AddDeclined()
            {
                Times++;
            }
            
            public bool IsBanned()
            {
                return _bannedUntil > DateTime.UtcNow;
            }
            
            public TimeSpan GetRemainingBan()
            {
                return _bannedUntil - DateTime.UtcNow;
            }
            
            public void SetBanDuration(float hours)
            {
                _bannedUntil = DateTime.UtcNow.AddHours(hours);
            }
        }
        #endregion

        #region Link\JoinBanHandler.cs
        public class JoinBanHandler
        {
            private readonly Hash<string, JoinBanData> _playerBans = new();
            private readonly Hash<Snowflake, JoinBanData> _discordBans = new();
            private readonly LinkBanSettings _settings;
            
            public JoinBanHandler(LinkBanSettings settings)
            {
                _settings = settings;
            }
            
            public void AddBan(IPlayer player)
            {
                if (!_settings.EnableLinkBanning)
                {
                    return;
                }
                
                JoinBanData ban = GetBan(player);
                
                ban.AddDeclined();
                if (ban.Times >= _settings.BanDeclineAmount)
                {
                    ban.SetBanDuration(_settings.BanDuration);
                    DiscordCore.Instance.SendGlobalTemplateMessage(TemplateKeys.Announcements.Ban.PlayerBanned, _settings.BanAnnouncementChannel, null, player, DiscordCore.Instance.GetDefault(player).AddTimestamp(GetBannedEndDate(player)));
                }
            }
            
            public void AddBan(DiscordUser user)
            {
                if (!_settings.EnableLinkBanning)
                {
                    return;
                }
                
                JoinBanData ban = GetBan(user);
                
                ban.AddDeclined();
                if (ban.Times >= _settings.BanDeclineAmount)
                {
                    ban.SetBanDuration(_settings.BanDuration);
                    DiscordCore.Instance.SendGlobalTemplateMessage(TemplateKeys.Announcements.Ban.UserBanned, _settings.BanAnnouncementChannel, user, null, DiscordCore.Instance.GetDefault(user).AddTimestamp(GetBannedEndDate(user)));
                }
            }
            
            public bool Unban(IPlayer player)
            {
                return _playerBans.Remove(player.Id);
            }
            
            public bool Unban(DiscordUser user)
            {
                return _discordBans.Remove(user.Id);
            }
            
            public bool IsBanned(IPlayer player)
            {
                if (!_settings.EnableLinkBanning)
                {
                    return false;
                }
                
                JoinBanData ban = _playerBans[player.Id];
                return ban != null && ban.IsBanned();
            }
            
            public bool IsBanned(DiscordUser user)
            {
                if (!_settings.EnableLinkBanning)
                {
                    return false;
                }
                
                JoinBanData ban = _discordBans[user.Id];
                return ban != null && ban.IsBanned();
            }
            
            public TimeSpan GetRemainingDuration(IPlayer player)
            {
                if (!_settings.EnableLinkBanning)
                {
                    return TimeSpan.Zero;
                }
                
                return _playerBans[player.Id]?.GetRemainingBan() ?? TimeSpan.Zero;
            }
            
            public DateTimeOffset GetBannedEndDate(IPlayer player)
            {
                return DateTimeOffset.UtcNow + GetRemainingDuration(player);
            }
            
            public TimeSpan GetRemainingDuration(DiscordUser user)
            {
                if (!_settings.EnableLinkBanning)
                {
                    return TimeSpan.Zero;
                }
                
                return _discordBans[user.Id]?.GetRemainingBan() ?? TimeSpan.Zero;
            }
            
            public DateTimeOffset GetBannedEndDate(DiscordUser user)
            {
                return DateTimeOffset.UtcNow + GetRemainingDuration(user);
            }
            
            private JoinBanData GetBan(IPlayer player)
            {
                JoinBanData ban = _playerBans[player.Id];
                if (ban == null)
                {
                    ban = new JoinBanData();
                    _playerBans[player.Id] = ban;
                }
                
                return ban;
            }
            
            private JoinBanData GetBan(DiscordUser user)
            {
                JoinBanData ban = _discordBans[user.Id];
                if (ban == null)
                {
                    ban = new JoinBanData();
                    _discordBans[user.Id] = ban;
                }
                
                return ban;
            }
        }
        #endregion

        #region Link\JoinData.cs
        public class JoinData
        {
            public IPlayer Player { get; set; }
            public DiscordUser Discord { get; set; }
            public string Code { get; set; }
            public JoinSource From { get; }
            
            public JoinData(JoinSource from)
            {
                From = from;
            }
            
            public bool IsCompleted() => Player != null && Discord != null && Discord.Id.IsValid();
            
            public bool IsMatch(IPlayer player) => Player != null && player != null && Player.Id == player.Id;
            
            public bool IsMatch(DiscordUser user) => Discord != null && user != null && Discord.Id == user.Id;
        }
        #endregion

        #region Link\JoinHandler.cs
        public class JoinHandler
        {
            private readonly List<JoinData> _activations = new();
            private readonly LinkSettings _settings;
            private readonly LinkHandler _linkHandler;
            private readonly JoinBanHandler _ban;
            private readonly DiscordCore _plugin = DiscordCore.Instance;
            private readonly StringBuilder _sb = new();
            
            public JoinHandler(LinkSettings settings, LinkHandler linkHandler, JoinBanHandler ban)
            {
                _settings = settings;
                _linkHandler = linkHandler;
                _ban = ban;
            }
            
            public JoinData FindByCode(string code)
            {
                for (int index = 0; index < _activations.Count; index++)
                {
                    JoinData activation = _activations[index];
                    if (activation.Code?.Equals(code, StringComparison.OrdinalIgnoreCase) ?? false)
                    {
                        return activation;
                    }
                }
                
                return null;
            }
            
            public JoinData FindCompletedByPlayer(IPlayer player)
            {
                for (int index = 0; index < _activations.Count; index++)
                {
                    JoinData activation = _activations[index];
                    if (activation.IsCompleted() && activation.Player.Id.Equals(player.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        return activation;
                    }
                }
                
                return null;
            }
            
            public JoinData FindCompletedByUser(DiscordUser user)
            {
                for (int index = 0; index < _activations.Count; index++)
                {
                    JoinData activation = _activations[index];
                    if (activation.IsCompleted() && activation.Discord.Id == user.Id)
                    {
                        return activation;
                    }
                }
                
                return null;
            }
            
            public void RemoveByPlayer(IPlayer player)
            {
                for (int index = _activations.Count - 1; index >= 0; index--)
                {
                    JoinData activation = _activations[index];
                    if (activation.IsMatch(player))
                    {
                        _activations.RemoveAt(index);
                    }
                }
            }
            
            public void RemoveByUser(DiscordUser user)
            {
                for (int index = _activations.Count - 1; index >= 0; index--)
                {
                    JoinData activation = _activations[index];
                    if (activation.IsMatch(user))
                    {
                        _activations.RemoveAt(index);
                    }
                }
            }
            
            public JoinData CreateActivation(IPlayer player)
            {
                if (player == null) throw new ArgumentNullException(nameof(player));
                
                RemoveByPlayer(player);
                JoinData activation = new(JoinSource.Server)
                {
                    Code = GenerateCode(),
                    Player = player
                };
                _activations.Add(activation);
                return activation;
            }
            
            public JoinData CreateActivation(DiscordUser user)
            {
                if (user == null) throw new ArgumentNullException(nameof(user));
                
                RemoveByUser(user);
                JoinData activation = new(JoinSource.Discord)
                {
                    Code = GenerateCode(),
                    Discord = user
                };
                _activations.Add(activation);
                return activation;
            }
            
            public JoinData CreateActivation(IPlayer player, DiscordUser user, JoinSource from)
            {
                if (user == null) throw new ArgumentNullException(nameof(user));
                
                RemoveByPlayer(player);
                RemoveByUser(user);
                JoinData activation = new(from)
                {
                    Discord = user,
                    Player = player
                };
                _activations.Add(activation);
                return activation;
            }
            
            private string GenerateCode()
            {
                _sb.Clear();
                for (int i = 0; i < _settings.LinkCodeLength; i++)
                {
                    _sb.Append(_settings.LinkCodeCharacters[Oxide.Core.Random.Range(0, _settings.LinkCodeCharacters.Length)]);
                }
                
                return _sb.ToString();
            }
            
            public void CompleteLink(JoinData data, DiscordInteraction interaction)
            {
                IPlayer player = data.Player;
                DiscordUser user = data.Discord;
                
                _activations.Remove(data);
                RemoveByPlayer(data.Player);
                RemoveByUser(data.Discord);
                
                _linkHandler.HandleLink(player, user, LinkReason.Command, interaction);
            }
            
            public void DeclineLink(JoinData data, DiscordInteraction interaction)
            {
                _activations.Remove(data);
                
                if (data.From == JoinSource.Server)
                {
                    _ban.AddBan(data.Player);
                    RemoveByPlayer(data.Player);
                    using (PlaceholderData placeholders = _plugin.GetDefault(data.Player, data.Discord))
                    {
                        placeholders.ManualPool();
                        _plugin.Chat(data.Player, ServerLang.Link.Declined.JoinWithUser, placeholders);
                        _plugin.SendTemplateMessage(TemplateKeys.Link.Declined.JoinWithUser, interaction, placeholders);
                    }
                }
                else if (data.From == JoinSource.Discord)
                {
                    _ban.AddBan(data.Discord);
                    RemoveByUser(data.Discord);
                    using (PlaceholderData placeholders = _plugin.GetDefault(data.Player, data.Discord))
                    {
                        placeholders.ManualPool();
                        _plugin.Chat(data.Player, ServerLang.Link.Declined.JoinWithPlayer, placeholders);
                        _plugin.SendTemplateMessage(TemplateKeys.Link.Declined.JoinWithPlayer, data.Discord, data.Player, placeholders);
                    }
                }
            }
        }
        #endregion

        #region Link\LinkHandler.cs
        public class LinkHandler
        {
            private readonly PluginData _pluginData;
            private readonly LinkPermissionSettings _permissionSettings;
            private readonly LinkSettings _settings;
            private readonly DiscordLink _link = Interface.Oxide.GetLibrary<DiscordLink>();
            private readonly IPlayerManager _players = Interface.Oxide.GetLibrary<Covalence>().Players;
            private readonly DiscordCore _plugin = DiscordCore.Instance;
            private readonly Hash<LinkReason, LinkMessage> _linkMessages = new();
            private readonly Hash<UnlinkedReason, LinkMessage> _unlinkMessages = new();
            
            public LinkHandler(PluginData pluginData, PluginConfig config)
            {
                _pluginData = pluginData;
                _settings = config.LinkSettings;
                _permissionSettings = config.PermissionSettings;
                LinkSettings link = config.LinkSettings;
                
                _linkMessages[LinkReason.Command] = new LinkMessage(ServerLang.Link.Completed.Command, ServerLang.Announcements.Link.Command, TemplateKeys.Link.Completed.Command, TemplateKeys.Announcements.Link.Command, _plugin, link);
                _linkMessages[LinkReason.Admin] = new LinkMessage(ServerLang.Link.Completed.Admin, ServerLang.Announcements.Link.Admin, TemplateKeys.Link.Completed.Admin, TemplateKeys.Announcements.Link.Admin, _plugin, link);
                _linkMessages[LinkReason.Api] = new LinkMessage(ServerLang.Link.Completed.Api, ServerLang.Announcements.Link.Api, TemplateKeys.Link.Completed.Api, TemplateKeys.Announcements.Link.Api, _plugin, link);
                _linkMessages[LinkReason.GuildRejoin] = new LinkMessage(ServerLang.Link.Completed.GuildRejoin, ServerLang.Announcements.Link.GuildRejoin, TemplateKeys.Link.Completed.GuildRejoin, TemplateKeys.Announcements.Link.GuildRejoin, _plugin, link);
                _linkMessages[LinkReason.InactiveRejoin] = new LinkMessage(ServerLang.Link.Completed.InactiveRejoin, ServerLang.Announcements.Link.InactiveRejoin, TemplateKeys.Link.Completed.InactiveRejoin, TemplateKeys.Announcements.Link.InactiveRejoin, _plugin, link);
                
                _unlinkMessages[UnlinkedReason.Command] = new LinkMessage(ServerLang.Unlink.Completed.Command, ServerLang.Announcements.Unlink.Command, TemplateKeys.Unlink.Completed.Command, TemplateKeys.Announcements.Unlink.Command, _plugin, link);
                _unlinkMessages[UnlinkedReason.Admin] = new LinkMessage(ServerLang.Unlink.Completed.Admin, ServerLang.Announcements.Unlink.Admin, TemplateKeys.Unlink.Completed.Admin, TemplateKeys.Announcements.Unlink.Admin, _plugin, link);
                _unlinkMessages[UnlinkedReason.Api] = new LinkMessage(ServerLang.Unlink.Completed.Api, ServerLang.Announcements.Unlink.Api, TemplateKeys.Unlink.Completed.Api, TemplateKeys.Announcements.Unlink.Api, _plugin, link);
                _unlinkMessages[UnlinkedReason.LeftGuild] = new LinkMessage(ServerLang.Unlink.Completed.LeftGuild, ServerLang.Announcements.Unlink.LeftGuild, default, TemplateKeys.Announcements.Unlink.LeftGuild, _plugin, link);
                _unlinkMessages[UnlinkedReason.Inactive] = new LinkMessage(null, ServerLang.Announcements.Unlink.Inactive, TemplateKeys.Unlink.Completed.Inactive, TemplateKeys.Announcements.Unlink.Inactive, _plugin, link);
            }
            
            public void HandleLink(IPlayer player, DiscordUser user, LinkReason reason, DiscordInteraction interaction)
            {
                if (player == null) throw new ArgumentNullException(nameof(player));
                if (user == null) throw new ArgumentNullException(nameof(user));
                _pluginData.InactivePlayerInfo.Remove(player.Id);
                _pluginData.LeftPlayerInfo.Remove(user.Id);
                _pluginData.PlayerDiscordInfo[player.Id] = new DiscordInfo(player, user);
                _link.OnLinked(_plugin, player, user);
                PlaceholderData data = _plugin.GetDefault(player, user);
                _linkMessages[reason]?.SendMessages(player, user, interaction, data);
                AddPermissions(player, user);
                _plugin.SaveData();
            }
            
            public void HandleUnlink(IPlayer player, DiscordUser user, UnlinkedReason reason, DiscordInteraction interaction)
            {
                if (player == null || user == null || !user.Id.IsValid())
                {
                    return;
                }
                
                DiscordInfo info = _pluginData.PlayerDiscordInfo[player.Id];
                if (info == null)
                {
                    return;
                }
                
                PlaceholderData data = _plugin.GetDefault(player, user);
                if (reason == UnlinkedReason.LeftGuild)
                {
                    _pluginData.LeftPlayerInfo[info.DiscordId] = info;
                }
                else if (reason == UnlinkedReason.Inactive)
                {
                    _pluginData.InactivePlayerInfo[info.PlayerId] = info;
                    data.AddTimeSpan(TimeSpan.FromDays(_settings.InactiveSettings.UnlinkInactiveDays));
                }
                
                _pluginData.PlayerDiscordInfo.Remove(player.Id);
                _link.OnUnlinked(_plugin, player, user);
                _unlinkMessages[reason]?.SendMessages(player, user, interaction, data);
                RemovePermissions(player, user, reason);
                _plugin.SaveData();
            }
            
            public void OnUserConnected(IPlayer player)
            {
                DiscordInfo info = _pluginData.PlayerDiscordInfo[player.Id];
                if (info != null)
                {
                    info.LastOnline = DateTime.UtcNow;
                    return;
                }
                
                if (_settings.InactiveSettings.AutoRelinkInactive)
                {
                    info = _pluginData.InactivePlayerInfo[player.Id];
                    if (info != null)
                    {
                        info.LastOnline = DateTime.UtcNow;
                        DiscordUser user = _plugin.Guild.Members[info.DiscordId]?.User;
                        if (user == null)
                        {
                            _pluginData.LeftPlayerInfo[info.DiscordId] = info;
                            return;
                        }
                        
                        HandleLink(player, user, LinkReason.InactiveRejoin, null);
                    }
                }
            }
            
            public void OnGuildMemberLeft(DiscordUser user)
            {
                IPlayer player = user.Player;
                if (player != null)
                {
                    HandleUnlink(player, user, UnlinkedReason.LeftGuild, null);
                }
            }
            
            public void OnGuildMemberJoin(DiscordUser user)
            {
                if (!_settings.AutoRelinkPlayer)
                {
                    return;
                }
                
                DiscordInfo info = _pluginData.LeftPlayerInfo[user.Id];
                if (info == null)
                {
                    return;
                }
                
                _pluginData.PlayerDiscordInfo[info.PlayerId] = info;
                _pluginData.LeftPlayerInfo.Remove(info.DiscordId);
                
                IPlayer player = _players.FindPlayerById(info.PlayerId);
                if (player == null)
                {
                    return;
                }
                
                HandleLink(player, user, LinkReason.GuildRejoin, null);
            }
            
            public void ProcessLeaveAndRejoin()
            {
                foreach (DiscordInfo info in _pluginData.PlayerDiscordInfo.Values.ToList())
                {
                    if (!_plugin.Guild.Members.ContainsKey(info.DiscordId))
                    {
                        IPlayer player = _link.GetPlayer(info.DiscordId);
                        if (player != null)
                        {
                            DiscordUser user = player.GetDiscordUser();
                            HandleUnlink(player, user, UnlinkedReason.LeftGuild, null);
                        }
                    }
                    
                    if (_settings.InactiveSettings.UnlinkInactive && info.LastOnline + TimeSpan.FromDays(_settings.InactiveSettings.UnlinkInactiveDays) < DateTime.UtcNow)
                    {
                        IPlayer player = _link.GetPlayer(info.DiscordId);
                        if (player != null)
                        {
                            DiscordUser user = player.GetDiscordUser();
                            HandleUnlink(player, user, UnlinkedReason.LeftGuild, null);
                        }
                    }
                }
                
                if (_settings.AutoRelinkPlayer)
                {
                    foreach (DiscordInfo info in _pluginData.LeftPlayerInfo.Values.ToList())
                    {
                        GuildMember member = _plugin.Guild.Members[info.DiscordId];
                        if (member != null)
                        {
                            OnGuildMemberJoin(member.User);
                        }
                    }
                }
            }
            
            private void AddPermissions(IPlayer player, DiscordUser user)
            {
                for (int index = 0; index < _permissionSettings.LinkPermissions.Count; index++)
                {
                    string permission = _permissionSettings.LinkPermissions[index];
                    player.GrantPermission(permission);
                }
                
                for (int index = 0; index < _permissionSettings.LinkGroups.Count; index++)
                {
                    string group = _permissionSettings.LinkGroups[index];
                    player.AddToGroup(group);
                }
                
                for (int index = 0; index < _permissionSettings.LinkRoles.Count; index++)
                {
                    Snowflake role = _permissionSettings.LinkRoles[index];
                    DiscordCore.Instance.Guild.AddMemberRole(_plugin.Client, user.Id, role);
                }
            }
            
            private void RemovePermissions(IPlayer player, DiscordUser user, UnlinkedReason reason)
            {
                for (int index = 0; index < _permissionSettings.UnlinkPermissions.Count; index++)
                {
                    string permission = _permissionSettings.UnlinkPermissions[index];
                    player.RevokePermission(permission);
                }
                
                for (int index = 0; index < _permissionSettings.UnlinkGroups.Count; index++)
                {
                    string group = _permissionSettings.UnlinkGroups[index];
                    player.RemoveFromGroup(group);
                }
                
                if (reason != UnlinkedReason.LeftGuild)
                {
                    for (int index = 0; index < _permissionSettings.UnlinkRoles.Count; index++)
                    {
                        Snowflake role = _permissionSettings.UnlinkRoles[index];
                        DiscordCore.Instance.Guild.RemoveMemberRole(_plugin.Client, user.Id, role);
                    }
                }
            }
        }
        #endregion

        #region Link\LinkMessage.cs
        public class LinkMessage
        {
            private readonly string _chatLang;
            private readonly string _chatAnnouncement;
            private readonly TemplateKey _discordTemplate;
            private readonly TemplateKey _announcementTemplate;
            private readonly DiscordCore _plugin;
            private readonly LinkSettings _link;
            
            public LinkMessage(string chatLang, string chatAnnouncement, TemplateKey discordTemplate, TemplateKey announcementTemplate, DiscordCore plugin, LinkSettings link)
            {
                _chatLang = chatLang;
                _chatAnnouncement = chatAnnouncement;
                _discordTemplate = discordTemplate;
                _announcementTemplate = announcementTemplate;
                _plugin = plugin;
                _link = link;
            }
            
            public void SendMessages(IPlayer player, DiscordUser user, DiscordInteraction interaction, PlaceholderData data)
            {
                using (data)
                {
                    data.ManualPool();
                    _plugin.BroadcastMessage(_chatAnnouncement, data);
                    _plugin.Chat(player, _chatLang, data);
                    if (_discordTemplate.IsValid)
                    {
                        if (interaction != null)
                        {
                            _plugin.SendTemplateMessage(_discordTemplate, interaction, data);
                        }
                        else
                        {
                            _plugin.SendTemplateMessage(_discordTemplate, user, player, data);
                        }
                    }
                    
                    _plugin.SendGlobalTemplateMessage(_announcementTemplate, _link.AnnouncementChannel, user, player, data);
                }
            }
        }
        #endregion

        #region Localization\ServerLang.cs
        public static class ServerLang
        {
            public const string Format = nameof(Format);
            public const string NoPermission = nameof(NoPermission);
            
            public static class Announcements
            {
                private const string Base = nameof(Announcements) + ".";
                
                public static class Link
                {
                    private const string Base = Announcements.Base + nameof(Link) + ".";
                    public const string Command = Base + nameof(Command);
                    public const string Admin = Base + nameof(Admin);
                    public const string Api = Base + nameof(Api);
                    public const string GuildRejoin = Base + nameof(GuildRejoin);
                    public const string InactiveRejoin = Base + nameof(InactiveRejoin);
                }
                
                public static class Unlink
                {
                    private const string Base = Announcements.Base + nameof(Unlink) + ".";
                    
                    public const string Command = Base + nameof(Command);
                    public const string Admin = Base + nameof(Admin);
                    public const string Api = Base + nameof(Api);
                    public const string LeftGuild = Base + nameof(LeftGuild);
                    public const string Inactive = Base + nameof(Inactive);
                }
            }
            
            public static class Commands
            {
                private const string Base = nameof(Commands) + ".";
                
                public const string DcCommand = Base + nameof(DcCommand);
                public const string CodeCommand = Base + nameof(CodeCommand);
                public const string UserCommand = Base + nameof(UserCommand);
                public const string LeaveCommand = Base + nameof(LeaveCommand);
                public const string AcceptCommand = Base + nameof(AcceptCommand);
                public const string DeclineCommand = Base + nameof(DeclineCommand);
                public const string LinkCommand = Base + nameof(LinkCommand);
                public const string HelpMessage = Base + nameof(HelpMessage);
                
                public static class Code
                {
                    private const string Base = Commands.Base + nameof(Code) + ".";
                    
                    public const string LinkInfo = Base + nameof(LinkInfo);
                    public const string LinkServer = Base + nameof(LinkServer);
                    public const string LinkInGuild = Base + nameof(LinkInGuild);
                    public const string LinkInDm = Base + nameof(LinkInDm);
                }
                
                public static class User
                {
                    private const string Base = Commands.Base + nameof(User) + ".";
                    
                    public const string MatchFound = Base + nameof(MatchFound);
                    
                    public static class Errors
                    {
                        private const string Base = User.Base + nameof(Errors) + ".";
                        
                        public const string InvalidSyntax = Base + nameof(InvalidSyntax);
                        public const string UserIdNotFound = Base + nameof(UserIdNotFound);
                        public const string UserNotFound = Base + nameof(UserNotFound);
                        public const string MultipleUsersFound = Base + nameof(MultipleUsersFound);
                        public const string SearchError = Base + nameof(SearchError);
                    }
                }
                
                public static class Leave
                {
                    private const string Base = Commands.Base + nameof(Leave) + ".";
                    
                    public static class Errors
                    {
                        private const string Base = Leave.Base + nameof(Errors) + ".";
                        
                        public const string NotLinked = Base + nameof(NotLinked);
                    }
                }
            }
            
            public static class Link
            {
                private const string Base = nameof(Link) + ".";
                public static class Completed
                {
                    private const string Base = Link.Base + nameof(Completed) + ".";
                    
                    public const string Command = Base + nameof(Command);
                    public const string Admin = Base + nameof(Admin);
                    public const string Api = Base + nameof(Api);
                    public const string GuildRejoin = Base + nameof(GuildRejoin);
                    public const string InactiveRejoin = Base + nameof(InactiveRejoin);
                }
                
                public static class Declined
                {
                    private const string Base = Link.Base + nameof(Declined) + ".";
                    
                    public const string JoinWithPlayer = Base + nameof(JoinWithPlayer);
                    public const string JoinWithUser = Base + nameof(JoinWithUser);
                }
                
                public static class Errors
                {
                    private const string Base = Link.Base + nameof(Errors) + ".";
                    
                    public const string InvalidSyntax = Base + nameof(InvalidSyntax);
                }
            }
            
            public static class Unlink
            {
                private const string Base = nameof(Unlink) + ".";
                
                public static class Completed
                {
                    private const string Base = Unlink.Base + nameof(Completed) + ".";
                    
                    public const string Command = Base + nameof(Command);
                    public const string LeftGuild = Base + nameof(LeftGuild);
                    public const string Admin = Base + nameof(Admin);
                    public const string Api = Base + nameof(Api);
                }
            }
            
            public static class Banned
            {
                private const string Base = nameof(Banned) + ".";
                
                public const string IsUserBanned = Base + nameof(IsUserBanned);
            }
            
            public static class Join
            {
                private const string Base = nameof(Join) + ".";
                
                public const string ByPlayer = Base + nameof(ByPlayer);
                
                public static class Errors
                {
                    private const string Base = Join.Base + nameof(Errors) + ".";
                    
                    public const string PlayerJoinActivationNotFound = Base + nameof(PlayerJoinActivationNotFound);
                }
            }
            
            public static class Discord
            {
                private const string Base = nameof(Discord) + ".";
                
                public const string DiscordCommand = Base + nameof(DiscordCommand);
                public const string LinkCommand = Base + nameof(LinkCommand);
            }
            
            public static class Errors
            {
                private const string Base = nameof(Errors) + ".";
                
                public const string PlayerAlreadyLinked = Base + nameof(PlayerAlreadyLinked);
                public const string DiscordAlreadyLinked = Base + nameof(DiscordAlreadyLinked);
                public const string ActivationNotFound = Base + nameof(ActivationNotFound);
                public const string MustBeCompletedInDiscord = Base + nameof(MustBeCompletedInDiscord);
                public const string ConsolePlayerNotSupported = Base + nameof(ConsolePlayerNotSupported);
            }
        }
        #endregion

        #region Placeholders\PlaceholderDataKeys.cs
        public class PlaceholderDataKeys
        {
            public static readonly PlaceholderDataKey Code = new("dc.code");
            public static readonly PlaceholderDataKey NotFound = new("dc.notfound");
        }
        #endregion

        #region Placeholders\PlaceholderKeys.cs
        public class PlaceholderKeys
        {
            public static readonly PlaceholderKey InviteUrl = new(nameof(DiscordCore), "invite.url");
            public static readonly PlaceholderKey LinkCode = new(nameof(DiscordCore), "link.code");
            public static readonly PlaceholderKey CommandChannels = new(nameof(DiscordCore), "command.channels");
            public static readonly PlaceholderKey NotFound = new(nameof(DiscordCore), "notfound");
        }
        #endregion

        #region Templates\TemplateKeys.cs
        public static class TemplateKeys
        {
            public static class Announcements
            {
                private const string Base = nameof(Announcements) + ".";
                
                public static class Link
                {
                    private const string Base = Announcements.Base + nameof(Link) + ".";
                    public static readonly TemplateKey Command = new(Base + nameof(Command));
                    public static readonly TemplateKey Admin = new(Base + nameof(Admin));
                    public static readonly TemplateKey Api = new(Base + nameof(Api));
                    public static readonly TemplateKey GuildRejoin = new(Base + nameof(GuildRejoin));
                    public static readonly TemplateKey InactiveRejoin = new(Base + nameof(InactiveRejoin));
                }
                
                public static class Unlink
                {
                    private const string Base = Announcements.Base + nameof(Unlink) + ".";
                    
                    public static readonly TemplateKey Command = new(Base + nameof(Command));
                    public static readonly TemplateKey Admin = new(Base + nameof(Admin));
                    public static readonly TemplateKey Api = new(Base + nameof(Api));
                    public static readonly TemplateKey LeftGuild = new(Base + nameof(LeftGuild));
                    public static readonly TemplateKey Inactive = new(Base + nameof(Inactive));
                }
                
                public static class Ban
                {
                    private const string Base = Announcements.Base + nameof(Ban) + ".";
                    
                    public static readonly TemplateKey PlayerBanned = new(Base + nameof(PlayerBanned));
                    public static readonly TemplateKey UserBanned = new(Base + nameof(UserBanned));
                }
            }
            
            public static class WelcomeMessage
            {
                private const string Base = nameof(WelcomeMessage) + ".";
                
                public static readonly TemplateKey PmWelcomeMessage = new(Base + nameof(PmWelcomeMessage));
                public static readonly TemplateKey GuildWelcomeMessage = new(Base + nameof(GuildWelcomeMessage));
                
                public static class Error
                {
                    private const string Base = WelcomeMessage.Base + nameof(Error) + ".";
                    
                    public static readonly TemplateKey AlreadyLinked = new(Base + nameof(AlreadyLinked));
                }
            }
            
            public static class Commands
            {
                private const string Base = nameof(Commands) + ".";
                
                public static class Code
                {
                    private const string Base = Commands.Base + nameof(Code) + ".";
                    
                    public static readonly TemplateKey Success = new(Base + nameof(Success));
                }
                
                public static class User
                {
                    private const string Base = Commands.Base + nameof(User) + ".";
                    
                    public static readonly TemplateKey Success = new(Base + nameof(Success));
                    
                    public static class Error
                    {
                        private const string Base = User.Base + nameof(Error) + ".";
                        
                        public static readonly TemplateKey PlayerIsInvalid = new(Base + nameof(PlayerIsInvalid));
                        public static readonly TemplateKey PlayerNotConnected = new(Base + nameof(PlayerNotConnected));
                    }
                }
                
                public static class Leave
                {
                    private const string Base = Commands.Base + nameof(Leave) + ".";
                    
                    public static class Error
                    {
                        private const string Base = Leave.Base + nameof(Error) + ".";
                        
                        public static readonly TemplateKey UserNotLinked = new(Base + nameof(UserNotLinked));
                    }
                }
                
                public static class Admin
                {
                    private const string Base = Commands.Base + nameof(Admin) + ".";
                    
                    public static class Link
                    {
                        private const string Base = Admin.Base + nameof(Link) + ".";
                        
                        public static readonly TemplateKey Success = new(Base + nameof(Success));
                        
                        public static class Error
                        {
                            private const string Base = Link.Base + nameof(Error) + ".";
                            
                            public static readonly TemplateKey PlayerNotFound = new(Base + nameof(PlayerNotFound));
                            public static readonly TemplateKey PlayerAlreadyLinked = new(Base + nameof(PlayerAlreadyLinked));
                            public static readonly TemplateKey UserAlreadyLinked = new(Base + nameof(UserAlreadyLinked));
                        }
                    }
                    
                    public static class Unlink
                    {
                        private const string Base = Admin.Base + nameof(Unlink) + ".";
                        
                        public static readonly TemplateKey Success = new(Base + nameof(Success));
                        
                        public static class Error
                        {
                            private const string Base = Unlink.Base + nameof(Error) + ".";
                            
                            public static readonly TemplateKey MustSpecifyOne = new(Base + nameof(MustSpecifyOne));
                            public static readonly TemplateKey PlayerIsNotLinked = new(Base + nameof(PlayerIsNotLinked));
                            public static readonly TemplateKey UserIsNotLinked = new(Base + nameof(UserIsNotLinked));
                            public static readonly TemplateKey LinkNotSame = new(Base + nameof(LinkNotSame));
                        }
                    }
                    
                    public static class Search
                    {
                        private const string Base = Admin.Base + nameof(Search) + ".";
                        
                        public static readonly TemplateKey Success = new(Base + nameof(Success));
                        
                        public static class Error
                        {
                            private const string Base = Search.Base + nameof(Error) + ".";
                            
                            public static readonly TemplateKey PlayerNotFound = new(Base + nameof(PlayerNotFound));
                        }
                    }
                    
                    
                    public static class Unban
                    {
                        private const string Base = nameof(Unban) + ".";
                        
                        public static readonly TemplateKey Player = new(Base + nameof(Player));
                        public static readonly TemplateKey User = new(Base + nameof(User));
                        
                        public static class Error
                        {
                            private const string Base = Unban.Base + nameof(Error) + ".";
                            
                            public static readonly TemplateKey PlayerNotFound = new(Base + nameof(PlayerNotFound));
                            public static readonly TemplateKey PlayerNotBanned = new(Base + nameof(PlayerNotBanned));
                            public static readonly TemplateKey UserNotBanned = new(Base + nameof(UserNotBanned));
                        }
                    }
                }
            }
            
            public static class Link
            {
                private const string Base = nameof(Link) + ".";
                
                public static class Completed
                {
                    private const string Base = Link.Base + nameof(Completed) + ".";
                    
                    public static readonly TemplateKey Command = new(Base + nameof(Command));
                    public static readonly TemplateKey Admin = new(Base + nameof(Admin));
                    public static readonly TemplateKey Api = new(Base + nameof(Api));
                    public static readonly TemplateKey GuildRejoin = new(Base + nameof(GuildRejoin));
                    public static readonly TemplateKey InactiveRejoin = new(Base + nameof(InactiveRejoin));
                }
                
                public static class Declined
                {
                    private const string Base = Link.Base + nameof(Declined) + ".";
                    
                    public static readonly TemplateKey JoinWithUser = new(Base + nameof(JoinWithUser));
                    public static readonly TemplateKey JoinWithPlayer = new(Base + nameof(JoinWithPlayer));
                }
                
                public static class WelcomeMessage
                {
                    private const string Base = Link.Base + nameof(WelcomeMessage) + ".";
                    
                    public static readonly TemplateKey DmLinkAccounts = new(Base + nameof(DmLinkAccounts));
                    public static readonly TemplateKey GuildLinkAccounts = new(Base + nameof(GuildLinkAccounts));
                }
            }
            
            public static class Unlink
            {
                private const string Base = nameof(Unlink) + ".";
                
                public static class Completed
                {
                    private const string Base = Unlink.Base + nameof(Completed) + ".";
                    public static readonly TemplateKey Command = new(Base + nameof(Command));
                    public static readonly TemplateKey Admin = new(Base + nameof(Admin));
                    public static readonly TemplateKey Api = new(Base + nameof(Api));
                    public static readonly TemplateKey Inactive = new(Base + nameof(Inactive));
                }
            }
            
            public static class Banned
            {
                private const string Base = nameof(Banned) + ".";
                
                public static readonly TemplateKey PlayerBanned = new(Base + nameof(PlayerBanned));
            }
            
            public static class Join
            {
                private const string Base = nameof(Join) + ".";
                
                public static readonly TemplateKey CompleteLink = new(Base + nameof(CompleteLink));
            }
            
            public static class Errors
            {
                private const string Base = nameof(Errors) + ".";
                
                public static readonly TemplateKey UserAlreadyLinked = new(Base + nameof(UserAlreadyLinked));
                public static readonly TemplateKey PlayerAlreadyLinked = new(Base + nameof(PlayerAlreadyLinked));
                public static readonly TemplateKey CodeActivationNotFound = new(Base + nameof(CodeActivationNotFound));
                public static readonly TemplateKey LookupActivationNotFound = new(Base + nameof(LookupActivationNotFound));
                public static readonly TemplateKey MustBeCompletedInServer = new(Base + nameof(MustBeCompletedInServer));
            }
        }
        #endregion

    }

}
