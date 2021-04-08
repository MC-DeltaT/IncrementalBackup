using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;


namespace IncrementalBackup
{
    /// <summary>
    /// Configuration of a backup run.
    /// </summary>
    /// <param name="SourceDirectory">The directory to be backed up. Should be fully qualified and normalised.</param>
    /// <param name="TargetDirectory">The directory to back up to. Should be fully qualified and normalised.</param>
    /// <param name="ExcludePaths">A list of files and folders that are excluded from being backed up.
    /// Each path should be fully qualified and normalised.</param>
    record BackupConfig(
        string SourceDirectory,
        string TargetDirectory,
        IReadOnlyList<string> ExcludePaths
    );

    /// <summary>
    /// Results of <see cref="Backup.Run(BackupConfig, IReadOnlyList{BackupManifest}, Logger)"/>.
    /// </summary>
    class BackupResults
    {
        /// <summary>
        /// The manifest for the backup.
        /// </summary>
        public BackupManifest Manifest;
        /// <summary>
        /// Indicates whether any paths were skipped due to I/O errors, permission errors, etc.
        /// (NOT inclusive of paths that were specifically requested to be exluded).
        /// </summary>
        public bool PathsSkipped;
    }


    class Backup
    {
        /// <summary>
        /// Runs a backup instance. Creates the new backup directory in the target directory and then backs up files
        /// from the source directory to it. <br/>
        /// Also creates a new log file in the backup directory and attaches it to <paramref name="logger"/>.
        /// </summary>
        /// <param name="config">The configuration of this backup run.</param>
        /// <param name="previousManifests">The existing backup manifests for this source directory. Must be in order
        /// of the backup time.</param>
        /// <param name="logger">For logging of info during the backup.</param>
        /// <returns></returns>
        public static BackupResults Run(BackupConfig config, IReadOnlyList<BackupManifest> previousManifests,
                Logger logger) =>
            new Backup(config, previousManifests, logger).Run();

        /// <summary>
        /// The name of the log file created in backup directories.
        /// </summary>
        private const string LOG_FILENAME = "log.txt";

        private readonly BackupConfig Config;
        private readonly IReadOnlyList<BackupManifest> PreviousManifests;
        private readonly string BackupDirectory;
        private readonly Logger Logger;
        private BackupResults Results;

        private Backup(BackupConfig config, IReadOnlyList<BackupManifest> previousManifests, Logger logger) {
            Config = config;
            PreviousManifests = previousManifests;
            Results = new() { Manifest = new(), PathsSkipped = false };
            Logger = logger;

            BackupDirectory = CreateBackupDirectory();
            CreateLogFile(BackupDirectory, Logger);
        }

        private BackupResults Run() {
            Results.Manifest.BeginTime = DateTime.UtcNow;

            // Explore directories in a depth-first manner, on the basis that files/folders within the same
            // branch of the filesystem are likely to be modified together, so we want to back them up as
            // close together in time as possible.
            // Using an iterative algorithm to avoid recursion errors. However nstead of the normal iterative
            // depth-first search algorithm, we push iterators of the child nodes onto the stack, and the algorithm
            // constantly enumerates the top iterator. This means that the order of directories in the stack actually
            // gives us the path from the source directory to the current directory, which is handy.

            // Tuple item 1 is the parent node in the new backup tree.
            // Tuple item 2 is an iterator of the directory's subdirectories.
            List<Tuple<DirectoryNode?, IEnumerator<DirectoryInfo>>> searchStack = new();

            searchStack.Add(new(
                null,
                new List<DirectoryInfo>() { new(Config.SourceDirectory) }.GetEnumerator()));       // TODO: exception handling

            while (searchStack.Count > 0) {
                while (searchStack[^1].Item2.MoveNext()) {
                    var parentNode = searchStack[^1].Item1;
                    var currentDirectory = searchStack[^1].Item2.Current;
                    var fullPath = currentDirectory.FullName;      // TODO: exception handling

                    if (IsPathExcluded(fullPath)) {
                        Logger.Info($"Skipped excluded directory \"{fullPath}\"");
                    }
                    else {
                        var relativePathComponents = searchStack.Where(t => t.Item1 != null).Select(t => t.Item1!.Name);
                        var relativePathString = string.Join(Path.DirectorySeparatorChar, relativePathComponents);

                        DirectoryNode newNode;
                        if (parentNode == null) {
                            newNode = Results.Manifest.BackupTree;
                        }
                        else {
                            //Directory.CreateDirectory(Path.Join(BackupDirectory, relativePathString));    // TODO: exception handling
                            newNode = new() { Name = currentDirectory.Name };
                            parentNode.Subdirectories.Add(newNode);
                        }

                        //BackUpDirectoryFiles(currentDirectory, relativePathString, relativePathComponents);

                        var subdirectories = currentDirectory.GetDirectories();     // TODO: exception handling
                        searchStack.Add(new(newNode, subdirectories.AsEnumerable().GetEnumerator()));
                    }
                }
                searchStack.Last().Item2.Dispose();
                searchStack.RemoveAt(searchStack.Count - 1);
            }

            return Results;
        }

        /// <summary>
        /// Creates a new directory in the target directory for this backup.
        /// </summary>
        /// <returns>The path to the created directory.</returns>
        /// <exception cref="BackupException">If a new backup directory could not be created.</exception>
        private string CreateBackupDirectory() {
            string path;
            try {
                path = BackupMeta.CreateBackupDirectory(Config.TargetDirectory);
            }
            catch (CreateBackupDirectoryException e) {
                throw new BackupException("Failed to create new backup directory.", e);
            }
            Logger.Info($"Created backup directory \"{path}\"");
            return path;
        }

        /// <summary>
        /// Initialises logging to a file in the backup directory. <br/>
        /// If creation of the log file fails, writes a warning to the logs.
        /// </summary>
        /// <param name="backupDirectory">The directory to create the log file in.</param>
        /// <param name="logger">The <see cref="Logger"/> to associate with the log file.</param>
        private static void CreateLogFile(string backupDirectory, Logger logger) {
            var path = Path.Join(backupDirectory, LOG_FILENAME);
            try {
                logger.FileHandler = new(path);
            }
            catch (LoggerException e) {
                logger.Warning(e.Message);
                return;
            }
            logger.Info($"Created log file \"{path}\"");
        }

        private void BackUpDirectoryFiles(DirectoryInfo directory, string relativePath, IEnumerable<string> relativePathComponents) {
            var files = directory.GetFiles();       // TODO: exception handling
            foreach (var file in files) {
                var path = file.FullName;       // TODO: exception handling
                if (IsPathExcluded(path)) {
                    Logger.Info($"Skipped excluded file \"{path}\"");
                }
                else if (ShouldBackUpFile(relativePathComponents, file.Name, file.LastWriteTimeUtc)) {
                    var backupPath = Path.Join(relativePath, file.Name);
                    BackUpFile(file, backupPath);
                }
            }
        }

        private void BackUpFile(FileInfo file, string relativePath) {
            var backupPath = Path.Join(BackupDirectory, relativePath);
            file.CopyTo(backupPath);        // TODO: exception handling
        }

        private bool ShouldBackUpFile(IEnumerable<string> relativePathComponents, string filename, DateTime lastWriteTimeUtc) {
            // Find the last backup that included the file. Then see if the file has been modified since then.
            foreach (var manifest in PreviousManifests.Reverse()) {
                // Match the directory segment of the path by walking down the backup tree.
                var directoryMatch = true;
                var node = manifest.BackupTree;
                foreach (var component in relativePathComponents) {
                    try {
                        node = node.Subdirectories.First(s => string.Compare(s.Name, component, true) == 0);
                    }
                    catch (InvalidOperationException) {
                        directoryMatch = false;
                        break;
                    }
                }
                if (directoryMatch) {
                    if (node.Files.Any(f => string.Compare(f, filename, true) == 0)) {
                        return lastWriteTimeUtc >= manifest.BeginTime;
                    }
                }
            }
            // If no backups included the file, it's never been backed up.
            return true;
        }

        /// <summary>
        /// Checks if a path matches any paths in the excluded paths configuration, and thus should be excluded
        /// from the back up.
        /// </summary>
        /// <remarks>
        /// Uses case-insensitive path matching, so only works on Windows.
        /// </remarks>
        /// <param name="path">The full path to check.</param>
        /// <returns><c>true</c> if the path is excluded from the back up, otherwise <c>false</c>.</returns>
        private bool IsPathExcluded(string path) {
            path = Utility.RemoveTrailingDirSep(path);
            return Config.ExcludePaths.Any(
                p => string.Compare(path, Utility.RemoveTrailingDirSep(p), true) == 0);
        }
    }

    /// <summary>
    /// Thrown when a backup operation cannot be completed.
    /// </summary>
    class BackupException : ApplicationException
    {
        public BackupException(string? message, Exception? innerException) : base(message, innerException) { }
    }
}
