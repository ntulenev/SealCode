import { HubErrorCode, classifyHubError, createHubError } from './hub-errors.js';

/** Decides whether journal growth warrants a checkpoint from this participant. */
/**
 * @param {{documentRevision: number, checkpointRevision: number, bytesSinceCheckpoint: number, isLatestAuthor: boolean, minRevisions?: number, minBytes?: number, fallbackFactor?: number}} options
 */
export function shouldWriteCheckpoint({
  documentRevision,
  checkpointRevision,
  bytesSinceCheckpoint,
  isLatestAuthor,
  minRevisions = 64,
  minBytes = 256 * 1024,
  fallbackFactor = 2
}) {
  if (documentRevision <= checkpointRevision) return false;
  const factor = isLatestAuthor ? 1 : fallbackFactor;
  return (
    documentRevision - checkpointRevision >= minRevisions * factor ||
    bytesSinceCheckpoint >= minBytes * factor
  );
}

/** Bounded transport queue. No DOM, Yjs singleton, or network globals. */
export class UpdateQueue {
  /** @param {import('./client-types.js').UpdateQueueOptions} options */
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
    maxEncodedBytes = 4 * 1024 * 1024,
    maxCount = 2048
  }) {
    this.send = send;
    this.merge = merge;
    this.ready = ready;
    this.onIdle = onIdle;
    this.onError = onError;
    this.onSuccess = onSuccess;
    this.setTimer = setTimer;
    this.clearTimer = clearTimer;
    this.maxBytes = maxBytes;
    this.maxEncodedBytes = maxEncodedBytes;
    this.singleRemaining = 0;
    this.maxCount = maxCount;
    /** @type {Uint8Array[]} */
    this.pending = [];
    this.timer = null;
    this.inFlight = false;
    this.blocked = false;
    this.generation = 0;
    /** @type {Uint8Array[]} */
    this.flight = [];
  }
  /**
   * @param {Uint8Array} update
   */
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
        this.onError(
          createHubError(HubErrorCode.LimitExceeded, 'Local pending queue'),
          true
        );
        return;
      }
      this.pending.push(update);
    }
    this.schedule(60);
  }
  /**
   * @param {number} delay
   */
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
    let count = 1;
    let estimate = 4 * Math.ceil(this.pending[0].length / 3);
    while (this.singleRemaining === 0 && count < this.pending.length) {
      const next = 4 * Math.ceil(this.pending[count].length / 3);
      if (estimate + next > this.maxEncodedBytes) break;
      estimate += next;
      count++;
    }
    const updates = this.pending.splice(0, count);
    this.flight = updates;
    this.inFlight = true;
    let retryDelay = 60;
    let merging = true;
    try {
      let payload = count === 1 ? updates[0] : this.merge(updates);
      while (count > 1 && 4 * Math.ceil(payload.length / 3) > this.maxEncodedBytes) {
        count = Math.max(1, Math.floor(count / 2));
        this.pending.unshift(...updates.splice(count));
        payload = count === 1 ? updates[0] : this.merge(updates);
      }
      merging = false;
      await this.send(payload);
      if (generation !== this.generation) return;
      if (this.singleRemaining > 0) this.singleRemaining--;
      this.onSuccess();
    } catch (error) {
      if (generation !== this.generation) return;
      this.pending.unshift(...updates);
      const limited = !merging && classifyHubError(error) === HubErrorCode.LimitExceeded;
      if ((merging || limited) && updates.length > 1) this.singleRemaining = updates.length;
      this.blocked = limited && updates.length === 1;
      retryDelay = classifyHubError(error) === HubErrorCode.RateLimited ? 1100 : 500;
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
    this.singleRemaining = 0;
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
  /** @param {import('./client-types.js').TimerOptions & {now?: () => number}} [options] */
  constructor({
    setTimer = (fn, delay) => setTimeout(fn, delay),
    clearTimer = (timer) => clearTimeout(timer),
    now = () => performance.now()
  } = {}) {
    this.setTimer = setTimer;
    this.clearTimer = clearTimer;
    this.now = now;
    this.jobs = new Map();
    this.generation = 0;
  }
  /** @param {string} key
   * @param {number} delay
   * @param {() => unknown} callback */
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
  /** @param {string} key */
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
  /** @param {import('./client-types.js').SnapshotRecoveryOptions} options */
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
    this.scheduler = scheduler;
    this.active = active;
    this.ready = ready;
    this.capture = capture;
    this.current = current;
    this.revisions = revisions;
    this.probe = probe;
    this.snapshot = snapshot;
    this.begin = begin;
    this.apply = apply;
    this.end = end;
    this.onSuccess = onSuccess;
    this.onError = onError;
    this.inFlight = false;
    this.generation = 0;
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
    const generation = this.generation;
    const context = this.capture();
    let token;
    this.inFlight = true;
    try {
      const remote = await this.probe();
      if (generation !== this.generation || !this.current(context) || !this.ready()) return;
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
      if (generation !== this.generation || !this.current(context)) return;
      this.apply(result);
      this.onSuccess();
    } catch (error) {
      if (generation === this.generation && this.current(context)) this.onError(error);
    } finally {
      if (generation === this.generation) {
        this.inFlight = false;
        if (token !== undefined) this.end(token);
        if (this.current(context)) this.schedule();
      }
    }
  }
  reset() {
    this.generation++;
    this.inFlight = false;
    this.scheduler.cancel('recovery');
  }
}

/** Checkpoint state is scoped to one document session and revision. */
export class CheckpointWriter {
  /** @param {import('./client-types.js').CheckpointWriterOptions} options */
  constructor({
    delay = 400,
    retryAfterRevisions = 1,
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
    this.delay = delay;
    this.retryAfterRevisions = retryAfterRevisions;
    this.scheduler = scheduler;
    this.ready = ready;
    this.capture = capture;
    this.current = current;
    this.revision = revision;
    this.payload = payload;
    this.send = send;
    this.onSuccess = onSuccess;
    this.onError = onError;
    this.inFlight = false;
    this.timer = false;
    this.skippedRevision = -1;
    this.generation = 0;
  }
  schedule() {
    if (
      !this.ready() ||
      this.inFlight ||
      this.timer ||
      (this.skippedRevision >= 0 &&
        this.revision() < this.skippedRevision + this.retryAfterRevisions)
    )
      return;
    this.timer = true;
    this.scheduler.schedule('checkpoint', this.delay, () => this.run());
  }
  async run() {
    this.timer = false;
    if (!this.ready()) return;
    const generation = this.generation;
    const context = this.capture();
    const revision = this.revision();
    let retry = false;
    this.inFlight = true;
    try {
      const accepted = await this.send(this.payload(revision));
      if (generation === this.generation && this.current(context) && accepted) this.onSuccess(revision);
    } catch (error) {
      if (generation !== this.generation || !this.current(context)) return;
      const permanent = classifyHubError(error) === HubErrorCode.LimitExceeded;
      if (permanent) this.skippedRevision = revision;
      retry = !permanent;
      this.onError(error, permanent);
    } finally {
      if (generation === this.generation) {
        this.inFlight = false;
        if (this.current(context) && (retry || this.revision() > revision)) this.schedule();
      }
    }
  }
  reset() {
    this.generation++;
    this.inFlight = false;
    this.timer = false;
    this.skippedRevision = -1;
    this.scheduler.cancel('checkpoint');
  }
}

/** Tracks checkpoint journal bytes and the author of the latest seen revision. */
export class CheckpointJournalTracker {
  constructor() {
    this.reset();
  }
  get checkpointRevision() {
    return this.knownRevision;
  }
  get isLatestAuthor() {
    return this.latestAuthor;
  }
  get bytesSinceCheckpoint() {
    return this.totalBytes;
  }
  /**
   * @param {number} revision
   */
  noteCheckpoint(revision) {
    if (!Number.isSafeInteger(revision) || revision <= this.knownRevision) return;
    this.knownRevision = revision;
    for (const [key, bytes] of this.updateBytes) {
      if (key <= revision) {
        this.totalBytes -= bytes;
        this.updateBytes.delete(key);
      }
    }
  }
  /**
   * @param {number} revision
   * @param {number} base64Length
   * @param {boolean} authoredLocally
   * @param {number} highestSeenBefore
   */
  noteUpdate(revision, base64Length, authoredLocally, highestSeenBefore) {
    if (!Number.isSafeInteger(revision) || revision <= 0) return;
    if (revision > highestSeenBefore) this.latestAuthor = authoredLocally;
    if (revision > this.knownRevision) {
      this.totalBytes += base64Length - (this.updateBytes.get(revision) ?? 0);
      this.updateBytes.set(revision, base64Length);
    }
  }
  /**
   * @param {number} checkpointRevision
   * @param {Array<{revision: number, update: string}>} updates
   */
  resetFromSync(checkpointRevision, updates) {
    this.noteCheckpoint(checkpointRevision);
    this.updateBytes.clear();
    this.totalBytes = 0;
    this.latestAuthor = false;
    for (const item of updates) {
      this.noteUpdate(item.revision, item.update.length, false, Infinity);
    }
  }
  reset() {
    this.knownRevision = 0;
    this.latestAuthor = false;
    this.totalBytes = 0;
    this.updateBytes = new Map();
  }
}

/**
 * @typedef {object} SyncState
 * @property {boolean} closed
 * @property {boolean} joined
 * @property {boolean} participating
 * @property {boolean} hasDocument
 * @property {boolean} synchronizing
 * @property {boolean} connected
 * @property {boolean} [hasText]
 * @property {number} [revision]
 * @property {number} [highestRevision]
 * @property {boolean} [pending]
 * @property {boolean} [timer]
 * @property {boolean} [inFlight]
 * @property {boolean} [blocked]
 * @property {boolean} [checkpointDue]
 */
/** @param {SyncState} state */
export function canSendUpdates(state) {
  return (
    !state.closed &&
    state.participating &&
    state.hasDocument &&
    !state.synchronizing &&
    state.connected
  );
}
/** @param {SyncState} state */
export function canRecover(state) {
  return !state.closed && state.joined && state.participating && state.hasDocument;
}
/** @param {SyncState} state */
export function canWriteCheckpoint(state) {
  return (
    canSendUpdates(state) &&
    state.joined &&
    state.hasText &&
    state.revision > 0 &&
    state.revision === state.highestRevision &&
    !state.pending &&
    !state.timer &&
    !state.inFlight &&
    !state.blocked &&
    state.checkpointDue
  );
}
