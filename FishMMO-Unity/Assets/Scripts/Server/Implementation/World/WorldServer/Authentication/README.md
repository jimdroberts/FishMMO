# WorldServer Authentication System

## Overview

The WorldServer Authentication system specializes the shared server authenticator flow for world-server entry. After base SRP authentication succeeds, it enforces world-specific admission rules (server lock state, population limit, selected-character requirement) and returns world-scoped authentication outcomes (`WorldLoginSuccess`, `ServerFull`, etc.).

## Directory Structure

```
Authentication/
├── WorldServerAuthenticator.cs   # World-server-specific post-SRP authentication gate
└── README.md
```

Related shared/auth core pieces:

- `Server/Implementation/Authentication/ServerAuthenticator.cs` (base SRP pipeline)
- `Server/Core/World/WorldServer/WorldServer/IWorldServerSystemRuntimeData.cs`
- `Server/Core/World/WorldServer/WorldScene/IWorldSceneMappingData.cs`
- `Shared/Network/Authentication/ClientAuthenticationResult.cs`

## Inheritance Hierarchy

```
Authenticator (FishNet)
└── ServerAuthenticator
    └── WorldServerAuthenticator
```

`WorldServerAuthenticator` overrides `TryLoginAsync(...)` to append world-entry validation after successful base login.

## Authentication Flow

### 1) Base authentication (inherited)

`ServerAuthenticator` handles:

- handshake key exchange
- SRP verify/proof flow
- account/session state transitions

On success, base flow calls:

- `TryLoginAsync(ClientAuthenticationResult.LoginSuccess, username)`

### 2) World-specific gate (`WorldServerAuthenticator.TryLoginAsync`)

`WorldServerAuthenticator` applies checks in order:

1. If incoming result is not `LoginSuccess`, return it unchanged.
2. If world runtime data indicates server lock (`IsLocked == true`), return `ServerFull`.
3. If world scene mapping count is at or above `MaxPlayers`, return `ServerFull`.
4. Resolve `ICharacterService`; if unavailable, return `ServerBusy`.
5. Verify account has a selected character (`FetchByAccountAsync(username, selected: true)`):
   - DB call failed -> `ServerBusy`
   - selected character exists -> `WorldLoginSuccess`
   - no selected character -> `InvalidUsernameOrPassword`

## Admission Rules

| Rule | Source | Outcome |
|------|--------|---------|
| World server is locked | `IWorldServerSystemRuntimeData.IsLocked` | `ServerFull` |
| World server is at capacity | `IWorldSceneMappingData<NetworkConnection>.ConnectionCount >= MaxPlayers` | `ServerFull` |
| Character service unavailable | DB service registry | `ServerBusy` |
| Selected character present | `ICharacterService.FetchByAccountAsync(..., selected: true)` | `WorldLoginSuccess` |
| Selected character missing | Same query | `InvalidUsernameOrPassword` |

## Configuration

| Field | Type | Default | Purpose |
|------|------|---------|---------|
| `MaxPlayers` | `uint` | `5000` | Upper bound for concurrent world-server admissions |

## Runtime Dependencies

`WorldServerAuthenticator` depends on:

- **Data Container Registry**
  - `IWorldServerSystemRuntimeData` (lock flag)
  - `IWorldSceneMappingData<NetworkConnection>` (current connection count)
- **Database Service Registry**
  - `ICharacterService` (selected character verification)

## Why this override exists

The shared authenticator can confirm account identity, but world entry must also verify gameplay readiness (selected character) and operational constraints (lock/full state). This class isolates those checks so login and scene authenticators can maintain different policies while reusing the same SRP pipeline.