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
    TokenExpired,
    RateLimited,
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

    /// <summary>How long we keep serving the last good snapshot while the API is rate-limiting us or unreachable.</summary>
    private static readonly TimeSpan StaleServeLimit = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultRateLimitBackoff = TimeSpan.FromSeconds(90);

    /// <summary>How long a fetched snapshot stays fresh. The provider tunes this to the dock refresh interval.</summary>
    public TimeSpan CacheLifetime { get; set; } = TimeSpan.FromSeconds(45);

    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly string _credentialsFilePath;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private ClaudeUsageSnapshot? _lastSnapshot;
    private UsageFetchResult? _lastFailure;
    private DateTimeOffset _backoffUntil = DateTimeOffset.MinValue;

    public ClaudeUsageService()
    {
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _credentialsFilePath = Path.Combine(homeDirectory, ".claude", ".credentials.json");
    }

    public async Task<UsageFetchResult> GetSnapshotAsync(bool bypassCache = false)
    {
        if (!bypassCache && IsCacheFresh())
        {
            return UsageFetchResult.Ok(_lastSnapshot!);
        }

        // Single-flight: the dock timer and a page open can race; only one request goes out.
        await _fetchLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!bypassCache && IsCacheFresh())
            {
                return UsageFetchResult.Ok(_lastSnapshot!);
            }

            // While rate-limited, don't touch the API at all — even for manual refreshes.
            if (DateTimeOffset.UtcNow < _backoffUntil)
            {
                return StaleOrFailure();
            }

            var result = await FetchFromApiAsync().ConfigureAwait(false);
            if (result.Outcome == UsageFetchOutcome.Success)
            {
                _lastFailure = null;
                return result;
            }

            _lastFailure = result;
            return result.Outcome is UsageFetchOutcome.RateLimited or UsageFetchOutcome.Offline
                ? StaleOrFailure()
                : result;
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    private bool IsCacheFresh() =>
        _lastSnapshot is not null && DateTimeOffset.UtcNow - _lastSnapshot.RetrievedAt < CacheLifetime;

    /// <summary>A recent-enough snapshot beats an error tile; only fail when we have nothing to show.</summary>
    private UsageFetchResult StaleOrFailure()
    {
        if (_lastSnapshot is not null && DateTimeOffset.UtcNow - _lastSnapshot.RetrievedAt < StaleServeLimit)
        {
            return UsageFetchResult.Ok(_lastSnapshot);
        }

        return _lastFailure ?? UsageFetchResult.Failure(UsageFetchOutcome.RateLimited);
    }

    private async Task<UsageFetchResult> FetchFromApiAsync()
    {
        var credentials = ReadCredentials();
        if (string.IsNullOrEmpty(credentials.AccessToken))
        {
            DebugLogger.Log("No local Claude Code access token found; treating as signed out.");
            return UsageFetchResult.Failure(UsageFetchOutcome.NotSignedIn);
        }

        if (credentials.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            DebugLogger.Log($"Stored access token expired at {expiresAt:O}; Claude Code needs to refresh it.");
            return UsageFetchResult.Failure(UsageFetchOutcome.TokenExpired);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credentials.AccessToken);
            request.Headers.Add("anthropic-beta", OAuthBetaHeader);
            request.Headers.TryAddWithoutValidation("User-Agent", ClientUserAgent);

            using var response = await SharedHttpClient.SendAsync(request).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
                _backoffUntil = DateTimeOffset.UtcNow + Clamp(retryAfter ?? DefaultRateLimitBackoff);
                DebugLogger.Log($"Rate limited (429); backing off until {_backoffUntil:O}.");
                return UsageFetchResult.Failure(UsageFetchOutcome.RateLimited, 429);
            }

            if (!response.IsSuccessStatusCode)
            {
                DebugLogger.Log($"Usage endpoint returned {(int)response.StatusCode}.");
                return UsageFetchResult.Failure(UsageFetchOutcome.RequestFailed, (int)response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var snapshot = ParseSnapshot(body, credentials.PlanType ?? "unknown");
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

    private static ClaudeUsageSnapshot ParseSnapshot(string json, string planType)
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
            PlanType: planType,
            RetrievedAt: DateTimeOffset.UtcNow);
    }

    /// <summary>Keeps a server-provided Retry-After within sane bounds (30 s – 15 min).</summary>
    private static TimeSpan Clamp(TimeSpan backoff) =>
        TimeSpan.FromSeconds(Math.Clamp(backoff.TotalSeconds, 30, 900));

    private static DateTimeOffset ParseResetTime(string? isoTimestamp)
    {
        if (!string.IsNullOrEmpty(isoTimestamp) &&
            DateTimeOffset.TryParse(isoTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return DateTimeOffset.UtcNow;
    }

    private sealed record StoredCredentials(string? AccessToken, DateTimeOffset? ExpiresAt, string? PlanType);

    private StoredCredentials ReadCredentials()
    {
        try
        {
            if (!File.Exists(_credentialsFilePath))
            {
                return new StoredCredentials(null, null, null);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_credentialsFilePath));
            var oauth = document.RootElement.GetPropertyOrNull("claudeAiOauth");

            DateTimeOffset? expiresAt = null;
            if (oauth?.GetPropertyOrNull("expiresAt") is { ValueKind: JsonValueKind.Number } expiresElement &&
                expiresElement.TryGetInt64(out var expiresMs))
            {
                expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expiresMs);
            }

            return new StoredCredentials(
                oauth?.GetPropertyOrNull("accessToken")?.GetString(),
                expiresAt,
                oauth?.GetPropertyOrNull("subscriptionType")?.GetString());
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            DebugLogger.Log($"Could not read Claude Code credentials file: {ex.Message}");
            return new StoredCredentials(null, null, null);
        }
    }

    public void Dispose()
    {
        _fetchLock.Dispose();
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
