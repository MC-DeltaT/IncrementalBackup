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

                if (line is null) {
                    break;
                }
                lineNum++;

                // Empty line ok, just skip it.
                if (line.Length == 0) {
                    continue;
                }
                
                // Split on first separator (should be exactly 1).
                var parts = line.Split(BackupIndexFileConstants.SEPARATOR, 2);
                if (parts.Length != 2) {
                    throw new BackupIndexFileParseException(filePath, lineNum);
                }

                // Note that empty values are valid, even though that shouldn't happen in practice.
                var backupDirectory = parts[0];
                var backupSourcePath = DecodePath(parts[1]);

                index.Backups[backupDirectory] = backupSourcePath;
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

        /// <summary>
        /// Inverts <see cref="BackupIndexWriter.EncodePath(string)"/>.
        /// </summary>
        /// <param name="encodedPath">The encoded path.</param>
        /// <returns>The decoded path.</returns>
        private static string DecodePath(string encodedPath) =>
            encodedPath.Replace(@"\r", "\r").Replace(@"\n", "\n").Replace(@"\\", @"\");
    }

    class BackupIndexWriter
    {
        /// <summary>
        /// Writes a new backup entry to a backup index file.
        /// </summary>
        /// <param name="indexFilePath">The path of the index file to write to. May be a nonexistent file, in which
        /// case it will be created.</param>
        /// <param name="backupDirectory">The name of the backup directory. Must not contain
        /// <see cref="BackupIndexFileConstants.SEPARATOR"/> or newline characters.</param>
        /// <param name="backupSourcePath">The source directory path for the backup.</param>
        /// <exception cref="ArgumentException">If <paramref name="backupDirectory"/> contains invalid characters.
        /// </exception>
        /// <exception cref="BackupIndexFileIOException">Failed to write to the index file due to filesystem-related
        /// errors.</exception>
        public static void AddEntry(string indexFilePath, string backupDirectory, string backupSourcePath) {
            if (backupDirectory.Contains(BackupIndexFileConstants.SEPARATOR)) {
                throw new ArgumentException(
                    $"{nameof(backupDirectory)} must not contain {BackupIndexFileConstants.SEPARATOR}",
                    nameof(backupDirectory));
            }
            if (backupDirectory.ContainsNewlines()) {
                throw new ArgumentException($"{nameof(backupDirectory)} must not contain newlines.",
                    nameof(backupDirectory));
            }

            var entry = $"{backupDirectory}{BackupIndexFileConstants.SEPARATOR}{EncodePath(backupSourcePath)}";
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

        /// <summary>
        /// Encodes a path for use in a backup index. <br/>
        /// Specifically, escapes newline characters so each entry in the index can always be 1 line.
        /// </summary>
        /// <param name="path">The path to encode.</param>
        /// <returns>The encoded path.</returns>
        private static string EncodePath(string path) =>
            path.Replace(@"\", @"\\").Replace("\n", @"\n").Replace("\r", @"\r");
    }

    static class BackupIndexFileConstants
    {
        public const char SEPARATOR = ';';
    }

    /// <summary>
    /// Indicates a backup index file operation failed.
    /// </summary>
    abstract class BackupIndexFileException : BackupMetaFileException
    {
        public BackupIndexFileException(string filePath, string message, Exception? innerException) :
            base(filePath, message, innerException) {}
    }

    /// <summary>
    /// Indicates a backup index file operation failed due to filesystem-related errors.
    /// </summary>
    class BackupIndexFileIOException : BackupIndexFileException
    {
        public BackupIndexFileIOException(string filePath, FilesystemException innerException) :
            base(filePath, $"Failed to access backup index file \"{filePath}\": {innerException.Reason}",
                innerException) { }

        public new FilesystemException InnerException =>
            (FilesystemException)base.InnerException!;
    }

    /// <summary>
    /// Indicates a backup index file could not be parsed because it is not in a valid format.
    /// </summary>
    class BackupIndexFileParseException : BackupIndexFileException
    {
        public BackupIndexFileParseException(string filePath, long line) :
            base(filePath, $"Failed to parse backup index file \"{filePath}\", line {line}",
                null) {
            Line = line;
        }

        /// <summary>
        /// 1-indexed number of the invalid line.
        /// </summary>
        public readonly long Line;
    }
}
