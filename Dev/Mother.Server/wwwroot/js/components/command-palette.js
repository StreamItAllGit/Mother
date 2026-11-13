// @module: Mother.Server.Web.Components.CommandPalette
// @purpose: Implements Ctrl+K Omni-Search and ? Hotkey Cheat-Sheet dialogs using <dialog closedby="any"> with JS light-dismiss fallback.
// @depends-on: /js/api/client.js
// @exports: initDialogs, openOmniDialog, openHotkeyDialog

import { escapeHtml } from '../api/client.js';

function attachDialogFallback(dialog) {
  if (!dialog) return;
  dialog.querySelectorAll('[data-close-dialog]').forEach((btn) => {
    btn.addEventListener('click', () => dialog.close());
  });

  // Fallback for browsers without HTMLDialogElement closedby="any" support
  if (!('closedBy' in HTMLDialogElement.prototype)) {
    dialog.addEventListener('click', (event) => {
      if (event.target !== dialog) return;
      const rect = dialog.getBoundingClientRect();
      const inside =
        event.clientX >= rect.left &&
        event.clientX <= rect.right &&
        event.clientY >= rect.top &&
        event.clientY <= rect.bottom;
      if (!inside || event.target === dialog) {
        dialog.close();
      }
    });
  }
}

export function openHotkeyDialog() {
  const dlg = document.getElementById('hotkey-dialog');
  if (dlg && !dlg.open) dlg.showModal();
}

export function openOmniDialog(getItemsFn, onSelectFn) {
  const dlg = document.getElementById('omni-dialog');
  const input = document.getElementById('omni-input');
  const list = document.getElementById('omni-results');
  if (!dlg || !input || !list) return;

  const renderMatches = () => {
    const q = input.value.trim().toLowerCase();
    const items = getItemsFn().filter(
      (item) =>
        !q ||
        item.label.toLowerCase().includes(q) ||
        item.kind.toLowerCase().includes(q)
    );
    list.innerHTML = items
      .slice(0, 12)
      .map(
        (item, idx) => `
      <li>
        <button type="button" class="nav-item" data-omni-idx="${idx}">
          <span class="badge-info">${escapeHtml(item.kind)}</span>
          <span class="truncate-single">${escapeHtml(item.label)}</span>
        </button>
      </li>`
      )
      .join('');
    list.querySelectorAll('[data-omni-idx]').forEach((btn) => {
      btn.addEventListener('click', () => {
        const picked = items[Number(btn.dataset.omniIdx)];
        dlg.close();
        if (picked) onSelectFn(picked);
      });
    });
  };

  input.value = '';
  input.oninput = renderMatches;
  renderMatches();
  if (!dlg.open) dlg.showModal();
  input.focus();
}

export function initDialogs() {
  attachDialogFallback(document.getElementById('omni-dialog'));
  attachDialogFallback(document.getElementById('hotkey-dialog'));
}
