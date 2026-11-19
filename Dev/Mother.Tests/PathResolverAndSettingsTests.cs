// @module: Mother.Tests.PathResolverAndSettingsTests
// @purpose: Validates PathResolver isolation, dynamic ProgramsRoot resolution, and SettingsStore 20-snapshot rollback.
// @depends-on: System, System.IO, Xunit, Mother.Core.Configuration
// @exports: PathResolverAndSettingsTests

using System.IO;
using Mother.Core.Configuration;
using Xunit;

namespace Mother.Tests;

public sealed class PathResolverAndSettingsTests
{
    [Fact]
    public void PathResolver_HonorsIsolationFlagsAndPortOverride()
    {
        using var scope = new TempWorkspaceScope();
        var resolver = new PathResolver(new[]
        {
            "--data-dir", scope.DataDir,
            "--config", scope.ConfigFile,
            "--log-dir", scope.LogDir,
            "--programs-dir", scope.ProgramsDir,
            "--port", "5199",
            "--suite-tab", "shadowlibrary"
        }, scope.BaseDir);

        Assert.True(resolver.IsIsolatedOverride);
        Assert.Equal(scope.DataDir, resolver.DataDirectory);
        Assert.Equal(scope.ConfigFile, resolver.ConfigFilePath);
        Assert.Equal(scope.LogDir, resolver.LogDirectory);
        Assert.Equal(scope.ProgramsDir, resolver.ProgramsRoot);
        Assert.Equal(5199, resolver.Options.PortOverride);
        Assert.Equal("shadowlibrary", resolver.Options.SuiteTabAppId);
    }

    [Fact]
    public void PathResolver_ResolvesDefaultProgramsRootDynamicallyWithoutHardcodedPaths()
    {
        using var scope = new TempWorkspaceScope();
        var isolatedResolver = new PathResolver(new[]
        {
            "--data-dir", scope.DataDir,
            "--config", scope.ConfigFile,
            "--log-dir", scope.LogDir
        }, scope.BaseDir);

        Assert.EndsWith("mock-apps", isolatedResolver.ProgramsRoot);
        Assert.False(string.IsNullOrWhiteSpace(isolatedResolver.ProgramsRoot));
    }

    [Fact]
    public void SettingsStore_PreservesGlobalFormatRulesAndCaps20Snapshots()
    {
        using var scope = new TempWorkspaceScope();
        using var runtime = scope.CreateRuntime();

        Assert.True(runtime.Settings.GetCurrent().UseGlobalFormatRules);

        for (int i = 0; i < 24; i++)
        {
            var next = runtime.Settings.GetCurrent();
            next.IdleTimeoutMinutes = 10 + i;
            runtime.Settings.Save(next);
        }

        var snaps = runtime.Settings.ListSnapshots();
        Assert.True(snaps.Count <= 20, $"Expected <= 20 snapshots, found {snaps.Count}");
        Assert.True(snaps.Count > 0);
    }

    [Fact]
    public void SettingsStore_RollbackRestoresEarlierPolicyAndPort()
    {
        using var scope = new TempWorkspaceScope();
        using var runtime = scope.CreateRuntime();

        var first = runtime.Settings.GetCurrent();
        first.PostRunPowerPolicy = "Sleep";
        runtime.Settings.Save(first);
        string sleepSnapshotId = System.Linq.Enumerable.First(runtime.Settings.ListSnapshots()).SnapshotId;

        var second = runtime.Settings.GetCurrent();
        second.PostRunPowerPolicy = "Shutdown";
        runtime.Settings.Save(second);
        Assert.Equal("Shutdown", runtime.Settings.GetCurrent().PostRunPowerPolicy);

        var rolled = runtime.Settings.Rollback(sleepSnapshotId);
        Assert.Equal("Sleep", rolled.PostRunPowerPolicy);
    }

    [Fact]
    public void SettingsStore_ExportProducesVersionedPayload()
    {
        using var scope = new TempWorkspaceScope();
        using var runtime = scope.CreateRuntime();

        var payload = runtime.Settings.Export(runtime.Version);
        Assert.Equal("0.3.0", payload.Version);
        Assert.Equal(2, payload.Settings.SchemaVersion);
        Assert.False(string.IsNullOrWhiteSpace(payload.ExportedAtUtc));
    }
}
