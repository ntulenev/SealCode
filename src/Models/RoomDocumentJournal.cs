using Models.Configuration;
using Models.Exceptions;

namespace Models;

/// <summary>
/// Stores document state under the owning room's lock.
/// </summary>
internal sealed class RoomDocumentJournal(RoomLimits limits, byte[]? state)
{
    internal RoomDocumentRevision DocumentRevision { get; private set; }
    internal RoomDocumentRevision YjsCheckpointRevision { get; private set; }
    internal byte[] State => [.. _state];
    internal long Bytes { get; private set; }
    internal int Count => _updates.Count;

    internal RoomDocumentRevision Append(byte[] update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.Length == 0)
        {
            throw new ArgumentException("Document update cannot be empty.", nameof(update));
        }
        if (Base64Size.EncodedLength(update.Length) > _limits.MaxEncodedUpdateBytes
            || Bytes + update.Length > _limits.MaxJournalBytes
            || Count >= _limits.MaxJournalCount)
        {
            RoomMetrics.RejectedUpdates.Add(1);
            throw new RoomLimitException();
        }
        DocumentRevision = DocumentRevision.Next();
        _updates.Add(new RoomDocumentUpdate(DocumentRevision, update));
        Bytes += update.Length;
        RoomMetrics.JournalSize.Add(update.Length);
        RoomMetrics.JournalEntries.Add(1);
        return DocumentRevision;
    }

    internal bool TryCheckpoint(RoomDocumentRevision revision, byte[] state, RoomText text)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Length == 0)
        {
            throw new ArgumentException("Document checkpoint cannot be empty.", nameof(state));
        }
        if (revision != DocumentRevision)
        {
            return false;
        }
        if (Base64Size.EncodedLength(state.Length) > _limits.MaxEncodedCheckpointBytes
            || text.Value.Length > _limits.MaxSnapshotCharacters)
        {
            throw new RoomLimitException();
        }
        _state = [.. state];
        YjsCheckpointRevision = revision;
        Clear();
        return true;
    }

    internal RoomDocumentSyncState CreateSnapshot()
        => new(DocumentRevision, YjsCheckpointRevision, State, [.. _updates]);

    internal void Clear()
    {
        RoomMetrics.JournalSize.Add(-Bytes);
        RoomMetrics.JournalEntries.Add(-Count);
        Bytes = 0;
        _updates.Clear();
    }

    private byte[] _state = state is null ? [] : [.. state];
    private readonly List<RoomDocumentUpdate> _updates = [];
    private readonly RoomLimits _limits = limits;
}
