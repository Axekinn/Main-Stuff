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
        private async Task<List<GameMetadata>> ScrapeGames()
        {
            var gameEntries = new List<GameMetadata>();
            var uniqueGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                logger.Info($"Scraping games from: {baseUrl}");

                // Fetch main page content
                string pageContent = await LoadPageContent(baseUrl);
                if (string.IsNullOrEmpty(pageContent))
                {
                    logger.Warn("Failed to retrieve main page content.");
                    return gameEntries;
                }

                logger.Info("Main page content retrieved successfully.");

                // Extract game links
                var links = ExtractGameLinks(pageContent);
                logger.Info($"Found {links.Count} potential game links.");

                foreach (var link in links)
                {
                    // Fetch individual game page content
                    string gamePageContent = await LoadPageContent(link);
                    if (string.IsNullOrEmpty(gamePageContent))
                    {
                        logger.Warn($"Failed to retrieve content for link: {link}");
                        continue;
                    }

                    // Extract game name from the game page
                    string gameName = ExtractGameNameFromPage(gamePageContent);
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
        private string ExtractGameNameFromPage(string pageContent)
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
                    logger.Error($"Failed to load page content: {ex.Message}");
                    return string.Empty;
                }
            }
        }

        /// <summary>
        /// Extract game links matching the AnkerGames pattern.
        /// </summary>
        private List<string> ExtractGameLinks(string pageContent)
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

        /// <summary>
        /// Extract the game name from the URL.
        /// </summary>
        private string ExtractGameNameFromUrl(string url)
        {
            var match = Regex.Match(url, @"\/game\/([a-zA-Z0-9\-]+)$");
            if (match.Success)
            {
                string rawName = match.Groups[1].Value;
                return Uri.UnescapeDataString(rawName.Replace("-", " ")).Trim();
            }
            return string.Empty;
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var games = new List<GameMetadata>();
            var scrapedGames = ScrapeGames().GetAwaiter().GetResult(); // Use the ScrapeGames method tailored for AnkerGames
            logger.Info($"Total scraped game entries: {scrapedGames.Count}");

            foreach (var game in scrapedGames)
            {
                var gameName = game.Name;
                var sanitizedGameName = SanitizePath(gameName);

                // Check if the game already exists in the Playnite library
                if (PlayniteApi.Database.Games.Any(existingGame => existingGame.PluginId == Id && existingGame.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase)))
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
                    logger.Error($"Platform not found for game: {gameName}, Platform: PC (Windows)");
                }
            }

            logger.Info($"Total games prepared for library: {games.Count}");
            return games;
        }

        /// <summary>
        /// Sanitize the path to remove invalid characters.
        /// </summary>
        private string SanitizePath(string path)
        {
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }

        /// <summary>
        /// Check if the game is already in the Playnite library.
        /// </summary>
        private bool IsDuplicate(GameMetadata gameMetadata)
        {
            return PlayniteApi.Database.Games.Any(existingGame =>
                existingGame.PluginId == Id &&
                existingGame.Name.Equals(gameMetadata.Name, StringComparison.OrdinalIgnoreCase));
        }
    }
}