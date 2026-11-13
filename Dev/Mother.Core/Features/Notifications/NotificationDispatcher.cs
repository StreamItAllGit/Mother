// @module: Mother.Core.Features.Notifications.NotificationDispatcher
// @purpose: Persists notifications to SQLite notification_history and raises live SSE alert events.
// @depends-on: System, System.Collections.Generic, System.Text.Json, System.Threading, System.Threading.Tasks, Mother.Core.Configuration, Mother.Core.Diagnostics, Mother.Core.Persistence
// @exports: NotificationDispatcher

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mother.Core.Configuration;
using Mother.Core.Diagnostics;
using Mother.Core.Persistence;

namespace Mother.Core.Features.Notifications;

public sealed class NotificationDispatcher
{
    private readonly PathResolver _paths;
    private readonly MotherDb _db;
    private readonly MotherDiagnostics _diag;

    public event Action<NotificationHistoryRecord>? AlertRaised;

    public NotificationDispatcher(PathResolver paths, MotherDb db, MotherDiagnostics diag)
    {
        _paths = paths;
        _db = db;
        _diag = diag;
    }

    public async Task<NotificationHistoryRecord> DispatchAsync(
        string appId,
        string slug,
        string title,
        string? description = null,
        object? metadata = null,
        CancellationToken ct = default)
    {
        string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        string metaJson = metadata is null ? "{}" : JsonSerializer.Serialize(metadata);
        var rec = new NotificationHistoryRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SuiteId = "mother",
            AppId = string.IsNullOrWhiteSpace(appId) ? "mother" : appId.Trim(),
            Slug = string.IsNullOrWhiteSpace(slug) ? "suite-alert" : slug.Trim(),
            Title = title,
            Description = description,
            Status = _paths.Options.TestMode || _paths.Options.DryRun ? "simulated" : "sent",
            IsDryRun = _paths.Options.DryRun ? 1 : 0,
            MetadataJson = metaJson,
            CreatedAtUtc = nowUtc,
            CompletedAtUtc = nowUtc
        };

        await _db.InsertNotificationAsync(rec, ct).ConfigureAwait(false);
        _diag.Log("INFO", rec.AppId, "Notifications", $"Notification [{rec.Slug}]: {rec.Title}");
        AlertRaised?.Invoke(rec);
        return rec;
    }

    public Task<IReadOnlyList<NotificationHistoryRecord>> GetHistoryAsync(int limit = 100, CancellationToken ct = default) =>
        _db.GetNotificationsAsync(limit, ct);
}
