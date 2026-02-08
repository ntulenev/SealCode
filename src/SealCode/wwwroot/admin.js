const roomsEl = document.getElementById('rooms');
const createBtn = document.getElementById('createRoom');
const nameInput = document.getElementById('roomName');
const langInput = document.getElementById('roomLanguage');
const resultEl = document.getElementById('createResult');

function setLanguageOptions(selectEl, languages, selected) {
  const cleaned = Array.isArray(languages)
    ? languages.map((language) => (typeof language === 'string' ? language.trim() : ''))
      .filter((language) => language)
    : [];
  const unique = [...new Set(cleaned)];

  selectEl.innerHTML = '';
  if (unique.length === 0) {
    const option = document.createElement('option');
    option.value = '';
    option.textContent = 'No languages configured';
    option.disabled = true;
    option.selected = true;
    selectEl.appendChild(option);
    selectEl.disabled = true;
    return;
  }

  for (const language of unique) {
    const option = document.createElement('option');
    option.value = language;
    option.textContent = language;
    selectEl.appendChild(option);
  }

  selectEl.disabled = false;
  if (selected && unique.includes(selected)) {
    selectEl.value = selected;
  } else {
    selectEl.selectedIndex = 0;
  }
}

async function loadLanguages() {
  try {
    const res = await fetch('/languages');
    if (!res.ok) {
      throw new Error('Failed to load languages');
    }
    const languages = await res.json();
    setLanguageOptions(langInput, languages);
  } catch {
    setLanguageOptions(langInput, []);
    resultEl.textContent = 'Failed to load languages.';
  }
}

async function fetchRooms() {
  const res = await fetch('/admin/rooms');
  if (!res.ok) {
    roomsEl.innerHTML = '<p class="error">Failed to load rooms.</p>';
    return;
  }
  const rooms = await res.json();
  renderRooms(rooms);
}

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
    name.innerHTML = `<strong>${escapeHtml(room.Name)}</strong><br/><small>${room.RoomId}</small>`;

    const lang = document.createElement('div');
    lang.innerHTML = `<span class="badge">${room.Language}</span>`;

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
    link.addEventListener('click', () => {
      const url = `${window.location.origin}/room/${room.RoomId}`;
      navigator.clipboard.writeText(url);
      resultEl.textContent = `Copied ${url}`;
    });

    const del = document.createElement('button');
    del.textContent = 'Delete';
    del.addEventListener('click', async () => {
      if (!confirm('Delete this room?')) return;
      await fetch(`/admin/rooms/${room.RoomId}`, { method: 'DELETE' });
      fetchRooms();
    });

    actions.append(link, del);
    row.append(name, lang, users, updated, createdBy, actions);
    roomsEl.append(row);
  }
}

createBtn.addEventListener('click', async () => {
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
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, language })
  });

  if (!res.ok) {
    resultEl.textContent = 'Failed to create room.';
    return;
  }

  const room = await res.json();
  const url = `${window.location.origin}/room/${room.RoomId}`;
  resultEl.textContent = `Room created: ${url}`;
  nameInput.value = '';
  fetchRooms();
});

function escapeHtml(text) {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

fetchRooms();
loadLanguages();
