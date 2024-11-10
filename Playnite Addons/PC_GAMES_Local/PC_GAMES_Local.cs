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

            var setupExe = Directory.GetFiles(args.Game.InstallDirectory, "setup.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (setupExe == null)
            {
                logger.Debug($"No setup.exe found for {args.Game.Name}, ID: {args.Game.GameId}");
                PlayniteApi.Dialogs.ShowErrorMessage("No setup.exe found.", "Uninstall error");
            }
            else
            {
                yield return new LocalUninstallController(args.Game, setupExe);
            }
        }

        public void GameInstaller(Game game)
        {
            var setupExe = Directory.GetFiles(game.InstallDirectory, "setup.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (!string.IsNullOrEmpty(setupExe))
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = setupExe;
                    process.StartInfo.WorkingDirectory = game.InstallDirectory;
                    process.StartInfo.UseShellExecute = true;
                    process.Start();
                    process.WaitForExit();
                }

                // Signal that the installation is completed
                InvokeOnInstalled(new GameInstalledEventArgs(game.Id));
            }
            else
            {
                API.Instance.Dialogs.ShowErrorMessage("Setup.exe not found. Installation cancelled.", "Error");
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

        protected void InvokeOnInstalled(GameInstalledEventArgs args)
        {
            // Playnite will handle updating the game's state
            PlayniteApi.Notifications.Add(new NotificationMessage("InstallCompleted", $"Installation of {API.Instance.Database.Games.Get(args.GameId).Name} is complete!", NotificationType.Info));
        }

        public class LocalUninstallController : UninstallController
        {
            private readonly string uninstallPath;

            public LocalUninstallController(Game game, string uninstallPath) : base(game)
            {
                this.uninstallPath = uninstallPath;
                Name = "Uninstall using setup.exe";
            }

            public override void Uninstall(UninstallActionArgs args)
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = uninstallPath;
                    process.StartInfo.UseShellExecute = true;
                    process.Start();
                    process.WaitForExit();
                }
                Game.IsInstalled = false;
                API.Instance.Database.Games.Update(Game);
            }
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
