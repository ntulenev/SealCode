namespace Transport;

#pragma warning disable IDE1006 // Wire contract constants retain the SignalR spelling.
/// <summary>
/// Stable SignalR method and event names shared with browser clients.
/// </summary>
public static class HubContract
{
    /// <summary>
    /// Defines hub methods.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "The shared contract groups method and event wire names.")]
    public static class Methods
    {
        /// <summary>
        /// The JoinRoom wire name.
        /// </summary>
        public const string JoinRoom = "JoinRoom";

        /// <summary>
        /// The UpdateYjs wire name.
        /// </summary>
        public const string UpdateYjs = "UpdateYjs";

        /// <summary>
        /// The GetDocumentRevision wire name.
        /// </summary>
        public const string GetDocumentRevision = "GetDocumentRevision";

        /// <summary>
        /// The GetRoomRevisions wire name.
        /// </summary>
        public const string GetRoomRevisions = "GetRoomRevisions";

        /// <summary>
        /// The SyncYjs wire name.
        /// </summary>
        public const string SyncYjs = "SyncYjs";

        /// <summary>
        /// The CheckpointYjs wire name.
        /// </summary>
        public const string CheckpointYjs = "CheckpointYjs";

        /// <summary>
        /// The SetLanguage wire name.
        /// </summary>
        public const string SetLanguage = "SetLanguage";

        /// <summary>
        /// The UpdateCursor wire name.
        /// </summary>
        public const string UpdateCursor = "UpdateCursor";

        /// <summary>
        /// The UpdateSelection wire name.
        /// </summary>
        public const string UpdateSelection = "UpdateSelection";

        /// <summary>
        /// The UpdateCopy wire name.
        /// </summary>
        public const string UpdateCopy = "UpdateCopy";

        /// <summary>
        /// The LeaveRoom wire name.
        /// </summary>
        public const string LeaveRoom = "LeaveRoom";

    }
    /// <summary>
    /// Defines hub events.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "The shared contract groups method and event wire names.")]
    public static class Events
    {
        /// <summary>
        /// The UserJoined wire name.
        /// </summary>
        public const string UserJoined = "UserJoined";

        /// <summary>
        /// The YjsUpdated wire name.
        /// </summary>
        public const string YjsUpdated = "YjsUpdated";

        /// <summary>
        /// The YjsCheckpointed wire name.
        /// </summary>
        public const string YjsCheckpointed = "YjsCheckpointed";

        /// <summary>
        /// The LanguageUpdated wire name.
        /// </summary>
        public const string LanguageUpdated = "LanguageUpdated";

        /// <summary>
        /// The CursorUpdated wire name.
        /// </summary>
        public const string CursorUpdated = "CursorUpdated";

        /// <summary>
        /// The UserSelection wire name.
        /// </summary>
        public const string UserSelection = "UserSelection";

        /// <summary>
        /// The UserCopy wire name.
        /// </summary>
        public const string UserCopy = "UserCopy";

        /// <summary>
        /// The UserLeft wire name.
        /// </summary>
        public const string UserLeft = "UserLeft";

        /// <summary>
        /// The RoomKilled wire name.
        /// </summary>
        public const string RoomKilled = "RoomKilled";

    }
}

#pragma warning restore IDE1006
