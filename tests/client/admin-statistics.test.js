import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createAdminStatisticsPage } from '../../src/SealCode/wwwroot/admin-statistics.js';

function element() {
  return {
    children: [], textContent: '', hidden: false, disabled: false, handlers: {}, value: '2000-01-01', valid: true,
    get firstElementChild() { return this.children[0]; },
    append(...children) { this.children.push(...children); },
    replaceChildren() { this.children = []; this.textContent = ''; },
    addEventListener(event, handler) { this.handlers[event] = handler; },
    reportValidity() { return this.valid; }
  };
}
const report = {
  Logins: 3, Rooms: 2, UsersCount: 1, EventsCount: 5,
  FirstActivityUtc: '2026-01-01T00:00:00Z', LastActivityUtc: '2026-01-02T00:00:00Z',
  Breakdown: [{ Kind: 'code', Language: 'csharp', Count: 1 }, { Kind: 'whiteboard', Language: 'plaintext', Count: 1 }],
  Users: [{ Id: 'USER+?&', DisplayName: '<img src=x>', Logins: 3, Rooms: 2, LastActivityUtc: '2026-01-02T00:00:00Z' }],
  Events: [
    { Type: 'login', Provider: 'google', OccurredAtUtc: '2026-01-01T00:00:00Z' },
    { Type: 'room', Kind: 'code', Language: 'csharp', RoomName: '<script>room</script>', OccurredAtUtc: '2026-01-02T00:00:00Z' },
    { Type: 'room', Kind: 'whiteboard', Language: 'plaintext', RoomName: 'Diagram', OccurredAtUtc: '2026-01-02T00:00:00Z' }
  ]
};
const ok = value => ({ ok: true, json: async () => value });
const event = { preventDefault() {} };
function deferred() {
  let resolve;
  let reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}
async function fixture({ fetchData = async () => ok(report), confirm = () => true, fetchToken = async () => ok({ Token: 'csrf-token' }) } = {}) {
  const elements = Object.fromEntries([
    'statisticsStatus', 'statisticsCutoff', 'previousPage', 'nextPage', 'allUsers', 'statisticsSummary',
    'statisticsBreakdown', 'summaryTitle', 'detailsTitle', 'statisticsHead', 'statisticsDetails',
    'statisticsPage', 'refreshStatistics', 'pruneStatistics', 'deleteButton'
  ].map(id => [id, element()]));
  const calls = [];
  const page = createAdminStatisticsPage({
    document: { getElementById: id => elements[id], createElement: element, querySelector: () => elements.deleteButton },
    fetch: async (url, options) => {
      calls.push({ url, options });
      return url === '/admin/token' ? fetchToken() : fetchData(url, options);
    },
    confirm
  });
  await page.ready;
  return { page, elements, calls, select: () => elements.statisticsDetails.children[0].children[0].children[0].handlers.click(), submit: () => elements.pruneStatistics.handlers.submit(event) };
}

test('renders totals, code and whiteboard breakdown, UTC dates and untrusted names as text', async () => {
  const { elements, select, calls } = await fixture();
  assert.equal(elements.statisticsSummary.children[1].textContent, '3');
  assert.equal(elements.statisticsSummary.children[7].textContent, '2026-01-01 00:00:00 UTC');
  assert.equal(elements.statisticsBreakdown.children[1].children[1].textContent, '—');
  assert.equal(elements.statisticsDetails.children[0].children[0].children[0].textContent, '<img src=x>');
  await select();
  assert.equal(new URL(calls.at(-1).url, 'http://test').searchParams.get('userId'), 'USER+?&');
  assert.equal(elements.summaryTitle.textContent, '<img src=x>');
  assert.equal(elements.statisticsDetails.children[1].children[3].textContent, '<script>room</script>');
  assert.equal(elements.statisticsDetails.children[2].children[5].textContent, '—');
  await elements.allUsers.handlers.click();
  assert.equal(elements.summaryTitle.textContent, 'All users');
  assert.equal(elements.allUsers.hidden, true);
});

test('paginates users and history and resets the offset when selecting an account', async () => {
  const { elements, calls, select } = await fixture({ fetchData: async () => ok({ ...report, UsersCount: 51, EventsCount: 51, Users: Array(50).fill(report.Users[0]), Events: Array(50).fill(report.Events[0]) }) });
  assert.equal(elements.nextPage.disabled, false);
  await elements.nextPage.handlers.click();
  assert.match(calls.at(-1).url, /offset=50/);
  assert.equal(elements.previousPage.disabled, false);
  await select();
  assert.match(calls.at(-1).url, /offset=0/);
  await elements.nextPage.handlers.click();
  assert.match(calls.at(-1).url, /offset=50/);
  assert.equal(elements.nextPage.disabled, true);
  await elements.previousPage.handlers.click();
  assert.match(calls.at(-1).url, /offset=0/);
});

test('empty reports display explicit empty states and disable pagination', async () => {
  const { elements } = await fixture({ fetchData: async () => ok({ ...report, UsersCount: 0, FirstActivityUtc: null, LastActivityUtc: null, Breakdown: [], Users: [] }) });
  assert.equal(elements.statisticsDetails.children[0].children[0].textContent, 'No activity recorded.');
  assert.equal(elements.statisticsBreakdown.children[0].children[0].textContent, 'No rooms recorded yet.');
  assert.equal(elements.statisticsSummary.children[7].textContent, '—');
  assert.equal(elements.statisticsPage.textContent, '0 results');
  assert.equal(elements.nextPage.disabled, true);
});

for (const [status, message] of [[401, 'Session expired'], [403, 'super admins only'], [500, 'Try Refresh']]) {
  test(`HTTP ${status} clears stale data and Refresh recovers`, async () => {
    let fail = false;
    const { elements, page } = await fixture({ fetchData: async () => fail ? { ok: false, status } : ok(report) });
    fail = true;
    await page.reload();
    assert.equal(elements.statisticsDetails.children.length, 0);
    assert.ok(elements.statisticsStatus.textContent.includes(message));
    fail = false;
    await elements.refreshStatistics.handlers.click();
    assert.equal(elements.statisticsStatus.textContent, '');
  });
}

for (const fails of [false, true]) {
  test(`ignores stale ${fails ? 'errors' : 'responses'} after a newer report`, async () => {
    const pending = deferred();
    let count = 0;
    const { page, elements } = await fixture({ fetchData: async () => ++count === 2 ? pending.promise : ok(report) });
    const older = page.reload();
    await page.reload();
    if (fails) pending.reject(new Error('old failure')); else pending.resolve(ok({ ...report, Logins: 999 }));
    await older;
    assert.equal(elements.statisticsSummary.children[1].textContent, '3');
    assert.equal(elements.statisticsStatus.textContent, '');
  });
}

test('invalid and canceled retention forms never send a deletion', async () => {
  let confirmations = 0;
  const { elements, submit, calls } = await fixture({ confirm: () => { confirmations++; return false; } });
  elements.statisticsCutoff.valid = false;
  await submit();
  assert.equal(confirmations, 0);
  elements.statisticsCutoff.valid = true;
  await submit();
  assert.equal(confirmations, 1);
  assert.equal(calls.length, 1);
});

test('confirmed retention uses UTC and CSRF, prevents duplicate submits and refreshes totals', async () => {
  const pending = deferred();
  const { elements, submit, calls } = await fixture({ fetchData: async (url, options) => options?.method === 'DELETE' ? pending.promise : ok(report) });
  const deleting = submit();
  await Promise.resolve();
  await submit();
  assert.equal(elements.deleteButton.disabled, true);
  pending.resolve(ok({ DeletedEvents: 12 }));
  await deleting;
  const mutations = calls.filter(call => call.options?.method === 'DELETE');
  assert.equal(mutations.length, 1);
  assert.equal(new URL(mutations[0].url, 'http://test').searchParams.get('before'), '2000-01-01T00:00:00Z');
  assert.equal(mutations[0].options.headers['X-CSRF-TOKEN'], 'csrf-token');
  assert.equal(elements.statisticsStatus.textContent, '12 old events deleted.');
  assert.equal(elements.deleteButton.disabled, false);
});

test('failed CSRF acquisition does not send a deletion', async () => {
  const { submit, elements, calls } = await fixture({ fetchToken: async () => ({ ok: false, status: 401 }) });
  await submit();
  assert.equal(calls.filter(call => call.options?.method === 'DELETE').length, 0);
  assert.match(elements.statisticsStatus.textContent, /Session expired/);
  assert.equal(elements.deleteButton.disabled, false);
});

test('deletion failure and failed refresh remain visible and reenable the form', async () => {
  let failRead = false;
  let failDelete = true;
  const { elements, submit } = await fixture({ fetchData: async (url, options) => {
    if (options?.method === 'DELETE') { failRead = true; return failDelete ? { ok: false } : ok({ DeletedEvents: 1 }); }
    return failRead ? { ok: false, status: 500 } : ok(report);
  } });
  await submit();
  assert.match(elements.statisticsStatus.textContent, /Unable to delete/);
  failDelete = false;
  await submit();
  assert.match(elements.statisticsStatus.textContent, /Unable to load/);
  assert.equal(elements.deleteButton.disabled, false);
});

test('non-Error network rejections produce usable messages', async () => {
  const { elements, submit } = await fixture({ fetchData: async () => { throw 'offline'; } });
  assert.equal(elements.statisticsStatus.textContent, 'Unable to load statistics.');
  await submit();
  assert.equal(elements.statisticsStatus.textContent, 'Unable to delete statistics.');
});
