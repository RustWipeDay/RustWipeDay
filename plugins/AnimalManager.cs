using Oxide.Core;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Animal Manager", "Rust Wipe Day", "1.8.1")]
    [Description("Manages animal configurations, scaling incoming and outgoing damage per entity.")]
    public class AnimalManager : RustPlugin
    {
        private ConfigData animalConfigs;

        /// <summary>
        /// Represents the plugin configuration for managing animals.
        /// </summary>
        class ConfigData
        {
            public Dictionary<string, AnimalConfig> Animals { get; set; } = new Dictionary<string, AnimalConfig>();
        }

        /// <summary>
        /// Defines individual animal configurations, including incoming and outgoing damage scaling.
        /// </summary>
        class AnimalConfig
        {
            /// <summary>
            /// Incoming damage scale factor for the animal.
            /// </summary>
            public float IncomingDamageScale { get; set; } = 1.0f;

            /// <summary>
            /// Outgoing damage scale factor for the animal.
            /// </summary>
            public float OutgoingDamageScale { get; set; } = 1.0f;
        }

        private const string adminPermission = "animalmanager.admin";

        /// <summary>
        /// Initializes the plugin, setting up permissions and loading configurations.
        /// </summary>
        void Init()
        {
            Puts("Initializing Animal Manager Plugin...");
            permission.RegisterPermission(adminPermission, this);
            LoadConfigValues();
            CacheAnimalPrefabs();
        }

        /// <summary>
        /// Loads the default configuration file with predefined values.
        /// </summary>
        protected override void LoadDefaultConfig()
        {
            Puts("Creating default configuration...");
            animalConfigs = new ConfigData();
            CacheAnimalPrefabs();
            SaveConfigValues();
        }

        /// <summary>
        /// Loads configuration values from the configuration file.
        /// </summary>
        private void LoadConfigValues()
        {
            try
            {
                animalConfigs = Config.ReadObject<ConfigData>() ?? new ConfigData();
                Puts("Configuration loaded successfully.");
            }
            catch (System.Exception ex)
            {
                Puts($"Error loading configuration: {ex.Message}");
                LoadDefaultConfig();
            }
        }

        /// <summary>
        /// Saves the current configuration values to the configuration file.
        /// </summary>
        private void SaveConfigValues()
        {
            try
            {
                Config.WriteObject(animalConfigs, true);
                Puts("Configuration saved successfully.");
            }
            catch (System.Exception ex)
            {
                Puts($"Error saving configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Caches animal prefabs and initializes their default configurations.
        /// </summary>
        private void CacheAnimalPrefabs()
        {
            foreach (var prefab in GameManifest.Current.entities)
            {
                if (prefab.ToLower().Contains("assets/rust.ai/agents/"))
                {
                    var entity = GameManager.server.FindPrefab(prefab.ToLower())?.GetComponent<BaseCombatEntity>();
                    if (entity != null && !animalConfigs.Animals.ContainsKey(prefab.ToLower()))
                    {
                        animalConfigs.Animals[prefab.ToLower()] = new AnimalConfig();
                    }
                }
            }
            SaveConfigValues();
        }

        /// <summary>
        /// Scales incoming and outgoing damage for animals.
        /// </summary>
        /// <param name="entity">The entity receiving damage.</param>
        /// <param name="info">Damage information.</param>
        /// <returns>Null to allow default behavior.</returns>
        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info == null) return null;

            // Scale incoming damage for the target entity
            var entityPrefab = entity.PrefabName?.ToLower();
            if (entityPrefab != null && animalConfigs.Animals.TryGetValue(entityPrefab, out var targetConfig))
            {
                //Puts($"Scaling incoming damage for {entityPrefab}: {targetConfig.IncomingDamageScale}");
                info.damageTypes.ScaleAll(targetConfig.IncomingDamageScale);
            }

            // Scale outgoing damage for the attacker
            var attackerPrefab = info.Initiator?.PrefabName?.ToLower();
            if (attackerPrefab != null && animalConfigs.Animals.TryGetValue(attackerPrefab, out var attackerConfig))
            {
                //Puts($"Scaling outgoing damage for {attackerPrefab}: {attackerConfig.OutgoingDamageScale}");
                info.damageTypes.ScaleAll(attackerConfig.OutgoingDamageScale);
            }

            return null;
        }

        /// <summary>
        /// Lists all cached animal prefabs via a console command.
        /// </summary>
        /// <param name="arg">Console arguments.</param>
        [ConsoleCommand("animalmanager.listanimals")]
        private void ListAnimalsCommand(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin)
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            foreach (var animal in animalConfigs.Animals)
            {
                arg.ReplyWith($"Animal Prefab: {animal.Key}");
            }
        }
    }
}
