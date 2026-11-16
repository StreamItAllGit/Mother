// @module: Mother.Core.Features.Power.PowerSleepInhibitor
// @purpose: Toggles Win32 SetThreadExecutionState when active .hklock holds exist; strictly simulated/no-op in isolated and --dry-run modes.
// @depends-on: System, System.Runtime.InteropServices, Mother.Core.Configuration, Mother.Core.Diagnostics
// @exports: PowerSleepInhibitor

using System;
using System.Runtime.InteropServices;
using Mother.Core.Configuration;
using Mother.Core.Diagnostics;

namespace Mother.Core.Features.Power;

public sealed class PowerSleepInhibitor : IDisposable
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private readonly PathResolver _paths;
    private readonly MotherDiagnostics _diag;

    public bool IsInhibiting { get; private set; }
    public bool IsSimulated => _paths.IsIsolatedOverride || _paths.Options.DryRun;

    public PowerSleepInhibitor(PathResolver paths, MotherDiagnostics diag)
    {
        _paths = paths;
        _diag = diag;
    }

    public void UpdateInhibition(bool shouldInhibit)
    {
        if (IsInhibiting == shouldInhibit) return;
        IsInhibiting = shouldInhibit;

        if (IsSimulated || !OperatingSystem.IsWindows())
        {
            _diag.Log("INFO", "mother", "Power", $"[SIMULATED] Sleep inhibition -> {shouldInhibit}.");
            return;
        }

        SetThreadExecutionState(shouldInhibit ? (ES_CONTINUOUS | ES_SYSTEM_REQUIRED) : ES_CONTINUOUS);
        _diag.Log("INFO", "mother", "Power", $"OS sleep inhibition {(shouldInhibit ? "enabled" : "cleared")}.");
    }

    public void Dispose() => UpdateInhibition(false);
}
