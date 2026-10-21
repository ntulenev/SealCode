import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PresenceView, truncateName } from '../../src/SealCode/wwwroot/presence-ui.js';

function view() {
  const elements = [];
  return new PresenceView({
    editor: null,
    monaco: {},
    participantsList: { innerHTML: '', appendChild: (child) => elements.push(child) },
    document: { createElement: () => ({ classList: { add() {} } }) }
  });
}

test('participant colors are stable per name and remain within the palette', () => {
  const presence = view();
  const first = presence.assignColor('Alice');
  for (let i = 0; i < 50; i++) {
    assert.ok(presence.caretPalette.includes(presence.assignColor(`User ${i}`)));
  }
  assert.equal(presence.assignColor('Alice'), first);
});

test('names retain the existing truncation boundary', () => {
  assert.equal(truncateName(''), '');
  assert.equal(truncateName('1234567890'), '1234567890');
  assert.equal(truncateName('12345678901'), '123456789...');
});

test('active and copy states expire while selection follows transitions', (context) => {
  context.mock.timers.enable({ apis: ['setTimeout', 'Date'], now: 100 });
  const presence = view();
  presence.markActiveEditor('Alice');
  presence.markCopying('Bob');
  presence.markSelecting('Alice', true);
  assert.equal(presence.activeEditors.has('Alice'), true);
  assert.equal(presence.copyingUsers.has('Bob'), true);
  context.mock.timers.tick(1301);
  assert.equal(presence.activeEditors.has('Alice'), false);
  assert.equal(presence.copyingUsers.has('Bob'), false);
  assert.equal(presence.selectingUsers.has('Alice'), true);
  presence.markSelecting('Alice', false);
  assert.equal(presence.selectingUsers.has('Alice'), false);
});

test('remote carets clamp columns, keep stable styles and restore after hiding', (context) => {
  context.mock.timers.enable({ apis: ['setTimeout', 'Date'], now: 100 });
  const children = [];
  let style;
  const document = {
    getElementById: () => style,
    createElement: () => ({
      classes: new Set(),
      classList: {
        add(name) {
          this.owner.classes.add(name);
        }
      }
    }),
    head: {
      appendChild: (element) => {
        style = element;
      }
    }
  };
  const createElement = document.createElement;
  document.createElement = () => {
    const element = createElement();
    element.classList.owner = element;
    return element;
  };
  const model = {
    getPositionAt: (offset) => ({ lineNumber: 2, column: offset + 1 }),
    getLineMaxColumn: () => 5
  };
  let decorations = [];
  const editor = {
    getModel: () => model,
    deltaDecorations: (_, next) => {
      decorations = next;
      return next.map((_, index) => String(index));
    }
  };
  const monaco = {
    Range: class {
      constructor(line, col) {
        this.line = line;
        this.col = col;
      }
    },
    editor: { TrackedRangeStickiness: { NeverGrowsWhenTypingAtEdges: 1 } }
  };
  const participantsList = {
    set innerHTML(_) {
      children.length = 0;
    },
    appendChild: (element) => children.push(element)
  };
  const presence = new PresenceView({ editor, monaco, document, participantsList });
  presence.displayName = 'Alice';
  Object.assign(presence.cursorPositions, { Alice: 3, Bob: 100, Invalid: '?' });
  presence.markActiveEditor('Bob');
  presence.markSelecting('Bob', true);
  presence.markCopying('Bob');
  presence.renderUsers(['Alice', 'Bob']);
  assert.equal(children[1].title, 'Bob');
  assert.equal(children[1].textContent, 'Bob (2:101)');
  assert.deepEqual([...children[1].classes], ['active', 'copied', 'copying']);
  assert.deepEqual(presence.getLineColFromOffset(-1), { line: 2, col: 1 });
  presence.renderRemoteCarets();
  assert.equal(decorations.length, 1);
  assert.equal(decorations[0].range.col, 5);
  const css = style.textContent;
  presence.hideRemoteCaretsTemporarily();
  assert.equal(decorations.length, 0);
  context.mock.timers.tick(800);
  assert.equal(decorations.length, 1);
  assert.equal(style.textContent, css);
  presence.renderUsers(null);
  assert.equal(children.length, 0);
});

test('retaining users cleans every departed presence collection and preserves self', () => {
  const presence = view();
  presence.displayName = 'Alice';
  for (const name of ['Alice', 'Bob', 'Carol']) {
    presence.cursorPositions[name] = 20;
    presence.caretColors[name] = 'red';
    for (const map of [
      presence.caretClassMap,
      presence.activeEditors,
      presence.selectingUsers,
      presence.copyingUsers
    ])
      map.set(name, 1);
  }
  presence.retainUsers(['Carol']);
  assert.deepEqual(Object.keys(presence.cursorPositions), ['Alice', 'Carol']);
  assert.deepEqual(Object.keys(presence.caretColors), ['Alice', 'Carol']);
  for (const map of [
    presence.caretClassMap,
    presence.activeEditors,
    presence.selectingUsers,
    presence.copyingUsers
  ])
    assert.deepEqual([...map.keys()], ['Alice', 'Carol']);
  presence.removeUser('Carol');
  assert.deepEqual(Object.keys(presence.cursorPositions), ['Alice']);
});
