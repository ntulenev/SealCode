import { spawn } from 'node:child_process';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { setTimeout } from 'node:timers/promises';

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
      ...settings
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
  let output = '';
  child.stdout.on('data', (value) => {
    output += value;
  });
  child.stderr.on('data', (value) => {
    output += value;
  });
  const exited = new Promise((done) => child.once('exit', done));
  const stop = async () => {
    if (child.exitCode === null) child.kill();
    await exited;
    await rm(root, { recursive: true, force: true });
  };
  try {
    for (let attempt = 0; attempt < 200; attempt++) {
      if (child.exitCode !== null) throw new Error(output);
      try {
        if ((await fetch(`${baseURL}/health`)).ok) return { baseURL, stop };
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
