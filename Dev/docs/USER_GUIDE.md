# Mother — End-User Manual & Fleet Supervisor Guide (`USER_GUIDE.md`)

> **Active Baseline Version:** `v0.2.0` (`MPL-2.0` — Next Horizon: `v0.2.1`) | **Full SemVer (`MAJOR.MINOR.PATCH`)** | **SDK Pin:** `Foundation v0.4.0`

Welcome to the **`Mother`** User Guide. This manual explains what `Mother` (`:5100`) does, how to install and upgrade it inside your `StreamItAll\Mother` suite umbrella, how to launch it via CLI or tray, how to navigate its Titanium Sky (`#38bdf8`) single-window shell, and how to supervise, schedule, and align all six sister applications (**Housekeeping**, **ShadowLibrary**, **Redroom**, **Greenroom**, **StreamEngine**, and **legacy-sister-app**).

---

## 1. What It Is & Who It's For

**`Mother`** is the Single Shared Suite Daemon (`http://127.0.0.1:5100/`), unified Windows system tray sentinel, reverse-proxy gateway, disk traffic cop, autonomous Cold-Wake scheduler, post-run power coordinator, and cross-app settings broker for your workstation.

### What Problems `Mother` Solves for You
1. **One System Tray Icon & One Window Instead of Six:** Instead of six separate background tray icons and six browser tabs competing for attention, `Mother` hosts your entire suite under a single **Titanium Sky (`#38bdf8`)** outer horizontal tab strip (`Alt+1..7`)—while still letting you pop any app out into a standalone desktop window (`--window`) or browser (`--browser`).
2. **Partial Suite Freedom (Install 1 App or All 6):** You never have to install apps you do not use. `Mother` discovers only installed sister apps in `manifests\`, your `StreamItAll\Mother\` suite umbrella, and HKCU registry keys, scaling its tabs and checks automatically.
3. **Zero 3:00 AM Drive Thrashing:** When two apps target the same physical drive or hold a `.hklock` sentinel (for example, a `ShadowLibrary` scrub while `Housekeeping` runs an archive job), `Mother`'s **Traffic Cop** flags live vs. stale locks and records schedule deferrals (`POST /api/v1/scheduler/defer`) so drives never thrash.
4. **Autonomous Cold-Wake & Idle Hibernation:** `Cold` (`on-demand`) apps stay at `0 MB` RAM until clicked, requested through `/api/v1/apps/<app-id>/*` or `/app/<app-id>/`, or triggered by a `schedules[]` entry (with `runIfMissed` catch-up), and hibernate automatically after `idleTimeoutMinutes` (`15m` default) once active/queued jobs reach zero.
5. **Safe Post-Run Sleep & Shutdown:** Instead of one app sleeping your PC while another is mid-job, `Mother`'s **Post-Run Power Coordinator (`StayAwake`, `Sleep`, `Shutdown`)** waits until every registered sister app reports `0` active/queued jobs and `0` live `.hklock` holds (followed by a 60-second cancellable countdown) before sleeping or shutting down.
6. **1-Click Cross-App Settings Alignment:** `Mother` inspects shared root `.coexistence.json` manifests and `/api/v1/coexistence/status`, surfacing conflicts with 1-click **`Apply Safe-Stagger`**.

---

## 2. Installation, Upgrading & Uninstalling (`Mother-Setup-v0.2.0-win-x64.exe`)

### 2.1 Standalone Installation
1. Launch **`Mother-Setup-v0.2.0-win-x64.exe`**.
2. The **4-Pillar Dark Setup Wizard** (`650x434` compact / `650x544` details, Titanium Sky `#38bdf8`, powered by `Foundation v0.4.0` `InstallerEngineCore`) opens:
   - **Destination Folder:** Defaults to `%LOCALAPPDATA%\Programs\StreamItAll\Mother\Mother\` (Clean Release Root: `Mother.exe`, `AGENTS.md`, `LICENSE`, `app\`, `scripts\`).
   - **Shared Suite Umbrella Directory (`--suite-dir`):** Defaults to `%LOCALAPPDATA%\Programs\StreamItAll\Mother\` where installed sister applications reside beside `Mother\`.
   - **Install Profile (`--profile`):** Choose **`full`** (Full Suite Host with bundled Web UI & scripts) or **`lightweight`** (Core suite gateway & supervisor only).
   - **Options:** Desktop/Start Menu shortcuts (HKCU suite + uninstall metadata always registers even if shortcuts are unchecked), Windows startup (`--tray-only` + `\Mother_Watchdog`), and opt-in **`--migrate-legacy`** for migrating legacy flat installations.
3. Click **`Install`** (or **`Upgrade`**). Click **`Show Details`** to watch the live extraction log.
4. Click **`Finish`** when complete to launch `Mother`.

### 2.2 Upgrading & Uninstalling
- **Upgrading:** Running a newer `Mother-Setup-v<Version>-win-x64.exe` over an existing install detects the installation, creates a pre-upgrade backup under `backups\`, replaces only `app\` and `scripts\`, and preserves `manifests\`, `data\`, `logs\`, and your 20-snapshot config history.
- **Uninstalling:** Run `Mother-Uninstall.exe` in your installation directory (or use Windows *Installed Apps*). Your `data\` and `logs\` directories are preserved by default unless you explicitly enable destructive purge (`--purge-data`).
- **Automated Installer Flags:** Supports `--silent`, `--headless`, `--verify`, `--uninstall`, `--purge-data`, `--dry-run`, `--install-dir <dir>`, `--suite-dir <dir>`, `--profile <lightweight|full>`, `--migrate-legacy`, `--autostart`, `--visible-test`, `--test-mode`, `--data-dir <dir>`, and `--log-dir <dir>`.

### 2.3 Sister-App Co-Bootstrapping
When sister-app installers register `<app-id>.manifest.json` in `%LOCALAPPDATA%\Programs\StreamItAll\Mother\manifests\` (or install directly inside `StreamItAll\Mother\<AppName>\`) and call `POST /api/v1/suite/apps/reload` (or when you click **`Reload Manifests`** in `Fleet Overview` or type `reload` in the docked terminal), `Mother` adds or updates those tabs without restarting running daemons.

---

## 3. First-Run Setup & Command-Line Launch Modes

When `Mother.exe` starts (`http://127.0.0.1:5100/`):
1. **Automatic Dual-Schema Discovery:** `Mother` scans `manifests\*.json`, installed sibling roots under `StreamItAll\Mother\`, HKCU app metadata, and read-only legacy fallback locations, supporting both `v2` `camelCase` (`suite.manifest.json`) and legacy `v1` `snake_case` (`mother.manifest.json`).
   - Navigating to `/app/<app-id>` redirects (`307`) to `/app/<app-id>/`, which forwards directly to the sister UI root with `X-Forwarded-Prefix` and unbuffered SSE streaming.
2. **Residency Startup (`Warm` / `Cold` / `Disabled`):** `Warm` (`always-on`) apps start automatically under `Mother` (`--managed-by-mother`); `Cold` (`on-demand`) apps stay at `0 MB` RAM until clicked, called via `/api/v1/apps/<app-id>/*` or `/app/<app-id>/`, or triggered by `schedules[]`; `Disabled` apps stay parked.
3. **CLI Launch Modes:**
   - `Mother.exe --window <appId>` — Opens a specific sister app in its own dedicated desktop window.
   - `Mother.exe --browser [<appId>]` — Opens `Mother` or a sister app in your default web browser.
   - `Mother.exe --tray-only` *(or `--minimized`)* — Starts the daemon and tray icon quietly in the background.
   - `Mother.exe --suite-tab <appId>` — Focuses an existing `Mother` window and switches directly to `<appId>`.
   - `Mother.exe --diagnose` — Runs `Fleet System Doctor` and prints a JSON diagnostic summary (`0` pass/warn, `1` fail).

---

## 4. Dashboard & Interface Tour (`v0.2.0` Controls)

`Mother`'s interface uses **two orthogonal navigation axes** so outer suite controls never collide with inner sister-app sidebars:

### 4.1 Outer Horizontal Topbar (`Alt+1..7` & Global Actions)
| Control / Element | Hotkey | What It Does |
| :--- | :---: | :--- |
| **Outer Suite Tab Strip** (`Mother` + discovered sister apps) | `Alt+1` .. `Alt+7` | `Alt+1` shows Mother's Control Hub; `Alt+2`..`Alt+7` switch to each sister app's embedded frame (`/app/<appId>/`, auto-redirected from `/app/<appId>` via `307`) with `Wake App` / `Open Standalone` controls. |
| **Status Chips (`PROD` / `SANDBOX`, `STAY AWAKE` / `SLEEP <N>S`)** | — | Displays live containment mode and current Post-Run Power policy or active countdown timer. |
| **`QR` Button** | — | Opens the **Slide-Over Right Drawer** with an offline SVG QR code (`100.64.0.0/10` Tailscale CGNAT / local URL) for mobile PWA pairing. |
| **`Stop All` Button** | — | Emergency stop: broadcasts `POST /api/v1/suite/jobs/stop-all` across all running sister workers and cancels any active power countdown. |

### 4.2 Collapsible Sidebar (`Ctrl+B`), Mobile Bottom Nav (`<= 820px`) & Sub-Page Pills
Press **`Ctrl+B`** to collapse/expand the left sidebar (`220px` <-> `56px`). On viewports `<= 820px`, navigation moves automatically to the fixed bottom bar (**`Fleet`**, **`Traffic`**, **`Doctor`**, **`Settings`**):
1. **`Fleet Overview`:** Shows KPI cards (`Registered Apps`, `Online Now`, `Warm / Cold`, `Active Conflicts`), **`Reload Manifests`**, and the sortable **Supervised Sister Applications** table (`Wake` / `Stop` actions + click any row to open the **Slide-Over Right Drawer** for **`Warm` / `Cold` / `Disabled`** residency switching, `Target Drives`, `Shared Mounts`, and `Mutually Exclusive With` details).
2. **`Traffic Cop`:** Organized into 3 **Sub-Page Pills** (`Active Locks`, `Coexistence Conflicts`, `Schedule Deferrals`) with **`Purge Stale Locks`** and **`Apply Safe-Stagger`** toolbar buttons.
3. **`Doctor & Power`:** Organized into 4 **Sub-Page Pills** (`Doctor`, `Power`, `Notifications`, `DPAPI Vaults`) with **`Run Doctor`**, **`Auto-Repair`**, **`Backup DB`**, and **`Export Bundle`** toolbar buttons.
4. **`Settings` (`Ctrl+S`):** Visual settings screen (no raw `Key=Value` forms): *Use global format rules*, *Allow Tailscale bind (100.64.0.0/10)*, *Auto-align sister app schedules*, segmented **Post-Run Power Policy** picker, numeric steppers (`Cold-Wake Idle (min)`, `Poll Interval (ms)`), **`Target Drives`**, **`Save Settings (Ctrl+S)`**, **`Export Settings JSON`**, and the **20-Snapshot Rollback Table**.

### 4.3 Docked Terminal (`Ctrl+\``), Dialogs (`Ctrl+K`, `?`), Tables & Tooltips
- **Docked Terminal (`Ctrl+\``):** Toggles the bottom `<foundation-terminal>` drawer (`S` / `M` / `L` height presets) streaming real-time SSE `log_line` events and running whitelisted diagnostic & control commands: `help`, `status`, `health`, `apps`, `traffic`, `locks`, `coexistence`, `doctor`, `logs [1..100]`, `purge-stale`, `reload`, `wake <appId>`, `stop <appId>`, and `power [StayAwake|Sleep|Shutdown]`.
- **Omni-Search (`Ctrl+K`) & Hotkeys (`?`):** Native `<dialog closedby="any">` overlays (`Escape` or backdrop click to dismiss).
- **Sortable Accessible Tables:** Column headers sort deterministically (`Intl.Collator` + `moveBefore`), support row **Bulk Select**, and include **`Export CSV`** / **`Export JSON`** toolbars.
- **2-Line Styled Tooltips:** Hovering or focusing primary controls shows clean 2-line styled tooltips (`data-tooltip-title` / `data-tooltip-body`) with zero native `title=""` clutter.

### 4.4 Mobile Companion PWA & Remote Pairing (`100.64.0.0/10`)
`Mother` exposes a responsive Mobile PWA layout automatically when opened on viewports `<= 820px` (replacing the left sidebar with a fixed bottom navigation bar and touch-friendly drawer):
1. Open **`Settings`** (`Ctrl+S`), toggle **Allow Tailscale bind (`100.64.0.0/10`)** on, and save.
2. Click **`QR`** in the outer topbar to open the offline SVG pairing drawer, then scan the QR code from your phone over Tailscale (`100.64.0.0/10`) — non-loopback public/LAN IPs remain blocked by the CGNAT allowlist guard.
3. On mobile browsers, the `manifest.webmanifest` and `sw.js` service worker allow installing `Mother` to your home screen for one-tap fleet monitoring, job stop, or power countdown control.

---

## 5. Everyday Workflows

### Workflow 1: Switching an App Between `Warm` and `Cold` (`0 MB` Idle RAM)
1. In **`Fleet Overview`**, click any sister app row to open the **Slide-Over Right Drawer**.
2. Click **`Warm`**, **`Cold`**, or **`Disabled`** in the **Residency Mode** segmented picker.
3. `Cold` apps stay stopped (`0 MB` RAM) until you click their outer tab (`Alt+2..7`), click **`Wake`** (or run `wake <appId>` in the terminal), hit `/api/v1/apps/<appId>/*` or `/app/<appId>/`, or when a `schedules[]` entry comes due; once idle for `idleTimeoutMinutes`, Mother hibernates the worker cleanly.

### Workflow 2: Safe Overnight Post-Run Sleep or Shutdown
1. Open **`Doctor & Power`** -> **`Power`** (or **`Settings`**) and select **`Sleep`** (or **`Shutdown`**), or run `power Sleep` in the docked terminal.
2. While any sister app has active/queued jobs or a live `.hklock` hold, `Mother` inhibits sleep (`SLEEP INHIBITED: YES`).
3. Once all apps hit `0` active/queued jobs and `0` live locks, a **60-second cancellable countdown** begins (`SLEEP 60S` in the topbar; click **`Cancel Countdown`** anytime to abort back to `StayAwake`).

### Workflow 3: Resolving Drive Contention & Coexistence Drift
1. Open **`Traffic Cop`**. If dead-PID `.hklock` files appear as `STALE`, click **`Purge Stale Locks`** (or type `purge-stale` in the terminal).
2. Switch to **`Schedule Deferrals`** to review automatic or API-submitted deferrals (`/api/v1/scheduler/defer`), and use **`Coexistence Conflicts`** -> **`Apply Safe-Stagger`** to align shared presets and update `<SharedRoot>\.coexistence.json`.

---

## 6. System Tray Sentinel & Master Watchdog (`\Mother_Watchdog`)

- **Single System Tray Icon (`Global\Mother_Supervisor_Active`):** Right-clicking `Mother`'s tray icon lets you open the Control Hub, switch directly to a sister tab, run `Doctor`, or cleanly exit the suite daemon. When `Explorer` restarts, `TaskbarCreated` automatically restores the tray icon.
- **`\Mother_Watchdog`:** `Mother` centralizes Task Scheduler recovery in a single `\Mother_Watchdog` entry and monitors child daemons with a `3 crashes in 60s` circuit breaker.

---

## 7. Troubleshooting & FAQ

### 7.1 Common Issues & Fixes
| Question / Symptom | Root Cause | Solution / Remediation |
| :--- | :--- | :--- |
| **A newly installed sister app is missing from `Mother`'s tab bar** | Its manifest was added after boot. | Click **`Reload Manifests`** in **`Fleet Overview`** or run `reload` in the terminal (`POST /api/v1/suite/apps/reload`). |
| **A `.hklock` entry stays listed after an app crashed** | The previous process exited without deleting its lockfile. | Open **`Traffic Cop`** -> **`Active Locks`** (status shows `STALE`) and click **`Purge Stale Locks`** (or run `purge-stale`). |
| **Why didn't `Mother` sleep my PC after one app finished?** | Another sister app still had active/queued jobs or a live `.hklock`. | Check **`Fleet Overview`** and **`Traffic Cop`**; `Mother` waits until every registered app is idle before starting the 60s countdown. |
| **How do I revert a bad settings change?** | Saved changes are stored in `data\config_history\`. | Open **`Settings`** (`Ctrl+S` screen) and click **`Rollback`** on any of the last 20 snapshots. |
| **How do I inspect daemon logs quickly?** | Live logs stream into the docked terminal and `logs\mother-YYYYMMDD.jsonl`. | Press **`Ctrl+\``** and run `logs 50` or `status`, or run `Mother.exe --diagnose` to inspect health & crash bundles. |
| **Why did `/app/<appId>` return `307` or why did a Cold app take a second to load?** | Subpath routes canonicalize trailing slashes (`307 -> /app/<appId>/`) and wake stopped `Cold` workers on demand. | Wait 1–2 seconds for the `ColdWakeCoordinator` poll loop to launch the worker and forward the proxied HTTP request automatically. |
| **Why is my phone blocked when connecting over local Wi-Fi?** | `Mother` allows only loopback (`127.0.0.1`) and optional Tailscale CGNAT (`100.64.0.0/10`) addresses for security. | Enable **Allow Tailscale bind** in **`Settings`**, scan the **`QR`** drawer URL, and connect over Tailscale rather than raw LAN IP. |

### 7.2 Keyboard Shortcut Cheat Sheet
| Shortcut | Action |
| :--- | :--- |
| **`Alt+1` .. `Alt+7`** | Switch between `Mother` Control Hub (`Alt+1`) and discovered sister-app tabs (`Alt+2`..`Alt+7`). |
| **`Ctrl+B`** | Collapse or expand the desktop navigation sidebar (`220px` <-> `56px`). |
| **`Ctrl+\``** | Toggle the bottom docked interactive diagnostic terminal (`S` / `M` / `L` presets). |
| **`Ctrl+K`** | Open **Omni-Search** modal to jump across sister apps, actions, and screens. |
| **`Ctrl+S`** | Save current **`Settings`** configuration (creates a rollback snapshot under `data\config_history\`). |
| **`?`** | Open the keyboard shortcut cheat-sheet modal (`Escape` or backdrop click to close). |

---

## 8. Plain-Language Glossary

| Term | Plain-English Definition |
| :--- | :--- |
| **Shared Suite Daemon (`Mother :5100`)** | The single master background coordinator and reverse-proxy gateway (`http://127.0.0.1:5100/`) supervising all installed sister apps. |
| **`Warm` / `Cold` / `Disabled` Residency** | `Warm` (`always-on`) keeps an app running for instant access; `Cold` (`on-demand`) keeps it at `0 MB` RAM until requested or scheduled and hibernates when idle; `Disabled` keeps it parked. |
| **Traffic Cop & `.hklock`** | `ResourceTrafficCopService` + `HkLockScanner`, which inspect PID-safe `.hklock` files to prevent drive thrashing, purge dead-PID stale locks, and track `/api/v1/scheduler/defer` deferrals. |
| **Post-Run Power Coordinator** | Central `StayAwake` / `Sleep` / `Shutdown` controller that inhibits OS sleep until all sister apps finish and runs a 60s cancellable countdown. |
| **Coexistence Broker** | `FleetCoexistenceArbitrator`, which compares sister-app rules and `.coexistence.json` files and applies safe presets with **`Apply Safe-Stagger`**. |
| **`\Mother_Watchdog`** | Single Windows Task Scheduler recovery task replacing per-app watchdogs across the suite. |
