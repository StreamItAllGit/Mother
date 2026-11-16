// @module: Mother.Server.Web.App
// @purpose: Main frontend shell orchestrating Outer Tabs (Alt+1..7), Sidebar (Ctrl+B), Mobile Nav, Dialogs, Tooltips, Docked Terminal, and SSE updates.
// @depends-on: /js/api/contracts.js, /js/api/client.js, /js/components/*, /js/views/*, /vendor/foundation/foundation-terminal.js
// @exports: Mother WebUI runtime

import { DEFAULT_SISTER_TABS } from './api/contracts.js';
import { announceStatus, apiGet, apiPost, escapeHtml, subscribeSuiteEvents } from './api/client.js';
import { initDialogs, openHotkeyDialog, openOmniDialog } from './components/command-palette.js';
import { closeDetailDrawer, initDrawerControls, openQrDrawer } from './components/drawer.js';
import { renderFleetView } from './views/fleet-view.js';
import { renderTrafficCopView } from './views/traffic-cop-view.js';
import { renderDoctorVaultsView } from './views/doctor-vaults-view.js';
import { renderSettingsView, saveCurrentSettings } from './views/settings-view.js';

const appState = {
  route: 'fleet',
  activeTabId: 'mother',
  health: null,
  apps: [],
  trafficCop: null,
  coexistence: null,
  power: null,
  doctor: null,
  notifications: [],
  secrets: [],
  settingsBundle: null,
};

let terminalApi = null;

function renderTabStrip() {
  const strip = document.getElementById('app-tab-strip');
  if (!strip) return;
  strip.innerHTML = '';
  const tabs = [
    { appId: 'mother', name: 'Mother', isOnline: true },
    ...DEFAULT_SISTER_TABS.slice(1).map((def) => {
      const live = appState.apps.find((a) => a.appId === def.appId);
      return { appId: def.appId, name: live?.name || def.name, isOnline: Boolean(live?.isOnline) };
    }),
  ];

  tabs.forEach((tab, idx) => {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = `suite-tab ${appState.activeTabId === tab.appId ? 'is-active' : ''}`;
    btn.setAttribute('role', 'tab');
    btn.setAttribute('aria-selected', String(appState.activeTabId === tab.appId));
    btn.innerHTML = `<span class="tab-dot ${tab.isOnline ? 'is-online' : ''}" aria-hidden="true"></span><span>${escapeHtml(tab.name)}</span>`;
    btn.addEventListener('click', () => switchOuterTab(tab.appId));
    btn.dataset.hotkeyIndex = String(idx + 1);
    strip.appendChild(btn);
  });
}

function switchOuterTab(appId) {
  appState.activeTabId = appId;
  renderTabStrip();
  const viewContainer = document.getElementById('view-container');
  const frameWrap = document.getElementById('sister-frame-container');
  const frame = document.getElementById('sister-frame');
  const titleEl = document.getElementById('sister-frame-title');
  if (!viewContainer || !frameWrap || !frame || !titleEl) return;

  if (appId === 'mother') {
    frameWrap.hidden = true;
    viewContainer.hidden = false;
    renderActiveRoute();
  } else {
    const live = appState.apps.find((a) => a.appId === appId);
    viewContainer.hidden = true;
    frameWrap.hidden = false;
    titleEl.textContent = `${live?.name || appId} (/app/${appId}/)`;
    frame.src = `/app/${encodeURIComponent(appId)}/`;
  }
}

function setRoute(nextRoute) {
  appState.route = nextRoute;
  appState.activeTabId = 'mother';
  document.getElementById('sister-frame-container').hidden = true;
  document.getElementById('view-container').hidden = false;
  document.querySelectorAll('[data-route]').forEach((el) => {
    el.classList.toggle('is-active', el.dataset.route === nextRoute);
  });
  renderTabStrip();
  renderActiveRoute();
}

function renderActiveRoute() {
  const container = document.getElementById('view-container');
  if (!container) return;
  if (appState.route === 'fleet') renderFleetView(container, appState, refreshAll);
  else if (appState.route === 'traffic-cop') renderTrafficCopView(container, appState, refreshAll);
  else if (appState.route === 'doctor') renderDoctorVaultsView(container, appState, refreshAll);
  else if (appState.route === 'settings') renderSettingsView(container, appState, refreshAll);
}

async function refreshAll() {
  try {
    const [health, appsRes, trafficCop, coexistence, power, doctor, notifs, secrets, settingsBundle] = await Promise.all([
      apiGet('/api/v1/suite/health'),
      apiGet('/api/v1/suite/apps'),
      apiGet('/api/v1/suite/scheduler/traffic-cop'),
      apiGet('/api/v1/suite/coexistence/status'),
      apiGet('/api/v1/suite/power/status'),
      apiGet('/api/v1/suite/doctor'),
      apiGet('/api/v1/suite/notifications?limit=100'),
      apiGet('/api/v1/suite/secrets/metadata'),
      apiGet('/api/v1/suite/settings'),
    ]);
    Object.assign(appState, {
      health,
      apps: appsRes.apps || [],
      trafficCop,
      coexistence,
      power,
      doctor,
      notifications: notifs.notifications || [],
      secrets: secrets.keys || [],
      settingsBundle,
    });

    const ver = document.getElementById('suite-version');
    if (ver) ver.textContent = `v${health.version || '0.1.0'}`;
    const modeChip = document.getElementById('chip-mode');
    if (modeChip) modeChip.textContent = health.isolatedMode ? 'ISOLATED' : 'PROD';
    const pwrChip = document.getElementById('chip-power');
    if (pwrChip) {
      const rawPolicy = String(power.policy || 'StayAwake');
      const policyLabel = rawPolicy.toLowerCase() === 'stayawake' ? 'STAY AWAKE' : rawPolicy.toUpperCase();
      pwrChip.textContent = power.countdownActive ? `SLEEP ${power.secondsRemaining}S` : policyLabel;
    }
    const cBadge = document.getElementById('badge-conflicts');
    if (cBadge) cBadge.textContent = String((coexistence.conflicts || []).length);

    renderTabStrip();
    if (appState.activeTabId === 'mother') renderActiveRoute();
  } catch (err) {
    announceStatus(`Refresh failed: ${err.message}`);
  }
}

function initTooltips() {
  const tip = document.getElementById('mother-tooltip');
  const titleEl = document.getElementById('tooltip-title');
  const bodyEl = document.getElementById('tooltip-body');
  if (!tip || !titleEl || !bodyEl) return;

  const showTip = (target) => {
    const title = target.getAttribute('data-tooltip-title');
    const body = target.getAttribute('data-tooltip-body');
    if (!title && !body) return;
    titleEl.textContent = title || '';
    bodyEl.textContent = body || '';
    const rect = target.getBoundingClientRect();
    tip.style.position = 'fixed';
    tip.style.left = `${Math.max(8, Math.min(window.innerWidth - 330, rect.left))}px`;
    tip.style.top = `${Math.min(window.innerHeight - 80, rect.bottom + 8)}px`;
    tip.hidden = false;
  };
  const hideTip = () => { tip.hidden = true; };

  document.querySelectorAll('[data-tooltip-title]').forEach((el) => {
    el.addEventListener('mouseenter', () => showTip(el));
    el.addEventListener('focus', () => showTip(el));
    el.addEventListener('mouseleave', hideTip);
    el.addEventListener('blur', hideTip);
  });
  window.addEventListener('keydown', (e) => { if (e.key === 'Escape') hideTip(); });
}

function toggleSidebar() {
  const rail = document.getElementById('sidebar-rail');
  if (!rail) return;
  rail.classList.toggle('is-collapsed');
}

async function boot() {
  initDialogs();
  initDrawerControls();
  initTooltips();

  document.querySelectorAll('[data-route]').forEach((btn) => {
    btn.addEventListener('click', () => setRoute(btn.dataset.route));
  });
  document.getElementById('btn-sidebar-toggle')?.addEventListener('click', toggleSidebar);
  document.getElementById('btn-qr-access')?.addEventListener('click', () => openQrDrawer(window.location.origin + '/'));
  document.getElementById('btn-stop-all')?.addEventListener('click', async () => {
    await apiPost('/api/v1/suite/jobs/stop-all');
    announceStatus('Stopped all running sister apps.');
    await refreshAll();
  });
  document.getElementById('btn-sister-wake')?.addEventListener('click', async () => {
    if (appState.activeTabId !== 'mother') {
      await apiPost(`/api/v1/suite/apps/${encodeURIComponent(appState.activeTabId)}/wake`);
      switchOuterTab(appState.activeTabId);
    }
  });
  document.getElementById('btn-sister-open')?.addEventListener('click', () => {
    if (appState.activeTabId !== 'mother') window.open(`/app/${encodeURIComponent(appState.activeTabId)}/`, '_blank', 'noopener');
  });

  const openSearch = () => openOmniDialog(
    () => [
      { kind: 'Route', label: 'Fleet Overview', action: () => setRoute('fleet') },
      { kind: 'Route', label: 'Traffic Cop & Coexistence', action: () => setRoute('traffic-cop') },
      { kind: 'Route', label: 'Doctor, Power & Vaults', action: () => setRoute('doctor') },
      { kind: 'Route', label: 'Suite Settings', action: () => setRoute('settings') },
      ...appState.apps.map((a) => ({ kind: 'App', label: `${a.name} (${a.state})`, action: () => switchOuterTab(a.appId) })),
    ],
    (picked) => picked.action()
  );

  document.getElementById('btn-omni-search')?.addEventListener('click', openSearch);
  document.getElementById('btn-hotkeys')?.addEventListener('click', openHotkeyDialog);

  window.addEventListener('keydown', (e) => {
    if (e.ctrlKey && e.key.toLowerCase() === 'b') { e.preventDefault(); toggleSidebar(); }
    else if (e.ctrlKey && e.key.toLowerCase() === 'k') { e.preventDefault(); openSearch(); }
    else if (e.ctrlKey && e.key.toLowerCase() === 's') { e.preventDefault(); if (appState.route === 'settings') saveCurrentSettings(refreshAll); }
    else if (e.altKey && e.key >= '1' && e.key <= '7') {
      e.preventDefault();
      const target = DEFAULT_SISTER_TABS[Number(e.key) - 1];
      if (target) switchOuterTab(target.appId);
    } else if (e.key === '?' && !['INPUT', 'TEXTAREA'].includes(document.activeElement?.tagName)) {
      e.preventDefault();
      openHotkeyDialog();
    } else if (e.key === 'Escape') {
      closeDetailDrawer();
    }
  });

  const termHost = document.getElementById('terminal-host');
  if (termHost && window.FoundationTerminalDrawer) {
    terminalApi = window.FoundationTerminalDrawer.mountFoundationTerminalDrawer({
      container: termHost,
      storagePrefix: 'mother-terminal',
      onClearTerminal: () => apiPost('/api/v1/terminal/clear').catch(() => undefined),
      onCancelAllJobs: () => apiPost('/api/v1/suite/jobs/stop-all').then(refreshAll).catch(() => undefined),
    });
    apiGet('/api/v1/terminal/history?limit=100').then((res) => terminalApi?.setLogs(res.lines || [])).catch(() => undefined);
  }

  subscribeSuiteEvents({
    onConnectionChange: (connected) => terminalApi?.setConnected(connected),
    onLogLine: (line) => { if (line) terminalApi?.appendLogs([line]); },
    onAppStatus: () => refreshAll(),
    onLockChanged: () => refreshAll(),
    onConflictChanged: () => refreshAll(),
    onPowerCountdown: () => refreshAll(),
  });

  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('/sw.js').catch(() => undefined);
  }

  await refreshAll();
  const requestedTab = new URLSearchParams(window.location.search).get('tab');
  if (requestedTab && DEFAULT_SISTER_TABS.some((t) => t.appId === requestedTab)) {
    switchOuterTab(requestedTab);
  }
}

boot();
