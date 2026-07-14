using System.Globalization;
using System.Text.Json.Nodes;
using ClaudeUsageDock.Resources;
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
///     times and the burn-rate note.
///   - Breakdown — what's using the limits, derived from the local history log:
///     last-24h weekly burn, daily average, busiest period, pace projection,
///     and per-model weekly caps.
///   - Heatmap — the monthly GitHub-style calendar (five Monday-aligned week
///     rows × weekday columns, month labels, WK totals column, color legend).
///
/// AdaptiveCards has no native tab control, so the strip is three Action.Submit
/// buttons; SubmitForm records the chosen tab and re-renders the page. Refresh
/// lives in the page's command bar (Enter / Ctrl+R) and account configuration
/// under More (Ctrl+K).
/// </summary>
internal sealed class UsageDetailsPage : ContentPage
{
    private const string TabUsage = "usage";
    private const string TabBreakdown = "breakdown";
    private const string TabHeatmap = "heatmap";

    // The busiest-period grid: weekday rows (0 = Monday) by 3-hour slot columns.
    private const int SlotRows = 7;
    private const int SlotColumns = 8;

    private readonly ClaudeUsageService _usageService;
    private readonly string _heading;
    private string _activeTab = TabUsage;

    public UsageDetailsPage(ClaudeUsageService usageService, UsageProfile profile, ICommand settingsCommand)
    {
        _usageService = usageService;
        _heading = profile.Label is null
            ? Strings.Get("Heading_Default")
            : Strings.Format("Heading_WithLabel", profile.Label);

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
                title: Strings.Get("Command_Refresh"),
                subtitle: string.Empty,
                name: Strings.Get("Command_Refresh"),
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
                Title = Strings.Get("Command_ConfigureAccounts"),
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
        var header = $"# {_heading}\n\n{Strings.Format("Header_PlanLine", snapshot.PlanType, snapshot.RetrievedAt.ToLocalTime().ToString("t"))}";
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
        foreach (var (id, title) in new[] { (TabUsage, Strings.Get("Tab_Usage")), (TabBreakdown, Strings.Get("Tab_Breakdown")), (TabHeatmap, Strings.Get("Tab_Heatmap")) })
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

    /// <summary>
    /// Limit bars with relative reset times and the burn note. Plan and
    /// last-checked already live in the page header, so nothing else repeats here.
    /// </summary>
    private void AppendUsageSection(JsonArray body, ClaudeUsageSnapshot snapshot)
    {
        AddBar(body, Strings.Get("Bar_SessionLabel"), snapshot.SessionRemainingPercent, snapshot.SessionResetsAt);
        if (DescribeBurnRate(snapshot) is { } burnNote)
        {
            body.Add(SubtleText(burnNote, spacing: "Small"));
        }

        AddBar(body, Strings.Get("Bar_WeeklyAllModels"), snapshot.WeeklyRemainingPercent, snapshot.WeeklyResetsAt);
        foreach (var model in snapshot.PerModelWeekly)
        {
            AddBar(body, Strings.Format("Bar_WeeklyModel", model.DisplayName), 100 - model.PercentUsed, model.ResetsAt);
        }
    }

    /// <summary>
    /// One usage row: bold label on the left, subtle reset time and bold percent
    /// remaining on the right, and underneath a thin rendered progress bar sized to
    /// what's left, whose color shifts as the remaining quota runs low. The reset
    /// time and percent are two separate auto-width columns (rather than one string
    /// with a manual gap of literal spaces) so the visual gap between them is a
    /// fixed column spacing instead of font-dependent kerning. The text row and the
    /// bar image both sit inside one column fixed at the bar's own render width, so
    /// the card's full (wider) width can't stretch the text past where the bar ends.
    /// </summary>
    private static void AddBar(JsonArray body, string label, double remainingPercent, DateTimeOffset resetsAt)
    {
        var remaining = (int)Math.Round(Math.Clamp(remainingPercent, 0, 100));

        var textRow = new JsonObject
        {
            ["type"] = "ColumnSet",
            ["columns"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "Column",
                    ["width"] = "stretch",
                    ["verticalContentAlignment"] = "Bottom",
                    ["items"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "TextBlock",
                            ["text"] = label,
                            ["weight"] = "Bolder",
                        },
                    },
                },
                new JsonObject
                {
                    ["type"] = "Column",
                    ["width"] = "auto",
                    ["verticalContentAlignment"] = "Bottom",
                    ["items"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "TextBlock",
                            ["text"] = FormatReset(resetsAt),
                            ["isSubtle"] = true,
                            ["horizontalAlignment"] = "Right",
                        },
                    },
                },
                new JsonObject
                {
                    ["type"] = "Column",
                    ["width"] = "auto",
                    ["spacing"] = "Medium",
                    ["verticalContentAlignment"] = "Bottom",
                    ["items"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "TextBlock",
                            ["text"] = $"{remaining}%",
                            ["weight"] = "Bolder",
                        },
                    },
                },
            },
        };

        var barImage = new JsonObject
        {
            ["type"] = "Image",
            ["url"] = $"data:image/png;base64,{Convert.ToBase64String(BarRenderer.Render(remaining))}",
            ["width"] = $"{BarRenderer.DisplayWidth}px",
            ["altText"] = Strings.Format("Bar_AltText", label, remaining),
            ["spacing"] = "Small",
        };

        body.Add(new JsonObject
        {
            ["type"] = "ColumnSet",
            ["spacing"] = "Medium",
            ["columns"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "Column",
                    ["width"] = $"{BarRenderer.DisplayWidth}px",
                    ["items"] = new JsonArray { textRow, barImage },
                },
            },
        });
    }

    /// <summary>
    /// "Resets in 4 hr 39 min" while it's less than a day away, otherwise the
    /// concrete moment ("Resets Sat 2:00 PM") — matching the reference UI.
    /// </summary>
    private static string FormatReset(DateTimeOffset resetsAt)
    {
        var delta = resetsAt - DateTimeOffset.UtcNow;
        if (delta <= TimeSpan.Zero)
        {
            return Strings.Get("Reset_Soon");
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return Strings.Format("Reset_InMinutes", Math.Max(1, (int)delta.TotalMinutes));
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return Strings.Format("Reset_InHoursMinutes", (int)delta.TotalHours, delta.Minutes);
        }

        // Weekday + short time in the user's locale ("Sat 2:00 PM" / "Sa 14:00").
        return Strings.Format("Reset_AtTime", resetsAt.ToLocalTime().ToString("ddd " + CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern));
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
            ? Strings.Get("Burn_LastsUntilReset")
            : Strings.Format("Burn_RunsOutAround", burnPerHour.ToString("F0"), emptyAt.ToLocalTime().ToString("t"));
    }

    // ------------------------------------------------------------ Breakdown tab

    /// <summary>
    /// "What's using your limits?" — statistics derived from the local history
    /// log (nothing here comes from the API beyond the per-model caps).
    /// </summary>
    private void AppendBreakdownSection(JsonArray body, ClaudeUsageSnapshot snapshot)
    {
        body.Add(new JsonObject
        {
            ["type"] = "TextBlock",
            ["text"] = Strings.Get("Breakdown_Title"),
            ["weight"] = "Bolder",
            ["spacing"] = "Medium",
        });
        body.Add(SubtleText(Strings.Get("Breakdown_Disclaimer"), spacing: "Small"));

        if (ComputeWeeklyBurn() is not { } burn)
        {
            body.Add(SubtleText(Strings.Get("Breakdown_NotEnoughHistory")));
        }
        else
        {
            var facts = new JsonArray
            {
                new JsonObject { ["title"] = Strings.Get("Fact_Last24Hours"), ["value"] = Strings.Format("Fact_Last24HoursValue", burn.Last24Hours.ToString("F0")) },
                new JsonObject { ["title"] = Strings.Get("Fact_DailyAverage"), ["value"] = Strings.Format("Fact_DailyAverageValue", burn.DailyAverage.ToString("F0")) },
            };
            if (BusiestSlotLabel(burn.SlotCells) is { } busiest)
            {
                facts.Add(new JsonObject { ["title"] = Strings.Get("Fact_BusiestPeriod"), ["value"] = busiest });
            }

            facts.Add(new JsonObject { ["title"] = Strings.Get("Fact_Pace"), ["value"] = DescribeWeeklyPace(snapshot, burn.DailyAverage) });

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
                ["text"] = Strings.Get("Breakdown_PerModelTitle"),
                ["weight"] = "Bolder",
                ["spacing"] = "Large",
            });
            foreach (var model in snapshot.PerModelWeekly)
            {
                body.Add(SubtleText(Strings.Format("Breakdown_PerModelLine", model.DisplayName, model.PercentUsed.ToString("F0"), FormatReset(model.ResetsAt)), spacing: "Small"));
            }
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
        var cells = new double[SlotRows, SlotColumns];
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
        for (var row = 0; row < SlotRows; row++)
        {
            for (var col = 0; col < SlotColumns; col++)
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
            return Strings.Get("Pace_BarelyDenting");
        }

        var daysLeft = snapshot.WeeklyRemainingPercent / dailyAverage;
        var emptyAt = DateTimeOffset.Now.AddDays(daysLeft);
        return emptyAt >= snapshot.WeeklyResetsAt.ToLocalTime()
            ? Strings.Get("Pace_LastsUntilReset")
            : Strings.Format("Pace_RunsOutAround", emptyAt.ToString("ddd " + CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern));
    }

    // -------------------------------------------------------------- Heatmap tab

    /// <summary>The monthly GitHub-style calendar heatmap with its captions.</summary>
    private void AppendHeatmapSection(JsonArray body)
    {
        if (BuildMonthlyGraph() is not { } trend)
        {
            body.Add(SubtleText(Strings.Get("Heatmap_NotEnoughHistory")));
            return;
        }

        body.Add(new JsonObject
        {
            ["type"] = "TextBlock",
            ["text"] = Strings.Get("Heatmap_Title"),
            ["weight"] = "Bolder",
            ["spacing"] = "Medium",
        });
        body.Add(new JsonObject
        {
            ["type"] = "Image",
            ["url"] = $"data:image/png;base64,{trend.PngBase64}",
            ["width"] = $"{TrendChartRenderer.DisplayWidth}px",
            ["altText"] = Strings.Get("Heatmap_AltText"),
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
            ? Strings.Get("Heatmap_CaptionPastMonth")
            : Strings.Format("Heatmap_CaptionSince", first.ToLocalTime().ToString("MMM d"));
        return (Convert.ToBase64String(png), caption, Strings.Format("Heatmap_MonthTotal", monthTotal.ToString("F0")));
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
        UsageFetchOutcome.NotSignedIn => Strings.Get("Fail_NotSignedIn"),
        UsageFetchOutcome.TokenExpired => Strings.Get("Fail_TokenExpired"),
        UsageFetchOutcome.RateLimited => Strings.Get("Fail_RateLimited"),
        UsageFetchOutcome.RequestFailed => Strings.Format("Fail_RequestFailed", result.StatusCode),
        UsageFetchOutcome.Offline => Strings.Get("Fail_Offline"),
        UsageFetchOutcome.UnexpectedResponse => Strings.Get("Fail_UnexpectedResponse"),
        _ => Strings.Get("Fail_Unknown"),
    };
}
