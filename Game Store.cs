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




        public GameStore(IPlayniteAPI api) : base(api)
        {


        }




public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
{
    // Pre-fetch normalized game names already stored in the DB for this plugin.
    var existingNormalizedFromDB = new HashSet<string>(
        PlayniteApi.Database.Games
            .Where(g => g.PluginId == Id)
            .Select(g => NormalizeGameName(g.Name)),
        StringComparer.OrdinalIgnoreCase);

    // Dictionary for merging new games by normalized name.
    var combinedGames = new Dictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

    // Helper to create common image metadata.
    Func<string, (MetadataFile icon, MetadataFile background)> getFiles = gameName =>
    {
        string sanitizedPath = SanitizePath(gameName);
        return (new MetadataFile(Path.Combine(sanitizedPath, "icon.png")),
                new MetadataFile(Path.Combine(sanitizedPath, "background.png")));
    };

    // --- Process STEAMRIP entries ---
    var steamEntries = ScrapeSite().GetAwaiter().GetResult();
    logger.Info($"Total SteamRip entries: {steamEntries.Count}");
    foreach (var game in steamEntries)
    {
        string gameName = game.Name;
        string normalizedKey = NormalizeGameName(gameName);
        // Skip quickly if already in DB.
        if (existingNormalizedFromDB.Contains(normalizedKey))
            continue;

        // If an entry for this normalized key already exists and has a SteamRip action, skip.
        if (combinedGames.TryGetValue(normalizedKey, out var existingEntry) &&
            existingEntry.GameActions.Any(a =>
                a.Name.Equals("Download: SteamRip", StringComparison.OrdinalIgnoreCase)))
            continue;

        var (iconFile, bgFile) = getFiles(gameName);

        if (combinedGames.TryGetValue(normalizedKey, out existingEntry))
        {
            // Add the SteamRip URL action if missing.
            existingEntry.GameActions.Add(new GameAction
            {
                Name = "Download: SteamRip",
                Type = GameActionType.URL,
                Path = game.GameActions.First().Path.StartsWith("/")
                            ? $"https://steamrip.com{game.GameActions.First().Path}"
                            : game.GameActions.First().Path,
                IsPlayAction = false
            });
        }
        else
        {
            var newGame = new GameMetadata
            {
                Name = gameName,
                GameId = normalizedKey.ToLower(),
                Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = "Download: SteamRip",
                        Type = GameActionType.URL,
                        Path = game.GameActions.First().Path.StartsWith("/")
                                ? $"https://steamrip.com{game.GameActions.First().Path}"
                                : game.GameActions.First().Path,
                        IsPlayAction = false
                    }
                },
                Version = game.Version,
                IsInstalled = false,
                Icon = iconFile,
                BackgroundImage = bgFile
            };
            combinedGames[normalizedKey] = newGame;
        }
    }
    
    // --- Process ANKERGAMES entries ---
    var ankerEntries = AnkerScrapeGames().GetAwaiter().GetResult();
    logger.Info($"Total AnkerGames entries: {ankerEntries.Count}");
    foreach (var game in ankerEntries)
    {
        string gameName = game.Name;
        string normalizedKey = NormalizeGameName(gameName);
        if (existingNormalizedFromDB.Contains(normalizedKey))
            continue;

        if (combinedGames.TryGetValue(normalizedKey, out var existingEntry) &&
            existingEntry.GameActions.Any(a =>
                a.Name.Equals("Download: AnkerGames", StringComparison.OrdinalIgnoreCase)))
            continue;

        var (iconFile, bgFile) = getFiles(gameName);

        if (combinedGames.TryGetValue(normalizedKey, out existingEntry))
        {
            existingEntry.GameActions.Add(new GameAction
            {
                Name = "Download: AnkerGames",
                Type = GameActionType.URL,
                Path = game.GameActions.First().Path,
                IsPlayAction = false
            });
            // Optionally update display title if the new version is preferred.
            if (!existingEntry.Name.Contains(":") && gameName.Contains(":"))
            {
                existingEntry.Name = gameName;
                existingEntry.GameId = normalizedKey.ToLower();
            }
        }
        else
        {
            var newGame = new GameMetadata
            {
                Name = gameName,
                GameId = normalizedKey.ToLower(),
                Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = "Download: AnkerGames",
                        Type = GameActionType.URL,
                        Path = game.GameActions.First().Path,
                        IsPlayAction = false
                    }
                },
                IsInstalled = false,
                Icon = iconFile,
                BackgroundImage = bgFile
            };
            combinedGames[normalizedKey] = newGame;
        }
    }

    // --- Process MAGIPACK entries ---
    var magipackEntries = MagipackScrapeGames().GetAwaiter().GetResult();
    logger.Info($"Total Magipack entries: {magipackEntries.Count}");
    foreach (var game in magipackEntries)
    {
        string gameName = game.Name;
        string normalizedKey = NormalizeGameName(gameName);
        if (existingNormalizedFromDB.Contains(normalizedKey) || MagipackIsDuplicate(gameName))
            continue;

        if (combinedGames.TryGetValue(normalizedKey, out var existingEntry) &&
            existingEntry.GameActions.Any(a =>
                a.Name.Equals("Download: Magipack", StringComparison.OrdinalIgnoreCase)))
            continue;

        var (iconFile, bgFile) = getFiles(gameName);

        if (combinedGames.TryGetValue(normalizedKey, out existingEntry))
        {
            existingEntry.GameActions.Add(new GameAction
            {
                Name = "Download: Magipack",
                Type = GameActionType.URL,
                Path = game.GameActions.First().Path,
                IsPlayAction = false
            });
        }
        else
        {
            var newGame = new GameMetadata
            {
                Name = gameName,
                GameId = normalizedKey.ToLower(),
                Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = "Download: Magipack",
                        Type = GameActionType.URL,
                        Path = game.GameActions.First().Path,
                        IsPlayAction = false
                    }
                },
                IsInstalled = false,
                Icon = iconFile,
                BackgroundImage = bgFile
            };
            combinedGames[normalizedKey] = newGame;
        }
    }

    // --- Process ELAMIGOS entries ---
    var elamigosEntries = ElamigosScrapeGames().GetAwaiter().GetResult();
    logger.Info($"Total Elamigos entries: {elamigosEntries.Count}");
    foreach (var game in elamigosEntries)
    {
        string gameName = game.Name;
        string normalizedKey = NormalizeGameName(gameName);
        if (existingNormalizedFromDB.Contains(normalizedKey) || ElamigosIsDuplicate(gameName))
            continue;

        if (combinedGames.TryGetValue(normalizedKey, out var existingEntry) &&
            existingEntry.GameActions.Any(a =>
                a.Name.Equals("Download: Elamigos", StringComparison.OrdinalIgnoreCase)))
            continue;

        var (iconFile, bgFile) = getFiles(gameName);

        if (combinedGames.TryGetValue(normalizedKey, out existingEntry))
        {
            existingEntry.GameActions.Add(new GameAction
            {
                Name = "Download: Elamigos",
                Type = GameActionType.URL,
                Path = game.GameActions.First().Path,
                IsPlayAction = false
            });
            if (!existingEntry.Name.Contains(":") && gameName.Contains(":"))
            {
                existingEntry.Name = gameName;
                existingEntry.GameId = normalizedKey.ToLower();
            }
        }
        else
        {
            var newGame = new GameMetadata
            {
                Name = gameName,
                GameId = normalizedKey.ToLower(),
                Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = "Download: Elamigos",
                        Type = GameActionType.URL,
                        Path = game.GameActions.First().Path,
                        IsPlayAction = false
                    }
                },
                IsInstalled = false,
                Icon = iconFile,
                BackgroundImage = bgFile
            };
            combinedGames[normalizedKey] = newGame;
        }
    }

    // --- Process FITGIRL REPACKS entries ---
    var fitgirlEntries = FitGirlScrapeGames().GetAwaiter().GetResult();
    logger.Info($"Total FitGirl entries: {fitgirlEntries.Count}");
    foreach (var game in fitgirlEntries)
    {
        string gameName = game.Name;
        string normalizedKey = NormalizeGameName(gameName);
        if (existingNormalizedFromDB.Contains(normalizedKey))
            continue;

        if (combinedGames.TryGetValue(normalizedKey, out var existingEntry) &&
            existingEntry.GameActions.Any(a =>
                a.Name.Equals("Download: FitGirl Repacks", StringComparison.OrdinalIgnoreCase)))
            continue;

        var (iconFile, bgFile) = getFiles(gameName);

        if (combinedGames.TryGetValue(normalizedKey, out existingEntry))
        {
            existingEntry.GameActions.Add(new GameAction
            {
                Name = "Download: FitGirl Repacks",
                Type = GameActionType.URL,
                Path = game.GameActions.First().Path,
                IsPlayAction = false
            });
        }
        else
        {
            var newGame = new GameMetadata
            {
                Name = gameName,
                GameId = normalizedKey.ToLower(),
                Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = "Download: FitGirl Repacks",
                        Type = GameActionType.URL,
                        Path = game.GameActions.First().Path,
                        IsPlayAction = false
                    }
                },
                IsInstalled = false,
                Icon = iconFile,
                BackgroundImage = bgFile
            };
            combinedGames[normalizedKey] = newGame;
        }
    }

    logger.Info($"Total combined game entries: {combinedGames.Count}");
    return combinedGames.Values;
}

        private async Task<List<GameMetadata>> ScrapeSite()
        {
            // Build a dictionary for DB games keyed by normalized name.
            var dbGames = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id)
                .ToDictionary(g => NormalizeGameName(g.Name), g => g, StringComparer.OrdinalIgnoreCase);

            // Dictionary for new (scraped) games, keyed by normalized name.
            var scrapedGames = new Dictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

            string url = steamripBaseUrl;
            logger.Info($"Scraping: {url}");

            string pageContent = await LoadPageContent(url);
            if (string.IsNullOrWhiteSpace(pageContent))
            {
                logger.Warn($"No content retrieved from {url}");
                return scrapedGames.Values.ToList();
            }

            var links = ParseLinks(pageContent);
            if (links == null || links.Count == 0)
            {
                logger.Info($"No links found on {url}");
                return scrapedGames.Values.ToList();
            }

            foreach (var link in links)
            {
                string href = link.Item1;
                string text = link.Item2;

                // Skip if either href or text is null/whitespace or the link is not valid.
                if (string.IsNullOrWhiteSpace(href) ||
                    string.IsNullOrWhiteSpace(text) ||
                    !IsValidGameLink(href, text))
                {
                    continue;
                }

                // Optionally extract version.
                string version = ExtractVersionNumber(text);

                // Clean the game title.
                string cleanName = CleanGameName(text);
                if (string.IsNullOrEmpty(cleanName))
                    continue;

                // Generate the normalized key.
                string normalizedKey = NormalizeGameName(cleanName);

                // Prepend domain if href is relative.
                if (href.StartsWith("/"))
                {
                    href = $"https://steamrip.com{href}";
                }

                // Check if this game already exists in the DB.
                if (dbGames.TryGetValue(normalizedKey, out var dbGame))
                {
                    // If the DB game does not already have a "Download: SteamRip" action, add it.
                    if (!dbGame.GameActions.Any(a => a.Name.Equals("Download: SteamRip", StringComparison.OrdinalIgnoreCase)))
                    {
                        dbGame.GameActions.Add(new GameAction
                        {
                            Name = "Download: SteamRip",
                            Type = GameActionType.URL,
                            Path = href,
                            IsPlayAction = false
                        });
                        logger.Info($"Added download action to existing DB game: {cleanName}");
                    }
                    // Skip adding as a new scraped game.
                    continue;
                }

                // Check if the game was scraped already.
                if (!scrapedGames.ContainsKey(normalizedKey))
                {
                    // Create a new game entry.
                    var gameMetadata = new GameMetadata
                    {
                        Name = cleanName,
                        GameId = normalizedKey.ToLower(),
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                        GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = "Download: SteamRip",
                        Type = GameActionType.URL,
                        Path = href,
                        IsPlayAction = false
                    }
                },
                        Version = version,
                        IsInstalled = false
                    };
                    scrapedGames.Add(normalizedKey, gameMetadata);
                }
                else
                {
                    // Duplicate found in scraped games; add the download action if missing.
                    var existingGame = scrapedGames[normalizedKey];
                    if (!existingGame.GameActions.Any(a => a.Name.Equals("Download: SteamRip", StringComparison.OrdinalIgnoreCase)))
                    {
                        existingGame.GameActions.Add(new GameAction
                        {
                            Name = "Download: SteamRip",
                            Type = GameActionType.URL,
                            Path = href,
                            IsPlayAction = false
                        });
                        logger.Info($"Added download action to duplicate scraped game: {cleanName}");
                    }
                }
            }

            return scrapedGames.Values.ToList();
        }

        private async Task<List<GameMetadata>> AnkerScrapeGames()
        {
            // Build a dictionary for faster lookup of DB games keyed by normalized game name.
            var dbGames = PlayniteApi.Database.Games
                            .Where(g => g.PluginId == Id)
                            .ToDictionary(g => NormalizeGameName(g.Name), g => g, StringComparer.OrdinalIgnoreCase);

            // Use a concurrent dictionary for newly scraped games.
            var scrapedGames = new ConcurrentDictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

            try
            {
                logger.Info($"Scraping games from: {ankerBaseUrl}");

                // Fetch the main page content.
                string pageContent = await AnkerLoadPageContent(ankerBaseUrl).ConfigureAwait(false);
                if (string.IsNullOrEmpty(pageContent))
                {
                    logger.Warn("Failed to retrieve main page content from AnkerGames.");
                    return scrapedGames.Values.ToList();
                }
                logger.Info("Main page content retrieved successfully.");

                // Extract game links (assume AnkerExtractGameLinks returns a List<string> of URLs).
                var links = AnkerExtractGameLinks(pageContent);
                logger.Info($"Found {links.Count} potential game links.");

                // Increase max concurrency if your system/network can handle more.
                int maxConcurrency = 20;
                using (var semaphore = new SemaphoreSlim(maxConcurrency))
                {
                    var tasks = links.Select(async link =>
                    {
                        await semaphore.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            // Fetch the individual game page content.
                            string gamePageContent = await AnkerLoadPageContent(link).ConfigureAwait(false);
                            if (string.IsNullOrWhiteSpace(gamePageContent))
                            {
                                logger.Warn($"Failed to retrieve content for link: {link}");
                                return;
                            }

                            // Extract the raw game name from the page.
                            string rawGameName = AnkerExtractGameNameFromPage(gamePageContent);
                            if (string.IsNullOrWhiteSpace(rawGameName))
                            {
                                logger.Warn($"Could not extract game name from page: {link}");
                                return;
                            }

                            // Decode HTML entities and normalize the game name.
                            string gameName = WebUtility.HtmlDecode(rawGameName);
                            string normalizedKey = NormalizeGameName(gameName);

                            // Check if the game exists in the database.
                            if (dbGames.TryGetValue(normalizedKey, out var dbGame))
                            {
                                // If the DB game does not already have a "Download: AnkerGames" action, add it.
                                if (!dbGame.GameActions.Any(a => a.Name.Equals("Download: AnkerGames",
                                                                              StringComparison.OrdinalIgnoreCase)))
                                {
                                    dbGame.GameActions.Add(new GameAction
                                    {
                                        Name = "Download: AnkerGames",
                                        Type = GameActionType.URL,
                                        Path = link,
                                        IsPlayAction = false
                                    });
                                    logger.Info($"Added new download action for DB game: {gameName}");
                                }
                                else
                                {
                                    logger.Info($"Skipping duplicate DB game (action exists): {gameName}");
                                }
                                return;
                            }

                            // Check if we've already scraped an entry for this game.
                            if (scrapedGames.TryGetValue(normalizedKey, out GameMetadata existingGame))
                            {
                                // If the scraped game does not already have a "Download: AnkerGames" action, add it.
                                if (!existingGame.GameActions.Any(a => a.Name.Equals("Download: AnkerGames", StringComparison.OrdinalIgnoreCase)))
                                {
                                    lock (existingGame.GameActions)
                                    {
                                        existingGame.GameActions.Add(new GameAction
                                        {
                                            Name = "Download: AnkerGames",
                                            Type = GameActionType.URL,
                                            Path = link,
                                            IsPlayAction = false
                                        });
                                    }
                                    logger.Info($"Added new download action for scraped game: {gameName}");
                                }
                                else
                                {
                                    logger.Info($"Scraped duplicate already has action: {gameName}");
                                }
                                return;
                            }

                            // Otherwise, create a new game metadata entry.
                            string sanitizedGameName = AnkerSanitizePath(gameName);
                            var gameMetadata = new GameMetadata
                            {
                                Name = gameName,
                                GameId = normalizedKey.ToLower(),
                                Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                                GameActions = new List<GameAction>
                        {
                            new GameAction
                            {
                                Name = "Download: AnkerGames",
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

                            if (scrapedGames.TryAdd(normalizedKey, gameMetadata))
                            {
                                logger.Info($"Added new game entry: {gameName}");
                            }
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

        private async Task<List<GameMetadata>> MagipackScrapeGames()
        {
            // Build a dictionary for DB games (type Game) keyed by normalized game name.
            var dbGames = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id)
                .ToDictionary(g => NormalizeGameName(g.Name), g => g, StringComparer.OrdinalIgnoreCase);

            // Dictionary for new (scraped) games (of type GameMetadata).
            var scrapedGames = new Dictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

            logger.Info($"Scraping games from: {magipackBaseUrl}");

            // Fetch main page content.
            string pageContent = await LoadPageContent(magipackBaseUrl);
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

            foreach (var link in links)
            {
                string href = link.Item1;
                string text = link.Item2;

                // Skip if either href or text is null/whitespace or link is invalid.
                if (string.IsNullOrWhiteSpace(href) ||
                    string.IsNullOrWhiteSpace(text) ||
                    !IsValidGameLink(href))
                    continue;

                // Clean the game title.
                string cleanName = CleanGameName(text);

                // Use fallback if cleaning produced an empty name.
                if (string.IsNullOrEmpty(cleanName))
                {
                    cleanName = fallbackRegex.Replace(href, "$1").Replace('-', ' ').Trim();
                }
                if (string.IsNullOrEmpty(cleanName))
                    continue;

                // Generate normalized key for duplicate checking.
                string normalizedKey = NormalizeGameName(cleanName);

                // First, if the game exists in the DB, update its GameActions if needed.
                if (dbGames.TryGetValue(normalizedKey, out var dbGame))
                {
                    if (!dbGame.GameActions.Any(a => a.Name.Equals("Download: Magipack", StringComparison.OrdinalIgnoreCase)))
                    {
                        dbGame.GameActions.Add(new GameAction
                        {
                            Name = "Download: Magipack",
                            Type = GameActionType.URL,
                            Path = href,
                            IsPlayAction = false
                        });
                        logger.Info($"Added download action to existing DB game: {cleanName}");
                    }
                    continue;
                }

                // Otherwise, process as a new, scraped game.
                if (!scrapedGames.ContainsKey(normalizedKey))
                {
                    var gameMetadata = new GameMetadata
                    {
                        Name = cleanName,
                        GameId = normalizedKey.ToLower(),
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                        GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = "Download: Magipack",
                        Type = GameActionType.URL,
                        Path = href,
                        IsPlayAction = false
                    }
                },
                        IsInstalled = false
                    };
                    scrapedGames.Add(normalizedKey, gameMetadata);
                }
                else
                {
                    var existingGame = scrapedGames[normalizedKey];
                    if (!existingGame.GameActions.Any(a => a.Name.Equals("Download: Magipack", StringComparison.OrdinalIgnoreCase)))
                    {
                        existingGame.GameActions.Add(new GameAction
                        {
                            Name = "Download: Magipack",
                            Type = GameActionType.URL,
                            Path = href,
                            IsPlayAction = false
                        });
                        logger.Info($"Added download action to duplicate scraped game: {cleanName}");
                    }
                }
            }

            logger.Info($"Magipack scraping completed. New games added: {scrapedGames.Count}");
            return scrapedGames.Values.ToList();
        }


        private async Task<List<GameMetadata>> ElamigosScrapeGames()
        {
            // Build a dictionary for DB games keyed by normalized game name.
            var dbGames = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id)
                .ToDictionary(g => NormalizeGameName(g.Name), g => g, StringComparer.OrdinalIgnoreCase);

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

            foreach (Match match in matches)
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
                    continue;

                string displayName = cleanName;
                string normalizedName = NormalizeGameName(cleanName);

                // First, if the game exists in the DB, update its GameActions if needed.
                if (dbGames.TryGetValue(normalizedName, out var dbGame))
                {
                    if (!dbGame.GameActions.Any(a =>
                           a.Name.Equals("Download: elAmigos", StringComparison.OrdinalIgnoreCase)))
                    {
                        dbGame.GameActions.Add(new GameAction
                        {
                            Name = "Download: elAmigos",
                            Type = GameActionType.URL,
                            Path = href,
                            IsPlayAction = false
                        });
                        logger.Info($"Added download action to existing DB game: {displayName}");
                        // Note: There is no UpdateGame method available,
                        // so we assume modifying dbGame in memory is sufficient.
                    }
                    continue;
                }

                // If game is not in the DB, check our scrapedGames dictionary.
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
                        Name = "Download: elAmigos",
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
                           a.Name.Equals("Download: elAmigos", StringComparison.OrdinalIgnoreCase)))
                    {
                        existingGame.GameActions.Add(new GameAction
                        {
                            Name = "Download: elAmigos",
                            Type = GameActionType.URL,
                            Path = href,
                            IsPlayAction = false
                        });
                        logger.Info($"Added download action to duplicate scraped game: {displayName}");
                    }
                }
            }

            logger.Info($"ElAmigos scraping completed. New games added: {scrapedGames.Count}");
            return scrapedGames.Values.ToList();
        }


        private async Task<List<GameMetadata>> FitGirlScrapeGames()
        {
            // Create a dictionary for DB games keyed by normalized name.
            var dbGames = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id)
                .ToDictionary(g => NormalizeGameName(g.Name), g => g, StringComparer.OrdinalIgnoreCase);

            // Use a concurrent dictionary for new (scraped) games.
            var scrapedGames = new ConcurrentDictionary<string, GameMetadata>(StringComparer.OrdinalIgnoreCase);

            // Get the latest page number.
            int latestPage = await GetLatestPageNumber();
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
                    string pageContent = await LoadPageContent(url);
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

                    // Process each game link on the page.
                    foreach (var link in links)
                    {
                        string href = link.Item1;
                        string text = link.Item2;

                        // Quick checks.
                        if (string.IsNullOrWhiteSpace(href) ||
                            string.IsNullOrWhiteSpace(text) ||
                            !IsValidGameLink(href))
                            continue;

                        // Skip internal pagination links.
                        if (href.Contains("page0="))
                            continue;

                        // Clean the game title (no fallback is applied).
                        string cleanName = CleanGameName(text);
                        if (string.IsNullOrEmpty(cleanName))
                            continue;

                        // Generate a normalized key.
                        string normalizedName = NormalizeGameName(cleanName);

                        // First, if the game exists in the DB, update its GameActions if needed.
                        if (dbGames.TryGetValue(normalizedName, out var dbGame))
                        {
                            if (!dbGame.GameActions.Any(a => a.Name.Equals("Download: FitGirl Repacks", StringComparison.OrdinalIgnoreCase)))
                            {
                                dbGame.GameActions.Add(new GameAction
                                {
                                    Name = "Download: FitGirl Repacks",
                                    Type = GameActionType.URL,
                                    Path = href,
                                    IsPlayAction = false
                                });
                                logger.Info($"Added download action to existing DB game: {cleanName}");
                            }
                            // Skip adding as a new scraped game.
                            continue;
                        }

                        // Otherwise, process as a new (scraped) game.
                        if (!scrapedGames.ContainsKey(normalizedName))
                        {
                            var gameMetadata = new GameMetadata
                            {
                                Name = cleanName,
                                GameId = normalizedName.ToLower(),
                                Platforms = new HashSet<MetadataProperty>
                        {
                            new MetadataSpecProperty("PC (Windows)")
                        },
                                GameActions = new List<GameAction>
                        {
                            new GameAction
                            {
                                Name = "Download: FitGirl Repacks",
                                Type = GameActionType.URL,
                                Path = href,
                                IsPlayAction = false
                            }
                        },
                                IsInstalled = false
                            };
                            scrapedGames.TryAdd(normalizedName, gameMetadata);
                        }
                        else
                        {
                            // Duplicate among scraped games; add the action if it doesn't exist.
                            var existingGame = scrapedGames[normalizedName];
                            if (!existingGame.GameActions.Any(a => a.Name.Equals("Download: FitGirl Repacks", StringComparison.OrdinalIgnoreCase)))
                            {
                                existingGame.GameActions.Add(new GameAction
                                {
                                    Name = "Download: FitGirl Repacks",
                                    Type = GameActionType.URL,
                                    Path = href,
                                    IsPlayAction = false
                                });
                                logger.Info($"Added download action to duplicate scraped game: {cleanName}");
                            }
                        }
                    }
                }));
            }

            // Wait for all page tasks to complete.
            await Task.WhenAll(tasks);

            logger.Info($"FitGirl scraping completed. Total new games added: {scrapedGames.Count}");
            return scrapedGames.Values.ToList();
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
            // (We remove everything except letters, digits, and whitespace.
            // This will remove colons, dots, dashes, etc. for duplicate-checking.)
            normalized = Regex.Replace(normalized, @"[^\w\s]", " ");

            // 5. Collapse multiple spaces.
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            // 6. Special rule for Marvel: if it starts with "marvels", change it to "marvel".
            normalized = Regex.Replace(normalized, @"^marvels\b", "marvel", RegexOptions.IgnoreCase);

            // 7. Normalize "Game of The Year Edition" variations.
            // Replace phrases like "game of the year" optionally followed by "edition" with "goty".
            normalized = Regex.Replace(normalized, @"\bgame of (the year)( edition)?\b", "goty", RegexOptions.IgnoreCase);

            // 8. Normalize the acronym REPO:
            // Whether the title appears as "repo" or "r.e.p.o", force it to "r.e,p.o".
            normalized = Regex.Replace(normalized, @"\br\.?e\.?p\.?o\b", "r.e,p.o", RegexOptions.IgnoreCase);

            // 9. Normalize the acronym FEAR:
            // Match variants such as "fear", "f.e.a.r", or even with spaces ("f e a r") and force it to "fear".
            normalized = Regex.Replace(normalized, @"\bf\s*e\s*a\s*r\b", "fear", RegexOptions.IgnoreCase);

            // 10. Normalize Rick and Morty Virtual Rick-ality VR variants.
            // Remove an optional colon between "rick and morty" and the rest.
            normalized = Regex.Replace(normalized,
                @"rick and morty\s*[:]?\s*virtual rickality vr",
                "rick and morty virtual rickality vr", RegexOptions.IgnoreCase);

            // 11. (Optional) Normalize common Roman numerals.
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

        // Elamigos

        private List<string> AnkerExtractGameLinks(string pageContent)
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
        private bool ElamigosIsDuplicate(string gameName)
        {
            return PlayniteApi.Database.Games.Any(existing =>
                existing.PluginId == Id &&
                existing.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
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
                    // Search for a local repack candidate.
                    var (candidatePath, isArchive, fileSize, candidateFound) = await SearchForLocalRepackAsync(Game.Name);
                    if (candidateFound)
                    {
                        if (string.IsNullOrEmpty(candidatePath))
                        {
                            // Local installation was aborted.
                            return;
                        }
                        else
                        {
                            if (isArchive)
                            {
                                // For an archive candidate (ZIP/RAR):
                                // Ask the user for a target drive.
                                string selectedDrive = ShowDriveSelectionDialog(fileSize);
                                if (string.IsNullOrEmpty(selectedDrive))
                                {
                                    return;
                                }

                                // Build the target install directory as "Drive:\Games\{Game.Name}"
                                string gamesFolder = Path.Combine($"{selectedDrive}:", "Games");
                                Directory.CreateDirectory(gamesFolder);
                                string targetInstallDir = Path.Combine(gamesFolder, Game.Name);
                                Directory.CreateDirectory(targetInstallDir);

                                // Extract the archive candidate into the target folder.
                                string sevenZipExe = Get7ZipPath();  // Helper: finds 7z.exe path from PATH or default locations.
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

                                // Now update the game's install directory and game actions.
                                Game.InstallDirectory = targetInstallDir;
                                playniteApi.Database.Games.Update(Game);
                                LogToInstall("Updated InstallDir to: " + targetInstallDir + " (archive candidate).");
                                UpdateGameActionsAndStatus(Game, GetPluginUserDataPathLocal());
                                return;
                            }
                            else
                            {
                                // For a folder candidate, we assume it contains Setup.exe.
                                string setupExePath = Path.Combine(candidatePath, "Setup.exe");
                                if (System.IO.File.Exists(setupExePath))
                                {
                                    // Run Setup.exe asynchronously.
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

                                // After Setup.exe completes, search all drives for the installed game folder.
                                // The Setup.exe is assumed to create an installation folder like "Games/{GameName}".
                                string installDir = SearchForGameInstallDirectory(Game.Name);
                                if (string.IsNullOrEmpty(installDir))
                                {
                                    playniteApi.Dialogs.ShowErrorMessage("Could not locate the installation folder after running Setup.exe.", "Installation Error");
                                    return;
                                }
                                LogToInstall("Found installed directory: " + installDir);

                                // Update the game's install directory and perform update of game actions.
                                Game.InstallDirectory = installDir;
                                playniteApi.Database.Games.Update(Game);
                                LogToInstall("Updated InstallDir to: " + installDir + " (after running Setup.exe).");
                                UpdateGameActionsAndStatus(Game, GetPluginUserDataPathLocal());
                                return;
                            }
                        }
                    }
                    // If no local repack candidate was found, do nothing.
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
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady)
                        continue;

                    // Build the candidate path under each drive.
                    string gamesFolder = Path.Combine(drive.RootDirectory.FullName, "Games");
                    if (!Directory.Exists(gamesFolder))
                        continue;

                    string candidate = Path.Combine(gamesFolder, gameName);
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                }
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

                    // --- Menu: Ask the user which action to take  ---
                    string userChoice = "";
                    if (candidateFound)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            userChoice = ShowInstallDownloadCancelDialog();
                        });
                        LogToInstall("User choice from Install/Download/Cancel: " + userChoice);
                    }

                    // If candidate was found but user did not choose "Install", then abort (and do not fall back to online download).
                    if (candidateFound && (string.IsNullOrEmpty(userChoice) || userChoice != "Install"))
                    {
                        LogToInstall("User did not choose Install. Aborting repack installation.");
                        candidatePath = null;
                        return (candidatePath, isArchive, fileSize, candidateFound);
                    }

                    // --- If user chose "Install" and candidate is a folder, no extraction is needed ---
                    if (!isArchive)
                    {
                        LogToInstall("Candidate is a folder, no extraction needed.");
                        return (candidatePath, isArchive, fileSize, candidateFound);
                    }

                    // --- For archive candidates, ask for target drive and extract the repack ---
                    string selectedDrive = "";
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        selectedDrive = ShowDriveSelectionDialog((long)Math.Ceiling(fileSize * 1.2));
                    });
                    LogToInstall("Selected drive for extraction: " + selectedDrive);

                    if (string.IsNullOrEmpty(selectedDrive))
                    {
                        LogToInstall("No drive selected. Aborting extraction.");
                        candidatePath = null;
                        return (candidatePath, isArchive, fileSize, candidateFound);
                    }

                    // Build target output folder: e.g., "D:\Games\<gameName>"
                    string driveGamesFolder = $"{selectedDrive}:" + Path.DirectorySeparatorChar + "Games";
                    System.IO.Directory.CreateDirectory(driveGamesFolder);
                    string outputFolder = Path.Combine(driveGamesFolder, gameName);
                    System.IO.Directory.CreateDirectory(outputFolder);
                    LogToInstall("Output folder created: " + outputFolder);

                    // Verify candidate archive exists.
                    if (!System.IO.File.Exists(candidatePath))
                    {
                        LogToInstall("Candidate archive not found: " + candidatePath);
                        candidatePath = null;
                        return (candidatePath, isArchive, fileSize, candidateFound);
                    }

                    // Prepare extraction using 7‑Zip.
                    string arguments = $"x \"{candidatePath}\" -o\"{outputFolder}\" -y";
                    string sevenZipExe = "7z.exe"; // try system PATH
                    if (!System.IO.File.Exists(sevenZipExe))
                    {
                        // Try default install paths.
                        sevenZipExe = @"C:\Program Files\7-Zip\7z.exe";
                        if (!System.IO.File.Exists(sevenZipExe))
                        {
                            sevenZipExe = @"C:\Program Files (x86)\7-Zip\7z.exe";
                            if (!System.IO.File.Exists(sevenZipExe))
                            {
                                LogToInstall("7z.exe not found in PATH or default install locations.");
                                candidatePath = null;
                                return (candidatePath, isArchive, fileSize, candidateFound);
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
                        // Update candidatePath to extracted folder.
                        candidatePath = outputFolder;
                        isArchive = false;
                        LogToInstall("Extraction succeeded. Candidate repack now at: " + candidatePath);
                    }
                    catch (Exception ex)
                    {
                        LogToInstall("Extraction failed: " + ex.Message);
                        candidatePath = null;
                        return (candidatePath, isArchive, fileSize, candidateFound);
                    }

                    // --- Follow-up Menu: After extraction, prompt with "Install Redsits" (Yes/No)  ---
                    string redistChoice = "";
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        redistChoice = ShowYesNoDialog("Install Redsits?", "Post Extraction");
                    });
                    LogToInstall("Redist installation choice: " + redistChoice);
                    if (redistChoice.Equals("Yes", StringComparison.OrdinalIgnoreCase))
                    {
                        // Look for the _CommonRedist folder inside the extracted folder.
                        string redistsFolder = Path.Combine(candidatePath, "_CommonRedist");
                        LogToInstall("Attempting to install redists from: " + redistsFolder);
                        // Call your existing redists installer.
                        // (This method should silently install all .exe files found in the folder if not already installed.)
                        // Note: InstallRedistsAsync should be written to check and skip already installed files.
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            await InstallRedistsAsync(redistsFolder);
                        }).Wait();
                    }
                    else
                    {
                        LogToInstall("User chose not to install redists.");
                    }

                    // --- Now, prompt with a "Delete Repack?" dialog  ---
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
                            // We now set candidatePath to empty because it's been deleted.
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
                // Load the exclusions from "exclusions.txt"
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

                // Get all .exe files recursively from the game's InstallDirectory.
                var exeFiles = Directory.GetFiles(game.InstallDirectory, "*.exe", SearchOption.AllDirectories);
                foreach (var exeFile in exeFiles)
                {
                    // Get the relative path from the install directory
                    string relativePath = GetRelativePathCustom(game.InstallDirectory, exeFile);

                    // Split the relative path into segments.
                    var segments = relativePath.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

                    // Skip this exe if any folder in its relative path contains "redist" or "redsit" (case-insensitive).
                    bool skipDueToRedist = segments.Any(seg =>
                        seg.ToLower().Contains("redist") || seg.ToLower().Contains("redsit"));
                    if (skipDueToRedist)
                    {
                        continue;
                    }

                    // Get the exe file name (without extension) in lower-case.
                    var exeName = Path.GetFileNameWithoutExtension(exeFile).ToLower();
                    if (exclusions.Contains(exeName))
                    {
                        continue;
                    }

                    // Avoid duplicate actions.
                    if (game.GameActions.Any(a => a.Name.Equals(Path.GetFileNameWithoutExtension(exeFile), StringComparison.OrdinalIgnoreCase)))
                    {
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
                    LogToInstall("Added new game action for exe: " + exeFile);
                }

                API.Instance.Database.Games.Update(game);

                // Signal that installation is completed.
                InvokeOnInstalled(new GameInstalledEventArgs(game.Id));

                // Force library update for the specific game.
                var pluginGames = API.Instance.Database.Games;
                var updatedGame = pluginGames.FirstOrDefault(g =>
                    g.Name.Equals(game.Name, StringComparison.OrdinalIgnoreCase));
                if (updatedGame != null)
                {
                    game.InstallDirectory = updatedGame.InstallDirectory;
                    game.GameActions = new ObservableCollection<GameAction>(updatedGame.GameActions);
                    API.Instance.Database.Games.Update(game);
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
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c start \"\" \"{url}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
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

            // Main download handler for Elamigos.
            private async Task HandleElamigosDownload()
            {
                // Look for the download action with the proper name.
                var downloadAction = Game.GameActions
                    .FirstOrDefault(a => a.Name.Equals("Download: ElAmigos", StringComparison.OrdinalIgnoreCase));
                if (downloadAction == null || string.IsNullOrEmpty(downloadAction.Path))
                {
                    playniteApi.Dialogs.ShowErrorMessage("Invalid source URL selected.", "Error");
                    return;
                }

                string gameUrl = downloadAction.Path;

                // Scrape the download page to get provider groups (e.g. "DDOWNLOAD", "RAPIDGATOR").
                Dictionary<string, List<string>> groups = await ElamigosScrapeDownloadProviderGroupsAsync(Game.Name, gameUrl);
                if (groups == null || groups.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"No provider groups found for {Game.Name}.", "Download Error");
                    return;
                }

                // First dialog: let the user select a group.
                string[] groupOptions = groups.Keys.ToArray();
                string selectedGroup = ElamigosShowGroupSelectionDialog("Select Provider Group", groupOptions);
                if (string.IsNullOrEmpty(selectedGroup))
                {
                    // User cancelled.
                    return;
                }

                // Build a provider dictionary for the selected group.
                List<string> groupLinks = groups[selectedGroup];
                // You can also use the helper method below:
                // Dictionary<string, string> providerDict = ElamigosBuildProviderDictionary(groupLinks);
                Dictionary<string, string> providerDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string link in groupLinks)
                {
                    string provider = ElamigosGetProviderName(link);
                    if (!providerDict.ContainsKey(provider))
                    {
                        providerDict.Add(provider, link);
                    }
                }
                if (providerDict.Count == 0)
                {
                    playniteApi.Dialogs.ShowErrorMessage("No recognized providers were found in the selected group.", "Provider Error");
                    return;
                }

                // Second dialog: let the user select a provider within the group.
                string selectedProvider = ElamigosShowProviderSelectionDialog(providerDict);
                if (string.IsNullOrEmpty(selectedProvider))
                {
                    // User cancelled.
                    return;
                }

                // Open the download link for the selected provider.
                if (providerDict.TryGetValue(selectedProvider, out string providerUrl))
                {
                    await ElamigosOpenDownloadLinkForProviderAsync(selectedProvider, providerUrl);
                }
                else
                {
                    playniteApi.Dialogs.ShowErrorMessage("Selected provider was not found.", "Selection Error");
                }
            }


            private async Task<Dictionary<string, List<string>>> ElamigosScrapeDownloadProviderGroupsAsync(string gameName, string gameUrl)
            {
                var providerGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
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

                    // Remove all HTML tags.
                    string plainText = Regex.Replace(pageContent, "<.*?>", "").Trim();
                    string[] lines = plainText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    // Process each line:
                    // If a line contains URL(s) (extracted via regex) then process each; otherwise, treat it as a new group header.
                    string currentGroup = null;
                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed))
                            continue;

                        // Use regex to extract all URLs from the line.
                        var urlMatches = Regex.Matches(trimmed, @"https?://\S+");
                        if (urlMatches.Count > 0)
                        {
                            foreach (Match match in urlMatches)
                            {
                                string url = match.Value.Trim();
                                // Skip any URL that contains YouTube.
                                if (url.IndexOf("youtube.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    url.IndexOf("youtu.be", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    continue;
                                }

                                // If there's a valid current group, add the URL.
                                if (!string.IsNullOrEmpty(currentGroup))
                                {
                                    if (!providerGroups.ContainsKey(currentGroup))
                                    {
                                        providerGroups[currentGroup] = new List<string>();
                                    }
                                    providerGroups[currentGroup].Add(url);
                                }
                            }
                        }
                        else
                        {
                            // No URL found in this line: treat it as a new group header.
                            currentGroup = trimmed;
                            if (!providerGroups.ContainsKey(currentGroup))
                            {
                                providerGroups[currentGroup] = new List<string>();
                            }
                        }
                    }

                    return providerGroups;
                }
                catch (Exception ex)
                {
                    playniteApi.Dialogs.ShowErrorMessage($"Error while scraping provider groups: {ex.Message}", "Scraping Error");
                    return providerGroups;
                }
            }

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
                // Add a cancel option.
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

            // Displays a provider selection dialog for Elamigos and returns the chosen provider.
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

                    // Look for anchor tags with protocol-less href attributes for known providers.
                    var matches = Regex.Matches(pageContent,
                        @"<a\s+href=[""'](//(?:megadb\.net|gofile\.io|1fichier\.com|filecrypt\.co|buzzheavier\.com)[^\s""']+)[""']");
                    List<string> links = matches.Cast<Match>()
                                                .Select(m => "https:" + m.Groups[1].Value.Trim())
                                                .ToList();

                    // Log found links.
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
