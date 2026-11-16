// @module: Mother.Server.Web.Views.DoctorVaultsView
// @purpose: Sub-page pill view for Fleet Doctor diagnostics/backups, Power & Watchdog coordinator, Notifications, and DPAPI secret vault metadata.
// @depends-on: /js/api/client.js, /js/components/sortable-table.js
// @exports: renderDoctorVaultsView

import { announceStatus, apiPost, escapeHtml } from '../api/client.js';
import { renderSortableTable } from '../components/sortable-table.js';

let activePill = 'doctor';

export function renderDoctorVaultsView(container, state, refreshAll) {
  container.innerHTML = '';
  const doctor = state.doctor || { suiteChecks: [] };
  const power = state.power || {};
  const notifications = state.notifications || [];
  const secrets = state.secrets || [];

  const overall = String(doctor.overall || 'PASS').toUpperCase();
  const overallClass = overall === 'PASS' ? 'badge-ok' : overall === 'WARN' ? 'badge-warn' : 'badge-fail';
  const top = document.createElement('div');
  top.className = 'table-toolbar';
  top.innerHTML = `
    <div>
      <h1 style="margin:0;font-size:20px">Doctor, Power &amp; Vault Inspector</h1>
      <div class="row-gap" style="margin-top:4px">
        <span class="${overallClass}">OVERALL: ${escapeHtml(overall)}</span>
        <span class="kpi-label">Crashes: ${escapeHtml(doctor.crashBundleCount ?? 0)}</span>
      </div>
    </div>
    <div class="row-gap">
      <button type="button" id="btn-doctor-run" class="btn-secondary">Run Doctor</button>
      <button type="button" id="btn-doctor-repair" class="btn-secondary">Auto-Repair</button>
      <button type="button" id="btn-doctor-backup" class="btn-secondary">Backup DB</button>
      <button type="button" id="btn-doctor-bundle" class="btn-primary">Export Bundle</button>
    </div>
  `;
  container.appendChild(top);

  const pills = document.createElement('div');
  pills.className = 'subpage-pills';
  pills.setAttribute('role', 'tablist');
  pills.setAttribute('aria-label', 'Doctor and Power Sub-Pages');
  for (const t of [
    { id: 'doctor', label: `Doctor (${(doctor.suiteChecks || []).length})` },
    { id: 'power', label: `Power (${power.policy || 'StayAwake'})` },
    { id: 'alerts', label: `Notifications (${notifications.length})` },
    { id: 'vaults', label: `DPAPI Vaults (${secrets.length})` },
  ]) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'subpage-pill';
    btn.setAttribute('role', 'tab');
    btn.setAttribute('aria-selected', String(activePill === t.id));
    btn.textContent = t.label;
    btn.addEventListener('click', () => {
      activePill = t.id;
      renderDoctorVaultsView(container, state, refreshAll);
    });
    pills.appendChild(btn);
  }
  container.appendChild(pills);

  top.querySelector('#btn-doctor-run')?.addEventListener('click', async () => {
    await apiPost('/api/v1/suite/doctor/run');
    announceStatus('Fleet Doctor checks completed.');
    await refreshAll();
  });
  top.querySelector('#btn-doctor-repair')?.addEventListener('click', async () => {
    await apiPost('/api/v1/suite/doctor/repair');
    announceStatus('Fleet Doctor auto-repair completed.');
    await refreshAll();
  });
  top.querySelector('#btn-doctor-backup')?.addEventListener('click', async () => {
    const res = await apiPost('/api/v1/suite/doctor/backup-dbs');
    announceStatus(`Created SQLite backup ${res.fileName || ''}.`);
    await refreshAll();
  });
  top.querySelector('#btn-doctor-bundle')?.addEventListener('click', async () => {
    const res = await apiPost('/api/v1/suite/doctor/export-bundle');
    announceStatus(`Exported diagnostic bundle ${res.fileName || ''}.`);
    await refreshAll();
  });

  if (activePill === 'doctor') {
    renderSortableTable(container, {
      tableId: 'doctor-checks-table',
      caption: 'Suite Diagnostic Checks',
      rows: doctor.suiteChecks || [],
      rowKey: (r) => r.checkId,
      defaultSortKey: 'status',
      exportPrefix: 'mother-doctor-checks',
      columns: [
        { key: 'checkId', label: 'Check ID' },
        { key: 'title', label: 'Title' },
        {
          key: 'status',
          label: 'Status',
          render: (r) => {
            const st = String(r.status || '').toUpperCase();
            return `<span class="${st === 'PASS' ? 'badge-ok' : st === 'WARN' ? 'badge-warn' : 'badge-fail'}">${escapeHtml(st)}</span>`;
          },
        },
        { key: 'detail', label: 'Detail' },
      ],
    });
  } else if (activePill === 'power') {
    const card = document.createElement('div');
    card.className = 'panel-card';
    card.innerHTML = `
      <h2 style="margin-top:0;font-size:16px">Fleet Post-Run Power Policy &amp; Watchdog</h2>
      <div class="row-gap" style="margin-bottom:10px">
        <span class="${power.simulated ? 'badge-warn' : 'badge-ok'}">${power.simulated ? 'SIMULATED' : 'LIVE'}</span>
        <span class="${power.fleetBusy ? 'badge-warn' : 'badge-info'}">BUSY: ${power.fleetBusy ? 'YES' : 'NO'}</span>
        <span class="badge-info">SLEEP INHIBITED: ${power.sleepInhibited ? 'YES' : 'NO'}</span>
        <span class="${power.watchdogRegistered ? 'badge-ok' : 'badge-warn'}">WATCHDOG: ${power.watchdogRegistered ? 'REGISTERED' : 'MISSING'}</span>
        <span class="${power.countdownActive ? 'badge-warn' : 'badge-info'}">COUNTDOWN: ${power.countdownActive ? `${escapeHtml(power.secondsRemaining)}S` : 'INACTIVE'}</span>
      </div>
      <div class="segmented-picker" id="power-policy-picker">
        ${['StayAwake', 'Sleep', 'Shutdown'].map((p) => `<button type="button" class="segment-btn" data-policy="${p}" aria-pressed="${power.policy === p}">${p}</button>`).join('')}
      </div>
      <div class="row-gap" style="margin-top:12px">
        <button type="button" id="btn-power-eval" class="btn-secondary">Evaluate Tick</button>
        <button type="button" id="btn-power-cancel" class="btn-danger">Cancel Countdown</button>
      </div>
    `;
    card.querySelectorAll('[data-policy]').forEach((btn) => {
      btn.addEventListener('click', async () => {
        await apiPost('/api/v1/suite/power/action', { policy: btn.dataset.policy });
        announceStatus(`Power policy set to ${btn.dataset.policy}.`);
        await refreshAll();
      });
    });
    card.querySelector('#btn-power-eval')?.addEventListener('click', async () => {
      await apiPost('/api/v1/suite/power/action', { action: 'evaluate' });
      await refreshAll();
    });
    card.querySelector('#btn-power-cancel')?.addEventListener('click', async () => {
      await apiPost('/api/v1/suite/power/cancel-countdown');
      announceStatus('Cancelled pending sleep countdown.');
      await refreshAll();
    });
    container.appendChild(card);
  } else if (activePill === 'alerts') {
    const bar = document.createElement('div');
    bar.innerHTML = `<button type="button" id="btn-test-alert" class="btn-secondary">Send Test Notification</button>`;
    bar.querySelector('#btn-test-alert')?.addEventListener('click', async () => {
      await apiPost('/api/v1/suite/notifications/test', {});
      announceStatus('Dispatched test suite notification.');
      await refreshAll();
    });
    container.appendChild(bar);
    renderSortableTable(container, {
      tableId: 'notifications-table',
      caption: 'Notification Dispatch History',
      rows: notifications,
      rowKey: (r) => r.id,
      defaultSortKey: 'createdAtUtc',
      exportPrefix: 'mother-notifications',
      columns: [
        { key: 'appId', label: 'App' },
        { key: 'slug', label: 'Slug' },
        { key: 'title', label: 'Title' },
        {
          key: 'status',
          label: 'Status',
          render: (r) => {
            const st = String(r.status || 'sent').toUpperCase();
            const cls = st === 'SENT' || st === 'OK' || st === 'DELIVERED' ? 'badge-ok' : st === 'SIMULATED' ? 'badge-info' : 'badge-warn';
            return `<span class="${cls}">${escapeHtml(st)}</span>`;
          },
        },
        { key: 'createdAtUtc', label: 'Created (UTC)', numeric: true },
      ],
    });
  } else {
    renderSortableTable(container, {
      tableId: 'vaults-table',
      caption: 'DPAPI Secret Vault Key Metadata (Zero Plaintext)',
      rows: secrets,
      rowKey: (r) => `${r.appId}:${r.keyName}`,
      defaultSortKey: 'appId',
      exportPrefix: 'mother-vault-metadata',
      columns: [
        { key: 'appId', label: 'App' },
        { key: 'keyName', label: 'Key Name' },
        { key: 'vaultSizeBytes', label: 'Vault Size', numeric: true },
        { key: 'lastModifiedUtc', label: 'Last Modified (UTC)', numeric: true },
      ],
    });
  }
}
