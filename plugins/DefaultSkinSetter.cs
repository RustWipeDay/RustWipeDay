using Oxide.Core.Plugins;
using Oxide.Core;
using System.Collections.Generic;
using Rust;

namespace Oxide.Plugins
{
    [Info("DefaultSkinSetter", "TTV OdsScott", "1.0.0")]
    public class DefaultSkinSetter : RustPlugin
    {
        private Dictionary<string, ulong> defaultSkins = new Dictionary<string, ulong>();

        void OnServerInitialized()
        {
            Subscribe(nameof(OnItemSkinChanged));
        }

        private void OnItemSkinChanged(BasePlayer player, Item item)
        {
            string key = GetPlayerItemKey(player.userID, item.info.shortname);
            defaultSkins[key] = item.skin;
        }
        
        void OnItemCraftFinished(ItemCraftTask task, Item item, ItemCrafter crafter)
        {
            if (crafter.owner != null)
            {
                BasePlayer player = crafter.owner;
                string key = GetPlayerItemKey(player.userID, item.info.shortname);

                if (item.skin != 0) {
                    return;
                }
                if (defaultSkins.TryGetValue(key, out ulong defaultSkin))
                {
                    item.skin = defaultSkin;
                    item.MarkDirty();
                    BaseEntity heldEntity = item.GetHeldEntity();
                    if (heldEntity != null)
                    {
                        heldEntity.skinID = defaultSkin;
                        heldEntity.SendNetworkUpdate();
                    }
                }
            }
        }

        private string GetPlayerItemKey(ulong userID, string shortName)
        {
            return $"{userID}_{shortName}";
        }
    }
}
