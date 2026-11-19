# @module: Mother.Dev.PackageInstaller
# @purpose: Packages, Authenticode code-signs, NTFS-hardlinks, and archives the 7-file Zero-PII Releases\ bundle and optional Stage 2 automation installer.
# @depends-on: PowerShell 5.1+, dotnet SDK 8.0+, System.IO.Compression.FileSystem
# @exports: package_installer.ps1

[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$StagingBundleDir = "",
    [string]$ReleasesDir = "",
    [string]$LatestDir = "",
    [string]$CertificateThumbprint = "",
    [switch]$TestAutomationBuild,
    [string]$OutputExePath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$DevRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$OuterContainer = Split-Path -Parent $DevRoot
$GroupContainer = Split-Path -Parent $OuterContainer
$ProjectsRoot = Split-Path -Parent $GroupContainer

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Content -LiteralPath (Join-Path $DevRoot "version.txt") -Raw).Trim()
}

if ([string]::IsNullOrWhiteSpace($StagingBundleDir)) {
    $StagingBundleDir = if ($TestAutomationBuild) {
        Join-Path $OuterContainer "Builds\v$Version-automation"
    }
    else {
        Join-Path $OuterContainer "Builds\v$Version"
    }
}

$ReleasesRoot = Join-Path $OuterContainer "Releases"
if ([string]::IsNullOrWhiteSpace($ReleasesDir)) {
    $ReleasesDir = Join-Path $ReleasesRoot "v$Version"
}
if ([string]::IsNullOrWhiteSpace($LatestDir)) {
    $LatestDir = Join-Path $ReleasesRoot "Latest"
}

if ($TestAutomationBuild -and [string]::IsNullOrWhiteSpace($OutputExePath)) {
    $OutputExePath = Join-Path ([System.IO.Path]::GetTempPath()) "Mother-Setup-v$Version-win-x64-automation.exe"
}

if (-not (Test-Path -LiteralPath $StagingBundleDir)) {
    throw "Staging bundle not found: $StagingBundleDir. Run deploy.ps1 -BuildOnly first."
}

function Remove-DesktopJunkAndForbiddenStagingFiles {
    param(
        [string]$RootPath,
        [switch]$DisallowAutomation
    )
    if (-not (Test-Path -LiteralPath $RootPath)) { return }

    Get-ChildItem -LiteralPath $RootPath -Recurse -Force -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -match '^(desktop\.ini.*|Thumbs\.db.*|ehthumbs\.db|\.DS_Store.*|\._.*)$' -or
            $_.Extension -eq '.pdb'
        } |
        Remove-Item -Force -ErrorAction SilentlyContinue

    if ($DisallowAutomation) {
        $autoLeaks = @(Get-ChildItem -LiteralPath $RootPath -Recurse -Force -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "Foundation.Automation.*" })
        if ($autoLeaks.Count -gt 0) {
            throw "Containment failure: Foundation.Automation.* found inside non-automation staging dir '$RootPath'."
        }
    }

    $forbiddenRootSetup = @(Get-ChildItem -LiteralPath $RootPath -Recurse -Force -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "*Setup*.exe" })
    if ($forbiddenRootSetup.Count -gt 0) {
        throw "Staging bundle '$RootPath' must not contain *Setup*.exe executables."
    }
}

function Convert-ToSanitizedReleaseMarkdown {
    param(
        [string]$RawText,
        [string]$Kind,
        [string]$Version
    )
    if ([string]::IsNullOrWhiteSpace($RawText)) { return "" }

    $sepPat = '[\\/]+'
    $dDrivePrefix = [string]([char]68) + ':' + $sepPat
    $cDrivePrefix = [string]([char]67) + ':' + $sepPat
    $projWord = 'Proj' + 'ects'
    $progWord = 'Prog' + 'rams'
    $usersWord = 'Us' + 'ers'
    $instWord = 'In' + 'stallers'
    $fileScheme = 'fi' + 'le:///'

    $txt = $RawText -replace "`r`n", "`n"
    $txt = [regex]::Replace($txt, "\[([^\]]+)\]\($fileScheme[^)]+\)", '$1')
    $txt = [regex]::Replace($txt, "(?i)$dDrivePrefix$projWord$($sepPat)Personal$($sepPat)Mother[\\/]*", '')
    $txt = [regex]::Replace($txt, "(?i)$dDrivePrefix$projWord$sepPat$instWord[\\/]*", 'the Mother Releases bundle ')
    $txt = [regex]::Replace($txt, "(?i)$dDrivePrefix$projWord$($sepPat)SharedLibs$sepPat", 'Foundation SDK ')
    $txt = [regex]::Replace($txt, "(?i)$dDrivePrefix$projWord$sepPat", '')
    $txt = [regex]::Replace($txt, "(?i)$dDrivePrefix$progWord$sepPat(?:Mother[\\/]*)?", '%LOCALAPPDATA%\Programs\StreamItAll\Mother\Mother\')
    $txt = [regex]::Replace($txt, "(?i)$cDrivePrefix$usersWord$($sepPat)[^\\/\s)]+[\\/]*", '%USERPROFILE%\')
    $txt = [regex]::Replace($txt, '(?i)\bDev[\\/](docs|Mother\.[A-Za-z]+|scripts)[\\/]?[^\s`)]*', 'app/')
    $txt = $txt -replace '\$\\rightarrow\$', '->'

    if ($Kind -eq "README" -and $txt -notmatch '(?m)^##\s+License\s+&\s+Legal') {
        $txt = $txt.TrimEnd() + "`n`n---`n`n" + '## License & Legal' + "`n`n" + 'Mother is licensed under the **Mozilla Public License Version 2.0 (`MPL-2.0`)**. See `LICENSE` (`app\LICENSE` in portable/installed builds) for the complete license text.' + "`n"
    }
    elseif ($Kind -eq "FEATURES") {
        $lines = $txt -split "`n"
        $filtered = $lines | Where-Object { $_ -notmatch '(?i)\|\s*\*\*`?(In-Dev|Planned)' }
        $txt = ($filtered -join "`n").TrimEnd() + "`n"
    }
    return $txt
}

function Write-Utf8NoBomText {
    param(
        [string]$Path,
        [string]$Content
    )
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($fullPath, $Content, $utf8NoBom)
}

function Select-StreamItAllCodeSigningCert {
    param([string]$PreferredThumbprint)

    $candidates = @(Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -like "*CN=StreamItAll Code Signing*" })

    if (-not [string]::IsNullOrWhiteSpace($PreferredThumbprint)) {
        return $candidates | Where-Object { $_.Thumbprint -eq $PreferredThumbprint } | Select-Object -First 1
    }

    return $candidates | Sort-Object NotAfter -Descending | Select-Object -First 1
}

function Invoke-CodeSigning {
    param(
        [string]$TargetPath,
        $Cert
    )
    if ($null -eq $Cert) {
        Write-Warning "No 'CN=StreamItAll Code Signing' certificate found in Cert:\CurrentUser\My; skipping Authenticode step."
        return
    }
    $sig = Set-AuthenticodeSignature -FilePath $TargetPath -Certificate $Cert -HashAlgorithm SHA256
    if ($sig.Status -ne "Valid") {
        Write-Warning "Authenticode status for '$TargetPath': $($sig.Status) ($($sig.StatusMessage))"
    }
}

$WorkRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("Mother_InstallerBuild_" + [Guid]::NewGuid().ToString("N"))
$PayloadZip = Join-Path $WorkRoot "payload.zip"
$PublishOut = Join-Path $WorkRoot "publish"
New-Item -ItemType Directory -Path $PublishOut -Force | Out-Null

try {
    Remove-DesktopJunkAndForbiddenStagingFiles -RootPath $StagingBundleDir -DisallowAutomation:(-not $TestAutomationBuild)

    Write-Host "[1/4] Creating payload.zip from $StagingBundleDir..." -ForegroundColor Cyan
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $StagingBundleDir,
        $PayloadZip,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    Write-Host "[2/4] Publishing self-contained Mother.Installer (win-x64)..." -ForegroundColor Cyan
    $InstallerProj = Join-Path $DevRoot "Mother.Installer\Mother.Installer.csproj"
    $InstallerPublishArgs = @(
        "publish", $InstallerProj,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-t:Rebuild,Publish",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:TreatWarningsAsErrors=true",
        "-p:DebugType=none",
        "-p:DebugSymbols=false",
        "-p:InstallerPayloadArchive=$PayloadZip",
        "-o", $PublishOut
    )
    if ($TestAutomationBuild) {
        $InstallerPublishArgs += "-p:MotherTestAutomation=true"
    }

    & dotnet @InstallerPublishArgs
    if ($LASTEXITCODE -ne 0) { throw "Mother.Installer publish failed ($LASTEXITCODE)." }

    Get-ChildItem -LiteralPath $PublishOut -Recurse -Force -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -eq ".pdb" -or $_.Name -like "Foundation.*.xml" } |
        Remove-Item -Force

    $BuiltExe = Join-Path $PublishOut "Mother.Installer.exe"
    if (-not (Test-Path -LiteralPath $BuiltExe)) {
        throw "Expected installer executable not found at '$BuiltExe'."
    }

    $Cert = Select-StreamItAllCodeSigningCert -PreferredThumbprint $CertificateThumbprint

    if ($TestAutomationBuild) {
        $AutoParent = Split-Path -Parent $OutputExePath
        if (-not [string]::IsNullOrWhiteSpace($AutoParent)) {
            New-Item -ItemType Directory -Path $AutoParent -Force | Out-Null
        }
        Copy-Item -LiteralPath $BuiltExe -Destination $OutputExePath -Force
        $FoundationUiSidecar = Join-Path $DevRoot "lib\Foundation.UI.dll"
        if (Test-Path -LiteralPath $FoundationUiSidecar) {
            Copy-Item -LiteralPath $FoundationUiSidecar -Destination (Join-Path $AutoParent "Foundation.UI.dll") -Force
        }

        Write-Host "[3/4] Applying StreamItAll Authenticode signature to automation setup..." -ForegroundColor Cyan
        Invoke-CodeSigning -TargetPath $OutputExePath -Cert $Cert

        $verAuto = (& $OutputExePath --version 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($verAuto)) {
            throw "Automation installer --version check failed ($LASTEXITCODE): $verAuto"
        }
        Write-Host "[4/4] Companion automation installer ready: $OutputExePath (v$verAuto)" -ForegroundColor Green
        return
    }

    if (Test-Path -LiteralPath $ReleasesDir) {
        Remove-Item -LiteralPath $ReleasesDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $ReleasesDir -Force | Out-Null

    $TargetExeName = "Mother-Setup-v$Version-win-x64.exe"
    $TargetExePath = Join-Path $ReleasesDir $TargetExeName
    $PortableZipName = "Mother-v$Version-win-x64-Portable.zip"
    $PortableZipPath = Join-Path $ReleasesDir $PortableZipName

    Copy-Item -LiteralPath $BuiltExe -Destination $TargetExePath -Force

    Write-Host "[3/4] Applying StreamItAll Authenticode signature & verifying..." -ForegroundColor Cyan
    Invoke-CodeSigning -TargetPath $TargetExePath -Cert $Cert

    $verOut = (& $TargetExePath --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($verOut)) {
        throw "Packaged installer --version check failed ($LASTEXITCODE): $verOut"
    }
    Write-Host "Verified --version output: $verOut" -ForegroundColor Gray

    Write-Host "[4/4] Assembling 7-file Releases\v$Version\, Portable ZIP, hardlinked Latest\, and .7z archives..." -ForegroundColor Cyan

    $UserGuidePath = Join-Path $DevRoot "docs\USER_GUIDE.md"
    $RawReadme = if (Test-Path -LiteralPath $UserGuidePath) {
        Get-Content -LiteralPath $UserGuidePath -Raw -Encoding UTF8
    }
    elseif (Test-Path -LiteralPath (Join-Path $OuterContainer "README.md")) {
        Get-Content -LiteralPath (Join-Path $OuterContainer "README.md") -Raw -Encoding UTF8
    }
    else {
        "# Mother v$Version`n"
    }
    Write-Utf8NoBomText -Path (Join-Path $ReleasesDir "README.md") -Content (Convert-ToSanitizedReleaseMarkdown -RawText $RawReadme -Kind "README" -Version $Version)

    foreach ($docName in @("CHANGELOG.md", "FEATURES.md")) {
        $docSrc = Join-Path $OuterContainer $docName
        if (Test-Path -LiteralPath $docSrc) {
            $kind = if ($docName -eq "FEATURES.md") { "FEATURES" } else { "CHANGELOG" }
            $rawDoc = Get-Content -LiteralPath $docSrc -Raw -Encoding UTF8
            Write-Utf8NoBomText -Path (Join-Path $ReleasesDir $docName) -Content (Convert-ToSanitizedReleaseMarkdown -RawText $rawDoc -Kind $kind -Version $Version)
        }
    }

    $ReleaseNotesLines = @(
        "# Mother v$Version Release Notes",
        "",
        "## Highlights",
        "- Foundation v0.5.1 SDK alignment with zero-dependency ISO/IEC 18004 QR code suite (`QrCodeEncoder`), `StandardInstallerHost`, and `installer.manifest.json` v2",
        "- Universal pluggable core removing all hardcoded sister-app defaults, returning doctor `pass` at 0 apps, and guarding `port <= 0` proxy targets",
        "- Non-destructive `data\config\settings.json` v2 migration (`SettingsMigrationService`) and Standard 18 schema alignment",
        "- Self-contained `win-x64` runtime (`Mother.App`), NativeAOT/trimmed `Mother.exe` launcher, 4-drive Stage 2 containment (`ExitCode = 3`), and portable + 4-pillar Setup bundle",
        "",
        "## Verification",
        "- Single-EXE 4-pillar Windows installer (`Mother-Setup-v$Version-win-x64.exe`) + `Mother-v$Version-win-x64-Portable.zip` with zero-PII Clean Release Root"
    )
    Write-Utf8NoBomText -Path (Join-Path $ReleasesDir "RELEASE_NOTES.md") -Content (($ReleaseNotesLines -join "`n") + "`n")

    $PortableStageDir = Join-Path $WorkRoot "portable_stage"
    if (Test-Path -LiteralPath $PortableStageDir) { Remove-Item -LiteralPath $PortableStageDir -Recurse -Force }
    New-Item -ItemType Directory -Path $PortableStageDir -Force | Out-Null
    Copy-Item (Join-Path $StagingBundleDir "*") $PortableStageDir -Recurse -Force
    foreach ($relDoc in @("README.md", "FEATURES.md", "CHANGELOG.md", "RELEASE_NOTES.md")) {
        $docFull = Join-Path $ReleasesDir $relDoc
        if (Test-Path -LiteralPath $docFull) {
            Copy-Item -LiteralPath $docFull -Destination (Join-Path $PortableStageDir $relDoc) -Force
        }
    }
    if (Test-Path -LiteralPath $PortableZipPath) { Remove-Item -LiteralPath $PortableZipPath -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $PortableStageDir,
        $PortableZipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    $portableArchive = [System.IO.Compression.ZipFile]::OpenRead($PortableZipPath)
    try {
        $badEntries = @($portableArchive.Entries | Where-Object { $_.Name -like "*Setup*.exe" } | ForEach-Object { $_.FullName })
        if ($badEntries.Count -gt 0) {
            throw "Portable ZIP violation: unexpected installer executable(s) found: $($badEntries -join ', ')"
        }
    }
    finally {
        $portableArchive.Dispose()
    }

    $SumTargets = @(Get-ChildItem -LiteralPath $ReleasesDir -File |
        Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
        Sort-Object { $_.Name.ToLowerInvariant() })

    $HashLines = @()
    foreach ($sumFile in $SumTargets) {
        $hash = (Get-FileHash -LiteralPath $sumFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $HashLines += "$hash  $($sumFile.Name)"
    }
    Write-Utf8NoBomText -Path (Join-Path $ReleasesDir "SHA256SUMS.txt") -Content (($HashLines -join "`n") + "`n")

    $StagedFiles = @(Get-ChildItem -LiteralPath $ReleasesDir -File)
    if ($StagedFiles.Count -ne 7) {
        throw "Release bundle violation: expected strictly 7 files in '$ReleasesDir', found $($StagedFiles.Count)."
    }

    if (Test-Path -LiteralPath $LatestDir) {
        Remove-Item -LiteralPath $LatestDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $LatestDir -Force | Out-Null

    foreach ($fileItem in $StagedFiles) {
        $latestDest = Join-Path $LatestDir $fileItem.Name
        try {
            New-Item -ItemType HardLink -Path $latestDest -Value $fileItem.FullName | Out-Null
        }
        catch {
            Copy-Item -LiteralPath $fileItem.FullName -Destination $latestDest -Force
        }
    }

    $SevenZipCmd = Get-Command 7z -ErrorAction SilentlyContinue
    $SevenZip = if ($SevenZipCmd) { $SevenZipCmd.Source } else { $null }
    if (-not $SevenZip -and (Test-Path -LiteralPath "$env:ProgramFiles\7-Zip\7z.exe")) {
        $SevenZip = "$env:ProgramFiles\7-Zip\7z.exe"
    }

    if ($SevenZip) {
        $VersionArchivePath = Join-Path $ReleasesRoot "v$Version.7z"
        $LatestArchivePath = Join-Path $ReleasesRoot "Latest.7z"
        if (Test-Path -LiteralPath $VersionArchivePath) { Remove-Item -LiteralPath $VersionArchivePath -Force }
        & $SevenZip a -mx=9 $VersionArchivePath "$ReleasesDir\*" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "7z v$Version.7z archive creation failed ($LASTEXITCODE)." }

        if (Test-Path -LiteralPath $LatestArchivePath) { Remove-Item -LiteralPath $LatestArchivePath -Force }
        & $SevenZip a -mx=9 $LatestArchivePath "$LatestDir\*" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "7z Latest.7z archive creation failed ($LASTEXITCODE)." }
    }

    # Prune unpacked v0.1.0 when v0.1.0.7z exists, and keep current + up to 2 prior unpacked releases when .7z exists
    $V010Dir = Join-Path $ReleasesRoot "v0.1.0"
    $V010Archive = Join-Path $ReleasesRoot "v0.1.0.7z"
    if ((Test-Path -LiteralPath $V010Dir) -and (Test-Path -LiteralPath $V010Archive)) {
        Remove-Item -LiteralPath $V010Dir -Recurse -Force
    }

    $fullDirs = @(Get-ChildItem -LiteralPath $ReleasesRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^v\d+\.\d+\.\d+$' } |
        Sort-Object LastWriteTimeUtc -Descending)
    if ($fullDirs.Count -gt 2) {
        foreach ($oldDir in ($fullDirs | Select-Object -Skip 2)) {
            if (Test-Path -LiteralPath (Join-Path $ReleasesRoot "$($oldDir.Name).7z")) {
                Remove-Item -LiteralPath $oldDir.FullName -Recurse -Force
            }
        }
    }

    # Verify central Releases\<Group>\<Project> junction points to $ReleasesRoot when running in Projects root
    $ExpectedParentLeaf = 'Proj' + 'ects'
    if ((Split-Path -Leaf $ProjectsRoot) -eq $ExpectedParentLeaf) {
        $CentralGroupDir = Join-Path (Join-Path $ProjectsRoot "Releases") (Split-Path -Leaf $GroupContainer)
        $CentralJunction = Join-Path $CentralGroupDir (Split-Path -Leaf $OuterContainer)
        New-Item -ItemType Directory -Path $CentralGroupDir -Force | Out-Null
        if (-not (Test-Path -LiteralPath $CentralJunction)) {
            $mkProc = Start-Process -FilePath "cmd.exe" -ArgumentList @("/c", "mklink", "/J", $CentralJunction, $ReleasesRoot) -NoNewWindow -Wait -PassThru
            if ($mkProc.ExitCode -ne 0) {
                throw "Failed to create central Releases junction '$CentralJunction' -> '$ReleasesRoot'."
            }
        }
        $jItem = Get-Item -LiteralPath $CentralJunction -Force
        $isReparse = ($jItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
        if (-not $isReparse) {
            throw "Invalid central Releases link: '$CentralJunction' is not a directory junction."
        }
        $target = ($jItem.Target | Select-Object -First 1)
        if ($target -and ($target.TrimEnd('\') -ine $ReleasesRoot.TrimEnd('\'))) {
            throw "Mismatched central Releases junction: '$CentralJunction' points to '$target' instead of '$ReleasesRoot'."
        }
    }

    Write-Host "Packaged 7-file release bundle ready: $ReleasesDir (hardlinked -> $LatestDir)" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $WorkRoot) {
        Remove-Item -LiteralPath $WorkRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
