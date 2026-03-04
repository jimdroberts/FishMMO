# WorldServer Authentication System

## Overview

The WorldServer Authentication system specializes the shared token-based authenticator flow for world-server entry. After base token authentication succeeds, it enforces world-specific admission rules (server lock state, population limit, selected-character requirement) and returns world-scoped authentication outcomes (`WorldLoginSuccess`, `ServerFull`, etc.).

## Directory Structure

```
Authentication/
├── WorldServerAuthenticator.cs   # World-server-specific post-SRP authentication gate
└── README.md
```

Related shared/auth core pieces:

- `Server/Implementation/Authentication/BaseServerAuthenticator.cs` (shared X25519 ECDH handshake, main-thread queue, TTL sweeps)
- `Server/Implementation/Authentication/TokenServerAuthenticator.cs` (token-based auth pipeline)
- `Server/Core/World/WorldServer/WorldServer/IWorldServerSystemRuntimeData.cs`
- `Server/Core/World/WorldServer/WorldScene/IWorldSceneMappingData.cs`
- `Shared/Implementation/Network/Authentication/ClientAuthenticationResult.cs`

## Inheritance Hierarchy

```
Authenticator (FishNet)
└── BaseServerAuthenticator
    └── TokenServerAuthenticator
        └── WorldServerAuthenticator
```

`WorldServerAuthenticator` overrides `TryLoginAsync(...)` to append world-entry validation after successful token authentication.

## Authentication Flow

### 1) Base authentication (inherited)

`BaseServerAuthenticator` handles the X25519 ECDH key exchange and stale-auth TTL sweeps.
`TokenServerAuthenticator` handles HMAC-signed token verification and account mapping.

On success, the token flow calls:

- `TryLoginAsync(ClientAuthenticationResult.LoginSuccess, username)`

### 2) World-specific gate (`WorldServerAuthenticator.TryLoginAsync`)

`WorldServerAuthenticator` applies checks in order:

1. If incoming result is not `LoginSuccess`, return it unchanged.
2. **Per-account rate limiting** via `ExpiringKeyTracker<string>` — rejects rapid duplicate attempts within a 1-second debounce window, returning `ServerBusy`.
3. If world runtime data indicates server lock (`IsLocked == true`), return `ServerFull`.
4. If world scene mapping count is at or above `MaxPlayers`, return `ServerFull`.
5. Resolve `ICharacterService`; if unavailable, return `ServerBusy`.
6. Verify account has a selected character (`FetchByAccountAsync(username, selected: true)`):
   - DB call failed -> `ServerBusy`
   - selected character exists -> `WorldLoginSuccess`
   - no selected character -> `NoCharacterSelected`

### 3) Periodic sweep

`OnAuthSweep()` invokes `ExpiringKeyTracker.SweepExpired()` to evict stale rate-limit entries and prevent unbounded memory growth under sustained load.

## Admission Rules

| Rule | Source | Outcome |
|------|--------|--------|
| Per-account rate limit | `ExpiringKeyTracker<string>` (1 s debounce) | `ServerBusy` |
| World server is locked | `IWorldServerSystemRuntimeData.IsLocked` | `ServerFull` |
| World server is at capacity | `IWorldSceneMappingData<NetworkConnection>.ConnectionCount >= MaxPlayers` | `ServerFull` |
| Character service unavailable | DB service registry | `ServerBusy` |
| Selected character present | `ICharacterService.FetchByAccountAsync(..., selected: true)` | `WorldLoginSuccess` |
| Selected character missing | Same query | `NoCharacterSelected` |

## Configuration

| Field | Type | Default | Purpose |
|------|------|---------|---------|
| `MaxPlayers` | `uint` | `5000` | Upper bound for concurrent world-server admissions |
| `LoginAttemptDebounceWindow` | `TimeSpan` | `1.0 s` | Per-account rate-limit window for `TryLoginAsync` |
| `SweepMaxScan` | `int` | `128` | Maximum entries to scan per auth sweep cycle |
| `SweepMaxRemove` | `int` | `64` | Maximum entries to remove per auth sweep cycle |

## Runtime Dependencies

`WorldServerAuthenticator` depends on:

- **Data Container Registry**
  - `IWorldServerSystemRuntimeData` (lock flag)
  - `IWorldSceneMappingData<NetworkConnection>` (current connection count)
- **Database Service Registry**
  - `ICharacterService` (selected character verification)
- **Core Collections**
  - `ExpiringKeyTracker<string>` (per-account rate limiting with bounded memory)

## Why this override exists

The shared authenticator can confirm account identity, but world entry must also verify gameplay readiness (selected character) and operational constraints (lock/full state). This class isolates those checks so login and scene authenticators can maintain different policies while reusing the same token pipeline.

Rate-limit entries are tracked via `ExpiringKeyTracker<string>` (instead of a raw `ConcurrentDictionary`) to guarantee bounded memory growth. Expired entries are swept automatically during `OnAuthSweep()`, which runs at the base authenticator's sweep interval.