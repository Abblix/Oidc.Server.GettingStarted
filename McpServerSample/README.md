# MCP Server Sample: an authorized MCP server with its own provider

`McpServerSample` is three applications configured for each other. `McpServerApp` is a Model Context Protocol server with one tool, and it accepts only access tokens issued for it. `ProviderApp` is the authorization server, built on Abblix OIDC Server. `McpClientApp` is an MCP client built on the official C# MCP SDK. It has never met the provider. It finds the provider through the MCP server, gets the user to sign in and calls the tool.

A client that has never met a provider has two ways to become known to it, and the provider here accepts both:

- The client names itself by the https URL of a metadata document it publishes, and the provider fetches that document (Client ID Metadata Documents, draft-ietf-oauth-client-id-metadata-document-03).
- The client registers itself at the provider's registration endpoint (Dynamic Client Registration, RFC 7591).

The MCP specification (revision 2026-07-28) prefers the first. Its client registration section has clients that support both "Use Client ID Metadata Documents if the Authorization Server indicates that it supports them" and "Use Dynamic Client Registration as a fallback if the Authorization Server supports it". It marks dynamic registration as deprecated, "included for backwards compatibility with earlier versions of the MCP authorization spec", and a provider serving clients written to those revisions keeps it.

## Discovery, common to both

- The client calls the MCP endpoint without a token and gets `401` with `WWW-Authenticate: Bearer resource_metadata="...", scope="mcp:tools"`.
- It reads the protected resource metadata (RFC 9728) at that address. The document names the resource, `https://localhost:5008/mcp`, and the authorization server, `https://localhost:5007`.
- It reads the provider's metadata at `/.well-known/oauth-authorization-server` (RFC 8414). There it finds `client_id_metadata_document_supported: true`, the `registration_endpoint`, and S256 among the PKCE methods. The SDK refuses a provider without S256.

From here the client takes one of the two scenarios below. Both end with the authorization code flow with PKCE, naming the MCP server in the `resource` parameter (RFC 8707) of the authorization and token requests. Then the client calls the tool with an access token whose `aud` is exactly `https://localhost:5008/mcp`.

## Scenario 1: the client is known by its metadata document

The client publishes a JSON document about itself and uses the document's URL as its `client_id`. `McpClientApp` serves its document, `McpClientApp/client-metadata.json`, at `https://localhost:5009/client-metadata.json`:

```json
{
  "client_id": "https://localhost:5009/client-metadata.json",
  "client_name": "McpServerSample client",
  "redirect_uris": [ "http://127.0.0.1:5010/callback" ],
  "grant_types": [ "authorization_code", "refresh_token" ],
  "response_types": [ "code" ],
  "token_endpoint_auth_methods_supported": [ "none" ],
  "application_type": "native"
}
```

What the client does:

- It sees `client_id_metadata_document_supported` in the provider's metadata and sends the browser to the authorization endpoint with `client_id=https://localhost:5009/client-metadata.json`, its redirect URI, `resource`, `scope=mcp:tools offline_access` and a PKCE challenge. It does not call the registration endpoint.
- After the redirect, it redeems the code at the token endpoint under the same `client_id`, with the PKCE verifier and no secret, and gets an access token and a refresh token.

What the provider fetches and checks, before it shows the user anything:

- The `client_id` is an https URL and no client is configured or registered under it, so the provider treats it as a document address. It accepts only an address that starts with one of `ClientIdMetadataDocuments:AllowedClientIdPrefixes` in its `appsettings.json` (`https://localhost:5009/`). Any other address is refused with `invalid_client` and nothing is fetched.
- It fetches the URL with `Accept: application/json` through the library's guarded HTTP client. It follows no redirects and reads at most 5 KB. It accepts only a `200` answer whose body is JSON.
- The document's `client_id` must equal the URL it was fetched from, character for character. The draft says the value "MUST match the Client Identifier URL, which MUST also match the URL that the authorization server used to fetch the document".
- The document may carry no secret and no method based on one (draft section 4.1). This client declares `none`, which makes it a public client, and the provider then requires PKCE of it.
- Its grant types are narrowed to the ones the provider allows a new client, `authorization_code` and `refresh_token` (`NewClientOptions.AllowedGrantTypes`), the same limit a registering client meets.
- The redirect URI in the request must be one the document lists. The draft has the server "ensure that the redirect URL in an authorization request is an exact match (using simple string comparison) of a registered redirect URL", and the document is the registration.
- It keeps the document as long as the response's cache headers allow, five minutes when they say nothing and never more than an hour, so the next request from the same client is not another fetch.

What the user sees: the provider's login page, then, once signed in, the client's page at `http://127.0.0.1:5010/callback`. The redirect carries `code`, `state` and `iss`, and the SDK checks `iss`, which draft section 7 makes mandatory in this flow. There is no consent page in this sample (see "Before you take it further").

The call to `who_am_i` then answers `subject=1234567890; client_id=https://localhost:5009/client-metadata.json; scope=offline_access mcp:tools`. The client is known by its URL in the token as well.

When the provider refuses, it answers the authorization request with `400` and never redirects to the client:

- A redirect URI the document does not list: `invalid_request`.
- An address outside `AllowedClientIdPrefixes`: `invalid_client`, with no request to that address.
- A document that cannot be fetched (the client's server is down, the name does not resolve) or is refused (not JSON, a `client_id` that differs, a secret inside): `invalid_client`. The provider's log names the reason.

### A document served on your own machine

The document in this sample lives on `localhost`, and draft section 10.7 forbids exactly that in production: servers "MUST NOT fetch a Client ID Metadata Document URL or any URLs contained within a Client ID Metadata Document that resolve to special-use IP addresses". The same section allows one exception: servers "deployed for development or testing purposes MAY relax this restriction to allow fetching from loopback addresses when the authorization server itself is also running on a loopback address". `ProviderApp` turns that exception on in the Development environment only, and the library honours it only while the issuer is itself on loopback. A real MCP client publishes its document on its own public site, and a provider fetches it with no exception at all.

## Scenario 2: the client registers itself

What the client does:

- It posts its metadata to the `registration_endpoint` with no initial access token: its name, its redirect URI, `application_type` `native` (the SDK infers it from the loopback redirect URI, as the MCP specification asks of a client) and no secret.
- It gets back a `client_id` the provider made up, and runs the same authorization code flow with PKCE under that identifier.

What the provider checks:

- `RequireInitialAccessToken = false` lets a client register without a token handed out in advance. The library's default asks for one.
- `NewClientOptions.AllowedGrantTypes` holds registration to `authorization_code` and `refresh_token`. A client asking for `client_credentials` or anything else is refused with `invalid_client_metadata`, because such a client would get tokens for the MCP server with no user behind them.
- A client registering as a web application is refused a `localhost` redirect.
- A client that registers a `scope` has to include `offline_access` in it. One registered with `mcp:tools` alone is refused with `invalid_scope` when it asks for `offline_access`.

What the user sees: the same login page and the same redirect. `who_am_i` answers with the provider-made `client_id` in place of the URL.

## What it demonstrates

On the provider side (`ProviderApp/Program.cs`):

- The MCP server is declared as a `ResourceDefinition` with its `mcp:tools` scope. The provider writes the resource a client names into the access token's audience and answers `invalid_target` for a resource it does not know. The `mcp:tools` scope is declared once, on the resource, and registration accepts a scope any of the tenant's resources declares.
- `AddClientIdMetadataDocuments()` resolves a `client_id` that is an https URL by fetching its document, and advertises `client_id_metadata_document_supported`. The feature is experimental in Abblix OIDC Server 3.1 because the draft is not an RFC yet, so the compiler reports `ABX9001` where it is used, and `Program.cs` suppresses it there.
- `AllowedClientIdPrefixes` is required here, and the library will not start without it. This provider grants every client everything it asks for (`AddConsentGrantedForEveryClient`), so without a list any URL could get the user's tokens without the user being asked.
- `AddDynamicClientRegistration()` serves the registration endpoint and advertises it in the metadata.
- No pre-registered clients at all: every client arrives through one of the two scenarios.
- `NewClientOptions` limits both kinds of client the same way. `AllowedGrantTypes` admits only the grant types acting for a user. `OfflineAccess` set to `Allowed` states that the user's consent is enough for a refresh token, so a client asking for `offline_access` without `prompt=consent` still gets one (OpenID Connect Core 1.0 section 11 has that scope ignored otherwise) and keeps working after its access token expires.

On the MCP server side (`McpServerApp/Program.cs`), using the official C# MCP SDK (`ModelContextProtocol.AspNetCore`):

- The SDK's MCP authentication scheme publishes the protected resource metadata at `/.well-known/oauth-protected-resource/mcp`, the address RFC 9728 derives from the resource `https://localhost:5008/mcp`.
- `JwtBearer` validates the token. Its `Authority` is the provider, so the signing keys and the issuer come from the provider's metadata. Its `Audience` is the MCP server's own resource URI. A token issued for another resource, or for none, gets `401`, and so does a token that carries no user: one whose subject is missing or is the client itself, as a `client_credentials` token's is. A token that is not an access token (`typ` `at+jwt`) is refused too.
- `ScopeChallengeHandler` adds the `scope` parameter to the SDK's challenge, which the specification recommends, and `error="invalid_token"` when the request carried a token that was refused. It answers a token without `mcp:tools` with `403` and `error="insufficient_scope"`.
- The `who_am_i` tool reads the user and the client from the token, so a successful call shows that the user who signed in at the provider, and the client the provider issued the token to, are the ones the tool sees.

On the client side (`McpClientApp/Program.cs`), using the C# MCP SDK client (`ModelContextProtocol.Core`):

- `ClientOAuthOptions.ClientMetadataDocumentUri` is the document's URL. The SDK sends it as the `client_id` when the provider advertises support, and registers the client otherwise. `--Registration dynamic` leaves it out, so the client registers even here.
- The app serves its own document and receives the provider's redirect. The client's name and redirect URI are read from the document in both scenarios, so the document and the registration cannot say different things.
- `AuthorizationCallbackHandler` returns `code`, `state` and `iss` from the redirect, so the SDK checks both `state` and the issuer. `BrowserSignIn` prints the authorization URL and waits for the browser to come back. `HeadlessSignIn` posts the provider's login form itself as the demo user, for a run with no one at the keyboard.

The applications meet in configuration. `McpServer:Resource` in the provider's `appsettings.json` and `Mcp:Resource` in the MCP server's must be the same URI, because one writes it into tokens and the other compares tokens against it. The provider's `AllowedClientIdPrefixes` must cover the `client_id` in the client's document.

## Run it

From the `Oidc.Server.GettingStarted` root, trust the development certificate once, then start the provider and the MCP server:

```shell
dotnet dev-certs https --trust
dotnet run --project McpServerSample/ProviderApp
dotnet run --project McpServerSample/McpServerApp
```

The provider listens on `https://localhost:5007` and the MCP server on `https://localhost:5008`. Then run the client, which serves its document on `https://localhost:5009` and takes the redirect on `http://127.0.0.1:5010`:

```shell
dotnet run --project McpServerSample/McpClientApp
```

It prints the authorization URL. Open it in a browser and sign in as `john.doe@example.com` with password `Jd!2024$3cur3` (in memory, visible in `ProviderApp/Program.cs`). The terminal then shows the provider fetching the document (`Served the metadata document (Accept: application/json)`) and the answer of `who_am_i`.

Other ways to run it:

```shell
dotnet run --project McpServerSample/McpClientApp -- --Registration dynamic
dotnet run --project McpServerSample/McpClientApp -- --SignIn headless
```

The first registers the client instead (scenario 2). The second signs in without a browser and runs the whole flow on its own.

Another MCP client that supports authorization works too, pointed at `https://localhost:5008/mcp`. One that registers itself has to send `application_type` `native`, as the MCP specification requires of it, and has to run on the desktop or the command line rather than inside a web page, because the registration endpoint and the MCP server answer requests from the client application itself. One known by its metadata document is accepted only when its `client_id` starts with a prefix in `AllowedClientIdPrefixes`, so add its prefix there first.

A quick look without a client:

```shell
curl -k -i -X POST https://localhost:5008/mcp
curl -k https://localhost:5008/.well-known/oauth-protected-resource/mcp
curl -k https://localhost:5007/.well-known/oauth-authorization-server
```

The first returns `401` with the challenge, the second the resource metadata, and the third the provider's metadata with `client_id_metadata_document_supported`, `registration_endpoint` and `code_challenge_methods_supported`.

## Before you take it further

- Consent is granted automatically (`AddConsentGrantedForEveryClient`). With open registration and metadata documents, any application can become a client, so a real provider shows the user which client is asking and where it will send the code, through an `IUserConsentsProvider` of its own. For a client known by its document, nobody reviewed the name the document claims (draft section 10.10), so the page shows the host of the `client_id` beside it. The MCP specification asks that it also "clearly display the redirect URI hostname". A consent the provider remembers is keyed by the `client_id` and the resource together (draft section 10.12). The library requires `AllowedClientIdPrefixes` only while every client is granted everything, so with such a provider the list can be dropped, from `Program.cs` as well, and any client's document is accepted.
- Registered clients live in memory and are gone when the provider restarts. A client holding a `client_id` from before the restart has to register again. A client known by its document has nothing to redo: the provider fetches the document again.
- Signing keys are generated at startup, so a restarted provider invalidates every token issued before. The MCP server refuses tokens signed with the new key until it fetches the provider's keys again: `JwtBearer` does that by itself on meeting an unknown key, at most once per its refresh interval, a few minutes by default. Restarting the MCP server along with the provider recovers at once.
- Open registration admits machine clients unless the host narrows the grant types, as `NewClientOptions.AllowedGrantTypes` does here. A provider that drops it hands MCP server tokens to any program that asks.
- The loopback exception for metadata documents is for a provider on a developer's machine. A provider with a public issuer fetches documents only from public addresses.

## Layout

- `ProviderApp/Program.cs`: the tenant with the MCP server as its resource, metadata documents limited to the allowed prefixes, open registration, the limits both kinds of client share, and the login endpoints.
- `ProviderApp/appsettings.json`: the issuer, the MCP server's resource URI and scope, and the allowed client identifier prefixes.
- `McpServerApp/Program.cs`: `JwtBearer` bound to the provider and to the MCP server's own audience and refusing tokens with no user, the MCP authentication scheme with the resource metadata, the scope policy, and `MapMcp`.
- `McpServerApp/ScopeChallengeHandler.cs`: the SDK's handler with `scope` in its challenges.
- `McpServerApp/WhoAmITool.cs`: the tool.
- `McpServerApp/appsettings.json`: the resource URI, the authorization server and the scope.
- `McpClientApp/client-metadata.json`: the client's metadata document.
- `McpClientApp/Program.cs`: serving the document, the SDK's OAuth options for both scenarios, and the call to `who_am_i`.
- `McpClientApp/BrowserSignIn.cs`, `McpClientApp/HeadlessSignIn.cs`: the two ways the user signs in.
- `McpClientApp/appsettings.json`: the MCP server's address, the document, the scenario and the sign-in mode.

## The specifications behind this sample

- MCP authorization, revision 2026-07-28: https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization
- MCP client registration, revision 2026-07-28: https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization/client-registration
- OAuth Client ID Metadata Document, draft 03: https://datatracker.ietf.org/doc/html/draft-ietf-oauth-client-id-metadata-document-03
- OAuth 2.0 Protected Resource Metadata (RFC 9728): https://www.rfc-editor.org/rfc/rfc9728
- Resource Indicators for OAuth 2.0 (RFC 8707): https://www.rfc-editor.org/rfc/rfc8707
- OAuth 2.0 Dynamic Client Registration (RFC 7591): https://www.rfc-editor.org/rfc/rfc7591
- OAuth 2.0 Authorization Server Issuer Identification (RFC 9207): https://www.rfc-editor.org/rfc/rfc9207
