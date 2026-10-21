import { PresenceView } from './presence-ui.js';
import { base64FromUint8, uint8FromBase64 } from './encoding.js';
import {
  isCopyShortcut,
  isUndoShortcut,
  isRedoShortcut,
  isUndoRedoShortcut,
  isPasteShortcut
} from './keyboard-shortcuts.js';
import { HubMethods, HubEvents } from './hub-contract.js';

import { HubErrorCode, classifyHubError, createHubError } from './hub-errors.js';

import { createMonacoAdapter } from './monaco-adapter.js';
import { createRoomConnection } from './room-connection.js';
import { RevisionedRoomView } from './room-presence.js';
globalThis.MonacoEnvironment = {
  getWorker(_module, label) {
    const name =
      {
        json: 'json',
        css: 'css',
        scss: 'css',
        less: 'css',
        html: 'html',
        handlebars: 'html',
        razor: 'html',
        typescript: 'ts',
        javascript: 'ts'
      }[label] ?? 'editor';
    return new Worker(`/assets/${name}.worker.js`, { type: 'module' });
  }
};
import * as monaco from 'monaco-editor';
import * as signalR from '@microsoft/signalr';
import * as Y from 'yjs';
import { MonacoBinding } from 'y-monaco';
import { Awareness } from 'y-protocols/awareness';
import {
  UpdateQueue,
  SyncScheduler,
  SnapshotRecovery,
  shouldWriteCheckpoint,
  CheckpointWriter,
  CheckpointJournalTracker
} from './room-sync.js';
import { BoundedUndoManager } from './bounded-undo-manager.js';
import { DEFAULT_MAX_YJS_MESSAGE_BYTES, fitsYjsMessage } from './yjs-message-size.js';

window.monaco = monaco;
window.signalR = signalR;
const remoteYjsOrigin = Object.freeze({ source: 'signalr' });
const bootstrapYjsOrigin = Object.freeze({ source: 'bootstrap' });
const maxUndoDepth = 100;
const maxUndoEstimatedBytes = 8 * 1024 * 1024;
let collaborativeUndoEnabled = true;
let maxYjsMessageBytes = DEFAULT_MAX_YJS_MESSAGE_BYTES;

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
const syncError = document.getElementById('syncError');

const pathSegments = new URL(window.location.href).pathname.split('/').filter(Boolean);
const roomId = pathSegments[pathSegments.length - 1] ?? '';
let displayName = '';
const presence = new PresenceView({ editor: null, monaco, participantsList, document });
let currentVersion = 0;
let isApplyingRemoteUpdate = false;
let cursorTimer = null;

let editor = null;
let model = null;
let editorReadyResolve;
const editorReady = new Promise((resolve) => {
  editorReadyResolve = resolve;
});
let ydoc = null;
let ytext = null;
let awareness = null;
let monacoBinding = null;
let undoHistory = null;

const maxPendingBytes = 8 * 1024 * 1024;

let yjsSession = 0;
let currentDocumentRevision = 0;
let highestDocumentRevisionSeen = 0;
let checkpointEveryRevisions = 64;
let checkpointEveryBase64Bytes = 256 * 1024;
const checkpointJournal = new CheckpointJournalTracker();
const pendingDocumentRevisions = new Set();
let isSynchronizingYjs = false;
let synchronizationEpoch = 0;
const bufferedYjsMessages = [];
let roomClosed = false;
let hasJoinedRoom = false;
let lastSentSelectionMultiLine;
let isRejoiningRoom = false;
let rejoinRetryTimer = null;
let rejoinRetryAttempt = 0;
const rejoinRetryDelaysMs = [500, 1000, 2000, 5000, 10000];

const connectionLifecycle = createRoomConnection(signalR, '/roomHub');
const connection = connectionLifecycle.connection;
Object.defineProperty(presence, 'displayName', { get: () => displayName });
const roomView = new RevisionedRoomView({
  language: (language) => {
    ensureLanguageOption(language);
    languageSelect.value = language;
    setLanguage(language);
  },
  users: (users) => presence.renderUsers(users)
});

const syncScheduler = new SyncScheduler();
const updateSender = new UpdateQueue({
  merge: (updates) => Y.mergeUpdates(updates),
  ready: () =>
    !roomClosed &&
    ydoc &&
    !isSynchronizingYjs &&
    connection.state === signalR.HubConnectionState.Connected,
  send: (update) => {
    const args = [roomId, base64FromUint8(update)];
    if (!fitsYjsMessage(HubMethods.UpdateYjs, args, maxYjsMessageBytes))
      throw createHubError(HubErrorCode.LimitExceeded, 'An edit is too large to send');
    return connection.invoke(HubMethods.UpdateYjs, ...args);
  },
  onSuccess: () => {
    clearSyncError('update-size');
    clearSyncError('transport');
  },
  onIdle: () => scheduleYjsCheckpoint(),
  onError: (error, permanent) => {
    if (isRoomNotFound(error)) return closeRoom('Room not found.');
    if (permanent) {
      setEditorReadOnly(true);
      showSyncError(
        'update-size',
        'An edit is too large or the room limit was reached. Copy your text before reloading; some changes have not synced.'
      );
    } else
      showSyncError(
        'transport',
        'Changes could not be sent. Retrying; keep this page open.'
      );
  }
});

function captureSyncContext() {
  return { document: ydoc, session: yjsSession, connectionId: connection.connectionId };
}
function isCurrentSyncContext(context) {
  return (
    !roomClosed &&
    ydoc === context.document &&
    yjsSession === context.session &&
    connection.connectionId === context.connectionId
  );
}
const recoveryController = new SnapshotRecovery({
  scheduler: syncScheduler,
  active: () => !roomClosed && hasJoinedRoom && !!ydoc,
  ready: () =>
    !isSynchronizingYjs && connection.state === signalR.HubConnectionState.Connected,
  capture: captureSyncContext,
  current: isCurrentSyncContext,
  revisions: () => ({
    DocumentRevision: currentDocumentRevision,
    MetadataRevision: roomView.languageRevision,
    PresenceRevision: roomView.presenceRevision
  }),
  probe: () => connection.invoke(HubMethods.GetRoomRevisions, roomId),
  snapshot: () => connection.invoke(HubMethods.SyncYjs, roomId),
  begin: () => {
    isSynchronizingYjs = true;
    return ++synchronizationEpoch;
  },
  apply: (result) => {
    if (
      !mergeYjsSyncState(
        result.YjsState ?? result.yjsState,
        result.YjsUpdates ?? result.yjsUpdates ?? [],
        result.DocumentRevision ?? result.documentRevision,
        result.CheckpointRevision ?? result.checkpointRevision
      )
    )
      throw new Error('Failed to recover the room document');
    currentVersion = Math.max(
      currentVersion,
      Number(result.Version ?? result.version) || 0
    );
    versionNumberEl.textContent = currentVersion;
    applyRoomMetadata(result);
  },
  end: (epoch) => {
    if (epoch !== synchronizationEpoch) return;
    isSynchronizingYjs = false;
    flushBufferedYjsMessages();
    scheduleYjsUpdateSend();
    scheduleYjsCheckpoint();
  },
  onSuccess: () => clearSyncError('recovery'),
  onError: (error) => {
    if (isRoomNotFound(error)) return closeRoom('Room not found.');
    showSyncError(
      'recovery',
      'Some changes have not arrived. Retrying synchronization; keep this page open.'
    );
  }
});
const checkpointWriter = new CheckpointWriter({
  delay: 1000,
  scheduler: syncScheduler,
  ready: () =>
    !roomClosed &&
    hasJoinedRoom &&
    !!ydoc &&
    !!ytext &&
    currentDocumentRevision > 0 &&
    currentDocumentRevision === highestDocumentRevisionSeen &&
    !isSynchronizingYjs &&
    !updateSender.pending.length &&
    !updateSender.timer &&
    !updateSender.inFlight &&
    !updateSender.blocked &&
    shouldWriteCheckpoint({
      documentRevision: currentDocumentRevision,
      checkpointRevision: checkpointJournal.checkpointRevision,
      minRevisions: checkpointEveryRevisions,
      minBytes: checkpointEveryBase64Bytes,
      bytesSinceCheckpoint: checkpointJournal.bytesSinceCheckpoint,
      isLatestAuthor: checkpointJournal.isLatestAuthor
    }) &&
    connection.state === signalR.HubConnectionState.Connected,
  capture: captureSyncContext,
  current: isCurrentSyncContext,
  revision: () => currentDocumentRevision,
  payload: (revision) => {
    const args = [
      roomId,
      base64FromUint8(Y.encodeStateAsUpdate(ydoc)),
      ytext.toString(),
      revision
    ];
    if (!fitsYjsMessage(HubMethods.CheckpointYjs, args, maxYjsMessageBytes))
      throw createHubError(HubErrorCode.LimitExceeded, 'Checkpoint size');
    return args;
  },
  send: (args) => connection.invoke(HubMethods.CheckpointYjs, ...args),
  onSuccess: (revision) => {
    checkpointJournal.noteCheckpoint(revision);
    clearSyncError('checkpoint-size');
  },
  onError: (error, permanent) => {
    if (isRoomNotFound(error)) return closeRoom('Room not found.');
    if (permanent)
      showSyncError(
        'checkpoint-size',
        'The document is too large to save a snapshot. Edits still sync; reduce its size to restore snapshots.'
      );
  }
});

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
  return (
    connection.state === signalR.HubConnectionState.Connected &&
    connectionStatusEl.textContent === 'connected'
  );
};

function setStatus(status) {
  connectionStatusEl.textContent = status;
}

function showSyncError(kind, message) {
  syncError.dataset.kind = kind;
  syncError.textContent = message;
  syncError.classList.remove('hidden');
}

function clearSyncError(kind) {
  if (syncError.dataset.kind !== kind) return;
  syncError.textContent = '';
  syncError.classList.add('hidden');
}

function clearRejoinRetryTimer() {
  if (!rejoinRetryTimer) return;
  clearTimeout(rejoinRetryTimer);
  rejoinRetryTimer = null;
}

async function ensureConnectionStarted() {
  return connectionLifecycle.start();
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

function setLanguageOptions(selectEl, languages, selected) {
  const cleaned = Array.isArray(languages)
    ? languages
        .map((language) => (typeof language === 'string' ? language.trim() : ''))
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

function sendCursor() {
  if (!displayName) return;
  if (connection.state !== signalR.HubConnectionState.Connected) return;
  if (!editor || !model) return;
  const pos = editor.getPosition();
  if (!pos) return;
  const offset = model.getOffsetAt(pos);
  presence.cursorPositions[displayName] = offset;
  presence.renderUsers(presence.currentUsers);
  presence.renderRemoteCarets();
  connection.invoke(HubMethods.UpdateCursor, roomId, offset).catch(() => {});
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

async function loadFeatures() {
  try {
    const response = await fetch('/features');
    if (!response.ok) throw new Error('Failed to load feature flags');
    const features = await response.json();
    collaborativeUndoEnabled =
      features.CollaborativeUndoEnabled ?? features.collaborativeUndoEnabled ?? true;
    const limits = features.Limits ?? features.limits ?? {};
    const revisions = limits.CheckpointEveryRevisions ?? limits.checkpointEveryRevisions;
    const bytes = limits.CheckpointEveryBase64Bytes ?? limits.checkpointEveryBase64Bytes;
    if (Number.isSafeInteger(revisions) && revisions > 0) {
      checkpointEveryRevisions = revisions;
    }
    if (Number.isSafeInteger(bytes) && bytes > 0) {
      checkpointEveryBase64Bytes = bytes;
    }
    const messageLimit = features.MaxYjsMessageBytes ?? features.maxYjsMessageBytes;
    if (Number.isSafeInteger(messageLimit) && messageLimit > 1024) {
      maxYjsMessageBytes = messageLimit;
    }
  } catch (err) {
    console.warn(
      'Failed to load feature flags; collaborative undo remains enabled.',
      err
    );
    collaborativeUndoEnabled = true;
  }
}

function performCollaborativeUndo() {
  if (!undoHistory || connection.state !== signalR.HubConnectionState.Connected)
    return false;
  return undoHistory.undo() != null;
}

function performCollaborativeRedo() {
  if (!undoHistory || connection.state !== signalR.HubConnectionState.Connected)
    return false;
  return undoHistory.redo() != null;
}

function setLanguage(language) {
  if (!model) return;
  monaco.editor.setModelLanguage(model, language);
}

function setEditorReadOnly(isReadOnly) {
  if (!editor) return;
  editor.updateOptions({ readOnly: isReadOnly || updateSender.blocked });
}

function setLanguageReadOnly(isReadOnly) {
  if (!languageSelect) return;
  languageSelect.disabled = isReadOnly;
}

function scheduleYjsUpdateSend(update) {
  if (roomClosed || !ydoc) return;
  updateSender.enqueue(update);
}

function normalizeYjsUpdates(updates) {
  if (!Array.isArray(updates)) return [];
  return updates
    .map((item) => ({
      revision: Number(item?.Revision ?? item?.revision),
      update: item?.Update ?? item?.update
    }))
    .filter(
      (item) =>
        Number.isSafeInteger(item.revision) &&
        item.revision > 0 &&
        typeof item.update === 'string' &&
        item.update.length > 0
    )
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
  checkpointJournal.resetFromSync(baseRevision, updates);
  currentDocumentRevision =
    Number.isSafeInteger(latestRevision) && latestRevision >= 0 ? latestRevision : 0;
  highestDocumentRevisionSeen = currentDocumentRevision;
  pendingDocumentRevisions.clear();

  let expectedRevision =
    Number.isSafeInteger(baseRevision) && baseRevision >= 0 ? baseRevision : 0;
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
  if (currentDocumentRevision < highestDocumentRevisionSeen) scheduleYjsRecovery(250);
  return advanced;
}

function scheduleYjsRecovery(delay = 5000) {
  recoveryController.schedule(delay);
}
function scheduleYjsCheckpoint() {
  checkpointWriter.schedule();
}

function disposeYjs() {
  checkpointJournal.reset();
  yjsSession += 1;
  if (updateSender.timer) clearTimeout(updateSender.timer);
  updateSender.timer = null;
  updateSender.reset();
  syncScheduler.reset();
  updateSender.inFlight = false;
  recoveryController.reset();
  checkpointWriter.reset();
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
    scheduleYjsUpdateSend(update);
  });
  resetDocumentRevisionState(documentRevision, checkpointRevision, normalizedUpdates);
  presence.renderRemoteCarets();
}

function mergeYjsSyncState(stateBase64, updates, documentRevision, checkpointRevision) {
  if (!ydoc || !ytext) return false;
  const normalizedUpdates = applyYjsSyncPayload('', stateBase64, updates);
  resetDocumentRevisionState(documentRevision, checkpointRevision, normalizedUpdates);
  if (model && model.getValue() !== ytext.toString()) {
    console.error('Monaco and Yjs diverged after reconnect synchronization.');
    return false;
  }
  presence.renderRemoteCarets();
  return true;
}

function applyRemoteCursorAdjustment(changes) {
  if (!changes || !changes.length || !model) return;
  const sorted = [...changes].sort((a, b) => (a.rangeOffset ?? 0) - (b.rangeOffset ?? 0));
  for (const change of sorted) {
    const start =
      typeof change.rangeOffset === 'number'
        ? change.rangeOffset
        : model.getOffsetAt({
            lineNumber: change.range.startLineNumber,
            column: change.range.startColumn
          });
    const length =
      typeof change.rangeLength === 'number'
        ? change.rangeLength
        : model.getOffsetAt({
            lineNumber: change.range.endLineNumber,
            column: change.range.endColumn
          }) - start;
    const end = start + length;
    const delta = (change.text || '').length - length;

    for (const [name, offset] of Object.entries(presence.cursorPositions)) {
      if (name === displayName || typeof offset !== 'number') continue;
      if (offset <= start) continue;
      if (offset >= end) {
        presence.cursorPositions[name] = offset + delta;
      } else {
        presence.cursorPositions[name] = start + Math.max(0, (change.text || '').length);
      }
    }
  }
}

function initMonaco() {
  ({ model, editor } = createMonacoAdapter(monaco, editorHost, {
    language: languageSelect?.value,
    undo: performCollaborativeUndo,
    redo: performCollaborativeRedo
  }));

  presence.editor = editor;
  editor.onDidChangeModelContent((event) => {
    if (isApplyingRemoteUpdate) return;
    presence.hideRemoteCaretsTemporarily();
    applyRemoteCursorAdjustment(event.changes);
    presence.markActiveEditor(displayName);
    scheduleCursorSend();
  });

  editor.onKeyDown((keyboardEvent) => {
    const browserEvent = keyboardEvent.browserEvent;
    if (isPasteShortcut(browserEvent)) {
      undoHistory?.stopCapturing();
      setTimeout(() => undoHistory?.stopCapturing(), 0);
    }
    if (isUndoRedoShortcut(browserEvent)) {
      browserEvent.preventDefault();
      browserEvent.stopImmediatePropagation();
      keyboardEvent.preventDefault();
      keyboardEvent.stopPropagation();
      if (isUndoShortcut(browserEvent)) {
        performCollaborativeUndo();
      } else {
        performCollaborativeRedo();
      }
      return;
    }
    if (!isCopyShortcut(browserEvent)) return;
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
    presence.markSelecting(displayName, isMultiLine);
    if (lastSentSelectionMultiLine === isMultiLine) return;
    lastSentSelectionMultiLine = isMultiLine;
    connection.invoke(HubMethods.UpdateSelection, roomId, isMultiLine).catch((err) => {
      lastSentSelectionMultiLine = undefined;
      console.warn('Failed to send selection update.', err);
    });
  });

  editorReadyResolve();
}

async function joinRoom() {
  if (roomClosed) return false;
  joinError.classList.add('hidden');
  isSynchronizingYjs = true;
  const epoch = ++synchronizationEpoch;
  try {
    const result = await connection.invoke(HubMethods.JoinRoom, roomId, displayName);
    if (roomClosed || epoch !== synchronizationEpoch) return false;
    const name = result.Name ?? result.name;
    const language = result.Language ?? result.language;
    const text = result.Text ?? result.text;
    const version = result.Version ?? result.version;
    const users = result.Users ?? result.users;
    const createdBy = result.CreatedBy ?? result.createdBy;
    const yjsState = result.YjsState ?? result.yjsState;
    const yjsUpdates = result.YjsUpdates ?? result.yjsUpdates ?? [];
    const documentRevision = result.DocumentRevision ?? result.documentRevision ?? 0;
    const checkpointRevision =
      result.CheckpointRevision ?? result.checkpointRevision ?? 0;
    roomNameEl.textContent = name;
    if (createdByEl) {
      createdByEl.textContent = createdBy || 'unknown';
    }
    applyRoomMetadata(result);
    if (ydoc && hasJoinedRoom) {
      if (
        !mergeYjsSyncState(yjsState, yjsUpdates, documentRevision, checkpointRevision)
      ) {
        throw new Error('Failed to merge the room document after reconnect.');
      }
    } else {
      initializeYjs(
        text || '',
        yjsState,
        yjsUpdates,
        documentRevision,
        checkpointRevision
      );
    }
    currentVersion = Math.max(currentVersion, version);
    versionNumberEl.textContent = currentVersion;
    hasJoinedRoom = true;
    lastSentSelectionMultiLine = undefined;
    scheduleYjsRecovery();
    rejoinRetryAttempt = 0;
    clearRejoinRetryTimer();
    joinOverlay.classList.add('hidden');
    sendCursor();
    return true;
  } catch (err) {
    if (isRoomNotFound(err)) {
      closeRoom('Room not found.');
      return false;
    }
    joinError.textContent = err?.message || 'Failed to join room.';
    joinError.classList.remove('hidden');
    return false;
  } finally {
    if (epoch === synchronizationEpoch) {
      isSynchronizingYjs = false;
      flushBufferedYjsMessages();
      scheduleYjsUpdateSend();
      scheduleYjsCheckpoint();
    }
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
  recoveryController.reset();
  checkpointWriter.reset();
  syncScheduler.reset();
  clearRejoinRetryTimer();
  isRejoiningRoom = false;
  setStatus('reconnecting');
  setEditorReadOnly(true);
  setLanguageReadOnly(true);
});
connection.onclose(() => {
  recoveryController.reset();
  checkpointWriter.reset();
  syncScheduler.reset();
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

connection.on(HubEvents.UserJoined, (name, users, revision) => {
  if (!roomView.updateUsers(users, Number(revision))) return;
  presence.cursorPositions[name] ??= 0;
  presence.renderUsers(users);
  presence.renderRemoteCarets();
});

connection.on(HubEvents.UserLeft, (name, users, revision) => {
  if (!roomView.updateUsers(users, Number(revision))) return;
  delete presence.cursorPositions[name];
  presence.caretClassMap.delete(name);
  delete presence.caretColors[name];
  presence.activeEditors.delete(name);
  presence.selectingUsers.delete(name);
  presence.copyingUsers.delete(name);
  presence.renderUsers(users);
  presence.renderRemoteCarets();
});

function handleYjsUpdated(updateBase64, documentRevision, roomVersion, author) {
  currentVersion = Math.max(currentVersion, Number(roomVersion) || 0);
  versionNumberEl.textContent = currentVersion;
  presence.renderUsers(presence.currentUsers);
  if (!ydoc) return;
  if (author && author !== displayName) presence.markActiveEditor(author);
  const update = uint8FromBase64(updateBase64);
  if (update.length === 0) return;
  applyYjsUpdate(update, remoteYjsOrigin);
  const revision = Number(documentRevision);
  checkpointJournal.noteUpdate(
    revision,
    updateBase64.length,
    author === displayName,
    highestDocumentRevisionSeen
  );
  noteDocumentRevision(documentRevision);
  if (model && ytext && model.getValue() !== ytext.toString()) {
    console.error('Monaco and Yjs diverged after applying a room update.', {
      documentRevision
    });
  }
}

function flushBufferedYjsMessages() {
  if (isSynchronizingYjs || bufferedYjsMessages.length === 0) return;
  const messages = bufferedYjsMessages
    .splice(0, bufferedYjsMessages.length)
    .sort(
      (left, right) => Number(left.documentRevision) - Number(right.documentRevision)
    );
  for (const message of messages) {
    handleYjsUpdated(
      message.updateBase64,
      message.documentRevision,
      message.roomVersion,
      message.author
    );
  }
}

connection.on(
  HubEvents.YjsUpdated,
  (updateBase64, documentRevision, roomVersion, author) => {
    if (isSynchronizingYjs) {
      if (
        bufferedYjsMessages.length >= 2048 ||
        bufferedYjsMessages.reduce((sum, item) => sum + item.updateBase64.length, 0) +
          updateBase64.length >
          maxPendingBytes
      ) {
        // The authoritative journal is retained; discard only this delivery buffer and recover.
        bufferedYjsMessages.length = 0;
        scheduleYjsRecovery(250);
        return;
      }
      bufferedYjsMessages.push({ updateBase64, documentRevision, roomVersion, author });
      return;
    }
    handleYjsUpdated(updateBase64, documentRevision, roomVersion, author);
  }
);

connection.on(HubEvents.YjsCheckpointed, (revision) => {
  revision = Number(revision);
  if (!Number.isSafeInteger(revision) || revision <= 0) return;
  checkpointJournal.noteCheckpoint(revision);
});

connection.on(HubEvents.LanguageUpdated, (language, version, revision) => {
  if (!roomView.updateLanguage(language, Number(revision))) return;
  languageSelect.value = language;
  setLanguage(language);
  currentVersion = Math.max(currentVersion, Number(version) || 0);
  versionNumberEl.textContent = currentVersion;
});

connection.on(HubEvents.CursorUpdated, (name, position) => {
  if (!name) return;
  presence.cursorPositions[name] = position;
  presence.renderUsers(presence.currentUsers);
  presence.renderRemoteCarets();
});

connection.on(HubEvents.UserSelection, (name, isMultiLine) => {
  presence.markSelecting(name, isMultiLine);
});

connection.on(HubEvents.UserCopy, (name) => {
  presence.markCopying(name);
});

function isRoomNotFound(error) {
  return classifyHubError(error) === HubErrorCode.RoomNotFound;
}

function closeRoom(reason) {
  roomClosed = true;
  lastSentSelectionMultiLine = undefined;
  synchronizationEpoch += 1;
  isSynchronizingYjs = false;
  hasJoinedRoom = false;
  bufferedYjsMessages.length = 0;
  clearRejoinRetryTimer();
  setEditorReadOnly(true);
  setLanguageReadOnly(true);
  disposeYjs();
  joinError.textContent = reason || 'Room closed.';
  joinError.classList.remove('hidden');
  joinOverlay.classList.remove('hidden');
}

connection.on(HubEvents.RoomKilled, closeRoom);

languageSelect.addEventListener('change', async () => {
  if (!displayName) return;
  if (!languageSelect.value) return;
  const restoreRoomLanguage = () => {
    if (roomView.currentLanguage === undefined) return;
    languageSelect.value = roomView.currentLanguage;
    setLanguage(roomView.currentLanguage);
  };
  if (connection.state !== signalR.HubConnectionState.Connected) {
    restoreRoomLanguage();
    return;
  }
  setLanguage(languageSelect.value);
  try {
    await connection.invoke(HubMethods.SetLanguage, roomId, languageSelect.value);
  } catch (err) {
    restoreRoomLanguage();
    console.warn('Failed to update language.', err);
  }
});

document.addEventListener(
  'copy',
  (event) => {
    if (!editor) return;
    const target = event.target;
    const isEditorCopy =
      editor.hasTextFocus() || (target instanceof Node && editorHost.contains(target));
    if (!isEditorCopy) return;
    if (!hasNonEmptyEditorSelection()) {
      event.preventDefault();
      event.stopImmediatePropagation();
      return;
    }
    if (!displayName) return;
    if (connection.state !== signalR.HubConnectionState.Connected) return;
    presence.markCopying(displayName);
    connection.invoke(HubMethods.UpdateCopy, roomId).catch(() => {});
  },
  true
);

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

function applyRoomMetadata(result) {
  roomView.apply(result);
}
