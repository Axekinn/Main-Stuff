using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Drawing;
using System.Windows.Media;
using System.Threading;
using static System.Net.WebRequestMethods;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Xml;
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

        // Base URLs for each scraper
        private static readonly string steamripBaseUrl = "https://steamrip.com/games-list-page/";
        private static readonly string ankerBaseUrl = "https://ankergames.net/games-list";
        private static readonly string magipackBaseUrl = "https://www.magipack.games/games-list/";
        private static readonly string ElamigosBaseUrl = "https://elamigos.site/";
        private static readonly string fitgirlBaseUrl = "https://fitgirl-repacks.site/all-my-repacks-a-z/?lcp_page0=";
        private static readonly Regex fallbackRegex = new Regex(@"https://fitgirl-repacks\.site/([^/]+)/?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly string Sony_PS2_Games_BaseUrl = "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation%202/";
        private static readonly string Sony_PS1_Games_BaseUrl = "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation/";
        private static readonly string Nintendo_WII_Games_BaseUrl = "https://myrient.erista.me/files/Redump/Nintendo%20-%20Wii%20-%20NKit%20RVZ%20[zstd-19-128k]/";
        private static readonly string Nintendo64_Games_BaseUrl = "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%2064%20(BigEndian)/";
        private const string Xbox360_Games_BaseUrl = "https://myrient.erista.me/files/Redump/Microsoft%20-%20Xbox%20360/";
        private const string Xbox360Digital_Games_BaseUrl = "https://myrient.erista.me/files/No-Intro/Microsoft%20-%20Xbox%20360%20(Digital)/";





        // Nintendo64_Games_BaseUrl
        public GameStore(IPlayniteAPI api) : base(api)
        {


        }

               

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
                // ------------------- LOCAL UPDATE SECTION -------------------
                
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
                    logger.Info($"Total exclusions loaded: {excludedCount}");
                }

            // Build a dictionary of all PC games for O(1) lookup by normalized name, keeping only the first occurrence if duplicates exist
            var pcGames = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id
                    && g.Platforms != null
                    && g.Platforms.Any(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)))
                .GroupBy(g => NormalizeGameName(ConvertHyphenToColon(CleanGameName(SanitizePath(g.Name)))), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Thread-safe collection for local games
            var localGames = new ConcurrentBag<GameMetadata>();

            // Parallel scan of "Games" folders across all drives
            Parallel.ForEach(DriveInfo.GetDrives().Where(d => d.IsReady), drive =>
            {
                string gamesFolderPath = Path.Combine(drive.RootDirectory.FullName, "Games");
                if (!Directory.Exists(gamesFolderPath))
                    return;

                Parallel.ForEach(Directory.GetDirectories(gamesFolderPath), folder =>
                {
                    if (!Directory.Exists(folder))
                    {
                        logger.Warn($"Folder not found (skipped): {folder}");
                        return;
                    }

                    string folderName = Path.GetFileName(folder);
                    string gameNameLocal = NormalizeGameName(ConvertHyphenToColon(CleanGameName(SanitizePath(folderName))));

                    pcGames.TryGetValue(gameNameLocal, out var existingGame);

                    // Find version files (*.txt matching ^v[version])
                    string[] versionFiles = Array.Empty<string>();
                    try
                    {
                        versionFiles = Directory.GetFiles(folder, "*.txt")
                                                .AsParallel()
                                                .Where(file => Regex.IsMatch(Path.GetFileNameWithoutExtension(file), @"^v\d+(\.\d+)*$"))
                                                .ToArray();
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"Failed to get version files for folder '{folder}': {ex.Message}");
                        return;
                    }

                    // Find valid EXEs, tracking exclusions
                    string[] exeFiles = Array.Empty<string>();
                    var excludedFiles = new ConcurrentBag<string>();
                    try
                    {
                        exeFiles = Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories)
                                             .AsParallel()
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
                                             })
                                             .ToArray();
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"Failed to get exe files for folder '{folder}': {ex.Message}");
                        return;
                    }

                    if (!excludedFiles.IsEmpty)
                    {
                        logger.Info($"Excluded EXE files in '{folder}': {string.Join(", ", excludedFiles)}");
                    }

                    if (existingGame != null)
                    {
                        lock (existingGame) // Thread-safe update of existing game
                        {
                            if (string.IsNullOrWhiteSpace(existingGame.InstallDirectory) || !Directory.Exists(existingGame.InstallDirectory))
                            {
                                existingGame.InstallDirectory = folder;
                            }
                            existingGame.IsInstalled = true;

                            // Preserve all "Download:" actions while adding new play actions
                            var downloadActions = existingGame.GameActions
                                .Where(a => a.Name.StartsWith("Download:", StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            foreach (var exe in exeFiles)
                            {
                                if (!existingGame.GameActions.Any(action => action.Path.Equals(exe, StringComparison.OrdinalIgnoreCase)))
                                {
                                    existingGame.GameActions.Add(new GameAction()
                                    {
                                        Type = GameActionType.File,
                                        Path = exe,
                                        Name = Path.GetFileNameWithoutExtension(exe),
                                        IsPlayAction = true,
                                        WorkingDir = folder
                                    });
                                    logger.Info($"Added new play action '{Path.GetFileName(exe)}' for game '{existingGame.Name}'");
                                }
                            }

                            foreach (var action in downloadActions)
                            {
                                if (!existingGame.GameActions.Contains(action))
                                {
                                    existingGame.GameActions.Add(action);
                                }
                            }

                            if (versionFiles.Any())
                            {
                                string localVersion = Path.GetFileNameWithoutExtension(versionFiles.First());
                                existingGame.Version = localVersion;
                            }

                            PlayniteApi.Database.Games.Update(existingGame);
                            logger.Info($"Updated install status for {existingGame.Name}: Installed = {existingGame.IsInstalled}");
                        }
                    }
                    else
                    {
                        if (!exeFiles.Any())
                            return;

                        var gameMetadata = new GameMetadata()
                        {
                            Name = gameNameLocal,
                            GameId = gameNameLocal.ToLower(),
                            Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                            GameActions = new List<GameAction>(),
                            IsInstalled = true,
                            InstallDirectory = folder,
                            Icon = new MetadataFile(Path.Combine(folder, "icon.png")),
                            BackgroundImage = new MetadataFile(Path.Combine(folder, "background.png")),
                            Version = ExtractVersionNumber(folderName)
                        };

                        if (versionFiles.Any())
                        {
                            string localVersion = Path.GetFileNameWithoutExtension(versionFiles.First());
                            gameMetadata.Version = localVersion;
                        }

                        foreach (var exe in exeFiles)
                        {
                            gameMetadata.GameActions.Add(new GameAction()
                            {
                                Type = GameActionType.File,
                                Path = exe,
                                Name = Path.GetFileNameWithoutExtension(exe),
                                IsPlayAction = true,
                                WorkingDir = folder
                            });
                            logger.Info($"Added new play action '{Path.GetFileName(exe)}' for new game '{gameNameLocal}'");
                        }

                        localGames.Add(gameMetadata);
                    }
                });
            });

            // Convert concurrent bag to list for later use
            var localGamesList = localGames.ToList();

            // Pre-index all Repacks games for O(1) lookup by normalized name, using only the first found if duplicates exist
            var repackGames = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id)
                .GroupBy(g => ConvertHyphenToColon(CleanGameName(SanitizePath(g.Name))), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Thread-safe collection for repack games to add
            var repackGameMetas = new ConcurrentBag<GameMetadata>();

            // Parallel scan of "Repacks" folders across all drives
            Parallel.ForEach(DriveInfo.GetDrives().Where(d => d.IsReady), drive =>
            {
                string repacksFolderPath = Path.Combine(drive.RootDirectory.FullName, "Repacks");
                if (!Directory.Exists(repacksFolderPath))
                    return;

                Parallel.ForEach(Directory.GetDirectories(repacksFolderPath), folder =>
                {
                    string folderName = Path.GetFileName(folder);
                    string gameNameLocal = ConvertHyphenToColon(CleanGameName(SanitizePath(folderName)));

                    repackGames.TryGetValue(gameNameLocal, out var existingGame);

                    if (existingGame != null)
                    {
                        AddInstallReadyFeature(existingGame);
                        PlayniteApi.Database.Games.Update(existingGame);
                        logger.Info($"Updated repack game: {existingGame.Name} | Install Directory: {existingGame.InstallDirectory}");
                    }
                    else
                    {
                        var gameMetadata = new GameMetadata
                        {
                            Name = gameNameLocal,
                            GameId = gameNameLocal.ToLower(),
                            Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                            GameActions = new List<GameAction>(),
                            IsInstalled = false,
                            InstallDirectory = null,
                            Icon = new MetadataFile(Path.Combine(folder, "icon.png")),
                            BackgroundImage = new MetadataFile(Path.Combine(folder, "background.png")),
                            Version = ExtractVersionNumber(folderName)
                        };

                        AddInstallReadyFeature(gameMetadata);
                        repackGameMetas.Add(gameMetadata);
                        logger.Info($"Added new repack game: {gameMetadata.Name} | Expected Install Directory: {gameMetadata.InstallDirectory}");
                    }
                });
            });
            // ------------------- ONLINE SCRAPE SECTION -------------------
            var allGames = new List<GameMetadata>();
            allGames.AddRange(localGamesList); // localGamesList likely from your local PC scan section
            allGames.AddRange(repackGameMetas); // add repack games collected above

            // Deduplication by normalized name
            var existingNormalizedFromDB = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id)
                .Select(g => NormalizeGameName(g.Name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Helper for adding online games
            void AddScraped(IEnumerable<GameMetadata> entries, string sourceName, Func<string, bool> duplicateCheck = null, Action<GameMetadata, string, string> additionalMerge = null)
            {
                foreach (var game in entries)
                {
                    string gameName = game.Name;
                    string normalizedKey = NormalizeGameName(gameName);

                    if (existingNormalizedFromDB.Contains(normalizedKey) || (duplicateCheck != null && duplicateCheck(gameName)))
                        continue;

                    additionalMerge?.Invoke(game, gameName, normalizedKey);

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
                    existingNormalizedFromDB.Add(normalizedKey);
                }
            }

            // Add online-scraped games (deduped)
            AddScraped(ScrapeSite().GetAwaiter().GetResult(), "SteamRip");
            AddScraped(AnkerScrapeGames().GetAwaiter().GetResult(), "AnkerGames");
            AddScraped(MagipackScrapeGames().GetAwaiter().GetResult(), "Magipack", gameName => MagipackIsDuplicate(gameName));
            AddScraped(ElamigosScrapeGames().GetAwaiter().GetResult(), "Elamigos", gameName => ElamigosIsDuplicate(gameName),
                (game, gameName, normalizedKey) =>
                {
                    if (!game.Name.Contains(":") && gameName.Contains(":"))
                    {
                        game.Name = gameName;
                        game.GameId = normalizedKey.ToLower();
                    }
                });
            AddScraped(FitGirlScrapeGames().GetAwaiter().GetResult(), "Fitgirl", gameName => FitGirlIsDuplicate(gameName));

            // ------------------- PS1/PS2 SECTION -------------------
            // PS1
            var ps1Roms = PS1_FindGameRoms("");
            foreach (var game in Myrient_Sony_PS1_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);
                if (existingNormalizedFromDB.Contains(norm)) continue;
                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = ps1Roms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // -- Emulator Play Action --
                    var duckStation = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("DuckStation", StringComparison.OrdinalIgnoreCase));
                    if (duckStation != null && duckStation.BuiltinProfiles != null && duckStation.BuiltinProfiles.Any())
                    {
                        var profile = duckStation.BuiltinProfiles.First(); // or select by name if you want
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
                existingNormalizedFromDB.Add(norm);
            }

            // Wii
            var WIIRoms = FindWIIGameRoms("");
            foreach (var game in Myrient_Nintendo_WII_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Ensure existing game check uses Plugin ID & Platform logic
                if (existingNormalizedFromDB.Contains(norm) || PlayniteApi.Database.Games.Any(g =>
                        g.PluginId == Id &&
                        g.Platforms != null && g.Platforms.Any(p => p.Name.Equals("Nintendo Wii", StringComparison.OrdinalIgnoreCase)) &&
                        g.Name.Equals(norm, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = WIIRoms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // -- Emulator Play Action --
                    var dolphin = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("Dolphin", StringComparison.OrdinalIgnoreCase));
                    if (dolphin != null && dolphin.BuiltinProfiles != null && dolphin.BuiltinProfiles.Any())
                    {
                        var profile = dolphin.BuiltinProfiles.First();
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
                existingNormalizedFromDB.Add(norm);
            }


            // PS2
            var ps2Roms = Myrient_FindGameRoms("");
            foreach (var game in Myrient_Sony_PS2_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Ensure existing game check uses Plugin ID & Platform logic
                if (existingNormalizedFromDB.Contains(norm) || PlayniteApi.Database.Games.Any(g =>
                        g.PluginId == Id &&
                        g.Platforms != null && g.Platforms.Any(p => p.Name.Equals("Sony PlayStation 2", StringComparison.OrdinalIgnoreCase)) &&
                        g.Name.Equals(norm, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = ps2Roms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // -- Emulator Play Action --
                    var pcsx2 = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("PCSX2", StringComparison.OrdinalIgnoreCase));
                    if (pcsx2 != null && pcsx2.BuiltinProfiles != null && pcsx2.BuiltinProfiles.Any())
                    {
                        // Try "Default QT" if it exists, else first profile
                        var profile = pcsx2.BuiltinProfiles.FirstOrDefault(p => p.Name.Equals("Default QT", StringComparison.OrdinalIgnoreCase));
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
                existingNormalizedFromDB.Add(norm);
            }



            // Nintendo 64
            var N64Roms = FindN64GameRoms("");
            foreach (var game in Myrient_Nintendo64_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);

                // Ensure existing game check uses Plugin ID & Platform logic
                if (existingNormalizedFromDB.Contains(norm) || PlayniteApi.Database.Games.Any(g =>
                        g.PluginId == Id &&
                        g.Platforms != null && g.Platforms.Any(p => p.Name.Equals("Nintendo 64", StringComparison.OrdinalIgnoreCase)) &&
                        g.Name.Equals(norm, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = N64Roms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // -- Emulator Play Action --
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
                existingNormalizedFromDB.Add(norm);
            }


            // 1. Gather all Xbox 360 ROMs with supported extensions
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
                catch (Exception ex)
                {
                    logger.Error($"Error searching Xbox 360 ROMs in drive {drive.Name}: {ex.Message}");
                }
            }

            // 2. Prepare log file path
            var logPath = Path.Combine(PlayniteApi.Paths.ConfigurationPath ?? "", "Xbox 360.txt");
            File.WriteAllText(logPath, "=== Xbox 360 ROMs Found ===\n");
            foreach (var rom in xbox360Roms)
            {
                var romNorm = Myrient_NormalizeGameName(Path.GetFileNameWithoutExtension(rom));
                File.AppendAllText(logPath, $"{rom}\n    Normalized: {romNorm}\n");
            }
            File.AppendAllText(logPath, "\n=== Xbox 360 Games Scanned ===\n");

            // 3. Prevent duplicate processing in this run
            var processedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 4. For all scraped games, update install state if ROM matched
            foreach (var game in Myrient_Xbox360_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);
                string platformName = "Microsoft Xbox 360";
                string uniqueKey = $"{norm}|{platformName}";

                if (!processedKeys.Add(uniqueKey))
                    continue;

                var matchingRoms = xbox360Roms
                    .Where(r => Myrient_NormalizeGameName(Path.GetFileNameWithoutExtension(r)).Equals(norm, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                File.AppendAllText(logPath, $"Game: {game.Name}\n  Normalized: {norm}\n");

                if (matchingRoms.Any())
                {
                    File.AppendAllText(logPath, $"  MATCHED ROMS:\n");
                    foreach (var rom in matchingRoms)
                        File.AppendAllText(logPath, $"    {rom}\n");

                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // Add (or update) emulator play action for Xenia
                    var xenia = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("Xenia", StringComparison.OrdinalIgnoreCase));
                    if (xenia != null && xenia.BuiltinProfiles != null && xenia.BuiltinProfiles.Any())
                    {
                        var profile = xenia.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();

                        // Remove old emulator actions to avoid duplicates
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
                    File.AppendAllText(logPath, $"  No matching ROM found.\n");
                }

                allGames.Add(game);
            }

            // Find all Xbox 360 Digital ROMs once
            var Xbox360DigitalRoms = FindXbox360DigitalGameRoms("");

            // Build a hash set of normalized name + platform for fast O(1) existence check
            var dbGameKeys_Xbox360Digital = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id && g.Platforms != null)
                    .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

            // Local set to prevent duplicate adds in this run
            var processedKeys_Xbox360Digital = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var game in Myrient_Xbox360Digital_ScrapeStaticPage().GetAwaiter().GetResult())
            {
                string norm = Myrient_NormalizeGameName(game.Name);
                string platformName = "Microsoft Xbox 360";
                string uniqueKey = $"{norm}|{platformName}";

                // Fast skip if exists in DB or already processed this run
                if (dbGameKeys_Xbox360Digital.Contains(uniqueKey) || processedKeys_Xbox360Digital.Contains(uniqueKey))
                    continue;
                processedKeys_Xbox360Digital.Add(uniqueKey);

                var cleaned = Myrient_CleanNameForMatching(game.Name);
                var matchingRoms = Xbox360DigitalRoms.Where(r =>
                    Myrient_CleanNameForMatching(Path.GetFileNameWithoutExtension(r)).Equals(cleaned, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingRoms.Any())
                {
                    game.IsInstalled = true;
                    game.InstallDirectory = Path.GetDirectoryName(matchingRoms.First());
                    game.Roms = matchingRoms.Select(r => new GameRom { Name = Path.GetFileName(r), Path = r }).ToList();

                    // -- Emulator Play Action --
                    var xenia = PlayniteApi.Database.Emulators.FirstOrDefault(e => e.Name.Equals("Xenia", StringComparison.OrdinalIgnoreCase));
                    if (xenia != null && xenia.BuiltinProfiles != null && xenia.BuiltinProfiles.Any())
                    {
                        var profile = xenia.BuiltinProfiles.First();
                        if (game.GameActions == null)
                            game.GameActions = new List<GameAction>();

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
                allGames.Add(game);
            }




            return allGames;




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

       

        private void AddInstallReadyFeature(Game existingGame)
        {
            if (existingGame == null)
                return;

            var installReadyFeature = PlayniteApi.Database.Features
                .FirstOrDefault(f => f.Name.Equals("[Install Ready]", StringComparison.OrdinalIgnoreCase));
            if (installReadyFeature == null)
            {
                installReadyFeature = new GameFeature("[Install Ready]");
                PlayniteApi.Database.Features.Add(installReadyFeature);
            }

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

            var installReadyFeature = PlayniteApi.Database.Features
                .FirstOrDefault(f => f.Name.Equals("[Install Ready]", StringComparison.OrdinalIgnoreCase));
            if (installReadyFeature == null)
            {
                installReadyFeature = new GameFeature("[Install Ready]");
                PlayniteApi.Database.Features.Add(installReadyFeature);
            }

            if (newGame.Features == null)
                newGame.Features = new HashSet<MetadataProperty>();

            bool featureExists = newGame.Features
                .OfType<MetadataSpecProperty>()
                .Any(f => f.Id == installReadyFeature.Id.ToString());

            if (!featureExists && installReadyFeature.Id != Guid.Empty)
            {
                newGame.Features.Add(new MetadataSpecProperty(installReadyFeature.Id.ToString()));
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

        private async Task<List<GameMetadata>> ScrapeSite()
        {
            // Build a hash set for fast O(1) lookup of existing DB games by normalized name,
            // but ONLY include those for "PC (Windows)" platform AND having "Download: SteamRip" action.
            var dbGameKeys = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.Platforms.Any(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase))
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals("Download: SteamRip", StringComparison.OrdinalIgnoreCase)))
                    .Select(g => NormalizeGameName(g.Name)),
                StringComparer.OrdinalIgnoreCase);

            // URL to scrape.
            string url = steamripBaseUrl;
            logger.Info($"Scraping: {url}");

            string pageContent = await LoadPageContent(url);
            if (string.IsNullOrWhiteSpace(pageContent))
            {
                logger.Warn($"No content retrieved from {url}");
                return new List<GameMetadata>();
            }

            var links = ParseLinks(pageContent);
            if (links == null || links.Count == 0)
            {
                logger.Info($"No links found on {url}");
                return new List<GameMetadata>();
            }

            // Use PLINQ to process links in parallel, producing an intermediate list.
            var scrapedItems = links.AsParallel()
                .Select(link =>
                {
                    string href = link.Item1;
                    string text = link.Item2;

                    // Validate link values.
                    if (string.IsNullOrWhiteSpace(href) ||
                        string.IsNullOrWhiteSpace(text) ||
                        !IsValidGameLink(href, text))
                        return (dynamic)null;

                    // Prepend domain if href is relative.
                    if (href.StartsWith("/"))
                        href = $"https://steamrip.com{href}";

                    string version = ExtractVersionNumber(text);
                    string cleanName = CleanGameName(text);
                    if (string.IsNullOrEmpty(cleanName))
                        return (dynamic)null;

                    string normalizedKey = NormalizeGameName(cleanName);
                    return new { NormalizedKey = normalizedKey, CleanName = cleanName, Href = href, Version = version };
                })
                .Where(item => item != null)
                .ToList();

            // Group scraped items by normalized key.
            var groupedItems = scrapedItems.GroupBy(x => x.NormalizedKey).ToList();

            // Merge grouped items into GameMetadata objects in parallel.
            var mergedScrapedGames = groupedItems.AsParallel()
                .Select(group =>
                {
                    var first = group.First();
                    // Merge all distinct download links.
                    var downloadHrefs = group.Select(x => (string)x.Href).Distinct().ToList();
                    var downloadActions = downloadHrefs.Select(href => new GameAction
                    {
                        Name = "Download: SteamRip",
                        Type = GameActionType.URL,
                        Path = href,
                        IsPlayAction = false
                    }).ToList();

                    return new GameMetadata
                    {
                        Name = first.CleanName,
                        GameId = group.Key.ToLower(),
                        Platforms = new HashSet<MetadataProperty>
                        {
                    new MetadataSpecProperty("PC (Windows)")
                        },
                        GameActions = downloadActions,
                        Version = first.Version,
                        IsInstalled = false
                    };
                })
                .ToList();

            // Final list: only include games NOT already in DB with matching platform and action.
            var finalScrapedGames = mergedScrapedGames
                .Where(game => !dbGameKeys.Contains(game.GameId))
                .ToList();

            return finalScrapedGames;
        }
        // Example: Include this code in your class

        private async Task<List<GameMetadata>> AnkerScrapeGames()
        {
            const string downloadActionName = "Download: AnkerGames";

            // Fast O(1) lookup: Only DB games with PC (Windows) platform AND Download: AnkerGames action
            var dbGameKeys = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.Platforms.Any(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase))
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .Select(g => NormalizeGameName(g.Name)),
                StringComparer.OrdinalIgnoreCase);

            // Use a concurrent dictionary for newly scraped games.
            var scrapedGames = new ConcurrentDictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

            try
            {
                logger.Info($"Scraping games from: {ankerBaseUrl}");

                // Load the main page content.
                string mainPageContent = await AnkerLoadPageContent(ankerBaseUrl).ConfigureAwait(false);
                if (string.IsNullOrEmpty(mainPageContent))
                {
                    logger.Warn("Failed to retrieve main page content from AnkerGames.");
                    return scrapedGames.Values.ToList();
                }
                logger.Info("Main page content retrieved successfully.");

                // Extract game links from the main page.
                var allLinks = new List<string>(AnkerExtractGameLinks(mainPageContent));
                logger.Info($"Initial page: found {allLinks.Count} game links.");

                // Simulate clicking "Load More" until no new links are returned.
                while (true)
                {
                    string moreContent = await AnkerLoadMoreContent().ConfigureAwait(false);
                    if (string.IsNullOrEmpty(moreContent))
                    {
                        logger.Info("No additional content returned from Load More.");
                        break;
                    }

                    var newLinks = AnkerExtractGameLinks(moreContent);
                    if (newLinks.Count == 0)
                    {
                        logger.Info("No new game links found on Load More content.");
                        break;
                    }

                    int prevCount = allLinks.Count;
                    allLinks.AddRange(newLinks.Except(allLinks));
                    logger.Info($"Load More: added {allLinks.Count - prevCount} new links (total: {allLinks.Count}).");
                    if (allLinks.Count == prevCount)
                        break;
                }

                logger.Info($"Total unique game links to process: {allLinks.Count}");

                // Process each game link concurrently.
                int maxConcurrency = 20;
                using (var semaphore = new SemaphoreSlim(maxConcurrency))
                {
                    var tasks = allLinks.Select(async link =>
                    {
                        await semaphore.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            // Fetch individual game page content.
                            string gamePageContent = await AnkerLoadPageContent(link).ConfigureAwait(false);
                            if (string.IsNullOrWhiteSpace(gamePageContent))
                            {
                                logger.Warn($"Failed to retrieve content for link: {link}");
                                return;
                            }

                            // Extract and decode the game name.
                            string rawGameName = AnkerExtractGameNameFromPage(gamePageContent);
                            if (string.IsNullOrWhiteSpace(rawGameName))
                            {
                                logger.Warn($"Could not extract game name from page: {link}");
                                return;
                            }
                            string gameName = WebUtility.HtmlDecode(rawGameName);
                            string normalizedKey = NormalizeGameName(gameName);

                            // O(1) skip if the game already exists in Playnite DB for PC (Windows) and has Download: AnkerGames action
                            if (dbGameKeys.Contains(normalizedKey))
                                return;

                            // Merge the new game into scrapedGames.
                            scrapedGames.AddOrUpdate(
                                normalizedKey,
                                key =>
                                {
                                    string sanitizedGameName = AnkerSanitizePath(gameName);
                                    var newGame = new GameMetadata
                                    {
                                        Name = gameName,
                                        GameId = key.ToLower(),
                                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                                        GameActions = new List<GameAction>
                                        {
                                    new GameAction
                                    {
                                        Name = downloadActionName,
                                        Type = GameActionType.URL,
                                        Path = link,
                                        IsPlayAction = false
                                    }
                                        },
                                        IsInstalled = false,
                                        InstallDirectory = null,
                                        Icon = new MetadataFile(Path.Combine(sanitizedGameName, "icon.png")),
                                        BackgroundImage = new MetadataFile(Path.Combine(sanitizedGameName, "background.png"))
                                    };
                                    logger.Info($"Added new game entry: {gameName}");
                                    return newGame;
                                },
                                (key, existingGame) =>
                                {
                                    lock (existingGame.GameActions)
                                    {
                                        if (!existingGame.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                                        {
                                            existingGame.GameActions.Add(new GameAction
                                            {
                                                Name = downloadActionName,
                                                Type = GameActionType.URL,
                                                Path = link,
                                                IsPlayAction = false
                                            });
                                            logger.Info($"Added new download action for scraped game: {gameName}");
                                        }
                                    }
                                    return existingGame;
                                });
                        }
                        catch (Exception ex)
                        {
                            logger.Error($"Error processing AnkerGames link {link}: {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }).ToArray();

                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                logger.Info($"AnkerGames scraping completed. Total new games added: {scrapedGames.Count}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error during AnkerGames scraping: {ex.Message}");
            }

            return scrapedGames.Values.ToList();
        }
        private async Task<string> AnkerLoadMoreContent()
        {
            // Construct the URL for "Load More". Adjust this based on how AnkerGames loads additional games.
            string loadMoreUrl = $"{ankerBaseUrl}?loadmore=true";
            return await AnkerLoadPageContent(loadMoreUrl).ConfigureAwait(false);
        }

        private List<string> AnkerExtractGameLinks(string pageContent)
        {
            var links = new List<string>();
            // Use regex to extract game links that match the AnkerGames pattern
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

        private async Task<List<GameMetadata>> MagipackScrapeGames()
        {
            const string downloadActionName = "Download: Magipack";
            // O(1) lookup: Only DB games with PC (Windows) platform AND Download: Magipack action
            var dbGameKeys = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.Platforms.Any(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase))
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .Select(g => NormalizeGameName(g.Name)),
                StringComparer.OrdinalIgnoreCase);

            var scrapedGames = new ConcurrentDictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

            logger.Info($"Scraping games from: {magipackBaseUrl}");

            // Fetch the main page content.
            string pageContent = await LoadPageContent(magipackBaseUrl).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pageContent))
            {
                logger.Warn("Failed to retrieve main page content from Magipack.");
                return scrapedGames.Values.ToList();
            }
            logger.Info("Main page content retrieved successfully.");

            // Extract game links using your parsing method.
            var links = ParseLinks(pageContent);
            if (links == null || links.Count == 0)
            {
                logger.Info("No game links found on Magipack page.");
                return scrapedGames.Values.ToList();
            }
            logger.Info($"Found {links.Count} potential game links.");

            // Process each link in parallel.
            Parallel.ForEach(links, link =>
            {
                string href = link.Item1;
                string text = link.Item2;

                // Skip if either href or text is missing or invalid.
                if (string.IsNullOrWhiteSpace(href) ||
                    string.IsNullOrWhiteSpace(text) ||
                    !IsValidGameLink(href))
                {
                    return;
                }

                // Clean up the game title.
                string cleanName = CleanGameName(text);
                if (string.IsNullOrEmpty(cleanName))
                {
                    cleanName = fallbackRegex.Replace(href, "$1").Replace('-', ' ').Trim();
                }
                if (string.IsNullOrEmpty(cleanName))
                {
                    return;
                }

                // Generate normalized key for duplicate checking.
                string normalizedKey = NormalizeGameName(cleanName);

                // O(1) skip if game already in DB for PC (Windows) with Download: Magipack action
                if (dbGameKeys.Contains(normalizedKey))
                    return;

                // Otherwise, add or update the scraped game entry.
                scrapedGames.AddOrUpdate(
                    normalizedKey,
                    key =>
                    {
                        // Create new game metadata.
                        var gameMetadata = new GameMetadata
                        {
                            Name = cleanName,
                            GameId = key.ToLower(),
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
                    (key, existingGame) =>
                    {
                        // Update existing entry if download action is missing.
                        if (!existingGame.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
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
                            }
                            logger.Info($"Added download action to duplicate scraped game: {cleanName}");
                        }
                        return existingGame;
                    });
            });

            logger.Info($"Magipack scraping completed. New games added: {scrapedGames.Count}");
            return scrapedGames.Values.ToList();
        }
        private async Task<List<GameMetadata>> ElamigosScrapeGames()
        {
            const string downloadActionName = "Download: elAmigos";
            // O(1) lookup: Only DB games with PC (Windows) platform AND Download: elAmigos action
            var dbGameKeys = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.Platforms.Any(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase))
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .Select(g => NormalizeGameName(g.Name)),
                StringComparer.OrdinalIgnoreCase);

            // Dictionary for new (scraped) games.
            var scrapedGames = new Dictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

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

            // Lock object to protect scrapedGames dictionary.
            object scrapedLock = new object();

            // Process each match in parallel.
            Parallel.ForEach(matches.Cast<Match>(), match =>
            {
                // Extract raw title and download link.
                string rawName = match.Groups[1].Value.Trim();
                string href = match.Groups[2].Value.Trim();

                // If href is relative, prepend the base URL.
                if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    href = ElamigosBaseUrl.TrimEnd('/') + "/" + href.TrimStart('/');
                }

                // Remove "ElAmigos" from the raw title.
                rawName = removeElamigosRegex.Replace(rawName, "").Trim();

                // Clean the game title.
                string cleanName = CleanGameName(rawName);

                // Process slashes: keep only the text before the first slash.
                int slashIndex = cleanName.IndexOf('/');
                if (slashIndex > 0)
                {
                    cleanName = cleanName.Substring(0, slashIndex).Trim();
                }

                // Process commas: if the text after the comma looks extraneous (e.g., a file size), remove it.
                int commaIndex = cleanName.IndexOf(',');
                if (commaIndex > 0)
                {
                    string afterComma = cleanName.Substring(commaIndex + 1).Trim();
                    if (extraneousInfoRegex.IsMatch(afterComma))
                    {
                        cleanName = cleanName.Substring(0, commaIndex).Trim();
                    }
                }

                // Validate the cleaned title and download link.
                if (string.IsNullOrWhiteSpace(cleanName) || !IsValidGameLink(href))
                    return;

                string displayName = cleanName;
                string normalizedName = NormalizeGameName(cleanName);

                // O(1) skip if game already in DB for PC (Windows) with Download: elAmigos action
                if (dbGameKeys.Contains(normalizedName))
                    return;

                // Update the scrapedGames dictionary.
                lock (scrapedLock)
                {
                    if (!scrapedGames.ContainsKey(normalizedName))
                    {
                        // Create a new game entry.
                        var gameMetadata = new GameMetadata
                        {
                            Name = displayName,
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
                        scrapedGames.Add(normalizedName, gameMetadata);
                    }
                    else
                    {
                        // Duplicate found among the scraped games; add the action if it does not exist.
                        var existingGame = scrapedGames[normalizedName];
                        if (!existingGame.GameActions.Any(a =>
                               a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
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
                }
            });

            logger.Info($"ElAmigos scraping completed. New games added: {scrapedGames.Count}");
            return scrapedGames.Values.ToList();
        }
        private async Task<List<GameMetadata>> FitGirlScrapeGames()
        {
            const string downloadActionName = "Download: FitGirl Repacks";
            // O(1) lookup: Only DB games with PC (Windows) platform AND Download: FitGirl Repacks action
            var dbGameKeys = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.Platforms.Any(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase))
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                    .Select(g => NormalizeGameName(g.Name)),
                StringComparer.OrdinalIgnoreCase);

            // Use a concurrent dictionary for new (scraped) games.
            var scrapedGames = new ConcurrentDictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

            // Get the latest page number.
            int latestPage = await GetLatestPageNumber().ConfigureAwait(false);
            logger.Info($"Latest FitGirl page: {latestPage}");

            // Create tasks to scrape each page concurrently.
            var tasks = new List<Task>();
            for (int page = 1; page <= latestPage; page++)
            {
                int currentPage = page; // local variable for closure
                tasks.Add(Task.Run(async () =>
                {
                    // Build the page URL.
                    string url = $"{fitgirlBaseUrl}{currentPage}#lcp_instance_0";

                    // Get page content.
                    string pageContent = await LoadPageContent(url).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(pageContent))
                    {
                        logger.Warn($"No content returned for page {currentPage}, skipping.");
                        return;
                    }

                    // Extract game links and titles.
                    var links = ParseLinks(pageContent);
                    if (links == null || links.Count == 0)
                    {
                        logger.Info($"No game links found on page {currentPage}, skipping.");
                        return;
                    }

                    // Process each game link on the page in parallel.
                    Parallel.ForEach(links, link =>
                    {
                        string href = link.Item1;
                        string text = link.Item2;

                        // Quick checks.
                        if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(text) || !IsValidGameLink(href))
                            return;

                        // Skip internal pagination links.
                        if (href.Contains("page0="))
                            return;

                        // Clean the game title (no fallback is applied).
                        string cleanName = CleanGameName(text);
                        if (string.IsNullOrEmpty(cleanName))
                            return;

                        // Generate a normalized key.
                        string normalizedName = NormalizeGameName(cleanName);

                        // O(1) skip if game already in DB for PC (Windows) with Download: FitGirl Repacks action
                        if (dbGameKeys.Contains(normalizedName))
                            return;

                        // Otherwise, process as a new (scraped) game.
                        // Use AddOrUpdate to handle duplicates gracefully.
                        scrapedGames.AddOrUpdate(
                            normalizedName,
                            key =>
                            {
                                // Create a new game entry.
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
                            (key, existingGame) =>
                            {
                                // Duplicate among scraped games; add the action if it doesn't exist.
                                if (!existingGame.GameActions.Any(a => a.Name.Equals(downloadActionName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    existingGame.GameActions.Add(new GameAction
                                    {
                                        Name = downloadActionName,
                                        Type = GameActionType.URL,
                                        Path = href,
                                        IsPlayAction = false
                                    });
                                    logger.Info($"Added download action to duplicate scraped game: {cleanName}");
                                }
                                return existingGame;
                            });
                    });
                }));
            }

            // Wait for all page tasks to complete.
            await Task.WhenAll(tasks).ConfigureAwait(false);

            logger.Info($"FitGirl scraping completed. Total new games added: {scrapedGames.Count}");
            return scrapedGames.Values.ToList();
        }
        private async Task<List<GameMetadata>> Myrient_Sony_PS1_ScrapeStaticPage()
        {
            // Build a hash set of normalized names for fast O(1) lookup of existing DB games per platform,
            // ONLY including games that already have a "Download: Myrient" action.
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals("Download: Myrient", StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

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

            // Concurrent collection for storing results
            var results = new ConcurrentBag<GameMetadata>();

            Parallel.ForEach(links, link =>
            {
                string text = link.Item2;
                if (string.IsNullOrWhiteSpace(text))
                    return;

                // Normalize game name to remove regional/version differences
                string cleanName = Myrient_CleanGameName(text).Replace(".zip", "").Trim();
                if (string.IsNullOrEmpty(cleanName))
                    cleanName = fallbackRegex.Replace(text, "$1").Replace('-', ' ').Trim();
                if (string.IsNullOrEmpty(cleanName))
                    return;

                string platformName = "Sony PlayStation";
                string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                // *** O(1) skip if game already present with Download: Myrient action for this platform ***
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    return;

                var metadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = uniqueKey.ToLower(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = "Download: Myrient",
                    Type = GameActionType.URL,
                    Path = Sony_PS1_Games_BaseUrl,
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

                results.Add(metadata);
            });

            logger.Info($"[Sony_PS1_Games] Scraping completed. Total new games added: {results.Count}");
            return results.ToList();
        }
        private async Task<List<GameMetadata>> Myrient_Sony_PS2_ScrapeStaticPage()
        {
            // Build a hash set of normalized names for fast O(1) lookup of existing DB games per platform
            // ONLY include games that already have a "Download: Myrient" action
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals("Download: Myrient", StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

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

            // Concurrent collection for storing results
            var results = new ConcurrentBag<GameMetadata>();

            Parallel.ForEach(links, link =>
            {
                string text = link.Item2;
                if (string.IsNullOrWhiteSpace(text))
                    return;

                // Normalize game name to remove regional/version differences
                string cleanName = Myrient_CleanGameName(text).Replace(".zip", "").Trim();
                if (string.IsNullOrEmpty(cleanName))
                    cleanName = fallbackRegex.Replace(text, "$1").Replace('-', ' ').Trim();
                if (string.IsNullOrEmpty(cleanName))
                    return;

                string platformName = "Sony PlayStation 2";
                string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                // O(1) skip if game already present with Download: Myrient action for this platform
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    return;

                var metadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = uniqueKey.ToLower(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = "Download: Myrient",
                    Type = GameActionType.URL,
                    Path = Sony_PS2_Games_BaseUrl,
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

                results.Add(metadata);
            });

            logger.Info($"[Sony_PS2_Games] Scraping completed. Total new games added: {results.Count}");
            return results.ToList();
        }


        private async Task<List<GameMetadata>> Myrient_Nintendo_WII_ScrapeStaticPage()
        {
            // Build a hash set of existing games (normalized name + platform) with Download: Myrient action
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id && g.Platforms != null &&
                                g.GameActions != null &&
                                g.GameActions.Any(a => a.Name.Equals("Download: Myrient", StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

            // Download and parse page as before
            string pageContent = await Myrient_LoadPageContent(Nintendo_WII_Games_BaseUrl).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pageContent))
                return new List<GameMetadata>();

            var links = Myrient_ParseLinks(pageContent)?
                .Where(link => link.Item1.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (links == null || links.Length == 0)
                return new List<GameMetadata>();

            var results = new ConcurrentBag<GameMetadata>();
            Parallel.ForEach(links, link =>
            {
                string text = link.Item2;
                if (string.IsNullOrWhiteSpace(text))
                    return;

                string cleanName = Myrient_CleanGameName(text).Replace(".zip", "").Trim();
                if (string.IsNullOrEmpty(cleanName))
                    return;

                string platformName = "Nintendo Wii";
                string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                // O(1) skip if already in Playnite with Download: Myrient
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    return;

                // ... build GameMetadata as before and add to results ...
            });

            return results.ToList();
        }


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

        private List<string> FindPS1GameRoms(string gameName)
        {
            var romPaths = new List<string>();
            var searchExtensions = new[] { ".chd", ".iso" };
            var searchDirectory = "Roms\\Sony - PlayStation 1";

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

        private List<string> FindWIIGameRoms(string gameName)
        {
            var romPaths = new List<string>();
            var searchExtensions = new[] { ".rvz", ".iso" };
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

        // N64 part:
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
            // Build a hash set of normalized names for fast O(1) lookup of existing DB games,
            // ONLY including games that already have a "Download: Myrient" action.
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals("Download: Myrient", StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

            // Scrape links in parallel, return new unique GameMetadata only.
            const string Nintendo64_Games_BaseUrl = "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Nintendo%2064%20(BigEndian)/";
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
                    || link.Item1.EndsWith(".v64", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (links == null || links.Length == 0)
            {
                logger.Info("[Nintendo64_Games] No valid game links found.");
                return new List<GameMetadata>();
            }
            logger.Info($"[Nintendo64_Games] Found {links.Length} Nintendo 64 game links.");

            // Result bag for concurrent add.
            var results = new ConcurrentBag<GameMetadata>();

            Parallel.ForEach(links, link =>
            {
                string text = link.Item2;
                if (string.IsNullOrWhiteSpace(text))
                    return;

                // Clean the game name: remove the file extension (.z64, .n64, .v64)
                string rawName = Myrient_CleanGameName(text);
                string cleanName = Regex.Replace(rawName, @"\.(zip|z64|n64|v64)$", "", RegexOptions.IgnoreCase).Trim();

                if (string.IsNullOrEmpty(cleanName))
                    cleanName = fallbackRegex.Replace(text, "$1").Replace('-', ' ').Trim();
                if (string.IsNullOrEmpty(cleanName))
                    return;

                string platformName = "Nintendo 64";
                string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                // O(1) skip if game already present with Download: Myrient action for this platform
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    return;

                var metadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = uniqueKey.ToLower(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = "Download: Myrient",
                    Type = GameActionType.URL,
                    Path = Nintendo64_Games_BaseUrl,
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
                        // Use link.Item1 as a placeholder for the ROM path.
                        metadata.GameActions.Add(new GameAction
                        {
                            Name = "Play",
                            Type = GameActionType.Emulator,
                            EmulatorId = project64.Id,
                            EmulatorProfileId = n64Profile.Id,
                            Path = link.Item1,
                            IsPlayAction = true
                        });
                    }
                }

                results.Add(metadata);
            });

            logger.Info($"[Nintendo64_Games] Scraping completed. Total new games added: {results.Count}");
            return results.ToList();
        }
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
            // Fast O(1) lookup: All normalized names for this pluginId & platform WITH "Download: Myrient" action only
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals("Download: Myrient", StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

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

            var results = new ConcurrentBag<GameMetadata>();

            Parallel.ForEach(links, link =>
            {
                string originalText = link.Item2?.Trim();
                if (string.IsNullOrWhiteSpace(originalText))
                    return;

                // Remove .zip extension and region tags for matching
                string cleanName = originalText;
                if (cleanName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    cleanName = cleanName.Substring(0, cleanName.Length - ".zip".Length);
                // Remove region tags like (USA), (Europe), etc.
                cleanName = System.Text.RegularExpressions.Regex.Replace(cleanName, @"\s*\(.*?\)$", "");
                cleanName = cleanName.Trim();

                // Fallback if name is still empty
                if (string.IsNullOrEmpty(cleanName))
                    cleanName = originalText;

                // Normalize name for matching and deduplication
                string normalizedName = Myrient_NormalizeGameName(cleanName);
                string platformName = "Microsoft Xbox 360";
                string uniqueKey = $"{normalizedName}|{platformName}";

                // Log for debug
                logger.Info($"[Xbox360_Games] Scraped: '{originalText}', cleaned: '{cleanName}', normalized: '{normalizedName}'");

                // O(1) skip if the game exists in the Playnite DB with "Download: Myrient" action
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    return;

                // Always assign only Xbox 360 platform here
                var metadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = uniqueKey.ToLower(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = "Download: Myrient",
                    Type = GameActionType.URL,
                    Path = link.Item1,
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

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
                            Path = link.Item1, // Note: This is the download link; you probably want to set this to the ROM path when installed
                            IsPlayAction = true
                        });
                    }
                }

                results.Add(metadata);
            });

            logger.Info($"[Xbox360_Games] Scraping completed. Total new games added: {results.Count}");
            return results.ToList();
        }
        private List<string> FindXbox360DigitalGameRoms(string gameName)
        {
            // Use same extensions and directory as disc-based
            var romPaths = new List<string>();
            var searchExtensions = new[] { ".zar", ".iso", ".xex" };
            var searchDirectory = "Roms\\Microsoft - Xbox 360"; // Digital and Disc together

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

        // 2. Scraper for "Xbox 360 Digital"
        private async Task<List<GameMetadata>> Myrient_Xbox360Digital_ScrapeStaticPage()
        {
            // O(1) lookup: Only games with Download: Myrient action for this pluginId & platform
            var dbGameKeysPerPlatform = new HashSet<string>(
                PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id
                        && g.Platforms != null
                        && g.GameActions != null
                        && g.GameActions.Any(a => a.Name.Equals("Download: Myrient", StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(g => g.Platforms.Select(p => $"{Myrient_NormalizeGameName(g.Name)}|{p.Name}")),
                StringComparer.OrdinalIgnoreCase);

            logger.Info($"[Xbox360Digital_Games] Scraping games from: {Xbox360Digital_Games_BaseUrl}");

            string pageContent = await Myrient_LoadPageContent(Xbox360Digital_Games_BaseUrl).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pageContent))
            {
                logger.Warn("[Xbox360Digital_Games] Failed to retrieve main page content.");
                return new List<GameMetadata>();
            }
            logger.Info($"[Xbox360Digital_Games] Page content retrieved successfully ({pageContent.Length} characters).");

            // Get all .zip links (unfiltered)
            var links = Myrient_ParseLinks(pageContent)?
                .Where(link => link.Item1.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (links == null || links.Length == 0)
            {
                logger.Info("[Xbox360Digital_Games] No valid game links found.");
                return new List<GameMetadata>();
            }
            logger.Info($"[Xbox360Digital_Games] Found {links.Length} .zip links before XBLA/XBLIG filtering.");

            var results = new ConcurrentBag<GameMetadata>();

            Parallel.ForEach(links, link =>
            {
                string text = link.Item2;
                if (string.IsNullOrWhiteSpace(text))
                    return;

                // Strict filtering: Only allow games with "(XBLA)" or "(XBLIG)", block others
                string lowerText = text.ToLower();
                if (!lowerText.Contains("(xbla)") && !lowerText.Contains("(xblig)"))
                    return;

                string cleanName = Myrient_CleanGameName(text).Replace(".zip", "").Trim();
                if (string.IsNullOrEmpty(cleanName))
                    cleanName = fallbackRegex.Replace(text, "$1").Replace('-', ' ').Trim();
                if (string.IsNullOrEmpty(cleanName))
                    return;

                string platformName = "Microsoft Xbox 360";
                string uniqueKey = $"{Myrient_NormalizeGameName(cleanName)}|{platformName}";

                // O(1) skip if the game already exists in the Playnite DB with "Download: Myrient" action
                if (dbGameKeysPerPlatform.Contains(uniqueKey))
                    return;

                var metadata = new GameMetadata
                {
                    Name = cleanName,
                    GameId = uniqueKey.ToLower(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty(platformName) },
                    GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = "Download: Myrient",
                    Type = GameActionType.URL,
                    Path = Xbox360Digital_Games_BaseUrl,
                    IsPlayAction = false
                }
            },
                    IsInstalled = false
                };

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
                            Path = link.Item1,
                            IsPlayAction = true
                        });
                    }
                }

                results.Add(metadata);
            });

            logger.Info($"[Xbox360Digital_Games] Scraping completed. Total new games added: {results.Count}");
            return results.ToList();
        }
        // Test to see ig Xbox Digital platform gets added, remove if fails:

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

        private string CleanGameName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // Start with the original text trimmed.
            string cleanName = name.Trim();

            // Remove any trailing update/build/hotfix info that starts with a dash (or en-dash) or plus sign.
            // This removes any text starting with these characters followed by "update" or "hotfix" (case-insensitive).
            cleanName = Regex.Replace(cleanName, @"[\-\–\+]\s*(update|hotfix).*$", "", RegexOptions.IgnoreCase).Trim();

            // Remove version numbers, build info, and "Free Download" markers.
            cleanName = Regex.Replace(cleanName, @"\s*v[\d\.]+.*", "", RegexOptions.IgnoreCase);
            cleanName = Regex.Replace(cleanName, @"\s*Build\s*\d+.*", "", RegexOptions.IgnoreCase);
            cleanName = Regex.Replace(cleanName, @"\s*Free Download.*", "", RegexOptions.IgnoreCase);
            cleanName = Regex.Replace(cleanName, @"\.zip$", "", RegexOptions.IgnoreCase);


            // Remove text in parentheses or brackets.
            cleanName = Regex.Replace(cleanName, @"[

\[\(].*?[\]

\)]", "", RegexOptions.IgnoreCase).Trim();
            cleanName = Regex.Replace(cleanName, @"[

\[\(].*$", "", RegexOptions.IgnoreCase).Trim();

            // Replace common HTML-encoded characters.
            cleanName = cleanName.Replace("&#8217;", "'")
                                 .Replace("&#8211;", "-")
                                 .Replace("&#8216;", "‘")
                                 .Replace("&#038;", "&")
                                 .Replace("&#8220;", "\"")
                                 .Replace("&#8221;", "\"");

            // Remove specific unwanted phrases.
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

            // Replace standalone dashes (with spaces before and after) with a colon and space.
            cleanName = Regex.Replace(cleanName, @"\s+-\s+", ": ");

            // Process comma: if text after the comma looks like file size (e.g. "56.24GB"), remove it.
            int commaIndex = cleanName.IndexOf(',');
            if (commaIndex > 0)
            {
                string afterComma = cleanName.Substring(commaIndex + 1).Trim();
                if (Regex.IsMatch(afterComma, @"^\d+(\.\d+)?\s*(gb|mb)", RegexOptions.IgnoreCase))
                {
                    cleanName = cleanName.Substring(0, commaIndex).Trim();
                }
            }

            // Process slash: if a slash exists, keep only the text before the first slash.
            int slashIndex = cleanName.IndexOf('/');
            if (slashIndex > 0)
            {
                cleanName = cleanName.Substring(0, slashIndex).Trim();
            }

            return cleanName.Trim(' ', '-', '–', '+', ',');
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

        private string NormalizeGameName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // 1. Convert to lowercase and trim.
            string normalized = name.ToLowerInvariant().Trim();

            // 1.1 Remove periods if they occur between word characters.
            normalized = Regex.Replace(normalized, @"(?<=\w)\.(?=\w)", "");

            // 2. Remove apostrophes (both straight and smart).
            normalized = normalized.Replace("’", "").Replace("'", "");

            // 3. Replace ampersands and plus signs with " and ".
            normalized = normalized.Replace("&", " and ").Replace("+", " and ");

            // 4. Remove unwanted punctuation (except spaces).
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

            // 9. Normalize Spider-man variants into one word "spiderman".
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

            // 14. Final cleanup: collapse spaces **once more** to catch subtle issues.
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            return normalized;
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

        private List<Tuple<string, string>> FitGirlExtractGameLinks(string pageContent)
        {
            var links = new List<Tuple<string, string>>();

            // Adjusted regex: capture valid game links inside <h2> or <h3> tags with "entry-title"
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

        private string AnkerExtractGameNameFromPage(string pageContent)
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

        private string ElamigosExtractGameNameFromPage(string pageContent)
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

        private string SanitizePath(string path)
        {
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }

        private string AnkerSanitizePath(string path)
        {
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }

        private string PS2_SanitizePath(string path)
        {
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }

        private string MagipackSanitizePath(string path)
        {
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }

        private string ElamigosSanitizePath(string path)
        {
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }

        private bool IsDuplicate(GameMetadata gameMetadata)
        {
            return PlayniteApi.Database.Games.Any(existing =>
                existing.PluginId == Id &&
                existing.Name.Equals(gameMetadata.Name, StringComparison.OrdinalIgnoreCase));
        }

        private bool AnkerIsDuplicate(string gameName)
        {
            return PlayniteApi.Database.Games.Any(existing =>
                existing.PluginId == Id &&
                existing.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        }

        private bool MagipackIsDuplicate(string gameName)
        {
            return PlayniteApi.Database.Games.Any(existing =>
                existing.PluginId == Id &&
                existing.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        }

        private bool FitGirlIsDuplicate(string gameName)
        {
            return PlayniteApi.Database.Games.Any(existing =>
                existing.PluginId == Id &&
                existing.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        }

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

        private bool ElamigosIsDuplicate(string gameName)
        {
            return PlayniteApi.Database.Games.Any(existing =>
                existing.PluginId == Id &&
                existing.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        }

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
                    // Ensure the game belongs to Game Store before modifying installation.
                    if (Game.PluginId != pluginInstance.Id)  // Checks against the correct PluginId
                    {
                        LogToInstall($"Skipping installation update for {Game.Name}. It does not belong to Game Store.");
                        return;
                    }

                    // Search for a local repack candidate.
                    var (candidatePath, isArchive, fileSize, candidateFound) = await SearchForLocalRepackAsync(Game.Name);
                    if (candidateFound)
                    {
                        if (string.IsNullOrEmpty(candidatePath))
                        {
                            return;
                        }
                        else
                        {
                            if (isArchive)
                            {
                                string selectedDrive = ShowDriveSelectionDialog(fileSize);
                                if (string.IsNullOrEmpty(selectedDrive))
                                {
                                    return;
                                }

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
                                ProcessStartInfo psi = new ProcessStartInfo
                                {
                                    FileName = sevenZipExe,
                                    Arguments = arguments,
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    WindowStyle = ProcessWindowStyle.Hidden
                                };

                                await Task.Run(() =>
                                {
                                    using (Process proc = Process.Start(psi))
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
                                        ProcessStartInfo psi = new ProcessStartInfo
                                        {
                                            FileName = setupExePath,
                                            UseShellExecute = false,
                                            CreateNoWindow = true
                                        };
                                        using (Process proc = Process.Start(psi))
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
                    }
                    else
                    {
                        var urlActions = Game.GameActions.Where(a => a.Type == GameActionType.URL).ToList();
                        if (!urlActions.Any())
                        {
                            playniteApi.Dialogs.ShowErrorMessage("No valid download sources found.", "Error");
                            return;
                        }

                        string selectedSource = ShowSourceSelectionDialog(urlActions);
                        if (string.IsNullOrEmpty(selectedSource))
                        {
                            return;
                        }

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
                        else
                        {
                            playniteApi.Dialogs.ShowErrorMessage("Unknown download source selected.", "Error");
                        }
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
                // Build custom button options.
                List<MessageBoxOption> options = new List<MessageBoxOption>();
                Dictionary<MessageBoxOption, string> optionMapping = new Dictionary<MessageBoxOption, string>();
                bool isFirst = true;

                // "Install" button (default)
                MessageBoxOption installOption = new MessageBoxOption("Install", isFirst, false);
                isFirst = false;
                options.Add(installOption);
                optionMapping[installOption] = "Install";

                // "Download" button
                MessageBoxOption downloadOption = new MessageBoxOption("Download", false, false);
                options.Add(downloadOption);
                optionMapping[downloadOption] = "Download";

                // "Cancel" button
                MessageBoxOption cancelOption = new MessageBoxOption("Cancel", false, true);
                options.Add(cancelOption);
                optionMapping[cancelOption] = "Cancel";

                var mainWindow = playniteApi.Dialogs.GetCurrentAppWindow();
                mainWindow?.Activate();

                // Use the working overload (positional parameters only) just like your provider dialog.
                MessageBoxOption selectedOption = playniteApi.Dialogs.ShowMessage(
                     "Select your action:",
                     "Action",
                     MessageBoxImage.Question,
                     options);

                if (selectedOption != null &&
                    optionMapping.TryGetValue(selectedOption, out string chosenAction) &&
                    !chosenAction.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
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
                var downloadAction = Game.GameActions.FirstOrDefault(a => a.Name.Equals("Download: AnkerGames", StringComparison.OrdinalIgnoreCase));
                if (downloadAction == null || string.IsNullOrEmpty(downloadAction.Path))
                {
                    playniteApi.Dialogs.ShowErrorMessage("No valid download URL found for this game.", "Error");
                    return;
                }
                string url = downloadAction.Path;

                await Task.Run(() =>
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd",
                        Arguments = $"/c start \"\" \"{url}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                });
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


            private async Task HandleSteamRipDownload()
            {
                var downloadAction = Game.GameActions.FirstOrDefault(a => a.Name.Equals("Download: SteamRip", StringComparison.OrdinalIgnoreCase));
                if (downloadAction == null || string.IsNullOrEmpty(downloadAction.Path))
                {
                    playniteApi.Dialogs.ShowErrorMessage("Invalid source URL selected.", "Error");
                    return;
                }
                string gameUrl = downloadAction.Path;

                List<string> links = await ScrapeSiteForLinksAsync(Game.Name, gameUrl);
                if (links == null || links.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"No download links found for {Game.Name}.", "Download Error");
                    return;
                }

                Dictionary<string, string> providerDict = BuildProviderDictionary(links);
                if (providerDict.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage("No recognized providers were found.", "Provider Error");
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

            private async Task HandleMyrientDownload()
            {
                // Retrieve the Myrient download action from the game.
                var downloadAction = Game.GameActions
                    .FirstOrDefault(a => a.Name.Equals("Download: Myrient", StringComparison.OrdinalIgnoreCase));

                if (downloadAction == null || string.IsNullOrEmpty(downloadAction.Path))
                {
                    playniteApi.Dialogs.ShowErrorMessage("Invalid source URL selected.", "Error");
                    return;
                }

                // The base URL to scrape for ROM download links.
                string baseUrl = downloadAction.Path; // e.g. "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation%202/"

                // Scrape the base URL for all hyperlinks that point to ZIP files.
                List<string> links = await ScrapeSiteForLinksAsync(Game.Name, baseUrl);
                if (links == null || links.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"No download links found for {Game.Name}.", "Download Error");
                    return;
                }

                // Convert any relative links into fully-qualified URLs.
                links = links.Select(link =>
                {
                    // If the link does not start with "http", then combine with baseUrl.
                    if (!link.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // Create a new Uri based on baseUrl.
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

                // Normalize the game name from the current Game.
                string normGameName = NormalizeGameName(Game.Name);

                // Filter the list: decode each URL, extract & normalize its file name,
                // keeping only those that contain the normalized game name.
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

                // Build a dictionary mapping each ROM variant to its full download URL.
                Dictionary<string, string> variantDict = BuildMyrientVariantDictionary(links);
                if (variantDict.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage("No recognized download variants were found.", "Download Error");
                    return;
                }

                // Display a menu with one button for each variant.
                string selectedVariant = ShowMyrientVariantSelectionDialog(variantDict);
                if (string.IsNullOrEmpty(selectedVariant))
                {
                    // User cancelled.
                    return;
                }

                // Open the download URL for the selected variant in the default browser.
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

            /// <summary>
            /// Builds a dictionary mapping each Myrient variant to its full download URL.
            /// </summary>
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

            /// <summary>
            /// Attempts to deduce a variant name from the download URL's file name.
            /// Heuristics:
            /// 1. If the file name contains text within parentheses (e.g. "OutRun 2006 - Coast 2 Coast (Beta).zip"),
            ///    returns the inner text.
            /// 2. Otherwise, if underscores are present, returns the final underscore-separated token.
            /// 3. Otherwise, returns the trimmed file name.
            /// </summary>
            private string GetMyrientVariantName(string url)
            {
                // Decode the URL and extract the file name without extension.
                string decodedUrl = WebUtility.UrlDecode(url);
                string fileName = System.IO.Path.GetFileNameWithoutExtension(decodedUrl);
                if (string.IsNullOrEmpty(fileName))
                {
                    return url.Trim();
                }

                // Look for text inside parentheses.
                var match = Regex.Match(fileName, @"\(([^)]+)\)");
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }

                // Otherwise, split on underscores and return the last segment.
                if (fileName.Contains("_"))
                {
                    var parts = fileName.Split('_');
                    if (parts.Length > 0)
                    {
                        return parts.Last().Trim();
                    }
                }

                // Fallback to the whole file name.
                return fileName.Trim();
            }

            /// <summary>
            /// Displays a selection dialog with one button per variant using MessageBoxOption.
            /// Returns the selected variant label or null if the user cancels.
            /// </summary>
            private string ShowMyrientVariantSelectionDialog(Dictionary<string, string> variantDict)
            {
                List<MessageBoxOption> options = new List<MessageBoxOption>();
                Dictionary<MessageBoxOption, string> optionMapping = new Dictionary<MessageBoxOption, string>();

                bool isFirst = true;
                foreach (string variant in variantDict.Keys)
                {
                    var option = new MessageBoxOption(variant, isFirst, false);
                    isFirst = false;
                    options.Add(option);
                    optionMapping[option] = variant;
                }

                // Add a Cancel option.
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
                // Use our DDL scraper – for now, filter the full link list to include only DDL providers
                List<string> allLinks = await ScrapeFitGirlLinksAsync(Game.Name, gameUrl);
                if (allLinks == null || allLinks.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"No FitGirl links found for {Game.Name}.", "Download Error");
                    return;
                }

                // Filter for DDL links – for example, you might require that the link does NOT start with "magnet:"
                var ddlLinks = allLinks
                    .Where(link => !link.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) &&
                                   (link.IndexOf("datanodes", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    link.IndexOf("fuckingfast", StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToList();

                if (ddlLinks.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage("No FitGirl DDL links found.", "Download Error");
                    return;
                }

                Dictionary<string, string> providerDict = BuildProviderDictionary(ddlLinks);
                if (providerDict.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage("No recognized FitGirl DDL providers were found.", "Provider Error");
                    return;
                }

                string selectedProvider = ShowProviderSelectionDialog(providerDict);
                if (string.IsNullOrEmpty(selectedProvider))
                {
                    return; // User canceled.
                }

                if (providerDict.TryGetValue(selectedProvider, out string providerUrl))
                {
                    // Special-case the "FuckingFast" provider.
                    if (selectedProvider.Equals("FuckingFast", StringComparison.OrdinalIgnoreCase))
                    {
                        await OpenFuckingFastLinkAsync(providerUrl);
                    }
                    else
                    {
                        await OpenDownloadLinkForProviderAsync(selectedProvider, providerUrl);
                    }
                }
                else
                {
                    playniteApi.Dialogs.ShowErrorMessage("Selected provider was not found.", "Selection Error");
                }
            }


            // Helper method to load the page at the given URL, find the paste.fitgirl link, and open it.
            private async Task OpenFuckingFastLinkAsync(string url)
            {
                try
                {
                    // Load the page content.
                    string pageContent = await pluginInstance.LoadPageContent(url);

                    // Use a domain-specific regex that looks for an href attribute that begins with "https://paste.fitgirl-repacks.site/"
                    string pattern = @"href\s*=\s*[""'](?<dlUrl>https?://paste\.fitgirl-repacks\.site/[^""']+)[""']";
                    Match match = Regex.Match(pageContent, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

                    if (match.Success)
                    {
                        string selectedUrl = match.Groups["dlUrl"].Value.Trim();

                        // Open the extracted URL in the default browser.
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "cmd",
                            Arguments = $"/c start \"\" \"{selectedUrl}\"",
                            CreateNoWindow = true,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        playniteApi.Dialogs.ShowErrorMessage("Could not locate the paste.fitgirl-repacks.site link.", "Parsing Error");
                    }
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"Error opening FuckingFast link: {ex.Message}", "Parsing Error");
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
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "cmd",
                                Arguments = $"/c start \"\" \"{selectedUrl}\"",
                                CreateNoWindow = true,
                                UseShellExecute = true
                            });
                            return;
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

                // For all other providers, open the URL in the default browser.
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c start \"\" \"{url}\"",
                    CreateNoWindow = true,
                    UseShellExecute = true
                });
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

                    // 2. Extract relative or absolute .zip links for Myrient
                    //    Matches: <a href="Something.zip"> or <a href='/path/Another.zip'>
                    var zipMatches = Regex.Matches(pageContent,
                        @"<a\s+[^>]*href\s*=\s*[""']([^""'>]+\.zip)[""']",
                        RegexOptions.IgnoreCase);
                    links.AddRange(
                        zipMatches.Cast<Match>()
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
                    if (!providerDict.ContainsKey(provider))
                    {
                        providerDict.Add(provider, link);
                    }
                }
                return providerDict;
            }

            // Helper: Determine provider name from URL.
            private string GetProviderName(string url)
            {
                // If the URL is a magnet link...
                if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    // If an explicit prefix exists before the first colon, use it.
                    int colonIndex = url.IndexOf(":");
                    if (colonIndex > 0)
                    {
                        string potentialPrefix = url.Substring(0, colonIndex).Trim();
                        // If the prefix matches a known torrent provider, return it.
                        if (potentialPrefix.Equals("1337x", StringComparison.OrdinalIgnoreCase) ||
                            potentialPrefix.Equals("rutor", StringComparison.OrdinalIgnoreCase))
                        {
                            return potentialPrefix;
                        }
                    }

                    // Check if the magnet link itself includes known identifiers.
                    if (url.IndexOf("1337x", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return "1337x";
                    }
                    if (url.IndexOf("rutor", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return "RuTor";
                    }

                    // Next, check the tracker parameters.
                    var parameters = url.Split('&');
                    foreach (var param in parameters)
                    {
                        string lowerParam = param.ToLower();
                        if (lowerParam.Contains("1337x.to"))
                        {
                            return "1337x";
                        }
                        if (lowerParam.Contains("rutor"))
                        {
                            return "RuTor";
                        }
                    }
                    // Fall back to a generic label if no identifier is found.
                    return "Torrent";
                }

                // For direct download (DDL) links and others, check for known domains.
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
                if (url.IndexOf("filecrypt.co", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "FileCrypt";
                if (url.IndexOf("buzzheavier.com", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "BuzzHeavier";
                if (url.IndexOf("filecrypt.co", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "filecrypt";
                // Fallback: return the trimmed URL text (or you could return "Unknown" if preferred).
                return url.Trim();
            }

            // Helper: Display provider selection dialog.
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
                if (provider.Equals("1Fichier", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string pageContent = await pluginInstance.LoadPageContent(url);
                        // Look for the form containing an input with id="dlb" and get the action URL.
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
                    try
                    {
                        string buzzContent = await pluginInstance.LoadPageContent(url);
                        // Look for an anchor with a class such as "link-button gay-button" and an hx-get attribute.
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
                        playniteApi.Dialogs.ShowErrorMessage($"Error extracting BuzzHeavier download link: {ex.Message}", "Parsing Error");
                    }
                }
                else if (provider.Equals("FuckingFast", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        // Load the page content from the specified URL.
                        string pageContent = await pluginInstance.LoadPageContent(url);

                        // Define a pattern that captures all anchor tags with both the href attribute and inner text.
                        string pattern = @"<a\s+[^>]*href\s*=\s*[""'](?<dlUrl>[^""']+)[""'][^>]*>(?<linkText>.*?)</a>";
                        Regex regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                        MatchCollection matches = regex.Matches(pageContent);

                        string selectedUrl = null;
                        foreach (Match match in matches)
                        {
                            // Retrieve the inner text, and trim it.
                            string linkText = match.Groups["linkText"].Value.Trim();

                            // Check if it matches the desired hyperlink name.
                            if (linkText.Equals("Filehoster: FuckingFast", StringComparison.OrdinalIgnoreCase))
                            {
                                selectedUrl = match.Groups["dlUrl"].Value.Trim();
                                break;
                            }
                        }

                        if (!string.IsNullOrEmpty(selectedUrl))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "cmd",
                                Arguments = $"/c start \"\" \"{selectedUrl}\"",
                                CreateNoWindow = true,
                                UseShellExecute = true
                            });
                            return;
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

                // For GoFile and all other providers, simply open the URL in the default browser.
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c start \"\" \"{url}\"",
                    CreateNoWindow = true,
                    UseShellExecute = true
                });
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
