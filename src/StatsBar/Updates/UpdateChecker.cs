using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace StatsBar.Updates;

public sealed record UpdateCheckResult(string CurrentVersion, string LatestVersion, bool IsUpdateAvailable);

public static class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/VC067/win-stats/releases/latest";
    public const string LatestReleasePage = "https://github.com/VC067/win-stats/releases/latest";

    private static readonly HttpClient Client = CreateClient();

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(LatestReleaseApi, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("tag_name", out var tagElement)
            || tagElement.ValueKind != JsonValueKind.String
            || !TryParseVersion(tagElement.GetString(), out var latestVersion))
        {
            throw new InvalidDataException("GitHub returned a release without a valid version tag.");
        }

        var current = GetCurrentVersion();
        return new UpdateCheckResult(current.ToString(), latestVersion.ToString(), latestVersion > current);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WinStats", GetCurrentVersion().ToString()));
        return client;
    }

    private static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        var informationalVersion = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var versionText = informationalVersion ?? assembly?.GetName().Version?.ToString();
        if (TryParseVersion(versionText, out var version))
            return version;

        throw new InvalidOperationException("The installed Win Stats version could not be read.");
    }

    private static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
            normalized = normalized[1..];

        var suffix = normalized.IndexOfAny(['-', '+']);
        if (suffix >= 0)
            normalized = normalized[..suffix];

        if (!Version.TryParse(normalized, out var parsed) || parsed == null)
            return false;

        version = parsed;
        return true;
    }
}
