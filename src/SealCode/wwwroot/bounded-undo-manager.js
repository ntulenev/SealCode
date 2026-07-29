const DEFAULT_MAX_DEPTH = 100;
const DEFAULT_CAPTURE_TIMEOUT_MS = 500;
const DEFAULT_MAX_ESTIMATED_BYTES = 8 * 1024 * 1024;

/**
 * Bounds the user-visible Yjs undo stack without releasing CRDT items that may
 * still be referenced by newer undo entries. Discarded entries are retained
 * until the whole history can be cleared safely.
 */
export class BoundedUndoManager {
  constructor(Y, scope, options) {
    if (!Y?.UndoManager) throw new TypeError('Y.UndoManager is required.');
    if (!scope) throw new TypeError('A Yjs scope is required.');
    if (!options?.trackedOrigin) throw new TypeError('A tracked origin is required.');

    this.maxDepth = options.maxDepth ?? DEFAULT_MAX_DEPTH;
    this.maxEstimatedBytes = options.maxEstimatedBytes ?? DEFAULT_MAX_ESTIMATED_BYTES;
    this.trackedOrigin = options.trackedOrigin;
    this.discardedStackItems = [];
    this.estimatedBytes = 0;
    this.listeners = new Set();
    this.lastClearReason = null;

    if (!Number.isInteger(this.maxDepth) || this.maxDepth < 1) {
      throw new RangeError('maxDepth must be a positive integer.');
    }
    if (!Number.isFinite(this.maxEstimatedBytes) || this.maxEstimatedBytes < 1) {
      throw new RangeError('maxEstimatedBytes must be positive.');
    }

    this.undoManager = new Y.UndoManager(scope, {
      captureTimeout: options.captureTimeout ?? DEFAULT_CAPTURE_TIMEOUT_MS,
      trackedOrigins: new Set([this.trackedOrigin])
    });

    this.handleStackItemAdded = (event) => {
      if (event.origin === this.trackedOrigin && event.type === 'undo') {
        this.trimVisibleUndoStack();
      }
      this.emitChange();
    };
    this.handleStackChanged = () => this.emitChange();
    this.undoManager.on('stack-item-added', this.handleStackItemAdded);
    this.undoManager.on('stack-item-updated', this.handleStackChanged);
    this.undoManager.on('stack-item-popped', this.handleStackChanged);
    this.undoManager.on('stack-cleared', this.handleStackChanged);
  }

  get undoDepth() {
    return this.undoManager.undoStack.length;
  }

  get redoDepth() {
    return this.undoManager.redoStack.length;
  }

  canUndo() {
    return this.undoManager.canUndo();
  }

  canRedo() {
    return this.undoManager.canRedo();
  }

  undo() {
    return this.undoManager.undo();
  }

  redo() {
    return this.undoManager.redo();
  }

  stopCapturing() {
    this.undoManager.stopCapturing();
  }

  replaceTrackedOrigin(origin) {
    if (!origin) throw new TypeError('A tracked origin is required.');
    this.undoManager.removeTrackedOrigin(this.trackedOrigin);
    this.trackedOrigin = origin;
    this.undoManager.addTrackedOrigin(origin);
  }

  recordLocalUpdateSize(byteLength) {
    if (!Number.isFinite(byteLength) || byteLength <= 0) return false;
    this.estimatedBytes += byteLength;
    if (this.estimatedBytes <= this.maxEstimatedBytes) return false;
    this.clear('memory-limit');
    return true;
  }

  onChange(listener) {
    if (typeof listener !== 'function') throw new TypeError('listener must be a function.');
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  clear(reason = 'manual') {
    if (this.discardedStackItems.length > 0) {
      this.undoManager.undoStack.unshift(...this.discardedStackItems);
      this.discardedStackItems = [];
    }
    this.undoManager.clear(true, true);
    this.estimatedBytes = 0;
    this.lastClearReason = reason;
    this.emitChange();
  }

  destroy() {
    this.clear('destroy');
    this.undoManager.off('stack-item-added', this.handleStackItemAdded);
    this.undoManager.off('stack-item-updated', this.handleStackChanged);
    this.undoManager.off('stack-item-popped', this.handleStackChanged);
    this.undoManager.off('stack-cleared', this.handleStackChanged);
    this.undoManager.destroy();
    this.listeners.clear();
  }

  trimVisibleUndoStack() {
    while (this.undoManager.undoStack.length > this.maxDepth) {
      const discarded = this.undoManager.undoStack.shift();
      if (discarded) this.discardedStackItems.push(discarded);
    }
  }

  emitChange() {
    const state = {
      canUndo: this.canUndo(),
      canRedo: this.canRedo(),
      undoDepth: this.undoDepth,
      redoDepth: this.redoDepth,
      lastClearReason: this.lastClearReason
    };
    for (const listener of this.listeners) listener(state);
  }
}
