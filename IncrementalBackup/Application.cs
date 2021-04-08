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
        static int Main(string[] cmdArgs) =>
            (int)(new Application().Run(cmdArgs));

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
        /// <returns>The process return code.</returns>
        private ProcessReturnCode Run(string[] cmdArgs) {
            try {
                BackupConfig? config = ParseCmdArgs(cmdArgs);
                if (config == null) {
                    return ProcessReturnCode.InvalidArgs;
                }

                LogConfig(config);

                var index = ReadBackupIndex(config.TargetDirectory);

                var previousManifests = ReadPreviousManifests(config.SourceDirectory, config.TargetDirectory, index);

                var results = DoBackup(config, previousManifests);

                if (results.PathsSkipped) {
                    return ProcessReturnCode.SuccessSkippedFiles;
                }
                else {
                    return ProcessReturnCode.Success;
                }
            }
            catch (CriticalError e) {
                Logger.Error(e.Message);
                return ProcessReturnCode.Error;
            }
            catch (Exception e) {
                Logger.Error($"Unhandled exception: {e}");
                return ProcessReturnCode.LogicError;
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
            var excludePaths = args.Skip(2).ToList();

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
            sourceDirectory = Utility.RemoveTrailingDirSep(sourceDirectory);

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
            targetDirectory = Utility.RemoveTrailingDirSep(targetDirectory);

            for (int i = 0; i < excludePaths.Count; ++i) {
                try {
                    excludePaths[i] = Path.GetFullPath(excludePaths[i], sourceDirectory);
                }
                catch (ArgumentException) {
                    Logger.Error($"Invalid exclude path \"{excludePaths[i]}\".");
                    validArgs = false;
                    continue;
                }

                if (!Utility.PathContainsPath(sourceDirectory, excludePaths[i])) {
                    Logger.Error($"Exclude path \"{excludePaths[i]}\" is not within source directory.");
                    validArgs = false;
                }
            }

            if (validArgs) {
                return new(sourceDirectory, targetDirectory, excludePaths);
            }
            else {
                return null;
            }
        }

        /// <summary>
        /// Outputs a <see cref="BackupConfig"/> to the logs.
        /// </summary>
        /// <param name="config">The <see cref="BackupConfig"/> to output.</param>
        private void LogConfig(BackupConfig config) {
            Logger.Info($"Source directory: {config.SourceDirectory}");
            Logger.Info($"Target directory: {config.TargetDirectory}");
            Logger.Info("Exclude paths:\n"
                + string.Join('\n', config.ExcludePaths.Select(path => $"\t{path}").DefaultIfEmpty("\t<none>")));
        }

        /// <summary>
        /// Reads the backup index from a target directory. <br/>
        /// If no backup index exists, writes a message to the logs.
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
                Logger.Info("No existing backup index found.");
                return null;
            }
            catch (IndexFileException e) {
                throw new CriticalError($"Failed to read existing index file: {e.Message}", e);
            }
        }

        /// <summary>
        /// Reads the existing backup manifests matching the given source directory. <br/>
        /// Outputs the number of manifests matched to the logs.
        /// </summary>
        /// <remarks>
        /// Manifests are matched by comparing their source directories to <paramref name="sourceDirectory"/>. <br/>
        /// The comparison ignores case, so this only works on Windows. <br/>
        /// The comparison only considers the literal path, e.g. symbolic links are not resolved.
        /// </remarks>
        /// <param name="sourceDirectory">The source directory to match.</param>
        /// <param name="targetDirectory">The target directory which is being examined.</param>
        /// <param name="index">The backup index detailing all the existing backups in <paramref name="targetDirectory"/>.
        /// If <c>null</c>, no manifests are matched.</param>
        /// <returns>A list of the matched backup manifests, in ascending order of their backup time.</returns>
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
                if (string.Compare(sourceDirectory, backupSourceDirectory, true) == 0) {
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
            
            manifests.Sort((a, b) => DateTime.Compare(a.BeginTime, b.BeginTime));

            Logger.Info($"{manifests.Count} previous backups found for this source directory.");

            return manifests;
        }

        /// <summary>
        /// Runs the backup. Creates the new backup directory and copies files over.
        /// </summary>
        /// <param name="config">The configuration of this backup.</param>
        /// <param name="previousManifests">The existing backup manifests for this source directory. Must be in order
        /// of the backup time.</param>
        /// <returns>Results of the backup.</returns>
        /// <exception cref="CriticalError">If an error occurred during the backup such that it could not continue.</exception>
        /// <seealso cref="Backup.Run(BackupConfig, IReadOnlyList{BackupManifest}, Logger)"/>
        private BackupResults DoBackup(BackupConfig config, IReadOnlyList<BackupManifest> previousManifests) {
            try {
                return Backup.Run(config, previousManifests, Logger);
            }
            catch (BackupException e) {
                throw new CriticalError(e.Message, e);
            }
        }
    }

    enum ProcessReturnCode
    {
        /// <summary>
        /// Process return code indicating successfully backed up all requested files.
        /// </summary>
        Success = 0,
        /// <summary>
        /// Process return code indicating that some files were not backed up due to I/O errors, permission errors, etc.
        /// </summary>
        SuccessSkippedFiles = 1,
        /// <summary>
        /// Process return code indicating the backup was aborted due to invalid command line arguments.
        /// </summary>
        InvalidArgs = 2,
        /// <summary>
        /// Process return code indicating the backup was aborted due to some runtime error.
        /// </summary>
        Error = 3,
        /// <summary>
        /// Process return code indicating the backup was aborted due to an unhandled exception (bad programmer!).
        /// </summary>
        LogicError = 4,
    }

    /// <summary>
    /// Thrown when an unrecoverable error is encountered and the application should exit. <br/>
    /// Should only be handled at the very top level of the application.
    /// </summary>
    class CriticalError : ApplicationException
    {
        public CriticalError(string? message = null, Exception? innerException = null) : base(message, innerException) { }
    }
}
