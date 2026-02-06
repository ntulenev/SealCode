const connectionStatusEl = document.getElementById('connectionStatus');
const roomNameEl = document.getElementById('roomName');
const participantsList = document.getElementById('participantsList');
const languageSelect = document.getElementById('languageSelect');
const versionNumberEl = document.getElementById('versionNumber');
const editor = document.getElementById('editor');
const codePreview = document.getElementById('codePreview');
const joinOverlay = document.getElementById('joinOverlay');
const joinBtn = document.getElementById('joinBtn');
const displayNameInput = document.getElementById('displayNameInput');
const joinError = document.getElementById('joinError');

const roomId = window.location.pathname.split('/').pop();
let displayName = '';
let currentVersion = 0;
let isApplyingRemoteUpdate = false;
let pendingText = null;
let pendingTimer = null;

const connection = new signalR.HubConnectionBuilder()
  .withUrl('/roomHub')
  .withAutomaticReconnect()
  .build();

function setStatus(status) {
  connectionStatusEl.textContent = status;
}

function renderUsers(users) {
  if (!Array.isArray(users)) {
    participantsList.innerHTML = '';
    return;
  }
  participantsList.innerHTML = '';
  for (const user of users) {
    const li = document.createElement('li');
    li.textContent = user;
    participantsList.appendChild(li);
  }
}

function updatePreview(text) {
  if (!codePreview || !window.hljs) return;
  codePreview.classList.add('hljs');
  codePreview.removeAttribute('data-highlighted');
  codePreview.textContent = text || '';
  requestAnimationFrame(() => hljs.highlightElement(codePreview));
}

function setPreviewLanguage(language) {
  if (!codePreview) return;
  codePreview.className = '';
  codePreview.classList.add(`language-${language}`);
  codePreview.classList.add('hljs');
}

function syncScroll() {
  if (!codePreview) return;
  codePreview.parentElement.scrollTop = editor.scrollTop;
  codePreview.parentElement.scrollLeft = editor.scrollLeft;
}

async function joinRoom() {
  joinError.classList.add('hidden');
  try {
    const result = await connection.invoke('JoinRoom', roomId, displayName);
    const name = result.Name ?? result.name;
    const language = result.Language ?? result.language;
    const text = result.Text ?? result.text;
    const version = result.Version ?? result.version;
    const users = result.Users ?? result.users;
    roomNameEl.textContent = name;
    languageSelect.value = language;
    setPreviewLanguage(language);
    isApplyingRemoteUpdate = true;
    editor.value = text || '';
    isApplyingRemoteUpdate = false;
    updatePreview(editor.value);
    syncScroll();
    currentVersion = version;
    versionNumberEl.textContent = currentVersion;
    renderUsers(users);
    joinOverlay.classList.add('hidden');
  } catch (err) {
    joinError.textContent = err?.message || 'Failed to join room.';
    joinError.classList.remove('hidden');
  }
}

connection.onreconnecting(() => setStatus('reconnecting'));
connection.onreconnected(async () => {
  setStatus('connected');
  if (displayName) {
    await joinRoom();
  }
});
connection.onclose(() => setStatus('disconnected'));

connection.on('UserJoined', (name, users) => {
  renderUsers(users);
});

connection.on('UserLeft', (name, users) => {
  renderUsers(users);
});

connection.on('TextUpdated', (newText, version, author) => {
  isApplyingRemoteUpdate = true;
  editor.value = newText || '';
  isApplyingRemoteUpdate = false;
  updatePreview(editor.value);
  syncScroll();
  currentVersion = version;
  versionNumberEl.textContent = currentVersion;
});

connection.on('LanguageUpdated', (language, version) => {
  languageSelect.value = language;
  setPreviewLanguage(language);
  updatePreview(editor.value);
  syncScroll();
  currentVersion = version;
  versionNumberEl.textContent = currentVersion;
});

connection.on('RoomKilled', (reason) => {
  editor.disabled = true;
  joinError.textContent = reason || 'Room closed.';
  joinError.classList.remove('hidden');
  joinOverlay.classList.remove('hidden');
});

languageSelect.addEventListener('change', async () => {
  if (!displayName) return;
  setPreviewLanguage(languageSelect.value);
  updatePreview(editor.value);
  syncScroll();
  await connection.invoke('SetLanguage', roomId, languageSelect.value);
});

editor.addEventListener('input', () => {
  if (isApplyingRemoteUpdate) return;
  pendingText = editor.value;
  updatePreview(pendingText);
  syncScroll();
  if (pendingTimer) return;
  pendingTimer = setTimeout(async () => {
    try {
      await connection.invoke('UpdateText', roomId, pendingText, currentVersion);
      currentVersion += 1;
      versionNumberEl.textContent = currentVersion;
    } catch {
      // ignore errors; reconnect handler will resync
    } finally {
      pendingTimer = null;
      pendingText = null;
    }
  }, 200);
});

joinBtn.addEventListener('click', async () => {
  displayName = displayNameInput.value.trim();
  if (!displayName) {
    joinError.textContent = 'Display name is required.';
    joinError.classList.remove('hidden');
    return;
  }
  if (connection.state === signalR.HubConnectionState.Disconnected) {
    await connection.start();
    setStatus('connected');
  }
  await joinRoom();
});

editor.addEventListener('scroll', () => {
  syncScroll();
});

setStatus('disconnected');
