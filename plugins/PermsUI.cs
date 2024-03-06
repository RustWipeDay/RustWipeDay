using UnityEngine;
using System.Collections.Generic;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins
{
    [Info("Perms UI", "Camoec", 1.3)]
    [Description("GUI for managing plugin permissions in-game")]

    public class PermsUI : RustPlugin
    {
        private const string UsePerm = "PermsUI.use";
        private void Init()
        {
            permission.RegisterPermission(UsePerm, this);
        }

        private void MainUI(BasePlayer player, string userID, bool user, int page)
        {
            string Layer = "UI";
            BasePlayer target = user ? BasePlayer.FindByID(ulong.Parse(userID)) : null;
            var container = new CuiElementContainer();
            List<string> perms = new List<string>();
            foreach (var perm in permission.GetPermissions())
                if (!perm.StartsWith("oxide"))
                    perms.Add(perm);

            int pageLen = 11;

            CuiHelper.DestroyUi(player, Layer);
            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1"},
                Image =
                {
                    Color = "0 0 0 0.5"
                },
                CursorEnabled = true                
            }, "Overlay", Layer);

            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = "" },
                Button =
                    {
                        Color = "0 0 0 0",
                        Close = Layer
                    }
            }, Layer);

            container.Add(new CuiPanel
            {
                RectTransform =
                {
                    AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                    OffsetMin = "-310 -250",
                    OffsetMax = "310 250"
                },
                Image =
                {
                    Color = "0.1 0.1 0.1 0.99"
                }
            }, Layer, Layer + ".Main");

            container.Add(new CuiPanel
            {
                RectTransform =
                {
                    AnchorMin = "0 1", AnchorMax = "1 1",
                    OffsetMin = "0 -50",
                    OffsetMax = "0 0"
                },
                Image = 
                {
                    Color =  "0.09 0.09 0.09 1"
                }
            }, Layer + ".Main", Layer + ".Header");

            container.Add(new CuiLabel
            {
                RectTransform =
                {
                    AnchorMin = "0 0", AnchorMax = "1 1",
                    OffsetMin = "30 0",
                    OffsetMax = "0 0"
                },
                Text =
                {
                    Text = string.Format(Lang("Info", player.UserIDString), page+1, (int)(perms.Count / 10)+1, (user ? $"{target.displayName} [{target.UserIDString}]" : userID)),
                    Align = TextAnchor.MiddleLeft,
                    Font = "RobotoCondensed-Bold.ttf",
                    FontSize = 14,
                    Color = "1 1 1 1"
                }
            }, Layer + ".Header");

            var _y = -60f;
            float _x = -10;

            float width = 30;
            float margin = 2;

            container.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = "1 1", AnchorMax = "1 1",
                    OffsetMin = $"{_x - width} -37.5",
                    OffsetMax = $"{_x} -12.5"
                },
                Text =
                {
                    Text = ">",
                    Align = TextAnchor.MiddleCenter,
                    FontSize = 12,
                    Color = "1 1 1 1"
                },
                Button =
                {
                    Color =  "0.6 0.01 0.16 1",
                    Command = $"PERMS page {player.UserIDString} {userID} {user} {(page < (perms.Count / pageLen) ? page+1 : page)}"
                }
            }, Layer + ".Header");



            _x = _x - margin - width;

            container.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = "1 1", AnchorMax = "1 1",
                    OffsetMin = $"{_x - width} -37.5",
                    OffsetMax = $"{_x} -12.5"
                },
                Text =
                {
                    Text = "<",
                    Align = TextAnchor.MiddleCenter,
                    FontSize = 12,
                    Color = "1 1 1 1"
                },
                Button =
                {
                    Color =  "0.6 0.01 0.16 1",
                    Command = $"perms page {player.UserIDString} {userID} {user} {(page > 0 ? page-1 : 0)}"
                }
            }, Layer + ".Header");
            
            for (int i = page * pageLen ; i < perms.Count; i++)
            {
                if (i > page * pageLen + pageLen - 1)
                    break;
                var perm = perms[i];
                container.Add(new CuiPanel
                {
                    RectTransform =
                    {
                        AnchorMin = "0.5 1", AnchorMax = "0.5 1",
                        OffsetMin = $"-300 {_y - 30}",
                        OffsetMax = $"300 {_y}"
                    },
                    Image =
                    {
                        Color =  "0.09 0.09 0.09 1"
                    }
                }, Layer + ".Main", Layer + $".Perm.{_y}");

                container.Add(new CuiLabel
                {
                    RectTransform =
                    {
                        AnchorMin = "0 0", AnchorMax = "1 1",
                        OffsetMin = "10 0", OffsetMax = "0 0"
                    },
                    Text =
                    {
                        Text = $"{perm}",
                        Align = TextAnchor.MiddleLeft,
                        FontSize = 14,
                        Color = "1 1 1 1"
                    }
                }, Layer + $".Perm.{_y}");

                string cmd = $"{(user ? permission.UserHasPermission(userID, perm) ? "Revoke" : "Grant" : permission.GroupHasPermission(userID, perm) ? "Revoke" : "Grant")}";

                container.Add(new CuiButton
                {
                    RectTransform =
                    {
                        AnchorMin = "1 0.5", AnchorMax = "1 0.5",
                        OffsetMin = "-120 -15", OffsetMax = "-0 15"
                    },
                    Text =
                    {
                        Text = cmd,
                        Align = TextAnchor.MiddleCenter,
                        FontSize = 14,
                        Color = "1 1 1 1"
                    },
                    Button =
                    {
                        Color =  cmd == "Revoke" ? "0.6 0.01 0.16 1" : "0.1 0.6 0.1 1",
                        Command = $"perms {(user ? "USER" : "GROUP")} {cmd} {perm} {userID} {player.userID} {page}"
                    }
                }, Layer + $".Perm.{_y}");

                _y = _y - 30 - 10;
            }


            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("perms")]
        private void GivePerm(ConsoleSystem.Arg arg)
        {
            if (!arg.IsRcon && (arg.Player() != null && !permission.UserHasPermission(arg.Player().UserIDString, UsePerm)))
                return;

            if (arg == null || arg.Args.Length == 0)
                return;
            var args = arg.Args;

            string id = args[0].ToLower();
            string cmd = args[1].ToLower();

            if(id == "page")
            {
                MainUI(BasePlayer.FindByID(ulong.Parse(cmd)), args[2], bool.Parse(args[3]), int.Parse(args[4]));
                return;
            }

            string perm = args[2];
            string userid = args[3];
            string caller = args[4];
            int page = int.Parse(args[5]);

            if (id == "user")
            {
                if (cmd == "grant")
                {
                    permission.GrantUserPermission(userid, perm, null);
                }
                else if (cmd == "revoke")
                {
                    permission.RevokeUserPermission(userid, perm);
                }
                MainUI(BasePlayer.FindByID(ulong.Parse(caller)), userid, true, page);
            }else if(id == "group")
            {
                if (cmd == "grant")
                {
                    permission.GrantGroupPermission(userid, perm, null);
                }
                else if (cmd == "revoke")
                {
                    permission.RevokeGroupPermission(userid, perm);
                }
                MainUI(BasePlayer.FindByID(ulong.Parse(caller)), userid, false, page);
            }
            
        }


        [ChatCommand("perms")]
        private void permsCommand (BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, UsePerm))
            {
                PrintToChat(player, Lang("NoPermission", player.UserIDString));
                return;
            }

            if (args.Length > 1)
            {
                string cmd = args[0].ToLower();
                string id = args[1].ToLower();
                
                if (cmd == "user")
                {
                    BasePlayer target = BasePlayer.Find(id);
                    if(target == null)
                    {
                        PrintToChat(player, Lang("UserNF", player.UserIDString));
                        return;
                    }
                    MainUI(player, target.UserIDString, true, 0);
                    return;
                }

                if (cmd == "group")
                {
                    if(!permission.GroupExists(id))
                    {
                        PrintToChat(player, Lang("GroupNF", player.UserIDString));
                        return;
                    }
                    MainUI(player, id, false, 0);
                    return;
                }
                return;
            }

            PrintToChat(player, Lang("Syntax", player.UserIDString));
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "You don't have permission to use this",
                ["Syntax"] = "Use /perms [user|group] [id|name]",
                ["GroupNF"] = "Group not found",
                ["UserNF"] = "Player not found",
                ["Info"] = "[{0}/{1}] Permissions for {2}"
            }, this);
        }

        private string Lang(string key, string userid) => lang.GetMessage(key, this, userid);

    }
}