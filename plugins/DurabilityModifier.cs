using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("DurabilityModifier", "Rust Wipe Day", "1.0.0")]
    [Description("Modifies item durability loss and allows configuration of damage multipliers.")]
    public class DurabilityModifier : CovalencePlugin
    {
        private ConfigData _config;
        private bool _debug = false; // Enable debug mode

        private class ConfigData
        {
            public float DurabilityMultiplier { get; set; } = 2.0f;
        }

        protected override void LoadDefaultConfig()
        {
            Debug("Loading default config.");
            _config = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            Debug("Loading config.");
            base.LoadConfig();
            _config = Config.ReadObject<ConfigData>() ?? new ConfigData();
        }

        protected override void SaveConfig()
        {
            Debug("Saving config.");
            Config.WriteObject(_config);
        }

        private object OnLoseCondition(Item item, ref float amount)
        {
            Debug("OnLoseCondition called with item: {0}, amount: {1}", item?.info?.shortname, amount);

            if (item != null && amount > 0)
            {
                // Apply configurable durability damage multiplier
                amount *= _config.DurabilityMultiplier;
                Debug("Adjusted amount to {0}", amount);
            }

            return null; // Continue with the default behavior
        }

        private void Init()
        {
            Debug("Initializing plugin.");
            Debug("DurabilityModifier plugin initialized with a durability multiplier of {0}x.", _config.DurabilityMultiplier);
        }

        private void Unload()
        {
            Debug("Unloading plugin.");
            Debug("DurabilityModifier plugin unloaded.");
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
