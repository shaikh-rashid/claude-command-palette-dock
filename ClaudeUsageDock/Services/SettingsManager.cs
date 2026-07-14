using ClaudeUsageDock.Resources;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock.Services;

/// <param name="Id">Stable, e.g. for dock item / page ids — never changes even if the label does.</param>
/// <param name="Label">Shown in titles, e.g. "Claude usage — Work".</param>
/// <param name="CredentialsFilePath">Null for the default profile (Claude Code's own credentials file).</param>
/// <param name="HistorySuffix">Null for the default profile, to preserve its original history filename.</param>
public sealed record UsageProfile(string Id, string? Label, string? CredentialsFilePath, string? HistorySuffix);

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
        Strings.Get("Setting_RefreshInterval_Title"),
        Strings.Get("Setting_RefreshInterval_Desc"),
        [
            new ChoiceSetSetting.Choice(Strings.Get("Setting_Choice_15Seconds"), "15"),
            new ChoiceSetSetting.Choice(Strings.Get("Setting_Choice_30Seconds"), "30"),
            new ChoiceSetSetting.Choice(Strings.Get("Setting_Choice_1Minute"), "60"),
            new ChoiceSetSetting.Choice(Strings.Get("Setting_Choice_5Minutes"), "300"),
        ]);

    private readonly ChoiceSetSetting _lowQuotaThreshold = new(
        "lowQuotaThreshold",
        Strings.Get("Setting_Threshold_Title"),
        Strings.Get("Setting_Threshold_Desc"),
        [
            new ChoiceSetSetting.Choice("10%", "10"),
            new ChoiceSetSetting.Choice("20%", "20"),
            new ChoiceSetSetting.Choice("30%", "30"),
            new ChoiceSetSetting.Choice("50%", "50"),
        ]);

    private readonly ToggleSetting _lowQuotaToast = new(
        "lowQuotaToast",
        Strings.Get("Setting_Toast_Title"),
        Strings.Get("Setting_Toast_Desc"),
        true);

    private readonly TextSetting _profile2Label = new(
        "profile2Label",
        Strings.Get("Setting_Profile2Label_Title"),
        Strings.Get("Setting_Profile2Label_Desc"),
        string.Empty);

    private readonly TextSetting _profile2Path = new(
        "profile2Path",
        Strings.Get("Setting_Profile2Path_Title"),
        Strings.Get("Setting_ProfilePath_Desc"),
        string.Empty);

    private readonly TextSetting _profile3Label = new(
        "profile3Label",
        Strings.Get("Setting_Profile3Label_Title"),
        Strings.Get("Setting_Profile3Label_Desc"),
        string.Empty);

    private readonly TextSetting _profile3Path = new(
        "profile3Path",
        Strings.Get("Setting_Profile3Path_Title"),
        Strings.Get("Setting_ProfilePath_Desc"),
        string.Empty);

    /// <summary>How often the provider's timer refreshes the dock tiles.</summary>
    public TimeSpan DockRefreshInterval => TimeSpan.FromSeconds(ParseOrDefault(_refreshInterval.Value, DefaultRefreshSeconds));

    /// <summary>Session-remaining percentage below which the tile switches to the alert icon.</summary>
    public int LowQuotaThresholdPercent => ParseOrDefault(_lowQuotaThreshold.Value, DefaultThresholdPercent);

    /// <summary>Whether crossing the threshold also fires a Windows toast.</summary>
    public bool LowQuotaToastEnabled => _lowQuotaToast.Value;

    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_refreshInterval);
        Settings.Add(_lowQuotaThreshold);
        Settings.Add(_lowQuotaToast);
        Settings.Add(_profile2Label);
        Settings.Add(_profile2Path);
        Settings.Add(_profile3Label);
        Settings.Add(_profile3Path);

        LoadSettings();
        Settings.SettingsChanged += (_, _) => SaveSettings();
    }

    /// <summary>
    /// The default profile (id "default") is always present, pointing at Claude
    /// Code's own credentials file. Slots 2 and 3 only appear once a path is set,
    /// so the common single-account case is unaffected.
    /// </summary>
    public IReadOnlyList<UsageProfile> GetProfiles()
    {
        var profiles = new List<UsageProfile> { new("default", null, null, null) };

        AddIfConfigured(profiles, "profile2", _profile2Label.Value, _profile2Path.Value, Strings.Get("Setting_Profile2_Fallback"));
        AddIfConfigured(profiles, "profile3", _profile3Label.Value, _profile3Path.Value, Strings.Get("Setting_Profile3_Fallback"));

        return profiles;
    }

    /// <summary>Adds an extra account slot only when its credentials path is filled in.</summary>
    private static void AddIfConfigured(List<UsageProfile> profiles, string id, string? label, string? path, string fallbackLabel)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        profiles.Add(new UsageProfile(id, string.IsNullOrWhiteSpace(label) ? fallbackLabel : label.Trim(), path.Trim(), id));
    }

    /// <summary>Defends against hand-edited settings.json values that aren't positive integers.</summary>
    private static int ParseOrDefault(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    /// <summary>settings.json under CmdPal's per-extension settings directory (created on first run).</summary>
    private static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath("ClaudeUsageDock");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }
}
