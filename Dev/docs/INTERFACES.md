# Mother — Single-API Gateway Reference, Manifests & File Contracts (`INTERFACES.md`)

> **Active Baseline Version:** `v0.1.0` (`v0.2.0` In-Dev) | **Full SemVer (`MAJOR.MINOR.PATCH`)** | **Owner:** `@devv` & `@tessa` ("The Manual Maker") | **Verified By:** `@dean` ([Standard 16 §3.5](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/16_PROJECT_DOSSIERS_AND_FEATURE_TRACKING.md))

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

### 2.1 Suite-Level Gateway Endpoints (`/api/v1/suite/*` on Port `:5100` / Sandbox `:15100`)
Per [Standard 17 §5.1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md#L248-L260) and [`v0.1.0.2026-11-04.md#L109-L116`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-04.md#L109-L116):

| HTTP Method & Route | Request Body (`camelCase`) | Response Payload (`camelCase` JSON) | Behavior & Description |
| :--- | :--- | :--- | :--- |
| `GET /api/v1/suite/health` | — | `{ "status": "ok", "suiteId": "mother", "version": "0.1.0", "sdkVersion": "0.1.0", "pid": 4120, "uptimeSeconds": 86400, "registeredApps": 6, "warmApps": 4, "coldApps": 2, "disabledApps": 0 }` | Suite daemon liveness, version, and residency summary. |
| `GET /api/v1/suite/apps` | — | `{ "apps": [{ "appId": "housekeeping", "name": "Housekeeping", "version": "1.0.0-rc.12", "brandAccent": "#f59e0b", "internalPort": 5178, "residency": "warm", "state": "idle", "activeJobs": 0, "queuedJobs": 0, "uiUrl": "/app/housekeeping/" }] }` | Lists all installed sister apps discovered in `manifests\*.manifest.json` with live status and job counts. |
| `POST /api/v1/suite/apps/reload` | `{}` | `{ "reloaded": true, "discoveredCount": 6, "apps": [...] }` | Hot-reloads `manifests\*.manifest.json` after an installer adds, upgrades, or removes a sister app. |
| `POST /api/v1/suite/apps/<app-id>/residency` | `{ "residency": "warm\|cold\|disabled", "idleTimeoutMinutes": 15 }` | `{ "appId": "<app-id>", "residency": "cold", "state": "stopped" }` | Updates per-app residency in `data\settings.json` and immediately starts or hibernates the child worker. |
| `POST /api/v1/suite/apps/<app-id>/wake` | `{}` | `{ "appId": "<app-id>", "woken": true, "pid": 15240, "startupMs": 420 }` | Wakes a `cold` sister app, polls its internal `/api/v1/health` at `250ms` intervals until `200 OK`, and returns. |
| `POST /api/v1/suite/apps/<app-id>/stop` | `{ "gracefulTimeoutMs": 5000 }` | `{ "appId": "<app-id>", "stopped": true }` | Drains (`5s`) and stops a specific child worker (used during sister-app upgrade or uninstall). |
| `POST /api/v1/suite/jobs/stop-all` | `{ "reason": "User Fleet Emergency Stop" }` | `{ "stoppedApps": ["housekeeping", "shadowlibrary"], "totalCancelledJobs": 3 }` | Broadcasts `POST /api/v1/jobs/stop-all` to all running sister apps. |
| `POST /api/v1/suite/power/action` | `{ "postRunPowerAction": "StayAwake\|Sleep\|Shutdown" }` | `{ "postRunPowerAction": "Sleep", "fleetBusy": true, "countdownActive": false }` | Sets the global Post-Run Power Coordinator target (`FleetPowerCoordinator`). |
| `POST /api/v1/suite/power/cancel-countdown` | `{}` | `{ "cancelled": true, "postRunPowerAction": "StayAwake" }` | Aborts an active 60-second pre-Sleep/Shutdown countdown toast and resets to `StayAwake`. |
| `GET /api/v1/suite/scheduler/traffic-cop` | — | `{ "activeLocks": [...], "recentDeferrals": [{ "appId": "housekeeping", "scheduleId": "nightly", "drive": "E:", "deferredMinutes": 30, "reason": "Held by shadowlibrary (.hklock)" }] }` | Returns live drive mutex locks and recent schedule deferral decisions from `FleetScheduleTrafficCop`. |
| `GET /api/v1/suite/coexistence/status` | *(Alias: `GET /api/v1/coexistence/status`)* | `{ "autoAlignWithSisterApps": false, "sharedRoots": [...], "conflicts": [...], "apps": [...] }` | Aggregates `/api/v1/coexistence/status` across running apps and `<Root>\.coexistence.json` for offline apps (`FleetCoexistenceArbitrator`). |
| `POST /api/v1/suite/coexistence/apply-recommendations` | `{ "targetAppIds": ["housekeeping", "shadowlibrary"], "presetId": "zero-rewrite-parity" }` | `{ "applied": true, "updatedApps": ["housekeeping", "shadowlibrary"] }` | Dispatches 1-click preset alignment to child apps and updates `<Root>\.coexistence.json`. |
| `POST /api/v1/suite/doctor/run` | `{}` | `{ "overall": "pass\|warn\|fail", "suiteChecks": [...], "appReports": { "housekeeping": {...} } }` | Executes `🩺 Fleet System Doctor` across `Mother` and all installed sister apps. |

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

`Mother` exposes a unified Server-Sent Events (SSE) / WebSocket stream at `GET /api/v1/suite/events` (and proxies child `/app/<app-id>/events` streams) powering the outer tab status dots and the `Ctrl+\`` Unified Log Drawer:

| Event Name (`event:`) | JSON Payload (`data:`) | Trigger Condition |
| :--- | :--- | :--- |
| `suite.app.stateChanged` | `{ "appId": "redroom", "residency": "cold", "state": "starting\|idle\|busy\|error", "activeJobs": 1, "progressPercent": 45.0 }` | Emitted whenever a child app starts, wakes, hibernates, changes job state, or trips the crash-loop circuit breaker. |
| `suite.log.line` | `{ "timestampUtc": "2026-11-04T18:00:00.000Z", "appId": "shadowlibrary", "level": "INFO", "slice": "BitRotScrub", "message": "[HEARTBEAT] ..." }` | Streams real-time lines from `Mother`'s logger and each child process's 50-line stdout/stderr ring buffer into the `Ctrl+\`` drawer. |
| `suite.schedule.deferred` | `{ "appId": "housekeeping", "scheduleId": "nightly", "drive": "E:", "blockingAppId": "shadowlibrary", "deferredMinutes": 30 }` | Emitted when `FleetScheduleTrafficCop` defers a colliding drive schedule. |
| `suite.power.countdown` | `{ "action": "Sleep\|Shutdown", "secondsRemaining": 60, "cancellable": true }` | Emitted every second during the 60-second post-run power countdown. |

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
<PROGRAMS_ROOT>/Mother\Mother.exe [--suite-tab <app-id>]

# 2. Pop out a specific sister app into its own standalone desktop window
<PROGRAMS_ROOT>/Mother\Mother.exe --window housekeeping

# 3. Open Mother or a specific sister app in the default web browser
<PROGRAMS_ROOT>/Mother\Mother.exe --browser greenroom

# 4. Run deterministic headless self-test in Sandbox (exits 0 on pass)
<WORKSPACE_ROOT>/Mother\Sandbox\Mother.exe --test-mode --headless --programs-dir .\data\mock_programs --data-dir .\data --log-dir .\logs --port 15100
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
  "sdkVersion": "0.1.0",
  "suitePort": 5100,
  "appId": "housekeeping",
  "name": "Housekeeping",
  "shortName": "Housekeeping",
  "version": "1.0.0-rc.12",
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
`AppDiscoveryService` also parses legacy `v1` manifests by normalizing:
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
  "updatedAtUtc": "2026-11-04T18:00:00.000Z",
  "registeredApps": {
    "shadowlibrary": {
      "version": "1.0.0-rc.14",
      "archiveFormat": "7z",
      "verifySidecarHash": true,
      "zeroRewriteCoAudit": true
    },
    "housekeeping": {
      "version": "1.0.0-rc.12",
      "archiveFormat": "7z",
      "verifySidecarHash": true,
      "respectSisterHashSidecars": true
    }
  }
}
```

### 6.4 Distributed Directory Lock (`.hklock`) & Universal `.hash` Sidecar
- **`.hklock` (`<TargetDir>\.<app-id>_<pid>.hklock`):** Uses `Foundation.Core.PidSafeLockEnvelope` (`schemaVersion: 1`, `appId`, `pid`, `processStartTimeUtc`, `hostName`, `operation`, `acquiredAtUtc`, `heartbeatAtUtc`, `expiresAtUtc`). Inspected by `FleetScheduleTrafficCop` before allowing any scheduled task to run on that drive.
- **`.hash` Sidecar (`# Container:` header):** Verified across `ShadowLibrary`, `Housekeeping`, `Redroom`, and `legacy-sister-app` (`# Format: Universal Sister-App Hash Sidecar v1`, `# Container: size=... crc32=... sha256=... mtimeUtc=...`).

---

## 7. SQLite / JSON / DTO Schema Reference (`data\mother.db` — [Standard 18](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/18_DATA_DICTIONARY_AND_SCHEMA_LOOKUP.md))

All SQLite tables in `<PROGRAMS_ROOT>/Mother\data\mother.db` use `snake_case` SQL columns mapped to `camelCase` JSON keys per [Standard 18](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/18_DATA_DICTIONARY_AND_SCHEMA_LOOKUP.md):

### 7.1 Table `fleet_health_events`
| SQL Column (`snake_case`) | JSON Key (`camelCase`) | SQLite Type | Description |
| :--- | :--- | :--- | :--- |
| `id` | `id` | `INTEGER PRIMARY KEY` | Auto-increment event row ID. |
| `app_id` | `appId` | `TEXT NOT NULL` | Canonical sister app slug (`housekeeping`, `shadowlibrary`, etc.). |
| `event_type` | `eventType` | `TEXT NOT NULL` | `"started"`, `"woken"`, `"hibernated"`, `"crashed"`, `"circuit_breaker_tripped"`. |
| `pid` | `pid` | `INTEGER` | OS process ID at time of event. |
| `details_json` | `detailsJson` | `TEXT` | Additional diagnostic context. |
| `created_at_utc` | `createdAtUtc` | `TEXT NOT NULL` | ISO-8601 UTC timestamp (`YYYY-MM-DDTHH:mm:ss.fffZ`). |

### 7.2 Table `schedule_deferrals`
| SQL Column (`snake_case`) | JSON Key (`camelCase`) | SQLite Type | Description |
| :--- | :--- | :--- | :--- |
| `id` | `id` | `INTEGER PRIMARY KEY` | Auto-increment deferral record ID. |
| `deferred_app_id` | `deferredAppId` | `TEXT NOT NULL` | Sister app whose scheduled job was deferred. |
| `schedule_id` | `scheduleId` | `TEXT NOT NULL` | Schedule identifier from `suite.manifest.json`. |
| `blocking_app_id` | `blockingAppId` | `TEXT NOT NULL` | Sister app holding the active job or `.hklock` on the target drive. |
| `target_drive` | `targetDrive` | `TEXT NOT NULL` | Contended drive mount (e.g. `"E:"`). |
| `deferred_minutes` | `deferredMinutes` | `INTEGER NOT NULL` | Minutes deferred (`30` default). |
| `reason` | `reason` | `TEXT NOT NULL` | Human-readable arbitration reason (`Suite Drive I/O Mutex`). |
| `created_at_utc` | `createdAtUtc` | `TEXT NOT NULL` | ISO-8601 UTC timestamp. |

---

## 8. Backward Compatibility & Sister-App Integration Notes

1. **Full SemVer Interoperability (`v0.1.0` $\rightarrow$ `v0.2.0` $\rightarrow$ `v1.0.0`):**
   - `Mother` uses strict 3-part SemVer without `-rc.<n>` suffixes so any sister app's installer or runtime can compare `minSuiteVersion` (`"0.1.0"`) using simple 3-integer tuple comparison.
2. **Graceful Partial-Suite Operation:**
   - `Mother` never assumes all 6 sister apps are installed. If `manifests\` contains 0, 1, or 3 manifests, all endpoints (`/api/v1/suite/apps`, `/api/v1/suite/coexistence/status`, `/api/v1/suite/doctor/run`) and UI tabs scale dynamically to the installed set.
3. **Dual Route & Manifest Tolerance:**
   - During the transition from `v1` (`mother.manifest.json` + `/api/*`) to `v2` (`suite.manifest.json` + `/api/v1/*`), `Mother` probes `/api/v1/*` first and falls back to `/api/*` automatically, ensuring zero broken health checks or schedule deferrals.
