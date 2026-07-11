using System.Globalization;
using System.Text.Json;

namespace ClaudeUsageDock.Services;

public sealed record ModelUsage(string DisplayName, double PercentUsed, DateTimeOffset ResetsAt);

public sealed record ClaudeUsageSnapshot(
    double SessionRemainingPercent,
    double WeeklyRemainingPercent,
    DateTimeOffset SessionResetsAt,
    DateTimeOffset WeeklyResetsAt,
    IReadOnlyList<ModelUsage> PerModelWeekly,
    string PlanType,
    DateTimeOffset RetrievedAt);

public enum UsageFetchOutcome
{
    Success,
    NotSignedIn,
    RequestFailed,
    Offline,
    UnexpectedResponse,
}

public sealed record UsageFetchResult(UsageFetchOutcome Outcome, ClaudeUsageSnapshot? Snapshot, int? StatusCode = null)
{
    public static UsageFetchResult Ok(ClaudeUsageSnapshot snapshot) => new(UsageFetchOutcome.Success, snapshot);

    public static UsageFetchResult Failure(UsageFetchOutcome outcome, int? statusCode = null) => new(outcome, null, statusCode);
}

/// <summary>
/// Talks to Anthropic's usage endpoint using the OAuth token Claude Code already
/// stores locally. Nothing is sent anywhere except Anthropic's official API.
/// </summary>
public sealed class ClaudeUsageService : IDisposable
{
    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
    private const string OAuthBetaHeader = "oauth-2025-04-20";
    private const string ClientUserAgent = "claude-usage-dock/1.0";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(45);

    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly string _credentialsFilePath;
    private ClaudeUsageSnapshot? _lastSnapshot;

    public ClaudeUsageService()
    {
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _credentialsFilePath = Path.Combine(homeDirectory, ".claude", ".credentials.json");
    }

    public async Task<UsageFetchResult> GetSnapshotAsync(bool bypassCache = false)
    {
        if (!bypassCache && _lastSnapshot is not null && DateTimeOffset.UtcNow - _lastSnapshot.RetrievedAt < CacheLifetime)
        {
            return UsageFetchResult.Ok(_lastSnapshot);
        }

        return await FetchFromApiAsync().ConfigureAwait(false);
    }

    private async Task<UsageFetchResult> FetchFromApiAsync()
    {
        var accessToken = TryReadAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            DebugLogger.Log("No local Claude Code access token found; treating as signed out.");
            return UsageFetchResult.Failure(UsageFetchOutcome.NotSignedIn);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("anthropic-beta", OAuthBetaHeader);
            request.Headers.TryAddWithoutValidation("User-Agent", ClientUserAgent);

            using var response = await SharedHttpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                DebugLogger.Log($"Usage endpoint returned {(int)response.StatusCode}.");
                return UsageFetchResult.Failure(UsageFetchOutcome.RequestFailed, (int)response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var snapshot = ParseSnapshot(body);
            _lastSnapshot = snapshot;
            return UsageFetchResult.Ok(snapshot);
        }
        catch (HttpRequestException ex)
        {
            DebugLogger.Log($"Network error reaching Anthropic: {ex.Message}");
            return UsageFetchResult.Failure(UsageFetchOutcome.Offline);
        }
        catch (TaskCanceledException ex)
        {
            DebugLogger.Log($"Usage request timed out: {ex.Message}");
            return UsageFetchResult.Failure(UsageFetchOutcome.Offline);
        }
        catch (JsonException ex)
        {
            DebugLogger.Log($"Could not parse usage response: {ex.Message}");
            return UsageFetchResult.Failure(UsageFetchOutcome.UnexpectedResponse);
        }
    }

    private static ClaudeUsageSnapshot ParseSnapshot(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var fiveHour = root.GetProperty("five_hour");
        var sevenDay = root.GetProperty("seven_day");

        var sessionUsedPercent = fiveHour.GetProperty("utilization").GetDouble();
        var weeklyUsedPercent = sevenDay.GetProperty("utilization").GetDouble();

        var perModel = new List<ModelUsage>();
        if (root.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var limit in limits.EnumerateArray())
            {
                if (limit.GetPropertyOrNull("kind")?.GetString() != "weekly_scoped")
                {
                    continue;
                }

                var modelName = limit.GetPropertyOrNull("scope")?.GetPropertyOrNull("model")?.GetPropertyOrNull("display_name")?.GetString();
                if (string.IsNullOrEmpty(modelName))
                {
                    continue;
                }

                perModel.Add(new ModelUsage(
                    modelName,
                    limit.GetProperty("percent").GetDouble(),
                    ParseResetTime(limit.GetPropertyOrNull("resets_at")?.GetString())));
            }
        }

        return new ClaudeUsageSnapshot(
            SessionRemainingPercent: Math.Clamp(100 - sessionUsedPercent, 0, 100),
            WeeklyRemainingPercent: Math.Clamp(100 - weeklyUsedPercent, 0, 100),
            SessionResetsAt: ParseResetTime(fiveHour.GetPropertyOrNull("resets_at")?.GetString()),
            WeeklyResetsAt: ParseResetTime(sevenDay.GetPropertyOrNull("resets_at")?.GetString()),
            PerModelWeekly: perModel,
            PlanType: root.GetPropertyOrNull("subscription_type")?.GetString() ?? "unknown",
            RetrievedAt: DateTimeOffset.UtcNow);
    }

    private static DateTimeOffset ParseResetTime(string? isoTimestamp)
    {
        if (!string.IsNullOrEmpty(isoTimestamp) &&
            DateTimeOffset.TryParse(isoTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return DateTimeOffset.UtcNow;
    }

    private string? TryReadAccessToken()
    {
        try
        {
            if (!File.Exists(_credentialsFilePath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_credentialsFilePath));
            return document.RootElement.GetPropertyOrNull("claudeAiOauth")?.GetPropertyOrNull("accessToken")?.GetString();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            DebugLogger.Log($"Could not read Claude Code credentials file: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        // SharedHttpClient is process-lifetime; nothing per-instance to release yet.
    }
}

internal static class JsonElementExtensions
{
    public static JsonElement? GetPropertyOrNull(this JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;
    }
}
