namespace ExternalKeySample;

/// <summary>
/// Binds the <c>Provider</c> section of appsettings: the OIDC issuer, the single client_credentials client
/// this demo issues tokens to, and the toggle that turns on access-token encryption to exercise the
/// custodian's unwrap path. Keeping these in configuration means the sample is retargeted without touching code.
/// </summary>
public sealed class ProviderOptions
{
    public const string SectionName = "Provider";

    /// <summary>The OIDC issuer identifier, which is also the base URL the server runs on.</summary>
    public string Issuer { get; set; } = "https://localhost:5001";

    /// <summary>
    /// When true, access tokens are encrypted (JWE): validating one drives the remote CEK unwrap through the
    /// custodian. When false they are a plain JWS whose signature verifies against <c>/jwks</c>.
    /// </summary>
    public bool EncryptAccessToken { get; set; }

    /// <summary>The scope the demo client is allowed to request.</summary>
    public string Scope { get; set; } = "api";

    /// <summary>The client_credentials client identifier.</summary>
    public string ClientId { get; set; } = "demo-service";

    /// <summary>The secret the demo client authenticates with. It is hashed before it reaches the client store.</summary>
    public string ClientSecret { get; set; } = "secret";

    /// <summary>
    /// The custodian's name for the signing key. It lives here rather than in the Vault or Azure section because
    /// it is not a property of the connection: the same name is used whichever custodian holds the key.
    /// </summary>
    public string SigningKeyName { get; set; } = "oidc-sign";

    /// <summary>
    /// The custodian's name for the key that unwraps encrypted-token CEKs. Used only while
    /// <see cref="EncryptAccessToken"/> is on.
    /// </summary>
    public string EncryptionKeyName { get; set; } = "oidc-enc";
}
