import { token } from './admin-token.js';

/** @param {{document: Document, fetch: typeof fetch, confirm?: (message: string) => boolean}} options */
export function createAdminStatisticsPage({ document, fetch, confirm = message => window.confirm(message) }) {
  const status = /** @type {HTMLElement} */ (document.getElementById('statisticsStatus'));
  const cutoff = /** @type {HTMLInputElement} */ (document.getElementById('statisticsCutoff'));
  const previous = /** @type {HTMLButtonElement} */ (document.getElementById('previousPage'));
  const next = /** @type {HTMLButtonElement} */ (document.getElementById('nextPage'));
  const back = /** @type {HTMLButtonElement} */ (document.getElementById('allUsers'));
  let userId = '';
  let userName = '';
  let offset = 0;
  let requestId = 0;
  cutoff.max = new Date().toISOString().slice(0, 10);

  /** @param {unknown} value */
  function date(value) {
    return value ? new Date(String(value)).toISOString().slice(0, 19).replace('T', ' ') + ' UTC' : '—';
  }

  /** @param {HTMLElement} parent @param {unknown[]} values @param {boolean} [header] */
  function row(parent, values, header = false) {
    const tr = document.createElement('tr');
    for (const value of values) {
      const cell = document.createElement(header ? 'th' : 'td');
      cell.textContent = String(value ?? '—');
      tr.append(cell);
    }
    parent.append(tr);
    return tr;
  }

  /** @param {string} id */
  function clear(id) {
    const element = /** @type {HTMLElement} */ (document.getElementById(id));
    element.replaceChildren();
    return element;
  }

  async function load() {
    const current = ++requestId;
    previous.disabled = next.disabled = true;
    status.textContent = 'Loading statistics…';
    try {
      const query = new URLSearchParams({ offset: String(offset) });
      if (userId) query.set('userId', userId);
      const response = await fetch(`/admin/statistics/data?${query}`, { cache: 'no-store' });
      if (!response.ok) throw new Error(response.status === 401 ? 'Session expired. Sign in again.' : response.status === 403 ? 'Statistics are available to super admins only.' : 'Unable to load statistics. Try Refresh.');
      const report = await response.json();
      if (current !== requestId) return false;
      const summary = clear('statisticsSummary');
      for (const [label, value] of [['Successful sign-ins', report.Logins], ['Rooms created', report.Rooms], ['Users', report.UsersCount], ['First recorded activity', date(report.FirstActivityUtc)], ['Last recorded activity', date(report.LastActivityUtc)]]) {
        const dt = document.createElement('dt');
        dt.textContent = label;
        const dd = document.createElement('dd');
        dd.textContent = String(value);
        summary.append(dt, dd);
      }
      const breakdown = clear('statisticsBreakdown');
      for (const item of report.Breakdown) row(breakdown, [item.Kind === 'whiteboard' ? 'Whiteboard' : 'Code', item.Kind === 'whiteboard' ? '—' : item.Language, item.Count]);
      if (!report.Breakdown.length) row(breakdown, ['No rooms recorded yet.']);
      clear('summaryTitle').textContent = userId ? userName : 'All users';
      clear('detailsTitle').textContent = userId ? 'User activity' : 'Users';
      back.hidden = !userId;
      row(clear('statisticsHead'), userId ? ['Time (UTC)', 'Activity', 'Sign-in method', 'Room', 'Workspace', 'Language'] : ['User', 'Sign-ins', 'Rooms', 'Last activity (UTC)'], true);
      const details = clear('statisticsDetails');
      const items = userId ? report.Events : report.Users;
      for (const item of items) {
        if (userId) {
          row(details, [date(item.OccurredAtUtc), item.Type === 'login' ? 'Sign-in' : 'Room created', item.Provider, item.RoomName, item.Kind, item.Kind === 'whiteboard' ? '—' : item.Language]);
        } else {
          const tr = row(details, ['', item.Logins, item.Rooms, date(item.LastActivityUtc)]);
          const button = document.createElement('button');
          button.type = 'button';
          button.className = 'ghost';
          button.textContent = item.DisplayName;
          button.addEventListener('click', () => { userId = item.Id; userName = item.DisplayName; offset = 0; return load(); });
          tr.firstElementChild?.append(button);
        }
      }
      if (!items.length) row(details, ['No activity recorded.']);
      const total = userId ? report.EventsCount : report.UsersCount;
      clear('statisticsPage').textContent = total ? `${offset + 1}–${offset + items.length} of ${total}` : '0 results';
      previous.disabled = offset === 0;
      next.disabled = offset + items.length >= total;
      status.textContent = '';
      return true;
    } catch (error) {
      if (current === requestId) {
        for (const id of ['statisticsSummary', 'statisticsBreakdown', 'statisticsHead', 'statisticsDetails', 'statisticsPage']) clear(id);
        status.textContent = error instanceof Error ? error.message : 'Unable to load statistics.';
      }
      return false;
    }
  }

  previous.addEventListener('click', () => { offset = Math.max(0, offset - 50); return load(); });
  next.addEventListener('click', () => { offset += 50; return load(); });
  back.addEventListener('click', () => { userId = ''; offset = 0; return load(); });
  document.getElementById('refreshStatistics')?.addEventListener('click', () => { return load(); });
  document.getElementById('pruneStatistics')?.addEventListener('submit', async event => {
    event.preventDefault();
    if (!cutoff.reportValidity()) return;
    const button = /** @type {HTMLButtonElement} */ (document.querySelector('#pruneStatistics button'));
    if (button.disabled) return;
    if (!confirm(`Permanently delete all statistics before ${cutoff.value} 00:00 UTC? This cannot be undone.`)) return;
    button.disabled = true;
    try {
      const response = await fetch(`/admin/statistics/data?before=${encodeURIComponent(cutoff.value + 'T00:00:00Z')}`, { method: 'DELETE', headers: { 'X-CSRF-TOKEN': await token(fetch) } });
      if (!response.ok) throw new Error('Unable to delete statistics. Check your session and cutoff date.');
      const result = await response.json();
      offset = 0;
      if (await load()) status.textContent = `${result.DeletedEvents} old events deleted.`;
    } catch (error) {
      status.textContent = error instanceof Error ? error.message : 'Unable to delete statistics.';
    } finally {
      button.disabled = false;
    }
  });
  return { ready: load(), reload: load };
}
