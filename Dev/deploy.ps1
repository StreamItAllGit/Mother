# @module: Mother.Dev.Deploy
# @purpose: Deterministic Clean-Root build packager (Builds\v<Version>\) and production-safe atomic deployment script for Mother.
# @depends-on: PowerShell 5.1+, dotnet SDK 8.0+
# @exports: deploy.ps1

[CmdletBinding()]
param(
    [string]$TargetDir = "",
    [string]$OutputDir = "",
    [switch]$BuildOnly,
    [switch]$TestAutomationBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$SdkVersion = "0.5.1"
$DevRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$OuterContainer = Split-Path -Parent $DevRoot
$GroupContainer = Split-Path -Parent $OuterContainer
$ProjectsRoot = Split-Path -Parent $GroupContainer
$Version = (Get-Content (Join-Path $DevRoot "version.txt") -Raw).Trim()

function Test-MsvcToolchainAvailable {
    if (Get-Command cl.exe -ErrorAction SilentlyContinue) {
        return $true
    }

    $pf86 = [Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
    $pf = [Environment]::GetEnvironmentVariable("ProgramFiles")
    $vswhereCandidates = @(
        $(if (-not [string]::IsNullOrWhiteSpace($pf86)) { Join-Path $pf86 "Microsoft Visual Studio\Installer\vswhere.exe" }),
        $(if (-not [string]::IsNullOrWhiteSpace($pf)) { Join-Path $pf "Microsoft Visual Studio\Installer\vswhere.exe" })
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) }

    foreach ($vswhere in $vswhereCandidates) {
        $vcInstall = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($vcInstall | Out-String).Trim())) {
            return $true
        }
    }

    return $false
}

$FoundationCoreDll = Join-Path $DevRoot "lib\Foundation.Core.dll"
$FoundationAutomationDll = Join-Path $DevRoot "lib\Foundation.Automation.dll"
$SdkManifestPath = Join-Path $DevRoot "lib\sdk-manifest.json"
$NeedsSdkSync = (-not (Test-Path -LiteralPath $FoundationCoreDll)) -or ($TestAutomationBuild -and -not (Test-Path -LiteralPath $FoundationAutomationDll))
if (-not $NeedsSdkSync -and (Test-Path -LiteralPath $SdkManifestPath)) {
    try {
        $sdkManifest = Get-Content -LiteralPath $SdkManifestPath -Raw | ConvertFrom-Json
        if ($sdkManifest.version -ne $SdkVersion) { $NeedsSdkSync = $true }
    }
    catch {
        $NeedsSdkSync = $true
    }
}

$SdkSyncCandidates = @(
    (Join-Path $ProjectsRoot "Personal\Foundation\Dev\tools\sync_foundation_sdk.py"),
    (Join-Path $ProjectsRoot "SharedLibs\Foundation\v$SdkVersion\tools\sync_foundation_sdk.py")
)
$SdkSync = $SdkSyncCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if ($NeedsSdkSync) {
    if ($null -ne $SdkSync) {
        Write-Host "[0/3] Syncing Foundation v$SdkVersion SDK into Dev\lib\..." -ForegroundColor Cyan
        python $SdkSync --sync-consumer $OuterContainer --source-version "v$SdkVersion"
        if ($LASTEXITCODE -ne 0) { throw "Foundation SDK sync failed ($LASTEXITCODE)." }
    }
    elseif (-not (Test-Path -LiteralPath $FoundationCoreDll)) {
        throw "Dev\lib\Foundation.Core.dll is missing and sync_foundation_sdk.py was not found."
    }
}

# Copy strictly the 3 vanilla runtime assets into wwwroot\vendor\foundation\ (never .tsx/.ts/Web/)
$RequiredWebAssets = @("foundation-tokens.css", "foundation-terminal.css", "foundation-terminal.js")
$VendorDir = Join-Path $DevRoot "Mother.Server\wwwroot\vendor\foundation"
$WebAssetsSource = @(
    (Join-Path $DevRoot "lib\WebAssets"),
    (Join-Path $ProjectsRoot "SharedLibs\Foundation\v$SdkVersion\WebAssets")
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if ($null -ne $WebAssetsSource) {
    New-Item -ItemType Directory -Path $VendorDir -Force | Out-Null
    foreach ($assetName in $RequiredWebAssets) {
        $assetSrc = Join-Path $WebAssetsSource $assetName
        if (-not (Test-Path -LiteralPath $assetSrc)) {
            throw "Missing required Foundation web asset '$assetName' in '$WebAssetsSource'."
        }
        Copy-Item -LiteralPath $assetSrc -Destination (Join-Path $VendorDir $assetName) -Force
    }
}
else {
    foreach ($assetName in $RequiredWebAssets) {
        if (-not (Test-Path -LiteralPath (Join-Path $VendorDir $assetName))) {
            throw "Missing required Foundation web asset '$assetName' in '$VendorDir'."
        }
    }
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = if ($TestAutomationBuild) {
        Join-Path $OuterContainer "Builds\v$Version-automation"
    }
    else {
        Join-Path $OuterContainer "Builds\v$Version"
    }
}

if ([string]::IsNullOrWhiteSpace($TargetDir)) {
    $TargetDir = Join-Path $env:LOCALAPPDATA "Programs\StreamItAll\Mother\Mother"
}

$BuildFlavorLabel = if ($TestAutomationBuild) { "v$Version Automation" } else { "v$Version" }
Write-Host "=== Mother Clean-Root Build Packager ($BuildFlavorLabel) ===" -ForegroundColor Cyan

$StageDir = Join-Path ([System.IO.Path]::GetTempPath()) ("Mother_Stage_" + [Guid]::NewGuid().ToString("N"))
$StageAppDir = Join-Path $StageDir "app"
$StageConfigDir = Join-Path $StageAppDir "config"
$StageScriptsDir = Join-Path $StageDir "scripts"
$StageInteractiveDir = Join-Path $StageScriptsDir "interactive"
$LauncherTemp = Join-Path ([System.IO.Path]::GetTempPath()) ("Mother_Launcher_" + [Guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path $StageAppDir, $StageConfigDir, $StageScriptsDir, $StageInteractiveDir, $LauncherTemp -Force | Out-Null

    Write-Host "[1/3] Publishing Mother.App (self-contained win-x64, no IL trim) -> app\..." -ForegroundColor Yellow
    $AppObjDir = Join-Path $DevRoot "Mother.App\obj"
    if (Test-Path -LiteralPath $AppObjDir) {
        Get-ChildItem -LiteralPath $AppObjDir -Recurse -File -Filter "*.deps.json" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    }
    $AppProj = Join-Path $DevRoot "Mother.App\Mother.App.csproj"
    $AppPublishArgs = @(
        "publish", $AppProj,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-t:Rebuild,Publish",
        "-p:TreatWarningsAsErrors=true",
        "-p:DebugType=none",
        "-p:DebugSymbols=false",
        "-o", $StageAppDir
    )
    if ($TestAutomationBuild) {
        $AppPublishArgs += "-p:MotherTestAutomation=true"
    }

    & dotnet @AppPublishArgs
    if ($LASTEXITCODE -ne 0) { throw "Mother.App publish failed ($LASTEXITCODE)." }

    if (-not $TestAutomationBuild) {
        Get-ChildItem -LiteralPath $StageAppDir -Recurse -Force -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "Foundation.Automation.*" } |
            Remove-Item -Force
        if (Test-Path -LiteralPath (Join-Path $StageAppDir "Foundation.Automation.dll")) {
            throw "Containment failure: Foundation.Automation.dll leaked into non-automation Mother.App publish output."
        }
    }

    $HasMsvc = Test-MsvcToolchainAvailable
    $LauncherProj = Join-Path $DevRoot "Mother.Launcher\Mother.Launcher.csproj"
    if ($HasMsvc) {
        Write-Host "[2/3] Publishing Mother.Launcher (NativeAOT) -> Mother.exe..." -ForegroundColor Yellow
        & dotnet publish $LauncherProj `
            -c Release -r win-x64 -t:Rebuild,Publish `
            -p:PublishAot=true -p:StripSymbols=true -p:NuGetAudit=false `
            -p:TreatWarningsAsErrors=true -p:DebugType=none -p:DebugSymbols=false `
            -o $LauncherTemp
    }
    else {
        Write-Host "[2/3] MSVC not detected; publishing Mother.Launcher (single-file partial trim) -> Mother.exe..." -ForegroundColor Yellow
        & dotnet publish $LauncherProj `
            -c Release -r win-x64 --self-contained true -t:Rebuild,Publish `
            -p:PublishSingleFile=true -p:PublishTrimmed=true -p:TrimMode=partial `
            -p:TreatWarningsAsErrors=true -p:DebugType=none -p:DebugSymbols=false `
            -o $LauncherTemp
    }
    if ($LASTEXITCODE -ne 0) { throw "Mother.Launcher publish failed ($LASTEXITCODE)." }

    Copy-Item -LiteralPath (Join-Path $LauncherTemp "Mother.exe") -Destination (Join-Path $StageDir "Mother.exe") -Force

    foreach ($fileName in @("version.txt", "suite.manifest.json", "mother.manifest.json")) {
        $src = Join-Path $DevRoot $fileName
        if (Test-Path -LiteralPath $src) {
            Copy-Item -LiteralPath $src -Destination (Join-Path $StageAppDir $fileName) -Force
        }
    }

    $SettingsExample = Join-Path $DevRoot "config\settings.example.json"
    if (Test-Path -LiteralPath $SettingsExample) {
        Copy-Item -LiteralPath $SettingsExample -Destination (Join-Path $StageConfigDir "settings.example.json") -Force
    }

    # Keep app\LICENSE ONLY; never write AGENTS.md or loose root LICENSE into Builds\
    $LicenseSrc = Join-Path $OuterContainer "LICENSE"
    if (Test-Path -LiteralPath $LicenseSrc) {
        Copy-Item -LiteralPath $LicenseSrc -Destination (Join-Path $StageAppDir "LICENSE") -Force
    }

    $MigrationsSrc = Join-Path $DevRoot "migrations"
    $MigrationsDst = Join-Path $StageAppDir "migrations"
    if (Test-Path -LiteralPath $MigrationsSrc) {
        if (Test-Path -LiteralPath $MigrationsDst) { Remove-Item -LiteralPath $MigrationsDst -Recurse -Force }
        New-Item -ItemType Directory -Path $MigrationsDst -Force | Out-Null
        Copy-Item (Join-Path $MigrationsSrc "*") $MigrationsDst -Recurse -Force
    }

    $AssetsSrc = Join-Path $DevRoot "Assets"
    $AssetsDst = Join-Path $StageAppDir "Assets"
    if (Test-Path -LiteralPath $AssetsSrc) {
        if (Test-Path -LiteralPath $AssetsDst) { Remove-Item -LiteralPath $AssetsDst -Recurse -Force }
        New-Item -ItemType Directory -Path $AssetsDst -Force | Out-Null
        Copy-Item (Join-Path $AssetsSrc "*") $AssetsDst -Recurse -Force
    }

    $ScriptsSrc = Join-Path $DevRoot "scripts"
    if (Test-Path -LiteralPath $ScriptsSrc) {
        Get-ChildItem -LiteralPath $ScriptsSrc -Filter "*.bat" -File | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $StageScriptsDir $_.Name) -Force
        }
        $IntSrc = Join-Path $ScriptsSrc "interactive"
        if (Test-Path -LiteralPath $IntSrc) {
            Get-ChildItem -LiteralPath $IntSrc -Filter "*.bat" -File | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $StageInteractiveDir $_.Name) -Force
            }
        }
    }

    # Remove any accidental .pdb sidecars or nested OS junk from staged output (PS 5.1 safe)
    Get-ChildItem -LiteralPath $StageDir -Recurse -Force -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -eq '.pdb' -or $_.Name -match '^(desktop\.ini.*|Thumbs\.db.*|ehthumbs\.db|\.DS_Store.*|\._.*)$' } |
        Remove-Item -Force -ErrorAction SilentlyContinue

    Write-Host "[3/3] Staging Clean Release Root -> $OutputDir" -ForegroundColor Yellow
    if (Test-Path -LiteralPath $OutputDir) { Remove-Item -LiteralPath $OutputDir -Recurse -Force }
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    Copy-Item (Join-Path $StageDir "*") $OutputDir -Recurse -Force

    if (-not $TestAutomationBuild) {
        $leakedAuto = @(Get-ChildItem -LiteralPath $OutputDir -Recurse -Force -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "Foundation.Automation.*" })
        if ($leakedAuto.Count -gt 0) {
            $leakedAuto | Remove-Item -Force
            if (Test-Path -LiteralPath (Join-Path $OutputDir "app\Foundation.Automation.dll")) {
                throw "Containment failure: Foundation.Automation.dll present in non-automation build '$OutputDir'."
            }
        }
    }

    if (Test-Path -LiteralPath (Join-Path $OutputDir "AGENTS.md")) {
        throw "Containment failure: AGENTS.md must never be staged inside '$OutputDir'."
    }
    if (Test-Path -LiteralPath (Join-Path $OutputDir "LICENSE")) {
        throw "Layout failure: loose root LICENSE must not be staged inside '$OutputDir' (keep app\LICENSE only)."
    }

    if (-not $BuildOnly -and -not $TestAutomationBuild) {
        Write-Host "Deploying binaries atomically to $TargetDir (preserving data\ and logs\)..." -ForegroundColor Cyan
        New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
        foreach ($sub in @("app", "scripts")) {
            $dstSub = Join-Path $TargetDir $sub
            if (Test-Path -LiteralPath $dstSub) { Remove-Item -LiteralPath $dstSub -Recurse -Force }
            Copy-Item (Join-Path $OutputDir $sub) $dstSub -Recurse -Force
        }
        Copy-Item -LiteralPath (Join-Path $OutputDir "Mother.exe") -Destination (Join-Path $TargetDir "Mother.exe") -Force
        foreach ($staleRootFile in @("AGENTS.md", "LICENSE")) {
            $stalePath = Join-Path $TargetDir $staleRootFile
            if (Test-Path -LiteralPath $stalePath) { Remove-Item -LiteralPath $stalePath -Force }
        }
    }

    Write-Host "Mother $BuildFlavorLabel staged cleanly at $OutputDir" -ForegroundColor Green
}
finally {
    foreach ($tmp in @($StageDir, $LauncherTemp)) {
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
