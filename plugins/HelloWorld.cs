namespace Carbon.Plugins
{
    [Info("HelloWorld", "<author>", "1.0.0")]
    [Description("<optional_description>")]
    public class HelloWorld : CarbonPlugin
    {
        private void OnServerInitialized()
        {
            Puts("Hello world!");
        }

        void OnPlayerConnected(BasePlayer player)
        {
            Server.Broadcast($"Hello {player.displayName}");
        }
    }
}