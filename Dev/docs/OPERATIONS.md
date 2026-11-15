# Mother — Operations, Supervision, Traffic Cop & Deployment Runbook (`OPERATIONS.md`)

> **Active Baseline Version:** `v0.2.0` (`MPL-2.0`) | **Full SemVer (`MAJOR.MINOR.PATCH`)** | **SDK Pin:** `Foundation v0.4.0`

This operational runbook documents the Studio/Suite umbrella, development, and sandbox folder topologies, pre-build `Foundation v0.4.0` SDK auto-sync, CLI flags, `data\settings.json` schema (`targetDrives` overlay), port/mutex/lockfile coexistence rules, structured JSONL logging and crash-bundle diagnostics, backup/restore procedures, build/sign/package/rollback runbooks, `--test-mode` verification recipes, and operational incident playbooks for **`Mother`** ([`<WORKSPACE_ROOT>/Mother\`](file:///<WORKSPACE_ROOT>/Mother/)).

---

## 1. Canonical Umbrella Production vs. Development vs. Sandbox Topology

```text
%LOCALAPPDATA%\Programs\StreamItAll\Mother\Mother\    # DEFAULT LIVE CLEAN RELEASE ROOT (:5100, Tailscale :8500)
├── Mother.exe                                         # Clean-Root Launcher Shim (forwards CLI flags to app\Mother.App.exe)
├── Mother-Uninstall.exe                               # Clean-Root Uninstaller Entry (self-copied by Mother.Installer)
├── AGENTS.md                                          # Compact runtime context anchor (Notion-free)
├── LICENSE                                            # Verbatim MPL-2.0 License
├── app\                                               # Compiled binaries, wwwroot\, flat Assets\, flat migrations\
│   ├── Mother.App.exe / Mother.Server.dll / Mother.Core.dll
│   ├── Foundation.*.dll (v0.4.0, auto-synced pre-build)
│   ├── LICENSE                                        # Bundled MPL-2.0 License
│   ├── version.txt / suite.manifest.json / mother.manifest.json / install-manifest.json
│   └── config\settings.example.json                   # Neutral default configuration overlay
├── data\                                              # Canonical User State (settings.json, scheduler-state.json, mother.db, secrets.dpapi, backups\)
├── logs\                                              # Rolling daily JSONL logs & crashes\
└── scripts\ (+ interactive\)                          # Non-pausing .bat operational scripts

%LOCALAPPDATA%\Programs\StreamItAll\Mother\manifests\  # Suite Umbrella sister-app manifests (or Sandbox\data\manifests\)

<WORKSPACE_ROOT>/Mother\                           # DEVELOPMENT & SANDBOX WORKSPACE (Tier A 6-Folder)
├── active.state                                       # Single Lifecycle Marker (version: "0.2.0", MPL-2.0)
├── AGENTS.md / LICENSE / README.md / FEATURES.md / ROADMAP.md / CHANGELOG.md
├── Archive\                                           # Historical .7z Release & Pre-Filter Snapshots
├── Builds\v0.2.0\                                     # Clean-Root Staged Release Output (no loose README.md, no .pdb)
├── Dev\                                               # Mother.slnx, <= 400 LOC Slices, config\settings.example.json, deploy.ps1, package_installer.ps1
├── Installers\                                        # Historical + Mother-Setup-v0.2.0-win-x64.exe (latest mirrored to <WORKSPACE_ROOT>/Installers\)
├── Releases\                                          # Sanitized 6-file v0.2.0\ + v0.1.0\ + Latest\ + v0.2.0.7z + Latest.7z
└── Sandbox\                                           # Fail-Closed Test Environment (:15100; shares Sandbox\ ancestor for sister-apps\)
```

*Legacy fallback:* Non-sandbox deployments also check `MOTHER_HOME`, `HKCU\Software\StreamItAll\Mother\SuiteInstallDir|InstallDir`, and read-only legacy `<PROGRAMS_ROOT>/` sister roots automatically.

---

## 2. Complete CLI Flags & Environment Variables Reference

### 2.1 `Mother.exe` / `Mother.App.exe` Runtime CLI Flags ([`PathResolver.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Core/Core/Configuration/PathResolver.cs), [`Mother.App/Program.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.App/Program.cs))

| CLI Flag | Environment Variable | Default Value | Purpose & Operational Behavior |
| :--- | :--- | :--- | :--- |
| `--programs-dir <path>` | `MOTHER_PROGRAMS_ROOT` | Umbrella root / `Sandbox\data\mock-apps` | Root directory scanned for sister manifests. Sibling-sandbox contained under `--test-mode`. |
| `--data-dir <path>` | `MOTHER_DATA_DIR`, `DATA_DIR` | `<BaseDir>\data` | Directory for `settings.json`, `scheduler-state.json`, `config_history\`, `secrets.dpapi`, `manifests\`, `backups\`, and `mother.db`. |
| `--config <path>` | `MOTHER_CONFIG`, `CONFIG_PATH` | `<DataDir>\settings.json` | Explicit path to the primary suite JSON configuration file. |
| `--log-dir <path>` | `MOTHER_LOG_DIR`, `LOG_DIR` | `<BaseDir>\logs` (Prod) / `<DataDir>\logs` (Sandbox) | Directory for `mother-YYYYMMDD.jsonl` and `crashes\crash_*.json`. |
| `--port <int>` | `MOTHER_PORT`, `PORT` | `5100` (Prod) / `15100` (Sandbox) | Loopback/gateway Kestrel port serving `/api/v1/suite/*`, `/api/v1/scheduler/defer`, `/api/v1/apps/*`, and `/app/*`. |
| `--test-mode` | `MOTHER_TEST_MODE=1`, `TEST_MODE=1` | `false` | Activates fail-closed Sandbox containment (`SandboxEnvironment.ResolveSiblingSandboxRoot`), isolates IPC mutexes, disables OS Sleep/Shutdown and `\Mother_Watchdog` mutations. |
| `--dry-run`, `--headless`, `--diagnose` | `MOTHER_DRY_RUN=1`, `MOTHER_HEADLESS=1` | `false` | Simulates mutations, runs headless API or `--test-mode --headless` self-test, or prints Doctor JSON (`0`/`1`). |
| `--tray-only` / `--minimized`, `--suite-tab <id>`, `--window <id>`, `--browser [<id>]` | — | `false` | Controls GUI presentation modes and single-instance IPC tab routing. |
| `--version` / `-v`, `--help` / `-h` / `/?` | — | — | Prints semantic version (`0.2.0`) or CLI usage summary and exits `0`. |

### 2.2 `Mother-Setup-v0.2.0-win-x64.exe` CLI Flags ([`MotherInstallerRunner.cs`](file:///<WORKSPACE_ROOT>/Mother/Dev/Mother.Installer/MotherInstallerRunner.cs))
- `--install-dir <dir>`: Overrides target install folder (default `%LOCALAPPDATA%\Programs\StreamItAll\Mother\Mother`).
- `--suite-dir <dir>`: Sets shared suite umbrella directory persisted to `HKCU\Software\StreamItAll\Mother\SuiteInstallDir`.
- `--profile <lightweight|full>`: Selects install profile (`full` default).
- `--migrate-legacy`: Opt-in migration from legacy flat `<PROGRAMS_ROOT>/Mother\` via `InstallerEngineCore.MigrateLegacyInstallAsync`.
- `--autostart`: Creates `--tray-only` Startup shortcut independently of Desktop/Start Menu shortcuts (HKCU metadata always writes for live installs).
- `--silent`, `--headless`, `--verify`, `--uninstall` (dry-run guarded before touching disk), `--purge-data`, `--dry-run`, `--test-mode`, `--visible-test` (saves wizard PNG strictly to `<logDir>\screenshots\installer-visible-test.png`).
- `NormalizeInstallerArgs` auto-appends `--data-dir <installDir>` whenever `--test-mode` and `--install-dir` are passed without an explicit `--data-dir`.

---

## 3. Pre-Build SDK Auto-Sync & Configuration Overlay (`data\settings.json`)

- **Pre-Build `Foundation v0.4.0` Auto-Sync (`Dev\deploy.ps1#L22-L32`):** Because `Dev\lib\Foundation.*.dll` binaries are gitignored for clean-room distribution, running `Dev\deploy.ps1` automatically checks for `Dev\lib\Foundation.Core.dll` and invokes `<WORKSPACE_ROOT>/SharedLibs\Foundation\v0.4.0\tools\sync_foundation_sdk.py --sync-consumer <WORKSPACE_ROOT>/Mother --web-dir Dev\Mother.Server\wwwroot\vendor\foundation` when missing.
- **Neutral Configuration Overlay (`Dev\config\settings.example.json`):** `targetDrives` is configured in `data\settings.json` (`MotherSettings.TargetDrives`) rather than `suite.manifest.json`, and uses a 20-snapshot ring buffer (`data\config_history\`):
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
    "targetDrives": [],
    "residencyOverrides": {}
  }
  ```

---

## 4. Ports `:5100`, Mutexes, Lockfiles & Sister-App Coexistence

| Service / Sister App | Production Bind & Port | Gateway Route on `:5100` | Sandbox Port (`--test-mode`) | Default Residency | Brand Accent |
| :--- | :---: | :--- | :---: | :---: | :--- |
| **`Mother` (Suite Gateway)** | `127.0.0.1:5100` *(Tailscale CGNAT opt-in)* | `/` & `/api/v1/suite/*` | `:15100` | Always-On | `#38bdf8` (Titanium Sky) |
| **`Housekeeping`** | `127.0.0.1:5178` | `/app/housekeeping/` & `/api/v1/apps/housekeeping/*` | `:15178` | `warm` | `#f59e0b` (Amber) |
| **`ShadowLibrary`** | `127.0.0.1:5179` | `/app/shadowlibrary/` & `/api/v1/apps/shadowlibrary/*` | `:15179` | `warm` | `#6366f1` (Shadow Indigo) |
| **`Redroom`** | `127.0.0.1:5180` | `/app/redroom/` & `/api/v1/apps/redroom/*` | `:15180` | `warm` / `cold` | `#ef4444` (Crimson) |
| **`Greenroom`** | `127.0.0.1:8420` | `/app/greenroom/` & `/api/v1/apps/greenroom/*` | `:15420` | `warm` / `cold` | `#10b981` (Emerald) |
| **`StreamEngine`** | `127.0.0.1:8430` *(probe `:18766`)* | `/app/streamengine/` & `/api/v1/apps/streamengine/*` | `:15430` | `warm` | `#8b5cf6` (Violet) |
| **`legacy-sister-app`** | `127.0.0.1:8460` | `/app/legacy-sister-app/` & `/api/v1/apps/legacy-sister-app/*` | `:15460` | `warm` / `cold` | `#06b6d4` (Electric Cyan) |

- **Named OS Mutexes & Pipes (`SupervisorIpc.cs`):** Live uses `Global\Mother_SingleInstance_Mutex`, `Global\Mother_Supervisor_Active`, and `Mother.Suite.Daemon.Pipe`; `--test-mode` isolates to `Local\Mother.TestMode.<hash>`, `Local\Mother.TestMode.Supervisor.<hash>`, and `Mother.TestMode.Pipe.<hash>`.
- **Master Task Scheduler Entry (`\Mother_Watchdog`):** Registered via `WatchdogSchedulerService` (`schtasks.exe`); hard-simulated in `--test-mode` and `--dry-run`.
- **Scheduler Persistence & Lock Arbitration:** `FleetScheduleEvaluator` stores `scheduler-state.json` atomically alongside SQLite `schedule_deferrals`; `HkLockScanner` + `ResourceTrafficCopService` inspect `.hklock` via `PidSafeLockfile.IsLockedByLiveProcess` and expose `GET`/`POST /api/v1/scheduler/defer`.

---

## 5. Logging, Heartbeats `<= 15s`, Crash Bundles & System Doctor

1. **Structured Daily JSONL Logs (`MotherDiagnostics.cs`):** `<log_dir>\mother-YYYYMMDD.jsonl` (UTF-8 No-BOM) + 250-entry in-memory ring buffer serving `GET /api/v1/terminal/history` and SSE `log_line`.
2. **15-Second Progress Heartbeats (`Foundation.Core.HeartbeatLogger`):** Emits heartbeats every `<= 15s` with in-place `\r\x1b[K` terminal updates and single `[HEARTBEAT]` normalization (`StripLeadingHeartbeatTag`).
3. **Zero-PII 50-Line Crash Bundles (`logs\crashes\crash_*.json`):** `CrashBundleService.RegisterGlobalHandlers` flushes sanitized 50-line diagnostic state cleanly.
4. **`Fleet System Doctor` (`POST /api/v1/suite/doctor/run` & `--diagnose`):** Validates `mother.db` `PRAGMA quick_check` + `PRAGMA user_version = 1`, `Foundation.Core v0.4.0`, manifests, stale `.hklock` count, `\Mother_Watchdog`, and crash bundles, with **`Backup DB`**, **`Export Bundle`**, and **`Auto-Repair`**.

---

## 6. Backup, Restore, Packaging & Rollback Runbook

> [!CAUTION]
> Never deploy to `%LOCALAPPDATA%\Programs\StreamItAll\Mother\`, `<PROGRAMS_ROOT>/Mother\`, or stop/restart live production services without explicit user confirmation. Nightly/Test builds must remain inside `Builds\`, `Installers\`, `Releases\`, and `Sandbox\`.

### 6.1 Database Backup & Restore
- **Online Backup:** `scripts\backup-now.bat` / `POST /api/v1/suite/doctor/backup-dbs` executes `SqliteWalPool.VacuumIntoAsync` into `<DataDir>\backups\`.
- **Config / DB Restore:** Use **`Settings`** (`Ctrl+S`) -> **`Rollback`** (20 retained snapshots) or stop daemon (`scripts\stop-daemon.bat`), restore `data\mother.db` after removing stale `-wal`/`-shm` sidecars, and restart.

### 6.2 Build, Test & Single-Pass Packaging (`Dev\scripts\`)
```powershell
# 1. Run Release xUnit suite (35/35 passing, 0 warnings)
dotnet test "<WORKSPACE_ROOT>/Mother\Dev\Mother.slnx" -c Release -p:TreatWarningsAsErrors=true --logger "console;verbosity=normal"

# 2. Stage Clean Release Root (auto-syncs Foundation v0.4.0 SDK if Dev\lib\ is empty) & Package Installer + 6-file Releases\v0.2.0\ + Latest\
powershell -ExecutionPolicy Bypass -File "<WORKSPACE_ROOT>/Mother\Dev\deploy.ps1" -BuildOnly
powershell -ExecutionPolicy Bypass -File "<WORKSPACE_ROOT>/Mother\Dev\package_installer.ps1"

# 3. Audit Installer (FindingCount: 0)
powershell -ExecutionPolicy Bypass -File "%USERPROFILE%\.gemini\config\skills\dev\scripts\audit_installer.ps1" `
  -InstallerPath "<WORKSPACE_ROOT>/Mother\Installers" `
  -StagingDir "<WORKSPACE_ROOT>/Mother\Builds\v0.2.0" `
  -ExpectedVersion "0.2.0"
```

### 6.3 Authorized Production Upgrade & Rollback
- **Upgrade:** After explicit user confirmation, run `Mother-Setup-v0.2.0-win-x64.exe` (or `--silent`) targeting the operator's approved install directory (`--migrate-legacy` if upgrading from `<PROGRAMS_ROOT>/Mother\`).
- **Rollback:** Run a prior versioned installer from `Mother\Installers\` (all historical installers are retained in `Installers\`) and restore a matching `data\config_history\` snapshot if needed.

---

## 7. Sandbox Verification Recipe (`--test-mode`)

```powershell
# 1. Headless Sandbox Self-Test & Doctor Check (Exit 0)
<WORKSPACE_ROOT>/Mother\Sandbox\app\Mother.exe --test-mode --headless `
  --data-dir "<WORKSPACE_ROOT>/Mother\Sandbox\data" `
  --log-dir "<WORKSPACE_ROOT>/Mother\Sandbox\logs"

<WORKSPACE_ROOT>/Mother\Sandbox\app\Mother.exe --test-mode --diagnose `
  --data-dir "<WORKSPACE_ROOT>/Mother\Sandbox\data" `
  --log-dir "<WORKSPACE_ROOT>/Mother\Sandbox\logs"

# 2. Visible Wizard Capture & Dry-Run Uninstall Preservation
<WORKSPACE_ROOT>/Mother\Installers\Mother-Setup-v0.2.0-win-x64.exe --visible-test --test-mode --dry-run `
  --data-dir "<WORKSPACE_ROOT>/Mother\Sandbox\data\inst-vis" `
  --log-dir "<WORKSPACE_ROOT>/Mother\Sandbox\logs"
```

---

## 8. Common Operational Incident Playbooks

1. **Child Sister Daemon Trips `3x/60s` Crash-Loop Circuit Breaker:** Open `Ctrl+\`` terminal (`logs 50` or inspect `<SisterApp>\logs\crashes\crash_*.json`), resolve root cause, run `doctor`, and type `wake <appId>` (or click **`Wake`**).
2. **Port `:5100` Address Already in Use (`EADDRINUSE`):** Run `Get-NetTCPConnection -LocalPort 5100`, stop the stale PID via `scripts\stop-daemon.bat`, and relaunch `Mother.exe`.
3. **Stale `.hklock` Files Blocking Scheduled Runs:** Run `locks` / `purge-stale` in the docked terminal (or **`Traffic Cop`** -> **`Purge Stale Locks`**); live PID locks stay protected while dead-PID locks are cleaned safely.
4. **Sister App Fails to Wake from `Cold` Residency:** Verify manifest `commands.startDaemon`/`executable`, `internalPort`, and `InstallDir` in `manifests\<appId>.manifest.json`, repair the sister installer if needed, and run `reload` (`POST /api/v1/suite/apps/reload`).
