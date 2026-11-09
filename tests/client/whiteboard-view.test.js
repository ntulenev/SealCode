import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as Y from 'yjs';
import { randomUUID } from 'node:crypto';
import { createWhiteboardView } from '../../src/SealCode/wwwroot/whiteboard-view.js';
import { shapeTypes } from '../../src/SealCode/wwwroot/whiteboard-model.js';

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
function fixture() {
  const win = new Element('window');
  win.crypto = { randomUUID }; win.HTMLTextAreaElement = TextArea; win.HTMLInputElement = Input;
  const document = { defaultView: win };
  document.createElement = (tag) => new (tag === 'textarea' ? TextArea : Element)(tag, document);
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
    assert.equal(f.map.size, 8);
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
    assert.equal(f.map.size, 10);
    f.host.fire('keydown', { key: 'Delete' });
    assert.equal(f.map.size, 9);
    f.host.fire('keydown', { key: 'Enter' });
    f.pointer('pointerdown', 20, 50);
    f.pointer('pointerup', 20, 50);
    f.button('Delete selected').click();
    f.svg.fire('dblclick');
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
