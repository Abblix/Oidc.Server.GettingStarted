# BFF Sample: a React SPA secured by a Backend-for-Frontend

A single-page React application that authenticates against Abblix OIDC Server without ever holding a token in the browser. A thin ASP.NET Core backend is the actual OpenID Connect client: it runs the authorization-code flow, keeps the resulting tokens in an encrypted, HttpOnly session cookie, and reverse-proxies the SPA's API calls with the access token attached server-side. This is the Backend-for-Frontend (BFF) pattern.

## Why BFF

A browser is a hostile place to keep a token. Anything that can run script on the page (an XSS bug, a compromised dependency) can read a token held in `localStorage` or JavaScript memory and exfiltrate it. The BFF pattern removes tokens from the browser entirely: they live only on the backend, behind an HttpOnly cookie the SPA cannot read. The SPA talks to its own backend over that cookie, and the backend talks to the OpenID Connect provider and to protected APIs.

## What the backend exposes

The SPA never calls the provider or the API directly. It calls its own origin, under `/bff`:

- `GET /bff/check_session`: returns the current user's claims, or `401` when there is no session. The SPA calls this on load to decide whether to render or to redirect to login.
- `GET /bff/login`: starts the authorization-code flow (`Challenge`), then returns to the SPA.
- `POST /bff/logout`: clears the session cookie.
- `GET /bff/{**catch-all}`: reverse-proxies to the protected API (`/bff/weatherforecast` reaches the weather API), attaching the access token as a `Bearer` header. The token is read from the session, so the browser never sees it.

The forwarding is done with [YARP](https://microsoft.github.io/reverse-proxy/); the SPA dev server is launched and proxied in development by `Microsoft.AspNetCore.SpaProxy`.

## Running it

The sample is one client in a small constellation. Start these projects (each in its own terminal, from the `Oidc.Server.GettingStarted` root):

- `dotnet run --project OpenIDProviderApp` : the OpenID Connect provider on `https://localhost:5001`.
- `dotnet run --project ApiSample` : the protected weather API on `https://localhost:5004`.
- `dotnet run --project BffSample` : the BFF backend on `https://localhost:5003`. It launches the Vite dev server automatically.

Then open `https://localhost:5003`. You are redirected to the provider to sign in (the seeded demo user is `john.doe@example.com`), and land back on the SPA showing your claims and the weather forecast pulled through the BFF.

Node.js is required to build and run the SPA; the backend restores npm packages on first build.

## Layout

- `Program.cs`: cookie + OpenID Connect authentication, and the YARP forwarder that attaches the access token.
- `Controllers/BffController.cs`: the `check_session`, `login`, and `logout` endpoints.
- `ClientApp/`: the React + TypeScript SPA built with Vite. `src/components/Bff.tsx` holds the session-aware fetch wrapper the rest of the UI uses.

## The guides behind this sample

- Securing a React SPA with the BFF pattern and Abblix OIDC Server: https://docs.abblix.com/docs/react-spa-bff-guide
- Practical implementation of modern authentication on .NET (OpenID Connect, BFF and SPA): https://docs.abblix.com/docs/practical-implementation-of-modern-authentication-on-the-net-openid-connect-bff-and-spa
