// @module: Mother.Installer.Program
// @purpose: Single-EXE 4-pillar Setup Wizard loading GZip-compressed Foundation assemblies and delegating lifecycle to StandardInstallerHost.
// @depends-on: System, System.IO, System.IO.Compression, System.Reflection, System.Runtime.CompilerServices, Foundation.Installer
// @exports: Program

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Foundation.Installer;

namespace Mother.Installer;

public static class Program
{
    static Program()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string? asmName = new AssemblyName(e.Name).Name;
            if (asmName is "Foundation.Core" or "Foundation.Installer")
            {
                using Stream? gzStream = typeof(Program).Assembly.GetManifestResourceStream(asmName + ".dll.gz");
                if (gzStream is not null)
                {
                    using var decompressor = new GZipStream(gzStream, CompressionMode.Decompress);
                    using var ms = new MemoryStream();
                    decompressor.CopyTo(ms);
                    return Assembly.Load(ms.ToArray());
                }
            }
            return null;
        };
    }

    [STAThread]
    public static int Main(string[] args)
    {
        string[] normalizedArgs = NormalizeInstallerArgs(args);
        WaitForTargetUnlocked(normalizedArgs);
        return RunHost(normalizedArgs);
    }

    public static string[] NormalizeInstallerArgs(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0) return Array.Empty<string>();
        bool hasTestMode = args.Any(a => string.Equals(a, "--test-mode", StringComparison.OrdinalIgnoreCase));
        bool hasDataDir = args.Any(a => string.Equals(a, "--data-dir", StringComparison.OrdinalIgnoreCase)
            || a.StartsWith("--data-dir=", StringComparison.OrdinalIgnoreCase));
        string? installDir = TryReadFlagValue(args, "--install-dir");

        var result = new List<string>(args);
        if (hasTestMode && !hasDataDir && !string.IsNullOrWhiteSpace(installDir))
        {
            result.Add("--data-dir");
            result.Add(installDir);
        }
        return result.ToArray();
    }

    public static void WaitForTargetUnlocked(IReadOnlyList<string> args, int maxAttempts = 12, int delayMs = 250)
    {
        if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)
            || a.Equals("--verify", StringComparison.OrdinalIgnoreCase)
            || a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)
            || a.Equals("--version", StringComparison.OrdinalIgnoreCase)
            || a.Equals("--help", StringComparison.OrdinalIgnoreCase)))
            return;

        string? installDir = TryReadFlagValue(args, "--install-dir");
        if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir)) return;

        string appDir = Path.Combine(installDir, "app");
        var candidates = new List<string> { Path.Combine(installDir, "Mother.exe"), Path.Combine(appDir, "Mother.App.exe") };
        if (Directory.Exists(appDir)) candidates.AddRange(Directory.EnumerateFiles(appDir, "Mother*.dll", SearchOption.TopDirectoryOnly));

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            bool locked = false;
            foreach (string file in candidates.Where(File.Exists))
            {
                try { using var _ = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { locked = true; break; }
            }
            if (!locked) return;
            Thread.Sleep(delayMs);
        }
    }

    private static string? TryReadFlagValue(IReadOnlyList<string> args, string flagName)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], flagName, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                return args[i + 1];
            if (args[i].StartsWith(flagName + "=", StringComparison.OrdinalIgnoreCase))
                return args[i][(flagName.Length + 1)..].Trim('"');
        }
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunHost(string[] args)
    {
        string version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.1.0";

        int plusIdx = version.IndexOf('+');
        if (plusIdx > 0) version = version[..plusIdx];

        var descriptor = new InstallerAppDescriptor(
            ProductName: "Mother",
            DisplayName: "Mother",
            Publisher: "Personal Suite",
            Version: version,
            MainExecutableRelativePath: Path.Combine("app", "Mother.App.exe"),
            Subtitle: "Clean Release Root (Mother.exe + app\\, scripts\\, data\\)",
            EmbeddedPayloadResourceName: "Mother.Installer.Embedded.payload.zip",
            BrandAccentHex: "#38bdf8");

        return StandardInstallerHost.Run(descriptor, args, typeof(Program).Assembly);
    }
}
