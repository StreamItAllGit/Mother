# Mother — Single-API Gateway Reference, Manifests & File Contracts (`INTERFACES.md`)

> **Active Baseline Version:** `v0.2.0` (`MPL-2.0`) | **Full SemVer (`MAJOR.MINOR.PATCH`)** | **SDK Pin:** `Foundation v0.4.0`

This reference manual documents the authentication and transport boundaries, the Single Public API Gateway (`/api/v1/suite/*`, `/api/v1/scheduler/defer`, `/api/v1/apps/<app-id>/*`, and `/app/<app-id>/` on `:5100` / `:15100`), real-time SSE streams, MCP proxy contracts, CLI subcommands and deterministic exit codes, file contracts (`suite.manifest.json` `v2`, `mother.manifest.json` `v1`, `.coexistence.json`, `.hklock`, `.hash`), SQLite/DTO schemas, and sister-app compatibility rules for **`Mother`** ([`<WORKSPACE_ROOT>/Mother\`](file:///<WORKSPACE_ROOT>/Mother/)).

---

## 1. Authentication & Transport Security

1. **Single Public Gateway (`:5100` HTTP / `:8500` Tailscale Serve HTTPS):**
   - `Mother` is the sole network listener exposed to workstation/LAN (`http://127.0.0.1:5100/`) and Tailscale tailnet (`https://<tailnet>:8500/`).
   - All 6 supervised sister daemons (`Housekeeping :5178`, `ShadowLibrary :5179`, `Redroom :5180`, `Greenroom :8420`, `StreamEngine :8430`, `legacy-sister-app :8460`) bind strictly to `127.0.0.1` loopback behind `Mother`'s reverse proxy (`--managed-by-mother --bind 127.0.0.1 --port <effectivePort>`).
2. **DPAPI Secret Storage at Rest:**
   - Outbound Discord webhook URLs (`discord_webhook_url`) and optional gateway tokens are encrypted inside `<DataDir>\secrets.dpapi` (`%LOCALAPPDATA%\Programs\StreamItAll\Mother\Mother\data\secrets.dpapi` in live installs) via `Foundation.Core.DpapiSecretStore` (`DataProtectionScope.CurrentUser`).
3. **Scoped Mobile PWA & Tailscale HTTPS Pairing:**
   - `Mother` serves the root Suite PWA manifest (`/manifest.webmanifest`, `scope: "/"`) and proxies per-app scoped PWAs (`/app/<app-id>/manifest.webmanifest`, `scope: "/app/<app-id>/"`) paired via offline SVG QR code.

---

## 2. REST API Reference `/api/v1/*`

### 2.1 Suite-Level Gateway & Scheduler Endpoints (`:5100` / Sandbox `:15100`)

| HTTP Method & Route | Request Body (`camelCase`) | Response Payload (`camelCase` JSON) | Behavior & Description |
| :--- | :--- | :--- | :--- |
| `GET /api/v1/suite/health` *(Alias: `/api/v1/health`)* | — | `{ "status": "ok", "suiteId": "mother", "version": "0.2.0", "sdkVersion": "0.4.0", "pid": 4120, "uptimeSeconds": 120, "testMode": false, "sqliteUserVersion": 1, "registeredApps": 6, "warmApps": 3, "coldApps": 3, "disabledApps": 0 }` | Suite daemon liveness, version, SDK pin (`0.4.0`), SQLite schema version, and residency summary. |
| `GET /api/v1/suite/manifest` *(Alias: `/api/v1/manifest`)* | — | `{ "schemaVersion": 2, "appId": "mother", "name": "Mother", "version": "0.2.0", "sdkVersion": "0.4.0", "spdxLicense": "MPL-2.0", "brandAccent": "#38bdf8", "port": 5100, "testMode": false, "capabilities": [...], "registeredAppIds": [...] }` | Exposes Mother's suite capability manifest and registered app IDs. |
| `GET /api/v1/suite/apps` | — | `{ "suiteId": "mother", "testMode": false, "apps": [{ "appId": "housekeeping", "name": "HouseKeeping", "version": "1.0.0", "brandAccent": "#f59e0b", "effectivePort": 5178, "residency": "warm", "state": "stopped", "isOnline": false, "activeJobs": 0, "queuedJobs": 0 }] }` | Lists all installed sister apps discovered across `manifests\*.json`, umbrella siblings, and HKCU metadata. |
| `POST /api/v1/suite/apps/reload` | `{}` | `{ "reloadedCount": 6, "apps": [...] }` | Hot-reloads `manifests\*.json` and `StreamItAll\Mother\*` installs without restarting running daemons. |
| `POST /api/v1/suite/apps/{appId}/residency` | `{ "residencyMode": "warm\|cold\|disabled" }` | `{ "app": {...} }` | Updates per-app residency in `data\settings.json` and reconciles child worker lifecycle. |
| `POST /api/v1/suite/apps/{appId}/wake` | `{}` | `{ "outcome": {...}, "app": {...} }` | Arbitrates coexistence and wakes a `cold` sister app (`409` on conflict, `503` on startup timeout). |
| `POST /api/v1/suite/apps/{appId}/stop` | `{}` | `{ "appId": "<appId>", "stopped": true, "app": {...} }` | Gracefully stops (`5s` drain) a specific child worker. |
| `POST /api/v1/suite/jobs/stop-all` | `{}` | `{ "stoppedApps": [...], "power": {...} }` | Stops all running child jobs/workers and cancels any active power countdown. |
| `GET /api/v1/scheduler/defer` | `?limit=50` | `{ "schedules": [...], "deferrals": [...] }` | Returns active `FleetScheduleEvaluator` states (`scheduler-state.json`) and recent SQLite deferrals. |
| `POST /api/v1/scheduler/defer` | `{ "appId": "greenroom", "scheduleId": "plex-sync", "driveMount": "D:\\", "deferMinutes": 20, "deferReason": "Disk busy", "blockingAppId": "housekeeping" }` | `{ "appId": "greenroom", "scheduleId": "plex-sync", "deferMinutes": 20, "deferReason": "Disk busy", "nextRunAtUtc": "...", "schedule": {...} }` | Records a schedule deferral (`schedule_deferrals` + `scheduler-state.json`), shifting `nextRunAtUtc` cleanly (also tolerates legacy `jobId`/`minutes`/`reason`). |
| `GET/POST /api/v1/suite/scheduler/traffic-cop` | `{ "appId": "...", "jobId": "...", "driveMount": "...", "minutes": 30 }` | `{ "activeLocks": [...], "recentDeferrals": [...] }` | Returns live/stale `.hklock` holds and recent deferrals (`POST` records a legacy deferral entry). |
| `GET /api/v1/suite/traffic-cop/locks` & `POST .../purge-stale` | `{}` | `{ "locks": [...], "purgedCount": 1 }` | Lists scanned `.hklock` sentinels or safely purges dead-PID `stale` sentinels. |
| `GET /api/v1/suite/coexistence/status` *(Alias: `/api/v1/coexistence/status`)* | — | `{ "autoAlignWithSisterApps": false, "sharedRoots": [...], "conflicts": [...], "apps": [...] }` | Aggregates coexistence status, shared roots, and conflicts (`.../conflicts`, `.../deferrals`, `.../arbitrate` also exposed). |
| `POST /api/v1/suite/coexistence/apply-recommendations` | `{ "targetAppIds": [...], "presetId": "suite-safe-stagger", "sharedRoots": [...] }` | `{ "appliedPreset": "...", "updatedApps": [...], "status": {...} }` | Applies non-conflicting presets and persists `<SharedRoot>\.coexistence.json`. |
| `GET /api/v1/suite/power/status`, `POST .../action`, `POST .../cancel-countdown` | `{ "policy": "StayAwake\|Sleep\|Shutdown" }` / `{ "action": "cancel\|evaluate" }` | `{ "policy": "...", "fleetBusy": false, "sleepInhibited": false, "watchdogRegistered": true, "countdownActive": false, "secondsRemaining": 0 }` | Returns or updates Post-Run Power Coordinator state and `\Mother_Watchdog` status. |
| `GET /api/v1/suite/doctor` & `POST .../run` | `{}` | `{ "overall": "pass\|warn\|fail", "suiteChecks": [...], "crashBundleCount": 0 }` | Runs `Fleet System Doctor` checks (`.../repair`, `.../backup-dbs`, `.../export-bundle` also exposed). |
| `GET /api/v1/suite/notifications` & `POST .../test` | `{ "appId": "...", "slug": "...", "title": "...", "description": "..." }` | `{ "notifications": [...] }` / `{ "notification": {...} }` | Queries persisted alert history (`notification_history`) or sends a test alert. |
| `GET /api/v1/suite/secrets/metadata` | — | `{ "keys": [{ "keyName": "...", "valueByteLength": 64, "updatedAtUtc": "..." }] }` | Returns DPAPI secret key names and lengths only (never decrypted plaintext). |
| `GET/PUT /api/v1/suite/settings`, `.../snapshots`, `.../rollback`, `.../export` | `MotherSettings` / `{ "snapshotId": "..." }` | `{ "settings": {...}, "snapshots": [...] }` | Reads/saves `data\settings.json` (`targetDrives` + 20-snapshot rollback ring buffer & JSON export). |
| `GET /api/v1/terminal/history`, `POST .../exec`, `POST .../clear`, `GET .../stream` | `{ "command": "<whitelisted command>" }` | `{ "accepted": true, "output": "..." }` | Powers `foundation-terminal` via `TerminalCommandDispatcher`: `help`, `status`, `health`, `apps`, `traffic`, `locks`, `coexistence`, `doctor`, `logs [1..100]`, `purge-stale`, `reload`, `wake <appId>`, `stop <appId>`, `power [StayAwake|Sleep|Shutdown]` (invalid power policy fails cleanly with `accepted: false`). |
| `GET /api/v1/suite/qr.svg` | — | `image/svg+xml` | Generates an offline SVG QR code for Tailscale mobile pairing. |

### 2.2 Reverse-Proxy Gateway Routes (`/api/v1/apps/<app-id>/*`, `/app/<app-id>`, `/app/<app-id>/*`)
- **`/app/{appId}` (No Trailing Slash):** Returns **`307 Temporary Redirect`** to `/app/{appId}/` preserving query string parameters (`preserveMethod: true`).
- **`/app/{appId}/{**catchAll}`:** Wakes `cold` child workers if permitted by coexistence, injects **`X-Forwarded-Prefix: /app/{appId}`**, updates `LastActivityUtc` (for idle hibernation tracking), and proxies upstream to `http://127.0.0.1:<effectivePort>/...`.
- **`/api/v1/apps/{appId}/{**catchAll}`:** Reverse-proxies standardized child sister routes: `GET /api/v1/health`, `/api/v1/status`, `/api/v1/telemetry`, `/api/v1/scheduler/status`, `POST /api/v1/scheduler/defer`, `POST /api/v1/jobs/stop-all`, `GET /api/v1/coexistence/status`, `POST /api/v1/coexistence/apply-recommendations`, `POST /api/v1/system/doctor/run`, and `GET /api/v1/system/drives`.

---

## 3. Server-Sent Events (`/api/v1/suite/events`) & MCP Proxy

| Event Name (`event:`) | JSON Payload (`data:`) | Trigger Condition |
| :--- | :--- | :--- |
| `heartbeat` | `{ "tsUtc": "2026-11-15T12:00:00.000Z", "uptimeSeconds": 120 }` | Initial connect and 10-second liveness heartbeat. |
| `app_status` | `SisterAppRuntimeDto` (`appId`, `state`, `residency`, `isOnline`, `activeJobs`) | Emitted whenever a child app starts, wakes, hibernates, changes state, or trips the circuit breaker. |
| `lock_changed` | `{ "locks": [...] }` | Emitted when `.hklock` scans detect changes or stale locks are purged. |
| `conflict_changed` | `CoexistenceStatusDto` | Emitted when coexistence conflict status updates. |
| `power_countdown` | `PowerStatusDto` (`policy`, `countdownActive`, `secondsRemaining`) | Emitted during the 60-second cancellable post-run power countdown. |
| `log_line` | `{ "seq": 1, "timestampUtc": "2026-11-15T12:00:00.000Z", "level": "INFO", "source": "...", "message": "[HEARTBEAT] ..." }` | Streams real-time lines into the `Ctrl+\`` `foundation-terminal` drawer. |
| `alert_raised` | `NotificationHistoryRecord` | Emitted when `NotificationDispatcher` records a fleet alert. |

- **MCP HTTP/SSE Proxy (`/api/v1/apps/<app-id>/mcp`):** Reverse-proxies HTTP/SSE MCP traffic to supervised child workers (waking `cold` apps if permitted).

---

## 4. CLI Subcommands & Deterministic Exit Codes

```powershell
Mother.exe [--suite-tab <app-id>] [--window <app-id>] [--browser [<app-id>]] [--tray-only | --minimized]
Mother.exe --test-mode --headless --data-dir "<WORKSPACE_ROOT>/Mother\Sandbox\data" --log-dir "<WORKSPACE_ROOT>/Mother\Sandbox\logs" [--port 15100] [--diagnose]
Mother-Setup-v0.2.0-win-x64.exe [--install-dir <dir>] [--suite-dir <dir>] [--profile <lightweight|full>] [--migrate-legacy] [--autostart] [--silent] [--headless] [--verify] [--uninstall [--purge-data]] [--test-mode] [--dry-run] [--visible-test]
```

| Exit Code | Meaning | Trigger Condition |
| :---: | :--- | :--- |
| **`0`** | **Success** | Clean user shutdown, successful `--test-mode --headless` self-test, or completed installer run. |
| **`1`** | **Runtime / Unhandled Exception** | Fatal startup or runtime exception (after writing `logs\crashes\crash_*.json`). |
| **`2`** | **Validation / Doctor Failure** | Invalid CLI flag, corrupt manifest in `--headless` self-test, or failed `Fleet System Doctor` check. |
| **`3`** | **Fail-Closed Sandbox Violation** | Attempted access outside allowed `Sandbox\` hierarchy while `--test-mode` is active. |
| **`130`** | **Clean `SIGINT` / `Ctrl+C` Abort** | Graceful console interrupt (`5s` child drain completed). |

---

## 5. File Contracts — `suite.manifest.json` (`v2`), `v1` Tolerance, `.coexistence.json`, `.hklock` & `.hash`

### 5.1 Universal `suite.manifest.json` (`schemaVersion: 2`)
Placed at `<AppDir>\app\suite.manifest.json` and registered in `manifests\<app-id>.manifest.json`:
```json
{
  "$schema": "https://schemas.local/suite.manifest.v2.json",
  "schemaVersion": 2,
  "suiteId": "mother",
  "suiteName": "Mother",
  "minSuiteVersion": "0.1.0",
  "sdkVersion": "0.4.0",
  "suitePort": 5100,
  "appId": "greenroom",
  "name": "Greenroom",
  "shortName": "Greenroom",
  "version": "0.2.0",
  "spdxLicense": "MIT",
  "brandAccent": "#10b981",
  "executable": "app\\greenroom\\cli.py",
  "commands": {
    "startDaemon": "python -m greenroom companion --quiet",
    "statusCheck": "python -m greenroom status"
  },
  "workerArgs": ["--managed-by-suite", "--suite-id", "mother", "--bind", "127.0.0.1", "--port", "8420"],
  "internalPort": 8420,
  "sandboxPort": 15420,
  "gatewayBasePath": "/app/greenroom",
  "apiBasePath": "/api/v1/apps/greenroom",
  "residency": {
    "defaultMode": "cold",
    "allowedModes": ["warm", "cold", "disabled"],
    "idleTimeoutMinutes": 15,
    "wakeOnApiRequest": true,
    "wakeOnUiNavigation": true,
    "wakeOnSchedule": true
  },
  "schedules": [
    {
      "scheduleId": "plex-sync",
      "name": "Nightly Plex Sync",
      "cadence": "daily",
      "timeUtc": "04:15",
      "runIfMissed": true,
      "targetDrives": ["D:\\"],
      "triggerEndpoint": "/api/v1/plex/sync"
    }
  ]
}
```
- **Mother Settings `targetDrives` Relocation (`v0.2.0`):** `Mother`'s own `Dev\suite.manifest.json` omits `targetDrives`; operator-configured `targetDrives` live in `data\settings.json` (`MotherSettings.TargetDrives`, seeded by `Dev\config\settings.example.json`). Child sister manifests may still declare per-job `schedules[].targetDrives` or `targetDrives`.
- **Residency & Legacy `v1` Tolerance:** `ManifestParser` maps `on-demand` -> `cold` and `always-on` -> `warm`, accepts top-level or nested `idleTimeoutMinutes`/`wakeOnApiRequest`/`wakeOnUiNavigation`, and normalizes legacy `v1` `snake_case` keys (`app_id`, `display_name`, `brand_color`, `health_endpoint`).

### 5.2 Shared Root `.coexistence.json`, `.hklock` & `.hash`
- **`<SharedRoot>\.coexistence.json`:** `schemaVersion: 1`, `rootPath`, `updatedAtUtc`, `registeredApps` (`archiveFormat`, `verifySidecarHash`, `zeroRewriteCoAudit`, `respectSisterHashSidecars`).
- **`.hklock` (`<TargetDir>\.<app-id>_<pid>.hklock`):** `PidSafeLockEnvelope` (`schemaVersion: 1`, `appId`, `pid`, `processStartTimeUtc`, `hostName`, `operation`, `acquiredAtUtc`, `heartbeatAtUtc`, `expiresAtUtc`), supporting `acquiredAtUtc` and `createdAtUtc` fallback.
- **`.hash` Sidecar:** `# Format: Universal Sister-App Hash Sidecar v1` + `# Container: size=... crc32=... sha256=... mtimeUtc=...`.

---

## 6. SQLite / JSON / DTO Schema Reference (`data\mother.db`, `PRAGMA user_version = 1`)

Defined in `Dev\migrations\0001_initial_schema.sql`, mapped 1:1 to `camelCase` JSON DTOs (`MotherDbAndWireLockstepTests` + `wwwroot\js\api\contracts.js`):

### 6.1 Table `fleet_health_events`
| SQL Column (`snake_case`) | JSON Key (`camelCase`) | SQLite Type | Description |
| :--- | :--- | :--- | :--- |
| `id`, `suite_id`, `app_id` | `id`, `suiteId`, `appId` | `TEXT NOT NULL` | Event ID, `"mother"`, sister app slug. |
| `status`, `residency_mode` | `status`, `residencyMode` | `TEXT NOT NULL` | Lifecycle status + `"warm"`, `"cold"`, or `"disabled"`. |
| `pid`, `process_start_time_utc`, `exit_code`, `retry_count` | `pid`, `processStartTimeUtc`, `exitCode`, `retryCount` | `INTEGER` / `TEXT` | Process identity, exit code, circuit-breaker retries. |
| `is_online`, `has_active_conflicts` | `isOnline`, `hasActiveConflicts` | `INTEGER CHECK IN (0, 1)` | Boolean online and conflict indicators. |
| `error_code`, `error_message`, `error_remedy` | `errorCode`, `errorMessage`, `errorRemedy` | `TEXT` | Structured error details (optional). |
| `metadata_json`, `created_at_utc` | `metadataJson`, `createdAtUtc` | `TEXT NOT NULL` | Validated JSON metadata (`json_valid`) & ISO-8601 UTC timestamp. |

### 6.2 Table `schedule_deferrals`
| SQL Column (`snake_case`) | JSON Key (`camelCase`) | SQLite Type | Description |
| :--- | :--- | :--- | :--- |
| `id`, `suite_id`, `app_id`, `job_id` | `id`, `suiteId`, `appId`, `jobId` | `TEXT NOT NULL` | Deferral ID, `"mother"`, deferred app & schedule ID. |
| `drive_mount`, `duration_ms`, `description` | `driveMount`, `durationMs`, `description` | `TEXT` / `INTEGER` | Contended mount, deferral duration (ms), arbitration reason. |
| `status`, `is_dry_run`, `next_run_at_utc` | `status`, `isDryRun`, `nextRunAtUtc` | `TEXT` / `INTEGER` | `"deferred"` / `"resolved"`, dry-run bit, deferred target UTC timestamp. |
| `metadata_json`, `created_at_utc` | `metadataJson`, `createdAtUtc` | `TEXT NOT NULL` | Validated JSON (`blockingAppId`, `lockFilePath`) & UTC creation timestamp. |

### 6.3 Table `notification_history`
| SQL Column (`snake_case`) | JSON Key (`camelCase`) | SQLite Type | Description |
| :--- | :--- | :--- | :--- |
| `id`, `suite_id`, `app_id`, `slug`, `title`, `description` | `id`, `suiteId`, `appId`, `slug`, `title`, `description` | `TEXT` | Alert row ID, source app, slug, title, redacted summary. |
| `status`, `retry_count`, `is_dry_run` | `status`, `retryCount`, `isDryRun` | `TEXT` / `INTEGER` | `"sent"` / `"simulated"` / `"failed"`, retry count, dry-run bit. |
| `error_code`, `error_message`, `metadata_json`, `created_at_utc`, `completed_at_utc` | `errorCode`, `errorMessage`, `metadataJson`, `createdAtUtc`, `completedAtUtc` | `TEXT` | Optional error fields, validated JSON metadata, UTC start & completion timestamps. |

---

## 7. Backward Compatibility Notes

1. Full 3-part SemVer (`v0.2.0`, `sdkVersion: "0.4.0"`) lets sister apps compare `minSuiteVersion` reliably.
2. Partial-suite installations (0..6 apps) work cleanly without missing-app errors, and non-sandbox discovery falls back read-only to legacy `<PROGRAMS_ROOT>/` paths when needed.
