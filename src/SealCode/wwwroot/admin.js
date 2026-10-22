import { token } from './admin-token.js';
import { setLanguageOptions } from './language-options.js';

/** Creates the admin page with explicit browser dependencies. */
/** @param {{document: Document, window: Window, fetch: typeof fetch, confirm: (message: string) => boolean, clipboard: Pick<Clipboard, "writeText">}} options */
export function createAdminPage({ document, window, fetch, confirm, clipboard }) {
  const roomsEl = document.getElementById('rooms');
  const createBtn = /** @type {HTMLButtonElement} */ (document.getElementById('createRoom'));
  const nameInput = /** @type {HTMLInputElement} */ (document.getElementById('roomName'));
  const langInput = /** @type {HTMLSelectElement} */ (
    document.getElementById('roomLanguage')
  );
  const resultEl = document.getElementById('createResult');

  async function loadLanguages() {
    try {
      const res = await fetch('/languages');
      if (!res.ok || res.redirected) {
        throw requestError(res);
      }
      const languages = await res.json();
      setLanguageOptions(document, langInput, languages);
    } catch (error) {
      setLanguageOptions(document, langInput, []);
      showError(error);
    }
  }

  async function fetchRooms() {
    try {
      const res = await fetch('/admin/rooms');
      if (!res.ok || res.redirected) {
        throw requestError(res);
      }
      const rooms = await res.json();
      renderRooms(rooms);
    } catch (error) {
      showError(error);
    }
  }

  /** @param {Array<Record<string, any>>} rooms */
  function renderRooms(rooms) {
    if (!rooms.length) {
      roomsEl.innerHTML = '<p class="muted">No rooms yet.</p>';
      return;
    }

    roomsEl.innerHTML = '';
    for (const room of rooms) {
      const row = document.createElement('div');
      row.className = 'room-row';

      const name = document.createElement('div');
      const title = document.createElement('strong');
      title.textContent = room.Name ?? '';
      const id = document.createElement('small');
      id.textContent = room.RoomId;
      name.append(title, document.createElement('br'), id);

      const lang = document.createElement('div');
      const languageBadge = document.createElement('span');
      languageBadge.className = 'badge';
      languageBadge.textContent = room.Language;
      lang.appendChild(languageBadge);

      const users = document.createElement('div');
      users.textContent = `${room.UsersCount} users`;

      const updated = document.createElement('div');
      updated.textContent = new Date(room.LastUpdatedUtc).toLocaleString();

      const createdBy = document.createElement('div');
      createdBy.textContent = room.CreatedBy || 'unknown';

      const actions = document.createElement('div');
      actions.className = 'room-actions';

      const link = document.createElement('button');
      link.textContent = 'Copy link';
      link.addEventListener('click', async () => {
        try {
          const url = `${window.location.origin}/room/${encodeURIComponent(room.RoomId)}`;
          await clipboard.writeText(url);
          resultEl.textContent = `Copied ${url}`;
        } catch {
          resultEl.textContent = 'Could not copy the link.';
        }
      });

      actions.append(link);

      const del = document.createElement('button');
      del.textContent = 'Delete';
      if (!room.CanDelete) {
        del.disabled = true;
        del.title = 'Only super admins can delete rooms created by other users.';
      } else {
        del.addEventListener('click', async () => {
          try {
            if (!confirm('Delete this room?')) return;
            const res = await fetch(`/admin/rooms/${encodeURIComponent(room.RoomId)}`, {
              method: 'DELETE',
              headers: { 'X-CSRF-TOKEN': await token(fetch) }
            });
            if (res.status === 403) {
              resultEl.textContent =
                'Only super admins can delete rooms created by other users.';
              return;
            }
            if (!res.ok || res.redirected) {
              throw requestError(res);
            }
            resultEl.textContent = 'Room deleted.';
            await fetchRooms();
          } catch (error) {
            showError(error);
          }
        });
      }
      actions.append(del);
      row.append(name, lang, users, updated, createdBy, actions);
      roomsEl.append(row);
    }
  }

  createBtn.addEventListener('click', async () => {
    if (createBtn.disabled) return;
    createBtn.disabled = true;
    try {
      const name = nameInput.value.trim();
      const language = langInput.value;
      if (!name) {
        resultEl.textContent = 'Room name is required.';
        return;
      }
      if (!language) {
        resultEl.textContent = 'Language is required.';
        return;
      }

      const res = await fetch('/admin/rooms', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-CSRF-TOKEN': await token(fetch)
        },
        body: JSON.stringify({ name, language })
      });

      if (!res.ok || res.redirected) {
        throw requestError(res);
      }

      const room = await res.json();
      const url = `${window.location.origin}/room/${encodeURIComponent(room.RoomId)}`;
      resultEl.textContent = `Room created: ${url}`;
      nameInput.value = '';
      await fetchRooms();
    } catch (error) {
      showError(error);
    } finally {
      createBtn.disabled = false;
    }
  });

  /** @param {Response} response */
  function requestError(response) {
    return new Error(
      response.status === 401 || response.redirected
        ? 'Session expired. Sign in again.'
        : 'Request failed. Try again.'
    );
  }
  /** @param {unknown} error */
  function showError(error) {
    resultEl.textContent =
      error instanceof Error && error.message === 'Session expired. Sign in again.'
        ? error.message
        : 'Request failed. Try again.';
  }

  const ready = Promise.all([fetchRooms(), loadLanguages()]);
  return { ready };
}
