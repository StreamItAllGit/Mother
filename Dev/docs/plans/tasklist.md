# Mother — `v0.2.0` Tasklist

- **Target Version:** `v0.2.0` (Promoted from `v0.1.1` + `0938`/`2115` Remediation)
- **Detail Level:** `3` *(1 = Basic · 2 = Informative · 3 = Guided Hints · 4 = Full How-To)*
- **Last Synced:** `2026-11-15`

## Block 1 — `v0.1.0` Suite Host, WebView2 & Packaging ✅
- [x] **1.1 Shipped `Mother.App`, `Mother.Launcher`, 4-Pillar `Mother.Installer` & `28/28` Tests (`T4`–`T11`):**
  - **Targets:** `Dev\Mother.{App,Core,Installer,Launcher,Server,Tests}\`, `Builds\`, `Installers\`, `Releases\`
  - **Why:** Establishes the central system tray supervisor, reverse proxy, Traffic Cop, and Dual-Viewport CDP verified UI.

## Block 2 — `@inspecter` Final Sign-Off & `v0.1.1` Co-Bootstrapping
- [x] **2.1 Complete `@inspecter` Sign-Off on `Mother v0.1.0` Packaged Release:**
  - **Targets:** `Sandbox\logs\`, `Dev\docs\reports\`, `active.state`
  - **Why:** Closes remaining verification gate (`isInspecterVerified`) for `v0.1.0` baseline.
  - **Hint:** Confirm `--test-mode` `:15100` startup, single-tray mutex, and `<WORKSPACE_ROOT>/Installers\` parity.
- [x] **2.2 Implement Sister-App Installer Co-Bootstrapping & Live Cold-Wake Schedule Loop (`v0.1.1`):**
  - **Targets:** `Dev\Mother.Core\`, `Dev\Mother.Server\`, `Dev\suite.manifest.json`
  - **Why:** Enables `Mother` to wake `Cold`-resident sister apps (`Greenroom`, `Redroom`, `ShadowLibrary`, `legacy-sister-app`, `Housekeeping`, `StreamEngine`) cleanly on schedule.
  - **Gotcha:** Enforce fail-closed coexistence deferral before waking disk-heavy workers.
  - **Hint:** Use `suite.manifest.json` `schedules[]` and `/api/v1/coexistence/status`.
- [x] **2.3 Expand `foundation-terminal` Whitelisted Diagnostic Subcommands:**
  - **Targets:** `Dev\Mother.Server\`, `Dev\Mother.App\`
  - **Why:** Surfaces live residency state, Traffic Cop queue depth, and child health directly in the docked terminal.


## v0.2.0 — StreamItAll\Mother Suite Root, Supervision, Scheduler & `0938`/`2115` Remediation
- [x] **T1:** Upgrade to `Foundation v0.4.0` SDK & untrack `Dev\lib\Foundation.*.dll` from Git (`0938`, `2115`)
- [x] **T2:** Move `Mother.Installer` default root to `%LOCALAPPDATA%\Programs\StreamItAll\Mother\Mother\` with `MOTHER_HOME`/HKCU lookup & opt-in `--migrate-legacy` (`2115`)
- [x] **T3:** Update `PathResolver.ResolveProgramsRoot` & `ManifestRegistryService` for `...\StreamItAll\Mother\` peer sister discovery + read-only legacy fallback (`2115`)
- [x] **T4:** Fix `deploy.ps1#L97-105` `Assets\Assets` & `migrations\migrations` nesting, stage Notion-free `AGENTS.md` + `LICENSE`, and update `$TargetDir` (`0938`, `2115`)
- [x] **T5:** Sync `Directory.Build.props`, `version.txt`, `suite.manifest.json`, `mother.manifest.json`, `active.state`, and code fallbacks (`"0.3.0"` -> `"0.4.0"`) (`0938`)
- [x] **T6:** `Dev\config\settings.example.json` overlay + `OPERATIONS.md#L205` scrub + cold archive + authorized `git-filter-repo` private force-push (`2115`)
- [x] **T7:** Foreground `WorkerArgs` + `commands.startDaemon`/`commands.start` supervision, `ResolveSiblingSandboxRoot` containment, live `schedules[]` Cold-Wake loop + idle hibernation, `/api/v1/scheduler/defer`, & `/app/{appId}` `307` redirect + `X-Forwarded-Prefix` (`2.2` + Seam Fix)
- [x] **T8:** Expand `foundation-terminal` whitelisted subcommands (`status`, `traffic`, `logs`, `wake`, `stop`, `reload`, `power <StayAwake|Sleep|Shutdown>`) (`2.3`)
