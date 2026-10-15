namespace GithubLauncher.Services;

public static class LauncherPaths
{
    private static readonly Lazy<string> DataPath = new(CreateDataDirectory);
    public static string DataDirectory => DataPath.Value;

    private static string CreateDataDirectory()
    {
        if (!OperatingSystem.IsMacOS()) return AppDomain.CurrentDomain.BaseDirectory;
        var path = Environment.GetEnvironmentVariable("GITHUB_LAUNCHER_DATA_DIR");
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GithubLauncherMac");
        Directory.CreateDirectory(path);
        // Migrate existing portable configuration once, without replacing user data.
        foreach (var file in new[] { "settings.json", "apps.json", "games.json", "app_catalog_cache.json", "app_catalog_version.txt" })
        {
            var source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file);
            var target = Path.Combine(path, file);
            if (File.Exists(source) && !File.Exists(target)) File.Copy(source, target);
        }
        return path;
    }
}
