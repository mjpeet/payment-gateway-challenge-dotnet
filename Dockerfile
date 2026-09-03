# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/PaymentGateway.Api/PaymentGateway.Api.csproj", "src/PaymentGateway.Api/"]
RUN dotnet restore "src/PaymentGateway.Api/PaymentGateway.Api.csproj"

COPY src/PaymentGateway.Api/. src/PaymentGateway.Api/
WORKDIR /src/src/PaymentGateway.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# curl needed for the HEALTHCHECK below to hit our own /health endpoint.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# .NET 8's base images ship a built-in non-root "app" user specifically for this —
# it's opt-in, not the default, so it has to be set explicitly. Files copied above
# were copied as root, but are world-readable by default, so the app user can still
# read and execute them.
USER app

# .NET 8's own container images default ASPNETCORE_URLS to 8080 already — set explicitly
# here for clarity rather than relying on that default silently.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "PaymentGateway.Api.dll"]
