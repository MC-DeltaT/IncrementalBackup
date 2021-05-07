using System;
using System.IO;
using System.Text.Json;


namespace IncrementalBackup
{
    /// <summary>
    /// Information written to the backup directory at the start of a backup.
    /// </summary>
    /// <param name="SourcePath">The path of the directory that was backed up. Should be normalised.</param>
    /// <param name="StartTime">The UTC time at which the backup was initiated (just before any files were copied).
    /// </param>
    public record BackupStartInfo(
        string SourcePath,
        DateTime StartTime
    );

    public static class BackupStartInfoReader
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
                bytes = FilesystemException.ConvertSystemException(() => File.ReadAllBytes(filePath), () => filePath);
            }
            catch (FilesystemException e) {
                throw new BackupStartInfoFileIOException(filePath, e);
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

    public static class BackupStartInfoWriter
    {
        /// <summary>
        /// Writes backup start info to file. <br/>
        /// The file is created new or overwritten if it exists.
        /// </summary>
        /// <param name="filePath">The path of the file to write to.</param>
        /// <param name="value">The backup start info to write.</param>
        /// <exception cref="BackupStartInfoFileIOException">If the file could not be written to.</exception>
        public static void Write(string filePath, BackupStartInfo value) {
            JsonSerializerOptions jsonOptions = new() { WriteIndented = true };     // Pretty print for human reading.
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, jsonOptions);
            try {
                FilesystemException.ConvertSystemException(() => File.WriteAllBytes(filePath, bytes), () => filePath);
            }
            catch (FilesystemException e) {
                throw new BackupStartInfoFileIOException(filePath, e);
            }
        }
    }

    /// <summary>
    /// Indicates a backup start info file operation failed.
    /// </summary>
    public abstract class BackupStartInfoFileException : BackupMetaFileException
    {
        public BackupStartInfoFileException(string filePath, string message, Exception? innerException) :
            base(filePath, message, innerException) {}
    }

    /// <summary>
    /// Indicates a backup start info file operation failed due to filesystem-related errors.
    /// </summary>
    public class BackupStartInfoFileIOException : BackupStartInfoFileException
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
    public class BackupStartInfoFileParseException : BackupStartInfoFileException
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
