using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NSWDL
{
    public class NSWDL : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string BaseUrl = "https://nswdl.com/switch-posts/";

        public override Guid Id { get; } = Guid.Parse("c2320a0c-c0cd-409c-a7df-f1fd46d151de");
        public override string Name => "NSWDL";

        public NSWDL(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties { HasSettings = false };
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var games = new List<GameMetadata>();

            // Step 1: Get existing games in Playnite library added by this plugin
            var existingGames = PlayniteApi.Database.Games
                .Where(game => game.PluginId == Id) // Only consider games added by this plugin
                .Select(game => CleanNameForMatching(game.Name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            logger.Info($"Total games in Playnite library added by NSWDL: {existingGames.Count}");

            // Step 2: Scrape game data from the site
            var scrapedGames = ScrapeSite().GetAwaiter().GetResult();
            logger.Info($"Total scraped game entries: {scrapedGames.Count}");

            // Step 3: Process each scraped game
            foreach (var scrapedGame in scrapedGames)
            {
                var gameName = scrapedGame.Name;
                var cleanedGameName = CleanNameForMatching(gameName);

                // Check if the game already exists in Playnite (added by this plugin)
                if (existingGames.Contains(cleanedGameName))
                {
                    logger.Info($"Skipping game already in library (from this plugin): {gameName}");
                    continue;
                }

                // Find matching ROMs
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
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("Nintendo Switch") },
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
                    IsInstalled = matchingRoms.Any(),
                    Icon = null,
                    BackgroundImage = null
                };

                // Add emulator play action and ROMs if matching ROMs are found
                if (matchingRoms.Any())
                {
                    var firstRomPath = matchingRoms.First();

                    gameMetadata.GameActions.Add(new GameAction
                    {
                        Name = "Play",
                        Type = GameActionType.Emulator,
                        EmulatorId = PlayniteApi.Database.Emulators
                            .FirstOrDefault(e => e.Name.Equals("Ryujinx", StringComparison.OrdinalIgnoreCase))?.Id ?? Guid.Empty,
                        EmulatorProfileId = "Default",
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

                // Add the game metadata to the list of games
                games.Add(gameMetadata);
                logger.Info($"Game added to library: {gameName}");
            }

            logger.Info($"Total games prepared for library: {games.Count}");
            return games;
        }

        private async Task<List<GameMetadata>> ScrapeSite()
        {
            var gameEntries = new List<GameMetadata>();
            var uniqueUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string currentUrl = BaseUrl;
                logger.Info($"Scraping site index: {currentUrl}");

                string pageContent = await LoadPageContent(currentUrl);

                if (string.IsNullOrWhiteSpace(pageContent))
                {
                    logger.Warn($"No content found on site index. Stopping scraping.");
                    return gameEntries;
                }

                var links = ParseLinks(pageContent);
                logger.Info($"Found {links.Count} hyperlinks on the site index.");

                foreach (var link in links)
                {
                    string href = link.Item1;
                    string text = link.Item2;

                    if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(text))
                    {
                        logger.Warn($"Skipping invalid hyperlink: Text='{text}', URL='{href}'");
                        continue;
                    }

                    // Use original naming logic for accurate naming
                    string cleanName = CleanGameName(text);

                    if (!string.IsNullOrEmpty(cleanName) && uniqueUrls.Add(href))
                    {
                        var gameMetadata = new GameMetadata
                        {
                            Name = cleanName,
                            GameActions = new List<GameAction>
                            {
                                new GameAction
                                {
                                    Name = "Download",
                                    Type = GameActionType.URL,
                                    Path = href,
                                    IsPlayAction = false
                                }
                            },
                            IsInstalled = false
                        };

                        gameEntries.Add(gameMetadata);
                        logger.Info($"Game added: Name='{cleanName}', URL='{href}'");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error during scraping: {ex.Message}");
            }

            return gameEntries;
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
                var matches = Regex.Matches(pageContent, @"<a\s+href=[""'](https://nswdl\.com/[^""']+)[""'].*?>(.*?)</a>");
                foreach (Match match in matches)
                {
                    string href = match.Groups[1].Value;
                    string text = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value).Trim();
                    text = Regex.Replace(text, "<.*?>", string.Empty);
                    links.Add(new Tuple<string, string>(href, text));
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error parsing links: {ex.Message}");
            }

            return links;
        }

        private string CleanGameName(string name)
        {
            try
            {
                // Use the original naming logic for accuracy
                var decodedName = System.Net.WebUtility.HtmlDecode(name);

                // Remove text inside parentheses (e.g., "(Extra Content)")
                var cleanName = Regex.Replace(decodedName, @"\s*\(.*?\)", "", RegexOptions.IgnoreCase).Trim();

                // Remove trademark and registered symbols
                cleanName = cleanName.Replace("™", "").Replace("®", "").Trim();

                // Remove "+ AOC bundle" or similar phrases in the game name
                cleanName = Regex.Replace(cleanName, @"\s*\+.*?AOC bundle", "", RegexOptions.IgnoreCase).Trim();

                return cleanName;
            }
            catch (Exception ex)
            {
                logger.Error($"Error cleaning game name: {name}. Error: {ex.Message}");
                return name; // Return the original name if cleaning fails
            }
        }

        private string CleanNameForMatching(string name)
        {
            try
            {
                // Normalize colons (:), dashes (-), and other characters for flexible matching
                name = name.Replace(":", " ").Replace("-", " ");

                // Remove text inside square brackets [ ] (e.g., [0100FB2021B0E000][v0][US])
                name = Regex.Replace(name, @"\[[^\]]*\]", "");

                // Decode HTML entities and trim whitespace
                name = System.Net.WebUtility.HtmlDecode(name).Trim();

                // Remove file extensions like .nsp, .xci, .nsa
                name = Regex.Replace(name, @"\.\w+$", "");

                // Preserve text in parentheses ( ) and curly braces { }
                name = Regex.Replace(name, @"[^a-zA-Z0-9\s\(\)\{\}]", "").Trim();

                // Normalize consecutive spaces to a single space
                name = Regex.Replace(name, @"\s+", " ");

                return name.ToLowerInvariant();
            }
            catch (Exception ex)
            {
                logger.Error($"Error cleaning name for matching: {name}. Error: {ex.Message}");
                return name;
            }
        }

        private List<string> FindGameRoms(string gameName)
        {
            if (string.IsNullOrEmpty(gameName))
            {
                throw new ArgumentException($"'{nameof(gameName)}' cannot be null or empty.", nameof(gameName));
            }

            var romPaths = new List<string>();
            var searchExtensions = new[] { ".nsp", ".nsa", ".nca", ".xci" };
            var searchDirectory = "Roms\\Nintendo - Switch\\Games";

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

        

        }
    }
