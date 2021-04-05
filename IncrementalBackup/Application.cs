using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;


namespace IncrementalBackup
{
    /// <summary>
    /// The top-level program class, containing high level operations and control flow.
    /// </summary>
    class Application
    {
        /// <summary>
        /// Application entry point. <br/>
        /// Instantiates the <see cref="Application"/> instance and calls <see cref="Run(string[])"/>.
        /// </summary>
        static int Main(string[] cmdArgs) {
            return new Application().Run(cmdArgs);
        }

        /// <summary>
        /// Process return code indicating successfully backed up all requested files.
        /// </summary>
        private const int EXIT_SUCCESS = 0;
        /// <summary>
        /// Process return code indicating that some files were not backed up due to I/O errors, permission errors, etc.
        /// </summary>
        private const int EXIT_SUCCESS_SKIPPED_FILES = 1;
        /// <summary>
        /// Process return code indicating the backup was aborted due to invalid command line arguments.
        /// </summary>
        private const int EXIT_INVALID_ARGS = 2;
        /// <summary>
        /// Process return code indicating the backup was aborted due to some runtime error.
        /// </summary>
        private const int EXIT_ERROR = 3;
        /// <summary>
        /// Process return code indicating the backup was aborted due to an unhandled exception (bad programmer!).
        /// </summary>
        private const int EXIT_LOGIC_ERROR = 4;

        /// <summary>
        /// Logger responsible for writing info to the console and log file.
        /// </summary>
        private readonly Logger Logger;

        private Application() {
            Logger = new(new ConsoleLogHandler());
        }

        /// <summary>
        /// Main application functionality.
        /// </summary>
        /// <param name="cmdArgs">The process's command line arguments.</param>
        /// <returns>Process return code.</returns>
        private int Run(string[] cmdArgs) {
            try {
                BackupConfig? config = ParseCmdArgs(cmdArgs);
                if (config == null) {
                    return EXIT_INVALID_ARGS;
                }

                DisplayConfig(config);

                var index = ReadBackupIndex(config.TargetDirectory);
                if (index == null) {
                    Logger.Info("No existing backup index found.");
                }

                var previousManifests = ReadPreviousManifests(config.SourceDirectory, config.TargetDirectory, index);
                Logger.Info($"{previousManifests.Count} previous backups found for this source directory.");

                var backupDirectory = CreateBackupDirectory(config.TargetDirectory);
                // TODO: exception handling? technically FullName can throw
                Logger.Info($"Created backup directory \"{backupDirectory.FullName}\"");

                if (false/*manifest.SkippedDirectories.Concat(manifest.SkippedFiles).Any(s => s.Reason != SkipReason.Excluded)*/) {
                    return EXIT_SUCCESS_SKIPPED_FILES;
                }
                else {
                    return EXIT_SUCCESS;
                }
            }
            catch (CriticalError e) {
                Logger.Error(e.Message);
                return EXIT_ERROR;
            }
            catch (Exception e) {
                Logger.Error($"Unhandled exception: {e}");
                return EXIT_LOGIC_ERROR;
            }
        }

        /// <summary>
        /// Parses and validates the application's command line arguments. <br/>
        /// If any of the arguments are invalid, writes error info to the logs.
        /// </summary>
        /// <remarks>
        /// Note that the filesystem paths in the returned <see cref="BackupConfig"/> are not guaranteed to be valid, as this
        /// is unfortunately not really possible to check without just doing the desired I/O operation. <br/>
        /// However, some invalid paths are detected by this method.
        /// </remarks>
        /// <param name="args">The command line arguments.</param>
        /// <returns>The <see cref="BackupConfig"/> parsed from the arguments, or <c>null</c> if any argument were invalid.</returns>
        private BackupConfig? ParseCmdArgs(string[] args) {
            if (args.Length < 2) {
                Console.Out.WriteLine("Usage: IncrementalBackup.exe <source_dir> <target_dir> [exclude_path1 exclude_path2 ...]");
                return null;
            }

            var sourceDirectory = args[0];
            var targetDirectory = args[1];
            var excludePaths = new List<string>(args.Skip(2));

            var validArgs = true;

            try {
                sourceDirectory = Path.GetFullPath(sourceDirectory);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) {
                Logger.Error("Source directory is not a valid path.");
                validArgs = false;
            }
            catch (SecurityException) {
                Logger.Error("Access denied while resolving source directory.");
                validArgs = false;
            }
            sourceDirectory = sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            try {
                targetDirectory = Path.GetFullPath(targetDirectory);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) {
                Logger.Error("Access denied while resolving target directory.");
                validArgs = false;
            }
            catch (SecurityException) {
                Logger.Error("Access to target directory is denied.");
                validArgs = false;
            }
            targetDirectory = targetDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            for (int i = 0; i < excludePaths.Count; ++i) {
                var path = excludePaths[i];
                try {
                    if (Path.IsPathFullyQualified(path)) {
                        // TODO: detect if path is above source directory.
                        if (Path.GetRelativePath(sourceDirectory, path) == path) {
                            Logger.Error($"Exclude path \"{path}\" is not within source directory.");
                            validArgs = false;
                        }
                    }
                    excludePaths[i] = Path.GetFullPath(path, sourceDirectory);
                }
                catch (ArgumentException) {
                    Logger.Error($"Invalid exclude path \"{path}\".");
                    validArgs = false;
                }
            }

            if (validArgs) {
                return new BackupConfig(sourceDirectory, targetDirectory, excludePaths);
            }
            else {
                return null;
            }
        }

        /// <summary>
        /// Outputs a <see cref="BackupConfig"/> to the logs.
        /// </summary>
        /// <param name="config">The <see cref="BackupConfig"/> to output.</param>
        private void DisplayConfig(BackupConfig config) {
            Logger.Info($"Source directory: {config.SourceDirectory}");
            Logger.Info($"Target directory: {config.TargetDirectory}");
            Logger.Info("Exclude paths:\n"
                + string.Join('\n', config.ExcludePaths.Select(path => $"\t{path}").DefaultIfEmpty("\t<none>")));
        }

        /// <summary>
        /// Reads the backup index from a target directory.
        /// </summary>
        /// <param name="targetDirectory">The target directory to read from.</param>
        /// <returns>The read <see cref="BackupIndex"/>, or <c>null</c> if the index file does not exist.</returns>
        /// <exception cref="CriticalError">If the index file exists, but could not be read/parsed.</exception>
        /// <seealso cref="BackupMeta.ReadIndexFile(string)"/>
        private BackupIndex? ReadBackupIndex(string targetDirectory) {
            try {
                return BackupMeta.ReadIndexFile(targetDirectory);
            }
            catch (IndexFileNotFoundException) {
                return null;
            }
            catch (IndexFileException e) {
                throw new CriticalError($"Failed to read existing index file: {e.Message}", e);
            }
        }

        /// <summary>
        /// Creates a new backup directory in the given target directory.
        /// </summary>
        /// <param name="targetDirectory">The target directory to create the backup directory in.</param>
        /// <returns>A <see cref="DirectoryInfo"/> instance associated with the created directory.</returns>
        /// <exception cref="CriticalError">If a new backup directory could not be created.</exception>
        private DirectoryInfo CreateBackupDirectory(string targetDirectory) {
            try {
                return BackupMeta.CreateBackupDirectory(targetDirectory);
            }
            catch (CreateBackupDirectoryException e) {
                throw new CriticalError("Failed to create new backup directory.", e);
            }
        }

        /// <summary>
        /// Reads the existing backup manifests matching the given source directory.
        /// </summary>
        /// <remarks>
        /// Manifests are matched by comparing their source directories to <paramref name="sourceDirectory"/>. <br/>
        /// The comparison ignores case, but is otherwise exact. E.g. symbolic links are not resolved.
        /// </remarks>
        /// <param name="sourceDirectory">The source directory to match.</param>
        /// <param name="targetDirectory">The target directory which is being examined.</param>
        /// <param name="index">The backup index detailing all the existing backups in <paramref name="targetDirectory"/>.
        /// If <c>null</c>, no manifests are matched.</param>
        /// <returns>A list of the matched backup manifests.</returns>
        /// <exception cref="CriticalError">If a manifest file could not be read.</exception>
        private List<BackupManifest> ReadPreviousManifests(string sourceDirectory, string targetDirectory, BackupIndex? index) {
            if (index == null) {
                return new();
            }

            List<BackupManifest> manifests = new();
            foreach (var pair in index.Backups) {
                var backupName = pair.Key;
                var backupSourceDirectory = pair.Value;

                // Paths are assumed to be already normalised.
                if (string.Compare(sourceDirectory, backupSourceDirectory, StringComparison.InvariantCultureIgnoreCase) == 0) {
                    var folderPath = Path.Join(targetDirectory, backupName);
                    BackupManifest manifest;
                    try {
                        manifest = BackupMeta.ReadManifestFile(folderPath);
                    }
                    catch (ManifestFileException e) {
                        throw new CriticalError($"Failed to read existing backup \"{backupName}\".", e);
                    }
                    manifests.Add(manifest);
                }
            }
            return manifests;
        }

        private static BackupManifest BackUp(BackupConfig config, BackupManifest? lastManifest) {
            var newManifest = new BackupManifest() { BeginTime = DateTime.UtcNow };

            // Explore directories in a depth-first manner, on the basis that files/folders within the same
            // branch of the filesystem are likely to be modified together, so we want to back them up as
            // close together in time as possible.
            // Using explicit stack to avoid recursion errors.
            var directoryStack = new List<DirectoryInfo> {
                new DirectoryInfo(config.SourceDirectory)       // TODO: exception handling
            };
            while (directoryStack.Count > 0) {
                var currentDirectory = directoryStack.Last();
                directoryStack.RemoveAt(directoryStack.Count - 1);

                if (IsPathExcluded(currentDirectory.FullName, config.ExcludePaths)) {
                    newManifest.AddSkippedPath(currentDirectory.FullName, true, SkipReason.Excluded);
                }
                else {
                    BackUpDirectoryFiles(currentDirectory, config, lastManifest, newManifest);
                    directoryStack.AddRange(currentDirectory.GetDirectories());       // TODO: exception handling
                }
            }

            newManifest.EndTime = DateTime.UtcNow;
            return newManifest;
        }

        private static void BackUpDirectoryFiles(DirectoryInfo directory, BackupConfig config, BackupManifest? lastManifest,
                BackupManifest newManifest) {
            // TODO: create directory in backup 

            var files = directory.GetFiles();       // TODO: exception handling
            foreach (var file in files) {
                var path = file.FullName;
                if (IsPathExcluded(path, config.ExcludePaths)) {
                    newManifest.AddSkippedPath(path, false, SkipReason.Excluded);
                }
                else if (lastManifest == null || file.LastWriteTimeUtc >= lastManifest.BeginTime) {
                    BackUpFile(file, newManifest);
                }
            }
        }

        private static void BackUpFile(FileInfo file, BackupManifest newManifest) {
            // TODO
        }

        private static bool IsPathExcluded(string path, IReadOnlyCollection<string> excludePaths) {
            // path must be normalised.

            // Case-insensitivity means this only works for Windows.
            return excludePaths.Any(p => string.Compare(path, p, StringComparison.InvariantCultureIgnoreCase) == 0);
        }
    }

    record BackupConfig(
        string SourceDirectory,
        string TargetDirectory,
        IReadOnlyList<string> ExcludePaths
    );

    /// <summary>
    /// Thrown when an unrecoverable error is encountered and the application should exit. <br/>
    /// Should only be handled at the very top level of the application.
    /// </summary>
    class CriticalError : ApplicationException
    {
        public CriticalError(string? message = null, Exception? innerException = null) : base(message, innerException) { }
    }
}
