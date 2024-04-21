

using System.Collections.Generic;
using UnityEngine.UI;
using Oxide.Core.Plugins;
using System;
using Oxide.Game.Rust.Cui;
using System.Drawing;
using Rust;
using System.ComponentModel;
using Oxide.Core;
using Newtonsoft.Json;
using UnityEngine;
using System.Linq;
using Oxide.Plugins.SimpleStatusExtensionMethods;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("SimpleStatus", "mr01sam", "1.1.3")]
    [Description("Allows plugins to add custom status displays for the UI.")]
    partial class SimpleStatus : CovalencePlugin
    {
        /* // Changelog
        # v1.1.0
        - Added functionality to change color, icon, icon color, title color and text color for existing statuses
        - New API method 'SetProperty' which allows you to set multiple status properties at the same time with minimal redraws
        - New command 'simplestatus.clearcache' that allows admins to clear the cache and status data with a command
        - When an icon is set to null or empty string now it will not display an icon (before it displayed blank white square)
        - Adjusted bleeding status threshold
        - Fixed issue where title color would be initialized to the text color
        - Fixed key error when statuses are removed from player
        # v1.1.1
        - Can now specify item ids for the icon. Use the format 'itemid:<item id>' to denote it as an item icon. For example, if you want the icon to be scrap use 'itemid:-932201673'
        - Statuses now update instantly when authing/deauthing from cupboard
        # v1.1.2
        - Now supports raw images. To designate as a raw image, prefix the name with "raw:" similar to how itemids work.
        - Fixed issue where background material would be wrong if an icon was used.
        - Fixed console spam if status without icon was shown.
        # v1.1.3
        - Fixed key error in disconnect
         */

        public static SimpleStatus PLUGIN;

        [PluginReference]
        private readonly Plugin ImageLibrary;

        [PluginReference]
        private readonly Plugin CustomStatusFramework;

        private readonly bool Debugging = false; // If you are a developer, you can enable this to get console logs.
        private readonly int[] DebugCodes = new int[] { }; // Add codes here for specific debug statements you want to see, empty array will show all.

        #region Oxide Hooks

        private void Init()
        {
            if (Data == null) { Data = new SavedData(); }
            if (CachedUI == null) { CachedUI = new Dictionary<string, string>(); }
            LoadData();
        }

        private void OnServerInitialized()
        {
            PLUGIN = this;
            if (!ImageLibrary?.IsLoaded ?? true)
            {
                PrintError("ImageLibary is REQUIRED for this plugin to work properly. Please load it onto your server and reload this plugin.");
                return;
            }
            if (CustomStatusFramework?.IsLoaded ?? false)
            {
                PrintError("You have both Simple Status and Custom Status Framework installed. These plugins do the same thing and will conflict with each other. Please unload Custom Status Framework and reload this plugin.");
                return;
            }
            AddCovalenceCommand(config.ToggleStatusCommand, nameof(CmdToggleStatus));
            foreach (var player in BasePlayer.activePlayerList)
            {
                OnPlayerConnected(player);
            }
        }

        private void Unload()
        {
            SaveData();
            foreach (var pair in Behaviours)
            {
                foreach(var status in Data.Statuses.Keys)
                {
                    pair.Value.RemoveStatus(status);
                }
                UnityEngine.Object.Destroy(pair.Value);
            }
            CachedUI = null;
            Data = null;
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            var name = plugin?.Name;
            if (name == Name) { return; }
            if (Data.Statuses.Values.Any(x => x.PluginName == name))
            {
                RemovePluginData(name);
            }
        }

        private void RemovePluginData(string pluginName)
        {
            Debug($"Removing {pluginName} because its no longer loaded");
            var statuses = Data.Statuses.Where(x => x.Value.PluginName == pluginName).Select(x => x.Key).ToArray();
            foreach(var status in statuses)
            {
                Data.Statuses.Remove(status);
            }
            var userIdsToUpdate = new HashSet<ulong>();
            foreach(var playerData in Data.Player.ToArray())
            {
                var userId = playerData.Key;
                foreach(var key in playerData.Value.Keys.ToArray())
                {
                    if (statuses.Contains(key)) { Data.Player[userId].Remove(key); }
                }
                userIdsToUpdate.Add(userId);
            }
            foreach(var userId in userIdsToUpdate)
            {
                var behavior = Behaviours.GetValueOrDefault(userId); if (behavior == null) { continue; }
                behavior.rowsNeedUpdate = true;
            }
        }

        private void OnPlayerConnected(BasePlayer basePlayer)
        {
            var obj = basePlayer.gameObject.AddComponent<StatusBehaviour>();
            Behaviours.Add(basePlayer.userID, obj);
            Debug($"Connecting {basePlayer.displayName} has {(Data.Player.ContainsKey(basePlayer.userID) ? Data.Player.GetValueOrDefault(basePlayer.userID)?.Count : 0)} statuses: {Data.Player.GetValueOrDefault(basePlayer.userID)?.Keys.ToSentence()}");
            NextFrame(() =>
            {
                Debug($"Resuming statuses for {basePlayer.displayName}..");
                if (Data.Player.ContainsKey(basePlayer.userID))
                {
                    foreach (var data in Data.Player[basePlayer.userID])
                    {
                        var statusName = data.Key;
                        Debug($"Resuming {data.Key} {data.Value.Duration} {data.Value.Title} {data.Value.Text} {data.Value.EndTime.HasValue} {data.Value.DurationUntilEndTime}");
                        obj.SetStatus(data.Key, data.Value.Duration, !data.Value.EndTime.HasValue, true);
                    }
                }
                if (config.WarnPlayersThatStatusIsHidden && Data.PlayersHiding.Contains(basePlayer.userID))
                {
                    Message(basePlayer, Lang(PLUGIN, "warning", basePlayer.userID, config.ToggleStatusCommand));
                }
            });
        }

        private void OnPlayerDisconnected(BasePlayer basePlayer)
        {
            var obj = Behaviours.GetValueOrDefault(basePlayer.userID);
            if (obj == null) { return; }
            UnityEngine.Object.Destroy(obj);
            Behaviours.Remove(basePlayer.userID);
            Debug($"Disconnecting {basePlayer.displayName} has {(Data.Player.ContainsKey(basePlayer.userID) ? Data.Player[basePlayer.userID].Count : 0)} statuses: {Data.Player.GetValueOrDefault(basePlayer.userID)?.Keys.ToSentence()}");
        }

        private void CanPickupEntity(BasePlayer basePlayer, BaseEntity entity)
        {
            if (basePlayer == null || entity == null) { return; }
            var name = entity?.name;
            NextTick(() =>
            {
                if (basePlayer != null && name != null)
                {
                    Behaviours.GetValueOrDefault(basePlayer.userID)?.itemStatuses.Inc(name);
                }
            });
        }

        private void OnItemPickup(Item item, BasePlayer basePlayer)
        {
            if (basePlayer == null || item == null) { return; }
            Behaviours.GetValueOrDefault(basePlayer.userID)?.itemStatuses.Inc(item.info.shortname);
        }

        private void OnStructureUpgrade(BaseCombatEntity entity, BasePlayer basePlayer, BuildingGrade.Enum grade)
        {
            if (basePlayer == null) { return; }
            Behaviours.GetValueOrDefault(basePlayer.userID)?.itemStatuses.Inc($"removed {grade}");
        }

        private void OnStructureRepair(BuildingBlock entity, BasePlayer basePlayer)
        {
            NextTick(() =>
            {
                if (entity != null && basePlayer != null)
                {
                    Behaviours.GetValueOrDefault(basePlayer.userID)?.itemStatuses.Inc($"removed {entity.grade}");
                }
            });
        }

        private void OnCupboardAuthorize(BuildingPrivlidge privilege, BasePlayer basePlayer)
        {
            NextTick(() =>
            {
                var b = Behaviours.GetValueOrDefault(basePlayer?.userID ?? 0);
                if (b == null) { return; }
                b.ForceCheckModifiers();
                b.ForceUpdateUI();
            });
        }

        private void OnCupboardDeauthorize(BuildingPrivlidge privilege, BasePlayer basePlayer)
        {
            NextTick(() =>
            {
                var b = Behaviours.GetValueOrDefault(basePlayer?.userID ?? 0);
                if (b == null) { return; }
                b.ForceCheckModifiers();
                b.ForceUpdateUI();
            });
        }

        private void OnDispenserGather(ResourceDispenser dispenser, BasePlayer basePlayer, Item item)
        {
            if (basePlayer == null || item == null) { return; }
            Behaviours.GetValueOrDefault(basePlayer.userID)?.itemStatuses.Inc(item.info.shortname);
        }

        private void OnCollectiblePickup(CollectibleEntity collectible, BasePlayer basePlayer)
        {
            if (basePlayer == null) { return; }
            collectible.itemList.ForEach(item =>
            {
                Behaviours.GetValueOrDefault(basePlayer.userID)?.itemStatuses.Inc(item.itemDef.shortname);
            });
        }
        #endregion

        #region Status Info
        protected class SavedData
        {
            public Dictionary<string, StatusInfo> Statuses = new Dictionary<string, StatusInfo>();
            public HashSet<ulong> PlayersHiding = new HashSet<ulong>();
            public Dictionary<ulong, Dictionary<string, PlayerStatusInfo>> Player = new Dictionary<ulong, Dictionary<string, PlayerStatusInfo>>();
        }

        private static string GetImageData(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }
            else if (path.IsAssetPath())
            {
                return path;
            }
            else if (path.IsItemId())
            {
                return path.AsItemId();
            }
            else if (path.IsRawImage())
            {
                return PLUGIN.ImageLibrary?.Call<string>("GetImage", path.AsRawImage());
            }
            return PLUGIN.ImageLibrary?.Call<string>("GetImage", path);
        }

        protected class PlayerStatusInfo
        {
            public int Duration;
            public string Color = null;
            public string Title = null;
            public string TitleColor = null;
            public string Text = null;
            public string TextColor = null;
            public string ImagePathOrUrl = null;
            public string IconColor = null;
            [JsonIgnore]
            public string ImageData => GetImageData(ImagePathOrUrl);
            public DateTime? EndTime = null;
            [JsonIgnore]
            public bool IsPastEndTime => EndTime.HasValue && EndTime.Value < DateTime.Now;
            [JsonIgnore]
            public int DurationUntilEndTime => !EndTime.HasValue ? Duration : (int)Math.Floor((EndTime.Value.Subtract(DateTime.Now)).TotalSeconds);
        }

        protected static SavedData Data = new SavedData();

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject($"{Name}/data", Data);
        }

        private void LoadData()
        {
            Debug("Load data called");
            var data = Interface.Oxide.DataFileSystem.ReadObject<SavedData>($"{Name}/data") ?? new SavedData();
            if (data.Statuses == null)
            {
                data.Statuses = new Dictionary<string, StatusInfo>();
            }
            if (data.Player == null)
            {
                data.Player = new Dictionary<ulong, Dictionary<string, PlayerStatusInfo>>();
            }
            Data.Player = data.Player;
            if (Data.Statuses == null)
            {
                Data.Statuses = new Dictionary<string, StatusInfo>();
            }
            foreach(var status in data.Statuses.ToArray())
            {
                if (!status.Value.Plugin?.IsLoaded ?? true)
                {
                    RemovePluginData(status.Value.PluginName);
                }
                else if (!Data.Statuses.ContainsKey(status.Key))
                {
                    Debug("Assigned new status from load");
                    Data.Statuses[status.Key] = status.Value;
                }
            }
        }


        private static void Debug(string message)
        {
            if (PLUGIN == null || !PLUGIN.Debugging) { return; }
            var code = message.Split(" ").First().GetHashCode();
            if (PLUGIN.DebugCodes != null && PLUGIN.DebugCodes.Length > 0 && !PLUGIN.DebugCodes.Contains(code)) { return; }
            PLUGIN?.Puts($"DEBUG({code}): {message}");
        }

        protected class StatusInfo
        {
            [JsonIgnore]
            public Plugin Plugin => Interface.uMod.RootPluginManager.GetPlugin(PluginName);
            [JsonIgnore]
            public bool PluginIsLoaded => Plugin?.IsLoaded ?? false;
            public string PluginName;
            public string Id;
            public string Color;
            public string Title;
            public string TitleColor;
            public string Text = null;
            public string TextColor;
            public string ImageLibraryNameOrAssetPath;
            [JsonProperty("ImageLibraryIconId")]
            private string ImageLibraryIconId // old version
            {
                set { ImageLibraryNameOrAssetPath = value; }
            }
            public string IconColor;
            [JsonIgnore]
            public bool IsAssetImage => !string.IsNullOrEmpty(ImageLibraryNameOrAssetPath) && ImageLibraryNameOrAssetPath.IsAssetPath();
            [JsonIgnore]
            public bool HasImage => !string.IsNullOrEmpty(ImageLibraryNameOrAssetPath);
            [JsonIgnore]
            public string ImageData => GetImageData(ImageLibraryNameOrAssetPath);
        }
        #endregion

        #region Utility
        private static void Message(BasePlayer basePlayer, string message)
        {
            var icon = PLUGIN.config.ChatMessageSteamId;
            ConsoleNetwork.SendClientCommand(basePlayer.Connection, "chat.add", 2, icon, message);
        }

        #endregion
    }
}

namespace Oxide.Plugins
{
    partial class SimpleStatus : CovalencePlugin
    {
        [HookMethod(nameof(CreateStatus))]
        private void CreateStatus(Plugin plugin, string statusId, string backgroundColor = "1 1 1 1", string title = "Text", string titleColor = "1 1 1 1", string text = null, string textColor = "1 1 1 1", string imageLibraryNameOrAssetPath = null, string imageColor = "1 1 1 1")
        {
            Debug("CreateStatus called");
            Data.Statuses[statusId] = new StatusInfo
            {
                PluginName = plugin.Name,
                Id = statusId,
                Color = backgroundColor,
                Title = title,
                TitleColor = titleColor,
                Text = text,
                TextColor = textColor,
                ImageLibraryNameOrAssetPath = imageLibraryNameOrAssetPath,
                IconColor = imageColor
            };
            CachedUI.Clear();
        }

        [HookMethod(nameof(SetStatus))]
        private void SetStatus(ulong userId, string statusId, int duration = int.MaxValue, bool pauseOffline = true)
        {
            Debug($"SetStatus called {userId} {statusId} {duration} {pauseOffline}");
            if (IsStatusIdInvalid(statusId)) { return; }
            var b = Behaviours.GetValueOrDefault(userId);
            if (b == null)
            {
                // save status for later if player is offline
                SetStatusForOfflinePlayer(userId, statusId, duration, pauseOffline);
                return;
            }
            if (duration > 0)
            {
                b.SetStatus(statusId, duration, pauseOffline);
            }
            else
            {
                b.RemoveStatus(statusId);
            }
        }

        [HookMethod(nameof(SetStatusColor))]
        private void SetStatusColor(ulong userId, string statusId, string color = null)
        {
            Debug("SetStatusColor called");
            if (IsStatusIdInvalid(statusId)) { return; }
            var b = Behaviours.GetValueOrDefault(userId); if (b == null) { return; }
            b.SetStatusColor(statusId, color ?? RESET_KEYWORD);
        }

        [HookMethod(nameof(SetStatusTitle))]
        private void SetStatusTitle(ulong userId, string statusId, string title = null)
        {
            Debug("SetStatusTitle called");
            if (IsStatusIdInvalid(statusId)) { return; }
            var b = Behaviours.GetValueOrDefault(userId); if (b == null) { return; }
            b.SetStatusTitle(statusId, title ?? RESET_KEYWORD, null);
        }

        [HookMethod(nameof(SetStatusTitleColor))]
        private void SetStatusTitleColor(ulong userId, string statusId, string color = null)
        {
            Debug("SetStatusTitleColor called");
            if (IsStatusIdInvalid(statusId)) { return; }
            var b = Behaviours.GetValueOrDefault(userId); if (b == null) { return; }
            b.SetStatusTitle(statusId, null, color ?? RESET_KEYWORD);
        }

        [HookMethod(nameof(SetStatusText))]
        private void SetStatusText(ulong userId, string statusId, string text = null)
        {
            Debug("SetStatusText called");
            if (IsStatusIdInvalid(statusId)) { return; }
            var b = Behaviours.GetValueOrDefault(userId); if (b == null) { return; }
            b.SetStatusText(statusId, text ?? RESET_KEYWORD, null);
        }

        [HookMethod(nameof(SetStatusTextColor))]
        private void SetStatusTextColor(ulong userId, string statusId, string color = null)
        {
            Debug("SetStatusTextColor called");
            if (IsStatusIdInvalid(statusId)) { return; }
            var b = Behaviours.GetValueOrDefault(userId); if (b == null) { return; }
            b.SetStatusText(statusId, null, color ?? RESET_KEYWORD);
        }

        [HookMethod(nameof(SetStatusIcon))]
        private void SetStatusIcon(ulong userId, string statusId, string imageLibraryNameOrAssetPath = null)
        {
            Debug("SetStatusIcon called");
            if (IsStatusIdInvalid(statusId)) { return; }
            var b = Behaviours.GetValueOrDefault(userId); if (b == null) { return; }
            b.SetStatusIcon(statusId, imageLibraryNameOrAssetPath ?? RESET_KEYWORD, null);
        }

        [HookMethod(nameof(SetStatusIconColor))]
        private void SetStatusIconColor(ulong userId, string statusId, string color = null)
        {
            Debug("SetStatusIconColor called");
            if (IsStatusIdInvalid(statusId)) { return; }
            var b = Behaviours.GetValueOrDefault(userId); if (b == null) { return; }
            b.SetStatusIcon(statusId, null, color ?? RESET_KEYWORD);
        }

        [HookMethod(nameof(SetStatusProperty))]
        private void SetStatusProperty(ulong userId, string statusId, Dictionary<string, object> properties)
        {
            Debug("SetStatusProperty called");
            if (IsStatusIdInvalid(statusId)) { return; }
            var b = Behaviours.GetValueOrDefault(userId); if (b == null) { return; }

            // title
            var title = GetPropertyOrKeyword(properties, "title"); 
            var titleColor = GetPropertyOrKeyword(properties, "titleColor");
            if (title != null || titleColor != null)
            {
                b.SetStatusTitle(statusId, title, titleColor);
            }

            // text
            var text = GetPropertyOrKeyword(properties, "text");
            var textColor = GetPropertyOrKeyword(properties, "textColor");
            if (text != null || textColor != null)
            {
                b.SetStatusText(statusId, text, textColor);
            }

            // icon
            var icon = GetPropertyOrKeyword(properties, "icon");
            var iconColor = GetPropertyOrKeyword(properties, "iconColor");
            if (icon != null || iconColor != null)
            {
                b.SetStatusIcon(statusId, icon, iconColor);
            }

            // color
            var color = GetPropertyOrKeyword(properties, "color");
            if (color != null)
            {
                b.SetStatusColor(statusId, color);
            }
        }

        [HookMethod(nameof(GetDuration))]
        private int GetDuration(ulong userId, string statusId)
        {
            Debug("GetDuration called");
            if (IsStatusIdInvalid(statusId)) { return 0; }
            if (!Data.Player.ContainsKey(userId) || !Data.Player[userId].ContainsKey(statusId)) { return 0; }
            return Data.Player[userId][statusId].Duration;
        }

        private bool IsStatusIdInvalid(string statusId)
        {
            if (Data?.Statuses?.ContainsKey(statusId) ?? true) { return false; }
            if (Debugging) { PrintError($"There is no status with the id of '{statusId}'"); }
            return true;
        }

        private string GetPropertyOrKeyword(Dictionary<string, object> properties, string key)
        {
            return properties.ContainsKey(key) ? properties[key]?.ToString() ?? RESET_KEYWORD : null;
        }

        /* Subscribable Hooks */

        /*
         * # Called when a status is initially set for a player.
         * void OnStatusSet(ulong userId, string statusId, int duration)
         * 
         * 
         * # Called when a status is removed for a player. (When the duration reaches 0).
         * void OnStatusEnd(ulong userId, string statusId, int duration)
         * 
         * 
         * # Called when a status property is updated.
         * # The 'property' parameter can be: 'title', 'titleColor', 'text', 'textColor', 'icon', 'iconColor', 'color'
         * void OnStatusUpdate(ulong userId, string statusId, string property, string value);
         */
    }
}

namespace Oxide.Plugins
{
    partial class SimpleStatus : CovalencePlugin
    {
        private void CmdToggleStatus(IPlayer player, string command, string[] args)
        {
            var basePlayer = player.Object as BasePlayer;
            if (basePlayer == null) { return; }
            if (!Data.PlayersHiding.Contains(basePlayer.userID))
            {
                Data.PlayersHiding.Add(basePlayer.userID);
                CuiHelper.DestroyUi(basePlayer, UI_Base_ID);
                Message(basePlayer, Lang(PLUGIN, "hiding", basePlayer.userID));
            }
            else
            {
                Data.PlayersHiding.Remove(basePlayer.userID);
                var b = Behaviours.GetValueOrDefault(basePlayer.userID); if (b == null) { return; }
                b.InitStatusUI();
                b.rowsNeedUpdate = true;
                Message(basePlayer, Lang(PLUGIN, "showing", basePlayer.userID));
            }
        }

        [Command("simplestatus.clearcache")]
        private void CmdClearCache(IPlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !player.IsServer) { return; }
            CachedUI = new Dictionary<string, string>();
            Data.Statuses = new Dictionary<string, StatusInfo>();
            player.Reply("Cached data has been cleared, please reload Simple Status and all dependent plugins to avoid issues");
        }
    }
}

namespace Oxide.Plugins
{
    partial class SimpleStatus : CovalencePlugin
    {
        private Configuration config;

        private partial class Configuration
        {
            public ulong ChatMessageSteamId = 0;
            public string ToggleStatusCommand = "ts";
            public bool WarnPlayersThatStatusIsHidden = true;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) throw new Exception();
            }
            catch
            {
                PrintError("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        protected override void LoadDefaultConfig() => config = new Configuration();
    }
}

namespace Oxide.Plugins.SimpleStatusExtensionMethods
{
    public static class ExtensionMethods
    {
        public static void RemoveAll<TKey, TValue>(this Dictionary<TKey, TValue> dict, Func<KeyValuePair<TKey, TValue>, bool> condition)
        {
            foreach (var cur in dict.Where(condition).ToList())
            {
                dict.Remove(cur.Key);
            }
        }

        public static void Inc<TKey>(this Dictionary<TKey, float> dict, TKey key)
        {
            dict[key] = Time.realtimeSinceStartup + 3.6f;
        }

        public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
        {
            foreach (T element in source) action(element);
        }

        public static bool IsAssetPath(this string path) => path.StartsWith("assets/");

        public static bool IsItemId(this string path) => path.StartsWith("itemid:");

        public static bool IsRawImage(this string path) => path.StartsWith("raw:");

        public static string AsRawImage(this string path) => !IsRawImage(path) ? "" : $"{path.Substring(4)}";

        public static string AsItemId(this string path) => !IsItemId(path) ? "" : path.Substring(7);
    }
}

namespace Oxide.Plugins
{
    partial class SimpleStatus : CovalencePlugin
    {
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["showing"] = "Showing statuses.",
                ["hiding"] = "Hiding statuses.",
                ["warning"] = "You have statuses hidden. Use the /{0} command to show them again."
            }, this);
        }

        private string Lang(Plugin plugin, string key, ulong userId, params object[] args) => string.Format(lang.GetMessage(key, plugin, userId.ToString()), args);
    }
}

namespace Oxide.Plugins
{
    partial class SimpleStatus : CovalencePlugin
    {
        private void SetStatusForOfflinePlayer(ulong userId, string statusId, int duration, bool pauseOffline)
        {
            if (duration <= 0)
            {
                var data = Data.Player.GetValueOrDefault(userId);
                if (data == null) { return; }
                Debug($"Remove status {statusId} for offline player {userId}");
                data.Remove(statusId);
                if (data.Count <= 0)
                {
                    Data.Player.Remove(userId);
                }
                return;
            }
            if (!Data.Player.ContainsKey(userId))
            {
                Data.Player[userId] = new Dictionary<string, PlayerStatusInfo>();
            }
            Debug($"Set status {statusId} for offline player {userId} duration {duration}");
            Data.Player[userId][statusId] = new PlayerStatusInfo() { Duration = duration, EndTime = pauseOffline ? null : (DateTime?)DateTime.Now.AddSeconds(duration) };
        }

        private Dictionary<ulong, StatusBehaviour> Behaviours = new Dictionary<ulong, StatusBehaviour>();
        public int BehaviorCount = 0;
        public class StatusBehaviour : MonoBehaviour
        {
            private BasePlayer basePlayer;
            private int smallModifiersCount;
            private int bigModifiersCount;
            private int previousModifiersCount = 0;
            public bool rowsNeedUpdate = false;
            public bool privForceUpdate = false;
            public bool forceCheckMods = false;
            public Dictionary<string, float> itemStatuses = new Dictionary<string, float>();
            private float nextBigStatusUpdate;
            public int ModifiersCount => smallModifiersCount + bigModifiersCount;
            public string[] ActiveStatusIds => !Data.Player.ContainsKey(UserId) ? new string[] { } : Data.Player[UserId].Keys.ToArray();
            public ulong UserId => basePlayer.userID;
            private bool inBuildingPriv = false;
            private ulong lastPrivId = 0;

            #region Private

            private void Awake()
            {
                basePlayer = GetComponent<BasePlayer>();
                PLUGIN.BehaviorCount++;
            }
            private void OnDestroy()
            {
                CuiHelper.DestroyUi(basePlayer, UI_Base_ID);
                PLUGIN.BehaviorCount--;
            }

            private void StartWorking()
            {
                InitStatusUI();
                RepeatCheckModifiers();
                RepeatUpdateUI();
                nextBigStatusUpdate = 0;

                InvokeRepeating(nameof(RepeatCheckDurations), 1f, 1f);
                InvokeRepeating(nameof(RepeatUpdateUI), 0.2f, 0.2f);
                InvokeRepeating(nameof(RepeatCheckModifiers), 0.2f, 0.2f);
            }

            private void StopWorking()
            {
                CancelInvoke(nameof(RepeatCheckDurations));
                CancelInvoke(nameof(RepeatUpdateUI));
                CancelInvoke(nameof(RepeatCheckModifiers));
                CuiHelper.DestroyUi(basePlayer, UI_Base_ID);
            }

            public void InitStatusUI()
            {
                if (Data.PlayersHiding.Contains(UserId)) { return; }
                CuiHelper.DestroyUi(basePlayer, UI_Base_ID);
                CuiHelper.AddUi(basePlayer, UI_Base());
            }

            private void RepeatCheckDurations()
            {
                if (basePlayer.IsSleeping())
                {
                    rowsNeedUpdate = true;
                    return;
                }
                foreach (var statusId in ActiveStatusIds)
                {
                    var status = Data.Statuses.GetValueOrDefault(statusId); if (status == null) { continue; }
                    var data = Data.Player.GetValueOrDefault(UserId)?.GetValueOrDefault(statusId); if (data == null) { continue; }
                    var duration = data.Duration;
                    if (duration >= int.MaxValue) { continue; } // No need to update duration
                    data.Duration -= 1;
                    if (data.Duration <= 0 || data.IsPastEndTime)
                    {
                        if (data.IsPastEndTime)
                        {
                            Debug($"Current: {DateTime.Now.ToLongTimeString()} EndTime: {data.EndTime.Value.ToLongTimeString()}");
                        }
                        RemoveStatus(statusId);
                    }
                    else if (status.Text == null && data.Text == null && !Data.PlayersHiding.Contains(basePlayer.userID))
                    {
                        // Update duration text
                        var textColor = data.TextColor ?? status.TextColor;
                        CuiHelper.DestroyUi(basePlayer, string.Format(UI_Text_ID, statusId));
                        CuiHelper.AddUi(basePlayer, UI_StatusText(status, FormatDuration(data.Duration), textColor));
                    }
                }
            }

            public void ForceCheckModifiers()
            {
                forceCheckMods = true;
                RepeatCheckModifiers();
            }

            public void ForceUpdateUI()
            {
                RepeatUpdateUI();
            }

            private void RepeatUpdateUI()
            {
                if (!rowsNeedUpdate && !privForceUpdate) { return; }
                if (Data.PlayersHiding.Contains(basePlayer.userID))
                {
                    rowsNeedUpdate = false;
                    privForceUpdate = false;
                    return;
                }
                if (basePlayer.IsSleeping())
                {
                    ActiveStatusIds.ForEach(statusId => CuiHelper.DestroyUi(basePlayer, string.Format(UI_Status_ID, statusId)));
                    return;
                }
                Debug($"RepeatUpdateUI {ActiveStatusIds.Length} RowUpdate={rowsNeedUpdate} PrivUpdate={privForceUpdate}");
                var index = ModifiersCount;
                foreach (var statusId in ActiveStatusIds)
                {
                    var status = Data.Statuses.GetValueOrDefault(statusId); if (status == null) { continue; }
                    if (!status.PluginIsLoaded)
                    {
                        RemoveStatus(statusId);
                        continue;
                    }
                    var data = Data.Player.GetValueOrDefault(UserId)?.GetValueOrDefault(statusId); if (data == null) { continue; }
                    var titleLocalized = data.Title != null ? data.Title : PLUGIN.Lang(status.Plugin, status.Title, UserId);
                    var textOrDurationLocalized = data.Text != null ? data.Text : status.Text != null ? PLUGIN.Lang(status.Plugin, status.Text, UserId) : data.Duration < int.MaxValue ? FormatDuration(data.Duration) : string.Empty;
                    CuiHelper.DestroyUi(basePlayer, string.Format(UI_Status_ID, statusId));
                    CuiHelper.AddUi(basePlayer, UI_Status(status, index, titleLocalized, textOrDurationLocalized, data));
                    var imagePath = data.ImagePathOrUrl ?? status.ImageLibraryNameOrAssetPath;
                    var imageData = data.ImagePathOrUrl != null ? data.ImageData : status.ImageData;
                    if (!string.IsNullOrWhiteSpace(imagePath) && !string.IsNullOrWhiteSpace(imageData))
                    {
                        CuiHelper.AddUi(basePlayer, UI_StatusIcon(Data.Statuses[statusId], imagePath, imageData, data.IconColor ?? status.IconColor));
                    }
                    index++;
                }
                rowsNeedUpdate = false;
                privForceUpdate = false;
                if (ActiveStatusIds.Length <= 0)
                {
                    StopWorking();
                }
            }

            private void RepeatCheckModifiers()
            {
                smallModifiersCount = 0;

                if (basePlayer.metabolism.bleeding.value >= 1)
                {
                    smallModifiersCount++; // bleeding
                }
                if (basePlayer.metabolism.temperature.value < 5)
                {
                    smallModifiersCount++; // toocold
                }
                if (basePlayer.metabolism.temperature.value > 40)
                {
                    smallModifiersCount++; // toohot
                }
                if (basePlayer.currentComfort > 0)
                {
                    smallModifiersCount++; // comfort
                }
                if (basePlayer.metabolism.calories.value < 40)
                {
                    smallModifiersCount++; // starving
                }
                if (basePlayer.metabolism.hydration.value < 35)
                {
                    smallModifiersCount++; // dehydrated
                }
                if (basePlayer.metabolism.radiation_poison.value > 0)
                {
                    smallModifiersCount++; // radiation
                }
                if (basePlayer.metabolism.wetness.value >= 0.02)
                {
                    smallModifiersCount++; // wet
                }
                if (basePlayer.metabolism.oxygen.value < 1f)
                {
                    smallModifiersCount++; // drowning
                }
                if (basePlayer.currentCraftLevel > 0)
                {
                    smallModifiersCount++; // workbench
                }
                if (basePlayer.inventory.crafting.queue.Count > 0)
                {
                    smallModifiersCount++; // crafting
                }
                if (basePlayer.modifiers.ActiveModifierCoount > 0)
                {
                    smallModifiersCount++; // modifiers
                }
                if (basePlayer.HasPlayerFlag(BasePlayer.PlayerFlags.SafeZone))
                {
                    smallModifiersCount++; // safezone
                }
                if (basePlayer.isMounted && (basePlayer.GetMountedVehicle() != null || basePlayer.GetMounted() is RidableHorse))
                {
                    smallModifiersCount++; // mounted
                }

                // Other stats
                if (forceCheckMods || nextBigStatusUpdate < Time.realtimeSinceStartup)
                {
                    var stillInBuildingPriv = false;

                    bigModifiersCount = 0;
                    var priv = basePlayer.GetBuildingPrivilege();
                    if (priv != null && priv.IsAuthed(basePlayer))
                    {
                        bigModifiersCount++; // buildpriv authed
                        bigModifiersCount++; // upkeep
                        inBuildingPriv = true;
                        stillInBuildingPriv = true;
                    }
                    else if (priv != null && !priv.IsAuthed(basePlayer) && basePlayer.GetActiveItem()?.info.shortname == "hammer")
                    {
                        bigModifiersCount++; // buildpriv not authed
                        inBuildingPriv = true;
                        stillInBuildingPriv = true;
                    }
                    if (inBuildingPriv && !stillInBuildingPriv) // Raid Protection relies on this
                    {
                        Interface.CallHook("OnStatusEnd", UserId, "simplestatus.buildingpriv", 0);
                        inBuildingPriv = false;
                    }
                    if ((priv == null && lastPrivId != 0) || (priv != null && lastPrivId != priv.net.ID.Value))
                    {
                        lastPrivId = priv == null ? 0 : priv.net.ID.Value;
                        privForceUpdate = true;
                    }
                    nextBigStatusUpdate = Time.realtimeSinceStartup + 2;
                }
                itemStatuses.RemoveAll(x => x.Value < Time.realtimeSinceStartup);
                smallModifiersCount += itemStatuses.Count;
                if (ModifiersCount != previousModifiersCount)
                {
                    previousModifiersCount = ModifiersCount;
                    rowsNeedUpdate = true;
                }
                forceCheckMods = false;
            }

            private string FormatDuration(int duration)
            {
                var ts = TimeSpan.FromSeconds(duration);
                return ts.TotalDays >= 1 ? $"{Math.Floor(ts.TotalDays):0}d {ts.Hours}h {ts.Minutes}m" :
                    ts.TotalHours >= 1 ? $"{Math.Floor(ts.TotalHours):0}h {ts.Minutes}m" :
                    ts.TotalMinutes >= 1 ? $"{Math.Floor(ts.TotalMinutes):0}m" :
                    $"{ts.TotalSeconds:0}";
            }

            #endregion

            #region Public

            public void SetStatus(string statusId, int duration, bool pauseOffline, bool resuming = false)
            {
                if (!Data.Player.ContainsKey(UserId))
                {
                    Data.Player[UserId] = new Dictionary<string, PlayerStatusInfo>();
                }
                var data = Data.Player[UserId].GetValueOrDefault(statusId);
                if (data == null)
                {
                    Debug($"Set status new PauseOffline={pauseOffline}");
                    Data.Player[UserId][statusId] = new PlayerStatusInfo() { Duration = duration, EndTime = pauseOffline ? null : (DateTime?) DateTime.Now.AddSeconds(duration) };
                    Debug($"End time is {Data.Player[UserId][statusId].EndTime?.ToShortTimeString()}");
                    Interface.CallHook("OnStatusSet", UserId, statusId, duration);
                    rowsNeedUpdate = true;
                    if (!IsInvoking(nameof(RepeatUpdateUI))) { StartWorking(); }
                }
                else if (data != null && !data.IsPastEndTime)
                {
                    Debug($"Set status existing end time is {data.EndTime?.ToShortTimeString()}");
                    if (resuming)
                    {
                        data.Duration = data.DurationUntilEndTime;
                    }
                    else
                    {
                        data.Duration = duration;
                        data.EndTime = pauseOffline ? null : (DateTime?)DateTime.Now.AddSeconds(duration);
                    }
                    Interface.CallHook("OnStatusSet", basePlayer, statusId, duration);
                    rowsNeedUpdate = true;
                    if (!IsInvoking(nameof(RepeatUpdateUI))) { StartWorking(); }
                }
                else if (data != null && data.IsPastEndTime)
                {
                    Debug($"Cancelling {statusId} past end time");
                    RemoveStatus(statusId);
                }
                Debug($"Player {basePlayer.displayName} has {(Data.Player.ContainsKey(basePlayer.userID) ? Data.Player[basePlayer.userID].Count : 0)} statuses");
            }

            public void RemoveStatus(string statusId)
            {
                Debug($"Remove status invoked");
                if (Data?.Player.ContainsKey(UserId) ?? false)
                {
                    Debug($"Removing {statusId}");
                    var status = Data.Player[UserId].GetValueOrDefault(statusId); if (status == null) { return; }
                    Data.Player[UserId].Remove(statusId);
                    CuiHelper.DestroyUi(basePlayer, string.Format(UI_Status_ID, statusId));
                    rowsNeedUpdate = true;
                    Interface.CallHook("OnStatusEnd", UserId, statusId, status.Duration);
                    if (Data.Player.GetValueOrDefault(UserId)?.Count <= 0)
                    {
                        Debug($"All statuses removed");
                        Data.Player.Remove(UserId);
                        StopWorking();
                    }
                }
            }

            public void SetStatusColor(string statusId, string color = null)
            {
                if (Data.Statuses.ContainsKey(statusId) && Data.Player.ContainsKey(UserId) && Data.Player[UserId].ContainsKey(statusId))
                {
                    var needsUpdate = false;
                    var data = Data.Player[UserId][statusId];
                    var status = Data.Statuses[statusId];
                    if (color == RESET_KEYWORD) { color = status.Color; }
                    if (color != null && data.Color != color)
                    {
                        needsUpdate = true;
                        data.Color = color;
                        Interface.CallHook("OnStatusUpdate", UserId, statusId, "color", color);
                    }
                    if (needsUpdate)
                    {
                        // Update
                        if (Data.PlayersHiding.Contains(basePlayer.userID)) { return; }
                        rowsNeedUpdate = true;
                    }
                }
            }

            public void SetStatusTitle(string statusId, string title = null, string color = null)
            {
                if (Data.Statuses.ContainsKey(statusId) && Data.Player.ContainsKey(UserId) && Data.Player[UserId].ContainsKey(statusId))
                {
                    var needsUpdate = false;
                    var data = Data.Player[UserId][statusId];
                    var status = Data.Statuses[statusId];
                    var updateTitle = data.Title ?? status.Title;
                    var updateTitleColor = data.TitleColor ?? status.TitleColor;
                    if (title == RESET_KEYWORD) { title = status.Title; }
                    if (color == RESET_KEYWORD) { color = status.TitleColor; }
                    if (title != null && data.Title != title)
                    {
                        needsUpdate = true;
                        data.Title = title;
                        updateTitle = title;
                        Interface.CallHook("OnStatusUpdate", UserId, statusId, "title", title);
                    }
                    if (color != null && data.TitleColor != color)
                    {
                        needsUpdate = true;
                        data.TitleColor = color;
                        updateTitleColor = color;
                        Interface.CallHook("OnStatusUpdate", UserId, statusId, "titleColor", color);
                    }
                    if (needsUpdate)
                    {
                        // Update
                        if (Data.PlayersHiding.Contains(basePlayer.userID)) { return; }
                        var titleLocalized = string.IsNullOrWhiteSpace(updateTitle) ? updateTitle : PLUGIN.Lang(status.Plugin, updateTitle, UserId);
                        CuiHelper.DestroyUi(basePlayer, string.Format(UI_Title_ID, statusId));
                        CuiHelper.AddUi(basePlayer, UI_StatusTitle(Data.Statuses[statusId], titleLocalized, updateTitleColor));
                    }
                }
            }

            public void SetStatusText(string statusId, string text = null, string color = null)
            {
                if (Data.Statuses.ContainsKey(statusId) && Data.Player.ContainsKey(UserId) && Data.Player[UserId].ContainsKey(statusId))
                {
                    var needsUpdate = false;
                    var ignoreText = text == null;
                    var data = Data.Player[UserId][statusId];
                    var status = Data.Statuses[statusId];
                    var updateText = data.Text ?? status.Text;
                    var updateTextColor = data.TextColor ?? status.TextColor;
                    if (text == RESET_KEYWORD) { text = status.Text; }
                    if (color == RESET_KEYWORD) { color = status.TextColor; }
                    if (!ignoreText && data.Text != text)
                    {
                        needsUpdate = true;
                        data.Text = text;
                        updateText = text;
                        Interface.CallHook("OnStatusUpdate", UserId, statusId, "text", text);
                    }
                    if (color != null && data.TextColor != color)
                    {
                        needsUpdate = true;
                        data.TextColor = color;
                        updateTextColor = color;
                        Interface.CallHook("OnStatusUpdate", UserId, statusId, "textColor", color);
                    }
                    if (needsUpdate)
                    {
                        // Update
                        if (Data.PlayersHiding.Contains(basePlayer.userID)) { return; }
                        if (updateText == null && data.Duration != int.MaxValue)
                        {
                            updateText = FormatDuration(data.Duration);
                        }
                        else
                        {
                            updateText = string.IsNullOrWhiteSpace(updateText) ? updateText : PLUGIN.Lang(status.Plugin, updateText, UserId);
                        }
                        CuiHelper.DestroyUi(basePlayer, string.Format(UI_Text_ID, statusId));
                        CuiHelper.AddUi(basePlayer, UI_StatusText(Data.Statuses[statusId], updateText, updateTextColor));
                    }
                }
            }

            public void SetStatusIcon(string statusId, string imageLibraryNameOrAssetPath = null, string color = null)
            {
                if (Data.Statuses.ContainsKey(statusId) && Data.Player.ContainsKey(UserId) && Data.Player[UserId].ContainsKey(statusId))
                {
                    var needsUpdate = false;
                    var data = Data.Player[UserId][statusId];
                    var status = Data.Statuses[statusId];
                    var updateIcon = data.ImagePathOrUrl ?? status.ImageLibraryNameOrAssetPath;
                    var updateIconColor = data.IconColor ?? status.IconColor;
                    if (imageLibraryNameOrAssetPath == RESET_KEYWORD) { imageLibraryNameOrAssetPath = status.ImageLibraryNameOrAssetPath; }
                    if (color == RESET_KEYWORD) { color = status.IconColor; }
                    if (imageLibraryNameOrAssetPath != null && data.ImagePathOrUrl != imageLibraryNameOrAssetPath)
                    {
                        needsUpdate = true;
                        data.ImagePathOrUrl = imageLibraryNameOrAssetPath;
                        updateIcon = imageLibraryNameOrAssetPath;
                        Interface.CallHook("OnStatusUpdate", UserId, statusId, "icon", imageLibraryNameOrAssetPath);
                    }
                    if (color != null && data.IconColor != color)
                    {
                        needsUpdate = true;
                        data.IconColor = color;
                        updateIconColor = color;
                        Interface.CallHook("OnStatusUpdate", UserId, statusId, "iconColor", color);
                    }
                    if (needsUpdate)
                    {
                        // Update
                        if (Data.PlayersHiding.Contains(basePlayer.userID)) { return; }
                        CuiHelper.DestroyUi(basePlayer, string.Format(UI_Icon_ID, statusId));
                        var imageData = data.ImagePathOrUrl != null ? data.ImageData : status.ImageData;
                        if (!string.IsNullOrWhiteSpace(updateIcon) && !string.IsNullOrWhiteSpace(imageData))
                        {
                            CuiHelper.AddUi(basePlayer, UI_StatusIcon(Data.Statuses[statusId], data.ImagePathOrUrl ?? status.ImageLibraryNameOrAssetPath, imageData, updateIconColor));
                        }
                    }
                }
            }

            #endregion
        }
    }
}

namespace Oxide.Plugins
{
    partial class SimpleStatus : CovalencePlugin
    {
        protected static Dictionary<string, string> CachedUI = new Dictionary<string, string>();

        protected static readonly string UI_Base_ID = "ss";
        protected static readonly string UI_Status_ID = "ss.{0}";
        protected static readonly string UI_Content_ID = "ss.{0}.content";
        protected static readonly string UI_Title_ID = "ss.{0}.title";
        protected static readonly string UI_Icon_ID = "ss.{0}.icon";
        protected static readonly string UI_Text_ID = "ss.{0}.text";
        protected static readonly string RESET_KEYWORD = "$reset$";

        public static string GetImage(string path)
        {
            if (path.IsRawImage()) { path = path.AsRawImage(); }
            return PLUGIN.ImageLibrary?.Call<string>("GetImage", path);
        }

        protected static class UI
        {
            public static int EntryH = 26;
            public static int EntryGap = 2;
            public static int Padding = 8;
            public static int ImageSize = 16;
            public static int ImageMargin = 5;
        }

        protected static string UI_Base()
        {
            if (!CachedUI.ContainsKey(UI_Base_ID))
            {
                var container = new CuiElementContainer();
                var offX = -16;
                var offY = 100;
                var w = 192;
                var eh = 26;
                var eg = 2;
                var numEntries = 12;
                var h = (eh + eg) * numEntries - offY;
                container.Add(new CuiElement
                {
                    Name = UI_Base_ID,
                    Parent = "Under",
                    Components =
                    {
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "1 0",
                            AnchorMax = "1 0",
                            OffsetMin = $"{offX-w} {offY}",
                            OffsetMax = $"{offX} {offY+h}"
                        }
                    }
                });
                CachedUI[UI_Base_ID] = container.ToJson();
            }
            return CachedUI[UI_Base_ID];
        }

        protected static string UI_Status(StatusInfo status, int index, string titleLocalized, string textLocalized, PlayerStatusInfo data)
        {
            var uiStatusId = string.Format(UI_Status_ID, status.Id);
            var bottom = index * (UI.EntryGap + UI.EntryH);
            var top = bottom + UI.EntryH;
            //var imageData = data.ImagePathOrUrl != null ? data.ImageData : status.ImageData;
            //var spriteOrPng = (imageData?.IsAssetPath() ?? false) ? "\"sprite\":" : (imageData?.IsItemId() ?? false) ? "\"itemid\":" : "\"png\":";
            //var imageType = (imageData?.IsRawImage() ?? false) ? "\"CuiRawImageComponent\"" : "\"CuiImageComponent\"";
            if (!CachedUI.ContainsKey(uiStatusId))
            {
                var uiContentId = string.Format(UI_Content_ID, status.Id);
                //var uiIconId = string.Format(UI_Icon_ID, status.Id);
                var uiText1Id = string.Format(UI_Title_ID, status.Id);
                var uiText2Id = string.Format(UI_Text_ID, status.Id);
                var container = new CuiElementContainer();
                container.Add(new CuiElement
                {
                    Parent = UI_Base_ID,
                    Name = uiStatusId,
                    Components =
                    {
                        new CuiImageComponent
                        {
                            Color = "{color}",
                            Sprite = "assets/content/ui/ui.background.tile.psd",
                            Material = "assets/content/ui/namefontmaterial.mat"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 0",
                            OffsetMin = "0 {bottom}",
                            OffsetMax = "0 {top}"
                        }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = uiStatusId,
                    Name = uiContentId,
                    Components =
                    {
                        new CuiRectTransformComponent
                        {
                            OffsetMin = $"{UI.Padding+UI.ImageSize+UI.ImageMargin-3} {0}",
                            OffsetMax = $"{-UI.Padding} {0}"
                        }
                    }
                });
                //if (!string.IsNullOrWhiteSpace(imageData))
                //{
                //    var imageComponent = new CuiImageComponent
                //    {
                //        Color = status.IconColor
                //    };
                //    if (imageData.IsAssetPath())
                //    {
                //        imageComponent.Sprite = "{imageData}";
                //    }
                //    else
                //    {
                //        imageComponent.Png = "{imageData}";
                //    }
                //    container.Add(new CuiElement
                //    {
                //        Parent = uiStatusId,
                //        Name = uiIconId,
                //        Components =
                //        {
                //            imageComponent,
                //            new CuiRectTransformComponent
                //            {
                //                AnchorMin = "0 0.5",
                //                AnchorMax = "0 0.5",
                //                OffsetMin = $"{UI.Padding-4} {-UI.ImageSize/2}",
                //                OffsetMax = $"{UI.Padding+UI.ImageSize-4} {UI.ImageSize/2}"
                //            }
                //        }
                //    });
                //}
                container.Add(new CuiElement
                {
                    Parent = uiContentId,
                    Name = uiText1Id,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "{title}",
                            FontSize = 12,
                            Color = "{titleColor}",
                            Align = TextAnchor.MiddleLeft
                        }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = uiContentId,
                    Name = uiText2Id,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "{text}",
                            FontSize = 12,
                            Color = "{textColor}",
                            Align = TextAnchor.MiddleRight
                        }
                    }
                });
                CachedUI[uiStatusId] = container.ToJson();
            }
            return CachedUI[uiStatusId]
                .Replace("{color}", data?.Color ??  status.Color)
                .Replace("{title}", titleLocalized)
                .Replace("{titleColor}", data?.TitleColor ?? status.TitleColor)
                .Replace("{text}", textLocalized)
                .Replace("{textColor}", data?.TextColor ?? status.TextColor)
                .Replace("{bottom}", bottom.ToString())
                .Replace("{top}", top.ToString());
        }

        protected static string UI_StatusIcon(StatusInfo status, string imageUrlOrAssetPath, string imageData, string iconColor)
        {
            var parentId = string.Format(UI_Status_ID, status.Id);
            var uiIconId = string.Format(UI_Icon_ID, status.Id);
            var spriteOrPng = imageUrlOrAssetPath.IsAssetPath() ? "\"sprite\":" : imageUrlOrAssetPath.IsItemId() ? "\"itemid\":" : "\"png\":";
            var imageType = imageUrlOrAssetPath.IsRawImage() ? "\"UnityEngine.UI.RawImage\"" : "\"UnityEngine.UI.Image\"";
            if (!CachedUI.ContainsKey(uiIconId))
            {
                var container = new CuiElementContainer();
                if (status.HasImage)
                {
                    var imageComponent = new CuiImageComponent
                    {
                        Color = "{iconColor}"
                    };
                    if (imageData.IsAssetPath())
                    {
                        imageComponent.Sprite = "{imageData}";
                    }
                    else
                    {
                        imageComponent.Png = "{imageData}";
                    }
                    container.Add(new CuiElement
                    {
                        Parent = parentId,
                        Name = uiIconId,
                        Components =
                        {
                            imageComponent,
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0.5",
                                AnchorMax = "0 0.5",
                                OffsetMin = $"{UI.Padding-4} {-UI.ImageSize/2}",
                                OffsetMax = $"{UI.Padding+UI.ImageSize-4} {UI.ImageSize/2}"
                            }
                        }
                    });
                }
                CachedUI[uiIconId] = container.ToJson();
            }
            return CachedUI[uiIconId]
                .Replace("\"sprite\":", spriteOrPng)
                .Replace("\"png\":", spriteOrPng)
                .Replace("\"UnityEngine.UI.Image\"", imageType)
                .Replace("\"UnityEngine.UI.RawImage\"", imageType)
                .Replace("\"itemid\":", spriteOrPng)
                .Replace("{imageData}", imageData)
                .Replace("{iconColor}", iconColor);
        }

        protected static string UI_StatusTitle(StatusInfo status, string titleLocalized, string titleColor)
        {
            var parentId = string.Format(UI_Content_ID, status.Id);
            var uiTitleId = string.Format(UI_Title_ID, status.Id);
            if (!CachedUI.ContainsKey(uiTitleId))
            {
                var container = new CuiElementContainer();
                container.Add(new CuiElement
                {
                    Parent = parentId,
                    Name = uiTitleId,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Color = "{color}",
                            Text = "{title}",
                            FontSize = 12,
                            Align = TextAnchor.MiddleLeft
                        }
                    }
                });
                CachedUI[uiTitleId] = container.ToJson();
            }
            return CachedUI[uiTitleId]
                .Replace("{title}", titleLocalized)
                .Replace("{color}", titleColor);
        }

        protected static string UI_StatusText(StatusInfo status, string textLocalized, string textColor)
        {
            var parentId = string.Format(UI_Content_ID, status.Id);
            var uiTextId = string.Format(UI_Text_ID, status.Id);
            if (!CachedUI.ContainsKey(uiTextId))
            {
                var container = new CuiElementContainer();
                container.Add(new CuiElement
                {
                    Parent = parentId,
                    Name = uiTextId,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Color = "{color}",
                            Text = "{text}",
                            FontSize = 12,
                            Align = TextAnchor.MiddleRight
                        }
                    }
                });
                CachedUI[uiTextId] = container.ToJson();
            }
            return CachedUI[uiTextId]
                .Replace("{text}", textLocalized)
                .Replace("{color}", textColor);
        }
    }
}
