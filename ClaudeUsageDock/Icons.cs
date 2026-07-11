using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock;

/// <summary>
/// Shared themed icons. Each has a light-background and dark-background variant;
/// Command Palette picks the right one for the active theme.
/// </summary>
internal static class Icons
{
    public static IconInfo ClaudeMark { get; } = IconHelpers.FromRelativePaths(
        "Assets\\icons\\claude-mark-light.svg",
        "Assets\\icons\\claude-mark-dark.svg");

    public static IconInfo ClaudeMarkAlert { get; } = IconHelpers.FromRelativePaths(
        "Assets\\icons\\claude-mark-alert-light.svg",
        "Assets\\icons\\claude-mark-alert-dark.svg");
}
