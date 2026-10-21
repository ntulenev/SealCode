const DEFAULT_MAX_DEPTH = 100;
const DEFAULT_CAPTURE_TIMEOUT_MS = 500;
const DEFAULT_MAX_ESTIMATED_BYTES = 8 * 1024 * 1024;
const ESTIMATED_ITEM_OVERHEAD_BYTES = 128;

/**
 * Bounds the user-visible Yjs undo stack without releasing CRDT items that may
 * still be referenced by newer undo entries. Discarded entries are retained
 * until the whole history can be cleared safely.
 */
export class BoundedUndoManager {
  /**
   * @param {typeof import('yjs')} Y
   * @param {import('yjs').Text} scope
   * @param {{trackedOrigin: unknown, maxDepth?: number, maxEstimatedBytes?: number, captureTimeout?: number}} options
   */
  constructor(Y, scope, options) {
    if (!Y?.UndoManager) throw new TypeError('Y.UndoManager is required.');
    if (!scope) throw new TypeError('A Yjs scope is required.');
    if (!options?.trackedOrigin) throw new TypeError('A tracked origin is required.');

    this.maxDepth = options.maxDepth ?? DEFAULT_MAX_DEPTH;
    this.Y = Y;
    this.maxEstimatedBytes = options.maxEstimatedBytes ?? DEFAULT_MAX_ESTIMATED_BYTES;
    this.trackedOrigin = options.trackedOrigin;
    /** @type {import('yjs').UndoManager['undoStack']} */
    this.discardedStackItems = [];
    this.retainedStackItems = new Set();
    this.estimatedBytes = 0;
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

    /** @param {{stackItem: import('yjs').UndoManager['undoStack'][number], origin: unknown, type: string}} event */
    this.handleStackItemAdded = (event) => {
      // Popped entries can still retain the original items behind redo links.
      // Remember them until a complete clear can release all keep flags.
      this.retainedStackItems.add(event.stackItem);
      if (event.origin === this.trackedOrigin && event.type === 'undo') {
        this.trimVisibleUndoStack();
      }
    };
    this.undoManager.on('stack-item-added', this.handleStackItemAdded);

    // Update payloads contain only IDs for deletions. Account for the actual
    // retained text before Yjs garbage collection can discard its content.
    /** @param {import('yjs').Transaction} transaction */
    this.handleAfterTransaction = (transaction) => {
      if (!this.isTrackedOrigin(transaction.origin)) return;
      Y.iterateDeletedStructs(transaction, transaction.deleteSet, (item) => {
        if (!(item instanceof Y.Item) || !Y.isParentOf(scope, item)) return;
        const textBytes =
          item.content instanceof Y.ContentString ? item.content.str.length * 2 : 0;
        this.estimatedBytes += textBytes + ESTIMATED_ITEM_OVERHEAD_BYTES;
      });
    };
    /** @param {Uint8Array} update
     * @param {unknown} origin */
    this.handleDocumentUpdate = (update, origin) => {
      if (this.isTrackedOrigin(origin)) this.recordLocalUpdateSize(update.length);
    };
    this.undoManager.doc.on('afterTransaction', this.handleAfterTransaction);
    this.undoManager.doc.on('update', this.handleDocumentUpdate);
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

  /**
   * @param {number} byteLength
   */
  recordLocalUpdateSize(byteLength) {
    if (!Number.isFinite(byteLength) || byteLength <= 0) return false;
    // A conservative cumulative budget, including undo/redo and discarded
    // entries. Do not subtract entries while other history may reference them.
    this.estimatedBytes += byteLength * 2 + ESTIMATED_ITEM_OVERHEAD_BYTES;
    if (this.estimatedBytes <= this.maxEstimatedBytes) return false;
    this.clear('memory-limit');
    return true;
  }

  clear(reason = 'manual') {
    this.undoManager.undoStack = [
      ...new Set([
        ...this.retainedStackItems,
        ...this.discardedStackItems,
        ...this.undoManager.undoStack
      ])
    ];
    this.retainedStackItems.clear();
    this.discardedStackItems = [];
    const releasedDeletions = this.Y.mergeDeleteSets([
      ...this.undoManager.undoStack.map((item) => item.deletions),
      ...this.undoManager.redoStack.map((item) => item.deletions)
    ]);
    this.undoManager.clear(true, true);
    // clear() releases keep flags, but old transactions have already passed
    // Yjs GC. Collect their deleted content now that no history references it.
    const doc = this.undoManager.doc;
    if (doc.gc) this.Y.tryGc(releasedDeletions, doc.store, doc.gcFilter);
    this.estimatedBytes = 0;
    this.lastClearReason = reason;
  }

  destroy() {
    this.undoManager.doc.off('afterTransaction', this.handleAfterTransaction);
    this.undoManager.doc.off('update', this.handleDocumentUpdate);
    this.clear('destroy');
    this.undoManager.off('stack-item-added', this.handleStackItemAdded);
    this.undoManager.destroy();
  }

  trimVisibleUndoStack() {
    while (this.undoManager.undoStack.length > this.maxDepth) {
      const discarded = this.undoManager.undoStack.shift();
      if (discarded) this.discardedStackItems.push(discarded);
    }
  }

  /**
   * @param {unknown} origin
   */
  isTrackedOrigin(origin) {
    return origin === this.trackedOrigin || origin === this.undoManager;
  }
}
