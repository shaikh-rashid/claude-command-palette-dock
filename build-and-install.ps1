# Builds, packages, signs, and sideloads Claude Usage Dock into Command Palette.
# Re-run this after every code change. The first run elevates once to trust the
# dev certificate; later runs only prompt if Windows requires it.

$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot "ClaudeUsageDock"
$csproj = Join-Path $projectDir "ClaudeUsageDock.csproj"
$binDir = Join-Path $projectDir "bin"
$publishDir = Join-Path $projectDir "bin\Release\net9.0-windows10.0.26100.0\win-x64\publish"
$stagingDir = Join-Path $binDir "msix-staging"
$msixPath = Join-Path $binDir "ClaudeUsageDock.msix"
$certPath = Join-Path $binDir "ClaudeUsageDock.cer"
$pfxPath = Join-Path $binDir "ClaudeUsageDock.pfx"
$pfxPassword = "ClaudeUsageDockDev!"
$certSubject = "CN=ClaudeUsageDock-Dev"
$packageIdentityName = "ClaudeUsageDock"

. (Join-Path $PSScriptRoot "scripts\BuildTools.ps1")

$dotnetExe = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnetExe)) {
    throw ".NET SDK not found at '$dotnetExe'. Install the .NET 9 SDK from https://dotnet.microsoft.com/download and re-run this script."
}
if (-not (& $dotnetExe --list-sdks | Select-String "^9\.")) {
    throw "No .NET 9 SDK is registered with dotnet ('dotnet --list-sdks' shows none). Install it from https://dotnet.microsoft.com/download and re-run this script."
}
Assert-WindowsSdkPlatform -PlatformVersion "10.0.26100.0"

Write-Host "[1/5] dotnet publish"
& $dotnetExe publish $csproj -c Release -r win-x64 --self-contained false -p:GenerateAppxPackageOnBuild=false -v minimal -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "[2/5] Ensure a dev signing certificate exists"
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $certSubject } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $certSubject `
        -KeyUsage DigitalSignature `
        -FriendlyName "Claude Usage Dock Dev" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
}
$securePassword = ConvertTo-SecureString -String $pfxPassword -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
Export-Certificate -Cert $cert -FilePath $certPath | Out-Null

Write-Host "[3/5] Stage and pack MSIX"
New-MsixPackage -ProjectDir $projectDir -PublishDir $publishDir -StagingDir $stagingDir -OutputMsixPath $msixPath

Write-Host "[4/5] Sign MSIX"
Set-MsixSignature -MsixPath $msixPath -PfxPath $pfxPath -PfxPassword $pfxPassword

Write-Host "[5/5] Install"
$certTrusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
    Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
if (-not $certTrusted) {
    # Trusting the dev cert in LocalMachine needs admin; only elevate the first time.
    $elevatedScript = @"
`$ErrorActionPreference = 'Stop'
Import-Certificate -FilePath '$certPath' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Add-AppxPackage -Path '$msixPath' -ForceTargetApplicationShutdown
"@
    $elevatedScriptPath = Join-Path $binDir "elevated-install.ps1"
    Set-Content -Path $elevatedScriptPath -Value $elevatedScript
    $proc = Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$elevatedScriptPath`"" -PassThru -Wait
    if ($proc.ExitCode -ne 0) { throw "Elevated install failed (exit code $($proc.ExitCode))" }
} else {
    Add-AppxPackage -Path $msixPath -ForceTargetApplicationShutdown
}

$installedVersion = (Get-AppxPackage -Name $packageIdentityName).Version
$expectedVersion = ([xml](Get-Content (Join-Path $projectDir "Package.appxmanifest"))).Package.Identity.Version
if ($installedVersion -ne $expectedVersion) {
    throw "Installed version $installedVersion does not match manifest version $expectedVersion — install did not take effect."
}

Write-Host ""
Write-Host "Restarting Command Palette..."
Get-Process -Name "Microsoft.CmdPal.UI" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
explorer.exe "shell:AppsFolder\Microsoft.CommandPalette_8wekyb3d8bbwe!App"

Write-Host ""
Write-Host "Installed:"
Get-AppxPackage -Name $packageIdentityName | Format-Table Name, Version, PackageFamilyName -AutoSize
Write-Host "Open Command Palette (Win+Alt+Space), then Settings -> Dock -> add the Claude Usage band."
