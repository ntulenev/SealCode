import { mkdtempSync, writeFileSync, rmSync, cpSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve, join } from 'node:path';

export function testHostCommand(baseURL, admins) {
  const root = mkdtempSync(join(tmpdir(), 'sealcode-tests-'));
  cpSync(resolve('src/SealCode/Resources/Statistics'), join(root, 'Resources', 'Statistics'), { recursive: true });
  writeFileSync(
    join(root, 'appsettings.json'),
    JSON.stringify({
      AdminUsers: admins,
      Languages: ['csharp', 'sql', 'javascript'],
      MaxUsersPerRoom: 5,
      Limits: {
        MaxRooms: 1000,
        MaxConnections: 1000,
        MaxConnectionsPerSource: 1000,
        UnjoinedConnectionTimeoutSeconds: 3600,
        NewConnectionsPerSourcePerMinute: 10000,
        InvocationsPerSecond: 1000
      },
      Logging: { LogLevel: { Default: 'Warning' } }
    })
  );
  process.on('exit', () => rmSync(root, { recursive: true, force: true }));
  const host = `dotnet "${resolve('src/SealCode/bin/Debug/net10.0/SealCode.dll')}" --contentRoot "${root}" --webroot "${resolve('src/SealCode/wwwroot')}" --urls ${baseURL}`;
  // A soak test may be holding the same Windows DLL open. Callers opting out
  // of the build must have built the current revision explicitly first.
  return process.env.SEALCODE_E2E_SKIP_BUILD === 'true'
    ? host : `dotnet build src/SealCode/SealCode.csproj -c Debug -p:SkipClientBuild=true && ${host}`;
}
