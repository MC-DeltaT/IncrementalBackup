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
    /// Results of <see cref="IncrementalBackup.Backup.Run(BackupConfig, IReadOnlyList{PreviousBackup}, Logger)"/>.
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
        /// <returns>The results of the backup.</returns>
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
        /// Creates a new directory in the target directory for this backup. <br/>
        /// If successful, writes to <see cref="Logger"/>
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
        /// Initialises logging to a file (with <see cref="FileLogHandler"/>) in the backup directory. <br/>
        /// If creation of the log file fails, writes a warning to <see cref="Logger"/>.
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

        /// <summary>
        /// Performs the back up.
        /// </summary>
        /// <returns>The results of the backup.</returns>
        private BackupResults Run() {
            // Explore directories in a depth-first manner, on the basis that files/folders within the same branch of
            // the filesystem are likely to be modified together, so we want to back them up as close together in time
            // as possible. It's also probably more useful to have some folders fully backed up rather than all folders
            // partially backed up (if using breadth-first), in the case the backup is stopped early.

            PathsSkipped = false;

            List<DirectoryInfo?> searchStack = new() {
                new(Config.SourceDirectory)      // TODO: exception handling
            };
            List<string> relativePathComponents = new();

            try {
                var root = FilesystemException.ConvertSystemException(() => new DirectoryInfo(Config.SourceDirectory),
                    () => Config.SourceDirectory);
                searchStack.Add(root);
            }
            catch (FilesystemException e) {
                throw new ...;      // TODO
            }

            bool isRoot = true;
            do {
                var currentDirectory = searchStack[^1];
                searchStack.RemoveAt(searchStack.Count - 1);

                if (currentDirectory is null) {
                    relativePathComponents.RemoveAt(relativePathComponents.Count - 1);
                    ManifestWriter.PopDirectory();
                }
                else {
                    // Unfortunately we have these inelegant checks for the backup source directory, because that's
                    // a bit of a special case. I don't think it can be avoided.
                    if (!isRoot) {
                        relativePathComponents.Add(currentDirectory.Name);
                    }

                    string relativePath = string.Join(Path.DirectorySeparatorChar, relativePathComponents);
                    Lazy<string> fullPath = new(() => Path.Join(Config.SourceDirectory, relativePath), false);

                    // Probably a good idea to get the full path from the system rather than form it ourselves, to make
                    // sure it's normalised correctly.
                    string? fullPathNormalised = null;
                    try {
                        fullPathNormalised = FilesystemException.ConvertSystemException(
                            () => currentDirectory.FullName,
                            () => fullPath.Value);
                    }
                    catch (FilesystemException e) {
                        Logger.Warning(
                            $"Failed to read metadata of directory \"{fullPath.Value}\" ({e.Reason}); skipping");
                        PathsSkipped = true;
                    }

                    if (fullPathNormalised is not null) {
                        if (IsPathExcluded(fullPathNormalised)) {
                            Logger.Info($"Skipped excluded directory \"{fullPathNormalised}\"");
                        }
                        else {
                            bool directoryRecorded = BackUpDirectory(currentDirectory, relativePath,
                                relativePathComponents, isRoot, fullPathNormalised);

                            if (!isRoot && directoryRecorded) {
                                searchStack.Add(null);
                            }

                            DirectoryInfo[]? subdirectories = null;
                            try {
                                subdirectories = FilesystemException.ConvertSystemException(
                                    currentDirectory.GetDirectories,
                                    () => fullPathNormalised!);
                            }
                            catch (FilesystemException e) {
                                Logger.Warning(
                                    $"Failed to enumerate subdirectories of \"{fullPath.Value}\": ({e.Reason}); skipping");
                                PathsSkipped = true;
                            }
                            if (subdirectories is not null) {
                                searchStack.AddRange(subdirectories);
                            }
                        }
                    }
                }

                isRoot = false;
            } while (searchStack.Count > 0);

            // The search is kinda tricky and too ages to figure out - I don't trust that it fully works.
            if (ManifestWriter.PathDepth != 0) {
                throw new LogicException(
                    $"Manifest writer path depth should be 0 after backup, but is {ManifestWriter.PathDepth}");
            }

            return new(PathsSkipped);
        }

        /// <summary>
        /// Copies a directory and its directly contained files to their respective locations in the backup data
        /// directory. <br/>
        /// If the directory was created, records it to <see cref="ManifestWriter"/>. <br/>
        /// If the directory or any files could not be copied, sets <see cref="PathsSkipped"/> to <c>true</c> and
        /// writes a warning to <see cref="Logger"/>.
        /// </summary>
        /// <param name="directory">The directory to copy.</param>
        /// <param name="relativePath">The path of the directory, relative to the backup source directory.</param>
        /// <param name="relativePathComponents">The components of the path of the directory, relative to the backup
        /// source directory.</param>
        /// <param name="isRoot"><c>true</c> if the directory is the backup source directory, otherwise <c>false</c>.
        /// </param>
        /// <param name="fullPath">The full path of the directory.</param>
        /// <returns><c>true</c> if the directory was recorded to <see cref="ManifestWriter"/>, otherwise <c>false</c>.
        /// </returns>
        private bool BackUpDirectory(DirectoryInfo directory, string relativePath,
                IEnumerable<string> relativePathComponents, bool isRoot, string fullPath) {
            var backupPath = Path.Join(BackupDataDirectory, relativePath);

            try {
                FilesystemException.ConvertSystemException(() => Directory.CreateDirectory(backupPath),
                    () => backupPath);
            }
            catch (FilesystemException e) {
                Logger.Warning($"Failed to create backup directory for {fullPath} ({e.Reason}); skipping");
                PathsSkipped = true;
                return false;
            }

            if (!isRoot) {
                try {
                    // TODO: consider if this provides strong exception guarantee. Maybe abort backup completely?
                    ManifestWriter.PushDirectory(directory.Name);
                }
                catch (BackupManifestFileIOException e) {
                    Logger.Warning(
                        $"Failed to record directory \"{fullPath}\" in manifest file ({e.InnerException.Message}); skipping");
                    PathsSkipped = true;
                    return false;
                }
            }

            BackUpDirectoryFiles(directory, relativePathComponents, backupPath, fullPath);

            return true;
        }

        /// <summary>
        /// Copies all the files directly contained within a directory to their respective locations in the backup data
        /// directory. <br/>
        /// Copied files are recorded to <see cref="ManifestWriter"/>. <br/>
        /// If any files couldn't be copied, sets <see cref="PathsSkipped"/> to <c>true</c> and writes a warning to
        /// <see cref="Logger"/>.
        /// </summary>
        /// <param name="directory">The directory whose files to copy.</param>
        /// <param name="relativePathComponents">The components of the directory's path, relative to the backup source
        /// directory.</param>
        /// /// <param name="directoryBackupPath">The backup path for the directory.</param>
        /// <param name="fullDirectoryPath">The full path to the directory.</param>
        private void BackUpDirectoryFiles(DirectoryInfo directory, IEnumerable<string> relativePathComponents,
                string directoryBackupPath, string fullDirectoryPath) {
            FileInfo[] files;
            try {
                files = FilesystemException.ConvertSystemException(directory.GetFiles, () => fullDirectoryPath);
            }
            catch (FilesystemException e) {
                Logger.Warning(
                    $"Failed to enumerate files in directory \"{fullDirectoryPath}\" ({e.Reason}); skipping");
                PathsSkipped = true;
                return;
            }

            foreach (var file in files) {
                Lazy<string> fullFilePath = new(() => Path.Join(fullDirectoryPath, file.Name));

                // Probably a good idea to get the full path from the system rather than form it ourselves, to make
                // sure it's normalised correctly.
                string fullFilePathNormalised;
                DateTime lastWriteTimeUtc;
                try {
                    (fullFilePathNormalised, lastWriteTimeUtc) = FilesystemException.ConvertSystemException(
                        () => (file.FullName, file.LastWriteTimeUtc),
                        () => fullFilePath.Value);
                }
                catch (FilesystemException e) {
                    Logger.Warning(
                        $"Failed to read metadata of file \"{fullFilePath.Value}\" ({e.Reason}); skipping");
                    PathsSkipped = true;
                    continue;
                }

                if (IsPathExcluded(fullFilePathNormalised)) {
                    Logger.Info($"Skipped excluded file \"{fullFilePathNormalised}\"");
                }
                else if (ShouldBackUpFile(relativePathComponents, file.Name, lastWriteTimeUtc)) {
                    BackUpFile(file, directoryBackupPath, fullDirectoryPath);
                }
            }
        }

        /// <summary>
        /// Copies a file in the backup source directory to its respective location in the backup data directory. <br/>
        /// If the file is copied, records it to <see cref="ManifestWriter"/>. <br/>
        /// If the file can't be copied, sets <see cref="PathsSkipped"/> to <c>true</c> and writes a warning to
        /// <see cref="Logger"/>.
        /// </summary>
        /// <param name="file">The file to copy.</param>
        /// <param name="directoryBackupPath">The backup path for the file's parent directory.</param>
        /// <param name="fullDirectoryPath">The full path to the file's parent directory.</param>
        private void BackUpFile(FileInfo file, string directoryBackupPath, string fullDirectoryPath) {
            Lazy<string> fullFilePath = new(() => Path.Join(fullDirectoryPath, file.Name), false);
            var backupPath = Path.Join(directoryBackupPath, file.Name);

            try {
                // I think if access is denied it must be due to the destination path, because we already have a
                // loaded FileInfo for the source file (which should imply permission to read).

                FilesystemException.ConvertSystemException(
                    () => file.CopyTo(backupPath, true),
                    () => backupPath,
                    // Don't know which path the exception came from :/
                    ioExceptionHandler: (e) => new FilesystemException(null, e.Message));
            }
            catch (FilesystemException e) {
                Logger.Warning($"Failed to copy file \"{fullFilePath.Value}\" ({e.Reason}); skipping");
                PathsSkipped = true;
                return;
            }

            // Going to ignore and continue if the file is copied but can't record the file in the backup manifest. May
            // as well keep the file there anyway.
            try {
                ManifestWriter.RecordFile(file.Name);
            }
            catch (BackupManifestFileIOException e) {
                Logger.Warning(
                    $"Failed to record file \"{fullFilePath.Value}\" in manifest file ({e.InnerException.Reason})");
            }
        }

        /// <summary>
        /// Checks if a file should be backed up based on previous backups. <br />
        /// Specifically, checks if the file has been modified since the last backup which included it.
        /// </summary>
        /// <param name="relativePathDirectories">The directory components of the file path, relative to the backup
        /// source directory.</param>
        /// <param name="filename">The name of the file to check.</param>
        /// <param name="lastWriteTimeUtc">The UTC time the file was last modified.</param>
        /// <returns><c>true</c> if the file should be backed up, otherwise <c>false</c>.</returns>
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

    class BackupException : Exception
    {
        public BackupException(string message, FilesystemException innerException) :
            base(message, innerException) { }

        public new FilesystemException InnerException =>
            (FilesystemException)base.InnerException!;
    }
}
