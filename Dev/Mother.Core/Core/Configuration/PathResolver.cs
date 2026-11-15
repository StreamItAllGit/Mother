// @module: Mother.Core.Configuration.PathResolver
// @purpose: Fail-closed SandboxEnvironment wrapper resolving CLI flags, dynamic ProgramsRoot (zero host literals), and contained directories.
// @depends-on: System, System.Collections.Generic, System.IO, System.Security.Cryptography, System.Text, Foundation.Core
// @exports: MotherLaunchOptions, PathResolver

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Foundation.Core;
using Microsoft.Win32;

namespace Mother.Core.Configuration;

public sealed record MotherLaunchOptions(
    string? SuiteTabAppId,
    string? WindowAppId,
    string? BrowserAppId,
    bool TrayOnly,
    bool TestMode,
    bool DryRun,
    bool Headless,
    bool Diagnose,
    bool ShowHelp,
    bool ShowVersion,
    int? PortOverride,
    string? ProgramsDirOverride
);

public sealed class PathResolver
{
    public SandboxEnvironment Sandbox { get; }
    public MotherLaunchOptions Options { get; }
    public string BaseDirectory => Sandbox.BaseDirectory;
    public string DataDirectory => Sandbox.DataDirectory;
    public string ConfigFilePath => Sandbox.ConfigFilePath;
    public string LogDirectory => Sandbox.LogDirectory;
    public string ProgramsRoot { get; }
    public string ManifestsDirectory { get; }
    public string DatabasePath { get; }
    public string BackupsDirectory { get; }
    public string MigrationsDirectory { get; }

    public PathResolver(IReadOnlyList<string>? args = null, string? baseDirectoryOverride = null)
    {
        IReadOnlyList<string> cli = args ?? Array.Empty<string>();
        Sandbox = new SandboxEnvironment("mother", cli, baseDirectoryOverride, "settings.json");

        string? suiteTab = ReadFlagValue(cli, "--suite-tab");
        string? windowApp = ReadFlagValue(cli, "--window");
        string? browserApp = ReadFlagValue(cli, "--browser");
        string? programsDir = ReadFlagValue(cli, "--programs-dir") ?? Environment.GetEnvironmentVariable("MOTHER_PROGRAMS_ROOT");
        bool trayOnly = Sandbox.TrayOnly || HasFlag(cli, "--minimized");
        bool diagnose = HasFlag(cli, "--diagnose");
        bool showHelp = HasFlag(cli, "--help") || HasFlag(cli, "-h") || HasFlag(cli, "/?");
        bool showVersion = HasFlag(cli, "--version") || HasFlag(cli, "-v");

        Options = new MotherLaunchOptions(
            SuiteTabAppId: suiteTab,
            WindowAppId: windowApp,
            BrowserAppId: browserApp,
            TrayOnly: trayOnly,
            TestMode: Sandbox.TestMode,
            DryRun: Sandbox.DryRun,
            Headless: Sandbox.Headless,
            Diagnose: diagnose,
            ShowHelp: showHelp,
            ShowVersion: showVersion,
            PortOverride: Sandbox.PortOverride,
            ProgramsDirOverride: programsDir);

        ProgramsRoot = ResolveProgramsRoot(programsDir);
        ManifestsDirectory = Path.Combine(DataDirectory, "manifests");
        DatabasePath = Path.Combine(DataDirectory, "mother.db");
        BackupsDirectory = Path.Combine(DataDirectory, "backups");
        MigrationsDirectory = ResolveMigrationsDirectory(BaseDirectory);

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(ManifestsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }

    public string AssertContainedIfSandbox(string candidatePath)
    {
        if (Sandbox.IsContainedSandbox)
        {
            string containmentRoot = SandboxEnvironment.ResolveSiblingSandboxRoot(DataDirectory) ?? DataDirectory;
            return SandboxEnvironment.AssertPathContained(candidatePath, containmentRoot);
        }
        return Path.GetFullPath(candidatePath);
    }

    public string ComputeSandboxMutexHash()
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(DataDirectory.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    public static string ReadAppVersion(string baseDirectory)
    {
        string[] candidates =
        {
            Path.Combine(baseDirectory, "version.txt"),
            Path.Combine(baseDirectory, "..", "version.txt")
        };
        foreach (string path in candidates)
        {
            if (File.Exists(path))
            {
                string raw = File.ReadAllText(path, Encoding.UTF8).Trim();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    return raw;
                }
            }
        }
        return "0.2.0";
    }

    private string ResolveProgramsRoot(string? explicitOverride)
    {
        if (!string.IsNullOrWhiteSpace(explicitOverride))
        {
            string full = Path.GetFullPath(explicitOverride.Trim());
            return AssertContainedIfSandbox(full);
        }

        if (Sandbox.IsContainedSandbox)
        {
            return Path.Combine(DataDirectory, "mock-apps");
        }

        string? motherHome = Environment.GetEnvironmentVariable("MOTHER_HOME");
        if (!string.IsNullOrWhiteSpace(motherHome))
        {
            string fullHome = Path.GetFullPath(motherHome.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? homeParent = Directory.GetParent(fullHome)?.FullName;
            if (!string.IsNullOrWhiteSpace(homeParent) &&
                string.Equals(Path.GetFileName(fullHome), "Mother", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFileName(homeParent), "Mother", StringComparison.OrdinalIgnoreCase))
            {
                return homeParent;
            }
            return fullHome;
        }

        if (OperatingSystem.IsWindows())
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\StreamItAll\Mother");
            string? suiteDir = key?.GetValue("SuiteInstallDir") as string;
            if (!string.IsNullOrWhiteSpace(suiteDir))
            {
                return Path.GetFullPath(suiteDir.Trim());
            }

            string? installDir = key?.GetValue("InstallDir") as string;
            if (!string.IsNullOrWhiteSpace(installDir))
            {
                DirectoryInfo? parent = Directory.GetParent(Path.GetFullPath(installDir.Trim()));
                if (parent is not null)
                {
                    return parent.FullName;
                }
            }
        }

        DirectoryInfo? baseParent = Directory.GetParent(BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (baseParent is not null &&
            baseParent.Parent is not null &&
            string.Equals(baseParent.Name, "Mother", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(baseParent.Parent.Name, "Mother", StringComparison.OrdinalIgnoreCase))
        {
            return baseParent.Parent.FullName;
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.GetFullPath(Path.Combine(localAppData, "Programs", "StreamItAll", "Mother"));
    }

    private static string ResolveMigrationsDirectory(string baseDir)
    {
        string[] candidates =
        {
            Path.Combine(baseDir, "migrations"),
            Path.Combine(baseDir, "..", "migrations")
        };
        foreach (string c in candidates)
        {
            if (Directory.Exists(c))
            {
                return Path.GetFullPath(c);
            }
        }
        return Path.Combine(baseDir, "migrations");
    }

    private static bool HasFlag(IReadOnlyList<string> args, string flag)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string? ReadFlagValue(IReadOnlyList<string> args, string flag)
    {
        string prefix = flag + "=";
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return a[prefix.Length..].Trim().Trim('"');
            }
            if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Count &&
                !args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                return args[i + 1].Trim().Trim('"');
            }
        }
        return null;
    }
}
