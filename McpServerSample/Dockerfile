# One image holds the three apps and the certificates they serve and trust, so a build mints one root for all of them.
# Nothing here reaches the host: the root's private key stays in the certificates stage, which the image does not keep.

FROM mcr.microsoft.com/dotnet/sdk:11.0.100 AS certificates
WORKDIR /certificates
# A root valid for a year, and one leaf it signs for every name the apps are called by: they all run on localhost
RUN openssl req -x509 -newkey rsa:2048 -nodes -days 365 -subj "/CN=McpServerSample development root" \
        -addext "basicConstraints=critical,CA:TRUE" -addext "keyUsage=critical,keyCertSign,cRLSign" \
        -keyout root.key -out root.crt \
 && openssl req -newkey rsa:2048 -nodes -subj "/CN=localhost" -keyout localhost.key -out localhost.csr \
 && printf '%s\n' "basicConstraints=critical,CA:FALSE" "keyUsage=critical,digitalSignature,keyEncipherment" \
        "extendedKeyUsage=serverAuth" "subjectAltName=DNS:localhost,IP:127.0.0.1,IP:::1" > localhost.ext \
 && openssl x509 -req -in localhost.csr -CA root.crt -CAkey root.key -CAcreateserial -days 365 \
        -extfile localhost.ext -out localhost.crt

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
COPY --from=certificates /certificates/root.crt /etc/mcp-sample/trust/root.crt
COPY --from=certificates /certificates/localhost.crt /etc/mcp-sample/tls/localhost.crt
# Readable by the apps' user alone; copied after the certificate, whose copy creates the directory open to all
COPY --from=certificates --chown=app:app --chmod=0400 /certificates/localhost.key /etc/mcp-sample/tls/localhost.key
ENV \
    # Every https endpoint serves the leaf
    Kestrel__Certificates__Default__Path=/etc/mcp-sample/tls/localhost.crt \
    Kestrel__Certificates__Default__KeyPath=/etc/mcp-sample/tls/localhost.key \
    # The apps trust this root and no other: .NET on Linux reads its trusted roots from these two places
    SSL_CERT_FILE=/etc/mcp-sample/trust/root.crt \
    SSL_CERT_DIR=/etc/mcp-sample/trust \
    # Each service names its own addresses; the image's default http listener on 8080 is not one of them
    ASPNETCORE_HTTP_PORTS=
USER app
