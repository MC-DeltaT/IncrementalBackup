using System;
using System.IO;
using System.Linq;


namespace IncrementalBackup
{
    /// <summary>
    /// Logs messages to the console and to file.
    /// </summary>
    class Logger : Disposable
    {
        public Logger(ConsoleLogHandler? consoleHandler, FileLogHandler? fileHandler) {
            ConsoleHandler = consoleHandler;
            FileHandler = fileHandler;
        }

        /// <summary>
        /// Handler for logging to the console. If <c>null</c>, logs will not be written to the console.
        /// </summary>
        public ConsoleLogHandler? ConsoleHandler;
        /// <summary>
        /// Handler for logging to file. If <c>null</c>, logs will not be written to file.
        /// </summary>
        public FileLogHandler? FileHandler;

        /// <summary>
        /// Logs a message with level <see cref="LogLevel.Info"/>.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void Info(string message) =>
            Log(LogLevel.Info, message);

        /// <summary>
        /// Logs a message with level <see cref="LogLevel.Warning"/>.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void Warning(string message) =>
            Log(LogLevel.Warning, message);

        /// <summary>
        /// Logs a message with level <see cref="LogLevel.Error"/>.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void Error(string message) =>
            Log(LogLevel.Error, message);

        /// <summary>
        /// Logs a message to the associated handlers, if present.
        /// </summary>
        /// <param name="level">The level of the log message.</param>
        /// <param name="message">The message to log.</param>
        public void Log(LogLevel level, string message) {
            LoggingException? fileException = null;
            try {
                FileHandler?.Log(level, message);
            }
            catch (LoggingException e) {
                fileException = e;
            }

            LoggingException? consoleException = null;
            try {
                ConsoleHandler?.Log(level, message);
            }
            catch (LoggingException e) {
                consoleException = e;
            }

            if (fileException is not null) {
                try {
                    ConsoleHandler?.Log(LogLevel.Warning, fileException.Message);
                }
                catch (LoggingException) { }
            }
            if (consoleException is not null) {
                try {
                    FileHandler?.Log(LogLevel.Warning, consoleException.Message);
                }
                catch (LoggingException) { }
            }
        }

        protected override void DisposeManaged() {
            FileHandler?.Dispose();
            base.DisposeManaged();
        }
    }

    /// <summary>
    /// Logs messages to <see cref="Console.Out"/> and <see cref="Console.Error"/>.
    /// </summary>
    class ConsoleLogHandler
    {
        /// <summary>
        /// Logs a message to the console.
        /// </summary>
        /// <param name="level">The level of the message. If <see cref="LogLevel.Error"/>, the message is logged
        /// to <see cref="Console.Error"/>. Otherwise, it is logged to <see cref="Console.Out"/>.</param>
        /// <param name="message">The message to log.</param>
        /// <exception cref="LoggingException">If the message could not be written to the console due to I/O errors.
        /// </exception>
        public void Log(LogLevel level, string message) {
            var consoleStream = level == LogLevel.Error ? Console.Error : Console.Out;
            try {
                consoleStream.Write(LogFormatter.FormatMessage(level, message));
                consoleStream.Flush();
            }
            catch (IOException e) {
                throw new LoggingException($"Failed to log to console: {e.Message}", e);
            }
        }
    }

    /// <summary>
    /// Logs messages to a file.
    /// </summary>
    class FileLogHandler : Disposable
    {
        /// <summary>
        /// Creates a handler that logs to a file at the given path. <br/>
        /// The file is created new or overwritten if it exists.
        /// </summary>
        /// <param name="path">The file to log messages to.</param>
        /// <exception cref="LoggingException">If the file could not be opened.</exception>
        public FileLogHandler(string path) {
            try {
                try {
                    Stream = File.CreateText(path);
                }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) {
                    throw new InvalidPathException(path);
                }
                catch (DirectoryNotFoundException) {
                    throw new PathNotFoundException(path);
                }
                catch (UnauthorizedAccessException) {
                    throw new PathAccessDeniedException(path);
                }
            }
            catch (FilesystemException e) {
                throw new LoggingException($"Failed to create/open log file \"{path}\": {e.Reason}", e);
            }
            FilePath = path;
        }

        /// <summary>
        /// The path of the file this handler writes to.
        /// </summary>
        public readonly string FilePath;

        /// <summary>
        /// Logs a message to the associated file.
        /// </summary>
        /// <param name="level">The level of the message.</param>
        /// <param name="message">The message to log.</param>
        /// <exception cref="LoggingException">If the message could not be written to the file due to I/O errors.
        /// </exception>
        public void Log(LogLevel level, string message) {
            try {
                Stream.Write(LogFormatter.FormatMessage(level, message));
                Stream.Flush();
            }
            catch (IOException e) {
                throw new LoggingException($"Failed to log to file \"{FilePath}\": {e.Message}",
                    new FilesystemException(FilePath, e.Message));
            }
        }

        protected override void DisposeManaged() {
            Stream.Dispose();
            base.DisposeManaged();
        }

        /// <summary>
        /// The stream which writes to the log file.
        /// </summary>
        private readonly StreamWriter Stream;
    }

    static class LogFormatter
    {
        /// <summary>
        /// Formats a log message. <br/>
        /// Messages which consist of multiple lines are split into multiple log messages.
        /// </summary>
        /// <param name="level">The level of the message.</param>
        /// <param name="message">The log message.</param>
        /// <returns>The formatted log message. Includes a trailing newline.</returns>
        public static string FormatMessage(LogLevel level, string message) =>
            string.Concat(
                message.Split(NEWLINES, StringSplitOptions.None)
                .Select(line => FormatLine(level, line) + Environment.NewLine));

        /// <summary>
        /// Formats a line of a log message.
        /// </summary>
        /// <param name="level">The severity level.</param>
        /// <param name="line">A line of the log message. Shouldn't contain newlines.</param>
        /// <returns>The formatted log line.</returns>
        private static string FormatLine(LogLevel level, string line) =>
            $"{level} | {line}";

        /// <summary>
        /// Array of newline character sequences used for splitting messages in
        /// <see cref="FormatMessage(LogLevel, string)"/>, cached for performance reasons.
        /// </summary>
        private static readonly string[] NEWLINES = new[] { "\r\n", "\r", "\n" };
    }

    /// <summary>
    /// Severity level for messages logged via <see cref="Logger"/>.
    /// </summary>
    enum LogLevel
    {
        /// <summary>
        /// Informational purposes only, everything is ok.
        /// </summary>
        Info,
        /// <summary>
        /// Something is not as expected, the user may want to know, but the program can continue fine.
        /// </summary>
        Warning,
        /// <summary>
        /// A critical error has occurred and the program cannot continue.
        /// </summary>
        Error
    }

    /// <summary>
    /// Thrown from log handlers to indicate an I/O error on the underlying medium.
    /// </summary>
    class LoggingException : Exception
    {
        public LoggingException(string message, Exception? innerException) :
            base(message, innerException) { }
    }
}
