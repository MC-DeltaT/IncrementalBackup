using System;
using System.Collections.Generic;
using System.IO;


namespace IncrementalBackup
{
    /// <summary>
    /// Information on a previous backup.
    /// </summary>
    /// <remarks>
    /// Used to store the previous backups for a specific source directory (to know which files have been previously
    /// backed up and when).
    /// </remarks>
    record PreviousBackup(
        DateTime StartTime,
        BackupManifest Manifest
    );

    static class BackupHistory
    {
        /// <summary>
        /// Reads previous backups in a target directory whose source directory matches the given source directory.
        /// </summary>
        /// <param name="sourceDirectory">The source directory to read backups for.</param>
        /// <param name="targetDirectory">The target directory to read backups from.</param>
        /// <param name="index">The backup index listing the backups present in <paramref name="targetDirectory"/>.
        /// </param>
        /// <param name="errorHandler">Handles exception raised from reading backup metadata.<br/>
        /// Argument will be <see cref="BackupHistoryReadException"/>, or
        /// <see cref="BackupMetadataInconsistentException"/>.</param>
        /// <returns>All the backups in <paramref name="targetDirectory"/> matching <paramref name="sourceDirectory"/>.
        /// The order of the list is unspecified.</returns>
        public static List<PreviousBackup> ReadPreviousBackups(string sourceDirectory, string targetDirectory,
                BackupIndex index, Action<BackupMetaException> errorHandler) {
            List<PreviousBackup> history = new();
            foreach (var pair in index.Backups) {
                var backupName = pair.Key;
                var backupSourceDirectory = pair.Value;

                // Paths are assumed to be already normalised.
                if (string.Compare(sourceDirectory, backupSourceDirectory, true) == 0) {
                    var backupPath = Path.Join(targetDirectory, backupName);

                    BackupStartInfo startInfo;
                    try {
                        startInfo = BackupStartInfoReader.Read(BackupMeta.StartInfoFilePath(backupPath));
                    }
                    catch (BackupStartInfoFileException e) {
                        errorHandler(new BackupHistoryReadException(backupPath, e));
                        continue;
                    }

                    // We could just assume the index file and start info file are consistent, but it might be a good
                    // idea to check just in case something goes particularly wrong.
                    if (string.Compare(sourceDirectory, startInfo.SourceDirectory, true) == 0) {
                        errorHandler(
                            new BackupMetadataInconsistentException(backupPath,
                            $"Source directory of backup start info in \"{backupPath}\" doesn't match backup index."));
                        continue;
                    }

                    BackupManifest manifest;
                    try {
                        manifest = BackupManifestReader.Read(BackupMeta.ManifestFilePath(backupPath));
                    }
                    catch (BackupManifestFileException e) {
                        errorHandler(new BackupHistoryReadException(backupPath, e));
                        continue;
                    }
                    history.Add(new(startInfo.StartTime, manifest));
                }
            }

            return history;
        }
    }

    /// <summary>
    /// Thrown from <see cref="BackupHistory.ReadPreviousBackups(string, string, BackupIndex)"/> on failure.
    /// </summary>
    class BackupHistoryReadException : BackupMetaException
    {
        public BackupHistoryReadException(string backupPath, BackupMetaFileException innerException) :
            base($"Failed to read metadata in previous backup \"{backupPath}\": {innerException.Message}",
                innerException) {
            BackupPath = backupPath;
        }

        /// <summary>
        /// The path of the backup directory that could not be read.
        /// </summary>
        public readonly string BackupPath;

        public new BackupMetaFileException InnerException =>
            (BackupMetaFileException)base.InnerException!;
    }
}
