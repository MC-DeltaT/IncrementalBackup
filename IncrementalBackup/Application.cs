using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;


namespace IncrementalBackup
{
    class Application
    {
        static int Main(string[] args) {
            try {
                BackupConfig? config = ParseArgs(args);
                if (config == null) {
                    return 2;
                }

                DisplayConfig(config);

                var index = ReadBackupIndex(config.TargetDirectory);

                var backupDirectory = CreateBackupDirectory(config.TargetDirectory);

                return 0;
            }
            catch (CriticalError e) {
                Console.Error.WriteLine(e.Message);
                return 3;
            }
            catch (Exception e) {
                Console.Error.WriteLine($"Unhandled error: {e}");
                return 4;
            }
        }

        private static BackupConfig? ParseArgs(string[] args) {
            if (args.Length < 2) {
                Console.Out.WriteLine("Usage: IncrementalBackup.exe <source_dir> <target_dir> [exclude_path1 exclude_path2 ...]");
                return null;
            }

            var sourceDirectory = args[0];
            var targetDirectory = args[1];
            var excludePaths = new List<string>(args.Skip(2));

            var validArgs = true;

            try {
                sourceDirectory = Path.GetFullPath(sourceDirectory);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) {
                Console.Error.WriteLine("Error: source directory is not a valid path.");
                validArgs = false;
            }
            catch (SecurityException) {
                Console.Error.WriteLine("Error: access to source directory is denied.");
                validArgs = false;
            }

            try {
                targetDirectory = Path.GetFullPath(targetDirectory);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) {
                Console.Error.WriteLine("Error: target directory is not a valid path.");
                validArgs = false;
            }
            catch (SecurityException) {
                Console.Error.WriteLine("Error: access to target directory is denied.");
                validArgs = false;
            }

            for (int i = 0; i < excludePaths.Count; ++i) {
                try {
                    excludePaths[i] = Path.GetFullPath(excludePaths[i], sourceDirectory);
                }
                catch (ArgumentException) {
                    Console.Error.WriteLine($"Error: invalid exclude path \"{excludePaths[i]}\".");
                    validArgs = false;
                }
            }
            
            if (validArgs) {
                sourceDirectory = sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                targetDirectory = targetDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return new BackupConfig(sourceDirectory, targetDirectory, excludePaths);
            }
            else {
                return null;
            }
        }

        private static void DisplayConfig(BackupConfig config) {
            Console.Out.WriteLine($"Source directory: {config.SourceDirectory}");
            Console.Out.WriteLine($"Target directory: {config.TargetDirectory}");
            Console.Out.WriteLine("Exclude paths:");
            if (config.ExcludePaths.Count == 0) {
                Console.Out.WriteLine("\t<none>");
            }
            else {
                foreach (var path in config.ExcludePaths) {
                    Console.Out.WriteLine($"\t{path}");
                }
            }
            Console.Out.WriteLine();
        }

        private static BackupIndex? ReadBackupIndex(string targetDirectory) {
            try {
                return Metastructure.ReadBackupIndex(targetDirectory);
            }
            catch (IndexFileNotFoundException) {
                Console.Out.WriteLine("No existing backup index found.");
                return null;
            }
            catch (IndexFileException e) {
                throw new CriticalError($"Failed to read existing index file: {e.Message}", e);
            }
        }

        private static DirectoryInfo CreateBackupDirectory(string targetDirectory) {
            try {
                return Metastructure.CreateBackupDirectory(targetDirectory);
            }
            catch (CreateBackupDirectoryException e) {
                throw new CriticalError("Failed to create new backup directory.", e);
            }
        }

        private static BackupManifest BackUp(BackupConfig config, BackupManifest? lastManifest) {
            var newManifest = new BackupManifest() { BeginTime = DateTime.UtcNow };

            // Explore directories in a depth-first manner, on the basis that files/folders within the same
            // branch of the filesystem are likely to be modified together, so we want to back them up as
            // close together in time as possible.
            // Using explicit stack to avoid recursion errors.
            var directoryStack = new List<DirectoryInfo> {
                new DirectoryInfo(config.SourceDirectory)       // TODO: exception handling
            };
            while (directoryStack.Count > 0) {
                var currentDirectory = directoryStack.Last();
                directoryStack.RemoveAt(directoryStack.Count - 1);

                if (IsPathExcluded(currentDirectory.FullName, config.ExcludePaths)) {
                    newManifest.AddSkippedPath(currentDirectory.FullName, true, SkipReason.Excluded);
                }
                else {
                    BackUpDirectoryFiles(currentDirectory, config, lastManifest, newManifest);
                    directoryStack.AddRange(currentDirectory.GetDirectories());       // TODO: exception handling
                }
            }

            newManifest.EndTime = DateTime.UtcNow;
            return newManifest;
        }

        private static void BackUpDirectoryFiles(DirectoryInfo directory, BackupConfig config, BackupManifest? lastManifest,
                BackupManifest newManifest) {
            // TODO: create directory in backup 

            var files = directory.GetFiles();       // TODO: exception handling
            foreach (var file in files) {
                var path = file.FullName;
                if (IsPathExcluded(path, config.ExcludePaths)) {
                    newManifest.AddSkippedPath(path, false, SkipReason.Excluded);
                }
                else if (lastManifest == null || file.LastWriteTimeUtc >= lastManifest.BeginTime) {
                    BackUpFile(file, newManifest);
                }
            }
        }

        private static void BackUpFile(FileInfo file, BackupManifest newManifest) {
            // TODO
        }

        private static bool IsPathExcluded(string path, IReadOnlyCollection<string> excludePaths) {
            // path must be normalised.

            // Case-insensitivity means this only works for Windows.
            return excludePaths.Any(p => string.Compare(path, p, StringComparison.InvariantCultureIgnoreCase) == 0);
        }
    }

    record BackupConfig(
        string SourceDirectory,
        string TargetDirectory,
        IReadOnlyList<string> ExcludePaths      // Should be normalised.
    );

    class CriticalError : ApplicationException
    {
        public CriticalError(string? message = null, Exception? innerException = null) : base(message, innerException) {}
    }
}
