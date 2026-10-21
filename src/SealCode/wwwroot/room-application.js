import { RoomLifecycle, isRoomNotFound, registerHubHandlers } from './room-lifecycle.js';
import { YjsSession } from './yjs-session.js';
import { PresenceView } from './presence-ui.js';
import { base64FromUint8 } from './encoding.js';
import {
  isCopyShortcut,
  isUndoShortcut,
  isRedoShortcut,
  isUndoRedoShortcut,
  isPasteShortcut
} from './keyboard-shortcuts.js';
import { HubMethods } from './hub-contract.js';
import { HubErrorCode, createHubError } from './hub-errors.js';
import { createMonacoAdapter } from './monaco-adapter.js';
import { createRoomConnection } from './room-connection.js';
import { RevisionedRoomView } from './room-presence.js';
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
import { readClientLimits } from './client-limits.js';

/** Creates the room controllers and wires their UI and transport callbacks. */
export function createRoomApplication({
  monaco,
  signalR,
  Y,
  MonacoBinding,
  Awareness,
  window,
  document,
  roomId,
  elements
}) {
  const {
    connectionStatusEl,
    roomNameEl,
    createdByEl,
    participantsList,
    languageSelect,
    versionNumberEl,
    editorHost,
    joinOverlay,
    joinBtn,
    displayNameInput,
    joinError,
    syncError
  } = elements;
  let collaborativeUndoEnabled = true;
  let maxYjsMessageBytes = DEFAULT_MAX_YJS_MESSAGE_BYTES;
  let maxEncodedCheckpointBytes = 4 * 1024 * 1024;
  let maxSnapshotCharacters = 1024 * 1024;

  let displayName = '';
  const presence = new PresenceView({ editor: null, monaco, participantsList, document });
  let currentVersion = 0;
  let cursorTimer = null;

  let editor = null;
  let model = null;
  let editorReadyResolve;
  const editorReady = new Promise((resolve) => {
    editorReadyResolve = resolve;
  });

  let checkpointEveryRevisions = 64;
  let checkpointEveryBase64Bytes = 256 * 1024;
  let lastSentSelectionMultiLine;

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

  const yjs = new YjsSession({
    Y,
    MonacoBinding,
    Awareness,
    BoundedUndoManager,
    CheckpointJournalTracker,
    getDisplayName: () => displayName,
    undoEnabled: () => collaborativeUndoEnabled,
    cursorPositions: presence.cursorPositions,
    onRevision: (version) => {
      currentVersion = Math.max(currentVersion, Number(version) || 0);
      versionNumberEl.textContent = currentVersion;
      presence.renderUsers(presence.currentUsers);
    },
    onRemoteChange: () => presence.renderRemoteCarets(),
    onRemoteAuthor: (author) => presence.markActiveEditor(author),
    onUpdate: (update) => scheduleYjsUpdateSend(update),
    onCheckpoint: () => scheduleYjsCheckpoint(),
    onRecovery: (delay) => scheduleYjsRecovery(delay),
    onDispose: () => {
      if (updateSender.timer) clearTimeout(updateSender.timer);
      updateSender.reset();
      syncScheduler.reset();
      recoveryController.reset();
      checkpointWriter.reset();
    }
  });

  const lifecycle = new RoomLifecycle({
    connection,
    roomId,
    session: yjs,
    getDisplayName: () => displayName,
    start: () => ensureConnectionStarted(),
    stop: () => connection.stop(),
    connectedState: signalR.HubConnectionState.Connected,
    onJoinStart: () => joinError.classList.add('hidden'),
    applyJoin: (result) => {
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
      if (yjs.ydoc && lifecycle.joined) {
        if (
          !yjs.mergeYjsSyncState(
            yjsState,
            yjsUpdates,
            documentRevision,
            checkpointRevision
          )
        ) {
          throw new Error('Failed to merge the room document after reconnect.');
        }
      } else {
        yjs.initializeYjs(
          text || '',
          yjsState,
          yjsUpdates,
          documentRevision,
          checkpointRevision
        );
      }
      currentVersion = Math.max(currentVersion, version);
      versionNumberEl.textContent = currentVersion;
    },
    onJoinSuccess: () => {
      lastSentSelectionMultiLine = undefined;
      scheduleYjsRecovery();
      joinOverlay.classList.add('hidden');
      sendCursor();
    },
    onJoinError: (error) => {
      joinError.textContent = error?.message || 'Failed to join room.';
      joinError.classList.remove('hidden');
    },
    onSynchronizationEnd: () => {
      yjs.flushBufferedYjsMessages();
      scheduleYjsUpdateSend();
      scheduleYjsCheckpoint();
    },
    onClose: (reason) => {
      lastSentSelectionMultiLine = undefined;
      yjs.disposeYjs();
      joinError.textContent = reason || 'Room closed.';
      joinError.classList.remove('hidden');
      joinOverlay.classList.remove('hidden');
    },
    setStatus,
    setReadOnly: (readOnly) => {
      setEditorReadOnly(readOnly);
      setLanguageReadOnly(readOnly);
    },
    resetTransport: () => {
      recoveryController.reset();
      checkpointWriter.reset();
      syncScheduler.reset();
    }
  });

  const syncScheduler = new SyncScheduler();
  const updateSender = new UpdateQueue({
    merge: (updates) => Y.mergeUpdates(updates),
    ready: () =>
      !lifecycle.closed &&
      yjs.ydoc &&
      !yjs.isSynchronizingYjs &&
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
      if (isRoomNotFound(error)) return lifecycle.closeRoom('Room not found.');
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
    return {
      document: yjs.ydoc,
      session: yjs.yjsSession,
      connectionId: connection.connectionId
    };
  }
  function isCurrentSyncContext(context) {
    return (
      !lifecycle.closed &&
      yjs.ydoc === context.document &&
      yjs.yjsSession === context.session &&
      connection.connectionId === context.connectionId
    );
  }
  const recoveryController = new SnapshotRecovery({
    scheduler: syncScheduler,
    active: () => !lifecycle.closed && lifecycle.joined && !!yjs.ydoc,
    ready: () =>
      !yjs.isSynchronizingYjs &&
      connection.state === signalR.HubConnectionState.Connected,
    capture: captureSyncContext,
    current: isCurrentSyncContext,
    revisions: () => ({
      DocumentRevision: yjs.currentDocumentRevision,
      MetadataRevision: roomView.languageRevision,
      PresenceRevision: roomView.presenceRevision
    }),
    probe: () => connection.invoke(HubMethods.GetRoomRevisions, roomId),
    snapshot: () => connection.invoke(HubMethods.SyncYjs, roomId),
    begin: () => {
      yjs.isSynchronizingYjs = true;
      return ++yjs.synchronizationEpoch;
    },
    apply: (result) => {
      if (
        !yjs.mergeYjsSyncState(
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
      if (epoch !== yjs.synchronizationEpoch) return;
      yjs.isSynchronizingYjs = false;
      yjs.flushBufferedYjsMessages();
      scheduleYjsUpdateSend();
      scheduleYjsCheckpoint();
    },
    onSuccess: () => clearSyncError('recovery'),
    onError: (error) => {
      if (isRoomNotFound(error)) return lifecycle.closeRoom('Room not found.');
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
      !lifecycle.closed &&
      lifecycle.joined &&
      !!yjs.ydoc &&
      !!yjs.ytext &&
      yjs.currentDocumentRevision > 0 &&
      yjs.currentDocumentRevision === yjs.highestDocumentRevisionSeen &&
      !yjs.isSynchronizingYjs &&
      !updateSender.pending.length &&
      !updateSender.timer &&
      !updateSender.inFlight &&
      !updateSender.blocked &&
      shouldWriteCheckpoint({
        documentRevision: yjs.currentDocumentRevision,
        checkpointRevision: yjs.checkpointJournal.checkpointRevision,
        minRevisions: checkpointEveryRevisions,
        minBytes: checkpointEveryBase64Bytes,
        bytesSinceCheckpoint: yjs.checkpointJournal.bytesSinceCheckpoint,
        isLatestAuthor: yjs.checkpointJournal.isLatestAuthor
      }) &&
      connection.state === signalR.HubConnectionState.Connected,
    capture: captureSyncContext,
    current: isCurrentSyncContext,
    revision: () => yjs.currentDocumentRevision,
    payload: (revision) => {
      const args = [
        roomId,
        base64FromUint8(Y.encodeStateAsUpdate(yjs.ydoc)),
        yjs.ytext.toString(),
        revision
      ];
      if (
        args[1].length > maxEncodedCheckpointBytes ||
        args[2].length > maxSnapshotCharacters ||
        !fitsYjsMessage(HubMethods.CheckpointYjs, args, maxYjsMessageBytes)
      )
        throw createHubError(HubErrorCode.LimitExceeded, 'Checkpoint size');
      return args;
    },
    send: (args) => connection.invoke(HubMethods.CheckpointYjs, ...args),
    onSuccess: (revision) => {
      yjs.checkpointJournal.noteCheckpoint(revision);
      clearSyncError('checkpoint-size');
    },
    onError: (error, permanent) => {
      if (isRoomNotFound(error)) return lifecycle.closeRoom('Room not found.');
      if (permanent)
        showSyncError(
          'checkpoint-size',
          'The document is too large to save a snapshot. Edits still sync; reduce its size to restore snapshots.'
        );
    }
  });

  window.__debugYjsSend = () => {
    if (!yjs.ydoc) return false;
    scheduleYjsUpdateSend(Y.encodeStateAsUpdate(yjs.ydoc));
    return true;
  };

  window.__debugUndoState = () => ({
    canUndo: yjs.undoHistory?.canUndo() ?? false,
    canRedo: yjs.undoHistory?.canRedo() ?? false,
    undoDepth: yjs.undoHistory?.undoDepth ?? 0,
    redoDepth: yjs.undoHistory?.redoDepth ?? 0,
    documentRevision: yjs.currentDocumentRevision,
    highestDocumentRevisionSeen: yjs.highestDocumentRevisionSeen
  });

  window.__debugReconnect = async () => {
    if (!lifecycle.joined || lifecycle.closed) return false;
    await connection.stop();
    const started = await ensureConnectionStarted();
    if (!started) return false;
    await lifecycle.rejoinRoomWithRetry();
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

  async function ensureConnectionStarted() {
    return connectionLifecycle.start();
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
      const limits = readClientLimits(features);
      checkpointEveryRevisions = limits.checkpointEveryRevisions;
      checkpointEveryBase64Bytes = limits.checkpointEveryBase64Bytes;
      maxYjsMessageBytes = limits.maxYjsMessageBytes;
      maxEncodedCheckpointBytes = limits.maxEncodedCheckpointBytes;
      maxSnapshotCharacters = limits.maxSnapshotCharacters;
      updateSender.maxEncodedBytes = limits.maxEncodedUpdateBytes;
      checkpointWriter.retryAfterRevisions = checkpointEveryRevisions;
    } catch (err) {
      console.warn(
        'Failed to load feature flags; collaborative undo remains enabled.',
        err
      );
      collaborativeUndoEnabled = true;
    }
  }

  function performCollaborativeUndo() {
    if (!yjs.undoHistory || connection.state !== signalR.HubConnectionState.Connected)
      return false;
    return yjs.undoHistory.undo() != null;
  }

  function performCollaborativeRedo() {
    if (!yjs.undoHistory || connection.state !== signalR.HubConnectionState.Connected)
      return false;
    return yjs.undoHistory.redo() != null;
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
    if (lifecycle.closed || !yjs.ydoc) return;
    updateSender.enqueue(update);
  }

  function scheduleYjsRecovery(delay = 5000) {
    recoveryController.schedule(delay);
  }
  function scheduleYjsCheckpoint() {
    checkpointWriter.schedule();
  }

  function initMonaco() {
    ({ model, editor } = createMonacoAdapter(monaco, editorHost, {
      language: languageSelect?.value,
      undo: performCollaborativeUndo,
      redo: performCollaborativeRedo
    }));

    presence.editor = editor;
    yjs.editor = editor;
    editor.onDidChangeModelContent((event) => {
      if (yjs.isApplyingRemoteUpdate) return;
      presence.hideRemoteCaretsTemporarily();
      yjs.applyRemoteCursorAdjustment(event.changes);
      presence.markActiveEditor(displayName);
      scheduleCursorSend();
    });

    editor.onKeyDown((keyboardEvent) => {
      const browserEvent = keyboardEvent.browserEvent;
      if (isPasteShortcut(browserEvent)) {
        yjs.undoHistory?.stopCapturing();
        setTimeout(() => yjs.undoHistory?.stopCapturing(), 0);
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

    editor.onDidPaste?.(() => yjs.undoHistory?.stopCapturing());

    editor.onDidChangeCursorSelection((event) => {
      if (event.reason === monaco.editor.CursorChangeReason.Explicit) {
        yjs.undoHistory?.stopCapturing();
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

  lifecycle.registerConnectivity(window);

  registerHubHandlers(connection, {
    UserJoined: (name, users, revision) => {
      if (!roomView.updateUsers(users, Number(revision))) return;
      presence.cursorPositions[name] ??= 0;
      presence.renderUsers(users);
      presence.renderRemoteCarets();
    },
    UserLeft: (name, users, revision) => {
      if (!roomView.updateUsers(users, Number(revision))) return;
      delete presence.cursorPositions[name];
      presence.caretClassMap.delete(name);
      delete presence.caretColors[name];
      presence.activeEditors.delete(name);
      presence.selectingUsers.delete(name);
      presence.copyingUsers.delete(name);
      presence.renderUsers(users);
      presence.renderRemoteCarets();
    },
    YjsUpdated: (...args) => yjs.receiveUpdate(...args),
    YjsCheckpointed: (revision) => {
      revision = Number(revision);
      if (!Number.isSafeInteger(revision) || revision <= 0) return;
      yjs.checkpointJournal.noteCheckpoint(revision);
    },
    LanguageUpdated: (language, version, revision) => {
      if (!roomView.updateLanguage(language, Number(revision))) return;
      languageSelect.value = language;
      setLanguage(language);
      currentVersion = Math.max(currentVersion, Number(version) || 0);
      versionNumberEl.textContent = currentVersion;
    },
    CursorUpdated: (name, position) => {
      if (!name) return;
      presence.cursorPositions[name] = position;
      presence.renderUsers(presence.currentUsers);
      presence.renderRemoteCarets();
    },
    UserSelection: (name, isMultiLine) => {
      presence.markSelecting(name, isMultiLine);
    },
    UserCopy: (name) => {
      presence.markCopying(name);
    },
    RoomKilled: (reason) => lifecycle.closeRoom(reason)
  });

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
    const joined = await lifecycle.joinRoom();
    if (!joined && !lifecycle.joined) await connection.stop();
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
}
