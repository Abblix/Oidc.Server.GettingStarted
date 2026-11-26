# MCP Server Sample: a company task tracker used by an AI assistant

A company runs a task tracker and wants its employees to use it from the AI assistant they already like, made by another company. The tracker is exposed as a Model Context Protocol (MCP) server. The assistant reaches it on an employee's behalf. The company's own authorization server, built on Abblix OIDC Server, decides which assistants it accepts, and the employee decides what each of them may do.

## The scenario in plain words

Three parties take part, and each sees something different.

- The company runs the tracker (`McpServerApp`) and its sign-in service, the provider (`ProviderApp`). It never signed a contract with the assistant's vendor and holds no account for the assistant. What it decides is which vendors it trusts, by listing the web address under which a vendor publishes its assistants (a `client_id` prefix).
- The vendor, Example Assistant, publishes a short description of its assistant on its own web site (its client metadata document): its name, its home page, and where the provider may send the employee back to. The assistant uses the address of that description as its `client_id` at the company (`McpClientApp`).
- The employee, John Doe, asks the assistant about his tasks. The assistant sends him to the company's sign-in page; he signs in and allows the assistant to read his tasks on a consent page, and the assistant lists them. When he asks it to add a task, the assistant asks him once more, this time for changing his tasks too, and after he allows that it adds the task. He never types his password into the assistant, and he can allow reading alone by clearing the permission to change tasks.

In this sample the three run on one machine: the provider on `https://localhost:5007`, the tracker on `https://localhost:5008`, and the assistant on `https://localhost:5009`, where it also serves the vendor's description, with its redirect on `http://127.0.0.1:5010`. In production the vendor publishes the description on its own domain, such as `https://assistant.example.com/client-metadata.json`, and the company adds `https://assistant.example.com/` to `AllowedClientIdPrefixes`. That one line is what the company sets up for the vendor: no registration and no shared secret.

## Run it

You need the .NET 11 SDK, and ports 5007, 5008, 5009 and 5010 free. From the `Oidc.Server.GettingStarted` root, trust the development certificate once:

```shell
dotnet dev-certs https --trust
```

The provider and the tracker call each other over https, so on Linux check that `dotnet dev-certs https --check --trust` reports the certificate as trusted; an untrusted one shows up as `invalid_client` on the authorization request or a `401` from the tracker.

Start each application in a terminal of its own and leave it running, the provider first:

```shell
dotnet run --project McpServerSample/ProviderApp
```

```shell
dotnet run --project McpServerSample/McpServerApp
```

Then, in a third terminal, the assistant:

```shell
dotnet run --project McpServerSample/McpClientApp
```

The assistant prints the authorization URL. Open it in a browser. As soon as the browser opens it, the assistant's terminal shows the provider fetching the description (`Served the metadata document (Accept: application/json)`), before the sign-in page appears. Sign in as `john.doe@example.com` with password `Jd!2024$3cur3` (in memory, visible in `ProviderApp/Program.cs`) and allow the assistant on the consent page. The assistant prints the tools the tracker lists, `create_task` among them, and the tasks.

When it adds a task, it prints a second sign-in, `Asking the employee to sign in and allow: tasks.read tasks.write offline_access`. Open that URL in the same browser: the provider still has you signed in, so the consent page appears straight away, now with `tasks.write` too. Allow it, and the assistant adds the task, closes it and lists the tasks again. The assistant waits for each sign-in as long as `Client:SignInTimeout` in its `appsettings.json` allows, then stops and says so. If the browser shows an error instead of the sign-in page, the provider refused the request and its log says why; it does not redirect back after that, so stop the assistant with Ctrl+C.

Other ways to run the assistant:

```shell
dotnet run --project McpServerSample/McpClientApp -- --Registration dynamic
dotnet run --project McpServerSample/McpClientApp -- --Scopes:0 tasks.read
dotnet run --project McpServerSample/McpClientApp -- --SignIn headless
```

- The first registers the assistant instead of naming its description.
- The second keeps the assistant to reading. It lists the tasks; when it tries to add one, it asks for a second sign-in with the same scopes, and after you allow it the repeated call is refused with `403` as well, so the assistant stops.
- The third signs in without a browser and answers the consent page with `HeadlessSignIn:Consent` (`allow`, or `deny` to see the refusal). It runs the whole flow with no one at the keyboard, so it works as a check of the setup in CI. `--HeadlessSignIn:GrantedScopes:0 tasks.read` makes it uncheck every other scope on the page, as an employee allowing reading alone would.

Another MCP client that supports authorization works too, pointed at `https://localhost:5008/mcp`, with three conditions:

- It runs on the desktop or the command line, not inside a web page: the provider and the tracker send no CORS headers.
- It trusts the development certificate.
- Known by its metadata document, it is accepted only when its `client_id` starts with a prefix in `AllowedClientIdPrefixes`, so add its vendor's prefix there first. Registering itself with a loopback redirect, it sends `application_type` `native`, because this provider refuses a loopback redirect to a web client.

A quick look without a client:

```shell
curl -k -i -X POST https://localhost:5008/mcp
curl -k https://localhost:5008/.well-known/oauth-protected-resource/mcp
curl -k https://localhost:5007/.well-known/oauth-authorization-server
```

The first returns `401` with the challenge, the second the tracker's resource metadata, and the third the provider's metadata with `client_id_metadata_document_supported`, `registration_endpoint` and `code_challenge_methods_supported`.

## How a client becomes known to the provider

An MCP client that has never met a provider has two ways to become known to it, and this provider accepts both.

- By its metadata document (Client ID Metadata Documents, draft-ietf-oauth-client-id-metadata-document-03). The client's `client_id` is the https URL of a JSON document its vendor publishes, and the provider fetches the document. Only whoever controls `https://assistant.example.com/` can publish a document under it, so the company can trust a vendor by its address.
- By registering itself (Dynamic Client Registration, RFC 7591). The client posts its name and redirect URI and gets back an identifier the provider generated. Anybody can register any name, so the name says nothing about who made the client, and this provider forgets the client when it restarts.

The prefix binds the description to the vendor, not the program presenting it. The assistant has no secret and redirects to this computer, so any program running here can present the same `client_id` and receive a code on a loopback port. The employee's consent, with the warning the consent page shows for a loopback redirect, is what stops that program, so trusting a vendor never skips the consent page. List a prefix only where the vendor alone controls what is published under it: a domain that hosts its users' content lets them publish clients too.

The MCP specification (revision 2026-07-28) prefers the metadata document. Its client registration section says clients that support both should "Use Client ID Metadata Documents if the Authorization Server indicates that it supports them" and "Use Dynamic Client Registration as a fallback if the Authorization Server supports it". It marks dynamic registration as deprecated, "included for backwards compatibility with earlier versions of the MCP authorization spec", so a provider that serves clients built for those revisions keeps supporting it.

## Discovery, common to both

- The assistant calls the tracker without a token and gets `401` with `WWW-Authenticate: Bearer resource_metadata="...", scope="tasks.read"`. The protected resource metadata lists only `tasks.read` as well: the MCP specification says `scopes_supported` should represent "the minimal set of scopes necessary for basic functionality", with more asked for when a call needs them.
- It reads the protected resource metadata (RFC 9728) at that address. The document names the resource, `https://localhost:5008/mcp`, and the authorization server, `https://localhost:5007`.
- It reads the provider's metadata at `/.well-known/oauth-authorization-server` (RFC 8414). There it finds `client_id_metadata_document_supported: true`, the `registration_endpoint`, and S256 among the PKCE methods; the SDK refuses a provider without S256.

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

`token_endpoint_auth_methods_supported` is the draft's own member, not a misspelled `token_endpoint_auth_method`: the assistant authenticates with `none`, that is PKCE and no secret.

What the assistant does:

- It sees `client_id_metadata_document_supported` in the provider's metadata and sends the browser to the authorization endpoint with `client_id=https://localhost:5009/client-metadata.json`, its redirect URI, `resource`, the scope the tracker named, `tasks.read`, plus `offline_access`, and a PKCE challenge. It does not call the registration endpoint.
- After the redirect, it redeems the code at the token endpoint under the same `client_id`, with the PKCE verifier and no secret, and gets an access token and a refresh token.

What the provider fetches and checks, before it shows the employee anything:

- The `client_id` is an https URL and no client is configured or registered under it, so the provider treats it as a document address. It accepts only an address that starts with one of `ClientIdMetadataDocuments:AllowedClientIdPrefixes` in its `appsettings.json`. Any other address is refused with `invalid_client` and nothing is fetched.
- It fetches the URL with `Accept: application/json` through the library's guarded HTTP client. It follows no redirects, reads no more than the library's default document size, and accepts only a `200` answer whose body is JSON.
- The document's `client_id` must equal the URL it was fetched from, character for character. The draft says the value "MUST match the Client Identifier URL, which MUST also match the URL that the authorization server used to fetch the document".
- The document may carry no secret and no method based on one (draft section 4.1). The provider requires PKCE of every client known by its document.
- Its grant types are narrowed to the ones the provider allows a new client, `authorization_code` and `refresh_token`, the same limit a registering client meets.
- The redirect URI in the request must be one the document lists. Draft section 4.2 asks for "an exact match (using simple string comparison) of a registered redirect URL" and makes one exception: "redirect URIs on the loopback network interface, which allow for randomly assigned ports". So `http://127.0.0.1:5010/callback` admits the same address on any port, as RFC 8252 section 7.3 asks for a native application, and nothing with another host or path.
- It keeps the document as long as the response's cache headers allow, within the library's default minimum and maximum cache durations, which a host changes in `ClientIdMetadataDocumentOptions`. This app sends no cache headers, so the provider keeps the document for the minimum: the second sign-in and a second run within that time show no fetch.

What the employee sees: the provider's sign-in page, then the consent page, then the assistant's page at `http://127.0.0.1:5010/callback`. The consent page names the assistant by the document's `client_name`, says the description is published at `localhost:5009`, the host of the `client_id`, and names the host the code goes to, `127.0.0.1`. Because that host is this computer, the page warns that any program running on it could receive the code, as the MCP specification asks ("SHOULD display additional warnings for `localhost`-only redirect URIs"). The name is the client's own claim (draft section 10.10), so the host beside it is what the employee can check. Each scope has its own checkbox; the token carries only the scopes left checked. Allow sends the code; Deny, or Allow with every scope cleared, ends the request with `access_denied`. The redirect carries `code`, `state` and `iss`, and the SDK checks `iss`, which draft section 7 makes mandatory in this flow.

The assistant then calls `who_am_i`, which answers `subject=1234567890; client_id=https://localhost:5009/client-metadata.json; scope=offline_access tasks.read`. The tracker knows the assistant by its URL, too.

When the provider refuses, it answers the authorization request with `400` and never redirects to the assistant:

- A redirect URI with another host or path than the document lists: `invalid_request`.
- An address outside `AllowedClientIdPrefixes`: `invalid_client`, with no request to that address.
- A document that cannot be fetched (the vendor's server is down, the name does not resolve) or is refused (not JSON, a `client_id` that differs, a secret inside): `invalid_client`. The provider's log names the reason.

The document in this sample lives on `localhost`, which draft section 10.7 forbids in production and allows for development. `ProviderApp` allows it in the Development environment only; the library applies that only while the issuer is itself on loopback, and refuses to start a host in Production with it set. Nothing else is relaxed (see [Before you take it further](#before-you-take-it-further)).

## The assistant registering itself

What the assistant does:

- It posts its metadata to the `registration_endpoint` with no initial access token: its name and redirect URI, read from the same document, `application_type` `native` (the SDK infers it from the loopback redirect URI), the scopes it will use, and `token_endpoint_auth_method` `client_secret_post`, which the SDK always sends. The provider issues it a `client_secret`, and the assistant sends it with every token request.
- The provider holds a registered client to the scopes it registered, so a scope asked for later could not be added by step-up. The assistant therefore registers with, and asks for, `tasks.read` and `tasks.write` at once (`RegistrationScopes` in its `appsettings.json`); for such a client the checkbox on the consent page is where the employee keeps writing out of the token.
- It gets back a `client_id` the provider generated, and runs the same authorization code flow with PKCE under that identifier.

What the provider checks:

- `RequireInitialAccessToken = false` lets a client register without a token handed out in advance. The library's default asks for one.
- `NewClientOptions.AllowedGrantTypes` holds registration to `authorization_code` and `refresh_token`, which is also the library's default. A client asking for `client_credentials` or anything else is refused with `invalid_client_metadata`, because such a client would get tokens for the tracker with no employee behind them.
- A client registering as a web application is refused a `localhost` redirect.
- A refresh token goes only to a client that registered the `refresh_token` grant. A client that leaves `grant_types` out is registered with `authorization_code` alone, and its request for `offline_access` is dropped without an error: the token response simply carries no refresh token. A client that registered `refresh_token` and also a `scope` has to include `offline_access` in that scope, or it is refused with `invalid_scope` when it asks for `offline_access`.

What the employee sees: the same sign-in page, a consent page that says the client registered itself, so its name is its own claim, and the same redirect. `who_am_i` answers with the generated `client_id` in place of the URL. Nothing ties that identifier to the vendor: a registration named "Example Assistant" could come from anyone.

## Reading and changing tasks

The tracker has two scopes, `tasks.read` for `list_my_tasks` and `who_am_i`, and `tasks.write` for `create_task` and `close_task`. Every tool works on the tasks of the employee the access token was issued for, read from its subject, so an assistant acting for one employee never sees another's.

The tracker lists every tool to every token, so an assistant sees `create_task` while it can only read, and calls it. That call gets the answer the MCP specification gives for a missing scope: "HTTP 403 Forbidden" with `WWW-Authenticate: Bearer error="insufficient_scope", scope="tasks.write", resource_metadata="..."`, naming the scope "needed to satisfy the current operation". A client acting for a user "SHOULD attempt the step-up authorization flow": the assistant asks the employee again with `tasks.write` added and repeats the call with the new token. A step-up that asks for nothing new cannot succeed, so an assistant kept to `tasks.read`, or an employee who clears `tasks.write` on the second consent page, ends with the `403`.

## Signing out at the provider

When the employee signs out at the provider, Abblix OIDC Server 3.1 revokes the tokens issued in that session (`RevokeSessionTokensOnLogout`, on by default). The assistant's refresh token stops working, so it cannot get a new access token without sending the employee to sign in again. The access token it already holds keeps working until it expires, because the tracker checks its signature and expiry itself. A tracker that has to stop access at once introspects the token (RFC 7662) instead, as a client of the provider with credentials of its own, which this sample does not register.

The sample does not show sign-out, because `ProviderApp` has no sign-out page: its job here is the sign-in an MCP client needs. Sign-out is shown in this repository's `TestClientApp`, signing in through `OpenIDProviderApp` or `OpenIDProviderApp.MinimalApi`.

## What it demonstrates

On the provider side (`ProviderApp/Program.cs`):

- The tracker is declared as a `ResourceDefinition` with its two scopes. The provider writes the resource a client names into the access token's audience and answers `invalid_target` for a resource it does not know.
- `AddClientIdMetadataDocuments()` resolves a `client_id` that is an https URL by fetching its document, and advertises `client_id_metadata_document_supported`. The feature is experimental in Abblix OIDC Server 3.1 because the draft is not an RFC yet, so the compiler reports `ABX9001` where it is used, and `Program.cs` suppresses it there.
- `AddDynamicClientRegistration()` serves the registration endpoint and advertises it in the metadata. No client is configured in advance: every client arrives one of the two ways.
- The employee approves every authorization on a consent page. Any application can become a client, so the provider cannot tell who is asking, and RFC 10017 section 6.3.3.2 says an authorization server "SHOULD NOT process authorization requests automatically without user consent or interaction, except when the authorization server can assure the identity of the client application". `ConsentPageDecisions` is the sample's `IUserConsentsProvider`: it holds every request's consent pending, so the library sends the user to the tenant's `ConsentUri`, `/Auth/Consent`. A decision answers only the request it was made on and is not remembered: the section lets an earlier approval count for a client whose redirect URIs are a fixed set of exact HTTPS URIs, and an MCP client redirects to a loopback `http` address. With nothing remembered, no approval given to one client and resource is reused for another, which draft section 10.12 asks of any approval a server keeps.
- The sign-in and consent forms carry antiforgery tokens, so another site cannot sign the employee in as somebody else or answer the consent page for them. Every page the provider renders forbids being framed (`Content-Security-Policy` and `X-Frame-Options`), so a page on another site cannot load it under a decoy button (RFC 9700 section 4.16).
- `AllowedClientIdPrefixes` is the company's list of trusted vendors. The library does not require it with a consent provider that asks the user; without it any document is fetched and the consent page is the employee's only check. A provider that grants every client everything it asks for (`AddConsentGrantedForEveryClient`) does not start without the list, nor while registration is open as it is here, because any URL or any registrant could then get the employee's tokens without the employee being asked.
- `NewClientOptions` limits both kinds of client the same way. `AllowedGrantTypes` admits only the grant types acting for a user. `AllowedResources` lets such a client get a token for the tracker and nothing else the provider protects. `OfflineAccess` set to `Allowed` makes the user's consent enough for a refresh token, so a client asking for `offline_access` without `prompt=consent` still gets one (OpenID Connect Core 1.0 section 11 says to ignore that scope otherwise).
- Open registration lets anyone create clients, so each address may send only `Registration:PermitLimit` registration requests per `Registration:Window`, refused ones included; more are answered `429`. The limit applies to the registration endpoint alone, recognized by the address the library publishes for it.

On the tracker side (`McpServerApp/Program.cs`), using the official C# MCP SDK (`ModelContextProtocol.AspNetCore`):

- The SDK's MCP authentication scheme publishes the protected resource metadata at `/.well-known/oauth-protected-resource/mcp`, the address RFC 9728 derives from the resource `https://localhost:5008/mcp`. Its challenge carries only `resource_metadata`, so `ScopeChallengeHandler` adds the `scope` parameter, which the specification recommends, and `error="invalid_token"` when the request carried a token that was refused.
- `JwtBearer` validates the token. Its `Authority` is the provider, so the signing keys and the issuer come from the provider's metadata. Its `Audience` is the tracker's own resource URI: a token issued for another resource, or for none, gets `401`. A refresh token from this provider is addressed to the issuer, so the audience check already refuses it; the type check (`ValidTypes = ["at+jwt"]`) keeps any token that is not an access token out whatever its audience. A token that carries no user gets `401` too: one whose subject is missing, or one the provider issued to a client on its own behalf, which it marks with `idp` `client_credentials`.
- Each tool names its policy with the sample's `[ToolPolicy]` attribute, and each policy is satisfied by one scope from the settings. `ToolScopeGate` answers a call that fails its tool's policy with `403` and that policy's scope, before the SDK dispatches the call. It is attached to the endpoint `MapMcp` maps, so every path that routes there passes it, and it reads the request with the SDK's own message types. A body it cannot read as one JSON-RPC message gets `400` from the gate itself, so no call reaches the SDK without passing it.
- The tools do not carry `[Authorize]`. The SDK requires its authorization filters for that attribute, and those filters leave a tool the token cannot use out of `tools/list` and refuse a call inside the JSON-RPC response, with a `200`, which a client cannot step up from. That is the C# SDK's behavior the gate works around.

On the assistant side (`McpClientApp/Program.cs`), using the C# MCP SDK client (`ModelContextProtocol.Core`):

- `ClientOAuthOptions.ClientMetadataDocumentUri` is the document's URL. The SDK sends it as the `client_id` when the provider advertises support, and registers the client otherwise. `--Registration dynamic` leaves it out, so the assistant registers even though the provider supports metadata documents.
- The app serves the vendor's document and receives the provider's redirect. The assistant's name and redirect URI are read from the document both ways, so the document and the registration cannot say different things.
- `ScopeSelector` takes the scope the tracker names. When a `Scopes` setting is given, it keeps the assistant to those scopes; when none is, it adds the `RegistrationScopes` for an assistant that registers itself.
- It asks for `tools/list` first and prints it, as an assistant does to learn which tools it can offer its model.
- It accepts a plain `http` address for the tracker only on this computer (`localhost`, `127.0.0.1`, `::1`), because the access token travels in every request.
- Every sign-in prints the scopes it asks for, so a run shows the first sign-in and the step-up. `Client:SignInTimeout` bounds each of them.
- `AuthorizationCallbackHandler` returns `code`, `state` and `iss` from the redirect, so the SDK checks both `state` and the issuer. `BrowserSignIn` prints the authorization URL and waits for the browser to come back. `HeadlessSignIn` posts the provider's sign-in form itself as the demo employee.

The applications meet in configuration. `McpServer:Resource` and `McpServer:Scopes` in the provider's `appsettings.json` must equal `Mcp:Resource` and `Mcp:Scopes` in the tracker's, character for character, because one writes them into tokens and the other compares tokens against them. The provider's `AllowedClientIdPrefixes` must cover the `client_id` in the assistant's document.

## Before you take it further

- Sign-in is the provider's own page (`/Auth/Login`), which checks one demo user in memory. A company signs its employees in there through the identity system it already runs, a federated sign-in or ASP.NET Core Identity (see `AspNetIdentitySample`), and then calls `IAuthSessionService.SignInAsync` as this page does.
- The client name on the consent page is whatever the client registered or its document claims, and the page says which. A provider that verifies some clients in another way can let those through without the page.
- Use your own client secret encryption key. A provider that serves registration keeps each `client_secret` it issues encrypted with `Provider:ClientSecretEncryptionKey`, so a client reading its registration gets its secret back, and it does not start without the key or with one shorter than 32 bytes. The key in `ProviderApp/appsettings.json` is published with the sample, so it keeps no secret from anyone who can read the stored registrations. Generate 32 random bytes, for example with `openssl rand -base64 32`, and set the key through an environment variable or a secret store. Outside the Development environment the provider refuses to start while the key comes from `appsettings.json`.
- Registered clients live in memory and are gone when the provider restarts. A client holding a `client_id` from before the restart has to register again. A client known by its document has nothing to redo: the provider fetches the document again.
- The tasks live in memory too, and every employee's list starts from the same seed tasks (`Tracker:SeedTasks`).
- Signing keys are generated at startup, so a restarted provider invalidates every token issued before. The tracker refuses tokens signed with the new key until it fetches the provider's keys again: `JwtBearer` does that by itself on meeting an unknown key, at most once per its refresh interval. Restarting the tracker along with the provider recovers at once. Until then the assistant stops with a `401` and says to restart the tracker.
- The registration limit counts by the address the request arrives from, in this process alone. Behind a reverse proxy that address is the proxy's, so every caller would share one count: let `UseForwardedHeaders` read the client's address from the proxies you name in `KnownProxies` or `KnownIPNetworks`, never from any proxy. Several instances each keep their own count, so a fleet limits at its ingress or keeps the count in a shared store.
- `NewClientOptions.AllowedGrantTypes` written out here is the library's default. A provider that adds `client_credentials` there hands tracker tokens to any program that registers.
- `AllowLoopback` is for a provider on a developer's machine. It relaxes the rule for the document's own URL alone: a URL inside the document that the provider fetches, such as `jwks_uri` or `sector_identifier_uri`, must still resolve to a public address, while a URL it only displays, such as `client_uri`, is not fetched. A provider with a public issuer fetches documents only from public addresses, whatever the setting says, and a vendor's document on its public site needs no exception at all.

## Layout

- `ProviderApp/Program.cs`: the tenant with the tracker as its resource, metadata documents limited to the allowed prefixes, open registration, the limits both kinds of client share, and the sign-in and consent endpoints.
- `ProviderApp/ConsentPageDecisions.cs`: the consent provider that holds each request's consent until the employee answers the consent page.
- `ProviderApp/TestUserStore.cs`: the in-memory store of the demo employee, whom `Program.cs` declares.
- `ProviderApp/appsettings.json`: the issuer, the client secret encryption key, the registration rate limit, the tracker's resource URI and scopes, and the allowed client identifier prefixes.
- `McpServerApp/Program.cs`: `JwtBearer` bound to the provider and to the tracker's own audience, the MCP authentication scheme with the resource metadata, the scope policies, the gate, and `MapMcp`.
- `McpServerApp/TaskTools.cs`, `McpServerApp/TaskTracker.cs`, `McpServerApp/TrackedTask.cs`: the tools, the in-memory tasks and the task record.
- `McpServerApp/TaskPolicies.cs`, `McpServerApp/ToolPolicyAttribute.cs`: the policy names and the attribute through which a tool names the policy it needs.
- `McpServerApp/ToolScopeGate.cs`: the `403` naming the scope a tool needs.
- `McpServerApp/ScopeChallengeHandler.cs`: the SDK's handler with `scope` in its challenges.
- `McpServerApp/WhoAmITool.cs`: the tool answering from the token.
- `McpServerApp/appsettings.json`: the resource URI and name, the authorization server, the scopes and the seed tasks.
- `McpClientApp/client-metadata.json`: the vendor's metadata document.
- `McpClientApp/Program.cs`: serving the document, the SDK's OAuth options both ways, and the calls the assistant makes.
- `McpClientApp/ISignIn.cs`, `McpClientApp/BrowserSignIn.cs`, `McpClientApp/HeadlessSignIn.cs`: the two ways the employee signs in and what both implement.
- `McpClientApp/appsettings.json`: the tracker's address, the document, the sign-in timeout, the way to become known, the scopes, the scopes it registers with, the sign-in mode and the headless run's consent answer.
- `*/Properties/launchSettings.json`: each application's address and the Development environment the loopback document depends on.

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
