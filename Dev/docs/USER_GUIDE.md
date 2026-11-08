# Mother — End-User Manual & Fleet Supervisor Guide (`USER_GUIDE.md`)

> **Active Baseline Version:** `v0.1.0` (`v0.2.0` In-Dev) | **Full SemVer (`MAJOR.MINOR.PATCH`)** | **Owner:** `@tessa` ("The Manual Maker") | **Verified By:** `@dean` ([Standard 16 §3.5](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/16_PROJECT_DOSSIERS_AND_FEATURE_TRACKING.md))

Welcome to the **`Mother`** User Guide. This manual explains what `Mother` (`:5100`) does, how to install and upgrade it, how to navigate its single-window outer tab bar and `🏛️ Fleet Overview` dashboard, and how to use it to supervise, schedule, and align all six of your sister applications (**Housekeeping**, **ShadowLibrary**, **Redroom**, **Greenroom**, **StreamEngine**, and **legacy-sister-app**).

---

## 1. What It Is & Who It's For

**`Mother`** is the Single Shared Suite Daemon (`http://127.0.0.1:5100/`), unified system tray sentinel, reverse-proxy gateway, 3:00 AM disk traffic cop, post-run power coordinator, and cross-app settings broker for your workstation.

### What Problems `Mother` Solves for You
1. **One System Tray Icon & One Window Instead of Six:** Instead of six separate background tray icons and six separate browser tabs competing for attention, `Mother` hosts your entire suite under a single **Titanium Sky (`#38bdf8`)** outer horizontal tab bar (`Alt+1..7`)—while still letting you pop any app out into its own standalone desktop window (`--window`) whenever you want it on a second monitor.
2. **Partial Suite Freedom (Install 1 App or All 6):** You never have to install apps you don't use. If you only install `Housekeeping` and `Greenroom`, `Mother` only shows tabs and controls for `Housekeeping` and `Greenroom`.
3. **Zero 3:00 AM Drive Thrashing:** When two apps schedule heavy disk jobs on the same physical hard drive at the same time (for example, a `ShadowLibrary` bit-rot scrub on `E:` while `Housekeeping` tries to repack archives on `E:`), `Mother`'s **Schedule Traffic Cop** automatically defers the second job for 30 minutes until the drive is free.
4. **Safe Post-Run Sleep & Shutdown:** Instead of one app putting your PC to sleep while another app is halfway through ripping a disc or recording a stream, `Mother`'s **Post-Run Power Coordinator (`StayAwake / Sleep / Shutdown`)** waits until **every** installed sister app has `0` active and `0` queued jobs (and gives you a 60-second cancel toast) before sleeping or shutting down your PC.
5. **1-Click Cross-App Settings Alignment:** `Mother` checks whether two sister apps have conflicting settings on the same folder and lets you align them with a single click.

---

## 2. Installation / SDK Consumption & Upgrade

### 2.1 Standalone Installation (`Mother-Setup-v0.1.0-win-x64.exe`)
1. Launch **`Mother-Setup-v0.1.0-win-x64.exe`** from `<WORKSPACE_ROOT>/Installers\` (or `<WORKSPACE_ROOT>/Mother\Installers\`).
2. The **`650×434` Deep Slate Dark Setup Wizard** opens with the **Titanium Sky (`#38bdf8`)** header badge (`CLEAN INSTALL` or `UPGRADE DETECTED`):
   - **Destination Folder:** Defaults to `<PROGRAMS_ROOT>/Mother`.
   - **Checkboxes:**
     - *Create Desktop and Start Menu shortcuts* (checked by default)
     - *Start Mother automatically with Windows* (registers `\Mother_Watchdog`)
     - *Launch Mother when setup finishes* (checked by default)
3. Click **`Install`** (or **`Upgrade`**). Click **`Show Details ▾`** if you want to expand the window (`650×544`) and watch the live file extraction log.
4. When the button turns emerald green (**`Finish`**), click it to launch `Mother`.

### 2.2 Automatic Co-Bootstrapping via Sister-App Installers (`v0.2.0+`)
Whenever you install or upgrade any sister app (`Housekeeping`, `ShadowLibrary`, `Redroom`, `Greenroom`, `StreamEngine`, `legacy-sister-app`), its installer automatically drops its registration manifest into `<PROGRAMS_ROOT>/Mother\manifests\<app-id>.manifest.json` and hot-reloads `Mother` (`POST /api/v1/suite/apps/reload`) without interrupting running jobs.

### 2.3 Upgrading & Uninstalling
- **Upgrading:** Running a newer `Mother-Setup-v<Version>.exe` over `<PROGRAMS_ROOT>/Mother\` automatically detects your existing installation (`UPGRADE DETECTED`), replaces only `app\` and `scripts\`, and preserves all your `manifests\`, `data\`, `logs\`, and 20-snapshot config history.
- **Uninstalling:** Run `<PROGRAMS_ROOT>/Mother\Mother-Uninstall.exe` (or use Windows *Installed Apps*). Your `data\` and `logs\` are preserved by default unless you explicitly check the red *Delete all user `data\`, `logs\`, and databases (destructive)* box.

---

## 3. First-Run Setup / Integration Quickstart

When `Mother` launches for the first time at `<PROGRAMS_ROOT>/Mother\Mother.exe` (`http://127.0.0.1:5100/`):
1. **Automatic Sister-App Discovery:** `Mother` immediately scans `<PROGRAMS_ROOT>/Mother\manifests\*.manifest.json` and `<PROGRAMS_ROOT>/*\` (`AppDiscoveryService`), discovering every installed sister app whether it uses the new `v2` `suite.manifest.json` or legacy `v1` `mother.manifest.json` format.
2. **Residency Startup (`Warm` vs `Cold`):**
   - Installed apps configured as **`Warm`** (the default) are started automatically in the background (`--managed-by-mother --bind 127.0.0.1`) with their standalone tray icons hidden so only `Mother`'s master tray icon appears.
   - Apps configured as **`Cold`** stay at **`0 MB` RAM** until you click their tab, call their API, or their scheduled run time arrives.
3. **Fleet Coexistence Check:** Open the first tab (`🏛️ Fleet Overview`, **`Alt+1`**) and review the **Fleet Coexistence & Shared Storage Matrix** card. If any conflict banner is shown across your shared drives, click **`Align Fleet Settings`** to synchronize your sister apps in one click.

---

## 4. Dashboard / Component & SDK Surface Tour

`Mother`'s interface is organized along **two orthogonal axes** ([`v0.1.0.2026-11-04.md#L25`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-04.md#L25)) so it never collides with the left sidebar inside your sister apps:

### 4.1 The Outer Horizontal Command Bar (Always Visible at the Top of `Mother`)
| Control / Element | Hotkey | What It Does in Plain English |
| :--- | :---: | :--- |
| **`🏛️ Fleet Overview` Tab** | `Alt+1` | Opens `Mother`'s master supervisor dashboard, schedule traffic cop, coexistence broker, and `🩺 Fleet System Doctor`. |
| **Discovered Sister-App Tabs** (`🧹 Housekeeping`, `🟢 Greenroom`, `📚 ShadowLibrary`, `🔴 Redroom`, `🎮 legacy-sister-app`, `📡 StreamEngine`) | `Alt+2` .. `Alt+7` | Switches immediately to that sister app's full UI inside `Mother`. Each tab displays a live status dot (`🟢 Idle`, `🔵 Busy %`, `🟡 Paused`, `⚪ Cold Standby`, `🔴 Error`) and right-click context menu (`Pop Out to Own Window`, `Residency: Warm / Cold / Disabled`, `Restart App`). Inactive tabs automatically suspend (`TrySuspendAsync()`) so `Mother` uses `< 35 MB` of base RAM. |
| **`🛑 Fleet Stop All`** | — | Emergency stop across the entire workstation: broadcasts `POST /api/v1/jobs/stop-all` to every running sister app, clearing queues and terminating child helper tools (`7z`, `ffmpeg`, `chdman`). |
| **`⏸ Fleet Pause / ❄ Suspend All`** | — | Pauses all sister-app FIFO queues or freezes active child worker processes (`NtSuspendProcess`) so you can reclaim 100% CPU/disk immediately for gaming or live streaming. |
| **`🧪 Fleet Dry Run`** | — | Toggles global simulation mode across all sister apps so you can preview what scheduled jobs would do without modifying files on disk. |
| **`⚡ Post-Run: StayAwake / Sleep / Shutdown`** | — | Global power action selector. When set to `Sleep` or `Shutdown`, `Mother` waits until every installed sister app finishes its active and queued jobs, shows a 60-second cancellable toast, and then puts the PC to sleep or shuts it down. |
| **`📱 Tailscale QR`** | — | Opens an offline SVG QR code modal so you can pair your phone or tablet over LAN (`:5100`) or Tailscale HTTPS (`:8500`) and install either the full Suite PWA or individual sister-app PWAs to your mobile home screen. |

### 4.2 Inside `🏛️ Fleet Overview` (Collapsible Left Sidebar Rail `Ctrl+B` + Main Pane)
When `🏛️ Fleet Overview` (`Alt+1`) is active, the left vertical sidebar (`220px` expanded / `56px` collapsed via **`Ctrl+B`**) provides 6 dedicated supervisor views (`1..6`):
1. **`Overview` (`1`):** Live cards for every installed sister app showing residency mode (`Warm` / `Cold` / `Disabled`), memory usage, active job progress, target drives, and the **Fleet Coexistence & Shared Storage Matrix** with 1-click **`Align Fleet Settings`** and the opt-in **`Auto-Align Shared Settings Across Fleet`** toggle (`default: false`).
2. **`Fleet Schedules & Traffic Cop` (`2`):** Unified timeline of all recurring schedules (`Daily / Weekly / Monthly / Interval`) across every sister app, active `<Dir>\.<app>_<pid>.hklock` drive locks, and a history table of automatic 30-minute Traffic Cop deferrals (`📥 Export CSV / JSON`).
3. **`Unified Logs` (`3` & `Ctrl+\``):** Aggregated real-time log stream combining `Mother` and all supervised sister daemons with app/level filters (`ALL`, `INFO`, `WARN`, `ERROR`). Also accessible from any view via the docked resizable **`Ctrl+\``** terminal drawer (`S` `25%` / `M` `40%` / `L` `60%`).
4. **`Notifications` (`4`):** Configure Windows Tray Toast alerts and Discord Rich Embed webhooks (`#38bdf8` Titanium Sky) for fleet events (`Daemon Crash/Restart`, `Fleet Queue Complete`, `Pre-Sleep Countdown`, `Schedule Deferred`), plus a 1-click **`Send Test Notification`** button.
5. **`Config Snapshots` (`5` & `Ctrl+S`):** Browse and restore any of the last **20 automatic configuration snapshots** (`data\config_history\`) or export/import your fleet configuration as JSON.
6. **`🩺 Fleet System Doctor` (`6`):** Runs a comprehensive health check across `Mother` and all installed sister apps (binary versions, SQLite integrity, external tools, drive write permissions, port health, crash counts) with 1-click **`Backup All DBs (.7z)`** and **`Export Fleet Diagnostic Bundle (.zip)`**.

---

## 5. Everyday Workflows — Fleet Supervision, Job Arbitration, Shared UI/Backend Primitives

### Workflow 1: Switching an App Between `Warm` (Instant) and `Cold` (`0 MB` Idle RAM)
If you use `Greenroom` and `Housekeeping` every day, keep them **`Warm`**. If you only use `Redroom` when ripping physical discs once a week, set it to **`Cold`**:
1. In `🏛️ Fleet Overview` -> `Overview` (or by right-clicking the `🔴 Redroom` tab in the top bar), change **Residency Mode** from `Warm` to **`Cold`**.
2. `Mother` gracefully stops the `Redroom` background worker once it is idle (`0 MB` RAM, `0%` CPU).
3. The next time you click the `🔴 Redroom` tab (or when a `Redroom` schedule comes due), `Mother` automatically wakes `Redroom` in the background (`250ms` health poll) and opens the UI seamlessly.

### Workflow 2: Popping a Sister App Out to Its Own Monitor (`--window`)
Want `Greenroom` on Monitor 1 and `Housekeeping` on Monitor 2 at the same time?
1. Right-click any app tab in `Mother`'s outer top bar and select **`🗗 Pop Out to Own Window`** (or run `<PROGRAMS_ROOT>/Housekeeping\Housekeeping.exe --window`).
2. A dedicated desktop window opens for that app with its own taskbar icon and saved window position—while still powered underneath by `Mother`'s single shared daemon (`:5100`) with zero duplicate background processes.

### Workflow 3: Running Overnight Jobs with Automatic Post-Run PC Sleep
1. Queue your overnight ROM verification in `ShadowLibrary` and media transcoding in `Housekeeping`.
2. In `Mother`'s top horizontal command bar, change **`⚡ Post-Run`** from `StayAwake` to **`Sleep`** (or **`Shutdown`**).
3. Go to bed! `Mother`'s **Schedule Traffic Cop** will prevent drive collisions if both apps touch the same drive, and `Mother`'s **Post-Run Power Coordinator** will wait until **every** sister app hits `0` active and `0` queued jobs, display a 60-second cancellable notification toast (+ Discord alert), and then put your PC to sleep safely.

---

## 6. Notifications, Tray/Daemon Controls & Scheduling

### 6.1 The Single System Tray Sentinel (`Global\Mother_Supervisor_Active`)
Right-clicking `Mother`'s Titanium Sky icon in the Windows system tray gives you instant access to:
- **Aggregate Fleet Status Header:** Shows `Fleet Idle (6/6 Online)` or `Fleet Busy — 2 Active Jobs (64%)`.
- **`Open Mother Suite Window`** (or double-click tray icon).
- **Per-App Submenus** for each installed sister app:
  - `Open in Suite Tab` / `Pop Out to Own Window` / `Open in Browser`
  - `Residency: Warm (Resident)` / `Cold (0 MB Auto-Wake)` / `Disabled`
  - `Restart Worker` / `Stop App Jobs`
- **Quick Fleet Actions:** `⚡ Post-Run: StayAwake / Sleep / Shutdown`, `⏸ Pause All Queues`, `🛑 Fleet Stop All`, `🩺 Run Fleet System Doctor`, and `Exit Mother & Stop Fleet`.

### 6.2 Master Watchdog (`\Mother_Watchdog`) & Schedule Traffic Cop
- **`\Mother_Watchdog`:** `Mother` owns the single Windows Task Scheduler watchdog entry for the entire suite (`\Mother_Watchdog`), polling child `/api/v1/health` endpoints every 3 seconds and auto-restarting any crashed daemon (up to 3 times in 60 seconds before tripping the crash-loop circuit breaker).
- **Cold-Wake Scheduling & Drive Deferral:** Even if a sister app is set to **`Cold`** (`0 MB` idle), `Mother` reads its `schedules[]` array from `manifests\<app-id>.manifest.json`, wakes the app when its UTC schedule (`RunIfMissed`) comes due, and automatically issues a 30-minute `/api/v1/scheduler/defer` if another app is already holding a `.hklock` or running a heavy job on the target drive.

---

## 7. Troubleshooting & FAQ

| Question / Symptom | Root Cause | Solution / Remediation |
| :--- | :--- | :--- |
| **A newly installed sister app doesn't appear in `Mother`'s top tab bar** | The app's manifest was not yet reloaded or is missing from `<PROGRAMS_ROOT>/Mother\manifests\`. | Open `🏛️ Fleet Overview` and click **`Reload Installed Apps`** (`POST /api/v1/suite/apps/reload`), or copy `<AppRoot>\app\suite.manifest.json` to `<PROGRAMS_ROOT>/Mother\manifests\<app-id>.manifest.json`. |
| **A sister-app tab shows a red dot (`Crash-Loop Circuit Breaker Tripped`)** | The child daemon crashed 3 times within 60 seconds, so `Mother` paused auto-restarts to protect your system. | Press **`Ctrl+\``** to inspect the child app's last 50 log lines and `logs\crashes\crash_*.json` in the Unified Log Drawer, fix the underlying issue (e.g. a locked port or missing drive), and click **`Reset Circuit Breaker & Restart`** on the app card. |
| **Why was my 3:00 AM scheduled scan delayed by 30 minutes?** | Another sister app was actively running a job or holding a `.hklock` on the same physical drive (`targetDrives`). | Working as designed: check `🏛️ Fleet Overview` -> `Fleet Schedules & Traffic Cop` to see the exact deferral reason (`Suite Drive I/O Mutex`). |
| **Why didn't `Mother` put my PC to sleep when `Housekeeping` finished?** | Another registered sister app (such as `StreamEngine` recording a VOD or `ShadowLibrary` running a queued audit) still had `activeJobs > 0` or `queuedJobs > 0`. | `Mother` never sleeps the PC while any sister app is still working. Check the top bar or tray icon to see which app is still busy. |
| **Both `Mother` and a sister app are showing system tray icons** | The sister app was launched manually without `--managed-by-mother` before `Mother` started. | Restart the sister app from `Mother`'s `🏛️ Fleet Overview` card (or run `Mother`), which passes `--managed-by-mother` and sets `Global\Mother_Supervisor_Active` so only `Mother`'s single tray icon is shown. |

---

## 8. Plain-Language Glossary

| Term | Plain-English Definition |
| :--- | :--- |
| **Shared Suite Daemon (`Mother :5100`)** | The single master background coordinator and web gateway (`http://127.0.0.1:5100/`) that hosts and supervises all installed sister applications. |
| **`Warm` Residency** | Keeps a sister app running in the background for instant `0ms` response when you click its tab. |
| **`Cold` Residency (`Auto-Wake`)** | Keeps a sister app completely stopped (`0 MB` RAM) when idle, automatically waking it up when you click its tab, call its API, or when a scheduled task is due, and hibernating it 15 minutes after jobs finish. |
| **Schedule Traffic Cop** | `Mother`'s disk I/O arbitrator (`FleetScheduleTrafficCop`) that checks `/api/v1/scheduler/status` and `.hklock` files so two apps never thrash the same hard drive at the same time. |
| **Post-Run Power Coordinator** | The global `StayAwake / Sleep / Shutdown` controller in `Mother`'s top bar that waits until **all** installed sister apps finish their queues (+ 60s cancel toast) before sleeping or shutting down the PC. |
| **4-Pillar Coexistence Broker** | `Mother`'s cross-app settings inspector (`FleetCoexistenceArbitrator`) that detects conflicting folder/archive rules across sister apps and aligns them with 1 click. |
| **Orthogonal 2-Axis Shell** | `Mother`'s layout design where `Mother` owns the **Top Horizontal Bar** (`Alt+1..7` app tabs) and each sister app owns its **Left Vertical Sidebar Rail** (`Ctrl+B`), preventing double-tab clutter. |
| **`\Mother_Watchdog`** | The single Windows Task Scheduler entry managed by `Mother` that keeps the suite alive and replaces individual per-app watchdogs. |
