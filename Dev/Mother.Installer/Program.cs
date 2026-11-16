// @module: Mother.Installer.Program
// @purpose: Single-EXE 4-pillar Setup Wizard loading GZip-compressed Foundation assemblies and delegating lifecycle to MotherInstallerRunner.
// @depends-on: System, System.Collections.Concurrent, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Reflection, System.Runtime.CompilerServices, System.Runtime.Loader, System.Threading
// @exports: Program

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;

namespace Mother.Installer;

public static class Program
{
    private static readonly ConcurrentDictionary<string, Assembly> LoadedFoundationAssemblies = new(StringComparer.OrdinalIgnoreCase);

    static Program()
    {
        AssemblyLoadContext.Default.Resolving += (_, name) => LoadEmbeddedFoundationAssembly(name.Name);
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) => LoadEmbeddedFoundationAssembly(new AssemblyName(e.Name).Name);
    }

    private static Assembly? LoadEmbeddedFoundationAssembly(string? asmName)
    {
        if (asmName is not ("Foundation.Core" or "Foundation.Installer")) return null;
        return LoadedFoundationAssemblies.GetOrAdd(asmName, static key =>
        {
            using Stream? gzStream = typeof(Program).Assembly.GetManifestResourceStream(key + ".dll.gz")
                ?? throw new FileNotFoundException($"Missing embedded Foundation assembly resource '{key}.dll.gz'.");
            using var decompressor = new GZipStream(gzStream, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            decompressor.CopyTo(ms);
            ms.Position = 0;
            return AssemblyLoadContext.Default.LoadFromStream(ms);
        });
    }

    [STAThread]
    public static int Main(string[] args)
    {
        string[] normalizedArgs = NormalizeInstallerArgs(args);
        WaitForTargetUnlocked(normalizedArgs);
        return RunHost(normalizedArgs);
    }

    public static string[] NormalizeInstallerArgs(IReadOnlyList<string>? args) =>
        args is null || args.Count == 0 ? Array.Empty<string>() : args.ToArray();

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
            ?? "0.2.0-hf.1";

        int plusIdx = version.IndexOf('+');
        if (plusIdx > 0) version = version[..plusIdx];

        return MotherInstallerRunner.Run(args, version, typeof(Program).Assembly);
    }
}
