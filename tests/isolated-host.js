import { spawn } from 'node:child_process';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { setTimeout } from 'node:timers/promises';

const children = new Set();
function killChildren() {
  for (const child of children) child.kill();
}
process.on('exit', killChildren);
for (const signal of ['SIGINT', 'SIGTERM']) {
  const handler = () => {
    killChildren();
    process.removeListener(signal, handler);
    process.kill(process.pid, signal);
  };
  process.on(signal, handler);
}

/** Owns disposable hosts through Playwright teardown, including test timeouts. */
export async function isolatedHostFixture({}, use) {
  const hosts = [];
  try {
    await use(async (settings) => {
      const host = await startIsolatedHost(settings);
      hosts.push(host);
      return host;
    });
  } finally {
    await Promise.all(hosts.map((host) => host.stop()));
  }
}

/** Starts a disposable host for tests that need different server limits. */
export async function startIsolatedHost(settings) {
  const reservation = createServer();
  await new Promise((done) => reservation.listen(0, '127.0.0.1', done));
  const port = reservation.address().port;
  await new Promise((done) => reservation.close(done));
  const root = await mkdtemp(join(tmpdir(), 'sealcode-isolated-'));
  await writeFile(
    join(root, 'appsettings.json'),
    JSON.stringify({
      AdminUsers: [
        { Name: 'BrowserAdmin', Password: 'browser-pass', IsSuperAdmin: true }
      ],
      Languages: ['csharp', 'sql', 'javascript'],
      MaxUsersPerRoom: 5,
      Logging: { LogLevel: { Default: 'Warning' } },
      ...settings,
      Limits: {
        MaxConnections: 1000,
        MaxConnectionsPerSource: Math.min(1000, settings.Limits?.MaxConnections ?? 1000),
        UnjoinedConnectionTimeoutSeconds: 3600,
        NewConnectionsPerSourcePerMinute: 10000,
        InvocationsPerSecond: 1000,
        ...settings.Limits
      }
    })
  );
  const baseURL = `http://127.0.0.1:${port}`;
  const child = spawn(
    'dotnet',
    [
      resolve('src/SealCode/bin/Debug/net10.0/SealCode.dll'),
      '--contentRoot',
      root,
      '--webroot',
      resolve('src/SealCode/wwwroot'),
      '--urls',
      baseURL
    ],
    { env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development' }, windowsHide: true }
  );
  children.add(child);
  let spawnError;
  let output = '';
  child.stdout.on('data', (value) => {
    output += value;
  });
  child.stderr.on('data', (value) => {
    output += value;
  });
  const exited = new Promise((done) => {
    child.once('exit', done);
    child.once('error', (error) => {
      spawnError = new Error(`Could not start isolated dotnet host: ${error.message}`, {
        cause: error
      });
      done();
    });
  });
  let stopped;
  const stop = () =>
    (stopped ??= (async () => {
      if (!spawnError && child.exitCode === null) child.kill();
      await exited;
      children.delete(child);
      await rm(root, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
    })());
  try {
    for (let attempt = 0; attempt < 200; attempt++) {
      if (spawnError) throw spawnError;
      if (child.exitCode !== null)
        throw new Error(`Isolated dotnet host exited: ${output}`);
      try {
        if ((await fetch(`${baseURL}/health`, { signal: AbortSignal.timeout(1000) })).ok)
          return { baseURL, stop };
      } catch {
        /* The listener is not ready yet. */
      }
      await setTimeout(50);
    }
    throw new Error(`Isolated host did not start: ${output}`);
  } catch (error) {
    await stop();
    throw error;
  }
}
