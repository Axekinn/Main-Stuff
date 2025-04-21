using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PS2
{
    public class PS2 : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly string baseUrl = "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation%202/";

        public override Guid Id { get; } = Guid.Parse("772ef741-cf7e-4f48-9f01-a80d6121c616");
        public override string Name => "PS2";

        public PS2(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties { HasSettings = false };
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var games = new List<GameMetadata>();

            // Step 1: Filter existing games in Playnite
            var existingGames = PlayniteApi.Database.Games
                .Where(g =>
                    g.PluginId == Id && // Check if the plugin ID matches
                    g.Platforms != null && g.Platforms.Any(p => p.Name.Equals("Sony PlayStation 2", StringComparison.OrdinalIgnoreCase)))
                .ToDictionary(g => g.Name.ToLowerInvariant(), g => g);

            // Step 2: Scrape game data from the site (only once)
            var scrapedGames = ScrapeStaticPage().GetAwaiter().GetResult();
            logger.Info($"Total scraped game entries: {scrapedGames.Count}");

            // Step 3: Process each scraped game
            foreach (var scrapedGame in scrapedGames)
            {
                var gameName = scrapedGame.Name.Replace(".zip", "").Trim(); // Clean game name

                // Check if the game already exists in Playnite
                if (existingGames.ContainsKey(gameName.ToLowerInvariant()))
                {
                    logger.Info($"Game '{gameName}' already exists in Playnite library. Skipping.");
                    continue;
                }

                // Proceed with processing the scraped game
                var cleanedGameName = CleanNameForMatching(gameName);
                var romPaths = FindGameRoms(cleanedGameName);

                // Match ROMs to the cleaned game name
                var matchingRoms = romPaths
                    .Where(romPath =>
                        string.Equals(
                            CleanNameForMatching(System.IO.Path.GetFileNameWithoutExtension(romPath)),
                            cleanedGameName,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!matchingRoms.Any())
                {
                    logger.Warn($"No matching ROMs found for game: {gameName} (Cleaned: {cleanedGameName})");
                }
                else
                {
                    logger.Info($"Matching ROMs found for game: {gameName} - ROMs: {string.Join(", ", matchingRoms)}");
                }

                // Create the base game metadata
                var gameMetadata = new GameMetadata
                {
                    Name = gameName,
                    GameId = gameName.ToLowerInvariant(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("Sony PlayStation 2") },
                    GameActions = new List<GameAction>
            {
                // Add the "Download" action
                new GameAction
                {
                    Name = "Download",
                    Type = GameActionType.URL,
                    Path = scrapedGame.GameActions.FirstOrDefault()?.Path,
                    IsPlayAction = false
                }
            },
                    IsInstalled = matchingRoms.Any()
                };

                // Add emulator play action and ROMs if matching ROMs are found
                if (matchingRoms.Any())
                {
                    var firstRomPath = matchingRoms.First();

                    // Find the PCSX2 emulator and its "Default QT" profile
                    var pcsx2Emulator = PlayniteApi.Database.Emulators
                        .FirstOrDefault(e => e.Name.Equals("PCSX2", StringComparison.OrdinalIgnoreCase));

                    if (pcsx2Emulator != null)
                    {
                        // Retrieve the built-in profiles for the emulator
                        var builtInProfiles = pcsx2Emulator.BuiltinProfiles?.ToList();

                        if (builtInProfiles != null && builtInProfiles.Any())
                        {
                            var emulatorProfile = builtInProfiles
                                .FirstOrDefault(p => p.Name.Equals("Default QT", StringComparison.OrdinalIgnoreCase));

                            if (emulatorProfile != null)
                            {
                                // Add the play action with the matched profile
                                gameMetadata.GameActions.Add(new GameAction
                                {
                                    Name = "Play",
                                    Type = GameActionType.Emulator,
                                    EmulatorId = pcsx2Emulator.Id,
                                    EmulatorProfileId = emulatorProfile.Id,
                                    Path = firstRomPath,
                                    IsPlayAction = true
                                });

                                // Associate ROMs with the game
                                gameMetadata.Roms = matchingRoms.Select(romPath => new GameRom
                                {
                                    Name = System.IO.Path.GetFileName(romPath),
                                    Path = romPath
                                }).ToList();

                                // Set the installation directory to the first ROM's directory
                                gameMetadata.InstallDirectory = System.IO.Path.GetDirectoryName(firstRomPath);
                            }
                            else
                            {
                                logger.Warn($"Emulator profile 'Default QT' not found for PCSX2. Skipping profile setup for game '{gameName}'.");
                            }
                        }
                        else
                        {
                            logger.Warn($"No built-in profiles found for PCSX2 emulator. Skipping emulator setup for game '{gameName}'.");
                        }
                    }
                    else
                    {
                        logger.Warn($"PCSX2 emulator not found in the database. Skipping emulator setup for game '{gameName}'.");
                    }
                }

                games.Add(gameMetadata);
                logger.Info($"Game added to library: {gameName}");
            }

            logger.Info($"Total games prepared for library: {games.Count}");
            return games;
        }

        private async Task<List<GameMetadata>> ScrapeStaticPage()
        {
            var gameEntries = new Dictionary<string, GameMetadata>();
            var uniqueGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string url = baseUrl;
            logger.Info($"Scraping: {url}");

            string pageContent = await LoadPageContent(url);
            var links = ParseLinks(pageContent);

            foreach (var link in links)
            {
                string href = link.Item1;
                string text = link.Item2;

                if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(text) || !href.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Filter for "(World)" or "(USA)" regions only
                if (!Regex.IsMatch(text, @"\((World|USA)\)", RegexOptions.IgnoreCase))
                    continue;

                string cleanName = CleanGameName(text);

                if (!string.IsNullOrEmpty(cleanName))
                {
                    var gameKey = cleanName;
                    if (!gameEntries.ContainsKey(gameKey))
                    {
                        // Create a new game entry
                        gameEntries[gameKey] = new GameMetadata
                        {
                            Name = cleanName,
                            Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("Sony PlayStation 2") },
                            GameActions = new List<GameAction>(),
                            IsInstalled = false
                        };
                    }

                    // Add the URL as a new game action
                    var gameAction = new GameAction
                    {
                        Name = $"Download: {text}",
                        Type = GameActionType.URL,
                        Path = href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                            ? href
                            : $"{baseUrl}{href}",
                        IsPlayAction = false
                    };

                    // Avoid duplicate actions
                    if (!gameEntries[gameKey].GameActions.Any(action => action.Path.Equals(gameAction.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        gameEntries[gameKey].GameActions.Add(gameAction);
                    }
                }
            }

            // Convert the game entries dictionary to a list and return it
            return gameEntries.Values.ToList();
        }

        private string CleanGameName(string name)
        {
            // Remove unwanted characters and whitespace
            var cleanName = name.Trim();

            // Remove extension if present
            cleanName = Regex.Replace(cleanName, @"\.zip$", "", RegexOptions.IgnoreCase);

            // Remove any region information (e.g., "(Europe)") from the name
            cleanName = Regex.Replace(cleanName, @"\s*\(.*?\)$", "", RegexOptions.IgnoreCase);

            return cleanName;
        }

        private async Task<string> LoadPageContent(string url)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    return await httpClient.GetStringAsync(url);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to load page content from {url}: {ex.Message}");
                return string.Empty;
            }
        }

        private List<Tuple<string, string>> ParseLinks(string pageContent)
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

        private List<string> FindGameRoms(string gameName)
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

        private string CleanNameForMatching(string name)
        {
            try
            {
                // Normalize colons and dashes for flexible matching
                name = name.Replace(":", " ").Replace("-", " ");

                // Remove text inside square brackets (e.g., [0100FB2021B0E000][v0][US])
                name = Regex.Replace(name, @"\[[^\]]*\]", "");

                // Remove text inside parentheses, including region and language info
                name = Regex.Replace(name, @"\s*\([^)]*\)", "");

                // Decode HTML entities and trim whitespace
                name = System.Net.WebUtility.HtmlDecode(name).Trim();

                // Remove file extensions like .zip, .chd, .iso
                name = Regex.Replace(name, @"\.\w+$", "");

                // Normalize consecutive spaces
                name = Regex.Replace(name, @"\s+", " ");

                return name;
            }
            catch (Exception ex)
            {
                logger.Error($"Error cleaning name for matching: {name}. Error: {ex.Message}");
                return name; // Return the original name if cleaning fails
            }
        }
    }
}
