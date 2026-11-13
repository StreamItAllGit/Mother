# Mother — Single-API Gateway Reference, Manifests & File Contracts (`INTERFACES.md`)

> **Active Baseline Version:** `v0.1.0` | **Full SemVer (`MAJOR.MINOR.PATCH`)** | **Owner:** `@devv` & `@tessa` ("The Manual Maker") | **Verified By:** `@dean` ([Standard 16 §3.5](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/16_PROJECT_DOSSIERS_AND_FEATURE_TRACKING.md))

This reference manual documents the authentication and transport boundaries, the Single Public API Gateway (`/api/v1/suite/*` and `/api/v1/apps/<app-id>/*` on `:5100`), real-time SignalR/SSE streams, MCP/Agent proxy contracts, CLI subcommands and exit codes, file contracts (`suite.manifest.json` `v2`, `mother.manifest.json` `v1`, `.coexistence.json`, `.hklock`, `.hash`), SQLite/DTO schemas ([Standard 18](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/18_DATA_DICTIONARY_AND_SCHEMA_LOOKUP.md)), and sister-app compatibility rules for **`Mother`** ([`<WORKSPACE_ROOT>/Mother\`](file:///<WORKSPACE_ROOT>/Mother/)).

---

## 1. Authentication & Transport Security

1. **Single Public Gateway (`:5100` HTTP / `:8500` Tailscale Serve HTTPS — [Standard 17 §1.1, §5.1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md#L242-L263)):**
   - `Mother` is the **sole** network listener exposed to the local workstation/LAN (`http://127.0.0.1:5100/`) and Tailscale tailnet (`https://<tailnet>:8500/`).
   - All 6 supervised sister daemons (`Housekeeping :5178`, `ShadowLibrary :5179`, `Redroom :5180`, `Greenroom :8420`, `StreamEngine :8430`, `legacy-sister-app :8460`) bind strictly to `127.0.0.1` loopback behind `Mother`'s reverse proxy (`--managed-by-mother --bind 127.0.0.1`).
2. **DPAPI Secret Storage at Rest:**
   - Outbound Discord webhook URLs (`discord_webhook_url`) and optional gateway tokens are encrypted inside `<PROGRAMS_ROOT>/Mother\data\secrets.dpapi` via `Foundation.Core.DpapiSecretStore` (`DataProtectionScope.CurrentUser`).
3. **Scoped Mobile PWA & Tailscale HTTPS Pairing:**
   - `Mother` serves the root Suite PWA manifest (`/manifest.webmanifest`, `scope: "/"`) and proxies per-app scoped PWAs (`/app/<app-id>/manifest.webmanifest`, `scope: "/app/<app-id>/"`) over Tailscale Serve HTTPS (`:8500`) paired via an offline SVG QR code modal.

---

## 2. REST API Reference `/api/v1/*` & Public C#/TS SDK Interfaces

### 2.1 Suite-Level Gateway Endpoints (`/api/v1/suite/*` & `/api/v1/terminal/*` on Port `:5100` / Sandbox `:15100`)
Per [Standard 17 §5.1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md#L248-L260) and [`v0.1.0.2026-11-12.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-12.md):

| HTTP Method & Route | Request Body (`camelCase`) | Response Payload (`camelCase` JSON) | Behavior & Description |
| :--- | :--- | :--- | :--- |
| `GET /api/v1/suite/health` *(Alias: `/api/v1/health`)* | — | `{ "status": "ok", "suiteId": "mother", "version": "0.1.0", "sdkVersion": "0.3.0", "pid": 4120, "uptimeSeconds": 120, "testMode": false, "sqliteUserVersion": 1, "registeredApps": 6, "warmApps": 3, "coldApps": 3, "disabledApps": 0 }` | Suite daemon liveness, version, SQLite schema version, and residency summary. |
| `GET /api/v1/suite/manifest` *(Alias: `/api/v1/manifest`)* | — | `{ "schemaVersion": 2, "appId": "mother", "name": "Mother", "version": "0.1.0", "sdkVersion": "0.3.0", "brandAccent": "#38bdf8", "port": 5100, "testMode": false, "capabilities": [...], "registeredAppIds": [...] }` | Exposes Mother's suite capability manifest and registered app IDs. |
| `GET /api/v1/suite/apps` | — | `{ "suiteId": "mother", "testMode": false, "apps": [{ "appId": "housekeeping", "name": "HouseKeeping", "version": "1.0.0", "brandAccent": "#f59e0b", "effectivePort": 5178, "residency": "warm", "state": "stopped", "isOnline": false, "activeJobs": 0, "queuedJobs": 0 }] }` | Lists all installed sister apps discovered in `manifests\*.json` with live status and job counts. |
| `POST /api/v1/suite/apps/reload` | `{}` | `{ "reloadedCount": 6, "apps": [...] }` | Hot-reloads `manifests\*.json` after an installer adds, upgrades, or removes a sister app. |
| `POST /api/v1/suite/apps/{appId}/residency` | `{ "residencyMode": "warm\|cold\|disabled" }` | `{ "app": {...} }` | Updates per-app residency in `data\settings.json` and reconciles child worker lifecycle. |
| `POST /api/v1/suite/apps/{appId}/wake` | `{}` | `{ "outcome": {...}, "app": {...} }` | Arbitrates coexistence and wakes a `cold` sister app (`409` on conflict, `503` on startup timeout). |
| `POST /api/v1/suite/apps/{appId}/stop` | `{}` | `{ "appId": "<appId>", "stopped": true, "app": {...} }` | Gracefully stops (`5s` drain) a specific child worker. |
| `POST /api/v1/suite/jobs/stop-all` | `{}` | `{ "stoppedApps": [...], "power": {...} }` | Stops all running child jobs/workers and cancels any active power countdown. |
| `GET /api/v1/suite/scheduler/traffic-cop` | — | `{ "activeLocks": [...], "recentDeferrals": [...] }` | Returns live/stale `.hklock` holds and recent schedule deferrals (`POST` also supported to record a deferral). |
| `GET /api/v1/suite/traffic-cop/locks` & `POST .../purge-stale` | `{}` | `{ "locks": [...], "purgedCount": 1 }` | Lists scanned `.hklock` sentinels or safely purges dead-PID `stale` sentinels. |
| `GET /api/v1/suite/coexistence/status` *(Alias: `/api/v1/coexistence/status`)* | — | `{ "autoAlignWithSisterApps": false, "sharedRoots": [...], "conflicts": [...], "apps": [...] }` | Aggregates coexistence status, shared roots, and conflicts (`.../conflicts`, `.../deferrals`, `.../arbitrate` also exposed). |
| `POST /api/v1/suite/coexistence/apply-recommendations` | `{ "targetAppIds": [...], "presetId": "suite-safe-stagger", "sharedRoots": [...] }` | `{ "appliedPreset": "...", "updatedApps": [...], "status": {...} }` | Applies non-conflicting presets and persists `<SharedRoot>\.coexistence.json`. |
| `GET /api/v1/suite/power/status` | — | `{ "policy": "StayAwake", "fleetBusy": false, "sleepInhibited": false, "watchdogRegistered": true, "countdownActive": false, "secondsRemaining": 0 }` | Returns Post-Run Power Coordinator and `\Mother_Watchdog` status. |
| `POST /api/v1/suite/power/action` & `POST .../cancel-countdown` | `{ "policy": "StayAwake\|Sleep\|Shutdown" }` or `{ "action": "cancel\|evaluate" }` | `{ "policy": "...", "countdownActive": ... }` | Sets power policy, evaluates fleet idle tick, or cancels active 60s countdown. |
| `GET /api/v1/suite/doctor` & `POST /api/v1/suite/doctor/run` | `{}` | `{ "overall": "pass\|warn\|fail", "suiteChecks": [...], "crashBundleCount": 0 }` | Runs `🩺 Fleet System Doctor` checks (`.../repair`, `.../backup-dbs`, `.../export-bundle` also exposed). |
| `GET /api/v1/suite/notifications` & `POST .../test` | `{ "appId": "...", "slug": "...", "title": "...", "description": "..." }` | `{ "notifications": [...] }` / `{ "notification": {...} }` | Queries persisted alert history (`notification_history`) or sends a test alert. |
| `GET /api/v1/suite/secrets/metadata` | — | `{ "keys": [{ "keyName": "...", "valueByteLength": 64, "updatedAtUtc": "..." }] }` | Returns DPAPI secret key names and lengths only (never decrypted plaintext). |
| `GET/PUT /api/v1/suite/settings`, `.../snapshots`, `.../rollback`, `.../export` | `MotherSettings` / `{ "snapshotId": "..." }` | `{ "settings": {...}, "snapshots": [...] }` | Reads/saves `data\settings.json` (`20`-snapshot rollback ring buffer & JSON export). |
| `GET /api/v1/terminal/history`, `POST .../exec`, `POST .../clear`, `GET .../stream` | `{ "command": "health\|apps\|doctor\|locks\|help" }` | `{ "lines": [...] }` / `{ "exitCode": 0, "output": "..." }` | Powers the docked `Ctrl+\`` `foundation-terminal` drawer and whitelisted diagnostics. |
| `GET /api/v1/suite/qr.svg` | — | `image/svg+xml` | Generates an offline SVG QR code for Tailscale mobile pairing. |

### 2.2 Reverse-Proxy Gateway Routes (`/api/v1/apps/<app-id>/*` & `/app/<app-id>/*`)
Every HTTP/WebSocket request arriving at `Mother` (`:5100`) under `/api/v1/apps/<app-id>/<subpath>` or `/app/<app-id>/<asset>` is reverse-proxied to `http://127.0.0.1:<internalPort>/api/v1/<subpath>` (automatically waking the child app first if its residency is `cold` and `wakeOnApiRequest` / `wakeOnUiNavigation` is `true`).

Each child sister app implements these standardized routes (polled by `Mother` and accessible through the proxy):
- `GET /api/v1/health` (Fallback: `/api/health`)
- `GET /api/v1/status` (Fallback: `/api/status`)
- `GET /api/v1/telemetry` (Fallback: `/api/telemetry`)
- `GET /api/v1/scheduler/status` (Fallback: `/api/scheduler/status`)
- `POST /api/v1/scheduler/defer` (Fallback: `/api/scheduler/defer`)
- `POST /api/v1/jobs/stop-all` (Fallback: `/api/jobs/stop-all`)
- `GET /api/v1/coexistence/status` (Fallback: `/api/coexistence/status`)
- `POST /api/v1/coexistence/apply-recommendations`
- `POST /api/v1/system/doctor/run`
- `GET /api/v1/system/drives`

---

## 3. SignalR / SSE / Event Bus Contracts

`Mother` exposes a unified Server-Sent Events (SSE) stream at `GET /api/v1/suite/events` powering the outer tab status dots, live views, and `Ctrl+\`` Unified Log Drawer:

| Event Name (`event:`) | JSON Payload (`data:`) | Trigger Condition |
| :--- | :--- | :--- |
| `heartbeat` | `{ "tsUtc": "2026-11-12T18:00:00.000Z", "uptimeSeconds": 120 }` | Initial connect and 10-second liveness heartbeat. |
| `app_status` | `SisterAppRuntimeDto` (`appId`, `state`, `residency`, `isOnline`, `activeJobs`) | Emitted whenever a child app starts, wakes, hibernates, changes state, or trips the circuit breaker. |
| `lock_changed` | `{ "locks": [...] }` | Emitted when `.hklock` scans detect changes or stale locks are purged. |
| `conflict_changed` | `CoexistenceStatusDto` | Emitted when coexistence conflict status updates. |
| `power_countdown` | `PowerStatusDto` (`policy`, `countdownActive`, `secondsRemaining`) | Emitted during the 60-second cancellable post-run power countdown. |
| `log_line` | `{ "seq": 1, "timestampUtc": "2026-11-12T18:00:00.000Z", "level": "INFO", "source": "...", "message": "[HEARTBEAT] ..." }` | Streams real-time lines into the `Ctrl+\`` `foundation-terminal` drawer. |
| `alert_raised` | `NotificationHistoryRecord` | Emitted when `NotificationDispatcher` records a fleet alert. |

---

## 4. MCP Server / Agent Contracts

Per [Standard 17 §7.3](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md#L398-L401):
1. **MCP HTTP/SSE Gateway Proxy (`/api/v1/apps/<app-id>/mcp`):**
   - `Mother` reverse-proxies HTTP/SSE Model Context Protocol requests at `/api/v1/apps/greenroom/mcp` (`:8420`) and `/api/v1/apps/shadowlibrary/mcp` (`:5179`), automatically waking the target child app if `cold`.
2. **Suite Inspection Endpoints for Autonomous Agents (`@inspecter`, `@nora`, `@tessa`):**
   - Agents query `GET http://127.0.0.1:5100/api/v1/suite/health`, `GET /api/v1/suite/apps`, and `GET /api/v1/suite/coexistence/status` to audit fleet readiness and cross-app parity in a single HTTP call.

---

## 5. CLI Subcommands & Exit Codes

### 5.1 CLI Invocation Patterns
```powershell
# 1. Start Mother in normal Single-Window Suite Shell mode (or bring existing window to front)
Mother.exe [--suite-tab <app-id>] [--tray-only | --minimized]

# 2. Pop out a specific sister app into its own standalone desktop window
Mother.exe --window housekeeping

# 3. Open Mother or a specific sister app in the default web browser
Mother.exe --browser greenroom

# 4. Run deterministic headless self-test in Sandbox (exits 0 on pass)
Mother.exe --test-mode --headless --data-dir "<WORKSPACE_ROOT>/Mother\Sandbox\data" --log-dir "<WORKSPACE_ROOT>/Mother\Sandbox\logs" [--port 15100] [--diagnose]

# 5. Installer Sandbox lifecycle (--test-mode + --install-dir auto-contains --data-dir to <dir> if omitted)
Mother-Setup-v0.1.0-win-x64.exe --test-mode --silent --install-dir "<WORKSPACE_ROOT>/Mother\Sandbox\installer-test"
```

### 5.2 Deterministic Exit-Code Taxonomy (`STD-03`)
| Exit Code | Meaning | Trigger Condition |
| :---: | :--- | :--- |
| **`0`** | **Success** | Clean user shutdown, successful `--test-mode --headless` self-test, or completed installer run. |
| **`1`** | **Runtime / Unhandled Exception** | Fatal startup or runtime exception (after writing `logs\crashes\crash_*.json`). |
| **`2`** | **Validation / Doctor Failure** | Invalid CLI flag, corrupt manifest in `--headless` self-test, or failed `🩺 Fleet System Doctor` check. |
| **`3`** | **Fail-Closed Sandbox Violation** | Attempted access to `<PROGRAMS_ROOT>/...` or any path outside `Sandbox\` while `--test-mode` is active. |
| **`130`** | **Clean `SIGINT` / `Ctrl+C` Abort** | Graceful console interrupt (`5s` child drain completed). |

---

## 6. File Contracts — `suite.manifest.json` (`v2`), `mother.manifest.json` (`v1`), `.coexistence.json`, `.hklock`, `.hash`

### 6.1 Universal `suite.manifest.json` (`schemaVersion: 2`, Strict `camelCase` — [Standard 17 §5.2](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md#L280-L356))
Placed at `<SisterApp>\app\suite.manifest.json` and registered at `<PROGRAMS_ROOT>/Mother\manifests\<app-id>.manifest.json`:
```json
{
  "$schema": "https://schemas.local/suite.manifest.v2.json",
  "schemaVersion": 2,
  "suiteId": "mother",
  "suiteName": "Mother",
  "minSuiteVersion": "0.1.0",
  "sdkVersion": "0.3.0",
  "suitePort": 5100,
  "appId": "housekeeping",
  "name": "HouseKeeping",
  "shortName": "HouseKeeping",
  "version": "1.0.0",
  "brandAccent": "#f59e0b",
  "installDir": "D:\\Programs\\Housekeeping",
  "executable": "app\\Housekeeping.Server.exe",
  "launcherExecutable": "Housekeeping.exe",
  "workerArgs": ["--managed-by-suite", "--suite-id", "mother", "--bind", "127.0.0.1", "--port", "5178"],
  "internalPort": 5178,
  "sandboxPort": 15178,
  "gatewayBasePath": "/app/housekeeping",
  "apiBasePath": "/api/v1/apps/housekeeping",
  "residency": {
    "defaultMode": "warm",
    "allowedModes": ["warm", "cold", "disabled"],
    "idleTimeoutMinutes": 15,
    "wakeOnApiRequest": true,
    "wakeOnUiNavigation": true,
    "wakeOnSchedule": true
  },
  "endpoints": {
    "health": "/api/v1/health",
    "status": "/api/v1/status",
    "telemetry": "/api/v1/telemetry",
    "schedulerStatus": "/api/v1/scheduler/status",
    "schedulerDefer": "/api/v1/scheduler/defer",
    "stopAll": "/api/v1/jobs/stop-all",
    "doctorRun": "/api/v1/system/doctor/run",
    "drives": "/api/v1/system/drives",
    "coexistenceStatus": "/api/v1/coexistence/status"
  },
  "targetDrives": ["C:", "D:", "E:"],
  "schedules": []
}
```

### 6.2 Dual-Schema Tolerance for Legacy `mother.manifest.json` (`v1` — [`1715` Report §5.1](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-03_1715.-.fleet-coexistence-broker-and-manifest-schema.md#L59-L60))
`ManifestRegistryService` also parses legacy `v1` manifests by normalizing:
- `appId`: `appId ?? id ?? app_id`
- `name`: `name ?? display_name`
- `brandAccent`: `brandAccent ?? brand_accent ?? brand_color`
- `internalPort`: `internalPort ?? port`
- Endpoints: top-level `camelCase` (`healthEndpoint`) $\rightarrow$ `snake_case` (`health_endpoint`) $\rightarrow$ nested `endpoints.*` $\rightarrow$ canonical `/api/v1/*` fallback on `internalPort`.

### 6.3 Shared Root Coexistence Manifest (`<SharedRoot>\.coexistence.json` — [Standard 17 §7.1, §7.4](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md#L385-L412))
```json
{
  "schemaVersion": 1,
  "rootPath": "E:\\Vault",
  "updatedAtUtc": "2026-11-12T18:00:00.000Z",
  "registeredApps": {
    "shadowlibrary": {
      "version": "0.1.25",
      "archiveFormat": "7z",
      "verifySidecarHash": true,
      "zeroRewriteCoAudit": true
    },
    "housekeeping": {
      "version": "1.0.0",
      "archiveFormat": "7z",
      "verifySidecarHash": true,
      "respectSisterHashSidecars": true
    }
  }
}
```

### 6.4 Distributed Directory Lock (`.hklock`) & Universal `.hash` Sidecar
- **`.hklock` (`<TargetDir>\.<app-id>_<pid>.hklock`):** Uses `Foundation.Core.PidSafeLockEnvelope` (`schemaVersion: 1`, `appId`, `pid`, `processStartTimeUtc`, `hostName`, `operation`, `acquiredAtUtc`, `heartbeatAtUtc`, `expiresAtUtc`). Inspected by `ResourceTrafficCopService` + `HkLockScanner` before allowing any scheduled task to run on that drive.
- **`.hash` Sidecar (`# Container:` header):** Verified across `ShadowLibrary`, `HouseKeeping`, `RedRoom`, and `legacy-sister-app` (`# Format: Universal Sister-App Hash Sidecar v1`, `# Container: size=... crc32=... sha256=... mtimeUtc=...`).

---

## 7. SQLite / JSON / DTO Schema Reference (`data\mother.db`, `PRAGMA user_version = 1` — [Standard 18 §3](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/18_DATA_DICTIONARY_AND_SCHEMA_LOOKUP.md))

All SQLite tables in `data\mother.db` (defined in `Dev\migrations\0001_initial_schema.sql` and tracked exclusively via `PRAGMA user_version = 1`) use strictly pre-registered Standard 18 §3 `snake_case` SQL columns mapped 1:1 to `camelCase` JSON keys (locked by `MotherDbAndWireLockstepTests` + `wwwroot\js\api\contracts.js`):

### 7.1 Table `fleet_health_events`
| SQL Column (`snake_case`) | JSON Key (`camelCase`) | SQLite Type | Description |
| :--- | :--- | :--- | :--- |
| `id` | `id` | `TEXT PRIMARY KEY NOT NULL` | Deterministic/UUID event row ID. |
| `suite_id` | `suiteId` | `TEXT NOT NULL` | Suite identifier (`"mother"`). |
| `app_id` | `appId` | `TEXT NOT NULL` | Canonical sister app slug (`housekeeping`, `shadowlibrary`, etc.). |
| `status` | `status` | `TEXT NOT NULL` | Health/lifecycle status (`"online"`, `"stopped"`, `"crashed"`, `"woken"`). |
| `residency_mode` | `residencyMode` | `TEXT NOT NULL DEFAULT 'warm'` | `"warm"`, `"cold"`, or `"disabled"`. |
| `pid` | `pid` | `INTEGER` | OS process ID at time of event. |
| `process_start_time_utc` | `processStartTimeUtc` | `TEXT` | Process start timestamp (`YYYY-MM-DDTHH:mm:ss.fffZ`). |
| `exit_code` | `exitCode` | `INTEGER` | Child process exit code (if terminated). |
| `retry_count` | `retryCount` | `INTEGER NOT NULL DEFAULT 0` | Restart attempts inside circuit-breaker window. |
| `is_online` | `isOnline` | `INTEGER NOT NULL DEFAULT 0 CHECK (is_online IN (0, 1))` | Boolean flag (`0` or `1`). |
| `has_active_conflicts` | `hasActiveConflicts` | `INTEGER NOT NULL DEFAULT 0 CHECK (has_active_conflicts IN (0, 1))` | Boolean conflict flag (`0` or `1`). |
| `error_code` | `errorCode` | `TEXT` | Structured error code (optional). |
| `error_message` | `errorMessage` | `TEXT` | Error description (optional). |
| `error_remedy` | `errorRemedy` | `TEXT` | Suggested operator action (optional). |
| `metadata_json` | `metadataJson` | `TEXT NOT NULL DEFAULT '{}' CHECK (json_valid(metadata_json))` | Validated JSON context object. |
| `created_at_utc` | `createdAtUtc` | `TEXT NOT NULL` | ISO-8601 UTC millisecond timestamp (`YYYY-MM-DDTHH:mm:ss.fffZ`). |

### 7.2 Table `schedule_deferrals`
| SQL Column (`snake_case`) | JSON Key (`camelCase`) | SQLite Type | Description |
| :--- | :--- | :--- | :--- |
| `id` | `id` | `TEXT PRIMARY KEY NOT NULL` | Deferral record ID. |
| `suite_id` | `suiteId` | `TEXT NOT NULL` | Suite identifier (`"mother"`). |
| `app_id` | `appId` | `TEXT NOT NULL` | Sister app whose scheduled job was deferred. |
| `job_id` | `jobId` | `TEXT NOT NULL` | Schedule / job identifier. |
| `drive_mount` | `driveMount` | `TEXT NOT NULL` | Contended drive mount or root path. |
| `duration_ms` | `durationMs` | `INTEGER NOT NULL DEFAULT 0` | Deferral duration in milliseconds. |
| `description` | `description` | `TEXT` | Human-readable arbitration reason (`Suite Drive I/O Mutex`). |
| `status` | `status` | `TEXT NOT NULL` | Deferral state (`"deferred"`, `"resolved"`). |
| `is_dry_run` | `isDryRun` | `INTEGER NOT NULL DEFAULT 0 CHECK (is_dry_run IN (0, 1))` | Sandbox/dry-run indicator (`0` or `1`). |
| `next_run_at_utc` | `nextRunAtUtc` | `TEXT` | Deferred target execution timestamp (`YYYY-MM-DDTHH:mm:ss.fffZ`). |
| `metadata_json` | `metadataJson` | `TEXT NOT NULL DEFAULT '{}' CHECK (json_valid(metadata_json))` | Validated JSON metadata (`blockingAppId`, `lockFilePath`). |
| `created_at_utc` | `createdAtUtc` | `TEXT NOT NULL` | ISO-8601 UTC millisecond timestamp. |

### 7.3 Table `notification_history`
| SQL Column (`snake_case`) | JSON Key (`camelCase`) | SQLite Type | Description |
| :--- | :--- | :--- | :--- |
| `id` | `id` | `TEXT PRIMARY KEY NOT NULL` | Alert record ID. |
| `suite_id` | `suiteId` | `TEXT NOT NULL` | Suite identifier (`"mother"`). |
| `app_id` | `appId` | `TEXT NOT NULL` | Originating app ID. |
| `slug` | `slug` | `TEXT NOT NULL` | Canonical alert slug (e.g. `doctor-test-alert`). |
| `title` | `title` | `TEXT NOT NULL` | Alert title. |
| `description` | `description` | `TEXT` | Redacted summary description. |
| `status` | `status` | `TEXT NOT NULL` | Delivery state (`"sent"`, `"simulated"`, `"failed"`). |
| `retry_count` | `retryCount` | `INTEGER NOT NULL DEFAULT 0` | Delivery retry count. |
| `is_dry_run` | `isDryRun` | `INTEGER NOT NULL DEFAULT 0 CHECK (is_dry_run IN (0, 1))` | Simulated/dry-run indicator (`0` or `1`). |
| `error_code` | `errorCode` | `TEXT` | Failure code (if any). |
| `error_message` | `errorMessage` | `TEXT` | Failure message (if any). |
| `metadata_json` | `metadataJson` | `TEXT NOT NULL DEFAULT '{}' CHECK (json_valid(metadata_json))` | Redacted JSON metadata. |
| `created_at_utc` | `createdAtUtc` | `TEXT NOT NULL` | ISO-8601 UTC millisecond timestamp. |
| `completed_at_utc` | `completedAtUtc` | `TEXT` | Delivery completion UTC millisecond timestamp. |

---

## 8. Backward Compatibility & Sister-App Integration Notes

1. **Full SemVer Interoperability (`v0.1.0` $\rightarrow$ `v0.2.0` $\rightarrow$ `v1.0.0`):**
   - `Mother` uses strict 3-part SemVer without pre-release suffixes so any sister app's installer or runtime can compare `minSuiteVersion` (`"0.1.0"`) using simple 3-integer tuple comparison.
2. **Graceful Partial-Suite Operation:**
   - `Mother` never assumes all 6 sister apps are installed. If `manifests\` contains 0, 1, or 3 manifests, all endpoints (`/api/v1/suite/apps`, `/api/v1/suite/coexistence/status`, `/api/v1/suite/doctor/run`) and UI tabs scale dynamically to the installed set.
3. **Dual Route & Manifest Tolerance:**
   - During the transition from `v1` (`mother.manifest.json` + `/api/*`) to `v2` (`suite.manifest.json` + `/api/v1/*`), `Mother` probes `/api/v1/*` first and falls back to `/api/*` automatically, ensuring zero broken health checks or schedule deferrals.
