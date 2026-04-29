# Build context: raiz do repositório (compartilha /shared; sem NuGet token)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["shared/shared.csproj", "shared/"]
COPY ["ingestion-service/Simcag.IngestionService.Api/Simcag.IngestionService.Api.csproj", "ingestion-service/Simcag.IngestionService.Api/"]
COPY ["ingestion-service/Simcag.IngestionService.Application/Simcag.IngestionService.Application.csproj", "ingestion-service/Simcag.IngestionService.Application/"]
COPY ["ingestion-service/Simcag.IngestionService.Domain/Simcag.IngestionService.Domain.csproj", "ingestion-service/Simcag.IngestionService.Domain/"]
COPY ["ingestion-service/Simcag.IngestionService.Infrastructure/Simcag.IngestionService.Infrastructure.csproj", "ingestion-service/Simcag.IngestionService.Infrastructure/"]
RUN dotnet restore "ingestion-service/Simcag.IngestionService.Api/Simcag.IngestionService.Api.csproj"
COPY shared/ shared/
COPY ingestion-service/ ingestion-service/
RUN dotnet publish "ingestion-service/Simcag.IngestionService.Api/Simcag.IngestionService.Api.csproj" -c ${BUILD_CONFIGURATION} -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Simcag.IngestionService.Api.dll"]
