/** Decides whether journal growth warrants a checkpoint from this participant. */
export function shouldWriteCheckpoint({ documentRevision, checkpointRevision, bytesSinceCheckpoint,
  isLatestAuthor, minRevisions = 64, minBytes = 256 * 1024, fallbackFactor = 2 }) {
  if (documentRevision <= checkpointRevision) return false;
  const factor = isLatestAuthor ? 1 : fallbackFactor;
  return documentRevision - checkpointRevision >= minRevisions * factor ||
    bytesSinceCheckpoint >= minBytes * factor;
}

/** Bounded transport queue. No DOM, Yjs singleton, or network globals. */
export class UpdateQueue {
  constructor({
    send,
    merge,
    ready,
    onIdle,
    onError,
    onSuccess,
    setTimer = (fn, delay) => setTimeout(fn, delay),
    clearTimer = (timer) => clearTimeout(timer),
    maxBytes = 8 * 1024 * 1024,
    maxCount = 2048
  }) {
    Object.assign(this, {
      send,
      merge,
      ready,
      onIdle,
      onError,
      onSuccess,
      setTimer,
      clearTimer,
      maxBytes,
      maxCount
    });
    this.pending = [];
    this.timer = null;
    this.inFlight = false;
    this.blocked = false;
    this.generation = 0;
    this.flight = [];
  }
  enqueue(update) {
    if (this.blocked) return;
    if (update?.length) {
      const retained = [...this.pending, ...this.flight];
      if (
        retained.length >= this.maxCount ||
        retained.reduce((sum, item) => sum + item.length, 0) + update.length >
          this.maxBytes
      ) {
        this.blocked = true;
        this.onError(new Error('LimitExceeded: Local pending queue'), true);
        return;
      }
      this.pending.push(update);
    }
    this.schedule(60);
  }
  schedule(delay) {
    if (this.blocked || this.timer || this.inFlight || !this.pending.length) return;
    this.timer = this.setTimer(() => {
      this.timer = null;
      return this.flush();
    }, delay);
  }
  async flush() {
    if (this.blocked || this.inFlight || !this.pending.length || !this.ready()) return;
    const generation = this.generation;
    const updates = this.pending.splice(0);
    this.flight = updates;
    this.inFlight = true;
    let retryDelay = 60;
    try {
      await this.send(updates.length === 1 ? updates[0] : this.merge(updates));
      if (generation !== this.generation) return;
      this.onSuccess();
    } catch (error) {
      if (generation !== this.generation) return;
      this.pending.unshift(...updates);
      this.blocked = /LimitExceeded|too large/.test(String(error));
      retryDelay = /RateLimited/.test(String(error)) ? 1100 : 500;
      this.onError(error, this.blocked);
    } finally {
      if (generation === this.generation) {
        this.flight = [];
        this.inFlight = false;
        if (this.pending.length) this.schedule(retryDelay);
        else this.onIdle();
      }
    }
  }
  reset() {
    this.generation++;
    if (this.timer) this.clearTimer(this.timer);
    this.pending = [];
    this.flight = [];
    this.timer = null;
    this.inFlight = false;
    this.blocked = false;
  }
}

/** A session-scoped recovery/checkpoint scheduler with an injectable clock. */
export class SyncScheduler {
  constructor({
    setTimer = (fn, delay) => setTimeout(fn, delay),
    clearTimer = (timer) => clearTimeout(timer),
    now = () => performance.now()
  } = {}) {
    Object.assign(this, { setTimer, clearTimer, now });
    this.jobs = new Map();
    this.generation = 0;
  }
  schedule(key, delay, callback) {
    const due = this.now() + delay;
    const existing = this.jobs.get(key);
    if (existing && existing.due <= due) return;
    if (existing) this.clearTimer(existing.timer);
    const generation = this.generation;
    const timer = this.setTimer(() => {
      this.jobs.delete(key);
      if (generation === this.generation) return callback();
    }, delay);
    this.jobs.set(key, { due, timer });
  }
  cancel(key) {
    const job = this.jobs.get(key);
    if (job) this.clearTimer(job.timer);
    this.jobs.delete(key);
  }
  reset() {
    this.generation++;
    for (const { timer } of this.jobs.values()) this.clearTimer(timer);
    this.jobs.clear();
  }
}

/** Probes all revisions and merges snapshots into the current document session. */
export class SnapshotRecovery {
  constructor({
    scheduler,
    active,
    ready,
    capture,
    current,
    revisions,
    probe,
    snapshot,
    begin,
    apply,
    end,
    onSuccess,
    onError
  }) {
    Object.assign(this, {
      scheduler,
      active,
      ready,
      capture,
      current,
      revisions,
      probe,
      snapshot,
      begin,
      apply,
      end,
      onSuccess,
      onError
    });
    this.inFlight = false;
  }
  schedule(delay = 5000) {
    if (this.active()) this.scheduler.schedule('recovery', delay, () => this.run());
  }
  async run() {
    if (!this.active()) return;
    if (this.inFlight || !this.ready()) {
      this.schedule();
      return;
    }
    const context = this.capture();
    let token;
    this.inFlight = true;
    try {
      const remote = await this.probe();
      if (!this.current(context) || !this.ready()) return;
      const local = this.revisions();
      if (
        remote.DocumentRevision <= local.DocumentRevision &&
        remote.MetadataRevision <= local.MetadataRevision &&
        remote.PresenceRevision <= local.PresenceRevision
      ) {
        this.onSuccess();
        return;
      }
      token = this.begin();
      const result = await this.snapshot();
      if (!this.current(context)) return;
      this.apply(result);
      this.onSuccess();
    } catch (error) {
      if (this.current(context)) this.onError(error);
    } finally {
      if (this.current(context)) {
        this.inFlight = false;
        if (token !== undefined) this.end(token);
        this.schedule();
      }
    }
  }
  reset() {
    this.inFlight = false;
    this.scheduler.cancel('recovery');
  }
}

/** Checkpoint state is scoped to one document session and revision. */
export class CheckpointWriter {
  constructor({
    delay = 400,
    scheduler,
    ready,
    capture,
    current,
    revision,
    payload,
    send,
    onSuccess,
    onError
  }) {
    Object.assign(this, {
      delay,
      scheduler,
      ready,
      capture,
      current,
      revision,
      payload,
      send,
      onSuccess,
      onError
    });
    this.inFlight = false;
    this.timer = false;
    this.skippedRevision = -1;
  }
  schedule() {
    if (
      !this.ready() ||
      this.inFlight ||
      this.timer ||
      this.revision() === this.skippedRevision
    )
      return;
    this.timer = true;
    this.scheduler.schedule('checkpoint', this.delay, () => this.run());
  }
  async run() {
    this.timer = false;
    if (!this.ready()) return;
    const context = this.capture();
    const revision = this.revision();
    let retry = false;
    this.inFlight = true;
    try {
      const accepted = await this.send(this.payload(revision));
      if (this.current(context) && accepted) this.onSuccess(revision);
    } catch (error) {
      if (!this.current(context)) return;
      const permanent = /LimitExceeded|too large/.test(String(error));
      if (permanent) this.skippedRevision = revision;
      retry = !permanent;
      this.onError(error, permanent);
    } finally {
      if (this.current(context)) {
        this.inFlight = false;
        if (retry || this.revision() > revision) this.schedule();
      }
    }
  }
  reset() {
    this.inFlight = false;
    this.timer = false;
    this.skippedRevision = -1;
    this.scheduler.cancel('checkpoint');
  }
}
