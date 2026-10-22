/** Keeps active sync failures ordered by their importance to the participant. */
export class SyncErrors {
  constructor() {
    /** @type {Map<string, string>} */
    this.errors = new Map();
  }
  /** @param {string} kind
   * @param {string} message */
  show(kind, message) {
    this.errors.set(kind, message);
    return this.current();
  }
  /** @param {string} kind */
  clear(kind) {
    this.errors.delete(kind);
    return this.current();
  }
  current() {
    for (const kind of [
      'update-size',
      'checkpoint-size',
      'rejoin',
      'recovery',
      'transport'
    ]) {
      const message = this.errors.get(kind);
      if (message !== undefined) return { kind, message };
    }
    return null;
  }
}
