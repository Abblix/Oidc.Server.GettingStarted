# Test Client: a relying party that signs in against the provider

TestClientApp is the Relying Party in the Getting Started constellation: a plain ASP.NET Core MVC app that delegates authentication to the provider (OpenIDProviderApp or AspNetIdentitySample) over OpenID Connect. It uses the standard `Microsoft.AspNetCore.Authentication.OpenIdConnect` handler, configured entirely from `appsettings.json`, so it is the smallest honest example of the client side of a login: no library code of its own, just the authorization code flow with PKCE.

## What it demonstrates

- Wiring a .NET app as an OpenID Connect client with a cookie session and the `OpenIdConnect` handler, bound from configuration rather than code.
- The full round trip: an unauthenticated request is challenged, redirected to the provider to sign in, and returns to the app with an ID token and the user's claims.
- Reading the signed-in user's claims in an MVC view.
- Calling a protected API with the access token from the sign-in: the client asks for the `weather` scope of ApiSample (the `Resource` setting), so the token's audience is that API. A token minted for the API is refused by the provider's userinfo endpoint, so the profile and email claims come from the ID token instead.

It registers with the provider as `test_client`, redirect URI `https://localhost:5002/signin-oidc`.

## Running it

TestClientApp needs a provider running next to it. From the `Oidc.Server.GettingStarted` root:

- `dotnet run --project OpenIDProviderApp` (or `AspNetIdentitySample`) starts the provider on `https://localhost:5001`.
- `dotnet run --project TestClientApp` starts the client on `https://localhost:5002`.

- `dotnet run --project ApiSample` starts the API on `https://localhost:5004`, which Call the API needs.

`dotnet run-loop.cs` from the root starts OpenIDProviderApp, ApiSample and this client together.

Open `https://localhost:5002`: the home page requires authentication, so it sends you to the provider to sign in. With OpenIDProviderApp the demo user is `john.doe@example.com` / `Jd!2024$3cur3`; with AspNetIdentitySample, register a new account first. Node.js is required: the client bundles Bootstrap with Vite.

## Layout

- `Program.cs`: the cookie + OpenID Connect handler, bound from `appsettings.json`.
- `appsettings.json` and `appsettings.Development.json`: the client id, secret, authority, the API as the token's resource, and scopes.
- `Controllers/HomeController.cs` and `Views/Home/Index.cshtml`: the protected page that shows the user's claims, and the Weather action that calls the API with the access token.

## The guide behind this sample

- Getting Started with Abblix OIDC Server, which builds this client alongside the provider: https://docs.abblix.com/docs/getting-started-guide
