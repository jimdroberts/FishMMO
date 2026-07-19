# WebGLServerASP.NET

## Table of Contents

- [Overview](#overview)
- [Supported Platforms](#supported-platforms)
- [Architecture](#architecture)
- [Directory Structure](#directory-structure)
- [Middleware Pipeline](#middleware-pipeline)
- [Key Components](#key-components)
- [Configuration](#configuration)
- [Security](#security)
- [Deployment](#deployment)
- [External Dependencies](#external-dependencies)
- [Requirements](#requirements)
- [Flow Diagram](#flow-diagram)

## Overview

ASP.NET Core static file server purpose-built for serving Unity WebGL builds to browsers. Serves all content from the configured content root (set via `WebClient:ContentRootPath` in appsettings), supports HTTP range requests for efficient streaming of large `.wasm`/`.data` files, and applies security headers (COOP/COEP/CSP) required for Unity 6 WebGL multi-threading (SharedArrayBuffer).

Designed to run behind NGINX as a reverse proxy (via `play.fishmmo.com`). NGINX terminates SSL and forwards requests over plain HTTP to Kestrel on localhost.

## Supported Platforms

| Target | Status |
|---|---|
| .NET 8.0 — Linux | Yes (recommended) |
| .NET 8.0 — Windows | Yes |
| .NET 8.0 — macOS | Yes |

| Client Browser | Notes |
|---|---|
| Chromium / Chrome / Edge | Full support |
| Firefox | Full support |
| Safari | Full support (Range requests required for large `.wasm`) |

| Requirement | Version |
|---|---|
| .NET SDK | 8.0+ |
| Unity WebGL build | Output placed into configured content root |
| NGINX | Recommended for production |

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
|  +-- UseResponseCompression               |
|  +-- MapControllers                       |
+--------------------------------------------+
```

## Directory Structure

```
WebGLServer/
├── Program.cs                      # Host builder, Kestrel config, middleware pipeline
├── appsettings.json                # Port and content root configuration
└── (WebGL build output)            # Unity WebGL build placed in configured content root
```

The content root path is configured via `WebClient:ContentRootPath` in appsettings. For production, the Unity WebGL build output is placed in this directory. The `wwwroot/` convention is not used — the path is explicitly configured.

## Middleware Pipeline

1. **`UseForwardedHeaders`** — trusts `X-Forwarded-For` / `X-Forwarded-Proto` from NGINX.
2. **`UseCors`** — allows cross-origin requests from `play.fishmmo.com` (required for WebGL).
3. **`UseDefaultFiles`** — serves `index.html` for root requests.
4. **`UseStaticFiles`** — serves files from the configured content root with custom MIME type mappings (`.wasm`, `.unityweb`, `.bundle`, `.bin`, `.data`, `.hash`, `.webmanifest`).
5. **`UseResponseCompression`** — compresses wasm/octet-stream/gzip MIME types.
6. **`UseRouting` + `MapControllers`** — standard ASP.NET routing (for health checks and future API endpoints).

**Note:** The legacy `RangeRequestMiddleware` and `UnityOnlyMiddleware` referenced in older documentation no longer exist. ASP.NET Core's built-in `UseStaticFiles` handles range requests natively. The `ClientGate` HMAC middleware used by IPFetch/Patcher is intentionally absent here — browsers cannot add custom headers to static resource requests.

## Key Components

### `Program.cs`

Configures Kestrel (localhost-only binding, port 8000), response compression, security headers (COOP/COEP/CSP for SharedArrayBuffer), and the static file pipeline. Custom MIME type mappings are registered for WebGL-specific extensions.

### Security Headers

The server applies COOP (`same-origin`), COEP (`require-corp`), and a comprehensive CSP to enable Unity 6 WebGL multi-threading via `SharedArrayBuffer`. These headers are essential for modern Unity WebGL builds.

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
2. Copy the build output into the configured content root path.
3. Start the server:
   ```bash
   dotnet run --project WebGLServer/WebGLServer.csproj
   ```
4. Configure NGINX to proxy `play.fishmmo.com` to `localhost:8000`.

## External Dependencies

- **FishMMO.Logging** - structured async logging.

## Requirements

- .NET 8.0 SDK or later
- Unity WebGL build output placed in configured content root
- NGINX reverse proxy (recommended for production)

## Flow Diagram

```mermaid
flowchart LR
    Browser[Browser] -->|"HTTPS play.fishmmo.com"| Nginx[NGINX SSL termination]
    Nginx -->|"HTTP localhost:8000"| Kestrel[Kestrel]
    subgraph Server[WebGLServer]
        Kestrel --> Fwd[ForwardedHeaders]
        Fwd --> Cors[CORS AllowAllOrigins]
        Cors --> Defaults[UseDefaultFiles index.html]
        Defaults --> Static[UseStaticFiles\n(range requests natively supported)]
        Static -->|file found| OK[200 full / 206 partial]
        Static -->|not found| NF[404]
    end
    OK --> Browser
    NF --> Browser
```
