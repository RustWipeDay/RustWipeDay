using Oxide.Core.Libraries.Covalence;
using Oxide.Ext.Discord.Interfaces;
using Oxide.Plugins;
using System.Collections.Generic;
using TinyJSON;
using UnityEngine.LowLevel;

namespace Carbon.Plugins
{
    [Info ("HelpCommand", "insilicon / ODS", "0.0.1")]
    [Description ("Help command for RWD")]
    public class Help : CarbonPlugin
    {

        private List<ulong> bountyTips = new List<ulong>();
        private List<ulong> baseBountyTips = new List<ulong>();
        private List<ulong> raidProtectionTips = new List<ulong>();

        #region CommandConfigs
        private List<(string CommandName, string Description)> commands = new List<(string CommandName, string Description)>
        {
            ("help", "Show this help message"),
            ("leaderboard", "Shows the leaderboard of the server with specialies and more!"),
            ("elo", "All players have a rank in the server, use this command to find your general rank."),
            ("bounty", "You may receieve bounty by turning in bounty tags or killing players. Use this command to view your bounty."),
            ("basebounty", "You may receieve base bounty by raiding, using bounty tags, or killing players. Use this command to view your base bounty."),
            ("honor", "You may receieve honor by farming, pveing, and pvping. Honor ranks are limited at the top and unlock more market items at the bounty hunter."),
            ("kit", "You can find your VIP kits here!"),
            ("skinbox", "Skinbox allows you to change your item's skins to a skin you don't own!"),
            ("queue", "Queue into duel matches against fellow server-mates and teammates!"),
            ("dc code", "Link your account to your Discord account for free bounty points every wipe! Once you type this command open discord and start typing /dc link and use the slash command to complete."),
            // Add more commands here
        };
        #endregion

        #region Hooks

        private void OnServerInitialized()
        {
            // Subscribe(nameof(OnAddBaseBountyPoints));
        }

        /*void OnAddBaseBountyPoints(BasePlayer player, int amount)
        {
            SendBaseBountyTip(player);
        }

        object OnItemPickup(Item item, BasePlayer player)
        {
            if (item.info.shortname == "dogtagneutral" || item.info.shortname == "bluedogtags")
            {
                SendBountyTagTip(player);
            }
            return null;
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (item.info.shortname == "dogtagneutral" || item.info.shortname == "bluedogtags")
            {
                var player = container?.entityOwner?.ToPlayer();
                if (player == null)
                {
                    return;
                }
                SendBountyTagTip(player);
            }
        }

        object OnLootNetworkUpdate(PlayerLoot self)
        {
            var item = self.itemSource;
            if (item != null && (item.info.shortname == "dogtagneutral" || item.info.shortname == "bluedogtags"))
            {
                var player = self.baseEntity;
                SendBountyTagTip(player);
            }
            return (object)null;
        }

        object OnInventoryNetworkUpdate(PlayerInventory inventory, ItemContainer container, ProtoBuf.UpdateItemContainer updateItemContainer, PlayerInventory.Type type, PlayerInventory.NetworkInventoryMode networkInventoryMode)
        {
            var player = inventory.baseEntity.ToPlayer();
            if (player != null && !bountyTips.Contains(player.userID))
            {
                int dogTag = inventory.FindItemByItemName("dogtagneutral")?.amount ?? 0 + inventory.FindItemByItemName("bluedogtags")?.amount ?? 0;
                if (dogTag > 0)
                {
                    SendBountyTagTip(player);
                }
            }
            return null;
        }


        private object OnCupboardAuthorize(BuildingPrivlidge priv, BasePlayer player)
        {
            SendRaidProtectionTip(player);
            return null;
        }*/

        #endregion


        /*void SendBountyTagTip(BasePlayer player)
        {
            if (!bountyTips.Contains(player.userID))
            {
                HelpDogTap(player);
                bountyTips.Add(player.userID);
            }
        }

        void SendRaidProtectionTip(BasePlayer player)
        {
            if (!raidProtectionTips.Contains(player.userID))
            {
                HelpRaidProtection(player);
                raidProtectionTips.Add(player.userID);
            }
        }

        void SendBaseBountyTip(BasePlayer player)
        {
            if (!baseBountyTips.Contains(player.userID))
            {
                HelpBaseBounty(player);
                baseBountyTips.Add(player.userID);
            }
        }*/

        #region Commands

        /*[ChatCommand("helpraidprotection")]
        void HelpRaidProtection(BasePlayer player)
        {
            player.ChatMessage($"<size=16><color=#c45508>Raid Protection System</color></size>\n\n• Every team is entitled to one TC that has raid protection at the start of wipe.\n\n• Raid protection decays over time until it reaches 0%.\n\n• You must claim a main TC by clicking the dog tag bounty icon in the top right corner and confirming.\n\n• Having any form of base bounty will increase the rate that the raid protection decays. \n\n• Every individual has spendable base bounty points, a tool cupboard stores these points as redeemed perks.\n\n• You may only have one main TC with raid protection per wipe, no exceptions.\n\n• <color=#ff0000>CAUTION:</color> Teaming with people who already have a main TC will invalidate both TCs raid protection.\n\n<size=8>To see this message again type /helpraidprotection</size>");
        }

        [ChatCommand("helpbounty")]
        void HelpDogTap(BasePlayer player)
        {
            player.ChatMessage($"<size=16><color=#c45508>Player Bounty System</color></size>\n\n• White dog tags (bounty tags) can be eaten for honor and raid bounty.\n\n• Blue dog tags may only be turned in to bounty hunters.\n\n• To eat a white dog tag, select them on your arm bar and then press the primary fire (left mouse) button.\n\n• You can take your white and blue dog tags to a bounty hunter in certain monuments such as Outpost or Gas Station.\n\n• Interact with the bounty hunter to hand over your dog tags.\n\n• Once you have handed in your dog tags, you may ask about the loot and shop for items.\n\n• The items you may shop for are based on your honor level, unlocking more honor will unlock more items.\n\n• PvP with players will cause you to gain or lose bounty points, some points will drop on the floor as blue dog tags.\n\n• Use the /bounty command to view your points.\n\n<size=8>To see this message again type /helpbounty</size>");
        }

        [ChatCommand("helpbasebounty")]
        void HelpBaseBounty(BasePlayer player)
        {
            player.ChatMessage($"<size=16><color=#c45508>Base Bounty System</color></size>\n\n• Base bounty points are earned by killing players, eating dog tags, turning in dog tags, buying bounty items, or raiding other base bounties.\n\n• You can open your base tool cupboard and spend these base bounty points.\n\n• Anyone on your team who earns a base bounty point spent or unspent will speed up raid protection decay.\n\n• Spending your base bounty points will unlock additional buffs and perks for your team and base.\n\n• Spending base bounty may alert others of your tool cupboard location with a map marker.\n\n• Spending base bounty will increase blue dog tag generation that can be looted from a free space in the tool cupboard.\n\n• Use the /basebounty command to view your spendable base bounty.\n\n<size=8>To see this message again type /helpbasebounty</size>");
        }*/

        [ChatCommand("help")]
        private void HelpCommand(IPlayer player, string command, string[] args)
        {
            if (!player.IsConnected || player.IsSleeping)
                return;

            string helpText = "<color=#C84E00>Available Commands:</color>\n\n";
            foreach (var cmd in commands)
            {
                helpText += $"<color=#5A1A01>/</color><color=#FF6F3D>{cmd.CommandName}</color><color=#5A1A01> - </color><color=#ffffff>{cmd.Description}</color>\n\n";
            }

            player.Message(helpText);
        }

        #endregion

    }


}