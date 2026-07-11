using System.Text;
using ClaudeUsageDock.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock.Pages;

/// <summary>
/// Full-page breakdown shown when the dock tile (or the top-level command) is opened:
/// session limit, weekly limit, and any per-model weekly limits, each as a bar chart.
/// </summary>
internal sealed class UsageDetailsPage : ContentPage
{
    private const int BarWidthChars = 20;
    private readonly ClaudeUsageService _usageService;
    private readonly string _heading;

    public UsageDetailsPage(ClaudeUsageService usageService, UsageProfile profile)
    {
        _usageService = usageService;
        _heading = profile.Label is null ? "Claude usage" : $"Claude usage — {profile.Label}";

        // Keep the default profile's ids/labels exactly as before so existing
        // users' pinned/added items aren't orphaned by this change.
        var idSuffix = profile.Label is null ? string.Empty : $".{profile.Id}";
        Id = $"claudeusagedock.page.usage{idSuffix}";
        Name = profile.Label is null ? "Claude Usage" : _heading;
        Title = _heading;
        Icon = Icons.ClaudeMark;
    }

    public override IContent[] GetContent()
    {
        var result = _usageService.GetSnapshotAsync(bypassCache: false).GetAwaiter().GetResult();

        return [new MarkdownContent(Render(result)), new RefreshFormContent(this)];
    }

    /// <summary>
    /// The Refresh button. Submitting re-queries Anthropic past the snapshot cache
    /// and re-renders the page with the fresh result.
    /// </summary>
    private sealed partial class RefreshFormContent : FormContent
    {
        private readonly UsageDetailsPage _page;

        public RefreshFormContent(UsageDetailsPage page)
        {
            _page = page;
            TemplateJson = """
            {
              "type": "AdaptiveCard",
              "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
              "version": "1.5",
              "body": [],
              "actions": [
                { "type": "Action.Submit", "title": "Refresh", "data": { "action": "refresh" } }
              ]
            }
            """;
            DataJson = "{}";
        }

        public override ICommandResult SubmitForm(string inputs, string data)
        {
            _ = _page._usageService.GetSnapshotAsync(bypassCache: true).GetAwaiter().GetResult();
            _page.RaiseItemsChanged();
            return CommandResult.KeepOpen();
        }
    }

    private string Render(UsageFetchResult result)
    {
        if (result.Outcome != UsageFetchOutcome.Success || result.Snapshot is null)
        {
            return $"# {_heading}\n\n{DescribeFailure(result)}";
        }

        var snapshot = result.Snapshot;
        var text = new StringBuilder();
        text.AppendLine($"# {_heading}");
        text.AppendLine();
        text.AppendLine($"Plan: **{snapshot.PlanType}** · last checked {snapshot.RetrievedAt.ToLocalTime():t}");
        text.AppendLine();

        AppendBar(text, "5-hour session", snapshot.SessionRemainingPercent, snapshot.SessionResetsAt);
        AppendBurnEstimate(text, snapshot);
        AppendBar(text, "7-day (all models)", snapshot.WeeklyRemainingPercent, snapshot.WeeklyResetsAt);
        AppendWeeklySparkline(text);

        foreach (var model in snapshot.PerModelWeekly)
        {
            AppendBar(text, $"7-day — {model.DisplayName}", 100 - model.PercentUsed, model.ResetsAt);
        }

        return text.ToString();
    }

    /// <summary>Projects when the session hits 0% from the last ~90 minutes of samples.</summary>
    private void AppendBurnEstimate(StringBuilder text, ClaudeUsageSnapshot snapshot)
    {
        var points = _usageService.History.Load(TimeSpan.FromMinutes(90));
        if (points.Count < 2)
        {
            return;
        }

        var oldest = points[0];
        var newest = points[^1];
        var elapsedHours = (newest.Timestamp - oldest.Timestamp).TotalHours;
        if (elapsedHours < 0.25)
        {
            return; // Not enough spread to say anything credible.
        }

        var burnPerHour = (oldest.SessionRemainingPercent - newest.SessionRemainingPercent) / elapsedHours;
        if (burnPerHour < 1)
        {
            return; // Flat or recovering — an estimate would be noise.
        }

        var hoursLeft = snapshot.SessionRemainingPercent / burnPerHour;
        var emptyAt = DateTimeOffset.UtcNow.AddHours(hoursLeft);

        text.AppendLine(emptyAt >= snapshot.SessionResetsAt
            ? "*At the current pace, your session lasts until it resets.*"
            : $"*At the current pace (≈{burnPerHour:F0}%/h), the session runs out around {emptyAt.ToLocalTime():t}.*");
        text.AppendLine();
    }

    /// <summary>Sparkline of weekly usage (used %, 6-hour buckets over the past 7 days).</summary>
    private void AppendWeeklySparkline(StringBuilder text)
    {
        const int BucketCount = 28;
        var window = TimeSpan.FromDays(7);
        var points = _usageService.History.Load(window);
        if (points.Count < 3)
        {
            return;
        }

        var start = DateTimeOffset.UtcNow - window;
        var bucketLength = TimeSpan.FromTicks(window.Ticks / BucketCount);
        var levels = "▁▂▃▄▅▆▇█";
        var line = new StringBuilder(BucketCount);
        var filledBuckets = 0;

        for (var i = 0; i < BucketCount; i++)
        {
            var bucketEnd = start + bucketLength * (i + 1);
            var sample = points.LastOrDefault(p => p.Timestamp < bucketEnd && p.Timestamp >= bucketEnd - bucketLength);
            if (sample is null)
            {
                line.Append('·');
                continue;
            }

            var usedPercent = Math.Clamp(100 - sample.WeeklyRemainingPercent, 0, 100);
            line.Append(levels[Math.Min((int)(usedPercent / 100 * levels.Length), levels.Length - 1)]);
            filledBuckets++;
        }

        if (filledBuckets < 3)
        {
            return; // Sparkline needs a little history before it means anything.
        }

        text.AppendLine($"`{line}` *usage over the past week*");
        text.AppendLine();
    }

    private void AppendBar(StringBuilder text, string label, double remainingPercent, DateTimeOffset resetsAt)
    {
        var remaining = (int)Math.Round(Math.Clamp(remainingPercent, 0, 100));
        var filledChars = Math.Clamp(remaining * BarWidthChars / 100, 0, BarWidthChars);
        var bar = new string('▰', filledChars) + new string('▱', BarWidthChars - filledChars);

        text.AppendLine($"### {label}");
        text.AppendLine($"`{bar}` **{remaining}%** left · resets {resetsAt.ToLocalTime():t}");
        text.AppendLine();
    }

    private static string DescribeFailure(UsageFetchResult result) => result.Outcome switch
    {
        UsageFetchOutcome.NotSignedIn => "No local Claude Code session found. Sign in with `claude login` and reopen this page.",
        UsageFetchOutcome.TokenExpired => "Your Claude Code token has expired and automatic refresh didn't succeed. Sign in again in Claude Code (run `claude` in a terminal), then refresh this page.",
        UsageFetchOutcome.RateLimited => "Anthropic is rate-limiting usage checks right now (HTTP 429). This clears on its own — the page will show data again within a couple of minutes.",
        UsageFetchOutcome.RequestFailed => $"Anthropic's usage API returned an error (status {result.StatusCode}).",
        UsageFetchOutcome.Offline => "Couldn't reach Anthropic — check your network connection.",
        UsageFetchOutcome.UnexpectedResponse => "Got a response that didn't look like usage data.",
        _ => "Couldn't fetch usage right now.",
    };
}
