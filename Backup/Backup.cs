﻿using System;
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

    class Backup
    {
        /// <summary>
        /// Runs a backup instance. Backs up files from the source directory to the backup directory and records them
        /// in the backup manifest file.
        /// </summary>
        /// <param name="sourcePath">The path of the directory to back up.</param>
        /// <param name="excludePaths">Paths to exclude from the backup. Should be normalised.</param>
        /// <param name="previousBackups">The existing backups for this source directory.</param>
        /// <param name="backupPath">The path of the directory to contain the new backup.</param>
        /// <param name="manifestWriter">Writes the backup manifest. Must be in a newly-constructed state.</param>
        /// <param name="logger">For logging of info during the backup.</param>
        /// <returns>The results of the backup.</returns>
        /// <exception cref="BackupException">If the source directory can't be accessed.</exception>
        public static BackupResults Run(string sourcePath, IReadOnlyList<string> excludePaths,
                IReadOnlyList<PreviousBackup> previousBackups, string backupPath, BackupManifestWriter manifestWriter,
                Logger logger) =>
            new Backup(sourcePath, excludePaths, previousBackups, backupPath, manifestWriter, logger).Run();

        private Backup(string sourcePath, IReadOnlyList<string> excludePaths,
                IReadOnlyList<PreviousBackup> previousBackups, string backupPath, BackupManifestWriter manifestWriter,
                Logger logger) {
            var previousBackupsSorted = previousBackups.ToList();
            previousBackupsSorted.Sort((a, b) => DateTime.Compare(a.StartTime, b.StartTime));

            SourcePath = sourcePath;
            ExcludePaths = excludePaths;
            PreviousBackups = previousBackupsSorted;
            BackupPath = backupPath;
            BackupDataPath = BackupMeta.BackupDataPath(backupPath);
            ManifestWriter = manifestWriter;
            Logger = logger;
            Results = new(false, true);
        }

        // Configuration data.
        private readonly string SourcePath;
        private readonly IReadOnlyList<string> ExcludePaths;
        private readonly IReadOnlyList<PreviousBackup> PreviousBackups;
        private readonly string BackupPath;
        private readonly string BackupDataPath;

        // Mutable state.
        private readonly BackupManifestWriter ManifestWriter;
        private readonly Logger Logger;
        private readonly BackupResults Results;

        /// <summary>
        /// Performs the back up. <br/>
        /// Should not be called more than once per instance.
        /// </summary>
        /// <returns>The results of the backup.</returns>
        /// <exception cref="BackupException">If the source directory can't be accessed.</exception>
        private BackupResults Run() {
            // Explore directories in a depth-first manner, on the basis that files/directories within the same branch
            // of the filesystem are likely to be modified together, so we want to back them up as close together in
            // time as possible. It's also probably more useful to have some directories fully backed up rather than
            // all directories partially backed up (if using breadth-first), in the case the backup is stopped early.

            SearchState searchState = new(true, new(), new());

            try {
                var root = FilesystemException.ConvertSystemException(() => new DirectoryInfo(SourcePath),
                    () => SourcePath);
                searchState.NodeStack.Add(state => VisitDirectory(state, root));
            }
            catch (FilesystemException e) {
                throw new BackupException($"Failed to enumerate source directory \"{SourcePath}\": {e.Reason}", e);
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
                    $"Failed to read metadata of directory \"{fullPath.Value}\" ({e.Reason}); skipping");
                Results.PathsSkipped = true;
            }

            bool directoryRecorded = false;
            DirectoryInfo[]? subdirectories = null;

            if (fullPathNormalised is not null) {
                if (IsPathExcluded(fullPathNormalised)) {
                    Logger.Info($"Skipping excluded directory \"{fullPathNormalised}\"");
                }
                else {
                    directoryRecorded = BackUpDirectory(directory, relativePath,
                        state.RelativePathComponents, state.IsRootNode, fullPathNormalised);

                    if (directoryRecorded) {
                        try {
                            subdirectories = FilesystemException.ConvertSystemException(
                                directory.GetDirectories, () => fullPathNormalised);
                        }
                        catch (FilesystemException e) {
                            Logger.Warning(
                                $"Failed to enumerate subdirectories of \"{fullPathNormalised}\" ({e.Reason}); skipping");
                            Results.PathsSkipped = true;
                        }
                    }
                }
            }

            if (!state.IsRootNode) {
                state.NodeStack.Add(state => Backtrack(state, directoryRecorded));
            }

            if (subdirectories is not null) {
                state.NodeStack.AddRange(subdirectories.Reverse()
                    .Select<DirectoryInfo, Func<SearchState, bool>>(d => state => VisitDirectory(state, d)));
            }

            return true;
        }

        private bool Backtrack(SearchState state, bool popManifest) {
            // Only pop a directory from the manifest if the directory was pushed, which may not happen every time
            // (e.g. if the directory was skipped).
            if (popManifest) {
                try {
                    ManifestWriter.PopDirectory();
                }
                catch (BackupManifestFileIOException e) {
                    Logger.Warning(
                        $"Failed to write to backup manifest file \"{ManifestWriter.FilePath}\" ({e.InnerException.Reason}); stopping backup");
                    Results.PathsSkipped = true;
                    return false;
                }
            }

            state.RelativePathComponents.RemoveAt(state.RelativePathComponents.Count - 1);

            return true;
        }

        /// <summary>
        /// Copies a directory and its directly contained files to their respective locations in the backup data
        /// directory. <br/>
        /// If the directory was created, records it to <see cref="ManifestWriter"/>. <br/>
        /// Files in the directory that were backed up are recorded to <see cref="ManifestWriter"/>. <br/>
        /// If the directory or any files could not be copied, sets <see cref="PathsSkipped"/> to <c>true</c>. <br/>
        /// If the directory or any files were backed up but could not be written to the backup manifest, sets
        /// <see cref="ManifestComplete"/> to <c>false</c>.
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
            var directoryBackupPath = Path.Join(BackupDataPath, relativePath);

            try {
                FilesystemException.ConvertSystemException(() => Directory.CreateDirectory(directoryBackupPath),
                    () => directoryBackupPath);
            }
            catch (FilesystemException e) {
                Logger.Warning(
                    $"Failed to back up directory \"{fullPath}\" to \"{directoryBackupPath}\" ({e.Reason}); skipping");
                Results.PathsSkipped = true;
                return false;
            }

            if (!isRoot) {
                try {
                    // TODO: consider if this provides strong exception guarantee. Maybe abort backup completely?
                    ManifestWriter.PushDirectory(directory.Name);
                }
                catch (BackupManifestFileIOException e) {
                    Logger.Warning(
                        $"Failed to record directory \"{fullPath}\" in manifest file \"{ManifestWriter.FilePath}\" ({e.InnerException.Message}); skipping");
                    Results.PathsSkipped = true;
                    Results.ManifestComplete = false;
                    return false;
                }
            }

            BackUpDirectoryFiles(directory, relativePathComponents, directoryBackupPath, fullPath);

            return true;
        }

        /// <summary>
        /// Copies all the files directly contained within a directory to their respective locations in the backup data
        /// directory. <br/>
        /// Copied files are recorded to <see cref="ManifestWriter"/>. If writing to the manifest fails, sets
        /// <see cref="ManifestComplete"/> to <c>false</c>. <br/>
        /// If any files couldn't be copied, sets <see cref="PathsSkipped"/> to <c>true</c>.
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
                Results.PathsSkipped = true;
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
                    Results.PathsSkipped = true;
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
        /// If the file is copied, records it to <see cref="ManifestWriter"/>. If writing to the manifest fails, sets
        /// <see cref="ManifestComplete"/> to <c>false</c>. <br/>
        /// If the file can't be copied, sets <see cref="PathsSkipped"/> to <c>true</c>.
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
                    $"Failed to back up file \"{fullFilePath.Value}\" to \"{fileBackupPath}\" ({e.Reason}); skipping");
                Results.PathsSkipped = true;
                return;
            }

            // Going to ignore and continue if the file is copied but can't record the file in the backup manifest. May
            // as well keep the file there anyway.
            try {
                ManifestWriter.RecordFile(file.Name);
            }
            catch (BackupManifestFileIOException e) {
                Logger.Warning(
                    $"Failed to record file \"{fullFilePath.Value}\" in manifest file \"{ManifestWriter.FilePath}\" ({e.InnerException.Reason})");
                Results.ManifestComplete = false;
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
            return ExcludePaths.Any(p => string.Compare(path, Utility.RemoveTrailingDirSep(p), true) == 0);
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
    class BackupException : Exception
    {
        public BackupException(string message, FilesystemException innerException) :
            base(message, innerException) { }

        public new FilesystemException InnerException =>
            (FilesystemException)base.InnerException!;
    }
}