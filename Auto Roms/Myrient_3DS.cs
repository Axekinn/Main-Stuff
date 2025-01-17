using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MyrientNintendo3DS
{
    public class MyrientNintendo3DSStore : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        public override Guid Id { get; } = Guid.Parse("293e9fb0-84ed-45ca-a706-321a173b0a12");
        public override string Name => "MyrientNintendo3DS";

        private readonly string configFilePath;

        public MyrientNintendo3DSStore(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties { HasSettings = false };
            configFilePath = Path.Combine(api.Paths.ExtensionsDataPath, $"{Id}", $"{Id}.config");
            LoadConfig();
        }

        private static readonly Dictionary<string, List<string>> platformUrls = new Dictionary<string, List<string>>
        {
            { "Nintendo 3DS", new List<string> {
                "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%203DS%20(Decrypted)/",
                "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Nintendo%20-%20Nintendo%203DS%20%5BT-En%5D%20Collection/" } },
            { "Nintendo DS", new List<string> {
                "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%20DS%20(Decrypted)/" } },
            { "Nintendo DSi", new List<string> {
                "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%20DSi%20(Decrypted)/",
                "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%20DSi%20(Digital)/" } },
            { "Nintendo Wii", new List<string> {
                "https://myrient.erista.me/files/Redump/Nintendo%20-%20Wii%20-%20NKit%20RVZ%20[zstd-19-128k]/",
                "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Nintendo%20-%20Wii%20%5BT-En%5D%20Collection/" } },
            { "Nintendo Wii U", new List<string> {
                "https://myrient.erista.me/files/Internet%20Archive/teamgt19/nintendo-wii-u-usa-full-set-wua-format-embedded-dlc-updates/",
                "https://myrient.erista.me/files/Internet%20Archive/teamgt19/nintendo-wii-u-eshop-usa-full-set-wua-format-embedded-dlc-updates/" } },
            { "Sony PlayStation", new List<string> {
                "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation/",
                "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sony%20-%20PlayStation%20%5BT-En%5D%20Collection/" } },
            { "Sony PlayStation 2", new List<string> {
                "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation%202/",
                "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sony%20-%20PlayStation%202%20%5BT-En%5D%20Collection/Discs/" } },
            { "Sony PlayStation 3", new List<string> {
                "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation%203/",
                "https://myrient.erista.me/files/No-Intro/Sony%20-%20PlayStation%203%20(PSN)%20(Content)/",
                "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sony%20-%20PlayStation%203%20%5BT-En%5D%20Collection/" } },
            { "Sony PlayStation Portable", new List<string> {
                "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation%20Portable/",
                "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sony%20-%20PlayStation%20Portable%20%5BT-En%5D%20Collection/Discs/",
                "https://myrient.erista.me/files/Internet%20Archive/storage_manager/PSP-DLC/%5BNo-Intro%5D%20PSP%20DLC/" } },
            { "Microsoft Xbox", new List<string> {
                "https://myrient.erista.me/files/Redump/Microsoft%20-%20Xbox/",
                "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Microsoft%20-%20XBOX%20%5BT-En%5D%20Collection/" } },
            { "Microsoft Xbox 360", new List<string> {
                "https://myrient.erista.me/files/Redump/Microsoft%20-%20Xbox%20360/",
                "https://myrient.erista.me/files/No-Intro/Microsoft%20-%20Xbox%20360%20(Digital)/" } },
            { "Nintendo GameCube", new List<string> {
                "https://myrient.erista.me/files/Redump/Nintendo%20-%20GameCube%20-%20NKit%20RVZ%20[zstd-19-128k]/",
                "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Nintendo%20-%20GameCube%20%5BT-En%5D%20Collection/" } },
            { "Nintendo Game Boy Advance", new List<string> {
                "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy%20Advance/" } },
            { "Nintendo Game Boy Color", new List<string> {
                "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy%20Color/" } },
            { "Nintendo Game Boy", new List<string> {
                "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy/" } },
            { "Nintendo 64", new List<string> {
                "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%2064%20(BigEndian)/", } },
            { "Sega Dreamcast", new List<string> {
                "https://myrient.erista.me/files/Internet%20Archive/chadmaster/dc-chd-zstd-redump/dc-chd-zstd/",
                "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sega%20-%20Dreamcast%20%5BT-En%5D%20Collection/" } },
            { "Sega Saturn", new List<string> {
                "https://myrient.erista.me/files/Redump/Sega%20-%20Saturn/",
                "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sega%20-%20Saturn%20%5BT-En%5D%20Collection/" } }
        };

        private static readonly Dictionary<string, string> platformRomsFolders = new Dictionary<string, string>
        {
            { "Nintendo 3DS", "Roms/Nintendo - 3DS/Games" },
            { "Nintendo DS", "Roms/Nintendo - DS/Games" },
            { "Nintendo DSi", "Roms/Nintendo - DSi/Games" },
            { "Nintendo Wii", "Roms/Nintendo - WII/Games" },
            { "Nintendo Wii U", "Roms/Nintendo - Wii U/Games" },
            { "Sony PlayStation", "Roms/Sony - PlayStation/Games" },
            { "Sony PlayStation 2", "Roms/Sony - PlayStation 2/Games" },
            { "Sony PlayStation 3", "Roms/Sony - PlayStation 3/Games" },
            { "Sony PlayStation Portable", "Roms/Sony - PlayStation Portable/Games" },
            { "Microsoft Xbox", "Roms/Microsoft - Xbox/Games" },
            { "Microsoft Xbox 360", "Roms/Microsoft - Xbox 360/Games" },
            { "Nintendo GameCube", "Roms/Nintendo - GameCube/Games" },
            { "Nintendo Game Boy Advance", "Roms/Nintendo - Game Boy Advance/Games" },
            { "Nintendo Game Boy Color", "Roms/Nintendo - Game Boy Color/Games" },
            { "Nintendo Game Boy", "Roms/Nintendo - Game Boy/Games" },
            { "Sega Dreamcast", "Roms/Sega - Dreamcast/Games" },
            { "Nintendo 64", "Roms/Nintendo - 64/Games" },
            { "Saga Saturn", "Roms/Saga - Saturn/Games" },
            { "Nintendo Switch", "Roms/Nintendo - Switch/Games" }
        };

        private static readonly Dictionary<string, List<string>> platformRomTypes = new Dictionary<string, List<string>>
        {
            { "Nintendo 3DS", new List<string> { ".3ds", ".zip" } },
            { "Nintendo DS", new List<string> { ".nds", ".zip" } },
            { "Nintendo DSi", new List<string> { ".dsi", ".zip" } },
            { "Nintendo Wii", new List<string> { ".wbfs", ".iso", ".rvz" } },
            { "Nintendo Wii U", new List<string> { ".wux", ".wad", ".wua" } },
            { "Sony PlayStation", new List<string> { ".chd", ".iso", ".bin", ".cue" } },
            { "Sony PlayStation 2", new List<string> { ".chd", ".iso", ".m3u", ".bin", ".cue" } },
            { "Sony PlayStation 3", new List<string> { ".iso", ".pkg" } },
            { "Sony PlayStation Portable", new List<string> { ".iso", ".cso" } },
            { "Microsoft Xbox", new List<string> { ".iso" } },
            { "Microsoft Xbox 360", new List<string> { ".iso", ".zar" } },
            { "Nintendo GameCube", new List<string> { ".iso", ".rvz" } },
            { "Nintendo Game Boy Advance", new List<string> { ".gba", ".zip" } },
            { "Nintendo Game Boy Color", new List<string> { ".gbc", ".zip" } },
            { "Nintendo Game Boy", new List<string> { ".gb", ".zip" } },
            { "Sega Dreamcast", new List<string> { ".gdi", ".cue", ".chd" } },
            { "Sega Saturn", new List<string> { ".iso", ".cue", ".bin" } },
            { "Nintendo 64", new List<string> { ".z64", ".n64", ".zip", ".v64" } },
            { "Nintendo Switch", new List<string> { ".nsp", ".xci" } }
        };

        private static readonly Dictionary<string, string> platformEmulators = new Dictionary<string, string>
        {
            { "Nintendo 3DS", "Lime 3DS" },
            { "Nintendo DS", "BizHawk" },
            { "Nintendo DSi", "BizHawk" },
            { "Nintendo Wii", "Dolphin" },
            { "Nintendo Wii U", "Cemu" },
            { "Sony PlayStation", "DuckStation" },
            { "Sony PlayStation 2", "PCSX2" },
            { "Sony PlayStation 3", "RPCS3" },
            { "Sony PlayStation Portable", "PPSSPP" },
            { "Microsoft Xbox", "Xemu" },
            { "Microsoft Xbox 360", "Xenia" },
            { "Nintendo GameCube", "Dolphin" },
            { "Nintendo Game Boy Advance", "BizHawk" },
            { "Nintendo Game Boy Color", "BizHawk" },
            { "Nintendo Game Boy", "BizHawk" },
            { "Sega Dreamcast", "Redream" },
            { "Sega Saturn", "BizHawk" },
            { "Nintendo Switch", "Ruyjinx" },
            { "Nintendo 64", "BizHawk" }
        };

        private static readonly Dictionary<string, string> platformProfiles = new Dictionary<string, string>
        {
            { "Nintendo 3DS", "Default" },
            { "Nintendo DS", "Nintendo DS/DSI" },
            { "Nintendo DSi", "Nintendo DS/DSi" },
            { "Nintendo Wii", "Nintendo Wii" },
            { "Nintendo Wii U", "Default" },
            { "Sony PlayStation", "Default" },
            { "Sony PlayStation 2", "Default QT" },
            { "Sony PlayStation 3", "Default" },
            { "Sony PlayStation Portable", "Default" },
            { "Microsoft Xbox", "Default" },
            { "Microsoft Xbox 360", "Canary" },
            { "Nintendo GameCube", "Nintendo GameCube" },
            { "Nintendo Game Boy Advance", "Nintendo Game Boy Advance" },
            { "Nintendo Game Boy Color", "Nintendo Game Boy Color" },
            { "Nintendo Game Boy", "Nintendo Game Boy" },
            { "Sega Dreamcast", "Default" },
            { "Sega Saturn", "Saga Saturn" },
            { "Nintendo Switch", "Default" },
            { "Nintendo 64", "Nintendo 64" }
        };

        private void LoadConfig()
        {
            if (!File.Exists(configFilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configFilePath)); // Ensure the directory exists
                File.WriteAllText(configFilePath, "ScrapeUrls=False\nImportRoms=False");
            }

            var configLines = File.ReadAllLines(configFilePath);
            var configDict = configLines.Select(line => line.Split('=')).ToDictionary(split => split[0].Trim(), split => split[1].Trim().Equals("True", StringComparison.OrdinalIgnoreCase));

            scrapeUrls = configDict.ContainsKey("ScrapeUrls") ? configDict["ScrapeUrls"] : false;
            importRoms = configDict.ContainsKey("ImportRoms") ? configDict["ImportRoms"] : false;

            logger.Info($"Configuration Loaded: ScrapeUrls={scrapeUrls}, ImportRoms={importRoms}");
        }

        private bool scrapeUrls;
        private bool importRoms;

        private async Task<List<GameMetadata>> ScrapeAllUrlsForPlatform(string platform)
        {
            var gameEntries = new List<GameMetadata>();

            if (platformUrls.TryGetValue(platform, out var urls))
            {
                foreach (var url in urls)
                {
                    var gamesFromUrl = await ScrapeSite(url, platform);
                    foreach (var game in gamesFromUrl)
                    {
                        // Check for existing game in the database with the same name and platform
                        var existingGame = PlayniteApi.Database.Games.FirstOrDefault(g =>
                            g.Name.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                            g.Platforms != null &&
                            g.Platforms.Any(p => p.SpecificationId == new MetadataSpecProperty(platform).Id));

                        if (existingGame == null)
                        {
                            gameEntries.Add(game);
                        }
                        else
                        {
                            logger.Info($"Game already exists: {game.Name} on platform {platform}. Skipping.");
                        }
                    }
                }
            }

            return gameEntries;
        }

        private void ScanRomsAndUpdateGames(string platform)
        {
            string[] driveLetters = Environment.GetLogicalDrives();
            var romPaths = new HashSet<string>();

            // Gather all ROM paths from the drives
            foreach (string drive in driveLetters)
            {
                string romsPath = platformRomsFolders.ContainsKey(platform) ? Path.Combine(drive, platformRomsFolders[platform]) : string.Empty;
                if (!string.IsNullOrEmpty(romsPath) && Directory.Exists(romsPath))
                {
                    logger.Info($"Scanning for ROMs in: {romsPath}");
                    var romFiles = Directory.GetFiles(romsPath, "*.*", SearchOption.AllDirectories)
                        .Where(file => platformRomTypes.ContainsKey(platform) && platformRomTypes[platform].Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                        .Select(file => file.Replace('/', '\\')).ToArray();
                    foreach (var romFile in romFiles)
                    {
                        romPaths.Add(romFile);
                    }
                }
            }

            var allExistingGames = PlayniteApi.Database.Games
                .Where(g => g.Platforms != null && g.Platforms.Any(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var existingGameNames = allExistingGames
                .Select(g => CleanGameName(g.Name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Check installed games for ROMs
            foreach (var game in allExistingGames)
            {
                if (game.Roms == null)
                {
                    logger.Warn($"Game '{game.Name}' has null Roms collection.");
                    continue;
                }

                var gameRoms = game.Roms.Where(r => romPaths.Contains(r.Path)).ToList();
                if (gameRoms.Any())
                {
                    logger.Info($"Game '{game.Name}' already has ROMs installed: {string.Join(", ", gameRoms.Select(r => r.Path))}");
                    continue;
                }
                else
                {
                    var removedRoms = game.Roms.Where(r => !romPaths.Contains(r.Path)).ToList();
                    if (removedRoms.Any())
                    {
                        foreach (var removedRom in removedRoms)
                        {
                            logger.Info($"Removing non-existent ROM: {removedRom.Path} from game: {game.Name}");
                            game.Roms.Remove(removedRom);
                        }

                        if (game.Roms.Count == 0)
                        {
                            game.IsInstalled = false;
                            logger.Info($"Marked game as not installed: {game.Name} due to no remaining ROMs.");
                        }

                        PlayniteApi.Database.Games.Update(game);
                    }
                }
            }

            // Check ROMs folder for new games
            foreach (var romFile in romPaths)
            {
                string originalGameName = Path.GetFileNameWithoutExtension(romFile);
                string cleanedGameName = CleanGameName(originalGameName);

                if (!existingGameNames.Contains(cleanedGameName))
                {
                    // Add as new game
                    logger.Info($"Adding new game: {cleanedGameName} with ROM: {romFile}");
                    var newGame = new Game
                    {
                        Name = cleanedGameName,
                        Roms = new ObservableCollection<GameRom> { new GameRom(originalGameName, romFile) },
                        InstallDirectory = Path.GetDirectoryName(romFile),
                        IsInstalled = true
                    };

                    var platformObj = PlayniteApi.Database.Platforms.FirstOrDefault(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase));
                    if (platformObj != null)
                    {
                        newGame.Platforms.Add(platformObj);
                    }
                    else
                    {
                        logger.Error($"Platform '{platform}' not found in Playnite database.");
                        continue;
                    }

                    var emulator = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals(platformEmulators.ContainsKey(platform) ? platformEmulators[platform] : string.Empty, StringComparison.OrdinalIgnoreCase));
                    if (emulator == null)
                    {
                        logger.Error($"Emulator not found for platform: {platform}");
                        continue;
                    }
                    logger.Info($"Set emulator: {emulator.Name} for platform: {platform}");

                    var builtInProfiles = emulator.BuiltinProfiles?.ToList();
                    if (builtInProfiles == null || !builtInProfiles.Any())
                    {
                        logger.Error($"No built-in profiles found for emulator: {emulator.Name}");
                        continue;
                    }
                    logger.Info($"Available built-in profiles for emulator {emulator.Name}: {string.Join(", ", builtInProfiles.Select(p => p.Name))}");

                    var profileName = platformProfiles.ContainsKey(platform) ? platformProfiles[platform] : string.Empty;
                    var emulatorProfile = builtInProfiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
                    if (emulatorProfile == null)
                    {
                        logger.Error($"Profile '{profileName}' not found for platform: {platform}");
                        continue;
                    }
                    logger.Info($"Set profile: {emulatorProfile.Name} with ID: {emulatorProfile.Id} for platform: {platform}");

                    var playAction = new GameAction
                    {
                        Name = "Play",
                        Type = GameActionType.Emulator,
                        EmulatorId = emulator.Id,
                        EmulatorProfileId = emulatorProfile.Id,
                        Path = romFile,
                        IsPlayAction = true
                    };
                    newGame.GameActions.Insert(0, playAction);

                    PlayniteApi.Database.Games.Add(newGame);
                    logger.Info($"Added new game: {cleanedGameName} with play action.");

                    // Fetch metadata for the new game
                    FetchMetadata(newGame);
                    logger.Info($"Fetched metadata for new game: {cleanedGameName}");
                }
            }

            logger.Info($"ROM scanning and updating completed for platform: {platform}");
        }

        private string CleanGameName(string name)
        {
            // Remove revision and disc numbers, keep only essential parts of the name
            name = Regex.Replace(name, @"\s*\(Rev \d+\)", ""); // Remove revision numbers from the main name
            name = Regex.Replace(name, @"\s*\(Disc \d+\)", ""); // Remove Disc numbers from the main name
            name = Regex.Replace(name, @"\s*\(.*?\)", "").Replace(".zip", "").Replace(".wua", "").Replace(".chd", "").Trim(); // Remove other parenthesized content

            // Remove "the " for better matching
            if (name.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(4); // Remove "the " at the beginning
            }

            return name;
        }



        private async Task<string> LoadPageContent(string url, string platform)
        {
            using (var httpClient = new HttpClient())
            {
                try
                {
                    // Set user-agent header to mimic a browser
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                    HttpResponseMessage response = await httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode(); // Throws if the status code is not success

                    return await response.Content.ReadAsStringAsync();
                }
                catch (HttpRequestException e)
                {
                    logger.Error($"Error loading URL for platform {platform}: {url}. Exception: {e.Message}");
                    return string.Empty;
                }
                catch (Exception ex)
                {
                    logger.Error($"Error loading URL for platform {platform}: {url}. Exception: {ex}");
                    return string.Empty;
                }
            }
        }
                
        private List<Tuple<string, string>> ParseLinks(string pageContent)
        {
            var links = new List<Tuple<string, string>>();
            var matches = Regex.Matches(pageContent, @"<a\s+(?:[^>]*?\s+)?href=[""'](.*?)[""'].*?>(.*?)</a>");

            foreach (Match match in matches)
            {
                string href = match.Groups[1].Value;
                string text = Regex.Replace(match.Groups[2].Value, "<.*?>", string.Empty); // Remove HTML tags
                links.Add(new Tuple<string, string>(href, text));
            }

            return links;
        }

        private async Task<List<GameMetadata>> ScrapeSite(string baseUrl, string platform)
        {
            var gameEntries = new List<GameMetadata>();
            var uniqueGames = new Dictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

            logger.Info($"Scraping: {baseUrl} for platform: {platform}");
            string pageContent = await LoadPageContent(baseUrl, platform);
            if (string.IsNullOrEmpty(pageContent))
            {
                logger.Warn($"Skipping URL: {baseUrl} for platform: {platform} due to empty content");
                return gameEntries;
            }

            var links = ParseLinks(pageContent);

            foreach (var link in links)
            {
                string href = link.Item1;
                string text = WebUtility.HtmlDecode(link.Item2);

                if (IsValidGameLink(href, text))
                {
                    string cleanName = CleanGameName(text);
                    string addonOrDlc = IsAddonOrDLC(text) ? ExtractAddonOrDLC(text) : string.Empty;
                    string actionName = GetActionName(text, cleanName);

                    if (!string.IsNullOrEmpty(cleanName))
                    {
                        string gameNameForKey = cleanName;
                        if (IsAddonOrDLC(text))
                        {
                            gameNameForKey = text.Split(new[] { " - " }, StringSplitOptions.None)[0].Trim();
                        }

                        var existingGame = PlayniteApi.Database.Games
                            .Where(g => g.Platforms != null && g.Platforms.Any(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase)))
                            .FirstOrDefault(g => CleanGameName(g.Name).Equals(gameNameForKey, StringComparison.OrdinalIgnoreCase));

                        if (existingGame != null)
                        {
                            bool downloadActionExists = existingGame.GameActions
                                .Any(a => a.Type == GameActionType.URL && a.Path == (href.StartsWith("http") ? href : $"{baseUrl}{href}"));

                            if (downloadActionExists)
                            {
                                logger.Info($"Download action already exists for game: {existingGame.Name} with URL: {href}");
                                continue;
                            }

                            var gameAction = new GameAction
                            {
                                Name = actionName,
                                Type = GameActionType.URL,
                                Path = href.StartsWith("http") ? href : $"{baseUrl}{href}",
                                IsPlayAction = false // Ensure it's not a play action
                            };
                            existingGame.GameActions.Add(gameAction);
                            PlayniteApi.Database.Games.Update(existingGame);
                            logger.Info($"Updated existing game: {existingGame.Name} with new action: {gameAction.Name}");
                        }
                        else
                        {
                            var gameMetadata = new GameMetadata
                            {
                                Name = cleanName,
                                Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platform) },
                                GameActions = new List<GameAction>(),
                                IsInstalled = false
                            };

                            var gameAction = new GameAction
                            {
                                Name = actionName,
                                Type = GameActionType.URL,
                                Path = href.StartsWith("http") ? href : $"{baseUrl}{href}",
                                IsPlayAction = false // Ensure it's not a play action
                            };
                            gameMetadata.GameActions.Add(gameAction);

                            uniqueGames[gameNameForKey] = gameMetadata;
                            gameEntries.Add(gameMetadata);
                        }
                    }
                }
            }

            return uniqueGames.Values.ToList();
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var games = new List<GameMetadata>();

            if (scrapeUrls)
            {
                logger.Info("Scraping URLs is enabled.");
                foreach (var platformKey in platformUrls.Keys)
                {
                    logger.Info($"Starting scraping for platform: {platformKey}");
                    var scrapedGames = ScrapeAllUrlsForPlatform(platformKey).GetAwaiter().GetResult();
                    logger.Info($"Total {platformKey} games: {scrapedGames.Count}");

                    foreach (var game in scrapedGames)
                    {
                        // Check for existing game in the database with the same name and platform
                        var platform = PlayniteApi.Database.Platforms.FirstOrDefault(pl => pl.Name.Equals(platformKey, StringComparison.OrdinalIgnoreCase));
                        if (platform == null)
                        {
                            logger.Warn($"Could not find platform for {platformKey}. Ensure that the platform is defined in Playnite and try again.");
                            continue;
                        }

                        var existingGame = PlayniteApi.Database.Games.FirstOrDefault(g =>
                            g.Name.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                            g.PlatformIds.Contains(platform.Id));

                        if (existingGame == null)
                        {
                            logger.Info($"Adding new game: {game.Name} to Playnite database with platform {platformKey}.");
                            var newGame = new Game()
                            {
                                GameId = game.GameId,
                                Name = game.Name,
                                PlatformIds = new List<Guid> { platform.Id },
                                GameActions = new ObservableCollection<GameAction>(game.GameActions),
                                IsInstalled = game.IsInstalled
                            };

                            PlayniteApi.Database.Games.Add(newGame);
                            games.Add(game);
                        }
                        else
                        {
                            logger.Info($"Game already exists: {game.Name} on platform {platformKey}. Skipping.");
                        }
                    }
                }
            }
            else
            {
                logger.Info("Scraping URLs is disabled.");
            }

            if (importRoms)
            {
                logger.Info("Importing ROMs is enabled.");
                foreach (var platform in platformRomsFolders.Keys)
                {
                    ScanRomsAndUpdateGames(platform);
                }
            }
            else
            {
                logger.Info("Importing ROMs is disabled.");
            }

            logger.Info($"Total games added: {games.Count}");
            return games;
        }

        private void AssignPlatform(GameMetadata game, string platform)
        {
            if (game.Platforms == null || game.Platforms.Count == 0)
            {
                game.Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platform) };
            }
            logger.Info($"Assigned platform {platform} to game: {game.Name}");
        }

        private bool IsAddonOrDLC(string name)
        {
            return name.IndexOf("DLC", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Addon", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void FetchMetadata(Game game)
        {
            // Update metadata using Playnite's built-in functionality
            PlayniteApi.Database.Games.Update(game);
            logger.Info($"Updated metadata for game: {game.Name}");
        }

        private void HandleAddOnsAndDLCs(GameMetadata game, Game existingGame = null)
        {
            var mainGameName = CleanGameName(game.Name).Replace(" - ", " ");
            var mainGame = existingGame ?? PlayniteApi.Database.Games.FirstOrDefault(g =>
                g.Name.Equals(mainGameName, StringComparison.OrdinalIgnoreCase) &&
                g.Platforms != null &&
                g.Platforms.Any(p => p.SpecificationId == ((MetadataSpecProperty)game.Platforms.First()).Id));

            if (mainGame != null)
            {
                foreach (var action in game.GameActions)
                {
                    mainGame.GameActions.Add(action);
                }
                PlayniteApi.Database.Games.Update(mainGame);
                logger.Info($"Added add-ons/DLCs to main game: {mainGame.Name}");
            }
            else
            {
                logger.Warn($"Base game not found for DLC/add-on: {game.Name}. Consider adding the base game manually.");
            }
        }

        private bool IsDuplicate(GameMetadata gameMetadata)
        {
            return PlayniteApi.Database.Games.Any(existingGame =>
                existingGame.PluginId == Id &&
                existingGame.Name.Equals(gameMetadata.Name, StringComparison.OrdinalIgnoreCase) &&
                existingGame.Platforms.Any(platform => platform.SpecificationId == ((MetadataSpecProperty)gameMetadata.Platforms.First()).Id));
        }
                
        private string GetLanguageTags(string gameName)
        {
            var match = Regex.Match(gameName, @"\(([^)]+)\)");
            if (match.Success)
            {
                var languages = match.Groups[1].Value;
                var translationMatch = Regex.Match(gameName, @"

\[T - ([^\]

]+)\]

");
                if (translationMatch.Success)
                {
                    languages += $" ({translationMatch.Groups[1].Value.Replace(" by ", ", ")})";
                }
                return languages;
            }
            return string.Empty;
        }

        private string ExtractRegion(string name)
        {
            var match = Regex.Match(name, @"\((.*?)\)");
            return match.Success ? match.Groups[1].Value : "Unknown Region";
        }

        private string ExtractRevision(string name)
        {
            var match = Regex.Match(name, @"\(Rev (\d+)\)");
            return match.Success ? $"(Rev {match.Groups[1].Value})" : string.Empty;
        }

        private string ExtractDisc(string name)
        {
            var match = Regex.Match(name, @"\(Disc (\d+)\)");
            return match.Success ? $"(Disc {match.Groups[1].Value})" : string.Empty;
        }

        private bool IsDemo(string name)
        {
            return name.IndexOf("Demo", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsAddon(string name)
        {
            return name.IndexOf("Addon", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string ExtractLanguages(string name)
        {
            var matches = Regex.Matches(name, @"\b(?:En|Fr|De|Es|It|Pt|Ru|Ja|Ko|Zh|Nl|Sv|No|Da|Fi|El|Tr|Hu|Pl|Cs|Sk|Sl|Hr|Sr|Bg|Ro|Lv|Lt|Et|Mt|Ga|Is|Ms|Th|Vi|Ar|Hi|Ur|Fa)\b");
            return matches.Count > 0 ? $"({string.Join(",", matches.Cast<Match>().Select(m => m.Value))})" : string.Empty;
        }

        private bool IsValidGameLink(string href, string text)
        {
            return (href.EndsWith(".zip") || href.EndsWith(".wua") || href.EndsWith(".chd") || href.EndsWith(".7z")) && !text.Equals("↓", StringComparison.OrdinalIgnoreCase);
        }

        private string ExtractAddonOrDLC(string name)
        {
            var match = Regex.Match(name, @"\((Addon|DLC)\)", RegexOptions.IgnoreCase);
            return match.Success ? match.Value : string.Empty;
        }

        private string GetActionName(string fullName, string baseGameName)
        {
            return fullName.Replace(baseGameName, "").TrimStart(new char[] { ' ', '-', '_' });
        }

        private Guid? GetPlatformId(string platformName)
        {
            var platform = PlayniteApi.Database.Platforms.FirstOrDefault(pl => pl.Name.Equals(platformName, StringComparison.OrdinalIgnoreCase));
            return platform?.Id;
        }

    }
}

