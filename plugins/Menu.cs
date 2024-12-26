using Oxide.Core.Plugins;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("Menu", "YourName", "1.0.0")]
    [Description("Opens a menu with index 0 when a player connects")]

    public class Menu : RustPlugin
    {
        void Init()
        {
            Puts("PlayerMenuOpener plugin loaded");
        }

        void OnPlayerConnected(BasePlayer player)
        {
            OpenMenu(player, 0);
        }

        private void OpenMenu(BasePlayer player, int menuIndex)
        {
            // Here you would have the code to open the menu for the player.
            // This is a placeholder method and should be replaced with actual implementation.
            player.SendConsoleCommand("kits.welcome");
        }
    }
}
