using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


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
            (int)new Application().Run(cmdArgs);

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
                BackupConfig config = ParseCmdArgs(cmdArgs);
                LogConfig(config);
                var index = ReadBackupIndex(config.TargetPath);
                var previousBackups = ReadPreviousBackups(config.SourcePath, config.TargetPath, index);
                var (backupName, manifestWriter) = InitialiseBackup(config.SourcePath, config.TargetPath);
                var results = DoBackup(config, previousBackups, backupName, manifestWriter);
                var metadataWritten = CompleteBackup(config.SourcePath, config.TargetPath, backupName, results);

                if (!results.PathsSkipped && results.ManifestComplete && metadataWritten) {
                    return ProcessExitCode.Success;
                }
                else {
                    return ProcessExitCode.Warning;
                }
            }
            catch (InvalidCmdArgsError) {
                Console.Out.WriteLine(
                    "Usage: IncrementalBackup.exe <source_dir> <target_dir> [exclude_path1 exclude_path2 ...]");
                return ProcessExitCode.InvalidArgs;
            }
            catch (ApplicationRuntimeError e) {
                Logger.Error(e.Message);
                return ProcessExitCode.RuntimeError;
            }
            catch (Exception e) {
                Logger.Error($"Unhandled exception: {e}");
                return ProcessExitCode.LogicError;
            }
        }

        /// <summary>
        /// Parses and validates the application's command line arguments.
        /// </summary>
        /// <remarks>
        /// Note that the filesystem paths in the returned <see cref="BackupConfig"/> are not guaranteed to be valid,
        /// as this is unfortunately not really possible to check without actually doing the desired I/O operation. <br/>
        /// However, some invalid paths are detected by this method.
        /// </remarks>
        /// <param name="args">The command line arguments.</param>
        /// <returns>The <see cref="BackupConfig"/> parsed from the arguments.</returns>
        /// <exception cref="InvalidCmdArgsError">If the command line arguments are invalid.</exception>
        /// <exception cref="ApplicationRuntimeError">If the config paths can't be resolved.</exception>
        private BackupConfig ParseCmdArgs(string[] args) {
            if (args.Length < 2) {
                throw new InvalidCmdArgsError();
            }

            var sourcePath = args[0];
            var targetPath = args[1];
            var excludePaths = args.Skip(2);

            try {
                sourcePath = FilesystemException.ConvertSystemException(() => Path.GetFullPath(sourcePath),
                    () => sourcePath);
            }
            catch (FilesystemException e) {
                throw new ApplicationRuntimeError($"Failed to resolve source directory: {e.Reason}");
            }

            if (File.Exists(sourcePath)) {
                throw new ApplicationRuntimeError("Source directory is not a directory");
            }
            if (!Directory.Exists(sourcePath)) {
                throw new ApplicationRuntimeError("Source directory not found");
            }

            try {
                targetPath = FilesystemException.ConvertSystemException(() => Path.GetFullPath(targetPath),
                    () => targetPath);
            }
            catch (FilesystemException e) {
                throw new ApplicationRuntimeError($"Failed to resolve target directory: {e.Reason}");
            }

            if (File.Exists(targetPath)) {
                throw new ApplicationRuntimeError("Target directory exists and is not a directory.");
            }

            List<string> parsedExcludePaths = new();
            foreach (var path in excludePaths) {
                string fullPath;
                try {
                    fullPath = FilesystemException.ConvertSystemException(
                        () => Path.GetFullPath(path, sourcePath), () => path);
                }
                catch (FilesystemException e) {
                    Logger.Warning($"Failed to resolve exclude path \"{path}\" ({e.Reason}); discarding.");
                    continue;
                }
                parsedExcludePaths.Add(fullPath);
            }

            return new(sourcePath, targetPath, parsedExcludePaths);
        }

        /// <summary>
        /// Outputs a <see cref="BackupConfig"/> to <see cref="Logger"/>.
        /// </summary>
        /// <param name="config">The backup config to output.</param>
        private void LogConfig(BackupConfig config) {
            Logger.Info($"Source directory: {config.SourcePath}");
            Logger.Info($"Target directory: {config.TargetPath}");
            Logger.Info("Exclude paths:\n"
                + string.Join('\n', config.ExcludePaths.Select(path => $"  {path}").DefaultIfEmpty("\t<none>")));
        }

        /// <summary>
        /// Reads the backup index from a target directory.
        /// </summary>
        /// <param name="targetPath">The target directory to read from.</param>
        /// <returns>The read backup index, or <c>null</c> if the index file does not exist.</returns>
        /// <exception cref="ApplicationRuntimeError">If the index file exists, but could not be read/parsed.
        /// </exception>
        /// <seealso cref="BackupMeta.ReadIndexFile(string)"/>
        private BackupIndex? ReadBackupIndex(string targetPath) {
            var indexFilePath = BackupMeta.IndexFilePath(targetPath);
            try {
                var index = BackupIndexReader.Read(indexFilePath);
                Logger.Info($"Read backup index \"{indexFilePath}\"");
                return index;
            }
            catch (BackupIndexFileIOException e) when (e.InnerException is PathNotFoundException) {
                Logger.Info("No existing backup index found");
                return null;
            }
            catch (BackupIndexFileException e) {
                throw new ApplicationRuntimeError(e.Message);
            }
        }

        /// <summary>
        /// Reads the existing backup manifests matching the given source directory.
        /// </summary>
        /// <remarks>
        /// Manifests are matched by comparing their source directories to <paramref name="sourcePath"/>. <br/>
        /// The comparison ignores case, so this only works on Windows. <br/>
        /// The comparison only considers the literal path, e.g. symbolic links are not resolved.
        /// </remarks>
        /// <param name="sourcePath">The source directory to match.</param>
        /// <param name="targetPath">The target directory which is being examined.</param>
        /// <param name="index">The backup index detailing all the existing backups in <paramref name="targetPath"/>.
        /// If <c>null</c>, no manifests are matched.</param>
        /// <returns>A list of the matched backup manifests, in unspecified order.</returns>
        private List<PreviousBackup> ReadPreviousBackups(string sourcePath, string targetPath, BackupIndex? index) {
            if (index is null) {
                return new();
            }

            List<PreviousBackup> previousBackups = new();
            foreach (var pair in index.Backups) {
                var backupName = pair.Key;
                var backupSourcePath = pair.Value;

                // Paths are assumed to be already normalised.
                if (string.Compare(sourcePath, backupSourcePath, true) == 0) {
                    var backupPath = Path.Join(targetPath, backupName);

                    var startInfoFilePath = BackupMeta.StartInfoFilePath(backupPath);
                    BackupStartInfo startInfo;
                    try {
                        startInfo = BackupStartInfoReader.Read(startInfoFilePath);
                    }
                    catch (BackupStartInfoFileException e) {
                        Logger.Warning($"Failed to read metadata of previous backup \"{backupName}\": {e.Message}");
                        continue;
                    }

                    // We could just assume the index file and start info file are consistent, but it might be a
                    // good idea to check just in case something goes particularly wrong.
                    if (string.Compare(sourcePath, startInfo.SourcePath, true) != 0) {
                        Logger.Warning(
                            $"Source directory of backup start info in previous backup \"{backupName}\" doesn't match backup index");
                        continue;
                    }

                    var manifestFilePath = BackupMeta.ManifestFilePath(backupPath);
                    BackupManifest manifest;
                    try {
                        manifest = BackupManifestReader.Read(manifestFilePath);
                    }
                    catch (BackupManifestFileException e) {
                        Logger.Warning($"Failed to read metadata of previous backup \"{backupName}\": {e.Message}");
                        continue;
                    }

                    previousBackups.Add(new(startInfo.StartTime, manifest));
                }
            }

            Logger.Info($"{previousBackups.Count} previous backups found in target directory for source directory");
            return previousBackups;
        }

        /// <summary>
        /// Creates the backup directory, the log file, the start info file, and the manifest writer.
        /// </summary>
        /// <param name="sourcePath">The path of the backup source directory.</param>
        /// <param name="targetPath">The path of the backup target directory.</param>
        /// <returns>A tuple of the backup directory name and manifest writer.</returns>
        /// <exception cref="ApplicationRuntimeError">If the backup directory, start info file, or manifest writer
        /// can't be created.</exception>
        public (string, BackupManifestWriter) InitialiseBackup(string sourcePath, string targetPath) {
            string backupName;
            try {
                backupName = BackupMeta.CreateBackupDirectory(targetPath);
            }
            catch (BackupDirectoryCreateException) {
                throw new ApplicationRuntimeError("Failed to create new backup directory");
            }
            Logger.Info($"Backup name: {backupName}");
            var backupPath = BackupMeta.BackupPath(targetPath, backupName);
            Logger.Info($"Created backup directory \"{backupPath}\"");

            var logFilePath = BackupMeta.LogFilePath(backupPath);
            try {
                Logger.FileHandler = new(logFilePath);
                Logger.Info($"Created log file \"{logFilePath}\"");
            }
            catch (LoggingException e) {
                // Not much we can do if we can't create the log file, just ignore and continue.
                Logger.Warning(e.Message);
            }

            var manifestFilePath = BackupMeta.ManifestFilePath(backupPath);
            BackupManifestWriter manifestWriter;
            try {
                manifestWriter = new(manifestFilePath);
            }
            catch (BackupManifestFileIOException e) {
                throw new ApplicationRuntimeError(
                    $"Failed to create backup manifest file \"{manifestFilePath}\": {e.InnerException.Reason}");
            }
            Logger.Info($"Created backup manifest file \"{manifestFilePath}\"");

            BackupStartInfo startInfo = new(sourcePath, DateTime.UtcNow);
            var startInfoFilePath = BackupMeta.StartInfoFilePath(backupPath);
            try {
                BackupStartInfoWriter.Write(startInfoFilePath, startInfo);
            }
            catch (BackupStartInfoFileIOException e) {
                throw new ApplicationRuntimeError(
                    $"Failed to write backup start info file \"{startInfoFilePath}\": {e.InnerException.Reason}");
            }
            Logger.Info($"Created backup start info file \"{startInfoFilePath}\"");

            return (backupName, manifestWriter);
        }

        /// <summary>
        /// Runs the backup.
        /// </summary>
        /// <param name="config">The configuration of this backup.</param>
        /// <param name="previousBackups">The existing backup data for this source directory.</param>
        /// <param name="backupName">The name of the new backup directory.</param>
        /// <param name="manifestWriter">Writes the backup manifest. Must be in a newly-constructed state.</param>
        /// <returns>Results of the backup.</returns>
        /// <exception cref="ApplicationRuntimeError">If the backup fails.</exception>
        /// <seealso cref="Backup.Run(string, IReadOnlyList{string}, IReadOnlyList{PreviousBackup}, string, BackupManifestWriter, Logger)"/>
        private BackupResults DoBackup(BackupConfig config, IReadOnlyList<PreviousBackup> previousBackups,
                string backupName, BackupManifestWriter manifestWriter) {
            var backupPath = BackupMeta.BackupPath(config.TargetPath, backupName);
            Logger.Info("Copying files");
            try {
                return Backup.Run(config.SourcePath, config.ExcludePaths, previousBackups, backupPath, manifestWriter,
                    Logger);
            }
            catch (BackupException e) {
                throw new ApplicationRuntimeError(e.Message);
            }
        }

        /// <summary>
        /// Generates the backup completion info file and adds the backup to the backup index.
        /// </summary>
        /// <param name="sourcePath">The path of the backup source directory.</param>
        /// <param name="targetPath">The path of the backup target directory.</param>
        /// <param name="backupName">The name of the backup directory.</param>
        /// <param name="results">The results of the backup.</param>
        /// <returns><c>true</c> if all the operations completed successfully, otherwise <c>false</c>.</returns>
        public bool CompleteBackup(string sourcePath, string targetPath, string backupName, BackupResults results) {
            var backupPath = BackupMeta.BackupPath(targetPath, backupName);

            bool success = true;

            BackupCompleteInfo completionInfo = new(DateTime.UtcNow, results.PathsSkipped, results.ManifestComplete);
            var completionInfoFilePath = BackupMeta.CompleteInfoFilePath(backupPath);
            try {
                BackupCompleteInfoWriter.Write(completionInfoFilePath, completionInfo);
                Logger.Info($"Created backup completion info file \"{completionInfoFilePath}\"");
            }
            catch (BackupCompleteInfoFileIOException e) {
                Logger.Warning(
                    $"Failed to write backup completion info \"{completionInfoFilePath}\": {e.InnerException.Reason}");
                success = false;
            }

            var indexFilePath = BackupMeta.IndexFilePath(targetPath);
            try {
                BackupIndexWriter.AddEntry(indexFilePath, backupName, sourcePath);
                Logger.Info($"Added this backup to backup index");
            }
            catch (BackupIndexFileIOException e) {
                Logger.Warning($"Failed to add backup to backup index \"{indexFilePath}\": {e.InnerException.Reason}");
                success = false;
            }

            return success;
        }
    }

    /// <summary>
    /// Configuration of a backup run.
    /// </summary>
    /// <param name="SourcePath">The path of the directory to be backed up. Should be fully qualified and normalised.
    /// </param>
    /// <param name="TargetPath">The path of the directory to back up to. Should be fully qualified and normalised.
    /// </param>
    /// <param name="ExcludePaths">A list of files and folders that are excluded from being backed up.
    /// Each path should be fully qualified and normalised.</param>
    record BackupConfig(
        string SourcePath,
        string TargetPath,
        IReadOnlyList<string> ExcludePaths
    );

    /// <summary>
    /// Information on a previous backup.
    /// </summary>
    /// <remarks>
    /// Used to store the previous backups for a specific source directory (to know which files have been previously
    /// backed up and when).
    /// </remarks>
    /// <param name="StartTime">The UTC time the backup operation started.</param>
    /// <param name="Manifest">The backup's manifest.</param>
    record PreviousBackup(
        DateTime StartTime,
        BackupManifest Manifest
    );

    enum ProcessExitCode
    {
        /// <summary>
        /// Successfully backed up all requested files.
        /// </summary>
        Success = 0,
        /// <summary>
        /// The backup completed mostly successfully, but with some warnings (e.g. files were skipped or metadata
        /// couldn't be saved).
        /// </summary>
        Warning = 1,
        /// <summary>
        /// The backup was aborted due to invalid command line arguments.
        /// </summary>
        InvalidArgs = 2,
        /// <summary>
        /// The backup was aborted due to some runtime error.
        /// </summary>
        RuntimeError = 3,
        /// <summary>
        /// The backup was aborted due to an unhandled exception (bad programmer!).
        /// </summary>
        LogicError = 4,
    }

    /// <summary>
    /// Indicates a high-level operation failed and as a result the application cannot continue. <br/>
    /// <see cref="Exception.Message"/> is the message to write to the logs.
    /// </summary>
    class ApplicationRuntimeError : Exception
    {
        public ApplicationRuntimeError(string message) :
            base(message) { }
    }

    /// <summary>
    /// Indicates the application's command line arguments are invalid.
    /// </summary>
    class InvalidCmdArgsError : ApplicationRuntimeError
    {
        public InvalidCmdArgsError() :
            base("Invalid command line arguments") { }
    }
}
