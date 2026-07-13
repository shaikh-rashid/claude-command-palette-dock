using System.Globalization;
using System.Text.Json.Nodes;
using ClaudeUsageDock.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;

namespace ClaudeUsageDock.Pages;

/// <summary>
/// Full-page breakdown shown when the dock tile (or the top-level command) is opened:
/// session limit, weekly limit, and any per-model weekly limits as bar charts, with
/// the monthly usage heatmap alongside them. Refresh lives in the page's command bar
/// (Enter / Ctrl+R) and account configuration under More (Ctrl+K).
/// </summary>
internal sealed class UsageDetailsPage : ContentPage
{
    /// <summary>Width of each unicode progress bar; sized so bars and heatmap fit side by side.</summary>
    private const int BarWidthChars = 16;

    private readonly ClaudeUsageService _usageService;
    private readonly string _heading;

    public UsageDetailsPage(ClaudeUsageService usageService, UsageProfile profile, ICommand settingsCommand)
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

        Commands =
        [
            new CommandContextItem(
                title: "Refresh",
                subtitle: string.Empty,
                name: "Refresh",
                result: CommandResult.KeepOpen(),
                action: () =>
                {
                    _ = _usageService.GetSnapshotAsync(bypassCache: true).GetAwaiter().GetResult();
                    RaiseItemsChanged();
                })
            {
                Icon = new IconInfo(""), // Segoe Fluent refresh arrows
                RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, alt: false, shift: false, win: false, vkey: VirtualKey.R, scanCode: 0),
            },
            new CommandContextItem(settingsCommand)
            {
                Title = "Configure accounts",
                Icon = new IconInfo(""), // Segoe Fluent gear
            },
        ];
    }

    /// <summary>
    /// Renders the page: a markdown heading block, then the usage card. Failures
    /// render as markdown only. CmdPal calls this on open and again whenever
    /// RaiseItemsChanged fires (i.e. after a Refresh).
    /// </summary>
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
    /// The usage card: bars in the left column, the monthly heatmap in the right
    /// one (an AdaptiveCard ColumnSet — markdown can't put block content side by
    /// side). It has no actions: Refresh lives in the page's command bar.
    /// </summary>
    private sealed partial class UsageCardContent : FormContent
    {
        public UsageCardContent(UsageDetailsPage page, ClaudeUsageSnapshot snapshot)
        {
            TemplateJson = BuildCardJson(page, snapshot);
            DataJson = "{}";
        }

        public override ICommandResult SubmitForm(string inputs, string data) => CommandResult.KeepOpen();
    }

    /// <summary>
    /// Assembles the AdaptiveCard JSON: a ColumnSet with the bars column
    /// (stretch) and, once there's enough history, the heatmap column (auto).
    /// Built with JsonObject rather than string templates so values never need
    /// hand-escaping.
    /// </summary>
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
                        ["text"] = "Past month",
                        ["weight"] = "Bolder",
                    },
                    new JsonObject
                    {
                        ["type"] = "Image",
                        ["url"] = $"data:image/png;base64,{trend.PngBase64}",
                        ["width"] = $"{TrendChartRenderer.DisplayWidth}px",
                        ["altText"] = "Heatmap of daily usage over the past five weeks, with week totals",
                    },
                    new JsonObject
                    {
                        ["type"] = "TextBlock",
                        ["text"] = trend.Caption,
                        ["isSubtle"] = true,
                        ["size"] = "Small",
                    },
                    new JsonObject
                    {
                        ["type"] = "TextBlock",
                        ["text"] = trend.MonthTotal,
                        ["isSubtle"] = true,
                        ["size"] = "Small",
                        ["spacing"] = "None",
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
        };

        return card.ToJsonString();
    }

    /// <summary>
    /// One labeled progress bar: a bold label TextBlock, then a RichTextBlock
    /// mixing a monospace unicode bar with proportional text (an AdaptiveCard
    /// TextBlock can't change fonts mid-line).
    /// </summary>
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

    /// <summary>
    /// Monthly usage heatmap: a GitHub-style calendar of when the weekly quota was
    /// consumed over the past five Monday-aligned weeks, from the local history log.
    /// Day cells show daily burn, the WK column shows week totals, and the month
    /// total goes in the second caption line — day, week, and month usage in one
    /// graphic. Each pair of consecutive samples attributes the quota burned
    /// between them to the local calendar day at their midpoint. Null until enough
    /// history has accumulated to mean anything.
    /// </summary>
    private (string PngBase64, string Caption, string MonthTotal)? BuildTrendGraph()
    {
        var points = _usageService.History.Load(TimeSpan.FromDays(36));
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

        var today = DateTimeOffset.Now.Date;
        var currentMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); // DayOfWeek starts on Sunday; the grid starts on Monday
        var startMonday = currentMonday.AddDays(-7 * (TrendChartRenderer.WeekRows - 1));

        var dayCells = new double[TrendChartRenderer.WeekRows, TrendChartRenderer.DayColumns];
        var weekTotals = new double[TrendChartRenderer.WeekRows];
        for (var row = 0; row < TrendChartRenderer.WeekRows; row++)
        {
            for (var col = 0; col < TrendChartRenderer.DayColumns; col++)
            {
                if (startMonday.AddDays(row * 7 + col) > today)
                {
                    dayCells[row, col] = TrendChartRenderer.NotApplicable;
                }
            }
        }

        var monthTotal = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            var burned = points[i - 1].WeeklyRemainingPercent - points[i].WeeklyRemainingPercent;
            if (burned <= 0)
            {
                continue; // Idle, or the weekly limit reset between samples.
            }

            var midpoint = (points[i - 1].Timestamp + (points[i].Timestamp - points[i - 1].Timestamp) / 2).ToLocalTime().Date;
            var dayIndex = (midpoint - startMonday).Days;
            if (dayIndex < 0 || dayIndex >= TrendChartRenderer.WeekRows * TrendChartRenderer.DayColumns)
            {
                continue;
            }

            dayCells[dayIndex / 7, dayIndex % 7] += burned;
            weekTotals[dayIndex / 7] += burned;
            monthTotal += burned;
        }

        // Month labels where a month begins (and on the first row), GitHub style.
        // Invariant culture keeps the abbreviations inside the renderer's typeface.
        var rowLabels = new string?[TrendChartRenderer.WeekRows];
        for (var row = 0; row < TrendChartRenderer.WeekRows; row++)
        {
            var monday = startMonday.AddDays(row * 7);
            if (row == 0 || monday.Month != startMonday.AddDays((row - 1) * 7).Month)
            {
                rowLabels[row] = monday.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
            }
        }

        var png = TrendChartRenderer.Render(dayCells, weekTotals, rowLabels);
        var caption = span >= TimeSpan.FromDays(27)
            ? "usage over the past month"
            : $"usage since {first.ToLocalTime():MMM d}";
        return (Convert.ToBase64String(png), caption, $"month ≈ {monthTotal:F0}% of one week's quota");
    }

    /// <summary>Long-form failure text with what to do about it; the dock tile shows the short versions.</summary>
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
