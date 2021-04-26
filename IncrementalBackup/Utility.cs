using System;
using System.Collections.Generic;
using System.IO;


namespace IncrementalBackup
{
    /// <summary>
    /// Miscellaneous static functionality.
    /// </summary>
    static class Utility
    {
        public static Random RandomEngine = new(unchecked((int)(7451L * DateTime.UtcNow.Ticks)));

        /// <summary>
        /// Creates a pseudorandom sequence of alphabetic and numeric characters of the given length. <br/>
        /// NOT cryptographically secure.
        /// </summary>
        /// <param name="length">The length of the random string.</param>
        /// <returns>The pseudorandom alphanumeric string.</returns>
        /// <exception cref="ArgumentException">If <paramref name="length"/> is &lt; 0.</exception>
        public static string RandomAlphaNumericString(int length) {
            if (length < 0) {
                throw new ArgumentException($"{nameof(length)} must be >= 0.", nameof(length));
            }

            const string characters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var result = new char[length];
            for (int i = 0; i < length; ++i) {
                result[i] = characters[RandomEngine.Next() % characters.Length];
            }
            return new(result);
        }

        /// <summary>
        /// Removes trailing directory separators from a path.
        /// </summary>
        /// <returns><paramref name="path"/> with trailing directory separators removed.</returns>
        public static string RemoveTrailingDirSep(string path) =>
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Basic implementation of <see cref="IDisposable"/> for convenience.
    /// Simply inherit from this class and override <see cref="DisposeManaged"/> and/or <see cref="DisposeUnmanaged"/>
    /// to implement resource disposal.
    /// </summary>
    abstract class Disposable : IDisposable
    {
        ~Disposable() =>
            Dispose(true);

        /// <summary>
        /// Disposes of all resources held by this object. <br/>
        /// After calling this method, the object probably should not be used.
        /// </summary>
        public void Dispose() {
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes of managed resources. Override this to dispose of your managed members. <br/>
        /// If you inherit from a class which inherits from <see cref="Disposable"/>, make sure to call the superclass's
        /// implementation too.
        /// </summary>
        protected virtual void DisposeManaged() { }

        /// <summary>
        /// Disposes of unmanaged resources. Override this to dispose of your unmanaged members. <br/>
        /// If you inherit from a class which inherits from <see cref="Disposable"/>, make sure to call the superclass's
        /// implementation too.
        /// </summary>
        protected virtual void DisposeUnmanaged() { }

        /// <summary>
        /// Disposes of resources held by this object, if not already disposed. <br/>
        /// </summary>
        /// <param name="inFinaliser">Indicates if we are currently in the finaliser. If <c>true</c>, then managed
        /// resources are not disposed of (because they will be disposed shortly anyway).</param>
        protected void Dispose(bool inFinaliser) {
            if (!Disposed) {
                if (!inFinaliser) {
                    DisposeManaged();
                }
                DisposeUnmanaged();
                Disposed = true;
            }
        }

        /// <summary>
        /// Indicates if resources have already been disposed of via <see cref="Dispose(bool)"/>.
        /// </summary>
        private bool Disposed = false;
    }

    static class StringExtensions
    {
        /// <summary>
        /// Checks if this string contains any newline characters (\n or \r).
        /// </summary>
        /// <returns><c>true</c> if the string contains newline characters, otherwise <c>false</c>.</returns>
        public static bool ContainsNewlines(this string str) {
            for (int i = 0; i < str.Length; i++) {
                if (str[i] is '\n' or '\r') {
                    return true;
                }
            }
            return false;
        }
    }

    static class ExceptionExtensions
    {
        /// <summary>
        /// Creates a detailed message by combining the messages from an exception and its nested exceptions.
        /// </summary>
        /// <param name="exception">The exception to create the detailed message from.</param>
        /// <returns>The detailed exception message, or an empty string if all messages are empty.</returns>
        public static string DetailedMessage(this Exception exception) {
            static IEnumerable<string> WalkMessages(Exception? exception) {
                while (exception != null) {
                    if (exception.Message != string.Empty) {
                        yield return exception.Message;
                    }
                    exception = exception.InnerException;
                }
            }

            return string.Join(": ", WalkMessages(exception));
        }
    }
}
