using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Database;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using Rust;
using static BaseEntity.RPC_Server;

namespace Oxide.Plugins
{
    [Info("HonorSystem", "TTV OdsScott", "1.0.0")]
    [Description("Adds honor points on kill and titles in chat that update server-wide on a timer.")]
    public class HonorSystem : RustPlugin
    {
        [PluginReference]
        Plugin BetterChat;

        [PluginReference]
        Plugin Duelist;


        private readonly Dictionary<int, (string title, string color, int required, double percentPopulation)> ranks = new Dictionary<int, (string, string, int, double)>
        {
            {1, ("Rookie", "#B0C4DE", 0, 1)}, // Light Steel Blue, more common and less vibrant
            {2, ("Skirmisher", "#F4A460", 400, 1)}, // Sandy Brown, slightly more vibrant
            {3, ("Raider", "#6A5ACD", 800, 1)}, // Slate Blue, a step up in vibrancy
            {4, ("Veteran Raider", "#20B2AA", 1600, 0.9)}, // Light Sea Green, more unique
            {5, ("Squad Leader", "#3CB371", 2400, 0.8)}, // Medium Sea Green, cooler and more prestigious
            {6, ("Combat Specialist", "#4682B4", 3200, 0.7)}, // Steel Blue, denotes higher expertise
            {7, ("Elite Marksman", "#FFD700", 4000, 0.6)}, // Gold, signifies elite status
            {8, ("Raid Commander", "#40E0D0", 4800, 0.5)}, // Turquoise, cooler and more commanding
            {9, ("War Tactician", "#FF8C00", 5600, 0.4)}, // Dark Orange, vibrant and strategic
            {10, ("Siege Master", "#DA70D6", 6400, 0.3)}, // Orchid, unique and masterful
            {11, ("Wasteland General", "#7B68EE", 7200, 0.2)}, // Medium Slate Blue, commanding and general-like
            {12, ("Master of the Wastes", "#00FA9A", 8000, 0.1)}, // Medium Spring Green, rare and prestigious
            {13, ("Rust Warlord", "#FF4500", 8800, 0.05)}, // Orange Red, signifies power and warlord status
            {14, ("High Warlord of the Wastes", "#4B0082", 9600, 0.01)} // Indigo, the coolest and most prestigious, befitting the highest rank
        };

        private Dictionary<ulong, int> playerRanks = new Dictionary<ulong, int>();
        private Dictionary<ulong, int> playerHonorPoints = new Dictionary<ulong, int>();
        private Dictionary<ulong, int> playerHonorPointsDelta = new Dictionary<ulong, int>();
        private Dictionary<string, DateTime> lastKillTimes = new Dictionary<string, DateTime>();
        private Dictionary<NetworkableId, Dictionary<ulong, int>> HeliAttackers = new Dictionary<NetworkableId, Dictionary<ulong, int>>();

        private const string honorDataFile = "HonorData";


        void OnServerInitialized()
        {
            LoadData();
            StartHonorUpdateTimer();
            Subscribe(nameof(OnConsumedDogTag));
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void Unload()
        {
            SaveData();;
        }

        private void StartHonorUpdateTimer()
        {
            timer.Every(600, () =>
            {
                CalculateAndAssignRanks();
            });
        }

        bool InDuel(BasePlayer player)
        {
            if (player == null) return false;
            return Duelist?.Call<bool>("inEvent", player) ?? false;
        }

        object OnBetterChat(Dictionary<string, object> data)
        {          
            //Puts(data);  
            if (data.ContainsKey("Player") && data["Player"] is Oxide.Game.Rust.Libraries.Covalence.RustPlayer player)
            {
                BasePlayer basePlayer = (BasePlayer)player.Object;
                (string honorColor, string honorTitle) = GetHonorTitle(basePlayer.userID);
                if (!string.IsNullOrEmpty(honorTitle))
                {
                    ((Dictionary<string, object>)data["UsernameSettings"])["Color"] = honorColor;
                    ((List<string>)data["Titles"]).Add(honorTitle); // Set the title in the BetterChat message data
                }
            }
            return data;
        }

        void OnConsumedDogTag(BasePlayer player, Item activeItem)
        {
            ApplyHonorPoints(player, "Dog tag", false, null, 50);
        }


        [Command("sethonor")]
        private void SetHonorCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                return;
            }
       
            var playerId = Convert.ToUInt64(args[0]);
            var honor = Convert.ToInt32(args[1]);

            var playerToSet = BasePlayer.FindByID(playerId);

            if (!playerToSet) {
                return;
            }

            playerHonorPoints[playerId] = honor;
        }

        [Command("honorcalculate")]
        private void HonorCalculateCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                return;
            }

            CalculateAndAssignRanks();
        }

        private void LoadData()
        {
            try
            {
                playerHonorPoints = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, int>>(honorDataFile);
                if (playerHonorPoints == null)
                {
                    playerHonorPoints = new Dictionary<ulong, int>();
                    Puts("No existing data found, initializing new honor data.");
                }
                else
                {
                    Puts("Honor data loaded successfully.");
                }

                CalculateAndAssignRanks(); // Recalculate ranks after loading data
            }
            catch (Exception ex)
            {
                Puts($"Error loading honor data: {ex.Message}");
                playerHonorPoints = new Dictionary<ulong, int>();
            }
        }

        private void SaveData()
        {
            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(honorDataFile, playerHonorPoints);
                Puts("Honor data saved successfully.");
            }
            catch (Exception ex)
            {
                Puts($"Error saving honor data: {ex.Message}");
            }
        }


        private void CalculateAndAssignRanks()
        { 
            int maxServerSlots = ConVar.Server.maxplayers;
            var rankSlots = ranks.ToDictionary(rank => rank.Key, rank => rank.Value.percentPopulation < 1 ? (int)Math.Ceiling(maxServerSlots * rank.Value.percentPopulation) : int.MaxValue);

            var sortedPlayers = playerHonorPoints.OrderByDescending(p => p.Value).ToList();

            var highestHonorGain = playerHonorPointsDelta.OrderByDescending(p => p.Value).FirstOrDefault();

            if (highestHonorGain.Key != 0 && highestHonorGain.Value > 0)
            {
                if (!playerRanks.TryGetValue(highestHonorGain.Key, out var decayAboveRankValue))
                {
                    decayAboveRankValue = 0;
                }

                // Retrieve the highest gain
                int highestGain = highestHonorGain.Value;

                // Define the decay percentage (e.g., 2%)
                double decayRate = 0.05;

                // Calculate decay amount based on the highest gain
                int decayAmount = (int)(highestGain * decayRate);

                var keys = new List<ulong>(playerHonorPoints.Keys);
                foreach (ulong key in keys)
                {
                    if (key == highestHonorGain.Key)
                    {
                        continue;
                    }

                    if (!playerRanks.TryGetValue(key, out int playerRank))
                    {
                        playerRank = 0;
                    }

                    if (playerRank < decayAboveRankValue)
                    {
                        continue;
                    }

                    playerHonorPoints[key] = Math.Max(0, playerHonorPoints[key] - decayAmount);
                }
            }

            playerHonorPointsDelta.Clear();

            foreach (var player in sortedPlayers)
            {
                foreach (var rank in ranks.OrderByDescending(r => r.Key))
                {
                    if (player.Value >= rank.Value.required && rankSlots[rank.Key] > 0)
                    {
                        if (!playerRanks.ContainsKey(player.Key))
                        {
                            playerRanks.Add(player.Key, rank.Key);
                        }
                        else
                        {
                            playerRanks[player.Key] = rank.Key;
                        }
                        rankSlots[rank.Key]--; 
                        break;
                    }
                }
            }

            
        }

        [ChatCommand("honor")]
        private void CheckHonorCommand(BasePlayer player, string command, string[] args)
        {
            int rank = GetPlayerHonorRank(player.userID);
            int points = GetPlayerHonorPoints(player.userID);
            if (rank > 0)
            {
                var rankInfo = ranks[rank];
                SendReply(player, $"<color=#c45508>[RustWipeDay]</color>: Your current honor rank is: {ApplyColor(rankInfo.title, rankInfo.color)}. You have {ApplyColor(points.ToString(), rankInfo.color)} honor points.");
            }
            else
            {
                SendReply(player, $"<color=#c45508>[RustWipeDay]</color>: You currently have no honor rank. You have {points.ToString()} honor points.");
            }
        }

        object OnCollectiblePickup(CollectibleEntity collectible, BasePlayer player)
        {
            var displayName = collectible.ShortPrefabName.Replace(".", " ").Replace("-", " ");
            displayName = Char.ToUpper(displayName[0]) + displayName.Substring(1);
            displayName = displayName.TrimEnd();
            while (displayName.Length > 0 && char.IsDigit(displayName[displayName.Length - 1]))
            {
                displayName = displayName.Remove(displayName.Length - 1).TrimEnd();
            }
            ProcessGather(player, displayName, 1);
            return null;
        }

        void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            var player = entity?.ToPlayer();
            if (player == null) return;

            NextTick(() =>
            {
                if (player == null || !player.IsConnected) 
                {
                    Puts("Dispenser or player is no longer valid.");
                    return;
                }

                if (dispenser == null || dispenser.fractionRemaining <= 0)
                {
                    var displayName = item.info.shortname.Replace(".", " ");
                    displayName = Char.ToUpper(displayName[0]) + displayName.Substring(1);
                    displayName = displayName.TrimEnd();
                    while (displayName.Length > 0 && char.IsDigit(displayName[displayName.Length - 1]))
                    {
                        displayName = displayName.Remove(displayName.Length - 1).TrimEnd();
                    }
                    ProcessGather(player, displayName);
                }
                else
                {
                    // Puts($"Dispenser not yet finished. {dispenser.fractionRemaining}");
                }
            });
        }

        private void OnPlayerDeath(BasePlayer victim, HitInfo info)
        {
            BasePlayer attacker = null;
            if (info != null && info.InitiatorPlayer != null)
            {
                attacker = info.InitiatorPlayer;
            }

            if (InDuel(attacker) || InDuel(victim.ToPlayer()))
            {
                return;
            }

            if (GivesHonor(attacker, victim))
            {
                ProcessKill(attacker, victim);
            }
        }

        void OnEntityDeath(BaseCombatEntity victim, HitInfo info)
        {
            if (victim.ToPlayer() != null)
            {
                return;
            }

            BasePlayer attacker = null;
            if (info != null && info.InitiatorPlayer != null) {
                attacker = info.InitiatorPlayer;
            }
            else if (victim.GetComponent<PatrolHelicopter>() != null) {
                attacker = BasePlayer.FindByID(GetLastAttacker(victim.net.ID));
            }

            if (InDuel(attacker) || InDuel(victim.ToPlayer()))
            {
                return;
            }

            if (GivesHonor(attacker, victim))
            {
                ProcessKill(attacker, victim);
            }
        }

        private void OnLootEntityEnd(BasePlayer player, LootContainer entity)
        {
            // Check if the entity is a crate (adjust this check as necessary for your specific crate types)
            if (entity.inventory.IsEmpty())
            {
                // Your custom logic here
                //Puts($"{player.displayName} has finished looting a crate: {entity.ShortPrefabName}");
                var displayName = entity.ShortPrefabName.Replace(".", " ").Replace("_"," ").Replace("-", " "); ;
                displayName = Char.ToUpper(displayName[0]) + displayName.Substring(1);
                // Remove trailing digits and whitespace
                displayName = displayName.TrimEnd();
                while (displayName.Length > 0 && char.IsDigit(displayName[displayName.Length - 1]))
                {
                    displayName = displayName.Remove(displayName.Length - 1);
                }
                displayName = displayName.TrimEnd();
                ProcessGather(player, displayName);
            }
        }


        private ulong GetLastAttacker(NetworkableId id)
        {
            int hits = 0;
            ulong majorityPlayer = 0U;
            if (HeliAttackers.ContainsKey(id))
            {
                foreach (var score in HeliAttackers[id])
                {
                    if (score.Value > hits)
                        majorityPlayer = score.Key;
                }
            }

            return majorityPlayer;
        }

        void OnEntityTakeDamage(BaseCombatEntity victim, HitInfo info)
        {
            if (victim.GetComponent<PatrolHelicopter>() != null && info?.Initiator?.ToPlayer() != null)
            {
                var heli = victim.GetComponent<PatrolHelicopter>();
                var player = info.Initiator.ToPlayer();

                NextTick(() =>
                {
                    if (heli == null) return;
                    if (!HeliAttackers.ContainsKey(heli.net.ID))
                        HeliAttackers.Add(heli.net.ID, new Dictionary<ulong, int>());
                    if (!HeliAttackers[heli.net.ID].ContainsKey(player.userID))
                        HeliAttackers[heli.net.ID].Add(player.userID, 0);
                    HeliAttackers[heli.net.ID][player.userID]++;
                });
            }
        }

        bool GivesHonor(BasePlayer attacker, BaseCombatEntity entity) {
            var victim = entity as BasePlayer;
            if (attacker == null || attacker == victim) {
                return false;
            }
            var isPlayer = victim is BasePlayer && !entity.IsNpc;
            if (isPlayer || IsBarrel(entity) || IsRoadsign(entity)) {
                return true;
            }
            switch(GetDisplayName(entity)) {
                case "Bear":
                case "Stag":
                case "Polarbear":
                case "Wolf":
                case "Boar":
                case "Scientist":
                case "Nvg Scientist":
                case "Heavy Scientist":
                case "Bradley APC":
                case "Tunnel Dweller":
                case "Patrol Helicopter":
                    return true;
                default:
                    return false;
            }
            return false;
        }

        bool IsBarrel(BaseCombatEntity entity) {
            return entity.ShortPrefabName.StartsWith("loot-barrel") || 
                   entity.ShortPrefabName.StartsWith("loot_barrel") ||
                   entity.ShortPrefabName == "oil_barrel" ||
                   entity.ShortPrefabName == "diesel_barrel_world";
        }

        bool IsRoadsign(BaseCombatEntity entity) {
            return entity.ShortPrefabName.StartsWith("roadsign");
        }


        void ProcessGather(BasePlayer attacker, string type, int honorPoints = 3)
        {
            float proximityRadius = 50f; // Meters
            List<BasePlayer> partyMembers = GetNearbyAllies(attacker, proximityRadius);

            foreach (var member in partyMembers)
            {
                ApplyHonorPoints(member, type, false, null, honorPoints);
            }
        }

        void ProcessKill(BasePlayer attacker, BaseCombatEntity victim)
        {
            float proximityRadius = 50f; // Meters
            List<BasePlayer> partyMembers = GetNearbyAllies(attacker, proximityRadius);
            
            int victimRank = GetEntityHonorRank(victim);
            var rankInfo = GetRankInfo(Math.Max(0, victimRank));
            string victimRankTitle = null;
            if (rankInfo.title != null) {
                victimRankTitle = ApplyColor(rankInfo.title, rankInfo.color);
            }

            int honorPoints = GetHonorPointsReward(victim, victimRank);

            if (honorPoints <= 0) {
                return;
            }

            foreach (var member in partyMembers)
            {
                ApplyHonorPoints(member, GetDisplayName(victim), !IsBarrel(victim) && !IsRoadsign(victim), victimRankTitle, honorPoints);
            }
        }

        int GetHonorPointsReward(BaseCombatEntity victim, int victimRank) {
            int baseHonor = 10;
                        
            if (IsBarrel(victim) || IsRoadsign(victim)) {
                return 2;
            }
            else if(victim is Bear || victim is Polarbear) {
                return 5;
            }
            else if(victim is Stag || victim is Boar || victim is Wolf) {
                return 3;
            }
            else {
                return baseHonor * Math.Max(1, victimRank);
            }
        }

        List<BasePlayer> GetNearbyAllies(BasePlayer player, float radius)
        {
            List<BasePlayer> nearbyAllies = new List<BasePlayer>() { player };

            if (player == null || player.Team == null)
            {
                return nearbyAllies;
            }

            var team = player.Team;

            foreach (var memberId in team.members)
            {
                BasePlayer teamMember = BasePlayer.FindByID(memberId);
                if (teamMember != null && player.userID != teamMember.userID && IsPlayerInRange(player, teamMember, 20))
                {
                    nearbyAllies.Add(teamMember);
                }
            }
            
            return nearbyAllies;
        }

        private bool IsPlayerInRange(BasePlayer player, BasePlayer targetPlayer, float range)
        {
            if (player == null || targetPlayer == null)
                return false;

            float distance = Vector3.Distance(
                player.transform.position,
                targetPlayer.transform.position
            );
            return distance <= range;
        }

        // Define a hook that other plugins can use to modify the honor points.
        object Hook_OnHonorPointsAwarded(BasePlayer player, string victimName, bool isNpc, string victimRank, int points)
        {
            // Call out to any plugin interested in modifying the points.
            // If no plugin modifies the points, this will return null.
            var modifiedPoints = Interface.CallHook("OnHonorPointsAwarded", player, victimName, isNpc, victimRank, points);

            if (modifiedPoints is int)
            {
                return (int)modifiedPoints;
            }

            // Return the original points if no modifications were made.
            return points;
        }


        void ApplyHonorPoints(BasePlayer player, string victimName, bool isNpc, string victimRank, int points)
        {
            // Use the hook to allow modification of the points.
            int modifiedPoints = (int)Hook_OnHonorPointsAwarded(player, victimName, isNpc, victimRank, points);

            var diesText = isNpc ? "dies" : "gathered";
            var victimRankText = victimRank != null ? ", honorable kill rank: " + victimRank + "." : ".";
            player.ChatMessage($"<color=#c45508>[RustWipeDay]</color>: <color=#FFFF00>{victimName} {diesText}{victimRankText} (Honor Points: {modifiedPoints})</color>");
            if (playerHonorPoints.ContainsKey(player.userID))
            {
                playerHonorPoints[player.userID] += modifiedPoints;
            }
            else
            {
                playerHonorPoints.Add(player.userID, modifiedPoints);
            }

            if (playerHonorPointsDelta.ContainsKey(player.userID))
            {
                playerHonorPointsDelta[player.userID] += modifiedPoints;
            }
            else
            {
                playerHonorPointsDelta.Add(player.userID, modifiedPoints);
            }
        }

        private int GetPlayerHonorPoints(ulong playerId)
        {
           return playerHonorPoints.TryGetValue(playerId, out int points) ? points : 0;
        }

        string GetDisplayName(BaseCombatEntity entity)
        {
            if (entity is BradleyAPC) {
                return "Bradley APC";
            }
            else if (entity is PatrolHelicopter) {
                return "Patrol Helicopter";
            }
            else if(IsRoadsign(entity)) {
                return "Roadsign";
            }
            else if(IsBarrel(entity)) {
                return "Barrel";
            }
            else if (entity.IsNpc)
            {
                var prefabName = entity.ShortPrefabName;
                if (prefabName.Contains("scientist")) {
                    if (prefabName.Contains("nvg")) {
                        return "Nvg Scientist";
                    }
                    if (prefabName.Contains("heavy")) {
                        return "Heavy Scientist";
                    }
                    return "Scientist";
                }
                else if(prefabName.Contains("tunneldweller")) {
                    return "Tunnel Dweller";
                }
            }
            else if (entity is BasePlayer) {
                return (entity as BasePlayer).displayName;
            }
            var displayName = entity.ShortPrefabName.Replace(".prefab", "").Replace("_", " ");
            displayName = Char.ToUpper(displayName[0]) + displayName.Substring(1);
            return displayName;
        }

        (string honorColor, string honorTitle) GetHonorTitle(ulong playerId)
        {
            int playerRank = GetPlayerHonorRank(playerId);
            if (playerRank == 0) {             
                return (string.Empty, string.Empty);
            }

            var rankInfo = GetRankInfo(playerRank);
            return (rankInfo.color, ApplyColor($"{rankInfo.title}", rankInfo.color));
        }

        (string title, string color, int required, double percentPopulation) GetRankInfo(int playerRank) {
            ranks.TryGetValue(playerRank, out var rankInfo);
            return rankInfo;
        }

        private int GetEntityHonorRank(BaseCombatEntity entity) {
            if (entity is BasePlayer && !entity.IsNpc) {
                var player = entity as BasePlayer;
                return GetPlayerHonorRank(player.userID);
            }
            switch (GetDisplayName(entity)) {
                case "Scientist":
                case "Tunnel Dweller":
                    return 2;
                case "Nvg Scientist":
                    return 5;
                case "Heavy Scientist":
                    return 7;
                case "Bradley APC":
                case "Patrol Helicopter":
                    return 10;
            }
            return 0;
        }

        private int GetPlayerHonorRank(ulong playerId)
        {
            if (playerRanks.TryGetValue(playerId, out int rank))
            {
                return rank;
            }
            return 0;
        }

        string ApplyColor(string message, string hexColor)
        {
            return $"<color={hexColor}>{message}</color>";
        }

        private string FormatPlayerName(ulong playerId, string userName, bool includeTitle = true) {
            int playerRank = GetPlayerHonorRank(playerId);
            if (playerRank == 0) {             
                return ApplyColor(userName, "#5af");
            }

            var rankInfo = GetRankInfo(playerRank);
            var name = (includeTitle ? $"{rankInfo.title} " : "") + userName;
            return ApplyColor(name, rankInfo.color);
        }
    }
}
