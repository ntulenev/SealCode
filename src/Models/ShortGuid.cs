namespace Models;

/// <summary>
/// Represents a GUID encoded as a URL-safe, Base64 string (22 characters, no padding),
/// while preserving full reversibility to the original <see cref="Guid"/>.
/// </summary>
/// <remarks>
/// The string representation uses RFC 4648 "Base64url" character substitutions:
/// <c>'/' -&gt; '_'</c>, <c>'+' -&gt; '-'</c>, and trims <c>'='</c> padding.
/// </remarks>
public readonly record struct ShortGuid
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShortGuid"/> struct from an existing <see cref="Guid"/>.
    /// The string representation is computed once at creation time.
    /// </summary>
    /// <param name="value">The GUID value.</param>
    public ShortGuid(Guid value)
    {
        Value = value;
        _string = Encode(value);
    }

    /// <summary>
    /// Gets the underlying <see cref="Guid"/> value.
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// Returns the URL-safe 22-character representation of this GUID.
    /// </summary>
    /// <returns>The URL-safe Base64 string representation (22 characters, no padding).</returns>
    public override string ToString() => _string;

    /// <summary>
    /// Attempts to parse a string into a <see cref="ShortGuid"/>.
    /// </summary>
    /// <param name="s">The input string.</param>
    /// <param name="result">When this method returns, contains the parsed <see cref="ShortGuid"/>
    /// if parsing succeeded; otherwise, the default value.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// Accepts either:
    /// <list type="bullet">
    /// <item><description>A 22-character URL-safe Base64 representation (no padding).</description></item>
    /// <item><description>A standard GUID string (<c>xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx</c>) or any
    /// format accepted by <see cref="Guid.TryParse(string?, out Guid)"/>.</description></item>
    /// </list>
    /// </remarks>
    public static bool TryParse(string? s, out ShortGuid result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        if (s.Length == 22)
        {
            try
            {
                var base64 = s.Replace('_', '/').Replace('-', '+') + "==";
                var bytes = Convert.FromBase64String(base64);

                if (bytes.Length != 16)
                {
                    return false;
                }

                result = new ShortGuid(new Guid(bytes));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        if (Guid.TryParse(s, out var g))
        {
            result = new ShortGuid(g);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Converts this instance to the underlying <see cref="Guid"/>.
    /// </summary>
    /// <returns>The underlying <see cref="Guid"/>.</returns>
    public Guid ToGuid() => Value;

    /// <summary>
    /// Creates a <see cref="ShortGuid"/> from a <see cref="Guid"/>.
    /// </summary>
    /// <param name="g">The GUID value.</param>
    /// <returns>The created <see cref="ShortGuid"/>.</returns>
    public static ShortGuid FromGuid(Guid g) => new(g);

    /// <summary>
    /// Implicitly converts a <see cref="ShortGuid"/> to a <see cref="Guid"/>.
    /// </summary>
    /// <param name="s">The <see cref="ShortGuid"/> value.</param>
    public static implicit operator Guid(ShortGuid s) => s.Value;

    /// <summary>
    /// Implicitly converts a <see cref="Guid"/> to a <see cref="ShortGuid"/>.
    /// </summary>
    /// <param name="g">The GUID value.</param>
    public static implicit operator ShortGuid(Guid g) => new(g);

    /// <summary>
    /// Determines whether this instance is equal to another <see cref="ShortGuid"/> by comparing the underlying <see cref="Guid"/> values.
    /// </summary>
    /// <param name="other">The other value to compare.</param>
    /// <returns><see langword="true"/> if the underlying GUID values are equal; otherwise, <see langword="false"/>.</returns>
    public bool Equals(ShortGuid other) => Value.Equals(other.Value);

    /// <summary>
    /// Returns a hash code for this instance based on the underlying <see cref="Guid"/>.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code.</returns>
    public override int GetHashCode() => Value.GetHashCode();

    private static string Encode(Guid value)
    {
        return Convert.ToBase64String(value.ToByteArray())
            .Replace("/", "_", StringComparison.InvariantCulture)
            .Replace("+", "-", StringComparison.InvariantCulture)
            .TrimEnd('=');
    }

    private readonly string _string;
}
