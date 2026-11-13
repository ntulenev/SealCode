/** Applies one Y.Text delta to the read-only model, preserving unchanged text.
 * @param {import('monaco-editor').editor.ITextModel} model
 * @param {import('yjs').Text} text
 * @param {Array<{retain?: number, insert?: string | object, delete?: number}>} delta
 * @param {boolean} [reconcile] Whether to repair immediately or at the message boundary.
 */
export function applyObserverTextDelta(model, text, delta, reconcile = true) {
  // Minimal read-only model adapters can still accept authoritative text.
  if (typeof model.applyEdits !== 'function' || typeof model.getPositionAt !== 'function'
      || typeof model.getValue !== 'function') {
    if (reconcile) reconcileObserverText(model, text);
    return;
  }
  let offset = 0;
  let start = 0;
  let deleted = 0;
  let inserted = '';
  /** @type {import('monaco-editor').editor.IIdentifiedSingleEditOperation[]} */
  const edits = [];
  function flush() {
    if (!deleted && !inserted) return;
    const from = model.getPositionAt(start);
    const to = model.getPositionAt(start + deleted);
    edits.push({ range: {
      startLineNumber: from.lineNumber, startColumn: from.column,
      endLineNumber: to.lineNumber, endColumn: to.column
    }, text: inserted });
    deleted = 0;
    inserted = '';
  }
  for (const change of delta) {
    if (change.retain) {
      flush();
      offset += change.retain;
      start = offset;
    }
    if (typeof change.insert === 'string') inserted += change.insert;
    if (change.delete) {
      deleted += change.delete;
      offset += change.delete;
    }
  }
  flush();
  try {
    model.applyEdits(edits);
  } catch {
    // A stale range can be rejected by Monaco. The authoritative text is intact.
    if (reconcile) model.setValue(text.toString());
    return;
  }
  if (reconcile) reconcileObserverText(model, text);
}

/** Repairs a divergent read-only view once after a complete transport message.
 * @param {import('monaco-editor').editor.ITextModel} model
 * @param {import('yjs').Text} text
 * @returns {boolean} Whether a full-text repair was necessary.
 */
export function reconcileObserverText(model, text) {
  const expected = text.toString();
  if (typeof model.getValue === 'function' && model.getValue() === expected) return false;
  model.setValue(expected);
  return true;
}
