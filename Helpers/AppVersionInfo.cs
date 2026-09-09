using System.Reflection;

namespace DMS.Helpers
{
    public static class AppVersionInfo
    {
        public static string CurrentVersion => GetCurrentVersion();

        public static string CurrentVersionDisplayText => $"Version {CurrentVersion}";

        private static string GetCurrentVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
                return informationalVersion.Split('+')[0];

            var fileVersion = assembly
                .GetCustomAttribute<AssemblyFileVersionAttribute>()?
                .Version;

            if (!string.IsNullOrWhiteSpace(fileVersion))
                return fileVersion;

            var assemblyVersion = assembly.GetName().Version?.ToString();
            return !string.IsNullOrWhiteSpace(assemblyVersion)
                ? assemblyVersion
                : "1.0.0";
        }
    }
}
