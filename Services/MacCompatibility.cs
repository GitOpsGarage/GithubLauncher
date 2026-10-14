using System.Diagnostics;
using System.Text.Json;
using GitHubLauncher.Core.Models;
using GitHubLauncher.Core.Services;

namespace GithubLauncher.Services;

public sealed class MacCompatibilityEntry
{
    public string Mode { get; set; } = "native";
    public string Release { get; set; } = "";
    public string AssetName { get; set; } = "";
    public bool Verified { get; set; }
    public string Evidence { get; set; } = "";
    public string Requirements { get; set; } = "";
    public string Bottle { get; set; } = "";
    public DateTimeOffset CheckedAt { get; set; }
    public List<MacGameplayReport> Reports { get; set; } = [];
    public List<string> AvailableAssets { get; set; } = [];
}

public static class MacCompatibility
{
    public static Dictionary<string, MacCompatibilityEntry> Load()
    {
        var result = new Dictionary<string, MacCompatibilityEntry>(StringComparer.OrdinalIgnoreCase);
        var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mac-compatibility.json");
        var custom = Path.Combine(LauncherPaths.DataDirectory, "mac-compatibility.json");
        var discovered = Path.Combine(LauncherPaths.DataDirectory, "mac-discovery.json");
        foreach (var path in new[] { bundled, discovered, custom }.Distinct())
        {
            if (!File.Exists(path)) continue;
            try
            {
                var entries = JsonSerializer.Deserialize<Dictionary<string, MacCompatibilityEntry>>(
                    File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                foreach (var pair in entries ?? [])
                {
                    if (path == discovered && result.TryGetValue(pair.Key, out var newer) && newer.CheckedAt > pair.Value.CheckedAt)
                        continue;
                    result[pair.Key] = pair.Value;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Mac compatibility catalog unavailable: {ex.Message}");
            }
        }
        try
        {
            foreach (var (repository, reports) in MacGameplay.Load())
            {
                if (reports == null) continue;
                if (!result.TryGetValue(repository, out var entry)) result[repository] = entry = new() { Mode = "unknown" };
                entry.Reports ??= [];
                entry.Reports.AddRange(reports);
                entry.Reports = entry.Reports.DistinctBy(r => (r.Runner, r.Release, r.AssetName, r.Environment, r.RuntimeId, r.PlatformFingerprint, r.TestedAt)).ToList();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { Debug.WriteLine($"Gameplay reports unavailable: {ex.Message}"); }
        return result;
    }

    public static MacCompatibilityEntry? Find(string? repository) =>
        repository != null && Load().TryGetValue(repository, out var entry) ? entry : null;

    public static bool IsSupportedArchive(string name) =>
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase);

    public static List<GitHubAsset> FilterAssets(GitHubRelease release, MacCompatibilityEntry? entry, bool allowTesting = false, string runner = "crossover", string? runtimeId = null, bool preferWindows = false)
    {
        var assets = GitHubReleaseService.GetDownloadableAssets(release);
        var native = assets.Where(a => IsSupportedArchive(a.name) &&
            PlatformAssetMatcher.MatchesPlatform(a.name, "macOS")).ToList();

        // Prefer Apple Silicon builds; retain universal/unspecified builds.
        bool hasArm = native.Any(a => IsArm(a.name) || IsUniversal(a.name));
        if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
            System.Runtime.InteropServices.Architecture.Arm64 && hasArm)
            native.RemoveAll(a => IsIntel(a.name) && !IsArm(a.name) && !IsUniversal(a.name));
        else if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
            System.Runtime.InteropServices.Architecture.X64)
            native.RemoveAll(a => IsArm(a.name) && !IsIntel(a.name) && !IsUniversal(a.name));

        if (native.Count > 0 && !preferWindows)
            return native.OrderBy(a => IsDebug(a.name)).ThenByDescending(a => IsArm(a.name) || IsUniversal(a.name))
                .ThenBy(a => a.name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase)).ToList();

        if (allowTesting)
            return assets.Where(a => (IsSupportedArchive(a.name) || a.name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) &&
                PlatformAssetMatcher.MatchesPlatform(a.name, "Windows") && !IsArm(a.name)).ToList();

        return assets.Where(a => MacGameplay.IsVerified(entry, release.tag_name, a.name, runner, runtimeId) &&
            (IsSupportedArchive(a.name) || a.name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) &&
            PlatformAssetMatcher.IsWindowsAsset(a.name)).ToList();
    }

    private static bool IsArm(string name) =>
        PlatformAssetMatcher.HasAnyOf(name.ToLowerInvariant(), "arm64", "aarch64", "_arm", "-arm", "applesilicon", "apple-silicon", "apple_silicon");

    private static bool IsUniversal(string name) => name.Contains("universal", StringComparison.OrdinalIgnoreCase);
    private static bool IsDebug(string name) => PlatformAssetMatcher.HasAnyOf(name.ToLowerInvariant(), "debug", "-dbg", "symbols");

    public static MacCompatibilityEntry FromRelease(string repository, GitHubRelease release)
    {
        var native = FilterAssets(release, null).FirstOrDefault();
        var candidate = native == null ? FilterAssets(release, null, true).FirstOrDefault() : null;
        return new()
        {
            Mode = native != null ? "native" : candidate != null ? "candidate" : "unknown",
            Release = release.tag_name, AssetName = (native ?? candidate)?.name ?? "",
            AvailableAssets = release.assets?.Select(a => a.name).ToList() ?? [],
            Evidence = $"https://github.com/{repository}/releases/tag/{Uri.EscapeDataString(release.tag_name)}",
            CheckedAt = DateTimeOffset.UtcNow,
            Requirements = "Follow this project's setup guide and supply its required original game data. Gameplay has not been tested."
        };
    }

    private static bool IsIntel(string name) =>
        PlatformAssetMatcher.HasAnyOf(name.ToLowerInvariant(), "intel", "x64", "x86_64", "amd64");

    public static bool IsVerifiedCrossOver(MacCompatibilityEntry? entry, string? version) =>
        entry != null && entry.Release == version && MacGameplay.IsVerified(entry, version, entry.AssetName, "crossover");

    public static bool IsCatalogEligible(MacCompatibilityEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Evidence) &&
        (entry.Mode.Equals("native", StringComparison.OrdinalIgnoreCase)
            ? IsSupportedArchive(entry.AssetName) && PlatformAssetMatcher.MatchesPlatform(entry.AssetName, "macOS")
            : IsVerifiedCrossOver(entry, entry.Release));

    public static string CatalogCategory(MacCompatibilityEntry? entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Evidence)) return "unknown";
        if (entry.Mode.Equals("native", StringComparison.OrdinalIgnoreCase) && IsCatalogEligible(entry)) return "native";
        if (MacGameplay.Current(entry).Any(r => r.IsPlayable)) return "tested";
        if ((entry.Mode.Equals("candidate", StringComparison.OrdinalIgnoreCase) || entry.Mode.Equals("crossover", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(entry.Release) && PlatformAssetMatcher.MatchesPlatform(entry.AssetName, "Windows") &&
            (IsSupportedArchive(entry.AssetName) || entry.AssetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) return "candidate";
        return "unknown";
    }

    public static ProcessStartInfo CrossOverStartInfo(string appPath, string bottle, string executable, string gamePath)
    {
        var wine = Path.Combine(appPath, "Contents", "SharedSupport", "CrossOver", "bin", "wine");
        if (!File.Exists(wine))
            throw new FileNotFoundException("CrossOver not found. Set its app path in Settings.", wine);
        if (string.IsNullOrWhiteSpace(bottle) || Path.GetFileName(bottle) != bottle || bottle is "." or "..")
            throw new InvalidOperationException("Set an existing CrossOver bottle name in Settings or mac-compatibility.json.");
        var bottlePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "CrossOver", "Bottles", bottle);
        if (!Directory.Exists(bottlePath))
            throw new DirectoryNotFoundException($"CrossOver bottle '{bottle}' does not exist. Create it in CrossOver first.");
        var startInfo = new ProcessStartInfo(wine) { UseShellExecute = false, WorkingDirectory = gamePath };
        foreach (var argument in new[] { "--bottle", bottle, "--workdir", Path.GetDirectoryName(executable) ?? gamePath,
            "--cx-app", executable })
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    public static List<string> FindExecutables(string path, SearchOption searchOption, out bool needsWine)
    {
        needsWine = false;
        if (!Directory.Exists(path)) return [];
        var bundles = Directory.GetDirectories(path, "*.app", searchOption)
            .Where(bundle => !Path.GetRelativePath(path, bundle).Split(Path.DirectorySeparatorChar)
                .SkipLast(1).Any(part => part.EndsWith(".app", StringComparison.OrdinalIgnoreCase))).ToList();
        // Do not offer internal helpers from inside an app bundle as separate games.
        if (bundles.Count > 0) return bundles;
        var files = Directory.GetFiles(path, "*", searchOption);
        var scripts = files.Where(IsLaunchScript).OrderBy(f => Path.GetRelativePath(path, f).Count(c => c == Path.DirectorySeparatorChar));
        var native = scripts.Concat(files.Where(IsMachExecutable)).ToList();
        if (native.Count > 0) return native;
        var windows = files.Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
        needsWine = windows.Count > 0;
        return windows;
    }

    private static bool IsLaunchScript(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (new[] { "install", "setup", "uninstall", "update" }.Any(name.StartsWith)) return false;
        try
        {
            using var stream = File.OpenRead(path);
            return stream.ReadByte() == '#' && stream.ReadByte() == '!';
        }
        catch (IOException) { return false; }
    }

    private static bool IsMachExecutable(string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is ".dylib" or ".so" or ".a" or ".dll") return false;
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[16];
            if (stream.Read(header) != header.Length) return false;
            uint magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header);
            // Mach-O MH_EXECUTE; reject shared libraries and non-executable fat binaries.
            if (magic is 0xFEEDFACE or 0xFEEDFACF)
                return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header[12..]) == 2;
            if (magic is not (0xBEBAFECA or 0xBFBAFECA)) return false;
            int count = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[4..]);
            if (count is < 1 or > 64) return false;
            bool fat64 = magic == 0xBFBAFECA;
            Span<byte> offsetBytes = stackalloc byte[8];
            for (int i = 0; i < count; i++)
            {
                stream.Position = 8 + i * (fat64 ? 32 : 20) + 8;
                int length = fat64 ? 8 : 4;
                if (stream.Read(offsetBytes[..length]) != length) return false;
                ulong offset = fat64 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(offsetBytes) :
                    System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(offsetBytes);
                if (offset > (ulong)Math.Max(0, stream.Length - 16)) continue;
                stream.Position = (long)offset;
                if (stream.Read(header) == 16 &&
                    System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header) is 0xFEEDFACE or 0xFEEDFACF &&
                    System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header[12..]) == 2) return true;
            }
            return false;
        }
        catch (IOException) { return false; }
    }
}
