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

# Copy shared project (caminho relativo à raiz do repositório)
COPY shared/ ./shared/

# Copy ingestion-service projects (caminho relativo à raiz do repositório)
COPY ingestion-service/ ./ingestion-service/

# ✅ CAMINHO CORRIGIDO
WORKDIR /src/ingestion-service/Simcag.IngestionService.Api

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
