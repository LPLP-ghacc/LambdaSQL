// ── API client ────────────────────────────────────────────────────────────────

const API = {
  base: '',

  async query(sql) {
    const res = await fetch(`${API.base}/api/query`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sql })
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json(); // returns array of results
  },

  async tables() {
    const res = await fetch(`${API.base}/api/tables`);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
  },

  async tableInfo(name) {
    const res = await fetch(`${API.base}/api/tables/${encodeURIComponent(name)}`);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
  }
};
