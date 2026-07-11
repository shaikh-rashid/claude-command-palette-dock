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

function Find-WindowsKitTool {
    param([Parameter(Mandatory)][string]$ToolName)

    $kitsRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    if (-not (Test-Path $kitsRoot)) {
        throw "Windows 10 SDK not found under '$kitsRoot'. Install the 'Windows 10/11 SDK' component via Visual Studio Installer, then re-run this script."
    }

    $candidate = Get-ChildItem $kitsRoot -Directory |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$ToolName" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1

    if (-not $candidate) {
        throw "Could not find $ToolName under '$kitsRoot'. Install the 'Windows 10/11 SDK' component via Visual Studio Installer, then re-run this script."
    }

    return $candidate
}

$dotnetExe = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnetExe)) {
    throw ".NET SDK not found at '$dotnetExe'. Install the .NET 9 SDK from https://dotnet.microsoft.com/download and re-run this script."
}
if (-not (& $dotnetExe --list-sdks | Select-String "^9\.")) {
    throw "No .NET 9 SDK is registered with dotnet ('dotnet --list-sdks' shows none). Install it from https://dotnet.microsoft.com/download and re-run this script."
}

$makeAppxExe = Find-WindowsKitTool -ToolName "makeappx.exe"
$signToolExe = Find-WindowsKitTool -ToolName "signtool.exe"

Write-Host "[1/6] dotnet publish"
& $dotnetExe publish $csproj -c Release -r win-x64 --self-contained false -p:GenerateAppxPackageOnBuild=false -v minimal -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "[2/6] Stage MSIX payload"
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item $stagingDir -ItemType Directory | Out-Null
Copy-Item "$publishDir\*" $stagingDir -Recurse -Force
Copy-Item (Join-Path $projectDir "Assets\*") (Join-Path $stagingDir "Assets") -Recurse -Force
Copy-Item (Join-Path $projectDir "Package.appxmanifest") (Join-Path $stagingDir "AppxManifest.xml") -Force

Write-Host "[3/6] Ensure a dev signing certificate exists"
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

Write-Host "[4/6] Pack MSIX"
if (Test-Path $msixPath) { Remove-Item $msixPath -Force }
& $makeAppxExe pack /d $stagingDir /p $msixPath /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

Write-Host "[5/6] Sign MSIX"
& $signToolExe sign /fd SHA256 /a /f $pfxPath /p $pfxPassword $msixPath | Out-Null
if ($LASTEXITCODE -ne 0) { throw "signtool failed" }

Write-Host "[6/6] Install"
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
