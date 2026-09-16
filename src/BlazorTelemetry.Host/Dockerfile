# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app
EXPOSE 8080
EXPOSE 4317
ENV DOTNET_EnableDiagnostics=0 \
    TZ=Europe/Paris \
    BlazorTelemetry__ConnectionString="Data Source=/data/telemetry.db;Cache=Shared" \
    DataProtection__KeysPath=/data/keys
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data \
    && chown -R $APP_UID:$APP_UID /data /app

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
RUN dotnet restore BlazorTelemetry.Host/BlazorTelemetry.Host.csproj
RUN dotnet publish BlazorTelemetry.Host/BlazorTelemetry.Host.csproj \
    --configuration $BUILD_CONFIGURATION \
    --output /out \
    --no-restore \
    /p:UseAppHost=false

FROM runtime AS final
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
VOLUME ["/data"]
HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 CMD ["curl", "--fail", "http://localhost:8080/health/live"]
ENTRYPOINT ["dotnet", "BlazorTelemetry.Host.dll"]
