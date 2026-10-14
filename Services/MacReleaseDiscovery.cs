using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GitHubLauncher.Core.Models;

namespace GithubLauncher.Services;

public static class MacReleaseDiscovery
{
    private static readonly SemaphoreSlim RefreshLock = new(1);
    public static async Task<string> RefreshAsync(IEnumerable<string> repositories, string? token)
    {
        await RefreshLock.WaitAsync();
        try
        {
            var evidence = MacCompatibility.Load();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GitOpsGarage-GithubLauncher");
            if (!string.IsNullOrWhiteSpace(token)) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            int checkedCount = 0;
            bool limited = false;
            foreach (var repo in repositories.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (evidence.TryGetValue(repo, out var prior) &&
                    prior.CheckedAt > DateTimeOffset.UtcNow.AddDays(-1)) continue;
                try
                {
                    using var response = await client.GetAsync($"https://api.github.com/repos/{repo}/releases/latest");
                    if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                    { limited = true; break; }
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        evidence[repo] = new() { Mode = "unknown", CheckedAt = DateTimeOffset.UtcNow,
                            Requirements = "No published stable release found; source builds may still be available." };
                    }
                    else
                    {
                        response.EnsureSuccessStatusCode();
                        var release = JsonSerializer.Deserialize<GitHubRelease>(await response.Content.ReadAsStringAsync());
                        if (release == null) continue;
                        if (prior is { Verified: true } && prior.Release == release.tag_name)
                            prior.CheckedAt = DateTimeOffset.UtcNow;
                        else
                        {
                            var discovered = MacCompatibility.FromRelease(repo, release);
                            if (!string.IsNullOrWhiteSpace(prior?.Requirements)) discovered.Requirements = prior.Requirements;
                            evidence[repo] = discovered;
                        }
                    }
                    checkedCount++;
                    if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) &&
                        int.TryParse(values.FirstOrDefault(), out var remaining) && remaining <= 5)
                    { limited = true; break; }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    // A network failure means unknown, never incompatible. Preserve existing evidence.
                    System.Diagnostics.Debug.WriteLine($"Mac discovery skipped {repo}: {ex.Message}");
                }
            }
            var path = Path.Combine(LauncherPaths.DataDirectory, "mac-discovery.json");
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
            return limited ? "Limite GitHub atingido; avaliação restante pendente. Dados já conhecidos foram preservados." :
                checkedCount > 0 ? $"Releases atualizadas: {checkedCount}." : "Avaliação de releases em cache (24 horas).";
        }
        finally { RefreshLock.Release(); }
    }
}
