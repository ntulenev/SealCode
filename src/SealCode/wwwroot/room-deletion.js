import { token } from './admin-token.js';

/** @param {{button: HTMLButtonElement, error: HTMLElement, roomId: string, window: Window, fetch: typeof fetch, isClosed: () => boolean}} options */
export function createRoomDeletion({ button, error, roomId, window, fetch, isClosed }) {
  const url = `/admin/rooms/${encodeURIComponent(roomId)}`;

  function hide() {
    button.classList.add('hidden');
  }

  async function refresh() {
    hide();
    try {
      const response = await fetch(`/room/${encodeURIComponent(roomId)}/permissions`, { cache: 'no-store' });
      if (!response.ok || response.redirected || isClosed()) return;
      const permissions = await response.json();
      if (permissions.CanDelete && !isClosed()) button.classList.remove('hidden');
    } catch {
      // Guests and expired admin sessions can still use the room.
    }
  }

  button.addEventListener('click', async () => {
    if (button.disabled || isClosed()) return;
    if (!window.confirm('Delete this room for everyone? This cannot be undone.')) return;
    button.disabled = true;
    error.classList.add('hidden');
    try {
      const response = await fetch(url, {
        method: 'DELETE',
        headers: { 'X-CSRF-TOKEN': await token(fetch) }
      });
      if (response.status === 401 || response.redirected) {
        hide();
        throw new Error('Admin session expired. Sign in again to delete the room.');
      }
      if (response.status === 403) {
        hide();
        throw new Error('You no longer have permission to delete this room.');
      }
      if (!response.ok) throw new Error('Could not delete the room. Try again.');
      window.location.assign('/');
    } catch (failure) {
      const message = failure instanceof Error ? failure.message : '';
      error.textContent = message === 'Session expired. Sign in again.'
        ? 'Admin session expired. Sign in again to delete the room.'
        : message || 'Could not delete the room. Try again.';
      error.classList.remove('hidden');
    } finally {
      button.disabled = false;
    }
  });

  return { refresh, hide };
}
