// @module: Mother.Installer.MotherInstallerIntegration
// @purpose: Handles post-install uninstaller self-copy, Windows shortcuts, HKCU metadata registration, and --visible-test PNG capture.
// @depends-on: System, System.Diagnostics, System.Drawing, System.Drawing.Imaging, System.IO, System.Windows.Forms, Foundation.Core, Foundation.Installer, Microsoft.Win32
// @exports: MotherInstallerIntegration

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Foundation.Core;
using Foundation.Installer;
using Microsoft.Win32;

namespace Mother.Installer;

internal static class MotherInstallerIntegration
{
    internal static (bool WriteShortcuts, bool WriteAutoStart, bool WriteRegistry) PlanLivePostInstallActions(
        bool isContainedSandbox,
        bool dryRun,
        bool isWindows,
        bool createShortcuts,
        bool autoStart)
    {
        if (isContainedSandbox || dryRun || !isWindows)
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
            WindowsShortcutWriter.RegisterCurrentUserUninstallEntry(engine.Theme, installDir, uninstallerExe, engine.Sandbox.IsContainedSandbox);

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

    internal static void RemoveShortcutsIfLive(SandboxEnvironment sandbox)
    {
        if (sandbox.IsContainedSandbox || sandbox.DryRun || !OperatingSystem.IsWindows()) return;
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

    internal static void HookVisibleTestCapture(InstallerWizardForm wizard, string logDir)
    {
        bool captured = false;
        wizard.Shown += (_, _) =>
        {
            var timer = new System.Windows.Forms.Timer { Interval = 350 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                if (captured || wizard.IsDisposed) return;
                captured = true;
                try
                {
                    string shotsDir = Path.Combine(logDir, "screenshots");
                    Directory.CreateDirectory(shotsDir);
                    string outPng = Path.Combine(shotsDir, "installer-visible-test.png");
                    using var bmp = new Bitmap(wizard.Width, wizard.Height);
                    wizard.DrawToBitmap(bmp, new Rectangle(0, 0, wizard.Width, wizard.Height));
                    Point clientOrigin = wizard.PointToScreen(Point.Empty);
                    Point formOrigin = wizard.Bounds.Location;
                    int dx = clientOrigin.X - formOrigin.X;
                    int dy = clientOrigin.Y - formOrigin.Y;
                    using var g = Graphics.FromImage(bmp);
                    for (int i = wizard.Controls.Count - 1; i >= 0; i--)
                    {
                        Control c = wizard.Controls[i];
                        if (!c.Visible || c.Width <= 0 || c.Height <= 0) continue;
                        using var cb = new Bitmap(c.Width, c.Height);
                        c.DrawToBitmap(cb, new Rectangle(0, 0, c.Width, c.Height));
                        g.DrawImageUnscaled(cb, dx + c.Left, dy + c.Top);
                    }
                    bmp.Save(outPng, ImageFormat.Png);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Mother.Installer visible-test capture failed: {ex.Message}");
                }
            };
            timer.Start();
        };
    }
}
