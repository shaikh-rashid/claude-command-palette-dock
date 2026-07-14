using ClaudeUsageDock.Dock;
using ClaudeUsageDock.Pages;
using ClaudeUsageDock.Resources;
using ClaudeUsageDock.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock;

/// <summary>
/// The single command provider behind the extension: it owns the settings, builds
/// one <see cref="ProfileRuntime"/> (usage service + dock tile + details page +
/// top-level command) per configured account, and drives the periodic refresh
/// timer that keeps the dock tiles live.
/// </summary>
public sealed partial class PowerCommandProvider : CommandProvider, IDisposable
{
    private readonly SettingsManager _settingsManager = new();
    private readonly Timer _dockRefreshTimer;
    private readonly object _profilesLock = new();

    // Swapped wholesale (never mutated) under _profilesLock when settings change;
    // readers take a local copy so a rebuild can't pull the list out from under them.
    private IReadOnlyList<ProfileRuntime> _profiles = [];

    public PowerCommandProvider()
    {
        Id = "ClaudeUsageDock";
        DisplayName = "Claude Usage Dock";
        Icon = Icons.ClaudeMark;
        Settings = _settingsManager.Settings;

        RebuildProfiles();

        _dockRefreshTimer = new Timer(
            async _ => await RefreshAllAsync().ConfigureAwait(false),
            state: null,
            dueTime: TimeSpan.Zero,
            period: _settingsManager.DockRefreshInterval);

        _settingsManager.Settings.SettingsChanged += (_, _) =>
        {
            RebuildProfiles();
            _dockRefreshTimer.Change(TimeSpan.Zero, _settingsManager.DockRefreshInterval);
        };
    }

    /// <summary>
    /// Recreates the profile set only when it actually changed, so unrelated
    /// settings edits (refresh interval, threshold) don't drop caches or
    /// re-trigger the low-quota toast for every account.
    /// </summary>
    private void RebuildProfiles()
    {
        var desired = _settingsManager.GetProfiles();

        lock (_profilesLock)
        {
            if (_profiles.Count == desired.Count &&
                _profiles.Select(p => p.Profile).SequenceEqual(desired))
            {
                return;
            }

            foreach (var old in _profiles)
            {
                old.Service.Dispose();
            }

            _profiles = desired.Select(CreateRuntime).ToList();
        }

        ApplyCacheLifetimes();
        RaiseItemsChanged(0);
    }

    /// <summary>Builds the full object set for one account profile.</summary>
    private ProfileRuntime CreateRuntime(UsageProfile profile)
    {
        var service = new ClaudeUsageService(profile.CredentialsFilePath, profile.HistorySuffix);
        var detailsPage = new UsageDetailsPage(service, profile, _settingsManager.Settings.SettingsPage);
        var dockBand = new UsageDockBand(service, _settingsManager, detailsPage, profile);
        var command = new CommandItem(detailsPage)
        {
            Title = profile.Label is null ? "Claude Usage Dock" : $"Claude Usage Dock — {profile.Label}",
            Subtitle = Strings.Get("Command_Subtitle"),
            Icon = Icons.ClaudeMark,
        };

        return new ProfileRuntime(profile, service, dockBand, command);
    }

    private void ApplyCacheLifetimes()
    {
        // Keep the cache slightly shorter than the dock interval so ticks render
        // fresh data, but never let the API be polled more than twice a minute —
        // Anthropic's usage endpoint rate-limits aggressive pollers with 429s.
        var interval = _settingsManager.DockRefreshInterval;
        var lifetime = TimeSpan.FromSeconds(Math.Max(interval.TotalSeconds - 5, 30));

        foreach (var profile in _profiles)
        {
            profile.Service.CacheLifetime = lifetime;
        }
    }

    /// <summary>One searchable "Claude Usage Dock" command per profile.</summary>
    public override ICommandItem[] TopLevelCommands() => _profiles.Select(p => (ICommandItem)p.Command).ToArray();

    /// <summary>One dock tile per profile, offered to CmdPal's Dock settings page.</summary>
    public override ICommandItem[]? GetDockBands() => _profiles.Select(p => (ICommandItem)p.DockBand.DockItem).ToArray();

    /// <summary>Timer tick: refresh every profile's tile, then tell CmdPal to re-read them.</summary>
    private async Task RefreshAllAsync()
    {
        // Snapshot the list: RebuildProfiles() can swap it out from under a running tick.
        var profiles = _profiles;
        await Task.WhenAll(profiles.Select(p => p.DockBand.RefreshAsync())).ConfigureAwait(false);
        RaiseItemsChanged(0);
    }

    public override void Dispose()
    {
        _dockRefreshTimer.Dispose();

        foreach (var profile in _profiles)
        {
            profile.Service.Dispose();
        }

        base.Dispose();
    }

    /// <summary>Everything one account contributes to CmdPal, bundled so it's created and disposed together.</summary>
    private sealed record ProfileRuntime(UsageProfile Profile, ClaudeUsageService Service, UsageDockBand DockBand, CommandItem Command);
}
