using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using ProtoBuf;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Teleport Marker", "Talha", "1.0.6")]
    [Description("Authorized players can teleport to map markers with that plugin.")]
    public class TeleportMarker : RustPlugin
    {
		private const string permUse = "teleportmarker.use";
        private ConfigData config;
        private class ConfigData
        {
            [JsonProperty(PropertyName = "Admins can teleport without permission")]
            public bool admins;
        }

        private ConfigData GetDefaultConfig()
        {
            return new ConfigData
            {
                admins = true,
            };
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
                PrintError("Configuration file is corrupt.");
                return;
            }
            SaveConfig();
        }
        protected override void LoadDefaultConfig()
        {
            config = GetDefaultConfig();
        }
        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }
        private void Message(BasePlayer player, string key, params object[] args)
        {
            var message = string.Format(lang.GetMessage(key, this, player.UserIDString), args);
            player.ChatMessage(message);
        }
        private void OnServerInitialized()
        {
            permission.RegisterPermission(permUse, this);
        }
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Teleported"] = "You are teleported to <color=#FFA600>{0}</color>.",
                ["Cooldown"] = "Your health has been set to {0}, you can teleport again."
            }, this);
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Teleported"] = "<color=#FFA600>{0}</color> konumuna ışınlandınız.",
                ["Cooldown"] = "Canın eskisi gibi <color=#FFA600>{0}</color> olarak ayarlandı, yeniden ışınlanabilirsiniz."
            }, this, "tr");
        }
        private void TP(BasePlayer player, MapNote note)
        {
            player.flyhackPauseTime = 10f;
            var health = player.health;
            var health2 = 100 - health;
            player._health = 100000;
            var pos = note.worldPosition + new Vector3(0,120,0);;
            player.Teleport(pos);
            Message(player, "Teleported", pos);
            timer.Once(6f, () => { if (player == null) return; Message(player, "Cooldown", (100 - health2)); player.SetMaxHealth(100); player.Hurt(health2); });
        }
        private void OnMapMarkerAdded(BasePlayer player, MapNote note)
        {
            if (player == null || note == null || player.isMounted || !player.IsAlive()) return;
            if (config.admins == true)
            {
                if (player.IsAdmin) {TP(player, note);}
            }
            else
            {
                if (player.IPlayer.HasPermission(permUse)) {TP(player, note);}
            }
        }
    }
}