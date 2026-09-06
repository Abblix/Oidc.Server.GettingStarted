# OpenID Provider on Minimal API: the same server without MVC

`OpenIDProviderApp.MinimalApi` is `OpenIDProviderApp` hosted through the `Abblix.Oidc.Server.MinimalApi` adapter instead of MVC. The protocol core is the same library; what differs is the transport it is mapped onto and the login screen, which is a pair of endpoints rather than a controller and a Razor view. Read it next to its MVC twin to see what a host owes the library and what the library owes the host.

## What it demonstrates

- Registering the server with `AddOidcServices`, whose delegate binds the `Oidc` configuration section, and mapping the protocol endpoints with `app.MapOidcEndpoints()`.
- A login screen built from `MapGet("/Auth/Login")` and `MapPost`, resuming the paused authorization request through the `request_uri` the library hands it.
- The same three interactive clients and the same `weather` resource as the MVC provider, so a client is moved between the two by pointing it at a different address. That is the whole move for signing users in; one that also calls ApiSample needs the change described under Running it.
- A `client_credentials` client for headless checks, which the MVC provider does not carry.

## Running it

From the `Oidc.Server.GettingStarted` root:

- `dotnet run --project OpenIDProviderApp.MinimalApi` starts the provider on `https://localhost:5006`.
- Open `https://localhost:5006/.well-known/openid-configuration` to see the discovery document.

To point a client here instead of at the MVC provider, set its `Authority` to `https://localhost:5006`. That is enough for a client that only signs users in. A client that then calls `ApiSample` needs one more change: the API pins `ValidIssuer` and `Authority` to `https://localhost:5001` in its own `appsettings.Development.json`, so a token minted by this provider is rejected until those move too.

The demo user is `john.doe@example.com` with password `Jd!2024$3cur3`, in memory and visible in `Program.cs`.

## Layout

- `Program.cs`: `AddOidcServices` with the configuration binding, the signing key, the `weather` resource, `MapOidcEndpoints()`, and the login endpoints.
- `appsettings.json`: the client registrations, in the `Oidc` section.
- `TestUserStore.cs`: the in-memory user list, doubling as the `IUserInfoProvider`.

## How far the twin goes

Both providers are built on the same release of the library, so their discovery documents agree on every capability - the same algorithms, grants, response modes and scopes - and they carry the same resource and the same three interactive clients; this one adds `console_client`. What differs is the address, because the two run on different ports: the issuer and the six endpoint URLs built on it. Read the two documents side by side if you ever doubt the rest - a difference outside those seven fields means either the projects have drifted onto different package versions, or their `Oidc` sections have, since some of those fields are settings rather than build-time facts. They are not the same host: it has no HTTPS redirection, no static files and no home page, because none of that is what it exists to show, and `GET /` answers 404.

One setting is worth knowing because it reaches other projects. This provider pins `Issuer`, while the MVC one derives it from the request, so a token minted here names `https://localhost:5006` whatever address the request arrived on - and ApiSample, pinned to the other address, rejects it until its own settings move. The pin is not what makes the two discovery documents differ: remove it and this provider still says 5006, because that is the port it listens on. What the pin changes is that the answer stops following the request, which is what a host behind a proxy needs.

## The guides behind this sample

- Getting Started with Abblix OIDC Server: https://docs.abblix.com/docs/getting-started-guide
- Configuration guide: https://docs.abblix.com/docs/configuration-and-setup
