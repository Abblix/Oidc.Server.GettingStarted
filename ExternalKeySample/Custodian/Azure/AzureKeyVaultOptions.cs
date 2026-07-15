namespace ExternalKeySample.Custodian.Azure;

/// <summary>
/// Binds the <c>Azure</c> section of appsettings. Points the sample at an Azure Key Vault and names the two
/// keys it uses. The keys are software- or HSM-protected RSA keys whose private half never leaves the vault;
/// this process only ever sends bytes to sign or decrypt and receives the result.
/// </summary>
public sealed class AzureKeyVaultOptions
{
    public const string SectionName = "Azure";

    /// <summary>The vault URI, e.g. <c>https://my-vault.vault.azure.net/</c>. Credentials are resolved by
    /// <c>DefaultAzureCredential</c> (environment, managed identity, Azure CLI, ...), never from config.</summary>
    public string KeyVaultUri { get; set; } = "";

    /// <summary>Name of the Key Vault RSA key used to sign tokens. Also the published <c>kid</c>.</summary>
    public string SigningKeyName { get; set; } = "oidc-sign";

    /// <summary>Name of the Key Vault RSA key used to unwrap encrypted-token CEKs. Also the published <c>kid</c>.</summary>
    public string EncryptionKeyName { get; set; } = "oidc-enc";
}
