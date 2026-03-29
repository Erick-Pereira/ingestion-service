# =========================
# Base
# =========================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# =========================
# Build
# =========================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy all files from ingestion-service repository
COPY . .

# Configure NuGet to use GitHub Packages
RUN dotnet nuget add source https://nuget.pkg.github.com/Erick-Pereira/index.json --name github --username Erick-Pereira --password $GITHUB_TOKEN --store-password-in-clear-text

# ✅ CAMINHO CORRIGIDO - Verificar se a pasta existe
RUN if [ -d "Simcag.IngestionService.Api" ]; then \
      cd Simcag.IngestionService.Api; \
    else \
      echo "Pasta Simcag.IngestionService.Api não encontrada, listando conteúdo:"; \
      ls -la; \
      find . -name "*.csproj" -type f; \
      exit 1; \
    fi

# 🔥 melhor sem hardcode do csproj
RUN dotnet restore
RUN dotnet build -c $BUILD_CONFIGURATION -o /app/build

# =========================
# Publish
# =========================
FROM build AS publish
ARG BUILD_CONFIGURATION=Release

RUN dotnet publish \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

# =========================
# Final
# =========================
FROM base AS final
WORKDIR /app

COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "Simcag.IngestionService.Api.dll"]
