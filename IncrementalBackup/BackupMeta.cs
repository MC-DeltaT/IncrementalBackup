using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text.Json;


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
        /// The path of the directory that was backed up. <br/>
        /// Should be normalised.
        /// </summary>
        public string SourceDirectory;
        /// <summary>
        /// The UTC time at which the backup was initiated (just before any files were copied).
        /// </summary>
        public DateTime BeginTime;
        /// <summary>
        /// The UTC time at which the backup was completed (just after the last file was copied).
        /// </summary>
        public DateTime EndTime;
        /// <summary>
        /// Tree of files and directories that were successfully backed up. <br/>
        /// The tree root is the source directory. <br/>
        /// The existence of a <see cref="DirectoryNode"/> means that directory was copied. <br/>
        /// The existence of a <see cref="FileNode"/> means that file was copied. <br/>
        /// </summary>
        public DirectoryNode BackupTree = new();
    }

    static class BackupMeta
    {
        /// <summary>
        /// The name of the file used to store the backup index in a target directory.
        /// </summary>
        private const string INDEX_FILENAME = "index.json";
        /// <summary>
        /// The name of the file used to store the backup manifest for each backup.
        /// </summary>
        private const string MANIFEST_FILENAME = "manifest.json";
        /// <summary>
        /// The length of the randomly-generated backup folder names.
        /// </summary>
        private const int BACKUP_DIRECTORY_NAME_LENGTH = 16;
        /// <summary>
        /// The number of times <see cref="CreateBackupDirectory(string)"/> will retry creation of the directory
        /// before failing.
        /// </summary>
        private const int BACKUP_DIRECTORY_CREATION_RETRIES = 20;

        /// <summary>
        /// Reads the backup index from a target directory.
        /// </summary>
        /// <remarks>
        /// The filename read from is given by <see cref="INDEX_FILENAME"/>.
        /// </remarks>
        /// <param name="targetDirectory">The target directory to read the index from.</param>
        /// <returns>The read <see cref="BackupIndex"/>.</returns>
        /// <exception cref="IndexFileNotFoundException">If the index file does not exist (including if
        /// <paramref name="targetDirectory"/> does not exist).</exception>
        /// <exception cref="IndexFileException">If the index file could not be read due to I/O errors,
        /// permission errors, etc., or the file is malformed.</exception>
        public static BackupIndex ReadIndexFile(string targetDirectory) {
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

        /// <summary>
        /// Reads the backup manifest from a backup directory.
        /// </summary>
        /// <remarks>
        /// The filename read from is given by <see cref="MANIFEST_FILENAME"/>.
        /// </remarks>
        /// <param name="backupDirectory">The backup directory to read the manifest from.</param>
        /// <returns>The read <see cref="BackupManifest"/> instance.</returns>
        /// <exception cref="ManifestFileNotFoundException">If the manifest file does not exist (including if
        /// <paramref name="backupDirectory"/> does not exist).</exception>
        /// <exception cref="ManifestFileException">If the manifest file could not be read due to I/O errors,
        /// permission errors, etc., or the file is malformed.</exception>
        public static BackupManifest ReadManifestFile(string backupDirectory) {
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

        /// <summary>
        /// Writes a backup manifest to file in a backup directory.
        /// </summary>
        /// <remarks>
        /// The filename written to is given by <see cref="MANIFEST_FILENAME"/>
        /// </remarks>
        /// <param name="manifest">The <see cref="BackupManifest"/> to write.</param>
        /// <param name="backupDirectory">The directory in which to write the manifest file.</param>
        /// <exception cref="ManifestFileException">If the file cannot be written due to I/O errors,
        /// permission errors, etc.</exception>
        public static void WriteManifestFile(BackupManifest manifest, string backupDirectory) {
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

        /// <summary>
        /// Creates a new randomly-named backup directory in the given directory.
        /// </summary>
        /// <param name="targetDirectory">The directory in which to create the backup directory.</param>
        /// <returns>The path to the new backup directory.</returns>
        /// <exception cref="CreateBackupDirectoryException">If the new directory could not be created, due to
        /// I/O errors, permission errors, etc.</exception>
        public static string CreateBackupDirectory(string targetDirectory) {
            var retries = BACKUP_DIRECTORY_CREATION_RETRIES;
            while (true) {
                var name = Utility.RandomAlphaNumericString(BACKUP_DIRECTORY_NAME_LENGTH);
                var path = Path.Join(targetDirectory, name);
                Exception? exception = null;
                // Non-atomicity :|
                if (!Directory.Exists(path) && !File.Exists(path)) {
                    try {
                        Directory.CreateDirectory(path);
                        return path;
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                            || e is NotSupportedException) {
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

    /// <summary>
    /// Thrown from <see cref="BackupMeta.ReadIndexFile(string)"/> on failure.
    /// </summary>
    class IndexFileException : ApplicationException
    {
        public IndexFileException(string? message = null, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown from <see cref="BackupMeta.ReadIndexFile(string)"/> when the index file does not exist.
    /// </summary>
    class IndexFileNotFoundException : IndexFileException
    {
        public IndexFileNotFoundException(string? message = null, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown from <see cref="BackupMeta.ReadManifestFile(string)"/> on failure.
    /// </summary>
    class ManifestFileException : ApplicationException
    {
        public ManifestFileException(string? message = null, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown from <see cref="BackupMeta.ReadManifestFile(string)"/> when the manifest file does not exist.
    /// </summary>
    class ManifestFileNotFoundException : ManifestFileException
    {
        public ManifestFileNotFoundException(string? message = null, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown from <see cref="BackupMeta.CreateBackupDirectory(string)"/> on failure.
    /// </summary>
    class CreateBackupDirectoryException : ApplicationException
    {
        public CreateBackupDirectoryException(string? message = null, Exception? innerException = null) : base(message, innerException) { }
    }
}
