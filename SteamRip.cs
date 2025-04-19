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

namespace SteamRip
{
    public class SteamRip : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        public override Guid Id { get; } = Guid.Parse("13d99643-ebc9-4d63-9ff2-50afd847cfc6");
        public override string Name => "SteamRip";
        private static readonly string baseUrl = "https://steamrip.com/games-list-page/";

        public SteamRip(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties { HasSettings = false };
        }

        private async Task<List<GameMetadata>> ScrapeSite()
        {
            var gameEntries = new List<GameMetadata>();
            var uniqueGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string url = baseUrl;
            logger.Info($"Scraping: {url}");

            string pageContent = await LoadPageContent(url);
            var links = ParseLinks(pageContent);

            foreach (var link in links)
            {
                string href = link.Item1;
                string text = link.Item2;

                if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(text) || !IsValidGameLink(href, text))
                    continue;

                string version = ExtractVersionNumber(text);
                string cleanName = CleanGameName(text);

                if (!string.IsNullOrEmpty(cleanName))
                {
                    var gameKey = $"{cleanName}|{version}";
                    if (uniqueGames.Contains(gameKey))
                        continue;

                    uniqueGames.Add(gameKey);

                    var gameMetadata = new GameMetadata
                    {
                        Name = cleanName,
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                        GameActions = new List<GameAction>
                        {
                            new GameAction
                            {
                                Name = "Download: SteamRip",
                                Type = GameActionType.URL,
                                Path = href.StartsWith("/") ? $"https://steamrip.com{href}" : href,
                                IsPlayAction = false
                            }
                        },
                        Version = version,
                        IsInstalled = false
                    };

                    if (!IsDuplicate(gameMetadata))
                    {
                        gameEntries.Add(gameMetadata);
                    }
                }
            }

            return gameEntries;
        }

        private string ExtractVersionNumber(string name)
        {
            var versionMatch = Regex.Match(name, @"\((.*?)\)");
            if (versionMatch.Success)
            {
                return versionMatch.Groups[1].Value;
            }

            return "0";
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
            var matches = Regex.Matches(pageContent, @"<a\s+(?:[^>]*?\s+)?href=[""'](.*?)[""'].*?>(.*?)</a>");
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
            // Remove version numbers and unwanted characters
            var cleanName = Regex.Replace(name, @"\s*v[\d\.]+.*", "", RegexOptions.IgnoreCase);
            cleanName = Regex.Replace(cleanName, @"\s*\(.*?\)", "", RegexOptions.IgnoreCase).Trim();
            cleanName = cleanName.Replace("&#8217;", "'"); // Fix the apostrophe character
            cleanName = cleanName.Replace("&#8211;", "-");
            cleanName = cleanName.Replace("&#038;", "&"); // Fix the ampersand character
            cleanName = cleanName.Replace("&#8220;", "\""); // Fix the opening quotation mark
            cleanName = cleanName.Replace("&#8221;", "\""); // Fix the closing quotation mark

            // Remove specific phrases
            cleanName = Regex.Replace(cleanName, @"\s*\+\s*Windows 7 Fix", "", RegexOptions.IgnoreCase);
            cleanName = Regex.Replace(cleanName, @"\s*\+\s*Bonus Soundtrack", "", RegexOptions.IgnoreCase);
            cleanName = Regex.Replace(cleanName, @"\s*\+\s*Bonus OST", "", RegexOptions.IgnoreCase);
            cleanName = Regex.Replace(cleanName, @"\s*\+\s*Bonus Content", "", RegexOptions.IgnoreCase);
            cleanName = Regex.Replace(cleanName, @"\s*\+\s*Bonus", "", RegexOptions.IgnoreCase);
            cleanName = Regex.Replace(cleanName, @"Free Download", "", RegexOptions.IgnoreCase);
            cleanName = Regex.Replace(cleanName, @"\s*\+\s*Soundtrack", "", RegexOptions.IgnoreCase);

            // Trim and remove trailing hyphens or other unwanted characters
            cleanName = cleanName.Trim(' ', '-', '–').TrimEnd(',');

            // Remove any trailing parentheses
            cleanName = cleanName.TrimEnd('(').Trim();

            return cleanName;
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
                "https://steamrip.com/contact-us/"
            };

            // Exclude links with specific texts that are not games
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
                
            };

            if (Regex.IsMatch(href, @"^https://steamrip.com\d{4}/\d{2}/$") ||
                Regex.IsMatch(href, @"^https://steamrip.com/\?lcp_page0=\d+#lcp_instance_0$") ||
                nonGameUrls.Contains(href) ||
                nonGameTexts.Any(text.Contains))
            {
                return false;
            }

            return true;
        }

        private bool IsDuplicate(GameMetadata gameMetadata)
        {
            return PlayniteApi.Database.Games.Any(existingGame => existingGame.PluginId == Id && existingGame.Name.Equals(gameMetadata.Name, StringComparison.OrdinalIgnoreCase));
        }

                public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var games = new List<GameMetadata>();
            var scrapedGames = ScrapeSite().GetAwaiter().GetResult();
            logger.Info($"Total repack entries: {scrapedGames.Count}");

            foreach (var game in scrapedGames)
            {
                var gameName = game.Name;
                var sanitizedGameName = SanitizePath(gameName);

                if (PlayniteApi.Database.Games.Any(existingGame => existingGame.PluginId == Id && existingGame.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var platformId = PlayniteApi.Database.Platforms.FirstOrDefault(p => p.Name.Equals(game.Platforms.First().ToString(), StringComparison.OrdinalIgnoreCase))?.Id;
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
                                Name = "Download: SteamRip",
                                Type = GameActionType.URL,
                                Path = game.GameActions.First().Path.StartsWith("/") ? $"https://steamrip.com{game.GameActions.First().Path}" : game.GameActions.First().Path,
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
                    logger.Error($"Platform not found for game: {gameName}, Platform: {game.Platforms.First()}");
                }
            }

            return games;
        }

        private string SanitizePath(string path)
        {
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);
        }
    }
}
