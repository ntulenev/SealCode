/** Applies one Y.Text delta to the read-only model, preserving unchanged text.
 * @param {import('monaco-editor').editor.ITextModel} model
 * @param {import('yjs').Text} text
 * @param {Array<{retain?: number, insert?: string | object, delete?: number}>} delta
 */
export function applyObserverTextDelta(model, text, delta) {
  // Minimal read-only model adapters can still accept authoritative text.
  if (typeof model.applyEdits !== 'function' || typeof model.getPositionAt !== 'function'
      || typeof model.getValue !== 'function') {
    model.setValue(text.toString());
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
  model.applyEdits(edits);
  // A missed change or unsupported embedded value must never leave a stale view.
  const expected = text.toString();
  if (model.getValue() !== expected) model.setValue(expected);
}
