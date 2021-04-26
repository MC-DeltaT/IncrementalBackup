using System;
using System.IO;
using System.Security;
using System.Text.Json;


namespace IncrementalBackup
{
    /// <summary>
    /// Information written to the backup directory when the backup completes successfully.
    /// </summary>
    /// <param name="EndTime">The UTC time at which the backup was completed (just after the last
    /// file was copied).</param>
    record BackupCompleteInfo(
        DateTime EndTime
    );

    static class BackupCompleteInfoReader
    {
        /// <summary>
        /// Reads backup completion info from file.
        /// </summary>
        /// <param name="filePath">The path of the file to read from.</param>
        /// <returns>The read backup completion info.</returns>
        /// <exception cref="BackupCompleteInfoFileIOException">If the file could not be read.</exception>
        /// <exception cref="BackupCompleteInfoFileParseException">If the file is not valid backup completion info.
        /// </exception>
        public static BackupCompleteInfo Read(string filePath) {
            byte[] bytes;
            try {
                bytes = File.ReadAllBytes(filePath);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) {
                throw new BackupCompleteInfoFileIOException(filePath, new InvalidPathException(filePath));
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) {
                throw new BackupCompleteInfoFileIOException(filePath, new PathNotFoundException(filePath));
            }
            catch (Exception e) when (e is UnauthorizedAccessException or SecurityException) {
                throw new BackupCompleteInfoFileIOException(filePath, new PathAccessDeniedException(filePath));
            }
            catch (IOException e) {
                throw new BackupCompleteInfoFileIOException(filePath, new FilesystemException(filePath, e.Message));
            }

            BackupCompleteInfo? value;
            try {
                value = JsonSerializer.Deserialize<BackupCompleteInfo>(bytes);
            }
            catch (JsonException e) {
                throw new BackupCompleteInfoFileParseException(filePath, e);
            }
            if (value == null) {
                throw new BackupCompleteInfoFileParseException(filePath, null);
            }
            else {
                return value;
            }
        }
    }

    static class BackupCompleteInfoWriter
    {
        /// <summary>
        /// Writes backup completion info to file. <br/>
        /// The file is created new or overwritten if it exists.
        /// </summary>
        /// <param name="filePath">The path of the file to write to.</param>
        /// <param name="value">The backup completion info to write.</param>
        /// <exception cref="BackupCompleteInfoFileIOException">If the file could not be written to.</exception>
        public static void Write(string filePath, BackupCompleteInfo value) {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            try {
                File.WriteAllBytes(filePath, bytes);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) {
                throw new BackupCompleteInfoFileIOException(filePath, new InvalidPathException(filePath));
            }
            catch (DirectoryNotFoundException) {
                throw new BackupCompleteInfoFileIOException(filePath, new PathNotFoundException(filePath));
            }
            catch (Exception e) when (e is UnauthorizedAccessException or SecurityException) {
                throw new BackupCompleteInfoFileIOException(filePath, new PathAccessDeniedException(filePath));
            }
            catch (IOException e) {
                throw new BackupCompleteInfoFileIOException(filePath, new FilesystemException(filePath, e.Message));
            }
        }
    }

    /// <summary>
    /// Indicates a backup completion info file operation failed.
    /// </summary>
    abstract class BackupCompleteInfoFileException : BackupMetaFileException
    {
        public BackupCompleteInfoFileException(string filePath, string message, Exception? innerException) :
            base(filePath, message, innerException) {}
    }

    /// <summary>
    /// Indicates a backup completion info file operation failed due to filesystem-related errors.
    /// </summary>
    class BackupCompleteInfoFileIOException : BackupCompleteInfoFileException
    {
        public BackupCompleteInfoFileIOException(string filePath, FilesystemException innerException) :
            base(filePath, $"Failed to access backup completion info file \"{filePath}\"", innerException) { }

        public new FilesystemException InnerException {
            get => (FilesystemException)base.InnerException;
        }
    }

    /// <summary>
    /// Indicates a backup completion info file could not be parsed because it is not in a valid format.
    /// </summary>
    class BackupCompleteInfoFileParseException : BackupCompleteInfoFileException
    {
        public BackupCompleteInfoFileParseException(string filePath, JsonException? innerException) :
            base(filePath, $"Failed to parse backup completion info file \"{filePath}\"", innerException) { }

        public new JsonException? InnerException {
            get => base.InnerException as JsonException;
        }
    }
}
