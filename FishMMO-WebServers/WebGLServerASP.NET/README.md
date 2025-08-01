# WebGLServerASP.NET

## Overview
WebGLServerASP.NET is an ASP.NET Core web server designed to serve WebGL content and related assets for FishMMO or similar projects. It provides static file serving, range request support (for efficient large file downloads), and is configurable for different environments and database backends.

## Features
- Serves WebGL builds and static assets
- Supports HTTP range requests for efficient streaming/downloads
- Configurable HTTP port
- PostgreSQL (Npgsql) database connection support

## Configuration
Configuration is managed via the `appsettings.json` file. Below are the main options:

### ConnectionStrings
- **NpgsqlConnection**: Connection string for the PostgreSQL database. Example:
  ```json
  "NpgsqlConnection": "Host=localhost;Port=5432;Database=your_database_name;Username=your_username;Password=your_password;"
  ```

### WebServer
- **HttpPort**: The port number the server will listen on. Example:
  ```json
  "HttpPort": 8000
  ```

## Getting Started
1. Clone the repository.
2. Update `appsettings.json` with your database credentials and desired HTTP port.
3. Build and run the project using Visual Studio or the .NET CLI:
   ```powershell
   dotnet build
   dotnet run --project WebGLServer/WebGLServer.csproj
   ```
4. Access the server at `http://localhost:<HttpPort>`.

## Requirements
- .NET 8.0 SDK or later
- PostgreSQL database (if using database features)