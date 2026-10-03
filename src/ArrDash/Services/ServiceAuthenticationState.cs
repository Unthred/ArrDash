using ArrDash.Models;

namespace ArrDash.Services;

/// <summary>Classifies only the sanitized status text already retained in a service health snapshot.</summary>
public static class ServiceAuthenticationState
{
    public static IReadOnlyList<ServiceHealth> FindExpiredCredentials(IEnumerable<ServiceHealth> services) =>
        services.Where(service => service.Configured && !service.Online && IsAuthenticationFailure(service.Error)).ToList();

    public static bool IsAuthenticationFailure(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return false;

        return error.Contains("401", StringComparison.Ordinal)
            || error.Contains("403", StringComparison.Ordinal)
            || error.Contains("unauthoriz", StringComparison.OrdinalIgnoreCase)
            || error.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
            || error.Contains("authentication failed", StringComparison.OrdinalIgnoreCase)
            || error.Contains("token has expired", StringComparison.OrdinalIgnoreCase);
    }
}
