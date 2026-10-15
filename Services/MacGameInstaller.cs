using System.Diagnostics;

namespace GithubLauncher.Services;

public static class MacGameInstaller
{
    public static async Task InstallAsync(string downloadPath, string gamePath, string assetName, string version)
    {
        var staging = Path.Combine(Path.GetTempPath(), "github-launcher-install-" + Guid.NewGuid());
        Directory.CreateDirectory(staging);
        try
        {
            if (assetName.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
                await ExtractDiskImageAsync(downloadPath, staging);
            else if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                await RunAsync("/usr/bin/ditto", "-x", "-k", downloadPath, staging);
            else if (assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                     assetName.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase) ||
                     assetName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
                await RunAsync("/usr/bin/tar", "-xf", downloadPath, "-C", staging);
            else if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                File.Copy(downloadPath, Path.Combine(staging, Path.GetFileName(assetName)));
            else
                throw new NotSupportedException($"Unsupported Mac installation format: {assetName}");

            // Some release ZIPs wrap a ZIP containing the actual app bundle.
            foreach (var nested in Directory.GetFiles(staging, "*.zip", SearchOption.AllDirectories))
                await RunAsync("/usr/bin/ditto", "-x", "-k", nested, Path.Combine(Path.GetDirectoryName(nested)!, Path.GetFileNameWithoutExtension(nested)));

            var source = GetPayloadRoot(staging);
            var candidates = MacCompatibility.FindExecutables(source, SearchOption.AllDirectories, out _);
            if (candidates.Count == 0)
                throw new InvalidDataException("The downloaded archive contains no launchable Mac app or Windows executable. The existing installation has been preserved.");
            Directory.CreateDirectory(gamePath);
            // Copy the full payload: SDKs, tools, portable markers, and resources can live beside the app.
            await RunAsync("/usr/bin/ditto", source, gamePath);
            await File.WriteAllTextAsync(Path.Combine(gamePath, "version.txt"), version);
            await File.WriteAllTextAsync(Path.Combine(gamePath, "installed-asset.txt"), assetName);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    internal static string GetPayloadRoot(string path)
    {
        var entries = Directory.GetFileSystemEntries(path)
            .Where(p => Path.GetFileName(p) is not ".DS_Store" and not "__MACOSX").ToArray();
        while (entries.Length == 1 && Directory.Exists(entries[0]) &&
               !entries[0].EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            path = entries[0];
            entries = Directory.GetFileSystemEntries(path)
                .Where(p => Path.GetFileName(p) is not ".DS_Store" and not "__MACOSX").ToArray();
        }
        return path;
    }

    private static async Task ExtractDiskImageAsync(string image, string destination)
    {
        var mount = Path.Combine(Path.GetTempPath(), "github-launcher-mount-" + Guid.NewGuid());
        Directory.CreateDirectory(mount);
        bool mounted = false;
        try
        {
            await RunAsync("/usr/bin/hdiutil", "attach", "-readonly", "-nobrowse", "-noautoopen", "-mountpoint", mount, image);
            mounted = true;
            foreach (var path in Directory.GetFileSystemEntries(mount))
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith('.') || name == "Applications") continue;
                await RunAsync("/usr/bin/ditto", path, Path.Combine(destination, name));
            }
        }
        finally
        {
            if (mounted) await RunAsync("/usr/bin/hdiutil", "detach", mount);
            if (Directory.Exists(mount)) Directory.Delete(mount);
        }
    }

    private static async Task RunAsync(string command, params string[] arguments)
    {
        var info = new ProcessStartInfo(command)
        {
            UseShellExecute = false, RedirectStandardError = true,
            RedirectStandardOutput = true, RedirectStandardInput = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException($"Could not start {command}.");
        // Never auto-accept a disk image's license agreement or wait for hidden input.
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await output;
        var message = await error;
        if (process.ExitCode != 0) throw new IOException($"{Path.GetFileName(command)} failed: {message}");
    }
}
