namespace ExternalKeySample.Custodian.Vault;

/// <summary>
/// Binds the <c>Vault</c> section of appsettings. Points the sample at a Vault / OpenBao Transit secrets
/// engine and names the two keys it uses. The keys live inside Transit as non-exportable RSA keys, so their
/// private halves never reach this process.
/// </summary>
public sealed class VaultTransitOptions
{
    public const string SectionName = "Vault";

    /// <summary>Base URL of the Vault / OpenBao server, e.g. <c>http://127.0.0.1:8200</c>.</summary>
    public string Address { get; set; } = "http://127.0.0.1:8200";

    /// <summary>
    /// Auth token presented as the <c>X-Vault-Token</c> header. Sourced from the environment
    /// (<c>Vault__Token</c>), never hardcoded: dev mode uses a well-known root token, while production
    /// authenticates through AppRole or Kubernetes and mints a short-lived token instead.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>Mount path of the Transit engine (the default mount is <c>transit</c>).</summary>
    public string TransitMount { get; set; } = "transit";

    /// <summary>Name of the Transit RSA key used to sign tokens. Also the published <c>kid</c>.</summary>
    public string SigningKeyName { get; set; } = "oidc-sign";

    /// <summary>Name of the Transit RSA key used to unwrap encrypted-token CEKs. Also the published <c>kid</c>.</summary>
    public string EncryptionKeyName { get; set; } = "oidc-enc";
}
