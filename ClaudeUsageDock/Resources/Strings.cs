using System.Globalization;
using System.Resources;

namespace ClaudeUsageDock.Resources;

/// <summary>
/// Typed access to the UI strings in Strings.resx. Lookup follows the user's
/// Windows display language (CurrentUICulture) through the satellite assemblies
/// packaged with the app, falling back to the neutral English resources. A
/// missing key returns the key itself rather than throwing — a visible
/// "Tab_Usage" in the UI beats a crashed page, and it names the culprit.
/// </summary>
internal static class Strings
{
    private static readonly ResourceManager Resources =
        new("ClaudeUsageDock.Resources.Strings", typeof(Strings).Assembly);

    public static string Get(string name) =>
        Resources.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    /// <summary>Composite-formats a resource template with CurrentCulture (so numbers and dates render locally too).</summary>
    public static string Format(string name, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(name), args);
}
