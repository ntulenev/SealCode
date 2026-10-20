import test from 'node:test';
import assert from 'node:assert/strict';
import { RevisionedRoomView } from '../../src/SealCode/wwwroot/room-presence.js';

test('current language follows only accepted metadata revisions', () => {
  const applied = [];
  const view = new RevisionedRoomView({ language: value => applied.push(value), users() {} });
  assert.equal(view.currentLanguage, undefined);
  assert.equal(view.updateLanguage('csharp', 0), true);
  assert.equal(view.currentLanguage, 'csharp');
  assert.equal(view.updateLanguage('sql', 2), true);
  assert.equal(view.currentLanguage, 'sql');
  for (const revision of [1, NaN, Infinity, 2.5]) {
    assert.equal(view.updateLanguage('javascript', revision), false);
    assert.equal(view.currentLanguage, 'sql');
  }
  assert.deepEqual(applied, ['csharp', 'sql']);
  view.apply({ Language: 'javascript', MetadataRevision: 3, Users: [], PresenceRevision: 0 });
  assert.equal(view.currentLanguage, 'javascript');
});
