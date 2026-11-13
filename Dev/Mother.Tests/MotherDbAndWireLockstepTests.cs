// @module: Mother.Tests.MotherDbAndWireLockstepTests
// @purpose: Verifies PRAGMA user_version=1 SQLite migrations, Standard 18 CRUD, VACUUM INTO backup, and 1:1 JS/C# wire-key lockstep.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Reflection, System.Text.Json.Serialization, System.Text.RegularExpressions, System.Threading.Tasks, Xunit, Mother.Core.Contracts, Mother.Core.Persistence
// @exports: MotherDbAndWireLockstepTests

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Mother.Core;
using Mother.Core.Configuration;
using Mother.Core.Contracts;
using Mother.Core.Features.TrafficCop;
using Mother.Core.Persistence;
using Xunit;

namespace Mother.Tests;

public sealed class MotherDbAndWireLockstepTests
{
    [Fact]
    public void MotherDb_AppliesInitialSchemaAndSetsPragmaUserVersion1()
    {
        using var scope = new TempWorkspaceScope();
        using var runtime = scope.CreateRuntime();

        Assert.Equal(1, runtime.Db.GetUserVersion());
        Assert.True(runtime.Db.CheckIntegrity(out string detail), detail);
    }

    [Fact]
    public async Task MotherDb_HealthEventsCrudAndUtcTimestampsWork()
    {
        using var scope = new TempWorkspaceScope();
        using var runtime = scope.CreateRuntime();

        await runtime.Db.InsertHealthEventAsync(new FleetHealthEventRecord
        {
            Id = "evt-1",
            SuiteId = "mother",
            AppId = "shadowlibrary",
            Status = "online",
            ResidencyMode = "warm",
            Pid = 1234,
            IsOnline = 1,
            CreatedAtUtc = "2026-11-12T18:00:00.123Z"
        });

        var list = await runtime.Db.GetHealthEventsAsync(10);
        Assert.Single(list);
        Assert.Equal("shadowlibrary", list[0].AppId);
        Assert.EndsWith("Z", list[0].CreatedAtUtc);
    }

    [Fact]
    public async Task MotherDb_BackupToAsyncCreatesValidSqliteCopy()
    {
        using var scope = new TempWorkspaceScope();
        using var runtime = scope.CreateRuntime();

        string dest = Path.Combine(scope.DataDir, "backup-test.db");
        string created = await runtime.Db.BackupToAsync(dest);

        Assert.True(File.Exists(created));
        Assert.True(new FileInfo(created).Length > 0);
    }

    [Fact]
    public async Task MotherDb_DeferralsAndNotificationsRoundTrip()
    {
        using var scope = new TempWorkspaceScope();
        using var runtime = scope.CreateRuntime();

        await runtime.Db.InsertDeferralAsync(new ScheduleDeferralRecord
        {
            Id = "def-1",
            SuiteId = "mother",
            AppId = "shadowlibrary",
            JobId = "job-123",
            DriveMount = "D:\\",
            DurationMs = 30000,
            Status = "deferred",
            CreatedAtUtc = "2026-11-12T18:01:00.000Z"
        });
        var deferrals = await runtime.Db.GetDeferralsAsync(10);
        Assert.Single(deferrals);
        Assert.Equal("job-123", deferrals[0].JobId);

        await runtime.Db.InsertNotificationAsync(new NotificationHistoryRecord
        {
            Id = "not-1",
            SuiteId = "mother",
            AppId = "mother",
            Slug = "doctor-warn",
            Title = "Doctor Warning",
            Status = "simulated",
            CreatedAtUtc = "2026-11-12T18:02:00.000Z"
        });
        var notifs = await runtime.Db.GetNotificationsAsync(10);
        Assert.Single(notifs);
        Assert.Equal("doctor-warn", notifs[0].Slug);
    }

    [Fact]
    public void DtoWireKeyLockstepTests_BackendDtosMatchFrontendContractsJs1To1()
    {
        string devRoot = TempWorkspaceScope.ResolveDevRoot();
        string contractsPath = Path.Combine(devRoot, "Mother.Server", "wwwroot", "js", "api", "contracts.js");
        Assert.True(File.Exists(contractsPath), $"Missing {contractsPath}");
        string js = File.ReadAllText(contractsPath);

        AssertWireKeyMatch(typeof(FleetHealthEventRecord), ParseJsKeyArray(js, "FLEET_HEALTH_EVENT_KEYS"));
        AssertWireKeyMatch(typeof(ScheduleDeferralRecord), ParseJsKeyArray(js, "SCHEDULE_DEFERRAL_KEYS"));
        AssertWireKeyMatch(typeof(NotificationHistoryRecord), ParseJsKeyArray(js, "NOTIFICATION_HISTORY_KEYS"));
        AssertWireKeyMatch(typeof(SisterAppRuntimeDto), ParseJsKeyArray(js, "RUNTIME_APP_KEYS"));
        AssertWireKeyMatch(typeof(SuiteHealthDto), ParseJsKeyArray(js, "SUITE_HEALTH_KEYS"));
        AssertWireKeyMatch(typeof(TrafficCopStatusDto), ParseJsKeyArray(js, "TRAFFIC_COP_STATUS_KEYS"));
        AssertWireKeyMatch(typeof(HkLockSnapshotDto), ParseJsKeyArray(js, "HK_LOCK_SNAPSHOT_KEYS"));
        AssertWireKeyMatch(typeof(FleetConflictDto), ParseJsKeyArray(js, "FLEET_CONFLICT_KEYS"));
        AssertWireKeyMatch(typeof(CoexistenceStatusDto), ParseJsKeyArray(js, "COEXISTENCE_STATUS_KEYS"));
        AssertWireKeyMatch(typeof(PowerStatusDto), ParseJsKeyArray(js, "POWER_STATUS_KEYS"));
        AssertWireKeyMatch(typeof(DoctorCheckDto), ParseJsKeyArray(js, "DOCTOR_CHECK_KEYS"));
        AssertWireKeyMatch(typeof(DoctorReportDto), ParseJsKeyArray(js, "DOCTOR_REPORT_KEYS"));
        AssertWireKeyMatch(typeof(SettingsSnapshotSummary), ParseJsKeyArray(js, "SETTINGS_SNAPSHOT_KEYS"));
        AssertWireKeyMatch(typeof(SecretKeyMetadataDto), ParseJsKeyArray(js, "SECRET_KEY_METADATA_KEYS"));
    }

    private static void AssertWireKeyMatch(Type dtoType, IReadOnlyList<string> jsKeys)
    {
        var csharpKeys = dtoType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? (char.ToLowerInvariant(p.Name[0]) + p.Name[1..]))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var sortedJs = jsKeys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(csharpKeys, sortedJs);
    }

    private static IReadOnlyList<string> ParseJsKeyArray(string jsContent, string exportName)
    {
        var match = Regex.Match(
            jsContent,
            $@"export\s+const\s+{Regex.Escape(exportName)}\s*=\s*Object\.freeze\(\[(?<body>[\s\S]*?)\]\);",
            RegexOptions.Multiline);
        Assert.True(match.Success, $"Could not find {exportName} in contracts.js");

        return Regex.Matches(match.Groups["body"].Value, @"'([^']+)'")
            .Select(m => m.Groups[1].Value)
            .ToArray();
    }
}
