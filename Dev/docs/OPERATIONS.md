# Mother — Operations, Supervision, Traffic Cop & Deployment Runbook (`OPERATIONS.md`)

> **Active Baseline Version:** `v0.1.0` (`v0.2.0` In-Dev) | **Full SemVer (`MAJOR.MINOR.PATCH`)** | **Owner:** `@tessa` ("The Manual Maker") & `@devv` | **Verified By:** `@dean` ([Standard 16 §3.5](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/16_PROJECT_DOSSIERS_AND_FEATURE_TRACKING.md))

This operational runbook documents the canonical production, development, and sandbox folder topologies, CLI flags, environment variables, configuration schemas, port/mutex/lockfile coexistence rules, structured logging and crash-bundle diagnostics, backup/restore procedures, build/sign/deploy/rollback runbooks, `--test-mode` sandbox verification recipes, and incident playbooks for **`Mother`** ([`<WORKSPACE_ROOT>/Mother\`](file:///<WORKSPACE_ROOT>/Mother/)).

---

## 1. Canonical Production vs Development vs Sandbox Topology

Per [Dev Bible Standard 01](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/01_PROJECT_LAYOUTS.md), [Standard 12 §3.1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/12_WORKSPACE_AND_DEPLOYMENT_CLEANLINESS.md#L70-L115), and [Standard 17 §4.1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md#L178-L204):

```text
<PROGRAMS_ROOT>/Mother\                                    # CANONICAL PRODUCTION ROOT (Port :5100, Tailscale :8500)
├── Mother.exe                                         # Clean-Root Launcher Shim (forwards CLI flags to app\Mother.App.exe)
├── Mother-Uninstall.exe                               # Clean-Root Uninstaller Entry
├── app\                                               # All Compiled Binaries, WebView2 Runtime, wwwroot\, version.txt
│   ├── Mother.App.exe / Mother.Server.dll / Mother.Core.dll
│   ├── Foundation.Core.dll / Foundation.Installer.dll / Foundation.UI.dll (v0.1.0)
│   ├── version.txt                                    # "0.1.0"
│   ├── suite.manifest.json                            # Mother's own suite descriptor
│   └── install-manifest.json                          # SHA-256 & file inventory verified by InstallerEngineCore
├── manifests\                                         # Dynamic Registry of Installed Sister Apps (1 JSON per installed app)
│   ├── housekeeping.manifest.json                     # Port :5178
│   ├── shadowlibrary.manifest.json                    # Port :5179
│   ├── redroom.manifest.json                          # Port :5180
│   ├── greenroom.manifest.json                        # Port :8420
│   ├── streamengine.manifest.json                     # Port :8430
│   └── legacy-sister-app.manifest.json                         # Port :8460
├── data\                                              # Canonical Production State (NEVER overwrite from Dev!)
│   ├── settings.json & settings.json.bak              # Suite settings, residency overrides, PostRunPowerAction
│   ├── config_history\                                # 20-Snapshot Automatic Config Rollback History
│   ├── secrets.dpapi                                  # DPAPI-encrypted Discord webhook & tokens (CurrentUser)
│   ├── mother.db (-wal, -shm)                         # SQLite WAL fleet telemetry & schedule deferral ledger
│   └── backups\                                       # Online VACUUM INTO .7z database backups (<= 10 retained)
├── logs\                                              # 10 MB x 5 Rotating Structured Logs (30-day pruning)
│   ├── mother.log
│   └── crashes\                                       # Zero-PII 50-line ring-buffer crash_*.json dumps (<= 20 retained)
└── scripts\                                           # Non-pausing .bat operational scripts (+ scripts\interactive\)
    ├── start-daemon.bat / stop-daemon.bat / status-check.bat / backup-now.bat
    └── interactive\

<WORKSPACE_ROOT>/Mother\                           # DEVELOPMENT & SANDBOX WORKSPACE (Tier A 6-Folder)
├── active.state                                       # Single Lifecycle Marker (version: "0.1.0")
├── README.md / FEATURES.md / ROADMAP.md / CHANGELOG.md# Root Documentation Quartet
├── Archive\                                           # Historical .7z Source Snapshots (Mother-v0.1.0-alpha.1-2026-11-02.7z)
├── Builds\v0.1.0\                                     # Clean-Root Staged Release Output
├── Dev\                                               # Solution (Mother.slnx), Source Slices, Tests, scripts\, docs\
├── Installers\                                        # Signed Mother-Setup-v0.1.0-win-x64.exe (mirrored to <WORKSPACE_ROOT>/Installers\)
├── Releases\                                          # 6-File Distribution Bundles (Latest\ + Latest.7z)
└── Sandbox\                                           # Fail-Closed Test Environment (Port :15100)
    ├── app\                                           # Sandbox binary deployment
    ├── data\mock_programs\                            # Mock sister-app manifests & .coexistence.json / .hklock fixtures
    └── logs\crashes\                                  # Sandbox runtime logs & crash dumps
```

---

## 2. Complete CLI Flags, MSBuild/NuGet/NPM Properties & Environment Variables Reference

### 2.1 `Mother.exe` Runtime CLI Flags & Environment Variables ([`v0.1.0.2026-11-04.md#L34`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-04.md#L34), [`SandboxEnvironment.cs`](file:///<WORKSPACE_ROOT>/Foundation/Dev/src/Foundation.Core/SandboxEnvironment.cs#L43-L128))

| CLI Flag | Environment Variable | Default Value | Purpose & Operational Behavior |
| :--- | :--- | :--- | :--- |
| `--programs-dir <path>` | `MOTHER_PROGRAMS_DIR` | `D:\Programs` (Prod) / `Sandbox\data\mock_programs` (Sandbox) | Root directory scanned by `AppDiscoveryService` for installed sister applications (`*/app/suite.manifest.json`, `*/mother.manifest.json`). |
| `--manifests-dir <path>` | `MOTHER_MANIFESTS_DIR` | `<BaseDir>\manifests` | Directory holding registered `<app-id>.manifest.json` files dropped by sister-app installers. |
| `--data-dir <path>` | `MOTHER_DATA_DIR`, `DATA_DIR` | `<BaseDir>\data` | Directory for `settings.json`, `config_history\`, `secrets.dpapi`, and `mother.db`. |
| `--config <path>` | `MOTHER_CONFIG`, `CONFIG_PATH` | `<DataDir>\settings.json` | Explicit path to the primary suite JSON configuration file. |
| `--log-dir <path>` | `MOTHER_LOG_DIR`, `LOG_DIR` | `<BaseDir>\logs` (Prod) / `<DataDir>\logs` (Sandbox) | Directory for `mother.log` (`10 MB × 5`) and `crashes\crash_*.json`. |
| `--port <int>` | `MOTHER_PORT`, `PORT` | `5100` (Prod) / `15100` (Sandbox) | Public/loopback Kestrel gateway port serving `/api/v1/suite/*` and `/app/<app-id>/*`. |
| `--test-mode` | `MOTHER_TEST_MODE=1`, `TEST_MODE=1` | `false` | Activates fail-closed Sandbox containment: blocks any I/O to `<PROGRAMS_ROOT>/...`, disables OS Sleep/Shutdown and `\Mother_Watchdog` Task Scheduler registration. |
| `--dry-run` | `MOTHER_DRY_RUN=1` | `false` | Simulates schedule deferrals, coexistence preset alignment, and power countdowns without mutating child processes or OS power state. |
| `--headless` | `MOTHER_HEADLESS=1` | `false` | Runs without WinForms/WebView2 GUI or system tray icon. When paired with `--test-mode --headless`, executes a deterministic startup self-test and exits `0`. |
| `--tray-only` / `--minimized` | — | `false` | Starts the Kestrel gateway and system tray sentinel in the background without opening the WebView2 window until clicked. |
| `--suite-tab [<app-id>]` | — | `fleet-overview` | Brings the single-window `Mother` shell to the foreground and focuses `<app-id>`'s outer horizontal tab (`Alt+1..7`). |
| `--window <app-id>` | — | — | Opens `<app-id>` in its own dedicated standalone desktop window while keeping it backed by the `:5100` daemon. |
| `--browser [<app-id>]` | — | — | Opens `http://127.0.0.1:5100/app/<app-id>/` in the default desktop web browser. |

### 2.2 `Mother-Setup-v0.1.0-win-x64.exe` Installer CLI Flags
- `--silent`: Non-interactive installation to `--install-dir` (defaults to `<PROGRAMS_ROOT>/Mother`).
- `--upgrade`: Forces in-place upgrade preserving `manifests\`, `data\`, and `logs\`.
- `--repair`: Re-extracts `app\` and `scripts\` and re-verifies `app\install-manifest.json`.
- `--uninstall` (`--purge-data`): Removes `app\` and `scripts\` (and `data\`/`logs\` only if `--purge-data` is specified).
- `--test-mode --data-dir <Sandbox\data>`: Runs the full install $\rightarrow$ verify $\rightarrow$ upgrade $\rightarrow$ uninstall lifecycle strictly inside `Sandbox\data\installed-app`.

---

## 3. Configuration & Settings Reference (`data\settings.json`)

Managed via `Foundation.Core.GranularSettingsManager` with atomic `.tmp` $\rightarrow$ `.bak` replacement and 20-snapshot `data\config_history\` rotation:

```json
{
  "schemaVersion": 2,
  "suiteId": "mother",
  "port": 5100,
  "tailscaleHttpsPort": 8500,
  "programsDir": "D:\\Programs",
  "postRunPowerAction": "StayAwake",
  "powerCountdownSeconds": 60,
  "scheduleDeferralMinutes": 30,
  "autoAlignWithSisterApps": false,
  "enableTrayToasts": true,
  "enableDiscordNotifications": false,
  "residencyOverrides": {
    "housekeeping": "warm",
    "shadowlibrary": "warm",
    "redroom": "cold",
    "greenroom": "warm",
    "streamengine": "warm",
    "legacy-sister-app": "cold"
  },
  "idleTimeoutMinutes": 15,
  "windowBounds": {
    "width": 1600,
    "height": 960,
    "x": 160,
    "y": 60,
    "isMaximized": false,
    "sidebarExpanded": true,
    "terminalPreset": "M"
  }
}
```

---

## 4. Ports `:5100`, Mutexes, Lockfiles & Sister-App Coexistence

### 4.1 Canonical Port & Loopback Matrix ([Standard 17 §2.1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md#L55-L66))

| Service / Sister App | Production Bind & Port | Gateway Route on `:5100` | Sandbox Port (`--test-mode`) | Default Residency | Brand Accent |
| :--- | :---: | :--- | :---: | :---: | :--- |
| **`Mother` (Suite Gateway)** | `0.0.0.0:5100` / Tailscale `:8500` | `/` & `/api/v1/suite/*` | `:15100` | Always-On | `#38bdf8` (Titanium Sky) |
| **`Housekeeping`** | `127.0.0.1:5178` | `/app/housekeeping/` & `/api/v1/apps/housekeeping/*` | `:15178` | `warm` | `#f59e0b` (Amber) |
| **`ShadowLibrary`** | `127.0.0.1:5179` | `/app/shadowlibrary/` & `/api/v1/apps/shadowlibrary/*` | `:15179` | `warm` | `#6366f1` (Shadow Indigo) |
| **`Redroom`** | `127.0.0.1:5180` | `/app/redroom/` & `/api/v1/apps/redroom/*` | `:15180` | `warm` / `cold` | `#ef4444` (Crimson) |
| **`Greenroom`** | `127.0.0.1:8420` | `/app/greenroom/` & `/api/v1/apps/greenroom/*` | `:15420` | `warm` | `#10b981` (Emerald) |
| **`StreamEngine`** | `127.0.0.1:8430` *(probe `:18766`)* | `/app/streamengine/` & `/api/v1/apps/streamengine/*` | `:15430` | `warm` | `#8b5cf6` (Violet) |
| **`legacy-sister-app`** | `127.0.0.1:8460` | `/app/legacy-sister-app/` & `/api/v1/apps/legacy-sister-app/*` | `:15460` | `warm` / `cold` | `#06b6d4` (Electric Cyan) |

### 4.2 Named OS Mutexes, Task Scheduler & `.hklock` Arbitration
- **Named OS Mutexes:**
  - `Global\Mother_SingleInstance_Mutex`: Ensures only one instance of `Mother.exe` runs in production (second launch brings the existing window to the foreground).
  - `Global\Mother_Supervisor_Active`: Signaled while `Mother` is running so child sister apps suppress their per-app system tray icons.
- **Master Task Scheduler Entry (`\Mother_Watchdog`):** Single Windows Task Scheduler task replacing per-app watchdogs.
- **Distributed `.hklock` & `.coexistence.json` Files:**
  - `FleetScheduleTrafficCop` inspects `<TargetDir>\.<app>_<pid>.hklock` via `Foundation.Core.PidSafeLockfile.IsLockedByLiveProcess` and defers competing scheduled jobs by 30 minutes via `POST /api/v1/scheduler/defer`.
  - `FleetCoexistenceArbitrator` polls `GET /api/v1/coexistence/status` and reads `<Root>\.coexistence.json` to detect and 1-click align conflicting settings across the 6 sister apps.

---

## 5. Logging, Heartbeats `<= 15s`, Crash Bundles & System Doctor

1. **Structured Log Grammar ([Standard 15 §5.1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/15_AUTOMATED_BUG_REPORTS_AND_INSPECTOR_LOGS.md)):**
   - Format: `YYYY-MM-DDTHH:mm:ss.fffZ [v0.1.0] [PID:<pid>/T:<tid>] [LEVEL] [<slice>] <message>`
   - Rotation: `10 MB` max file size × `5` generations (`mother.log`, `mother.1.log` .. `mother.4.log`), with automatic 30-day age pruning.
2. **15-Second Progress Heartbeats (`Foundation.Core.HeartbeatLogger`):**
   - Fleet-wide operations (`Backup All DBs (.7z)`, `Export Fleet Diagnostic Bundle`, `Fleet System Doctor`) emit `[HEARTBEAT]` lines at least once every `<= 15s` and update interactive consoles in-place with `\r\x1b[K`.
3. **Zero-PII 50-Line Crash Bundles (`logs\crashes\crash_*.json`):**
   - Unhandled exceptions flush a 50-line in-memory log ring buffer and active child process states to `<log_dir>\crashes\crash_YYYYMMDD_HHMMSS_v0.1.0.json` (retaining at most 20 files and scrubbing `%USERPROFILE%` $\rightarrow$ `[USER_HOME]`).
4. **`🩺 Fleet System Doctor` (`POST /api/v1/suite/doctor/run`):**
   - Aggregates `/api/v1/system/doctor/run` across all installed sister apps, checks `mother.db` `PRAGMA integrity_check`, verifies `manifests\*.manifest.json` `minSuiteVersion` / `sdkVersion` compatibility, validates drive free space across all `targetDrives`, and provides 1-click **`Backup All DBs (.7z)`** and **`Export Fleet Diagnostic Bundle (.zip)`**.

---

## 6. Backup, Restore & Schema/SDK Migration

### 6.1 Fleet-Wide Database Backups (`scripts\backup-now.bat` & `🩺 Fleet System Doctor`)
1. Executes `SqliteWalPool.VacuumIntoAsync` on `<PROGRAMS_ROOT>/Mother\data\mother.db`.
2. Triggers online hot backups across all running sister apps, packages the snapshots plus `manifests\*.manifest.json` and `data\settings.json` into a timestamped `.7z` archive under `<PROGRAMS_ROOT>/Mother\data\backups\`, and prunes backups beyond the 10 newest archives.

### 6.2 Restoring Configuration or Database State
- **Config Rollback (`Ctrl+S`):** Open `🏛️ Fleet Overview` -> `Config Snapshots` and click **`Restore`** next to any of the 20 timestamped `settings_*.json` snapshots in `data\config_history\`.
- **Database Restore:** Stop `Mother` (`<PROGRAMS_ROOT>/Mother\scripts\stop-daemon.bat`), extract `mother.db` from the `.7z` backup into `<PROGRAMS_ROOT>/Mother\data\`, delete any stale `mother.db-wal` / `mother.db-shm` files, and run `scripts\start-daemon.bat`.

---

## 7. Build, Package, Sign, Deploy & Rollback Runbook

> [!CAUTION]
> **Production Deployment Gate ([Standard 02 §1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/02_SANDBOXING_AND_TESTING.md), [Standard 03 §2–§3](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/03_VERSIONING_AND_RELEASES.md))**
> Never deploy binaries to `<PROGRAMS_ROOT>/Mother\` or stop/restart the live production suite daemon without explicit user confirmation. Nightly/Test builds must stay strictly inside `<WORKSPACE_ROOT>/Mother\{Builds,Installers,Releases,Sandbox}\`.

### 7.1 Build, Test & Package Installer (`Dev\scripts\`)
```powershell
# 1. Build Release binaries (0 warnings)
<WORKSPACE_ROOT>/Mother\Dev\scripts\build.bat

# 2. Run xUnit test suite
<WORKSPACE_ROOT>/Mother\Dev\scripts\test.bat

# 3. Package & Authenticode-sign Mother-Setup-v0.1.0-win-x64.exe
#    (Outputs to Mother\Installers\, mirrors to <WORKSPACE_ROOT>/Installers\, and builds Mother\Releases\v0.1.0\)
<WORKSPACE_ROOT>/Mother\Dev\scripts\package_installer.bat
```

### 7.2 Authorized Production Upgrade & Rollback (`<PROGRAMS_ROOT>/Mother\`)
- **Upgrade:** After explicit user confirmation, run `<WORKSPACE_ROOT>/Installers\Mother-Setup-v0.1.0-win-x64.exe --upgrade` (or interactive GUI). It stops the old daemon gracefully (`5s` drain), backs up `data\`, updates `app\` and `scripts\`, and restarts `Mother`.
- **Rollback:** Run the prior versioned installer from `<WORKSPACE_ROOT>/Mother\Installers\Mother-Setup-v<PrevVer>-win-x64.exe --upgrade` and restore the matching `data\config_history\` snapshot if needed.

---

## 8. Sandbox Verification Recipe (`--test-mode`)

Before any build passes `@dean` Gate 2 and `@inspecter` Step 3 verification, execute this isolated recipe inside `<WORKSPACE_ROOT>/Mother\Sandbox\` (Port `:15100`):

```powershell
# 1. Run full xUnit suite covering dual-schema manifests, .hklock Traffic Cop, and Coexistence Broker
dotnet test "<WORKSPACE_ROOT>/Mother\Dev\Mother.slnx" -c Release --logger "console;verbosity=normal"

# 2. Run deterministic headless Sandbox self-test (Must exit 0 with zero D:\Programs I/O)
<WORKSPACE_ROOT>/Mother\Sandbox\Mother.exe --test-mode --headless `
  --programs-dir "<WORKSPACE_ROOT>/Mother\Sandbox\data\mock_programs" `
  --data-dir "<WORKSPACE_ROOT>/Mother\Sandbox\data" `
  --config "<WORKSPACE_ROOT>/Mother\Sandbox\data\settings.json" `
  --log-dir "<WORKSPACE_ROOT>/Mother\Sandbox\logs" `
  --port 15100

# 3. Run 4-Pillar Installer Audit (Verifies signature, single-EXE, --test-mode lifecycle, and 0 PII leaks)
powershell -ExecutionPolicy Bypass -File "%USERPROFILE%\.gemini\config\skills\dev\scripts\audit_installer.ps1" `
  -InstallerPath "<WORKSPACE_ROOT>/Mother\Installers" `
  -ExpectedVersion "0.1.0"
```

---

## 9. Common Operational Incident Playbooks

### Playbook 1: Child Sister Daemon Trips `3x/60s` Crash-Loop Circuit Breaker
- **Symptom:** Sister app tab in `Mother` shows a red `ERROR` badge and stops auto-restarting.
- **Diagnosis:** Open `Ctrl+\`` Unified Logs filtered to that app, or inspect `<PROGRAMS_ROOT>/<SisterApp>\logs\crashes\crash_*.json` for the root exception (e.g. corrupt config or port conflict).
- **Remediation:** Fix the child issue (or run `🩺 Fleet System Doctor`), then click **`Reset Circuit Breaker & Restart`** on the app's card in `🏛️ Fleet Overview` (or call `POST /api/v1/suite/apps/<app-id>/wake`).

### Playbook 2: Port `:5100` Address Already in Use (`EADDRINUSE`)
- **Symptom:** `Mother.exe` logs a startup port binding error on `127.0.0.1:5100`.
- **Diagnosis:** Run `Get-NetTCPConnection -LocalPort 5100` in PowerShell to identify the holding PID.
- **Remediation:** If an older `Mother.Server` process is still draining, run `<PROGRAMS_ROOT>/Mother\scripts\stop-daemon.bat` to stop it cleanly, then relaunch `Mother.exe`.

### Playbook 3: Schedule Traffic Cop Repeatedly Defers a Sister App's Job (`Suite Drive I/O Mutex`)
- **Symptom:** A scheduled job in `Housekeeping` or `ShadowLibrary` shows multiple 30-minute deferrals in `Fleet Schedules & Traffic Cop`.
- **Diagnosis:** Check `GET /api/v1/suite/coexistence/status` to see which peer app is holding an active job or `<Dir>\.<app>_<pid>.hklock` on the shared drive.
- **Remediation:** Once the long-running primary job finishes, the next scheduled poll runs automatically. If a process died without releasing its `.hklock`, `PidSafeLockfile.IsLockedByLiveProcess` breaks the dead lock automatically on the next check.

### Playbook 4: Sister App Fails to Wake from `Cold` Residency
- **Symptom:** Clicking a `Cold` app tab shows `Starting <AppName>...` for more than 10 seconds and times out.
- **Diagnosis:** Verify `installDir` and `executable` inside `<PROGRAMS_ROOT>/Mother\manifests\<app-id>.manifest.json` point to an existing binary on disk.
- **Remediation:** Run `--repair` on that sister app's installer in `<WORKSPACE_ROOT>/Installers\<AppName>-Setup-*.exe`, which refreshes `manifests\<app-id>.manifest.json` and calls `POST /api/v1/suite/apps/reload`.
