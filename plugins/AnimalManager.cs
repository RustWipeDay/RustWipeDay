using Oxide.Core;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Animal Manager", "Odell Davis Scott, ChatGPT", "1.7.4")]
    [Description("Lists animal prefabs and adjusts or scales their starting max HP and attack damage.")]
    public class AnimalManager : RustPlugin
    {
        private ConfigData animalConfigs;

        class ConfigData
        {
            public Dictionary<string, AnimalConfig> Animals { get; set; } = new Dictionary<string, AnimalConfig>();
        }

        class AnimalConfig
        {
            public float HealthScale { get; set; } = 1.0f;
            public float DamageScale { get; set; } = 1.0f;
        }

        private const string adminPermission = "animalmanager.admin";

        void Init()
        {
            Puts("Initializing Animal Manager Plugin...");
            permission.RegisterPermission(adminPermission, this);
            LoadConfigValues();
            CacheAnimalPrefabs();
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating a new configuration file with default values...");
            animalConfigs = new ConfigData();
            CacheAnimalPrefabs();
            SaveConfigValues();
        }

        private void LoadConfigValues()
        {
            Puts("Loading configuration...");
            try
            {
                animalConfigs = Config.ReadObject<ConfigData>();
                if (animalConfigs == null)
                {
                    Puts("No existing configuration found, creating new configuration.");
                    LoadDefaultConfig();
                }
                Puts("Configuration loaded successfully.");
            }
            catch (System.Exception ex)
            {
                Puts($"Error loading configuration: {ex.Message}");
                LoadDefaultConfig();
            }
        }

        private void SaveConfigValues()
        {
            Puts("Saving configuration...");
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

        private void CacheAnimalPrefabs()
        {
            Puts("Caching animal prefabs...");
            foreach (var prefab in GameManifest.Current.entities)
            {
                if (prefab.ToLower().Contains("assets/rust.ai/agents/"))
                {
                    Puts($"Found potential animal prefab: {prefab}");
                    var entity = GameManager.server.FindPrefab(prefab.ToLower())?.GetComponent<BaseCombatEntity>();
                    if (entity != null && !animalConfigs.Animals.ContainsKey(prefab.ToLower()))
                    {
                        animalConfigs.Animals[prefab.ToLower()] = new AnimalConfig();
                        Puts($"Cached animal prefab: {prefab.ToLower()}");
                    }
                }
            }
            SaveConfigValues();
            Puts("Animal prefabs cached successfully.");
        }

        void OnEntitySpawned(BaseNetworkable entity)
        {
            var baseEntity = entity as BaseEntity;
            if (baseEntity == null) return;

            var prefabName = baseEntity.PrefabName.ToLower();
            if (animalConfigs.Animals.TryGetValue(prefabName, out var animalConfig))
            {
                var combatEntity = baseEntity as BaseCombatEntity;
                if (combatEntity != null)
                {
                    Puts($"Applying health scale to {prefabName}: {animalConfig.HealthScale}");
                    combatEntity._maxHealth *= animalConfig.HealthScale;
                    combatEntity.health = combatEntity._maxHealth;
                }
            }
        }

        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            // Check if the HitInfo is valid and has an attacker
            if (info == null || info.Initiator == null) return null;

            // Get the attacker entity
            var attacker = info.Initiator as BaseEntity;
            if (attacker == null) return null;

            // Get the prefab name of the attacker to determine if it matches an animal
            var prefabName = attacker.PrefabName?.ToLower() ?? "";

            // Check if the attacker is an animal and apply outgoing damage scaling
            if (animalConfigs.Animals.TryGetValue(prefabName, out var animalConfig))
            {
                Puts($"Applying outgoing damage scale to {prefabName}: {animalConfig.DamageScale}");
                info.damageTypes.ScaleAll(animalConfig.DamageScale);
            }

            return null;
        }


        [ConsoleCommand("animalmanager.listanimals")]
        private void ListAnimalsCommand(ConsoleSystem.Arg arg)
        {
            Puts("Executing console command 'animalmanager.listanimals'...");
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

        private bool HasAdminPermission(BasePlayer player)
        {
            return permission.UserHasPermission(player.UserIDString, adminPermission);
        }
    }
}
