# WebGLServerASP.NET

## Overview

ASP.NET Core static file server purpose-built for serving Unity WebGL builds to browsers. Serves all content from `wwwroot/`, supports HTTP range requests for efficient streaming of large `.wasm`/`.data` files, and applies permissive CORS headers for cross-origin WebGL loading.

Designed to run behind NGINX as a reverse proxy (via `play.fishmmo.com`). NGINX terminates SSL and forwards requests over plain HTTP to Kestrel on localhost.

## Architecture

```
Browser
    |
    v HTTPS (play.fishmmo.com)
+---------+
|  NGINX  |  <- SSL termination, X-Forwarded-For/Proto
+----+----+
     | HTTP (localhost:8000)
+----v---------------------------------------+
|  Kestrel (WebGLServer)                    |
|  +-- ForwardedHeaders middleware          |
|  +-- CORS (AllowAllOrigins)               |
|  +-- UseDefaultFiles + UseStaticFiles     |
|  +-- RangeRequestMiddleware               |
|  +-- MapControllers                       |
|       +-- wwwroot/ (WebGL build)          |
+--------------------------------------------+
```

## Directory Structure

```
WebGLServer/
+-- Program.cs                      # Host builder, Kestrel config, middleware pipeline
+-- RangeRequestMiddleware.cs       # HTTP range request handler for partial content delivery
-- appsettings.json                # Port configuration
+-- wwwroot/                        # Unity WebGL build output (static files served from here)
    +-- index.html
    +-- Build/
    |   +-- *.wasm
    |   +-- *.data
    |   +-- *.js
    +-- TemplateData/
```

## Middleware Pipeline

1. **`UseForwardedHeaders`** - trusts `X-Forwarded-For` / `X-Forwarded-Proto` from NGINX.
2. **`UseCors("AllowAllOrigins")`** - allows any origin, method, and header (required for WebGL cross-origin requests).
3. **`UseDefaultFiles`** - serves `index.html` for root requests.
4. **`UseStaticFiles`** - serves files from `wwwroot/` with default content type mappings.
5. **`RangeRequestMiddleware`** - handles HTTP `Range` header for partial content delivery.
6. **`UseRouting` + `MapControllers`** - standard ASP.NET routing (for any future API endpoints).

## Key Components

### `RangeRequestMiddleware`

Custom middleware that handles HTTP range requests for efficient streaming of large WebGL assets:

**Behavior:**
1. Resolves requested path to a file in `wwwroot/`.
2. Sets `Accept-Ranges: bytes` response header.
3. If `Range` header is present (e.g., `bytes=0-1023`):
   - Parses start/end byte positions.
   - Returns `206 Partial Content` with `Content-Range` header.
   - Streams only the requested byte range.
4. If no `Range` header: serves full file with appropriate content type.

**Supported Content Types:**

| Extension | Content-Type |
|-----------|-------------|
| `.html` | `text/html` |
| `.js` | `application/javascript` |
| `.json` | `application/json` |
| `.wasm` | `application/wasm` |
| `.css` | `text/css` |
| `.png` | `image/png` |
| `.jpg` | `image/jpeg` |
| `.gif` | `image/gif` |
| `.unityweb` | `application/octet-stream` |
| `.bin` | `application/octet-stream` |
| `.bundle` | `application/octet-stream` |
| `.hash` | `text/plain` |
| Other | `application/octet-stream` |

**Error Handling:**

| Code | Condition |
|------|-----------|
| 200 | Full file served |
| 206 | Partial content (range request) |
| 404 | File not found |
| 416 | Range not satisfiable (out of bounds) |

## Configuration

`appsettings.json`:

```json
{
  "WebServer": {
    "HttpPort": 8000
  }
}
```

| Key | Default | Purpose |
|-----|---------|---------|
| `WebServer:HttpPort` | `8000` | Kestrel listen port (localhost only) |

## Security

- **CORS** (`AllowAllOrigins`) is intentionally permissive - WebGL builds require cross-origin access for `.wasm` and `.data` files.
- **ForwardedHeaders** ensures correct client IP logging when behind NGINX.
- Kestrel binds to **localhost only** - not directly accessible from the internet.
- No authentication middleware - static content is publicly served (access control is at the NGINX layer).

## Deployment

1. Build the Unity WebGL project.
2. Copy the build output into `wwwroot/`.
3. Start the server:
   ```bash
   dotnet run --project WebGLServer/WebGLServer.csproj
   ```
4. Configure NGINX to proxy `play.fishmmo.com` to `localhost:8000`.

## External Dependencies

- **FishMMO.Logging** - structured async logging.

## Requirements

- .NET 8.0 SDK or later
- Unity WebGL build output in `wwwroot/`
- NGINX reverse proxy (recommended for production)
