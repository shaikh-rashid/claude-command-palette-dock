using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock.Services;

/// <summary>
/// User-configurable options, persisted through Command Palette's JSON settings
/// store and editable from the extension's Settings page in CmdPal.
/// </summary>
internal sealed class SettingsManager : JsonSettingsManager
{
    private const int DefaultRefreshSeconds = 30;
    private const int DefaultThresholdPercent = 20;

    private readonly ChoiceSetSetting _refreshInterval = new(
        "refreshInterval",
        "Dock refresh interval",
        "How often the dock tile re-checks your usage",
        [
            new ChoiceSetSetting.Choice("15 seconds", "15"),
            new ChoiceSetSetting.Choice("30 seconds", "30"),
            new ChoiceSetSetting.Choice("1 minute", "60"),
            new ChoiceSetSetting.Choice("5 minutes", "300"),
        ]);

    private readonly ChoiceSetSetting _lowQuotaThreshold = new(
        "lowQuotaThreshold",
        "Low-quota alert threshold",
        "Switch the tile to the alert icon when less than this much of the session remains",
        [
            new ChoiceSetSetting.Choice("10%", "10"),
            new ChoiceSetSetting.Choice("20%", "20"),
            new ChoiceSetSetting.Choice("30%", "30"),
            new ChoiceSetSetting.Choice("50%", "50"),
        ]);

    public TimeSpan DockRefreshInterval => TimeSpan.FromSeconds(ParseOrDefault(_refreshInterval.Value, DefaultRefreshSeconds));

    public int LowQuotaThresholdPercent => ParseOrDefault(_lowQuotaThreshold.Value, DefaultThresholdPercent);

    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_refreshInterval);
        Settings.Add(_lowQuotaThreshold);

        LoadSettings();
        Settings.SettingsChanged += (_, _) => SaveSettings();
    }

    private static int ParseOrDefault(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath("ClaudeUsageDock");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }
}
