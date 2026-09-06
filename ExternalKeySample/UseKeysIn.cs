namespace ExternalKeySample;

/// <summary>
/// Which posture the library uses the custodian in. This is the security choice, so the sample names it in
/// configuration rather than defaulting to one. The two are written up in
/// <see href="https://github.com/Abblix/Oidc.Server/blob/master/EXTERNAL_KEYS.md">EXTERNAL_KEYS.md</see>.
/// </summary>
public enum UseKeysIn
{
    /// <summary>
    /// The custodian holds the keys and the private half never enters this process: every signature and every
    /// Content Encryption Key unwrap is a round-trip to it.
    /// </summary>
    Custodian,

    /// <summary>
    /// The server mints its own keys, seals each to a key the custodian holds, and keeps the sealed copies in a
    /// store the same backend provides. Signing is local; the private half lives in process memory while in use.
    /// </summary>
    Process,
}
