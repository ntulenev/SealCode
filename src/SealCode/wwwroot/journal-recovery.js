import { readSyncResult } from './sync-result.js';
import { HubMethods } from './hub-contract.js';
import { HubErrorCode, createHubError } from './hub-errors.js';
import { isRoomNotFound } from './room-lifecycle.js';
import { uint8FromBase64, base64FromUint8 } from './encoding.js';
import { checkCheckpointSize } from './client-limits.js';
import { fitsYjsMessage } from './yjs-message-size.js';

/** Detects permanent compaction refusals.
 * @param {unknown} error */
export function classifyJournalRecoveryError(error) {
  return /LimitExceeded|too large/.test(String(
    error && typeof error === 'object' && 'message' in error ? error.message : undefined));
}

/** Creates a single-flight journal compaction loop.
 * @param {{Y: typeof import('yjs'), yjs: import('./yjs-session.js').YjsSession,
 * updateSender: import('./room-sync.js').UpdateQueue,
 * lifecycle: import('./room-lifecycle.js').RoomLifecycle,
 * connection: import('@microsoft/signalr').HubConnection,
 * signalR: Pick<typeof import('@microsoft/signalr'), 'HubConnectionState'>,
 * roomId: string, isBoard: boolean,
 * getLimits: () => {maxEncodedCheckpointBytes: number, maxSnapshotCharacters: number,
 * maxYjsMessageBytes: number}, clearSyncError: (kind: string) => void,
 * showSyncError: (kind: string, message: string) => void,
 * setEditorReadOnly: (value: boolean) => void,
 * setTimer?: (callback: () => void, delay: number) => unknown}} options
 */
export function createJournalRecovery({ Y, yjs, updateSender, lifecycle, connection,
  signalR, roomId, isBoard, getLimits, clearSyncError, showSyncError,
  setEditorReadOnly, setTimer = setTimeout }) {
  let recoveringJournal = false;
  return async function recoverJournal() {
    if (recoveringJournal || !updateSender.pausedForJournal) return;
    recoveringJournal = true;
    const session = yjs.yjsSession;
    try {
      while (updateSender.pausedForJournal && !lifecycle.closed && session === yjs.yjsSession) {
        if (!lifecycle.participating || connection.state !== signalR.HubConnectionState.Connected)
          return;
        try {
          const sync = readSyncResult(await connection.invoke(HubMethods.SyncYjs, roomId));
          if (session !== yjs.yjsSession || !lifecycle.participating) return;
          const compact = new Y.Doc();
          /** @type {[string, string, string, number]} */
          let args;
          try {
            if (sync.yjsState) Y.applyUpdate(compact, uint8FromBase64(sync.yjsState));
            for (const item of sync.yjsUpdates)
              Y.applyUpdate(compact, uint8FromBase64(item.Update ?? item.update));
            args = [roomId, base64FromUint8(Y.encodeStateAsUpdate(compact)),
              compact.getText('monaco').toString(), sync.documentRevision];
          } finally {
            compact.destroy();
          }
          checkCheckpointSize(
            { stateBase64Length: args[1].length, textLength: args[2].length },
            getLimits()
          );
          if (!fitsYjsMessage(HubMethods.CheckpointYjs, args, getLimits().maxYjsMessageBytes))
            throw createHubError(HubErrorCode.LimitExceeded, 'CheckpointTooLarge');
          const accepted = await connection.invoke(HubMethods.CheckpointYjs, ...args);
          if (session !== yjs.yjsSession || !lifecycle.participating) return;
          if (accepted) {
            yjs.checkpointJournal.noteCheckpoint(sync.documentRevision);
            updateSender.resumeAfterCheckpoint();
            clearSyncError('transport');
            setEditorReadOnly(false);
            return;
          }
        } catch (error) {
          if (classifyJournalRecoveryError(error)) {
            showSyncError('checkpoint-size',
              isBoard ? 'The board cannot be compacted. Download the local board before reloading.' : 'The room state cannot be compacted. Copy your text before reloading.');
            return;
          }
          if (isRoomNotFound(error)) return lifecycle.closeRoom('Room not found.');
        }
        await new Promise((resolve) => setTimer(() => resolve(undefined), 1100));
      }
    } finally {
      recoveringJournal = false;
    }
  };
}
