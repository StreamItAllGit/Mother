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
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("useGlobalFormatRules")]
    public bool UseGlobalFormatRules { get; set; } = true;

    [JsonPropertyName("port")]
    public int Port { get; set; } = 5100;

    [JsonPropertyName("testModePort")]
    public int TestModePort { get; set; } = 15100;

    [JsonPropertyName("bindHost")]
    public string BindHost { get; set; } = "127.0.0.1";

    [JsonPropertyName("allowTailscaleBind")]
    public bool AllowTailscaleBind { get; set; }

    [JsonPropertyName("coldWakeIdleMinutes")]
    public int ColdWakeIdleMinutes { get; set; } = 15;

    [JsonPropertyName("healthPollIntervalMs")]
    public int HealthPollIntervalMs { get; set; } = 5000;

    [JsonPropertyName("sleepInhibitDuringActiveLock")]
    public bool SleepInhibitDuringActiveLock { get; set; } = true;

    [JsonPropertyName("postRunPowerPolicy")]
    public string PostRunPowerPolicy { get; set; } = "StayAwake";

    [JsonPropertyName("postRunIdleSeconds")]
    public int PostRunIdleSeconds { get; set; } = 60;

    [JsonPropertyName("postRunCountdownSeconds")]
    public int PostRunCountdownSeconds { get; set; } = 60;

    [JsonPropertyName("autoAlignWithSisterApps")]
    public bool AutoAlignWithSisterApps { get; set; }

    [JsonPropertyName("desktopToastNotifications")]
    public bool DesktopToastNotifications { get; set; } = true;

    [JsonPropertyName("discordWebhookNotifications")]
    public bool DiscordWebhookNotifications { get; set; }

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
        SchemaVersion = SchemaVersion <= 0 ? 1 : SchemaVersion;
        Port = Port is > 0 and <= 65535 ? Port : 5100;
        TestModePort = TestModePort is > 0 and <= 65535 ? TestModePort : 15100;
        BindHost = string.IsNullOrWhiteSpace(BindHost) ? "127.0.0.1" : BindHost.Trim();
        ColdWakeIdleMinutes = Math.Clamp(ColdWakeIdleMinutes, 1, 1440);
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
