namespace ExternalKeySample;

/// <summary>
/// Selects which external key custodian holds the server's signing and encryption keys. Bound from the
/// top-level <c>KeyCustodian</c> setting. Each member's name is also the configuration section the chosen
/// custodian binds its options from, so naming the custodian names its settings.
/// </summary>
public enum KeyCustodian
{
    /// <summary>HashiCorp Vault or OpenBao Transit secrets engine.</summary>
    Vault,

    /// <summary>Azure Key Vault.</summary>
    Azure,
}
