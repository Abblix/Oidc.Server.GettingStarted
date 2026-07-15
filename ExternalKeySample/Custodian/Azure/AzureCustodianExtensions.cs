using Abblix.Jwt.Encryption;
using Abblix.Jwt.Signing;
using Abblix.Oidc.Server.Common.Interfaces;

namespace ExternalKeySample.Custodian.Azure;

/// <summary>
/// Wires the Azure Key Vault custodian: the two remote crypto ports and the public-only key provider, plus the
/// SDK client they share.
/// </summary>
public static class AzureCustodianExtensions
{
    public static IServiceCollection AddAzureCustodian(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AzureKeyVaultOptions>(configuration.GetSection(AzureKeyVaultOptions.SectionName));
        services.AddSingleton<AzureKeyVaultClient>();

        // The two remote ports are optional constructor dependencies on the JWT orchestrators; the library
        // registers no default for them. The keys provider REPLACES the library default, so this method must be
        // called AFTER AddOidc* for the last singular registration to win.
        services.AddSingleton<IExternalSigner, AzureKeyVaultSigner>();
        services.AddSingleton<IExternalKeyEncryptor, AzureKeyVaultKeyEncryptor>();
        services.AddSingleton<IAuthServiceKeysProvider, AzureKeyVaultKeysProvider>();

        return services;
    }
}
