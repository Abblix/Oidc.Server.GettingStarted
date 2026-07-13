# OpenID Provider: the identity server from the Getting Started guide

OpenIDProviderApp is the OpenID Connect provider built step by step in the Getting Started guide. It authenticates users, manages their sessions, and issues ID, access, and refresh tokens to registered clients, using Abblix OIDC Server for the whole protocol surface. Everything it needs is held in memory, so it starts with no database and no external setup: read it end to end, then graduate to AspNetIdentitySample when you want the same provider backed by a real user store.

## What it demonstrates

- Registering Abblix OIDC Server in an ASP.NET Core MVC app with `AddOidcServices`, which brings up the discovery, authorization, token, userinfo, and JWKS endpoints.
- A minimal user store (`TestUserStorage`) exposed to the library through `IUserInfoProvider`, turning a subject identifier into claims.
- A login controller and a Bootstrap view under `/Auth/Login` that verify the password and hand the session to the library.
- In-code client registrations for the sibling samples: `test_client` (TestClientApp), `bff_sample` (BffSample), and `blazor_sample` (BlazorSample), each using the authorization code flow with PKCE.
- A `weather` resource definition that scopes the access tokens ApiSample validates.

The shortcuts are deliberate: users, clients, and the RSA signing key all live in memory, so the signing key is regenerated on every restart and all state resets when the process stops. AspNetIdentitySample replaces each of these with a durable equivalent.

## Running it

From the `Oidc.Server.GettingStarted` root:

- `dotnet run --project OpenIDProviderApp` starts the provider on `https://localhost:5001`.
- Open `https://localhost:5001/.well-known/openid-configuration` to see the discovery document, with the authorization, token, userinfo, and JWKS endpoints live.

A provider is only half of a flow, so start a client next to it to sign in. TestClientApp (`https://localhost:5002`) is the simplest. The demo user is `john.doe@example.com` with password `Jd!2024$3cur3` (a demo credential, visible in `Program.cs`).

Node.js is required: the first build bundles Bootstrap for the login page with Vite.

## Layout

- `Program.cs`: `AddOidcServices`, the in-memory user, the client registrations, and the `weather` resource.
- `TestUserStorage.cs`: the in-memory user list, doubling as the `IUserInfoProvider`.
- `Controllers/AuthController.cs` and `Views/Auth/Login.cshtml`: the login flow.
- `ClientAssets/`: the Vite project that bundles Bootstrap into `wwwroot`.

## The guides behind this sample

- Getting Started with Abblix OIDC Server: https://docs.abblix.com/docs/getting-started-guide
- Configuration guide: https://docs.abblix.com/docs/configuration-and-setup
- Moving toward production: https://docs.abblix.com/docs/production-hardening-checklist, and the AspNetIdentitySample project next door, which turns this provider into a database-backed one.
