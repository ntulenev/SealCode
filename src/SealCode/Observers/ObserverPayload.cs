using System.Text.Json;

namespace SealCode.Observers;

/// <summary>Encoded immutable guest payload and its transport size.</summary>
/// <param name="Value">The immutable JSON value.</param>
/// <param name="Bytes">The encoded payload size.</param>
internal sealed record ObserverPayload(JsonElement Value, int Bytes);

/// <summary>Guest work item bound to a presentation generation.</summary>
/// <param name="Room">The authorized presentation.</param>
/// <param name="Generation">The capability generation when queued.</param>
/// <param name="Payload">The encoded message, or null to request a snapshot.</param>
/// <param name="Bytes">The retained size charged against the queue budget.</param>
internal sealed record ObserverDeliveryItem(ObserverPresentation Room, long Generation,
    ObserverPayload? Payload, int Bytes);

/// <summary>Serializes allow-listed guest transport messages.</summary>
internal static class ObserverEncoding
{
    /// <summary>Captures an immutable JSON payload and its byte size.</summary>
    /// <param name="value">The allow-listed message.</param>
    /// <returns>The serialized message.</returns>
    public static ObserverPayload Encode(object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        using var document = JsonDocument.Parse(bytes);
        return new(document.RootElement.Clone(), bytes.Length);
    }
}
