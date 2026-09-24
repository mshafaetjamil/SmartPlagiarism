using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SmartPlagiarism.Core.Abstractions;

namespace SmartPlagiarism.Web.Authorization;

/// <summary>
/// Authenticates REST API calls by the <c>X-Api-Key</c> header. Used only by the
/// /api/v1 controllers; the web UI keeps its cookie sign-in.
///
/// A missing or bad key gets a plain 401 - never the redirect to the login page
/// that cookie authentication would give, which an API client cannot follow.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiKeyService apiKeys)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    public const string HeaderName = "X-Api-Key";

    /// <summary>Claim holding the authenticated key's id.</summary>
    public const string ApiKeyIdClaim = "api_key_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || values.Count == 0)
        {
            return AuthenticateResult.NoResult();
        }

        var client = await apiKeys.AuthenticateAsync(values.ToString(), Context.RequestAborted);

        if (client is null)
        {
            return AuthenticateResult.Fail("Invalid or revoked API key.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ApiKeyIdClaim, client.ApiKeyId.ToString(CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, client.Name),
            ],
            SchemeName);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"{SchemeName} header=\"{HeaderName}\"";
        return Task.CompletedTask;
    }

    /// <summary>The calling API client, read back from the claims this handler issued.</summary>
    public static ApiClient ClientOf(ClaimsPrincipal user) => new(
        int.Parse(user.FindFirstValue(ApiKeyIdClaim)!, CultureInfo.InvariantCulture),
        user.FindFirstValue(ClaimTypes.Name) ?? string.Empty);
}
