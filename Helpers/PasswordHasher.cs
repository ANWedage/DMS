using System;
using System.Security.Cryptography;

namespace DMS.Helpers
{
    /// <summary>
    /// Simple PBKDF2-based password hashing. Good enough for a local, single-user
    /// desktop app. Swap for a stronger scheme (e.g. BCrypt.Net) later if needed.
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltSize = 16;      // 128 bit
        private const int HashSize = 32;      // 256 bit
        private const int Iterations = 100_000;

        public static (string Hash, string Salt) HashPassword(string password)
        {
            byte[] saltBytes = RandomNumberGenerator.GetBytes(SaltSize);

            byte[] hashBytes = Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
        }

        public static bool Verify(string password, string storedHash, string storedSalt)
        {
            byte[] saltBytes = Convert.FromBase64String(storedSalt);

            byte[] hashBytes = Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            string computedHash = Convert.ToBase64String(hashBytes);
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(computedHash),
                Convert.FromBase64String(storedHash));
        }
    }
}
