# Mother — Strategic Roadmap & Milestones (`ROADMAP.md`)

> **Active Full SemVer Version:** `v0.1.0` (`Live` — `MAJOR.MINOR.PATCH` per [Standard 03 §1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/03_VERSIONING_AND_RELEASES.md)) | **Next Horizon:** `v0.2.0` | **Shipped Blueprint:** [`Dev/docs/plans/v0.1.0.2026-11-12.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-12.md) | **Updated:** `2026-11-12` | **Maintained By:** `@tessa` & `@pland`

---

## 1. North Star Goal

Deliver a single unified system tray sentinel, reverse-proxy gateway, cross-app I/O traffic cop, and fleet coexistence arbitrator (`Mother`, `:5100`) that supervises all installed sister applications (`Housekeeping`, `ShadowLibrary`, `Redroom`, `Greenroom`, `legacy-sister-app`, `StreamEngine`), eliminating tray icon clutter, preventing physical drive contention, and brokering zero-double-work settings alignment across the workstation.

---

## 2. Shipped Baseline (`v0.1.0` — Shared Suite Daemon, Cold-Wake Gateway & 4-Pillar Installer)

- **Shipped Blueprint (`@pland`):** [`Dev/docs/plans/v0.1.0.2026-11-12.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-12.md) (`Status: Completed & Verified` — `@dean` Gate 1/2 + Delta `PASS`, `@inspecter` `PASS`)
- **Verified & Archived Diagnostic Reports (`@inspecter`):**
  - [`Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-02_0448.-.scaffold-and-readiness-audit.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-02_0448.-.scaffold-and-readiness-audit.md) (`Verified in v0.1.0`)
  - [`Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-02_0532.-.sister-app-feature-parity.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-02_0532.-.sister-app-feature-parity.md) (`Verified in v0.1.0`)
  - [`Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-03_1715.-.fleet-coexistence-broker-and-manifest-schema.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-03_1715.-.fleet-coexistence-broker-and-manifest-schema.md) (`Verified in v0.1.0`)
  - [`Dev/docs/reports/archive/0.1.0.2026-11-12T2348.-.v0-1-0-sandbox-verification.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/reports/archive/0.1.0.2026-11-12T2348.-.v0-1-0-sandbox-verification.md) (`PASS` — `T11` Remediation Verified)
- **Shipped `v0.1.0` Deliverables (`T1`–`T11` Complete, `28/28` xUnit Tests Passing):**
  - [x] **T1 — Repo Hygiene, `Foundation v0.3.0` SDK Sync & `Mother.slnx` Scaffolding:** Updated `.gitignore`, synced `Foundation v0.3.0` to `Dev\lib\` & `wwwroot\vendor\foundation\`, and scaffolded `Dev\Mother.slnx` (`net8.0-windows`, `/warnaserror`).
  - [x] **T2–T3 — `Mother.Core` Engine, `:5100` Kestrel Gateway, Cold-Wake Proxy & Titanium Sky UI:** Delivered Standard 18 §3 `mother.db` (`PRAGMA user_version = 1`), dual-schema `v1`/`v2` manifest loader, `.hklock` Traffic Cop, `FleetCoexistenceArbitrator`, `FleetPowerCoordinator`, `WatchdogSchedulerService`, `FleetDoctorService`, Tailscale CGNAT/QR, PWA, and modular `#38bdf8` UI (`Alt+1..7`, `Ctrl+B`, `Ctrl+\``, `Ctrl+K`, `Ctrl+S` 20-snapshot rollback, `?`).
  - [x] **T4–T7 — `Mother.App` WebView2 + Tray Host, Clean-Root `Mother.Launcher`, 4-Pillar `Mother.Installer` & `28/28` xUnit Suite:** Delivered desktop/headless host, `TaskbarCreated` tray restoration, GZip-embedded single-EXE setup wizard, `deploy.ps1`, `package_installer.ps1`, and `Dev\scripts\*.bat`.
  - [x] **T8–T11 — `Sandbox\` Dual-Viewport CDP Verification, `F-01`/`F-02` Remediation & Single-Pass Packaging:** Verified `1920x1080`, `390x844`, and `844x390` UI geometry (`0` console/overflow errors), Clean Release Root (`Builds\v0.1.0\`), zero-PII signed `Installers\Mother-Setup-v0.1.0-win-x64.exe` (`FindingCount: 0`), 6-file `Releases\v0.1.0\`, and `Archive\*.7z`.

---

## 3. Active Horizon (`v0.2.0` — Live Sister-App Co-Bootstrapping & Cold-Wake Scheduler Execution)

- **Target Objectives for `v0.2.0`:**
  - [ ] **Sister-App Installer Co-Bootstrapping:** Coordinate sister-app installers (`Housekeeping`, `ShadowLibrary`, `Redroom`, `Greenroom`, `StreamEngine`, `legacy-sister-app`) to auto-drop `<PROGRAMS_ROOT>/Mother\manifests\<app-id>.manifest.json` and hot-reload via `POST /api/v1/suite/apps/reload`.
  - [ ] **Live Cold-Wake Schedule Execution Loop:** Execute scheduled `schedules[]` entries across `Cold`-resident sister daemons (`RunIfMissed`, automatic hibernation timer after `coldWakeIdleMinutes`, and live `/api/v1/scheduler/defer` handshakes).
  - [ ] **Interactive `foundation-terminal` Command Expansion:** Add rich whitelisted diagnostic & residency subcommands (`wake <app>`, `stop <app>`, `coexistence`, `power <policy>`) to `/api/v1/terminal/exec`.

---

## 4. Major-as-Destination Trajectory (`0.1.0 -> 0.2.0 -> 1.0.0 -> 1.1.0 -> 2.0.0`)

- **`v0.1.0` (`Live`):** Initial Shared Suite Daemon (`:5100`), Cold-Wake reverse-proxy gateway, `.hklock` Traffic Cop, Coexistence Broker, Post-Run Power Coordinator, Titanium Sky (`#38bdf8`) WebView2 + PWA shell, and 4-pillar zero-PII installer.
- **`v0.2.0` (`Active Horizon`):** Self-bootstrapping sister-app installer manifest registration, autonomous Cold-Wake scheduler execution, and expanded terminal diagnostics.
- **`v1.0.0` (Going Gold — Production Cutover):** Authorized production cutover to `<PROGRAMS_ROOT>/Mother\` supervising all 6 live sister daemons end-to-end.
- **`v1.1.0`:** Multi-node LAN / Tailscale fleet telemetry aggregation and historical health rollups.
- **`v2.0.0`:** Distributed cross-workstation job routing and cluster-wide storage lock arbitration.
