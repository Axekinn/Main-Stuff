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
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace MyrientNintendo3DS
{
    public class MyrientNintendo3DSStore : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public override Guid Id { get; } = Guid.Parse("293e9fb0-84ed-45ca-a706-321a173b0a12");
        public override string Name => "MyrientNintendo3DS";

        private readonly string configFilePath;
        private readonly string SwitchGamesFilePath;

        public MyrientNintendo3DSStore(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties { HasSettings = false };
            configFilePath = Path.Combine(api.Paths.ExtensionsDataPath, $"{Id}", $"{Id}.config");
            LoadConfig();
            SwitchGamesFilePath = Path.Combine(api.Paths.ExtensionsDataPath, $"{Id}", "Switch Games.txt");
        }

        private static readonly Dictionary<string, List<string>> platformUrls = new Dictionary<string, List<string>>
        {
            { "Nintendo 3DS", new List<string> { "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%203DS%20(Decrypted)/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Nintendo%20-%20Nintendo%203DS%20%5BT-En%5D%20Collection/" } },
            { "Nintendo DS", new List<string> { "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%20DS%20(Decrypted)/" } },
            { "Nintendo DSi", new List<string> { "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%20DSi%20(Decrypted)/", "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%20DSi%20(Digital)/" } },
            { "Nintendo Wii", new List<string> { "https://myrient.erista.me/files/Redump/Nintendo%20-%20Wii%20-%20NKit%20RVZ%20[zstd-19-128k]/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Nintendo%20-%20Wii%20%5BT-En%5D%20Collection/" } },
            { "Nintendo Wii U", new List<string> { "https://myrient.erista.me/files/Internet%20Archive/teamgt19/nintendo-wii-u-usa-full-set-wua-format-embedded-dlc-updates/", "https://myrient.erista.me/files/Internet%20Archive/teamgt19/nintendo-wii-u-eshop-usa-full-set-wua-format-embedded-dlc-updates/" } },
            { "Sony PlayStation", new List<string> { "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sony%20-%20PlayStation%20%5BT-En%5D%20Collection/" } },
            { "Sony PlayStation 2", new List<string> { "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation%202/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sony%20-%20PlayStation%202%20%5BT-En%5D%20Collection/Discs/" } },
            { "Sony PlayStation 3", new List<string> { "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation%203/", "https://myrient.erista.me/files/No-Intro/Sony%20-%20PlayStation%203%20(PSN)%20(Content)/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sony%20-%20PlayStation%203%20%5BT-En%5D%20Collection/" } },
            { "Sony PlayStation Portable", new List<string> { "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation%20Portable/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sony%20-%20PlayStation%20Portable%20%5BT-En%5D%20Collection/Discs/", "https://myrient.erista.me/files/Internet%20Archive/storage_manager/PSP-DLC/%5BNo-Intro%5D%20PSP%20DLC/"  } },
            { "Microsoft Xbox", new List<string> { "https://myrient.erista.me/files/Redump/Microsoft%20-%20Xbox/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Microsoft%20-%20XBOX%20%5BT-En%5D%20Collection/" } },
            { "Microsoft Xbox 360", new List<string> { "https://myrient.erista.me/files/Redump/Microsoft%20-%20Xbox%20360/", "https://myrient.erista.me/files/No-Intro/Microsoft%20-%20Xbox%20360%20(Digital)/" } },
            { "Nintendo GameCube", new List<string> { "https://myrient.erista.me/files/Redump/Nintendo%20-%20GameCube%20-%20NKit%20RVZ%20[zstd-19-128k]/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Nintendo%20-%20GameCube%20%5BT-En%5D%20Collection/" } },
            { "Nintendo Game Boy Advance", new List<string> { "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy%20Advance/" } },
            { "Nintendo Game Boy Color", new List<string> { "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy%20Color/" } },
            { "Nintendo Game Boy", new List<string> { "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy/" } },
            { "Nintendo 64", new List<string> { "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%2064%20(BigEndian)/" } },
            { "Sega Dreamcast", new List<string> { "https://myrient.erista.me/files/Internet%20Archive/chadmaster/dc-chd-zstd-redump/dc-chd-zstd/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sega%20-%20Dreamcast%20%5BT-En%5D%20Collection/" } },
            { "Sega Saturn", new List<string> { "https://myrient.erista.me/files/Redump/Sega%20-%20Saturn/", "https://myrient.erista.me/files/Internet%20Archive/chadmaster/En-ROMs/En-ROMs/Sega%20-%20Saturn%20%5BT-En%5D%20Collection/" } }
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
            { "Sony PlayStation Portable", new List<string> { ".iso", ".cso" , ".chd"} },
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
            { "Nintendo 64", "Roms/Nintendo - 64/Games" },
            { "Sega Dreamcast", "Roms/Sega - Dreamcast/Games" },
            { "Sega Saturn", "Roms/Sega - Saturn/Games" },
            { "Nintendo Switch", "Roms/Nintendo - Switch/Games" }
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
            { "Nintendo Switch", "Ryujinx" },
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
            { "Sony PlayStation Portable", "64bit" },
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

        private Dictionary<string, bool> configDict;
        private bool scrapeUrls;
        private bool importRoms;
        private bool switchAchievements;
        private bool hdTexturePacks;
        private bool romTranslations; // New setting
        public bool ExclusiveGames;

        private void LoadConfig()
        {
            if (!File.Exists(configFilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configFilePath)); // Ensure the directory exists
                File.WriteAllText(configFilePath, GetDefaultConfigContent());
            }

            var configLines = File.ReadAllLines(configFilePath);
            configDict = configLines
                .Select(line => line.Split('='))
                .Where(split => split.Length == 2)
                .ToDictionary(split => split[0].Trim(), split => split[1].Trim().Equals("True", StringComparison.OrdinalIgnoreCase));

            scrapeUrls = configDict.ContainsKey("All") && configDict["All"];
            importRoms = configDict.ContainsKey("ImportRoms") && configDict["ImportRoms"];
            switchAchievements = configDict.ContainsKey("SwitchAchievements") && configDict["SwitchAchievements"];
            hdTexturePacks = configDict.ContainsKey("HDTexturePacks") && configDict["HDTexturePacks"];
            romTranslations = configDict.ContainsKey("RomTranslations") && configDict["RomTranslations"]; // New setting
            ExclusiveGames = configDict.ContainsKey("ExclusiveGames") && configDict["ExclusiveGames"]; // New setting

            // Ensure that platforms are scraped correctly based on the configuration
            if (!scrapeUrls)
            {
                foreach (var platform in platformUrls.Keys)
                {
                    if (configDict.ContainsKey(platform) && configDict[platform])
                    {
                        scrapeUrls = true;
                        break;
                    }
                }
            }
        }

        private string GetDefaultConfigContent()
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Scrape URLs");
            builder.AppendLine("All=False");

            // Add all platforms to the config file
            foreach (var platform in platformUrls.Keys)
            {
                builder.AppendLine($"{platform}=False");
            }

            builder.AppendLine();
            builder.AppendLine("ImportRoms=True");
            builder.AppendLine("SwitchAchievements=False"); // Add the new setting
            builder.AppendLine("HDTexturePacks=False"); // New setting
            builder.AppendLine("RomTranslations=False"); // New setting
            builder.AppendLine("ExclusiveGames=False"); // New setting

            return builder.ToString();
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
{
    // Reload configuration to ensure we have the latest settings
    LoadConfig();

    var games = new List<GameMetadata>();

    if (scrapeUrls)
    {
        logger.Info("Scraping URLs is enabled.");

        foreach (var platformKey in platformUrls.Keys)
        {
            // Check if "All" is enabled or the specific platform is enabled
            if (configDict["All"] || (configDict.ContainsKey(platformKey) && configDict[platformKey]))
            {
                logger.Info($"Starting scraping for platform: {platformKey}");
                var scrapedGames = ScrapeAllUrlsForPlatform(platformKey).GetAwaiter().GetResult();
                logger.Info($"Total {platformKey} games: {scrapedGames.Count}");

                foreach (var game in scrapedGames)
                {
                    var platform = PlayniteApi.Database.Platforms.FirstOrDefault(pl => pl.Name.Equals(platformKey, StringComparison.OrdinalIgnoreCase));

                    if (platform == null)
                    {
                        logger.Warn($"Could not find platform for {platformKey}. Ensure that the platform is defined in Playnite and try again.");
                        continue;
                    }

                    var existingGame = PlayniteApi.Database.Games.FirstOrDefault(g => g.Name.Equals(game.Name, StringComparison.OrdinalIgnoreCase) && g.PlatformIds.Contains(platform.Id));

                    if (existingGame == null)
                    {
                        try
                        {
                            logger.Info($"Adding new game: {game.Name} to Playnite database with platform {platformKey}.");
                            var newGame = new Game
                            {
                                PluginId = Id,
                                Name = game.Name,
                                PlatformIds = new List<Guid> { platform.Id },
                                GameActions = new ObservableCollection<GameAction>(game.GameActions),
                                IsInstalled = game.IsInstalled
                            };

                            PlayniteApi.Database.Games.Add(newGame);

                            // Add the new game to the list of games to be returned
                            games.Add(new GameMetadata
                            {
                                Name = newGame.Name,
                                GameId = newGame.GameId,
                                Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platform.Name) },
                                GameActions = newGame.GameActions.ToList(),
                                IsInstalled = newGame.IsInstalled,
                                InstallDirectory = newGame.InstallDirectory
                            });

                            // Notify Playnite about the new game
                            PlayniteApi.Notifications.Add(new NotificationMessage(
                                Guid.NewGuid().ToString(),
                                $"New game added: {newGame.Name}",
                                NotificationType.Info
                            ));

                            // Trigger metadata download for the new game
                            PlayniteApi.Database.Games.Update(newGame);

                            logger.Info($"Successfully added new game: {newGame.Name} with GameId: {newGame.GameId}");
                        }
                        catch (Exception ex)
                        {
                            logger.Error($"Error adding new game: {game.Name}, Exception: {ex.Message}");
                        }
                    }
                    else
                    {
                        logger.Info($"Game already exists: {game.Name} on platform {platformKey}. Skipping.");
                    }
                }
            }
            else
            {
                logger.Info($"Scraping for platform {platformKey} is disabled.");
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
            try
            {
                ScanRomsAndUpdateGames(platform, games).GetAwaiter().GetResult();
                logger.Info($"Successfully scanned ROMs and updated games for platform: {platform}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error scanning ROMs and updating games for platform: {platform}, Exception: {ex.Message}");
            }
        }
    }
    else
    {
        logger.Info("Importing ROMs is disabled.");
    }

    // Check for HD texture packs
    if (hdTexturePacks)
    {
        foreach (var platform in platformHdTextureUrls.Keys)
        {
            try
            {
                logger.Info($"Checking for HD texture packs for platform: {platform}");
                CheckForHdTexturePacks(platform).GetAwaiter().GetResult();
                logger.Info($"Successfully checked for HD texture packs for platform: {platform}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error checking for HD texture packs for platform: {platform}, Exception: {ex.Message}");
            }
        }
    }
    else
    {
        logger.Info("HD texture packs check is disabled.");
    }

    // Check for Exclusive Games
    if (ExclusiveGames)
    {
        foreach (var platform in platformExclusiveGamesUrls.Keys)
        {
            try
            {
                logger.Info($"Checking for Exclusive Games for platform: {platform}");
                CheckForExclusiveGames(platform).GetAwaiter().GetResult();
                logger.Info($"Successfully checked for Exclusive Games for platform: {platform}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error checking for Exclusive Games for platform: {platform}, Exception: {ex.Message}");
            }
        }
    }
    else
    {
        logger.Info("Exclusive Games check is disabled.");
    }

    // Check for Switch achievements
    if (switchAchievements)
    {
        foreach (var game in games)
        {
            var platformName = PlayniteApi.Database.Games.FirstOrDefault(g => g.GameId == game.GameId)?.Platforms.FirstOrDefault()?.Name;
            if (platformName != null && platformName.Equals("Nintendo Switch", StringComparison.OrdinalIgnoreCase))
            {
                CheckForSwitchGameJson(game.Name).GetAwaiter().GetResult();
            }
        }
    }
    else
    {
        logger.Info("Switch achievements check is disabled.");
    }

    // Check for ROM translations
    if (romTranslations)
    {
        foreach (var platformKey in platformUrls.Keys)
        {
            try
            {
                logger.Info($"Checking for ROM translations for platform: {platformKey}");
                CheckForRomTranslations(platformKey).GetAwaiter().GetResult();
                logger.Info($"Successfully checked for ROM translations for platform: {platformKey}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error checking for ROM translations for platform: {platformKey}, Exception: {ex.Message}");
            }
        }
    }
    else
    {
        logger.Info("ROM translations check is disabled.");
    }

    // Utilize GetActionName method to ensure actions are properly named
    foreach (var game in games)
    {
        foreach (var action in game.GameActions)
        {
            action.Name = GetActionName(action.Name, game.Name);
        }
    }

    // Filter duplicates using IsDuplicate method
    games = games.Where(g => !IsDuplicate(g)).ToList();

    // Return the list of games added
    logger.Info($"Total games added: {games.Count}");
    return games;
}


        private async Task ScanRomsAndUpdateGames(string platform, List<GameMetadata> games)
        {
            string[] driveLetters = Environment.GetLogicalDrives();
            var romPaths = new HashSet<string>();
            var switchGames = new List<string>();
            var existingSwitchGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hdTextureGames = new List<string>();
            var existingHdTextureGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Load existing Switch games from the file if it exists
            if (File.Exists(SwitchGamesFilePath))
            {
                existingSwitchGames.UnionWith(File.ReadAllLines(SwitchGamesFilePath));
            }

            // Scan for ROM files in specified directories
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
                .Where(g => g.PlatformIds != null && g.Platforms.Any(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var existingGameNames = allExistingGames
                .Select(g => CleanGameName(g.Name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // First, add ROMs to existing games where applicable
            foreach (var game in allExistingGames.Where(g => g.PluginId == Id))
            {
                // Initialize ROMs collection if null
                if (game.Roms == null)
                {
                    game.Roms = new ObservableCollection<GameRom>();
                    logger.Warn($"Game '{game.Name}' had null Roms collection. Initialized an empty Roms collection.");
                }

                var romFilesToAdd = romPaths.Where(rp => CleanGameName(Path.GetFileNameWithoutExtension(rp)).Equals(CleanGameName(game.Name), StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var romFile in romFilesToAdd)
                {
                    if (!game.Roms.Any(r => r.Path.Equals(romFile, StringComparison.OrdinalIgnoreCase)))
                    {
                        game.Roms.Add(new GameRom(Path.GetFileNameWithoutExtension(romFile), romFile));
                        logger.Info($"Added ROM '{romFile}' to existing game '{game.Name}'");

                        // Ensure the emulator and profile as play action
                        var emulatorName = platformEmulators.ContainsKey(platform) ? platformEmulators[platform] : string.Empty;
                        var emulator = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals(emulatorName, StringComparison.OrdinalIgnoreCase));
                        if (emulator != null)
                        {
                            var builtInProfiles = emulator.BuiltinProfiles?.ToList();
                            if (builtInProfiles != null && builtInProfiles.Any())
                            {
                                var profileName = platformProfiles.ContainsKey(platform) ? platformProfiles[platform] : string.Empty;
                                var emulatorProfile = builtInProfiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
                                if (emulatorProfile != null)
                                {
                                    var playAction = new GameAction
                                    {
                                        Name = "Play",
                                        Type = GameActionType.Emulator,
                                        EmulatorId = emulator.Id,
                                        EmulatorProfileId = emulatorProfile.Id,
                                        Path = romFile,
                                        IsPlayAction = true
                                    };
                                    if (!game.GameActions.Any(a => a.Name == "Play" && a.Type == GameActionType.Emulator))
                                    {
                                        game.GameActions.Add(playAction);
                                        logger.Info($"Added play action for emulator '{emulator.Name}' and profile '{emulatorProfile.Name}' to existing game '{game.Name}'");
                                    }
                                }
                            }
                        }
                    }
                }

                if (game.Roms.Count == 0)
                {
                    game.IsInstalled = false;
                    logger.Info($"Marked game as not installed: {game.Name} due to no remaining ROMs.");
                    // Remove Play action if no ROMs exist
                    var playAction = game.GameActions.FirstOrDefault(a => a.Name == "Play" && a.Type == GameActionType.Emulator);
                    if (playAction != null)
                    {
                        game.GameActions.Remove(playAction);
                        logger.Info($"Removed play action for game '{game.Name}' due to no remaining ROMs.");
                    }
                }
                else
                {
                    game.IsInstalled = true;
                }

                PlayniteApi.Database.Games.Update(game);
            }

            // Dictionary to hold new games to be added
            var gamesToAdd = new Dictionary<string, Game>();

            // Add new ROMs to new games if no matching existing game is found
            foreach (var romFile in romPaths)
            {
                string originalGameName = Path.GetFileNameWithoutExtension(romFile);
                string cleanedGameName = CleanGameName(originalGameName);

                if (IsAddonOrDLC(originalGameName))
                {
                    var baseGameName = ExtractAddonOrDLCBaseGameName(originalGameName);
                    var baseGame = allExistingGames.FirstOrDefault(g => CleanGameName(g.Name).Equals(baseGameName, StringComparison.OrdinalIgnoreCase));

                    if (baseGame != null)
                    {
                        var gameActions = baseGame.GameActions.ToList();
                        gameActions.Add(new GameAction
                        {
                            Name = $"Download Myrient: {originalGameName}",
                            Type = GameActionType.URL,
                            Path = romFile,
                            IsPlayAction = false
                        });

                        baseGame.GameActions = new ObservableCollection<GameAction>(gameActions);
                        PlayniteApi.Database.Games.Update(baseGame);
                        logger.Info($"Added action for {baseGame.Name}: {originalGameName}");
                    }
                    else
                    {
                        logger.Warn($"Base game not found for DLC/Add-on: {originalGameName}");
                    }
                }
                else if (!existingGameNames.Contains(cleanedGameName))
                {
                    var platformObj = PlayniteApi.Database.Platforms.FirstOrDefault(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase));
                    if (platformObj == null)
                    {
                        logger.Error($"Platform '{platform}' not found in Playnite database.");
                        continue;
                    }

                    var newGame = new Game
                    {
                        PluginId = Id, // Ensure the game is added via your plugin
                        Name = cleanedGameName,
                        Roms = new ObservableCollection<GameRom> { new GameRom(originalGameName, romFile) },
                        InstallDirectory = Path.GetDirectoryName(romFile),
                        IsInstalled = true,
                        PlatformIds = new List<Guid> { platformObj.Id }
                    };

                    var emulatorName = platformEmulators.ContainsKey(platform) ? platformEmulators[platform] : string.Empty;
                    var emulator = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals(emulatorName, StringComparison.OrdinalIgnoreCase));
                    if (emulator == null)
                    {
                        logger.Error($"Emulator not found for platform: {platform}");
                        continue;
                    }

                    var builtInProfiles = emulator.BuiltinProfiles?.ToList();
                    if (builtInProfiles == null || !builtInProfiles.Any())
                    {
                        logger.Error($"No built-in profiles found for emulator: {emulator.Name}");
                        continue;
                    }

                    var profileName = platformProfiles.ContainsKey(platform) ? platformProfiles[platform] : string.Empty;
                    var emulatorProfile = builtInProfiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
                    if (emulatorProfile == null)
                    {
                        logger.Error($"Profile '{profileName}' not found for platform: {platform}");
                        continue;
                    }

                    var playAction = new GameAction
                    {
                        Name = "Play",
                        Type = GameActionType.Emulator,
                        EmulatorId = emulator.Id,
                        EmulatorProfileId = emulatorProfile.Id,
                        Path = romFile,
                        IsPlayAction = true
                    };

                    newGame.GameActions = new ObservableCollection<GameAction> { playAction };

                    gamesToAdd[cleanedGameName] = newGame;
                }
                else
                {
                    logger.Info($"Game already exists: {cleanedGameName} on platform {platform}. Skipping.");
                }
            }

            // Add new games to Playnite via your plugin
            foreach (var game in gamesToAdd.Values)
            {
                PlayniteApi.Database.Games.Add(game);
                logger.Info($"Added new game: {game.Name} with play action.");

                var gameMetadata = new GameMetadata
                {
                    Name = game.Name,
                    GameId = game.GameId,
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platform) },
                    GameActions = game.GameActions.ToList(),
                    IsInstalled = game.IsInstalled,
                    InstallDirectory = game.InstallDirectory
                };

                games.Add(gameMetadata);
                PlayniteApi.Database.Games.Update(game);

                // Log details of the new game
                logger.Info($"New game added: {game.Name}");
                logger.Info($"ROMs added to game '{game.Name}': {string.Join(", ", game.Roms.Select(r => r.Path))}");
            }

            // Save the Switch Games list to a text file in the same directory as the .config file
            if (switchGames.Count > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SwitchGamesFilePath)); // Ensure the directory exists
                File.WriteAllLines(SwitchGamesFilePath, switchGames);
                logger.Info($"Saved Nintendo Switch games list to: {SwitchGamesFilePath}");
            }

            logger.Info($"ROM scanning and updating completed for platform: {platform}");

            // Now, check for HD texture packs
            await CheckForHdTexturePacks(platform);
        }
              
        private bool IsAddonOrDLC(string name)
        {
            return name.IndexOf("DLC", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Addon", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string ExtractAddonOrDLCBaseGameName(string name)
        {
            return Regex.Replace(name, @"\s*-.*\((Addon|DLC)\)", "", RegexOptions.IgnoreCase).Trim();
        }

        private async Task CheckForHdTexturePacks(string platform)
        {
            try
            {
                logger.Info($"Checking for HD texture packs for platform: {platform}");

                if (!platformHdTextureUrls.ContainsKey(platform))
                {
                    logger.Info($"No HD texture URL found for platform: {platform}");
                    return;
                }

                string url = platformHdTextureUrls[platform];
                string gameTxtContent;
                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        gameTxtContent = await response.Content.ReadAsStringAsync();
                        logger.Info($"Successfully downloaded content from {url} for platform: {platform}");
                    }
                    else
                    {
                        logger.Error($"Failed to download HD texture pack list for platform: {platform} from {url}");
                        return;
                    }
                }

                // Read GitHub .txt file and extract game names, authors, and URLs
                string[] lines = gameTxtContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                bool readGameData = false;
                var hdTexturePacks = new List<(string GameName, string Author, string DownloadUrl)>();
                foreach (var line in lines)
                {
                    logger.Info($"Processing line: {line}");
                    if (line.StartsWith("# Games:"))
                    {
                        readGameData = true;
                        continue;
                    }
                    if (!readGameData)
                    {
                        continue;
                    }
                    if (line.StartsWith("Name:"))
                    {
                        string gameName = line.Split(new[] { ':' }, 2)[1].Trim().Trim('"');
                        hdTexturePacks.Add((gameName, string.Empty, string.Empty));
                        logger.Info($"Platform: {platform} | Extracted game name: {gameName}");
                    }
                    else if (hdTexturePacks.Count > 0)
                    {
                        if (line.StartsWith("Author:"))
                        {
                            hdTexturePacks[hdTexturePacks.Count - 1] = (hdTexturePacks[hdTexturePacks.Count - 1].GameName, line.Split(new[] { ':' }, 2)[1].Trim().Trim('"'), hdTexturePacks[hdTexturePacks.Count - 1].DownloadUrl);
                            logger.Info($"Platform: {platform} | Extracted author: {hdTexturePacks[hdTexturePacks.Count - 1].Author}");
                        }
                        else if (line.StartsWith("Urls:"))
                        {
                            hdTexturePacks[hdTexturePacks.Count - 1] = (hdTexturePacks[hdTexturePacks.Count - 1].GameName, hdTexturePacks[hdTexturePacks.Count - 1].Author, line.Split(new[] { ':' }, 2)[1].Trim().Trim('"'));
                            logger.Info($"Platform: {platform} | Extracted URL: {hdTexturePacks[hdTexturePacks.Count - 1].DownloadUrl}");
                        }
                    }
                }

                logger.Info($"Total extracted HD texture packs for platform {platform}: {hdTexturePacks.Count}");

                // Check each entry against Playnite database
                var hdTextureGames = new List<string>();
                string hdTextureGamesFilePath = Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, $"{Id}", $"{platform}.HD.txt");
                var existingHdTextureGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(hdTextureGamesFilePath))
                {
                    existingHdTextureGames.UnionWith(File.ReadAllLines(hdTextureGamesFilePath));
                }

                var allExistingGames = PlayniteApi.Database.Games
                    .Where(g => g.PlatformIds != null && g.Platforms.Any(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                foreach (var (gameName, author, downloadUrl) in hdTexturePacks)
                {
                    logger.Info($"Platform: {platform} | Checking for game: {gameName} in Playnite database.");
                    var game = allExistingGames.FirstOrDefault(g => g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
                    if (game != null)
                    {
                        logger.Info($"Platform: {platform} | Found matching game in Playnite: {gameName}");
                        bool hasExclusiveGames = await CheckForExclusiveGames(platform, gameName, author, downloadUrl);
                        if (hasExclusiveGames)
                        {
                            hdTextureGames.Add($"{game.Name}: {game.GameId}: True");
                            existingHdTextureGames.Add($"{game.Name}: {game.GameId}");
                        }
                    }
                    else
                    {
                        logger.Info($"Platform: {platform} | No matching game found for HD texture pack: {gameName}");
                    }
                }

                // Save the HD Texture Games list to a text file in the same directory as the .config file
                if (hdTextureGames.Count > 0)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(hdTextureGamesFilePath)); // Ensure the directory exists
                    File.WriteAllLines(hdTextureGamesFilePath, hdTextureGames);
                    logger.Info($"Platform: {platform} | Saved HD texture games list to: {hdTextureGamesFilePath}");
                }
                else
                {
                    logger.Info($"No HD texture packs matched for platform: {platform}");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error checking for HD texture packs for platform: {platform}, Exception: {ex.Message}");
            }
        }

        private async Task<bool> CheckForExclusiveGames(string platform, string gameName, string author, string downloadUrl)
        {
            try
            {
                // Ensure the HD Texture Pack feature exists in Playnite
                var hdTexturePackFeature = PlayniteApi.Database.Features.FirstOrDefault(f => f.Name.Equals("HD Texture Pack", StringComparison.OrdinalIgnoreCase));
                if (hdTexturePackFeature == null)
                {
                    hdTexturePackFeature = new GameFeature("HD Texture Pack");
                    PlayniteApi.Database.Features.Add(hdTexturePackFeature);
                    PlayniteApi.Database.Features.Update(hdTexturePackFeature);
                    logger.Info("Added HD Texture Pack feature to Playnite database");
                }

                // Find and update all games with HD Texture Download action
                var games = PlayniteApi.Database.Games.Where(g => g.Platforms.Any(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase)));
                foreach (var game in games)
                {
                    var actionName = $"HD Texture Download: by {author}";
                    var existingAction = game.GameActions?.FirstOrDefault(a => a.Name.Equals(actionName, StringComparison.OrdinalIgnoreCase));

                    if (existingAction != null && !game.FeatureIds.Contains(hdTexturePackFeature.Id))
                    {
                        game.FeatureIds.Add(hdTexturePackFeature.Id);
                        PlayniteApi.Database.Games.Update(game);
                        logger.Info($"Added HD Texture Pack feature to game {game.Name}");
                    }
                }

                // Update or add the HD Texture Download action for the specific game
                var specificGame = PlayniteApi.Database.Games.FirstOrDefault(g => g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase) && g.Platforms.Any(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase)));
                if (specificGame != null)
                {
                    var actionName = $"HD Texture Download: by {author}";
                    var existingAction = specificGame.GameActions?.FirstOrDefault(a => a.Name.Equals(actionName, StringComparison.OrdinalIgnoreCase));

                    if (existingAction == null)
                    {
                        var hdTextureAction = new GameAction
                        {
                            Name = actionName,
                            Type = GameActionType.URL,
                            Path = downloadUrl,
                            IsPlayAction = false
                        };

                        if (specificGame.GameActions == null)
                        {
                            specificGame.GameActions = new ObservableCollection<GameAction>();
                        }

                        specificGame.GameActions.Add(hdTextureAction);
                        logger.Info($"Added HD texture download action for {gameName}: {actionName}");

                        if (specificGame.FeatureIds == null)
                        {
                            specificGame.FeatureIds = new List<Guid>();
                        }

                        if (!specificGame.FeatureIds.Contains(hdTexturePackFeature.Id))
                        {
                            specificGame.FeatureIds.Add(hdTexturePackFeature.Id);
                            logger.Info($"Added HD Texture Pack feature to game {gameName}");
                        }

                        PlayniteApi.Database.Games.Update(specificGame);
                    }
                    else
                    {
                        logger.Info($"HD texture download action for {gameName} already exists: {actionName}");
                    }

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                logger.Error($"Error adding HD texture download action for game {gameName}, Exception: {ex.Message}");
                return false;
            }
        }

        private async Task CheckForExclusiveGames(string platform)
        {
            try
            {
                logger.Info($"Checking for Exclusive Games for platform: {platform}");

                if (!platformExclusiveGamesUrls.ContainsKey(platform))
                {
                    logger.Info($"No Exclusive Games URL found for platform: {platform}");
                    return;
                }

                string url = platformExclusiveGamesUrls[platform];
                string gameTxtContent;
                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        gameTxtContent = await response.Content.ReadAsStringAsync();
                        logger.Info($"Successfully downloaded content from {url} for platform: {platform}");
                    }
                    else
                    {
                        logger.Error($"Failed to download Exclusive Games list for platform: {platform} from {url}");
                        return;
                    }
                }

                // Read GitHub .txt file and extract game names
                string[] lines = gameTxtContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var exclusiveGames = new List<string>();
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                    {
                        string gameName = line.Trim();
                        exclusiveGames.Add(gameName);
                        logger.Info($"Platform: {platform} | Extracted game name: {gameName}");
                    }
                }

                logger.Info($"Total extracted Exclusive Games for platform {platform}: {exclusiveGames.Count}");

                // Check each entry against Playnite database
                var exclusiveGamesList = new List<string>();
                string exclusiveGamesFilePath = Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, $"{Id}", $"{platform}.Exclusive.txt");
                var existingExclusiveGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(exclusiveGamesFilePath))
                {
                    existingExclusiveGames.UnionWith(File.ReadAllLines(exclusiveGamesFilePath));
                }

                foreach (var gameName in exclusiveGames)
                {
                    logger.Info($"Platform: {platform} | Checking for game: {gameName} in Playnite database.");
                    var game = PlayniteApi.Database.Games.FirstOrDefault(g => g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase) && g.Platforms.Any(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase)));
                    if (game != null)
                    {
                        logger.Info($"Platform: {platform} | Found matching game in Playnite: {gameName}");
                        bool hasExclusiveGame = await CheckForExclusiveGame(platform, gameName);
                        if (hasExclusiveGame)
                        {
                            exclusiveGamesList.Add($"{game.Name}: {game.GameId}: True");
                            existingExclusiveGames.Add($"{game.Name}: {game.GameId}");
                        }
                    }
                    else
                    {
                        logger.Info($"Platform: {platform} | No matching game found for Exclusive Game: {gameName}");
                    }
                }

                // Save the Exclusive Games list to a text file in the same directory as the .config file
                if (exclusiveGamesList.Count > 0)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(exclusiveGamesFilePath)); // Ensure the directory exists
                    File.WriteAllLines(exclusiveGamesFilePath, exclusiveGamesList);
                    logger.Info($"Platform: {platform} | Saved Exclusive Games list to: {exclusiveGamesFilePath}");
                }
                else
                {
                    logger.Info($"No Exclusive Games matched for platform: {platform}");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error checking for Exclusive Games for platform: {platform}, Exception: {ex.Message}");
            }
        }


        private async Task<bool> CheckForExclusiveGame(string platform, string gameName)
        {
            try
            {
                // Ensure the Exclusive Games feature exists in Playnite
                var exclusiveGameFeature = PlayniteApi.Database.Features.FirstOrDefault(f => f.Name.Equals("Exclusive Games", StringComparison.OrdinalIgnoreCase));
                if (exclusiveGameFeature == null)
                {
                    exclusiveGameFeature = new GameFeature("Exclusive Games");
                    PlayniteApi.Database.Features.Add(exclusiveGameFeature);
                    PlayniteApi.Database.Features.Update(exclusiveGameFeature);
                    logger.Info("Added Exclusive Games feature to Playnite database");
                }

                // Update the specific game to include the Exclusive Games feature
                var specificGame = PlayniteApi.Database.Games.FirstOrDefault(g => g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase) && g.Platforms.Any(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase)));
                if (specificGame != null)
                {
                    if (specificGame.FeatureIds == null)
                    {
                        specificGame.FeatureIds = new List<Guid>();
                    }

                    if (!specificGame.FeatureIds.Contains(exclusiveGameFeature.Id))
                    {
                        specificGame.FeatureIds.Add(exclusiveGameFeature.Id);
                        logger.Info($"Added Exclusive Games feature to game {gameName}");
                        PlayniteApi.Database.Games.Update(specificGame);
                    }
                    else
                    {
                        logger.Info($"Exclusive Games feature already exists for {gameName}");
                    }

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                logger.Error($"Error adding Exclusive Games feature to game {gameName}, Exception: {ex.Message}");
                return false;
            }
        }


        private async Task<bool> CheckForSwitchGameJson(string gameName)
        {
            string jsonFileName = $"{gameName}.json";
            string url = $"https://github.com/Koriebonx98/Switch-Achievements-/tree/main/Games/{jsonFileName}";

            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    logger.Info($"Found JSON file for {gameName} at {url}");
                    return true;
                }
                else
                {
                    logger.Info($"No JSON file found for {gameName} at {url}");
                    return false;
                }
            }
        }

        private static readonly Dictionary<string, string> platformHdTextureUrls = new Dictionary<string, string>
{
    { "Sony PlayStation Portable", "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/main/HD%20Textures/Sony%20-%20Playstation%20Portable/Games.txt" },
    { "Sony PlayStation", "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/refs/heads/main/HD%20Textures/Sony%20-%20Playstation/Games.txt" },
    { "Sony PlayStation 2", "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/refs/heads/main/HD%20Textures/Sony%20-%20Playstation%202/Games.txt" },
    { "Nintendo Wii", "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/refs/heads/main/HD%20Textures/Nintendo%20-%20WII/Games.txt" },
    // Add other platforms and their URLs as needed
};

        private static readonly Dictionary<string, string> platformExclusiveGamesUrls = new Dictionary<string, string>
{
    { "Sony PlayStation 3", "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/refs/heads/main/Sony%20Playstation%203.Exclusives.txt" },
    { "Sony PlayStation", "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/refs/heads/main/Sony%20Playstation.Exclusives.txt" },
    { "Sony PlayStation 2", "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/refs/heads/main/Sony%20Playstation%202.Exclusives.txt" },
    { "Microsoft Xbox 360", "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/refs/heads/main/Microsoft%20Xbox%20360.Exclusives.txt" },
    { "Microsoft Xbox", "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/refs/heads/main/Microsoft%20Xbox.Exclusives" },


        // Add other platforms and their URLs as needed
};


        private string CleanGameName(string name)
        {
            name = Regex.Replace(name, @"\s*\(Rev \d+\)", "");
            name = Regex.Replace(name, @"\s*\(Disc \d+\)", "");
            name = Regex.Replace(name, @"\s*\(.*?\)", "").Replace(".zip", "").Replace(".wua", "").Replace(".7z", "").Replace(".chd", "").Trim();

            if (name.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(4);
            }

            // Replace hyphen with colon if it's not connecting two words
            name = Regex.Replace(name, @"(?<!\w)-\s+(?!\w)", ": ");

            return name;
        }

        private async Task<string> LoadPageContent(string url, string platform)
        {
            using (var httpClient = new HttpClient())
            {
                try
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                    HttpResponseMessage response = await httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();
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
                string text = Regex.Replace(match.Groups[2].Value, "<.*?>", string.Empty);
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
                logger.Warn($"Skipping URL: {baseUrl} for platform {platform} due to empty content");
                return gameEntries;
            }

            var links = ParseLinks(pageContent);
            var allExistingGames = PlayniteApi.Database.Games
                .Where(g => g.Platforms != null && g.Platforms.Any(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var existingGameNames = allExistingGames
                .Select(g => CleanGameName(g.Name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var gamesToAdd = new Dictionary<string, Game>();
            var dlcOrAddons = new List<(string Text, string Href, string BaseGameName)>();

            // First pass: Add games and collect DLC/Addons
            foreach (var link in links)
            {
                string href = link.Item1;
                string text = WebUtility.HtmlDecode(link.Item2);

                if (IsValidGameLink(href, text))
                {
                    string cleanName = CleanGameName(text);
                    string gameNameForKey = cleanName;

                    if (text.Contains("(Dlc)") || text.Contains("(Addon)"))
                    {
                        int index = text.IndexOf(" - ");
                        if (index >= 0)
                        {
                            string baseGameName = text.Substring(0, index).Trim();
                            dlcOrAddons.Add((text, href, baseGameName));
                        }
                        else
                        {
                            logger.Warn($"Invalid format for DLC/Add-on: {text}");
                        }
                    }
                    else if (text.Contains("(XBLA)"))
                    {
                        if (!existingGameNames.Contains(gameNameForKey))
                        {
                            logger.Info($"Adding new game: {cleanName} with URL: {href}");

                            var platformObj = PlayniteApi.Database.Platforms.FirstOrDefault(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase));
                            if (platformObj == null)
                            {
                                logger.Error($"Platform '{platform}' not found in Playnite database.");
                                continue;
                            }

                            var newGame = new Game()
                            {
                                PluginId = Id,
                                Name = cleanName,
                                Roms = new ObservableCollection<GameRom>(), // Initialize Roms collection
                                InstallDirectory = string.Empty,
                                IsInstalled = false, // Set to true to trigger metadata fetching
                                PlatformIds = new List<Guid> { platformObj.Id }
                            };

                            newGame.GameActions = new ObservableCollection<GameAction>
                    {
                        new GameAction
                        {
                            Name = $"Download Myrient: {text}",
                            Type = GameActionType.URL,
                            Path = href.StartsWith("http") ? href : $"{baseUrl}{href}",
                            IsPlayAction = false
                        }
                    };

                            gamesToAdd[cleanName] = newGame;
                        }
                    }
                    else
                    {
                        if (!existingGameNames.Contains(cleanName))
                        {
                            logger.Info($"Adding new game: {cleanName} with URL: {href}");

                            var platformObj = PlayniteApi.Database.Platforms.FirstOrDefault(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase));
                            if (platformObj == null)
                            {
                                logger.Error($"Platform '{platform}' not found in Playnite database.");
                                continue;
                            }

                            var newGame = new Game()
                            {
                                PluginId = Id,
                                Name = cleanName,
                                Roms = new ObservableCollection<GameRom>(), // Initialize Roms collection
                                InstallDirectory = string.Empty,
                                IsInstalled = false, // Set to true to trigger metadata fetching
                                PlatformIds = new List<Guid> { platformObj.Id }
                            };

                            newGame.GameActions = new ObservableCollection<GameAction>
                    {
                        new GameAction
                        {
                            Name = $"Download Myrient: {text}",
                            Type = GameActionType.URL,
                            Path = href.StartsWith("http") ? href : $"{baseUrl}{href}",
                            IsPlayAction = false
                        }
                    };

                            gamesToAdd[cleanName] = newGame;
                        }
                        else
                        {
                            var existingGame = gamesToAdd.ContainsKey(cleanName) ? gamesToAdd[cleanName] : allExistingGames.FirstOrDefault(g => CleanGameName(g.Name) == cleanName);
                            if (existingGame != null)
                            {
                                var gameActions = existingGame.GameActions.ToList();
                                gameActions.Add(new GameAction
                                {
                                    Name = $"Download Myrient: {text}",
                                    Type = GameActionType.URL,
                                    Path = href.StartsWith("http") ? href : $"{baseUrl}{href}",
                                    IsPlayAction = false
                                });
                                existingGame.GameActions = new ObservableCollection<GameAction>(gameActions);
                            }
                            else
                            {
                                logger.Warn($"Could not find existing game entry for: {cleanName}");
                            }
                        }
                    }
                }
            }

            // Add new games to Playnite
            foreach (var game in gamesToAdd.Values)
            {
                PlayniteApi.Database.Games.Add(game);
                logger.Info($"Added new game: {game.Name} with URL action.");

                // Trigger metadata fetching
                await Task.Run(() =>
                {
                    var updatedGame = PlayniteApi.Database.Games.FirstOrDefault(g => g.GameId == game.GameId);
                    if (updatedGame != null)
                    {
                        PlayniteApi.Database.Games.Update(updatedGame);
                        logger.Info($"Metadata fetched for new game: {updatedGame.Name}");
                    }
                });

                // Create GameMetadata and add to the list
                var gameMetadata = new GameMetadata
                {
                    Name = game.Name,
                    GameId = game.GameId,
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platform) },
                    GameActions = game.GameActions.ToList(),
                    IsInstalled = game.IsInstalled,
                    InstallDirectory = game.InstallDirectory
                };

                gameEntries.Add(gameMetadata);
            }

            // Second pass: Add DLC/Addons to existing games
            foreach (var dlcOrAddon in dlcOrAddons)
            {
                var baseGame = PlayniteApi.Database.Games.FirstOrDefault(g => CleanGameName(g.Name).Contains(CleanGameName(dlcOrAddon.BaseGameName)) && g.Platforms.Any(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase)));

                if (baseGame != null && CleanGameName(baseGame.Name) == CleanGameName(dlcOrAddon.BaseGameName))
                {
                    var gameActions = baseGame.GameActions.ToList();
                    gameActions.Add(new GameAction
                    {
                        Name = $"Download Myrient: {dlcOrAddon.Text}",
                        Type = GameActionType.URL,
                        Path = dlcOrAddon.Href.StartsWith("http") ? dlcOrAddon.Href : $"{baseUrl}{dlcOrAddon.Href}",
                        IsPlayAction = false
                    });

                    baseGame.GameActions = new ObservableCollection<GameAction>(gameActions);
                    PlayniteApi.Database.Games.Update(baseGame);
                    logger.Info($"Added DLC/Addon '{dlcOrAddon.Text}' to base game '{baseGame.Name}'");
                }
                else
                {
                    logger.Warn($"Base game not found for DLC/Add-on: {dlcOrAddon.Text}");
                }
            }

            logger.Info($"URL scraping and updating completed for platform: {platform}");

            return uniqueGames.Values.ToList();
        }

        private async Task CheckForRomTranslations(string platform)
        {
            try
            {
                logger.Info($"Checking for ROM translations for platform: {platform}");

                var allExistingGames = PlayniteApi.Database.Games
                    .Where(g => g.PlatformIds != null && g.Platforms.Any(p => p.Name.Equals(platform, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (allExistingGames == null || allExistingGames.Count == 0)
                {
                    logger.Warn($"No games found for platform: {platform}");
                    return;
                }

                var romTranslationFeature = PlayniteApi.Database.Features.FirstOrDefault(f => f.Name.Equals("Rom Translation", StringComparison.OrdinalIgnoreCase));
                if (romTranslationFeature == null)
                {
                    romTranslationFeature = new GameFeature("Rom Translation");
                    PlayniteApi.Database.Features.Add(romTranslationFeature);
                    PlayniteApi.Database.Features.Update(romTranslationFeature);
                    logger.Info("Added Rom Translation feature to Playnite database");
                }

                foreach (var game in allExistingGames)
                {
                    if (game == null)
                    {
                        logger.Warn("Encountered a null game object");
                        continue;
                    }

                    logger.Info($"Processing game: {game.Name}");

                    if (game.GameActions == null)
                    {
                        logger.Warn($"Game {game.Name} has null GameActions");
                        continue;
                    }

                    foreach (var action in game.GameActions)
                    {
                        if (action == null)
                        {
                            logger.Warn($"Game {game.Name} has a null GameAction");
                            continue;
                        }

                        if (action.Name == null)
                        {
                            logger.Warn($"GameAction in {game.Name} has a null Name");
                            continue;
                        }

                        try
                        {
                            if (action.Name.IndexOf("[T-En", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                logger.Info($"GameAction '{action.Name}' contains '[T-En'");

                                if (game.FeatureIds == null)
                                {
                                    logger.Warn($"Game {game.Name} has null FeatureIds. Initializing FeatureIds collection.");
                                    game.FeatureIds = new List<Guid>();
                                }

                                if (!game.FeatureIds.Contains(romTranslationFeature.Id))
                                {
                                    logger.Info($"Adding Rom Translation feature to game {game.Name}");
                                    game.FeatureIds.Add(romTranslationFeature.Id);
                                    PlayniteApi.Database.Games.Update(game);
                                    logger.Info($"Added Rom Translation feature to game {game.Name}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Error($"Error processing game action for game: {game.Name}, Action: {action.Name}, Exception: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error checking for ROM translations for platform: {platform}, Exception: {ex.Message}");
            }
        }

        private bool IsValidGameLink(string href, string text)
        {
            return (href.EndsWith(".zip") || href.EndsWith(".wua") || href.EndsWith(".chd") || href.EndsWith(".7z")) && !text.Equals("↓", StringComparison.OrdinalIgnoreCase);
        }
                
        private string GetActionName(string fullName, string baseGameName)
        {
            return fullName.Replace(baseGameName, "").TrimStart(new char[] { ' ', '-', '_' });
        }

        private bool IsDuplicate(GameMetadata gameMetadata)
        {
            return PlayniteApi.Database.Games.Any(existingGame =>
                existingGame.PluginId == Id &&
                existingGame.Name.Equals(gameMetadata.Name, StringComparison.OrdinalIgnoreCase) &&
                existingGame.Platforms.Any(platform => platform.SpecificationId == ((MetadataSpecProperty)gameMetadata.Platforms.First()).Id));
        }
               
        private async Task<List<GameMetadata>> ScrapeAllUrlsForPlatform(string platform)
        {
            var tasks = platformUrls[platform].Select(url => ScrapeSite(url, platform));
            var results = await Task.WhenAll(tasks);
            return results.SelectMany(result => result).ToList();
        }
    }
        }
