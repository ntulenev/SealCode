// Near-limit Unicode documents and a genuinely paused WebSocket receiver.
// This starts only a disposable local host with synthetic credentials.
import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdir, writeFile } from 'node:fs/promises';
import { setTimeout as delay } from 'node:timers/promises';
import { performance } from 'node:perf_hooks';
import { request } from '@playwright/test';
import * as signalR from '@microsoft/signalr';
import * as Y from 'yjs';
import { startIsolatedHost } from './isolated-host.js';

process.env.DOTNET_GCHeapHardLimit = '0x14000000';
const host = await startIsolatedHost({ ConnectionStrings: { SealCode: '' }, Observers: { Enabled: true } });
const api = await request.newContext({ baseURL: host.baseURL });
const connections = [];
const guests = [];
const samples = [];
const doc = new Y.Doc();
const run = promisify(execFile);
const result = { profile: 'one near-limit room, 5 editors, 20 observers, one paused socket', errors: [] };
async function csrf() { return { 'X-CSRF-TOKEN': (await (await api.get('/admin/token')).json()).Token }; }
async function connect(path) {
  const c = new signalR.HubConnectionBuilder().withUrl(`${host.baseURL}/${path}`).configureLogging(signalR.LogLevel.None).build();
  c.keepAliveIntervalInMilliseconds = 5000;
  c.serverTimeoutInMilliseconds = 60000;
  connections.push(c); await c.start(); return c;
}
async function memory(stage) {
  if (process.platform !== 'win32') return;
  const { stdout } = await run('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `$p = Get-Process -Id ${host.pid}; [PSCustomObject]@{WorkingSet=$p.WorkingSet64;Private=$p.PrivateMemorySize64;CPU=$p.CPU}|ConvertTo-Json -Compress`], { windowsHide: true });
  samples.push({ stage, ...JSON.parse(stdout) });
}
let socket;
try {
  assert((await api.post('/admin/login', { headers: await csrf(), form: { name: 'BrowserAdmin', password: 'browser-pass' } })).ok());
  const created = await api.post('/admin/rooms', { headers: await csrf(), data: { Name: 'Near-limit Unicode', Language: 'sql' } });
  const { RoomId: id } = await created.json();
  const editors = [];
  for (let i = 0; i < 5; i++) { const c = await connect('roomHub'); await c.invoke('JoinRoom', id, `Editor ${i}`); editors.push(c); }
  doc.getText('monaco').insert(0, '漢'.repeat(1000000));
  const checkpoint = () => Buffer.from(Y.encodeStateAsUpdate(doc)).toString('base64');
  result.checkpointBase64Characters = checkpoint().length;
  assert(result.checkpointBase64Characters < 4 * 1024 ** 2);
  await editors[0].invoke('CheckpointYjs', id, checkpoint(), doc.getText('monaco').toString(), 0);
  const linkResponse = await api.post(`/room/${id}/observer-link`, { headers: await csrf() });
  const token = (await linkResponse.json()).Url.split('#')[1];
  await memory('5-editors');
  const started = performance.now();
  await Promise.all(Array.from({ length: 20 }, async (_, index) => {
    const connection = await connect('observerHub');
    const guest = { connection, doc: new Y.Doc(), revision: -1 };
    guests[index] = guest;
    let ready;
    let failed;
    const initial = new Promise((resolve, reject) => { ready = resolve; failed = reject; });
    connection.on('ObserverMessage', raw => {
      try {
        raw = typeof raw === 'string' ? raw : JSON.stringify(raw);
        assert(!raw.includes(id));
        const m = JSON.parse(raw);
        if (m.Type === 'RequiresResync') { void connection.invoke('SyncObserver').catch(error => result.errors.push(error.message)); return; }
        if (m.Type === 'Snapshot') {
          guest.doc.destroy(); guest.doc = new Y.Doc();
          if (m.YjsState) Y.applyUpdate(guest.doc, Buffer.from(m.YjsState, 'base64'));
          guest.revision = m.CheckpointRevision;
        }
        for (const update of m.YjsUpdates || []) {
          if (update.Revision <= guest.revision) continue;
          assert.equal(update.Revision, guest.revision + 1);
          Y.applyUpdate(guest.doc, Buffer.from(update.Update, 'base64'));
          guest.revision = update.Revision;
        }
        if (m.Type === 'Snapshot') { guest.revision = m.DocumentRevision; ready(); }
      } catch (error) { result.errors.push(error.message); failed(error); }
    });
    await connection.invoke('JoinObserver', token);
    await Promise.race([initial, delay(30000).then(() => { throw new Error('Near-limit snapshot deadline'); })]);
  }));
  result.twentyConcurrentJoinsMs = performance.now() - started;
  assert(guests.every(g => g.doc.getText('monaco').length === 1000000));
  await memory('20-large-snapshots');
  socket = guests[0].connection.connection.transport._webSocket._socket;
  assert(socket?.pause, 'The stress test requires the Node ws transport');
  socket.pause();
  let revision = 0;
  for (let i = 0; i < 30; i++) {
    const vector = Y.encodeStateVector(doc);
    doc.transact(() => {
      doc.getText('monaco').delete(0, 100000);
      doc.getText('monaco').insert(0, (i % 2 ? '漢' : '語').repeat(100000));
    });
    const update = Buffer.from(Y.encodeStateAsUpdate(doc, vector)).toString('base64');
    await editors[i % 5].invoke('UpdateYjs', id, update);
    revision++;
    if (revision % 4 === 0) await editors[0].invoke('CheckpointYjs', id, checkpoint(), doc.getText('monaco').toString(), revision);
    await delay(600);
    if (i % 10 === 9) await memory(`large-updates-${i + 1}`);
  }
  await delay(12000);
  const probe = await editors[0].invoke('GetRoomRevisions', id);
  result.observersAfterSlowConsumerTimeout = probe.ObserverCount;
  assert.equal(probe.ObserverCount, 19);
  await delay(1200);
  assert(guests.slice(1).every(g => g.revision === revision && g.doc.getText('monaco').toString() === doc.getText('monaco').toString()));
  result.healthyGuestsConverged = true;
  result.editorRevision = probe.DocumentRevision;
  await memory('slow-consumer-removed');
} catch (error) { result.errors.push(error.stack); console.log(host.diagnostics()); process.exitCode = 1; }
finally {
  socket?.resume();
  await Promise.allSettled(connections.map(c => c.stop()));
  guests.forEach(g => g.doc.destroy()); doc.destroy();
  await api.dispose(); await host.stop();
  result.samples = samples;
  result.peakWorkingSetMiB = Math.max(...samples.map(s => s.WorkingSet)) / 1024 ** 2;
  result.peakPrivateMiB = Math.max(...samples.map(s => s.Private)) / 1024 ** 2;
  await mkdir('output/observer-load', { recursive: true });
  await writeFile(`output/observer-load/large-${Date.now()}.json`, JSON.stringify(result, null, 2));
  console.log(JSON.stringify(result, null, 2));
}
