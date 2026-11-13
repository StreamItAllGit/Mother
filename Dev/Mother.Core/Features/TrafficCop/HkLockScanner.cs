// @module: Mother.Core.Features.TrafficCop.HkLockScanner
// @purpose: Scans registered roots and data directories for .hklock files and validates live vs stale PIDs via PidSafeLockfile.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, Foundation.Core, Mother.Core.Contracts
// @exports: HkLockScanner

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Foundation.Core;
using Mother.Core.Contracts;

namespace Mother.Core.Features.TrafficCop;

public sealed class HkLockScanner
{
    public IReadOnlyList<HkLockSnapshotDto> Scan(IEnumerable<string> directories)
    {
        var results = new List<HkLockSnapshotDto>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string rawDir in directories)
        {
            if (string.IsNullOrWhiteSpace(rawDir) || !Directory.Exists(rawDir)) continue;
            string fullDir = Path.GetFullPath(rawDir);
            if (!visited.Add(fullDir)) continue;

            IEnumerable<string> lockFiles;
            try
            {
                lockFiles = Directory.EnumerateFiles(fullDir, "*.hklock", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.EnumerateFiles(fullDir, ".*.hklock", SearchOption.TopDirectoryOnly))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                continue;
            }

            foreach (string lockPath in lockFiles)
            {
                bool isLive = PidSafeLockfile.IsLockedByLiveProcess(lockPath, out PidSafeLockEnvelope? env);
                string drive = Path.GetPathRoot(lockPath)?.TrimEnd('\\') ?? "";
                string lockId = Path.GetFileName(lockPath);
                ReadFallbackJson(lockPath, out var fb);

                if (env is null && fb is null)
                {
                    results.Add(new HkLockSnapshotDto(
                        LockId: lockId,
                        AppId: "unknown",
                        Pid: 0,
                        HostName: Environment.MachineName,
                        Operation: "corrupt-or-empty",
                        DriveMount: drive,
                        LockFilePath: lockPath,
                        Status: "stale",
                        AcquiredAtUtc: "",
                        HeartbeatAtUtc: "",
                        ExpiresAtUtc: ""));
                    continue;
                }

                string acquired = FirstNonEmpty(env?.AcquiredAtUtc, fb?.AcquiredAtUtc, fb?.CreatedAtUtc);
                string heartbeat = FirstNonEmpty(env?.HeartbeatAtUtc, fb?.HeartbeatAtUtc, acquired);
                string expires = FirstNonEmpty(env?.ExpiresAtUtc, fb?.ExpiresAtUtc);

                results.Add(new HkLockSnapshotDto(
                    LockId: lockId,
                    AppId: FirstNonEmpty(env?.AppId, fb?.AppId, "unknown"),
                    Pid: env?.Pid ?? fb?.Pid ?? 0,
                    HostName: FirstNonEmpty(env?.HostName, fb?.HostName, Environment.MachineName),
                    Operation: FirstNonEmpty(env?.Operation, fb?.Operation, "lock"),
                    DriveMount: drive,
                    LockFilePath: lockPath,
                    Status: isLive ? "live" : "stale",
                    AcquiredAtUtc: acquired,
                    HeartbeatAtUtc: heartbeat,
                    ExpiresAtUtc: expires));
            }
        }

        return results;
    }

    private sealed record FallbackLockFields(
        string AppId,
        int Pid,
        string HostName,
        string Operation,
        string AcquiredAtUtc,
        string CreatedAtUtc,
        string HeartbeatAtUtc,
        string ExpiresAtUtc);

    private static void ReadFallbackJson(string lockPath, out FallbackLockFields? fields)
    {
        fields = null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(lockPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            JsonElement root = doc.RootElement;
            int pid = root.TryGetProperty("pid", out var pidEl) && pidEl.TryGetInt32(out int p) ? p : 0;
            fields = new FallbackLockFields(
                GetStr(root, "appId"),
                pid,
                GetStr(root, "hostName"),
                GetStr(root, "operation"),
                GetStr(root, "acquiredAtUtc"),
                GetStr(root, "createdAtUtc"),
                GetStr(root, "heartbeatAtUtc"),
                GetStr(root, "expiresAtUtc"));
        }
        catch
        {
            fields = null;
        }
    }

    private static string GetStr(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : string.Empty;

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (string? c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c)) return c;
        }
        return string.Empty;
    }

    public int PurgeStaleLocks(IEnumerable<string> directories, out IReadOnlyList<string> purgedPaths)
    {
        var purged = new List<string>();
        foreach (HkLockSnapshotDto snap in Scan(directories))
        {
            if (!string.Equals(snap.Status, "stale", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (!PidSafeLockfile.IsLockedByLiveProcess(snap.LockFilePath, out _) && File.Exists(snap.LockFilePath))
                {
                    File.Delete(snap.LockFilePath);
                    purged.Add(snap.LockFilePath);
                }
            }
            catch
            {
                /* ignore locked file race */
            }
        }
        purgedPaths = purged;
        return purged.Count;
    }
}
