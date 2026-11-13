// @module: Mother.Server.Web.Api.Client
// @purpose: Typed REST fetch client, SSE suite event stream subscriber, CSV/JSON exporter, and ARIA live announcer.
// @depends-on: None
// @exports: apiGet, apiPost, apiPut, subscribeSuiteEvents, announceStatus, exportRowsAsFile, escapeHtml

export function escapeHtml(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

export async function apiGet(path) {
  const res = await fetch(path, { headers: { Accept: 'application/json' } });
  const text = await res.text();
  const body = text ? JSON.parse(text) : {};
  if (!res.ok) throw new Error(body.errorMessage || `HTTP ${res.status}`);
  return body;
}

export async function apiPost(path, payload = {}) {
  const res = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify(payload),
  });
  const text = await res.text();
  const body = text ? JSON.parse(text) : {};
  if (!res.ok) throw new Error(body.errorMessage || `HTTP ${res.status}`);
  return body;
}

export async function apiPut(path, payload = {}) {
  const res = await fetch(path, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify(payload),
  });
  const text = await res.text();
  const body = text ? JSON.parse(text) : {};
  if (!res.ok) throw new Error(body.errorMessage || `HTTP ${res.status}`);
  return body;
}

export function subscribeSuiteEvents(handlers) {
  const es = new EventSource('/api/v1/suite/events');
  const bind = (eventName, fn) => {
    if (!fn) return;
    es.addEventListener(eventName, (ev) => {
      try {
        fn(JSON.parse(ev.data));
      } catch {
        fn(null);
      }
    });
  };

  bind('heartbeat', handlers.onHeartbeat);
  bind('app_status', handlers.onAppStatus);
  bind('lock_changed', handlers.onLockChanged);
  bind('conflict_changed', handlers.onConflictChanged);
  bind('power_countdown', handlers.onPowerCountdown);
  bind('log_line', handlers.onLogLine);
  bind('alert_raised', handlers.onAlertRaised);

  es.onopen = () => handlers.onConnectionChange?.(true);
  es.onerror = () => handlers.onConnectionChange?.(false);
  return () => es.close();
}

export function announceStatus(message) {
  const el = document.getElementById('live-announcer');
  if (el) {
    el.textContent = '';
    requestAnimationFrame(() => {
      el.textContent = message;
    });
  }
}

export function exportRowsAsFile(rows, filenameBase, format) {
  const safeRows = Array.isArray(rows) ? rows : [];
  let mime = 'application/json;charset=utf-8';
  let content = JSON.stringify(safeRows, null, 2);
  let ext = 'json';

  if (format === 'csv') {
    ext = 'csv';
    mime = 'text/csv;charset=utf-8';
    const keys = safeRows.length > 0 ? Object.keys(safeRows[0]) : [];
    const header = keys.join(',');
    const lines = safeRows.map((row) =>
      keys.map((k) => `"${String(row[k] ?? '').replace(/"/g, '""')}"`).join(',')
    );
    content = [header, ...lines].join('\n');
  }

  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `${filenameBase}.${ext}`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
  announceStatus(`Exported ${safeRows.length} rows as ${ext.toUpperCase()}.`);
}
