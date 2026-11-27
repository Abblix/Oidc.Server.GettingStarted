# One image holds the three apps and the one certificate they serve and trust, so a build makes it once for all of them.
# Nothing here reaches the host.

FROM mcr.microsoft.com/dotnet/sdk:11.0.100 AS certificates
WORKDIR /certificates

# The ASP.NET Core development certificate for localhost, the one dotnet dev-certs makes on a developer machine,
# written as localhost.crt and localhost.key. It is self-signed, so it is also the one root the apps trust.
RUN dotnet dev-certs https --export-path /certificates/localhost.crt --format PEM --no-password \
 && chmod 0444 localhost.crt

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
COPY --from=certificates /certificates/localhost.crt /etc/mcp-sample/trust/localhost.crt
COPY --from=certificates /certificates/localhost.crt /etc/mcp-sample/tls/localhost.crt

# Readable by the apps' user alone; copied after the certificate, whose copy creates the directory open to all
COPY --from=certificates --chown=app:app --chmod=0400 /certificates/localhost.key /etc/mcp-sample/tls/localhost.key

ENV \
    # Every https endpoint serves the development certificate
    Kestrel__Certificates__Default__Path=/etc/mcp-sample/tls/localhost.crt \
    Kestrel__Certificates__Default__KeyPath=/etc/mcp-sample/tls/localhost.key \
    # The apps trust that certificate and no other: .NET on Linux reads its trusted roots from these two places
    SSL_CERT_FILE=/etc/mcp-sample/trust/localhost.crt \
    SSL_CERT_DIR=/etc/mcp-sample/trust \
    # Each service names its own addresses; the image's default http listener on 8080 is not one of them
    ASPNETCORE_HTTP_PORTS=

# A volume mounted here starts with this folder's owner, so the apps' user can write its keys there
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R app:app /home/app/.aspnet

USER app
