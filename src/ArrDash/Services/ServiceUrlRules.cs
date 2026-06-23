using System.Net;

namespace ArrDash.Services;

public static class ServiceUrlRules
{
    public static bool IsPrivateOrLoopbackUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IPAddress.TryParse(uri.Host, out var ip))
            return false;

        if (IPAddress.IsLoopback(ip))
            return true;

        var bytes = ip.GetAddressBytes();
        return ip.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => IsPrivateIpv4(bytes),
            System.Net.Sockets.AddressFamily.InterNetworkV6 => ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal,
            _ => false
        };
    }

    private static bool IsPrivateIpv4(byte[] bytes) =>
        bytes[0] switch
        {
            10 => true,
            172 => bytes[1] is >= 16 and <= 31,
            192 => bytes[1] == 168,
            127 => true,
            _ => false
        };
}
