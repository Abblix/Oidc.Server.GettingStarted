<a name="top"></a>
[![Getting Started with OIDC Server](https://resources.abblix.com/imgs/jpg/getting-started-github-banner.jpg)](https://github.com/Abblix/Oidc.Server.GettingStarted)
[![.NET](https://img.shields.io/badge/.NET-6.0%2C%207.0%2C%208.0-512BD4)](https://docs.abblix.com/docs/technical-requirements)
[![language](https://img.shields.io/badge/language-C%23-239120)](https://learn.microsoft.com/ru-ru/dotnet/csharp/tour-of-csharp/overview)
[![OS](https://img.shields.io/badge/OS-linux%2C%20windows%2C%20macOS-0078D4)](https://docs.abblix.com/docs/technical-requirements)
[![CPU](https://img.shields.io/badge/CPU-x86%2C%20x64%2C%20ARM%2C%20ARM64-FF8C00)](https://docs.abblix.com/docs/technical-requirements)
[![GitHub last commit](https://img.shields.io/github/last-commit/Abblix/Oidc.Server.GettingStarted)](#)
[![license: CC BY 4.0](https://img.shields.io/badge/license-CC%20BY%204.0-lightgrey.svg)](https://creativecommons.org/licenses/by/4.0/)


⭐ Star us on GitHub — it motivates us a lot!

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
The `OpenIDProviderApp` serves as the OpenID Connect provider within this project. Its primary responsibilities include authenticating users, managing their sessions, and issuing tokens in accordance with the OpenID Connect protocol. Specifically, it validates client requests and provides access and refresh tokens that authorize user resource access, as well as ID tokens that verify user identity. The application employs the Abblix OIDC Server solution to function effectively as an OpenID Connect protocol server. Additionally, the app is designed to handle various OAuth 2.0 flows, ensuring secure and compliant user authentication and authorization processes in modern web applications.

- **TestClientApp**  
The `TestClientApp` functions as the Relying Party, acting as a client that depends on the `OpenIDProviderApp` for user authentication. It demonstrates the interaction between a client application and an OpenID Connect provider, showing how users are authenticated, tokens are obtained, and protected resources are accessed. This scenario offers practical insight into integrating OpenID Connect authentication into client applications. The `TestClientApp` uses `Microsoft.AspNetCore.Authentication.OpenIdConnect` to operate as an OpenID Connect client, making it a practical example of real-world authentication in .NET environments.

- **BffSample**  
The `BffSample` implements the Backend-For-Frontend (BFF) architectural pattern to improve the security and manageability of interactions between a Single Page Application (SPA) and its backend services. The BFF acts as an intermediary, handling authentication and session management on behalf of the SPA, thereby reducing the surface area for attacks and simplifying client-side code. This sample is designed to showcase how to effectively apply the BFF pattern in a .NET environment, leveraging modern security practices and enhancing the overall security posture of web applications.

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
## 📃 License

This project is licensed under the Creative Commons Attribution 4.0 International License. You can review the full license text at the following link: [CC BY 4.0 License](https://creativecommons.org/licenses/by/4.0/).

## 🔗 Key Contacts & Resources

For more details about our products, services, or any general information regarding the Abblix OIDC Server, feel free to reach out to us. We are here to provide support and answer any questions you may have. Below are the best ways to contact our team:

- **[Email](mailto:support@abblix.com)**: Send us your inquiries or support requests at support@abblix.com.
- **[Website](https://www.abblix.com/abblix-oidc-server)**: Visit the official page for more information.
- **[GitHub Repository](https://github.com/Abblix/Oidc.Server)**: Explore the source code and contribute to the Abblix OIDC Server.
- **[Abblix Documentation](https://docs.abblix.com/docs)**: Access detailed documentation for all our products and services.

We look forward to assisting you and ensuring your experience with our products is successful and enjoyable!

[Back to top](#top)
