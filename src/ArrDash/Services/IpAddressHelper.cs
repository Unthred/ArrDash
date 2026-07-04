using System.Net;
using System.Net.Sockets;

namespace ArrDash.Services;

public static class IpAddressHelper
{
    public static bool? IsPrivate(string? remoteEndPoint)
    {
        var address = ExtractAddress(remoteEndPoint);
        return address is null ? null : IsPrivate(address);
    }

    private static IPAddress? ExtractAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (IPEndPoint.TryParse(value, out var endpoint))
            return endpoint.Address;

        return IPAddress.TryParse(value, out var address) ? address : null;
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                10 => true,
                172 => b[1] is >= 16 and <= 31,
                192 => b[1] == 168,
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Unique local addresses fc00::/7
            var b = address.GetAddressBytes();
            return (b[0] & 0xfe) == 0xfc;
        }

        return false;
    }
}
