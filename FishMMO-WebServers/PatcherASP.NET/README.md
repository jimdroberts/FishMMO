# FishMMO Patch Server (ASP.NET)

## Overview

This project is a patch server for the FishMMO game, built with ASP.NET Core. It provides versioned patch delivery and update management for FishMMO clients, with a focus on Unity-based clients. The server exposes endpoints to:

- Retrieve the latest available patch version
- Download patch files for specific versions
- Restrict access to Unity clients only (via middleware)
- Track patch versions and serve them from a configurable directory
- Provide a background heartbeat service for server health

## Features

- **REST API** for patch version and file delivery
- **Unity-only middleware**: Only allows requests from Unity clients
- **Configurable patch directory** and server port
- **Background heartbeat service** for external IP reporting
- **PostgreSQL database support** (via Npgsql)
- **Structured logging**

## Configuration

All configuration is handled via `appsettings.json` in the `Patcher` directory. Key options:

```
{
  "WebServer": {
    "HttpPort": "8090" // Port the server listens on
  },
  "Patches": {
    "DirectoryName": "Patches" // Directory where patch .zip files are stored
  },
  "HeartbeatService": {
    "IntervalSeconds": 60, // Heartbeat interval in seconds
    "ExternalIpServiceUrl": "https://checkip.amazonaws.com/" // Service to check external IP
  },
  "ConnectionStrings": {
    "NpgsqlConnection": "Host=localhost;Port=5432;Username=fishmmo_user;Password=your_password;Database=fishmmo_db"
  }
}
```

### Patch File Naming
Patch files must be named in the format:
```
<from_version>-<to_version>.zip
```
Example: `1.0.0-1.0.1.zip`

### Endpoints
- `GET /latest_version` — Returns the latest patch version
- `GET /{version}` — Returns the patch file for the specified version (if available)

## Running the Server

1. Restore dependencies and build the project:
   ```
   dotnet restore
   dotnet build
   ```
2. Run the server:
   ```
   dotnet run --project Patcher/Patcher.csproj
   ```
3. Configure your Unity client to request patches from this server's address and port.

## Notes
- Only Unity clients (with a valid Unity User-Agent) are allowed to access patch endpoints.
- Ensure the patch directory exists and contains valid patch files.
- Update the database connection string as needed for your environment.