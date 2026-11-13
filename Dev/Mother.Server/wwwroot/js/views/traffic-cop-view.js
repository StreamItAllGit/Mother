// @module: Mother.Server.Web.Views.TrafficCopView
// @purpose: Sub-page pill view for .hklock Resource Traffic Cop locks, Coexistence Conflicts arbitration, and Schedule Deferrals.
// @depends-on: /js/api/client.js, /js/components/sortable-table.js
// @exports: renderTrafficCopView

import { announceStatus, apiPost, escapeHtml } from '../api/client.js';
import { renderSortableTable } from '../components/sortable-table.js';

let activePill = 'locks';

function formatUtcTimestamp(raw) {
  if (typeof raw !== 'string' || !raw.trim()) return '—';
  const trimmed = raw.trim();
  return Number.isNaN(Date.parse(trimmed)) ? '—' : trimmed;
}

export function renderTrafficCopView(container, state, refreshAll) {
  container.innerHTML = '';
  const locks = state.trafficCop?.activeLocks || [];
  const deferrals = state.trafficCop?.recentDeferrals || [];
  const conflicts = state.coexistence?.conflicts || [];

  const top = document.createElement('div');
  top.className = 'table-toolbar';
  top.innerHTML = `
    <h1 style="margin:0;font-size:20px">Resource Traffic Cop &amp; Coexistence</h1>
    <div class="row-gap">
      <button type="button" id="btn-purge-stale" class="btn-secondary">Purge Stale Locks</button>
      <button type="button" id="btn-apply-stagger" class="btn-primary">Apply Safe-Stagger</button>
    </div>
  `;
  container.appendChild(top);

  const pills = document.createElement('div');
  pills.className = 'subpage-pills';
  pills.setAttribute('role', 'tablist');
  pills.setAttribute('aria-label', 'Traffic Cop Sub-Pages');
  const tabs = [
    { id: 'locks', label: `Active Locks (${locks.length})` },
    { id: 'conflicts', label: `Coexistence Conflicts (${conflicts.length})` },
    { id: 'deferrals', label: `Schedule Deferrals (${deferrals.length})` },
  ];
  for (const t of tabs) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'subpage-pill';
    btn.setAttribute('role', 'tab');
    btn.setAttribute('aria-selected', String(activePill === t.id));
    btn.textContent = t.label;
    btn.addEventListener('click', () => {
      activePill = t.id;
      renderTrafficCopView(container, state, refreshAll);
    });
    pills.appendChild(btn);
  }
  container.appendChild(pills);

  top.querySelector('#btn-purge-stale')?.addEventListener('click', async () => {
    const res = await apiPost('/api/v1/suite/traffic-cop/purge-stale');
    announceStatus(`Purged ${res.purgedCount ?? 0} stale lockfiles.`);
    await refreshAll();
  });

  top.querySelector('#btn-apply-stagger')?.addEventListener('click', async () => {
    await apiPost('/api/v1/suite/coexistence/apply-recommendations', { presetId: 'suite-safe-stagger' });
    announceStatus('Applied suite-safe-stagger recommendations.');
    await refreshAll();
  });

  if (activePill === 'locks') {
    renderSortableTable(container, {
      tableId: 'traffic-locks-table',
      caption: 'Cross-App .hklock Files',
      rows: locks,
      rowKey: (r) => r.lockId,
      defaultSortKey: 'appId',
      exportPrefix: 'mother-hklocks',
      columns: [
        { key: 'appId', label: 'App' },
        { key: 'pid', label: 'PID', numeric: true },
        { key: 'operation', label: 'Operation' },
        { key: 'driveMount', label: 'Drive' },
        { key: 'status', label: 'Status', render: (r) => `<span class="${r.status === 'live' ? 'badge-ok' : 'badge-warn'}">${escapeHtml(String(r.status || '').toUpperCase())}</span>` },
        { key: 'acquiredAtUtc', label: 'Acquired (UTC)', numeric: true, render: (r) => `<span class="tabular-nums">${escapeHtml(formatUtcTimestamp(r.acquiredAtUtc))}</span>` },
        { key: 'expiresAtUtc', label: 'Expires (UTC)', numeric: true, render: (r) => `<span class="tabular-nums">${escapeHtml(formatUtcTimestamp(r.expiresAtUtc))}</span>` },
      ],
    });
  } else if (activePill === 'conflicts') {
    renderSortableTable(container, {
      tableId: 'coexistence-conflicts-table',
      caption: 'Detected Coexistence Conflicts',
      rows: conflicts,
      rowKey: (r) => r.conflictId,
      defaultSortKey: 'severity',
      exportPrefix: 'mother-conflicts',
      columns: [
        { key: 'severity', label: 'Severity', render: (r) => `<span class="${r.severity === 'high' ? 'badge-fail' : 'badge-warn'}">${escapeHtml(String(r.severity || '').toUpperCase())}</span>` },
        { key: 'kind', label: 'Type' },
        { key: 'primaryAppId', label: 'Primary App' },
        { key: 'secondaryAppId', label: 'Peer App' },
        { key: 'resourceKey', label: 'Shared Resource' },
        { key: 'summary', label: 'Summary' },
      ],
    });
  } else {
    renderSortableTable(container, {
      tableId: 'schedule-deferrals-table',
      caption: 'SQLite Schedule Deferrals',
      rows: deferrals,
      rowKey: (r) => r.id,
      defaultSortKey: 'createdAtUtc',
      exportPrefix: 'mother-deferrals',
      columns: [
        { key: 'appId', label: 'App' },
        { key: 'jobId', label: 'Job ID' },
        { key: 'driveMount', label: 'Drive' },
        { key: 'status', label: 'Status', render: (r) => `<span class="badge-warn">${escapeHtml(String(r.status || 'deferred').toUpperCase())}</span>` },
        { key: 'nextRunAtUtc', label: 'Next Run (UTC)', numeric: true, render: (r) => `<span class="tabular-nums">${escapeHtml(formatUtcTimestamp(r.nextRunAtUtc))}</span>` },
        { key: 'description', label: 'Description' },
      ],
    });
  }
}
