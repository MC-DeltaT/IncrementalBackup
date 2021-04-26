using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


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
    /// Results of <see cref="Backup.Run(BackupConfig, IReadOnlyList{PreviousBackup}, Logger)"/>.
    /// </summary>
    /// <param name="PathsSkipped">Indicates whether any paths were skipped due to I/O errors, permission errors, etc.
    /// (NOT inclusive of paths that were specifically requested to be exluded).</param>
    record BackupResults(
        bool PathsSkipped
    );

    class Backup
    {
        /// <summary>
        /// Runs a backup instance. Creates the new backup directory in the target directory and then backs up files
        /// from the source directory to it. <br/>
        /// Also creates a new log file in the backup directory and attaches it to <paramref name="logger"/>.
        /// </summary>
        /// <param name="config">The configuration of this backup run.</param>
        /// <param name="previousBackups">The existing backups for this source directory.</param>
        /// <param name="logger">For logging of info during the backup.</param>
        /// <returns></returns>
        public static BackupResults Run(BackupConfig config, IReadOnlyList<PreviousBackup> previousBackups,
                Logger logger) =>
            new Backup(config, previousBackups, logger).Run();

        /// <summary>
        /// The name of the log file created in backup directories.
        /// </summary>
        private const string LOG_FILENAME = "log.txt";

        private readonly BackupConfig Config;
        private readonly IReadOnlyList<PreviousBackup> PreviousBackups;
        private readonly string BackupDirectory;
        private readonly string BackupDataDirectory;
        private bool PathsSkipped;
        private readonly BackupManifestWriter ManifestWriter;
        private readonly Logger Logger;

        private Backup(BackupConfig config, IReadOnlyList<PreviousBackup> previousBackups, Logger logger) {
            var previousBackupsSorted = previousBackups.ToList();
            previousBackupsSorted.Sort((a, b) => DateTime.Compare(a.StartTime, b.StartTime));

            Config = config;
            PreviousBackups = previousBackupsSorted;
            PathsSkipped = false;
            Logger = logger;
            BackupDirectory = CreateBackupDirectory();
            BackupDataDirectory = BackupMeta.BackupDataPath(BackupDirectory);
            CreateLogFile(BackupDirectory, Logger);
            ManifestWriter = new(BackupMeta.ManifestFilePath(BackupDirectory));
        }

        /// <summary>
        /// Creates a new directory in the target directory for this backup.
        /// </summary>
        /// <returns>The path to the created directory.</returns>
        /// <exception cref="BackupDirectoryCreateException">If a new backup directory could not be created.
        /// </exception>
        private string CreateBackupDirectory() {
            var path = BackupMeta.CreateBackupDirectory(Config.TargetDirectory);
            Logger.Info($"Created backup directory \"{path}\"");
            return path;
        }

        /// <summary>
        /// Initialises logging to a file in the backup directory. <br/>
        /// If creation of the log file fails, writes a warning to the logs.
        /// </summary>
        /// <param name="backupDirectory">The directory to create the log file in.</param>
        /// <param name="logger">The logger instance to associate with the log file.</param>
        private static void CreateLogFile(string backupDirectory, Logger logger) {
            var path = Path.Join(backupDirectory, LOG_FILENAME);
            try {
                logger.FileHandler = new(path);
            }
            catch (LoggingException e) {
                logger.Warning(e.Message);
                return;
            }
            logger.Info($"Created log file \"{path}\"");
        }

        private BackupResults Run() {
            // Explore directories in a depth-first manner, on the basis that files/folders within the same branch of
            // the filesystem are likely to be modified together, so we want to back them up as close together in time
            // as possible. It's also probably more useful to have some folders fully backed up rather than all folders
            // partially backed up (if using breadth-first), in the case the backup is stopped early.

            List<DirectoryInfo?> searchStack = new() {
                new(Config.SourceDirectory)      // TODO: exception handling
            };
            List<string> path = new();

            bool isRoot = true;
            do {
                var currentDirectory = searchStack[^1];
                searchStack.RemoveAt(searchStack.Count - 1);

                if (currentDirectory == null) {
                    path.RemoveAt(path.Count - 1);
                    ManifestWriter.PopDirectory();
                }
                else {
                    var fullPath = currentDirectory.FullName;      // TODO: exception handling

                    if (IsPathExcluded(fullPath)) {
                        Logger.Info($"Skipped excluded directory \"{fullPath}\"");
                    }
                    else {
                        // Unfortunately we have these inelegant checks for the backup source directory, because that's
                        // a bit of a special case. I don't think it can be avoided.
                        if (!isRoot) {
                            path.Add(currentDirectory.Name);
                        }

                        BackUpDirectory(currentDirectory, path, isRoot);

                        if (!isRoot) {
                            searchStack.Add(null);
                        }

                        var subdirectories = currentDirectory.GetDirectories();     // TODO: exception handling
                        searchStack.AddRange(subdirectories);
                    }
                }

                isRoot = false;
            } while (searchStack.Count > 0);

            if (ManifestWriter.PathDepth != 0) {
                throw new Exception(
                    $"Manifest writer path depth should be 0 after backup, but is {ManifestWriter.PathDepth}");
            }

            return new(PathsSkipped);
        }

        private void BackUpDirectory(DirectoryInfo directory, IEnumerable<string> relativePath, bool isRoot) {
            var relativePathString = string.Join(Path.DirectorySeparatorChar, relativePath);
            var fullBackupPath = Path.Join(BackupDataDirectory, relativePathString);
            Directory.CreateDirectory(fullBackupPath);      // TODO: error handling
            if (!isRoot) {
                ManifestWriter.PushDirectory(directory.Name);     // TODO: exception handling
            }
            BackUpDirectoryFiles(directory, relativePathString, relativePath);
        }

        private void BackUpDirectoryFiles(DirectoryInfo directory, string relativePath,
                IEnumerable<string> relativePathComponents) {
            var files = directory.GetFiles();       // TODO: exception handling
            foreach (var file in files) {
                var fullFilePath = file.FullName;       // TODO: exception handling
                if (IsPathExcluded(fullFilePath)) {
                    Logger.Info($"Skipped excluded file \"{fullFilePath}\"");
                }
                else if (ShouldBackUpFile(relativePathComponents, file.Name, file.LastWriteTimeUtc)) {
                    var backupPath = Path.Join(relativePath, file.Name);
                    BackUpFile(file, backupPath);
                }
            }
        }

        private void BackUpFile(FileInfo file, string relativePath) {
            var backupPath = Path.Join(BackupDataDirectory, relativePath);
            file.CopyTo(backupPath);        // TODO: exception handling
            ManifestWriter.WriteFile(file.Name);
        }

        private bool ShouldBackUpFile(IEnumerable<string> relativePathDirectories, string filename,
                DateTime lastWriteTimeUtc) {
            // Find the last backup that included the file. Then see if the file has been modified since then.
            foreach (var previousBackup in PreviousBackups.Reverse()) {
                // Match the directory segment of the path by walking down the backup tree.
                var directoryMatch = true;
                var node = previousBackup.Manifest.Root;
                foreach (var directory in relativePathDirectories) {
                    try {
                        node = node.Subdirectories.First(s => string.Compare(s.Name, directory, true) == 0);
                    }
                    catch (InvalidOperationException) {
                        directoryMatch = false;
                        break;
                    }
                }
                if (directoryMatch) {
                    if (node.Files.Any(f => string.Compare(f, filename, true) == 0)) {
                        return lastWriteTimeUtc >= previousBackup.StartTime;
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
}
