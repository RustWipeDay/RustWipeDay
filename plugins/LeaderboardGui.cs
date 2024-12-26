using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;


namespace Oxide.Plugins
{
    [Info("LeaderboardGUI", "YourName", "1.0.0")]
    [Description("Creates a leaderboard GUI with player statistics")]
    public class LeaderboardGui : RustPlugin
    {
        [PluginReference]
        private Plugin StatisticsDB;

        [PluginReference]
        public Plugin HonorSystem = null;

        [PluginReference]
        public Plugin DogTags = null;

        private Dictionary<ulong, PlayerStats> playerStats = new Dictionary<ulong, PlayerStats>();

        private class PlayerStats
        {
            public uint LastUpdate;
            public uint Joins;
            public uint Leaves;
            public uint Elo;
            public uint Kills;
            public uint Deaths;
            public uint Suicides;
            public uint Shots;
            public uint Headshots;
            public uint Experiments;
            public uint Recoveries;
            public uint VoiceBytes;
            public uint WoundedTimes;
            public uint CraftedItems;
            public uint RepairedItems;
            public uint LiftUsages;
            public uint WheelSpins;
            public uint HammerHits;
            public uint ExplosivesThrown;
            public uint WeaponReloads;
            public uint RocketsLaunched;
            public uint SecondsPlayed;
            public List<string> Names;
            public Dictionary<string, uint> Gathered = new Dictionary<string, uint>();
        }

        private void Init()
        {
            permission.RegisterPermission("leaderboardgui.use", this);
        }

        private void DrawLeaderboard(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "LeaderboardOverlay");
            ulong playerId = player.userID;

            JObject playerData = StatisticsDB.Call<JObject>("API_GetAllData");
            Dictionary<ulong, PlayerStats> stats = playerData.ToObject<
                Dictionary<ulong, PlayerStats>
            >();

            var orderedStats = stats
                .OrderByDescending(
                    statKvp =>
                        statKvp.Value.Elo
                )
                .Take(20)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            CuiElementContainer container = new CuiElementContainer();

            container.Add(
                new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0.2 0", AnchorMax = "0.95 1" },
                    CursorEnabled = true
                },
                "Overlay",
                "LeaderboardOverlay"
            );

            // Table
            container.Add(
                new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                },
                "LeaderboardOverlay",
                "Leaderboard"
            );

            // Border Box
            container.Add(
                new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "50 50",
                        OffsetMax = "-50 -50"
                    }
                },
                "Leaderboard",
                "TableOverlay"
            );

            CreateTableHeaders(
                container,
                new TableHeader[]
                {
                    new TableHeader(
                        "#",
                        Enumerable.Range(1, orderedStats.Count).Select(i => i.ToString()).ToList(),
                        2
                    ),
                    new TableHeader(
                        "Player",
                        orderedStats.Select(statKvp => HonorSystem.Call<string>("FormatPlayerName", statKvp.Key, statKvp.Value.Names.Last(), false)).ToList(),
                        28,
                        TextAnchor.MiddleLeft
                    ),
                    new TableHeader(
                        "Elo",
                        orderedStats.Select(statKvp => $"<color={StatisticsDB.Call<string>("API_GetEloColorRank", statKvp.Value.Elo)}>{statKvp.Value.Elo}</color>").ToList(),
                        6
                    ),
                    new TableHeader(
                        "Bounty",
                        orderedStats.Select(statKvp => DogTags.Call<int>("GetPoints", statKvp.Key).ToString()).ToList(),
                        8
                    ),
                    new TableHeader(
                        "Kills",
                        orderedStats.Select(statKvp => statKvp.Value.Kills.ToString()).ToList(),
                        6
                    ),
                    new TableHeader(
                        "Deaths",
                        orderedStats.Select(statKvp => statKvp.Value.Deaths.ToString()).ToList(),
                        6
                    ),
                    new TableHeader(
                        "KDR",
                        orderedStats
                            .Select(
                                statKvp =>
                                    (
                                        statKvp.Value.Deaths == 0
                                            ? statKvp.Value.Kills
                                            : (
                                                (double)statKvp.Value.Kills
                                                / (double)statKvp.Value.Deaths
                                            )
                                    ).ToString("0.00")
                            )
                            .ToList(),
                        5
                    ),
                    new TableHeader(
                        "Suicides",
                        orderedStats.Select(statKvp => statKvp.Value.Suicides.ToString()).ToList()
                    ),
                    new TableHeader(
                        "Shots Fired",
                        orderedStats.Select(statKvp => statKvp.Value.Shots.ToString()).ToList()
                    ),
                    new TableHeader(
                        "Explosions",
                        orderedStats
                            .Select(statKvp => (statKvp.Value.ExplosivesThrown + statKvp.Value.RocketsLaunched).ToString())
                            .ToList()
                    ),
                    new TableHeader(
                        "Gathered",
                        orderedStats
                            .Select(statKvp => statKvp.Value.Gathered.Sum(kv => kv.Value).ToString())
                            .ToList()
                    )
                }
            );

            // Create the GUI
            CuiHelper.AddUi(player, container);
        }

        [ChatCommand("leaderboard")]
        private void LeaderboardCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "leaderboardgui.use"))
            {
                SendReply(player, "You don't have permission to use this command.");
                return;
            }
            DrawLeaderboard(player);
        }

        public class TableHeader
        {
            public string label;
            public int size;
            public List<string> values;
            public TextAnchor anchor;

            public TableHeader(string label, List<string> values, int size = -1, TextAnchor anchor = TextAnchor.MiddleCenter)
            {
                this.values = values;
                this.label = label;
                this.anchor = anchor;
                if (size > -1)
                {
                    this.size = size;
                }
                else
                {
                    this.size = label.Length;
                }
            }
        }

        void CreateTableHeaders(CuiElementContainer container, TableHeader[] headers)
        {
            double offset = 0;
            var rows = 0;
            container.Add(
                new CuiPanel
                {
                    Image = { Color = "0 0 0 0.8" },
                    RectTransform = { AnchorMin = $"0 0.95", AnchorMax = $"1 1", }
                },
                "TableOverlay",
                "TableHeaders"
            );

            var lastAnchorMax = 0.0;
            for (int i = 0; i < headers.Length; i++)
            {
                var header = headers[i];
                var labelSize = header.label.Length;
                if (header.label.Length > 0)
                {
                    offset = (header.size) * 0.01;
                }
                else
                {
                    offset = 0;
                }
                var anchorMin = lastAnchorMax;

                var anchorMax = lastAnchorMax + offset;
                if (anchorMax > 1) {
                    anchorMax = 1;
                }
                lastAnchorMax = anchorMax + 0.0025;
                if (lastAnchorMax > 1) {
                    lastAnchorMax = 1;
                }

                container.Add(
                    new CuiPanel
                    {
                        Image = { Color = "0.2 0.2 0.2 0.9" },
                        RectTransform =
                        {
                            AnchorMin = $"{anchorMin} 0",
                            AnchorMax = $"{anchorMax} 1",
                        }
                    },
                    "TableHeaders",
                    "BorderBox" + i
                );

                // Column Header
                container.Add(
                    new CuiLabel
                    {
                        Text =
                        {
                            Text = header.label,
                            Align = TextAnchor.MiddleCenter,
                            FontSize = 12
                        },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", }
                    },
                    "BorderBox" + i,
                    "ColumnHeader" + i
                );

                for (int x = 0; x < header.values.Count; x++)
                {
                    var rowHeightMin = 0.9 - (x * 0.05);
                    var rowHeightMax = rowHeightMin + 0.05;
                    if (x >= rows)
                    {
                        container.Add(
                            new CuiPanel
                            {
                                Image = { Color = "0 0 0 0.7" },
                                RectTransform =
                                {
                                    AnchorMin = $"0 {rowHeightMin}",
                                    AnchorMax = $"1 {rowHeightMax}",
                                }
                            },
                            "TableOverlay",
                            "TableRow" + x
                        );
                        rows++;
                    }

                    container.Add(
                        new CuiPanel
                        {
                            Image = { Color = $"0.2 0.2 0.2 {(x % 2 == 0 ? 0.5 : 0.25)}" },
                            RectTransform =
                            {
                                AnchorMin = $"{anchorMin} 0",
                                AnchorMax = $"{anchorMax} 1",
                            }
                        },
                        "TableRow" + x,
                        "ValueBorderBox" + i + "_" + x
                    );

                    // Column Header
                    container.Add(
                        new CuiLabel
                        {
                            Text =
                            {
                                Text = header.values[x],
                                Align = header.anchor,
                                FontSize = 12
                            },
                            RectTransform = { AnchorMin = "0.01 0", AnchorMax = "0.99 1", }
                        },
                        "ValueBorderBox" + i + "_" + x,
                        "ColumnValue" + i + "_" + x
                    );
                }
            }
        }

        [ConsoleCommand("leaderboard")]
        private void LeaderboardConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null)
                return;

            BasePlayer player = arg.Player();
            LeaderboardCommand(player, "leaderboard", new string[0]);
        }

        [ConsoleCommand("leaderboard.close")]
        private void CloseLeaderboardConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null)
                return;

            BasePlayer player = arg.Player();
            CloseLeaderboard(player);
        }

        private void CloseLeaderboard(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "LeaderboardOverlay");
        }

        private string GenerateTableContent(ulong playerId)
        {
            JObject playerData = StatisticsDB.Call<JObject>("API_GetAllPlayerData", playerId);
            PlayerStats stats = playerData.ToObject<PlayerStats>();

            // Generate the table content using playerStats dictionary
            string content = "Player Statistics:\n\n";

            // Append player stats to the content string
            content += $"Player ID: {playerId}\n";
            content += $"Kills: {stats.Kills} \u2694️\n"; // Sword icon
            content += $"Deaths: {stats.Deaths} \ud83d\udc80\n"; // Skull icon
            content += $"Joins: {stats.Joins} \u25b6️\n"; // Play icon
            content += $"Leaves: {stats.Leaves} \u25c0️\n"; // Stop icon
            content += $"Suicides: {stats.Suicides} \ud83d\udca9\n"; // Bomb icon
            content += $"Shots: {stats.Shots} \ud83d\udd2b\n"; // Gun icon
            content += $"Headshots: {stats.Headshots} \ud83d\udde1️\n"; // Target icon
            content += $"Experiments: {stats.Experiments} \ud83c\udf93\n"; // Test tube icon
            content += $"Recoveries: {stats.Recoveries} \ud83d\udee0\n"; // Bandage icon
            content += $"VoiceBytes: {stats.VoiceBytes} \ud83d\udde3️\n"; // Microphone icon
            content += $"WoundedTimes: {stats.WoundedTimes} \ud83d\udee5️\n"; // Bandage icon
            content += $"CraftedItems: {stats.CraftedItems} \ud83d\udd28\n"; // Hammer and wrench icon
            content += $"RepairedItems: {stats.RepairedItems} \ud83d\udee0\n"; // Bandage icon
            content += $"LiftUsages: {stats.LiftUsages} \u2195️\n"; // Up-down arrow icon
            content += $"WheelSpins: {stats.WheelSpins} \ud83c\udfaf\n"; // Ferris wheel icon
            content += $"HammerHits: {stats.HammerHits} \ud83d\udd28\n"; // Hammer and wrench icon
            content += $"ExplosivesThrown: {stats.ExplosivesThrown} \ud83d\udca3\n"; // Bomb icon
            content += $"WeaponReloads: {stats.WeaponReloads} \ud83d\udd2b\n"; // Gun icon
            content += $"RocketsLaunched: {stats.RocketsLaunched} \ud83d\udea8\n"; // Rocket icon
            content += $"SecondsPlayed: {stats.SecondsPlayed} \u23f3️\n"; // Clock icon

            content += "\n";

            return content;
        }
    }
}
