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
      Limits: {
        MaxRooms: 1000,
        MaxConnections: 1000,
        NewConnectionsPerSourcePerMinute: 10000,
        InvocationsPerSecond: 1000
      },
      Logging: { LogLevel: { Default: 'Warning' } }
    })
  );
  process.on('exit', () => rmSync(root, { recursive: true, force: true }));
  return `dotnet build src/SealCode/SealCode.csproj -c Debug -p:SkipClientBuild=true && dotnet "${resolve('src/SealCode/bin/Debug/net10.0/SealCode.dll')}" --contentRoot "${root}" --webroot "${resolve('src/SealCode/wwwroot')}" --urls ${baseURL}`;
}
