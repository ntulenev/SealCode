import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as Y from 'yjs';
import { randomUUID } from 'node:crypto';
import { createWhiteboardView } from '../../src/SealCode/wwwroot/whiteboard-view.js';
import { shapeTypes } from '../../src/SealCode/wwwroot/whiteboard-model.js';
import { BoundedUndoManager } from '../../src/SealCode/wwwroot/bounded-undo-manager.js';

// A small DOM event adapter keeps geometry/gesture tests deterministic.
class Element {
  constructor(tag, document) {
    this.tag = tag; this.ownerDocument = document;
    this.children = []; this.attributes = {}; this.handlers = {};
    this.style = {}; this.dataset = {}; this.value = ''; this.textContent = '';
    this.clientWidth = 1000; this.clientHeight = 600;
    this.classList = { add() {}, toggle() {} };
  }
  append(...elements) { for (const el of elements) { el.parent = this; this.children.push(el); } }
  replaceChildren() { this.children = []; }
  setAttribute(key, value) { this.attributes[key] = value; }
  getAttribute(key) { return this.attributes[key]; }
  closest(selector) { return this.attributes[selector.slice(1, -1)] ? this : this.parent?.closest(selector); }
  addEventListener(name, handler) { (this.handlers[name] ??= []).push(handler); }
  fire(name, event = {}) {
    const e = { target: this, key: '', preventDefault() {}, pointerId: 1, button: 0, clientX: 100, clientY: 150, ...event };
    for (const handler of this.handlers[name] ?? []) handler(e);
  }
  getBoundingClientRect() { return { left: 0, top: 0 }; }
  setPointerCapture(id) { this.pointer = id; }
  hasPointerCapture(id) { return this.pointer === id; }
  releasePointerCapture() { this.pointer = null; }
  focus() { this.ownerDocument.activeElement = this; }
  select() {}
  blur() { this.fire('blur'); }
  click() { this.fire('click'); }
  remove() { this.parent.children = this.parent.children.filter((e) => e !== this); }
}
class TextArea extends Element {}
class Input extends Element {}
class Button extends Element {}
function fixture() {
  const win = new Element('window');
  win.crypto = { randomUUID }; win.HTMLTextAreaElement = TextArea; win.HTMLInputElement = Input;
  win.HTMLButtonElement = Button;
  const document = { defaultView: win };
  document.createElement = (tag) => new (tag === 'textarea' ? TextArea : tag === 'button' ? Button : Element)(tag, document);
  document.createElementNS = (_, tag) => new Element(tag, document);
  const host = new Element('section', document);
  const board = createWhiteboardView(host);
  const doc = new Y.Doc();
  const binding = board.bind(doc);
  board.setReadOnly(false);
  const map = doc.getMap('whiteboard');
  const svg = host.children.find((e) => e.tag === 'svg');
  const find = (predicate, root = host) => predicate(root) ? root : root.children.map((el) => find(predicate, el)).find(Boolean);
  const button = (name) => find((el) => el.attributes['aria-label'] === name);
  const tool = (name) => find((el) => el.dataset.tool === name).click();
  const pointer = (name, x, y, target = svg, extras = {}) => svg.fire(name, { clientX: x, clientY: y, target, ...extras });
  const draw = (type, dx = 150, dy = 80) => {
    tool(type); pointer('pointerdown', 100, 150); pointer('pointermove', 100 + dx, 150 + dy); pointer('pointerup', 100 + dx, 150 + dy);
    return [...map.values()].at(-1);
  };
  const shapeElement = (id) => find((el) => el.attributes['data-shape-id'] === id);
  const editor = () => find((el) => el.tag === 'textarea');
  return { board, doc, binding, map, host, svg, win, button, tool, pointer, draw, find, shapeElement, editor };
}

test('remote cursors retain board coordinates and constant size across viewport changes', () => {
  const f = fixture();
  const points = [];
  f.board.onPointer = (point) => points.push(point);
  f.pointer('pointermove', 100, 150);
  assert.deepEqual(points.at(-1), { x: 100, y: 150 });
  const name = '<script>Alice</script>';
  f.board.setRemoteCursor(name, { x: 100, y: 150 });
  const cursor = f.find((el) => el.attributes['data-cursor-name'] === name);
  assert.equal(cursor.style.transform, 'translate(100px, 150px)');
  assert.equal(cursor.children[0].textContent, name);
  f.svg.fire('wheel', { deltaX: 20, deltaY: 30, ctrlKey: false });
  assert.equal(cursor.style.transform, 'translate(80px, 120px)');
  assert.deepEqual(points.at(-1), { x: 120, y: 180 });
  f.button('Zoom in').click();
  assert.equal(cursor.style.transform, 'translate(-4px, 84px)');
  assert.equal(cursor.attributes.transform, undefined);
  f.pointer('pointerleave', 0, 0);
  assert.equal(points.at(-1), null);
  f.board.setRemoteCursor(name, { x: -10, y: 20 });
  f.board.removeRemoteCursor(name);
  assert.equal(f.find((el) => el.attributes['data-cursor-name'] === name), undefined);
  f.board.setRemoteCursor('Bob', { x: 0, y: 0 });
  f.board.clearRemoteCursors();
  assert.equal(f.find((el) => el.attributes['data-cursor-name']), undefined);
  assert.equal(f.map.size, 0);
  f.binding.destroy(); f.doc.destroy();
});

test('cursor colors are stable across peers, arrival order and removal; labels remain plain text', () => {
  const a = fixture(), b = fixture();
  const names = ['Maya', 'Leo', '<img onerror=alert(1)>'];
  for (const name of names) a.board.setRemoteCursor(name, { x: 0, y: 0 });
  for (const name of [...names].reverse()) b.board.setRemoteCursor(name, { x: 0, y: 0 });
  const cursor = (f, name) => f.find((el) => el.attributes['data-cursor-name'] === name);
  for (const name of names) {
    assert.equal(cursor(a, name).children[1].attributes.fill, cursor(b, name).children[1].attributes.fill);
    assert.equal(cursor(a, name).children[0].textContent, name);
    assert.ok(cursor(a, name).children[3].textContent.length <= 22);
  }
  const color = cursor(a, 'Maya').children[1].attributes.fill;
  a.board.removeRemoteCursor('Maya');
  a.board.setRemoteCursor('Maya', { x: 10, y: 20 });
  assert.equal(cursor(a, 'Maya').children[1].attributes.fill, color);
  assert.notEqual(color, cursor(a, 'Leo').children[1].attributes.fill);
  for (const f of [a, b]) { f.binding.destroy(); f.doc.destroy(); }
});

test('drawing tools render all primitives, labels safely and normalize reverse drags', () => {
  const f = fixture();
  try {
    for (const type of shapeTypes) {
      const shape = f.draw(type, -130, -70);
      assert.equal(shape.type, type);
      assert.ok(f.shapeElement(shape.id));
      if (type === 'text') {
        f.editor().value = '<script>\nService';
        f.editor().fire('keydown', { key: 'Enter', shiftKey: true });
        assert.ok(f.editor());
        f.editor().fire('keydown', { key: 'Enter' });
        assert.equal(f.map.get(shape.id).text, '<script>\nService');
      }
    }
    assert.equal(f.map.size, shapeTypes.length);
    const shape = f.draw('rectangle', 0, 0);
    assert.equal(shape.width, 150);
    f.tool('select');
    f.pointer('pointerdown', 130, 180, f.shapeElement(shape.id));
    f.pointer('pointermove', 170, 220);
    f.pointer('pointerup', 170, 220);
    assert.equal(f.map.get(shape.id).x, 140);
    const resize = f.find((el) => el.attributes['data-resize'] === shape.id);
    f.pointer('pointerdown', 290, 310, resize);
    f.pointer('pointermove', 330, 350, resize);
    f.pointer('pointerup', 330, 350, resize);
    assert.equal(f.map.get(shape.id).width, 190);
    f.svg.fire('dblclick', { target: f.shapeElement(shape.id) });
    f.editor().value = 'Gateway'; f.editor().blur();
    assert.equal(f.map.get(shape.id).text, 'Gateway');
    f.button('Edit label').click();
    f.editor().value = 'Discard'; f.editor().fire('keydown', { key: 'Escape' });
    assert.equal(f.map.get(shape.id).text, 'Gateway');
    f.host.fire('keydown', { ctrlKey: true, key: 'd' });
    assert.equal(f.map.size, shapeTypes.length + 2);
    f.host.fire('keydown', { key: 'Delete' });
    assert.equal(f.map.size, shapeTypes.length + 1);
    f.host.fire('keydown', { key: 'Enter' });
    f.pointer('pointerdown', 20, 50);
    f.pointer('pointerup', 20, 50);
    f.button('Delete selected').click();
    f.svg.fire('dblclick');
  } finally { f.binding.destroy(); f.doc.destroy(); }
});

test('arrowheads point outwards at both endpoints for horizontal, vertical and reverse drags', () => {
  const f = fixture();
  try {
    f.host.fire('keydown', { key: 'b' });
    assert.equal(f.svg.dataset.tool, 'bidirectional-arrow');
    assert.equal(f.button('Bidirectional arrow (B)').getAttribute('aria-pressed'), 'true');
    for (const [dx, dy] of [[160, 0], [-160, 0], [0, 120], [0, -120], [130, 70], [-130, -70], [-130, 70], [130, -70]]) {
      for (const type of ['line', 'arrow', 'bidirectional-arrow']) {
        const shape = f.draw(type, dx, dy);
        assert.deepEqual([shape.x, shape.y, shape.width, shape.height], [100, 150, dx, dy]);
        const paths = f.shapeElement(shape.id).children.filter((el) => el.tag === 'path');
        assert.equal(paths.length, type === 'line' ? 0 : type === 'arrow' ? 1 : 2);
        for (const [index, path] of paths.entries()) {
          const [ax, ay, tx, ty, bx, by] = path.getAttribute('d').match(/-?\d+(?:\.\d+)?(?:e[+-]?\d+)?/gi).map(Number);
          assert.deepEqual([tx, ty], index === 0 ? [100 + dx, 150 + dy] : [100, 150]);
          const direction = index === 0 ? 1 : -1;
          const length = Math.hypot(dx, dy);
          for (const [x, y] of [[ax, ay], [bx, by]]) {
            assert.ok(Math.abs(Math.hypot(tx - x, ty - y) - 15) < 1e-9);
            const projection = ((tx - x) * dx + (ty - y) * dy) / length * direction;
            assert.ok(Math.abs(projection - 15 * Math.cos(Math.PI / 6)) < 1e-9);
          }
          assert.ok(Math.abs((ax + bx - 2 * tx) * dy - (ay + by - 2 * ty) * dx) < 1e-8);
        }
      }
    }
  } finally { f.binding.destroy(); f.doc.destroy(); }
});

test('viewport, keyboard shortcuts, readonly and canceled gestures never mutate remote state', () => {
  const f = fixture();
  try {
    let undos = 0, redos = 0;
    f.board.undo = () => ++undos; f.board.redo = () => ++redos;
    f.host.fire('keydown', { ctrlKey: true, key: 'z' });
    f.host.fire('keydown', { metaKey: true, shiftKey: true, key: 'z' });
    f.host.fire('keydown', { ctrlKey: true, key: 'y' });
    f.button('Undo').click(); f.button('Redo').click();
    assert.equal(undos, 2); assert.equal(redos, 3);
    f.button('Fit diagram').click();
    const shape = f.draw('database');
    f.button('Zoom in').click(); f.button('Zoom out').click();
    f.svg.fire('wheel', { deltaY: -100, ctrlKey: true });
    f.svg.fire('wheel', { deltaX: 12, deltaY: 24 });
    f.button('Fit diagram').click(); f.button('Reset view').click();
    f.tool('pan'); f.pointer('pointerdown', 10, 20); f.pointer('pointermove', 100, 120); f.pointer('pointerup', 100, 120);
    f.host.fire('keydown', { code: 'Space' });
    f.pointer('pointerdown', 0, 0); f.pointer('pointermove', 100, 100); f.pointer('pointerup', 100, 100);
    f.win.fire('keyup', { code: 'Space' });
    f.win.fire('keyup', { code: 'KeyR' });
    f.win.fire('blur');
    f.host.fire('keydown', { key: 'r' });
    f.pointer('pointerdown', 100, 100); f.pointer('pointermove', 200, 200); f.svg.fire('pointercancel');
    assert.equal(f.map.size, 1);
    f.pointer('pointerdown', 100, 100); f.host.fire('keydown', { key: 'Escape' });
    f.pointer('pointermove', 300, 300); f.pointer('pointerup', 300, 300);
    f.pointer('pointerdown', 100, 100, f.svg, { button: 2 });
    f.board.setReadOnly(true);
    f.host.fire('keydown', { key: 'Delete' });
    f.pointer('pointerdown', 100, 100);
    f.tool('pan'); f.pointer('pointerdown', 10, 20); f.pointer('pointermove', 30, 40); f.pointer('pointerup', 30, 40);
    assert.equal(f.map.size, 1);
    assert.equal(f.map.get(shape.id).x, shape.x);
    f.board.setReadOnly(false);
    f.board.download();
  } finally { f.binding.destroy(); f.doc.destroy(); }
});

test('concurrent deletion during a drag or label edit does not resurrect the element', () => {
  const f = fixture();
  try {
    const shape = f.draw('rectangle');
    f.pointer('pointerdown', 130, 180, f.shapeElement(shape.id));
    f.pointer('pointermove', 170, 220);
    f.map.delete(shape.id);
    f.pointer('pointerup', 170, 220);
    assert.equal(f.map.size, 0);
    const next = f.draw('text');
    f.editor().fire('keydown', { key: 'a' });
    f.map.delete(next.id);
    f.editor().value = 'Gone'; f.editor().blur();
    assert.equal(f.map.size, 0);
    const last = f.draw('text');
    f.host.fire('keydown', { key: 'Delete', target: f.editor() });
    assert.ok(f.map.has(last.id));
    f.board.setReadOnly(true);
    assert.equal(f.editor(), undefined);
  } finally { f.binding.destroy(); f.doc.destroy(); }
});


test('double clicks remain stable when SVG children were replaced between pointer events', () => {
  const f = fixture();
  try {
    const shape = f.draw('rectangle');
    for (let i = 0; i < 2; i++) {
      f.pointer('pointerdown', 140, 180, f.shapeElement(shape.id));
      f.pointer('pointerup', 140, 180);
    }
    assert.ok(f.editor());
    f.svg.fire('dblclick');
    f.editor().value = 'Gateway';
    f.editor().fire('keydown', { key: 'Enter' });
    assert.equal(f.map.get(shape.id).text, 'Gateway');
  } finally { f.binding.destroy(); f.doc.destroy(); }
});

test('a second pointer cannot replace or cancel the active drawing gesture', () => {
  const f = fixture();
  try {
    f.tool('rectangle');
    f.pointer('pointerdown', 100, 150);
    f.pointer('pointermove', 250, 230);
    f.pointer('pointerdown', 400, 400, f.svg, { pointerId: 2 });
    f.pointer('pointermove', 500, 500, f.svg, { pointerId: 2 });
    f.pointer('pointercancel', 500, 500, f.svg, { pointerId: 2 });
    f.pointer('lostpointercapture', 500, 500, f.svg, { pointerId: 2 });
    f.pointer('pointerup', 500, 500, f.svg, { pointerId: 2 });
    f.pointer('pointerup', 250, 230);
    assert.equal(f.map.size, 1);
    const [shape] = f.map.values();
    assert.deepEqual([shape.x, shape.y, shape.width, shape.height], [100, 150, 150, 80]);
  } finally { f.binding.destroy(); f.doc.destroy(); }
});

test('toolbar Enter and Space retain native activation without editing labels or starting a pan', () => {
  const f = fixture();
  try {
    const shape = f.draw('rectangle');
    const button = f.button('Zoom in');
    const preventDefault = () => assert.fail('Native button activation was prevented');
    f.host.fire('keydown', { key: 'Enter', target: button, preventDefault });
    f.host.fire('keydown', { key: ' ', code: 'Space', target: button, preventDefault });
    assert.equal(f.editor(), undefined);
    f.pointer('pointerdown', 140, 180, f.shapeElement(shape.id));
    f.pointer('pointermove', 180, 210);
    f.pointer('pointerup', 180, 210);
    assert.deepEqual([f.map.get(shape.id).x, f.map.get(shape.id).y], [140, 180]);
  } finally { f.binding.destroy(); f.doc.destroy(); }
});

function clipboard() {
  const data = new Map();
  return { setData: (type, value) => data.set(type, value), getData: (type) => data.get(type) ?? '' };
}

test('clipboard copies every shape as a snapshot, pastes unique offset elements and supports selective undo', () => {
  const f = fixture();
  const undo = new BoundedUndoManager(Y, f.binding.scope, { trackedOrigin: f.binding });
  f.board.beforeChange = () => undo.stopCapturing();
  try {
    for (const type of shapeTypes) {
      const shape = f.draw(type, -130, -70);
      if (f.editor()) f.editor().fire('keydown', { key: 'Enter' });
      f.map.set(shape.id, { ...f.map.get(shape.id), text: 'Service\n<Label>' });
      const source = f.map.get(shape.id);
      const clipboardData = clipboard();
      let copies = 0, pastes = 0;
      f.host.fire('copy', { clipboardData, preventDefault: () => copies++ });
      f.map.delete(source.id);
      f.host.fire('paste', { clipboardData, preventDefault: () => pastes++ });
      const first = [...f.map.values()].at(-1);
      assert.notEqual(first.id, source.id);
      assert.deepEqual(first, { ...source, id: first.id, x: source.x + 24, y: source.y + 24 });
      f.host.fire('paste', { clipboardData, preventDefault: () => pastes++ });
      const second = [...f.map.values()].at(-1);
      assert.notEqual(second.id, first.id);
      assert.deepEqual(second, { ...source, id: second.id, x: source.x + 48, y: source.y + 48 });
      assert.equal(copies, 1); assert.equal(pastes, 2);
      undo.undo();
      assert.ok(f.map.has(first.id)); assert.equal(f.map.has(second.id), false);
      undo.redo(); assert.ok(f.map.has(second.id));
      f.host.fire('keydown', { key: 'Delete' });
      assert.equal(f.map.has(second.id), false);
    }
  } finally { undo.destroy(); f.binding.destroy(); f.doc.destroy(); }
});

test('clipboard transfers between boards and leaves text editing, readonly and invalid data untouched', () => {
  const source = fixture(), target = fixture();
  const clipboardData = clipboard();
  const preventDefault = () => assert.fail('Native clipboard action was prevented');
  try {
    source.host.fire('copy', { clipboardData, preventDefault });
    const shape = source.draw('database');
    source.host.fire('copy', { clipboardData });
    target.host.fire('paste', { clipboardData });
    assert.equal(target.map.size, 1);
    const pasted = [...target.map.values()][0];
    assert.deepEqual(pasted, { ...shape, id: pasted.id, x: shape.x + 24, y: shape.y + 24 });
    for (const event of ['copy', 'paste']) {
      target.host.fire(event, { preventDefault });
      target.host.fire(event, { clipboardData, target: new Input('input'), preventDefault });
      target.host.fire(event, { clipboardData, target: { isContentEditable: true }, preventDefault });
      target.button('Edit label').click();
      target.host.fire(event, { clipboardData, target: target.editor(), preventDefault });
      target.editor().fire('keydown', { key: 'Escape' });
    }
    target.tool('rectangle'); target.pointer('pointerdown', 300, 300);
    for (const event of ['copy', 'paste']) target.host.fire(event, { clipboardData, preventDefault });
    target.svg.fire('pointercancel');
    target.board.setReadOnly(true);
    target.host.fire('paste', { clipboardData, preventDefault });
    target.board.setReadOnly(false);
    for (const content of ['', 'plain text', 'null', '{}', 'x'.repeat(100001),
      ...[
        { format: 'other', version: 1, shapes: [shape] },
        { format: 'sealcode-whiteboard', version: 2, shapes: [shape] },
        ...[null, [], Array(101).fill(shape), [shape, { ...shape, type: 'unknown' }], [{ ...shape, x: 1e7 }]].map((shapes) =>
          ({ format: 'sealcode-whiteboard', version: 1, shapes }))
      ].map(JSON.stringify)]) {
      clipboardData.setData('text/plain', content);
      target.host.fire('paste', { clipboardData, preventDefault });
    }
    assert.equal(target.map.size, 1);
    target.binding.destroy();
    target.host.fire('paste', { clipboardData, preventDefault });
  } finally {
    source.binding.destroy(); source.doc.destroy(); target.doc.destroy();
  }
});

test('diagram paste preserves geometry, regenerates IDs and is one collaborative undo operation', () => {
  const f = fixture();
  const undo = new BoundedUndoManager(Y, f.binding.scope, { trackedOrigin: f.binding });
  f.board.beforeChange = () => undo.stopCapturing();
  try {
    const shapes = ['rectangle', 'arrow', 'database'].map((type, i) =>
      ({ id: String(i), type, x: 50 + i * 180, y: 100, width: 140, height: 80, text: type }));
    const data = clipboard();
    data.setData('text/plain', JSON.stringify({ format: 'sealcode-whiteboard', version: 1, shapes }));
    f.host.fire('paste', { clipboardData: data });
    const pasted = [...f.map.values()];
    assert.equal(pasted.length, 3);
    assert.equal(new Set(pasted.map(s => s.id)).size, 3);
    pasted.forEach((s, i) => {
      assert.notEqual(s.id, shapes[i].id);
      assert.deepEqual(s, { ...shapes[i], id: s.id, x: shapes[i].x + 24, y: 124 });
    });
    undo.undo(); assert.equal(f.map.size, 0);
    undo.redo(); assert.equal(f.map.size, 3);
    f.host.fire('paste', { clipboardData: data });
    assert.equal(f.map.size, 6);
    undo.undo(); assert.equal(f.map.size, 3);
  } finally { undo.destroy(); f.binding.destroy(); f.doc.destroy(); }
});
