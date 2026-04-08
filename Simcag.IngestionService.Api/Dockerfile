# ============================================================================
# Multi-stage Dockerfile otimizado para .NET 10 Web API
# ============================================================================
# Stage 1: Build & Publish
# ============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

ARG BUILD_CONFIGURATION=Release
ARG TOKEN

# Configure NuGet for GitHub Packages (only used during build)
RUN dotnet nuget add source https://nuget.pkg.github.com/Erick-Pereira/index.json \
    --name github \
    --username Erick-Pereira \
    --password "${TOKEN}" \
    --store-password-in-clear-text

# Copy project files first (for cache efficiency)
COPY ["Simcag.IngestionService.Api/Simcag.IngestionService.Api.csproj", "Simcag.IngestionService.Api/"]
COPY ["Simcag.IngestionService.Application/Simcag.IngestionService.Application.csproj", "Simcag.IngestionService.Application/"]
COPY ["Simcag.IngestionService.Domain/Simcag.IngestionService.Domain.csproj", "Simcag.IngestionService.Domain/"]
COPY ["Simcag.IngestionService.Infrastructure/Simcag.IngestionService.Infrastructure.csproj", "Simcag.IngestionService.Infrastructure/"]
COPY ["../shared/shared.csproj", "../shared/"]

# Restore NuGet packages
RUN dotnet restore "Simcag.IngestionService.Api/Simcag.IngestionService.Api.csproj"

# Copy entire source code
COPY ["Simcag.IngestionService.Api/", "Simcag.IngestionService.Api/"]
COPY ["Simcag.IngestionService.Application/", "Simcag.IngestionService.Application/"]
COPY ["Simcag.IngestionService.Domain/", "Simcag.IngestionService.Domain/"]
COPY ["Simcag.IngestionService.Infrastructure/", "Simcag.IngestionService.Infrastructure/"]
COPY ["../shared/", "../shared/"]

# Publish in Release mode
RUN dotnet publish "Simcag.IngestionService.Api/Simcag.IngestionService.Api.csproj" \
    --configuration ${BUILD_CONFIGURATION} \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ============================================================================
# Stage 2: Runtime (lightweight)
# ============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Set working directory
WORKDIR /app

# Create non-root user for security
RUN groupadd -r appgroup && useradd -r -g appgroup appuser

# Copy published application from builder
COPY --from=builder /app/publish .

# Set ownership to non-root user
RUN chown -R appuser:appgroup /app

# Configure environment
ENV ASPNETCORE_URLS="http://+:8080" \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

# Expose port
EXPOSE 8080

# Switch to non-root user
USER appuser

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD dotnet /app/Simcag.IngestionService.Api.dll --version 2>/dev/null || exit 1

# Start application
ENTRYPOINT ["dotnet", "Simcag.IngestionService.Api.dll"]
