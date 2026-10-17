using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class CustomGoogleHandler : OAuthHandler<GoogleOptions>
{
    public CustomGoogleHandler(
        IOptionsMonitor<GoogleOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override void GenerateCorrelationId(AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        properties.Items[".xsrf"] = Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        // No correlation cookie — CSRF token travels in encrypted state.
    }

    protected override bool ValidateCorrelationId(AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (!properties.Items.TryGetValue(".xsrf", out var correlationId) || string.IsNullOrEmpty(correlationId))
        {
            Logger.LogWarning("Correlation '.xsrf' missing from state.");
            return false;
        }
        properties.Items.Remove(".xsrf");
        return true;
    }

    // Copy of GoogleHandler's claim extraction — needed because we inherit
    // from OAuthHandler, not GoogleHandler, so the base won't map these for us.
    protected override async Task<AuthenticationTicket> CreateTicketAsync(
        ClaimsIdentity identity,
        AuthenticationProperties properties,
        OAuthTokenResponse tokens)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Options.UserInformationEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Backchannel.SendAsync(request, Context.RequestAborted);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Google userinfo failed: {response.StatusCode}");

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Context.RequestAborted));

        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, payload.RootElement.GetString("sub")!));
        var email = payload.RootElement.GetString("email");
        if (email != null) identity.AddClaim(new Claim(ClaimTypes.Email, email));
        var name = payload.RootElement.GetString("name");
        if (name != null) identity.AddClaim(new Claim(ClaimTypes.Name, name));
        var picture = payload.RootElement.GetString("picture");
        if (picture != null) identity.AddClaim(new Claim("picture", picture));

        return await base.CreateTicketAsync(identity, properties, tokens);
    }
}

internal static class JsonElementExtensions
{
    public static string? GetString(this JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}