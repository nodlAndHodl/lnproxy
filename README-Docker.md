# Docker Setup for LnProxyApi

This guide explains how to use the Docker setup for running LnProxyApi.

## Prerequisites

- Docker and Docker Compose installed on your system
- Access to an LND node with gRPC enabled

## Quick Start

1. Create a directory for your LND certificates and macaroons:

```bash
mkdir -p lnd-certs
```

2. Copy your LND credentials to the lnd-certs directory:

```bash
# Replace these paths with the actual paths to your LND files
cp /path/to/your/admin.macaroon lnd-certs/
cp /path/to/your/tls.cert lnd-certs/
```

3. Build and start the container:

```bash
docker-compose up -d
```

4. Check the logs:

```bash
docker-compose logs -f
```

## Environment Variables

The container uses the following environment variables:

| Variable | Description | Default |
|----------|-------------|---------|
| APPSETTINGS__PATHTOMACAROON | Path to LND admin macaroon inside container | /app/certs/admin.macaroon |
| APPSETTINGS__PATHTOSSLCERTIFICATE | Path to LND TLS certificate inside container | /app/certs/tls.cert |
| APPSETTINGS__GRPCHOST | LND gRPC host address (format: host:port) | lnd:10009 |
| ASPNETCORE_ENVIRONMENT | ASP.NET Core environment | Production |
| Logging__LogLevel__Default | Default logging level | Information |

## Connecting to a Local LND Instance

If you're running LND on your host machine, update the docker-compose.yml file:

```yaml
services:
  lnproxyapi:
    # ... other settings
    environment:
      # ... other environment variables
      - APPSETTINGS__GRPCHOST=host.docker.internal:10009
    extra_hosts:
      - "host.docker.internal:host-gateway"
```

## Building the Docker Image Manually

```bash
docker build -t lnproxyapi .
```

## Running the Docker Container Manually

```bash
docker run -p 8080:8080 \
  -e APPSETTINGS__PATHTOMACAROON=/app/certs/admin.macaroon \
  -e APPSETTINGS__PATHTOSSLCERTIFICATE=/app/certs/tls.cert \
  -e APPSETTINGS__GRPCHOST=lnd:10009 \
  -v $(pwd)/lnd-certs:/app/certs \
  lnproxyapi
```

## Testing the API

Once running, access the Swagger UI at:

```
http://localhost:8080/swagger/index.html
```

Or use curl to test the API:

```bash
curl --header "Content-Type: application/json" \
  --request POST \
  --data '{"invoice":"<bolt11 invoice>"}' \
  http://localhost:8080
```