using DMS.Models;
using System.IO;
using System.Text.Json;

namespace DMS.Helpers
{
    public static class AppSession
    {
        public static string? CurrentUserId { get; private set; }
        public static string? CurrentUsername { get; private set; }
        public static string? CurrentDisplayName { get; private set; }
        public static string CurrentRole { get; private set; } = "User";
        public static string? AccessToken { get; private set; }

        public static bool IsAdmin => string.Equals(CurrentRole, "Admin", StringComparison.OrdinalIgnoreCase);

        public static void SetCurrentUser(User user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            CurrentRole = "User";
            CurrentUserId = user.Id;
            CurrentUsername = user.Username ?? user.Email;
            CurrentDisplayName = user.Username ?? user.Email;
            Save();
        }

        public static void SetAccessToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Access token is required.", nameof(token));

            AccessToken = token;
            Save();
        }

        public static void SetAdmin(string name, string username, string? userId = null)
        {
            CurrentRole = "Admin";
            CurrentUserId = string.IsNullOrWhiteSpace(userId) ? username : userId;
            CurrentUsername = username;
            CurrentDisplayName = name;
            Save();
        }

        public static SessionSnapshot? Load()
        {
            try
            {
                if (!File.Exists(SessionFilePath))
                    return null;

                return JsonSerializer.Deserialize<SessionSnapshot>(File.ReadAllText(SessionFilePath));
            }
            catch
            {
                return null;
            }
        }

        public static void Clear()
        {
            CurrentUserId = null;
            CurrentUsername = null;
            CurrentDisplayName = null;
            CurrentRole = "User";
            AccessToken = null;

            try
            {
                if (File.Exists(SessionFilePath))
                    File.Delete(SessionFilePath);
            }
            catch
            {
            }
        }

        private static string SessionFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DMS",
            "session.json");

        private static void Save()
        {
            if (string.IsNullOrWhiteSpace(CurrentUserId))
                return;

            try
            {
                var directory = Path.GetDirectoryName(SessionFilePath);
                if (directory != null)
                    Directory.CreateDirectory(directory);

                var session = new SessionSnapshot(
                    CurrentUserId,
                    CurrentUsername,
                    CurrentDisplayName,
                    CurrentRole,
                    AccessToken);
                File.WriteAllText(SessionFilePath, JsonSerializer.Serialize(session));
            }
            catch
            {
            }
        }

        public sealed record SessionSnapshot(
            string? UserId,
            string? Username,
            string? DisplayName,
            string Role,
            string? AccessToken);
    }
}
