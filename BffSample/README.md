# BFF Sample: a React SPA secured by a Backend-for-Frontend

A single-page React application that authenticates against Abblix OIDC Server without ever holding a token in the browser. A thin ASP.NET Core backend is the actual OpenID Connect client: it runs the authorization-code flow, keeps the resulting tokens in an encrypted, HttpOnly session cookie, and reverse-proxies the SPA's API calls with the access token attached server-side. This is the Backend-for-Frontend (BFF) pattern.

## Why BFF

A browser is a hostile place to keep a token. Anything that can run script on the page (an XSS bug, a compromised dependency) can read a token held in `localStorage` or JavaScript memory and exfiltrate it. The BFF pattern removes tokens from the browser entirely: they live only on the backend, behind an HttpOnly cookie the SPA cannot read. The SPA talks to its own backend over that cookie, and the backend talks to the OpenID Connect provider and to protected APIs.

## What the backend exposes

The SPA never calls the provider or the API directly. It calls its own origin, under `/bff`:

- `GET /bff/check_session`: returns the current user's claims, or `401` when there is no session. The SPA calls this on load to decide whether to render or to redirect to login.
- `GET /bff/login`: starts the authorization-code flow (`Challenge`), then returns to the SPA. The SPA navigates here, because the flow is a top-level redirect to the provider.
- `POST /bff/logout`: signs out of the BFF, clearing the session cookie, and answers with the provider's end-session address. The SPA then navigates there, which ends the provider's session and comes back to the SPA. A fetch cannot follow that redirect itself, so the BFF hands over the address instead of redirecting.
- `GET /bff/weatherforecast`: forwarded to the weather API's `/weatherforecast` with the access token attached as a `Bearer` header. The token is read from the session, so the browser never sees it.

Nothing else under `/bff` is forwarded. Every address the SPA may reach through the BFF is named in `Program.cs` together with its method, and any other path answers `404`. A path forwarded as it came in would carry the user's access token to whatever else the API serves, its Swagger UI included. RFC 10017 section 6.1.3.6 requires the BFF to forward only to destinations it allows.

Every request under `/bff` except the login navigation carries the header `X-CSRF: 1`, and the BFF refuses a request without it with `400`. This is the CSRF defence of RFC 10017 section 6.1.3.3.2. A page on another origin cannot add a custom header without the BFF approving it in a CORS preflight, and this BFF has no CORS policy, so it approves none. The `SameSite=Strict` session cookie does not stop such a page on its own when it runs on the same site as the BFF: here every sample shares the site `localhost`, and in production that could be another subdomain.

The SPA reaches `/bff` on its own origin. In development the Vite dev server forwards `/bff` to the BFF, and once published the BFF serves the SPA itself. The browser therefore never sends a cross-origin request to the BFF.

The session cookie is named `__Host-Http-Bff`. It is `Secure`, `HttpOnly` and `SameSite=Strict`, with `Path=/` and no `Domain`, as RFC 10017 section 6.1.3.2 asks. The published SPA is served with a Content Security Policy that lets script, style and connections come only from its own origin.

The forwarding is done with [YARP](https://microsoft.github.io/reverse-proxy/); the SPA dev server is launched and proxied in development by `Microsoft.AspNetCore.SpaProxy`.

## Running it

The sample is one client in a small constellation. Start these projects (each in its own terminal, from the `Oidc.Server.GettingStarted` root):

- `dotnet run --project OpenIDProviderApp` : the OpenID Connect provider on `https://localhost:5001`. `AspNetIdentitySample` works here too, on the same port.
- `dotnet run --project ApiSample` : the protected weather API on `https://localhost:5004`.
- `dotnet run --project BffSample` : the BFF backend on `https://localhost:5003`. It launches the Vite dev server automatically.

Then open `https://localhost:5003`. You are redirected to the provider to sign in (with OpenIDProviderApp the demo user is `john.doe@example.com` / `Jd!2024$3cur3`; with AspNetIdentitySample, create an account first with Create one on its sign-in screen), and land back on the SPA showing your claims and the weather forecast pulled through the BFF. In development the SPA is served by the Vite dev server, so the address you land on is `https://localhost:3000`.

Node.js is required to build and run the SPA; the backend restores npm packages on first build.

## Layout

- `Program.cs`: cookie + OpenID Connect authentication, the CSRF header check, the Content Security Policy, and the YARP forwarder that attaches the access token.
- `CsrfHeader.cs`: the header the SPA sends and the BFF checks for.
- `Controllers/BffController.cs`: the `check_session`, `login`, and `logout` endpoints.
- `ClientApp/`: the React + TypeScript SPA built with Vite. `src/components/Bff.tsx` holds the session-aware fetch wrapper the rest of the UI uses, which adds the CSRF header. `vite.config.ts` forwards `/bff` to the BFF in development.

## The guides behind this sample

- Securing a React SPA with the BFF pattern and Abblix OIDC Server: https://docs.abblix.com/docs/react-spa-bff-guide
- Practical implementation of modern authentication on .NET (OpenID Connect, BFF and SPA): https://docs.abblix.com/docs/practical-implementation-of-modern-authentication-on-the-net-openid-connect-bff-and-spa
