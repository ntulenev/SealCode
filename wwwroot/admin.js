const roomsEl = document.getElementById('rooms');
const createBtn = document.getElementById('createRoom');
const nameInput = document.getElementById('roomName');
const langInput = document.getElementById('roomLanguage');
const resultEl = document.getElementById('createResult');

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
    row.append(name, lang, users, updated, actions);
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
