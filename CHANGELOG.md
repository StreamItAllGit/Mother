# Changelog

All notable changes to `Mother` will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to strict 3-part [Semantic Versioning (`MAJOR.MINOR.PATCH`)](https://semver.org/spec/v2.0.0.html) per [Dev Bible Standard 03 §1](file:///<WORKSPACE_ROOT>/<DEV_BIBLE>/03_VERSIONING_AND_RELEASES.md).

## [Unreleased]

## [0.1.0] - 2026-11-04
### Added
- **6-Folder Root Topology, Root Documentation Quartet & 4-Doc Manual Shelf (`UNI-01`, `UNI-02`, `Std 16 §3.5`)**:
  - Added `Releases\` folder to complete the 6-folder layout (`Archive\`, `Builds\`, `Dev\`, `Installers\`, `Releases\`, `Sandbox\`) and migrated `README.md` and `CHANGELOG.md` to the outer project root alongside `FEATURES.md`, `ROADMAP.md`, and `active.state`.
  - Authored the complete 4-Document Manual Shelf in `Dev\docs\`: [`ARCHITECTURE.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/ARCHITECTURE.md), [`USER_GUIDE.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/USER_GUIDE.md), [`OPERATIONS.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/OPERATIONS.md), and [`INTERFACES.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/INTERFACES.md).

### Changed
- **Adopted Full Semantic Versioning (`MAJOR.MINOR.PATCH` — `v0.1.0`)**:
  - Replaced legacy `0.1.0-alpha.1` / `1.0.0-rc.1` pre-release suffixes with strict suffix-free `MAJOR.MINOR.PATCH` versioning (`v0.1.0`) per Dev Bible Standard 03 §1 for instant at-a-glance `minSuiteVersion` interoperability across all sister apps.
  - Updated active implementation plan to [`Dev\docs\plans\v0.1.0.2026-11-04.md`](file:///<WORKSPACE_ROOT>/Mother/Dev/docs/plans/v0.1.0.2026-11-04.md).

## [0.1.0-alpha.1] - 2026-11-02
### Added
- Initialized project structure under `<WORKSPACE_ROOT>/Mother\` and created initial version snapshot `Mother-v0.1.0-alpha.1-2026-11-02.7z`.
