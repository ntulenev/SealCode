/** @typedef {{id: string, type: string, x: number, y: number, width: number, height: number, text: string}} Shape */
export const shapeTypes = ['rectangle', 'arrow', 'line', 'text', 'database', 'queue', 'ellipse', 'diamond'];

/** Validates untrusted shared data before rendering it in the SVG workspace.
 * @param {unknown} value
 * @returns {value is Shape} */
export function isShape(value) {
  const s = /** @type {Shape} */ (value);
  return !!s && typeof s.id === 'string' && s.id.length <= 100 &&
    shapeTypes.includes(s.type) && typeof s.text === 'string' && s.text.length <= 4000 &&
    [s.x, s.y, s.width, s.height].every((n) => Number.isFinite(n) && Math.abs(n) <= 1e7);
}

/** @param {Shape} shape */
export function bounds(shape) {
  return {
    x: Math.min(shape.x, shape.x + shape.width),
    y: Math.min(shape.y, shape.y + shape.height),
    width: Math.abs(shape.width), height: Math.abs(shape.height)
  };
}

/** @param {Shape} shape */
export function normalizeShape(shape) {
  return ['arrow', 'line'].includes(shape.type) ? shape : { ...shape, ...bounds(shape) };
}

/** The origin is local to a binding, so undo never tracks a remote participant.
 * @param {import('yjs').Doc} doc
 * @param {() => void} render */
export function bindWhiteboard(doc, render) {
  const scope = doc.getMap('whiteboard');
  scope.observe(render);
  render();
  return { scope, destroy: () => scope.unobserve(render) };
}
