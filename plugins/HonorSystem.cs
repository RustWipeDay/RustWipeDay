// HonorSystem Plugin Implementation
using Oxide.Core;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using Rust;
using static Carbon.Modules.AdminModule.Tab;

namespace Oxide.Plugins
{
    [Info("HonorSystem", "TTV OdsScott", "1.3.1")]
    [Description("Adds honor points on kill and titles in chat that update server-wide on a timer.")]
    public class HonorSystem : RustPlugin
    {
        [PluginReference]
        Plugin BetterChat;

        [PluginReference]
        Plugin Duelist;

        private Dictionary<int, (string Title, string Color, int Required, double PercentPopulation)> Ranks;
        private Dictionary<string, int> EntityHonorPoints;
        private int DispenserBonus;
        private int CollectibleBonus;
        private Dictionary<ulong, int> PlayerHonorPoints = new Dictionary<ulong, int>();
        private Dictionary<ulong, int> PlayerRanks = new Dictionary<ulong, int>();
        private Dictionary<ulong, int> PlayerHonorPointsDelta = new Dictionary<ulong, int>();
        private Dictionary<string, DateTime> LastKillTimes = new Dictionary<string, DateTime>();
        private Dictionary<NetworkableId, Dictionary<ulong, int>> HeliAttackers = new Dictionary<NetworkableId, Dictionary<ulong, int>>();

        private const string HonorDataFile = "HonorSystemData";
        private const int HonorDecayInterval = 600;
        private const int DecayAmount = 10;
        private const double DecayMultiplier = 1.0; // Configurable decay multiplier

        private HashSet<ulong> RecentlyNotifiedPlayers = new HashSet<ulong>(); // Tracks players recently notified

        protected override void LoadDefaultConfig()
        {
            Config["Ranks"] = new Dictionary<int, object>
            {
                {1, new { Title = "Rookie", Color = "#B0C4DE", Required = 0, PercentPopulation = 1.0 }},
                {2, new { Title = "Skirmisher", Color = "#F4A460", Required = 400, PercentPopulation = 1.0 }},
                {3, new { Title = "Raider", Color = "#6A5ACD", Required = 800, PercentPopulation = 1.0 }},
                {4, new { Title = "Veteran Raider", Color = "#20B2AA", Required = 1600, PercentPopulation = 0.9 }},
                {5, new { Title = "Squad Leader", Color = "#3CB371", Required = 2400, PercentPopulation = 0.8 }},
                {6, new { Title = "Combat Specialist", Color = "#4682B4", Required = 3200, PercentPopulation = 0.7 }},
                {7, new { Title = "Elite Marksman", Color = "#FFD700", Required = 4000, PercentPopulation = 0.6 }},
                {8, new { Title = "Raid Commander", Color = "#40E0D0", Required = 4800, PercentPopulation = 0.5 }},
                {9, new { Title = "War Tactician", Color = "#FF8C00", Required = 5600, PercentPopulation = 0.4 }},
                {10, new { Title = "Siege Master", Color = "#DA70D6", Required = 6400, PercentPopulation = 0.3 }},
                {11, new { Title = "Wasteland General", Color = "#7B68EE", Required = 7200, PercentPopulation = 0.2 }},
                {12, new { Title = "Master of the Wastes", Color = "#00FA9A", Required = 8000, PercentPopulation = 0.1 }},
                {13, new { Title = "Rust Warlord", Color = "#FF4500", Required = 8800, PercentPopulation = 0.05 }},
                {14, new { Title = "High Warlord of the Wastes", Color = "#4B0082", Required = 9600, PercentPopulation = 0.01 }}
            };

            Config["EntityHonorPoints"] = new Dictionary<string, int>
            {
                { "scientist", 10 },
                { "elite_scientist", 30 },
                { "heavy_scientist", 20 },
                { "patrol_helicopter", 50 },
                { "bear", 20 },
                { "stag", 10 },
                { "polarbear", 25 },
                { "wolf", 15 },
                { "boar", 10 },
                { "bradleyapc", 100 },
                { "tunneldweller", 15 },
                { "loot_barrel", 5 },
                { "roadsign", 5 }
            };

            Config["DispenserBonus"] = 5;
            Config["CollectibleBonus"] = 3;
            Config["DecayMultiplier"] = DecayMultiplier;

            SaveConfig();
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["HonorMessage"] = "<color=#c45508>[RustWipeDay]</color>: <color=#FFFF00>{0} {1} (Honor Points: {2})</color>",
                ["DogTagMessage"] = "<color=#00FF00>You received a Dog Tag!</color>",
                ["DispenserText"] = "You harvested {0}",
                ["CollectibleText"] = "You gathered {0}"
            }, this);
        }

        void OnServerInitialized()
        {
            LoadData();
            Ranks = Config["Ranks"] as Dictionary<int, (string Title, string Color, int Required, double PercentPopulation)>;
            EntityHonorPoints = Config["EntityHonorPoints"] as Dictionary<string, int>;
            DispenserBonus = Convert.ToInt32(Config["DispenserBonus"]);
            CollectibleBonus = Convert.ToInt32(Config["CollectibleBonus"]);
            StartHonorDecayTimer();
        }

        private void LoadData()
        {
            PlayerHonorPoints = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, int>>(HonorDataFile) ?? new();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(HonorDataFile, PlayerHonorPoints);
        }


        bool GivesHonor(BasePlayer attacker, BaseCombatEntity entity)
        {
            if (attacker == null || entity == null)
                return false;

            string entityDisplayName = entity.ShortPrefabName.ToLowerInvariant();
            return EntityHonorPoints.ContainsKey(entityDisplayName);
        }

        bool IsBarrel(BaseCombatEntity entity)
        {
            if (entity == null || string.IsNullOrEmpty(entity.ShortPrefabName))
                return false;

            string prefabName = entity.ShortPrefabName.ToLowerInvariant();
            var barrelPrefabs = new List<string> { "loot-barrel", "loot_barrel", "oil_barrel", "diesel_barrel" };

            return barrelPrefabs.Any(prefab => prefabName.Contains(prefab));
        }

        private void StartHonorDecayTimer()
        {
            timer.Every(HonorDecayInterval, () =>
            {
                foreach (var playerId in PlayerHonorPoints.Keys.ToList())
                {
                    // Calculate decay amount based on the multiplier
                    int decayAmount = (int)(DecayAmount * Convert.ToDouble(Config["DecayMultiplier"]));
                    PlayerHonorPoints[playerId] = Math.Max(0, PlayerHonorPoints[playerId] - decayAmount);

                    // Update rank if necessary
                    RecalculatePlayerRank(playerId);
                }

                // Save data after decay has been applied
                SaveData();

                // Notify active players about rank changes
                BroadcastRankChanges();
            });
        }


         List<BasePlayer> GetNearbyAllies(BasePlayer player, float radius)
         {
            List<BasePlayer> nearbyAllies = new List<BasePlayer>() { player };
            if (player == null || player.Team == null) return nearbyAllies;
            
            var team = player.Team;
            foreach (var memberId in team.members)
            {
                BasePlayer teamMember = BasePlayer.FindByID(memberId);
                if (teamMember != null && player.userID != teamMember.userID && IsPlayerInRange(player, teamMember, radius))
                {
                    nearbyAllies.Add(teamMember);
                }
            }
            return nearbyAllies;
        }


        // Define a hook to modify honor points dynamically
        object Hook_OnHonorPointsAwarded(BasePlayer player, string victimName, bool isNpc, string victimRank, int points)
        {
            var modifiedPoints = Interface.CallHook("OnHonorPointsAwarded", player, victimName, isNpc, victimRank, points);
            return modifiedPoints is int? (int) modifiedPoints : points;
        }

        // Apply Honor Points with modification hooks
        void ApplyHonorPoints(BasePlayer player, string victimName, bool isNpc, string victimRank, int points)
        {
            int modifiedPoints = (int)Hook_OnHonorPointsAwarded(player, victimName, isNpc, victimRank, points);
            if (PlayerHonorPoints.ContainsKey(player.userID))
            {
                PlayerHonorPoints[player.userID] += modifiedPoints;
            }
            else
            {
                PlayerHonorPoints.Add(player.userID, modifiedPoints);
            }
            if (PlayerHonorPointsDelta.ContainsKey(player.userID))
            {
                PlayerHonorPointsDelta[player.userID] += modifiedPoints;
            }
            else
            {
                PlayerHonorPointsDelta.Add(player.userID, modifiedPoints);
            }
            player.ChatMessage($"<color=#FFD700>Honor Updated: {victimName} - {modifiedPoints} points</color>");
        }
}
