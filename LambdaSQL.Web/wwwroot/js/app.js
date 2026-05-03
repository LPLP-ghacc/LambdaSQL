// ── App bootstrap ─────────────────────────────────────────────────────────────

document.addEventListener('DOMContentLoaded', () => {
  Editor.init();
  Tables.init();
  Modals.init();

  // Keyboard shortcuts
  document.addEventListener('keydown', e => {
    // Escape closes any open modal
    if (e.key === 'Escape') {
      document.querySelectorAll('.modal-overlay').forEach(m => {
        m.style.display = 'none';
      });
    }
  });
});
