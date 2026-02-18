# IPFetchServer

## Overview

ASP.NET Core Web API that provides login server IP address discovery for FishMMO clients. Unity clients query this service to obtain the list of active login servers before connecting. The server reads login server records from PostgreSQL, caches results in memory, and gates access through a custom middleware that rejects non-FishMMO requests.

Designed to run behind NGINX as a reverse proxy (via `api.fishmmo.com`). NGINX terminates SSL and forwards requests over plain HTTP to Kestrel on localhost.

## Architecture

```
Unity Client
    |
    v HTTPS (api.fishmmo.com/loginserver)
+---------+
|  NGINX  |  <- SSL termination, X-Forwarded-For/Proto
+----+----+
     | HTTP (localhost:8080)
+----v------------------------------------+
|  Kestrel (IPFetchServer)               |
|  +-- ForwardedHeaders middleware       |
|  +-- CORS (AllowXFishMMO)              |
|  +-- UnityOnlyMiddleware               |
|  +-- LoginServerController             |
|       +-- PostgreSQL (via Npgsql)      |
|       +-- MemoryCache (300s TTL)       |
+-----------------------------------------+
```

## Directory Structure

```
IpFetchServer/
+-- Program.cs                      # Host builder, Kestrel config, middleware pipeline
+-- Controllers/
|   +-- LoginServerController.cs    # GET /loginserver - returns login server list
+-- UnityOnlyMiddleware.cs          # Rejects requests without X-FishMMO: Client header
+-- appsettings.json                # Port, connection string configuration
```

## Middleware Pipeline

1. **`UseForwardedHeaders`** - trusts `X-Forwarded-For` / `X-Forwarded-Proto` from NGINX.
2. **`UseCors("AllowXFishMMO")`** - allows any origin with `X-FishMMO` header.
3. **`UnityOnlyMiddleware`** - checks `X-FishMMO` header equals `"Client"`. Returns 403 if missing/invalid.
4. **`UseRouting` + `UseAuthorization`** - standard ASP.NET routing.
5. **`MapControllers`** - maps attribute-routed controller endpoints.

## Endpoints

### `GET /loginserver`

Returns JSON array of active login servers with `Address` and `Port` fields.

**Caching:** Results are cached in `IMemoryCache` for 300 seconds (5 minutes). Cache miss triggers a PostgreSQL query via `NpgsqlDbContext.LoginServers`.

**Response Codes:**

| Code | Condition |
|------|-----------|
| 200 | Login servers found (returns `[{Address, Port}, ...]`) |
| 401 | Failed to create DB context |
| 403 | Missing or invalid `X-FishMMO` header (from middleware) |
| 404 | No login servers available |

## Configuration

`appsettings.json`:

```json
{
  "ConnectionStrings": {
    "NpgsqlConnection": "Host=localhost;Port=5432;Database=fishmmo_db;Username=user;Password=pass;"
  },
  "WebServer": {
    "HttpPort": 8080
  }
}
```

| Key | Default | Purpose |
|-----|---------|---------|
| `ConnectionStrings:NpgsqlConnection` | - | PostgreSQL connection string |
| `WebServer:HttpPort` | `8080` | Kestrel listen port (localhost only) |

## Security

- **UnityOnlyMiddleware** rejects all requests that do not include `X-FishMMO: Client` header.
- **CORS policy** (`AllowXFishMMO`) restricts allowed headers to `X-FishMMO`.
- **ForwardedHeaders** ensures correct client IP logging when behind NGINX.
- Kestrel binds to **localhost only** - not directly accessible from the internet.

## External Dependencies

- **Npgsql / Entity Framework Core** - PostgreSQL access via `NpgsqlDbContextFactory`.
- **Microsoft.Extensions.Caching.Memory** - in-memory caching for login server list.
- **FishMMO.Logging** - structured async logging.

## Requirements

- .NET 8.0 SDK or later
- PostgreSQL database with `LoginServers` table
- NGINX reverse proxy (recommended for production)
