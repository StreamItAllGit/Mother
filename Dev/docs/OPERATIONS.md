# Mother — Operations, Supervision, Traffic Cop & Deployment Runbook (`OPERATIONS.md`)

> **Active Baseline Version:** `v0.1.0` (`Live` — Next Horizon: `v0.2.0`) | **Full SemVer (`MAJOR.MINOR.PATCH`)** | **SDK Pin:** `Foundation v0.3.0` | **Owner:** `@tessa` ("The Manual Maker") & `@devv` | **Verified By:** `@dean` ([Standard 16 §3.5](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/16_PROJECT_DOSSIERS_AND_FEATURE_TRACKING.md))

This operational runbook documents the canonical production, development, and sandbox folder topologies, CLI flags, environment variables, configuration schemas, port/mutex/lockfile coexistence rules, structured JSONL logging and crash-bundle diagnostics, backup/restore procedures, build/sign/deploy/rollback runbooks, `--test-mode` verification recipes, and incident playbooks for **`Mother`** ([`<WORKSPACE_ROOT>/Mother\`](file:///<WORKSPACE_ROOT>/Mother/)).

---

## 1. Canonical Production vs Development vs Sandbox Topology

Per [Dev Bible Standard 01](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/01_PROJECT_LAYOUTS.md), [Standard 12 §3.1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/12_WORKSPACE_AND_DEPLOYMENT_CLEANLINESS.md#L70-L115), and [Standard 17 §4.1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md#L178-L204):

```text
<Drive>:\Programs\Mother\                              # CANONICAL PRODUCTION ROOT (Port :5100, Tailscale :8500)
├── Mother.exe                                         # Clean-Root Launcher Shim (forwards CLI flags to app\Mother.App.exe)
├── Mother-Uninstall.exe                               # Clean-Root Uninstaller Entry (created by Mother.Installer)
├── AGENTS.md                                          # Compact project context anchor
├── README.md                                          # End-user Quick-Start & User Guide
├── app\                                               # Compiled binaries, WebView2 deps, wwwroot\, migrations\, manifests
│   ├── Mother.App.exe / Mother.Server.dll / Mother.Core.dll
│   ├── Foundation.Core.dll / Foundation.Installer.dll / Foundation.Launcher.dll / Foundation.Server.dll / Foundation.UI.dll (v0.3.0)
│   ├── version.txt                                    # "0.1.0"
│   ├── suite.manifest.json                            # Mother's own suite descriptor (schemaVersion: 2)
│   ├── mother.manifest.json                           # Legacy v1 compatibility manifest
│   └── install-manifest.json                          # SHA-256 & file inventory verified by StandardInstallerHost
├── manifests\                                         # Dynamic Registry of Installed Sister Apps (1 JSON per installed app)
├── data\                                              # Canonical Production State (NEVER overwrite from Dev!)
│   ├── settings.json & settings.json.bak              # Suite settings, residency overrides, postRunPowerPolicy
│   ├── config_history\                                # 20-Snapshot Automatic Config Rollback History
│   ├── secrets.dpapi                                  # DPAPI-encrypted Discord webhook & tokens (CurrentUser)
│   ├── mother.db (-wal, -shm)                         # SQLite WAL fleet telemetry & schedule deferral ledger (PRAGMA user_version = 1)
│   └── backups\                                       # Online SQLite backup / .7z archives
├── logs\                                              # Structured daily JSONL logs & Zero-PII crash bundles
│   ├── mother-YYYYMMDD.jsonl
│   └── crashes\                                       # Zero-PII 50-line ring-buffer crash_*.json dumps
└── scripts\                                           # Non-pausing .bat operational scripts (+ scripts\interactive\)

<WORKSPACE_ROOT>/Mother\                           # DEVELOPMENT & SANDBOX WORKSPACE (Tier A 6-Folder)
├── active.state                                       # Single Lifecycle Marker (version: "0.1.0")
├── AGENTS.md                                          # Compact Project Context Anchor (<= 40 lines / 2 KB)
├── README.md / FEATURES.md / ROADMAP.md / CHANGELOG.md# Root Documentation Quartet
├── Archive\                                           # Historical .7z Release & Source Snapshots
├── Builds\v0.1.0\                                     # Clean-Root Staged Release Output (Mother.exe, AGENTS.md, README.md, app\, scripts\)
├── Dev\                                               # Solution (Mother.slnx), Source Slices (<= 400 LOC), Tests, scripts\, docs\
├── Installers\                                        # Signed Mother-Setup-v0.1.0-win-x64.exe (mirrored to <WORKSPACE_ROOT>/Installers\)
├── Releases\                                          # 6-File Distribution Bundles (v0.1.0\ + Latest\ + .7z)
└── Sandbox\                                           # Fail-Closed Test Environment (Port :15100)
    ├── app\                                           # Staged Sandbox Clean-Root Deployment (Sandbox\app\Mother.exe)
    ├── data\                                          # Sandbox settings.json, mother.db, manifests\, mock-apps\
    └── logs\                                          # Sandbox mother-YYYYMMDD.jsonl, screenshots\, crashes\
```

---

## 2. Complete CLI Flags & Environment Variables Reference

### 2.1 `Mother.exe` / `Mother.App.exe` Runtime CLI Flags ([`PathResolver.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Core/Configuration/PathResolver.cs), [`Mother.App/Program.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.App/Program.cs))

| CLI Flag | Environment Variable | Default Value | Purpose & Operational Behavior |
| :--- | :--- | :--- | :--- |
| `--programs-dir <path>` | `MOTHER_PROGRAMS_ROOT` | Dynamic `<NonSystemDrive>:\Programs` (Prod) / `Sandbox\data\mock-apps` (Sandbox) | Root directory scanned by `ManifestRegistryService` for sister-app manifests (`*/app/suite.manifest.json`, `*/mother.manifest.json`). Contained in `--test-mode`. |
| `--data-dir <path>` | `MOTHER_DATA_DIR`, `DATA_DIR` | `<BaseDir>\data` | Directory for `settings.json`, `config_history\`, `secrets.dpapi`, `manifests\`, `backups\`, and `mother.db`. |
| `--config <path>` | `MOTHER_CONFIG`, `CONFIG_PATH` | `<DataDir>\settings.json` | Explicit path to the primary suite JSON configuration file. |
| `--log-dir <path>` | `MOTHER_LOG_DIR`, `LOG_DIR` | `<BaseDir>\logs` (Prod) / `<DataDir>\logs` (Sandbox) | Directory for `mother-YYYYMMDD.jsonl` and `crashes\crash_*.json`. |
| `--port <int>` | `MOTHER_PORT`, `PORT` | `5100` (Prod) / `15100` (Sandbox) | Loopback/gateway Kestrel port serving `/api/v1/suite/*` and `/app/<app-id>/*`. |
| `--test-mode` | `MOTHER_TEST_MODE=1`, `TEST_MODE=1` | `false` | Activates fail-closed Sandbox containment (`SandboxEnvironment.AssertPathContained`), isolates IPC mutexes to `Local\Mother.TestMode.<hash>`, disables OS Sleep/Shutdown and `\Mother_Watchdog` mutations. |
| `--dry-run` | `MOTHER_DRY_RUN=1` | `false` | Simulates schedule deferrals, coexistence presets, and power countdowns without mutating OS power state or Task Scheduler. |
| `--headless` | `MOTHER_HEADLESS=1` | `false` | Runs Kestrel headless without WinForms/WebView2 GUI. Paired with `--test-mode --headless`, executes startup self-test (`mother.db` + Doctor) and exits `0`. |
| `--diagnose` | — | `false` | Runs `FleetDoctorService`, prints structured JSON report to stdout, and exits `0` (`PASS`/`WARN`) or `1` (`FAIL`). |
| `--tray-only` / `--minimized` | — | `false` | Starts the Kestrel gateway and system tray sentinel minimized without opening the WebView2 window until clicked. |
| `--suite-tab <appId>` | — | — | Focuses an already-running `Mother` instance (via named pipe) and switches its outer horizontal tab (`Alt+1..7`) to `<appId>`. |
| `--window <appId>` | — | — | Launches/focuses `<appId>` in a standalone desktop window backed by the `:5100` daemon. |
| `--browser [<appId>]` | — | — | Opens `http://127.0.0.1:<port>/` (or `/app/<appId>/`) in the default web browser. |
| `--version` / `-v`, `--help` / `-h` / `/?` | — | — | Prints semantic version (`0.1.0`) or CLI usage summary and exits `0`. |

### 2.2 `Mother-Setup-v0.1.0-win-x64.exe` Installer CLI Flags ([`Mother.Installer/Program.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Installer/Program.cs))
- `--silent`: Non-interactive installation to `--install-dir` (defaults to `<NonSystemDrive>:\Programs\Mother`).
- `--verify`: Verifies installed `install-manifest.json` SHA-256 inventory and compressed embedded `payload.zip` integrity.
- `--upgrade` / `--repair`: Replaces `app\` and `scripts\` while creating a pre-upgrade backup and preserving `manifests\`, `data\`, `config\`, and `logs\`. Includes `WaitForTargetUnlocked` (`12 × 250ms` retry/backoff) on `Mother.exe`, `app\Mother.App.exe`, and `app\Mother*.dll` to prevent transient file-lock failures during rapid back-to-back upgrades.
- `--uninstall` (`--purge-data`): Removes `app\` and `scripts\` (preserving `data\`, `config\`, and `logs\` unless `--purge-data` is passed).
- `--test-mode --install-dir <SandboxDir>` *(plus `--silent`, `--headless`, `--dry-run`)*: `NormalizeInstallerArgs` automatically appends `--data-dir <installDir>` whenever `--test-mode` and `--install-dir` are passed without an explicit `--data-dir`, keeping the entire install/verify/upgrade/uninstall lifecycle strictly contained inside `<SandboxDir>`.

---

## 3. Configuration & Settings Reference (`data\settings.json`)

Managed via `Foundation.Core.GranularSettingsManager` (`SettingsStore.cs`) with atomic `.tmp` $\rightarrow$ `.bak` replacement and 20-snapshot `data\config_history\` rotation:

```json
{
  "schemaVersion": 1,
  "useGlobalFormatRules": true,
  "port": 5100,
  "testModePort": 15100,
  "bindHost": "127.0.0.1",
  "allowTailscaleBind": false,
  "coldWakeIdleMinutes": 15,
  "healthPollIntervalMs": 5000,
  "sleepInhibitDuringActiveLock": true,
  "postRunPowerPolicy": "StayAwake",
  "postRunIdleSeconds": 60,
  "postRunCountdownSeconds": 60,
  "autoAlignWithSisterApps": false,
  "desktopToastNotifications": true,
  "discordWebhookNotifications": false,
  "customManifestRoots": [],
  "residencyOverrides": {}
}
```

---

## 4. Ports `:5100`, Mutexes, Lockfiles & Sister-App Coexistence

### 4.1 Canonical Port & Loopback Matrix ([Standard 17 §2.1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md#L55-L66))

| Service / Sister App | Production Bind & Port | Gateway Route on `:5100` | Sandbox Port (`--test-mode`) | Default Residency | Brand Accent |
| :--- | :---: | :--- | :---: | :---: | :--- |
| **`Mother` (Suite Gateway)** | `127.0.0.1:5100` *(Tailscale CGNAT opt-in)* | `/` & `/api/v1/suite/*` | `:15100` | Always-On | `#38bdf8` (Titanium Sky) |
| **`Housekeeping`** | `127.0.0.1:5178` | `/app/housekeeping/` & `/api/v1/apps/housekeeping/*` | `:15178` | `warm` | `#f59e0b` (Amber) |
| **`ShadowLibrary`** | `127.0.0.1:5179` | `/app/shadowlibrary/` & `/api/v1/apps/shadowlibrary/*` | `:15179` | `warm` | `#6366f1` (Shadow Indigo) |
| **`Redroom`** | `127.0.0.1:5180` | `/app/redroom/` & `/api/v1/apps/redroom/*` | `:15180` | `warm` / `cold` | `#ef4444` (Crimson) |
| **`Greenroom`** | `127.0.0.1:8420` | `/app/greenroom/` & `/api/v1/apps/greenroom/*` | `:15420` | `warm` | `#10b981` (Emerald) |
| **`StreamEngine`** | `127.0.0.1:8430` *(probe `:18766`)* | `/app/streamengine/` & `/api/v1/apps/streamengine/*` | `:15430` | `warm` | `#8b5cf6` (Violet) |
| **`legacy-sister-app`** | `127.0.0.1:8460` | `/app/legacy-sister-app/` & `/api/v1/apps/legacy-sister-app/*` | `:15460` | `warm` / `cold` | `#06b6d4` (Electric Cyan) |

### 4.2 Named OS Mutexes, Pipes, Task Scheduler & `.hklock` Arbitration
- **Production IPC (`SupervisorIpc.cs`):**
  - `Global\Mother_SingleInstance_Mutex`: Single-instance guard (subsequent launches forward `--suite-tab` / `--window` over `Mother.Suite.Daemon.Pipe` and exit).
  - `Global\Mother_Supervisor_Active`: Signaled while `Mother` owns supervision so child sister apps suppress per-app tray icons.
- **Sandbox IPC (`--test-mode`):** Automatically isolated to `Local\Mother.TestMode.<hash>`, `Local\Mother.TestMode.Supervisor.<hash>`, and `Mother.TestMode.Pipe.<hash>` (SHA-256 prefix of `DataDirectory`).
- **Master Task Scheduler Entry (`\Mother_Watchdog`):** Registered via `WatchdogSchedulerService` (`schtasks.exe` ArgumentList); hard-simulated in `--test-mode` and `--dry-run`.
- **Distributed `.hklock` & `.coexistence.json`:** `HkLockScanner` + `ResourceTrafficCopService` inspect `.hklock` sentinels via `Foundation.Core.PidSafeLockfile.IsLockedByLiveProcess` (supporting `acquiredAtUtc` and `createdAtUtc` fallbacks), mark dead-PID locks `stale`, and persist deferrals in `schedule_deferrals`; `FleetCoexistenceArbitrator` aggregates `/api/v1/coexistence/status` and `<SharedRoot>\.coexistence.json`.

---

## 5. Logging, Heartbeats `<= 15s`, Crash Bundles & System Doctor

1. **Structured Daily JSONL Logs (`MotherDiagnostics.cs`):**
   - Written to `<log_dir>\mother-YYYYMMDD.jsonl` with UTF-8 No-BOM encoding (`seq`, `timestampUtc`, `appId`, `level`, `slice`, `message`) plus a 250-entry in-memory ring buffer exposed to `GET /api/v1/terminal/history` and SSE `log_line`.
2. **15-Second Progress Heartbeats (`Foundation.Core.HeartbeatLogger`):**
   - Emits heartbeats every `<= 15s` with in-place `\r\x1b[K` terminal updates; `StripLeadingHeartbeatTag` ensures a single `[HEARTBEAT]` tag in terminal/SSE output.
3. **Zero-PII 50-Line Crash Bundles (`logs\crashes\crash_*.json`):**
   - `CrashBundleService.RegisterGlobalHandlers` flushes the 50-line diagnostic ring buffer and active job context to `crash_YYYYMMDD_HHMMSS_v0.1.0.json` without PII.
4. **`🩺 Fleet System Doctor` (`POST /api/v1/suite/doctor/run` & `--diagnose`):**
   - Checks `mother.db` `PRAGMA quick_check` & `PRAGMA user_version = 1`, `Foundation.Core` (`v0.3.0`), manifest schema validity, `.hklock` stale counts, `\Mother_Watchdog` status, and crash bundle count, with 1-click **`Backup DB`**, **`Export Bundle`**, and **`Auto-Repair`**.

---

## 6. Backup, Restore & Schema/SDK Migration

### 6.1 Fleet Database Backups (`scripts\backup-now.bat` & `POST /api/v1/suite/doctor/backup-dbs`)
- Executes `SqliteWalPool.VacuumIntoAsync` / online backup on `data\mother.db` into `<DataDir>\backups\`.

### 6.2 Restoring Configuration or Database State
- **Config Rollback (`Ctrl+S`):** Open **`Settings`** and click **`Rollback`** on any of the 20 retained snapshots in `data\config_history\`.
- **Database Restore:** Stop `Mother` (`scripts\stop-daemon.bat`), replace `data\mother.db` from `data\backups\`, remove stale `-wal`/`-shm` sidecars, and run `scripts\start-daemon.bat`.

---

## 7. Build, Package, Sign, Deploy & Rollback Runbook

> [!CAUTION]
> **Production Deployment Gate ([Standard 02 §1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/02_SANDBOXING_AND_TESTING.md), [Standard 03 §2–§3](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/03_VERSIONING_AND_RELEASES.md))**
> Never deploy binaries to `<PROGRAMS_ROOT>/Mother\` or stop/restart the live production suite daemon without explicit user confirmation. Nightly/Test builds must stay strictly inside `<WORKSPACE_ROOT>/Mother\{Builds,Installers,Releases,Sandbox}\`.

### 7.1 Build, Test & Package Installer (`Dev\scripts\`)
```powershell
# 1. Build Release binaries (0 warnings, /warnaserror)
<WORKSPACE_ROOT>/Mother\Dev\scripts\build.bat

# 2. Run xUnit test suite (28/28 tests)
<WORKSPACE_ROOT>/Mother\Dev\scripts\test.bat

# 3. Package & Authenticode-sign Mother-Setup-v0.1.0-win-x64.exe
#    (Outputs Builds\v0.1.0\, Installers\, <WORKSPACE_ROOT>/Installers\, and 6-file Releases\v0.1.0\ + Latest\)
<WORKSPACE_ROOT>/Mother\Dev\scripts\package_installer.bat
```
Operational scripts in `Dev\scripts\`: `build.bat`, `test.bat`, `package_installer.bat`, `run.bat`, `start-daemon.bat`, `stop-daemon.bat`, `status-check.bat`, `doctor-now.bat`, `backup-now.bat`, plus interactive helpers in `Dev\scripts\interactive\{inspect-status.bat,tail-logs.bat}`.

### 7.2 Authorized Production Upgrade & Rollback (`<PROGRAMS_ROOT>/Mother\`)
- **Upgrade:** After explicit user confirmation, run `<WORKSPACE_ROOT>/Installers\Mother-Setup-v0.1.0-win-x64.exe --upgrade` (or interactive GUI).
- **Rollback:** Run the prior versioned installer `Mother-Setup-v<PrevVer>-win-x64.exe --upgrade` and restore a matching `data\config_history\` snapshot if needed.

---

## 8. Sandbox Verification Recipe (`--test-mode`)

Execute this isolated verification recipe inside `<WORKSPACE_ROOT>/Mother\Sandbox\` (Port `:15100`):

```powershell
# 1. Run full xUnit suite (28/28 tests passing, 0 warnings)
dotnet test "<WORKSPACE_ROOT>/Mother\Dev\Mother.slnx" -c Release --logger "console;verbosity=normal"

# 2. Run deterministic headless Sandbox self-test & --diagnose (Exit 0, zero D:\Programs I/O)
<WORKSPACE_ROOT>/Mother\Sandbox\app\Mother.exe --test-mode --headless `
  --data-dir "<WORKSPACE_ROOT>/Mother\Sandbox\data" `
  --log-dir "<WORKSPACE_ROOT>/Mother\Sandbox\logs"

<WORKSPACE_ROOT>/Mother\Sandbox\app\Mother.exe --test-mode --diagnose `
  --data-dir "<WORKSPACE_ROOT>/Mother\Sandbox\data" `
  --log-dir "<WORKSPACE_ROOT>/Mother\Sandbox\logs"

# 3. Run 4-Pillar Installer Audit (Verifies signature, single-EXE, --test-mode lifecycle, and FindingCount: 0)
powershell -ExecutionPolicy Bypass -File "%USERPROFILE%\.gemini\config\skills\dev\scripts\audit_installer.ps1" `
  -InstallerPath "<WORKSPACE_ROOT>/Mother\Installers" `
  -ExpectedVersion "0.1.0"

# 4. Run isolated --test-mode installer lifecycle (--install-dir auto-contains --data-dir when omitted)
<WORKSPACE_ROOT>/Mother\Installers\Mother-Setup-v0.1.0-win-x64.exe --test-mode --silent --headless --install-dir "<WORKSPACE_ROOT>/Mother\Sandbox\installer_test"
```

---

## 9. Common Operational Incident Playbooks

### Playbook 1: Child Sister Daemon Trips `3x/60s` Crash-Loop Circuit Breaker
- **Symptom:** Sister app shows `ERROR` in `Fleet Overview` and stops auto-restarting.
- **Diagnosis:** Press **`Ctrl+\``** to inspect the terminal stream or check `<SisterApp>\logs\crashes\crash_*.json` for the root exception.
- **Remediation:** Resolve the child issue, run **`Run Doctor`**, and click **`Wake`** on the app's row (`POST /api/v1/suite/apps/<app-id>/wake`).

### Playbook 2: Port `:5100` Address Already in Use (`EADDRINUSE`)
- **Symptom:** `Mother.exe` fails to bind `127.0.0.1:5100`.
- **Diagnosis:** Run `Get-NetTCPConnection -LocalPort 5100` in PowerShell to locate the holding PID.
- **Remediation:** Stop the stale process via `scripts\stop-daemon.bat` and relaunch `Mother.exe`.

### Playbook 3: Stale `.hklock` Files Blocking Scheduled Runs
- **Symptom:** `Traffic Cop` reports `STALE` locks or recurring deferrals for a crashed peer process.
- **Diagnosis:** Check `Traffic Cop` $\rightarrow$ `Active Locks` (or `GET /api/v1/suite/traffic-cop/locks`).
- **Remediation:** Click **`Purge Stale Locks`** (`POST /api/v1/suite/traffic-cop/purge-stale`); live PID locks remain protected while dead-PID locks are cleaned safely.

### Playbook 4: Sister App Fails to Wake from `Cold` Residency
- **Symptom:** Clicking `Wake` or navigating to `/app/<app-id>/` returns `503` or stalls.
- **Diagnosis:** Inspect the app's manifest in `manifests\<app-id>.manifest.json` (`installDir`, `executable`, `internalPort`) via `Fleet Overview` row drawer.
- **Remediation:** Run `--repair` on that sister app's installer and click **`Reload Manifests`**.
