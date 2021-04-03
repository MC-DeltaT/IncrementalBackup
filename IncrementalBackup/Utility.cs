using System;


namespace IncrementalBackup
{
    static class Utility
    {
        public static Random RandomEngine = new(unchecked((int)(13L * DateTime.UtcNow.Ticks)));

        public static string RandomAlphaNumericString(int length) {
            var characters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var result = new char[length];
            for (int i = 0; i < length; ++i) {
                result[i] = characters[RandomEngine.Next() % characters.Length];
            }
            return new string(result);
        }
    }
}
