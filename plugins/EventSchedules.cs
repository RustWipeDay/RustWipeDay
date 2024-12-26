using System;
using System.Linq;
using UnityEngine;
using Rust;
using Oxide.Core;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Oxide.Plugins
{
    public static class PrefabConstants
    {
        public const string EventHelicopter =
            "assets/bundled/prefabs/world/event_helicopter.prefab";
        public const string EventEaster = "assets/prefabs/misc/easter/event_easter.prefab";
        public const string EventCargoship = "assets/bundled/prefabs/world/event_cargoship.prefab";
        public const string EventCargoheli = "assets/bundled/prefabs/world/event_cargoheli.prefab";
        public const string EventAirdrop = "assets/bundled/prefabs/world/event_airdrop.prefab";
        public const string EventXmas = "assets/prefabs/misc/xmas/event_xmas.prefab";
        public const string EventHalloween = "assets/prefabs/misc/halloween/event_halloween.prefab";
        public const string EventRoadbradley =
            "assets/bundled/prefabs/world/event_roadbradley.prefab";
        public const string EventF15e = "assets/bundled/prefabs/world/event_f15e.prefab";
    }

    public static class FriendlyPrefabNames
    {
        public const string EventHelicopter = "Helicopter Event";
        public const string EventEaster = "Easter Event";
        public const string EventCargoship = "Cargoship Event";
        public const string EventCargoheli = "Chinook Event";
        public const string EventAirdrop = "Airdrop Event";
        public const string EventXmas = "Christmas Event";
        public const string EventHalloween = "Halloween Event";
        public const string EventRoadbradley = "Roadside Bradley Event";
        public const string EventF15e = "F15E Event";
    }

    public static class PrefabCollections
    {
        public static readonly string[] EventPrefabs =
        {
            PrefabConstants.EventEaster,
            PrefabConstants.EventHalloween,
            PrefabConstants.EventXmas,
            PrefabConstants.EventHelicopter,
            PrefabConstants.EventCargoship,
            PrefabConstants.EventCargoheli,
            PrefabConstants.EventAirdrop,
            PrefabConstants.EventRoadbradley,
            PrefabConstants.EventF15e
        };

        public static readonly Dictionary<string, string> Lookup = new Dictionary<string, string>()
        {
            { PrefabConstants.EventEaster, FriendlyPrefabNames.EventEaster },
            { PrefabConstants.EventHalloween, FriendlyPrefabNames.EventHalloween },
            { PrefabConstants.EventXmas, FriendlyPrefabNames.EventXmas },
            { PrefabConstants.EventHelicopter, FriendlyPrefabNames.EventHelicopter },
            { PrefabConstants.EventCargoship, FriendlyPrefabNames.EventCargoship },
            { PrefabConstants.EventCargoheli, FriendlyPrefabNames.EventCargoheli },
            { PrefabConstants.EventAirdrop, FriendlyPrefabNames.EventAirdrop },
            { PrefabConstants.EventRoadbradley, FriendlyPrefabNames.EventRoadbradley },
            { PrefabConstants.EventF15e, FriendlyPrefabNames.EventF15e }
        };
    }

    public class EventScheduleSettings
    {
        [JsonProperty("Minimum Hours Between")]
        public float MinimumHoursBetween { get; set; } = 12f;
        [JsonProperty("Maximum Hours Between")]
        public float MaximumHoursBetween { get; set; } = 24f;
    }

    public class BradleySpawnerSettings
    {
        [JsonProperty("Respawn Delay Minutes")]
        public float RespawnDelayMinutes { get; set; } = 60f;
        [JsonProperty("Respawn Delay Variance")]
        public float RespawnDelayVariance { get; set; } = 1f;
        public bool Enabled = true;
    }

    public class EventSchedulesConfig
    {
        [JsonProperty("Bradley Spawner Settings")]
        public BradleySpawnerSettings BradleySpawnerSettings { get; set; } =
            new BradleySpawnerSettings();

        [JsonProperty("Event Schedule Settings")]
        public Dictionary<string, EventScheduleSettings> EventScheduleSettings { get; set; } =
            new Dictionary<string, EventScheduleSettings>()
            {
                { FriendlyPrefabNames.EventEaster, new EventScheduleSettings() },
                { FriendlyPrefabNames.EventHalloween, new EventScheduleSettings() },
                { FriendlyPrefabNames.EventXmas, new EventScheduleSettings() },
                {
                    FriendlyPrefabNames.EventHelicopter,
                    new EventScheduleSettings() { MinimumHoursBetween = 48f, MaximumHoursBetween = 72f }
                },
                {
                    FriendlyPrefabNames.EventCargoship,
                    new EventScheduleSettings() { MinimumHoursBetween = 60f, MaximumHoursBetween = 104f }
                },
                {
                    FriendlyPrefabNames.EventCargoheli,
                    new EventScheduleSettings() { MinimumHoursBetween = 36f, MaximumHoursBetween = 72f }
                },
                {
                    FriendlyPrefabNames.EventAirdrop,
                    new EventScheduleSettings() { MinimumHoursBetween = 12f, MaximumHoursBetween = 24f }
                },
                {
                    FriendlyPrefabNames.EventRoadbradley,
                    new EventScheduleSettings() { MinimumHoursBetween = 10f, MaximumHoursBetween = 24f }
                },
                {
                    FriendlyPrefabNames.EventF15e,
                    new EventScheduleSettings() { MinimumHoursBetween = 20f, MaximumHoursBetween = 40f }
                }
            };
    }

    [Info("Event Schedules", "ODS", "0.2.0")]
    [Description("Allows admins to control the timers of server events.")]
    public class EventSchedules : RustPlugin
    {
        private EventSchedulesConfig _eventSchedulesConfig;

        private Dictionary<EventSchedule, EventScheduleSettings> _originalEventScheduleSettings =
            new Dictionary<EventSchedule, EventScheduleSettings>();

        private BradleySpawnerSettings _originalBradleySpawnerSettings = new BradleySpawnerSettings();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _eventSchedulesConfig = Config.ReadObject<EventSchedulesConfig>();
                if (_eventSchedulesConfig == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch (Exception ex)
            {
                PrintError($"The configuration file is corrupted. \n{ex}");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file.");
            _eventSchedulesConfig = new EventSchedulesConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_eventSchedulesConfig);

        private object OnEventTrigger(TriggeredEventPrefab eventPrefab)
        {
            Puts($"Spawning {eventPrefab}");
            return null;
        }

        private void OnServerInitialized()
        {
            Puts("Loading new event schedules.");
            var eventSchedules = UnityEngine.Object.FindObjectsOfType<EventSchedule>();
            foreach (var eventSchedule in eventSchedules)
            {
                _originalEventScheduleSettings.Add(
                    eventSchedule,
                    new EventScheduleSettings()
                    {
                        MinimumHoursBetween = eventSchedule.minimumHoursBetween,
                        MaximumHoursBetween = eventSchedule.maxmumHoursBetween
                    }
                );

                try
                {
                    var eventSettings = _eventSchedulesConfig.EventScheduleSettings[
                        PrefabCollections.Lookup[eventSchedule.name]
                    ];

                    eventSchedule.minimumHoursBetween = eventSettings.MinimumHoursBetween;
                    eventSchedule.maxmumHoursBetween = eventSettings.MaximumHoursBetween;
                    eventSchedule.hoursRemaining = UnityEngine.Random.Range(eventSchedule.minimumHoursBetween, eventSchedule.maxmumHoursBetween);
                    Puts($"{eventSchedule.name} {eventSchedule.enabled} {eventSchedule.hoursRemaining}");
                }
                catch (Exception ex) { 
                    Puts(ex.Message);
                }
            }

            _originalBradleySpawnerSettings.Enabled = ConVar.Bradley.enabled;
            _originalBradleySpawnerSettings.RespawnDelayMinutes = ConVar.Bradley.respawnDelayMinutes;
            _originalBradleySpawnerSettings.RespawnDelayVariance = ConVar.Bradley.respawnDelayVariance;

            try {
                var bradleySpawnerSettings = _eventSchedulesConfig.BradleySpawnerSettings;
                ConVar.Bradley.enabled = bradleySpawnerSettings.Enabled;
                ConVar.Bradley.respawnDelayMinutes = bradleySpawnerSettings.RespawnDelayMinutes;
                ConVar.Bradley.respawnDelayVariance = bradleySpawnerSettings.RespawnDelayVariance;
            } catch (Exception ex) { }
        }

        private void Unload()
        {
            Puts("Restoring orginal event schedules.");
            foreach (var originalEventSettingKvp in _originalEventScheduleSettings)
            {
                var eventSchedule = originalEventSettingKvp.Key;
                var eventSettings = originalEventSettingKvp.Value;
                eventSchedule.minimumHoursBetween = eventSettings.MinimumHoursBetween;
                eventSchedule.maxmumHoursBetween = eventSettings.MaximumHoursBetween;
                eventSchedule.hoursRemaining = UnityEngine.Random.Range(eventSchedule.minimumHoursBetween, eventSchedule.maxmumHoursBetween);
                Puts($"{eventSchedule.name} {eventSchedule.hoursRemaining}");
            }

            ConVar.Bradley.enabled = _originalBradleySpawnerSettings.Enabled;
            ConVar.Bradley.respawnDelayMinutes = _originalBradleySpawnerSettings.RespawnDelayMinutes;
            ConVar.Bradley.respawnDelayVariance = _originalBradleySpawnerSettings.RespawnDelayVariance;
        }
    }
}
