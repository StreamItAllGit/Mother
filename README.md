# Mother

- **Active Version:** `v0.1.0` (`v0.2.0` In-Dev — Full SemVer `MAJOR.MINOR.PATCH` for At-a-Glance Suite Interoperability)
- **Lifecycle State:** `active` ([`active.state`](file:///<WORKSPACE_ROOT>/Mother/active.state))
- **Tech Stack:** .NET 8 (C# 12, Kestrel Reverse-Proxy Gateway `:5100`, WebView2 Single-Window Shell, WinForms Tray Sentinel)
- **Canonical Production Path:** `<PROGRAMS_ROOT>/Mother\` (`http://127.0.0.1:5100/`, Tailscale HTTPS `:8500`, Sandbox `:15100`)

---

## 1. What It Is & Core Goal
**Mother** is the Single Shared Suite Daemon (`:5100`), unified system tray sentinel, reverse-proxy API gateway, schedule traffic cop, post-run power coordinator, and cross-app coexistence broker for the 6 sister applications (**Housekeeping** `:5178`, **ShadowLibrary** `:5179`, **Redroom** `:5180`, **Greenroom** `:8420`, **StreamEngine** `:8430`, and **legacy-sister-app** `:8460`).

Per [Dev Bible Standard 03 §1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/03_VERSIONING_AND_RELEASES.md) and [Standard 17 §1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/17_SISTER_COMPATIBILITY_AND_FEATURE_PARITY.md), `Mother` uses strict 3-part **Full Semantic Versioning (`MAJOR.MINOR.PATCH`)** with zero `-rc.<n>` or `-alpha.<n>` tags so sister apps can verify `minSuiteVersion` compatibility at a single glance.

---

## 2. Quickstart & Developer Commands

### Build & Run in Isolated Sandbox (`Sandbox\`, Port `15100`)
```powershell
cd <WORKSPACE_ROOT>/Mother\Sandbox
..\Dev\scripts\run.bat --test-mode --data-dir .\data --config .\data\settings.json --log-dir .\logs --port 15100
```

### Run Unit & Integration Tests
```powershell
cd <WORKSPACE_ROOT>/Mother\Dev
dotnet test --logger "console;verbosity=normal"
```

---

## 3. Architecture At a Glance
- **Application Archetype:** Shared Suite Daemon + Single Public Gateway (`:5100`) + Single-Window WebView2 Shell (`Standard 08`, `Standard 17`)
- **Shared SDK Pin:** `Foundation v0.1.0` (`<WORKSPACE_ROOT>/SharedLibs\Foundation\v0.1.0\` $\rightarrow$ `Dev\lib\`)
- **Per-App Residency Modes:** `Warm` (resident background worker), `Cold` (0 MB idle standby; woken on UI/API/schedule), and `Disabled`.
- **4 UI Presentation Modes:** `--suite-tab` (outer horizontal tab inside Mother), `--window` (dedicated standalone app window), `--browser` (desktop browser), and scoped Mobile PWA (`/app/<app-id>/`).

---

## 4. Project Navigation, Root Quartet & 4-Doc Manual Shelf
- **Root Documentation Quartet:**
  - **Feature Inventory & Coexistence Map:** [`FEATURES.md`](file:///<WORKSPACE_ROOT>/Mother/FEATURES.md)
  - **Milestones & Strategic Horizon:** [`ROADMAP.md`](file:///<WORKSPACE_ROOT>/Mother/ROADMAP.md)
  - **Version History Ledger:** [`CHANGELOG.md`](file:///<WORKSPACE_ROOT>/Mother/CHANGELOG.md)
- **4-Document Manual Shelf (`Dev\docs\` — Standard 16 §3.5):**
  - **📐 Architecture & Engineering:** [`Dev\docs\ARCHITECTURE.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/ARCHITECTURE.md)
  - **📘 User Guide:** [`Dev\docs\USER_GUIDE.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/USER_GUIDE.md)
  - **🛠️ Operations Runbook:** [`Dev\docs\OPERATIONS.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/OPERATIONS.md)
  - **🔌 Interfaces & API Reference:** [`Dev\docs\INTERFACES.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/INTERFACES.md)
- **Blueprints & Diagnostic Reports:**
  - **Implementation Plans (`@pland`):** [`Dev\docs\plans\v0.1.0.2026-11-04.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-04.md)
  - **Diagnostic Reports (`@inspecter`):** [`Dev\docs\reports\archive\`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/reports/archive/)
