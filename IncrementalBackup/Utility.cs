using System;
using System.IO;


namespace IncrementalBackup
{
    /// <summary>
    /// Miscellaneous static functionality.
    /// </summary>
    static class Utility
    {
        public static Random RandomEngine = new(unchecked((int)(13L * DateTime.UtcNow.Ticks)));

        /// <summary>
        /// Creates a pseudorandom sequence of alphabetic and numeric characters of the given length. <br/>
        /// NOT cryptographically secure.
        /// </summary>
        /// <param name="length">The length of the random string.</param>
        /// <returns>The pseudorandom alphanumeric string.</returns>
        /// <exception cref="ArgumentException">If <paramref name="length"/> is &lt; 0.</exception>
        public static string RandomAlphaNumericString(int length) {
            if (length < 0) {
                throw new ArgumentException("length must be >= 0.");
            }

            var characters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
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
        /// Checks if a path contains another path.
        /// </summary>
        /// <remarks>
        /// Does not interact with the filesystem, so doesn't handle weird stuff like symbolic links, etc.
        /// </remarks>
        /// <param name="path1">The first path. Should be normalised.</param>
        /// <param name="path2">The second path. Should be normalised.</param>
        /// <returns><c>true</c> if <paramref name="path2"/> is contained within <paramref name="path1"/>, or
        /// <paramref name="path1"/> == <paramref name="path2"/>, otherwise <c>false</c>.</returns>
        public static bool PathContainsPath(string path1, string path2) {
            if (path1.StartsWith(path2, StringComparison.InvariantCultureIgnoreCase)) {
                if (path1.Length == path2.Length) {
                    // Is this correct semantics???
                    return true;
                }
                else {
                    // path1.Length > path2.Length

                    // Take care of the cases like: path1="C:\foo" path2="C:\foobar"
                    var nonMatchingChar = path1[path2.Length];
                    return nonMatchingChar == Path.DirectorySeparatorChar || nonMatchingChar == Path.AltDirectorySeparatorChar;
                }
            }
            else {
                return false;
            }
        }
    }
}
