// @module: Mother.Core.Features.Supervision.FleetScheduleEvaluator
// @purpose: Evaluates sister manifests schedules[] with coexistence/lock checks, Cold-Wake execution, deferral tracking, and atomic scheduler-state.json persistence.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.IO, System.Linq, System.Net.Http, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Mother.Core.Configuration, Mother.Core.Contracts, Mother.Core.Diagnostics, Mother.Core.Features.Coexistence, Mother.Core.Features.TrafficCop
// @exports: ScheduleRuntimeState, FleetScheduleEvaluator

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Mother.Core.Configuration;
using Mother.Core.Contracts;
using Mother.Core.Diagnostics;
using Mother.Core.Features.Coexistence;
using Mother.Core.Features.TrafficCop;

namespace Mother.Core.Features.Supervision;

public sealed record ScheduleRuntimeState
{
    [JsonPropertyName("appId")] public required string AppId { get; init; }
    [JsonPropertyName("scheduleId")] public required string ScheduleId { get; init; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("cadence")] public string Cadence { get; set; } = "daily";
    [JsonPropertyName("timeUtc")] public string TimeUtc { get; set; } = "03:00";
    [JsonPropertyName("lastRunAtUtc")] public string? LastRunAtUtc { get; set; }
    [JsonPropertyName("nextRunAtUtc")] public string NextRunAtUtc { get; set; } = string.Empty;
    [JsonPropertyName("deferMinutes")] public int DeferMinutes { get; set; }
    [JsonPropertyName("deferReason")] public string? DeferReason { get; set; }
}

public sealed class FleetScheduleEvaluator
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly object _sync = new();
    private readonly string _stateFilePath;
    private readonly ProcessSupervisor _supervisor;
    private readonly ColdWakeCoordinator _coldWake;
    private readonly FleetCoexistenceArbitrator _coexistence;
    private readonly ResourceTrafficCopService _trafficCop;
    private readonly MotherDiagnostics _diag;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, ScheduleRuntimeState> _states = new(StringComparer.OrdinalIgnoreCase);

    public FleetScheduleEvaluator(
        PathResolver paths,
        ProcessSupervisor supervisor,
        ColdWakeCoordinator coldWake,
        FleetCoexistenceArbitrator coexistence,
        ResourceTrafficCopService trafficCop,
        MotherDiagnostics diag)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _stateFilePath = Path.Combine(paths.DataDirectory, "scheduler-state.json");
        _supervisor = supervisor;
        _coldWake = coldWake;
        _coexistence = coexistence;
        _trafficCop = trafficCop;
        _diag = diag;
        LoadState();
    }

    public IReadOnlyList<ScheduleRuntimeState> GetScheduleStates()
    {
        lock (_sync)
        {
            return _states.Values
                .OrderBy(s => s.AppId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.ScheduleId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public async Task EvaluateDueSchedulesAsync(
        IReadOnlyList<SisterAppEntry> manifests,
        CancellationToken ct,
        DateTime? utcNowOverride = null)
    {
        DateTime now = utcNowOverride ?? DateTime.UtcNow;
        IReadOnlyList<HkLockSnapshotDto> locks = _trafficCop.GetCurrentLocks(manifests);
        IReadOnlyList<SisterAppRuntimeDto> snapshots = _supervisor.GetSnapshots();

        foreach (SisterAppEntry app in manifests)
        {
            if (ct.IsCancellationRequested || app.ResidencyMode == "disabled" || app.Schedules.Count == 0)
            {
                continue;
            }

            foreach (SisterScheduleEntry sched in app.Schedules)
            {
                ScheduleRuntimeState state = EnsureRegistered(app.AppId, sched, now);
                if (!TryParseUtc(state.NextRunAtUtc, out DateTime dueUtc) || now < dueUtc)
                {
                    continue;
                }

                ArbitrationDecisionDto decision = _coexistence.ArbitrateColdWake(app, snapshots, locks);
                if (!decision.Allowed)
                {
                    string drive = sched.TargetDrives.FirstOrDefault() ?? app.TargetDrives.FirstOrDefault() ?? "local";
                    await DeferScheduleAsync(app.AppId, sched.ScheduleId, drive, 15, decision.Reason, decision.BlockingAppId, null, ct).ConfigureAwait(false);
                    continue;
                }

                HkLockSnapshotDto? blockingDriveLock = locks.FirstOrDefault(l =>
                    l.Status == "live" &&
                    !l.AppId.Equals(app.AppId, StringComparison.OrdinalIgnoreCase) &&
                    sched.TargetDrives.Any(d => d.Equals(l.DriveMount, StringComparison.OrdinalIgnoreCase)));
                if (blockingDriveLock is not null)
                {
                    await DeferScheduleAsync(
                        app.AppId,
                        sched.ScheduleId,
                        blockingDriveLock.DriveMount,
                        15,
                        $"Drive lock active by '{blockingDriveLock.AppId}' ({blockingDriveLock.Operation}).",
                        blockingDriveLock.AppId,
                        blockingDriveLock.LockFilePath,
                        ct).ConfigureAwait(false);
                    continue;
                }

                WakeOutcomeDto wake = await _coldWake.WakeAsync(app, 8000, ct).ConfigureAwait(false);
                if (wake.Woken && sched.TriggerEndpoint.StartsWith("/", StringComparison.Ordinal))
                {
                    int port = app.InternalPort;
                    if (port > 0)
                    {
                        try
                        {
                            await _http.PostAsync($"http://127.0.0.1:{port}{sched.TriggerEndpoint}", null, ct).ConfigureAwait(false);
                        }
                        catch
                        {
                            /* trigger endpoint invocation is best-effort */
                        }
                    }
                }

                lock (_sync)
                {
                    state.LastRunAtUtc = now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                    state.NextRunAtUtc = ComputeNextAfterRun(sched, now).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                    state.DeferMinutes = 0;
                    state.DeferReason = null;
                    SaveStateUnsynchronized();
                }

                _diag.Log("INFO", app.AppId, "Scheduler", $"Evaluated schedule '{sched.ScheduleId}' ({sched.Cadence}); next run at {state.NextRunAtUtc}.");
            }
        }
    }

    public async Task<ScheduleRuntimeState> DeferScheduleAsync(
        string appId,
        string scheduleId,
        string driveMount,
        int deferMinutes,
        string deferReason,
        string? blockingAppId = null,
        string? lockFilePath = null,
        CancellationToken ct = default)
    {
        int clampedMinutes = Math.Clamp(deferMinutes, 1, 1440);
        DateTime deferredUntil = DateTime.UtcNow.AddMinutes(clampedMinutes);
        string key = BuildKey(appId, scheduleId);
        ScheduleRuntimeState updated;

        lock (_sync)
        {
            if (!_states.TryGetValue(key, out ScheduleRuntimeState? existing))
            {
                existing = new ScheduleRuntimeState
                {
                    AppId = appId.Trim().ToLowerInvariant(),
                    ScheduleId = scheduleId.Trim(),
                    Name = scheduleId.Trim()
                };
                _states[key] = existing;
            }

            existing.NextRunAtUtc = deferredUntil.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            existing.DeferMinutes = clampedMinutes;
            existing.DeferReason = deferReason;
            SaveStateUnsynchronized();
            updated = existing with { };
        }

        await _trafficCop.RecordDeferralAsync(
            appId,
            scheduleId,
            string.IsNullOrWhiteSpace(driveMount) ? "local" : driveMount,
            blockingAppId ?? "scheduler",
            lockFilePath ?? string.Empty,
            clampedMinutes,
            ct).ConfigureAwait(false);

        return updated;
    }

    private ScheduleRuntimeState EnsureRegistered(string appId, SisterScheduleEntry sched, DateTime now)
    {
        string key = BuildKey(appId, sched.ScheduleId);
        lock (_sync)
        {
            if (!_states.TryGetValue(key, out ScheduleRuntimeState? state))
            {
                DateTime initialNext = ComputeInitialNextUtc(sched, now);
                state = new ScheduleRuntimeState
                {
                    AppId = appId,
                    ScheduleId = sched.ScheduleId,
                    Name = sched.Name,
                    Cadence = sched.Cadence,
                    TimeUtc = sched.TimeUtc,
                    NextRunAtUtc = initialNext.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };
                _states[key] = state;
                SaveStateUnsynchronized();
            }
            return state;
        }
    }

    internal static DateTime ComputeInitialNextUtc(SisterScheduleEntry sched, DateTime now)
    {
        TimeSpan at = ParseTimeUtc(sched.TimeUtc);
        DateTime todaySlot = now.Date.Add(at);
        return sched.RunIfMissed && now >= todaySlot
            ? now
            : (now < todaySlot ? todaySlot : ComputeNextAfterRun(sched, now));
    }

    internal static DateTime ComputeNextAfterRun(SisterScheduleEntry sched, DateTime fromUtc)
    {
        TimeSpan at = ParseTimeUtc(sched.TimeUtc);
        string cadence = (sched.Cadence ?? "daily").Trim().ToLowerInvariant();
        if (cadence == "hourly")
        {
            DateTime slot = new DateTime(fromUtc.Year, fromUtc.Month, fromUtc.Day, fromUtc.Hour, at.Minutes, 0, DateTimeKind.Utc);
            return slot <= fromUtc ? slot.AddHours(1) : slot;
        }
        int stepDays = cadence == "weekly" ? 7 : 1;
        DateTime candidate = fromUtc.Date.Add(at);
        while (candidate <= fromUtc)
        {
            candidate = candidate.AddDays(stepDays);
        }
        return candidate;
    }

    private static TimeSpan ParseTimeUtc(string? timeUtc)
    {
        if (!string.IsNullOrWhiteSpace(timeUtc) &&
            TimeSpan.TryParseExact(timeUtc.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan parsed))
        {
            return parsed;
        }
        return new TimeSpan(3, 0, 0);
    }

    private static bool TryParseUtc(string? iso, out DateTime dt) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt);

    private static string BuildKey(string appId, string scheduleId) =>
        $"{appId.Trim().ToLowerInvariant()}:{scheduleId.Trim()}";

    private void LoadState()
    {
        if (!File.Exists(_stateFilePath)) return;
        try
        {
            List<ScheduleRuntimeState>? list = JsonSerializer.Deserialize<List<ScheduleRuntimeState>>(File.ReadAllText(_stateFilePath, Encoding.UTF8), JsonOpts);
            if (list is null) return;
            lock (_sync)
            {
                foreach (ScheduleRuntimeState item in list)
                {
                    _states[BuildKey(item.AppId, item.ScheduleId)] = item;
                }
            }
        }
        catch (Exception ex)
        {
            _diag.Log("WARN", "mother", "Scheduler", $"Failed loading scheduler state; starting fresh: {ex.Message}");
        }
    }

    private void SaveStateUnsynchronized()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);
        string tmp = $"{_stateFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            string json = JsonSerializer.Serialize(_states.Values.ToList(), JsonOpts);
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            File.Move(tmp, _stateFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                try { File.Delete(tmp); }
                catch (Exception ex) { _diag.Log("WARN", "mother", "Scheduler", $"Temp scheduler state cleanup failed: {ex.Message}"); }
            }
        }
    }
}
