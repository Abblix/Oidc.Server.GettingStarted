<a name="top"></a>
[![Getting Started with OIDC Server](https://resources.abblix.com/imgs/jpg/getting-started-github-banner.jpg)](https://github.com/Abblix/Oidc.Server.GettingStarted)
[![.NET](https://img.shields.io/badge/.NET-11.0-512BD4)](https://docs.abblix.com/docs/technical-requirements)
[![language](https://img.shields.io/badge/language-C%23-239120)](https://learn.microsoft.com/dotnet/csharp/tour-of-csharp/overview)
[![OS](https://img.shields.io/badge/OS-linux%2C%20windows%2C%20macOS-0078D4)](https://docs.abblix.com/docs/technical-requirements)
[![CPU](https://img.shields.io/badge/CPU-x86%2C%20x64%2C%20ARM%2C%20ARM64-FF8C00)](https://docs.abblix.com/docs/technical-requirements)
[![GitHub last commit](https://img.shields.io/github/last-commit/Abblix/Oidc.Server.GettingStarted)](#)
[![license: MIT](https://img.shields.io/badge/license-MIT-brightgreen.svg)](LICENSE)


⭐ Star us on GitHub - it motivates us a lot!

[![Share](https://img.shields.io/badge/share-000000?logo=x&logoColor=white)](https://x.com/intent/tweet?text=Check%20out%20this%20project%20on%20GitHub:%20https://github.com/Abblix/Oidc.Server.GettingStarted%20%23OpenIDConnect%20%23DotNet)
[![Share](https://img.shields.io/badge/share-1877F2?logo=facebook&logoColor=white)](https://www.facebook.com/sharer/sharer.php?u=https://github.com/Abblix/Oidc.Server.GettingStarted)
[![Share](https://img.shields.io/badge/share-0A66C2?logo=linkedin&logoColor=white)](https://www.linkedin.com/sharing/share-offsite/?url=https://github.com/Abblix/Oidc.Server.GettingStarted)
[![Share](https://img.shields.io/badge/share-FF4500?logo=reddit&logoColor=white)](https://www.reddit.com/submit?title=Check%20out%20this%20project%20on%20GitHub:%20https://github.com/Abblix/Oidc.Server.GettingStarted)
[![Share](https://img.shields.io/badge/share-0088CC?logo=telegram&logoColor=white)](https://t.me/share/url?url=https://github.com/Abblix/Oidc.Server.GettingStarted&text=Check%20out%20this%20project%20on%20GitHub)

## Table of Contents
- [About this repository](#-about-this-repository)
- [Prerequisites](#-prerequisites)
- [Run the loop with one command](#%EF%B8%8F-run-the-loop-with-one-command)
- [Follow the guide with one command](#-follow-the-guide-with-one-command)
- [About Abblix OIDC Server](#%EF%B8%8F-about-abblix-oidc-server)
- [How to Build](#%EF%B8%8F-how-to-build)
- [Feedback](#-feedback)
- [License](#-license)
- [Contacts and resources](#-contacts-and-resources)

## 🚀 About this repository

Runnable samples for Abblix OIDC Server 3.0: OpenID Connect providers (MVC, Minimal API, ASP.NET Core Identity, keys in an external custodian) and the clients and resources that use them (an MVC client, a Blazor app, a React SPA behind a BFF, an API, an MCP server, a Shared Signals receiver). To see one working end to end, start with [Run the loop with one command](#%EF%B8%8F-run-the-loop-with-one-command). Samples that have a guide of their own link to it below.

> [!TIP]
> To learn the protocol, follow the [Getting Started Guide](https://www.abblix.com/en/docs/getting-started-guide) yourself and compare your result with `GettingStartedGuide`, which holds exactly the code the guide writes. The other samples are reference code you can copy into your own project.

### Samples

Every sample folder has a README on running that sample alone.

- OpenIDProviderApp  
The `OpenIDProviderApp` is the MVC provider: it signs users in, keeps their session and issues ID and access tokens through Abblix OIDC Server. It registers the interactive clients (`TestClientApp`, `BffSample`, `BlazorSample`), a `client_credentials` client `console_client`, and the `weather` resource that `ApiSample` protects, and it serves the authorization code flow with PKCE. It is a demo by design: one user hard-coded in `Program.cs`, a signing key generated at every start (tokens issued before a restart stop validating), and consent granted to every client without asking. `AspNetIdentitySample` shows what changes on the way to production.

- AspNetIdentitySample  
The `AspNetIdentitySample` is the provider taken one step toward production, hosted through the Minimal API adapter: users live in an ASP.NET Core Identity store backed by Entity Framework Core and SQLite, and its signing key (encrypted with Data Protection) and client registrations persist in SQLite as well, so a restart keeps both. No user is seeded: the sign-in screen links to sign-up, where the first account is created. It demonstrates the seams where Abblix OIDC Server meets an external user system. First, an `IUserInfoProvider` adapter over Identity's `UserManager` turns a subject identifier into claims. Second, a login flow that splits the work: Identity verifies the password through `CheckPasswordSignInAsync` (password hashing, failed-attempt counting, and lockout), while the library's `IAuthSessionService` issues the OpenID Connect session cookie. The result is a provider whose users, credentials, and profile data live in a database rather than in code, while the protocol handling stays entirely with the library. Its sign-in and sign-up screens are a React and Tailwind SPA that talks to a JSON auth API, with the client's types generated from the server's OpenAPI document.

- OpenIDProviderApp.MinimalApi  
The `OpenIDProviderApp.MinimalApi` is the Minimal API counterpart of `OpenIDProviderApp`: the same protocol core hosted through `Abblix.Oidc.Server.MinimalApi` and mapped with `MapOidcEndpoints()`, with the same clients, the same `weather` resource and `console_client`, read from the `Oidc` section of `appsettings.json`. Like the MVC provider, it generates its signing key at every start. A client moves between the two by changing `Authority`, and a client that also calls `ApiSample` moves the API's `Authority` with it, since the API takes the provider's keys and issuer from there.

- TestClientApp  
The `TestClientApp` is the relying party: an MVC client that signs users in through `OpenIDProviderApp` (or its Minimal API twin, `OpenIDProviderApp.MinimalApi`) and calls `ApiSample` with the access token it receives. It uses `Microsoft.AspNetCore.Authentication.OpenIdConnect` as its OpenID Connect client.

- BffSample  
The `BffSample` implements the Backend-For-Frontend (BFF) architectural pattern for a React Single Page Application (built with Vite) served by a .NET backend. The backend is a confidential OpenID Connect client: it runs the authorization code flow with PKCE, keeps the resulting tokens in an encrypted, HttpOnly session cookie, and never exposes them to browser JavaScript. Requests from the SPA to protected APIs are proxied through the backend, which strips the session cookie and attaches the access token, so the browser holds a session reference rather than a bearer token. The sample follows the BFF pattern of RFC 10017 (OAuth 2.0 for Browser-Based Applications): the SPA calls the backend on its own origin with a static CSRF header, the backend forwards only the API routes it names, and the session cookie carries the `__Host-Http-` prefix. It signs in through `OpenIDProviderApp` (or `OpenIDProviderApp.MinimalApi`) and pairs with `ApiSample` as the protected resource. It is the runnable counterpart to the [Securing a React SPA with the BFF Pattern](https://www.abblix.com/en/docs/react-spa-bff-guide) guide and the [OpenID Connect, BFF and SPA](https://www.abblix.com/en/docs/net-authentication-openid-connect-bff-spa) article.

- BlazorSample  
The `BlazorSample` is a Blazor Web App (interactive Server render mode) acting as an OpenID Connect client of the `OpenIDProviderApp` (or its Minimal API twin, `OpenIDProviderApp.MinimalApi`). It shows the pattern that keeps Blazor and OIDC working together: the pages are rendered by Blazor, but sign-in and sign-out run on plain HTTP endpoints rather than inside an interactive circuit, because writing the authentication cookie and issuing the OIDC redirect both need the HTTP response. The sample uses `Microsoft.AspNetCore.Authentication.OpenIdConnect` with cookie sessions, the authorization code flow and PKCE.

- SharedSignalsSample  
The `SharedSignalsSample` is a pair of applications: a transmitter that revokes a user's session and announces it, and a receiver that hears the announcement and closes its own. Between them runs a real push delivery of a signed Security Event Token over HTTPS, verified against a JWK Set the receiver fetches for itself. The second host is what the sample is paying for: the question a reader arrives with is how to tell a genuine event from a forged one, and the answer - a signature, a published key set, an expected issuer and an expected audience - has nothing to check in a single process. It shows the transmitter side (`AddSecurityEvents`, `AddSharedSignalsTransmitter`, streams declared in configuration, and one `DispatchAsync` where the revocation happens) and the receiver side (`AddJwksKeyResolution` as the trust root, the validation options, a record of the events it accepted, whose repeats still reach an idempotent sink, and an `ISecurityEventSink` that is the only class the application itself writes). It also shows what only appears with a second host: why a transmitter refuses by default to deliver into its own network, and what a key rollover looks like from the receiving side. It is the runnable counterpart to [a working Shared Signals transmitter and receiver for ASP.NET Core](https://www.abblix.com/en/docs/shared-signals-guide).

- ApiSample  
The `ApiSample` is the protected resource: a minimal API whose `/weatherforecast` endpoint accepts only a JWT access token of type `at+jwt` issued by the provider for its own audience, `https://localhost:5004`, and carrying the `weather` scope. It takes the issuer and keys from the provider named in its `Authority` setting, `OpenIDProviderApp` by default. `TestClientApp` and `BffSample` call it with the tokens they obtain.

- ExternalKeySample  
The `ExternalKeySample` is an OpenID Connect provider whose signing and encryption keys are held in an external key custodian, a HashiCorp Vault or OpenBao Transit engine (the sample starts OpenBao in Docker) or an Azure Key Vault, instead of in the application. It issues `client_credentials` tokens to one service client, publishes only the public halves of its keys, and, with encryption turned on, has the custodian open its encrypted tokens when they are introspected. One setting switches between the placements the library offers: keys that never leave the custodian, where every signature is a call to it, and keys the server mints itself and keeps sealed to a custodian key, where signing is local. Sealing the vault shows the trade: with keys held in the custodian, signing stops at once; with minted keys, a running server keeps signing and a fresh one cannot start.

- McpServerSample  
The `McpServerSample` is an authorized Model Context Protocol server paired with its own provider. `McpServerApp`, built on the official C# MCP SDK, publishes OAuth 2.0 Protected Resource Metadata (RFC 9728), answers an unauthenticated call with a challenge naming that metadata and the scope it needs, and accepts only access tokens whose audience is its own URI. `ProviderApp`, built on Abblix OIDC Server, declares the MCP server as a resource, lets MCP clients register themselves without an initial access token (RFC 7591) and issues tokens for the resource a client names (RFC 8707), with PKCE. Because any application can register, the user approves every authorization on a consent page that names the client and where the code will go. An MCP client that has never met either of them finds the provider through the MCP server, registers, signs the user in, gets the user's consent and calls the tool.

`OpenIDProviderApp`, `OpenIDProviderApp.MinimalApi`, `AspNetIdentitySample` and `ExternalKeySample` each ship a `requests.http` that fetches the discovery document and key set, gets a client credentials token and introspects it, straight from Visual Studio, Rider or VS Code.

`OpenIDProviderApp`, `AspNetIdentitySample` and `ExternalKeySample` all listen on `https://localhost:5001`, so run one of them at a time; the interactive clients work with `OpenIDProviderApp` or `AspNetIdentitySample`, whichever answers there.

## 📋 Prerequisites

- The .NET 11 SDK: every project targets `net11.0`.
- Node.js with npm: `OpenIDProviderApp`, `TestClientApp`, `AspNetIdentitySample` and `BffSample` restore or build their front ends during `dotnet build`, and the build stops if Node.js is missing.
- A trusted development certificate, since the samples call each other over HTTPS: `dotnet dev-certs https --trust`. On Linux, also add `$HOME/.aspnet/dev-certs/trust` to `SSL_CERT_DIR`, as that command explains.
- Docker, only for `ExternalKeySample` with the OpenBao custodian.
- bash and Python 3.9 or later, only for `GettingStartedGuide/guide.sh`.

## ▶️ Run the loop with one command

`OpenIDProviderApp`, `TestClientApp` and `ApiSample` form a complete loop: a provider, a web client that signs in through it, and an API that accepts the client's access token. From the repository root:

```shell
dotnet run-loop.cs
```

`run-loop.cs` is a single-file C# program, so the .NET SDK runs it with nothing installed beyond the [prerequisites](#-prerequisites). It builds the provider, the client and the API, starts each on the address of its launch profile (provider `https://localhost:5001`, or `https://localhost:5006` with `--provider minimal`, client `https://localhost:5002`, API `https://localhost:5004`) and waits until each answers. Ctrl+C stops them all. It refuses to start while any of those addresses already answers, so it never runs against an older copy left running.

Then walk through the loop in the browser:

- Open `https://localhost:5002`. The page needs a signed-in user, so it sends you to the provider; sign in as the demo user hard-coded in the provider's `Program.cs`, `john.doe@example.com` with password `Jd!2024$3cur3`, and you return to the client, which lists the claims from your ID token.
- Choose Call the API. The client sends its access token to `https://localhost:5004/weatherforecast` and shows the forecast the API returns. The token was requested for the API's `weather` scope, and the API accepts it only because the audience and scope match.
- Go back and choose Logout. The client ends its own session and the provider's, so the next visit asks you to sign in again.

`dotnet run-loop.cs --check` does the same walk without a browser and exits with a non-zero code at the first step that fails.

`--provider minimal` swaps the provider for `OpenIDProviderApp.MinimalApi`. The client and the API are pointed at it through environment variables for that run only, so their settings files stay as they are. It combines with `--check`, and [a workflow](.github/workflows/run-the-loop.yaml) runs the check with each provider on every pull request.

## 📜 Follow the guide with one command

The projects above carry more than the Getting Started guide builds, because other samples need it. `GettingStartedGuide` holds what the guide itself writes and nothing else: each file the guide creates, replaces or edits in its projects, `GettingStartedGuide/OpenIDProviderApp` and `GettingStartedGuide/TestClientApp`, at the path the guide gives it. These are not the root projects of the same names, which have grown past the guide. The guide's code blocks are taken from these files, so what you read is what gets built. A file the guide edits is the `dotnet new mvc` file with the guide's code in it; `GettingStartedGuide/guide-files.py check` fails when a piece of that code no longer has its place in the file today's template generates.

`GettingStartedGuide/guide.sh` follows the guide from an empty directory: it creates the solution and both projects, adds the packages, writes those files in as a reader does (a file the guide shows whole is copied, an edited one gets the guide's code put into the generated file), builds, starts both applications and walks the flow the guide describes, from the discovery document through signing in and reading the claims to signing out. It stops with a non-zero exit code at the first step that fails and names it. Beyond the [prerequisites](#-prerequisites) it needs free ports 5001 and 5002:

```shell
bash GettingStartedGuide/guide.sh ../guide-run
```

`--abblix-version <version>` installs that version of `Abblix.Oidc.Server.Mvc` instead of the latest, and `--nuget-source <folder or URL>` adds a package source next to nuget.org to take that version from, so a build of the library can be tried against the guide before it is published. [A workflow](.github/workflows/guide.yaml) runs the script on every pull request.

## 🛡️ About Abblix OIDC Server

Abblix OIDC Server is the .NET library every provider sample here is built on. Versions 1.x and 2.x are certified by the OpenID Foundation in every OpenID Provider profile and every logout profile; version 3.0 is being prepared for certification and runs the same conformance plans before every release. Beyond the OAuth 2.0 and OpenID Connect core it implements DPoP, PAR, JARM, RAR, CIBA, device authorization, token exchange and certificate-bound tokens. It registers through the standard .NET DI container and is hosted through MVC controllers or Minimal API endpoints, each mapped with one call. See the [library's repository](https://github.com/Abblix/Oidc.Server) and its [implemented standards](https://www.abblix.com/en/docs/implemented-standards).

## 🛠️ How to Build

With the [prerequisites](#-prerequisites) in place:

```shell
git clone https://github.com/Abblix/Oidc.Server.GettingStarted.git
cd Oidc.Server.GettingStarted

# Builds every project in GettingStarted.slnx, restoring packages first
dotnet build
```

## 🤝 Feedback

A sample that does not run, or a README that says something the code does not do, is a defect: [open an issue](https://github.com/Abblix/Oidc.Server.GettingStarted/issues) naming the sample and the step. Questions about the library itself go to its [issues](https://github.com/Abblix/Oidc.Server/issues) or [discussions](https://github.com/Abblix/Oidc.Server/discussions).

## 📃 License

The sample code in this repository is licensed under the MIT License - see [LICENSE](LICENSE). Copy it into your own project, change it, ship it. The boundary between this code and the product it demonstrates is spelled out in [NOTICE](NOTICE).

Abblix OIDC Server itself is a separate product under its own license, consumed here as a NuGet package and not redistributed in source form. Its terms are at [abblix.com/en/oidc-server/license](https://www.abblix.com/en/oidc-server/license), and the full text is [LICENSE.md](https://github.com/Abblix/Oidc.Server/blob/master/LICENSE.md) in the library's repository. Running these samples costs nothing: the license makes development, testing and staging environments free for everyone. Who may use it free in production is set out in the license itself.

## 🔗 Contacts and resources

- [Documentation](https://www.abblix.com/en/docs/introduction-to-abblix-oidc-server)
- [Abblix OIDC Server on GitHub](https://github.com/Abblix/Oidc.Server)
- [Product page](https://www.abblix.com/en/oidc-server)
- General inquiries: [info@abblix.com](mailto:info@abblix.com)
- Support requests: [support@abblix.com](mailto:support@abblix.com)

[Back to top](#top)
