using System;


namespace IncrementalBackup
{
    /// <summary>
    /// Indicates a general filesystem-related error.
    /// </summary>
    /// <remarks>
    /// Used to wrap exceptions thrown from system file I/O functionality, because there too many different exception
    /// types for us to handle easily at all levels of the application.
    /// </remarks>
    class FilesystemException : Exception
    {
        public FilesystemException(string path, string message = "Unspecified filesystem error") :
            base(message, null) {
            Path = path;
        }

        public readonly string Path;
    }

    /// <summary>
    /// Indicates a path was not valid.
    /// </summary>
    class InvalidPathException : FilesystemException
    {
        public InvalidPathException(string path) :
            base(path, $"\"{path}\" is not a valid path") { }
    }

    /// <summary>
    /// Indicates a path was not found.
    /// </summary>
    class PathNotFoundException : FilesystemException
    {
        public PathNotFoundException(string path) :
            base(path, $"Part of \"{path}\" not found") { }
    }

    /// <summary>
    /// Indicates access to a path was denied (either because the user or application does not have permission).
    /// </summary>
    class PathAccessDeniedException : FilesystemException
    {
        public PathAccessDeniedException(string path) :
            base(path, $"Access to \"{path}\" is denied") { }
    }
}
