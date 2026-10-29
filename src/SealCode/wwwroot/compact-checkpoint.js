/** Encodes the visible CRDT state without retaining the author's undo-only content. */
/** @param {typeof import('yjs')} Y
 * @param {import('yjs').Doc} source */
export function compactCheckpoint(Y, source) {
  const compact = new Y.Doc();
  try {
    Y.applyUpdate(compact, Y.encodeStateAsUpdate(source));
    return {
      state: Y.encodeStateAsUpdate(compact),
      text: compact.getText('monaco').toString()
    };
  } finally {
    compact.destroy();
  }
}
