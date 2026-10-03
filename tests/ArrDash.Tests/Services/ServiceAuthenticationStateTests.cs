using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Services;

public class ServiceAuthenticationStateTests
{
    [Theory]
    [InlineData("HTTP 401")]
    [InlineData("HTTP 403")]
    [InlineData("Unauthorized")]
    [InlineData("token has expired")]
    public void IsAuthenticationFailure_recognises_sanitized_auth_status(string error) =>
        Assert.True(ServiceAuthenticationState.IsAuthenticationFailure(error));

    [Theory]
    [InlineData(null)]
    [InlineData("HTTP 503")]
    [InlineData("connection timed out")]
    public void IsAuthenticationFailure_does_not_turn_transient_outages_into_auth_expiry(string? error) =>
        Assert.False(ServiceAuthenticationState.IsAuthenticationFailure(error));

    [Fact]
    public void FindExpiredCredentials_only_returns_configured_offline_auth_failures()
    {
        var services = new[]
        {
            new ServiceHealth("radarr", "Radarr", true, false, "HTTP 401", null),
            new ServiceHealth("sonarr", "Sonarr", true, false, "HTTP 503", null),
            new ServiceHealth("plex", "Plex", false, false, "HTTP 401", null),
            new ServiceHealth("emby", "Emby", true, true, "HTTP 401", null)
        };

        var actual = ServiceAuthenticationState.FindExpiredCredentials(services);

        Assert.Collection(actual, service => Assert.Equal("radarr", service.Key));
    }
}
