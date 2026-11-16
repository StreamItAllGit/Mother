# Mother — Strategic Roadmap & Milestones (`ROADMAP.md`)

> **Active Full SemVer Version:** `v0.2.0-hf.1` (`MAJOR.MINOR.PATCH[-hf.N]`) | **Next Horizon:** `v0.2.1` | **Active Blueprint:** [`Dev/docs/plans/v0.2.0.2026-11-15.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.2.0.2026-11-15.md) | **Updated:** `2026-11-15`

---

## 1. North Star Goal

Deliver a single unified system tray sentinel, reverse-proxy gateway, cross-app I/O traffic cop, autonomous Cold-Wake scheduler, and fleet coexistence arbitrator (`Mother`, `:5100`) that supervises all installed sister applications (`Housekeeping`, `ShadowLibrary`, `Redroom`, `Greenroom`, `legacy-sister-app`, `StreamEngine`), eliminating tray icon clutter, preventing physical drive contention, and brokering zero-double-work settings alignment across the workstation.

---

## 2. Shipped Baseline (`v0.1.0` — Shared Suite Daemon, Cold-Wake Gateway & 4-Pillar Installer)

- **Shipped Blueprint:** [`Dev/docs/plans/v0.1.0.2026-11-12.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-12.md) (`Status: Completed & Verified`)
- **Verified & Archived Diagnostic Reports:**
  - [`Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-02_0448.-.scaffold-and-readiness-audit.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-02_0448.-.scaffold-and-readiness-audit.md)
  - [`Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-02_0532.-.sister-app-feature-parity.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-02_0532.-.sister-app-feature-parity.md)
  - [`Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-03_1715.-.fleet-coexistence-broker-and-manifest-schema.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-03_1715.-.fleet-coexistence-broker-and-manifest-schema.md)
- **Shipped `v0.1.0` Deliverables (`T1`–`T11` Complete, `28/28` xUnit Tests Passing):**
  - [x] `T1`–`T11` Foundation v0.3.0 sync, `Mother.Core`/`Server`/`App`/`Launcher`/`Installer`, Titanium Sky UI (`Alt+1..7`, `Ctrl+B`, `Ctrl+\``, `Ctrl+K`, `Ctrl+S`), isolated unit test verification, and 4-pillar signed packaging.

---

## 3. Current Release (`v0.2.0` -> `v0.2.0-hf.1` — `StreamItAll\Mother` Umbrella Root, `Foundation v0.4.1-hf.1`, Cold-Wake Scheduler & Subpath Proxy)

- **Completed Blueprint:** [`Dev/docs/plans/v0.2.0.2026-11-15.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.2.0.2026-11-15.md) (`Verified Release Gate PASS`)
- **Predecessor Audit Reports:**
  - [`Dev/docs/reports/archive/0.1.0.2026-11-14_0938.-.fleet-audit-remediation.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/reports/archive/0.1.0.2026-11-14_0938.-.fleet-audit-remediation.md)
  - [`Dev/docs/reports/archive/0.1.0.2026-11-14_2115.-.v020-suite-nesting-installer-and-public-audit.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/reports/archive/0.1.0.2026-11-14_2115.-.v020-suite-nesting-installer-and-public-audit.md)
- **Shipped `v0.2.0` / `v0.2.0-hf.1` Deliverables (`T1`–`T8` + Hotfix 1 Complete):**
  - [x] **`T1`–`T5` SDK v0.4.1-hf.1, Umbrella Install Root & Sibling Suite Discovery:** Consume `Foundation v0.4.1-hf.1` (`sync_foundation_sdk.py`), move defaults to `%LOCALAPPDATA%\Programs\StreamItAll\Mother\Mother`, switch installer to `InstallerEngineCore` (`--profile`, `--suite-dir`, `--migrate-legacy`), enable sibling-directory discovery, align `MPL-2.0`, fix `deploy.ps1`/`package_installer.ps1`, and relocate `targetDrives` to `MotherSettings`.
  - [x] **`T6` Clean-Room Scrub & Config Example Overlay:** Add neutral `Dev\config\settings.example.json`, scrub user-profile path leaks, capture pre-filter `.7z` backup, and purge tracked SDK DLLs/PII from private Git history.
  - [x] **`T7`–`T8` Cold-Wake Scheduler Loop, Worker Command Builder, Subpath Proxy & Expanded Terminal:** Implement `WorkerLaunchCommandBuilder`, `FleetScheduleEvaluator` + `scheduler-state.json`, `ColdWakeCoordinator` idle hibernation, `POST`/`GET /api/v1/scheduler/defer`, `307 /app/{appId}` -> `/app/{appId}/` + `X-Forwarded-Prefix`, and `TerminalCommandDispatcher`.
  - [x] **`v0.2.0-hf.1` Legacy Harness Retirement & `Foundation v0.4.1-hf.1` Sync:** Remove retired isolated runner scripts, custom test flags, and secondary port bifurcation; adopt `AppEnvironment`, `UiPresentationMode.TrayOrBackground`, & `InstallerRuntimeContext` (`APP_ISOLATED_TEST=1`, `35/35` Release tests passing under `/warnaserror`).

---

## 4. Major-as-Destination Trajectory (`0.1.0 -> 0.2.0-hf.1 -> 0.2.1 -> 1.0.0 -> 1.1.0 -> 2.0.0`)

- **`v0.1.0`:** Initial Shared Suite Daemon (`:5100`), Cold-Wake reverse-proxy gateway, `.hklock` Traffic Cop, Coexistence Broker, Post-Run Power Coordinator, and Titanium Sky WebView2 + PWA shell.
- **`v0.2.0` / `v0.2.0-hf.1` (Current):** `Foundation v0.4.1-hf.1`, `StreamItAll\Mother` umbrella layout, sibling directory containment, `WorkerArgs`/`commands.startDaemon` supervision, autonomous `schedules[]` Cold-Wake execution + idle hibernation, `/api/v1/scheduler/defer`, `/app/{appId}` subpath proxy, expanded terminal diagnostics, and Tier A 5-folder layout.
- **`v0.2.1` (Next Minor Patch):** Full live 6-sister-app manifest integration (`Housekeeping`, `ShadowLibrary`, `Redroom`, `Greenroom`, `StreamEngine`, `legacy-sister-app`) under `StreamItAll\Mother\`.
- **`v1.0.0` (Going Gold — Production Cutover):** Authorized production cutover supervising all installed sister daemons end-to-end.
- **`v1.1.0`:** Multi-node LAN / Tailscale fleet telemetry aggregation and historical health rollups.
- **`v2.0.0`:** Distributed cross-workstation job routing and cluster-wide storage lock arbitration.
