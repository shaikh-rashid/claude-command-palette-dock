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
    private const int LowSessionThresholdPercent = 20;
    private const string NormalIconPath = "Assets\\icons\\claude-mark.svg";
    private const string AlertIconPath = "Assets\\icons\\claude-mark-alert.svg";

    private readonly ClaudeUsageService _usageService;
    private readonly ListItem _tile;
    private string? _appliedIconPath;

    public WrappedDockItem DockItem { get; }

    public UsageDockBand(ClaudeUsageService usageService, UsageDetailsPage detailsPage)
    {
        _usageService = usageService;
        _tile = new ListItem(detailsPage)
        {
            Title = "Claude usage",
            Icon = IconHelpers.FromRelativePath(NormalIconPath),
        };
        _appliedIconPath = NormalIconPath;
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

        ApplyTile(
            title: $"{sessionLeft}% session left",
            subtitle: $"{weeklyLeft}% week · resets {resetsLocal:t}",
            lowOnQuota: sessionLeft < LowSessionThresholdPercent);
    }

    private static string DescribeFailure(UsageFetchOutcome outcome, int? statusCode) => outcome switch
    {
        UsageFetchOutcome.NotSignedIn => "Not signed in to Claude Code",
        UsageFetchOutcome.RequestFailed => $"Anthropic API error ({statusCode})",
        UsageFetchOutcome.Offline => "Offline — will retry",
        UsageFetchOutcome.UnexpectedResponse => "Unexpected response",
        _ => "Unable to fetch usage",
    };

    private void ApplyTile(string title, string subtitle, bool lowOnQuota)
    {
        _tile.Title = title;
        _tile.Subtitle = subtitle;

        var iconPath = lowOnQuota ? AlertIconPath : NormalIconPath;
        if (iconPath == _appliedIconPath)
        {
            return;
        }

        try
        {
            _tile.Icon = IconHelpers.FromRelativePath(iconPath);
            _appliedIconPath = iconPath;
        }
        catch (IOException)
        {
            // Keep whatever icon is already showing rather than leave the tile blank.
        }
    }
}
