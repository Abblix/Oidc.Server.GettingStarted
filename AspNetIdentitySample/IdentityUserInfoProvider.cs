using System.Text.Json.Nodes;
using Abblix.Oidc.Server.Features.UserAuthentication;
using Abblix.Oidc.Server.Features.UserInfo;
using Microsoft.AspNetCore.Identity;

namespace AspNetIdentitySample;

/// <summary>
/// Turns an authenticated subject into OIDC claims using the ASP.NET Core Identity user store.
/// The library ships no IUserInfoProvider of its own, so this registration is mandatory.
/// </summary>
public sealed class IdentityUserInfoProvider(UserManager<IdentityUser> users) : IUserInfoProvider
{
    public async Task<JsonObject?> GetUserInfoAsync(AuthSession authSession, IEnumerable<string> requestedClaims)
    {
        // the subject is the Identity user id: set at login, resolved here
        var user = await users.FindByIdAsync(authSession.Subject);
        if (user is null)
            return null; // no user, no claims: userinfo answers invalid_token, no ID token is issued

        // requestedClaims arrives as a deferred sequence: materialize it once; names match by ordinal comparison
        var requested = requestedClaims.ToHashSet(StringComparer.Ordinal);

        var claims = new JsonObject();
        foreach (var claim in requested)
        {
            JsonNode? value = claim switch
            {
                // prefer the session's snapshot when it carries one: it reflects what was true at login
                "email" => authSession.Email ?? user.Email,
                "email_verified" => authSession.EmailVerified ?? user.EmailConfirmed,
                "preferred_username" => user.UserName,
                "phone_number" => user.PhoneNumber,
                "phone_number_verified" => user.PhoneNumber is not null ? user.PhoneNumberConfirmed : null,
                _ => null,
            };

            if (value is not null)
                claims[claim] = value;
        }

        // profile claims Identity does not model (name, given_name, picture...) live
        // in Identity's claim store: surface the ones the request asked for
        foreach (var stored in await users.GetClaimsAsync(user))
        {
            if (requested.Contains(stored.Type) && !claims.ContainsKey(stored.Type))
                claims[stored.Type] = stored.Value;
        }

        return claims;
    }
}
