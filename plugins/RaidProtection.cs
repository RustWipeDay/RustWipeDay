using Newtonsoft.Json;
using System.Text;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Linq;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using Oxide.Core;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
	[Info("Raid Protection", "mr01sam", "2.1.10")]
	[Description("Configurable raid protection at the cost of a resource")]
	public class RaidProtection : CovalencePlugin
	{
		/*
		 * CURRENT CHANGELOG
		 * - Fix May 4th Update
		 */

		[PluginReference]
		private readonly Plugin Economics;

		[PluginReference]
		private readonly Plugin ServerRewards;

		[PluginReference]
		private readonly Plugin ScrapRaidProtection;

		[PluginReference]
		private readonly Plugin ImageLibrary;

		public static RaidProtection PLUGIN;

		public readonly int COLLECTION_INTERVAL = 30;

		public readonly string PermissionLevel = "raidprotection.level.";
		public const string PermissionAdmin = "raidprotection.admin";

		private readonly HashSet<ulong> onCooldown = new HashSet<ulong>();

		private readonly EconomyPanel economyPanel = new EconomyPanel();

		private readonly string[] trackedBuildingBlocks = new string[] { "foundation", "foundation.triangle", "floor", "floor.triangle" };

		private bool UseMaterialPrices;
		private ItemDefinition CurrencyItemDef;

		#region Oxide Hooks

		private void Init()
		{
			Unsubscribe(nameof(Unload));
			Unsubscribe(nameof(OnServerSave));
			Unsubscribe(nameof(OnPlayerSleepEnded));
			Unsubscribe(nameof(OnUserConnected));
			Unsubscribe(nameof(OnUserDisconnected));
			Unsubscribe(nameof(OnEntitySpawned));
			Unsubscribe(nameof(OnEntityKill));
			Unsubscribe(nameof(OnEntityDeath));
			Unsubscribe(nameof(OnCupboardAuthorize));
			Unsubscribe(nameof(OnCupboardDeauthorize));
			Unsubscribe(nameof(OnCupboardClearList));
			Unsubscribe(nameof(OnUserPermissionGranted));
			Unsubscribe(nameof(OnUserPermissionRevoked));
			Unsubscribe(nameof(OnUserGroupAdded));
			Unsubscribe(nameof(OnUserGroupRemoved));
			Unsubscribe(nameof(OnGroupPermissionGranted));
			Unsubscribe(nameof(OnGroupPermissionRevoked));
			Unsubscribe(nameof(OnLootEntity));
			Unsubscribe(nameof(OnLootEntityEnd));
			Unsubscribe(nameof(OnEntityTakeDamage));
			Unsubscribe(nameof(OnEntityBuilt));
			Unsubscribe(nameof(OnEntityKill));
			Unsubscribe(nameof(OnStructureUpgrade));
			ProtectionLevel.Load();
			PLUGIN = this;
		}

		private void OnServerInitialized()
		{
			if (ScrapRaidProtection)
			{
				PrintError($"You have both Raid Protection and Scrap Raid Protection installed. Raid Protection is a new version of Scrap Raid Protection and it is recommened that you remove Scrap Raid Protection.");
			}
			if (config.Settings.UseEconomics && !Economics)
			{
				PrintWarning($"You do not have the Economics plugin installed, setting 'Use economics balance' to false");
				config.Settings.UseEconomics = false;
			}
			if (config.Settings.UseRP && !ServerRewards)
			{
				PrintWarning($"You do not have the Server Rewards plugin installed, setting 'Use rewards points' to false");
				config.Settings.UseRP = false;
			}
			if (config.Settings.UseRP && config.Settings.UseEconomics)
			{
				PrintWarning($"You have both 'Use reward points' and 'Use economics balance' set to true. Only one can be used. Setting 'Use economics balance' to false");
				config.Settings.UseEconomics = false;
			}

			InitProtectionLevels();
			LoadImages();
			LoadAllTcs();
			ResetUI();

			Subscribe(nameof(Unload));
			Subscribe(nameof(OnServerSave));
			Subscribe(nameof(OnPlayerSleepEnded));
			Subscribe(nameof(OnUserConnected));
			Subscribe(nameof(OnUserDisconnected));
			Subscribe(nameof(OnEntitySpawned));
			Subscribe(nameof(OnEntityKill));
			Subscribe(nameof(OnEntityDeath));
			Subscribe(nameof(OnCupboardAuthorize));
			Subscribe(nameof(OnCupboardDeauthorize));
			Subscribe(nameof(OnCupboardClearList));
			Subscribe(nameof(OnUserPermissionGranted));
			Subscribe(nameof(OnUserPermissionRevoked));
			Subscribe(nameof(OnUserGroupAdded));
			Subscribe(nameof(OnUserGroupRemoved));
			Subscribe(nameof(OnGroupPermissionGranted));
			Subscribe(nameof(OnGroupPermissionRevoked));
			Subscribe(nameof(OnLootEntity));
			Subscribe(nameof(OnLootEntityEnd));
			Subscribe(nameof(OnEntityTakeDamage));
			Subscribe(nameof(OnEntityBuilt));
			Subscribe(nameof(OnEntityKill));
			Subscribe(nameof(OnStructureUpgrade));
		}

		private void OnServerSave()
		{
			SaveTcBalances();
		}

		private void Unload()
		{
			SaveTcBalances();
			ProtectedCupboard.Unload();
			ProtectionLevel.Unload();
			PLUGIN = null;
		}

		private void OnUserConnected(IPlayer player)
		{
			HandleUserConnected(player);
		}

		private void OnUserDisconnected(IPlayer player)
		{
			HandleUserDisconnected(player);
		}

		void OnPlayerSleepEnded(BasePlayer player)
		{
			if (config.Indicator.Persistent)
			{
				IndicatorLoop(player);
			}
		}

		private void OnEntitySpawned(BuildingPrivlidge priv)
		{
			timer.In(0.1f, () =>
			{
				if (priv.IsValid())
				{
					ProtectedCupboard.InitCupboard(priv);
				}
			});
		}

		private void OnEntityKill(BuildingPrivlidge priv)
		{
			ProtectedCupboard.RemoveCupboard(priv);
		}

		private void OnEntityDeath(BuildingPrivlidge priv, HitInfo hitInfo) => OnEntityKill(priv);

		private object OnCupboardAuthorize(BuildingPrivlidge priv, BasePlayer player)
		{
			timer.In(0.1f, () =>
			{
				if (priv.IsValid())
				{
					ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
					if ((tc != null) && (!permission.UserHasPermission(player.userID.ToString(), PermissionAdmin)))
					{
						tc.AllOwnerIds.Add(player.userID);
						tc.OnlineOwners.Add(player);
						tc.UpdateHighestProtectionLevel();
						tc.UpdateTotalBuildingCost();
						tc.UpdateProtectionCostPerHour();
						tc.UpdateStatus();
					}
				}
			});
			return null;
		}

		private object OnCupboardDeauthorize(BuildingPrivlidge priv, BasePlayer player)
		{
			timer.In(0.1f, () =>
			{
				if (priv.IsValid())
				{
					ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
					if ((tc != null) && (!permission.UserHasPermission(player.userID.ToString(), PermissionAdmin)))
					{
						tc.AllOwnerIds.Remove(player.userID);
						tc.OnlineOwners.Remove(player);
						tc.UpdateHighestProtectionLevel();
						tc.UpdateTotalBuildingCost();
						tc.UpdateProtectionCostPerHour();
						tc.UpdateStatus();
					}
				}
			});
			return null;
		}

		private object OnCupboardClearList(BuildingPrivlidge priv, BasePlayer player)
		{
			timer.In(0.1f, () =>
			{
				if (priv.IsValid())
				{
					ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
					if (tc != null)
					{
						tc.AllOwnerIds = new HashSet<ulong>();
						tc.UpdateHighestProtectionLevel();
						tc.UpdateTotalBuildingCost();
						tc.UpdateProtectionCostPerHour();
						tc.UpdateStatus();
					}
				}
			});
			return null;
		}

		private void OnUserPermissionGranted(string id, string permName)
		{
			HandleUserPermissionChanged(id, permName);
		}

		private void OnUserPermissionRevoked(string id, string permName)
		{
			HandleUserPermissionChanged(id, permName);
		}

		void OnUserGroupAdded(string id, string groupName)
		{
			if (groupName != null)
				HandleUserPermissionChanged(id, PermissionLevel);
		}

		void OnUserGroupRemoved(string id, string groupName)
		{
			if (groupName != null)
				HandleUserPermissionChanged(id, PermissionLevel);
		}

		private void OnGroupPermissionGranted(string group, string permName)
		{
			HandleGroupPermissionChanged(group, permName);
		}

		private void OnGroupPermissionRevoked(string group, string permName)
		{
			HandleGroupPermissionChanged(group, permName);
		}

		void OnLootEntity(BasePlayer player, BuildingPrivlidge priv)
		{
			if (player != null && priv != null)
			{
				ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
				if (tc != null)
				{
					tc.PlayersViewing.Add(player);
					OpenUi(player, priv);
				}
			}
		}

		private void OnLootEntityEnd(BasePlayer player, BuildingPrivlidge priv)
		{
			ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
			if (tc != null)
			{
				tc.PlayersViewing.Remove(player);
			}
			CloseUi(player);
		}

		object OnLootNetworkUpdate(PlayerLoot loot)
		{
			try
			{
				if (loot.entitySource != null && loot.entitySource is BuildingPrivlidge && !config.Settings.UseEconomics && !config.Settings.UseRP)
				{
					ProtectedCupboard tc = ProtectedCupboard.InitCupboard((BuildingPrivlidge)loot.entitySource);
					if (tc != null)
					{
						bool before = tc.IsActive;
						tc.UpdateStoredBalance(tc.ProtectionCostPerInterval);
						tc.InitialPurchaseProtection();
						BasePlayer player = loot._baseEntity;
						if (player != null && tc.Priv != null)
						{
							OpenUi(player, tc.Priv);
						}
						if (tc.IsActive != before)
						{
							ShowIndicatorForOwners(tc);
						}
					}
				}
				return null;
			}
			catch (Exception) { return null; }
		}

		private object OnEntityTakeDamage(DecayEntity entity, HitInfo info)
		{
			return config.Protection.ProtectBuildings ? ProtectFromDamage(entity, info) : null;
		}

		private object OnEntityTakeDamage(LootContainer entity, HitInfo info)
		{
			return null;
		}

		private object OnEntityTakeDamage(BaseMountable entity, HitInfo info)
		{
			return config.Protection.ProtectVehicles ? ProtectFromDamage(entity, info) : null;
		}

		private object OnEntityTakeDamage(BasePlayer entity, HitInfo info)
		{
			return config.Protection.ProtectPlayers ? ProtectFromDamage(entity, info) : null;
		}

		private object OnEntityTakeDamage(NPCPlayer entity, HitInfo info)
		{
			return null;
		}

		private object OnEntityTakeDamage(IOEntity entity, HitInfo info)
		{
			return config.Protection.ProtectTraps ? ProtectFromDamage(entity, info) : null;
		}

		private object OnEntityTakeDamage(BaseResourceExtractor entity, HitInfo info)
		{
			return config.Protection.ProtectBuildings ? ProtectFromDamage(entity, info) : null;
		}

		private void OnEntityBuilt(Planner plan, GameObject go)
		{
			if (plan == null || go?.name == "assets/prefabs/deployable/tool cupboard/cupboard.tool.deployed.prefab") return;
			DebugTimeStart("OnEntityBuilt");
			BuildingPrivlidge priv = plan.GetBuildingPrivilege();
			if (priv != null)
			{
				ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
				if (tc != null)
				{
					tc.UpdateTotalBuildingCost();
					tc.UpdateProtectionCostPerHour();
				}
			}
			DebugTimeEnd("OnEntityBuilt");
		}

		private void OnEntityKill(BuildingBlock block)
		{
			BuildingPrivlidge priv = block.GetBuildingPrivilege();
			if (priv != null)
			{
				ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
				timer.In(0.1f, () =>
				{
					if (tc != null)
					{
						tc.UpdateTotalBuildingCost();
						tc.UpdateProtectionCostPerHour();
					}
				});
			}
		}

		private void OnEntityDeath(BuildingBlock block, HitInfo hitInfo) => OnEntityKill(block);

		private object OnStructureUpgrade(BuildingBlock block, BasePlayer player, BuildingGrade.Enum grade)
		{
			BuildingPrivlidge priv = block.GetBuildingPrivilege();
			if (priv != null)
			{
				ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
				if (tc != null)
				{
					tc.UpdateTotalBuildingCost();
					tc.UpdateProtectionCostPerHour();
				}
			}
			return null;
		}

		#endregion Oxide Hooks

		#region Helper Functions

		private object ProtectFromDamage(BaseCombatEntity entity, HitInfo info)
		{
			if (entity?.net == null) return null;
			if (info == null) return null;

			BuildingPrivlidge priv = entity.GetBuildingPrivilege();
			if (priv == null) { return null; };

			Rust.DamageType majorityDamage = info.damageTypes.GetMajorityDamageType();
			if (majorityDamage == Rust.DamageType.Decay || majorityDamage == Rust.DamageType.ElectricShock) { return null; };

			ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
			if (tc == null) { return null; };

			DebugTimeStart("ProtectFromDamage");
			if (tc.Status == ProtectionStatus.Protected)
			{

				if (tc.ProtectionPercentage >= 100)
				{
					ApplyDamageCost(tc, info, majorityDamage);
					if (info.Initiator is BasePlayer && info.InitiatorPlayer != null)
					{
						ShowIndicator(info.InitiatorPlayer, priv, true);
					}
					DebugTimeEnd("ProtectFromDamage");
					return true;
				}
				else
				{
					ApplyDamageCost(tc, info, majorityDamage, (tc.ProtectionPercentage / 100));
					info.damageTypes.ScaleAll((float)(1 - (tc.ProtectionPercentage / 100)));
					if (info.Initiator is BasePlayer && info.InitiatorPlayer != null)
					{
						ShowIndicator(info.InitiatorPlayer, priv, true);
					}
				}
			}
			else
			{
				tc.MostRecentAttack = DateTime.Now;
			}
			DebugTimeEnd("ProtectFromDamage");
			return null;
		}

		private void ApplyDamageCost(ProtectedCupboard tc, HitInfo info, Rust.DamageType majorityDamage, float ratio = 1f)
		{
			if (tc.CostPerDamage > 0 && majorityDamage != Rust.DamageType.Heat)
			{
				float costFromDamage = info.damageTypes.Total() * tc.CostPerDamage * (ratio);
				tc.UpdateCostDebt(costFromDamage);
				tc.UpdateStoredBalance(costFromDamage);
				tc.UpdateStatus();
				PLUGIN.Debug($"TC with ID={tc.Priv.net.ID} consumed ${costFromDamage} from damage");
			}
		}

		private void InitProtectionLevels()
		{
			try
			{
				foreach (int i in config.Protection.ProtectionLevels.Keys)
				{
					permission.RegisterPermission(PermissionLevel + i.ToString(), this);
					Puts($"Registered permission {PermissionLevel + i.ToString()}");
				}
			}
			catch
			{
				PrintError("Failed to load protection levels from config file, make sure they are properly formatted.");
			}
		}

		private string FormatCurrency(double amount)
		{
			if (config.Settings.UseRP)
			{
				return $"{amount} RP";
			}
			if (config.Settings.UseEconomics)
			{
				return $"{amount:C}";
			}
			return $"{amount} {CurrencyItemDef.displayName.translated}";

		}

		private string GetStatusMessage(BasePlayer player, ProtectedCupboard tc)
		{
			if (!tc.HasPermission || !tc.HasAnyProtection)
			{
				return Lang("StatusNoPermission", player.UserIDString);
			}
			if (!tc.CanAffordProtection && tc.IsActive)
			{
				return Lang("StatusNoBalance", player.UserIDString);
			}
			if (!tc.CanAffordProtection && !tc.IsActive)
			{
				return Lang("StatusUnprotected", player.UserIDString, FormatCurrency((int)Math.Ceiling(tc.ProtectionCostPerInterval)));
			}
			switch (tc.Status)
			{
				case ProtectionStatus.Protected:
					return Lang("StatusProtected", player.UserIDString, tc.ProtectionPercentage, GetTimeRemaining(player, tc));

				case ProtectionStatus.PendingOfflineOnly:
					return Lang("StatusPendingOffline", player.UserIDString, tc.HighestProtectionLevel.OfflineProtectionPercentage);

				case ProtectionStatus.PendingRecentlyDamage:
					return Lang("StatusPendingDamage", player.UserIDString, tc.ProtectionPercentage);

				default:
					return Lang("StatusUnprotected", player.UserIDString, FormatCurrency((int)Math.Ceiling(tc.ProtectionCostPerInterval)));
			}
		}

		private string GetTimeRemaining(BasePlayer player, ProtectedCupboard tc)
		{
			return GetTimeRemaining(player.UserIDString, tc);
		}

		private string GetTimeRemaining(string UserIDString, ProtectedCupboard tc)
		{
			float hours = tc.HoursRemaining;
			if (hours <= 0 || tc.HoursRemaining <= 0)
			{
				return "∞";
			}
			if (hours >= 24)
			{
				return Math.Round(hours / 24, 1).ToString() + $" {Lang("Days", UserIDString)}";
			}
			if (hours >= 1)
			{
				return Math.Round(hours, 1).ToString() + $" {Lang("Hours", UserIDString)}";
			}
			else
			{
				return Math.Round((hours * 60), 1).ToString() + $" {Lang("Minutes", UserIDString)}";
			}
		}

		private void LoadImages()
		{
			if (ImageLibrary != null && ImageLibrary.IsLoaded)
			{
				ImageLibrary.Call<bool>("AddImage", config.Indicator.ImageUrl, "SrpIndicatorIcon", 0UL);
			}
		}

		private void LoadAllTcs()
		{
			timer.In(1f, () =>
			{
				ProtectedCupboard.toolcupboards = new Dictionary<ulong, ProtectedCupboard>();
				List<BuildingPrivlidge> allCupboards = GameObject.FindObjectsOfType<BuildingPrivlidge>().ToList();
				int i = 0;
				foreach (BuildingPrivlidge priv in allCupboards)
				{
					if (ProtectedCupboard.InitCupboard(priv) != null)
					{
						i++;
					}
				}
				Puts($"Initialized {i} cupboards");
				StartCollecting();
				StartIndicatorLoops();
				LoadTcBalances();
			});
		}

		private void StartCollecting()
		{
			int i = 0;
			PLUGIN.Debug($"Starting collection loop");
			PLUGIN.timer.Repeat(0.1f, ProtectedCupboard.toolcupboards.Count, () =>
			{
				if (ProtectedCupboard.toolcupboards != null && ProtectedCupboard.toolcupboards.Keys.Count > i)
				{
					ProtectedCupboard tc = ProtectedCupboard.toolcupboards[ProtectedCupboard.toolcupboards.Keys.ElementAt(i)];
					if (tc != null && tc.Priv != null)
					{
						tc.PurchaseProtection();
						i++;
					}
				}
			});
		}

		private void StartIndicatorLoops()
		{
			if (config.Indicator.Persistent)
			{
				PLUGIN.Debug($"Starting indicator loop");
				foreach (BasePlayer basePlayer in BasePlayer.activePlayerList)
				{
					IndicatorLoop(basePlayer);
				}
			}
		}

		private void SaveTcBalances()
		{
			if (config.Settings.UseEconomics || config.Settings.UseRP)
			{
				Dictionary<ulong, float> balances = ProtectedCupboard.toolcupboards.Values.ToDictionary(x => x.Priv.net.ID.Value, x => x.StoredBalance);
				Interface.Oxide.DataFileSystem.WriteObject("CupboardBalances", balances);
				Debug($"Saved {balances.Count} balances");
			}
		}

		private void LoadTcBalances()
		{
			if (config.Settings.UseEconomics || config.Settings.UseRP)
			{
				timer.In(0.5f, () =>
				{
					Dictionary<uint, float> balances = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<uint, float>>("CupboardBalances");
					int loaded = 0;
					foreach (uint key in balances.Keys)
					{
						if (ProtectedCupboard.toolcupboards.ContainsKey(key))
						{
							ProtectedCupboard tc = ProtectedCupboard.toolcupboards[key];
							tc.StoredBalance = balances[key];
							tc.PurchaseProtection();
							loaded++;
						}
					}
					Debug($"Loaded {loaded} balances");
				});

			}
		}

		private void ResetUI()
		{
			foreach (BasePlayer basePlayer in BasePlayer.activePlayerList)
			{
				if (basePlayer.IsValid())
				{
					CloseUi(basePlayer);
					CloseIndicator(basePlayer);
					if (onCooldown.Contains(basePlayer.userID))
						onCooldown.Remove(basePlayer.userID);
				}
			}
		}

		private double GetBalance(ulong userId)
		{
			if (config.Settings.UseRP)
			{
				int? points = (int?)ServerRewards.Call("CheckPoints", userId);
				return points ?? 0;
			}
			if (config.Settings.UseEconomics)
			{
				return (double)Economics.Call("Balance", userId);
			}
			return 0;
		}

		private void TakeBalance(ulong userId, double amount)
		{
			if (config.Settings.UseRP)
			{
				ServerRewards.Call("TakePoints", userId, (int)amount);
			}
			if (config.Settings.UseEconomics)
			{
				Economics.Call("Withdraw", userId, amount);
			}
		}

		private void GiveBalance(ulong userId, double amount)
		{
			if (config.Settings.UseRP)
			{
				ServerRewards.Call("AddPoints", userId, (int)amount);
			}
			if (config.Settings.UseEconomics)
			{
				Economics.Call("Deposit", userId, amount);
			}
		}

		private void HandleUserPermissionChanged(string id, string permName)
		{
			if (id == null || permName == null)
				return;
			if (permName.StartsWith(PermissionLevel))
			{
				List<BuildingPrivlidge> allPrivlidges = BaseNetworkable.serverEntities.OfType<BuildingPrivlidge>().ToList();
				BasePlayer player = BasePlayer.FindByID(ulong.Parse(id));
				if (player.IsValid())
				{
					foreach (BuildingPrivlidge priv in allPrivlidges)
					{
						if ((priv.IsAuthed(player)) && (!permission.UserHasPermission(player.userID.ToString(), PermissionAdmin)))
						{
							ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
							if (tc != null)
							{
								tc.UpdateHighestProtectionLevel();
								tc.UpdateTotalBuildingCost();
								tc.UpdateProtectionCostPerHour();
								tc.UpdateStatus();
							}
						}
					}
				}
			}
		}

		private void HandleGroupPermissionChanged(string group, string permName)
		{
			if (group == null || permName == null)
				return;
			if (permName.StartsWith(PermissionLevel))
			{
				string[] userIds = permission.GetUsersInGroup(group);
				foreach (string steamid in userIds)
				{
					BasePlayer basePlayer = BasePlayer.Find(steamid.Split(' ')[0]);
					if (basePlayer != null)
					{
						if (!permission.UserHasPermission(basePlayer.userID.ToString(), PermissionAdmin))
							HandleUserPermissionChanged(basePlayer.UserIDString, permName);
					}
				}
			}
		}

		private void HandleUserConnected(IPlayer player)
		{
			timer.In(0.1f, () =>
			{
				DebugTimeStart("HandleUserConnected");
				if (player != null)
				{
					foreach (ProtectedCupboard tc in ProtectedCupboard.toolcupboards.Values.Where(
						x => x?.Priv != null
						&& x.AllOwnerIds.Contains(ulong.Parse(player.Id))
						&& x.OnlineOwners.Count == 1
						&& !(x.HasOnlineProtection && x.HasOfflineProtection)
					))
					{
						if (tc.HasOnlineProtection && !tc.IsActive)
						{
							tc.PurchaseProtection();
						}
						else if (tc.HasOfflineProtection && tc.IsActive)
						{
							tc.StopProtection();
						}
					}
				}
				DebugTimeEnd("HandleUserConnected");
			});
		}

		private void HandleUserDisconnected(IPlayer player)
		{
			timer.In(0.1f, () =>
			{
				DebugTimeStart("HandleUserDisconnected");
				if (player != null)
				{
					foreach (ProtectedCupboard tc in ProtectedCupboard.toolcupboards.Values.Where(
						x => x?.Priv != null
						&& x.AllOwnerIds.Contains(ulong.Parse(player.Id))
						&& x.OnlineOwners.Count == 0
						&& !(x.HasOnlineProtection && x.HasOfflineProtection)
					))
					{
						if (tc.HasOnlineProtection && tc.IsActive)
						{
							tc.StopProtection();
						}
						else if (tc.HasOfflineProtection && !tc.IsActive)
						{
							timer.In(config.Protection.OfflineProtectionDelay, () =>
							{
								if (tc?.Priv != null && !tc.HasOwnersOnline)
								{
									tc.PurchaseProtection();
								}
							});
						}
					}
				}
				DebugTimeEnd("HandleUserDisconnected");
			});
		}

		private void IndicatorLoop(BasePlayer player)
		{
			if (config.Indicator.Persistent)
			{
				timer.In(2f, () =>
				{
					if (player != null && player.IsConnected && !player.IsSleeping())
					{
						BuildingPrivlidge priv = player.GetBuildingPrivilege();
						if (priv != null)
						{
							ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
							if (tc != null)
							{
								if (priv.IsAuthed(player) && tc.HasOnlineProtection)
								{
									ShowIndicator(player, priv, true, true, true);
								}
								else if (tc.Status == ProtectionStatus.Protected)
								{
									ShowIndicator(player, priv, true, true, true);
								}
								else
								{
									CloseIndicator(player);
								}
							}
						}
						else
						{
							CloseIndicator(player);
						}
						IndicatorLoop(player);
					}
				});
			}
		}

		#endregion Helper Functions

		#region Classes

		private enum ProtectionStatus
		{
			Unprotected = 0,
			PendingOfflineOnly = 1, /* Not currently protected, but will be when offline */
			PendingOnlineOnly = 2, /* Not currently protected, but will be when online */
			PendingRecentlyDamage = 3,
			Protected = 4
		}

		private class EconomyPanel
		{
			private const float MIN = 0f;
			private const float MAX = 9999f;

			private Dictionary<string, float> SetBalances = new Dictionary<string, float>();

			public void Increment(string userIdString, float amount)
			{
				if (!SetBalances.ContainsKey(userIdString))
				{
					SetBalances.Add(userIdString, MIN);
				}
				SetBalances[userIdString] = Math.Min(SetBalances[userIdString] + amount, MAX);
			}

			public void Decrement(string userIdString, float amount)
			{
				if (!SetBalances.ContainsKey(userIdString))
				{
					SetBalances.Add(userIdString, MIN);
				}
				SetBalances[userIdString] = Math.Max(SetBalances[userIdString] - amount, MIN);
			}

			public float Get(string userIdString)
			{
				if (!SetBalances.ContainsKey(userIdString))
				{
					SetBalances.Add(userIdString, MIN);
				}
				return SetBalances[userIdString];
			}

			public void Reset(string userIdString)
			{
				if (!SetBalances.ContainsKey(userIdString))
				{
					SetBalances.Add(userIdString, MIN);
				}
				SetBalances[userIdString] = MIN;
			}
		}

		private class ProtectionLevel
		{
			public static ProtectionLevel NONE { get; set; }

			[JsonProperty(PropertyName = "Online protection percentage (0-100)")]
			public float OnlineProtectionPercentage { get; set; }

			[JsonProperty(PropertyName = "Offline protection percentage (0-100)")]
			public float OfflineProtectionPercentage { get; set; }

			[JsonProperty(PropertyName = "Hourly building cost", NullValueHandling = NullValueHandling.Ignore)]
			public float? HourlyBuildingCost { get; set; } = null;

			[JsonProperty(PropertyName = "Hourly base cost", NullValueHandling = NullValueHandling.Ignore)]
			public float? HourlyBaseCost { get; set; } = null;

			[JsonProperty(PropertyName = "Structure damage cost", NullValueHandling = NullValueHandling.Ignore)]
			public float? CostPerDamage { get; set; } = null;

			public static int LevelOf(string useridstring)
			{
				List<int> keys = PLUGIN.config.Protection.ProtectionLevels.Keys.ToList();
				keys.Sort((a, b) => b.CompareTo(a));
				foreach (int i in keys)
				{
					if (PLUGIN.permission.UserHasPermission(useridstring, PLUGIN.PermissionLevel + i) || PLUGIN.permission.GetUserGroups(useridstring).Any(groupid => PLUGIN.permission.GroupHasPermission(groupid, PLUGIN.PermissionLevel + i)))
					{
						return (int)i;
					}
				}
				return -1;
			}

			public override string ToString()
			{
				return this == NONE ? "--" : PLUGIN.config.Protection.ProtectionLevels.Values.ToList().IndexOf(this).ToString();
			}

			public static void Load()
			{
				NONE = new ProtectionLevel { OnlineProtectionPercentage = 0f, OfflineProtectionPercentage = 0f };
			}

			public static void Unload()
			{
				NONE = null;
			}
		}

		private class ProtectedCupboard
		{
			public static Dictionary<ulong, ProtectedCupboard> toolcupboards = new Dictionary<ulong, ProtectedCupboard>();

			public static ProtectedCupboard InitCupboard(BuildingPrivlidge priv)
			{
				if (priv != null && toolcupboards != null && !toolcupboards.ContainsKey(priv.net.ID.Value))
				{
					toolcupboards.Add(priv.net.ID.Value, new ProtectedCupboard(priv));
					PLUGIN.Debug($"Added TC with ID={priv.net.ID}");
					PLUGIN.Debug($"There are {toolcupboards.Count} TCs added");
					return toolcupboards[priv.net.ID.Value];
				}
				else if (priv != null && toolcupboards != null)
				{
					return toolcupboards[priv.net.ID.Value];
				}
				return null;
			}

			public static void RemoveCupboard(BuildingPrivlidge priv)
			{
				if (priv != null && toolcupboards.ContainsKey(priv.net.ID.Value))
				{
					toolcupboards.Remove(priv.net.ID.Value);
					PLUGIN.Debug($"Removed TC with ID={priv.net.ID}");
					PLUGIN.Debug($"There are {toolcupboards.Count} TCs added");
				}
			}

			public BuildingPrivlidge Priv { get; set; }

			public float ProtectionCostPerHour { get; private set; }

			public float HoursRemaining
			{
				get { return StoredBalance / ProtectionCostPerHour; }
			}

			public float ProtectionCostPerInterval
			{
				get { return ProtectionCostPerHour / 3600 * PLUGIN.COLLECTION_INTERVAL; }
			}


			public int SurfaceBlockCount { get { return Priv == null ? 0 : Priv.GetBuilding().buildingBlocks.Where(x => x.ShortPrefabName.Contains("floor") || x.ShortPrefabName.Contains("foundation")).Count(); } }

			public DateTime MostRecentAttack { get; set; }

			public bool RecentlyDamaged
			{
				get { return MostRecentAttack == null ? false : (DateTime.Now - MostRecentAttack).TotalSeconds < PLUGIN.config.Protection.ProtectedDelayAfterTakingDamage; }
			}

			public float StoredBalance { get; set; }

			public float CostDebt { get; set; }

			public bool ContentsLocked { get; set; }

			public int ItemAmount { get { return Priv.inventory.GetAmount(PLUGIN.config.Settings.CurrencyItemId, false); } }

			public float HourlyBaseCost { get; set; }

			public float HourlyBuildingCost { get; set; }

			public float TotalBuildingCost { get; set; }

			public float CostPerDamage { get; set; }

			public bool CanAffordProtection
			{
				get { return StoredBalance >= ProtectionCostPerInterval; }
			}

			public HashSet<BasePlayer> PlayersViewing { get; set; }

			public HashSet<ulong> AllOwnerIds { get; set; }

			public HashSet<BasePlayer> OnlineOwners
			{
				get
				{
					return _debugging ? PLUGIN._onlinePlayers : (from id in AllOwnerIds select BasePlayer.FindByID(id)).Where(x => x != null && x.IsConnected).ToHashSet();
				}
			}

			public ProtectionLevel HighestProtectionLevel { get; set; }

			public bool ForcedProtectionLevel { get; set; }

			public bool IsActive { get; set; }

			public bool HasPermission { get { return HighestProtectionLevel != ProtectionLevel.NONE; } }

			public bool HasOnlineProtection { get { return HighestProtectionLevel.OnlineProtectionPercentage > 0; } }

			public bool HasOfflineProtection { get { return HighestProtectionLevel.OfflineProtectionPercentage > 0; } }

			public bool HasAnyProtection { get { return HasOnlineProtection || HasOfflineProtection; } }

			public bool HasOwnersOnline { get { return OnlineOwners.Count > 0; } }

			public ProtectionStatus Status { get; private set; }

			public float ProtectionPercentage
			{
				get
				{
					return HighestProtectionLevel == null ? 0 : OnlineOwners.Count == 0 ? HighestProtectionLevel.OfflineProtectionPercentage : HighestProtectionLevel.OnlineProtectionPercentage;
				}
			}

			public ProtectedCupboard(BuildingPrivlidge priv)
			{
				HashSet<ulong> AllOwnerIdsTemp = (from id in priv.authorizedPlayers select id.userid).ToHashSet();
				HashSet<ulong> AllOwnerIds = new HashSet<ulong>();

				if (AllOwnerIdsTemp.Count >= 1)
				{
					foreach (ulong OwnerId in AllOwnerIdsTemp)
					{
						if (PLUGIN.permission.UserHasPermission(OwnerId.ToString(), PermissionAdmin))
							continue;

						AllOwnerIds.Add(OwnerId);
					}
				}
				else
				{
					AllOwnerIds = AllOwnerIdsTemp;
				}

				this.Priv = priv;
				this.StoredBalance = (PLUGIN.config.Settings.UseEconomics || PLUGIN.config.Settings.UseRP) ? 0 : Priv.inventory.GetAmount(PLUGIN.config.Settings.CurrencyItemId, false);
				this.PlayersViewing = new HashSet<BasePlayer>();
				this.ContentsLocked = false;
				this.AllOwnerIds = AllOwnerIds;
				this.IsActive = false;
				this.ForcedProtectionLevel = false;
				UpdateHighestProtectionLevel();
				UpdateTotalBuildingCost();
				UpdateProtectionCostPerHour();
				UpdateStatus();
			}

			public void UpdateHighestProtectionLevel()
			{
				if (Priv != null)
				{
					if (ForcedProtectionLevel == false)
					{
						if (AllOwnerIds != null)
						{
							int level = AllOwnerIds.Count == 0 ? -1 : (from id in (from ownerid in AllOwnerIds select ownerid.ToString()) select ProtectionLevel.LevelOf(id)).Max();
							HighestProtectionLevel = level == -1 ? ProtectionLevel.NONE : PLUGIN.config.Protection.ProtectionLevels[level];
							HourlyBaseCost = HighestProtectionLevel.HourlyBaseCost ?? PLUGIN.config.Pricing.DefaultBasePrice;
							HourlyBuildingCost = HighestProtectionLevel.HourlyBuildingCost ?? PLUGIN.config.Pricing.DefaultBuildingCost;
							CostPerDamage = HighestProtectionLevel.CostPerDamage ?? PLUGIN.config.Pricing.DefaultCostPerDamage;
							PLUGIN.Debug($"TC with ID={Priv.net.ID} HighestProtectionLevel={HighestProtectionLevel.OnlineProtectionPercentage}:{HighestProtectionLevel.OfflineProtectionPercentage}");
							PLUGIN.RefreshUi(this);
						}
					}
				}
			}

			public void UpdateProtectionCostPerHour()
			{
				if (Priv != null)
				{
					if (HasAnyProtection)
					{
						ProtectionCostPerHour = HourlyBaseCost + TotalBuildingCost;
						PLUGIN.RefreshUi(this);
					}
					else
					{
						ProtectionCostPerHour = 0;
						PLUGIN.RefreshUi(this);
					}
				}
			}

			public void UpdateTotalBuildingCost()
			{
				PLUGIN.DebugTimeStart("UpdateTotalBuildingCost");
				if (Priv != null)
				{
					BuildingManager.Building building = Priv.GetBuilding();
					if (building != null)
					{
						if (PLUGIN.UseMaterialPrices)
						{
							TotalBuildingCost = 0;
							foreach (BuildingBlock block in building.buildingBlocks)
							{
								if (block.ShortPrefabName.Contains("foundation") || block.ShortPrefabName.Contains("floor"))
								{
									float mod = block.ShortPrefabName.Contains("triangle") ? 0.5f : 1f;
									int rank = (int)block.grade;
									if (rank >= 0)
									{
										TotalBuildingCost += PLUGIN.config.Pricing.MaterialMultipliers.Values.ElementAt(rank) * HourlyBuildingCost * mod;
									}
								}
							}
						}
						else
						{
							TotalBuildingCost = 0;
							foreach (BuildingBlock block in building.buildingBlocks)
							{
								if (block.ShortPrefabName.Contains("foundation") || block.ShortPrefabName.Contains("floor"))
								{
									float mod = block.ShortPrefabName.Contains("triangle") ? 0.5f : 1f;
									TotalBuildingCost += HourlyBuildingCost * mod;
								}
							}
						}
					}
				}
				PLUGIN.DebugTimeEnd("UpdateTotalBuildingCost");
			}

			public void InitialPurchaseProtection()
			{
				if (Priv != null)
				{
					if (HasPermission && ((HasOnlineProtection && HasOwnersOnline) || (HasOfflineProtection && !HasOwnersOnline)))
					{
						if (RecentlyDamaged)
						{
							UpdateStatus();
						}
						else if (Priv != null && CanAffordProtection && !IsActive)
						{
							IsActive = true;
							UpdateCostDebt(ProtectionCostPerInterval, 1);
							UpdateStoredBalance(ProtectionCostPerInterval);
							UpdateStatus();
							CollectionLoop();
						}
					}
				}
			}

			public void PurchaseProtection()
			{
				if (Priv != null)
				{
					if (HasPermission && ((HasOnlineProtection && HasOwnersOnline) || (HasOfflineProtection && !HasOwnersOnline)))
					{
						PLUGIN.DebugTimeStart("PurchaseProtection");
						if (RecentlyDamaged)
						{
							UpdateStatus();
							PLUGIN.DebugTimeEnd("PurchaseProtection");
						}
						else if (Priv != null && CanAffordProtection && !IsActive)
						{
							IsActive = true;
							UpdateCostDebt(ProtectionCostPerInterval);
							UpdateStoredBalance(ProtectionCostPerInterval);
							UpdateStatus();
							CollectionLoop();
						}
					}
				}
			}

			public void StopProtection()
			{
				if (Priv != null && IsActive)
				{
					IsActive = false;
					UpdateStatus();
					PLUGIN.Debug($"Stopping protection for TC with ID={Priv.net.ID}");
					PLUGIN.ShowIndicatorForOwners(this);
				}
			}

			private void CollectionLoop()
			{
				if (Priv != null && IsActive)
				{
					PLUGIN.timer.In(PLUGIN.COLLECTION_INTERVAL, () =>
					{
						if (Priv != null && CanAffordProtection && IsActive)
						{
							IsActive = false;
							PurchaseProtection();
						}
						else
						{
							StopProtection();
						}
					});
				}
			}

			public void UpdateCostDebt(float amount, int minAmount = 0)
			{
				if (Priv != null && !PLUGIN.config.Settings.UseEconomics && !PLUGIN.config.Settings.UseRP)
				{
					ContentsLocked = true;
					CostDebt += Math.Max(minAmount, amount);
					while (CostDebt >= 1)
					{
						Priv.inventory.FindItemByItemID(PLUGIN.config.Settings.CurrencyItemId).UseItem(1);
						CostDebt -= 1;
					}
					ContentsLocked = false;
					PLUGIN.RefreshUi(this);
				}
			}


			public void UpdateStoredBalance(float amount)
			{
				if (Priv != null && !PLUGIN.config.Settings.UseEconomics && !PLUGIN.config.Settings.UseRP)
				{
					float remainder = (float)(StoredBalance - Math.Truncate(StoredBalance));
					StoredBalance = ItemAmount + remainder;
				}
				else
				{
					StoredBalance = Math.Max(StoredBalance - amount, 0);
				}
			}

			public void UpdateStatus()
			{
				if (Priv != null)
				{
					if (!HasPermission)
					{
						Status = ProtectionStatus.Unprotected;
					}
					else if (HasOnlineProtection && HasOwnersOnline && RecentlyDamaged && Status != ProtectionStatus.Protected && CanAffordProtection)
					{
						Status = ProtectionStatus.PendingRecentlyDamage;
						PLUGIN.Debug($"TC with ID={Priv.net.ID} recently took damage, protection starts soon");
						PLUGIN.timer.In(PLUGIN.config.Protection.ProtectedDelayAfterTakingDamage, () =>
						{
							PurchaseProtection();
							UpdateStatus();
							PLUGIN.ShowIndicatorForOwners(this);
						});
					}
					else if (HasOfflineProtection && !HasOwnersOnline && RecentlyDamaged && Status != ProtectionStatus.Protected && CanAffordProtection)
					{
						Status = ProtectionStatus.PendingRecentlyDamage;
						PLUGIN.Debug($"TC with ID={Priv.net.ID} recently took damage, protection starts soon");
						PLUGIN.timer.In(PLUGIN.config.Protection.ProtectedDelayAfterTakingDamage, () =>
						{
							PurchaseProtection();
							UpdateStatus();
							PLUGIN.ShowIndicatorForOwners(this);
						});
					}
					else if (HasOfflineProtection && !HasOnlineProtection && HasOwnersOnline)
					{
						Status = ProtectionStatus.PendingOfflineOnly;
					}
					else if (!HasOfflineProtection && HasOnlineProtection && !HasOwnersOnline)
					{
						Status = ProtectionStatus.PendingOnlineOnly;
					}
					else if (IsActive)
					{
						Status = ProtectionStatus.Protected;
					}
					else
					{
						Status = ProtectionStatus.Unprotected;
					}
					PLUGIN.RefreshUi(this);
					PLUGIN.Debug($"TC with ID={Priv.net.ID} Status={Status} StoredBalance={StoredBalance}");
				}
			}

			public static void Unload()
			{
				toolcupboards = null;
			}
		}

		#endregion Classes

		#region Configuration
		private Configuration config;

		private class Configuration
		{
			[JsonProperty(PropertyName = "General settings")]
			public GeneralConfig Settings = new GeneralConfig();

			[JsonProperty(PropertyName = "Protection settings")]
			public ProtectionConfig Protection = new ProtectionConfig();

			[JsonProperty(PropertyName = "Cost settings")]
			public PricingConfig Pricing = new PricingConfig();

			[JsonProperty(PropertyName = "Indicator settings")]
			public IndicatorConfig Indicator = new IndicatorConfig();

			public class GeneralConfig
			{
				[JsonProperty(PropertyName = "Currency item ID (if not using economics)")]
				public int CurrencyItemId = -932201673;

				[JsonProperty(PropertyName = "Use economics balance (requires economics plugin)")]
				public bool UseEconomics = false;

				[JsonProperty(PropertyName = "Use reward points (requires server rewards plugin)")]
				public bool UseRP = false;
			}

			public class ProtectionConfig
			{
				[JsonProperty(PropertyName = "Delay after taking damage (seconds)")]
				public int ProtectedDelayAfterTakingDamage { get; set; } = 10;

				[JsonProperty(PropertyName = "Delay for offline protection (seconds)")]
				public int OfflineProtectionDelay { get; set; } = 600;

				[JsonProperty(PropertyName = "Protect buildings and deployables (true/false)")]
				public bool ProtectBuildings { get; set; } = true;

				[JsonProperty(PropertyName = "Protect players (true/false)")]
				public bool ProtectPlayers { get; set; } = true;

				[JsonProperty(PropertyName = "Protect vehicles and horses (true/false)")]
				public bool ProtectVehicles { get; set; } = true;

				[JsonProperty(PropertyName = "Protect traps and electronics (true/false)")]
				public bool ProtectTraps { get; set; } = true;

				[JsonProperty(PropertyName = "Protection levels")]
				public Dictionary<int, ProtectionLevel> ProtectionLevels = new Dictionary<int, ProtectionLevel>
				{
					{
						0, new ProtectionLevel {
							OnlineProtectionPercentage = 100f,
							OfflineProtectionPercentage = 100f
						}
					}
				};
			}

			public class PricingConfig
			{
				[JsonProperty(PropertyName = "Material cost multipliers")]
				public Dictionary<string, float> MaterialMultipliers { get; set; } = new Dictionary<string, float> {
					{ "twig", 1.0f },
					{ "wood", 1.0f },
					{ "stone", 1.0f },
					{ "metal", 1.0f },
					{ "armored", 1.0f }
				};

				[JsonProperty(PropertyName = "Default hourly building cost")]
				public float DefaultBuildingCost { get; set; } = 1.0f;

				[JsonProperty(PropertyName = "Default hourly base cost")]
				public float DefaultBasePrice { get; set; } = 9f;

				[JsonProperty(PropertyName = "Default structure damage cost")]
				public float DefaultCostPerDamage { get; set; } = 0f;
			}

			public class IndicatorConfig
			{
				[JsonProperty(PropertyName = "Enabled")]
				public bool Enabled { get; set; } = true;

				[JsonProperty(PropertyName = "Persistent")]
				public bool Persistent { get; set; } = false;

				[JsonProperty(PropertyName = "Image url")]
				public string ImageUrl { get; set; } = "https://i.imgur.com/ue05FGg.png";
			}
		}

		protected override void LoadConfig()
		{
			LoadDefaultConfig();
			base.LoadConfig();

			try
			{
				config = Config.ReadObject<Configuration>();
				if (config == null) throw new Exception();
			}
			catch
			{
				PrintError("Your configuration file contains an error. Default configuration values will be used. It is recommended to backup your current configuration file and remove it to generate a fresh one.");
				LoadDefaultConfig();
			}
			CurrencyItemDef = ItemManager.FindItemDefinition(config.Settings.CurrencyItemId);
			UseMaterialPrices = config.Pricing.MaterialMultipliers.Values.Any(x => x != 1f);
		}

		protected override void SaveConfig() => Config.WriteObject(config);

		protected override void LoadDefaultConfig() => config = new Configuration();

		#endregion Configuration

		#region Localization

		private string Label(string message, string color)
		{
			return $"<color={color}>{message}: </color>";
		}

		private string Color(string message, string color)
		{
			return $"<color={color}>{message}</color>";
		}

		protected override void LoadDefaultMessages()
		{
			lang.RegisterMessages(new Dictionary<string, string>
			{
				["Title"] = "RAID PROTECTION",
				["Cost"] = "Cost",
				["Balance"] = "Balance",
				["Level"] = "Level",
				["Protected"] = "PROTECTED",
				["Unprotected"] = "UNPROTECTED",

				["StatusProtected"] = "Your base is {0}% protected for {1}",
				["StatusUnprotected"] = "Balance of {0} is required to receive protection",
				["StatusPendingDamage"] = "Recently damaged, protection starts soon",
				["StatusPendingOffline"] = "Resuming {0}% protection when owners are offline",
				["StatusNoPermission"] = "You do not have permission to receive protection",
				["StatusNoBalance"] = "Low remaining balance, protection ends soon",

				["Days"] = "days",
				["Hour"] = "hr",
				["Hours"] = "hrs",
				["Minutes"] = "min",
				["Usage"] = "Usage",
				["NoPriv"] = "There is no protected area at the current location",
				["NoPerm"] = "You do not have permission to use that command",
				["ForceLevel"] = "The protection level for this protected area will be forced to level {0}. It will remain at this level until reset with the {1} command.",
				["ResetLevel"] = "The protection level for this protected area will default to the highest protection level among authorized players.",
				["NoLevel"] = "No protection level defined in the config for level {0}",
				["Activate"] = "Activating protection for protected area",
				["Deactive"] = "Deactivating protection for protected area",

				["LabelStatus"] = "Status",
				["LabelLevel"] = "Level",
				["LabelProtection"] = "Protection",
				["LabelDelay"] = "Offline Protection Delay",
				["LabelBalance"] = "Balance",
				["LabelCost"] = "Cost Per Hour",
				["LabelCostPerDamage"] = "Cost Per Damage Taken",
				["LabelTime"] = "Hours Left",
				["LabelOnline"] = "Online Players",
				["LabelId"] = "Toolcupboard ID",

				["ValueStatusProtected"] = "{0}% Protected",
				["ValueStatusUnprotected"] = "Unprotected",
				["ValueProtection"] = "{0}% Online | {1}% Offline",
				["ValueCost"] = "{0} Base + {1} Building = {2} Total",

				["Deposit"] = "Deposit",
				["Wallet"] = "Wallet",
				["Wallet"] = "Wallet",

				["IndicatorProtected"] = "{0}% PROTECTED",
				["IndicatorUnprotected"] = "UNPROTECTED",
				["IndicatorWarning"] = "Damage dealt deducts from the protection balance",
			}, this);
		}

		private string Lang(string key, string id = null, params object[] args)
		{
			return string.Format(lang.GetMessage(key, this, id), args);
		}
		#endregion Localization

		#region UI

		private readonly string COLOR_TRANSPARENT = "0 0 0 0";
		private readonly string COLOR_GREEN = "0.749 0.9059 0.4783 1";
		private readonly string COLOR_RED = "1 0.529 0.180 1";

		private readonly string COLOR_GREEN_DARK = "0.5992 0.72472 0.38264 1";
		private readonly string COLOR_GREEN_DARK_LESS = "0.25 0.4 0.1 1";
		private readonly string COLOR_RED_DARK = "0.8 0.4232 0.144 1";
		private readonly string COLOR_RED_DARK_LESS = "0.8 0.25 0.144 1";

		private readonly string COLOR_YELLOW = "1 1 0.5 1";

		private void RefreshUi(ProtectedCupboard tc)
		{
			foreach (BasePlayer player in tc.PlayersViewing)
			{
				OpenUi(player, tc.Priv);
			}
		}
		private void OpenUi(BasePlayer player, BuildingPrivlidge priv)
		{
			ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
			if (tc != null)
			{

				/* Constant */
				string titleColor = "1 1 1 0.75";
				string textColor = "1 1 1 0.6";
				int titleFont = 11;
				int textFont = 10;
				CuiElementContainer container = new CuiElementContainer
			{
				new CuiElement
				{
					Name = "srpPanel",
					Parent = "Overlay",
					Components = {
					new CuiImageComponent {
						Color=COLOR_TRANSPARENT
					},
					new CuiRectTransformComponent {
						AnchorMin="0.650 0.022",
						AnchorMax="0.828 0.135"
					}
				}
				},
				new CuiElement
				{
					Name = "srpTitle",
					Parent = "srpPanel",
					Components = {
					new CuiImageComponent {
						Color="0.3786 0.3686 0.3686 0.5"
					},
					new CuiRectTransformComponent {
						AnchorMin="0 0.80",
						AnchorMax="1 1"
					}
				}
				},
				new CuiElement
				{
					Name = "srpTitleText",
					Parent = "srpTitle",
					Components = {
					new CuiTextComponent {
						Text = Lang("Title", player.UserIDString),
						Align = TextAnchor.MiddleLeft,
						Color = titleColor,
						FontSize = titleFont
					},
					new CuiRectTransformComponent {
						AnchorMin="0.03 0.0",
						AnchorMax="0.97 1.0"
					}
				}
				},
				new CuiElement
				{
					Name = "srpBody",
					Parent = "srpPanel",
					Components = {
					new CuiImageComponent {
						Color="0.3786 0.3686 0.3686 0.5"
					},
					new CuiRectTransformComponent {
						AnchorMin="0 0",
						AnchorMax="1 0.75"
					}
				}
				},
				new CuiElement
				{
					Name = "srpBodyText1",
					Parent = "srpBody",
					Components = {
					new CuiTextComponent {
						Text = $"{(tc.Status == ProtectionStatus.Protected ? Lang("Protected", player.UserIDString) : Lang("Unprotected", player.UserIDString))}",
						Align = TextAnchor.MiddleCenter,
						Color = (tc.Status == ProtectionStatus.Protected ? COLOR_GREEN : COLOR_RED),
						FontSize = titleFont
					},
					new CuiRectTransformComponent {
						AnchorMin="0.03 0.7",
						AnchorMax="0.97 0.9"
					}
				}
				},
				new CuiElement
				{
					Name = "srpBodyText2",
					Parent = "srpBody",
					Components = {
					new CuiTextComponent {
						Text = GetStatusMessage(player, tc),
						Align = TextAnchor.MiddleCenter,
						Color = (tc.Status == ProtectionStatus.Protected && tc.CanAffordProtection ? COLOR_GREEN : COLOR_RED),
						FontSize = textFont
					},
					new CuiRectTransformComponent {
						AnchorMin="0.03 0.3",
						AnchorMax="0.97 0.7"
					}
				}
				},
				new CuiElement
				{
					Name = "srpBodyText3a",
					Parent = "srpBody",
					Components = {
					new CuiTextComponent {
						Text = $"{Lang("Cost", player.UserIDString)}: {tc.ProtectionCostPerHour}/{Lang("Hour", player.UserIDString)}",
						Align = TextAnchor.MiddleLeft,
						Color = textColor,
						FontSize = textFont
					},
					new CuiRectTransformComponent {
						AnchorMin="0.03 0.1",
						AnchorMax="0.97 0.3"
					}
				}
				},
				new CuiElement
				{
					Name = "srpBodyText3b",
					Parent = "srpBody",
					Components = {
					new CuiTextComponent {
						Text = $"{Lang("Balance", player.UserIDString)}: {Math.Round(tc.StoredBalance, 2)}",
						Align = TextAnchor.MiddleCenter,
						Color = textColor,
						FontSize = textFont
					},
					new CuiRectTransformComponent {
						AnchorMin="0.03 0.1",
						AnchorMax="0.97 0.3"
					}
				}
				},
				new CuiElement
				{
					Name = "srpBodyText3c",
					Parent = "srpBody",
					Components = {
					new CuiTextComponent {
						Text = $"{Lang("Level", player.UserIDString)}: {tc.HighestProtectionLevel}",
						Align = TextAnchor.MiddleRight,
						Color = textColor,
						FontSize = textFont
					},
					new CuiRectTransformComponent {
						AnchorMin="0.03 0.1",
						AnchorMax="0.97 0.3"
					}
				}
				}
			};
				if (PLUGIN.config.Settings.UseEconomics || PLUGIN.config.Settings.UseRP)
				{
					double balance = GetBalance(player.userID);
					container.Add(new CuiElement
					{
						Name = "srpEconPanel",
						Parent = "Overlay",
						Components = {
					new CuiImageComponent {
						Color="0.3786 0.3686 0.3686 0.5"
					},
					new CuiRectTransformComponent {
						AnchorMin="0.650 0.005",
						AnchorMax="0.829 0.021"
					}
				}
					});
					container.Add(new CuiButton
					{
						Button = {
						Command = $"srp.balance.decrement {priv.net.ID}",
						Color = textColor
					},
						Text = {
						Text = "<",
						Align = TextAnchor.MiddleCenter,
						FontSize = 8,
					},
						RectTransform = {
						AnchorMin = "0.37 0.3",
						AnchorMax = "0.43 0.9"
					}
					}, "srpEconPanel", "srpEconSub");
					container.Add(new CuiButton
					{
						Button = {
						Command = $"srp.balance.increment {priv.net.ID}",
						Color = textColor
					},
						Text = {
						Text = ">",
						Align = TextAnchor.MiddleCenter,
						FontSize = 8,
					},
						RectTransform = {
						AnchorMin = "0.57 0.3",
						AnchorMax = "0.63 0.9"
					}
					}, "srpEconPanel", "srpEconAdd");
					container.Add(new CuiElement
					{
						Name = "srpEconLabel",
						Parent = "srpEconPanel",
						Components = {
					new CuiTextComponent {
						Text = $"{economyPanel.Get(player.UserIDString)}",
						Align = TextAnchor.MiddleCenter,
						FontSize = 8,
						Color = textColor
					},
					new CuiRectTransformComponent {
						AnchorMin="0.45 0.2",
						AnchorMax="0.55 1.0"
					}
				}
					});
					container.Add(new CuiButton
					{
						Button = {
							Command = $"srp.balance.deposit {priv.net.ID}",
							Color = balance >= economyPanel.Get(player.UserIDString) && economyPanel.Get(player.UserIDString) != 0 ? COLOR_GREEN_DARK : textColor
						},
						Text = {
							Text = $"{Lang("Deposit", player.UserIDString)}",
							Align = TextAnchor.MiddleCenter,
							FontSize = 7,
						},
						RectTransform = {
							AnchorMin = "0.67 0.2",
							AnchorMax = "0.81 1.0"
						}
					}, "srpEconPanel", "srpEconDeposit");
					container.Add(new CuiButton
					{
						Button = {
							Command = $"srp.balance.withdraw {priv.net.ID}",
							Color = tc.StoredBalance >= economyPanel.Get(player.UserIDString) && economyPanel.Get(player.UserIDString) != 0 ? COLOR_RED_DARK : textColor
						},
						Text = {
							Text = $"{Lang("Withdraw", player.UserIDString)}",
							Align = TextAnchor.MiddleCenter,
							FontSize = 7,
						},
						RectTransform = {
							AnchorMin = "0.83 0.2",
							AnchorMax = "0.97 1.0"
						}
					}, "srpEconPanel", "srpEconWithdraw");
					container.Add(new CuiElement
					{
						Name = "srpEconBalance",
						Parent = "srpEconPanel",
						Components = {
					new CuiTextComponent {
						Text = $"{Lang("Wallet", player.UserIDString)}: {FormatCurrency(GetBalance(player.userID))}",
						Align = TextAnchor.MiddleLeft,
						FontSize = 8,
						Color = textColor
					},
					new CuiRectTransformComponent {
						AnchorMin="0.035 0.3",
						AnchorMax="0.3 1.0"
					}
				}
					});
				}
				CuiHelper.DestroyUi(player, "srpPanel");
				CuiHelper.DestroyUi(player, "srpEconPanel");
				CuiHelper.AddUi(player, container);
			}
		}

		private void CloseUi(BasePlayer player)
		{
			CuiHelper.DestroyUi(player, "srpPanel");
			CuiHelper.DestroyUi(player, "srpEconPanel");
		}

		private void CloseIndicator(BasePlayer player)
		{
			if (player != null)
				CuiHelper.DestroyUi(player, "srpIndicator");

			if (!config.Indicator.Persistent)
			{
				timer.In(3f, () =>
				{
					if (player != null && onCooldown.Contains(player.userID))
						onCooldown.Remove(player.userID);
				});
			}
		}

		private void ShowIndicatorForOwners(ProtectedCupboard tc)
		{
			if (config.Indicator.Enabled)
			{
				foreach (BasePlayer player in tc.OnlineOwners)
				{
					if (player.GetBuildingPrivilege() == tc.Priv)
					{
						PLUGIN.ShowIndicator(player, tc.Priv, false, true);
					}
				}
			}
		}

		private void ShowIndicator(BasePlayer player, BuildingPrivlidge priv, bool fromDamage = false, bool ignoreCooldown = false, bool isPersistent = false)
		{
			if (config.Indicator.Enabled && priv != null && player != null && (ignoreCooldown || !onCooldown.Contains(player.userID)) && ((config.Indicator.Persistent && isPersistent) || !config.Indicator.Persistent))
			{
				ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
				string anchorMin = "0.8400 0.875";
				string anchorMax = "0.9885 0.940";
				string textColor = tc.Status == ProtectionStatus.Protected ? COLOR_GREEN : COLOR_YELLOW;
				if (tc != null)
				{
					string panelColor = tc.Status == ProtectionStatus.Protected ? COLOR_GREEN_DARK_LESS : COLOR_RED_DARK_LESS;
					CuiElementContainer container = new CuiElementContainer
				{
					new CuiElement
					{
						Name = "srpIndicator",
						Parent = "Hud",
						Components = {
						new CuiImageComponent {
							Color = COLOR_TRANSPARENT
						},
						new CuiRectTransformComponent {
							AnchorMin = anchorMin,
							AnchorMax = anchorMax
						}
					}},
					new CuiElement
					{
						Name = "srpIndicatorMain",
						Parent = "srpIndicator",
						Components = {
						new CuiImageComponent {
							Color = panelColor
						},
						new CuiRectTransformComponent {
							AnchorMin = "0 0.4",
							AnchorMax = "1 1.0"
						}
					}},
					new CuiElement
					{
						Name = "srpIndicatorText",
						Parent = "srpIndicatorMain",
						Components = {
							new CuiTextComponent {
								Text = tc.Status == ProtectionStatus.Protected ? Lang("IndicatorProtected", player.UserIDString, tc.ProtectionPercentage) : Lang("IndicatorUnprotected", player.UserIDString),
								Align = TextAnchor.MiddleCenter,
								Color = textColor,
								FontSize = 12
							},
							new CuiRectTransformComponent {
								AnchorMin = "0.2 0",
								AnchorMax = "0.8 1"
							}}
						}
					};
					if (ImageLibrary)
						container.Add(new CuiElement
						{
							Name = "srpIndicatorImg",
							Parent = "srpIndicatorMain",
							Components = {
							new CuiRawImageComponent {
								Png = ImageLibrary?.Call<string>("GetImage", "SrpIndicatorIcon"),
							},
							new CuiRectTransformComponent {
								AnchorMin = "0.05 0.1",
								AnchorMax = "0.175 0.9"
							}
						}
						});
					if (tc.Status == ProtectionStatus.Protected && tc.CostPerDamage > 0 && fromDamage)
					{
						container.Add(
							new CuiElement
							{
								Name = "srpIndicatorSub",
								Parent = "srpIndicator",
								Components = {
						new CuiImageComponent {
							Color = COLOR_RED_DARK_LESS
						},
						new CuiRectTransformComponent {
							AnchorMin = "0 0",
							AnchorMax = "1 0.35"
						}}
							}
						);
						container.Add(new CuiElement
						{
							Name = "srpIndicatorSubText",
							Parent = "srpIndicatorSub",
							Components = {
						new CuiTextComponent {
							Text = Lang("IndicatorWarning", player.UserIDString),
							Align = TextAnchor.MiddleCenter,
							Color = COLOR_YELLOW,
							FontSize = 8
						},
						new CuiRectTransformComponent {
							AnchorMin = "0 0",
							AnchorMax = "1 1"
						}}
						});
					}
					CuiHelper.DestroyUi(player, "srpIndicator");
					CuiHelper.AddUi(player, container);
					if (!isPersistent)
					{
						onCooldown.Add(player.userID);
						timer.In(3f, () =>
						{
							CloseIndicator(player);
						});
					}
				}
				else
				{
					CuiHelper.DestroyUi(player, "srpIndicator");
				}
			}
		}

		#endregion UI

		#region Commands
		[Command("protection")]
		private void cmd_protection(IPlayer player, string command, string[] args)
		{
			BuildingPrivlidge priv;
			BasePlayer basePlayer = null;
			int mode = 0;
			string labelColor = "#b5b5b5";

			if (args.Length == 0)
			{
				basePlayer = BasePlayer.FindByID(ulong.Parse(player.Id));
				if (basePlayer == null)
				{
					player.Reply($"{Lang("NoPriv", player.Id)}");
					return;
				}
				priv = basePlayer.GetBuildingPrivilege();
			}
			else
			{
				if (!permission.UserHasPermission(player.Id, PermissionAdmin))
				{
					player.Reply($"{Lang("NoPerm", player.Id)}");
					return;
				}
				mode = 2;
				ulong id = ulong.Parse(args[0]);
				priv = (BuildingPrivlidge)BaseNetworkable.serverEntities.Find(new NetworkableId(id));
			}
			StringBuilder sb = new StringBuilder();
			ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
			if (tc != null)
			{
				sb.AppendLine($"<size=20><color=#c4341d>R</size><size=16>aid </size><size=20>P</size><size=16>rotection</color></size>");
				if (permission.UserHasPermission(player.Id, PermissionAdmin))
				{
					mode = 2;
				}
				else if (basePlayer != null & tc.AllOwnerIds.Contains(basePlayer.userID))
				{
					mode = 1;
				}
				if (mode >= 0)
				{
					sb.AppendLine($"{Label(Lang("LabelStatus", player.Id), labelColor)}{(tc.Status == ProtectionStatus.Protected ? Lang("ValueStatusProtected", player.Id, tc.ProtectionPercentage) : Lang("ValueStatusUnprotected", player.Id))}");
				}
				if (mode >= 1)
				{
					sb.AppendLine($"{Label(Lang("LabelLevel", player.Id), labelColor)}{tc.HighestProtectionLevel}");
					sb.AppendLine($"{Label(Lang("LabelProtection", player.Id), labelColor)}{Lang("ValueProtection", player.Id, tc.HighestProtectionLevel.OnlineProtectionPercentage, tc.HighestProtectionLevel.OfflineProtectionPercentage)}");
					sb.AppendLine($"{Label(Lang("LabelDelay", player.Id), labelColor)}{config.Protection.OfflineProtectionDelay}");
					sb.AppendLine($"{Label(Lang("LabelBalance", player.Id), labelColor)}{Math.Round(tc.StoredBalance, 2)}");
					sb.AppendLine($"{Label(Lang("LabelCost", player.Id), labelColor)}{Lang("ValueCost", player.Id, tc.HourlyBaseCost, tc.TotalBuildingCost, tc.ProtectionCostPerHour)}");
					sb.AppendLine($"{Label(Lang("LabelCostPerDamage", player.Id), labelColor)}{tc.CostPerDamage}");
					sb.AppendLine($"{Label(Lang("LabelTime", player.Id), labelColor)}{Math.Round(tc.HoursRemaining, 1)}");
					sb.AppendLine($"{Label(Lang("LabelOnline", player.Id), labelColor)}{tc.OnlineOwners.Count}/{tc.AllOwnerIds.Count}");
				}
				if (mode >= 2)
				{
					sb.AppendLine($"{Label(Lang("LabelId", player.Id), labelColor)}{tc.Priv.net.ID}");
					sb.AppendLine($"{Label(Lang("LabelStatus", player.Id), labelColor)}{tc.Status}");
				}
				player.Reply(sb.ToString());
			}
		}

		/* This command is used only by the UI and will not be documented */
		[Command("srp.balance.increment")]
		private void cmd_balance_increment(IPlayer player, string command, string[] args)
		{
			ulong id;
			if (args.Length > 0 && ulong.TryParse(args[0], out id))
			{
				var nid = new NetworkableId(id);
				economyPanel.Increment(player.Id, config.Settings.UseRP ? 1 : 10);
				BasePlayer basePlayer = BasePlayer.FindByID(ulong.Parse(player.Id));
				BuildingPrivlidge priv = (BuildingPrivlidge)BaseNetworkable.serverEntities.Find(nid);
				if (basePlayer != null && priv != null)
				{
					OpenUi(basePlayer, (BuildingPrivlidge)BaseNetworkable.serverEntities.Find(nid));
				}
			}
		}

		/* This command is used only by the UI and will not be documented */
		[Command("srp.balance.decrement")]
		private void cmd_balance_decrement(IPlayer player, string command, string[] args)
		{
			ulong id;
			if (args.Length > 0 && ulong.TryParse(args[0], out id))
			{
				var nid = new NetworkableId(id);
				economyPanel.Decrement(player.Id, config.Settings.UseRP ? 1 : 10);
				BasePlayer basePlayer = BasePlayer.FindByID(ulong.Parse(player.Id));
				BuildingPrivlidge priv = (BuildingPrivlidge)BaseNetworkable.serverEntities.Find(nid);
				if (basePlayer != null && priv != null)
				{
					OpenUi(basePlayer, (BuildingPrivlidge)BaseNetworkable.serverEntities.Find(nid));
				}
			}
		}

		/* This command is used only by the UI and will not be documented */
		[Command("srp.balance.deposit")]
		private void cmd_balance_deposit(IPlayer player, string command, string[] args)
		{
			ulong id;
			if (player != null && args.Length > 0 && ulong.TryParse(args[0], out id))
			{
				var nid = new NetworkableId(id);
				float amount = economyPanel.Get(player.Id);
				ulong playerId = ulong.Parse(player.Id);
				if ((config.Settings.UseEconomics || config.Settings.UseRP) && GetBalance(playerId) >= amount)
				{
					TakeBalance(playerId, amount);
					BuildingPrivlidge priv = (BuildingPrivlidge)BaseNetworkable.serverEntities.Find(nid);
					ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
					if (tc != null)
					{
						tc.StoredBalance += amount;
						if (tc.HasOnlineProtection)
						{
							tc.PurchaseProtection();
						}
						tc.UpdateStatus();
						BasePlayer basePlayer = BasePlayer.FindByID(ulong.Parse(player.Id));
						if (basePlayer != null && priv != null)
						{
							economyPanel.Reset(player.Id);
							OpenUi(basePlayer, (BuildingPrivlidge)BaseNetworkable.serverEntities.Find(nid));
						}
					}
				}
			}
		}

		/* This command is used only by the UI and will not be documented */
		[Command("srp.balance.withdraw")]
		private void cmd_balance_withdraw(IPlayer player, string command, string[] args)
		{
			ulong id;
			if (player != null && args.Length > 0 && ulong.TryParse(args[0], out id))
			{
				var nid = new NetworkableId(id);
				float amount = economyPanel.Get(player.Id);
				BuildingPrivlidge priv = (BuildingPrivlidge)BaseNetworkable.serverEntities.Find(nid);
				ProtectedCupboard tc = ProtectedCupboard.InitCupboard(priv);
				if (tc != null && (config.Settings.UseEconomics || config.Settings.UseRP) && priv != null && tc.StoredBalance >= amount)
				{
					GiveBalance(ulong.Parse(player.Id), amount);
					tc.StoredBalance -= amount;
					tc.UpdateStatus();
					BasePlayer basePlayer = BasePlayer.FindByID(ulong.Parse(player.Id));
					if (basePlayer != null && priv != null)
					{
						economyPanel.Reset(player.Id);
						OpenUi(basePlayer, (BuildingPrivlidge)BaseNetworkable.serverEntities.Find(nid));
					}
				}
			}
		}

		/* This command is used only by the UI and will not be documented */

		[Command("srp.test.online"), Permission(PermissionAdmin)]
		private void cmd_test_online(IPlayer player, string command, string[] args)
		{
			BasePlayer basePlayer = BasePlayer.FindByID(ulong.Parse(player.Id));
			_onlinePlayers = new HashSet<BasePlayer>() { basePlayer };
			foreach (ProtectedCupboard tc in ProtectedCupboard.toolcupboards.Values)
			{
				tc.UpdateStatus();
			}
			player.Reply("Testing online protection");
			HandleUserConnected(player);
		}

		[Command("srp.test.offline"), Permission(PermissionAdmin)]
		private void cmd_test_offline(IPlayer player, string command, string[] args)
		{
			_onlinePlayers = new HashSet<BasePlayer>() { };
			foreach (ProtectedCupboard tc in ProtectedCupboard.toolcupboards.Values)
			{
				tc.UpdateStatus();
			}
			player.Reply("Testing offline protection");
			HandleUserDisconnected(player);
		}

		#endregion Commands

		#region Debugging

		private const bool _debugging = false;

		private HashSet<BasePlayer> _onlinePlayers = new HashSet<BasePlayer>();

		private Dictionary<string, long> _startTimes = new Dictionary<string, long>();

		private HashSet<string> _trackedHooks = new HashSet<string>() {
			"ProtectFromDamage",
			"OnLootNetworkUpdate",
			"HandleUserDisconnected",
			"HandleUserConnected",
			"PurchaseProtection",
			"UpdateTotalBuildingCost"
		};

		private void Debug(string stmt)
		{
			if (_debugging)
			{
				Puts(stmt);
			}
		}

		private void DebugTimeStart(string function)
		{
			if (_debugging && _trackedHooks.Contains(function))
			{
				if (_startTimes.ContainsKey(function))
				{
					_startTimes[function] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				}
				else
				{
					_startTimes.Add(function, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
				}
			}
		}

		private void DebugTimeEnd(string function)
		{
			if (_debugging && _trackedHooks.Contains(function))
			{
				if (_startTimes.ContainsKey(function))
				{
					Puts($"{function} took {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _startTimes[function]}ms");
				}
			}
		}

		#endregion Debugging
	}
}
