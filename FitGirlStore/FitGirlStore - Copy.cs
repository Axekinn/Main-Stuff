using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading.Tasks;

namespace FitGirlStore
{
    public class FitGirlStore : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        public override Guid Id { get; } = Guid.Parse("5c415b39-d755-4514-9be5-2701d3de94d4");
        public override string Name => "FitGirl Store";
        private static readonly string baseUrl = "https://fitgirl-repacks.site/all-my-repacks-a-z/?lcp_page0=";

        public FitGirlStore(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties { HasSettings = false };
        }

        private async Task<List<GameMetadata>> ScrapeSite()
        {
            var gameEntries = new List<GameMetadata>();
            var uniqueGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int latestPage = await GetLatestPageNumber();

            for (int page = 1; page <= latestPage; page++)
            {
                string url = $"{baseUrl}{page}#lcp_instance_0";
                logger.Info($"Scraping: {url}");

                string pageContent = await LoadPageContent(url);
                var links = ParseLinks(pageContent);

                foreach (var link in links)
                {
                    string href = link.Item1;
                    string text = link.Item2;

                    if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(text) || !IsValidGameLink(href))
                        continue;

                    string version = ExtractVersionNumber(text);
                    string cleanName = CleanGameName(text);

                    if (string.IsNullOrEmpty(cleanName))
                    {
                        cleanName = Regex.Replace(href, @"https://fitgirl-repacks.site/([^/]+)/$", "$1").Replace('-', ' ');
                    }

                    if (!string.IsNullOrEmpty(cleanName) && !href.Contains("page0="))
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
                                    Name = "Download",
                                    Type = GameActionType.URL,
                                    Path = href,
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
            }

            return gameEntries;
        }

        private string ExtractVersionNumber(string name)
        {
            var buildMatch = Regex.Match(name, @"Build (\d+)");
            if (buildMatch.Success)
            {
                return buildMatch.Groups[1].Value;
            }

            var versionMatch = Regex.Match(name, @"v[\d\.]+");
            return versionMatch.Success ? versionMatch.Value : "0";
        }

        private async Task<int> GetLatestPageNumber()
        {
            string homePageContent = await LoadPageContent("https://fitgirl-repacks.site/all-my-repacks-a-z/");
            var paginationLinks = ParseLinks(homePageContent);
            int latestPage = 1;

            foreach (var link in paginationLinks)
            {
                var match = Regex.Match(link.Item1, @"\?lcp_page0=(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int pageNumber) && pageNumber > latestPage)
                {
                    latestPage = pageNumber;
                }
            }

            return latestPage;
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
            cleanName = Regex.Replace(cleanName, @"\s*Build \d+.*", "", RegexOptions.IgnoreCase);
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
            cleanName = Regex.Replace(cleanName, @"\s*\+\s*Soundtrack", "", RegexOptions.IgnoreCase);

            // Trim and remove trailing hyphens or other unwanted characters
            cleanName = cleanName.Trim(' ', '-', '–').TrimEnd(',');

            return cleanName;
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
                "https://wordpress.org/"
            };

            if (Regex.IsMatch(href, @"^https://fitgirl-repacks.site/\d{4}/\d{2}/$") ||
                Regex.IsMatch(href, @"^https://fitgirl-repacks.site/all-my-repacks-a-z/\?lcp_page0=\d+#lcp_instance_0$") ||
                nonGameUrls.Contains(href))
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
                        Name = "Download: Fitgirl",
                        Type = GameActionType.URL,
                        Path = game.GameActions.First().Path,
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