# MCP Server Sample: a company task tracker used by an AI assistant

A company exposes its task tracker as a Model Context Protocol (MCP) server so that employees can use it from an AI assistant made by another company. The company's authorization server, built on Abblix OIDC Server, decides which assistants it accepts, and the employee decides what each may do.

## The scenario in plain words

- The company runs the tracker (`McpServerApp`) and its sign-in service, the provider (`ProviderApp`). It has no contract and no account with the assistant's vendor; it only lists the web addresses under which trusted vendors publish their assistants (`client_id` prefixes).
- The vendor publishes a client metadata document on its own site: the assistant's name, home page and redirect URI. The assistant (`McpClientApp`) uses the document's URL as its `client_id`.
- The employee asks the assistant about his tasks, signs in at the provider and allows reading on a consent page. When he asks it to add a task, the assistant asks him once more, for changing tasks too. He never types his password into the assistant, and he can allow reading alone.

Here all three run on one machine: the provider on `https://localhost:5007`, the tracker on `https://localhost:5008`, the assistant and its document on `https://localhost:5009`, its redirect on `http://127.0.0.1:5010`. In production the vendor serves `https://assistant.example.com/client-metadata.json`, and the company adds `https://assistant.example.com/` to `AllowedClientIdPrefixes`: no registration, no shared secret.

## Run it

With Docker and nothing else, see [Run it in Docker](#run-it-in-docker).

You need the .NET 11 SDK and ports 5007-5010 free. From the repository root, trust the development certificate once (on Linux, check it with `dotnet dev-certs https --check --trust`: an untrusted one shows up as `invalid_client` or a `401`):

```shell
dotnet dev-certs https --trust
```

Start each application in its own terminal, in this order:

```shell
dotnet run --project McpServerSample/ProviderApp
```

```shell
dotnet run --project McpServerSample/McpServerApp
```

```shell
dotnet run --project McpServerSample/McpClientApp
```

The assistant prints an authorization URL; open it in a browser. Its terminal shows `Served the metadata document (Accept: application/json)` before the sign-in page appears. Sign in as `john.doe@example.com` with `Jd!2024$3cur3` (declared in `ProviderApp/Program.cs`) and allow. The assistant prints the tools, `create_task` among them, and the tasks. To add a task it prints a second URL asking for `tasks.write`: open it in the same browser, and since you are still signed in, the consent page appears at once. Allow, and the assistant adds a task, closes it and lists the tasks. Each sign-in waits up to `Client:SignInTimeout`. If the browser shows an error instead of the sign-in page, the provider's log says why; stop the assistant with Ctrl+C.

Other runs:

```shell
dotnet run --project McpServerSample/McpClientApp -- --Registration dynamic
dotnet run --project McpServerSample/McpClientApp -- --Scopes:0 tasks.read
dotnet run --project McpServerSample/McpClientApp -- --SignIn headless
```

- The first registers the assistant instead of naming its document.
- The second keeps it to reading: adding a task leads to a second sign-in with the same scopes, then a `403`, and the assistant stops.
- The third signs in without a browser, answering the consent page with `HeadlessSignIn:Consent` (`allow` or `deny`), so it can run in CI. `--HeadlessSignIn:GrantedScopes:0 tasks.read` unchecks every other scope.

Another MCP client works too, pointed at `https://localhost:5008/mcp`, if it runs outside a browser (neither server sends CORS headers), trusts the development certificate, and either has a `client_id` under a prefix in `AllowedClientIdPrefixes` or registers as `application_type` `native` (this provider refuses a loopback redirect to a web client).

Without a client:

```shell
curl -k -i -X POST https://localhost:5008/mcp
curl -k https://localhost:5008/.well-known/oauth-protected-resource/mcp
curl -k https://localhost:5007/.well-known/oauth-authorization-server
```

## Run it in Docker

You need Docker and ports 5007 and 5010 free on this computer. From `McpServerSample`:

```shell
docker compose up
```

It builds one image with the three apps, starts the provider and the tracker, and then the assistant, which prints the authorization URL in the log, as in [Run it](#run-it). Open it in a browser on this computer. The browser warns once about the provider's certificate, which the image build issued and your computer does not trust; continue to the sign-in page, sign in and allow. The redirect back to `http://127.0.0.1:5010/callback` reaches the assistant's container. To add a task the assistant prints a second URL, asking for `tasks.write`: open it in the same browser, where the consent page appears at once, and allow again. When the assistant has finished, stop the provider and the tracker with Ctrl+C; `docker compose down` removes the containers.

To run the same scenario with no browser, for example in CI:

```shell
docker compose --profile headless up --exit-code-from assistant
```

The assistant signs in with `--SignIn headless`, which answers the consent page with `allow`, and its lines in the log are the ones the browser run prints: `Served the metadata document (Accept: application/json)`, the tools, `who_am_i: subject=1234567890; client_id=https://localhost:5009/client-metadata.json; scope=offline_access tasks.read`, the tasks, a second sign-in asking for `tasks.write`, `create_task`, `close_task` and the tasks again. Then the containers stop, and the command exits with the assistant's exit code, 0 when the scenario passed. `docker compose --profile headless down` removes them.

The image build makes its own root certificate and, signed by it, one certificate for `localhost`, `127.0.0.1` and `::1`, which the three apps serve. They trust that root and no other, and nothing is installed or trusted on your computer. The root's private key stays in a build stage, outside the image the apps run from; Docker caches that stage, so the root changes only when the image is built with `--no-cache`.

The tracker and the assistant join the provider's network namespace, so the three apps share one loopback interface and reach each other at the addresses they use under `dotnet run`. The issuer, the resource, the document URL and the redirect URI in the tokens and the metadata stay the same, and the provider fetches the document under `AllowLoopback`, which `ProviderApp` turns on in Development and the library applies only with an issuer on loopback. `SecureHttpFetchOptions.AllowedDestinations` could not stand in for it: the library never fetches a client identifier from a destination listed there. The provider publishes ports 5007 and 5010 on this computer's loopback alone; the run without a browser leaves them unused.

## How a client becomes known

- By its metadata document ([draft-ietf-oauth-client-id-metadata-document-03](https://datatracker.ietf.org/doc/html/draft-ietf-oauth-client-id-metadata-document-03)): the `client_id` is the document's https URL, and the provider fetches it. Only whoever controls the vendor's address can publish there.
- By registering itself (RFC 7591): the client posts its metadata and gets a generated `client_id`. Anybody can register any name, and this provider forgets registrations on restart.

The MCP specification (revision 2026-07-28) prefers the document and marks registration as deprecated, kept for older clients.

A prefix vouches for the document, not for the program presenting it: the assistant has no secret and redirects to this computer, so any local program can present its `client_id`. The consent page, with its loopback warning, is what stops that. List only prefixes whose content the vendor alone controls.

## Discovery

- A call without a token gets `401` with `WWW-Authenticate: Bearer resource_metadata="...", scope="tasks.read"`; the tracker advertises only the scope needed to start.
- The protected resource metadata (RFC 9728) names the resource, `https://localhost:5008/mcp`, and the authorization server.
- The provider's metadata (`/.well-known/oauth-authorization-server`) has `client_id_metadata_document_supported`, the `registration_endpoint` and S256 for PKCE.

Both ways then run the authorization code flow with PKCE, naming the tracker in `resource` (RFC 8707), and get an access token whose `aud` is exactly `https://localhost:5008/mcp`.

## The assistant known by its metadata document

`McpClientApp/client-metadata.json`, served at `https://localhost:5009/client-metadata.json`:

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

`token_endpoint_auth_methods_supported` is the draft's own member; `none` means PKCE and no secret.

The assistant sends the browser to the authorization endpoint with that `client_id`, `resource`, `tasks.read offline_access` and a PKCE challenge, then redeems the code without a secret.

Before showing the employee anything, the provider:

- accepts only a `client_id` under one of `ClientIdMetadataDocuments:AllowedClientIdPrefixes`; any other is refused with `invalid_client`, unfetched;
- fetches it without following redirects, within the library's size limit, and accepts only a `200` JSON answer whose `client_id` equals the URL;
- refuses a document with a secret, requires PKCE, and narrows its grants to `authorization_code` and `refresh_token`;
- accepts only a redirect URI the document lists, a loopback one on any port (draft section 4.2, RFC 8252 section 7.3);
- caches the document within the library's default durations (`ClientIdMetadataDocumentOptions`); this app sends no cache headers, so a second run soon after shows no fetch.

The consent page shows the document's `client_name`, the host it is published on, the host the code goes to, a warning that any local program could receive a code sent to `127.0.0.1`, and a checkbox per scope. Deny, or Allow with nothing checked, ends with `access_denied`. The redirect carries `iss`, which the SDK checks. `who_am_i` then answers `subject=1234567890; client_id=https://localhost:5009/client-metadata.json; scope=offline_access tasks.read`.

A refused request gets `400` and no redirect: `invalid_request` for an unlisted redirect URI, `invalid_client` for an address outside the prefixes or a document that cannot be fetched or is invalid, with the reason in the provider's log.

The sample's document is on `localhost`, which draft section 10.7 allows only for development. `ProviderApp` allows it in Development only, the library only while the issuer is on loopback, and a Production host does not start with it.

## The assistant registering itself

The assistant posts its name and redirect URI from the same document, `application_type` `native` and `client_secret_post` (both chosen by the SDK), and its scopes, with no initial access token. It gets a `client_id` and a `client_secret`. A registered client cannot widen its scopes by step-up, so it registers and asks for `tasks.read tasks.write` at once (`RegistrationScopes`); the consent checkbox is then where the employee keeps writing out.

The provider admits it because `RequireInitialAccessToken = false`, limits it to `authorization_code` and `refresh_token` (`client_credentials` is refused with `invalid_client_metadata`), refuses a loopback redirect to a web client, and gives a refresh token only to a client that registered the `refresh_token` grant and `offline_access`. The consent page says the client registered itself, so nothing ties its name to the vendor.

## Reading and changing tasks

`tasks.read` covers `list_my_tasks` and `who_am_i`, `tasks.write` covers `create_task` and `close_task`, and every tool works on the tasks of the token's subject. The tracker lists every tool to every token, so the assistant calls `create_task` while it can only read and gets `403` with `error="insufficient_scope", scope="tasks.write"`. It then asks the employee again with `tasks.write` added (the MCP step-up flow) and repeats the call. If the new token still lacks the scope, it stops at the `403`.

## Signing out

Signing out at the provider revokes that session's refresh tokens (`RevokeSessionTokensOnLogout`), so the assistant cannot renew its access. The access token it holds works until it expires, because the tracker checks it itself; stopping it at once needs introspection (RFC 7662) by a tracker registered with credentials, which this sample does not do. `ProviderApp` has no sign-out page; `TestClientApp` with `OpenIDProviderApp` shows one.

## What it demonstrates

Provider (`ProviderApp/Program.cs`):

- The tracker is a `ResourceDefinition` with two scopes; its URI becomes the token audience.
- `AddClientIdMetadataDocuments()` resolves URL client identifiers (experimental, `ABX9001`) and `AddDynamicClientRegistration()` serves registration; no client is configured in advance.
- `ConsentPageDecisions` (`IUserConsentsProvider`) keeps every consent pending, so the user always lands on `/Auth/Consent`, as RFC 10017 section 6.3.3.2 recommends when the client's identity is unknown. No decision is remembered, so none is reused for another client or resource (draft section 10.12).
- Sign-in and consent forms carry antiforgery tokens, and every page refuses framing (RFC 9700 section 4.16).
- Without `AllowedClientIdPrefixes` any document is fetched and consent is the only check; `AddConsentGrantedForEveryClient` refuses to start without the list or with registration open.
- `NewClientOptions` gives both kinds of client only user grants and only the tracker as a resource; `OfflineAccess = Allowed` lets consent alone yield a refresh token.
- Registration is rate-limited per address (`Registration:PermitLimit` per `Registration:Window`, then `429`).

Tracker (`McpServerApp/Program.cs`, official C# MCP SDK `ModelContextProtocol.AspNetCore`):

- The SDK publishes the resource metadata at `/.well-known/oauth-protected-resource/mcp`; `ScopeChallengeHandler` adds `scope` and `error="invalid_token"` to its challenge.
- `JwtBearer` checks issuer and keys through `Authority`, the audience through `Audience`, and `ValidTypes = ["at+jwt"]`; tokens without a subject or marked `idp` `client_credentials` get `401`. The provider's refresh tokens are addressed to the issuer, so the audience check refuses them anyway.
- Tools name their policy with the sample's `[ToolPolicy]`. `ToolScopeGate`, in front of the `MapMcp` endpoint, answers a call lacking the scope with `403` before the SDK dispatches it, and an unreadable body with `400`. The tools avoid `[Authorize]` because the SDK's filters for it hide tools from `tools/list` and refuse inside a `200` JSON-RPC response, which a client cannot step up from.

Assistant (`McpClientApp/Program.cs`, C# MCP SDK client):

- `ClientMetadataDocumentUri` makes the SDK use the document when the provider supports it and register otherwise; `--Registration dynamic` forces registration.
- `ScopeSelector` keeps to `Scopes` when set, and otherwise adds `RegistrationScopes` when registering.
- It lists tools first, prints the scopes of each sign-in, bounds each with `Client:SignInTimeout`, returns `iss` with the code, and accepts plain `http` for the tracker only on this computer.

`McpServer:Resource` and `McpServer:Scopes` in the provider must equal `Mcp:Resource` and `Mcp:Scopes` in the tracker character for character, and `AllowedClientIdPrefixes` must cover the document's `client_id`.

## Before you take it further

- `/Auth/Login` checks one demo user. Sign employees in through your own identity system (federation, or ASP.NET Core Identity as in `AspNetIdentitySample`) and call `IAuthSessionService.SignInAsync` as this page does.
- Replace `Provider:ClientSecretEncryptionKey` (32 random bytes, for example `openssl rand -base64 32`) and supply it from an environment variable or secret store; outside Development the provider refuses a key from `appsettings.json`.
- Registered clients, tasks and signing keys live in memory. After a provider restart, registered clients register again and the tracker accepts new tokens once `JwtBearer` refetches the keys (or the tracker restarts).
- The registration limit counts per process and per remote address. Behind a proxy, use `UseForwardedHeaders` with `KnownProxies` or `KnownIPNetworks`; across instances, limit at the ingress or in a shared store.
- The written-out `AllowedGrantTypes` is the library default; adding `client_credentials` there hands tracker tokens to any program that registers.
- `AllowLoopback` covers only the document's own URL on a developer's machine; URLs the provider fetches from inside a document stay public-only.

## Layout

- `ProviderApp/`
    - `Program.cs`: the tenant, metadata documents, registration, client limits, sign-in and consent.
    - `ConsentPageDecisions.cs`: the consent provider behind the consent page.
    - `TestUserStore.cs`: the store of the demo employee.
    - `appsettings.json`: issuer, secret encryption key, rate limit, the tracker's URI and scopes, allowed prefixes.
- `McpServerApp/`
    - `Program.cs`: `JwtBearer`, the MCP authentication scheme, scope policies, the gate, `MapMcp`.
    - `TaskTools.cs`, `TaskTracker.cs`, `TrackedTask.cs`: tools and in-memory tasks.
    - `TaskPolicies.cs`, `ToolPolicyAttribute.cs`, `ToolScopeGate.cs`: policies and the `403` gate.
    - `ScopeChallengeHandler.cs`, `WhoAmITool.cs`: the challenge with `scope`, and the token echo tool.
    - `appsettings.json`: resource URI, authorization server, scopes, seed tasks.
- `McpClientApp/`
    - `client-metadata.json`: the vendor's document.
    - `Program.cs`, `ISignIn.cs`, `BrowserSignIn.cs`, `HeadlessSignIn.cs`: the assistant and its two ways to sign in.
    - `appsettings.json`: tracker address, document, timeout, scopes, sign-in mode.
- `Dockerfile`, `compose.yaml`, `.env`: the image, the services and the default profile of [Run it in Docker](#run-it-in-docker).
- `Properties/launchSettings.json` in each project: its address and the Development environment.

## The specifications behind this sample

- MCP authorization, revision 2026-07-28: https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization
- OAuth Client ID Metadata Document, draft 03: https://datatracker.ietf.org/doc/html/draft-ietf-oauth-client-id-metadata-document-03
- RFC 9728, Protected Resource Metadata: https://www.rfc-editor.org/rfc/rfc9728
- RFC 8707, Resource Indicators: https://www.rfc-editor.org/rfc/rfc8707
- RFC 7591, Dynamic Client Registration: https://www.rfc-editor.org/rfc/rfc7591
- RFC 8252, OAuth 2.0 for Native Apps: https://www.rfc-editor.org/rfc/rfc8252
- RFC 9207, Issuer Identification: https://www.rfc-editor.org/rfc/rfc9207
- RFC 10017, OAuth 2.0 for Browser-Based Applications: https://www.rfc-editor.org/rfc/rfc10017
- RFC 9700, OAuth 2.0 Security Best Current Practice: https://www.rfc-editor.org/rfc/rfc9700
