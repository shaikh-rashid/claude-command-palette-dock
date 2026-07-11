# TODO

Feature ideas and task tracking. Move items between sections as they progress;
when something ships, add it to [CHANGELOG.md](CHANGELOG.md) under the release
that includes it.

Legend: `[ ]` planned · `[~]` in progress · `[x]` done

## Now

- [ ] First successful build + install on a dev machine (needs .NET 9 SDK and Windows 10/11 SDK installed)
- [ ] Verify dock band renders and refreshes in Command Palette 0.11+
- [ ] Verify per-model weekly limits parse correctly against a live API response

## Next

- [ ] Refresh button on the details page should actually bypass the cache (wire the AdaptiveCard submit to `GetSnapshotAsync(bypassCache: true)`)
- [ ] Handle token refresh: read `refreshToken`/`expiresAt` from credentials and surface a clearer message when the access token is stale
- [ ] Configurable low-quota threshold (currently hardcoded at 20%)
- [ ] Configurable refresh interval (currently 30 s dock / 45 s cache)
- [ ] Better icons — replace placeholder star mark with a proper Claude-styled glyph, light/dark theme variants

## Ideas

- [ ] Toast notification when session usage crosses the alert threshold
- [ ] Show estimated time until the session hits 0% at current burn rate
- [ ] History sparkline: keep a rolling local log of snapshots and chart usage over the week
- [ ] Multiple accounts: support more than one credentials file / profile
- [ ] Publish signed releases (GitHub Actions build + MSIX artifact) so users don't need the SDKs
- [ ] WinGet / Microsoft Store distribution
- [ ] Localize strings (en → more languages)

## Done

- [x] Project scaffold: MSIX-packaged Command Palette extension (COM host, provider, manifest)
- [x] `ClaudeUsageService` — credentials read + Anthropic usage API client with caching
- [x] Dock band with live session/weekly percentages and reset time
- [x] Details page with progress bars for session, weekly, and per-model limits
- [x] Low-quota alert icon (< 20% session remaining)
- [x] Opt-in debug logging (`%TEMP%\claude-power-command.debug` flag)
- [x] `build-and-install.ps1` — publish → pack → sign → sideload → restart CmdPal
- [x] Git repository, versioning files (VERSION, CHANGELOG, LICENSE), v0.1.0 tag
- [x] User guide README + INSTALL.md
