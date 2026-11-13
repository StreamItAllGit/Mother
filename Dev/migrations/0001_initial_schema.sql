-- @module: Mother.Database.InitialSchema
-- @purpose: Standard 18 §3 canonical SQLite schema for mother.db (PRAGMA user_version = 1).
-- @depends-on: SQLite WAL (Foundation.Core.SqliteWalPool)
-- @exports: fleet_health_events, schedule_deferrals, notification_history

CREATE TABLE IF NOT EXISTS fleet_health_events (
    id TEXT PRIMARY KEY NOT NULL,
    suite_id TEXT NOT NULL,
    app_id TEXT NOT NULL,
    status TEXT NOT NULL,
    residency_mode TEXT NOT NULL DEFAULT 'warm',
    pid INTEGER,
    process_start_time_utc TEXT,
    exit_code INTEGER,
    retry_count INTEGER NOT NULL DEFAULT 0,
    is_online INTEGER NOT NULL DEFAULT 0 CHECK (is_online IN (0, 1)),
    has_active_conflicts INTEGER NOT NULL DEFAULT 0 CHECK (has_active_conflicts IN (0, 1)),
    error_code TEXT,
    error_message TEXT,
    error_remedy TEXT,
    metadata_json TEXT NOT NULL DEFAULT '{}' CHECK (json_valid(metadata_json)),
    created_at_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_fleet_health_events_app_created
    ON fleet_health_events (app_id, created_at_utc DESC);

CREATE TABLE IF NOT EXISTS schedule_deferrals (
    id TEXT PRIMARY KEY NOT NULL,
    suite_id TEXT NOT NULL,
    app_id TEXT NOT NULL,
    job_id TEXT NOT NULL,
    drive_mount TEXT NOT NULL,
    duration_ms INTEGER NOT NULL DEFAULT 0,
    description TEXT,
    status TEXT NOT NULL,
    is_dry_run INTEGER NOT NULL DEFAULT 0 CHECK (is_dry_run IN (0, 1)),
    next_run_at_utc TEXT,
    metadata_json TEXT NOT NULL DEFAULT '{}' CHECK (json_valid(metadata_json)),
    created_at_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_schedule_deferrals_app_created
    ON schedule_deferrals (app_id, created_at_utc DESC);

CREATE TABLE IF NOT EXISTS notification_history (
    id TEXT PRIMARY KEY NOT NULL,
    suite_id TEXT NOT NULL,
    app_id TEXT NOT NULL,
    slug TEXT NOT NULL,
    title TEXT NOT NULL,
    description TEXT,
    status TEXT NOT NULL,
    retry_count INTEGER NOT NULL DEFAULT 0,
    is_dry_run INTEGER NOT NULL DEFAULT 0 CHECK (is_dry_run IN (0, 1)),
    error_code TEXT,
    error_message TEXT,
    metadata_json TEXT NOT NULL DEFAULT '{}' CHECK (json_valid(metadata_json)),
    created_at_utc TEXT NOT NULL,
    completed_at_utc TEXT
);

CREATE INDEX IF NOT EXISTS idx_notification_history_created
    ON notification_history (created_at_utc DESC);
