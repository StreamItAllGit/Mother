# Mother — Living Feature Map & Fleet Coexistence Registry (`FEATURES.md`)

> **Active Version:** `v0.1.0` (Full SemVer `MAJOR.MINOR.PATCH` — Blueprint: [`v0.1.0.2026-11-04.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-04.md)) | **Maintained By:** `@tessa` (Documentation Master & Strategic Planner) | **Updated:** `2026-11-04`

This living dossier maps all planned and active capabilities of **Mother (`:5100`)**, the unified desktop fleet supervisor, schedule traffic cop, post-run power coordinator, and cross-app coexistence broker for the 6 sister applications (**Housekeeping `:5178`**, **ShadowLibrary `:5179`**, **Redroom `:5180`**, **Greenroom `:8420`**, **StreamEngine `:8430`**, and **legacy-sister-app `:8460`**).

---

## 1. At-a-Glance Feature Index

| Feature | Status | Target Version | Primary Code Location | 1-Line Summary |
| :--- | :--- | :--- | :--- | :--- |
| **Dual-Schema `suite.manifest.json` (`v2`) Discovery** | `Planned` | `v0.1.0` | `Dev/Mother.Core/Discovery/AppDiscoveryService.cs` | Discovers installed sister apps in `<PROGRAMS_ROOT>/Mother\manifests\` (`minSuiteVersion`, `sdkVersion`) with `Warm`/`Cold`/`Disabled` residency. |
| **Master Watchdog (`\Mother_Watchdog`)** | `Planned` | `v0.1.0` | `Dev/Mother.Core/Supervisor/ProcessSupervisor.cs` | Single Task Scheduler watchdog polling child `/api/v1/health` with `3x/60s` crash-loop circuit breaker. |
| **Fleet Schedule Traffic Cop & `.hklock` Arbitrator** | `Planned` | `v0.1.0` | `Dev/Mother.Core/Orchestration/FleetScheduleTrafficCop.cs` | Polls `/api/v1/scheduler/status` and disk `*.hklock` sentinels, issuing `/api/v1/scheduler/defer` (`30m`) on drive contention. |
| **Fleet Post-Run Power Coordinator (`StayAwake/Sleep/Shutdown`)** | `Planned` | `v0.1.0` | `Dev/Mother.Core/Orchestration/FleetPowerCoordinator.cs` | Triggers OS Sleep/Shutdown only after all installed sister apps hit `0` active/queued jobs (+ `60s` cancel toast). |
| **Fleet Coexistence & Settings Conflict Broker** | `Planned` | `v0.1.0` | `Dev/Mother.Core/Orchestration/FleetCoexistenceArbitrator.cs` | Aggregates `/api/v1/coexistence/status` and `<Root>\.coexistence.json` across installed sister apps with 1-click Fleet `Auto-Align`. |
| **Orthogonal Outer Horizontal App Switcher (`Alt+1..7`)** | `Planned` | `v0.1.0` | `Dev/Mother.App/` & `Dev/Mother.Server/wwwroot/` | Single-window WebView2 container with `TrySuspendAsync()` (`<35 MB` idle), `--window` pop-out, and `🏛️ Fleet Overview` dashboard. |
| **4-Pillar Single-Surface Dark WinForms Installer** | `Planned` | `v0.1.0` | `Dev/Mother.Installer/InstallerWizardForm.cs` | `650×434` -> `650×544` Titanium Sky (`#38bdf8`) Setup Wizard powered by `Foundation.Installer.dll` (`v0.1.0`). |

---

## 2. Cross-App Operability, Shared Seams & Conflict Matrix (`@tessa` Registry — `v0.1.0`)

Per [Dev Bible Standard 17 §5–§6.5](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md) and [`v0.1.0.2026-11-04.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-04.md):

| Supervised Sister App | Canonical Port / Sandbox | Shared Discovery & Coexistence Seams | Conflict / Contention Vector | Mother Arbitration & Coexistence Resolution |
| :--- | :---: | :--- | :--- | :--- |
| **`Housekeeping`** | `:5178` / `:15178` | `app\mother.manifest.json`, `/api/v1/coexistence/status`, `/api/v1/scheduler/defer`, `.housekeeping_<pid>.hklock` | Competing NAS/ROM vault jobs with ShadowLibrary, Redroom, legacy-sister-app, or StreamEngine. | Staggers overlapping drive schedules via `/api/v1/scheduler/defer` and surfaces Module 4/5/8 coexistence warnings in `🏛️ Fleet Overview`. |
| **`ShadowLibrary`** | `:5179` / `:15179` | `app\mother.manifest.json`, `/api/v1/coexistence/status`, `/api/v1/scheduler/defer`, `.shadowlibrary_<pid>.hklock` | Concurrent bit-rot scrub or DAT audit on `E:` / `H:` while Housekeeping or Greenroom scans the same drive. | Detects `.shadowlibrary_<pid>.hklock` and defers competing scheduled tasks on `E:` / `H:`; brokers 1-click `Co-Audit Zero-Rewrite Parity`. |
| **`Redroom`** | `:5180` / `:15180` | `app\mother.manifest.json`, `/api/v1/coexistence/status`, `/api/v1/scheduler/defer`, `.redroom_<pid>.hklock` | Concurrent media promotion to `V:..Z:` during Housekeeping Media Transcoder or Cloud Sync runs. | Honors `.redroom_<pid>.hklock` in the Traffic Cop and brokers `Align Housekeeping Media Library Parity`. |
| **`Greenroom`** | `:8420` / `:15420` | `app\mother.manifest.json`, `/api/v1/coexistence/status`, `/api/v1/scheduler/defer` | Redundant disk DAT hashing or legacy `snake_case` manifest fields. | Parses both `camelCase` and `snake_case` manifest keys; brokers `Use Verified Sister .hash & Manifest Cache`. |
| **`StreamEngine`** | `:8430` (`:18766`) / `:15430` | `app\mother.manifest.json`, `/api/v1/coexistence/status`, `.streamengine_<pid>.hklock` | Active broadcast VOD recording/remux colliding with Housekeeping Proxy/Archiver or OS Sleep/Shutdown. | Blocks Fleet Post-Run Sleep/Shutdown while streaming/recording and defers Housekeeping jobs on `.streamengine_<pid>.hklock`. |
| **`legacy-sister-app`** | `:8460` / `:15460` | `app\mother.manifest.json`, `/api/v1/coexistence/status`, `.legacy-sister-app_<pid>.hklock` | Overlapping emulator save sync (`.uss` vs. `rclone`) or `.lnk` launcher directories with Housekeeping. | Surfaces save/launcher overlap warnings in `🏛️ Fleet Overview` and brokers 1-click save-sync delegation. |
