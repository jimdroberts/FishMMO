# FishMMO IpFetchServer Dockerfile
# ================================
#
# Build context must be the repository root (../..), not this directory,
# because the project references FishMMO-Database and FishMMO-SharedUtility
# which live elsewhere in the monorepo.  The docker-compose.yml in the
# Setup directory sets the context correctly.
#
# Example:
#   docker build -t fishmmo/ipfetch-server:latest \
#       -f FishMMO-WebServers/IPFetchASP.NET/IpFetchServer/Dockerfile \
#       ../..
#
# Or via docker-compose (from FishMMO-Setup/):
#   docker compose build ipfetch-server

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
# Copy the entire monorepo needed for project references
COPY . .
WORKDIR /src/IPFetchASP.NET/IpFetchServer
RUN dotnet restore
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:6.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "IpFetchServer.dll"]
