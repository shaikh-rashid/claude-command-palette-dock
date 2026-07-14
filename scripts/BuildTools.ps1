# Shared MSIX packaging helpers. Dot-sourced by build-and-install.ps1 (local
# dev sideload) and the CI release workflow, so the staging/pack/sign steps
# can't silently drift between the two — this is the logic that had the
# Assets\Assets nesting bug, so it only gets fixed in one place from now on.

function Assert-WindowsSdkPlatform {
    param([Parameter(Mandatory)][string]$PlatformVersion)

    # dotnet publish fails here with an opaque CsWinRT/NETSDK1140 error if this
    # is missing — checking directly gives a much clearer message. Hit locally
    # when a machine had only a newer preview SDK installed, not this one.
    $platformXmlPath = "C:\Program Files (x86)\Windows Kits\10\Platforms\UAP\$PlatformVersion\Platform.xml"
    if (-not (Test-Path $platformXmlPath)) {
        throw "Windows SDK UAP platform $PlatformVersion is not installed (missing '$platformXmlPath'). Install it with: winget install Microsoft.WindowsSDK.10.0.26100"
    }
}

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

function New-MsixPackage {
    param(
        [Parameter(Mandatory)][string]$ProjectDir,
        [Parameter(Mandatory)][string]$PublishDir,
        [Parameter(Mandatory)][string]$StagingDir,
        [Parameter(Mandatory)][string]$OutputMsixPath,
        # MSIX signing requires Identity Publisher == signing cert subject, so
        # release builds must swap out the dev publisher the manifest ships with.
        [string]$PublisherOverride
    )

    if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }
    New-Item $StagingDir -ItemType Directory | Out-Null
    Copy-Item "$PublishDir\*" $StagingDir -Recurse -Force

    # Copy Assets\* (contents), not the Assets folder itself — the publish
    # output already has Assets\icons from CopyToOutputDirectory, so copying
    # the folder as a unit nests it as Assets\Assets instead of merging.
    Copy-Item (Join-Path $ProjectDir "Assets\*") (Join-Path $StagingDir "Assets") -Recurse -Force
    Copy-Item (Join-Path $ProjectDir "Package.appxmanifest") (Join-Path $StagingDir "AppxManifest.xml") -Force

    if ($PublisherOverride) {
        $manifestPath = Join-Path $StagingDir "AppxManifest.xml"
        [xml]$manifest = Get-Content $manifestPath
        $manifest.Package.Identity.Publisher = $PublisherOverride
        $manifest.Save($manifestPath)
    }

    $makeAppxExe = Find-WindowsKitTool -ToolName "makeappx.exe"
    if (Test-Path $OutputMsixPath) { Remove-Item $OutputMsixPath -Force }
    & $makeAppxExe pack /d $StagingDir /p $OutputMsixPath /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }
}

function Set-MsixSignature {
    param(
        [Parameter(Mandatory)][string]$MsixPath,
        [Parameter(Mandatory)][string]$PfxPath,
        [Parameter(Mandatory)][string]$PfxPassword,
        # On GitLab's hosted Windows runners, signtool's own /f PFX import fails
        # with 0x80090010 (NTE_PERM, "Store::ImportCertObject() failed") — the
        # job context can't create the per-user key container it wants. When set,
        # import the PFX into a certificate store here and sign by thumbprint
        # instead, which sidesteps signtool's internal import. Local dev leaves
        # this off and signs straight from the PFX, which works with a real
        # interactive user profile.
        [switch]$ViaCertStore
    )

    $signToolExe = Find-WindowsKitTool -ToolName "signtool.exe"

    if ($ViaCertStore) {
        $securePassword = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText
        # Prefer LocalMachine: its key lives in the machine key container, which
        # the admin runner can write and which doesn't depend on a loaded user
        # profile (the per-user container is what's denied). Fall back to
        # CurrentUser if the machine store is refused.
        $storeLocation = "Cert:\LocalMachine\My"
        $useMachineStore = $true
        try {
            $imported = Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation $storeLocation -Password $securePassword
        }
        catch {
            Write-Host "LocalMachine PFX import failed ($($_.Exception.Message)); falling back to the CurrentUser store."
            $storeLocation = "Cert:\CurrentUser\My"
            $useMachineStore = $false
            $imported = Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation $storeLocation -Password $securePassword
        }

        try {
            # /sm makes signtool search the machine store; omit it for CurrentUser.
            $storeArgs = if ($useMachineStore) { @("/sm") } else { @() }
            # Don't pipe to Out-Null: signtool writes "Error information:
            # SignerSign() failed" diagnostics to stdout, and hiding them cost a
            # CI debugging round.
            & $signToolExe sign /fd SHA256 @storeArgs /sha1 $imported.Thumbprint $MsixPath
            if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
        }
        finally {
            Remove-Item -Path (Join-Path $storeLocation $imported.Thumbprint) -Force -ErrorAction SilentlyContinue
        }

        return
    }

    # Don't pipe to Out-Null: signtool writes "Error information: SignerSign()
    # failed" diagnostics to stdout, and hiding them cost a CI debugging round.
    & $signToolExe sign /fd SHA256 /a /f $PfxPath /p $PfxPassword $MsixPath
    if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
}
