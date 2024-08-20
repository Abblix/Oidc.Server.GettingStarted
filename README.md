# Getting Started with Abblix OIDC Server

This repository contains all the necessary code from the Getting Started article on creating an OpenID Connect provider using ASP.NET MVC and our Abblix OIDC Server solution.

## Description

Before diving into this solution, make sure to review the complete [Getting Started Guide](https://docs.abblix.com/docs/getting-started-guide). This guide offers detailed, step-by-step instructions and tips to fully grasp the intricacies of this solution. 
> [!IMPORTANT]
> This codebase is intended primarily for self-checks. We encourage you to build the entire 
> project from scratch to significantly enhance your understanding of these technologies.

### Included projects

- **OpenIDProviderApp**  
The `OpenIDProviderApp` serves as the OpenID Connect provider within this project. Its primary responsibilities include authenticating users, managing their sessions, and issuing tokens in accordance with the OpenID Connect protocol. Specifically, it validates client requests and provides access and refresh tokens that authorize user resource access, as well as ID tokens that verify user identity. The application employs the Abblix OIDC Server solution to function effectively as an OpenID Connect protocol server. Additionally, the app is designed to handle various OAuth 2.0 flows, ensuring secure and compliant user authentication and authorization processes in modern web applications.

- **TestClientApp**  
The `TestClientApp` functions as the Relying Party, acting as a client that depends on the `OpenIDProviderApp` for user authentication. It demonstrates the interaction between a client application and an OpenID Connect provider, showing how users are authenticated, tokens are obtained, and protected resources are accessed. This scenario offers practical insight into integrating OpenID Connect authentication into client applications. The `TestClientApp` uses `Microsoft.AspNetCore.Authentication.OpenIdConnect` to operate as an OpenID Connect client, making it a practical example of real-world authentication in .NET environments.

- **BffSample**  
The `BffSample` implements the Backend-For-Frontend (BFF) architectural pattern to improve the security and manageability of interactions between a Single Page Application (SPA) and its backend services. The BFF acts as an intermediary, handling authentication and session management on behalf of the SPA, thereby reducing the surface area for attacks and simplifying client-side code. This sample is designed to showcase how to effectively apply the BFF pattern in a .NET environment, leveraging modern security practices and enhancing the overall security posture of web applications.

- **ApiSample**  
The `ApiSample` demonstrates how to build a secure backend API that works in conjunction with an OpenID Connect provider to authenticate and authorize client requests. This sample illustrates the integration of security protocols like OAuth 2.0 and OpenID Connect into API development, ensuring that only authenticated and authorized users can access protected resources. The `ApiSample` serves as a practical guide for implementing secure APIs that comply with modern authentication standards, providing a robust foundation for securing backend services in a distributed web application architecture.

## How to Build

Follow these steps to get your development environment set up:

```shell
# Ensure Git and .NET SDK are installed on your system

# Clone the repository
git clone https://github.com/Abblix/Oidc.Server.GettingStarted.git

# Navigate to the project directory
cd Oidc.Server.GettingStarted

# Restore dependencies and build the project
# dotnet restore - Downloads the required .NET dependencies.
# dotnet build - Compiles the project.
dotnet restore
dotnet build
```

## Contacts

For more information regarding the Abblix OIDC Server, feel free to reach out to us. We are here to provide support and answer any questions you may have. Below are the best ways to contact our team:

- **Email**: Send us your inquiries or support requests at [support@abblix.com](mailto:support@abblix.com).
- **Website**: Visit the official Abblix OIDC Server page for more information: [Abblix OIDC Server](https://www.abblix.com/abblix-oidc-server).

We are dedicated to ensuring your experience with our products is both successful and enjoyable!
