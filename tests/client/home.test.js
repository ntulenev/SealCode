import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { runInNewContext } from 'node:vm';

const scriptPath = fileURLToPath(new URL('../../src/SealCode/wwwroot/home.js', import.meta.url));
const source = readFileSync(scriptPath, 'utf8');
const roomPath = '/room/AAAAAAAAAAAAAAAAAAAAAQ';

function fixture(value) {
  const handlers = {};
  const attributes = {};
  let destination;
  let hidden = true;
  let focused = false;
  const input = {
    value,
    addEventListener: (name, handler) => { handlers[name] = handler; },
    setAttribute: (name, value) => { attributes[name] = value; },
    removeAttribute: (name) => { delete attributes[name]; },
    focus: () => { focused = true; }
  };
  const error = { classList: { add: () => { hidden = true; }, remove: () => { hidden = false; } } };
  const form = { addEventListener: (name, handler) => { handlers[name] = handler; } };
  runInNewContext(source, {
    URL,
    document: { getElementById: (id) => ({ joinRoomForm: form, roomLink: input, roomLinkError: error })[id] },
    window: { location: { origin: 'https://seal.example', assign: (url) => { destination = url; } } }
  }, { filename: scriptPath });
  return { handlers, attributes, input, error, destination: () => destination, hidden: () => hidden, focused: () => focused };
}

test('room links submit by form and accept same-site absolute or relative URLs', () => {
  for (const value of [roomPath, `  https://seal.example${roomPath}  `]) {
    const page = fixture(value);
    let prevented = false;
    page.handlers.submit({ preventDefault: () => { prevented = true; } });
    assert.equal(page.destination(), `https://seal.example${roomPath}`);
    assert.equal(prevented, true);
    assert.equal(page.hidden(), true);
  }
});

test('invalid and external links display an accessible error without navigating', () => {
  for (const value of ['javascript:alert(1)', `https://other.example${roomPath}`, '/admin', 'http://[', '']) {
    const page = fixture(value);
    page.handlers.submit({ preventDefault() {} });
    assert.equal(page.destination(), undefined);
    assert.equal(page.hidden(), false);
    assert.equal(page.focused(), true);
    assert.equal(page.attributes['aria-invalid'], 'true');
    assert.match(page.error.textContent, /room link/);
    page.handlers.input();
    assert.equal(page.hidden(), true);
    assert.equal(page.attributes['aria-invalid'], undefined);
  }
});
