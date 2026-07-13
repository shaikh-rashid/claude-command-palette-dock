using System.Globalization;
using System.Text.Json.Nodes;
using ClaudeUsageDock.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;

namespace ClaudeUsageDock.Pages;

/// <summary>
/// Full-page view shown when the dock tile (or the top-level command) is opened,
/// organized as three tabbed sections switched by a button strip at the top:
///
///   - Usage — the limit bars (session, weekly, per-model) with relative reset
///     times, the burn-rate note, and account facts.
///   - Breakdown — what's using the limits, derived from the local history log:
///     last-24h weekly burn, daily average, busiest period, pace projection,
///     per-model weekly caps, and the when-do-you-use-Claude weekly heatmap.
///   - Heatmap — the monthly GitHub-style calendar (five Monday-aligned week
///     rows × weekday columns, month labels, WK totals column).
///
/// AdaptiveCards has no native tab control, so the strip is three Action.Submit
/// buttons; SubmitForm records the chosen tab and re-renders the page. Refresh
/// lives in the page's command bar (Enter / Ctrl+R) and account configuration
/// under More (Ctrl+K).
/// </summary>
internal sealed class UsageDetailsPage : ContentPage
{
    /// <summary>Width of each unicode progress bar; the bars own the row now that the heatmap has its own tab.</summary>
    private const int BarWidthChars = 24;

    private const string TabUsage = "usage";
    private const string TabBreakdown = "breakdown";
    private const string TabHeatmap = "heatmap";

    private readonly ClaudeUsageService _usageService;
    private readonly UsageProfile _profile;
    private readonly string _heading;
    private string _activeTab = TabUsage;

    public UsageDetailsPage(ClaudeUsageService usageService, UsageProfile profile, ICommand settingsCommand)
    {
        _usageService = usageService;
        _profile = profile;
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
                Icon = new IconInfo(""), // Segoe Fluent refresh arrows
                RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, alt: false, shift: false, win: false, vkey: VirtualKey.R, scanCode: 0),
            },
            new CommandContextItem(settingsCommand)
            {
                Title = "Configure accounts",
                Icon = new IconInfo(""), // Segoe Fluent gear
            },
        ];
    }

    /// <summary>
    /// Renders the page: a markdown heading block, then the tabbed usage card.
    /// Failures render as markdown only. CmdPal calls this on open and again
    /// whenever RaiseItemsChanged fires (after a Refresh or a tab switch).
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
    /// The tabbed usage card. Tab switches arrive through SubmitForm (the strip's
    /// Action.Submit buttons); Refresh lives in the page's command bar.
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
            try
            {
                var tab = JsonNode.Parse(data)?["tab"]?.GetValue<string>();
                if (tab is TabUsage or TabBreakdown or TabHeatmap && tab != _page._activeTab)
                {
                    _page._activeTab = tab;
                    _page.RaiseItemsChanged();
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // A submit without tab data (shouldn't happen) — just keep the page open.
            }

            return CommandResult.KeepOpen();
        }
    }

    /// <summary>
    /// Assembles the AdaptiveCard JSON: the tab strip, then only the active
    /// section (each switch re-renders, so hidden tabs cost nothing). Built with
    /// JsonObject rather than string templates so values never need hand-escaping.
    /// </summary>
    private static string BuildCardJson(UsageDetailsPage page, ClaudeUsageSnapshot snapshot)
    {
        var body = new JsonArray { BuildTabStrip(page._activeTab) };

        switch (page._activeTab)
        {
            case TabBreakdown:
                page.AppendBreakdownSection(body, snapshot);
                break;
            case TabHeatmap:
                page.AppendHeatmapSection(body);
                break;
            default:
                page.AppendUsageSection(body, snapshot);
                break;
        }

        var card = new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = body,
        };

        return card.ToJsonString();
    }

    /// <summary>The tab strip: three submit buttons, the active one accented.</summary>
    private static JsonObject BuildTabStrip(string activeTab)
    {
        var actions = new JsonArray();
        foreach (var (id, title) in new[] { (TabUsage, "Usage"), (TabBreakdown, "Breakdown"), (TabHeatmap, "Heatmap") })
        {
            var action = new JsonObject
            {
                ["type"] = "Action.Submit",
                ["title"] = title,
                ["data"] = new JsonObject { ["tab"] = id },
            };
            if (id == activeTab)
            {
                action["style"] = "positive";
            }

            actions.Add(action);
        }

        return new JsonObject
        {
            ["type"] = "ActionSet",
            ["actions"] = actions,
        };
    }

    // ---------------------------------------------------------------- Usage tab

    /// <summary>Limit bars with relative reset times, the burn note, and account facts.</summary>
    private void AppendUsageSection(JsonArray body, ClaudeUsageSnapshot snapshot)
    {
        AddBar(body, "5-hour session", snapshot.SessionRemainingPercent, snapshot.SessionResetsAt);
        if (DescribeBurnRate(snapshot) is { } burnNote)
        {
            body.Add(SubtleText(burnNote, spacing: "Small"));
        }

        AddBar(body, "7-day (all models)", snapshot.WeeklyRemainingPercent, snapshot.WeeklyResetsAt);
        foreach (var model in snapshot.PerModelWeekly)
        {
            AddBar(body, $"7-day — {model.DisplayName}", 100 - model.PercentUsed, model.ResetsAt);
        }

        body.Add(new JsonObject
        {
            ["type"] = "TextBlock",
            ["text"] = "Account",
            ["weight"] = "Bolder",
            ["spacing"] = "Large",
        });
        body.Add(new JsonObject
        {
            ["type"] = "FactSet",
            ["spacing"] = "Small",
            ["facts"] = new JsonArray
            {
                new JsonObject { ["title"] = "Plan", ["value"] = snapshot.PlanType },
                new JsonObject { ["title"] = "Profile", ["value"] = _profile.Label ?? "Default (Claude Code sign-in)" },
                new JsonObject { ["title"] = "Auth method", ["value"] = "Claude Code OAuth token" },
                new JsonObject { ["title"] = "Last checked", ["value"] = snapshot.RetrievedAt.ToLocalTime().ToString("t") },
            },
        });
    }

    /// <summary>
    /// One labeled progress bar: a bold label TextBlock, then a RichTextBlock
    /// mixing a monospace unicode bar with proportional text (an AdaptiveCard
    /// TextBlock can't change fonts mid-line).
    /// </summary>
    private static void AddBar(JsonArray body, string label, double remainingPercent, DateTimeOffset resetsAt)
    {
        var remaining = (int)Math.Round(Math.Clamp(remainingPercent, 0, 100));
        var filledChars = Math.Clamp(remaining * BarWidthChars / 100, 0, BarWidthChars);
        var bar = new string('▰', filledChars) + new string('▱', BarWidthChars - filledChars);

        body.Add(new JsonObject
        {
            ["type"] = "TextBlock",
            ["text"] = label,
            ["weight"] = "Bolder",
            ["wrap"] = true,
            ["spacing"] = "Medium",
        });
        body.Add(new JsonObject
        {
            ["type"] = "RichTextBlock",
            ["spacing"] = "Small",
            ["inlines"] = new JsonArray
            {
                new JsonObject { ["type"] = "TextRun", ["text"] = bar, ["fontType"] = "Monospace" },
                new JsonObject { ["type"] = "TextRun", ["text"] = $" {remaining}% left", ["weight"] = "Bolder" },
                new JsonObject { ["type"] = "TextRun", ["text"] = $" · {FormatResetsIn(resetsAt)}", ["isSubtle"] = true },
            },
        });
    }

    /// <summary>"resets in 3 h 05 m" — relative like the reference UI, coarser as the horizon grows.</summary>
    private static string FormatResetsIn(DateTimeOffset resetsAt)
    {
        var delta = resetsAt - DateTimeOffset.UtcNow;
        if (delta <= TimeSpan.Zero)
        {
            return "resets soon";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"resets in {Math.Max(1, delta.Minutes)} min";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return $"resets in {(int)delta.TotalHours} h {delta.Minutes:D2} m";
        }

        return $"resets in {(int)delta.TotalDays} d {delta.Hours} h";
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

    // ------------------------------------------------------------ Breakdown tab

    /// <summary>
    /// "What's using your limits?" — statistics derived from the local history
    /// log (nothing here comes from the API beyond the per-model caps), plus the
    /// weekly time-of-day heatmap showing when the quota gets spent.
    /// </summary>
    private void AppendBreakdownSection(JsonArray body, ClaudeUsageSnapshot snapshot)
    {
        body.Add(new JsonObject
        {
            ["type"] = "TextBlock",
            ["text"] = "What's using your limits?",
            ["weight"] = "Bolder",
            ["spacing"] = "Medium",
        });
        body.Add(SubtleText("Approximate, from local samples on this machine — other devices and claude.ai aren't counted.", spacing: "Small"));

        if (ComputeWeeklyBurn() is not { } burn)
        {
            body.Add(SubtleText("Not enough local history yet — statistics appear after a few hours of recorded usage."));
        }
        else
        {
            var facts = new JsonArray
            {
                new JsonObject { ["title"] = "Last 24 h", ["value"] = $"{burn.Last24Hours:F0}% of the weekly quota" },
                new JsonObject { ["title"] = "Daily average", ["value"] = $"{burn.DailyAverage:F0}% of the weekly quota per day" },
            };
            if (BusiestSlotLabel(burn.SlotCells) is { } busiest)
            {
                facts.Add(new JsonObject { ["title"] = "Busiest period", ["value"] = busiest });
            }

            facts.Add(new JsonObject { ["title"] = "Pace", ["value"] = DescribeWeeklyPace(snapshot, burn.DailyAverage) });

            body.Add(new JsonObject
            {
                ["type"] = "FactSet",
                ["spacing"] = "Medium",
                ["facts"] = facts,
            });
        }

        if (snapshot.PerModelWeekly.Count > 0)
        {
            body.Add(new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = "Per-model weekly limits",
                ["weight"] = "Bolder",
                ["spacing"] = "Large",
            });
            foreach (var model in snapshot.PerModelWeekly)
            {
                body.Add(SubtleText($"{model.DisplayName} — {model.PercentUsed:F0}% used · {FormatResetsIn(model.ResetsAt)}", spacing: "Small"));
            }
        }

        if (BuildWeeklySlotGraph() is { } trend)
        {
            body.Add(new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = "When you use Claude — past week",
                ["weight"] = "Bolder",
                ["spacing"] = "Large",
            });
            body.Add(new JsonObject
            {
                ["type"] = "Image",
                ["url"] = $"data:image/png;base64,{trend.PngBase64}",
                ["width"] = $"{TrendChartRenderer.WeeklyDisplayWidth}px",
                ["altText"] = "Heatmap of weekly usage by day and time",
                ["spacing"] = "Small",
            });
            body.Add(SubtleText(trend.Caption, spacing: "Small"));
        }
    }

    /// <summary>
    /// Weekly-quota burn statistics from the past 7 days of history. Each pair of
    /// consecutive samples attributes the quota burned between them to the slot at
    /// their midpoint (same attribution the heatmaps use). Null until the log
    /// spans enough time to mean anything.
    /// </summary>
    private (double Last24Hours, double DailyAverage, double[,] SlotCells)? ComputeWeeklyBurn()
    {
        var points = _usageService.History.Load(TimeSpan.FromDays(7));
        if (points.Count < 3)
        {
            return null;
        }

        var span = points[^1].Timestamp - points[0].Timestamp;
        if (span < TimeSpan.FromHours(6))
        {
            return null;
        }

        var dayAgo = DateTimeOffset.UtcNow - TimeSpan.FromDays(1);
        var last24 = 0.0;
        var total = 0.0;
        var cells = new double[TrendChartRenderer.WeeklyRows, TrendChartRenderer.WeeklyColumns];
        for (var i = 1; i < points.Count; i++)
        {
            var burned = points[i - 1].WeeklyRemainingPercent - points[i].WeeklyRemainingPercent;
            if (burned <= 0)
            {
                continue; // Idle, or the weekly limit reset between samples.
            }

            var midpoint = points[i - 1].Timestamp + (points[i].Timestamp - points[i - 1].Timestamp) / 2;
            total += burned;
            if (midpoint >= dayAgo)
            {
                last24 += burned;
            }

            var local = midpoint.ToLocalTime();
            var row = ((int)local.DayOfWeek + 6) % 7; // DayOfWeek starts on Sunday; the grid starts on Monday
            cells[row, local.Hour / 3] += burned;
        }

        var dailyAverage = total / Math.Max(span.TotalDays, 1);
        return (last24, dailyAverage, cells);
    }

    /// <summary>The busiest weekday + 3-hour slot, e.g. "Tue 12:00–15:00"; null when nothing registered.</summary>
    private static string? BusiestSlotLabel(double[,] cells)
    {
        var best = 0.0;
        var bestRow = 0;
        var bestCol = 0;
        for (var row = 0; row < TrendChartRenderer.WeeklyRows; row++)
        {
            for (var col = 0; col < TrendChartRenderer.WeeklyColumns; col++)
            {
                if (cells[row, col] > best)
                {
                    best = cells[row, col];
                    bestRow = row;
                    bestCol = col;
                }
            }
        }

        if (best <= 0)
        {
            return null;
        }

        var day = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[(bestRow + 1) % 7]; // grid row 0 = Monday
        return $"{day} {bestCol * 3:D2}:00–{bestCol * 3 + 3:D2}:00";
    }

    /// <summary>Extends the daily average to the weekly cap: run-out day, or all-clear.</summary>
    private static string DescribeWeeklyPace(ClaudeUsageSnapshot snapshot, double dailyAverage)
    {
        if (dailyAverage < 0.5)
        {
            return "barely denting the weekly limit";
        }

        var daysLeft = snapshot.WeeklyRemainingPercent / dailyAverage;
        var emptyAt = DateTimeOffset.Now.AddDays(daysLeft);
        return emptyAt >= snapshot.WeeklyResetsAt.ToLocalTime()
            ? "at this pace, the weekly limit lasts until it resets"
            : $"at this pace, the weekly limit runs out around {emptyAt:ddd h tt}";
    }

    /// <summary>
    /// The when-during-the-week heatmap (weekday rows × 3-hour slots, local time)
    /// for the Breakdown tab. Null until enough history has accumulated.
    /// </summary>
    private (string PngBase64, string Caption)? BuildWeeklySlotGraph()
    {
        if (ComputeWeeklyBurn() is not { } burn)
        {
            return null;
        }

        var points = _usageService.History.Load(TimeSpan.FromDays(7));
        var first = points[0].Timestamp;
        var png = TrendChartRenderer.RenderWeekly(burn.SlotCells);
        var caption = points[^1].Timestamp - first >= TimeSpan.FromDays(6.5)
            ? "usage over the past week"
            : $"usage since {first.ToLocalTime():MMM d}";
        return (Convert.ToBase64String(png), caption);
    }

    // -------------------------------------------------------------- Heatmap tab

    /// <summary>The monthly GitHub-style calendar heatmap with its captions.</summary>
    private void AppendHeatmapSection(JsonArray body)
    {
        if (BuildMonthlyGraph() is not { } trend)
        {
            body.Add(SubtleText("Not enough local history yet — the heatmap appears after a few hours of recorded usage."));
            return;
        }

        body.Add(new JsonObject
        {
            ["type"] = "TextBlock",
            ["text"] = "Past month",
            ["weight"] = "Bolder",
            ["spacing"] = "Medium",
        });
        body.Add(new JsonObject
        {
            ["type"] = "Image",
            ["url"] = $"data:image/png;base64,{trend.PngBase64}",
            ["width"] = $"{TrendChartRenderer.MonthDisplayWidth}px",
            ["altText"] = "Heatmap of daily usage over the past five weeks, with week totals",
            ["spacing"] = "Small",
        });
        body.Add(SubtleText(trend.Caption, spacing: "Small"));
        body.Add(SubtleText(trend.MonthTotal, spacing: "None"));
    }

    /// <summary>
    /// Monthly trend graph: a GitHub-style calendar of the past five Monday-aligned
    /// weeks from the local history log — day cells, month labels where a month
    /// begins, and a WK column of week totals. Weekly-quota burn between
    /// consecutive samples is attributed to the day at their midpoint, local time.
    /// Null until enough history has accumulated to mean anything.
    /// </summary>
    private (string PngBase64, string Caption, string MonthTotal)? BuildMonthlyGraph()
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
        var startMonday = currentMonday.AddDays(-7 * (TrendChartRenderer.MonthWeekRows - 1));

        var dayCells = new double[TrendChartRenderer.MonthWeekRows, TrendChartRenderer.MonthDayColumns];
        var weekTotals = new double[TrendChartRenderer.MonthWeekRows];
        for (var row = 0; row < TrendChartRenderer.MonthWeekRows; row++)
        {
            for (var col = 0; col < TrendChartRenderer.MonthDayColumns; col++)
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
            if (dayIndex < 0 || dayIndex >= TrendChartRenderer.MonthWeekRows * TrendChartRenderer.MonthDayColumns)
            {
                continue;
            }

            dayCells[dayIndex / 7, dayIndex % 7] += burned;
            weekTotals[dayIndex / 7] += burned;
            monthTotal += burned;
        }

        // Month labels where a month begins (and on the first row), GitHub style.
        // Invariant culture keeps the abbreviations inside the renderer's typeface.
        var rowLabels = new string?[TrendChartRenderer.MonthWeekRows];
        for (var row = 0; row < TrendChartRenderer.MonthWeekRows; row++)
        {
            var monday = startMonday.AddDays(row * 7);
            if (row == 0 || monday.Month != startMonday.AddDays((row - 1) * 7).Month)
            {
                rowLabels[row] = monday.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
            }
        }

        var png = TrendChartRenderer.RenderMonthly(dayCells, weekTotals, rowLabels);
        var caption = span >= TimeSpan.FromDays(27)
            ? "usage over the past month"
            : $"usage since {first.ToLocalTime():MMM d}";
        return (Convert.ToBase64String(png), caption, $"month ≈ {monthTotal:F0}% of one week's quota");
    }

    // ------------------------------------------------------------------ helpers

    private static JsonObject SubtleText(string text, string spacing = "Medium") => new()
    {
        ["type"] = "TextBlock",
        ["text"] = text,
        ["isSubtle"] = true,
        ["size"] = "Small",
        ["wrap"] = true,
        ["spacing"] = spacing,
    };

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
