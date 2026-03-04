# Account System (Server Implementation)

## Overview

The Account system manages the server-side lifecycle of player connections, including encryption key exchange, authentication state, account-to-connection mappings, and access level tracking. It is split into a Core interface layer (transport-agnostic) and an Implementation layer that binds to FishNet's `NetworkConnection`.

## Directory Structure

```
Server/
├── Core/Account/                              # Transport-agnostic interfaces and data types
│   ├── IAccountManager.cs                     # Generic interface for account/connection management
│   ├── ISrpAccountManager.cs                  # SRP-specific account management interface
│   ├── ITokenAccountManager.cs                # Token-specific account management interface
│   ├── AccountData.cs                         # Access level + auth state + SRP data container
│   ├── ConnectionEncryptionData.cs            # X25519 public key, directional AES keys, nonce contexts
│   └── SRP/
│       ├── ServerSrpData.cs                   # Server-side SRP session (ephemeral, proof, session)
│       └── AuthState.cs                       # Unified authentication state enum (all server types)
│
├── Core/Collections/
│   └── ArrivalOrderTracker.cs                 # Shared oldest-first tracking utility for TTL sweeps
│
└── Implementation/Account/                    # FishNet-specific implementation
    ├── AccountManager.cs                      # Thread-safe IAccountManager<NetworkConnection>
    ├── SrpAccountManager.cs                   # SRP-specific account management (LoginServer)
    └── TokenAccountManager.cs                 # Token-specific account management (World/Scene)
```

## Architecture

### Core / Implementation Split

```
IAccountManager<TConnection>          (Core — transport-agnostic)
        │
        ▼
AccountManager                        (Implementation — TConnection = NetworkConnection)
    : IAccountManager<NetworkConnection>
```

The `IAccountManager<TConnection>` interface defines all account operations generically. `AccountManager` is the concrete implementation that binds `TConnection` to FishNet's `NetworkConnection`, allowing the Core layer to remain independent of any specific networking library.

### Internal Data Stores

`AccountManager` maintains synchronized connection/account dictionaries and an oldest-first unauthenticated tracker:

| Dictionary | Key → Value | Purpose |
|------------|-------------|---------|
| `connectionEncryptionDatas` | `NetworkConnection → ConnectionEncryptionData` | Encryption keys per connection |
| `connectionAccounts` | `NetworkConnection → string` | Connection → account name lookup |
| `accountConnections` | `string → NetworkConnection` | Account name → connection reverse lookup |
| `connectionAccountData` | `NetworkConnection → AccountData` | Connection → full account data (auth state, access level, SRP) |
| `unauthenticatedTracker` | `NetworkConnection` (arrival-ordered) | Oldest-first stale-state sweep support |

The `connectionAccounts` and `accountConnections` dictionaries form a **bidirectional map**, kept in sync by all add/remove operations.

`connectionAccountData` is created at handshake time (when `AddConnectionEncryptionData` is called) and holds the `AuthState` enum — the **single source of truth** for where a connection sits in the authentication lifecycle.

`unauthenticatedTracker` is backed by shared `ArrivalOrderTracker<NetworkConnection>` to provide O(1) track/untrack and low-GC oldest-first sweeps.

### Thread Safety

All public methods acquire `lock(syncRoot)` before accessing any dictionary. This supports concurrent access from:
- FishNet's main-thread broadcast handlers
- Async worker threads performing database lookups
- Scene server handoff operations

The `TryAdvanceAuthState` callback variant executes the callback **inside** the lock, so callbacks must not block or re-enter the `AccountManager`.

## Authentication Flow (SRP)

The SRP (Secure Remote Password) protocol allows password verification without transmitting the password. The flow is managed through `AuthState` transitions on `AccountData`:

```
Client                          Server (AccountManager)
  │                                  │
  │── Public Key ──────────────────► │  AddConnectionEncryptionData()
  │                                  │  AuthState = Handshake (AccountData created)
  │                                  │
  │── Auth Request ────────────────► │  TryAdvanceAuthState(Handshake → VerifyPending)
  │   (username, ephemeral)          │  Worker: AddConnectionAccount()
  │                                  │          AuthState → WaitingForProof
  │                                  │
  │◄── Server Ephemeral + Salt ──── │  TryAdvanceAuthState(WaitingForProof → WaitingForProof, callback)
  │                                  │
  │── Client Proof ────────────────► │  TryAdvanceAuthState(WaitingForProof → ProofPending)
  │                                  │  Worker: TryAdvanceAuthState(ProofPending → SrpSuccess, callback)
  │                                  │          ServerSrpData.GetProof() verifies client
  │◄── Server Proof ─────────────── │
  │                                  │  TryAdvanceAuthState(SrpSuccess → Authenticated)
  │                                  │  ClearSrpData()
  │  ═══ Authenticated Session ═══  │
```

## Authentication Flow (Token)

Token authentication is used by World/Scene servers for post-login reconnection:

```
Client                          Server (AccountManager)
  │                                  │
  │── Public Key ──────────────────► │  AddConnectionEncryptionData()
  │                                  │  AuthState = Handshake (AccountData created)
  │                                  │
  │── Token ───────────────────────► │  TryAdvanceAuthState(Handshake → TokenPending)
  │                                  │  Worker: validates token against DB
  │                                  │          TryAdvanceAuthState(TokenPending → Authenticated)
  │◄── Auth Result ────────────────  │
  │                                  │
  │  ═══ Authenticated Session ═══  │
```

### AuthState Enum

| State | Value | Description |
|-------|-------|-------------|
| `None` | 0 | Default / cleared — no active authentication |
| `Handshake` | 1 | Public-key exchange complete — AccountData created |
| `VerifyPending` | 2 | SRP verify request accepted — worker in progress |
| `WaitingForProof` | 3 | SRP server ephemeral ready — awaiting client proof |
| `ProofPending` | 4 | SRP proof request accepted — worker in progress |
| `SrpSuccess` | 5 | SRP proof verified — session keys established |
| `TokenPending` | 6 | Token auth request accepted — worker in progress |
| `Authenticated` | 7 | Terminal state — fully authenticated session |

State transitions are atomic via `TryAdvanceAuthState`, which validates the current state matches `requiredState` before advancing to `nextState` (compare-and-swap under lock).

On transition to `SrpSuccess` or `Authenticated`, unauthenticated tracking is removed immediately.

**Note:** Values are explicitly numbered and must not be renumbered — existing logs and diagnostics depend on stable numeric values.

## Data Types

### ConnectionEncryptionData

Holds the cryptographic material for a connection:

| Field | Type | Description |
|-------|------|-------------|
| `PublicKey` | `byte[]` | Client's X25519 public key (32 bytes) |
| `MasterSecret` | `byte[]` | X25519 ECDH + HKDF derived master secret (null after promotion) |
| `ClientToServerKey` | `byte[]` | Directional AES-256 key for decrypting client→server messages |
| `ServerToClientKey` | `byte[]` | Directional AES-256 key for encrypting server→client messages |
| `SendNonceCtx` | `GcmNonceContext` | Server→client nonce context (prefix + counter) |
| `ReceiveNonceCtx` | `GcmNonceContext` | Client→server nonce context (prefix + counter) |
| `AgreedVersion` | `ushort` | Negotiated protocol version for AAD construction |

Directional keys and nonce contexts are null until `PromoteToDirectional()` completes the X25519 ECDH key agreement.

### AccountData

Holds session-level account information and authentication state:

| Property | Type | Description |
|----------|------|-------------|
| `AuthState` | `AuthState` | Current position in the authentication state machine |
| `AccessLevel` | `AccessLevel` | Permission tier (e.g., Player, GameMaster, Admin) |
| `SrpData` | `ServerSrpData` | SRP authentication session data (null for token auth) |

`AccountData` is the **single source of truth** for authentication state. It is created at handshake time with `AuthState.Handshake` and all state transitions go through `TryAdvanceAuthState`. The `Clear()` method resets `AuthState` to `None` and calls `SrpData.Clear()` to null all SRP references.

### ServerSrpData

Manages the server's side of the SRP protocol:

| Property | Type | Description |
|----------|------|-------------|
| `UserName` | `string` | Account username |
| `PublicClientEphemeral` | `string` | Client's public ephemeral value |
| `SrpServer` | `SrpServer` | SRP protocol handler (2048-bit, SHA-512) |
| `Salt` | `string` | Password salt from database |
| `Verifier` | `string` | Password verifier from database |
| `ServerEphemeral` | `SrpEphemeral` | Server's generated ephemeral values |
| `Session` | `SrpSession` | Derived session after successful proof verification |

The `GetProof(clientProof, out serverProof)` method verifies the client's proof and derives the session, returning the server's proof on success or an error message on failure.

The `Clear()` method nulls all property references. Note: the underlying `string` values are .NET immutable strings and cannot be deterministically zeroed from memory — they remain eligible for GC collection. The `SecureRemotePassword` third-party library requires `string` parameters, preventing a move to `byte[]`.

## Connection Lifecycle

```
1. Client connects
   └── AddConnectionEncryptionData(connection, publicKey)
       └── Creates AccountData with AuthState.Handshake

2a. SRP flow: Client sends authentication request
    └── TryAdvanceAuthState(Handshake → VerifyPending)
    └── Worker: AddConnectionAccount(connection, name, ephemeral, salt, verifier, accessLevel)
        ├── Creates ServerSrpData (generates server ephemeral)
        ├── Populates existing AccountData via SetSrpData()
        ├── Registers bidirectional connection ↔ account mappings
        └── Advances AuthState to WaitingForProof

2b. Token flow: Client sends token
    └── TryAdvanceAuthState(Handshake → TokenPending)
    └── Worker: validates token, TryAdvanceAuthState(TokenPending → Authenticated)

3. SRP handshake proceeds (SRP flow only)
   └── TryAdvanceAuthState() advances through WaitingForProof → ProofPending → SrpSuccess → Authenticated

4. Client disconnects
   └── RemoveConnectionAccount(connection)  OR  RemoveAccountConnection(accountName)
       ├── Calls AccountData.Clear() (resets AuthState, nulls SRP references)
       └── Removes connection encryption/account state, then bidirectional account maps

5. Periodic stale unauthenticated sweep
    └── `SweepUnauthenticatedConnections(maxAge, isAuthenticated, maxScan, maxRemovals)`
         ├── processes oldest tracked entries first
         ├── drops authenticated entries from tracking
         └── purges stale unauthenticated encryption/account rows (calls AccountData.Clear())
```

## External Dependencies

| Dependency | Purpose |
|------------|---------|
| `FishNet.Connection.NetworkConnection` | FishNet's connection type used as `TConnection` |
| `SecureRemotePassword` | Third-party SRP library (2048-bit parameters, SHA-512) |
| `System.Security.Cryptography` | SHA-512 hash algorithm for SRP parameters |
| `CryptoHelper` | X25519 ECDH + HKDF-SHA256 key derivation, AES-GCM, nonce builder |
| `AccessLevel` | Enum defining account permission tiers |

## Integration Points

- **Login ServerBehaviour** — Calls `AddConnectionEncryptionData` (creates `AccountData` with `AuthState.Handshake`) and drives SRP via `TryAdvanceAuthState`.
- **ServerAuthenticator (SRP)** — Uses `TryAdvanceAuthState` to advance through `Handshake → VerifyPending → WaitingForProof → ProofPending → SrpSuccess → Authenticated`. No separate in-flight dictionaries — `AuthState` is the single source of truth.
- **TokenServerAuthenticator** — Uses `TryAdvanceAuthState` for the simpler `Handshake → TokenPending → Authenticated` flow.
- **Scene/World Servers** — Query `GetAccountNameByConnection` and `GetConnectionAccountData` to validate permissions.
- **Disconnect Handling** — Calls `RemoveConnectionAccount` to clean up all mappings on client disconnect (calls `AccountData.Clear()`).
- **Server Shutdown** — Calls `Clear()` to release all tracked connections and account data.
- **Authentication Backstop Cleanup** — `ServerAuthenticator.Update()` invokes `SweepUnauthenticatedConnections(...)` to bound stale encryption memory under delayed-disconnect or attack conditions.