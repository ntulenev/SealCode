// Disposable local host only. Never reads or copies the repository's appsettings.
import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { writeFile, mkdir } from 'node:fs/promises';
import { setTimeout as delay } from 'node:timers/promises';
import { performance } from 'node:perf_hooks';
import { request } from '@playwright/test';
import * as signalR from '@microsoft/signalr';
import * as Y from 'yjs';
import { startIsolatedHost } from './isolated-host.js';

const run = promisify(execFile);
const minutes = Number(process.argv[2] || 3);
assert(Number.isFinite(minutes) && minutes > 0 && minutes <= 60);
// Keep a heap reserve inside the 512 MiB target; this is not an OS process limit.
process.env.DOTNET_GCHeapHardLimit = '0x14000000';
const host = await startIsolatedHost({
  ConnectionStrings: { SealCode: '' },
  TrustedProxies: ['127.0.0.1'],
  Observers: { Enabled: true },
  Logging: { LogLevel: { Default: 'Warning', 'SealCode.Observers': 'Debug' } },
  Limits: { MaxRooms: 32, MaxConnections: 160, MaxConnectionsPerSource: 20,
    NewConnectionsPerSourcePerMinute: 10000, InvocationsPerSecond: 100 }
});
const api = await request.newContext({ baseURL: host.baseURL });
const editors = [];
const guests = [];
const rooms = [];
const samples = [];
const editorLatency = [];
const guestLatency = [];
let bytes = 0;
let sampling = false;
let sampler;
const report = { minutes, targetMemoryMiB: 512, managedHeapLimitMiB: 320, serverPid: host.pid,
  hostArchitecture: process.arch, stages: [], boundaries: {}, errors: [] };

async function sample(stage) {
  if (process.platform !== 'win32') return;
  const { stdout } = await run('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command',
    `$p = Get-Process -Id ${host.pid}; [PSCustomObject]@{WorkingSet=$p.WorkingSet64;Private=$p.PrivateMemorySize64;CPU=$p.CPU}|ConvertTo-Json -Compress`], { windowsHide: true });
  samples.push({ stage, elapsed: performance.now(), ...JSON.parse(stdout) });
  if (stage !== 'soak') console.log(JSON.stringify({ stage, workingSetMiB: samples.at(-1).WorkingSet / 1024 ** 2, privateMiB: samples.at(-1).Private / 1024 ** 2 }));
}
async function csrf() { return { 'X-CSRF-TOKEN': (await (await api.get('/admin/token')).json()).Token }; }
async function mutate(path, data, method = 'post') {
  const response = await api[method](path, { headers: await csrf(), data });
  assert(response.ok(), `${method} ${path}: ${response.status()}`);
  return response;
}
async function connect(path, source) {
  const connection = new signalR.HubConnectionBuilder().withUrl(`${host.baseURL}/${path}`, {
    headers: { 'X-Forwarded-For': source }
  }).configureLogging(signalR.LogLevel.None).build();
  connection.keepAliveIntervalInMilliseconds = 5000;
  await connection.start();
  return connection;
}
async function rejectedConnection(path, source) {
  const connection = new signalR.HubConnectionBuilder().withUrl(`${host.baseURL}/${path}`, {
    headers: { 'X-Forwarded-For': source }
  }).configureLogging(signalR.LogLevel.None).build();
  connection.keepAliveIntervalInMilliseconds = 5000;
  const closed = new Promise(resolve => connection.onclose(resolve));
  try {
    try { await connection.start(); } catch { return; }
    await Promise.race([closed, delay(3000).then(() => { throw new Error('Excess connection was admitted'); })]);
  } finally { await connection.stop(); }
}
async function observer(room, index) {
  const connection = await connect('observerHub', `198.51.100.${1 + Math.floor(index / 10)}`);
  const guest = { connection, room, revision: -1, snapshots: 0, doc: new Y.Doc() };
  let resolveInitial;
  let rejectInitial;
  const initial = new Promise((resolve, reject) => { resolveInitial = resolve; rejectInitial = reject; });
  connection.onclose(error => { if (guest.revision < 0) rejectInitial(new Error(`Guest disconnected before snapshot: ${error?.message || 'closed'}`)); });
  connection.on('ObserverMessage', raw => {
    try {
      raw = typeof raw === 'string' ? raw : JSON.stringify(raw);
      bytes += Buffer.byteLength(raw);
      const previousRevision = guest.revision;
      assert(!raw.includes(room.id), 'Internal room ID leaked');
      const message = JSON.parse(raw);
      if (message.Type === 'RequiresResync' || (message.RequiresResync && message.DocumentRevision > guest.revision)) {
        void connection.invoke('SyncObserver').catch(error => report.errors.push(error.message));
        return;
      }
      if (message.Type === 'Snapshot') {
        guest.doc.destroy(); guest.doc = new Y.Doc();
        if (message.YjsState) Y.applyUpdate(guest.doc, Buffer.from(message.YjsState, 'base64'));
        guest.revision = message.CheckpointRevision;
        guest.snapshots++;
      }
      for (const update of message.YjsUpdates || []) {
        if (update.Revision <= guest.revision) continue;
        assert.equal(update.Revision, guest.revision + 1);
        Y.applyUpdate(guest.doc, Buffer.from(update.Update, 'base64'));
        guest.revision = update.Revision;
      }
      if (message.Type === 'Snapshot') guest.revision = message.DocumentRevision;
      if (message.Type === 'Update' && guest.revision > previousRevision && room.sentAt.has(guest.revision))
        guestLatency.push(performance.now() - room.sentAt.get(guest.revision));
      if (message.Type === 'Snapshot') resolveInitial();
    } catch (error) { report.errors.push(error.message); rejectInitial(error); }
  });
  const started = performance.now();
  try {
    await connection.invoke('JoinObserver', room.token);
    await Promise.race([initial, delay(20000).then(() => { throw new Error('Snapshot deadline'); })]);
    guest.initialMs = performance.now() - started;
    return guest;
  } catch (error) { await connection.stop(); guest.doc.destroy(); throw error; }
}
function percentile(values, p) { return values.length ? [...values].sort((a, b) => a - b)[Math.min(values.length - 1, Math.floor(values.length * p))] : null; }
async function edits(durationMs) {
  const end = performance.now() + durationMs;
  while (performance.now() < end) {
    for (const room of rooms.slice(0, 2)) {
      const doc = room.doc;
      const before = Y.encodeStateVector(doc);
      doc.getText('monaco').insert(doc.getText('monaco').length, 'x');
      const update = Buffer.from(Y.encodeStateAsUpdate(doc, before)).toString('base64');
      room.revision++;
      room.sentAt.set(room.revision, performance.now());
      if (room.sentAt.size > 200) room.sentAt.delete(room.sentAt.keys().next().value);
      await room.editors[room.revision % 5].invoke('UpdateYjs', room.id, update);
      if (room.revision % 64 === 0) {
        await room.editors[0].invoke('CheckpointYjs', room.id, Buffer.from(Y.encodeStateAsUpdate(doc)).toString('base64'), doc.getText('monaco').toString(), room.revision);
      }
    }
    await delay(200);
  }
}
try {
  const login = await api.post('/admin/login', { headers: await csrf(), form: { name: 'BrowserAdmin', password: 'browser-pass' } });
  assert(login.ok());
  await sample('idle');
  for (let r = 0; r < 32; r++) {
    const created = await (await mutate('/admin/rooms', { Name: `Load ${r}`, Language: 'sql' })).json();
    const room = { id: created.RoomId, editors: [], doc: new Y.Doc(), sentAt: new Map(), revision: 0 };
    rooms.push(room);
    for (let e = 0; e < 5; e++) {
      const connection = await connect('roomHub', `192.0.2.${1 + Math.floor(editors.length / 20)}`);
      editors.push(connection); room.editors.push(connection);
      await connection.invoke('JoinRoom', room.id, `Editor ${e}`);
      if (e === 1) connection.on('YjsUpdated', (_update, rev) => {
        if (room.sentAt.has(rev)) editorLatency.push(performance.now() - room.sentAt.get(rev));
      });
    }
    room.doc.getText('monaco').insert(0, 'a'.repeat(500000));
    await room.editors[0].invoke('CheckpointYjs', room.id, Buffer.from(Y.encodeStateAsUpdate(room.doc)).toString('base64'), room.doc.getText('monaco').toString(), 0);
    if (r < 3) room.token = (await (await mutate(`/room/${room.id}/observer-link`)).json()).Url.split('#')[1];
  }
  await edits(10000);
  await sample('160-editors-baseline');
  report.baselineEditorP95Ms = percentile(editorLatency, 0.95);
  editorLatency.length = 0;
  await rejectedConnection('roomHub', '192.0.2.100');
  report.boundaries.editor161Rejected = true;
  await editors[159].stop();
  const sixth = await connect('roomHub', '192.0.2.100');
  await assert.rejects(sixth.invoke('JoinRoom', rooms[0].id, 'Sixth'));
  await sixth.stop();
  const replacement = await connect('roomHub', '192.0.2.8');
  await replacement.invoke('JoinRoom', rooms[31].id, 'Editor 4'); editors[159] = replacement; rooms[31].editors[4] = replacement;
  report.boundaries.editor6Rejected = true;
  guests.push(...await Promise.all(Array.from({ length: 20 }, (_, i) => observer(rooms[0], i))));
  await edits(5000); await sample('160-editors-20-guests');
  const overflowRoom = await connect('observerHub', '198.51.100.50');
  await assert.rejects(overflowRoom.invoke('JoinObserver', rooms[0].token), /ObserverLimitReached/);
  await overflowRoom.stop(); report.boundaries.guest21Rejected = true;
  guests.push(...await Promise.all(Array.from({ length: 20 }, (_, i) => observer(rooms[1], i + 20))));
  await sample('160-editors-40-guests');
  await rejectedConnection('observerHub', '198.51.100.60');
  report.boundaries.guest41Rejected = true;
  for (const method of ['UpdateYjs', 'CheckpointYjs', 'SetLanguage', 'UpdateCursor', 'UpdateBoardCursor', 'UpdateSelection', 'UpdateCopy', 'JoinRoom']) {
    await delay(220); // Stay inside the guest invocation rate while probing forbidden methods.
    await assert.rejects(guests[0].connection.invoke(method, rooms[0].id, 'AQ=='));
  }
  report.boundaries.guestMutationsRejected = true;
  await sample('soak-start');
  sampling = true;
  sampler = (async () => { while (sampling) { await delay(5000); if (sampling) await sample('soak'); } })();
  for (let cycle = 0; cycle < 5; cycle++) {
    await edits(minutes * 60000 / 5);
    // Reconnect all 40 simultaneously using fresh disposable source addresses.
    // The dedicated source-window test covers 30/min without hiding admission races.
    const group = guests.splice(0);
    await Promise.all(group.map(guest => guest.connection.stop()));
    for (const guest of group) guest.doc.destroy();
    await delay(500);
    guests.push(...await Promise.all(group.map((guest, i) => observer(guest.room, 100 + cycle * 100 + i))));
    await sample(`reconnect-${cycle + 1}`);
  }
  await delay(1500);
  for (const guest of guests) assert.equal(guest.doc.getText('monaco').toString(), guest.room.doc.getText('monaco').toString());
  report.boundaries.allGuestsConverged = true;
  report.initialGuestP95Ms = percentile(guests.map(guest => guest.initialMs), 0.95);
  report.editorP95Ms = percentile(editorLatency, 0.95);
  report.guestP95Ms = percentile(guestLatency, 0.95);
  report.guestPayloadBytes = bytes;
  await mutate(`/room/${rooms[0].id}/observer-link`, undefined, 'delete');
  await delay(1200);
  assert(guests.filter(guest => guest.room === rooms[0]).every(guest => guest.connection.state === signalR.HubConnectionState.Disconnected));
  report.boundaries.revokeDisconnects = true;
  const rejected = await connect('observerHub', '198.51.100.70');
  await assert.rejects(rejected.invoke('JoinObserver', rooms[0].token), /ObserverUnavailable/); await rejected.stop();
  report.boundaries.revokedTokenRejected = true;
  await Promise.all(guests.map(guest => guest.connection.stop()));
  await delay(1500); await sample('guests-left');
  await Promise.all(editors.map(connection => connection.stop()));
  for (const room of rooms) await mutate(`/admin/rooms/${room.id}`, undefined, 'delete');
  await delay(3000); await sample('rooms-deleted');
} catch (error) {
  console.log(host.diagnostics());
  report.errors.push(error.stack); process.exitCode = 1;
} finally {
  sampling = false;
  if (sampler) await sampler.catch(error => report.errors.push(error.message));
  report.samples = samples;
  report.peakWorkingSetMiB = Math.max(...samples.map(s => s.WorkingSet)) / 1024 ** 2;
  report.peakPrivateMiB = Math.max(...samples.map(s => s.Private)) / 1024 ** 2;
  await Promise.allSettled([...guests.map(g => g.connection.stop()), ...editors.map(c => c.stop())]);
  guests.forEach(guest => guest.doc.destroy()); rooms.forEach(room => room.doc.destroy());
  await api.dispose(); await host.stop();
  await mkdir('output/observer-load', { recursive: true });
  await writeFile(`output/observer-load/report-${Date.now()}.json`, JSON.stringify(report, null, 2));
  console.log(JSON.stringify({ ...report, samples: `${samples.length} samples` }, null, 2));
}
