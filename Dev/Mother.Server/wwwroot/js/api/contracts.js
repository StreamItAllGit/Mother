// @module: Mother.Server.Web.Api.Contracts
// @purpose: Frozen Standard 18 §3 SQLite wire-key schemas and canonical sister-app tab registry for 1:1 backend/frontend lockstep.
// @depends-on: None
// @exports: SUITE_DB_WIRE_KEYS, FLEET_HEALTH_EVENT_KEYS, SCHEDULE_DEFERRAL_KEYS, NOTIFICATION_HISTORY_KEYS, RUNTIME_APP_KEYS, SUITE_HEALTH_KEYS, TRAFFIC_COP_STATUS_KEYS, HK_LOCK_SNAPSHOT_KEYS, FLEET_CONFLICT_KEYS, COEXISTENCE_STATUS_KEYS, POWER_STATUS_KEYS, DOCTOR_CHECK_KEYS, DOCTOR_REPORT_KEYS, SETTINGS_SNAPSHOT_KEYS, SECRET_KEY_METADATA_KEYS, DEFAULT_SISTER_TABS

export const FLEET_HEALTH_EVENT_KEYS = Object.freeze([
  'id',
  'suiteId',
  'appId',
  'status',
  'residencyMode',
  'pid',
  'processStartTimeUtc',
  'exitCode',
  'retryCount',
  'isOnline',
  'hasActiveConflicts',
  'errorCode',
  'errorMessage',
  'errorRemedy',
  'metadataJson',
  'createdAtUtc',
]);

export const SCHEDULE_DEFERRAL_KEYS = Object.freeze([
  'id',
  'suiteId',
  'appId',
  'jobId',
  'driveMount',
  'durationMs',
  'description',
  'status',
  'isDryRun',
  'nextRunAtUtc',
  'metadataJson',
  'createdAtUtc',
]);

export const NOTIFICATION_HISTORY_KEYS = Object.freeze([
  'id',
  'suiteId',
  'appId',
  'slug',
  'title',
  'description',
  'status',
  'retryCount',
  'isDryRun',
  'errorCode',
  'errorMessage',
  'metadataJson',
  'createdAtUtc',
  'completedAtUtc',
]);

export const SUITE_DB_WIRE_KEYS = Object.freeze(
  Array.from(
    new Set([
      ...FLEET_HEALTH_EVENT_KEYS,
      ...SCHEDULE_DEFERRAL_KEYS,
      ...NOTIFICATION_HISTORY_KEYS,
    ])
  )
);

export const RUNTIME_APP_KEYS = Object.freeze([
  'appId',
  'name',
  'version',
  'brandAccent',
  'internalPort',
  'effectivePort',
  'residency',
  'state',
  'isOnline',
  'pid',
  'activeJobs',
  'queuedJobs',
  'retryCount',
  'circuitBreakerOpen',
  'uiUrl',
  'targetDrives',
  'sharedMounts',
  'capabilities',
  'mutuallyExclusiveWith',
  'lastProbeUtc',
  'lastError',
]);

export const SUITE_HEALTH_KEYS = Object.freeze([
  'status',
  'suiteId',
  'version',
  'sdkVersion',
  'pid',
  'uptimeSeconds',
  'isolatedMode',
  'sqliteUserVersion',
  'registeredApps',
  'warmApps',
  'coldApps',
  'disabledApps',
]);

export const TRAFFIC_COP_STATUS_KEYS = Object.freeze([
  'activeLockCount',
  'staleLockCount',
  'activeLocks',
  'recentDeferrals',
]);

export const HK_LOCK_SNAPSHOT_KEYS = Object.freeze([
  'lockId',
  'appId',
  'pid',
  'hostName',
  'operation',
  'driveMount',
  'lockFilePath',
  'status',
  'acquiredAtUtc',
  'heartbeatAtUtc',
  'expiresAtUtc',
]);

export const FLEET_CONFLICT_KEYS = Object.freeze([
  'conflictId',
  'kind',
  'severity',
  'primaryAppId',
  'secondaryAppId',
  'resourceKey',
  'summary',
  'recommendedPreset',
  'detectedAtUtc',
]);

export const COEXISTENCE_STATUS_KEYS = Object.freeze([
  'autoAlignWithSisterApps',
  'sharedRoots',
  'conflicts',
  'apps',
]);

export const POWER_STATUS_KEYS = Object.freeze([
  'policy',
  'fleetBusy',
  'activeJobCount',
  'queuedJobCount',
  'sleepInhibited',
  'countdownActive',
  'secondsRemaining',
  'idleSecondsElapsed',
  'simulated',
  'watchdogRegistered',
]);

export const DOCTOR_CHECK_KEYS = Object.freeze([
  'checkId',
  'title',
  'status',
  'detail',
  'repairable',
]);

export const DOCTOR_REPORT_KEYS = Object.freeze([
  'overall',
  'checkedAtUtc',
  'suiteVersion',
  'sqliteUserVersion',
  'crashBundleCount',
  'suiteChecks',
]);

export const SETTINGS_SNAPSHOT_KEYS = Object.freeze([
  'snapshotId',
  'fileName',
  'createdAtUtc',
  'fileSizeBytes',
]);

export const SECRET_KEY_METADATA_KEYS = Object.freeze([
  'vaultPath',
  'appId',
  'keyName',
  'vaultSizeBytes',
  'lastModifiedUtc',
]);

export const DEFAULT_SISTER_TABS = Object.freeze([
  { appId: 'mother', name: 'Mother', port: 5100, accent: '#38bdf8' },
  { appId: 'housekeeping', name: 'HouseKeeping', port: 5178, accent: '#f59e0b' },
  { appId: 'shadowlibrary', name: 'ShadowLibrary', port: 5179, accent: '#6366f1' },
  { appId: 'redroom', name: 'RedRoom', port: 5180, accent: '#ef4444' },
  { appId: 'greenroom', name: 'GreenRoom', port: 8420, accent: '#10b981' },
  { appId: 'streamengine', name: 'StreamEngine', port: 8430, accent: '#ec4899' },
  { appId: 'legacy-sister-app', name: 'legacy-sister-app', port: 8460, accent: '#a855f7' },
]);
