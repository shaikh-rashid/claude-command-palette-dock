# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.8.1] - 2026-07-12

### Changed

- The heatmap is back to the weekly design from 0.7.0 (weekday rows × 3-hour-slot columns, "Past week"), reverting 0.8.0's monthly calendar layout — the older look was preferred. The rest of 0.8.0 (command-bar Refresh, "Configure accounts" in More) is unchanged, and history retention stays at 36 days so a future multi-week view starts with data already collected.

## [0.8.0] - 2026-07-12

### Changed

- The heatmap now covers the past month instead of the past week, laid out as a GitHub-style calendar: five Monday-aligned week rows by weekday columns (headed M T W T F S S), month labels where a month begins, a separated WK column of week totals, and a month-total line under the caption — day, week, and month usage in one graphic. Days that haven't happened yet aren't drawn. History retention grew from 8 to 36 days to feed it (the log stays local and prunes itself).
- The Refresh button moved out of the usage card into the page's command bar, like native Command Palette pages: primary command on Enter with a Ctrl+R shortcut. A "Configure accounts" entry (the extension's settings page) now sits in the More menu (Ctrl+K).

## [0.7.0] - 2026-07-12

### Changed

- The weekly trend graph is now a GitHub-style contribution heatmap instead of an area chart, in the same navy/teal/cyan scheme: weekday rows (labeled M/W/F) by 3-hour-slot columns (labeled 06/12/18, local time), with rounded cells stepping through a four-level teal→cyan intensity ramp relative to the busiest slot. Cell values are the share of the weekly quota burned in that slot, attributed from consecutive history samples, so the graph shows *when* you use Claude rather than the cumulative used-percent curve.

## [0.6.1] - 2026-07-12

### Added

- Security checks in CI on both platforms, running on pushes to `main` and on PRs/MRs: a full-history secret scan (gitleaks via the new `.github/workflows/security.yml` on GitHub; GitLab's built-in Secret Detection template on GitLab) and a `dotnet list package --vulnerable --include-transitive` NuGet audit. The GitHub audit also runs on a weekly schedule so advisories published between pushes still surface. The GitLab pipeline now runs on default-branch and merge-request pipelines in addition to release tags. Both checks verified locally: 11 commits scanned with no leaks, no vulnerable packages.

## [0.6.0] - 2026-07-12

### Changed

- The weekly usage trend on the details page is now a real area chart (dark panel, teal fill, cyan line) instead of a unicode sparkline, and it sits beside the usage bars instead of underneath them. The chart is rasterized in-process to a PNG (new `TrendChartRenderer`, no drawing-library dependency) and embedded as a data URI in the page's AdaptiveCard; the bars, burn-rate note, and Refresh button moved into the same card so the two-column layout works. It appears once ~6 hours of history has accumulated and is captioned "usage since …" until a full week is logged.

## [0.5.1] - 2026-07-11

### Added

- GitLab is now the primary repository (`git@gitlab.com:shaikh.rashid/claude-command-palette-dock.git`), with GitHub kept as a mirror. `origin` is configured with two push URLs (GitLab + GitHub), so a single `git push origin` updates both.
- `.gitlab-ci.yml`: builds, packs, and signs the MSIX on a `vX.Y.Z` tag push, same as the GitHub workflow, but as a two-stage pipeline — a Windows job (GitLab.com shared runner) does the build and uploads artifacts to the project's generic package registry, then a Linux job creates the actual GitLab Release referencing those files. GitLab Releases can't have files attached directly the way GitHub's can, hence the two stages.

### Changed

- Both release pipelines now require the signing certificate secret to be set independently — GitHub repo secrets and GitLab CI/CD variables don't share storage. Documented in INSTALL.md's "Publishing a release" section.

## [0.5.0] - 2026-07-11

### Added

- Signed release publishing: `.github/workflows/release.yml` builds, packs, and signs the MSIX on a `vX.Y.Z` tag push and attaches `ClaudeUsageDock.msix` plus the public `ClaudeUsageDock-Release.cer` to a GitHub Release, with release notes generated from that version's CHANGELOG.md section (the build fails if the section is missing). The workflow verifies the pushed tag matches the `VERSION` file before doing anything else.
- `scripts/generate-release-cert.ps1`: one-time local setup that creates the persistent self-signed certificate CI signs every release with, so a user who trusts it once never has to re-trust a new certificate on a later update.
- INSTALL.md now documents installing a prebuilt release (no SDKs required — download, trust the certificate once, `Add-AppxPackage`) as the primary path, alongside a "Publishing a release" section for maintainers.

### Changed

- Extracted the MSIX staging/pack/sign logic shared by local dev builds and CI releases into `scripts/BuildTools.ps1`, so a bug like the earlier `Assets\Assets` nesting issue can only exist in one place instead of silently diverging between the two. `build-and-install.ps1` now dot-sources it instead of duplicating the logic inline.
- Both `build-and-install.ps1` and the CI release script now check that the exact required Windows SDK platform (`10.0.26100.0`) is installed *before* running `dotnet publish`, failing with a direct, actionable message instead of the opaque CsWinRT/NETSDK1140 error chain this project hit earlier.
- Fixed INSTALL.md's quickstart `winget` command, which recommended installing Windows SDK `10.0.28000` — the project actually requires `10.0.26100` to match the csproj's `TargetFramework`; the two are not interchangeable.

## [0.4.0] - 2026-07-11

### Added

- Multiple accounts: two additional profiles can be configured from the extension's Settings page (a label and a `.credentials.json` path each — Claude Code itself only keeps one, so these point at files you or a script save separately). Each configured profile gets its own dock tile, its own labeled "Claude Usage Dock" command, and its own local history log, refreshed independently. The default profile's ids, titles, and history filename are unchanged, so existing single-account setups aren't affected.

### Changed

- `ClaudeUsageService` and `UsageHistoryStore` now take the credentials path / history filename as constructor parameters instead of hardcoding Claude Code's default location, so multiple instances can run side by side without clobbering each other's cache, backoff state, or on-disk history.

## [0.3.0] - 2026-07-11

### Added

- Automatic token refresh: when the stored access token has expired, the extension exchanges `refreshToken` at Anthropic's token endpoint (the same OAuth client as Claude Code) and writes the rotated tokens back to `.credentials.json` atomically, preserving all other fields. If the write-back fails, refreshed tokens are kept in memory so the extension keeps working; refresh attempts are rate-limited to one per 5 minutes.
- Low-quota toast notification: a Windows notification fires the first time the session drops below the alert threshold (re-arms after the session resets). Can be turned off in the extension's settings.
- Burn-rate estimate on the details page: projects when the session hits 0% from the last ~90 minutes of samples, or notes that the session will last until reset. Hidden when usage is flat or history is too thin.
- Weekly usage sparkline on the details page (6-hour buckets over 7 days), backed by a new rolling local snapshot log (`usage-history.csv`, ~4-minute sample spacing, 8-day retention, never leaves the machine).

### Changed

- New icons: an original Claude-styled sunburst mark in terracotta replaces the placeholder star, with separate light/dark theme variants and a red exclamation badge for the low-quota alert state.
- "Token expired" messages now say to sign in again, since they only appear after automatic refresh has failed.

## [0.2.1] - 2026-07-11

### Fixed

- Anthropic's usage endpoint rate-limits aggressive polling (HTTP 429), which 0.2.0 made more likely by tying the cache lifetime to the refresh interval. The service now backs off on 429 (honoring `Retry-After`, default 90 s, clamped to 30 s–15 min), serializes concurrent fetches so the dock timer and page opens can't double-request, and keeps showing the last good snapshot for up to 10 minutes during rate limiting or network loss instead of an error page. API polling is also floored at one request per 30 s regardless of the UI refresh setting.
- New "Rate limited — retrying soon" dock message and a details-page explanation for HTTP 429, shown only when no recent snapshot is available to fall back on.

## [0.2.0] - 2026-07-11

### Added

- Settings page (Command Palette → extension settings): configurable dock refresh interval (15 s / 30 s / 1 min / 5 min) and low-quota alert threshold (10/20/30/50%). Stored via CmdPal's JSON settings store; the snapshot cache lifetime now tracks the chosen interval so every dock tick shows fresh data.
- Token-expiry detection: the stored OAuth token's `expiresAt` is checked before calling the API, and a clear message ("Token expired — open Claude Code") replaces the generic 401 error when it's stale.
- The details page now shows your plan type read from Claude Code's local credentials (`subscriptionType`) instead of relying on the API response.

### Fixed

- The Refresh button on the details page now actually bypasses the 45-second snapshot cache and re-renders with fresh data; previously it submitted a form that nothing handled.

## [0.1.3] - 2026-07-11

### Fixed

- The packaged exe was also being indexed as a launchable Start Menu / app-search entry (from the mandatory `uap:VisualElements` block), duplicating the "Claude Usage Dock" search result alongside the real command — with an inert description and no working action, since the exe is a COM extension host, not a standalone app. Added `AppListEntry="none"` to hide it from app listings while keeping the COM server and Command Palette extension registration intact.

## [0.1.2] - 2026-07-11

### Fixed

- Renamed the top-level command from `Claude usage` to `Claude Usage Dock` so it's discoverable by typing the extension's own name into Command Palette search — the previous title didn't share enough characters with "Claude Usage Dock" for fuzzy search to match it, so users who searched the extension name (as documented in the README/INSTALL guide) got no results.

## [0.1.1] - 2026-07-11

### Fixed

- `build-and-install.ps1`: fixed MSIX staging step nesting the `Assets` folder inside itself (`Assets\Assets\...`) instead of merging with the icons already copied from the publish output, which made `makeappx` fail manifest validation for missing logo files.
- `PowerCommandProvider.Dispose()` now correctly `override`s `CommandProvider.Dispose()` instead of hiding it, so the base class's disposal logic also runs.

### Changed

- Renamed the project from `ClaudePowerCommand` to `ClaudeUsageDock`: folder, assembly, namespace, MSIX identity (`ClaudeUsageDock` / `CN=ClaudeUsageDock-Dev`), executable, and debug file names (`%TEMP%\claude-usage-dock.*`). If a package was installed under the old identity, remove it with `Get-AppxPackage -Name ClaudePowerCommand | Remove-AppxPackage`.

## [0.1.0] - 2026-07-11

### Added

- Initial PowerToys Command Palette extension scaffold (`ClaudeUsageDock`): MSIX-packaged COM extension host with `PowerCommandExtension` and `PowerCommandProvider`.
- Dock band (`UsageDockBand`) showing 5-hour session % remaining, weekly % and session reset time, auto-refreshing every 30 seconds.
- Usage details page (`UsageDetailsPage`) with progress bars for session, 7-day all-model, and per-model weekly limits, plus a Refresh action.
- `ClaudeUsageService`: reads the Claude Code OAuth token from `%USERPROFILE%\.claude\.credentials.json` and queries `https://api.anthropic.com/api/oauth/usage` with a 45-second cache.
- Low-quota alert icon when the session drops below 20% remaining.
- Opt-in debug logging to `%TEMP%\claude-usage-dock.log` (flag file `%TEMP%\claude-usage-dock.debug`).
- `build-and-install.ps1`: publish → MSIX pack → self-signed dev-cert sign → sideload → restart Command Palette.
