using System.Collections.Concurrent;

namespace SealCode.Domain;

public sealed class RoomState
{
    public string RoomId { get; init; } = "";
    public string Name { get; set; } = "";
    public string Language { get; set; } = "csharp";
    public string Text { get; set; } = "";
    public int Version { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> ConnectedUsers { get; } = new(StringComparer.Ordinal);
}
