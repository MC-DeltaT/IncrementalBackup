using System;
using System.IO;
using System.Security;


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
        public FilesystemException(string? path, string message, string reason = "nonspecific filesystem error") :
            base(message, null) {
            Path = path;
            Reason = reason;
        }

        public readonly string? Path;

        public readonly string Reason;

        /// <summary>
        /// Calls a system filesystem function and converts commonly thrown system filesystem exceptions to instances
        /// of <see cref="FilesystemException"/> or subclasses. <br/>
        /// The default mapping of system exceptions to <see cref="FilesystemException"/> is as follows: <br/>
        /// <list type="bullet">
        /// <item><see cref="ArgumentException"/> to <see cref="InvalidPathException"/></item>
        /// <item><see cref="PathTooLongException"/> to <see cref="InvalidPathException"/></item>
        /// <item><see cref="NotSupportedException"/> to <see cref="InvalidPathException"/></item>
        /// <item><see cref="DirectoryNotFoundException"/> to <see cref="PathNotFoundException"/></item>
        /// <item><see cref="FileNotFoundException"/> to <see cref="PathNotFoundException"/></item>
        /// <item><see cref="UnauthorizedAccessException"/> to <see cref="PathAccessDeniedException"/></item>
        /// <item><see cref="SecurityException"/> to <see cref="PathAccessDeniedException"/></item>
        /// <item><see cref="IOException"/> to <see cref="FilesystemException"/></item>
        /// </list>
        /// </summary>
        /// <typeparam name="T">The return type of <paramref name="func"/>.</typeparam>
        /// <param name="func">The system filesystem function to call.</param>
        /// <param name="path">Gets the default value to use for <see cref="Path"/>.</param>
        /// <param name="argumentExceptionHandler">Specifies a custom exception mapping for
        /// <see cref="ArgumentException"/>.</param>
        /// <param name="pathTooLongExceptionHandler">Specifies a custom exception mapping for
        /// <see cref="PathTooLongException"/>.</param>
        /// <param name="notSupportedExceptionHandler">Specifies a custom exception mapping for
        /// <see cref="NotSupportedException"/>.</param>
        /// <param name="directoryNotFoundExceptionHandler">Specifies a custom exception mapping for
        /// <see cref="DirectoryNotFoundException"/>.</param>
        /// <param name="fileNotFoundExceptionHandler">Specifies a custom exception mapping for
        /// <see cref="FileNotFoundException"/>.</param>
        /// <param name="unauthorisedAccessExceptionHandler">Specifies a custom exception mapping for
        /// <see cref="UnauthorizedAccessException"/>.</param>
        /// <param name="securityExceptionHandler">Specifies a custom exception mapping for
        /// <see cref="SecurityException"/>.</param>
        /// <param name="ioExceptionHandler">Specifies a custom exception mapping for <see cref="IOException"/>.
        /// </param>
        /// <returns>The return value of <paramref name="func"/>.</returns>
        /// <exception cref="FilesystemException"/>
        public static T ConvertSystemException<T>(Func<T> func, Func<string> path,
                Func<ArgumentException, FilesystemException>? argumentExceptionHandler = null,
                Func<PathTooLongException, FilesystemException>? pathTooLongExceptionHandler = null,
                Func<NotSupportedException, FilesystemException>? notSupportedExceptionHandler = null,
                Func<DirectoryNotFoundException, FilesystemException>? directoryNotFoundExceptionHandler = null,
                Func<FileNotFoundException, FilesystemException>? fileNotFoundExceptionHandler = null,
                Func<UnauthorizedAccessException, FilesystemException>? unauthorisedAccessExceptionHandler = null,
                Func<SecurityException, FilesystemException>? securityExceptionHandler = null,
                Func<IOException, FilesystemException>? ioExceptionHandler = null) {
            try {
                return func();
            }
            catch (ArgumentException e) {
                if (argumentExceptionHandler is null) {
                    throw new InvalidPathException(path());
                }
                else {
                    throw argumentExceptionHandler(e);
                }
            }
            catch (PathTooLongException e) {
                if (pathTooLongExceptionHandler is null) {
                    throw new InvalidPathException(path());
                }
                else {
                    throw pathTooLongExceptionHandler(e);
                }
            }
            catch (NotSupportedException e) {
                if (notSupportedExceptionHandler is null) {
                    throw new InvalidPathException(path());
                }
                else {
                    throw notSupportedExceptionHandler(e);
                }
            }
            catch (DirectoryNotFoundException e) {
                if (directoryNotFoundExceptionHandler is null) {
                    throw new PathNotFoundException(path());
                }
                else {
                    throw directoryNotFoundExceptionHandler(e);
                }
            }
            catch (FileNotFoundException e) {
                if (fileNotFoundExceptionHandler is null) {
                    throw new PathNotFoundException(path());
                }
                else {
                    throw fileNotFoundExceptionHandler(e);
                }
            }
            catch (UnauthorizedAccessException e) {
                if (unauthorisedAccessExceptionHandler is null) {
                    throw new PathAccessDeniedException(path());
                }
                else {
                    throw unauthorisedAccessExceptionHandler(e);
                }
            }
            catch (SecurityException e) {
                if (securityExceptionHandler is null) {
                    throw new PathAccessDeniedException(path());
                }
                else {
                    throw securityExceptionHandler(e);
                }
            }
            catch (IOException e) {
                if (ioExceptionHandler is null) {
                    throw new FilesystemException(path(), e.Message);
                }
                else {
                    throw ioExceptionHandler(e);
                }
            }
        }

        /// <summary>
        /// Calls a system filesystem function and converts commonly thrown system filesystem exceptions to instances
        /// of <see cref="FilesystemException"/> or subclasses. <br/>
        /// The default mapping of system exceptions to <see cref="FilesystemException"/> is as follows: <br/>
        /// <list type="bullet">
        /// <item><see cref="ArgumentException"/> to <see cref="InvalidPathException"/></item>
        /// <item><see cref="PathTooLongException"/> to <see cref="InvalidPathException"/></item>
        /// <item><see cref="NotSupportedException"/> to <see cref="InvalidPathException"/></item>
        /// <item><see cref="DirectoryNotFoundException"/> to <see cref="PathNotFoundException"/></item>
        /// <item><see cref="FileNotFoundException"/> to <see cref="PathNotFoundException"/></item>
        /// <item><see cref="UnauthorizedAccessException"/> to <see cref="PathAccessDeniedException"/></item>
        /// <item><see cref="SecurityException"/> to <see cref="PathAccessDeniedException"/></item>
        /// <item><see cref="IOException"/> to <see cref="FilesystemException"/></item>
        /// </list>
        /// </summary>
        /// <typeparam name="T">The return type of <paramref name="func"/>.</typeparam>
        /// <param name="func">The system filesystem function to call.</param>
        /// <param name="path">Gets the default value to use for <see cref="Path"/>.</param>
        /// <param name="argumentExceptionHandler">Specifies a custom exception mapping for
        /// <see cref="ArgumentException"/>.</param>
        /// <param name="pathTooLongExceptionHandler">Specifies a custom exception mapping for
        /// <see cref="PathTooLongException"/>.</param>
        /// <param name="notSupportedExceptionHandler">Specifies a custom exception mapping for
        /// <see cref="NotSupportedException"/>.</param>
        /// <param name="directoryNotFoundExceptionHandler">Specifies a custom exception mapping for
        /// <see cref="DirectoryNotFoundException"/>.</param>
        /// <param name="fileNotFoundExceptionHandler">Specifies a custom exception mapping for
        /// <see cref="FileNotFoundException"/>.</param>
        /// <param name="unauthorisedAccessExceptionHandler">Specifies a custom exception mapping for
        /// <see cref="UnauthorizedAccessException"/>.</param>
        /// <param name="securityExceptionHandler">Specifies a custom exception mapping for
        /// <see cref="SecurityException"/>.</param>
        /// <param name="ioExceptionHandler">Specifies a custom exception mapping for <see cref="IOException"/>.
        /// </param>
        /// <exception cref="FilesystemException"/>
        public static void ConvertSystemException(Action func, Func<string> path,
                Func<ArgumentException, FilesystemException>? argumentExceptionHandler = null,
                Func<PathTooLongException, FilesystemException>? pathTooLongExceptionHandler = null,
                Func<NotSupportedException, FilesystemException>? notSupportedExceptionHandler = null,
                Func<DirectoryNotFoundException, FilesystemException>? directoryNotFoundExceptionHandler = null,
                Func<FileNotFoundException, FilesystemException>? fileNotFoundExceptionHandler = null,
                Func<UnauthorizedAccessException, FilesystemException>? unauthorisedAccessExceptionHandler = null,
                Func<SecurityException, FilesystemException>? securityExceptionHandler = null,
                Func<IOException, FilesystemException>? ioExceptionHandler = null) {
            ConvertSystemException(() => { func(); return 0; }, path,
                argumentExceptionHandler, pathTooLongExceptionHandler, notSupportedExceptionHandler,
                directoryNotFoundExceptionHandler, fileNotFoundExceptionHandler,
                unauthorisedAccessExceptionHandler, securityExceptionHandler,
                ioExceptionHandler);
        }
    }

    /// <summary>
    /// Indicates a path was not valid.
    /// </summary>
    class InvalidPathException : FilesystemException
    {
        public InvalidPathException(string path) :
            base(path, $"\"{path}\" is not a valid path", "invalid path") { }

        public new string Path =>
            base.Path!;
    }

    /// <summary>
    /// Indicates a path was not found.
    /// </summary>
    class PathNotFoundException : FilesystemException
    {
        public PathNotFoundException(string path) :
            base(path, $"Part of \"{path}\" not found", "part of path not found") { }

        public new string Path =>
            base.Path!;
    }

    /// <summary>
    /// Indicates access to a path was denied (either because the user or application does not have permission).
    /// </summary>
    class PathAccessDeniedException : FilesystemException
    {
        public PathAccessDeniedException(string path) :
            base(path, $"Access to \"{path}\" is denied", "access denied") { }

        public new string Path =>
            base.Path!;
    }

    /// <summary>
    /// Indicates there's a bug in the program.
    /// </summary>
    class LogicException : Exception
    {
        public LogicException(string message) :
            base(message) { }
    }
}
