# Changelog

All notable changes to `Mother` will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning (`MAJOR.MINOR.PATCH`)](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.0] - 2026-11-19
### Added
- **Universal 0-App Empty State & Non-HTTP Proxy Guard**:
  - Added an actionable 0-app onboarding card (`#fleet-empty-state`) in [fleet-view.js](Dev/Mother.Server/wwwroot/js/views/fleet-view.js), dynamic outer tab shortcuts (`Alt+1..9`) and `?tab=` routing in [app.js](Dev/Mother.Server/wwwroot/js/app.js), and `"pass"` Doctor verification at 0 registered apps in [FleetDoctorService.cs](Dev/Mother.Core/Features/Doctor/FleetDoctorService.cs).
  - Added HTTP `503 NO_HTTP_ENDPOINT` proxy protection in [ColdWakeProxyMiddleware.cs](Dev/Mother.Server/Endpoints/ColdWakeProxyMiddleware.cs) for headless/CLI apps (`internalPort <= 0`).
- **Non-Destructive Settings v2 Migration & Centralized Build Constants**:
  - Added [SettingsMigrationService.cs](Dev/Mother.Core/Core/Configuration/SettingsMigrationService.cs) to migrate `data\settings.json` atomically to `data\config\settings.json` (`schemaVersion: 2`, preserving `data\settings.json.migrated.bak`), upgrade `--config` / `Rollback()` / `PUT /api/v1/suite/settings` payloads, and preserve unknown JSON extension keys.
  - Added [MotherBuildConstants.cs](Dev/Mother.Core/Core/Configuration/MotherBuildConstants.cs) (`0.3.0` app fallback / `0.5.1` SDK pin), [MotherLauncherArgsHelper.cs](Dev/Mother.Launcher/MotherLauncherArgsHelper.cs) for launcher path rebasing and sync-wait flags, and an interactive WebView2 runtime-missing fallback panel + one-shot `SetLoaderDllFolderPath` setup in [MainForm.cs](Dev/Mother.App/MainForm.cs).
- **Declarative Installer, Portable ZIP, Clean-Room Root Docs & Verification**:
  - Added embedded [installer.manifest.json](Dev/Mother.Installer/installer.manifest.json) (`schemaVersion: 2`), [SECURITY.md](SECURITY.md), [CONTRIBUTING.md](CONTRIBUTING.md), [Mother.code-workspace](Dev/Mother.code-workspace), [stage2_mother_e2e.py](Dev/tools/stage2_mother_e2e.py), and [V030PublicBetaTests.cs](Dev/Mother.Tests/V030PublicBetaTests.cs) (`40/40` passing xUnit tests with zero warnings).
  - Added self-contained `Mother-v0.3.0-win-x64-Portable.zip` (zero installer binaries inside) and NTFS-hardlinked 7-file `Releases\v0.3.0\` / `Releases\Latest\` packaging in [package_installer.ps1](Dev/package_installer.ps1).

### Changed
- **Universal Pluggable Roadmap & Public Beta Readiness**:
  - Aligned the roadmap through `v1.0.0` (`v0.3.0` Public Beta -> `v0.4.0` Zero-Update App Onboarding -> `v0.5.0` Live Fleet Burn-In & Update Radar -> `v0.6.0` Safe Staged Updater -> `v1.0.0` Going Gold), retired standalone non-suite utilities from suite scope, enabled permanent human push/release review gating (`requireHumanReviewBeforePush: true`), and configured Discord devlog and Google Drive mirror release targets.
- **`Foundation v0.5.1` SDK, Self-Contained Packaging & Launcher**:
  - Upgraded to `Foundation v0.5.1` across [Mother.App](Dev/Mother.App/Mother.App.csproj), [Mother.Installer](Dev/Mother.Installer/Mother.Installer.csproj), and [Mother.Launcher](Dev/Mother.Launcher/Mother.Launcher.csproj), replacing custom QR SVG code in [TailscaleAndQrEndpoints.cs](Dev/Mother.Server/Endpoints/TailscaleAndQrEndpoints.cs) with `Foundation.Core.QrCodeEncoder.RenderSvg` (ISO/IEC 18004 Versions 1–10 ECC-M, 4-module quiet zone).
  - Switched `Mother.App` to self-contained `win-x64` publishing without IL trimming, updated `Mother.Launcher` (`Mother.exe`) to compile directly against `Foundation.Launcher` with MSVC-autodetected NativeAOT (or automatic single-file partial-trim fallback), and omitted developer `AGENTS.md` and loose outer `LICENSE` from staged `Builds\` (keeping `app\LICENSE` only).
- **Discovery Precedence, Coexistence v2 & Canonical Settings Keys**:
  - Inverted [ManifestRegistryService.cs](Dev/Mother.Core/Features/Discovery/ManifestRegistryService.cs) to strict `TryAdd` highest-to-lowest precedence (`data\manifests\` > `ProgramsRoot` > `IPlatformRegistry` `Apps` > `customManifestRoots` > host-gated legacy `<Drive>:\Programs\`).
  - Upgraded `.coexistence.json` output in [FleetCoexistenceArbitrator.cs](Dev/Mother.Core/Features/Coexistence/FleetCoexistenceArbitrator.cs) to `schemaVersion: 2` (`targetAppIds`), and normalized all 6 canonical settings keys (`shouldAllowTailscaleBind`, `idleTimeoutMinutes`, `shouldInhibitSleepDuringActiveLock`, `shouldAutoAlignSisterApps`, `shouldShowDesktopToasts`, `shouldSendDiscordWebhooks`) across [MotherSettings.cs](Dev/Mother.Core/Core/Configuration/MotherSettings.cs), [FleetContracts.cs](Dev/Mother.Core/Core/Contracts/FleetContracts.cs), [contracts.js](Dev/Mother.Server/wwwroot/js/api/contracts.js), [settings-view.js](Dev/Mother.Server/wwwroot/js/views/settings-view.js), and [settings.example.json](Dev/config/settings.example.json).
  - Updated [MotherInstallerRunner.cs](Dev/Mother.Installer/MotherInstallerRunner.cs) and [MotherInstallerIntegration.cs](Dev/Mother.Installer/MotherInstallerIntegration.cs) onto `StandardInstallerHost`, `InstallerEngine`, `IPlatformRegistry`, and `KnownFolderPaths`, and synced Root Quartet, `AGENTS.md`, `active.state`, and `Dev\docs\*.md`.

### Removed
- **Hardcoded Sister Lists & Stale Tracking**:
  - Removed `CanonicalSisterDefaults`, `DEFAULT_SISTER_TABS`, hardcoded sister fallback arrays, unused tracked assets in `Dev\lib\` and `wwwroot\vendor\foundation\` (keeping strictly the 3 vanilla runtime CSS/JS assets), and the retired standalone installer staging directory.

### Fixed
- **Packaging, Diagnostics & Log Tailer**:
  - Updated [package_installer.ps1](Dev/package_installer.ps1) Authenticode lookup to target `CN=StreamItAll Code Signing`, replaced unsafe `-Include` filtering with `Where-Object`, updated [tail-logs.bat](Dev/scripts/interactive/tail-logs.bat) to tail `*.jsonl` logs, removed bare empty `catch` blocks, and bumped the PWA cache key in [sw.js](Dev/Mother.Server/wwwroot/sw.js) to `mother-shell-v0.3.0`.

### Security
- **Release Fail-Closed Automation Exclusion & 3-Mode Sandbox Containment**:
  - Scoped `Foundation.Automation` (`MotherAutomationHost.cs`, `InstallerWizardForm` automation attach) strictly to `Debug` and `-p:MotherTestAutomation=true`; plain Release builds strip automation assemblies and return **`ExitCode = 2`** if `--automation-port` or `--automation-token` is passed.
  - Enforced 3-mode sandbox and path containment (`ExitCode = 3`) across `PathResolver`, `ManifestRegistryService`, `SupervisorIpc`, `WatchdogSchedulerService`, `FleetPowerCoordinator`, `PowerSleepInhibitor`, `NotificationDispatcher`, `WorkerLaunchCommandBuilder`, and `Mother.Installer`.

## [0.2.0-hf.1] - 2026-11-15
### Changed
- **SDK `Foundation v0.4.1-hf.1` & Isolation Refactor**:
  - Synced `Foundation v0.4.1-hf.1` into `Dev\lib\` and `Dev\Mother.Server\wwwroot\vendor\foundation\`.
  - Migrated `PathResolver`, `MotherRuntime`, `Mother.App`, `Mother.Server`, and `Mother.Installer` to `AppEnvironment`, `UiPresentationMode.TrayOrBackground`, and `InstallerRuntimeContext` (`APP_ISOLATED_TEST=1`).
  - Standardized the clean 4-folder workspace layout across `active.state`, `.gitignore`, `AGENTS.md`, root documentation, and technical manuals.

### Removed
- **Legacy Harness Scaffolding**:
  - Removed legacy isolated-harness directories, retired CLI test-runner, viewport-probe, and registry-mock scaffolding, and alternate port bifurcation while preserving standard runtime overrides (`--data-dir`, `--config`, `--log-dir`, `--port`, `--tray-only`) and in-process Release xUnit tests (`35/35` passing under `/warnaserror`).

### Fixed
- **PowerShell 5.1 Pre-Package Scrubbing**:
  - Updated [deploy.ps1](Dev/deploy.ps1) desktop junk scrubbing to use `Where-Object` name filtering instead of unsafe `-Include` recursion.

## [0.2.0] - 2026-11-15
### Added
- **Cold-Wake Scheduler Execution Loop & Idle Hibernation**:
  - Added [FleetScheduleEvaluator.cs](Dev/Mother.Core/Features/Supervision/FleetScheduleEvaluator.cs) to evaluate manifest `schedules[]`, enforce `FleetCoexistenceArbitrator` + `.hklock` checks before scheduled Cold-Wake runs, support `runIfMissed`, persist atomic `data\scheduler-state.json`, and expose `GET`/`POST /api/v1/scheduler/defer`.
  - Added `ColdWakeCoordinator.EvaluateIdleHibernationAsync` to hibernate idle `Cold` workers automatically after `idleTimeoutMinutes` (`15m` default) once `activeJobs + queuedJobs == 0`.
- **Command-Aware Worker Supervision (`WorkerLaunchCommandBuilder.cs`)**:
  - Extracted [WorkerLaunchCommandBuilder.cs](Dev/Mother.Core/Features/Supervision/WorkerLaunchCommandBuilder.cs) supporting manifest `workerArgs`, `commands.startDaemon` primary and `commands.start` fallback resolution, resolved `WorkingDirectory`, and `MANAGED_BY_SUITE=1` + `MOTHER_GATEWAY_URL` injection.
- **Subpath Proxy Forwarding & Expanded Terminal**:
  - Updated [ColdWakeProxyMiddleware.cs](Dev/Mother.Server/Endpoints/ColdWakeProxyMiddleware.cs) to emit `307 Temporary Redirect` from `/app/{appId}` (no trailing slash) to `/app/{appId}/` preserving query strings, forward trailing-slash `/app/{appId}/` directly to upstream `/`, proxy `/api/v1/apps/{appId}` root requests, disable HTTP response buffering on `text/event-stream` SSE routes, return early when aborted or started SSE clients disconnect post-headers, and forward `X-Forwarded-Prefix: /app/{appId}`.
  - Added [TerminalCommandDispatcher.cs](Dev/Mother.Server/Endpoints/TerminalCommandDispatcher.cs) supporting `help`, `status`, `health`, `apps`, `traffic`, `locks`, `coexistence`, `doctor`, `logs [count]`, `purge-stale`, `reload`, `wake <appId>`, `stop <appId>`, and validated `power [StayAwake|Sleep|Shutdown]`.
- **`Foundation v0.4.0` Umbrella Wizard & Config Overlay**:
  - Replaced legacy setup host in [MotherInstallerRunner.cs](Dev/Mother.Installer/MotherInstallerRunner.cs) + [MotherInstallerIntegration.cs](Dev/Mother.Installer/MotherInstallerIntegration.cs) with `Foundation.Installer.InstallerEngineCore` + `InstallerWizardForm`, Studio/Suite defaults (`%LOCALAPPDATA%\Programs\StreamItAll\Mother\Mother`), `--profile`, `--suite-dir`, `--migrate-legacy`, and `--autostart`.
  - Added neutral [settings.example.json](Dev/config/settings.example.json) staged to `app\config\settings.example.json`.

### Changed
- **SDK Pin, Sibling Containment & `MPL-2.0` License Alignment**:
  - Synced `Foundation v0.4.0` into `Dev\lib\` and `wwwroot\vendor\foundation\`; untracked `Dev/lib/*.{dll,pdb,xml,deps.json}` and updated `Dev\deploy.ps1` to auto-run `sync_foundation_sdk.py --sync-consumer` when SDK DLLs are absent.
  - Aligned `Dev\suite.manifest.json`, `Dev\deploy.ps1`, and runtime metadata to `MPL-2.0` and `sdkVersion: "0.4.0"`.
  - Updated [PathResolver.cs](Dev/Mother.Core/Core/Configuration/PathResolver.cs) and [ManifestRegistryService.cs](Dev/Mother.Core/Features/Discovery/ManifestRegistryService.cs) for `StreamItAll\Mother\` peer discovery, HKCU + `MOTHER_HOME` lookup, read-only legacy root fallback, sibling directory containment, and residency aliases (`on-demand` -> `cold`, `always-on` -> `warm`).
  - Relocated `targetDrives` out of `Dev\suite.manifest.json` into `MotherSettings.TargetDrives` (`data\settings.json`).
  - Updated xUnit coverage in `Mother.Tests` (`35/35` passing under Release `/warnaserror`).

### Fixed
- **Clean-Root Staging & Release Packaging**:
  - Fixed `Assets\` and `migrations\` nested directory copies in `Dev\deploy.ps1`, removed loose outer `README.md` from the staged runtime root, and staged `LICENSE`, `app\LICENSE`, and `AGENTS.md`.
  - Updated `Dev\package_installer.ps1` to emit sanitized `Releases\v0.2.0\` + `v0.2.0.7z` + `Latest\` + `Latest.7z` bundles and keep `SHA256SUMS.txt` strictly scoped to bundle files.
  - Decoupled HKCU uninstall/app registration and startup shortcut creation from Desktop/Start Menu shortcut toggles, and guarded `--uninstall --dry-run` against mutating installed files.
- **Public Clean-Room Scrub & PII Removal**:
  - Scrubbed personal host paths across documentation, captured a pre-filter `.7z` backup, and rewrote private Git history via `git-filter-repo` to drop tracked `Dev\lib\` binaries and PII literals.

## [0.1.0] - 2026-11-12
### Added
- **Initial `Mother.Core`, `Mother.Server`, `Mother.App`, `Mother.Launcher`, `Mother.Installer` & `Mother.Tests` Implementation**:
  - Synced `Foundation v0.3.0` into `Dev\lib\` and `Dev\Mother.Server\wwwroot\vendor\foundation\`.
  - Implemented vertical-slice `Mother.Core` engine (`<= 400` lines per file, zero hardcoded host paths): dynamic `PathResolver`, `SettingsStore` (20-snapshot ring buffer), suite SQLite `mother.db` (`Dev\migrations\0001_initial_schema.sql`, `PRAGMA user_version = 1`), `SupervisorIpc`, `ManifestRegistryService` (`v1`/`v2` dual-schema loader), `ProcessSupervisor` (`3x/60s` circuit breaker, `Warm`/`Cold`/`Disabled` residency), `ColdWakeCoordinator`, `WatchdogSchedulerService`, `ResourceTrafficCopService` + `HkLockScanner`, `FleetCoexistenceArbitrator` + `SecretVaultInspector`, `FleetPowerCoordinator` + `PowerSleepInhibitor`, `NotificationDispatcher`, and `FleetDoctorService`.
  - Implemented `Mother.Server` Kestrel gateway (`:5100`), Tailscale CGNAT (`100.64.0.0/10`) filter + SVG QR pairing, Cold-Wake reverse proxy, SSE `/api/v1/suite/events`, docked `Ctrl+\`` `foundation-terminal`, PWA (`/manifest.webmanifest`, `/sw.js`), and modular Titanium Sky (`#38bdf8`) UI (`Alt+1..7` outer tabs, `Ctrl+B` sidebar, mobile bottom nav `<= 820px`, sub-page pills, slide-over drawer, `Ctrl+S` settings, `Ctrl+K`/`?` modals, and sortable tables with CSV/JSON export).
  - Implemented `Mother.App` (`WinForms + WebView2` single-window shell, `TaskbarCreated` tray sentinel, `--tray-only`, `--diagnose`), clean-root `Mother.Launcher` (`Mother.exe`), GZip-embedded 4-pillar `Mother.Installer`, `Dev\deploy.ps1`, `Dev\package_installer.ps1`, `Dev\scripts\`, and `28` xUnit tests (`28/28` passing under `/warnaserror`).
- **Workspace Topology, Root Documentation Quartet & Manual Shelf**:
  - Established outer-root `README.md`, `FEATURES.md`, `ROADMAP.md`, `CHANGELOG.md`, `AGENTS.md`, and `active.state` alongside technical manuals in `Dev\docs\` (`ARCHITECTURE.md`, `USER_GUIDE.md`, `OPERATIONS.md`, `INTERFACES.md`).

### Changed
- **Full SemVer (`MAJOR.MINOR.PATCH`), Schema & `.gitignore` Synchronization**:
  - Updated `.gitignore` to permit `!Dev/**/*.csproj` and `!Dev/*.slnx`, and adopted strict suffix-free 3-part SemVer (`v0.1.0`).

### Fixed
- **Contained Install & Clean-Root Remediation**:
  - Fixed [deploy.ps1](Dev/deploy.ps1) to keep `version.txt`, `suite.manifest.json`, and `mother.manifest.json` strictly inside `app\` and emit a clean runtime layout.
  - Updated [Program.cs](Dev/Mother.Installer/Program.cs) to auto-forward `--data-dir <installDir>` when custom `--install-dir` flows isolate app data, and added `WaitForTargetUnlocked` pre-extraction file-unlock retry/backoff for back-to-back upgrades.
  - Updated [MotherDiagnostics.cs](Dev/Mother.Core/Core/Diagnostics/MotherDiagnostics.cs) (`StripLeadingHeartbeatTag`) to remove duplicate `[HEARTBEAT]` prefixes from `HeartbeatLogger` terminal/SSE lines.

## [0.1.0-alpha.1] - 2026-11-02
### Added
- Initialized the `Mother` workspace layout and created initial version snapshot `Mother-v0.1.0-alpha.1-2026-11-02.7z`.
