import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ADJECTIVES, ANIMALS, generateDisplayName } from '../../src/SealCode/wwwroot/display-name.js';

test('word lists provide 65,536 distinct short names that fit participant limits', () => {
  for (const words of [ADJECTIVES, ANIMALS]) {
    assert.equal(words.length, 256);
    assert.equal(new Set(words.map((word) => word.toLowerCase())).size, 256);
    assert.ok(Object.isFrozen(words));
    for (const word of words) assert.match(word, /^[A-Z][a-z]{1,9}$/);
  }
  const names = new Set();
  for (let adjective = 0; adjective < 256; adjective++) {
    for (let animal = 0; animal < 256; animal++) {
      const name = generateDisplayName((values) => {
        values.set([adjective, animal]);
        return values;
      });
      assert.ok(name.length <= 20);
      names.add(name);
    }
  }
  assert.equal(names.size, 65536);
  for (const example of ['Wild Fox', 'Bold Seal', 'Shy Otter']) assert.ok(names.has(example));
});

test('default generator uses browser randomness to produce a valid pair', () => {
  const [adjective, animal] = generateDisplayName().split(' ');
  assert.ok(ADJECTIVES.includes(adjective));
  assert.ok(ANIMALS.includes(animal));
});
