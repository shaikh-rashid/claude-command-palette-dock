# One-time setup: generates the persistent self-signed certificate CI uses to
# sign every release MSIX, and prints what to paste into GitHub repo secrets.
#
# Run this ONCE, locally, then add the two secrets it prints to:
#   GitHub repo -> Settings -> Secrets and variables -> Actions -> New repository secret
#     RELEASE_PFX_BASE64    (the long base64 blob)
#     RELEASE_PFX_PASSWORD  (the generated password)
#
# The certificate is reused for every release so users who trust it once
# never have to re-trust a new certificate on a later update. Re-running this
# script generates a NEW certificate — only do that if you intend to rotate
# it (which requires every existing user to re-trust the new one).

$ErrorActionPreference = "Stop"

$certSubject = "CN=ClaudeUsageDock-Release"
$outDir = Join-Path $env:TEMP "ClaudeUsageDock-release-cert"
$pfxPath = Join-Path $outDir "release.pfx"

New-Item $outDir -ItemType Directory -Force | Out-Null

$existing = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $certSubject }
if ($existing) {
    Write-Host "A certificate with subject '$certSubject' already exists (thumbprint $($existing.Thumbprint))." -ForegroundColor Yellow
    $answer = Read-Host "Generate a NEW one anyway? Existing users would need to re-trust it. Type 'yes' to proceed"
    if ($answer -ne "yes") {
        Write-Host "Aborted. No changes made."
        exit 0
    }
}

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $certSubject `
    -KeyUsage DigitalSignature `
    -FriendlyName "Claude Usage Dock Release" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -NotAfter (Get-Date).AddYears(10) `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

$password = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
$securePassword = ConvertTo-SecureString -String $password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null

$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath))
$base64Path = Join-Path $outDir "release-pfx-base64.txt"
Set-Content -Path $base64Path -Value $base64 -NoNewline

Remove-Item $pfxPath -Force

Write-Host ""
Write-Host "Certificate generated (thumbprint $($cert.Thumbprint))." -ForegroundColor Green
Write-Host ""
Write-Host "Add these two GitHub repo secrets (Settings -> Secrets and variables -> Actions):"
Write-Host ""
Write-Host "  RELEASE_PFX_BASE64   -> contents of: $base64Path"
Write-Host "  RELEASE_PFX_PASSWORD -> $password"
Write-Host ""
Write-Host "Copy both values now — the password is not saved anywhere else. Once the secrets" -ForegroundColor Yellow
Write-Host "are added, delete $base64Path (it contains your private key)." -ForegroundColor Yellow
