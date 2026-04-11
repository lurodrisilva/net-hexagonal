# Stage 1: Restore
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /app

COPY global.json .
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY Hex.Scaffold.slnx .

COPY src/Hex.Scaffold.Domain/Hex.Scaffold.Domain.csproj src/Hex.Scaffold.Domain/
COPY src/Hex.Scaffold.Application/Hex.Scaffold.Application.csproj src/Hex.Scaffold.Application/
COPY src/Hex.Scaffold.Adapters.Inbound/Hex.Scaffold.Adapters.Inbound.csproj src/Hex.Scaffold.Adapters.Inbound/
COPY src/Hex.Scaffold.Adapters.Outbound/Hex.Scaffold.Adapters.Outbound.csproj src/Hex.Scaffold.Adapters.Outbound/
COPY src/Hex.Scaffold.Adapters.Persistence/Hex.Scaffold.Adapters.Persistence.csproj src/Hex.Scaffold.Adapters.Persistence/
COPY src/Hex.Scaffold.Api/Hex.Scaffold.Api.csproj src/Hex.Scaffold.Api/

RUN dotnet restore src/Hex.Scaffold.Api/Hex.Scaffold.Api.csproj

# Stage 2: Build
FROM restore AS build
COPY src/ src/
RUN dotnet build src/Hex.Scaffold.Api/Hex.Scaffold.Api.csproj -c Release --no-restore

# Stage 3: Publish
FROM build AS publish
RUN dotnet publish src/Hex.Scaffold.Api/Hex.Scaffold.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-build

# Stage 4: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "Hex.Scaffold.Api.dll"]
