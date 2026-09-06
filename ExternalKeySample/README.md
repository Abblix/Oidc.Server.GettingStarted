# External Key Custodian sample

An Abblix OIDC Server that signs and decrypts its tokens with keys held in an external custodian - a HashiCorp
Vault / OpenBao Transit engine or Azure Key Vault - so the private keys never enter the application's memory.
The server publishes only the public halves at `/jwks`; every private operation is a call to the custodian.

This mirrors a real microservices deployment: the provider issues service tokens through the
`client_credentials` grant, each microservice verifies them against the published JWKS, and the signing key
lives in an HSM/KMS the way a production identity provider keeps it.

## What it demonstrates

- **Signing with an external key.** A `client_credentials` request returns an `RS256` JWS whose signature was
  produced inside the custodian. The matching public key is in `/jwks`, so any service verifies the token
  without ever holding a private key.
- **Encryption with an external key.** With encryption turned on, the access token is a JWE. Introspecting it
  makes the server unwrap the Content Encryption Key inside the custodian, then validate the token.
- **The private key never leaves the custodian.** The JWKS carries only `n`/`e`; the Transit key is created
  non-exportable. Seal the custodian and token issuance fails - proof the key really lives there.
- **Or the two postures side by side.** `UseKeysIn` switches between keys held in the custodian (a round-trip per
  token, the key never in the process) and keys minted in the process and sealed to the custodian (local signing,
  the opened key held in memory). The sealing kill switch behaves oppositely in each, which is the trade made
  visible.

## Prerequisites

- .NET 10 SDK
- Docker (for the Vault / OpenBao custodian)

## Run it (Vault / OpenBao)

Start the custodian and provision its keys, then run the provider:

```bash
docker compose up -d          # OpenBao Transit + the oidc-sign / oidc-enc / oidc-kek RSA keys
export Vault__Token=root      # dev-mode token; production uses AppRole or Kubernetes auth
dotnet run --urls https://localhost:5001
```

Get a token for the demo client and read it:

```bash
curl -sk -X POST https://localhost:5001/connect/token \
  -d "grant_type=client_credentials&client_id=demo-service&client_secret=secret&scope=api"
```

The `access_token` is a three-part JWS. Its header names `"alg":"RS256"` and `"kid":"oidc-sign:1"` - the key
name plus the Transit key version, because a `kid` addresses one version and not the key as a whole.

## Verify it yourself

**The JWKS is public-only.** It carries `n` and `e`, never `d`/`p`/`q`:

```bash
curl -sk https://localhost:5001/.well-known/jwks
```

**The signature verifies against the JWKS.** Paste the token and the JWKS into any JWT verifier (for example
jwt.io) - it validates with the public key alone.

**The key really lives in the custodian (kill switch).** Seal it and watch signing stop:

```bash
docker compose exec openbao bao operator seal
curl -sk -o /dev/null -w "%{http_code}\n" -X POST https://localhost:5001/connect/token \
  -d "grant_type=client_credentials&client_id=demo-service&client_secret=secret&scope=api"
# -> 500: with the key sealed away, the server cannot sign. Recreate the custodian to continue:
docker compose down && docker compose up -d
```

That 500 is what the token endpoint has to say here: RFC 6749 section 5.2 lists the error codes it may return, and
an unreachable key store is none of them. The `server_error` code belongs to the authorization endpoint, which
needs it because a 500 cannot travel through a redirect. What the body carries depends on the environment: the
launch profile runs the sample in Development, where ASP.NET Core answers with the exception and its stack trace,
naming the custodian key path. In Production the same failure is a bare 500. Neither is a shaped OAuth error, and
a real provider would decide which of the two its operators get.

**The encryption path.** Turn encryption on and introspect the result:

```bash
export Provider__EncryptAccessToken=true
dotnet run --urls https://localhost:5001
```

Now the `access_token` is a five-part JWE (`"alg":"RSA-OAEP-256"`, `"enc":"A256CBC-HS512"`, `"kid":"oidc-enc:1"`).
Introspecting it makes
the server unwrap the CEK inside the custodian:

```bash
AT=... # the encrypted access_token
curl -sk -X POST https://localhost:5001/connect/introspect \
  -d "token=$AT&client_id=demo-service&client_secret=secret"
# -> {"active":true,...}: the server decrypted the token through the custodian.
```

## Mint the keys in the process instead

`UseKeysIn` chooses the posture. Everything above is the default, `Custodian`: the private half never leaves the
custodian. Set it to `Process` and the server mints its own signing key, seals it to `oidc-kek`, keeps the sealed
copy in the custodian's own store (Vault's KV v2 engine here), and then signs locally:

```bash
export UseKeysIn=Process
dotnet run --urls https://localhost:5001
```

The token still verifies against `/jwks`, but the `kid` is now the minted key's thumbprint, not `oidc-sign`: the
server generated the key, so it named it. The sealed key sits in the ring as ciphertext, never a plaintext key:

```bash
docker compose exec openbao bao kv list secret/oidc-keyring
# one entry per active key; read one and it is a compact JWE (alg=RSA-OAEP-256, kid=oidc-kek:1), not a raw key.
```

The kill switch now behaves the opposite way, which is the whole trade. Seal the custodian and a **running** server
keeps issuing tokens, because it signs from the key it already opened into memory:

```bash
docker compose exec openbao bao operator seal
curl -sk -o /dev/null -w "%{http_code}\n" -X POST https://localhost:5001/connect/token \
  -d "grant_type=client_credentials&client_id=demo-service&client_secret=secret&scope=api"
# -> 200: signing is local now, so a sealed custodian does not stop a running server. A cold start would: a fresh
#    server cannot open the ring without the custodian.
```

That is the placement choice in one line. `Custodian` keeps the key out of the process and pays a round-trip per token;
`Process` signs locally and keeps the sealed keys durable, at the cost of holding the opened key in memory. The
full trade-off is in [EXTERNAL_KEYS.md](https://github.com/Abblix/Oidc.Server/blob/master/EXTERNAL_KEYS.md).

## Switch to Azure Key Vault

Set `KeyCustodian` to `Azure`, point `Azure:KeyVaultUri` at your vault, and create two RSA keys named
`oidc-sign` and `oidc-enc` in it, and grant the identity you sign in as Key Vault Crypto User on them - every
signature and unwrap is that identity calling the vault.

The package takes credentials two ways: a service principal named by `Azure:TenantId`, `Azure:ClientId` and
`Azure:ClientSecret`, or, with those left unset, `DefaultAzureCredential` (Azure CLI sign-in, a managed identity,
or environment variables). This sample uses the second and its `Azure` section carries no credential fields at
all, so there is nowhere in a committed file for a secret to end up. Azure Key Vault's Standard tier with
software-protected RSA keys has no per-key monthly fee, so a demo stays within the free credit.

`UseKeysIn` works the same against Azure. For `Custodian`, only the custodian call changes: `AddAzureCustodian`
satisfies the same `IKeyCustodian` seam as the Vault one, so the switch is driven purely by configuration. For
`Process`, the ring lives in Blob Storage rather than Vault's KV engine, so there is a little more to set up:
create an `oidc-kek` RSA key in the vault, set `Azure:Blob:ServiceUri` to a storage account's blob endpoint (the
container is created on first use), and grant the running identity Key Vault Crypto User on the key plus Storage
Blob Data Contributor on the container.

## Settings

| Setting | Meaning |
| --- | --- |
| `KeyCustodian` | Which custodian backs the keys: `Vault` or `Azure`. |
| `UseKeysIn` | The posture: `Custodian` (the private half stays in the custodian) or `Process` (the server mints keys, seals them to the custodian, and signs locally). |
| `Provider:Issuer` | The OIDC issuer identifier and the base URL the server runs on. |
| `Provider:EncryptAccessToken` | `true` encrypts access tokens (JWE) to exercise the unwrap path; `false` leaves them a verifiable JWS. |
| `Provider:Scope` | The scope the demo client may request. |
| `Provider:ClientId` / `Provider:ClientSecretSha512Hash` | The `client_credentials` client's identity. The configuration the server loads carries the SHA-512 hash, base64-encoded, rather than the secret itself; the sample holds no default for it, so an absent value stops the server. The demo secret is `secret`, which is what the request examples above send. To use a different one, compute its hash with `printf %s 'your-secret' \| openssl dgst -sha512 -binary \| base64 -w0`. |
| `Provider:SigningKeyName` / `Provider:EncryptionKeyName` | The custodian's key names. They sit here, not in the `Vault` or `Azure` section, because they are not part of the connection: the same names work whichever custodian holds the keys. |
| `Provider:KeyEncryptionKeyName` | The custodian's key that seals the minted keys. Used only when `UseKeysIn` is `Process`. |
| `Vault:Address` | Base URL of the Vault / OpenBao server. |
| `Vault:TransitMount` | Mount path of the Transit engine (default `transit`). |
| `Vault:Token` | Presented as `X-Vault-Token`. Bound like any other setting, so the sample supplies it as the `Vault__Token` environment variable rather than writing it into `appsettings.json`. Production replaces it with the `Vault:Authentication` AppRole or Kubernetes section, which issues short-lived tokens instead. |
| `Azure:KeyVaultUri` | The vault URI, e.g. `https://my-vault.vault.azure.net/`. |

## How it maps to the library

The provider sets no signing or encryption keys in `OidcOptions`. They come from the custodian instead, wired in
two steps that the sample keeps visibly apart.

`AddVaultCustodian` (the **Abblix.Jwt.Vault** package) or `AddAzureCustodian` (the
**Abblix.Jwt.Azure** package) says which custodian holds the keys and how to reach it. Each registers its
backend as an `IKeyCustodian`: sign and unwrap by key version, plus version enumeration by key name.

The placement call then says how the library uses it, and naming it is what makes the posture explicit: omit it and the
provider fails at startup rather than quietly falling back to keys in configuration. The sample picks the call from
`UseKeysIn`. `UseKeysInCustodian` routes every private operation through the shared crypto seam and publishes the
**public-only** JWKs; the missing private half is what sends an operation to the custodian, addressed by the `kid`
of the exact key version (Transit publishes `oidc-sign:1`, Key Vault `oidc-sign/<version>`, so a rotation overlaps
and the bare key name is never a `kid`). `UseKeysInProcess` instead mints the signing key in the process and seals
it to the key-encryption key, so it is followed by `PersistRingToVaultKeyValue` or `PersistRingToAzureBlob` to say
where the sealed ring lives; signing then runs locally and the `kid` is the minted key's own thumbprint.

There is no custodian code in the sample: the packages carry it. A host with a different backend (an on-prem HSM,
AWS KMS) implements one `IKeyCustodian` and wires it with `AddCustodian<T>()` plus the same placement call. The shared
model, including what the guarantee costs and does not cover, is in
[EXTERNAL_KEYS.md](https://github.com/Abblix/Oidc.Server/blob/master/EXTERNAL_KEYS.md).

## Not for production as-is

This is a demo. A real deployment: runs the custodian with persistent storage, a real seal, and short-lived
tokens from AppRole / Kubernetes / managed identity (never a static root token); serves the provider over a
trusted TLS certificate; adds a readiness probe on the custodian so an instance stops taking traffic when its
keys are unreachable; and rotates keys by minting a new `kid` and publishing it before signing with it.
