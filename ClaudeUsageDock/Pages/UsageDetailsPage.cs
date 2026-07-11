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

    public UsageDetailsPage(ClaudeUsageService usageService)
    {
        _usageService = usageService;
        Id = "claudeusagedock.page.usage";
        Name = "Claude Usage";
        Title = "Claude usage";
        Icon = IconHelpers.FromRelativePath("Assets\\icons\\claude-mark.svg");
    }

    public override IContent[] GetContent()
    {
        var result = _usageService.GetSnapshotAsync(bypassCache: false).GetAwaiter().GetResult();

        var refreshForm = new FormContent
        {
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
            """,
            DataJson = "{}",
        };

        return [new MarkdownContent(Render(result)), refreshForm];
    }

    private string Render(UsageFetchResult result)
    {
        if (result.Outcome != UsageFetchOutcome.Success || result.Snapshot is null)
        {
            return $"# Claude usage\n\n{DescribeFailure(result)}";
        }

        var snapshot = result.Snapshot;
        var text = new StringBuilder();
        text.AppendLine("# Claude usage");
        text.AppendLine();
        text.AppendLine($"Plan: **{snapshot.PlanType}** · last checked {snapshot.RetrievedAt.ToLocalTime():t}");
        text.AppendLine();

        AppendBar(text, "5-hour session", snapshot.SessionRemainingPercent, snapshot.SessionResetsAt);
        AppendBar(text, "7-day (all models)", snapshot.WeeklyRemainingPercent, snapshot.WeeklyResetsAt);

        foreach (var model in snapshot.PerModelWeekly)
        {
            AppendBar(text, $"7-day — {model.DisplayName}", 100 - model.PercentUsed, model.ResetsAt);
        }

        return text.ToString();
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
        UsageFetchOutcome.RequestFailed => $"Anthropic's usage API returned an error (status {result.StatusCode}).",
        UsageFetchOutcome.Offline => "Couldn't reach Anthropic — check your network connection.",
        UsageFetchOutcome.UnexpectedResponse => "Got a response that didn't look like usage data.",
        _ => "Couldn't fetch usage right now.",
    };
}
