using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PC_GAMES_Local
{
    public class PC_GAMES_Local : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private PC_GAMES_LocalSettings settings { get; set; }

        public override Guid Id { get; } = Guid.Parse("7a9fcdb1-3f2c-4737-a84d-067db39910bb");
        public override string Name => "PC Games Local";

        public PC_GAMES_Local(IPlayniteAPI api) : base(api)
        {
            settings = new PC_GAMES_LocalSettings(this);
            Properties = new LibraryPluginProperties { HasSettings = true };
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var games = new List<GameMetadata>();
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Network));
            var exclusions = LoadExclusions();

            foreach (var drive in drives)
            {
                var gamesFolderPath = Path.Combine(drive.RootDirectory.FullName, "Games");
                var repacksFolderPath = Path.Combine(drive.RootDirectory.FullName, "Repacks");

                var gameFolders = Directory.Exists(gamesFolderPath) ? Directory.GetDirectories(gamesFolderPath) : new string[0];
                var repackFolders = Directory.Exists(repacksFolderPath) ? Directory.GetDirectories(repacksFolderPath) : new string[0];

                foreach (var folder in gameFolders)
                {
                    var gameName = CleanGameName(Path.GetFileName(folder));
                    var exeFiles = Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories)
                                            .Where(exe => !exclusions.Contains(Path.GetFileName(exe)));

                    if (exclusions.Contains(Path.GetFileName(folder)))
                    {
                        continue;
                    }

                    var gameMetadata = new GameMetadata()
                    {
                        Name = gameName,
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("pc_windows") },
                        GameId = gameName.ToLower(),
                        GameActions = new List<GameAction>(),
                        IsInstalled = exeFiles.Any(),
                        InstallDirectory = folder,
                        Icon = new MetadataFile(Path.Combine(folder, "icon.png")),
                        BackgroundImage = new MetadataFile(Path.Combine(folder, "background.png"))
                    };

                    // Add play actions
                    gameMetadata.GameActions.AddRange(exeFiles.Where(exe => !exe.ToLower().Contains("unins")).Select(exe => new GameAction()
                    {
                        Type = GameActionType.File,
                        Path = $"{{InstallDir}}\\{GetRelativePath(folder, exe).Replace(gameName, "").TrimStart('\\')}",
                        Name = Path.GetFileNameWithoutExtension(exe),
                        IsPlayAction = true,
                        WorkingDir = "{InstallDir}"
                    }));

                    games.Add(gameMetadata);
                }

                foreach (var folder in repackFolders)
                {
                    var gameName = CleanGameName(Path.GetFileName(folder));
                    var exeFiles = Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories)
                                            .Where(exe => !exclusions.Contains(Path.GetFileName(exe)));

                    var setupExe = exeFiles.FirstOrDefault(exe => exe.ToLower().Contains("setup"));

                    var existingGame = games.FirstOrDefault(g => g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
                    if (existingGame != null)
                    {
                        if (!existingGame.IsInstalled)
                        {
                            existingGame.InstallDirectory = folder;
                            existingGame.GameActions.RemoveAll(a => a.IsPlayAction && a.Path.ToLower().Contains("unins"));

                            foreach (var action in existingGame.GameActions.Where(a => a.IsPlayAction))
                            {
                                var newPath = exeFiles.FirstOrDefault(exe => Path.GetFileNameWithoutExtension(exe) == action.Name);
                                if (newPath != null && action.Path != newPath)
                                {
                                    action.Path = newPath;
                                    action.WorkingDir = Path.GetDirectoryName(newPath);
                                }
                            }
                        }
                    }
                    else
                    {
                        var gameMetadata = new GameMetadata()
                        {
                            Name = gameName,
                            GameId = gameName.ToLower(),
                            Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("pc_windows") },
                            GameActions = new List<GameAction>(),
                            IsInstalled = false,
                            InstallDirectory = folder,
                            Icon = new MetadataFile(Path.Combine(folder, "icon.png")),
                            BackgroundImage = new MetadataFile(Path.Combine(folder, "background.png"))
                        };

                        games.Add(gameMetadata);
                    }
                }
            }

            return games;
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

        private string CleanGameName(string folderName)
        {
            return Regex.Replace(folderName, @"[\(

\[].*?[\)\]

]", "").Trim();
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
        public override IEnumerable<InstallController> GetInstallActions(GetInstallActionsArgs args)
        {
            if (args.Game.PluginId == Id)
            {
                yield return new LocalInstallController(args.Game, this);
            }
        }

        public override IEnumerable<UninstallController> GetUninstallActions(GetUninstallActionsArgs args)
        {
            if (args.Game.PluginId != Id) yield break;

            yield return new LocalUninstallController(args.Game, this);
        }

        public void GameInstaller(Game game)
        {
            var userDataPath = GetPluginUserDataPath();
            var fdmPath = Path.Combine(userDataPath, "Free Download Manager", "fdm.exe");

            if (!File.Exists(fdmPath))
            {
                API.Instance.Dialogs.ShowErrorMessage($"fdm.exe not found at {fdmPath}. Installation cancelled.", "Error");
                return;
            }

            var repackSetupExe = game.InstallDirectory != null
                ? Directory.GetFiles(game.InstallDirectory, "setup.exe", SearchOption.AllDirectories).FirstOrDefault()
                : null;

            if (!string.IsNullOrEmpty(repackSetupExe))
            {
                // Run setup.exe if found in Repacks
                using (var process = new Process())
                {
                    process.StartInfo.FileName = repackSetupExe;
                    process.StartInfo.WorkingDirectory = game.InstallDirectory;
                    process.StartInfo.UseShellExecute = true;
                    process.Start();
                    process.WaitForExit();
                }

                // Wait and retry to find the newly installed game directory
                var rootDrive = Path.GetPathRoot(game.InstallDirectory);
                var gamesFolderPath = Path.Combine(rootDrive, "Games");
                if (Directory.Exists(gamesFolderPath))
                {
                    var installedGameDir = Directory.GetDirectories(gamesFolderPath, "*", SearchOption.AllDirectories)
                        .FirstOrDefault(d => Path.GetFileName(d).Equals(game.Name, StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrEmpty(installedGameDir))
                    {
                        game.InstallDirectory = installedGameDir;
                        API.Instance.Database.Games.Update(game);

                        // Reload the game's data to ensure the install directory is up-to-date
                        game = API.Instance.Database.Games.Get(game.Id);
                    }
                }

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
            else
            {
                // Run fdm.exe and scrape for magnet link if setup.exe is not found
                var downloadAction = game.GameActions.FirstOrDefault(action => action.Name == "Download: Fitgirl" && action.Type == GameActionType.URL);
                var gameDownloadUrl = downloadAction?.Path;
                if (!string.IsNullOrEmpty(gameDownloadUrl))
                {
                    var magnetLink = ScrapeMagnetLink(gameDownloadUrl);
                    if (!string.IsNullOrEmpty(magnetLink))
                    {
                        using (var process = new Process())
                        {
                            process.StartInfo.FileName = fdmPath;
                            process.StartInfo.Arguments = magnetLink;
                            process.StartInfo.UseShellExecute = true;
                            process.Start();
                            process.WaitForExit();
                        }
                    }
                    else
                    {
                        API.Instance.Dialogs.ShowErrorMessage("Magnet link not found. Download cancelled.", "Error");
                        return;
                    }

                    // Check if the repack is downloaded
                    var repacksFolderPath = Path.Combine(Path.GetPathRoot(userDataPath), "Repacks");
                    var repackDir = Directory.GetDirectories(repacksFolderPath, game.Name, SearchOption.AllDirectories).FirstOrDefault();
                    if (!string.IsNullOrEmpty(repackDir))
                    {
                        var downloadingFiles = Directory.GetFiles(repackDir, "*.fdmdownload", SearchOption.AllDirectories);
                        while (downloadingFiles.Any())
                        {
                            System.Threading.Thread.Sleep(5000);  // Wait for 5 seconds before rechecking
                            downloadingFiles = Directory.GetFiles(repackDir, "*.fdmdownload", SearchOption.AllDirectories);
                        }

                        game.InstallDirectory = repackDir;
                        API.Instance.Database.Games.Update(game);

                        // Run setup.exe after download is complete
                        repackSetupExe = Directory.GetFiles(repackDir, "setup.exe", SearchOption.AllDirectories).FirstOrDefault();
                        if (!string.IsNullOrEmpty(repackSetupExe))
                        {
                            using (var process = new Process())
                            {
                                process.StartInfo.FileName = repackSetupExe;
                                process.StartInfo.WorkingDirectory = repackDir;
                                process.StartInfo.UseShellExecute = true;
                                process.Start();
                                process.WaitForExit();
                            }

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
                        else
                        {
                            API.Instance.Dialogs.ShowErrorMessage("setup.exe not found in the repack directory. Installation cancelled.", "Error");
                        }
                    }
                    else
                    {
                        API.Instance.Dialogs.ShowErrorMessage("Repack directory not found after download. Installation cancelled.", "Error");
                    }
                }
                else
                {
                    API.Instance.Dialogs.ShowErrorMessage("Game download URL not found. Download cancelled.", "Error");
                }
            }
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
                    return match.Value;
                }
            }
            return null;
        }

        public void GameUninstaller(Game game)
{
    // Fetch the latest game data at the beginning of the method
    game = API.Instance.Database.Games.Get(game.Id);

    var uninstallExe = Directory.GetFiles(game.InstallDirectory, "unins000.exe", SearchOption.AllDirectories).FirstOrDefault();
    if (!string.IsNullOrEmpty(uninstallExe))
    {
        using (var process = new Process())
        {
            process.StartInfo.FileName = uninstallExe;
            process.StartInfo.WorkingDirectory = game.InstallDirectory;
            process.StartInfo.UseShellExecute = true;
            process.Start();
            process.WaitForExit();
        }

        // Ensure "unins000.exe" has stopped running
        var processName = Path.GetFileNameWithoutExtension(uninstallExe);
        while (Process.GetProcessesByName(processName).Any())
        {
            System.Threading.Thread.Sleep(1000);
        }

        // Check if the game is no longer in the current InstallDirectory
        while (Directory.Exists(game.InstallDirectory))
        {
            System.Threading.Thread.Sleep(1000);
        }
    }
    else
    {
        if (Directory.Exists(game.InstallDirectory))
        {
            Directory.Delete(game.InstallDirectory, true);
        }

        // Check if the game is no longer in the current InstallDirectory
        while (Directory.Exists(game.InstallDirectory))
        {
            System.Threading.Thread.Sleep(1000);
        }
    }

    // Update the install directory to Repacks if it exists, otherwise set to empty
    var rootDrive = Path.GetPathRoot(game.InstallDirectory);
    var repacksFolderPath = Path.Combine(rootDrive, "Repacks");
    var repacksGameDir = Path.Combine(repacksFolderPath, game.Name);

    if (Directory.Exists(repacksGameDir))
    {
        game.InstallDirectory = repacksGameDir;

        var setupExe = Directory.GetFiles(repacksGameDir, "setup.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (!string.IsNullOrEmpty(setupExe))
        {
            game.GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>
            {
                new GameAction
                {
                    Name = "Install",
                    Type = GameActionType.File,
                    Path = setupExe,
                    IsPlayAction = true,
                    WorkingDir = repacksGameDir
                }
            };
        }
        else
        {
            game.GameActions.Clear();
        }
    }
    else
    {
        game.InstallDirectory = string.Empty;
        game.GameActions.Clear();
    }

    game.IsInstalled = false;
    game.IsInstalling = false;
    API.Instance.Database.Games.Update(game);

    // Signal that the uninstallation is completed
    InvokeOnUninstalled(new GameUninstalledEventArgs(game.Id));
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
            private readonly PC_GAMES_Local pluginInstance;

            public LocalInstallController(Game game, PC_GAMES_Local instance) : base(game)
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
            private readonly PC_GAMES_Local pluginInstance;

            public LocalUninstallController(Game game, PC_GAMES_Local instance) : base(game)
            {
                pluginInstance = instance;
                Name = "Uninstall using unins000.exe";
            }

            public override void Uninstall(UninstallActionArgs args)
            {
                pluginInstance.GameUninstaller(Game);
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
