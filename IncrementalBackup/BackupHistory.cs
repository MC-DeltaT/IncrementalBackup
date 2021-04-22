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
        /// <returns>All the backups in <paramref name="targetDirectory"/> matching <paramref name="sourceDirectory"/>.
        /// The list is sorted by backup start time.</returns>
        /// <exception cref="BackupHistoryReadException">If any backup start info or manifests could not be read.
        /// </exception>
        /// <exception cref="InconsistentBackupMetadataException">If the source directory of any backup is inconsistent
        /// with the source directory listed in <paramref name="index"/>.</exception>
        public static List<PreviousBackup> ReadPreviousBackups(string sourceDirectory, string targetDirectory,
                BackupIndex index) {
            List<PreviousBackup> history = new();
            foreach (var pair in index.Backups) {
                var backupName = pair.Key;
                var backupSourceDirectory = pair.Value;

                // Paths are assumed to be already normalised.
                if (string.Compare(sourceDirectory, backupSourceDirectory, true) == 0) {
                    var backupDirectory = Path.Join(targetDirectory, backupName);

                    BackupStartInfo startInfo;
                    try {
                        startInfo = BackupStartInfoReader.Read(BackupMeta.StartInfoFilePath(backupDirectory));
                    }
                    catch (BackupStartInfoFileException e) {
                        throw new BackupHistoryReadException(targetDirectory, backupName, e);
                    }

                    // We could just assume the index file and start info file are consistent, but it might be a good
                    // idea to check just in case something goes particularly wrong.
                    if (string.Compare(sourceDirectory, startInfo.SourceDirectory, true) == 0) {
                        throw new InconsistentBackupMetadataException(backupDirectory,
                            $"Source directory in backup start info in \"{backupDirectory}\" doesn't match backup index.");
                    }

                    BackupManifest manifest;
                    try {
                        manifest = BackupManifestReader.Read(BackupMeta.ManifestFilePath(backupDirectory));
                    }
                    catch (BackupManifestFileException e) {
                        throw new BackupHistoryReadException(targetDirectory, backupName, e);
                    }
                    history.Add(new(startInfo.StartTime, manifest));
                }
            }

            history.Sort((a, b) => DateTime.Compare(a.StartTime, b.StartTime));

            return history;
        }
    }

    /// <summary>
    /// Thrown from <see cref="BackupHistory.ReadPreviousBackups(string, string, BackupIndex)"/> on failure.
    /// </summary>
    class BackupHistoryReadException : Exception
    {
        public BackupHistoryReadException(string targetDirectory, string backupName, Exception innerException) :
            base($"Failed to read previous backup \"{Path.Join(targetDirectory, backupName)}\"", innerException) {
            TargetDirectory = targetDirectory;
            BackupName = backupName;
        }

        /// <summary>
        /// The path of the target directory that was being checked.
        /// </summary>
        public readonly string TargetDirectory;

        /// <summary>
        /// The name of the backup directory that could not be read.
        /// </summary>
        public readonly string BackupName;

        /// <summary>
        /// The path of the backup directory that could not be read.
        /// </summary>
        public string BackupDirectory {
            get => Path.Join(TargetDirectory, BackupName);
        }
    }
}
