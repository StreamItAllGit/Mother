// @module: Mother.Core.Persistence.MotherDbModels
// @purpose: Standard 18 §3 SQLite DTO contracts and SSE/API wire records strictly aligned to contracts.js.
// @depends-on: System, System.Text.Json.Serialization
// @exports: FleetHealthEventRecord, ScheduleDeferralRecord, NotificationHistoryRecord, TerminalLogEntryDto

using System.Text.Json.Serialization;

namespace Mother.Core.Persistence;

public sealed record FleetHealthEventRecord
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("suiteId")]
    public required string SuiteId { get; init; }

    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("residencyMode")]
    public string ResidencyMode { get; init; } = "warm";

    [JsonPropertyName("pid")]
    public int? Pid { get; init; }

    [JsonPropertyName("processStartTimeUtc")]
    public string? ProcessStartTimeUtc { get; init; }

    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; init; }

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; init; }

    [JsonPropertyName("isOnline")]
    public int IsOnline { get; init; }

    [JsonPropertyName("hasActiveConflicts")]
    public int HasActiveConflicts { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("errorRemedy")]
    public string? ErrorRemedy { get; init; }

    [JsonPropertyName("metadataJson")]
    public string MetadataJson { get; init; } = "{}";

    [JsonPropertyName("createdAtUtc")]
    public required string CreatedAtUtc { get; init; }
}

public sealed record ScheduleDeferralRecord
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("suiteId")]
    public required string SuiteId { get; init; }

    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("jobId")]
    public required string JobId { get; init; }

    [JsonPropertyName("driveMount")]
    public required string DriveMount { get; init; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("isDryRun")]
    public int IsDryRun { get; init; }

    [JsonPropertyName("nextRunAtUtc")]
    public string? NextRunAtUtc { get; init; }

    [JsonPropertyName("metadataJson")]
    public string MetadataJson { get; init; } = "{}";

    [JsonPropertyName("createdAtUtc")]
    public required string CreatedAtUtc { get; init; }
}

public sealed record NotificationHistoryRecord
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("suiteId")]
    public required string SuiteId { get; init; }

    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("slug")]
    public required string Slug { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; init; }

    [JsonPropertyName("isDryRun")]
    public int IsDryRun { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("metadataJson")]
    public string MetadataJson { get; init; } = "{}";

    [JsonPropertyName("createdAtUtc")]
    public required string CreatedAtUtc { get; init; }

    [JsonPropertyName("completedAtUtc")]
    public string? CompletedAtUtc { get; init; }
}

public sealed record TerminalLogEntryDto(
    [property: JsonPropertyName("seq")] long Seq,
    [property: JsonPropertyName("timestampUtc")] string TimestampUtc,
    [property: JsonPropertyName("appId")] string AppId,
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("slice")] string Slice,
    [property: JsonPropertyName("message")] string Message
);
