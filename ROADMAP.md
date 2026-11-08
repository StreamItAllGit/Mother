# Mother — Strategic Roadmap & Milestones (`ROADMAP.md`)

> **Active Full SemVer Version:** `v0.1.0` (`MAJOR.MINOR.PATCH` per [Standard 03 §1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/03_VERSIONING_AND_RELEASES.md)) | **Active Blueprint:** [`Dev/docs/plans/v0.1.0.2026-11-04.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-04.md) | **Updated:** `2026-11-04` | **Maintained By:** `@tessa` & `@pland`

---

## 1. North Star Goal

Deliver a single unified system tray sentinel, reverse-proxy gateway, cross-app I/O traffic cop, and fleet coexistence arbitrator (`Mother`, `:5100`) that supervises all installed sister applications (`Housekeeping`, `ShadowLibrary`, `Redroom`, `Greenroom`, `legacy-sister-app`, `StreamEngine`), eliminating tray icon clutter, preventing physical drive contention, and brokering zero-double-work settings alignment across the workstation.

---

## 2. Active In-Dev Milestone (`v0.1.0` — Shared Suite Daemon & Fleet Coexistence Broker)

- **Active Blueprint (`@pland`):** [`Dev/docs/plans/v0.1.0.2026-11-04.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-04.md) (`Status: Design Locked — Awaiting Execution Directive`)
- **Ingested Diagnostic Reports (`@inspecter`):**
  - [`Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-02_0448.-.scaffold-and-readiness-audit.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-02_0448.-.scaffold-and-readiness-audit.md) (`Planned in v0.1.0`)
  - [`Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-02_0532.-.sister-app-feature-parity.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-02_0532.-.sister-app-feature-parity.md) (`Planned in v0.1.0`)
  - [`Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-03_1715.-.fleet-coexistence-broker-and-manifest-schema.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/reports/archive/v0.1.0-alpha.1.2026-11-03_1715.-.fleet-coexistence-broker-and-manifest-schema.md) (`Planned in v0.1.0`)
- **Planned `v0.1.0` Deliverables:**
  - [x] **Phase 1 — 6-Folder Migration, Root Docs Quartet & Full SemVer (`v0.1.0`):** Migrated to 6-folder layout with root `active.state` and Root Documentation Quartet (`README.md`, `FEATURES.md`, `ROADMAP.md`, `CHANGELOG.md`).
  - [ ] **Phase 2 — Dual-Compatible `suite.manifest.json` (`v2`) Loader, `*.hklock`-Aware Traffic Cop & `FleetCoexistenceArbitrator`:** Parse `manifests\*.manifest.json` (`minSuiteVersion`, `sdkVersion`), check `<Dir>\.<app>_<pid>.hklock` sentinels in `ResourceTrafficCop`, and implement `GET /api/v1/suite/coexistence/status` + `POST /api/v1/suite/coexistence/apply-recommendations` with opt-in `AutoAlignWithSisterApps` (`Default: false`).
  - [ ] **Phase 3 — Single-Window Suite Shell, `Warm`/`Cold`/`Disabled` Residency & Single-Surface Installer:** Deliver Titanium Sky (`#38bdf8`) outer tab shell, Fleet Coexistence Matrix card, and Authenticode-signed WinForms single-EXE installer (`Mother-Setup-v0.1.0.exe`).

---

## 3. Upcoming Milestones (`Major-as-Destination` Trajectory: `0.1.0 -> 0.2.0 -> 1.0.0`)

- **`v0.2.0`:** Self-bootstrapping co-packaging inside sister-app installers (`<PROGRAMS_ROOT>/Mother\manifests\<app-id>.manifest.json`) and Cold-Wake scheduler orchestration.
- **`v1.0.0` (Going Gold — Full Release):** Full production cutover supervising all 6 sister daemons in `<PROGRAMS_ROOT>/Mother\`.
- **`v1.1.0`:** Cross-machine LAN/Tailscale node federation and historical fleet telemetry rollups.
