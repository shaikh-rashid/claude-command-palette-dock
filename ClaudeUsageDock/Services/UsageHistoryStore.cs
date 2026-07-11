using System.Globalization;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock.Services;

public sealed record UsagePoint(DateTimeOffset Timestamp, double SessionRemainingPercent, double WeeklyRemainingPercent);

/// <summary>
/// Rolling local log of usage snapshots (CSV: unix-ms,session%,weekly%). Backs the
/// burn-rate estimate and the weekly sparkline. Data never leaves the machine.
/// </summary>
public sealed class UsageHistoryStore
{
    private static readonly TimeSpan MinSampleSpacing = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(8);
    private const long PruneThresholdBytes = 256 * 1024;

    private readonly object _gate = new();
    private readonly string _filePath;
    private DateTimeOffset _lastAppend = DateTimeOffset.MinValue;

    /// <param name="fileSuffix">
    /// Distinguishes one profile's history from another's on disk. Null/empty keeps
    /// the original single-profile filename so existing history isn't orphaned.
    /// </param>
    public UsageHistoryStore(string? fileSuffix = null)
    {
        var directory = Utilities.BaseSettingsPath("ClaudeUsageDock");
        Directory.CreateDirectory(directory);
        var fileName = string.IsNullOrEmpty(fileSuffix) ? "usage-history.csv" : $"usage-history-{fileSuffix}.csv";
        _filePath = Path.Combine(directory, fileName);
    }

    public void Record(ClaudeUsageSnapshot snapshot)
    {
        lock (_gate)
        {
            if (snapshot.RetrievedAt - _lastAppend < MinSampleSpacing)
            {
                return;
            }

            try
            {
                var line = string.Create(CultureInfo.InvariantCulture,
                    $"{snapshot.RetrievedAt.ToUnixTimeMilliseconds()},{snapshot.SessionRemainingPercent:F1},{snapshot.WeeklyRemainingPercent:F1}");
                File.AppendAllText(_filePath, line + Environment.NewLine);
                _lastAppend = snapshot.RetrievedAt;

                if (new FileInfo(_filePath).Length > PruneThresholdBytes)
                {
                    Prune();
                }
            }
            catch (IOException ex)
            {
                DebugLogger.Log($"Could not append usage history: {ex.Message}");
            }
        }
    }

    /// <summary>Returns points within the given window, oldest first.</summary>
    public IReadOnlyList<UsagePoint> Load(TimeSpan window)
    {
        var cutoff = DateTimeOffset.UtcNow - window;
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return [];
                }

                return File.ReadAllLines(_filePath)
                    .Select(ParseLine)
                    .OfType<UsagePoint>()
                    .Where(p => p.Timestamp >= cutoff)
                    .OrderBy(p => p.Timestamp)
                    .ToList();
            }
            catch (IOException ex)
            {
                DebugLogger.Log($"Could not read usage history: {ex.Message}");
                return [];
            }
        }
    }

    private static UsagePoint? ParseLine(string line)
    {
        var parts = line.Split(',');
        if (parts.Length != 3 ||
            !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixMs) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var session) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var weekly))
        {
            return null;
        }

        return new UsagePoint(DateTimeOffset.FromUnixTimeMilliseconds(unixMs), session, weekly);
    }

    private void Prune()
    {
        var cutoff = DateTimeOffset.UtcNow - RetentionWindow;
        var kept = File.ReadAllLines(_filePath)
            .Where(line => ParseLine(line) is { } point && point.Timestamp >= cutoff)
            .ToArray();

        var tempPath = _filePath + ".tmp";
        File.WriteAllLines(tempPath, kept);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
