# Blazor Client: OpenID Connect from a Blazor Web App

BlazorSample is a Blazor Web App (interactive Server render mode) that signs in against the provider over OpenID Connect. It exists to show the one pattern that trips people up when Blazor meets OIDC: an interactive circuit cannot write an authentication cookie or issue a redirect, so sign-in and sign-out run on plain HTTP endpoints outside the circuit, while the rest of the UI stays interactive.

## What it demonstrates

- A Blazor Server app as an OpenID Connect client using `Microsoft.AspNetCore.Authentication.OpenIdConnect` with a cookie session, the authorization code flow, and PKCE, bound from `appsettings.json` (the same shape as TestClientApp).
- Sign-in and sign-out as minimal HTTP endpoints (`/auth/login`, `/auth/logout`) that `Challenge` and `SignOut`, because writing the auth cookie and issuing the OIDC redirect both need the HTTP response, which has already started once a component is interactive.
- Exposing the signed-in user to components through `CascadingAuthenticationState` and `AuthorizeView`.
- Pointing `Identity.Name` at the `name` claim (inbound claim mapping is off, so claims keep their short names).

It registers with the provider as `blazor_sample`, redirect URI `https://localhost:5005/signin-oidc`.

## Running it

From the `Oidc.Server.GettingStarted` root:

- `dotnet run --project OpenIDProviderApp` starts the provider on `https://localhost:5001`.
- `dotnet run --project BlazorSample` starts the Blazor app on `https://localhost:5005`.

Open `https://localhost:5005` and sign in (demo user `john.doe@example.com` / `Jd!2024$3cur3`). No Node.js is needed: this sample has no client-side bundling step.

## Layout

- `Program.cs`: the OIDC client wiring and the `/auth/login` and `/auth/logout` endpoints.
- `Components/Pages/Home.razor`: the page that reads the authenticated user.
- `Components/App.razor`, `Components/Routes.razor`, `Components/Layout/MainLayout.razor`: the Blazor shell.

## The guides behind this sample

- Getting Started with Abblix OIDC Server, the provider this client talks to: https://docs.abblix.com/docs/getting-started-guide
- Practical implementation of modern authentication on .NET (OpenID Connect, BFF and SPA): https://docs.abblix.com/docs/practical-implementation-of-modern-authentication-on-the-net-openid-connect-bff-and-spa
- For Blazor-specific authentication details, see Microsoft's Blazor security documentation: https://learn.microsoft.com/aspnet/core/blazor/security
