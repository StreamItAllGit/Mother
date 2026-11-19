# Project Context: Mother

- **Role:** Universal Pluggable Shared Suite Daemon (`:5100`), Gateway & Watchdog (`v0.3.0 -> v0.4.0` Beta, `MPL-2.0`).
- **Boundaries:** Code/tests live in `Dev\` (`APP_ISOLATED_TEST=1` isolated tests); canonical prod (`%LOCALAPPDATA%\Programs\StreamItAll\Mother\Mother\`), `Foundation\`, and Dev Bible standards are read-only; permanent human review gate (`requireHumanReviewBeforePush: true`) before any push or release.

## 1. Root Quartet & State
- [README.md](README.md) | [FEATURES.md](FEATURES.md) | [ROADMAP.md](ROADMAP.md) | [CHANGELOG.md](CHANGELOG.md) | [SECURITY.md](SECURITY.md) | [CONTRIBUTING.md](CONTRIBUTING.md) | [active.state](active.state) | [LICENSE](LICENSE)

## 2. Manual Shelf (`Dev\docs\`)
- [ARCHITECTURE.md](Dev/docs/ARCHITECTURE.md) | [INTERFACES.md](Dev/docs/INTERFACES.md) | [USER_GUIDE.md](Dev/docs/USER_GUIDE.md) | [OPERATIONS.md](Dev/docs/OPERATIONS.md)
- Local-only plans & reports (gitignored): `Dev/docs/plans/`, `Dev/docs/reports/`

## 3. Governance, Discord & Notion
- **Standards & QA:** Dev Bible standards (read-only) | `@dean` Dual-Gate | `@inspecter` Unit & Log QA | `@tessa` Docs
- **Discord & Notion:** Release-only `/post` via `active.state` `discordPost` tagging `@StreamDoesDev` (human-gated); Notion `<NOTION_PROJECT_PAGE_ID>` / `<NOTION_TASK_LIST_COLLECTION>` (manual-only; local IDs in gitignored `Dev/data/notion_hub.local.json`)
