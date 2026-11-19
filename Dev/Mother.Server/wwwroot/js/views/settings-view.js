// @module: Mother.Server.Web.Views.SettingsView
// @purpose: Implements SIS-03 Settings route with visual pickers, useGlobalFormatRules, Ctrl+S persistence, JSON export, and 20-snapshot rollback.
// @depends-on: /js/api/client.js, /js/components/sortable-table.js
// @exports: renderSettingsView, saveCurrentSettings

import { announceStatus, apiPost, apiPut, escapeHtml, exportRowsAsFile } from '../api/client.js';
import { renderSortableTable } from '../components/sortable-table.js';

let draftSettings = null;

export async function saveCurrentSettings(refreshAll) {
  if (!draftSettings) return;
  await apiPut('/api/v1/suite/settings', draftSettings);
  announceStatus('Saved Mother settings and created snapshot.');
  await refreshAll();
}

export function renderSettingsView(container, state, refreshAll) {
  container.innerHTML = '';
  const current = state.settingsBundle?.settings || {};
  draftSettings = {
    ...structuredClone(current),
    schemaVersion: Math.max(2, Number(current.schemaVersion) || 2),
    useGlobalFormatRules: current.useGlobalFormatRules ?? true,
    shouldAllowTailscaleBind: Boolean(current.shouldAllowTailscaleBind),
    idleTimeoutMinutes: current.idleTimeoutMinutes ?? 15,
    healthPollIntervalMs: current.healthPollIntervalMs ?? 5000,
    shouldInhibitSleepDuringActiveLock: current.shouldInhibitSleepDuringActiveLock ?? true,
    postRunPowerPolicy: current.postRunPowerPolicy || 'StayAwake',
    shouldAutoAlignSisterApps: Boolean(current.shouldAutoAlignSisterApps),
    shouldShowDesktopToasts: current.shouldShowDesktopToasts ?? true,
    shouldSendDiscordWebhooks: Boolean(current.shouldSendDiscordWebhooks),
  };
  const snapshots = state.settingsBundle?.snapshots || [];

  const header = document.createElement('div');
  header.className = 'table-toolbar';
  header.innerHTML = `
    <div>
      <h1 style="margin:0;font-size:20px">Suite Settings &amp; Snapshot Rollback</h1>
      <span class="kpi-label">Schema v${escapeHtml(draftSettings.schemaVersion)} · Press Ctrl+S to save</span>
    </div>
    <div class="row-gap">
      <button type="button" id="btn-export-settings" class="btn-secondary">Export Settings JSON</button>
      <button type="button" id="btn-save-settings" class="btn-primary">Save Settings (Ctrl+S)</button>
    </div>
  `;
  container.appendChild(header);

  const grid = document.createElement('div');
  grid.className = 'settings-grid';
  grid.innerHTML = `
    <div class="panel-card">
      <label class="row-gap"><input type="checkbox" id="set-global-format" ${draftSettings.useGlobalFormatRules ? 'checked' : ''} /><strong>Use global format rules</strong></label>
      <p class="kpi-label">Enforce suite-wide ISO-8601 UTC millisecond and tabular numeric standards.</p>
      <label class="row-gap"><input type="checkbox" id="set-tailscale" ${draftSettings.shouldAllowTailscaleBind ? 'checked' : ''} /><strong>Allow Tailscale bind (100.64.0.0/10)</strong></label>
      <label class="row-gap" style="margin-top:8px"><input type="checkbox" id="set-auto-align" ${draftSettings.shouldAutoAlignSisterApps ? 'checked' : ''} /><strong>Auto-align sister app schedules</strong></label>
      <label class="row-gap" style="margin-top:8px"><input type="checkbox" id="set-inhibit-sleep" ${draftSettings.shouldInhibitSleepDuringActiveLock ? 'checked' : ''} /><strong>Inhibit sleep during active lock</strong></label>
      <label class="row-gap" style="margin-top:8px"><input type="checkbox" id="set-desktop-toasts" ${draftSettings.shouldShowDesktopToasts ? 'checked' : ''} /><strong>Show desktop toast notifications</strong></label>
      <label class="row-gap" style="margin-top:8px"><input type="checkbox" id="set-discord-webhooks" ${draftSettings.shouldSendDiscordWebhooks ? 'checked' : ''} /><strong>Send Discord webhook alerts</strong></label>
    </div>
    <div class="panel-card">
      <div class="kpi-label">Post-Run Power Policy</div>
      <div class="segmented-picker" id="settings-power-picker">
        ${['StayAwake', 'Sleep', 'Shutdown'].map((p) => `<button type="button" class="segment-btn" data-policy="${p}" aria-pressed="${draftSettings.postRunPowerPolicy === p}">${p}</button>`).join('')}
      </div>
      <div class="row-gap" style="margin-top:12px">
        <label style="flex:1">Idle Timeout (min)<input type="number" id="set-idle-min" class="mother-input" min="1" max="1440" value="${escapeHtml(draftSettings.idleTimeoutMinutes)}" /></label>
        <label style="flex:1">Poll Interval (ms)<input type="number" id="set-poll-ms" class="mother-input" min="1000" max="60000" step="500" value="${escapeHtml(draftSettings.healthPollIntervalMs)}" /></label>
      </div>
    </div>
  `;
  container.appendChild(grid);

  grid.querySelector('#set-global-format')?.addEventListener('change', (e) => { draftSettings.useGlobalFormatRules = e.target.checked; });
  grid.querySelector('#set-tailscale')?.addEventListener('change', (e) => { draftSettings.shouldAllowTailscaleBind = e.target.checked; });
  grid.querySelector('#set-auto-align')?.addEventListener('change', (e) => { draftSettings.shouldAutoAlignSisterApps = e.target.checked; });
  grid.querySelector('#set-inhibit-sleep')?.addEventListener('change', (e) => { draftSettings.shouldInhibitSleepDuringActiveLock = e.target.checked; });
  grid.querySelector('#set-desktop-toasts')?.addEventListener('change', (e) => { draftSettings.shouldShowDesktopToasts = e.target.checked; });
  grid.querySelector('#set-discord-webhooks')?.addEventListener('change', (e) => { draftSettings.shouldSendDiscordWebhooks = e.target.checked; });
  grid.querySelector('#set-idle-min')?.addEventListener('input', (e) => {
    const n = Number(e.target.value);
    draftSettings.idleTimeoutMinutes = Number.isFinite(n) && n >= 1 ? Math.min(1440, Math.round(n)) : 15;
  });
  grid.querySelector('#set-poll-ms')?.addEventListener('input', (e) => {
    const n = Number(e.target.value);
    draftSettings.healthPollIntervalMs = Number.isFinite(n) && n >= 1000 ? Math.min(60000, Math.round(n)) : 5000;
  });
  grid.querySelectorAll('[data-policy]').forEach((btn) => {
    btn.addEventListener('click', () => {
      draftSettings.postRunPowerPolicy = btn.dataset.policy;
      grid.querySelectorAll('[data-policy]').forEach((x) => x.setAttribute('aria-pressed', String(x === btn)));
    });
  });

  header.querySelector('#btn-save-settings')?.addEventListener('click', () => saveCurrentSettings(refreshAll));
  header.querySelector('#btn-export-settings')?.addEventListener('click', () => {
    exportRowsAsFile([draftSettings], 'mother-settings-export', 'json');
  });

  renderSortableTable(container, {
    tableId: 'settings-snapshots-table',
    caption: 'Settings Snapshots (Up to 20 Retained)',
    rows: snapshots,
    rowKey: (r) => r.snapshotId,
    defaultSortKey: 'createdAtUtc',
    exportPrefix: 'mother-settings-snapshots',
    columns: [
      { key: 'snapshotId', label: 'Snapshot ID', numeric: true },
      { key: 'createdAtUtc', label: 'Created (UTC)', numeric: true },
      { key: 'fileSizeBytes', label: 'File Size', numeric: true },
    ],
    actions: (box, row) => {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'btn-secondary';
      btn.textContent = 'Rollback';
      btn.addEventListener('click', async () => {
        await apiPost('/api/v1/suite/settings/rollback', { snapshotId: row.snapshotId });
        announceStatus(`Rolled back settings to ${row.snapshotId}.`);
        await refreshAll();
      });
      box.appendChild(btn);
    },
  });
}
