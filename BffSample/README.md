# ASP.NET Core WebAPI with React and Vite Template

This template provides a starting point for building an ASP.NET Core WebAPI project with a React frontend using TypeScript and Vite. It sets up a basic project structure and configuration to help you get started quickly.

## Features

- **ASP.NET Core WebAPI**: A powerful framework for building web APIs with .NET.
- **React**: A popular JavaScript library for building user interfaces.
- **TypeScript**: A typed superset of JavaScript that compiles to plain JavaScript.
- **Vite**: A fast build tool and development server for modern web projects.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js and npm](https://nodejs.org/) (for managing React and Vite dependencies)

## Installing the Templates

First, you need to install the template from NuGet. If you haven't published it to NuGet yet, you can use the local `.nupkg` file.

### Installing from NuGet

```sh
dotnet new install Abblix.Templates
```

### Installing from Local `.nupkg` File

```sh
dotnet new install path/to/Abblix.Templates.1.0.0.nupkg
```

## Creating a New Project

You can create a new project using this template with any of the specified short names or aliases.

### Using the Primary Short Name

```sh
dotnet new abblix-react -n MyNewProject
```

### Using Alternative Short Names

```sh
dotnet new react-net8 -n MyNewProject
dotnet new react-typescript -n MyNewProject
dotnet new react-vite -n MyNewProject
```

## Running the Project

1. Navigate to the project directory:

   ```sh
   cd MyNewProject
   ```

2. Restore and build the .NET project:

   ```sh
   dotnet restore
   dotnet build
   ```

3. Navigate to the `ClientApp` directory to install the React dependencies:

   ```sh
   cd ClientApp
   npm install
   ```

4. Run the .NET backend and React frontend together:

   ```sh
   dotnet run
   ```

   This will start the ASP.NET Core server and the Vite development server. You can access the application in your browser at `http://localhost:5000`.

## Project Structure

```
/MyNewProject
├── Controllers/
├── ClientApp/
│   ├── src/
│   │   ├── App.tsx
│   │   ├── index.tsx
│   │   └── ...
│   ├── public/
│   │   └── index.html
│   ├── package.json
│   ├── tsconfig.json
│   └── vite.config.ts
├── Properties/
│   └── launchSettings.json
├── appsettings.json
├── MyNewProject.csproj
└── Program.cs
```

- **Controllers/**: Contains API controllers.
- **ClientApp/**: Contains the React frontend application.
- **Properties/**: Contains project settings files.
- **appsettings.json**: Configuration settings for the application.
- **MyNewProject.csproj**: Project file for the .NET project.
- **Program.cs**: Entry point for the .NET application.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for more details.

## Contacts

For more details about our products, services, or any general information regarding the our flagship product - Abblix OIDC Server, feel free to reach out to us. We are here to provide support and answer any questions you may have. Below are the best ways to contact our team:

- **Email**: Send us your inquiries or support requests at [support@abblix.com](mailto:support@abblix.com).
- **Website**: Visit the official Abblix OIDC Server page for more information: [Abblix OIDC Server](https://www.abblix.com/abblix-oidc-server).

We look forward to assisting you and ensuring your experience with our products is successful and enjoyable!

## Acknowledgements

- [ASP.NET Core](https://docs.microsoft.com/aspnet/core)
- [React](https://reactjs.org/)
- [TypeScript](https://www.typescriptlang.org/)
- [Vite](https://vitejs.dev/)
