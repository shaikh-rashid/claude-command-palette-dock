using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace ClaudeUsageDock.Services;

/// <summary>
/// Windows toast for the low-quota alert. Works because the MSIX package gives
/// the extension host its own identity; failures are logged and swallowed so a
/// notification problem can never break the dock.
/// </summary>
internal static class ToastNotifier
{
    /// <summary>Fires the "session running low" toast with the remaining percent and reset time.</summary>
    public static void ShowLowQuota(int sessionLeftPercent, DateTimeOffset resetsAtLocal)
    {
        try
        {
            var xml = new XmlDocument();
            xml.LoadXml($"""
                <toast>
                  <visual>
                    <binding template="ToastGeneric">
                      <text>Claude session running low</text>
                      <text>{sessionLeftPercent}% remaining · resets {resetsAtLocal:t}</text>
                    </binding>
                  </visual>
                </toast>
                """);

            ToastNotificationManager.CreateToastNotifier().Show(new ToastNotification(xml));
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"Could not show low-quota toast: {ex.Message}");
        }
    }
}
