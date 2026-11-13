import { test } from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import ts from 'typescript';

test('observer wire variants accept real payloads and reject unavailable fields', () => {
  const fixturePath = path.resolve('tests/client/observer-contract.fixture.js');
  const source = `
    /** @type {import('../../src/SealCode/wwwroot/client-types.js').ObserverMessage} */
    const count = {Type: 'Count', ObserverCount: 1, ObserverRevision: 2};
    /** @type {import('../../src/SealCode/wwwroot/client-types.js').ObserverMessage} */
    const snapshot = {Type: 'Snapshot', Name: 'Room', Kind: 'code', Language: 'sql',
      Text: '', Version: 1, YjsState: null, YjsUpdates: [], DocumentRevision: 0,
      CheckpointRevision: 0, MetadataRevision: 0, PresenceRevision: 0, Users: []};
    /** @param {import('../../src/SealCode/wwwroot/client-types.js').ObserverMessage} message */
    function inspect(message) {
      if (message.Type === 'Count') {
        message.ObserverCount;
        // @ts-expect-error Count messages never carry document payloads.
        message.YjsUpdates;
      }
      if (message.Type === 'Snapshot') {
        message.Name;
        // @ts-expect-error Snapshot counts arrive in a separate message.
        message.ObserverCount;
      }
      if (message.Type === 'RequiresResync') {
        // @ts-expect-error A resync marker only contains its discriminator.
        message.DocumentRevision;
      }
    }
  `;
  const config = ts.readConfigFile('jsconfig.json', ts.sys.readFile);
  const parsed = ts.parseJsonConfigFileContent(config.config, ts.sys, process.cwd());
  const host = ts.createCompilerHost(parsed.options);
  const readFile = host.readFile, fileExists = host.fileExists;
  const isFixture = file => path.resolve(file) === fixturePath;
  host.readFile = file => isFixture(file) ? source : readFile(file);
  host.fileExists = file => isFixture(file) || fileExists(file);
  const program = ts.createProgram([fixturePath], {...parsed.options, noEmit: true}, host);
  const diagnostics = ts.getPreEmitDiagnostics(program);
  assert.deepEqual(diagnostics.map(item => ts.flattenDiagnosticMessageText(item.messageText, '\n')), []);
});
