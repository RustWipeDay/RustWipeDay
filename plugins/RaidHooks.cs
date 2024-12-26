using System;
using System.Collections.Generic;
using System.Linq;
using CompanionServer;
using Oxide.Core;
using ProtoBuf;
using UnityEngine;


namespace Oxide.Plugins
{
    [Info("Raid Hooks", "OdsScott", "1.0.0")]
    [Description("Create raid hooks for other plugins to use.")]
    internal class RaidHooks : RustPlugin
    {
        #region init, data and cleanup

        private static HashSet<ulong> disabled = new HashSet<ulong>();

        private void Init()
        {

        }

        #endregion

        private readonly Dictionary<string, DateTime> raidblocked = new Dictionary<string, DateTime>();
        private DateTime lastAttack = DateTime.Now;

        private object OnEntityTakeDamage(DecayEntity entity, HitInfo info)
        {
            return OnEntityDamage(entity, info);
        }

        private object OnEntityTakeDamage(BaseMountable entity, HitInfo info)
        {
            return OnEntityDamage(entity, info);
        }

        private object OnEntityTakeDamage(BasePlayer entity, HitInfo info)
        {
            return OnEntityDamage(entity, info);
        }

        private object OnEntityTakeDamage(IOEntity entity, HitInfo info)
        {
            return OnEntityDamage(entity, info);
        }

        private object OnEntityTakeDamage(BaseResourceExtractor entity, HitInfo info)
        {
            return OnEntityDamage(entity, info);
        }

        object OnEntityDamage(BaseEntity entity, HitInfo hitInfo)
        {
            if (hitInfo == null) return null;

            if (!IsRaidEntity(entity)) return null;
            if (hitInfo.InitiatorPlayer == null) return null;

            TimeSpan timesince = DateTime.Now - lastAttack;
            if (timesince.TotalSeconds < 5) return null;
            lastAttack = DateTime.Now;

            var buildingPrivilege = entity.GetBuildingPrivilege();
            if (buildingPrivilege == null || buildingPrivilege.authorizedPlayers.IsEmpty() || buildingPrivilege.authorizedPlayers.Select(playerNameId => playerNameId.userid).Contains(hitInfo.InitiatorPlayer.userID)) return null;

            Interface.CallHook("OnBuildingPrivilegeRaid", buildingPrivilege, hitInfo); 
            return null;
        }

        private static bool IsRaidEntity(BaseEntity entity)
        {
            if (entity is Door) return true;

            if (!(entity is BuildingBlock)) return false;

            return ((BuildingBlock) entity).grade != BuildingGrade.Enum.Twigs;
        }
    }
}
