using ClaudeUsageDock.Pages;
using ClaudeUsageDock.Resources;
using ClaudeUsageDock.Services;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock.Dock;

/// <summary>
/// The small tile that lives in the Command Palette dock. Clicking it opens
/// <see cref="UsageDetailsPage"/>; the tile itself just shows a live percentage.
/// </summary>
internal sealed class UsageDockBand
{
    private readonly ClaudeUsageService _usageService;
    private readonly SettingsManager _settings;
    private readonly ListItem _tile;
    private readonly string _baseTitle;
    private readonly string _titleSuffix;
    // Last icon state applied, so the icon object is only swapped on transitions.
    private bool _appliedLowQuota;
    // Whether the current dip below the threshold has already produced a toast.
    private bool _lowQuotaNotified;

    public WrappedDockItem DockItem { get; }

    public UsageDockBand(ClaudeUsageService usageService, SettingsManager settings, UsageDetailsPage detailsPage, UsageProfile profile)
    {
        _usageService = usageService;
        _settings = settings;
        _baseTitle = profile.Label is null
            ? Strings.Get("Heading_Default")
            : Strings.Format("Heading_WithLabel", profile.Label);
        _titleSuffix = profile.Label is null ? string.Empty : $" — {profile.Label}";

        _tile = new ListItem(detailsPage)
        {
            Title = _baseTitle,
            Icon = Icons.ClaudeMark,
        };

        // Keep the default profile's dock item id exactly as before so it's not
        // orphaned in anyone's already-configured Dock.
        var dockId = profile.Label is null ? "claudeusagedock.dock.usage" : $"claudeusagedock.dock.usage.{profile.Id}";
        var bandName = profile.Label is null ? "Claude Usage" : $"Claude Usage — {profile.Label}";
        DockItem = new WrappedDockItem([_tile], dockId, bandName);
    }

    /// <summary>
    /// Called on every provider timer tick: re-reads usage (usually served from
    /// the service's cache) and rewrites the tile's title, subtitle, and icon.
    /// Failures render as a short status message instead of numbers.
    /// </summary>
    public async Task RefreshAsync()
    {
        var result = await _usageService.GetSnapshotAsync().ConfigureAwait(false);

        if (result.Outcome != UsageFetchOutcome.Success || result.Snapshot is null)
        {
            ApplyTile(_baseTitle, DescribeFailure(result.Outcome, result.StatusCode), lowOnQuota: false);
            return;
        }

        var snapshot = result.Snapshot;
        var sessionLeft = (int)Math.Round(snapshot.SessionRemainingPercent);
        var weeklyLeft = (int)Math.Round(snapshot.WeeklyRemainingPercent);
        var resetsLocal = snapshot.SessionResetsAt.ToLocalTime();
        var lowOnQuota = sessionLeft < _settings.LowQuotaThresholdPercent;

        // Toast once per dip below the threshold; recovering (session reset)
        // re-arms it for the next one.
        if (lowOnQuota && !_lowQuotaNotified && _settings.LowQuotaToastEnabled)
        {
            ToastNotifier.ShowLowQuota(sessionLeft, resetsLocal);
        }

        _lowQuotaNotified = lowOnQuota;

        ApplyTile(
            title: Strings.Format("Tile_SessionLeft", sessionLeft) + _titleSuffix,
            subtitle: Strings.Format("Tile_Subtitle", weeklyLeft, resetsLocal.ToString("t")),
            lowOnQuota: lowOnQuota);
    }

    /// <summary>Tile-sized (subtitle-length) failure text; the details page has the long-form versions.</summary>
    private static string DescribeFailure(UsageFetchOutcome outcome, int? statusCode) => outcome switch
    {
        UsageFetchOutcome.NotSignedIn => Strings.Get("TileFail_NotSignedIn"),
        UsageFetchOutcome.TokenExpired => Strings.Get("TileFail_TokenExpired"),
        UsageFetchOutcome.RateLimited => Strings.Get("TileFail_RateLimited"),
        UsageFetchOutcome.RequestFailed => Strings.Format("TileFail_RequestFailed", statusCode),
        UsageFetchOutcome.Offline => Strings.Get("TileFail_Offline"),
        UsageFetchOutcome.UnexpectedResponse => Strings.Get("TileFail_UnexpectedResponse"),
        _ => Strings.Get("TileFail_Unknown"),
    };

    /// <summary>Writes the tile; the icon is only touched when the low-quota state flips, since it's the expensive part to re-render.</summary>
    private void ApplyTile(string title, string subtitle, bool lowOnQuota)
    {
        _tile.Title = title;
        _tile.Subtitle = subtitle;

        if (lowOnQuota != _appliedLowQuota)
        {
            _tile.Icon = lowOnQuota ? Icons.ClaudeMarkAlert : Icons.ClaudeMark;
            _appliedLowQuota = lowOnQuota;
        }
    }
}
