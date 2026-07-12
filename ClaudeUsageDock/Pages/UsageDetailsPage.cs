using System.Text.Json.Nodes;
using ClaudeUsageDock.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock.Pages;

/// <summary>
/// Full-page breakdown shown when the dock tile (or the top-level command) is opened:
/// session limit, weekly limit, and any per-model weekly limits as bar charts, with
/// the weekly trend graph alongside them.
/// </summary>
internal sealed class UsageDetailsPage : ContentPage
{
    private const int BarWidthChars = 16;
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

        if (result.Outcome != UsageFetchOutcome.Success || result.Snapshot is null)
        {
            return [new MarkdownContent($"# {_heading}\n\n{DescribeFailure(result)}")];
        }

        var snapshot = result.Snapshot;
        var header = $"# {_heading}\n\nPlan: **{snapshot.PlanType}** · last checked {snapshot.RetrievedAt.ToLocalTime():t}";
        return [new MarkdownContent(header), new UsageCardContent(this, snapshot)];
    }

    /// <summary>
    /// The usage card: bars in the left column, the weekly trend graph in the right
    /// one (an AdaptiveCard ColumnSet — markdown can't put block content side by
    /// side), and the Refresh action. Submitting re-queries Anthropic past the
    /// snapshot cache and re-renders the page with the fresh result.
    /// </summary>
    private sealed partial class UsageCardContent : FormContent
    {
        private readonly UsageDetailsPage _page;

        public UsageCardContent(UsageDetailsPage page, ClaudeUsageSnapshot snapshot)
        {
            _page = page;
            TemplateJson = BuildCardJson(page, snapshot);
            DataJson = "{}";
        }

        public override ICommandResult SubmitForm(string inputs, string data)
        {
            _ = _page._usageService.GetSnapshotAsync(bypassCache: true).GetAwaiter().GetResult();
            _page.RaiseItemsChanged();
            return CommandResult.KeepOpen();
        }
    }

    private static string BuildCardJson(UsageDetailsPage page, ClaudeUsageSnapshot snapshot)
    {
        var bars = new JsonArray();
        AddBar(bars, "5-hour session", snapshot.SessionRemainingPercent, snapshot.SessionResetsAt, first: true);
        if (page.DescribeBurnRate(snapshot) is { } burnNote)
        {
            bars.Add(new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = burnNote,
                ["isSubtle"] = true,
                ["size"] = "Small",
                ["wrap"] = true,
                ["spacing"] = "Small",
            });
        }

        AddBar(bars, "7-day (all models)", snapshot.WeeklyRemainingPercent, snapshot.WeeklyResetsAt);
        foreach (var model in snapshot.PerModelWeekly)
        {
            AddBar(bars, $"7-day — {model.DisplayName}", 100 - model.PercentUsed, model.ResetsAt);
        }

        var columns = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "Column",
                ["width"] = "stretch",
                ["items"] = bars,
            },
        };

        if (page.BuildTrendGraph() is { } trend)
        {
            columns.Add(new JsonObject
            {
                ["type"] = "Column",
                ["width"] = "auto",
                ["spacing"] = "Large",
                ["verticalContentAlignment"] = "Center",
                ["items"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "TextBlock",
                        ["text"] = "Past week",
                        ["weight"] = "Bolder",
                    },
                    new JsonObject
                    {
                        ["type"] = "Image",
                        ["url"] = $"data:image/png;base64,{trend.PngBase64}",
                        ["width"] = $"{TrendGraphWidth}px",
                        ["altText"] = "Area chart of weekly usage",
                    },
                    new JsonObject
                    {
                        ["type"] = "TextBlock",
                        ["text"] = trend.Caption,
                        ["isSubtle"] = true,
                        ["size"] = "Small",
                    },
                },
            });
        }

        var card = new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "ColumnSet",
                    ["columns"] = columns,
                },
            },
            ["actions"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "Action.Submit",
                    ["title"] = "Refresh",
                    ["data"] = new JsonObject { ["action"] = "refresh" },
                },
            },
        };

        return card.ToJsonString();
    }

    private static void AddBar(JsonArray bars, string label, double remainingPercent, DateTimeOffset resetsAt, bool first = false)
    {
        var remaining = (int)Math.Round(Math.Clamp(remainingPercent, 0, 100));
        var filledChars = Math.Clamp(remaining * BarWidthChars / 100, 0, BarWidthChars);
        var bar = new string('▰', filledChars) + new string('▱', BarWidthChars - filledChars);

        bars.Add(new JsonObject
        {
            ["type"] = "TextBlock",
            ["text"] = label,
            ["weight"] = "Bolder",
            ["wrap"] = true,
            ["spacing"] = first ? "None" : "Medium",
        });
        bars.Add(new JsonObject
        {
            ["type"] = "RichTextBlock",
            ["spacing"] = "Small",
            ["inlines"] = new JsonArray
            {
                new JsonObject { ["type"] = "TextRun", ["text"] = bar, ["fontType"] = "Monospace" },
                new JsonObject { ["type"] = "TextRun", ["text"] = $" {remaining}% left", ["weight"] = "Bolder" },
                new JsonObject { ["type"] = "TextRun", ["text"] = $" · resets {resetsAt.ToLocalTime():t}", ["isSubtle"] = true },
            },
        });
    }

    /// <summary>Projects when the session hits 0% from the last ~90 minutes of samples.</summary>
    private string? DescribeBurnRate(ClaudeUsageSnapshot snapshot)
    {
        var points = _usageService.History.Load(TimeSpan.FromMinutes(90));
        if (points.Count < 2)
        {
            return null;
        }

        var oldest = points[0];
        var newest = points[^1];
        var elapsedHours = (newest.Timestamp - oldest.Timestamp).TotalHours;
        if (elapsedHours < 0.25)
        {
            return null; // Not enough spread to say anything credible.
        }

        var burnPerHour = (oldest.SessionRemainingPercent - newest.SessionRemainingPercent) / elapsedHours;
        if (burnPerHour < 1)
        {
            return null; // Flat or recovering — an estimate would be noise.
        }

        var hoursLeft = snapshot.SessionRemainingPercent / burnPerHour;
        var emptyAt = DateTimeOffset.UtcNow.AddHours(hoursLeft);

        return emptyAt >= snapshot.SessionResetsAt
            ? "At the current pace, your session lasts until it resets."
            : $"At the current pace (≈{burnPerHour:F0}%/h), the session runs out around {emptyAt.ToLocalTime():t}.";
    }

    private const int TrendGraphWidth = 240;
    private const int TrendGraphHeight = 100;

    /// <summary>
    /// Weekly trend graph: usage % over the past 7 days from the local history log,
    /// averaged into ~half-hour-to-2-hour buckets and rendered as a PNG area chart.
    /// Null until enough history has accumulated to mean anything.
    /// </summary>
    private (string PngBase64, string Caption)? BuildTrendGraph()
    {
        var points = _usageService.History.Load(TimeSpan.FromDays(7));
        if (points.Count < 3)
        {
            return null;
        }

        var first = points[0].Timestamp;
        var span = points[^1].Timestamp - first;
        if (span < TimeSpan.FromHours(6))
        {
            return null;
        }

        // Average samples into buckets so the line shows the trend, not poll noise.
        var bucketCount = (int)Math.Clamp(span.TotalMinutes / 30, 12, 84);
        var sums = new double[bucketCount];
        var counts = new int[bucketCount];
        foreach (var point in points)
        {
            var index = Math.Min((int)((point.Timestamp - first).Ticks * bucketCount / span.Ticks), bucketCount - 1);
            sums[index] += Math.Clamp(100 - point.WeeklyRemainingPercent, 0, 100);
            counts[index]++;
        }

        var averages = Enumerable.Range(0, bucketCount)
            .Where(i => counts[i] > 0)
            .Select(i => (X: (i + 0.5) / bucketCount, Used: sums[i] / counts[i]))
            .ToList();
        if (averages.Count < 2)
        {
            return null;
        }

        // Zero baseline, headroom above the peak so the line never kisses the frame.
        var yMax = Math.Max(10, averages.Max(a => a.Used) * 1.15);
        var normalized = averages.Select(a => (a.X, a.Used / yMax)).ToList();

        var png = TrendChartRenderer.Render(normalized, TrendGraphWidth, TrendGraphHeight);
        if (png is null)
        {
            return null;
        }

        var caption = span >= TimeSpan.FromDays(6.5)
            ? "usage over the past week"
            : $"usage since {first.ToLocalTime():MMM d}";
        return (Convert.ToBase64String(png), caption);
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
