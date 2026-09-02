using System.IO;

namespace DMS.Data
{
    /// <summary>
    /// MongoDB connection settings.
    /// Reads DMS_MONGO_CONNECTION_STRING from the local .env file when present,
    /// then falls back to OS environment variables and finally localhost for development.
    /// </summary>
    public static class MongoConfig
    {
        private const string LocalDevelopmentConnectionString = "mongodb://127.0.0.1:27017/DMS";
        private const string EnvFileName = ".env";

        public static string GetConnectionString(bool throwIfMissing = false)
        {
            var connectionString = ReadFromEnvFile();
            if (string.IsNullOrWhiteSpace(connectionString))
                connectionString = Environment.GetEnvironmentVariable("DMS_MONGO_CONNECTION_STRING");

            if (!string.IsNullOrWhiteSpace(connectionString))
                return connectionString.Trim();

            if (throwIfMissing)
            {
                throw new InvalidOperationException(
                    "DMS_MONGO_CONNECTION_STRING is not configured. Add it to a .env file beside DMS.exe or set it as an environment variable before starting the app.");
            }

            return LocalDevelopmentConnectionString;
        }

        public static string ConnectionString => GetConnectionString();

        public const string DatabaseName = "DMS";

        private static string ReadFromEnvFile()
        {
            try
            {
                var envFile = Path.Combine(AppContext.BaseDirectory, EnvFileName);
                var projectRoot = Directory.GetCurrentDirectory();

                var candidatePaths = new[]
                {
                    Path.Combine(projectRoot, EnvFileName),
                    Path.Combine(projectRoot, "..", EnvFileName),
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

                        var key = trimmed.Substring(0, index).Trim();
                        var value = trimmed.Substring(index + 1).Trim();

                        if (string.Equals(key, "DMS_MONGO_CONNECTION_STRING", StringComparison.OrdinalIgnoreCase))
                            return value.Trim('"', '\'');
                    }
                }
            }
            catch
            {
                // Ignore unreadable env files and fall back to environment variables / localhost.
            }

            return string.Empty;
        }
    }
}
