// @module: Mother.Core.Persistence.MotherDb
// @purpose: WAL-mode SQLite persistence layer using PRAGMA user_version migrations and parameterized Standard 18 §3 queries.
// @depends-on: System, System.Collections.Generic, System.IO, System.Text, System.Threading, System.Threading.Tasks, Microsoft.Data.Sqlite, Foundation.Core
// @exports: MotherDb

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;
using Microsoft.Data.Sqlite;
using Mother.Core.Configuration;

namespace Mother.Core.Persistence;

public sealed class MotherDb
{
    public const int TargetSchemaVersion = 1;
    private readonly SqliteWalPool _pool;
    private readonly string _migrationsDir;

    public string DatabasePath => _pool.DatabasePath;

    public MotherDb(PathResolver paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _pool = new SqliteWalPool(paths.DatabasePath, busyTimeoutMs: 15000);
        _migrationsDir = paths.MigrationsDirectory;
        ApplyMigrations();
    }

    public int GetUserVersion()
    {
        using SqliteConnection conn = _pool.OpenConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        object? result = cmd.ExecuteScalar();
        return result is null ? 0 : Convert.ToInt32(result);
    }

    public bool CheckIntegrity(out string detail)
    {
        using SqliteConnection conn = _pool.OpenConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA quick_check;";
        detail = Convert.ToString(cmd.ExecuteScalar()) ?? "unknown";
        return string.Equals(detail, "ok", StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> BackupToAsync(string destinationPath, CancellationToken ct = default) =>
        _pool.VacuumIntoAsync(destinationPath, ct);

    public async Task InsertHealthEventAsync(FleetHealthEventRecord evt, CancellationToken ct = default)
    {
        await using SqliteConnection conn = await _pool.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO fleet_health_events (
                id, suite_id, app_id, status, residency_mode, pid, process_start_time_utc,
                exit_code, retry_count, is_online, has_active_conflicts,
                error_code, error_message, error_remedy, metadata_json, created_at_utc
            ) VALUES (
                $id, $suiteId, $appId, $status, $residencyMode, $pid, $procStart,
                $exitCode, $retryCount, $isOnline, $hasConflicts,
                $errCode, $errMsg, $errRemedy, $metaJson, $createdAt
            );
            """;
        cmd.Parameters.AddWithValue("$id", evt.Id);
        cmd.Parameters.AddWithValue("$suiteId", evt.SuiteId);
        cmd.Parameters.AddWithValue("$appId", evt.AppId);
        cmd.Parameters.AddWithValue("$status", evt.Status);
        cmd.Parameters.AddWithValue("$residencyMode", evt.ResidencyMode);
        cmd.Parameters.AddWithValue("$pid", (object?)evt.Pid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$procStart", (object?)evt.ProcessStartTimeUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$exitCode", (object?)evt.ExitCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$retryCount", evt.RetryCount);
        cmd.Parameters.AddWithValue("$isOnline", evt.IsOnline);
        cmd.Parameters.AddWithValue("$hasConflicts", evt.HasActiveConflicts);
        cmd.Parameters.AddWithValue("$errCode", (object?)evt.ErrorCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$errMsg", (object?)evt.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$errRemedy", (object?)evt.ErrorRemedy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$metaJson", evt.MetadataJson);
        cmd.Parameters.AddWithValue("$createdAt", evt.CreatedAtUtc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FleetHealthEventRecord>> GetHealthEventsAsync(int limit = 100, CancellationToken ct = default)
    {
        await using SqliteConnection conn = await _pool.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, suite_id, app_id, status, residency_mode, pid, process_start_time_utc,
                   exit_code, retry_count, is_online, has_active_conflicts,
                   error_code, error_message, error_remedy, metadata_json, created_at_utc
            FROM fleet_health_events
            ORDER BY created_at_utc DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        var rows = new List<FleetHealthEventRecord>();
        await using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new FleetHealthEventRecord
            {
                Id = r.GetString(0),
                SuiteId = r.GetString(1),
                AppId = r.GetString(2),
                Status = r.GetString(3),
                ResidencyMode = r.GetString(4),
                Pid = r.IsDBNull(5) ? null : r.GetInt32(5),
                ProcessStartTimeUtc = r.IsDBNull(6) ? null : r.GetString(6),
                ExitCode = r.IsDBNull(7) ? null : r.GetInt32(7),
                RetryCount = r.GetInt32(8),
                IsOnline = r.GetInt32(9),
                HasActiveConflicts = r.GetInt32(10),
                ErrorCode = r.IsDBNull(11) ? null : r.GetString(11),
                ErrorMessage = r.IsDBNull(12) ? null : r.GetString(12),
                ErrorRemedy = r.IsDBNull(13) ? null : r.GetString(13),
                MetadataJson = r.GetString(14),
                CreatedAtUtc = r.GetString(15)
            });
        }
        return rows;
    }

    public async Task InsertDeferralAsync(ScheduleDeferralRecord rec, CancellationToken ct = default)
    {
        await using SqliteConnection conn = await _pool.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO schedule_deferrals (
                id, suite_id, app_id, job_id, drive_mount, duration_ms,
                description, status, is_dry_run, next_run_at_utc, metadata_json, created_at_utc
            ) VALUES (
                $id, $suiteId, $appId, $jobId, $driveMount, $durationMs,
                $desc, $status, $isDryRun, $nextRun, $metaJson, $createdAt
            );
            """;
        cmd.Parameters.AddWithValue("$id", rec.Id);
        cmd.Parameters.AddWithValue("$suiteId", rec.SuiteId);
        cmd.Parameters.AddWithValue("$appId", rec.AppId);
        cmd.Parameters.AddWithValue("$jobId", rec.JobId);
        cmd.Parameters.AddWithValue("$driveMount", rec.DriveMount);
        cmd.Parameters.AddWithValue("$durationMs", rec.DurationMs);
        cmd.Parameters.AddWithValue("$desc", (object?)rec.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", rec.Status);
        cmd.Parameters.AddWithValue("$isDryRun", rec.IsDryRun);
        cmd.Parameters.AddWithValue("$nextRun", (object?)rec.NextRunAtUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$metaJson", rec.MetadataJson);
        cmd.Parameters.AddWithValue("$createdAt", rec.CreatedAtUtc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScheduleDeferralRecord>> GetDeferralsAsync(int limit = 100, CancellationToken ct = default)
    {
        await using SqliteConnection conn = await _pool.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, suite_id, app_id, job_id, drive_mount, duration_ms,
                   description, status, is_dry_run, next_run_at_utc, metadata_json, created_at_utc
            FROM schedule_deferrals
            ORDER BY created_at_utc DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        var rows = new List<ScheduleDeferralRecord>();
        await using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ScheduleDeferralRecord
            {
                Id = r.GetString(0),
                SuiteId = r.GetString(1),
                AppId = r.GetString(2),
                JobId = r.GetString(3),
                DriveMount = r.GetString(4),
                DurationMs = r.GetInt64(5),
                Description = r.IsDBNull(6) ? null : r.GetString(6),
                Status = r.GetString(7),
                IsDryRun = r.GetInt32(8),
                NextRunAtUtc = r.IsDBNull(9) ? null : r.GetString(9),
                MetadataJson = r.GetString(10),
                CreatedAtUtc = r.GetString(11)
            });
        }
        return rows;
    }

    public async Task InsertNotificationAsync(NotificationHistoryRecord rec, CancellationToken ct = default)
    {
        await using SqliteConnection conn = await _pool.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO notification_history (
                id, suite_id, app_id, slug, title, description, status,
                retry_count, is_dry_run, error_code, error_message, metadata_json,
                created_at_utc, completed_at_utc
            ) VALUES (
                $id, $suiteId, $appId, $slug, $title, $desc, $status,
                $retryCount, $isDryRun, $errCode, $errMsg, $metaJson,
                $createdAt, $completedAt
            );
            """;
        cmd.Parameters.AddWithValue("$id", rec.Id);
        cmd.Parameters.AddWithValue("$suiteId", rec.SuiteId);
        cmd.Parameters.AddWithValue("$appId", rec.AppId);
        cmd.Parameters.AddWithValue("$slug", rec.Slug);
        cmd.Parameters.AddWithValue("$title", rec.Title);
        cmd.Parameters.AddWithValue("$desc", (object?)rec.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", rec.Status);
        cmd.Parameters.AddWithValue("$retryCount", rec.RetryCount);
        cmd.Parameters.AddWithValue("$isDryRun", rec.IsDryRun);
        cmd.Parameters.AddWithValue("$errCode", (object?)rec.ErrorCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$errMsg", (object?)rec.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$metaJson", rec.MetadataJson);
        cmd.Parameters.AddWithValue("$createdAt", rec.CreatedAtUtc);
        cmd.Parameters.AddWithValue("$completedAt", (object?)rec.CompletedAtUtc ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NotificationHistoryRecord>> GetNotificationsAsync(int limit = 100, CancellationToken ct = default)
    {
        await using SqliteConnection conn = await _pool.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, suite_id, app_id, slug, title, description, status,
                   retry_count, is_dry_run, error_code, error_message, metadata_json,
                   created_at_utc, completed_at_utc
            FROM notification_history
            ORDER BY created_at_utc DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        var rows = new List<NotificationHistoryRecord>();
        await using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new NotificationHistoryRecord
            {
                Id = r.GetString(0),
                SuiteId = r.GetString(1),
                AppId = r.GetString(2),
                Slug = r.GetString(3),
                Title = r.GetString(4),
                Description = r.IsDBNull(5) ? null : r.GetString(5),
                Status = r.GetString(6),
                RetryCount = r.GetInt32(7),
                IsDryRun = r.GetInt32(8),
                ErrorCode = r.IsDBNull(9) ? null : r.GetString(9),
                ErrorMessage = r.IsDBNull(10) ? null : r.GetString(10),
                MetadataJson = r.GetString(11),
                CreatedAtUtc = r.GetString(12),
                CompletedAtUtc = r.IsDBNull(13) ? null : r.GetString(13)
            });
        }
        return rows;
    }

    private void ApplyMigrations()
    {
        using SqliteConnection conn = _pool.OpenConnection();
        int currentVersion;
        using (SqliteCommand vCmd = conn.CreateCommand())
        {
            vCmd.CommandText = "PRAGMA user_version;";
            currentVersion = Convert.ToInt32(vCmd.ExecuteScalar() ?? 0);
        }

        if (currentVersion >= TargetSchemaVersion)
        {
            return;
        }

        string sqlPath = Path.Combine(_migrationsDir, "0001_initial_schema.sql");
        string sql = File.Exists(sqlPath)
            ? File.ReadAllText(sqlPath, Encoding.UTF8)
            : FallbackInitialSql;

        using SqliteTransaction tx = conn.BeginTransaction();
        using (SqliteCommand mCmd = conn.CreateCommand())
        {
            mCmd.Transaction = tx;
            mCmd.CommandText = sql;
            mCmd.ExecuteNonQuery();
        }
        using (SqliteCommand setVer = conn.CreateCommand())
        {
            setVer.Transaction = tx;
            setVer.CommandText = $"PRAGMA user_version = {TargetSchemaVersion};";
            setVer.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private const string FallbackInitialSql = """
        CREATE TABLE IF NOT EXISTS fleet_health_events (
            id TEXT PRIMARY KEY NOT NULL, suite_id TEXT NOT NULL, app_id TEXT NOT NULL,
            status TEXT NOT NULL, residency_mode TEXT NOT NULL DEFAULT 'warm',
            pid INTEGER, process_start_time_utc TEXT, exit_code INTEGER,
            retry_count INTEGER NOT NULL DEFAULT 0,
            is_online INTEGER NOT NULL DEFAULT 0 CHECK (is_online IN (0, 1)),
            has_active_conflicts INTEGER NOT NULL DEFAULT 0 CHECK (has_active_conflicts IN (0, 1)),
            error_code TEXT, error_message TEXT, error_remedy TEXT,
            metadata_json TEXT NOT NULL DEFAULT '{}' CHECK (json_valid(metadata_json)),
            created_at_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS schedule_deferrals (
            id TEXT PRIMARY KEY NOT NULL, suite_id TEXT NOT NULL, app_id TEXT NOT NULL,
            job_id TEXT NOT NULL, drive_mount TEXT NOT NULL, duration_ms INTEGER NOT NULL DEFAULT 0,
            description TEXT, status TEXT NOT NULL,
            is_dry_run INTEGER NOT NULL DEFAULT 0 CHECK (is_dry_run IN (0, 1)),
            next_run_at_utc TEXT,
            metadata_json TEXT NOT NULL DEFAULT '{}' CHECK (json_valid(metadata_json)),
            created_at_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS notification_history (
            id TEXT PRIMARY KEY NOT NULL, suite_id TEXT NOT NULL, app_id TEXT NOT NULL,
            slug TEXT NOT NULL, title TEXT NOT NULL, description TEXT, status TEXT NOT NULL,
            retry_count INTEGER NOT NULL DEFAULT 0,
            is_dry_run INTEGER NOT NULL DEFAULT 0 CHECK (is_dry_run IN (0, 1)),
            error_code TEXT, error_message TEXT,
            metadata_json TEXT NOT NULL DEFAULT '{}' CHECK (json_valid(metadata_json)),
            created_at_utc TEXT NOT NULL, completed_at_utc TEXT
        );
        """;
}
