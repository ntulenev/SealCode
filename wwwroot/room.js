const connectionStatusEl = document.getElementById('connectionStatus');
const roomNameEl = document.getElementById('roomName');
const participantsList = document.getElementById('participantsList');
const languageSelect = document.getElementById('languageSelect');
const versionNumberEl = document.getElementById('versionNumber');
const editor = document.getElementById('editor');
const codePreview = document.getElementById('codePreview');
const lineNumbersEl = document.getElementById('lineNumbers');
const remoteCaretsEl = document.getElementById('remoteCarets');
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
let cursorTimer = null;
let currentUsers = [];
const cursorPositions = {};
const caretColors = {};
const caretPalette = ['#fbbf24', '#22d3ee', '#34d399', '#f472b6', '#a78bfa', '#fb7185'];
const gutterWidth = 56;
let metricsCache = null;

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
  currentUsers = users;
  participantsList.innerHTML = '';
  for (const user of users) {
    const li = document.createElement('li');
    const pos = cursorPositions[user];
    const display = truncateName(user);
    li.title = user;
    if (typeof pos === 'number') {
      const lc = getLineCol(editor.value, pos);
      li.textContent = `${display} (${lc.line}:${lc.col})`;
    } else {
      li.textContent = display;
    }
    participantsList.appendChild(li);
  }
}

function truncateName(name) {
  if (!name) return '';
  if (name.length <= 10) return name;
  return `${name.slice(0, 9)}...`;
}

function getLineCol(text, index) {
  const safeIndex = Math.max(0, Math.min(index, text.length));
  const before = text.slice(0, safeIndex);
  const lastBreak = before.lastIndexOf('\n');
  const line = before.split('\n').length;
  const col = lastBreak === -1 ? before.length + 1 : before.length - lastBreak;
  return { line, col };
}

function getMetrics() {
  const styles = getComputedStyle(editor);
  const font = `${styles.fontSize} ${styles.fontFamily}`;
  const fontSize = parseFloat(styles.fontSize) || 14;
  let lineHeight = parseFloat(styles.lineHeight);
  if (Number.isNaN(lineHeight)) {
    lineHeight = fontSize * 1.5;
  }
  const paddingLeft = parseFloat(styles.paddingLeft) || 0;
  const paddingTop = parseFloat(styles.paddingTop) || 0;

  if (metricsCache && metricsCache.font === font && metricsCache.lineHeight === lineHeight && metricsCache.paddingLeft === paddingLeft && metricsCache.paddingTop === paddingTop) {
    return metricsCache;
  }

  const measure = document.createElement('span');
  measure.style.position = 'fixed';
  measure.style.visibility = 'hidden';
  measure.style.whiteSpace = 'pre';
  measure.style.fontSize = styles.fontSize;
  measure.style.fontFamily = styles.fontFamily;
  measure.textContent = 'M';
  document.body.appendChild(measure);
  const charWidth = measure.getBoundingClientRect().width;
  document.body.removeChild(measure);

  metricsCache = { font, lineHeight, paddingLeft, paddingTop, charWidth };
  return metricsCache;
}

function assignColor(name) {
  if (!caretColors[name]) {
    const idx = Object.keys(caretColors).length % caretPalette.length;
    caretColors[name] = caretPalette[idx];
  }
  return caretColors[name];
}

function renderRemoteCarets() {
  if (!remoteCaretsEl) return;
  remoteCaretsEl.innerHTML = '';
  const { lineHeight, paddingLeft, paddingTop, charWidth } = getMetrics();
  const scrollTop = editor.scrollTop;
  const scrollLeft = editor.scrollLeft;

  for (const [name, pos] of Object.entries(cursorPositions)) {
    if (name === displayName) continue;
    if (typeof pos !== 'number') continue;
    const lc = getLineCol(editor.value, pos);
    const x = (paddingLeft - gutterWidth) + (lc.col - 1) * charWidth - scrollLeft;
    const y = paddingTop + (lc.line - 1) * lineHeight - scrollTop;
    if (y < -lineHeight || y > editor.clientHeight + lineHeight) continue;

    const caret = document.createElement('div');
    caret.className = 'remote-caret';
    const color = assignColor(name);
    caret.style.left = `${Math.max(0, x)}px`;
    caret.style.top = `${y}px`;
    caret.style.height = `${lineHeight}px`;
    caret.style.background = color;
    caret.style.boxShadow = `0 0 0 2px ${color}33`;

    const label = document.createElement('div');
    label.className = 'remote-caret-label';
    label.style.background = color;
    label.style.color = '#0b1220';
    label.textContent = name;

    caret.appendChild(label);
    remoteCaretsEl.appendChild(caret);
  }
}

function sendCursor() {
  if (!displayName) return;
  if (connection.state !== signalR.HubConnectionState.Connected) return;
  const pos = editor.selectionStart ?? 0;
  cursorPositions[displayName] = pos;
  renderUsers(currentUsers);
  renderRemoteCarets();
  connection.invoke('UpdateCursor', roomId, pos).catch(() => {});
}

function scheduleCursorSend() {
  if (cursorTimer) return;
  cursorTimer = setTimeout(() => {
    cursorTimer = null;
    sendCursor();
  }, 100);
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
  if (lineNumbersEl) {
    lineNumbersEl.scrollTop = editor.scrollTop;
  }
  renderRemoteCarets();
}

function updateLineNumbers(text) {
  if (!lineNumbersEl) return;
  const lines = (text || '').split('\n').length;
  let output = '';
  for (let i = 1; i <= lines; i++) {
    output += i === lines ? `${i}` : `${i}\n`;
  }
  lineNumbersEl.textContent = output;
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
    roomNameEl.textContent = `SealCode 🦭 — ${name}`;
    languageSelect.value = language;
    setPreviewLanguage(language);
    isApplyingRemoteUpdate = true;
    editor.value = text || '';
    isApplyingRemoteUpdate = false;
    updatePreview(editor.value);
    updateLineNumbers(editor.value);
    syncScroll();
    currentVersion = version;
    versionNumberEl.textContent = currentVersion;
    renderUsers(users);
    joinOverlay.classList.add('hidden');
    renderRemoteCarets();
    sendCursor();
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
  cursorPositions[name] ??= 0;
  renderUsers(users);
  renderRemoteCarets();
});

connection.on('UserLeft', (name, users) => {
  delete cursorPositions[name];
  renderUsers(users);
  renderRemoteCarets();
});

connection.on('TextUpdated', (newText, version, author) => {
  isApplyingRemoteUpdate = true;
  editor.value = newText || '';
  isApplyingRemoteUpdate = false;
  updatePreview(editor.value);
  updateLineNumbers(editor.value);
  syncScroll();
  currentVersion = version;
  versionNumberEl.textContent = currentVersion;
  renderUsers(currentUsers);
  renderRemoteCarets();
});

connection.on('LanguageUpdated', (language, version) => {
  languageSelect.value = language;
  setPreviewLanguage(language);
  updatePreview(editor.value);
  updateLineNumbers(editor.value);
  syncScroll();
  currentVersion = version;
  versionNumberEl.textContent = currentVersion;
  renderRemoteCarets();
});

connection.on('CursorUpdated', (name, position) => {
  if (!name) return;
  cursorPositions[name] = position;
  renderUsers(currentUsers);
  renderRemoteCarets();
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
  updateLineNumbers(pendingText);
  syncScroll();
  scheduleCursorSend();
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

editor.addEventListener('click', scheduleCursorSend);
editor.addEventListener('keyup', scheduleCursorSend);
editor.addEventListener('select', scheduleCursorSend);

setStatus('disconnected');
