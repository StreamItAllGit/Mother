// @module: Mother.Core.Features.Coexistence.FleetCoexistenceArbitrator
// @purpose: Evaluates cross-app conflicts (MutualExclusion, DriveMountLock, PortCollision, ManifestSchemaWarning), reads .coexistence.json, and brokers recommendations.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text, System.Text.Json, Mother.Core.Configuration, Mother.Core.Contracts, Mother.Core.Diagnostics
// @exports: ArbitrationDecisionDto, FleetCoexistenceArbitrator

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mother.Core.Configuration;
using Mother.Core.Contracts;
using Mother.Core.Diagnostics;

namespace Mother.Core.Features.Coexistence;

public sealed record ArbitrationDecisionDto(
    [property: JsonPropertyName("allowed")] bool Allowed,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("blockingAppId")] string? BlockingAppId,
    [property: JsonPropertyName("conflicts")] IReadOnlyList<FleetConflictDto> Conflicts
);

public sealed class FleetCoexistenceArbitrator
{
    private readonly PathResolver _paths;
    private readonly SettingsStore _settings;
    private readonly MotherDiagnostics _diag;

    public FleetCoexistenceArbitrator(PathResolver paths, SettingsStore settings, MotherDiagnostics diag)
    {
        _paths = paths;
        _settings = settings;
        _diag = diag;
    }

    public CoexistenceStatusDto Evaluate(
        IReadOnlyList<SisterAppRuntimeDto> apps,
        IReadOnlyList<HkLockSnapshotDto> locks)
    {
        var conflicts = new List<FleetConflictDto>();
        string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var sharedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (SisterAppRuntimeDto app in apps)
        {
            foreach (string mount in app.SharedMounts)
            {
                if (!string.IsNullOrWhiteSpace(mount)) sharedRoots.Add(mount);
            }
        }

        // 1. Port Collisions
        foreach (var group in apps.Where(a => a.EffectivePort > 0 && a.Residency != "disabled").GroupBy(a => a.EffectivePort))
        {
            var list = group.ToList();
            if (list.Count > 1)
            {
                conflicts.Add(new FleetConflictDto(
                    ConflictId: $"port-{group.Key}",
                    Kind: "PortCollision",
                    Severity: "high",
                    PrimaryAppId: list[0].AppId,
                    SecondaryAppId: list[1].AppId,
                    ResourceKey: $":{group.Key}",
                    Summary: $"Port :{group.Key} claimed by both {list[0].Name} and {list[1].Name}.",
                    RecommendedPreset: "reassign-port",
                    DetectedAtUtc: nowUtc));
            }
        }

        // 2. Mutual Exclusions & Live .hklock Drive Contention
        var liveLocks = locks.Where(l => l.Status == "live").ToList();
        for (int i = 0; i < apps.Count; i++)
        {
            SisterAppRuntimeDto a = apps[i];
            if (a.Residency == "disabled") continue;

            foreach (string exId in a.MutuallyExclusiveWith)
            {
                SisterAppRuntimeDto? peer = apps.FirstOrDefault(x => x.AppId.Equals(exId, StringComparison.OrdinalIgnoreCase));
                if (peer is not null && (a.IsOnline || liveLocks.Any(l => l.AppId == a.AppId)) &&
                    (peer.IsOnline || liveLocks.Any(l => l.AppId == peer.AppId)))
                {
                    conflicts.Add(new FleetConflictDto(
                        ConflictId: $"mutex-{a.AppId}-{peer.AppId}",
                        Kind: "MutualExclusion",
                        Severity: "high",
                        PrimaryAppId: a.AppId,
                        SecondaryAppId: peer.AppId,
                        ResourceKey: "mutual-exclusion",
                        Summary: $"{a.Name} and {peer.Name} are marked mutually exclusive while both active.",
                        RecommendedPreset: "zero-rewrite-parity",
                        DetectedAtUtc: nowUtc));
                }
            }

            foreach (HkLockSnapshotDto lk in liveLocks)
            {
                if (!lk.AppId.Equals(a.AppId, StringComparison.OrdinalIgnoreCase) &&
                    a.TargetDrives.Any(d => d.Equals(lk.DriveMount, StringComparison.OrdinalIgnoreCase)) &&
                    a.ActiveJobs > 0)
                {
                    conflicts.Add(new FleetConflictDto(
                        ConflictId: $"drive-{lk.DriveMount}-{lk.AppId}-{a.AppId}",
                        Kind: "DriveMountLock",
                        Severity: "medium",
                        PrimaryAppId: lk.AppId,
                        SecondaryAppId: a.AppId,
                        ResourceKey: lk.DriveMount,
                        Summary: $"Drive {lk.DriveMount} locked by {lk.AppId} ({lk.Operation}); {a.Name} should defer.",
                        RecommendedPreset: "defer-competing-schedules",
                        DetectedAtUtc: nowUtc));
                }
            }
        }

        // 3. Offline .coexistence.json root drift inspection
        foreach (string root in sharedRoots)
        {
            string coexPath = Path.Combine(root, ".coexistence.json");
            if (!File.Exists(coexPath)) continue;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(coexPath, Encoding.UTF8));
                if (doc.RootElement.TryGetProperty("settingsDrift", out JsonElement drift) &&
                    drift.ValueKind == JsonValueKind.True)
                {
                    conflicts.Add(new FleetConflictDto(
                        ConflictId: $"drift-{Math.Abs(root.GetHashCode())}",
                        Kind: "SettingsDrift",
                        Severity: "low",
                        PrimaryAppId: "shadowlibrary",
                        SecondaryAppId: "housekeeping",
                        ResourceKey: root,
                        Summary: $"Shared root '{root}' has unaligned .coexistence.json parity flags.",
                        RecommendedPreset: "zero-rewrite-parity",
                        DetectedAtUtc: nowUtc));
                }
            }
            catch
            {
                /* ignore malformed coexistence file */
            }
        }

        return new CoexistenceStatusDto(
            ShouldAutoAlignSisterApps: _settings.GetCurrent().ShouldAutoAlignSisterApps,
            SharedRoots: sharedRoots.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList(),
            Conflicts: conflicts,
            Apps: apps);
    }

    public ArbitrationDecisionDto ArbitrateColdWake(
        SisterAppEntry target,
        IReadOnlyList<SisterAppRuntimeDto> apps,
        IReadOnlyList<HkLockSnapshotDto> locks)
    {
        var blockingLocks = locks.Where(l => l.Status == "live").ToList();
        foreach (string exId in target.MutuallyExclusiveWith)
        {
            bool peerRunning = apps.Any(a => a.AppId.Equals(exId, StringComparison.OrdinalIgnoreCase) && a.IsOnline);
            bool peerLocked = blockingLocks.Any(l => l.AppId.Equals(exId, StringComparison.OrdinalIgnoreCase));
            if (peerRunning || peerLocked)
            {
                var status = Evaluate(apps, locks);
                return new ArbitrationDecisionDto(
                    Allowed: false,
                    Decision: "defer",
                    Reason: $"Mutual exclusion active: '{target.AppId}' conflicts with running/locked '{exId}'.",
                    BlockingAppId: exId,
                    Conflicts: status.Conflicts);
            }
        }

        return new ArbitrationDecisionDto(
            Allowed: true,
            Decision: "allow",
            Reason: "No mutual exclusion or active lock conflict.",
            BlockingAppId: null,
            Conflicts: Array.Empty<FleetConflictDto>());
    }

    public IReadOnlyList<string> ApplyRecommendations(IReadOnlyList<string>? targetAppIds, string presetId, IEnumerable<string> sharedRoots)
    {
        var updated = new List<string>();
        string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        foreach (string root in sharedRoots)
        {
            try
            {
                string safeRoot = _paths.AssertContainedIfIsolated(root);
                Directory.CreateDirectory(safeRoot);
                string coexPath = Path.Combine(safeRoot, ".coexistence.json");
                var payload = new
                {
                    schemaVersion = 1,
                    rootPath = safeRoot,
                    updatedAtUtc = nowUtc,
                    appliedPreset = string.IsNullOrWhiteSpace(presetId) ? "zero-rewrite-parity" : presetId,
                    settingsDrift = false,
                    targetAppIds = targetAppIds ?? Array.Empty<string>()
                };
                File.WriteAllText(coexPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                updated.Add(safeRoot);
            }
            catch (Exception ex)
            {
                _diag.Log("WARN", "mother", "Coexistence", $"Skipped coexistence write for '{root}': {ex.Message}");
            }
        }

        _diag.Log("INFO", "mother", "Coexistence", $"Applied preset '{presetId}' across {updated.Count} shared root(s).");
        return updated;
    }
}
