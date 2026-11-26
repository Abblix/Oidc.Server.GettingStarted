# Test Client: a relying party that signs in against the provider

TestClientApp is the Relying Party in the Getting Started constellation: a plain ASP.NET Core MVC app that delegates authentication to the provider (OpenIDProviderApp or AspNetIdentitySample) over OpenID Connect. It uses the standard `Microsoft.AspNetCore.Authentication.OpenIdConnect` handler, configured entirely from `appsettings.json`, so it is the smallest honest example of the client side of a login: the authorization code flow with PKCE, plus the back-channel logout receiver from Abblix.SecurityEvents, since the handler has none.

## What it demonstrates

- Wiring a .NET app as an OpenID Connect client with a cookie session and the `OpenIdConnect` handler, bound from configuration rather than code.
- The full round trip: an unauthenticated request is challenged, redirected to the provider to sign in, and returns to the app with an ID token and the user's claims.
- Reading the signed-in user's claims in an MVC view.
- Calling a protected API with the access token from the sign-in: the client asks for the `weather` scope of ApiSample (the `Resource` setting), so the token's audience is that API. A token minted for the API is refused by the provider's userinfo endpoint, so the profile and email claims come from the ID token instead. With `SaveTokens` on, that ID token, profile and email included, is sent back to the provider as `id_token_hint` in the sign-out address.

- Back-channel logout (OpenID Connect Back-Channel Logout 1.0), which the `OpenIdConnect` handler does not implement. When a provider ends a session, it POSTs a logout token to `https://localhost:5002/signout-backchannel`. The receiver is Abblix's (`Abblix.SecurityEvents.MinimalAPI`): `AddBackChannelLogoutReceiver` validates the token as section 2.6 asks, against the keys the provider's discovery document points to, refuses a token it has seen before, and answers `200` or `400`, or `500` when the provider's keys cannot be fetched; `MapBackChannelLogoutEndpoint` maps the address. The client's own part is `RevokedSessions`, the sink that records the ended `sid`, and the cookie check that refuses a sign-in whose ID token carried that `sid` on its next request. At startup the receiver logs two warnings that its profile drops two Security Event Token defaults (a pinned `typ`, a forbidden `exp`); both are what a logout token needs, and the warnings say so.

It registers with the provider as `test_client`, redirect URI `https://localhost:5002/signin-oidc`. AspNetIdentitySample also registers `https://localhost:5002/signout-backchannel` as its back-channel logout URI and asks for the `sid`; OpenIDProviderApp and OpenIDProviderApp.MinimalApi register none, so they never call it and the client signs in and out against them as before. The section Watching a session end in the AspNetIdentitySample README shows a logout token arrive.

## Running it

TestClientApp needs a provider running next to it. From the `Oidc.Server.GettingStarted` root:

- `dotnet run --project OpenIDProviderApp` (or `AspNetIdentitySample`) starts the provider on `https://localhost:5001`.
- `dotnet run --project TestClientApp` starts the client on `https://localhost:5002`.

- `dotnet run --project ApiSample` starts the API on `https://localhost:5004`, which Call the API needs.

`dotnet run-loop.cs` from the root starts OpenIDProviderApp, ApiSample and this client together.

Open `https://localhost:5002`: the home page requires authentication, so it sends you to the provider to sign in. With OpenIDProviderApp the demo user is `john.doe@example.com` / `Jd!2024$3cur3`; with AspNetIdentitySample, register a new account first. An `oidc.db` that AspNetIdentitySample created before this client took its claims from the ID token still holds the older `test_client`, which leaves the profile and email claims out: delete `oidc.db*` once to seed it again. Node.js is required: the client bundles Bootstrap with Vite.

## Layout

- `Program.cs`: the cookie + OpenID Connect handler, bound from `appsettings.json`, the back-channel logout receiver and its endpoint, and the cookie check that refuses a revoked session.
- `appsettings.json` and `appsettings.Development.json`: the client id, secret, authority, the API as the token's resource, and scopes.
- `BackChannelLogout/RevokedSessions.cs`: where a validated logout token lands. Revoked sessions are kept in memory, which holds for one instance until a restart.
- `Controllers/HomeController.cs` and `Views/Home/Index.cshtml`: the protected page that shows the user's claims, and the Weather action that calls the API with the access token.

## The guide behind this sample

- Getting Started with Abblix OIDC Server, which builds this client alongside the provider: https://docs.abblix.com/docs/getting-started-guide
