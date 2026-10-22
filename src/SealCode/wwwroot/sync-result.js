/**
 * @typedef {object} SyncResult
 * @property {string} name
 * @property {string} language
 * @property {string} text
 * @property {number} version
 * @property {string[]} users
 * @property {string} createdBy
 * @property {string} yjsState
 * @property {Array<{Revision?: number, revision?: number, Update?: string, update?: string}>} yjsUpdates
 * @property {number} documentRevision
 * @property {number} checkpointRevision
 */
/**
 * Reads JoinRoomResult and SyncYjs responses in either JSON naming convention.
 * @param {Record<string, any>} result
 * @returns {SyncResult}
 */
export function readSyncResult(result) {
  return {
    name: result.Name ?? result.name,
    language: result.Language ?? result.language,
    text: result.Text ?? result.text ?? '',
    version: Number(result.Version ?? result.version) || 0,
    users: result.Users ?? result.users ?? [],
    createdBy: result.CreatedBy ?? result.createdBy,
    yjsState: result.YjsState ?? result.yjsState,
    yjsUpdates: result.YjsUpdates ?? result.yjsUpdates ?? [],
    documentRevision: Number(result.DocumentRevision ?? result.documentRevision) || 0,
    checkpointRevision:
      Number(result.CheckpointRevision ?? result.checkpointRevision) || 0
  };
}

/**
 * Applies a normalized join response while preserving a reconnecting document.
 * @param {Pick<import('./yjs-session.js').YjsSession, 'ydoc' | 'mergeYjsSyncState' | 'initializeYjs'>} session
 * @param {SyncResult} sync
 * @param {boolean} rejoining
 */
export function applyJoinDocument(session, sync, rejoining) {
  if (session.ydoc && rejoining) {
    if (
      !session.mergeYjsSyncState(
        sync.yjsState,
        sync.yjsUpdates,
        sync.documentRevision,
        sync.checkpointRevision
      )
    ) {
      throw new Error('Failed to merge the room document after reconnect.');
    }
  } else {
    session.initializeYjs(
      sync.text || '',
      sync.yjsState,
      sync.yjsUpdates,
      sync.documentRevision,
      sync.checkpointRevision
    );
  }
}
