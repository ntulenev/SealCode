/** Installs browser test probes only in an automated browser.
 * @param {{window: Window & {__debugYjsSend?: () => boolean,
 * __debugUndoState?: () => object, __debugReconnect?: () => Promise<boolean>},
 * yjs: import('./yjs-session.js').YjsSession, Y: typeof import('yjs'),
 * scheduleYjsUpdateSend: (update: Uint8Array) => void,
 * lifecycle: import('./room-lifecycle.js').RoomLifecycle,
 * connection: import('@microsoft/signalr').HubConnection,
 * connectionLifecycle: {start: () => Promise<boolean>}, connectedState: string,
 * connectionStatusEl: HTMLElement}} options
 */
export function installDebugHooks({ window, yjs, Y, scheduleYjsUpdateSend, lifecycle,
  connection, connectionLifecycle, connectedState, connectionStatusEl }) {
  if (window.navigator?.webdriver !== true) return;
  window.__debugYjsSend = () => {
    if (!yjs.ydoc) return false;
    scheduleYjsUpdateSend(Y.encodeStateAsUpdate(yjs.ydoc));
    return true;
  };

  window.__debugUndoState = () => ({
    canUndo: yjs.undoHistory?.canUndo() ?? false,
    canRedo: yjs.undoHistory?.canRedo() ?? false,
    undoDepth: yjs.undoHistory?.undoDepth ?? 0,
    redoDepth: yjs.undoHistory?.redoDepth ?? 0,
    documentRevision: yjs.currentDocumentRevision,
    highestDocumentRevisionSeen: yjs.highestDocumentRevisionSeen
  });

  window.__debugReconnect = async () => {
    if (!lifecycle.joined || lifecycle.closed) return false;
    await connection.stop();
    const started = await connectionLifecycle.start();
    if (!started) return false;
    await lifecycle.rejoinRoomWithRetry();
    return (
      connection.state === connectedState &&
      connectionStatusEl.textContent === 'connected'
    );
  };

}
