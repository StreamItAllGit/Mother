# Features — Mother

Overview of what `Mother` (`:5100`) supports today (`v0.3.0`) and what is planned across upcoming milestones toward `v1.0.0`. For release sequencing and verification criteria, see [ROADMAP.md](ROADMAP.md).

---

## Feature Summary

| Feature | Status | Target | Primary Code | What It Does |
| :--- | :--- | :--- | :--- | :--- |
| **Dual-Schema Manifest Discovery** | `Live` | `v0.1.0` | [ManifestRegistryService.cs](Dev/Mother.Core/Features/Discovery/ManifestRegistryService.cs) | Discovers installed app manifests (`v2` camelCase and `v1` snake_case) by strict precedence across `data/manifests/`, umbrella folders, registry metadata, configured roots, and host-gated legacy roots. |
| **Process Supervision & Command Builder** | `Live` | `v0.2.0` | [WorkerLaunchCommandBuilder.cs](Dev/Mother.Core/Features/Supervision/WorkerLaunchCommandBuilder.cs) | Launches supervised loopback daemons with `commands.startDaemon`/`workerArgs`, contained working directories, and a `3x/60s` circuit breaker. |
| **Cold-Wake Scheduler & Idle Hibernation** | `Live` | `v0.2.0` | [FleetScheduleEvaluator.cs](Dev/Mother.Core/Features/Supervision/FleetScheduleEvaluator.cs) | Evaluates `schedules[]`, checks coexistence locks, tracks `runIfMissed` in `scheduler-state.json`, and hibernates idle `Cold` workers. |
| **Disk Traffic Cop & `.hklock` Arbitrator** | `Live` | `v0.2.0` | [ResourceTrafficCopService.cs](Dev/Mother.Core/Features/TrafficCop/ResourceTrafficCopService.cs) | Inspects PID-safe `.hklock` sentinels, purges dead-PID stale locks, and serves `GET`/`POST /api/v1/scheduler/defer`. |
| **Post-Run Power Coordinator** | `Live` | `v0.1.0` | [FleetPowerCoordinator.cs](Dev/Mother.Core/Features/Power/FleetPowerCoordinator.cs) | Inhibits OS sleep while locks or jobs are active, then runs a 60-second cancellable countdown before `Sleep` or `Shutdown`. |
| **Coexistence Broker & Vault Inspector** | `Live` | `v0.1.0` | [FleetCoexistenceArbitrator.cs](Dev/Mother.Core/Features/Coexistence/FleetCoexistenceArbitrator.cs) | Evaluates cross-app conflicts and `.coexistence.json` (`schemaVersion: 2`) parity (`Apply Safe-Stagger`), plus key-metadata-only DPAPI inspection. |
| **Single Gateway, `307` Proxy & Mobile PWA** | `Live` | `v0.2.0` | [ColdWakeProxyMiddleware.cs](Dev/Mother.Server/Endpoints/ColdWakeProxyMiddleware.cs) | Proxies `/api/v1/apps/<id>/*` and `/app/<id>/*` (`307` slash redirect + `X-Forwarded-Prefix`), unbuffered SSE, CGNAT filter, and QR pairing. |
| **Titanium Sky Shell & Docked Terminal** | `Live` | `v0.2.0` | [TerminalCommandDispatcher.cs](Dev/Mother.Server/Endpoints/TerminalCommandDispatcher.cs) | Provides dynamic `Alt+1..9` outer tabs, `Ctrl+B` sidebar, mobile bottom nav, `Ctrl+\`` terminal commands, `Ctrl+K`/`?` modals, and 20-snapshot `Ctrl+S` rollback. |
| **Clean-Root Launcher & 4-Pillar Setup** | `Live` | `v0.2.0` | [MotherInstallerRunner.cs](Dev/Mother.Installer/MotherInstallerRunner.cs) | Ships NativeAOT/trimmed `Mother.exe` launcher shim plus a single-EXE `StandardInstallerHost` dark installer (`installer.manifest.json` v2) supporting `--profile`, `--suite-dir`, and `--migrate-legacy`. |
| **Universal Pluggable Core & Sandbox Isolation** | `Live` | `v0.3.0` | [ManifestModels.cs](Dev/Mother.Core/Features/Discovery/ManifestModels.cs) | Removes all hardcoded sister-app defaults for a 0-app empty state and Doctor `"pass"`, guards `port <= 0` (`503 NO_HTTP_ENDPOINT`), adds ISO/IEC 18004 QR SVG (`Foundation v0.5.1`), non-destructive `data/config/settings.json` v2 migration, 3-mode sandbox containment (`ExitCode` `2`/`3`), self-contained `win-x64` runtime, WebView2 fallback, and 7-file `Releases/` + `Portable.zip`. |
| **Zero-Update App Onboarding (`+ Register App`)** | `Planned` | `v0.4.0` | `Dev/Mother.Core/Features/Discovery/` | Adds UI/CLI/REST dynamic app registration and validation, CSRF protection, shell/path security guards, and custom icon/accent onboarding docs. |
| **Multi-App Burn-In & Update Radar** | `Planned` | `v0.5.0` | `Dev/Mother.Core/Features/Updates/` | Hardens drive-root lock scans, single-flight Cold-Wake, and parallel shutdown alongside read-only ETag-cached GitHub Release update checks. |
| **Safe Staged Updater & Auto-Rollback** | `Planned` | `v0.6.0` | `Dev/Mother.Core/Features/Updates/` | Adds opt-in TLS 1.2+/SSRF-guarded `.partial` downloads, SHA-256 + Authenticode checks, `VACUUM INTO` backups, detached self-update handoff, and automatic rollback. |

---

## Capability Details

### Core Host, Configuration & Persistence
- **What it does:** Resolves runtime data, config, log, and port overrides (`--data-dir`, `--config`, `--log-dir`, `--port`), enforces 3-mode isolation and 4-drive sandbox containment (`ExitCode = 3`), migrates `data/settings.json` non-destructively to `data/config/settings.json` (`schemaVersion: 2`), maintains a 20-snapshot config rollback ring buffer, and logs telemetry into SQLite WAL (`mother.db`).
- **Entry Points:** [PathResolver.cs](Dev/Mother.Core/Core/Configuration/PathResolver.cs), [SettingsMigrationService.cs](Dev/Mother.Core/Core/Configuration/SettingsMigrationService.cs), [SettingsStore.cs](Dev/Mother.Core/Core/Configuration/SettingsStore.cs), [MotherDb.cs](Dev/Mother.Core/Core/Persistence/MotherDb.cs)
- **Tests:** [PathResolverAndSettingsTests.cs](Dev/Mother.Tests/PathResolverAndSettingsTests.cs), [MotherDbAndWireLockstepTests.cs](Dev/Mother.Tests/MotherDbAndWireLockstepTests.cs), [V030PublicBetaTests.cs](Dev/Mother.Tests/V030PublicBetaTests.cs)
- **Extension Point / What to Touch:** Add new settings fields to `MotherSettings`, `SettingsMigrationService`, and `Dev/config/settings.example.json`, and append SQLite tables via numbered `Dev/migrations/000N_*.sql` files.

### Discovery, Process Supervision & Cold-Wake Scheduler
- **What it does:** Loads `suite.manifest.json` (`v2`) or legacy `mother.manifest.json` (`v1`) manifests by strict first-wins precedence, launches loopback workers (`Warm` always-on, `Cold` on-demand, or `Disabled`), evaluates `schedules[]` with coexistence checks, and hibernates idle `Cold` processes.
- **Entry Points:** [ManifestRegistryService.cs](Dev/Mother.Core/Features/Discovery/ManifestRegistryService.cs), [WorkerLaunchCommandBuilder.cs](Dev/Mother.Core/Features/Supervision/WorkerLaunchCommandBuilder.cs), [FleetScheduleEvaluator.cs](Dev/Mother.Core/Features/Supervision/FleetScheduleEvaluator.cs), [ProcessSupervisor.cs](Dev/Mother.Core/Features/Supervision/ProcessSupervisor.cs)
- **Tests:** [ManifestAndTrafficCopTests.cs](Dev/Mother.Tests/ManifestAndTrafficCopTests.cs), [V020FeaturesTests.cs](Dev/Mother.Tests/V020FeaturesTests.cs), [V030PublicBetaTests.cs](Dev/Mother.Tests/V030PublicBetaTests.cs)
- **Extension Point / What to Touch:** Extend `ManifestParser` / `SisterAppEntry` in `Features/Discovery/` for new manifest properties, and wire `v0.4.0` validation/registration slices cleanly beside `ManifestRegistryService`.

### Traffic Cop, Coexistence Broker & Power Coordinator
- **What it does:** Detects live vs. dead-PID `.hklock` sentinels (`PidSafeLockfile`), coordinates schedule deferrals across shared roots via `.coexistence.json` (`schemaVersion: 2`), and inhibits OS sleep until all supervised workers reach zero active jobs and locks.
- **Entry Points:** [HkLockScanner.cs](Dev/Mother.Core/Features/TrafficCop/HkLockScanner.cs), [ResourceTrafficCopService.cs](Dev/Mother.Core/Features/TrafficCop/ResourceTrafficCopService.cs), [FleetCoexistenceArbitrator.cs](Dev/Mother.Core/Features/Coexistence/FleetCoexistenceArbitrator.cs), [FleetPowerCoordinator.cs](Dev/Mother.Core/Features/Power/FleetPowerCoordinator.cs)
- **Tests:** [PowerWatchdogAndDoctorTests.cs](Dev/Mother.Tests/PowerWatchdogAndDoctorTests.cs)
- **Extension Point / What to Touch:** Add custom conflict rules or shared-root presets inside `Features/Coexistence/FleetCoexistenceArbitrator.cs` without modifying the HTTP layer.

### Gateway API, Subpath Reverse Proxy & Web Shell
- **What it does:** Exposes `/api/v1/suite/*`, `/api/v1/scheduler/defer`, `/api/v1/apps/<appId>/*`, `/app/<appId>/` (with `307` trailing-slash canonicalization, `X-Forwarded-Prefix`, and `503 NO_HTTP_ENDPOINT` for `port <= 0`), offline ISO/IEC 18004 QR SVG (`/api/v1/suite/qr.svg`), SSE logs/events, WebView2 runtime fallback, and the modular Titanium Sky UI (`Alt+1..9` dynamic tabs).
- **Entry Points:** [MotherWebHost.cs](Dev/Mother.Server/MotherWebHost.cs), [ColdWakeProxyMiddleware.cs](Dev/Mother.Server/Endpoints/ColdWakeProxyMiddleware.cs), [TailscaleAndQrEndpoints.cs](Dev/Mother.Server/Endpoints/TailscaleAndQrEndpoints.cs), [TerminalCommandDispatcher.cs](Dev/Mother.Server/Endpoints/TerminalCommandDispatcher.cs), [app.js](Dev/Mother.Server/wwwroot/js/app.js), [contracts.js](Dev/Mother.Server/wwwroot/js/api/contracts.js)
- **Tests:** [ServerAndInstallerTests.cs](Dev/Mother.Tests/ServerAndInstallerTests.cs), [V030PublicBetaTests.cs](Dev/Mother.Tests/V030PublicBetaTests.cs)
- **Extension Point / What to Touch:** Add Minimal API endpoint groups in `Dev/Mother.Server/Endpoints/` and corresponding frontend views under `Dev/Mother.Server/wwwroot/js/views/`.

---

## Suite Coexistence Architecture

`Mother` treats every supervised application symmetrically via manifest declarations rather than hardcoded product rules. Any manifest-compliant app declares its loopback port (`<= 0` for non-HTTP CLI apps), `residency` defaults, `sharedMounts`, `mutuallyExclusiveWith`, and `schedules[]` inside `suite.manifest.json`:

| Example App Pattern | Discovery & Arbitration Seams | Contention Scenario | Mother Resolution |
| :--- | :--- | :--- | :--- |
| **`demo-worker`** *(Cold Background Worker)* | `suite.manifest.json`, `/api/v1/scheduler/defer`, `.demo-worker_<pid>.hklock` | Scheduled batch job overlaps an active scan on a shared root mount. | Holds scheduled Cold-Wake, logs deferral in `scheduler-state.json` and `schedule_deferrals`, and retries once clear. |
| **`media-tool`** *(Warm Loopback Web UI)* | `suite.manifest.json`, `/app/media-tool/`, `/api/v1/coexistence/status`, `.coexistence.json` | Multiple tools index the same shared folder concurrently or risk mid-run OS sleep. | Proxies `/app/media-tool/` (`X-Forwarded-Prefix`), coordinates `Apply Safe-Stagger` (`v2`) presets, and blocks OS sleep until active jobs hit `0`. |

Optional StreamItAll suite applications (`Housekeeping`, `ShadowLibrary`, `Redroom`, `Greenroom`, and `StreamEngine`) integrate with `Mother` using this exact same public manifest, `.hklock`, and `.coexistence.json` contract whenever installed on the same machine.
