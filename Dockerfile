FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["LnProxyApi.App/LnProxyApi.csproj", "LnProxyApi.App/"]
COPY ["LnProxyApi.App.Tests/LnProxyApi.App.Tests.csproj", "LnProxyApi.App.Tests/"]
COPY ["LnProxyApi.sln", "./"]
RUN dotnet restore

# Copy source code
COPY . .

# Build and publish application
RUN dotnet publish "LnProxyApi.App/LnProxyApi.csproj" -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy published files and SSL certificates
COPY --from=build /app/publish .

# Create directory for any necessary certificates/macaroons
RUN mkdir -p /app/certs

# Expose port
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Required environment variables:
# - LN_PROXY_MACAROON_PATH: Path to LND admin macaroon inside container
# - LN_PROXY_CERT_PATH: Path to LND TLS certificate inside container
# - LN_PROXY_GRPC_HOST: LND gRPC host address (format: host:port)
# 
# Optional environment variables:
# - ASPNETCORE_ENVIRONMENT: Set to Production by default
# - Logging__LogLevel__Default: Default logging level

# Set production environment
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "LnProxyApi.dll"]