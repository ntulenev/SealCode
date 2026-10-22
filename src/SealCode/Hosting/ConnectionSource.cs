using System.Net;
using System.Net.Sockets;

namespace SealCode.Hosting;

/// <summary>
/// Normalizes network sources for resource accounting.
/// </summary>
internal static class ConnectionSource
{
    /// <summary>
    /// Returns an IPv4 address, an IPv6 /64 prefix, or the unknown source key.
    /// </summary>
    /// <param name="address">The remote address.</param>
    /// <returns>The normalized source key.</returns>
    public static string From(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return address.ToString();
        }
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes).ToString();
    }
}
