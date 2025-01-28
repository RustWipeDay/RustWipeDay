using System;
using System.Collections.Generic;
using Oxide.Core;
using Rust;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Database;
using UnityEngine;
using System.Linq;
using Newtonsoft.Json.Linq;
using Oxide.Core.Configuration;
using Steamworks;
using SpawnAddonCallback = System.Func<UnityEngine.Vector3, UnityEngine.Quaternion, Newtonsoft.Json.Linq.JObject, UnityEngine.Component>;
using KillAddonCallback = System.Action<UnityEngine.Component>;
using UpdateAddonCallback = System.Action<UnityEngine.Component, Newtonsoft.Json.Linq.JObject>;
using AddDisplayInfoCallback = System.Action<UnityEngine.Component, Newtonsoft.Json.Linq.JObject, System.Text.StringBuilder>;
using SetAddonDataCallback = System.Action<UnityEngine.Component, object>;
using static BaseEntity.RPC_Server;

namespace Oxide.Plugins
{
    [Info("DogTags", "TTV OdsScott", "1.0.0")]
    [Description("Drops a bounty tag item when a player dies")]
    public class DogTags : RustPlugin
    {
        private const string DogTagPrefab = "assets/prefabs/misc/dogtag_neutral.prefab";
        private List<BaseEntity> SpawnedEntities = new List<BaseEntity>();
        private string[] BountyHunterGuids = {};
        private Dictionary<ulong, int> playerPoints = new Dictionary<ulong, int>();
        private Dictionary<ulong, uint> playerRewards = new Dictionary<ulong, uint>();
        

        Core.SQLite.Libraries.SQLite SQLite = Interface
            .GetMod()
            .GetLibrary<Core.SQLite.Libraries.SQLite>();
        Connection dbConnection;

        [PluginReference]
        private Plugin Kits = null;

        [PluginReference]
        private Plugin HonorSystem = null;

        [PluginReference]
        private Plugin MonumentAddons = null;

        [PluginReference]
        private Plugin Duelist = null;


        private void Init() { }

        SetAddonDataCallback _setAddonData;

        void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == MonumentAddons)
            {
                RegisterCustomAddon();
            }
        }

        void RegisterCustomAddon()
        {
            var registeredAddon = MonumentAddons.Call(
                "API_RegisterCustomAddon",
                this,
                "bountyhunter",
                new Dictionary<string, object>
                {
                    ["Spawn"] = new SpawnAddonCallback(SpawnCustomAddon),
                    ["Kill"] = new KillAddonCallback(KillCustomAddon)
                }
            ) as Dictionary<string, object>;

            if (registeredAddon == null)
            {
                LogError($"Error registering addon with Monument Addons.");
                return;
            }

            _setAddonData = registeredAddon["SetData"] as SetAddonDataCallback;
            if (_setAddonData == null)
            {
                LogError($"SetData method not present in MonumentAddons return value.");
            }
        }

        private UnityEngine.Component SpawnCustomAddon(Vector3 position, Quaternion rotation, JObject data)
        {
            var entity = GameManager.server.CreateEntity("assets/prefabs/npc/bandit/shopkeepers/missionprovider_test.prefab", position, rotation) as NPCTalking;
            entity.EnableSaving(false);
            entity.Spawn();
            entity._displayName = "Bounty Hunter";
            entity.inventory.Strip();
            entity.inventory.containerWear.AddItem(
                ItemManager.FindItemDefinition("hazmatsuit.nomadsuit"),
                1
            );

            SpawnedEntities.Add(entity);

            return entity;
        }

        private void KillCustomAddon(UnityEngine.Component component)
        {
            var entity = component as BaseEntity;
            if (entity != null && !entity.IsDestroyed)
            {
                entity.Kill();
                SpawnedEntities.Remove(entity);
            }
        }

        bool InDuel(BasePlayer player)
        {
            if (player == null) return false;
            return Duelist?.Call<bool>("inEvent", player) ?? false;
        }

        private void OnServerInitialized()
        {
            // LoadBountyHunters();
            LoadData();
            var playersCount = BasePlayer.activePlayerList.Count;
            for (var i = 0; i < playersCount; i++)
            {
                OnPlayerInit(BasePlayer.activePlayerList[i]);
            }
            if(MonumentAddons) {
                RegisterCustomAddon();
            }
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void Unload()
        {
            SaveData();
            // DestroySpawnedEntities();
            // SQLite.CloseDb(dbConnection);
        }

        private void OnServerShutdown()
        {
            SaveData();
            // DestroySpawnedEntities();
            // SQLite.CloseDb(dbConnection);
        }

        // private void LoadBountyHunters() {
        //     BountyHunterGuids = Interface.Oxide.DataFileSystem.ReadObject<string[]>("DogTags");
        // }

        private void LoadData()
        {
            dbConnection = SQLite.OpenDb("PlayerPoints.db", this);
            SQLite.Insert(
                Core.Database.Sql.Builder.Append(
                    "CREATE TABLE IF NOT EXISTS PlayerPoints (userId TEXT PRIMARY KEY, points INTEGER)"
                ),
                dbConnection
            );

            SQLite.Query(
                Core.Database.Sql.Builder.Append("SELECT * FROM PlayerPoints"),
                dbConnection,
                query =>
                {
                    foreach (var data in query)
                    {
                        // Puts(data["userId"].ToString());
                        // Puts(data["points"].ToString());
                        var userId = ulong.Parse(data["userId"].ToString());
                        var points = int.Parse(data["points"].ToString());
                        playerPoints[userId] = points;
                    }
                }
            );
        }

        private void SaveData()
        {
            foreach (var entry in playerPoints)
            {
                var userId = entry.Key.ToString();
                var points = entry.Value.ToString();
                SQLite.Insert(
                    Core.Database.Sql.Builder.Append(
                        "UPDATE PlayerPoints SET points = @1 WHERE userId = @0",
                        userId,
                        points
                    ),
                    dbConnection,
                    rowsAffected =>
                    {
                        if (rowsAffected > 0)
                        {
                            // Puts("New record inserted with ID: {0}", dbConnection.LastInsertRowId);
                        }
                        else
                        {
                            SQLite.Insert(
                                Core.Database.Sql.Builder.Append(
                                    "INSERT OR REPLACE INTO PlayerPoints (userId, points) VALUES (@0, @1)",
                                    userId,
                                    points
                                ),
                                dbConnection
                            );
                        }
                    }
                );
            }
        }

        private void AddPoints(ulong playerId, int points, string type)
        {
            if (playerPoints.ContainsKey(playerId))
            {
                playerPoints[playerId] += points;
            }
            else
            {
                playerPoints[playerId] = points;
            }

            Puts(type);

            Interface.CallHook("OnAddBountyPoints", playerId, points, type);
        }

        private void OnPlayerInit(BasePlayer player)
        {
            AddPoints(player.userID, 0, "init");
        }

        private int GetPoints(ulong playerId) {
            if (!playerPoints.ContainsKey(playerId)) {
                return 0;
            }

            if (!playerPoints.TryGetValue(playerId, out int points)) {
                return 0;
            }

            return points;
        }

        [ChatCommand("bounty")]
        private void PointsCommand(BasePlayer player, string command, string[] args)
        {
            if (args.Length < 1)
            {
                SendReply(
                    player,
                    $"<color=#c45508>[RustWipeDay]</color>: You are currently at {GetPoints(player.userID)} bounty points."
                );
                return;
            }

            if (!player.IsAdmin)
            {
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You don't have permission to use this command.");
                return;
            }

            if (args[0].ToLower() == "add" && args.Length == 3)
            {
                if (
                    ulong.TryParse(args[1], out ulong targetUserId)
                    && int.TryParse(args[2], out int points)
                )
                {
                    AddPoints(targetUserId, points, "admin");
                    SendReply(player, $"<color=#c45508>[RustWipeDay]</color>: Added {points} bounty points to user {targetUserId}.");
                }
                else
                {
                    SendReply(player, "<color=#c45508>[RustWipeDay]</color>: Invalid arguments. Usage: bounty add <userId> <points>");
                }
            }
            else
            {
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: Invalid command. Usage: bounty [add <userId> <points>]");
            }
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo hitInfo)
        {
            var npcDropChance = NpcDropsTagChance(player);
            if (npcDropChance > 0)
            {
                int maxChanceValue = 10; // This value is used to cap the npcDropChance at 100%
                float actualChance = Mathf.Min(npcDropChance * 10, 100) / 100.0f; // Convert to decimal for percentage (e.g., 2 becomes 0.2 for 20%)
                Puts(actualChance);

                float randomValue = UnityEngine.Random.Range(0f, 1f); // Generate a random number between 0 and 1 for percentage comparison
                Puts(randomValue);
                if (randomValue <= actualChance)
                {
                    DropDogTags(player, 1, "dogtagneutral");
                }
                return;
            }

            if (hitInfo == null)
            {
                return;
            }

            var attacker = hitInfo.InitiatorPlayer;

            if (InDuel(attacker) || InDuel(player))
            {
                return;
            }

            if (player == null || 
                attacker == null ||
                (player != null && player.secondsSleeping >= 300) ||
                attacker == player ||
                (attacker != null && attacker.IsNpc) || 
                hitInfo.damageTypes.GetMajorityDamageType() == DamageType.Suicide
            )
            {
                return;
            }

            if (!playerPoints.ContainsKey(player.userID))
            {
                playerPoints[player.userID] = 0;
            }

            HandlePlayerPointsOnDeath(attacker, player);
        }

        void HandlePlayerPointsOnDeath(BasePlayer attacker, BasePlayer victim) {
            var currentPoints = playerPoints[victim.userID];

            var pointsToLose = CalculatePointsToLose(currentPoints);
    
            if (pointsToLose < 0)
            {
                return;
            }

            AddPoints(victim.userID, -1 * (int)pointsToLose, "death");
            SendReply(victim, $"<color=#c45508>[RustWipeDay]</color>: You have died and lost {pointsToLose} bounty points.");

            if ((attacker == null || attacker.IsNpc) || attacker == victim)
            {
                DropDogTags(victim, (int)pointsToLose);
                return;
            }

            double tagsToDrop = pointsToLose / 2;
            var pointsToEarn = Math.Floor(tagsToDrop);
            if (tagsToDrop < 1) {
                tagsToDrop = 1;
            }

            if (pointsToEarn > 0) {
                AddPoints(attacker.userID, (int)pointsToEarn, "kill");
                SendReply(attacker, $"<color=#c45508>[RustWipeDay]</color>: You have killed a player and gained {pointsToEarn} bounty points.");
            }
        
            DropDogTags(victim, (int)tagsToDrop);
            return;  
        }

        int NpcDropsTagChance(BasePlayer entity) {
            var isPlayer = entity is BasePlayer && !entity.IsNpc;
            
            if (isPlayer) {
                return 0;
            }

            return GetEntityHonorRank(entity) * 3;
        }
        
        int GetEntityHonorRank(BaseCombatEntity entity)
        {
            return HonorSystem.Call<int>("GetEntityHonorRank", entity);
        }

        string GetDisplayName(BaseCombatEntity entity)
        {
            return HonorSystem.Call<string>("GetDisplayName", entity);
        }

        private void DropDogTags(BasePlayer player, int amount, string type="bluedogtags") {
            var dropPosition = player.GetDropPosition();
            var dogTagItem = ItemManager.CreateByItemID(
                ItemManager.FindItemDefinition(type).itemid,
                amount
            );

            if (dogTagItem != null)
            {
                var vector3 = new Vector3(
                    UnityEngine.Random.Range(-2f, 2f),
                    0.2f,
                    UnityEngine.Random.Range(-2f, 2f)
                );
                dogTagItem.Drop(
                    dropPosition,
                    player.GetInheritedDropVelocity() + vector3.normalized * 3f
                );
            }
        }

        object OnNpcConversationStart(
            NPCTalking npcTalking,
            BasePlayer player,
            ConversationData conversationData
        )
        {
            if (!SpawnedEntities.Contains(npcTalking)) {
                return null;
            }
            ClaimDogTags(player);
            Kits.Call("OpenKitGrid", player, 0, 0, true);
            //OpenGui(player);
            return true;
        }

        [ConsoleCommand("bountyhunter.close")]
        private void BountyHunterCloseConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null)
                return;

            BasePlayer player = arg.Player();
            CloseBountyHunter(player);
        }

        [ConsoleCommand("bountyhunter.reward")]
        private void ClaimReward(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerInRangeOfEntities(player, SpawnedEntities, 5))
                return;

            CloseBountyHunter(player);

            ulong playerId = player.userID;

            Kits.Call("OpenKitGrid", player, 0, 0, true);
        }

        [ConsoleCommand("bountyhunter.claim")]
        private void BountyHunterClaimConsoleCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !IsPlayerInRangeOfEntities(player, SpawnedEntities, 5))
                return;

            ClaimDogTags(player);

            CloseBountyHunter(player);
        }

        private void ClaimDogTags(BasePlayer player)
        {
            var points = CountAndRemoveItemFromPlayer(player, "dogtagneutral");
            var points2 = CountAndRemoveItemFromPlayer(player, "bluedogtags");

            if (points > 0)
            {
                AddPoints(player.userID, points, "dogtagneutral");

            }


            if (points2 > 0)
            {
                AddPoints(player.userID, points2, "bluedogtags");
            }
        }

        [HookMethod("TakePoints")]
        public bool TakePoints(ulong playerId, int amount) {
            var rangeCheck = false;
            var player = BasePlayer.FindByID(playerId);
            if (player == null || (rangeCheck && !IsPlayerInRangeOfEntities(player, SpawnedEntities, 5)))
                return false;
            
            if (playerPoints.ContainsKey(playerId) && playerPoints[playerId] >= amount)
            {
                playerPoints[playerId] -= amount;
                Interface.CallHook("OnTakeBountyPoints", playerId, amount);
                return true;
            }           
            return false;
        }

        private bool IsPlayerInRangeOfEntities(
            BasePlayer player,
            List<BaseEntity> entities,
            float range
        )
        {
            if (player == null || entities == null || entities.Count == 0)
                return false;

            foreach (var entity in entities)
            {
                if (entity == null)
                    continue;

                float distance = Vector3.Distance(
                    player.transform.position,
                    entity.transform.position
                );
                if (distance <= range)
                    return true;
            }

            return false;
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

        private int CountAndRemoveItemFromPlayer(BasePlayer player, string shortname)
        {
            int totalCount = 0;

            ItemContainer beltContainer = player.inventory.containerBelt;
            ItemContainer mainContainer = player.inventory.containerMain;

            totalCount += CountAndRemoveItemInContainer(beltContainer, shortname);
            totalCount += CountAndRemoveItemInContainer(mainContainer, shortname);

            return totalCount;
        }

        private int CountAndRemoveItemInContainer(ItemContainer container, string shortname)
        {
            int count = 0;

            foreach (Item item in container.itemList)
            {
                if (item.info.shortname == shortname)
                {
                    count += item.amount;
                    item.Remove();
                }
            }

            return count;
        }

        private int CalculatePointsToLose(int currentPoints)
        {
            if (currentPoints <= -6)
            {
                return 0;
            }
            if (currentPoints <= 0)
            {
                // If points are already 0 or below, set the minimum to -3
                return 1;
            }
            else
            {
                // Calculate points to lose based on logarithmic scale
                // Adjust the base and multiplier values as needed
                float baseValue = 2f; // Base value for the logarithm

                float logValue = Mathf.Log(currentPoints, baseValue); // Calculate the logarithm
                int pointsToLose = Mathf.RoundToInt(logValue); // Apply the multiplier and round to nearest integer
                if (pointsToLose <= 0)
                {
                    pointsToLose = 1;
                }
                return pointsToLose;
            }
        }

        private void CloseBountyHunter(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "BountyHunterOverlay");
        }

        private void OpenGui(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "BountyHunterOverlay");
            var container = new CuiElementContainer();

            // Black bar on top
            container.Add(
                new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMax = "0 0"
                    },
                    CursorEnabled = true
                },
                "Overlay",
                "BountyHunterOverlay"
            );

            // Black bar on top
            container.Add(
                new CuiPanel
                {
                    Image = { Color = "0 0 0 1" },
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 0.125",
                        OffsetMax = "0 0"
                    }
                },
                "BountyHunterOverlay",
                "TopBar"
            );

            // Black bar on bottom
            container.Add(
                new CuiPanel
                {
                    Image = { Color = "0 0 0 1" },
                    RectTransform =
                    {
                        AnchorMin = "0 0.875",
                        AnchorMax = "1 1",
                        OffsetMax = "0 0"
                    }
                },
                "BountyHunterOverlay",
                "BottomBar"
            );

            // Dialog window
            container.Add(
                new CuiPanel
                {
                    Image = { Color = "0.2 0.2 0.2 0.9" },
                    RectTransform = { AnchorMin = "0.60 0.375", AnchorMax = "0.85 0.675" }
                },
                "BountyHunterOverlay",
                "DialogWindow"
            );

            // Dialog window
            container.Add(
                new CuiPanel
                {
                    Image = { Color = "0.2 0.2 0.2 1" },
                    RectTransform = { AnchorMin = "0.05 0.85", AnchorMax = "0.4 0.95" }
                },
                "DialogWindow",
                "NameOverlay"
            );

            // Name Text
            container.Add(
                new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Text =
                    {
                        Color = "0.72 0.72 0.72 1",
                        FontSize = 16,
                        Text = "Bounty Hunter",
                        Align = TextAnchor.MiddleCenter
                    }
                },
                "NameOverlay",
                "BountyHunterNpcName"
            );

            // Close button
            container.Add(
                new CuiButton
                {
                    Button =
                    {
                        Command = "bountyhunter.close",
                        Color = "0.8 0.1 0.1 1",
                        FadeIn = 0.5f
                    },
                    RectTransform = { AnchorMin = "0.90 0.90", AnchorMax = "1 1" },
                    Text = { Text = "X", Align = TextAnchor.MiddleCenter }
                },
                "DialogWindow",
                "CloseButton"
            );

            // Text overlay
            container.Add(
                new CuiPanel
                {
                    Image = { Color = "0.2 0.2 0.2 1" },
                    RectTransform = { AnchorMin = "0.05 0.40", AnchorMax = "0.95 0.80" }
                },
                "DialogWindow",
                "TextOverlay"
            );

            // Name Text
            container.Add(
                new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.01 0.01", AnchorMax = "0.99 0.99" },
                    Text =
                    {
                        Color = "0.53 0.51 0.49 1",
                        FontSize = 10,
                        Text =
                            "Hey there! I am looking for Bounty Tags, do you have any? If you give me some, I will reward you with some loot. I heard you can find them near dead bodies.",
                        Align = TextAnchor.UpperLeft,
                        Font = "robotocondensed-regular.ttf"
                    }
                },
                "TextOverlay",
                "TextConversation"
            );

            // Close button
            container.Add(
                new CuiButton
                {
                    Button = { Command = "bountyhunter.close", Color = "0.8 0.1 0.1 1", },
                    RectTransform = { AnchorMin = "0.90 0.90", AnchorMax = "1 1" },
                    Text = { Text = "X", Align = TextAnchor.MiddleCenter }
                },
                "DialogWindow",
                "CloseButton"
            );

            // Claim Button
            container.Add(
                new CuiButton
                {
                    Button = { Command = "bountyhunter.claim", Color = "0.16 0.16 0.13 1", },
                    RectTransform = { AnchorMin = "0.05 0.30", AnchorMax = "0.95 0.35" },
                    Text =
                    {
                        FontSize = 10,
                        Font = "robotocondensed-regular.ttf",
                        Text = "1. Hand over the bounty tags.",
                        Align = TextAnchor.MiddleLeft
                    }
                },
                "DialogWindow",
                "ClaimButton"
            );

            // Claim Button
            container.Add(
                new CuiButton
                {
                    Button = { Command = "bountyhunter.reward", Color = "0.16 0.16 0.13 1", },
                    RectTransform = { AnchorMin = "0.05 0.20", AnchorMax = "0.95 0.25" },
                    Text =
                    {
                        FontSize = 10,
                        Font = "robotocondensed-regular.ttf",
                        Text = "2. Ask about the loot.",
                        Align = TextAnchor.MiddleLeft
                    }
                },
                "DialogWindow",
                "ClaimButton"
            );

            // Create the GUI
            CuiHelper.AddUi(player, container);
        }

        [ChatCommand("spawnbountyhunter")]
        private void SpawnNpcWithCuiCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You must be an admin to use this command.");
                return;
            }

            SpawnNpcWithCui(player);
        }

        private void SpawnNpcWithCui(BasePlayer player)
        {
            try
            {
                var npc = GameManager.server
                    .CreateEntity(
                        "assets/prefabs/npc/bandit/shopkeepers/missionprovider_test.prefab",
                        player.transform.position + new Vector3(3f, 0f, 0f),
                        Quaternion.identity
                    )
                    .ToPlayer();

                if (npc != null)
                {
                    npc.Spawn();
                    npc._displayName = "Bounty Hunter";
                    npc.inventory.Strip();
                    npc.inventory.containerWear.AddItem(
                        ItemManager.FindItemDefinition("hazmatsuit.nomadsuit"),
                        1
                    );
                    npc.SendNetworkUpdateImmediate();
                    SpawnedEntities.Add(npc);
                }
            }
            catch (Exception e)
            {
                Puts(e.Message);
            }
        }

        private void DestroySpawnedEntities()
        {
            foreach (var entity in SpawnedEntities)
            {
                if (entity != null && !entity.IsDestroyed)
                    entity.Kill();
            }

            SpawnedEntities.Clear();
        }
    }
}
