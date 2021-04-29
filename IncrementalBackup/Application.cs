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
            Logger = new(new ConsoleLogHandler(), null);
        }

        /// <summary>
        /// Main application functionality.
        /// </summary>
        /// <param name="cmdArgs">The process's command line arguments.</param>
        /// <returns>The process return code.</returns>
        private ProcessExitCode Run(string[] cmdArgs) {
            try {
                BackupConfig config;
                try {
                    config = ParseCmdArgs(cmdArgs);
                }
                catch (InvalidCmdArgsException) {
                    Console.Out.WriteLine(
                        "Usage: IncrementalBackup.exe <source_dir> <target_dir> [exclude_path1 exclude_path2 ...]");
                    return ProcessExitCode.InvalidArgs;
                }

                LogConfig(config);

                try {
                    var index = ReadBackupIndex(config.TargetDirectory);
                    var previousBackups = ReadPreviousBackups(config.SourceDirectory, config.TargetDirectory, index);
                    var results = DoBackup(config, previousBackups);
                    if (results.PathsSkipped) {
                        return ProcessExitCode.SuccessSkippedFiles;
                    }
                    else {
                        return ProcessExitCode.Success;
                    }
                }
                catch (Exception e) when (e is BackupMetaException) {
                    Logger.Error(e.Message);
                    return ProcessExitCode.RuntimeError;
                }
            }
            catch (Exception e) {
                Logger.Error($"Unhandled exception: {e}");
                return ProcessExitCode.LogicError;
            }
        }

        /// <summary>
        /// Parses and validates the application's command line arguments. <br/>
        /// If any of the arguments are invalid, writes error info to <see cref="Logger"/>.
        /// </summary>
        /// <remarks>
        /// Note that the filesystem paths in the returned <see cref="BackupConfig"/> are not guaranteed to be valid,
        /// as this is unfortunately not really possible to check without just doing the desired I/O operation. <br/>
        /// However, some invalid paths are detected by this method.
        /// </remarks>
        /// <param name="args">The command line arguments.</param>
        /// <returns>The <see cref="BackupConfig"/> parsed from the arguments.</returns>
        /// <exception cref="InvalidCmdArgsException">If the command line arguments are invalid.</exception>
        private BackupConfig ParseCmdArgs(string[] args) {
            if (args.Length < 2) {
                throw new InvalidCmdArgsException();
            }

            var sourceDirectory = args[0];
            var targetDirectory = args[1];
            var excludePaths = args.Skip(2).ToList();

            var validArgs = true;

            try {
                sourceDirectory = Path.GetFullPath(sourceDirectory);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) {
                Logger.Error("Source directory is not a valid path.");
                validArgs = false;
            }
            catch (SecurityException) {
                Logger.Error("Access denied while resolving source directory.");
                validArgs = false;
            }

            try {
                targetDirectory = Path.GetFullPath(targetDirectory);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) {
                Logger.Error("Target directory is not a valid path.");
                validArgs = false;
            }
            catch (SecurityException) {
                Logger.Error("Access denied while resolving target directory.");
                validArgs = false;
            }

            List<string> parsedExcludePaths = new();
            for (int i = 0; i < excludePaths.Count; ++i) {
                try {
                    parsedExcludePaths.Add(Path.GetFullPath(excludePaths[i], sourceDirectory));
                }
                catch (ArgumentException) {
                    Logger.Warning($"Discarding invalid exclude path \"{excludePaths[i]}\".");
                }
            }

            if (validArgs) {
                return new(sourceDirectory, targetDirectory, parsedExcludePaths);
            }
            else {
                throw new InvalidCmdArgsException();
            }
        }

        /// <summary>
        /// Outputs a <see cref="BackupConfig"/> to <see cref="Logger"/>.
        /// </summary>
        /// <param name="config">The backup config to output.</param>
        private void LogConfig(BackupConfig config) {
            Logger.Info($"Source directory: {config.SourceDirectory}");
            Logger.Info($"Target directory: {config.TargetDirectory}");
            Logger.Info("Exclude paths:\n"
                + string.Join('\n', config.ExcludePaths.Select(path => $"\t{path}").DefaultIfEmpty("\t<none>")));
        }

        /// <summary>
        /// Reads the backup index from a target directory. <br/>
        /// If no backup index exists, writes a message to <see cref="Logger"/>.
        /// </summary>
        /// <param name="targetDirectory">The target directory to read from.</param>
        /// <returns>The read backup index, or <c>null</c> if the index file does not exist.</returns>
        /// <exception cref="BackupIndexFileException">If the index file exists, but could not be read/parsed.
        /// </exception>
        /// <seealso cref="BackupMeta.ReadIndexFile(string)"/>
        private BackupIndex? ReadBackupIndex(string targetDirectory) {
            try {
                return BackupIndexReader.Read(BackupMeta.IndexFilePath(targetDirectory));
            }
            catch (BackupIndexFileIOException e) when (e.InnerException is PathNotFoundException) {
                Logger.Info("No existing backup index found.");
                return null;
            }
        }

        /// <summary>
        /// Reads the existing backup manifests matching the given source directory. <br/>
        /// Outputs the number of manifests matched to <see cref="Logger"/>.
        /// </summary>
        /// <remarks>
        /// Manifests are matched by comparing their source directories to <paramref name="sourceDirectory"/>. <br/>
        /// The comparison ignores case, so this only works on Windows. <br/>
        /// The comparison only considers the literal path, e.g. symbolic links are not resolved.
        /// </remarks>
        /// <param name="sourceDirectory">The source directory to match.</param>
        /// <param name="targetDirectory">The target directory which is being examined.</param>
        /// <param name="index">The backup index detailing all the existing backups in
        /// <paramref name="targetDirectory"/>. If <c>null</c>, no manifests are matched.</param>
        /// <returns>A list of the matched backup manifests, in unspecified order.</returns>
        /// <exception cref="BackupHistoryReadException">If a manifest file could not be read.</exception>
        /// <exception cref="BackupMetadataInconsistentException">If previous backups' metadata is inconsistent.
        /// </exception>
        private List<PreviousBackup> ReadPreviousBackups(string sourceDirectory, string targetDirectory,
                BackupIndex? index) {
            if (index is null) {
                return new();
            }
            else {
                var previousBackups = BackupHistory.ReadPreviousBackups(sourceDirectory, targetDirectory, index,
                    e => Logger.Warning(e.Message));
                Logger.Info($"{previousBackups.Count} previous backups found for this source directory.");
                return previousBackups;
            }
        }

        /// <summary>
        /// Runs the backup. Creates the new backup directory and copies files over.
        /// </summary>
        /// <param name="config">The configuration of this backup.</param>
        /// <param name="previousBackups">The existing backup data for this source directory.</param>
        /// <returns>Results of the backup.</returns>
        /// <seealso cref="Backup.Run(BackupConfig, IReadOnlyList{BackupManifest}, Logger)"/>
        private BackupResults DoBackup(BackupConfig config, IReadOnlyList<PreviousBackup> previousBackups) =>
            Backup.Run(config, previousBackups, Logger);
    }

    enum ProcessExitCode
    {
        /// <summary>
        /// Indicates successfully backed up all requested files.
        /// </summary>
        Success = 0,
        /// <summary>
        /// Indicates that some files were not backed up due to I/O errors, permission errors, etc.
        /// </summary>
        SuccessSkippedFiles = 1,
        /// <summary>
        /// Indicates the backup was aborted due to invalid command line arguments.
        /// </summary>
        InvalidArgs = 2,
        /// <summary>
        /// Indicates the backup was aborted due to some runtime error.
        /// </summary>
        RuntimeError = 3,
        /// <summary>
        /// Indicates the backup was aborted due to an unhandled exception (bad programmer!).
        /// </summary>
        LogicError = 4,
    }

    /// <summary>
    /// Indicates the application's command line arguments are invalid.
    /// </summary>
    class InvalidCmdArgsException : Exception
    {
        public InvalidCmdArgsException() :
            base("Invalid command line arguments", null) { }
    }
}
