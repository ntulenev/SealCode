import { test } from 'node:test';
import assert from 'node:assert/strict';
import { setLanguageOptions } from '../../src/SealCode/wwwroot/language-options.js';

test('language options clean duplicates, preserve selection and disable empty lists', () => {
  const select = {
    children: [],
    appendChild(item) {
      this.children.push(item);
    },
    set innerHTML(value) {
      this.children = [];
    }
  };
  const document = { createElement: () => ({}) };
  setLanguageOptions(document, select, [' sql ', 'sql', 'csharp', null], 'csharp');
  assert.equal(select.value, 'csharp');
  assert.equal(select.children.length, 2);
  setLanguageOptions(document, select, [], 'sql');
  assert.equal(select.disabled, true);
  assert.equal(select.children.length, 1);
  setLanguageOptions(document, select, ['sql']);
  assert.equal(select.selectedIndex, 0);
  assert.equal(select.disabled, false);
});
