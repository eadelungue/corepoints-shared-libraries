# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files for restore
COPY src/CorePoints.Resilience/CorePoints.Resilience.csproj src/CorePoints.Resilience/
COPY src/CorePoints.Caching/CorePoints.Caching.csproj src/CorePoints.Caching/
COPY src/CorePoints.FeatureToggles/CorePoints.FeatureToggles.csproj src/CorePoints.FeatureToggles/
COPY src/CorePoints.ProductService/CorePoints.ProductService.csproj src/CorePoints.ProductService/
RUN dotnet restore src/CorePoints.ProductService/CorePoints.ProductService.csproj

# Copy everything and build
COPY src/ src/
RUN dotnet publish src/CorePoints.ProductService/CorePoints.ProductService.csproj \
    -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "CorePoints.ProductService.dll"]
