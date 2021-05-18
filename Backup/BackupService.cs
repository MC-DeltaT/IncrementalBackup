using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace IncrementalBackup
{
    /// <summary>
    /// Results of a backup run.
    /// </summary>
    class BackupResults
    {
        public BackupResults(bool pathsSkipped, bool manifestComplete) {
            PathsSkipped = pathsSkipped;
            ManifestComplete = manifestComplete;
        }

        /// <summary>
        /// Indicates whether any paths were skipped due to I/O errors, permission errors, etc. (NOT inclusive of paths
        /// that were specifically requested to be excluded).
        /// </summary>
        public bool PathsSkipped;

        /// <summary>
        /// Indicates whether all files and directories backed up were recorded in the backup manifest file.
        /// </summary>
        public bool ManifestComplete;
    }

    class BackupService
    {
        /// <summary>
        /// Runs a backup instance. Backs up files from the source directory to the backup directory and records them
        /// in the backup manifest file.
        /// </summary>
        /// <param name="sourcePath">The path of the directory to back up.</param>
        /// <param name="excludePaths">Paths to exclude from the backup. Should be normalised.</param>
        /// <param name="previousBackupSum">Sum of the existing backups for this source directory.</param>
        /// <param name="backupPath">The path of the directory to contain the new backup.</param>
        /// <param name="manifestWriter">Writes the backup manifest. Must be in a newly-constructed state.</param>
        /// <param name="logger">For logging of info and warnings during the backup.</param>
        /// <returns>The results of the backup.</returns>
        /// <exception cref="BackupServiceException">If the source directory can't be accessed.</exception>
        public static BackupResults Run(string sourcePath, IReadOnlyList<string> excludePaths,
                BackupSum previousBackupSum, string backupPath, BackupManifestWriter manifestWriter, Logger logger) =>
            new BackupService(sourcePath, excludePaths, previousBackupSum, backupPath, manifestWriter, logger)
                .Run();

        private BackupService(string sourcePath, IReadOnlyList<string> excludePaths, BackupSum previousBackupSum,
                string backupPath, BackupManifestWriter manifestWriter, Logger logger) {
            SourcePath = sourcePath;
            // Trim trailing directory separator for efficient matching later.
            ExcludePaths = excludePaths.Select(p => Utility.RemoveTrailingDirSep(p)).ToList();
            PreviousBackupSum = previousBackupSum;
            BackupPath = backupPath;
            BackupDataPath = BackupMeta.BackupDataPath(backupPath);
            ManifestWriter = manifestWriter;
            Logger = logger;
            Results = new(false, true);
        }

        /// <summary>
        /// The path of the directory to back up. Normalised.
        /// </summary>
        private readonly string SourcePath;
        /// <summary>
        /// Paths to exclude from the backup. Normalised and trailing directory separator removed.
        /// </summary>
        private readonly IReadOnlyList<string> ExcludePaths;
        /// <summary>
        /// Sum of the existing backups for the source directory.
        /// </summary>
        private readonly BackupSum PreviousBackupSum;
        /// <summary>
        /// The path of the backup directory.
        /// </summary>
        private readonly string BackupPath;
        /// <summary>
        /// The path of the directory to store backed up files.
        /// </summary>
        private readonly string BackupDataPath;

        /// <summary>
        /// Writes backed up directories and files to the backup manifest file.
        /// </summary>
        private readonly BackupManifestWriter ManifestWriter;
        /// <summary>
        /// For logging of info and warnings during the backup.
        /// </summary>
        private readonly Logger Logger;
        /// <summary>
        /// The results of the backup. Updated as the backup progresses.
        /// </summary>
        private readonly BackupResults Results;

        /// <summary>
        /// Performs the back up. <br/>
        /// Should not be called more than once per instance.
        /// </summary>
        /// <returns>The results of the backup.</returns>
        /// <exception cref="BackupServiceException">If the source directory can't be accessed.</exception>
        private BackupResults Run() {
            // Explore directories in a depth-first manner, on the basis that files/directories within the same branch
            // of the filesystem are likely to be modified together, so we want to back them up as close together in
            // time as possible. It's also probably more useful to have some directories fully backed up rather than
            // all directories partially backed up (if using breadth-first), in the case the backup is stopped early.

            SearchState searchState = new(true, new(1000), new(20));

            try {
                var root = FilesystemException.ConvertSystemException(() => new DirectoryInfo(SourcePath),
                    () => SourcePath);
                searchState.NodeStack.Add(state => VisitDirectory(state, root));
            }
            catch (FilesystemException e) {
                throw new BackupServiceException($"Failed to enumerate source directory \"{SourcePath}\": {e.Reason}", e);
            }

            do {
                var currentNode = searchState.NodeStack[^1];
                searchState.NodeStack.RemoveAt(searchState.NodeStack.Count - 1);
                if (!currentNode(searchState)) {
                    break;
                }
                searchState.IsRootNode = false;
            } while (searchState.NodeStack.Count > 0);

            // The search is kinda tricky and took ages to figure out - I don't trust that it fully works.
            if (ManifestWriter.PathDepth != searchState.RelativePathComponents.Count) {
                throw new LogicException(
                    $"Manifest writer path depth should be {searchState.RelativePathComponents.Count} after backup, but is {ManifestWriter.PathDepth}");
            }

            return Results;
        }

        /// <summary>
        /// A search action that backs up the current search directory and queues further search actions for its
        /// subdirectories.
        /// </summary>
        /// <param name="state">The search state to operate on.</param>
        /// <param name="directory">The directory to back up.</param>
        /// <returns><c>true</c> (for compatibility with the search action interface).</returns>
        private bool VisitDirectory(SearchState state, DirectoryInfo directory) {
            // Unfortunately we have these inelegant checks for the backup source directory, because that's
            // a bit of a special case. I don't think it can be avoided.
            if (!state.IsRootNode) {
                state.RelativePathComponents.Add(directory.Name);
            }

            string relativePath = string.Join(Path.DirectorySeparatorChar, state.RelativePathComponents);
            Lazy<string> fullPath = new(() => Path.Join(SourcePath, relativePath), false);

            // Probably a good idea to get the full path from the system rather than form it ourselves, to make
            // sure it's normalised correctly.
            string? fullPathNormalised = null;
            try {
                fullPathNormalised = FilesystemException.ConvertSystemException(
                    () => directory.FullName, () => fullPath.Value);
            }
            catch (FilesystemException e) {
                Logger.Warning(
                    $"Failed to read metadata of directory \"{fullPath.Value}\": {e.Reason}");
                Logger.Warning($"Skipping directory \"{fullPath.Value}\"");
                Results.PathsSkipped = true;
            }

            var backupSumEntry = PreviousBackupSum.FindDirectory(state.RelativePathComponents);

            bool directoryRecorded = false;
            DirectoryInfo[]? subdirectories = null;

            if (fullPathNormalised is not null) {
                if (IsPathExcluded(fullPathNormalised)) {
                    Logger.Info($"Skipping excluded directory \"{fullPathNormalised}\"");
                }
                else {
                    directoryRecorded = BackUpDirectory(directory, relativePath, backupSumEntry, state.IsRootNode,
                        fullPathNormalised);

                    if (directoryRecorded) {
                        try {
                            subdirectories = FilesystemException.ConvertSystemException(
                                directory.GetDirectories, () => fullPathNormalised);
                        }
                        catch (FilesystemException e) {
                            Logger.Warning(
                                $"Failed to enumerate subdirectories of directory \"{fullPathNormalised}\": {e.Reason}");
                            Logger.Warning($"Skipping subdirectories of directory \"{fullPathNormalised}\"");
                            Results.PathsSkipped = true;
                        }
                    }
                }
            }

            if (!state.IsRootNode) {
                state.NodeStack.Add(state => Backtrack(state, directoryRecorded));
            }

            if (subdirectories is not null) {
                // Record subdirectories that were removed since last backup.
                if (backupSumEntry is not null) {
                    foreach (var existingDir in backupSumEntry.Subdirectories) {
                        if (!subdirectories.Any(d => Utility.PathEqual(d.Name, existingDir.Name))) {
                            RecordRemovedDirectory(existingDir.Name,
                                new(() => Path.Join(fullPathNormalised!, existingDir.Name)));
                        }
                    }
                }

                state.NodeStack.AddRange(subdirectories.Reverse()
                    .Select<DirectoryInfo, Func<SearchState, bool>>(d => state => VisitDirectory(state, d)));
            }

            return true;
        }

        /// <summary>
        /// A search action that backtracks the search state from the current search directory to its parent directory.
        /// </summary>
        /// <param name="state">The search state to operate on.</param>
        /// <param name="backtrackManifest">Indicates whether to backtrack the directory in the backup manifest.
        /// </param>
        /// <returns><c>true</c> if the operation completed successfully. <c>false</c> if the backup manifest could not
        /// be written to, and thus the search should terminate.</returns>
        private bool Backtrack(SearchState state, bool backtrackManifest) {
            // Only backtrack the manifest if the directory was pushed, which may not happen every time (e.g. if the
            // directory was skipped).
            if (backtrackManifest) {
                try {
                    ManifestWriter.BacktrackDirectory();
                }
                catch (BackupManifestFileIOException e) {
                    Logger.Warning(
                        $"Failed to write to manifest file: {e.InnerException.Reason}");
                    Logger.Warning("Stopping backup");
                    Results.PathsSkipped = true;
                    return false;
                }
            }

            state.RelativePathComponents.RemoveAt(state.RelativePathComponents.Count - 1);

            return true;
        }

        /// <summary>
        /// Backs up a directory and the files contained directly within it. This involves:
        /// <list type="bullet">
        /// <item>Creating a backup directory for that directory, and recording it in the backup manifest.</item>
        /// <item>Copying modified/new files to their respective locations in the backup data directory, and recording
        /// those files in the backup manifest.</item>
        /// <item>Recording files in the backup manifest which were present in the previous backup, but are now
        /// removed.</item>
        /// </list>
        /// If the directory or any files could not be copied, sets <see cref="Results.PathsSkipped"/> to <c>true</c>. <br/>
        /// If the directory or any files were examined but could not be recorded in the backup manifest, sets
        /// <see cref="Results.ManifestComplete"/> to <c>false</c>.
        /// </summary>
        /// <param name="directory">The directory to copy.</param>
        /// <param name="relativePath">The path of the directory, relative to the backup source directory.</param>
        /// <param name="backupSumEntry">The previous backup sum entry for the directory. May be <c>null</c> if the
        /// directory isn't present in previous backups.</param>
        /// <param name="isRoot"><c>true</c> if the directory is the backup source directory, otherwise <c>false</c>.
        /// </param>
        /// <param name="fullPath">The full path of the directory.</param>
        /// <returns><c>true</c> if the directory was recorded to <see cref="ManifestWriter"/>, otherwise <c>false</c>.
        /// </returns>
        private bool BackUpDirectory(DirectoryInfo directory, string relativePath, BackupSum.Directory? backupSumEntry,
                bool isRoot, string fullPath) {
            var directoryBackupPath = Path.Join(BackupDataPath, relativePath);

            try {
                FilesystemException.ConvertSystemException(() => Directory.CreateDirectory(directoryBackupPath),
                    () => directoryBackupPath);
            }
            catch (FilesystemException e) {
                Logger.Warning(
                    $"Failed to back up directory \"{fullPath}\" to \"{directoryBackupPath}\": {e.Reason}");
                Logger.Warning($"Skipping directory \"{fullPath}\"");
                Results.PathsSkipped = true;
                return false;
            }

            if (!isRoot) {
                // TODO? consider if writing to manifest provides strong exception guarantee. Maybe abort backup
                // completely?
                if (!RecordDirectoryEntered(directory.Name, fullPath)) {
                    Logger.Warning($"Skipping contents of directory \"{fullPath}\"");
                    Results.PathsSkipped = true;
                    return false;
                }
            }

            BackUpDirectoryFiles(directory, backupSumEntry, directoryBackupPath, fullPath);

            return true;
        }

        /// <summary>
        /// Backs up the files directly contained in a directory. This involves:
        /// <list type="bullet">
        /// <item>Copying modified/new files to their respective locations in the backup data directory.</item>
        /// <item>Recording copied files in the backup manifest.</item>
        /// <item>Recording files in the backup manifest which were present in the previous backup, but are now
        /// removed.</item>
        /// </list>
        /// If any files couldn't be copied, sets <see cref="Results.PathsSkipped"/> to <c>true</c>.
        /// If writing to the manifest fails, sets <see cref="Results.ManifestComplete"/> to <c>false</c>. <br/>
        /// </summary>
        /// <param name="directory">The directory whose files to copy.</param>
        /// <param name="backupSumEntry">The previous backup sum entry for the directory. May be <c>null</c> if the
        /// directory isn't present in previous backups.</param>
        /// <param name="directoryBackupPath">The backup path for the directory.</param>
        /// <param name="fullDirectoryPath">The full path to the directory.</param>
        private void BackUpDirectoryFiles(DirectoryInfo directory, BackupSum.Directory? backupSumEntry,
                string directoryBackupPath, string fullDirectoryPath) {
            var files = GetDirectoryFiles(directory, fullDirectoryPath);
            if (files is null) {
                Logger.Warning($"Skipping files in directory \"{fullDirectoryPath}\"");
                Results.PathsSkipped = true;
                return;
            }

            foreach (var file in files) {
                Lazy<string> fullFilePath = new(() => Path.Join(fullDirectoryPath, file.Name));

                var metadata = GetFileMetadata(file, fullFilePath);
                if (metadata is null) {
                    Logger.Warning($"Skipping file \"{fullFilePath.Value}\"");
                    Results.PathsSkipped = true;
                    continue;
                }

                // The ?? default is strangely required, apparently if the tuple type is nullable you can never
                // deconstruct it normally, even if we assert the value is not null.
                var (fullFilePathNormalised, lastWriteTimeUtc) = metadata ?? default;

                if (IsPathExcluded(fullFilePathNormalised)) {
                    Logger.Info($"Skipped excluded file \"{fullFilePathNormalised}\"");
                }
                else if (ShouldBackUpFile(backupSumEntry, file.Name, lastWriteTimeUtc)) {
                    BackUpFile(file, directoryBackupPath, fullDirectoryPath);
                }
            }

            // Record files that were removed since last backup.
            if (backupSumEntry is not null) {
                foreach (var existingFile in backupSumEntry.Files) {
                    if (!files.Any(f => Utility.PathEqual(f.Name, existingFile.Name))) {
                        RecordRemovedFile(existingFile.Name,
                            new(() => Path.Join(fullDirectoryPath, existingFile.Name)));
                    }
                }
            }
        }

        /// <summary>
        /// Copies a file in the backup source directory to its respective location in the backup data directory. <br/>
        /// If the file is copied, records it to the backup manifest. If writing to the manifest fails, sets
        /// <see cref="Results.ManifestComplete"/> to <c>false</c>. <br/>
        /// If the file can't be copied, sets <see cref="Results.PathsSkipped"/> to <c>true</c>.
        /// </summary>
        /// <param name="file">The file to copy.</param>
        /// <param name="directoryBackupPath">The backup path for the file's parent directory.</param>
        /// <param name="fullDirectoryPath">The full path to the file's parent directory.</param>
        private void BackUpFile(FileInfo file, string directoryBackupPath, string fullDirectoryPath) {
            Lazy<string> fullFilePath = new(() => Path.Join(fullDirectoryPath, file.Name), false);
            var fileBackupPath = Path.Join(directoryBackupPath, file.Name);

            try {
                // I think if access is denied it must be due to the destination path, because we already have a
                // loaded FileInfo for the source file (which should imply permission to read).

                FilesystemException.ConvertSystemException(
                    () => file.CopyTo(fileBackupPath, true),
                    () => fileBackupPath,
                    // Don't know which path the exception came from :/
                    ioExceptionHandler: (e) => new FilesystemException(null, e.Message));
            }
            catch (FilesystemException e) {
                Logger.Warning(
                    $"Failed to back up file \"{fullFilePath.Value}\" to \"{fileBackupPath}\": {e.Reason}");
                Logger.Warning($"Skipped file \"{fullFilePath.Value}\"");
                Results.PathsSkipped = true;
                return;
            }

            RecordBackedUpFile(file.Name, fullFilePath);
        }

        /// <summary>
        /// Gets the files directly contained in a directory.
        /// </summary>
        /// <param name="directory">A handle to the directory.</param>
        /// <param name="fullPath">Gets the full path of the directory (for error information).</param>
        /// <returns>An array of the files in the directory, in arbitrary order, or <c>null</c> if the operation fails.
        /// </returns>
        private FileInfo[]? GetDirectoryFiles(DirectoryInfo directory, string fullPath) {
            try {
                return FilesystemException.ConvertSystemException(directory.GetFiles, () => fullPath);
            }
            catch (FilesystemException e) {
                Logger.Warning(
                    $"Failed to enumerate files in directory \"{fullPath}\": {e.Reason}");
                return null;
            }
        }

        /// <summary>
        /// Gets the normalised full path and last write time of a file.
        /// </summary>
        /// <param name="file">A handle to the file.</param>
        /// <param name="fullPath">Gets the full path of the file (for error information).</param>
        /// <returns>A tuple of (normalised full path, last write time UTC), or <c>null</c> if the operation fails.
        /// </returns>
        private (string, DateTime)? GetFileMetadata(FileInfo file, Lazy<string> fullPath) {
            // We get the full path from the system rather than form it ourselves, to make sure it's normalised
            // correctly.
            try {
                return FilesystemException.ConvertSystemException(() => (file.FullName, file.LastWriteTimeUtc),
                    () => fullPath.Value);
            }
            catch (FilesystemException e) {
                Logger.Warning(
                    $"Failed to read metadata of file \"{fullPath.Value}\": {e.Reason}");
                return null;
            }
        }

        /// <summary>
        /// Changes the backup manifest current search directory to a subdirectory. <br/>
        /// If the operation fails, sets <see cref="Results.ManifestComplete"/> to <c>false</c>.
        /// </summary>
        /// <param name="name">The name of the directory.</param>
        /// <param name="fullPath">The full path of the directory (for error information).</param>
        /// <returns><c>true</c> if the operation succeeded, otherwise <c>false</c>.</returns>
        private bool RecordDirectoryEntered(string name, string fullPath) {
            try {
                // TODO? can this provide strong exception guarantee?
                ManifestWriter.EnterDirectory(name);
                return true;
            }
            catch (BackupManifestFileIOException e) {
                Logger.Warning(
                    $"Failed to record directory \"{fullPath}\" in manifest file: {e.InnerException.Message}");
                Results.ManifestComplete = false;
                return false;
            }
        }

        /// <summary>
        /// Records a directory in the current search directory as removed since the last backup in the backup
        /// manifest. <br/>
        /// If the operation fails, sets <see cref="Results.ManifestComplete"/> to <c>false</c>.
        /// </summary>
        /// <param name="name">The name of the directory.</param>
        /// <param name="fullPath">Gets the full path of the directory (for error information).</param>
        private void RecordRemovedDirectory(string name, Lazy<string> fullPath) {
            try {
                ManifestWriter.RecordDirectoryRemoved(name);
            }
            catch (BackupManifestFileIOException e) {
                Logger.Warning(
                    $"Failed to record removed directory \"{fullPath.Value}\" in manifest file: {e.InnerException.Reason}");
                Results.ManifestComplete = false;
            }
        }

        /// <summary>
        /// Records a file in the current search directory as backed up (copied) in the backup manifest. <br/>
        /// If the operation fails, sets <see cref="Results.ManifestComplete"/> to <c>false</c>.
        /// </summary>
        /// <param name="name">The name of the file.</param>
        /// <param name="fullPath">Gets the full path of the file (for error information).</param>
        private void RecordBackedUpFile(string name, Lazy<string> fullPath) {
            try {
                ManifestWriter.RecordFileBackedUp(name);
            }
            catch (BackupManifestFileIOException e) {
                Logger.Warning(
                    $"Failed to record backed up file \"{fullPath.Value}\" in manifest file: {e.InnerException.Reason}");
                Results.ManifestComplete = false;
            }
        }

        /// <summary>
        /// Records a file in the current search directory as removed since the last backup in the backup manifest. <br/>
        /// If the operation fails, sets <see cref="Results.ManifestComplete"/> to <c>false</c>.
        /// </summary>
        /// <param name="name">The name of the file.</param>
        /// <param name="fullPath">Gets the full path of the file (for error information).</param>
        private void RecordRemovedFile(string name, Lazy<string> fullPath) {
            try {
                ManifestWriter.RecordFileRemoved(name);
            }
            catch (BackupManifestFileIOException e) {
                Logger.Warning(
                    $"Failed to record removed file \"{fullPath.Value}\" in manifest file: {e.InnerException.Reason}");
                Results.ManifestComplete = false;
            }
        }

        /// <summary>
        /// Checks if a file should be backed up based on previous backups. <br />
        /// Specifically, checks if the file has been modified since the last backup which included it.
        /// </summary>
        /// <param name="backupSumDirectoryEntry">The previous backup sum entry for the directory that contains the
        /// file. May be <c>null</c> if the directory isn't present in previous backups.</param>
        /// <param name="filename">The name of the file to check.</param>
        /// <param name="lastWriteTimeUtc">The UTC time the file was last modified.</param>
        /// <returns><c>true</c> if the file should be backed up, otherwise <c>false</c>.</returns>
        private static bool ShouldBackUpFile(BackupSum.Directory? backupSumDirectoryEntry, string filename,
                DateTime lastWriteTimeUtc) {
            var file = backupSumDirectoryEntry?.Files.Find(f => Utility.PathEqual(f.Name, filename));
            if (file is null) {
                // File has never been backed up.
                return true;
            }
            else {
                return lastWriteTimeUtc >= file.LastBackup.StartInfo.StartTime;
            }
        }

        /// <summary>
        /// Checks if a path matches any paths in the excluded paths configuration, and thus should be excluded
        /// from the back up.
        /// </summary>
        /// <remarks>
        /// Uses case-insensitive path matching, so only works on Windows.
        /// </remarks>
        /// <param name="path">The full path to check.</param>
        /// <returns><c>true</c> if the path is excluded from the backup, otherwise <c>false</c>.</returns>
        private bool IsPathExcluded(string path) {
            path = Utility.RemoveTrailingDirSep(path);
            return ExcludePaths.Any(p => Utility.PathEqual(path, p));
        }

        /// <summary>
        /// Contains the state for the backup source directory search.
        /// </summary>
        private class SearchState
        {
            public SearchState(bool isRootNode, List<Func<SearchState, bool>> nodeStack,
                    List<string> relativePathComponents) {
                IsRootNode = isRootNode;
                NodeStack = nodeStack;
                RelativePathComponents = relativePathComponents;
            }

            /// <summary>
            /// Indicates if the current node is the backup source directory.
            /// </summary>
            public bool IsRootNode;
            /// <summary>
            /// The stack of search operations queued to be performed. Each operation accepts the current search state
            /// and returns a boolean indicating whether the search should continue.
            /// </summary>
            public List<Func<SearchState, bool>> NodeStack;
            /// <summary>
            /// The components of the path of the current search directory, relative to the backup source directory.
            /// </summary>
            public List<string> RelativePathComponents;
        }
    }

    /// <summary>
    /// Indicates that a backup operation failed completely, i.e. could not be started and no files were copied.
    /// </summary>
    class BackupServiceException : Exception
    {
        public BackupServiceException(string message, FilesystemException innerException) :
            base(message, innerException) { }

        public new FilesystemException InnerException =>
            (FilesystemException)base.InnerException!;
    }
}
