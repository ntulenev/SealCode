const connectionStatusEl = document.getElementById('connectionStatus');
const roomNameEl = document.getElementById('roomName');
const participantsList = document.getElementById('participantsList');
const languageSelect = document.getElementById('languageSelect');
const versionNumberEl = document.getElementById('versionNumber');
const editorHost = document.getElementById('monacoEditor');
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

let editor = null;
let model = null;
let editorReadyResolve;
const editorReady = new Promise((resolve) => { editorReadyResolve = resolve; });

const connection = new signalR.HubConnectionBuilder()
  .withUrl('/roomHub')
  .withAutomaticReconnect()
  .build();

function setStatus(status) {
  connectionStatusEl.textContent = status;
}

function truncateName(name) {
  if (!name) return '';
  if (name.length <= 10) return name;
  return `${name.slice(0, 9)}...`;
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
    if (typeof pos === 'number' && model) {
      const lc = getLineColFromOffset(pos);
      li.textContent = `${display} (${lc.line}:${lc.col})`;
    } else {
      li.textContent = display;
    }
    participantsList.appendChild(li);
  }
}

function getLineColFromOffset(offset) {
  if (!model) return { line: 1, col: 1 };
  const pos = model.getPositionAt(Math.max(0, offset));
  return { line: pos.lineNumber, col: pos.column };
}

function assignColor(name) {
  if (!caretColors[name]) {
    const idx = Object.keys(caretColors).length % caretPalette.length;
    caretColors[name] = caretPalette[idx];
  }
  return caretColors[name];
}

function ensureCaretStyles() {
  let styleEl = document.getElementById('remoteCaretStyles');
  if (!styleEl) {
    styleEl = document.createElement('style');
    styleEl.id = 'remoteCaretStyles';
    document.head.appendChild(styleEl);
  }
  return styleEl;
}

function renderRemoteCarets() {
  if (!editor || !model) return;
  const decorations = [];
  const styleEl = ensureCaretStyles();
  let css = '';

  for (const [name, offset] of Object.entries(cursorPositions)) {
    if (name === displayName) continue;
    if (typeof offset !== 'number') continue;
    const pos = model.getPositionAt(Math.max(0, offset));
    const line = pos.lineNumber;
    const col = Math.min(pos.column, model.getLineMaxColumn(line));
    const color = assignColor(name);
    const className = `remote-caret-${name.replace(/[^a-z0-9]/gi, '').toLowerCase()}`;
    css += `.${className} { border-left: 2px solid ${color}; margin-left: -1px; }\n`;
    decorations.push({
      range: new monaco.Range(line, col, line, col),
      options: {
        beforeContentClassName: className,
        stickiness: monaco.editor.TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges
      }
    });
  }

  styleEl.textContent = css;
  editor.remoteCaretDecorations = editor.deltaDecorations(editor.remoteCaretDecorations || [], decorations);
}

function sendCursor() {
  if (!displayName) return;
  if (connection.state !== signalR.HubConnectionState.Connected) return;
  if (!editor || !model) return;
  const pos = editor.getPosition();
  if (!pos) return;
  const offset = model.getOffsetAt(pos);
  cursorPositions[displayName] = offset;
  renderUsers(currentUsers);
  renderRemoteCarets();
  connection.invoke('UpdateCursor', roomId, offset).catch(() => {});
}

function scheduleCursorSend() {
  if (cursorTimer) return;
  cursorTimer = setTimeout(() => {
    cursorTimer = null;
    sendCursor();
  }, 100);
}

function setLanguage(language) {
  if (!model) return;
  monaco.editor.setModelLanguage(model, language);
}

function setText(text) {
  if (!model) return;
  isApplyingRemoteUpdate = true;
  model.setValue(text || '');
  isApplyingRemoteUpdate = false;
  renderRemoteCarets();
}

function initMonaco() {
  require.config({ paths: { vs: 'https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.47.0/min/vs' } });
  require(['vs/editor/editor.main'], () => {
    model = monaco.editor.createModel('', 'csharp');
    editor = monaco.editor.create(editorHost, {
      model,
      theme: 'vs-dark',
      minimap: { enabled: false },
      automaticLayout: true,
      fontFamily: 'IBM Plex Mono, monospace',
      fontSize: 14
    });

    editor.onDidChangeModelContent(() => {
      if (isApplyingRemoteUpdate) return;
      pendingText = model.getValue();
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

    editor.onDidChangeCursorSelection(() => {
      scheduleCursorSend();
    });

    editorReadyResolve();
  });
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
    setLanguage(language);
    setText(text || '');
    currentVersion = version;
    versionNumberEl.textContent = currentVersion;
    renderUsers(users);
    joinOverlay.classList.add('hidden');
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
  setText(newText || '');
  currentVersion = version;
  versionNumberEl.textContent = currentVersion;
  renderUsers(currentUsers);
});

connection.on('LanguageUpdated', (language, version) => {
  languageSelect.value = language;
  setLanguage(language);
  currentVersion = version;
  versionNumberEl.textContent = currentVersion;
});

connection.on('CursorUpdated', (name, position) => {
  if (!name) return;
  cursorPositions[name] = position;
  renderUsers(currentUsers);
  renderRemoteCarets();
});

connection.on('RoomKilled', (reason) => {
  if (editor) editor.updateOptions({ readOnly: true });
  joinError.textContent = reason || 'Room closed.';
  joinError.classList.remove('hidden');
  joinOverlay.classList.remove('hidden');
});

languageSelect.addEventListener('change', async () => {
  if (!displayName) return;
  setLanguage(languageSelect.value);
  await connection.invoke('SetLanguage', roomId, languageSelect.value);
});

joinBtn.addEventListener('click', async () => {
  displayName = displayNameInput.value.trim();
  if (!displayName) {
    joinError.textContent = 'Display name is required.';
    joinError.classList.remove('hidden');
    return;
  }
  await editorReady;
  if (connection.state === signalR.HubConnectionState.Disconnected) {
    await connection.start();
    setStatus('connected');
  }
  await joinRoom();
});

setStatus('disconnected');
initMonaco();
