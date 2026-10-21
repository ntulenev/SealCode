import { HubMethods, HubEvents } from './hub-contract.js';
import { HubErrorCode, classifyHubError } from './hub-errors.js';

/** Recognizes the legacy room deletion error inside a SignalR wrapper. */
export function isRoomNotFound(error) {
  return classifyHubError(error) === HubErrorCode.RoomNotFound;
}

/** Registers the event names from the shared hub contract in one place. */
export function registerHubHandlers(connection, handlers) {
  for (const [event, handler] of Object.entries(handlers)) {
    connection.on(HubEvents[event], handler);
  }
}

/** Owns joining, reconnect retries, connectivity and terminal room closure. */
export class RoomLifecycle {
  constructor({
    connection,
    roomId,
    session,
    getDisplayName,
    start,
    connectedState = 'Connected',
    onJoinStart,
    applyJoin,
    onJoinSuccess,
    onJoinError,
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
    this.connectedState = connectedState;
    this.onJoinStart = onJoinStart;
    this.applyJoin = applyJoin;
    this.onJoinSuccess = onJoinSuccess;
    this.onJoinError = onJoinError;
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
    this.rejoining = false;
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
      this.retryAttempt = 0;
      this.clearRejoinRetryTimer();
      this.onJoinSuccess();
      return true;
    } catch (error) {
      if (isRoomNotFound(error)) {
        this.closeRoom('Room not found.');
        return false;
      }
      this.onJoinError(error);
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
    this.rejoining = true;
    this.clearRejoinRetryTimer();
    this.setStatus('reconnecting');
    this.setReadOnly(true);
    let joined = false;
    try {
      joined = await this.joinRoom();
    } finally {
      this.rejoining = false;
    }
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

  closeRoom(reason) {
    this.closed = true;
    this.joined = false;
    this.session.synchronizationEpoch += 1;
    this.session.isSynchronizingYjs = false;
    this.session.bufferedYjsMessages.length = 0;
    this.clearRejoinRetryTimer();
    this.setReadOnly(true);
    this.onClose(reason);
  }

  pause(status) {
    this.resetTransport();
    this.clearRejoinRetryTimer();
    this.rejoining = false;
    this.setStatus(status);
    this.setReadOnly(true);
  }

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
      this.setStatus('reconnecting');
      this.setReadOnly(true);
    });
    events.addEventListener('online', () => {
      if (!this.joined || this.closed) return;
      if (this.connection.state === this.connectedState) {
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
