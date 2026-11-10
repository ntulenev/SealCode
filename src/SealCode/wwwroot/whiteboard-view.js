import { isCopyShortcut, isPasteShortcut } from './keyboard-shortcuts.js';
import { layoutText } from './whiteboard-text.js';
import { createShapeElement } from './whiteboard-shape.js';
import { parseWhiteboardClipboard } from './whiteboard-clipboard.js';
import { bindWhiteboard, bounds, isShape, normalizeShape, shapeTypes } from './whiteboard-model.js';
import { truncateName } from './presence-ui.js';

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
  /** @type {Set<string>} */
  const selected = new Set();
  let pastedContent = '';
  let pasteCount = 0;
  /** @type {{id: string, time: number} | null} */
  let lastClick = null;
  let space = false;
  let zoom = 1;
  let offset = { x: 0, y: 0 };
  /** @type {{x: number, y: number} | null} */
  let pointer = null;
  /** @type {Map<string, {point: {x: number, y: number}, element: SVGElement}>} */
  const remoteCursors = new Map();
  /** @type {{start: {x: number, y: number}, original?: import('./whiteboard-model.js').Shape, draft?: import('./whiteboard-model.js').Shape, originals?: import('./whiteboard-model.js').Shape[], drafts?: import('./whiteboard-model.js').Shape[], end?: {x: number, y: number}, selectionStart?: string[], mode: string, pointerId: number, offset: {x: number, y: number}} | null} */
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
  const cursorLayer = node('g', { class: 'board-cursors', 'aria-hidden': 'true' });
  svg.append(cursorLayer);
  host.append(svg);
  const hint = document.createElement('p');
  hint.className = 'board-hint';
  hint.textContent = 'Drag empty canvas to select · Ctrl+click to add/remove · Drag selection to move · Ctrl+A to select all · Ctrl+C / Ctrl+V to copy / paste · Space + drag to pan · Ctrl + wheel to zoom';
  host.append(hint);
  const clipboardStatus = document.createElement('p');
  clipboardStatus.className = 'board-clipboard-status';
  clipboardStatus.setAttribute('role', 'status');
  host.append(clipboardStatus);
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
    /** @param {{x: number, y: number} | null} point */
    onPointer: (point) => {},
    /** @param {string} name @param {{x: number, y: number}} point */
    setRemoteCursor(name, point) {
      let cursor = remoteCursors.get(name);
      if (!cursor) {
        // Names determine colors identically on every client, regardless of arrival order.
        let hash = 0;
        for (const char of name) hash = (Math.imul(hash, 31) + char.charCodeAt(0)) | 0;
        const color = `hsl(${(hash >>> 0) % 360} 65% 40%)`;
        const element = node('g', { class: 'board-remote-cursor', 'data-cursor-name': name });
        const title = node('title');
        title.textContent = name;
        const label = truncateName(name);
        const text = node('text', { x: 14, y: 33, class: 'board-cursor-name' });
        text.textContent = label;
        element.append(title,
          node('path', { d: 'M0 0 L5 19 L9 12 L17 10 Z', fill: color, stroke: 'white', 'stroke-width': 1.5 }),
          node('rect', { x: 9, y: 20, width: label.length * 8 + 10, height: 20, rx: 4, fill: color }), text);
        cursor = { point, element };
        remoteCursors.set(name, cursor);
        positionCursor(cursor);
        cursorLayer.append(element);
      } else {
        cursor.point = point;
        positionCursor(cursor);
      }
    },
    /** @param {string} name */
    removeRemoteCursor(name) {
      remoteCursors.get(name)?.element.remove();
      remoteCursors.delete(name);
    },
    clearRemoteCursors() {
      remoteCursors.clear();
      cursorLayer.replaceChildren();
    },
    /** @param {boolean} value */
    setReadOnly(value) {
      api.readOnly = value;
      if (value) { pointer = null; gesture = null; cancelText(); }
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
  function selectedShapes() { return shapes().filter(shape => selected.has(shape.id)); }
  function getSelected() {
    const all = selectedShapes();
    return all.length === 1 ? all[0] : null;
  }
  /** @param {string} id */
  function selectOne(id) {
    selected.clear();
    if (id) selected.add(id);
  }
  /** @param {import('./whiteboard-model.js').Shape} shape */
  function commit(shape) {
    if (!binding || api.readOnly || !isShape(shape)) return;
    api.beforeChange();
    binding.scope.doc.transact(() => binding.scope.set(shape.id, normalizeShape(shape)), undoOrigin);
    api.onChange();
  }
  function removeSelected() {
    if (api.readOnly || !binding) return;
    const targets = selectedShapes();
    if (!targets.length) return;
    api.beforeChange();
    binding.scope.doc.transact(() => {
      for (const shape of targets) binding.scope.delete(shape.id);
    }, undoOrigin);
    selected.clear();
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
  /** @param {{point: {x: number, y: number}, element: SVGElement}} cursor */
  function positionCursor(cursor) {
    cursor.element.style.transform = `translate(${cursor.point.x * zoom + offset.x}px, ${cursor.point.y * zoom + offset.y}px)`;
  }
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
    const drafts = new Map((gesture?.drafts ?? []).map(shape => [shape.id, shape]));
    for (const shape of all) {
      const moved = drafts.get(shape.id);
      draw(moved ? { ...shape, x: moved.x, y: moved.y } : draft?.id === shape.id ? draft : shape);
    }
    if (draft && !all.some((s) => s.id === draft.id)) draw(draft);
    const selection = selectedShapes();
    const activeShapes = draft ? [draft] : selection.map(shape => {
      const moved = drafts.get(shape.id);
      return moved ? { ...shape, x: moved.x, y: moved.y } : shape;
    });
    for (const active of api.readOnly ? [] : activeShapes) {
      const b = bounds(active);
      layer.append(node('rect', { x: b.x - 5 / zoom, y: b.y - 5 / zoom, width: Math.max(1, b.width) + 10 / zoom, height: Math.max(1, b.height) + 10 / zoom, class: 'board-selection' }));
      if (activeShapes.length !== 1) continue;
      const handle = node('rect', { x: active.x + active.width - 6 / zoom, y: active.y + active.height - 6 / zoom, width: 12 / zoom, height: 12 / zoom, class: 'board-resize', 'data-resize': active.id });
      layer.append(handle);
    }
    if (gesture?.mode === 'marquee') {
      const start = world(gesture.start), end = world(gesture.end ?? gesture.start);
      layer.append(node('rect', { x: Math.min(start.x, end.x), y: Math.min(start.y, end.y),
        width: Math.abs(end.x - start.x), height: Math.abs(end.y - start.y), class: 'board-marquee' }));
    }
    undoButton.disabled = redoButton.disabled = api.readOnly;
    deleteButton.disabled = api.readOnly || !selection.length;
    labelButton.disabled = api.readOnly || !getSelected();
    for (const [name, button] of toolButtons) button.disabled = api.readOnly && !['select', 'pan'].includes(name);
    zoomButton.textContent = `${Math.round(zoom * 100)}%`;
    host.classList.toggle('board-readonly', api.readOnly);
    hint.hidden = all.length > 0;
    for (const cursor of remoteCursors.values()) positionCursor(cursor);
    if (pointer) api.onPointer(world(pointer));
  }
  /** @param {import('./whiteboard-model.js').Shape} s */
  function draw(s) { layer.append(createShapeElement(s, document)); }
  /** @param {import('./whiteboard-model.js').Shape | null} shape */
  function editLabel(shape) {
    if (!shape || api.readOnly || !binding) return;
    cancelText();
    const input = document.createElement('textarea');
    textEditor = input;
    input.className = 'board-label-editor';
    input.setAttribute('aria-label', 'Element label');
    input.title = 'Enter to save · Ctrl/Cmd+Enter or Shift+Enter for a new line · Escape to cancel';
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
        commit({ ...current, text });
      }
      input.remove();
      textEditor = null;
      render();
    };
    cancelText = () => finish(false);
    input.addEventListener('blur', () => finish(true));
    input.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') { event.preventDefault(); finish(false); svg.focus(); }
      if (event.key === 'Enter' && !event.isComposing) {
        if (event.ctrlKey || event.metaKey) {
          event.preventDefault();
          const start = input.selectionStart, end = input.selectionEnd;
          if (input.value.length - (end - start) < input.maxLength) {
            input.setRangeText('\n', start, end, 'end');
            input.dispatchEvent(new win.Event('input', { bubbles: true }));
          }
        } else if (!event.shiftKey) { event.preventDefault(); finish(true); svg.focus(); }
      }
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
      if (!resize && id && (event.ctrlKey || event.metaKey)) {
        if (selected.has(id)) selected.delete(id); else selected.add(id);
        gesture.mode = 'toggle';
      } else if (!id && !resize) {
        gesture.mode = 'marquee';
        gesture.selectionStart = [...selected];
        if (!event.ctrlKey && !event.metaKey) selected.clear();
        gesture.originals = event.ctrlKey || event.metaKey ? selectedShapes() : [];
      } else {
        if (resize || !selected.has(id)) selectOne(resize || id);
        const selection = selectedShapes();
        if (!resize && selection.length > 1) {
          gesture.originals = selection;
          gesture.drafts = selection;
          gesture.mode = 'move-group';
        } else {
          const shape = getSelected();
          if (shape) { gesture.original = shape; gesture.draft = { ...shape }; gesture.mode = resize ? 'resize' : 'move'; }
        }
      }
    } else if (shapeTypes.includes(tool)) {
      const p = world(start);
      const type = tool;
      const shape = { id: win.crypto.randomUUID(), type, x: p.x, y: p.y, width: 0, height: 0, text: '' };
      gesture.draft = shape;
      gesture.mode = 'create';
      selectOne(shape.id);
    }
    render();
  });
  svg.addEventListener('pointermove', (event) => {
    if (event.isPrimary === false) return;
    pointer = local(event);
    if (!gesture || gesture.pointerId !== event.pointerId) {
      api.onPointer(world(pointer));
      return;
    }
    const point = local(event);
    const dx = (point.x - gesture.start.x) / zoom, dy = (point.y - gesture.start.y) / zoom;
    if (gesture.mode === 'pan') {
      offset = { x: gesture.offset.x + dx * zoom, y: gesture.offset.y + dy * zoom };
    } else if (gesture.mode === 'marquee') {
      gesture.end = point;
      const start = world(gesture.start), end = world(point);
      const left = Math.min(start.x, end.x), top = Math.min(start.y, end.y);
      const right = Math.max(start.x, end.x), bottom = Math.max(start.y, end.y);
      selected.clear();
      for (const shape of gesture.originals) selected.add(shape.id);
      for (const shape of shapes()) {
        const b = bounds(shape);
        if (b.x >= left && b.y >= top && b.x + b.width <= right && b.y + b.height <= bottom) selected.add(shape.id);
      }
    } else if (gesture.mode === 'move-group') {
      gesture.drafts = gesture.originals.map(shape => ({ ...shape, x: shape.x + dx, y: shape.y + dy }));
    } else if (gesture.draft) {
      const s = gesture.original;
      if (gesture.mode === 'move') gesture.draft = { ...s, x: s.x + dx, y: s.y + dy };
      else gesture.draft = { ...gesture.draft, width: (s?.width ?? 0) + dx, height: (s?.height ?? 0) + dy };
    }
    render();
  });
  svg.addEventListener('pointerup', (event) => {
    if (!gesture || gesture.pointerId !== event.pointerId) return;
    const { draft, original, mode, drafts, originals } = gesture;
    const clicked = mode === 'move' && draft && original &&
      Math.hypot(draft.x - original.x, draft.y - original.y) < 3 / zoom;
    const now = Date.now();
    const doubleClick = clicked && lastClick?.id === original.id && now - lastClick.time < 500;
    lastClick = clicked && !doubleClick ? { id: original.id, time: now } : null;
    gesture = null;
    if (svg.hasPointerCapture(event.pointerId)) svg.releasePointerCapture(event.pointerId);
    if (mode === 'move-group' && binding && !api.readOnly) {
      const moves = drafts.flatMap((shape, i) => {
        const current = binding.scope.get(shape.id);
        return isShape(current) && (shape.x !== originals[i].x || shape.y !== originals[i].y)
          ? [{ ...current, x: shape.x, y: shape.y }] : [];
      });
      if (moves.length && moves.every(isShape)) {
        api.beforeChange();
        binding.scope.doc.transact(() => {
          for (const shape of moves) binding.scope.set(shape.id, shape);
        }, undoOrigin);
        api.onChange();
      }
    } else if (draft && mode === 'create') {
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
  function cancelGesture() {
    if (gesture?.selectionStart) {
      selected.clear();
      for (const id of gesture.selectionStart) selected.add(id);
    }
    gesture = null; render();
  }
  /** @param {PointerEvent} event */
  function cancelPointer(event) {
    if (gesture?.pointerId === event.pointerId) cancelGesture();
  }
  svg.addEventListener('pointercancel', cancelPointer);
  const hidePointer = () => { pointer = null; api.onPointer(null); };
  svg.addEventListener('pointerleave', hidePointer);
  svg.addEventListener('pointercancel', hidePointer);
  win.addEventListener('blur', hidePointer);
  svg.addEventListener('lostpointercapture', cancelPointer);
  svg.addEventListener('dblclick', (event) => {
    if (textEditor || event.ctrlKey || event.metaKey) return;
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
    if (id) { selectOne(id); editLabel(getSelected()); }
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
  function copyContent() {
    if (gesture) return null;
    const shapes = selectedShapes();
    if (!shapes.length) return null;
    return JSON.stringify({ format: 'sealcode-whiteboard', version: 1, shapes });
  }
  /** @param {string} content */
  function copied(content) {
    pastedContent = content;
    pasteCount = 0;
    clipboardStatus.textContent = '';
  }
  /** @param {string} content */
  function pasteContent(content) {
    if (gesture || api.readOnly || !binding) return false;
    let shapes = parseWhiteboardClipboard(content);
    let plainText = false;
    if (!shapes) {
      if (!content.trim() || content.length > 4000) return false;
      let payload;
      try { payload = JSON.parse(content); } catch { /* Ordinary clipboard text. */ }
      if (payload?.format === 'sealcode-whiteboard') return false;
      const text = content.replace(/\r\n?/g, '\n');
      const width = 320;
      const height = Math.max(80, Math.min(480, layoutText(text, width - 24, Infinity).lines.length * 22 + 24));
      const center = world({ x: svg.clientWidth / 2, y: svg.clientHeight / 2 });
      shapes = [{ id: '', type: 'text', x: center.x - width / 2, y: center.y - height / 2, width, height, text }];
      plainText = true;
    }
    const count = content === pastedContent ? pasteCount + 1 : 1;
    const copies = shapes.map(shape => ({ ...shape, id: win.crypto.randomUUID(),
      x: shape.x + (plainText ? count - 1 : count) * 24, y: shape.y + (plainText ? count - 1 : count) * 24 }));
    if (!copies.every(isShape)) return false;
    pastedContent = content;
    pasteCount = count;
    selected.clear();
    for (const shape of copies) selected.add(shape.id);
    setTool('select');
    api.beforeChange();
    binding.scope.doc.transact(() => {
      for (const shape of copies) binding.scope.set(shape.id, normalizeShape(shape));
    }, undoOrigin);
    api.onChange();
    clipboardStatus.textContent = '';
    svg.focus();
    return true;
  }
  // Keep native clipboard events for standard shortcuts, context menus and text editing.
  host.addEventListener('copy', (event) => {
    if (isTextTarget(event.target) || !event.clipboardData) return;
    const content = copyContent();
    if (!content) return;
    event.clipboardData.setData('text/plain', content);
    event.preventDefault();
    copied(content);
  });
  host.addEventListener('paste', (event) => {
    if (isTextTarget(event.target) || !event.clipboardData) return;
    if (pasteContent(event.clipboardData.getData('text/plain'))) event.preventDefault();
  });
  host.addEventListener('keydown', (event) => {
    if (isTextTarget(event.target)) return;
    // Preserve native keyboard activation for the focused toolbar button.
    if (event.target instanceof win.HTMLButtonElement && ['Enter', ' '].includes(event.key)) return;
    const modifier = (event.ctrlKey || event.metaKey) && !event.altKey;
    const key = event.code?.startsWith('Key') ? event.code.slice(3).toLowerCase() : event.key.toLowerCase();
    // Some browsers do not dispatch copy/paste for non-Latin shortcut characters.
    // Handle those physical keys explicitly, without changing native text editing.
    if ((isCopyShortcut(event) && event.key.toLowerCase() !== 'c') ||
        (isPasteShortcut(event) && event.key.toLowerCase() !== 'v')) {
      if (gesture || event.repeat) return;
      const sourceBinding = binding;
      if (isCopyShortcut(event)) {
        const content = copyContent();
        if (!content) return;
        event.preventDefault();
        void (async () => {
          try { await win.navigator.clipboard.writeText(content); copied(content); }
          catch { clipboardStatus.textContent = 'Clipboard unavailable. Allow clipboard access and try again.'; }
        })();
      } else if (!api.readOnly && binding) {
        event.preventDefault();
        void (async () => {
          try {
            const content = await win.navigator.clipboard.readText();
            if (binding === sourceBinding) pasteContent(content);
          } catch { clipboardStatus.textContent = 'Clipboard unavailable. Allow clipboard access and try again.'; }
        })();
      }
      return;
    }
    if (event.code === 'Space') { event.preventDefault(); space = true; }
    if (event.key === 'Escape') { cancelGesture(); selected.clear(); setTool('select'); render(); }
    if (modifier && key === 'a') {
      event.preventDefault();
      if (gesture) return;
      selected.clear();
      for (const shape of shapes()) selected.add(shape.id);
      setTool('select');
      render();
      return;
    }
    if (api.readOnly) return;
    if (modifier && ['z', 'y'].includes(key)) {
      event.preventDefault();
      if (event.shiftKey || key === 'y') api.redo(); else api.undo();
      return;
    }
    if (['Delete', 'Backspace'].includes(event.key)) { event.preventDefault(); removeSelected(); }
    if (event.key === 'Enter') { event.preventDefault(); editLabel(getSelected()); }
    if (modifier && key === 'd') {
      event.preventDefault();
      if (!binding) return;
      const copies = selectedShapes().map(shape => ({ ...shape, id: win.crypto.randomUUID(),
        x: shape.x + 24, y: shape.y + 24 }));
      if (!copies.length || !copies.every(isShape)) return;
      selected.clear();
      for (const shape of copies) selected.add(shape.id);
      api.beforeChange();
      binding.scope.doc.transact(() => {
        for (const shape of copies) binding.scope.set(shape.id, normalizeShape(shape));
      }, undoOrigin);
      api.onChange();
    }
    if (!modifier && !event.altKey) {
      const match = tools.find(([, , shortcut]) => shortcut.toLowerCase() === key);
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
