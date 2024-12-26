using System;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Player Demos", "OfficerJAKE", "1.1.5")]
    [Description("Allow players to record demo files to the server, and more")]
    public class PlayerDemos : RustPlugin
    {

        #region Global Vars

        public const string PERM_ALLOW = "playerdemos.allow";
        public const string PERM_ADMIN = "playerdemos.admin";
        public const string CHAT_COMMAND_START = "pd.start";
        public const string CHAT_COMMAND_STOP = "pd.stop";
        public const string CHAT_COMMAND_ADMIN_START = "pda.start";
        public const string CHAT_COMMAND_ADMIN_STOP = "pda.stop";
        public const string CHAT_COMMAND_ADMIN_STOP_ALL = "pda.stopall";

        #endregion Global Vars

        #region Configuration

        private ConfigData configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Global settings")]
            public GlobalSettings globalSettings = new GlobalSettings();

            public class GlobalSettings
            {
				
                [JsonProperty(PropertyName = "Log To Console")]
                public bool LogToConsole = false;
				
                [JsonProperty(PropertyName = "Alert Player")]
                public bool AlertPlayer = false;
				
                [JsonProperty(PropertyName = "Max record time")]
                public float MaxDemoRecordTime = 1800F;
				
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                configData = Config.ReadObject<ConfigData>();
                if (configData == null)
                    LoadDefaultConfig();
            }
            catch
            {
                PrintError("The configuration file is corrupted");
                LoadDefaultConfig();
            }
            SaveConfig();
        }
        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            configData = new ConfigData();
        }
        protected override void SaveConfig() => Config.WriteObject(configData);

        #endregion Configuration

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CHAT_PREFIX"] = "<color=#359bf2>PLAYER DEMOS: </color>",
                ["NO_PERMS"] = "You do not have permission to use this command",
                ["DEMO_STARTED"] = "You have started a demo recording",
                ["DEMO_STOPPED"] = "Your demo recording has stopped",
                ["DEMO_STARTED_LOG"] = "{0} has started a demo recording",
                ["DEMO_STOPPED_LOG"] = "{0} has stopped a demo recording",
                ["ARG_ERROR"] = "<color=#ff0000>ERROR</color> You cannot supply arguments with this command",
                ["ADMIN_ARG_ERROR"] = "<color=#ff0000>ERROR</color> Usage: /admindemo STEAMID",
                ["INCORRECT_ID"] = "{0} does not appear to be a valid steamid",
                ["NO_PLAYER_ONLINE"] = "Could not find online player: {0}",
                ["PLAYER_FOUND"] = "Player was found with name: {0}",
                ["ADMIN_DEMO_STARTED"] = "{0} Started a demo for {1}",
                ["ADMIN_DEMO_STOPPED"] = "{0} Stopped a demo for {1}",
                ["PLAYER_ALERT_STARTED"] = "An admin started recording you",
                ["PLAYER_ALERT_STOPPED"] = "An admin stopped recording you",
                ["PLAYER_NOT_RECORDING"] = "Cannot stop recording, {0} is not recording",
				["YOU_ARE_NOT_RECORDING"] = "You are not recording, we cannot stop",
                ["ALL_DEMOS_STOPPED"] = "All demo recordings have been stopped",
                ["ALL_DEMOS_STOPPED_LOG"] = "All demo recordings have been stopped by {0}",
                //		[""] = "",
            }, this);
        }

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        #endregion Localization

        #region Chat Commands

        [ChatCommand(CHAT_COMMAND_START)]
        private void ChatCommandDemoStart(BasePlayer player, string command, string[] args)
        {
			
            if (!permission.UserHasPermission(player.UserIDString, PERM_ALLOW))
            {
                player.ChatMessage(Lang("CHAT_PREFIX") + Lang("NO_PERMS"));
                return;
            }
			
            if (args.Length > 0)
            {
                player.ChatMessage(Lang("CHAT_PREFIX") + Lang("ARG_ERROR"));
                return;
            }

            StartRecordingDemo(player);

        }

        [ChatCommand(CHAT_COMMAND_STOP)]
        private void ChatCommandDemoStop(BasePlayer player, string command, string[] args)
        {
			
            if (!permission.UserHasPermission(player.UserIDString, PERM_ALLOW))
            {
                player.ChatMessage(Lang("CHAT_PREFIX") + Lang("NO_PERMS"));
                return;
            }
			
            if (args.Length > 0)
            {
                player.ChatMessage(Lang("CHAT_PREFIX") + Lang("ARG_ERROR"));
                return;
            }
			
			if(!player.Connection.IsRecording)
			{
				player.ChatMessage(Lang("CHAT_PREFIX") + Lang("YOU_ARE_NOT_RECORDING"));
                return;
			}
			
            StopRecordingDemo(player);

        }

        [ChatCommand(CHAT_COMMAND_ADMIN_START)]
        private void AdminStartDemo(BasePlayer player, string command, string[] args)
        {

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, PERM_ADMIN))
            {
				
                if (args.Length != 1)
                {
                    player.ChatMessage(Lang("CHAT_PREFIX") + Lang("ADMIN_ARG_ERROR"));
                    return;
                }

                string SentID = args[0];

                if (!SentID.StartsWith("7656119") || SentID.Length != 17)
                {
                    player.ChatMessage(Lang("CHAT_PREFIX") + Lang("INCORRECT_ID", null, SentID));
                    return;
                }

                ulong IdToRecord = Convert.ToUInt64(args[0]);
                BasePlayer WhoToRecord = BasePlayer.FindByID(IdToRecord);

                if (WhoToRecord == null)
                {
                    player.ChatMessage(Lang("CHAT_PREFIX") + Lang("NO_PLAYER_ONLINE", null, IdToRecord));
                    return;
                }

                StartAdminDemo(player, WhoToRecord);

            }
            else
            {
                player.ChatMessage(Lang("CHAT_PREFIX") + Lang("NO_PERMS"));
                return;
            }

        }

        [ChatCommand(CHAT_COMMAND_ADMIN_STOP)]
        private void AdminStopDemo(BasePlayer player, string command, string[] args)
        {
			
            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, PERM_ADMIN))
            {
				
                if (args.Length != 1)
                {
                    player.ChatMessage(Lang("CHAT_PREFIX") + Lang("ADMIN_ARG_ERROR"));
                    return;
                }

                string SentID = args[0];
                if (!SentID.StartsWith("7656119") || SentID.Length != 17)
                {
                    player.ChatMessage(Lang("CHAT_PREFIX") + Lang("INCORRECT_ID", null, SentID));
                    return;
                }

                ulong IdToRecord = Convert.ToUInt64(args[0]);
                BasePlayer WhoToRecord = BasePlayer.FindByID(IdToRecord);

                if (WhoToRecord == null)
                {
                    player.ChatMessage(Lang("CHAT_PREFIX") + Lang("NO_PLAYER_ONLINE", null, IdToRecord));
                    return;
                }

                if (!WhoToRecord.Connection.IsRecording)
                {
                    player.ChatMessage(Lang("PLAYER_NOT_RECORDING", null, WhoToRecord.UserIDString));
                    return;
                }

                StopAdminDemo(player, WhoToRecord);

            }
            else
            {
                player.ChatMessage(Lang("CHAT_PREFIX") + Lang("NO_PERMS"));
                return;
            }

        }

        [ChatCommand(CHAT_COMMAND_ADMIN_STOP_ALL)]
        private void AdminStopAllDemos(BasePlayer player, string command, string[] args)
        {
			
            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, PERM_ADMIN))
            {
                AdminStopAllDemos(player);
            }
            else
            {
                player.ChatMessage(Lang("CHAT_PREFIX") + Lang("NO_PERMS"));
                return;
            }
			
        }

        #endregion Chat Commands

        #region Hooks

        private void Unload()
        {
			
            foreach (var player in BasePlayer.activePlayerList)
            {
				
                if (player.Connection.IsRecording)
                {
                    player.Connection.StopRecording();
                }
				
            }
			
        }

        void Init()
        {
            permission.RegisterPermission(PERM_ALLOW, this);
            permission.RegisterPermission(PERM_ADMIN, this);
        }
		
        private void StartRecordingDemo(BasePlayer player)
        {
			
            if (configData.globalSettings.LogToConsole)
            {
                PrintWarning(Lang("DEMO_STARTED_LOG", null, player));
            }

            player.ChatMessage(Lang("CHAT_PREFIX") + Lang("DEMO_STARTED"));

            player.StartDemoRecording();
            timer.Once(configData.globalSettings.MaxDemoRecordTime, () => StopRecordingDemo(player));
			
        }

        private void StopRecordingDemo(BasePlayer player)
        {
			
            if (configData.globalSettings.LogToConsole)
            {
                PrintWarning(Lang("DEMO_STOPPED_LOG", null, player));
            }

            player.ChatMessage(Lang("CHAT_PREFIX") + Lang("DEMO_STOPPED"));
            player.StopDemoRecording();
			
        }

        private void StartAdminDemo(BasePlayer player, BasePlayer WhoToRecord)
        {
			
            if (configData.globalSettings.LogToConsole)
            {
                PrintWarning(Lang("ADMIN_DEMO_STARTED", null, player.displayName, WhoToRecord.displayName));
            }

            player.ChatMessage(Lang("CHAT_PREFIX") + Lang("ADMIN_DEMO_STARTED", null, player.displayName, WhoToRecord.UserIDString));

            if (configData.globalSettings.AlertPlayer)
            {
                WhoToRecord.ChatMessage(Lang("CHAT_PREFIX") + Lang("PLAYER_ALERT_STARTED"));
            }

            WhoToRecord.StartDemoRecording();
            timer.Once(configData.globalSettings.MaxDemoRecordTime, () => StopAdminDemo(player, WhoToRecord));

        }

        private void StopAdminDemo(BasePlayer player, BasePlayer WhoToRecord)
        {
			
            if (configData.globalSettings.LogToConsole)
            {
                PrintWarning(Lang("ADMIN_DEMO_STOPPED", null, player.displayName, WhoToRecord.displayName));
            }

            player.ChatMessage(Lang("CHAT_PREFIX") + Lang("ADMIN_DEMO_STOPPED", null, player.displayName, WhoToRecord.UserIDString));

            if (configData.globalSettings.AlertPlayer)
            {
                WhoToRecord.ChatMessage(Lang("CHAT_PREFIX") + Lang("PLAYER_ALERT_STOPPED"));
            }

            WhoToRecord.StopDemoRecording();

        }
		
		private void AdminStopAllDemos(BasePlayer player)
        {
			
            foreach (var Rplayer in BasePlayer.activePlayerList)
            {
				
                if (Rplayer.Connection.IsRecording)
                {
                    Rplayer.Connection.StopRecording();
                    Rplayer.ChatMessage(Lang("CHAT_PREFIX") + Lang("ALL_DEMOS_STOPPED"));

                    if (configData.globalSettings.LogToConsole)
                    {
                        PrintWarning(Lang("ALL_DEMOS_STOPPED_LOG", null, player));
                    }

                }
				
            }
			
        }
		
        #endregion Hooks

    }

}
