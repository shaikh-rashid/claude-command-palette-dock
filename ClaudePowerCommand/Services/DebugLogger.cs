namespace ClaudePowerCommand.Services;

/// <summary>
/// Opt-in file logger. Silent unless %TEMP%\claude-power-command.debug exists,
/// so the extension never writes to disk for ordinary users.
/// </summary>
internal static class DebugLogger
{
    private static readonly string LogFilePath = Path.Combine(Path.GetTempPath(), "claude-power-command.log");
    private static readonly string EnableFlagPath = Path.Combine(Path.GetTempPath(), "claude-power-command.debug");

    public static void Log(string message)
    {
        if (!File.Exists(EnableFlagPath))
        {
            return;
        }

        try
        {
            File.AppendAllText(LogFilePath, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Best-effort logging only; never let a logging failure surface to the user.
        }
    }
}
