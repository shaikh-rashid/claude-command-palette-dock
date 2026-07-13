# 📝 TODO

Feature ideas and task tracking. Move items between sections as they progress;
when something ships, add it to [CHANGELOG.md](CHANGELOG.md) under the release
that includes it.

Legend: `[ ]` planned · `[~]` in progress · `[x]` done

## 🔥 Now

(nothing in flight)

## ⏭️ Next

- [ ] WinGet / Microsoft Store distribution
- [ ] Localize strings (en → more languages)

## 💡 Ideas

- [ ] Weekly burn projection: extend the session burn-rate estimate to the 7-day cap ("at this pace you'll hit the weekly limit Thursday evening"), since the history log already has everything needed
- [ ] Weekly & per-model alerts: today the toast only fires on the session threshold — add opt-in alerts for the 7-day cap and per-model caps (e.g. Opus), each with its own threshold
- [ ] Configurable dock tile metric: let the tile title show session %, weekly %, or time-to-reset instead of the fixed session-first layout
- [ ] Update notifier: check the GitLab/GitHub Releases feed on a slow cadence and show a subtle "v0.8.0 available" line on the details page (respecting the no-telemetry stance — a plain HTTPS GET, off by default?)
- [ ] Usage statistics page: daily totals, busiest day/slot, current streak, and week-over-week comparison computed from the history log — a natural companion command to the details page
- [ ] Export / open history: a command that reveals `usage-history.csv` (or copies a JSON export) for people who want to chart their own data
- [ ] Quiet hours: suppress toasts during configured hours (e.g. overnight) while the tile keeps updating
- [ ] Longer history window: make retention configurable (up to ~30 days) and let the heatmap aggregate multiple weeks once the data exists (closer to the real GitHub graph)
- [ ] PR/MR build check: a CI job that compiles the extension on pushes and merge requests, so a broken build is caught before a release tag (today only security checks run there)

## ✅ Done

- [x] Reverted the heatmap to the weekly design from v0.7.0 (weekday rows × 3-hour columns) — the monthly calendar layout didn't look as good; kept v0.8.0's command-bar Refresh, Configure-accounts entry, and 36-day retention (v0.8.1)
- [x] Monthly usage heatmap: the graph now covers five Monday-aligned weeks as a GitHub-style calendar — day cells, month labels (JUN/JUL) at month starts, a separated WK week-totals column, and a month-total caption line, so day/week/month usage read from one graphic; history retention extended 8 → 36 days. Refresh moved from a card button to the page command bar (Enter / Ctrl+R) and "Configure accounts" (settings page) added to the More menu (Ctrl+K) (v0.8.0)
- [x] Restyled the weekly trend graph as a GitHub-style contribution heatmap in the existing navy/teal/cyan scheme: weekday rows (M/W/F labels) × 3-hour columns (06/12/18 labels, local time), rounded cells, four-step teal→cyan intensity ramp relative to the busiest slot; cells show when the weekly quota was burned. Labels come from a tiny built-in 5×5 pixel font, still no drawing-library deps (v0.7.0)
- [x] Security & secret checking in CI for both GitHub and GitLab: full-history secret scan (gitleaks action / GitLab Secret Detection template) + `dotnet list package --vulnerable` NuGet audit on main pushes and PRs/MRs, weekly scheduled audit on GitHub. Ran both checks locally — history is clean, no vulnerable packages (v0.6.1)
- [x] Weekly trend graph: replaced the unicode sparkline with a rendered PNG area chart (dark panel, teal fill, cyan line — modeled on the reference mock), shown beside the usage bars instead of underneath them. Details page body moved from pure markdown into an AdaptiveCard ColumnSet to get the side-by-side layout; chart is drawn in-process by the new `TrendChartRenderer` with no added dependencies (v0.6.0)
- [x] Migrate to GitLab as the primary repo, GitHub kept as a mirror: `origin` now points at GitLab with a second push URL for GitHub, so one `git push origin` updates both. Added `.gitlab-ci.yml` (build on a GitLab.com shared Windows runner, then a separate Linux job publishes the GitLab Release via the generic package registry — GitLab can't attach files to a Release directly the way GitHub can). Same signing certificate as the GitHub pipeline; both need the two secrets set independently (documented in INSTALL.md). Neither this nor the GitLab CI pipeline could be exercised end-to-end — no SSH credentials for gitlab.com or github.com in this environment (v0.5.1).
- [x] Publish signed releases: `.github/workflows/release.yml` builds, packs, and signs the MSIX on `vX.Y.Z` tag push and attaches it + the trust certificate to a GitHub Release, using changelog text as release notes (v0.5.0). One-time manual setup still needed before the first real release: run `scripts/generate-release-cert.ps1` and add the two printed values as repo secrets (`RELEASE_PFX_BASE64`, `RELEASE_PFX_PASSWORD`) — documented in INSTALL.md's "Publishing a release" section. I don't have push/secrets access to the GitHub remote from here, so this couldn't be exercised end-to-end.
- [x] Multiple accounts: two additional profiles configurable in Settings (label + credentials file path each), each getting its own dock tile, details command, and local history log (v0.4.0)
- [x] Better icons — original Claude-styled sunburst glyph with light/dark theme variants and an alert badge (v0.3.0)
- [x] Actively refresh a stale token using `refreshToken`, with atomic write-back to the credentials file (v0.3.0)
- [x] Toast notification when session usage crosses the alert threshold, with a settings toggle (v0.3.0)
- [x] Show estimated time until the session hits 0% at current burn rate (v0.3.0)
- [x] History sparkline: rolling local log of snapshots + weekly usage chart on the details page (v0.3.0)
- [x] First successful build + install on a dev machine (2026-07-11; required installing Windows SDK 26100 alongside 28000)
- [x] Verify dock band renders and refreshes in Command Palette
- [x] Verify per-model weekly limits parse correctly against a live API response
- [x] Refresh button on the details page bypasses the cache (AdaptiveCard submit wired to `GetSnapshotAsync(bypassCache: true)`)
- [x] Detect a stale access token via `expiresAt` and surface a clearer message ("Token expired — open Claude Code")
- [x] Configurable low-quota threshold (Settings page: 10/20/30/50%)
- [x] Configurable refresh interval (Settings page: 15 s–5 min; snapshot cache tracks the interval automatically)
- [x] Show plan type from local credentials (`subscriptionType`) on the details page
- [x] Project scaffold: MSIX-packaged Command Palette extension (COM host, provider, manifest)
- [x] `ClaudeUsageService` — credentials read + Anthropic usage API client with caching
- [x] Dock band with live session/weekly percentages and reset time
- [x] Details page with progress bars for session, weekly, and per-model limits
- [x] Low-quota alert icon (< 20% session remaining)
- [x] Opt-in debug logging (`%TEMP%\claude-usage-dock.debug` flag)
- [x] `build-and-install.ps1` — publish → pack → sign → sideload → restart CmdPal
- [x] Git repository, versioning files (VERSION, CHANGELOG, LICENSE), v0.1.0 tag
- [x] User guide README + INSTALL.md
