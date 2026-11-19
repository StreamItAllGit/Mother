// @module: Mother.Core.Features.Discovery.ManifestRegistryService
// @purpose: Discovers v1/v2 sister-app manifests by strict precedence (TryAdd), applies settings residency overrides, and enforces Stage 2 containment.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Security, System.Text, System.Text.Json, Foundation.Core, Mother.Core.Configuration, Mother.Core.Contracts, Mother.Core.Diagnostics
// @exports: ManifestRegistryService

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using Foundation.Core;
using Mother.Core.Configuration;
using Mother.Core.Contracts;
using Mother.Core.Diagnostics;

namespace Mother.Core.Features.Discovery;

public sealed class ManifestRegistryService
{
    private const string RegistryAppsSubKey = @"Software\StreamItAll\Mother\Apps";

    private readonly object _sync = new();
    private readonly PathResolver _paths;
    private readonly SettingsStore _settings;
    private readonly MotherDiagnostics _diag;
    private readonly Dictionary<string, SisterAppEntry> _apps = new(StringComparer.OrdinalIgnoreCase);

    public ManifestRegistryService(PathResolver paths, SettingsStore settings, MotherDiagnostics diag)
    {
        _paths = paths;
        _settings = settings;
        _diag = diag;
        Reload();
    }

    public IReadOnlyList<SisterAppEntry> GetAll()
    {
        lock (_sync)
        {
            return _apps.Values
                .OrderBy(a => a.InternalPort)
                .ThenBy(a => a.AppId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public SisterAppEntry? GetById(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return null;
        lock (_sync)
        {
            return _apps.TryGetValue(appId.Trim(), out SisterAppEntry? entry) ? entry : null;
        }
    }

    public int GetEffectivePort(SisterAppEntry entry) => entry.InternalPort;

    public IReadOnlyList<SisterAppEntry> Reload()
    {
        MotherSettings cfg = _settings.GetCurrent();
        var discovered = new Dictionary<string, SisterAppEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in EnumerateCandidateManifestFiles(cfg))
        {
            try
            {
                string raw = File.ReadAllText(file, Encoding.UTF8);
                SisterAppEntry? parsed = ManifestParser.Parse(file, raw);
                if (parsed is null) continue;

                if (cfg.ResidencyOverrides.TryGetValue(parsed.AppId, out string? overrideMode) &&
                    overrideMode is "warm" or "cold" or "disabled")
                {
                    parsed = parsed with { ResidencyMode = overrideMode };
                }

                // First-wins precedence: data\manifests > ProgramsRoot > Registry > CustomManifestRoots > legacy <Drive>:\Programs.
                discovered.TryAdd(parsed.AppId, parsed);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _diag.Log("WARN", "mother", "Discovery", $"Failed to parse manifest '{file}': {ex.Message}");
            }
        }

        lock (_sync)
        {
            _apps.Clear();
            foreach (var kv in discovered)
            {
                _apps[kv.Key] = kv.Value;
            }
        }

        _diag.Log("INFO", "mother", "Discovery", $"Manifest reload complete: {discovered.Count} sister apps registered.");
        return GetAll();
    }

    public JsonDocument? ReadRawSuiteManifest()
    {
        string[] candidates =
        {
            Path.Combine(_paths.BaseDirectory, "suite.manifest.json"),
            Path.Combine(_paths.DataDirectory, "suite.manifest.json"),
            Path.Combine(_paths.BaseDirectory, "..", "suite.manifest.json")
        };
        foreach (string p in candidates)
        {
            if (!File.Exists(p)) continue;
            try
            {
                return JsonDocument.Parse(File.ReadAllText(p, Encoding.UTF8));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _diag.Log("WARN", "mother", "Discovery", $"Failed reading suite manifest '{p}': {ex.Message}");
            }
        }
        return null;
    }

    private IEnumerable<string> EnumerateCandidateManifestFiles(MotherSettings cfg)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. data\manifests\
        if (Directory.Exists(_paths.ManifestsDirectory))
        {
            foreach (string f in SafeEnumerateFiles(_paths.ManifestsDirectory, "*.json"))
            {
                if (Path.GetFileName(f).StartsWith(".", StringComparison.Ordinal)) continue;
                if (seen.Add(Path.GetFullPath(f))) yield return f;
            }
        }

        // 2. ProgramsRoot umbrella
        if (Directory.Exists(_paths.ProgramsRoot))
        {
            foreach (string dir in SafeEnumerateDirectories(_paths.ProgramsRoot))
            {
                foreach (string candidate in YieldManifestCandidates(dir))
                {
                    if (seen.Add(Path.GetFullPath(candidate))) yield return candidate;
                }
            }
        }

        // 3. _paths.Registry (PlatformRegistryHive.CurrentUser, Software\StreamItAll\Mother\Apps)
        foreach (string candidate in EnumerateRegistryManifests())
        {
            if (seen.Add(Path.GetFullPath(candidate))) yield return candidate;
        }

        // 4. CustomManifestRoots
        foreach (string custom in cfg.CustomManifestRoots)
        {
            if (string.IsNullOrWhiteSpace(custom)) continue;
            string resolved;
            try
            {
                resolved = _paths.AssertContainedIfIsolatedOrSandboxed(custom.Trim());
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                _diag.Log("WARN", "mother", "Discovery", $"Skipped out-of-root customManifestRoot '{custom}': {ex.Message}");
                continue;
            }

            if (!Directory.Exists(resolved)) continue;
            foreach (string candidate in YieldManifestCandidates(resolved))
            {
                if (seen.Add(Path.GetFullPath(candidate))) yield return candidate;
            }
            foreach (string dir in SafeEnumerateDirectories(resolved))
            {
                foreach (string candidate in YieldManifestCandidates(dir))
                {
                    if (seen.Add(Path.GetFullPath(candidate))) yield return candidate;
                }
            }
        }

        // 5. Legacy <Drive>:\Programs (live drives gated on !ShouldSuppressLiveHostMutations)
        foreach (string legacyRoot in EnumerateLegacyProgramsRoots())
        {
            foreach (string dir in SafeEnumerateDirectories(legacyRoot))
            {
                foreach (string candidate in YieldManifestCandidates(dir))
                {
                    if (seen.Add(Path.GetFullPath(candidate))) yield return candidate;
                }
            }
        }
    }

    private IEnumerable<string> EnumerateRegistryManifests()
    {
        IReadOnlyList<string> subKeys;
        try
        {
            subKeys = _paths.Registry.GetSubKeyNames(PlatformRegistryHive.CurrentUser, RegistryAppsSubKey);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException or TimeoutException)
        {
            _diag.Log("WARN", "mother", "Discovery", $"Skipped registry app discovery: {ex.Message}");
            yield break;
        }

        foreach (string sub in subKeys)
        {
            string? dir;
            try
            {
                dir = _paths.Registry.GetValue(PlatformRegistryHive.CurrentUser, $@"{RegistryAppsSubKey}\{sub}", "InstallDir")?.AsString();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException or TimeoutException)
            {
                _diag.Log("WARN", "mother", "Discovery", $"Failed reading registry InstallDir for '{sub}': {ex.Message}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(dir)) continue;
            string resolved;
            try
            {
                resolved = _paths.AssertContainedIfIsolatedOrSandboxed(dir.Trim());
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                _diag.Log("WARN", "mother", "Discovery", $"Skipped out-of-root registry InstallDir for '{sub}': {ex.Message}");
                continue;
            }

            if (!Directory.Exists(resolved)) continue;
            foreach (string candidate in YieldManifestCandidates(resolved))
            {
                yield return candidate;
            }
        }
    }

    private IEnumerable<string> EnumerateLegacyProgramsRoots()
    {
        if (_paths.ShouldSuppressLiveHostMutations)
        {
            if (_paths.IsSandboxActive && !string.IsNullOrWhiteSpace(_paths.Sandbox.StorageDriveRoot))
            {
                string? sandboxPrograms = TryGetContainedDirectory(Path.Combine(_paths.Sandbox.StorageDriveRoot, "Programs"));
                if (sandboxPrograms is not null)
                {
                    yield return sandboxPrograms;
                }
            }
            yield break;
        }

        string sysDir = KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.System, SandboxResolution.Inactive);
        string? sysRoot = string.IsNullOrWhiteSpace(sysDir) ? null : Path.GetPathRoot(sysDir);
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            string? legacyPrograms = TryResolveHostDrivePrograms(drive, sysRoot);
            if (legacyPrograms is not null)
            {
                yield return legacyPrograms;
            }
        }
    }

    private string? TryGetContainedDirectory(string candidatePath)
    {
        try
        {
            string resolved = _paths.AssertContainedIfIsolatedOrSandboxed(candidatePath);
            return Directory.Exists(resolved) ? resolved : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or IOException)
        {
            _diag.Log("WARN", "mother", "Discovery", $"Skipped non-contained sandbox Programs root '{candidatePath}': {ex.Message}");
            return null;
        }
    }

    private string? TryResolveHostDrivePrograms(DriveInfo drive, string? sysRoot)
    {
        try
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) return null;
            if (!string.IsNullOrWhiteSpace(sysRoot) &&
                string.Equals(drive.RootDirectory.FullName, sysRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            string legacyPrograms = Path.Combine(drive.RootDirectory.FullName, "Programs");
            return Directory.Exists(legacyPrograms) ? legacyPrograms : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DriveNotFoundException)
        {
            _diag.Log("WARN", "mother", "Discovery", $"Skipped inaccessible drive '{drive.Name}': {ex.Message}");
            return null;
        }
    }

    private IEnumerable<string> SafeEnumerateFiles(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _diag.Log("WARN", "mother", "Discovery", $"Skipped unreadable manifest directory '{directory}': {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _diag.Log("WARN", "mother", "Discovery", $"Skipped unreadable scan root '{root}': {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> YieldManifestCandidates(string dir)
    {
        string[] names =
        {
            Path.Combine(dir, "suite.manifest.json"),
            Path.Combine(dir, "mother.manifest.json"),
            Path.Combine(dir, "app", "suite.manifest.json"),
            Path.Combine(dir, "app", "mother.manifest.json")
        };
        foreach (string candidate in names)
        {
            if (File.Exists(candidate)) yield return candidate;
        }
    }
}
