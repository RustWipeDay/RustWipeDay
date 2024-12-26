using System;
using HarmonyLib;

namespace Carbon.Plugins
{
    [Info("AlwaysHotspot", "TTV OdsScott", "1.0.0")]
    [Description("Always hit tree and ore hot spot")]

    public class AlwaysHotspot : CarbonPlugin
    {
        public override bool AutoPatch => true;

        [HarmonyPatch(typeof(TreeEntity), "DidHitMarker", new Type[] { typeof(HitInfo) })]
        public class TreeEntity_DidHitMarker
        {
            private static bool Prefix(HitInfo info, ref bool __result)
            {
                if (info != null)
                {
                    __result = true;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(OreResourceEntity), "OnAttacked", new Type[] { typeof(HitInfo) })]
        public class OreResourceEntity_OnAttacked
        {
            private static bool Prefix(HitInfo info, OreResourceEntity __instance)
            {
                if (info != null && __instance != null)
                {
                    if (__instance._hotSpot == null)
                    {
                        __instance._hotSpot = __instance.SpawnBonusSpot(info.HitPositionWorld);
                    }
                    if (__instance._hotSpot == null || __instance._hotSpot.transform == null)
                    {
                        return true;
                    }
                    __instance._hotSpot.transform.position = info.HitPositionWorld;
                    __instance._hotSpot.SendNetworkUpdateImmediate();
                }
                return true;
            }
        }
    }
}
