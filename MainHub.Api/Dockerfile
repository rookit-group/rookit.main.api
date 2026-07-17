# Use the official .NET 10.0 runtime as the base image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5001

# Use the official .NET 10.0 SDK for building
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Copy project files first to leverage Docker layer caching on restore.
# Build context is the solution root, so referenced projects are available.
COPY ["MainHub.Api/MainHub.Api.csproj", "MainHub.Api/"]
COPY ["Shared.Contracts/Shared.Contracts.csproj", "Shared.Contracts/"]
RUN dotnet restore "MainHub.Api/MainHub.Api.csproj"
COPY . .
WORKDIR "/src/MainHub.Api"
RUN dotnet build "MainHub.Api.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "MainHub.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage/image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MainHub.Api.dll"]
