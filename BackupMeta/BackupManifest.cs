using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;


namespace IncrementalBackup
{
    /// <summary>
    /// Records all the files and directories that were successfully backed up.
    /// </summary>
    public class BackupManifest
    {
        /// <summary>
        /// Tree of files and directories in the backup manifest. <br/>
        /// The tree root is the source directory. <br/>
        /// </summary>
        public Directory Root = new("source", new());

        public abstract class Entry { }

        /// <summary>
        /// Represents a directory that was backed up (copied).
        /// </summary>
        public class Directory : Entry
        {
            public Directory(string name, List<Entry> entry) {
                Name = name;
                Entries = entry;
            }

            /// <summary>
            /// The name of the directory.
            /// </summary>
            public string Name;
            /// <summary>
            /// Manifest entries directly contained in this directory.
            /// </summary>
            public List<Entry> Entries;
        }

        /// <summary>
        /// Represents a directory that was removed, compared to the previous backup.
        /// </summary>
        public class RemovedDirectory : Entry
        {
            public RemovedDirectory(string name) {
                Name = name;
            }

            /// <summary>
            /// The name of the directory.
            /// </summary>
            public string Name;
        }

        /// <summary>
        /// Represents a file that was copied (due to having been modified since the last backup).
        /// </summary>
        public class CopiedFile : Entry
        {
            public CopiedFile(string name) {
                Name = name;
            }

            /// <summary>
            /// The name of the file.
            /// </summary>
            public string Name;
        }

        /// <summary>
        /// Represents a file that was removed, compared to the previous backup.
        /// </summary>
        public class RemovedFile : Entry
        {
            public RemovedFile(string name) {
                Name = name;
            }

            /// <summary>
            /// The name of the file.
            /// </summary>
            public string Name;
        }
    }

    public static class BackupManifestReader
    {
        /// <summary>
        /// Reads a backup manifest from file.
        /// </summary>
        /// <param name="filePath">The path to the manifest file.</param>
        /// <returns>The read backup manifest.</returns>
        /// <exception cref="BackupManifestFileIOException">If a filesystem error occurs during reading.</exception>
        /// <exception cref="BackupManifestFileParseException">If the file is not a valid backup manifest.</exception>
        public static BackupManifest Read(string filePath) {
            using var stream = OpenFile(filePath);

            BackupManifest manifest = new();
            List<BackupManifest.Directory> directoryStack = new() { manifest.Root };

            long lineNum = 0;
            while (true) {
                string? line;
                try {
                    line = FilesystemException.ConvertSystemException(() => stream.ReadLine(), () => filePath);
                }
                catch (FilesystemException e) {
                    throw new BackupManifestFileIOException(filePath, e);
                }

                if (line is null) {
                    break;
                }
                lineNum++;

                // Empty lines ok, just skip them.
                if (line.Length == 0) {
                    continue;
                }

                if (line.Length < 3 || line[2] != BackupManifestFileConstants.SEPARATOR) {
                    throw new BackupManifestFileParseException(filePath, lineNum);
                }
                var command = line[0..2];
                var argument = line[3..];
                switch (command) {
                    case BackupManifestFileConstants.ENTER_DIRECTORY: {
                            // Note that empty directory names are allowed, even though it should never occur in
                            // practice.
                            var directoryName = Utility.NewlineDecode(argument);
                            // Shouldn't occur in practice, but we will allow entering a subdirectory more than once,
                            // there shouldn't be any issues with it.
                            var existingNode = directoryStack[^1].Entries.OfType<BackupManifest.Directory>()
                                .FirstOrDefault(d => Utility.PathEqual(d.Name, directoryName));
                            if (existingNode is null) {
                                BackupManifest.Directory newEntry = new(directoryName, new());
                                directoryStack[^1].Entries.Add(newEntry);
                                directoryStack.Add(newEntry);
                            }
                            else {
                                directoryStack.Add(existingNode);
                            }
                            break;
                        }
                    case BackupManifestFileConstants.BACKTRACK_DIRECTORY: {
                            if (line.Length > 3) {
                                throw new BackupManifestFileParseException(filePath, lineNum);
                            }
                            if (directoryStack.Count <= 1) {
                                throw new BackupManifestFileParseException(filePath, lineNum);
                            }
                            directoryStack.RemoveAt(directoryStack.Count - 1);
                            break;
                        }
                    case BackupManifestFileConstants.DIRECTORY_REMOVED: {
                            var directoryName = Utility.NewlineDecode(argument);
                            directoryStack[^1].Entries.Add(new BackupManifest.RemovedDirectory(directoryName));
                            break;
                        }
                    case BackupManifestFileConstants.FILE_COPIED: {
                            // Note that empty filenames are allowed, even though it should never occur in practice.
                            var filename = Utility.NewlineDecode(argument);
                            // Technically we should check if the file has already been read, but in practice there
                            // shouldn't be any duplicate files, and duplicates shouldn't cause any issues, so we
                            // won't do any checks for performance reasons.
                            directoryStack[^1].Entries.Add(new BackupManifest.CopiedFile(filename));
                            break;
                        }
                    case BackupManifestFileConstants.FILE_REMOVED: {
                            var filename = Utility.NewlineDecode(argument);
                            directoryStack[^1].Entries.Add(new BackupManifest.RemovedFile(filename));
                            break;
                        }
                    default:
                        throw new BackupManifestFileParseException(filePath, lineNum);
                }
            }

            return manifest;
        }

        /// <summary>
        /// Opens a backup manifest file for reading.
        /// </summary>
        /// <param name="filePath">The path to the manifest file.</param>
        /// <returns>A new <see cref="StreamReader"/> associated with the file.</returns>
        /// <exception cref="BackupManifestFileIOException">If the file could not be opened.</exception>
        private static StreamReader OpenFile(string filePath) {
            try {
                return FilesystemException.ConvertSystemException(
                    () => new StreamReader(filePath, new UTF8Encoding(false, true)), () => filePath);
            }
            catch (FilesystemException e) {
                throw new BackupManifestFileIOException(filePath, e);
            }
        }
    }

    /// <summary>
    /// Incrementally writes a backup manifest to file. <br/>
    /// Tracks the depth-first search used to explore the backup source location. The search's current
    /// directory is manipulated with the <see cref="EnterDirectory(string)"/> and <see cref="BacktrackDirectory"/>
    /// methods.
    /// </summary>
    /// <remarks>
    /// Writing the manifest incrementally is important in case the application is prematurely terminated
    /// due to some uncontrollable factor and we may not get a chance to save the manifest all at once.
    /// Without the manifest written, a backup is effectively useless, as it could not be built upon in
    /// the next backup.
    /// </remarks>
    public class BackupManifestWriter : Disposable {
        /// <summary>
        /// Constructs an instance that writes a new backup manifest the to given file. <br/>
        /// The file is created or overwritten if it exists.
        /// The current path is set to the backup source directory (i.e. the backup root).
        /// </summary>
        /// <param name="filePath">The path of the file to write.</param>
        /// <exception cref="BackupManifestFileIOException">If the manifest file can't be created/opened.
        /// </exception>
        public BackupManifestWriter(string filePath) {
            try {
                Stream = FilesystemException.ConvertSystemException(
                    () => new StreamWriter(filePath, false, new UTF8Encoding(false, true)),
                    () => filePath);
            }
            catch (FilesystemException e) {
                throw new BackupManifestFileIOException(filePath, e);
            }
            FilePath = filePath;
            PathDepth = 0;
        }

        /// <summary>
        /// The path of the manifest file being written to.
        /// </summary>
        public readonly string FilePath;

        /// <summary>
        /// The number of directories deep the current path is, relative to the backup source directory. <br/>
        /// 0 = backup source directory.
        /// </summary>
        public long PathDepth { get; private set; }

        /// <summary>
        /// Changes the current directory to one of its subdirectories, and records it as backed up.
        /// </summary>
        /// <param name="name">The name of the subdirectory to enter.</param>
        /// <exception cref="BackupManifestFileIOException">If the manifest file could not be written to.</exception>
        public void EnterDirectory(string name) {
            var encodedName = Utility.NewlineEncode(name);
            var line = $"{BackupManifestFileConstants.ENTER_DIRECTORY}{BackupManifestFileConstants.SEPARATOR}{encodedName}";
            WriteLine(line);
            PathDepth++;
        }

        /// <summary>
        /// Changes the current directory to its parent directory.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the current directory is the backup source directory.
        /// </exception>
        /// <exception cref="BackupManifestFileIOException">If the manifest file could not be written to.</exception>
        public void BacktrackDirectory() {
            if (PathDepth == 0) {
                throw new InvalidOperationException("Current directory is already backup source directory.");
            }
            var line = $"{BackupManifestFileConstants.BACKTRACK_DIRECTORY}{BackupManifestFileConstants.SEPARATOR}";
            WriteLine(line);
            PathDepth--;
        }

        /// <summary>
        /// Records a directory in the current directory as removed, compared to the last backup.
        /// </summary>
        /// <param name="name">The name of the directory to record.</param>
        /// <exception cref="BackupManifestFileIOException">If the manifest file could not be written to.</exception>
        public void RecordDirectoryRemoved(string name) {
            var encodedName = Utility.NewlineEncode(name);
            var line = $"{BackupManifestFileConstants.DIRECTORY_REMOVED}{BackupManifestFileConstants.SEPARATOR}{encodedName}";
            WriteLine(line);
        }

        /// <summary>
        /// Records a file in the current directory as copied.
        /// </summary>
        /// <param name="name">The name of the file to record.</param>
        /// <exception cref="BackupManifestFileIOException">If the manifest file could not be written to.</exception>
        public void RecordFileCopied(string name) {
            var encodedName = Utility.NewlineEncode(name);
            var line = $"{BackupManifestFileConstants.FILE_COPIED}{BackupManifestFileConstants.SEPARATOR}{encodedName}";
            WriteLine(line);
        }

        /// <summary>
        /// Records a file in the current directory as removed, compared to the last backup.
        /// </summary>
        /// <param name="name">The name of the file to record.</param>
        /// <exception cref="BackupManifestFileIOException">If the manifest file could not be written to.</exception>
        public void RecordFileRemoved(string name) {
            var encodedName = Utility.NewlineEncode(name);
            var line = $"{BackupManifestFileConstants.FILE_REMOVED}{BackupManifestFileConstants.SEPARATOR}{encodedName}";
            WriteLine(line);
        }

        protected override void DisposeManaged() {
            Stream.Dispose();
            base.DisposeManaged();
        }

        /// <summary>
        /// Writes to the manifest file.
        /// </summary>
        private readonly StreamWriter Stream;

        /// <summary>
        /// Writes a line to <see cref="Stream"/> and flushes it.
        /// </summary>
        /// <param name="line">The line to write. Should not include a trailing newline.</param>
        /// <exception cref="BackupManifestFileIOException">If the line could not be written.</exception>
        private void WriteLine(string line) {
            try {
                FilesystemException.ConvertSystemException(() => {
                    Stream.WriteLine(line);
                    Stream.Flush();
                }, () => FilePath);
            }
            catch (FilesystemException e) {
                throw new BackupManifestFileIOException(FilePath, e);
            }
        }
    }

    public static class BackupManifestFileConstants
    {
        public const string ENTER_DIRECTORY = ">d";
        public const string BACKTRACK_DIRECTORY = "<d";
        public const string DIRECTORY_REMOVED = "-d";
        public const string FILE_COPIED = "+f";
        public const string FILE_REMOVED = "-f";
        public const char SEPARATOR = ';';
    }

    /// <summary>
    /// Indicates a backup manifest file operation failed.
    /// </summary>
    public abstract class BackupManifestFileException : BackupMetaFileException
    {
        public BackupManifestFileException(string filePath, string message, Exception? innerException) :
            base(filePath, message, innerException) {}
    }

    /// <summary>
    /// Indicates a backup manifest file operation failed due to filesystem-related errors.
    /// </summary>
    public class BackupManifestFileIOException : BackupManifestFileException
    {
        public BackupManifestFileIOException(string filePath, FilesystemException innerException) :
            base(filePath, $"Failed to access backup manifest file \"{filePath}\": {innerException.Reason}",
                innerException) { }

        public new FilesystemException InnerException =>
            (FilesystemException)base.InnerException!;
    }

    /// <summary>
    /// Indicates a backup manifest file could not be parsed because it is not in a valid format.
    /// </summary>
    public class BackupManifestFileParseException : BackupManifestFileException
    {
        public BackupManifestFileParseException(string filePath, long line) :
            base(filePath,  $"Failed to parse backup manifest file \"{filePath}\", line {line}", null) {
            Line = line;
        }

        /// <summary>
        /// 1-indexed number of the invalid line.
        /// </summary>
        public readonly long Line;
    }
}
