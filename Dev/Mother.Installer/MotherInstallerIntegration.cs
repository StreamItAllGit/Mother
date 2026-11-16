// @module: Mother.Installer.MotherInstallerIntegration
// @purpose: Handles post-install uninstaller self-copy, Windows shortcuts, and HKCU metadata registration.
// @depends-on: System, System.Diagnostics, System.IO, Foundation.Core, Foundation.Installer, Microsoft.Win32
// @exports: MotherInstallerIntegration

using System;
using System.Diagnostics;
using System.IO;
using Foundation.Core;
using Foundation.Installer;
using Microsoft.Win32;

namespace Mother.Installer;

internal static class MotherInstallerIntegration
{
    internal static (bool WriteShortcuts, bool WriteAutoStart, bool WriteRegistry) PlanLivePostInstallActions(
        bool suppressHostMutations,
        bool dryRun,
        bool isWindows,
        bool createShortcuts,
        bool autoStart)
    {
        if (suppressHostMutations || dryRun || !isWindows)
        {
            return (false, false, false);
        }
        return (createShortcuts, autoStart, true);
    }

    internal static void CopyUninstallerSelf(string installDir)
    {
        string? selfExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(selfExe) || !File.Exists(selfExe)) return;

        string uninstallerPath = Path.Combine(installDir, "Mother-Uninstall.exe");
        if (!string.Equals(Path.GetFullPath(selfExe), Path.GetFullPath(uninstallerPath), StringComparison.OrdinalIgnoreCase))
        {
            FileLockRetryHelper.CopyWithOverwrite(selfExe, uninstallerPath);
        }
    }

    internal static void ApplyLivePostInstallActions(
        InstallerEngineCore engine,
        string installDir,
        string suiteDir,
        string profileId,
        (bool WriteShortcuts, bool WriteAutoStart, bool WriteRegistry) plan)
    {
        string launcherExe = Path.Combine(installDir, engine.Theme.ExecutableFileName ?? "Mother.exe");
        string uninstallerExe = Path.Combine(installDir, engine.Theme.UninstallerFileName ?? "Mother-Uninstall.exe");

        if (plan.WriteShortcuts)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Mother");
            if (!string.IsNullOrWhiteSpace(desktop))
            {
                WindowsShortcutWriter.WriteShortcut(Path.Combine(desktop, "Mother.lnk"), launcherExe, workingDirectory: installDir);
            }
            WindowsShortcutWriter.WriteShortcut(Path.Combine(startMenu, "Mother.lnk"), launcherExe, workingDirectory: installDir);
        }

        if (plan.WriteAutoStart)
        {
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (!string.IsNullOrWhiteSpace(startup))
            {
                WindowsShortcutWriter.WriteShortcut(Path.Combine(startup, "Mother.lnk"), launcherExe, arguments: "--tray-only", workingDirectory: installDir);
            }
        }

        if (plan.WriteRegistry)
        {
            WindowsShortcutWriter.RegisterCurrentUserUninstallEntry(engine.Theme, installDir, uninstallerExe, suppressHostRegistryWrites: false);

            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(@"Software\StreamItAll\Mother", writable: true);
            if (key is not null)
            {
                key.SetValue("InstallDir", installDir);
                key.SetValue("SuiteInstallDir", suiteDir);
                key.SetValue("InstallProfile", profileId);
                key.SetValue("Version", engine.Theme.Version);
            }
        }
    }

    internal static void RemoveShortcutsIfLive(AppEnvironment appEnv, bool isIsolated)
    {
        if (isIsolated || appEnv.DryRun || !OperatingSystem.IsWindows()) return;
        string[] paths =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Mother.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Mother", "Mother.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Mother.lnk")
        };
        foreach (string p in paths)
        {
            if (File.Exists(p))
            {
                try { File.Delete(p); }
                catch (Exception ex) { Trace.WriteLine($"Shortcut removal warning ({p}): {ex.Message}"); }
            }
        }
    }
}
