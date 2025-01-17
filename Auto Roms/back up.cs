using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MyrientNintendo3DS
{
    public class MyrientNintendo3DSStore : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly string configFilePath;

        public override Guid Id { get; } = Guid.Parse("293e9fb0-84ed-45ca-a706-321a173b0a12");
        public override string Name => "MyrientNintendo3DS";

        private static readonly Dictionary<string, List<string>> platformUrls = new Dictionary<string, List<string>>
        {
            { "Nintendo 3DS", new List<string> { "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%203DS%20(Decrypted)/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Nintendo%20-%20Nintendo%203DS%20%5BT-En%5D%20Collection/", "https://myrient.erista.me/files/TranslationHacks/Nintendo%20-%20Nintendo%203DS%20(Translations)/" } },
            { "Nintendo DS", new List<string> { "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%20DS%20(Decrypted)/", "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%20DS%20(Translations)/" } },
            { "Nintendo DSi", new List<string> { "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%20DSi%20(Decrypted)/", "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%20DSi%20(Digital)/" } },
            { "Nintendo Wii", new List<string> { "https://myrient.erista.me/files/Redump/Nintendo%20-%20Wii%20-%20NKit%20RVZ%20[zstd-19-128k]/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Nintendo%20-%20Wii%20%5BT-En%5D%20Collection/" } },
            { "Nintendo Wii U", new List<string> { "https://myrient.erista.me/files/Internet%20Archive/teamgt19/nintendo-wii-u-usa-full-set-wua-format-embedded-dlc-updates/", "https://myrient.erista.me/files/Internet%20Archive/teamgt19/nintendo-wii-u-eshop-usa-full-set-wua-format-embedded-dlc-updates/" } },
            { "Sony PlayStation", new List<string> { "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sony%20-%20PlayStation%20%5BT-En%5D%20Collection/" } },
            { "Sony PlayStation 2", new List<string> { "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation%202/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sony%20-%20PlayStation%202%20%5BT-En%5D%20Collection/Discs/" } },
            { "Sony PlayStation 3", new List<string> { "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation%203/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sony%20-%20PlayStation%203%20%5BT-En%5D%20Collection/" } },
            { "Sony PlayStation Portable", new List<string> { "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation%20Portable/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sony%20-%20PlayStation%20Portable%20%5BT-En%5D%20Collection/Discs/" } },
            { "Microsoft Xbox", new List<string> { "https://myrient.erista.me/files/Redump/Microsoft%20-%20Xbox/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Microsoft%20-%20XBOX%20%5BT-En%5D%20Collection/" } },
            { "Microsoft Xbox 360", new List<string> { "https://myrient.erista.me/files/Redump/Microsoft%20-%20Xbox%20360/", "https://myrient.erista.me/files/No-Intro/Microsoft%20-%20Xbox%20360%20(Digital)/" } },
            { "Nintendo GameCube", new List<string> { "https://myrient.erista.me/files/Redump/Nintendo%20-%20GameCube%20-%20NKit%20RVZ%20[zstd-19-128k]/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Nintendo%20-%20GameCube%20%5BT-En%5D%20Collection/" } },
            { "Nintendo Game Boy Advance", new List<string> { "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy%20Advance/", "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy%20Advance%20(Translations)/" } },
            { "Nintendo Game Boy Color", new List<string> { "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy%20Color/", "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy%20Color%20(Translations)/" } },
            { "Nintendo Game Boy", new List<string> { "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy/", "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy%20(Translations)/" } },
            { "Sega Dreamcast", new List<string> { "https://myrient.erista.me/files/Internet%20Archive/chadmaster/dc-chd-zstd-redump/dc-chd-zstd/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sega%20-%20Dreamcast%20%5BT-En%5D%20Collection/" } }
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
            { "Sega Dreamcast", "Roms/Sega - Dreamcast/Games" }
        };

        private static readonly Dictionary<string, List<string>> platformRomTypes = new Dictionary<string, List<string>>
{
    { "Nintendo 3DS", new List<string> { ".3ds", ".zip" } },
    { "Nintendo DS", new List<string> { ".nds", ".zip" } },
    { "Nintendo DSi", new List<string> { ".dsi", ".zip" } },
    { "Nintendo Wii", new List<string> { ".wbfs", ".iso", ".rvz" } },
    { "Nintendo Wii U", new List<string> { ".wux", ".wad" } },
    { "Sony PlayStation", new List<string> { ".chd", ".iso", ".bin", ".cue" } },
    { "Sony PlayStation 2", new List<string> { ".chd", ".iso", ".m3u", ".bin", ".cue" } },
    { "Sony PlayStation 3", new List<string> { ".iso", ".pkg" } },
    { "Sony PlayStation Portable", new List<string> { ".iso", ".cso" } },
    { "Microsoft Xbox", new List<string> { ".iso" } },
    { "Microsoft Xbox 360", new List<string> { ".iso" } },
    { "Nintendo GameCube", new List<string> { ".iso", ".rvz" } },
    { "Nintendo Game Boy Advance", new List<string> { ".gba", ".zip" } },
    { "Nintendo Game Boy Color", new List<string> { ".gbc", ".zip" } },
    { "Nintendo Game Boy", new List<string> { ".gb", ".zip" } },
    { "Sega Dreamcast", new List<string> { ".gdi", ".cue", ".chd" } }
};

        private static readonly Dictionary<string, string> platformEmulators = new Dictionary<string, string>
{
    { "Nintendo 3DS", "Citra" },
    { "Nintendo DS", "DeSmuME" },
    { "Nintendo DSi", "DeSmuME" },
    { "Nintendo Wii", "Dolphin" },
    { "Nintendo Wii U", "Cemu" },
    { "Sony PlayStation", "PCSX" },
    { "Sony PlayStation 2", "PCSX2" },
    { "Sony PlayStation 3", "RPCS3" },
    { "Sony PlayStation Portable", "PPSSPP" },
    { "Microsoft Xbox", "Xemu" },
    { "Microsoft Xbox 360", "Xenia" },
    { "Nintendo GameCube", "Dolphin" },
    { "Nintendo Game Boy Advance", "mGBA" },
    { "Nintendo Game Boy Color", "Gambatte" },
    { "Nintendo Game Boy", "Gambatte" },
    { "Sega Dreamcast", "Redream" }
};

        private static readonly Dictionary<string, string> platformProfiles = new Dictionary<string, string>
{
    { "Nintendo 3DS", "Default" },
    { "Nintendo DS", "Nintendo DS" },
    { "Nintendo DSi", "Nintendo DSi" },
    { "Nintendo Wii", "Nintendo Wii" },
    { "Nintendo Wii U", "Default" },
    { "Sony PlayStation", "Default" },
    { "Sony PlayStation 2", "Default QT" },
    { "Sony PlayStation 3", "Default" },
    { "Sony PlayStation Portable", "Default" },
    { "Microsoft Xbox", "Default" },
    { "Microsoft Xbox 360", "Default" },
    { "Nintendo GameCube", "Nintendo GameCube" },
    { "Nintendo Game Boy Advance", "Default" },
    { "Nintendo Game Boy Color", "Default" },
    { "Nintendo Game Boy", "Default" },
    { "Sega Dreamcast", "Default" }
};

        public MyrientNintendo3DSStore(IPlayniteAPI api) : base(api)
        {
            configFilePath = Path.Combine(GetPluginUserDataPath(), "MyrientNintendo3DS.config");
            Properties = new LibraryPluginProperties { HasSettings = false };
            InitializeConfigFile();
        }

        private void InitializeConfigFile()
        {
            if (!File.Exists(configFilePath))
            {
                logger.Info("Configuration file not found. Creating a new one...");
                using (var writer = new StreamWriter(configFilePath))
                {
                    writer.WriteLine("# Scrape:");
                    writer.WriteLine("All: False");
                    foreach (var platform in platformUrls.Keys)
                    {
                        writer.WriteLine($"{platform}: False");
                    }
                    writer.WriteLine();
                    writer.WriteLine("# Roms:");
                    writer.WriteLine("All: False");
                    foreach (var platform in platformUrls.Keys)
                    {
                        writer.WriteLine($"{platform}: False");
                    }
                }
                logger.Info("Configuration file created and initialized.");
            }
            else
            {
                logger.Info("Configuration file found. Reading configurations...");
            }
        }

        private Dictionary<string, Dictionary<string, string>> LoadConfig()
        {
            var config = new Dictionary<string, Dictionary<string, string>>();

            if (File.Exists(configFilePath))
            {
                string[] lines = File.ReadAllLines(configFilePath);
                string currentSection = string.Empty;

                foreach (var line in lines)
                {
                    if (line.StartsWith("#"))
                    {
                        currentSection = line.TrimStart('#').Trim();
                        config[currentSection] = new Dictionary<string, string>();
                    }
                    else if (!string.IsNullOrWhiteSpace(line))
                    {
                        var parts = line.Split(':');
                        if (parts.Length == 2)
                        {
                            config[currentSection][parts[0].Trim()] = parts[1].Trim();
                        }
                    }
                }
                logger.Info("Configurations successfully read.");
            }

            return config;
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var games = new List<GameMetadata>();

            // Load configuration settings
            var config = LoadConfig();

            // Debug logging: Log the configuration file settings
            logger.Info("Loaded configuration settings:");
            foreach (var section in config)
            {
                logger.Info($"{section.Key}:");
                foreach (var setting in section.Value)
                {
                    logger.Info($"  {setting.Key}: {setting.Value}");
                }
            }

            // Iterate over each platform and its URLs
            foreach (var platform in platformUrls.Keys)
            {
                bool shouldScrapeSite = config.TryGetValue("Scrape", out var scrapeSiteConfig) &&
                                        (scrapeSiteConfig.TryGetValue("All", out var scrapeAll) && scrapeAll == "True" ||
                                         scrapeSiteConfig.TryGetValue(platform, out var scrapePlatform) && scrapePlatform == "True");

                bool shouldImportRoms = config.TryGetValue("Roms", out var importRomsConfig) &&
                                        (importRomsConfig.TryGetValue("All", out var importAll) && importAll == "True" ||
                                         importRomsConfig.TryGetValue(platform, out var importPlatform) && importPlatform == "True");

                // Detailed logging for configuration checks
                logger.Info($"Processing platform: {platform}");
                logger.Info($"  Scrape - All: {scrapeAll}, {platform}: {scrapePlatform}");
                logger.Info($"  ShouldScrapeSite: {shouldScrapeSite}");
                logger.Info($"  Roms - All: {importAll}, {platform}: {importPlatform}");
                logger.Info($"  ShouldImportRoms: {shouldImportRoms}");

                if (shouldScrapeSite)
                {
                    foreach (var url in platformUrls[platform])
                    {
                        logger.Info($"Scraping site: {url} for platform: {platform}");
                        var scrapedGames = ScrapeSite(url, platform).GetAwaiter().GetResult();
                        logger.Info($"Total {platform} games from {url}: {scrapedGames.Count}");
                        games.AddRange(scrapedGames);
                    }
                }

                if (shouldImportRoms)
                {
                    logger.Info($"Scanning ROMs for platform: {platform}");
                    // Scan ROMs for the current platform
                    ScanRomsForPlatform(platform, games);
                }
            }

            logger.Info($"Total games: {games.Count}");
            return games;
        }

        private async Task<List<GameMetadata>> ScrapeSite(string baseUrl, string platform)
        {
            var gameEntries = new List<GameMetadata>();
            var uniqueGames = new Dictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);
            logger.Info($"Scraping: {baseUrl}");
            string pageContent = await LoadPageContent(baseUrl);
            var links = ParseLinks(pageContent);

            foreach (var link in links)
            {
                string href = link.Item1;
                string text = System.Net.WebUtility.HtmlDecode(link.Item2);

                if (IsValidGameLink(href, text))
                {
                    string cleanName = CleanGameNameWithoutTranslation(text);
                    string originalName = CleanGameNameWithoutTranslation(text);
                    string region = ExtractRegion(text);
                    string revision = ExtractRevision(text);
                    string disc = ExtractDisc(text);
                    string demo = IsDemo(text) ? "(Demo)" : string.Empty;
                    string addon = IsAddon(text) ? "(Addon)" : string.Empty;
                    string languages = ExtractLanguages(text);
                    if (!string.IsNullOrEmpty(cleanName))
                    {
                        var gameAction = new GameAction
                        {
                            Name = FormatActionName(region, revision, disc, demo, languages, addon),
                            Type = GameActionType.URL,
                            Path = href.StartsWith("http") ? href : $"{baseUrl}{href}",
                            IsPlayAction = string.IsNullOrEmpty(addon)
                        };

                        if (!uniqueGames.TryGetValue(originalName, out var gameMetadata))
                        {
                            gameMetadata = new GameMetadata
                            {
                                Name = originalName,
                                Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platform) },
                                GameActions = new List<GameAction> { gameAction },
                                IsInstalled = false
                            };
                            uniqueGames[originalName] = gameMetadata;
                        }
                        else
                        {
                            if (!gameMetadata.GameActions.Any(a => a.Path == gameAction.Path && a.Name == gameAction.Name))
                            {
                                gameMetadata.GameActions.Add(gameAction);
                            }
                        }
                    }
                }
            }
            return uniqueGames.Values.ToList();
        }

        private async Task<string> LoadPageContent(string url)
        {
            using (var httpClient = new HttpClient())
            {
                return await httpClient.GetStringAsync(url);
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

        private string CleanGameNameWithoutTranslation(string name)
        {
            name = Regex.Replace(name, @"\s*\(Rev \d+\)", ""); // Remove revision numbers from the main name
            name = Regex.Replace(name, @"\s*\(Disc \d+\)", ""); // Remove Disc numbers from the main name
            return Regex.Replace(name, @"\s*\(.*?\)", "").Replace(".zip", "").Replace(".wua", "").Replace(".chd", "").Trim();
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
            return (href.EndsWith(".zip") || href.EndsWith(".wua") || href.EndsWith(".chd")) && !text.Equals("↓", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsDuplicate(GameMetadata gameMetadata)
        {
            return PlayniteApi.Database.Games.Any(existingGame => existingGame.PluginId == Id && existingGame.Name.Equals(gameMetadata.Name, StringComparison.OrdinalIgnoreCase) && existingGame.Platforms.Any(platform => platform.ToString().Equals(gameMetadata.Platforms.First().ToString(), StringComparison.OrdinalIgnoreCase)));
        }

        private string FormatActionName(string region, string revision, string disc, string demo, string languages, string addon)
        {
            return $"{region} {revision} {disc} {demo} {languages} {addon}".Trim();
        }

        private string SanitizePath(string path)
        {
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }

        private void ScanRomsForPlatform(string platform, List<GameMetadata> games)
        {
            if (!platformRomsFolders.ContainsKey(platform))
            {
                logger.Error($"No ROM folders defined for platform: {platform}");
                return;
            }

            var romsFolder = platformRomsFolders[platform];
            var romTypes = platformRomTypes[platform];
            var emulatorName = platformEmulators.ContainsKey(platform) ? platformEmulators[platform] : "Unknown";
            var emulatorProfile = platformProfiles.ContainsKey(platform) ? platformProfiles[platform] : "Default";
            string[] driveLetters = Environment.GetLogicalDrives();
            int romCount = 0;
            int newGamesCount = 0;
            int updatedGamesCount = 0;

            foreach (string drive in driveLetters)
            {
                try
                {
                    string romsPath = Path.Combine(drive, romsFolder);

                    if (Directory.Exists(romsPath))
                    {
                        logger.Info($"Scanning for ROMs in: {romsPath}");
                        var romFiles = Directory.GetFiles(romsPath, "*.*", SearchOption.AllDirectories)
                                                .Where(f => romTypes.Contains(Path.GetExtension(f).ToLower()))
                                                .ToArray();
                        romCount += romFiles.Length;

                        foreach (var romFile in romFiles)
                        {
                            string gameName = Path.GetFileNameWithoutExtension(romFile);
                            string cleanGameName = CleanGameNameWithoutTranslation(gameName);
                            var existingGame = PlayniteApi.Database.Games.FirstOrDefault(g => g.Name.Equals(cleanGameName, StringComparison.OrdinalIgnoreCase) && g.PluginId == Id);

                            if (existingGame != null)
                            {
                                existingGame.InstallDirectory = Path.GetDirectoryName(romFile);
                                existingGame.GameActions = new ObservableCollection<GameAction>
                        {
                            new GameAction
                            {
                                Type = GameActionType.Emulator,
                                EmulatorId = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals(emulatorName, StringComparison.OrdinalIgnoreCase))?.Id ?? Guid.Empty,
                                EmulatorProfileId = emulatorProfile,
                                Path = romFile
                            }
                        };
                                PlayniteApi.Database.Games.Update(existingGame);
                                logger.Info($"Updated game: {cleanGameName} with ROM: {romFile}");
                                updatedGamesCount++;
                            }
                            else
                            {
                                var newGame = new GameMetadata
                                {
                                    Name = cleanGameName,
                                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platform) },
                                    InstallDirectory = Path.GetDirectoryName(romFile),
                                    GameActions = new List<GameAction>
                            {
                                new GameAction
                                {
                                    Type = GameActionType.Emulator,
                                    EmulatorId = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals(emulatorName, StringComparison.OrdinalIgnoreCase))?.Id ?? Guid.Empty,
                                    EmulatorProfileId = emulatorProfile,
                                    Path = romFile
                                }
                            },
                                    IsInstalled = true
                                };
                                games.Add(newGame);
                                logger.Info($"Added new game: {cleanGameName} with ROM: {romFile}");
                                newGamesCount++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Error scanning for ROMs in folder: {romsFolder}");
                }
            }

            // Log totals
            logger.Info($"Total {platform} ROMs found: {romCount}");
            logger.Info($"Total {platform} games added: {newGamesCount}");
            logger.Info($"Total {platform} games updated: {updatedGamesCount}");

            // Check for no longer installed games
            var noLongerInstalledGames = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && g.Platforms.Any(p => p.Name == platform) && g.IsInstalled && !Directory.Exists(g.InstallDirectory))
                .ToList();

            foreach (var game in noLongerInstalledGames)
            {
                game.IsInstalled = false;
                game.InstallDirectory = null;
                game.GameActions.Clear();
                PlayniteApi.Database.Games.Update(game);
                logger.Info($"Game no longer installed: {game.Name}");
            }

            logger.Info($"Total {platform} games no longer installed: {noLongerInstalledGames.Count}");
        }
    }
}