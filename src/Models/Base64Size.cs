namespace Models;

/// <summary>
/// Computes Base64 lengths without encoding a payload.
/// </summary>
internal static class Base64Size
{
    /// <summary>
    /// Gets the encoded character count for a byte count.
    /// </summary>
    /// <param name="bytes">The decoded payload size in bytes.</param>
    /// <returns>The padded Base64 character count.</returns>
    public static long EncodedLength(int bytes) => 4L * ((bytes + 2L) / 3);
}
