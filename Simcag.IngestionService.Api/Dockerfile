# Multi-stage — contexto: pasta ingestion-service (monorepo) ou repo standalone.
# Simcag.Shared: GitHub Packages (TOKEN + NUGET_GITHUB_OWNER).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

ARG BUILD_CONFIGURATION=Release
ARG TOKEN
ARG NUGET_GITHUB_OWNER

RUN if [ -n "${TOKEN:-}" ] && [ -n "${NUGET_GITHUB_OWNER:-}" ]; then \
    dotnet nuget add source "https://nuget.pkg.github.com/${NUGET_GITHUB_OWNER}/index.json" \
      --name github \
      --username "${NUGET_GITHUB_OWNER}" \
      --password "${TOKEN}" \
      --store-password-in-clear-text; \
  fi

COPY ["Simcag.IngestionService.Api/Simcag.IngestionService.Api.csproj", "Simcag.IngestionService.Api/"]
COPY ["Simcag.IngestionService.Application/Simcag.IngestionService.Application.csproj", "Simcag.IngestionService.Application/"]
COPY ["Simcag.IngestionService.Domain/Simcag.IngestionService.Domain.csproj", "Simcag.IngestionService.Domain/"]
COPY ["Simcag.IngestionService.Infrastructure/Simcag.IngestionService.Infrastructure.csproj", "Simcag.IngestionService.Infrastructure/"]

RUN dotnet restore "Simcag.IngestionService.Api/Simcag.IngestionService.Api.csproj"

COPY . .

RUN dotnet publish "Simcag.IngestionService.Api/Simcag.IngestionService.Api.csproj" \
    --configuration "${BUILD_CONFIGURATION}" \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

RUN groupadd -r appgroup && useradd -r -g appgroup appuser

COPY --from=builder /app/publish .

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && chown -R appuser:appgroup /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

USER appuser

HEALTHCHECK --interval=30s --timeout=8s --start-period=25s --retries=3 \
    CMD sh -c 'p="${ASPNETCORE_HTTP_PORTS:-8080}"; p="${p%%,*}"; curl -fsS "http://127.0.0.1:${p}/health/live" >/dev/null || exit 1'

ENTRYPOINT ["dotnet", "Simcag.IngestionService.Api.dll"]
