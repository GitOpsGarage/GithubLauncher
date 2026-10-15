using System.Diagnostics;
using System.Text;

namespace GithubLauncher.Services;

public static class MacLaunchDiagnostics
{
    public static async Task<Process> StartAsync(ProcessStartInfo info, int observationMilliseconds = 2000)
    {
        info.UseShellExecute = false;
        info.RedirectStandardError = true;
        info.RedirectStandardOutput = true;
        var output = new StringBuilder();
        var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the game process.");
        async Task DrainAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
                lock (output)
                    if (output.Length < 16384) output.AppendLine(line[..Math.Min(line.Length, 16384 - output.Length)]);
        }
        // Keep draining both streams so a running game never blocks on a full pipe.
        var drain = Task.WhenAll(DrainAsync(process.StandardError), DrainAsync(process.StandardOutput));
        var exit = process.WaitForExitAsync();
        if (await Task.WhenAny(exit, Task.Delay(observationMilliseconds)) == exit)
        {
            await exit;
            if (process.ExitCode != 0)
            {
                // A child may inherit the pipes; do not wait indefinitely for EOF.
                await Task.WhenAny(drain, Task.Delay(250));
                string details;
                lock (output) details = output.ToString().Trim();
                int code = process.ExitCode;
                process.Dispose();
                throw new InvalidOperationException($"The game exited during startup (code {code}).\n\n" +
                    (details.Length > 0 ? details : "No diagnostic output was produced. Check the game's setup requirements."));
            }
        }
        return process;
    }
}
