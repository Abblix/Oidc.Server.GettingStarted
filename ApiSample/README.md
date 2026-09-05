# API Sample: a resource server protected by access tokens

ApiSample is the protected resource in the constellation: a minimal ASP.NET Core Web API that accepts only requests carrying a valid access token with the `weather` scope. It has no login UI and no library code of its own; it validates the JWT the provider issued using the standard `JwtBearer` handler. It is the API that BffSample forwards to.

## What it demonstrates

- Protecting an API with JWT bearer authentication, configured from `appsettings.json`, so tokens minted by the provider are validated against its published signing keys.
- A scope-based authorization policy: the `weatherforecast` endpoint requires an authenticated caller whose `scope` claim contains `weather`, and rejects anything else.
- A Swagger UI in development for exercising the endpoint by hand.

The `weather` scope and the resource URL (`https://localhost:5004`) match the resource definition registered in the provider, so the access tokens are audience-scoped to this API.

## Running it

ApiSample validates tokens; it does not issue them, so it runs behind a provider and is normally called through a client. The BFF flow exercises it end to end. From the `Oidc.Server.GettingStarted` root:

- `dotnet run --project OpenIDProviderApp` starts the provider on `https://localhost:5001`.
- `dotnet run --project ApiSample` starts this API on `https://localhost:5004`.
- `dotnet run --project BffSample` starts the SPA and BFF that call it on `https://localhost:5003`.

In development, open `https://localhost:5004/swagger` to see the endpoint. A direct call to `https://localhost:5004/weatherforecast` without a bearer token returns `401`.

## Layout

- `Program.cs`: the `JwtBearer` handler, the `weather` scope policy, and the `/weatherforecast` endpoint.
- `appsettings.json`: the `JwtBearerAuthentication` section (authority, audience, validation).

## The guides behind this sample

- Securing a React SPA with the BFF pattern, where this API is the resource the BFF forwards to: https://docs.abblix.com/docs/react-spa-bff-guide
- Getting Started with Abblix OIDC Server, the provider that issues the tokens this API validates: https://docs.abblix.com/docs/getting-started-guide
