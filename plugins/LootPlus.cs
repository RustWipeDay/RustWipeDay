using ConVar;
using Facepunch;
using Network;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Libraries;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using UnityEngine;
using static ConsoleSystem;
using static LootContainer;

namespace Oxide.Plugins
{
    [Info("LootPlus", "ODS - RustWipeDay", "2.0.0")]
    [Description("A loot container modification system")]
    public class LootPlus : RustPlugin
    {
        Dictionary<string, LootSpawn> LootSpawns = new Dictionary<string, LootSpawn>();
        List<ulong> SimulatedCrateIds = new List<ulong>();
        ConfigData LootConfig = new ConfigData();

        static LootPlus Instance;

        private void Init()
        {
            Instance = this;
            permission.RegisterPermission("lootplus.simulate", this);
            LoadConfigVariables();
        }

        public class JsonLootContainer
        {
            public float MinSecondsBetweenRefresh;
            public float MaxSecondsBetweenRefresh;
            public int ScrapAmount;
            public string LootDefinition;
            public int MaxDefinitionsToSpawn;
            public string PanelName;
            public JsonLootSpawnSlot[] LootSpawnSlots;
            public int InventorySlots;

            public LootSpawn GetLootDefinition()
            {
                return Instance.LootSpawnByName(LootDefinition);
            }

            private LootContainer.LootSpawnSlot[] _lootSpawnSlots = null;

            public LootContainer.LootSpawnSlot[] GetLootSpawnSlots()
            {
                if (_lootSpawnSlots == null)
                {
                    _lootSpawnSlots = LootSpawnSlots.Select(lootSpawnSlot => lootSpawnSlot.ToObject()).Where(lootSpawnSlot => lootSpawnSlot.definition != null).ToArray();
                }

                return _lootSpawnSlots;
            }

            public void Update(ref LootContainer lootContainer)
            {
                lootContainer.LootSpawnSlots = GetLootSpawnSlots();
                lootContainer.lootDefinition = GetLootDefinition();
                lootContainer.maxDefinitionsToSpawn = MaxDefinitionsToSpawn;
                lootContainer.scrapAmount = ScrapAmount * Instance.LootConfig.ScrapMultiplier;
                lootContainer.panelName = PanelName;
                lootContainer.inventorySlots = InventorySlots;
                lootContainer.minSecondsBetweenRefresh = MinSecondsBetweenRefresh;
                lootContainer.maxSecondsBetweenRefresh = MaxSecondsBetweenRefresh;

                if (lootContainer.inventory == null)
                {
                    lootContainer.CreateInventory(giveUID: true);
                    lootContainer.OnInventoryFirstCreated(lootContainer.inventory);
                }

                lootContainer.inventory.capacity = InventorySlots;

                lootContainer.SpawnLoot();
            }
        }

        public class JsonHumanNpc
        {
            public JsonLootSpawnSlot[] LootSpawnSlots;

            private LootContainer.LootSpawnSlot[] _lootSpawnSlots = null;

            public LootContainer.LootSpawnSlot[] GetLootSpawnSlots()
            {
                if (_lootSpawnSlots == null)
                {
                    _lootSpawnSlots = LootSpawnSlots.Select(lootSpawnSlot => lootSpawnSlot.ToObject()).Where(lootSpawnSlot => lootSpawnSlot.definition != null).ToArray();
                }

                return _lootSpawnSlots;
            }
        }

        public class JsonLootSpawnSlot
        {
            public string Definition;
            public int NumberToSpawn;
            public float Probability;
            public string OnlyWithLoadoutNamed;

            public LootContainer.LootSpawnSlot ToObject()
            {
                var lootSpawnSlot = new LootContainer.LootSpawnSlot();
                lootSpawnSlot.definition = Instance.LootSpawnByName(Definition);
                lootSpawnSlot.numberToSpawn = NumberToSpawn;
                lootSpawnSlot.probability = Probability;
                lootSpawnSlot.onlyWithLoadoutNamed = OnlyWithLoadoutNamed;

                return lootSpawnSlot;
            }
        }

        public class JsonLootSpawn
        {
            public JsonItemAmountRanged[] Items;
            public JsonLootSpawnEntry[] SubSpawn;

            public LootSpawn ToObject()
            {
                var lootSpawn = ScriptableObject.CreateInstance<LootSpawn>();
                lootSpawn.items = Items.Select((item) => item.ToObject()).ToArray();
                lootSpawn.subSpawn = SubSpawn.Select(subSpawn => subSpawn.ToObject()).ToArray();

                return lootSpawn;
            }
        }

        public class JsonItemAmountRanged
        {
            public string Item;
            public float Amount;
            public float MaxAmount;

            public ItemAmountRanged ToObject()
            {
                var itemAmountRanged = new ItemAmountRanged();
                itemAmountRanged.amount = Amount;
                itemAmountRanged.maxAmount = MaxAmount;
                itemAmountRanged.itemDef = ItemManager.FindItemDefinition(Item);

                return itemAmountRanged;
            }
        }

        public class JsonLootSpawnEntry
        {
            public string Category;
            public int ExtraSpawns;
            public int Weight;

            public LootSpawn.Entry ToObject()
            {
                var entry = new LootSpawn.Entry();
                entry.weight = Weight;
                entry.category = Instance.LootSpawnByName(Category);
                entry.extraSpawns = ExtraSpawns;

                return entry;
            }
        }

        public LootSpawn LootSpawnByName(string name)
        {
            if (name == null || name.Equals(""))
            {
                return null;
            }

            if (LootSpawns.TryGetValue(name, out LootSpawn lootSpawn))
            {
                return lootSpawn;
            }

            if (LootConfig.LootSpawns.TryGetValue(name, out JsonLootSpawn jsonLootSpawn))
            {
                var unloadedLootSpawn = jsonLootSpawn.ToObject();
                LootSpawns.Add(name, unloadedLootSpawn);
                return unloadedLootSpawn;
            }

            Puts("ERROR: Could not find loot spawn: " + name);

            return null;
        }

        void RecurseLootSpawn(Dictionary<string, JsonLootSpawn> lootSpawns, LootSpawn lootSpawn)
        {
            var lootSpawnKey = lootSpawn.ToString().Replace(" (LootSpawn)", "") ?? "";
            if (lootSpawns.ContainsKey(lootSpawnKey))
            {
                return;
            }
            lootSpawns.Add(lootSpawnKey, new JsonLootSpawn()
            {
                Items = lootSpawn.items.ToList().Select(itemAmount => new JsonItemAmountRanged()
                {
                    Item = itemAmount.itemDef.shortname,
                    MaxAmount = itemAmount.maxAmount,
                    Amount = itemAmount.amount
                }).ToArray(),
                SubSpawn = lootSpawn.subSpawn.ToList().Select(subSpawn => new JsonLootSpawnEntry()
                {
                    ExtraSpawns = subSpawn.extraSpawns,
                    Category = subSpawn.category.ToString().Replace(" (LootSpawn)", "") ?? "",
                    Weight = subSpawn.weight
                }).ToArray()
            });

            lootSpawn.subSpawn.ToList().ForEach(subSpawn => RecurseLootSpawn(lootSpawns, subSpawn.category));
        }

        [Command("lootplus.simulate")]
        private void CmdChat(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            if (!permission.UserHasPermission(player.UserIDString, "lootplus.simulate"))
            {
                player.ChatMessage("You do not have permission to use this command.");
                return;
            }

            if (args.Length == 0)
            {
                player.ChatMessage("Please specify a part of the prefab name to search for.");
                return;
            }

            var searchTerm = args[0].ToLower();
            var allPrefabs = GameManifest.Current.pooledStrings.Where(x => x.str.Contains(searchTerm) && x.str.Contains("/")).ToList();

            if (!allPrefabs.Any())
            {
                player.ChatMessage("No prefabs found matching your search term.");
                return;
            }

            if (allPrefabs.Count > 1)
            {
                player.ChatMessage($"Multiple prefabs found, please be more specific. Showing first 5 matches: {string.Join(", ", allPrefabs.Select(x => x.str).Take(5))}");
                return;
            }

            var prefab = args[0];

            var prefabPath = allPrefabs.First().str;
            var entity = GameManager.server.CreateEntity(prefabPath, new Vector3(0, 0, 0), Quaternion.identity) as StorageContainer;

            entity.Spawn();

            SimulatedCrateIds.Add(entity.net.ID.Value);

            timer.Once(0.5f, () =>
            {
                var loot = player.inventory.loot;

                loot.Clear();
                loot.PositionChecks = false;
                loot.entitySource = entity;
                loot.itemSource = null;
                loot.AddContainer(entity.inventory);
                loot.SendImmediate();
                loot.MarkDirty();

                player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), (entity as LootContainer).panelName);
            });
        }

        void OnLootEntityEnd(BasePlayer player, BaseEntity entity)
        {
            if (SimulatedCrateIds.Contains(entity.net.ID.Value))
            {
                entity.Kill();
            }
            return;
        }

        void OnEntitySpawn(HumanNPC humanNpc)
        {
            if (!LootConfig.HumanNpcs.TryGetValue(humanNpc.PrefabName, out var humanNpcConfig))
            {
                return;
            }
            humanNpc.LootSpawnSlots = humanNpcConfig.GetLootSpawnSlots();
        }

        void OnEntitySpawn(LootContainer lootContainer)
        {
            if (!LootConfig.LootContainers.TryGetValue(lootContainer.PrefabName, out var lootContainerConfig))
            {
                return;
            }

            lootContainerConfig.Update(ref lootContainer);
        }

        private void OnServerInitialized()
        {
            try
            {
                NextTick(() =>
                {
                    var populatedContainers = 0;
                    foreach (
                        var container in BaseNetworkable.serverEntities
                            .Where(
                                p =>
                                    p != null
                                    && p.GetComponent<BaseEntity>() != null
                                    && p is LootContainer
                            )
                            .Cast<LootContainer>()
                            .ToList()
                    )
                    {
                        if (container == null)
                        {
                            continue;
                        }
                        OnEntitySpawn(container);
                        container.SpawnLoot();
                        populatedContainers++;
                    }

                    Puts($"Populated {populatedContainers} containers.");
                });
            }
            catch (Exception e)
            {
                Interface.GetMod().UnloadPlugin(Name);
                return;
            }
        }

        #region config

        public class ConfigData
        {
            [JsonProperty(Order = -2)]
            public bool Enabled = true;
            [JsonProperty(Order = -1)]
            public int ScrapMultiplier = 2;
            public Dictionary<string, JsonLootContainer> LootContainers = new Dictionary<string, JsonLootContainer>();
            public Dictionary<string, JsonHumanNpc> HumanNpcs = new Dictionary<string, JsonHumanNpc>();
            public Dictionary<string, JsonLootSpawn> LootSpawns = new Dictionary<string, JsonLootSpawn>();
        }

        private void LoadConfigVariables()
        {
            LootConfig = Config.ReadObject<ConfigData>();

            if (LootConfig == null)
            {
                LoadDefaultConfig();
            }
        }


        Dictionary<string, JsonHumanNpc> GenerateNpcLootConfig(Dictionary<string, JsonLootSpawn> lootSpawns)
        {
            Dictionary<string, JsonHumanNpc> humanNpcs = new Dictionary<string, JsonHumanNpc>();

            List<string> prefabList = new List<string>() {
                "assets/rust.ai/agents/npcplayer/humannpc/tunneldweller/npc_tunneldweller.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/underwaterdweller/npc_underwaterdweller.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_cargo.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_cargo_turret_any.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_cargo_turret_lr300.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_ch47_gunner.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_excavator.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_lr300.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_mp5.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_pistol.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_shotgun.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_junkpile_pistol.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_patrol.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_peacekeeper.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roamtethered.prefab",
                // "assets/prefabs/npc/scarecrow/scarecrow.prefab",
                // "assets/rust.ai/agents/zombie/zombie.prefab"
            };

            foreach (var prefab in prefabList)
            {
                var npc = GameManager.server.CreateEntity(prefab, new Vector3(0, 0, 0), Quaternion.identity) as HumanNPC;
                if (npc == null)
                {
                    continue;
                }

                npc.Spawn();

                var lootSpawnSlots = new List<JsonLootSpawnSlot>() { };

                foreach (var lootSpawnSlot in npc.LootSpawnSlots)
                {
                    var lootSpawnKey = lootSpawnSlot.definition?.ToString().Replace(" (LootSpawn)", "") ?? "";
                    if (lootSpawnKey == "")
                    {
                        continue;
                    }

                    lootSpawnSlots.Add(new JsonLootSpawnSlot()
                    {
                        Definition = lootSpawnKey,
                        NumberToSpawn = lootSpawnSlot.numberToSpawn,
                        Probability = lootSpawnSlot.probability,
                        OnlyWithLoadoutNamed = lootSpawnSlot.onlyWithLoadoutNamed
                    });

                    if (lootSpawns.ContainsKey(lootSpawnKey))
                    {
                        continue;
                    }

                    RecurseLootSpawn(lootSpawns, lootSpawnSlot.definition);
                }

                humanNpcs.Add(prefab, new JsonHumanNpc()
                {
                    LootSpawnSlots = lootSpawnSlots.ToArray()
                });

                npc.Kill();

            }

            return humanNpcs;
        }

        Dictionary<string, JsonLootContainer> GenerateContainerLootConfig(Dictionary<string, JsonLootSpawn> lootDefinitions)
        {
            Dictionary<string, JsonLootContainer> lootContainers = new Dictionary<string, JsonLootContainer>();
            List<string> prefabList = new List<string>() {
                "assets/bundled/prefabs/autospawn/resource/loot/loot-barrel-1.prefab",
                "assets/bundled/prefabs/autospawn/resource/loot/loot-barrel-2.prefab",
                "assets/bundled/prefabs/autospawn/resource/loot/trash-pile-1.prefab",
                "assets/bundled/prefabs/radtown/crate_basic.prefab",
                "assets/bundled/prefabs/radtown/crate_elite.prefab",
                "assets/bundled/prefabs/radtown/crate_mine.prefab",
                "assets/bundled/prefabs/radtown/crate_normal.prefab",
                "assets/bundled/prefabs/radtown/crate_normal_2.prefab",
                "assets/bundled/prefabs/radtown/crate_normal_2_food.prefab",
                "assets/bundled/prefabs/radtown/crate_normal_2_medical.prefab",
                "assets/bundled/prefabs/radtown/crate_tools.prefab",
                "assets/bundled/prefabs/radtown/crate_underwater_advanced.prefab",
                "assets/bundled/prefabs/radtown/crate_underwater_basic.prefab",
                "assets/bundled/prefabs/radtown/dmloot/dm ammo.prefab",
                "assets/bundled/prefabs/radtown/dmloot/dm c4.prefab",
                "assets/bundled/prefabs/radtown/dmloot/dm construction resources.prefab",
                "assets/bundled/prefabs/radtown/dmloot/dm construction tools.prefab",
                "assets/bundled/prefabs/radtown/dmloot/dm food.prefab",
                "assets/bundled/prefabs/radtown/dmloot/dm medical.prefab",
                "assets/bundled/prefabs/radtown/dmloot/dm res.prefab",
                "assets/bundled/prefabs/radtown/dmloot/dm tier1 lootbox.prefab",
                "assets/bundled/prefabs/radtown/dmloot/dm tier2 lootbox.prefab",
                "assets/bundled/prefabs/radtown/dmloot/dm tier3 lootbox.prefab",
                "assets/bundled/prefabs/radtown/foodbox.prefab",
                "assets/bundled/prefabs/radtown/loot_barrel_1.prefab",
                "assets/bundled/prefabs/radtown/loot_barrel_2.prefab",
                "assets/bundled/prefabs/radtown/loot_trash.prefab",
                "assets/bundled/prefabs/radtown/minecart.prefab",
                "assets/bundled/prefabs/radtown/oil_barrel.prefab",
                "assets/bundled/prefabs/radtown/underwater_labs/crate_ammunition.prefab",
                "assets/bundled/prefabs/radtown/underwater_labs/crate_elite.prefab",
                "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab",
                "assets/bundled/prefabs/radtown/underwater_labs/crate_food_2.prefab",
                "assets/bundled/prefabs/radtown/underwater_labs/crate_fuel.prefab",
                "assets/bundled/prefabs/radtown/underwater_labs/crate_medical.prefab",
                "assets/bundled/prefabs/radtown/underwater_labs/crate_normal.prefab",
                "assets/bundled/prefabs/radtown/underwater_labs/crate_normal_2.prefab",
                "assets/bundled/prefabs/radtown/underwater_labs/crate_tools.prefab",
                "assets/bundled/prefabs/radtown/underwater_labs/tech_parts_1.prefab",
                "assets/bundled/prefabs/radtown/underwater_labs/tech_parts_2.prefab",
                "assets/bundled/prefabs/radtown/underwater_labs/vehicle_parts.prefab",
                "assets/bundled/prefabs/radtown/vehicle_parts.prefab",
                "assets/content/props/roadsigns/roadsign1.prefab",
                "assets/content/props/roadsigns/roadsign2.prefab",
                "assets/content/props/roadsigns/roadsign3.prefab",
                "assets/content/props/roadsigns/roadsign4.prefab",
                "assets/content/props/roadsigns/roadsign5.prefab",
                "assets/content/props/roadsigns/roadsign6.prefab",
                "assets/content/props/roadsigns/roadsign7.prefab",
                "assets/content/props/roadsigns/roadsign8.prefab",
                "assets/content/props/roadsigns/roadsign9.prefab",
                "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab",
                "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate_oilrig.prefab",
                "assets/prefabs/misc/supply drop/supply_drop.prefab",
                "assets/prefabs/npc/m2bradley/bradley_crate.prefab",
                "assets/prefabs/npc/patrol helicopter/heli_crate.prefab"
            };

            foreach (var prefab in prefabList)
            {
                var lootContainer = GameManager.server.CreateEntity(prefab, new Vector3(0, 0, 0), Quaternion.identity) as LootContainer;

                if (lootContainer != null)
                {
                    lootContainer.Spawn();

                    var lootSpawnSlots = new List<JsonLootSpawnSlot>() { };

                    if (lootContainer.lootDefinition != null)
                    {
                        RecurseLootSpawn(lootDefinitions, lootContainer.lootDefinition);
                    }

                    foreach (var lootSpawnSlot in lootContainer.LootSpawnSlots)
                    {
                        var lootSpawnKey = lootSpawnSlot.definition?.ToString().Replace(" (LootSpawn)", "") ?? "";
                        if (lootSpawnKey == "")
                        {
                            continue;
                        }

                        lootSpawnSlots.Add(new JsonLootSpawnSlot()
                        {
                            Definition = lootSpawnKey,
                            NumberToSpawn = lootSpawnSlot.numberToSpawn,
                            Probability = lootSpawnSlot.probability
                        });

                        if (lootDefinitions.ContainsKey(lootSpawnKey))
                        {
                            continue;
                        }

                        RecurseLootSpawn(lootDefinitions, lootSpawnSlot.definition);
                    }

                    lootContainers.Add(prefab, new JsonLootContainer()
                    {
                        MinSecondsBetweenRefresh = lootContainer.minSecondsBetweenRefresh,
                        MaxSecondsBetweenRefresh = lootContainer.maxSecondsBetweenRefresh,
                        ScrapAmount = lootContainer.scrapAmount,
                        LootDefinition = lootContainer.lootDefinition?.ToString().Replace(" (LootSpawn)", "") ?? "",
                        MaxDefinitionsToSpawn = lootContainer.maxDefinitionsToSpawn,
                        LootSpawnSlots = lootSpawnSlots.ToArray(),
                        PanelName = lootContainer.panelName,
                        InventorySlots = lootContainer.inventorySlots,
                    });

                    lootContainer.Kill();
                }
            }
            return lootContainers;
        }


        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file with default values.");
            Dictionary<string, JsonLootSpawn> lootSpawns = new Dictionary<string, JsonLootSpawn>();
            LootConfig.HumanNpcs = GenerateNpcLootConfig(lootSpawns);
            LootConfig.LootContainers = GenerateContainerLootConfig(lootSpawns);
            LootConfig.LootSpawns = lootSpawns;
            SaveConfig(LootConfig);
        }

        void SaveConfig(ConfigData config) => Config.WriteObject(config, true);

        #endregion
    }
}
