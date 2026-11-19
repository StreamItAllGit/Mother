// @module: Mother.Core.Features.Supervision.ColdWakeCoordinator
// @purpose: Wakes cold sister apps with 250ms health polling until ready and enforces idle hibernation.
// @depends-on: System, System.Diagnostics, System.Net.Http, System.Threading, System.Threading.Tasks, Mother.Core.Contracts, Mother.Core.Diagnostics
// @exports: WakeOutcomeDto, ColdWakeCoordinator

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Mother.Core.Contracts;
using Mother.Core.Diagnostics;

namespace Mother.Core.Features.Supervision;

public sealed record WakeOutcomeDto(
    [property: JsonPropertyName("appId")] string AppId,
    [property: JsonPropertyName("woken")] bool Woken,
    [property: JsonPropertyName("pid")] int? Pid,
    [property: JsonPropertyName("startupMs")] long StartupMs,
    [property: JsonPropertyName("message")] string Message
);

public sealed class ColdWakeCoordinator
{
    private readonly ProcessSupervisor _supervisor;
    private readonly MotherDiagnostics _diag;
    private readonly HttpClient _http;

    public ColdWakeCoordinator(ProcessSupervisor supervisor, MotherDiagnostics diag, HttpClient? httpClient = null)
    {
        _supervisor = supervisor;
        _diag = diag;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMilliseconds(900) };
    }

    public async Task<WakeOutcomeDto> WakeAsync(SisterAppEntry entry, int timeoutMs = 8000, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _supervisor.TouchActivity(entry.AppId);
        SisterAppRuntimeDto? snap = _supervisor.GetSnapshot(entry.AppId);
        if (snap is { IsOnline: true })
        {
            return new WakeOutcomeDto(entry.AppId, true, snap.Pid, 0, $"{entry.Name} is already warm/online.");
        }

        bool started = _supervisor.TryStartProcess(entry.AppId, out string startMsg);
        int port = snap?.EffectivePort ?? entry.InternalPort;
        if (port <= 0)
        {
            if (!started)
            {
                return new WakeOutcomeDto(entry.AppId, false, null, sw.ElapsedMilliseconds, startMsg);
            }
            SisterAppRuntimeDto? launched = _supervisor.GetSnapshot(entry.AppId);
            return new WakeOutcomeDto(entry.AppId, true, launched?.Pid, sw.ElapsedMilliseconds, $"{entry.Name} started (no HTTP health port configured).");
        }

        string healthUrl = $"http://127.0.0.1:{port}{entry.HealthEndpoint}";
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Clamp(timeoutMs, 500, 30000));
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                using HttpResponseMessage resp = await _http.GetAsync(healthUrl, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    _supervisor.UpdateProbeResult(entry.AppId, true, 0, 0);
                    SisterAppRuntimeDto? ready = _supervisor.GetSnapshot(entry.AppId);
                    _diag.Log("INFO", entry.AppId, "ColdWake", $"Cold-Wake succeeded in {sw.ElapsedMilliseconds}ms.");
                    return new WakeOutcomeDto(entry.AppId, true, ready?.Pid, sw.ElapsedMilliseconds, $"{entry.Name} ready.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                // Child worker may still be binding its HTTP health endpoint during boot.
                if (!started)
                {
                    _diag.Log("WARN", entry.AppId, "ColdWake", $"Health probe failed for '{healthUrl}': {ex.Message}");
                }
            }
            if (!started) break;
            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        return new WakeOutcomeDto(entry.AppId, false, snap?.Pid, sw.ElapsedMilliseconds, startMsg);
    }

    public async Task<IReadOnlyList<string>> EvaluateIdleHibernationAsync(
        IReadOnlyList<SisterAppEntry> manifests,
        IReadOnlyList<SisterAppRuntimeDto> snapshots,
        CancellationToken ct = default,
        DateTime? utcNowOverride = null)
    {
        DateTime now = utcNowOverride ?? DateTime.UtcNow;
        var hibernated = new List<string>();
        var manifestMap = manifests.ToDictionary(m => m.AppId, StringComparer.OrdinalIgnoreCase);

        foreach (SisterAppRuntimeDto snap in snapshots)
        {
            if (ct.IsCancellationRequested) break;
            if (!snap.IsOnline || snap.Pid is null) continue;
            if (snap.ActiveJobs > 0 || snap.QueuedJobs > 0) continue;
            if (!manifestMap.TryGetValue(snap.AppId, out SisterAppEntry? entry)) continue;
            if (!string.Equals(entry.ResidencyMode, "cold", StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.IdleTimeoutMinutes <= 0) continue;

            DateTime? lastActivity = _supervisor.GetLastActivityUtc(snap.AppId);
            if (lastActivity is null) continue;

            if (now - lastActivity.Value >= TimeSpan.FromMinutes(entry.IdleTimeoutMinutes))
            {
                _diag.Log("INFO", entry.AppId, "ColdWake",
                    $"Hibernating idle cold worker after {entry.IdleTimeoutMinutes}m inactivity.");
                if (await _supervisor.StopAppAsync(entry.AppId, ct: ct).ConfigureAwait(false))
                {
                    hibernated.Add(entry.AppId);
                }
            }
        }

        return hibernated;
    }
}
