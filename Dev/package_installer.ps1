# @module: Mother.Dev.PackageInstaller
# @purpose: Packages, Authenticode code-signs, mirrors, and audits the Zero-PII single-EXE Mother-Setup-v<Version>-win-x64.exe installer.
# @depends-on: PowerShell 5.1+, dotnet SDK 8.0+, System.IO.Compression.FileSystem
# @exports: package_installer.ps1

[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$StagingBundleDir = "",
    [string]$InstallersDir = "",
    [string]$MirrorInstallersDir = "",
    [string]$CertificateThumbprint = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$DevRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$OuterContainer = Split-Path -Parent $DevRoot
$GroupContainer = Split-Path -Parent $OuterContainer
$ProjectsRoot = Split-Path -Parent $GroupContainer

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Content (Join-Path $DevRoot "version.txt") -Raw).Trim()
}
if ([string]::IsNullOrWhiteSpace($StagingBundleDir)) {
    $StagingBundleDir = Join-Path $OuterContainer "Builds\v$Version"
}
if ([string]::IsNullOrWhiteSpace($InstallersDir)) {
    $InstallersDir = Join-Path $OuterContainer "Installers"
}
if ([string]::IsNullOrWhiteSpace($MirrorInstallersDir)) {
    $MirrorInstallersDir = Join-Path $ProjectsRoot "Installers"
}

if (-not (Test-Path -LiteralPath $StagingBundleDir)) {
    throw "Staging bundle not found: $StagingBundleDir. Run deploy.ps1 -BuildOnly first."
}

New-Item -ItemType Directory -Path $InstallersDir, $MirrorInstallersDir -Force | Out-Null

$WorkRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("Mother_InstallerBuild_" + [Guid]::NewGuid().ToString("N"))
$PayloadZip = Join-Path $WorkRoot "payload.zip"
$PublishOut = Join-Path $WorkRoot "publish"
New-Item -ItemType Directory -Path $PublishOut -Force | Out-Null

try {
    Write-Host "[1/4] Creating payload.zip from $StagingBundleDir..." -ForegroundColor Cyan
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $StagingBundleDir,
        $PayloadZip,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    Write-Host "[2/4] Publishing self-contained Mother.Installer (win-x64)..." -ForegroundColor Cyan
    $InstallerProj = Join-Path $DevRoot "Mother.Installer\Mother.Installer.csproj"
    dotnet publish $InstallerProj `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:TreatWarningsAsErrors=true `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -p:InstallerPayloadArchive="$PayloadZip" `
        -o $PublishOut
    if ($LASTEXITCODE -ne 0) { throw "Mother.Installer publish failed ($LASTEXITCODE)." }

    Get-ChildItem -Path $PublishOut -Include @("*.pdb", "Foundation.*.xml") -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force
    $BuiltExe = Join-Path $PublishOut "Mother.Installer.exe"
    $TargetExeName = "Mother-Setup-v$Version-win-x64.exe"
    $TargetExePath = Join-Path $InstallersDir $TargetExeName
    Copy-Item $BuiltExe $TargetExePath -Force

    Write-Host "[3/4] Applying Authenticode code signature..." -ForegroundColor Cyan
    $Cert = $null
    if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $Cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Thumbprint -eq $CertificateThumbprint } | Select-Object -First 1
    } else {
        $Cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Sort-Object NotAfter -Descending | Select-Object -First 1
    }

    if ($null -ne $Cert) {
        $sig = Set-AuthenticodeSignature -FilePath $TargetExePath -Certificate $Cert -HashAlgorithm SHA256
        if ($sig.Status -ne "Valid") {
            Write-Warning "Authenticode status: $($sig.Status)"
        }
    } else {
        Write-Warning "No code-signing certificate found in Cert:\CurrentUser\My; skipping Authenticode step."
    }

    Write-Host "[4/4] Mirroring to $MirrorInstallersDir and verifying..." -ForegroundColor Cyan
    $MirrorExePath = Join-Path $MirrorInstallersDir $TargetExeName
    Copy-Item $TargetExePath $MirrorExePath -Force
    & $TargetExePath --version
    if ($LASTEXITCODE -ne 0) { throw "Packaged installer --version check failed ($LASTEXITCODE)." }

    Write-Host "Packaged installer ready: $TargetExePath" -ForegroundColor Green
}
finally {
    if (Test-Path $WorkRoot) {
        Remove-Item $WorkRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
