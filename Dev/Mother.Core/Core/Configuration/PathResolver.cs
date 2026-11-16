// @module: Mother.Core.Configuration.PathResolver
// @purpose: Fail-closed AppEnvironment wrapper resolving CLI flags, dynamic ProgramsRoot (zero host literals), and isolated directories.
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
    bool DryRun,
    bool Diagnose,
    bool ShowHelp,
    bool ShowVersion,
    int? PortOverride,
    string? ProgramsDirOverride
);

public sealed class PathResolver
{
    public AppEnvironment AppEnv { get; }
    public MotherLaunchOptions Options { get; }
    public string BaseDirectory => AppEnv.BaseDirectory;
    public string DataDirectory => AppEnv.DataDirectory;
    public string ConfigFilePath => AppEnv.ConfigFilePath;
    public string LogDirectory => AppEnv.LogDirectory;
    public bool IsIsolatedOverride { get; }
    public string ContainmentRoot { get; }
    public string ProgramsRoot { get; }
    public string ManifestsDirectory { get; }
    public string DatabasePath { get; }
    public string BackupsDirectory { get; }
    public string MigrationsDirectory { get; }

    public PathResolver(IReadOnlyList<string>? args = null, string? baseDirectoryOverride = null)
    {
        IReadOnlyList<string> cli = args ?? Array.Empty<string>();
        AppEnv = new AppEnvironment("mother", cli, baseDirectoryOverride, "settings.json");

        string? suiteTab = ReadFlagValue(cli, "--suite-tab");
        string? windowApp = ReadFlagValue(cli, "--window");
        string? browserApp = ReadFlagValue(cli, "--browser");
        string? programsDir = ReadFlagValue(cli, "--programs-dir") ?? Environment.GetEnvironmentVariable("MOTHER_PROGRAMS_ROOT");
        bool trayOnly = AppEnv.TrayOnly || HasFlag(cli, "--minimized");
        bool diagnose = HasFlag(cli, "--diagnose");
        bool showHelp = HasFlag(cli, "--help") || HasFlag(cli, "-h") || HasFlag(cli, "/?");
        bool showVersion = HasFlag(cli, "--version") || HasFlag(cli, "-v");

        IsIsolatedOverride =
            string.Equals(Environment.GetEnvironmentVariable("APP_ISOLATED_TEST"), "1", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                Path.GetFullPath(DataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(Path.Combine(BaseDirectory, "data")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        ContainmentRoot = ResolveIsolatedContainmentRoot(DataDirectory) ?? DataDirectory;

        Options = new MotherLaunchOptions(
            SuiteTabAppId: suiteTab,
            WindowAppId: windowApp,
            BrowserAppId: browserApp,
            TrayOnly: trayOnly,
            DryRun: AppEnv.DryRun,
            Diagnose: diagnose,
            ShowHelp: showHelp,
            ShowVersion: showVersion,
            PortOverride: AppEnv.PortOverride,
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

    public string AssertContainedIfIsolated(string candidatePath)
    {
        if (IsIsolatedOverride)
        {
            return AppEnvironment.AssertPathContained(candidatePath, ContainmentRoot);
        }
        return Path.GetFullPath(candidatePath);
    }

    public static string? ResolveIsolatedContainmentRoot(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory)) return null;
        string full = Path.GetFullPath(dataDirectory.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetFileName(full), "data", StringComparison.OrdinalIgnoreCase))
            return null;
        string? parent = Directory.GetParent(full)?.FullName;
        if (string.IsNullOrWhiteSpace(parent)) return null;
        string normParent = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normTemp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(normParent, normTemp, StringComparison.OrdinalIgnoreCase)) return null;
        return Path.GetPathRoot(normParent) is { } root &&
               string.Equals(normParent, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            ? null
            : normParent;
    }

    public string ComputeIsolatedMutexHash()
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
        return "0.2.0-hf.1";
    }

    private string ResolveProgramsRoot(string? explicitOverride)
    {
        if (!string.IsNullOrWhiteSpace(explicitOverride))
        {
            string full = Path.GetFullPath(explicitOverride.Trim());
            return AssertContainedIfIsolated(full);
        }

        if (IsIsolatedOverride)
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
