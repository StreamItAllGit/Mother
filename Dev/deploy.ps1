# @module: Mother.Dev.Deploy
# @purpose: Deterministic Clean-Root build packager (Builds\v<Version>\) and production-safe atomic deployment script for Mother.
# @depends-on: PowerShell 5.1+, dotnet SDK 8.0+
# @exports: deploy.ps1

[CmdletBinding()]
param(
    [string]$TargetDir = "",
    [string]$OutputDir = "",
    [switch]$BuildOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$DevRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$OuterContainer = Split-Path -Parent $DevRoot
$GroupContainer = Split-Path -Parent $OuterContainer
$ProjectsRoot = Split-Path -Parent $GroupContainer
$Version = (Get-Content (Join-Path $DevRoot "version.txt") -Raw).Trim()

$FoundationCoreDll = Join-Path $DevRoot "lib\Foundation.Core.dll"
if (-not (Test-Path -LiteralPath $FoundationCoreDll)) {
    $SdkSync = Join-Path $ProjectsRoot "SharedLibs\Foundation\v0.4.0\tools\sync_foundation_sdk.py"
    if (Test-Path -LiteralPath $SdkSync) {
        Write-Host "[0/3] Syncing Foundation v0.4.0 SDK into Dev\lib\..." -ForegroundColor Cyan
        python $SdkSync --sync-consumer $OuterContainer --web-dir "Dev\Mother.Server\wwwroot\vendor\foundation"
        if ($LASTEXITCODE -ne 0) { throw "Foundation SDK sync failed ($LASTEXITCODE)." }
    } else {
        throw "Dev\lib\Foundation.Core.dll is missing and sync_foundation_sdk.py was not found."
    }
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $OuterContainer "Builds\v$Version"
}

if ([string]::IsNullOrWhiteSpace($TargetDir)) {
    $TargetDir = Join-Path $env:LOCALAPPDATA "Programs\StreamItAll\Mother\Mother"
}

Write-Host "=== Mother Clean-Root Build Packager (v$Version) ===" -ForegroundColor Cyan

$StageDir = Join-Path ([System.IO.Path]::GetTempPath()) ("Mother_Stage_" + [Guid]::NewGuid().ToString("N"))
$StageAppDir = Join-Path $StageDir "app"
$StageConfigDir = Join-Path $StageAppDir "config"
$StageScriptsDir = Join-Path $StageDir "scripts"
$StageInteractiveDir = Join-Path $StageScriptsDir "interactive"
$LauncherTemp = Join-Path ([System.IO.Path]::GetTempPath()) ("Mother_Launcher_" + [Guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path $StageAppDir, $StageConfigDir, $StageScriptsDir, $StageInteractiveDir, $LauncherTemp -Force | Out-Null

    Write-Host "[1/3] Publishing Mother.App -> app\..." -ForegroundColor Yellow
    dotnet publish (Join-Path $DevRoot "Mother.App\Mother.App.csproj") `
        -c Release -r win-x64 --self-contained false `
        -p:TreatWarningsAsErrors=true -p:DebugType=none -p:DebugSymbols=false `
        -o $StageAppDir
    if ($LASTEXITCODE -ne 0) { throw "Mother.App publish failed ($LASTEXITCODE)." }

    Write-Host "[2/3] Publishing Mother.Launcher -> Mother.exe..." -ForegroundColor Yellow
    dotnet publish (Join-Path $DevRoot "Mother.Launcher\Mother.Launcher.csproj") `
        -c Release -r win-x64 --self-contained false `
        -p:PublishSingleFile=true -p:TreatWarningsAsErrors=true -p:DebugType=none -p:DebugSymbols=false `
        -o $LauncherTemp
    if ($LASTEXITCODE -ne 0) { throw "Mother.Launcher publish failed ($LASTEXITCODE)." }

    Copy-Item (Join-Path $LauncherTemp "Mother.exe") (Join-Path $StageDir "Mother.exe") -Force

    foreach ($fileName in @("version.txt", "suite.manifest.json", "mother.manifest.json")) {
        $src = Join-Path $DevRoot $fileName
        if (Test-Path $src) {
            Copy-Item $src (Join-Path $StageAppDir $fileName) -Force
        }
    }

    $SettingsExample = Join-Path $DevRoot "config\settings.example.json"
    if (Test-Path -LiteralPath $SettingsExample) {
        Copy-Item $SettingsExample (Join-Path $StageConfigDir "settings.example.json") -Force
    }

    $LicenseSrc = Join-Path $OuterContainer "LICENSE"
    if (Test-Path -LiteralPath $LicenseSrc) {
        Copy-Item $LicenseSrc (Join-Path $StageDir "LICENSE") -Force
        Copy-Item $LicenseSrc (Join-Path $StageAppDir "LICENSE") -Force
    }

    $RuntimeAgents = @(
        "# Mother Runtime Root (v$Version)"
        ""
        '- **Role:** StreamItAll Shared Suite Daemon, Reverse-Proxy Gateway (`127.0.0.1:5100`), Cold-Wake Scheduler & Post-Run Power Coordinator.'
        '- **Clean Release Root:** Launch `Mother.exe` at install root; binaries live under `app\`, batch helpers under `scripts\`, user state under `data\` and `logs\`.'
        '- **Sandbox Flags:** Pass `--test-mode --data-dir <dir> --log-dir <dir> --port <port>` to isolate all I/O.'
        '- **License:** MPL-2.0 (see `LICENSE` / `app\LICENSE`).'
    )
    $RuntimeAgents | Set-Content -Path (Join-Path $StageDir "AGENTS.md") -Encoding UTF8

    $MigrationsSrc = Join-Path $DevRoot "migrations"
    $MigrationsDst = Join-Path $StageAppDir "migrations"
    if (Test-Path -LiteralPath $MigrationsSrc) {
        if (Test-Path -LiteralPath $MigrationsDst) { Remove-Item $MigrationsDst -Recurse -Force }
        New-Item -ItemType Directory -Path $MigrationsDst -Force | Out-Null
        Copy-Item (Join-Path $MigrationsSrc "*") $MigrationsDst -Recurse -Force
    }

    $AssetsSrc = Join-Path $DevRoot "Assets"
    $AssetsDst = Join-Path $StageAppDir "Assets"
    if (Test-Path -LiteralPath $AssetsSrc) {
        if (Test-Path -LiteralPath $AssetsDst) { Remove-Item $AssetsDst -Recurse -Force }
        New-Item -ItemType Directory -Path $AssetsDst -Force | Out-Null
        Copy-Item (Join-Path $AssetsSrc "*") $AssetsDst -Recurse -Force
    }

    $ScriptsSrc = Join-Path $DevRoot "scripts"
    if (Test-Path $ScriptsSrc) {
        Get-ChildItem $ScriptsSrc -Filter "*.bat" -File | ForEach-Object {
            Copy-Item $_.FullName (Join-Path $StageScriptsDir $_.Name) -Force
        }
        $IntSrc = Join-Path $ScriptsSrc "interactive"
        if (Test-Path $IntSrc) {
            Get-ChildItem $IntSrc -Filter "*.bat" -File | ForEach-Object {
                Copy-Item $_.FullName (Join-Path $StageInteractiveDir $_.Name) -Force
            }
        }
    }

    # Remove any accidental .pdb sidecars or nested OS junk from staged output
    Get-ChildItem $StageDir -Include @("*.pdb", "desktop.ini", "Thumbs.db", "ehthumbs.db", ".DS_Store", "._*") -Recurse -Force -File -ErrorAction SilentlyContinue | Remove-Item -Force

    Write-Host "[3/3] Staging Clean Release Root -> $OutputDir" -ForegroundColor Yellow
    if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    Copy-Item (Join-Path $StageDir "*") $OutputDir -Recurse -Force

    if (-not $BuildOnly) {
        Write-Host "Deploying binaries atomically to $TargetDir (preserving data\ and logs\)..." -ForegroundColor Cyan
        New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
        foreach ($sub in @("app", "scripts")) {
            $dstSub = Join-Path $TargetDir $sub
            if (Test-Path $dstSub) { Remove-Item $dstSub -Recurse -Force }
            Copy-Item (Join-Path $OutputDir $sub) $dstSub -Recurse -Force
        }
        foreach ($rootItem in @("Mother.exe", "AGENTS.md", "LICENSE")) {
            $srcItem = Join-Path $OutputDir $rootItem
            if (Test-Path $srcItem) { Copy-Item $srcItem (Join-Path $TargetDir $rootItem) -Force }
        }
    }

    Write-Host "Mother v$Version staged cleanly at $OutputDir" -ForegroundColor Green
}
finally {
    foreach ($tmp in @($StageDir, $LauncherTemp)) {
        if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
