using System;
using System.IO;
using System.Text.Json;


namespace IncrementalBackup
{
    /// <summary>
    /// Information written to the backup directory when the backup completes successfully.
    /// </summary>
    /// <param name="EndTime">The UTC time at which the backup was completed (just after the last
    /// file was copied).</param>
    /// <param name="PathsSkipped">Indicates whether any paths were skipped due to I/O errors, permission errors, etc.
    /// (NOT inclusive of paths that were specifically requested to be exluded).</param>
    /// <param name="ManifestComplete">Indicates whether all files and directories backed up were recorded in the
    /// backup manifest file.</param>
    record BackupCompleteInfo(
        DateTime EndTime,
        bool PathsSkipped,
        bool ManifestComplete
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
                bytes = FilesystemException.ConvertSystemException(() => File.ReadAllBytes(filePath), () => filePath);
            }
            catch (FilesystemException e) {
                throw new BackupCompleteInfoFileIOException(filePath, e);
            }

            BackupCompleteInfo? value;
            try {
                value = JsonSerializer.Deserialize<BackupCompleteInfo>(bytes);
            }
            catch (JsonException e) {
                throw new BackupCompleteInfoFileParseException(filePath, e);
            }
            if (value is null) {
                throw new BackupCompleteInfoFileParseException(filePath, "null not allowed");
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
            JsonSerializerOptions jsonOptions = new() { WriteIndented = true };     // Pretty print for human reading.
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, jsonOptions);
            try {
                FilesystemException.ConvertSystemException(() => File.WriteAllBytes(filePath, bytes), () => filePath);
            }
            catch (FilesystemException e) {
                throw new BackupCompleteInfoFileIOException(filePath, e);
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
            base(filePath, $"Failed to access backup completion info file \"{filePath}\": {innerException.Reason}",
                innerException) { }

        public new FilesystemException InnerException =>
            (FilesystemException)base.InnerException!;
    }

    /// <summary>
    /// Indicates a backup completion info file could not be parsed because it is not in a valid format.
    /// </summary>
    class BackupCompleteInfoFileParseException : BackupCompleteInfoFileException
    {
        public BackupCompleteInfoFileParseException(string filePath, JsonException innerException) :
            base(filePath, $"Failed to parse backup completion info file \"{filePath}\": {innerException.Message}",
                innerException) { }

        public BackupCompleteInfoFileParseException(string filePath, string reason) :
            base(filePath, $"Failed to parse backup completion info file \"{filePath}\": {reason}", null) { }

        public new JsonException? InnerException =>
            base.InnerException as JsonException;
    }
}
