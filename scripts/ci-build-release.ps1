# Builds, packs, and signs the release MSIX in CI. Not used for local dev —
# see build-and-install.ps1 for that (it also installs and needs an
# elevation prompt for the first-run dev cert, neither of which apply here).

param(
    [Parameter(Mandatory)][string]$PfxPath,
    [Parameter(Mandatory)][string]$PfxPassword,
    [Parameter(Mandatory)][string]$OutputDir
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$projectDir = Join-Path $repoRoot "ClaudeUsageDock"
$csproj = Join-Path $projectDir "ClaudeUsageDock.csproj"
$publishDir = Join-Path $projectDir "bin\Release\net9.0-windows10.0.26100.0\win-x64\publish"
$stagingDir = Join-Path $repoRoot "msix-staging"

. (Join-Path $PSScriptRoot "BuildTools.ps1")
Assert-WindowsSdkPlatform -PlatformVersion "10.0.26100.0"

New-Item $OutputDir -ItemType Directory -Force | Out-Null
$msixPath = Join-Path $OutputDir "ClaudeUsageDock.msix"
$certPath = Join-Path $OutputDir "ClaudeUsageDock-Release.cer"

# Load the signing cert up front: it validates the PFX and password with a
# clear error before spending time on the build, and its subject is needed to
# patch the manifest publisher (signtool refuses to sign an MSIX whose
# Identity Publisher differs from the certificate subject — the manifest in
# the repo carries the dev publisher, not the release one).
#
# EphemeralKeySet keeps the private key in memory and never writes it to the
# on-disk user key container. The default (DefaultKeySet) persists the key
# there, which throws "Access denied" on the GitLab SaaS Windows runner whose
# job context can't write to that store. This object is only read for its
# Subject/Thumbprint and to export the public .cer — the actual signing hands
# the PFX path straight to signtool — so an in-memory key is all it needs.
Write-Host "Loading signing certificate..."
$securePassword = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText
$cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
    $PfxPath, $securePassword,
    [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
Write-Host "Signing as: $($cert.Subject) (thumbprint $($cert.Thumbprint))"

Write-Host "Publishing..."
& dotnet publish $csproj -c Release -r win-x64 --self-contained false -p:GenerateAppxPackageOnBuild=false -v minimal -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Staging and packing MSIX..."
New-MsixPackage -ProjectDir $projectDir -PublishDir $publishDir -StagingDir $stagingDir -OutputMsixPath $msixPath -PublisherOverride $cert.Subject

Write-Host "Signing MSIX..."
Set-MsixSignature -MsixPath $msixPath -PfxPath $PfxPath -PfxPassword $PfxPassword -ViaCertStore

Write-Host "Exporting the public certificate for users to trust..."
Export-Certificate -Cert $cert -FilePath $certPath | Out-Null

Write-Host "Done. Artifacts in $OutputDir"
