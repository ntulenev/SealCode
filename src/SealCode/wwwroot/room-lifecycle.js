import { HubMethods, HubEvents } from './hub-contract.js';
import { HubErrorCode, classifyHubError } from './hub-errors.js';

/** Recognizes the legacy room deletion error inside a SignalR wrapper. */
/**
 * @param {unknown} error
 */
export function isRoomNotFound(error) {
  return classifyHubError(error) === HubErrorCode.RoomNotFound;
}

/** Registers the event names from the shared hub contract in one place. */
/**
 * @param {Pick<import('@microsoft/signalr').HubConnection, 'on'>} connection
 * @param {Partial<Record<keyof typeof HubEvents, (...args: any[]) => void>>} handlers
 */
export function registerHubHandlers(connection, handlers) {
  for (const [event, handler] of Object.entries(handlers)) {
    connection.on(HubEvents[/** @type {keyof typeof HubEvents} */ (event)], handler);
  }
}

/** Owns joining, reconnect retries, connectivity and terminal room closure. */
export class RoomLifecycle {
  /** @param {import('./client-types.js').RoomLifecycleOptions} options */
  constructor({
    connection,
    roomId,
    session,
    getDisplayName,
    start,
    stop = async () => {},
    connectedState = 'Connected',
    onJoinStart,
    applyJoin,
    onJoinSuccess,
    onJoinError,
    onRejoinError = onJoinError,
    onSynchronizationEnd,
    onClose,
    setStatus,
    setReadOnly,
    resetTransport,
    setTimer = (fn, delay) => setTimeout(fn, delay),
    clearTimer = (timer) => clearTimeout(timer),
    rejoinRetryDelaysMs = [500, 1000, 2000, 5000, 10000]
  }) {
    this.connection = connection;
    this.roomId = roomId;
    this.session = session;
    this.getDisplayName = getDisplayName;
    this.start = start;
    this.stop = stop;
    this.connectedState = connectedState;
    this.onJoinStart = onJoinStart;
    this.applyJoin = applyJoin;
    this.onJoinSuccess = onJoinSuccess;
    this.onJoinError = onJoinError;
    this.onRejoinError = onRejoinError;
    this.onSynchronizationEnd = onSynchronizationEnd;
    this.onClose = onClose;
    this.setStatus = setStatus;
    this.setReadOnly = setReadOnly;
    this.resetTransport = resetTransport;
    this.setTimer = setTimer;
    this.clearTimer = clearTimer;
    this.rejoinRetryDelaysMs = rejoinRetryDelaysMs;
    this.closed = false;
    this.joined = false;
    this.participating = false;
    this.rejoining = false;
    this.rejoinToken = 0;
    this.retryTimer = null;
    this.retryAttempt = 0;
  }

  clearRejoinRetryTimer() {
    if (!this.retryTimer) return;
    this.clearTimer(this.retryTimer);
    this.retryTimer = null;
  }

  async joinRoom() {
    if (this.closed) return false;
    this.participating = false;
    this.onJoinStart();
    this.session.isSynchronizingYjs = true;
    const epoch = ++this.session.synchronizationEpoch;
    try {
      const result = await this.connection.invoke(
        HubMethods.JoinRoom,
        this.roomId,
        this.getDisplayName()
      );
      if (this.closed || epoch !== this.session.synchronizationEpoch) return false;
      this.applyJoin(result);
      this.joined = true;
      this.participating = true;
      this.retryAttempt = 0;
      this.clearRejoinRetryTimer();
      this.onJoinSuccess();
      return true;
    } catch (error) {
      if (isRoomNotFound(error)) {
        this.closeRoom('Room not found.');
        return false;
      }
      if (this.joined) this.onRejoinError(error);
      else this.onJoinError(error);
      return false;
    } finally {
      if (epoch === this.session.synchronizationEpoch) {
        this.session.isSynchronizingYjs = false;
        this.onSynchronizationEnd();
      }
    }
  }

  async rejoinRoomWithRetry() {
    if (!this.joined || !this.getDisplayName() || this.closed) return;
    if (this.connection.state !== this.connectedState || this.rejoining) return;
    this.resetTransport();
    const token = ++this.rejoinToken;
    this.rejoining = true;
    this.clearRejoinRetryTimer();
    this.setStatus('reconnecting');
    this.setReadOnly(true);
    let joined = false;
    try {
      joined = await this.joinRoom();
    } finally {
      if (token === this.rejoinToken) this.rejoining = false;
    }
    if (token !== this.rejoinToken) return;
    if (joined) {
      this.retryAttempt = 0;
      this.setStatus('connected');
      this.setReadOnly(false);
      this.onSynchronizationEnd();
      return;
    }
    if (this.closed || this.connection.state !== this.connectedState) return;
    const index = Math.min(this.retryAttempt, this.rejoinRetryDelaysMs.length - 1);
    const delay = this.rejoinRetryDelaysMs[index];
    this.retryAttempt += 1;
    this.retryTimer = this.setTimer(() => {
      this.retryTimer = null;
      return this.rejoinRoomWithRetry().catch((error) =>
        console.warn('Failed to rejoin room.', error)
      );
    }, delay);
  }

  /**
   * @param {string} reason
   */
  closeRoom(reason) {
    if (this.closed) return;
    this.closed = true;
    this.joined = false;
    this.participating = false;
    this.session.synchronizationEpoch += 1;
    this.session.isSynchronizingYjs = false;
    this.session.bufferedYjsMessages.length = 0;
    this.clearRejoinRetryTimer();
    this.setReadOnly(true);
    this.onClose(reason);
    Promise.resolve()
      .then(() => this.stop())
      .catch((error) => console.warn('Failed to stop connection.', error));
  }

  /**
   * @param {string} status
   */
  pause(status) {
    this.participating = false;
    this.resetTransport();
    this.clearRejoinRetryTimer();
    this.rejoinToken++;
    this.rejoining = false;
    this.setStatus(status);
    this.setReadOnly(true);
  }

  /**
   * @param {Pick<Window, 'addEventListener'>} events
   */
  registerConnectivity(events) {
    this.connection.onreconnected(async () => {
      if (this.joined && this.getDisplayName() && !this.closed) {
        await this.rejoinRoomWithRetry();
        return;
      }
      this.setStatus('connected');
    });
    this.connection.onreconnecting(() => this.pause('reconnecting'));
    this.connection.onclose(() => this.pause('disconnected'));
    events.addEventListener('offline', () => {
      if (!this.joined || this.closed) return;
      this.participating = false;
      this.resetTransport();
      this.setStatus('reconnecting');
      this.setReadOnly(true);
    });
    events.addEventListener('online', () => {
      if (!this.joined || this.closed) return;
      if (this.connection.state === this.connectedState) {
        if (!this.participating) {
          this.rejoinRoomWithRetry().catch((error) =>
            console.warn('Failed to rejoin room.', error)
          );
          return;
        }
        this.setStatus('connected');
        this.setReadOnly(false);
        return;
      }
      this.setStatus('reconnecting');
      this.start()
        .then((started) => {
          if (started) return this.rejoinRoomWithRetry();
        })
        .catch((error) => console.warn('Failed to recover after going online.', error));
    });
  }
}
