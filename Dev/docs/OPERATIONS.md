# Mother — Operations, Supervision, Traffic Cop & Deployment Runbook (`OPERATIONS.md`)

> **Active Baseline Version:** `v0.3.0` (`MPL-2.0`) | **Full SemVer (`MAJOR.MINOR.PATCH[-tag]`)** | **SDK Pin:** `Foundation v0.5.1`

This operational runbook documents the Studio/Suite umbrella and development folder topologies, `Foundation v0.5.1` SDK setup, CLI flags and containment exit codes (`0`, `1`, `2`, `3`, `130`), `data\config\settings.json` (`v2`) migration and schema overlay, discovery precedence and port/mutex/lockfile coexistence rules, structured JSONL logging and crash-bundle diagnostics, backup/restore procedures, single-pass packaging, and operational incident playbooks for **`Mother`**.

---

## 1. Canonical Umbrella Production vs. Development Topology

```text
%LOCALAPPDATA%\Programs\StreamItAll\Mother\Mother\    # DEFAULT LIVE CLEAN RELEASE ROOT (:5100)
├── Mother.exe                                         # Clean-Root Launcher Shim (NativeAOT or single-file partial trim)
├── Mother-Uninstall.exe                               # Clean-Root Uninstaller Entry (managed by Mother.Installer)
├── app\                                               # Self-contained win-x64 runtime, wwwroot\, Assets\, migrations\
│   ├── Mother.App.exe / Mother.Server.dll / Mother.Core.dll
│   ├── Foundation.*.dll                               # v0.5.1 runtime assemblies (Foundation.Automation.* excluded in Release)
│   ├── runtimes\win-x64\native\WebView2Loader.dll     # Bundled WebView2 loader
│   ├── wwwroot\vendor\foundation\                     # Strictly 3 runtime web assets (tokens CSS, terminal CSS, terminal JS)
│   ├── LICENSE                                        # Bundled MPL-2.0 License (keep in app\ only; never loose root AGENTS/LICENSE)
│   ├── version.txt / suite.manifest.json / mother.manifest.json / install-manifest.json
│   └── config\settings.example.json                   # Canonical v2 default configuration overlay
├── data\                                              # User state (config\settings.json, config_history\, manifests\, mother.db, secrets.dpapi, backups\, webview2\)
├── logs\                                              # Rolling daily JSONL logs (mother-YYYYMMDD.jsonl) & crashes\
└── scripts\ (+ interactive\tail-logs.bat)             # Non-pausing operational scripts & interactive log tailer

%LOCALAPPDATA%\Programs\StreamItAll\Mother\manifests\  # Suite umbrella external app manifests

<repo-root>\                                           # DEVELOPMENT WORKSPACE (Tier A 4-Folder)
├── active.state                                       # Single Lifecycle Marker (version: "0.3.0", activeMilestone: "0.4.0", MPL-2.0)
├── AGENTS.md / LICENSE / README.md / FEATURES.md / ROADMAP.md / CHANGELOG.md / SECURITY.md / CONTRIBUTING.md
├── Archive\                                           # Compressed .7z version & pre-filter snapshots
├── Builds\v0.3.0\                                     # Clean-Root staged runtime (no AGENTS.md, no loose LICENSE, no .pdb)
├── Dev\                                               # Mother.slnx, Mother.code-workspace, <= 400 SLOC slices, deploy.ps1, package_installer.ps1
│   ├── lib\                                           # Gitignored Foundation v0.5.1 SDK binaries
│   └── tools\stage2_mother_e2e.py                     # Two-Build 4-drive sandbox E2E harness
└── Releases\                                          # 7-file v0.3.0\ + hardlinked Latest\ + v0.3.0.7z + Latest.7z
```

*Legacy discovery fallback:* Non-isolated, non-sandbox deployments also resolve `MOTHER_HOME`, `HKCU\Software\StreamItAll\Mother\SuiteInstallDir|InstallDir`, and read-only legacy `<Drive>:\Programs\` sibling folders automatically.

---

## 2. Complete CLI Flags, Containment Modes & Exit Codes

### 2.1 `Mother.exe` / `Mother.App.exe` Runtime CLI Flags (`PathResolver.cs`, `MotherLauncherArgsHelper.cs`, `Mother.App/Program.cs`)

| CLI Flag | Environment Variable | Default Value | Purpose & Operational Behavior |
| :--- | :--- | :--- | :--- |
| `--programs-dir <path>` | `MOTHER_PROGRAMS_ROOT` | Umbrella root / `<DataDir>\mock-apps` | Root directory scanned for suite application manifests. |
| `--data-dir <path>` | `MOTHER_DATA_DIR`, `DATA_DIR`, `APP_HOME` | `<BaseDir>\data` | Directory for `config\settings.json`, `scheduler-state.json`, `config_history\`, `secrets.dpapi`, `manifests\`, `backups\`, `webview2\`, and `mother.db`. |
| `--config <path>` | `MOTHER_CONFIG`, `CONFIG_PATH` | `<DataDir>\config\settings.json` | Explicit path to the suite JSON configuration file. |
| `--log-dir <path>` | `MOTHER_LOG_DIR`, `LOG_DIR` | `<BaseDir>\logs` | Directory for `mother-YYYYMMDD.jsonl` and `crashes\crash_*.json`. |
| `--port <int>` | `MOTHER_PORT`, `PORT` | `5100` | Loopback/gateway Kestrel port serving `/api/v1/suite/*`, `/api/v1/scheduler/defer`, `/api/v1/apps/*`, and `/app/*`. |
| *(Test Isolation)* | `APP_ISOLATED_TEST=1` | `false` | Stage 1 isolation: scopes IPC mutexes, suppresses live HKCU/Task Scheduler/`DriveInfo`/sleep mutations. |
| *(4-Drive Sandbox)* | `FOUNDATION_SANDBOX_ROOT` + `SANDBOX_*_DRIVE` | Inactive | Stage 2 sandbox: redirects registry to `JsonHivePlatformRegistry`, folders to `KnownFolderPaths`, and fails closed with `ExitCode = 3` on escape. |
| `--dry-run`, `--diagnose` | `MOTHER_DRY_RUN=1` | `false` | Simulates mutations safely or emits `Fleet Doctor` JSON (`0` pass/warn, `1` fail). |
| `--tray-only` / `--minimized`, `--suite-tab <id>`, `--window <id>`, `--browser [<id>]` | — | `false` | Selects desktop shell, pop-out window, browser, tray-only, or single-instance tab focus (`MotherLauncherArgsHelper` launches asynchronously). |
| `--automation-port <int>`, `--automation-token <tok>` | `FOUNDATION_AUTOMATION_TOKEN` | Disabled | Debug / `-p:MotherTestAutomation=true` UI loopback only; plain Release binaries fail closed immediately with **`ExitCode = 2`**. |
| `--version` / `-v`, `--help` / `-h` / `/?` | — | — | Prints semantic version (`0.3.0`) or CLI usage summary and exits `0`. |

### 2.2 `Mother-Setup-v0.3.0-win-x64.exe` CLI Flags (`MotherInstallerRunner.cs`, `StandardInstallerHost`)
- `--install-dir <dir>`: Overrides target install folder (default `%LOCALAPPDATA%\Programs\StreamItAll\Mother\Mother`).
- `--suite-dir <dir>`: Sets shared suite umbrella directory persisted to `HKCU\Software\StreamItAll\Mother\SuiteInstallDir` (via `IPlatformRegistry`).
- `--profile <lightweight|full|custom>`: Selects install profile (`full` default).
- `--migrate-legacy`: Opt-in migration from a detected legacy flat install via `InstallerEngineCore.MigrateLegacyInstallAsync`.
- `--autostart`: Creates `--tray-only` Startup shortcut independently of Desktop/Start Menu shortcuts (`Software\StreamItAll\Mother` metadata always writes in non-dry-run live/sandbox installs).
- `--silent`, `--quiet`, `--verify`, `--uninstall`, `--purge-data`, `--dry-run`, `--data-dir <dir>`, `--config <file>`, `--log-dir <dir>`, `--version`, `--help`.
- Passing `--automation-port` or `--automation-token` to a plain Release Setup executable fails closed with **`ExitCode = 2`**.

---

## 3. `Foundation v0.5.1` SDK Setup & Settings v2 Migration (`data\config\settings.json`)

- **Clean-Room SDK Setup (`Dev\deploy.ps1`):** `Dev\lib\Foundation.*.dll` binaries are gitignored. Contributors download `Foundation-v0.5.1.zip` from `StreamItAllGit/Foundation` Releases, extract `Foundation.*.dll` into `Dev\lib\`, and place **strictly the 3 vanilla runtime assets** (`foundation-tokens.css`, `foundation-terminal.css`, `foundation-terminal.js`) into `Dev\Mother.Server\wwwroot\vendor\foundation\` (never copy `.tsx`/`.ts` files into `wwwroot\`). See `CONTRIBUTING.md` for full setup steps.
- **Non-Destructive Settings v1 -> v2 Migration (`SettingsMigrationService.cs`):**
  1. Executes **before** `GranularSettingsManager.LoadOrSelfHeal` so existing v1 configurations are never overwritten by defaults.
  2. Relocates legacy `data\settings.json` atomically to `data\config\settings.json` (`schemaVersion: 2`) and renames the legacy file to `data\settings.json.migrated.bak` (moving unreadable files to `.corrupt.bak`).
  3. Upgrades explicit `--config` files, v1 `data\config\settings.json` payloads, `SettingsStore.Rollback()` snapshots, and `PUT /api/v1/suite/settings` `JsonElement` payloads across all 6 canonical keys while stripping migrated v1 keys from `[JsonExtensionData]` and preserving unknown custom keys:
     - `allowTailscaleBind` -> `shouldAllowTailscaleBind`
     - `coldWakeIdleMinutes` -> `idleTimeoutMinutes`
     - `sleepInhibitDuringActiveLock` -> `shouldInhibitSleepDuringActiveLock`
     - `autoAlignWithSisterApps` -> `shouldAutoAlignSisterApps`
     - `desktopToastNotifications` -> `shouldShowDesktopToasts`
     - `discordWebhookNotifications` -> `shouldSendDiscordWebhooks`
- **Canonical v2 Configuration (`Dev\config\settings.example.json`):**
  ```json
  {
    "schemaVersion": 2,
    "useGlobalFormatRules": true,
    "port": 5100,
    "bindHost": "127.0.0.1",
    "shouldAllowTailscaleBind": false,
    "idleTimeoutMinutes": 15,
    "healthPollIntervalMs": 5000,
    "shouldInhibitSleepDuringActiveLock": true,
    "postRunPowerPolicy": "StayAwake",
    "postRunIdleSeconds": 60,
    "postRunCountdownSeconds": 60,
    "shouldAutoAlignSisterApps": false,
    "shouldShowDesktopToasts": true,
    "shouldSendDiscordWebhooks": false,
    "customManifestRoots": [],
    "targetDrives": [],
    "residencyOverrides": {}
  }
  ```

---

## 4. Discovery Precedence, Ports `:5100`, Mutexes & Coexistence

| Discovery Tier (Highest -> Lowest) | Source Location | Isolation / Sandbox Behavior |
| :---: | :--- | :--- |
| **1** | `<DataDir>\manifests\*.json` | Active in all modes (operator override; always wins on `appId` collision). |
| **2** | `<ProgramsRoot>\<App>\` (`suite.manifest.json`, `mother.manifest.json`, or `app\...`) | Active in all modes (`<DataDir>\mock-apps` in Stage 1; umbrella in Stage 2/Prod). |
| **3** | `IPlatformRegistry` (`HKCU\Software\StreamItAll\Mother\Apps\*\InstallDir`) | Suppressed in Stage 1; reads `registry.hive.json` in Stage 2; live HKCU in Prod. |
| **4** | `MotherSettings.CustomManifestRoots` | Active in all modes; validated via `AssertContainedIfIsolatedOrSandboxed`. |
| **5** | Legacy `<Drive>:\Programs\<App>\` | Strictly suppressed from probing host drives in Stage 1/Stage 2; Stage 2 checks contained sandbox storage drive `Programs\` only. |

- **Proxy Routing & Non-HTTP Workers:** `Mother` binds to `127.0.0.1:5100` (with optional `100.64.0.0/10` Tailscale CGNAT filtering). Discovered HTTP workers run on `127.0.0.1:<internalPort>` behind `/app/<app-id>/` (`307` slash redirect) and `/api/v1/apps/<app-id>/*`. Workers with `internalPort <= 0` return HTTP **`503 NO_HTTP_ENDPOINT`**.
- **Named OS Mutexes & Pipes (`SupervisorIpc.cs`):** Live runs use `Global\Mother_SingleInstance_Mutex`, `Global\Mother_Supervisor_Active`, and `Mother.Suite.Daemon.Pipe`; Stage 1/Stage 2 isolated runs scope to `Local\Mother.<Isolated|Sandbox>.<hash>`, `Local\Mother.<Isolated|Sandbox>.Supervisor.<hash>`, and `Mother.<Isolated|Sandbox>.Pipe.<hash>`.
- **Coexistence & Lock Arbitration:** `FleetCoexistenceArbitrator` detects `PortCollision`, `MutualExclusion`, `DriveMountLock`, and `SettingsDrift`, and writes `<SharedRoot>\.coexistence.json` (`schemaVersion: 2`, `rootPath`, `updatedAtUtc`, `appliedPreset`, `settingsDrift`, `targetAppIds`). `HkLockScanner` + `ResourceTrafficCopService` separate `live` vs. dead-PID `stale` `.hklock` files.

---

## 5. Logging, Heartbeats `<= 15s`, Crash Bundles & System Doctor

1. **Structured Daily JSONL Logs (`MotherDiagnostics.cs`):** Writes UTF-8 No-BOM lines to `<log_dir>\mother-YYYYMMDD.jsonl` plus a 250-entry in-memory ring buffer feeding `GET /api/v1/terminal/history` and SSE `log_line`. `scripts\interactive\tail-logs.bat` streams the newest `*.jsonl` file cleanly.
2. **15-Second Progress Heartbeats (`Foundation.Core.HeartbeatLogger`):** Emits heartbeats every `<= 15s` with in-place `\r\x1b[K` updates and normalized single `[HEARTBEAT]` tags.
3. **50-Line Crash Bundles (`logs\crashes\crash_*.json`):** `CrashBundleService.RegisterGlobalHandlers` flushes unhandled exceptions and recent logs cleanly.
4. **`Fleet System Doctor` (`POST /api/v1/suite/doctor/run` & `--diagnose`):** Validates `mother.db` `PRAGMA quick_check` + `PRAGMA user_version = 1`, `Foundation.Core v0.5.1`, discovered manifests (returning **`"pass"` at `0` apps**), stale `.hklock` count, `\Mother_Watchdog`, and crash bundles, with **`Backup DB`**, **`Export Bundle`**, and **`Auto-Repair`**.

---

## 6. Backup, Restore, Single-Pass Build/Package & Verification Runbook

> [!CAUTION]
> Never deploy directly into `%LOCALAPPDATA%\Programs\StreamItAll\Mother\` or stop/restart live production services without explicit operator confirmation. Keep test and nightly builds strictly inside `Builds\` and `Releases\`.

### 6.1 Database Backup & Restore
- **Online Backup:** `Dev\scripts\backup-now.bat` / `POST /api/v1/suite/doctor/backup-dbs` runs `SqliteWalPool.VacuumIntoAsync` into `<DataDir>\backups\`.
- **Config / DB Restore:** Use **`Settings`** (`Ctrl+S`) -> **`Rollback`** (up to 20 snapshots in `data\config_history\`) or stop the daemon (`Dev\scripts\stop-daemon.bat`), restore `data\mother.db` after deleting stale `-wal`/`-shm` sidecars, and restart.

### 6.2 Build, Test, Stage 2 Sandbox E2E & 7-File Packaging
Run from `<repo-root>`:

```powershell
# 1. Run Release xUnit suite (40/40 passing, 0 warnings)
$env:APP_ISOLATED_TEST = "1"
dotnet test .\Dev\Mother.slnx -c Release -p:TreatWarningsAsErrors=true --logger "console;verbosity=normal"
Remove-Item Env:APP_ISOLATED_TEST

# 2. Optional Two-Build Stage 2 4-Drive Sandbox E2E (Automation build -> Sandbox E2E)
powershell -ExecutionPolicy Bypass -File .\Dev\deploy.ps1 -BuildOnly -TestAutomationBuild
python .\Dev\tools\stage2_mother_e2e.py

# 3. Single-Pass Release Stage (Builds\v0.3.0\) & 7-File Packaging (Releases\v0.3.0\ + hardlinked Latest\)
powershell -ExecutionPolicy Bypass -File .\Dev\deploy.ps1 -BuildOnly
powershell -ExecutionPolicy Bypass -File .\Dev\package_installer.ps1
```

- **7-File `Releases\v0.3.0\` Bundle (`Latest\` NTFS-hardlinked):** `Mother-Setup-v0.3.0-win-x64.exe`, `Mother-v0.3.0-win-x64-Portable.zip` (zero `*Setup*.exe`), `README.md`, `FEATURES.md`, `CHANGELOG.md`, `RELEASE_NOTES.md`, and lowercase `SHA256SUMS.txt` (plus `v0.3.0.7z` and `Latest.7z`).

### 6.3 Smoke Verification & Authorized Production Upgrade
```powershell
# Isolated One-Shot Doctor Check (Exit 0)
.\Builds\v0.3.0\Mother.exe --diagnose --data-dir "$env:TEMP\mother_diag\data" --log-dir "$env:TEMP\mother_diag\logs"

# Release Fail-Closed Automation Check (Exit 2)
.\Builds\v0.3.0\Mother.exe --automation-port 51999 --automation-token verify

# Installer Verification & Dry-Run Check
.\Releases\v0.3.0\Mother-Setup-v0.3.0-win-x64.exe --verify
.\Releases\v0.3.0\Mother-Setup-v0.3.0-win-x64.exe --silent --dry-run --data-dir "$env:TEMP\mother_inst\data" --install-dir "$env:TEMP\mother_inst\app" --log-dir "$env:TEMP\mother_inst\logs"
```
- **Upgrade / Rollback:** After explicit user confirmation, run `Mother-Setup-v0.3.0-win-x64.exe` (`--migrate-legacy` only if migrating from a legacy flat install), or re-run an archived prior Setup EXE and restore a matching `data\config_history\` snapshot if rollback is needed.

---

## 7. Common Operational Incident Playbooks

1. **Child Worker Trips `3x/60s` Crash-Loop Circuit Breaker:** Open `Ctrl+\`` terminal (`logs 50` or inspect `<App>\logs\crashes\crash_*.json`), resolve the worker issue, run `doctor`, and execute `wake <appId>`.
2. **Port `:5100` Address Already in Use (`EADDRINUSE`):** Run `Get-NetTCPConnection -LocalPort 5100`, stop the existing instance cleanly (`Dev\scripts\stop-daemon.bat`), and relaunch `Mother.exe`.
3. **Stale `.hklock` Files Blocking Scheduled Runs:** Run `locks` / `purge-stale` in the docked terminal (or **`Traffic Cop`** -> **`Purge Stale Locks`**); live PID locks remain protected while dead-PID locks are cleaned safely.
4. **QR Pairing Fails or Phone Is Blocked:** Confirm **Allow Tailscale bind (`100.64.0.0/10`)** is enabled in **`Settings`**, verify the QR URL uses your Tailscale address/URL under `213` UTF-8 bytes (`400 QR_PAYLOAD_INVALID` if exceeded), and connect over Tailscale rather than raw LAN IP.
5. **CLI App Returns `503 NO_HTTP_ENDPOINT`:** Workers with `internalPort <= 0` have no HTTP UI/API listener; manage them via `schedules[]` or terminal commands instead of `/app/<appId>/`.
