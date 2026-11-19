// @module: Mother.Tests.TestSupport.TempWorkspaceScope
// @purpose: Isolated temporary directory and migration-seeded base directory helper for fail-closed xUnit execution.
// @depends-on: System, System.IO, Mother.Core
// @exports: TempWorkspaceScope

using System;
using System.IO;
using Mother.Core;

namespace Mother.Tests;

internal sealed class TempWorkspaceScope : IDisposable
{
    public string RootDir { get; }
    public string BaseDir { get; }
    public string DataDir { get; }
    public string LogDir { get; }
    public string ConfigFile { get; }
    public string ProgramsDir { get; }

    public TempWorkspaceScope()
    {
        RootDir = Path.Combine(Path.GetTempPath(), $"mother-test-{Guid.NewGuid():N}");
        BaseDir = Path.Combine(RootDir, "app");
        DataDir = Path.Combine(RootDir, "data");
        LogDir = Path.Combine(RootDir, "logs");
        ConfigFile = Path.Combine(DataDir, "settings.json");
        ProgramsDir = Path.Combine(DataDir, "mock-apps");

        Directory.CreateDirectory(BaseDir);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(ProgramsDir);

        string devRoot = ResolveDevRoot();
        File.Copy(Path.Combine(devRoot, "version.txt"), Path.Combine(BaseDir, "version.txt"), overwrite: true);
        File.Copy(Path.Combine(devRoot, "suite.manifest.json"), Path.Combine(BaseDir, "suite.manifest.json"), overwrite: true);

        string migSrc = Path.Combine(devRoot, "migrations");
        string migDst = Path.Combine(BaseDir, "migrations");
        Directory.CreateDirectory(migDst);
        foreach (string f in Directory.GetFiles(migSrc, "*.sql"))
        {
            File.Copy(f, Path.Combine(migDst, Path.GetFileName(f)), overwrite: true);
        }
    }

    public MotherRuntime CreateRuntime(params string[] extraArgs)
    {
        var args = new System.Collections.Generic.List<string>
        {
            "--data-dir", DataDir,
            "--config", ConfigFile,
            "--log-dir", LogDir,
            "--programs-dir", ProgramsDir
        };
        args.AddRange(extraArgs);
        return new MotherRuntime(args, BaseDir, skipIpcMutex: true);
    }

    public static string ResolveDevRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Mother.slnx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Mother Dev root containing Mother.slnx.");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootDir))
            {
                Directory.Delete(RootDir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning($"TempWorkspaceScope cleanup deferred: {ex.Message}");
        }
    }
}
