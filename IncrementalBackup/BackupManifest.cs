﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;


namespace IncrementalBackup
{
    /// <summary>
    /// Records all the files and directories that were successfully backed up.
    /// </summary>
    class BackupManifest
    {
        /// <summary>
        /// Tree of files and directories that were successfully backed up. <br/>
        /// The tree root is the source directory. <br/>
        /// </summary>
        public DirectoryNode Root = new() { Name = "root" };
    }

    static class BackupManifestReader
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
            List<DirectoryNode> directoryStack = new() { manifest.Root };

            long lineNum = 0;
            while (true) {
                string? line;
                try {
                    line = stream.ReadLine();
                }
                catch (IOException e) {
                    throw new BackupManifestFileIOException(filePath, new FilesystemException(filePath, e.Message));
                }

                if (line == null) {
                    break;
                }
                lineNum++;

                if (line.Length == 0) {
                    continue;
                }

                if (line.Length < 2 || line[1] != BackupManifestFileConstants.SEPARATOR) {
                    throw new BackupManifestFileParseException(filePath, lineNum);
                }
                switch (line[0]) {
                    case BackupManifestFileConstants.PUSH_DIRECTORY: {
                            if (line.Length <= 2) {
                                throw new BackupManifestFileParseException(filePath, lineNum);
                            }
                            var directoryName = line[2..];
                            // Shouldn't occur in practice, but we will allow entering a subdirectory more than once,
                            // there shouldn't be any issues with it.
                            var existingNode = directoryStack[^1].Subdirectories.Find(
                                d => string.Compare(d.Name, directoryName, true) == 0);
                            if (existingNode == null) {
                                DirectoryNode newNode = new() { Name = directoryName };
                                directoryStack[^1].Subdirectories.Add(newNode);
                                directoryStack.Add(newNode);
                            }
                            else {
                                directoryStack.Add(existingNode);
                            }
                            break;
                        }
                    case BackupManifestFileConstants.POP_DIRECTORY: {
                            if (line.Length > 2) {
                                throw new BackupManifestFileParseException(filePath, lineNum);
                            }
                            if (directoryStack.Count <= 1) {
                                throw new BackupManifestFileParseException(filePath, lineNum);
                            }
                            directoryStack.RemoveAt(directoryStack.Count - 1);
                            break;
                        }
                    case BackupManifestFileConstants.RECORD_FILE: {
                            if (line.Length <= 2) {
                                throw new BackupManifestFileParseException(filePath, lineNum);
                            }
                            var filename = line[2..];
                            // Technically we should check if the file has already been read, but in practice there
                            // shouldn't be any duplicate files, and duplicates shouldn't cause any issues, so we
                            // won't do any checks for performance reasons.
                            directoryStack[^1].Files.Add(filename);
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
                return new(filePath, new UTF8Encoding(false, true));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException) {
                throw new BackupManifestFileIOException(filePath, new InvalidPathException(filePath));
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) {
                throw new BackupManifestFileIOException(filePath, new PathNotFoundException(filePath));
            }
        }
    }

    /// <summary>
    /// Incrementally writes a backup manifest to file. <br/>
    /// Tracks the depth-first search used to explore the backup source location. The search's current
    /// directory is manipulated with the <see cref="PushDirectory(string)"/> and <see cref="PopDirectory"/>
    /// methods.
    /// </summary>
    /// <remarks>
    /// Writing the manifest incrementally is important in case the application is prematurely terminated
    /// due to some uncontrollable factor and we may not get a chance to save the manifest all at once.
    /// Without the manifest written, a backup is effectively useless, as it could not be built upon in
    /// the next backup.
    /// </remarks>
    class BackupManifestWriter : Disposable
    {
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
                Stream = new(filePath, false, new UTF8Encoding(false, true));
            }
            catch (DirectoryNotFoundException) {
                throw new BackupManifestFileIOException(filePath, new PathNotFoundException(filePath));
            }
            catch (Exception e) when (e is UnauthorizedAccessException or SecurityException) {
                throw new BackupManifestFileIOException(filePath, new PathAccessDeniedException(filePath));
            }
            catch (Exception e) when (e is IOException or ArgumentException) {
                throw new BackupManifestFileIOException(filePath, new InvalidPathException(filePath));
            }
            FilePath = filePath;
            CurrentPath = new();
        }

        /// <summary>
        /// Changes the current directory to one of its subdirectories, and records it as backed up.
        /// </summary>
        /// <param name="name">The name of the subdirectory to enter. Must not be empty or contain newlines.</param>
        /// <exception cref="ArgumentException">If <paramref name="name"/> is empty.</exception>
        /// <exception cref="BackupManifestFileIOException">If the manifest file could not be written to.</exception>
        public void PushDirectory(string name) {
            if (name.Length == 0) {
                throw new ArgumentException($"{nameof(name)} must not be empty.", nameof(name));
            }
            if (name.ContainsNewlines()) {
                throw new ArgumentException($"{nameof(name)} must not contain newlines", nameof(name));
            }

            try {
                var line = $"{BackupManifestFileConstants.PUSH_DIRECTORY}{BackupManifestFileConstants.SEPARATOR}{name}";
                Stream.WriteLine(line);
                Stream.Flush();
            }
            catch (IOException e) {
                throw new BackupManifestFileIOException(FilePath, new FilesystemException(FilePath, e.Message));
            }
            CurrentPath.Add(name);
        }

        /// <summary>
        /// Changes the current directory to its parent directory.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the current directory is the backup source
        /// directory.</exception>
        /// <exception cref="BackupManifestFileIOException">If the manifest file could not be written to.</exception>
        public void PopDirectory() {
            if (CurrentPath.Count == 0) {
                throw new InvalidOperationException("No directories to pop.");
            }
            try {
                var line = $"{BackupManifestFileConstants.POP_DIRECTORY}{BackupManifestFileConstants.SEPARATOR}";
                Stream.WriteLine(line);
                Stream.Flush();
            }
            catch (IOException e) {
                throw new BackupManifestFileIOException(FilePath, new FilesystemException(FilePath, e.Message));
            }
            CurrentPath.RemoveAt(CurrentPath.Count - 1);
        }

        /// <summary>
        /// Records a file in the current directory as backed up.
        /// </summary>
        /// <param name="filename">The name of the file to record as backed up. Must not be empty or
        /// contain newlines.</param>
        /// <exception cref="ArgumentException">If <paramref name="filename"/> is empty.</exception>
        /// <exception cref="BackupManifestFileIOException">If the manifest file could not be written to.</exception>
        public void WriteFile(string filename) {
            if (filename.Length == 0) {
                throw new ArgumentException($"{nameof(filename)} must not be empty.", nameof(filename));
            }
            if (filename.ContainsNewlines()) {
                throw new ArgumentException($"{nameof(filename)} must not contain newlines", nameof(filename));
            }

            try {
                var line = $"{BackupManifestFileConstants.RECORD_FILE}{BackupManifestFileConstants.SEPARATOR}{filename}";
                Stream.WriteLine(line);
                Stream.Flush();
            }
            catch (IOException e) {
                throw new BackupManifestFileIOException(FilePath, new FilesystemException(FilePath, e.Message));
            }
        }

        protected override void DisposeManaged() {
            Stream.Dispose();
            base.DisposeManaged();
        }

        /// <summary>
        /// The path of the manifest file being written to. Kept in case it's needed for error information.
        /// </summary>
        private readonly string FilePath;
        /// <summary>
        /// Writes to the manifest file.
        /// </summary>
        private readonly StreamWriter Stream;
        /// <summary>
        /// The stack of directories representing the current path being explored in the backup source directory. <br/>
        /// The stack is relative to the backup source directory, i.e. empty stack represents the source directory
        /// itself.
        /// </summary>
        private readonly List<string> CurrentPath;
    }

    static class BackupManifestFileConstants
    {
        public const char PUSH_DIRECTORY = 'd';
        public const char POP_DIRECTORY = 'p';
        public const char RECORD_FILE = 'f';
        public const char SEPARATOR = ';';
    }

    /// <summary>
    /// Indicates a backup manifest file operation failed.
    /// </summary>
    abstract class BackupManifestFileException : Exception
    {
        public BackupManifestFileException(string filePath, string message, Exception? innerException) :
            base(message, innerException) {
            FilePath = filePath;
        }

        /// <summary>
        /// Path of the backup manifest file that was being accessed.
        /// </summary>
        public readonly string FilePath;
    }

    /// <summary>
    /// Indicates a backup manifest file operation failed due to filesystem-related errors.
    /// </summary>
    class BackupManifestFileIOException : BackupManifestFileException
    {
        public BackupManifestFileIOException(string filePath, FilesystemException innerException) :
            base(filePath, $"Failed to access backup manifest file \"{filePath}\"", innerException) { }

        public new FilesystemException InnerException {
            get => (FilesystemException)base.InnerException;
        }
    }

    /// <summary>
    /// Indicates a backup manifest file could not be parsed because it is not in a valid format.
    /// </summary>
    class BackupManifestFileParseException : BackupManifestFileException
    {
        public BackupManifestFileParseException(string filePath, long line) :
            base(filePath, $"Failed to parse backup manifest file \"{filePath}\"", null) {
            Line = line;
        }

        /// <summary>
        /// 1-indexed number of the invalid line.
        /// </summary>
        public readonly long Line;
    }
}
