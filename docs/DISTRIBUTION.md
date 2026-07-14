# 📦 Distribution: Microsoft Store & WinGet

How to publish ClaudeUsageDock beyond the GitHub release downloads.
Both channels remove the biggest install-friction of the direct download:
users no longer have to trust and import the self-signed release certificate.

The recommended order is **Store first** — a Store listing automatically makes
`winget install` work too (via winget's built-in `msstore` source), and it
solves the certificate problem that blocks the winget community repo.

## 🏪 Microsoft Store

One-time setup:

1. Register a [Partner Center](https://partner.microsoft.com/dashboard) individual
   developer account (one-time fee).
2. **Apps and games → New product → App**, reserve the name (e.g. "Claude Usage
   Dock").
3. Open **Product management → Product identity** and note the three values:
   `Package/Identity/Name`, `Package/Identity/Publisher` (a `CN=<GUID>`), and
   `Package/Properties/PublisherDisplayName`.

Per release:

1. Build the Store package with the identity from step 3 (it must **not** be
   signed — the Store signs it during ingestion):

   ```powershell
   .\scripts\build-store-package.ps1 `
       -IdentityName "<Package/Identity/Name>" `
       -Publisher "<Package/Identity/Publisher>" `
       -PublisherDisplayName "<PublisherDisplayName>"
   ```

2. In Partner Center, create a submission and upload
   `store-output\ClaudeUsageDock-Store.msix` on the **Packages** page.
3. Fill in the **Store listing** (per language). The package declares en-US,
   de-DE, fr-FR, es-ES, ja and zh-Hans, so Partner Center offers one listing per
   language; the listing copy is separate from the in-app strings and is
   pre-written for every language in this repo:
   - **Description, short description, product features, search terms, and
     "what's new"** — copy the row for each language from
     [`store-listings.csv`](store-listing/store-listings.csv) (one column per field). The
     `ProductFeatures` and `SearchTerms` cells hold several items separated by
     ` | ` — paste each item into its own Partner Center box.
   - **Screenshots** — upload the four 1366×768 PNGs in
     [`store-listing/screenshots/`](store-listing/screenshots/) (`01-dock-tile`,
     `02-usage`, `03-breakdown`, `04-heatmap`). One set can be reused across all
     languages.
   - **Screenshot captions** — one caption per screenshot per language in
     [`store-screenshot-captions.csv`](store-listing/store-screenshot-captions.csv).
4. Pricing: free. Under **Properties**, category "Developer tools".
5. **Privacy policy URL** (required): the submission asks whether the app
   accesses/collects/transmits personal information — answer **Yes** (the app
   reads and transmits your Claude Code OAuth credentials, though only to
   Anthropic's own endpoints), and give the URL of [PRIVACY.md](../PRIVACY.md)
   as served by your repo host, e.g.
   `https://github.com/shaikh-rashid/claude-command-palette-dock/blob/main/PRIVACY.md`.
6. Submit for certification (typically 1–3 business days).

### 📋 Submission checklist

Everything Partner Center asks you to upload or paste, and where it comes from
in this repo. Build `store-output\ClaudeUsageDock-Store.msix` with step 1 first
(it's gitignored, so it isn't committed).

| Partner Center page / field | What to provide | Source in this repo |
|---|---|---|
| Packages | The app package (unsigned) | `store-output\ClaudeUsageDock-Store.msix` |
| Store listing → Description | Long description | [`store-listings.csv`](store-listing/store-listings.csv) · `Description` |
| Store listing → Short description | One-line summary | [`store-listings.csv`](store-listing/store-listings.csv) · `ShortDescription` |
| Store listing → What's new in this version | Release notes | [`store-listings.csv`](store-listing/store-listings.csv) · `WhatsNew` |
| Store listing → Product features | Feature bullets (one per box) | [`store-listings.csv`](store-listing/store-listings.csv) · `ProductFeatures` (pipe-separated) |
| Store listing → Search terms | Up to 7 keywords (one per box) | [`store-listings.csv`](store-listing/store-listings.csv) · `SearchTerms` (pipe-separated) |
| Store listing → Screenshots | 4 × 1366×768 PNG | [`store-listing/screenshots/`](store-listing/screenshots/) `01`–`04-*.png` |
| Store listing → each screenshot's caption | Per-image caption | [`store-screenshot-captions.csv`](store-listing/store-screenshot-captions.csv) |
| Properties → Privacy policy URL | **Yes**, plus the hosted URL | [`PRIVACY.md`](../PRIVACY.md) → its `github.com/…/blob/main/PRIVACY.md` URL |
| Properties → Category | "Developer tools" | — |
| Pricing and availability | Free | — |
| Age ratings | Complete the IARC questionnaire (no objectionable content) | — |

Repeat the **Store listing** rows for each of the six languages — screenshots
can be reused across languages; the copy and captions are per-language (one CSV
row each).

### Bulk-fill the listing (Import listings)

Instead of pasting field by field, Partner Center can import a whole folder
(**app overview → Store listings → Import listings → Upload folder**). Two ways
to produce that folder:

- **Native import (simplest).** The import CSV is a transposed template whose
  `Field`/`ID`/`Type` rows — including a per-app `ID` — must stay unchanged, so
  first **Export listing** from Partner Center, then fill the language columns
  from this repo's content:

  ```powershell
  .\scripts\fill-store-listing-csv.ps1 -ExportedCsv .\exported-listing.csv
  ```

  That writes `docs\store-import\listing-import.csv` plus the four screenshots
  (referenced as `store-import/<file>`), preserving the export's Field/ID/Type
  rows and writing only the `default` + per-language columns. It prints any field
  rows it didn't recognize. Then upload the `store-import` folder via **Import
  listings → Upload folder**.

- **StoreBroker (API automation).** Generate a per-language PDP bundle from the
  authored listing content — no export needed:

  ```powershell
  .\scripts\build-store-submission.ps1
  ```

  That writes `docs\store-submission\` (generated and gitignored): `PDP/<lang>/`
  `ProductDescription.xml` per language, `Media/en-us/` with the four shared
  screenshots, and `source/` with the plain CSVs for manual copy/paste. Then,
  once (`Install-Module StoreBroker`, `Set-StoreBrokerAuthentication`,
  `New-StoreBrokerConfigFile` with `"MediaFallbackLanguage": "en-us"`), submit:

  ```powershell
  New-SubmissionPackage -ConfigPath .\SBConfig.json `
      -PDPRootPath .\docs\store-submission\PDP -PDPInclude "ProductDescription.xml" `
      -ImagesRootPath .\docs\store-submission\Media -OutPath .\out -OutName submission
  Update-ApplicationSubmission -AppId <StoreAppId> `
      -SubmissionDataPath .\out\submission.json -PackagePath .\out\submission.zip `
      -UpdateListingText -UpdateImagesAndCaptions -AutoCommit -Force
  ```

Notes:

- The app is a PowerToys Command Palette *extension* — the listing description
  should say clearly that it requires PowerToys and appears inside the Command
  Palette, not as a standalone window. Certification testers need that context
  (and it's the first support question otherwise).
- Once published, the same submission flow with a new
  `build-store-package.ps1` output ships each update; Store versions must
  strictly increase, which the `VERSION`-file discipline already guarantees.

## 🎯 WinGet

### Route A (automatic): via the Store

Nothing to do. Once the Store listing is live, users can:

```
winget search "Claude Usage Dock"
winget install "Claude Usage Dock" --source msstore
```

### Route B (community repo): microsoft/winget-pkgs

> ⚠️ **Certificate constraint:** winget-pkgs validation *installs* the MSIX,
> which requires its signature to chain to a root the machine already trusts.
> The project's self-signed release certificate does not, so a submission
> built from the current GitHub release assets **will fail validation**.
> This route needs either (a) the Store package to exist and Route A used
> instead, or (b) the release pipeline re-keyed to a purchased code-signing
> certificate. The tooling below is ready for the day either happens, and the
> manifests it produces already work for local testing.

Generate the manifests from a signed release MSIX:

```powershell
.\scripts\new-winget-manifest.ps1 `
    -MsixPath release-output\ClaudeUsageDock.msix `
    -InstallerUrl "https://github.com/shaikh-rashid/claude-command-palette-dock/releases/download/v1.0.0/ClaudeUsageDock.msix"
```

The script reads the identity and version out of the MSIX, computes
`InstallerSha256`, `SignatureSha256`, and the `PackageFamilyName` winget
verifies at install time, and writes the three manifest files
(`version` / `installer` / `defaultLocale`) under `winget-manifests\<version>\`.

Test locally (local manifests must be enabled once, from an elevated prompt:
`winget settings --enable LocalManifestFiles`; the release certificate must be
imported first, as on any sideload install):

```powershell
winget validate winget-manifests\1.0.0
winget install --manifest winget-manifests\1.0.0
```

Submit by copying the version folder into a fork of
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) at
`manifests/s/ShaikhRashid/ClaudeUsageDock/<version>/` and opening a PR, or let
[wingetcreate](https://github.com/microsoft/winget-create) drive the whole
update (`wingetcreate update ShaikhRashid.ClaudeUsageDock ...`) for subsequent
releases.
