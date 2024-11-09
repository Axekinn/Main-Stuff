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
            Properties = new LibraryPluginProperties { HasSettings = true };
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var games = new List<GameMetadata>();
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Network));
            var (folderExclusions, exeExclusions) = LoadExclusions();

            foreach (var drive in drives)
            {
                var gamesFolderPath = Path.Combine(drive.RootDirectory.FullName, "Games");
                var repacksFolderPath = Path.Combine(drive.RootDirectory.FullName, "Repacks");
                var gameFolders = Directory.Exists(gamesFolderPath) ? Directory.GetDirectories(gamesFolderPath) : new string[0];
                var repackFolders = Directory.Exists(repacksFolderPath) ? Directory.GetDirectories(repacksFolderPath) : new string[0];

                var gameDict = new Dictionary<string, (string folder, bool isRepack)>();

                foreach (var folder in gameFolders)
                {
                    var gameName = CleanGameName(Path.GetFileName(folder));
                    gameDict[gameName.ToLower()] = (folder, false);
                }

                foreach (var folder in repackFolders)
                {
                    var gameName = CleanGameName(Path.GetFileName(folder));
                    if (gameDict.ContainsKey(gameName.ToLower()))
                    {
                        gameDict[gameName.ToLower()] = (folder, true);
                    }
                    else
                    {
                        gameDict[gameName.ToLower()] = (folder, true);
                    }
                }

                foreach (var game in gameDict)
                {
                    var folder = game.Value.folder;
                    var gameName = CleanGameName(Path.GetFileName(folder));

                    if (folderExclusions.Any(ex => folder.IndexOf(ex, StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;

                    var exeFiles = Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories)
                        .Where(exe => !exeExclusions.Contains(Path.GetFileName(exe))).ToList();

                    var gameMetadata = new GameMetadata
                    {
                        Name = gameName,
                        Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("pc_windows") },
                        GameId = gameName.ToLower(),
                        GameActions = new List<GameAction>(),
                        IsInstalled = !game.Value.isRepack || gameFolders.Any(f => f.Contains(gameName)),
                        InstallDirectory = !game.Value.isRepack ? folder : "",
                        Icon = new MetadataFile(Path.Combine(folder, "icon.png")),
                        BackgroundImage = new MetadataFile(Path.Combine(folder, "background.png"))
                    };

                    // Add Install action for repacks
                    if (game.Value.isRepack)
                    {
                        var setupExe = exeFiles.FirstOrDefault(exe => exe.ToLower().Contains("setup"));
                        if (setupExe != null)
                        {
                            gameMetadata.GameActions.Add(new GameAction
                            {
                                Type = GameActionType.File,
                                Path = setupExe,
                                Name = "Install",
                                IsPlayAction = false,
                                WorkingDir = Path.GetDirectoryName(setupExe)
                            });
                        }
                    }

                    // Add play actions
                    gameMetadata.GameActions.AddRange(exeFiles
                        .Where(exe => !exe.ToLower().Contains("unins"))
                        .Select(exe => new GameAction
                        {
                            Type = GameActionType.File,
                            Path = $"{{InstallDir}}\\{GetRelativePath(folder, exe).Replace(gameName, "").TrimStart('\\')}",
                            Name = Path.GetFileNameWithoutExtension(exe),
                            IsPlayAction = true,
                            WorkingDir = "{InstallDir}"
                        }));

                    // Add Uninstall action if found
                    var uninstallExe = exeFiles.FirstOrDefault(exe => exe.ToLower().Contains("unins"));
                    if (uninstallExe != null)
                    {
                        gameMetadata.GameActions.Add(new GameAction
                        {
                            Type = GameActionType.File,
                            Path = uninstallExe,
                            Name = "Uninstall",
                            IsPlayAction = false,
                            WorkingDir = Path.GetDirectoryName(uninstallExe)
                        });
                    }

                    games.Add(gameMetadata);
                }
            }

            return games;
        }

        private (List<string> folderExclusions, List<string> exeExclusions) LoadExclusions()
        {
            var folderExclusions = new List<string>();
            var exeExclusions = new List<string>();
            var exclusionsFilePath = Path.Combine(GetPluginUserDataPath(), "Exclusions.txt");

            if (!File.Exists(exclusionsFilePath))
            {
                File.WriteAllText(exclusionsFilePath, "Folders:\n\nExe's:\n");
            }

            var lines = File.ReadAllLines(exclusionsFilePath);
            bool isFolderSection = false, isExeSection = false;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("Folders:", StringComparison.OrdinalIgnoreCase))
                {
                    isFolderSection = true;
                    isExeSection = false;
                    continue;
                }

                if (line.StartsWith("Exe's:", StringComparison.OrdinalIgnoreCase))
                {
                    isFolderSection = false;
                    isExeSection = true;
                    continue;
                }

                if (isFolderSection)
                    folderExclusions.Add(line.Trim('\"'));
                else if (isExeSection)
                    exeExclusions.Add(line.Trim('\"'));
            }

            return (folderExclusions, exeExclusions);
        }

        private string CleanGameName(string folderName) => Regex.Replace(folderName, @"[\( 

\[].*?[\)\]

 ]", "").Trim();

        private string GetRelativePath(string fromPath, string toPath)
        {
            var fromUri = new Uri(fromPath);
            var toUri = new Uri(toPath);

            if (fromUri.Scheme != toUri.Scheme)
                return toPath;

            var relativeUri = fromUri.MakeRelativeUri(toUri);
            var relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            return toUri.Scheme.Equals("file", StringComparison.InvariantCultureIgnoreCase)
                ? relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                : relativePath;
        }
    }
}
