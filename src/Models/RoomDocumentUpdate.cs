namespace Models;

/// <summary>
/// Represents one opaque incremental CRDT update assigned to a document revision.
/// </summary>
public sealed class RoomDocumentUpdate
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomDocumentUpdate"/> class.
    /// </summary>
    /// <param name="revision">The assigned document revision.</param>
    /// <param name="payload">The encoded CRDT update.</param>
    /// <exception cref="ArgumentException">Thrown when the payload is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the payload is null.</exception>
    public RoomDocumentUpdate(RoomDocumentRevision revision, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0)
        {
            throw new ArgumentException("Document update cannot be empty.", nameof(payload));
        }

        Revision = revision;
        _payload = [.. payload];
    }

    /// <summary>
    /// Gets the assigned document revision.
    /// </summary>
    public RoomDocumentRevision Revision { get; }

    /// <summary>
    /// Gets a copy of the encoded CRDT update.
    /// </summary>
    public byte[] Payload => [.. _payload];
    private readonly byte[] _payload;
}
