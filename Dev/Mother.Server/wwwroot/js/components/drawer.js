// @module: Mother.Server.Web.Components.Drawer
// @purpose: Slide-Over Right Drawer inspector for sister-app details and Tailscale Mobile QR preview.
// @depends-on: /js/api/client.js
// @exports: openDetailDrawer, closeDetailDrawer, openQrDrawer

import { escapeHtml } from '../api/client.js';

export function closeDetailDrawer() {
  const drawer = document.getElementById('detail-drawer');
  if (drawer) drawer.hidden = true;
}

export function openDetailDrawer(title, renderBodyFn) {
  const drawer = document.getElementById('detail-drawer');
  const titleEl = document.getElementById('drawer-title');
  const bodyEl = document.getElementById('drawer-body');
  if (!drawer || !titleEl || !bodyEl) return;

  titleEl.textContent = title;
  bodyEl.innerHTML = '';
  renderBodyFn(bodyEl);
  drawer.hidden = false;
  document.getElementById('drawer-close')?.focus();
}

export function openQrDrawer(listenUrl) {
  openDetailDrawer('Tailscale Mobile Access QR', (body) => {
    const encoded = encodeURIComponent(listenUrl);
    body.innerHTML = `
      <p>Scan this QR code from a device connected to your Tailscale tailnet (<code>100.64.0.0/10</code>).</p>
      <div class="panel-card" style="display:grid;place-items:center">
        <img src="/api/v1/suite/qr.svg?url=${encoded}" alt="Tailscale Access QR Code" width="220" height="220" />
      </div>
      <div class="panel-card">
        <div class="kpi-label">Target URL</div>
        <div class="truncate-single">${escapeHtml(listenUrl)}</div>
      </div>
    `;
  });
}

export function initDrawerControls() {
  document.getElementById('drawer-close')?.addEventListener('click', closeDetailDrawer);
}
