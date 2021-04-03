using System;
using System.Collections.Generic;


namespace IncrementalBackup
{
    /// <summary>
    /// Indexes all the backups present in a target directory. <br/>
    /// Gets saved to the target directory to indicate what backups exist.
    /// </summary>
    class BackupIndex
    {
        /// <summary>
        /// Maps backup directory names to the source directory used in the backup. <br/>
        /// The source directories should be normalised.
        /// </summary>
        public Dictionary<string, string> Backups = new();
    }

    /// <summary>
    /// Results of a specific backup run. <br/>
    /// Gets saved to the backup directory to preserve the details of the backup.
    /// </summary>
    class BackupManifest
    {
        /// <summary>
        /// The location that was backed up. <br/>
        /// Should be normalised.
        /// </summary>
        public string? sourceDirectory = null;
        /// <summary>
        /// The UTC time at which the backup was initiated (just before any files were copied).
        /// </summary>
        public DateTime? BeginTime = null;
        /// <summary>
        /// The UTC time at which the backup was completed (just after the last file was copied).
        /// </summary>
        public DateTime? EndTime = null;
        /// <summary>
        /// The list of directories that were not backed up, due to being exluded, I/O errors, etc.
        /// </summary>
        public List<SkippedPath> SkippedDirectories = new();
        /// <summary>
        /// The list of files that were not backed up, due to being exluded, I/O errors, etc.
        /// </summary>
        public List<SkippedPath> SkippedFiles = new();

        /// <summary>
        /// Adds a skipped file or folder path to the manifest.
        /// </summary>
        /// <param name="path">The path that was skipped.</param>
        /// <param name="isDirectory">Indicates whether the path is a file or folder. <br/>
        /// If false, the path is added to <see cref="SkippedFiles">SkippedFiles</see>.
        /// If true, the path is added to <see cref="SkippedDirectories">SkippedDirectories</see>.</param>
        /// <param name="reason">The reason the path was skipped.</param>
        public void AddSkippedPath(string path, bool isDirectory, SkipReason reason) {
            var list = isDirectory ? SkippedDirectories : SkippedFiles;
            list.Add(new SkippedPath(path, reason));
        }
    }

    /// <summary>
    /// A file or folder that was not backed up due to being explicitly excluded, I/O errors, permission errors, etc.
    /// </summary>
    record SkippedPath(
        string Path,
        SkipReason Reason
    );

    /// <summary>
    /// Reasons why a file or folder was not included in a backup.
    /// </summary>
    enum SkipReason
    {
        /// <summary>
        /// User requested file/folder to be excluded.
        /// </summary>
        Excluded,
        /// <summary>
        /// File/folder no longer exists at time of copying. Probably won't occur in practice.
        /// </summary>
        DoesntExist,
        /// <summary>
        /// The application did not have permission to access the folder/file.
        /// </summary>
        AccessDenied
    }
}
