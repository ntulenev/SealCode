import { token } from './admin-token.js';

/** Owner-only presentation-link controls; capability stays in this tab's memory.
 * @param {{document: Document, roomId: string, fetch: typeof globalThis.fetch, window: Window}} options
 */
export async function mountObserverLink({ document, roomId, fetch, window }) {
  const url = `/room/${encodeURIComponent(roomId)}/observer-link`;
  const response = await fetch(url, { cache: 'no-store' });
  if (!response.ok) return;
  const settings = await response.json();
  if (!settings.Enabled) return;
  let hasLink = settings.HasLink === true;
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
  link.value = '';
  link.readOnly = true;
  link.hidden = true;
  link.setAttribute('aria-label', 'Read-only guest link');
  const copy = document.createElement('button');
  copy.type = 'button';
  copy.textContent = 'Copy link';
  copy.hidden = true;
  const status = document.createElement('p');
  status.setAttribute('role', 'status');
  function updateControls(busy = false) {
    create.disabled = busy;
    link.hidden = copy.hidden = !link.value;
    copy.disabled = busy || !link.value;
    disable.hidden = !hasLink;
    disable.disabled = busy || !hasLink;
  }
  updateControls();
  /** @param {string} method */
  const mutate = async (method) => {
    updateControls(true);
    try {
      const result = await fetch(url, { method, headers: { 'X-CSRF-TOKEN': await token(fetch) } });
      if (!result.ok) throw new Error('Could not update guest access. Check your admin session.');
      link.value = method === 'POST' ? new URL((await result.json()).Url, window.location.origin).href : '';
      hasLink = method === 'POST';
      status.textContent = method === 'POST' ? 'Guest link ready. Copy and share it.' : 'Guest access disabled.';
    } catch (error) { status.textContent = error.message; }
    finally { updateControls(); }
  };
  create.addEventListener('click', () => {
    if (window.confirm('Create a new guest link? Any previous guest link and its sessions will be revoked.')) void mutate('POST');
  });
  disable.addEventListener('click', () => { if (!disable.disabled) void mutate('DELETE'); });
  copy.addEventListener('click', async () => {
    if (copy.disabled || !link.value) return;
    try { await window.navigator.clipboard.writeText(link.value); status.textContent = 'Guest link copied.'; }
    catch { link.select(); status.textContent = 'Select and copy the guest link.'; }
  });
  content.append(description, create, link, copy, disable, status);
  panel.append(summary, content);
  document.querySelector('.room-heading')?.append(panel);
}
