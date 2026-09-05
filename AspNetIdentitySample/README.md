# OpenID Provider with ASP.NET Core Identity: the production-shaped step

AspNetIdentitySample is OpenIDProviderApp taken one step toward production. It keeps the same protocol surface from Abblix OIDC Server but replaces every in-memory shortcut with a durable equivalent: users and credentials live in ASP.NET Core Identity on EF Core with SQLite, clients live in a database-backed store, and the signing key is persisted (encrypted) so a restart does not invalidate tokens already issued. Instead of a seeded demo account, it ships a self-service sign-up form, so the first thing a fresh run asks for is registration.

## What it demonstrates

- The two seams where the library meets an external user system: an `IUserInfoProvider` adapter over Identity's `UserManager` (`IdentityUserInfoProvider`) that turns a subject into claims, and a login flow where Identity verifies the password (`CheckPasswordSignInAsync`, with hashing, failed-attempt counting, and lockout) while the library's `IAuthSessionService` issues the OpenID Connect session cookie.
- Self-service registration: the sign-up form posts to `POST /api/auth/register`, which creates the account through `UserManager` (salted PBKDF2 hash, with the unique-email and password policy enforced), signs the user in, and returns the URL that resumes the OIDC flow. There is no seeded user.
- A React auth UI decoupled from the server's transport: the SignIn and SignUp screens are a React and Tailwind SPA that talks to the server only through the JSON auth API. The client's TypeScript types are generated from the server's OpenAPI document, so the two cannot drift, and the same UI works whether the OIDC endpoints are wired through the MVC adapter or a Minimal API host.
- A durable client store in the database, so clients can be added or changed without a redeploy, and a client that registers itself through Dynamic Client Registration survives a restart.
- Signing keys persisted to SQLite, encrypted with Data Protection, so tokens issued before a restart keep validating afterwards.
- Security-stamp validation: the stamp snapshotted at login is compared on every request, so a password change or an explicit stamp update ends existing sessions.

The sample is honestly labelled. Each development-grade default (an unconfirmed email on sign-up, an unencrypted local Data Protection key ring, client secrets in the database, `EnsureCreated` instead of EF migrations) carries a compile-time `#warning` that names the production choice and links the article explaining it.

## Running it

From the `Oidc.Server.GettingStarted` root:

- `dotnet run --project AspNetIdentitySample` starts the provider on `https://localhost:5001`. It stands in for OpenIDProviderApp on the same port, and it seeds `test_client`, so TestClientApp points at it unchanged. BffSample and BlazorSample need their clients added to the store first.
- Start a client next to it (TestClientApp on `https://localhost:5002`), follow its sign-in link, and use Register to create an account. You return to the client already signed in.

On first run it creates its SQLite databases (`users.db`, `oidc.db`) and seeds a signing key and the `test_client`. Delete the `.db` files to start from an empty store. Node.js is required: the auth screens are a React SPA that Vite builds into `wwwroot/auth` on first build.

## Layout

- `Program.cs`: Identity + EF wiring, `AddOidcServices`, and the durable signing-key and client-store registrations.
- `Controllers/AuthController.cs`: serves the React auth SPA for `/Auth/Login` and `/Auth/Register`, and issues the antiforgery token the SPA echoes back.
- `Controllers/AuthApiController.cs` and `Models/AuthContracts.cs`: the JSON auth API (`/api/auth/login`, `/api/auth/register`) and its request and response contracts.
- `ClientApp/`: the React, TypeScript, Vite, and Tailwind auth SPA. `npm run gen:api` regenerates `src/api/schema.d.ts` from the server's OpenAPI document (refresh the snapshot with `curl -k https://localhost:5001/openapi/v1.json -o ClientApp/openapi.json` while the provider runs).
- `IdentityUserInfoProvider.cs`: the subject-to-claims adapter over Identity.
- `OidcStore/`: the SQLite-backed signing-key provider (`DatabaseKeysProvider`), the durable client store (`DurableClientStore`), and their `DbContext`.
- `AppDbContext.cs`: the Identity `DbContext`.

## The guides behind this sample

- Integrating ASP.NET Core Identity with Abblix OIDC Server: https://docs.abblix.com/docs/aspnet-identity-integration
- A durable client store in about a hundred lines: https://docs.abblix.com/docs/durable-client-store
- Persisting JWT signing keys in production: https://docs.abblix.com/docs/signing-key-persistence
- Choosing a backend for operational state: https://docs.abblix.com/docs/choosing-a-cache-backend
- Production hardening checklist: https://docs.abblix.com/docs/production-hardening-checklist
