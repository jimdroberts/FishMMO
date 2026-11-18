# World Server Authentication System

**Short description:** World-server-specific authentication gate that enforces admission rules (population cap, selected-character requirement, staff character lock, world lock and scheduled shutdown) after shared token-based authentication succeeds, with per-account rate limiting and bounded memory sweeps.

## Table of Contents

- [Overview](#overview)
- [Supported Platforms](#supported-platforms)
- [Features](#features)
- [Prerequisites](#prerequisites)
- [Installation / Build](#installation--build)
- [Quick Start Guides](#quick-start-guides)
- [Configuration](#configuration)
- [Usage Examples](#usage-examples)
- [Operational Checks](#operational-checks)
- [Flow Diagram](#flow-diagram)
- [Project Structure](#project-structure)
- [License](#license)

## Overview

The WorldServer Authentication system specializes the shared token-based authenticator flow for world-server entry. After base token authentication succeeds, it enforces world-specific admission rules (population limit, selected-character requirement, staff character lock, world lock and scheduled shutdown) and returns world-scoped authentication outcomes (`WorldLoginSuccess`, `ServerFull`, `ServerLocked`, etc.).

The implementation uses a layered execution model:

- **Base layer (`BaseServerAuthenticator`):** X25519 ECDH key exchange, main-thread action queue with time-sliced drain, stale-auth TTL sweeps with hard deadline enforcement, and connection encryption data management.
- **Token layer (`TokenServerAuthenticator`):** HMAC-signed token verification, bounded channel with configurable worker count for async token processing, account mapping, and the `ClientHandshake → ServerHandshake → TokenAuthBroadcast → ClientAuthResultBroadcast` flow.
- **World layer (`WorldServerAuthenticator`):** World-specific admission gate — per-account rate limiting via `ExpiringKeyTracker<string>`, population cap enforcement, character service availability check, one database read of the selected character with its staff lock, then the world lock and scheduled-shutdown checks.

On successful token authentication the token layer calls `TryLoginAsync(ClientAuthenticationResult.LoginSuccess, username)`, which the world layer overrides to apply its admission checks before returning a final `ClientAuthenticationResult`.

## Supported Platforms

| Platform | Supported | Notes |
|---|---|---|
| Windows | Yes | |
| Linux | Yes | |
| WebGL | N/A | Server-only module |
| Unity 6.3 LTS | Yes | Required engine version |
| IL2CPP | Yes | Supported scripting backend |

## Features

- Inherits full X25519 ECDH handshake, main-thread marshalling, stale-auth TTL sweeps, and hard deadline enforcement from `BaseServerAuthenticator`
- Inherits HMAC-signed token verification, bounded async worker channel, and account mapping from `TokenServerAuthenticator`
- Per-account rate limiting via `ExpiringKeyTracker<string>` with a 1-second debounce window on the monotonic clock to prevent repeated expensive database calls. A terminal refusal (full, locked, no character, a failed read) removes the account's entry, so the player is not also debounced for it
- Bounded memory growth guarantee — expired rate-limit entries are swept automatically during `OnAuthSweep()` with configurable scan and removal caps
- World lock and scheduled shutdown, checked once the selected character is known: a scheduled shutdown (`IWorldServerSystemRuntimeData.ShutdownAtUtc` set) refuses everyone, a lock (`IsLocked`) refuses characters at `AccessLevel.Player` and admits staff, both with `ServerLocked` (not `ServerFull`: a locked world is not a busy one). The access level comes from the character row just read, never from the client
- Population cap enforcement: `IWorldSceneMappingData<NetworkConnection>.ConnectionCount` plus the accounts admitted in the last `recentAdmissionWindowSeconds` (30 s) against `MaxPlayers`, so concurrent token workers cannot all pass a check that says one slot is left. The recent admissions are a `FixedWindowCounter<string>` (one window per account, re-admission restarts it) swept head-first before each count, so the count is exact when read
- Character service availability check with graceful `ServerBusy` fallback when the database service registry is unavailable
- Selected-character verification in one read, `ICharacterService.FetchSelectedWithLockAsync(username)`, which returns the selected character with its staff lock state. A failed read answers `ServerBusy` (fail-closed); no selected character, or one locked by staff, answers `NoCharacterSelected` (character select then says who locked it and until when)
- Empty/whitespace username rejection returning `InvalidUsernameOrPassword` before any database or registry access
- Async `TryLoginAsync` override — all admission checks run on the async authentication path without blocking the main thread
- Warning-level logging for rate-limited authentication attempts with account identification

## Prerequisites

- **Unity 6.3 LTS**
- **FishNetworking** — networking framework (provides `Authenticator`, `NetworkConnection`)
- **FishMMO Server Core** — provides `BaseServerAuthenticator`, `TokenServerAuthenticator`, `IWorldServerSystemRuntimeData`, `IWorldSceneMappingData<NetworkConnection>`, `ExpiringKeyTracker<string>`, `MonotonicClock`, and `DataContainerRegistry`
- **FishMMO-Auth** — provides `FixedWindowCounter<TKey>` (`FishMMO.Auth.Core.Collections`) for the recent-admission reservation
- **FishMMO Database** — provides `ICharacterService`, `CharacterData`, `CharacterLockState`, `DatabaseResult<T>`, and `ServiceRegistry`
- **FishMMO Shared** — provides `ClientAuthenticationResult` enum (`LoginSuccess`, `WorldLoginSuccess`, `ServerFull`, `ServerLocked`, `ServerBusy`, `NoCharacterSelected`, `InvalidUsernameOrPassword`)
- **FishMMO Logging** — provides async-safe `Log.Warning`

## Installation / Build

This is an integrated module within FishMMO. It is included as part of the server-side world-server implementation and does not require separate installation. Ensure the FishMMO Server Core and its dependencies are properly configured in your Unity project.

## Quick Start Guides

1. Ensure `WorldServerAuthenticator` is present on the world server GameObject. It inherits from `TokenServerAuthenticator` (which inherits from `BaseServerAuthenticator` → FishNet `Authenticator`), so it automatically participates in the FishNet authentication lifecycle.
2. Set `maxPlayers` in the inspector to the desired concurrent player cap (default: `5000`).
3. Verify that the following data containers are registered in `DataContainerRegistry`:
   - `IWorldServerSystemRuntimeData` — provides the `IsLocked` flag and `ShutdownAtUtc` for the lock and shutdown checks.
   - `IWorldSceneMappingData<NetworkConnection>` — provides `ConnectionCount` for population cap enforcement.
4. Verify that the database `ServiceRegistry` is initialized and `ICharacterService` is registered for selected-character verification.
5. On client connection, the inherited base/token layers handle the ECDH handshake, token verification, and account mapping. If token auth succeeds, `TryLoginAsync` is called with `LoginSuccess` and the account username.
6. `WorldServerAuthenticator.TryLoginAsync` applies admission checks in order: username validation → rate limit → population cap (with recent admissions) → character service availability → selected character and its staff lock (one read) → scheduled shutdown → world lock. The final `ClientAuthenticationResult` is returned to the token layer for broadcast to the client.
7. `OnAuthSweep()` runs every frame with the base authenticator's sweep and evicts expired rate-limit entries and recent admissions.

## Configuration

### Inspector Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `maxPlayers` | `uint` | `5000` | Upper bound for concurrent world-server admissions. When `ConnectionCount` plus recent admissions reaches `MaxPlayers`, new logins receive `ServerFull`. |
| `loginAttemptDebounceSeconds` | `float` | `1.0` | Per-account rate-limit window (seconds) for `TryLoginAsync`. Rapid duplicate attempts within this window are rejected with `ServerBusy`. |
| `sweepMaxScan` | `int` | `128` | Maximum rate-limit entries to scan per auth sweep cycle in `OnAuthSweep()`. |
| `sweepMaxRemove` | `int` | `64` | Maximum entries to remove per auth sweep cycle in `OnAuthSweep()` (rate-limit entries, and closed recent-admission windows). |
| `recentAdmissionWindowSeconds` | `float` | `30.0` | Seconds a recently admitted username still counts against `MaxPlayers`, bounding the read-then-admit burst race. `0` or less turns the reservation off. |

### Admission Rules

| Rule | Source | Outcome |
|---|---|---|
| Empty/whitespace username | `string.IsNullOrWhiteSpace(username)` | `InvalidUsernameOrPassword` |
| Per-account rate limit | `ExpiringKeyTracker<string>` (1 s debounce, monotonic clock) | `ServerBusy` |
| World server is at capacity | `ConnectionCount` + recent admissions `>= MaxPlayers` | `ServerFull` |
| Character service unavailable | `Server.Database?.ServiceRegistry` null or `ICharacterService` unresolvable | `ServerBusy` |
| Database fetch failed | `FetchSelectedWithLockAsync` fails | `ServerBusy` |
| Selected character missing | Same read, no data | `NoCharacterSelected` |
| Selected character locked by staff | The read's lock state is in force | `NoCharacterSelected` |
| Shutdown scheduled | `IWorldServerSystemRuntimeData.ShutdownAtUtc` has a value | `ServerLocked` |
| World locked, player character | `IsLocked` and the character's `AccessLevel <= Player` | `ServerLocked` |
| Otherwise | Recent-admission slot reserved | `WorldLoginSuccess` |

### Runtime Dependencies

| Dependency | Source | Purpose |
|---|---|---|
| `IWorldServerSystemRuntimeData` | `DataContainerRegistry` | World lock flag and scheduled shutdown |
| `IWorldSceneMappingData<NetworkConnection>` | `DataContainerRegistry` | Current connection count |
| `ICharacterService` | `Database.ServiceRegistry` | Selected character and its staff lock, one read |
| `ExpiringKeyTracker<string>` | Internal field | Per-account rate limiting with bounded memory |
| `FixedWindowCounter<string>` | Internal field, created on first use | Recent admissions counted against `MaxPlayers` |

## Usage Examples

### Authentication Flow (Step by Step)

1. A client connects to the world server and the FishNet `Authenticator` lifecycle begins.
2. `BaseServerAuthenticator` performs the X25519 ECDH key exchange, establishing an encrypted channel.
3. `TokenServerAuthenticator` receives the encrypted token, decrypts it, verifies the HMAC signature, and maps the account.
4. On success, `TokenServerAuthenticator` calls `TryLoginAsync(ClientAuthenticationResult.LoginSuccess, username)`.
5. `WorldServerAuthenticator.TryLoginAsync` executes:

```
TryLoginAsync(result, username)
│
├── result != LoginSuccess? → return result unchanged
├── username empty/whitespace? → return InvalidUsernameOrPassword
├── loginAttemptByAccount.TryBegin(username) fails? → return ServerBusy (rate-limited)
├── ConnectionCount + recent admissions >= MaxPlayers? → return ServerFull
├── ICharacterService unavailable? → return ServerBusy
├── FetchSelectedWithLockAsync failed? → return ServerBusy
├── no selected character → return NoCharacterSelected
├── character locked by staff? → return NoCharacterSelected
├── shutdown scheduled? → return ServerLocked
├── world locked and character is a player? → return ServerLocked
└── reserve a recent-admission slot → return WorldLoginSuccess
```

6. The result is broadcast to the client via `ClientAuthResultBroadcast`.

### Rate Limiting Behavior

When a client rapidly retries authentication for the same account within the 1-second debounce window:

- The first attempt proceeds through all admission checks.
- Subsequent attempts within the window are immediately rejected with `ServerBusy`.
- A warning is logged: `"Rate-limited TryLoginAsync for account '{username}'"`.
- After the debounce window expires the next attempt proceeds normally.
- Expired entries are cleaned up by `OnAuthSweep()` which scans up to 128 entries and removes up to 64 per cycle.

### Extending Admission Rules

To add a custom admission rule, override `TryLoginAsync` in a subclass of `WorldServerAuthenticator`:

```csharp
internal override async Task<ClientAuthenticationResult> TryLoginAsync(
    ClientAuthenticationResult result, string username)
{
    result = await base.TryLoginAsync(result, username);
    if (result != ClientAuthenticationResult.WorldLoginSuccess)
        return result;

    // Custom check here
    return result;
}
```

## Operational Checks

| Check | How to Verify |
|---|---|
| Initialization success | Confirm `WorldServerAuthenticator` is attached to the world server GameObject and no errors appear during startup |
| Data containers available | Verify `IWorldServerSystemRuntimeData` and `IWorldSceneMappingData<NetworkConnection>` both resolve from `DataContainerRegistry` |
| Character service available | Verify `ICharacterService` resolves from `Server.Database.ServiceRegistry` |
| Successful world login | Connect with a valid token and a selected character; confirm client receives `WorldLoginSuccess` |
| No selected character | Connect with a valid token but no selected character; confirm client receives `NoCharacterSelected` |
| Server full (population cap) | Fill the server to `MaxPlayers` connections; confirm next login receives `ServerFull` |
| Server locked | Set `IWorldServerSystemRuntimeData.IsLocked = true`; confirm a player character receives `ServerLocked` and a staff character is admitted |
| Shutdown scheduled | Schedule a shutdown for the world; confirm every login, staff included, receives `ServerLocked` |
| Staff character lock | Lock the selected character from the staff console; confirm the login receives `NoCharacterSelected` |
| Rate limiting | Send rapid consecutive login attempts for the same account; confirm excess attempts receive `ServerBusy` and a warning is logged |
| Rate-limit sweep | Wait for auth sweep interval; confirm `ExpiringKeyTracker` entries are evicted (no unbounded memory growth) |
| Empty username rejection | Send a login with empty/whitespace username; confirm `InvalidUsernameOrPassword` is returned |
| Database service unavailable | Disconnect the database; confirm login attempts receive `ServerBusy` |
| Database fetch failure | Simulate a failed `FetchSelectedWithLockAsync`; confirm `ServerBusy` is returned |
| Token auth failure passthrough | Send an invalid token; confirm the base authentication failure result propagates unchanged |
| Auth sweep cycle | Confirm `OnAuthSweep()` runs each frame, invoking the base sweep, `loginAttemptByAccount.SweepExpired()` and the recent-admission sweep |

## Flow Diagram

### High-Level Overview

```mermaid
flowchart LR
    Client[Unity Client] -->|token from LoginServer| WAuth[WorldServer.Authentication]
    WAuth -->|validate token| DB[(PostgreSQL Tokens)]
    WAuth -->|character payload| Scene[Route to SceneServer]
    Scene --> SceneSrv[SceneServer]
```

### Full Authentication Pipeline

```
Client Connection
│
▼
BaseServerAuthenticator
├── X25519 ECDH Key Exchange
├── Encrypted Channel Established
├── Stale-Auth TTL Sweep (periodic)
│
▼
TokenServerAuthenticator
├── Decrypt Token Payload (max 2048 bytes)
├── HMAC Signature Verification
├── Account Mapping
├── Bounded Channel → Async Workers (2 workers, capacity 500)
│
▼
TryLoginAsync(LoginSuccess, username)
│
▼
WorldServerAuthenticator.TryLoginAsync
│
├── 1. Pre-check: result != LoginSuccess → pass through
├── 2. Username validation: empty/whitespace → InvalidUsernameOrPassword
├── 3. Rate limit: ExpiringKeyTracker.TryBegin(username, 1s)
│      └── Blocked → ServerBusy (+ warning log)
├── 4. Population cap: ConnectionCount + recent admissions >= MaxPlayers
│      └── Full → ServerFull
├── 5. Service check: ICharacterService available?
│      └── Unavailable → ServerBusy
├── 6. DB read: FetchSelectedWithLockAsync(username)
│      ├── Fetch failed → ServerBusy
│      ├── No character → NoCharacterSelected
│      └── Locked by staff → NoCharacterSelected
├── 7. Shutdown scheduled → ServerLocked
├── 8. World locked and AccessLevel <= Player → ServerLocked
└── 9. Reserve recent-admission slot → WorldLoginSuccess
│
▼
ClientAuthResultBroadcast → Client
```

### Auth Sweep Lifecycle

```
OnAuthSweep() [periodic, inherited interval]
│
├── base.OnAuthSweep()
│   └── BaseServerAuthenticator stale-connection purge
│
├── loginAttemptByAccount.SweepExpired(MonotonicClock.NowSeconds, 128, 64)
│   └── Evicts expired rate-limit entries (bounded scan + removal)
│
└── RecentAdmissions.SweepExpired(now, 64)
    └── Closed admission windows, oldest first (head-first, stops at the first open one)
```

## Project Structure

### Directory Tree

```
Authentication/
├── WorldServerAuthenticator.cs   # World-server-specific post-token authentication gate
└── README.md                     # This file
```

### Related Files

| File | Purpose |
|---|---|
| `Server/Implementation/Authentication/BaseServerAuthenticator.cs` | Shared X25519 ECDH handshake, main-thread queue, stale-auth TTL sweeps, hard deadline enforcement |
| `Server/Implementation/Authentication/TokenServerAuthenticator.cs` | Token-based auth pipeline: HMAC verification, bounded async channel, account mapping |
| `Server/Core/World/WorldServer/WorldServer/IWorldServerSystemRuntimeData.cs` | Interface providing the `IsLocked` flag and `ShutdownAtUtc` for the lock and shutdown checks |
| `Server/Core/World/WorldServer/WorldScene/IWorldSceneMappingData.cs` | Interface providing `ConnectionCount` for population tracking |
| `FishMMO-Auth/FishMMO-AuthShared/Core/Enums/ClientAuthenticationResult.cs` | Enum defining all authentication result codes (shipped via the `FishMMO-Auth` DLLs) |
| `Server/Core/Collections/ExpiringKeyTracker.cs` | Bounded expiring key collection used for per-account rate limiting |

### Inheritance Hierarchy

```
Authenticator (FishNet)
└── BaseServerAuthenticator
    ├── X25519 ECDH handshake
    ├── Main-thread action queue (time-sliced drain)
    ├── Stale-auth TTL sweeps (hard deadline)
    └── TokenServerAuthenticator
        ├── HMAC-signed token verification
        ├── Bounded async channel (2 workers, capacity 500)
        ├── Account mapping
        └── WorldServerAuthenticator
            ├── Per-account rate limiting (ExpiringKeyTracker, 1s debounce)
            ├── Population cap (with recent admissions) / world lock / shutdown checks
            ├── Selected character + staff lock, one DB read
            └── Bounded auth sweep (128 scan, 64 remove)
```

## License

This module is part of the FishMMO project and is subject to the FishMMO project license. See the repository root for license details.
