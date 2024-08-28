using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
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

        public override LibraryClient Client { get; } = new PC_GAMES_LocalClient();

        public PC_GAMES_Local(IPlayniteAPI api) : base(api)
        {
            settings = new PC_GAMES_LocalSettings(this);
            Properties = new LibraryPluginProperties
            {
                HasSettings = true
            };
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

                    var gameMetadata = new GameMetadata()
                    {
                        Name = gameName,
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("pc_windows") },
                        GameId = gameName.ToLower(),
                        GameActions = exeFiles.Select(exe => new GameAction()
                        {
                            Type = GameActionType.File,
                            Path = $"{{InstallDir}}\\{GetRelativePath(folder, exe).Replace(gameName, "").TrimStart('\\')}",
                            Name = Path.GetFileNameWithoutExtension(exe),
                            IsPlayAction = true,
                            WorkingDir = "{InstallDir}"
                        }).ToList(),
                        IsInstalled = exeFiles.Any(),
                        InstallDirectory = folder,
                        Icon = new MetadataFile(Path.Combine(folder, "icon.png")),
                        BackgroundImage = new MetadataFile(Path.Combine(folder, "background.png"))
                    };

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
                        if (setupExe != null)
                        {
                            existingGame.GameActions.Add(new GameAction()
                            {
                                Type = GameActionType.File,
                                Path = setupExe,
                                Name = "Install",
                                IsPlayAction = false,
                                WorkingDir = Path.GetDirectoryName(setupExe)
                            });
                        }
                    }
                    else
                    {
                        var gameMetadata = new GameMetadata()
                        {
                            Name = gameName,
                            GameId = gameName.ToLower(),
                            Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("pc_windows") },
                            GameActions = setupExe != null ? new List<GameAction>
                            {
                                new GameAction()
                                {
                                    Type = GameActionType.File,
                                    Path = setupExe,
                                    Name = "Install",
                                    IsPlayAction = false, // Set as the primary action
                                    WorkingDir = Path.GetDirectoryName(setupExe)
                                }
                            } : new List<GameAction>(),
                            IsInstalled = false,
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
            // Implement your method to load exclusions from settings or configuration
            return new List<string> { "unwanted.exe", "anotherunwanted.exe" };
        }

        private string CleanGameName(string folderName)
        {
            // Remove text within parentheses () or square brackets []
            return Regex.Replace(folderName, @"[\(\[].*?[\)\]]", "").Trim();
        }

        private string GetRelativePath(string fromPath, string toPath)
        {
            Uri fromUri = new Uri(fromPath);
            Uri toUri = new Uri(toPath);

            if (fromUri.Scheme != toUri.Scheme) { return toPath; } // path can't be made relative.

            Uri relativeUri = fromUri.MakeRelativeUri(toUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            if (toUri.Scheme.Equals("file", StringComparison.InvariantCultureIgnoreCase))
            {
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }

            return relativePath;
        }
    }
}
