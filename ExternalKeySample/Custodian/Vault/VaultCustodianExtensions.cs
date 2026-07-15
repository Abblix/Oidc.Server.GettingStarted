using Abblix.Jwt.Encryption;
using Abblix.Jwt.Signing;
using Abblix.Oidc.Server.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace ExternalKeySample.Custodian.Vault;

/// <summary>
/// Wires the Vault / OpenBao Transit custodian: the two remote crypto ports and the public-only key provider,
/// plus the typed HTTP client they share.
/// </summary>
public static class VaultCustodianExtensions
{
    public static IServiceCollection AddVaultCustodian(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<VaultTransitOptions>(configuration.GetSection(VaultTransitOptions.SectionName));

        // Typed client pointed at the Transit mount, carrying the auth token header. Options are read at
        // creation so appsettings and the Vault__Token environment variable drive the address and token.
        services.AddHttpClient<VaultTransitClient>((provider, http) =>
        {
            var options = provider.GetRequiredService<IOptions<VaultTransitOptions>>().Value;
            http.BaseAddress = new Uri($"{options.Address.TrimEnd('/')}/v1/{options.TransitMount}/");
            if (!string.IsNullOrWhiteSpace(options.Token))
                http.DefaultRequestHeaders.Add("X-Vault-Token", options.Token);
        });

        // The two remote ports are optional constructor dependencies on the JWT orchestrators; the library
        // registers no default for them, so a plain AddSingleton is correct. The keys provider REPLACES the
        // library default (OidcOptionsKeysProvider), so this method must be called AFTER AddOidc* for the last
        // singular registration to win.
        services.AddSingleton<IExternalSigner, VaultTransitSigner>();
        services.AddSingleton<IExternalKeyEncryptor, VaultTransitKeyEncryptor>();
        services.AddSingleton<IAuthServiceKeysProvider, VaultKeysProvider>();

        return services;
    }
}
