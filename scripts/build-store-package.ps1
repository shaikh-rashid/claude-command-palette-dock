# Builds the UNSIGNED MSIX for a Microsoft Store submission.
#
# The Store re-signs every package with its own certificate during ingestion,
# so this package must NOT be signed here — but it must carry the identity
# Partner Center assigned when the app name was reserved. Find the three
# values under: Partner Center -> Apps and games -> (your app) ->
# Product management -> Product identity:
#
#   Package/Identity/Name                  -> -IdentityName
#   Package/Identity/Publisher             -> -Publisher   (a CN=GUID string)
#   Package/Properties/PublisherDisplayName -> -PublisherDisplayName
#
# Example:
#   .\scripts\build-store-package.ps1 `
#       -IdentityName "12345YourName.ClaudeUsageDock" `
#       -Publisher "CN=A1B2C3D4-0000-1111-2222-333344445555" `
#       -PublisherDisplayName "Your Name"
#
# Upload the resulting store-output\ClaudeUsageDock-Store.msix on the
# submission's Packages page. See docs/DISTRIBUTION.md for the full walkthrough.

param(
    [Parameter(Mandatory)][string]$IdentityName,
    [Parameter(Mandatory)][string]$Publisher,
    [Parameter(Mandatory)][string]$PublisherDisplayName,
    [string]$OutputDir = "store-output"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$projectDir = Join-Path $repoRoot "ClaudeUsageDock"
$csproj = Join-Path $projectDir "ClaudeUsageDock.csproj"
$publishDir = Join-Path $projectDir "bin\Release\net9.0-windows10.0.26100.0\win-x64\publish"
$stagingDir = Join-Path $repoRoot "msix-staging-store"

. (Join-Path $PSScriptRoot "BuildTools.ps1")
Assert-WindowsSdkPlatform -PlatformVersion "10.0.26100.0"

if ($Publisher -notmatch '^CN=') {
    throw "-Publisher must be the full X.500 string from Partner Center (starts with 'CN='), got: $Publisher"
}

New-Item $OutputDir -ItemType Directory -Force | Out-Null
$msixPath = Join-Path $OutputDir "ClaudeUsageDock-Store.msix"

Write-Host "Publishing..."
& dotnet publish $csproj -c Release -r win-x64 --self-contained false -p:GenerateAppxPackageOnBuild=false -v minimal -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Staging and packing the Store MSIX (unsigned, Store identity)..."
New-MsixPackage -ProjectDir $projectDir -PublishDir $publishDir -StagingDir $stagingDir `
    -OutputMsixPath $msixPath `
    -PublisherOverride $Publisher `
    -IdentityNameOverride $IdentityName `
    -PublisherDisplayNameOverride $PublisherDisplayName

$version = ([xml](Get-Content (Join-Path $projectDir "Package.appxmanifest"))).Package.Identity.Version
Write-Host ""
Write-Host "Done: $msixPath (version $version, identity '$IdentityName', unsigned)"
Write-Host "Upload it in Partner Center on the submission's Packages page."
