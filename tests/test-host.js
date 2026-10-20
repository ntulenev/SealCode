import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve, join } from 'node:path';

export function testHostCommand(baseURL, admins) {
  const root = mkdtempSync(join(tmpdir(), 'sealcode-tests-'));
  writeFileSync(
    join(root, 'appsettings.json'),
    JSON.stringify({
      AdminUsers: admins,
      Languages: ['csharp', 'sql', 'javascript'],
      MaxUsersPerRoom: 5,
      Logging: { LogLevel: { Default: 'Warning' } }
    })
  );
  process.on('exit', () => rmSync(root, { recursive: true, force: true }));
  return `dotnet "${resolve('src/SealCode/bin/Debug/net10.0/SealCode.dll')}" --contentRoot "${root}" --webroot "${resolve('src/SealCode/wwwroot')}" --urls ${baseURL}`;
}
