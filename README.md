<div align="center">

<img src="Dev/Assets/logo.png" alt="Mother Logo" width="96" />

# Mother

A universal, pluggable Windows suite daemon, reverse-proxy API gateway, and disk I/O traffic cop that coordinates background worker apps through a single tray icon and web shell.

![Version](https://img.shields.io/badge/version-0.3.0--beta-blue?style=flat) ![License](https://img.shields.io/badge/license-MPL--2.0-green?style=flat) ![Runtime](https://img.shields.io/badge/runtime-.NET%208-512BD4?style=flat) ![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey?style=flat)

**Roadmap Progress (toward v1.0.0):** `▓▓▓▓░░░░░░` **40%** — `v0.3.0` Public Beta Universal Core, `Foundation v0.5.1` SDK alignment, and self-contained packaging complete; preparing `v0.4.0` Zero-Update App Onboarding.

</div>

> **Release Status: Beta (Pre-1.0 — `v0.3.0`)**
> Pre-1.0 builds ship as Beta (`--prerelease`) while universal plug-in onboarding (`v0.4.0`), multi-app soak burn-in (`v0.5.0`), and safe staged self-updating (`v0.6.0`) are finalized ahead of `v1.0.0`.

---

## About

`Mother` replaces scattered system tray icons, duplicate watchdogs, and colliding overnight drive tasks with one lightweight Windows supervisor and reverse-proxy gateway (`http://127.0.0.1:5100/`). Any application that exposes a `suite.manifest.json` (`v2`) or `mother.manifest.json` (`v1`) file—such as `demo-worker`, `media-tool`, headless CLI workers, or optional StreamItAll suite members—can run behind `Mother`'s single-window WebView2 shell, desktop browser tab, or mobile PWA.

Zero-to-many installations (`0..N` registered apps) work out of the box: `Mother` ships with zero hardcoded application defaults, boots cleanly into an actionable **0-App Empty State** (`Fleet Doctor` returns `"pass"`), and discovers installed apps dynamically to coordinate startup, idle hibernation, disk lock contention, and post-run OS sleep safely.

## Key Features

- **Single Tray Sentinel & 4-Mode UI:** Hosts `0..N` dynamically discovered apps inside one Titanium Sky (`#38bdf8`) WebView2 shell (with interactive browser fallback if WebView2 runtime is absent), dynamic tab switching (`Alt+1..9`), standalone pop-out windows (`--window`), browser tabs (`--browser`), and a responsive mobile PWA.
- **Autonomous Cold-Wake & Idle Hibernation:** Keeps `Cold` (`on-demand`) apps at `0 MB` RAM until requested via UI, API, or `schedules[]`, then hibernates them automatically once idle; headless workers (`internalPort <= 0`) fail closed cleanly with `503 NO_HTTP_ENDPOINT`.
- **Disk Traffic Cop & Coexistence Broker:** Inspects PID-safe `.hklock` sentinels to purge stale locks, defers colliding schedules (`POST /api/v1/scheduler/defer`), and aligns shared storage presets via `.coexistence.json` (`schemaVersion: 2`).
- **Safe Post-Run Power Coordinator:** Inhibits OS sleep until every supervised app reports zero active/queued jobs and zero live locks, then starts a 60-second cancellable countdown before `Sleep` or `Shutdown`.
- **Offline QR Pairing, Doctor & Settings v2 Rollback:** Includes offline ISO/IEC 18004 SVG QR pairing (`/api/v1/suite/qr.svg`), a docked `Ctrl+\`` diagnostic terminal, SQLite WAL telemetry with `VACUUM INTO` backups, non-destructive `data/config/settings.json` (`schemaVersion: 2`) migration, and a 20-snapshot rollback history (`Ctrl+S`).

## Getting Started

### Prerequisites

- Windows 10 or Windows 11 (`win-x64`)
- End-user installers (`Mother-Setup-v0.3.0-win-x64.exe`) and portable archives (`Mother-v0.3.0-win-x64-Portable.zip`) ship **self-contained** (`win-x64`, no separate .NET Runtime install required)
- Building from source requires **.NET 8 SDK** and the `Foundation v0.5.1` SDK bundle (see [CONTRIBUTING.md](CONTRIBUTING.md))
- Microsoft Edge WebView2 Evergreen Runtime (for embedded desktop shell mode; includes an automatic browser-launch fallback panel if missing)

### Quick Try

```powershell
# Launch Mother in tray mode using isolated local directories
cd Builds\v0.3.0
.\Mother.exe --tray-only --data-dir .\data --log-dir .\logs --port 5100
```

```powershell
# Run the full Release unit & integration test suite (40/40 passing, zero warnings)
$env:APP_ISOLATED_TEST = "1"
dotnet test Dev\Mother.slnx -c Release -p:TreatWarningsAsErrors=true --logger "console;verbosity=normal"
Remove-Item Env:APP_ISOLATED_TEST
```

<details>
<summary>Developer verification, CLI overrides, and packaging scripts</summary>

```powershell
# Stage portable clean-root build (Mother.App self-contained + NativeAOT/trimmed Mother.exe)
powershell -ExecutionPolicy Bypass -File "Dev\deploy.ps1" -BuildOnly

# Package the Windows installer, Portable ZIP, SHA256SUMS.txt, and Releases\v0.3.0\ + Latest\
powershell -ExecutionPolicy Bypass -File "Dev\package_installer.ps1"

# Run one-shot isolated System Doctor diagnostics
.\Builds\v0.3.0\Mother.exe --diagnose --data-dir "$env:TEMP\mother_diag\data" --log-dir "$env:TEMP\mother_diag\logs"
```

Supported runtime overrides: `--data-dir <dir>`, `--config <file>`, `--log-dir <dir>`, `--port <int>`, `--programs-dir <dir>`, `--suite-tab <appId>`, `--window <appId>`, `--browser [<appId>]`, `--tray-only`, `--dry-run`, `--diagnose`, `--version`, `--help`.

</details>

## Release Channels

Releases follow an explicit human-reviewed publishing flow across three distribution channels:

- **GitHub Releases (`--prerelease` Beta):** Versioned Windows Setup installers (`Mother-Setup-v0.3.0-win-x64.exe`), self-contained portable ZIPs (`Mother-v0.3.0-win-x64-Portable.zip`), release docs, and `SHA256SUMS.txt` checksums on `StreamItAllGit/Mother`.
- **Google Drive Public Mirror:** Public folder [`Personal/Mother`](https://drive.google.com/drive/folders/1WmIfaxbTNv8ZDWUMfNjis-vQSPP_ocHH) (populated alongside approved beta/GA releases).
- **Discord Devlogs:** Release summaries posted to community testers upon release sign-off.
- **Project Hub Page:** Optional public web overview managed manually outside automated release scripts.

## Project Structure

- `Dev/` — Source code (`Mother.Core`, `Mother.Server`, `Mother.App`, `Mother.Launcher`, `Mother.Installer`), xUnit tests (`Mother.Tests`), and technical manuals (`Dev/docs/`)
- `Builds/` — Local compiled portable staging outputs (`Builds/v0.3.0/`)
- `Releases/` — Packaged 7-file end-user release bundles (`Mother-Setup`, `Portable.zip`, docs, `SHA256SUMS.txt`) and NTFS-hardlinked `Latest/`
- `Archive/` — Compressed version snapshots

## Documentation

- [Features](FEATURES.md) — Current capabilities, extension points, and coexistence matrix
- [Roadmap](ROADMAP.md) — Milestone trajectory from `v0.3.0` Public Beta through `v1.0.0`
- [Changelog](CHANGELOG.md) — Version history ledger
- [Contributing](CONTRIBUTING.md) — Contributor setup (`Foundation v0.5.1` SDK ZIP), build, and test guide
- [Security Policy](SECURITY.md) — Vulnerability disclosure, network binding, and containment guarantees
- [User Guide](Dev/docs/USER_GUIDE.md) — Installation, portable setup, UI walkthrough, and workflows
- [Architecture](Dev/docs/ARCHITECTURE.md) — System design, symbol registry, and design decisions
- [Interfaces](Dev/docs/INTERFACES.md) — REST API, SSE streams, manifests, and database schema
- [Operations](Dev/docs/OPERATIONS.md) — CLI flags, configuration, backups, and incident playbooks

## License & Legal

Distributed under the **Mozilla Public License Version 2.0 (`MPL-2.0`)** ([LICENSE](LICENSE)).

`Mother` is clean-room infrastructure containing zero proprietary third-party binaries, secrets, or personal configuration data. Optional StreamItAll suite applications referenced as examples are independent software modules discovered dynamically via user-installed manifests.
