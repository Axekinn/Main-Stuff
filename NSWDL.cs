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

        /// <summary>
        /// Scrapes the website for game entries using the same logic as the PowerShell script.
        /// </summary>
        private async Task<List<GameMetadata>> ScrapeSite()
        {
            var gameEntries = new List<GameMetadata>();
            var uniqueUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pagesScraped = 0;

            try
            {
                string currentUrl = BaseUrl;
                while (!string.IsNullOrEmpty(currentUrl))
                {
                    pagesScraped++;
                    logger.Info($"Scraping page {pagesScraped}: {currentUrl}");

                    string pageContent = await LoadPageContent(currentUrl);

                    if (string.IsNullOrWhiteSpace(pageContent))
                    {
                        logger.Warn($"No content found for page {currentUrl}. Ending scraping.");
                        break;
                    }

                    var links = ParseLinks(pageContent);
                    logger.Info($"Page {pagesScraped}: Found {links.Count} hyperlinks.");

                    foreach (var link in links)
                    {
                        string href = link.Item1;
                        string text = link.Item2;

                        if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(text))
                        {
                            logger.Warn($"Skipping invalid hyperlink: Text='{text}', URL='{href}'");
                            continue;
                        }

                        string cleanName = CleanGameName(text);

                        // Use URL as unique identifier to ensure no games are skipped
                        if (!string.IsNullOrEmpty(cleanName) && uniqueUrls.Add(href))
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
                            logger.Info($"Game added: Name='{cleanName}', URL='{href}'");
                        }
                        else
                        {
                            logger.Warn($"Duplicate or invalid game skipped: Name='{cleanName}', URL='{href}'");
                        }
                    }

                    // Find the next page URL if pagination exists
                    currentUrl = GetNextPageUrl(pageContent);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error during scraping: {ex.Message}");
            }

            logger.Info($"Total pages scraped: {pagesScraped}");
            logger.Info($"Total unique games scraped: {gameEntries.Count}");
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
                // Regex to capture all hyperlinks and their text
                var matches = Regex.Matches(pageContent, @"<a\s+href=[""'](https://nswdl\.com/[^""']+)[""'].*?>(.*?)</a>");
                foreach (Match match in matches)
                {
                    string href = match.Groups[1].Value;
                    string text = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value); // Decode text
                    text = Regex.Replace(text, "<.*?>", string.Empty); // Remove any HTML tags inside the text
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
        /// Extracts the URL for the next page in the pagination, if available.
        /// </summary>
        private string GetNextPageUrl(string pageContent)
        {
            try
            {
                var match = Regex.Match(pageContent, @"<a\s+href=[""'](https://nswdl\.com/switch-posts/page/\d+/?)[""'].*?>Next</a>");
                if (match.Success)
                {
                    string nextPageUrl = match.Groups[1].Value;
                    logger.Info($"Next page found: {nextPageUrl}");
                    return nextPageUrl;
                }
                else
                {
                    logger.Info("No next page found. Scraping complete.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error finding next page URL: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fetches games from the scraped site and prepares them for the Playnite library.
        /// </summary>
        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var games = new List<GameMetadata>();
            var scrapedGames = ScrapeSite().GetAwaiter().GetResult();
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
                                Path = game.GameActions.First().Path,
                                IsPlayAction = false
                            }
                        },
                        IsInstalled = false,
                        InstallDirectory = null,
                        Icon = null,
                        BackgroundImage = null
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
