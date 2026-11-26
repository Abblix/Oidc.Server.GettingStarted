# MCP Server Sample: a company task tracker used by an AI assistant

A company runs a task tracker and wants its employees to use it from the AI assistant they already like, made by another company. The tracker is exposed as a Model Context Protocol server. The assistant reaches it on an employee's behalf, and the company's own authorization server, built on Abblix OIDC Server, decides what the assistant may do.

## The scenario in plain words

Three parties take part, and each sees something different.

- The company runs the tracker (`McpServerApp`) and its sign-in service (`ProviderApp`). It never signed a contract with the assistant's vendor and holds no account for the assistant. What it decides is which vendors it trusts, by listing the web address under which a vendor publishes its assistants.
- The employee, John Doe, opens the assistant and asks it about his tasks. The assistant sends him to the company's sign-in page. He signs in with his company account, and a consent page names the assistant, the web site its description is published on, and where the employee goes back to. He allows it and comes back to the assistant, which can now list his tasks. When he asks it to add a task, the assistant sends him to the provider once more, this time asking to change his tasks as well; he allows that too, and it adds the task and closes it. He never types his password into the assistant.
- The vendor, Example Assistant, publishes a short description of its assistant on its own web site: its name, its home page, and where the company's sign-in page may send the employee back to. The assistant uses the address of that description as its name at the company (`McpClientApp`).

The employee can also let the assistant read his tasks without changing them, by clearing the permission to change them on the consent page. The assistant then sees the list but is refused when it tries to add a task, and the tracker tells it which permission it lacks.

In this sample the three run on one machine: the tracker on `https://localhost:5008`, the sign-in service on `https://localhost:5007`, and the assistant on `https://localhost:5009`, where it also serves the vendor's description. In production the vendor publishes the description on its own domain, such as `https://assistant.example.com/client-metadata.json`, and the company adds `https://assistant.example.com/` to `AllowedClientIdPrefixes`. That one line is the whole of the company's setup for the vendor: no registration, no shared secret, and every assistant the vendor publishes under that address is accepted.

## How a client becomes known to the provider

An MCP client that has never met a provider has two ways to become known to it, and this provider accepts both.

- By its metadata document (Client ID Metadata Documents, draft-ietf-oauth-client-id-metadata-document-03). The client's `client_id` is the https URL of a JSON document its vendor publishes, and the provider fetches the document. The client is bound to the vendor's web address: only whoever controls `https://assistant.example.com/` can publish a client under it, and the company can trust a vendor by its address.
- By registering itself (Dynamic Client Registration, RFC 7591). The client posts its name and redirect URI to the provider and gets back an identifier the provider made up. Anybody can register any name, so the name says nothing about who made the client, and the provider forgets the client when it restarts.

The MCP specification (revision 2026-07-28) prefers the first. Its client registration section has clients that support both "Use Client ID Metadata Documents if the Authorization Server indicates that it supports them" and "Use Dynamic Client Registration as a fallback if the Authorization Server supports it". It marks dynamic registration as deprecated, "included for backwards compatibility with earlier versions of the MCP authorization spec", and a provider serving clients written to those revisions keeps it.

## Discovery, common to both

- The assistant calls the tracker without a token and gets `401` with `WWW-Authenticate: Bearer resource_metadata="...", scope="tasks.read"`. The protected resource metadata lists `tasks.read` as well: the MCP specification has `scopes_supported` "represent the minimal set of scopes necessary for basic functionality", with more asked for when a call needs them.
- It reads the protected resource metadata (RFC 9728) at that address. The document names the resource, `https://localhost:5008/mcp`, and the authorization server, `https://localhost:5007`.
- It reads the provider's metadata at `/.well-known/oauth-authorization-server` (RFC 8414). There it finds `client_id_metadata_document_supported: true`, the `registration_endpoint`, and S256 among the PKCE methods. The SDK refuses a provider without S256.

From here the assistant takes one of the two ways below. Both end with the authorization code flow with PKCE, naming the tracker in the `resource` parameter (RFC 8707) of the authorization and token requests, and with an access token whose `aud` is exactly `https://localhost:5008/mcp`.

## The assistant known by its metadata document

The vendor's document, `McpClientApp/client-metadata.json`, which the assistant serves at `https://localhost:5009/client-metadata.json`:

```json
{
  "client_id": "https://localhost:5009/client-metadata.json",
  "client_name": "Example Assistant",
  "client_uri": "https://localhost:5009/",
  "redirect_uris": [ "http://127.0.0.1:5010/callback" ],
  "grant_types": [ "authorization_code", "refresh_token" ],
  "response_types": [ "code" ],
  "token_endpoint_auth_methods_supported": [ "none" ],
  "application_type": "native"
}
```

What the assistant does:

- It sees `client_id_metadata_document_supported` in the provider's metadata and sends the browser to the authorization endpoint with `client_id=https://localhost:5009/client-metadata.json`, its redirect URI, `resource`, the scope the tracker named, `tasks.read`, plus `offline_access`, and a PKCE challenge. It does not call the registration endpoint.
- After the redirect, it redeems the code at the token endpoint under the same `client_id`, with the PKCE verifier and no secret, and gets an access token and a refresh token.

What the provider fetches and checks, before it shows the employee anything:

- The `client_id` is an https URL and no client is configured or registered under it, so the provider treats it as a document address. It accepts only an address that starts with one of `ClientIdMetadataDocuments:AllowedClientIdPrefixes` in its `appsettings.json`. Any other address is refused with `invalid_client` and nothing is fetched.
- It fetches the URL with `Accept: application/json` through the library's guarded HTTP client. It follows no redirects, reads no more than `MaxDocumentSize`, and accepts only a `200` answer whose body is JSON.
- The document's `client_id` must equal the URL it was fetched from, character for character. The draft says the value "MUST match the Client Identifier URL, which MUST also match the URL that the authorization server used to fetch the document".
- The document may carry no secret and no method based on one (draft section 4.1). The provider requires PKCE of every client known by its document.
- Its grant types are narrowed to the ones the provider allows a new client, `authorization_code` and `refresh_token` (`NewClientOptions.AllowedGrantTypes`), the same limit a registering client meets.
- The redirect URI in the request must be one the document lists. Draft section 4.2 asks for "an exact match (using simple string comparison) of a registered redirect URL" and makes one exception: "redirect URIs on the loopback network interface, which allow for randomly assigned ports". So `http://127.0.0.1:5010/callback` admits the same address on any port, as RFC 8252 section 7.3 asks for a native application, and nothing with another host or path.
- It keeps the document as long as the response's cache headers allow, within the `MinimumCacheDuration` and `MaximumCacheDuration` the host sets, so the next request from the same assistant is not another fetch.

What the employee sees: the provider's sign-in page, then the consent page, then the assistant's page at `http://127.0.0.1:5010/callback`. The consent page names the assistant by the document's `client_name`, says the description is published at `localhost:5009`, the host of the `client_id`, and names the host the code goes to, `127.0.0.1`. Because that host is this computer, the page warns that any program running on it could receive the code, as the MCP specification asks ("SHOULD display additional warnings for `localhost`-only redirect URIs"). Each scope has its own checkbox, so the employee can allow some and clear others; the token carries only the scopes left checked. The name is the document's own claim and nobody reviewed it (draft section 10.10), so the host beside it is what the employee can check. Allow sends the code; Deny, or Allow with every scope cleared, ends the request with `access_denied`. The redirect carries `code`, `state` and `iss`, and the SDK checks `iss`, which draft section 7 makes mandatory in this flow.

The assistant then calls `who_am_i`, which answers `subject=1234567890; client_id=https://localhost:5009/client-metadata.json; scope=offline_access tasks.read`. The tracker knows the assistant by its URL, too.

When the provider refuses, it answers the authorization request with `400` and never redirects to the assistant:

- A redirect URI with another host or path than the document lists: `invalid_request`.
- An address outside `AllowedClientIdPrefixes`: `invalid_client`, with no request to that address.
- A document that cannot be fetched (the vendor's server is down, the name does not resolve) or is refused (not JSON, a `client_id` that differs, a secret inside): `invalid_client`. The provider's log names the reason.

### A document served on your own machine

The document in this sample lives on `localhost`, and draft section 10.7 forbids exactly that in production: servers "MUST NOT fetch a Client ID Metadata Document URL or any URLs contained within a Client ID Metadata Document that resolve to special-use IP addresses". The same section allows one exception: servers "deployed for development or testing purposes MAY relax this restriction to allow fetching from loopback addresses when the authorization server itself is also running on a loopback address". `ProviderApp` turns that exception on in the Development environment only, by setting `ClientIdMetadataDocuments.DocumentAddresses` to `ClientIdMetadataDocumentAddresses.AllowLoopback` under `builder.Environment.IsDevelopment()`. The library applies it only while the tenant's issuer is itself on loopback, and refuses to start a host in the Production environment with it set. It relaxes the rule for the document's own URL alone: a URL inside the document that the provider fetches, such as `jwks_uri` or `sector_identifier_uri`, must still resolve to a public address. A URL it only displays or records, such as `client_uri`, is not fetched, so the sample's `https://localhost:5009/` is accepted there. Run with any other environment, or with an issuer that is not on loopback, the provider refuses the sample's document like any other loopback address. A vendor's document on its public site needs no exception at all.

## The assistant registering itself

What the assistant does:

- It posts its metadata to the `registration_endpoint` with no initial access token: its name and redirect URI, read from the same document, `application_type` `native` (the SDK infers it from the loopback redirect URI, as the MCP specification asks of a client), the scopes it will use, and `token_endpoint_auth_method` `client_secret_post`, which the SDK always sends. The provider issues it a `client_secret`, and the assistant sends it with every token request, so a registered assistant holds a secret where one known by its document holds none. The provider holds a registered client to the scopes it registered, so a scope asked for later could not be added by step-up: the assistant registers, and asks, for `tasks.read` and `tasks.write` at once (`RegistrationScopes` in its `appsettings.json`). A client known by its document has no such list and starts with `tasks.read` alone.
- It gets back a `client_id` the provider made up, and runs the same authorization code flow with PKCE under that identifier.

What the provider checks:

- `RequireInitialAccessToken = false` lets a client register without a token handed out in advance. The library's default asks for one.
- `NewClientOptions.AllowedGrantTypes` holds registration to `authorization_code` and `refresh_token`. A client asking for `client_credentials` or anything else is refused with `invalid_client_metadata`, because such a client would get tokens for the tracker with no employee behind them.
- A client registering as a web application is refused a `localhost` redirect.
- A refresh token goes only to a client that registered the `refresh_token` grant. A client that leaves `grant_types` out is registered with `authorization_code` alone, and its request for `offline_access` is dropped without an error: the token response simply carries no refresh token. A client that registered `refresh_token` and also a `scope` has to include `offline_access` in that scope, or it is refused with `invalid_scope` when it asks for `offline_access`.

What the employee sees: the same sign-in page, a consent page that says the client registered itself, so its name is its own claim, and the same redirect. `who_am_i` answers with the provider-made `client_id` in place of the URL. Nothing ties that identifier to the vendor: a registration named "Example Assistant" could come from anyone.

## Reading and changing tasks

The tracker has two scopes, `tasks.read` for `list_my_tasks` and `who_am_i`, and `tasks.write` for `create_task` and `close_task`. Every tool works on the tasks of the employee the access token was issued for, read from its subject, so an assistant acting for one employee never sees another's.

A call to a tool whose scope the token lacks gets the answer the MCP specification gives for it: "HTTP 403 Forbidden" with `WWW-Authenticate: Bearer error="insufficient_scope", scope="tasks.write", resource_metadata="..."`, naming the scope "needed to satisfy the current operation". A client acting for a user "SHOULD attempt the step-up authorization flow", asking the user again with the scope added. The assistant known by its document does exactly that: its first call that changes a task is answered 403, it sends the employee to the provider asking for `tasks.read tasks.write`, and after the second consent it repeats the call with the new token. The sample's read-only run keeps the assistant to `tasks.read`: the SDK sends the employee to the provider once more with the same scopes, and when the new token is refused as well it stops, because a step-up that asks for nothing new cannot succeed. An employee who clears `tasks.write` on the second consent page ends the same way.

## Signing out at the provider

When the employee signs out at the provider, Abblix OIDC Server 3.1 revokes the tokens issued in that session (`RevokeSessionTokensOnLogout`, on by default). The assistant's refresh token stops working, so the assistant cannot get a new access token without sending the employee to sign in again. The access token it already holds keeps working until it expires, because the tracker checks its signature and expiry itself and never asks the provider about it. A tracker that has to stop access at once introspects the token (RFC 7662) instead.

The sample does not show this, because `ProviderApp` has no sign-out page: its job here is the sign-in an MCP client needs. Sign-out is shown by `TestClientApp` with either Getting Started provider.

## What it demonstrates

On the provider side (`ProviderApp/Program.cs`):

- The tracker is declared as a `ResourceDefinition` with its two scopes. The provider writes the resource a client names into the access token's audience and answers `invalid_target` for a resource it does not know.
- `AddClientIdMetadataDocuments()` resolves a `client_id` that is an https URL by fetching its document, and advertises `client_id_metadata_document_supported`. The feature is experimental in Abblix OIDC Server 3.1 because the draft is not an RFC yet, so the compiler reports `ABX9001` where it is used, and `Program.cs` suppresses it there.
- The employee approves every authorization on a consent page. Any application can become a client, by registering itself or by naming its metadata document, so the provider cannot tell who is asking, and RFC 10017 section 6.3.3.2 says an authorization server "SHOULD NOT process authorization requests automatically without user consent or interaction, except when the authorization server can assure the identity of the client application". `ConsentPageDecisions` is the sample's `IUserConsentsProvider`: it holds every request's consent pending, so the library sends the user to the tenant's `ConsentUri`, `/Auth/Consent`. Allow resumes the request with the consent given, and Deny ends it with `access_denied`. A decision answers only the request it was made on and is not remembered: the section lets an earlier approval count only for a client limited to exact HTTPS redirect URIs, and an MCP client redirects to a loopback `http` address. With nothing remembered, no approval given to one client and resource is reused for another, which draft section 10.12 asks of any approval a server keeps.
- `AllowedClientIdPrefixes` is the company's list of trusted vendors. The library does not require it with a consent provider that asks the user, and without it any document is fetched and the consent page is the employee's only check. A provider that grants every client everything it asks for (`AddConsentGrantedForEveryClient`) does not start without the list, because any URL could then get the employee's tokens without the employee being asked.
- Open registration lets anyone create clients, so each address may send only `Registration:PermitLimit` registration requests per `Registration:Window`, refused ones included; more are answered `429`. The limit applies to the registration endpoint alone, recognised by the address the library publishes for it, and every other endpoint stays unlimited.
- Every page the provider renders, the sign-in form and the consent page included, forbids being framed (`Content-Security-Policy` and `X-Frame-Options`), so a page on another site cannot load it under a decoy button (RFC 9700 section 4.16).
- `AddDynamicClientRegistration()` serves the registration endpoint and advertises it in the metadata.
- No pre-registered clients at all: every client arrives one of the two ways.
- `NewClientOptions` limits both kinds of client the same way. `AllowedGrantTypes` admits only the grant types acting for a user. `AllowedResources` lets such a client get a token for the tracker and nothing else the provider protects. `OfflineAccess` set to `Allowed` states that the user's consent is enough for a refresh token, so a client asking for `offline_access` without `prompt=consent` still gets one (OpenID Connect Core 1.0 section 11 has that scope ignored otherwise).

On the tracker side (`McpServerApp/Program.cs`), using the official C# MCP SDK (`ModelContextProtocol.AspNetCore`):

- The SDK's MCP authentication scheme publishes the protected resource metadata at `/.well-known/oauth-protected-resource/mcp`, the address RFC 9728 derives from the resource `https://localhost:5008/mcp`.
- `JwtBearer` validates the token. Its `Authority` is the provider, so the signing keys and the issuer come from the provider's metadata. Its `Audience` is the tracker's own resource URI. A token issued for another resource, or for none, gets `401`, and so does a token that carries no user: one whose subject is missing, or one the provider issued to the client on its own behalf, which it marks with `idp` `client_credentials`. The subject is no guide to that, since a client with pairwise subjects gets a pseudonym there rather than its `client_id`. A token that is not an access token (`typ` `at+jwt`) is refused too.
- Each tool names its policy with `[ToolPolicy]`, and each policy is satisfied by one scope from the settings. `ToolScopeGate` answers a call that fails its tool's policy with 403 and that policy's scope, before the SDK dispatches the call. It is attached to the endpoint `MapMcp` maps, so every path that routes there passes it, and it reads the request with the SDK's own message types. A body it cannot read as one JSON-RPC message, or a `tools/call` whose parameters it cannot read, gets `400` from the gate itself, so no call reaches the SDK without passing it.
- `tools/list` names every tool, whatever the token's scope. An assistant offers its model only the tools the server lists, so a tool left out of the list is never called, and the user is never asked for its scope. That is why the tools do not carry `[Authorize]`: the SDK's authorization filters, which it requires for that attribute, leave such a tool out of the list, and they refuse a call inside the JSON-RPC response, which a client cannot step up from.
- `ScopeChallengeHandler` adds the `scope` parameter to the SDK's challenge, which the specification recommends, and `error="invalid_token"` when the request carried a token that was refused.

On the assistant side (`McpClientApp/Program.cs`), using the C# MCP SDK client (`ModelContextProtocol.Core`):

- `ClientOAuthOptions.ClientMetadataDocumentUri` is the document's URL. The SDK sends it as the `client_id` when the provider advertises support, and registers the client otherwise. `--Registration dynamic` leaves it out, so the assistant registers even here.
- The app serves the vendor's document and receives the provider's redirect. The assistant's name and redirect URI are read from the document both ways, so the document and the registration cannot say different things.
- `ScopeSelector` takes the scope the tracker names, keeps the assistant to its `Scopes` setting when one is given, and adds the `RegistrationScopes` when the assistant registers itself.
- It accepts a plain `http` address for the tracker only on this computer (`localhost`, `127.0.0.1`, `::1`), because the access token travels in every request.
- It asks for `tools/list` first and prints it, as an assistant does to learn which tools it can offer its model.
- Every sign-in prints the scopes it asks for, so a run shows the first sign-in and the step-up. `Client:SignInTimeout` bounds each of them.
- `AuthorizationCallbackHandler` returns `code`, `state` and `iss` from the redirect, so the SDK checks both `state` and the issuer. `BrowserSignIn` prints the authorization URL and waits for the browser to come back. `HeadlessSignIn` posts the provider's sign-in form itself as the demo employee, for a run with no one at the keyboard.

The applications meet in configuration. `McpServer:Resource` and `McpServer:Scopes` in the provider's `appsettings.json` must equal `Mcp:Resource` and `Mcp:Scopes` in the tracker's, because one writes them into tokens and the other compares tokens against them. The provider's `AllowedClientIdPrefixes` must cover the `client_id` in the assistant's document.

## Run it

From the `Oidc.Server.GettingStarted` root, trust the development certificate once, then start the provider and the tracker:

```shell
dotnet dev-certs https --trust
dotnet run --project McpServerSample/ProviderApp
dotnet run --project McpServerSample/McpServerApp
```

Then run the assistant:

```shell
dotnet run --project McpServerSample/McpClientApp
```

It prints the authorization URL. Open it in a browser: the terminal shows the provider fetching the document (`Served the metadata document (Accept: application/json)`) before the browser gets the sign-in page. Sign in as `john.doe@example.com` with password `Jd!2024$3cur3` (in memory, visible in `ProviderApp/Program.cs`) and allow the assistant on the consent page. It prints the tools the tracker lists, `create_task` among them, and the tasks. When it adds one, it prints a second sign-in, `Asking the employee to sign in and allow: tasks.read tasks.write offline_access`. Open that URL in the same browser: the provider still has you signed in, so it shows the consent page straight away. Allow again, and the assistant adds the task, closes it and lists them again. The provider keeps the document for as long as it may, which for a response with no cache headers, as this app's is, means `MinimumCacheDuration`; the second sign-in and a second run within that time show no fetch. The assistant waits for each sign-in as long as `Client:SignInTimeout` in its `appsettings.json` allows, then stops and says so. If the browser shows an error instead of the sign-in page, the provider refused the request and its log says why; it does not redirect back after that, so stop the assistant with Ctrl+C.

Other ways to run it:

```shell
dotnet run --project McpServerSample/McpClientApp -- --Registration dynamic
dotnet run --project McpServerSample/McpClientApp -- --Scopes:0 tasks.read
dotnet run --project McpServerSample/McpClientApp -- --SignIn headless
```

The first registers the assistant instead. The second lets it read the tasks only, so it lists them and is refused with 403 when it tries to add one. The third signs in without a browser and answers the consent page with `HeadlessSignIn:Consent` (`allow`, or `deny` to see the refusal), running the whole flow on its own; `--HeadlessSignIn:GrantedScopes:0 tasks.read` makes it clear every other scope on the page, as an employee allowing reading alone would.

Another MCP client that supports authorization works too, pointed at `https://localhost:5008/mcp`. One that registers itself has to send `application_type` `native`, as the MCP specification requires of it, and has to run on the desktop or the command line rather than inside a web page, because the registration endpoint and the tracker answer requests from the client application itself. One known by its metadata document is accepted only when its `client_id` starts with a prefix in `AllowedClientIdPrefixes`, so add its vendor's prefix there first.

A quick look without a client:

```shell
curl -k -i -X POST https://localhost:5008/mcp
curl -k https://localhost:5008/.well-known/oauth-protected-resource/mcp
curl -k https://localhost:5007/.well-known/oauth-authorization-server
```

The first returns `401` with the challenge, the second the resource metadata, and the third the provider's metadata with `client_id_metadata_document_supported`, `registration_endpoint` and `code_challenge_methods_supported`.

## Before you take it further

- The client name on the consent page is whatever the client registered or its document claims, and the page says which. A provider that verifies some clients in another way can let those through without the page.
- Use your own client secret encryption key. A provider that serves registration keeps each `client_secret` it issues encrypted with `Provider:ClientSecretEncryptionKey`, so a client reading its registration gets its secret back, and it does not start without the key or with one shorter than 32 bytes. The key in `ProviderApp/appsettings.json` is published with the sample, so it keeps no secret from anyone who can read the stored registrations. Generate 32 random bytes, for example with `openssl rand -base64 32`, and set the key through an environment variable or a secret store rather than in the file. Outside the Development environment the provider refuses to start while the key comes from `appsettings.json`.
- Registered clients live in memory and are gone when the provider restarts. A client holding a `client_id` from before the restart has to register again. A client known by its document has nothing to redo: the provider fetches the document again.
- The tasks live in memory too, and every employee's list starts from the same seed tasks (`Tracker:SeedTasks`).
- Signing keys are generated at startup, so a restarted provider invalidates every token issued before. The tracker refuses tokens signed with the new key until it fetches the provider's keys again: `JwtBearer` does that by itself on meeting an unknown key, at most once per its refresh interval. Restarting the tracker along with the provider recovers at once. Until then the assistant stops with a 401 and says to restart the tracker.
- The registration limit counts by the address the request arrives from, in this process alone. Behind a reverse proxy that address is the proxy's, so every caller would share one count: let `UseForwardedHeaders` read the client's address from the proxies you name in `KnownProxies` or `KnownNetworks`, never from any proxy. Several instances each keep their own count, so a fleet limits at its ingress or keeps the count in a shared store.
- Open registration admits machine clients unless the host narrows the grant types, as `NewClientOptions.AllowedGrantTypes` does here. A provider that drops it hands tracker tokens to any program that asks.
- `AllowLoopback` is for a provider on a developer's machine. A provider with a public issuer fetches documents only from public addresses, whatever the setting says.

## Layout

- `ProviderApp/Program.cs`: the tenant with the tracker as its resource, metadata documents limited to the allowed prefixes, open registration, the limits both kinds of client share, and the sign-in and consent endpoints.
- `ProviderApp/ConsentPageDecisions.cs`: the consent provider that holds each request's consent until the employee answers the consent page.
- `ProviderApp/appsettings.json`: the issuer, the client secret encryption key, the registration rate limit, the tracker's resource URI and scopes, and the allowed client identifier prefixes.
- `McpServerApp/Program.cs`: `JwtBearer` bound to the provider and to the tracker's own audience, the MCP authentication scheme with the resource metadata, the scope policies, the gate, and `MapMcp`.
- `McpServerApp/TaskTools.cs`, `McpServerApp/TaskTracker.cs`: the tools and the in-memory tasks.
- `McpServerApp/ToolScopeGate.cs`: the 403 naming the scope a tool needs.
- `McpServerApp/ToolPolicyAttribute.cs`: the attribute naming the policy a tool needs, which the gate reads.
- `McpServerApp/ScopeChallengeHandler.cs`: the SDK's handler with `scope` in its challenges.
- `McpServerApp/WhoAmITool.cs`: the tool answering from the token.
- `McpServerApp/appsettings.json`: the resource URI and name, the authorization server, the scopes and the seed tasks.
- `McpClientApp/client-metadata.json`: the vendor's metadata document.
- `McpClientApp/Program.cs`: serving the document, the SDK's OAuth options both ways, and the calls the assistant makes.
- `McpClientApp/BrowserSignIn.cs`, `McpClientApp/HeadlessSignIn.cs`: the two ways the employee signs in.
- `McpClientApp/appsettings.json`: the tracker's address, the document, the sign-in timeout, the way to become known, the scopes, the scopes it registers with, the sign-in mode and the headless run's consent answer.

## The specifications behind this sample

- MCP authorization, revision 2026-07-28: https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization
- MCP client registration, revision 2026-07-28: https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization/client-registration
- OAuth Client ID Metadata Document, draft 03: https://datatracker.ietf.org/doc/html/draft-ietf-oauth-client-id-metadata-document-03
- OAuth 2.0 Protected Resource Metadata (RFC 9728): https://www.rfc-editor.org/rfc/rfc9728
- Resource Indicators for OAuth 2.0 (RFC 8707): https://www.rfc-editor.org/rfc/rfc8707
- OAuth 2.0 Dynamic Client Registration (RFC 7591): https://www.rfc-editor.org/rfc/rfc7591
- OAuth 2.0 for Native Apps (RFC 8252): https://www.rfc-editor.org/rfc/rfc8252
- OAuth 2.0 Authorization Server Issuer Identification (RFC 9207): https://www.rfc-editor.org/rfc/rfc9207
- OAuth 2.0 for Browser-Based Applications (RFC 10017): https://www.rfc-editor.org/rfc/rfc10017
- OAuth 2.0 Security Best Current Practice (RFC 9700): https://www.rfc-editor.org/rfc/rfc9700
