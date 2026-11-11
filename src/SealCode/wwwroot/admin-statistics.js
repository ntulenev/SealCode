import { token } from './admin-token.js';

/** @param {{document: Document, fetch: typeof fetch, confirm?: (message: string) => boolean}} options */
export function createAdminStatisticsPage({ document, fetch, confirm = message => window.confirm(message) }) {
  /** @param {string} id */
  const element = id => /** @type {HTMLElement} */ (document.getElementById(id));
  /** @param {string} id */
  const input = id => /** @type {HTMLInputElement} */ (document.getElementById(id));
  const status = element('statisticsStatus');
  const cutoff = input('statisticsCutoff');
  const previous = /** @type {HTMLButtonElement} */ (element('previousPage'));
  const next = /** @type {HTMLButtonElement} */ (element('nextPage'));
  const back = /** @type {HTMLButtonElement} */ (element('allUsers'));
  const from = input('statisticsFrom');
  const to = input('statisticsTo');
  const applyRange = /** @type {HTMLButtonElement} */ (element('applyStatisticsRange'));
  let userId = '';
  let userName = '';
  let offset = 0;
  let requestId = 0;
  let period = 'week';
  let sortBy = 'activity';
  let descending = true;
  let breakdownSortBy = 'count';
  let breakdownDescending = true;
  /** @type {Record<string, string>} */
  let filters = {};
  const today = new Date().toISOString().slice(0, 10);
  cutoff.max = from.max = to.max = today;
  setDates(7);
  let range = readRange();

  /** @param {number} days */
  function setDates(days) {
    to.value = today;
    from.value = new Date(Date.parse(today + 'T00:00:00Z') - (days - 1) * 86400000).toISOString().slice(0, 10);
  }

  function readRange() {
    const start = Date.parse(from.value + 'T00:00:00Z');
    const end = Date.parse(to.value + 'T00:00:00Z') + 86400000;
    if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start || end - start > 31 * 86400000 || to.value > today) {
      throw new Error('Choose a valid UTC period of at most 31 days, ending today or earlier.');
    }
    return { from: new Date(start).toISOString(), to: new Date(end).toISOString() };
  }

  /** @param {string} value */
  function selectPeriod(value) {
    period = value;
    for (const [id, name] of [['statisticsToday', 'today'], ['statisticsWeek', 'week'], ['statisticsCustom', 'custom']]) {
      element(id).setAttribute('aria-selected', String(period === name));
    }
    from.disabled = to.disabled = applyRange.disabled = period !== 'custom';
    if (period === 'custom') return;
    setDates(period === 'today' ? 1 : 7);
    range = readRange();
    offset = 0;
    return load();
  }

  /** @param {unknown} value */
  function date(value) {
    return value ? new Date(String(value)).toISOString().slice(0, 19).replace('T', ' ') + ' UTC' : '—';
  }

  /** @param {HTMLElement} parent @param {unknown[]} values */
  function row(parent, values) {
    const tr = document.createElement('tr');
    for (const value of values) {
      const cell = document.createElement('td');
      cell.textContent = String(value ?? '—');
      tr.append(cell);
    }
    parent.append(tr);
    return tr;
  }

  /** @param {string} id */
  function clear(id) {
    const target = element(id);
    target.replaceChildren();
    return target;
  }

  /** @param {string} id @param {string[][]} columns @param {boolean} [breakdown] */
  function headings(id, columns, breakdown = false) {
    const tr = document.createElement('tr');
    for (const [label, key] of columns) {
      const th = document.createElement('th');
      th.scope = 'col';
      const selected = key === (breakdown ? breakdownSortBy : sortBy);
      const reverse = breakdown ? breakdownDescending : descending;
      th.setAttribute('aria-sort', selected ? reverse ? 'descending' : 'ascending' : 'none');
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'statistics-sort';
      button.textContent = label + (selected ? reverse ? ' ↓' : ' ↑' : '');
      button.addEventListener('click', () => {
        if (breakdown) {
          breakdownDescending = selected ? !breakdownDescending : key === 'count';
          breakdownSortBy = key;
        } else {
          descending = selected ? !descending : !['user', 'room', 'type', 'kind', 'language', 'provider'].includes(key);
          sortBy = key;
        }
        offset = 0;
        return load();
      });
      th.append(button);
      tr.append(th);
    }
    clear(id).append(tr);
  }

  async function load() {
    const current = ++requestId;
    previous.disabled = next.disabled = true;
    status.textContent = 'Loading statistics…';
    try {
      const query = new URLSearchParams({ ...range, ...filters, offset: String(offset), sortBy, descending: String(descending), breakdownSortBy, breakdownDescending: String(breakdownDescending) });
      if (userId) query.set('userId', userId);
      const response = await fetch(`/admin/statistics/data?${query}`, { cache: 'no-store' });
      if (!response.ok) throw new Error(response.status === 401 ? 'Session expired. Sign in again.' : response.status === 403 ? 'Statistics are available to super admins only.' : 'Unable to load statistics. Check the period and filters, then try Refresh.');
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
      /** @param {number} count @param {string} name */
      const plural = (count, name) => count === 1 ? name : name + 's';
      clear('statisticsReportText').textContent = `${period === 'today' ? 'Today' : period === 'week' ? 'Last 7 days' : 'Selected period'}: ${report.Logins} successful ${plural(report.Logins, 'sign-in')} · ${report.Rooms} ${plural(report.Rooms, 'room')} created · ${report.UsersCount} ${plural(report.UsersCount, 'active account')}.`;
      headings('statisticsBreakdownHead', [['Workspace', 'kind'], ['Language', 'language'], ['Created', 'count']], true);
      const breakdown = clear('statisticsBreakdown');
      for (const item of report.Breakdown) row(breakdown, [item.Kind === 'whiteboard' ? 'Whiteboard' : 'Code', item.Kind === 'whiteboard' ? '—' : item.Language, item.Count]);
      if (!report.Breakdown.length) row(breakdown, ['No rooms recorded yet.']);
      clear('statisticsBreakdownNote').textContent = report.BreakdownCount > 100 ? 'Showing the first 100 groups. Use workspace or language filters to narrow the report.' : '';
      clear('summaryTitle').textContent = userId ? userName : 'All users';
      clear('detailsTitle').textContent = userId ? 'User activity' : 'Users';
      back.hidden = !userId;
      headings('statisticsHead', userId ? [['Time (UTC)', 'time'], ['Activity', 'type'], ['Sign-in method', 'provider'], ['Room', 'room'], ['Workspace', 'kind'], ['Language', 'language']] : [['User', 'user'], ['Sign-ins', 'logins'], ['Rooms', 'rooms'], ['Last activity (UTC)', 'activity']]);
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
          button.addEventListener('click', () => { userId = item.Id; userName = item.DisplayName; offset = 0; sortBy = 'time'; descending = true; return load(); });
          tr.firstElementChild?.append(button);
        }
      }
      if (!items.length) row(details, ['No activity recorded.']);
      const total = userId ? report.EventsCount : report.UsersCount;
      clear('statisticsPage').textContent = total ? `${offset + 1}–${Math.min(offset + items.length, total)} of ${total}` : '0 results';
      previous.disabled = offset === 0;
      next.disabled = offset + items.length >= total || offset + 50 > 10000;
      status.textContent = '';
      return true;
    } catch (error) {
      if (current === requestId) {
        for (const id of ['statisticsSummary', 'statisticsReportText', 'statisticsBreakdown', 'statisticsHead', 'statisticsDetails', 'statisticsPage']) clear(id);
        status.textContent = error instanceof Error ? error.message : 'Unable to load statistics.';
      }
      return false;
    }
  }

  previous.addEventListener('click', () => { offset = Math.max(0, offset - 50); return load(); });
  next.addEventListener('click', () => { offset += 50; return load(); });
  back.addEventListener('click', () => { userId = ''; offset = 0; sortBy = 'activity'; descending = true; return load(); });
  element('refreshStatistics').addEventListener('click', load);
  element('statisticsToday').addEventListener('click', () => selectPeriod('today'));
  element('statisticsWeek').addEventListener('click', () => selectPeriod('week'));
  element('statisticsCustom').addEventListener('click', () => selectPeriod('custom'));
  applyRange.addEventListener('click', () => {
    try { range = readRange(); } catch (error) { status.textContent = error.message; return; }
    offset = 0;
    return load();
  });
  const filterIds = { search: 'statisticsSearch', eventType: 'statisticsEventType', kind: 'statisticsKind', language: 'statisticsLanguage', provider: 'statisticsProvider' };
  element('statisticsFilters').addEventListener('submit', event => {
    event.preventDefault();
    filters = {};
    for (const [key, id] of Object.entries(filterIds)) {
      const value = input(id).value.trim();
      if (value) filters[key] = value;
    }
    offset = 0;
    return load();
  });
  element('resetStatisticsFilters').addEventListener('click', () => {
    for (const id of Object.values(filterIds)) input(id).value = '';
    filters = {};
    offset = 0;
    return load();
  });
  element('pruneStatistics').addEventListener('submit', async event => {
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
