using System.Text.RegularExpressions;

namespace DMS.Helpers
{
    public static partial class SecurityValidator
    {
        private const string EmailPattern = "^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}$";
        private const string UsernamePattern = "^[A-Za-z0-9_]{3,20}$";

        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            return Regex.IsMatch(email.Trim(), EmailPattern, RegexOptions.IgnoreCase);
        }

        public static bool IsStrongPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return false;

            if (password.Length < 8)
                return false;

            bool hasUpper = Regex.IsMatch(password, "[A-Z]");
            bool hasLower = Regex.IsMatch(password, "[a-z]");
            bool hasDigit = Regex.IsMatch(password, "[0-9]");
            bool hasSymbol = Regex.IsMatch(password, "[^A-Za-z0-9]");

            return hasUpper && hasLower && hasDigit && hasSymbol;
        }

        public static bool IsValidUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            var trimmed = username.Trim();
            return trimmed.Length is >= 3 and <= 20 && Regex.IsMatch(trimmed, UsernamePattern);
        }

        public static string NormalizeEmail(string email) => email?.Trim() ?? string.Empty;

        public static string NormalizeUsername(string username) => username?.Trim() ?? string.Empty;
    }
}
