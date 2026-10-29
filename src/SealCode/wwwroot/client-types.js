/**
 * @typedef {object} JoinRoomResult
 * @property {string} [Name]
 * @property {string} [name]
 * @property {string} [Language]
 * @property {string} [language]
 * @property {string} [Text]
 * @property {string} [text]
 * @property {number} [Version]
 * @property {number} [version]
 * @property {string[]} [Users]
 * @property {string[]} [users]
 * @property {string} [CreatedBy]
 * @property {string} [createdBy]
 * @property {string} [YjsState]
 * @property {string} [yjsState]
 * @property {Array<{Revision: number, Update: string}>} [YjsUpdates]
 * @property {Array<{revision: number, update: string}>} [yjsUpdates]
 * @property {number} [DocumentRevision]
 * @property {number} [documentRevision]
 * @property {number} [CheckpointRevision]
 * @property {number} [checkpointRevision]
 * @property {number} [MetadataRevision]
 * @property {number} [metadataRevision]
 * @property {number} [PresenceRevision]
 * @property {number} [presenceRevision]
 * @typedef {JoinRoomResult} SyncYjs
 * @typedef {object} ClientLimits
 * @property {number} maxEncodedUpdateBytes
 * @property {number} maxEncodedCheckpointBytes
 * @property {number} maxSnapshotCharacters
 * @property {number} checkpointEveryRevisions
 * @property {number} checkpointEveryBase64Bytes
 * @property {number} maxYjsMessageBytes
 */
/**
 * @typedef {object} TimerOptions
 * @property {(callback: () => unknown, delay: number) => ReturnType<typeof setTimeout>} [setTimer]
 * @property {(timer: ReturnType<typeof setTimeout>) => void} [clearTimer]
 */
/**
 * @typedef {TimerOptions & {
 * send: (update: Uint8Array) => unknown,
 * merge: (updates: Uint8Array[]) => Uint8Array,
 * ready: () => boolean, onIdle: () => void, onSuccess: () => void,
 * onError: (error: unknown, permanent: boolean) => void,
 * maxBytes?: number, maxCount?: number, maxEncodedBytes?: number
 * }} UpdateQueueOptions
 */
/**
 * @typedef {object} Revisions
 * @property {number} DocumentRevision
 * @property {number} MetadataRevision
 * @property {number} PresenceRevision
 */
/**
 * @typedef {object} SnapshotRecoveryOptions
 * @property {import('./room-sync.js').SyncScheduler} scheduler
 * @property {() => boolean} active
 * @property {() => boolean} ready
 * @property {() => unknown} capture
 * @property {(context: unknown) => boolean} current
 * @property {() => Revisions} revisions
 * @property {() => Promise<Revisions>} probe
 * @property {() => Promise<SyncYjs>} snapshot
 * @property {() => number} begin
 * @property {(result: SyncYjs) => void} apply
 * @property {(token: number) => void} end
 * @property {() => void} onSuccess
 * @property {(error: unknown) => void} onError
 */
/**
 * @typedef {object} CheckpointWriterOptions
 * @property {number} [delay]
 * @property {number} [retryAfterRevisions]
 * @property {import('./room-sync.js').SyncScheduler} scheduler
 * @property {() => boolean} ready
 * @property {() => unknown} capture
 * @property {(context: unknown) => boolean} current
 * @property {() => number} revision
 * @property {(revision: number) => unknown} payload
 * @property {(payload: unknown) => Promise<boolean>} send
 * @property {(revision: number) => void} onSuccess
 * @property {(error: unknown, permanent: boolean) => void} onError
 */
/**
 * @typedef {TimerOptions & {
 * connection: import('@microsoft/signalr').HubConnection, roomId: string,
 * session: {synchronizationEpoch: number, isSynchronizingYjs: boolean, bufferedYjsMessages: unknown[]},
 * getDisplayName: () => string, start: () => Promise<boolean>, stop?: () => Promise<void>,
 * connectedState?: string, onJoinStart: () => void,
 * applyJoin: (result: JoinRoomResult) => void, onJoinSuccess: () => void,
 * onJoinError: (error: unknown) => void, onRejoinError?: (error: unknown) => void,
 * onSynchronizationEnd: () => void, onClose: (reason: string) => void,
 * setStatus: (status: string) => void, setReadOnly: (value: boolean) => void,
 * resetTransport: () => void, rejoinRetryDelaysMs?: number[], random?: () => number
 * }} RoomLifecycleOptions
 */
/**
 * @typedef {object} PresenceViewOptions
 * @property {any} editor Monaco editor adapter.
 * @property {any} monaco Monaco namespace adapter.
 * @property {HTMLElement} participantsList
 * @property {Document} document
 * @property {() => string} [getDisplayName]
 */
/**
 * @typedef {object} YjsSessionOptions
 * @property {typeof import('yjs')} Y
 * @property {any} MonacoBinding Binding constructor adapter.
 * @property {any} Awareness Awareness constructor adapter.
 * @property {typeof import('./bounded-undo-manager.js').BoundedUndoManager} BoundedUndoManager
 * @property {typeof import('./room-sync.js').CheckpointJournalTracker} CheckpointJournalTracker
 * @property {any} [editor] Monaco editor adapter.
 * @property {() => string} getDisplayName
 * @property {() => boolean} undoEnabled
 * @property {Record<string, number>} cursorPositions
 * @property {(revision: number) => void} onRevision
 * @property {() => void} onRemoteChange
 * @property {(author: string) => void} onRemoteAuthor
 * @property {(update: Uint8Array) => void} onUpdate
 * @property {() => void} onCheckpoint
 * @property {(delay?: number) => void} onRecovery
 * @property {() => void} onDispose
 */
export {};
