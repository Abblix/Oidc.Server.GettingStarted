namespace ExternalKeySample;

/// <summary>
/// Selects which external key custodian holds the server's signing and encryption keys. Bound from the
/// top-level <c>KeyCustodian</c> setting (a string like <c>"Vault"</c> maps to the matching member).
/// </summary>
public enum KeyCustodian
{
    /// <summary>HashiCorp Vault or OpenBao Transit secrets engine.</summary>
    Vault,

    /// <summary>Azure Key Vault.</summary>
    Azure,
}
