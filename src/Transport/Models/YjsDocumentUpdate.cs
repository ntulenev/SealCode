namespace Transport.Models;

/// <summary>
/// Represents one encoded incremental Yjs update in a synchronization response.
/// </summary>
/// <param name="Revision">The assigned document revision.</param>
/// <param name="Update">The base64-encoded update.</param>
public sealed record YjsDocumentUpdate(long Revision, string Update);
