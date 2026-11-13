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
$Version = (Get-Content (Join-Path $DevRoot "version.txt") -Raw).Trim()

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $OuterContainer "Builds\v$Version"
}

if ([string]::IsNullOrWhiteSpace($TargetDir)) {
    $nonSystemPrograms = [System.IO.DriveInfo]::GetDrives() |
        Where-Object { $_.IsReady -and $_.DriveType -eq 'Fixed' -and $_.RootDirectory.FullName -ne ([System.IO.Path]::GetPathRoot($env:SystemRoot)) } |
        ForEach-Object { Join-Path $_.RootDirectory.FullName "Programs" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
    if ($nonSystemPrograms) {
        $TargetDir = Join-Path $nonSystemPrograms "Mother"
    } else {
        $TargetDir = Join-Path $env:LOCALAPPDATA "Programs\Mother"
    }
}

Write-Host "=== Mother Clean-Root Build Packager (v$Version) ===" -ForegroundColor Cyan

$StageDir = Join-Path ([System.IO.Path]::GetTempPath()) ("Mother_Stage_" + [Guid]::NewGuid().ToString("N"))
$StageAppDir = Join-Path $StageDir "app"
$StageScriptsDir = Join-Path $StageDir "scripts"
$StageInteractiveDir = Join-Path $StageScriptsDir "interactive"
$LauncherTemp = Join-Path ([System.IO.Path]::GetTempPath()) ("Mother_Launcher_" + [Guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path $StageAppDir, $StageScriptsDir, $StageInteractiveDir, $LauncherTemp -Force | Out-Null

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

    $AgentsSrc = Join-Path $OuterContainer "AGENTS.md"
    if (Test-Path $AgentsSrc) {
        Copy-Item $AgentsSrc (Join-Path $StageDir "AGENTS.md") -Force
    }

    $QuickStartLines = @(
        "# Mother v$Version — Quick-Start & User Guide",
        "",
        "**Mother** is the Single Shared Suite Daemon (`http://127.0.0.1:5100/`), unified Windows system tray sentinel, reverse-proxy gateway, disk traffic cop, post-run power coordinator, and cross-app settings broker for the Personal Suite (**HouseKeeping**, **ShadowLibrary**, **RedRoom**, **GreenRoom**, **StreamEngine**, and **legacy-sister-app**).",
        "",
        "## Quick-Start",
        "1. Launch **`Mother.exe`** to open the desktop shell (`http://127.0.0.1:5100/`).",
        "2. Switch across installed sister apps from the top horizontal bar (`Alt+1` .. `Alt+7`), or pop an app out with `Mother.exe --window <app-id>`.",
        "3. Use **Traffic Cop** (`Alt+1`) to inspect `.hklock` drive locks and apply safe-stagger coexistence schedules.",
        "4. Configure **Post-Run Power** (`StayAwake`, `Sleep`, `Shutdown`) and rollback snapshots (`Ctrl+S`) in **Settings**.",
        "",
        "## Shortcuts",
        "- `Alt+1..7`: Switch Suite / Sister-App tabs",
        "- `Ctrl+B`: Toggle sidebar rail",
        "- `Ctrl+K`: Open Suite Omni-Search",
        "- `Ctrl+S`: Save Settings snapshot",
        "- `Ctrl+``: Toggle docked Suite Terminal drawer",
        "- `?`: Show Keyboard Shortcuts"
    )
    $QuickStartLines | Set-Content -Path (Join-Path $StageDir "README.md") -Encoding UTF8

    $MigrationsSrc = Join-Path $DevRoot "migrations"
    if (Test-Path $MigrationsSrc) {
        Copy-Item $MigrationsSrc (Join-Path $StageAppDir "migrations") -Recurse -Force
    }

    $AssetsSrc = Join-Path $DevRoot "Assets"
    if (Test-Path $AssetsSrc) {
        Copy-Item $AssetsSrc (Join-Path $StageAppDir "Assets") -Recurse -Force
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

    # Remove any accidental .pdb sidecars from staged output
    Get-ChildItem $StageDir -Filter "*.pdb" -Recurse -File | Remove-Item -Force

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
        foreach ($rootItem in @("Mother.exe", "AGENTS.md", "README.md")) {
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
