using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace AnkerGames
{
    public class AnkerGames : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        public override Guid Id { get; } = Guid.Parse("f8356d30-7556-485c-acb9-0bb286e2a3d2");
        public override string Name => "AnkerGames";

        private static readonly string baseUrl = "https://ankergames.net/games-list";

        public AnkerGames(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties { HasSettings = false };
        }

        /// <summary>
        /// Scrape games from the AnkerGames website.
        /// </summary>
        private async Task<List<GameMetadata>> AnkerScrapeGames()
        {
            var gameEntries = new List<GameMetadata>();
            var uniqueGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                logger.Info($"Scraping games from: {baseUrl}");

                // Fetch main page content
                string pageContent = await AnkerLoadPageContent(baseUrl);
                if (string.IsNullOrEmpty(pageContent))
                {
                    logger.Warn("Failed to retrieve main page content.");
                    return gameEntries;
                }

                logger.Info("Main page content retrieved successfully.");

                // Extract game links
                var links = AnkerExtractGameLinks(pageContent);
                logger.Info($"Found {links.Count} potential game links.");

                foreach (var link in links)
                {
                    // Fetch individual game page content
                    string gamePageContent = await AnkerLoadPageContent(link);
                    if (string.IsNullOrEmpty(gamePageContent))
                    {
                        logger.Warn($"Failed to retrieve content for link: {link}");
                        continue;
                    }

                    // Extract game name from the game page
                    string gameName = HttpUtility.HtmlDecode(AnkerExtractGameNameFromPage(gamePageContent)); // Decode HTML entities
                    if (string.IsNullOrEmpty(gameName))
                    {
                        logger.Warn($"Could not extract game name from page: {link}");
                        continue;
                    }

                    if (uniqueGames.Contains(gameName.ToLower()))
                    {
                        logger.Info($"Duplicate game found, skipping: {gameName}");
                        continue;
                    }

                    uniqueGames.Add(gameName.ToLower());

                    var gameMetadata = new GameMetadata
                    {
                        Name = gameName,
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                        GameActions = new List<GameAction>
                        {
                            new GameAction
                            {
                                Name = "View on AnkerGames",
                                Type = GameActionType.URL,
                                Path = link,
                                IsPlayAction = false
                            }
                        },
                        IsInstalled = false
                    };

                    gameEntries.Add(gameMetadata);
                    logger.Info($"Game added: {gameName}");
                }

                logger.Info($"Scraping completed. Total games added: {gameEntries.Count}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error during scraping: {ex.Message}");
            }

            return gameEntries;
        }

        /// <summary>
        /// Extract the game name from the game's page content.
        /// </summary>
        private string AnkerExtractGameNameFromPage(string pageContent)
        {
            // Use a regular expression to match the <h3> element with the specified class
            var match = Regex.Match(pageContent, @"<h3 class=""text-xl tracking-tighter font-semibold text-gray-900 dark:text-gray-100 line-clamp-1"">\s*(.+?)\s*</h3>");
            if (match.Success)
            {
                // Return the extracted game name
                return match.Groups[1].Value.Trim();
            }

            logger.Warn("Game name could not be found in the provided page content.");
            return string.Empty;
        }

        /// <summary>
        /// Fetch HTML content of the page.
        /// </summary>
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
                    logger.Error($"Failed to load page content: {ex.Message}");
                    return string.Empty;
                }
            }
        }

        /// <summary>
        /// Extract game links matching the AnkerGames pattern.
        /// </summary>
        private List<string> AnkerExtractGameLinks(string pageContent)
        {
            var links = new List<string>();
            var matches = Regex.Matches(pageContent, @"href=[""'](https:\/\/ankergames\.net\/game\/[a-zA-Z0-9\-]+)[""']", RegexOptions.IgnoreCase);

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

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var games = new List<GameMetadata>();
            var scrapedGames = AnkerScrapeGames().GetAwaiter().GetResult();
            logger.Info($"Total scraped game entries: {scrapedGames.Count}");

            foreach (var game in scrapedGames)
            {
                var gameName = game.Name;
                var sanitizedGameName = AnkerSanitizePath(gameName);

                // Check if the game already exists in the Playnite library
                if (AnkerIsDuplicate(gameName)) // Fixed to prevent being greyed out
                {
                    logger.Info($"Skipping duplicate game: {gameName}");
                    continue;
                }

                // Find the platform ID for "PC (Windows)"
                var platformId = PlayniteApi.Database.Platforms.FirstOrDefault(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase))?.Id;

                if (platformId != null)
                {
                    var gameMetadata = new GameMetadata()
                    {
                        Name = gameName,
                        GameId = gameName.ToLower(),
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                        GameActions = new List<GameAction>
                        {
                            new GameAction
                            {
                                Name = "Download AnkerGames",
                                Type = GameActionType.URL,
                                Path = game.GameActions.First().Path,
                                IsPlayAction = false
                            }
                        },
                        IsInstalled = false,
                        InstallDirectory = null,
                        Icon = new MetadataFile(Path.Combine(sanitizedGameName, "icon.png")),
                        BackgroundImage = new MetadataFile(Path.Combine(sanitizedGameName, "background.png"))
                    };

                    games.Add(gameMetadata);
                    logger.Info($"Game added to library: {gameName}");
                }
                else
                {
                    logger.Error($"Platform not found for game: {gameName}, Platform: PC (Windows)");
                }
            }

            logger.Info($"Total games prepared for library: {games.Count}");
            return games;
        }

        /// <summary>
        /// Sanitize the path to remove invalid characters.
        /// </summary>
        private string AnkerSanitizePath(string path)
        {
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }

        /// <summary>
        /// Check if the game is already in the Playnite library.
        /// </summary>
        private bool AnkerIsDuplicate(string gameName)
        {
            // Fixed logic to prevent being greyed out
            return PlayniteApi.Database.Games.Any(existingGame =>
                existingGame.PluginId == Id &&
                existingGame.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
