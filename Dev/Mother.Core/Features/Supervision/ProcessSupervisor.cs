// @module: Mother.Core.Features.Supervision.ProcessSupervisor
// @purpose: Supervises child sister-app workers with JobObjectGuard, 3x/60s crash-loop circuit breaker, and graceful 5s drain.
// @depends-on: System, System.Collections.Concurrent, System.Collections.Generic, System.ComponentModel, System.Diagnostics, System.IO, System.Linq, System.Threading, System.Threading.Tasks, Foundation.Core, Mother.Core.Configuration, Mother.Core.Contracts, Mother.Core.Diagnostics
// @exports: ProcessSupervisor

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;
using Mother.Core.Configuration;
using Mother.Core.Contracts;
using Mother.Core.Diagnostics;

namespace Mother.Core.Features.Supervision;

public sealed class ProcessSupervisor : IDisposable
{
    private sealed class ManagedChild
    {
        public required SisterAppEntry Manifest { get; set; }
        public Process? Process { get; set; }
        public string State { get; set; } = "stopped";
        public bool IsOnline { get; set; }
        public int ActiveJobs { get; set; }
        public int QueuedJobs { get; set; }
        public int RetryCount { get; set; }
        public bool CircuitBreakerOpen { get; set; }
        public Queue<DateTime> RecentCrashesUtc { get; } = new();
        public string? LastProbeUtc { get; set; }
        public string? LastError { get; set; }
        public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
    }

    private readonly ConcurrentDictionary<string, ManagedChild> _children = new(StringComparer.OrdinalIgnoreCase);
    private readonly JobObjectGuard _jobGuard = new();
    private readonly PathResolver _paths;
    private readonly SettingsStore _settings;
    private readonly MotherDiagnostics _diag;

    public event Action<SisterAppRuntimeDto>? AppStateChanged;

    public ProcessSupervisor(PathResolver paths, SettingsStore settings, MotherDiagnostics diag)
    {
        _paths = paths;
        _settings = settings;
        _diag = diag;
    }

    public void SyncManifests(IEnumerable<SisterAppEntry> manifests)
    {
        var list = manifests.ToList();
        var ids = new HashSet<string>(list.Select(m => m.AppId), StringComparer.OrdinalIgnoreCase);
        foreach (string existing in _children.Keys.ToList())
        {
            if (!ids.Contains(existing)) _children.TryRemove(existing, out _);
        }

        foreach (SisterAppEntry m in list)
        {
            _children.AddOrUpdate(
                m.AppId,
                _ => new ManagedChild
                {
                    Manifest = m,
                    State = m.ResidencyMode == "disabled" ? "disabled" : "stopped"
                },
                (_, cur) =>
                {
                    cur.Manifest = m;
                    if (m.ResidencyMode == "disabled" && cur.Process is null) cur.State = "disabled";
                    return cur;
                });
        }
    }

    public IReadOnlyList<SisterAppRuntimeDto> GetSnapshots() =>
        _children.Values
            .OrderBy(c => c.Manifest.InternalPort)
            .ThenBy(c => c.Manifest.AppId, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto)
            .ToList();

    public SisterAppRuntimeDto? GetSnapshot(string appId) =>
        _children.TryGetValue(appId, out ManagedChild? c) ? ToDto(c) : null;

    public DateTime? GetLastActivityUtc(string appId) =>
        _children.TryGetValue(appId, out ManagedChild? c) ? c.LastActivityUtc : null;

    public void TouchActivity(string appId)
    {
        if (_children.TryGetValue(appId, out ManagedChild? c))
        {
            c.LastActivityUtc = DateTime.UtcNow;
        }
    }

    public void UpdateProbeResult(string appId, bool isOnline, int activeJobs, int queuedJobs, string? errorMessage = null)
    {
        if (!_children.TryGetValue(appId, out ManagedChild? c)) return;
        bool changed = c.IsOnline != isOnline || c.ActiveJobs != activeJobs || c.QueuedJobs != queuedJobs;
        c.IsOnline = isOnline;
        c.ActiveJobs = activeJobs;
        c.QueuedJobs = queuedJobs;
        c.LastProbeUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        if (isOnline)
        {
            c.State = activeJobs > 0 ? "busy" : "idle";
            c.LastError = null;
            c.LastActivityUtc = DateTime.UtcNow;
        }
        else if (c.Process is null && c.State != "starting" && c.State != "error" && c.State != "disabled")
        {
            c.State = "stopped";
            if (!string.IsNullOrWhiteSpace(errorMessage)) c.LastError = errorMessage;
        }
        if (changed) AppStateChanged?.Invoke(ToDto(c));
    }

    public bool TryStartProcess(string appId, out string message)
    {
        if (!_children.TryGetValue(appId, out ManagedChild? c))
        {
            message = $"App '{appId}' is not registered.";
            return false;
        }
        if (c.Manifest.ResidencyMode == "disabled")
        {
            message = $"App '{appId}' is disabled.";
            return false;
        }
        if (c.CircuitBreakerOpen)
        {
            message = $"App '{appId}' crash-loop circuit breaker is open (3 crashes in 60s).";
            return false;
        }
        if (c.Process is { HasExited: false })
        {
            message = $"App '{appId}' process is already running (PID {c.Process.Id}).";
            return true;
        }

        int port = c.Manifest.InternalPort;
        MotherSettings cfg = _settings.GetCurrent();
        int motherPort = _paths.Options.PortOverride ?? cfg.Port;
        ProcessStartInfo? psi;
        try
        {
            psi = WorkerLaunchCommandBuilder.BuildStartInfo(
                c.Manifest,
                _paths,
                port,
                motherPort,
                out _,
                out string unavailableReason);
            if (psi is null)
            {
                c.State = "stopped";
                c.LastError = unavailableReason;
                message = unavailableReason;
                return false;
            }
        }
        catch (Exception ex)
        {
            c.State = "error";
            c.LastError = ex.Message;
            message = ex.Message;
            _diag.Log("WARN", c.Manifest.AppId, "Supervision", $"Rejected child launch: {ex.Message}");
            return false;
        }

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) _diag.Log("INFO", c.Manifest.AppId, "ChildStdout", e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) _diag.Log("WARN", c.Manifest.AppId, "ChildStderr", e.Data); };
        proc.Exited += (_, _) => OnChildExited(c, proc);

        if (!proc.Start())
        {
            c.State = "error";
            c.LastError = "Failed to start child process.";
            message = c.LastError;
            return false;
        }

        _jobGuard.TryAttachProcess(proc);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        c.Process = proc;
        c.State = "starting";
        c.LastActivityUtc = DateTime.UtcNow;
        AppStateChanged?.Invoke(ToDto(c));
        message = $"Started {c.Manifest.Name} on 127.0.0.1:{port} (PID {proc.Id}).";
        _diag.Log("INFO", c.Manifest.AppId, "Supervision", message);
        return true;
    }

    public async Task<bool> StopAppAsync(string appId, int gracefulTimeoutMs = 5000, CancellationToken ct = default)
    {
        if (!_children.TryGetValue(appId, out ManagedChild? c)) return false;
        Process? proc = c.Process;
        if (proc is null || proc.HasExited)
        {
            c.Process = null;
            c.IsOnline = false;
            c.State = c.Manifest.ResidencyMode == "disabled" ? "disabled" : "stopped";
            AppStateChanged?.Invoke(ToDto(c));
            return true;
        }

        try
        {
            if (!proc.CloseMainWindow()) proc.Kill(entireProcessTree: true);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Math.Clamp(gracefulTimeoutMs, 500, 15000));
            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Graceful close timeout or caller cancellation elapsed; terminate process tree cleanly.
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or IOException)
        {
            // Child process may have already exited during teardown race.
            _diag.Log("INFO", appId, "Supervision", $"Process stop race resolved for '{appId}': {ex.Message}");
        }

        c.Process = null;
        c.IsOnline = false;
        c.State = c.Manifest.ResidencyMode == "disabled" ? "disabled" : "stopped";
        AppStateChanged?.Invoke(ToDto(c));
        _diag.Log("INFO", appId, "Supervision", $"Stopped {c.Manifest.Name}.");
        return true;
    }

    public async Task<IReadOnlyList<string>> StopAllAsync(CancellationToken ct = default)
    {
        var stopped = new List<string>();
        foreach (string appId in _children.Keys.ToList())
        {
            if (await StopAppAsync(appId, 5000, ct).ConfigureAwait(false)) stopped.Add(appId);
        }
        return stopped;
    }

    public SisterAppEntry? SetResidency(string appId, string residencyMode)
    {
        string norm = residencyMode.Trim().ToLowerInvariant();
        if (norm is not ("warm" or "cold" or "disabled"))
        {
            throw new ArgumentException($"Invalid residency mode '{residencyMode}'.");
        }
        if (!_children.TryGetValue(appId, out ManagedChild? c)) return null;

        MotherSettings cfg = _settings.GetCurrent();
        cfg.ResidencyOverrides[c.Manifest.AppId] = norm;
        _settings.Save(cfg);

        c.Manifest = c.Manifest with { ResidencyMode = norm };
        if (norm == "disabled" && c.Process is null) c.State = "disabled";
        else if (norm != "disabled" && c.State == "disabled") c.State = "stopped";
        if (norm != "disabled") c.CircuitBreakerOpen = false;

        AppStateChanged?.Invoke(ToDto(c));
        return c.Manifest;
    }

    private void OnChildExited(ManagedChild c, Process proc)
    {
        c.Process = null;
        c.IsOnline = false;
        DateTime now = DateTime.UtcNow;
        c.RecentCrashesUtc.Enqueue(now);
        while (c.RecentCrashesUtc.Count > 0 && (now - c.RecentCrashesUtc.Peek()).TotalSeconds > 60)
        {
            c.RecentCrashesUtc.Dequeue();
        }
        c.RetryCount++;
        if (c.RecentCrashesUtc.Count >= 3)
        {
            c.CircuitBreakerOpen = true;
            c.State = "error";
            c.LastError = $"Crash-loop circuit breaker tripped ({proc.ExitCode}) — 3 exits within 60s.";
            _diag.Log("ERROR", c.Manifest.AppId, "Supervision", c.LastError);
        }
        else
        {
            c.State = "stopped";
        }
        AppStateChanged?.Invoke(ToDto(c));
    }

    private SisterAppRuntimeDto ToDto(ManagedChild c)
    {
        int effectivePort = c.Manifest.InternalPort;
        return new SisterAppRuntimeDto(
            AppId: c.Manifest.AppId,
            Name: c.Manifest.Name,
            Version: c.Manifest.Version,
            BrandAccent: c.Manifest.BrandAccent,
            InternalPort: c.Manifest.InternalPort,
            EffectivePort: effectivePort,
            Residency: c.Manifest.ResidencyMode,
            State: c.State,
            IsOnline: c.IsOnline,
            Pid: c.Process is { HasExited: false } p ? p.Id : null,
            ActiveJobs: c.ActiveJobs,
            QueuedJobs: c.QueuedJobs,
            RetryCount: c.RetryCount,
            CircuitBreakerOpen: c.CircuitBreakerOpen,
            UiUrl: $"/app/{c.Manifest.AppId}/",
            TargetDrives: c.Manifest.TargetDrives,
            SharedMounts: c.Manifest.SharedMounts,
            Capabilities: c.Manifest.Capabilities,
            MutuallyExclusiveWith: c.Manifest.MutuallyExclusiveWith,
            LastProbeUtc: c.LastProbeUtc,
            LastError: c.LastError);
    }

    public void Dispose() => _jobGuard.Dispose();
}
