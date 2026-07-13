using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeUsageDock.Services;

/// <summary>One model-scoped weekly limit (e.g. Opus), as reported by the usage API.</summary>
public sealed record ModelUsage(string DisplayName, double PercentUsed, DateTimeOffset ResetsAt);

/// <summary>
/// One successful reading of the usage API: how much of the 5-hour session and
/// 7-day windows remain (as percentages), when each resets, any per-model weekly
/// limits, plus the plan type read from the local credentials file.
/// </summary>
public sealed record ClaudeUsageSnapshot(
    double SessionRemainingPercent,
    double WeeklyRemainingPercent,
    DateTimeOffset SessionResetsAt,
    DateTimeOffset WeeklyResetsAt,
    IReadOnlyList<ModelUsage> PerModelWeekly,
    string PlanType,
    DateTimeOffset RetrievedAt);

/// <summary>
/// Why a fetch produced (or didn't produce) a snapshot. The dock tile and details
/// page map each value to its own user-facing message.
/// </summary>
public enum UsageFetchOutcome
{
    Success,
    /// <summary>No credentials file, or no access token in it.</summary>
    NotSignedIn,
    /// <summary>Stored token expired and the refresh attempt failed too.</summary>
    TokenExpired,
    /// <summary>HTTP 429 — the service is backing off and serving stale data meanwhile.</summary>
    RateLimited,
    /// <summary>Any other non-success HTTP status (carried in StatusCode).</summary>
    RequestFailed,
    /// <summary>Network error or timeout before a response arrived.</summary>
    Offline,
    /// <summary>The response wasn't JSON in the shape the parser expects.</summary>
    UnexpectedResponse,
}

/// <summary>Outcome plus snapshot-on-success; StatusCode is set for HTTP-level failures.</summary>
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
    private const string TokenEndpoint = "https://console.anthropic.com/v1/oauth/token";

    /// <summary>Claude Code's public OAuth client id — we refresh the same grant it created.</summary>
    private const string ClaudeCodeClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    private const string OAuthBetaHeader = "oauth-2025-04-20";
    private const string ClientUserAgent = "claude-usage-dock/1.0";
    private static readonly TimeSpan RefreshRetryInterval = TimeSpan.FromMinutes(5);

    /// <summary>How long we keep serving the last good snapshot while the API is rate-limiting us or unreachable.</summary>
    private static readonly TimeSpan StaleServeLimit = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultRateLimitBackoff = TimeSpan.FromSeconds(90);

    /// <summary>How long a fetched snapshot stays fresh. The provider tunes this to the dock refresh interval.</summary>
    public TimeSpan CacheLifetime { get; set; } = TimeSpan.FromSeconds(45);

    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Local snapshot log backing the burn-rate estimate and weekly trend graph.</summary>
    public UsageHistoryStore History { get; }

    private readonly string _credentialsFilePath;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private ClaudeUsageSnapshot? _lastSnapshot;
    private UsageFetchResult? _lastFailure;
    private DateTimeOffset _backoffUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRefreshAttempt = DateTimeOffset.MinValue;

    /// <summary>
    /// Tokens we refreshed but could not write back to the credentials file.
    /// Without this a failed write-back would strand us (and Claude Code) on a
    /// rotated-away refresh token.
    /// </summary>
    private StoredCredentials? _memoryCredentials;

    /// <param name="credentialsFilePath">
    /// Overrides where the OAuth token is read from, for monitoring a secondary
    /// account whose credentials were saved to a different file. Defaults to
    /// Claude Code's own path.
    /// </param>
    /// <param name="historyFileSuffix">Keeps each profile's local usage log separate.</param>
    public ClaudeUsageService(string? credentialsFilePath = null, string? historyFileSuffix = null)
    {
        if (!string.IsNullOrWhiteSpace(credentialsFilePath))
        {
            _credentialsFilePath = Environment.ExpandEnvironmentVariables(credentialsFilePath);
        }
        else
        {
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _credentialsFilePath = Path.Combine(homeDirectory, ".claude", ".credentials.json");
        }

        History = new UsageHistoryStore(historyFileSuffix);
    }

    /// <summary>
    /// The one public entry point: returns the cached snapshot while it's fresh,
    /// otherwise fetches a new one. <paramref name="bypassCache"/> (the Refresh
    /// command) skips the freshness check but still honors rate-limit backoff.
    /// </summary>
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

    /// <summary>
    /// One full fetch: read credentials, refresh the token if it's expired, call
    /// the usage endpoint, parse, cache, and log the snapshot to history.
    /// </summary>
    private async Task<UsageFetchResult> FetchFromApiAsync()
    {
        var credentials = ReadCredentials();
        if (string.IsNullOrEmpty(credentials.AccessToken))
        {
            DebugLogger.Log("No local Claude Code access token found; treating as signed out.");
            return UsageFetchResult.Failure(UsageFetchOutcome.NotSignedIn);
        }

        // Prefer in-memory tokens from a refresh whose write-back failed; the
        // file's copy is stale (and its refresh token possibly rotated away).
        if (_memoryCredentials is { } memory &&
            memory.ExpiresAt > (credentials.ExpiresAt ?? DateTimeOffset.MinValue))
        {
            credentials = memory with { PlanType = credentials.PlanType ?? memory.PlanType };
        }

        if (credentials.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            var refreshed = await TryRefreshTokenAsync(credentials).ConfigureAwait(false);
            if (refreshed is null)
            {
                DebugLogger.Log($"Stored access token expired at {expiresAt:O} and refresh didn't succeed.");
                return UsageFetchResult.Failure(UsageFetchOutcome.TokenExpired);
            }

            credentials = refreshed;
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
            History.Record(snapshot);
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

    /// <summary>
    /// Maps the API's shape (<c>five_hour</c>/<c>seven_day</c> objects with a
    /// used-percent <c>utilization</c>, plus a <c>limits</c> array whose
    /// <c>weekly_scoped</c> entries are per-model caps) onto the snapshot record.
    /// Utilization is inverted here: the API reports percent used, the UI shows
    /// percent remaining.
    /// </summary>
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

    /// <summary>Parses an ISO reset timestamp, falling back to "now" so the UI never shows a garbage date.</summary>
    private static DateTimeOffset ParseResetTime(string? isoTimestamp)
    {
        if (!string.IsNullOrEmpty(isoTimestamp) &&
            DateTimeOffset.TryParse(isoTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return DateTimeOffset.UtcNow;
    }

    /// <summary>The fields this extension needs from Claude Code's credentials file; all optional because the file may be absent or partial.</summary>
    private sealed record StoredCredentials(string? AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, string? PlanType);

    /// <summary>
    /// Reads the <c>claudeAiOauth</c> block of Claude Code's credentials JSON.
    /// Any read/parse problem degrades to "no credentials" (→ NotSignedIn)
    /// rather than throwing into the dock refresh path.
    /// </summary>
    private StoredCredentials ReadCredentials()
    {
        try
        {
            if (!File.Exists(_credentialsFilePath))
            {
                return new StoredCredentials(null, null, null, null);
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
                oauth?.GetPropertyOrNull("refreshToken")?.GetString(),
                expiresAt,
                oauth?.GetPropertyOrNull("subscriptionType")?.GetString());
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            DebugLogger.Log($"Could not read Claude Code credentials file: {ex.Message}");
            return new StoredCredentials(null, null, null, null);
        }
    }

    /// <summary>
    /// Exchanges the stored refresh token for a fresh access token, the same way
    /// Claude Code does. Only called once the stored token is actually expired,
    /// so a running Claude Code instance (which refreshes proactively) never races us.
    /// </summary>
    private async Task<StoredCredentials?> TryRefreshTokenAsync(StoredCredentials current)
    {
        if (string.IsNullOrEmpty(current.RefreshToken))
        {
            return null;
        }

        // A dead refresh token fails deterministically; don't retry every tick.
        if (DateTimeOffset.UtcNow - _lastRefreshAttempt < RefreshRetryInterval)
        {
            return null;
        }

        _lastRefreshAttempt = DateTimeOffset.UtcNow;

        try
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = current.RefreshToken,
                ["client_id"] = ClaudeCodeClientId,
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("User-Agent", ClientUserAgent);

            using var response = await SharedHttpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                DebugLogger.Log($"Token refresh failed with status {(int)response.StatusCode}.");
                return null;
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var accessToken = root.GetPropertyOrNull("access_token")?.GetString();
            if (string.IsNullOrEmpty(accessToken))
            {
                DebugLogger.Log("Token refresh response contained no access token.");
                return null;
            }

            var refreshToken = root.GetPropertyOrNull("refresh_token")?.GetString() ?? current.RefreshToken;
            var expiresInSeconds = root.GetPropertyOrNull("expires_in")?.GetDouble() ?? 3600;
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);

            var refreshed = current with { AccessToken = accessToken, RefreshToken = refreshToken, ExpiresAt = expiresAt };
            _memoryCredentials = refreshed;
            PersistRefreshedCredentials(accessToken, refreshToken, expiresAt);
            DebugLogger.Log("Access token refreshed.");
            return refreshed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            DebugLogger.Log($"Token refresh failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Writes rotated tokens back so Claude Code keeps working. Preserves every
    /// other field in the file and swaps it in atomically; on failure the
    /// in-memory copy keeps this extension alive until Claude Code re-signs-in.
    /// </summary>
    private void PersistRefreshedCredentials(string accessToken, string refreshToken, DateTimeOffset expiresAt)
    {
        try
        {
            var rootNode = JsonNode.Parse(File.ReadAllText(_credentialsFilePath)) as JsonObject ?? [];
            if (rootNode["claudeAiOauth"] is not JsonObject oauth)
            {
                oauth = [];
                rootNode["claudeAiOauth"] = oauth;
            }

            oauth["accessToken"] = accessToken;
            oauth["refreshToken"] = refreshToken;
            oauth["expiresAt"] = expiresAt.ToUnixTimeMilliseconds();

            var tempPath = _credentialsFilePath + ".tmp";
            File.WriteAllText(tempPath, rootNode.ToJsonString());
            File.Move(tempPath, _credentialsFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            DebugLogger.Log($"Could not write refreshed tokens back to the credentials file: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _fetchLock.Dispose();
    }
}

/// <summary>Small helper so optional JSON fields read as one-liners instead of TryGetProperty ceremony.</summary>
internal static class JsonElementExtensions
{
    /// <summary>The property's value, or null when absent, JSON-null, or the element isn't an object.</summary>
    public static JsonElement? GetPropertyOrNull(this JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;
    }
}
