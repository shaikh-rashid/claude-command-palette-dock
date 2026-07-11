# 📝 TODO

Feature ideas and task tracking. Move items between sections as they progress;
when something ships, add it to [CHANGELOG.md](CHANGELOG.md) under the release
that includes it.

Legend: `[ ]` planned · `[~]` in progress · `[x]` done

## 🔥 Now

(nothing in flight)

## ⏭️ Next

- [ ] Publish signed releases (GitHub Actions build + MSIX artifact) so users don't need the SDKs

## 💡 Ideas

- [ ] WinGet / Microsoft Store distribution
- [ ] Localize strings (en → more languages)

## ✅ Done

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
