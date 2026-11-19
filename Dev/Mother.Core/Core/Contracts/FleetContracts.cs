// @module: Mother.Core.Contracts.FleetContracts
// @purpose: Shared domain DTOs and contracts consumed across vertical slices without cross-feature imports.
// @depends-on: System, System.Collections.Generic, System.Text.Json.Serialization
// @exports: SisterAppEntry, SisterAppRuntimeDto, HkLockSnapshotDto, FleetConflictDto, CoexistenceStatusDto, PowerStatusDto, DoctorReportDto

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Mother.Core.Configuration;

namespace Mother.Core.Contracts;

public sealed record SisterScheduleEntry(
    [property: JsonPropertyName("scheduleId")] string ScheduleId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("cadence")] string Cadence,
    [property: JsonPropertyName("timeUtc")] string TimeUtc,
    [property: JsonPropertyName("runIfMissed")] bool RunIfMissed,
    [property: JsonPropertyName("targetDrives")] IReadOnlyList<string> TargetDrives,
    [property: JsonPropertyName("triggerEndpoint")] string TriggerEndpoint
);

public sealed record SisterCommandsEntry(
    [property: JsonPropertyName("startDaemon")] string? StartDaemon = null,
    [property: JsonPropertyName("start")] string? Start = null,
    [property: JsonPropertyName("stopDaemon")] string? StopDaemon = null,
    [property: JsonPropertyName("statusCheck")] string? StatusCheck = null,
    [property: JsonPropertyName("backupNow")] string? BackupNow = null
);

public sealed record SisterAppEntry
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 2;

    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = "0.1.0";

    [JsonPropertyName("minSuiteVersion")]
    public string MinSuiteVersion { get; init; } = "0.1.0";

    [JsonPropertyName("sdkVersion")]
    public string SdkVersion { get; init; } = MotherBuildConstants.SdkVersion;

    [JsonPropertyName("brandAccent")]
    public string BrandAccent { get; init; } = "#38bdf8";

    [JsonPropertyName("installDir")]
    public string InstallDir { get; init; } = string.Empty;

    [JsonPropertyName("executable")]
    public string Executable { get; init; } = string.Empty;

    [JsonPropertyName("workerArgs")]
    public IReadOnlyList<string> WorkerArgs { get; init; } = Array.Empty<string>();

    [JsonPropertyName("internalPort")]
    public int InternalPort { get; init; }

    [JsonPropertyName("residencyMode")]
    public string ResidencyMode { get; init; } = "warm";

    [JsonPropertyName("idleTimeoutMinutes")]
    public int IdleTimeoutMinutes { get; init; } = 15;

    [JsonPropertyName("wakeOnApiRequest")]
    public bool WakeOnApiRequest { get; init; } = true;

    [JsonPropertyName("wakeOnUiNavigation")]
    public bool WakeOnUiNavigation { get; init; } = true;

    [JsonPropertyName("healthEndpoint")]
    public string HealthEndpoint { get; init; } = "/api/v1/health";

    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    [JsonPropertyName("sharedMounts")]
    public IReadOnlyList<string> SharedMounts { get; init; } = Array.Empty<string>();

    [JsonPropertyName("mutuallyExclusiveWith")]
    public IReadOnlyList<string> MutuallyExclusiveWith { get; init; } = Array.Empty<string>();

    [JsonPropertyName("targetDrives")]
    public IReadOnlyList<string> TargetDrives { get; init; } = Array.Empty<string>();

    [JsonPropertyName("schedules")]
    public IReadOnlyList<SisterScheduleEntry> Schedules { get; init; } = Array.Empty<SisterScheduleEntry>();

    [JsonPropertyName("commands")]
    public SisterCommandsEntry? Commands { get; init; }

    [JsonPropertyName("secretVaultPath")]
    public string? SecretVaultPath { get; init; }

    [JsonPropertyName("manifestSourcePath")]
    public string ManifestSourcePath { get; init; } = string.Empty;

    [JsonPropertyName("detectedSchemaFormat")]
    public string DetectedSchemaFormat { get; init; } = "v2";
}

public sealed record SisterAppRuntimeDto(
    [property: JsonPropertyName("appId")] string AppId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("brandAccent")] string BrandAccent,
    [property: JsonPropertyName("internalPort")] int InternalPort,
    [property: JsonPropertyName("effectivePort")] int EffectivePort,
    [property: JsonPropertyName("residency")] string Residency,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("isOnline")] bool IsOnline,
    [property: JsonPropertyName("pid")] int? Pid,
    [property: JsonPropertyName("activeJobs")] int ActiveJobs,
    [property: JsonPropertyName("queuedJobs")] int QueuedJobs,
    [property: JsonPropertyName("retryCount")] int RetryCount,
    [property: JsonPropertyName("circuitBreakerOpen")] bool CircuitBreakerOpen,
    [property: JsonPropertyName("uiUrl")] string UiUrl,
    [property: JsonPropertyName("targetDrives")] IReadOnlyList<string> TargetDrives,
    [property: JsonPropertyName("sharedMounts")] IReadOnlyList<string> SharedMounts,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities,
    [property: JsonPropertyName("mutuallyExclusiveWith")] IReadOnlyList<string> MutuallyExclusiveWith,
    [property: JsonPropertyName("lastProbeUtc")] string? LastProbeUtc,
    [property: JsonPropertyName("lastError")] string? LastError
);

public sealed record HkLockSnapshotDto(
    [property: JsonPropertyName("lockId")] string LockId,
    [property: JsonPropertyName("appId")] string AppId,
    [property: JsonPropertyName("pid")] int Pid,
    [property: JsonPropertyName("hostName")] string HostName,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("driveMount")] string DriveMount,
    [property: JsonPropertyName("lockFilePath")] string LockFilePath,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("acquiredAtUtc")] string AcquiredAtUtc,
    [property: JsonPropertyName("heartbeatAtUtc")] string HeartbeatAtUtc,
    [property: JsonPropertyName("expiresAtUtc")] string ExpiresAtUtc
);

public sealed record FleetConflictDto(
    [property: JsonPropertyName("conflictId")] string ConflictId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("primaryAppId")] string PrimaryAppId,
    [property: JsonPropertyName("secondaryAppId")] string SecondaryAppId,
    [property: JsonPropertyName("resourceKey")] string ResourceKey,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("recommendedPreset")] string RecommendedPreset,
    [property: JsonPropertyName("detectedAtUtc")] string DetectedAtUtc
);

public sealed record CoexistenceStatusDto(
    [property: JsonPropertyName("shouldAutoAlignSisterApps")] bool ShouldAutoAlignSisterApps,
    [property: JsonPropertyName("sharedRoots")] IReadOnlyList<string> SharedRoots,
    [property: JsonPropertyName("conflicts")] IReadOnlyList<FleetConflictDto> Conflicts,
    [property: JsonPropertyName("apps")] IReadOnlyList<SisterAppRuntimeDto> Apps
);

public sealed record PowerStatusDto(
    [property: JsonPropertyName("policy")] string Policy,
    [property: JsonPropertyName("fleetBusy")] bool FleetBusy,
    [property: JsonPropertyName("activeJobCount")] int ActiveJobCount,
    [property: JsonPropertyName("queuedJobCount")] int QueuedJobCount,
    [property: JsonPropertyName("sleepInhibited")] bool SleepInhibited,
    [property: JsonPropertyName("countdownActive")] bool CountdownActive,
    [property: JsonPropertyName("secondsRemaining")] int SecondsRemaining,
    [property: JsonPropertyName("idleSecondsElapsed")] int IdleSecondsElapsed,
    [property: JsonPropertyName("simulated")] bool Simulated,
    [property: JsonPropertyName("watchdogRegistered")] bool WatchdogRegistered
);

public sealed record DoctorCheckDto(
    [property: JsonPropertyName("checkId")] string CheckId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("repairable")] bool Repairable
);

public sealed record DoctorReportDto(
    [property: JsonPropertyName("overall")] string Overall,
    [property: JsonPropertyName("checkedAtUtc")] string CheckedAtUtc,
    [property: JsonPropertyName("suiteVersion")] string SuiteVersion,
    [property: JsonPropertyName("sqliteUserVersion")] int SqliteUserVersion,
    [property: JsonPropertyName("crashBundleCount")] int CrashBundleCount,
    [property: JsonPropertyName("suiteChecks")] IReadOnlyList<DoctorCheckDto> SuiteChecks
);

public sealed record SecretKeyMetadataDto(
    [property: JsonPropertyName("vaultPath")] string VaultPath,
    [property: JsonPropertyName("appId")] string AppId,
    [property: JsonPropertyName("keyName")] string KeyName,
    [property: JsonPropertyName("vaultSizeBytes")] long VaultSizeBytes,
    [property: JsonPropertyName("lastModifiedUtc")] string LastModifiedUtc
);
