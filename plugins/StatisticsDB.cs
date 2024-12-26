using System;
using System.Collections.Generic;
using System.Linq;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Rust;
using UnityEngine;
using Time = Oxide.Core.Libraries.Time;

namespace Oxide.Plugins
{
    [Info("Statistics DB", "Iv Misticos", "1.2.6")]
    [Description("Statistics database for developers")]
    class StatisticsDB : CovalencePlugin
    {
        #region Variables

        [PluginReference]
        private Plugin ConnectionDB = null,
            PlayerDatabase = null;

        private static PluginData _data = new PluginData();

        private static StatisticsDB ins;

        private static readonly Time Time = GetLibrary<Time>();

        #endregion

        #region Configuration

        private static Configuration _config = new Configuration();

        private class Configuration
        {
            [JsonProperty(PropertyName = "Debug")]
            public bool Debug = false;

            [JsonProperty(PropertyName = "Inactive Entry Lifetime")]
            public uint Lifetime = 259200;

            [JsonProperty(PropertyName = "Collect Joins")]
            public bool CollectJoins = true;

            [JsonProperty(PropertyName = "Collect Leaves")]
            public bool CollectLeaves = true;

            [JsonProperty(PropertyName = "Collect Kills")]
            public bool CollectKills = true;

            [JsonProperty(PropertyName = "Collect Deaths")]
            public bool CollectDeaths = true;

            [JsonProperty(PropertyName = "Collect Suicides")]
            public bool CollectSuicides = true;

            [JsonProperty(PropertyName = "Collect Shots")]
            public bool CollectShots = true;

            [JsonProperty(PropertyName = "Collect Headshots")]
            public bool CollectHeadshots = true;

            [JsonProperty(PropertyName = "Collect Experiments")]
            public bool CollectExperiments = true;

            [JsonProperty(PropertyName = "Collect Recoveries")]
            public bool CollectRecoveries = true;

            [JsonProperty(PropertyName = "Collect Voice Bytes")]
            public bool CollectVoiceBytes = true;

            [JsonProperty(PropertyName = "Collect Wounded Times")]
            public bool CollectWoundedTimes = true;

            [JsonProperty(PropertyName = "Collect Crafted Items")]
            public bool CollectCraftedItems = true;

            [JsonProperty(PropertyName = "Collect Repaired Items")]
            public bool CollectRepairedItems = true;

            [JsonProperty(PropertyName = "Collect Lift Usages")]
            public bool CollectLiftUsages = true;

            [JsonProperty(PropertyName = "Collect Wheel Spins")]
            public bool CollectWheelSpins = true;

            [JsonProperty(PropertyName = "Collect Hammer Hits")]
            public bool CollectHammerHits = true;

            [JsonProperty(PropertyName = "Collect Explosives Thrown")]
            public bool CollectExplosivesThrown = true;

            [JsonProperty(PropertyName = "Collect Weapon Reloads")]
            public bool CollectWeaponReloads = true;

            [JsonProperty(PropertyName = "Collect Rockets Launched")]
            public bool CollectRocketsLaunched = true;

            [JsonProperty(PropertyName = "Collect Collectible Pickups")]
            public bool CollectCollectiblePickups = true;

            [JsonProperty(PropertyName = "Collect Plant Pickups")]
            public bool CollectPlantPickups = true;

            [JsonProperty(PropertyName = "Collect Gathered")]
            public bool CollectGathered = true;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                    throw new Exception();
            }
            catch
            {
                PrintError(
                    "Your configuration file contains an error. Using default configuration values."
                );
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion

        #region Work with Data

        private void SaveData()
        {
            foreach (var kvp in _data.Statistics)
            {
                SaveData(kvp.Key);
            }
        }

        private void SaveData(ulong id)
        {
            PlayerDatabase.Call("SetPlayerData", id.ToString(), Name, _data.Statistics[id], true);
        }

        private PlayerStats? LoadData(ulong id)
        {
            var success = PlayerDatabase.Call("GetPlayerDataRaw", id.ToString(), Name);
            if (success is string)
            {
                return JsonConvert.DeserializeObject<PlayerStats>((string)success);
            }
            return null;
        }

        private class PluginData
        {
            public Dictionary<ulong, PlayerStats> Statistics = new Dictionary<ulong, PlayerStats>();
        }


        [Command("elo")]
        private void EloCommand(BasePlayer player, string command, string[] args)
        {

            var stats = PlayerStats.Find(player.userID);
            if (stats == null)
            {
                return;
            }

            // Get the rank of the player based on their ELO
            var rank = _data.Statistics.OrderByDescending(stat => stat.Value.Elo)
                                       .Select((stat, index) => new { PlayerID = stat.Key, Rank = index + 1 })
                                       .FirstOrDefault(stat => stat.PlayerID == player.userID)?.Rank ?? -1;

            // Get the total number of players
            var totalPlayers = _data.Statistics.Count;


            var message = $"<color=#c45508>[RustWipeDay]</color>: Your elo is currently at <color={EloSystem.GetColorBySkillRank(stats.Elo)}>{stats.Elo}</color>. Rank {rank} out of {totalPlayers}.";
            player.ChatMessage(message);
        }


        public class EloSystem
        {
            public static uint DefaultRating = 1500;

            // Define the range of K-Factor values.
            private const uint MinKFactor = 4;
            private const uint MaxKFactor = 32;

            // Define the rating difference range.
            private const int MinRatingDifference = 0;  // Minimum rating difference for MaxKFactor.
            private const int MaxRatingDifference = 400; // Maximum rating difference for MinKFactor.

            public static void UpdateRatings(ref uint ratingA, ref uint ratingB, bool isAWinner)
            {
                double expectedScoreA = CalculateExpectedScore(ratingA, ratingB);
                double expectedScoreB = CalculateExpectedScore(ratingB, ratingA);

                double actualScoreA = isAWinner ? 1u : 0u;
                double actualScoreB = isAWinner ? 0u : 1u;

                uint newRatingA = CalculateNewRating(ratingA, ratingB, actualScoreA, expectedScoreA);
                uint newRatingB = CalculateNewRating(ratingB, ratingA, actualScoreB, expectedScoreB);

                ratingA = newRatingA;
                ratingB = newRatingB;
            }

            private static double CalculateExpectedScore(uint ratingA, uint ratingB)
            {
                return 1 / (1 + Math.Pow(10, ((double)ratingB - (double)ratingA) / 400.0));
            }

            private static uint CalculateNewRating(uint ratingA, uint ratingB, double actualScore, double expectedScore)
            {
                double kFactor = 15; // CalculateDynamicKFactor(ratingA, ratingB);
                return (uint)(ratingA + (kFactor * (actualScore - expectedScore)));
            }

            private static uint CalculateDynamicKFactor(uint ratingA, uint ratingB)
            {
                int ratingDifference = Math.Abs((int)ratingA - (int)ratingB);

                // Linear interpolation to calculate the K-Factor based on the rating difference.
                if (ratingDifference >= MaxRatingDifference)
                    return MinKFactor;

                double kFactor = MaxKFactor - ((double)(ratingDifference - MinRatingDifference) / (MaxRatingDifference - MinRatingDifference)) * (MaxKFactor - MinKFactor);

                // Ensure K-Factor is within the defined range.
                kFactor = Math.Max(MinKFactor, Math.Min(kFactor, MaxKFactor));

                return (uint)kFactor;
            }

            public static string GetColorBySkillRank(uint skillRank)
            {
                // Clamp the skill rank to be within 0 to 3000.
                skillRank = Math.Max(0, Math.Min(skillRank, 3000));

                // Define RGB values for start and end colors.
                // Start with red (low skill level).
                int startR = 255, startG = 0, startB = 0;
                // End with green (high skill level).
                int endR = 0, endG = 255, endB = 0;

                // Calculate the ratio of the current skill rank to the maximum.
                double ratio = skillRank / 3000.0;

                // Linearly interpolate the RGB values based on the skill rank.
                int r = (int)(startR + (endR - startR) * ratio);
                int g = (int)(startG + (endG - startG) * ratio);
                int b = (int)(startB + (endB - startB) * ratio);

                // Convert RGB to Hex color code.
                return $"#{r:X2}{g:X2}{b:X2}";
            }
        }

        private class PlayerStats
        {
            public long LastUpdate;

            public uint Joins;

            public uint Leaves;

            public uint Elo;

            public uint Kills;

            public uint Deaths;

            public uint Suicides;

            public uint Shots;

            public uint Headshots;

            public uint Experiments;

            public uint Recoveries;

            public uint VoiceBytes;

            public uint WoundedTimes;

            public uint CraftedItems;

            public uint RepairedItems;

            public uint LiftUsages;

            public uint WheelSpins;

            public uint HammerHits;

            public uint ExplosivesThrown;

            public uint WeaponReloads;

            public uint RocketsLaunched;

            public long SecondsPlayed;

            public List<string> Names;

            public List<string> IPs;

            public List<long> TimeStamps;

            public Dictionary<string, uint> CollectiblePickups = new Dictionary<string, uint>();

            public Dictionary<string, uint> PlantPickups = new Dictionary<string, uint>();

            public Dictionary<string, uint> Gathered = new Dictionary<string, uint>();

            public PlayerStats()
            {
                PrintDebug("Called PlayerStats Constructor");
            }

            internal PlayerStats(ulong id)
            {
                PrintDebug("Called PlayerStats Constructor 2");
                if (!id.IsSteamId())
                    return;

                Update();
                _data.Statistics.Add(id, this);
            }

            public void Update() => LastUpdate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            public static PlayerStats Find(ulong id)
            {
                if (_data.Statistics.ContainsKey(id))
                {
                    return _data.Statistics[id];
                }
                var stats = ins.LoadData(id);

                if (stats != null)
                {
                    _data.Statistics.Add(id, stats);
                    return _data.Statistics[id];
                }

                return null;
            }
        }

        #endregion

        #region Hooks

        private void Init()
        {
            ins = this;

            if (!_config.CollectJoins)
                Unsubscribe(nameof(OnPlayerConnected));

            if (!_config.CollectLeaves)
                Unsubscribe(nameof(OnPlayerDisconnected));

            if (!_config.CollectExperiments)
                Unsubscribe(nameof(CanExperiment));

            if (!_config.CollectHeadshots)
                Unsubscribe(nameof(OnEntityTakeDamage));

            if (!_config.CollectWoundedTimes)
                Unsubscribe(nameof(OnPlayerWound));

            if (!_config.CollectRecoveries)
                Unsubscribe(nameof(OnPlayerRecover));

            if (!_config.CollectVoiceBytes)
                Unsubscribe(nameof(OnPlayerVoice));

            //if (!_config.CollectCraftedItems)
            //    Unsubscribe(nameof(OnItemCraftFinished));

            if (!_config.CollectRepairedItems)
                Unsubscribe(nameof(OnItemRepair));

            if (!_config.CollectLiftUsages)
                Unsubscribe(nameof(OnLiftUse));

            if (!_config.CollectWheelSpins)
                Unsubscribe(nameof(OnSpinWheel));

            if (!_config.CollectHammerHits)
                Unsubscribe(nameof(OnHammerHit));

            if (!_config.CollectExplosivesThrown)
                Unsubscribe(nameof(OnExplosiveThrown));

            if (!_config.CollectWeaponReloads)
                Unsubscribe(nameof(OnReloadWeapon));

            if (!_config.CollectRocketsLaunched)
                Unsubscribe(nameof(OnRocketLaunched));

            if (!_config.CollectShots)
                Unsubscribe(nameof(OnWeaponFired));

            if (!_config.CollectCollectiblePickups)
                Unsubscribe(nameof(OnCollectiblePickup));

            if (!_config.CollectPlantPickups)
                Unsubscribe(nameof(OnGrowableGather));

            if (!_config.CollectGathered)
                Unsubscribe(nameof(OnDispenserGather));
        }

        private void OnServerInitialized()
        {
            var knownPlayers = PlayerDatabase.Call("GetAllKnownPlayers") as List<string>;


            foreach (var userIdString in knownPlayers)
            {
                var userId = Convert.ToUInt64(userIdString);
                PrintDebug(userId.ToString());
                var found = PlayerStats.Find(userId);
                if (found != null)
                {
                    continue;
                }
                InitPlayer(BasePlayer.FindByID(userId));
            }

            var playersCount = BasePlayer.activePlayerList.Count;
            for (var i = 0; i < playersCount; i++)
            {
                var found = PlayerStats.Find(BasePlayer.activePlayerList[i].userID);
                if (found != null) {
                    continue;
                }
                InitPlayer(BasePlayer.activePlayerList[i]);
            }
        }

        private void OnServerSave()
        {
            var current = Time.GetUnixTimestamp();
            var data = _data.Statistics.ToArray();
            var removed = 0;
            /*for (var i = _data.Statistics.Count - 1; i >= 0; i--)
            {
                var entry = data[i].Value;
                if (entry.LastUpdate + _config.Lifetime > current)
                    continue;

                _data.Statistics.Remove(data[i].Key);
                removed++;
            }*/

            PrintDebug($"Removed old data entries: {removed}");
            SaveData();
        }

        private void Unload() => SaveData();

        private void OnPlayerConnected(BasePlayer player)
        {
            InitPlayer(player);
            var stats = PlayerStats.Find(player.userID);
            stats.Joins++;
            stats.Update();
        }

        [Command("statistics.migrate")]
        private void MigrateCommand(IPlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
                return;
            try
            {
                _data = JsonConvert.DeserializeObject<PluginData>(
                    ConnectionDB?.Call<string>("API_GetValueRaw", Name),
                    new JsonSerializerSettings
                    {
                        ObjectCreationHandling = ObjectCreationHandling.Replace,
                        NullValueHandling = NullValueHandling.Ignore
                    }
                );
            }
            catch (Exception e)
            {
                PrintError($"Error: {e.Message}\n" + $"Description: {e.StackTrace}");
                return;
            }

            if (_data == null)
                return;

            SaveData();
            player.Reply("Migration successful!");
        }

        [Command("statistics.output")]
        private void OutputCommand(IPlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
                return;
            if (args == null || args.Length == 0)
                return;
            var action = args[0].ToLower();
            var nameOrID = action;
            var targets = BasePlayer.activePlayerList
                .Where(
                    x =>
                        x.UserIDString == nameOrID
                        || x.displayName.ToLower().Contains(nameOrID.ToLower())
                )
                .ToList();
            if (targets.Count == 0)
                return;
            if (targets.Count > 1)
                return;
            var target = targets[0];
            Puts(API_GetAllPlayerData(ulong.Parse(target.UserIDString)).ToString());
        }

        private void OnPlayerDisconnected(BasePlayer player) =>
            PlayerStats.Find(player.userID).Leaves++;

        private void CanExperiment(BasePlayer player, Workbench workbench) =>
            PlayerStats.Find(player.userID).Experiments++;

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (info == null || player == null || player.IsNpc)
                return;

            var stats = PlayerStats.Find(player.userID);
            if (
                _config.CollectSuicides
                && info.damageTypes.GetMajorityDamageType() == DamageType.Suicide
            )
                stats.Suicides++;
            else
            {
                if (_config.CollectDeaths)
                    stats.Deaths++;

                if (!_config.CollectKills)
                    return;

                var attacker = info.InitiatorPlayer;
                if (attacker == null || attacker.IsNpc)
                    return;

                var attackerStats = PlayerStats.Find(attacker.userID);

                attackerStats.Kills++;

                if (player.IsSleeping() || player.userID == attacker.userID)
                {
                    return;
                }

                var attackerEloCurrent = attackerStats.Elo;
                var victimEloCurrent = stats.Elo;

                EloSystem.UpdateRatings(ref attackerStats.Elo, ref stats.Elo, true);

                var attackerEloDiff = Math.Abs(attackerStats.Elo - attackerEloCurrent);
                var victimEloDiff = Math.Abs(victimEloCurrent - stats.Elo);

                SendReply(player, $"<color=#c45508>[RustWipeDay]</color>: You have died and your ELO has decreased by <color=#ff0000>{victimEloDiff}</color>, it is now at <color={EloSystem.GetColorBySkillRank(stats.Elo)}>{stats.Elo}</color>.");
                SendReply(attacker, $"<color=#c45508>[RustWipeDay]</color>: Your have killed your opponent and your ELO has increased by <color=#00ff00>{attackerEloDiff}</color> it is now at  <color={EloSystem.GetColorBySkillRank(attackerStats.Elo)}>{attackerStats.Elo}</color>.");
            }
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info?.InitiatorPlayer == null || !info.isHeadshot)
                return;

            PlayerStats.Find(info.InitiatorPlayer.userID).Headshots++;
        }

        private void OnPlayerWound(BasePlayer player) =>
            PlayerStats.Find(player.userID).WoundedTimes++;

        private void OnPlayerRecover(BasePlayer player) =>
            PlayerStats.Find(player.userID).Recoveries++;

        private void OnPlayerVoice(BasePlayer player, byte[] data) =>
            PlayerStats.Find(player.userID).VoiceBytes += (uint)data.Length;

        //private void OnItemCraftFinished(ItemCraftTask task, Item item) =>
            //PlayerStats.Find(task.owner.userID).CraftedItems++;

        private void OnItemRepair(BasePlayer player, Item item) =>
            PlayerStats.Find(player.userID).RepairedItems++;

        private void OnLiftUse(Lift lift, BasePlayer player) =>
            PlayerStats.Find(player.userID).LiftUsages++;

        private void OnLiftUse(ProceduralLift lift, BasePlayer player) =>
            PlayerStats.Find(player.userID).LiftUsages++;

        private void OnSpinWheel(BasePlayer player, SpinnerWheel wheel) =>
            PlayerStats.Find(player.userID).WheelSpins++;

        private void OnHammerHit(BasePlayer player, HitInfo info) =>
            PlayerStats.Find(player.userID).HammerHits++;

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity) =>
            PlayerStats.Find(player.userID).ExplosivesThrown++;

        private void OnReloadWeapon(BasePlayer player, BaseProjectile projectile) =>
            PlayerStats.Find(player.userID).WeaponReloads++;

        private void InitPlayer(BasePlayer player, bool isDisconnect = false)
        {
            var id = player.userID;
            var name = player.displayName;
            var ip = player.net.connection.ipaddress;
            long time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            ip = ip.Substring(0, ip.LastIndexOf(':'));
            var stats = PlayerStats.Find(player.userID);
            PlayerStats p;
            if (stats == null)
            {
                var info = new PlayerStats
                {
                    Names = new List<string> { name },
                    IPs = new List<string> { ip },
                    TimeStamps = new List<long> { time },
                    Elo = EloSystem.DefaultRating,
                    SecondsPlayed = 1
                };

                PrintDebug($"Added new user {name} ({id})");
                _data.Statistics.Add(id, info);
                SaveData(player.userID);
                return;
            }

            if (!stats.Names.Contains(name))
                stats.Names.Add(name);
            if (!stats.IPs.Contains(ip))
                stats.IPs.Add(ip);

            if (isDisconnect)
                stats.SecondsPlayed += time - stats.TimeStamps.Last();
            stats.TimeStamps.Add(time);

            PrintDebug($"Updated user {name} ({id})");
        }

        private void OnRocketLaunched(BasePlayer player, BaseEntity entity) =>
            PlayerStats.Find(player.userID).RocketsLaunched++;

        private void OnWeaponFired(
            BaseProjectile projectile,
            BasePlayer player,
            ItemModProjectile mod,
            ProtoBuf.ProjectileShoot projectiles
        ) => PlayerStats.Find(player.userID).Shots++;

        private void OnCollectiblePickup(Item item, BasePlayer player)
        {
            PrintDebug("OnCollectiblePickup called");
            var dict = PlayerStats.Find(player.userID).CollectiblePickups;
            uint count;
            if (dict.TryGetValue(item.info.shortname, out count))
                count += (uint)item.amount;
            else
            {
                count = (uint)item.amount;
                dict.Add(item.info.shortname, count);
            }

            dict[item.info.shortname] = count;
        }

        private void OnGrowableGather(GrowableEntity plant, Item item, BasePlayer player)
        {
            PrintDebug("OnGrowableGather called");
            var dict = PlayerStats.Find(player.userID).PlantPickups;
            uint count;
            if (dict.TryGetValue(item.info.shortname, out count))
                count += (uint)item.amount;
            else
            {
                count = (uint)item.amount;
                dict.Add(item.info.shortname, count);
            }

            dict[item.info.shortname] = count;
        }

        private void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            PrintDebug("OnDispenserGather called");
            var dict = PlayerStats.Find(((BasePlayer)entity).userID).Gathered;
            if (dict.ContainsKey(item.info.shortname))
                dict[item.info.shortname] += (uint)item.amount;
            else
            {
                dict.Add(item.info.shortname, (uint)item.amount);
            }
        }

        #endregion

        #region API

        // General API

        private JObject API_GetAllData() => JObject.FromObject(_data.Statistics);

        private JObject API_GetAllPlayerData(ulong id) => JObject.FromObject(PlayerStats.Find(id));

        private bool API_ContainsPlayer(ulong id) => PlayerStats.Find(id) != null;

        private uint? API_GetElo(ulong id) => PlayerStats.Find(id)?.Elo;

        private uint? API_GetJoins(ulong id) => PlayerStats.Find(id)?.Joins;

        private uint? API_GetLeaves(ulong id) => PlayerStats.Find(id)?.Leaves;

        private uint? API_GetKills(ulong id) => PlayerStats.Find(id)?.Kills;

        private uint? API_GetDeaths(ulong id) => PlayerStats.Find(id)?.Deaths;

        private uint? API_GetSuicides(ulong id) => PlayerStats.Find(id)?.Suicides;

        private uint? API_GetShots(ulong id) => PlayerStats.Find(id)?.Shots;

        private uint? API_GetHeadshots(ulong id) => PlayerStats.Find(id)?.Headshots;

        private uint? API_GetExperiments(ulong id) => PlayerStats.Find(id)?.Experiments;

        private uint? API_GetRecoveries(ulong id) => PlayerStats.Find(id)?.Recoveries;

        private uint? API_GetVoiceBytes(ulong id) => PlayerStats.Find(id)?.VoiceBytes;

        private uint? API_GetWoundedTimes(ulong id) => PlayerStats.Find(id)?.WoundedTimes;

        private uint? API_GetCraftedItems(ulong id) => PlayerStats.Find(id)?.CraftedItems;

        private uint? API_GetRepairedItems(ulong id) => PlayerStats.Find(id)?.RepairedItems;

        private uint? API_GetLiftUsages(ulong id) => PlayerStats.Find(id)?.LiftUsages;

        private uint? API_GetWheelSpins(ulong id) => PlayerStats.Find(id)?.WheelSpins;

        private uint? API_GetHammerHits(ulong id) => PlayerStats.Find(id)?.HammerHits;

        private uint? API_GetExplosivesThrown(ulong id) => PlayerStats.Find(id)?.ExplosivesThrown;

        private uint? API_GetWeaponReloads(ulong id) => PlayerStats.Find(id)?.WeaponReloads;

        private uint? API_GetRocketsLaunched(ulong id) => PlayerStats.Find(id)?.RocketsLaunched;

        private long? API_GetSecondsPlayed(ulong id) => PlayerStats.Find(id)?.SecondsPlayed;

        private List<string> API_GetNames(ulong id) => PlayerStats.Find(id)?.Names;

        private List<string> API_GetIPs(ulong id) => PlayerStats.Find(id)?.IPs;

        private List<long> API_GetTimeStamps(ulong id) => PlayerStats.Find(id)?.TimeStamps;

        // Gather API

        private Dictionary<string, uint> API_GetCollectiblePickups(ulong id) =>
            PlayerStats.Find(id)?.CollectiblePickups;

        private Dictionary<string, uint> API_GetPlantPickups(ulong id) =>
            PlayerStats.Find(id)?.PlantPickups;

        private Dictionary<string, uint> API_GetGathered(ulong id) =>
            PlayerStats.Find(id)?.Gathered;

        private uint? API_GetCollectiblePickups(ulong id, string shortname)
        {
            var data = API_GetCollectiblePickups(id);
            uint amount = 0;
            if (data?.TryGetValue(shortname, out amount) == true)
                return amount;

            return null;
        }

        private uint? API_GetPlantPickups(ulong id, string shortname)
        {
            var data = API_GetPlantPickups(id);
            uint amount = 0;
            if (data?.TryGetValue(shortname, out amount) == true)
                return amount;

            return null;
        }

        private uint? API_GetGathered(ulong id, string shortname)
        {
            var data = API_GetGathered(id);
            uint amount = 0;
            if (data?.TryGetValue(shortname, out amount) == true)
                return amount;

            return null;
        }

        private string API_GetEloColorRank(uint elo) => EloSystem.GetColorBySkillRank(elo);

        #endregion

        #region Helpers

        private static void PrintDebug(string message)
        {
            if (!_config.Debug)
                return;
            Debug.Log($"DEBUG: {message}");
        }

        #endregion
    }
}
