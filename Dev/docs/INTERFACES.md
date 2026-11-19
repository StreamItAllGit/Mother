# Mother — Single-API Gateway Reference, Manifests & File Contracts (`INTERFACES.md`)

> **Active Baseline Version:** `v0.3.0` (`MPL-2.0`) | **Full SemVer (`MAJOR.MINOR.PATCH[-tag]`)** | **SDK Pin:** `Foundation v0.5.1`

This reference manual documents the authentication and transport boundaries, the Single Public API Gateway (`/api/v1/suite/*`, `/api/v1/scheduler/defer`, `/api/v1/apps/<app-id>/*`, and `/app/<app-id>/` on `:5100`), real-time SSE streams, MCP proxy contracts, CLI subcommands and deterministic exit codes (`0`, `1`, `2`, `3`, `130`), declarative `installer.manifest.json` (`v2`), `suite.manifest.json` (`v2`), Standard 18 `settings.json` (`v2`), `.coexistence.json` (`v2`), `.hklock`, `.hash`, and SQLite/DTO wire schemas for **`Mother`**.

---

## 1. Authentication & Transport Security

1. **Single Public Gateway (`:5100` HTTP / `:8500` Tailscale Serve HTTPS):**
   - `Mother` is the sole listener exposed on workstation loopback (`http://127.0.0.1:5100/`) or Tailscale tailnet (`https://<tailnet>:8500/`, filtered by `100.64.0.0/10` CGNAT allowlist in `TailscaleAndQrEndpoints`).
   - All discovered supervised applications bind strictly to `127.0.0.1` loopback behind `Mother` (`--managed-by-mother --bind 127.0.0.1 --port <effectivePort>`).
2. **DPAPI Secret Storage at Rest:**
   - Outbound webhook URLs (`discord_webhook_url`) and optional gateway tokens are encrypted inside `<DataDir>\secrets.dpapi` via `Foundation.Core.DpapiSecretStore` (`DataProtectionScope.CurrentUser`).
3. **Scoped Mobile PWA & Offline ISO QR Pairing:**
   - `Mother` serves the Suite PWA manifest (`/manifest.webmanifest`, `scope: "/"`) and proxies per-app scoped PWAs (`/app/<app-id>/manifest.webmanifest`, `scope: "/app/<app-id>/"`), paired via deterministic ISO/IEC 18004 SVG QR codes (`Foundation.Core.QrCodeEncoder.RenderSvg`).
4. **Debug/TestAutomation Loopback Boundary (`MotherAutomationHost`):**
   - Optional Win32/WebView2 test automation (`Foundation.Automation`) compiles **only** in `Debug` or `-p:MotherTestAutomation=true`, binds strictly to `127.0.0.1` with constant-time Bearer token auth and `Host`/`Origin` validation (`401`/`403`), and is completely absent in plain Release builds (`ExitCode = 2`).

---

## 2. REST API Reference `/api/v1/*`

### 2.1 Suite-Level Gateway, Settings & Scheduler Endpoints (`:5100`)

| HTTP Method & Route | Request Body (`camelCase`) | Response Payload (`camelCase` JSON) | Behavior & Description |
| :--- | :--- | :--- | :--- |
| `GET /api/v1/suite/health` *(Alias: `/api/v1/health`)* | — | `{ "status": "ok", "suiteId": "mother", "version": "0.3.0", "sdkVersion": "0.5.1", "pid": 4120, "uptimeSeconds": 120, "isolatedMode": false, "sqliteUserVersion": 1, "registeredApps": 0, "warmApps": 0, "coldApps": 0, "disabledApps": 0 }` | Suite daemon liveness, SemVer, Foundation SDK pin (`0.5.1`), SQLite `user_version`, and dynamic app counts (`0` on fresh install). |
| `GET /api/v1/suite/manifest` *(Alias: `/api/v1/manifest`)* | — | `{ "schemaVersion": 2, "appId": "mother", "name": "Mother", "version": "0.3.0", "sdkVersion": "0.5.1", "brandAccent": "#38bdf8", "port": 5100, "isolatedMode": false, "capabilities": [...], "registeredAppIds": [...] }` | Exposes Mother's suite capability manifest and discovered `appId` list. |
| `GET /api/v1/suite/apps` | — | `{ "suiteId": "mother", "isolatedMode": false, "apps": [...] }` | Lists dynamically discovered suite apps (`[]` when none installed); ordered by port then `appId`. |
| `POST /api/v1/suite/apps/reload` | `{}` | `{ "reloadedCount": 0, "apps": [...] }` | Hot-reloads manifests by strict precedence: `data/manifests/` > `ProgramsRoot` > `IPlatformRegistry` `Apps` > `CustomManifestRoots` > host-gated legacy fallback. |
| `POST /api/v1/suite/apps/{appId}/residency` | `{ "residencyMode": "warm\|cold\|disabled" }` | `{ "app": {...} }` | Persists per-app residency override into `data\config\settings.json` (`v2`) and reconciles worker state. |
| `POST /api/v1/suite/apps/{appId}/wake` | `{}` | `{ "outcome": {...}, "app": {...} }` | Arbitrates coexistence (`409 COEXISTENCE_CONFLICT` if peer holds live `.hklock`), wakes `cold` worker (`503` if startup fails). |
| `POST /api/v1/suite/apps/{appId}/stop` | `{}` | `{ "appId": "<appId>", "stopped": true, "app": {...} }` | Gracefully stops (`5s` drain) a specific child worker. |
| `POST /api/v1/suite/jobs/stop-all` | `{}` | `{ "stoppedApps": [...], "power": {...} }` | Stops all active workers/jobs and cancels any active sleep/shutdown countdown. |
| `GET /POST /api/v1/scheduler/defer` | `{ "appId": "<appId>", "scheduleId": "<id>", "driveMount": "D:\\", "deferMinutes": 20, "deferReason": "...", "blockingAppId": "..." }` | `{ "schedules": [...], "deferrals": [...] }` / updated deferral | Reads or records schedule deferrals in SQLite `schedule_deferrals` and `scheduler-state.json`. |
| `GET/POST /api/v1/suite/scheduler/traffic-cop` | `{ "appId": "...", "jobId": "...", "driveMount": "...", "minutes": 30 }` | `{ "activeLocks": [...], "recentDeferrals": [...] }` | Returns active/stale `.hklock` holds and recent schedule deferrals. |
| `GET /api/v1/suite/traffic-cop/locks` & `POST .../purge-stale` | `{}` | `{ "locks": [...], "purgedCount": 1 }` | Lists `.hklock` sentinels or safely purges dead-PID `stale` sentinels. |
| `GET /api/v1/suite/coexistence/status` *(Alias: `/api/v1/coexistence/status`)* | — | `{ "shouldAutoAlignSisterApps": false, "sharedRoots": [...], "conflicts": [...], "apps": [...] }` | Aggregates coexistence status, shared roots, and conflicts (`shouldAutoAlignSisterApps` Standard 18 wire key). |
| `POST /api/v1/suite/coexistence/apply-recommendations` | `{ "targetAppIds": [...], "presetId": "suite-safe-stagger", "sharedRoots": [...] }` | `{ "appliedPreset": "...", "updatedApps": [...], "status": {...} }` | Writes `<SharedRoot>\.coexistence.json` (`schemaVersion: 2`) keyed by dynamic `appId`s. |
| `GET /api/v1/suite/power/status`, `POST .../action`, `POST .../cancel-countdown` | `{ "policy": "StayAwake\|Sleep\|Shutdown" }` / `{ "action": "cancel\|evaluate" }` | `{ "policy": "...", "fleetBusy": false, "sleepInhibited": false, "watchdogRegistered": true, "countdownActive": false, "secondsRemaining": 0 }` | Inspects or updates Post-Run Power Coordinator and `\Mother_Watchdog` state. |
| `GET /api/v1/suite/doctor` & `POST .../run` | `{}` | `{ "overall": "pass\|warn\|fail", "suiteChecks": [...], "crashBundleCount": 0 }` | Runs `FleetDoctorService` diagnostics (`overall: "pass"` at 0 apps; exposes `.../repair`, `.../backup-dbs`, `.../export-bundle`). |
| `GET /api/v1/suite/notifications` & `POST .../test` | `{ "appId": "...", "slug": "...", "title": "...", "description": "..." }` | `{ "notifications": [...] }` / `{ "notification": {...} }` | Queries `notification_history` or triggers a dispatch test (`simulated` under isolation/sandbox). |
| `GET /api/v1/suite/secrets/metadata` | — | `{ "keys": [{ "vaultPath": "...", "appId": "...", "keyName": "...", "vaultSizeBytes": 64, "lastModifiedUtc": "..." }] }` | Returns DPAPI key metadata only (never decrypted secret values). |
| `GET/PUT /api/v1/suite/settings`, `.../snapshots`, `.../rollback`, `.../export` | `JsonElement` (v2 or v1 aliases) / `{ "snapshotId": "..." }` | `{ "settings": {...}, "snapshots": [...] }` | Reads/writes `data\config\settings.json` (`schemaVersion: 2`, preserves unknown JSON keys, auto-normalizes v1 aliases on `PUT` and `Rollback()`). |
| `GET /api/v1/terminal/history`, `POST .../exec`, `POST .../clear`, `GET .../stream` | `{ "command": "<whitelisted>" }` | `{ "accepted": true, "output": "..." }` | `TerminalCommandDispatcher` (`help`, `status`, `health`, `apps`, `traffic`, `locks`, `coexistence`, `doctor`, `logs [1..100]`, `purge-stale`, `reload`, `wake <appId>`, `stop <appId>`, `power [StayAwake\|Sleep\|Shutdown]`). |
| `GET /api/v1/suite/qr.svg?url=<encoded>` | Query `url` (or legacy `target` alias) | `image/svg+xml` (`#0b0e14` on `#ffffff`, 4-module quiet zone) | Generates offline ISO/IEC 18004 Versions 1–10 ECC Level M QR SVG via `Foundation.Core.QrCodeEncoder.RenderSvg`; returns `400 QR_PAYLOAD_INVALID` on oversize payload. |

### 2.2 Reverse-Proxy Gateway Routes (`/api/v1/apps/<app-id>/*`, `/app/<app-id>`, `/app/<app-id>/*`)
- **`/app/{appId}` (No Trailing Slash):** Returns **`307 Temporary Redirect`** to `/app/{appId}/` preserving query parameters.
- **`effectivePort <= 0` Guard (`ColdWakeProxyMiddleware`):** Headless/CLI apps without an HTTP endpoint (`effectivePort <= 0`) immediately return HTTP **`503 Service Unavailable`** JSON `{ "errorCode": "NO_HTTP_ENDPOINT", "message": "...", "errorMessage": "..." }` instead of dialing invalid port `0`.
- **`/app/{appId}/{**catchAll}` & `/api/v1/apps/{appId}/{**catchAll}`:** Wakes `cold` workers when permitted, injects **`X-Forwarded-Prefix: /app/{appId}`**, refreshes idle activity tracking, and proxies HTTP/SSE/MCP traffic to `http://127.0.0.1:<effectivePort>/...`.

---

## 3. Server-Sent Events (`/api/v1/suite/events`) & UI Deep Links

| Event Name (`event:`) | JSON Payload (`data:`) | Trigger Condition |
| :--- | :--- | :--- |
| `heartbeat` | `{ "tsUtc": "2026-11-19T08:00:00.000Z", "uptimeSeconds": 120 }` | Initial connect and 10s heartbeat. |
| `app_status` | `SisterAppRuntimeDto` (`appId`, `state`, `residency`, `isOnline`, `activeJobs`) | App starts, wakes, hibernates, changes state, or trips breaker. |
| `lock_changed` | `{ "locks": [...] }` | `.hklock` acquisition, release, or stale purge. |
| `conflict_changed` | `CoexistenceStatusDto` | Coexistence status recomputation. |
| `power_countdown` | `PowerStatusDto` | Post-run sleep/shutdown countdown updates. |
| `log_line` | `{ "seq": 1, "timestampUtc": "...", "level": "INFO", "source": "...", "message": "..." }` | Live log stream to `Ctrl+\`` terminal drawer. |
| `alert_raised` | `NotificationHistoryRecord` | New entry recorded in `notification_history`. |

- **UI Deep-Linking Query Parameters:** `/?tab=<appId>` selects an outer dynamic application tab (also accepts internal desktop `route:<section>`); `/?route=fleet|traffic-cop|doctor|settings` opens that navigation section directly on load.

---

## 4. CLI Flags, `MotherLauncherArgsHelper` & Deterministic Exit Codes

```powershell
Mother.exe [--suite-tab <app-id>] [--window <app-id>] [--browser [<app-id>]] [--tray-only | --minimized]
Mother.exe --data-dir "<dir>" --log-dir "<dir>" [--config "<file>"] [--programs-dir "<dir>"] [--port 5100] [--diagnose]
Mother-Setup-v0.3.0-win-x64.exe [--install-dir <dir>] [--suite-dir <dir>] [--profile <lightweight|full|custom>] [--migrate-legacy] [--autostart] [--silent] [--quiet] [--verify] [--uninstall [--purge-data]] [--dry-run] [--data-dir <dir>] [--log-dir <dir>]
```

- **Launcher Wrapper (`MotherLauncherArgsHelper`):** Rebases Foundation relative paths plus `--programs-dir` against caller CWD before spawning `app\Mother.App.exe`, and sync-waits on Foundation CLI flags plus `--diagnose`, `--automation-port`, and `--automation-token` while launching `--suite-tab`, `--window`, `--browser`, and `--programs-dir` asynchronously.

| Exit Code | Meaning | Trigger Condition |
| :---: | :--- | :--- |
| **`0`** | **Success** | Clean exit, `--version`/`--help`, `--diagnose` `pass`/`warn`, or installer completion. |
| **`1`** | **Runtime / Unhandled Exception** | Fatal error (writes `logs\crashes\crash_*.json` where configured) or `--diagnose` `fail`. |
| **`2`** | **Validation / Release Guard Failure** | Invalid CLI flag OR passing `--automation-port`/`--automation-token` to plain Release `Mother.exe` / `Mother.App.exe` / `Mother-Setup`. |
| **`3`** | **Sandbox / Isolated Containment Violation** | Stage 1 `--data-dir` / `APP_ISOLATED_TEST=1` escape OR Stage 2 `FOUNDATION_SANDBOX_ROOT` boundary violation (`SandboxEnvironmentSeam.ContainmentExitCode`). |
| **`130`** | **Clean `SIGINT` / `Ctrl+C` Abort** | Console interrupt cleanly terminated child wait. |

---

## 5. File Contracts (`settings.json` v2, `suite.manifest.json` v2, `installer.manifest.json` v2, `.coexistence.json` v2)

### 5.1 Suite Settings (`data\config\settings.json`, `schemaVersion: 2`)
Standard 18 canonical keys managed by `MotherSettings`, `SettingsMigrationService`, and `SettingsStore`:
- `schemaVersion`: `2`
- `useGlobalFormatRules`: `true` (UTC millisecond `Z` timestamps + tabular numbers)
- `port`: `5100`, `bindHost`: `"127.0.0.1"`
- `shouldAllowTailscaleBind`: `false` *(migrates v1 `allowTailscaleBind`)*
- `idleTimeoutMinutes`: `15` *(migrates v1 `coldWakeIdleMinutes`)*
- `healthPollIntervalMs`: `5000`
- `shouldInhibitSleepDuringActiveLock`: `true` *(migrates v1 `sleepInhibitDuringActiveLock`)*
- `postRunPowerPolicy`: `"StayAwake"` | `"Sleep"` | `"Shutdown"`, `postRunIdleSeconds`: `60`, `postRunCountdownSeconds`: `60`
- `shouldAutoAlignSisterApps`: `false` *(migrates v1 `autoAlignWithSisterApps`)*
- `shouldShowDesktopToasts`: `true` *(migrates v1 `desktopToastNotifications`)*
- `shouldSendDiscordWebhooks`: `false` *(migrates v1 `discordWebhookNotifications`)*
- `customManifestRoots`, `targetDrives`, `residencyOverrides`, plus `[JsonExtensionData]` forward-compatible custom properties.

### 5.2 Declarative `installer.manifest.json` (`schemaVersion: 2`, Embedded)
Embedded inside `Mother.Installer` (`Mother.Installer.Embedded.installer.manifest.json`) for `Foundation.Installer.InstallerManifestStore`: `appId: "mother"`, `manifestType: "installer-manifest"`, `version: "0.3.0"`, `studioName: "StreamItAll"`, `suiteName: "Mother"`, `umbrellaSubpath: "StreamItAll\\Mother\\Mother"`, `mainExecutableRelativePath: "Mother.exe"`, `allowAutoStart: true`, `allowLegacyMigration: true`, `supportedProfiles: ["lightweight", "full", "custom"]`, `defaultProfile: "full"`, secondary picker `--suite-dir`, and Titanium Sky `#38BDF8` theme.

### 5.3 Universal `suite.manifest.json` (`v2` & `v1`), `.coexistence.json` (`v2`) & `.hklock`
- **`suite.manifest.json` (`v2`):** Declares `appId`, `name`, `version`, `sdkVersion: "0.5.1"`, `internalPort` (`<= 0` for non-HTTP CLI apps), `executable`, `commands.startDaemon`, `workerArgs`, `residency` (`warm`/`cold`/`disabled`, aliases `always-on`/`on-demand`), `sharedMounts`, `mutuallyExclusiveWith`, `targetDrives`, and `schedules[]`.
- **`<SharedRoot>\.coexistence.json` (`v2`):** Written by `FleetCoexistenceArbitrator.ApplyRecommendations` with fields `schemaVersion` (`2`), `rootPath`, `updatedAtUtc`, `appliedPreset` (`"zero-rewrite-parity"` fallback, or `"suite-safe-stagger"` from UI/REST), `settingsDrift` (`false`), and `targetAppIds` (normalized lowercase distinct array of dynamically discovered `appId` values; zero hardcoded app entries).
- **`.hklock` (`<TargetDir>\.<app-id>_<pid>.hklock`):** `PidSafeLockEnvelope` (`schemaVersion: 1`, `acquiredAtUtc` + `createdAtUtc` fallback).

---

## 6. SQLite / JSON / DTO Schema Reference (`data\mother.db`, `PRAGMA user_version = 1`)

Defined in `Dev\migrations\0001_initial_schema.sql` and lockstep-tested against `wwwroot\js\api\contracts.js` (`MotherDbAndWireLockstepTests`):
- **`fleet_health_events`:** `id`, `suiteId`, `appId`, `status`, `residencyMode`, `pid`, `processStartTimeUtc`, `exitCode`, `retryCount`, `isOnline`, `hasActiveConflicts`, `errorCode`, `errorMessage`, `errorRemedy`, `metadataJson`, `createdAtUtc`.
- **`schedule_deferrals`:** `id`, `suiteId`, `appId`, `jobId`, `driveMount`, `durationMs`, `description`, `status`, `isDryRun`, `nextRunAtUtc`, `metadataJson`, `createdAtUtc`.
- **`notification_history`:** `id`, `suiteId`, `appId`, `slug`, `title`, `description`, `status`, `retryCount`, `isDryRun`, `errorCode`, `errorMessage`, `metadataJson`, `createdAtUtc`, `completedAtUtc`.

---

## 7. Compatibility & Clean-Room Guarantees

1. Full SemVer (`v0.3.0`, `sdkVersion: "0.5.1"`) enables deterministic `minSuiteVersion` checks across suite apps.
2. Zero-to-many app flexibility (`0..N`) works cleanly without pre-configured sister manifests.
3. 7-file `Releases\v0.3.0\` & NTFS-hardlinked `Latest\` bundles contain zero PII/workstation paths, zero `Foundation.Automation`, lowercase `SHA256SUMS.txt`, and both 4-pillar `Mother-Setup-v0.3.0-win-x64.exe` and docs-complete `Mother-v0.3.0-win-x64-Portable.zip`.
