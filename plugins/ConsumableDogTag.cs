using Oxide.Core;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using UnityEngine;
using Rust;
using Oxide.Plugins;


namespace Oxide.Plugins
{
    [Info("Consumable Dog Tag", "TTV OdsScott", "1.0.0")]
    [Description("Allows players to consume a dog tag and provide hooks for other plugins.")]
    public class ConsumableDogTag : RustPlugin
    {
        private Dictionary<ulong, float> lastUseTime = new Dictionary<ulong, float>();
        private const float COOLDOWN = 1.0f;

        void ConsumeDogTag(BasePlayer player, Item activeItem)
        {
            if (!lastUseTime.ContainsKey(player.userID) ||
               Time.realtimeSinceStartup - lastUseTime[player.userID] >= COOLDOWN)
            {
               /* if (activeItem.amount > 1)
                {
                    activeItem.amount = activeItem.amount - 1;
                }
                else
                {
                    activeItem.Remove();
                }*/

                Effect.server.Run("assets/bundled/prefabs/fx/repairbench/itemrepair.prefab", player.transform.position);
                // Update the last use time for this player
                lastUseTime[player.userID] = Time.realtimeSinceStartup;
                player.SendConsoleCommand("kits.gridview page 0 0 true");
                //Interface.CallHook("OnConsumedDogTag", player, activeItem);
                player.SendNetworkUpdate();
            }
        }

        void OnPlayerInput(BasePlayer player, InputState inputState)
        {
            var activeItem = player.GetActiveItem();

            // Check if the fire button is pressed
            if (activeItem != null &&
                activeItem.info.shortname.Contains("dogtag") &&
                inputState.IsDown(BUTTON.FIRE_PRIMARY))
            {
                ConsumeDogTag(player, activeItem);
            }
        }
    }
}
