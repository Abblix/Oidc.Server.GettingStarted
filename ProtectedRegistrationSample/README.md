# Protected Registration Sample: registration behind initial access tokens

`ProtectedRegistrationSample` is a provider whose registration endpoint accepts only a party that holds an initial access token, and a client that registers with one and then manages its own registration. Use it as a starting point when registration is for parties you approve, such as partners or internal tools, rather than for anyone who finds the endpoint.

- `RegistrationProviderApp` is the provider, built on Abblix OIDC Server. Its registration endpoint requires an initial access token (RFC 7591 section 3). It also serves an operator endpoint, which issues initial access tokens with a lifetime and revokes them.
- `RegistrationClientApp` is a console client. It registers with the initial access token, then reads, updates and deletes its registration with the registration access token it got back (RFC 7592). On the way it tries what the provider has to refuse.

## What it demonstrates

On the provider side (`RegistrationProviderApp/Program.cs`):

- Protected registration. `RequireInitialAccessToken = true` turns away a registration request without a valid initial access token in the `Authorization` header. RFC 7591 section 3 leaves the choice to the provider: the registration endpoint "MAY accept an initial access token in the form of an OAuth 2.0 access token to limit registration to only previously authorized parties". This is also the library's default. It is written out here because it is what the sample is about.
- Issuing a token. `POST /operator/initial-access-tokens` takes the party the token is for (`subject`) and its lifetime in seconds (`expires_in`). It calls `IInitialAccessTokenService.IssueTokenAsync` and returns the token with its `jti`. A token can register clients until it expires or is revoked, so one token can serve every client a partner registers.
- Revoking a token. `DELETE /operator/initial-access-tokens/{jti}` records the `jti` as revoked in `ITokenRegistry`, at the tenant that issued the token, which is where the registration endpoint looks. A `jti` does not say when its token expires, so the record is kept for `Operator:MaxTokenLifetime`, the longest lifetime the operator endpoint will issue. That outlives any token the record could refuse.
- The operator endpoint is not open to everyone. It accepts only the operator key, sent as `Authorization: Bearer <key>` and checked by `OperatorKeyAuthenticationHandler`. The provider stores only the key's SHA-256 hash (`Operator:KeyHash`), so reading `appsettings.json` does not let anyone issue tokens. A plain SHA-256 is enough because the key is 32 random bytes and not a password a person chose. A separate command-line tool would not work for this sample: the provider keeps its signing key and its token registry in memory, so a tool running in another process would sign tokens the provider rejects and record revocations it never reads.
- The client secret can be read back. `TenantDefinition.ClientSecretEncryption` holds a key, set by `Provider:ClientSecretEncryptionKey`, that the provider uses to keep each `client_secret` it issues encrypted as well as hashed. A client reading its registration then gets its secret back, as RFC 7592 Appendix A.1 requires: "The client's current registration access token and client credentials (if applicable) MUST be included in the client information response." A provider that serves registration does not start without this key.
- Registration admits one grant type. `NewClientOptions.AllowedGrantTypes` limits registration to `client_credentials`, the grant a service uses to act for itself. A client asking for a grant that signs a user in is refused with `invalid_client_metadata`, so the provider needs no login page. A client using `client_credentials` acts for itself, so it has to prove who it is: one that registers `token_endpoint_auth_method` `none`, a public client with no credential, is refused with `invalid_client_metadata` too. The sample's client registers `client_secret_basic`. `AddConsentGrantedForEveryClient()` is there because the provider requires some consent decision, but with no user-facing grant nobody is ever asked.

On the client side (`RegistrationClientApp/Program.cs`):

- It reads `registration_endpoint` from the provider's metadata.
- It uses `registration_client_uri` exactly as the registration response gives it. RFC 7592 Appendix B: "The client MUST use the URL as given by the server and MUST NOT construct this URL from component pieces."
- The update is the registration it last read, `client_id` and `client_secret` included, with only the changed field replaced. RFC 7592 section 2.2: "This request MUST include all client metadata fields as returned to the client from a previous registration, read, or update operation. The updated client metadata fields request MUST NOT include the "registration_access_token", "registration_client_uri", "client_secret_expires_at", or "client_id_issued_at" fields". A field left out would be deleted from the registration.
- It keeps the `client_secret` and the registration access token that each read and update returns, and uses them for the next request. Appendix A.1 lets the provider issue new ones in either response, and sections 2.1 and 2.2 say "the client MUST immediately discard its previous client secret and/or registration access token". The secret also travels in the update, which section 2.2 allows on one condition: "If the client includes the "client_secret" field in the request, the value of this field MUST match the currently issued client secret for that client."
- The update returns a new registration access token, and the old one stops working at once.

## Run it

From the `Oidc.Server.GettingStarted` root, trust the development certificate once, then start the provider:

```shell
dotnet dev-certs https --trust
dotnet run --project ProtectedRegistrationSample/RegistrationProviderApp
```

The provider listens on `https://localhost:5011`. The commands below use `curl` from a bash shell. `-k` skips verification of the development certificate. The sample's operator key is

```
KxoddZkIXr01bdo1M9WCGfxqnCBhsn8vrXTaYMvSF7Y
```

It is published here, so it is only for trying the sample. The section at the end shows how to make your own.

### Issue an initial access token

An initial access token for a partner, valid for ten minutes:

```shell
OPERATOR_KEY=KxoddZkIXr01bdo1M9WCGfxqnCBhsn8vrXTaYMvSF7Y
curl -k -X POST https://localhost:5011/operator/initial-access-tokens \
  -H "Authorization: Bearer $OPERATOR_KEY" \
  -H "Content-Type: application/json" \
  -d '{"subject":"partner-a","expires_in":600}'
```

```json
{
  "initial_access_token": "eyJ0eX...",
  "jti": "SH7eWtvw-sqIt7bkqnZC8BzMFhqInT_bKblfxjxzIrymqA0UveoNXDr9rGymIUgieb2FeUTedgnhQHuFdZxeLQ",
  "expires_at": "2026-11-23T12:16:38+00:00"
}
```

Tokens in this README are shortened: the real `initial_access_token` is a JWT of several hundred characters. Hand the token to the partner and keep the `jti`, which is what revokes the token later.

The operator endpoint refuses a request without the operator key, or with any other key:

```shell
curl -k -i -X POST https://localhost:5011/operator/initial-access-tokens \
  -H "Content-Type: application/json" -d '{"subject":"partner-a","expires_in":600}'
```

```
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer realm="operator"
```

An `expires_in` that is a whole number of seconds below 1 or above `Operator:MaxTokenLifetime` gets `400` with `{"error":"expires_in is the token lifetime in seconds, from 1 to 86400"}`, and a request without `subject` gets `400` too.

### Register and manage the client

Pass the initial access token to the client:

```shell
dotnet run --project ProtectedRegistrationSample/RegistrationClientApp -- <initial access token>
```

The client prints every request and response. It prints credentials shortened, by their first and last characters, which is enough to tell one token from another. Each step ends with the status the client expected, and the exit code is the number of responses that were not the expected ones. A run, with the default metadata the provider fills in left out of the longer responses:

```
=== 1. Register without an initial access token: refused
> POST https://localhost:5011/connect/register
{
  "client_name": "Partner A reporting job",
  "grant_types": [
    "client_credentials"
  ],
  "response_types": [],
  "token_endpoint_auth_method": "client_secret_basic"
}
< 401 Unauthorized
< WWW-Authenticate: Bearer realm="https://localhost:5011"
  as expected: 401
```

A request with no token at all gets a challenge with no error code, as RFC 6750 section 3.1 asks for a request that "lacks any authentication information".

```
=== 2. Register with the initial access token
> POST https://localhost:5011/connect/register
> Authorization: Bearer eyJ0eX...W4gV7Q
{
  "client_name": "Partner A reporting job",
  "grant_types": [
    "client_credentials"
  ],
  "response_types": [],
  "token_endpoint_auth_method": "client_secret_basic"
}
< 201 Created
{
  "client_id": "0c5fjflnckfgqo45esog",
  "client_secret": "3XYTY3...UEXA4V",
  "registration_access_token": "eyJ0eX...JvggKQ",
  "registration_client_uri": "https://localhost:5011/connect/register/0c5fjflnckfgqo45esog",
  "client_id_issued_at": 1795435599,
  "client_secret_expires_at": 1798027599,
  "token_endpoint_auth_method": "client_secret_basic",
  "grant_types": [
    "client_credentials"
  ],
  "response_types": [],
  "client_name": "Partner A reporting job",
  ...
}
  as expected: 201

=== 3. Register a client that sends no credential (token_endpoint_auth_method none): refused
> POST https://localhost:5011/connect/register
> Authorization: Bearer eyJ0eX...W4gV7Q
{
  "client_name": "Partner A reporting job",
  "grant_types": [
    "client_credentials"
  ],
  "response_types": [],
  "token_endpoint_auth_method": "none"
}
< 400 Bad Request
{
  "error": "invalid_client_metadata",
  "error_description": "The grant types client_credentials are only for a client that authenticates itself, and 'none' does not"
}
  as expected: 400

=== 4. Get a token as the registered client
> POST https://localhost:5011/connect/token
> Authorization: Basic MGM1Zm...hBNFY=
grant_type=client_credentials
< 200 OK
{
  "access_token": "eyJ0eX...iGynAA",
  "issued_token_type": "urn:ietf:params:oauth:token-type:access_token",
  "token_type": "Bearer",
  "expires_in": 600
}
  as expected: 200
```

The registered client works at the token endpoint straight away. Next it reads its registration with the registration access token, sending a `GET` to `registration_client_uri`:

```
=== 5. Read the registration
> GET https://localhost:5011/connect/register/0c5fjflnckfgqo45esog
> Authorization: Bearer eyJ0eX...JvggKQ
< 200 OK
{
  "client_id": "0c5fjflnckfgqo45esog",
  "client_secret": "3XYTY3...UEXA4V",
  "client_secret_expires_at": 1798027599,
  "registration_client_uri": "https://localhost:5011/connect/register/0c5fjflnckfgqo45esog",
  "registration_access_token": "eyJ0eX...JvggKQ",
  "token_endpoint_auth_method": "client_secret_basic",
  "grant_types": [
    "client_credentials"
  ],
  "response_types": [],
  "client_name": "Partner A reporting job",
  ...
}
  as expected: 200
```

The read carries the `client_secret` the registration issued (`...UEXA4V`) and a registration access token. The client takes both from the response and uses them from here on: RFC 7592 section 2.1 lets a read return a new secret or token, and then "the client MUST immediately discard its previous client secret and/or registration access token". This provider returns the same secret, and a token with the same identifier as the one presented. The `client_id_issued_at` of the registration response is not in the read.

The update is built from the read. First the client tries a `client_secret` of its own choosing:

```
=== 6. Update the registration with a client_secret of the client's own choosing: refused
> PUT https://localhost:5011/connect/register/0c5fjflnckfgqo45esog
> Authorization: Bearer eyJ0eX...JvggKQ
{
  "client_id": "0c5fjflnckfgqo45esog",
  "client_secret": "a-secr...-chose",
  ...
}
< 400 Bad Request
{
  "error": "invalid_client_metadata",
  "error_description": "The client_secret is not the one currently issued to the client"
}
  as expected: 400
```

RFC 7592 section 2.2: "The client MUST NOT be allowed to overwrite its existing client secret with its own chosen value." The refused update changes nothing, so the next one goes out with the same token and the secret from the read:

```
=== 7. Update the registration: a new client_name
> PUT https://localhost:5011/connect/register/0c5fjflnckfgqo45esog
> Authorization: Bearer eyJ0eX...JvggKQ
{
  "client_id": "0c5fjflnckfgqo45esog",
  "client_secret": "3XYTY3...UEXA4V",
  "token_endpoint_auth_method": "client_secret_basic",
  "grant_types": [
    "client_credentials"
  ],
  "response_types": [],
  "client_name": "Partner A nightly reporting job",
  ...
}
< 200 OK
{
  "client_id": "0c5fjflnckfgqo45esog",
  "client_secret": "3XYTY3...UEXA4V",
  "client_secret_expires_at": 1798027599,
  "registration_client_uri": "https://localhost:5011/connect/register/0c5fjflnckfgqo45esog",
  "registration_access_token": "eyJ0eX...gJcufQ",
  "client_name": "Partner A nightly reporting job",
  ...
}
  as expected: 200

=== 8. Read with the registration access token the update replaced: refused
> GET https://localhost:5011/connect/register/0c5fjflnckfgqo45esog
> Authorization: Bearer eyJ0eX...JvggKQ
< 401 Unauthorized
< WWW-Authenticate: Bearer error="invalid_token", error_description="The access token unauthorized"
  as expected: 401
```

The update returned a new registration access token (`...gJcufQ`), and the one it replaced (`...JvggKQ`) is refused from then on. RFC 7592 section 2.2: "If the authorization server includes a new client secret and/or registration access token in its response, the client MUST immediately discard its previous client secret and/or registration access token." The secret in the update response is the one the client already holds, and the client keeps it.

The client then registers a second client with the same initial access token, and reads that registration with its own registration access token (`...vXYeHQ`) to show the token works. Then it uses that token on the first client's registration:

```
=== 11. Delete the first registration with the second client's registration access token: refused
> DELETE https://localhost:5011/connect/register/0c5fjflnckfgqo45esog
> Authorization: Bearer eyJ0eX...vXYeHQ
< 401 Unauthorized
< WWW-Authenticate: Bearer error="invalid_token", error_description="The access token unauthorized"
  as expected: 401

=== 12. Delete the first registration with its own registration access token
> DELETE https://localhost:5011/connect/register/0c5fjflnckfgqo45esog
> Authorization: Bearer eyJ0eX...gJcufQ
< 204 No Content
  as expected: 204

=== 13. Read the deleted registration: refused
> GET https://localhost:5011/connect/register/0c5fjflnckfgqo45esog
> Authorization: Bearer eyJ0eX...gJcufQ
< 401 Unauthorized
< WWW-Authenticate: Bearer error="invalid_token", error_description="The access token unauthorized"
  as expected: 401

=== 14. Get a token as the deleted client: refused
> POST https://localhost:5011/connect/token
> Authorization: Basic MGM1Zm...hBNFY=
grant_type=client_credentials
< 401 Unauthorized
< WWW-Authenticate: Basic realm="https://localhost:5011"
{
  "error": "invalid_client",
  "error_description": "The client is not authorized"
}
  as expected: 401
```

A registration access token manages only the registration it was issued for. Step 11 deletes nothing: step 12 finds the first registration still in place. After the delete, everything the registration held is dead. RFC 7592 section 2.3: "A successful delete action will invalidate the "client_id", "client_secret", and "registration_access_token" for this client". Section 5 adds that the server "MUST treat all such requests as if the registration access token was invalid by returning an HTTP 401 Unauthorized error". Step 15 deletes the second registration, and the run ends with `Every response was the expected one.`

### An expired initial access token

An initial access token with a lifetime of three seconds registers a client while it is fresh:

```shell
curl -k -X POST https://localhost:5011/operator/initial-access-tokens \
  -H "Authorization: Bearer $OPERATOR_KEY" \
  -H "Content-Type: application/json" \
  -d '{"subject":"partner-b","expires_in":3}'

IAT=<the initial_access_token from the response>
curl -k -i -X POST https://localhost:5011/connect/register \
  -H "Authorization: Bearer $IAT" \
  -H "Content-Type: application/json" \
  -d '{"client_name":"Partner B","grant_types":["client_credentials"],"response_types":[]}'
```

```
HTTP/1.1 201 Created
```

Once its lifetime has passed, the same request is refused:

```
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer error="invalid_token", error_description="Token has expired"
```

### A revoked initial access token

Issue a token, register with it once to see it accepted, then revoke it by the `jti` from the issue response:

```shell
curl -k -i -X DELETE https://localhost:5011/operator/initial-access-tokens/<jti> \
  -H "Authorization: Bearer $OPERATOR_KEY"
```

```
HTTP/1.1 204 No Content
```

The next registration with that token is refused:

```
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer error="invalid_token", error_description="Token was revoked"
```

The client run with a revoked token stops at step 2 with the same challenge, prints `The scenario cannot go on: the registration was refused.` and exits with `1`. Revoking a `jti` that was never issued also answers `204`: revoking is idempotent, and the answer does not tell anyone which identifiers exist. Clients already registered with a token keep working after the token is revoked. The token only lets a party register. The registrations themselves are managed by their own registration access tokens.

## Before you take it further

- Use your own operator key. Generate 32 random bytes as the key and put the base64 SHA-256 of the key in `Operator:KeyHash`, for example with `openssl rand -base64 32` for the key and `printf %s "$KEY" | openssl dgst -sha256 -binary | base64` for the hash. Keep the key in the operator's secret store. Set the hash through an environment variable or a secret store instead of `appsettings.json` if you would rather not keep it in the repository. The provider does not start without a valid `Operator:KeyHash`, and the error says which setting to fix.
- Use your own client secret encryption key. The one in `appsettings.json` is published with the sample, so it keeps no secret from anyone who can read the stored registrations. Generate 32 random bytes, for example with `openssl rand -base64 32`, and set `Provider:ClientSecretEncryptionKey` through an environment variable or a secret store rather than in the file. The provider does not start without the key, or with one shorter than 32 bytes.
- Registered clients, the signing key and revocations all live in memory and are gone when the provider restarts. Restarting also invalidates every initial access token and registration access token issued before. A real deployment keeps the signing key and the storage behind `ITokenRegistry` and the client store somewhere that survives a restart and is shared by every instance of the provider.
- `Operator:MaxTokenLifetime` is both the longest lifetime the operator endpoint issues and how long a revocation is kept. Change it in one place and the two stay in step.
