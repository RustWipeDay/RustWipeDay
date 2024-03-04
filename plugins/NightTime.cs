using Oxide.Core.Plugins;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NightTime", "OdsScott", "0.1.0")]
    public class NightTime : RustPlugin
    {
        private bool hasAnnouncedSunset = false;
        private Timer sunsetCheckTimer;

        void OnServerInitialized()
        {
            sunsetCheckTimer = timer.Every(5f, () =>
            {
                CheckForSunset();
            });
            CheckForSunset();
        }

        private void CheckForSunset()
        {
            if (IsNightTime() && !hasAnnouncedSunset)
            {
                hasAnnouncedSunset = true;
                Server.Broadcast("The sun has set, gather rates have increased and raid protection has weakened until morning.");
            }
            else if (!IsNightTime())
            {
                hasAnnouncedSunset = false;
            }
        }

        bool IsNightTime()
        {
            return TOD_Sky.Instance.Cycle.Hour < 7 || TOD_Sky.Instance.Cycle.Hour > 18.5;
        }

        object OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            if (IsNightTime())
            {
                item.amount = (int)(2.5 * item.amount);
            }

            return null;
        }

        void Unload()
        {
            sunsetCheckTimer.Destroy();
        }
    }
}
