using System;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("SleepCommand", "Rust Wipe Day", "1.0.0")]
    public class SleepCommand : CovalencePlugin
    {
        private const int CombatCooldown = 180; // 3 minutes in seconds
        private System.Collections.Generic.Dictionary<string, DateTime> lastCombatTime = new System.Collections.Generic.Dictionary<string, DateTime>();

        // Hook into when a player takes damage
        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity is IPlayer player && info.Initiator is IPlayer)
            {
                lastCombatTime[player.Id] = DateTime.UtcNow;
            }
        }

        // Command to make the player sleep
        [Command("sleep")]
        private void OnSleepCommand(IPlayer player, string command, string[] args)
        {
            if (player.IsSleeping)
            {
                player.Reply("You are already sleeping.");
                return;
            }

            if (lastCombatTime.TryGetValue(player.Id, out DateTime lastCombat))
            {
                TimeSpan timeSinceCombat = DateTime.UtcNow - lastCombat;
                if (timeSinceCombat.TotalSeconds < CombatCooldown)
                {
                    player.Reply($"You must wait {CombatCooldown - (int)timeSinceCombat.TotalSeconds} more seconds since your last combat.");
                    return;
                }
            }

            (player.Object as BasePlayer)?.StartSleeping();
            player.Reply("You are now sleeping.");
        }
    }
}
