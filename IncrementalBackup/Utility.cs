using System;


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
            return new string(result);
        }
    }
}
