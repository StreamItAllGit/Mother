// @module: Mother.Launcher.Program
// @purpose: NativeAOT-ready root Mother.exe shim delegating CLI/GUI forwarding directly to compile-time referenced Foundation.Launcher.
// @depends-on: System, System.Diagnostics, System.IO, System.Runtime.InteropServices, Foundation.Launcher
// @exports: Program

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Foundation.Launcher;

namespace Mother.Launcher;

internal static class Program
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    public static int Main(string[] args)
    {
        string rootDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? selfPath = Environment.ProcessPath;
        string appExe = RootLauncherHost.ResolveTargetExecutable(rootDir, Path.Combine("app", "Mother.App.exe"), selfPath);

        if (!string.IsNullOrWhiteSpace(selfPath) &&
            string.Equals(Path.GetFullPath(appExe), Path.GetFullPath(selfPath), StringComparison.OrdinalIgnoreCase))
        {
            TryAttachParentConsole(always: true);
            Console.Error.WriteLine($"Fail-closed self-spawn guard: refusing to launch '{appExe}' from itself.");
            return 1;
        }

        if (!File.Exists(appExe))
        {
            TryAttachParentConsole(always: true);
            Console.Error.WriteLine($"Mother application binary not found at '{appExe}'.");
            return 1;
        }

        bool waitForChild = MotherLauncherArgsHelper.ShouldWaitForChild(args);
        if (waitForChild)
        {
            TryAttachParentConsole(always: false);
        }

        string[] normalizedArgs = MotherLauncherArgsHelper.NormalizeArgs(args, Environment.CurrentDirectory);
        var psi = new ProcessStartInfo
        {
            FileName = appExe,
            WorkingDirectory = Path.GetDirectoryName(appExe) ?? rootDir,
            UseShellExecute = false
        };

        foreach (string arg in normalizedArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        using Process? proc = Process.Start(psi);
        if (proc is null)
        {
            return 1;
        }

        if (!waitForChild)
        {
            return 0;
        }

        bool interrupted = false;
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            interrupted = true;
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                Trace.WriteLine($"Child termination skipped on Ctrl+C: {ex.Message}");
            }
        };

        Console.CancelKeyPress += cancelHandler;
        try
        {
            proc.WaitForExit();
            return interrupted ? 130 : proc.ExitCode;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void TryAttachParentConsole(bool always)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        if (!always && (Console.IsOutputRedirected || Console.IsErrorRedirected))
        {
            return;
        }
        try
        {
            AttachConsole(AttachParentProcess);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.WriteLine($"Parent console attachment unavailable: {ex.Message}");
        }
    }
}
