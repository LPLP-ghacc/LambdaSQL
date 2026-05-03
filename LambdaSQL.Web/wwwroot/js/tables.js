// ── Table browser ─────────────────────────────────────────────────────────────

const Tables = (() => {
  let _current = null;
  let _currentInfo = null;
  let _page = 0;
  const PAGE_SIZE = 100;

  function init() {
    document.getElementById('btn-refresh-tables').addEventListener('click', refresh);
    refresh();
  }

  async function refresh() {
    const list = document.getElementById('table-list');
    list.innerHTML = '<li class="empty-hint"><span class="spinner"></span></li>';
    try {
      const tables = await API.tables();
      renderList(tables);
    } catch (e) {
      list.innerHTML = '<li class="empty-hint">Error loading tables</li>';
    }
  }

  function renderList(tables) {
    const list = document.getElementById('table-list');
    list.innerHTML = '';

    if (tables.length === 0) {
      list.innerHTML = '<li class="empty-hint">No tables yet</li>';
      return;
    }

    tables.forEach(name => {
      const li = document.createElement('li');
      li.innerHTML = `<span class="tbl-icon">▤</span>${name}`;
      if (name === _current) li.classList.add('active');
      li.addEventListener('click', () => openTable(name));
      list.appendChild(li);
    });
  }

  async function openTable(name) {
    _current = name;
    _page = 0;

    // Update sidebar active state
    document.querySelectorAll('.table-list li').forEach(li => {
      li.classList.toggle('active', li.textContent.trim() === name);
    });

    document.getElementById('topbar-title').textContent = name;

    // Load table info + first page
    try {
      _currentInfo = await API.tableInfo(name);
      await loadPage(name, 0);
    } catch (e) {
      showTableError(e.message);
    }
  }

  async function loadPage(name, page) {
    const container = document.getElementById('results-container');
    container.innerHTML = '<div class="results-empty"><span class="spinner"></span></div>';

    const offset = page * PAGE_SIZE;
    const sql = `select * from ${name} limit ${PAGE_SIZE}`;

    try {
      const results = await API.query(sql);
      const r = results[0];

      if (!r || r.error) {
        showTableError(r?.error || 'Unknown error');
        return;
      }

      renderTableView(name, r, page);
    } catch (e) {
      showTableError(e.message);
    }
  }

  function renderTableView(name, result, page) {
    const container = document.getElementById('results-container');
    container.innerHTML = '';

    // Toolbar above table
    const toolbar = document.createElement('div');
    toolbar.className = 'table-toolbar';

    const search = document.createElement('input');
    search.type = 'text';
    search.className = 'search-input';
    search.placeholder = 'Filter rows...';
    search.addEventListener('input', () => filterRows(search.value));

    const btnInsert = document.createElement('button');
    btnInsert.className = 'btn btn-primary btn-sm';
    btnInsert.textContent = '+ Insert Row';
    btnInsert.addEventListener('click', () => Modals.openInsert(name, _currentInfo));

    const btnDrop = document.createElement('button');
    btnDrop.className = 'btn btn-ghost btn-sm';
    btnDrop.style.marginLeft = 'auto';
    btnDrop.textContent = 'Drop Table';
    btnDrop.addEventListener('click', () => confirmDrop(name));

    toolbar.appendChild(search);
    toolbar.appendChild(btnInsert);
    toolbar.appendChild(btnDrop);
    container.appendChild(toolbar);

    // Table
    const wrap = document.createElement('div');
    wrap.className = 'data-table-wrap';
    wrap.id = 'table-data-wrap';

    const table = Editor.buildTable(result.columns, result.rows, row => {
      Modals.openEdit(name, _currentInfo, row);
    });
    table.id = 'main-data-table';
    wrap.appendChild(table);
    container.appendChild(wrap);

    // Count
    document.getElementById('results-count').textContent =
      `${result.rows.length} row${result.rows.length !== 1 ? 's' : ''}`;
  }

  function filterRows(query) {
    const table = document.getElementById('main-data-table');
    if (!table) return;
    const q = query.toLowerCase();
    table.querySelectorAll('tbody tr').forEach(tr => {
      const text = tr.textContent.toLowerCase();
      tr.style.display = text.includes(q) ? '' : 'none';
    });
  }

  function showTableError(msg) {
    const container = document.getElementById('results-container');
    container.innerHTML = `<div class="result-message error">✗ ${msg}</div>`;
  }

  async function confirmDrop(name) {
    if (!confirm(`Drop table "${name}"? This cannot be undone.`)) return;
    try {
      await API.query(`drop table ${name}`);
      _current = null;
      document.getElementById('topbar-title').textContent = 'Query Editor';
      document.getElementById('results-container').innerHTML =
        '<div class="results-empty">Run a query to see results</div>';
      document.getElementById('results-count').textContent = '';
      refresh();
    } catch (e) {
      alert('Error: ' + e.message);
    }
  }

  function getCurrent() { return _current; }
  function getCurrentInfo() { return _currentInfo; }
  function reloadCurrent() { if (_current) openTable(_current); }

  return { init, refresh, openTable, reloadCurrent, getCurrent, getCurrentInfo };
})();
