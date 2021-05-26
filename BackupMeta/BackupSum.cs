using System;
using System.Collections.Generic;
using System.Linq;


namespace IncrementalBackup
{
    /// <summary>
    /// The "sum" of a set of previous backup manifests, that is, the result of applying each backup in sequence.
    /// </summary>
    public class BackupSum
    {
        /// <summary>
        /// Constructs backup sum from a set of backups. <br/>
        /// The backups are assumed to be for the same source directory (otherwise the results won't be useful).
        /// </summary>
        /// <param name="backups">The backups to construct the backup sum from. They should all have the same source
        /// directory.</param>
        public BackupSum(IReadOnlyList<BackupMetadata> backups) {
            Root = new("source", new(), new());

            // TODO? Can probably do this more efficiently by going over backups in reverse?

            var backupsSorted = backups.OrderBy(p => p.StartInfo.StartTime);
            foreach (var backup in backupsSorted) {
                // Do a depth-first search of the backup's manifest while tracking it in this object to merge the
                // structure in.

                List<BackupManifest.Directory?> searchStack = new() { backup.Manifest.Root };
                List<Directory> nodeStack = new() { Root };
                bool isRoot = true;
                do {
                    var currentDirectory = searchStack[^1];
                    searchStack.RemoveAt(searchStack.Count - 1);

                    // null marks backtrack to parent directory.
                    if (currentDirectory is null) {
                        nodeStack.RemoveAt(nodeStack.Count - 1);
                    }
                    else {
                        if (!isRoot) {
                            var node = nodeStack[^1].Subdirectories.Find(
                                d => Utility.PathEqual(d.Name, currentDirectory.Name));
                            if (node is null) {
                                node = new Directory(currentDirectory.Name, new(), new());
                                nodeStack[^1].Subdirectories.Add(node);
                            }
                            nodeStack.Add(node);
                        }

                        List<BackupManifest.Directory> subdirectories = new();
                        foreach (var entry in currentDirectory.Entries) {
                            switch (entry) {
                                case BackupManifest.Directory dir:
                                    subdirectories.Add(dir);
                                    break;
                                case BackupManifest.BackedUpFile file: {
                                        var prevFile = nodeStack[^1].Files.Find(
                                            f => Utility.PathEqual(f.Name, file.Name));
                                        if (prevFile is null) {
                                            nodeStack[^1].Files.Add(new(file.Name, backup));
                                        }
                                        else {
                                            prevFile.LastBackup = backup;
                                        }
                                        break;
                                    }
                                case BackupManifest.RemovedFile removedFile:
                                    nodeStack[^1].Files.RemoveAll(f => Utility.PathEqual(f.Name, removedFile.Name));
                                    break;
                                case BackupManifest.RemovedDirectory removedDir:
                                    nodeStack[^1].Subdirectories.RemoveAll(
                                        d => Utility.PathEqual(d.Name, removedDir.Name));
                                    break;
                            }
                        }

                        if (!isRoot) {
                            searchStack.Add(null);
                        }

                        subdirectories.Reverse();
                        searchStack.AddRange(subdirectories);
                    }

                    isRoot = false;
                } while (searchStack.Count > 0);
            }
        }

        /// <summary>
        /// The summed backup manifest. <br/>
        /// The root is the backup source directory.
        /// </summary>
        public Directory Root;

        /// <summary>
        /// Finds a directory within the summed backup manifest.
        /// </summary>
        /// <param name="path">The path components of the directory, relative to the backup source directory.</param>
        /// <returns>The requested directory, or <c>null</c> if it was not found.</returns>
        public Directory? FindDirectory(IEnumerable<string> path) {
            Directory? entry = Root;
            foreach (var directoryName in path) {
                entry = entry.Subdirectories.Find(d => Utility.PathEqual(d.Name, directoryName));
                if (entry is null) {
                    break;
                }
            }
            return entry;
        }

        public class Directory
        {
            public Directory(string name, List<File> files, List<Directory> subdirectories) {
                Name = name;
                Files = files;
                Subdirectories = subdirectories;
            }

            /// <summary>
            /// The name of the directory.
            /// </summary>
            public string Name;
            /// <summary>
            /// Files directly contained in this directory.
            /// </summary>
            public List<File> Files;
            /// <summary>
            /// Directories directly contained in this directory.
            /// </summary>
            public List<Directory> Subdirectories;
        }

        public class File
        {
            public File(string name, BackupMetadata lastBackup) {
                Name = name;
                LastBackup = lastBackup;
            }

            /// <summary>
            /// The name of the file.
            /// </summary>
            public string Name;
            /// <summary>
            /// The last backup that backed up this file.
            /// </summary>
            public BackupMetadata LastBackup;
        }
    }
}
