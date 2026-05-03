// ── Modals ────────────────────────────────────────────────────────────────────

const Modals = (() => {

  // ── New Table ──────────────────────────────────────────────────────────────

  function initNewTable() {
    const modal   = document.getElementById('modal-new-table');
    const btnOpen = document.getElementById('btn-new-table');
    const btnClose = document.getElementById('modal-close');
    const btnCancel = document.getElementById('btn-cancel-table');
    const btnCreate = document.getElementById('btn-create-table');
    const btnAddCol = document.getElementById('btn-add-column');

    btnOpen.addEventListener('click', () => {
      document.getElementById('new-table-name').value = '';
      document.getElementById('columns-list').innerHTML = '';
      addColumnRow(); // start with one column
      modal.style.display = 'flex';
      document.getElementById('new-table-name').focus();
    });

    [btnClose, btnCancel].forEach(b => b.addEventListener('click', () => {
      modal.style.display = 'none';
    }));

    modal.addEventListener('click', e => {
      if (e.target === modal) modal.style.display = 'none';
    });

    btnAddCol.addEventListener('click', addColumnRow);

    btnCreate.addEventListener('click', async () => {
      const name = document.getElementById('new-table-name').value.trim();
      if (!name) { alert('Table name is required'); return; }

      const rows = document.querySelectorAll('#columns-list .col-row');
      if (rows.length === 0) { alert('Add at least one column'); return; }

      const cols = [];
      let valid = true;
      rows.forEach(row => {
        const colName = row.querySelector('.col-name').value.trim();
        const colType = row.querySelector('.col-type').value;
        const notNull = row.querySelector('.col-notnull').checked;
        const pk      = row.querySelector('.col-pk').checked;
        if (!colName) { valid = false; return; }
        let def = `${colName} ${colType}`;
        if (pk)      def += ' primary key';
        if (notNull && !pk) def += ' not null';
        cols.push(def);
      });

      if (!valid) { alert('All columns must have a name'); return; }

      const sql = `create table ${name} (${cols.join(', ')})`;
      try {
        await API.query(sql);
        modal.style.display = 'none';
        Tables.refresh();
        Tables.openTable(name);
      } catch (e) {
        alert('Error: ' + e.message);
      }
    });
  }

  function addColumnRow() {
    const list = document.getElementById('columns-list');
    const row = document.createElement('div');
    row.className = 'col-row';
    row.innerHTML = `
      <input type="text" class="input col-name" placeholder="column_name" />
      <select class="select col-type">
        <option value="int">int</option>
        <option value="bigint">bigint</option>
        <option value="float">float</option>
        <option value="text" selected>text</option>
        <option value="bool">bool</option>
      </select>
      <label class="col-check"><input type="checkbox" class="col-pk" /> PK</label>
      <label class="col-check"><input type="checkbox" class="col-notnull" /> NN</label>
      <button class="btn-icon col-remove" title="Remove">✕</button>
    `;
    row.querySelector('.col-remove').addEventListener('click', () => row.remove());
    row.querySelector('.col-pk').addEventListener('change', e => {
      if (e.target.checked) row.querySelector('.col-notnull').checked = true;
    });
    list.appendChild(row);
    row.querySelector('.col-name').focus();
  }

  // ── Insert Row ─────────────────────────────────────────────────────────────

  function openInsert(tableName, tableInfo) {
    const modal = document.getElementById('modal-insert-row');
    const body  = document.getElementById('insert-form-body');
    document.getElementById('insert-modal-title').textContent = `Insert into ${tableName}`;
    body.innerHTML = '';

    if (!tableInfo || !tableInfo.columns) {
      body.innerHTML = '<p style="color:var(--text-muted)">No schema info</p>';
    } else {
      tableInfo.columns.forEach(col => {
        const group = document.createElement('div');
        group.className = 'form-group';
        group.innerHTML = `
          <label>${col.name} <span style="color:var(--text-muted);font-weight:400">${col.type}${col.primaryKey ? ' · PK' : ''}${col.notNull ? ' · NN' : ''}</span></label>
          <input type="text" class="input" data-col="${col.name}" placeholder="${col.type === 'bool' ? 'true / false' : col.type}" />
        `;
        body.appendChild(group);
      });
    }

    modal.style.display = 'flex';
    body.querySelector('input')?.focus();

    document.getElementById('btn-confirm-insert').onclick = async () => {
      const inputs = body.querySelectorAll('input[data-col]');
      const cols = [], vals = [];
      inputs.forEach(inp => {
        cols.push(inp.dataset.col);
        vals.push(sqlValue(inp.value));
      });
      const sql = `insert into ${tableName} (${cols.join(', ')}) values (${vals.join(', ')})`;
      try {
        await API.query(sql);
        modal.style.display = 'none';
        Tables.reloadCurrent();
      } catch (e) {
        alert('Error: ' + e.message);
      }
    };
  }

  // ── Edit Row ───────────────────────────────────────────────────────────────

  function openEdit(tableName, tableInfo, row) {
    const modal = document.getElementById('modal-edit-row');
    const body  = document.getElementById('edit-form-body');
    body.innerHTML = '';

    const cols = tableInfo?.columns || Object.keys(row).map(k => ({ name: k, type: 'text' }));
    const pkCol = cols.find(c => c.primaryKey);

    cols.forEach(col => {
      const group = document.createElement('div');
      group.className = 'form-group';
      const val = row[col.name];
      group.innerHTML = `
        <label>${col.name} <span style="color:var(--text-muted);font-weight:400">${col.type}${col.primaryKey ? ' · PK' : ''}</span></label>
        <input type="text" class="input" data-col="${col.name}"
          value="${val === null || val === undefined ? '' : val}"
          ${col.primaryKey ? 'readonly style="opacity:0.6"' : ''} />
      `;
      body.appendChild(group);
    });

    modal.style.display = 'flex';

    // Save
    document.getElementById('btn-confirm-edit').onclick = async () => {
      const inputs = body.querySelectorAll('input[data-col]');
      const sets = [];
      inputs.forEach(inp => {
        if (!inp.readOnly)
          sets.push(`${inp.dataset.col} = ${sqlValue(inp.value)}`);
      });

      if (sets.length === 0) { modal.style.display = 'none'; return; }

      let sql;
      if (pkCol) {
        const pkVal = sqlValue(String(row[pkCol.name]));
        sql = `update ${tableName} set ${sets.join(', ')} where ${pkCol.name} = ${pkVal}`;
      } else {
        // No PK: build WHERE from all original values
        const where = cols.map(c => `${c.name} = ${sqlValue(String(row[c.name]))}`).join(' and ');
        sql = `update ${tableName} set ${sets.join(', ')} where ${where}`;
      }

      try {
        await API.query(sql);
        modal.style.display = 'none';
        Tables.reloadCurrent();
      } catch (e) {
        alert('Error: ' + e.message);
      }
    };

    // Delete
    document.getElementById('btn-delete-row').onclick = async () => {
      if (!confirm('Delete this row?')) return;

      let sql;
      if (pkCol) {
        const pkVal = sqlValue(String(row[pkCol.name]));
        sql = `delete from ${tableName} where ${pkCol.name} = ${pkVal}`;
      } else {
        const where = cols.map(c => `${c.name} = ${sqlValue(String(row[c.name]))}`).join(' and ');
        sql = `delete from ${tableName} where ${where}`;
      }

      try {
        await API.query(sql);
        modal.style.display = 'none';
        Tables.reloadCurrent();
      } catch (e) {
        alert('Error: ' + e.message);
      }
    };
  }

  // ── Close buttons ──────────────────────────────────────────────────────────

  function initCloseButtons() {
    ['insert-modal-close', 'btn-cancel-insert'].forEach(id => {
      document.getElementById(id)?.addEventListener('click', () => {
        document.getElementById('modal-insert-row').style.display = 'none';
      });
    });
    ['edit-modal-close', 'btn-cancel-edit'].forEach(id => {
      document.getElementById(id)?.addEventListener('click', () => {
        document.getElementById('modal-edit-row').style.display = 'none';
      });
    });

    // Click outside to close
    ['modal-insert-row', 'modal-edit-row'].forEach(id => {
      const el = document.getElementById(id);
      el.addEventListener('click', e => { if (e.target === el) el.style.display = 'none'; });
    });
  }

  // ── Helpers ────────────────────────────────────────────────────────────────

  function sqlValue(v) {
    if (v === '' || v === 'NULL' || v === 'null') return 'null';
    if (v === 'true' || v === 'false') return v;
    if (!isNaN(v) && v.trim() !== '') return v;
    return `'${v.replace(/'/g, "''")}'`;
  }

  function init() {
    initNewTable();
    initCloseButtons();
  }

  return { init, openInsert, openEdit };
})();
