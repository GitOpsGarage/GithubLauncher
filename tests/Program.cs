using GithubLauncher.Services;
using GitHubLauncher.Core.Models;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
GitHubRelease Release(params string[] names) => new()
{
    tag_name = "v1", assets = names.Select(n => new GitHubAsset { name = n }).ToArray()
};
Check(MacCompatibility.FilterAssets(Release("game-Windows.zip", "game-Linux.zip"), null).Count == 0,
    "Unverified Windows/Linux assets must not be offered on Mac.");
Check(MacCompatibility.FilterAssets(Release("game-macOS.zip", "game-Windows.zip", "game-macOS.dmg"), null)
    .Select(a => a.name).SequenceEqual(new[] { "game-macOS.zip", "game-macOS.dmg" }), "ZIP is preferred but native DMG remains available.");
var approved = new MacCompatibilityEntry { Mode = "crossover", Release = "v1", AssetName = "game-Windows.zip",
    Verified = true, Evidence = "Local gameplay test with CrossOver 26.3" };
Check(MacCompatibility.FilterAssets(Release("game-Windows.zip", "other-Windows.exe"), approved).Count == 1,
    "CrossOver approval must match the exact asset.");
Check(!MacCompatibility.IsVerifiedCrossOver(approved, "v2"), "New versions require fresh verification.");
approved.Verified = false;
Check(MacCompatibility.FilterAssets(Release("game-Windows.zip"), approved).Count == 0,
    "An unverified record must not enable Windows downloads.");
approved.Verified = true;
Check(MacCompatibility.CatalogCategory(approved) == "tested", "Verified records are classified as tested.");
var candidate = new MacCompatibilityEntry { Mode = "candidate", Release = "v1", AssetName = "game-Windows.zip", Evidence = "official release" };
Check(MacCompatibility.CatalogCategory(candidate) == "candidate", "Published Windows builds are candidates, not tested.");
Check(MacCompatibility.FilterAssets(Release("game-Windows.zip", "game-Linux.zip"), candidate).Count == 0,
    "Candidate downloads require explicit per-game testing opt-in.");
Check(MacCompatibility.FilterAssets(Release("game-Windows.zip", "game-Linux.zip", "game-Windows.msi"), candidate, true)
    .Select(a => a.name).SequenceEqual(new[] { "game-Windows.zip" }), "Opt-in excludes Linux and unsupported installers.");
Check(MacCompatibility.FilterAssets(Release("game-macOS.zip", "game-Windows.zip"), approved)[0].name == "game-macOS.zip",
    "Native assets take priority over CrossOver.");
var root = Path.Combine(Path.GetTempPath(), "github-launcher-tests-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    File.WriteAllText(Path.Combine(root, "README"), new string('x', 2048));
    File.WriteAllText(Path.Combine(root, "game with spaces.exe"), "fixture");
    var files = MacCompatibility.FindExecutables(root, SearchOption.AllDirectories, out var needsWine);
    Check(files.Count == 1 && needsWine, "Text files must not hide Windows executables.");
    byte[] macho = new byte[16];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(macho, 0xFEEDFACF);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(macho.AsSpan(12), 2);
    File.WriteAllBytes(Path.Combine(root, "native.game"), macho);
    files = MacCompatibility.FindExecutables(root, SearchOption.AllDirectories, out needsWine);
    Check(files.Count == 1 && !needsWine && files[0].EndsWith("native.game"), "Native Mach-O binaries take priority.");
    File.WriteAllText(Path.Combine(root, "launch.sh"), "#!/bin/sh\nexec java -jar game.jar\n");
    files = MacCompatibility.FindExecutables(root, SearchOption.AllDirectories, out needsWine);
    Check(files[0].EndsWith("launch.sh") && !needsWine, "Launch scripts must precede bundled runtime helpers.");
    Directory.CreateDirectory(Path.Combine(root, "Game.app", "Contents", "MacOS"));
    File.WriteAllBytes(Path.Combine(root, "Game.app", "Contents", "MacOS", "helper"), macho);
    files = MacCompatibility.FindExecutables(root, SearchOption.AllDirectories, out needsWine);
    Check(files.Count == 1 && files[0].EndsWith("Game.app"), "Bundle helpers must not become launch candidates.");
}
finally { Directory.Delete(root, true); }
Check(!MacCompatibility.IsCatalogEligible(new() { Mode = "native", AssetName = "game-Windows.zip", Evidence = "source" }),
    "A native catalog record needs an actual Mac archive.");
Check(MacCompatibility.CatalogCategory(MacCompatibility.FromRelease("owner/game", Release("game-macOS.dmg"))) == "native",
    "Native DMG releases must be discovered without an allowlist.");
Check(MacCompatibility.CatalogCategory(MacCompatibility.FromRelease("owner/game", Release("game-Windows.zip"))) == "candidate",
    "Published Windows releases must not be classified as tested.");
Check(MacCompatibility.CatalogCategory(MacCompatibility.FromRelease("owner/game", Release())) == "unknown",
    "Missing assets mean unknown, not incompatible.");
Check(MacCompatibility.FilterAssets(Release("game-macOS-universal.zip", "game-macOS-arm64.zip"), null).Count > 0,
    "Universal builds must be offered on the host architecture.");
Console.WriteLine($"Passed {checks} Mac compatibility checks.");

if (OperatingSystem.IsMacOS())
{
    var fixture = Path.Combine(Path.GetTempPath(), "github-launcher-install-tests-" + Guid.NewGuid());
    Directory.CreateDirectory(fixture);
    try
    {
        var payload = Path.Combine(fixture, "payload");
        Directory.CreateDirectory(Path.Combine(payload, "Wind Waker HD.app", "Contents", "MacOS"));
        File.WriteAllText(Path.Combine(payload, "Wind Waker HD.app", "Contents", "MacOS", "game"), "fixture");
        Directory.CreateDirectory(Path.Combine(payload, "tools"));
        File.WriteAllText(Path.Combine(payload, "tools", "setup.py"), "# fixture");
        File.WriteAllText(Path.Combine(payload, "portable.txt"), "fixture");
        string zip = Path.Combine(fixture, "macOS with spaces.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(payload, zip);
        string installed = Path.Combine(fixture, "installed");
        await MacGameInstaller.InstallAsync(zip, installed, Path.GetFileName(zip), "v1");
        Check(File.Exists(Path.Combine(installed, "tools", "setup.py")), "ZIP installation must preserve sibling setup tools.");
        Check(File.Exists(Path.Combine(installed, "portable.txt")), "Portable markers must survive installation.");
        Check(await File.ReadAllTextAsync(Path.Combine(installed, "version.txt")) == "v1", "Version is written after successful installation.");

        var badPayload = Path.Combine(fixture, "bad-payload");
        Directory.CreateDirectory(badPayload);
        File.WriteAllText(Path.Combine(badPayload, "README"), "No app inside.");
        var badZip = Path.Combine(fixture, "bad-macOS.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(badPayload, badZip);
        bool rejected = false;
        try { await MacGameInstaller.InstallAsync(badZip, installed, "bad-macOS.zip", "v2"); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected && await File.ReadAllTextAsync(Path.Combine(installed, "version.txt")) == "v1",
            "An invalid download must not replace a working installation/version.");

        var tar = Path.Combine(fixture, "game-macOS.tar.xz");
        var tarInfo = new System.Diagnostics.ProcessStartInfo("/usr/bin/tar") { UseShellExecute = false };
        foreach (var arg in new[] { "-cJf", tar, "-C", payload, "." }) tarInfo.ArgumentList.Add(arg);
        using (var process = System.Diagnostics.Process.Start(tarInfo)!)
        { await process.WaitForExitAsync(); Check(process.ExitCode == 0, "TAR.XZ fixture must be created."); }
        var tarInstall = Path.Combine(fixture, "tar-installed");
        await MacGameInstaller.InstallAsync(tar, tarInstall, "game-macOS.tar.xz", "v3");
        Check(File.Exists(Path.Combine(tarInstall, "tools", "setup.py")), "TAR.XZ installation must preserve all payload files.");

        var dmg = Path.Combine(fixture, "game-macOS.dmg");
        var dmgInfo = new System.Diagnostics.ProcessStartInfo("/usr/bin/hdiutil") { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var arg in new[] { "create", "-srcfolder", payload, "-volname", "Launcher Native Test", "-format", "UDZO", dmg }) dmgInfo.ArgumentList.Add(arg);
        using (var process = System.Diagnostics.Process.Start(dmgInfo)!)
        { var output = process.StandardOutput.ReadToEndAsync(); await process.WaitForExitAsync(); await output;
          Check(process.ExitCode == 0, "DMG fixture must be created."); }
        var dmgInstall = Path.Combine(fixture, "dmg-installed");
        await MacGameInstaller.InstallAsync(dmg, dmgInstall, "game-macOS.dmg", "v4");
        Check(File.Exists(Path.Combine(dmgInstall, "tools", "setup.py")), "DMG installation must preserve sibling files and detach the image.");

        if (args.Length > 0)
        {
            var actualInstall = Path.Combine(fixture, "actual-release");
            await MacGameInstaller.InstallAsync(args[0], actualInstall, Path.GetFileName(args[0]), "fixture-release");
            var actualApps = MacCompatibility.FindExecutables(actualInstall, SearchOption.AllDirectories, out var windows);
            Check(actualApps.Count > 0 && !windows, "The actual macOS release must expose a native launch target.");
            Check(actualApps.Any(p => Directory.Exists(p) && File.Exists(Path.Combine(p, "Contents", "Info.plist"))),
                "The actual app bundle metadata must survive installation.");
        }
    }
    finally { Directory.Delete(fixture, true); }
    Console.WriteLine($"Passed {checks} compatibility and native installation checks.");
}
