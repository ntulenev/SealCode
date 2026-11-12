import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createAdminLimitsPage } from '../../src/SealCode/wwwroot/admin-limits.js';

function element() {
  return {
    children: [], textContent: '', hidden: false, disabled: false, handlers: {},
    append(...children) { this.children.push(...children); },
    replaceChildren() { this.children = []; },
    addEventListener(event, handler) { this.handlers[event] = handler; }
  };
}
const config = {
  MaxUsersPerRoom: 3,
  Observers: { Enabled: false, MaxPerRoom: 20, MaxConnections: 40, MaxConnectionsPerSource: 20,
    UnjoinedTimeoutSeconds: 15, NewConnectionsPerSourcePerMinute: 30, InvocationsPerSecond: 5,
    SnapshotRequestsPerConnectionPerMinute: 4, ConcurrentSnapshots: 2,
    DeliveryQueueBytesPerConnection: 1048576, DeliveryQueueEventsPerConnection: 256, SendTimeoutSeconds: 10 },
  Limits: {
    MaxRooms: 12, MaxConnections: 80, MaxConnectionsPerSource: 20,
    RoomIdleTimeoutMinutes: 0, UnjoinedConnectionTimeoutSeconds: 60,
    MaxSnapshotCharacters: 1048576, MaxEncodedUpdateBytes: 4194304,
    MaxEncodedCheckpointBytes: 4194304, MaxJournalBytes: 4194304,
    MaxJournalCount: 2048, CheckpointEveryRevisions: 64,
    CheckpointEveryBase64Bytes: 262144, InvocationsPerSecond: 100,
    NewConnectionsPerSourcePerMinute: 30
  }
};
async function fixture(fetchConfig = async () => ({ ok: true, json: async () => config })) {
  const elements = Object.fromEntries([
    'limitsStatus', 'limitsContent', 'retryLimits', 'adminGreeting',
    'capacityLimits', 'observerLimits', 'observerTechnicalLimits', 'lifecycleLimits', 'technicalLimits'
  ].map((id) => [id, element()]));
  const page = createAdminLimitsPage({
    document: { getElementById: (id) => elements[id], createElement: element },
    fetch: (url) => url === '/admin/me'
      ? Promise.resolve({ ok: true, json: async () => ({ DisplayName: 'Admin' }) })
      : fetchConfig()
  });
  await page.ready;
  return elements;
}

test('renders configured values and units, including disabled cleanup', async () => {
  const elements = await fixture();
  assert.equal(elements.adminGreeting.textContent, 'Hello, Admin!');
  assert.equal(elements.capacityLimits.children[0].children[1].textContent, '12');
  assert.equal(elements.capacityLimits.children[1].children[1].textContent, '3');
  assert.equal(elements.lifecycleLimits.children[0].children[1].textContent, 'Disabled');
  assert.equal(elements.technicalLimits.children[0].children[1].textContent, '1,048,576 characters');
  assert.equal(elements.technicalLimits.children[1].children[1].textContent, '4,096 KiB');
  assert.equal(elements.limitsContent.hidden, false);
  assert.equal(elements.retryLimits.hidden, true);
  assert.equal(elements.limitsStatus.textContent, '');
  assert.deepEqual(elements.observerLimits.children.slice(0, 5).map(row => row.children[1].textContent), ['Disabled', '20', '40', '20', '80']);
});

test('observer limits use configured values and include enabled guest pool in the total', async () => {
  const elements = await fixture(async () => ({ ok: true, json: async () => ({
    ...config, Observers: { ...config.Observers, Enabled: true, MaxPerRoom: 12, MaxConnections: 30 }
  }) }));
  assert.deepEqual(elements.observerLimits.children.slice(0, 5).map(row => row.children[1].textContent), ['Enabled', '12', '30', '20', '110']);
  assert.equal(elements.observerTechnicalLimits.children[5].children[1].textContent, '1,024 KiB');
});

test('retry recovers from a request failure and renders an enabled timeout', async () => {
  let calls = 0;
  const elements = await fixture(async () => ++calls === 1 ? { ok: false, status: 500 } : {
    ok: true, json: async () => ({ ...config, Limits: { ...config.Limits, RoomIdleTimeoutMinutes: 45 } })
  });
  assert.equal(elements.limitsContent.hidden, true);
  assert.equal(elements.retryLimits.hidden, false);
  assert.equal(elements.limitsStatus.textContent, 'Could not load the configured limits. Try again.');
  await elements.retryLimits.handlers.click();
  assert.equal(elements.lifecycleLimits.children[0].children[1].textContent, '45 minutes');
  assert.equal(elements.retryLimits.disabled, false);
  assert.equal(elements.capacityLimits.children.length, 4);
});

for (const response of [{ status: 401 }, { redirected: true }]) {
  test(`expired session hides limits (${JSON.stringify(response)})`, async () => {
    const elements = await fixture(async () => response);
    assert.equal(elements.limitsContent.hidden, true);
    assert.equal(elements.limitsStatus.textContent, 'Session expired. Sign in again.');
  });
}

test('network failures show a retryable error', async () => {
  const elements = await fixture(async () => { throw new TypeError('Failed to fetch'); });
  assert.equal(elements.limitsContent.hidden, true);
  assert.equal(elements.retryLimits.hidden, false);
});
