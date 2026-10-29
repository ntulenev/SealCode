using Models.Configuration;
using Models.Exceptions;

namespace Models;

/// <summary>
/// Stores document state under the owning room's lock.
/// </summary>
internal sealed class RoomDocumentJournal
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomDocumentJournal"/> class.
    /// </summary>
    /// <param name="limits">The room document resource budgets.</param>
    /// <param name="state">The serialized checkpoint, or null for an empty initial state.</param>
    public RoomDocumentJournal(RoomLimits limits, byte[]? state)
    {
        _state = state is null ? [] : [.. state];
        _limits = limits;
    }

    /// <summary>
    /// Gets the latest accepted document revision.
    /// </summary>
    internal RoomDocumentRevision DocumentRevision { get; private set; }

    /// <summary>
    /// Gets the document revision represented by the retained checkpoint.
    /// </summary>
    internal RoomDocumentRevision YjsCheckpointRevision { get; private set; }

    /// <summary>
    /// Gets a copy of the retained serialized checkpoint.
    /// </summary>
    internal byte[] State => [.. _state];

    /// <summary>
    /// Gets the total decoded size of retained incremental updates.
    /// </summary>
    internal long Bytes { get; private set; }

    /// <summary>
    /// Gets the number of retained incremental updates.
    /// </summary>
    internal int Count => _updates.Count;

    /// <summary>
    /// Appends an incremental update within the configured resource budgets.
    /// </summary>
    /// <param name="update">The serialized incremental document update.</param>
    /// <returns>The document revision assigned to the accepted update.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the update is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the update is empty.</exception>
    /// <exception cref="RoomLimitException">Thrown when the update would exceed a configured resource budget.</exception>
    internal RoomDocumentRevision Append(byte[] update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.Length == 0)
        {
            throw new ArgumentException("Document update cannot be empty.", nameof(update));
        }
        if (Base64Size.EncodedLength(update.Length) > _limits.MaxEncodedUpdateBytes
            || update.Length > _limits.MaxJournalBytes)
        {
            RoomMetrics.RejectedUpdates.Add(1);
            throw new RoomLimitException("UpdateTooLarge");
        }
        if (Bytes + update.Length > _limits.MaxJournalBytes || Count >= _limits.MaxJournalCount)
        {
            RoomMetrics.RejectedUpdates.Add(1);
            throw new RoomLimitException("JournalFull");
        }
        DocumentRevision = DocumentRevision.Next();
        _updates.Add(new RoomDocumentUpdate(DocumentRevision, update));
        Bytes += update.Length;
        RoomMetrics.JournalSize.Add(update.Length);
        RoomMetrics.JournalEntries.Add(1);
        return DocumentRevision;
    }

    /// <summary>
    /// Replaces the checkpoint at the current revision and releases the retained journal.
    /// </summary>
    /// <param name="revision">The document revision represented by the checkpoint.</param>
    /// <param name="state">The serialized document checkpoint.</param>
    /// <param name="text">The plain text corresponding to the checkpoint.</param>
    /// <returns>True when the checkpoint was accepted; false when its revision is stale.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the state is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the state is empty.</exception>
    /// <exception cref="RoomLimitException">Thrown when the current checkpoint exceeds its payload or text budget.</exception>
    internal bool TryCheckpoint(RoomDocumentRevision revision, byte[] state, RoomText text)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Length == 0)
        {
            throw new ArgumentException("Document checkpoint cannot be empty.", nameof(state));
        }
        if (revision != DocumentRevision)
        {
            RoomMetrics.CheckpointStale.Add(1);
            return false;
        }
        if (Base64Size.EncodedLength(state.Length) > _limits.MaxEncodedCheckpointBytes
            || text.Value.Length > _limits.MaxSnapshotCharacters)
        {
            RoomMetrics.CheckpointRejected.Add(1);
            throw new RoomLimitException("CheckpointTooLarge");
        }
        _state = [.. state];
        YjsCheckpointRevision = revision;
        Clear();
        RoomMetrics.CheckpointAccepted.Add(1);
        return true;
    }

    /// <summary>
    /// Creates an independent synchronization snapshot of the checkpoint and retained updates.
    /// </summary>
    /// <returns>A snapshot containing copied checkpoint and update payloads.</returns>
    internal RoomDocumentSyncState CreateSnapshot()
        => new(DocumentRevision, YjsCheckpointRevision, State, [.. _updates]);

    /// <summary>
    /// Clears retained incremental updates and releases their metric balances.
    /// </summary>
    internal void Clear()
    {
        RoomMetrics.JournalSize.Add(-Bytes);
        RoomMetrics.JournalEntries.Add(-Count);
        Bytes = 0;
        _updates.Clear();
    }

    private byte[] _state;
    private readonly List<RoomDocumentUpdate> _updates = [];
    private readonly RoomLimits _limits;
}
