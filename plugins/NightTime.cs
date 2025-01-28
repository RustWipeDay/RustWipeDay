using Oxide.Core.Plugins;
using Oxide.Core;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("NightTime", "Rust Wipe Day", "1.0.0")]
    public class NightTime : RustPlugin
    {
        private bool _debug = false; // Disable debug mode
        private bool hasAnnouncedSunset = false;
        private List<LootContainer> trackedLootContainers = new List<LootContainer>();

        private void OnServerInitialized()
        {
            Debug("Initializing server.");
            trackedLootContainers = GameObject.FindObjectsOfType<LootContainer>().ToList();
            Subscribe(nameof(OnEntitySpawned));
            TOD_Sky.Instance.Night.AmbientMultiplier = 1f;
            TOD_Sky.Instance.Night.LightIntensity = 1f;
        }

        [HookMethod("OnTimeSunset")]
        public void OnTimeSunset()
        {
            if (!hasAnnouncedSunset)
            {
                Debug("Sunset detected.");
                hasAnnouncedSunset = true;
                Server.Broadcast("<color=#c45508>[RustWipeDay]</color>: The sun has set, gather rates have increased to 5x until morning. Loot containers have been populated with 5x loot!");
                BatchUpdateLootContainers(populate: true); // Populate loot at sunset.
            }
        }

        [HookMethod("OnTimeSunrise")]
        public void OnTimeSunrise()
        {
            Debug("Sunrise detected.");
            hasAnnouncedSunset = false; // Reset the flag for the next sunset.
            Server.Broadcast("<color=#c45508>[RustWipeDay]</color>: Morning has arrived, gather rates and loot have been reset to 2x until night time.");
            BatchUpdateLootContainers(populate: false); // Spawn loot at sunrise.
        }

        public object OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            Debug("OnDispenserGather called with item: {0}, amount: {1}", item?.info?.shortname, item?.amount);
            if (IsNightTime())
            {
                item.amount = (int)(2.5 * item.amount);
                Debug("Adjusted item amount to {0}", item.amount);
            }

            return null;
        }

        private void OnLootEntity(BasePlayer player, LootContainer container)
        {
            Debug("LootEntity called for player: {0}, container: {1}", player?.displayName, container?.name);
            trackedLootContainers.Remove(container);
        }

        private void OnEntitySpawned(LootContainer entity)
        {
            if (entity is LootContainer lootContainer && !trackedLootContainers.Contains(lootContainer))
            {
                Debug("Tracking new loot container: {0}", lootContainer?.name);
                trackedLootContainers.Add(lootContainer);
                if (IsNightTime())
                {
                    lootContainer.Invoke(lootContainer.PopulateLoot, 0f);
                    Debug("Populated loot for container: {0}", lootContainer?.name);
                }
            }
        }

        private void OnEntityKill(LootContainer entity)
        {
            if (entity is LootContainer lootContainer)
            {
                Debug("Removing loot container: {0}", lootContainer?.name);
                trackedLootContainers.Remove(lootContainer);
            }
        }

        private bool IsNightTime()
        {
            return TOD_Sky.Instance.Cycle.Hour < TOD_Sky.Instance.SunriseTime || TOD_Sky.Instance.Cycle.Hour > TOD_Sky.Instance.SunsetTime;
        }

        private void BatchUpdateLootContainers(bool populate)
        {
            int batchSize = 100;
            for (int i = 0; i < trackedLootContainers.Count; i += batchSize)
            {
                var batch = trackedLootContainers.Skip(i).Take(batchSize).ToList();
                timer.Once(0.1f * (i / batchSize), () =>
                {
                    foreach (var lootContainer in batch)
                    {
                        Debug("Updating container: {0}, populate: {1}", lootContainer?.name, populate);
                        if (populate)
                        {
                            lootContainer.Invoke(lootContainer.PopulateLoot, 0f);
                        }
                        else
                        {
                            lootContainer.Invoke(lootContainer.SpawnLoot, 0f);
                        }
                    }
                });
            }
        }

        void Unload()
        {
            Debug("Unloading plugin.");
            trackedLootContainers.Clear();
        }

        private void Debug(string message, params object[] args)
        {
            if (_debug)
            {
                Puts(message, args);
            }
        }
    }
}
