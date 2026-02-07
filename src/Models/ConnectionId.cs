namespace Models;

/// <summary>
/// Represents a connection identifier.
/// </summary>
public readonly record struct ConnectionId
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionId"/> struct.
    /// </summary>
    /// <param name="value">The connection identifier value.</param>
    /// <exception cref="ArgumentException">Thrown when the value is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public ConnectionId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Connection id is required", nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// Gets the connection identifier value.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}
