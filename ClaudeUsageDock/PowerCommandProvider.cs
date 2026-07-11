using ClaudeUsageDock.Dock;
using ClaudeUsageDock.Pages;
using ClaudeUsageDock.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock;

public sealed partial class PowerCommandProvider : CommandProvider, IDisposable
{
    private readonly ClaudeUsageService _usageService = new();
    private readonly SettingsManager _settingsManager = new();
    private readonly UsageDetailsPage _detailsPage;
    private readonly UsageDockBand _dockBand;
    private readonly Timer _dockRefreshTimer;

    public PowerCommandProvider()
    {
        Id = "ClaudeUsageDock";
        DisplayName = "Claude Usage Dock";
        Icon = IconHelpers.FromRelativePath("Assets\\icons\\claude-mark.svg");
        Settings = _settingsManager.Settings;

        _detailsPage = new UsageDetailsPage(_usageService);
        _dockBand = new UsageDockBand(_usageService, _settingsManager, _detailsPage);

        _dockRefreshTimer = new Timer(
            async _ => await RefreshDockAsync().ConfigureAwait(false),
            state: null,
            dueTime: TimeSpan.Zero,
            period: _settingsManager.DockRefreshInterval);

        ApplyCacheLifetime();
        _settingsManager.Settings.SettingsChanged += (_, _) => ApplySettings();
    }

    private void ApplySettings()
    {
        _dockRefreshTimer.Change(TimeSpan.Zero, _settingsManager.DockRefreshInterval);
        ApplyCacheLifetime();
    }

    private void ApplyCacheLifetime()
    {
        // Keep the cache slightly shorter than the dock interval so ticks render
        // fresh data, but never let the API be polled more than twice a minute —
        // Anthropic's usage endpoint rate-limits aggressive pollers with 429s.
        var interval = _settingsManager.DockRefreshInterval;
        _usageService.CacheLifetime = TimeSpan.FromSeconds(Math.Max(interval.TotalSeconds - 5, 30));
    }

    public override ICommandItem[] TopLevelCommands() =>
    [
        new CommandItem(_detailsPage)
        {
            Title = "Claude Usage Dock",
            Subtitle = "Session, weekly, and per-model limits",
            Icon = IconHelpers.FromRelativePath("Assets\\icons\\claude-mark.svg"),
        },
    ];

    public override ICommandItem[]? GetDockBands() => [_dockBand.DockItem];

    private async Task RefreshDockAsync()
    {
        await _dockBand.RefreshAsync().ConfigureAwait(false);
        RaiseItemsChanged(0);
    }

    public override void Dispose()
    {
        _dockRefreshTimer.Dispose();
        _usageService.Dispose();
        base.Dispose();
    }
}
