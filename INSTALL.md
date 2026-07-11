# 🛠️ Installation Guide

This guide walks through building and installing **Claude Power Command Extension** from source on any Windows machine.

## 1. ✅ Check the requirements

| Requirement | Why | Check |
|---|---|---|
| Windows 11 (22H2 or later) | Command Palette dock support | `winver` |
| [PowerToys](https://github.com/microsoft/PowerToys) v0.9+ with Command Palette enabled | Hosts the extension | PowerToys Settings → Command Palette |
| [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) | Builds the extension | `dotnet --list-sdks` shows a `9.x` entry |
| Windows 10/11 SDK | Provides `makeappx.exe` / `signtool.exe` for MSIX packaging | Folder exists under `C:\Program Files (x86)\Windows Kits\10\bin` |
| [Claude Code](https://claude.com/claude-code), signed in | Supplies the OAuth token used to read your usage | `%USERPROFILE%\.claude\.credentials.json` exists |

Quick installs with winget:

```powershell
winget install Microsoft.PowerToys
winget install Microsoft.DotNet.SDK.9
winget install Microsoft.WindowsSDK.10.0.28000   # or any recent Windows SDK
```

## 2. 📥 Get the source

```powershell
git clone <repository-url>
cd "Claude Power Command Extension"
```

## 3. 🏗️ Build and install

Run from a **normal** (non-elevated) PowerShell prompt:

```powershell
.\build-and-install.ps1
```

The script:

1. Publishes the .NET project (`dotnet publish`, Release, win-x64).
2. Stages the MSIX payload (binaries + assets + manifest).
3. Creates a self-signed development certificate on first run (`CN=ClaudeUsageDock-Dev`).
4. Packs and signs the MSIX.
5. Installs the package. **The first install shows one UAC prompt** — that's Windows trusting the dev certificate; later installs don't need it.
6. Restarts Command Palette so it picks up the extension.

Re-run the same script after any code change; it handles upgrades in place.

## 4. 📌 Add the dock tile

1. Open Command Palette: `Win+Alt+Space`.
2. Open **Settings → Dock**.
3. Add the **Claude Usage** band.

The tile shows your 5-hour session percentage remaining and refreshes every 30 seconds. Click it for the full breakdown.

## 5. ✔️ Verify

- The `Claude usage` command appears in Command Palette search.
- The dock tile shows a percentage (or "Not signed in to Claude Code" if there's no local token — run `claude` and sign in, then wait one refresh cycle).

## 🔧 Troubleshooting

| Symptom | Fix |
|---|---|
| `dotnet publish failed` / SDK not found | Install the .NET 9 SDK and re-open the terminal so `PATH` updates. |
| `makeappx.exe / signtool.exe not found` | Install the Windows 10/11 SDK (Visual Studio Installer → Individual components, or standalone). |
| `Add-AppxPackage` certificate error | Delete `ClaudeUsageDock\bin\*.cer/.pfx`, remove the old cert from `certmgr.msc` (Personal → `ClaudeUsageDock-Dev`), and re-run the script. |
| Tile shows "Not signed in to Claude Code" | Sign in to Claude Code (`claude` in a terminal). The token is read from `%USERPROFILE%\.claude\.credentials.json`. |
| Tile shows "Anthropic API error (401)" | Your token expired — open Claude Code once so it refreshes, or sign in again. |
| Extension doesn't appear after install | Restart Command Palette: quit `Microsoft.CmdPal.UI` from Task Manager and reopen with `Win+Alt+Space`. |
| Need diagnostics | `New-Item $env:TEMP\claude-usage-dock.debug -ItemType File`, reproduce, then read `$env:TEMP\claude-usage-dock.log`. Token values are never logged. |

## 🗑️ Uninstall

```powershell
Get-AppxPackage -Name ClaudeUsageDock | Remove-AppxPackage
```

Optionally remove the dev certificate: `certmgr.msc` → Personal → Certificates → delete `ClaudeUsageDock-Dev` (and the copy under Trusted People in `certlm.msc`).
