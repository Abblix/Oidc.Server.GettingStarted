# MCP Server Sample: an authorized MCP server with its own provider

`McpServerSample` is two applications that are configured for each other. `McpServerApp` is a Model Context Protocol server with one tool, and it accepts only access tokens issued for it. `ProviderApp` is the authorization server, built on Abblix OIDC Server. An MCP client that has never seen either of them finds the provider through the MCP server, registers itself, signs the user in and calls the tool.

The flow follows the authorization part of the MCP specification (revision 2026-07-28):

- The client calls the MCP endpoint without a token and gets `401` with `WWW-Authenticate: Bearer resource_metadata="...", scope="mcp:tools"`.
- It reads the protected resource metadata (RFC 9728) at that address. The document names the resource, `https://localhost:5008/mcp`, and the authorization server, `https://localhost:5007`.
- It reads the provider's metadata at `/.well-known/oauth-authorization-server` (RFC 8414). There it finds the registration endpoint and checks that PKCE is supported.
- It registers itself with no initial access token (RFC 7591).
- It runs the authorization code flow with PKCE (S256) and names the MCP server in the `resource` parameter (RFC 8707) of both the authorization request and the token request.
- It calls the tool with the access token. The token's `aud` is exactly `https://localhost:5008/mcp`, and the MCP server refuses any token whose audience is something else, and any token that is not an access token (`typ` `at+jwt`).

## What it demonstrates

On the provider side (`ProviderApp/Program.cs`):

- The MCP server is declared as a `ResourceDefinition` with its `mcp:tools` scope. The provider writes the resource a client names into the access token's audience and answers `invalid_target` for a resource it does not know.
- Open dynamic client registration. `AddDynamicClientRegistration()` serves the registration endpoint and advertises it in the metadata. `RequireInitialAccessToken = false` lets a client register without a token handed out in advance, which is what an MCP client meeting this provider for the first time needs. The library's default asks for one.
- No pre-registered clients at all: every client in this sample arrives through registration.
- Only clients acting for a user are admitted. Open registration accepts any grant type the server supports, so a `client_credentials` client could register itself and get a token for the MCP server with no user behind it. `UserGrantsOnlyValidator`, added to the end of the library's registration pipeline, refuses every grant type other than `authorization_code` and `refresh_token` with `invalid_client_metadata`.

On the MCP server side (`McpServerApp/Program.cs`), using the official C# MCP SDK (`ModelContextProtocol.AspNetCore`):

- The SDK's MCP authentication scheme publishes the protected resource metadata at `/.well-known/oauth-protected-resource/mcp`, the address RFC 9728 derives from the resource `https://localhost:5008/mcp`.
- `JwtBearer` validates the token. Its `Authority` is the provider, so the signing keys and the issuer come from the provider's metadata. Its `Audience` is the MCP server's own resource URI. A token issued for another resource, or for none, gets `401`, and so does a token that carries no user: one whose subject is missing or is the client itself, as a `client_credentials` token's is.
- `ScopeChallengeHandler` adds the `scope` parameter to the SDK's challenge, which the specification recommends, and `error="invalid_token"` when the request carried a token that was refused. It answers a token without `mcp:tools` with `403` and `error="insufficient_scope"`.
- The `who_am_i` tool reads the user from the token, so a successful call shows that the user who signed in at the provider is the user the tool sees.

The two halves meet in configuration. `McpServer:Resource` in the provider's `appsettings.json` and `Mcp:Resource` in the MCP server's must be the same URI, because one writes it into tokens and the other compares tokens against it.

## Run it

From the `Oidc.Server.GettingStarted` root, trust the development certificate once, then start both applications:

```shell
dotnet dev-certs https --trust
dotnet run --project McpServerSample/ProviderApp
dotnet run --project McpServerSample/McpServerApp
```

The provider listens on `https://localhost:5007` and the MCP server on `https://localhost:5008`. Point a desktop or command-line MCP client that supports authorization at `https://localhost:5008/mcp`. It opens the provider's login page in a browser. It has to register with `application_type` `native`, as the MCP specification requires of it: a client registering as a web application is refused a `localhost` redirect. A client running inside a web page is not served: the registration endpoint and the MCP server answer no cross-origin requests. The demo user is `john.doe@example.com` with password `Jd!2024$3cur3`, in memory and visible in `ProviderApp/Program.cs`.

A quick look without a client:

```shell
curl -k -i -X POST https://localhost:5008/mcp
curl -k https://localhost:5008/.well-known/oauth-protected-resource/mcp
curl -k https://localhost:5007/.well-known/oauth-authorization-server
```

The first returns `401` with the challenge, the second the resource metadata, and the third the provider's metadata with `registration_endpoint` and `code_challenge_methods_supported`.

## Before you take it further

- Consent is granted automatically (`AddConsentGrantedForEveryClient`). With open registration any application can become a client, so a real provider shows the user which client is asking and where it will send the code, through an `IUserConsentsProvider` of its own.
- Registered clients live in memory and are gone when the provider restarts. A client holding a `client_id` from before the restart has to register again.
- Signing keys are generated at startup, so a restarted provider invalidates every token issued before. The MCP server refuses tokens signed with the new key until it fetches the provider's keys again: `JwtBearer` does that by itself on meeting an unknown key, at most once per its refresh interval, a few minutes by default. Restarting the MCP server along with the provider recovers at once.
- Open registration admits machine clients unless the host narrows the grant types, as `UserGrantsOnlyValidator` does here. A provider that drops it hands MCP server tokens to any program that asks.

## Layout

- `ProviderApp/Program.cs`: the tenant with the MCP server as its resource, open registration, and the login endpoints.
- `ProviderApp/UserGrantsOnlyValidator.cs`: the registration step that admits only grant types acting for a user.
- `ProviderApp/appsettings.json`: the issuer, and the MCP server's resource URI and scope.
- `McpServerApp/Program.cs`: `JwtBearer` bound to the provider and to the MCP server's own audience and refusing tokens with no user, the MCP authentication scheme with the resource metadata, the scope policy, and `MapMcp`.
- `McpServerApp/ScopeChallengeHandler.cs`: the SDK's handler with `scope` in its challenges.
- `McpServerApp/WhoAmITool.cs`: the tool.
- `McpServerApp/appsettings.json`: the resource URI, the authorization server and the scope.

## The specifications behind this sample

- MCP authorization, revision 2026-07-28: https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization
- OAuth 2.0 Protected Resource Metadata (RFC 9728): https://www.rfc-editor.org/rfc/rfc9728
- Resource Indicators for OAuth 2.0 (RFC 8707): https://www.rfc-editor.org/rfc/rfc8707
- OAuth 2.0 Dynamic Client Registration (RFC 7591): https://www.rfc-editor.org/rfc/rfc7591
