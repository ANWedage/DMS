using DMS.Models;

namespace DMS.Helpers
{
    public static class AppSession
    {
        public static string? CurrentUserId { get; private set; }
        public static string? CurrentUsername { get; private set; }
        public static string? CurrentDisplayName { get; private set; }
        public static string CurrentRole { get; private set; } = "User";

        public static bool IsAdmin => string.Equals(CurrentRole, "Admin", StringComparison.OrdinalIgnoreCase);

        public static void SetCurrentUser(User user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            CurrentRole = "User";
            CurrentUserId = user.Id;
            CurrentUsername = user.Username ?? user.Email;
            CurrentDisplayName = user.Username ?? user.Email;
        }

        public static void SetAdmin(string name, string username)
        {
            CurrentRole = "Admin";
            CurrentUserId = username;
            CurrentUsername = username;
            CurrentDisplayName = name;
        }

        public static void Clear()
        {
            CurrentUserId = null;
            CurrentUsername = null;
            CurrentDisplayName = null;
            CurrentRole = "User";
        }
    }
}
