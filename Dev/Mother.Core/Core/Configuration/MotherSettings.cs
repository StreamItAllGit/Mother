// @module: Mother.Core.Configuration.MotherSettings
// @purpose: Schema-versioned Mother suite settings model (Standard 19) with forward-compatible JsonExtensionData and SIS-03 snapshot DTOs.
// @depends-on: System, System.Collections.Generic, System.Text.Json, System.Text.Json.Serialization
// @exports: MotherSettings, SettingsSnapshotSummary, SettingsExportPayload

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mother.Core.Configuration;

public sealed class MotherSettings
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    [JsonPropertyName("useGlobalFormatRules")]
    public bool UseGlobalFormatRules { get; set; } = true;

    [JsonPropertyName("port")]
    public int Port { get; set; } = 5100;

    [JsonPropertyName("bindHost")]
    public string BindHost { get; set; } = "127.0.0.1";

    [JsonPropertyName("shouldAllowTailscaleBind")]
    public bool ShouldAllowTailscaleBind { get; set; }

    [JsonPropertyName("idleTimeoutMinutes")]
    public int IdleTimeoutMinutes { get; set; } = 15;

    [JsonPropertyName("healthPollIntervalMs")]
    public int HealthPollIntervalMs { get; set; } = 5000;

    [JsonPropertyName("shouldInhibitSleepDuringActiveLock")]
    public bool ShouldInhibitSleepDuringActiveLock { get; set; } = true;

    [JsonPropertyName("postRunPowerPolicy")]
    public string PostRunPowerPolicy { get; set; } = "StayAwake";

    [JsonPropertyName("postRunIdleSeconds")]
    public int PostRunIdleSeconds { get; set; } = 60;

    [JsonPropertyName("postRunCountdownSeconds")]
    public int PostRunCountdownSeconds { get; set; } = 60;

    [JsonPropertyName("shouldAutoAlignSisterApps")]
    public bool ShouldAutoAlignSisterApps { get; set; }

    [JsonPropertyName("shouldShowDesktopToasts")]
    public bool ShouldShowDesktopToasts { get; set; } = true;

    [JsonPropertyName("shouldSendDiscordWebhooks")]
    public bool ShouldSendDiscordWebhooks { get; set; }

    [JsonPropertyName("customManifestRoots")]
    public List<string> CustomManifestRoots { get; set; } = new();

    [JsonPropertyName("targetDrives")]
    public List<string> TargetDrives { get; set; } = new();

    [JsonPropertyName("residencyOverrides")]
    public Dictionary<string, string> ResidencyOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public MotherSettings Normalized()
    {
        SchemaVersion = SchemaVersion < 2 ? 2 : SchemaVersion;
        Port = Port is > 0 and <= 65535 ? Port : 5100;
        BindHost = string.IsNullOrWhiteSpace(BindHost) ? "127.0.0.1" : BindHost.Trim();
        IdleTimeoutMinutes = Math.Clamp(IdleTimeoutMinutes, 1, 1440);
        HealthPollIntervalMs = Math.Clamp(HealthPollIntervalMs, 1000, 60000);
        PostRunIdleSeconds = Math.Clamp(PostRunIdleSeconds, 5, 3600);
        PostRunCountdownSeconds = Math.Clamp(PostRunCountdownSeconds, 5, 600);
        PostRunPowerPolicy = PostRunPowerPolicy is "StayAwake" or "Sleep" or "Shutdown"
            ? PostRunPowerPolicy
            : "StayAwake";
        CustomManifestRoots ??= new List<string>();
        TargetDrives ??= new List<string>();
        ResidencyOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return this;
    }
}

public sealed record SettingsSnapshotSummary(
    string SnapshotId,
    string FileName,
    string CreatedAtUtc,
    long FileSizeBytes
);

public sealed record SettingsExportPayload(
    string ExportedAtUtc,
    string AppId,
    string Version,
    MotherSettings Settings
);
