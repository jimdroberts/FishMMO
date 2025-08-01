# IPFetchServer

IPFetchServer is an ASP.NET Core Web API project designed to provide IP address fetching and related services for FishMMO game servers. It exposes endpoints for login and patch servers, and is intended to be used as a backend component for managing server IPs and related configuration in a scalable, secure manner.

## Features
- RESTful API endpoints for server IP management
- Configurable PostgreSQL database connection
- Customizable HTTP port
- Designed for integration with FishMMO infrastructure

## Configuration
Configuration is managed via the `appsettings.json` file. The main options are:

### ConnectionStrings
- **NpgsqlConnection**: The PostgreSQL connection string. Example:
  ```json
  "NpgsqlConnection": "Host=localhost;Port=5432;Database=your_database_name;Username=your_username;Password=your_password;"
  ```

### WebServer
- **HttpPort**: The port the web server listens on. Example:
  ```json
  "WebServer": {
    "HttpPort": 8080
  }
  ```

## Getting Started
1. Clone the repository.
2. Update `appsettings.json` with your PostgreSQL credentials and desired HTTP port.
3. Build and run the project using Visual Studio or the .NET CLI:
   ```powershell
   dotnet build
   dotnet run --project IpFetchServer/IpFetchServer.csproj
   ```
4. The API will be available at `http://localhost:<HttpPort>`.

## Folder Structure
- `Controllers/` - API controllers for login and patch server endpoints
- `UnityOnlyMiddleware.cs` - Custom middleware for Unity integration
- `Program.cs` - Main entry point

## Requirements
- .NET 8.0 SDK or later
- PostgreSQL database