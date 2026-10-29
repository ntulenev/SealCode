// Classic script loaded before CSS so a saved light theme is applied before paint.
(() => {
  const key = 'sealcode.theme';
  const root = document.documentElement;
  /** @type {Storage | undefined} */
  let storage;
  try { storage = window.localStorage; } catch { /* Browser privacy settings. */ }

  function readTheme() {
    try { return storage?.getItem(key) === 'light' ? 'light' : 'dark'; }
    catch { return 'dark'; }
  }

  /** @param {string} theme */
  function applyTheme(theme) {
    root.dataset.theme = theme;
    const next = theme === 'dark' ? 'light' : 'dark';
    for (const button of document.querySelectorAll('[data-theme-toggle]')) {
      button.setAttribute('aria-label', `Switch to ${next} theme`);
      button.setAttribute('title', `Switch to ${next} theme`);
      const label = button.querySelector('[data-theme-label]');
      if (label) label.textContent = next === 'light' ? 'Light' : 'Dark';
    }
    window.dispatchEvent(new Event('sealcode:themechange'));
  }

  applyTheme(readTheme());

  function bindButtons() {
    applyTheme(root.dataset.theme);
    for (const button of document.querySelectorAll('[data-theme-toggle]')) {
      button.addEventListener('click', () => {
        const next = root.dataset.theme === 'dark' ? 'light' : 'dark';
        applyTheme(next);
        try { storage?.setItem(key, next); } catch { /* Keep the in-memory choice. */ }
      });
    }
  }

  if (document.readyState === 'loading')
    document.addEventListener('DOMContentLoaded', bindButtons, { once: true });
  else bindButtons();

  window.addEventListener('storage', (event) => {
    if (storage && event.storageArea === storage && (event.key === key || event.key === null))
      applyTheme(readTheme());
  });
})();
