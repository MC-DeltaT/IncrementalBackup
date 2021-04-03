using System;
using System.IO;
using System.Security;
using System.Text.Json;


namespace IncrementalBackup
{
    static class Meta
    {
        private const string MANIFEST_FILENAME = "manifest.json";
        private const string INDEX_FILENAME = "index.json";
        private const int BACKUP_DIRECTORY_NAME_LENGTH = 16;

        public static BackupIndex ReadBackupIndex(string targetDirectory) {
            var indexPath = Path.Join(targetDirectory, INDEX_FILENAME);

            BackupIndex? index;
            try {
                index = JsonSerializer.Deserialize<BackupIndex>(File.ReadAllBytes(indexPath));
            }
            catch (Exception e) when (e is DirectoryNotFoundException || e is FileNotFoundException) {
                throw new IndexFileNotFoundException(innerException: e);
            }
            catch (IOException e) {
                throw new IndexFileException(e.Message, e);
            }
            catch (Exception e) when (e is UnauthorizedAccessException || e is SecurityException) {
                throw new IndexFileException("Access denied.", e);
            }
            catch (JsonException e) {
                throw new IndexFileException("Malformed contents.", e);
            }

            if (index == null) {
                throw new IndexFileException("Malformed contents.");
            }
            else {
                return index;
            }
        }

        public static BackupManifest ReadBackupManifest(string backupDirectory) {
            var manifestPath = Path.Join(backupDirectory, MANIFEST_FILENAME);

            BackupManifest? manifest;
            try {
                manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllBytes(manifestPath));
            }
            catch (Exception e) when (e is DirectoryNotFoundException || e is FileNotFoundException) {
                throw new ManifestFileNotFoundException(innerException: e);
            }
            catch (IOException e) {
                throw new ManifestFileException(e.Message, e);
            }
            catch (Exception e) when (e is UnauthorizedAccessException || e is SecurityException) {
                throw new ManifestFileException("Access denied.", e);
            }
            catch (JsonException e) {
                throw new ManifestFileException("Malformed contents.", e);
            }

            if (manifest == null) {
                throw new ManifestFileException("Malformed contents.");
            }
            else {
                return manifest;
            }
        }

        public static void WriteBackupManifest(BackupManifest manifest, string backupDirectory) {
            var manifestPath = Path.Join(backupDirectory, MANIFEST_FILENAME);
            try {
                using var stream = File.Create(manifestPath);
                var json = JsonSerializer.SerializeToUtf8Bytes(manifest);
                stream.Write(json);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException) {
                throw new ManifestFileException("Path is invalid.", e);
            }
            catch (IOException e) {
                throw new ManifestFileException(e.Message, e);
            }
            catch (UnauthorizedAccessException e) {
                throw new ManifestFileException("Access denied.", e);
            }
        }

        public static DirectoryInfo CreateBackupDirectory(string targetDirectory) {
            var retries = 20;
            while (true) {
                var name = Utility.RandomAlphaNumericString(BACKUP_DIRECTORY_NAME_LENGTH);
                var path = Path.Join(targetDirectory, name);
                Exception? exception = null;
                // Non-atomicity :|
                if (!Directory.Exists(path) && !File.Exists(path)) {
                    try {
                        return Directory.CreateDirectory(path);
                    }
                    catch (Exception e) {
                        exception = e;
                    }
                }
                if (retries <= 0) {
                    throw new CreateBackupDirectoryException(innerException: exception);
                }
                retries--;
            }
        }
    }

    class IndexFileException : ApplicationException
    {
        public IndexFileException(string? message = null, Exception? innerException = null) : base(message, innerException) {}
    }

    class IndexFileNotFoundException : IndexFileException
    {
        public IndexFileNotFoundException(string? message = null, Exception? innerException = null) : base(message, innerException) {}
    }

    class ManifestFileException : ApplicationException
    {
        public ManifestFileException(string? message = null, Exception? innerException = null) : base(message, innerException) {}
    }

    class ManifestFileNotFoundException : ManifestFileException
    {
        public ManifestFileNotFoundException(string? message = null, Exception? innerException = null) : base(message, innerException) {}
    }

    class CreateBackupDirectoryException : ApplicationException
    {
        public CreateBackupDirectoryException(string? message = null, Exception? innerException = null) : base(message, innerException) { }
    }
}
