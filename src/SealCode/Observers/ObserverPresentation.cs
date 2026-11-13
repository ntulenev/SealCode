using Models;

namespace SealCode.Observers;

/// <summary>Shared presentation state guarded by the observer gate.</summary>
internal sealed class ObserverPresentation
{
    /// <summary>Creates presentation state for an authoritative room.</summary>
    /// <param name="state">The authoritative room.</param>
    public ObserverPresentation(RoomState state)
    {
        State = state;
    }

    /// <summary>Gets the authoritative room aggregate.</summary>
    public RoomState State { get; }
    /// <summary>Gets or sets the admission capability digest.</summary>
    public string? TokenHash { get; set; }
    /// <summary>Gets or sets the encrypted owner copy of the capability.</summary>
    public string? ProtectedToken { get; set; }
    /// <summary>Gets or sets the current capability generation.</summary>
    public long Generation { get; set; }
    /// <summary>Gets or sets the independent guest count.</summary>
    public int Count { get; set; }
    /// <summary>Gets or sets the guest membership revision.</summary>
    public long CountRevision { get; set; }
    /// <summary>Gets or sets whether the counter needs publication.</summary>
    public bool CountDirty { get; set; }
    /// <summary>Gets or sets the last published document revision.</summary>
    public long LastDocument { get; set; }
    /// <summary>Gets or sets the last published metadata revision.</summary>
    public long LastMetadata { get; set; }
    /// <summary>Gets or sets the last published presence revision.</summary>
    public long LastPresence { get; set; }
    /// <summary>Gets or sets the immutable cached snapshot.</summary>
    public ObserverPayload? SnapshotPayload { get; set; }
    /// <summary>Gets or sets the cached document revision.</summary>
    public long SnapshotDocument { get; set; }
    /// <summary>Gets or sets the cached metadata revision.</summary>
    public long SnapshotMetadata { get; set; }
    /// <summary>Gets or sets the cached presence revision.</summary>
    public long SnapshotPresence { get; set; }
    /// <summary>Gets committed updates awaiting publication.</summary>
    public SortedDictionary<long, string> PendingUpdates { get; } = [];
    /// <summary>Gets or sets the retained publication size.</summary>
    public int PendingBytes { get; set; }
    /// <summary>Gets or sets whether publication requires resynchronization.</summary>
    public bool PendingOverflow { get; set; }
}
