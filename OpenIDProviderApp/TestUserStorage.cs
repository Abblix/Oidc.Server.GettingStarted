using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Abblix.Jwt;
using Abblix.Oidc.Server.Features.UserInfo;

namespace OpenIDProviderApp;

/// <summary>
/// Represents user information, including subject identifier and profile attributes like name and email.
/// </summary>
public record UserInfo(string Subject, string Name, string Email, string Password);

/// <summary>
/// Provides a test storage implementation for user information, simulating a database of users.
/// </summary>
public class TestUserStorage(params UserInfo[] users) : IUserInfoProvider
{
    /// <summary>
    /// Asynchronously retrieves user information based on a subject identifier and a collection of requested claims.
    /// </summary>
    public Task<JsonObject?> GetUserInfoAsync(string subject, IEnumerable<string> requestedClaims)
    {
        var userInfo = GetUserInfo(subject, requestedClaims);
        return Task.FromResult(userInfo);
    }

    /// <summary>
    /// Retrieves user information for a specific subject with respect to requested claims.
    /// </summary>
    /// <param name="subject">The subject identifier for which user information is requested.</param>
    /// <param name="requestedClaims">The claims that determine which information to include in the response.</param>
    /// <returns>A <see cref="JsonObject"/> containing the requested user information, or null if no user matches the subject.</returns>
    private JsonObject? GetUserInfo(string subject, IEnumerable<string> requestedClaims)
    {
        var user = users.FirstOrDefault(user => user.Subject == subject);

        if (user == null)
        {
            return null;
        }

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
        return result;
    }

    /// <summary>
    /// Attempts to authenticate a user based on their email and password.
    /// </summary>
    /// <param name="email">The email of the user attempting to authenticate.</param>
    /// <param name="password">The password provided for authentication.</param>
    /// <param name="subject">When this method returns, contains the subject identifier of the authenticated user if the return value is true; otherwise, null.</param>
    /// <returns>true if the authentication is successful; otherwise, false.</returns>
    public bool TryAuthenticate(
        string email,
        string password,
        [NotNullWhen(true)] out string? subject)
    {
        foreach (var user in users)
        {
            if (user.Email == email && user.Password == password)
            {
                subject = user.Subject;
                return true;
            }
        }

        subject = null;
        return false;
    }
}
