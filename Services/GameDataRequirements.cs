namespace GithubLauncher.Services;

public sealed record GameDataRequirement(string Console, string Region, string DiscId, string Format, string Setup, string Guide)
{
    public string Summary => $"Original game: {Console} · {Region} · {DiscId} · {Format}";
    public string Details => $"Console: {Console}\nRegion: {Region}\nDisc / revision: {DiscId}\nFiles: {Format}\n\n{Setup}";
}

public static class GameDataRequirements
{
    // Curated from each project's official setup guide; never infer a console from the title.
    private static readonly Dictionary<string, GameDataRequirement> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Matteo842/CrashBandicoot-Launcher"] = new("PlayStation (PS1)", "USA", "SCUS-94900", ".chd or .cue + .bin",
            "Select the disc in the Crash launcher, then START GAME. Keep the .cue and every referenced .bin together. First launch builds the game. An arbitrary .iso or a disc from another console is not the documented input.", "https://github.com/Matteo842/CrashBandicoot-Launcher#quick-start"),
        ["Zumbo06/Crash2Recomp"] = new("PlayStation (PS1)", "North America", "SCUS-94154", ".chd or .cue + .bin",
            "Choose the disc on Setup, then Build the game. Keep all tracks together. First build can take 5–20 minutes. No retail BIOS required.", "https://github.com/Zumbo06/Crash2Recomp#what-you-need"),
        ["goliathret/EdgeOfTimeRecomp"] = new("Xbox 360", "US", "Title Update required", "Xbox 360 dump containers / images",
            "Use Add Files in the game's installer with original Xbox 360 dump containers/images. Folder input is currently unavailable. Identity Crisis DLC is optional. Follow the official dumping guide; another console's edition is not interchangeable.", "https://github.com/goliathret/EdgeOfTimeRecomp#how-to-install"),
        ["BlackLabelHQ/SymphonyRecomp"] = new("PlayStation (PS1)", "North America / NTSC", "English USA base; revision not specified in README", ".chd or .cue + both .bin tracks",
            "Keep both data and audio tracks. For manual setup, use the exact filenames and disc directory documented by the project. Other language versions do not replace the required USA base.", "https://github.com/BlackLabelHQ/SymphonyRecomp#prerequisites"),
        ["TechnicallyComputers/Street-Fighter-Alpha-3-Recomp"] = new("PlayStation (PS1)", "USA", "SLUS-00821", ".cue + referenced .bin tracks",
            "Use the built-in Generate & Build setup with the original USA PS1 disc. Keep the .cue and its tracks together. Check the official guide for your release's setup requirements.", "https://github.com/TechnicallyComputers/Street-Fighter-Alpha-3-Recomp#quick-start-dev")
    };

    public static GameDataRequirement Find(string? repository) => repository != null && Known.TryGetValue(repository, out var value)
        ? value : new("Not documented here", "Not confirmed", "Not confirmed", "Check the official setup guide",
            "This launcher has not recorded the required console, region, revision or file format for this project. Check its official guide before choosing game data. A matching title or .iso extension does not prove compatibility.",
            "https://github.com/" + Uri.EscapeDataString(repository?.Split('/').FirstOrDefault() ?? "") + "/" + Uri.EscapeDataString(repository?.Split('/').ElementAtOrDefault(1) ?? "") + "#readme");

    public static string NoReleaseMessage(string? name, bool windows, bool testing) => windows
        ? $"No Windows release is available for {name} under the current compatibility settings. " +
            (testing ? "Check the project's releases and supported archive formats." : "To try an unverified Windows build, open Configure Mac launch…, select Windows, and enable Allow testing unverified Windows builds. Steam / NotProton or CrossOver is required; gameplay is unconfirmed.")
        : $"No native macOS release was found for {name}. A Windows or Linux release does not run natively on Mac. To try a Windows build, open Configure Mac launch…, select Windows, choose Steam / NotProton or CrossOver, and enable testing of unverified builds. Gameplay is unconfirmed.";
}
