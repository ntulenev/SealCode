import { bounds } from './whiteboard-model.js';
import { layoutText } from './whiteboard-text.js';

/** @type {WeakMap<Document, CanvasRenderingContext2D | null>} */
const contexts = new WeakMap();

/** @param {import('./whiteboard-model.js').Shape} s
 * @param {Document} document */
export function createShapeElement(s, document) {
  /** @param {string} tag @param {Record<string, string | number>} [attributes] */
  function node(tag, attributes = {}) {
    const element = document.createElementNS('http://www.w3.org/2000/svg', tag);
    for (const [key, value] of Object.entries(attributes)) element.setAttribute(key, String(value));
    return element;
  }
  const group = node('g', { 'data-shape-id': s.id, 'data-shape-type': s.type, class: 'board-shape' });
  const { x, y, width: w, height: h } = bounds(s);
  const cy = y + h / 2;
  if (['arrow', 'bidirectional-arrow', 'line'].includes(s.type)) {
    const ex = s.x + s.width, ey = s.y + s.height;
    group.append(node('line', { x1: s.x, y1: s.y, x2: ex, y2: ey, class: 'board-line-hit' }));
    group.append(node('line', { x1: s.x, y1: s.y, x2: ex, y2: ey }));
    if (s.type !== 'line') {
      const angle = Math.atan2(s.height, s.width);
      const tips = s.type === 'bidirectional-arrow' ? [[ex, ey, angle], [s.x, s.y, angle + Math.PI]] : [[ex, ey, angle]];
      for (const [tx, ty, direction] of tips) {
        const a = direction - Math.PI / 6, b = direction + Math.PI / 6;
        group.append(node('path', { d: `M ${tx - 15 * Math.cos(a)} ${ty - 15 * Math.sin(a)} L ${tx} ${ty} L ${tx - 15 * Math.cos(b)} ${ty - 15 * Math.sin(b)}`, fill: 'none' }));
      }
    }
  } else if (s.type === 'ellipse') {
    group.append(node('ellipse', { cx: x + w / 2, cy, rx: w / 2, ry: h / 2, class: 'board-fill' }));
  } else if (s.type === 'diamond') {
    group.append(node('path', { d: `M ${x + w / 2} ${y} L ${x + w} ${cy} L ${x + w / 2} ${y + h} L ${x} ${cy} Z`, class: 'board-fill' }));
  } else if (s.type === 'database') {
    const ry = Math.min(16, h / 5);
    group.append(node('path', { d: `M ${x} ${y + ry} a ${w / 2} ${ry} 0 0 1 ${w} 0 V ${y + h - ry} a ${w / 2} ${ry} 0 0 1 ${-w} 0 Z`, class: 'board-fill' }));
    group.append(node('ellipse', { cx: x + w / 2, cy: y + ry, rx: w / 2, ry, fill: 'none' }));
  } else {
    group.append(node('rect', { x, y, width: Math.max(1, w), height: Math.max(1, h), rx: s.type === 'text' ? 0 : 6, class: s.type === 'text' ? 'board-text-hit' : 'board-fill' }));
    if (s.type === 'queue') {
      for (const fraction of [0.15, 0.3, 0.7, 0.85]) group.append(node('line', { x1: x + w * fraction, y1: y + 8, x2: x + w * fraction, y2: y + h - 8 }));
    }
  }
  if (!contexts.has(document)) contexts.set(document, document.createElement('canvas').getContext?.('2d') ?? null);
  const context = contexts.get(document);
  const family = document.defaultView?.getComputedStyle?.(document.documentElement).getPropertyValue('--sans') || 'sans-serif';
  const connector = ['arrow', 'bidirectional-arrow', 'line'].includes(s.type);
  const width = connector ? Math.max(160, w) : Math.max(1, w - 24);
  const height = connector ? Math.max(60, h) : Math.max(1, h - 16);
  const measure = context ? (/** @type {string} */ value, /** @type {number} */ size) => {
    context.font = `${size}px ${family}`;
    return context.measureText(value).width;
  } : undefined;
  const { lines, fontSize, lineHeight } = layoutText(s.text, width, height, measure);
  const text = node('text', { x: x + w / 2, y: cy - (lines.length - 1) * lineHeight / 2 + fontSize / 3, 'text-anchor': 'middle', class: 'board-label' });
  text.setAttribute('style', `font-size: ${fontSize}px`);
  for (let i = 0; i < lines.length; i++) {
    const span = node('tspan', { x: x + w / 2, dy: i ? lineHeight : 0 });
    span.textContent = lines[i];
    text.append(span);
  }
  if (lines.length * lineHeight > height || width < fontSize) {
    // Very small boxes keep their full stored text while clipping visual overflow.
    const left = x + (w - width) / 2, top = cy - height / 2;
    const clip = node('svg', { x: left, y: top, width, height, overflow: 'hidden', 'aria-label': s.text });
    text.setAttribute('transform', `translate(${-left} ${-top})`);
    clip.append(text);
    group.append(clip);
  } else group.append(text);
  return group;
}
