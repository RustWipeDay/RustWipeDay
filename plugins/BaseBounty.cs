using ConVar;
using HarmonyLib;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Plugins;
using ProtoBuf;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using UnityEngine;
using static BuildingPrivlidge;
using static Carbon.Plugins.BaseBountyPerk;
using static UnityEngine.Rendering.PostProcessing.BloomRenderer;

namespace Carbon.Plugins
{

    // Extension method to find the driver
    public static class VehicleExtensions
    {
        public static BasePlayer GetDriver(this BaseVehicle vehicle)
        {
            return vehicle?.mountPoints[0]?.mountable?.GetMounted() as BasePlayer;
        }
    }

    [Info("Base Bounty", "TTV OdsScott", "1.0.0")]
    [Description("Base bounty with raid protection")]
    public class BaseBounty : CarbonPlugin
    {
        public override bool AutoPatch => true;

        [PluginReference]
        public readonly Plugin ImageLibrary, Clans, SimpleStatus, DogTags, RaidHooks, DiscordThreads, NightTime, Kits, CarSpawnSettings;
        public static BaseBounty Instance;

        public ToolcupboardTracker ToolcupboardTracker;
        public BaseBountyData BaseBountyData;


        public const float HoursOfProtection = 4f;
        private Dictionary<ulong, BaseEntity> PlayerHorses = new Dictionary<ulong, BaseEntity>();
        private Dictionary<ulong, BaseEntity> PlayerCars = new Dictionary<ulong, BaseEntity>();
        private Dictionary<ulong, BaseEntity> PlayerMinis = new Dictionary<ulong, BaseEntity>();
        private bool _debugMode = true;

        private void Init()
        {
            Instance = this;
            BaseBountyData = BaseBountyData.Load();
            LoadImages();
            CreateStatus();
        }

        public static double HoursSinceWipe {
            get {
                DateTime wipeTime = SaveRestore.SaveCreatedTime;
                TimeSpan timeSinceWipe = DateTime.UtcNow - wipeTime;
                return timeSinceWipe.TotalHours;
            } 
        }

        private void LoadImages()
        {
            if (ImageLibrary == null || !ImageLibrary.IsLoaded)
            {
                Debug("Image Library not found or unloaded.");
                return;
            }
            ImageLibrary.Call<bool>("AddImage", "https://rustwipeday.com/img/raidprotectionindicator.png", "SrpIndicatorIcon", 0UL);
            ImageLibrary.Call<bool>("AddImage", "https://rustwipeday.com/img/kits/dogtagneutral.png", "DogTagIcon", 0UL);
        }

        private void CreateStatus()
        {
            if (SimpleStatus == null || !SimpleStatus.IsLoaded)
            {
                Debug("Simple Status not found or unloaded.");
                return;
            }
            SimpleStatus.CallHook("CreateStatus", this, "raidprotection.status", Colors.COLOR_GREEN_DARK_LESS, "RAID PROTECTION", Colors.COLOR_GREEN, "0%", Colors.COLOR_GREEN, "SrpIndicatorIcon", Colors.COLOR_GREEN);
            SimpleStatus.CallHook("CreateStatus", this, "raidprotection.bounty", Colors.COLOR_GREY, "BASE BOUNTY", Colors.COLOR_BLACK, "0 Points", Colors.COLOR_BLACK, "itemid:1223900335", null);
        }

        private void OnServerInitialized()
        {
            ToolcupboardTracker = new ToolcupboardTracker(BaseBountyData);
            GameObject.FindObjectsOfType<BuildingPrivlidge>().ToList().ForEach((buildingPrivlidge) => ToolcupboardTracker.FindOrCreate(buildingPrivlidge));
            foreach (BasePlayer basePlayer in BasePlayer.activePlayerList)
            {
                ToolcupboardTracker.TrackBuildingPrivlidgeLoop(basePlayer);
            }
        }

        private void Unload()
        {
            OnServerSave();
            Instance = null;
        }

        // @TODO: Ideally I think we only want to gain base bounty when turning in white dog tags making it rare and hard to level.
       
        // void OnConsumedDogTag(BasePlayer player, Item activeItem) => BaseBountyData.AddBaseBountyPoint(player, 1);

        void OnAddBountyPoints(ulong playerId, int points, string type)
        {
            Puts(type);
            if (points <= 0 || type != "dogtagneutral")
            {
                return;
            }

            var player = BasePlayer.FindByID(playerId);

            if (player == null)
            {
                return;
            }

            BaseBountyData.AddBaseBountyPoint(player, points);
        }
        private bool IsNightTime()
        {
            // Utilize TimeOfDay plugin's TOD_Sky instance for accurate time checks.
            return TOD_Sky.Instance.Cycle.Hour < TOD_Sky.Instance.SunriseTime || TOD_Sky.Instance.Cycle.Hour > TOD_Sky.Instance.SunsetTime;
        }

        int OnHonorPointsAwarded(BasePlayer player, string victimName, bool isNpc, string victimRank, int points)
        {
            var highestHonorPerk = Instance.ToolcupboardTracker.GetHighestPerkLevel(player, BaseBountyPerk.BaseBountyPerkEnum.Honor);
            var newPoints = (int)(points * (1 + CalculatePercentage(highestHonorPerk, 100, 25) / 100));
            // Fix for night time double rates.
            if (IsNightTime()) { newPoints = newPoints * 2; }
            return newPoints;
        }

        /// <summary>
        ///  I did not like this
        /// </summary>
        /// <param name="basePlayer"></param>
        /// <param name="buildingPrivlidge"></param>
        /*void OnTakeBountyPoints(ulong playerId, int points)
        {
            var player = BasePlayer.FindByID(playerId);

            if (player == null)
            {
                return;
            }

            BaseBountyData.AddBaseBountyPoint(player, points);
        }*/

        private void OnLootEntity(BasePlayer basePlayer, BuildingPrivlidge buildingPrivlidge) => ToolcupboardTracker.OnOpenCupboard(basePlayer, buildingPrivlidge);

        private void OnLootEntityEnd(BasePlayer basePlayer, BuildingPrivlidge buildingPrivlidge) => ToolcupboardTracker.OnCloseCupboard(basePlayer, buildingPrivlidge);

        private void OnPlayerSleepEnded(BasePlayer basePlayer)
        {
            ToolcupboardTracker.TrackBuildingPrivlidgeLoop(basePlayer);
            var mainCupboard = ToolcupboardTracker.GetPlayerMainCupboard(basePlayer.userID);
            
            if (mainCupboard == null)
            {
                return;
            }
            
            mainCupboard.DepositPlayerHeldBounty(basePlayer);
        }

        object OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            if (entity.ToPlayer() == null)
            {
                return null;
            }

            var highestLevel = Instance.ToolcupboardTracker.GetHighestPerkLevel(entity.ToPlayer(), BaseBountyPerk.BaseBountyPerkEnum.Gathering);

            if (highestLevel == 0)
            {
                return null;
            }

            item.amount += (int)(item.amount * (0.01 * CalculatePercentage(highestLevel, 200, 30)));

            return null;
        }

        private void OnEntityKill(BuildingPrivlidge buildingPrivlidge, HitInfo hitInfo) => ToolcupboardTracker.FindOrCreate(buildingPrivlidge).Kill(hitInfo);

        private void OnEntitySpawned(BuildingPrivlidge buildingPrivlidge) => ToolcupboardTracker.FindOrCreate(buildingPrivlidge);

        private object OnCupboardAuthorize(BuildingPrivlidge buildingPrivlidge, BasePlayer player) => ToolcupboardTracker.AuthorizePlayer(buildingPrivlidge, player);


        private object OnCupboardDeauthorize(BuildingPrivlidge buildingPrivlidge, BasePlayer player) => ToolcupboardTracker.DeauthorizePlayer(buildingPrivlidge, player);

        private void OnServerSave() => BaseBountyData.Save();

        private object OnEntityTakeDamage(DecayEntity entity, HitInfo info) => ProtectFromDamage(entity, info);
        
        private object OnEntityTakeDamage(IOEntity entity, HitInfo info) => ProtectFromDamage(entity, info);
       
        private object OnEntityTakeDamage(BaseResourceExtractor entity, HitInfo info) => ProtectFromDamage(entity, info);

        private object ProtectFromDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity?.net == null || info == null)
            {
                return null;
            }

            BuildingPrivlidge buildingPrivlidge = entity.GetBuildingPrivilege();

            if (buildingPrivlidge == null) 
            {
                return null; 
            }

            Rust.DamageType majorityDamage = info.damageTypes.GetMajorityDamageType();
            if (majorityDamage == Rust.DamageType.Decay || majorityDamage == Rust.DamageType.ElectricShock) { return null; };

            BaseBountyCupboard baseBountyCupboard = ToolcupboardTracker.FindOrCreate(buildingPrivlidge);
            var protectionPercentage = baseBountyCupboard.ProtectionPercentage;
            if (protectionPercentage >= 100)
            {
                return true;
            }
            else
            {
                info.damageTypes.ScaleAll((float)(1 - (protectionPercentage / 100)));
            }

            return null;
        }

        object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            var player = target.player;
            
            List<DroppedItemContainer> droppedItemContainers = Facepunch.Pool.GetList<DroppedItemContainer>();

            var obb = target.player.WorldSpaceBounds();
            Vis.Entities(obb.position, 16f + obb.extents.magnitude, droppedItemContainers); 
            for (int i = 0; i < droppedItemContainers.Count; i++)
            {
                DroppedItemContainer droppedItemContainer = droppedItemContainers[i];
                if (ToolcupboardTracker.BaseBountyClaimContainer.ContainsKey(droppedItemContainer)) {
                    player.ChatMessage($"<color=#c45508>[RustWipeDay]</color>: You can not build in this area while a base bounty is still unclaimed.");
                    return false;
                }
            }

            Facepunch.Pool.FreeList(ref droppedItemContainers);

            return null;
        }

        object CanLootEntity(BasePlayer player, DroppedItemContainer droppedItemContainer) => ToolcupboardTracker.OnLootClaimContainer(player, droppedItemContainer);

        #region Fuel System Perk Handling

        /// <summary>
        /// Tracks active engine perks for vehicles by storing fuel storage IDs, driver IDs, and perk levels.
        /// </summary>
        private Dictionary<ulong, (ulong driverId, int perkLevel)> FuelStorageEnginePerks = new Dictionary<ulong, (ulong, int)>();

        #endregion

        #region Engine Event Handlers

        /// <summary>
        /// Called when a vehicle engine starts. Registers the driver and their highest fuel perk level.
        /// </summary>
        /// <param name="vehicle">The vehicle whose engine is starting.</param>
        /// <param name="driver">The player driving the vehicle.</param>
        /// <returns>Null to allow normal engine start behavior.</returns>
        private object OnEngineStart(BaseVehicle vehicle, BasePlayer driver)
        {
            if (vehicle == null || driver == null)
            {
                return null; // No valid vehicle or driver
            }

            var fuelSystem = vehicle.GetFuelSystem();
            if (fuelSystem == null || !ToolcupboardTracker.GetPlayerBaseBountyToolcupboards(driver.userID).Any())
            {
                return null; // No fuel system or no eligible cupboards for the driver
            }

            // Retrieve the highest fuel perk level for the driver
            var highestFuelLevel = ToolcupboardTracker.GetHighestPerkLevel(driver, BaseBountyPerk.BaseBountyPerkEnum.Fuel);
            var fuelStorageId = fuelSystem.GetInstanceID().Value;

            // Update the perks tracking dictionary
            FuelStorageEnginePerks[fuelStorageId] = (driver.userID, highestFuelLevel);
            return null;
        }

        /// <summary>
        /// Called when a vehicle engine stops. Cleans up any associated engine perks.
        /// </summary>
        /// <param name="vehicle">The vehicle whose engine is stopping.</param>
        /// <param name="driver">The player driving the vehicle.</param>
        /// <returns>Null to allow normal engine stop behavior.</returns>
        private object OnEngineStop(BaseVehicle vehicle, BasePlayer driver)
        {
            if (vehicle == null)
            {
                return null; // No valid vehicle
            }

            var fuelSystem = vehicle.GetFuelSystem();
            if (fuelSystem == null)
            {
                return null; // No fuel system
            }

            var fuelStorageId = fuelSystem.GetInstanceID().Value;
            FuelStorageEnginePerks.Remove(fuelStorageId); // Clean up perks on engine stop

            return null;
        }

        #endregion

        #region Fuel Efficiency Hook

        /// <summary>
        /// Modifies fuel usage based on the player's fuel perk level.
        /// </summary>
        /// <param name="fuelSystem">The fuel system of the vehicle.</param>
        /// <param name="fuelContainer">The container holding the vehicle's fuel.</param>
        /// <param name="seconds">The duration for which fuel is being consumed.</param>
        /// <param name="fuelUsedPerSecond">The base amount of fuel consumed per second.</param>
        /// <returns>Null to allow normal fuel consumption behavior, or 0 to prevent consumption.</returns>
        private object CanUseFuel(EntityFuelSystem fuelSystem, StorageContainer fuelContainer, float seconds, float fuelUsedPerSecond)
        {
            if (fuelSystem == null || fuelContainer == null)
            {
                return null; // Invalid fuel system or container
            }

            var fuelStorageId = fuelSystem.fuelStorageInstance.uid.Value;

            if (!FuelStorageEnginePerks.TryGetValue(fuelStorageId, out var perkData))
            {
                return null; // No perks registered for this fuel storage
            }

            var (driverId, fuelPerk) = perkData;

            // Apply the fuel perk bonus to reduce consumption
            var fuelBonus = (seconds * fuelUsedPerSecond) * (fuelPerk * 0.01f);
            fuelSystem.pendingFuel -= fuelBonus; // Reduce pending fuel consumption

            return null;
        }

        #endregion

        private (int baseXp, int baseXpRequired, int baseLevel) GetPlayerBaseLevel(ulong userId)
        {
            var cupboard = ToolcupboardTracker.GetPlayerMainCupboard(userId);
            try
            {
                return (cupboard.Bounty - cupboard.LevelXp, cupboard.NextLevelXp - cupboard.LevelXp, cupboard.Level);
            }
            catch (Exception e)
            {
                return (0, 0, 0);
            }
        }

        [ChatCommand("basebounty")]
        private void BaseBountyCommand(BasePlayer player, string command, string[] args)
        {
            var amount = BaseBountyData.GetBaseBountyPoints(player.userID);
            SendReply(
                player,
                $"<color=#c45508>[RustWipeDay]</color>: You currently have {amount} base bounty points."
            );
        }

        [ConsoleCommand("basebounty.claim")]
        private void Command_BaseBounty_Claim(IPlayer player, string command, string[] args)
        {
            var userId = ulong.Parse(player.Id);
            var basePlayer = BasePlayer.FindByID(userId);
           
            if (basePlayer == null || ToolcupboardTracker.GetPlayerMainCupboard(userId) == null)
            {
                return;
            }

            ulong privId = Convert.ToUInt64(args[0]);

            var baseBountyCupboard = ToolcupboardTracker.Find(privId);

            if (baseBountyCupboard == null )
            {
                return;
            }

            uint perkId = Convert.ToUInt32(args[1]);
            var perk = BaseBountyPerk.BaseBountyPerks[(int)perkId];


            if (baseBountyCupboard.Data.BaseBountyPerkLevel[perkId] + 1 > perk.MaxLevel)
            {
                return;
            }

            var cost = perk.Cost;


            if (!baseBountyCupboard.SpendBounty(cost))
            {
                basePlayer.ChatMessage($"<color=#c45508>[RustWipeDay]</color>: Not enough base bounty points to claim the {perk.Title} base perk.");
                return;
            }

            baseBountyCupboard.Data.BaseBountyPerkLevel[perkId]++;
            

            if (perkId == (int)BaseBountyPerk.BaseBountyPerkEnum.TurretLimit)
            {
                baseBountyCupboard.UpdateSentryInterference();
            }

            baseBountyCupboard.UpdateMapMarker();

            basePlayer.ChatMessage($"<color=#c45508>[RustWipeDay]</color>: You have claimed the {perk.Title} base perk for {cost} bounty points. Your base bounty has increased to {baseBountyCupboard.Bounty}.");

            Command_BaseBounty_Panel(player, command, new string[] { args[0] });
        }

        [ConsoleCommand("basebounty.panel.close")]
        private void Command_BaseBounty_Panel_Close(IPlayer player, string command, string[] args)
        {
            var basePlayer = BasePlayer.FindByID(ulong.Parse(player.Id));
            if (basePlayer == null)
            {
                return;
            }

            CuiHelper.DestroyUi(basePlayer, "BaseBountyPanel");
        }

        [Command("basebounty.panel")]
        private void Command_BaseBounty_Panel(IPlayer player, string command, string[] args)
        {
            var basePlayer = BasePlayer.FindByID(ulong.Parse(player.Id));
            if (basePlayer == null)
            {
                return;
            }

            var baseBountyCupboard = ToolcupboardTracker.GetPlayerMainCupboard(basePlayer.userID);

            if (baseBountyCupboard == null)
            {
                return;
            }

            basePlayer.EndLooting();
            basePlayer.ClientRPCPlayer(null, basePlayer, "OnDied");
            Gui.OpenBaseBountyPanel(basePlayer, baseBountyCupboard);

        }

        // This command spawns or claims a horse for the player.
        [ChatCommand("myhorse")]
        private void MyHorseCommand(BasePlayer player, string command, string[] args)
        {
            // Check for level requirements first
            var mainCupboard = ToolcupboardTracker.GetPlayerMainCupboard(player.userID);
            if (mainCupboard == null)
            {
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You do not have a main cupboard set. Set one using /basebounty.setmain.");
                return;
            }

            int animalPerkLevel = mainCupboard.Level;
            if (animalPerkLevel < 5)
            {
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You must have a base bounty level of at least 5 to use this command.");
                return;
            }

            var mountedEntity = player.GetMountedVehicle();
            // Check if the player is mounted on a horse
            if (mountedEntity is BaseMountable mountedHorse && mountedHorse.ShortPrefabName.Contains("horse"))
            {
                if (args.Length > 0 && args[0].ToLower() == "claim")
                {
                    // Claim the mounted horse
                    if (mountedHorse.OwnerID != 0 || PlayerHorses.ContainsValue(mountedHorse))
                    {
                        SendReply(player, "<color=#c45508>[RustWipeDay]</color>: This horse is already owned.");
                        return;
                    }

                    mountedHorse.OwnerID = player.userID; // Assign ownership
                    PlayerHorses[player.userID] = mountedHorse;
                    mountedHorse.gameObject.AddComponent<TeleportCooldown>().Initialize(180f); // Apply cooldown
                    SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You have claimed this horse.");
                    return;
                }
                else
                {
                    // Inform the player about /myhorse claim
                    SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You are currently on a horse. Use <color=#00ff00>/myhorse claim</color> to claim it if it's unowned.");
                    return;
                }
            }

            // Check for existing horse owned by the player
            if (PlayerHorses.TryGetValue(player.userID, out BaseEntity existingHorse) && existingHorse.IsValid())
            {
                // Teleport the horse to the player's location
                if (existingHorse.GetComponent<TeleportCooldown>() != null)
                {
                    SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You must wait before teleporting your horse again.");
                    return;
                }

                existingHorse.transform.position = FindSafeSpawnPosition(player.transform.position + player.transform.forward * 2f, 20f);
                existingHorse.SendNetworkUpdate();

                existingHorse.gameObject.AddComponent<TeleportCooldown>().Initialize(180f); // 3-minute cooldown
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: Your horse has been teleported to a safe location near you.");
                return;
            }

            // Find a safe spawn position for the horse
            Vector3 spawnPosition = FindSafeSpawnPosition(player.transform.position + player.transform.forward * 5f, 20f);
            if (spawnPosition == Vector3.zero)
            {
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: Unable to find a safe location to spawn your horse. Please try again.");
                return;
            }

            // Spawn the horse
            BaseEntity horse = GameManager.server.CreateEntity("assets/rust.ai/nextai/testridablehorse.prefab", spawnPosition, Quaternion.identity);
            if (horse == null)
            {
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: Failed to spawn the horse. Please try again.");
                return;
            }

            horse.Spawn();
            horse.OwnerID = player.userID;
            PlayerHorses[player.userID] = horse;

            SendReply(player, "<color=#c45508>[RustWipeDay]</color>: Your horse has been granted.");
        }


        // This command spawns or claims a modular car for the player.
        [ChatCommand("mycar")]
        private void MyCarCommand(BasePlayer player, string command, string[] args)
        {
            // Check for level requirements first
            var mainCupboard = ToolcupboardTracker.GetPlayerMainCupboard(player.userID);
            if (mainCupboard == null)
            {
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You do not have a main cupboard set. Set one using /basebounty.setmain.");
                return;
            }

            int vehiclePerkLevel = mainCupboard.Level;
            if (vehiclePerkLevel < 10)
            {
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You must have a base bounty level of at least 10 to use this command.");
                return;
            }
            var mountedVehicle = player.GetMountedVehicle();
            // Check if the player is mounted in a vehicle
            if (mountedVehicle)
            {
                if (args.Length > 0 && args[0].ToLower() == "claim")
                {
                    // Claim the mounted modular car
                    if (mountedVehicle.OwnerID != 0 || PlayerCars.ContainsValue(mountedVehicle))
                    {
                        SendReply(player, "<color=#c45508>[RustWipeDay]</color>: This vehicle is already owned.");
                        return;
                    }

                    if (IsFlyingVehicle(mountedVehicle))
                    {
                        SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You can only claim ground vehicles.");
                        return;
                    }

                    mountedVehicle.OwnerID = player.userID; // Assign ownership
                    PlayerCars[player.userID] = mountedVehicle;
                    mountedVehicle.gameObject.AddComponent<TeleportCooldown>().Initialize(180f); // Apply cooldown
                    SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You have claimed this vehicle.");
                    return;
                }
                else
                {
                    // Inform the player about /mycar claim
                    SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You are currently in a vehicle. Use <color=#00ff00>/mycar claim</color> to claim it if it's unowned.");
                    return;
                }
            }

            // Check for existing car owned by the player
            if (PlayerCars.TryGetValue(player.userID, out BaseEntity existingCar) && existingCar.IsValid())
            {
                // Teleport the car to the player's location
                if (existingCar.GetComponent<TeleportCooldown>() != null)
                {
                    SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You must wait before teleporting your vehicle again.");
                    return;
                }

                existingCar.transform.position = FindSafeSpawnPosition(player.transform.position + player.transform.forward * 2f, 20f);
                existingCar.SendNetworkUpdate();

                existingCar.gameObject.AddComponent<TeleportCooldown>().Initialize(180f); // 3-minute cooldown
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: Your vehicle has been teleported to a safe location near you.");
                return;
            }

            // Find a safe spawn position for the car
            Vector3 spawnPosition = FindSafeSpawnPosition(player.transform.position + player.transform.forward * 5f, 20f);
            if (spawnPosition == Vector3.zero)
            {
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: Unable to find a safe location to spawn your vehicle. Please try again.");
                return;
            }

            // Spawn the car
            ModularCar car = GameManager.server.CreateEntity("assets/content/vehicles/modularcar/4module_car_spawned.entity.prefab", spawnPosition, Quaternion.identity) as ModularCar;
            if (car == null)
            {
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: Failed to spawn the vehicle. Please try again.");
                return;
            }

            car.Spawn();
            CarSpawnSettings.Call("ProcessCar", car); // Equip, repair, and fuel the car
            car.OwnerID = player.userID;
            PlayerCars[player.userID] = car;

            SendReply(player, "<color=#c45508>[RustWipeDay]</color>: Your vehicle has been granted.");
        }

        // This command spawns a minicopter for the player.
        [ChatCommand("mymini")]
        private void MyMiniCommand(BasePlayer player, string command, string[] args)
        {
            // Check for level requirements first
            var mainCupboard = ToolcupboardTracker.GetPlayerMainCupboard(player.userID);
            if (mainCupboard == null)
            {
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You do not have a main cupboard set. Set one using /basebounty.setmain.");
                return;
            }

            int vehiclePerkLevel = mainCupboard.Level;
            if (vehiclePerkLevel < 20)
            {
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You must have a base bounty level of at least 20 to use this command.");
                return;
            }

            var mountedVehicle = player.GetMountedVehicle();
            // Check if the player is mounted in a vehicle
            if (mountedVehicle)
            {
                if (args.Length > 0 && args[0].ToLower() == "claim")
                {
                    // Claim the mounted flying vehicle
                    if (mountedVehicle.OwnerID != 0 || PlayerMinis.ContainsValue(mountedVehicle))
                    {
                        SendReply(player, "<color=#c45508>[RustWipeDay]</color>: This vehicle is already owned.");
                        return;
                    }

                    if (!IsFlyingVehicle(mountedVehicle))
                    {
                        SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You can only claim flying vehicles.");
                        return;
                    }

                    mountedVehicle.OwnerID = player.userID; // Assign ownership
                    PlayerMinis[player.userID] = mountedVehicle;
                    mountedVehicle.gameObject.AddComponent<TeleportCooldown>().Initialize(180f); // Apply cooldown
                    SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You have claimed this vehicle.");
                    return;
                }
                else
                {
                    // Inform the player about /mymini claim
                    SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You are currently in a vehicle. Use <color=#00ff00>/mymini claim</color> to claim it if it's unowned.");
                    return;
                }
            }

            // Check for existing minicopter owned by the player
            if (PlayerMinis.TryGetValue(player.userID, out BaseEntity existingMini) && existingMini.IsValid())
            {
                // Teleport the minicopter to the player's location
                if (existingMini.GetComponent<TeleportCooldown>() != null)
                {
                    SendReply(player, "<color=#c45508>[RustWipeDay]</color>: You must wait before teleporting your vehicle again.");
                    return;
                }

                existingMini.transform.position = FindSafeSpawnPosition(player.transform.position + player.transform.forward * 2f, 20f);
                existingMini.SendNetworkUpdate();

                existingMini.gameObject.AddComponent<TeleportCooldown>().Initialize(180f); // 3-minute cooldown
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: Your minicopter has been teleported to a safe location near you.");
                return;
            }

            // Find a safe spawn position for the minicopter
            Vector3 spawnPosition = FindSafeSpawnPosition(player.transform.position + player.transform.forward * 5f, 20f);
            if (spawnPosition == Vector3.zero)
            {
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: Unable to find a safe location to spawn your vehicle. Please try again.");
                return;
            }

            // Spawn the minicopter
            BaseEntity mini = GameManager.server.CreateEntity("assets/content/vehicles/minicopter/minicopter.entity.prefab", spawnPosition, Quaternion.identity);
            if (mini == null)
            {
                SendReply(player, "<color=#c45508>[RustWipeDay]</color>: Failed to spawn the minicopter. Please try again.");
                return;
            }

            mini.Spawn();
            mini.OwnerID = player.userID;
            PlayerMinis[player.userID] = mini;

            SendReply(player, "<color=#c45508>[RustWipeDay]</color>: Your minicopter has been granted.");
        }

        #region Helper Methods

        /// <summary>
        /// Finds a safe spawn position within a defined radius.
        /// Ensures the position is free of players, entities, and water.
        /// </summary>
        /// <param name="startPosition">The initial position to search around.</param>
        /// <param name="maxRadius">The maximum radius to search.</param>
        /// <returns>A safe position or Vector3.zero if none is found.</returns>
        private Vector3 FindSafeSpawnPosition(Vector3 startPosition, float maxRadius)
        {
            const int maxAttempts = 15;
            const float safeDistance = 5f;

            for (int i = 0; i < maxAttempts; i++)
            {
                Vector3 potentialPosition = startPosition + UnityEngine.Random.insideUnitSphere * maxRadius;
                potentialPosition.y = TerrainMeta.HeightMap.GetHeight(potentialPosition);

                if (IsPositionSafe(potentialPosition, safeDistance))
                {
                    return potentialPosition;
                }
            }

            return Vector3.zero; // No safe position found
        }

        /// <summary>
        /// Checks if a position is safe for spawning a car.
        /// Ensures no nearby players, entities, or water.
        /// </summary>
        /// <param name="position">The position to check.</param>
        /// <param name="safeDistance">The minimum safe distance from other entities or players.</param>
        /// <returns>True if the position is safe; otherwise, false.</returns>
        private bool IsPositionSafe(Vector3 position, float safeDistance)
        {
            // Ensure no players are nearby
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player.Distance(position) < safeDistance)
                {
                    return false;
                }
            }

            // Ensure no significant entities are nearby
            var entities = new List<BaseEntity>();
            Vis.Entities(position, safeDistance, entities, LayerMask.GetMask("Default", "Deployed", "Construction"));

            foreach (BaseEntity entity in entities)
            {
                if (entity != null && entity is BaseCombatEntity)
                {
                    return false;
                }
            }

            // Ensure the position is on solid ground and not in water
            return UnityEngine.Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, safeDistance, LayerMask.GetMask("Terrain")) &&
                   hit.collider != null;
        }


        /// <summary>
        /// Determines if a vehicle is a flying vehicle.
        /// </summary>
        /// <param name="vehicle">The vehicle to check.</param>
        /// <returns>True if the vehicle can fly; otherwise, false.</returns>
        private bool IsFlyingVehicle(BaseVehicle vehicle)
        {
            // Check for specific flying vehicle types (e.g., Minicopter, Scrap Transport Helicopter)
            return vehicle.PrefabName.Contains("minicopter") || vehicle.PrefabName.Contains("scraptransport");
        }

        #endregion

        private class TeleportCooldown : MonoBehaviour
        {
            private float cooldownTime;

            public void Initialize(float cooldown)
            {
                cooldownTime = UnityEngine.Time.time + cooldown;
                Invoke(nameof(RemoveCooldown), cooldown);
            }

            private void RemoveCooldown()
            {
                Destroy(this);
            }
        }


        [Command("basebounty.setmain")]
        private void Command_BaseBounty_SetMain(IPlayer player, string command, string[] args)
        {
            var playerId = ulong.Parse(player.Id);
            var basePlayer = BasePlayer.FindByID(ulong.Parse(player.Id));
            ulong privId = Convert.ToUInt64(args[0]);
            var baseBountyCupboard = ToolcupboardTracker.Find(privId);
            if (baseBountyCupboard == null)
            {
                return;
            }

            var hasMainCupboard = ToolcupboardTracker.GetPlayerMainCupboard(basePlayer.userID) != null;

            Debug($"Main cupboard: {hasMainCupboard}");

            if (hasMainCupboard)
            {
                return;
            }

            var protectedCupboard = ToolcupboardTracker.Find(privId);

            if (protectedCupboard == null || protectedCupboard.Data.IsRemoved)
            {
                return;
            }

            Debug($"Checking ownership {playerId}");

            if (protectedCupboard.Data.AllOwnerIds.Contains(playerId))
            {
                Debug($"Setting main");
                protectedCupboard.Data.IsMainCupboard = true;
                protectedCupboard.Data.PlayerRelations.ForEach((relationId) =>
                {
                    var relationPlayer = BasePlayer.FindByID(relationId);

                    if (relationPlayer == null)
                    {
                        return;
                    }
                    relationPlayer.ChatMessage($"<color=#c45508>[RustWipeDay]</color>: Your main cupboard has been set and raid protection has been enabled for this wipe.");
                    protectedCupboard.DepositPlayerHeldBounty(relationPlayer);
                });
                CuiHelper.DestroyUi(basePlayer, "RaidProtectionControl");
                Gui.CreateRaidProtectionButton(basePlayer, privId, true);
                return;
            }

            //basePlayer.EndLooting();
            //basePlayer.ClientRPCPlayer(null, basePlayer, "OnDied");
            //OpenBaseBountyPanel(basePlayer, Convert.ToUInt64(args[0]));
        }

        #region Harmony Patches

        [HarmonyPatch(typeof(VehicleEngineController<GroundVehicle>), "TickFuel")]
        public static class TickFuelPatch_GroundVehicle
        {
            // Prefix method to adjust fuel consumption before the original method executes
            static void Prefix(ref float fuelPerSecond, VehicleEngineController<GroundVehicle> __instance)
            {
                // Validate that the engine is associated with a vehicle
                if (__instance.owner is GroundVehicle vehicle)
                {
                    // Attempt to find the driver (BasePlayer) for the vehicle
                    BasePlayer driver = vehicle.GetDriver() as BasePlayer;
                    if (driver != null)
                    {
                        // Get the player's fuel perk level
                        int fuelPerkLevel = GetPlayerFuelPerkLevel(driver);
                        if (fuelPerkLevel > 0)
                        {
                            // Reduce fuel consumption by a percentage based on the perk level
                            float multiplier = 1f - (fuelPerkLevel * 0.01f); // Example: Each level reduces fuel by 1%
                            fuelPerSecond *= Mathf.Clamp(multiplier, 0.1f, 1f); // Clamp multiplier to prevent zero or negative fuel usage
                        }
                    }
                }
            }

            // Example method to retrieve the player's fuel perk level
            static int GetPlayerFuelPerkLevel(BasePlayer basePlayer)
            {
                // Replace with your logic to fetch the player's fuel perk level
                return Instance.ToolcupboardTracker.GetHighestPerkLevel(basePlayer, BaseBountyPerk.BaseBountyPerkEnum.Fuel);
                //return 30; // ToolcupboardTracker.GetPlayerBaseBountyPerkLevel(userID, );
            }
        }

        [HarmonyPatch(typeof(VehicleEngineController<PlayerHelicopter>), "TickFuel")]
        public static class TickFuelPatch_PlayerHelicopter
        {
            // Prefix method to adjust fuel consumption before the original method executes
            static void Prefix(ref float fuelPerSecond, VehicleEngineController<PlayerHelicopter> __instance)
            {
                // Validate that the engine is associated with a vehicle
                if (__instance.owner is PlayerHelicopter vehicle)
                {
                   
                    // Attempt to find the driver (BasePlayer) for the vehicle
                    BasePlayer driver = vehicle.GetDriver();
                    if (driver != null)
                    {
                        
                        // Get the player's fuel perk level
                        int fuelPerkLevel = GetPlayerFuelPerkLevel(driver);
                        if (fuelPerkLevel > 0)
                        {
                            // Reduce fuel consumption by a percentage based on the perk level
                            float multiplier = 1f - (fuelPerkLevel * 0.01f); // Example: Each level reduces fuel by 1%
                            fuelPerSecond *= Mathf.Clamp(multiplier, 0.1f, 1f); // Clamp multiplier to prevent zero or negative fuel usage
                        }
                    }
                }
            }

            // Example method to retrieve the player's fuel perk level
            static int GetPlayerFuelPerkLevel(BasePlayer basePlayer)
            {
                // Replace with your logic to fetch the player's fuel perk level
                return Instance.ToolcupboardTracker.GetHighestPerkLevel(basePlayer, BaseBountyPerk.BaseBountyPerkEnum.Fuel);
            }
        }

        [HarmonyPatch(typeof(BuildingPrivlidge), "CalculateBuildingTaxRate", new Type[] { })]
        public class BuildingPrivlidge_CalculateBuildingTaxRate
        {
            private static bool Prefix(BuildingPrivlidge __instance, ref float __result)
            {
                BuildingManager.Building building = __instance.GetBuilding();
                if (building == null)
                {
                    __result = Instance.GetBuildingPriviledgeUpkeepRate(__instance, ConVar.Decay.bracket_0_costfraction);
                    return false;
                }
                if (!building.HasBuildingBlocks())
                {
                    __result = Instance.GetBuildingPriviledgeUpkeepRate(__instance, ConVar.Decay.bracket_0_costfraction);
                    return false;
                }
                int count = building.buildingBlocks.Count;
                int num = count;
                for (int i = 0; i < upkeepBrackets.Length; i++)
                {
                    UpkeepBracket upkeepBracket = upkeepBrackets[i];
                    upkeepBracket.blocksTaxPaid = 0f;
                    if (num > 0)
                    {
                        int num2 = 0;
                        num2 = ((i != upkeepBrackets.Length - 1) ? Mathf.Min(num, upkeepBrackets[i].objectsUpTo) : num);
                        num -= num2;
                        upkeepBracket.blocksTaxPaid = (float)num2 * upkeepBracket.fraction;
                    }
                }
                float num3 = 0f;
                for (int j = 0; j < upkeepBrackets.Length; j++)
                {
                    UpkeepBracket upkeepBracket2 = upkeepBrackets[j];
                    if (!(upkeepBracket2.blocksTaxPaid > 0f))
                    {
                        break;
                    }
                    num3 += upkeepBracket2.blocksTaxPaid;
                }
                __result = Instance.GetBuildingPriviledgeUpkeepRate(__instance, num3 / (float)count);

                return false;
            }
        }

        /* [HarmonyPatch(typeof(BasePlayer), "SendMarkersToClient", new Type[] { })]
        public class BasePlayer_SendMarkersToClient
        {
            private static bool Prefix(BasePlayer __instance)
            {
                MapNoteList mapNoteList = Facepunch.Pool.Get<MapNoteList>();
                mapNoteList.notes = Facepunch.Pool.GetList<MapNote>();
                if (__instance.ServerCurrentDeathNote != null)
                {
                    mapNoteList.notes.Add(__instance.ServerCurrentDeathNote);
                }
                if (__instance.State.pointsOfInterest != null)
                {
                    mapNoteList.notes.AddRange(__instance.State.pointsOfInterest);
                }

                mapNoteList.notes.AddRange(Instance.ToolcupboardTracker.BaseBounties.Values.Where(tc => tc.Bounty >= 150 && tc.MapNote != null).OrderByDescending(tc => tc.Bounty).Select(tc => tc.MapNote).Take(3));
                __instance.ClientRPCPlayer(null, __instance, "Client_ReceiveMarkers", mapNoteList);
                mapNoteList.notes.Clear();
                return false;
            }
        } */

        [HarmonyPatch(typeof(AutoTurret), "UpdateInterference", new Type[] { })]
        public class AutoTurret_UpdateInterference
        {
            private static bool Prefix(AutoTurret __instance)
            {
                if (!__instance.IsOn())
                {
                    return false;
                }
                float num = 0f;
                foreach (AutoTurret nearbyTurret in __instance.nearbyTurrets)
                {
                    if (!nearbyTurret.isClient && nearbyTurret.IsValid() && nearbyTurret.gameObject.activeSelf && !nearbyTurret.EqualNetID(__instance.net.ID) && nearbyTurret.IsOn() && !nearbyTurret.HasInterference())
                    {
                        num += 1f;
                    }
                }
                __instance.SetFlag(BaseEntity.Flags.OnFire, num >= Instance.GetSentryInterference(__instance));
                return false;
            }
        }

        [HarmonyPatch(typeof(ItemCrafter), "ServerUpdate")]
        public class ItemCrafter_ServerUpdate
        {
            private static bool Prefix(ItemCrafter __instance, float delta)
            {
                var player = __instance.owner;

                var highestCraftingLevel = Instance.ToolcupboardTracker.GetHighestPerkLevel(player, BaseBountyPerk.BaseBountyPerkEnum.Crafting);

                if (highestCraftingLevel == 0)
                {
                    return true;
                }

                if (__instance.queue.Count == 0)
                {
                    return false;
                }

                ItemCraftTask value = __instance.queue.First.Value;
                if (value.cancelled)
                {
                    __instance.owner.Command("note.craft_done", value.taskUID, 0);
                    __instance.queue.RemoveFirst();
                    return false;
                }
                float currentCraftLevel = __instance.owner.currentCraftLevel;
                if (value.endTime > UnityEngine.Time.realtimeSinceStartup)
                {
                    return false;
                }
                if (value.endTime == 0f)
                {
                    float scaledDuration = Math.Max(ItemCrafter.GetScaledDuration(value.blueprint, currentCraftLevel, __instance.owner.IsInTutorial) * (float)(1 - (0.02 * highestCraftingLevel)), 0f);
                    value.endTime = UnityEngine.Time.realtimeSinceStartup + scaledDuration;
                    value.workbenchEntity = __instance.owner.GetCachedCraftLevelWorkbench();
                    if (__instance.owner != null)
                    {
                        __instance.owner.Command("note.craft_start", value.taskUID, scaledDuration, value.amount);
                        if (__instance.owner.IsAdmin && Craft.instant)
                        {
                            value.endTime = UnityEngine.Time.realtimeSinceStartup + 1f;
                        }
                    }
                }
                else
                {
                    __instance.FinishCrafting(value);
                    if (value.amount <= 0)
                    {
                        __instance.queue.RemoveFirst();
                    }
                    else
                    {
                        value.endTime = 0f;
                    }
                }
                
                return false;
            }
        }

        private float GetBuildingPriviledgeUpkeepRate(BuildingPrivlidge buildingPrivlidge, float cost)
        {
            if (buildingPrivlidge == null)
            {
                return cost;
            }
            BaseBountyCupboard baseBountyCupboard = ToolcupboardTracker.FindOrCreate(buildingPrivlidge);

            var level = baseBountyCupboard.Level;

            return cost * (float)(1 - level * 0.02);
        }

        private float GetSentryInterference(AutoTurret autoTurret)
        {

            var buildingPrivlidge = autoTurret.GetNearestBuildingPrivledge();
            var sentryInterference = Sentry.maxinterference;
            if (buildingPrivlidge == null)
            {
                return sentryInterference;
            }

            BaseBountyCupboard baseBountyCupboard = ToolcupboardTracker.FindOrCreate(buildingPrivlidge);

            var level = baseBountyCupboard.Level;
            var limit = CalculateTurretLimit(level);

            return limit + sentryInterference;
        }
        #endregion

        public void Debug(string message)
        {
            if (_debugMode)
            {
                Puts(message);
            }
        }
    }

    public class BaseBountyPerk
    {

        public static readonly List<BaseBountyPerk> BaseBountyPerks = new List<BaseBountyPerk>
        {
            new BaseBountyPerk("gathering rates", 10, 25, level => $"Increased gathering rates by {CalculatePercentage(level, 200, 30):0.0}%."),
            new BaseBountyPerk("honor gains", 1, 15, level => $"Increased honor gained by {CalculatePercentage(level, 100, 30):0.0}%."),
            new BaseBountyPerk("turret limit", 25, 12, level => $"Increased the main base turret limit by {CalculateTurretLimit(level)}.\nMax ({12+CalculateTurretLimit(level):0.0})"),
            new BaseBountyPerk("upkeep costs", 15, 30, level => $"Base upkeep costs are decreased by {CalculatePercentage(level, 75, 30):0.0}%."),
            new BaseBountyPerk("crafting time", 15, 50, level => $"The crafting time of items is decreased by {CalculatePercentage(level, 100, 30):0.0}%."),
            new BaseBountyPerk("fuel consumption", 5, 30, level => $"Your low-grade fuel consumption is decreased by {CalculatePercentage(level, 100, 30):0.0}%."),
            new BaseBountyPerk("raid alarm", 50, 1, level => $"Rust+ and Discord raid alerts are {GetRaidAlarmText(level)}."),
            new BaseBountyPerk("vehicle", 50, 10, level => GetVehicleUnlockText(level)),
            new BaseBountyPerk("base protection", 25, 50, level => $"Increased online base protection by {CalculateBaseProtection(level):0.0}% and offline base protection by {CalculateOfflineProtection(level):0.0}%.")
        };

        public static string GetRaidAlarmText(int level)
        {
            return level >= 25 ? "enabled" : "disabled";
        }

        public static string GetVehicleUnlockText(int level)
        {
            if (level >= 20)
                return "You have unlocked /mymini. Type /mymini or /mycar or /myhorse.";
            if (level >= 10)
                return "You have unlocked /mycar. Next unlock at level 20. Type /mycar or /myhorse";
            if (level >= 5)
                return "You have unlocked /myhorse. Next unlock at level 10.";
            return "You currently have no vehicles unlocked.\nNext unlock at level 5.";
        }

        public static double CalculatePercentage(int level, int maxLevel, int capLevel)
        {
            return (double)Math.Min(level, capLevel) / capLevel * maxLevel;
        }

        public static int CalculateTurretLimit(int level)
        {
            return Math.Min(level / 3, 12);
        }

        public static double CalculateBaseProtection(int level)
        {
            if (level < 25) return 0;
            return CalculatePercentage(level - 24, 15, 25); // Online protection starts from level 25
        }

        public static double CalculateOfflineProtection(int level)
        {
            return CalculatePercentage(level, 100, 25); // Offline protection caps at level 25
        }

        public enum BaseBountyPerkEnum
        {
            Gathering = 0,
            Honor = 1,
            TurretLimit = 2,
            Upkeep = 3,
            Crafting = 4,
            Fuel = 5,
            Alerts = 6,
            Vehicle = 7,
            Protection = 8
        }

        public string Title { get; set; }
        public int Cost { get; set; }
        public int MaxLevel { get; set; }
        public Func<int, string> Description { get; set; }

        // Constructor to initialize the BaseBountyPerk object
        public BaseBountyPerk(string title, int cost, int maxLevel, Func<int, string> description)
        {
            Title = title;
            Cost = cost;
            MaxLevel = maxLevel;
            Description = description;
        }
    }


    public class BaseBountyCupboardData
    {
        public bool IsRemoved { get; set; }
        public int AvailableBounty { get; set; }
        public int Bounty { get; set; }
        public int[] BaseBountyPerkLevel { get; set; }
        public HashSet<ulong> AllOwnerIds { get; set; }
        public bool IsMainCupboard { get; set; }
        public bool IsActive { get; set; }
        public int AccessedCount { get; set; }
        public string RaidAlertChannelId { get; set; }
        public double PlayerBountyPayout { get; set; }
        public ulong NetId { get; set; }
        public ulong OwnerId { get; set; }
        public List<ulong> PlayerRelations = new List<ulong> { };

        public BaseBountyCupboardData()
        {

        }

        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            if (BaseBountyPerkLevel == null)
            {
                BaseBountyPerkLevel = new int[BaseBountyPerk.BaseBountyPerks.Count];
            }
        }


        public static int[] CalculateXpDistribution(int levels, int totalXp)
        {
            // Define the levels
            int[] levelArray = Enumerable.Range(1, levels).ToArray();

            // Initial guess for the parameters
            double a = 1;
            double b = 0.1;

            // Define the exponential function
            double[] xpPerLevel = levelArray.Select(n => a * Math.Exp(b * n)).ToArray();

            // Scale the function so that the total XP is equal to totalXp
            double scaleFactor = totalXp / xpPerLevel.Sum();
            xpPerLevel = xpPerLevel.Select(xp => xp * scaleFactor).ToArray();

            // Convert to whole numbers and adjust to ensure the total sum is total_xp
            int[] xpPerLevelInt = xpPerLevel.Select(xp => (int)Math.Round(xp)).ToArray();

            // Adjust the last level to make sure the total is exactly totalXp
            int xpDifference = totalXp - xpPerLevelInt.Sum();
            xpPerLevelInt[levels - 1] += xpDifference;

            return xpPerLevelInt;
        }
    }

    public class BaseBountyCupboard
    {
        public BaseBountyCupboardData Data { get; set; }
        public MapNote MapNote;
        public BuildingPrivlidge BuildingPrivlidge { get; set; }
        public HashSet<BasePlayer> PlayersViewing { get; set; }
        public bool CachedPlayerRelations = false;

        public int BountyClaim { get => (int)(Bounty * 0.75); }
        public int Bounty { get => Data.AvailableBounty + Data.Bounty; }
        public int LevelXp
        {
            get
            {
                int cumulativeXP = 0;

                for (int level = 0; level < Level; level++)
                {
                    cumulativeXP += XpRequirement[level];
                }

                // If bounty exceeds the total XP for all levels, return max level
                return cumulativeXP;
            }
        }
        public int NextLevelXp
        {
            get
            {
                int cumulativeXP = 0;

                for (int level = 0; level < Level + 1; level++)
                {
                    cumulativeXP += XpRequirement[level];
                }

                // If bounty exceeds the total XP for all levels, return max level
                return cumulativeXP;
            }
        }


        public static int[] XpRequirement = BaseBountyCupboardData.CalculateXpDistribution(50, 6350);
        public int Level
        {
            get
            {
                int cumulativeXP = 0;

                for (int level = 0; level < XpRequirement.Length; level++)
                {
                    cumulativeXP += XpRequirement[level];
                    if (Bounty < cumulativeXP)
                    {
                        return level;
                    }
                }

                // If bounty exceeds the total XP for all levels, return max level
                return XpRequirement.Length;
            }
        }


        public BaseBountyCupboard(BuildingPrivlidge buildingPrivlidge)
        {
            BuildingPrivlidge = buildingPrivlidge;
            PlayersViewing = new HashSet<BasePlayer>() { };

            if (!BaseBounty.Instance.BaseBountyData.BaseBountyCupboardData.TryGetValue(buildingPrivlidge.net.ID.Value, out var data))
            {
                Data = BaseBounty.Instance.BaseBountyData.AddBaseBountyCupboardData(buildingPrivlidge);
                Data.IsActive = true;
                Data.IsRemoved = false;
                Data.OwnerId = buildingPrivlidge.OwnerID;
                Data.NetId = buildingPrivlidge.net.ID.Value;
                Data.AvailableBounty = 0;
                Data.Bounty = 0;
                Data.BaseBountyPerkLevel = new int[BaseBountyPerk.BaseBountyPerks.Count];
                Data.AccessedCount = 0;
            }
            else
            {
                Data = data;
            }

            // Owner should deposit his bounty points, but what about team mates?

            UpdatePlayerRelationships();
            UpdateMapMarker();
        }

        public object Kill(HitInfo hitInfo)
        {
            BaseBounty.Instance.Debug($"{BuildingPrivlidge} has been killed by {hitInfo}");

            var killer = hitInfo?.InitiatorPlayer;

            if (BuildingPrivlidge == null)
            {
                return null;
            }

            var payout = (int)Math.Floor(Data.PlayerBountyPayout);

            if (killer != null && !Data.PlayerRelations.Contains(killer.userID) && Bounty > 0)
            {
                BaseBounty.Instance.BaseBountyData.AddBaseBountyPoint(killer, BountyClaim);
                Data.AvailableBounty = 0;
                Data.Bounty = 0;
            }

            if (payout <= 0)
            {
                BuildingPrivlidge.inventory.capacity = 36;
                BuildingPrivlidge.panelName = "generic_resizable";
                BuildingPrivlidge.inventory.AddItem(ItemManager.FindItemDefinition("bluedogtags"), payout);
                Data.PlayerBountyPayout = 0;
            }

            if (Bounty > 0)
            {
                BaseBounty.Instance.ToolcupboardTracker.CreateClaim(this);
            }

            Data.IsRemoved = true;

            if (MapNote != null)
            {
                MapNote.Dispose();
                MapNote = null;
            }

            foreach(var player in PlayersViewing)
            {
                if (player != null && player.IsValid())
                {
                    Gui.OnCloseCupboard(player, BuildingPrivlidge);
                }
            }

            return null;
        }

        public float ProtectionPercentage
        {
            get
            {
                var hoursSinceWipe = BaseBounty.HoursSinceWipe;
                if (hoursSinceWipe >= BaseBounty.HoursOfProtection)
                {
                    return 0f;
                }

                if (!Data.IsMainCupboard || !Data.IsActive)
                {
                    return 0f;
                }

                if (hoursSinceWipe < 2)
                {
                    return 100;
                }

                double decayPercent = 100 * (1 - Math.Log(1 + hoursSinceWipe) / Math.Log(1 + BaseBounty.HoursOfProtection));
                decayPercent = Math.Max(decayPercent, 0);

                return (float)Math.Round(decayPercent, 2);
            }

        }
        public void UpdatePlayerRelationships()
        {
            HashSet<ulong> allOwnerIds = (from id in BuildingPrivlidge.authorizedPlayers select id.userid).Union(new List<ulong> { BuildingPrivlidge.OwnerID }).ToHashSet();

            Data.AllOwnerIds = allOwnerIds;

            List<ulong> relatedPlayers = Data.AllOwnerIds.ToList();

            foreach (var owner in Data.AllOwnerIds)
            {
                List<ulong> clanMembers = BaseBounty.Instance.Clans.Call<List<string>>("GetClanMembers", owner).Select(clanMember => Convert.ToUInt64(clanMember)).ToList();
                foreach (var clanMember in clanMembers)
                {
                    if (!relatedPlayers.Contains(clanMember))
                    {
                        relatedPlayers.Add(clanMember);
                    }
                }
                BasePlayer ownerPlayer = BasePlayer.FindByID(owner);
                if (ownerPlayer)
                {
                    var team = ownerPlayer.Team;
                    if (team != null && team.members != null)
                    {
                        foreach (var teamMember in team.members)
                        {
                            if (!relatedPlayers.Contains(teamMember))
                            {
                                relatedPlayers.Add(teamMember);
                            }
                        }
                    }
                }
            }

            Data.PlayerRelations = Data.PlayerRelations.Union(relatedPlayers).ToList();

            return;
        }

        public void UpdateStatus()
        {
            BaseBounty.Instance.Debug($"TC with ID={BuildingPrivlidge.net.ID} Active={Data.IsMainCupboard}");
        }

        public void UpdateSentryInterference()
        {
            List<BaseEntity> nearby = new List<BaseEntity>();
            Vis.Entities(BuildingPrivlidge.transform.position, Sentry.interferenceradius, nearby, LayerMask.GetMask("Deployed"), QueryTriggerInteraction.Ignore);

            foreach (var ent in nearby.Distinct().ToList())
            {
                if (!(ent is AutoTurret))
                {
                    continue;
                }

                (ent as AutoTurret).TryRegisterForInterferenceUpdate();
            }
        }

        public void UpdateMapMarker()
        {
            if (Bounty <= 0 || Data.IsRemoved)
            {
                if (MapNote != null)
                {
                    MapNote.Dispose();
                    MapNote = null;
                }
                return;
            }

            if (MapNote != null)
            {
                MapNote.label = $"{Bounty}";
            }
            else
            {
                MapNote = new MapNote()
                {
                    noteType = 1,
                    label = $"{Bounty}",
                    worldPosition = BuildingPrivlidge.transform.position,
                    isPing = true,
                    associatedId = BuildingPrivlidge.net.ID
                };
                BasePlayer.PingStyle pingStyle = BasePlayer.GetPingStyle(BasePlayer.PingType.Dollar);
                MapNote.colourIndex = 3;
                MapNote.icon = pingStyle.IconIndex;
            }
        }

        public void DepositPlayerHeldBounty(BasePlayer player)
        {
            var amount = BaseBounty.Instance.BaseBountyData.GetBaseBountyPoints(player.userID);
            if (amount <= 0)
            {
                return;
            }
            Data.AvailableBounty += amount;
            BaseBounty.Instance.BaseBountyData.SpendBountyPoints(player, amount);
            player.ChatMessage($"<color=#c45508>[RustWipeDay]</color>: You have deposited {amount} base bounty points into your main cupboard.");
            UpdateMapMarker();
        }

        public bool SpendBounty(int amount)
        {
            if (Data.AvailableBounty >= amount)
            {
                Data.AvailableBounty -= amount;
                Data.Bounty += amount;
                return true;
            }

            UpdateMapMarker();
            return false;
        }

        public static void Unload()
        {
        }
    }

    public class ToolcupboardTracker
    {
        protected BaseBountyData BaseBountyData;
        public Dictionary<ulong, BaseBountyCupboard> BaseBounties = new Dictionary<ulong, BaseBountyCupboard>();
        public Dictionary<DroppedItemContainer, BaseBountyCupboard> BaseBountyClaimContainer = new Dictionary<DroppedItemContainer, BaseBountyCupboard>();
        public ToolcupboardTracker(BaseBountyData baseBountyData)
        {
            BaseBountyData = baseBountyData;

            // StartMapMarkerUpdates();
            StartPlayerBountyGenerator();
            DepositBaseBountyAtEndOfProtection();
            StartRankingSystem();
        }

        private void DepositBaseBountyAtEndOfProtection()
        {
            Debug("Starting base bounty deposit checker.");
            BaseBounty.Instance.timer.In(10f, () =>
            {
                if (BaseBounty.HoursSinceWipe <= BaseBounty.HoursOfProtection)
                {
                    DepositBaseBountyAtEndOfProtection();
                    return;
                }

                foreach (BasePlayer basePlayer in BasePlayer.allPlayerList.ToList())
                {
                    var mainCupboard = GetPlayerMainCupboard(basePlayer.userID);

                    if (mainCupboard == null)
                    {
                        continue;
                    }

                    mainCupboard.DepositPlayerHeldBounty(basePlayer);
                }
                Debug("Deposited base bounty at end of protection");
            });
        }

        private void StartRankingSystem()
        {
            BaseBounty.Instance.Debug($"Starting base bounty ranking system.");
            BaseBounty.Instance.timer.Every(60f, () =>
            {
            });
        }

        private void StartMapMarkerUpdates()
        {
            BaseBounty.Instance.Debug($"Starting map marker update loop.");
            BaseBounty.Instance.timer.Every(10f, () =>
            {
                // Sending map notes
                foreach (var player in BasePlayer.activePlayerList)
                {
                    player.SendMarkersToClient();
                }
            });
        }

        private void StartPlayerBountyGenerator()
        {
            BaseBounty.Instance.Debug("Starting player bounty point generator loop.");

            var TICK_INTERVAL = 3600f; // Time interval for each interest application in seconds (1 hour)
            var TICK_RATE = 1f; // Function triggers every second

            // Adjust the payout calculation to reflect how it accumulates per second
            var INTERVAL_ADJUSTMENT = TICK_RATE / TICK_INTERVAL;

            BaseBounty.Instance.timer.Every(TICK_RATE, () =>
            {
                foreach (BaseBountyCupboard pc in BaseBounties.Values)
                {
                    if (pc.Bounty <= 0 || pc.Data.IsRemoved)
                    {
                        continue;
                    }

                    double payoutIncrement = CalculatePayout(pc.Bounty) * INTERVAL_ADJUSTMENT;

                    pc.Data.PlayerBountyPayout += payoutIncrement;

                    var roundedPayout = (int)Math.Floor(pc.Data.PlayerBountyPayout);

                    if (roundedPayout <= 0 || pc.BuildingPrivlidge.inventory.itemList.Count >= 24)
                    {
                        continue;
                    }

                    var bluedogtags = ItemManager.CreateByItemID(
                        ItemManager.FindItemDefinition("bluedogtags").itemid,
                        roundedPayout
                    );
                    pc.Data.PlayerBountyPayout -= roundedPayout;  // Adjusting for the actual payout processed
                    bluedogtags.MoveToContainer(pc.BuildingPrivlidge.inventory);
                    pc.BuildingPrivlidge.SendNetworkUpdate();
                }
            });
        }

        private double CalculatePayout(double bounty)
        {
            double payout = 0;

            // Calculate 2% for the first 1000
            if (bounty > 1000)
            {
                payout += 1000 * 0.02;
            }
            else
            {
                payout += bounty * 0.02;
                return payout;
            }

            // Calculate 1% for the next 1000
            if (bounty > 2000)
            {
                payout += 1000 * 0.01;
            }
            else
            {
                payout += (bounty - 1000) * 0.01;
                return payout;
            }

            // Calculate scaled interest from 2000 to 10000
            if (bounty > 2000)
            {
                double remainingBounty = Math.Min(bounty, 10000) - 2000;
                // Using a logarithmic decrease formula to reduce the interest rate from 1% to 0%
                double interestRate = 0.01 * (1 - Math.Log10(remainingBounty + 100) / Math.Log10(8000));
                payout += remainingBounty * Math.Max(0, interestRate);
            }

            return payout;
        }


        public BaseBountyCupboard FindOrCreate(BuildingPrivlidge buildingPrivlidge)
        {
            if (buildingPrivlidge == null || !buildingPrivlidge.IsValid())
            {
                Debug("Requested null or invalid building privlidge");
                return null;
            }

            if (!BaseBounties.ContainsKey(buildingPrivlidge.net.ID.Value))
            {
                BaseBounties.Add(buildingPrivlidge.net.ID.Value, new BaseBountyCupboard(buildingPrivlidge));
                Debug($"Added TC with ID={buildingPrivlidge.net.ID}");
                Debug($"There are {BaseBounties.Count} TCs being tracked.");
                return BaseBounties[buildingPrivlidge.net.ID.Value];
            }
            else
            {
                return BaseBounties[buildingPrivlidge.net.ID.Value];
            }
        }

        public BaseBountyCupboard Find(ulong buildingPrivlidgeId)
        {
            if (!BaseBounties.ContainsKey(buildingPrivlidgeId))
            {
                return null;
            }
            else
            {
                return BaseBounties[buildingPrivlidgeId];
            }
        }

        public void RemoveCupboard(BuildingPrivlidge buildingPrivlidge)
        {
            if (buildingPrivlidge == null)
            {
                return;
            }

            if (BaseBounties.ContainsKey(buildingPrivlidge.net.ID.Value))
            {
                var baseBounty = BaseBounties[buildingPrivlidge.net.ID.Value];
                baseBounty.Data.IsRemoved = true;
            }
        }

        public object AuthorizePlayer(BuildingPrivlidge buildingPrivlidge, BasePlayer basePlayer) => AuthorizePlayer(buildingPrivlidge, basePlayer.userID);

        public object AuthorizePlayer(BuildingPrivlidge buildingPrivlidge, ulong userId)
        {
            if (!BaseBountyData.PlayerBuildingPrivlidges.ContainsKey(userId))
            {
                BaseBountyData.PlayerBuildingPrivlidges.Add(userId, new HashSet<ulong>());
            }

            Debug($"{userId} has authed from {buildingPrivlidge}");
            BaseBountyData.PlayerBuildingPrivlidges[userId].Add(buildingPrivlidge.net.ID.Value);

            var baseBountyCupboard = FindOrCreate(buildingPrivlidge);
            // baseBountyCupboard.UpdatePlayerRelationships();

            var mainCupboard = ToolcupboardTracker.GetPlayerMainCupboard(player.userID, true);

            if (mainCupboard == null)
            {
                var newCupboard = FindOrCreate(buildingPrivlidge);
                newCupboard.Data.IsMainCupboard = true;
                Gui.CreateRaidProtectionButton(player, buildingPrivlidge.net.ID.Value, true);
                player.ChatMessage("<color=#c45508>[RustWipeDay]</color>: Base Bounty enabled for your first TC.");
            }

            return null;
        }

        public object DeauthorizePlayer(BuildingPrivlidge buildingPrivlidge, BasePlayer basePlayer) => DeauthorizePlayer(buildingPrivlidge, basePlayer.userID);

        public object DeauthorizePlayer(BuildingPrivlidge buildingPrivlidge, ulong userId)
        {
            if (BaseBountyData.PlayerBuildingPrivlidges.ContainsKey(userId))
            {
                Debug($"{userId} has deauthed from {buildingPrivlidge}");
                BaseBountyData.PlayerBuildingPrivlidges[userId].Remove(buildingPrivlidge.net.ID.Value);
            }
            var baseBountyCupboard = FindOrCreate(buildingPrivlidge);
            baseBountyCupboard.UpdatePlayerRelationships();
            return null;
        }

        public void TrackBuildingPrivlidgeLoop(BasePlayer basePlayer)
        {
            BaseBounty.Instance.timer.In(0.2f, () =>
            {
                if (basePlayer == null || !basePlayer.IsConnected || basePlayer.IsSleeping())
                {
                    OnEmptyBuildingPrivlidge(basePlayer);
                    return;
                }

                BuildingPrivlidge buildingPrivlidge = basePlayer.GetBuildingPrivilege();

                if (buildingPrivlidge != null)
                {
                    OnEnterBuildingPrivlidge(basePlayer, buildingPrivlidge);
                }
                else
                {
                    OnEmptyBuildingPrivlidge(basePlayer);
                }
                
                TrackBuildingPrivlidgeLoop(basePlayer);
            });
        }

        public void OnEnterBuildingPrivlidge(BasePlayer basePlayer, BuildingPrivlidge buildingPrivlidge)
        {
            var baseBountyCupboard = FindOrCreate(buildingPrivlidge);
            var protection = baseBountyCupboard.ProtectionPercentage;
            var userId = basePlayer.userID;

            if (baseBountyCupboard.Bounty > 0)
            {
                BaseBounty.Instance.SimpleStatus.CallHook("SetStatus", userId, "raidprotection.bounty", int.MaxValue);
                BaseBounty.Instance.SimpleStatus.CallHook("SetStatusText", userId, "raidprotection.bounty", $"{baseBountyCupboard.Bounty} Points");
            }
            else
            {
                BaseBounty.Instance.SimpleStatus.CallHook("SetStatus", userId, "raidprotection.bounty", 0);
            }
            if (protection > 0)
            {
                BaseBounty.Instance.SimpleStatus.CallHook("SetStatus", userId, "raidprotection.status", int.MaxValue);
                BaseBounty.Instance.SimpleStatus.CallHook("SetStatusText", userId, "raidprotection.status", $"{protection}%");
            }
            else
            {
                BaseBounty.Instance.SimpleStatus.CallHook("SetStatus", userId, "raidprotection.status", 0);
            }
        }

        public void OnEmptyBuildingPrivlidge(BasePlayer basePlayer)
        {
            var userId = basePlayer.userID;
            BaseBounty.Instance.SimpleStatus.CallHook("SetStatus", userId, "raidprotection.status", 0);
            BaseBounty.Instance.SimpleStatus.CallHook("SetStatus", userId, "raidprotection.bounty", 0);
        }

        public void OnOpenCupboard(BasePlayer basePlayer, BuildingPrivlidge buildingPrivlidge)
        {
            if (basePlayer == null || buildingPrivlidge == null)
            {
                return;
            }
            var baseBountyCupboard = FindOrCreate(buildingPrivlidge);
            baseBountyCupboard.PlayersViewing.Add(basePlayer);
            baseBountyCupboard.Data.AccessedCount++;
            Gui.OnOpenCupboard(basePlayer, buildingPrivlidge);            
        }

        public void OnCloseCupboard(BasePlayer basePlayer, BuildingPrivlidge buildingPrivlidge)
        {
            Gui.OnCloseCupboard(basePlayer, buildingPrivlidge);
            if (basePlayer == null || buildingPrivlidge == null)
            {
                return;
            }
            var baseBountyCupboard = FindOrCreate(buildingPrivlidge);
            baseBountyCupboard.PlayersViewing.Remove(basePlayer);
        }

        public HashSet<BaseBountyCupboard> GetPlayerBaseBountyToolcupboards(ulong userId)
        {
            if(!BaseBountyData.PlayerBuildingPrivlidges.TryGetValue(userId, out var buildingPrivlidgeSet))
            {
                return new HashSet<BaseBountyCupboard> { };
            }

            return buildingPrivlidgeSet
                .Select(buildingPrivlidgeId => Find(buildingPrivlidgeId))
                .Where(baseBountyCupboard => baseBountyCupboard != null)
                .ToHashSet();
        }

        public BasePlayer FindTeamLeader(BasePlayer player)
        {
            var teamLeader = player?.Team?.GetLeader();
            return teamLeader ?? player;
        }

        public BaseBountyCupboard GetPlayerMainCupboard(ulong userId, bool checkRemoved = true)
        {
            var baseBountyCupboard = GetPlayerBaseBountyToolcupboards(userId)
                .Where(_baseBountyCupboard => checkRemoved ? !_baseBountyCupboard.Data.IsRemoved && _baseBountyCupboard.Data.IsMainCupboard : _baseBountyCupboard.Data.IsMainCupboard)
                .FirstOrDefault();

            if (baseBountyCupboard != null)
            {
                return baseBountyCupboard;
            }

            if (BaseBounty.HoursSinceWipe < BaseBounty.HoursOfProtection)
            {
                return null;
            }

            baseBountyCupboard = GetPlayerBaseBountyToolcupboards(userId)
                .Where(_baseBountyCupboard => !_baseBountyCupboard.Data.IsRemoved)
                .OrderByDescending(_baseBountyCupboard => _baseBountyCupboard.Data.AccessedCount)
                .FirstOrDefault();

            if (baseBountyCupboard == null)
            {
                return null; // No TCs for this player, check the team leader or clan leader
            }

            baseBountyCupboard.Data.IsMainCupboard = true;

            return baseBountyCupboard;
        }

        public int GetHighestPerkLevel(BasePlayer player, BaseBountyPerk.BaseBountyPerkEnum perk)
        {
            if (!player)
            {
                return 0;
            }

            return GetPlayerBaseBountyToolcupboards(player.userID).Select(baseBountyCupboard => baseBountyCupboard.Level).OrderByDescending(level => level).FirstOrDefault();
        }

        public void CreateClaim(BaseBountyCupboard baseBountyCupboard)
        {
            BaseBounty.Instance.Debug($"Creating Claim for {baseBountyCupboard.Bounty} points");
            string prefab = "assets/prefabs/misc/item drop/item_drop.prefab";
            var baseBountyClaim = GameManager.server.CreateEntity(prefab, baseBountyCupboard.BuildingPrivlidge.GetDropPosition(), baseBountyCupboard.BuildingPrivlidge.Transform.rotation) as DroppedItemContainer;
            baseBountyClaim.inventory = new ItemContainer();
            baseBountyClaim.inventory.ServerInitialize(null, 1);
            baseBountyClaim.inventory.GiveUID();
            baseBountyClaim.inventory.entityOwner = baseBountyClaim;
            baseBountyClaim.inventory.SetFlag(ItemContainer.Flag.NoItemInput, b: true);
            baseBountyClaim.inventory.AddItem(ItemManager.FindItemDefinition("bleach"), 1);
            baseBountyClaim.Spawn();

            BaseBountyClaimContainer.Add(baseBountyClaim, baseBountyCupboard);
        }

        public object OnLootClaimContainer(BasePlayer basePlayer, DroppedItemContainer droppedItemContainer)
        {
            if (!BaseBountyClaimContainer.ContainsKey(droppedItemContainer))
            {
                return null;
            }

            var claimContainer = BaseBountyClaimContainer[droppedItemContainer];

            var isBleachBox = droppedItemContainer.inventory.itemList.Count == 1 && droppedItemContainer.inventory.GetSlot(0).info.shortname == "bleach";

            BaseBountyData.AddBaseBountyPoint(basePlayer, claimContainer.BountyClaim);
            // claimContainer.Data.AvailableBounty = 0;
            droppedItemContainer.Kill();
            BaseBountyClaimContainer.Remove(droppedItemContainer);


            /*if (tcClaimDrops[entity].BaseBounty > 0 && !tcClaimDrops[entity].PlayerRelations.Contains(player.userID))
            {
                ;
                tcClaimDrops[entity].BaseBounty = 0;
                tcClaimDrops.Remove(entity);
                if (entity.inventory.itemList.Count == 1 && entity.inventory.GetSlot(0).info.shortname == "bleach")
                {
                    entity.Kill();
                }
                return null;
            }
            else if (tcClaimDrops.ContainsKey(entity) && 
            {
                player.ChatMessage($"<color=#c45508>[RustWipeDay]</color>: You can not claim your own base bounty.");
                return true;
            }*/
            return null;
            
        }

        private void Debug(string message)
        {
            BaseBounty.Instance.Debug(message);
        }
    }

    public class BaseBountyData
    {
        public Dictionary<ulong, int> PlayerHeldBaseBounty = new Dictionary<ulong, int>();
        public Dictionary<ulong, HashSet<ulong>> PlayerBuildingPrivlidges = new Dictionary<ulong, HashSet<ulong>>() { };
        public Dictionary<ulong, BaseBountyCupboardData> BaseBountyCupboardData = new Dictionary<ulong, BaseBountyCupboardData>();
        public BaseBountyData() 
        {
            BaseBounty.Instance.Debug("Creating Base Bounty Data Instance");
        }
        
        public void Save()
        {
            Interface.Oxide.DataFileSystem.WriteObject("BaseBounty", this);
        }

        public static BaseBountyData Load()
        {
            BaseBountyData baseBountyData = Interface.Oxide.DataFileSystem.ReadObject<BaseBountyData>("BaseBounty");

            if (baseBountyData?.PlayerHeldBaseBounty == null)
            {
                baseBountyData.PlayerHeldBaseBounty = new Dictionary<ulong, int>();
            }
  
            if (baseBountyData?.PlayerBuildingPrivlidges == null)
            {
                baseBountyData.PlayerBuildingPrivlidges = new Dictionary<ulong, HashSet<ulong>>() { };
            }
     
            if (baseBountyData?.BaseBountyCupboardData == null)
            {
                baseBountyData.BaseBountyCupboardData = new Dictionary<ulong, BaseBountyCupboardData>();
            }

            return baseBountyData;
        }

        public bool TryFindBaseBountyCupboardData(BuildingPrivlidge buildingPrivlidge, out BaseBountyCupboardData baseBountyCupboardData)
        {
            baseBountyCupboardData = null;
            if (buildingPrivlidge == null)
            {
                return false;
            }
            return BaseBountyCupboardData.TryGetValue(buildingPrivlidge.net.ID.Value, out baseBountyCupboardData);
        }

        public BaseBountyCupboardData AddBaseBountyCupboardData(BuildingPrivlidge buildingPrivlidge)
        {
            if (buildingPrivlidge == null)
            {
                return null;
            }

            var netId = buildingPrivlidge.net.ID.Value;

            if (BaseBountyCupboardData.ContainsKey(netId))
            {
                return BaseBountyCupboardData[netId];
            }
            var baseBountyCupboardData = new BaseBountyCupboardData();
            BaseBountyCupboardData.Add(netId, baseBountyCupboardData);
            return BaseBountyCupboardData[netId];
        }

        public void AddBaseBountyPoint(BasePlayer player, int bountyPoints)
        {
            var userId = player.userID;
            AddBaseBountyPoint(userId, bountyPoints);

            Interface.CallHook("OnAddBaseBountyPoints", player, bountyPoints);
            player.ChatMessage($"<color=#c45508>[RustWipeDay]</color>: You have gained {bountyPoints} base bounty points.");
        }

        public void AddBaseBountyPoint(ulong userId, int amount)
        {
            var mainCupboard = BaseBounty.Instance.ToolcupboardTracker.GetPlayerMainCupboard(userId);
            if (mainCupboard != null)
            {
                mainCupboard.Data.AvailableBounty += amount;
                mainCupboard.UpdateMapMarker();
            }
            else if (PlayerHeldBaseBounty.ContainsKey(userId))
            {
                PlayerHeldBaseBounty[userId] += amount;
            }
            else
            {
                PlayerHeldBaseBounty.Add(userId, amount);
            }
        }

        public int GetBaseBountyPoints(ulong userId)
        {
            if (PlayerHeldBaseBounty.ContainsKey(userId))
            {
                return PlayerHeldBaseBounty[userId];
            }
            else
            {
                return 0;
            }
        }

        public bool SpendBountyPoints(BasePlayer player, int amount, ulong? buildingPrivlidgeId = null)
        {
            var userId = player.userID;

            if (amount <= 0)
            {
                return true;
            }

            if (!PlayerHeldBaseBounty.ContainsKey(userId))
            {
                return false;
            }

            var availableBounty = PlayerHeldBaseBounty[userId];

            if (availableBounty < amount)
            {
                return false;
            }

            PlayerHeldBaseBounty[userId] -= amount;

            return true;
        }
    }

    public static class Gui
    {
        public static void OnOpenCupboard(BasePlayer basePlayer, BuildingPrivlidge buildingPrivlidge)
        {
            var isMainCupboard = BaseBounty.Instance.ToolcupboardTracker.GetPlayerMainCupboard(basePlayer.userID, true) != null;

            CreateRaidProtectionButton(basePlayer, buildingPrivlidge.net.ID.Value, isMainCupboard);
        }

        public static void OnCloseCupboard(BasePlayer basePlayer, BuildingPrivlidge buildingPrivlidge)
        {
            CuiHelper.DestroyUi(basePlayer, "BaseBountyControl");
            CuiHelper.DestroyUi(basePlayer, "RaidProtectionControl");
        }

        public static void CreateRaidProtectionButton(BasePlayer basePlayer, ulong privId, bool baseBountyEnabled)
        {
            var text = "Enable Base Bounty and Raid Protection";
            var command = $"basebounty.setmain {privId}";
            if (baseBountyEnabled)
            {
                text = "View Base Bounty\nControl Panel";
                command = $"basebounty.panel {privId}";
            }

            var cuiElements = new CuiElementContainer
            {
                new CuiElement
                {
                    Name = "RaidProtectionControl",
                    Parent = "Overlay",
                    Components = {
                        new CuiRectTransformComponent {
                            AnchorMin="1 1",
                            AnchorMax="1 1",
                            OffsetMin = "-250 -60",
                            OffsetMax = "-68 0"
                        }
                    }
                }
            };
            var button = new CuiButton
            {
                Button = { Color = "0.3786 0.3686 0.3686 0.5", Command = command },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, // Position top-right and set size
                Text =
                {
                    Align = TextAnchor.MiddleCenter,
                    Text = text
                }
            };

            cuiElements.Add(button, "RaidProtectionControl", "RaidProtectionControlButton");

            CuiHelper.AddUi(basePlayer, cuiElements);
        }

        public static void OpenBaseBountyPanel(BasePlayer basePlayer, BaseBountyCupboard baseBountyCupboard)
        {
            BaseBounty.Instance.Kits.Call("OpenKitGrid", basePlayer, -1, 0, false, "BASE BOUNTY");
            var cuiContainer = new CuiElementContainer();

            // Full-screen black background with 10 pixel padding
            cuiContainer.Add(new CuiPanel
            {
                Image = { Color = "0.33 0.33 0.33 0.0" }, // Black and fully opaque
                RectTransform = { AnchorMin = "0.20 0.000", AnchorMax = "0.95 1" }, // 10 pixels padding assuming 1920x1080 resolution, adjust if needed
                CursorEnabled = true,

            }, "kits.menu", "BaseBountyPanel");

            /*
            cuiContainer.Add(
                new CuiLabel
                {
                    Text =
                    {
                        Text = "<b>BASE BOUNTY CONTROL PANEL</b>",
                        Align = TextAnchor.MiddleCenter,
                        Color = "1 1 1 1",
                        FontSize = 24,
                        Font = "robotocondensed-regular.ttf"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.2 0.85",
                        AnchorMax = "0.8 1",
                        OffsetMin = "0 -5",
                        OffsetMax = "0 -5"
                    }
                },
                "BaseBountyPanel",
                "BaseBountyPanel.TitleText"
            );
             */

            cuiContainer.Add(
                new CuiLabel
                {
                    Text =
                    {
                        Text = $"You are level {baseBountyCupboard.Level}.",
                        Align = TextAnchor.MiddleCenter,
                        Color = "1 1 1 1",
                        FontSize = 36,
                        Font = "robotocondensed-regular.ttf"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.2 0.65",
                        AnchorMax = "0.8 1",
                        OffsetMin = "0 -5",
                        OffsetMax = "0 -5"
                    }
                },
                "BaseBountyPanel",
                "BaseBountyPanel.TitleText"
            );
            var row = 0d;
            var column = 0d;
            for (var i = 0; i < BaseBountyPerk.BaseBountyPerks.Count; i++)
            {
                var perk = BaseBountyPerk.BaseBountyPerks[i];
 
                cuiContainer.Add(new CuiPanel
                {
                    Image = { Color = "0.0745 0.0745 0.0745 1" }, // Black and fully opaque
                    RectTransform =
                        {
                            AnchorMin = $"{0.12+column} {0.6-row}",
                            AnchorMax = $"{0.35+column} {0.65-row}",
                            OffsetMin = $"0 {0}",
                            OffsetMax = $"0 {0}"
                        },
                    CursorEnabled = true
                }, "BaseBountyPanel", $"BaseBountyPanel.PerkHeader.{i}");
                cuiContainer.Add(new CuiPanel
                {
                    Image = { Color = "0.0745 0.0745 0.0745 1" }, // Black and fully opaque
                    RectTransform =
                        {
                            AnchorMin = $"{0.12+column} {0.50-row}",
                            AnchorMax = $"{0.35+column} {0.595-row}",
                            OffsetMin = $"0 {0}",
                            OffsetMax = $"0 {0}"
                        },
                    CursorEnabled = true
                }, "BaseBountyPanel", $"BaseBountyPanel.PerkPanel.{i}");
                cuiContainer.Add(
                    new CuiLabel
                    {
                        Text =
                        {
                            Text = $"<b>{perk.Title.ToUpper()}</b>",
                            Align = TextAnchor.MiddleCenter,
                            Color = "1 1 1 1",
                            FontSize = 18,
                            Font = "robotocondensed-regular.ttf"
                        },
                        RectTransform =
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = $"0 0",
                            OffsetMax = $"0 0"
                        }
                    },
                    $"BaseBountyPanel.PerkHeader.{i}",
                    $"BaseBountyPanel.PerkHeaderText.{i}"
                );

                cuiContainer.Add(
                new CuiLabel
                {
                    Text =
                    {
                        Text = $"{perk.Description(baseBountyCupboard.Level)}",
                        Align = TextAnchor.MiddleCenter,
                        Color = "1 1 1 1",
                        FontSize = 10,
                        Font = "robotocondensed-regular.ttf"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.0 0.05",
                        AnchorMax = "1 1",
                        OffsetMin = $"0 0",
                        OffsetMax = $"0 0"
                    }
                },
                $"BaseBountyPanel.PerkPanel.{i}",
                $"BaseBountyPanel.PerkCostText.{i}"
            );

                /* cuiContainer.Add(new CuiButton
                {
                    Button = { Color = "0.44 0.44 0.44 1", Command = $"basebounty.claim {baseBountyCupboard.Data.NetId} {i}", }, // Set button command and image type if needed
                    RectTransform = { AnchorMin = "0.01 0.05", AnchorMax = "0.20 0.95" }, // Position top-right and set size
                    Text = { Text = "" } // No text, using an icon
                }, $"BaseBountyPanel.PerkPanel.{i}", $"BaseBountyPanel.PerkButton.{i}");

                // Assuming you have an icon ready and accessible. For CUI, you often reference an in-game or server-side resource.
                cuiContainer.Add(new CuiElement
                {
                    Parent = $"BaseBountyPanel.PerkButton.{i}",

                    Components =
                    {
                        new CuiImageComponent { ItemId = 1223900335 },
                    }
                });*/

                column += 0.25;
                if (i % 3 == 2)
                {
                    row += 0.2d;
                    column = 0;
                }
            }

            // Close button (X)
            /*cuiContainer.Add(new CuiButton
            {
                Button =
                    {
                        Command = "basebounty.panel.close",
                        Color = "0.8 0.1 0.1 1",
                        FadeIn = 0.5f
                    },
                RectTransform = { AnchorMin = "0.9 0.9", AnchorMax = "0.95 0.95" },
                Text = { Text = "X", Align = TextAnchor.MiddleCenter }
            }, "BaseBountyPanel", "BaseBountyPanel.CloseButton");*/

            CuiHelper.DestroyUi(basePlayer, "BaseBountyPanel");
            CuiHelper.AddUi(basePlayer, cuiContainer);
        }
    }

    public static class Colors
    {
        public static readonly string COLOR_TRANSPARENT = "0 0 0 0";
        public static readonly string COLOR_GREEN = "0.749 0.9059 0.4783 1";
        public static readonly string COLOR_RED = "1 0.529 0.180 1";

        public static readonly string COLOR_BLACK = "0 0 0 1";
        public static readonly string COLOR_WHITE = "1 1 1 1";
        public static readonly string COLOR_GREY = "0.75 0.75 0.75 1";

        public static readonly string COLOR_GREEN_DARK = "0.5992 0.72472 0.38264 1";
        public static readonly string COLOR_GREEN_DARK_LESS = "0.25 0.4 0.1 1";
        public static readonly string COLOR_RED_DARK = "0.8 0.4232 0.144 1";
        public static readonly string COLOR_RED_DARK_LESS = "0.8 0.25 0.144 1";

        public static readonly string COLOR_YELLOW = "1 1 0.5 1";
    }
}
