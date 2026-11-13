// @module: Mother.Core.Features.TrafficCop.ResourceTrafficCopService
// @purpose: Arbitrates drive/mount .hklock contention, persists schedule_deferrals to SQLite, and purges stale orphan locks.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text.Json, System.Threading, System.Threading.Tasks, Mother.Core.Configuration, Mother.Core.Contracts, Mother.Core.Diagnostics, Mother.Core.Persistence
// @exports: TrafficCopStatusDto, ResourceTrafficCopService

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Mother.Core.Configuration;
using Mother.Core.Contracts;
using Mother.Core.Diagnostics;
using Mother.Core.Persistence;

namespace Mother.Core.Features.TrafficCop;

public sealed record TrafficCopStatusDto(
    [property: JsonPropertyName("activeLockCount")] int ActiveLockCount,
    [property: JsonPropertyName("staleLockCount")] int StaleLockCount,
    [property: JsonPropertyName("activeLocks")] IReadOnlyList<HkLockSnapshotDto> ActiveLocks,
    [property: JsonPropertyName("recentDeferrals")] IReadOnlyList<ScheduleDeferralRecord> RecentDeferrals
);

public sealed class ResourceTrafficCopService
{
    private readonly HkLockScanner _scanner = new();
    private readonly PathResolver _paths;
    private readonly MotherDb _db;
    private readonly MotherDiagnostics _diag;

    public event Action<IReadOnlyList<HkLockSnapshotDto>>? LocksChanged;

    public ResourceTrafficCopService(PathResolver paths, MotherDb db, MotherDiagnostics diag)
    {
        _paths = paths;
        _db = db;
        _diag = diag;
    }

    public IReadOnlyList<HkLockSnapshotDto> GetCurrentLocks(IEnumerable<SisterAppEntry> apps)
    {
        IEnumerable<string> dirs = CollectScanDirectories(apps);
        return _scanner.Scan(dirs);
    }

    public async Task<TrafficCopStatusDto> GetStatusAsync(IEnumerable<SisterAppEntry> apps, CancellationToken ct = default)
    {
        IReadOnlyList<HkLockSnapshotDto> locks = GetCurrentLocks(apps);
        IReadOnlyList<ScheduleDeferralRecord> deferrals = await _db.GetDeferralsAsync(100, ct).ConfigureAwait(false);
        return new TrafficCopStatusDto(
            ActiveLockCount: locks.Count(l => l.Status == "live"),
            StaleLockCount: locks.Count(l => l.Status == "stale"),
            ActiveLocks: locks,
            RecentDeferrals: deferrals);
    }

    public async Task<int> PurgeStaleLocksAsync(IEnumerable<SisterAppEntry> apps, CancellationToken ct = default)
    {
        IEnumerable<string> dirs = CollectScanDirectories(apps);
        int count = _scanner.PurgeStaleLocks(dirs, out IReadOnlyList<string> purgedPaths);
        if (count > 0)
        {
            string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            string meta = JsonSerializer.Serialize(new { eventKind = "stale_lock_purged", purgedPaths });
            await _db.InsertHealthEventAsync(new FleetHealthEventRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                SuiteId = "mother",
                AppId = "mother",
                Status = "stale_lock_purged",
                ResidencyMode = "warm",
                IsOnline = 1,
                MetadataJson = meta,
                CreatedAtUtc = nowUtc
            }, ct).ConfigureAwait(false);

            _diag.Log("INFO", "mother", "TrafficCop", $"Purged {count} stale .hklock sentinel(s).");
            LocksChanged?.Invoke(GetCurrentLocks(apps));
        }
        return count;
    }

    public async Task<ScheduleDeferralRecord> RecordDeferralAsync(
        string deferredAppId,
        string jobId,
        string driveMount,
        string blockingAppId,
        string lockFilePath,
        int minutes = 30,
        CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;
        long durationMs = Math.Max(1, minutes) * 60_000L;
        string meta = JsonSerializer.Serialize(new { blockingAppId, lockFilePath });
        var rec = new ScheduleDeferralRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SuiteId = "mother",
            AppId = deferredAppId,
            JobId = jobId,
            DriveMount = driveMount,
            DurationMs = durationMs,
            Description = $"Suite Drive I/O Mutex: {driveMount} locked by {blockingAppId}",
            Status = "deferred",
            IsDryRun = _paths.Options.DryRun ? 1 : 0,
            NextRunAtUtc = now.AddMilliseconds(durationMs).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            MetadataJson = meta,
            CreatedAtUtc = now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };

        await _db.InsertDeferralAsync(rec, ct).ConfigureAwait(false);
        _diag.Log("WARN", deferredAppId, "TrafficCop", $"Deferred '{jobId}' for {minutes}m ({rec.Description}).");
        return rec;
    }

    private IEnumerable<string> CollectScanDirectories(IEnumerable<SisterAppEntry> apps)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            _paths.DataDirectory,
            Path.Combine(_paths.DataDirectory, "locks")
        };
        foreach (SisterAppEntry app in apps)
        {
            if (!string.IsNullOrWhiteSpace(app.InstallDir)) dirs.Add(app.InstallDir);
            foreach (string m in app.SharedMounts)
            {
                if (!string.IsNullOrWhiteSpace(m)) dirs.Add(m);
            }
        }
        return dirs;
    }
}
