# Changelog

All notable changes to `Mother` will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to strict 3-part [Semantic Versioning (`MAJOR.MINOR.PATCH`)](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-11-15
### Added
- **Cold-Wake Scheduler Execution Loop & Idle Hibernation (`T7`)**:
  - Added [`FleetScheduleEvaluator.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Features/Supervision/FleetScheduleEvaluator.cs) to evaluate sister manifest `schedules[]`, enforce `FleetCoexistenceArbitrator` + `.hklock` checks before scheduled Cold-Wake runs, support `runIfMissed`, persist atomic `data\scheduler-state.json`, and expose `GET`/`POST /api/v1/scheduler/defer`.
  - Added `ColdWakeCoordinator.EvaluateIdleHibernationAsync` to hibernate idle `Cold` workers automatically after `idleTimeoutMinutes` (`15m` default) once `activeJobs + queuedJobs == 0`.
- **Command-Aware Worker Supervision (`WorkerLaunchCommandBuilder.cs`, `T7`)**:
  - Extracted [`WorkerLaunchCommandBuilder.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Features/Supervision/WorkerLaunchCommandBuilder.cs) supporting manifest `workerArgs`, `commands.startDaemon` primary / `commands.start` fallback resolution, sandbox-contained `WorkingDirectory`, and `MANAGED_BY_SUITE=1` + `MOTHER_GATEWAY_URL` injection.
- **Subpath Proxy Forwarding & Expanded `foundation-terminal` (`T7`, `T8`)**:
  - Updated [`ColdWakeProxyMiddleware.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Server/Endpoints/ColdWakeProxyMiddleware.cs) to emit `307 Temporary Redirect` from `/app/{appId}` (no trailing slash) to `/app/{appId}/` preserving query strings, forward trailing-slash `/app/{appId}/` directly to upstream `/`, proxy `/api/v1/apps/{appId}` root requests, disable HTTP response buffering on `text/event-stream` SSE routes, return early when aborted/started SSE clients disconnect post-headers, and forward `X-Forwarded-Prefix: /app/{appId}`.
  - Added [`TerminalCommandDispatcher.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Server/Endpoints/TerminalCommandDispatcher.cs) supporting `help`, `status`, `health`, `apps`, `traffic`, `locks`, `coexistence`, `doctor`, `logs [count]`, `purge-stale`, `reload`, `wake <appId>`, `stop <appId>`, and validated `power [StayAwake|Sleep|Shutdown]`.
- **`Foundation v0.4.0` Umbrella Wizard & Config Overlay (`T1`, `T2`, `T6`)**:
  - Replaced legacy setup host in [`MotherInstallerRunner.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Installer/MotherInstallerRunner.cs) + [`MotherInstallerIntegration.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Installer/MotherInstallerIntegration.cs) with `Foundation.Installer.InstallerEngineCore` + `InstallerWizardForm`, Studio/Suite defaults (`%LOCALAPPDATA%\Programs\StreamItAll\Mother\Mother`), `--profile`, `--suite-dir`, `--migrate-legacy`, `--autostart`, and `--visible-test` screenshot capture.
  - Added neutral [`Dev\config\settings.example.json`](file:///<WORKSPACE_ROOT>/Mother/Dev/config/settings.example.json) staged to `app\config\settings.example.json`.

### Changed
- **SDK Pin, Sibling Sandbox Containment & `MPL-2.0` License Alignment (`T1`, `T3`, `T4`, `T5`)**:
  - Synced `Foundation v0.4.0` into `Dev\lib\` and `wwwroot\vendor\foundation\`; untracked `Dev/lib/*.{dll,pdb,xml,deps.json}` and updated `Dev\deploy.ps1` to auto-run `sync_foundation_sdk.py --sync-consumer` when SDK DLLs are absent.
  - Aligned `Dev\suite.manifest.json`, `Dev\deploy.ps1`, and runtime metadata to `MPL-2.0` and `sdkVersion: "0.4.0"`.
  - Updated [`PathResolver.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Core/Configuration/PathResolver.cs) and [`ManifestRegistryService.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Features/Discovery/ManifestRegistryService.cs) for `StreamItAll\Mother\` peer sister discovery, HKCU + `MOTHER_HOME` lookup, read-only legacy `<PROGRAMS_ROOT>/` fallback, `SandboxEnvironment.ResolveSiblingSandboxRoot` sibling containment, and residency aliases (`on-demand` -> `cold`, `always-on` -> `warm`).
  - Relocated `targetDrives` out of `Dev\suite.manifest.json` into `MotherSettings.TargetDrives` (`data\settings.json`).
  - Updated xUnit coverage in `Mother.Tests` (`35/35` passing under Release `/warnaserror`).

### Fixed
- **Clean-Root Staging & Release Packaging (`T4`, `package_installer.ps1`, `Mother.Installer`)**:
  - Fixed `Assets\` and `migrations\` nested directory copies in `Dev\deploy.ps1`, removed loose outer `README.md` from Clean Release Root, and staged `LICENSE`, `app\LICENSE`, and Notion-free `AGENTS.md`.
  - Fixed `Dev\package_installer.ps1` to preserve historical installers in `Installers\`, emit sanitized 6-file `Releases\v0.2.0\` + `v0.2.0.7z` + `Latest\` + `Latest.7z` bundles, and keep `SHA256SUMS.txt` strictly scoped to bundle files.
  - Decoupled HKCU uninstall/app registration and startup shortcut creation from Desktop/Start Menu shortcut toggles, and guarded `--uninstall --dry-run` against mutating installed files.
- **Public Clean-Room Scrub & PII Removal (`T6`)**:
  - Scrubbed personal host paths across documentation, created `Archive\Mother-v0.1.0-pre-filter-backup.7z`, and rewrote private Git history via `git-filter-repo` to drop tracked `Dev\lib\` binaries and PII literals.

## [0.1.0] - 2026-11-12
### Added
- **Full `Mother.Core`, `Mother.Server`, `Mother.App`, `Mother.Launcher`, `Mother.Installer` & `Mother.Tests` Implementation (`v0.1.0`)**:
  - Synced `Foundation v0.3.0` into `Dev\lib\` and `Dev\Mother.Server\wwwroot\vendor\foundation\` via `sync_foundation_sdk.py --sync-consumer`.
  - Implemented vertical-slice `Mother.Core` engine (`<= 400 LOC` per file, zero hardcoded host paths): dynamic `PathResolver`, `SettingsStore` (20-snapshot ring buffer), suite SQLite `mother.db` (`Dev\migrations\0001_initial_schema.sql`, `PRAGMA user_version = 1`), `SupervisorIpc`, `ManifestRegistryService` (`v1`/`v2` dual-schema loader), `ProcessSupervisor` (`3x/60s` circuit breaker, `Warm`/`Cold`/`Disabled` residency), `ColdWakeCoordinator`, `WatchdogSchedulerService`, `ResourceTrafficCopService` + `HkLockScanner`, `FleetCoexistenceArbitrator` + `SecretVaultInspector`, `FleetPowerCoordinator` + `PowerSleepInhibitor`, `NotificationDispatcher`, and `FleetDoctorService`.
  - Implemented `Mother.Server` Kestrel gateway (`:5100` prod / `:15100` `--test-mode`), Tailscale CGNAT (`100.64.0.0/10`) filter + SVG QR pairing, Cold-Wake reverse proxy, SSE `/api/v1/suite/events`, docked `Ctrl+\`` `foundation-terminal`, PWA (`/manifest.webmanifest`, `/sw.js`), and modular Titanium Sky (`#38bdf8`) UI (`Alt+1..7` outer tabs, `Ctrl+B` sidebar, mobile bottom nav `<= 820px`, sub-page pills, slide-over drawer, `Ctrl+S` settings, `Ctrl+K`/`?` `<dialog closedby="any">`, `Intl.Collator` + `moveBefore` sortable tables with CSV/JSON export).
  - Implemented `Mother.App` (`WinForms + WebView2` single-window shell, `TaskbarCreated` tray sentinel, `--test-mode --headless`, `--diagnose`), clean-root `Mother.Launcher` (`Mother.exe`), GZip-embedded 4-pillar `Mother.Installer`, `Dev\deploy.ps1`, `Dev\package_installer.ps1`, `Dev\scripts\{build,test,package_installer,run,start-daemon,stop-daemon,status-check,doctor-now,backup-now}.bat` + `interactive\*.bat`, and `28` xUnit tests (`28/28` passing under `/warnaserror`, covering all 14 DTO wire schemas in `contracts.js`).
- **6-Folder Root Topology, Root Documentation Quartet & 4-Doc Manual Shelf**:
  - Completed `Releases\` folder in the 6-folder layout (`Archive\`, `Builds\`, `Dev\`, `Installers\`, `Releases\`, `Sandbox\`) with outer-root `README.md`, `FEATURES.md`, `ROADMAP.md`, `CHANGELOG.md`, `AGENTS.md`, and `active.state`.
  - Synced the 4-Document Manual Shelf in `Dev\docs\`: [`ARCHITECTURE.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/ARCHITECTURE.md), [`USER_GUIDE.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/USER_GUIDE.md), [`OPERATIONS.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/OPERATIONS.md), and [`INTERFACES.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/INTERFACES.md).

### Changed
- **Full SemVer (`MAJOR.MINOR.PATCH`), Schema & `.gitignore` Synchronization**:
  - Updated `.gitignore` to permit `!Dev/**/*.csproj` and `!Dev/*.slnx`.
  - Adopted strict suffix-free 3-part SemVer (`v0.1.0`) and finalized implementation against [`Dev\docs\plans\v0.1.0.2026-11-12.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-12.md).

### Fixed
- **Sandbox & Clean-Root Remediation (`F-01`, `F-02a/b/c` in `T11`)**:
  - Fixed [`Dev\deploy.ps1`](file:///<WORKSPACE_ROOT>/Mother/Dev/deploy.ps1) to keep `version.txt`, `suite.manifest.json`, and `mother.manifest.json` strictly inside `app\` and emit clean runtime layout (`F-01`).
  - Updated [`Dev\Mother.Installer\Program.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Installer/Program.cs) to auto-forward `--data-dir <installDir>` when `--test-mode --install-dir` omits `--data-dir` (`F-02a`) and added `WaitForTargetUnlocked` pre-extraction file-unlock retry/backoff for back-to-back upgrades (`F-02b`).
  - Updated [`Dev\Mother.Core\Core\Diagnostics\MotherDiagnostics.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Core/Diagnostics/MotherDiagnostics.cs) (`StripLeadingHeartbeatTag`) to remove duplicate `[HEARTBEAT]` prefixes from `HeartbeatLogger` terminal/SSE lines (`F-02c`).

## [0.1.0-alpha.1] - 2026-11-02
### Added
- Initialized project structure under `<WORKSPACE_ROOT>/Mother\` and created initial version snapshot `Mother-v0.1.0-alpha.1-2026-11-02.7z`.
