# Mother — End-User Manual & Fleet Supervisor Guide (`USER_GUIDE.md`)

> **Active Baseline Version:** `v0.3.0` (`MPL-2.0` — Next Horizon: `v0.4.0`) | **Full SemVer (`MAJOR.MINOR.PATCH[-tag]`)** | **SDK Pin:** `Foundation v0.5.1`

Welcome to the **`Mother`** User Guide. This manual explains what `Mother` (`:5100`) does, how to install, run portably, or upgrade it inside your `StreamItAll\Mother` suite umbrella, how to launch it via CLI or tray, how to navigate its Titanium Sky (`#38bdf8`) single-window shell, and how to supervise, schedule, and align `0..N` dynamically discovered suite applications.

---

## 1. What It Is & Who It's For

**`Mother`** is a universal, pluggable Windows suite daemon (`http://127.0.0.1:5100/`), unified system tray sentinel, reverse-proxy API gateway, disk I/O traffic cop, autonomous Cold-Wake scheduler, post-run power coordinator, and cross-app coexistence broker. It ships with **zero hardcoded application lists**—fresh installs start cleanly in a **0-App Empty State** and scale automatically as you add manifests or install suite apps.

### What Problems `Mother` Solves for You
1. **One System Tray Icon & One Window Instead of Clutter:** Instead of managing separate background tray icons and scattered browser tabs, `Mother` hosts your entire suite under a single **Titanium Sky (`#38bdf8`)** outer horizontal tab strip (`Alt+1..9`)—while still letting you pop any HTTP-enabled app out into a standalone desktop window (`--window`) or browser (`--browser`).
2. **Zero-to-Many Pluggable Freedom (`0..N` Apps):** You never have to install apps you do not use. With `0` manifests, `Mother` displays an actionable onboarding card and passes all `Fleet Doctor` checks (`overall: "pass"`). As soon as you drop manifests into `data\manifests\` or install compatible suite apps, `Mother` discovers them automatically by strict precedence.
3. **Zero 3:00 AM Drive Thrashing:** When two apps target the same physical drive or hold a `.hklock` sentinel, `Mother`'s **Traffic Cop** distinguishes live vs. dead-PID stale locks and records schedule deferrals (`POST /api/v1/scheduler/defer`) so drives never thrash.
4. **Autonomous Cold-Wake & Idle Hibernation:** `Cold` (`on-demand`) apps stay at `0 MB` RAM until clicked, requested through `/api/v1/apps/<app-id>/*` or `/app/<app-id>/`, or triggered by a `schedules[]` entry (with `runIfMissed` catch-up), and hibernate automatically after `idleTimeoutMinutes` (`15m` default) once active and queued jobs reach zero. Headless/CLI apps (`internalPort <= 0`) stay supervised without exposing an HTTP tab and safely return `503 NO_HTTP_ENDPOINT` if an HTTP route is dialed.
5. **Safe Post-Run Sleep & Shutdown:** Instead of one app sleeping your PC while another is mid-job, `Mother`'s **Post-Run Power Coordinator (`StayAwake`, `Sleep`, `Shutdown`)** waits until every registered app reports `0` active/queued jobs and `0` live `.hklock` holds (followed by a configurable idle wait and 60-second cancellable countdown) before sleeping or shutting down.
6. **1-Click Cross-App Alignment & Non-Destructive Settings v2:** `Mother` inspects shared root `.coexistence.json` (`v2`) manifests with 1-click **`Apply Safe-Stagger`**, and non-destructively migrates legacy `data\settings.json` configurations to `data\config\settings.json` (`schemaVersion: 2`) while preserving rollback history and custom keys.

---

## 2. Installation, Portable Usage, Upgrading & Uninstalling

### 2.1 Standalone Windows Setup (`Mother-Setup-v0.3.0-win-x64.exe`)
`Mother` ships self-contained for `win-x64`—you do **not** need to pre-install a separate .NET Desktop Runtime.
1. Launch **`Mother-Setup-v0.3.0-win-x64.exe`**.
2. The **4-Pillar Dark Setup Wizard** (Titanium Sky `#38BDF8`, powered by `Foundation v0.5.1` `StandardInstallerHost` and embedded `installer.manifest.json` v2) opens:
   - **Destination Folder (`--install-dir`):** Defaults to `%LOCALAPPDATA%\Programs\StreamItAll\Mother\Mother\` (Clean Release Root containing `Mother.exe`, `Mother-Uninstall.exe`, `app\`, and `scripts\`, with `app\LICENSE`).
   - **Shared Suite Umbrella Directory (`--suite-dir`):** Defaults to `%LOCALAPPDATA%\Programs\StreamItAll\Mother\` where installed suite applications reside alongside `Mother\`.
   - **Install Profile (`--profile`):** Choose **`full`** (default; Complete Clean Release Root with Web UI & scripts), **`lightweight`** (core suite gateway & supervisor), or **`custom`**.
   - **Options:** Desktop and Start Menu shortcuts (HKCU suite + uninstall metadata always registers for live installs), Windows sign-in autostart (`--autostart` launching `--tray-only` + `\Mother_Watchdog`), and opt-in **`--migrate-legacy`** for upgrading legacy flat installs.
3. Click **`Install Now`** (or **`Upgrade Now`**). Expand details to watch the live extraction log, then click **`Finish`**.

### 2.2 Portable ZIP (`Mother-v0.3.0-win-x64-Portable.zip`)
1. Extract **`Mother-v0.3.0-win-x64-Portable.zip`** to any folder. (The portable archive contains `Mother.exe`, `app\`, `scripts\`, and release docs—never an installer executable.)
2. Start **`Mother.exe`** directly, or pass explicit runtime directories (for example: `Mother.exe --tray-only --data-dir .\data --log-dir .\logs --port 5100`).

### 2.3 Upgrading & Uninstalling
- **Upgrading:** Running a newer `Mother-Setup-v<Version>-win-x64.exe` detects the existing installation, creates a pre-upgrade backup under `backups\`, replaces only `Mother.exe`, `app\`, and `scripts\`, and preserves `data\` (`config\settings.json`, `manifests\`, `mother.db`, `config_history\`) and `logs\`.
- **Uninstalling:** Run `Mother-Uninstall.exe` inside your installation folder (or remove via Windows *Installed Apps*). Your `data\` and `logs\` directories are preserved by default unless you pass `--purge-data`.
- **Automated Setup CLI Flags:** `--install-dir <dir>`, `--suite-dir <dir>`, `--profile <lightweight|full|custom>`, `--migrate-legacy`, `--autostart`, `--silent`, `--quiet`, `--verify`, `--uninstall`, `--purge-data`, `--dry-run`, `--data-dir <dir>`, `--log-dir <dir>`, `--version`, `--help`.

### 2.4 Sister-App Discovery Precedence & Co-Bootstrapping
`Mother` evaluates manifest sources in strict **highest-to-lowest first-wins precedence** (`TryAdd` by `appId`):
1. `<DataDir>\manifests\*.json` (operator overrides—always wins over discovered installs)
2. `<ProgramsRoot>` umbrella sibling folders (`StreamItAll\Mother\<App>\suite.manifest.json`, `mother.manifest.json`, or `app\...`)
3. Registry metadata (`HKCU\Software\StreamItAll\Mother\Apps\<App>\InstallDir`)
4. Configured `customManifestRoots` in `data\config\settings.json`
5. Legacy `<Drive>:\Programs\<App>\` read-only scan (strictly skipped during isolated or sandbox runs)

Both `v2` `camelCase` (`suite.manifest.json`) and legacy `v1` `snake_case` (`mother.manifest.json`) schemas are normalized automatically. After adding a new manifest or installing a compatible app, click **`Reload Manifests`** in **`Fleet Overview`** (or run `reload` in the docked terminal / `POST /api/v1/suite/apps/reload`) to add tabs live without restarting `Mother`.

---

## 3. First-Run Setup, WebView2 Fallback & CLI Modes

When `Mother.exe` starts (`http://127.0.0.1:5100/`):
1. **Non-Destructive Settings Migration (`v1 -> v2`):** On startup, `SettingsMigrationService` checks for legacy `data\settings.json`. If found, it migrates your settings atomically to `data\config\settings.json` (`schemaVersion: 2`), renames the old file to `data\settings.json.migrated.bak`, upgrades legacy key names, and preserves any third-party custom keys.
2. **Embedded WebView2 & Fallback Panel:** `Mother` loads its bundled `WebView2Loader.dll` (`app\runtimes\win-x64\native\`) and stores browser data in `data\webview2\`. If the Microsoft Edge WebView2 Evergreen Runtime is not installed on your system, `Mother` displays an interactive dark-theme **Desktop Fallback** card showing the local URL with **`Open in Browser`**, **`Get WebView2 Runtime`**, and **`Retry`** buttons so your suite remains immediately usable.
3. **Residency Modes (`Warm` / `Cold` / `Disabled`):** `Warm` (`always-on`) workers start with `Mother`; `Cold` (`on-demand`) workers remain at `0 MB` RAM until woken by tab/API/schedule; `Disabled` workers remain stopped.
4. **CLI Launch & Containment Modes:**
   - `Mother.exe --suite-tab <appId>` — Focuses the running `Mother` shell and switches to `<appId>` (or `route:fleet|traffic-cop|doctor|settings`).
   - `Mother.exe --window <appId>` / `Mother.exe --browser [<appId>]` — Opens a registered app in a dedicated desktop window or default web browser.
   - `Mother.exe --tray-only` *(or `--minimized`)* — Starts background supervision and tray icon without opening a window.
   - `Mother.exe --programs-dir <dir>`, `--data-dir <dir>`, `--config <file>`, `--log-dir <dir>`, `--port <port>` — Overrides runtime paths and listener port.
   - `Mother.exe --diagnose` — Prints `Fleet Doctor` JSON to stdout and exits `0` (`pass`/`warn`) or `1` (`fail`).
   - **Fail-Closed Safety Exit Codes:** Release binaries return **`ExitCode = 2`** if test-automation flags (`--automation-port` / `--automation-token`) are passed, and **`ExitCode = 3`** on isolated/sandbox path containment violations.

---

## 4. Dashboard & Interface Tour (`v0.3.0` Controls)

`Mother`'s interface separates outer suite navigation from inner application controls along two perpendicular axes:

### 4.1 Outer Horizontal Topbar (`Alt+1..9` & Global Actions)
| Control / Element | Hotkey | What It Does |
| :--- | :---: | :--- |
| **Outer Suite Tab Strip** (`Mother` + `0..N` discovered apps) | `Alt+1` .. `Alt+9` | `Alt+1` opens Mother's Control Hub; `Alt+2`..`Alt+9` open each discovered app's embedded frame (`/app/<appId>/`, redirected `307` from `/app/<appId>`) with **`Wake App`** and **`Open Standalone`**. |
| **Status Chips (`PROD` / `ISOLATED`, `STAY AWAKE` / `SLEEP <N>S`)** | — | Displays live vs. isolated mode and active Post-Run Power policy or countdown timer. |
| **`QR` Button** | — | Opens the **Slide-Over Right Drawer** with an offline **ISO/IEC 18004 SVG QR code** (`#0b0e14` on `#ffffff`, generated by `/api/v1/suite/qr.svg`) for mobile PWA pairing. |
| **`Stop All` Button** | — | Emergency stop: broadcasts `POST /api/v1/suite/jobs/stop-all` across all active workers and cancels any pending sleep/shutdown countdown. |

### 4.2 Collapsible Sidebar (`Ctrl+B`), Mobile Bottom Nav (`<= 820px`) & Sub-Page Pills
Press **`Ctrl+B`** to toggle the left sidebar (`220px` <-> `56px`). On viewports `<= 820px`, navigation moves automatically to the fixed bottom bar (**`Fleet`**, **`Traffic`**, **`Doctor`**, **`Settings`**):
1. **`Fleet Overview`:** Shows KPI cards (`Registered Apps`, `Online Now`, `Warm / Cold`, `Active Conflicts`) and **`Reload Manifests`**. At `0` apps, displays the **0-App Empty State** onboarding card; once apps are discovered, renders the sortable **Supervised Sister Applications** table (`Wake` / `Stop` buttons + row click to open the **Slide-Over Right Drawer** for **`Warm` / `Cold` / `Disabled`** residency switching, `Target Drives`, `Shared Mounts`, and `Mutually Exclusive With`).
2. **`Traffic Cop`:** Three **Sub-Page Pills** (`Active Locks`, `Coexistence Conflicts`, `Schedule Deferrals`) with **`Purge Stale Locks`** and **`Apply Safe-Stagger`**.
3. **`Doctor & Power`:** Four **Sub-Page Pills** (`Doctor`, `Power`, `Notifications`, `DPAPI Vaults`) with **`Run Doctor`**, **`Auto-Repair`**, **`Backup DB`**, and **`Export Bundle`**.
4. **`Settings` (`Ctrl+S`):** Visual schema v2 editor stored in `data\config\settings.json`: *Use global format rules*, *Allow Tailscale bind (`100.64.0.0/10`)*, *Auto-align sister app schedules*, *Inhibit sleep during active lock*, *Show desktop toast notifications*, *Send Discord webhook alerts*, **Post-Run Power Policy** (`StayAwake` / `Sleep` / `Shutdown`), numeric inputs (`Idle Timeout (min)`, `Poll Interval (ms)`), **`Save Settings (Ctrl+S)`**, **`Export Settings JSON`**, and the **20-Snapshot Rollback Table**.

### 4.3 Docked Terminal (`Ctrl+\``), Dialogs (`Ctrl+K`, `?`), Tables & Mobile PWA
- **Docked Terminal (`Ctrl+\``):** Toggles the resizable bottom terminal drawer (`S` / `M` / `L` presets) streaming real-time SSE `log_line` entries and running whitelisted commands: `help`, `status`, `health`, `apps`, `traffic`, `locks`, `coexistence`, `doctor`, `logs [1..100]`, `purge-stale`, `reload`, `wake <appId>`, `stop <appId>`, and `power [StayAwake|Sleep|Shutdown]`.
- **Omni-Search (`Ctrl+K`) & Hotkeys (`?`):** Native `<dialog closedby="any">` overlays (`Escape` or backdrop click to dismiss).
- **Mobile Companion PWA (`100.64.0.0/10`):** Enable **Allow Tailscale bind (`100.64.0.0/10`)** in **`Settings`** (`Ctrl+S`), click **`QR`** in the topbar, and scan from a Tailscale-connected device (`sw.js` + `manifest.webmanifest` support home-screen install while non-loopback public/LAN IPs remain blocked).

---

## 5. Everyday Workflows

### Workflow 1: Switching an App Between `Warm` and `Cold` (`0 MB` Idle RAM)
1. In **`Fleet Overview`**, click any registered app row to open the **Slide-Over Right Drawer**.
2. Choose **`Warm`**, **`Cold`**, or **`Disabled`** under **Residency Mode**.
3. `Cold` workers remain at `0 MB` RAM until clicked in the outer tab strip (`Alt+2..9`), woken via **`Wake`** (or `wake <appId>`), requested via `/api/v1/apps/<app-id>/*` or `/app/<app-id>/`, or triggered by `schedules[]`, then hibernate after `idleTimeoutMinutes`.

### Workflow 2: Safe Overnight Post-Run Sleep or Shutdown
1. In **`Doctor & Power`** -> **`Power`** (or **`Settings`**), choose **`Sleep`** or **`Shutdown`** (or run `power Sleep` in the docked terminal).
2. While any worker reports active/queued jobs or a live `.hklock`, `Mother` inhibits OS sleep (`SLEEP INHIBITED: YES`).
3. Once all apps reach `0` active/queued jobs and `0` live locks (after `postRunIdleSeconds`), a **60-second cancellable countdown** runs (`SLEEP 60S` in the topbar; click **`Cancel Countdown`** anytime to reset to `StayAwake`).

### Workflow 3: Resolving Drive Contention & Coexistence Drift
1. Open **`Traffic Cop`**. If crashed-process `.hklock` files show `STALE`, click **`Purge Stale Locks`** (or run `purge-stale`).
2. Review automatic or API-submitted deferrals under **`Schedule Deferrals`**, and click **`Apply Safe-Stagger`** under **`Coexistence Conflicts`** to write aligned `<SharedRoot>\.coexistence.json` (`schemaVersion: 2`) presets.

---

## 6. Troubleshooting & Keyboard Shortcuts

| Question / Symptom | Root Cause | Solution / Remediation |
| :--- | :--- | :--- |
| **Fresh install shows zero app tabs** | No manifests registered yet (expected 0-App Empty State). | Place a `suite.manifest.json` (or `<appId>.json`) in `data\manifests\` or install a suite app into the umbrella folder, then click **`Reload Manifests`**. |
| **`/app/<appId>/` returns `503 NO_HTTP_ENDPOINT`** | Registered worker is a headless/CLI app (`internalPort <= 0`). | Expected fail-closed guard; supervise via schedules or terminal commands instead of HTTP UI proxy. |
| **A `.hklock` stays listed after a crash** | Previous process exited before deleting its lockfile. | Open **`Traffic Cop`** -> **`Active Locks`** (`STALE`) and click **`Purge Stale Locks`** (or run `purge-stale`). |
| **How do I revert a bad settings change?** | Changes snapshot automatically into `data\config_history\`. | Open **`Settings`** (`Ctrl+S`) and click **`Rollback`** on any of the last 20 snapshots. |
| **How do I inspect logs from disk or terminal?** | Rolling logs write to `logs\mother-YYYYMMDD.jsonl`. | Press **`Ctrl+\``** and run `logs 50`, run `scripts\interactive\tail-logs.bat`, or run `Mother.exe --diagnose`. |
| **Embedded window shows WebView2 Fallback** | Edge WebView2 Evergreen Runtime is missing on the host. | Click **`Open in Browser`** to keep working immediately, or click **`Get WebView2 Runtime`** and then **`Retry`**. |
| **Why is my phone blocked over local Wi-Fi?** | Only loopback (`127.0.0.1`) and optional Tailscale CGNAT (`100.64.0.0/10`) are allowed. | Enable **Allow Tailscale bind** in **`Settings`**, scan the **`QR`** code, and connect over Tailscale rather than raw LAN IP. |

| Shortcut | Action |
| :--- | :--- |
| **`Alt+1` .. `Alt+9`** | Switch between `Mother` Control Hub (`Alt+1`) and dynamically discovered app tabs (`Alt+2`..`Alt+9`). |
| **`Ctrl+B`** | Collapse or expand the desktop navigation sidebar (`220px` <-> `56px`). |
| **`Ctrl+\``** | Toggle the bottom docked diagnostic terminal (`S` / `M` / `L` height presets). |
| **`Ctrl+K`** | Open **Omni-Search** modal to jump across routes and discovered apps. |
| **`Ctrl+S`** | Save current **`Settings`** configuration (`data\config\settings.json` v2 + snapshot). |
| **`?`** | Open the keyboard shortcut cheat-sheet modal (`Escape` or backdrop click to close). |

---

## 7. Plain-Language Glossary

| Term | Plain-English Definition |
| :--- | :--- |
| **Shared Suite Daemon (`Mother :5100`)** | The single background coordinator and reverse-proxy gateway (`http://127.0.0.1:5100/`) supervising `0..N` installed applications. |
| **`Warm` / `Cold` / `Disabled` Residency** | `Warm` (`always-on`) starts with `Mother`; `Cold` (`on-demand`) stays at `0 MB` RAM until requested or scheduled and hibernates when idle; `Disabled` stays stopped. |
| **Traffic Cop & `.hklock`** | `ResourceTrafficCopService` + `HkLockScanner`, which inspect PID-safe `.hklock` sentinels to prevent drive thrashing, purge dead-PID stale locks, and track `/api/v1/scheduler/defer` deferrals. |
| **Post-Run Power Coordinator** | Central `StayAwake` / `Sleep` / `Shutdown` controller that inhibits OS sleep until all supervised workers finish and runs a 60s cancellable countdown. |
| **Coexistence Broker** | `FleetCoexistenceArbitrator`, which evaluates mutual exclusions, drive locks, and `.coexistence.json` (`v2`) parity and applies safe presets via **`Apply Safe-Stagger`**. |
| **`\Mother_Watchdog`** | Single Windows Task Scheduler recovery task replacing per-app watchdogs across the suite. |

---

## License & Legal

`Mother` is licensed under the **Mozilla Public License Version 2.0 (`MPL-2.0`)**. See `LICENSE` (`app\LICENSE` in portable and installed builds) for the complete license text. `Mother` is clean-room software containing zero proprietary third-party binaries, secrets, or hardcoded application dependencies.
