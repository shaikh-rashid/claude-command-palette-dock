using ClaudeUsageDock.Pages;
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
    private bool _appliedLowQuota;
    private bool _lowQuotaNotified;

    public WrappedDockItem DockItem { get; }

    public UsageDockBand(ClaudeUsageService usageService, SettingsManager settings, UsageDetailsPage detailsPage)
    {
        _usageService = usageService;
        _settings = settings;
        _tile = new ListItem(detailsPage)
        {
            Title = "Claude usage",
            Icon = Icons.ClaudeMark,
        };
        DockItem = new WrappedDockItem([_tile], "claudeusagedock.dock.usage", "Claude Usage");
    }

    public async Task RefreshAsync()
    {
        var result = await _usageService.GetSnapshotAsync().ConfigureAwait(false);

        if (result.Outcome != UsageFetchOutcome.Success || result.Snapshot is null)
        {
            ApplyTile("Claude usage", DescribeFailure(result.Outcome, result.StatusCode), lowOnQuota: false);
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
            title: $"{sessionLeft}% session left",
            subtitle: $"{weeklyLeft}% week · resets {resetsLocal:t}",
            lowOnQuota: lowOnQuota);
    }

    private static string DescribeFailure(UsageFetchOutcome outcome, int? statusCode) => outcome switch
    {
        UsageFetchOutcome.NotSignedIn => "Not signed in to Claude Code",
        UsageFetchOutcome.TokenExpired => "Session expired — sign in to Claude Code",
        UsageFetchOutcome.RateLimited => "Rate limited — retrying soon",
        UsageFetchOutcome.RequestFailed => $"Anthropic API error ({statusCode})",
        UsageFetchOutcome.Offline => "Offline — will retry",
        UsageFetchOutcome.UnexpectedResponse => "Unexpected response",
        _ => "Unable to fetch usage",
    };

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
