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

## Prerequisites

- .NET 10 SDK
- Docker (for the Vault / OpenBao custodian)

## Run it (Vault / OpenBao)

Start the custodian and provision its keys, then run the provider:

```bash
docker compose up -d          # OpenBao Transit + the oidc-sign / oidc-enc RSA keys
export Vault__Token=root      # dev-mode token; production uses AppRole or Kubernetes auth
dotnet run --urls https://localhost:5001
```

Get a token for the demo client and read it:

```bash
curl -sk -X POST https://localhost:5001/connect/token \
  -d "grant_type=client_credentials&client_id=demo-service&client_secret=secret&scope=api"
```

The `access_token` is a three-part JWS. Its header names `"alg":"RS256"` and `"kid":"oidc-sign"`.

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

**The encryption path.** Turn encryption on and introspect the result:

```bash
export Provider__EncryptAccessToken=true
dotnet run --urls https://localhost:5001
```

Now the `access_token` is a five-part JWE (`"alg":"RSA-OAEP-256"`, `"kid":"oidc-enc"`). Introspecting it makes
the server unwrap the CEK inside the custodian:

```bash
AT=... # the encrypted access_token
curl -sk -X POST https://localhost:5001/connect/introspect \
  -d "token=$AT&client_id=demo-service&client_secret=secret"
# -> {"active":true,...}: the server decrypted the token through the custodian.
```

## Switch to Azure Key Vault

Set `KeyCustodian` to `Azure`, point `Azure:KeyVaultUri` at your vault, and create two RSA keys named
`oidc-sign` and `oidc-enc` in it. Credentials come from `DefaultAzureCredential` (Azure CLI sign-in, a managed
identity, or environment variables) - never from configuration. Azure Key Vault's Standard tier with
software-protected RSA keys has no per-key monthly fee, so a demo stays within the free credit.

The application code does not change: the `AddAzureExternalKeys` package satisfies the same `IKeyCustodian`
seam as the Vault one, selected purely by configuration.

## Settings

| Setting | Meaning |
| --- | --- |
| `KeyCustodian` | Which custodian backs the keys: `Vault` or `Azure`. |
| `Provider:Issuer` | The OIDC issuer identifier and the base URL the server runs on. |
| `Provider:EncryptAccessToken` | `true` encrypts access tokens (JWE) to exercise the unwrap path; `false` leaves them a verifiable JWS. |
| `Provider:Scope` | The scope the demo client may request. |
| `Provider:ClientId` / `Provider:ClientSecret` | The `client_credentials` client's identity (the secret is hashed before storage). |
| `Vault:Address` | Base URL of the Vault / OpenBao server. |
| `Vault:TransitMount` | Mount path of the Transit engine (default `transit`). |
| `Vault:SigningKeyName` / `Vault:EncryptionKeyName` | Transit key names, which are also the published `kid` values. |
| `Vault` token | Presented as `X-Vault-Token`, taken from the `Vault__Token` environment variable, never hardcoded. |
| `Azure:KeyVaultUri` | The vault URI, e.g. `https://my-vault.vault.azure.net/`. |
| `Azure:SigningKeyName` / `Azure:EncryptionKeyName` | Key Vault key names, which are also the published `kid` values. |

## How it maps to the library

The provider sets no signing or encryption keys in `OidcOptions`. Instead, one call wires the chosen custodian:
`AddVaultExternalKeys` (the **Abblix.Oidc.Server.Vault** package) or `AddAzureExternalKeys` (the
**Abblix.Oidc.Server.Azure** package). Each registers its backend as an `IKeyCustodian` (sign and unwrap by
version, plus version enumeration by key name), routes every private operation through the shared crypto seam, and
replaces the default key provider with one that publishes the **public-only** JWKs - the missing private half is
what routes the operation to the custodian, keyed by a `kid` that is also the custodian's handle for the key.

There is no custodian code in the sample: the packages carry it. A host with a different backend (an on-prem HSM,
AWS KMS) implements one `IKeyCustodian` and calls `AddExternalKeys`.

## Not for production as-is

This is a demo. A real deployment: runs the custodian with persistent storage, a real seal, and short-lived
tokens from AppRole / Kubernetes / managed identity (never a static root token); serves the provider over a
trusted TLS certificate; adds a readiness probe on the custodian so an instance stops taking traffic when its
keys are unreachable; and rotates keys by minting a new `kid` and publishing it before signing with it.
