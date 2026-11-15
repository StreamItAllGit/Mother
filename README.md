# Mother

- **Active Version:** `v0.2.0` (Full SemVer `MAJOR.MINOR.PATCH` for At-a-Glance Suite Interoperability; Next Horizon: `v0.2.1`)
- **License:** `MPL-2.0` ([`LICENSE`](file:///<WORKSPACE_ROOT>/Mother/LICENSE))
- **Lifecycle State:** `active` ([`active.state`](file:///<WORKSPACE_ROOT>/Mother/active.state))
- **Tech Stack:** .NET 8 (`net8.0-windows`, C# 12, Kestrel Reverse-Proxy Gateway `:5100`, WinForms + WebView2 Single-Window Shell, Tray Sentinel, Titanium Sky `#38bdf8` UI)
- **Default Umbrella Install Root:** `%LOCALAPPDATA%\Programs\StreamItAll\Mother\Mother\` (`http://127.0.0.1:5100/`, Tailscale CGNAT `:8500`, Sandbox `:15100`; legacy read-only `<PROGRAMS_ROOT>/Mother\` fallback supported)

---

## 1. What It Is & Core Goal
**Mother** is the Single Shared Suite Daemon (`:5100`), unified system tray sentinel, reverse-proxy API gateway, schedule traffic cop, autonomous Cold-Wake scheduler, post-run power coordinator, and cross-app coexistence broker for the 6 sister applications (**Housekeeping** `:5178`, **ShadowLibrary** `:5179`, **Redroom** `:5180`, **Greenroom** `:8420`, **StreamEngine** `:8430`, and **legacy-sister-app** `:8460`).

Per StreamItAll release engineering conventions, `Mother` uses strict 3-part **Full Semantic Versioning (`MAJOR.MINOR.PATCH`)** with zero pre-release suffix ambiguity so sister apps can verify `minSuiteVersion` compatibility at a single glance.

---

## 2. Quickstart, Tests & Shipped Artifacts (`v0.2.0`)

### Build & Run in Isolated Sandbox (`Sandbox\`, Port `15100`)
```powershell
cd <WORKSPACE_ROOT>/Mother\Sandbox
.\app\Mother.exe --test-mode --headless --data-dir .\data --log-dir .\logs --port 15100
.\app\Mother.exe --test-mode --data-dir .\data --log-dir .\logs --port 15100
```

### Run Unit & Integration Tests (`35/35` xUnit Passing Under `/warnaserror`)
```powershell
cd <WORKSPACE_ROOT>/Mother\Dev
dotnet test .\Mother.slnx -c Release -p:TreatWarningsAsErrors=true --logger "console;verbosity=normal"
```

### Distribution Outputs (`v0.2.0`)
- **Clean Release Root:** [`Builds\v0.2.0\`](file:///<WORKSPACE_ROOT>/Mother/Builds/v0.2.0/) (`Mother.exe`, `AGENTS.md`, `LICENSE`, `app\{LICENSE,config\settings.example.json,...}`, `scripts\`)
- **Zero-PII 4-Pillar Installer:** [`Installers\Mother-Setup-v0.2.0-win-x64.exe`](file:///<WORKSPACE_ROOT>/Mother/Installers/Mother-Setup-v0.2.0-win-x64.exe) (`audit_installer.ps1` `FindingCount: 0`)
- **End-User Release Bundle:** [`Releases\v0.2.0\`](file:///<WORKSPACE_ROOT>/Mother/Releases/v0.2.0/) & `Releases\Latest\` (6 end-user files + `.7z` archives)

---

## 3. Architecture At a Glance
- **Application Archetype:** Shared Suite Daemon + Single Public Gateway (`:5100`) + Single-Window WebView2 Shell
- **Shared SDK Pin:** **`Foundation v0.4.0`** (`<WORKSPACE_ROOT>/SharedLibs\Foundation\v0.4.0\` $\rightarrow$ `Dev\lib\` & `Dev\Mother.Server\wwwroot\vendor\foundation\` via `sync_foundation_sdk.py --sync-consumer`)
- **Per-App Residency Modes:** `Warm` (`always-on`), `Cold` (`on-demand` — `0 MB` idle standby woken on UI/API/schedule and hibernated after `idleTimeoutMinutes`), and `Disabled`.
- **4 UI Presentation Modes:** `--suite-tab` (outer horizontal tab inside Mother), `--window` (dedicated standalone app window), `--browser` (desktop browser), and scoped Mobile PWA (`/app/<app-id>/`).

---

## 4. Project Navigation, Root Quartet & 4-Doc Manual Shelf
- **Root Documentation Quartet:**
  - **Feature Inventory & Coexistence Map:** [`FEATURES.md`](file:///<WORKSPACE_ROOT>/Mother/FEATURES.md)
  - **Milestones & Strategic Horizon:** [`ROADMAP.md`](file:///<WORKSPACE_ROOT>/Mother/ROADMAP.md)
  - **Version History Ledger:** [`CHANGELOG.md`](file:///<WORKSPACE_ROOT>/Mother/CHANGELOG.md)
- **4-Document Manual Shelf (`Dev\docs\`):**
  - **📐 Architecture & Engineering:** [`Dev\docs\ARCHITECTURE.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/ARCHITECTURE.md)
  - **📘 User Guide:** [`Dev\docs\USER_GUIDE.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/USER_GUIDE.md)
  - **🛠️ Operations Runbook:** [`Dev\docs\OPERATIONS.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/OPERATIONS.md)
  - **🔌 Interfaces & API Reference:** [`Dev\docs\INTERFACES.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/INTERFACES.md)
- **Blueprints & Diagnostic Reports:**
  - **Implementation Plans:** [`Dev\docs\plans\v0.2.0.2026-11-15.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.2.0.2026-11-15.md) | [`Dev\docs\plans\v0.1.0.2026-11-12.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-12.md)
  - **Diagnostic Reports:** [`Dev\docs\reports\archive\`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/reports/archive/)
