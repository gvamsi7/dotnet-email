using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ISH.CustomerPortal.API.Security;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-API-Key";
    private readonly IOptionsMonitor<ApiSecurityOptions> security;

    public ApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder, IOptionsMonitor<ApiSecurityOptions> security)
        : base(options, logger, encoder)
    {
        this.security = security;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
            return Task.FromResult(AuthenticateResult.NoResult());
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]) || values[0]!.Length > 512)
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(security.CurrentValue.ApiKey));
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(values[0]!));
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        // The key identifies the calling backend, not the feedback user.
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "feedback-api-client")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
