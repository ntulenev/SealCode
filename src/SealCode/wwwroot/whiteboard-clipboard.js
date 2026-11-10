import { isShape } from './whiteboard-model.js';

/** Validates an entire clipboard diagram before any shape is inserted.
 * @param {string} content
 * @returns {import('./whiteboard-model.js').Shape[] | null} */
export function parseWhiteboardClipboard(content) {
  if (content.length > 100000) return null;
  let payload;
  try { payload = JSON.parse(content); } catch { return null; }
  if (!payload || payload.format !== 'sealcode-whiteboard' || payload.version !== 1 ||
    !Array.isArray(payload.shapes) || payload.shapes.length < 1 || payload.shapes.length > 100 ||
    !payload.shapes.every(isShape)) return null;
  return payload.shapes;
}
