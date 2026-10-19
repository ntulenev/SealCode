(async () => {
  const labels = document.querySelectorAll('[data-app-version]');
  if (labels.length === 0) return;
  try {
    const response = await fetch('/version', { cache: 'no-store' });
    if (!response.ok) throw new Error('Version unavailable');
    const data = await response.json();
    if (typeof data.Version !== 'string' || !data.Version) throw new Error('Version unavailable');
    const version = data.Version === 'development' ? 'development' : `v${data.Version}`;
    const commit = typeof data.Commit === 'string' && data.Commit ? ` · ${data.Commit.slice(0, 7)}` : '';
    for (const label of labels) {
      label.textContent = `SealCode ${version}${commit}`;
      label.title = data.InformationalVersion || data.Version;
    }
  } catch {
    for (const label of labels) label.textContent = 'SealCode · version unavailable';
  }
})();
