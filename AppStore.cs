using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AppStore
{
    public class AppStore : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger("AppStore");

        private AppStoreSettingsViewModel Settings { get; set; }

        public override Guid Id { get; } = Guid.Parse("33385617-5b7d-47e0-8986-57f5c7d9711b");

        public override string Name => "App Store";

        public override LibraryClient Client { get; } = new AppStoreClient();

        public AppStore(IPlayniteAPI api) : base(api)
        {
            Settings = new AppStoreSettingsViewModel(this);
            Properties = new LibraryPluginProperties
            {
                HasSettings = false
            };
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var platformName = "PC (Windows) Apps";

            // Ensure the platform exists in Playnite
            var platform = PlayniteApi.Database.Platforms.FirstOrDefault(p => p.Name.Equals(platformName, StringComparison.OrdinalIgnoreCase));
            if (platform == null)
            {
                PlayniteApi.Database.Platforms.Add(new Platform(platformName));
                platform = PlayniteApi.Database.Platforms.FirstOrDefault(p => p.Name.Equals(platformName, StringComparison.OrdinalIgnoreCase));
            }

            var apps = GetAppsFromTextFile().Result;
            var modClients = GetModClientsFromTextFile().Result;

            var newApps = new List<GameMetadata>();

            foreach (var app in apps)
            {
                // Skip if the app already exists in Playnite
                var existingGame = PlayniteApi.Database.Games.FirstOrDefault(g => g.PluginId == Id && g.GameId == app.Id);
                if (existingGame != null)
                {
                    // Check if the game is uninstalled and update the install directory if necessary
                    var exePaths = GetExePathsForApp(existingGame.Name, out string installDir);
                    if (exePaths.Any())
                    {
                        existingGame.IsInstalled = true;
                        existingGame.InstallDirectory = installDir;
                        PlayniteApi.Database.Games.Update(existingGame);
                        logger.Info($"Updated install directory for '{existingGame.Name}': {installDir}");
                    }
                    else
                    {
                        existingGame.IsInstalled = false;
                        PlayniteApi.Database.Games.Update(existingGame);
                        logger.Info($"Marked '{existingGame.Name}' as uninstalled.");
                    }
                    continue;
                }

                // Add the app to the library
                var gameMetadata = new GameMetadata()
                {
                    Name = app.Name,
                    GameId = app.Id,
                    IsInstalled = false,
                    Platforms = new HashSet<MetadataProperty> { new MetadataNameProperty(platformName) },
                    GameActions = new List<GameAction>
                    {
                        new GameAction
                        {
                            Type = GameActionType.URL,
                            Path = app.Url,
                            Name = "Download"
                        }
                    },
                    Features = new HashSet<MetadataProperty> { new MetadataNameProperty("Dynamic URL") }
                };

                newApps.Add(gameMetadata);
                logger.Info($"Added app '{app.Name}' to the library.");
            }

            // Check default Windows locations, Playnite directory for app folders, and the new PlayniteDir/System/Apps/ directory
            foreach (var gameMetadata in newApps)
            {
                var exePaths = GetExePathsForApp(gameMetadata.Name, out string installDir);

                // If exe paths are found, mark the app as installed and add exe paths as play actions
                if (exePaths.Any())
                {
                    gameMetadata.IsInstalled = true;
                    gameMetadata.InstallDirectory = installDir;
                    logger.Info($"Found install directory for '{gameMetadata.Name}': {installDir}");

                    foreach (var exePath in exePaths.Distinct())
                    {
                        var actionName = $"{Path.GetFileName(exePath)}";
                        if (!gameMetadata.GameActions.Any(a => a.Name == actionName))
                        {
                            gameMetadata.GameActions.Add(new GameAction
                            {
                                Type = GameActionType.File,
                                Path = exePath.Replace(installDir, "{InstallDir}"),
                                WorkingDir = "{InstallDir}",
                                Name = actionName
                            });
                            logger.Info($"Added action for '{gameMetadata.Name}': {exePath}");
                        }
                    }
                }
                else
                {
                    logger.Info($"No executables found for '{gameMetadata.Name}' in default locations.");
                }
            }

            // Add mod clients
            AddModClients(modClients);

            return newApps; // Return all apps to add
        }

        private async Task<List<(string Name, string Id, string Url)>> GetAppsFromTextFile()
        {
            var apps = new List<(string Name, string Id, string Url)>();

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // URL to the text file on GitHub
                    var fileUrl = "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/main/PC%20Apps.txt";
                    var response = await client.GetStringAsync(fileUrl);

                    if (string.IsNullOrEmpty(response))
                    {
                        logger.Warn($"Failed to fetch content from {fileUrl}");
                        return apps;
                    }

                    // Split the response into lines
                    var lines = response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        // Skip header and empty lines
                        if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        var parts = line.Split(',');
                        if (parts.Length == 2)
                        {
                            var name = parts[0].Trim().Trim('"');
                            var url = parts[1].Trim().Trim('"');
                            var id = name.ToLower().Replace(" ", "_");
                            apps.Add((name, id, url));
                            logger.Info($"Parsed app from text file: {name}, {url}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error occurred while fetching apps from text file: {ex.Message}");
            }

            return apps;
        }

        private async Task<List<ModClient>> GetModClientsFromTextFile()
        {
            var modClients = new List<ModClient>();

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // URL to the text file on GitHub
                    var fileUrl = "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/main/PC%20Apps.txt";
                    var response = await client.GetStringAsync(fileUrl);

                    if (string.IsNullOrEmpty(response))
                    {
                        logger.Warn($"Failed to fetch content from {fileUrl}");
                        return modClients;
                    }

                    // Split the response into lines
                    var lines = response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var readingModClient = false;
                    ModClient currentModClient = null;

                    foreach (var line in lines)
                    {
                        // Skip header and empty lines
                        if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        if (line.StartsWith("GameName:"))
                        {
                            readingModClient = true;
                            currentModClient = new ModClient
                            {
                                GameName = line.Replace("GameName:", "").Trim().Trim('"')
                            };
                        }
                        else if (line.StartsWith("InstallDir:") && readingModClient)
                        {
                            currentModClient.InstallDirs = line.Replace("InstallDir:", "").Trim().Trim('"').Split(new[] { " or " }, StringSplitOptions.None);
                        }
                        else if (line.StartsWith("Url:") && readingModClient)
                        {
                            currentModClient.Url = line.Replace("Url:", "").Trim().Trim('"');
                        }
                        else if (line.StartsWith("Action Name") && readingModClient)
                        {
                            var action = new ModClientAction
                            {
                                Name = line.Split(':')[1].Trim().Trim('"')
                            };

                            // Read next lines for Exe, Arguments, and Play Action
                            for (int i = Array.IndexOf(lines, line) + 1; i < lines.Length; i++)
                            {
                                var actionLine = lines[i];
                                if (actionLine.StartsWith("Exe:"))
                                {
                                    action.Exe = actionLine.Replace("Exe:", "").Trim().Trim('"');
                                }
                                else if (actionLine.StartsWith("Arguments:"))
                                {
                                    action.Arguments = actionLine.Replace("Arguments:", "").Trim().Trim('"');
                                }
                                else if (actionLine.StartsWith("Play Action:"))
                                {
                                    action.IsPrimary = actionLine.Replace("Play Action:", "").Trim().Trim('"').Equals("True", StringComparison.OrdinalIgnoreCase);
                                    currentModClient.Actions.Add(action);
                                    break;
                                }
                            }
                        }
                        else if (line.StartsWith("GameName:") && readingModClient)
                        {
                            readingModClient = false;
                            modClients.Add(currentModClient);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error occurred while fetching mod clients from text file: {ex.Message}");
            }

            return modClients;
        }

        private void AddModClients(List<ModClient> modClients)
        {
            foreach (var modClient in modClients)
            {
                logger.Info($"Processing mod client: {modClient.GameName}");

                // Check for existing game
                var existingGame = PlayniteApi.Database.Games.FirstOrDefault(g =>
                    (g.Name.Equals(modClient.GameName, StringComparison.OrdinalIgnoreCase) || g.Name.Equals(modClient.GameNameAlt, StringComparison.OrdinalIgnoreCase)) &&
                    g.Platforms.Any(p => p.Name.Equals("PC (Windows)", StringComparison.OrdinalIgnoreCase)));

                if (existingGame != null)
                {
                    logger.Info($"Found existing game for mod client '{modClient.GameName}': {existingGame.Name}");

                    if (existingGame.IsInstalled)
                    {
                        logger.Info($"Game '{existingGame.Name}' is installed. Using install directory '{existingGame.InstallDirectory}'.");

                        // Update App Store app install directory and actions
                        var appStoreGame = PlayniteApi.Database.Games.FirstOrDefault(g => g.PluginId == Id && g.GameId == modClient.GameName.ToLower().Replace(" ", "_"));
                        if (appStoreGame != null)
                        {
                            logger.Info($"Updating install directory for App Store app '{appStoreGame.Name}' to '{existingGame.InstallDirectory}'.");

                            appStoreGame.InstallDirectory = existingGame.InstallDirectory;
                            appStoreGame.GameActions.Clear();

                            foreach (var action in modClient.Actions)
                            {
                                appStoreGame.GameActions.Add(new GameAction
                                {
                                    Type = GameActionType.File,
                                    Path = action.Exe.Replace(existingGame.InstallDirectory, "{InstallDir}"),
                                    Arguments = action.Arguments,
                                    WorkingDir = "{InstallDir}",
                                    Name = action.Name,

                                });
                            }

                            PlayniteApi.Database.Games.Update(appStoreGame);
                        }

                        // Add actions to the existing game in Playnite
                        foreach (var action in modClient.Actions)
                        {
                            if (!existingGame.GameActions.Any(a => a.Name.Equals(action.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                existingGame.GameActions.Add(new GameAction
                                {
                                    Type = GameActionType.File,
                                    Path = action.Exe.Replace(existingGame.InstallDirectory, "{InstallDir}"),
                                    Arguments = action.Arguments,
                                    WorkingDir = "{InstallDir}",
                                    Name = action.Name,
                                });
                                logger.Info($"Added action '{action.Name}' for '{existingGame.Name}': {action.Exe}");
                            }
                            else
                            {
                                logger.Info($"Action '{action.Name}' already exists for '{existingGame.Name}', skipping.");
                            }
                        }

                        PlayniteApi.Database.Games.Update(existingGame);
                    }
                }
                else
                {
                    logger.Info($"No existing game found for mod client '{modClient.GameName}'.");
                }
            }
        }

        private List<string> GetExePathsForApp(string appName, out string installDir)
        {
            var exePaths = new List<string>();
            var programFiles = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) };
            installDir = null;

            foreach (var programFile in programFiles)
            {
                var appFolder = Path.Combine(programFile, appName);
                if (Directory.Exists(appFolder))
                {
                    logger.Info($"Found folder for '{appName}' in '{programFile}': {appFolder}");

                    // For Steam, only add the main executable
                    if (appName.Equals("Steam", StringComparison.OrdinalIgnoreCase))
                    {
                        var steamExe = Path.Combine(appFolder, "Steam.exe");
                        if (File.Exists(steamExe))
                        {
                            exePaths.Add(steamExe);
                            installDir = appFolder;
                        }
                    }
                    else
                    {
                        var exes = Directory.GetFiles(appFolder, "*.exe", SearchOption.TopDirectoryOnly);
                        exePaths.AddRange(exes);
                        installDir = appFolder;
                    }
                }
            }

            // Additional specific case for Proton VPN
            if (installDir == null && appName.Equals("Proton VPN", StringComparison.OrdinalIgnoreCase))
            {
                var protonVpnFolder = Path.Combine("C:\\Program Files\\Proton\\VPN");
                if (Directory.Exists(protonVpnFolder))
                {
                    var exes = Directory.GetFiles(protonVpnFolder, "*.exe", SearchOption.AllDirectories);
                    exePaths.AddRange(exes);
                    installDir = protonVpnFolder;
                    logger.Info($"Found executables for 'Proton VPN' in {protonVpnFolder}: {string.Join(", ", exes)}");
                }
                else
                {
                    logger.Info($"No folder found for 'Proton VPN' in {protonVpnFolder}");
                }
            }

            // Additional specific case for 7-Zip
            if (installDir == null && appName.Equals("7-Zip", StringComparison.OrdinalIgnoreCase))
            {
                var sevenZipFolder = Path.Combine("C:\\Program Files\\7-Zip");
                if (Directory.Exists(sevenZipFolder))
                {
                    var exes = Directory.GetFiles(sevenZipFolder, "*.exe", SearchOption.AllDirectories);
                    exePaths.AddRange(exes);
                    installDir = sevenZipFolder;
                    logger.Info($"Found executables for '7-Zip' in {sevenZipFolder}: {string.Join(", ", exes)}");
                }
                else
                {
                    logger.Info($"No folder found for '7-Zip' in {sevenZipFolder}");
                }
            }

            // Check PlayniteDir/System/Apps for applications
            if (installDir == null)
            {
                var playniteDir = GetPlayniteInstallDir();
                logger.Info($"Playnite installation directory: {playniteDir}");
                var appsRootFolder = Path.Combine(playniteDir, "System", "Apps");

                if (Directory.Exists(appsRootFolder))
                {
                    var appFolder = Path.Combine(appsRootFolder, appName);
                    if (Directory.Exists(appFolder))
                    {
                        var exes = Directory.GetFiles(appFolder, "*.exe", SearchOption.AllDirectories);
                        exePaths.AddRange(exes);
                        installDir = appFolder;
                        logger.Info($"Found executables for '{appName}' in Playnite System Apps directory: {string.Join(", ", exes)}");
                    }

                    if (!exePaths.Any())
                    {
                        logger.Info($"No executables found for '{appName}' in Playnite System Apps directory.");
                    }
                }
                else
                {
                    logger.Info($"No folder found for apps in Playnite directory: {appsRootFolder}");
                }
            }

            // Check PlayniteDir for emulators
            if (installDir == null && IsEmulator(appName))
            {
                var playniteDir = GetPlayniteInstallDir();
                logger.Info($"Playnite installation directory: {playniteDir}");
                var emulatorsRootFolder = Path.Combine(playniteDir, "Emulation", "Emulators");

                if (Directory.Exists(emulatorsRootFolder))
                {
                    if (appName.Equals("Xenia Canary", StringComparison.OrdinalIgnoreCase))
                    {
                        var xeniaCanaryFolder = Path.Combine(emulatorsRootFolder, "Xenia", "xenia canary");
                        if (Directory.Exists(xeniaCanaryFolder))
                        {
                            var exes = Directory.GetFiles(xeniaCanaryFolder, "*.exe", SearchOption.AllDirectories);
                            exePaths.AddRange(exes);
                            installDir = xeniaCanaryFolder;
                            logger.Info($"Found executables for 'xenia canary' in Playnite directory: {string.Join(", ", exes)}");
                        }
                    }
                    else if (appName.Equals("Xenia", StringComparison.OrdinalIgnoreCase))
                    {
                        var xeniaFolder = Path.Combine(emulatorsRootFolder, "Xenia", "xenia");
                        if (Directory.Exists(xeniaFolder))
                        {
                            var exes = Directory.GetFiles(xeniaFolder, "*.exe", SearchOption.AllDirectories);
                            exePaths.AddRange(exes);
                            installDir = xeniaFolder;
                            logger.Info($"Found executables for 'xenia' in Playnite directory: {string.Join(", ", exes)}");
                        }
                    }
                    else if (appName.Equals("Dolphin Emulator", StringComparison.OrdinalIgnoreCase))
                    {
                        var dolphinFolder = Path.Combine(emulatorsRootFolder, "Dolphin");
                        if (Directory.Exists(dolphinFolder))
                        {
                            var exes = Directory.GetFiles(dolphinFolder, "*.exe", SearchOption.AllDirectories);
                            exePaths.AddRange(exes);
                            installDir = dolphinFolder;
                            logger.Info($"Found executables for 'Dolphin' in Playnite directory: {string.Join(", ", exes)}");
                        }
                    }
                    else
                    {
                        var emulatorFolders = Directory.GetDirectories(emulatorsRootFolder, "*", SearchOption.TopDirectoryOnly);
                        foreach (var emulatorFolder in emulatorFolders)
                        {
                            if (emulatorFolder.EndsWith(appName, StringComparison.OrdinalIgnoreCase))
                            {
                                var exes = Directory.GetFiles(emulatorFolder, "*.exe", SearchOption.AllDirectories);
                                exePaths.AddRange(exes);
                                installDir = emulatorFolder;
                                logger.Info($"Found executables for emulator '{appName}' in Playnite directory: {string.Join(", ", exes)}");
                            }
                        }
                    }

                    if (!exePaths.Any())
                    {
                        logger.Info($"No executables found for emulator '{appName}' in Playnite directory.");
                    }
                }
                else
                {
                    logger.Info($"No folder found for emulators in Playnite directory: {emulatorsRootFolder}");
                }
            }

            return exePaths;
        }

        private string GetPlayniteInstallDir()
        {
            // Attempt to retrieve Playnite installation directory from the registry
            string installDir = null;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Playnite"))
                {
                    if (key != null)
                    {
                        installDir = key.GetValue("InstallDir") as string;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error retrieving Playnite installation directory from registry: {ex.Message}");
            }

            // Fallback to Playnite configuration path if registry key is not found
            if (string.IsNullOrEmpty(installDir))
            {
                installDir = PlayniteApi.Paths.ConfigurationPath;
            }

            return installDir;
        }

        private bool IsEmulator(string appName)
        {
            var emulators = new[] { "Dolphin Emulator", "Xenia", "Xenia Canary" };
            return emulators.Contains(appName, StringComparer.OrdinalIgnoreCase);
        }



        public class ModClient
        {
            public string GameName { get; set; }
            public string GameNameAlt { get; set; }
            public string Url { get; set; }
            public string[] InstallDirs { get; set; }
            public List<ModClientAction> Actions { get; set; }

            public ModClient()
            {
                Actions = new List<ModClientAction>();
            }
        }

        public class ModClientAction
        {
            public string Name { get; set; }
            public string Exe { get; set; }
            public string Arguments { get; set; }
            public bool IsPrimary { get; set; }
        }
    }

}
