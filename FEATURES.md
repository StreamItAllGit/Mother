# Mother — Living Feature Map & Fleet Coexistence Registry (`FEATURES.md`)

> **Active Version:** `v0.2.0-hf.1` (Full SemVer `MAJOR.MINOR.PATCH[-hf.N]` — Blueprint: [`v0.2.0.2026-11-15.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.2.0.2026-11-15.md)) | **License:** `MPL-2.0` | **Updated:** `2026-11-15`

This living dossier maps all live capabilities of **Mother (`:5100`)**, the unified desktop fleet supervisor, reverse-proxy gateway, autonomous Cold-Wake scheduler, schedule traffic cop, post-run power coordinator, and cross-app coexistence broker for the 6 sister applications (**Housekeeping `:5178`**, **ShadowLibrary `:5179`**, **Redroom `:5180`**, **Greenroom `:8420`**, **StreamEngine `:8430`**, and **legacy-sister-app `:8460`**).

---

## 1. At-a-Glance Feature Index

| Feature | Status | Target Version | Primary Code Location | 1-Line Summary |
| :--- | :--- | :--- | :--- | :--- |
| **Dual-Schema `suite.manifest.json` (`v2`) & `v1` Discovery** | **`Live (v0.2.0-hf.1)`** | `v0.1.0` | [`Dev/Mother.Core/Features/Discovery/ManifestRegistryService.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Features/Discovery/ManifestRegistryService.cs) | Discovers installed sister apps (`v2`/`v1`) across `StreamItAll\Mother\`, HKCU registry, configured sibling roots, and read-only legacy fallback. |
| **WorkerArgs, `commands.startDaemon` & Process Supervision** | **`Live (v0.2.0-hf.1)`** | `v0.2.0` | [`Dev/Mother.Core/Features/Supervision/WorkerLaunchCommandBuilder.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Features/Supervision/WorkerLaunchCommandBuilder.cs) | Launches supervised daemons via `commands.startDaemon`/`workerArgs`, resolved app `WorkingDirectory`, and `3x/60s` circuit breaker. |
| **Autonomous Cold-Wake `schedules[]` Loop & Idle Hibernation** | **`Live (v0.2.0-hf.1)`** | `v0.2.0` | [`Dev/Mother.Core/Features/Supervision/FleetScheduleEvaluator.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Features/Supervision/FleetScheduleEvaluator.cs) | Evaluates `schedules[]`, enforces coexistence deferrals + `runIfMissed` in `scheduler-state.json`, and hibernates idle `Cold` apps. |
| **Fleet Schedule Traffic Cop & `.hklock` Arbitrator** | **`Live (v0.2.0-hf.1)`** | `v0.2.0` | [`Dev/Mother.Core/Features/TrafficCop/ResourceTrafficCopService.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Features/TrafficCop/ResourceTrafficCopService.cs) | Scans `.hklock` sentinels (`PidSafeLockfile`), purges dead-PID stale locks, and serves `GET`/`POST /api/v1/scheduler/defer`. |
| **Fleet Post-Run Power Coordinator (`StayAwake/Sleep/Shutdown`)** | **`Live (v0.2.0-hf.1)`** | `v0.1.0` | [`Dev/Mother.Core/Features/Power/FleetPowerCoordinator.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Features/Power/FleetPowerCoordinator.cs) | Inhibits sleep on active locks (`SetThreadExecutionState`) and triggers 60s cancellable countdown when all apps hit `0` jobs. |
| **Fleet Coexistence Broker & DPAPI Vault Inspector** | **`Live (v0.2.0-hf.1)`** | `v0.1.0` | [`Dev/Mother.Core/Features/Coexistence/FleetCoexistenceArbitrator.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Features/Coexistence/FleetCoexistenceArbitrator.cs) | Evaluates cross-app conflicts & `.coexistence.json` parity with `Apply Safe-Stagger` and key-metadata-only DPAPI inspection. |
| **Single Gateway (`:5100`), `307` Subpath Proxy & Tailscale PWA** | **`Live (v0.2.0-hf.1)`** | `v0.2.0` | [`Dev/Mother.Server/Endpoints/ColdWakeProxyMiddleware.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Server/Endpoints/ColdWakeProxyMiddleware.cs) | Reverse-proxies `/api/v1/apps/<id>/*` & `/app/<id>/*` (`307` trailing-slash + `X-Forwarded-Prefix`), SSE, CGNAT filter, and QR SVG. |
| **Titanium Sky (`#38bdf8`) WebView2 UI & Expanded Terminal** | **`Live (v0.2.0-hf.1)`** | `v0.2.0` | [`Dev/Mother.Server/Endpoints/TerminalCommandDispatcher.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Server/Endpoints/TerminalCommandDispatcher.cs) | `Alt+1..7` tabs, `Ctrl+B` sidebar, mobile nav (`<= 820px`), `Ctrl+\`` terminal (`wake`, `stop`, `reload`, `power`, `traffic`, `logs`), `Ctrl+S` rollback. |
| **Clean-Root Launcher & `InstallerEngineCore` Wizard (`Foundation v0.4.1-hf.1`)** | **`Live (v0.2.0-hf.1)`** | `v0.2.0` | [`Dev/Mother.Installer/MotherInstallerRunner.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Installer/MotherInstallerRunner.cs) | Root `Mother.exe` shim + single-EXE signed dark wizard (`--profile`, `--suite-dir`, `--migrate-legacy`, `FindingCount: 0`). |

---

## 2. Suite Compatibility & Parity Coverage

- **Universal Compatibility — `Live (v0.2.0-hf.1)`:** Clean Tier A 5-folder workspace layout (`Archive\`, `Builds\`, `Dev\`, `Installers\`, `Releases\`), Core documentation quartet + `AGENTS.md` + `MPL-2.0` `LICENSE`, 4-Doc Manual Shelf, clean-root `Mother.exe` launcher, 4-pillar signed zero-PII installer (`Foundation.Installer v0.4.1-hf.1`), and 6-file `Releases\{v0.2.0-hf.1,Latest}\` distribution bundles.
- **Sister UI & Tray Controls — `Live (v0.2.0-hf.1)`:** Docked `Ctrl+\`` `foundation-terminal` with residency/power subcommands, `Ctrl+K` & `?` `<dialog closedby="any">` palettes, dedicated `⚙️ Settings` (`useGlobalFormatRules`, `targetDrives`, `Ctrl+S`, 20-snapshot rollback/JSON export), `Intl.Collator` + `moveBefore` sortable tables (`Bulk Select` + `CSV/JSON` export), and `TaskbarCreated` Win32 tray restoration sentinel.
- **Schema, Contract & Exit Codes — `Live (v0.2.0-hf.1)`:** Strict `mother.db` SQLite schema (`PRAGMA user_version = 1`), 14-DTO wire-key lockstep (`contracts.js` verified by `Mother.Tests`), and deterministic exit codes (`0`, `1`, `2`, `3`, `130`).

---

## 3. Cross-App Operability, Shared Seams & Conflict Matrix (`v0.2.0-hf.1`)

Mapped against [`v0.2.0.2026-11-15.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.2.0.2026-11-15.md):

| Supervised Sister App | Canonical Port | Shared Discovery & Coexistence Seams | Conflict / Contention Vector | Mother Arbitration & Coexistence Resolution |
| :--- | :---: | :--- | :--- | :--- |
| **`Housekeeping`** | `:5178` | `suite.manifest.json` / `mother.manifest.json`, `/api/v1/coexistence/status`, `/api/v1/scheduler/defer`, `.housekeeping_<pid>.hklock` | Competing NAS/ROM vault jobs with ShadowLibrary, Redroom, legacy-sister-app, or StreamEngine. | Staggers overlapping drive schedules via Traffic Cop and surfaces coexistence conflicts in `Traffic Cop` (`Apply Safe-Stagger`). |
| **`ShadowLibrary`** | `:5179` | `suite.manifest.json` / `mother.manifest.json`, `/api/v1/coexistence/status`, `/api/v1/scheduler/defer`, `.shadowlibrary_<pid>.hklock` | Concurrent bit-rot scrub or DAT audit on `E:` / `H:` while Housekeeping or Greenroom scans the same drive. | Detects `.shadowlibrary_<pid>.hklock` and defers competing scheduled tasks on shared drives; brokers `suite-safe-stagger`. |
| **`Redroom`** | `:5180` | `suite.manifest.json` / `mother.manifest.json`, `/api/v1/coexistence/status`, `/api/v1/scheduler/defer`, `.redroom_<pid>.hklock` | Concurrent media promotion to `V:..Z:` during Housekeeping Media Transcoder or Cloud Sync runs. | Honors `.redroom_<pid>.hklock` in `HkLockScanner` and arbitrates shared media root presets. |
| **`Greenroom`** | `:8420` | `suite.manifest.json`, `/app/greenroom/`, `/api/v1/scheduler/defer`, `/api/v1/coexistence/status` | Redundant disk DAT hashing or concurrent schedule runs while Peer I/O locks are active. | Supervises `commands.startDaemon`/`workerArgs`, proxies `/app/greenroom/` (`X-Forwarded-Prefix`), evaluates `schedules[]`, and records `/api/v1/scheduler/defer` handshakes. |
| **`StreamEngine`** | `:8430` (`:18766`) | `suite.manifest.json` / `mother.manifest.json`, `/api/v1/coexistence/status`, `.streamengine_<pid>.hklock` | Active broadcast VOD recording/remux colliding with Housekeeping Proxy/Archiver or OS Sleep/Shutdown. | Blocks Fleet Post-Run Sleep/Shutdown while streaming/recording and defers competing jobs on `.streamengine_<pid>.hklock`. |
| **`legacy-sister-app`** | `:8460` | `suite.manifest.json` / `mother.manifest.json`, `/api/v1/coexistence/status`, `.legacy-sister-app_<pid>.hklock` | Overlapping emulator save sync (`.uss` vs. `rclone`) or `.lnk` launcher directories with Housekeeping. | Surfaces save/launcher overlap conflicts in `Traffic Cop` and coordinates shared root presets. |
