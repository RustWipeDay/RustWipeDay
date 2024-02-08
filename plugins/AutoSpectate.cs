using Oxide.Core.Libraries.Covalence;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using Oxide.Core;
using System;

namespace Oxide.Plugins
{
    [Info("Auto Spectate", "Aspectdev", "0.0.13")]
    [Description("Provides a way to automatically switch between users while spectating.")]
    class AutoSpectate : CovalencePlugin
    {

        #region Core

        Dictionary<string, Information> _users = new Dictionary<string, Information>();

        const string _permission = "autospectate.use";

        #endregion

        #region Hooks

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPlayers"] = "Auto Spectate session ended. There are less than the required amount of players in the game.",
                ["StartedSpectate"] = "Successfully started auto spectating with rate of {secondsRate} seconds.",
                ["MovedToSpectator"] = "It doesn't look like you were already spectating. You have been moved to spectator mode.",
                ["Removed"] = "You have been removed from auto spectate mode.",
                ["Updated"] = "{Prefix} Updated spectator to {newMessage}"
            }, this, "en");
        }

        void Init() => permission.RegisterPermission(_permission, this);

        void OnPlayerDisconnected(BasePlayer player)
        {
            string SteamID = player.UserIDString;
            if (_users.ContainsKey(SteamID))
            {
                if (!_users[SteamID].myTimer.Destroyed) _users[SteamID].myTimer.Destroy();
                _users.Remove(SteamID);
            }
        }

        object OnMessagePlayer(string message, BasePlayer player)
        {
            if (_users.ContainsKey(player.UserIDString) && message.StartsWith("Spectating:"))
            {
                return true;
            }
            return null;
        }

        void Unload() => _users.Clear();

        #endregion

        #region Commands

        [Command("autospectate")]
        void Spectate(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if (player == null) return;

            var SteamID = iplayer.Id;

            if (!permission.UserHasPermission(SteamID, _permission) || (!ServerUsers.Is(player.userID, ServerUsers.UserGroup.Moderator) && !ServerUsers.Is(player.userID, ServerUsers.UserGroup.Owner))) return;

            if (_users.ContainsKey(SteamID))
            {
                if (_users[SteamID].myTimer != null) _users[SteamID].myTimer.Destroy();
                _users.Remove(SteamID);

                player.ShowToast(0, lang.GetMessage("Removed", this, player.UserIDString));
                return;
            };

            int result;
            try
            {
                result = Math.Max(Math.Min(Int32.Parse(args[0]), 300), 1);
            } catch
            {
                result = 15;
            }

            player.ShowToast(0, lang.GetMessage("StartedSpectate", this, player.UserIDString).Replace("{secondsRate}", result.ToString()));
            
            Timer myTimer = null;
            myTimer = timer.Every(result, () =>
            {
                if (myTimer.Destroyed) return;

                if (!_users.ContainsKey(SteamID))
                {
                    if (myTimer != null && !myTimer.Destroyed) myTimer.Destroy();
                    return;
                }
                else if (_users[SteamID].myTimer == null) _users[SteamID].myTimer = myTimer;

                var list = BasePlayer.activePlayerList.Where(v => v.UserIDString != SteamID && !v.IsDead()).ToList();
                if (list.Count <= 0)
                {
                    _users.Remove(SteamID);
                    if (!myTimer.Destroyed) myTimer.Destroy();
                    player.ShowToast(0, lang.GetMessage("NoPlayers", this, player.UserIDString));
                    return;
                }

                else SwitchSpectator(list, player);
            });

            _users[SteamID] = new Information(Core.Random.Range(0, BasePlayer.activePlayerList.Where(v => v.UserIDString != SteamID && !v.IsDead()).ToList().Count-2), myTimer);
        }

        #endregion

        #region Helpers

        internal void SwitchSpectator(List<BasePlayer> list, BasePlayer player)
        {
            string SteamID = player.UserIDString;

            BasePlayer target = list[_users[SteamID].spectateId %= list.Count];
            _users[SteamID].spectateId += 1;

            if (target != null)
            {
                if (!player.IsSpectating())
                {
                    player.ShowToast(0, lang.GetMessage("MovedToSpectator", this, player.UserIDString));

                    if (!player.IsDead()) player.DieInstantly();
                    player.StartSpectating();
                }
                else
                {
                    var playerName = target.displayName;
                    player.ShowToast(GameTip.Styles.Red_Normal, lang.GetMessage("Updated", this, player.UserIDString).Replace("{Prefix}", $"[{_users[SteamID].spectateId}/{list.Count}]").Replace("{newMessage}", playerName));
                }
                player.UpdateSpectateTarget(target.UserIDString);
            }
        }

        #endregion

        #region Classes

        public class Information
        {
            public Information(int sp, Timer t)
            {
                spectateId = sp;
                myTimer = t;
            }

            public int spectateId;
            public Timer myTimer;
        }

        #endregion
    }
}