/** Stable hub methods. */
export const HubMethods = Object.freeze({
  JoinRoom: 'JoinRoom',
  UpdateYjs: 'UpdateYjs',
  GetDocumentRevision: 'GetDocumentRevision',
  GetRoomRevisions: 'GetRoomRevisions',
  SyncYjs: 'SyncYjs',
  CheckpointYjs: 'CheckpointYjs',
  SetLanguage: 'SetLanguage',
  UpdateCursor: 'UpdateCursor',
  UpdateSelection: 'UpdateSelection',
  UpdateCopy: 'UpdateCopy',
  LeaveRoom: 'LeaveRoom'
});

/** Stable hub events. */
export const HubEvents = Object.freeze({
  UserJoined: 'UserJoined',
  YjsUpdated: 'YjsUpdated',
  YjsCheckpointed: 'YjsCheckpointed',
  LanguageUpdated: 'LanguageUpdated',
  CursorUpdated: 'CursorUpdated',
  UserSelection: 'UserSelection',
  UserCopy: 'UserCopy',
  UserLeft: 'UserLeft',
  RoomKilled: 'RoomKilled'
});
