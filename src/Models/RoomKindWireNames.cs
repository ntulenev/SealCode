namespace Models;

/// <summary>Converts workspace kinds to and from their stable wire names.</summary>
public static class RoomKindWireNames
{
    /// <summary>Gets the stable wire name for a workspace kind.</summary>
    /// <param name="kind">The workspace kind.</param>
    /// <returns>The lowercase wire name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is undefined.</exception>
    public static string ToWireName(this RoomKind kind) => kind switch
    {
        RoomKind.Code => "code",
        RoomKind.Whiteboard => "whiteboard",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>Parses a wire name, defaulting an omitted kind to code.</summary>
    /// <param name="name">The case-sensitive wire name.</param>
    /// <param name="kind">The parsed workspace kind.</param>
    /// <returns>Whether the name is valid.</returns>
    public static bool TryParseWireName(string? name, out RoomKind kind)
    {
        kind = name == "whiteboard" ? RoomKind.Whiteboard : RoomKind.Code;
        return name is null or "code" or "whiteboard";
    }
}
