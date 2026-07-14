using System.Security;
using ClaudeUsageDock.Resources;
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
            // Localized strings get XML-escaped: a translation is data here, not markup.
            var title = SecurityElement.Escape(Strings.Get("Toast_LowQuotaTitle"));
            var body = SecurityElement.Escape(Strings.Format("Toast_LowQuotaBody", sessionLeftPercent, resetsAtLocal.ToString("t")));

            var xml = new XmlDocument();
            xml.LoadXml($"""
                <toast>
                  <visual>
                    <binding template="ToastGeneric">
                      <text>{title}</text>
                      <text>{body}</text>
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
