using System;
using System.Collections.Generic;


namespace IncrementalBackup
{
    // Indexes all the backups present in a target directory.
    class BackupIndex
    {
        public Dictionary<string, string> Backups = new();      // Maps backup directory names to their source directories.
    }

    // Results of a specific backup run.
    class BackupManifest
    {
        public string? sourceDirectory = null;
        public DateTime? BeginTime = null;      // UTC.
        public DateTime? EndTime = null;        // UTC.
        public List<SkippedPath> SkippedDirectories = new();
        public List<SkippedPath> SkippedFiles = new();

        public void AddSkippedPath(string path, bool isDirectory, SkipReason reason) {
            var list = isDirectory ? SkippedDirectories : SkippedFiles;
            list.Add(new SkippedPath(path, reason));
        }
    }

    // Files/folder not backed up due to being explicitly excluded, I/O errors, permission errors, etc.
    record SkippedPath(
        string Path,
        SkipReason Reason
    );

    enum SkipReason
    {
        Excluded,           // User requested file/folder to be excluded.
        DoesntExist,        // File/folder no longer exists. Hopefully shouldn't ever occur.
        PermissionDenied
    }
}
