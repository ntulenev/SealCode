/** @param {{document: Document, fetch: typeof fetch}} options */
export function createAdminLimitsPage({ document, fetch }) {
  const status = document.getElementById('limitsStatus');
  const content = document.getElementById('limitsContent');
  const retry = /** @type {HTMLButtonElement} */ (document.getElementById('retryLimits'));
  const number = new Intl.NumberFormat('en-US');
  /** @param {number} value */
  const count = (value) => number.format(value);
  /** @param {number} value */
  const bytes = (value) => `${number.format(value / 1024)} KiB`;

  /** @param {string} url */
  async function read(url) {
    const response = await fetch(url);
    if (response.status === 401 || response.redirected) {
      throw new Error('Session expired. Sign in again.');
    }
    if (!response.ok) throw new Error('Could not load the configured limits. Try again.');
    return response.json();
  }

  /** @param {string} id @param {Array<[string, string, string]>} rows */
  function render(id, rows) {
    const list = document.getElementById(id);
    list.replaceChildren();
    for (const [label, value, description] of rows) {
      const row = document.createElement('div');
      row.className = 'limit-row';
      const term = document.createElement('dt');
      term.textContent = label;
      const detail = document.createElement('small');
      detail.textContent = description;
      term.append(detail);
      const definition = document.createElement('dd');
      definition.textContent = value;
      row.append(term, definition);
      list.append(row);
    }
  }

  async function load() {
    retry.disabled = true;
    retry.hidden = true;
    content.hidden = true;
    status.textContent = 'Loading configured limits…';
    try {
      const [config, user] = await Promise.all([read('/admin/limits/config'), read('/admin/me')]);
      const limits = config.Limits;
      document.getElementById('adminGreeting').textContent = `Hello, ${user.DisplayName}!`;
      render('capacityLimits', [
        ['Rooms', count(limits.MaxRooms), 'Total rooms in this application, across all administrators. Empty rooms also count.'],
        ['Editors per room', count(config.MaxUsersPerRoom), 'Maximum editors connected to the same room. Read-only guests use their own limit.'],
        ['Editor connections', count(limits.MaxConnections), 'Editor connections across the application, including connections that have not joined a room.'],
        ['Connections per source', count(limits.MaxConnectionsPerSource), 'Shared by clients from the same source address. People on the same office network may share this limit.']
      ]);
      const observers = config.Observers;
      render('observerLimits', [
        ['Guest access', observers.Enabled ? 'Enabled' : 'Disabled', 'Global availability. A room owner must also create a guest link.'],
        ['Observers per room', count(observers.MaxPerRoom), 'Guests watching the same room. They cannot edit or create checkpoints.'],
        ['Observer connections', count(observers.MaxConnections), 'Separate application-wide guest pool, including connections not yet joined.'],
        ['Observers per source', count(observers.MaxConnectionsPerSource), 'Shared by guests on the same source address, such as one office network.'],
        ['Combined connection budget', count(limits.MaxConnections + (observers.Enabled ? observers.MaxConnections : 0)), 'Editor pool plus the guest pool when guest access is enabled. The hosting plan must support this total.']
      ]);
      render('observerTechnicalLimits', [
        ['Time to open a guest link', `${count(observers.UnjoinedTimeoutSeconds)} seconds`, 'An unjoined guest transport is closed after this deadline.'],
        ['New guest connections per source', `${count(observers.NewConnectionsPerSourcePerMinute)} / minute`, 'Independent guest admission rate limit.'],
        ['Guest requests per connection', `${count(observers.InvocationsPerSecond)} / second`, 'Guest commands are limited separately from editor requests.'],
        ['Full synchronizations per guest', `${count(observers.SnapshotRequestsPerConnectionPerMinute)} / minute`, 'Limits repeated initial-state and recovery requests.'],
        ['Concurrent guest snapshots', count(observers.ConcurrentSnapshots), 'Shared permits for creating and sending full guest snapshots.'],
        ['Queued guest data', bytes(observers.DeliveryQueueBytesPerConnection), 'Maximum queued live data per guest connection.'],
        ['Queued guest events', count(observers.DeliveryQueueEventsPerConnection), 'Maximum queued live events per guest connection.'],
        ['Guest send timeout', `${count(observers.SendTimeoutSeconds)} seconds`, 'Slow guest connections are disconnected without blocking editor delivery.']
      ]);
      render('lifecycleLimits', [
        ['Delete empty rooms after', limits.RoomIdleTimeoutMinutes === 0 ? 'Disabled' : `${count(limits.RoomIdleTimeoutMinutes)} minutes`, 'Automatic cleanup after a room is empty and inactive. Occupied rooms are kept.'],
        ['Time to join a room', `${count(limits.UnjoinedConnectionTimeoutSeconds)} seconds`, 'An open connection must join a room within this time.']
      ]);
      render('technicalLimits', [
        ['Text snapshot length', `${count(limits.MaxSnapshotCharacters)} characters`, 'Measured in UTF-16 code units. This is a snapshot limit, not a guaranteed document size.'],
        ['Document update size', bytes(limits.MaxEncodedUpdateBytes), 'Maximum Base64-encoded update size.'],
        ['Checkpoint size', bytes(limits.MaxEncodedCheckpointBytes), 'Maximum Base64-encoded checkpoint size.'],
        ['Retained change data', bytes(limits.MaxJournalBytes), 'Maximum decoded change history retained per room.'],
        ['Retained changes', count(limits.MaxJournalCount), 'Maximum updates retained per room before a checkpoint is needed.'],
        ['Checkpoint interval', `${count(limits.CheckpointEveryRevisions)} revisions`, 'The client attempts a checkpoint after this many document revisions.'],
        ['Checkpoint data threshold', bytes(limits.CheckpointEveryBase64Bytes), 'The client also attempts a checkpoint when encoded changes reach this size.'],
        ['Requests per connection', `${count(limits.InvocationsPerSecond)} / second`, 'Maximum real-time requests from one connection.'],
        ['New connections per source', `${count(limits.NewConnectionsPerSourcePerMinute)} / minute`, 'Rate limit shared by a source-address bucket.']
      ]);
      status.textContent = '';
      content.hidden = false;
    } catch (error) {
      status.textContent = error instanceof Error && error.message === 'Session expired. Sign in again.'
        ? error.message : 'Could not load the configured limits. Try again.';
      retry.hidden = false;
    } finally {
      retry.disabled = false;
    }
  }

  retry.addEventListener('click', load);
  return { ready: load() };
}
