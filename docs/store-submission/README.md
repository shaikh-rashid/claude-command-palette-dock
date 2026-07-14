# Store submission bundle

Everything needed to fill the Microsoft Store listing for **Claude Usage Dock**,
in one folder, for all six languages.

> **Reality check:** Partner Center's *web UI* has no "upload a folder to
> auto-fill" button. The supported way to fill a listing from a folder like this
> is **[StoreBroker](https://github.com/microsoft/StoreBroker)**, Microsoft's
> tool that drives the Store submission API. This folder is laid out for it. If
> you'd rather fill the web form by hand, everything is also in `source/` as CSVs
> to copy/paste from.

## Layout

```
store-submission/
  PDP/                     StoreBroker ProductDescription files, one per language
    en-us/ProductDescription.xml
    de-de/ …  fr-fr/ …  es-es/ …  ja/ …  zh-hans/ …
  Media/
    en-us/                 the four screenshots (shared across languages via
                           MediaFallbackLanguage; captions stay per-language)
  source/                  the authored CSVs, for manual copy/paste
    store-listings.csv
    store-screenshot-captions.csv
```

Each `ProductDescription.xml` carries that language's app name, description,
short description, keywords, release notes, the eight app features, and the four
per-screenshot captions, plus the website / support / privacy-policy URLs.

Regenerate it any time the CSVs or screenshots change:

```powershell
.\scripts\build-store-submission.ps1
```

## Auto-fill with StoreBroker

One-time:

1. `Install-Module -Name StoreBroker -Scope CurrentUser`
2. Create an Azure AD app with Partner Center access and authenticate:
   `Set-StoreBrokerAuthentication -TenantId <tenant> -ClientId <client>` (it
   prompts for the secret). See StoreBroker's *Setting up credentials* guide.
3. `New-StoreBrokerConfigFile -Path .\SBConfig.json -AppId <StoreAppId>`, then
   edit `SBConfig.json`: set `"MediaFallbackLanguage": "en-us"` (so every
   language reuses the `Media/en-us` screenshots) and point the package path at
   the built Store MSIX (`store-output\ClaudeUsageDock-Store.msix` — build it
   with `scripts\build-store-package.ps1`, see ../DISTRIBUTION.md).

Per submission (run from this folder):

```powershell
New-SubmissionPackage -ConfigPath .\SBConfig.json `
    -PDPRootPath .\PDP -PDPInclude "ProductDescription.xml" `
    -ImagesRootPath .\Media -OutPath .\out -OutName submission

Update-ApplicationSubmission -AppId <StoreAppId> `
    -SubmissionDataPath .\out\submission.json `
    -PackagePath .\out\submission.zip `
    -UpdateListingText -UpdateImagesAndCaptions `
    -AutoCommit -Force
```

`New-SubmissionPackage` bundles the PDPs, screenshots, and package into
`submission.zip` + `submission.json`; `Update-ApplicationSubmission` pushes them
to your pending submission and (with `-AutoCommit`) submits it.

## Manual fill (web UI)

On each language's **Store listing** page, copy from
[`source/store-listings.csv`](source/store-listings.csv) (one row per language:
description, short description, product features, search terms, what's new) and
[`source/store-screenshot-captions.csv`](source/store-screenshot-captions.csv)
for the per-image captions; upload the four PNGs from `Media/en-us`. The field →
file mapping is the checklist in [../DISTRIBUTION.md](../DISTRIBUTION.md).

## Notes

- **Product identity** (Partner Center → Product management → Product identity):
  name `ShaikhFarhanRashid.ClaudeUsageDock`, publisher
  `CN=354B8997-0284-4992-BDC3-0AD6ED7E8312`. The Store MSIX must be built with
  these (that's what `build-store-package.ps1` does).
- **Privacy policy question:** answer **Yes**; the `PrivacyPolicyURL` in every
  PDP already points at the hosted PRIVACY.md.
- Screenshots are shared across languages; only the captions are localized, so
  `Media` holds one `en-us` set and StoreBroker fans it out via
  `MediaFallbackLanguage`.
