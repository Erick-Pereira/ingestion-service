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
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copie apenas os arquivos de projeto para aproveitar o cache do Docker no restore
COPY Simcag.IngestionService.Api/*.csproj ./Simcag.IngestionService.Api/
COPY Simcag.IngestionService.Domain/*.csproj ./Simcag.IngestionService.Domain/
COPY Simcag.IngestionService.Application/*.csproj ./Simcag.IngestionService.Application/
COPY Simcag.IngestionService.Infrastructure/*.csproj ./Simcag.IngestionService.Infrastructure/
COPY Simcag.Shared.Messaging/*.csproj ./Simcag.Shared.Messaging/
# Adicione outros .csproj se existirem

# Restaure as dependências (aproveita cache se csproj não mudou)
RUN dot msgstr "dotnet restore ./Simcag.IngestionService.Api/Simcag.IngestionService.Api.csproj"

# Copie o restante do código
COPY . .

# Build e publish em modo Release, sem apphost para imagens menores
RUN dotnet publish ./Simcag.IngestionService.Api/Simcag.IngestionService.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# =========================
# Final
# =========================
FROM base AS final
WORKDIR /app

COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "Simcag.IngestionService.Api.dll"]
