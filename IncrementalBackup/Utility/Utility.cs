using System;
using System.IO;
using System.Linq;
using System.Text;


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

        /// <summary>
        /// Checks if a string contains any newline characters (<c>'\n'</c> or <c>'\r'</c>).
        /// </summary>
        /// <returns><c>true</c> if the string contains newline characters, otherwise <c>false</c>.</returns>
        public static bool ContainsNewlines(string str) =>
            str.Any(c => c is '\n' or '\r');

        /// <summary>
        /// Encodes newlines in a string such that it becomes a single line. <br/>
        /// The result may be decoded with <see cref="NewlineDecode(string)"/>.
        /// </summary>
        /// <param name="str">The string to encode.</param>
        /// <returns>The encoded string.</returns>
        public static string NewlineEncode(string str) =>
            str.Replace(@"\", @"\\").Replace("\n", @"\n").Replace("\r", @"\r");

        /// <summary>
        /// Reverses the encoding performed by <see cref="NewlineEncode(string)"/>.
        /// </summary>
        /// <param name="encodedStr">The encoded string; the result of <see cref="NewlineEncode(string)"/>.</param>
        /// <returns>The original, decoded string.</returns>
        public static string NewlineDecode(string encodedStr) {
            StringBuilder result = new(encodedStr.Length);
            for (int i = 0; i < encodedStr.Length; ) {
                var cur = encodedStr[i];
                if (cur == '\\' && i + 1 < encodedStr.Length) {
                    var next = encodedStr[i + 1];
                    switch (next) {
                        case '\\':
                            result.Append('\\');
                            i += 2;
                            break;
                        case 'n':
                            result.Append('\n');
                            i += 2;
                            break;
                        case 'r':
                            result.Append('\r');
                            i += 2;
                            break;
                        default:
                            result.Append(cur);
                            i++;
                            break;
                    }
                }
                else {
                    result.Append(cur);
                    i++;
                }
            }
            return result.ToString();
        }
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
}
