﻿using System;
using System.Collections.Generic;
using System.IO;


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
        /// <exception cref="ManifestFileCreateException">If the manifest file can't be created/opened.</exception>
        public BackupManifestWriter(string filePath) {
            try {
                Stream = File.CreateText(filePath);
            }
            catch (Exception e) when (e is ArgumentException or DirectoryNotFoundException or NotSupportedException
                or PathTooLongException or UnauthorizedAccessException) {
                throw new ManifestFileCreateException(filePath, innerException: e);
            }
            FilePath = filePath;
            CurrentPath = new();
        }

        /// <summary>
        /// Changes the current directory to one of its subdirectories, and records it as backed up.
        /// </summary>
        /// <param name="name">The name of the subdirectory to enter. Must not be empty or contain newlines.</param>
        /// <exception cref="ArgumentException">If <paramref name="name"/> is empty.</exception>
        /// <exception cref="ManifestFileIOException">If the manifest file could not be written to.</exception>
        public void PushDirectory(string name) {
            if (name.Length == 0) {
                throw new ArgumentException("name must not be empty.", nameof(name));
            }
            try {
                Stream.WriteLine($"{BackupManifestFileCommands.PUSH_DIRECTORY};{name}");
                Stream.Flush();
            }
            catch (IOException e) {
                throw new ManifestFileIOException(FilePath, innerException: e);
            }
            CurrentPath.Add(name);
        }

        /// <summary>
        /// Changes the current directory to its parent directory.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the current directory is the backup source
        /// directory.</exception>
        /// <exception cref="ManifestFileIOException">If the manifest file could not be written to.</exception>
        public void PopDirectory() {
            if (CurrentPath.Count == 0) {
                throw new InvalidOperationException("No directories to pop.");
            }
            try {
                Stream.WriteLine($"{BackupManifestFileCommands.POP_DIRECTORY};");
                Stream.Flush();
            }
            catch (IOException e) {
                throw new ManifestFileIOException(FilePath, innerException: e);
            }
            CurrentPath.RemoveAt(CurrentPath.Count - 1);
        }

        /// <summary>
        /// Records a file in the current directory as backed up.
        /// </summary>
        /// <param name="filename">The name of the file to record as backed up. Must not be empty or
        /// contain newlines.</param>
        /// <exception cref="ArgumentException">If <paramref name="filename"/> is empty.</exception>
        /// <exception cref="ManifestFileIOException">If the manifest file could not be written to.</exception>
        public void WriteFile(string filename) {
            if (filename.Length == 0) {
                throw new ArgumentException("filename must not be empty.", nameof(filename));
            }
            try {
                Stream.WriteLine($"{BackupManifestFileCommands.RECORD_FILE};{filename}");
                Stream.Flush();
            }
            catch (IOException e) {
                throw new ManifestFileIOException(FilePath, innerException: e);
            }
        }

        protected override void DisposeManaged() {
            Stream.Dispose();
            base.DisposeManaged();
        }

        private readonly string FilePath;
        private readonly StreamWriter Stream;
        private readonly List<string> CurrentPath;
    }

    static class BackupManifestReader
    {
        /// <summary>
        /// Reads a backup manifest from file.
        /// </summary>
        /// <param name="filePath">The path to the manifest file.</param>
        /// <returns>The read backup manifest.</returns>
        /// <exception cref="ManifestFileNotFoundException">If the specified file doesn't exist.</exception>
        /// <exception cref="ManifestFileIOException">If a file I/O error occurs during reading.</exception>
        /// <exception cref="ManifestFileInvalidException">If the file is not a valid backup manifest.</exception>
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
                    throw new ManifestFileIOException(filePath, innerException: e);
                }

                if (line == null) {
                    break;
                }
                lineNum++;

                if (line.Length == 0) {
                    continue;
                }

                if (line.Length < 2 || line[1] != ';') {
                    throw new ManifestFileInvalidException(filePath, lineNum);
                }
                switch (line[0]) {
                    case BackupManifestFileCommands.PUSH_DIRECTORY: {
                            if (line.Length <= 2) {
                                throw new ManifestFileInvalidException(filePath, lineNum);
                            }
                            var directoryName = line[2..];
                            DirectoryNode newNode = new() { Name = directoryName };
                            directoryStack[^1].Subdirectories.Add(newNode);
                            directoryStack.Add(newNode);
                            break;
                        }
                    case BackupManifestFileCommands.POP_DIRECTORY: {
                            if (line.Length > 2) {
                                throw new ManifestFileInvalidException(filePath, lineNum);
                            }
                            if (directoryStack.Count == 1) {
                                throw new ManifestFileInvalidException(filePath, lineNum);
                            }
                            directoryStack.RemoveAt(directoryStack.Count - 1);
                            break;
                        }
                    case BackupManifestFileCommands.RECORD_FILE: {
                            if (line.Length <= 2) {
                                throw new ManifestFileInvalidException(filePath, lineNum);
                            }
                            var filename = line[2..];
                            directoryStack[^1].Files.Add(filename);
                            break;
                        }
                    default:
                        throw new ManifestFileInvalidException(filePath, lineNum);
                }
            }

            return manifest;
        }

        /// <summary>
        /// Opens a manifest file for reading.
        /// </summary>
        /// <param name="filePath">The path to the manifest file.</param>
        /// <returns>A new <see cref="StreamReader"/> associated with the file.</returns>
        /// <exception cref="ManifestFileNotFoundException">If the file path doesn't exist.</exception>
        /// <exception cref="ManifestFileIOException">If the file could not be opened for some other reason.</exception>
        private static StreamReader OpenFile(string filePath) {
            try {
                return new(filePath);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) {
                throw new ManifestFileNotFoundException(filePath, innerException: e);
            }
            catch (IOException e) {
                throw new ManifestFileIOException(filePath, innerException: e);
            }
        }
    }

    static class BackupManifestFileCommands
    {
        public const char PUSH_DIRECTORY = 'd';
        public const char POP_DIRECTORY = 'p';
        public const char RECORD_FILE = 'f';
    }

    /// <summary>
    /// Indicates a manifest file operation failed.
    /// </summary>
    abstract class ManifestFileException : ApplicationException
    {
        public ManifestFileException(string filePath, string? message = null, Exception? innerException = null) :
            base(message, innerException) {
            FilePath = filePath;
        }

        /// <summary>
        /// Path of the manifest file that was being accessed.
        /// </summary>
        public readonly string FilePath;
    }

    /// <summary>
    /// Indicates a manifest file could not be created.
    /// </summary>
    class ManifestFileCreateException : ManifestFileException
    {
        public ManifestFileCreateException(string filePath, string? message = null, Exception? innerException = null) :
            base(filePath, message, innerException) { }
    }

    /// <summary>
    /// Indicates a manifest file operation failed due to I/O errors.
    /// </summary>
    class ManifestFileIOException : ManifestFileException
    {
        public ManifestFileIOException(string filePath, string? message = null, Exception? innerException = null) :
            base(filePath, message, innerException) { }
    }

    /// <summary>
    /// Indicates a requested manifest file could not be found.
    /// </summary>
    class ManifestFileNotFoundException : ManifestFileException
    {
        public ManifestFileNotFoundException(string filePath, string? message = null, Exception? innerException = null) :
            base(filePath, message, innerException) { }
    }

    /// <summary>
    /// Indicates a manifest file could not be parsed because it is not in a valid format.
    /// </summary>
    class ManifestFileInvalidException : ManifestFileException
    {
        public ManifestFileInvalidException(string filePath, long line, string? message = null, Exception? innerException = null) :
            base(filePath, message, innerException) {
            Line = line;
        }

        /// <summary>
        /// 1-indexed number of the invalid line.
        /// </summary>
        public readonly long Line;
    }
}
