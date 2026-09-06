<a name="top"></a>
[![Getting Started with OIDC Server](https://resources.abblix.com/imgs/jpg/getting-started-github-banner.jpg)](https://github.com/Abblix/Oidc.Server.GettingStarted)
[![.NET](https://img.shields.io/badge/.NET-8.0%2C%209.0%2C%2010.0-512BD4)](https://docs.abblix.com/docs/technical-requirements)
[![language](https://img.shields.io/badge/language-C%23-239120)](https://learn.microsoft.com/ru-ru/dotnet/csharp/tour-of-csharp/overview)
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
- [About the Getting Started](#-about-the-getting-started)
- [About Abblix OIDC Server](#%EF%B8%8F-about-abblix-oidc-server)
- [How to Build](#%EF%B8%8F-how-to-build)
- [License](#-license)
- [Key Contacts & Resources](#-key-contacts--resources)

## 🚀 About the Getting Started

This repository contains all the necessary code  from the Getting Started article on creating an OpenID Connect provider using ASP.NET MVC and our Abblix OIDC Server solution.

Before diving into this solution, make sure to review either the [Getting Started Guide](https://docs.abblix.com/docs/getting-started-guide) or the [Practical Implementation of Modern Authentication on the .NET Platform: OpenID Connect, BFF and SPA](https://docs.abblix.com/docs/practical-implementation-of-modern-authentication-on-the-net-openid-connect-bff-and-spa). This solution includes projects that are implementations described in these guides, which provide detailed, step-by-step instructions to help you fully understand each project.

> [!IMPORTANT]
> This codebase is intended primarily for self-checks. We strongly recommend building the entire project from scratch to significantly enhance your understanding of these technologies.
### Included projects

- **OpenIDProviderApp**  
The `OpenIDProviderApp` serves as the OpenID Connect provider within this project. Its primary responsibilities include authenticating users, managing their sessions, and issuing tokens in accordance with the OpenID Connect protocol. Specifically, it validates client requests and provides access tokens that authorize user resource access, as well as ID tokens that verify user identity. The application employs the Abblix OIDC Server solution to function effectively as an OpenID Connect protocol server. Additionally, the app is designed to handle various OAuth 2.0 flows, ensuring secure and compliant user authentication and authorization processes in modern web applications.

- **AspNetIdentitySample**  
The `AspNetIdentitySample` is the `OpenIDProviderApp` taken one step toward production: it replaces the in-memory demo user list with a real ASP.NET Core Identity user store backed by Entity Framework Core and SQLite. It demonstrates the two seams where Abblix OIDC Server meets an external user system. First, an `IUserInfoProvider` adapter over Identity's `UserManager` turns a subject identifier into claims. Second, a login flow that keeps responsibilities cleanly split: Identity verifies the password through `CheckPasswordSignInAsync` (password hashing, failed-attempt counting, and lockout), while the library's `IAuthSessionService` issues the OpenID Connect session cookie. The result is a provider whose users, credentials, and profile data live in a database rather than in code, while the protocol handling stays entirely with the library. Its sign-in and sign-up screens are a React and Tailwind SPA that talks to a JSON auth API, with the client's types generated from the server's OpenAPI document, so the same UI works over an MVC or a Minimal API host unchanged.

- **OpenIDProviderApp.MinimalApi**  
The `OpenIDProviderApp.MinimalApi` (port 5006) is the Minimal API counterpart of the `OpenIDProviderApp`: the same Abblix OIDC Server protocol core hosted through the `Abblix.Oidc.Server.MinimalApi` adapter instead of MVC. It configures a signing key in code, reads its clients from the `Oidc` section of `appsettings.json` like the MVC provider does, and maps the OIDC endpoints with `MapOidcEndpoints()`, making it a compact reference for hosting the server without MVC. It carries the same three interactive clients and the same `weather` resource as the MVC provider, so the two publish the same capabilities and a client moves between them by changing `Authority`. The addresses in the two discovery documents differ, because the two listen on different ports, and a client that also calls ApiSample needs the API's pinned issuer moved as well. It adds a `client_credentials` client for headless checks, which the MVC provider does not carry. Its own README covers running it.

- **TestClientApp**  
The `TestClientApp` functions as the Relying Party, acting as a client that depends on the `OpenIDProviderApp` (or its Minimal API twin, `OpenIDProviderApp.MinimalApi`) for user authentication. It demonstrates the interaction between a client application and an OpenID Connect provider, showing how users are authenticated and tokens are obtained. This scenario offers practical insight into integrating OpenID Connect authentication into client applications. The `TestClientApp` uses `Microsoft.AspNetCore.Authentication.OpenIdConnect` to operate as an OpenID Connect client, making it a practical example of real-world authentication in .NET environments.

- **BffSample**  
The `BffSample` implements the Backend-For-Frontend (BFF) architectural pattern for a React Single Page Application (built with Vite) served by a .NET backend. The backend is a confidential OpenID Connect client: it runs the authorization code flow with PKCE, keeps the resulting tokens in an encrypted, HttpOnly session cookie, and never exposes them to browser JavaScript. Requests from the SPA to protected APIs are proxied through the backend, which strips the session cookie and attaches the access token, so the browser holds a session reference rather than a bearer token. The sample follows the IETF `draft-ietf-oauth-browser-based-apps` BFF profile and pairs with `ApiSample` as the protected resource. It is the runnable counterpart to the [Securing a React SPA with the BFF Pattern](https://docs.abblix.com/docs/react-spa-bff-guide) guide.

- **BlazorSample**  
The `BlazorSample` is a Blazor Web App (interactive Server render mode) acting as an OpenID Connect client of the `OpenIDProviderApp` (or its Minimal API twin, `OpenIDProviderApp.MinimalApi`). It shows the pattern that keeps Blazor and OIDC working together: the pages are rendered by Blazor, but sign-in and sign-out run on plain HTTP endpoints rather than inside an interactive circuit, because writing the authentication cookie and issuing the OIDC redirect both need the HTTP response. The sample uses `Microsoft.AspNetCore.Authentication.OpenIdConnect` with cookie sessions, the authorization code flow and PKCE.

- **SharedSignalsSample**  
The `SharedSignalsSample` is a pair of applications: a transmitter that revokes a user's session and announces it, and a receiver that hears the announcement and closes its own. Between them runs a real push delivery of a signed Security Event Token over HTTPS, verified against a JWK Set the receiver fetches for itself. The second host is what the sample is paying for: the question a reader arrives with is how to tell a genuine event from a forged one, and the answer - a signature, a published key set, an expected issuer and an expected audience - has nothing to check in a single process. It shows the transmitter side (`AddSecurityEvents`, `AddSharedSignalsTransmitter`, streams declared in configuration, and one `DispatchAsync` where the revocation happens) and the receiver side (`AddJwksKeyResolution` as the trust root, the validation options, replay suppression, and an `ISecurityEventSink` that is the only class the application itself writes). It also shows two things that only appear with two hosts: why a transmitter refuses by default to deliver into its own network, and what a key rollover looks like from the receiving side. It is the runnable counterpart to the [Shared Signals](https://docs.abblix.com/docs/shared-signals-framework) article.

- **ApiSample**  
The `ApiSample` demonstrates how to build a secure backend API that works in conjunction with an OpenID Connect provider to authenticate and authorize client requests. This sample illustrates the integration of security protocols like OAuth 2.0 and OpenID Connect into API development, ensuring that only authenticated and authorized users can access protected resources. The `ApiSample` serves as a practical guide for implementing secure APIs that comply with modern authentication standards, providing a robust foundation for securing backend services in a distributed web application architecture.

## 🛡️ About Abblix OIDC Server

**Abblix OIDC Server** is a .NET library designed to provide comprehensive support for OAuth2 and OpenID Connect on the server side. It adheres to high standards of flexibility, reusability, and reliability, utilizing well-known software design patterns, including modular and hexagonal architectures. These patterns ensure the following benefits:

- **Modularity**: Different parts of the library can function independently, enhancing the library's modularity and allowing for easier maintenance and updates.
- **Testability**: Improved separation of concerns makes the code more testable.
- **Maintainability**: Clear structure and separation facilitate better management of the codebase.

The library also supports Dependency Injection through the standard .NET DI container, aiding in the organization and management of code. Specifically tailored for seamless integration with ASP.NET WebApi, Abblix OIDC Server employs standard controller classes, binding, and routing mechanisms, simplifying the integration of OpenID Connect into your services.

## 🛠️ How to Build

Setting up your development environment for this project is straightforward. The following steps will guide you through cloning the repository, restoring dependencies, and building the project. This ensures that all necessary tools and libraries are properly configured for development.

```shell
# Ensure Git and .NET SDK are installed on your system
# Git is required for cloning the repository, and the .NET SDK is necessary for building the project.

# Clone the repository
git clone https://github.com/Abblix/Oidc.Server.GettingStarted.git

# Navigate to the project directory
cd Oidc.Server.GettingStarted

# Restore dependencies and build the project
# 'dotnet restore' downloads all the required .NET dependencies specified in the project file.
# 'dotnet build' compiles the project, making it ready for execution.
dotnet restore
dotnet build
```
## 🤝 Contributing

If you plan to send a pull request, set up local pre-commit hooks once:

```bash
pip install pre-commit
pre-commit install
```

After this, `git commit` automatically runs `actionlint` and a custom secrets-interpolation check on any change to `.github/workflows/`. The same checks run in CI as a backstop.

## 📃 License

The sample code in this repository is licensed under the MIT License - see [LICENSE](LICENSE). Copy it into your own project, change it, ship it. The boundary between this code and the product it demonstrates is spelled out in [NOTICE](NOTICE).

Abblix OIDC Server itself is a separate product under its own licence, consumed here as a NuGet package and not redistributed in source form. Its terms are at [abblix.com/license](https://www.abblix.com/license).

## 🔗 Key Contacts & Resources

For more details about our products, services, or any general information regarding the Abblix OIDC Server, feel free to reach out to us. We are here to provide support and answer any questions you may have. Below are the best ways to contact our team:

- **[Email](mailto:support@abblix.com)**: Send us your inquiries or support requests at support@abblix.com.
- **[Website](https://www.abblix.com/abblix-oidc-server)**: Visit the official page for more information.
- **[GitHub Repository](https://github.com/Abblix/Oidc.Server)**: Explore the source code and contribute to the Abblix OIDC Server.
- **[Abblix Documentation](https://docs.abblix.com/docs)**: Access detailed documentation for all our products and services.

We look forward to assisting you and ensuring your experience with our products is successful and enjoyable!

[Back to top](#top)
