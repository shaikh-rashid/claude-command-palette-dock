# 🛠️ Installation Guide

This guide covers two ways to install **Claude Power Command Extension**: downloading a prebuilt, signed release (no SDKs needed), or building from source.

## 🚀 Option A: Install a prebuilt release

1. Go to the Releases page — primary: [GitLab](https://gitlab.com/shaikh.rashid/claude-command-palette-dock/-/releases); mirror: [GitHub](https://github.com/shaikh-rashid/claude-command-palette-dock/releases) — and download `ClaudeUsageDock.msix` and `ClaudeUsageDock-Release.cer` from the latest release. Both mirrors are signed with the same certificate.
2. Trust the certificate (one-time, requires admin — Windows won't install an MSIX signed by an untrusted certificate):

   ```powershell
   Import-Certificate -FilePath .\ClaudeUsageDock-Release.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

3. Install the package:

   ```powershell
   Add-AppxPackage -Path .\ClaudeUsageDock.msix
   ```

4. Continue at **📌 Add the dock tile** below.

Every release is signed with the same certificate, so step 2 only needs to happen once, ever — later updates just need step 3 again.

## 🏗️ Option B: Build from source

This is the path to use if you want to modify the code, or if no release has been published yet.

### 1. ✅ Check the requirements

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
winget install Microsoft.WindowsSDK.10.0.26100   # must match the csproj's TargetFramework platform version
```

### 2. 📥 Get the source

```powershell
git clone git@gitlab.com:shaikh.rashid/claude-command-palette-dock.git
cd "Claude Power Command Extension"
```

### 3. 🏗️ Build and install

Run from a **normal** (non-elevated) PowerShell prompt:

```powershell
.\build-and-install.ps1
```

The script:

1. Publishes the .NET project (`dotnet publish`, Release, win-x64).
2. Creates a self-signed development certificate on first run (`CN=ClaudeUsageDock-Dev`).
3. Stages the MSIX payload (binaries + assets + manifest) and packs it.
4. Signs the MSIX.
5. Installs the package. **The first install shows one UAC prompt** — that's Windows trusting the dev certificate; later installs don't need it. Also restarts Command Palette so it picks up the extension.

Re-run the same script after any code change; it handles upgrades in place. Uses shared logic from `scripts/BuildTools.ps1` — the same staging/packing code the release workflow uses, so the two can't silently drift apart.

## 📌 Add the dock tile

1. Open Command Palette: `Win+Alt+Space`.
2. Open **Settings → Dock**.
3. Add the **Claude Usage** band.

The tile shows your 5-hour session percentage remaining and refreshes every 30 seconds. Click it for the full breakdown.

## ✔️ Verify

- The `Claude Usage Dock` command appears in Command Palette search.
- The dock tile shows a percentage (or "Not signed in to Claude Code" if there's no local token — run `claude` and sign in, then wait one refresh cycle).

## 🔧 Troubleshooting

| Symptom | Fix |
|---|---|
| `dotnet publish failed` / SDK not found | Install the .NET 9 SDK and re-open the terminal so `PATH` updates. |
| `Windows SDK UAP platform ... is not installed` | Install the exact SDK version named in the error, e.g. `winget install Microsoft.WindowsSDK.10.0.26100`. Having a newer Windows SDK installed does **not** substitute for this — .NET's tooling only recognizes specific platform versions, and CsWinRT needs that platform's files physically on disk. |
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

Optionally remove the dev certificate: `certmgr.msc` → Personal → Certificates → delete `ClaudeUsageDock-Dev` (and the copy under Trusted People in `certlm.msc`). If you installed a prebuilt release instead, the certificate to remove is `ClaudeUsageDock-Release`.

## 📦 Publishing a release (maintainers)

Two CI pipelines build, pack, and sign the MSIX and publish a Release whenever a `vX.Y.Z` tag is pushed — GitLab (`.gitlab-ci.yml`, primary) and GitHub (`.github/workflows/release.yml`, mirror). Both consume the same signing certificate, so a release looks identical either place.

One-time setup, before the first automated release:

1. Run `.\scripts\generate-release-cert.ps1` locally. It creates a persistent self-signed certificate (`CN=ClaudeUsageDock-Release`) and prints a base64-encoded `.pfx` and a generated password.
2. Add both values in **both** places:
   - GitHub: **Settings → Secrets and variables → Actions** → new repository secrets `RELEASE_PFX_BASE64` and `RELEASE_PFX_PASSWORD`.
   - GitLab: **Settings → CI/CD → Variables** → new variables `RELEASE_PFX_BASE64` and `RELEASE_PFX_PASSWORD`, both marked **Masked** (and **Protected** if release tags are protected).
3. Delete the local base64 file afterward — it contains the private key.

Every release after that reuses the same certificate, so anyone who trusted it once (Option A, step 2) never needs to re-trust it for a later update, on either mirror. Rotating the certificate is possible by re-running the script and updating both sets of variables, but it forces every existing user to re-trust the new one — avoid unless the old key is compromised.

To ship a release: bump `VERSION` and the `Identity Version` in `ClaudeUsageDock/Package.appxmanifest` to match, add a section to `CHANGELOG.md` for that version (both pipelines copy it into the release notes verbatim — they fail the build if it's missing), commit, then tag and push:

```powershell
git tag -a v0.5.0 -m "v0.5.0 — ..."
git push origin v0.5.0
```

`origin` is GitLab (primary) and is configured with a second push URL for GitHub, so a single `git push origin` pushes to both — no need to push to each separately. Run `git remote -v` to see the current push URLs.
