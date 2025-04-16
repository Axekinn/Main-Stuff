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

namespace PS2
{
    public class PS2 : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        public override Guid Id { get; } = Guid.Parse("772ef741-cf7e-4f48-9f01-a80d6121c616");
        public override string Name => "PS2";
        private static readonly string baseUrl = "https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation%202/";

        public PS2(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties { HasSettings = false };
        }

        private async Task<List<GameMetadata>> ScrapeSite()
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

            return gameEntries.Values.ToList();
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
            var matches = Regex.Matches(pageContent, @"<a\s+href=[""'](.*?)[""'].*?>(.*?)</a>");
            foreach (Match match in matches)
            {
                string href = match.Groups[1].Value;
                string text = Regex.Replace(match.Groups[2].Value, "<.*?>", string.Empty); // Remove HTML tags
                links.Add(new Tuple<string, string>(href, text));
            }
            return links;
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



        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var games = new List<GameMetadata>();
            var scrapedGames = ScrapeSite().GetAwaiter().GetResult();
            logger.Info($"Total game entries: {scrapedGames.Count}");

            foreach (var game in scrapedGames)
            {
                if (IsDuplicate(game))
                {
                    logger.Info($"Duplicate game skipped: {game.Name}");
                    continue;
                }

                var platformId = PlayniteApi.Database.Platforms
                    .FirstOrDefault(p => p.Name.Equals(game.Platforms.First().ToString(), StringComparison.OrdinalIgnoreCase))
                    ?.Id;

                if (platformId != null)
                {
                    var sanitizedGameName = SanitizePath(game.Name);
                    var gameMetadata = new GameMetadata()
                    {
                        Name = game.Name,
                        GameId = game.Name.ToLower(),
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("Sony PlayStation 2") },
                        GameActions = new List<GameAction>
         {
             new GameAction
             {
                 Name = "Download: Myrient",
                 Type = GameActionType.URL,
                 Path = game.GameActions.First().Path.StartsWith("/")
                     ? $"{baseUrl}{game.GameActions.First().Path}"
                     : game.GameActions.First().Path,
                 IsPlayAction = false
             }
         },
                        IsInstalled = false,
                        InstallDirectory = null, // Scraped games don't have an install directory
                        Icon = new MetadataFile(Path.Combine(sanitizedGameName, "icon.png")),
                        BackgroundImage = new MetadataFile(Path.Combine(sanitizedGameName, "background.png"))
                    };

                    games.Add(gameMetadata);
                }
                else
                {
                    logger.Error($"Platform not found for game: {game.Name}, Platform: {game.Platforms.First()}");
                }
            }

            return games;
        }

        // SanitizePath method to clean invalid characters from game names
        private string SanitizePath(string path)
        {
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty); // Remove invalid characters
        }

        // Check if a game is a duplicate
        private bool IsDuplicate(GameMetadata gameMetadata)
        {
            return PlayniteApi.Database.Games.Any(existingGame => existingGame.PluginId == Id && existingGame.Name.Equals(gameMetadata.Name, StringComparison.OrdinalIgnoreCase));
        }
    }
    }
