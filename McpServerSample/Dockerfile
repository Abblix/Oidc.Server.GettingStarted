# One image holds the three apps. The certificate they serve is not in it: compose.yaml mounts the host's ASP.NET Core
# development certificate, which the host's browser and agents already trust.

FROM mcr.microsoft.com/dotnet/sdk:11.0.100 AS build
WORKDIR /src

COPY ProviderApp/ProviderApp.csproj ProviderApp/
COPY McpServerApp/McpServerApp.csproj McpServerApp/
COPY McpClientApp/McpClientApp.csproj McpClientApp/

RUN dotnet restore ProviderApp/ProviderApp.csproj \
 && dotnet restore McpServerApp/McpServerApp.csproj \
 && dotnet restore McpClientApp/McpClientApp.csproj

COPY . .

RUN dotnet publish ProviderApp/ProviderApp.csproj --no-restore -o /app/ProviderApp \
 && dotnet publish McpServerApp/McpServerApp.csproj --no-restore -o /app/McpServerApp \
 && dotnet publish McpClientApp/McpClientApp.csproj --no-restore -o /app/McpClientApp

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0

COPY --from=build /app /app

ENV \
    # Every https endpoint serves the certificate compose.yaml mounts here
    Kestrel__Certificates__Default__Path=/etc/mcp-sample/certificate/localhost.crt \
    Kestrel__Certificates__Default__KeyPath=/etc/mcp-sample/certificate/localhost.key \
    # .NET on Linux trusts the roots in these two places: the public ones, so the provider can fetch an agent's metadata
    # document from its vendor's site, and the mounted certificate, so the apps can call each other
    SSL_CERT_FILE=/etc/ssl/certs/ca-certificates.crt \
    SSL_CERT_DIR=/etc/mcp-sample/certificate \
    # Each service names its own addresses; the image's default http listener on 8080 is not one of them
    ASPNETCORE_HTTP_PORTS=

# A volume mounted here starts with this folder's owner, so the apps' user can write its keys there
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R app:app /home/app/.aspnet

USER app
