namespace Models;

/// <summary>
/// Computes Base64 lengths without encoding a payload.
/// </summary>
internal static class Base64Size
{
    /// <summary>
    /// Gets the encoded character count for a byte count.
    /// </summary>
    public static long EncodedLength(int bytes) => 4L * ((bytes + 2L) / 3);
}
