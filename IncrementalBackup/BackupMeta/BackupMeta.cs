using System;
using System.Collections.Generic;
using System.IO;


namespace IncrementalBackup
{
    /// <summary>
    /// Functionality for the file and directory structure of backups.
    /// </summary>
    static class BackupMeta
    {
        /// <summary>
        /// Creates a new randomly-named backup directory in the given directory. Creates the given directory too, if
        /// required.
        /// </summary>
        /// <param name="targetDirectory">The directory in which to create the backup directory.</param>
        /// <returns>The name of the new backup directory.</returns>
        /// <exception cref="BackupDirectoryCreateException">If the new directory could not be created, due to I/O
        /// errors, permission errors, etc.</exception>
        public static string CreateBackupDirectory(string targetDirectory) {
            var retries = BACKUP_DIRECTORY_CREATION_RETRIES;
            List<string> attemptedDirectories = new();
            while (true) {
                var name = Utility.RandomAlphaNumericString(BACKUP_DIRECTORY_NAME_LENGTH);
                attemptedDirectories.Add(name);

                var path = BackupPath(targetDirectory, name);

                FilesystemException? exception = null;
                // Non-atomicity :|
                if (!Directory.Exists(path) && !File.Exists(path)) {
                    try {
                        FilesystemException.ConvertSystemException(() => Directory.CreateDirectory(path), () => path);
                        return name;
                    }
                    catch (FilesystemException e) {
                        exception = e;
                    }
                }

                if (retries <= 0) {
                    throw new BackupDirectoryCreateException(targetDirectory, attemptedDirectories, exception);
                }
                retries--;
            }
        }

        /// <summary>
        /// Forms the path to a backup index file.
        /// </summary>
        /// <param name="targetDirectory">The path of the target directory the index file is in.</param>
        /// <returns>The path to the backup index file.</returns>
        public static string IndexFilePath(string targetDirectory) =>
            Path.Join(targetDirectory, INDEX_FILENAME);

        /// <summary>
        /// Forms the path to a backup directory.
        /// </summary>
        /// <param name="targetDirectory">The path of the target directory the backup is in.</param>
        /// <param name="backupName">The name of the backup.</param>
        /// <returns>The path to the backup directory.</returns>
        public static string BackupPath(string targetDirectory, string backupName) =>
            Path.Join(targetDirectory, backupName);

        /// <summary>
        /// Forms the path to a backup manifest file.
        /// </summary>
        /// <param name="backupDirectory">The path of the backup directory the manifest file is in.</param>
        /// <returns>The path to the backup manifest file.</returns>
        public static string ManifestFilePath(string backupDirectory) =>
            Path.Join(backupDirectory, MANIFEST_FILENAME);

        /// <summary>
        /// Forms the path to a backup start info file.
        /// </summary>
        /// <param name="backupDirectory">The path of the backup directory the start info file is in.</param>
        /// <returns>The path to the backup start info file.</returns>
        public static string StartInfoFilePath(string backupDirectory) =>
            Path.Join(backupDirectory, START_INFO_FILENAME);

        /// <summary>
        /// Forms the path to a backup completion info file.
        /// </summary>
        /// <param name="backupDirectory">The path of the backup directory the completion info file is in.</param>
        /// <returns>The path to the backup completion info file.</returns>
        public static string CompleteInfoFilePath(string backupDirectory) =>
            Path.Join(backupDirectory, COMPLETE_INFO_FILENAME);

        /// <summary>
        /// Forms the path to a backup log file.
        /// </summary>
        /// <param name="backupDirectory">The path of the backup directory the log file is in.</param>
        /// <returns>The path to the backup log file.</returns>
        public static string LogFilePath(string backupDirectory) =>
            Path.Join(backupDirectory, LOG_FILENAME);

        /// <summary>
        /// Forms the path to the data directory within a backup directory.
        /// </summary>
        /// <param name="backupDirectory">The path of the backup directory.</param>
        /// <returns>The path to the backup data directory.</returns>
        public static string BackupDataPath(string backupDirectory) =>
            Path.Join(backupDirectory, DATA_DIRECTORY);

        /// <summary>
        /// The name of the file used to store the backup index in a target directory.
        /// </summary>
        public const string INDEX_FILENAME = "index.txt";
        /// <summary>
        /// The name of the file used to store the backup manifest for each backup.
        /// </summary>
        public const string MANIFEST_FILENAME = "manifest.txt";
        /// <summary>
        /// The name of the file used to store info at the start of each backup.
        /// </summary>
        public const string START_INFO_FILENAME = "start.json";
        /// <summary>
        /// The name of the file used to store completion info for each backup.
        /// </summary>
        public const string COMPLETE_INFO_FILENAME = "completion.json";
        /// <summary>
        /// The name of the log file created in backup directories.
        /// </summary>
        public const string LOG_FILENAME = "log.txt";
        /// <summary>
        /// The name of the directory in the backup directory used to store the backed up files.
        /// </summary>
        public const string DATA_DIRECTORY = "data";
        /// <summary>
        /// The length of the randomly-generated backup folder names.
        /// </summary>
        private const int BACKUP_DIRECTORY_NAME_LENGTH = 16;
        /// <summary>
        /// The number of times <see cref="CreateBackupDirectory(string)"/> will retry creation of the directory
        /// before failing.
        /// </summary>
        private const int BACKUP_DIRECTORY_CREATION_RETRIES = 20;
    }

    /// <summary>
    /// Indicates a backup metadata or metastructure operation failed.
    /// </summary>
    abstract class BackupMetaException : Exception
    {
        public BackupMetaException(string message, Exception? innerException) :
            base(message, innerException) { }
    }

    /// <summary>
    /// Indicates a backup metadata file operation failed.
    /// </summary>
    abstract class BackupMetaFileException : BackupMetaException
    {
        public BackupMetaFileException(string filePath, string message, Exception? innerException) :
            base(message, innerException) {
            FilePath = filePath;
        }

        /// <summary>
        /// Path of the backup metadata file that was being accessed.
        /// </summary>
        public readonly string FilePath;
    }

    /// <summary>
    /// Thrown from <see cref="BackupMeta.CreateBackupDirectory(string)"/> on failure.
    /// </summary>
    class BackupDirectoryCreateException : BackupMetaException {
        public BackupDirectoryCreateException(string targetDirectory, IReadOnlyList<string> attemptedDirectoryNames,
                FilesystemException? innerException) :
            base($"Failed to create new backup directory in \"{targetDirectory}\"", innerException) {
            TargetDirectory = targetDirectory;
            AttemptedDirectoryNames = attemptedDirectoryNames;
        }

        /// <summary>
        /// The exception produced from trying the last directory name.
        /// </summary>
        public new FilesystemException? InnerException =>
            base.InnerException as FilesystemException;

        /// <summary>
        /// The target directory in which the new backup directory was being created.
        /// </summary>
        public string TargetDirectory;

        /// <summary>
        /// The new backup directory names which were tried (and failed).
        /// </summary>
        public IReadOnlyList<string> AttemptedDirectoryNames;
    }

    /// <summary>
    /// Indicates that some backup metadata (e.g. backup index, start info file, etc.) contradicts some other backup
    /// metadata.
    /// </summary>
    /// <remarks>
    /// Such inconsistency should never occur in practice, if the application works correctly. <br/>
    /// However, it is possible and probably shouldn't be ignored if it is detected.
    /// </remarks>
    class BackupMetadataInconsistentException : BackupMetaException
    {
        public BackupMetadataInconsistentException(string backupDirectory, string message) :
            base(message, null) {
            BackupDirectory = backupDirectory;
        }

        /// <summary>
        /// The path of the backup directory whose metadata is inconsistent.
        /// </summary>
        public readonly string BackupDirectory;
    }
}
