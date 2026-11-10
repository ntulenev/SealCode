import { bindWhiteboard, bounds, isShape, normalizeShape, shapeTypes } from './whiteboard-model.js';

const NS = 'http://www.w3.org/2000/svg';
const tools = [
  ['select', 'Select', 'V', '↖'], ['pan', 'Pan', 'H', '✥'],
  ['rectangle', 'Rectangle', 'R', '▭'], ['arrow', 'Arrow', 'A', '↗'],
  ['bidirectional-arrow', 'Bidirectional arrow', 'B', '↔'],
  ['line', 'Line', 'L', '╱'], ['text', 'Text', 'T', 'T'],
  ['database', 'Database', 'D', '▤'], ['queue', 'Queue', 'Q', '▥'],
  ['ellipse', 'Ellipse', 'O', '○'], ['diamond', 'Diamond', 'M', '◇']
];

/** A dependency-free SVG workspace; only committed gestures enter the shared CRDT.
 * @param {HTMLElement} host */
export function createWhiteboardView(host) {
  const document = host.ownerDocument;
  const win = document.defaultView;
  /** @type {ReturnType<typeof bindWhiteboard> | null} */
  let binding = null;
  /** @type {object | null} */
  let undoOrigin = null;
  let tool = 'select';
  let selected = '';
  let pastedContent = '';
  let pasteCount = 0;
  /** @type {{id: string, time: number} | null} */
  let lastClick = null;
  let space = false;
  let zoom = 1;
  let offset = { x: 0, y: 0 };
  /** @type {{start: {x: number, y: number}, original?: import('./whiteboard-model.js').Shape, draft?: import('./whiteboard-model.js').Shape, mode: string, pointerId: number, offset: {x: number, y: number}} | null} */
  let gesture = null;
  /** @type {HTMLTextAreaElement | null} */
  let textEditor = null;
  let cancelText = () => {};

  const toolbar = document.createElement('div');
  toolbar.className = 'board-toolbar';
  toolbar.setAttribute('role', 'toolbar');
  toolbar.setAttribute('aria-label', 'Drawing tools');
  host.append(toolbar);
  const svg = /** @type {SVGSVGElement} */ (node('svg', { tabindex: '0', role: 'application', 'aria-label': 'Whiteboard canvas. Select a tool to draw; Space and drag to pan.' }));
  svg.classList.add('board-canvas');
  const layer = node('g');
  svg.append(layer);
  host.append(svg);
  const hint = document.createElement('p');
  hint.className = 'board-hint';
  hint.textContent = 'Drag to draw · Double-click to label · Ctrl+C / Ctrl+V to copy / paste · Space + drag to pan · Ctrl + wheel to zoom';
  host.append(hint);
  const controls = document.createElement('div');
  controls.className = 'board-controls';
  controls.setAttribute('aria-label', 'Whiteboard actions');
  host.append(controls);
  /** @type {Map<string, HTMLButtonElement>} */
  const toolButtons = new Map();
  for (const [name, label, key, icon] of tools) {
    const button = addButton(toolbar, `${label} (${key})`, icon, () => setTool(name));
    button.dataset.tool = name;
    toolButtons.set(name, button);
  }
  const undoButton = addButton(controls, 'Undo', '↶', () => api.undo());
  const redoButton = addButton(controls, 'Redo', '↷', () => api.redo());
  const deleteButton = addButton(controls, 'Delete selected', '⌫', removeSelected);
  const labelButton = addButton(controls, 'Edit label', 'Label', () => editLabel(getSelected()));
  addButton(controls, 'Zoom out', '−', () => zoomAt(zoom / 1.2));
  const zoomButton = addButton(controls, 'Reset view', '100%', () => { zoom = 1; offset = { x: 0, y: 0 }; render(); });
  addButton(controls, 'Zoom in', '+', () => zoomAt(zoom * 1.2));
  addButton(controls, 'Fit diagram', 'Fit', fit);
  addButton(controls, 'Download board', '↓ JSON', download);

  const api = {
    readOnly: true,
    undo: () => false,
    redo: () => false,
    beforeChange: () => {},
    onChange: () => {},
    /** @param {boolean} value */
    setReadOnly(value) {
      api.readOnly = value;
      if (value) { gesture = null; cancelText(); }
      render();
    },
    /** @param {import('yjs').Doc} doc */
    bind(doc) {
      const current = bindWhiteboard(doc, render);
      binding = current;
      render();
      const result = { scope: current.scope, destroy() {
        current.destroy();
        cancelText();
        gesture = null;
        if (binding === current) binding = null;
        render();
      } };
      undoOrigin = result;
      return result;
    },
    download
  };
  setTool('select');

  /** @param {string} tag @param {Record<string, string | number>} [attributes] */
  function node(tag, attributes = {}) {
    const element = document.createElementNS(NS, tag);
    for (const [key, value] of Object.entries(attributes)) element.setAttribute(key, String(value));
    return element;
  }
  /** @param {HTMLElement} parent @param {string} label @param {string} text @param {() => void} click */
  function addButton(parent, label, text, click) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'ghost';
    button.title = label;
    button.setAttribute('aria-label', label);
    button.textContent = text;
    button.addEventListener('click', click);
    parent.append(button);
    return button;
  }
  /** @param {string} name */
  function setTool(name) {
    tool = name;
    for (const [key, button] of toolButtons) button.setAttribute('aria-pressed', String(key === tool));
    svg.dataset.tool = tool;
  }
  function shapes() {
    return binding ? [...binding.scope.values()].filter(isShape) : [];
  }
  function getSelected() {
    const shape = binding?.scope.get(selected);
    return isShape(shape) ? shape : null;
  }
  /** @param {import('./whiteboard-model.js').Shape} shape */
  function commit(shape) {
    if (!binding || api.readOnly || !isShape(shape)) return;
    api.beforeChange();
    binding.scope.doc.transact(() => binding.scope.set(shape.id, normalizeShape(shape)), undoOrigin);
    api.onChange();
  }
  function removeSelected() {
    if (api.readOnly || !binding || !selected) return;
    api.beforeChange();
    binding.scope.doc.transact(() => binding.scope.delete(selected), undoOrigin);
    selected = '';
    api.onChange();
    render();
  }
  /** @param {{clientX: number, clientY: number}} event */
  function local(event) {
    const rect = svg.getBoundingClientRect();
    return { x: event.clientX - rect.left, y: event.clientY - rect.top };
  }
  /** @param {{x: number, y: number}} point */
  function world(point) { return { x: (point.x - offset.x) / zoom, y: (point.y - offset.y) / zoom }; }
  /** @param {number} value @param {{x: number, y: number}} [point] */
  function zoomAt(value, point = { x: svg.clientWidth / 2, y: svg.clientHeight / 2 }) {
    cancelText();
    const before = world(point);
    zoom = Math.max(0.1, Math.min(4, value));
    offset = { x: point.x - before.x * zoom, y: point.y - before.y * zoom };
    render();
  }
  function fit() {
    const all = shapes().map(bounds);
    if (!all.length) { zoomAt(1); return; }
    const x = Math.min(...all.map((s) => s.x));
    const y = Math.min(...all.map((s) => s.y));
    const width = Math.max(...all.map((s) => s.x + s.width)) - x;
    const height = Math.max(...all.map((s) => s.y + s.height)) - y;
    zoom = Math.max(0.1, Math.min(2, (svg.clientWidth - 120) / Math.max(1, width), (svg.clientHeight - 160) / Math.max(1, height)));
    offset = { x: (svg.clientWidth - width * zoom) / 2 - x * zoom, y: (svg.clientHeight - height * zoom) / 2 - y * zoom };
    cancelText();
    render();
  }
  function render() {
    layer.replaceChildren();
    layer.setAttribute('transform', `translate(${offset.x} ${offset.y}) scale(${zoom})`);
    const all = shapes();
    const draft = gesture?.draft;
    for (const shape of all) draw(draft?.id === shape.id ? draft : shape);
    if (draft && !all.some((s) => s.id === draft.id)) draw(draft);
    const active = draft ?? getSelected();
    if (active && !api.readOnly) {
      const b = bounds(active);
      layer.append(node('rect', { x: b.x - 5 / zoom, y: b.y - 5 / zoom, width: Math.max(1, b.width) + 10 / zoom, height: Math.max(1, b.height) + 10 / zoom, class: 'board-selection' }));
      const handle = node('rect', { x: active.x + active.width - 6 / zoom, y: active.y + active.height - 6 / zoom, width: 12 / zoom, height: 12 / zoom, class: 'board-resize', 'data-resize': active.id });
      layer.append(handle);
    }
    undoButton.disabled = redoButton.disabled = api.readOnly;
    deleteButton.disabled = labelButton.disabled = api.readOnly || !getSelected();
    for (const [name, button] of toolButtons) button.disabled = api.readOnly && !['select', 'pan'].includes(name);
    zoomButton.textContent = `${Math.round(zoom * 100)}%`;
    host.classList.toggle('board-readonly', api.readOnly);
    hint.hidden = all.length > 0;
  }
  /** @param {import('./whiteboard-model.js').Shape} s */
  function draw(s) {
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
    const lines = s.text.split('\n');
    const text = node('text', { x: x + w / 2, y: cy - (lines.length - 1) * 11 + 6, 'text-anchor': 'middle', class: 'board-label' });
    for (let i = 0; i < lines.length; i++) {
      const span = node('tspan', { x: x + w / 2, dy: i ? 22 : 0 });
      span.textContent = lines[i];
      text.append(span);
    }
    group.append(text);
    layer.append(group);
  }
  /** @param {import('./whiteboard-model.js').Shape | null} shape */
  function editLabel(shape) {
    if (!shape || api.readOnly || !binding) return;
    cancelText();
    const input = document.createElement('textarea');
    textEditor = input;
    input.className = 'board-label-editor';
    input.setAttribute('aria-label', 'Element label');
    input.maxLength = 4000;
    input.value = shape.text;
    const b = bounds(shape);
    input.style.left = `${Math.max(4, Math.min(svg.clientWidth - 220, offset.x + b.x * zoom))}px`;
    input.style.top = `${Math.max(60, Math.min(svg.clientHeight - 100, offset.y + b.y * zoom))}px`;
    input.style.width = `${Math.max(200, Math.min(svg.clientWidth - 8, b.width * zoom))}px`;
    host.append(input);
    let finished = false;
    /** @param {boolean} save */
    const finish = (save) => {
      if (finished) return;
      finished = true;
      const current = binding?.scope.get(shape.id);
      if (save && isShape(current) && input.value !== current.text) {
        const text = input.value;
        const size = current.type === 'text' ? { width: Math.max(80, ...text.split('\n').map((line) => line.length * 11)), height: Math.max(40, text.split('\n').length * 24) } : {};
        commit({ ...current, ...size, text });
      }
      input.remove();
      textEditor = null;
      render();
    };
    cancelText = () => finish(false);
    input.addEventListener('blur', () => finish(true));
    input.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') { event.preventDefault(); finish(false); svg.focus(); }
      if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) { event.preventDefault(); finish(true); svg.focus(); }
    });
    input.focus();
    input.select();
  }
  svg.addEventListener('pointerdown', (event) => {
    // Keep one gesture owner: a second touch must not replace its draft.
    if (gesture) return;
    if (event.button !== 0 && event.button !== 1) return;
    if (textEditor) textEditor.blur();
    const start = local(event);
    const target = /** @type {Element} */ (event.target);
    const id = target.closest('[data-shape-id]')?.getAttribute('data-shape-id') ?? '';
    const resize = target.getAttribute('data-resize');
    const pan = event.button === 1 || space || tool === 'pan';
    if (api.readOnly && !pan) return;
    svg.focus();
    event.preventDefault();
    svg.setPointerCapture(event.pointerId);
    gesture = { start, mode: pan ? 'pan' : tool, pointerId: event.pointerId, offset: { ...offset } };
    if (pan) return;
    if (tool === 'select' || resize) {
      selected = resize || id;
      const shape = getSelected();
      if (shape) { gesture.original = shape; gesture.draft = { ...shape }; gesture.mode = resize ? 'resize' : 'move'; }
    } else if (shapeTypes.includes(tool)) {
      const p = world(start);
      const type = tool;
      const shape = { id: win.crypto.randomUUID(), type, x: p.x, y: p.y, width: 0, height: 0, text: '' };
      gesture.draft = shape;
      gesture.mode = 'create';
      selected = shape.id;
    }
    render();
  });
  svg.addEventListener('pointermove', (event) => {
    if (!gesture || gesture.pointerId !== event.pointerId) return;
    const point = local(event);
    const dx = (point.x - gesture.start.x) / zoom, dy = (point.y - gesture.start.y) / zoom;
    if (gesture.mode === 'pan') {
      offset = { x: gesture.offset.x + dx * zoom, y: gesture.offset.y + dy * zoom };
    } else if (gesture.draft) {
      const s = gesture.original;
      if (gesture.mode === 'move') gesture.draft = { ...s, x: s.x + dx, y: s.y + dy };
      else gesture.draft = { ...gesture.draft, width: (s?.width ?? 0) + dx, height: (s?.height ?? 0) + dy };
    }
    render();
  });
  svg.addEventListener('pointerup', (event) => {
    if (!gesture || gesture.pointerId !== event.pointerId) return;
    const { draft, original, mode } = gesture;
    const clicked = mode === 'move' && draft && original &&
      Math.hypot(draft.x - original.x, draft.y - original.y) < 3 / zoom;
    const now = Date.now();
    const doubleClick = clicked && lastClick?.id === original.id && now - lastClick.time < 500;
    lastClick = clicked && !doubleClick ? { id: original.id, time: now } : null;
    gesture = null;
    if (svg.hasPointerCapture(event.pointerId)) svg.releasePointerCapture(event.pointerId);
    if (draft && mode === 'create') {
      if (Math.abs(draft.width) + Math.abs(draft.height) < 8) Object.assign(draft, { width: draft.type === 'text' ? 160 : 150, height: draft.type === 'text' ? 40 : 90 });
      if (draft.type === 'text') draft.text = 'Text';
      commit(draft);
      setTool('select');
      if (draft.type === 'text') editLabel(draft);
    } else if (draft && original && (draft.x !== original.x || draft.y !== original.y || draft.width !== original.width || draft.height !== original.height)) {
      const current = binding?.scope.get(draft.id);
      if (isShape(current)) commit({ ...current, x: draft.x, y: draft.y, width: draft.width, height: draft.height });
    }
    if (doubleClick) editLabel(getSelected());
    render();
  });
  function cancelGesture() { gesture = null; render(); }
  /** @param {PointerEvent} event */
  function cancelPointer(event) {
    if (gesture?.pointerId === event.pointerId) cancelGesture();
  }
  svg.addEventListener('pointercancel', cancelPointer);
  svg.addEventListener('lostpointercapture', cancelPointer);
  svg.addEventListener('dblclick', (event) => {
    if (textEditor) return;
    const point = world(local(event));
    // A render can replace SVG children between the two clicks. Hit-test the
    // diagram as well as the event target so native double-clicks remain stable.
    const hit = shapes().reverse().find((shape) => {
      const b = bounds(shape);
      const margin = 6 / zoom;
      return point.x >= b.x - margin && point.x <= b.x + b.width + margin &&
        point.y >= b.y - margin && point.y <= b.y + b.height + margin;
    });
    const id = /** @type {Element} */ (event.target).closest('[data-shape-id]')?.getAttribute('data-shape-id') || hit?.id;
    if (id) { selected = id; editLabel(getSelected()); }
  });
  svg.addEventListener('wheel', (event) => {
    event.preventDefault();
    if (event.ctrlKey || event.metaKey) zoomAt(zoom * Math.exp(-event.deltaY * 0.003), local(event));
    else { offset.x -= event.deltaX; offset.y -= event.deltaY; render(); }
  }, { passive: false });
  /** @param {EventTarget | null} target */
  function isTextTarget(target) {
    return target instanceof win.HTMLTextAreaElement || target instanceof win.HTMLInputElement ||
      !!/** @type {HTMLElement | null} */ (target)?.isContentEditable;
  }
  // Native clipboard events also support platform shortcuts and keyboard layouts.
  host.addEventListener('copy', (event) => {
    if (isTextTarget(event.target) || gesture || !event.clipboardData) return;
    const shape = getSelected();
    if (!shape) return;
    const content = JSON.stringify({ format: 'sealcode-whiteboard', version: 1, shapes: [shape] });
    event.clipboardData.setData('text/plain', content);
    event.preventDefault();
    pastedContent = content;
    pasteCount = 0;
  });
  host.addEventListener('paste', (event) => {
    if (isTextTarget(event.target) || gesture || api.readOnly || !binding || !event.clipboardData) return;
    const content = event.clipboardData.getData('text/plain');
    // A single element cannot exceed this size under the shared shape limits.
    if (content.length > 100000) return;
    let payload;
    try { payload = JSON.parse(content); } catch { return; }
    if (!payload || payload.format !== 'sealcode-whiteboard' || payload.version !== 1 ||
      !Array.isArray(payload.shapes) || payload.shapes.length !== 1 || !isShape(payload.shapes[0])) return;
    const shape = payload.shapes[0];
    const count = content === pastedContent ? pasteCount + 1 : 1;
    const copy = { ...shape, id: win.crypto.randomUUID(), x: shape.x + count * 24, y: shape.y + count * 24 };
    if (!isShape(copy)) return;
    event.preventDefault();
    pastedContent = content;
    pasteCount = count;
    selected = copy.id;
    setTool('select');
    commit(copy);
    svg.focus();
  });
  host.addEventListener('keydown', (event) => {
    if (isTextTarget(event.target)) return;
    // Preserve native keyboard activation for the focused toolbar button.
    if (event.target instanceof win.HTMLButtonElement && ['Enter', ' '].includes(event.key)) return;
    const modifier = event.ctrlKey || event.metaKey;
    if (event.code === 'Space') { event.preventDefault(); space = true; }
    if (event.key === 'Escape') { cancelGesture(); selected = ''; setTool('select'); render(); }
    if (api.readOnly) return;
    if (modifier && ['z', 'y'].includes(event.key.toLowerCase())) {
      event.preventDefault();
      if (event.shiftKey || event.key.toLowerCase() === 'y') api.redo(); else api.undo();
      return;
    }
    if (['Delete', 'Backspace'].includes(event.key)) { event.preventDefault(); removeSelected(); }
    if (event.key === 'Enter') { event.preventDefault(); editLabel(getSelected()); }
    if (modifier && event.key.toLowerCase() === 'd') {
      event.preventDefault();
      const shape = getSelected();
      if (shape) { const copy = { ...shape, id: win.crypto.randomUUID(), x: shape.x + 24, y: shape.y + 24 }; selected = copy.id; commit(copy); }
    }
    if (!modifier && !event.altKey) {
      const match = tools.find(([, , key]) => key.toLowerCase() === event.key.toLowerCase());
      if (match) { event.preventDefault(); setTool(match[0]); }
    }
  });
  win.addEventListener('keyup', (event) => { if (event.code === 'Space') space = false; });
  win.addEventListener('blur', () => { space = false; cancelGesture(); });
  function download() {
    const blob = new Blob([JSON.stringify({ format: 'sealcode-whiteboard', version: 1, shapes: shapes() }, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'sealcode-whiteboard.json';
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  render();
  return api;
}
