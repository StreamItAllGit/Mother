// @module: Mother.Core.Features.Supervision.WatchdogSchedulerService
// @purpose: Manages \Mother_Watchdog Windows Task Scheduler registration via ArgumentList; strictly simulated/no-op in isolated, sandbox, and --dry-run modes.
// @depends-on: System, System.ComponentModel, System.Diagnostics, System.IO, Mother.Core.Configuration, Mother.Core.Diagnostics
// @exports: WatchdogSchedulerService

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Mother.Core.Configuration;
using Mother.Core.Diagnostics;

namespace Mother.Core.Features.Supervision;

public sealed class WatchdogSchedulerService
{
    public const string TaskName = @"\Mother_Watchdog";
    private readonly PathResolver _paths;
    private readonly MotherDiagnostics _diag;

    public bool IsSimulated => _paths.ShouldSuppressLiveHostMutations || _paths.Options.DryRun;
    public bool SimulatedRegisteredState { get; private set; }

    public WatchdogSchedulerService(PathResolver paths, MotherDiagnostics diag)
    {
        _paths = paths;
        _diag = diag;
    }

    public bool IsRegistered()
    {
        if (IsSimulated) return SimulatedRegisteredState;
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("/Query");
            psi.ArgumentList.Add("/TN");
            psi.ArgumentList.Add(TaskName);
            using Process? p = Process.Start(psi);
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            _diag.Log("WARN", "mother", "Watchdog", $"Failed querying scheduled task {TaskName}: {ex.Message}");
            return false;
        }
    }

    public bool EnsureRegistered(out string message)
    {
        if (IsSimulated)
        {
            SimulatedRegisteredState = true;
            message = $"[SIMULATED] {TaskName} registration skipped in isolated/sandbox/dry-run mode.";
            _diag.Log("INFO", "mother", "Watchdog", message);
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            message = "Task Scheduler requires Windows.";
            return false;
        }

        string? exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            message = "Current executable path could not be resolved.";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("/Create");
            psi.ArgumentList.Add("/F");
            psi.ArgumentList.Add("/SC");
            psi.ArgumentList.Add("ONLOGON");
            psi.ArgumentList.Add("/TN");
            psi.ArgumentList.Add(TaskName);
            psi.ArgumentList.Add("/TR");
            psi.ArgumentList.Add($"\"{exe}\" --tray-only");
            using Process? p = Process.Start(psi);
            p?.WaitForExit(5000);
            bool ok = p?.ExitCode == 0;
            message = ok ? $"Registered {TaskName} Windows Task." : $"schtasks exited with code {p?.ExitCode}.";
            _diag.Log(ok ? "INFO" : "WARN", "mother", "Watchdog", message);
            return ok;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }
}
