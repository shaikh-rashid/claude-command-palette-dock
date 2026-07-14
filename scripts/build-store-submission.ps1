# Consolidates the Store listing content into docs/store-submission/ — a single
# folder StoreBroker (Microsoft's Store submission API tool) can consume to
# auto-fill the listing across all languages, with the source CSVs kept alongside
# for manual copy/paste.
#
# It reads the two authored CSVs (docs/store-listing/store-listings.csv and
# docs/store-listing/store-screenshot-captions.csv) and, for each language,
# writes a StoreBroker PDP (ProductDescription.xml). Screenshots go in
# Media/en-us and are shared across languages via StoreBroker's
# MediaFallbackLanguage.
#
# The output folder (docs/store-submission/) is generated and gitignored — this
# script is its source of truth. Re-run whenever the CSVs or screenshots change:
#   .\scripts\build-store-submission.ps1

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$docs = Join-Path $repoRoot "docs"
$source = Join-Path $docs "store-listing"
$listingsCsv = Join-Path $source "store-listings.csv"
$captionsCsv = Join-Path $source "store-screenshot-captions.csv"
$assets = Join-Path $source "screenshots"
$out = Join-Path $docs "store-submission"

# Store listing language codes (lowercase) for each package locale.
$langMap = @{
    "en-US" = "en-us"; "de-DE" = "de-de"; "fr-FR" = "fr-fr"
    "es-ES" = "es-es"; "ja" = "ja"; "zh-Hans" = "zh-hans"
}

# The four screenshots, in listing order, paired to their caption CSV column.
$shots = @(
    @{ File = "01-dock-tile.png";  Caption = "DockTile" }
    @{ File = "02-usage.png";      Caption = "UsageTab" }
    @{ File = "03-breakdown.png";  Caption = "BreakdownTab" }
    @{ File = "04-heatmap.png";    Caption = "HeatmapTab" }
)

$repoUrl = "https://github.com/shaikh-rashid/claude-command-palette-dock"
$privacyUrl = "$repoUrl/blob/main/PRIVACY.md"
$supportUrl = "$repoUrl/issues"

function Esc([string]$s) { [System.Security.SecurityElement]::Escape($s) }
function WriteUtf8([string]$path, [string]$content) {
    [System.IO.File]::WriteAllText($path, $content, (New-Object System.Text.UTF8Encoding($false)))
}

$listings = Import-Csv $listingsCsv
$captions = Import-Csv $captionsCsv | Group-Object Locale -AsHashTable -AsString

# Fresh generated subfolders (the whole folder is regenerated on every run).
foreach ($sub in "PDP", "Media", "source") {
    $p = Join-Path $out $sub
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
    New-Item $p -ItemType Directory -Force | Out-Null
}

foreach ($row in $listings) {
    $locale = $row.Locale
    $lang = $langMap[$locale]
    if (-not $lang) { throw "No language-code mapping for locale '$locale'." }

    $keywords = ($row.SearchTerms -split '\s*\|\s*') | Where-Object { $_ }
    $features = ($row.ProductFeatures -split '\s*\|\s*') | Where-Object { $_ }
    $cap = $captions[$locale]
    if (-not $cap) { throw "No captions row for locale '$locale'." }

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$sb.AppendLine("<ProductDescription language=""$lang""")
    [void]$sb.AppendLine('    xmlns="http://schemas.microsoft.com/appx/2012/ProductDescription"')
    [void]$sb.AppendLine('    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">')
    [void]$sb.AppendLine("  <AppStoreName>$(Esc $row.ProductName)</AppStoreName>")
    [void]$sb.AppendLine('  <Keywords>')
    foreach ($k in $keywords) { [void]$sb.AppendLine("    <Keyword>$(Esc $k.Trim())</Keyword>") }
    [void]$sb.AppendLine('  </Keywords>')
    [void]$sb.AppendLine("  <Description>$(Esc $row.Description)</Description>")
    # ShortDescription is used by the Store for apps' short summary; harmless if ignored.
    [void]$sb.AppendLine("  <ShortDescription>$(Esc $row.ShortDescription)</ShortDescription>")
    [void]$sb.AppendLine("  <ReleaseNotes>$(Esc $row.WhatsNew)</ReleaseNotes>")
    [void]$sb.AppendLine('  <ScreenshotCaptions>')
    foreach ($shot in $shots) {
        $text = Esc ($cap.($shot.Caption))
        [void]$sb.AppendLine("    <Caption DesktopImage=""$($shot.File)"">$text</Caption>")
    }
    [void]$sb.AppendLine('  </ScreenshotCaptions>')
    [void]$sb.AppendLine('  <AppFeatures>')
    foreach ($f in $features) { [void]$sb.AppendLine("    <AppFeature>$(Esc $f.Trim())</AppFeature>") }
    [void]$sb.AppendLine('  </AppFeatures>')
    [void]$sb.AppendLine("  <WebsiteURL>$repoUrl</WebsiteURL>")
    [void]$sb.AppendLine("  <SupportContactInfo>$supportUrl</SupportContactInfo>")
    [void]$sb.AppendLine("  <PrivacyPolicyURL>$privacyUrl</PrivacyPolicyURL>")
    [void]$sb.AppendLine('</ProductDescription>')

    $langDir = Join-Path $out "PDP\$lang"
    New-Item $langDir -ItemType Directory -Force | Out-Null
    WriteUtf8 (Join-Path $langDir "ProductDescription.xml") $sb.ToString()
}

# Screenshots (shared across languages via MediaFallbackLanguage = en-us).
$mediaDir = Join-Path $out "Media\en-us"
New-Item $mediaDir -ItemType Directory -Force | Out-Null
foreach ($shot in $shots) { Copy-Item (Join-Path $assets $shot.File) $mediaDir -Force }

# The authored CSVs, for manual copy/paste into Partner Center's web UI.
Copy-Item $listingsCsv (Join-Path $out "source") -Force
Copy-Item $captionsCsv (Join-Path $out "source") -Force

# A short pointer README so the generated (gitignored) folder is self-explanatory.
$readme = @"
# Store submission bundle (generated)

Generated by ``scripts/build-store-submission.ps1`` from ``docs/store-listing/`` —
do not edit by hand; this folder is gitignored and rebuilt on demand.

- ``PDP/<lang>/ProductDescription.xml`` — StoreBroker listing text per language
- ``Media/en-us/`` — the four screenshots (shared across languages via
  MediaFallbackLanguage; captions stay per-language)
- ``source/`` — the authored CSVs, for manual copy/paste

StoreBroker / Partner Center steps are in ``docs/DISTRIBUTION.md``.
"@
WriteUtf8 (Join-Path $out "README.md") $readme

$langs = ($listings | ForEach-Object { $langMap[$_.Locale] }) -join ", "
Write-Host "Wrote $out"
Write-Host "  PDP languages: $langs"
Write-Host "  Media: $((Get-ChildItem $mediaDir).Count) screenshots in Media\en-us (shared via MediaFallbackLanguage)"
