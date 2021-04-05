using System;
using System.IO;
using System.Linq;


namespace IncrementalBackup
{
    /// <summary>
    /// Logs messages to the console and to file.
    /// </summary>
    class Logger : IDisposable
    {
        public Logger(ConsoleLogHandler? consoleHandler = null, FileLogHandler? fileHandler = null) {
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

        ~Logger() =>
            Dispose(true);

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
            LoggerException? fileException = null;
            try {
                FileHandler?.Log(level, message);
            }
            catch (LoggerException e) {
                fileException = e;
            }
            LoggerException? consoleException = null;
            try {
                ConsoleHandler?.Log(level, message);
            }
            catch (LoggerException e) {
                consoleException = e;
            }
            if (fileException != null) {
                ConsoleHandler?.Log(LogLevel.Warning, $"Failed to write log to file: {fileException.Message}");
            }
            if (consoleException != null) {
                FileHandler?.Log(LogLevel.Warning, $"Failed to write log to console: {consoleException.Message}");
            }
        }

        /// <summary>
        /// Disposes of all resources held by this object. <br/>
        /// After calling this method, the object should not be used.
        /// </summary>
        public void Dispose() {
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes of resources held by this object, if not already disposed. <br/>
        /// </summary>
        /// <param name="finalising">Indicates if we are currently in the finaliser. If <c>true</c>, then managed
        /// resources are not disposed of (because they will be disposed shortly anyway).</param>
        protected virtual void Dispose(bool finalising) {
            if (!Disposed) {
                if (!finalising) {
                    FileHandler?.Dispose();
                }
                Disposed = true;
            }
        }

        /// <summary>
        /// Indicates if resources have already been disposed of via <see cref="Dispose(bool)"/>.
        /// </summary>
        private bool Disposed = false;
    }

    /// <summary>
    /// Logs messages to a file.
    /// </summary>
    class FileLogHandler : IDisposable
    {
        public FileLogHandler(string path) {
            try {
                Stream = File.CreateText(path);
            }
            catch (Exception e) when (e is ArgumentException || e is DirectoryNotFoundException || e is NotSupportedException
                || e is PathTooLongException || e is UnauthorizedAccessException) {
                throw new LoggerException(innerException: e);
            }
        }

        ~FileLogHandler() =>
            Dispose(true);

        /// <summary>
        /// Logs a message to the associated file.
        /// </summary>
        /// <param name="level">The level of the message.</param>
        /// <param name="message">The message to log.</param>
        /// <exception cref="LoggerException">If the message could not be written to the file due to I/O errors.</exception>
        public void Log(LogLevel level, string message) {
            try {
                Stream.Write(LogFormatter.FormatMessage(level, message));
                Stream.Flush();
            }
            catch (IOException e) {
                throw new LoggerException($"Failed to write to log file: {e.Message}", e);
            }
        }

        /// <summary>
        /// Disposes of all resources held by this object. <br/>
        /// After calling this method, the object should not be used.
        /// </summary>
        public void Dispose() {
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes of resources held by this object, if not already disposed. <br/>
        /// </summary>
        /// <param name="finalising">Indicates if we are currently in the finaliser. If <c>true</c>, then managed
        /// resources are not disposed of (because they will be disposed shortly anyway).</param>
        protected virtual void Dispose(bool finalising) {
            if (!Disposed) {
                if (!finalising) {
                    Stream.Dispose();
                }
                Disposed = true;
            }
        }

        /// <summary>
        /// The stream which writes to the log file.
        /// </summary>
        private readonly StreamWriter Stream;
        /// <summary>
        /// Indicates if resources have already been disposed of via <see cref="Dispose(bool)"/>.
        /// </summary>
        private bool Disposed = false;
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
        /// <exception cref="LoggerException">If the message could not be written to the console due to I/O errors.</exception>
        public void Log(LogLevel level, string message) {
            var consoleStream = level == LogLevel.Error ? Console.Error : Console.Out;
            try {
                consoleStream.Write(LogFormatter.FormatMessage(level, message));
            }
            catch (IOException e) {
                throw new LoggerException($"Failed to write to console: {e.Message}", e);
            }
        }
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
                message.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                .Select(line => FormatLine(level, line) + Environment.NewLine));

        /// <summary>
        /// Formats a line of a log message.
        /// </summary>
        /// <param name="level">The severity level.</param>
        /// <param name="line">A line of the log message. Shouldn't contain newlines.</param>
        /// <returns>The formatted log line.</returns>
        private static string FormatLine(LogLevel level, string line) =>
            $"{level}: {line}";
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
    /// Thrown from logging functionality.
    /// </summary>
    class LoggerException : ApplicationException
    {
        public LoggerException(string? message = null, Exception? innerException = null) : base(message, innerException) { }
    }
}
