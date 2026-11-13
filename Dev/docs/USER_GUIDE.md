# Mother — End-User Manual & Fleet Supervisor Guide (`USER_GUIDE.md`)

> **Active Baseline Version:** `v0.1.0` (`Live` — Next Horizon: `v0.2.0`) | **Full SemVer (`MAJOR.MINOR.PATCH`)** | **SDK Pin:** `Foundation v0.3.0` | **Owner:** `@tessa` ("The Manual Maker") | **Verified By:** `@dean` ([Standard 16 §3.5](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/16_PROJECT_DOSSIERS_AND_FEATURE_TRACKING.md))

Welcome to the **`Mother`** User Guide. This manual explains what `Mother` (`:5100`) does, how to install and upgrade it, how to launch it via CLI or tray, how to navigate its Titanium Sky (`#38bdf8`) single-window shell, and how to supervise, schedule, and align all six sister applications (**Housekeeping**, **ShadowLibrary**, **Redroom**, **Greenroom**, **StreamEngine**, and **legacy-sister-app**).

---

## 1. What It Is & Who It's For

**`Mother`** is the Single Shared Suite Daemon (`http://127.0.0.1:5100/`), unified Windows system tray sentinel, reverse-proxy gateway, disk traffic cop, post-run power coordinator, and cross-app settings broker for your workstation.

### What Problems `Mother` Solves for You
1. **One System Tray Icon & One Window Instead of Six:** Instead of six separate background tray icons and six browser tabs competing for attention, `Mother` hosts your entire suite under a single **Titanium Sky (`#38bdf8`)** outer horizontal tab strip (`Alt+1..7`)—while still letting you pop any app out into a standalone desktop window (`--window`) or browser (`--browser`).
2. **Partial Suite Freedom (Install 1 App or All 6):** You never have to install apps you do not use. `Mother` discovers only the manifests present in `manifests\` (or your installed suite roots) and scales its tabs and checks automatically.
3. **Zero 3:00 AM Drive Thrashing:** When two apps target the same physical drive or hold a `.hklock` sentinel (for example, a `ShadowLibrary` scrub while `Housekeeping` runs an archive job), `Mother`'s **Traffic Cop** flags live vs. stale locks and records schedule deferrals so drives never thrash.
4. **Safe Post-Run Sleep & Shutdown:** Instead of one app sleeping your PC while another is mid-job, `Mother`'s **Post-Run Power Coordinator (`StayAwake`, `Sleep`, `Shutdown`)** waits until every registered sister app reports `0` active/queued jobs and `0` live `.hklock` holds (followed by a 60-second cancellable countdown) before sleeping or shutting down.
5. **1-Click Cross-App Settings Alignment:** `Mother` inspects shared root `.coexistence.json` manifests and `/api/v1/coexistence/status`, surfacing conflicts with 1-click **`Apply Safe-Stagger`**.

---

## 2. Installation, Upgrading & Uninstalling (`Mother-Setup-v0.1.0-win-x64.exe`)

### 2.1 Standalone Installation
1. Launch **`Mother-Setup-v0.1.0-win-x64.exe`** from `<WORKSPACE_ROOT>/Installers\` (or `<WORKSPACE_ROOT>/Mother\Installers\`).
2. The **4-Pillar Dark WinForms Setup Wizard** (`650×434` compact / `650×544` details, Titanium Sky `#38bdf8`) opens:
   - **Destination Folder:** Defaults to `<PROGRAMS_ROOT>/Mother` (Clean Release Root: `Mother.exe`, `README.md`, `app\`, `scripts\`).
   - **Options:** Desktop/Start Menu shortcuts, Windows startup (`\Mother_Watchdog`), and launch after setup.
3. Click **`Install`** (or **`Upgrade`**). Click **`Show Details ▾`** to watch the live extraction log.
4. Click **`Finish`** when complete to launch `Mother`.

### 2.2 Upgrading & Uninstalling
- **Upgrading:** Running a newer `Mother-Setup-v<Version>.exe` over an existing install detects the installation, creates a pre-upgrade backup under `backups\`, replaces only `app\` and `scripts\`, and preserves `manifests\`, `data\`, `logs\`, and your 20-snapshot config history.
- **Uninstalling:** Run `Mother-Uninstall.exe` in your installation directory (or use Windows *Installed Apps*). Your `data\` and `logs\` directories are preserved by default unless you explicitly enable destructive purge (`--purge-data`).
- **Automated / Sandbox Installer Flags:** Supports `--silent`, `--verify`, `--upgrade`, `--repair`, `--uninstall`, `--purge-data`, `--dry-run`, `--headless`, `--install-dir <dir>`, and `--test-mode` (auto-confines `--data-dir` to `<installDir>` when `--test-mode --install-dir` omits `--data-dir`).

### 2.3 Sister-App Co-Bootstrapping (`v0.2.0+`)
When sister-app installers register `<app-id>.manifest.json` in `<PROGRAMS_ROOT>/Mother\manifests\` and call `POST /api/v1/suite/apps/reload` (or when you click **`Reload Manifests`** in `Fleet Overview`), `Mother` adds or updates those tabs without restarting running daemons.

---

## 3. First-Run Setup & Command-Line Launch Modes

When `Mother.exe` starts (`http://127.0.0.1:5100/`):
1. **Automatic Dual-Schema Discovery:** `Mother` scans `manifests\*.json` and installed sister-app roots, supporting both Standard 17 `v2` `camelCase` (`suite.manifest.json`) and legacy `v1` `snake_case` (`mother.manifest.json`).
2. **Residency Startup (`Warm` / `Cold` / `Disabled`):** `Warm` apps start automatically under `Mother` (`--managed-by-mother`); `Cold` apps stay at `0 MB` RAM until clicked, called via `/api/v1/apps/<app-id>/*`, or woken manually; `Disabled` apps stay parked.
3. **CLI Launch Modes:**
   - `Mother.exe --window <appId>` — Opens a specific sister app in its own dedicated desktop window.
   - `Mother.exe --browser [<appId>]` — Opens `Mother` or a sister app in your default web browser.
   - `Mother.exe --tray-only` *(or `--minimized`)* — Starts the daemon and tray icon quietly in the background.
   - `Mother.exe --suite-tab <appId>` — Focuses an existing `Mother` window and switches directly to `<appId>`.
   - `Mother.exe --diagnose` — Runs `🩺 Fleet System Doctor` and prints a JSON diagnostic summary (`0` pass/warn, `1` fail).
   - `Mother.exe --test-mode --headless --data-dir <dir> --log-dir <dir>` — Executes a fail-closed Sandbox self-test and exits `0`.

---

## 4. Dashboard & Interface Tour (Live `v0.1.0` Controls)

`Mother`'s interface uses **two orthogonal navigation axes** ([`v0.1.0.2026-11-12.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-12.md)) so outer suite controls never collide with inner sister-app sidebars:

### 4.1 Outer Horizontal Topbar (`Alt+1..7` & Global Actions)
| Control / Element | Hotkey | What It Does |
| :--- | :---: | :--- |
| **Outer Suite Tab Strip** (`Mother` + discovered sister apps) | `Alt+1` .. `Alt+7` | `Alt+1` shows Mother's Control Hub; `Alt+2`..`Alt+7` switch to each sister app's embedded frame (`/app/<appId>/`) with live status dots (`Wake App` / `Open Standalone` controls when embedded). |
| **Status Chips (`PROD` / `SANDBOX`, `STAY AWAKE` / `SLEEP <N>S`)** | — | Displays live containment mode and current Post-Run Power policy or active countdown timer. |
| **`QR` Button** | — | Opens the **Slide-Over Right Drawer** with an offline SVG QR code (`100.64.0.0/10` Tailscale CGNAT / local URL) for mobile PWA pairing. |
| **`Stop All` Button** | — | Emergency stop: broadcasts `POST /api/v1/suite/jobs/stop-all` across all running sister workers and cancels any active power countdown. |

### 4.2 Collapsible Sidebar (`Ctrl+B`), Mobile Bottom Nav (`<= 820px`) & Sub-Page Pills
Press **`Ctrl+B`** to collapse/expand the left sidebar (`220px` $\leftrightarrow$ `56px`). On viewports `<= 820px`, navigation moves automatically to the fixed bottom bar (**`Fleet`**, **`Traffic`**, **`Doctor`**, **`Settings`**):
1. **`Fleet Overview`:** Shows KPI cards (`Registered Apps`, `Online Now`, `Warm / Cold`, `Active Conflicts`), **`Reload Manifests`**, and the sortable **Supervised Sister Applications** table (`Wake` / `Stop` actions + click any row to open the **Slide-Over Right Drawer** for **`Warm` / `Cold` / `Disabled`** residency switching, `Target Drives`, `Shared Mounts`, and `Mutually Exclusive With` details).
2. **`Traffic Cop`:** Organized into 3 **Sub-Page Pills** with **`Purge Stale Locks`** and **`Apply Safe-Stagger`** toolbar buttons:
   - `Active Locks` (`.hklock` holds with `LIVE` / `STALE` status badges and `(UTC)` timestamps)
   - `Coexistence Conflicts` (detected shared-resource overlaps)
   - `Schedule Deferrals` (SQLite `schedule_deferrals` history)
3. **`Doctor & Power`:** Organized into 4 **Sub-Page Pills** with **`Run Doctor`**, **`Auto-Repair`**, **`Backup DB`**, and **`Export Bundle`** toolbar buttons:
   - `Doctor` (diagnostic check table with `PASS` / `WARN` / `FAIL` badges)
   - `Power` (`StayAwake` / `Sleep` / `Shutdown` segmented picker, `Evaluate Tick`, `Cancel Countdown`, and `\Mother_Watchdog` registration badge)
   - `Notifications` (`Send Test Notification` + `Notification Dispatch History` table)
   - `DPAPI Vaults` (read-only key names, vault sizes, and `(UTC)` timestamps — never exposes plaintext secrets)
4. **`Settings` (`Ctrl+S`):** Dedicated visual settings screen (**zero** raw `Key=Value` fields):
   - Sentence case toggles: *Use global format rules*, *Allow Tailscale bind (100.64.0.0/10)*, *Auto-align sister app schedules*
   - Segmented **Post-Run Power Policy** picker (`StayAwake`, `Sleep`, `Shutdown`) and numeric steppers (`Cold-Wake Idle (min)`, `Poll Interval (ms)`)
   - **`Save Settings (Ctrl+S)`**, **`Export Settings JSON`**, and the **20-Snapshot Rollback Table** (`Rollback` button on any retained snapshot)

### 4.3 Docked Terminal (`Ctrl+\``), Dialogs (`Ctrl+K`, `?`), Tables & Tooltips
- **Docked Terminal (`Ctrl+\``):** Toggles the bottom `<foundation-terminal>` drawer (`S` / `M` / `L` height presets) streaming real-time SSE `log_line` events with Clear and Stop All controls.
- **Omni-Search (`Ctrl+K`) & Hotkeys (`?`):** Native `<dialog closedby="any">` overlays (`Escape` or backdrop click to dismiss) to jump directly to any route or sister app (`Ctrl+K`) or view all shortcuts (`?`).
- **Sortable Accessible Tables:** Column headers sort deterministically (`Intl.Collator` + `moveBefore`), support row **Bulk Select** checkboxes, and include **`Export CSV`** / **`Export JSON`** toolbars.
- **2-Line Styled Tooltips:** Hovering or focusing primary controls shows clean 2-line styled tooltips (`data-tooltip-title` / `data-tooltip-body`) with zero native `title=""` clutter.

---

## 5. Everyday Workflows

### Workflow 1: Switching an App Between `Warm` and `Cold` (`0 MB` Idle RAM)
1. In **`Fleet Overview`**, click any sister app row to open the **Slide-Over Right Drawer**.
2. Click **`Warm`**, **`Cold`**, or **`Disabled`** in the **Residency Mode** segmented picker.
3. `Cold` apps stay stopped (`0 MB` RAM) until you click their outer tab (`Alt+2..7`), click **`Wake`**, or hit `/api/v1/apps/<appId>/*`.

### Workflow 2: Safe Overnight Post-Run Sleep or Shutdown
1. Open **`Doctor & Power`** $\rightarrow$ **`Power`** (or **`Settings`**) and select **`Sleep`** (or **`Shutdown`**).
2. While any sister app has active/queued jobs or a live `.hklock` hold, `Mother` inhibits sleep (`SLEEP INHIBITED: YES`).
3. Once all apps hit `0` active/queued jobs and `0` live locks, a **60-second cancellable countdown** begins (`SLEEP 60S` in the topbar; click **`Cancel Countdown`** anytime to abort back to `StayAwake`).

### Workflow 3: Resolving Drive Contention & Coexistence Drift
1. Open **`Traffic Cop`**. If dead-PID `.hklock` files appear as `STALE`, click **`Purge Stale Locks`**.
2. Switch to the **`Coexistence Conflicts`** pill and click **`Apply Safe-Stagger`** to align presets and update `<SharedRoot>\.coexistence.json`.

---

## 6. System Tray Sentinel & Master Watchdog (`\Mother_Watchdog`)

- **Single System Tray Icon (`Global\Mother_Supervisor_Active`):** Right-clicking `Mother`'s tray icon lets you open the Control Hub, switch directly to a sister tab, run `Doctor`, or cleanly exit the suite daemon. When `Explorer` restarts, `TaskbarCreated` automatically restores the tray icon.
- **`\Mother_Watchdog`:** `Mother` centralizes Task Scheduler recovery in a single `\Mother_Watchdog` entry and monitors child daemons with a `3 crashes in 60s` circuit breaker.

---

## 7. Troubleshooting & FAQ

| Question / Symptom | Root Cause | Solution / Remediation |
| :--- | :--- | :--- |
| **A newly installed sister app is missing from `Mother`'s tab bar** | Its manifest was added after boot. | Click **`Reload Manifests`** in **`Fleet Overview`** (`POST /api/v1/suite/apps/reload`). |
| **A `.hklock` entry stays listed after an app crashed** | The previous process exited without deleting its lockfile. | Open **`Traffic Cop`** $\rightarrow$ **`Active Locks`** (status shows `STALE`) and click **`Purge Stale Locks`**. |
| **Why didn't `Mother` sleep my PC after one app finished?** | Another sister app still had active/queued jobs or a live `.hklock`. | Check **`Fleet Overview`** and **`Traffic Cop`**; `Mother` waits until every registered app is idle before starting the 60s countdown. |
| **How do I revert a bad settings change?** | Saved changes are stored in `data\config_history\`. | Open **`Settings`** (`Ctrl+S` screen) and click **`Rollback`** on any of the last 20 snapshots. |
| **How do I inspect daemon logs quickly?** | Live logs stream into the docked terminal and `logs\mother-YYYYMMDD.jsonl`. | Press **`Ctrl+\``** to toggle the terminal drawer, or run `Mother.exe --diagnose` to inspect health & crash bundles. |

---

## 8. Plain-Language Glossary

| Term | Plain-English Definition |
| :--- | :--- |
| **Shared Suite Daemon (`Mother :5100`)** | The single master background coordinator and reverse-proxy gateway (`http://127.0.0.1:5100/`) supervising all installed sister apps. |
| **`Warm` / `Cold` / `Disabled` Residency** | `Warm` keeps an app running for instant access; `Cold` keeps it at `0 MB` RAM and wakes it on demand; `Disabled` keeps it parked. |
| **Traffic Cop & `.hklock`** | `ResourceTrafficCopService` + `HkLockScanner`, which inspect PID-safe `.hklock` files to prevent drive thrashing and purge dead-PID stale locks. |
| **Post-Run Power Coordinator** | Central `StayAwake` / `Sleep` / `Shutdown` controller that inhibits OS sleep until all sister apps finish and runs a 60s cancellable countdown. |
| **Coexistence Broker** | `FleetCoexistenceArbitrator`, which compares sister-app rules and `.coexistence.json` files and applies safe presets with **`Apply Safe-Stagger`**. |
| **`\Mother_Watchdog`** | Single Windows Task Scheduler recovery task replacing per-app watchdogs across the suite. |
