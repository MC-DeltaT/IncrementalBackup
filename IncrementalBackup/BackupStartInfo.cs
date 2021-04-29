using System;
using System.IO;
using System.Security;
using System.Text.Json;


namespace IncrementalBackup
{
    /// <summary>
    /// Information written to the backup directory at the start of a backup.
    /// </summary>
    /// <param name="SourceDirectory">The path of the directory that was backed up. Should be normalised.</param>
    /// <param name="StartTime">The UTC time at which the backup was initiated (just before any files were copied).
    /// </param>
    record BackupStartInfo(
        string SourceDirectory,
        DateTime StartTime
    );

    static class BackupStartInfoReader
    {
        /// <summary>
        /// Reads backup start info from file.
        /// </summary>
        /// <param name="filePath">The path of the file to read from.</param>
        /// <returns>The read backup start info.</returns>
        /// <exception cref="BackupStartInfoFileIOException">If the file could not be read.</exception>
        /// <exception cref="BackupStartInfoFileParseException">If the file is not a valid backup start info.
        /// </exception>
        public static BackupStartInfo Read(string filePath) {
            byte[] bytes;
            try {
                bytes = File.ReadAllBytes(filePath);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) {
                throw new BackupStartInfoFileIOException(filePath, new InvalidPathException(filePath));
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) {
                throw new BackupStartInfoFileIOException(filePath, new PathNotFoundException(filePath));
            }
            catch (Exception e) when (e is UnauthorizedAccessException or SecurityException) {
                throw new BackupStartInfoFileIOException(filePath, new PathAccessDeniedException(filePath));
            }
            catch (IOException e) {
                throw new BackupStartInfoFileIOException(filePath,
                    new FilesystemException(filePath, e.Message));
            }

            BackupStartInfo? value;
            try {
                value = JsonSerializer.Deserialize<BackupStartInfo>(bytes);
            }
            catch (JsonException e) {
                throw new BackupStartInfoFileParseException(filePath, e);
            }
            if (value is null) {
                throw new BackupStartInfoFileParseException(filePath, "null not allowed");
            }
            else {
                return value;
            }
        }
    }

    static class BackupStartInfoWriter
    {
        /// <summary>
        /// Writes backup start info to file. <br/>
        /// The file is created new or overwritten if it exists.
        /// </summary>
        /// <param name="filePath">The path of the file to write to.</param>
        /// <param name="value">The backup start info to write.</param>
        /// <exception cref="BackupStartInfoFileIOException">If the file could not be written to.</exception>
        public static void Write(string filePath, BackupStartInfo value) {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            try {
                File.WriteAllBytes(filePath, bytes);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) {
                throw new BackupStartInfoFileIOException(filePath, new InvalidPathException(filePath));
            }
            catch (DirectoryNotFoundException) {
                throw new BackupStartInfoFileIOException(filePath, new PathNotFoundException(filePath));
            }
            catch (Exception e) when (e is UnauthorizedAccessException or SecurityException) {
                throw new BackupStartInfoFileIOException(filePath, new PathAccessDeniedException(filePath));
            }
            catch (IOException e) {
                throw new BackupStartInfoFileIOException(filePath,
                    new FilesystemException(filePath, e.Message));
            }
        }
    }

    /// <summary>
    /// Indicates a backup start info file operation failed.
    /// </summary>
    abstract class BackupStartInfoFileException : BackupMetaFileException
    {
        public BackupStartInfoFileException(string filePath, string message, Exception? innerException) :
            base(filePath, message, innerException) {}
    }

    /// <summary>
    /// Indicates a backup start info file operation failed due to filesystem-related errors.
    /// </summary>
    class BackupStartInfoFileIOException : BackupStartInfoFileException
    {
        public BackupStartInfoFileIOException(string filePath, FilesystemException innerException) :
            base(filePath,
                $"Failed to access backup start info file \"{filePath}\": {innerException.Reason}", innerException) { }

        public new FilesystemException InnerException =>
            (FilesystemException)base.InnerException!;
    }

    /// <summary>
    /// Indicates a backup start info file could not be parsed because it is not in a valid format.
    /// </summary>
    class BackupStartInfoFileParseException : BackupStartInfoFileException
    {
        public BackupStartInfoFileParseException(string filePath, JsonException innerException) :
            base(filePath, $"Failed to parse backup start info file \"{filePath}\": {innerException.Message}",
                innerException) { }

        public BackupStartInfoFileParseException(string filePath, string reason) :
            base(filePath, $"Failed to parse backup start info file \"{filePath}\": {reason}", null) { }

        public new JsonException? InnerException =>
            base.InnerException as JsonException;
    }
}
