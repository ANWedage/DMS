using System.IO;

namespace DMS.Data
{
    public static class AdminConfig
    {
        public static List<(string Name, string Username, string Password)> GetConfiguredAdmins()
        {
            var admins = new List<(string Name, string Username, string Password)>();
            var index = 1;

            while (true)
            {
                var name = ReadFromEnv($"DMS_ADMIN_{index}_NAME");
                var username = ReadFromEnv($"DMS_ADMIN_{index}_USERNAME");
                var password = ReadFromEnv($"DMS_ADMIN_{index}_PASSWORD");

                if (string.IsNullOrWhiteSpace(name) &&
                    string.IsNullOrWhiteSpace(username) &&
                    string.IsNullOrWhiteSpace(password))
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    throw new InvalidOperationException($"Admin configuration {index} is incomplete. Provide DMS_ADMIN_{index}_NAME, DMS_ADMIN_{index}_USERNAME, and DMS_ADMIN_{index}_PASSWORD.");
                }

                admins.Add((name.Trim(), username.Trim(), password.Trim()));
                index++;
            }

            if (admins.Count == 0)
            {
                throw new InvalidOperationException("At least one admin must be configured in the environment. Example: DMS_ADMIN_1_NAME, DMS_ADMIN_1_USERNAME, DMS_ADMIN_1_PASSWORD.");
            }

            return admins;
        }

        private static string ReadFromEnv(string key)
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
                return fromEnvironment.Trim();

            try
            {
                var envFile = Path.Combine(AppContext.BaseDirectory, ".env");
                var projectRoot = Directory.GetCurrentDirectory();

                var candidatePaths = new[]
                {
                    Path.Combine(projectRoot, ".env"),
                    Path.Combine(projectRoot, "..", ".env"),
                    envFile
                };

                foreach (var path in candidatePaths)
                {
                    if (!File.Exists(path)) continue;

                    foreach (var line in File.ReadAllLines(path))
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

                        var index = trimmed.IndexOf('=');
                        if (index <= 0) continue;

                        var envKey = trimmed.Substring(0, index).Trim();
                        var envValue = trimmed.Substring(index + 1).Trim();

                        if (string.Equals(envKey, key, StringComparison.OrdinalIgnoreCase))
                            return envValue.Trim('"', '\'');
                    }
                }
            }
            catch
            {
                // Ignore unreadable env config and fall back to empty values.
            }

            return string.Empty;
        }
    }
}
