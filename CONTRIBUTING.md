# Contributing to Mother

Thank you for your interest in contributing to **Mother** (`MPL-2.0`). This guide covers setting up the build environment, acquiring the required `Foundation v0.5.1` SDK bundle, running tests, and submitting clean pull requests.

---

## 1. Prerequisites

- **OS:** Windows 10 or Windows 11 (`win-x64`)
- **.NET SDK:** **.NET 8 SDK** (`8.0+`, targeting `net8.0-windows`)
- **Optional (Recommended for NativeAOT Launcher):** MSVC C++ Build Tools (Visual Studio Build Tools with *Desktop development with C++* / `cl.exe` x64/x86).
  - If MSVC is installed, `Dev\deploy.ps1` compiles `Mother.Launcher` (`Mother.exe`) with `-p:PublishAot=true`.
  - If MSVC is absent, `Dev\deploy.ps1` automatically falls back to `-p:PublishSingleFile=true -p:PublishTrimmed=true -p:TrimMode=partial` so you can still build and package locally without extra setup.
- **IDE Workspace (Optional):** Open `Dev\Mother.code-workspace` in VS Code or Cursor for preconfigured `Mother.slnx` solution settings, LF line endings, build/test tasks, and recommended C#/.NET extensions.

---

## 2. Acquiring the `Foundation v0.5.1` SDK Bundle

Compiled `Foundation` SDK assemblies are excluded from Git so the repository stays clean-room and reproducible.

1. Download **`Foundation-v0.5.1.zip`** from **`StreamItAllGit/Foundation` GitHub Releases** (`Releases\v0.5.1` -> `https://github.com/StreamItAllGit/Foundation/releases/tag/v0.5.1`).
2. Extract the `Foundation.*.dll` assemblies from the archive root into `Dev\lib\`:
   - Required assemblies: `Foundation.Core.dll`, `Foundation.Installer.dll`, `Foundation.Launcher.dll`, `Foundation.Server.dll`, `Foundation.UI.dll`, and `Foundation.Automation.dll` (used by `Mother.Tests` and Debug/automation builds).
3. From the `WebAssets\` folder inside `Foundation-v0.5.1.zip`, copy **strictly the 3 vanilla runtime web assets** into `Dev\Mother.Server\wwwroot\vendor\foundation\`:
   - `foundation-tokens.css`
   - `foundation-terminal.css`
   - `foundation-terminal.js`

> **Important:** Do **not** copy `.tsx`, `.ts`, `components\`, or `package.json` from `WebAssets\` into `Dev\Mother.Server\wwwroot\vendor\foundation\`. Only those 3 runtime CSS/JS assets belong there.

---

## 3. Building & Running Tests

Run all commands from the repository root:

```powershell
# Build the full solution with warnings treated as errors
dotnet build Dev\Mother.slnx -c Release -p:TreatWarningsAsErrors=true

# Run the isolated xUnit test suite (APP_ISOLATED_TEST=1 suppresses live host mutations)
$env:APP_ISOLATED_TEST = "1"
dotnet test Dev\Mother.slnx -c Release -p:TreatWarningsAsErrors=true --logger "console;verbosity=normal"
Remove-Item Env:APP_ISOLATED_TEST
```

### Staging & Packaging Locally

```powershell
# Stage self-contained runtime + launcher into Builds\v0.3.0\
powershell -ExecutionPolicy Bypass -File Dev\deploy.ps1 -BuildOnly

# Package Windows Setup EXE, Portable ZIP, checksums, and Releases\v0.3.0\ + Latest\
powershell -ExecutionPolicy Bypass -File Dev\package_installer.ps1
```

If using `Dev\Mother.code-workspace`, you can also launch these directly from **Terminal -> Run Task** (`Build Mother (.NET)`, `Test Mother (.NET)`, `Deploy Mother Staging Build`, `Package Mother Release Bundle`).

---

## 4. Coding & Pull Request Conventions

- **Architecture & File Size:** Organize new code cleanly inside vertical feature slices (`Dev\Mother.Core\{Core,Features}\`) and keep individual source files concise (`<= 400` code SLOC target).
- **Universal Pluggable Core:** Never hardcode application-specific lists, sister product names, private workstation paths, or environment secrets. All supervised apps must be discovered via `suite.manifest.json` (`v2`) or `mother.manifest.json` (`v1`).
- **Zero-Warning Policy:** All builds and tests compile with `-p:TreatWarningsAsErrors=true`; do not suppress warnings blindly or leave bare empty `catch` blocks.
- **Documentation & Changelog:**
  - Document new or modified endpoints, CLI flags, settings keys, or manifest fields in `Dev\docs\` (`ARCHITECTURE.md`, `INTERFACES.md`, `USER_GUIDE.md`, `OPERATIONS.md`).
  - Record user-facing changes under `## [Unreleased]` in `CHANGELOG.md` following [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) (`Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`) and [Semantic Versioning](https://semver.org/).
- **Pull Request Checklist:**
  - [ ] `dotnet test Dev\Mother.slnx -c Release -p:TreatWarningsAsErrors=true` passes completely (`0` failures, `0` warnings).
  - [ ] No binaries (`Dev\lib\*.dll`), local `data\`, `logs\`, `Builds\`, `Releases\`, or workstation paths are included in git commits.
  - [ ] New behaviors and edge cases include xUnit coverage in `Dev\Mother.Tests\`.
