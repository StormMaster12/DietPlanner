using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DietPlanner.Authentication;

/// <summary>
/// Single-user Basic Auth scheme backed by the APP_USERNAME/APP_PASSWORD configuration values
/// (set as Fly secrets in production). Plugs into the standard ASP.NET Core authentication
/// pipeline so [Authorize]/FallbackPolicy and the [Authorize] attribute work as normal, instead
/// of hand-checking headers in custom middleware.
/// </summary>
public sealed class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Basic";

    private readonly IConfiguration _configuration;

    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? authorizationHeader = Request.Headers.Authorization.FirstOrDefault();
        if (authorizationHeader is null || !authorizationHeader.StartsWith("Basic ", StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string? expectedUsername = _configuration["APP_USERNAME"];
        string? expectedPassword = _configuration["APP_PASSWORD"];
        if (string.IsNullOrEmpty(expectedUsername) || string.IsNullOrEmpty(expectedPassword))
        {
            return Task.FromResult(AuthenticateResult.Fail("APP_USERNAME/APP_PASSWORD are not configured."));
        }

        if (!TryParseCredentials(authorizationHeader["Basic ".Length..], out string username, out string password))
        {
            return Task.FromResult(AuthenticateResult.Fail("Malformed Authorization header."));
        }

        bool usernameMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(username), Encoding.UTF8.GetBytes(expectedUsername));
        bool passwordMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(expectedPassword));

        if (!usernameMatches || !passwordMatches)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid credentials."));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, username) };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = $"Basic realm=\"{Scheme.Name}\"";
        return base.HandleChallengeAsync(properties);
    }

    private static bool TryParseCredentials(string base64Credentials, out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;

        byte[] decodedBytes;
        try
        {
            decodedBytes = Convert.FromBase64String(base64Credentials);
        }
        catch (FormatException)
        {
            return false;
        }

        string decoded = Encoding.UTF8.GetString(decodedBytes);
        int separatorIndex = decoded.IndexOf(':');
        if (separatorIndex < 0)
        {
            return false;
        }

        username = decoded[..separatorIndex];
        password = decoded[(separatorIndex + 1)..];
        return true;
    }
}
