using Newtonsoft.Json;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Server Chat", "Enforcer", "2.0.2")]
    [Description("Replaces the default server chat icon and prefix")]
    public class ServerChat : RustPlugin
    {
        #region Config

        ConfigData config;

        public class ConfigData
        {
            [JsonProperty(PropertyName = "Chat icon (SteamID64)")]
            public ulong chatIcon { get; set; }

            [JsonProperty(PropertyName = "Messages to not modify", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> messagesToNotModify { get; set; }

            [JsonProperty(PropertyName = "Title Settings")]
            public ServerTitle serverTitleSettings { get; set; }

            [JsonProperty(PropertyName = "Message Settings")]
            public ServerMessage serverMessageSettings { get; set; }

            [JsonProperty(PropertyName = "Format")]
            public ServerFormat serverFormatSettings { get; set; }
        }

        public class ServerTitle
        {
            [JsonProperty(PropertyName = "Title")]
            public string titleName { get; set; }

            [JsonProperty(PropertyName = "Colour")]
            public string titleColour { get; set; }

            [JsonProperty(PropertyName = "Size")]
            public int titleSize { get; set; }
        }

        public class ServerMessage
        {
            [JsonProperty(PropertyName = "Colour")]
            public string messageColour { get; set; }

            [JsonProperty(PropertyName = "Size")]
            public int messageSize { get; set; }
        }

        public class ServerFormat
        {
            [JsonProperty(PropertyName = "Server chat format")]
            public string messageFormat { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                PrintError($"{Name}.json is corrupted! Recreating a new configuration");
                LoadDefaultConfig();
                return;
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData
            {
                chatIcon = 0,

                messagesToNotModify = new List<string>
                {
                    "gave",
                    "restarting"
                },

                serverTitleSettings = new ServerTitle
                {
                    titleName = "Server",
                    titleColour = "white",
                    titleSize = 15
                },

                serverMessageSettings = new ServerMessage
                {
                    messageColour = "white",
                    messageSize = 15
                },

                serverFormatSettings = new ServerFormat
                {
                    messageFormat = "{title} {message}"
                }
            };
        }

        protected override void SaveConfig() => Config.WriteObject(config);
        #endregion

        #region Hooks

        private object OnServerMessage(string serverMessage)
        {
            foreach (var msg in config.messagesToNotModify)
            {
                if (serverMessage.Contains(msg))
                return null;
            }
            
            string title = $"<size={config.serverTitleSettings.titleSize}><color={config.serverTitleSettings.titleColour}>{config.serverTitleSettings.titleName}</color></size>";
            string message = $"<size={config.serverMessageSettings.messageSize}><color={config.serverMessageSettings.messageColour}>{serverMessage}</color></size>";

            string format = config.serverFormatSettings.messageFormat
                .Replace("{title}", title)
                .Replace("{message}", message);

            Server.Broadcast(format, config.chatIcon);
            return true;
        }



        #endregion
    }
}