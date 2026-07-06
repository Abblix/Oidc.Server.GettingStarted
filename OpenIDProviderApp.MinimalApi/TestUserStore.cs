using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Abblix.Jwt;
using Abblix.Oidc.Server.Features.UserAuthentication;
using Abblix.Oidc.Server.Features.UserInfo;

namespace OpenIDProviderApp.MinimalApi;

/// <summary>
/// Represents user information, including subject identifier and profile attributes like name and email.
/// </summary>
public record TestUser(string Subject, string Name, string Email, string Password);

/// <summary>
/// Provides a test storage implementation for user information, simulating a database of users.
/// </summary>
public class TestUserStore(params TestUser[] users) : IUserInfoProvider
{
    /// <summary>
    /// Asynchronously retrieves user information based on an authentication session and a collection of requested claims.
    /// </summary>
    public Task<JsonObject?> GetUserInfoAsync(AuthSession authSession, IEnumerable<string> requestedClaims)
    {
        var user = users.FirstOrDefault(u => u.Subject == authSession.Subject);
        if (user == null)
            return Task.FromResult<JsonObject?>(null);

        var result = new JsonObject();
        foreach (var claim in requestedClaims)
        {
            switch (claim)
            {
                case IanaClaimTypes.Sub:
                    result.Add(claim, user.Subject);
                    break;
                case IanaClaimTypes.Email:
                    result.Add(claim, user.Email);
                    break;
                case IanaClaimTypes.Name:
                    result.Add(claim, user.Name);
                    break;
            }
        }
        return Task.FromResult<JsonObject?>(result);
    }

    /// <summary>
    /// Attempts to authenticate a user based on their email and password.
    /// </summary>
    public bool TryAuthenticate(
        string email,
        string password,
        [NotNullWhen(true)] out string? subject)
    {
        var user = users.FirstOrDefault(u => u.Email == email && u.Password == password);
        subject = user?.Subject;
        return subject != null;
    }
}
