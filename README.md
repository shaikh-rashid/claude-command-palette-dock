# 📊 Claude Power Command Extension

A [PowerToys Command Palette](https://aka.ms/PowerToysOverview_CommandPalette) extension that shows live **Claude Code subscription usage** as a tile in the Command Palette **Dock**, plus a detail page with session, weekly, and per-model progress bars.

It reads the OAuth token Claude Code already stores at `%USERPROFILE%\.claude\.credentials.json` and polls Anthropic's official usage endpoint. Nothing else leaves your machine — no telemetry, no third-party services.

Inspired by [omgapnt/ClaudeUsage](https://github.com/omgapnt/ClaudeUsage).

## ✨ Features

- **Dock tile** — live 5-hour session percentage remaining, with the weekly percentage and session reset time in the subtitle. Refreshes every 30 seconds by default (configurable from 15 s to 5 min).
- **Details page** — click the tile (or run the `Claude Usage Dock` command) for a full breakdown: session limit, 7-day all-model limit, and per-model weekly limits, each with a progress bar and reset time, plus a Refresh button.
- **Low-quota alert** — the tile icon gains a red alert badge when the session drops below a configurable threshold (20% by default), and a Windows toast notification fires the first time it crosses (can be turned off).
- **Burn-rate estimate** — the details page projects when your session hits 0% at the current pace, based on a rolling local snapshot log.
- **Weekly sparkline** — a 7-day usage trend chart on the details page. History stays on your machine.
- **Token auto-refresh** — when the stored Claude Code token expires, the extension refreshes it itself (and writes it back for Claude Code), so the tile keeps working overnight.
- **Multiple accounts** — monitor up to two additional Claude accounts by pointing extra profiles at separately saved credential files, each with its own dock tile, command, and history.
- **Settings** — refresh interval, alert threshold, toast notifications, and additional account profiles are editable from the extension's Settings page in Command Palette.
- **Resilient polling** — backs off when Anthropic rate-limits (HTTP 429) and keeps showing the last good numbers for up to 10 minutes instead of an error tile.
- **Privacy-first** — talks only to Anthropic's official endpoints with the token Claude Code already stores locally; usage history is a local file.

## 🚀 Getting started

See **[INSTALL.md](INSTALL.md)** for full step-by-step instructions, troubleshooting, and uninstall.

**Fastest path — no SDKs needed:** download `ClaudeUsageDock.msix` and `ClaudeUsageDock-Release.cer` from the [Releases page](../../releases), trust the certificate once, then `Add-AppxPackage`. Full steps in INSTALL.md.

**From source:**

```powershell
git clone <repository-url>
cd "Claude Power Command Extension"
.\build-and-install.ps1
```

Then open Command Palette (`Win+Alt+Space`) → **Settings → Dock** → add the **Claude Usage** band.

**Requirements (from source only):** Windows 11 (22H2+), PowerToys v0.9+ with Command Palette, .NET 9 SDK, Windows 10/11 SDK (must include the `10.0.26100.0` UAP platform), and a signed-in Claude Code installation. The prebuilt release only needs Windows 11 + PowerToys + Claude Code.

## 🧭 Using the extension

### The dock tile

| Tile shows | Meaning |
|---|---|
| `74% session left` / `91% week · resets 14:00` | Normal operation — session %, weekly %, and when the 5-hour window resets |
| Red alert badge on the icon | The session is below your alert threshold (a toast also fires once per dip) |
| `Not signed in to Claude Code` | No local token found — sign in to Claude Code |
| `Session expired — sign in to Claude Code` | The token expired **and** automatic refresh failed — sign in again |
| `Rate limited — retrying soon` | Anthropic returned 429 and there's no recent snapshot to show; clears on its own |
| `Anthropic API error (…)` | The usage API returned an error; usually transient |
| `Offline — will retry` | No network; the tile recovers automatically on the next refresh |

### 📋 The details page

Open it by clicking the dock tile or running **Claude Usage Dock** from Command Palette search. It shows:

- Your **plan type** (read from Claude Code's local credentials) and when the data was last checked.
- **5-hour session** — your current session window and when it resets, with a projection of when it runs out at your current pace (shown once ~15 minutes of history exists).
- **7-day (all models)** — the weekly cap across every model, with a 7-day usage sparkline once enough history has accumulated.
- **7-day per model** — individual weekly caps (e.g. Opus), when your plan has them.
- A **Refresh** button to re-query immediately, bypassing the snapshot cache.

### 👥 Multiple accounts

Claude Code only keeps one active `.credentials.json`, so this only helps if you (or a script) already save separate copies per account — e.g. `%USERPROFILE%\.claude\.credentials.work.json` after signing in to a second account and copying the file aside.

In the extension's Settings page, fill in **Second account** (and optionally **Third account**): a label and the full path to that account's saved credentials file. Each configured account gets its own dock tile, its own "Claude Usage Dock — *label*" command, and its own local history log — all refreshed independently. Leave a path blank to disable that slot. The default profile (your normal `~/.claude/.credentials.json`) is always present and unaffected by this.

### 🐞 Debug logging

Logging is off by default. To enable it:

```powershell
New-Item $env:TEMP\claude-usage-dock.debug -ItemType File
Get-Content $env:TEMP\claude-usage-dock.log -Tail 50 -Wait
```

Delete the flag file to turn it back off. Token values are never written to the log.

## 📁 Project layout

```text
ClaudeUsageDock/
  ClaudeUsageDock.csproj
  Program.cs                    entry point (ExtensionHostRunner)
  PowerCommandExtension.cs      IExtension implementation
  PowerCommandProvider.cs       top-level command + dock band wiring, refresh timer, settings
  Icons.cs                      themed (light/dark) sunburst icons
  Services/
    ClaudeUsageService.cs       credentials + token refresh, Anthropic usage API client
    UsageHistoryStore.cs        rolling local snapshot log (burn rate + sparkline)
    SettingsManager.cs          user settings (interval, threshold, toasts)
    ToastNotifier.cs            low-quota Windows notification
    DebugLogger.cs              opt-in file logging
  Dock/
    UsageDockBand.cs            the dock tile (title/subtitle/icon per refresh)
  Pages/
    UsageDetailsPage.cs         full stats page: progress bars, estimate, sparkline
  Assets/                       app tile logos + status icons
scripts/
  BuildTools.ps1                 shared publish/stage/pack/sign helpers (dev + CI)
  ci-build-release.ps1            CI-only: builds and signs the release MSIX
  generate-release-cert.ps1       one-time: creates the persistent release signing cert
.github/workflows/release.yml   builds, signs, and publishes a GitHub Release on tag push
build-and-install.ps1           publish -> MSIX pack -> sign -> sideload (local dev)
global.json                     pins the .NET SDK version
```

## 🗺️ Contributing & roadmap

Planned work and ideas are tracked in [TODO.md](TODO.md). Changes are recorded in [CHANGELOG.md](CHANGELOG.md).

## 🏷️ Versioning

Semantic versioning. The source of truth is the [VERSION](VERSION) file; keep the four-part `Identity Version` in `ClaudeUsageDock/Package.appxmanifest` in sync when bumping (`MAJOR.MINOR.PATCH.0`).

## 📄 License

[MIT](LICENSE)
