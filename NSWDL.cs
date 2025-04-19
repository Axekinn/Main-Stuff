using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
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

        /// <summary>
        /// Scrapes the website for game entries and returns a list of GameMetadata.
        /// </summary>
        private async Task<List<GameMetadata>> ScrapeSite()
        {
            var gameEntries = new List<GameMetadata>();
            var uniqueGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                logger.Info($"Scraping: {BaseUrl}");
                string pageContent = await LoadPageContent(BaseUrl);
                var links = ParseLinks(pageContent);

                foreach (var link in links)
                {
                    string href = link.Item1;
                    string text = link.Item2;

                    if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(text))
                        continue;

                    string cleanName = CleanGameName(text);

                    if (!string.IsNullOrEmpty(cleanName) && uniqueGames.Add(cleanName))
                    {
                        var gameMetadata = new GameMetadata
                        {
                            Name = cleanName,
                            Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("Nintendo Switch") },
                            GameActions = new List<GameAction>
                            {
                                new GameAction
                                {
                                    Name = "Download: NSWDL",
                                    Type = GameActionType.URL,
                                    Path = href,
                                    IsPlayAction = false
                                }
                            },
                            IsInstalled = false
                        };

                        gameEntries.Add(gameMetadata);
                        logger.Info($"Game scraped: {cleanName} | Link: {href}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error during scraping: {ex.Message}");
            }

            return gameEntries;
        }

        /// <summary>
        /// Loads the HTML content of the given URL.
        /// </summary>
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

        /// <summary>
        /// Parses HTML content to extract links and their text.
        /// </summary>
        private List<Tuple<string, string>> ParseLinks(string pageContent)
        {
            var links = new List<Tuple<string, string>>();

            try
            {
                var matches = Regex.Matches(pageContent, @"<a\s+href=[""'](https://nswdl\.com/\d+/)[""'].*?>(.*?)</a>");
                foreach (Match match in matches)
                {
                    string href = match.Groups[1].Value;
                    string text = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value); // Decode text
                    text = Regex.Replace(text, "<.*?>", string.Empty); // Strip any remaining HTML tags
                    links.Add(new Tuple<string, string>(href, text));
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error parsing links: {ex.Message}");
            }

            return links;
        }

        /// <summary>
        /// Cleans the game name by decoding HTML entities, removing unwanted characters, and trimming whitespace.
        /// </summary>
        private string CleanGameName(string name)
        {
            try
            {
                // Decode HTML entities
                var decodedName = System.Net.WebUtility.HtmlDecode(name);

                // Remove unwanted characters or HTML artifacts
                var cleanName = Regex.Replace(decodedName, @"\s*\(.*?\)", "", RegexOptions.IgnoreCase).Trim();
                cleanName = cleanName.Replace("™", "").Replace("®", "").Trim();

                return cleanName;
            }
            catch (Exception ex)
            {
                logger.Error($"Error cleaning game name: {name}. Error: {ex.Message}");
                return name; // Return the original name if cleaning fails
            }
        }

        /// <summary>
        /// Fetches games from the scraped site and prepares them for the Playnite library.
        /// </summary>
        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var games = new List<GameMetadata>();
            var scrapedGames = ScrapeSite().GetAwaiter().GetResult(); // Using ScrapeSite method for NSWDL
            logger.Info($"Total scraped game entries: {scrapedGames.Count}");

            foreach (var game in scrapedGames)
            {
                var gameName = game.Name;
                var sanitizedGameName = CleanGameName(gameName);

                // Check if the game already exists in the Playnite library
                if (PlayniteApi.Database.Games.Any(existingGame => existingGame.PluginId == Id && existingGame.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase)))
                {
                    logger.Info($"Skipping duplicate game: {gameName}");
                    continue;
                }

                // Find the platform ID for "Nintendo Switch"
                var platformId = PlayniteApi.Database.Platforms.FirstOrDefault(p => p.Name.Equals("Nintendo Switch", StringComparison.OrdinalIgnoreCase))?.Id;

                if (platformId != null)
                {
                    var gameMetadata = new GameMetadata()
                    {
                        Name = gameName,
                        GameId = gameName.ToLower(),
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("Nintendo Switch") },
                        GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = "Download: NSWDL",
                        Type = GameActionType.URL,
                        Path = game.GameActions.First().Path, // The scraped URL for the game
                        IsPlayAction = false
                    }
                },
                        IsInstalled = false,
                        InstallDirectory = null, // Scraped games don't have an install directory
                        Icon = new MetadataFile(Path.Combine(sanitizedGameName, "icon.png")),
                        BackgroundImage = new MetadataFile(Path.Combine(sanitizedGameName, "background.png"))
                    };

                    games.Add(gameMetadata);
                    logger.Info($"Game added to library: {gameName}");
                }
                else
                {
                    logger.Error($"Platform not found for game: {gameName}, Platform: Nintendo Switch");
                }
            }

            logger.Info($"Total games prepared for library: {games.Count}");
            return games;
        }
    }
}