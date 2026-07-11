# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-07-11

### Added

- Initial PowerToys Command Palette extension scaffold (`ClaudePowerCommand`): MSIX-packaged COM extension host with `PowerCommandExtension` and `PowerCommandProvider`.
- Dock band (`UsageDockBand`) showing 5-hour session % remaining, weekly % and session reset time, auto-refreshing every 30 seconds.
- Usage details page (`UsageDetailsPage`) with progress bars for session, 7-day all-model, and per-model weekly limits, plus a Refresh action.
- `ClaudeUsageService`: reads the Claude Code OAuth token from `%USERPROFILE%\.claude\.credentials.json` and queries `https://api.anthropic.com/api/oauth/usage` with a 45-second cache.
- Low-quota alert icon when the session drops below 20% remaining.
- Opt-in debug logging to `%TEMP%\claude-power-command.log` (flag file `%TEMP%\claude-power-command.debug`).
- `build-and-install.ps1`: publish → MSIX pack → self-signed dev-cert sign → sideload → restart Command Palette.
