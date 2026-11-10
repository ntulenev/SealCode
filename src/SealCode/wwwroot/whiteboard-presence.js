/** Ephemeral cursor transport: bounded sends, heartbeat and stale-peer cleanup. */
export class WhiteboardPresence {
  /** @param {{board: ReturnType<typeof import('./whiteboard-view.js').createWhiteboardView>, send: (point: {x: number, y: number} | null) => Promise<unknown>, ready: () => boolean, document: Document}} options */
  constructor({ board, send, ready, document }) {
    this.board = board;
    this.send = send;
    this.ready = ready;
    this.document = document;
    /** @type {{x: number, y: number} | null} */
    this.point = null;
    /** @type {Map<string, number>} */
    this.peers = new Map();
    /** @type {Set<string>} */
    this.users = new Set();
    /** @type {ReturnType<typeof setTimeout> | null} */
    this.timer = null;
    /** @type {ReturnType<typeof setInterval> | null} */
    this.heartbeat = null;
    this.pending = false;
    this.inFlight = false;
    this.epoch = 0;
    this.lastSent = 0;
    board.onPointer = (point) => {
      point = document.hidden ? null : point;
      if (this.point?.x === point?.x && this.point?.y === point?.y) return;
      this.point = point;
      this.queue();
    };
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) this.hide();
    });
    document.defaultView?.addEventListener('blur', () => this.hide());
  }

  hide() {
    this.point = null;
    this.queue();
  }

  start() {
    if (this.heartbeat) return;
    this.heartbeat = setInterval(() => {
      const now = Date.now();
      for (const [name, seen] of this.peers) {
        if (now - seen > 15000) {
          this.peers.delete(name);
          this.board.removeRemoteCursor(name);
        }
      }
      if (this.point && now - this.lastSent >= 5000) this.queue();
    }, 1000);
    if (this.point) this.queue();
  }

  queue() {
    if (!this.ready()) return;
    this.pending = true;
    if (this.timer || this.inFlight) return;
    this.timer = setTimeout(() => {
      this.timer = null;
      void this.flush();
    }, Math.max(0, 60 - (Date.now() - this.lastSent)));
  }

  async flush() {
    if (!this.pending || !this.ready()) return;
    this.pending = false;
    this.inFlight = true;
    this.lastSent = Date.now();
    const epoch = this.epoch;
    try {
      await this.send(this.point);
    } catch {
      // Presence is disposable; never block document edits or replay old positions.
    } finally {
      if (epoch === this.epoch) {
        this.inFlight = false;
        if (this.pending) this.queue();
      }
    }
  }

  /** @param {string[]} users */
  retainUsers(users) {
    this.users = new Set(users);
    for (const name of this.peers.keys()) {
      if (!this.users.has(name)) {
        this.peers.delete(name);
        this.board.removeRemoteCursor(name);
      }
    }
  }

  /** @param {string} name @param {number | null} x @param {number | null} y */
  receive(name, x, y) {
    if (!this.ready() || !this.users.has(name)) return;
    if (x === null && y === null) {
      this.peers.delete(name);
      this.board.removeRemoteCursor(name);
    } else if (typeof x === 'number' && typeof y === 'number' && Number.isFinite(x) && Number.isFinite(y)) {
      this.peers.set(name, Date.now());
      this.board.setRemoteCursor(name, { x, y });
    }
  }

  reset() {
    if (this.timer) clearTimeout(this.timer);
    if (this.heartbeat) clearInterval(this.heartbeat);
    this.timer = this.heartbeat = null;
    this.pending = this.inFlight = false;
    this.point = null;
    this.epoch++;
    this.peers.clear();
    this.board.clearRemoteCursors();
  }
}
