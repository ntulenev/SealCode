import { uint8FromBase64 } from './encoding.js';

const remoteYjsOrigin = Object.freeze({ source: 'signalr' });
const bootstrapYjsOrigin = Object.freeze({ source: 'bootstrap' });
const maxUndoDepth = 100;
const maxUndoEstimatedBytes = 8 * 1024 * 1024;
const maxPendingBytes = 8 * 1024 * 1024;

/** Owns one Yjs document, undo history, revisions and synchronization buffer. */
export class YjsSession {
  constructor({
    Y,
    MonacoBinding,
    Awareness,
    BoundedUndoManager,
    CheckpointJournalTracker,
    editor = null,
    getDisplayName,
    undoEnabled,
    cursorPositions,
    onRevision,
    onRemoteChange,
    onRemoteAuthor,
    onUpdate,
    onCheckpoint,
    onRecovery,
    onDispose
  }) {
    this.Y = Y;
    this.MonacoBinding = MonacoBinding;
    this.Awareness = Awareness;
    this.BoundedUndoManager = BoundedUndoManager;
    this.editor = editor;
    this.getDisplayName = getDisplayName;
    this.undoEnabled = undoEnabled;
    this.cursorPositions = cursorPositions;
    this.onRevision = onRevision;
    this.onRemoteChange = onRemoteChange;
    this.onRemoteAuthor = onRemoteAuthor;
    this.onUpdate = onUpdate;
    this.onCheckpoint = onCheckpoint;
    this.onRecovery = onRecovery;
    this.onDispose = onDispose;
    this.ydoc = null;
    this.ytext = null;
    this.awareness = null;
    this.monacoBinding = null;
    this.undoHistory = null;
    this.currentDocumentRevision = 0;
    this.highestDocumentRevisionSeen = 0;
    this.pendingDocumentRevisions = new Set();
    this.isApplyingRemoteUpdate = false;
    this.isSynchronizingYjs = false;
    this.synchronizationEpoch = 0;
    this.bufferedYjsMessages = [];
    this.yjsSession = 0;
    this.checkpointJournal = new CheckpointJournalTracker();
  }

  get model() {
    return this.editor?.getModel();
  }

  normalizeYjsUpdates(updates) {
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

  applyYjsUpdate(update, origin) {
    if (!this.ydoc || !update || update.length === 0) return;
    this.isApplyingRemoteUpdate = true;
    try {
      this.Y.applyUpdate(this.ydoc, update, origin);
    } finally {
      this.isApplyingRemoteUpdate = false;
    }
  }

  applyYjsSyncPayload(text, stateBase64, updates) {
    const normalizedUpdates = this.normalizeYjsUpdates(updates);
    let appliedCrdtState = false;
    if (stateBase64) {
      const checkpoint = uint8FromBase64(stateBase64);
      if (checkpoint.length > 0) {
        this.applyYjsUpdate(checkpoint, bootstrapYjsOrigin);
        appliedCrdtState = true;
      }
    }
    for (const item of normalizedUpdates) {
      const update = uint8FromBase64(item.update);
      if (update.length === 0) continue;
      this.applyYjsUpdate(update, bootstrapYjsOrigin);
      appliedCrdtState = true;
    }
    if (!appliedCrdtState && text && this.ytext?.length === 0) {
      this.ydoc.transact(() => {
        this.ytext.insert(0, text);
      }, bootstrapYjsOrigin);
    }
    return normalizedUpdates;
  }

  resetDocumentRevisionState(documentRevision, checkpointRevision, updates) {
    const latestRevision = Number(documentRevision);
    const baseRevision = Number(checkpointRevision);
    this.checkpointJournal.resetFromSync(baseRevision, updates);
    this.currentDocumentRevision =
      Number.isSafeInteger(latestRevision) && latestRevision >= 0 ? latestRevision : 0;
    this.highestDocumentRevisionSeen = this.currentDocumentRevision;
    this.pendingDocumentRevisions.clear();

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
    if (expectedRevision !== this.currentDocumentRevision) {
      console.warn(
        'Yjs synchronization payload does not end at the advertised revision.',
        {
          expectedRevision,
          currentDocumentRevision: this.currentDocumentRevision
        }
      );
    }
    if (this.currentDocumentRevision > baseRevision) this.onCheckpoint();
  }

  noteDocumentRevision(revision) {
    const parsedRevision = Number(revision);
    if (!Number.isSafeInteger(parsedRevision) || parsedRevision <= 0) return false;
    this.highestDocumentRevisionSeen = Math.max(
      this.highestDocumentRevisionSeen,
      parsedRevision
    );
    if (parsedRevision <= this.currentDocumentRevision) return false;

    this.pendingDocumentRevisions.add(parsedRevision);
    let advanced = false;
    while (this.pendingDocumentRevisions.delete(this.currentDocumentRevision + 1)) {
      this.currentDocumentRevision += 1;
      advanced = true;
    }
    if (advanced && this.currentDocumentRevision === this.highestDocumentRevisionSeen) {
      this.onCheckpoint();
    }
    if (this.currentDocumentRevision < this.highestDocumentRevisionSeen)
      this.onRecovery(250);
    return advanced;
  }

  disposeYjs(preserveSynchronization = false) {
    this.checkpointJournal.reset();
    this.yjsSession += 1;
    this.onDispose();
    this.undoHistory?.destroy();
    this.undoHistory = null;
    this.monacoBinding?.destroy();
    this.monacoBinding = null;
    this.awareness?.destroy();
    this.awareness = null;
    this.ydoc?.destroy();
    this.ydoc = null;
    this.ytext = null;
    this.currentDocumentRevision = 0;
    this.highestDocumentRevisionSeen = 0;
    this.pendingDocumentRevisions.clear();
    this.isApplyingRemoteUpdate = false;
    if (!preserveSynchronization) {
      this.isSynchronizingYjs = false;
      this.synchronizationEpoch += 1;
      this.bufferedYjsMessages.length = 0;
    }
  }

  initializeYjs(text, stateBase64, updates, documentRevision, checkpointRevision) {
    if (!this.model) return;
    this.disposeYjs(true);
    this.ydoc = new this.Y.Doc();
    this.ytext = this.ydoc.getText('monaco');
    const normalizedUpdates = this.applyYjsSyncPayload(text, stateBase64, updates);
    this.awareness = new this.Awareness(this.ydoc);
    if (this.getDisplayName()) {
      this.awareness.setLocalStateField('user', { name: this.getDisplayName() });
    }
    this.isApplyingRemoteUpdate = true;
    try {
      this.monacoBinding = new this.MonacoBinding(
        this.ytext,
        this.model,
        new Set([this.editor]),
        this.awareness
      );
    } finally {
      this.isApplyingRemoteUpdate = false;
    }
    if (this.undoEnabled()) {
      this.undoHistory = new this.BoundedUndoManager(this.Y, this.ytext, {
        trackedOrigin: this.monacoBinding,
        maxDepth: maxUndoDepth,
        maxEstimatedBytes: maxUndoEstimatedBytes,
        captureTimeout: 500
      });
    }
    this.ydoc.on('update', (update, origin) => {
      if (origin === remoteYjsOrigin || origin === bootstrapYjsOrigin) return;
      this.onUpdate(update);
    });
    this.resetDocumentRevisionState(
      documentRevision,
      checkpointRevision,
      normalizedUpdates
    );
    this.onRemoteChange();
  }

  mergeYjsSyncState(stateBase64, updates, documentRevision, checkpointRevision) {
    if (!this.ydoc || !this.ytext) return false;
    const normalizedUpdates = this.applyYjsSyncPayload('', stateBase64, updates);
    this.resetDocumentRevisionState(
      documentRevision,
      checkpointRevision,
      normalizedUpdates
    );
    if (this.model && this.model.getValue() !== this.ytext.toString()) {
      console.error('Monaco and Yjs diverged after reconnect synchronization.');
      return false;
    }
    this.onRemoteChange();
    return true;
  }

  applyRemoteCursorAdjustment(changes) {
    if (!changes || !changes.length || !this.model) return;
    const sorted = [...changes].sort(
      (a, b) => (a.rangeOffset ?? 0) - (b.rangeOffset ?? 0)
    );
    for (const change of sorted) {
      const start =
        typeof change.rangeOffset === 'number'
          ? change.rangeOffset
          : this.model.getOffsetAt({
              lineNumber: change.range.startLineNumber,
              column: change.range.startColumn
            });
      const length =
        typeof change.rangeLength === 'number'
          ? change.rangeLength
          : this.model.getOffsetAt({
              lineNumber: change.range.endLineNumber,
              column: change.range.endColumn
            }) - start;
      const end = start + length;
      const delta = (change.text || '').length - length;

      for (const [name, offset] of Object.entries(this.cursorPositions)) {
        if (name === this.getDisplayName() || typeof offset !== 'number') continue;
        if (offset <= start) continue;
        if (offset >= end) {
          this.cursorPositions[name] = offset + delta;
        } else {
          this.cursorPositions[name] = start + Math.max(0, (change.text || '').length);
        }
      }
    }
  }

  handleYjsUpdated(updateBase64, documentRevision, roomVersion, author) {
    this.onRevision(roomVersion);
    if (!this.ydoc) return;
    if (author && author !== this.getDisplayName()) this.onRemoteAuthor(author);
    const deliveredRevision = Number(documentRevision);
    if (
      Number.isSafeInteger(deliveredRevision) &&
      deliveredRevision > 0 &&
      deliveredRevision <= this.currentDocumentRevision
    )
      return;
    const update = uint8FromBase64(updateBase64);
    if (update.length === 0) return;
    this.applyYjsUpdate(update, remoteYjsOrigin);
    const revision = Number(documentRevision);
    this.checkpointJournal.noteUpdate(
      revision,
      updateBase64.length,
      author === this.getDisplayName(),
      this.highestDocumentRevisionSeen
    );
    this.noteDocumentRevision(documentRevision);
    if (this.model && this.ytext && this.model.getValue() !== this.ytext.toString()) {
      console.error('Monaco and Yjs diverged after applying a room update.', {
        documentRevision
      });
    }
  }

  flushBufferedYjsMessages() {
    if (this.isSynchronizingYjs || this.bufferedYjsMessages.length === 0) return;
    const messages = this.bufferedYjsMessages
      .splice(0, this.bufferedYjsMessages.length)
      .sort(
        (left, right) => Number(left.documentRevision) - Number(right.documentRevision)
      );
    for (const message of messages) {
      this.handleYjsUpdated(
        message.updateBase64,
        message.documentRevision,
        message.roomVersion,
        message.author
      );
    }
  }

  receiveUpdate(updateBase64, documentRevision, roomVersion, author) {
    if (this.isSynchronizingYjs) {
      if (
        this.bufferedYjsMessages.length >= 2048 ||
        this.bufferedYjsMessages.reduce(
          (sum, item) => sum + item.updateBase64.length,
          0
        ) +
          updateBase64.length >
          maxPendingBytes
      ) {
        // The authoritative journal is retained; discard only this delivery buffer and recover.
        this.bufferedYjsMessages.length = 0;
        this.onRecovery(250);
        return;
      }
      this.bufferedYjsMessages.push({
        updateBase64,
        documentRevision,
        roomVersion,
        author
      });
      return;
    }
    this.handleYjsUpdated(updateBase64, documentRevision, roomVersion, author);
  }
}
