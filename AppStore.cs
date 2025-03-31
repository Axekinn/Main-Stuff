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

            // Process mod clients
            foreach (var modClient in modClients)
            {
                logger.Info($"Mod clients:\nfound in txt \"{modClient.Name}\"");
                var matchingGame = PlayniteApi.Database.Games.FirstOrDefault(g => g.Name.Equals(modClient.GameName, StringComparison.OrdinalIgnoreCase));
                if (matchingGame != null)
                {
                    logger.Info($"looking for \"{modClient.GameName}\"\nFound \"{modClient.GameName}\" in Playnite\nusing installdir of \"{modClient.GameName}\" from Playnite");
                    var installDir = matchingGame.InstallDirectory;

                    foreach (var action in modClient.Actions)
                    {
                        var exePath = Path.Combine(installDir, action.ExeName);
                        if (File.Exists(exePath))
                        {
                            logger.Info($"found \"{action.ExeName}\" in InstallDir\nAdding Action \"{action.ActionName}\" to Game \"{modClient.GameName}\"");
                            if (!matchingGame.GameActions.Any(a => a.Name == action.ActionName))
                            {
                                matchingGame.GameActions.Add(new GameAction
                                {
                                    Type = GameActionType.File,
                                    Path = exePath,
                                    WorkingDir = installDir,
                                    Name = action.ActionName,
                                    Arguments = action.Arguments
                                });
                                PlayniteApi.Database.Games.Update(matchingGame);
                                logger.Info($"Added action \"{action.ActionName}\" for \"{modClient.GameName}\"");
                            }
                            else
                            {
                                logger.Info($"Action \"{action.ActionName}\" already exists and is up to date.. skipping adding actions");
                            }
                        }
                        else
                        {
                            logger.Info($"not found \"{action.ExeName}\" in InstallDir");
                        }
                    }
                }
                else
                {
                    logger.Info($"looking for \"{modClient.GameName}\"\nNot found \"{modClient.GameName}\" in Playnite");
                }
            }

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

        private async Task<List<(string Name, string GameName, List<(string ActionName, string ExeName, string Arguments)> Actions)>> GetModClientsFromTextFile()
        {
            var modClients = new List<(string Name, string GameName, List<(string ActionName, string ExeName, string Arguments)> Actions)>();

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // URL to the text file on GitHub
                    var fileUrl = "https://raw.githubusercontent.com/Koriebonx98/Main-Stuff/main/Mod%20Clients.txt";
                    var response = await client.GetStringAsync(fileUrl);

                    if (string.IsNullOrEmpty(response))
                    {
                        logger.Warn($"Failed to fetch content from {fileUrl}");
                        return modClients;
                    }

                    // Split the response into lines
                    var lines = response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    string currentModClientName = string.Empty;
                    string currentGameName = string.Empty;
                    var currentActions = new List<(string ActionName, string ExeName, string Arguments)>();

                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];

                        // Skip header and empty lines
                        if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        if (line.StartsWith("GameName:"))
                        {
                            // If already processing a mod client, add it to the list
                            if (!string.IsNullOrEmpty(currentGameName))
                            {
                                modClients.Add((currentModClientName, currentGameName, currentActions));
                                currentActions = new List<(string ActionName, string ExeName, string Arguments)>();
                            }

                            // Extract the full game name after "GameName:"
                            currentGameName = line.Substring(line.IndexOf(':') + 1).Trim().Trim('"');
                        }
                        else if (line.StartsWith("Action #"))
                        {
                            var actionName = line.Substring(line.IndexOf(':') + 1).Trim().Trim('"');
                            var exeName = lines[++i].Substring(lines[i].IndexOf(':') + 1).Trim().Trim('"');
                            var arguments = (i + 1 < lines.Length && lines[i + 1].StartsWith("Arguments:")) ? lines[++i].Substring(lines[i].IndexOf(':') + 1).Trim().Trim('"') : string.Empty;
                            currentActions.Add((actionName, exeName, arguments));
                        }
                        else
                        {
                            currentModClientName = line.Trim().Trim('"');
                        }
                    }

                    // Add the last mod client being processed
                    if (!string.IsNullOrEmpty(currentGameName))
                    {
                        modClients.Add((currentModClientName, currentGameName, currentActions));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error occurred while fetching mod clients from text file: {ex.Message}");
            }

            return modClients;
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
            // You can expand this method to include more sophisticated checks if needed
            return appName.Equals("Pcsx2", StringComparison.OrdinalIgnoreCase) ||
                   appName.Equals("DuckStation", StringComparison.OrdinalIgnoreCase) ||
                   appName.Equals("Dolphin Emulator", StringComparison.OrdinalIgnoreCase) ||
                   appName.Equals("Xenia", StringComparison.OrdinalIgnoreCase) ||
                   appName.Equals("Xenia Canary", StringComparison.OrdinalIgnoreCase) ||
                   appName.Equals("RPCS3", StringComparison.OrdinalIgnoreCase) ||
                   appName.Equals("Ryujinx Canary", StringComparison.OrdinalIgnoreCase);
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new AppStoreSettingsView();
        }
    }
}
