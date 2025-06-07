using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Text;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using XboxAuthNet;
using File = System.IO.File;

namespace GameStore
{
    public class GameStore : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();


        // Unique ID (unchanged)
        public override Guid Id { get; } = Guid.Parse("55eeaffc-4d50-4d08-85fb-d8e49800d058");
        public override string Name => "Game Store";
        public override LibraryClient Client { get; } = new GameStoreClient();
        public object HttpUtility { get; private set; }

        // URLS TO USE:

        // PC Games
        private static readonly string steamripBaseUrl = "https://steamrip.com/games-list-page/";
        private static readonly string ankerBaseUrl = "https://ankergames.net/games-list";
        private static readonly string magipackBaseUrl = "https://www.magipack.games/games-list/";
        private static readonly string ElamigosBaseUrl = "https://elamigos.site/";
        private static readonly string fitgirlBaseUrl = "https://fitgirl-repacks.site/all-my-repacks-a-z/?lcp_page0=";
        private static readonly Regex fallbackRegex = new Regex(@"https://fitgirl-repacks\.site/([^/]+)/?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AbandonRegex = new Regex(@"Name: ""(.+?)""[, ]+url: ""(.+?)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);


        // Sony 
        private static readonly string Sony_PS2_Games_BaseUrl = "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation%202/";
        private static readonly string Sony_PS1_Games_BaseUrl = "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation/";

        // Nintendo 
        private static readonly string Nintendo_WII_Games_BaseUrl = "https://myrient.erista.me/files/Redump/Nintendo%20-%20Wii%20-%20NKit%20RVZ%20[zstd-19-128k]/";
        private static readonly string Nintendo_WiiU_Games_BaseUrl = "https://myrient.erista.me/files/Internet%20Archive/teamgt19/nintendo-wii-u-usa-full-set-wua-format-embedded-dlc-updates/";
        private static readonly string Nintendo_GameCube_Games_BaseUrl = "https://myrient.erista.me/files/Redump/Nintendo%20-%20GameCube%20-%20NKit%20RVZ%20[zstd-19-128k]/";
        private static readonly string Nintendo64_Games_BaseUrl = "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%2064%20(BigEndian)/";
        private static readonly string Nintendo_GameBoy_Games_BaseUrl = "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy/";
        private static readonly string Nintendo_GameBoyAdvance_Games_BaseUrl = "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy%20Advance/";
        private static readonly string Nintendo_GameBoyColor_Games_BaseUrl = "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy%20Color/";
        private static readonly string Nintendo_3DS_Games_BaseUrl = "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%203DS%20(Decrypted)/";
        private static readonly string Nintendo_Switch_Games_BaseUrl = "https://nswdl.com/switch-posts/";





        // Microsoft
        private const string Xbox360_Games_BaseUrl = "https://myrient.erista.me/files/Redump/Microsoft%20-%20Xbox%20360/";
        private const string Xbox360Digital_Games_BaseUrl = "https://myrient.erista.me/files/No-Intro/Microsoft%20-%20Xbox%20360%20(Digital)/";
        private const string Microsoft_Xbox_Games_BaseUrl = "https://myrient.erista.me/files/Redump/Microsoft%20-%20Xbox/";

        // Saga 
        private const string Sega_Saturn_Games_BaseUrl = "https://myrient.erista.me/files/Redump/Sega%20-%20Saturn/";
        private const string Sega_Dreamcast_Games_BaseUrl = "https://myrient.erista.me/files/Redump/Sega%20-%20Dreamcast/";


        


       
        public GameStore(IPlayniteAPI api) : base(api)
        {


        }

        private static readonly HashSet<Guid> launchingGames = new HashSet<Guid>();
        private static HashSet<Guid> OnGameStopped_promptedGames = null;

        // Install py ect
        public override async void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            string dataFolder = GetPluginUserDataPath();
            string firstRunPath = System.IO.Path.Combine(dataFolder, "First.Run.0.txt");
            string quickFixBat = System.IO.Path.Combine(dataFolder, "_QUICK FIX.bat");

            // --- First Run/Disclosure Check ---
            if (!System.IO.File.Exists(firstRunPath))
            {
                // Show Disclosure messages in order (Playnite Dialogs is safe here, no need to check MainWindow)
                PlayniteApi.Dialogs.ShowMessage(
                    "\"Game Store\" is an all-in-one Game Store for all your games, both new and old. Despite not \"Buying\" Games, it will in future use APIs to know what games you own from different sources (Steam, Epic, etc.).",
                    "Disclosure");

                PlayniteApi.Dialogs.ShowMessage(
                    "\"Game Store\" offers a bunch of features that other launchers lack, such as game merging, playtime and achievement support. \"Games,\" \"ROMs,\" and \"Emulator\" setups will be included for easy experiences. With around 9k PC games, you’ll never miss a new game. In the future, we’ll let you know when certain games go on sale and where, and offer an advanced local installing system.",
                    "Disclosure 2");

                PlayniteApi.Dialogs.ShowMessage(
                    "\"Game Store\" DOES NOT & WILL NOT PROVIDE USERS WITH GAME FILES! We offer a unique storefront and management system for all your games. This ensures no duplicates and reduces the need for official launchers.",
                    "Disclosure 3");

                // --- Run _QUICK FIX.bat only on first run ---
                if (System.IO.File.Exists(quickFixBat))
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = quickFixBat,
                            UseShellExecute = true,
                            WorkingDirectory = dataFolder
                        };
                        var proc = System.Diagnostics.Process.Start(psi);
                        proc.WaitForExit();
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Failed to run _QUICK FIX.bat: {ex.Message}");
                        // You can choose to return here or continue
                        return;
                    }
                }
                else
                {
                    logger.Warn("_QUICK FIX.bat not found in plugin data folder. Skipping setup batch.");
                }

                // Mark first run completed so this never runs again
                try
                {
                    System.IO.File.WriteAllText(firstRunPath, "This file marks that the Game Store plugin has shown first run disclosures and executed first-time setup.");
                }
                catch (Exception ex)
                {
                    logger.Error($"Failed to write first run marker: {ex.Message}");
                }
            }

            // ...rest of your startup logic...
        }

        // Check Updates on Launch!!

        // Check Updates on Launch!!
        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            // Prevent recursive OnGameStarting when launching the game manually
            if (launchingGames.Contains(args.Game.Id))
            {
                launchingGames.Remove(args.Game.Id);
                return;
            }

            args.CancelStartup = true;

            Task.Run(() =>
            {
                string pluginDataPath = GetPluginUserDataPath();
                string gamesTxtPath = Path.Combine(pluginDataPath, "My Games.txt");
                string urlsFilePath = Path.Combine(pluginDataPath, "_Add_Urls_here.txt");
                string pythonScriptPath = Path.Combine(pluginDataPath, "fucking fast.py");

                void RunOnUi(Action uiAction)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(uiAction);
                }

                // 1. Check for My Games.txt
                if (!File.Exists(gamesTxtPath))
                {
                    RunOnUi(() => PlayniteApi.Dialogs.ShowErrorMessage(
                        $"My Games.txt was NOT found at:\n{gamesTxtPath}",
                        "My Games.txt Not Found"
                    ));
                    return;
                }

                // 2. Find game entry
                string gameName = args.Game.Name;
                string[] lines = File.ReadAllLines(gamesTxtPath);
                int foundIndex = Array.FindIndex(lines, l => l.StartsWith($"Name: {gameName},", StringComparison.OrdinalIgnoreCase));
                if (foundIndex == -1)
                {
                    // If not found in My Games.txt, just launch the game as normal
                    RunOnUi(() =>
                    {
                        launchingGames.Add(args.Game.Id);
                        PlayniteApi.StartGame(args.Game.Id);
                    });
                    return;
                }
                string foundLine = lines[foundIndex];


                // 3. Extract info
                var versionMatch = Regex.Match(foundLine, @"Version:\s*([^,\n]+)", RegexOptions.IgnoreCase);
                string localVersion = versionMatch.Success ? versionMatch.Groups[1].Value.Trim() : "Unknown";
                var sourceMatch = Regex.Match(foundLine, @"Source:\s*([^,\n]+)", RegexOptions.IgnoreCase);
                string source = sourceMatch.Success ? sourceMatch.Groups[1].Value.Trim() : "Unknown";
                string actionName = $"Download: {source}";
                var foundAction = args.Game.GameActions?
                    .FirstOrDefault(ga =>
                        !string.IsNullOrEmpty(ga.Name) &&
                        ga.Name.Equals(actionName, StringComparison.OrdinalIgnoreCase) &&
                        ga.Type == GameActionType.URL);
                string url = foundAction?.Path;

                // 4. Get site version (AnkerGames example)
                string siteVersion = localVersion;
                if (!string.IsNullOrEmpty(url) && source.Equals("AnkerGames", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using (var client = new HttpClient())
                        {
                            string html = client.GetStringAsync(url).GetAwaiter().GetResult();
                            var regex = new Regex(@"<span[^>]*bg-green-500[^>]*>\s*(?<ver>[^<]+?)\s*<\/span>", RegexOptions.IgnoreCase);
                            var match = regex.Match(html);
                            if (match.Success)
                                siteVersion = match.Groups["ver"].Value.Trim();
                        }
                    }
                    catch
                    {
                        // Ignore and continue with local version
                    }
                }

                // 5. Compare versions
                bool siteIsNewer =
                    !string.Equals(localVersion, "Unknown", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(siteVersion, "Unknown", StringComparison.OrdinalIgnoreCase) &&
                    !localVersion.Equals(siteVersion, StringComparison.OrdinalIgnoreCase);

                // 6. Update flow
                if (siteIsNewer)
                {
                    var updateResult = System.Windows.MessageBoxResult.None;
                    RunOnUi(() => updateResult = PlayniteApi.Dialogs.ShowMessage(
                        $"Update available on {source}!\nLocal version: {localVersion}\nOnline version: {siteVersion}\n\nDownload update?",
                        "Update Available",
                        System.Windows.MessageBoxButton.YesNo
                    ));

                    if (updateResult == System.Windows.MessageBoxResult.No)
                    {
                        RunOnUi(() =>
                        {
                            launchingGames.Add(args.Game.Id);
                            PlayniteApi.StartGame(args.Game.Id);
                        });
                        return;
                    }

                    var playWhileDownload = System.Windows.MessageBoxResult.None;
                    RunOnUi(() => playWhileDownload = PlayniteApi.Dialogs.ShowMessage(
                        "Continue playing while download?",
                        "Download Update",
                        System.Windows.MessageBoxButton.YesNo
                    ));

                    // (Download logic)
                    bool downloadSuccess = false;
                    try
                    {
                        File.AppendAllText(urlsFilePath, url + Environment.NewLine);

                        if (File.Exists(pythonScriptPath))
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "python",
                                Arguments = $"\"{pythonScriptPath}\"",
                                WorkingDirectory = pluginDataPath,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            };
                            using (var process = System.Diagnostics.Process.Start(psi))
                            {
                                process.WaitForExit();
                                downloadSuccess = process.ExitCode == 0;
                            }
                        }
                        else
                        {
                            RunOnUi(() => PlayniteApi.Dialogs.ShowErrorMessage(
                                $"Python script not found: {pythonScriptPath}",
                                "Script Error"
                            ));
                        }
                    }
                    catch (Exception ex)
                    {
                        RunOnUi(() => PlayniteApi.Dialogs.ShowErrorMessage(
                            $"Failed to start update download: {ex.Message}",
                            "Update Error"
                        ));
                    }

                    // Update version in txt if download succeeded
                    if (downloadSuccess)
                    {
                        string updatedLine = Regex.Replace(
                            foundLine,
                            @"Version:\s*([^,\n]+)",
                            $"Version: {siteVersion}",
                            RegexOptions.IgnoreCase
                        );
                        lines[foundIndex] = updatedLine;
                        File.WriteAllLines(gamesTxtPath, lines);
                    }

                    if (playWhileDownload == System.Windows.MessageBoxResult.Yes)
                    {
                        RunOnUi(() =>
                        {
                            launchingGames.Add(args.Game.Id);
                            PlayniteApi.StartGame(args.Game.Id);
                        });
                        return;
                    }
                    // If "No" to play while download, do NOT launch game, just exit
                    return;
                }

                // If no update, just start game (single entry point!)
                RunOnUi(() =>
                {
                    launchingGames.Add(args.Game.Id);
                    PlayniteApi.StartGame(args.Game.Id);
                });
            });


        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            // ------------------- LOCAL UPDATE SECTION -------------------
            var localGames = new ConcurrentBag<GameMetadata>();
            var exclusionsLocal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string localExclusionsPath = Path.Combine(GetPluginUserDataPathLocal(), "Exclusions.txt");

            // Efficiently read exclusions using streaming
            if (System.IO.File.Exists(localExclusionsPath))
            {
                int excludedCount = 0;
                foreach (var line in System.IO.File.ReadLines(localExclusionsPath)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x)))
                {
                    exclusionsLocal.Add(line.ToLower());
                    excludedCount++;
                }
                logger.Info("Total exclusions loaded: " + excludedCount);
            }

            void ImportAllOwnedSteamGames()
            {
                // Determine SteamLibrary config path (portable or installed Playnite)
                string configPath = GetSteamConfigPathUniversal();
                string steamApiKey = null;
                string steamUserId = null;

                if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
                {
                    var config = JObject.Parse(File.ReadAllText(configPath));
                    steamApiKey = config["ApiKey"]?.ToString();
                    // Prefer AccountId, fallback to UserId
                    steamUserId = config["AccountId"]?.ToString() ?? config["UserId"]?.ToString();
                    logger.Info($"Steam config found at: {configPath}");
                }
                else
                {
                    logger.Warn("Could not find Playnite SteamLibrary config.json for API key and UserID!");
                    return;
                }

                if (string.IsNullOrEmpty(steamApiKey) || string.IsNullOrEmpty(steamUserId))
                {
                    logger.Warn("Steam API key or UserID missing, skipping Steam API import.");
                    return;
                }

                // Retrieve list of owned Steam games (normalized and original names)
                var ownedGames = ImportOwnedSteamGamesWithOriginalsAsync(steamApiKey, steamUserId).GetAwaiter().GetResult();

                if (ownedGames == null || ownedGames.Count == 0)
                {
                    logger.Warn("No Steam owned games retrieved from the API.");
                    return;
                }

                // Build set of normalized names of games already in Playnite
                var playniteNames = new HashSet<string>(
                    PlayniteApi.Database.Games
                        .Where(g => g.Platforms != null && g.Platforms.Any(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrEmpty(g.Name))
                        .Select(g => NormalizeGameName(CleanGameName(SanitizePath(g.Name)))),
                    StringComparer.OrdinalIgnoreCase
                );

                // Count how many owned Steam games are already present
                int alreadyInPlaynite = ownedGames.Count(g => playniteNames.Contains(g.NormalizedName));

                logger.Info($"Steam API: Total owned games: {ownedGames.Count}, already in Playnite: {alreadyInPlaynite}");

                // Add [Own: Steam] feature to all existing plugin games that match Steam ownership
                AddOwnSteamFeatureToPluginGamesByName(new HashSet<string>(ownedGames.Select(g => g.NormalizedName)));
            }

            async Task<List<(string NormalizedName, string OriginalName)>> ImportOwnedSteamGamesWithOriginalsAsync(string steamApiKey, string steamUserId)
            {
                string apiUrl = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={steamApiKey}&steamid={steamUserId}&include_appinfo=1&include_played_free_games=0";
                var ownedGames = new List<(string NormalizedName, string OriginalName)>();

                using (var http = new HttpClient())
                {
                    try
                    {
                        var response = await http.GetAsync(apiUrl);

                        if ((int)response.StatusCode == 429)
                        {
                            logger.Warn("Steam API rate limited (429). Waiting 60 seconds before retry...");
                            await Task.Delay(60 * 1000);
                            response = await http.GetAsync(apiUrl);
                        }

                        response.EnsureSuccessStatusCode();

                        var responseBody = await response.Content.ReadAsStringAsync();
                        var data = JObject.Parse(responseBody);

                        var games = data["response"]?["games"];
                        int totalOwned = games?.Count() ?? 0;
                        logger.Info($"Steam API: Total owned games: {totalOwned}");

                        if (totalOwned == 0)
                        {
                            logger.Info("Steam API: No games found for the user.");
                            return ownedGames;
                        }

                        foreach (var game in games)
                        {
                            string originalName = game["name"]?.ToString();
                            if (string.IsNullOrEmpty(originalName))
                                continue;

                            string normalizedName = NormalizeGameName(
                                CleanGameName(
                                    SanitizePath(originalName)
                                )
                            );

                            ownedGames.Add((normalizedName, originalName));
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Steam API import failed: {ex.Message}");
                    }
                }
                return ownedGames;
            }

            // Add [Own: Steam] feature to all existing plugin library games that match owned normalized names
            void AddOwnSteamFeatureToPluginGamesByName(HashSet<string> ownedNames)
            {
                var ownSteamFeature = EnsureFeatureExists("[Own: Steam]");
                if (ownSteamFeature != null && ownSteamFeature.Id != Guid.Empty)
                {
                    foreach (var g in PlayniteApi.Database.Games)
                    {
                        if (g.PluginId == Id &&
                            g.Platforms != null &&
                            g.Platforms.Any(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)) &&
                            !string.IsNullOrEmpty(g.Name))
                        {
                            string gCleanName = NormalizeGameName(
                                ConvertHyphenToColon(
                                    CleanGameName(
                                        SanitizePath(g.Name)
                                    )
                                )
                            );
                            if (ownedNames.Contains(gCleanName))
                            {
                                if (g.FeatureIds == null)
                                    g.FeatureIds = new List<Guid>();

                                if (!g.FeatureIds.Contains(ownSteamFeature.Id))
                                {
                                    g.FeatureIds.Add(ownSteamFeature.Id);
                                    PlayniteApi.Database.Games.Update(g);
                                }
                            }
                        }
                    }
                }
            }

            string GetSteamConfigPathUniversal()
            {
                // Check for Playnite Portable mode first
                string portablePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtensionsData", "cb91dfc9-b977-43bf-8e70-55f46e410fab", "config.json");
                if (File.Exists(portablePath))
                {
                    logger.Info("Detected Playnite Portable mode.");
                    return portablePath;
                }

                // Fallback to Playnite Installed mode
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string installedPath = Path.Combine(appData, "Playnite", "ExtensionsData", "cb91dfc9-b977-43bf-8e70-55f46e410fab", "config.json");
                if (File.Exists(installedPath))
                {
                    logger.Info("Detected Playnite Installed mode.");
                    return installedPath;
                }

                // Config not found
                return null;
            }

            void ExportSteamOwnedGamesToFile()
            {
                // Use your plugin's GUID for the config path
                string pluginId = "55eeaffc-4d50-4d08-85fb-d8e49800d058";
                string configPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "ExtensionsData",
                    pluginId,
                    "config.json"
                );
                string steamApiKey = null;
                string steamUserId = null;

                if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
                {
                    var config = JObject.Parse(File.ReadAllText(configPath));
                    steamApiKey = config["ApiKey"]?.ToString();
                    steamUserId = config["AccountId"]?.ToString() ?? config["UserId"]?.ToString();
                    logger.Info($"Steam config found at: {configPath}");
                }
                else
                {
                    logger.Warn("Could not find Playnite SteamLibrary config.json for API key and UserID!");
                    return;
                }

                if (string.IsNullOrEmpty(steamApiKey) || string.IsNullOrEmpty(steamUserId))
                {
                    logger.Warn("Steam API key or UserID missing, skipping Steam API import.");
                    return;
                }

                var ownedGames = ImportOwnedSteamGamesWithOriginalsAsync(steamApiKey, steamUserId).GetAwaiter().GetResult();

                if (ownedGames == null)
                {
                    logger.Warn("No Steam owned games retrieved from the API.");
                    return;
                }

                var sortedNames = ownedGames.Select(g => g.NormalizedName).ToList();
                sortedNames.Sort(StringComparer.OrdinalIgnoreCase);

                string outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Own.Steam.txt");
                File.WriteAllLines(outputPath, sortedNames);

                logger.Info($"Exported {sortedNames.Count} Steam owned game names to {outputPath}");
            }

            // Usage: After repack/locals scan, before scraping other platforms.
            Task.Run(() => ImportAllOwnedSteamGames());

            var localGamesList = localGames.ToList();

            // ------------------- ONLINE SCRAPE SECTION -------------------
            var allGames = new List<GameMetadata>();
            allGames.AddRange(localGamesList);

            // Helper: for deduplication by normalized name
            var existingNormalizedFromDB = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id)
                .Select(g => NormalizeGameName(g.Name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Helper for adding online games (keeps original signature)
            void AddScraped(IEnumerable<GameMetadata> entries, string sourceName, Func<string, bool> duplicateCheck = null, Action<GameMetadata, string, string> additionalMerge = null)
            {
                foreach (var game in entries)
                {
                    string gameName = game.Name;
                    string normalizedKey = NormalizeGameName(gameName);

                    // Only deduplicate if the game is a PC game
                    bool isPC = game.Platforms != null && game.Platforms.Any(p => p.ToString().IndexOf("PC", StringComparison.OrdinalIgnoreCase) >= 0);

                    if ((isPC && existingNormalizedFromDB.Contains(normalizedKey)) || (duplicateCheck != null && duplicateCheck(gameName)))
                        continue;

                    additionalMerge?.Invoke(game, gameName, normalizedKey);

                    if (game.GameActions == null)
                        game.GameActions = new List<GameAction>();

                    if (!game.GameActions.Any(a => a.Name.StartsWith("Download:", StringComparison.OrdinalIgnoreCase)))
                    {
                        game.GameActions.Insert(0, new GameAction
                        {
                            Name = $"Download: {sourceName}",
                            Type = GameActionType.URL,
                            Path = game.GameActions.FirstOrDefault()?.Path ?? "",
                            IsPlayAction = false
                        });
                    }

                    allGames.Add(game);

                    if (isPC)
                        existingNormalizedFromDB.Add(normalizedKey);
                }
            }

            // ---- PC: Local Games/Repacks/Steam Installs ----

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                // "Games" folder
                string gamesFolderPath = Path.Combine(drive.RootDirectory.FullName, "Games");
                if (Directory.Exists(gamesFolderPath))
                {
                    foreach (var folder in Directory.GetDirectories(gamesFolderPath))
                    {
                        string folderName = Path.GetFileName(folder);
                        string cleanedName = ConvertHyphenToColon(CleanGameName(SanitizePath(folderName)));
                        string norm = NormalizeGameName(cleanedName);

                        string[] versionFiles = Directory.Exists(folder)
                            ? Directory.GetFiles(folder, "*.txt").Where(file => Regex.IsMatch(Path.GetFileNameWithoutExtension(file), @"^v\d+(\.\d+)*$")).ToArray()
                            : Array.Empty<string>();

                        var excludedFiles = new List<string>();
                        string[] exeFiles = Directory.Exists(folder)
                            ? Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories)
                                .Where(exe =>
                                {
                                    string fileName = Path.GetFileName(exe).ToLower();
                                    if (exclusionsLocal.Contains(fileName) ||
                                        fileName.Contains("setup") ||
                                        fileName.Contains("unins") ||
                                        fileName.Contains("uninstall"))
                                    {
                                        excludedFiles.Add(fileName);
                                        return false;
                                    }
                                    return true;
                                }).ToArray()
                            : Array.Empty<string>();

                        if (!exeFiles.Any())
                            continue;

                        var match = allGames.FirstOrDefault(g =>
                            NormalizeGameName(g.Name) == norm &&
                            g.Platforms != null &&
                            g.Platforms.Any(p => p.ToString().IndexOf("PC", StringComparison.OrdinalIgnoreCase) >= 0));

                        if (match != null)
                        {
                            match.IsInstalled = true;
                            match.InstallDirectory = folder;
                            match.Version = versionFiles.Any() ? Path.GetFileNameWithoutExtension(versionFiles.First()) : match.Version;
                            foreach (var exe in exeFiles)
                            {
                                if ((match.GameActions ?? Enumerable.Empty<GameAction>()).All(a => !string.Equals(a.Path, exe, StringComparison.OrdinalIgnoreCase)))
                                {
                                    if (match.GameActions == null)
                                        match.GameActions = new List<GameAction>();
                                    match.GameActions.Add(new GameAction
                                    {
                                        Type = GameActionType.File,
                                        Path = exe,
                                        Name = Path.GetFileNameWithoutExtension(exe),
                                        IsPlayAction = true,
                                        WorkingDir = folder
                                    });
                                }
                            }
                            match.Name = cleanedName;
                        }
                        else
                        {
                            var localGame = new GameMetadata
                            {
                                Name = cleanedName,
                                GameId = norm.ToLower(),
                                Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                                GameActions = exeFiles.Select(exe => new GameAction
                                {
                                    Type = GameActionType.File,
                                    Path = exe,
                                    Name = Path.GetFileNameWithoutExtension(exe),
                                    IsPlayAction = true,
                                    WorkingDir = folder
                                }).ToList(),
                                IsInstalled = true,
                                InstallDirectory = folder,
                                Icon = new MetadataFile(Path.Combine(folder, "icon.png")),
                                BackgroundImage = new MetadataFile(Path.Combine(folder, "background.png")),
                                Version = versionFiles.Any() ? Path.GetFileNameWithoutExtension(versionFiles.First()) : null
                            };
                            allGames.Add(localGame);
                        }
                    }
                }

                // SteamLibrary (Installed games)
                string steamAppsPath = Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps");
                string steamCommonPath = Path.Combine(steamAppsPath, "common");
                if (Directory.Exists(steamCommonPath) && Directory.Exists(steamAppsPath))
                {
                    var installDirToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var installDirToAppId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var manifest in Directory.EnumerateFiles(steamAppsPath, "appmanifest_*.acf"))
                    {
                        string manifestName = null, installdir = null, appid = null;
                        foreach (var line in File.ReadLines(manifest))
                        {
                            var matchAppId = Regex.Match(line, "\"appid\"\\s+\"([^\"]+)\"");
                            if (matchAppId.Success) { appid = matchAppId.Groups[1].Value.Trim(); continue; }
                            var matchName = Regex.Match(line, "\"name\"\\s+\"([^\"]+)\"");
                            if (matchName.Success) { manifestName = matchName.Groups[1].Value.Trim(); continue; }
                            var matchInstalldir = Regex.Match(line, "\"installdir\"\\s+\"([^\"]+)\"");
                            if (matchInstalldir.Success) { installdir = matchInstalldir.Groups[1].Value.Trim(); continue; }
                        }
                        if (!string.IsNullOrEmpty(installdir) && !string.IsNullOrEmpty(manifestName) && !string.IsNullOrEmpty(appid))
                        {
                            installDirToName[installdir] = manifestName;
                            installDirToAppId[installdir] = appid;
                        }
                    }

                    foreach (var folder in Directory.GetDirectories(steamCommonPath))
                    {
                        string folderName = Path.GetFileName(folder);
                        if (!installDirToName.TryGetValue(folderName, out var playniteName) || string.IsNullOrWhiteSpace(playniteName))
                            continue;

                        string appid = installDirToAppId.TryGetValue(folderName, out var id) ? id : null;
                        string cleanedName = ConvertHyphenToColon(CleanGameName(playniteName));
                        string norm = NormalizeGameName(cleanedName);

                        string[] exeFiles = Directory.Exists(folder)
                            ? Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories)
                                .Where(exe =>
                                {
                                    string fileName = Path.GetFileName(exe).ToLower();
                                    return !exclusionsLocal.Contains(fileName) &&
                                           !fileName.Contains("setup") &&
                                           !fileName.Contains("unins") &&
                                           !fileName.Contains("uninstall");
                                }).ToArray()
                            : Array.Empty<string>();

                        if (!exeFiles.Any())
                            continue;

                        var match = allGames.FirstOrDefault(g =>
                            NormalizeGameName(g.Name) == norm &&
                            g.Platforms != null &&
                            g.Platforms.Any(p => p.ToString().IndexOf("PC", StringComparison.OrdinalIgnoreCase) >= 0));
                        string[] versionFiles = Directory.Exists(folder)
                            ? Directory.GetFiles(folder, "*.txt").Where(file => Regex.IsMatch(Path.GetFileNameWithoutExtension(file), @"^v\d+(\.\d+)*$")).ToArray()
                            : Array.Empty<string>();

                        if (match != null)
                        {
                            match.IsInstalled = true;
                            match.InstallDirectory = folder;
                            match.Version = versionFiles.Any() ? Path.GetFileNameWithoutExtension(versionFiles.First()) : match.Version;
                            if (!string.IsNullOrEmpty(appid) && (match.GameActions == null || !match.GameActions.Any(a => a.Name == "{Steam}")))
                            {
                                if (match.GameActions == null)
                                    match.GameActions = new List<GameAction>();
                                match.GameActions.Add(new GameAction
                                {
                                    Type = GameActionType.URL,
                                    Path = "steam://run/" + appid,
                                    Name = "{Steam}",
                                    IsPlayAction = true
                                });
                            }
                            match.Name = cleanedName;
                        }
                        else
                        {
                            var gameMetadata = new GameMetadata
                            {
                                Name = cleanedName,
                                GameId = norm.ToLower(),
                                Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                                GameActions = !string.IsNullOrEmpty(appid)
                                    ? new List<GameAction>
                                    {
                new GameAction
                {
                    Type = GameActionType.URL,
                    Path = "steam://run/" + appid,
                    Name = "{Steam}",
                    IsPlayAction = true
                }
                                    }
                                    : null,
                                IsInstalled = true,
                                InstallDirectory = folder,
                                Icon = new MetadataFile(Path.Combine(folder, "icon.png")),
                                BackgroundImage = new MetadataFile(Path.Combine(folder, "background.png")),
                                Version = versionFiles.Any() ? Path.GetFileNameWithoutExtension(versionFiles.First()) : null
                            };
                            allGames.Add(gameMetadata);
                        }
                    }
                }

                // "Repacks" folder
                string repacksFolderPath = Path.Combine(drive.RootDirectory.FullName, "Repacks");
                if (Directory.Exists(repacksFolderPath))
                {
                    foreach (var folder in Directory.GetDirectories(repacksFolderPath))
                    {
                        string folderName = Path.GetFileName(folder);

                        string repackCleaned = Regex.Replace(folderName, @"[\[\(].*?[\]\)]", "");
                        repackCleaned = Regex.Replace(repackCleaned, @"\s{2,}", " ").Trim();
                        string displayName = ConvertHyphenToColon(CleanGameName(SanitizePath(repackCleaned)));
                        string norm = NormalizeGameName(displayName);

                        var match = allGames.FirstOrDefault(g =>
                            NormalizeGameName(g.Name) == norm &&
                            g.Platforms != null &&
                            g.Platforms.Any(p => p.ToString().IndexOf("PC", StringComparison.OrdinalIgnoreCase) >= 0)
                        );

                        if (match != null)
                        {
                            match.Name = displayName;
                            if (match is GameMetadata newGame)
                            {
                                AddInstallReadyFeature(newGame);
                            }
                        }
                        else
                        {
                            var gameMetadata = new GameMetadata
                            {
                                Name = displayName,
                                GameId = norm.ToLowerInvariant(),
                                Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                                GameActions = new List<GameAction>(),
                                IsInstalled = false,
                                InstallDirectory = null,
                                Icon = new MetadataFile(Path.Combine(folder, "icon.png")),
                                BackgroundImage = new MetadataFile(Path.Combine(folder, "background.png"))
                            };
                            AddInstallReadyFeature(gameMetadata);
                            allGames.Add(gameMetadata);
                        }
                    }
                }

                // "Missing Steam games" block: add owned Steam games not already present
                var configPath = GetSteamConfigPathUniversal();
                string steamApiKey = null;
                string steamUserId = null;
                if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
                {
                    var config = JObject.Parse(File.ReadAllText(configPath));
                    steamApiKey = config["ApiKey"]?.ToString();
                    steamUserId = config["AccountId"]?.ToString() ?? config["UserId"]?.ToString();
                    logger.Info($"Steam config found at: {configPath}");
                }
                else
                {
                    logger.Warn("Could not find Playnite SteamLibrary config.json for API key and UserID!");
                }

                if (!string.IsNullOrEmpty(steamApiKey) && !string.IsNullOrEmpty(steamUserId))
                {
                    var ownedGames = ImportOwnedSteamGamesWithOriginalsAsync(steamApiKey, steamUserId).GetAwaiter().GetResult();

                    if (ownedGames != null && ownedGames.Count > 0)
                    {
                        var playniteNames = new HashSet<string>(
                            PlayniteApi.Database.Games
                                .Where(g => g.Platforms != null && g.Platforms.Any(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrEmpty(g.Name))
                                .Select(g => NormalizeGameName(CleanGameName(SanitizePath(g.Name)))),
                            StringComparer.OrdinalIgnoreCase
                        );
                        var existingLocalNames = new HashSet<string>(allGames.Select(x => NormalizeGameName(x.Name)), StringComparer.OrdinalIgnoreCase);

                        var ownSteamFeature = EnsureFeatureExists("[Own: Steam]");
                        var platform = PlayniteApi.Database.Platforms.FirstOrDefault(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase));

                        int added = 0;
                        foreach (var g in ownedGames)
                        {
                            if (!playniteNames.Contains(g.NormalizedName) && !existingLocalNames.Contains(g.NormalizedName))
                            {
                                var newGame = new GameMetadata
                                {
                                    Name = g.OriginalName,
                                    GameId = g.NormalizedName.ToLower(),
                                    Platforms = platform != null ? new HashSet<MetadataProperty> { new MetadataSpecProperty(platform.Name) } : null,
                                    Features = ownSteamFeature != null ? new HashSet<MetadataProperty> { new MetadataNameProperty(ownSteamFeature.Name) } : null,
                                    IsInstalled = false
                                    // No GameActions!
                                };
                                allGames.Add(newGame);
                                added++;
                            }
                        }
                        logger.Info($"Added {added} missing Steam games as local entries (not in Playnite or local folders) after Repacks for drive {drive.Name}");
                    }
                }

            }

            // ----------- PC Games (Scraped) -----------
            AddScraped(ScrapeSite().GetAwaiter().GetResult(), "SteamRip");
            AddScraped(AnkerScrapeGames().GetAwaiter().GetResult(), "AnkerGames");
            AddScraped(MagipackScrapeGames().GetAwaiter().GetResult(), "Magipack", gameName => MagipackIsDuplicate(gameName));
            AddScraped(ElamigosScrapeGames().GetAwaiter().GetResult(), "Elamigos", gameName => ElamigosIsDuplicate(gameName),
                (game, gameName, normalizedKey) =>
                {
                    // Fix name/ID if missing colon but original had one
                    if (!game.Name.Contains(":") && gameName.Contains(":"))
                    {
                        game.Name = gameName;
                        game.GameId = normalizedKey.ToLower();
                    }
                });
            AddScraped(FitGirlScrapeGames().GetAwaiter().GetResult(), "Fitgirl", gameName => FitGirlIsDuplicate(gameName));
            AddScraped(DodiRepacksScrapeGames().GetAwaiter().GetResult(), "Dodi", gameName => DodiRepacksIsDuplicate(gameName));
            AddScraped(MyAbandonScrapeGames().GetAwaiter().GetResult(), "My.Abandon", gameName => MyAbandonIsDuplicate(gameName));

            // ----------- YIELD ONLY PC (Windows) GAMES HERE -----------
            foreach (var game in allGames)
            {
                if (game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase) ||
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "PC (Windows)", StringComparison.OrdinalIgnoreCase))
                ))
                {
                    yield return game;
                }
            }

            // ----------- PlayStation 1 (PS1) -----------
            var ps1PlatformName = "Sony PlayStation";
            var ps1Roms = PS1_FindGameRoms("");

            // Build a set of normalized names for only PS1 games in DB
            var existingPS1Norms = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms != null)
                .SelectMany(g => g.Platforms
                    .Where(p =>
                        p.ToString().IndexOf("PlayStation 1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        p.ToString().IndexOf("PlayStation One", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        p.ToString().IndexOf("PS1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (p.GetType().GetProperty("Name") != null &&
                            (
                                ((string)p.GetType().GetProperty("Name").GetValue(p)).IndexOf("PlayStation 1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                ((string)p.GetType().GetProperty("Name").GetValue(p)).IndexOf("PlayStation One", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                ((string)p.GetType().GetProperty("Name").GetValue(p)).IndexOf("PS1", StringComparison.OrdinalIgnoreCase) >= 0
                            )
                        )
                    )
                    .Select(p => Myrient_NormalizeGameName(g.Name))
                )
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var game in Myrient_Sony_PS1_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Ensure deduplication is only for PS1 games (platform contains "PlayStation 1" or similar)
                bool isPS1 = game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().IndexOf("PlayStation 1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.ToString().IndexOf("PlayStation One", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.ToString().IndexOf("PS1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (p.GetType().GetProperty("Name") != null &&
                        (
                            ((string)p.GetType().GetProperty("Name").GetValue(p)).IndexOf("PlayStation 1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            ((string)p.GetType().GetProperty("Name").GetValue(p)).IndexOf("PlayStation One", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            ((string)p.GetType().GetProperty("Name").GetValue(p)).IndexOf("PS1", StringComparison.OrdinalIgnoreCase) >= 0
                        )
                    )
                );

                if (isPS1 && existingPS1Norms.Contains(norm)) continue;

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = ps1Roms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // Emulator Play Action
                    var duckStation = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("DuckStation", StringComparison.OrdinalIgnoreCase));
                    if (duckStation != null && duckStation.BuiltinProfiles != null && duckStation.BuiltinProfiles.Any())
                    {
                        var profile = duckStation.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();
                        game.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = duckStation.Id,
                            EmulatorProfileId = profile.Id,
                            Path = matchingRoms.First(),
                            IsPlayAction = true
                        });
                    }
                }
                allGames.Add(game);
                if (isPS1)
                    existingPS1Norms.Add(norm);
            }

            // --------- YIELD ONLY PS1 GAMES (for this section) ---------
            foreach (var game in allGames)
            {
                if (game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().IndexOf("PlayStation 1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.ToString().IndexOf("PlayStation One", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.ToString().IndexOf("PS1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (p.GetType().GetProperty("Name") != null &&
                        (
                            ((string)p.GetType().GetProperty("Name").GetValue(p)).IndexOf("PlayStation 1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            ((string)p.GetType().GetProperty("Name").GetValue(p)).IndexOf("PlayStation One", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            ((string)p.GetType().GetProperty("Name").GetValue(p)).IndexOf("PS1", StringComparison.OrdinalIgnoreCase) >= 0
                        )
                    )
                ))
                {
                    yield return game;
                }
            }


            // ----------- Nintendo 3DS -----------
            var n3dsPlatformName = "Nintendo 3DS";
            var n3dsRoms = Find3DSGameRoms("");

            // Build a set of normalized names for only 3DS games in DB
            var existing3DSNorms = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms != null)
                .SelectMany(g => g.Platforms
                    .Where(p =>
                        p.ToString().IndexOf("Nintendo 3DS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        p.ToString().IndexOf("3DS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (p.GetType().GetProperty("Name") != null &&
                            (
                                ((string)p.GetType().GetProperty("Name").GetValue(p)).IndexOf("Nintendo 3DS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                ((string)p.GetType().GetProperty("Name").GetValue(p)).IndexOf("3DS", StringComparison.OrdinalIgnoreCase) >= 0
                            )
                        )
                    )
                    .Select(p => Myrient_NormalizeGameName(g.Name))
                )
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var game in Myrient_Nintendo_3DS_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Ensure deduplication is only for 3DS games (platform contains "Nintendo 3DS" or similar)
                bool is3DS = game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().IndexOf("Nintendo 3DS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.ToString().IndexOf("3DS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (p.GetType().GetProperty("Name") != null &&
                        (
                            ((string)p.GetType().GetProperty("Name").GetValue(p)).IndexOf("Nintendo 3DS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            ((string)p.GetType().GetProperty("Name").GetValue(p)).IndexOf("3DS", StringComparison.OrdinalIgnoreCase) >= 0
                        )
                    )
                );

                if (is3DS && existing3DSNorms.Contains(norm)) continue;

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = n3dsRoms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // Emulator Play Action
                    var Lime3DSEmulator = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("Lime 3DS", StringComparison.OrdinalIgnoreCase));
                    if (Lime3DSEmulator != null && Lime3DSEmulator.BuiltinProfiles != null && Lime3DSEmulator.BuiltinProfiles.Any())
                    {
                        var profile = Lime3DSEmulator.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();
                        game.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = Lime3DSEmulator.Id,
                            EmulatorProfileId = profile.Id,
                            Path = matchingRoms.First(),
                            IsPlayAction = true
                        });
                    }
                }
                allGames.Add(game);
                if (is3DS)
                    existing3DSNorms.Add(norm);
            }

            // --------- YIELD ONLY 3DS GAMES (for this section) ---------
            foreach (var game in allGames)
            {
                if (game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().IndexOf("Nintendo 3DS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.ToString().IndexOf("3DS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (p.GetType().GetProperty("Name") != null &&
                        (
                            ((string)p.GetType().GetProperty("Name").GetValue(p)).IndexOf("Nintendo 3DS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            ((string)p.GetType().GetProperty("Name").GetValue(p)).IndexOf("3DS", StringComparison.OrdinalIgnoreCase) >= 0
                        )
                    )
                ))
                {
                    yield return game;
                }
            }



            // ----------- PlayStation 2 (PS2) -----------
            var ps2PlatformName = "Sony PlayStation 2";
            var ps2Roms = Myrient_FindGameRoms("");

            // Build a set of (normalized name, platform) for PS2 games only
            var existingPS2Norms = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms != null)
                .SelectMany(g => g.Platforms
                    .Where(p =>
                        p.ToString().Equals(ps2PlatformName, StringComparison.OrdinalIgnoreCase) ||
                        (p.GetType().GetProperty("Name") != null &&
                         string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), ps2PlatformName, StringComparison.OrdinalIgnoreCase))
                    )
                    .Select(p => Myrient_NormalizeGameName(g.Name))
                )
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var game in Myrient_Sony_PS2_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Only deduplicate against PS2 games, not any game
                bool isDuplicate = existingPS2Norms.Contains(norm);
                if (isDuplicate)
                    continue;

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = ps2Roms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // Emulator Play Action
                    var pcsx2 = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("PCSX2", StringComparison.OrdinalIgnoreCase));
                    if (pcsx2 != null && pcsx2.BuiltinProfiles != null && pcsx2.BuiltinProfiles.Any())
                    {
                        var profile = pcsx2.BuiltinProfiles.FirstOrDefault(p =>
                            (p.GetType().GetProperty("Name") != null &&
                             string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "Default QT", StringComparison.OrdinalIgnoreCase))
                        );
                        if (profile == null) profile = pcsx2.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();
                        game.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = pcsx2.Id,
                            EmulatorProfileId = profile.Id,
                            Path = matchingRoms.First(),
                            IsPlayAction = true
                        });
                    }
                }
                allGames.Add(game);
                existingPS2Norms.Add(norm);
            }

            // --------- YIELD ONLY PS2 GAMES (for this section) ---------
            foreach (var game in allGames)
            {
                if (game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().Equals(ps2PlatformName, StringComparison.OrdinalIgnoreCase) ||
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), ps2PlatformName, StringComparison.OrdinalIgnoreCase))
                ))
                {
                    yield return game;
                }
            }

            // ----------- Microsoft Xbox -----------

            var xboxPlatformName = "Microsoft Xbox";
            var xboxRoms = FindMicrosoftXboxGameRoms("");

            // Build a set of (normalized name) for Xbox games only
            var existingXboxNorms = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms != null)
                .SelectMany(g => g.Platforms
                    .Where(p =>
                        p.ToString().Equals(xboxPlatformName, StringComparison.OrdinalIgnoreCase) ||
                        (p.GetType().GetProperty("Name") != null &&
                         string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), xboxPlatformName, StringComparison.OrdinalIgnoreCase))
                    )
                    .Select(p => Myrient_NormalizeGameName(g.Name))
                )
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var game in Myrient_Microsoft_Xbox_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Only deduplicate against Microsoft Xbox games
                bool isDuplicate = existingXboxNorms.Contains(norm);
                if (isDuplicate)
                    continue;

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = xboxRoms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // Emulator Play Action
                    // Try with Xemu (most common Xbox emulator), fallback to Cxbx-Reloaded
                    var emulator = PlayniteApi.Database.Emulators.FirstOrDefault(
                        e => e.Name.Equals("Xemu", StringComparison.OrdinalIgnoreCase) ||
                             e.Name.Equals("Cxbx-Reloaded", StringComparison.OrdinalIgnoreCase)
                    );
                    if (emulator != null && emulator.BuiltinProfiles != null && emulator.BuiltinProfiles.Any())
                    {
                        var profile = emulator.BuiltinProfiles.FirstOrDefault(p =>
                            (p.GetType().GetProperty("Name") != null &&
                             string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "Default", StringComparison.OrdinalIgnoreCase))
                        );
                        if (profile == null) profile = emulator.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();
                        game.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = emulator.Id,
                            EmulatorProfileId = profile.Id,
                            Path = matchingRoms.First(),
                            IsPlayAction = true
                        });
                    }
                }
                allGames.Add(game);
                existingXboxNorms.Add(norm);
            }

            // --------- YIELD ONLY MICROSOFT XBOX GAMES (for this section) ---------
            foreach (var game in allGames)
            {
                if (game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().Equals(xboxPlatformName, StringComparison.OrdinalIgnoreCase) ||
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), xboxPlatformName, StringComparison.OrdinalIgnoreCase))
                ))
                {
                    yield return game;
                }
            }

            // ----------- Nintendo Wii -----------
            var wiiPlatformName = "Nintendo Wii";
            var wiiRoms = FindWIIGameRoms("");

            // Build a set of (normalized name) for Wii games only
            var existingWiiNorms = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms != null)
                .SelectMany(g => g.Platforms
                    .Where(p =>
                        p.ToString().Equals(wiiPlatformName, StringComparison.OrdinalIgnoreCase) ||
                        (p.GetType().GetProperty("Name") != null &&
                         string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), wiiPlatformName, StringComparison.OrdinalIgnoreCase))
                    )
                    .Select(p => Myrient_NormalizeGameName(g.Name))
                )
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var game in Myrient_Nintendo_WII_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Only deduplicate against Wii games, not any game
                bool isDuplicate = existingWiiNorms.Contains(norm);
                if (isDuplicate)
                    continue;

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = wiiRoms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // Emulator Play Action
                    var dolphin = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("Dolphin", StringComparison.OrdinalIgnoreCase));
                    if (dolphin != null && dolphin.BuiltinProfiles != null && dolphin.BuiltinProfiles.Any())
                    {
                        var profile = dolphin.BuiltinProfiles.FirstOrDefault(p =>
                            (p.GetType().GetProperty("Name") != null &&
                             string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "Default", StringComparison.OrdinalIgnoreCase))
                        );
                        if (profile == null) profile = dolphin.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();
                        game.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = dolphin.Id,
                            EmulatorProfileId = profile.Id,
                            Path = matchingRoms.First(),
                            IsPlayAction = true
                        });
                    }
                }
                allGames.Add(game);
                existingWiiNorms.Add(norm);
            }

            // --------- YIELD ONLY NINTENDO WII GAMES (for this section) ---------
            foreach (var game in allGames)
            {
                if (game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().Equals(wiiPlatformName, StringComparison.OrdinalIgnoreCase) ||
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), wiiPlatformName, StringComparison.OrdinalIgnoreCase))
                ))
                {
                    yield return game;
                }
            }

            // ----------- Nintendo Wii U -----------
            var wiiuPlatformName = "Nintendo Wii U";
            var wiiuRoms = FindWiiUGameRoms("");

            // Build a set of (normalized name) for Wii U games only
            var existingWiiUNorms = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms != null)
                .SelectMany(g => g.Platforms
                    .Where(p =>
                        p.ToString().Equals(wiiuPlatformName, StringComparison.OrdinalIgnoreCase) ||
                        (p.GetType().GetProperty("Name") != null &&
                         string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), wiiuPlatformName, StringComparison.OrdinalIgnoreCase))
                    )
                    .Select(p => Myrient_NormalizeGameName(g.Name))
                )
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var game in Myrient_Nintendo_WiiU_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Only deduplicate against Wii U games, not any game
                bool isDuplicate = existingWiiUNorms.Contains(norm);
                if (isDuplicate)
                    continue;

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = wiiuRoms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // Emulator Play Action
                    var cemu = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("Cemu", StringComparison.OrdinalIgnoreCase));
                    if (cemu != null && cemu.BuiltinProfiles != null && cemu.BuiltinProfiles.Any())
                    {
                        var profile = cemu.BuiltinProfiles.FirstOrDefault(p =>
                            (p.GetType().GetProperty("Name") != null &&
                             string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "Default", StringComparison.OrdinalIgnoreCase))
                        );
                        if (profile == null) profile = cemu.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();
                        game.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = cemu.Id,
                            EmulatorProfileId = profile.Id,
                            Path = matchingRoms.First(),
                            IsPlayAction = true
                        });
                    }
                }
                allGames.Add(game);
                existingWiiUNorms.Add(norm);
            }

            // --------- YIELD ONLY NINTENDO WII U GAMES (for this section) ---------
            foreach (var game in allGames)
            {
                if (game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().Equals(wiiuPlatformName, StringComparison.OrdinalIgnoreCase) ||
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), wiiuPlatformName, StringComparison.OrdinalIgnoreCase))
                ))
                {
                    yield return game;
                }
            }

            // ----------- Nintendo GameCube -----------
            var gamecubePlatformName = "Nintendo GameCube";
            var gamecubeRoms = FindGameCubeGameRoms("");

            // Build a set of (normalized name) for GameCube games only
            var existingGameCubeNorms = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms != null)
                .SelectMany(g => g.Platforms
                    .Where(p =>
                        p.ToString().Equals(gamecubePlatformName, StringComparison.OrdinalIgnoreCase) ||
                        (p.GetType().GetProperty("Name") != null &&
                         string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), gamecubePlatformName, StringComparison.OrdinalIgnoreCase))
                    )
                    .Select(p => Myrient_NormalizeGameName(g.Name))
                )
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var game in Myrient_Nintendo_GameCube_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Only deduplicate against GameCube games
                bool isDuplicate = existingGameCubeNorms.Contains(norm);
                if (isDuplicate)
                    continue;

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = gamecubeRoms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // Emulator Play Action
                    var dolphin = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("Dolphin", StringComparison.OrdinalIgnoreCase));
                    if (dolphin != null && dolphin.BuiltinProfiles != null && dolphin.BuiltinProfiles.Any())
                    {
                        var profile = dolphin.BuiltinProfiles.FirstOrDefault(p =>
                            (p.GetType().GetProperty("Name") != null &&
                             string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "Default", StringComparison.OrdinalIgnoreCase))
                        );
                        if (profile == null) profile = dolphin.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();
                        game.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = dolphin.Id,
                            EmulatorProfileId = profile.Id,
                            Path = matchingRoms.First(),
                            IsPlayAction = true
                        });
                    }
                }
                allGames.Add(game);
                existingGameCubeNorms.Add(norm);
            }

            // --------- YIELD ONLY NINTENDO GAMECUBE GAMES (for this section) ---------
            foreach (var game in allGames)
            {
                if (game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().Equals(gamecubePlatformName, StringComparison.OrdinalIgnoreCase) ||
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), gamecubePlatformName, StringComparison.OrdinalIgnoreCase))
                ))
                {
                    yield return game;
                }
            }


            // ----------- Nintendo Game Boy -----------
            var gbPlatformName = "Nintendo Game Boy";
            var gbRoms = FindGameBoyGameRoms("");

            // Build a set of (normalized name) for Game Boy games only
            var existingGBNorms = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms != null)
                .SelectMany(g => g.Platforms
                    .Where(p =>
                        p.ToString().Equals(gbPlatformName, StringComparison.OrdinalIgnoreCase) ||
                        (p.GetType().GetProperty("Name") != null &&
                         string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), gbPlatformName, StringComparison.OrdinalIgnoreCase))
                    )
                    .Select(p => Myrient_NormalizeGameName(g.Name))
                )
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var game in Myrient_Nintendo_GameBoy_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Only deduplicate against Game Boy games
                bool isDuplicate = existingGBNorms.Contains(norm);
                if (isDuplicate)
                    continue;

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = gbRoms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // Emulator Play Action
                    var emulator = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("mGBA", StringComparison.OrdinalIgnoreCase) || e.Name.Equals("Gambatte", StringComparison.OrdinalIgnoreCase));
                    if (emulator != null && emulator.BuiltinProfiles != null && emulator.BuiltinProfiles.Any())
                    {
                        var profile = emulator.BuiltinProfiles.FirstOrDefault(p =>
                            (p.GetType().GetProperty("Name") != null &&
                             string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "Default", StringComparison.OrdinalIgnoreCase))
                        );
                        if (profile == null) profile = emulator.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();
                        game.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = emulator.Id,
                            EmulatorProfileId = profile.Id,
                            Path = matchingRoms.First(),
                            IsPlayAction = true
                        });
                    }
                }
                allGames.Add(game);
                existingGBNorms.Add(norm);
            }

            // --------- YIELD ONLY NINTENDO GAME BOY GAMES (for this section) ---------
            foreach (var game in allGames)
            {
                if (game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().Equals(gbPlatformName, StringComparison.OrdinalIgnoreCase) ||
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), gbPlatformName, StringComparison.OrdinalIgnoreCase))
                ))
                {
                    yield return game;
                }
            }

            // ----------- Nintendo Game Boy Color -----------
            var gbcPlatformName = "Nintendo Game Boy Color";
            var gbcRoms = FindGameBoyColorGameRoms("");

            // Build a set of (normalized name) for Game Boy Color games only
            var existingGBCNorms = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms != null)
                .SelectMany(g => g.Platforms
                    .Where(p =>
                        p.ToString().Equals(gbcPlatformName, StringComparison.OrdinalIgnoreCase) ||
                        (p.GetType().GetProperty("Name") != null &&
                         string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), gbcPlatformName, StringComparison.OrdinalIgnoreCase))
                    )
                    .Select(p => Myrient_NormalizeGameName(g.Name))
                )
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var game in Myrient_Nintendo_GameBoyColor_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Only deduplicate against Game Boy Color games
                bool isDuplicate = existingGBCNorms.Contains(norm);
                if (isDuplicate)
                    continue;

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = gbcRoms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // Emulator Play Action
                    var emulator = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("mGBA", StringComparison.OrdinalIgnoreCase) || e.Name.Equals("Gambatte", StringComparison.OrdinalIgnoreCase));
                    if (emulator != null && emulator.BuiltinProfiles != null && emulator.BuiltinProfiles.Any())
                    {
                        var profile = emulator.BuiltinProfiles.FirstOrDefault(p =>
                            (p.GetType().GetProperty("Name") != null &&
                             string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "Default", StringComparison.OrdinalIgnoreCase))
                        );
                        if (profile == null) profile = emulator.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();
                        game.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = emulator.Id,
                            EmulatorProfileId = profile.Id,
                            Path = matchingRoms.First(),
                            IsPlayAction = true
                        });
                    }
                }
                allGames.Add(game);
                existingGBCNorms.Add(norm);
            }

            // --------- YIELD ONLY NINTENDO GAME BOY COLOR GAMES (for this section) ---------
            foreach (var game in allGames)
            {
                if (game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().Equals(gbcPlatformName, StringComparison.OrdinalIgnoreCase) ||
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), gbcPlatformName, StringComparison.OrdinalIgnoreCase))
                ))
                {
                    yield return game;
                }
            }

            // ----------- Nintendo Game Boy Advance -----------
            var gbaPlatformName = "Nintendo Game Boy Advance";
            var gbaRoms = FindGameBoyAdvanceGameRoms("");

            // Build a set of (normalized name) for Game Boy Advance games only
            var existingGBANorms = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms != null)
                .SelectMany(g => g.Platforms
                    .Where(p =>
                        p.ToString().Equals(gbaPlatformName, StringComparison.OrdinalIgnoreCase) ||
                        (p.GetType().GetProperty("Name") != null &&
                         string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), gbaPlatformName, StringComparison.OrdinalIgnoreCase))
                    )
                    .Select(p => Myrient_NormalizeGameName(g.Name))
                )
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var game in Myrient_Nintendo_GameBoyAdvance_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Only deduplicate against Game Boy Advance games
                bool isDuplicate = existingGBANorms.Contains(norm);
                if (isDuplicate)
                    continue;

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = gbaRoms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // Emulator Play Action
                    var emulator = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("mGBA", StringComparison.OrdinalIgnoreCase) || e.Name.Equals("VBA-M", StringComparison.OrdinalIgnoreCase));
                    if (emulator != null && emulator.BuiltinProfiles != null && emulator.BuiltinProfiles.Any())
                    {
                        var profile = emulator.BuiltinProfiles.FirstOrDefault(p =>
                            (p.GetType().GetProperty("Name") != null &&
                             string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "Default", StringComparison.OrdinalIgnoreCase))
                        );
                        if (profile == null) profile = emulator.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();
                        game.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = emulator.Id,
                            EmulatorProfileId = profile.Id,
                            Path = matchingRoms.First(),
                            IsPlayAction = true
                        });
                    }
                }
                allGames.Add(game);
                existingGBANorms.Add(norm);
            }

            // --------- YIELD ONLY NINTENDO GAME BOY ADVANCE GAMES (for this section) ---------
            foreach (var game in allGames)
            {
                if (game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().Equals(gbaPlatformName, StringComparison.OrdinalIgnoreCase) ||
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), gbaPlatformName, StringComparison.OrdinalIgnoreCase))
                ))
                {
                    yield return game;
                }
            }

            // ----------- Nintendo 64 (N64) -----------
            var n64PlatformName = "Nintendo 64";
            var N64Roms = FindN64GameRoms("");

            // Build a set of (normalized name) for Nintendo 64 games only
            var existingN64Norms = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms != null)
                .SelectMany(g => g.Platforms
                    .Where(p =>
                        (p.GetType().GetProperty("Name") != null &&
                            string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), n64PlatformName, StringComparison.OrdinalIgnoreCase)) ||
                        p.ToString().Equals(n64PlatformName, StringComparison.OrdinalIgnoreCase)
                    )
                    .Select(p => Myrient_NormalizeGameName(g.Name))
                )
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var game in Myrient_Nintendo64_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Only deduplicate against N64 games for this plugin
                bool isDuplicate = existingN64Norms.Contains(norm);
                if (isDuplicate)
                    continue;

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = N64Roms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // Emulator Play Action
                    var project64 = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("Project64", StringComparison.OrdinalIgnoreCase));
                    if (project64 != null && project64.BuiltinProfiles != null && project64.BuiltinProfiles.Any())
                    {
                        var profile = project64.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();
                        game.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = project64.Id,
                            EmulatorProfileId = profile.Id,
                            Path = matchingRoms.First(),
                            IsPlayAction = true
                        });
                    }
                }
                allGames.Add(game);
                existingN64Norms.Add(norm);
            }

            // --------- YIELD ONLY NINTENDO 64 GAMES (for this section) ---------
            foreach (var game in allGames)
            {
                if (game.Platforms != null && game.Platforms.Any(p =>
                    (p.GetType().GetProperty("Name") != null &&
                        string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), n64PlatformName, StringComparison.OrdinalIgnoreCase)) ||
                    p.ToString().Equals(n64PlatformName, StringComparison.OrdinalIgnoreCase)
                ))
                {
                    yield return game;
                }
            }

            // ----------- Microsoft Xbox 360 (Physical) -----------
            var xbox360RomExtensions = new[] { ".zar", ".xex", ".god", ".iso" };
            var xbox360Roms = new List<string>();
            const string searchDirectory = @"Roms\Microsoft - Xbox 360\Games";
            foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    var rootPath = Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                    if (Directory.Exists(rootPath))
                    {
                        xbox360Roms.AddRange(
                            Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
                                .Where(file => xbox360RomExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                        );
                    }
                }
                catch { }
            }
            var romsByNormName = xbox360Roms
                .GroupBy(r => Myrient_NormalizeGameName(Path.GetFileNameWithoutExtension(r)))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var allScrapedGames = Myrient_Xbox360_ScrapeStaticPage().GetAwaiter().GetResult() ?? new List<GameMetadata>();
            var allDbGames = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms != null && g.Platforms.Any(p =>
                    // Defensive: handle both .Name property and ToString fallback
                    (p.GetType().GetProperty("Name") != null &&
                        string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "Microsoft Xbox 360", StringComparison.OrdinalIgnoreCase)) ||
                    p.ToString().Equals("Microsoft Xbox 360", StringComparison.OrdinalIgnoreCase)
                ))
                .Select(g => new GameMetadata
                {
                    Name = g.Name,
                    GameId = g.GameId,
                    Platforms = new HashSet<MetadataProperty>(g.Platforms.Select(p =>
                        p.GetType().GetProperty("Name") != null
                            ? new MetadataSpecProperty((string)p.GetType().GetProperty("Name").GetValue(p))
                            : new MetadataSpecProperty(p.ToString())
                    )),
                    GameActions = g.GameActions?.ToList() ?? new List<GameAction>(),
                    Roms = g.Roms?.ToList(),
                    IsInstalled = g.IsInstalled,
                    InstallDirectory = g.InstallDirectory
                })
                .ToList();

            var allGamesByNorm = new Dictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (var game in allDbGames)
            {
                var norm = Myrient_NormalizeGameName(game.Name);
                if (!allGamesByNorm.ContainsKey(norm))
                    allGamesByNorm[norm] = game;
            }
            foreach (var game in allScrapedGames)
            {
                var norm = Myrient_NormalizeGameName(game.Name);
                allGamesByNorm[norm] = game;
            }
            var processedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in allGamesByNorm)
            {
                var norm = kvp.Key;
                var game = kvp.Value;
                string platformName = "Microsoft Xbox 360";
                string uniqueKey = $"{norm}|{platformName}";

                if (!processedKeys.Add(uniqueKey))
                    continue;

                var matchingRoms = romsByNormName.TryGetValue(norm, out var foundRoms) ? foundRoms : new List<string>();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms
                        .Select(r => new GameRom { Name = Path.GetFileName(r), Path = r })
                        .ToList();

                    // Play Action for Xenia
                    var xenia = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("Xenia", StringComparison.OrdinalIgnoreCase));
                    if (xenia != null && xenia.BuiltinProfiles != null && xenia.BuiltinProfiles.Any())
                    {
                        var profile = xenia.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();

                        game.GameActions.RemoveAll(a => a.Type == GameActionType.Emulator);

                        game.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = xenia.Id,
                            EmulatorProfileId = profile.Id,
                            Path = matchingRoms.First(),
                            IsPlayAction = true
                        });
                    }
                }
                else
                {
                    game.IsInstalled = false;
                    game.Roms = null;
                    game.InstallDirectory = null;
                    if (game.GameActions != null)
                    {
                        game.GameActions.RemoveAll(a => a.Type == GameActionType.Emulator);
                    }
                }
                allGames.Add(game);
            }

            // ----------- Microsoft Xbox 360 Digital (XBLA/XBLIG) -----------
            var xbox360DigitalRoms = FindXbox360DigitalGameRoms("");
            var digitalRomsByNormName = xbox360DigitalRoms
                .GroupBy(r => Myrient_NormalizeGameName(Path.GetFileNameWithoutExtension(r)))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var dbGameKeys_Xbox360Digital = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id && g.Platforms != null && g.Platforms.Any(p =>
                        // Defensive: handle both .Name property and ToString fallback
                        (p.GetType().GetProperty("Name") != null &&
                            string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "Microsoft Xbox 360", StringComparison.OrdinalIgnoreCase)) ||
                        p.ToString().Equals("Microsoft Xbox 360", StringComparison.OrdinalIgnoreCase)
                    ))
                    .SelectMany(g => g.Platforms.Select(p =>
                        $"{Myrient_NormalizeGameName(g.Name)}|{(p.GetType().GetProperty("Name") != null ? (string)p.GetType().GetProperty("Name").GetValue(p) : p.ToString())}")),
                StringComparer.OrdinalIgnoreCase);

            var processedKeys_Xbox360Digital = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var game in Myrient_Xbox360Digital_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);
                string platformName = "Microsoft Xbox 360";
                string uniqueKey = $"{norm}|{platformName}";

                if (dbGameKeys_Xbox360Digital.Contains(uniqueKey) || processedKeys_Xbox360Digital.Contains(uniqueKey))
                    continue;
                processedKeys_Xbox360Digital.Add(uniqueKey);

                digitalRomsByNormName.TryGetValue(norm, out var matchingRoms);

                if (matchingRoms != null && matchingRoms.Count > 0)
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms[0]);
                    game.Roms = matchingRoms
                        .Select(r => new GameRom { Name = Path.GetFileName(r), Path = r })
                        .ToList();

                    // Play Action for Xenia
                    var xenia = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("Xenia", StringComparison.OrdinalIgnoreCase));
                    if (xenia != null && xenia.BuiltinProfiles != null && xenia.BuiltinProfiles.Any())
                    {
                        var profile = xenia.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();

                        game.GameActions.RemoveAll(a => a.Type == GameActionType.Emulator);

                        game.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = xenia.Id,
                            EmulatorProfileId = profile.Id,
                            Path = matchingRoms[0],
                            IsPlayAction = true
                        });
                    }
                }
                else
                {
                    game.IsInstalled = false;
                    game.Roms = null;
                    game.InstallDirectory = null;
                    if (game.GameActions != null)
                    {
                        game.GameActions.RemoveAll(a => a.Type == GameActionType.Emulator);
                    }
                }
                allGames.Add(game);
            }

            // ----------- Sega Saturn -----------
            var saturnPlatformName = "Sega Saturn";
            var saturnRoms = FindSegaSaturnGameRoms("");

            // Build a set of (normalized name) for Sega Saturn games only
            var existingSaturnNorms = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms != null)
                .SelectMany(g => g.Platforms
                    .Where(p =>
                        p.ToString().Equals(saturnPlatformName, StringComparison.OrdinalIgnoreCase) ||
                        (p.GetType().GetProperty("Name") != null &&
                         string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), saturnPlatformName, StringComparison.OrdinalIgnoreCase))
                    )
                    .Select(p => Myrient_NormalizeGameName(g.Name))
                )
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var game in Myrient_Sega_Saturn_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Only deduplicate against Sega Saturn games
                bool isDuplicate = existingSaturnNorms.Contains(norm);
                if (isDuplicate)
                    continue;

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = saturnRoms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // Emulator Play Action
                    // Popular Saturn emulators: Mednafen, Yabause, Kronos, SSF
                    var emulator = PlayniteApi.Database.Emulators.FirstOrDefault(e =>
                        e.Name.Equals("Mednafen", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.Equals("Yabause", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.Equals("Kronos", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.Equals("SSF", StringComparison.OrdinalIgnoreCase)
                    );
                    if (emulator != null && emulator.BuiltinProfiles != null && emulator.BuiltinProfiles.Any())
                    {
                        var profile = emulator.BuiltinProfiles.FirstOrDefault(p =>
                            (p.GetType().GetProperty("Name") != null &&
                             string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "Default", StringComparison.OrdinalIgnoreCase))
                        );
                        if (profile == null) profile = emulator.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();
                        game.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = emulator.Id,
                            EmulatorProfileId = profile.Id,
                            Path = matchingRoms.First(),
                            IsPlayAction = true
                        });
                    }
                }
                allGames.Add(game);
                existingSaturnNorms.Add(norm);
            }

            // --------- YIELD ONLY SEGA SATURN GAMES (for this section) ---------
            foreach (var game in allGames)
            {
                if (game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().Equals(saturnPlatformName, StringComparison.OrdinalIgnoreCase) ||
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), saturnPlatformName, StringComparison.OrdinalIgnoreCase))
                ))
                {
                    yield return game;
                }
            }


            // ----------- Sega Dreamcast -----------
            var dreamcastPlatformName = "Sega Dreamcast";
            var dreamcastRoms = FindSegaDreamcastGameRoms("");

            // Build a set of (normalized name) for Sega Dreamcast games only
            var existingDreamcastNorms = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms != null)
                .SelectMany(g => g.Platforms
                    .Where(p =>
                        p.ToString().Equals(dreamcastPlatformName, StringComparison.OrdinalIgnoreCase) ||
                        (p.GetType().GetProperty("Name") != null &&
                         string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), dreamcastPlatformName, StringComparison.OrdinalIgnoreCase))
                    )
                    .Select(p => Myrient_NormalizeGameName(g.Name))
                )
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var game in Myrient_Sega_Dreamcast_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Only deduplicate against Sega Dreamcast games
                bool isDuplicate = existingDreamcastNorms.Contains(norm);
                if (isDuplicate)
                    continue;

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = dreamcastRoms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // Emulator Play Action
                    // Popular Dreamcast emulators: Redream, Flycast, Demul, DEmul, NullDC, Reicast
                    var emulator = PlayniteApi.Database.Emulators.FirstOrDefault(e =>
                        e.Name.Equals("Redream", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.Equals("Flycast", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.Equals("Demul", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.Equals("DEmul", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.Equals("NullDC", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.Equals("Reicast", StringComparison.OrdinalIgnoreCase)
                    );
                    if (emulator != null && emulator.BuiltinProfiles != null && emulator.BuiltinProfiles.Any())
                    {
                        var profile = emulator.BuiltinProfiles.FirstOrDefault(p =>
                            (p.GetType().GetProperty("Name") != null &&
                             string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "Default", StringComparison.OrdinalIgnoreCase))
                        );
                        if (profile == null) profile = emulator.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();
                        game.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = emulator.Id,
                            EmulatorProfileId = profile.Id,
                            Path = matchingRoms.First(),
                            IsPlayAction = true
                        });
                    }
                }

                allGames.Add(game);
                existingDreamcastNorms.Add(norm);
            }

            // --------- YIELD ONLY SEGA DREAMCAST GAMES (for this section) ---------
            foreach (var game in allGames)
            {
                if (game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().Equals(dreamcastPlatformName, StringComparison.OrdinalIgnoreCase) ||
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), dreamcastPlatformName, StringComparison.OrdinalIgnoreCase))
                ))
                {
                    yield return game;
                }
            }


            // ----------- Nintendo Switch -----------
            var switchPlatformName = "Nintendo Switch";

            // Build a set of (normalized name) for Nintendo Switch games only
            var existingSwitchNorms = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms != null)
                .SelectMany(g => g.Platforms
                    .Where(p =>
                        p.ToString().Equals(switchPlatformName, StringComparison.OrdinalIgnoreCase) ||
                        (p.GetType().GetProperty("Name") != null &&
                         string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), switchPlatformName, StringComparison.OrdinalIgnoreCase))
                    )
                    .Select(p => Myrient_NormalizeGameName(g.Name))
                )
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // FIX: Call FindNintendoSwitchGameRoms() with NO arguments
            var switchRoms = FindNintendoSwitchGameRoms();

            foreach (var game in Myrient_Nintendo_Switch_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Only deduplicate against Nintendo Switch games
                if (existingSwitchNorms.Contains(norm))
                    continue;

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = switchRoms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // Emulator Play Action (Yuzu, Ryujinx are common Switch emulators, prefer Yuzu if available)
                    var emulator = PlayniteApi.Database.Emulators
                        .FirstOrDefault(e => e.Name.Equals("Yuzu", StringComparison.OrdinalIgnoreCase)) ??
                        PlayniteApi.Database.Emulators
                        .FirstOrDefault(e => e.Name.Equals("Ryujinx", StringComparison.OrdinalIgnoreCase));

                    if (emulator != null && emulator.BuiltinProfiles != null && emulator.BuiltinProfiles.Any())
                    {
                        var profile = emulator.BuiltinProfiles.FirstOrDefault(p =>
                            (p.GetType().GetProperty("Name") != null &&
                             string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "Default", StringComparison.OrdinalIgnoreCase))
                        );
                        if (profile == null) profile = emulator.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();
                        // Only add one emulator play action
                        if (!game.GameActions.Any(a => a.Type == GameActionType.Emulator))
                        {
                            game.GameActions.Add(new GameAction
                            {
                                Name = "Play",
                                Type = GameActionType.Emulator,
                                EmulatorId = emulator.Id,
                                EmulatorProfileId = profile.Id,
                                Path = matchingRoms.First(),
                                IsPlayAction = true
                            });
                        }
                    }
                }

                allGames.Add(game);
                existingSwitchNorms.Add(norm);
            }

            // --------- YIELD ONLY NINTENDO SWITCH GAMES (for this section) ---------
            foreach (var game in allGames)
            {
                if (game.Platforms != null && game.Platforms.Any(p =>
                    p.ToString().Equals(switchPlatformName, StringComparison.OrdinalIgnoreCase) ||
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), switchPlatformName, StringComparison.OrdinalIgnoreCase))
                ))
                {
                    yield return game;
                }
            }


            // Final global deduplication step: Only one entry per normalized name + platform
            var dedupedGames = allGames
                .GroupBy(g =>
                {
                    // Use normalized name and platform for deduplication
                    var normalizedName = NormalizeGameName(g.Name);
                    // Handle multiple platforms gracefully
                    string platform =
                        g.Platforms?.FirstOrDefault() is var plat && plat != null
                            ? (plat.GetType().GetProperty("Name") != null
                                ? (string)plat.GetType().GetProperty("Name").GetValue(plat)
                                : plat.ToString())
                            : string.Empty;
                    return $"{normalizedName}|{platform}";
                })
                .Select(g =>
                {
                    // Prefer installed games, then prefer most GameActions, then just take the first
                    return g
                        .OrderByDescending(x => x.IsInstalled)
                        .ThenByDescending(x => x.GameActions?.Count ?? 0)
                        .First();
                })
                .ToList();

            foreach (var game in dedupedGames)
            {
                yield return game; // ✅ Correct way in an iterator
            }
        }


        // ... (your existing using statements and GameStore class members)

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            // Path to the prompted games tracking file
            string promptedGamesPath = Path.Combine(GetPluginUserDataPath(), "enjoymentPromptedGames.txt");

            // Load prompted games only once per session
            if (OnGameStopped_promptedGames == null)
            {
                if (!File.Exists(promptedGamesPath))
                    OnGameStopped_promptedGames = new HashSet<Guid>();
                else
                    OnGameStopped_promptedGames = System.Linq.Enumerable.ToHashSet(
                        File.ReadAllLines(promptedGamesPath)
                            .Select(line => Guid.TryParse(line, out var id) ? id : Guid.Empty)
                            .Where(id => id != Guid.Empty)
                    );
            }

            var game = args.Game;
            if (game == null)
                return;

            // ===== COMPATIBILITY REPORT LOGIC FOR ALL NON-PC PLATFORMS =====
            string platformName = "";
            if (game.PlatformIds != null && game.PlatformIds.Count > 0)
            {
                var plat = PlayniteApi.Database.Platforms.Get((Guid)game.PlatformIds.First());
                if (plat != null)
                    platformName = plat.Name;
            }

            int playCount = (int)game.PlayCount;

            // For every game that is NOT PC
            if (!platformName.Equals("PC", StringComparison.OrdinalIgnoreCase) && playCount >= 3)
            {
                string compatDir = Path.Combine(GetPluginUserDataPath(), "Compat", platformName);
                Directory.CreateDirectory(compatDir);
                string reportPath = Path.Combine(compatDir, $"{SanitizeFileName(game.Name)}.txt");

                // Only show once per game
                if (!File.Exists(reportPath))
                {
                    var win = PlayniteApi.Dialogs.GetCurrentAppWindow();
                    win?.Activate();

                    // Menu 1: Did it boot?
                    var bootOptions = new List<MessageBoxOption>
            {
                new MessageBoxOption("Yes", true, false),
                new MessageBoxOption("No", false, false)
            };
                    var bootMapping = new Dictionary<MessageBoxOption, string>
            {
                { bootOptions[0], "Yes" },
                { bootOptions[1], "No" }
            };
                    var bootResult = PlayniteApi.Dialogs.ShowMessage(
                        "Did it boot?", $"{platformName} Compatibility Report", MessageBoxImage.Question, bootOptions);
                    if (bootResult == null) return;
                    string bootAns = bootMapping[bootResult];

                    // Menu 2: Graphics?
                    var gfxOptions = new List<MessageBoxOption>
            {
                new MessageBoxOption("Good", true, false),
                new MessageBoxOption("Okay", false, false),
                new MessageBoxOption("Bad", false, false)
            };
                    var gfxMapping = new Dictionary<MessageBoxOption, string>
            {
                { gfxOptions[0], "Good" },
                { gfxOptions[1], "Okay" },
                { gfxOptions[2], "Bad" }
            };
                    var gfxResult = PlayniteApi.Dialogs.ShowMessage(
                        "Graphics?", $"{platformName} Compatibility Report", MessageBoxImage.Question, gfxOptions);
                    if (gfxResult == null) return;
                    string gfxAns = gfxMapping[gfxResult];

                    // Menu 3: Audio?
                    var audioResult = PlayniteApi.Dialogs.ShowMessage(
                        "Audio?", $"{platformName} Compatibility Report", MessageBoxImage.Question, gfxOptions);
                    if (audioResult == null) return;
                    string audioAns = gfxMapping[audioResult];

                    // Menu 4: Playable?
                    var playOptions = new List<MessageBoxOption>
            {
                new MessageBoxOption("Yes", true, false),
                new MessageBoxOption("No", false, false)
            };
                    var playMapping = new Dictionary<MessageBoxOption, string>
            {
                { playOptions[0], "Yes" },
                { playOptions[1], "No" }
            };
                    var playResult = PlayniteApi.Dialogs.ShowMessage(
                        "Playable?", $"{platformName} Compatibility Report", MessageBoxImage.Question, playOptions);
                    if (playResult == null) return;
                    string playAns = playMapping[playResult];

                    // Write report file
                    var reportLines = new[]
                    {
                $"Game: \"{game.Name}\"",
                $"Boot: {bootAns}",
                $"Graphics: {gfxAns}",
                $"Audio: {audioAns}",
                $"Playable: {playAns}"
            };
                    File.WriteAllLines(reportPath, reportLines);
                }
            }

            // ===== ENJOYMENT PROMPT LOGIC =====
            if (game.Playtime < 36000)
                return;

            if (OnGameStopped_promptedGames.Contains(game.Id))
                return;

            // === MENU 1: Are You Enjoying this Game? ===
            var enjoyOptions = new List<MessageBoxOption>
    {
        new MessageBoxOption("Yes", true, false),
        new MessageBoxOption("No", false, false),
        new MessageBoxOption("Cancel", false, true)
    };
            var enjoyMapping = new Dictionary<MessageBoxOption, string>
    {
        { enjoyOptions[0], "Yes" },
        { enjoyOptions[1], "No" },
        { enjoyOptions[2], "Cancel" }
    };

            var win2 = PlayniteApi.Dialogs.GetCurrentAppWindow();
            win2?.Activate();
            var enjoyResult = PlayniteApi.Dialogs.ShowMessage(
                "Are you enjoying this game?",
                "Enjoyment Check",
                MessageBoxImage.Question,
                enjoyOptions
            );
            if (enjoyResult == null || enjoyMapping[enjoyResult] == "Cancel")
            {
                OnGameStopped_promptedGames.Add(game.Id);
                File.AppendAllLines(promptedGamesPath, new[] { game.Id.ToString() });
                return;
            }

            if (enjoyMapping[enjoyResult] == "No")
            {
                // === MENU 2: Uninstall Game? ===
                var uninstallOptions = new List<MessageBoxOption>
        {
            new MessageBoxOption("Yes", true, false),
            new MessageBoxOption("No", false, false),
            new MessageBoxOption("Cancel", false, true)
        };
                var uninstallMapping = new Dictionary<MessageBoxOption, string>
        {
            { uninstallOptions[0], "Yes" },
            { uninstallOptions[1], "No" },
            { uninstallOptions[2], "Cancel" }
        };
                win2?.Activate();
                var uninstallResult = PlayniteApi.Dialogs.ShowMessage(
                    "Uninstall Game?",
                    "Uninstall",
                    MessageBoxImage.Question,
                    uninstallOptions
                );
                if (uninstallResult == null || uninstallMapping[uninstallResult] == "Cancel")
                {
                    OnGameStopped_promptedGames.Add(game.Id);
                    File.AppendAllLines(promptedGamesPath, new[] { game.Id.ToString() });
                    return;
                }
                if (uninstallMapping[uninstallResult] == "Yes")
                {
                    var installDir = game.InstallDirectory;
                    bool uninstalled = false;
                    if (!string.IsNullOrWhiteSpace(installDir) && Directory.Exists(installDir))
                    {
                        var uninstaller = Directory.EnumerateFiles(installDir, "uninstall*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
                        if (!string.IsNullOrEmpty(uninstaller))
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(uninstaller);
                                PlayniteApi.Dialogs.ShowMessage("Uninstall process started.", "Info");
                                game.IsInstalled = false;
                                PlayniteApi.Database.Games.Update(game);
                                uninstalled = true;
                            }
                            catch (Exception ex)
                            {
                                PlayniteApi.Dialogs.ShowErrorMessage($"Failed to start uninstaller: {ex.Message}", "Error");
                            }
                        }
                    }
                    if (!uninstalled)
                    {
                        // === MENU 3: Delete Game? ===
                        var delOptions = new List<MessageBoxOption>
                {
                    new MessageBoxOption("Yes", true, false),
                    new MessageBoxOption("No", false, false),
                    new MessageBoxOption("Cancel", false, true)
                };
                        var delMapping = new Dictionary<MessageBoxOption, string>
                {
                    { delOptions[0], "Yes" },
                    { delOptions[1], "No" },
                    { delOptions[2], "Cancel" }
                };
                        win2?.Activate();
                        var delResult = PlayniteApi.Dialogs.ShowMessage(
                            "No uninstaller found. Delete game folder?",
                            "Delete Game",
                            MessageBoxImage.Question,
                            delOptions
                        );
                        if (delResult != null && delMapping[delResult] == "Yes")
                        {
                            try
                            {
                                Directory.Delete(game.InstallDirectory, true);
                                PlayniteApi.Dialogs.ShowMessage("Game folder deleted.", "Info");
                                game.IsInstalled = false;
                                PlayniteApi.Database.Games.Update(game);
                            }
                            catch (Exception ex)
                            {
                                PlayniteApi.Dialogs.ShowErrorMessage($"Failed to delete folder: {ex.Message}", "Error");
                            }
                        }
                    }
                }
                OnGameStopped_promptedGames.Add(game.Id);
                File.AppendAllLines(promptedGamesPath, new[] { game.Id.ToString() });
                return;
            }
            else // User is enjoying game
            {
                // === MENU 4: Rate out of 5 Stars ===
                var rateOptions = new List<MessageBoxOption>
        {
            new MessageBoxOption("1", false, false),
            new MessageBoxOption("2", false, false),
            new MessageBoxOption("3", false, false),
            new MessageBoxOption("4", false, false),
            new MessageBoxOption("5", true, false),
            new MessageBoxOption("Cancel", false, true)
        };
                var rateMapping = new Dictionary<MessageBoxOption, int?>
        {
            { rateOptions[0], 20 },
            { rateOptions[1], 40 },
            { rateOptions[2], 60 },
            { rateOptions[3], 80 },
            { rateOptions[4], 100 },
            { rateOptions[5], null }
        };
                win2?.Activate();
                var rateResult = PlayniteApi.Dialogs.ShowMessage(
                    "Rate out of 5 Stars:",
                    "User Score",
                    MessageBoxImage.Question,
                    rateOptions
                );
                if (rateResult != null && rateMapping[rateResult].HasValue)
                {
                    int userScore = rateMapping[rateResult].Value;
                    game.UserScore = userScore;
                    PlayniteApi.Database.Games.Update(game);
                }
                OnGameStopped_promptedGames.Add(game.Id);
                File.AppendAllLines(promptedGamesPath, new[] { game.Id.ToString() });
                return;
            }
        }

        // --- Helpers ---
        private string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
        
        private GameFeature EnsureFeatureExists(string featureName)
        {
            var feature = PlayniteApi.Database.Features
                .FirstOrDefault(f => f.Name.Equals(featureName, StringComparison.OrdinalIgnoreCase));
            if (feature == null || feature.Id == Guid.Empty)
            {
                feature = new GameFeature(featureName);
                PlayniteApi.Database.Features.Add(feature);
                PlayniteApi.Database.Features.Update(feature);
                // Re-fetch for valid ID
                feature = PlayniteApi.Database.Features
                    .FirstOrDefault(f => f.Name.Equals(featureName, StringComparison.OrdinalIgnoreCase));
            }
            return feature;
        }

        private string GetPluginUserDataPathLocal()
        {
            // Build the user data path under %APPDATA%\Playnite\ExtensionsData\<PluginID>
            string userDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                 "Playnite", "ExtensionsData", "55eeaffc-4d50-4d08-85fb-d8e49800d058");
            if (!Directory.Exists(userDataPath))
            {
                Directory.CreateDirectory(userDataPath);
            }
            return userDataPath;
        }

        // Install Ready Feature
        private void AddInstallReadyFeature(Game existingGame)
        {
            if (existingGame == null)
                return;

            if (existingGame.PluginId != Id ||
                existingGame.Platforms == null ||
                !existingGame.Platforms.Any(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)))
                return;

            var installReadyFeature = EnsureFeatureExists("[Install Ready]");
            if (installReadyFeature == null || installReadyFeature.Id == Guid.Empty)
                return;

            if (existingGame.FeatureIds == null)
                existingGame.FeatureIds = new List<Guid>();

            if (!existingGame.FeatureIds.Contains(installReadyFeature.Id))
            {
                existingGame.FeatureIds.Add(installReadyFeature.Id);
                PlayniteApi.Database.Games.Update(existingGame);
            }
        }
        private void AddInstallReadyFeature(GameMetadata newGame)
        {
            if (newGame == null)
                return;

            if (newGame.Platforms == null ||
                !newGame.Platforms.OfType<MetadataSpecProperty>().Any(p =>
                    p.Id.Equals("pc_windows", StringComparison.OrdinalIgnoreCase)))
                return;

            var installReadyFeature = EnsureFeatureExists("[Install Ready]");
            if (installReadyFeature == null || installReadyFeature.Id == Guid.Empty)
                return;

            if (newGame.Features == null)
                newGame.Features = new HashSet<MetadataProperty>();

            bool featureExists = newGame.Features
                .OfType<MetadataSpecProperty>()
                .Any(f => f.Id == installReadyFeature.Id.ToString());

            if (!featureExists)
            {
                newGame.Features.Add(new MetadataSpecProperty(installReadyFeature.Id.ToString()));
            }
        }

        // Own Steam Games Feature:
        // ---- [Own: Steam] Feature Support ----

        // For existingGame (Game)
        private void AddOwnSteamFeature(Game existingGame)
        {
            if (existingGame == null)
                return;

            if (existingGame.PluginId != Id ||
                existingGame.Platforms == null ||
                !existingGame.Platforms.Any(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)))
                return;

            var ownSteamFeature = EnsureFeatureExists("[Own: Steam]");
            if (ownSteamFeature == null || ownSteamFeature.Id == Guid.Empty)
                return;

            if (existingGame.FeatureIds == null)
                existingGame.FeatureIds = new List<Guid>();

            if (!existingGame.FeatureIds.Contains(ownSteamFeature.Id))
            {
                existingGame.FeatureIds.Add(ownSteamFeature.Id);
                PlayniteApi.Database.Games.Update(existingGame);
            }
        }

        // For newGame (GameMetadata)
        private void AddOwnSteamFeature(GameMetadata newGame)
        {
            if (newGame == null)
                return;

            if (newGame.Platforms == null ||
                !newGame.Platforms.OfType<MetadataSpecProperty>()
                    .Any(p => p.Id.Equals("pc_windows", StringComparison.OrdinalIgnoreCase)))
                return;

            var ownSteamFeature = EnsureFeatureExists("[Own: Steam]");
            if (ownSteamFeature == null || ownSteamFeature.Id == Guid.Empty)
                return;

            if (newGame.Features == null)
                newGame.Features = new HashSet<MetadataProperty>();

            bool featureExists = newGame.Features
                .OfType<MetadataSpecProperty>()
                .Any(f => f.Id == ownSteamFeature.Id.ToString());

            if (!featureExists)
            {
                newGame.Features.Add(new MetadataSpecProperty(ownSteamFeature.Id.ToString()));
            }
        }


        private string ConvertHyphenToColon(string name)



        {
            var parts = name.Split(new[] { " - " }, 2, StringSplitOptions.None);
            if (parts.Length > 1)
            {
                return parts[0] + ": " + parts[1];
            }
            return name;
        }
        private (MetadataFile icon, MetadataFile background) Myrient_getFiles(string gameName)
        {
            string Myrient_sanitizedPath = Myrient_SanitizePath(gameName);
            return (
                new MetadataFile(Path.Combine(Myrient_sanitizedPath, "icon.png")),
                new MetadataFile(Path.Combine(Myrient_sanitizedPath, "background.png"))
            );
        }

        // SteamRip
        private async Task<List<GameMetadata>> ScrapeSite()
        {
            const string downloadActionName = "Download: SteamRip";
            string dataFolder = GetPluginUserDataPath();
            string txtPath = Path.Combine(dataFolder, "SteamRip.Games.txt");
            string allGamesUrl = steamripBaseUrl; // Use this on first run
            string recentGamesUrl = "https://steamrip.com/"; // Use this on subsequent runs

            // Load known games from TXT (normalized names)
            var knownGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(txtPath))
            {
                foreach (var line in File.ReadAllLines(txtPath))
                {
                    var match = Regex.Match(line, @"Name: ""(.+?)"", Url: ""(.+?)"", Version: ""(.*?)""");
                    if (match.Success)
                        knownGames.Add(NormalizeGameName(match.Groups[1].Value));
                }
            }

            // Build lookup for existing DB games (PC platform)
            var dbGames = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id
                    && g.Platforms != null
                    && g.Platforms.Any(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var dbGameLookup = dbGames
                .GroupBy(g => NormalizeGameName(CleanGameName(g.Name)), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var scrapedGames = new ConcurrentDictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);
            var gamesToAdd = new List<(string cleanName, string href, string version)>();

            // First run: scrape all games
            if (!File.Exists(txtPath))
            {
                logger.Info("[SteamRip] First run, scraping all games...");
                string allGamesHtml = await LoadPageContent(allGamesUrl);
                var links = ParseLinks(allGamesHtml); // List<Tuple<string, string>>
                foreach (var link in links)
                {
                    string href = link.Item1;
                    string text = link.Item2;
                    if (href.StartsWith("/"))
                        href = "https://steamrip.com" + href;

                    // Exclude category URLs
                    if (href.StartsWith("https://steamrip.com/category/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string cleanName = CleanGameName(text);
                    string version = ExtractVersionNumber(text);
                    string norm = NormalizeGameName(cleanName);
                    if (!knownGames.Contains(norm))
                    {
                        gamesToAdd.Add((cleanName, href, version));
                        knownGames.Add(norm);
                    }
                }
            }
            else // Subsequent runs: scrape only recently added games
            {
                logger.Info("[SteamRip] Checking for new games (recently added)...");
                string recentHtml = await LoadPageContent(recentGamesUrl);
                var links = ParseLinks(recentHtml); // List<Tuple<string, string>>
                foreach (var link in links)
                {
                    string href = link.Item1;
                    string text = link.Item2;
                    if (href.StartsWith("/"))
                        href = "https://steamrip.com" + href;

                    // Exclude category URLs
                    if (href.StartsWith("https://steamrip.com/category/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string cleanName = CleanGameName(text);
                    string version = ExtractVersionNumber(text);
                    string norm = NormalizeGameName(cleanName);
                    if (!knownGames.Contains(norm))
                    {
                        gamesToAdd.Add((cleanName, href, version));
                        knownGames.Add(norm);
                    }
                }
            }

            // Write to TXT (always sorted by name)
            if (gamesToAdd.Count > 0)
            {
                List<string> allLines = File.Exists(txtPath) ? File.ReadAllLines(txtPath).ToList() : new List<string>();
                foreach (var g in gamesToAdd)
                    allLines.Add($"Name: \"{g.cleanName}\", Url: \"{g.href}\", Version: \"{g.version}\"");
                allLines = allLines
                    .Distinct()
                    .OrderBy(line =>
                    {
                        var m = Regex.Match(line, @"Name: ""(.+?)""");
                        return m.Success ? m.Groups[1].Value : line;
                    }, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                File.WriteAllLines(txtPath, allLines);
                logger.Info($"[SteamRip] TXT updated and sorted. Games in file: {allLines.Count}");
            }

            // Convert gamesToAdd to Playnite GameMetadata and update existing games as before
            var mergedScrapedGames = gamesToAdd.Select(g =>
                new GameMetadata
                {
                    Name = g.cleanName,
                    GameId = NormalizeGameName(g.cleanName).ToLower(),
                    Platforms = new HashSet<MetadataProperty>
                    {
                new MetadataSpecProperty("PC (Windows)")
                    },
                    GameActions = new List<GameAction>
                    {
                new GameAction
                {
                    Name = downloadActionName,
                    Type = GameActionType.URL,
                    Path = g.href,
                    IsPlayAction = false
                }
                    },
                    Version = g.version,
                    IsInstalled = false
                }).ToList();

            foreach (var scrapedGame in mergedScrapedGames)
            {
                var normalizedKey = NormalizeGameName(CleanGameName(scrapedGame.Name));
                if (dbGameLookup.TryGetValue(normalizedKey, out var existingGame))
                {
                    if (existingGame.GameActions == null)
                        existingGame.GameActions = new ObservableCollection<GameAction>();

                    foreach (var action in scrapedGame.GameActions)
                    {
                        bool actionExists = existingGame.GameActions.Any(a =>
                            a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase) &&
                            a.Path.Equals(action.Path, StringComparison.OrdinalIgnoreCase));

                        if (!actionExists)
                        {
                            existingGame.GameActions.Add(new GameAction
                            {
                                Name = downloadActionName,
                                Type = GameActionType.URL,
                                Path = action.Path,
                                IsPlayAction = false
                            });
                            PlayniteApi.Database.Games.Update(existingGame);
                            logger.Info($"Added SteamRip action to existing game: {scrapedGame.Name}");
                        }
                    }
                }
                else
                {
                    scrapedGames.TryAdd(scrapedGame.GameId, scrapedGame);
                }
            }

            logger.Info($"[SteamRip] Completed. New games added: {scrapedGames.Count}");
            return scrapedGames.Values.ToList();
        }
        // Fitgirl

        private List<Tuple<string, string>> FitGirlExtractGameLinks(string pageContent)
        {
            var links = new List<Tuple<string, string>>();

            Regex regex = new Regex(
                @"<h[23]\s+class=[""']entry-title[""']>\s*<a\s+href=[""'](?<url>https:\/\/fitgirl-repacks\.site\/[^""']+)[""'][^>]*>(?<title>.*?)<\/a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var matches = regex.Matches(pageContent);
            foreach (Match match in matches)
            {
                string url = match.Groups["url"].Value;
                string title = match.Groups["title"].Value;

                if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(title))
                {
                    title = WebUtility.HtmlDecode(title).Trim();
                    links.Add(new Tuple<string, string>(url, title));
                }
            }
            return links;
        }

        private async Task<List<GameMetadata>> FitGirlScrapeGames()
        {
            const string downloadActionName = "Download: FitGirl Repacks";
            // Get all PC (Windows) games in the DB
            var dbGames = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id
                    && g.Platforms != null
                    && g.Platforms.Any(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // FIX: Use GroupBy to avoid duplicate key exception!
            var dbGameLookup = dbGames
                .GroupBy(g => NormalizeGameName(g.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Use a concurrent dictionary for new (scraped) games.
            var scrapedGames = new ConcurrentDictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

            int latestPage = await GetLatestPageNumber().ConfigureAwait(false);
            logger.Info($"Latest FitGirl page: {latestPage}");

            var tasks = new List<Task>();
            for (int page = 1; page <= latestPage; page++)
            {
                int currentPage = page;
                tasks.Add(Task.Run(async () =>
                {
                    string url = $"{fitgirlBaseUrl}{currentPage}#lcp_instance_0";
                    string pageContent = await LoadPageContent(url).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(pageContent))
                    {
                        logger.Warn($"No content returned for page {currentPage}, skipping.");
                        return;
                    }

                    var links = ParseLinks(pageContent);
                    if (links == null || links.Count == 0)
                    {
                        logger.Info($"No game links found on page {currentPage}, skipping.");
                        return;
                    }

                    Parallel.ForEach(links, link =>
                    {
                        string href = link.Item1;
                        string text = link.Item2;

                        if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(text) || !IsValidGameLink(href))
                            return;
                        if (href.Contains("page0="))
                            return;

                        string cleanName = CleanGameName(text);
                        if (string.IsNullOrEmpty(cleanName))
                            return;

                        string normalizedName = NormalizeGameName(cleanName);

                        // If game exists in Playnite, add the action if missing (don't remove others)
                        if (dbGameLookup.TryGetValue(normalizedName, out var existingGame))
                        {
                            // Ensure GameActions is not null
                            if (existingGame.GameActions == null)
                                existingGame.GameActions = new ObservableCollection<GameAction>();

                            // Only add if this exact action/path doesn't exist
                            bool actionExists = existingGame.GameActions.Any(a =>
                                a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase) &&
                                a.Path.Equals(href, StringComparison.OrdinalIgnoreCase));

                            if (!actionExists)
                            {
                                existingGame.GameActions.Add(new GameAction
                                {
                                    Name = downloadActionName,
                                    Type = GameActionType.URL,
                                    Path = href,
                                    IsPlayAction = false
                                });
                                PlayniteApi.Database.Games.Update(existingGame);
                                logger.Info($"Added FitGirl action to existing game: {cleanName}");
                            }
                            return; // Don't add to scrapedGames, already in Playnite
                        }

                        // Otherwise, add as new scraped game with the action
                        scrapedGames.AddOrUpdate(
                            normalizedName,
                            key =>
                            {
                                var gameMetadata = new GameMetadata
                                {
                                    Name = cleanName,
                                    GameId = normalizedName.ToLower(),
                                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                                    GameActions = new List<GameAction>
                                    {
                                new GameAction
                                {
                                    Name = downloadActionName,
                                    Type = GameActionType.URL,
                                    Path = href,
                                    IsPlayAction = false
                                }
                                    },
                                    IsInstalled = false
                                };
                                return gameMetadata;
                            },
                            (key, existingGameMeta) =>
                            {
                                // Add the action if not present for this URL (don't remove others)
                                if (!existingGameMeta.GameActions.Any(a =>
                                    a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase) &&
                                    a.Path.Equals(href, StringComparison.OrdinalIgnoreCase)))
                                {
                                    existingGameMeta.GameActions.Add(new GameAction
                                    {
                                        Name = downloadActionName,
                                        Type = GameActionType.URL,
                                        Path = href,
                                        IsPlayAction = false
                                    });
                                }
                                return existingGameMeta;
                            });
                    });
                }));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            logger.Info($"FitGirl scraping completed. Total new games added: {scrapedGames.Count}");
            return scrapedGames.Values.ToList();
        }

        // Utility: decode HTML entities if needed
        private string HtmlDecode(string value)
        {
            return System.Net.WebUtility.HtmlDecode(value);
        }
       
        private bool FitGirlIsDuplicate(string gameName)
        {
            return PlayniteApi.Database.Games.Any(existing =>
                existing.PluginId == Id &&
                existing.Platforms != null &&
                existing.Platforms.Any(p =>
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "PC (Windows)", StringComparison.OrdinalIgnoreCase))
                    || p.ToString().Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)
                ) &&
                existing.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        }


        // Anker Games
        private async Task<List<GameMetadata>> AnkerScrapeGames()
        {
            const string downloadActionName = "Download: AnkerGames";
            string dataFolder = GetPluginUserDataPath();
            string txtPath = Path.Combine(dataFolder, "Anker Games.txt");
            string pythonScript = Path.Combine(dataFolder, "Scrape_AnkerGames.py");

            // Build lookup for existing DB games using cleaned+normalized names for dedupe
            var dbGames = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id)
                .GroupBy(g => NormalizeGameName(CleanGameName(g.Name)), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var scrapedGames = new ConcurrentDictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

            // Run Python script to update the TXT file
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{pythonScript}\"",
                    WorkingDirectory = dataFolder,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    logger.Info($"Python script output:\n{output}");
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        logger.Warn($"Python script error:\n{error}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to run Python script: {ex.Message}");
                return scrapedGames.Values.ToList();
            }

            // Parse TXT file and update/add games/actions as needed
            if (!File.Exists(txtPath))
            {
                logger.Warn($"TXT file not found: {txtPath}");
                return scrapedGames.Values.ToList();
            }

            foreach (var line in File.ReadAllLines(txtPath))
            {
                // Match: Name: "X", Url: "Y"
                var match = Regex.Match(line, @"Name: ""(.+?)"", Url: ""(.+?)""");
                if (!match.Success)
                    continue;

                var rawGameName = match.Groups[1].Value.Trim();
                var url = match.Groups[2].Value.Trim();

                // Clean before normalizing for both display and dedupe
                var cleanGameName = CleanGameName(rawGameName);
                var normalizedKey = NormalizeGameName(cleanGameName);

                // Log for debugging dedupe issues (optional)
                logger.Debug($"[ANKER] RAW='{rawGameName}', CLEANED='{cleanGameName}', KEY='{normalizedKey}', URL='{url}'");

                // Check if the game already exists in the DB (by normalized/cleaned name)
                if (dbGames.TryGetValue(normalizedKey, out var dbGame))
                {
                    // FIX: Ensure GameActions is not null before locking (use ObservableCollection, not List)
                    if (dbGame.GameActions == null)
                        dbGame.GameActions = new ObservableCollection<GameAction>();

                    lock (dbGame.GameActions)
                    {
                        if (!dbGame.GameActions.Any(a =>
                            a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase) &&
                            a.Path.Equals(url, StringComparison.OrdinalIgnoreCase) &&
                            a.Type == GameActionType.URL))
                        {
                            dbGame.GameActions.Add(new GameAction
                            {
                                Name = downloadActionName,
                                Type = GameActionType.URL,
                                Path = url,
                                IsPlayAction = false
                            });
                            PlayniteApi.Database.Games.Update(dbGame);
                            logger.Info($"Added AnkerGames download action to DB game: {cleanGameName}");
                        }
                    }
                    continue;
                }
                // Aggregate new scraped games by normalized name (dedupe within Anker scrape)
                scrapedGames.AddOrUpdate(
                    normalizedKey,
                    key =>
                    {
                        string sanitizedGameName = AnkerSanitizePath(cleanGameName);
                        var game = new GameMetadata
                        {
                            Name = cleanGameName,
                            GameId = key.ToLowerInvariant(),
                            Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                            GameActions = new List<GameAction>
                            {
                        new GameAction
                        {
                            Name = downloadActionName,
                            Type = GameActionType.URL,
                            Path = url,
                            IsPlayAction = false
                        }
                            },
                            IsInstalled = false,
                            InstallDirectory = null,
                            Icon = new MetadataFile(Path.Combine(sanitizedGameName, "icon.png")),
                            BackgroundImage = new MetadataFile(Path.Combine(sanitizedGameName, "background.png"))
                        };
                        logger.Info($"Added new AnkerGames game entry: {cleanGameName}");
                        return game;
                    },
                    (key, existingGame) =>
                    {
                        // FIX: Ensure GameActions is not null before locking
                        if (existingGame.GameActions == null)
                            existingGame.GameActions = new List<GameAction>();

                        lock (existingGame.GameActions)
                        {
                            if (!existingGame.GameActions.Any(a =>
                                a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase) &&
                                a.Path.Equals(url, StringComparison.OrdinalIgnoreCase) &&
                                a.Type == GameActionType.URL))
                            {
                                existingGame.GameActions.Add(new GameAction
                                {
                                    Name = downloadActionName,
                                    Type = GameActionType.URL,
                                    Path = url,
                                    IsPlayAction = false
                                });
                                logger.Info($"Added AnkerGames download action to duplicate scraped game: {cleanGameName}");
                            }
                        }
                        return existingGame;
                    }
                );
            }

            logger.Info($"AnkerGames import finished. New games added: {scrapedGames.Count}");
            return scrapedGames.Values.ToList();
        }


        private async Task<string> AnkerLoadMoreContent()
            {
                string loadMoreUrl = $"{ankerBaseUrl}?loadmore=true";
                return await AnkerLoadPageContent(loadMoreUrl).ConfigureAwait(false);
            }

            private List<string> AnkerExtractGameLinks(string pageContent)
            {
                var links = new List<string>();
                var matches = Regex.Matches(pageContent, @"href=[""'](https:\/\/ankergames\.net\/game\/[a-zA-Z0-9\-]+)[""']");
                foreach (Match match in matches)
                {
                    string href = match.Groups[1].Value;
                    if (!links.Contains(href))
                    {
                        links.Add(href);
                    }
                }
                return links;
            }

            private async Task<string> AnkerLoadPageContent(string url)
            {
                using (var httpClient = new HttpClient())
                {
                    try
                    {
                        return await httpClient.GetStringAsync(url);
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error loading content from {url}: {ex.Message}");
                        return string.Empty;
                    }
                }
            }

            private bool AnkerIsDuplicate(string gameName)
            {
                return PlayniteApi.Database.Games.Any(existing =>
                    existing.PluginId == Id &&
                    existing.Platforms != null &&
                    existing.Platforms.Any(p =>
                        (p.GetType().GetProperty("Name") != null &&
                         string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "PC (Windows)", StringComparison.OrdinalIgnoreCase))
                        || p.ToString().Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)
                    ) &&
                    existing.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
            }

            private string AnkerExtractGameNameFromPage(string pageContent)
            {
                var match = Regex.Match(pageContent,
                    @"<h3 class=""text-xl tracking-tighter font-semibold text-gray-900 dark:text-gray-100 line-clamp-1"">\s*(.+?)\s*</h3>");

                if (match.Success)
                {
                    string rawGameName = match.Groups[1].Value.Trim();
                    return WebUtility.HtmlDecode(rawGameName);
                }

                return string.Empty;
            }

            private string AnkerSanitizePath(string path)
            {
                return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
            }

        // MagiPack
        private async Task<List<GameMetadata>> MagipackScrapeGames()
        {
            const string downloadActionName = "Download: Magipack";
            string gamesTxtPath = Path.Combine(GetPluginUserDataPath(), "magipack_games.txt");

            // Load known games from .txt (normalized keys)
            HashSet<string> knownGameKeys = File.Exists(gamesTxtPath)
                ? File.ReadAllLines(gamesTxtPath)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Only DB games with PC (Windows) platform AND Download: Magipack action
            var dbGameKeys = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.Platforms.Any(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase))
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .Select(g => NormalizeGameName(g.Name)),
                StringComparer.OrdinalIgnoreCase);

            var scrapedGames = new List<GameMetadata>();
            var scrapedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool stopOnTxtMatch = knownGameKeys.Count > 0;

            string scrapeUrl = knownGameKeys.Count == 0 ? magipackBaseUrl : "https://www.magipack.games/";

            logger.Info($"Scraping games from: {scrapeUrl}");

            // Fetch the main page content.
            string pageContent = await LoadPageContent(scrapeUrl).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pageContent))
            {
                logger.Warn("Failed to retrieve main page content from Magipack.");
                return scrapedGames;
            }
            logger.Info("Main page content retrieved successfully.");

            // Extract game links using your parsing method.
            var links = ParseLinks(pageContent);
            if (links == null || links.Count == 0)
            {
                logger.Info("No game links found on Magipack page.");
                return scrapedGames;
            }
            logger.Info($"Found {links.Count} potential game links.");

            foreach (var link in links)
            {
                string href = link.Item1;
                string text = link.Item2;

                // Skip if either href or text is missing or invalid.
                if (string.IsNullOrWhiteSpace(href) ||
                    string.IsNullOrWhiteSpace(text) ||
                    !IsValidGameLink(href))
                {
                    continue;
                }

                // Clean up the game title.
                string cleanName = CleanGameName(text);
                if (string.IsNullOrEmpty(cleanName))
                {
                    cleanName = fallbackRegex.Replace(href, "$1").Replace('-', ' ').Trim();
                }
                if (string.IsNullOrEmpty(cleanName))
                {
                    continue;
                }

                // Generate normalized key for duplicate checking.
                string normalizedKey = NormalizeGameName(cleanName);

                // On incremental run: stop at first match in .txt (do not add it or any after)
                if (stopOnTxtMatch && knownGameKeys.Contains(normalizedKey))
                {
                    logger.Info($"Found match in txt: {cleanName}, stopping scrape.");
                    break;
                }

                // O(1) skip if game already in DB for PC (Windows) with Download: Magipack action
                if (dbGameKeys.Contains(normalizedKey))
                    continue;

                // Avoid duplicates within this scrape session
                if (scrapedKeys.Contains(normalizedKey))
                    continue;

                var gameMetadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = normalizedKey.ToLower(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = downloadActionName,
                    Type = GameActionType.URL,
                    Path = href,
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

                scrapedGames.Add(gameMetadata);
                scrapedKeys.Add(normalizedKey);
            }

            // If this is the first run, save all scraped keys to .txt
            // On incremental, append new keys to .txt
            if (scrapedKeys.Count > 0)
            {
                foreach (var key in scrapedKeys)
                    knownGameKeys.Add(key);

                File.WriteAllLines(gamesTxtPath, knownGameKeys.OrderBy(x => x));
            }

            logger.Info($"Magipack scraping completed. New games added: {scrapedGames.Count}");
            return scrapedGames;
        }
        private async Task<string> MagipackLoadPageContent(string url)
        {
            using (var httpClient = new HttpClient())
            {
                try
                {
                    return await httpClient.GetStringAsync(url);
                }
                catch (Exception ex)
                {
                    logger.Error($"Error loading content from {url}: {ex.Message}");
                    return string.Empty;
                }
            }
        }
        private List<string> MagipackExtractGameLinks(string pageContent)
        {
            var links = new List<string>();
            var matches = Regex.Matches(pageContent, @"href=[""'](https:\/\/ankergames\.net\/game\/[a-zA-Z0-9\-]+)[""']");
            foreach (Match match in matches)
            {
                string href = match.Groups[1].Value;
                if (!links.Contains(href))
                {
                    links.Add(href);
                }
            }
            return links;
        }
        private string MagipackExtractGameNameFromPage(string pageContent)
        {
            var match = Regex.Match(pageContent,
                @"<h3 class=""text-xl tracking-tighter font-semibold text-gray-900 dark:text-gray-100 line-clamp-1"">\s*(.+?)\s*</h3>");

            if (match.Success)
            {
                string rawGameName = match.Groups[1].Value.Trim();
                return WebUtility.HtmlDecode(rawGameName); // Use WebUtility for decoding
            }

            return string.Empty;
        }
        private string MagipackSanitizePath(string path)
        {
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }
        private bool MagipackIsDuplicate(string gameName)
        {
            return PlayniteApi.Database.Games.Any(existing =>
                existing.PluginId == Id &&
                existing.Platforms != null &&
                existing.Platforms.Any(p =>
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "PC (Windows)", StringComparison.OrdinalIgnoreCase))
                    || p.ToString().Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)
                ) &&
                existing.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        }

        // ElAmigos
        // ElAmigos
        private async Task<List<GameMetadata>> ElamigosScrapeGames()
        {
            const string downloadActionName = "Download: elAmigos";

            // Consistent dedupe: Use CleanGameName + NormalizeGameName for DB lookup keys
            var dbGameKeys = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.Platforms.Any(p =>
                            (p.GetType().GetProperty("Name") != null &&
                             ((string)p.GetType().GetProperty("Name").GetValue(p)).Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase))
                            || p.ToString().Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase))
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .Select(g => NormalizeGameName(CleanGameName(g.Name))),
                StringComparer.OrdinalIgnoreCase);

            var scrapedGames = new ConcurrentDictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

            logger.Info($"Scraping games from: {ElamigosBaseUrl}");

            // Fetch main page content.
            string pageContent = await ElamigosLoadPageContent(ElamigosBaseUrl);
            if (string.IsNullOrWhiteSpace(pageContent))
            {
                logger.Warn("Failed to retrieve main page content from ElAmigos.");
                return scrapedGames.Values.ToList();
            }
            logger.Info("Main page content retrieved successfully.");

            // Pre-compile regular expressions.
            var entryRegex = new Regex(
                @"<h3>(.*?)<a\s+href=""(.*?)"">DOWNLOAD</a></h3>",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var removeElamigosRegex = new Regex(
                @"\s*ElAmigos\s*",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var extraneousInfoRegex = new Regex(
                @"^\d+(\.\d+)?\s*(gb|mb)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            // Extract game entries from <h3> tags.
            var matches = entryRegex.Matches(pageContent);
            if (matches.Count == 0)
            {
                logger.Info("No game entries found on ElAmigos page.");
                return scrapedGames.Values.ToList();
            }
            logger.Info($"Found {matches.Count} potential game entries.");

            // Process each match in parallel.
            Parallel.ForEach(matches.Cast<Match>(), match =>
            {
                string rawName = match.Groups[1].Value.Trim();
                string href = match.Groups[2].Value.Trim();

                // If href is relative, prepend the base URL.
                if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    href = ElamigosBaseUrl.TrimEnd('/') + "/" + href.TrimStart('/');
                }

                // Remove "ElAmigos" from the raw title.
                rawName = removeElamigosRegex.Replace(rawName, "").Trim();

                // --- UNIFIED CLEANING: Use the same logic as other scrapers ---
                string cleanName = CleanGameName(rawName);

                // Validate the cleaned title and download link.
                if (string.IsNullOrWhiteSpace(cleanName) || !IsValidGameLink(href))
                    return;

                string displayName = cleanName;
                string normalizedName = NormalizeGameName(cleanName);

                // For debug: log the key and name
                logger.Debug($"[ELAMIGOS] RAW='{rawName}', CLEANED='{cleanName}', KEY='{normalizedName}', URL='{href}'");

                // Skip if game already in DB for PC (Windows) with Download: elAmigos action
                if (dbGameKeys.Contains(normalizedName))
                    return;

                // Add or update in concurrent dictionary
                scrapedGames.AddOrUpdate(
                    normalizedName,
                    key => new GameMetadata
                    {
                        Name = displayName,
                        GameId = key.ToLowerInvariant(),
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                        GameActions = new List<GameAction>
                        {
                    new GameAction
                    {
                        Name = downloadActionName,
                        Type = GameActionType.URL,
                        Path = href,
                        IsPlayAction = false
                    }
                        },
                        IsInstalled = false
                    },
                    (key, existingGame) =>
                    {
                        if (!existingGame.GameActions.Any(a =>
                                a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase) &&
                                a.Path.Equals(href, StringComparison.OrdinalIgnoreCase)))
                        {
                            lock (existingGame.GameActions)
                            {
                                existingGame.GameActions.Add(new GameAction
                                {
                                    Name = downloadActionName,
                                    Type = GameActionType.URL,
                                    Path = href,
                                    IsPlayAction = false
                                });
                                logger.Info($"Added download action to duplicate scraped game: {displayName}");
                            }
                        }
                        return existingGame;
                    }
                );
            });

            logger.Info($"ElAmigos scraping completed. New games added: {scrapedGames.Count}");
            return scrapedGames.Values.ToList();
        }

        private async Task<string> ElamigosLoadPageContent(string url)
        {
            using (var httpClient = new HttpClient())
            {
                try
                {
                    return await httpClient.GetStringAsync(url);
                }
                catch (Exception ex)
                {
                    logger.Error($"Error loading content from {url}: {ex.Message}");
                    return string.Empty;
                }
            }
        }

        private List<string> ElamigosExtractGameLinks(string ElamigospageContent)
        {
            var links = new List<string>();
            var matches = Regex.Matches(ElamigospageContent, @"href=[""'](https:\/\/ankergames\.net\/game\/[a-zA-Z0-9\-]+)[""']");
            foreach (Match match in matches)
            {
                string href = match.Groups[1].Value;
                if (!links.Contains(href))
                {
                    links.Add(href);
                }
            }
            return links;
        }

        private string ElamigosExtractGameNameFromPage(string pageContent)
        {
            var match = Regex.Match(pageContent,
                @"<h3 class=""text-xl tracking-tighter font-semibold text-gray-900 dark:text-gray-100 line-clamp-1"">\s*(.+?)\s*</h3>");

            if (match.Success)
            {
                string rawGameName = match.Groups[1].Value.Trim();
                return WebUtility.HtmlDecode(rawGameName);
            }

            return string.Empty;
        }

        private bool ElamigosIsDuplicate(string gameName)
        {
            return PlayniteApi.Database.Games.Any(existing =>
                existing.PluginId == Id &&
                existing.Platforms != null &&
                existing.Platforms.Any(p =>
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "PC (Windows)", StringComparison.OrdinalIgnoreCase))
                    || p.ToString().Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)
                ) &&
                existing.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        }

        private string ElamigosSanitizePath(string path)
        {
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }

        // Myrient 
        private List<Tuple<string, string>> Myrient_ParseLinks(string pageContent)
        {
            var links = new List<Tuple<string, string>>();

            try
            {
                var matches = Regex.Matches(pageContent, @"<a\s+href=[""']([^""']+\.zip)[""'].*?>(.*?)</a>");
                foreach (Match match in matches)
                {
                    string href = match.Groups[1].Value;
                    string text = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value);
                    links.Add(new Tuple<string, string>(href, text));
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error parsing links: {ex.Message}");
            }

            return links;
        }
        private string Myrient_CleanGameName(string name)
        {
            // Remove unwanted characters and trim whitespace
            var cleanName = name.Trim();

            // Remove file extension (.zip), as PS2 games are stored in ZIP format
            cleanName = Regex.Replace(cleanName, @"\.zip$", "", RegexOptions.IgnoreCase);

            // Remove any region tags (e.g., "(Europe)", "(USA)", "(Japan)")
            cleanName = Regex.Replace(cleanName, @"\s*\(.*?\)$", "", RegexOptions.IgnoreCase);

            return cleanName;
        }
        private string Myrient_NormalizeGameName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // 1. Lowercase and trim.
            string normalized = name.ToLowerInvariant().Trim();

            // 1.1 Remove periods if they occur between word characters.
            normalized = Regex.Replace(normalized, @"(?<=\w)\.(?=\w)", "");

            // 2. Remove apostrophes (both straight and smart).
            normalized = normalized.Replace("’", "").Replace("'", "");

            // 3. Replace ampersands and plus signs with " and ".
            normalized = normalized.Replace("&", " and ").Replace("+", " and ");

            // 4. Remove unwanted punctuation.
            normalized = Regex.Replace(normalized, @"[^\w\s]", " ");

            // 5. Collapse multiple spaces.
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            // 6. Special rule for Marvel: if it starts with "marvels", change it to "marvel".
            normalized = Regex.Replace(normalized, @"^marvels\b", "marvel", RegexOptions.IgnoreCase);

            // 7. Normalize "Game of The Year" variants:
            normalized = Regex.Replace(normalized, @"\bgame of (the year)( edition)?\b", "goty", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bgoty( edition)?\b", "goty", RegexOptions.IgnoreCase);

            // 8. Normalize "Grand Theft Auto" to "gta".
            normalized = Regex.Replace(normalized, @"\bgrand theft auto\b", "gta", RegexOptions.IgnoreCase);

            // 9. Normalize Spider-man variants.
            normalized = Regex.Replace(normalized, @"\bspider[\s\-]+man\b", "spiderman", RegexOptions.IgnoreCase);

            // 10. Normalize the acronym REPO.
            normalized = Regex.Replace(normalized, @"\br\.?e\.?p\.?o\b", "repo", RegexOptions.IgnoreCase);

            // 11. Normalize the acronym FEAR.
            normalized = Regex.Replace(normalized, @"\bf\s*e\s*a\s*r\b", "fear", RegexOptions.IgnoreCase);

            // 12. Normalize Rick and Morty Virtual Rick-ality VR variants.
            normalized = Regex.Replace(normalized,
                @"rick and morty\s*[:]?\s*virtual rickality vr",
                "rick and morty virtual rickality vr", RegexOptions.IgnoreCase);

            // 13. Normalize common Roman numerals.
            normalized = Regex.Replace(normalized, @"\bviii\b", "8", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bvii\b", "7", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\biv\b", "4", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\biii\b", "3", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bvi\b", "6", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bix\b", "9", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bx\b", "10", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bv\b", "5", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bii\b", "2", RegexOptions.IgnoreCase);

            return normalized.Trim();
        }
        private string Myrient_SanitizePath(string path)
        {
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }
        private bool Myrient_IsDuplicate(string gameName)
        {
            return PlayniteApi.Database.Games.Any(existing =>
                existing.PluginId == Id &&
                existing.Platforms != null &&
                existing.Platforms.Any(p => p.Name.Equals("Sony PlayStation", StringComparison.OrdinalIgnoreCase)) &&
                existing.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        }
        private List<string> Myrient_FindGameRoms(string gameName)
        {
            var romPaths = new List<string>();
            var searchExtensions = new[] { ".iso", ".chd", ".bin", ".cue", ".img", ".nrg", ".mdf", ".gz" }; // Full range of PCSX2-supported formats
            var searchDirectory = "Roms\\Sony - PlayStation 2";

            foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                    if (System.IO.Directory.Exists(rootPath))
                    {
                        var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                            .Where(file => searchExtensions.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));

                        romPaths.AddRange(files);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Error searching ROMs in drive {drive.Name}: {ex.Message}");
                }
            }

            return romPaths;
        }
        private async Task<string> Myrient_LoadPageContent(string url)
        {
            using (var httpClient = new HttpClient())
            {
                try
                {
                    return await httpClient.GetStringAsync(url);
                }
                catch (Exception ex)
                {
                    logger.Error($"Error loading content from {url}: {ex.Message}");
                    return string.Empty;
                }
            }
        }
        private string Myrient_CleanNameForMatching(string name)
        {
            try
            {
                // Normalize colons and dashes for flexible matching
                name = name.Replace(":", " ").Replace("-", " ");

                // Remove text inside square brackets (e.g., [0100FB2021B0E000][v0][US])
                name = Regex.Replace(name, @"

\[[^\]

]*\]

", "");

                // Remove text inside parentheses, including region and language info
                name = Regex.Replace(name, @"\s*\([^)]*\)", "");

                // Decode HTML entities and trim whitespace
                name = System.Net.WebUtility.HtmlDecode(name).Trim();

                // Remove file extensions like .zip, .chd, .iso, .bin, .cue, .img, .nrg, .mdf, .gz
                name = Regex.Replace(name, @"\.(zip|chd|iso|bin|cue|img|nrg|mdf|gz)$", "", RegexOptions.IgnoreCase);

                // Normalize consecutive spaces
                name = Regex.Replace(name, @"\s+", " ");

                return name;
            }
            catch (Exception ex)
            {
                logger.Error($"Error cleaning PS2 game name for matching: {name}. Error: {ex.Message}");
                return name; // Return the original name if cleaning fails
            }
        }

        // Dodi Repacks
        private async Task<List<GameMetadata>> DodiRepacksScrapeGames()
        {
            const string downloadActionName = "Download: Dodi";
            string dataFolder = GetPluginUserDataPath();
            string txtPath = Path.Combine(dataFolder, "Dodi.txt");
            string pythonScript = Path.Combine(dataFolder, "Dodi Games.py");

            // Use normalized + cleaned names for DB lookup
            var dbGames = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id)
                .GroupBy(g => NormalizeGameName(CleanGameName(g.Name)), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var scrapedGames = new ConcurrentDictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

            // 1. Update TXT file using Python script
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{pythonScript}\"",
                    WorkingDirectory = dataFolder,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    logger.Info($"Python script output:\n{output}");
                    if (!string.IsNullOrWhiteSpace(error))
                        logger.Warn($"Python script error:\n{error}");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to run Python script: {ex.Message}");
                return scrapedGames.Values.ToList();
            }

            // 2. Parse TXT file and update/add games/actions as needed
            if (!File.Exists(txtPath))
            {
                logger.Warn($"TXT file not found: {txtPath}");
                return scrapedGames.Values.ToList();
            }

            foreach (var line in File.ReadAllLines(txtPath))
            {
                // Match lines like: Name: "X" url: "Y"  or  Name: "X", Url: "Y"
                var match = Regex.Match(line, @"Name: ""(.+)""[, ]+url: ""(.+)""", RegexOptions.IgnoreCase);
                if (!match.Success)
                    continue;

                var rawGameName = match.Groups[1].Value.Trim();
                var url = match.Groups[2].Value.Trim();

                // Clean and normalize the game name
                var cleanGameName = CleanGameName(rawGameName);
                var normalizedKey = NormalizeGameName(cleanGameName);

                // If game exists in DB, add the download action if missing
                if (dbGames.TryGetValue(normalizedKey, out var dbGame))
                {
                    lock (dbGame.GameActions)
                    {
                        bool alreadyHas = dbGame.GameActions.Any(a =>
                            a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase) &&
                            a.Path.Equals(url, StringComparison.OrdinalIgnoreCase) &&
                            a.Type == GameActionType.URL);

                        if (!alreadyHas)
                        {
                            dbGame.GameActions.Add(new GameAction
                            {
                                Name = downloadActionName,
                                Type = GameActionType.URL,
                                Path = url,
                                IsPlayAction = false
                            });
                            PlayniteApi.Database.Games.Update(dbGame);
                            logger.Info($"Added Dodi download action to DB game: {cleanGameName}");
                        }
                    }
                    continue;
                }

                // Otherwise, aggregate new scraped games by normalized name
                scrapedGames.AddOrUpdate(
                    normalizedKey,
                    key =>
                    {
                        string sanitizedGameName = DodiSanitizePath(cleanGameName);
                        var game = new GameMetadata
                        {
                            Name = cleanGameName,
                            GameId = key.ToLower(),
                            Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                            GameActions = new List<GameAction>
                            {
                        new GameAction
                        {
                            Name = downloadActionName,
                            Type = GameActionType.URL,
                            Path = url,
                            IsPlayAction = false
                        }
                            },
                            IsInstalled = false,
                            InstallDirectory = null,
                            Icon = new MetadataFile(Path.Combine(sanitizedGameName, "icon.png")),
                            BackgroundImage = new MetadataFile(Path.Combine(sanitizedGameName, "background.png"))
                        };
                        logger.Info($"Added new Dodi game entry: {cleanGameName}");
                        return game;
                    },
                    (key, existingGame) =>
                    {
                        lock (existingGame.GameActions)
                        {
                            bool alreadyHas = existingGame.GameActions.Any(a =>
                                a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase) &&
                                a.Path.Equals(url, StringComparison.OrdinalIgnoreCase) &&
                                a.Type == GameActionType.URL);

                            if (!alreadyHas)
                            {
                                existingGame.GameActions.Add(new GameAction
                                {
                                    Name = downloadActionName,
                                    Type = GameActionType.URL,
                                    Path = url,
                                    IsPlayAction = false
                                });
                                logger.Info($"Added Dodi download action to duplicate scraped game: {cleanGameName}");
                            }
                        }
                        return existingGame;
                    }
                );
            }

            logger.Info($"DodiRepacks import finished. New games added: {scrapedGames.Count}");
            return scrapedGames.Values.ToList();
        }

        private string DodiSanitizePath(string path)
        {
            // Remove characters invalid in Windows paths.
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }

        private bool DodiRepacksIsDuplicate(string gameName)
        {
            return PlayniteApi.Database.Games.Any(existing =>
                existing.PluginId == Id &&
                existing.Platforms != null &&
                existing.Platforms.Any(p =>
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "PC (Windows)", StringComparison.OrdinalIgnoreCase))
                    || p.ToString().Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)
                ) &&
                existing.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        }




        // My Abandonware Repacks
        private async Task<List<GameMetadata>> MyAbandonScrapeGames()
        {
            const string downloadActionName = "Download: My.Abandon";
            string pluginFolder = GetPluginUserDataPath();
            string myAbandonwareFolder = Path.Combine(pluginFolder, "Other Sources", "My Abandonware");
            string txtPath = Path.Combine(myAbandonwareFolder, "AbandonWare.Games.txt");

            var dbGames = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id)
                .GroupBy(g => NormalizeGameName(CleanGameName(g.Name)), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var scrapedGames = new ConcurrentDictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(txtPath))
            {
                logger.Warn($"TXT file not found: {txtPath}");
                return scrapedGames.Values.ToList();
            }

            var regex = new Regex(@"Name: ""(.+)""[, ]+Url: ""(.+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            foreach (var line in File.ReadLines(txtPath))
            {
                var match = regex.Match(line);
                if (!match.Success)
                    continue;

                var rawGameName = match.Groups[1].Value.Trim();
                var url = match.Groups[2].Value.Trim();

                var cleanGameName = CleanGameName(rawGameName);
                var normalizedKey = NormalizeGameName(cleanGameName);

                if (dbGames.TryGetValue(normalizedKey, out var dbGame))
                {
                    lock (dbGame.GameActions)
                    {
                        bool alreadyHas = dbGame.GameActions.Any(a =>
                            a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase) &&
                            a.Path.Equals(url, StringComparison.OrdinalIgnoreCase) &&
                            a.Type == GameActionType.URL);

                        if (!alreadyHas)
                        {
                            dbGame.GameActions.Add(new GameAction
                            {
                                Name = downloadActionName,
                                Type = GameActionType.URL,
                                Path = url,
                                IsPlayAction = false
                            });
                            PlayniteApi.Database.Games.Update(dbGame);
                            logger.Info($"Added My Abandonware download action to DB game: {cleanGameName}");
                        }
                    }
                    continue;
                }

                scrapedGames.AddOrUpdate(
                    normalizedKey,
                    key =>
                    {
                        string sanitizedGameName = MyAbandonSanitizePath(cleanGameName);
                        var game = new GameMetadata
                        {
                            Name = cleanGameName,
                            GameId = key.ToLower(),
                            Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                            GameActions = new List<GameAction>
                            {
                        new GameAction
                        {
                            Name = downloadActionName,
                            Type = GameActionType.URL,
                            Path = url,
                            IsPlayAction = false
                        }
                            },
                            IsInstalled = false,
                            InstallDirectory = null,
                            Icon = new MetadataFile(Path.Combine(sanitizedGameName, "icon.png")),
                            BackgroundImage = new MetadataFile(Path.Combine(sanitizedGameName, "background.png"))
                        };
                        logger.Info($"Added new My Abandonware game entry: {cleanGameName}");
                        return game;
                    },
                    (key, existingGame) =>
                    {
                        lock (existingGame.GameActions)
                        {
                            bool alreadyHas = existingGame.GameActions.Any(a =>
                                a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase) &&
                                a.Path.Equals(url, StringComparison.OrdinalIgnoreCase) &&
                                a.Type == GameActionType.URL);

                            if (!alreadyHas)
                            {
                                existingGame.GameActions.Add(new GameAction
                                {
                                    Name = downloadActionName,
                                    Type = GameActionType.URL,
                                    Path = url,
                                    IsPlayAction = false
                                });
                                logger.Info($"Added My Abandonware download action to duplicate scraped game: {cleanGameName}");
                            }
                        }
                        return existingGame;
                    }
                );
            }

            logger.Info($"MyAbandonware import finished. New games added: {scrapedGames.Count}");
            return scrapedGames.Values.ToList();
        }        
        
        // Helper for path sanitization
        private string MyAbandonSanitizePath(string path)
        {
            // Remove characters invalid in Windows paths.
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }

       private bool MyAbandonIsDuplicate(string gameName)
        {
            return PlayniteApi.Database.Games.Any(existing =>
                existing.PluginId == Id &&
                existing.Platforms != null &&
                existing.Platforms.Any(p =>
                    (p.GetType().GetProperty("Name") != null &&
                        string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "PC (Windows)", StringComparison.OrdinalIgnoreCase))
                    || p.ToString().Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)
                ) &&
                existing.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        }

        // PS1
        private List<string> PS1_FindGameRoms(string gameName)
        {
            var romPaths = new List<string>();
            var searchExtensions = new[] { ".iso", ".chd", ".bin", ".cue", ".img", ".nrg", ".mdf", ".gz" }; // Full range of PCSX2-supported formats
            var searchDirectory = "Roms\\Sony - PlayStation";

            foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                    if (System.IO.Directory.Exists(rootPath))
                    {
                        var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                            .Where(file => searchExtensions.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));

                        romPaths.AddRange(files);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Error searching ROMs in drive {drive.Name}: {ex.Message}");
                }
            }

            return romPaths;
        }
        private async Task<List<GameMetadata>> Myrient_Sony_PS1_ScrapeStaticPage()
        {
            const string platformName = "Sony PlayStation";
            const string downloadActionName = "Download: Myrient";
            string dataFolder = GetPluginUserDataPath();
            string txtPath = Path.Combine(dataFolder, "PS1.Games.txt");

            // Build a hash set of normalized names for fast O(1) lookup of existing DB games per platform,
            // ONLY including games that already have a "Download: Myrient" action.
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

            // Only scrape the website if the TXT file does not exist
            if (!File.Exists(txtPath))
            {
                logger.Info($"[Sony_PS1_Games] Scraping games from: {Sony_PS1_Games_BaseUrl}");

                string pageContent = await Myrient_LoadPageContent(Sony_PS1_Games_BaseUrl).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(pageContent))
                {
                    logger.Warn("[Sony_PS1_Games] Failed to retrieve main page content.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Sony_PS1_Games] Page content retrieved successfully ({pageContent.Length} characters).");

                var links = Myrient_ParseLinks(pageContent)?
                    .Where(link => link.Item1.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (links == null || links.Length == 0)
                {
                    logger.Info("[Sony_PS1_Games] No valid game links found.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Sony_PS1_Games] Found {links.Length} PS1 game links.");

                // Write all found games to the TXT file
                using (var writer = new StreamWriter(txtPath, false))
                {
                    foreach (var link in links)
                    {
                        string text = link.Item2;
                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        string cleanName = Myrient_CleanGameName(text).Replace(".zip", "").Trim();
                        if (string.IsNullOrEmpty(cleanName))
                            cleanName = fallbackRegex.Replace(text, "$1").Replace('-', ' ').Trim();
                        if (string.IsNullOrEmpty(cleanName))
                            continue;

                        string url = link.Item1.Trim();
                        writer.WriteLine($"Name: \"{cleanName}\", Url: \"{url}\"");
                    }
                }
                logger.Info($"[Sony_PS1_Games] Wrote all games to TXT cache: {txtPath}");
            }
            else
            {
                logger.Info($"[Sony_PS1_Games] Skipping web scrape, using TXT cache: {txtPath}");
            }

            // Now always load from TXT file
            var results = new List<GameMetadata>();
            if (!File.Exists(txtPath))
            {
                logger.Warn($"[Sony_PS1_Games] TXT cache file missing: {txtPath}");
                return results;
            }

            var lines = File.ReadAllLines(txtPath);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                if (!match.Success)
                    continue;

                var cleanName = match.Groups[1].Value.Trim();
                string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                // O(1) skip if game already present with Download: Myrient action for this platform
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    continue;

                var metadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = uniqueKey.ToLower(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = downloadActionName,
                    Type = GameActionType.URL,
                    Path = Sony_PS1_Games_BaseUrl, // Always use the base folder/page URL
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

                results.Add(metadata);
            }

            logger.Info($"[Sony_PS1_Games] Import complete. New games added: {results.Count}");
            return results;
        }        


        // Wii 
        private List<string> FindWIIGameRoms(string gameName)
        {
            var romPaths = new List<string>();
            var searchExtensions = new[] { ".rvz", ".iso", ".wbfs" };
            var searchDirectory = "Roms\\Nintendo - WII\\Games";

            foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                    if (System.IO.Directory.Exists(rootPath))
                    {
                        var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                            .Where(file => searchExtensions.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));

                        romPaths.AddRange(files);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Error searching Wii ROMs in drive {drive.Name}: {ex.Message}");
                }
            }

            return romPaths;
        }
        private async Task<List<GameMetadata>> Myrient_Nintendo_WII_ScrapeStaticPage()
        {
            const string platformName = "Nintendo Wii";
            const string downloadActionName = "Download: Myrient";
            string dataFolder = GetPluginUserDataPath();
            string txtPath = Path.Combine(dataFolder, "WII.Games.txt");

            // Build a hash set of normalized names for O(1) lookup of existing DB games for this platform with "Download: Myrient" action.
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

            // Only scrape the website if the TXT file does not exist
            if (!File.Exists(txtPath))
            {
                logger.Info($"[Nintendo_WII_Games] Scraping games from: {Nintendo_WII_Games_BaseUrl}");

                string pageContent = await Myrient_LoadPageContent(Nintendo_WII_Games_BaseUrl).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(pageContent))
                {
                    logger.Warn("[Nintendo_WII_Games] Failed to retrieve main page content.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Nintendo_WII_Games] Page content retrieved successfully ({pageContent.Length} characters).");

                var links = Myrient_ParseLinks(pageContent)?
                    .Where(link => link.Item1.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (links == null || links.Length == 0)
                {
                    logger.Info("[Nintendo_WII_Games] No valid game links found.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Nintendo_WII_Games] Found {links.Length} Wii game links.");

                // Write all found games to the TXT file
                using (var writer = new StreamWriter(txtPath, false))
                {
                    foreach (var link in links)
                    {
                        string text = link.Item2;
                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        string cleanName = Myrient_CleanGameName(text).Replace(".zip", "").Trim();
                        if (string.IsNullOrEmpty(cleanName))
                            cleanName = fallbackRegex.Replace(text, "$1").Replace('-', ' ').Trim();
                        if (string.IsNullOrEmpty(cleanName))
                            continue;

                        string url = link.Item1.Trim();
                        writer.WriteLine($"Name: \"{cleanName}\", Url: \"{url}\"");
                    }
                }
                logger.Info($"[Nintendo_WII_Games] Wrote all games to TXT cache: {txtPath}");
            }
            else
            {
                logger.Info($"[Nintendo_WII_Games] Skipping web scrape, using TXT cache: {txtPath}");
            }

            // Always load from TXT file
            var results = new List<GameMetadata>();
            if (!File.Exists(txtPath))
            {
                logger.Warn($"[Nintendo_WII_Games] TXT cache file missing: {txtPath}");
                return results;
            }

            var lines = File.ReadAllLines(txtPath);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                if (!match.Success)
                    continue;

                var cleanName = match.Groups[1].Value.Trim();
                string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                // O(1) skip if game already present with Download: Myrient action for this platform
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    continue;

                var metadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = uniqueKey.ToLower(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = downloadActionName,
                    Type = GameActionType.URL,
                    Path = Nintendo_WII_Games_BaseUrl, // Always use the base folder/page URL
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

                results.Add(metadata);
            }

            logger.Info($"[Nintendo_WII_Games] Import complete. New games added: {results.Count}");
            return results;
        }        

        // Wii U 
        private List<string> FindWiiUGameRoms(string gameName)
        {
            var romPaths = new List<string>();
            var searchExtensions = new[] { ".wux", ".wud", ".iso", ".rpx", ".nsp" };
            var searchDirectory = "Roms\\Nintendo - Wii U\\Games";

            foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                    if (System.IO.Directory.Exists(rootPath))
                    {
                        var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                            .Where(file => searchExtensions.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));

                        romPaths.AddRange(files);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Error searching Wii U ROMs in drive {drive.Name}: {ex.Message}");
                }
            }

            return romPaths;
        }
        private async Task<List<GameMetadata>> Myrient_Nintendo_WiiU_ScrapeStaticPage()
        {
            const string platformName = "Nintendo Wii U";
            const string downloadActionName = "Download: Myrient";
            string dataFolder = GetPluginUserDataPath();
            string txtPath = Path.Combine(dataFolder, "WiiU.Games.txt");

            // O(1) lookup for games already in DB with this platform & download action
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

            // Only scrape the website if the TXT file does not exist
            if (!File.Exists(txtPath))
            {
                logger.Info($"[Nintendo_WiiU_Games] Scraping games from: {Nintendo_WiiU_Games_BaseUrl}");

                string pageContent = await Myrient_LoadPageContent(Nintendo_WiiU_Games_BaseUrl).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(pageContent))
                {
                    logger.Warn("[Nintendo_WiiU_Games] Failed to retrieve main page content.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Nintendo_WiiU_Games] Page content retrieved successfully ({pageContent.Length} characters).");

                // Extract all .wua links
                var wuaLinks = Regex.Matches(pageContent, "<a[^>]+href=[\"']([^\"']+\\.wua)[\"'][^>]*>(.*?)<\\/a>", RegexOptions.IgnoreCase)
                    .Cast<Match>()
                    .Select(m => new
                    {
                        Href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value),
                        Text = m.Groups[2].Value
                    })
                    .ToList();

                if (wuaLinks.Count == 0)
                {
                    logger.Info("[Nintendo_WiiU_Games] No valid .wua links found.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Nintendo_WiiU_Games] Found {wuaLinks.Count} Wii U .wua links.");

                using (var writer = new StreamWriter(txtPath, false))
                {
                    foreach (var link in wuaLinks)
                    {
                        string text = link.Text;
                        if (string.IsNullOrWhiteSpace(text))
                            text = Path.GetFileNameWithoutExtension(link.Href);

                        // Clean name: remove (Region), (vXX), any parenthesis, and .wua extension, then trim
                        string cleanName = Regex.Replace(text, @"\s*\(.*?\)", "");
                        cleanName = Regex.Replace(cleanName, @"\s*v\d+$", "", RegexOptions.IgnoreCase);
                        cleanName = Regex.Replace(cleanName, "\\.wua$", "", RegexOptions.IgnoreCase);
                        cleanName = cleanName.Trim();

                        if (string.IsNullOrEmpty(cleanName))
                            continue;

                        string url = link.Href.Trim();
                        writer.WriteLine($"Name: \"{cleanName}\", Url: \"{url}\"");
                    }
                }
                logger.Info($"[Nintendo_WiiU_Games] Wrote all games to TXT cache: {txtPath}");
            }
            else
            {
                logger.Info($"[Nintendo_WiiU_Games] Skipping web scrape, using TXT cache: {txtPath}");
            }

            // Always load from TXT file
            var results = new List<GameMetadata>();
            if (!File.Exists(txtPath))
            {
                logger.Warn($"[Nintendo_WiiU_Games] TXT cache file missing: {txtPath}");
                return results;
            }

            var lines = File.ReadAllLines(txtPath);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                if (!match.Success)
                    continue;

                string cleanName = match.Groups[1].Value.Trim();
                // string url = match.Groups[2].Value.Trim();  // Not used for GameAction.Path!
                string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                // Skip if already present
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    continue;

                var metadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = uniqueKey.ToLowerInvariant(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = downloadActionName,
                    Type = GameActionType.URL,
                    Path = Nintendo_WiiU_Games_BaseUrl, // Always use the base folder/page URL
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

                results.Add(metadata);
            }

            logger.Info($"[Nintendo_WiiU_Games] Import complete. New games added: {results.Count}");
            return results;
        }


        // Game Cube:
        private async Task<List<GameMetadata>> Myrient_Nintendo_GameCube_ScrapeStaticPage()
        {
            const string platformName = "Nintendo GameCube";
            const string downloadActionName = "Download: Myrient";
            string dataFolder = GetPluginUserDataPath();
            string txtPath = Path.Combine(dataFolder, "GameCube.Games.txt");

            // Build a hash set of normalized names for existing DB games with this platform
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

            // Only scrape and write TXT if missing
            if (!File.Exists(txtPath))
            {
                logger.Info($"[Nintendo_GameCube_Games] Scraping games from: {Nintendo_GameCube_Games_BaseUrl}");

                string pageContent = await Myrient_LoadPageContent(Nintendo_GameCube_Games_BaseUrl).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(pageContent))
                {
                    logger.Warn("[Nintendo_GameCube_Games] Failed to retrieve main page content.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Nintendo_GameCube_Games] Page content retrieved successfully ({pageContent.Length} characters).");

                // Extract all .zip links
                var zipLinks = Regex.Matches(pageContent, "<a[^>]+href=[\"']([^\"']+\\.zip)[\"'][^>]*>(.*?)<\\/a>", RegexOptions.IgnoreCase)
                    .Cast<Match>()
                    .Select(m => new
                    {
                        Href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value),
                        Text = m.Groups[2].Value
                    })
                    .ToList();

                if (zipLinks.Count == 0)
                {
                    logger.Info("[Nintendo_GameCube_Games] No valid .zip links found.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Nintendo_GameCube_Games] Found {zipLinks.Count} GameCube .zip links.");

                using (var writer = new StreamWriter(txtPath, false))
                {
                    foreach (var link in zipLinks)
                    {
                        string text = link.Text;
                        if (string.IsNullOrWhiteSpace(text))
                            text = Path.GetFileNameWithoutExtension(link.Href);

                        // Clean name: remove (Region), (vXX), any parenthesis section, and .zip extension, then trim
                        string cleanName = Regex.Replace(text, @"\s*\(.*?\)", "");
                        cleanName = Regex.Replace(cleanName, @"\s*v\d+$", "", RegexOptions.IgnoreCase);
                        cleanName = Regex.Replace(cleanName, "\\.zip$", "", RegexOptions.IgnoreCase);
                        cleanName = cleanName.Trim();

                        if (string.IsNullOrEmpty(cleanName))
                            continue;

                        string url = link.Href.Trim();
                        writer.WriteLine($"Name: \"{cleanName}\", Url: \"{url}\"");
                    }
                }
                logger.Info($"[Nintendo_GameCube_Games] Wrote all games to TXT cache: {txtPath}");
            }
            else
            {
                logger.Info($"[Nintendo_GameCube_Games] Skipping web scrape, using TXT cache: {txtPath}");
            }

            // Always load from TXT file
            var results = new List<GameMetadata>();
            if (!File.Exists(txtPath))
            {
                logger.Warn($"[Nintendo_GameCube_Games] TXT cache file missing: {txtPath}");
                return results;
            }

            var lines = File.ReadAllLines(txtPath);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                if (!match.Success)
                    continue;

                string cleanName = match.Groups[1].Value.Trim();
                string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                // Skip if already present in DB
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    continue;

                var metadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = uniqueKey.ToLowerInvariant(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = downloadActionName,
                    Type = GameActionType.URL,
                    Path = Nintendo_GameCube_Games_BaseUrl, // Always base URL for Playnite action
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

                results.Add(metadata);
            }

            logger.Info($"[Nintendo_GameCube_Games] Import complete. New games added: {results.Count}");
            return results;
        }
        private List<string> FindGameCubeGameRoms(string gameName)
        {
            var romPaths = new List<string>();
            var searchExtensions = new[] { ".iso", ".gcm", ".rvz", ".nkit.iso", ".nkit.gcm" };
            var searchDirectory = "Roms\\Nintendo - GameCube\\Games";

            foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                    if (System.IO.Directory.Exists(rootPath))
                    {
                        var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                            .Where(file => searchExtensions.Any(ext =>
                                file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

                        romPaths.AddRange(files);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Error searching GameCube ROMs in drive {drive.Name}: {ex.Message}");
                }
            }

            return romPaths;
        }



            // Nintendo 3DS:
            private async Task<List<GameMetadata>> Myrient_Nintendo_3DS_ScrapeStaticPage()
            {
                const string platformName = "Nintendo 3DS";
                const string downloadActionName = "Download: Myrient";
                string dataFolder = GetPluginUserDataPath();
                string txtPath = Path.Combine(dataFolder, "Nintendo3DS.Games.txt");

                // Build a hash set of normalized names for existing DB games with this platform
                var dbGameKeysPerPlatform = new HashSet<string>(
                    PlayniteApi.Database.Games
                        .Where(g => g.PluginId == Id
                            && g.Platforms != null
                            && g.GameActions != null
                            && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                        .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                    StringComparer.OrdinalIgnoreCase);

                // Only scrape and write TXT if missing
                if (!File.Exists(txtPath))
                {
                    logger.Info($"[Nintendo_3DS_Games] Scraping games from: {Nintendo_3DS_Games_BaseUrl}");

                    string pageContent = await Myrient_LoadPageContent(Nintendo_3DS_Games_BaseUrl).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(pageContent))
                    {
                        logger.Warn("[Nintendo_3DS_Games] Failed to retrieve main page content.");
                        return new List<GameMetadata>();
                    }
                    logger.Info($"[Nintendo_3DS_Games] Page content retrieved successfully ({pageContent.Length} characters).");

                    // Extract all .zip links
                    var zipLinks = Regex.Matches(pageContent, "<a[^>]+href=[\"']([^\"']+\\.zip)[\"'][^>]*>(.*?)<\\/a>", RegexOptions.IgnoreCase)
                        .Cast<Match>()
                        .Select(m => new
                        {
                            Href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value),
                            Text = m.Groups[2].Value
                        })
                        .ToList();

                    if (zipLinks.Count == 0)
                    {
                        logger.Info("[Nintendo_3DS_Games] No valid .zip links found.");
                        return new List<GameMetadata>();
                    }
                    logger.Info($"[Nintendo_3DS_Games] Found {zipLinks.Count} 3DS .zip links.");

                    using (var writer = new StreamWriter(txtPath, false))
                    {
                        foreach (var link in zipLinks)
                        {
                            string text = link.Text;
                            if (string.IsNullOrWhiteSpace(text))
                                text = Path.GetFileNameWithoutExtension(link.Href);

                            // Clean name: remove (Region), (vXX), any parenthesis section, and .zip extension, then trim
                            string cleanName = Regex.Replace(text, @"\s*\(.*?\)", "");
                            cleanName = Regex.Replace(cleanName, @"\s*v\d+$", "", RegexOptions.IgnoreCase);
                            cleanName = Regex.Replace(cleanName, "\\.zip$", "", RegexOptions.IgnoreCase);
                            cleanName = cleanName.Trim();

                            if (string.IsNullOrEmpty(cleanName))
                                continue;

                            string url = link.Href.Trim();
                            writer.WriteLine($"Name: \"{cleanName}\", Url: \"{url}\"");
                        }
                    }
                    logger.Info($"[Nintendo_3DS_Games] Wrote all games to TXT cache: {txtPath}");
                }
                else
                {
                    logger.Info($"[Nintendo_3DS_Games] Skipping web scrape, using TXT cache: {txtPath}");
                }

                // Always load from TXT file
                var results = new List<GameMetadata>();
                if (!File.Exists(txtPath))
                {
                    logger.Warn($"[Nintendo_3DS_Games] TXT cache file missing: {txtPath}");
                    return results;
                }

                var lines = File.ReadAllLines(txtPath);
                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                    if (!match.Success)
                        continue;

                    string cleanName = match.Groups[1].Value.Trim();
                    string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                    // Skip if already present in DB
                    if (dbGameKeysPerPlatform.Contains(uniqueKey))
                        continue;

                    var metadata = new GameMetadata
                    {
                        Name = cleanName,
                        GameId = uniqueKey.ToLowerInvariant(),
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                        GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = downloadActionName,
                        Type = GameActionType.URL,
                        Path = Nintendo_3DS_Games_BaseUrl, // Always base URL for Playnite action
                        IsPlayAction = false
                    }
                },
                        IsInstalled = false
                    };

                    results.Add(metadata);
                }

                logger.Info($"[Nintendo_3DS_Games] Import complete. New games added: {results.Count}");
                return results;
            }

            private List<string> Find3DSGameRoms(string gameName)
            {
                var romPaths = new List<string>();
                var searchExtensions = new[] { ".3ds", ".cia", ".zip" };
                var searchDirectory = "Roms\\Nintendo - 3DS\\Games";

                foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    try
                    {
                        var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                        if (System.IO.Directory.Exists(rootPath))
                        {
                            var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                                .Where(file => searchExtensions.Any(ext =>
                                    file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

                            romPaths.AddRange(files);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error searching 3DS ROMs in drive {drive.Name}: {ex.Message}");
                    }
                }

                return romPaths;
            }



            // ----------- Nintendo Game Boy -----------
            private async Task<List<GameMetadata>> Myrient_Nintendo_GameBoy_ScrapeStaticPage()
            {
                const string platformName = "Nintendo Game Boy";
                const string downloadActionName = "Download: Myrient";
                string dataFolder = GetPluginUserDataPath();
                string txtPath = Path.Combine(dataFolder, "GameBoy.Games.txt");

                // Fast O(1) lookup for games already in DB with this platform & download action
                var dbGameKeysPerPlatform = new HashSet<string>(
                    PlayniteApi.Database.Games
                        .Where(g => g.PluginId == Id
                            && g.Platforms != null
                            && g.GameActions != null
                            && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                        .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                    StringComparer.OrdinalIgnoreCase);

                // Only scrape the website if the TXT file does not exist
                if (!File.Exists(txtPath))
                {
                    logger.Info($"[Nintendo_GameBoy_Games] Scraping games from: {Nintendo_GameBoy_Games_BaseUrl}");

                    string pageContent = await Myrient_LoadPageContent(Nintendo_GameBoy_Games_BaseUrl).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(pageContent))
                    {
                        logger.Warn("[Nintendo_GameBoy_Games] Failed to retrieve main page content.");
                        return new List<GameMetadata>();
                    }
                    logger.Info($"[Nintendo_GameBoy_Games] Page content retrieved successfully ({pageContent.Length} characters).");

                    // Extract all .zip links
                    var gbLinks = Regex.Matches(pageContent, "<a[^>]+href=[\"']([^\"']+\\.zip)[\"'][^>]*>(.*?)<\\/a>", RegexOptions.IgnoreCase)
                        .Cast<Match>()
                        .Select(m => new
                        {
                            Href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value),
                            Text = m.Groups[2].Value
                        })
                        .ToList();

                    if (gbLinks.Count == 0)
                    {
                        logger.Info("[Nintendo_GameBoy_Games] No valid .zip links found.");
                        return new List<GameMetadata>();
                    }
                    logger.Info($"[Nintendo_GameBoy_Games] Found {gbLinks.Count} Game Boy .zip links.");

                    // Write all found games to the TXT file
                    using (var writer = new StreamWriter(txtPath, false))
                    {
                        foreach (var link in gbLinks)
                        {
                            string text = link.Text;
                            if (string.IsNullOrWhiteSpace(text))
                                text = Path.GetFileNameWithoutExtension(link.Href);

                            string cleanName = Regex.Replace(text, @"\s*\(.*?\)", "");
                            cleanName = Regex.Replace(cleanName, @"\s*v\d+$", "", RegexOptions.IgnoreCase);
                            cleanName = Regex.Replace(cleanName, "\\.zip$", "", RegexOptions.IgnoreCase);
                            cleanName = cleanName.Trim();

                            if (string.IsNullOrEmpty(cleanName))
                                continue;

                            string url = link.Href.Trim();
                            writer.WriteLine($"Name: \"{cleanName}\", Url: \"{url}\"");
                        }
                    }
                    logger.Info($"[Nintendo_GameBoy_Games] Wrote all games to TXT cache: {txtPath}");
                }
                else
                {
                    logger.Info($"[Nintendo_GameBoy_Games] Skipping web scrape, using TXT cache: {txtPath}");
                }

                // Always load from TXT file
                var results = new List<GameMetadata>();
                if (!File.Exists(txtPath))
                {
                    logger.Warn($"[Nintendo_GameBoy_Games] TXT cache file missing: {txtPath}");
                    return results;
                }

                var lines = File.ReadAllLines(txtPath);
                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                    if (!match.Success)
                        continue;

                    var cleanName = match.Groups[1].Value.Trim();
                    string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                    // O(1) skip if game already present in Playnite DB
                    if (dbGameKeysPerPlatform.Contains(uniqueKey))
                        continue;

                    var metadata = new GameMetadata
                    {
                        Name = cleanName,
                        GameId = uniqueKey.ToLowerInvariant(),
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                        GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = downloadActionName,
                        Type = GameActionType.URL,
                        Path = Nintendo_GameBoy_Games_BaseUrl, // Always base URL for Playnite action
                        IsPlayAction = false
                    }
                },
                        IsInstalled = false
                    };

                    results.Add(metadata);
                }

                logger.Info($"[Nintendo_GameBoy_Games] Import complete. New games added: {results.Count}");
                return results;
            }
            private List<string> FindGameBoyGameRoms(string gameName)
            {
                var romPaths = new List<string>();
                var searchExtensions = new[] { ".gb" };
                var searchDirectory = "Roms\\Nintendo - Game Boy\\Games";

                foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    try
                    {
                        var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                        if (System.IO.Directory.Exists(rootPath))
                        {
                            var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                                .Where(file => searchExtensions.Any(ext =>
                                    file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

                            romPaths.AddRange(files);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error searching Game Boy ROMs in drive {drive.Name}: {ex.Message}");
                    }
                }

                return romPaths;
            }


            // ----------- Nintendo Game Boy Color -----------
            private async Task<List<GameMetadata>> Myrient_Nintendo_GameBoyColor_ScrapeStaticPage()
            {
                const string platformName = "Nintendo Game Boy Color";
                const string downloadActionName = "Download: Myrient";
                string dataFolder = GetPluginUserDataPath();
                string txtPath = Path.Combine(dataFolder, "GameBoyColor.Games.txt");

                // Fast O(1) lookup for games already in DB with this platform & download action
                var dbGameKeysPerPlatform = new HashSet<string>(
                    PlayniteApi.Database.Games
                        .Where(g => g.PluginId == Id
                            && g.Platforms != null
                            && g.GameActions != null
                            && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                        .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                    StringComparer.OrdinalIgnoreCase);

                // Only scrape the website if the TXT file does not exist
                if (!File.Exists(txtPath))
                {
                    logger.Info($"[Nintendo_GameBoyColor_Games] Scraping games from: {Nintendo_GameBoyColor_Games_BaseUrl}");

                    string pageContent = await Myrient_LoadPageContent(Nintendo_GameBoyColor_Games_BaseUrl).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(pageContent))
                    {
                        logger.Warn("[Nintendo_GameBoyColor_Games] Failed to retrieve main page content.");
                        return new List<GameMetadata>();
                    }
                    logger.Info($"[Nintendo_GameBoyColor_Games] Page content retrieved successfully ({pageContent.Length} characters).");

                    // Extract all .zip links
                    var gbcLinks = Regex.Matches(pageContent, "<a[^>]+href=[\"']([^\"']+\\.zip)[\"'][^>]*>(.*?)<\\/a>", RegexOptions.IgnoreCase)
                        .Cast<Match>()
                        .Select(m => new
                        {
                            Href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value),
                            Text = m.Groups[2].Value
                        })
                        .ToList();

                    if (gbcLinks.Count == 0)
                    {
                        logger.Info("[Nintendo_GameBoyColor_Games] No valid .zip links found.");
                        return new List<GameMetadata>();
                    }
                    logger.Info($"[Nintendo_GameBoyColor_Games] Found {gbcLinks.Count} Game Boy Color .zip links.");

                    // Write all found games to the TXT file
                    using (var writer = new StreamWriter(txtPath, false))
                    {
                        foreach (var link in gbcLinks)
                        {
                            string text = link.Text;
                            if (string.IsNullOrWhiteSpace(text))
                                text = Path.GetFileNameWithoutExtension(link.Href);

                            string cleanName = Regex.Replace(text, @"\s*\(.*?\)", "");
                            cleanName = Regex.Replace(cleanName, @"\s*v\d+$", "", RegexOptions.IgnoreCase);
                            cleanName = Regex.Replace(cleanName, "\\.zip$", "", RegexOptions.IgnoreCase);
                            cleanName = cleanName.Trim();

                            if (string.IsNullOrEmpty(cleanName))
                                continue;

                            string url = link.Href.Trim();
                            writer.WriteLine($"Name: \"{cleanName}\", Url: \"{url}\"");
                        }
                    }
                    logger.Info($"[Nintendo_GameBoyColor_Games] Wrote all games to TXT cache: {txtPath}");
                }
                else
                {
                    logger.Info($"[Nintendo_GameBoyColor_Games] Skipping web scrape, using TXT cache: {txtPath}");
                }

                // Always load from TXT file
                var results = new List<GameMetadata>();
                if (!File.Exists(txtPath))
                {
                    logger.Warn($"[Nintendo_GameBoyColor_Games] TXT cache file missing: {txtPath}");
                    return results;
                }

                var lines = File.ReadAllLines(txtPath);
                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                    if (!match.Success)
                        continue;

                    var cleanName = match.Groups[1].Value.Trim();
                    string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                    // O(1) skip if game already present in Playnite DB
                    if (dbGameKeysPerPlatform.Contains(uniqueKey))
                        continue;

                    var metadata = new GameMetadata
                    {
                        Name = cleanName,
                        GameId = uniqueKey.ToLowerInvariant(),
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                        GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = downloadActionName,
                        Type = GameActionType.URL,
                        Path = Nintendo_GameBoyColor_Games_BaseUrl, // Always base URL for Playnite action
                        IsPlayAction = false
                    }
                },
                        IsInstalled = false
                    };

                    results.Add(metadata);
                }

                logger.Info($"[Nintendo_GameBoyColor_Games] Import complete. New games added: {results.Count}");
                return results;
            }
            private List<string> FindGameBoyColorGameRoms(string gameName)
            {
                var romPaths = new List<string>();
                var searchExtensions = new[] { ".gbc" };
                var searchDirectory = "Roms\\Nintendo - Game Boy Color\\Games";

                foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    try
                    {
                        var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                        if (System.IO.Directory.Exists(rootPath))
                        {
                            var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                                .Where(file => searchExtensions.Any(ext =>
                                    file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

                            romPaths.AddRange(files);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error searching Game Boy Color ROMs in drive {drive.Name}: {ex.Message}");
                    }
                }

                return romPaths;
            }


            // ----------- Nintendo Game Boy Advance -----------
            private async Task<List<GameMetadata>> Myrient_Nintendo_GameBoyAdvance_ScrapeStaticPage()
            {
                const string platformName = "Nintendo Game Boy Advance";
                const string downloadActionName = "Download: Myrient";
                string dataFolder = GetPluginUserDataPath();
                string txtPath = Path.Combine(dataFolder, "GameBoyAdvance.Games.txt");

                // Fast O(1) lookup for games already in DB with this platform & download action
                var dbGameKeysPerPlatform = new HashSet<string>(
                    PlayniteApi.Database.Games
                        .Where(g => g.PluginId == Id
                            && g.Platforms != null
                            && g.GameActions != null
                            && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                        .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                    StringComparer.OrdinalIgnoreCase);

                // Only scrape the website if the TXT file does not exist
                if (!File.Exists(txtPath))
                {
                    logger.Info($"[Nintendo_GameBoyAdvance_Games] Scraping games from: {Nintendo_GameBoyAdvance_Games_BaseUrl}");

                    string pageContent = await Myrient_LoadPageContent(Nintendo_GameBoyAdvance_Games_BaseUrl).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(pageContent))
                    {
                        logger.Warn("[Nintendo_GameBoyAdvance_Games] Failed to retrieve main page content.");
                        return new List<GameMetadata>();
                    }
                    logger.Info($"[Nintendo_GameBoyAdvance_Games] Page content retrieved successfully ({pageContent.Length} characters).");

                    // Extract all .zip links
                    var gbaLinks = Regex.Matches(pageContent, "<a[^>]+href=[\"']([^\"']+\\.zip)[\"'][^>]*>(.*?)<\\/a>", RegexOptions.IgnoreCase)
                        .Cast<Match>()
                        .Select(m => new
                        {
                            Href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value),
                            Text = m.Groups[2].Value
                        })
                        .ToList();

                    if (gbaLinks.Count == 0)
                    {
                        logger.Info("[Nintendo_GameBoyAdvance_Games] No valid .zip links found.");
                        return new List<GameMetadata>();
                    }
                    logger.Info($"[Nintendo_GameBoyAdvance_Games] Found {gbaLinks.Count} Game Boy Advance .zip links.");

                    // Write all found games to the TXT file
                    using (var writer = new StreamWriter(txtPath, false))
                    {
                        foreach (var link in gbaLinks)
                        {
                            string text = link.Text;
                            if (string.IsNullOrWhiteSpace(text))
                                text = Path.GetFileNameWithoutExtension(link.Href);

                            string cleanName = Regex.Replace(text, @"\s*\(.*?\)", "");
                            cleanName = Regex.Replace(cleanName, @"\s*v\d+$", "", RegexOptions.IgnoreCase);
                            cleanName = Regex.Replace(cleanName, "\\.zip$", "", RegexOptions.IgnoreCase);
                            cleanName = cleanName.Trim();

                            if (string.IsNullOrEmpty(cleanName))
                                continue;

                            string url = link.Href.Trim();
                            writer.WriteLine($"Name: \"{cleanName}\", Url: \"{url}\"");
                        }
                    }
                    logger.Info($"[Nintendo_GameBoyAdvance_Games] Wrote all games to TXT cache: {txtPath}");
                }
                else
                {
                    logger.Info($"[Nintendo_GameBoyAdvance_Games] Skipping web scrape, using TXT cache: {txtPath}");
                }

                // Always load from TXT file
                var results = new List<GameMetadata>();
                if (!File.Exists(txtPath))
                {
                    logger.Warn($"[Nintendo_GameBoyAdvance_Games] TXT cache file missing: {txtPath}");
                    return results;
                }

                var lines = File.ReadAllLines(txtPath);
                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                    if (!match.Success)
                        continue;

                    var cleanName = match.Groups[1].Value.Trim();
                    string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                    // O(1) skip if game already present in Playnite DB
                    if (dbGameKeysPerPlatform.Contains(uniqueKey))
                        continue;

                    var metadata = new GameMetadata
                    {
                        Name = cleanName,
                        GameId = uniqueKey.ToLowerInvariant(),
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                        GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = downloadActionName,
                        Type = GameActionType.URL,
                        Path = Nintendo_GameBoyAdvance_Games_BaseUrl, // Always base URL for Playnite action
                        IsPlayAction = false
                    }
                },
                        IsInstalled = false
                    };

                    results.Add(metadata);
                }

                logger.Info($"[Nintendo_GameBoyAdvance_Games] Import complete. New games added: {results.Count}");
                return results;
            }
            private List<string> FindGameBoyAdvanceGameRoms(string gameName)
            {
                var romPaths = new List<string>();
                var searchExtensions = new[] { ".gba" };
                var searchDirectory = "Roms\\Nintendo - Game Boy Advance\\Games";

                foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    try
                    {
                        var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                        if (System.IO.Directory.Exists(rootPath))
                        {
                            var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                                .Where(file => searchExtensions.Any(ext =>
                                    file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

                            romPaths.AddRange(files);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error searching Game Boy Advance ROMs in drive {drive.Name}: {ex.Message}");
                    }
                }

                return romPaths;
            }


            // Nnitendo 64
            private List<string> FindN64GameRoms(string gameName)
            {
                var romPaths = new List<string>();
                var searchExtensions = new[] { ".z64", ".n64", ".v64" };
                var searchDirectory = "Roms\\Nintendo - N64\\Games";

                foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    try
                    {
                        var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                        if (System.IO.Directory.Exists(rootPath))
                        {
                            var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                                .Where(file => searchExtensions.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));

                            romPaths.AddRange(files);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error searching Nintendo 64 ROMs in drive {drive.Name}: {ex.Message}");
                    }
                }

                return romPaths;
            }
            private async Task<List<GameMetadata>> Myrient_Nintendo64_ScrapeStaticPage()
            {
                const string platformName = "Nintendo 64";
                const string downloadActionName = "Download: Myrient";
                const string Nintendo64_Games_BaseUrl = "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%2064%20(BigEndian)/";
                string dataFolder = GetPluginUserDataPath();
                string txtPath = Path.Combine(dataFolder, "Nintendo64.Games.txt");

                // Fast O(1) lookup for games already in DB with this platform & download action
                var dbGameKeysPerPlatform = new HashSet<string>(
                    PlayniteApi.Database.Games
                        .Where(g => g.PluginId == Id
                            && g.Platforms != null
                            && g.GameActions != null
                            && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                        .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                    StringComparer.OrdinalIgnoreCase);

                // Only scrape and write TXT if missing
                if (!File.Exists(txtPath))
                {
                    logger.Info($"[Nintendo64_Games] Scraping games from: {Nintendo64_Games_BaseUrl}");

                    string pageContent = await Myrient_LoadPageContent(Nintendo64_Games_BaseUrl).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(pageContent))
                    {
                        logger.Warn("[Nintendo64_Games] Failed to retrieve main page content.");
                        return new List<GameMetadata>();
                    }
                    logger.Info($"[Nintendo64_Games] Page content retrieved successfully ({pageContent.Length} characters).");

                    var links = Myrient_ParseLinks(pageContent)?
                        .Where(link =>
                               link.Item1.EndsWith(".z64", StringComparison.OrdinalIgnoreCase)
                            || link.Item1.EndsWith(".n64", StringComparison.OrdinalIgnoreCase)
                            || link.Item1.EndsWith(".v64", StringComparison.OrdinalIgnoreCase)
                            || link.Item1.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    if (links == null || links.Length == 0)
                    {
                        logger.Info("[Nintendo64_Games] No valid game links found.");
                        return new List<GameMetadata>();
                    }
                    logger.Info($"[Nintendo64_Games] Found {links.Length} Nintendo 64 game links.");

                    // Write all found games to the TXT file
                    using (var writer = new StreamWriter(txtPath, false))
                    {
                        foreach (var link in links)
                        {
                            string text = link.Item2;
                            if (string.IsNullOrWhiteSpace(text))
                                continue;

                            // Clean the game name: remove file extension (.z64, .n64, .v64, .zip)
                            string rawName = Myrient_CleanGameName(text);
                            string cleanName = Regex.Replace(rawName, @"\.(zip|z64|n64|v64)$", "", RegexOptions.IgnoreCase).Trim();

                            if (string.IsNullOrEmpty(cleanName))
                                cleanName = fallbackRegex.Replace(text, "$1").Replace('-', ' ').Trim();
                            if (string.IsNullOrEmpty(cleanName))
                                continue;

                            string url = link.Item1.Trim();
                            writer.WriteLine($"Name: \"{cleanName}\", Url: \"{url}\"");
                        }
                    }
                    logger.Info($"[Nintendo64_Games] Wrote all games to TXT cache: {txtPath}");
                }
                else
                {
                    logger.Info($"[Nintendo64_Games] Skipping web scrape, using TXT cache: {txtPath}");
                }

                // Always load from TXT file
                var results = new List<GameMetadata>();
                if (!File.Exists(txtPath))
                {
                    logger.Warn($"[Nintendo64_Games] TXT cache file missing: {txtPath}");
                    return results;
                }

                var lines = File.ReadAllLines(txtPath);
                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                    if (!match.Success)
                        continue;

                    var cleanName = match.Groups[1].Value.Trim();
                    var url = match.Groups[2].Value.Trim();
                    string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                    // O(1) skip if game already present in Playnite DB with Download: Myrient action
                    if (dbGameKeysPerPlatform.Contains(uniqueKey))
                        continue;

                    var metadata = new GameMetadata
                    {
                        Name = cleanName,
                        GameId = uniqueKey.ToLower(),
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                        GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = downloadActionName,
                        Type = GameActionType.URL,
                        Path = Nintendo64_Games_BaseUrl, // Always base URL for Playnite action
                        IsPlayAction = false
                    }
                },
                        IsInstalled = false
                    };

                    // --- Add Emulator Play Action for Project64 ---
                    var project64 = PlayniteApi.Database.Emulators
                                        .FirstOrDefault(e => e.Name.Equals("Project64", StringComparison.OrdinalIgnoreCase));
                    if (project64 != null && project64.BuiltinProfiles != null)
                    {
                        var n64Profile = project64.BuiltinProfiles
                                            .FirstOrDefault(p => p.Name.IndexOf("Nintendo 64", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (n64Profile != null)
                        {
                            // Use url as a placeholder for the ROM path.
                            metadata.GameActions.Add(new GameAction
                            {
                                Name = "Play",
                                Type = GameActionType.Emulator,
                                EmulatorId = project64.Id,
                                EmulatorProfileId = n64Profile.Id,
                                Path = url,
                                IsPlayAction = true
                            });
                        }
                    }

                    results.Add(metadata);
                }

                logger.Info($"[Nintendo64_Games] Import complete. New games added: {results.Count}");
                return results;
            }


        // ----------- Microsoft Xbox -----------
        private async Task<List<GameMetadata>> Myrient_Microsoft_Xbox_ScrapeStaticPage()
        {
            const string platformName = "Microsoft Xbox";
            const string downloadActionName = "Download: Myrient";
            string dataFolder = GetPluginUserDataPath();
            string txtPath = Path.Combine(dataFolder, "Xbox.Games.txt");

            // Fast O(1) lookup for games already in DB with this platform & download action
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

            // Only scrape the website if the TXT file does not exist
            if (!File.Exists(txtPath))
            {
                logger.Info($"[Microsoft_Xbox_Games] Scraping games from: {Microsoft_Xbox_Games_BaseUrl}");

                string pageContent = await Myrient_LoadPageContent(Microsoft_Xbox_Games_BaseUrl).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(pageContent))
                {
                    logger.Warn("[Microsoft_Xbox_Games] Failed to retrieve main page content.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Microsoft_Xbox_Games] Page content retrieved successfully ({pageContent.Length} characters).");

                // Extract all .zip links (common for Xbox ISOs on Myrient)
                var zipLinks = Regex.Matches(pageContent, "<a[^>]+href=[\"']([^\"']+\\.zip)[\"'][^>]*>(.*?)<\\/a>", RegexOptions.IgnoreCase)
                    .Cast<Match>()
                    .Select(m => new
                    {
                        Href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value),
                        Text = m.Groups[2].Value
                    })
                    .ToList();

                if (zipLinks.Count == 0)
                {
                    logger.Info("[Microsoft_Xbox_Games] No valid .zip links found.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Microsoft_Xbox_Games] Found {zipLinks.Count} Xbox .zip links.");

                // Write all found games to the TXT file
                using (var writer = new StreamWriter(txtPath, false))
                {
                    foreach (var link in zipLinks)
                    {
                        string text = link.Text;
                        if (string.IsNullOrWhiteSpace(text))
                            text = Path.GetFileNameWithoutExtension(link.Href);

                        string cleanName = Regex.Replace(text, @"\s*\(.*?\)", "");
                        cleanName = Regex.Replace(cleanName, @"\s*v\d+$", "", RegexOptions.IgnoreCase);
                        cleanName = Regex.Replace(cleanName, "\\.zip$", "", RegexOptions.IgnoreCase);
                        cleanName = cleanName.Trim();

                        if (string.IsNullOrEmpty(cleanName))
                            continue;

                        string url = link.Href.Trim();
                        writer.WriteLine($"Name: \"{cleanName}\", Url: \"{url}\"");
                    }
                }
                logger.Info($"[Microsoft_Xbox_Games] Wrote all games to TXT cache: {txtPath}");
            }
            else
            {
                logger.Info($"[Microsoft_Xbox_Games] Skipping web scrape, using TXT cache: {txtPath}");
            }

            // Always load from TXT file
            var results = new List<GameMetadata>();
            if (!File.Exists(txtPath))
            {
                logger.Warn($"[Microsoft_Xbox_Games] TXT cache file missing: {txtPath}");
                return results;
            }

            var lines = File.ReadAllLines(txtPath);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                if (!match.Success)
                    continue;

                var cleanName = match.Groups[1].Value.Trim();
                string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                // O(1) skip if game already present in Playnite DB with Download: Myrient action
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    continue;

                var metadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = uniqueKey.ToLowerInvariant(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = downloadActionName,
                    Type = GameActionType.URL,
                    Path = Microsoft_Xbox_Games_BaseUrl, // Always base URL for Playnite action
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

                results.Add(metadata);
            }

            logger.Info($"[Microsoft_Xbox_Games] Import complete. New games added: {results.Count}");
            return results;
        }
        private List<string> FindMicrosoftXboxGameRoms(string gameName)
        {
            var romPaths = new List<string>();
            var searchExtensions = new[] { ".iso", ".zip", ".7z" }; // .iso is the primary format, but .zip/.7z are common on archive sites
            var searchDirectory = "Roms\\Microsoft - Xbox\\Games";

            foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                    if (System.IO.Directory.Exists(rootPath))
                    {
                        var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                            .Where(file => searchExtensions.Any(ext =>
                                file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

                        romPaths.AddRange(files);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Error searching Microsoft Xbox ROMs in drive {drive.Name}: {ex.Message}");
                }
            }

            return romPaths;
        }

        // Xbox 360 Disc
        private List<string> FindXbox360GameRoms(string gameName)
        {
            var romPaths = new List<string>();
            var searchExtensions = new[] { ".iso", ".xex", ".zar" };
            var searchDirectory = "Roms\\Microsoft - Xbox 360\\Games";

            foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                    if (System.IO.Directory.Exists(rootPath))
                    {
                        var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                            .Where(file => searchExtensions.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));

                        romPaths.AddRange(files);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Error searching Xbox 360 ROMs in drive {drive.Name}: {ex.Message}");
                }
            }

            return romPaths;
        }
        private async Task<List<GameMetadata>> Myrient_Xbox360_ScrapeStaticPage()
        {
            const string platformName = "Microsoft Xbox 360";
            const string downloadActionName = "Download: Myrient";
            string dataFolder = GetPluginUserDataPath();
            string txtPath = Path.Combine(dataFolder, "Xbox360.Games.txt");

            // Fast O(1) lookup: All normalized names for this pluginId & Xbox 360 platform WITH "Download: Myrient" action only
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.Platforms.Any(p => p.Name.Equals(platformName, StringComparison.OrdinalIgnoreCase))
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms
                        .Where(p => p.Name.Equals(platformName, StringComparison.OrdinalIgnoreCase))
                        .Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

            // Only scrape the website if the TXT file does not exist
            if (!File.Exists(txtPath))
            {
                logger.Info($"[Xbox360_Games] Scraping games from: {Xbox360_Games_BaseUrl}");

                string pageContent = await Myrient_LoadPageContent(Xbox360_Games_BaseUrl).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(pageContent))
                {
                    logger.Warn("[Xbox360_Games] Failed to retrieve main page content.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Xbox360_Games] Page content retrieved successfully ({pageContent.Length} characters).");

                var links = Myrient_ParseLinks(pageContent)?
                    .Where(link => link.Item1.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (links == null || links.Length == 0)
                {
                    logger.Info("[Xbox360_Games] No valid game links found.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Xbox360_Games] Found {links.Length} Xbox 360 game links.");

                // Write all found games to the TXT file
                using (var writer = new StreamWriter(txtPath, false))
                {
                    foreach (var link in links)
                    {
                        string originalText = link.Item2?.Trim();
                        if (string.IsNullOrWhiteSpace(originalText))
                            continue;

                        // Remove .zip extension and region tags for matching
                        string cleanName = originalText;
                        if (cleanName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            cleanName = cleanName.Substring(0, cleanName.Length - ".zip".Length);
                        cleanName = System.Text.RegularExpressions.Regex.Replace(cleanName, @"\s*\(.*?\)$", "");
                        cleanName = cleanName.Trim();

                        if (string.IsNullOrEmpty(cleanName))
                            cleanName = originalText;

                        string url = link.Item1.Trim();
                        writer.WriteLine($"Name: \"{cleanName}\", Url: \"{url}\"");
                    }
                }
                logger.Info($"[Xbox360_Games] Wrote all games to TXT cache: {txtPath}");
            }
            else
            {
                logger.Info($"[Xbox360_Games] Skipping web scrape, using TXT cache: {txtPath}");
            }

            // Now always load from TXT file
            var results = new List<GameMetadata>();
            if (!File.Exists(txtPath))
            {
                logger.Warn($"[Xbox360_Games] TXT cache file missing: {txtPath}");
                return results;
            }

            var lines = File.ReadAllLines(txtPath);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                if (!match.Success)
                    continue;

                var cleanName = match.Groups[1].Value.Trim();
                var url = match.Groups[2].Value.Trim();
                string normalizedName = Myrient_NormalizeGameName(cleanName);
                string uniqueKey = $"{normalizedName}|{platformName}";

                // O(1) skip if the game exists in the Playnite DB with "Download: Myrient" action and Xbox 360 platform
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    continue;

                var metadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = uniqueKey.ToLower(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = downloadActionName,
                    Type = GameActionType.URL,
                    Path = Xbox360_Games_BaseUrl, // Always use the base folder/page URL for Playnite action
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

                // Optionally add "Play" emulator action if Xenia is present
                var xenia = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("Xenia", StringComparison.OrdinalIgnoreCase));
                if (xenia != null && xenia.BuiltinProfiles != null)
                {
                    var x360Profile = xenia.BuiltinProfiles.FirstOrDefault(p => p.Name.Equals("Xbox 360", StringComparison.OrdinalIgnoreCase));
                    if (x360Profile != null)
                    {
                        metadata.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = xenia.Id,
                            EmulatorProfileId = x360Profile.Id,
                            Path = url, // This is the download link; update to local ROM path when installed
                            IsPlayAction = true
                        });
                    }
                }

                results.Add(metadata);
            }

            logger.Info($"[Xbox360_Games] Import complete. New games added: {results.Count}");
            return results;
        }        // Xbox 360 Digital
        private List<string> FindXbox360DigitalGameRoms(string gameName)
        {
            var romPaths = new List<string>();
            var searchExtensions = new[] { ".god", ".xex", ".zar" }; // Adjust extensions as needed for digital titles
            var searchDirectory = "Roms\\Microsoft - Xbox 360\\XBLA";

            foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                    if (System.IO.Directory.Exists(rootPath))
                    {
                        var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                            .Where(file => searchExtensions.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));

                        romPaths.AddRange(files);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Error searching Xbox 360 Digital ROMs in drive {drive.Name}: {ex.Message}");
                }
            }

            return romPaths;
        }
        private async Task<List<GameMetadata>> Myrient_Xbox360Digital_ScrapeStaticPage()
        {
            const string platformName = "Microsoft Xbox 360";
            const string downloadActionName = "Download: Myrient";
            string dataFolder = GetPluginUserDataPath();
            string txtPath = Path.Combine(dataFolder, "Xbox360Digital.Games.txt");

            // O(1) lookup for existing games with this plugin id, Xbox 360 platform, and "Download: Myrient" action
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.Platforms.Any(p => p.Name.Equals(platformName, StringComparison.OrdinalIgnoreCase))
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms
                        .Where(p => p.Name.Equals(platformName, StringComparison.OrdinalIgnoreCase))
                        .Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

            // Only scrape the website if the TXT file does not exist
            if (!File.Exists(txtPath))
            {
                logger.Info($"[Xbox360_Digital] Scraping games from: {Xbox360Digital_Games_BaseUrl}");

                string pageContent = await Myrient_LoadPageContent(Xbox360Digital_Games_BaseUrl).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(pageContent))
                {
                    logger.Warn("[Xbox360_Digital] Failed to retrieve main page content.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Xbox360_Digital] Page content retrieved successfully ({pageContent.Length} characters).");

                // Only get links with (XBLA) or (XBLIG) in the name/text
                var links = Myrient_ParseLinks(pageContent)?
                    .Where(link =>
                        (link.Item2.IndexOf("(XBLA)", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         link.Item2.IndexOf("(XBLIG)", StringComparison.OrdinalIgnoreCase) >= 0) &&
                        (link.Item1.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                         link.Item1.EndsWith(".god", StringComparison.OrdinalIgnoreCase) ||
                         link.Item1.EndsWith(".xex", StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

                if (links == null || links.Length == 0)
                {
                    logger.Info("[Xbox360_Digital] No valid game links found.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Xbox360_Digital] Found {links.Length} Xbox 360 digital game links.");

                // Write all found games to the TXT file
                using (var writer = new StreamWriter(txtPath, false))
                {
                    foreach (var link in links)
                    {
                        string originalText = link.Item2?.Trim();
                        if (string.IsNullOrWhiteSpace(originalText))
                            continue;

                        // Remove extension and region tags for matching
                        string cleanName = originalText;
                        cleanName = System.Text.RegularExpressions.Regex.Replace(cleanName, @"\.(zip|god|xex)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        cleanName = System.Text.RegularExpressions.Regex.Replace(cleanName, @"\s*\(.*?\)$", "");
                        cleanName = cleanName.Trim();

                        if (string.IsNullOrEmpty(cleanName))
                            cleanName = originalText;

                        string url = link.Item1.Trim();
                        writer.WriteLine($"Name: \"{cleanName}\", Url: \"{url}\"");
                    }
                }
                logger.Info($"[Xbox360_Digital] Wrote all games to TXT cache: {txtPath}");
            }
            else
            {
                logger.Info($"[Xbox360_Digital] Skipping web scrape, using TXT cache: {txtPath}");
            }

            // Now always load from TXT file
            var results = new List<GameMetadata>();
            if (!File.Exists(txtPath))
            {
                logger.Warn($"[Xbox360_Digital] TXT cache file missing: {txtPath}");
                return results;
            }

            var lines = File.ReadAllLines(txtPath);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                if (!match.Success)
                    continue;

                var cleanName = match.Groups[1].Value.Trim();
                var url = match.Groups[2].Value.Trim();
                string normalizedName = Myrient_NormalizeGameName(cleanName);
                string uniqueKey = $"{normalizedName}|{platformName}";

                // O(1) skip if the game exists in the Playnite DB with "Download: Myrient" action and Xbox 360 platform
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    continue;

                var metadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = uniqueKey.ToLower(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = downloadActionName,
                    Type = GameActionType.URL,
                    Path = Xbox360Digital_Games_BaseUrl, // Always use the base/folder URL for Playnite action
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

                // Optionally add "Play" emulator action if Xenia is present
                var xenia = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("Xenia", StringComparison.OrdinalIgnoreCase));
                if (xenia != null && xenia.BuiltinProfiles != null)
                {
                    var x360Profile = xenia.BuiltinProfiles.FirstOrDefault(p => p.Name.Equals("Xbox 360", StringComparison.OrdinalIgnoreCase));
                    if (x360Profile != null)
                    {
                        metadata.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = xenia.Id,
                            EmulatorProfileId = x360Profile.Id,
                            Path = url, // This is the download link; update to local ROM path when installed
                            IsPlayAction = true
                        });
                    }
                }

                results.Add(metadata);
            }

            logger.Info($"[Xbox360_Digital] Import complete. New games added: {results.Count}");
            return results;
        }
        public static MetadataSpecProperty GetOrCreateXbox360DigitalPlatform(IPlayniteAPI api)
        {
            const string platformName = "Microsoft Xbox 360 Digital";

            // Try to find existing platform
            var platform = api.Database.Platforms.FirstOrDefault(p =>
                p.Name.Equals(platformName, StringComparison.OrdinalIgnoreCase));
            if (platform == null)
            {
                // Create if not found
                var newPlatform = new Platform(platformName);
                api.Database.Platforms.Add(newPlatform); // Add returns void in Playnite 9, Guid in Playnite 10
                                                         // Fetch the newly created platform
                platform = api.Database.Platforms.FirstOrDefault(p => p.Name.Equals(platformName, StringComparison.OrdinalIgnoreCase));
            }
            if (platform != null)
            {
                return new MetadataSpecProperty(platform.Id.ToString());
            }
            return null;
        }

        // PS2 
        private List<string> FindPS2GameRoms(string gameName)
        {
            var romPaths = new List<string>();
            var searchExtensions = new[] { ".chd", ".iso" };
            var searchDirectory = "Roms\\Sony - PlayStation 2";

            foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                    if (System.IO.Directory.Exists(rootPath))
                    {
                        var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                            .Where(file => searchExtensions.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));

                        romPaths.AddRange(files);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Error searching ROMs in drive {drive.Name}: {ex.Message}");
                }
            }

            return romPaths;
        }
        private string PS2_NormalizeGameName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // 1. Lowercase and trim.
            string normalized = name.ToLowerInvariant().Trim();

            // 1.1 Remove periods if they occur between word characters.
            normalized = Regex.Replace(normalized, @"(?<=\w)\.(?=\w)", "");

            // 2. Remove apostrophes (both straight and smart).
            normalized = normalized.Replace("’", "").Replace("'", "");

            // 3. Replace ampersands and plus signs with " and ".
            normalized = normalized.Replace("&", " and ").Replace("+", " and ");

            // 4. Remove unwanted punctuation.
            normalized = Regex.Replace(normalized, @"[^\w\s]", " ");

            // 5. Collapse multiple spaces.
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            // 6. Special rule for Marvel: if it starts with "marvels", change it to "marvel".
            normalized = Regex.Replace(normalized, @"^marvels\b", "marvel", RegexOptions.IgnoreCase);

            // 7. Normalize "Game of The Year" variants:
            normalized = Regex.Replace(normalized, @"\bgame of (the year)( edition)?\b", "goty", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bgoty( edition)?\b", "goty", RegexOptions.IgnoreCase);

            // 8. Normalize "Grand Theft Auto" to "gta".
            normalized = Regex.Replace(normalized, @"\bgrand theft auto\b", "gta", RegexOptions.IgnoreCase);

            // 9. Normalize Spider-man variants.
            normalized = Regex.Replace(normalized, @"\bspider[\s\-]+man\b", "spiderman", RegexOptions.IgnoreCase);

            // 10. Normalize the acronym REPO.
            normalized = Regex.Replace(normalized, @"\br\.?e\.?p\.?o\b", "repo", RegexOptions.IgnoreCase);

            // 11. Normalize the acronym FEAR.
            normalized = Regex.Replace(normalized, @"\bf\s*e\s*a\s*r\b", "fear", RegexOptions.IgnoreCase);

            // 12. Normalize Rick and Morty Virtual Rick-ality VR variants.
            normalized = Regex.Replace(normalized,
                @"rick and morty\s*[:]?\s*virtual rickality vr",
                "rick and morty virtual rickality vr", RegexOptions.IgnoreCase);

            // 13. Normalize common Roman numerals.
            normalized = Regex.Replace(normalized, @"\bviii\b", "8", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bvii\b", "7", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\biv\b", "4", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\biii\b", "3", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bvi\b", "6", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bix\b", "9", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bx\b", "10", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bv\b", "5", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bii\b", "2", RegexOptions.IgnoreCase);

            return normalized.Trim();
        }
        private string PS2_SanitizePath(string path)
        {
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }
        private async Task<List<GameMetadata>> Myrient_Sony_PS2_ScrapeStaticPage()
        {
            const string platformName = "Sony PlayStation 2";
            const string downloadActionName = "Download: Myrient";
            string dataFolder = GetPluginUserDataPath();
            string txtPath = Path.Combine(dataFolder, "PS2.Games.txt");

            // Fast O(1) lookup for games already in DB with this platform & download action
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

            // Only scrape the website if the TXT file does not exist
            if (!File.Exists(txtPath))
            {
                logger.Info($"[Sony_PS2_Games] Scraping games from: {Sony_PS2_Games_BaseUrl}");

                string pageContent = await Myrient_LoadPageContent(Sony_PS2_Games_BaseUrl).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(pageContent))
                {
                    logger.Warn("[Sony_PS2_Games] Failed to retrieve main page content.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Sony_PS2_Games] Page content retrieved successfully ({pageContent.Length} characters).");

                var links = Myrient_ParseLinks(pageContent)?
                    .Where(link => link.Item1.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (links == null || links.Length == 0)
                {
                    logger.Info("[Sony_PS2_Games] No valid game links found.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Sony_PS2_Games] Found {links.Length} PS2 game links.");

                // Write all found games to the TXT file
                using (var writer = new StreamWriter(txtPath, false))
                {
                    foreach (var link in links)
                    {
                        string text = link.Item2;
                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        string cleanName = Myrient_CleanGameName(text).Replace(".zip", "").Trim();
                        if (string.IsNullOrEmpty(cleanName))
                            cleanName = fallbackRegex.Replace(text, "$1").Replace('-', ' ').Trim();
                        if (string.IsNullOrEmpty(cleanName))
                            continue;

                        string url = link.Item1.Trim();
                        writer.WriteLine($"Name: \"{cleanName}\", Url: \"{url}\"");
                    }
                }
                logger.Info($"[Sony_PS2_Games] Wrote all games to TXT cache: {txtPath}");
            }
            else
            {
                logger.Info($"[Sony_PS2_Games] Skipping web scrape, using TXT cache: {txtPath}");
            }

            // Always load from TXT file
            var results = new List<GameMetadata>();
            if (!File.Exists(txtPath))
            {
                logger.Warn($"[Sony_PS2_Games] TXT cache file missing: {txtPath}");
                return results;
            }

            var lines = File.ReadAllLines(txtPath);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                if (!match.Success)
                    continue;

                var cleanName = match.Groups[1].Value.Trim();
                string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                // O(1) skip if game already present in Playnite DB with Download: Myrient action and PS2 platform
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    continue;

                var metadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = uniqueKey.ToLower(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = downloadActionName,
                    Type = GameActionType.URL,
                    Path = Sony_PS2_Games_BaseUrl, // Always use the base/folder URL for Playnite action
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

                results.Add(metadata);
            }

            logger.Info($"[Sony_PS2_Games] Import complete. New games added: {results.Count}");
            return results;
        }
        private string PS2_CleanGameName(string name)
        {
            // Remove unwanted characters and trim whitespace
            var cleanName = name.Trim();

            // Remove file extension (.zip), as PS2 games are stored in ZIP format
            cleanName = Regex.Replace(cleanName, @"\.zip$", "", RegexOptions.IgnoreCase);

            // Remove any region tags (e.g., "(Europe)", "(USA)", "(Japan)")
            cleanName = Regex.Replace(cleanName, @"\s*\(.*?\)$", "", RegexOptions.IgnoreCase);

            return cleanName;
        }

        // ----------- Sega Saturn -----------
        private async Task<List<GameMetadata>> Myrient_Sega_Saturn_ScrapeStaticPage()
        {
            const string platformName = "Sega Saturn";
            const string downloadActionName = "Download: Myrient";
            string dataFolder = GetPluginUserDataPath();
            string txtPath = Path.Combine(dataFolder, "SegaSaturn.Games.txt");

            // Fast O(1) lookup for games already in DB with this platform & download action
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

            // Only scrape the website if the TXT file does not exist
            if (!File.Exists(txtPath))
            {
                logger.Info($"[Sega_Saturn_Games] Scraping games from: {Sega_Saturn_Games_BaseUrl}");

                string pageContent = await Myrient_LoadPageContent(Sega_Saturn_Games_BaseUrl).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(pageContent))
                {
                    logger.Warn("[Sega_Saturn_Games] Failed to retrieve main page content.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Sega_Saturn_Games] Page content retrieved successfully ({pageContent.Length} characters).");

                // Extract all .zip or .7z links
                var saturnLinks = Regex.Matches(pageContent, "<a[^>]+href=[\"']([^\"']+\\.(zip|7z))[\"'][^>]*>(.*?)<\\/a>", RegexOptions.IgnoreCase)
                    .Cast<Match>()
                    .Select(m => new
                    {
                        Href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value),
                        Text = m.Groups[3].Value
                    })
                    .ToList();

                if (saturnLinks.Count == 0)
                {
                    logger.Info("[Sega_Saturn_Games] No valid .zip/.7z links found.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Sega_Saturn_Games] Found {saturnLinks.Count} Sega Saturn .zip/.7z links.");

                // Write all found games to the TXT file
                using (var writer = new StreamWriter(txtPath, false))
                {
                    foreach (var link in saturnLinks)
                    {
                        string text = link.Text;
                        if (string.IsNullOrWhiteSpace(text))
                            text = Path.GetFileNameWithoutExtension(link.Href);

                        string cleanName = Regex.Replace(text, @"\s*\(.*?\)", "");
                        cleanName = Regex.Replace(cleanName, @"\s*v\d+$", "", RegexOptions.IgnoreCase);
                        cleanName = Regex.Replace(cleanName, "\\.(zip|7z)$", "", RegexOptions.IgnoreCase);
                        cleanName = cleanName.Trim();

                        if (string.IsNullOrEmpty(cleanName))
                            continue;

                        string url = link.Href.Trim();
                        writer.WriteLine($"Name: \"{cleanName}\", Url: \"{url}\"");
                    }
                }
                logger.Info($"[Sega_Saturn_Games] Wrote all games to TXT cache: {txtPath}");
            }
            else
            {
                logger.Info($"[Sega_Saturn_Games] Skipping web scrape, using TXT cache: {txtPath}");
            }

            // Always load from TXT file
            var results = new List<GameMetadata>();
            if (!File.Exists(txtPath))
            {
                logger.Warn($"[Sega_Saturn_Games] TXT cache file missing: {txtPath}");
                return results;
            }

            var lines = File.ReadAllLines(txtPath);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                if (!match.Success)
                    continue;

                var cleanName = match.Groups[1].Value.Trim();
                string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                // O(1) skip if game already present in Playnite DB with Download: Myrient action and Sega Saturn platform
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    continue;

                var metadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = uniqueKey.ToLowerInvariant(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = downloadActionName,
                    Type = GameActionType.URL,
                    Path = Sega_Saturn_Games_BaseUrl, // Always base URL for Playnite action
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

                results.Add(metadata);
            }

            logger.Info($"[Sega_Saturn_Games] Import complete. New games added: {results.Count}");
            return results;
        }
        private List<string> FindSegaSaturnGameRoms(string gameName)
        {
            var romPaths = new List<string>();
            var searchExtensions = new[] { ".cue", ".bin", ".iso", ".img", ".zip", ".7z" }; // Common Saturn formats
            var searchDirectory = "Roms\\Sega - Saturn\\Games";

            foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                    if (System.IO.Directory.Exists(rootPath))
                    {
                        var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                            .Where(file => searchExtensions.Any(ext =>
                                file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

                        romPaths.AddRange(files);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Error searching Sega Saturn ROMs in drive {drive.Name}: {ex.Message}");
                }
            }

            return romPaths;
        }

        // ----------- Sega Dreamcast -----------
        private async Task<List<GameMetadata>> Myrient_Sega_Dreamcast_ScrapeStaticPage()
        {
            const string platformName = "Sega Dreamcast";
            const string downloadActionName = "Download: Myrient";
            string dataFolder = GetPluginUserDataPath();
            string txtPath = Path.Combine(dataFolder, "Dreamcast.Games.txt");

            // Fast O(1) lookup for games already in DB with this platform & download action
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

            // Only scrape the website if the TXT file does not exist
            if (!File.Exists(txtPath))
            {
                logger.Info($"[Sega_Dreamcast_Games] Scraping games from: {Sega_Dreamcast_Games_BaseUrl}");

                string pageContent = await Myrient_LoadPageContent(Sega_Dreamcast_Games_BaseUrl).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(pageContent))
                {
                    logger.Warn("[Sega_Dreamcast_Games] Failed to retrieve main page content.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Sega_Dreamcast_Games] Page content retrieved successfully ({pageContent.Length} characters).");

                // Extract all .zip or .7z links
                var dreamcastLinks = Regex.Matches(pageContent, "<a[^>]+href=[\"']([^\"']+\\.(zip|7z))[\"'][^>]*>(.*?)<\\/a>", RegexOptions.IgnoreCase)
                    .Cast<Match>()
                    .Select(m => new
                    {
                        Href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value),
                        Text = m.Groups[3].Value
                    })
                    .ToList();

                if (dreamcastLinks.Count == 0)
                {
                    logger.Info("[Sega_Dreamcast_Games] No valid .zip/.7z links found.");
                    return new List<GameMetadata>();
                }
                logger.Info($"[Sega_Dreamcast_Games] Found {dreamcastLinks.Count} Sega Dreamcast .zip/.7z links.");

                // Write all found games to the TXT file
                using (var writer = new StreamWriter(txtPath, false))
                {
                    foreach (var link in dreamcastLinks)
                    {
                        string text = link.Text;
                        if (string.IsNullOrWhiteSpace(text))
                            text = Path.GetFileNameWithoutExtension(link.Href);

                        string cleanName = Regex.Replace(text, @"\s*\(.*?\)", "");
                        cleanName = Regex.Replace(cleanName, @"\s*v\d+$", "", RegexOptions.IgnoreCase);
                        cleanName = Regex.Replace(cleanName, "\\.(zip|7z)$", "", RegexOptions.IgnoreCase);
                        cleanName = cleanName.Trim();

                        if (string.IsNullOrEmpty(cleanName))
                            continue;

                        string url = link.Href.Trim();
                        writer.WriteLine($"Name: \"{cleanName}\", Url: \"{url}\"");
                    }
                }
                logger.Info($"[Sega_Dreamcast_Games] Wrote all games to TXT cache: {txtPath}");
            }
            else
            {
                logger.Info($"[Sega_Dreamcast_Games] Skipping web scrape, using TXT cache: {txtPath}");
            }

            // Always load from TXT file
            var results = new List<GameMetadata>();
            if (!File.Exists(txtPath))
            {
                logger.Warn($"[Sega_Dreamcast_Games] TXT cache file missing: {txtPath}");
                return results;
            }

            var lines = File.ReadAllLines(txtPath);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                if (!match.Success)
                    continue;

                var cleanName = match.Groups[1].Value.Trim();
                string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                // O(1) skip if game already present in Playnite DB with Download: Myrient action and Dreamcast platform
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    continue;

                var metadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = uniqueKey.ToLowerInvariant(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = downloadActionName,
                    Type = GameActionType.URL,
                    Path = Sega_Dreamcast_Games_BaseUrl, // Always base URL for Playnite action
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

                results.Add(metadata);
            }

            logger.Info($"[Sega_Dreamcast_Games] Import complete. New games added: {results.Count}");
            return results;
        }
        private List<string> FindSegaDreamcastGameRoms(string gameName)
        {
            var romPaths = new List<string>();
            var searchExtensions = new[] { ".cdi", ".gdi", ".bin", ".cue", ".chd", ".zip", ".7z" }; // Common Dreamcast formats
            var searchDirectory = "Roms\\Sega - Dreamcast\\Games";

            foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    var rootPath = System.IO.Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                    if (System.IO.Directory.Exists(rootPath))
                    {
                        var files = System.IO.Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories)
                            .Where(file => searchExtensions.Any(ext =>
                                file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

                        romPaths.AddRange(files);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Error searching Sega Dreamcast ROMs in drive {drive.Name}: {ex.Message}");
                }
            }

            return romPaths;
        }
        // ----------- Nintendo Switch Scraper & ROM Matcher (Rewritten, Unified Normalization) -----------

        private static string NormalizeSwitchGameName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;
            return new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        private async Task<List<GameMetadata>> Myrient_Nintendo_Switch_ScrapeStaticPage()
        {
            const string platformName = "Nintendo Switch";
            const string downloadActionName = "Download: Myrient";
            string dataFolder = GetPluginUserDataPath();
            string txtPath = Path.Combine(dataFolder, "Switch.Games.txt");
            string baseUrl = "https://nswdl.com/switch-posts/";

            var results = new List<GameMetadata>();

            if (!File.Exists(txtPath))
            {
                logger.Warn($"[Switch_Games] TXT file missing: {txtPath}");
                return results;
            }

            // Load all local ROMs and build a normalized map for fast matching
            var allRoms = FindNintendoSwitchGameRoms();
            var romMap = allRoms
                .GroupBy(r => NormalizeSwitchGameName(Path.GetFileNameWithoutExtension(r)))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Load all existing Playnite Switch games (by normalized name)
            var existingGamesByNormName = PlayniteApi.Database.Games
                .Where(g => g.Platforms != null && g.Platforms.Any(p => p.Name == platformName))
                .GroupBy(g => NormalizeSwitchGameName(g.Name))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var line in File.ReadAllLines(txtPath))
            {
                var match = Regex.Match(line, @"Name: ""(.+)"", Url: ""(.+)""");
                if (!match.Success)
                    continue;

                var name = match.Groups[1].Value.Trim();
                var url = match.Groups[2].Value.Trim();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
                    continue;

                string gameId = url.ToLowerInvariant();
                if (seenIds.Contains(gameId))
                    continue;
                seenIds.Add(gameId);

                string normName = NormalizeSwitchGameName(name);

                // 100% exact match only for normalized name
                List<string> matchingRoms = romMap.TryGetValue(normName, out var files)
                    ? files
                    : new List<string>(); // No fallback/fuzzy

                // ---- If existing game, update its InstallDirectory and Play Action ----
                if (existingGamesByNormName.TryGetValue(normName, out var existingGames) && existingGames.Count > 0 && matchingRoms.Any())
                {
                    foreach (var exGame in existingGames)
                    {
                        bool updated = false;
                        // Set IsInstalled
                        if (exGame.IsInstalled != true)
                        {
                            exGame.IsInstalled = true;
                            updated = true;
                        }
                        // Set InstallDirectory
                        var installDir = Path.GetDirectoryName(matchingRoms.First());
                        if (!string.IsNullOrEmpty(installDir) && exGame.InstallDirectory != installDir)
                        {
                            exGame.InstallDirectory = installDir;
                            updated = true;
                        }
                        // Set ROMs (keep your type)
                        var romObjs = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();
                        exGame.Roms = new System.Collections.ObjectModel.ObservableCollection<Playnite.SDK.Models.GameRom>(romObjs);

                        // Add Play action (Yuzu preferred, then Ryujinx)
                        var emulator = PlayniteApi.Database.Emulators
                            .FirstOrDefault(e => e.Name.Equals("Yuzu", StringComparison.OrdinalIgnoreCase)) ??
                            PlayniteApi.Database.Emulators
                            .FirstOrDefault(e => e.Name.Equals("Ryujinx", StringComparison.OrdinalIgnoreCase));

                        if (emulator != null && emulator.BuiltinProfiles != null && emulator.BuiltinProfiles.Any())
                        {
                            var profile = emulator.BuiltinProfiles.FirstOrDefault(p =>
                                p.GetType().GetProperty("Name") != null &&
                                string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "Default", StringComparison.OrdinalIgnoreCase)
                            );
                            if (profile == null) profile = emulator.BuiltinProfiles.First();
                            if (exGame.GameActions == null)
                                exGame.GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>();

                            // Add Play Action if not already present
                            if (exGame.GameActions.All(a => a.Type != GameActionType.Emulator))
                            {
                                exGame.GameActions.Add(new GameAction
                                {
                                    Name = "Play",
                                    Type = GameActionType.Emulator,
                                    EmulatorId = emulator.Id,
                                    EmulatorProfileId = profile.Id,
                                    Path = matchingRoms.First(),
                                    IsPlayAction = true
                                });
                                updated = true;
                            }
                        }
                        if (updated)
                            PlayniteApi.Database.Games.Update(exGame);
                    }
                    logger.Info($"[Switch_Match] Updated existing game: '{name}' with install dir, play action, and ROMs.");
                    continue;
                }

                // --------- Otherwise, add as new game (like your original logic) ---------
                var game = new GameMetadata
                {
                    Name = name,
                    GameId = gameId,
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = downloadActionName,
                    Type = GameActionType.URL,
                    Path = baseUrl,
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms
                        .Select(r => new GameRom { Name = Path.GetFileName(r), Path = r })
                        .ToList();

                    // Emulator Play Action (only add one, Yuzu preferred, then Ryujinx)
                    var emulator = PlayniteApi.Database.Emulators
                        .FirstOrDefault(e => e.Name.Equals("Yuzu", StringComparison.OrdinalIgnoreCase)) ??
                        PlayniteApi.Database.Emulators
                        .FirstOrDefault(e => e.Name.Equals("Ryujinx", StringComparison.OrdinalIgnoreCase));

                    if (emulator != null && emulator.BuiltinProfiles != null && emulator.BuiltinProfiles.Any())
                    {
                        var profile = emulator.BuiltinProfiles.FirstOrDefault(p =>
                            p.GetType().GetProperty("Name") != null &&
                            string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "Default", StringComparison.OrdinalIgnoreCase)
                        );
                        if (profile == null) profile = emulator.BuiltinProfiles.First();
                        if (game.GameActions.All(a => a.Type != GameActionType.Emulator))
                        {
                            game.GameActions.Add(new GameAction
                            {
                                Name = "Play",
                                Type = GameActionType.Emulator,
                                EmulatorId = emulator.Id,
                                EmulatorProfileId = profile.Id,
                                Path = matchingRoms.First(),
                                IsPlayAction = true
                            });
                        }
                    }
                }

                results.Add(game);

                // Debug log for matching
                logger.Info($"[Switch_Match] TXT: '{name}' -> Norm: '{normName}' | ROMs matched: {matchingRoms.Count}");
                foreach (var rom in matchingRoms)
                    logger.Info($"[Switch_Match]   ROM file: '{Path.GetFileName(rom)}'");
            }

            logger.Info($"[Switch_Games] Loaded {results.Count} games from TXT (ROM matching done).");
            return results;
        }

        private List<string> FindNintendoSwitchGameRoms()
        {
            var romPaths = new List<string>();
            var searchExtensions = new[] { ".nsp", ".xci", ".zip", ".7z" };
            var searchDirectory = Path.Combine("Roms", "Nintendo - Switch", "Games");

            foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    var rootPath = Path.Combine(drive.RootDirectory.FullName, searchDirectory);
                    if (Directory.Exists(rootPath))
                    {
                        var files = Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
                            .Where(file => searchExtensions.Any(ext =>
                                file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));
                        romPaths.AddRange(files);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Error searching Nintendo Switch ROMs in drive {drive.Name}: {ex.Message}");
                }
            }

            logger.Info($"[Switch_Roms] Total Switch ROMs found: {romPaths.Count}");

            return romPaths;
        }


        private async Task<int> GetLatestPageNumber()
        {
            string homePageContent = await LoadPageContent("https://fitgirl-repacks.site/all-my-repacks-a-z/");
            if (string.IsNullOrWhiteSpace(homePageContent))
            {
                logger.Warn("No content returned for FitGirl homepage.");
                return 1; // Fallback if page content isn't loaded
            }

            int latestPage = 1;
            Regex paginationRegex = new Regex(@"lcp_page0=(\d+)", RegexOptions.IgnoreCase);

            var matches = paginationRegex.Matches(homePageContent);
            foreach (Match match in matches)
            {
                if (int.TryParse(match.Groups[1].Value, out int pageNumber) && pageNumber > latestPage)
                {
                    latestPage = pageNumber;
                }
            }


            return latestPage;
        }

        private bool IsValidGameLink(string href)
        {
            var nonGameUrls = new List<string>
    {
        "https://fitgirl-repacks.site/",
        "about:blank#search-container",
        "about:blank#content",
        "https://fitgirl-repacks.site/pop-repacks/",
        "https://fitgirl-repacks.site/popular-repacks/",
        "https://fitgirl-repacks.site/popular-repacks-of-the-year/",
        "https://fitgirl-repacks.site/all-playstation-3-emulated-repacks-a-z/",
        "https://fitgirl-repacks.site/all-switch-emulated-repacks-a-z/",
        "https://fitgirl-repacks.site/category/updates-digest/",
        "https://fitgirl-repacks.site/feed/",
        "http://fitgirl-repacks.site/feed/",
        "https://fitgirl-repacks.site/donations/",
        "http://fitgirl-repacks.site/donations/",
        "https://fitgirl-repacks.site/faq/",
        "https://fitgirl-repacks.site/contacts/",
        "https://fitgirl-repacks.site/repacks-troubleshooting/",
        "https://fitgirl-repacks.site/updates-list/",
        "https://fitgirl-repacks.site/all-my-repacks-a-z/",
        "https://fitgirl-repacks.site/games-with-my-personal-pink-paw-award/",
        "https://wordpress.org/",
        "https://fitgirl-repacks.site/all-my-repacks-a-z/#comment",
        "http://www.hairstylesvip.com"
    };

            if (Regex.IsMatch(href, @"^https://fitgirl-repacks.site/\d{4}/\d{2}/$") ||
                Regex.IsMatch(href, @"^https://fitgirl-repacks.site/all-my-repacks-a-z/\?lcp_page0=\d+#lcp_instance_0$") ||
                href.Contains("#comment-") ||
                href.Contains("https://www.hairstylesvip.com/") ||
                nonGameUrls.Contains(href))
            {
                return false;
            }

            return true;
        }

        private async Task<string> LoadPageContent(string url)
        {
            using (var httpClient = new HttpClient())
            {
                try
                {
                    return await httpClient.GetStringAsync(url);
                }
                catch (Exception ex)
                {
                    logger.Error($"Error loading content from {url}: {ex.Message}");
                    return string.Empty;
                }
            }
        }
                
        private List<Tuple<string, string>> ParseLinks(string pageContent)
        {
            var links = new List<Tuple<string, string>>();

            // Regex pattern to extract hyperlinks and their display text
            string pattern = @"<a\s+(?:[^>]*?\s+)?href=[""'](?<url>[^""']+)[""'].*?>(?<text>.*?)</a>";
            MatchCollection matches = Regex.Matches(pageContent, pattern);

            foreach (Match match in matches)
            {
                string href = match.Groups["url"].Value.Trim();
                string text = Regex.Replace(match.Groups["text"].Value, "<.*?>", string.Empty).Trim();

                // Ensure both URL and text are non-empty before adding to the list
                if (!string.IsNullOrWhiteSpace(href) && !string.IsNullOrWhiteSpace(text))
                {
                    links.Add(Tuple.Create(href, WebUtility.HtmlDecode(text))); // Decode any HTML entities
                }
            }

            return links;
        }

        private bool IsValidGameLink(string href, string text)
        {
            var nonGameUrls = new List<string>
    {
        "https://fitgirl-repacks.site/",
        "about:blank#search-container",
        "about:blank#content",
        "https://steamrip.com/faq-steamrip/",
        "https://steamrip.com/steps-for-games-page/",
        "https://steamrip.com/top-games/#",
        "https://discord.gg/WkyjpA3Ua9",
        "https://steamrip.com/category/",
        "https://steamrip.com/about/",
        "https://steamrip.com/request-games/",
        "https://steamrip.com/privacy-policy/",
        "https://steamrip.com/terms-and-conditions/",
        "https://steamrip.com/contact-us/",
        "https://steamrip.com/category/action/",
        "https://steamrip.com/category/adventure/",
        "https://steamrip.com/category/anime/",
        "https://steamrip.com/category/horror/",
        "https://steamrip.com/category/indie/",
        "https://steamrip.com/category/multiplayer/",
        "https://steamrip.com/category/open-world/",
        "https://steamrip.com/category/racing/",
        "https://steamrip.com/category/shooting/",
        "https://steamrip.com/category/simulation/",
        "https://steamrip.com/category/sports/",
        "https://steamrip.com/category/strategy/",
        "https://steamrip.com/category/vr/",
        "https://steamrip.com/about/",
        "https://steamrip.com/request-games/",
        "https://steamrip.com/privacy-policy/",
        "https://steamrip.com/terms-and-conditions/",
        "https://steamrip.com/contact-us/",
        "https://www.magipack.games/tag/"
    };

            var nonGameTexts = new List<string>
    {
        "About",
        "Horror",
        "Action",
        "Adventure",
        "Anime",
        "Indie",
        "Multiplayer",
        "Open World",
        "Racing",
        "Shooting",
        "Simulation",
        "Sports",
        "Strategy",
        "Virtual Reality",
        "Request Games",
        "Privacy Policy",
        "Terms and Conditions",
        "Contact Us",
        "Reddit",
        "Back to top button",
        "Categories",
        "Terms & Conditions",
        "Discord",
        "Home",
        "Search for",
        "Top Games",
        "Recent Updates",
        "Games List",
        "Close",
        "All FAQs",
        "Menu",
        "How to Run Games",
        "FAQ",
        "Switch skin",
        "Shooters",
        "Shooter",
    };

            // Exclude if the URL matches specific patterns or is in the list of non-game URLs,
            // or if the supplied text contains any of the non-game texts.
            if (Regex.IsMatch(href, @"^https://steamrip.com\d{4}/\d{2}/$") ||
                Regex.IsMatch(href, @"^https://steamrip.com/\?lcp_page0=\d+#lcp_instance_0$") ||
                nonGameUrls.Contains(href) ||
                nonGameTexts.Any(nonGameText => text.Contains(nonGameText)))
            {
                return false;
            }

            return true;
        }

        private string ExtractVersionNumber(string text)
        {
            var versionMatch = Regex.Match(text, @"\((.*?)\)");
            return versionMatch.Success ? versionMatch.Groups[1].Value : "0";
        }

        public static string CleanGameName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // Normalize apostrophe/quote/dash types to ASCII
            string cleanName = name
                .Replace("’", "'")
                .Replace("‘", "'")
                .Replace("“", "\"")
                .Replace("”", "\"")
                .Replace("–", "-")
                .Replace("—", "-");

            // Remove registered, trademark, copyright symbols
            cleanName = cleanName.Replace("®", "").Replace("™", "").Replace("©", "");

            // Remove trailing update/build/hotfix info (after dash, en-dash, or plus)
            cleanName = Regex.Replace(cleanName, @"[\-\–\+]\s*(update|hotfix).*$", "", RegexOptions.IgnoreCase).Trim();

            // Remove version numbers, build info, Free Download markers, .zip endings
            cleanName = Regex.Replace(cleanName, @"\s*v[\d\.]+.*", "", RegexOptions.IgnoreCase);
            cleanName = Regex.Replace(cleanName, @"\s*Build\s*\d+.*", "", RegexOptions.IgnoreCase);
            cleanName = Regex.Replace(cleanName, @"\s*Free Download.*", "", RegexOptions.IgnoreCase);
            cleanName = Regex.Replace(cleanName, @"\.zip$", "", RegexOptions.IgnoreCase);

            // Remove text in parentheses or brackets (including trailing open parens/brackets)
            cleanName = Regex.Replace(cleanName, @"[\[\(].*?[\]\)]", "", RegexOptions.IgnoreCase).Trim();
            cleanName = Regex.Replace(cleanName, @"[\[\(].*$", "", RegexOptions.IgnoreCase).Trim();

            // Replace common HTML-encoded characters
            cleanName = cleanName.Replace("&#8217;", "'")
                                 .Replace("&#8211;", "-")
                                 .Replace("&#8216;", "'")
                                 .Replace("&#038;", "&")
                                 .Replace("&#8220;", "\"")
                                 .Replace("&#8221;", "\"")
                                 .Replace("&amp;", "&");

            // Remove specific unwanted phrases/patterns
            var patternsToRemove = new string[]
            {
        @"\s*\+\s*\d*\s*Update\s*\d*",
        @"\s*\+\s*\d*\s*DLCs?",
        @"\s*\+\s*\d*\s*Fix",
        @"\s*\+\s*\d*\s*Soundtrack",
        @"\s*\+\s*\d*\s*Online",
        @"\s*\+\s*\d*\s*Multiplayer",
        @"\s*Digital Deluxe Edition",
        @"\s*Ultimate Fishing Bundle",
        @"\s*All previous DLCs",
        @"\s*Bonus Content",
        @"\s*OST Bundle",
        @"\s*CrackFix",
        @"\s*\d+\s*DLCs?",
        @"\s*HotFix",
        @"\s*Game & Soundtrack Bundle"
            };
            foreach (var pattern in patternsToRemove)
            {
                cleanName = Regex.Replace(cleanName, pattern, "", RegexOptions.IgnoreCase);
            }

            // 1. Replace "number : number" (with/without spaces) with "number-number" (e.g. "years 5: 7" => "years 5-7")
            cleanName = Regex.Replace(cleanName, @"(?<=\b\d{1,3})\s*:\s*(?=\d{1,3}\b)", "-", RegexOptions.IgnoreCase);

            // 2. Replace dashes (hyphen or en-dash) between words with ": " (colon+space), but not between digits (so "5-7" stays)
            cleanName = Regex.Replace(cleanName, @"(?<=[a-zA-Z])\s*[\-–]\s*(?=[a-zA-Z])", ": ", RegexOptions.IgnoreCase);

            // Remove comma and file size after comma (e.g. "Game, 10 GB")
            int commaIndex = cleanName.IndexOf(',');
            if (commaIndex > 0)
            {
                string afterComma = cleanName.Substring(commaIndex + 1).Trim();
                if (Regex.IsMatch(afterComma, @"^\d+(\.\d+)?\s*(gb|mb)", RegexOptions.IgnoreCase))
                {
                    cleanName = cleanName.Substring(0, commaIndex).Trim();
                }
            }

            // If name contains a slash, keep only the part before the first slash
            int slashIndex = cleanName.IndexOf('/');
            if (slashIndex > 0)
            {
                cleanName = cleanName.Substring(0, slashIndex).Trim();
            }

            // Remove all apostrophes for dedupe purposes
            cleanName = cleanName.Replace("'", "");

            // Final trim for stray punctuation/whitespace
            cleanName = cleanName.Trim(' ', '-', '–', '+', ',', ':');

            // Collapse multiple spaces
            cleanName = Regex.Replace(cleanName, @"\s+", " ").Trim();

            // --- Title Case/Word Splitting Fix ---

            // If the name looks like "callofdutyworldatwar", try to split into proper words and title case
            if (Regex.IsMatch(cleanName, @"^[a-z0-9]+$", RegexOptions.IgnoreCase) && !cleanName.Contains(" "))
            {
                // Try to split based on known words (expand list as needed)
                string[] knownWords = new[]
                {
            "call", "of", "duty", "world", "at", "war", "black", "ops", "modern", "warfare", "infinite", "advanced",
            "battlefield", "star", "wars", "the", "elder", "scrolls", "fallout", "new", "vegas", "dragon", "age",
            "mass", "effect", "half", "life", "portal", "knights", "shadow", "mordor"
            // ...add more franchises as needed
        };

                string orig = cleanName.ToLowerInvariant();
                var result = new StringBuilder();
                int i = 0;
                while (i < orig.Length)
                {
                    string match = null;
                    foreach (var word in knownWords.OrderByDescending(w => w.Length))
                    {
                        if (orig.Substring(i).StartsWith(word))
                        {
                            match = word;
                            break;
                        }
                    }
                    if (match != null)
                    {
                        if (result.Length > 0)
                            result.Append(' ');
                        result.Append(char.ToUpper(match[0]) + match.Substring(1));
                        i += match.Length;
                    }
                    else
                    {
                        // If no known word matched, just append the rest as-is and break (prevents "W O L F E N S T E I N" and "1 1 F")
                        if (result.Length > 0)
                            result.Append(' ');
                        // If the rest is all upper, just TitleCase it (prevents "W O L F E N S T E I N")
                        string rest = orig.Substring(i);
                        if (Regex.IsMatch(rest, @"^[A-Z0-9]+$", RegexOptions.IgnoreCase))
                            result.Append(char.ToUpper(rest[0]) + rest.Substring(1));
                        else
                            result.Append(rest);
                        break;
                    }
                }
                cleanName = result.ToString();
            }
            else if (Regex.IsMatch(cleanName, @"^[A-Z0-9]+$") && !cleanName.Contains(" "))
            {
                // If all upper, just TitleCase it (preserves "WOLFENSTEIN" -> "Wolfenstein", "11F" -> "11F")
                cleanName = char.ToUpper(cleanName[0]) + cleanName.Substring(1).ToLower();
            }
            else
            {
                // Proper title casing (preserve common lowercase words in the middle)
                cleanName = ToTitleCasePreserveSmallWords(cleanName);
            }

            return cleanName;
        }

        /// <summary>
        /// Title cases string but preserves lowercase for articles/prepositions in the middle.
        /// </summary>
        private static string ToTitleCasePreserveSmallWords(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            string[] smallWords = { "a", "an", "the", "and", "but", "or", "for", "nor", "on", "at", "to", "from", "by", "of", "in", "with" };
            string[] words = input.ToLowerInvariant().Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                // Always capitalize the first and last word
                if (i == 0 || i == words.Length - 1 || !smallWords.Contains(words[i]))
                {
                    words[i] = char.ToUpperInvariant(words[i][0]) + (words[i].Length > 1 ? words[i].Substring(1) : "");
                }
            }
            return string.Join(" ", words.Where(w => !string.IsNullOrWhiteSpace(w)));
        }


        // Existing normalization, unchanged
        public static string NormalizeGameName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // Remove diacritics (accents)
            string normalized = name.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            normalized = sb.ToString().Normalize(NormalizationForm.FormC);

            // Lowercase
            normalized = normalized.ToLowerInvariant();

            // Remove registered, trademark, copyright symbols
            normalized = normalized.Replace("®", "").Replace("™", "").Replace("©", "");

            // Canonicalize "and": replace all standalone "and" or "&" with "and"
            normalized = Regex.Replace(normalized, @"\b(and|&)\b", "and", RegexOptions.IgnoreCase);

            // Canonicalize all word-joining dashes/hyphens/en-dashes to a space
            normalized = Regex.Replace(normalized, @"(?<=\w)[\-\–—](?=\w)", " ");

            // Canonicalize "GOTY", "goty", "game of the year" to "game of the year"
            normalized = Regex.Replace(normalized, @"\b(goty|game of the year)\b", "game of the year", RegexOptions.IgnoreCase);

            // Remove all apostrophes and quotes (straight and curly)
            normalized = normalized.Replace("'", "").Replace("’", "").Replace("‘", "").Replace("\"", "");

            // Remove unwanted punctuation but PRESERVE spaces
            normalized = Regex.Replace(normalized, @"[^\w\s]", "");

            // Collapse multiple spaces to single space and trim
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            // Canonicalize some known variants (add more as needed)
            normalized = Regex.Replace(normalized, @"marvels", "marvel", RegexOptions.IgnoreCase);

            // Roman numerals to numbers (excluding "I")
            normalized = Regex.Replace(normalized, @"\bX\b", "10", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bIX\b", "9", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bVIII\b", "8", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bVII\b", "7", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bVI\b", "6", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bV\b", "5", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bIV\b", "4", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bIII\b", "3", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bII\b", "2", RegexOptions.IgnoreCase);

            return normalized;
        }

        public static string SanitizePath(string path)
        {
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }

        public static bool IsDuplicate(GameMetadata gameMetadata, IPlayniteAPI PlayniteApi, Guid Id)
        {
            return PlayniteApi.Database.Games.Any(existing =>
                existing.PluginId == Id &&
                existing.Platforms != null &&
                existing.Platforms.Any(p =>
                    (p.GetType().GetProperty("Name") != null &&
                     string.Equals((string)p.GetType().GetProperty("Name").GetValue(p), "PC (Windows)", StringComparison.OrdinalIgnoreCase))
                    || p.ToString().Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)
                ) &&
                existing.Name.Equals(gameMetadata.Name, StringComparison.OrdinalIgnoreCase));
        }

        



        // Install Part 

        public override IEnumerable<InstallController> GetInstallActions(GetInstallActionsArgs args)
        {
            if (args.Game.PluginId == Id)
            {
                yield return new LocalInstallController(args.Game, this);
            }
        }

        public class LocalInstallController : InstallController
        {
            private readonly GameStore pluginInstance;
            private readonly IPlayniteAPI playniteApi;

            public LocalInstallController(Game game, GameStore instance) : base(game)
            {
                pluginInstance = instance;
                playniteApi = pluginInstance.PlayniteApi;
                Name = "Install using SteamRip";
            }

            // --- Main Install Method ---
            public override async void Install(InstallActionArgs args)
            {
                try
                {
                    // Only handle installation for this plugin's games
                    if (Game.PluginId != pluginInstance.Id)
                    {
                        LogToInstall($"Skipping installation update for {Game.Name}. It does not belong to Game Store.");
                        return;
                    }

                    // 1. Steam Ownership Handling
                    bool isSteamOwned = Game.Features?.Any(f => f.Name.Equals("[Own: Steam]", StringComparison.OrdinalIgnoreCase)) == true;
                    if (isSteamOwned)
                    {
                        var yesOption = new MessageBoxOption("Yes", true, false);
                        var noOption = new MessageBoxOption("No", false, true);
                        var installSteam = playniteApi.Dialogs.ShowMessage(
                            "This game is owned on Steam. Do you want to install using SteamCMD?",
                            "SteamCMD Install",
                            MessageBoxImage.Question,
                            new List<MessageBoxOption> { yesOption, noOption });

                        if (installSteam == yesOption)
                        {
                            await HandleSteamCMDInstall();
                            return;
                        }
                        // If No, let user proceed to repack/download logic below
                    }

                    // 2. Local Repack Candidate
                    var (candidatePath, isArchive, fileSize, candidateFound) = await SearchForLocalRepackAsync(Game.Name);
                    if (candidateFound)
                    {
                        if (string.IsNullOrEmpty(candidatePath))
                            return;

                        if (isArchive)
                        {
                            string selectedDrive = ShowDriveSelectionDialog(fileSize);
                            if (string.IsNullOrEmpty(selectedDrive))
                                return;

                            string gamesFolder = Path.Combine($"{selectedDrive}:", "Games");
                            Directory.CreateDirectory(gamesFolder);
                            string targetInstallDir = Path.Combine(gamesFolder, Game.Name);
                            Directory.CreateDirectory(targetInstallDir);

                            string sevenZipExe = Get7ZipPath();
                            if (string.IsNullOrEmpty(sevenZipExe))
                            {
                                playniteApi.Dialogs.ShowErrorMessage("7-Zip not found!", "Error");
                                return;
                            }

                            string arguments = $"x \"{candidatePath}\" -o\"{targetInstallDir}\" -y";
                            var psi = new ProcessStartInfo
                            {
                                FileName = sevenZipExe,
                                Arguments = arguments,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                WindowStyle = ProcessWindowStyle.Hidden
                            };

                            await Task.Run(() =>
                            {
                                using (var proc = Process.Start(psi))
                                {
                                    proc.WaitForExit();
                                }
                            });
                            LogToInstall("Extracted archive candidate to: " + targetInstallDir);

                            Game.InstallDirectory = targetInstallDir;
                            playniteApi.Database.Games.Update(Game);
                            LogToInstall("Updated InstallDir to: " + targetInstallDir + " (archive candidate).");
                            UpdateGameActionsAndStatus(Game, GetPluginUserDataPathLocal());
                            return;
                        }
                        else
                        {
                            string setupExePath = Path.Combine(candidatePath, "Setup.exe");
                            if (System.IO.File.Exists(setupExePath))
                            {
                                await Task.Run(() =>
                                {
                                    var psi = new ProcessStartInfo
                                    {
                                        FileName = setupExePath,
                                        UseShellExecute = false,
                                        CreateNoWindow = true
                                    };
                                    using (var proc = Process.Start(psi))
                                    {
                                        proc.WaitForExit();
                                    }
                                });
                                LogToInstall("Setup.exe finished for folder candidate: " + candidatePath);
                            }
                            else
                            {
                                LogToInstall("Setup.exe not found in candidate folder: " + candidatePath);
                            }

                            string installDir = SearchForGameInstallDirectory(Game.Name);
                            if (string.IsNullOrEmpty(installDir))
                            {
                                playniteApi.Dialogs.ShowErrorMessage("Could not locate the installation folder after running Setup.exe.", "Installation Error");
                                return;
                            }
                            LogToInstall("Found installed directory: " + installDir);

                            Game.InstallDirectory = installDir;
                            playniteApi.Database.Games.Update(Game);
                            LogToInstall("Updated InstallDir to: " + installDir + " (after running Setup.exe).");
                            UpdateGameActionsAndStatus(Game, GetPluginUserDataPathLocal());
                            return;
                        }
                    }

                    // 3. Dynamic Online Download Sources
                    var urlActions = Game.GameActions?.Where(a => a.Type == GameActionType.URL).ToList() ?? new List<GameAction>();
                    if (!urlActions.Any())
                    {
                        if (isSteamOwned)
                        {
                            playniteApi.Dialogs.ShowMessage(
                                "This Steam-owned game has no alternative download sources in this library. Please install it using SteamCMD, or add a repack/download action to this game.",
                                "No Download Source",
                                System.Windows.MessageBoxButton.OK
                            );
                            return;
                        }
                        else
                        {
                            playniteApi.Dialogs.ShowErrorMessage("No valid download sources found.", "Error");
                            return;
                        }
                    }

                    string selectedSource = ShowSourceSelectionDialog(urlActions);
                    if (string.IsNullOrEmpty(selectedSource))
                        return;

                    // Known download source handlers
                    if (selectedSource.Equals("Download: AnkerGames", StringComparison.OrdinalIgnoreCase))
                    {
                        await HandleAnkerGamesDownload();
                    }
                    else if (selectedSource.Equals("Download: SteamRip", StringComparison.OrdinalIgnoreCase))
                    {
                        await HandleSteamRipDownload();
                    }
                    else if (selectedSource.Equals("Download: FitGirl Repacks", StringComparison.OrdinalIgnoreCase))
                    {
                        await HandleFitGirlDownload();
                    }
                    else if (selectedSource.Equals("Download: Elamigos", StringComparison.OrdinalIgnoreCase))
                    {
                        await HandleElamigosDownload();
                    }
                    else if (selectedSource.Equals("Download: Magipack", StringComparison.OrdinalIgnoreCase))
                    {
                        await HandleMagipackDownload();
                    }
                    else if (selectedSource.Equals("Download: Myrient", StringComparison.OrdinalIgnoreCase))
                    {
                        await HandleMyrientDownload();
                    }
                    else if (selectedSource.Equals("Download: Dodi", StringComparison.OrdinalIgnoreCase))
                    {
                        await HandleDodiRepacksDownload();
                    }
                    else if (selectedSource.Equals("Download: Steam", StringComparison.OrdinalIgnoreCase))
                    {
                        // Steam download option in menu
                        await HandleSteamCMDInstall();
                    }
                    else
                    {
                        playniteApi.Dialogs.ShowErrorMessage("Unknown download source selected.", "Error");
                    }
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"Error during installation: {ex.Message}", "Installation Error");
                }
            }
            private string NormalizeGameName(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return string.Empty;

                // 1. Lowercase and trim.
                string normalized = name.ToLowerInvariant().Trim();

                // 2. Remove apostrophes (both straight and smart).
                normalized = normalized.Replace("’", "").Replace("'", "");

                // 3. Replace ampersands with " and ".
                normalized = normalized.Replace("&", " and ");

                // 4. Remove unwanted punctuation.
                normalized = Regex.Replace(normalized, @"[^\w\s]", " ");

                // 5. Collapse multiple spaces.
                normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

                // 6. Special rule for Marvel.
                normalized = Regex.Replace(normalized, @"^marvels\b", "marvel", RegexOptions.IgnoreCase);

                // 7. Normalize "Game of The Year Edition" variations.
                normalized = Regex.Replace(normalized, @"\bgame of (the year)( edition)?\b", "goty", RegexOptions.IgnoreCase);

                // 8. Normalize the acronym REPO.
                normalized = Regex.Replace(normalized, @"\br\.?e\.?p\.?o\b", "r.e,p.o", RegexOptions.IgnoreCase);

                // 9. Normalize FEAR.
                normalized = Regex.Replace(normalized, @"\bf\s*e\s*a\s*r\b", "fear", RegexOptions.IgnoreCase);

                // 10. Normalize Rick and Morty Virtual Rick-ality VR variants.
                normalized = Regex.Replace(normalized,
                    @"rick and morty\s*[:]?\s*virtual rickality vr",
                    "rick and morty virtual rickality vr", RegexOptions.IgnoreCase);

                // 11. Normalize common Roman numerals.
                normalized = Regex.Replace(normalized, @"\bviii\b", "8", RegexOptions.IgnoreCase);
                normalized = Regex.Replace(normalized, @"\bvii\b", "7", RegexOptions.IgnoreCase);
                normalized = Regex.Replace(normalized, @"\biv\b", "4", RegexOptions.IgnoreCase);
                normalized = Regex.Replace(normalized, @"\biii\b", "3", RegexOptions.IgnoreCase);
                normalized = Regex.Replace(normalized, @"\bvi\b", "6", RegexOptions.IgnoreCase);
                normalized = Regex.Replace(normalized, @"\bix\b", "9", RegexOptions.IgnoreCase);
                normalized = Regex.Replace(normalized, @"\bx\b", "10", RegexOptions.IgnoreCase);
                normalized = Regex.Replace(normalized, @"\bv\b", "5", RegexOptions.IgnoreCase);
                normalized = Regex.Replace(normalized, @"\bii\b", "2", RegexOptions.IgnoreCase);

                return normalized.Trim();
            }
            private string GetPluginUserDataPathLocal()
            {
                // Build the user data path under %APPDATA%\Playnite\ExtensionsData\<PluginID>
                string userDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                     "Playnite", "ExtensionsData", "55eeaffc-4d50-4d08-85fb-d8e49800d058");
                if (!Directory.Exists(userDataPath))
                {
                    Directory.CreateDirectory(userDataPath);
                }
                return userDataPath;
            }
            private string Get7ZipPath()
            {
                string sevenZipExe = "7z.exe";
                if (!System.IO.File.Exists(sevenZipExe))
                {
                    // Try default install locations.
                    sevenZipExe = @"C:\Program Files\7-Zip\7z.exe";
                    if (!System.IO.File.Exists(sevenZipExe))
                    {
                        sevenZipExe = @"C:\Program Files (x86)\7-Zip\7z.exe";
                        if (!System.IO.File.Exists(sevenZipExe))
                        {
                            return string.Empty;
                        }
                    }
                }
                return sevenZipExe;
            }
            private void CopyDirectory(string sourceDir, string destinationDir)
            {
                Directory.CreateDirectory(destinationDir);
                foreach (var file in Directory.GetFiles(sourceDir))
                {
                    string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                    System.IO.File.Copy(file, destFile, true);
                }
                foreach (var directory in Directory.GetDirectories(sourceDir))
                {
                    string destDir = Path.Combine(destinationDir, Path.GetFileName(directory));
                    CopyDirectory(directory, destDir);
                }
            }
            private string SearchForGameInstallDirectory(string gameName)
            {
                string normalizedGameName = NormalizeGameName(gameName);

                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady)
                        continue;

                    // Build the path to the "Games" folder on this drive.
                    string gamesFolder = Path.Combine(drive.RootDirectory.FullName, "Games");
                    if (!Directory.Exists(gamesFolder))
                        continue;

                    // Enumerate all subdirectories in the Games folder.
                    foreach (string folder in Directory.GetDirectories(gamesFolder))
                    {
                        string folderName = Path.GetFileName(folder);
                        // Compare normalized names.
                        if (NormalizeGameName(folderName).Equals(normalizedGameName, StringComparison.Ordinal))
                        {
                            // Check if this folder contains any executables.
                            var exeFiles = Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories);
                            if (exeFiles != null && exeFiles.Length > 0)
                            {
                                LogToInstall($"Found game installation for '{gameName}' on drive '{drive.Name}' at location '{folder}', with {exeFiles.Length} executable(s).");
                                return folder;
                            }
                            else
                            {
                                LogToInstall($"Folder '{folder}' on drive '{drive.Name}' matched game '{gameName}' but contains no .exe files.");
                            }
                        }
                    }
                }

                LogToInstall($"No installation folder found for game '{gameName}'.");
                return string.Empty;
            }


            // Method remains the same: searches for a repack candidate and returns its path, type, and file size.
            // 1. Search for a local repack candidate, extract if needed, and prompt for installation of redists.
            private async Task<(string repackPath, bool isArchive, long fileSize, bool candidateFound)> SearchForLocalRepackAsync(string gameName)
            {
                return await Task.Run(() =>
                {
                    LogToInstall("Starting SearchForLocalRepackAsync for game: " + gameName);
                    string candidatePath = null;
                    bool isArchive = false;
                    long fileSize = 0;
                    bool candidateFound = false;
                    string normalizedTarget = this.NormalizeGameName(gameName);

                    // --- Search for a candidate repack (archive file or repack folder) ---
                    foreach (DriveInfo drive in DriveInfo.GetDrives())
                    {
                        if (!drive.IsReady)
                            continue;

                        string repacksFolder = Path.Combine(drive.RootDirectory.FullName, "Repacks");
                        if (!Directory.Exists(repacksFolder))
                            continue;

                        // Look for archive files (.zip or .rar)
                        var archiveFiles = Directory.GetFiles(repacksFolder, "*.*", SearchOption.TopDirectoryOnly)
                            .Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".rar", StringComparison.OrdinalIgnoreCase));

                        foreach (var file in archiveFiles)
                        {
                            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);
                            string normalizedFileName = this.NormalizeGameName(fileNameWithoutExt);
                            if (normalizedFileName.Contains(normalizedTarget))
                            {
                                candidatePath = file;
                                isArchive = true;
                                fileSize = new FileInfo(file).Length;
                                candidateFound = true;
                                LogToInstall("Found archive candidate: " + file + " (Size: " + fileSize + " bytes)");
                                break;
                            }
                        }
                        if (candidatePath != null)
                            break;

                        // Look for repack folders (a folder that contains Setup.exe)
                        var directories = Directory.GetDirectories(repacksFolder, "*", SearchOption.TopDirectoryOnly);
                        foreach (var dir in directories)
                        {
                            string dirName = Path.GetFileName(dir);
                            string normalizedDirName = this.NormalizeGameName(dirName);
                            if (normalizedDirName.Contains(normalizedTarget))
                            {
                                string setupPath = Path.Combine(dir, "Setup.exe");
                                if (System.IO.File.Exists(setupPath))
                                {
                                    candidatePath = dir;
                                    isArchive = false;
                                    fileSize = 0;
                                    candidateFound = true;
                                    LogToInstall("Found folder candidate: " + dir);
                                    break;
                                }
                            }
                        }
                        if (candidatePath != null)
                            break;
                    }

                    // --- Menu: Ask user what to do if candidate found ---
                    string userChoice = "";
                    if (candidateFound)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            userChoice = ShowInstallDownloadCancelDialog();
                        });
                        LogToInstall("User choice from Install/Download/Cancel: " + userChoice);

                        // If user chooses "Download", immediately return so caller can show sources menu
                        if (userChoice.Equals("Download", StringComparison.OrdinalIgnoreCase))
                        {
                            LogToInstall("User chose Download. Will show sources menu.");
                            return (null, false, 0, false);
                        }

                        // If user cancels or doesn't choose Install, abort repack flow
                        if (!userChoice.Equals("Install", StringComparison.OrdinalIgnoreCase))
                        {
                            LogToInstall("User did not choose Install. Aborting local repack.");
                            return (null, false, 0, false);
                        }
                    }
                    else
                    {
                        // If no candidate found, return early (let caller show sources menu)
                        LogToInstall("No repack candidate found locally.");
                        return (null, false, 0, false);
                    }

                    // --- If user chose "Install" and candidate is a folder, just return path ---
                    if (!isArchive)
                    {
                        LogToInstall("Candidate is a folder, no extraction needed.");
                        return (candidatePath, isArchive, fileSize, candidateFound);
                    }

                    // --- If candidate is an archive, ask for target drive and extract ---
                    string selectedDrive = "";
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        selectedDrive = ShowDriveSelectionDialog((long)Math.Ceiling(fileSize * 1.2));
                    });
                    LogToInstall("Selected drive for extraction: " + selectedDrive);

                    if (string.IsNullOrEmpty(selectedDrive))
                    {
                        LogToInstall("No drive selected. Aborting extraction.");
                        return (null, false, fileSize, candidateFound);
                    }

                    string driveGamesFolder = $"{selectedDrive}:" + Path.DirectorySeparatorChar + "Games";
                    System.IO.Directory.CreateDirectory(driveGamesFolder);
                    string outputFolder = Path.Combine(driveGamesFolder, gameName);
                    System.IO.Directory.CreateDirectory(outputFolder);
                    LogToInstall("Output folder created: " + outputFolder);

                    if (!System.IO.File.Exists(candidatePath))
                    {
                        LogToInstall("Candidate archive not found: " + candidatePath);
                        return (null, false, fileSize, candidateFound);
                    }

                    string arguments = $"x \"{candidatePath}\" -o\"{outputFolder}\" -y";
                    string sevenZipExe = "7z.exe";
                    if (!System.IO.File.Exists(sevenZipExe))
                    {
                        sevenZipExe = @"C:\Program Files\7-Zip\7z.exe";
                        if (!System.IO.File.Exists(sevenZipExe))
                        {
                            sevenZipExe = @"C:\Program Files (x86)\7-Zip\7z.exe";
                            if (!System.IO.File.Exists(sevenZipExe))
                            {
                                LogToInstall("7z.exe not found.");
                                return (null, false, fileSize, candidateFound);
                            }
                        }
                    }

                    try
                    {
                        LogToInstall("Starting extraction: " + sevenZipExe + " " + arguments);
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = sevenZipExe,
                            Arguments = arguments,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };

                        using (Process proc = Process.Start(psi))
                        {
                            proc.WaitForExit();
                            LogToInstall("Extraction process exited with code: " + proc.ExitCode);
                        }
                        candidatePath = outputFolder;
                        isArchive = false;
                        LogToInstall("Extraction succeeded. Candidate repack now at: " + candidatePath);
                    }
                    catch (Exception ex)
                    {
                        LogToInstall("Extraction failed: " + ex.Message);
                        return (null, false, fileSize, candidateFound);
                    }

                    // --- Optionally prompt for redists install ---
                    string redistChoice = "";
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        redistChoice = ShowYesNoDialog("Install Redsits?", "Post Extraction");
                    });
                    LogToInstall("Redist installation choice: " + redistChoice);
                    if (redistChoice.Equals("Yes", StringComparison.OrdinalIgnoreCase))
                    {
                        string redistsFolder = Path.Combine(candidatePath, "_CommonRedist");
                        LogToInstall("Attempting to install redists from: " + redistsFolder);
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            await InstallRedistsAsync(redistsFolder);
                        }).Wait();
                    }
                    else
                    {
                        LogToInstall("User chose not to install redists.");
                    }

                    // --- Optionally prompt for cleanup ---
                    string deleteChoice = "";
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        deleteChoice = ShowYesNoDialog("Delete Repack?", "Cleanup");
                    });
                    LogToInstall("Delete repack choice: " + deleteChoice);
                    if (deleteChoice.Equals("Yes", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            Directory.Delete(candidatePath, true);
                            LogToInstall("Repack folder deleted: " + candidatePath);
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                playniteApi.Dialogs.ShowMessage("Repack folder deleted.", "Cleanup", MessageBoxButton.OK);
                            });
                            candidatePath = "";
                        }
                        catch (Exception ex)
                        {
                            LogToInstall("Error deleting repack folder: " + ex.Message);
                        }
                    }
                    else
                    {
                        LogToInstall("User chose to keep the repack folder.");
                    }

                    return (candidatePath, isArchive, fileSize, candidateFound);
                });
            }

            private void LogToInstall(string message)
            {
                try
                {
                    string logFile = "Install.txt";
                    string timeStamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string logLine = $"{timeStamp} {message}{Environment.NewLine}";
                    System.IO.File.AppendAllText(logFile, logLine);
                }
                catch (Exception)
                {
                    // Optionally handle logging errors.
                }
            }

            private void UpdateGameActionsAndStatus(Game game, string userDataPath)
            {
                // Verify that the InstallDirectory is valid.
                if (string.IsNullOrEmpty(game.InstallDirectory) || !Directory.Exists(game.InstallDirectory))
                {
                    LogToInstall($"Error: InstallDirectory is invalid or missing for {game.Name}.");
                    return;
                }

                LogToInstall($"Scanning game directory: {game.InstallDirectory} for {game.Name}");

                // Load exclusions from "Exclusions.txt"
                var exclusionsPath = Path.Combine(userDataPath, "Exclusions.txt");
                var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (System.IO.File.Exists(exclusionsPath))
                {
                    var exclusionLines = System.IO.File.ReadAllLines(exclusionsPath);
                    foreach (var line in exclusionLines)
                    {
                        var trimmedLine = line.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmedLine))
                        {
                            exclusions.Add(trimmedLine.ToLower());
                        }
                    }
                }
                else
                {
                    LogToInstall($"Warning: Exclusions file not found at {exclusionsPath}. No exclusions will be applied.");
                }

                // Get all .exe files recursively from the InstallDirectory.
                var exeFiles = Directory.GetFiles(game.InstallDirectory, "*.exe", SearchOption.AllDirectories);
                int addedActions = 0;

                foreach (var exeFile in exeFiles)
                {
                    // Get the relative path from the install directory and split it into segments.
                    string relativePath = GetRelativePathCustom(game.InstallDirectory, exeFile);
                    var segments = relativePath.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

                    // Skip this exe if any folder in its relative path contains "redist" or "redsit".
                    bool skipDueToRedist = segments.Any(seg => seg.ToLower().Contains("redist") || seg.ToLower().Contains("redsit"));
                    if (skipDueToRedist)
                    {
                        LogToInstall($"Skipped {exeFile} due to 'redist' folder in the path.");
                        continue;
                    }

                    // Get the exe file name (without extension) in lower-case.
                    var exeName = Path.GetFileNameWithoutExtension(exeFile).ToLower();
                    if (exclusions.Contains(exeName))
                    {
                        LogToInstall($"Skipped {exeFile} because '{exeName}' is in the exclusions list.");
                        continue;
                    }

                    // Avoid duplicate actions by checking if one already exists.
                    if (game.GameActions.Any(a => a.Name.Equals(Path.GetFileNameWithoutExtension(exeFile), StringComparison.OrdinalIgnoreCase)))
                    {
                        LogToInstall($"Skipped {exeFile} due to duplicate action.");
                        continue;
                    }

                    // Create a new play action.
                    var action = new GameAction
                    {
                        Name = Path.GetFileNameWithoutExtension(exeFile),
                        Type = GameActionType.File,
                        Path = exeFile,
                        WorkingDir = Path.GetDirectoryName(exeFile),
                        IsPlayAction = true
                    };

                    game.GameActions.Add(action);
                    addedActions++;
                    LogToInstall("Added new game action for exe: " + exeFile);
                }

                LogToInstall($"Total new game actions added: {addedActions}. Total actions now: {game.GameActions.Count}");

                // Update the game record in the database.
                API.Instance.Database.Games.Update(game);

                // Signal that installation is completed.
                InvokeOnInstalled(new GameInstalledEventArgs(game.Id));

                // Force library update for the specific game only if it belongs to our library.
                // Use PluginId to ensure we're updating the correct game when there are duplicates.
                var updatedGame = API.Instance.Database.Games.FirstOrDefault(g =>
                                      g.PluginId == game.PluginId &&
                                      g.Name.Equals(game.Name, StringComparison.OrdinalIgnoreCase));

                if (updatedGame != null)
                {
                    // Update the game from our library.
                    game.InstallDirectory = updatedGame.InstallDirectory;
                    game.GameActions = new ObservableCollection<GameAction>(updatedGame.GameActions);
                    LogToInstall($"Updated game data for {game.Name} using PluginId: {game.PluginId}");
                    API.Instance.Database.Games.Update(game);
                }
                else
                {
                    LogToInstall($"Warning: No matching game entry with PluginId {game.PluginId} found for {game.Name}. Installation details may not be updated.");
                }
            }


            private string ShowInstallDownloadCancelDialog()
            {
                List<MessageBoxOption> options = new List<MessageBoxOption>();
                Dictionary<MessageBoxOption, string> optionMapping = new Dictionary<MessageBoxOption, string>();
                bool isFirst = true;

                MessageBoxOption installOption = new MessageBoxOption("Install", isFirst, false);
                isFirst = false;
                options.Add(installOption);
                optionMapping[installOption] = "Install";

                MessageBoxOption downloadOption = new MessageBoxOption("Download", false, false);
                options.Add(downloadOption);
                optionMapping[downloadOption] = "Download";

                // Add Steam if present
                if (Game.Features?.Any(f => f.Name.Equals("[Own: Steam]", StringComparison.OrdinalIgnoreCase)) == true)
                {
                    MessageBoxOption steamOption = new MessageBoxOption("Steam", false, false);
                    options.Add(steamOption);
                    optionMapping[steamOption] = "Steam";
                }

                MessageBoxOption cancelOption = new MessageBoxOption("Cancel", false, true);
                options.Add(cancelOption);
                optionMapping[cancelOption] = "Cancel";

                var mainWindow = playniteApi.Dialogs.GetCurrentAppWindow();
                mainWindow?.Activate();

                MessageBoxOption selectedOption = playniteApi.Dialogs.ShowMessage(
                    "Select your action:",
                    "Action",
                    MessageBoxImage.Question,
                    options);

                if (selectedOption != null &&
                    optionMapping.TryGetValue(selectedOption, out string chosenAction) &&
                    !string.Equals(chosenAction, "Cancel", StringComparison.OrdinalIgnoreCase))
                {
                    return chosenAction;
                }
                return "Cancel";
            }
            // 2b. The follow-up menu after extraction: "Install Redsits" vs. "Cancel"
            private string ShowInstallRedistsDialog()
            {
                List<MessageBoxOption> options = new List<MessageBoxOption>();
                Dictionary<MessageBoxOption, string> mapping = new Dictionary<MessageBoxOption, string>();
                bool isFirst = true;

                var installRedOption = new MessageBoxOption("Yes", isFirst, false);
                isFirst = false;
                options.Add(installRedOption);
                mapping[installRedOption] = "yes";

                var cancelOption = new MessageBoxOption("No", false, true);
                options.Add(cancelOption);
                mapping[cancelOption] = "No";

                var mainWindow = playniteApi.Dialogs.GetCurrentAppWindow();
                mainWindow?.Activate();

                MessageBoxOption selectedOption = playniteApi.Dialogs.ShowMessage(
                     "Repack extracted successfully. Install Redsits?",
                     "Post Extraction",
                     MessageBoxImage.Question,
                     options);

                if (selectedOption != null && mapping.TryGetValue(selectedOption, out string choice))
                {
                    return choice;
                }
                return "No";
            }

            // 3. Process the repack candidate - after SearchForLocalRepackAsync returns a valid repackPath,
            // call your already existing InstallRedistsAsync method.
            private async Task ProcessRepackAsync(string gameName)
            {
                var (repackPath, isArchive, fileSize, candidateFound) = await SearchForLocalRepackAsync(gameName);
                if (!candidateFound || string.IsNullOrEmpty(repackPath))
                {
                    // Either no local repack candidate was found or the user canceled.
                    // In this case, do not proceed to show online download sources.
                    return;
                }

                // At this point, repackPath references either the repack folder (if it was a folder repack)
                // or the extracted folder (if it was an archive repack and extraction succeeded).
                // Call your InstallRedistsAsync method.
                await InstallRedistsAsync(repackPath);
            }


            // 2c. The drive selection dialog (must mirror your provider-style dialog)
            private string ShowDriveSelectionDialog(long requiredSpace)
            {
                List<string> validDrives = new List<string>();
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady)
                        continue;

                    string gamesFolder = Path.Combine(drive.RootDirectory.FullName, "Games");
                    if (!Directory.Exists(gamesFolder))
                        continue;

                    if (drive.AvailableFreeSpace >= requiredSpace)
                        validDrives.Add(drive.RootDirectory.FullName);
                }
                if (validDrives.Count == 0)
                    return null;

                List<MessageBoxOption> options = new List<MessageBoxOption>();
                Dictionary<MessageBoxOption, string> optionMapping = new Dictionary<MessageBoxOption, string>();
                bool isFirst = true;
                foreach (string driveRoot in validDrives)
                {
                    string driveLetter = driveRoot.Substring(0, 1).ToUpperInvariant();
                    MessageBoxOption option = new MessageBoxOption(driveLetter, isFirst, false);
                    isFirst = false;
                    options.Add(option);
                    optionMapping[option] = driveLetter;
                }
                var cancelOption = new MessageBoxOption("Cancel", false, true);
                options.Add(cancelOption);
                optionMapping[cancelOption] = "Cancel";

                double requiredSpaceGB = requiredSpace / (1024.0 * 1024 * 1024);
                string title = $"Choose Drive (Space Required: {requiredSpaceGB:F2} GB)";

                var mainWindow = playniteApi.Dialogs.GetCurrentAppWindow();
                mainWindow?.Activate();

                MessageBoxOption selectedOption = playniteApi.Dialogs.ShowMessage(
                     "Select Installation Drive (drive with 'Games' folder and sufficient free space)",
                     title,
                     MessageBoxImage.Question,
                     options);

                if (selectedOption != null && optionMapping.TryGetValue(selectedOption, out string chosenDrive) &&
                     !chosenDrive.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
                {
                    return chosenDrive;
                }
                return null;
            }

            private async Task InstallRedistsAsync(string redistsFolder)
            {
                if (!Directory.Exists(redistsFolder))
                {
                    playniteApi.Dialogs.ShowMessage("No Redists folder found.", "Install Redsits", MessageBoxButton.OK);
                    return;
                }

                var installerFiles = Directory.GetFiles(redistsFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var file in installerFiles)
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = file,
                            Arguments = "/quiet /norestart",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        await Task.Run(() =>
                        {
                            using (Process proc = Process.Start(psi))
                            {
                                proc.WaitForExit();
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error installing redist '{file}': {ex.Message}");
                    }
                }
                playniteApi.Dialogs.ShowMessage("Redists installation completed.", "Install Redsits", MessageBoxButton.OK);
            }

            private string ShowYesNoDialog(string message, string title)
            {
                // Use the standard MessageBoxButton.YesNo overload.
                MessageBoxResult result = playniteApi.Dialogs.ShowMessage(message, title, MessageBoxButton.YesNo);
                return (result == MessageBoxResult.Yes) ? "Yes" : "No";
            }



            // Helper: Display source selection dialog.
            private string ShowSourceSelectionDialog(List<GameAction> urlActions)
            {
                List<MessageBoxOption> options = new List<MessageBoxOption>();
                Dictionary<MessageBoxOption, string> optionMapping = new Dictionary<MessageBoxOption, string>();
                bool isFirst = true;
                foreach (var action in urlActions)
                {
                    var option = new MessageBoxOption(action.Name, isFirst, false);
                    isFirst = false;
                    options.Add(option);
                    optionMapping[option] = action.Name;
                }
                var cancelOption = new MessageBoxOption("Cancel", false, true);
                options.Add(cancelOption);
                optionMapping[cancelOption] = "Cancel";

                var mainWindow = playniteApi.Dialogs.GetCurrentAppWindow();
                mainWindow?.Activate();

                var selectedOption = playniteApi.Dialogs.ShowMessage(
                    "Select a download source:",
                    "Source Selection",
                    MessageBoxImage.Question,
                    options);

                return selectedOption != null &&
                       optionMapping.TryGetValue(selectedOption, out string chosenSource) &&
                       chosenSource != "Cancel"
                       ? chosenSource
                       : null;
            }

            private async Task HandleAnkerGamesDownload()
            {
                var downloadAction = Game.GameActions.FirstOrDefault(a =>
                    !string.IsNullOrEmpty(a.Name) &&
                    a.Name.Equals("Download: AnkerGames", StringComparison.OrdinalIgnoreCase));
                if (downloadAction == null || string.IsNullOrEmpty(downloadAction.Path))
                {
                    playniteApi.Dialogs.ShowErrorMessage("No valid AnkerGames download URL found for this game.", "Error");
                    return;
                }

                string url = downloadAction.Path;
                string pluginDataDir = pluginInstance.GetPluginUserDataPath();
                Directory.CreateDirectory(pluginDataDir);
                string urlsFilePath = Path.Combine(pluginDataDir, "_Add_Urls_here.txt");
                string pythonScriptPath = Path.Combine(pluginDataDir, "fucking fast.py");
                string myGamesPath = Path.Combine(pluginDataDir, "My Games.txt");
                string error = null;
                string version = null;

                // 1. Write URL and run the Python script
                await Task.Run(() =>
                {
                    try
                    {
                        File.AppendAllText(urlsFilePath, url + Environment.NewLine);

                        if (!File.Exists(pythonScriptPath))
                        {
                            error = $"Python script not found: {pythonScriptPath}";
                            return;
                        }

                        var psi = new ProcessStartInfo
                        {
                            FileName = "python", // Use full path if needed
                            Arguments = $"\"{pythonScriptPath}\"",
                            WorkingDirectory = pluginDataDir,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };

                        using (var process = Process.Start(psi))
                        {
                            process.WaitForExit();
                        }
                    }
                    catch (Exception ex)
                    {
                        error = $"Failed to process AnkerGames URL: {ex.Message}";
                    }
                });

                // 2. Scrape the version from the URL
                if (error == null)
                {
                    try
                    {
                        string pageContent = await pluginInstance.LoadPageContent(url);
                        // Extract any text from the span with bg-green-500
                        var regex = new Regex(@"<span[^>]*bg-green-500[^>]*>\s*(?<ver>[^<]+?)\s*<\/span>", RegexOptions.IgnoreCase);
                        var match = regex.Match(pageContent);
                        if (match.Success)
                        {
                            version = match.Groups["ver"].Value.Trim();
                        }
                        else
                        {
                            version = "Unknown";
                        }
                    }
                    catch (Exception ex)
                    {
                        error = $"Error scraping version info: {ex.Message}";
                    }
                }

                // 3. Write or update entry in "My Games.txt"
                if (error == null)
                {
                    try
                    {
                        string lineToWrite = $"Name: {Game.Name}, Source: AnkerGames, Version: {version}";
                        List<string> lines = new List<string>();
                        if (File.Exists(myGamesPath))
                        {
                            lines = File.ReadAllLines(myGamesPath).ToList();
                        }
                        bool found = false;
                        for (int i = 0; i < lines.Count; i++)
                        {
                            if (lines[i].StartsWith($"Name: {Game.Name},"))
                            {
                                lines[i] = lineToWrite;
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            lines.Add(lineToWrite);
                        }
                        File.WriteAllLines(myGamesPath, lines);
                    }
                    catch (Exception ex)
                    {
                        error = $"Error writing to My Games.txt: {ex.Message}";
                    }
                }

                // 4. Show result
                if (error != null)
                {
                    playniteApi.Dialogs.ShowErrorMessage(error, "AnkerGames Extraction");
                }
                else
                {
                    playniteApi.Dialogs.ShowMessage($"AnkerGames URL processed. Version: {version}\nEntry updated in My Games.txt.", "Success");
                }
            }

            private async Task HandleMyAbandonDownload()
            {
                var downloadAction = Game.GameActions.FirstOrDefault(a =>
                    !string.IsNullOrEmpty(a.Name) &&
                    a.Name.Equals("Download: My.Abandon", StringComparison.OrdinalIgnoreCase));
                if (downloadAction == null || string.IsNullOrEmpty(downloadAction.Path))
                {
                    playniteApi.Dialogs.ShowErrorMessage("No valid My.Abandon download URL found for this game.", "Error");
                    return;
                }

                string url = downloadAction.Path;
                string pluginDataDir = pluginInstance.GetPluginUserDataPath();
                string otherSourcesDir = Path.Combine(pluginDataDir, "Other Sources", "My Abadonware");
                Directory.CreateDirectory(otherSourcesDir);
                string urlsFilePath = Path.Combine(otherSourcesDir, "_Add_Urls_here.txt");
                string myGamesPath = Path.Combine(pluginDataDir, "My Games.txt");
                string error = null;
                string version = null;

                // 1. Write URL to _Add_Urls_here.txt
                await Task.Run(() =>
                {
                    try
                    {
                        File.AppendAllText(urlsFilePath, url + Environment.NewLine);
                    }
                    catch (Exception ex)
                    {
                        error = $"Failed to add URL to _Add_Urls_here.txt: {ex.Message}";
                    }
                });

                // 2. Scrape the download size from the game page's #download link, e.g. <a href="#download">Download <span>98 KB</span></a>
                if (error == null)
                {
                    try
                    {
                        string pageContent = await pluginInstance.LoadPageContent(url);
                        // Look for: <a href="#download">Download <span>98 KB</span></a>
                        var regex = new Regex(@"<a\s+href\s*=\s*[""']#download[""'][^>]*>\s*Download\s*<span[^>]*>\s*(?<size>[^<]+?)\s*</span>", RegexOptions.IgnoreCase);
                        var match = regex.Match(pageContent);
                        if (match.Success)
                        {
                            version = match.Groups["size"].Value.Trim();
                        }
                        else
                        {
                            version = "Unknown";
                        }
                    }
                    catch (Exception ex)
                    {
                        error = $"Error scraping download info: {ex.Message}";
                    }
                }

                // 3. Write or update entry in "My Games.txt"
                if (error == null)
                {
                    try
                    {
                        string lineToWrite = $"Name: {Game.Name}, Source: My.Abandon, Size: {version}";
                        List<string> lines = new List<string>();
                        if (File.Exists(myGamesPath))
                        {
                            lines = File.ReadAllLines(myGamesPath).ToList();
                        }
                        bool found = false;
                        for (int i = 0; i < lines.Count; i++)
                        {
                            if (lines[i].StartsWith($"Name: {Game.Name},"))
                            {
                                lines[i] = lineToWrite;
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            lines.Add(lineToWrite);
                        }
                        File.WriteAllLines(myGamesPath, lines);
                    }
                    catch (Exception ex)
                    {
                        error = $"Error writing to My Games.txt: {ex.Message}";
                    }
                }

                // 4. Show result
                if (error != null)
                {
                    playniteApi.Dialogs.ShowErrorMessage(error, "My.Abandon Extraction");
                }
                else
                {
                    playniteApi.Dialogs.ShowMessage($"My.Abandon URL processed. File size: {version}\nEntry updated in My Games.txt.", "Success");
                }
            }

            private async Task HandleMagipackDownload()
            {
                var downloadAction = Game.GameActions.FirstOrDefault(a =>
                    a.Name.Equals("Download: Magipack", StringComparison.OrdinalIgnoreCase));

                if (downloadAction == null || string.IsNullOrWhiteSpace(downloadAction.Path))
                {
                    playniteApi.Dialogs.ShowErrorMessage("No valid download URL found for this game.", "Error");
                    return;
                }

                string url = downloadAction.Path;

                // If the URL is not a magnet link, try to scrape the page to obtain one.
                if (!url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    string pageContent = await this.MagipackLoadPageContent(url);
                    if (string.IsNullOrWhiteSpace(pageContent))
                    {
                        playniteApi.Dialogs.ShowErrorMessage("Failed to load page content for magnet link extraction.", "Error");
                        return;
                    }

                    // Look for a magnet link using a regex.
                    var magnetRegex = new Regex(@"(magnet:\?xt=urn:btih:[^\s""]+)", RegexOptions.IgnoreCase);
                    var match = magnetRegex.Match(pageContent);
                    if (!match.Success)
                    {
                        playniteApi.Dialogs.ShowErrorMessage("No magnet link found on the page.", "Error");
                        return;
                    }

                    url = match.Groups[1].Value;
                }

                try
                {
                    // Open the magnet link using the system's default handler.
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd",
                        Arguments = $"/c start \"\" \"{url}\"",
                        CreateNoWindow = true,
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage("Failed to open magnet link: " + ex.Message, "Error");
                    return;
                }

                // After launching the provider link, search all drives for a repack that matches the game name.
                await SearchAndLogAndProcessRepackAsync(Game.Name);
            }

            // Dodi Repacks
            private async Task HandleDodiRepacksDownload()
            {
                var downloadAction = Game.GameActions?
                    .FirstOrDefault(a =>
                        !string.IsNullOrEmpty(a.Name) &&
                        a.Name.Equals("Download: Dodi", StringComparison.OrdinalIgnoreCase));
                if (downloadAction == null || string.IsNullOrEmpty(downloadAction.Path))
                {
                    playniteApi.Dialogs.ShowErrorMessage("No valid Dodi Repacks download URL found for this game.", "Error");
                    return;
                }

                string url = downloadAction.Path;
                string pluginDataDir = pluginInstance.GetPluginUserDataPath();
                Directory.CreateDirectory(pluginDataDir);

                // 1. Write URL to request file
                string requestDodiFile = Path.Combine(pluginDataDir, "Request_Dodi_Url.txt");
                File.WriteAllText(requestDodiFile, url);

                // 2. Run the Python script that produces Dodi.Links.txt
                string pythonScriptPath = Path.Combine(pluginDataDir, "Dodi.Links.py"); // Your actual script filename
                string dodiLinksPath = Path.Combine(pluginDataDir, "Dodi.Links.txt");
                string error = null;

                await Task.Run(() =>
                {
                    try
                    {
                        if (!File.Exists(pythonScriptPath))
                        {
                            error = $"Python script not found: {pythonScriptPath}";
                            return;
                        }

                        var psi = new ProcessStartInfo
                        {
                            FileName = "python",
                            Arguments = $"\"{pythonScriptPath}\"",
                            WorkingDirectory = pluginDataDir,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };

                        using (var process = Process.Start(psi))
                        {
                            process.WaitForExit();
                        }
                    }
                    catch (Exception ex)
                    {
                        error = $"Failed to process Dodi Repacks URL: {ex.Message}";
                    }
                });

                if (error != null)
                {
                    playniteApi.Dialogs.ShowErrorMessage(error, "Dodi Repacks Extraction");
                    return;
                }

                // 3. Read Dodi.Links.txt and build provider dictionary
                List<string> links = new List<string>();
                if (File.Exists(dodiLinksPath))
                {
                    links = File.ReadAllLines(dodiLinksPath)
                        .Where(l => !string.IsNullOrWhiteSpace(l) && l.Contains(":"))
                        .ToList();
                }
                if (links.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage("No download links found for this Dodi repack.", "Download Error");
                    return;
                }

                Dictionary<string, string> providerDict = new Dictionary<string, string>();
                foreach (var line in links)
                {
                    var split = line.Split(new[] { ':' }, 2);
                    if (split.Length == 2)
                    {
                        string provider = split[0].Trim();
                        string providerUrl = split[1].Trim();
                        if (!providerDict.ContainsKey(provider))
                            providerDict[provider] = providerUrl;
                    }
                }

                // 4. Show provider dialog (reuses your SteamRip logic)
                string selectedProvider = ShowProviderSelectionDialog(providerDict);
                if (string.IsNullOrEmpty(selectedProvider))
                    return;

                // 5. Open the chosen provider URL (open in browser, or custom logic)
                if (providerDict.TryGetValue(selectedProvider, out string selectedUrl) && !string.IsNullOrEmpty(selectedUrl))
                {
                    // Open in browser or do your download logic here
                    System.Diagnostics.Process.Start(new ProcessStartInfo
                    {
                        FileName = selectedUrl,
                        UseShellExecute = true
                    });
                }
            }
            private async Task SearchAndLogAndProcessRepackAsync(string gameName)
            {
                // Lists for matching repack file paths and folder paths.
                List<string> foundFilePaths = new List<string>();       // e.g. files ending with .rar or .zip
                List<string> foundDirectoryPaths = new List<string>();    // directories that contain a Setup.exe

                // Loop through all available drives.
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady)
                        continue;

                    // Assume repacks are stored in a folder named "Repacks" at the drive’s root.
                    string repacksFolder = Path.Combine(drive.RootDirectory.FullName, "Repacks");
                    if (!Directory.Exists(repacksFolder))
                        continue;

                    // Search for rar/zip files that start with the game name.
                    string[] filesZip = Directory.GetFiles(repacksFolder, $"{gameName}*.zip", SearchOption.TopDirectoryOnly);
                    string[] filesRar = Directory.GetFiles(repacksFolder, $"{gameName}*.rar", SearchOption.TopDirectoryOnly);

                    if (filesZip != null && filesZip.Length > 0)
                        foundFilePaths.AddRange(filesZip);
                    if (filesRar != null && filesRar.Length > 0)
                        foundFilePaths.AddRange(filesRar);

                    // Also search for directories whose names start with the game name.
                    string[] directories = Directory.GetDirectories(repacksFolder, $"{gameName}*", SearchOption.TopDirectoryOnly);
                    foreach (var dir in directories)
                    {
                        // Validate that this folder is a repack by checking for "Setup.exe" inside.
                        string setupPath = Path.Combine(dir, "Setup.exe");
                        if (System.IO.File.Exists(setupPath))
                        {
                            foundDirectoryPaths.Add(dir);
                        }
                    }
                }

                // Log all found items to "Repacks.txt".
                List<string> logEntries = new List<string>();
                foreach (string file in foundFilePaths)
                {
                    logEntries.Add($"{gameName} - {file}");
                }
                foreach (string dir in foundDirectoryPaths)
                {
                    logEntries.Add($"{gameName} - {dir}");
                }

                if (logEntries.Count > 0)
                {
                    try
                    {
                        string logFile = "Repacks.txt";
                        // Use Task.Run to avoid blocking if AppendAllLinesAsync is not available.
                        await Task.Run(() => System.IO.File.AppendAllLines(logFile, logEntries));
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error writing to Repacks.txt: {ex.Message}");
                    }
                }

                // Now, process the found repack.
                // Priority: if a rar/zip file is found, open it with 7-Zip.
                if (foundFilePaths.Count > 0)
                {
                    string fileToProcess = foundFilePaths.First(); // in a real scenario, you might let the user choose.
                    try
                    {
                        // Adjust the following line if the 7-Zip executable (7zFM.exe) is not in PATH.
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "7zFM.exe",
                            Arguments = $"\"{fileToProcess}\"",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error launching 7-Zip for file: {ex.Message}");
                    }
                }
                // Otherwise, if a repack folder was found (i.e. one with Setup.exe), launch Setup.exe.
                else if (foundDirectoryPaths.Count > 0)
                {
                    string folderToProcess = foundDirectoryPaths.First();
                    string setupExePath = Path.Combine(folderToProcess, "Setup.exe");
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = setupExePath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error launching Setup.exe: {ex.Message}");
                    }
                }
            }

            // Custom helper method to get a relative path (since Path.GetRelativePath is unavailable)
            private static string GetRelativePathCustom(string basePath, string fullPath)
            {
                if (string.IsNullOrEmpty(basePath))
                    throw new ArgumentNullException(nameof(basePath));
                if (string.IsNullOrEmpty(fullPath))
                    throw new ArgumentNullException(nameof(fullPath));

                // Ensure basePath ends with a directory separator.
                basePath = AppendDirectorySeparatorChar(basePath);
                Uri baseUri = new Uri(basePath);
                Uri fullUri = new Uri(fullPath);
                string relativeUri = baseUri.MakeRelativeUri(fullUri).ToString();

                // Replace forward slashes with system-specific directory separator.
                return Uri.UnescapeDataString(relativeUri.Replace('/', Path.DirectorySeparatorChar));
            }

            private static string AppendDirectorySeparatorChar(string path)
            {
                if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    return path + Path.DirectorySeparatorChar;
                }
                return path;
            }

            private async Task<string> MagipackLoadPageContent(string url)
            {
                using (var httpClient = new HttpClient())
                {
                    try
                    {
                        return await httpClient.GetStringAsync(url);
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error loading content from {url}: {ex.Message}");
                        return string.Empty;
                    }
                }
            }


           

            private static readonly Dictionary<string, string> HdTexturePackListUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    { "Sony PlayStation",   "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/main/HD%20Textures/Sony%20-%20Playstation/Games.txt" },
    { "Sony PlayStation 2", "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/main/HD%20Textures/Sony%20-%20Playstation%202/Games.txt" },
    { "Sony PlayStation 3", "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/main/HD%20Textures/Sony%20-%20Playstation%203/Games.txt" },
    { "Sony PlayStation Portable", "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/main/HD%20Textures/Sony%20-%20Playstation%20Portable/Games.txt" },
    { "Nintendo GameCube",  "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/main/HD%20Textures/Nintendo%20-%20GameCube/Games.txt" },
    { "Nintendo Wii",       "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/main/HD%20Textures/Nintendo%20-%20Wii/Games.txt" },
    { "Nintendo Wii U",     "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/main/HD%20Textures/Nintendo%20-%20Wii%20U/Games.txt" },
    { "Microsoft Xbox",     "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/main/HD%20Textures/Microsoft%20-%20Xbox/Games.txt" },
    { "Microsoft Xbox 360", "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/main/HD%20Textures/Microsoft%20-%20Xbox%20360/Games.txt" }
};

            private static readonly Dictionary<string, string> TranslationPlatformUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    { "Sony PlayStation", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sony%20-%20PlayStation%20%5BT-En%5D%20Collection/" }
};

            private static readonly Dictionary<string, string> ContentPlatformUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
               { "Microsoft Xbox 360", "https://myrient.erista.me/files/No-Intro/Microsoft%20-%20Xbox%20360%20(Digital)/" }
            };

            private async Task HandleMyrientDownload()
            {
                // Build main menu
                var mainOptions = new List<MessageBoxOption>();
                var mainMapping = new Dictionary<MessageBoxOption, string>();

                var gameOption = new MessageBoxOption("Game ROM", true, false);
                mainOptions.Add(gameOption);
                mainMapping[gameOption] = "Game";

                string platformName = GetGamePlatformName();

                string hdTxtUrl = null;
                if (HdTexturePackListUrls.TryGetValue(platformName, out hdTxtUrl))
                {
                    var hdOption = new MessageBoxOption("HD Texture Packs", false, false);
                    mainOptions.Add(hdOption);
                    mainMapping[hdOption] = "HD";
                }

                string translationTxtUrl = null;
                if (TranslationPlatformUrls.TryGetValue(platformName, out translationTxtUrl))
                {
                    var trOption = new MessageBoxOption("Translations", false, false);
                    mainOptions.Add(trOption);
                    mainMapping[trOption] = "Translation";
                }

                string contentUrl = null;
                if (ContentPlatformUrls.TryGetValue(platformName, out contentUrl))
                {
                    var contentOption = new MessageBoxOption("Content Menu", false, false);
                    mainOptions.Add(contentOption);
                    mainMapping[contentOption] = "Content";
                }

                var cancelOption = new MessageBoxOption("Cancel", false, true);
                mainOptions.Add(cancelOption);
                mainMapping[cancelOption] = "Cancel";

                var mainWindow = playniteApi.Dialogs.GetCurrentAppWindow();
                mainWindow?.Activate();

                var selectedMain = playniteApi.Dialogs.ShowMessage(
                    "Select download type:",
                    "Download Menu",
                    MessageBoxImage.Question,
                    mainOptions);

                if (selectedMain == null || mainMapping[selectedMain] == "Cancel")
                    return;

                string chosen = mainMapping[selectedMain];
                if (chosen == "HD")
                {
                    await HandleHdTexturePacks(Game.Name, platformName, hdTxtUrl);
                    return;
                }
                else if (chosen == "Translation")
                {
                    await HandleTranslationMenu(platformName, translationTxtUrl, Game.Name);
                    return;
                }
                else if (chosen == "Content")
                {
                    await HandleMyrientContentMenu(platformName, contentUrl, Game.Name);
                    return;
                }
                else if (chosen == "Game")
                {
                    await HandleMyrientGameRomDownload();
                    return;
                }
            }

            private async Task HandleTranslationMenu(string platformName, string baseUrl, string gameName)
            {
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    playniteApi.Dialogs.ShowErrorMessage("No translation URL found for this platform.", "Translations");
                    return;
                }

                List<string> links = await ScrapeSiteForLinksAsync(gameName, baseUrl);
                if (links == null || links.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"No translation links found for {gameName}.", "Translations");
                    return;
                }

                string normGameName = NormalizeGameName(gameName);
                links = links.Where(link =>
                {
                    string decodedLink = WebUtility.UrlDecode(link);
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(decodedLink);
                    return NormalizeGameName(fileName).Contains(normGameName);
                }).ToList();

                if (links.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"No translation links matching '{gameName}' were found.", "Translations");
                    return;
                }

                // Make all links absolute
                links = links.Select(link =>
                {
                    if (!link.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            Uri fullUri = new Uri(new Uri(baseUrl), link);
                            return fullUri.ToString();
                        }
                        catch (Exception ex)
                        {
                            logger.Warn($"Failed to combine '{baseUrl}' and '{link}': {ex.Message}");
                            return link;
                        }
                    }
                    return link;
                }).ToList();

                // Build mapping: short label (team + rev) => link
                var options = new List<MessageBoxOption>();
                var optionMapping = new Dictionary<MessageBoxOption, string>();
                bool isFirst = true;
                foreach (var link in links)
                {
                    string decoded = WebUtility.UrlDecode(link);
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(decoded);

                    // Extract [T-En by TeamName v#.#] or [T-En ...]
                    string team = "";
                    var patchMatch = Regex.Match(fileName, @"\[T-En by ([^\]]+)\]", RegexOptions.IgnoreCase);
                    if (patchMatch.Success)
                    {
                        team = patchMatch.Groups[1].Value.Trim(); // e.g. "Load Word Team v1.1"
                    }
                    else
                    {
                        // fallback: try to get anything after [T-En
                        patchMatch = Regex.Match(fileName, @"\[T-En([^\]]+)\]", RegexOptions.IgnoreCase);
                        if (patchMatch.Success)
                        {
                            team = patchMatch.Groups[1].Value.Trim();
                        }
                    }

                    // Extract (Rev X)
                    string rev = "";
                    var revMatch = Regex.Match(fileName, @"\(Rev\s*([^\)]+)\)", RegexOptions.IgnoreCase);
                    if (revMatch.Success)
                    {
                        rev = $"(Rev {revMatch.Groups[1].Value.Trim()})";
                    }

                    string shortLabel = "";
                    if (!string.IsNullOrEmpty(team) && !string.IsNullOrEmpty(rev))
                        shortLabel = $"{team} {rev}";
                    else if (!string.IsNullOrEmpty(team))
                        shortLabel = team;
                    else if (!string.IsNullOrEmpty(rev))
                        shortLabel = rev;
                    else
                        shortLabel = fileName; // fallback

                    var option = new MessageBoxOption(shortLabel, isFirst, false);
                    isFirst = false;
                    options.Add(option);
                    optionMapping[option] = link;
                }
                var cancelOption = new MessageBoxOption("Cancel", false, true);
                options.Add(cancelOption);
                optionMapping[cancelOption] = "Cancel";

                var mainWindow = playniteApi.Dialogs.GetCurrentAppWindow();
                mainWindow?.Activate();

                var selectedOption = playniteApi.Dialogs.ShowMessage(
                    "Select a translation variant to download:",
                    "Translations",
                    MessageBoxImage.Question,
                    options);

                if (selectedOption == null || optionMapping[selectedOption] == "Cancel")
                    return;

                string chosenUrl = optionMapping[selectedOption];
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = chosenUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"Failed to open translation link: {ex.Message}", "Translations");
                }
            }
            private async Task HandleMyrientContentMenu(string platformName, string baseUrl, string gameName)
            {
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    playniteApi.Dialogs.ShowErrorMessage("No content menu source URL specified for this platform.", "Content Menu");
                    return;
                }

                // Only fully implemented for Xbox 360. Add other platforms as needed.
                if (!platformName.Equals("Microsoft Xbox 360", StringComparison.OrdinalIgnoreCase))
                {
                    var win = playniteApi.Dialogs.GetCurrentAppWindow();
                    win?.Activate();
                    playniteApi.Dialogs.ShowErrorMessage("Content menu not yet supported for this platform.", "Content Menu");
                    return;
                }

                List<string> links = await ScrapeSiteForLinksAsync(gameName, baseUrl);
                if (links == null || links.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"No content found for {gameName}.", "Content Menu");
                    return;
                }

                // Make all links absolute before filtering
                links = links.Select(link =>
                {
                    if (!link.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            Uri fullUri = new Uri(new Uri(baseUrl), link);
                            return fullUri.ToString();
                        }
                        catch
                        {
                            return link;
                        }
                    }
                    return link;
                }).ToList();

                // Filter to correct game by normalized prefix
                string normGameName = NormalizeGameName(gameName);

                // Accept DLC/content keywords (both paren and bare)
                string[] contentKeywords = new[] { "DLC", "Content", "Addon", "Expansion", "Pack" };

                var dlcLinks = links.Where(link =>
                {
                    string decoded = WebUtility.UrlDecode(link);
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(decoded);

                    // Remove region/addon/extra info in parens for matching
                    string stripped = Regex.Replace(fileName, @"\s*\([^)]*\)", "");
                    string normalized = NormalizeGameName(stripped);

                    // Must start with the normalized game name
                    if (!normalized.StartsWith(normGameName, StringComparison.OrdinalIgnoreCase))
                        return false;

                    // Must contain one of the content keywords (ignore case, anywhere)
                    return contentKeywords.Any(keyword => fileName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                }).ToList();

                var tuLinks = links.Where(link =>
                {
                    string decoded = WebUtility.UrlDecode(link);
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(decoded);

                    string stripped = Regex.Replace(fileName, @"\s*\([^)]*\)", "");
                    string normalized = NormalizeGameName(stripped);

                    return normalized.StartsWith(normGameName, StringComparison.OrdinalIgnoreCase)
                        && fileName.IndexOf("Title Update", StringComparison.OrdinalIgnoreCase) >= 0;
                }).ToList();

                var contentOptions = new List<MessageBoxOption>();
                var contentMapping = new Dictionary<MessageBoxOption, string>();
                if (dlcLinks.Count > 0)
                {
                    var dlcOption = new MessageBoxOption("DLC/Addons", true, false);
                    contentOptions.Add(dlcOption); contentMapping[dlcOption] = "DLC";
                }
                if (tuLinks.Count > 0)
                {
                    var tuOption = new MessageBoxOption("TU", contentOptions.Count == 0, false);
                    contentOptions.Add(tuOption); contentMapping[tuOption] = "TU";
                }
                var cancelOption = new MessageBoxOption("Cancel", false, true);
                contentOptions.Add(cancelOption); contentMapping[cancelOption] = "Cancel";

                if (contentOptions.Count <= 1)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"No content found for {gameName}.", "Content Menu");
                    return;
                }

                var win2 = playniteApi.Dialogs.GetCurrentAppWindow();
                win2?.Activate();

                var selectedContent = playniteApi.Dialogs.ShowMessage(
                    "Select content type:",
                    "Content Menu",
                    MessageBoxImage.Question,
                    contentOptions);

                if (selectedContent == null || contentMapping[selectedContent] == "Cancel")
                    return;

                if (contentMapping[selectedContent] == "DLC")
                {
                    await ShowPagedContentMenu("Select DLC/Addon to download:", dlcLinks, 4, true, gameName);
                }
                else if (contentMapping[selectedContent] == "TU")
                {
                    await ShowPagedContentMenu("Select Title Update to download:", tuLinks, 4, false, gameName);
                }
            }

            // New paged menu with numbered buttons and compact names
            private async Task ShowPagedContentMenu(string title, List<string> links, int maxPerPage = 4, bool isContent = false, string gameName = "")
            {
                if (links == null || links.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage("No content found.", "Content Menu");
                    return;
                }
                int page = 0;
                int totalPages = (int)Math.Ceiling((double)links.Count / maxPerPage);

                while (true)
                {
                    var pageLinks = links.Skip(page * maxPerPage).Take(maxPerPage).ToList();

                    // Prepare compact DLC names for this page
                    var dlcNames = new List<string>();
                    foreach (var link in pageLinks)
                    {
                        string decoded = WebUtility.UrlDecode(link);
                        string fileName = System.IO.Path.GetFileName(decoded);

                        string dlcName = fileName;
                        if (isContent)
                        {
                            // Remove .zip, game name and dash, everything in parens
                            dlcName = Regex.Replace(dlcName, @"\.zip$", "", RegexOptions.IgnoreCase);
                            if (!string.IsNullOrEmpty(gameName))
                            {
                                string prefix = gameName.Trim() + " - ";
                                if (dlcName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                    dlcName = dlcName.Substring(prefix.Length);
                            }
                            dlcName = Regex.Replace(dlcName, @"\s*\([^)]*\)", "");

                            // Remove trailing/leading dashes, spaces, double spaces
                            dlcName = Regex.Replace(dlcName, @"[-\s]+$", "");
                            dlcName = Regex.Replace(dlcName, @"^[-\s]+", "");
                            dlcName = Regex.Replace(dlcName, @"\s{2,}", " ");
                            dlcName = dlcName.Trim();

                            if (string.IsNullOrWhiteSpace(dlcName))
                                dlcName = fileName;
                        }
                        dlcNames.Add(dlcName);
                    }

                    // Compose display text with space after title and before list
                    string display = "Available DLC:\n\n";
                    for (int i = 0; i < dlcNames.Count; i++)
                    {
                        display += $"{i + 1}) {dlcNames[i]}\n";
                    }

                    // Prepare number buttons, only for populated slots
                    var options = new List<MessageBoxOption>();
                    var mapping = new Dictionary<MessageBoxOption, string>();
                    for (int i = 0; i < dlcNames.Count; i++)
                    {
                        var opt = new MessageBoxOption($"{i + 1}", i == 0, false);
                        options.Add(opt);
                        mapping[opt] = pageLinks[i];
                    }
                    // Only show "more" if there is another page
                    if (totalPages > 1 && (page + 1 < totalPages))
                    {
                        var moreOpt = new MessageBoxOption("more", false, false);
                        options.Add(moreOpt);
                        mapping[moreOpt] = "More";
                    }
                    var cancelOpt = new MessageBoxOption("Cancel", false, true);
                    options.Add(cancelOpt);
                    mapping[cancelOpt] = "Cancel";

                    var win = playniteApi.Dialogs.GetCurrentAppWindow();
                    win?.Activate();

                    var selectedOpt = playniteApi.Dialogs.ShowMessage(
                        display,
                        "Content Menu",
                        MessageBoxImage.Question,
                        options);

                    if (selectedOpt == null || mapping[selectedOpt] == "Cancel")
                        return;
                    if (mapping[selectedOpt] == "More")
                    {
                        page++;
                        continue;
                    }

                    string downloadUrl = mapping[selectedOpt];
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = downloadUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        playniteApi.Dialogs.ShowErrorMessage("Failed to open URL:\n" + ex.Message, "Error");
                    }
                    return;
                }
            }
            private async Task HandleMyrientGameRomDownload()
            {
                var downloadAction = Game.GameActions
                    .FirstOrDefault(a => a.Name.Equals("Download: Myrient", StringComparison.OrdinalIgnoreCase));

                if (downloadAction == null || string.IsNullOrEmpty(downloadAction.Path))
                {
                    playniteApi.Dialogs.ShowErrorMessage("Invalid source URL selected.", "Error");
                    return;
                }

                string baseUrl = downloadAction.Path;
                List<string> links = await ScrapeSiteForLinksAsync(Game.Name, baseUrl);
                if (links == null || links.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"No download links found for {Game.Name}.", "Download Error");
                    return;
                }

                links = links.Select(link =>
                {
                    if (!link.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            Uri fullUri = new Uri(new Uri(baseUrl), link);
                            return fullUri.ToString();
                        }
                        catch (Exception ex)
                        {
                            logger.Warn($"Failed to combine '{baseUrl}' and '{link}': {ex.Message}");
                            return link;
                        }
                    }
                    return link;
                }).ToList();

                string normGameName = NormalizeGameName(Game.Name);

                var regions = new Dictionary<string, List<string>>();
                foreach (var link in links)
                {
                    string decodedLink = WebUtility.UrlDecode(link);
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(decodedLink);
                    string region = GetStandardRegionCode(fileName);
                    if (!regions.ContainsKey(region))
                        regions[region] = new List<string>();
                    regions[region].Add(link);
                }

                if (regions.Count > 1)
                {
                    var regionOptions = new List<MessageBoxOption>();
                    var regionMapping = new Dictionary<MessageBoxOption, string>();
                    bool isFirst = true;
                    foreach (var region in regions.Keys)
                    {
                        var opt = new MessageBoxOption(region, isFirst, false);
                        isFirst = false;
                        regionOptions.Add(opt);
                        regionMapping[opt] = region;
                    }
                    var cancelOption = new MessageBoxOption("Cancel", false, true);
                    regionOptions.Add(cancelOption);
                    regionMapping[cancelOption] = "Cancel";

                    var mainWindow = playniteApi.Dialogs.GetCurrentAppWindow();
                    mainWindow?.Activate();

                    var selectedRegionOpt = playniteApi.Dialogs.ShowMessage(
                        "Select region:",
                        "Download Region",
                        MessageBoxImage.Question,
                        regionOptions);

                    if (selectedRegionOpt == null || regionMapping[selectedRegionOpt] == "Cancel")
                        return;

                    string chosenRegion = regionMapping[selectedRegionOpt];
                    links = regions[chosenRegion];
                }

                links = links.Where(link =>
                {
                    string decodedLink = WebUtility.UrlDecode(link);
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(decodedLink);
                    return NormalizeGameName(fileName).Contains(normGameName);
                }).ToList();

                if (links.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"No download links matching '{Game.Name}' were found.", "Download Error");
                    return;
                }

                Dictionary<string, string> variantDict = BuildMyrientVariantDictionary(links);
                if (variantDict.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage("No recognized download variants were found.", "Download Error");
                    return;
                }

                string selectedVariant = ShowMyrientVariantSelectionDialog(variantDict);
                if (string.IsNullOrEmpty(selectedVariant))
                    return;

                if (variantDict.TryGetValue(selectedVariant, out string downloadUrl))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = downloadUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        playniteApi.Dialogs.ShowErrorMessage($"Failed to open download link: {ex.Message}", "Error");
                    }
                }
                else
                {
                    playniteApi.Dialogs.ShowErrorMessage("Selected variant was not found.", "Selection Error");
                }
            }

            private async Task HandleHdTexturePacks(string gameName, string platformName, string gamesTxtUrl)
            {
                var hdPacks = await GetHdTexturePackAuthorsForGame(gameName, gamesTxtUrl);
                if (hdPacks.Count == 0)
                {
                    var mainWindow = playniteApi.Dialogs.GetCurrentAppWindow();
                    mainWindow?.Activate();
                    playniteApi.Dialogs.ShowMessage(
                        $"No HD texture pack found for \"{gameName}\" on {platformName}.",
                        "HD Packs"
                    );
                    return;
                }

                if (hdPacks.Count == 1)
                {
                    var pair = hdPacks.First();
                    var mainWindow = playniteApi.Dialogs.GetCurrentAppWindow();
                    mainWindow?.Activate();
                    var result = playniteApi.Dialogs.ShowMessage(
                        $"HD Texture Pack by {pair.Key}\n\nDownload Link:\n{pair.Value}",
                        "HD Texture Pack",
                        MessageBoxImage.Question,
                        new List<MessageBoxOption> { new MessageBoxOption("OK", true, false), new MessageBoxOption("Cancel", false, true) }
                    );
                    if (result != null && result.IsDefault)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = pair.Value,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            playniteApi.Dialogs.ShowErrorMessage("Failed to open URL:\n" + ex.Message, "Error");
                        }
                    }
                    return;
                }

                var authorOptions = new List<MessageBoxOption>();
                var authorMapping = new Dictionary<MessageBoxOption, string>();
                bool isFirst = true;
                foreach (var author in hdPacks.Keys)
                {
                    var opt = new MessageBoxOption(author, isFirst, false);
                    isFirst = false;
                    authorOptions.Add(opt);
                    authorMapping[opt] = author;
                }
                var cancelOption = new MessageBoxOption("Cancel", false, true);
                authorOptions.Add(cancelOption);
                authorMapping[cancelOption] = "Cancel";

                var mainWindow2 = playniteApi.Dialogs.GetCurrentAppWindow();
                mainWindow2?.Activate();

                var selected = playniteApi.Dialogs.ShowMessage(
                    "Select HD Pack Author to open download link:",
                    "HD Texture Packs",
                    MessageBoxImage.Question,
                    authorOptions);

                if (selected == null || authorMapping[selected] == "Cancel")
                    return;

                string authorChosen = authorMapping[selected];
                string url = hdPacks[authorChosen];
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage("Failed to open URL:\n" + ex.Message, "Error");
                }
            }

            private async Task<Dictionary<string, string>> GetHdTexturePackAuthorsForGame(string gameName, string gamesTxtUrl)
            {
                var result = new Dictionary<string, string>();
                try
                {
                    using (var client = new System.Net.Http.HttpClient())
                    {
                        string txt = await client.GetStringAsync(gamesTxtUrl);
                        string[] entries = Regex.Split(txt, @"(?:\r?\n){2,}");
                        foreach (string entry in entries)
                        {
                            string title = null, author = null, packUrl = null;
                            string[] lines = entry.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string line in lines)
                            {
                                string trimmedLine = line.Trim();
                                if (trimmedLine.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
                                    title = trimmedLine.Substring(5).Trim().Trim('"');
                                else if (trimmedLine.StartsWith("Author:", StringComparison.OrdinalIgnoreCase))
                                    author = trimmedLine.Substring(7).Trim().Trim('"');
                                else if (trimmedLine.StartsWith("Urls:", StringComparison.OrdinalIgnoreCase))
                                    packUrl = trimmedLine.Substring(5).Trim().Trim('"');
                            }
                            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(packUrl))
                            {
                                if (NormalizeGameName(title) == NormalizeGameName(gameName))
                                {
                                    result[author] = packUrl;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn("Failed to download or parse HD Games.txt: " + ex.Message);
                }
                return result;
            }

            private Dictionary<string, string> BuildMyrientVariantDictionary(List<string> links)
            {
                var variantDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var link in links)
                {
                    string variant = GetMyrientVariantName(link);
                    if (!variantDict.ContainsKey(variant))
                    {
                        variantDict.Add(variant, link);
                    }
                }
                return variantDict;
            }

            private string GetMyrientVariantName(string url)
            {
                string decodedUrl = WebUtility.UrlDecode(url);
                string fileName = System.IO.Path.GetFileNameWithoutExtension(decodedUrl);
                if (string.IsNullOrEmpty(fileName))
                {
                    return url.Trim();
                }
                var match = Regex.Match(fileName, @"\(([^)]+)\)");
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
                if (fileName.Contains("_"))
                {
                    var parts = fileName.Split('_');
                    if (parts.Length > 0)
                    {
                        return parts.Last().Trim();
                    }
                }
                return fileName.Trim();
            }

            private string ShowMyrientVariantSelectionDialog(Dictionary<string, string> variantDict)
            {
                var options = new List<MessageBoxOption>();
                var optionMapping = new Dictionary<MessageBoxOption, string>();

                bool isFirst = true;
                foreach (string variant in variantDict.Keys)
                {
                    var option = new MessageBoxOption(variant, isFirst, false);
                    isFirst = false;
                    options.Add(option);
                    optionMapping[option] = variant;
                }

                var cancelOption = new MessageBoxOption("Cancel", false, true);
                options.Add(cancelOption);
                optionMapping[cancelOption] = "Cancel";

                var mainWindow = playniteApi.Dialogs.GetCurrentAppWindow();
                mainWindow?.Activate();

                MessageBoxOption selectedOption = playniteApi.Dialogs.ShowMessage(
                    "Select a download variant:",
                    "Download Variant",
                    MessageBoxImage.Question,
                    options);

                if (selectedOption != null &&
                    optionMapping.TryGetValue(selectedOption, out string chosenVariant) &&
                    !chosenVariant.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
                {
                    return chosenVariant;
                }
                return null;
            }

            private string GetStandardRegionCode(string fileName)
            {
                var regionMatch = Regex.Match(fileName, @"\((USA|EUR|JPN|JAP|EU|US|JP)\)", RegexOptions.IgnoreCase);
                if (regionMatch.Success)
                {
                    string code = regionMatch.Groups[1].Value.ToUpperInvariant();
                    if (code == "US") return "USA";
                    if (code == "EU") return "EUR";
                    if (code == "JP" || code == "JPN") return "JAP";
                    return code;
                }
                if (fileName.ToUpperInvariant().Contains("USA")) return "USA";
                if (fileName.ToUpperInvariant().Contains("EUR")) return "EUR";
                if (fileName.ToUpperInvariant().Contains("JPN") || fileName.ToUpperInvariant().Contains("JAP")) return "JAP";
                return "Other";
            }


            private string GetGamePlatformName()
            {
                if (Game.Platforms != null && Game.Platforms.Count > 0)
                {
                    var p = Game.Platforms[0];
                    if (p != null && !string.IsNullOrEmpty(p.Name))
                        return p.Name;
                }
#if PLAYNITE9_OR_10
    if (!string.IsNullOrEmpty(Game.Platform))
        return Game.Platform;
#endif
                return "";
            }





            private async Task HandleFitGirlDownload()
            {
                // Find the FitGirl game action by exact name.
                var downloadAction = Game.GameActions.FirstOrDefault(
                    a => a.Name.Equals("Download: FitGirl Repacks", StringComparison.OrdinalIgnoreCase));
                if (downloadAction == null || string.IsNullOrWhiteSpace(downloadAction.Path))
                {
                    playniteApi.Dialogs.ShowErrorMessage("Invalid FitGirl source URL selected.", "Error");
                    return;
                }
                string gameUrl = downloadAction.Path.Trim();

                // Build a secondary selection dialog for download type.
                var fitgirlOptions = new List<MessageBoxOption>();
                var fitgirlOptionMapping = new Dictionary<MessageBoxOption, string>();

                // Create options using the proper constructor.
                var ddlOption = new MessageBoxOption("DDL", true, false);
                fitgirlOptions.Add(ddlOption);
                fitgirlOptionMapping.Add(ddlOption, "DDL");

                var torrentOption = new MessageBoxOption("Torrent", false, false);
                fitgirlOptions.Add(torrentOption);
                fitgirlOptionMapping.Add(torrentOption, "Torrent");

                var cancelOption = new MessageBoxOption("Cancel", false, true);
                fitgirlOptions.Add(cancelOption);
                fitgirlOptionMapping.Add(cancelOption, "Cancel");

                // Show the dialog.
                MessageBoxOption selectedOption = playniteApi.Dialogs.ShowMessage(
                    "Select FitGirl download type:",
                    "FitGirl Installation",
                    MessageBoxImage.Question,
                    fitgirlOptions);

                // If the user cancels, exit.
                if (selectedOption == null ||
                   !fitgirlOptionMapping.TryGetValue(selectedOption, out string chosenType) ||
                   chosenType.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Call the appropriate handler.
                if (chosenType.Equals("DDL", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleFitGirlDDLDownload(gameUrl);
                }
                else if (chosenType.Equals("Torrent", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleFitGirlTorrentDownload(gameUrl);
                }
            }
            private async Task HandleFitGirlDDLDownload(string gameUrl)
            {
                // Load the FitGirl game page content
                string pageContent = await pluginInstance.LoadPageContent(gameUrl);

                // Extract the section with <h3>Download Mirrors (Direct Links)</h3>
                string sectionPattern = @"<h3[^>]*>\s*Download Mirrors\s*\(Direct Links\)\s*<\/h3>(?<section>[\s\S]+?)(?:<h3|$)";
                var sectionMatch = Regex.Match(pageContent, sectionPattern, RegexOptions.IgnoreCase);
                if (!sectionMatch.Success)
                {
                    playniteApi.Dialogs.ShowErrorMessage("Could not locate the download mirrors section.", "Parsing Error");
                    return;
                }
                string mirrorSection = sectionMatch.Groups["section"].Value;

                // First, try to find provider paste links (e.g. Filehoster: DataNodes, Filehoster: FuckingFast) that point to paste.fitgirl-repacks.site
                string pasteLinkPattern = @"<a\s+href\s*=\s*['""](?<url>https:\/\/paste\.fitgirl-repacks\.site\/[^\s'""]+)['""][^>]*>(?<label>[^<]+)<\/a>";
                var matches = Regex.Matches(mirrorSection, pasteLinkPattern, RegexOptions.IgnoreCase);

                var providerDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match m in matches)
                {
                    string label = m.Groups["label"].Value.Trim();
                    // Case-insensitive remove "Filehoster:" and "Filehoster"
                    string provider = Regex.Replace(label, @"Filehoster[:]?", "", RegexOptions.IgnoreCase).Trim();
                    string url = m.Groups["url"].Value.Trim();
                    if (!providerDict.ContainsKey(provider))
                        providerDict.Add(provider, url);
                }

                // Fallback: If no paste links found, look for direct host links like <a ... rel="noopener">Filehoster: X</a>
                if (providerDict.Count == 0)
                {
                    // Find all <a href="..."> links with rel="noopener">Filehoster: ...</a>
                    var fallbackMatches = Regex.Matches(
                        mirrorSection,
                        @"<a\s+[^>]*href\s*=\s*['""](?<url>[^'""]+)['""][^>]*rel\s*=\s*['""]noopener['""][^>]*>(?<label>Filehoster:[^<]+)<\/a>",
                        RegexOptions.IgnoreCase);

                    foreach (Match m in fallbackMatches)
                    {
                        string label = m.Groups["label"].Value.Trim();
                        string provider = Regex.Replace(label, @"Filehoster[:]?", "", RegexOptions.IgnoreCase).Trim();
                        string url = m.Groups["url"].Value.Trim();
                        if (!providerDict.ContainsKey(provider))
                            providerDict.Add(provider, url);
                    }
                }

                if (providerDict.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage("No recognized FitGirl DDL providers or direct links were found.", "Provider Error");
                    return;
                }

                string selectedProvider = ShowProviderSelectionDialog(providerDict);
                if (string.IsNullOrEmpty(selectedProvider))
                    return; // User canceled.

                if (!providerDict.TryGetValue(selectedProvider, out string providerPasteOrDirectUrl))
                {
                    playniteApi.Dialogs.ShowErrorMessage("Selected provider was not found.", "Selection Error");
                    return;
                }

                // If this is a paste link, use ScrapePasteLinksAndWriteToTxt.  
                // Otherwise, just write the direct link to the file.
                if (providerPasteOrDirectUrl.StartsWith("https://paste.fitgirl-repacks.site/", StringComparison.OrdinalIgnoreCase))
                {
                    await ScrapePasteLinksAndWriteToTxt(selectedProvider, providerPasteOrDirectUrl);
                }
                else
                {
                    // Write direct URL to file
                    string pluginDataDir = pluginInstance.GetPluginUserDataPath();
                    Directory.CreateDirectory(pluginDataDir);
                    string urlsFilePath = Path.Combine(pluginDataDir, "_Add_Urls_here.txt");
                    File.AppendAllLines(urlsFilePath, new[] { providerPasteOrDirectUrl });
                }
            }

            /// <summary>
            /// Scrapes the given paste.fitgirl-repacks.site page for all relevant download links (by provider) and writes them to _Add_Urls_here.txt.
            /// </summary>
            private async Task ScrapePasteLinksAndWriteToTxt(string provider, string pasteUrl)
            {
                try
                {
                    string pasteContent = await pluginInstance.LoadPageContent(pasteUrl);

                    // Find all <a href="..."> links with rel="noopener">
                    var linkMatches = Regex.Matches(
                        pasteContent,
                        @"<a\s+[^>]*href\s*=\s*['""](?<url>[^'""]+)['""][^>]*rel\s*=\s*['""]noopener['""][^>]*>(?<label>.*?)<\/a>",
                        RegexOptions.IgnoreCase);

                    var allLinks = new List<string>();

                    foreach (Match linkMatch in linkMatches)
                    {
                        string url = linkMatch.Groups["url"].Value.Trim();
                        string label = linkMatch.Groups["label"].Value.Trim();

                        // Use your existing GetProviderName to determine the provider for this link.
                        string detectedProvider = GetProviderName(url);

                        // Accept if provider matches, OR if label contains the provider (for more flexibility)
                        if (detectedProvider.Equals(provider, StringComparison.OrdinalIgnoreCase) ||
                            label.IndexOf(provider, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            allLinks.Add(url);
                        }
                    }

                    allLinks = allLinks.Distinct().ToList();

                    if (allLinks.Count == 0)
                    {
                        playniteApi.Dialogs.ShowErrorMessage($"No {provider} links found in page.", "Scraping Error");
                        return;
                    }

                    string pluginDataDir = pluginInstance.GetPluginUserDataPath();
                    Directory.CreateDirectory(pluginDataDir);
                    string urlsFilePath = Path.Combine(pluginDataDir, "_Add_Urls_here.txt");
                    File.AppendAllLines(urlsFilePath, allLinks);
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"Failed to scrape {provider} links: {ex.Message}", "Scraping Error");
                }
            }
            private async Task HandleFitGirlTorrentDownload(string gameUrl)
            {
                // Use our Torrent scraper – filter for magnet links or links associated with known torrent providers.
                List<string> allLinks = await ScrapeFitGirlLinksAsync(Game.Name, gameUrl);
                if (allLinks == null || allLinks.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"No FitGirl links found for {Game.Name}.", "Download Error");
                    return;
                }

                var torrentLinks = allLinks
                    .Where(link => link.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ||
                                   link.IndexOf("1337x", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   link.IndexOf("rutor", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                if (torrentLinks.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage("No FitGirl torrent links found.", "Download Error");
                    return;
                }

                Dictionary<string, string> providerDict = BuildProviderDictionary(torrentLinks);
                if (providerDict.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage("No recognized FitGirl torrent providers were found.", "Provider Error");
                    return;
                }

                string selectedProvider = ShowProviderSelectionDialog(providerDict);
                if (string.IsNullOrEmpty(selectedProvider))
                {
                    return; // User canceled.
                }

                if (providerDict.TryGetValue(selectedProvider, out string providerUrl))
                {
                    await OpenDownloadLinkForProviderAsync(selectedProvider, providerUrl);
                }
                else
                {
                    playniteApi.Dialogs.ShowErrorMessage("Selected provider was not found.", "Selection Error");
                }
            }

            // Scrapes an ElAmigos page and returns a dictionary of provider groups to their download links.
            private async Task<Dictionary<string, List<string>>> ElamigosScrapeDownloadProviderGroupsAsync(string gameName, string gameUrl)
            {
                var providerGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                var otherLinks = new List<string>();

                try
                {
                    if (string.IsNullOrWhiteSpace(gameUrl))
                    {
                        playniteApi.Dialogs.ShowErrorMessage($"No valid download URL found for {gameName}.", "Download Error");
                        return providerGroups;
                    }

                    string pageContent = await pluginInstance.LoadPageContent(gameUrl);
                    if (string.IsNullOrWhiteSpace(pageContent))
                    {
                        playniteApi.Dialogs.ShowErrorMessage("Empty page content received.", "Scraping Error");
                        return providerGroups;
                    }

                    string currentGroup = null;
                    bool groupIsOther = false;
                    var groupLinks = new List<string>();

                    // Parse HTML: Match <h2> group headers and <a href=...> download links.
                    string pattern = @"(<h2[^>]*>(.*?)<\/h2>)|(<a\s+href\s*=\s*[""']([^""']+)[""'][^>]*>.*?<\/a>)";
                    var matches = Regex.Matches(pageContent, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

                    foreach (Match match in matches)
                    {
                        // If a group header is matched.
                        if (match.Groups[2].Success)
                        {
                            // If a previous group exists, store its links.
                            if (!string.IsNullOrEmpty(currentGroup) && groupLinks.Any())
                            {
                                if (groupIsOther)
                                    otherLinks.AddRange(groupLinks);
                                else
                                    providerGroups[currentGroup] = new List<string>(groupLinks);
                            }
                            // Start a new group.
                            string header = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value.Trim());
                            currentGroup = header;
                            groupLinks.Clear();
                            // Replace Contains(...) with IndexOf(...) >= 0.
                            groupIsOther = header.IndexOf("update", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           header.IndexOf("crack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           header.IndexOf(gameName, StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                        // Else if a download link is matched.
                        else if (match.Groups[4].Success)
                        {
                            string href = match.Groups[4].Value;
                            if (string.IsNullOrWhiteSpace(href))
                                continue;
                            // Skip YouTube links.
                            if (href.IndexOf("youtube.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                href.IndexOf("youtu.be", StringComparison.OrdinalIgnoreCase) >= 0)
                                continue;
                            groupLinks.Add(href.Trim());
                        }
                    }
                    // Store the final group.
                    if (!string.IsNullOrEmpty(currentGroup) && groupLinks.Any())
                    {
                        if (groupIsOther)
                            otherLinks.AddRange(groupLinks);
                        else
                            providerGroups[currentGroup] = new List<string>(groupLinks);
                    }
                    // Add "Other" group if extra links exist.
                    if (otherLinks.Any())
                        providerGroups["Other"] = otherLinks;

                    // Remove any groups with no links.
                    foreach (var key in providerGroups.Keys.Where(key => providerGroups[key].Count == 0).ToList())
                        providerGroups.Remove(key);

                    return providerGroups;
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"Error while scraping provider groups: {ex.Message}", "Scraping Error");
                    return providerGroups;
                }
            }

            // Main download handler for Elamigos.
            // Main handler for ElAmigos download workflow.
            private async Task HandleElamigosDownload()
            {
                // 1. Find the ElAmigos download action for the current game.
                var downloadAction = Game.GameActions
                    .FirstOrDefault(a => a.Name.Equals("Download: ElAmigos", StringComparison.OrdinalIgnoreCase));
                if (downloadAction == null || string.IsNullOrEmpty(downloadAction.Path))
                {
                    playniteApi.Dialogs.ShowErrorMessage("Invalid source URL selected.", "Error");
                    return;
                }

                string gameUrl = downloadAction.Path;

                // 2. Scrape the download page for provider groups.
                Dictionary<string, List<string>> groups = await ElamigosScrapeDownloadProviderGroupsAsync(Game.Name, gameUrl);
                if (groups == null || groups.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"No provider groups found for {Game.Name}.", "Download Error");
                    return;
                }

                // Filter groups so only "DDOWNLOAD", "RAPIDGATOR", and "Other" remain.
                groups = groups.Where(kvp =>
                         kvp.Key.Equals("DDOWNLOAD", StringComparison.OrdinalIgnoreCase) ||
                         kvp.Key.Equals("RAPIDGATOR", StringComparison.OrdinalIgnoreCase) ||
                         kvp.Key.Equals("Other", StringComparison.OrdinalIgnoreCase))
                     .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

                if (groups.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage("No valid providers available for ElAmigos.", "Download Error");
                    return;
                }

                // 3. Let the user select a provider group.
                string[] groupOptions = groups.Keys.ToArray();
                string selectedGroup = ElamigosShowGroupSelectionDialog("Select Provider Group", groupOptions);
                if (string.IsNullOrEmpty(selectedGroup))
                {
                    return; // Cancelled
                }

                // 4. Build a provider dictionary for the selected group.
                List<string> groupLinks = groups[selectedGroup];
                Dictionary<string, string> providerDict = ElamigosBuildProviderDictionary(groupLinks);
                if (providerDict.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage("No recognized providers were found in the selected group.", "Provider Error");
                    return;
                }

                // 5. Let the user select a provider within the group.
                string selectedProvider = ElamigosShowProviderSelectionDialog(providerDict);
                if (string.IsNullOrEmpty(selectedProvider))
                {
                    return; // Cancelled
                }

                // 6. Open the download link for the selected provider.
                if (providerDict.TryGetValue(selectedProvider, out string providerUrl))
                {
                    await ElamigosOpenDownloadLinkForProviderAsync(selectedProvider, providerUrl);
                }
                else
                {
                    playniteApi.Dialogs.ShowErrorMessage("Selected provider was not found.", "Selection Error");
                }
            }


            // Show a dialog asking the user to select a group.
            private string ElamigosShowGroupSelectionDialog(string title, string[] options)
            {
                var msgOptions = new List<MessageBoxOption>();
                var mapping = new Dictionary<MessageBoxOption, string>();
                bool isFirst = true;
                foreach (string option in options)
                {
                    var msgOption = new MessageBoxOption(option, isFirst, false);
                    isFirst = false;
                    msgOptions.Add(msgOption);
                    mapping[msgOption] = option;
                }
                // Cancel option
                var cancel = new MessageBoxOption("Cancel", false, true);
                msgOptions.Add(cancel);
                mapping[cancel] = "Cancel";

                var mainWindow = playniteApi.Dialogs.GetCurrentAppWindow();
                mainWindow?.Activate();

                MessageBoxOption selected = playniteApi.Dialogs.ShowMessage(
                    title,
                    "Select a group:",
                    MessageBoxImage.Question,
                    msgOptions);

                if (selected != null && mapping.TryGetValue(selected, out string chosen) && chosen != "Cancel")
                {
                    return chosen;
                }
                return null;
            }

            // Builds a dictionary mapping provider names to their URLs for Elamigos.
            private Dictionary<string, string> ElamigosBuildProviderDictionary(List<string> links)
            {
                var providerDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var link in links)
                {
                    string provider = ElamigosGetProviderName(link);
                    if (!providerDict.ContainsKey(provider))
                    {
                        providerDict.Add(provider, link);
                    }
                }
                return providerDict;
            }

            // Determines the provider name from the given URL.
            private string ElamigosGetProviderName(string url)
            {
                if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    int colonIndex = url.IndexOf(":");
                    if (colonIndex > 0)
                    {
                        string potentialPrefix = url.Substring(0, colonIndex).Trim();
                        if (potentialPrefix.Equals("1337x", StringComparison.OrdinalIgnoreCase) ||
                            potentialPrefix.Equals("rutor", StringComparison.OrdinalIgnoreCase))
                        {
                            return potentialPrefix;
                        }
                    }
                    if (url.IndexOf("1337x", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "1337x";
                    if (url.IndexOf("rutor", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "RuTor";
                    foreach (var param in url.Split('&'))
                    {
                        if (param.ToLower().Contains("1337x.to"))
                            return "1337x";
                        if (param.ToLower().Contains("rutor"))
                            return "RuTor";
                    }
                    return "Torrent";
                }

                // Direct download link checks:
                if (url.IndexOf("1337x.to", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "1337x";
                if (url.IndexOf("rutor", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "RuTor";
                if (url.IndexOf("datanodes", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Datanodes";
                if (url.IndexOf("fuckingfast", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "FuckingFast";
                if (url.IndexOf("megadb.net", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "MegaDB";
                if (url.IndexOf("gofile.io", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "GoFile";
                if (url.IndexOf("1fichier.com", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "1Fichier";
                if (url.IndexOf("keeplinks.org", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Keeplinks";
                if (url.IndexOf("filecrypt", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "FileCrypt";
                if (url.IndexOf("buzzheavier.com", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "BuzzHeavy";

                // Fallback: return the trimmed URL.
                return url.Trim();
            }

            // Show a dialog asking the user to select a provider.
            private string ElamigosShowProviderSelectionDialog(Dictionary<string, string> providerDict)
            {
                var options = new List<MessageBoxOption>();
                var optionMapping = new Dictionary<MessageBoxOption, string>();
                bool isFirst = true;
                foreach (string provider in providerDict.Keys)
                {
                    var option = new MessageBoxOption(provider, isFirst, false);
                    isFirst = false;
                    options.Add(option);
                    optionMapping[option] = provider;
                }
                var cancelOption = new MessageBoxOption("Cancel", false, true);
                options.Add(cancelOption);
                optionMapping[cancelOption] = "Cancel";

                var mainWindow = playniteApi.Dialogs.GetCurrentAppWindow();
                mainWindow?.Activate();

                MessageBoxOption selectedOption = playniteApi.Dialogs.ShowMessage(
                    "Select a download provider:",
                    "Download Provider",
                    MessageBoxImage.Question,
                    options);

                if (selectedOption != null &&
                    optionMapping.TryGetValue(selectedOption, out string chosenProvider) &&
                    chosenProvider != "Cancel")
                {
                    return chosenProvider;
                }
                return null;
            }

            // Open the selected provider's download link, with special handling for some hosts.
            private async Task ElamigosOpenDownloadLinkForProviderAsync(string provider, string url)
            {
                string pluginDataDir = pluginInstance.GetPluginUserDataPath();
                Directory.CreateDirectory(pluginDataDir);

                if (provider.Equals("1Fichier", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string pageContent = await pluginInstance.LoadPageContent(url);
                        Regex regex = new Regex(
                            @"<form[^>]+action=[""'](?<dlUrl>[^""']+)[""'][^>]*>.*?<input[^>]+id=[""']dlb[""'][^>]*>",
                            RegexOptions.Singleline | RegexOptions.IgnoreCase);
                        Match match = regex.Match(pageContent);
                        if (match.Success)
                        {
                            url = match.Groups["dlUrl"].Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        playniteApi.Dialogs.ShowErrorMessage($"Error extracting 1Fichier download link: {ex.Message}", "Parsing Error");
                    }
                }
                else if (provider.Equals("BuzzHeavy", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string buzzContent = await pluginInstance.LoadPageContent(url);
                        Regex regexBuzz = new Regex(
                            @"<a\s+class=[""']link-button\s+gay-button[""'][^>]*hx-get=[""'](?<dlUrl>[^""']+)[""'][^>]*>",
                            RegexOptions.IgnoreCase | RegexOptions.Singleline);
                        Match matchBuzz = regexBuzz.Match(buzzContent);
                        if (matchBuzz.Success)
                        {
                            string hxUrl = matchBuzz.Groups["dlUrl"].Value;
                            if (hxUrl.StartsWith("/"))
                            {
                                hxUrl = "https://buzzheavier.com" + hxUrl;
                            }
                            if (matchBuzz.Value.IndexOf("data-clicked", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                url = hxUrl;
                            }
                            else
                            {
                                using (HttpClient client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }))
                                {
                                    HttpResponseMessage response = await client.GetAsync(hxUrl);
                                    url = response.RequestMessage.RequestUri.ToString();
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        playniteApi.Dialogs.ShowErrorMessage($"Error extracting BuzzHeavy download link: {ex.Message}", "Parsing Error");
                    }
                }
                else if (provider.Equals("FuckingFast", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string pageContent = await pluginInstance.LoadPageContent(url);
                        string pattern = @"<a\s+[^>]*href\s*=\s*[""'](?<dlUrl>[^""']+)[""'][^>]*>(?<linkText>.*?)</a>";
                        Regex regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                        MatchCollection matches = regex.Matches(pageContent);
                        string selectedUrl = null;
                        foreach (Match match in matches)
                        {
                            string linkText = match.Groups["linkText"].Value.Trim();
                            if (linkText.Equals("Filehoster: FuckingFast", StringComparison.OrdinalIgnoreCase))
                            {
                                selectedUrl = match.Groups["dlUrl"].Value.Trim();
                                break;
                            }
                        }
                        if (!string.IsNullOrEmpty(selectedUrl))
                        {
                            url = selectedUrl;
                        }
                        else
                        {
                            playniteApi.Dialogs.ShowErrorMessage("Could not locate the 'Filehoster: FuckingFast' link.", "Parsing Error");
                        }
                    }
                    catch (Exception ex)
                    {
                        playniteApi.Dialogs.ShowErrorMessage($"Error extracting FuckingFast link: {ex.Message}", "Parsing Error");
                    }
                }
                // For all providers: Write the url to file and run the Python script
                try
                {
                    string urlsFilePath = Path.Combine(pluginDataDir, "_Add_Urls_here.txt");
                    File.AppendAllText(urlsFilePath, url + Environment.NewLine);

                    string pythonScriptPath = Path.Combine(pluginDataDir, "fucking fast.py");
                    var psi = new ProcessStartInfo
                    {
                        FileName = "python", // or "python3" if that's what your system uses
                        Arguments = $"\"{pythonScriptPath}\"",
                        WorkingDirectory = pluginDataDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var process = Process.Start(psi))
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();

                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            playniteApi.Dialogs.ShowErrorMessage($"Python script error: {error}", $"{provider} Extraction");
                        }
                        // Optionally process 'output' if needed
                    }
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"Error running extraction for {provider}: {ex.Message}", "Python Script Error");
                }
            }
            // (Optional) Scrape all links from an ElAmigos page (not grouped by provider)
            private async Task<List<string>> ElamigosScrapeSiteForLinksAsync(string gameName, string gameUrl)
            {
                var links = new List<string>();
                try
                {
                    if (string.IsNullOrEmpty(gameUrl))
                    {
                        playniteApi.Dialogs.ShowErrorMessage($"No valid download URL found for {gameName}.", "Download Error");
                        return links;
                    }

                    string pageContent = await pluginInstance.LoadPageContent(gameUrl);
                    if (string.IsNullOrEmpty(pageContent))
                    {
                        playniteApi.Dialogs.ShowErrorMessage("Empty page content received.", "Scraping Error");
                        return links;
                    }

                    // Use a regex to extract all anchor tag href values.
                    var matches = Regex.Matches(pageContent, @"<a\s+href=[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase);
                    foreach (Match match in matches)
                    {
                        string url = match.Groups["url"].Value.Trim();

                        // Prepend "https:" if necessary.
                        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            url = "https:" + url;
                        }

                        // Skip YouTube links.
                        if (url.IndexOf("youtube.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            url.IndexOf("youtu.be", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            continue;
                        }

                        links.Add(url);
                    }
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"Error while scraping download links: {ex.Message}", "Scraping Error");
                }

                return links;
            }


            // Helper: Scrape SteamRip page for download links.
            private async Task<List<string>> ScrapeSiteForLinksAsync(string gameName, string gameUrl)
            {
                try
                {
                    if (string.IsNullOrEmpty(gameUrl))
                    {
                        playniteApi.Dialogs.ShowErrorMessage($"No valid download URL found for {gameName}.", "Download Error");
                        return new List<string>();
                    }

                    string pageContent = await pluginInstance.LoadPageContent(gameUrl);

                    var links = new List<string>();

                    // 1. Extract protocol-less links for known providers (steamrip etc.)
                    var matches = Regex.Matches(pageContent,
                        @"<a\s+href=[""'](//(?:megadb\.net|gofile\.io|1fichier\.com|filecrypt\.co|buzzheavier\.com)[^\s""']+)[""']",
                        RegexOptions.IgnoreCase);
                    links.AddRange(matches.Cast<Match>()
                                          .Select(m => "https:" + m.Groups[1].Value.Trim()));

                    // 2. Extract relative or absolute .zip/.7z/.rar/.wua/.iso links for Myrient and other archives
                    //    Matches: <a href="Something.zip"> or <a href='/path/Another.7z'> etc.
                    var archiveMatches = Regex.Matches(pageContent,
                        @"<a\s+[^>]*href\s*=\s*[""']([^""'>]+\.(zip|7z|rar|iso|wua))[""']",
                        RegexOptions.IgnoreCase);
                    links.AddRange(
                        archiveMatches.Cast<Match>()
                                      .Select(m => m.Groups[1].Value.Trim())
                                      .Where(href => !string.IsNullOrEmpty(href))
                    );

                    // Log found links
                    string logFilePath = Path.Combine(pluginInstance.PlayniteApi.Paths.ConfigurationPath, "install_links.txt");
                    if (!System.IO.File.Exists(logFilePath))
                    {
                        System.IO.File.Create(logFilePath).Dispose();
                    }
                    using (StreamWriter writer = new StreamWriter(logFilePath, true))
                    {
                        writer.WriteLine($"GameName: {gameName}");
                        for (int i = 0; i < links.Count; i++)
                        {
                            writer.WriteLine($"Url {i + 1} found: {links[i]}");
                        }
                        writer.WriteLine();
                    }

                    return links;
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"Error while scraping download links: {ex.Message}", "Scraping Error");
                    return new List<string>();
                }
            }

            private async Task HandleSteamRipDownload()
            {
                var downloadAction = Game.GameActions?
                    .FirstOrDefault(a => !string.IsNullOrEmpty(a.Name) && a.Name.Equals("Download: SteamRip", StringComparison.OrdinalIgnoreCase));
                if (downloadAction == null || string.IsNullOrEmpty(downloadAction.Path))
                {
                    playniteApi.Dialogs.ShowErrorMessage("Invalid source URL selected.", "Error");
                    return;
                }

                string gameUrl = downloadAction.Path;

                // No thread blocking: use await
                List<string> links = await ScrapeSiteForLinksAsync(Game.Name, gameUrl);
                if (links == null || links.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"No download links found for {Game.Name}.", "Download Error");
                    return;
                }

                Dictionary<string, string> providerDict = BuildProviderDictionary(links);
                if (providerDict == null || providerDict.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage("No recognized providers were found.", "Provider Error");
                    return;
                }

                // Show selection dialog on UI thread
                string selectedProvider = ShowProviderSelectionDialog(providerDict);
                if (string.IsNullOrEmpty(selectedProvider))
                {
                    return; // User canceled.
                }

                if (providerDict.TryGetValue(selectedProvider, out string providerUrl) && !string.IsNullOrEmpty(providerUrl))
                {
                    await OpenDownloadLinkForProviderAsync(selectedProvider, providerUrl);

                    // --- SCRAPE VERSION FROM PAGE ---
                    string version = "Unknown";
                    try
                    {
                        string pageContent = await pluginInstance.LoadPageContent(gameUrl);
                        // Extract everything after <strong>Version</strong> and optional colon/dash, up to < or line end
                        var regex = new Regex(@"<strong>\s*Version\s*<\/strong>\s*[:\-]?\s*(?<ver>[^\r\n<]+)", RegexOptions.IgnoreCase);
                        var match = regex.Match(pageContent);
                        if (match.Success)
                        {
                            version = match.Groups["ver"].Value.Trim();
                        }
                    }
                    catch
                    {
                        version = "Unknown";
                    }

                    // --- ADD/UPDATE IN MY GAMES.TXT ---
                    string pluginDataDir = pluginInstance.GetPluginUserDataPath();
                    Directory.CreateDirectory(pluginDataDir);
                    string myGamesPath = Path.Combine(pluginDataDir, "My Games.txt");

                    try
                    {
                        string lineToWrite = $"Name: {Game.Name}, Source: SteamRip, Version: {version}";
                        List<string> lines = new List<string>();
                        if (File.Exists(myGamesPath))
                        {
                            lines = File.ReadAllLines(myGamesPath).ToList();
                        }
                        bool found = false;
                        for (int i = 0; i < lines.Count; i++)
                        {
                            if (lines[i].StartsWith($"Name: {Game.Name},"))
                            {
                                lines[i] = lineToWrite;
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            lines.Add(lineToWrite);
                        }
                        File.WriteAllLines(myGamesPath, lines);
                    }
                    catch (Exception ex)
                    {
                        playniteApi.Dialogs.ShowErrorMessage($"Error writing SteamRip entry to My Games.txt: {ex.Message}", "File Error");
                    }
                }
                else
                {
                    playniteApi.Dialogs.ShowErrorMessage("Selected provider was not found or link is invalid.", "Selection Error");
                }
            }

            private Task HandleSteamCMDInstall()
            {
                // 1. Locate SteamCMD executable
                string pluginDataDir = pluginInstance.GetPluginUserDataPath();
                string steamCmdPath = Path.Combine(pluginDataDir, "steamcmd", "steamcmd.exe");
                if (!File.Exists(steamCmdPath))
                {
                    playniteApi.Dialogs.ShowErrorMessage(
                        "SteamCMD is missing.\n\nPlease add steamcmd.exe to the 'steamcmd' folder inside the plugin data directory:\n" +
                        pluginDataDir + "\\steamcmd",
                        "SteamCMD Not Found");
                    return Task.CompletedTask;
                }

                // 2. Prompt user for install drive
                string selectedDrive = ShowSteamDriveSelectionDialog();
                if (string.IsNullOrWhiteSpace(selectedDrive))
                {
                    playniteApi.Dialogs.ShowErrorMessage(
                        "No available drives found or selection cancelled.\n" +
                        "Please ensure you have a writable fixed drive connected.",
                        "Drive Selection");
                    return Task.CompletedTask;
                }

                // 3. Build install directory using Steam's default structure
                string installDir = Path.Combine(selectedDrive, "SteamLibrary", "steamapps", "common", SanitizePath(Game.Name));
                try
                {
                    Directory.CreateDirectory(installDir);
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage(
                        $"Failed to create install directory:\n{installDir}\n\n{ex.Message}", "Directory Error");
                    return Task.CompletedTask;
                }

                // 4. Get Steam App ID from Game (GameId or Feature)
                string appId = GetSteamAppId(Game);
                if (string.IsNullOrWhiteSpace(appId))
                {
                    var result = playniteApi.Dialogs.SelectString(
                        "Enter Steam AppID for this game:",
                        "SteamCMD Install", "");
                    if (result != null && result.Result && !string.IsNullOrWhiteSpace(result.SelectedString))
                    {
                        appId = result.SelectedString.Trim();
                    }
                    else
                    {
                        playniteApi.Dialogs.ShowErrorMessage(
                            "Steam AppID is required to continue SteamCMD install.", "SteamCMD Error");
                        return Task.CompletedTask;
                    }
                }

                // 5. Prompt user for Steam login or anonymous
                var loginResult = playniteApi.Dialogs.SelectString(
                    "Enter your Steam username (leave blank for anonymous):",
                    "SteamCMD Login", ""
                );
                string steamUser = loginResult?.SelectedString?.Trim();
                string steamPass = "";

                bool useAnonymous = string.IsNullOrWhiteSpace(steamUser);
                if (!useAnonymous)
                {
                    // Prompt for password if username is given
                    var passResult = playniteApi.Dialogs.SelectString(
                        "Enter your Steam password:",
                        "SteamCMD Password", ""
                    );
                    steamPass = passResult?.SelectedString?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(steamPass))
                    {
                        playniteApi.Dialogs.ShowErrorMessage(
                            "Steam password is required when a username is provided.", "SteamCMD Error");
                        return Task.CompletedTask;
                    }
                }

                // 6. Build SteamCMD script file (one command per line)
                string scriptPath = Path.Combine(pluginDataDir, "steamcmd_script.txt");
                string script;
                if (useAnonymous)
                {
                    script = string.Join(Environment.NewLine, new[]
                    {
            "+login anonymous",
            $@"+force_install_dir ""{installDir}""",
            $@"+app_update {appId} validate",
            "+quit"
        });
                    try
                    {
                        File.WriteAllText(scriptPath, script);
                    }
                    catch (Exception ex)
                    {
                        playniteApi.Dialogs.ShowErrorMessage(
                            $"Failed to write SteamCMD script:\n{scriptPath}\n\n{ex.Message}", "File Error");
                        return Task.CompletedTask;
                    }
                }

                // 7. Run SteamCMD process and show the console (not silent), while updating Playnite progress from log
                playniteApi.Dialogs.ActivateGlobalProgress((progress) =>
                {
                    progress.Text = "Starting SteamCMD install...";

                    // Use -Log to force SteamCMD to write its output to a log file
                    string logPath = Path.Combine(pluginDataDir, "steamcmd_output.log");
                    if (File.Exists(logPath))
                    {
                        try { File.Delete(logPath); } catch { }
                    }

                    string arguments = useAnonymous
                        ? $"-Log \"{logPath}\" +runscript \"{scriptPath}\""
                        : $"-Log \"{logPath}\" +login {steamUser} {steamPass} +force_install_dir \"{installDir}\" +app_update {appId} validate +quit";

                    var psi = new ProcessStartInfo
                    {
                        FileName = steamCmdPath,
                        Arguments = arguments,
                        WorkingDirectory = Path.GetDirectoryName(steamCmdPath),
                        UseShellExecute = true, // visible window
                        CreateNoWindow = false
                    };

                    using (var process = Process.Start(psi))
                    {
                        // Background task: parse log for percent and update Playnite
                        Task.Run(() =>
                        {
                            int lastPercent = -1;
                            while (process != null && !process.HasExited)
                            {
                                if (File.Exists(logPath))
                                {
                                    try
                                    {
                                        foreach (var line in File.ReadLines(logPath))
                                        {
                                            // Downloading
                                            var match = System.Text.RegularExpressions.Regex.Match(line, @"downloading, progress: ([\d\.]+)");
                                            if (match.Success && float.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dl))
                                            {
                                                int percent = (int)dl;
                                                if (percent != lastPercent)
                                                {
                                                    progress.Text = $"Downloading {percent}%";
                                                    lastPercent = percent;
                                                }
                                            }
                                            // Installing
                                            match = System.Text.RegularExpressions.Regex.Match(line, @"installing, progress: ([\d\.]+)");
                                            if (match.Success && float.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float il))
                                            {
                                                int percent = (int)il;
                                                if (percent != lastPercent)
                                                {
                                                    progress.Text = $"Installing {percent}%";
                                                    lastPercent = percent;
                                                }
                                            }
                                            // Validating
                                            match = System.Text.RegularExpressions.Regex.Match(line, @"validating, progress: ([\d\.]+)");
                                            if (match.Success && float.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float vl))
                                            {
                                                int percent = (int)vl;
                                                if (percent != lastPercent)
                                                {
                                                    progress.Text = $"Validating {percent}%";
                                                    lastPercent = percent;
                                                }
                                            }
                                        }
                                    }
                                    catch { /* ignore file in use/read errors */ }
                                }
                                Thread.Sleep(500);
                            }
                        });

                        process.WaitForExit();

                        if (process.ExitCode == 5)
                        {
                            playniteApi.Dialogs.ShowErrorMessage(
                                "SteamCMD login failed (code 5). This usually means:\n" +
                                "- Incorrect username or password\n" +
                                "- Steam Guard code required and not entered or entered incorrectly\n" +
                                "- Account is locked or requires additional authentication (CAPTCHA)\n\n" +
                                "Please try again. If Steam Guard is enabled, watch for the prompt in the console window and enter your code.",
                                "SteamCMD Login Error"
                            );
                        }
                        else if (process.ExitCode != 0)
                        {
                            playniteApi.Dialogs.ShowErrorMessage(
                                $"SteamCMD exited with code {process.ExitCode}. Check the console window for errors.",
                                "SteamCMD Error");
                        }
                        else
                        {
                            playniteApi.Dialogs.ShowMessage(
                                $"SteamCMD completed. Game installed to:\n{installDir}\n\nCheck the output folder for details.",
                                "SteamCMD Success");
                        }

                        // Update the Playnite install directory for the game
                        Game.InstallDirectory = installDir;
                        playniteApi.Database.Games.Update(Game);
                    }
                }, new GlobalProgressOptions("Installing via SteamCMD", false));

                return Task.CompletedTask;
            }
            private string ShowSteamDriveSelectionDialog()
            {
                var validDrives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                    .Select(d => d.RootDirectory.FullName)
                    .ToList();

                if (validDrives.Count == 0)
                    return null;

                var options = new List<MessageBoxOption>();
                var optionMapping = new Dictionary<MessageBoxOption, string>();
                bool isFirst = true;
                foreach (string driveRoot in validDrives)
                {
                    var option = new MessageBoxOption(driveRoot, isFirst, false); // Show just "C:\"
                    isFirst = false;
                    options.Add(option);
                    optionMapping[option] = driveRoot;
                }
                var cancelOption = new MessageBoxOption("Cancel", false, true);
                options.Add(cancelOption);
                optionMapping[cancelOption] = "Cancel";

                var selectedOption = playniteApi.Dialogs.ShowMessage(
                    "Select a drive for SteamCMD install:",
                    "SteamCMD - Choose Drive",
                    MessageBoxImage.Question,
                    options);

                if (selectedOption != null &&
                    optionMapping.TryGetValue(selectedOption, out string chosenDrive) &&
                    !chosenDrive.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
                {
                    return chosenDrive;
                }
                return null;
            }

            private string SanitizePath(string name)
            {
                var invalidChars = Path.GetInvalidFileNameChars();
                var builder = new System.Text.StringBuilder(name.Length);
                foreach (char c in name)
                {
                    builder.Append(invalidChars.Contains(c) ? '_' : c);
                }
                return builder.ToString();
            }

            private string GetSteamAppId(Game game)
            {
                if (!string.IsNullOrWhiteSpace(game.GameId) && game.GameId.All(char.IsDigit))
                    return game.GameId;

                var feature = game.Features?
                    .FirstOrDefault(f => f.Name.StartsWith("[SteamAppId:", StringComparison.OrdinalIgnoreCase));
                if (feature != null)
                {
                    var match = Regex.Match(feature.Name, @"\[SteamAppId:\s*(\d+)\]", RegexOptions.IgnoreCase);
                    if (match.Success)
                        return match.Groups[1].Value;
                }
                return null;
            }

            // Call this after successfully downloading/installing a Steam game using SteamCMD.
            // Place this call after your SteamCMD process exits and validates successfully.

            private void FinalizeSteamGameInstall(Game game, string userDataPath)
            {
                // 1. Try to determine the install directory for the Steam game.
                string foundInstallDir = SearchForSteamGameInstallDirectory(game.Name);

                if (string.IsNullOrEmpty(foundInstallDir))
                {
                    LogToInstall($"Steam install directory not found for {game.Name}. Using previous InstallDirectory: {game.InstallDirectory ?? "(null)"}");
                    foundInstallDir = game.InstallDirectory; // fallback to whatever the last known is
                }
                else
                {
                    LogToInstall($"Steam install directory found for {game.Name}: {foundInstallDir}");
                    game.InstallDirectory = foundInstallDir;
                }

                // 2. Update Steam game actions using Steam-specific logic.
                UpdateSteamGameActionsAndStatus(game, userDataPath);

                // 3. Save to database.
                API.Instance.Database.Games.Update(game);

                // 4. Signal install event (optional).
                InvokeOnInstalled(new GameInstalledEventArgs(game.Id));
            }

            /// <summary>
            /// Searches for the Steam game install directory using the common Steam library structure.
            /// Looks for the game in all SteamLibrary/common subfolders and validates by .exe presence.
            /// </summary>
            private string SearchForSteamGameInstallDirectory(string gameName)
            {
                string normalizedGameName = NormalizeGameName(gameName);

                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady)
                        continue;

                    // Look for Steam library folder on this drive.
                    string steamLibrary = Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps", "common");
                    if (!Directory.Exists(steamLibrary))
                        continue;

                    // Enumerate all subdirectories in the steam library.
                    foreach (string folder in Directory.GetDirectories(steamLibrary))
                    {
                        string folderName = Path.GetFileName(folder);
                        if (NormalizeGameName(folderName).Equals(normalizedGameName, StringComparison.Ordinal))
                        {
                            // Check if this folder contains any executables.
                            var exeFiles = Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories);
                            if (exeFiles != null && exeFiles.Length > 0)
                            {
                                LogToInstall($"[Steam] Found install for '{gameName}' at '{folder}' with {exeFiles.Length} .exe(s).");
                                return folder;
                            }
                            else
                            {
                                LogToInstall($"[Steam] Folder '{folder}' matched '{gameName}' but contains no .exe files.");
                            }
                        }
                    }
                }

                LogToInstall($"[Steam] No install folder found for '{gameName}'.");
                return string.Empty;
            }

            /// <summary>
            /// Updates the GameActions for a Steam game after install, using Steam-specific exclusions and logic.
            /// </summary>
            private void UpdateSteamGameActionsAndStatus(Game game, string userDataPath)
            {
                if (string.IsNullOrEmpty(game.InstallDirectory) || !Directory.Exists(game.InstallDirectory))
                {
                    LogToInstall($"[Steam] Error: InstallDirectory invalid or missing for {game.Name}.");
                    return;
                }

                LogToInstall($"[Steam] Scanning directory: {game.InstallDirectory} for {game.Name}");

                // Load exclusions from "SteamExclusions.txt" (Steam-specific!)
                var exclusionsPath = Path.Combine(userDataPath, "SteamExclusions.txt");
                var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (System.IO.File.Exists(exclusionsPath))
                {
                    var exclusionLines = System.IO.File.ReadAllLines(exclusionsPath);
                    foreach (var line in exclusionLines)
                    {
                        var trimmedLine = line.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmedLine))
                        {
                            exclusions.Add(trimmedLine.ToLower());
                        }
                    }
                }
                else
                {
                    LogToInstall($"[Steam] No SteamExclusions.txt found at {exclusionsPath}, no exclusions applied.");
                }

                // Find all .exe files recursively in install dir.
                var exeFiles = Directory.GetFiles(game.InstallDirectory, "*.exe", SearchOption.AllDirectories);
                int addedActions = 0;

                foreach (var exeFile in exeFiles)
                {
                    string relativePath = GetRelativePathCustom(game.InstallDirectory, exeFile);
                    var segments = relativePath.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

                    // Exclude if any folder in path contains "redist" or "redsit".
                    bool skipDueToRedist = segments.Any(seg => seg.ToLower().Contains("redist") || seg.ToLower().Contains("redsit"));
                    if (skipDueToRedist)
                    {
                        LogToInstall($"[Steam] Skipped {exeFile} due to 'redist' folder in path.");
                        continue;
                    }

                    string exeName = Path.GetFileNameWithoutExtension(exeFile).ToLower();
                    if (exclusions.Contains(exeName))
                    {
                        LogToInstall($"[Steam] Skipped {exeFile} (exe '{exeName}' is in Steam exclusions).");
                        continue;
                    }

                    if (game.GameActions.Any(a => a.Name.Equals(Path.GetFileNameWithoutExtension(exeFile), StringComparison.OrdinalIgnoreCase)))
                    {
                        LogToInstall($"[Steam] Skipped {exeFile} due to duplicate action.");
                        continue;
                    }

                    var action = new GameAction
                    {
                        Name = Path.GetFileNameWithoutExtension(exeFile),
                        Type = GameActionType.File,
                        Path = exeFile,
                        WorkingDir = Path.GetDirectoryName(exeFile),
                        IsPlayAction = true
                    };

                    game.GameActions.Add(action);
                    addedActions++;
                    LogToInstall($"[Steam] Added game action for exe: {exeFile}");
                }

                LogToInstall($"[Steam] Total new game actions added: {addedActions}. Total actions now: {game.GameActions.Count}");

                // No need to update database here, caller should do it after this method.
            }


            private async Task<List<string>> ScrapeFitGirlLinksAsync(string gameName, string gameUrl)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(gameUrl))
                    {
                        playniteApi.Dialogs.ShowErrorMessage($"No valid download URL found for {gameName}.", "Download Error");
                        return new List<string>();
                    }

                    string pageContent = await pluginInstance.LoadPageContent(gameUrl);
                    var matches = Regex.Matches(pageContent, @"<a\s+href=[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase);
                    List<string> links = matches
                        .Cast<Match>()
                        .Select(m => m.Groups["url"].Value.Trim())
                        .ToList();

                    // Log all found links for debugging.
                    string logFilePath = Path.Combine(pluginInstance.PlayniteApi.Paths.ConfigurationPath, "fitgirl_all_links.txt");
                    if (!System.IO.File.Exists(logFilePath))
                    {
                        System.IO.File.Create(logFilePath).Dispose();
                    }
                    using (StreamWriter writer = new StreamWriter(logFilePath, true))
                    {
                        writer.WriteLine($"GameName: {gameName}");
                        int i = 1;
                        foreach (var link in links)
                        {
                            writer.WriteLine($"Url {i} found: {link}");
                            i++;
                        }
                        writer.WriteLine();
                    }

                    return links;
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"Error while scraping FitGirl links: {ex.Message}", "Scraping Error");
                    return new List<string>();
                }
            }


            // Helper: Build provider dictionary.
            private Dictionary<string, string> BuildProviderDictionary(List<string> links)
            {
                var providerDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var link in links)
                {
                    string provider = GetProviderName(link);
                    if (!string.IsNullOrEmpty(provider) && !providerDict.ContainsKey(provider))
                    {
                        providerDict.Add(provider, link);
                    }
                }
                return providerDict;
            }

            // Helper: Determine provider name from URL.
            private string GetProviderName(string url)
            {
                if (string.IsNullOrWhiteSpace(url))
                    return "Unknown";

                // Magnet links (torrent providers/mirrors)
                if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    int colonIndex = url.IndexOf(":");
                    if (colonIndex > 0)
                    {
                        string potentialPrefix = url.Substring(0, colonIndex).Trim();
                        if (potentialPrefix.Equals("1337x", StringComparison.OrdinalIgnoreCase) ||
                            potentialPrefix.Equals("rutor", StringComparison.OrdinalIgnoreCase))
                        {
                            return potentialPrefix;
                        }
                    }
                    if (url.IndexOf("1337x", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "1337x";
                    if (url.IndexOf("rutor", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "RuTor";
                    // Check tracker parameters
                    var parameters = url.Split('&');
                    foreach (var param in parameters)
                    {
                        string lowerParam = param.ToLowerInvariant();
                        if (lowerParam.Contains("1337x.to"))
                            return "1337x";
                        if (lowerParam.Contains("rutor"))
                            return "RuTor";
                    }
                    return "Torrent";
                }

                // DDL and filehosters (add new here as needed)
                if (url.IndexOf("1337x.to", StringComparison.OrdinalIgnoreCase) >= 0) return "1337x";
                if (url.IndexOf("rutor", StringComparison.OrdinalIgnoreCase) >= 0) return "RuTor";
                if (url.IndexOf("datanods", StringComparison.OrdinalIgnoreCase) >= 0) return "DataNods";
                if (url.IndexOf("datanodes", StringComparison.OrdinalIgnoreCase) >= 0) return "DataNods";
                if (url.IndexOf("filespayout", StringComparison.OrdinalIgnoreCase) >= 0) return "FilesPayout";
                if (url.IndexOf("swiftuploads", StringComparison.OrdinalIgnoreCase) >= 0) return "SwiftUploads";
                if (url.IndexOf("multi-uploads", StringComparison.OrdinalIgnoreCase) >= 0) return "Multi-Uploads";
                if (url.IndexOf("multiup.io", StringComparison.OrdinalIgnoreCase) >= 0) return "MultiUp";
                if (url.IndexOf("megadb.net", StringComparison.OrdinalIgnoreCase) >= 0) return "MegaDB";
                if (url.IndexOf("gofile.io", StringComparison.OrdinalIgnoreCase) >= 0) return "GoFile";
                if (url.IndexOf("1fichier.com", StringComparison.OrdinalIgnoreCase) >= 0) return "1Fichier";
                if (url.IndexOf("filecrypt.co", StringComparison.OrdinalIgnoreCase) >= 0) return "FileCrypt";
                if (url.IndexOf("buzzheavier.com", StringComparison.OrdinalIgnoreCase) >= 0) return "BuzzHeavier";
                if (url.IndexOf("ww25.public.upera.co", StringComparison.OrdinalIgnoreCase) >= 0) return "Upera";
                if (url.IndexOf("worldsrc.net", StringComparison.OrdinalIgnoreCase) >= 0) return "WorldSrc";
                if (url.IndexOf("pixeldrain.com", StringComparison.OrdinalIgnoreCase) >= 0) return "PixelDrain";
                if (url.IndexOf("www9.zippyshare.com", StringComparison.OrdinalIgnoreCase) >= 0) return "Zippyshare";
                if (url.IndexOf("letsupload.io", StringComparison.OrdinalIgnoreCase) >= 0) return "LetsUpload";
                if (url.IndexOf("qiwi.gg", StringComparison.OrdinalIgnoreCase) >= 0) return "QiwiGG";
                if (url.IndexOf("bayfiles.com", StringComparison.OrdinalIgnoreCase) >= 0) return "BayFiles";
                if (url.IndexOf("www51.zippyshare.com", StringComparison.OrdinalIgnoreCase) >= 0) return "Zippyshare";
                if (url.IndexOf("fuckingfast", StringComparison.OrdinalIgnoreCase) >= 0) return "FuckingFast";
                // Dodi-specific
                if (url.IndexOf("torrent", StringComparison.OrdinalIgnoreCase) >= 0) return "Torrent";
                if (url.IndexOf("filespayouts.com", StringComparison.OrdinalIgnoreCase) >= 0) return "FilesPayout";
                if (url.IndexOf("swiftuploads.com", StringComparison.OrdinalIgnoreCase) >= 0) return "SwiftUploads";
                if (url.IndexOf("dayuploads.com", StringComparison.OrdinalIgnoreCase) >= 0) return "DayUploads";
                if (url.IndexOf("tpi.li", StringComparison.OrdinalIgnoreCase) >= 0) return "TpiLi";
                if (url.IndexOf("go.zovo.ink", StringComparison.OrdinalIgnoreCase) >= 0) return "GoZovo";
                if (url.IndexOf("up-4ever.net", StringComparison.OrdinalIgnoreCase) >= 0) return "Up4Ever";
                // Fallback: return URL or Unknown
                return url.Trim();
            }

            private string ShowProviderSelectionDialog(Dictionary<string, string> providerDict)
            {
                List<MessageBoxOption> options = new List<MessageBoxOption>();
                Dictionary<MessageBoxOption, string> optionMapping = new Dictionary<MessageBoxOption, string>();
                bool isFirst = true;
                foreach (string provider in providerDict.Keys)
                {
                    var option = new MessageBoxOption(provider, isFirst, false);
                    isFirst = false;
                    options.Add(option);
                    optionMapping[option] = provider;
                }
                var cancelOption = new MessageBoxOption("Cancel", false, true);
                options.Add(cancelOption);
                optionMapping[cancelOption] = "Cancel";

                var mainWindow = playniteApi.Dialogs.GetCurrentAppWindow();
                mainWindow?.Activate();

                MessageBoxOption selectedOption = playniteApi.Dialogs.ShowMessage(
                    "Select a download provider:",
                    "Download Provider",
                    MessageBoxImage.Question,
                    options);

                if (selectedOption != null &&
                    optionMapping.TryGetValue(selectedOption, out string chosenProvider) &&
                    chosenProvider != "Cancel")
                {
                    return chosenProvider;
                }
                return null;
            }

            private async Task OpenDownloadLinkForProviderAsync(string provider, string url)
            {
                string pluginDataDir = pluginInstance.GetPluginUserDataPath();
                Directory.CreateDirectory(pluginDataDir);

                // Provider-specific extraction
                if (provider.Equals("1Fichier", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string pageContent = await pluginInstance.LoadPageContent(url);
                        Regex regex = new Regex(
                            @"<form[^>]+action=[""'](?<dlUrl>[^""']+)[""'][^>]*>.*?<input[^>]+id=[""']dlb[""'][^>]*>",
                            RegexOptions.Singleline | RegexOptions.IgnoreCase);
                        Match match = regex.Match(pageContent);
                        if (match.Success)
                        {
                            url = match.Groups["dlUrl"].Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        playniteApi.Dialogs.ShowErrorMessage($"Error extracting 1Fichier download link: {ex.Message}", "Parsing Error");
                    }
                }
                else if (provider.Equals("BuzzHeavier", StringComparison.OrdinalIgnoreCase))
                {
                    // No extraction needed, use the original URL
                }
                else if (provider.Equals("FuckingFast", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string pageContent = await pluginInstance.LoadPageContent(url);
                        string pattern = @"<a\s+[^>]*href\s*=\s*[""'](?<dlUrl>[^""']+)[""'][^>]*>(?<linkText>.*?)</a>";
                        Regex regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                        MatchCollection matches = regex.Matches(pageContent);

                        string selectedUrl = null;
                        foreach (Match match in matches)
                        {
                            string linkText = match.Groups["linkText"].Value.Trim();
                            if (linkText.Equals("Filehoster: FuckingFast", StringComparison.OrdinalIgnoreCase))
                            {
                                selectedUrl = match.Groups["dlUrl"].Value.Trim();
                                break;
                            }
                        }
                        if (!string.IsNullOrEmpty(selectedUrl))
                        {
                            url = selectedUrl;
                        }
                        else
                        {
                            playniteApi.Dialogs.ShowErrorMessage("Could not locate the 'Filehoster: FuckingFast' link.", "Parsing Error");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        playniteApi.Dialogs.ShowErrorMessage($"Error extracting FuckingFast link: {ex.Message}", "Parsing Error");
                    }
                }
                else if (provider.Equals("GoFile", StringComparison.OrdinalIgnoreCase))
                {
                    // No extraction needed, use the original URL
                }

                // All providers: Write to file and run Python script
                try
                {
                    string urlsFilePath = Path.Combine(pluginDataDir, "_Add_Urls_here.txt");
                    File.AppendAllText(urlsFilePath, url + Environment.NewLine);

                    string pythonScriptPath = Path.Combine(pluginDataDir, "fucking fast.py");
                    var psi = new ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments = $"\"{pythonScriptPath}\"",
                        WorkingDirectory = pluginDataDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var process = Process.Start(psi))
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();

                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            playniteApi.Dialogs.ShowErrorMessage($"Python script error: {error}", $"{provider} Extraction");
                        }
                    }
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"Error running extraction for {provider}: {ex.Message}", "Python Script Error");
                }
            }
            protected void InvokeOnInstalled(GameInstalledEventArgs args)
            {
                // Update the game's state
                var game = API.Instance.Database.Games.Get(args.GameId);
                if (game != null)
                {
                    game.IsInstalling = false;
                    game.IsInstalled = true;
                    API.Instance.Database.Games.Update(game);

                    // Notify Playnite
                }
            }

            public class GameInstalledEventArgs : EventArgs
            {
                public Guid GameId { get; private set; }

                public GameInstalledEventArgs(Guid gameId)
                {
                    GameId = gameId;
                }
            }




        }




    }
}
