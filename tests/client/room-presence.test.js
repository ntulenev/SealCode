import test from 'node:test';
import assert from 'node:assert/strict';
import { RevisionedRoomView } from '../../src/SealCode/wwwroot/room-presence.js';

test('current language follows only accepted metadata revisions', () => {
  const applied = [];
  const view = new RevisionedRoomView({
    language: (value) => applied.push(value),
    users() {}
  });
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
  view.apply({
    Language: 'javascript',
    MetadataRevision: 3,
    Users: [],
    PresenceRevision: 0
  });
  assert.equal(view.currentLanguage, 'javascript');
});

test('equal revisions are accepted and stale or invalid presence is ignored', () => {
  const languages = [];
  const users = [];
  const view = new RevisionedRoomView({
    language: (value) => languages.push(value),
    users: (value) => users.push(value)
  });
  assert.equal(view.updateLanguage('sql', 2), true);
  assert.equal(view.updateLanguage('csharp', 2), true);
  assert.equal(view.currentLanguage, 'csharp');
  assert.equal(view.updateUsers(['Alice'], 3), true);
  assert.equal(view.updateUsers(['Alice', 'Bob'], 3), true);
  for (const revision of [2, NaN, Infinity, 3.5]) {
    assert.equal(view.updateUsers(['Stale'], revision), false);
  }
  assert.deepEqual(languages, ['sql', 'csharp']);
  assert.deepEqual(users, [['Alice'], ['Alice', 'Bob']]);
  assert.equal(view.presenceRevision, 3);
});

test('apply accepts lowercase snapshots and supplies defaults for omitted fields', () => {
  const languages = [];
  const users = [];
  const view = new RevisionedRoomView({
    language: (value) => languages.push(value),
    users: (value) => users.push(value)
  });
  view.apply({});
  assert.deepEqual(languages, [undefined]);
  assert.deepEqual(users, [[]]);
  assert.equal(view.languageRevision, 0);
  assert.equal(view.presenceRevision, 0);
  view.apply({
    language: 'sql',
    metadataRevision: 2,
    users: ['Bob'],
    presenceRevision: 3
  });
  view.apply({
    Language: 'stale',
    MetadataRevision: 1,
    Users: [],
    PresenceRevision: 2
  });
  assert.equal(view.currentLanguage, 'sql');
  assert.deepEqual(users, [[], ['Bob']]);
});
