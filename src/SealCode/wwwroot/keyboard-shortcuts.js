/** Recognizes the shortcut without browser globals. */
/** @param {Partial<KeyboardEvent>} event */
export function isCopyShortcut(event) {
  if (!event) return false;
  const key = typeof event.key === 'string' ? event.key.toLowerCase() : '';
  return key === 'c' && (event.ctrlKey || event.metaKey) && !event.altKey;
}

/** Recognizes the shortcut without browser globals. */
/** @param {Partial<KeyboardEvent>} event */
export function isUndoShortcut(event) {
  if (!event) return false;
  if (!(event.ctrlKey || event.metaKey) || event.altKey) return false;
  const key = typeof event.key === 'string' ? event.key.toLowerCase() : '';
  const code = typeof event.code === 'string' ? event.code.toLowerCase() : '';
  const isUndoKey = key === 'z' || code === 'keyz';
  return isUndoKey && !event.shiftKey;
}

/** Recognizes the shortcut without browser globals. */
/** @param {Partial<KeyboardEvent>} event */
export function isRedoShortcut(event) {
  if (!event) return false;
  if (!(event.ctrlKey || event.metaKey) || event.altKey) return false;
  const key = typeof event.key === 'string' ? event.key.toLowerCase() : '';
  const code = typeof event.code === 'string' ? event.code.toLowerCase() : '';
  const isRedoKey = key === 'y' || code === 'keyy';
  const isShiftedUndoKey = (key === 'z' || code === 'keyz') && event.shiftKey;
  return isRedoKey || isShiftedUndoKey;
}

/** Recognizes the shortcut without browser globals. */
/** @param {Partial<KeyboardEvent>} event */
export function isUndoRedoShortcut(event) {
  return isUndoShortcut(event) || isRedoShortcut(event);
}

/** Recognizes the shortcut without browser globals. */
/** @param {Partial<KeyboardEvent>} event */
export function isPasteShortcut(event) {
  if (!event) return false;
  const key = typeof event.key === 'string' ? event.key.toLowerCase() : '';
  return key === 'v' && (event.ctrlKey || event.metaKey) && !event.altKey;
}
