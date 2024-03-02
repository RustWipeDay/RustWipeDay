using Newtonsoft.Json;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Libraries.Covalence;
using Oxide.Game.Rust.Libraries;
using Oxide.Plugins;
using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Tebex", "Tebex", "2.0.6")]
    [Description("Official support for the Tebex server monetization platform")]
    public class Tebex : CovalencePlugin
    {
        private static TebexOxideAdapter _adapter;

        private Dictionary<string, DateTime> _lastNoteGiven = new Dictionary<string, DateTime>();

        public static string GetPluginVersion()
        {
            return "2.0.6";
        }

        private void Init()
        {
            // Setup our API and adapter
            _adapter = new TebexOxideAdapter(this);
            TebexApi.Instance.InitAdapter(_adapter);

            BaseTebexAdapter.PluginConfig = Config.ReadObject<BaseTebexAdapter.TebexConfig>();
            if (!Config.Exists())
            {
                //Creates new config file
                LoadConfig();
            }

            // Register permissions
            permission.RegisterPermission("tebex.secret", this);
            permission.RegisterPermission("tebex.sendlink", this);
            permission.RegisterPermission("tebex.forcecheck", this);
            permission.RegisterPermission("tebex.refresh", this);
            permission.RegisterPermission("tebex.report", this);
            permission.RegisterPermission("tebex.ban", this);
            permission.RegisterPermission("tebex.lookup", this);
            permission.RegisterPermission("tebex.debug", this);

            // Register user permissions
            permission.RegisterPermission("tebex.info", this);
            permission.RegisterPermission("tebex.categories", this);
            permission.RegisterPermission("tebex.packages", this);
            permission.RegisterPermission("tebex.checkout", this);

            // Check if auto reporting is disabled and show a warning if so.
            if (!BaseTebexAdapter.PluginConfig.AutoReportingEnabled)
            {
                _adapter.LogWarning("Auto reporting issues to Tebex is disabled.");
                _adapter.LogWarning("To enable, please set 'AutoReportingEnabled' to 'true' in config/Tebex.json");
            }

            // Check if secret key has been set. If so, get store information and place in cache
            if (BaseTebexAdapter.PluginConfig.SecretKey != "your-secret-key-here")
            {
                // No-op, just to place info in the cache for any future triage events
                _adapter.FetchStoreInfo((info => { }));
                return;
            }

            _adapter.LogInfo("Tebex detected a new configuration file.");
            _adapter.LogInfo("Use tebex:secret <secret> to add your store's secret key.");
            _adapter.LogInfo("Alternatively, add the secret key to 'Tebex.json' and reload the plugin.");
        }

        public WebRequests WebRequests()
        {
            return webrequest;
        }

        public IPlayerManager PlayerManager()
        {
            return players;
        }

        public PluginTimers PluginTimers()
        {
            return timer;
        }

        public IServer Server()
        {
            return server;
        }

        public string GetGame()
        {
            return game;
        }

        public void Warn(string message)
        {
            LogWarning("{0}", message);
        }

        public void Error(string message)
        {
            LogError("{0}", message);
        }

        public void Info(string info)
        {
            Puts("{0}", info);
        }

        private void OnUserConnected(IPlayer player)
        {
            // Check for default config and inform the admin that configuration is waiting.
            if (player.IsAdmin && BaseTebexAdapter.PluginConfig.SecretKey == "your-secret-key-here")
            {
                player.Command("chat.add", 0, player.Id,
                    "Tebex is not configured. Use tebex:secret <secret> from the F1 menu to add your key.");
                player.Command("chat.add", 0, player.Id, "Get your secret key by logging in at:");
                player.Command("chat.add", 0, player.Id, "https://tebex.io/");
            }

            _adapter.LogDebug($"Player login event: {player.Id}@{player.Address}");
            _adapter.OnUserConnected(player.Id, player.Address);
        }

        #if RUST // VIP notes are enabled on Rust only
        void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (!BaseTebexAdapter.PluginConfig.VipNotesEnabled)
            {
                return;
            }
            
            // If the user configured no VIP codes, skip.
            if (BaseTebexAdapter.PluginConfig.VipCodes.Count == 0)
            {
                return;
            }

            // Check if we have a target loot crate by prefab name
            string prefabName = entity?.ShortPrefabName;
            if (string.IsNullOrEmpty(prefabName) || (prefabName != "crate_normal_2" &&
                                                     prefabName != "crate_normal_2_food" &&
                                                     prefabName != "crate_normal_2_tools"))
            {
                return;
            }

            // Ensure the player was provided
            string userID = player?.UserIDString;
            if (string.IsNullOrEmpty(userID))
            {
                return;
            }

            // If the player is already in a VIP group, they won't receive a VIP note
            if (BaseTebexAdapter.PluginConfig.VipGroups.Any(group => permission.UserHasGroup(userID, group)))
            {
                return;
            }

            // Make sure we haven't spawned a note too recently for the user.
            if (_lastNoteGiven.TryGetValue(userID, out DateTime lastGivenTime) &&
                (DateTime.Now - lastGivenTime).Seconds < BaseTebexAdapter.PluginConfig.NoteCooldown)
            {
                return;
            }

            // Spawn chance check
            if (Oxide.Core.Random.Range(0.0f, 1.0f) > BaseTebexAdapter.PluginConfig.NoteSpawnChance)
            {
                return;
            }

            // Create the note
            Item item = ItemManager.CreateByName("note", 1, 0UL);
            if (item == null)
            {
                return;
            }

            List<string> messages = BaseTebexAdapter.PluginConfig.NoteMessages["en"];
            string message = messages[Oxide.Core.Random.Range(0, messages.Count)];
            string vipCode = BaseTebexAdapter.PluginConfig.VipCodes[Oxide.Core.Random.Range(0, BaseTebexAdapter.PluginConfig.VipCodes.Count)];

            var info = BaseTebexAdapter.Cache.Instance.Get("information").Value as TebexApi.TebexStoreInfo;
            if (info != null)
            {
                item.text = string.Format(message, player.displayName, info.AccountInfo.Domain, vipCode);
                item.MarkDirty();
                player.GiveItem(item, BaseEntity.GiveItemReason.Generic);

                _lastNoteGiven[userID] = DateTime.Now;                
            }
            else
            {
                _adapter.LogDebug("Store information not present in cache when trying to spawn VIP note!");
            }
        }
        #endif
        
        private void OnServerShutdown()
        {
            // Make sure join queue is always empties on shutdown
            _adapter.ProcessJoinQueue();
        }

        private void PrintCategories(IPlayer player, List<TebexApi.Category> categories)
        {
            // Index counter for selecting displayed items
            var categoryIndex = 1;
            var packIndex = 1;

            // Line separator for category response
            _adapter.ReplyPlayer(player, "---------------------------------");

            // Sort categories in order and display
            var orderedCategories = categories.OrderBy(category => category.Order).ToList();
            for (int i = 0; i < categories.Count; i++)
            {
                var listing = orderedCategories[i];
                _adapter.ReplyPlayer(player, $"[C{categoryIndex}] {listing.Name}");
                categoryIndex++;

                // Show packages for the category in order from API
                if (listing.Packages.Count > 0)
                {
                    var packages = listing.Packages.OrderBy(category => category.Order).ToList();
                    _adapter.ReplyPlayer(player, $"Packages");
                    foreach (var package in packages)
                    {
                        // Add additional flair on sales
                        if (package.Sale != null && package.Sale.Active)
                        {
                            _adapter.ReplyPlayer(player,
                                $"-> [P{packIndex}] {package.Name} {package.Price - package.Sale.Discount} (SALE {package.Sale.Discount} off)");
                        }
                        else
                        {
                            _adapter.ReplyPlayer(player, $"-> [P{packIndex}] {package.Name} {package.Price}");
                        }

                        packIndex++;
                    }
                }

                // At the end of each category add a line separator
                _adapter.ReplyPlayer(player, "---------------------------------");
            }
        }

        private static void PrintPackages(IPlayer player, List<TebexApi.Package> packages)
        {
            // Index counter for selecting displayed items
            var packIndex = 1;

            _adapter.ReplyPlayer(player, "---------------------------------");
            _adapter.ReplyPlayer(player, "      PACKAGES AVAILABLE         ");
            _adapter.ReplyPlayer(player, "---------------------------------");

            // Sort categories in order and display
            var orderedPackages = packages.OrderBy(package => package.Order).ToList();
            for (var i = 0; i < packages.Count; i++)
            {
                var package = orderedPackages[i];
                // Add additional flair on sales
                _adapter.ReplyPlayer(player, $"[P{packIndex}] {package.Name}");
                _adapter.ReplyPlayer(player, $"Category: {package.Category.Name}");
                _adapter.ReplyPlayer(player, $"Description: {package.Description}");

                if (package.Sale != null && package.Sale.Active)
                {
                    _adapter.ReplyPlayer(player,
                        $"Original Price: {package.Price} {package.GetFriendlyPayFrequency()}  SALE: {package.Sale.Discount} OFF!");
                }
                else
                {
                    _adapter.ReplyPlayer(player, $"Price: {package.Price} {package.GetFriendlyPayFrequency()}");
                }

                _adapter.ReplyPlayer(player,
                    $"Purchase with 'tebex.checkout P{packIndex}' or 'tebex.checkout {package.Id}'");
                _adapter.ReplyPlayer(player, "--------------------------------");

                packIndex++;
            }
        }

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(GetDefaultConfig(), true, "oxide/config/Tebex.json");
        }

        private BaseTebexAdapter.TebexConfig GetDefaultConfig()
        {
            return new BaseTebexAdapter.TebexConfig();
        }

        [Command("tebex.secret", "tebex:secret")]
        private void TebexSecretCommand(IPlayer player, string command, string[] args)
        {
            // Secret can only be ran as the admin
            if (!player.HasPermission("tebex.secret"))
            {
                _adapter.ReplyPlayer(player, "You do not have permission to run this command.");
                _adapter.ReplyPlayer(player, "If you are an admin, grant permission to use `tebex.secret`");
                return;
            }

            if (args.Length != 1)
            {
                _adapter.ReplyPlayer(player, "Invalid syntax. Usage: \"tebex.secret <secret>\"");
                return;
            }

            _adapter.ReplyPlayer(player, "Setting your secret key...");
            BaseTebexAdapter.PluginConfig.SecretKey = args[0];
            Config.WriteObject(BaseTebexAdapter.PluginConfig);

            // Reset store info so that we don't fetch from the cache
            BaseTebexAdapter.Cache.Instance.Remove("information");

            // Any failure to set secret key is logged to console automatically
            _adapter.FetchStoreInfo(info =>
            {
                _adapter.ReplyPlayer(player, $"Successfully set your secret key.");
                _adapter.ReplyPlayer(player,
                    $"Store set as: {info.ServerInfo.Name} for the web store {info.AccountInfo.Name}");
            });
        }

        [Command("tebex.info", "tebex:info", "tebex.information", "tebex:information")]
        private void TebexInfoCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(command))
            {
                _adapter.ReplyPlayer(player, "You do not have permission to run that command.");
                return;
            }

            _adapter.ReplyPlayer(player, "Getting store information...");
            _adapter.FetchStoreInfo(info =>
            {
                _adapter.ReplyPlayer(player, "Information for this server:");
                _adapter.ReplyPlayer(player, $"{info.ServerInfo.Name} for webstore {info.AccountInfo.Name}");
                _adapter.ReplyPlayer(player, $"Server prices are in {info.AccountInfo.Currency.Iso4217}");
                _adapter.ReplyPlayer(player, $"Webstore domain {info.AccountInfo.Domain}");
            });
        }

        [Command("tebex.checkout", "tebex:checkout")]
        private void TebexCheckoutCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(command))
            {
                _adapter.ReplyPlayer(player, "You do not have permission to run that command.");
                return;
            }

            if (player.IsServer)
            {
                _adapter.ReplyPlayer(player,
                    $"{command} cannot be executed via server. Use tebex:sendlink <username> <packageId> to specify a target player.");
                return;
            }

            // Only argument will be the package ID of the item in question
            if (args.Length != 1)
            {
                _adapter.ReplyPlayer(player, "Invalid syntax: Usage \"tebex.checkout <packageId>\"");
                return;
            }

            // Lookup the package by provided input and respond with the checkout URL
            var package = _adapter.GetPackageByShortCodeOrId(args[0].Trim());
            if (package == null)
            {
                _adapter.ReplyPlayer(player, "A package with that ID was not found.");
                return;
            }

            _adapter.ReplyPlayer(player, "Creating your checkout URL...");
            _adapter.CreateCheckoutUrl(player.Name, package, checkoutUrl =>
            {
                player.Command("chat.add", 0, player.Id, "Please visit the following URL to complete your purchase:");
                player.Command("chat.add", 0, player.Id, $"{checkoutUrl.Url}");
            }, error => { _adapter.ReplyPlayer(player, $"{error.ErrorMessage}"); });
        }

        [Command("tebex.help", "tebex:help")]
        private void TebexHelpCommand(IPlayer player, string command, string[] args)
        {
            _adapter.ReplyPlayer(player, "Tebex Commands Available:");
            if (player.IsAdmin) //Always show help to admins regardless of perms, for new server owners
            {
                _adapter.ReplyPlayer(player, "-- Administrator Commands --");
                _adapter.ReplyPlayer(player, "tebex.secret <secretKey>          - Sets your server's secret key.");
                _adapter.ReplyPlayer(player, "tebex.debug <on/off>              - Enables or disables debug logging.");
                _adapter.ReplyPlayer(player,
                    "tebex.sendlink <player> <packId>  - Sends a purchase link to the provided player.");
                _adapter.ReplyPlayer(player,
                    "tebex.forcecheck                  - Forces the command queue to check for any pending purchases.");
                _adapter.ReplyPlayer(player,
                    "tebex.refresh                     - Refreshes store information, packages, categories, etc.");
                _adapter.ReplyPlayer(player,
                    "tebex.report                      - Generates a report for the Tebex support team.");
                _adapter.ReplyPlayer(player,
                    "tebex.ban <playerId>              - Bans a player from using your Tebex store.");
                _adapter.ReplyPlayer(player,
                    "tebex.lookup <playerId>           - Looks up store statistics for the given player.");
            }

            _adapter.ReplyPlayer(player, "-- User Commands --");
            _adapter.ReplyPlayer(player,
                "tebex.info                       - Get information about this server's store.");
            _adapter.ReplyPlayer(player,
                "tebex.categories                 - Shows all item categories available on the store.");
            _adapter.ReplyPlayer(player,
                "tebex.packages <opt:categoryId>  - Shows all item packages available in the store or provided category.");
            _adapter.ReplyPlayer(player,
                "tebex.checkout <packId>          - Creates a checkout link for an item. Visit to purchase.");
        }

        [Command("tebex.debug", "tebex:debug")]
        private void TebexDebugCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(command))
            {
                _adapter.ReplyPlayer(player, "You do not have permission to run that command.");
                return;
            }

            if (args.Length != 1)
            {
                _adapter.ReplyPlayer(player, "Usage: tebex.debug <on/off>");
                return;
            }

            if (args[0].Equals("on"))
            {
                BaseTebexAdapter.PluginConfig.DebugMode = true;
                Config.WriteObject(BaseTebexAdapter.PluginConfig);
                _adapter.ReplyPlayer(player, "Debug mode is enabled.");
            }
            else if (args[0].Equals("off"))
            {
                BaseTebexAdapter.PluginConfig.DebugMode = false;
                Config.WriteObject(BaseTebexAdapter.PluginConfig);
                _adapter.ReplyPlayer(player, "Debug mode is disabled.");
            }
            else
            {
                _adapter.ReplyPlayer(player, "Usage: tebex.debug <on/off>");
            }
        }

        [Command("tebex.forcecheck", "tebex:forcecheck")]
        private void TebexForceCheckCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(command))
            {
                _adapter.ReplyPlayer(player, "You do not have permission to run that command.");
                return;
            }

            _adapter.RefreshStoreInformation(true);
            _adapter.ProcessCommandQueue(true);
            _adapter.ProcessJoinQueue(true);
            _adapter.DeleteExecutedCommands(true);
        }

        [Command("tebex.refresh", "tebex:refresh")]
        private void TebexRefreshCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(command))
            {
                _adapter.ReplyPlayer(player, "You do not have permission to run that command.");
                return;
            }

            _adapter.ReplyPlayer(player, "Refreshing listings...");
            BaseTebexAdapter.Cache.Instance.Remove("packages");
            BaseTebexAdapter.Cache.Instance.Remove("categories");

            _adapter.RefreshListings((code, body) =>
            {
                if (BaseTebexAdapter.Cache.Instance.HasValid("packages") &&
                    BaseTebexAdapter.Cache.Instance.HasValid("categories"))
                {
                    var packs = (List<TebexApi.Package>)BaseTebexAdapter.Cache.Instance.Get("packages").Value;
                    var categories = (List<TebexApi.Category>)BaseTebexAdapter.Cache.Instance.Get("categories").Value;
                    _adapter.ReplyPlayer(player,
                        $"Fetched {packs.Count} packages out of {categories.Count} categories");
                }
            });
        }

        [Command("tebex.report", "tebex:report")]
        private void TebexReportCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(command))
            {
                _adapter.ReplyPlayer(player, "You do not have permission to run that command.");
                return;
            }

            if (args.Length == 0) // require /confirm to send
            {
                _adapter.ReplyPlayer(player,
                    "Please run `tebex.report confirm 'Your description here'` to submit your report. The following information will be sent to Tebex: ");
                _adapter.ReplyPlayer(player, "- Your game version, store id, and server IP.");
                _adapter.ReplyPlayer(player, "- Your username and IP address.");
                _adapter.ReplyPlayer(player, "- Please include a short description of the issue you were facing.");
            }

            if (args.Length == 2 && args[0] == "confirm")
            {
                _adapter.ReplyPlayer(player, "Sending your report to Tebex...");

                var triageEvent = new TebexTriage.ReportedTriageEvent();
                triageEvent.GameId = $"{game} {server.Version}|{server.Protocol}";
                triageEvent.FrameworkId = "Oxide";
                triageEvent.PluginVersion = GetPluginVersion();
                triageEvent.ServerIp = server.Address.ToString();
                triageEvent.ErrorMessage = "Player Report: " + args[1];
                triageEvent.Trace = "";
                triageEvent.Metadata = new Dictionary<string, string>()
                {

                };
                triageEvent.Username = player.Name + "/" + player.Id;
                triageEvent.UserIp = player.Address;

                _adapter.ReportManualTriageEvent(triageEvent,
                    (code, body) => { _adapter.ReplyPlayer(player, "Your report has been sent. Thank you!"); },
                    (code, body) =>
                    {
                        _adapter.ReplyPlayer(player,
                            "An error occurred while submitting your report. Please contact our support team directly.");
                        _adapter.ReplyPlayer(player, "Error: " + body);
                    });

                return;
            }

            _adapter.ReplyPlayer(player, $"Usage: tebex.report <confirm> '<message>'");
        }

        [Command("tebex.ban", "tebex:ban")]
        private void TebexBanCommand(IPlayer commandRunner, string command, string[] args)
        {
            if (!commandRunner.HasPermission("tebex.ban"))
            {
                _adapter.ReplyPlayer(commandRunner, $"{command} can only be used by administrators.");
                return;
            }

            if (args.Length < 2)
            {
                _adapter.ReplyPlayer(commandRunner, $"Usage: tebex.ban <playerName> <reason>");
                return;
            }

            var player = players.FindPlayer(args[0].Trim());
            if (player == null)
            {
                _adapter.ReplyPlayer(commandRunner, $"Could not find that player on the server.");
                return;
            }

            var reason = string.Join(" ", args.Skip(1));
            _adapter.ReplyPlayer(commandRunner, $"Processing ban for player {player.Name} with reason '{reason}'");
            _adapter.BanPlayer(player.Name, player.Address, reason,
                (code, body) => { _adapter.ReplyPlayer(commandRunner, "Player banned successfully."); },
                error => { _adapter.ReplyPlayer(commandRunner, $"Could not ban player. {error.ErrorMessage}"); });
        }

        [Command("tebex.unban", "tebex:unban")]
        private void TebexUnbanCommand(IPlayer commandRunner, string command, string[] args)
        {
            if (!commandRunner.IsAdmin)
            {
                _adapter.ReplyPlayer(commandRunner, $"{command} can only be used by administrators.");
                return;
            }

            _adapter.ReplyPlayer(commandRunner, $"You must unban players via your webstore.");
        }

        [Command("tebex.categories", "tebex:categories", "tebex.listings", "tebex:listings")]
        private void TebexCategoriesCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(command))
            {
                _adapter.ReplyPlayer(player, "You do not have permission to run that command.");
                return;
            }

            _adapter.GetCategories(categories => { PrintCategories(player, categories); });
        }

        [Command("tebex.packages", "tebex:packages")]
        private void TebexPackagesCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(command))
            {
                _adapter.ReplyPlayer(player, "You do not have permission to run that command.");
                return;
            }

            _adapter.GetPackages(packages => { PrintPackages(player, packages); });
        }

        [Command("tebex.lookup", "tebex:lookup")]
        private void TebexLookupCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(command))
            {
                _adapter.ReplyPlayer(player, "You do not have permission to run that command.");
                return;
            }

            if (args.Length != 1)
            {
                _adapter.ReplyPlayer(player, $"Usage: tebex.lookup <playerId/playerUsername>");
                return;
            }

            // Try to find the given player
            var target = players.FindPlayer(args[0]);
            if (target == null)
            {
                _adapter.ReplyPlayer(player, $"Could not find a player matching the name or id {args[0]}.");
                return;
            }

            _adapter.GetUser(target.Id, (code, body) =>
            {
                var response = JsonConvert.DeserializeObject<TebexApi.UserInfoResponse>(body);
                _adapter.ReplyPlayer(player, $"Username: {response.Player.Username}");
                _adapter.ReplyPlayer(player, $"Id: {response.Player.Id}");
                _adapter.ReplyPlayer(player, $"Payments Total: ${response.Payments.Sum(payment => payment.Price)}");
                _adapter.ReplyPlayer(player, $"Chargeback Rate: {response.ChargebackRate}%");
                _adapter.ReplyPlayer(player, $"Bans Total: {response.BanCount}");
                _adapter.ReplyPlayer(player, $"Payments: {response.Payments.Count}");
            }, error => { _adapter.ReplyPlayer(player, error.ErrorMessage); });
        }

        [Command("tebex.sendlink", "tebex:sendlink")]
        private void TebexSendLinkCommand(IPlayer commandRunner, string command, string[] args)
        {
            if (!commandRunner.HasPermission("tebex.sendlink"))
            {
                _adapter.ReplyPlayer(commandRunner, "You must be an administrator to run this command.");
                return;
            }

            if (args.Length != 2)
            {
                _adapter.ReplyPlayer(commandRunner, "Usage: tebex.sendlink <username> <packageId>");
                return;
            }

            var username = args[0].Trim();
            var package = _adapter.GetPackageByShortCodeOrId(args[1].Trim());
            if (package == null)
            {
                _adapter.ReplyPlayer(commandRunner, "A package with that ID was not found.");
                return;
            }

            _adapter.ReplyPlayer(commandRunner,
                $"Creating checkout URL with package '{package.Name}'|{package.Id} for player {username}");
            var player = players.FindPlayer(username);
            if (player == null)
            {
                _adapter.ReplyPlayer(commandRunner, $"Couldn't find that player on the server.");
                return;
            }

            _adapter.CreateCheckoutUrl(player.Name, package, checkoutUrl =>
            {
                player.Command("chat.add", 0, player.Id, "Please visit the following URL to complete your purchase:");
                player.Command("chat.add", 0, player.Id, $"{checkoutUrl.Url}");
            }, error => { _adapter.ReplyPlayer(player, $"{error.ErrorMessage}"); });
        }
    }    public abstract class BaseTebexAdapter
    {
        public static BaseTebexAdapter Instance => _adapterInstance.Value;
        private static readonly Lazy<BaseTebexAdapter> _adapterInstance = new Lazy<BaseTebexAdapter>();
        
        public static TebexConfig PluginConfig { get; set; } = new TebexConfig();
        
        /** For rate limiting command queue based on next_check */
        private static DateTime _nextCheckCommandQueue = DateTime.Now;
        
        // Time checks for our plugin timers.
        private static DateTime _nextCheckDeleteCommands = DateTime.Now;
        private static DateTime _nextCheckJoinQueue = DateTime.Now;
        private static DateTime _nextCheckRefresh = DateTime.Now;
        
        private static List<TebexApi.TebexJoinEventInfo> _eventQueue = new List<TebexApi.TebexJoinEventInfo>();
        
        /** For storing successfully executed commands and deleting them from API */
        protected static readonly List<TebexApi.Command> ExecutedCommands = new List<TebexApi.Command>();

        /** Allow pausing all web requests if rate limits are received from remote */
        protected bool IsRateLimited = false;
        
        public abstract void Init();

        public void DeleteExecutedCommands(bool ignoreWaitCheck = false)
        {
            LogDebug("Deleting executed commands...");
            
            if (!CanProcessNextDeleteCommands() && !ignoreWaitCheck)
            {
                LogDebug("Skipping check for completed commands - not time to be processed");
                return;
            }
            
            if (ExecutedCommands.Count == 0)
            {
                LogDebug("  No commands to flush.");
                return;
            }

            LogDebug($"  Found {ExecutedCommands.Count} commands to flush.");

            List<int> ids = new List<int>();
            foreach (var command in ExecutedCommands)
            {
                ids.Add(command.Id);
            }

            _nextCheckDeleteCommands = DateTime.Now.AddSeconds(60);
            TebexApi.Instance.DeleteCommands(ids.ToArray(), (code, body) =>
            {
                LogDebug("Successfully flushed completed commands.");
                ExecutedCommands.Clear();
            }, (error) =>
            {
                LogDebug($"Failed to flush completed commands: {error.ErrorMessage}");
            }, (code, body) =>
            {
                LogDebug($"Unexpected error while flushing completed commands. API response code {code}. Response body follows:");
                LogDebug(body);
            });
        }

        /**
         * Logs a warning to the console and game log.
         */
        public abstract void LogWarning(string message);

        /**
         * Logs an error to the console and game log.
         */
        public abstract void LogError(string message);

        /**
             * Logs information to the console and game log.
             */
        public abstract void LogInfo(string message);

        /**
             * Logs debug information to the console and game log if debug mode is enabled.
             */
        public abstract void LogDebug(string message);

        public void OnUserConnected(string steam64Id, string ip)
        {
            var joinEvent = new TebexApi.TebexJoinEventInfo(steam64Id, "server.join", DateTime.Now, ip);
            _eventQueue.Add(joinEvent);

            // If we're already over a threshold, go ahead and send the events.
            if (_eventQueue.Count > 10)
            {
                ProcessJoinQueue();
            }
        }
        
        public class TebexConfig
        {
            // Enables additional debug logging, which may show raw user info in console.
            public bool DebugMode = false;

            // Automatically sends detected issues to Tebex 
            public bool AutoReportingEnabled = true;
            
            //public bool AllowGui = false;
            public string SecretKey = "your-secret-key-here";
            public int CacheLifetime = 30;
            
            //#if RUST
            [JsonProperty(PropertyName = "VIP Notes Enabled")]
            public bool VipNotesEnabled { get; set; } = false;
            
            [JsonProperty(PropertyName = "VIP Codes")]
            public List<string> VipCodes { get; set; } = new List<string>();
            
            [JsonProperty(PropertyName = "VIP Groups")]
            public List<string> VipGroups { get; set; } = new List<string>();

            [JsonProperty(PropertyName = "Note Spawn Chance")]
            public float NoteSpawnChance { get; set; } = 0.02f; // 2%
            
            [JsonProperty(PropertyName = "Note Cooldown (Seconds)")]
            public float NoteCooldown { get; set; } = 600; // 10 minutes

            [JsonProperty(PropertyName = "Note Messages")]
            public Dictionary<string, List<string>> NoteMessages { get; set; } = new Dictionary<string, List<string>>
            {
                {
                    "en", new List<string>
                    {
                        "Hey {0}, grab your exclusive 10% OFF VIP offer with code {2} at {1}! Limited time only!",
                        "{0}, seize your special discount! Use code {2} at {1} for a limited time offer!",
                        "Special surprise for you, {0}! Use code {2} at {1} for 10% off and enjoy your VIP benefits!"
                    }
                },
            };
            
            //#endif
        }
        
        public class Cache
        {
            public static Cache Instance => _cacheInstance.Value;
            private static readonly Lazy<Cache> _cacheInstance = new Lazy<Cache>(() => new Cache());
            private static Dictionary<string, CachedObject> _cache = new Dictionary<string, CachedObject>();
            public CachedObject Get(string key)
            {
                if (_cache.ContainsKey(key))
                {
                    return _cache[key];
                }
                return null;
            }

            public void Set(string key, CachedObject obj)
            {
                _cache[key] = obj;
            }

            public bool HasValid(string key)
            {
                return _cache.ContainsKey(key) && !_cache[key].HasExpired();
            }

            public void Clear()
            {
                _cache.Clear();
            }

            public void Remove(string key)
            {
                _cache.Remove(key);
            }
        }

        public class CachedObject
        {
            public object Value { get; private set; }
            private DateTime _expires;

            public CachedObject(object obj, int minutesValid)
            {
                Value = obj;
                _expires = DateTime.Now.AddMinutes(minutesValid);
            }

            public bool HasExpired()
            {
                return DateTime.Now > _expires;
            }
        }
        
        /** Callback type to use /information response */
        public delegate void FetchStoreInfoResponse(TebexApi.TebexStoreInfo info);

        /**
             * Returns the store's /information payload. Info is cached according to configured cache lifetime.
             */
        public void FetchStoreInfo(FetchStoreInfoResponse response)
        {
            if (Cache.Instance.HasValid("information"))
            {
                response?.Invoke((TebexApi.TebexStoreInfo)Cache.Instance.Get("information").Value);
            }
            else
            {
                TebexApi.Instance.Information((code, body) =>
                {
                    var storeInfo = JsonConvert.DeserializeObject<TebexApi.TebexStoreInfo>(body);
                    if (storeInfo == null)
                    {
                        ReportAutoTriageEvent(new TebexTriage.AutoTriageEvent());
                        LogError("Failed to parse fetched store information: ");
                        LogError(body);
                        return;
                    }

                    Cache.Instance.Set("information", new CachedObject(storeInfo, PluginConfig.CacheLifetime));
                    response?.Invoke(storeInfo);
                });
            }
        }

        /** Callback type for response from creating checkout url */
        public delegate void CreateCheckoutUrlResponse(TebexApi.CheckoutUrlPayload checkoutUrl);

        public TebexApi.Package GetPackageByShortCodeOrId(string value)
        {
            var shortCodes = (Dictionary<String, TebexApi.Package>)Cache.Instance.Get("packageShortCodes").Value;
            if (shortCodes.ContainsKey(value))
            {
                return shortCodes[value];
            }

            // No short code found, assume it's a package ID
            var packages = (List<TebexApi.Package>)Cache.Instance.Get("packages").Value;
            foreach (var package in packages)
            {
                if (package.Id.ToString() == value)
                {
                    return package;
                }
            }

            // Package not found
            return null;
        }

        /**
             * Refreshes cached categories and packages from the Tebex API. Can be used by commands or with no arguments
             * to update the information while the server is idle.
             */
        public void RefreshListings(TebexApi.ApiSuccessCallback onSuccess = null)
        {
            // Get our categories from the /listing endpoint as it contains all category data
            TebexApi.Instance.GetListing((code, body) =>
            {
                var response = JsonConvert.DeserializeObject<TebexApi.ListingsResponse>(body);
                if (response == null)
                {
                    LogError("Could not get refresh all listings!:");
                    LogError(body);
                    return;
                }

                Cache.Instance.Set("categories", new CachedObject(response.categories, PluginConfig.CacheLifetime));
                if (onSuccess != null)
                {
                    onSuccess.Invoke(code, body);    
                }
            });

            // Get our packages from a verbose get all packages call so that we always have the description
            // of the package cached.
            TebexApi.Instance.GetAllPackages(true, (code, body) =>
            {
                var response = JsonConvert.DeserializeObject<List<TebexApi.Package>>(body);
                if (response == null)
                {
                    LogError("Could not get refresh package listings!");
                    LogError(body);
                    return;
                }

                Cache.Instance.Set("packages", new CachedObject(response, PluginConfig.CacheLifetime));

                // Generate and save shortcodes for each package
                var orderedPackages = response.OrderBy(package => package.Order).ToList();
                var shortCodes = new Dictionary<String, TebexApi.Package>();
                for (var i = 0; i < orderedPackages.Count; i++)
                {
                    var package = orderedPackages[i];
                    shortCodes.Add($"P{i + 1}", package);
                }

                Cache.Instance.Set("packageShortCodes", new CachedObject(shortCodes, PluginConfig.CacheLifetime));
                onSuccess?.Invoke(code, body);
            });
        }

        /** Callback type for getting all categories */
        public delegate void GetCategoriesResponse(List<TebexApi.Category> categories);

        /**
             * Gets all categories and their packages (no description) from the API. Response is cached according to the
             * configured cache lifetime.
             */
        public void GetCategories(GetCategoriesResponse onSuccess,
            TebexApi.ServerErrorCallback onServerError = null)
        {
            if (Cache.Instance.HasValid("categories"))
            {
                onSuccess.Invoke((List<TebexApi.Category>)Cache.Instance.Get("categories").Value);
            }
            else
            {
                TebexApi.Instance.GetListing((code, body) =>
                {
                    var response = JsonConvert.DeserializeObject<TebexApi.ListingsResponse>(body);
                    if (response == null)
                    {
                        onServerError?.Invoke(code, body);
                        return;
                    }

                    Cache.Instance.Set("categories", new CachedObject(response.categories, PluginConfig.CacheLifetime));
                    onSuccess.Invoke(response.categories);
                });
            }
        }

        /** Callback type for working with packages received from the API */
        public delegate void GetPackagesResponse(List<TebexApi.Package> packages);

        /** Gets all package info from API. Response is cached according to the configured cache lifetime. */
        public void GetPackages(GetPackagesResponse onSuccess,
            TebexApi.ServerErrorCallback onServerError = null)
        {
            try
            {
                if (Cache.Instance.HasValid("packages"))
                {
                    onSuccess.Invoke((List<TebexApi.Package>)Cache.Instance.Get("packages").Value);
                }
                else
                {
                    // Updates both packages and shortcodes in the cache
                    RefreshListings((code, body) =>
                    {
                        onSuccess.Invoke((List<TebexApi.Package>)Cache.Instance.Get("packages").Value);
                    });
                }
            }
            catch (Exception e)
            {
                ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent("Raised exception while refreshing packages", new Dictionary<string, string>
                {
                    {"cacheHasValid", Cache.Instance.HasValid("packages").ToString()},
                    {"error", e.Message},
                    {"trace", e.StackTrace}
                }));
                LogError("An error occurred while getting your store's packages.");
            }
        }

        // Periodically keeps store info updated from the API
        public void RefreshStoreInformation(bool ignoreWaitCheck = false)
        {
            LogDebug("Refreshing store information...");
            
            // Calling places the information in the cache
            if (!CanProcessNextRefresh() && !ignoreWaitCheck)
            {
                LogDebug("  Skipping store info refresh - not time to be processed");
                return;
            }
            
            _nextCheckRefresh = DateTime.Now.AddMinutes(15);
            FetchStoreInfo(info => { });
        }
        
        public void ProcessJoinQueue(bool ignoreWaitCheck = false)
        {
            LogDebug("Processing player join queue...");
            
            if (!CanProcessNextJoinQueue() && !ignoreWaitCheck)
            {
                LogDebug("  Skipping join queue - not time to be processed");
                return;
            }
            
            _nextCheckJoinQueue = DateTime.Now.AddSeconds(60);
            if (_eventQueue.Count > 0)
            {
                LogDebug($"  Found {_eventQueue.Count} join events.");
                TebexApi.Instance.PlayerJoinEvent(_eventQueue, (code, body) =>
                    {
                        LogDebug("Join queue cleared successfully.");
                        _eventQueue.Clear();
                    }, error =>
                    {
                        ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent("API error while processing join queue", new Dictionary<string, string>()
                        {
                            {"error",error.ErrorMessage},
                        }));
                        LogError($"Could not process join queue - error response from API: {error.ErrorMessage}");
                    },
                    (code, body) =>
                    {
                        ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent("Server error while processing join queue", new Dictionary<string, string>()
                        {
                            {"code",code.ToString()},
                            {"responseBody",body},
                        }));
                        LogError("Could not process join queue - unexpected server error.");
                        LogError(body);
                    });
            }
            else // Empty queue
            {
                LogDebug($"  No recent join events.");
            }
        }
        
        public bool CanProcessNextCommandQueue()
        {
            return DateTime.Now > _nextCheckCommandQueue;
        }

        public bool CanProcessNextDeleteCommands()
        {
            return DateTime.Now > _nextCheckDeleteCommands;
        }
        
        public bool CanProcessNextJoinQueue()
        {
            return DateTime.Now > _nextCheckJoinQueue;
        }
        
        public bool CanProcessNextRefresh()
        {
            return DateTime.Now > _nextCheckRefresh;
        }
        
        public void ProcessCommandQueue(bool ignoreWaitCheck = false)
        {
            LogDebug("Processing command queue...");
            
            if (!CanProcessNextCommandQueue() && !ignoreWaitCheck)
            {
                var secondsToWait = (int)(_nextCheckCommandQueue - DateTime.Now).TotalSeconds;
                LogDebug($"  Tried to run command queue, but should wait another {secondsToWait} seconds.");
                return;
            }

            // Get the state of the command queue
            TebexApi.Instance.GetCommandQueue((cmdQueueCode, cmdQueueResponseBody) =>
            {
                var response = JsonConvert.DeserializeObject<TebexApi.CommandQueueResponse>(cmdQueueResponseBody);
                if (response == null)
                {
                    LogError("Failed to get command queue. Could not parse response from API. Response body follows:");
                    LogError(cmdQueueResponseBody);
                    return;
                }

                // Set next available check time
                _nextCheckCommandQueue = DateTime.Now.AddSeconds(response.Meta.NextCheck);

                // Process offline commands immediately
                if (response.Meta != null && response.Meta.ExecuteOffline)
                {
                    LogDebug("Requesting offline commands from API...");
                    TebexApi.Instance.GetOfflineCommands((code, offlineCommandsBody) =>
                    {
                        var offlineCommands = JsonConvert.DeserializeObject<TebexApi.OfflineCommandsResponse>(offlineCommandsBody);
                        if (offlineCommands == null)
                        {
                            ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent("Failed to parse offline commands response body", new Dictionary<string, string>()
                            {
                                {"code", code.ToString()},
                                {"responseBody", offlineCommandsBody}
                            }));
                            LogError("Failed to get offline commands. Could not parse response from API. Response body follows:");
                            LogError(offlineCommandsBody);
                            return;
                        }

                        LogDebug($"Found {offlineCommands.Commands.Count} offline commands to execute.");
                        foreach (TebexApi.Command command in offlineCommands.Commands)
                        {
                            var parsedCommand = ExpandOfflineVariables(command.CommandToRun, command.Player);
                            var splitCommand = parsedCommand.Split(' ');
                            var commandName = splitCommand[0];
                            var args = splitCommand.Skip(1);
                            
                            LogDebug($"Executing offline command: `{parsedCommand}`");
                            ExecuteOfflineCommand(command, null, commandName, args.ToArray());
                            ExecutedCommands.Add(command);
                            LogDebug($"Executed commands queue has {ExecutedCommands.Count} commands");
                        }
                    }, (error) =>
                    {
                        ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent("Error response from API while processing offline commands", new Dictionary<string, string>()
                        {
                            {"error",error.ErrorMessage},
                            {"errorCode", error.ErrorCode.ToString()}
                        }));
                        LogError($"Error response from API while processing offline commands: {error.ErrorMessage}");
                    }, (offlineComandsCode, offlineCommandsServerError) =>
                    {
                        ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent("Server error from API while processing offline commands", new Dictionary<string, string>()
                        {
                            {"code", offlineComandsCode.ToString()},
                            {"responseBody", offlineCommandsServerError}
                        }));
                        LogError("Unexpected error response from API while processing offline commands");
                        LogError(offlineCommandsServerError);
                    });
                }
                else
                {
                    LogDebug("No offline commands to execute.");
                }

                // Process any online commands 
                LogDebug($"Found {response.Players.Count} due players in the queue");
                foreach (var duePlayer in response.Players)
                {
                    LogDebug($"Processing online commands for player {duePlayer.Name}...");
                    if (!IsPlayerOnline(duePlayer.UUID))
                    {
                        LogDebug($"> Player {duePlayer.Name} has online commands but is not connected. Skipping.");
                        continue;
                    }
                    
                    TebexApi.Instance.GetOnlineCommands(duePlayer.Id,
                        (onlineCommandsCode, onlineCommandsResponseBody) =>
                        {
                            LogDebug(onlineCommandsResponseBody);
                            var onlineCommands =
                                JsonConvert.DeserializeObject<TebexApi.OnlineCommandsResponse>(
                                    onlineCommandsResponseBody);
                            if (onlineCommands == null)
                            {
                                ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent("Could not parse response from API while processing online commands of player", new Dictionary<string, string>()
                                {
                                    {"playerName", duePlayer.Name},
                                    {"code", onlineCommandsCode.ToString()},
                                    {"responseBody", onlineCommandsResponseBody}
                                }));
                                
                                LogError($"> Failed to get online commands for ${duePlayer.Name}. Could not unmarshal response from API.");
                                return;
                            }

                            LogDebug($"> Processing {onlineCommands.Commands.Count} commands for this player...");
                            foreach (var command in onlineCommands.Commands)
                            {
                                object playerRef = GetPlayerRef(onlineCommands.Player.Id);
                                if (playerRef == null)
                                {
                                    LogError($"No reference found for expected online player. Commands will be skipped for this player.");
                                    break;
                                }

                                var parsedCommand = ExpandUsernameVariables(command.CommandToRun, playerRef);
                                var splitCommand = parsedCommand.Split(' ');
                                var commandName = splitCommand[0];
                                var args = splitCommand.Skip(1);
                                
                                LogDebug($"Pre-execution: {parsedCommand}");
                                var success = ExecuteOnlineCommand(command, playerRef, commandName, args.ToArray());
                                LogDebug($"Post-execution: {parsedCommand}");
                                if (success)
                                {
                                    ExecutedCommands.Add(command);    
                                }
                            }
                        }, tebexError => // Error for this player's online commands
                        {
                            ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent("API responded with error while processing online player's commands", new Dictionary<string, string>()
                            {
                                {"playerName", duePlayer.Name},
                                {"code", tebexError.ErrorCode.ToString()},
                                {"message", tebexError.ErrorMessage}
                            }));
                            
                            LogError("Failed to get due online commands due to error response from API.");
                            LogError(tebexError.ErrorMessage);
                        });
                }
            }, tebexError => // Error for get due players
            {
                ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent("API responded with error while getting due players", new Dictionary<string, string>()
                {
                    {"code", tebexError.ErrorCode.ToString()},
                    {"message", tebexError.ErrorMessage}
                }));
                LogError("Failed to get due players due to error response from API.");
                LogError(tebexError.ErrorMessage);
            });
        }

        /**
     * Creates a checkout URL for a player to purchase the given package.
     */
        public void CreateCheckoutUrl(string playerName, TebexApi.Package package,
            CreateCheckoutUrlResponse success,
            TebexApi.ApiErrorCallback error)
        {
            TebexApi.Instance.CreateCheckoutUrl(package.Id, playerName, (code, body) =>
            {
                var responsePayload = JsonConvert.DeserializeObject<TebexApi.CheckoutUrlPayload>(body);
                if (responsePayload == null)
                {
                    return;
                }

                success?.Invoke(responsePayload);
            }, error);
        }

        public delegate void GetGiftCardsResponse(List<TebexApi.GiftCard> giftCards);

        public delegate void GetGiftCardByIdResponse(TebexApi.GiftCard giftCards);

        public void GetGiftCards(GetGiftCardsResponse success, TebexApi.ApiErrorCallback error)
        {
            //TODO
        }

        public void GetGiftCardById(GetGiftCardByIdResponse success, TebexApi.ApiErrorCallback error)
        {
            //TODO
        }

        public void BanPlayer(string playerName, string playerIp, string reason, TebexApi.ApiSuccessCallback onSuccess,
            TebexApi.ApiErrorCallback onError)
        {
            TebexApi.Instance.CreateBan(reason, playerIp, playerName, onSuccess, onError);
        }

        public void GetUser(string userId, TebexApi.ApiSuccessCallback onSuccess = null,
            TebexApi.ApiErrorCallback onApiError = null, TebexApi.ServerErrorCallback onServerError = null)
        {
            TebexApi.Instance.GetUser(userId, onSuccess, onApiError, onServerError);
        }

        public void GetActivePackagesForCustomer(string playerId, int? packageId = null, TebexApi.ApiSuccessCallback onSuccess = null,
            TebexApi.ApiErrorCallback onApiError = null, TebexApi.ServerErrorCallback onServerError = null)
        {
            TebexApi.Instance.GetActivePackagesForCustomer(playerId, packageId, onSuccess, onApiError, onServerError);
        }
        
        /**
         * Sends a message to the given player.
         */
        public abstract void ReplyPlayer(object player, string message);

        public abstract void ExecuteOfflineCommand(TebexApi.Command command, object playerObj, string commandName, string[] args);
        public abstract bool ExecuteOnlineCommand(TebexApi.Command command, object playerObj, string commandName, string[] args);
        
        public abstract bool IsPlayerOnline(string playerRefId);
        public abstract object GetPlayerRef(string playerId);

        /**
         * As we support the use of different games across the Tebex Store
         * we offer slightly different ways of getting a customer username or their ID.
         * 
         * All games support the same default variables, but some games may have additional variables.
         */
        public abstract string ExpandUsernameVariables(string input, object playerObj);

        public abstract string ExpandOfflineVariables(string input, TebexApi.PlayerInfo info);
        
        public abstract void MakeWebRequest(string endpoint, string body, TebexApi.HttpVerb verb,
            TebexApi.ApiSuccessCallback onSuccess, TebexApi.ApiErrorCallback onApiError,
            TebexApi.ServerErrorCallback onServerError);

        public abstract TebexTriage.AutoTriageEvent FillAutoTriageParameters(TebexTriage.AutoTriageEvent partialEvent);
        
        public void ReportAutoTriageEvent(TebexTriage.AutoTriageEvent autoTriageEvent)
        {
            if (!PluginConfig.AutoReportingEnabled)
            {
                return;
            }

            // Make sure we don't try to report triage events about ourselves if the triage API has failed.
            string requestUrl = "";
            var requestIncluded = autoTriageEvent.Metadata.TryGetValue("request", out requestUrl);
            if (requestUrl == null)
            {
                requestUrl = "";
            }
            if (requestIncluded && requestUrl.Contains(TebexApi.TebexTriageUrl))
            {
                return;
            }
            
            // Determine store name
            // Determine the store info, if we have it.
            var storeName = "";
            var storeUrl = "";
            
            if (Cache.Instance.HasValid("information"))
            {
                TebexApi.TebexStoreInfo storeInfo = (TebexApi.TebexStoreInfo)Cache.Instance.Get("information").Value;
                storeName = storeInfo.AccountInfo.Name;
                storeUrl = storeInfo.AccountInfo.Domain;
            }

            autoTriageEvent.StoreName = storeName;
            autoTriageEvent.StoreUrl = storeUrl;
            
            // Fill missing params using the framework adapter
            autoTriageEvent = FillAutoTriageParameters(autoTriageEvent);
            
            MakeWebRequest(TebexApi.TebexTriageUrl, JsonConvert.SerializeObject(autoTriageEvent),
                TebexApi.HttpVerb.POST,
                (code, body) =>
                {
                    LogDebug("Successfully submitted auto triage event");
                }, (error) =>
                {
                    LogDebug("Triage API responded with error: " + error.ErrorMessage);
                }, (code, body) =>
                {
                    LogDebug("Triage API encountered a server error while submitting triage event: " + body);
                });
        }

        public void ReportManualTriageEvent(TebexTriage.ReportedTriageEvent reportedTriageEvent, TebexApi.ApiSuccessCallback onSuccess, TebexApi.ServerErrorCallback onError)
        {
            var storeName = "";
            var storeUrl = "";
            
            if (Cache.Instance.HasValid("information"))
            {
                TebexApi.TebexStoreInfo storeInfo = (TebexApi.TebexStoreInfo)Cache.Instance.Get("information").Value;
                storeName = storeInfo.AccountInfo.Name;
                storeUrl = storeInfo.AccountInfo.Domain;
            }

            reportedTriageEvent.StoreName = storeName;
            reportedTriageEvent.StoreUrl = storeUrl;
            
            MakeWebRequest(TebexApi.TebexTriageUrl, JsonConvert.SerializeObject(reportedTriageEvent),
                TebexApi.HttpVerb.POST,
                (code, body) =>
                {
                    LogDebug("Successfully submitted manual triage event");
                    onSuccess(code, body);
                }, (error) =>
                {
                    LogDebug("Triage API responded with error: " + error.ErrorMessage);
                }, (code, body) =>
                {
                    LogDebug("Triage API encountered a server error while submitting manual triage event: " + body);
                    onError(code, body);
                });
        }
    }
    public class TebexOxideAdapter : BaseTebexAdapter
    {
        public static Oxide.Plugins.Tebex Plugin { get; private set; }

        public TebexOxideAdapter(Oxide.Plugins.Tebex plugin)
        {
            Plugin = plugin;
        }

        public override void Init()
        {
            // Initialize timers, hooks, etc. here
            
            /*
             * NOTE: We have noticed interesting behavior with plugin timers here in that Rust attempts to "catch up"
             *  on events that it missed instead of skipping ticks in the event of sleep, lag, etc. This caused
             *  hundreds of events to fire simultaneously for our timers. To handle this we will rate limit the plugin's
             *  requests when a 429 is received.
             */
            Plugin.PluginTimers().Every(121.0f, () =>
            {
                ProcessCommandQueue(false);
            });
            Plugin.PluginTimers().Every(61.0f, () =>
            {
                DeleteExecutedCommands(false);
            });
            Plugin.PluginTimers().Every(61.0f, () =>
            {
                ProcessJoinQueue(false);
            });
            Plugin.PluginTimers().Every((60.0f * 15) + 1.0f, () =>  // Every 15 minutes for store info
            {
                RefreshStoreInformation(false);
            });
        }

        public override void LogWarning(string message)
        {
            Plugin.Warn(message);
        }

        public override void LogError(string message)
        {
            Plugin.Error(message);
        }

        public override void LogInfo(string message)
        {
            Plugin.Info(message);
        }

        public override void LogDebug(string message)
        {
            if (PluginConfig.DebugMode)
            {
                Plugin.Error($"[DEBUG] {message}");    
            }
        }

        /**
             * Sends a web request to the Tebex API. This is just a wrapper around webrequest.Enqueue, but passes through
             * multiple callbacks that can be used to interact with each API function based on the response received.
             */
        public override void MakeWebRequest(string endpoint, string body, TebexApi.HttpVerb verb,
            TebexApi.ApiSuccessCallback onSuccess, TebexApi.ApiErrorCallback onApiError,
            TebexApi.ServerErrorCallback onServerError)
        {
            // Use Oxide request method for the webrequests call. We use HttpVerb in the api so as not to depend on
            // Oxide.
            RequestMethod method;
            Enum.TryParse<RequestMethod>(verb.ToString(), true, out method);
            if (method == null)
            {
                LogDebug($"Unknown HTTP method!: {verb.ToString()} {endpoint} | {body}");
                return;
            }

            var headers = new Dictionary<string, string>
            {
                { "X-Tebex-Secret", PluginConfig.SecretKey },
                { "Content-Type", "application/json" }
            };

            var url = endpoint;
            var logOutStr = $"-> {method.ToString()} {url} | {body}";
            
            LogDebug(logOutStr); // Write the full output entry to a debug log
            if (logOutStr.Length > 256) // Limit any sent size of an output string to 256 characters, to prevent sending too much data
            {
                logOutStr = logOutStr.Substring(0, 251) + "[...]";
            }
            
            if (IsRateLimited)
            {
                LogDebug("Skipping web request as rate limiting is enabled.");
                return;
            }
            
            Plugin.WebRequests().Enqueue(url, body, (code, response) =>
            {
                var truncatedResponse = response;
                if (truncatedResponse.Length > 256) // As above limit any data logged or sent to 256 characters
                {
                    truncatedResponse = truncatedResponse.Substring(0, 251) + "[...]";
                }
                
                var logInStr = $"{code} | '{truncatedResponse}' <- {method.ToString()} {url}";
                LogDebug(logInStr);
                
                // To prevent issues where triage events try to be reported due to server issues on the triage API itself,
                //   handle any triage response callbacks here
                if (url.Contains(TebexApi.TebexTriageUrl))
                {
                    try
                    {
                        switch (code)
                        {
                            case 200:
                            case 201:
                            case 202:
                            case 204:
                                onSuccess?.Invoke(code, response);
                                return;
                            case 400:
                                onServerError?.Invoke(code, response);
                                return;
                            case 500:
                                onServerError?.Invoke(code, response);
                                return;
                            default:
                                LogDebug($"Unexpected response code from plugin logs API: {code}");
                                return;
                        }                        
                    }
                    catch (Exception e)
                    {
                        LogDebug($"Failed to handle automatic error log request: {e.Message}");
                        LogDebug(e.ToString());
                        return;
                    }
                }
                
                // We should never have an HTML response passed to callback functions which might assume is JSON
                if (body.Contains("DOCTYPE html") || body.StartsWith("<html"))
                {
                    LogDebug("> Unexpected html response from web request!");
                    return;
                }
                
                if (code == 200 || code == 201 || code == 202 || code == 204)
                {
                    onSuccess?.Invoke(code, response);
                }
                else if (code == 403) // Admins get a secret key warning on any command that's rejected
                {
                    if (url.Contains(TebexApi.TebexApiBase))
                    {
                        LogError("Your server's secret key is either not set or incorrect.");
                        LogError("tebex.secret <key>\" to set your secret key to the one associated with your webstore.");
                        LogError("Set up your store and get your secret key at https://tebex.io/");
                    }
                }
                else if (code == 429) // Rate limited
                {
                    // Rate limits sent from Tebex enforce a 5 minute cooldown.
                    LogWarning("We are being rate limited by Tebex API. If this issue continues, please report a problem.");
                    LogWarning("Requests will resume after 5 minutes.");
                    Plugin.PluginTimers().Once(60 * 5, () =>
                    {
                        LogDebug("Rate limit timer has elapsed.");
                        IsRateLimited = false;
                    });
                }
                else if (code == 500)
                {
                    ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent("Internal server error from Plugin API",
                        new Dictionary<string, string>
                        {
                            { "request", logOutStr },
                            { "response", logInStr },
                        }));
                    LogDebug(
                        "Internal Server Error from Tebex API. Please try again later. Error details follow below.");
                    LogDebug(response);
                    onServerError?.Invoke(code, response);
                }
                else if (code == 530) // Cloudflare origin error
                {
                    LogDebug("CDN reported error code, web request not completed: " + code);
                    LogDebug(response);
                    onServerError?.Invoke(code, response);
                }
                else if (code == 0)
                {
                    ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent("Request timeout to Plugin API",
                        new Dictionary<string, string>
                        {
                            { "request", logOutStr },
                            { "response", logInStr },
                        }));
                    LogDebug("Request Timeout from Tebex API. Please try again later.");
                }
                else // This should be a general failure error message with a JSON-formatted response from the API.
                {
                    try
                    {
                        var error = JsonConvert.DeserializeObject<TebexApi.TebexError>(response);
                        if (error != null)
                        {
                            ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent(
                                "Plugin API reported general failure", new Dictionary<string, string>
                                {
                                    { "request", logOutStr },
                                    { "error", error.ErrorMessage },
                                }));
                            onApiError?.Invoke(error);
                        }
                        else
                        {
                            ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent(
                                "Plugin API error could not be interpreted!", new Dictionary<string, string>
                                {
                                    { "request", logOutStr },
                                    { "response", logInStr },
                                }));
                            LogDebug($"Failed to unmarshal an expected error response from API.");
                            onServerError?.Invoke(code, response);
                        }

                        LogDebug($"Request to {url} failed with code {code}.");
                        LogDebug(response);
                    }
                    catch (Exception e) // Something really unexpected with our response and it's likely not JSON
                    {
                        ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent(
                        "Did not handle error response from API", new Dictionary<string, string>
                        {
                            { "request", logOutStr },
                            { "response", logInStr },
                        }));
                        
                        LogDebug("Could not gracefully handle error response.");
                        LogDebug($"Response from remote {response}");
                        LogDebug(e.ToString());

                        // Try to allow server error callbacks to be processed, but they may assume the body contains
                        // parseable json when it doesn't.
                        try
                        {
                            onServerError?.Invoke(code, $"{e.Message}: {response}");    
                        }
                        catch (JsonReaderException jsonException)
                        {
                            LogDebug($"Could not parse response from remote as JSON. {jsonException.Message}: {response}");
                        }
                    }
                }
            }, Plugin, method, headers, 10.0f);
        }

        public override void ReplyPlayer(object player, string message)
        {
            var playerInstance = player as IPlayer;
            if (playerInstance != null)
            {
                playerInstance.Reply("{0}", "", message);
            }
        }

        public override void ExecuteOfflineCommand(TebexApi.Command command, object player, string commandName, string[] args)
        {
            if (command.Conditions.Delay > 0)
            {
                // Command requires a delay, use built-in plugin timer to wait until callback
                // in order to respect game threads
                Plugin.PluginTimers().Once(command.Conditions.Delay,
                    () =>
                    {
                        ExecuteServerCommand(command, player as IPlayer, commandName, args);
                    });
            }
            else // No delay, execute immediately
            {
                ExecuteServerCommand(command, player as IPlayer, commandName, args);
            }
        }

        private void ExecuteServerCommand(TebexApi.Command command, IPlayer player, string commandName, string[] args)
        {
            // For the say command, don't pass args or they will all get quoted in chat.
            if (commandName.Equals("chat.add") && args.Length >= 2 && player != null && args[0].ToString().Equals(player.Id))
            {
                var message = string.Join(" ", args.Skip(2));
                
                // Remove leading and trailing quotes if present
                if (message.StartsWith('"'))
                {
                    message = message.Substring(1, message.Length - 1);
                }

                if (message.EndsWith('"'))
                {
                    message = message.Substring(0, message.Length - 1);
                }
                
                player.Message(message);
                return;
            }

            var fullCommand = $"{commandName} {string.Join(" ", args)}";
            Plugin.Server().Command(fullCommand);
        }
        
        public override bool IsPlayerOnline(string playerRefId)
        {
            // Get a reference to the in-game player instance
            IPlayer iPlayer = GetPlayerRef(playerRefId) as IPlayer;
            if (iPlayer == null) // Player is not connected
            {
                return false;
            }

            // IsConnected might indicate just a connection to the server, but unknown if it's possible to have an IPlayer
            // reference and for the player to not actually be connected. This would create a case where we can't
            // check inventory slots prior to package delivery.
            return iPlayer.IsConnected;
        }

        public override object GetPlayerRef(string playerId)
        {
            return Plugin.PlayerManager().FindPlayer(playerId);
        }

        public override bool ExecuteOnlineCommand(TebexApi.Command command, object playerObj, string commandName,
            string[] args)
        {
            try
            {
                if (command.Conditions.Slots > 0)
                {
                    #if RUST
                    // Cast down to the base player in order to get inventory slots available.
                    var player = playerObj as Oxide.Game.Rust.Libraries.Covalence.RustPlayer;
                    BasePlayer basePlayer = player.Object as BasePlayer;
                    var slotsAvailable = basePlayer.inventory.containerMain.capacity - basePlayer.inventory.containerMain.TotalItemAmount();
                    
                    LogDebug($"Detected {slotsAvailable} slots in main inventory where command wants {command.Conditions.Slots}");
                    
                    // Some commands have slot requirements, don't execute those if the player can't accept it
                    if (slotsAvailable < command.Conditions.Slots)
                    {
                        LogWarning($"> Player has command {command.CommandToRun} but not enough main inventory slots. Need {command.Conditions.Slots} empty slots.");
                        return false;
                    }
                    #else
                    LogWarning($"> Command has slots condition, but slots are not supported in this game.");
                    #endif
                }
                
                if (command.Conditions.Delay > 0)
                {
                    // Command requires a delay, use built-in plugin timer to wait until callback
                    // in order to respect game threads
                    Plugin.PluginTimers().Once(command.Conditions.Delay,
                        () =>
                        {
                            ExecuteServerCommand(command, playerObj as IPlayer, commandName, args);
                        });
                }
                else
                {
                    ExecuteServerCommand(command, playerObj as IPlayer, commandName, args);    
                }
            }
            catch (Exception e)
            {
                ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent("Caused exception while executing online command", new Dictionary<string, string>()
                {
                    {"command", command.CommandToRun},
                    {"exception", e.Message},
                    {"trace", e.StackTrace},
                }));
                LogError("Failed to run online command due to exception. Command run is aborted.");
                LogError(e.ToString());
                return false;
            }
            
            return true;
        }

        public override string ExpandOfflineVariables(string input, TebexApi.PlayerInfo info)
        {
            string parsed = input;
            parsed = parsed.Replace("{id}", info.Uuid); // In offline commands there is a "UUID" param for the steam ID, and this ID is an internal plugin ID
            parsed = parsed.Replace("{username}", info.Username);
            parsed = parsed.Replace("{name}", info.Username);

            if (parsed.Contains("{") || parsed.Contains("}"))
            {
                LogDebug($"Detected lingering curly braces after expanding offline variables!");
                LogDebug($"Input: {input}");
                LogDebug($"Parsed: {parsed}");
            }

            return parsed;
        }

        public override string ExpandUsernameVariables(string input, object playerObj)
        {
            IPlayer iPlayer = playerObj as IPlayer;
            if (iPlayer == null)
            {
                LogError($"Could not cast player instance when expanding username variables: {playerObj}");
                ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent("Could not cast player instance when expanding username variables", new Dictionary<string, string>
                {
                    {"input", input},
                    {"playerObj", playerObj?.ToString()},
                }));
                return input;
            }

            if (input.Contains("{username}") && string.IsNullOrEmpty(iPlayer.Name))
            {
                LogError($"Player name is null while expanding username?!: {iPlayer}");
                LogError($"Base player object: {playerObj}");
                LogError($"Input command: {input}");
                ReportAutoTriageEvent(TebexTriage.CreateAutoTriageEvent("Player ID is null while expanding username?!: ", new Dictionary<string, string>
                {
                    {"input", input},
                    {"iPlayer.Id", iPlayer.Id},
                    {"iPlayer.Name", iPlayer.Name}
                }));
                return input;
            }

            string parsed = input;
            parsed = parsed.Replace("{id}", iPlayer.Id);
            parsed = parsed.Replace("{username}", iPlayer.Name);
            parsed = parsed.Replace("{name}", iPlayer.Name);

            if (parsed.Contains("{") || parsed.Contains("}"))
            {
                LogDebug($"Detected lingering curly braces after expanding username variables!");
                LogDebug($"Input: {input}");
                LogDebug($"Parsed: {parsed}");
            }

            return parsed;
        }

        public override TebexTriage.AutoTriageEvent FillAutoTriageParameters(TebexTriage.AutoTriageEvent partialEvent)
        {
            partialEvent.GameId = $"{Plugin.GetGame()} {Plugin.Server().Version} | {Plugin.Server().Protocol}";
            partialEvent.FrameworkId = "Oxide";
            partialEvent.PluginVersion = Oxide.Plugins.Tebex.GetPluginVersion();
            partialEvent.ServerIp = Plugin.Server().Address.ToString();
            
            return partialEvent;
        }
    }
    public class TebexTriage
    {
        // For automatically reported errors and issues
        public class AutoTriageEvent
        {
            [JsonProperty("game_id")] public string GameId { get; set; }
            [JsonProperty("framework_id")] public string FrameworkId { get; set; }
            [JsonProperty("plugin_version")] public string PluginVersion { get; set; }
            [JsonProperty("server_ip")] public string ServerIp { get; set; }
            [JsonProperty("error_message")] public string ErrorMessage { get; set; }
            [JsonProperty("trace")] public string Trace { get; set; }
            [JsonProperty("metadata")] public Dictionary<string, string> Metadata { get; set; }
            [JsonProperty("store_name")] public string StoreName { get; set; }
            [JsonProperty("store_url")] public string StoreUrl { get; set; }
        }

        public static AutoTriageEvent CreateAutoTriageEvent(string message, Dictionary<string, string> metadata)
        {
            var triageEvent = new TebexTriage.ReportedTriageEvent();

            triageEvent.ErrorMessage = message;
            triageEvent.Trace = "";
            triageEvent.Metadata = metadata;
            triageEvent.Log = "";

            return triageEvent;
        }
        
        // For issues reported using the /report command
        public class ReportedTriageEvent : AutoTriageEvent
        {
            [JsonProperty("username")] public string Username { get; set; }
            [JsonProperty("user_ip")] public string UserIp { get; set; }
            [JsonProperty("log")] public string Log { get; set; }
        }
    }   
    public class TebexApi
    {
        public static readonly string TebexApiBase = "https://plugin.tebex.io/";
        public static readonly string TebexTriageUrl = "https://plugin-logs.tebex.io/";
        
        public static TebexApi Instance => _apiInstance.Value;
        public static BaseTebexAdapter Adapter { get; private set; }

        // Singleton instance for the API
        private static readonly Lazy<TebexApi> _apiInstance = new Lazy<TebexApi>(() => new TebexApi());

        public TebexApi()
        {
        }

        public void InitAdapter(BaseTebexAdapter adapter)
        {
            Adapter = adapter;
            adapter.Init();
        }
        
        // Used so that we don't depend on Oxide
        public enum HttpVerb
        {
            DELETE,
            GET,
            PATCH,
            POST,
            PUT,
        }

        public delegate void ApiSuccessCallback(int code, string body);

        public delegate void ApiErrorCallback(TebexError error);

        public delegate void ServerErrorCallback(int code, string body);

        public class TebexError
        {
            [JsonProperty("error_code")] public int ErrorCode { get; set; }
            [JsonProperty("error_message")] public string ErrorMessage { get; set; } = "";
        }

        private static void Send(string endpoint, string body, HttpVerb method = HttpVerb.GET,
            ApiSuccessCallback onSuccess = null, ApiErrorCallback onApiError = null,
            ServerErrorCallback onServerError = null)
        {
            Adapter.MakeWebRequest(TebexApiBase + endpoint, body, method, onSuccess, onApiError, onServerError);
        }

        #region Events
        
        public class TebexJoinEventInfo
        {
            [JsonProperty("username_id")] /* steam64ID */
            public string UsernameId { get; private set; }
            [JsonProperty("event_type")]
            public string EventType { get; private set; }
            [JsonProperty("event_date")]
            public DateTime EventDate { get; private set; }
            [JsonProperty("ip")]
            public string IpAddress { get; private set; }

            public TebexJoinEventInfo(string usernameId, string eventType, DateTime eventDate, string ipAddress)
            {
                UsernameId = usernameId;
                EventType = eventType;
                EventDate = eventDate;
                IpAddress = ipAddress;
            }
        }
        
        public void PlayerJoinEvent(List<TebexJoinEventInfo> events, ApiSuccessCallback onSuccess, ApiErrorCallback onApiError = null,
            ServerErrorCallback onServerError = null)
        {
            Send("events", JsonConvert.SerializeObject(events), HttpVerb.POST, onSuccess, onApiError,
                onServerError);
        }
        
        #endregion
        
        #region Information

        public class TebexAccountInfo
        {
            [JsonProperty("id")] public int Id { get; set; }
            [JsonProperty("domain")] public string Domain { get; set; } = "";
            [JsonProperty("name")] public string Name { get; set; } = "";
            [JsonProperty("currency")] public TebexCurrency Currency { get; set; }
            [JsonProperty("online_mode")] public bool OnlineMode { get; set; }
            [JsonProperty("game_type")] public string GameType { get; set; } = "";
            [JsonProperty("log_events")] public bool LogEvents { get; set; }
        }

        public class TebexCurrency
        {
            [JsonProperty("iso_4217")] public string Iso4217 { get; set; } = "";
            [JsonProperty("symbol")] public string Symbol { get; set; } = "";
        }

        public class TebexServerInfo
        {
            [JsonProperty("id")] public int Id { get; set; }
            [JsonProperty("name")] public string Name { get; set; } = "";
        }

        public class TebexStoreInfo
        {
            [JsonProperty("account")] public TebexAccountInfo AccountInfo { get; set; }
            [JsonProperty("server")] public TebexServerInfo ServerInfo { get; set; }
        }

        public class Category
        {
            [JsonProperty("id")] public int Id { get; set; }
            [JsonProperty("order")] public int Order { get; set; }
            [JsonProperty("name")] public string Name { get; set; } = "";
            [JsonProperty("only_subcategories")] public bool OnlySubcategories { get; set; }
            [JsonProperty("subcategories")] public List<Category> Subcategories { get; set; }
            [JsonProperty("packages")] public List<Package> Packages { get; set; }
            [JsonProperty("gui_item")] public object GuiItem { get; set; }
        }

        public class PackageSaleData
        {
            [JsonProperty("active")] public bool Active { get; set; }
            [JsonProperty("discount")] public double Discount { get; set; }
        }

        public class Package
        {
            [JsonProperty("id")] public int Id { get; set; }

            [JsonProperty("name")] public string Name { get; set; } = "";

            [JsonProperty("order")] public string Order { get; set; } = "";

            [JsonProperty("image")] public string Image { get; set; }

            [JsonProperty("price")] public double Price { get; set; }

            [JsonProperty("sale")] public PackageSaleData Sale { get; set; }

            [JsonProperty("expiry_length")] public int ExpiryLength { get; set; }

            [JsonProperty("expiry_period")] public string ExpiryPeriod { get; set; } = "";

            [JsonProperty("type")] public string Type { get; set; } = "";

            [JsonProperty("category")] public Category Category { get; set; }

            [JsonProperty("global_limit")] public int GlobalLimit { get; set; }

            [JsonProperty("global_limit_period")] public string GlobalLimitPeriod { get; set; } = "";

            [JsonProperty("user_limit")] public int UserLimit { get; set; }

            [JsonProperty("user_limit_period")] public string UserLimitPeriod { get; set; } = "";

            [JsonProperty("servers")] public List<TebexServerInfo> Servers { get; set; }

            [JsonProperty("required_packages")] public List<object> RequiredPackages { get; set; } //TODO

            [JsonProperty("require_any")] public bool RequireAny { get; set; }

            [JsonProperty("create_giftcard")] public bool CreateGiftcard { get; set; }

            [JsonProperty("show_until")] public string ShowUntil { get; set; }

            [JsonProperty("gui_item")] public string GuiItem { get; set; } = "";

            [JsonProperty("disabled")] public bool Disabled { get; set; }

            [JsonProperty("disable_quantity")] public bool DisableQuantity { get; set; }

            [JsonProperty("custom_price")] public bool CustomPrice { get; set; }

            [JsonProperty("choose_server")] public bool ChooseServer { get; set; }

            [JsonProperty("limit_expires")] public bool LimitExpires { get; set; }

            [JsonProperty("inherit_commands")] public bool InheritCommands { get; set; }

            [JsonProperty("variable_giftcard")] public bool VariableGiftcard { get; set; }

            // Description is not provided unless verbose=true is passed to the Packages endpoint
            [JsonProperty("description")] public string Description { get; set; } = "";

            public string GetFriendlyPayFrequency()
            {
                switch (Type)
                {
                    case "single": return "One-Time";
                    case "subscription": return $"Each {ExpiryLength} {ExpiryPeriod}";
                    default: return "???";
                }
            }
        }

        // Data returned when sending a package to /checkout
        public class CheckoutUrlPayload
        {
            [JsonProperty("url")] public string Url { get; set; } = "";
            [JsonProperty("expires")] public string Expires { get; set; } = "";
        }

        public delegate void Callback(int code, string body);

        public void Information(ApiSuccessCallback success, ApiErrorCallback error = null)
        {
            Send("information", "", HttpVerb.GET, success, error);
        }

        #endregion

        #region Command Queue

        /**
             * Response received from /queue
             */
        public class CommandQueueResponse
        {
            [JsonProperty("meta")] public CommandQueueMeta Meta { get; set; }
            [JsonProperty("players")] public List<DuePlayer> Players { get; set; }
        }

        /**
             * Metadata received from /queue
             */
        public class CommandQueueMeta
        {
            [JsonProperty("execute_offline")] public bool ExecuteOffline { get; set; }

            [JsonProperty("next_check")] public int NextCheck { get; set; }

            [JsonProperty("more")] public bool More { get; set; }
        }

        /**
             * A due player is one returned by /queue to indicate we have some commands to run.
             */
        public class DuePlayer
        {
            [JsonProperty("id")] public int Id { get; set; }

            [JsonProperty("name")] public string Name { get; set; } = "";

            [JsonProperty("uuid")] public string UUID { get; set; } = "";
        }


        /**
             * The response recieved from /queue/online-commands
             */
        public class OnlineCommandsResponse
        {
            [JsonProperty("player")] public OnlineCommandsPlayer Player { get; set; }
            [JsonProperty("commands")] public List<Command> Commands { get; set; }
        }

        public class OnlineCommandsPlayer
        {
            [JsonProperty("id")] public string Id { get; set; }
            [JsonProperty("username")] public string Username { get; set; }
            [JsonProperty("meta")] public OnlineCommandPlayerMeta Meta { get; set; }
        }

        public class OnlineCommandPlayerMeta
        {
            [JsonProperty("avatar")] public string Avatar { get; set; } = "";
            [JsonProperty("avatarfull")] public string AvatarFull { get; set; } = "";
            [JsonProperty("steamID")] public string SteamId { get; set; } = "";
        }

        public class CommandConditions
        {
            [JsonProperty("delay")] public int Delay { get; set;  }
            [JsonProperty("slots")] public int Slots { get; set; }
        }

        public class OfflineCommandsMeta
        {
            [JsonProperty("limited")] public string Limited { get; set; }
        }
        public class OfflineCommandsResponse
        {
            [JsonProperty("meta")] public OfflineCommandsMeta Meta { get; set;  }
            [JsonProperty("commands")] public List<Command> Commands { get; set;  }
        }
        public class Command
        {
            [JsonProperty("id")] public int Id { get; set; }
            [JsonProperty("command")] public string CommandToRun { get; set; } = "";
            [JsonProperty("payment")] public long Payment { get; set; }
            [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public long PackageRef { get; set; }
            [JsonProperty("conditions")] public CommandConditions Conditions { get; set; } = new CommandConditions();
            [JsonProperty("player")] public PlayerInfo Player { get; set; }
        }

        /**
             * List the players who have commands due to be executed when they next login to the game server.
             * This endpoint also returns any offline commands to be processed and the amount of seconds to wait before performing the queue check again.
             * All clients should strictly follow the response of `next_check`, failure to do so would result in your secret key being revoked or IP address being banned from accessing the API.
             */
        public void GetCommandQueue(ApiSuccessCallback onSuccess = null, ApiErrorCallback onApiError = null,
            ServerErrorCallback onServerError = null)
        {
            Send("queue", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
        }

        /**
             * Gets commands that can be executed on the player even if they are offline.
             */
        public void GetOfflineCommands(ApiSuccessCallback onSuccess = null, ApiErrorCallback onApiError = null,
            ServerErrorCallback onServerError = null)
        {
            Send($"queue/offline-commands", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
        }

        /**
             * Gets commands that can be executed for the given player if they are online.
             */
        public void GetOnlineCommands(int playerId, ApiSuccessCallback onSuccess, ApiErrorCallback onApiError = null,
            ServerErrorCallback onServerError = null)
        {
            Send($"queue/online-commands/{playerId}", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
        }

        private class DeleteCommandsPayload
        {
            /**
                 * An array of one or more command IDs to delete.
                 */
            [JsonProperty("ids")]
            public int[] Ids { get; set; }
        }

        /**
             * Deletes one or more commands that have been executed on the game server.
             * An empty response with the status code of 204 No Content will be returned on completion.
             */
        public void DeleteCommands(int[] ids, ApiSuccessCallback onSuccess, ApiErrorCallback onApiError = null,
            ServerErrorCallback onServerError = null)
        {
            var payload = new DeleteCommandsPayload
            {
                Ids = ids
            };
            Send("queue", JsonConvert.SerializeObject(payload), HttpVerb.DELETE, onSuccess, onApiError,
                onServerError);
        }

        #endregion

        #region Listing

        /**
             * Response from /listing containing the categories and their packages.
             */
        public class ListingsResponse
        {
            [JsonProperty("categories")] public List<Category> categories { get; set; }
        }

        /**
             * Get the categories and packages which should be displayed to players in game. The returned order of this endpoint
             * does not reflect the desired order of the category/packages - please order based on the order object.
             */
        public void GetListing(ApiSuccessCallback onSuccess = null, ApiErrorCallback onError = null,
            ServerErrorCallback onServerError = null)
        {
            Send("listing", "", HttpVerb.GET, onSuccess, onError, onServerError);
        }

        #endregion

        #region Packages

        /**
             * Get a list of all packages on the webstore. Pass verbose=true to include descriptions of the packages.
             * API returns a list of JSON encoded Packages.
             */
        public void GetAllPackages(bool verbose, ApiSuccessCallback onSuccess = null,
            ApiErrorCallback onApiError = null,
            ServerErrorCallback onServerError = null)
        {
            Send(verbose ? "packages?verbose=true" : "packages", "", HttpVerb.GET, onSuccess, onApiError,
                onServerError);
        }

        /**
             * Gets a specific package from the webstore by its ID. Returns JSON-encoded Package object.
             */
        public void GetPackage(string packageId, ApiSuccessCallback onSuccess = null,
            ApiErrorCallback onApiError = null,
            ServerErrorCallback onServerError = null)
        {
            Send($"package/{packageId}", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
        }

        /*
        // Updates a package on the webstore.
        public void UpdatePackage(string packageId, Package package, ApiSuccessCallback onSuccess = null, ApiErrorCallback onApiError = null, ServerErrorCallback onServerError = null)
        {
            //NOOP
        }
        */

        #endregion

        #region Community Goals

        // Retrieves all community goals from the account.
        public void GetCommunityGoals(ApiSuccessCallback onSuccess = null, ApiErrorCallback onApiError = null,
            ServerErrorCallback onServerError = null)
        {
            Send("community_goals", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
        }

        // Retrieves a specific community goal.
        public void GetCommunityGoal(int goalId, ApiSuccessCallback onSuccess = null,
            ApiErrorCallback onApiError = null,
            ServerErrorCallback onServerError = null)
        {
            Send($"community_goals/{goalId}", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
        }

        #endregion

        #region Payments

        /** Payload for /payments to retrieve all payments with quantity limit */
        public class PaymentsPayload
        {
            [JsonProperty("limit")] public int Limit { get; set; } = 100;
        }

        /**
             * Retrieve the latest payments (up to a maximum of 100) made on the webstore.
             */
        public void GetAllPayments(int limit = 100, ApiSuccessCallback onSuccess = null,
            ApiErrorCallback onApiError = null, ServerErrorCallback onServerError = null)
        {
            var payload = new PaymentsPayload
            {
                Limit = limit
            };

            if (limit > 100)
            {
                limit = 100;
            }

            Send($"payments", JsonConvert.SerializeObject(payload), HttpVerb.GET, onSuccess, onApiError,
                onServerError);
        }

        /**
             * Return all payments as a page, at the given page number.
             */
        public void GetAllPaymentsPaginated(int pageNumber, ApiSuccessCallback onSuccess = null,
            ApiErrorCallback onApiError = null, ServerErrorCallback onServerError = null)
        {
            Send($"payments?paged={pageNumber}", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
        }

        /**
             * Retrieve a specific payment by transaction id.
             */
        public void GetPayment(string transactionId, ApiSuccessCallback onSuccess = null,
            ApiErrorCallback onApiError = null, ServerErrorCallback onServerError = null)
        {
            Send($"payments/{transactionId}", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
        }

        /**
            // Returns an array of fields (custom variables, etc) required to be entered for a manual payment to be created for a package.
            public void GetRequiredPaymentFields(Package package)
            {
                //var response = client.SendAsyncRequest($"payments/fields/{package.Id}", HttpMethod.Get);
            }
            
              //Create a manual payment in the same way as is possible from the control panel. One or more packages should be added to the payment,
              //and the package commands will be processed in the same way as would be for a standard manual payment.
                public void CreatePayment()
                {
                    Send($"payments", HttpMethod.Post);
                }

                // Updates a payment
                public void UpdatePayment(string transactionId)
                {
                   Send($"payments/{transactionId}", HttpMethod.Put);
                }

                // Create a note against a payment.
                public void CreatePaymentNote(string transactionId, string note)
                {
                   Send($"payments/{transactionId}/note", HttpMethod.Post);
                }
            */

        #endregion

        #region Checkout

        private class CreateCheckoutPayload
        {
            [JsonProperty("package_id")] public int PackageId { get; set; }

            [JsonProperty("username")] public string Username { get; set; } = "";
        }

        /**
             * Creates a URL which will take the player to a checkout area in order to purchase the given item.
             */
        public void CreateCheckoutUrl(int packageId, string username, ApiSuccessCallback success,
            ApiErrorCallback error = null)
        {
            var payload = new CreateCheckoutPayload
            {
                PackageId = packageId,
                Username = username
            };

            Send("checkout", JsonConvert.SerializeObject(payload), HttpVerb.POST, success, error);
        }

        #endregion

        #region Gift Cards

        public class GiftCard
        {
            //TODO            
        }

        public void GetAllGiftCards(ApiSuccessCallback onSuccess = null, ApiErrorCallback onApiError = null,
            ServerErrorCallback onServerError = null)
        {
            Send("gift-cards", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
        }

        public void GetGiftCard(string giftCardId, ApiSuccessCallback onSuccess = null,
            ApiErrorCallback onApiError = null, ServerErrorCallback onServerError = null)
        {
            Send($"gift-cards/{giftCardId}", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
        }

        public class CreateGiftCardPayload
        {
            [JsonProperty("expires_at")] public string ExpiresAt { get; set; } = "";
            [JsonProperty("note")] public string Note { get; set; } = "";
            [JsonProperty("amount")] public double Amount { get; set; }
        }

        public void CreateGiftCard(string expiresAt, string note, int amount, ApiSuccessCallback onSuccess = null,
            ApiErrorCallback onApiError = null, ServerErrorCallback onServerError = null)
        {
            var payload = new CreateGiftCardPayload
            {
                ExpiresAt = expiresAt,
                Note = note,
                Amount = amount
            };
            Send("gift-cards", JsonConvert.SerializeObject(payload), HttpVerb.POST, onSuccess, onApiError,
                onServerError);
        }

        public void VoidGiftCard(string giftCardId, ApiSuccessCallback onSuccess = null,
            ApiErrorCallback onApiError = null, ServerErrorCallback onServerError = null)
        {
            Send($"gift-cards/{giftCardId}", "", HttpVerb.DELETE, onSuccess, onApiError, onServerError);
        }

        public class TopUpGiftCardPayload
        {
            [JsonProperty("amount")] public string Amount { get; set; } = "";
        }

        public void TopUpGiftCard(string giftCardId, double amount, ApiSuccessCallback onSuccess = null,
            ApiErrorCallback onApiError = null, ServerErrorCallback onServerError = null)
        {
            var payload = new TopUpGiftCardPayload
            {
                Amount = $"{amount}"
            };
            Send($"gift-cards/{giftCardId}", JsonConvert.SerializeObject(payload), HttpVerb.PUT);
        }

        #endregion

        #region Coupons

        public void GetAllCoupons(ApiSuccessCallback onSuccess = null, ApiErrorCallback onApiError = null,
            ServerErrorCallback onServerError = null)
        {
            Send("coupons", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
        }

        public void GetCouponById(string couponId, ApiSuccessCallback onSuccess = null,
            ApiErrorCallback onApiError = null, ServerErrorCallback onServerError = null)
        {
            Send($"coupons/{couponId}", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
        }

        /**
            public void CreateCoupon(ApiSuccessCallback onSuccess = null, ApiErrorCallback onApiError = null, ServerErrorCallback onServerError = null)
            {
                Send("coupons", HttpMethod.Post);
            }

            public void DeleteCoupon(string couponId, ApiSuccessCallback onSuccess = null, ApiErrorCallback onApiError = null, ServerErrorCallback onServerError = null)
            {
               Send($"gift-cards/{couponId}", HttpMethod.Delete);
            }*/

        #endregion

        #region Bans

        public void GetAllBans(ApiSuccessCallback onSuccess = null, ApiErrorCallback onApiError = null,
            ServerErrorCallback onServerError = null)
        {
            //var response = client.SendAsyncRequest("bans", HttpMethod.Get);
            Send("bans", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
        }

        public class CreateBanPayload
        {
            [JsonProperty("reason")] public string Reason { get; set; }
            [JsonProperty("ip")] public string IP { get; set; }

            /** Username or UUID of the player to ban */
            [JsonProperty("user")]
            public string User { get; set; }
        }

        public void CreateBan(string reason, string ip, string userId, ApiSuccessCallback onSuccess = null,
            ApiErrorCallback onApiError = null, ServerErrorCallback onServerError = null)
        {
            var payload = new CreateBanPayload
            {
                Reason = reason,
                IP = ip,
                User = userId
            };
            Send("bans", JsonConvert.SerializeObject(payload), HttpVerb.POST, onSuccess, onApiError,
                onServerError);
        }

        #endregion

        #region Sales

        public void GetAllSales(ApiSuccessCallback onSuccess = null, ApiErrorCallback onApiError = null,
            ServerErrorCallback onServerError = null)
        {
            Send("sales", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
        }

        #endregion

        #region Player Lookup

        /**
             * Root object returned by the /user endpoint, containing PlayerInfo
             */
        public class UserInfoResponse
        {
            [JsonProperty("player")] public PlayerInfo Player { get; set; }

            [JsonProperty("banCount")] public int BanCount { get; set; }

            [JsonProperty("chargebackRate")] public int ChargebackRate { get; set; }

            [JsonProperty("payments")] public List<PaymentInfo> Payments { get; set; }

            [JsonProperty("purchaseTotals")] public object[] PurchaseTotals { get; set; }
        }

        public class PaymentInfo
        {
            [JsonProperty("txn_id")] public string TransactionId { get; set; }

            [JsonProperty("time")] public long Time { get; set; }

            [JsonProperty("price")] public double Price { get; set; }

            [JsonProperty("currency")] public string Currency { get; set; }

            [JsonProperty("status")] public int Status { get; set; }
        }

        /**
             * A player's information returned by the /user endpoint
             */
        public class PlayerInfo
        {
            [JsonProperty("id")] public string Id { get; set; }

            //FIXME sometimes referred to as `name` or `username` alternatively?
            [JsonProperty("name")] public string Username { get; set; }

            [JsonProperty("meta")] public OnlineCommandPlayerMeta Meta { get; set; }

            /** Only populated by offline commands */
            [JsonProperty("uuid")]
            public string Uuid { get; set; } = "";
            
            [JsonProperty("plugin_username_id")] public int PluginUsernameId { get; set; }
        }

        public void GetUser(string targetUserId, ApiSuccessCallback onSuccess = null,
            ApiErrorCallback onApiError = null,
            ServerErrorCallback onServerError = null)
        {
            Send($"user/{targetUserId}", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
        }

        #endregion

        #region Customer Purchases

        public class PackagePurchaseInfo
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }
        }

        public class CustomerPackagePurchaseRecord
        {
            [JsonProperty("txn_id")]
            public string TransactionId { get; set; }

            [JsonProperty("date")]
            public DateTime Date { get; set; }

            [JsonProperty("quantity")]
            public int Quantity { get; set; }

            [JsonProperty("package")]
            public PackagePurchaseInfo Package { get; set; }
        }
        
        // Return a list of all active (non-expired) packages that a customer has purchased.
        // If packageId is provided, filter down to a single package ID, if you want to check if a specific package has been purchased. 
        public void GetActivePackagesForCustomer(string userId, int? packageId = null,
            ApiSuccessCallback onSuccess = null,
            ApiErrorCallback onApiError = null, ServerErrorCallback onServerError = null)
        {
            if (packageId == null)
            {
                Send($"player/{userId}/packages", "", HttpVerb.GET, onSuccess, onApiError, onServerError);
            }
            else
            {
                Send($"player/{userId}/packages?package={packageId}", "", HttpVerb.GET, onSuccess, onApiError,
                    onServerError);
            }
        }

        #endregion
    }
	}
