// @module: Mother.Core.Features.Discovery.ManifestRegistryService
// @purpose: Discovers v1/v2 sister-app manifests across manifests\ and ProgramsRoot, applies settings residency overrides, and supports hot reload.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text, Mother.Core.Configuration, Mother.Core.Contracts, Mother.Core.Diagnostics
// @exports: ManifestRegistryService

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Mother.Core.Configuration;
using Mother.Core.Contracts;
using Mother.Core.Diagnostics;

namespace Mother.Core.Features.Discovery;

public sealed class ManifestRegistryService
{
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

                discovered[parsed.AppId] = parsed;
            }
            catch (Exception ex)
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
            if (File.Exists(p))
            {
                try { return JsonDocument.Parse(File.ReadAllText(p, Encoding.UTF8)); } catch { }
            }
        }
        return null;
    }

    private IEnumerable<string> EnumerateCandidateManifestFiles(MotherSettings cfg)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(_paths.ManifestsDirectory))
        {
            foreach (string f in Directory.EnumerateFiles(_paths.ManifestsDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(f).StartsWith(".", StringComparison.Ordinal)) continue;
                if (seen.Add(Path.GetFullPath(f))) yield return f;
            }
        }

        var peerDirs = new List<string>();
        var scanRoots = new List<string>();
        if (Directory.Exists(_paths.ProgramsRoot)) scanRoots.Add(_paths.ProgramsRoot);

        foreach (string custom in cfg.CustomManifestRoots)
        {
            if (string.IsNullOrWhiteSpace(custom)) continue;
            try
            {
                string resolved = _paths.AssertContainedIfIsolated(custom.Trim());
                if (Directory.Exists(resolved)) scanRoots.Add(resolved);
            }
            catch
            {
                _diag.Log("WARN", "mother", "Discovery", $"Skipped out-of-root customManifestRoot '{custom}'.");
            }
        }

        if (!_paths.IsIsolatedOverride)
        {
            AddHostAppDiscoveryPaths(peerDirs, scanRoots);
        }

        foreach (string dir in peerDirs)
        {
            foreach (string candidate in YieldManifestCandidates(dir))
            {
                if (seen.Add(Path.GetFullPath(candidate))) yield return candidate;
            }
        }

        foreach (string root in scanRoots)
        {
            foreach (string dir in Directory.EnumerateDirectories(root))
            {
                foreach (string candidate in YieldManifestCandidates(dir))
                {
                    if (seen.Add(Path.GetFullPath(candidate))) yield return candidate;
                }
            }
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

    private static void AddHostAppDiscoveryPaths(List<string> peerDirs, List<string> scanRoots)
    {
        if (OperatingSystem.IsWindows())
        {
            using RegistryKey? appsKey = Registry.CurrentUser.OpenSubKey(@"Software\StreamItAll\Mother\Apps");
            if (appsKey is not null)
            {
                foreach (string sub in appsKey.GetSubKeyNames())
                {
                    using RegistryKey? appKey = appsKey.OpenSubKey(sub);
                    string? dir = appKey?.GetValue("InstallDir") as string;
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        peerDirs.Add(Path.GetFullPath(dir));
                    }
                }
            }
        }

        string? sysRoot = Path.GetPathRoot(Environment.SystemDirectory);
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
            if (string.Equals(drive.RootDirectory.FullName, sysRoot, StringComparison.OrdinalIgnoreCase)) continue;
            string legacyPrograms = Path.Combine(drive.RootDirectory.FullName, "Programs");
            if (Directory.Exists(legacyPrograms)) scanRoots.Add(legacyPrograms);
        }
    }
}
