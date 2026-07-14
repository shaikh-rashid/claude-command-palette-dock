# Fills a Partner Center "Export listing" CSV with this repo's listing content,
# ready to re-import via Partner Center -> Import listings -> Upload folder.
#
# WHY AN EXPORT IS REQUIRED: the import CSV is a transposed template — rows are
# fields (Description, Feature1..20, DesktopScreenshot1.., ...), columns are
# Field/ID/Type/default plus one per language. The ID column is assigned by
# Partner Center per app, and Field/ID/Type must not be altered, so the file
# can't be authored from scratch. Export it first (app overview -> Store
# listings -> Export listing), then run this over it.
#
# It preserves Field/ID/Type untouched and only writes the language columns
# (and the shared "default" column), matching each row by its Field name. Rows
# it doesn't recognize are left exactly as they were and reported, so nothing is
# clobbered. Screenshots are copied into the output folder and referenced by the
# relative "<folder>/<file>" path Partner Center expects.
#
# Usage:
#   .\scripts\fill-store-listing-csv.ps1 -ExportedCsv .\exported-listing.csv
#   -> writes docs\store-import\listing-import.csv + the four screenshots.
#   Then in Partner Center: Import listings -> Upload folder -> pick store-import.

param(
    [Parameter(Mandatory)][string]$ExportedCsv,
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$docs = Join-Path $repoRoot "docs"
$source = Join-Path $docs "store-listing"
if (-not $OutputDir) { $OutputDir = Join-Path $docs "store-import" }

# Package locale -> Store listing language code (lowercase).
$langMap = [ordered]@{ "en-US" = "en-us"; "de-DE" = "de-de"; "fr-FR" = "fr-fr"; "es-ES" = "es-es"; "ja" = "ja"; "zh-Hans" = "zh-hans" }
$storeLocales = @($langMap.Values)
$defaultLocale = "en-us"
$shotFiles = @("01-dock-tile.png", "02-usage.png", "03-breakdown.png", "04-heatmap.png")
$capCols = @("DockTile", "UsageTab", "BreakdownTab", "HeatmapTab")

# Load this repo's authored content, keyed by Store language code.
$listings = Import-Csv (Join-Path $source "store-listings.csv")
$capByLoc = Import-Csv (Join-Path $source "store-screenshot-captions.csv") | Group-Object Locale -AsHashTable -AsString
$data = @{}
foreach ($row in $listings) {
    $store = $langMap[$row.Locale]
    $data[$store] = [pscustomobject]@{
        Description      = $row.Description
        ShortDescription = $row.ShortDescription
        ReleaseNotes     = $row.WhatsNew
        Title            = $row.ProductName
        Features         = @($row.ProductFeatures -split '\s*\|\s*' | Where-Object { $_ } | ForEach-Object { $_.Trim() })
        SearchTerms      = @($row.SearchTerms -split '\s*\|\s*' | Where-Object { $_ } | ForEach-Object { $_.Trim() })
        Captions         = @($capCols | ForEach-Object { $capByLoc[$row.Locale].$_ })
    }
}

$rows = @(Import-Csv -LiteralPath $ExportedCsv)
if ($rows.Count -eq 0) { throw "The exported CSV '$ExportedCsv' is empty." }
$orig = @($rows[0].PSObject.Properties.Name)
# The Type column header is exported as "Type (Type)", so only require the two
# columns this script actually reads/keys on.
if ($orig -notcontains "Field" -or $orig -notcontains "ID") {
    throw "'$ExportedCsv' doesn't look like a Partner Center listing export (no 'Field'/'ID' column). Export it from the app overview -> Store listings -> Export listing."
}

# Final column set = the export's columns + any of our language columns it lacks.
$headers = [System.Collections.Generic.List[string]]::new()
$orig | ForEach-Object { $headers.Add($_) }
if ($headers -notcontains "default") { $headers.Add("default") }
foreach ($loc in $storeLocales) { if ($headers -notcontains $loc) { $headers.Add($loc) } }

$folderName = Split-Path $OutputDir -Leaf
function Set-Cell($row, $col, $val) { $row | Add-Member -NotePropertyName $col -NotePropertyValue $val -Force }
function Set-AllLangs($row, [scriptblock]$pick) {
    foreach ($l in $storeLocales) { Set-Cell $row $l (& $pick $l) }
    Set-Cell $row 'default' (& $pick $defaultLocale)
}

$matched = [System.Collections.Generic.List[string]]::new()
$unmatched = [System.Collections.Generic.List[string]]::new()

foreach ($row in $rows) {
    $field = $row.Field
    $filled = $true
    switch -regex ($field) {
        '^Description$' { Set-AllLangs $row { param($l) $data[$l].Description }; break }
        '^ShortDescription$' { Set-AllLangs $row { param($l) $data[$l].ShortDescription }; break }
        '^ReleaseNotes$' { Set-AllLangs $row { param($l) $data[$l].ReleaseNotes }; break }
        # Title is intentionally left alone: every language here has a package, so
        # the Store derives the name from it. Filling Title risks a reserved-name
        # mismatch, and it's only required for languages without a package.
        '^Feature(\d+)$' {
            $i = [int]$Matches[1] - 1
            if ($i -lt $data[$defaultLocale].Features.Count) { Set-AllLangs $row { param($l) if ($i -lt $data[$l].Features.Count) { $data[$l].Features[$i] } else { $null } } } else { $filled = $false }
            break
        }
        '^(SearchTerm|Keyword)(\d+)$' {
            $i = [int]$Matches[2] - 1
            if ($i -lt $data[$defaultLocale].SearchTerms.Count) { Set-AllLangs $row { param($l) if ($i -lt $data[$l].SearchTerms.Count) { $data[$l].SearchTerms[$i] } else { $null } } } else { $filled = $false }
            break
        }
        '^(Desktop)?Screenshot(\d+)$' {
            $i = [int]$Matches[2] - 1
            if ($i -lt $shotFiles.Count) { Set-Cell $row 'default' "$folderName/$($shotFiles[$i])" } else { $filled = $false }
            break
        }
        '^(Desktop)?ScreenshotCaption(\d+)$' {
            $i = [int]$Matches[2] - 1
            if ($i -lt $capCols.Count) { Set-AllLangs $row { param($l) $data[$l].Captions[$i] } } else { $filled = $false }
            break
        }
        default { $filled = $false }
    }
    if ($filled) { $matched.Add($field) } else { $unmatched.Add($field) }
}

# Rebuild each row against the full header set so every column is emitted.
$out = foreach ($row in $rows) {
    $o = [ordered]@{}
    foreach ($h in $headers) { $o[$h] = $row.$h }
    [pscustomobject]$o
}

New-Item $OutputDir -ItemType Directory -Force | Out-Null
$csvPath = Join-Path $OutputDir "listing-import.csv"
$out | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8
foreach ($f in $shotFiles) { Copy-Item (Join-Path $source "screenshots\$f") $OutputDir -Force }

Write-Host "Wrote $csvPath (UTF-8)"
Write-Host "Filled $($matched.Count) field rows: $($matched -join ', ')"
if ($unmatched.Count) {
    Write-Host ""
    Write-Host "Left untouched ($($unmatched.Count)) — these keep whatever the export had:"
    Write-Host "  $($unmatched -join ', ')"
    Write-Host "If any of these are fields you expected filled (e.g. a differently-named"
    Write-Host "search-term or caption row), tell me the exact Field names and I'll extend the map."
}
Write-Host ""
Write-Host "Copied $($shotFiles.Count) screenshots into '$folderName' (referenced as '$folderName/<file>')."
Write-Host "Import in Partner Center: Import listings -> Upload folder -> select '$folderName'."
