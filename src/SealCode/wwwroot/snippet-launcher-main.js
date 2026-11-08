const actions = document.querySelector('.header-actions');
if (actions) {
  fetch('/admin/snippets/access', { cache: 'no-store' }).then(response => response.json()).then(access => {
    if (!access.CanAccess) return;
    const link = document.createElement('a');
    link.href = '/admin/snippets';
    link.target = '_blank';
    link.rel = 'noopener';
    link.className = 'snippet-launcher';
    link.title = 'Open question library (Ctrl / Cmd + click for a new tab)';
    link.setAttribute('aria-label', 'Open question library');
    link.innerHTML = '<svg width="23" height="23" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" aria-hidden="true"><path d="M12 5v15M12 5C9 3 5 3 2 4v15c3-1 7-1 10 1 3-2 7-2 10-1V4c-3-1-7-1-10 1Z"/></svg>';
    const updateDestination = () => {
      const language = /** @type {HTMLSelectElement | null} */ (document.getElementById('languageSelect'));
      const url = new URL('/admin/snippets', window.location.origin);
      if (language?.value) url.searchParams.set('language', language.value);
      link.href = url.href;
    };
    updateDestination();
    // Read the current value at opening time, including remote room-language changes.
    link.addEventListener('pointerdown', updateDestination);
    link.addEventListener('contextmenu', updateDestination);
    const themeToggle = actions.querySelector('[data-theme-toggle]');
    if (themeToggle) {
      const tools = document.createElement('div');
      tools.className = 'snippet-tools';
      themeToggle.before(tools);
      tools.append(link, themeToggle);
    } else {
      actions.append(link);
    }
    link.addEventListener('click', event => {
      updateDestination();
      if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
      event.preventDefault();
      const dialog = document.createElement('dialog');
      dialog.className = 'snippet-dialog';
      const toolbar = document.createElement('div');
      toolbar.className = 'snippet-dialog-toolbar';
      const newTab = document.createElement('a');
      newTab.href = link.href;
      newTab.target = '_blank';
      newTab.rel = 'noopener';
      newTab.textContent = 'Open in new tab ↗';
      const close = document.createElement('button');
      close.className = 'ghost';
      close.textContent = 'Close ×';
      const frame = document.createElement('iframe');
      frame.title = 'Question library';
      frame.src = link.href;
      toolbar.append(newTab, close);
      dialog.append(toolbar, frame);
      dialog.setAttribute('aria-label', 'Question library');
      document.body.append(dialog);
      close.onclick = () => dialog.close();
      dialog.addEventListener('close', () => { dialog.remove(); link.focus(); });
      dialog.showModal();
      close.focus();
    });
  }).catch(() => { /* The optional library stays hidden when access cannot be checked. */ });
}
