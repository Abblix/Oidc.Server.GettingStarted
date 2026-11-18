using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Abblix.Jwt;
using Abblix.Oidc.Server.Features.MultiTenancy;
using Abblix.Oidc.Server.Features.UserAuthentication;
using Abblix.Oidc.Server.Features.UserInfo;

namespace OpenIDProviderApp;

public record UserInfo(string Subject, string Name, string Email, string Password);

public class TestUserStorage(params UserInfo[] users) : IUserInfoProvider
{
    public Task<JsonObject?> GetUserInfoAsync(
        TenantDefinition tenant, AuthSession authSession, IEnumerable<string> requestedClaims)
    {
        var userInfo = GetUserInfo(authSession.Subject, requestedClaims);
        return Task.FromResult(userInfo);
    }

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
