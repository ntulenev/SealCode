import * as Y from 'https://esm.sh/yjs@13.6.29';
import { MonacoBinding } from 'https://esm.sh/y-monaco@0.1.6';
import { Awareness } from 'https://esm.sh/y-protocols@1.0.7/awareness';
import { BoundedUndoManager } from './bounded-undo-manager.js';

const remoteYjsOrigin = Object.freeze({ source: 'signalr' });
const bootstrapYjsOrigin = Object.freeze({ source: 'bootstrap' });
const maxUndoDepth = 100;
const maxUndoEstimatedBytes = 8 * 1024 * 1024;
let collaborativeUndoEnabled = true;

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

const pathSegments = new URL(window.location.href).pathname.split('/').filter(Boolean);
const roomId = pathSegments[pathSegments.length - 1] ?? '';
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
const caretClassMap = new Map();
let caretClassCounter = 0;

let editor = null;
let model = null;
let editorReadyResolve;
const editorReady = new Promise((resolve) => { editorReadyResolve = resolve; });
let ydoc = null;
let ytext = null;
let awareness = null;
let monacoBinding = null;
let undoHistory = null;
let pendingYjsStateTimer = null;
let pendingYjsUpdates = [];
let yjsSendInFlight = false;
let checkpointTimer = null;
let checkpointInFlight = false;
let currentDocumentRevision = 0;
let highestDocumentRevisionSeen = 0;
const pendingDocumentRevisions = new Set();
let isSynchronizingYjs = false;
const bufferedYjsMessages = [];
let roomClosed = false;
let hasJoinedRoom = false;
let startConnectionPromise = null;
let isRejoiningRoom = false;
let rejoinRetryTimer = null;
let rejoinRetryAttempt = 0;
const rejoinRetryDelaysMs = [500, 1000, 2000, 5000, 10000];

const connection = new signalR.HubConnectionBuilder()
  .withUrl('/roomHub')
  .withAutomaticReconnect({
    nextRetryDelayInMilliseconds: () => (navigator.onLine ? 3000 : 2000)
  })
  .build();

connection.serverTimeoutInMilliseconds = 15000;
connection.keepAliveIntervalInMilliseconds = 5000;

window.__debugYjsSend = () => {
  if (!ydoc) return false;
  scheduleYjsUpdateSend(Y.encodeStateAsUpdate(ydoc));
  return true;
};

window.__debugUndoState = () => ({
  canUndo: undoHistory?.canUndo() ?? false,
  canRedo: undoHistory?.canRedo() ?? false,
  undoDepth: undoHistory?.undoDepth ?? 0,
  redoDepth: undoHistory?.redoDepth ?? 0,
  documentRevision: currentDocumentRevision,
  highestDocumentRevisionSeen
});

window.__debugReconnect = async () => {
  if (!hasJoinedRoom || roomClosed) return false;
  await connection.stop();
  const started = await ensureConnectionStarted();
  if (!started) return false;
  await rejoinRoomWithRetry();
  return connection.state === signalR.HubConnectionState.Connected
    && connectionStatusEl.textContent === 'connected';
};

function setStatus(status) {
  connectionStatusEl.textContent = status;
}

function clearRejoinRetryTimer() {
  if (!rejoinRetryTimer) return;
  clearTimeout(rejoinRetryTimer);
  rejoinRetryTimer = null;
}

async function ensureConnectionStarted() {
  if (connection.state !== signalR.HubConnectionState.Disconnected) {
    return true;
  }
  if (startConnectionPromise) {
    return startConnectionPromise;
  }
  startConnectionPromise = connection.start()
    .then(() => true)
    .catch((err) => {
      console.warn('Failed to start connection.', err);
      return false;
    })
    .finally(() => {
      startConnectionPromise = null;
    });
  return startConnectionPromise;
}

function getRejoinDelayMs() {
  const index = Math.min(rejoinRetryAttempt, rejoinRetryDelaysMs.length - 1);
  return rejoinRetryDelaysMs[index];
}

async function rejoinRoomWithRetry() {
  if (!hasJoinedRoom || !displayName || roomClosed) return;
  if (connection.state !== signalR.HubConnectionState.Connected) return;
  if (isRejoiningRoom) return;

  isRejoiningRoom = true;
  clearRejoinRetryTimer();
  setStatus('reconnecting');
  setEditorReadOnly(true);
  setLanguageReadOnly(true);

  let joined = false;
  try {
    joined = await joinRoom();
  } finally {
    isRejoiningRoom = false;
  }

  if (joined) {
    rejoinRetryAttempt = 0;
    setStatus('connected');
    setEditorReadOnly(false);
    setLanguageReadOnly(false);
    scheduleYjsUpdateSend();
    return;
  }

  if (roomClosed || connection.state !== signalR.HubConnectionState.Connected) {
    return;
  }

  const delayMs = getRejoinDelayMs();
  rejoinRetryAttempt += 1;
  rejoinRetryTimer = setTimeout(() => {
    rejoinRetryTimer = null;
    rejoinRoomWithRetry().catch((err) => {
      console.warn('Failed to rejoin room.', err);
    });
  }, delayMs);
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
    option.textContent = 'Languages unavailable';
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

function ensureLanguageOption(language) {
  if (!language) return;
  if ([...languageSelect.options].some((option) => option.value === language)) return;
  const option = document.createElement('option');
  option.value = language;
  option.textContent = language;
  languageSelect.appendChild(option);
  languageSelect.disabled = false;
}

async function loadLanguages(selected) {
  const preferred = selected ?? languageSelect?.value ?? '';
  try {
    const res = await fetch('/languages');
    if (!res.ok) {
      throw new Error('Failed to load languages');
    }
    const languages = await res.json();
    setLanguageOptions(languageSelect, languages, preferred);
  } catch {
    setLanguageOptions(languageSelect, [], preferred);
  }

  if (!languageSelect.disabled && languageSelect.value) {
    setLanguage(languageSelect.value);
  }
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
    if (activeEditors.get(user)) {
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
    if (!caretClassMap.has(name)) {
      caretClassCounter += 1;
      caretClassMap.set(name, `remote-caret-${caretClassCounter}`);
    }
    const className = caretClassMap.get(name);
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
  if (!name) return;
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

function hasNonEmptyEditorSelection() {
  if (!editor) return false;
  const selections = editor.getSelections();
  if (!Array.isArray(selections) || selections.length === 0) {
    return false;
  }
  return selections.some((selection) => !selection.isEmpty());
}

function isCopyShortcutKeyboardEvent(event) {
  if (!event) return false;
  const key = typeof event.key === 'string' ? event.key.toLowerCase() : '';
  return key === 'c' && (event.ctrlKey || event.metaKey) && !event.altKey;
}

function isUndoShortcutKeyboardEvent(event) {
  if (!event) return false;
  if (!(event.ctrlKey || event.metaKey) || event.altKey) return false;
  const key = typeof event.key === 'string' ? event.key.toLowerCase() : '';
  const code = typeof event.code === 'string' ? event.code.toLowerCase() : '';
  const isUndoKey = key === 'z' || code === 'keyz';
  return isUndoKey && !event.shiftKey;
}

function isRedoShortcutKeyboardEvent(event) {
  if (!event) return false;
  if (!(event.ctrlKey || event.metaKey) || event.altKey) return false;
  const key = typeof event.key === 'string' ? event.key.toLowerCase() : '';
  const code = typeof event.code === 'string' ? event.code.toLowerCase() : '';
  const isRedoKey = key === 'y' || code === 'keyy';
  const isShiftedUndoKey = (key === 'z' || code === 'keyz') && event.shiftKey;
  return isRedoKey || isShiftedUndoKey;
}

function isUndoRedoShortcutKeyboardEvent(event) {
  return isUndoShortcutKeyboardEvent(event) || isRedoShortcutKeyboardEvent(event);
}

function isPasteShortcutKeyboardEvent(event) {
  if (!event) return false;
  const key = typeof event.key === 'string' ? event.key.toLowerCase() : '';
  return key === 'v' && (event.ctrlKey || event.metaKey) && !event.altKey;
}

async function loadFeatures() {
  try {
    const response = await fetch('/features');
    if (!response.ok) throw new Error('Failed to load feature flags');
    const features = await response.json();
    collaborativeUndoEnabled = features.CollaborativeUndoEnabled
      ?? features.collaborativeUndoEnabled
      ?? true;
  } catch (err) {
    console.warn('Failed to load feature flags; collaborative undo remains enabled.', err);
    collaborativeUndoEnabled = true;
  }
}

function performCollaborativeUndo() {
  if (!undoHistory || connection.state !== signalR.HubConnectionState.Connected) return false;
  return undoHistory.undo() != null;
}

function performCollaborativeRedo() {
  if (!undoHistory || connection.state !== signalR.HubConnectionState.Connected) return false;
  return undoHistory.redo() != null;
}

function setLanguage(language) {
  if (!model) return;
  monaco.editor.setModelLanguage(model, language);
}

function setEditorReadOnly(isReadOnly) {
  if (!editor) return;
  editor.updateOptions({ readOnly: isReadOnly });
}

function setLanguageReadOnly(isReadOnly) {
  if (!languageSelect) return;
  languageSelect.disabled = isReadOnly;
}

function scheduleYjsUpdateSend(update) {
  if (update?.length > 0) pendingYjsUpdates.push(update);
  if (pendingYjsUpdates.length === 0 || pendingYjsStateTimer || yjsSendInFlight) return;
  pendingYjsStateTimer = setTimeout(async () => {
    pendingYjsStateTimer = null;
    if (connection.state !== signalR.HubConnectionState.Connected) return;

    const updates = pendingYjsUpdates.splice(0, pendingYjsUpdates.length);
    yjsSendInFlight = true;
    try {
      const merged = updates.length === 1 ? updates[0] : Y.mergeUpdates(updates);
      await connection.invoke('UpdateYjs', roomId, base64FromUint8(merged));
    } catch (err) {
      pendingYjsUpdates.unshift(...updates);
      console.error('Failed to send Yjs update.', err);
      joinError.textContent = 'Failed to sync changes. Try refreshing.';
      joinError.classList.remove('hidden');
    } finally {
      yjsSendInFlight = false;
      if (pendingYjsUpdates.length > 0) scheduleYjsUpdateSend();
      else scheduleYjsCheckpoint();
    }
  }, 60);
}

function normalizeYjsUpdates(updates) {
  if (!Array.isArray(updates)) return [];
  return updates
    .map((item) => ({
      revision: Number(item?.Revision ?? item?.revision),
      update: item?.Update ?? item?.update
    }))
    .filter((item) => Number.isSafeInteger(item.revision)
      && item.revision > 0
      && typeof item.update === 'string'
      && item.update.length > 0)
    .sort((left, right) => left.revision - right.revision);
}

function applyYjsUpdate(update, origin) {
  if (!ydoc || !update || update.length === 0) return;
  isApplyingRemoteUpdate = true;
  try {
    Y.applyUpdate(ydoc, update, origin);
  } finally {
    isApplyingRemoteUpdate = false;
  }
}

function applyYjsSyncPayload(text, stateBase64, updates) {
  const normalizedUpdates = normalizeYjsUpdates(updates);
  let appliedCrdtState = false;
  if (stateBase64) {
    const checkpoint = uint8FromBase64(stateBase64);
    if (checkpoint.length > 0) {
      applyYjsUpdate(checkpoint, bootstrapYjsOrigin);
      appliedCrdtState = true;
    }
  }
  for (const item of normalizedUpdates) {
    const update = uint8FromBase64(item.update);
    if (update.length === 0) continue;
    applyYjsUpdate(update, bootstrapYjsOrigin);
    appliedCrdtState = true;
  }
  if (!appliedCrdtState && text && ytext?.length === 0) {
    ydoc.transact(() => {
      ytext.insert(0, text);
    }, bootstrapYjsOrigin);
  }
  return normalizedUpdates;
}

function resetDocumentRevisionState(documentRevision, checkpointRevision, updates) {
  const latestRevision = Number(documentRevision);
  const baseRevision = Number(checkpointRevision);
  currentDocumentRevision = Number.isSafeInteger(latestRevision) && latestRevision >= 0
    ? latestRevision
    : 0;
  highestDocumentRevisionSeen = currentDocumentRevision;
  pendingDocumentRevisions.clear();

  let expectedRevision = Number.isSafeInteger(baseRevision) && baseRevision >= 0
    ? baseRevision
    : 0;
  for (const item of updates) {
    if (item.revision !== expectedRevision + 1) {
      console.warn('Yjs synchronization payload contains a revision gap.', {
        expected: expectedRevision + 1,
        actual: item.revision
      });
    }
    expectedRevision = Math.max(expectedRevision, item.revision);
  }
  if (expectedRevision !== currentDocumentRevision) {
    console.warn('Yjs synchronization payload does not end at the advertised revision.', {
      expectedRevision,
      currentDocumentRevision
    });
  }
  if (currentDocumentRevision > baseRevision) scheduleYjsCheckpoint();
}

function noteDocumentRevision(revision) {
  const parsedRevision = Number(revision);
  if (!Number.isSafeInteger(parsedRevision) || parsedRevision <= 0) return false;
  highestDocumentRevisionSeen = Math.max(highestDocumentRevisionSeen, parsedRevision);
  if (parsedRevision <= currentDocumentRevision) return false;

  pendingDocumentRevisions.add(parsedRevision);
  let advanced = false;
  while (pendingDocumentRevisions.delete(currentDocumentRevision + 1)) {
    currentDocumentRevision += 1;
    advanced = true;
  }
  if (advanced && currentDocumentRevision === highestDocumentRevisionSeen) {
    scheduleYjsCheckpoint();
  }
  return advanced;
}

function scheduleYjsCheckpoint() {
  if (!ydoc || !ytext || currentDocumentRevision <= 0) return;
  if (currentDocumentRevision !== highestDocumentRevisionSeen) return;
  if (pendingYjsUpdates.length > 0 || pendingYjsStateTimer || yjsSendInFlight) return;
  if (checkpointTimer || checkpointInFlight) return;
  checkpointTimer = setTimeout(sendYjsCheckpoint, 400);
}

async function sendYjsCheckpoint() {
  checkpointTimer = null;
  if (!ydoc || !ytext) return;
  if (connection.state !== signalR.HubConnectionState.Connected) return;
  if (currentDocumentRevision !== highestDocumentRevisionSeen) return;
  if (pendingYjsUpdates.length > 0 || pendingYjsStateTimer || yjsSendInFlight) return;

  const basedOnRevision = currentDocumentRevision;
  const stateBase64 = base64FromUint8(Y.encodeStateAsUpdate(ydoc));
  const snapshot = ytext.toString();
  let retry = false;
  checkpointInFlight = true;
  try {
    await connection.invoke('CheckpointYjs', roomId, stateBase64, snapshot, basedOnRevision);
  } catch (err) {
    retry = true;
    console.warn('Failed to checkpoint Yjs state.', err);
  } finally {
    checkpointInFlight = false;
    if ((retry || currentDocumentRevision > basedOnRevision)
        && currentDocumentRevision === highestDocumentRevisionSeen) {
      scheduleYjsCheckpoint();
    }
  }
}

function disposeYjs() {
  if (checkpointTimer) {
    clearTimeout(checkpointTimer);
    checkpointTimer = null;
  }
  checkpointInFlight = false;
  undoHistory?.destroy();
  undoHistory = null;
  monacoBinding?.destroy();
  monacoBinding = null;
  awareness?.destroy();
  awareness = null;
  ydoc?.destroy();
  ydoc = null;
  ytext = null;
}

function initializeYjs(text, stateBase64, updates, documentRevision, checkpointRevision) {
  if (!model) return;
  disposeYjs();
  ydoc = new Y.Doc();
  ytext = ydoc.getText('monaco');
  const normalizedUpdates = applyYjsSyncPayload(text, stateBase64, updates);
  awareness = new Awareness(ydoc);
  if (displayName) {
    awareness.setLocalStateField('user', { name: displayName });
  }
  isApplyingRemoteUpdate = true;
  try {
    monacoBinding = new MonacoBinding(ytext, model, new Set([editor]), awareness);
  } finally {
    isApplyingRemoteUpdate = false;
  }
  if (collaborativeUndoEnabled) {
    undoHistory = new BoundedUndoManager(Y, ytext, {
      trackedOrigin: monacoBinding,
      maxDepth: maxUndoDepth,
      maxEstimatedBytes: maxUndoEstimatedBytes,
      captureTimeout: 500
    });
  }
  ydoc.on('update', (update, origin) => {
    if (origin === remoteYjsOrigin || origin === bootstrapYjsOrigin) return;
    if (origin === monacoBinding && undoHistory?.recordLocalUpdateSize(update.length)) {
      console.warn('Collaborative undo history was cleared after reaching its memory limit.');
    }
    scheduleYjsUpdateSend(update);
  });
  resetDocumentRevisionState(documentRevision, checkpointRevision, normalizedUpdates);
  renderRemoteCarets();
}

function mergeYjsSyncState(stateBase64, updates, documentRevision, checkpointRevision) {
  if (!ydoc || !ytext) return false;
  const normalizedUpdates = applyYjsSyncPayload('', stateBase64, updates);
  resetDocumentRevisionState(documentRevision, checkpointRevision, normalizedUpdates);
  if (model && model.getValue() !== ytext.toString()) {
    console.error('Monaco and Yjs diverged after reconnect synchronization.');
    return false;
  }
  renderRemoteCarets();
  return true;
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
    model = monaco.editor.createModel('', languageSelect?.value || 'plaintext');
    model.undo = async () => { performCollaborativeUndo(); };
    model.redo = async () => { performCollaborativeRedo(); };
    editor = monaco.editor.create(editorHost, {
      model,
      theme: 'vs-dark',
      minimap: { enabled: false },
      automaticLayout: true,
      fixedOverflowWidgets: true,
      emptySelectionClipboard: false,
      fontFamily: 'IBM Plex Mono, monospace',
      fontSize: 14,
      readOnly: true
    });

    editor.onDidChangeModelContent((event) => {
      if (isApplyingRemoteUpdate) return;
      hideRemoteCaretsTemporarily();
      applyRemoteCursorAdjustment(event.changes);
      markActiveEditor(displayName);
      scheduleCursorSend();
    });

    editor.onKeyDown((keyboardEvent) => {
      const browserEvent = keyboardEvent.browserEvent;
      if (isPasteShortcutKeyboardEvent(browserEvent)) {
        undoHistory?.stopCapturing();
        setTimeout(() => undoHistory?.stopCapturing(), 0);
      }
      if (isUndoRedoShortcutKeyboardEvent(browserEvent)) {
        browserEvent.preventDefault();
        browserEvent.stopImmediatePropagation();
        keyboardEvent.preventDefault();
        keyboardEvent.stopPropagation();
        if (isUndoShortcutKeyboardEvent(browserEvent)) {
          performCollaborativeUndo();
        } else {
          performCollaborativeRedo();
        }
        return;
      }
      if (!isCopyShortcutKeyboardEvent(browserEvent)) return;
      if (hasNonEmptyEditorSelection()) return;
      browserEvent.preventDefault();
      browserEvent.stopImmediatePropagation();
      keyboardEvent.preventDefault();
      keyboardEvent.stopPropagation();
    });

    editor.onDidPaste?.(() => undoHistory?.stopCapturing());

    editor.onDidChangeCursorSelection((event) => {
      if (event.reason === monaco.editor.CursorChangeReason.Explicit) {
        undoHistory?.stopCapturing();
      }
      if (!displayName || connection.state !== signalR.HubConnectionState.Connected) {
        return;
      }
      scheduleCursorSend();
      const sel = editor.getSelection();
      if (!sel) return;
      const isMultiLine = sel.startLineNumber !== sel.endLineNumber;
      connection.invoke('UpdateSelection', roomId, isMultiLine).catch((err) => {
        console.warn('Failed to send selection update.', err);
      });
    });

    editorReadyResolve();
  });
}

async function joinRoom() {
  joinError.classList.add('hidden');
  isSynchronizingYjs = true;
  try {
    const result = await connection.invoke('JoinRoom', roomId, displayName);
    const name = result.Name ?? result.name;
    const language = result.Language ?? result.language;
    const text = result.Text ?? result.text;
    const version = result.Version ?? result.version;
    const users = result.Users ?? result.users;
    const createdBy = result.CreatedBy ?? result.createdBy;
    const yjsState = result.YjsState ?? result.yjsState;
    const yjsUpdates = result.YjsUpdates ?? result.yjsUpdates ?? [];
    const documentRevision = result.DocumentRevision ?? result.documentRevision ?? 0;
    const checkpointRevision = result.CheckpointRevision ?? result.checkpointRevision ?? 0;
    roomNameEl.textContent = name;
    if (createdByEl) {
      createdByEl.textContent = createdBy || 'unknown';
    }
    ensureLanguageOption(language);
    languageSelect.value = language;
    setLanguage(language);
    if (ydoc && hasJoinedRoom) {
      if (!mergeYjsSyncState(yjsState, yjsUpdates, documentRevision, checkpointRevision)) {
        throw new Error('Failed to merge the room document after reconnect.');
      }
    } else {
      initializeYjs(text || '', yjsState, yjsUpdates, documentRevision, checkpointRevision);
    }
    currentVersion = Math.max(currentVersion, version);
    versionNumberEl.textContent = currentVersion;
    renderUsers(users);
    hasJoinedRoom = true;
    rejoinRetryAttempt = 0;
    clearRejoinRetryTimer();
    joinOverlay.classList.add('hidden');
    sendCursor();
    return true;
  } catch (err) {
    joinError.textContent = err?.message || 'Failed to join room.';
    joinError.classList.remove('hidden');
    return false;
  } finally {
    isSynchronizingYjs = false;
    flushBufferedYjsMessages();
  }
}

connection.onreconnected(async () => {
  if (hasJoinedRoom && displayName && !roomClosed) {
    await rejoinRoomWithRetry();
    return;
  }
  setStatus('connected');
});
connection.onreconnecting(() => {
  clearRejoinRetryTimer();
  isRejoiningRoom = false;
  setStatus('reconnecting');
  setEditorReadOnly(true);
  setLanguageReadOnly(true);
});
connection.onclose(() => {
  clearRejoinRetryTimer();
  isRejoiningRoom = false;
  setStatus('disconnected');
  setEditorReadOnly(true);
  setLanguageReadOnly(true);
});

window.addEventListener('offline', () => {
  if (!hasJoinedRoom || roomClosed) return;
  setStatus('reconnecting');
  setEditorReadOnly(true);
  setLanguageReadOnly(true);
});

window.addEventListener('online', () => {
  if (!hasJoinedRoom || roomClosed) return;
  if (connection.state === signalR.HubConnectionState.Connected) {
    setStatus('connected');
    setEditorReadOnly(false);
    setLanguageReadOnly(false);
    return;
  }
  setStatus('reconnecting');
  ensureConnectionStarted()
    .then((started) => {
      if (!started) return;
      return rejoinRoomWithRetry();
    })
    .catch((err) => {
      console.warn('Failed to recover after going online.', err);
    });
});

connection.on('UserJoined', (name, users) => {
  cursorPositions[name] ??= 0;
  renderUsers(users);
  renderRemoteCarets();
});

connection.on('UserLeft', (name, users) => {
  delete cursorPositions[name];
  caretClassMap.delete(name);
  delete caretColors[name];
  activeEditors.delete(name);
  selectingUsers.delete(name);
  copyingUsers.delete(name);
  renderUsers(users);
  renderRemoteCarets();
});

function handleYjsUpdated(updateBase64, documentRevision, roomVersion, author) {
  currentVersion = Math.max(currentVersion, Number(roomVersion) || 0);
  versionNumberEl.textContent = currentVersion;
  renderUsers(currentUsers);
  if (!ydoc) return;
  if (author && author !== displayName) markActiveEditor(author);
  const update = uint8FromBase64(updateBase64);
  if (update.length === 0) return;
  applyYjsUpdate(update, remoteYjsOrigin);
  noteDocumentRevision(documentRevision);
  if (model && ytext && model.getValue() !== ytext.toString()) {
    console.error('Monaco and Yjs diverged after applying a room update.', { documentRevision });
  }
}

function flushBufferedYjsMessages() {
  if (isSynchronizingYjs || bufferedYjsMessages.length === 0) return;
  const messages = bufferedYjsMessages.splice(0, bufferedYjsMessages.length)
    .sort((left, right) => Number(left.documentRevision) - Number(right.documentRevision));
  for (const message of messages) {
    handleYjsUpdated(
      message.updateBase64,
      message.documentRevision,
      message.roomVersion,
      message.author);
  }
}

connection.on('YjsUpdated', (updateBase64, documentRevision, roomVersion, author) => {
  if (isSynchronizingYjs) {
    bufferedYjsMessages.push({ updateBase64, documentRevision, roomVersion, author });
    return;
  }
  handleYjsUpdated(updateBase64, documentRevision, roomVersion, author);
});

connection.on('LanguageUpdated', (language, version) => {
  languageSelect.value = language;
  setLanguage(language);
  currentVersion = Math.max(currentVersion, Number(version) || 0);
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
  roomClosed = true;
  hasJoinedRoom = false;
  clearRejoinRetryTimer();
  setEditorReadOnly(true);
  disposeYjs();
  joinError.textContent = reason || 'Room closed.';
  joinError.classList.remove('hidden');
  joinOverlay.classList.remove('hidden');
});

languageSelect.addEventListener('change', async () => {
  if (!displayName) return;
  if (!languageSelect.value) return;
  setLanguage(languageSelect.value);
  if (connection.state !== signalR.HubConnectionState.Connected) return;
  try {
    await connection.invoke('SetLanguage', roomId, languageSelect.value);
  } catch (err) {
    console.warn('Failed to update language.', err);
  }
});

document.addEventListener('copy', (event) => {
  if (!editor) return;
  const target = event.target;
  const isEditorCopy = editor.hasTextFocus() || (target instanceof Node && editorHost.contains(target));
  if (!isEditorCopy) return;
  if (!hasNonEmptyEditorSelection()) {
    event.preventDefault();
    event.stopImmediatePropagation();
    return;
  }
  if (!displayName) return;
  if (connection.state !== signalR.HubConnectionState.Connected) return;
  markCopying(displayName);
  connection.invoke('UpdateCopy', roomId).catch(() => {});
}, true);

joinBtn.addEventListener('click', async () => {
  displayName = displayNameInput.value.trim();
  if (!displayName) {
    joinError.textContent = 'Display name is required.';
    joinError.classList.remove('hidden');
    return;
  }
  await Promise.all([editorReady, featuresReady]);
  if (connection.state === signalR.HubConnectionState.Disconnected) {
    const started = await ensureConnectionStarted();
    if (!started) {
      setStatus('disconnected');
      joinError.textContent = 'Failed to connect. Try again.';
      joinError.classList.remove('hidden');
      return;
    }
  }
  setStatus('connected');
  setLanguageReadOnly(true);
  const joined = await joinRoom();
  if (joined) {
    setEditorReadOnly(false);
    setLanguageReadOnly(false);
  } else {
    setEditorReadOnly(true);
    setLanguageReadOnly(true);
  }
});

setStatus('disconnected');
const featuresReady = loadFeatures();
loadLanguages().finally(() => {
  setLanguageReadOnly(true);
});
initMonaco();
