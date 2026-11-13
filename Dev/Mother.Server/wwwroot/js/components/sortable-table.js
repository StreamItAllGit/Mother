// @module: Mother.Server.Web.Components.SortableTable
// @purpose: Accessible sortable table with Intl.Collator, DOM moveBefore reordering, Bulk Select, and CSV/JSON export.
// @depends-on: /js/api/client.js
// @exports: renderSortableTable

import { announceStatus, escapeHtml, exportRowsAsFile } from '../api/client.js';

const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

export function renderSortableTable(host, config) {
  const {
    tableId,
    caption,
    rows = [],
    columns = [],
    rowKey = (r, i) => String(r.id ?? r.appId ?? i),
    exportPrefix = 'mother-export',
    actions = null,
    onRowInspect = null,
  } = config;

  const selectedKeys = new Set();
  let sortKey = config.defaultSortKey || columns[0]?.key || '';
  let sortDir = 'ascending';

  const captionId = `${tableId}-caption`;
  const wrapper = document.createElement('div');
  wrapper.className = 'panel-card';

  const toolbar = document.createElement('div');
  toolbar.className = 'table-toolbar';
  toolbar.innerHTML = `
    <span class="truncate-single" data-selected-count>0 of ${rows.length} selected</span>
    <div class="row-gap">
      <button type="button" class="btn-secondary" data-export="csv" aria-label="Export selected or all rows as CSV">Export CSV</button>
      <button type="button" class="btn-secondary" data-export="json" aria-label="Export selected or all rows as JSON">Export JSON</button>
    </div>
  `;

  const scrollRegion = document.createElement('div');
  scrollRegion.className = 'table-scroll-region';
  scrollRegion.setAttribute('role', 'region');
  scrollRegion.setAttribute('aria-labelledby', captionId);
  scrollRegion.tabIndex = 0;

  const table = document.createElement('table');
  table.className = 'mother-table';
  table.id = tableId;

  const capEl = document.createElement('caption');
  capEl.id = captionId;
  capEl.textContent = caption;
  table.appendChild(capEl);

  const thead = document.createElement('thead');
  const headRow = document.createElement('tr');

  const checkTh = document.createElement('th');
  checkTh.scope = 'col';
  checkTh.innerHTML = `<input type="checkbox" data-select-all aria-label="Select all rows in ${escapeHtml(caption)}" />`;
  headRow.appendChild(checkTh);

  const thMap = new Map();
  for (const col of columns) {
    const th = document.createElement('th');
    th.scope = 'col';
    if (col.key === sortKey) th.setAttribute('aria-sort', sortDir);
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'sort-btn';
    btn.dataset.sortKey = col.key;
    btn.textContent = col.label;
    th.appendChild(btn);
    headRow.appendChild(th);
    thMap.set(col.key, th);
  }

  if (actions || onRowInspect) {
    const actTh = document.createElement('th');
    actTh.scope = 'col';
    actTh.textContent = 'Actions';
    headRow.appendChild(actTh);
  }
  thead.appendChild(headRow);
  table.appendChild(thead);

  const tbody = document.createElement('tbody');
  const rowElements = rows.map((row, index) => {
    const key = rowKey(row, index);
    const tr = document.createElement('tr');
    tr.dataset.rowKey = key;
    tr.dataset.rowIndex = String(index);

    const cbTd = document.createElement('td');
    cbTd.innerHTML = `<input type="checkbox" data-row-check="${escapeHtml(key)}" aria-label="Select row ${escapeHtml(key)}" />`;
    tr.appendChild(cbTd);

    for (const col of columns) {
      const td = document.createElement('td');
      if (col.numeric) td.className = 'tabular-nums';
      td.innerHTML = col.render ? col.render(row) : escapeHtml(row[col.key] ?? '');
      tr.appendChild(td);
    }

    if (actions || onRowInspect) {
      const actTd = document.createElement('td');
      const box = document.createElement('div');
      box.className = 'row-gap';
      if (onRowInspect) {
        const inspectBtn = document.createElement('button');
        inspectBtn.type = 'button';
        inspectBtn.className = 'btn-secondary';
        inspectBtn.textContent = 'Inspect';
        inspectBtn.addEventListener('click', () => onRowInspect(row));
        box.appendChild(inspectBtn);
      }
      if (actions) actions(box, row);
      actTd.appendChild(box);
      tr.appendChild(actTd);
    }
    return { tr, row, key };
  });

  for (const item of rowElements) tbody.appendChild(item.tr);
  table.appendChild(tbody);
  scrollRegion.appendChild(table);
  wrapper.appendChild(toolbar);
  wrapper.appendChild(scrollRegion);
  host.appendChild(wrapper);

  const updateCount = () => {
    const label = toolbar.querySelector('[data-selected-count]');
    if (label) label.textContent = `${selectedKeys.size} of ${rows.length} selected`;
  };

  const applySort = (nextKey) => {
    if (sortKey === nextKey) {
      sortDir = sortDir === 'ascending' ? 'descending' : 'ascending';
    } else {
      sortKey = nextKey;
      sortDir = 'ascending';
    }
    for (const [k, th] of thMap.entries()) {
      if (k === sortKey) th.setAttribute('aria-sort', sortDir);
      else th.removeAttribute('aria-sort');
    }
    const mult = sortDir === 'ascending' ? 1 : -1;
    const sorted = [...rowElements].sort((a, b) => {
      const av = String(a.row[sortKey] ?? '');
      const bv = String(b.row[sortKey] ?? '');
      return collator.compare(av, bv) * mult;
    });
    const useMoveBefore = typeof tbody.moveBefore === 'function';
    for (const item of sorted) {
      if (useMoveBefore) tbody.moveBefore(item.tr, null);
      else tbody.appendChild(item.tr);
    }
    announceStatus(`Sorted ${caption} by ${sortKey} (${sortDir}).`);
  };

  thead.querySelectorAll('.sort-btn').forEach((btn) => {
    btn.addEventListener('click', () => applySort(btn.dataset.sortKey));
  });

  const selectAll = thead.querySelector('[data-select-all]');
  selectAll?.addEventListener('change', () => {
    const checked = selectAll.checked;
    selectedKeys.clear();
    tbody.querySelectorAll('[data-row-check]').forEach((cb) => {
      cb.checked = checked;
      if (checked) selectedKeys.add(cb.dataset.rowCheck);
    });
    updateCount();
  });

  tbody.querySelectorAll('[data-row-check]').forEach((cb) => {
    cb.addEventListener('change', () => {
      if (cb.checked) selectedKeys.add(cb.dataset.rowCheck);
      else selectedKeys.delete(cb.dataset.rowCheck);
      updateCount();
    });
  });

  toolbar.querySelectorAll('[data-export]').forEach((btn) => {
    btn.addEventListener('click', () => {
      const targetRows = selectedKeys.size > 0
        ? rowElements.filter((x) => selectedKeys.has(x.key)).map((x) => x.row)
        : rows;
      exportRowsAsFile(targetRows, exportPrefix, btn.dataset.export);
    });
  });
}
