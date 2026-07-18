using Microsoft.AspNetCore.DataProtection;

namespace ArrDash.Services;

public sealed class TraktTokenProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("ArrDash.Trakt.OAuthTokens.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
