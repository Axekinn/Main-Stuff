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

namespace FitGirlStore
{
    public class FitGirlStore : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        public override Guid Id { get; } = Guid.Parse("5c415b39-d755-4514-9be5-2701d3de94d4");
        public override string Name => "FitGirl Store";
        private static readonly string baseUrl = "https://fitgirl-repacks.site/all-my-repacks-a-z/?lcp_page0=";
        private static readonly string logFilePath = "Games.log";


        public FitGirlStore(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties { HasSettings = false };
        }

        private void LogGameInfo(string gameName, string version)
        {
            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                writer.WriteLine($"Scraped Game: {gameName}");
                writer.WriteLine($"Version/Build: {version}");
                writer.WriteLine();
            }
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
                                    Name = "Download: Fitgirl",
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
            cleanName = cleanName.Replace("&#8216;", "‘");
            cleanName = cleanName.Replace("&#038;", "&"); // Fix the ampersand character
            cleanName = cleanName.Replace("&#8220;", "\""); // Fix the opening quotation mark
            cleanName = cleanName.Replace("&#8221;", "\""); // Fix the closing quotation mark

            // Remove specific phrases
            var phrasesToRemove = new string[]
            {
            "Windows 7 Fix", "Bonus Soundtrack", "Bonus OST", "Ultimate Fishing Bundle",
            "Digital Deluxe Edition", "Bonus Content", "Bonus", "Soundtrack",
            "2 DLCs", "2 DLC", "All DLCs", "HotFix", "HotFix 1", "Multiplayer" , "DLCs",
            };

            foreach (var phrase in phrasesToRemove)
            {
                cleanName = Regex.Replace(cleanName, $@"\s*\+\s*{Regex.Escape(phrase)}", "", RegexOptions.IgnoreCase);
                cleanName = Regex.Replace(cleanName, $@"- {Regex.Escape(phrase)}", "", RegexOptions.IgnoreCase);
            }

            // Remove text in parentheses or square brackets
            cleanName = Regex.Replace(cleanName, @"[\[\(].*?[\]\)]", "").Trim();

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
            // Use the original name for comparison
            return PlayniteApi.Database.Games.Any(existingGame => existingGame.PluginId == Id && existingGame.Name.Equals(gameMetadata.Name, StringComparison.OrdinalIgnoreCase));
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var games = new List<GameMetadata>();

            // Check installation of existing uninstalled Games
            var existingGames = PlayniteApi.Database.Games.Where(g => g.PluginId == Id).ToList();
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Network));
            var exclusions = LoadExclusions();
            var gameFolders = new List<string>();
            var repackFolders = new List<string>();

            foreach (var drive in drives)
            {
                var gamesFolderPath = Path.Combine(drive.RootDirectory.FullName, "Games");
                var repacksFolderPath = Path.Combine(drive.RootDirectory.FullName, "Repacks");

                if (Directory.Exists(gamesFolderPath))
                {
                    gameFolders.AddRange(Directory.GetDirectories(gamesFolderPath));
                }

                if (Directory.Exists(repacksFolderPath))
                {
                    repackFolders.AddRange(Directory.GetDirectories(repacksFolderPath));
                }
            }

            foreach (var existingGame in existingGames)
            {
                var folder = gameFolders.FirstOrDefault(f => CleanGameName(Path.GetFileName(f)).Equals(existingGame.Name, StringComparison.OrdinalIgnoreCase));
                if (folder == null)
                {
                    existingGame.InstallDirectory = null;
                    existingGame.IsInstalled = false;
                    API.Instance.Database.Games.Update(existingGame);
                    logger.Info($"Marked game as uninstalled: {existingGame.Name}");
                }
                else
                {
                    var exeFiles = Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories)
                                            .Where(exe => !exclusions.Contains(Path.GetFileName(exe)) &&
                                                          !Path.GetFileName(exe).ToLower().Contains("setup") &&
                                                          !Path.GetFileName(exe).ToLower().Contains("unins"));

                    logger.Info($"Found {exeFiles.Count()} executable files for game: {existingGame.Name} in folder: {folder}");
                    foreach (var exe in exeFiles)
                    {
                        logger.Info($"Found executable: {exe}");
                    }

                    if (!exeFiles.Any())
                    {
                        logger.Warn($"No valid executable files found for game: {existingGame.Name} in folder: {folder}");
                        continue;
                    }

                    existingGame.InstallDirectory = folder;
                    existingGame.IsInstalled = true;
                    logger.Info($"Updated install directory for existing game: {existingGame.Name} to {folder}");

                    foreach (var exe in exeFiles)
                    {
                        if (!existingGame.GameActions.Any(action => action.Path.Equals(exe, StringComparison.OrdinalIgnoreCase)))
                        {
                            existingGame.GameActions.Add(new GameAction()
                            {
                                Type = GameActionType.File,
                                Path = exe,
                                Name = Path.GetFileNameWithoutExtension(exe),
                                IsPlayAction = true,
                                WorkingDir = folder
                            });
                            logger.Info($"Added play action: {exe} to existing game: {existingGame.Name}");
                        }
                    }

                    API.Instance.Database.Games.Update(existingGame);
                    logger.Info($"Updated existing game: {existingGame.Name} with new actions and install directory.");
                }
            }

            // Scrape Games
            var scrapedGames = ScrapeSite().GetAwaiter().GetResult();
            logger.Info($"Total repack entries: {scrapedGames.Count}");

            // Add scraped games to the Playnite database and check if they are in the "Games" folder
            foreach (var game in scrapedGames)
            {
                var originalGameName = game.Name;
                var version = ExtractVersionNumber(originalGameName); // Extract version before cleaning
                var cleanGameName = CleanGameName(originalGameName); // Clean game name after extracting version
                var sanitizedGameName = SanitizePath(cleanGameName);

                // Log the scraped game name and version
                LogGameInfo(originalGameName, version);

                var gameMetadata = new GameMetadata()
                {
                    Name = cleanGameName,
                    GameId = cleanGameName.ToLower(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                    GameActions = new List<GameAction>(),
                    IsInstalled = false,
                    InstallDirectory = null, // Scraped games don't have an install directory
                    Icon = new MetadataFile(Path.Combine(sanitizedGameName, "icon.png")),
                    BackgroundImage = new MetadataFile(Path.Combine(sanitizedGameName, "background.png")),
                    Version = version // Set the version or build number
                };

                gameMetadata.GameActions.Add(new GameAction
                {
                    Name = "Download: Fitgirl",
                    Type = GameActionType.URL,
                    Path = game.GameActions.FirstOrDefault()?.Path,
                    IsPlayAction = false
                });

                var folder = gameFolders.FirstOrDefault(f => CleanGameName(Path.GetFileName(f)).Equals(cleanGameName, StringComparison.OrdinalIgnoreCase));
                if (folder != null)
                {
                    var playniteGameName = ConvertHyphens(cleanGameName); // Convert hyphens to colon for Playnite
                    gameMetadata.Name = playniteGameName;

                    var exeFiles = Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories)
                                            .Where(exe => !exclusions.Contains(Path.GetFileName(exe)) &&
                                                          !Path.GetFileName(exe).ToLower().Contains("setup") &&
                                                          !Path.GetFileName(exe).ToLower().Contains("unins"));

                    logger.Info($"Found {exeFiles.Count()} executable files for game: {playniteGameName} in folder: {folder}");
                    foreach (var exe in exeFiles)
                    {
                        logger.Info($"Found executable: {exe}");
                    }

                    if (!exeFiles.Any())
                    {
                        logger.Warn($"No valid executable files found for game: {playniteGameName} in folder: {folder}");
                        continue;
                    }

                    gameMetadata.IsInstalled = true;
                    gameMetadata.InstallDirectory = folder;

                    foreach (var exe in exeFiles)
                    {
                        gameMetadata.GameActions.Add(new GameAction()
                        {
                            Type = GameActionType.File,
                            Path = exe,
                            Name = Path.GetFileNameWithoutExtension(exe),
                            IsPlayAction = true,
                            WorkingDir = folder
                        });
                        logger.Info($"Added play action: {exe} to new game: {playniteGameName}");
                    }
                }

                games.Add(gameMetadata);
                // Log the Playnite game name and version
                LogPlayniteGameInfo(gameMetadata.Name, gameMetadata.Version);
                logger.Info($"Added new game: {gameMetadata.Name} with actions and install directory.");
            }

            // Add new games found in the "Games" folder but not on the FitGirl site
            foreach (var folder in gameFolders)
            {
                var folderName = Path.GetFileName(folder);
                var gameName = CleanGameName(folderName);
                var playniteGameName = ConvertHyphens(gameName); // Convert hyphens to colon for Playnite
                var sanitizedGameName = SanitizePath(gameName);

                if (!scrapedGames.Any(game => game.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase)) &&
                    !games.Any(game => game.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase)))
                {
                    var version = ExtractVersionNumber(folderName); // Extract version from folder name

                    var exeFiles = Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories)
                                            .Where(exe => !exclusions.Contains(Path.GetFileName(exe)) &&
                                                          !Path.GetFileName(exe).ToLower().Contains("setup") &&
                                                          !Path.GetFileName(exe).ToLower().Contains("unins"));

                    logger.Info($"Found {exeFiles.Count()} executable files for game: {playniteGameName} in folder: {folder}");
                    foreach (var exe in exeFiles)
                    {
                        logger.Info($"Found executable: {exe}");
                    }

                    if (!exeFiles.Any())
                    {
                        logger.Warn($"No valid executable files found for game: {playniteGameName} in folder: {folder}");
                        continue;
                    }

                    var gameMetadata = new GameMetadata()
                    {
                        Name = playniteGameName,
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                        GameId = gameName.ToLower(),
                        GameActions = new List<GameAction>(),
                        IsInstalled = true,
                        InstallDirectory = folder,
                        Icon = new MetadataFile(Path.Combine(folder, "icon.png")),
                        BackgroundImage = new MetadataFile(Path.Combine(folder, "background.png")),
                        Version = version // Set the version or build number
                    };

                    foreach (var exe in exeFiles)
                    {
                        gameMetadata.GameActions.Add(new GameAction()
                        {
                            Type = GameActionType.File,
                            Path = exe,
                            Name = Path.GetFileNameWithoutExtension(exe),
                            IsPlayAction = true,
                            WorkingDir = folder
                        });
                        logger.Info($"Added play action: {exe} to new game: {playniteGameName}");
                    }

                    games.Add(gameMetadata);
                    // Log the Playnite game name and version
                    LogPlayniteGameInfo(gameMetadata.Name, gameMetadata.Version);
                    logger.Info($"Added new game: {gameMetadata.Name} with actions and install directory.");
                }
            }

            // Add new games found in the "Repacks" folder but not on the FitGirl site
            foreach (var folder in repackFolders)
            {
                var folderName = Path.GetFileName(folder);
                var gameName = CleanGameName(folderName);
                var sanitizedGameName = SanitizePath(gameName);

                if (!scrapedGames.Any(game => game.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase)) &&
                    !games.Any(game => game.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase)))
                {
                    var version = ExtractVersionNumber(folderName); // Extract version from folder name

                    var exeFiles = Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories)
                                            .Where(exe => !exclusions.Contains(Path.GetFileName(exe)) &&
                                                          !Path.GetFileName(exe).ToLower().Contains("setup") &&
                                                          !Path.GetFileName(exe).ToLower().Contains("unins"));

                    logger.Info($"Found {exeFiles.Count()} executable files for game: {gameName} in folder: {folder}");
                    foreach (var exe in exeFiles)
                    {
                        logger.Info($"Found executable: {exe}");
                    }

                    if (!exeFiles.Any())
                    {
                        logger.Warn($"No valid executable files found for game: {gameName} in folder: {folder}");
                        continue;
                    }

                    var gameMetadata = new GameMetadata()
                    {
                        Name = gameName,
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("PC (Windows)") },
                        GameId = gameName.ToLower(),
                        GameActions = new List<GameAction>(),
                        IsInstalled = false,
                        InstallDirectory = null,
                        Icon = new MetadataFile(Path.Combine(folder, "icon.png")),
                        BackgroundImage = new MetadataFile(Path.Combine(folder, "background.png")),
                        Version = version // Set the version or build number
                    };

                    foreach (var exe in exeFiles)
                    {
                        gameMetadata.GameActions.Add(new GameAction()
                        {
                            Type = GameActionType.File,
                            Path = exe,
                            Name = Path.GetFileNameWithoutExtension(exe),
                            IsPlayAction = true,
                            WorkingDir = folder
                        });
                        logger.Info($"Added play action: {exe} to new game: {gameName}");
                    }

                    games.Add(gameMetadata);
                    // Log the Playnite game name and version
                    LogPlayniteGameInfo(gameMetadata.Name, gameMetadata.Version);
                    logger.Info($"Added new game: {gameMetadata.Name} with actions and install directory.");
                }
            }

            return games;
        }

        private string ExtractVersionNumber(string name)
        {
            var versionPattern = @"(Build \d+|v[\d\.]+)";
            var match = Regex.Match(name, versionPattern);
            return match.Success ? match.Value : string.Empty;
        }

        private void LogPlayniteGameInfo(string gameName, string version)
        {
            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                writer.WriteLine($"Playnite Name: {gameName}");
                writer.WriteLine($"Version: {version}");
                writer.WriteLine();
            }
        }



        private string ConvertHyphens(string name)
        {
            int firstHyphenIndex = name.IndexOf(" - ");
            if (firstHyphenIndex != -1)
            {
                // Convert only the first hyphen to a colon
                return name.Substring(0, firstHyphenIndex) + ": " + name.Substring(firstHyphenIndex + 3);
            }
            return name;
        }

        private string GetRelativePath(string fromPath, string toPath)
        {
            Uri fromUri = new Uri(fromPath);
            Uri toUri = new Uri(toPath);

            if (fromUri.Scheme != toUri.Scheme)
            {
                return toPath;
            }

            Uri relativeUri = fromUri.MakeRelativeUri(toUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            if (toUri.Scheme.Equals("file", StringComparison.InvariantCultureIgnoreCase))
            {
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }

            return relativePath;
        }

        private List<string> LoadExclusions()
        {
            var exclusionsFilePath = Path.Combine(GetPluginUserDataPath(), "Exclusions.txt");
            if (!File.Exists(exclusionsFilePath))
            {
                File.WriteAllText(exclusionsFilePath, string.Empty);
            }

            return File.ReadAllLines(exclusionsFilePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim('\"').Trim())
                .ToList();
        }

        private string SanitizePath(string path)
        {
            // Replace colons with hyphens for filesystem compatibility
            path = path.Replace(":", " -");

            // Remove any other invalid characters
            return Regex.Replace(path, @"[<>:""/\\|?*]", string.Empty);



        }

        public override IEnumerable<InstallController> GetInstallActions(GetInstallActionsArgs args)
        {
            if (args.Game.PluginId == Id)
            {
                yield return new LocalInstallController(args.Game, this);
            }
        }

        public static class HtmlUtility
        {
            private static readonly Dictionary<string, string> htmlEntities = new Dictionary<string, string>
    {
        { "&#038;", "&" },
        { "&amp;", "&" },
        { "&#39;", "'" },
        { "&quot;", "\"" },
        { "&lt;", "<" },
        { "&gt;", ">" }
        // Add more entities as needed
    };

            public static string HtmlDecode(string input)
            {
                foreach (var entity in htmlEntities)
                {
                    input = input.Replace(entity.Key, entity.Value);
                }
                return input;
            }
        }

        private async void GameInstaller(Game game)
        {
            var userDataPath = GetPluginUserDataPath();
            var fdmPath = Path.Combine(userDataPath, "Free Download Manager", "fdm.exe");
            var logFilePath = Path.Combine(userDataPath, "install_log.log");

            if (!File.Exists(fdmPath))
            {
                API.Instance.Dialogs.ShowErrorMessage($"fdm.exe not found at {fdmPath}. Installation cancelled.", "Error");
                UpdateGameInstallationStatus(game, false);
                return;
            }

            string repackFolder = FindRepackFolder(game.Name);

            if (!string.IsNullOrEmpty(repackFolder))
            {
                if (IsDownloadIncomplete(repackFolder))
                {
                    // Run FDM to continue the download
                    var downloadAction = game.GameActions.FirstOrDefault(action => action.Name == "Download: Fitgirl" && action.Type == GameActionType.URL);
                    var gameDownloadUrl = downloadAction?.Path;
                    if (!string.IsNullOrEmpty(gameDownloadUrl))
                    {
                        var magnetLink = ScrapeMagnetLink(gameDownloadUrl);
                        if (!string.IsNullOrEmpty(magnetLink))
                        {
                            magnetLink = HtmlUtility.HtmlDecode(magnetLink);

                            LogMagnetLink(logFilePath, magnetLink);

                            await StartFdmWithMagnetLink(fdmPath, magnetLink);

                            // Check again if the download is complete
                            repackFolder = FindRepackFolder(game.Name);
                            if (IsDownloadIncomplete(repackFolder))
                            {
                                UpdateGameInstallationStatus(game, false);
                                return;
                            }
                        }
                        else
                        {
                            API.Instance.Dialogs.ShowErrorMessage("Magnet link not found. Download cancelled.", "Error");
                            UpdateGameInstallationStatus(game, false);
                            return;
                        }
                    }
                    else
                    {
                        API.Instance.Dialogs.ShowErrorMessage("Game download URL not found. Download cancelled.", "Error");
                        UpdateGameInstallationStatus(game, false);
                        return;
                    }
                }

                // If download is complete, run setup.exe
                var setupExe = Directory.GetFiles(repackFolder, "setup.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrEmpty(setupExe))
                {
                    game.InstallDirectory = repackFolder;
                    API.Instance.Database.Games.Update(game);

                    try
                    {
                        await Task.Run(() =>
                        {
                            using (var process = new Process())
                            {
                                process.StartInfo.FileName = setupExe;
                                process.StartInfo.WorkingDirectory = repackFolder;
                                process.StartInfo.UseShellExecute = true;
                                process.Start();
                                process.WaitForExit();
                            }

                            // Wait and retry to find the newly installed game directory
                            var rootDrive = Path.GetPathRoot(repackFolder);
                            var gamesFolderPath = Path.Combine(rootDrive, "Games");
                            if (Directory.Exists(gamesFolderPath))
                            {
                                var installedGameDir = Directory.GetDirectories(gamesFolderPath, "*", SearchOption.AllDirectories)
                                    .FirstOrDefault(d => Path.GetFileName(d).Equals(game.Name, StringComparison.OrdinalIgnoreCase));

                                if (!string.IsNullOrEmpty(installedGameDir))
                                {
                                    game.InstallDirectory = installedGameDir;
                                    API.Instance.Database.Games.Update(game);

                                    game = API.Instance.Database.Games.Get(game.Id);
                                }
                            }

                            // Update game actions and status
                            UpdateGameActionsAndStatus(game, userDataPath);
                        });
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error while running setup.exe");
                        API.Instance.Dialogs.ShowErrorMessage("An error occurred while running setup.exe. Installation cancelled.", "Error");
                        UpdateGameInstallationStatus(game, false);
                        return;
                    }
                }
                else
                {
                    API.Instance.Dialogs.ShowErrorMessage("Setup.exe not found. Installation cancelled.", "Error");
                    UpdateGameInstallationStatus(game, false);
                    return;
                }
            }
            else
            {
                var downloadAction = game.GameActions.FirstOrDefault(action => action.Name == "Download: Fitgirl" && action.Type == GameActionType.URL);
                var gameDownloadUrl = downloadAction?.Path;
                if (!string.IsNullOrEmpty(gameDownloadUrl))
                {
                    var magnetLink = ScrapeMagnetLink(gameDownloadUrl);
                    if (!string.IsNullOrEmpty(magnetLink))
                    {
                        magnetLink = HtmlUtility.HtmlDecode(magnetLink);

                        LogMagnetLink(logFilePath, magnetLink);

                        await StartFdmWithMagnetLink(fdmPath, magnetLink);

                        repackFolder = FindRepackFolder(game.Name);

                        if (!IsDownloadIncomplete(repackFolder))
                        {
                            var setupExe = Directory.GetFiles(repackFolder, "setup.exe", SearchOption.AllDirectories).FirstOrDefault();
                            if (!string.IsNullOrEmpty(setupExe))
                            {
                                game.InstallDirectory = repackFolder;
                                API.Instance.Database.Games.Update(game);

                                try
                                {
                                    await Task.Run(() =>
                                    {
                                        using (var process = new Process())
                                        {
                                            process.StartInfo.FileName = setupExe;
                                            process.StartInfo.WorkingDirectory = repackFolder;
                                            process.StartInfo.UseShellExecute = true;
                                            process.Start();
                                            process.WaitForExit();
                                        }

                                        // Wait and retry to find the newly installed game directory
                                        var rootDrive = Path.GetPathRoot(repackFolder);
                                        var gamesFolderPath = Path.Combine(rootDrive, "Games");
                                        if (Directory.Exists(gamesFolderPath))
                                        {
                                            var installedGameDir = Directory.GetDirectories(gamesFolderPath, "*", SearchOption.AllDirectories)
                                                .FirstOrDefault(d => Path.GetFileName(d).Equals(game.Name, StringComparison.OrdinalIgnoreCase));

                                            if (!string.IsNullOrEmpty(installedGameDir))
                                            {
                                                game.InstallDirectory = installedGameDir;
                                                API.Instance.Database.Games.Update(game);

                                                game = API.Instance.Database.Games.Get(game.Id);
                                            }
                                        }

                                        // Update game actions and status
                                        UpdateGameActionsAndStatus(game, userDataPath);
                                    });
                                }
                                catch (Exception ex)
                                {
                                    logger.Error(ex, "Error while running setup.exe");
                                    API.Instance.Dialogs.ShowErrorMessage("An error occurred while running setup.exe. Installation cancelled.", "Error");
                                    UpdateGameInstallationStatus(game, false);
                                    return;
                                }
                            }
                        }
                    }
                    else
                    {
                        API.Instance.Dialogs.ShowErrorMessage("Magnet link not found. Download cancelled.", "Error");
                        UpdateGameInstallationStatus(game, false);
                        return;
                    }
                }
                else
                {
                    API.Instance.Dialogs.ShowErrorMessage("Game download URL not found. Download cancelled.", "Error");
                    UpdateGameInstallationStatus(game, false);
                    return;
                }
            }
        }

        private bool IsDownloadIncomplete(string repackFolder)
        {
            var mainFilesIncomplete = Directory.GetFiles(repackFolder, "*.fdmdownload", SearchOption.TopDirectoryOnly).Any();
            var unwantedFolder = Path.Combine(repackFolder, "unwanted");
            return mainFilesIncomplete || (Directory.Exists(unwantedFolder) && Directory.GetFiles(unwantedFolder, "*.fdmdownload", SearchOption.AllDirectories).Any());
        }

        private string FindRepackFolder(string gameName)
        {
            // Convert the game name to the format used in repack folders
            string convertedGameName = ConvertHyphens(gameName);

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Network))
            {
                var repacksFolder = Path.Combine(drive.RootDirectory.FullName, "Repacks");
                if (Directory.Exists(repacksFolder))
                {
                    var potentialRepackFolders = Directory.GetDirectories(repacksFolder, "*", SearchOption.TopDirectoryOnly);
                    foreach (var potentialRepackFolder in potentialRepackFolders)
                    {
                        var folderName = Path.GetFileName(potentialRepackFolder);
                        var normalizedFolderName = NormalizeName(folderName);
                        var normalizedGameName = NormalizeName(convertedGameName);

                        // Log the folder and game names being compared
                        logger.Info($"Comparing normalized folder name '{normalizedFolderName}' with normalized game name '{normalizedGameName}'");

                        if (string.Equals(normalizedFolderName, normalizedGameName, StringComparison.OrdinalIgnoreCase))
                        {
                            return potentialRepackFolder;
                        }
                    }
                }
            }
            return null;
        }

        private string NormalizeName(string name)
        {
            var normalized = Regex.Replace(name, @"\[.*?\]", "").Trim();
            normalized = Regex.Replace(normalized, @"\(.+?\)", "").Trim();
            normalized = Regex.Replace(normalized, @"[^\w\s-]", "").Trim(); // Allow hyphens
            return normalized;
        }

        private async Task StartFdmWithMagnetLink(string fdmPath, string magnetLink)
        {
            await Task.Run(() =>
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = fdmPath;
                    process.StartInfo.Arguments = magnetLink;
                    process.StartInfo.UseShellExecute = true;
                    process.Start();
                    process.WaitForExit();
                }
            });
        }

        private string ScrapeMagnetLink(string gameDownloadUrl)
        {
            using (var httpClient = new HttpClient())
            {
                var response = httpClient.GetStringAsync(gameDownloadUrl).Result;
                var regex = new Regex(@"magnet:\?xt=urn:btih:[a-zA-Z0-9]+[^\""]*");
                var match = regex.Match(response);
                if (match.Success)
                {
                    var magnetLink = match.Value;
                    Console.WriteLine($"Magnet link found: {magnetLink}");
                    return magnetLink;
                }
                else
                {
                    Console.WriteLine("No magnet link found.");
                }
            }
            return null;
        }

        private void LogMagnetLink(string logFilePath, string magnetLink)
        {
            using (var writer = new StreamWriter(logFilePath, true))
            {
                writer.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Magnet link used: {magnetLink}");
            }
        }

        private void UpdateGameInstallationStatus(Game game, bool isInstalling)
        {
            game.IsInstalling = isInstalling;
            API.Instance.Database.Games.Update(game);
        }

        private void UpdateGameActionsAndStatus(Game game, string userDataPath)
        {
            // Add all .exe files as actions, excluding those listed in exclusions.txt
            var exclusionsPath = Path.Combine(userDataPath, "exclusions.txt");
            var exclusions = new HashSet<string>();
            if (File.Exists(exclusionsPath))
            {
                var exclusionLines = File.ReadAllLines(exclusionsPath);
                foreach (var line in exclusionLines)
                {
                    exclusions.Add(line.Trim().ToLower());
                }
            }

            var exeFiles = Directory.GetFiles(game.InstallDirectory, "*.exe", SearchOption.AllDirectories);
            foreach (var exeFile in exeFiles)
            {
                var exeName = Path.GetFileNameWithoutExtension(exeFile).ToLower();
                if (!exclusions.Contains(exeName))
                {
                    var action = new GameAction
                    {
                        Name = Path.GetFileNameWithoutExtension(exeFile),
                        Type = GameActionType.File,
                        Path = exeFile,
                        WorkingDir = Path.GetDirectoryName(exeFile)
                    };
                    game.GameActions.Add(action);
                }
            }
            API.Instance.Database.Games.Update(game);

            // Signal that the installation is completed
            InvokeOnInstalled(new GameInstalledEventArgs(game.Id));

            // Force library update for the specific game
            var pluginGames = GetGames(new LibraryGetGamesArgs());
            var updatedGame = pluginGames.FirstOrDefault(g => g.Name.Equals(game.Name, StringComparison.OrdinalIgnoreCase));
            if (updatedGame != null)
            {
                game.InstallDirectory = updatedGame.InstallDirectory;
                game.GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>(updatedGame.GameActions);
                API.Instance.Database.Games.Update(game);
            }
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
                PlayniteApi.Notifications.Add(new NotificationMessage("InstallCompleted", $"Installation of {game.Name} is complete!", NotificationType.Info));
            }
        }

        protected void InvokeOnUninstalled(GameUninstalledEventArgs args)
        {
            // Update the game's state after uninstallation
            var game = API.Instance.Database.Games.Get(args.GameId);
            if (game != null)
            {
                game.IsInstalling = false;
                game.IsInstalled = false;
                API.Instance.Database.Games.Update(game);

                // Notify Playnite
                PlayniteApi.Notifications.Add(new NotificationMessage("UninstallCompleted", $"Uninstallation of {game.Name} is complete!", NotificationType.Info));
            }
        }

        public class LocalInstallController : InstallController
        {
            private readonly FitGirlStore pluginInstance;

            public LocalInstallController(Game game, FitGirlStore instance) : base(game)
            {
                pluginInstance = instance;
                Name = "Install using setup.exe";
            }

            public override void Install(InstallActionArgs args)
            {
                pluginInstance.GameInstaller(Game);
            }
        }

        public class LocalUninstallController : UninstallController
        {
            private readonly FitGirlStore pluginInstance;

            public LocalUninstallController(Game game, FitGirlStore instance) : base(game)
            {
                pluginInstance = instance;
                Name = "Uninstall using unins000.exe";
            }

            public override void Uninstall(UninstallActionArgs args)
            {
                pluginInstance.GameInstaller(Game);
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

        public class GameUninstalledEventArgs : EventArgs
        {
            public Guid GameId { get; private set; }

            public GameUninstalledEventArgs(Guid gameId)
            {
                GameId = gameId;
            }
        }

    }
}
