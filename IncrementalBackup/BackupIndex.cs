using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;


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

    class BackupIndexReader
    {
        /// <summary>
        /// Reads a backup index from file.
        /// </summary>
        /// <param name="filePath">The path to the index file.</param>
        /// <returns>The read backup index.</returns>
        /// <exception cref="BackupIndexFileIOException">If a filesystem error occurs during reading.</exception>
        /// <exception cref="BackupIndexFileParseException">If the file is not a valid backup index.</exception>
        public static BackupIndex Read(string filePath) {
            using var stream = OpenFile(filePath);

            BackupIndex index = new();
            long lineNum = 0;
            while (true) {
                string? line;
                try {
                    line = stream.ReadLine();
                }
                catch (IOException e) {
                    throw new BackupIndexFileIOException(filePath, new FilesystemException(filePath, e.Message));
                }

                if (line == null) {
                    break;
                }
                lineNum++;

                if (line.Length == 0) {
                    continue;
                }

                var parts = line.Split(BackupIndexFileConstants.SEPARATOR, 2);
                if (parts.Length != 2) {
                    throw new BackupIndexFileParseException(filePath, lineNum);
                }

                var backupDirectory = parts[0];
                if (backupDirectory.Length == 0) {
                    throw new BackupIndexFileParseException(filePath, lineNum);
                }
                var backupSource = parts[1];
                if (backupDirectory.Length == 0) {
                    throw new BackupIndexFileParseException(filePath, lineNum);
                }

                index.Backups[backupDirectory] = backupSource;
            }

            return index;
        }

        /// <summary>
        /// Opens a backup index file for reading.
        /// </summary>
        /// <param name="filePath">The path to the index file.</param>
        /// <returns>A new <see cref="StreamReader"/> associated with the file.</returns>
        /// <exception cref="BackupIndexFileIOException">If the file could not be opened.</exception>
        private static StreamReader OpenFile(string filePath) {
            try {
                return new(filePath, new UTF8Encoding(false, true));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException) {
                throw new BackupIndexFileIOException(filePath, new InvalidPathException(filePath));
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) {
                throw new BackupIndexFileIOException(filePath, new PathNotFoundException(filePath));
            }
        }
    }

    class BackupIndexWriter
    {
        /// <summary>
        /// Writes a new backup entry to a backup index file.
        /// </summary>
        /// <param name="indexFilePath">The path of the index file to write to. May be a nonexistent file, in which
        /// case it will be created.</param>
        /// <param name="backupDirectory">The name of the backup directory. Must not be empty or contain
        /// <see cref="BackupIndexFileConstants.SEPARATOR"/> or newline characters.</param>
        /// <param name="backupSourceDirectory">The source directory path for the backup. Must not be empty or contain
        /// newline characters.</param>
        /// <exception cref="ArgumentException">If <paramref name="backupDirectory"/> or
        /// <paramref name="backupSourceDirectory"/> are empty or contain invalid characters.</exception>
        /// <exception cref="BackupIndexFileIOException">Failed to write to the index file due to filesystem-related
        /// errors.</exception>
        public static void AddEntry(string indexFilePath, string backupDirectory, string backupSourceDirectory) {
            if (backupDirectory.Length == 0) {
                throw new ArgumentException($"{nameof(backupDirectory)} must not be empty.", nameof(backupDirectory));
            }
            if (backupDirectory.Contains(BackupIndexFileConstants.SEPARATOR)) {
                throw new ArgumentException(
                    $"{nameof(backupDirectory)} must not contain {BackupIndexFileConstants.SEPARATOR}",
                    nameof(backupDirectory));
            }
            if (backupDirectory.ContainsNewlines()) {
                throw new ArgumentException($"{nameof(backupDirectory)} must not contain newlines.",
                    nameof(backupDirectory));
            }

            if (backupSourceDirectory.Length == 0) {
                throw new ArgumentException($"{nameof(backupSourceDirectory)} must not be empty.",
                    nameof(backupSourceDirectory));
            }
            if (backupSourceDirectory.ContainsNewlines()) {
                throw new ArgumentException($"{nameof(backupSourceDirectory)} must not contain newlines.",
                    nameof(backupSourceDirectory));
            }

            var entry = $"{backupDirectory}{BackupIndexFileConstants.SEPARATOR}{backupSourceDirectory}";
            try {
                File.AppendAllText(indexFilePath, entry, new UTF8Encoding(false, true));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) {
                throw new BackupIndexFileIOException(indexFilePath, new InvalidPathException(indexFilePath));
            }
            catch (DirectoryNotFoundException) {
                throw new BackupIndexFileIOException(indexFilePath, new PathNotFoundException(indexFilePath));
            }
            catch (Exception e) when (e is UnauthorizedAccessException or SecurityException) {
                throw new BackupIndexFileIOException(indexFilePath, new PathAccessDeniedException(indexFilePath));
            }
            catch (IOException e) {
                throw new BackupIndexFileIOException(indexFilePath, new FilesystemException(indexFilePath, e.Message));
            }
        }
    }

    static class BackupIndexFileConstants
    {
        public const char SEPARATOR = ';';
    }

    /// <summary>
    /// Indicates a backup index file operation failed.
    /// </summary>
    abstract class BackupIndexFileException : Exception
    {
        public BackupIndexFileException(string filePath, string message, Exception? innerException) :
            base(message, innerException) {
            FilePath = filePath;
        }

        /// <summary>
        /// Path of the backup index file that was being accessed.
        /// </summary>
        public readonly string FilePath;
    }

    /// <summary>
    /// Indicates a backup index file operation failed due to filesystem-related errors.
    /// </summary>
    class BackupIndexFileIOException : BackupIndexFileException
    {
        public BackupIndexFileIOException(string filePath, FilesystemException innerException) :
            base(filePath, $"Failed to access backup index file \"{filePath}\"", innerException) { }

        public new FilesystemException InnerException {
            get => (FilesystemException)base.InnerException;
        }
    }

    /// <summary>
    /// Indicates a backup index file could not be parsed because it is not in a valid format.
    /// </summary>
    class BackupIndexFileParseException : BackupIndexFileException
    {
        public BackupIndexFileParseException(string filePath, long line) :
            base(filePath, $"Failed to parse backup index file \"{filePath}\"", null) {
            Line = line;
        }

        /// <summary>
        /// 1-indexed number of the invalid line.
        /// </summary>
        public readonly long Line;
    }
}
