using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;


namespace Oxide.Plugins
{
    [Info("MapMarkerTeleport", "ODS", "1.0.0")]
    [Description("Allows players to teleport to wherever they click on the map after entering a command in the chat.")]

    class TeleportOnClick : RustPlugin
    {
        private const string PERMISSION_MAPMARKERTELEPORT_USE = "mapmarkerteleport.use";
        private Dictionary<ulong, double> teleportTimeout = new Dictionary<ulong, double>();

        private void OnServerInitialized()
        {
            permission.RegisterPermission(PERMISSION_MAPMARKERTELEPORT_USE, this);
        }

        [ChatCommand("tpclick")]
        private void TeleportOnClickCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_MAPMARKERTELEPORT_USE))
            {
                return;
            }

            player.ChatMessage("Set a marker anywhere on the map to teleport there.");

            teleportTimeout[player.userID] = Time.realtimeSinceStartup + 15.0;
        }

        private object OnMapMarkerAdd(BasePlayer player, MapNote note)
        {
            if (note.authorId == player.userID && teleportTimeout.ContainsKey(player.userID) && teleportTimeout[player.userID] < Time.realtimeSinceStartup)
            {
                teleportTimeout[player.userID] = 0;
                player.ChatMessage($"Teleporting to {note.position.ToString()}...");
                player.transform.position = note.position;
                return false;
            }

            return note;
        }
    }
}