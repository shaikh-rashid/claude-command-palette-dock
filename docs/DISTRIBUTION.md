# 📦 Distribution: Microsoft Store & WinGet

How to publish ClaudeUsageDock beyond the GitLab/GitHub release downloads.
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
3. Fill in the listing. The package declares en-US, de-DE, fr-FR, es-ES, ja and
   zh-Hans, so Partner Center offers a listing per language — the store
   description can be written (or machine-assisted) per language, independent
   of the in-app strings.
4. Pricing: free. Under **Properties**, category "Developer tools".
5. **Privacy policy URL** (required): the submission asks whether the app
   accesses/collects/transmits personal information — answer **Yes** (the app
   reads and transmits your Claude Code OAuth credentials, though only to
   Anthropic's own endpoints), and give the URL of [PRIVACY.md](../PRIVACY.md)
   as served by your repo host, e.g.
   `https://gitlab.com/shaikh.rashid/claude-command-palette-dock/-/blob/main/PRIVACY.md`.
6. Submit for certification (typically 1–3 business days).

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
> built from the current GitLab/GitHub release assets **will fail validation**.
> This route needs either (a) the Store package to exist and Route A used
> instead, or (b) the release pipeline re-keyed to a purchased code-signing
> certificate. The tooling below is ready for the day either happens, and the
> manifests it produces already work for local testing.

Generate the manifests from a signed release MSIX:

```powershell
.\scripts\new-winget-manifest.ps1 `
    -MsixPath release-output\ClaudeUsageDock.msix `
    -InstallerUrl "https://gitlab.com/shaikh.rashid/claude-command-palette-dock/-/releases/v1.0.0/downloads/ClaudeUsageDock.msix"
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
