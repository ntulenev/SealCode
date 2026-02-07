import * as Y from 'https://esm.sh/yjs@13.6.29';
import { MonacoBinding } from 'https://esm.sh/y-monaco@0.1.6';
import { Awareness } from 'https://esm.sh/y-protocols@1.0.7/awareness';

const connectionStatusEl = document.getElementById('connectionStatus');
const roomNameEl = document.getElementById('roomName');
const createdByEl = document.getElementById('createdBy');
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
let cursorTimer = null;
let currentUsers = [];
const cursorPositions = {};
const caretColors = {};
const caretPalette = ['#fbbf24', '#22d3ee', '#34d399', '#f472b6', '#a78bfa', '#fb7185'];
let hideRemoteCarets = false;
let hideRemoteTimer = null;
const activeEditors = new Map();
const selectingUsers = new Map();
const copyingUsers = new Map();

let editor = null;
let model = null;
let editorReadyResolve;
const editorReady = new Promise((resolve) => { editorReadyResolve = resolve; });
let ydoc = null;
let ytext = null;
let awareness = null;
let monacoBinding = null;
let pendingYjsStateTimer = null;
let pendingYjsUpdates = [];
let isBootstrapping = false;

const connection = new signalR.HubConnectionBuilder()
  .withUrl('/roomHub')
  .withAutomaticReconnect()
  .build();

window.__debugYjsSend = () => {
  if (!ydoc) return false;
  scheduleYjsUpdateSend(Y.encodeStateAsUpdate(ydoc));
  return true;
};

function setStatus(status) {
  connectionStatusEl.textContent = status;
}

function base64FromUint8(data) {
  if (!data || data.length === 0) return '';
  let binary = '';
  const chunkSize = 0x8000;
  for (let i = 0; i < data.length; i += chunkSize) {
    const chunk = data.subarray(i, i + chunkSize);
    binary += String.fromCharCode(...chunk);
  }
  return btoa(binary);
}

function uint8FromBase64(base64) {
  if (!base64) return new Uint8Array();
  const binary = atob(base64);
  const len = binary.length;
  const bytes = new Uint8Array(len);
  for (let i = 0; i < len; i += 1) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
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
    if (activeEditors.get(user) && user !== displayName) {
      li.classList.add('active');
    }
    if (selectingUsers.get(user)) {
      li.classList.add('copied');
    }
    if (copyingUsers.get(user)) {
      li.classList.add('copying');
    }
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
  if (hideRemoteCarets) {
    editor.remoteCaretDecorations = editor.deltaDecorations(editor.remoteCaretDecorations || [], []);
    return;
  }
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

function hideRemoteCaretsTemporarily() {
  hideRemoteCarets = true;
  renderRemoteCarets();
  if (hideRemoteTimer) clearTimeout(hideRemoteTimer);
  hideRemoteTimer = setTimeout(() => {
    hideRemoteCarets = false;
    renderRemoteCarets();
  }, 800);
}

function markActiveEditor(name) {
  if (!name || name === displayName) return;
  activeEditors.set(name, Date.now());
  renderUsers(currentUsers);
  setTimeout(() => {
    const last = activeEditors.get(name);
    if (last && Date.now() - last > 1200) {
      activeEditors.delete(name);
      renderUsers(currentUsers);
    }
  }, 1300);
}

function markSelecting(name, isSelecting) {
  if (!name) return;
  if (isSelecting) {
    selectingUsers.set(name, Date.now());
  } else {
    selectingUsers.delete(name);
  }
  renderUsers(currentUsers);
}

function markCopying(name) {
  if (!name) return;
  copyingUsers.set(name, Date.now());
  renderUsers(currentUsers);
  setTimeout(() => {
    const last = copyingUsers.get(name);
    if (last && Date.now() - last > 1200) {
      copyingUsers.delete(name);
      renderUsers(currentUsers);
    }
  }, 1300);
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

function scheduleYjsUpdateSend(update) {
  if (!update) return;
  pendingYjsUpdates.push(update);
  if (pendingYjsStateTimer) return;
  pendingYjsStateTimer = setTimeout(async () => {
    if (connection.state !== signalR.HubConnectionState.Connected) {
      pendingYjsStateTimer = null;
      return;
    }
    const updates = pendingYjsUpdates;
    pendingYjsUpdates = [];
    pendingYjsStateTimer = null;
    if (!ydoc || !ytext) return;
    try {
      const merged = updates.length === 1
        ? updates[0]
        : (Y.mergeUpdates ? Y.mergeUpdates(updates) : Y.encodeStateAsUpdate(ydoc));
      const updateBase64 = base64FromUint8(merged);
      const stateBase64 = base64FromUint8(Y.encodeStateAsUpdate(ydoc));
      const snapshot = ytext.toString();
      await connection.invoke('UpdateYjs', roomId, updateBase64, stateBase64, snapshot);
    } catch (err) {
      console.error('Failed to send Yjs update.', err);
      joinError.textContent = 'Failed to sync changes. Try refreshing.';
      joinError.classList.remove('hidden');
    }
  }, 60);
}



function initializeYjs(text, stateBase64) {
  if (!model) return;
  if (monacoBinding) {
    monacoBinding.destroy();
    monacoBinding = null;
  }
  if (ydoc) {
    ydoc.destroy();
  }
  ydoc = new Y.Doc();
  ytext = ydoc.getText('monaco');
  awareness = new Awareness(ydoc);
  if (displayName) {
    awareness.setLocalStateField('user', { name: displayName });
  }
  monacoBinding = new MonacoBinding(ytext, model, new Set([editor]), awareness);

  ydoc.on('update', (update, origin) => {
    if (isBootstrapping) return;
    if (origin === 'remote') return;
    scheduleYjsUpdateSend(update);
  });

  isBootstrapping = true;
  if (stateBase64) {
    const update = uint8FromBase64(stateBase64);
    if (update.length > 0) {
      isApplyingRemoteUpdate = true;
      Y.applyUpdate(ydoc, update, 'remote');
      isApplyingRemoteUpdate = false;
    }
  } else if (text) {
    ydoc.transact(() => {
      ytext.insert(0, text);
    }, 'init');
  }
  setText(ytext.toString());
  isBootstrapping = false;
}

function applyRemoteCursorAdjustment(changes) {
  if (!changes || !changes.length || !model) return;
  const sorted = [...changes].sort((a, b) => (a.rangeOffset ?? 0) - (b.rangeOffset ?? 0));
  for (const change of sorted) {
    const start = typeof change.rangeOffset === 'number'
      ? change.rangeOffset
      : model.getOffsetAt({ lineNumber: change.range.startLineNumber, column: change.range.startColumn });
    const length = typeof change.rangeLength === 'number'
      ? change.rangeLength
      : model.getOffsetAt({ lineNumber: change.range.endLineNumber, column: change.range.endColumn }) - start;
    const end = start + length;
    const delta = (change.text || '').length - length;

    for (const [name, offset] of Object.entries(cursorPositions)) {
      if (name === displayName || typeof offset !== 'number') continue;
      if (offset <= start) continue;
      if (offset >= end) {
        cursorPositions[name] = offset + delta;
      } else {
        cursorPositions[name] = start + Math.max(0, (change.text || '').length);
      }
    }
  }
}

function initMonaco() {
  const amdRequire = window.require;
  amdRequire.config({ paths: { vs: 'https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.47.0/min/vs' } });
  amdRequire(['vs/editor/editor.main'], () => {
    model = monaco.editor.createModel('', 'csharp');
    editor = monaco.editor.create(editorHost, {
      model,
      theme: 'vs-dark',
      minimap: { enabled: false },
      automaticLayout: true,
      fontFamily: 'IBM Plex Mono, monospace',
      fontSize: 14
    });

    editor.onDidChangeModelContent((event) => {
      if (isApplyingRemoteUpdate) return;
      hideRemoteCaretsTemporarily();
      applyRemoteCursorAdjustment(event.changes);
      scheduleCursorSend();
    });

    editor.onDidChangeCursorSelection(() => {
      scheduleCursorSend();
      const sel = editor.getSelection();
      if (!sel) return;
      const isMultiLine = sel.startLineNumber !== sel.endLineNumber;
      connection.invoke('UpdateSelection', roomId, isMultiLine).catch(() => {});
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
    const createdBy = result.CreatedBy ?? result.createdBy;
    const yjsState = result.YjsState ?? result.yjsState;
    roomNameEl.textContent = name;
    if (createdByEl) {
      createdByEl.textContent = createdBy || 'unknown';
    }
    languageSelect.value = language;
    setLanguage(language);
    initializeYjs(text || '', yjsState);
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
  if (ydoc) {
    scheduleYjsUpdateSend(Y.encodeStateAsUpdate(ydoc));
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

connection.on('YjsUpdated', (updateBase64, version, author) => {
  currentVersion = version;
  versionNumberEl.textContent = currentVersion;
  renderUsers(currentUsers);
  if (!ydoc) return;
  if (author === displayName) return;
  markActiveEditor(author);
  const update = uint8FromBase64(updateBase64);
  if (update.length === 0) return;
  isApplyingRemoteUpdate = true;
  Y.applyUpdate(ydoc, update, 'remote');
  isApplyingRemoteUpdate = false;
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

connection.on('UserSelection', (name, isMultiLine) => {
  markSelecting(name, isMultiLine);
});

connection.on('UserCopy', (name) => {
  markCopying(name);
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

document.addEventListener('copy', () => {
  if (!displayName) return;
  if (connection.state !== signalR.HubConnectionState.Connected) return;
  markCopying(displayName);
  connection.invoke('UpdateCopy', roomId).catch(() => {});
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
