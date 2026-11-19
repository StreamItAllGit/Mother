# Security Policy

## Supported Versions

| Version | Status | Security Updates |
| :--- | :--- | :--- |
| `0.3.x` (Beta) | Supported | Active fixes and security patches |
| `< 0.3.0` | Unsupported | Please upgrade to `0.3.x` |

---

## Reporting a Vulnerability

Please do **not** open a public GitHub issue for suspected security vulnerabilities.

1. Submit a private report using **GitHub Security Advisories** at `https://github.com/StreamItAllGit/Mother/security/advisories/new`.
2. Include as much of the following as possible:
   - Affected `Mother` version (`Mother.exe --version`) and Windows edition/architecture.
   - Affected component (gateway endpoint, reverse proxy, installer, launcher, manifest parser, or containment guard).
   - Step-by-step reproduction details or proof of concept.
   - Potential impact (arbitrary execution, unauthorized access, path escape, or secret exposure).
3. Maintainers review private advisories promptly, verify fixes with automated tests before release, and credit reporters in release notes unless anonymity is requested.

---

## Security Architecture & Boundaries

### 1. Loopback Default Bind (`127.0.0.1:5100`)
- By default, `Mother` binds Kestrel strictly to `127.0.0.1:5100` (`IPAddress.Loopback`), refusing external network traffic.
- Supervised applications launched by `Mother` bind strictly to `127.0.0.1` behind the gateway (`--managed-by-mother --bind 127.0.0.1 --port <effectivePort>`).
- Non-HTTP / CLI workers (`internalPort <= 0`) fail closed in `ColdWakeProxyMiddleware` with HTTP `503` (`NO_HTTP_ENDPOINT`) rather than dialing port `0`.

### 2. Optional Tailscale CGNAT Allowlist (`100.64.0.0/10`)
- When remote mobile/dashboard access is explicitly enabled (`shouldAllowTailscaleBind: true` in `data\config\settings.json`), `TailscaleAndQrEndpoints.IsLoopbackOrTailscale` inspects every incoming socket connection (including IPv4-mapped IPv6 addresses) and allows **only** loopback (`127.0.0.1` / `::1`) or Tailscale Carrier-Grade NAT (`100.64.0.0/10`) peers.
- Requests from public or local LAN addresses are rejected with HTTP `403` (`FORBIDDEN_SOURCE_IP`).
- Pairing QR codes served at `/api/v1/suite/qr.svg` are generated offline locally via `Foundation.Core.QrCodeEncoder` without external network calls.

### 3. Secrets, Bearer Token Handling & Log Hygiene
- Sensitive outbound credentials are encrypted at rest inside `data\secrets.dpapi` using Windows DPAPI (`DataProtectionScope.CurrentUser`). The inspection endpoint (`/api/v1/suite/secrets/metadata`) returns key names, vault paths, file sizes, and timestamps only—never decrypted secret plaintext.
- Opt-in test automation servers (available only in Debug or test-automation builds) bind strictly to `127.0.0.1`, reject non-loopback `Host` (`127.0.0.1` / `localhost`) or external `Origin` headers with `403`, and verify the required `Authorization: Bearer` header in constant time (`CryptographicOperations.FixedTimeEquals` over SHA-256 token digests), returning `401` on missing or mismatched tokens.
- Rolling daily JSONL logs (`logs\mother-*.jsonl`), crash bundles (`logs\crashes\crash_*.json`), and terminal history buffers do not log Bearer tokens, DPAPI secret values, or host credentials; automation startup logs record only the local loopback port.

### 4. Release Fail-Closed Exclusion of Automation Server (`ExitCode = 2`)
- `Foundation.Automation.dll` is linked and embedded strictly when compiling in `Debug` or `-p:MotherTestAutomation=true`.
- Standard Release builds (`Mother.exe`, `Mother.App.exe`, `Mother-Setup-v<Version>-win-x64.exe`, and the portable ZIP) strip automation assemblies completely and verify their absence during packaging.
- Passing `--automation-port` or `--automation-token` to any Release binary fails closed immediately with **`ExitCode = 2`**.

### 5. Filesystem & Sandbox Path Containment (`ExitCode = 3`)
- When isolated test execution (`--data-dir`) or 4-drive sandbox containment is active, `PathResolver`, `SandboxEnvironmentSeam`, and `WorkerLaunchCommandBuilder` confine manifest roots, configuration files, lockfiles, DPAPI vaults, and child working directories inside the approved containment root while suppressing live host Task Scheduler, registry, drive probing, and OS sleep mutations.
- Attempting to escape containment boundaries fails closed immediately with **`ExitCode = 3`**.
