import { test } from 'node:test';
import assert from 'node:assert/strict';
import { wrapText, layoutText } from '../../src/SealCode/wwwroot/whiteboard-text.js';
import { createShapeElement } from '../../src/SealCode/wwwroot/whiteboard-shape.js';

const measure = (text, size) => Array.from(text).length * size;

test('text wraps at words, splits unbroken Unicode text and preserves explicit blank lines', () => {
  assert.deepEqual(wrapText('one two three', 7, 1, measure), ['one two ', 'three']);
  assert.deepEqual(wrapText('абвгде😀😀', 3, 1, measure), ['абв', 'где', '😀😀']);
  assert.deepEqual(wrapText('first\r\n\r\nlast', 100, 1, measure), ['first', '', 'last']);
  assert.deepEqual(wrapText('', 100, 1, measure), ['']);
  assert.deepEqual(wrapText('a\tb', 100, 1, measure), ['a    b']);
  assert.deepEqual(wrapText('ab cd', 2, 1, measure), ['ab ', 'cd']);
});

test('layout uses measured glyph widths, shrinks fonts for height and reflows on resize without changing the label', () => {
  const source = 'WWWW WWWW WWWW WWWW';
  const wide = layoutText(source, 400, 200, measure);
  const narrow = layoutText(source, 90, 200, measure);
  const short = layoutText(source, 90, 40, measure);
  assert.equal(wide.fontSize, 18);
  assert.ok(narrow.lines.length > wide.lines.length);
  assert.ok(short.fontSize < 18);
  assert.ok(short.lines.length * short.lineHeight <= 40);
  for (const result of [wide, narrow, short])
    for (const line of result.lines) assert.ok(measure(line.trimEnd(), result.fontSize) <= (result === wide ? 400 : 90));
  assert.equal(layoutText('x'.repeat(4000), 1, 1).fontSize, 8);
  assert.equal(source, 'WWWW WWWW WWWW WWWW');
});

test('renderer measures with the canvas font and clips only when the box is too small', () => {
  const element = tag => ({ tag, attributes: {}, children: [], textContent: '',
    setAttribute(key, value) { this.attributes[key] = value; }, append(child) { this.children.push(child); } });
  const context = { font: '', measureText: value => ({ width: value.length * parseInt(context.font) }) };
  const document = { defaultView: { getComputedStyle: () => ({ getPropertyValue: () => 'sans-serif' }) },
    createElement: () => ({ getContext: () => context }), createElementNS: (_, tag) => element(tag) };
  const shape = { id: 'text', type: 'text', x: 10, y: 20, width: 120, height: 100, text: 'A'.repeat(20) };
  const normal = createShapeElement(shape, document);
  assert.equal(normal.children.at(-1).tag, 'text');
  assert.ok(normal.children.at(-1).children.length > 1);
  const clipped = createShapeElement({ ...shape, width: 5, height: 5 }, document);
  assert.equal(clipped.children.at(-1).tag, 'svg');
  assert.equal(clipped.children.at(-1).attributes.overflow, 'hidden');
  assert.equal(clipped.children.at(-1).attributes['aria-label'], shape.text);
});
