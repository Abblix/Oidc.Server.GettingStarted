using Abblix.Oidc;
using Abblix.Oidc.Server.Common;
using Abblix.Oidc.Server.Endpoints.DynamicClientManagement.Validation;
using Abblix.Oidc.Server.Features.MultiTenancy;

namespace ProviderApp;

/// <summary>
/// A step of the registration pipeline that admits only grant types acting for a signed-in user.
/// </summary>
/// <remarks>
/// Registration here is open, so without this step anybody could register a client_credentials client and get an
/// access token for the MCP server with no user behind it, and the tools would run for nobody in particular. MCP
/// clients act for a user through the authorization code flow, so nothing else is admitted. refresh_token stays
/// on the list because MCP clients register it, though this provider issues them no refresh token: a client that
/// registers itself is not allowed the offline_access scope.
/// </remarks>
public sealed class UserGrantsOnlyValidator : SyncClientRegistrationContextValidator
{
    private static readonly string[] AllowedGrantTypes = [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken];

    protected override OidcError? Validate(TenantDefinition tenant, ClientRegistrationValidationContext context)
    {
        var refused = context.Request.GrantTypes.Except(AllowedGrantTypes, StringComparer.Ordinal).ToArray();
        if (refused.Length == 0)
            return null;

        return ErrorFactory.InvalidClientMetadata(
            $"Only {string.Join(" and ", AllowedGrantTypes)} may be registered here, not {string.Join(", ", refused)}");
    }
}
