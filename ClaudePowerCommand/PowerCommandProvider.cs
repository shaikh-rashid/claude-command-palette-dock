using ClaudePowerCommand.Dock;
using ClaudePowerCommand.Pages;
using ClaudePowerCommand.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudePowerCommand;

public sealed partial class PowerCommandProvider : CommandProvider, IDisposable
{
    private static readonly TimeSpan DockRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly ClaudeUsageService _usageService = new();
    private readonly UsageDetailsPage _detailsPage;
    private readonly UsageDockBand _dockBand;
    private readonly Timer _dockRefreshTimer;

    public PowerCommandProvider()
    {
        Id = "ClaudePowerCommand";
        DisplayName = "Claude Power Command";
        Icon = IconHelpers.FromRelativePath("Assets\\icons\\claude-mark.svg");

        _detailsPage = new UsageDetailsPage(_usageService);
        _dockBand = new UsageDockBand(_usageService, _detailsPage);

        _dockRefreshTimer = new Timer(
            async _ => await RefreshDockAsync().ConfigureAwait(false),
            state: null,
            dueTime: TimeSpan.Zero,
            period: DockRefreshInterval);
    }

    public override ICommandItem[] TopLevelCommands() =>
    [
        new CommandItem(_detailsPage)
        {
            Title = "Claude usage",
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

    public void Dispose()
    {
        _dockRefreshTimer.Dispose();
        _usageService.Dispose();
    }
}
