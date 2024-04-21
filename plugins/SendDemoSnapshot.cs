namespace Oxide.Plugins
{
    [Info("Send Demo Snapshot", "Ods Scott", "1.0.0")]
    public class SendDemoSnapshot : CovalencePlugin
    {
        object OnClientCommand(Network.Connection connection, string command)
        {
            var player = connection.player as BasePlayer;

            if (!command.Equals("server.snapshot") || !player.IsAdmin || player.Connection.authLevel != 0)
            {
                return null;
            }
            
            Snapshot(player);
            
            return null;
        }

        void Snapshot(BasePlayer basePlayer)
        {
            if (!(basePlayer == null))
            {
                Puts("Sending full snapshot to " + basePlayer);
                basePlayer.SendNetworkUpdateImmediate();
                basePlayer.SendGlobalSnapshot();
                basePlayer.SendFullSnapshot();
                basePlayer.SendEntityUpdate();
                TreeManager.SendSnapshot(basePlayer);
                ServerMgr.SendReplicatedVars(basePlayer.net.connection);
            }
        }
    }
}
