using System.Text.Json;

namespace GithubLauncher.Services;

public sealed class MacGameplayReport
{
    public string Runner { get; set; } = "native";
    public string Release { get; set; } = "";
    public string AssetName { get; set; } = "";
    public string Result { get; set; } = "unknown";
    public string Environment { get; set; } = "";
    public string Evidence { get; set; } = "";
    public DateTimeOffset TestedAt { get; set; }
    public string PlatformFingerprint { get; set; } = "";
    public string RuntimeId { get; set; } = "";

    public bool IsComplete => Runner is "native" or "crossover" or "notproton" &&
        Result is "playable" or "adjustments" or "unsupported" &&
        !string.IsNullOrWhiteSpace(Release) && !string.IsNullOrWhiteSpace(AssetName) &&
        !string.IsNullOrWhiteSpace(Environment) && !string.IsNullOrWhiteSpace(Evidence) &&
        !string.IsNullOrWhiteSpace(PlatformFingerprint) && !string.IsNullOrWhiteSpace(RuntimeId) && TestedAt != default;
    public bool IsPlayable => IsComplete && Result is "playable" or "adjustments";
    public string Label => Result switch
    {
        "playable" => "Playable",
        "adjustments" => "Playable with adjustments",
        "unsupported" => "Unsupported",
        _ => "Untested"
    };
    public string RunnerLabel => Runner switch { "native" => "Native Mac", "notproton" => "Steam / NotProton", _ => "Direct CrossOver" };
}

public static class MacGameplay
{
    public static string PlatformFingerprint => $"{System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}|{System.Runtime.InteropServices.RuntimeInformation.OSDescription}";
    public static string RuntimeIdentity(string runner, string tool = "", string appPath = "/Applications/CrossOver.app")
    {
        if (runner == "native") return "native";
        if (runner == "notproton")
        {
            string versionPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "notproton", "dylib.version");
            return tool + "|" + (File.Exists(versionPath) ? File.ReadAllText(versionPath).Trim() : "missing");
        }
        var plist = Path.Combine(appPath, "Contents", "Info.plist");
        return "crossover|" + appPath + "|" + (File.Exists(plist) ?
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(plist))) : "missing");
    }
    private static string ReportPath => Path.Combine(LauncherPaths.DataDirectory, "mac-gameplay.json");
    public static Dictionary<string, List<MacGameplayReport>> Load()
    {
        if (!File.Exists(ReportPath)) return new(StringComparer.OrdinalIgnoreCase);
        // Invalid reports must not silently become confirmed compatibility.
        return new(JsonSerializer.Deserialize<Dictionary<string, List<MacGameplayReport>>>(File.ReadAllText(ReportPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public static void Save(string repository, MacGameplayReport report)
    {
        if (!report.IsComplete) throw new InvalidOperationException("Provide the release, asset, environment, and gameplay test report.");
        var reports = Load();
        if (!reports.TryGetValue(repository, out var list)) reports[repository] = list = [];
        list.Add(report);
        var temporary = ReportPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, ReportPath, true);
    }

    public static List<MacGameplayReport> Current(MacCompatibilityEntry? entry) =>
        (entry?.Reports ?? []).Where(r => r.IsComplete && r.PlatformFingerprint == PlatformFingerprint && RuntimeIsCurrent(r) && r.Release == entry!.Release &&
            (entry.AvailableAssets.Count == 0 || entry.AvailableAssets.Contains(r.AssetName)))
            .GroupBy(r => (r.Runner, r.AssetName)).Select(g => g.OrderByDescending(r => r.TestedAt).First()).ToList();

    private static bool RuntimeIsCurrent(MacGameplayReport report)
    {
        var parts = report.RuntimeId.Split('|');
        return report.Runner switch
        {
            "native" => report.RuntimeId == "native",
            "notproton" => report.RuntimeId == RuntimeIdentity("notproton", parts[0]),
            "crossover" => parts.Length == 3 && report.RuntimeId == RuntimeIdentity("crossover", appPath: parts[1]),
            _ => false
        };
    }

    public static bool IsVerified(MacCompatibilityEntry? entry, string? release, string? asset, string runner, string? runtimeId = null) =>
        (entry?.Reports ?? []).Where(r => r.IsComplete && r.PlatformFingerprint == PlatformFingerprint && RuntimeIsCurrent(r) &&
            (runtimeId == null || r.RuntimeId == runtimeId) && r.Runner == runner && r.Release == release && r.AssetName == asset)
            .OrderByDescending(r => r.TestedAt).FirstOrDefault()?.IsPlayable == true;

    public static string Summary(MacCompatibilityEntry? entry)
    {
        var reports = Current(entry);
        return reports.Count == 0 ? "Gameplay untested" : string.Join(" · ", reports.Select(r => $"{r.Label} — {r.RunnerLabel}"));
    }

    public static string Details(MacCompatibilityEntry? entry) => string.Join("\n\n", (entry?.Reports ?? [])
        .Where(r => r.IsComplete).OrderByDescending(r => r.TestedAt).Select(r =>
            $"{r.Label} — {r.RunnerLabel} | {r.Release} | {r.AssetName}\n{r.TestedAt:yyyy-MM-dd}: {r.Environment}\nRuntime: {r.RuntimeId}\nPlatform: {r.PlatformFingerprint}\n{r.Evidence}"));
}
