# OpenID Provider with ASP.NET Core Identity: the production-shaped step

AspNetIdentitySample is OpenIDProviderApp taken one step toward production. It keeps the same protocol surface from Abblix OIDC Server but replaces every in-memory shortcut with a durable equivalent: users and credentials live in ASP.NET Core Identity on EF Core with SQLite, client registrations live in a database-backed store, and the signing key is persisted (encrypted) so a restart does not invalidate tokens already issued. Instead of a seeded demo account, it ships a self-service sign-up form, so the first thing a fresh run asks for is registration.

## What it demonstrates

- The two seams where the library meets an external user system: an `IUserInfoProvider` adapter over Identity's `UserManager` (`IdentityUserInfoProvider`) that turns a subject into claims, and a login flow where Identity verifies the password (`CheckPasswordSignInAsync`, with hashing, failed-attempt counting, and lockout) while the library's `IAuthSessionService` issues the OpenID Connect session cookie.
- Self-service registration: the sign-up form posts to `POST /api/auth/register`, which creates the account through `UserManager` (salted PBKDF2 hash, with the unique-email and password policy enforced), signs the user in, and returns the URL that resumes the OIDC flow. There is no seeded user.
- A React auth UI decoupled from the server's transport: the SignIn and SignUp screens are a React and Tailwind SPA that talks to the server only through the JSON auth API. The client's TypeScript types are generated from the server's OpenAPI document, so the two cannot drift. This sample wires the OIDC endpoints through the Minimal API adapter, `Abblix.Oidc.Server.MinimalApi` (`AddOidcServices` and `app.MapOidcEndpoints()`); the same React UI and JSON auth API would drop onto an MVC host unchanged, because they depend on the seam, not the transport.
- A durable client store in the database (`DurableClientStore`, an `ITenantClientRegistrationStore`), so clients can be added or changed without a redeploy, and once Dynamic Client Registration is enabled, a client that registers itself survives a restart. The server's own client store still serves any client the tenant declares in its definition first, and keeps everything else in this store.
- Signing keys persisted to SQLite, encrypted with Data Protection, so tokens issued before a restart keep validating afterwards.
- Security-stamp validation: the stamp snapshotted at login is compared on every request that carries the provider's session cookie, so a password change or an explicit stamp update ends the user's existing sessions at the provider.
- Back-channel logout (OpenID Connect Back-Channel Logout 1.0): a session the provider ends is ended at the clients signed in through it. `AddBackChannelLogout` turns the channel on, TestClientApp registers a back-channel logout URI, and the session is ended through the library's `IAuthSessionTerminator`, the same path its end-session endpoint takes, which sends each client its logout token. A demo page shows both; see Watching a session end below.

The provider serves one tenant, `main`, whose issuer is `https://localhost:5001`. Every row in the OIDC store carries the tenant's id and generation, so the same tables would keep several tenants' keys and clients apart.

Its development-grade defaults (an unconfirmed email on sign-up, an unencrypted local Data Protection key ring, signing keys and client secrets in the database) each carry a compile-time `#warning` that names the production choice and links the article explaining it. `EnsureCreated` instead of EF migrations is called out in a comment beside it.

## Running it

Requires the .NET 11 SDK and Node.js. From the `Oidc.Server.GettingStarted` root:

- `dotnet run --project AspNetIdentitySample` starts the provider on `https://localhost:5001`. It stands in for OpenIDProviderApp on the same port and seeds the same clients, so TestClientApp, BffSample and BlazorSample all point at it unchanged.
- Start a client next to it (TestClientApp on `https://localhost:5002`) and open it: its home page requires authentication, so it sends you straight to the provider. Create an account there with Create one on the sign-in screen, and you return to the client already signed in.

On first run it creates its SQLite databases (`users.db`, `oidc.db`) and seeds a signing key and the clients. Delete the `.db` files to start from an empty store. Node.js is required because the auth screens are a React SPA that Vite builds into `wwwroot/auth` on first build.

An `oidc.db` created before TestClientApp registered for back-channel logout still holds the older `test_client`, so a stamp change does not reach the client: delete `oidc.db*` once to seed it again.

If you ran a 2.x version of this sample before, delete `oidc.db` (with its `-shm` and `-wal` files) before the first run: the store's tables now carry the tenant's id and generation, `EnsureCreated` does not alter tables that already exist, and the old file stops the provider at startup with `no such column`. The next run creates a new signing key, so tokens issued by the old version stop validating. `users.db` is unchanged and can stay.

## Watching a session end

The sample has no password change screen, so a demo page stands in for one: `https://localhost:5001/Auth/Sessions` has a single button, End my sessions, which changes the security stamp of the account signed in at the provider in this browser. It acts on nobody else, and it posts through the same antiforgery check as the sign-in form. A real provider reaches the same `UpdateSecurityStampAsync` call from a password change or a "sign out everywhere" action.

There is no validation interval to wait out. Identity's own `SecurityStampValidator` rechecks every 30 minutes; this sample compares the stamp on every request that carries the provider's session cookie, so the first such request after the change ends the session.

- Sign in to TestClientApp as in Running it.
- Open `https://localhost:5001/Auth/Sessions` in the same browser and press End my sessions. The page answers `Your security stamp has changed.` The provider ends this browser's session at once, and its log shows the logout token going to the client: `Sending HTTP request POST https://localhost:5002/signout-backchannel`, then `Received HTTP response headers ... - 200`.
- TestClientApp's log answers `A back-channel logout token ended a provider session; a sign-in made through it ends on its next request`.
- Reload TestClientApp. Instead of your claims you get the provider's sign-in form: the client refused its own cookie because the provider ended the session it came from. Sign in with the same email and password and you return to the client signed in. The new session carries the new stamp.

A session of the same account in another browser ends when that browser next reaches the provider, the first request whose cookie carries the old stamp, and its clients are told then. The provider keeps no list of a user's sessions, since each one lives in its own browser's cookie, so a session that never comes back is never seen again. It still ends at the provider, but its clients are not told and keep their sign-ins until their own cookies expire.

To see the refusal on the demo page itself, reload `/Auth/Sessions` after pressing the button, then press it again. It answers `This browser holds no session at this provider.` Pressed again without the reload, it answers `Antiforgery validation failed`, because the page's antiforgery token was issued to the user who has just been signed out.

## Layout

- `Program.cs`: Identity + EF wiring, the tenant definition, `AddConsentGrantedForEveryClient` (every client here is the provider's own), `AddOidcServices` with `app.MapOidcEndpoints()` for the OIDC protocol, the durable signing-key and client-store registrations, and the auth UI endpoints. `MapGet("/Auth/Login")` and `/Auth/Register` serve the React SPA and issue the antiforgery token it echoes back; `MapPost("/api/auth/login")` and `/api/auth/register` are the JSON auth API, guarded by an antiforgery endpoint filter. The demo-only `/Auth/Sessions` page and its `/api/auth/demo/end-my-sessions` POST change the signed-in user's security stamp and end the session through `EndSessionForItsClientsAsync`, which the stamp check calls too.
- `Models/AuthContracts.cs`: the request and response contracts for the JSON auth API.
- `ClientApp/`: the React, TypeScript, Vite, and Tailwind auth SPA. `npm run gen:api` regenerates `src/api/schema.d.ts` from the server's OpenAPI document (refresh the snapshot with `curl -k https://localhost:5001/openapi/v1.json -o ClientApp/openapi.json` while the provider runs).
- `IdentityUserInfoProvider.cs`: the subject-to-claims adapter over Identity.
- `OidcStore/`: the SQLite-backed signing-key provider (`DatabaseKeysProvider`), the client registration store (`DurableClientStore`), and their `DbContext`.
- `AppDbContext.cs`: the Identity `DbContext`.
- `requests.http`: the discovery, key set, client credentials token and introspection requests, ready to send from the IDE with the seeded `console_client`.

## The guides behind this sample

- Integrating ASP.NET Core Identity with Abblix OIDC Server: https://docs.abblix.com/docs/aspnet-identity-integration
- A durable client store in about a hundred lines: https://docs.abblix.com/docs/durable-client-store
- Persisting JWT signing keys in production: https://docs.abblix.com/docs/signing-key-persistence
- Choosing a backend for operational state: https://docs.abblix.com/docs/choosing-a-cache-backend
- Production hardening checklist: https://docs.abblix.com/docs/production-hardening-checklist
