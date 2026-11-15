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

function Convert-ToSanitizedReleaseMarkdown {
    param(
        [string]$RawText,
        [string]$Kind,
        [string]$Version
    )
    if ([string]::IsNullOrWhiteSpace($RawText)) { return "" }
    $txt = $RawText -replace "`r`n", "`n"
    # Strip internal file:/// links down to link label
    $txt = [regex]::Replace($txt, '\[([^\]]+)\]\(file:///[^)]+\)', '$1')
    # Neutralize local workspace/user/legacy drive paths
    $txt = [regex]::Replace($txt, '(?i)D:[\\/]+Projects[\\/]+Personal[\\/]+Mother[\\/]+', '')
    $txt = [regex]::Replace($txt, '(?i)D:[\\/]+Projects[\\/]+Installers[\\/]*', 'the Mother Releases bundle ')
    $txt = [regex]::Replace($txt, '(?i)D:[\\/]+Projects[\\/]+SharedLibs[\\/]+', 'Foundation SDK ')
    $txt = [regex]::Replace($txt, '(?i)D:[\\/]+Projects[\\/]+', '')
    $txt = [regex]::Replace($txt, '(?i)D:[\\/]+Programs[\\/]+(?:Mother[\\/]*)?', '%LOCALAPPDATA%\Programs\StreamItAll\Mother\Mother\')
    $txt = [regex]::Replace($txt, '(?i)C:[\\/]+Users[\\/]+[^\\/\s)]+[\\/]*', '%USERPROFILE%\')
    $txt = [regex]::Replace($txt, '(?i)\bDev[\\/](docs|Mother\.[A-Za-z]+|scripts)[\\/]?[^\s`)]*', 'app/')
    $txt = $txt -replace '\$\\rightarrow\$', '->'
    if ($Kind -eq "README") {
        $txt = $txt.TrimEnd() + "`n`n---`n`n" + '## License & Legal' + "`n`n" + 'Mother is licensed under the **Mozilla Public License Version 2.0 (`MPL-2.0`)**. See `LICENSE` (`app\LICENSE` in portable/installed builds) for the complete license text.' + "`n"
    }
    elseif ($Kind -eq "FEATURES") {
        $lines = $txt -split "`n"
        $filtered = $lines | Where-Object { $_ -notmatch '(?i)\|\s*\*\*`?(In-Dev|Planned)' }
        $txt = ($filtered -join "`n").TrimEnd() + "`n"
    }
    return $txt
}

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

    # Standard 03 §7.1: preserve historical installers in <ProjectName>\Installers\
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

    Write-Host "[4/4] Mirroring to $MirrorInstallersDir and assembling Releases\Latest\..." -ForegroundColor Cyan
    Get-ChildItem -Path $MirrorInstallersDir -Filter "Mother-Setup-v*.exe" -File -ErrorAction SilentlyContinue | Remove-Item -Force
    $MirrorExePath = Join-Path $MirrorInstallersDir $TargetExeName
    Copy-Item $TargetExePath $MirrorExePath -Force
    $verOut = (& $TargetExePath --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($verOut)) {
        throw "Packaged installer --version check failed ($LASTEXITCODE): $verOut"
    }
    Write-Host "Verified --version output: $verOut" -ForegroundColor Gray

    $ReleasesDir = Join-Path $OuterContainer "Releases"
    $LatestDir = Join-Path $ReleasesDir "Latest"
    $IsFullRelease = ($Version -notmatch '-')
    $VersionDir = Join-Path $ReleasesDir "v$Version"
    $BundleDir = if ($IsFullRelease) { $VersionDir } else { $LatestDir }
    if (Test-Path $BundleDir) { Remove-Item $BundleDir -Recurse -Force }
    New-Item -ItemType Directory -Path $BundleDir -Force | Out-Null

    Copy-Item $TargetExePath (Join-Path $BundleDir $TargetExeName) -Force

    $UserGuidePath = Join-Path $DevRoot "docs\USER_GUIDE.md"
    $RawReadme = if (Test-Path $UserGuidePath) { Get-Content $UserGuidePath -Raw -Encoding UTF8 } elseif (Test-Path (Join-Path $OuterContainer "README.md")) { Get-Content (Join-Path $OuterContainer "README.md") -Raw -Encoding UTF8 } else { "# Mother v$Version`n" }
    (Convert-ToSanitizedReleaseMarkdown -RawText $RawReadme -Kind "README" -Version $Version) | Set-Content -Path (Join-Path $BundleDir "README.md") -Encoding UTF8

    foreach ($docName in @("CHANGELOG.md", "FEATURES.md")) {
        $docSrc = Join-Path $OuterContainer $docName
        if (Test-Path $docSrc) {
            $kind = if ($docName -eq "FEATURES.md") { "FEATURES" } else { "CHANGELOG" }
            $rawDoc = Get-Content $docSrc -Raw -Encoding UTF8
            (Convert-ToSanitizedReleaseMarkdown -RawText $rawDoc -Kind $kind -Version $Version) | Set-Content -Path (Join-Path $BundleDir $docName) -Encoding UTF8
        }
    }

    $ReleaseNotesLines = @(
        "# Mother v$Version Release Notes",
        "",
        "## Highlights",
        "- Foundation SDK v0.4.0 alignment, MPL-2.0 licensing, and StreamItAll\Mother umbrella Clean Release Root",
        "- WorkerArgs and commands.startDaemon supervision, sibling Sandbox containment, and Cold-Wake schedules[] loop + idle hibernation",
        "- POST/GET /api/v1/scheduler/defer, /app/{appId} 307 subpath forwarding, and expanded foundation-terminal commands",
        "",
        "## Verification",
        "- Single-EXE 4-pillar Windows installer with zero-PII Clean Release Root"
    )
    $ReleaseNotesLines | Set-Content -Path (Join-Path $BundleDir "RELEASE_NOTES.md") -Encoding UTF8

    $HashLines = @()
    foreach ($sumTarget in @($TargetExeName, "README.md", "FEATURES.md", "CHANGELOG.md", "RELEASE_NOTES.md")) {
        $sumFile = Join-Path $BundleDir $sumTarget
        if (Test-Path -LiteralPath $sumFile) {
            $HashLines += "$((Get-FileHash $sumFile -Algorithm SHA256).Hash.ToLowerInvariant())  $sumTarget"
        }
    }
    $HashLines | Set-Content -Path (Join-Path $BundleDir "SHA256SUMS.txt") -Encoding UTF8

    $SevenZipCmd = Get-Command 7z -ErrorAction SilentlyContinue
    $SevenZip = if ($SevenZipCmd) { $SevenZipCmd.Source } else { $null }
    if (-not $SevenZip -and (Test-Path "$env:ProgramFiles\7-Zip\7z.exe")) { $SevenZip = "$env:ProgramFiles\7-Zip\7z.exe" }

    if ($IsFullRelease) {
        if (Test-Path $LatestDir) { Remove-Item $LatestDir -Recurse -Force }
        New-Item -ItemType Directory -Path $LatestDir -Force | Out-Null
        Copy-Item (Join-Path $BundleDir "*") $LatestDir -Recurse -Force

        $VersionArchivePath = Join-Path $ReleasesDir "v$Version.7z"
        if ($SevenZip) {
            if (Test-Path $VersionArchivePath) { Remove-Item $VersionArchivePath -Force }
            & $SevenZip a -mx=9 $VersionArchivePath "$BundleDir\*" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "7z v$Version.7z archive creation failed." }
        }

        # Standard 03 §6.2: keep up to 2 most recent unpacked full releases
        $fullDirs = @(Get-ChildItem -Path $ReleasesDir -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^v\d+\.\d+\.\d+$' } |
            Sort-Object LastWriteTimeUtc -Descending)
        if ($fullDirs.Count -gt 2) {
            $fullDirs | Select-Object -Skip 2 | ForEach-Object { Remove-Item $_.FullName -Recurse -Force }
        }
    }

    $LatestArchivePath = Join-Path $ReleasesDir "Latest.7z"
    if ($SevenZip) {
        if (Test-Path $LatestArchivePath) { Remove-Item $LatestArchivePath -Force }
        & $SevenZip a -mx=9 $LatestArchivePath "$LatestDir\*" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "7z Latest.7z archive creation failed." }
    }

    Write-Host "Packaged installer ready: $TargetExePath" -ForegroundColor Green
}
finally {
    if (Test-Path $WorkRoot) {
        Remove-Item $WorkRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
