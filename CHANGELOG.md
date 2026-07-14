# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-07-14

### Changed

- The Breakdown tab's statistics render as aligned key/value rows with a
  fixed-width label column and a divider between rows, instead of an
  AdaptiveCard `FactSet` whose host styling can't align columns or separate
  entries — matching the Store listing screenshots.

### Added

- **Localized UI** — every user-facing string (dock tile, details page tabs and
  bars, breakdown facts, failure messages, toast, and all settings) now comes
  from `.resx` resources with translations for **German, French, Spanish,
  Japanese, and Simplified Chinese**, following the Windows display language
  with English fallback. Times, dates, and numbers already format through the
  current culture. Deliberately not localized: the pixel-font labels rendered
  inside the heatmap PNGs (the built-in 5×5 typeface only covers A–Z0–9), debug
  log lines, and the "Claude Usage Dock" product name. The MSIX manifest now
  declares all six languages.
- **Microsoft Store packaging** — `scripts/build-store-package.ps1` produces
  the unsigned Store submission MSIX with the Partner Center identity
  (name/publisher/publisher display name) patched in;
  `New-MsixPackage` gained the corresponding identity-override parameters.
  The full submission walkthrough lives in `docs/DISTRIBUTION.md`.
- **WinGet distribution tooling** — `scripts/new-winget-manifest.ps1` generates
  the three winget-pkgs manifest files from a signed release MSIX, reading the
  identity/version out of the package and computing `InstallerSha256`,
  `SignatureSha256`, and the `PackageFamilyName` (publisher-hash algorithm
  verified against the installed package; output passes `winget validate`).
  `docs/DISTRIBUTION.md` documents both routes — the `msstore` source that
  comes free with a Store listing (recommended), and the community
  microsoft/winget-pkgs repo, which additionally requires a trusted-root
  signature that the current self-signed release certificate can't satisfy.

## [0.9.3] - 2026-07-13

### Fixed

- The 0.9.2 cert-store signing then handed `signtool` a malformed argument (`SignTool Error: Invalid option: /`): the `/sm` store flag was spliced in with inline array splatting mixed among literal arguments, which PowerShell's native-command handling mangled. The signing command is now built as one flat argument array and passed as a whole, and the resolved command line is echoed to the job log so any future argument problem is visible at a glance.
- The GitLab release job's `Invoke-WebRequest` calls now pass `-UseBasicParsing`. The shared Windows runner is Windows PowerShell 5.1, whose `Invoke-WebRequest` otherwise routes response parsing through the IE engine and fails ("Internet Explorer engine is not available") — a latent break in the artifact-upload step that the sign-only dry run wouldn't have surfaced.
- The manual `verify_signing` dry run now also performs the package-registry upload (to a throwaway `dry-run` path), so a green dry run proves the whole release path — sign and upload — rather than signing alone, and no step is first exercised only on a real (version-spending) tag build.

## [0.9.2] - 2026-07-13

### Fixed

- The release signing then failed one step later, in `signtool` itself, with `0x80090010` ("Store::ImportCertObject() failed" — `NTE_PERM`): signing straight from the PFX (`signtool /f /p`) makes signtool import the key into the per-user key container the hosted runner denies, the same access problem the certificate load hit. The CI signing path now imports the PFX into a certificate store itself and signs by thumbprint — preferring `LocalMachine` (a machine key container the admin runner can write, no user profile needed) and falling back to `CurrentUser` — which sidesteps signtool's own import. Local `build-and-install.ps1` sideloads still sign straight from the PFX, which works with a real interactive profile.

### Added

- A manual `verify_signing` CI job that runs the exact build-and-sign path on a default-branch pipeline without tagging or publishing. Because protected release tags can't be moved or deleted via git, every tagged attempt otherwise burns a version; this lets the signing be dry-run and iterated on `main` (Pipelines → the latest main pipeline → play `verify_signing`) so a tag is only spent once signing is known green. The tagged release and the dry run share one job template, so they can't drift.

## [0.9.1] - 2026-07-13

### Fixed

- The release build loaded the signing PFX with the default key-storage flags, which persist the private key to the on-disk user key container; the GitLab SaaS Windows runner's job context can't write there, so `X509Certificate2`'s constructor threw `Access denied` before the build even started (and the upload steps then failed on the artifacts that were never produced). It now loads with `EphemeralKeySet` — the key stays in memory, which is all that object needs since it's only read for its subject/thumbprint and to export the public `.cer`, while `signtool` does the actual signing straight from the PFX path. This is what failed the v0.9.0 release pipeline.

## [0.9.0] - 2026-07-13

### Added

- The details page is now organized as three tabbed sections, switched by a button strip at the top of the card (AdaptiveCards has no native tab control, so the strip is three submit buttons and a switch re-renders the page):
  - **Usage** — limit rows restyled after Claude Code's own usage panel: bold label left, subtle reset time and bold percent-remaining right ("Resets in 4 hr 39 min  69%", or "Resets Sat 2:00 PM" when more than a day out), and a thin rounded progress bar underneath, sized to what's left, whose color shifts blue → amber (below 25% remaining) → red (below 10%) as the quota runs low. The text row and the bar share one fixed width so the percent aligns to the bar's right edge. The burn-rate note stays under the session row; plan and last-checked stay in the page header only.
  - **Breakdown** — "What's using your limits?", derived from the local history log: weekly-quota burn over the last 24 h, daily average, busiest weekday/3-hour period, a pace projection for the weekly cap ("runs out around Thu 6 PM" / "lasts until it resets"), and the per-model weekly caps.
  - **Heatmap** — the monthly GitHub-style calendar heatmap returns from v0.8.0, redrawn to fill its tab (roughly double the cell size) and with a LESS→MORE color legend: five Monday-aligned week rows × weekday columns headed M T W T F S S, month labels where a month starts, a separated WK week-totals column, and a month-total line.

### Changed

- The progress bars are rendered PNGs now (a `BarRenderer` alongside the heatmap renderer) instead of unicode block glyphs — AdaptiveCards has no colorable progress element. The shared pixel primitives (fills, supersample downscaling, PNG encoding) moved into a common `Rasterizer` helper both renderers use.

### Removed

- The weekly time-of-day heatmap (weekday rows × 3-hour slots, v0.7.0–v0.8.2). Its insight survives as the Breakdown tab's "busiest period" statistic, and the monthly calendar covers the graphic itself.

### Fixed

- `build-and-install.ps1` now preserves the local usage history across a same-version reinstall. Sideloading the same version with different contents (the normal dev-iteration case) needs an uninstall-then-reinstall to dodge `0x80073CFB`, but that full uninstall deletes the package's `LocalState` — wiping the history log the Breakdown and Heatmap tabs are built from. The script backs `LocalState` up before removing the package and restores it after, so history survives a rebuild the way it would across a real version-bump upgrade.
- Both release pipelines now fail fast with a message naming the missing secret when `RELEASE_PFX_BASE64` or `RELEASE_PFX_PASSWORD` doesn't reach the job (unset, or on GitLab marked Protected while the tag isn't a protected tag). Previously an empty password surfaced late as an opaque `Cannot bind argument to parameter 'PfxPassword' because it is an empty string`, followed by missing-artifact noise from the upload steps — this is what failed the v0.8.2 GitLab pipeline, since GitHub secrets and GitLab CI/CD variables are separate stores.
- The GitLab `dependency_audit` job failed with NETSDK1100: restoring a `net9.0-windows`-TFM project on the job's Linux SDK image requires `EnableWindowsTargeting=true`. It's now set as a job-level variable (an environment variable) rather than a `-p:` flag, because `dotnet list package` doesn't accept MSBuild property arguments while MSBuild does pick the property up from the environment for both commands. Reproduced and verified end-to-end in the same `mcr.microsoft.com/dotnet/sdk:9.0` image CI uses: restore succeeds and the audit reports no vulnerable packages.

## [0.8.2] - 2026-07-12

### Fixed

- Release CI failed at the signing step with `SignTool Error: An unexpected internal error has occurred.` — signtool refuses to sign an MSIX whose manifest `Identity Publisher` (`CN=ClaudeUsageDock-Dev`, the dev sideload publisher) differs from the signing certificate's subject (`CN=ClaudeUsageDock-Release`). The CI build now patches the staged `AppxManifest.xml` publisher to the certificate's subject before packing; the manifest in the repo keeps the dev publisher for local `build-and-install.ps1` sideloads. Reproduced and the fix verified end-to-end locally with a simulated release certificate.
- The CI signing step no longer swallows signtool's stdout, which contained the actual `SignerSign() failed (0x8007000b)` diagnostic; the release script also validates the PFX and password up front (clear error instead of a generic one, before spending build time) and logs the signing subject and thumbprint.

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
