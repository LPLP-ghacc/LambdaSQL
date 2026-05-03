// ── SQL Editor ────────────────────────────────────────────────────────────────

const Editor = (() => {
  let _lastResults = null;

  function init() {
    const ta = document.getElementById('sql-editor');
    const btnRun = document.getElementById('btn-run');
    const btnClear = document.getElementById('btn-clear');
    const btnExport = document.getElementById('btn-export');

    // Ctrl+Enter to run
    ta.addEventListener('keydown', e => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
        e.preventDefault();
        runQuery();
      }
      // Tab → 2 spaces
      if (e.key === 'Tab') {
        e.preventDefault();
        const s = ta.selectionStart, end = ta.selectionEnd;
        ta.value = ta.value.substring(0, s) + '  ' + ta.value.substring(end);
        ta.selectionStart = ta.selectionEnd = s + 2;
      }
    });

    btnRun.addEventListener('click', runQuery);
    btnClear.addEventListener('click', () => {
      ta.value = '';
      clearResults();
    });
    btnExport.addEventListener('click', exportCsv);
  }

  async function runQuery() {
    const sql = document.getElementById('sql-editor').value.trim();
    if (!sql) return;

    setBadge('info', '⏳ Running...');
    setResultsLoading();

    try {
      const results = await API.query(sql);
      _lastResults = results;
      renderResults(results);

      // Refresh table list if DDL
      const lower = sql.toLowerCase();
      if (lower.includes('create table') || lower.includes('drop table')) {
        Tables.refresh();
      }
    } catch (err) {
      setBadge('error', '✗ Error');
      showError(err.message);
    }
  }

  function renderResults(results) {
    const container = document.getElementById('results-container');
    const countEl   = document.getElementById('results-count');
    const exportBtn = document.getElementById('btn-export');
    container.innerHTML = '';

    if (!results || results.length === 0) {
      container.innerHTML = '<div class="results-empty">No results</div>';
      countEl.textContent = '';
      exportBtn.style.display = 'none';
      setBadge('ok', '✓ Done');
      return;
    }

    // Render each result block
    results.forEach((r, idx) => {
      if (r.error) {
        const div = document.createElement('div');
        div.className = 'result-message error';
        div.textContent = '✗ ' + r.error;
        container.appendChild(div);
        setBadge('error', '✗ Error');
        return;
      }

      if (!r.columns || r.columns.length === 0) {
        const div = document.createElement('div');
        div.className = 'result-message ok';
        div.textContent = '✓ ' + (r.message || `${r.rowsAffected} row(s) affected`);
        container.appendChild(div);
        setBadge('ok', `✓ ${r.rowsAffected} affected`);
        return;
      }

      // Result set
      const wrap = document.createElement('div');
      wrap.className = 'data-table-wrap';
      wrap.appendChild(buildTable(r.columns, r.rows));
      container.appendChild(wrap);

      countEl.textContent = `${r.rows.length} row${r.rows.length !== 1 ? 's' : ''}`;
      exportBtn.style.display = '';
      setBadge('ok', `✓ ${r.rows.length} rows`);
    });
  }

  function buildTable(columns, rows, onRowClick) {
    const table = document.createElement('table');
    table.className = 'data-table';

    // Head
    const thead = document.createElement('thead');
    const hr = document.createElement('tr');
    columns.forEach(col => {
      const th = document.createElement('th');
      th.textContent = col;
      hr.appendChild(th);
    });
    thead.appendChild(hr);
    table.appendChild(thead);

    // Body
    const tbody = document.createElement('tbody');
    rows.forEach(row => {
      const tr = document.createElement('tr');
      columns.forEach(col => {
        const td = document.createElement('td');
        const val = row[col];
        if (val === null || val === undefined) {
          td.textContent = 'NULL';
          td.className = 'null-val';
        } else if (typeof val === 'number') {
          td.textContent = val;
          td.className = 'num-val';
        } else if (typeof val === 'boolean') {
          td.textContent = val ? 'true' : 'false';
          td.className = 'bool-val';
        } else {
          td.textContent = val;
          td.className = 'str-val';
        }
        tr.appendChild(td);
      });
      if (onRowClick) tr.addEventListener('click', () => onRowClick(row));
      tbody.appendChild(tr);
    });
    table.appendChild(tbody);
    return table;
  }

  function setResultsLoading() {
    const container = document.getElementById('results-container');
    container.innerHTML = '<div class="results-empty"><span class="spinner"></span></div>';
    document.getElementById('results-count').textContent = '';
    document.getElementById('btn-export').style.display = 'none';
  }

  function clearResults() {
    const container = document.getElementById('results-container');
    container.innerHTML = '<div class="results-empty">Run a query to see results</div>';
    document.getElementById('results-count').textContent = '';
    document.getElementById('btn-export').style.display = 'none';
    document.getElementById('status-badge').textContent = '';
    document.getElementById('status-badge').className = 'status-badge';
    _lastResults = null;
  }

  function showError(msg) {
    const container = document.getElementById('results-container');
    container.innerHTML = `<div class="result-message error">✗ ${msg}</div>`;
  }

  function setBadge(type, text) {
    const el = document.getElementById('status-badge');
    el.textContent = text;
    el.className = `status-badge ${type}`;
  }

  function exportCsv() {
    if (!_lastResults) return;
    const r = _lastResults.find(x => x.columns && x.columns.length > 0);
    if (!r) return;

    const lines = [r.columns.join(',')];
    r.rows.forEach(row => {
      lines.push(r.columns.map(c => {
        const v = row[c];
        if (v === null || v === undefined) return '';
        const s = String(v);
        return s.includes(',') || s.includes('"') || s.includes('\n')
          ? `"${s.replace(/"/g, '""')}"` : s;
      }).join(','));
    });

    const blob = new Blob([lines.join('\n')], { type: 'text/csv' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'result.csv';
    a.click();
  }

  function setSql(sql) {
    document.getElementById('sql-editor').value = sql;
  }

  return { init, runQuery, renderResults, buildTable, setSql, clearResults };
})();
