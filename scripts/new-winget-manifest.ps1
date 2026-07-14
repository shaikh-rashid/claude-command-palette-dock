# Generates the three WinGet manifest files (version / installer / defaultLocale)
# for a signed release MSIX, ready to submit to microsoft/winget-pkgs.
#
# Everything version-specific is read out of the MSIX itself (identity, version,
# min OS) and computed from its bytes (InstallerSha256, SignatureSha256, and the
# PackageFamilyName that winget validates at install time), so the only inputs
# are the package and the public URL it will be downloaded from.
#
# Example (after downloading the GitLab/GitHub release asset):
#   .\scripts\new-winget-manifest.ps1 `
#       -MsixPath release-output\ClaudeUsageDock.msix `
#       -InstallerUrl "https://gitlab.com/shaikh.rashid/claude-command-palette-dock/-/releases/v1.0.0/downloads/ClaudeUsageDock.msix"
#
# IMPORTANT: winget-pkgs validation actually installs the package, which
# requires its signature to chain to a trusted root. A self-signed release
# certificate will NOT pass — see docs/DISTRIBUTION.md for the two ways
# around that (Microsoft Store first, or a real code-signing certificate).
# Manifests generated here still work locally for testing with:
#   winget install --manifest <OutputDir>\<version>

param(
    [Parameter(Mandatory)][string]$MsixPath,
    [Parameter(Mandatory)][string]$InstallerUrl,
    [string]$PackageIdentifier = "ShaikhRashid.ClaudeUsageDock",
    [string]$OutputDir = "winget-manifests"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

# The publisher-id half of a PackageFamilyName: the first 8 bytes of the
# SHA-256 of the UTF-16LE publisher string, in Crockford Base32 (the 64 bits
# are read as 13 five-bit groups, the last padded with a zero bit).
function Get-PublisherIdHash([string]$Publisher) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash([System.Text.Encoding]::Unicode.GetBytes($Publisher)) }
    finally { $sha.Dispose() }

    $bits = -join ($hash[0..7] | ForEach-Object { [Convert]::ToString($_, 2).PadLeft(8, '0') })
    $bits += '0'
    $alphabet = "0123456789abcdefghjkmnpqrstvwxyz"
    return -join (0..12 | ForEach-Object { $alphabet[[Convert]::ToInt32($bits.Substring($_ * 5, 5), 2)] })
}

$MsixPath = (Resolve-Path $MsixPath).Path

# Identity and versions from the package's own manifest.
$zip = [System.IO.Compression.ZipFile]::OpenRead($MsixPath)
try {
    $manifestEntry = $zip.GetEntry("AppxManifest.xml")
    if (-not $manifestEntry) { throw "No AppxManifest.xml in $MsixPath — is this an MSIX?" }
    $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
    [xml]$appxManifest = $reader.ReadToEnd()
    $reader.Dispose()

    # winget validates SignatureSha256 against the package's AppxSignature.p7x.
    $sigEntry = $zip.GetEntry("AppxSignature.p7x")
    if (-not $sigEntry) { throw "No AppxSignature.p7x in $MsixPath — the MSIX must be signed before generating manifests." }
    $sigStream = $sigEntry.Open()
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $signatureSha256 = ([BitConverter]::ToString($sha.ComputeHash($sigStream)) -replace '-', '').ToUpperInvariant()
    $sha.Dispose()
    $sigStream.Dispose()
}
finally {
    $zip.Dispose()
}

$identity = $appxManifest.Package.Identity
$packageVersion = ($identity.Version -replace '\.0$', '')   # 1.0.0.0 -> 1.0.0 (matches the release tag)
$minOs = $appxManifest.Package.Dependencies.TargetDeviceFamily.MinVersion
$packageFamilyName = "$($identity.Name)_$(Get-PublisherIdHash $identity.Publisher)"
$installerSha256 = (Get-FileHash $MsixPath -Algorithm SHA256).Hash

$versionDir = Join-Path $OutputDir $packageVersion
New-Item $versionDir -ItemType Directory -Force | Out-Null

Set-Content -Path (Join-Path $versionDir "$PackageIdentifier.yaml") -Encoding utf8 -Value @"
# Created with scripts/new-winget-manifest.ps1
PackageIdentifier: $PackageIdentifier
PackageVersion: $packageVersion
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@

Set-Content -Path (Join-Path $versionDir "$PackageIdentifier.installer.yaml") -Encoding utf8 -Value @"
# Created with scripts/new-winget-manifest.ps1
PackageIdentifier: $PackageIdentifier
PackageVersion: $packageVersion
Platform:
- Windows.Desktop
MinimumOSVersion: $minOs
InstallerType: msix
PackageFamilyName: $packageFamilyName
Installers:
- Architecture: x64
  InstallerUrl: $InstallerUrl
  InstallerSha256: $installerSha256
  SignatureSha256: $signatureSha256
ManifestType: installer
ManifestVersion: 1.6.0
"@

Set-Content -Path (Join-Path $versionDir "$PackageIdentifier.locale.en-US.yaml") -Encoding utf8 -Value @"
# Created with scripts/new-winget-manifest.ps1
PackageIdentifier: $PackageIdentifier
PackageVersion: $packageVersion
PackageLocale: en-US
Publisher: $($appxManifest.Package.Properties.PublisherDisplayName)
PackageName: $($appxManifest.Package.Properties.DisplayName)
License: MIT
LicenseUrl: https://gitlab.com/shaikh.rashid/claude-command-palette-dock/-/blob/main/LICENSE
PackageUrl: https://gitlab.com/shaikh.rashid/claude-command-palette-dock
ShortDescription: Live Claude Code subscription usage in the PowerToys Command Palette dock.
Description: |-
  A PowerToys Command Palette extension that shows your Claude Code
  subscription usage at a glance: a dock tile with live session and weekly
  percentages, plus a details page with usage bars, burn-rate projections,
  a usage breakdown, and a monthly heatmap. Localized in English, German,
  French, Spanish, Japanese, and Simplified Chinese.
Moniker: claude-usage-dock
Tags:
- claude
- powertoys
- command-palette
- usage
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@

Write-Host "Manifests written to $versionDir\"
Write-Host "  PackageFamilyName: $packageFamilyName"
Write-Host "  InstallerSha256:   $installerSha256"
Write-Host "  SignatureSha256:   $signatureSha256"
Write-Host ""
Write-Host "Validate locally:  winget validate $versionDir"
Write-Host "Test install:      winget install --manifest $versionDir  (requires 'winget settings' LocalManifestFiles + a trusted signature)"
Write-Host "Submit:            copy the folder to manifests/s/ShaikhRashid/ClaudeUsageDock/$packageVersion in a microsoft/winget-pkgs fork and open a PR (or use wingetcreate)."
