# Claude Power Command Extension

A [PowerToys Command Palette](https://aka.ms/PowerToysOverview_CommandPalette) extension that shows live Claude Code subscription usage as a tile in the Command Palette **Dock**, plus a detail page with session, weekly, and per-model progress bars.

It reads the OAuth token Claude Code already stores at `%USERPROFILE%\.claude\.credentials.json` and polls Anthropic's official usage endpoint every 30 seconds. Nothing else leaves the machine.

Inspired by [omgapnt/ClaudeUsage](https://github.com/omgapnt/ClaudeUsage).

## Features

- **Dock tile** — live 5-hour session percentage remaining, with the weekly percentage and session reset time in the subtitle. Refreshes every 30 seconds.
- **Details page** — click the tile (or run the `Claude usage` command) for a full breakdown: session limit, 7-day all-model limit, and per-model weekly limits, each with a progress bar and reset time, plus a Refresh button.
- **Low-quota alert** — the tile icon switches to an orange alert mark when the session drops below 20% remaining.
- **Privacy-first** — talks only to Anthropic's official usage endpoint (`https://api.anthropic.com/api/oauth/usage`) with the token Claude Code already stores locally. No telemetry.

## Layout

```
ClaudePowerCommand/
  ClaudePowerCommand.csproj
  Program.cs                    entry point (ExtensionHostRunner)
  PowerCommandExtension.cs      IExtension implementation
  PowerCommandProvider.cs       top-level command + dock band wiring, refresh timer
  Services/
    ClaudeUsageService.cs       reads credentials, calls Anthropic's usage API
    DebugLogger.cs              opt-in file logging
  Dock/
    UsageDockBand.cs            the dock tile (title/subtitle/icon per refresh)
  Pages/
    UsageDetailsPage.cs         full stats page with progress bars
  Assets/                       app tile logos + status icons
build-and-install.ps1           publish -> MSIX pack -> sign -> sideload
global.json                     pins the .NET SDK version
```

## Prerequisites

This machine is currently missing both of the following — install them before running the build script:

1. **.NET 9 SDK** — only the runtime is installed today (`dotnet --list-sdks` returns nothing). Get it from https://dotnet.microsoft.com/download.
2. **Windows 10/11 SDK** (for `makeappx.exe` / `signtool.exe`) — not found under `C:\Program Files (x86)\Windows Kits\10\bin`. Install it via the Visual Studio Installer ("Windows 10 SDK" or "Windows 11 SDK" individual component), or the standalone installer from https://developer.microsoft.com/windows/downloads/windows-sdk/.

You already have **PowerToys Command Palette** installed (`Microsoft.CommandPalette 0.11.11762.0`), so no action needed there.

## Build & install

```powershell
.\build-and-install.ps1
```

This publishes the app, stages an MSIX payload, creates (and trusts, on first run) a local dev signing certificate, packs + signs the MSIX, and sideloads it. It restarts Command Palette at the end.

Then in Command Palette: **Settings → Dock → add the "Claude Usage" band**.

## Debug logging

Create an empty file at `%TEMP%\claude-power-command.debug` to turn on logging to `%TEMP%\claude-power-command.log`. Delete the flag file to turn it back off.

## Versioning

Semantic versioning. The source of truth is the [VERSION](VERSION) file; keep the four-part `Identity Version` in `ClaudePowerCommand/Package.appxmanifest` in sync when bumping (`MAJOR.MINOR.PATCH.0`). Changes are recorded in [CHANGELOG.md](CHANGELOG.md).

## License

[MIT](LICENSE)
