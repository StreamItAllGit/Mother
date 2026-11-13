# Mother — Living Feature Map & Fleet Coexistence Registry (`FEATURES.md`)

> **Active Version:** `v0.1.0` (`Live` — Full SemVer `MAJOR.MINOR.PATCH` — Blueprint: [`v0.1.0.2026-11-12.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-12.md)) | **Maintained By:** `@tessa` (Documentation Master & Strategic Planner) | **Updated:** `2026-11-12`

This living dossier maps all live and planned capabilities of **Mother (`:5100`)**, the unified desktop fleet supervisor, reverse-proxy gateway, schedule traffic cop, post-run power coordinator, and cross-app coexistence broker for the 6 sister applications (**Housekeeping `:5178`**, **ShadowLibrary `:5179`**, **Redroom `:5180`**, **Greenroom `:8420`**, **StreamEngine `:8430`**, and **legacy-sister-app `:8460`**).

---

## 1. At-a-Glance Feature Index

| Feature | Status | Target Version | Primary Code Location | 1-Line Summary |
| :--- | :--- | :--- | :--- | :--- |
| **Dual-Schema `suite.manifest.json` (`v2`) & `v1` Discovery** | **`Live (v0.1.0)`** | `v0.1.0` | [`Dev/Mother.Core/Features/Discovery/ManifestRegistryService.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Features/Discovery/ManifestRegistryService.cs) | Discovers installed sister apps (`v2` `camelCase` & `v1` `snake_case`) with `Warm`/`Cold`/`Disabled` residency. |
| **Master Watchdog (`\Mother_Watchdog`) & Process Supervision** | **`Live (v0.1.0)`** | `v0.1.0` | [`Dev/Mother.Core/Features/Supervision/ProcessSupervisor.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Features/Supervision/ProcessSupervisor.cs) | Manages `JobObjectGuard` child processes, `250ms`/`5000ms` health polling, `\Mother_Watchdog`, and `3x/60s` circuit breaker. |
| **Fleet Schedule Traffic Cop & `.hklock` Arbitrator** | **`Live (v0.1.0)`** | `v0.1.0` | [`Dev/Mother.Core/Features/TrafficCop/ResourceTrafficCopService.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Features/TrafficCop/ResourceTrafficCopService.cs) | Scans `.hklock` sentinels (`PidSafeLockfile`), purges dead-PID stale locks, and records `schedule_deferrals` in `mother.db`. |
| **Fleet Post-Run Power Coordinator (`StayAwake/Sleep/Shutdown`)** | **`Live (v0.1.0)`** | `v0.1.0` | [`Dev/Mother.Core/Features/Power/FleetPowerCoordinator.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Features/Power/FleetPowerCoordinator.cs) | Inhibits sleep on active locks (`SetThreadExecutionState`) and triggers 60s cancellable countdown when all apps hit `0` jobs. |
| **Fleet Coexistence Broker & DPAPI Vault Inspector** | **`Live (v0.1.0)`** | `v0.1.0` | [`Dev/Mother.Core/Features/Coexistence/FleetCoexistenceArbitrator.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Features/Coexistence/FleetCoexistenceArbitrator.cs) | Evaluates cross-app conflicts & `.coexistence.json` parity with `Apply Safe-Stagger` and key-metadata-only DPAPI inspection. |
| **Single Gateway (`:5100`), Cold-Wake Proxy & Tailscale PWA** | **`Live (v0.1.0)`** | `v0.1.0` | [`Dev/Mother.Server/MotherWebHost.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Server/MotherWebHost.cs) | Reverse-proxies `/api/v1/apps/<id>/*` & `/app/<id>/*` with Cold-Wake, SSE `/api/v1/suite/events`, CGNAT filter, and QR SVG. |
| **Titanium Sky (`#38bdf8`) WebView2 + Web UI (`Alt+1..7`)** | **`Live (v0.1.0)`** | `v0.1.0` | [`Dev/Mother.Server/wwwroot/js/app.js`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Server/wwwroot/js/app.js) | Outer horizontal tabs (`Alt+1..7`), collapsible sidebar (`Ctrl+B`), mobile nav (`<= 820px`), `Ctrl+\`` terminal, `Ctrl+S` 20-snapshot rollback. |
| **Clean-Root Launcher & 4-Pillar Zero-PII Installer (`Foundation v0.3.0`)** | **`Live (v0.1.0)`** | `v0.1.0` | [`Dev/Mother.Installer/Program.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Installer/Program.cs) | Root `Mother.exe` shim + single-EXE signed Setup Wizard (`--test-mode` auto-contained `--data-dir`, `FindingCount: 0`). |

---

## 2. Suite Parity Tier Coverage (`UNI` / `SIS` / `STD` — Standard 17)

- **Universal (`UNI-01..04`) — `Live (v0.1.0)`:** Tier A 6-folder layout (`Archive\`, `Builds\`, `Dev\`, `Installers\`, `Releases\`, `Sandbox\`), 4-file Root Quartet + `AGENTS.md`, 4-Doc Manual Shelf, clean-root `Mother.exe` launcher, 4-pillar signed zero-PII installer (`Foundation.Installer v0.3.0`), and 6-file `Releases\Latest\` distribution bundle.
- **Sister (`SIS-01..10`) — `Live (v0.1.0)`:** Docked `Ctrl+\`` `foundation-terminal`, `Ctrl+K` & `?` `<dialog closedby="any">` palettes, `SIS-03` dedicated `⚙️ Settings` (`useGlobalFormatRules`, `Ctrl+S`, 20-snapshot rollback/JSON export), `SIS-04` `Intl.Collator` + `moveBefore` sortable tables (`Bulk Select` + `CSV/JSON` export), and `TaskbarCreated` Win32 tray restoration sentinel.
- **Standard (`STD-01..03`) — `Live (v0.1.0)`:** Strict Standard 18 §3 `mother.db` schema (`PRAGMA user_version = 1`), 14-DTO wire-key lockstep (`contracts.js` verified by `Mother.Tests`), and deterministic exit codes (`0`, `1`, `2`, `3`, `130`).

---

## 3. Cross-App Operability, Shared Seams & Conflict Matrix (`@tessa` Registry — `v0.1.0`)

Per [Dev Bible Standard 17 §5–§6.5](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md) and [`v0.1.0.2026-11-12.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-12.md):

| Supervised Sister App | Canonical Port / Sandbox | Shared Discovery & Coexistence Seams | Conflict / Contention Vector | Mother Arbitration & Coexistence Resolution |
| :--- | :---: | :--- | :--- | :--- |
| **`Housekeeping`** | `:5178` / `:15178` | `suite.manifest.json` / `mother.manifest.json`, `/api/v1/coexistence/status`, `/api/v1/scheduler/defer`, `.housekeeping_<pid>.hklock` | Competing NAS/ROM vault jobs with ShadowLibrary, Redroom, legacy-sister-app, or StreamEngine. | Staggers overlapping drive schedules via Traffic Cop and surfaces coexistence conflicts in `Traffic Cop` (`Apply Safe-Stagger`). |
| **`ShadowLibrary`** | `:5179` / `:15179` | `suite.manifest.json` / `mother.manifest.json`, `/api/v1/coexistence/status`, `/api/v1/scheduler/defer`, `.shadowlibrary_<pid>.hklock` | Concurrent bit-rot scrub or DAT audit on `E:` / `H:` while Housekeeping or Greenroom scans the same drive. | Detects `.shadowlibrary_<pid>.hklock` and defers competing scheduled tasks on shared drives; brokers `suite-safe-stagger`. |
| **`Redroom`** | `:5180` / `:15180` | `suite.manifest.json` / `mother.manifest.json`, `/api/v1/coexistence/status`, `/api/v1/scheduler/defer`, `.redroom_<pid>.hklock` | Concurrent media promotion to `V:..Z:` during Housekeeping Media Transcoder or Cloud Sync runs. | Honors `.redroom_<pid>.hklock` in `HkLockScanner` and arbitrates shared media root presets. |
| **`Greenroom`** | `:8420` / `:15420` | `suite.manifest.json` / `mother.manifest.json`, `/api/v1/coexistence/status`, `/api/v1/scheduler/defer` | Redundant disk DAT hashing or legacy `snake_case` manifest fields. | Parses both `camelCase` (`v2`) and `snake_case` (`v1`) manifest keys; arbitrates shared `.coexistence.json` parity. |
| **`StreamEngine`** | `:8430` (`:18766`) / `:15430` | `suite.manifest.json` / `mother.manifest.json`, `/api/v1/coexistence/status`, `.streamengine_<pid>.hklock` | Active broadcast VOD recording/remux colliding with Housekeeping Proxy/Archiver or OS Sleep/Shutdown. | Blocks Fleet Post-Run Sleep/Shutdown while streaming/recording and defers competing jobs on `.streamengine_<pid>.hklock`. |
| **`legacy-sister-app`** | `:8460` / `:15460` | `suite.manifest.json` / `mother.manifest.json`, `/api/v1/coexistence/status`, `.legacy-sister-app_<pid>.hklock` | Overlapping emulator save sync (`.uss` vs. `rclone`) or `.lnk` launcher directories with Housekeeping. | Surfaces save/launcher overlap conflicts in `Traffic Cop` and coordinates shared root presets. |
