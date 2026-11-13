// @module: Mother.Server.Web.Views.FleetView
// @purpose: Renders Suite Fleet Overview KPIs and sortable Sister Applications supervision table with Cold-Wake, Stop, Residency, and Inspect drawer.
// @depends-on: /js/api/client.js, /js/components/sortable-table.js, /js/components/drawer.js
// @exports: renderFleetView

import { announceStatus, apiPost, escapeHtml } from '../api/client.js';
import { openDetailDrawer } from '../components/drawer.js';
import { renderSortableTable } from '../components/sortable-table.js';

export function renderFleetView(container, state, refreshAll) {
  container.innerHTML = '';
  const health = state.health || {};
  const apps = state.apps || [];

  const header = document.createElement('div');
  header.className = 'table-toolbar';
  header.innerHTML = `
    <div>
      <h1 style="margin:0;font-size:20px">Fleet Overview</h1>
      <span class="kpi-label">Single Shared Suite Daemon · Uptime ${escapeHtml(health.uptimeSeconds ?? 0)}s · SQLite v${escapeHtml(health.sqliteUserVersion ?? 1)}</span>
    </div>
    <div class="row-gap">
      <button type="button" id="btn-reload-manifests" class="btn-secondary">Reload Manifests</button>
    </div>
  `;
  container.appendChild(header);

  const kpis = document.createElement('div');
  kpis.className = 'kpi-grid';
  kpis.innerHTML = `
    <div class="kpi-card"><div class="kpi-label">Registered Apps</div><div class="kpi-value tabular-nums">${escapeHtml(apps.length)}</div></div>
    <div class="kpi-card"><div class="kpi-label">Online Now</div><div class="kpi-value tabular-nums">${escapeHtml(apps.filter((a) => a.isOnline).length)}</div></div>
    <div class="kpi-card"><div class="kpi-label">Warm / Cold</div><div class="kpi-value tabular-nums">${escapeHtml(health.warmApps ?? 0)} / ${escapeHtml(health.coldApps ?? 0)}</div></div>
    <div class="kpi-card"><div class="kpi-label">Active Conflicts</div><div class="kpi-value tabular-nums">${escapeHtml((state.coexistence?.conflicts || []).length)}</div></div>
  `;
  container.appendChild(kpis);

  header.querySelector('#btn-reload-manifests')?.addEventListener('click', async () => {
    await apiPost('/api/v1/suite/apps/reload');
    announceStatus('Reloaded sister application manifests.');
    await refreshAll();
  });

  renderSortableTable(container, {
    tableId: 'fleet-apps-table',
    caption: 'Supervised Sister Applications',
    rows: apps,
    defaultSortKey: 'name',
    exportPrefix: 'mother-fleet-apps',
    columns: [
      { key: 'name', label: 'Application', render: (r) => `<strong>${escapeHtml(r.name)}</strong><div class="kpi-label">${escapeHtml(r.appId)}</div>` },
      { key: 'state', label: 'State', render: (r) => `<span class="${r.isOnline ? 'badge-ok' : 'badge-info'}">${escapeHtml(String(r.state || '').toUpperCase())}</span>` },
      { key: 'residency', label: 'Residency', render: (r) => `<span class="badge-info">${escapeHtml(String(r.residency || '').toUpperCase())}</span>` },
      { key: 'effectivePort', label: 'Port', numeric: true },
      { key: 'activeJobs', label: 'Active Jobs', numeric: true },
      { key: 'version', label: 'Version', numeric: true },
    ],
    onRowInspect: (row) => {
      openDetailDrawer(`${row.name} (${row.appId})`, (body) => {
        body.innerHTML = `
          <div class="panel-card">
            <div class="kpi-label">Residency Mode</div>
            <div class="segmented-picker" id="drawer-residency-picker">
              ${['warm', 'cold', 'disabled'].map((m) => `<button type="button" class="segment-btn" data-res="${m}" aria-pressed="${row.residency === m}">${m[0].toUpperCase()}${m.slice(1)}</button>`).join('')}
            </div>
          </div>
          <div class="panel-card">
            <div class="kpi-label">Target Drives</div>
            <div>${escapeHtml((row.targetDrives || []).join(', ') || 'None')}</div>
          </div>
          <div class="panel-card">
            <div class="kpi-label">Shared Mounts</div>
            <div>${escapeHtml((row.sharedMounts || []).join(', ') || 'None')}</div>
          </div>
          <div class="panel-card">
            <div class="kpi-label">Mutually Exclusive With</div>
            <div>${escapeHtml((row.mutuallyExclusiveWith || []).join(', ') || 'None')}</div>
          </div>
        `;
        body.querySelectorAll('[data-res]').forEach((btn) => {
          btn.addEventListener('click', async () => {
            await apiPost(`/api/v1/suite/apps/${encodeURIComponent(row.appId)}/residency`, { residencyMode: btn.dataset.res });
            announceStatus(`Updated ${row.name} residency to ${btn.dataset.res}.`);
            await refreshAll();
          });
        });
      });
    },
    actions: (box, row) => {
      const wakeBtn = document.createElement('button');
      wakeBtn.type = 'button';
      wakeBtn.className = row.isOnline ? 'btn-secondary' : 'btn-primary';
      wakeBtn.textContent = row.isOnline ? 'Stop' : 'Wake';
      wakeBtn.addEventListener('click', async () => {
        try {
          if (row.isOnline) {
            await apiPost(`/api/v1/suite/apps/${encodeURIComponent(row.appId)}/stop`);
            announceStatus(`Stopped ${row.name}.`);
          } else {
            await apiPost(`/api/v1/suite/apps/${encodeURIComponent(row.appId)}/wake`);
            announceStatus(`Woke ${row.name}.`);
          }
        } catch (err) {
          announceStatus(`Action failed: ${err.message}`);
        }
        await refreshAll();
      });
      box.appendChild(wakeBtn);
    },
  });
}
