import { token } from './admin-token.js';

/** Owner-only presentation-link controls; the server restores the current link on reload.
 * @param {{document: Document, roomId: string, fetch: typeof globalThis.fetch, window: Window}} options
 */
export async function mountObserverLink({ document, roomId, fetch, window }) {
  const url = `/room/${encodeURIComponent(roomId)}/observer-link`;
  const permissionsUrl = `/room/${encodeURIComponent(roomId)}/permissions`;
  const cancellation = new AbortController();
  async function canManage() {
    // Guest-link management uses the same owner/super-admin policy as room deletion.
    // This discovery endpoint returns 200 for ordinary participants without exposing a capability.
    const response = await fetch(permissionsUrl, { cache: 'no-store', signal: cancellation.signal });
    return response.ok && (await response.json()).CanDelete === true;
  }
  if (!await canManage()) return;
  const response = await fetch(url, { cache: 'no-store', signal: cancellation.signal });
  if (!response.ok) return;
  const settings = await response.json();
  if (!settings.Enabled) return;
  let hasLink = settings.HasLink === true;
  let busy = false;
  let stopped = false;
  let pendingRefresh = false;
  /** @type {number | undefined} */
  let timer;
  /** @type {BroadcastChannel | undefined} */
  let channel;
  const panel = document.createElement('details');
  panel.className = 'observer-link';
  const summary = document.createElement('summary');
  summary.textContent = 'Guest link';
  const content = document.createElement('div');
  content.className = 'observer-link-panel';
  const description = document.createElement('p');
  description.textContent = `Read-only presentation. Up to ${settings.MaxPerRoom} observers. Creating a link replaces the previous link and disconnects its guests.`;
  const create = document.createElement('button');
  create.type = 'button';
  create.textContent = 'Create / replace link';
  const disable = document.createElement('button');
  disable.type = 'button';
  disable.className = 'ghost';
  disable.textContent = 'Disable guest access';
  const link = document.createElement('input');
  link.type = 'text';
  link.value = settings.Url ? new URL(settings.Url, window.location.origin).href : '';
  link.readOnly = true;
  link.hidden = true;
  link.setAttribute('aria-label', 'Read-only guest link');
  const copy = document.createElement('button');
  copy.type = 'button';
  copy.textContent = 'Copy link';
  copy.hidden = true;
  const status = document.createElement('p');
  status.setAttribute('role', 'status');
  function updateControls(value = false) {
    busy = value;
    create.disabled = busy;
    link.hidden = copy.hidden = !link.value;
    copy.disabled = busy || !link.value;
    disable.hidden = !hasLink;
    disable.disabled = busy || !hasLink;
  }
  updateControls();
  function schedule() {
    window.clearTimeout(timer);
    if (!stopped) timer = window.setTimeout(() => { void refresh(); }, 5000);
  }
  function finish() {
    if (stopped) return;
    updateControls();
    if (pendingRefresh) { pendingRefresh = false; void refresh(); }
    else schedule();
  }
  function failed() {
    link.value = '';
    hasLink = false;
    status.textContent = 'Could not verify guest access. Retrying automatically…';
  }
  async function readCurrent() {
    const allowed = await canManage();
    if (stopped) return false;
    if (!allowed) {
      panel.hidden = true;
      hasLink = false;
      link.value = '';
      return true;
    }
    const current = await fetch(url, { cache: 'no-store', signal: cancellation.signal });
    if (!current.ok) {
      if ([401, 403, 404].includes(current.status)) panel.hidden = true;
      throw new Error('Guest access could not be verified.');
    }
    const state = await current.json();
    if (stopped) return false;
    panel.hidden = !state.Enabled;
    hasLink = state.Enabled && state.HasLink;
    link.value = hasLink && state.Url ? new URL(state.Url, window.location.origin).href : '';
    return true;
  }
  /** @returns {Promise<void>} */
  async function refresh() {
    if (stopped) return;
    if (busy) { pendingRefresh = true; return; }
    updateControls(true);
    try {
      if (await readCurrent()) {
        status.textContent = link.value ? 'Guest link ready. Copy and share it.' : 'Guest access disabled.';
      }
    } catch { if (!stopped) failed(); }
    finally { finish(); }
  }
  /** @param {string} method */
  const mutate = async (method) => {
    if (busy || stopped) return;
    updateControls(true);
    try {
      const result = await fetch(url, { method, signal: cancellation.signal,
        headers: { 'X-CSRF-TOKEN': await token(fetch) } });
      if (!result.ok) throw new Error('Could not update guest access. Check your admin session.');
      const value = method === 'POST' ? new URL((await result.json()).Url, window.location.origin).href : '';
      if (stopped) return;
      link.value = value;
      hasLink = method === 'POST';
      status.textContent = method === 'POST' ? 'Guest link ready. Copy and share it.' : 'Guest access disabled.';
      // Re-read even our own result: a concurrent administrator may already have replaced it.
      pendingRefresh = true;
      try { channel?.postMessage({ roomId }); } catch { /* Polling still reconciles other tabs. */ }
    } catch (error) { if (!stopped) status.textContent = error.message; }
    finally { finish(); }
  };
  create.addEventListener('click', () => {
    if (!busy && !stopped && window.confirm('Create a new guest link? Any previous guest link and its sessions will be revoked.')) void mutate('POST');
  });
  disable.addEventListener('click', () => { if (!disable.disabled) void mutate('DELETE'); });
  copy.addEventListener('click', async () => {
    if (copy.disabled || !link.value || stopped) return;
    updateControls(true);
    try {
      // Reconcile notifications received while the authoritative check was in flight.
      do { pendingRefresh = false; if (!await readCurrent()) return; } while (pendingRefresh);
      if (!link.value) { status.textContent = 'Guest access disabled.'; return; }
      try { await window.navigator.clipboard.writeText(link.value); status.textContent = 'Guest link copied.'; }
      catch { link.select(); status.textContent = 'Select and copy the guest link.'; }
    } catch { if (!stopped) failed(); }
    finally { finish(); }
  });
  const onFocus = () => { void refresh(); };
  const onVisibility = () => { if (document.visibilityState === 'visible') void refresh(); };
  window.addEventListener('focus', onFocus);
  document.addEventListener('visibilitychange', onVisibility);
  panel.addEventListener('toggle', () => { if (panel.open) void refresh(); });
  try {
    const Channel = /** @type {Window & typeof globalThis} */ (window).BroadcastChannel;
    if (Channel) {
      channel = new Channel('sealcode.observer-links');
      channel.onmessage = event => { if (event.data?.roomId === roomId) void refresh(); };
    }
  } catch { /* Private browsing or policy restrictions can disable cross-tab messaging. */ }
  window.addEventListener('pagehide', () => {
    stopped = true;
    window.clearTimeout(timer);
    cancellation.abort();
    channel?.close();
    window.removeEventListener('focus', onFocus);
    document.removeEventListener('visibilitychange', onVisibility);
  });
  window.addEventListener('pageshow', event => { if (event.persisted) window.location.reload(); });
  schedule();
  content.append(description, create, link, copy, disable, status);
  panel.append(summary, content);
  document.querySelector('.room-heading')?.append(panel);
}
