# Account System (Server Implementation)

## Overview

The Account system manages the server-side lifecycle of player connections, including encryption key exchange, SRP (Secure Remote Password) authentication state, account-to-connection mappings, and access level tracking. It is split into a Core interface layer (transport-agnostic) and an Implementation layer that binds to FishNet's `NetworkConnection`.

## Directory Structure

```
Server/
├── Core/Account/                              # Transport-agnostic interfaces and data types
│   ├── IAccountManager.cs                     # Generic interface for account/connection management
│   ├── AccountData.cs                         # Access level + SRP data container
│   ├── ConnectionEncryptionData.cs            # Public key, symmetric key, and IV holder
│   └── SRP/
│       ├── ServerSrpData.cs                   # Server-side SRP session (ephemeral, proof, session)
│       └── SrpState.cs                        # Authentication state enum (Verify → Proof → Success)
│
├── Core/Collections/
│   └── ArrivalOrderTracker.cs                 # Shared oldest-first tracking utility for TTL sweeps
│
└── Implementation/Account/                    # FishNet-specific implementation
    └── AccountManager.cs                      # Thread-safe IAccountManager<NetworkConnection>
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
| `connectionAccountData` | `NetworkConnection → AccountData` | Connection → full account data (access level + SRP) |
| `unauthenticatedTracker` | `NetworkConnection` (arrival-ordered) | Oldest-first SRP/encryption stale-state sweep support |

The `connectionAccounts` and `accountConnections` dictionaries form a **bidirectional map**, kept in sync by all add/remove operations.

`unauthenticatedTracker` is backed by shared `ArrivalOrderTracker<NetworkConnection>` to provide O(1) track/untrack and low-GC oldest-first sweeps.

### Thread Safety

All public methods acquire `lock(syncRoot)` before accessing any dictionary. This supports concurrent access from:
- FishNet's main-thread broadcast handlers
- Async worker threads performing database lookups
- Scene server handoff operations

The `TryUpdateSrpState` callback variant executes the callback **inside** the lock, so callbacks must not block or re-enter the `AccountManager`.

## Authentication Flow (SRP)

The SRP (Secure Remote Password) protocol allows password verification without transmitting the password. The flow is managed through `SrpState` transitions:

```
Client                          Server (AccountManager)
  │                                  │
  │── Public Key ──────────────────► │  AddConnectionEncryptionData()
  │                                  │
  │── Auth Request ────────────────► │  AddConnectionAccount()
  │   (username, ephemeral)          │  SrpState = SrpVerify
  │                                  │
  │◄── Server Ephemeral + Salt ──── │  TryUpdateSrpState(SrpVerify → SrpProof)
  │                                  │
  │── Client Proof ────────────────► │  TryUpdateSrpState(SrpProof → SrpSuccess)
  │                                  │  ServerSrpData.GetProof() verifies client
  │◄── Server Proof ─────────────── │
  │                                  │
  │  ═══ Authenticated Session ═══  │
```

### SrpState Enum

| State | Description |
|-------|-------------|
| `SrpVerify` | Initial state — server has received client ephemeral, ready to send server ephemeral |
| `SrpProof` | Server ephemeral sent — awaiting client proof |
| `SrpSuccess` | Client proof verified — session is authenticated |

State transitions are atomic via `TryUpdateSrpState`, which validates the current state matches `requiredState` before advancing to `nextState`.

On transition to `SrpSuccess`, unauthenticated tracking is removed immediately.

## Data Types

### ConnectionEncryptionData

Holds the cryptographic material for a connection:

| Field | Type | Description |
|-------|------|-------------|
| `PublicKey` | `byte[]` | Client's public key for asymmetric encryption |
| `SymmetricKey` | `byte[]` | Generated 256-bit AES key for session encryption |
| `IV` | `byte[]` | Generated 128-bit initialization vector |

Keys are generated via `CryptoHelper.GenerateKey()` when `AddConnectionEncryptionData()` is called.

### AccountData

Holds session-level account information:

| Property | Type | Description |
|----------|------|-------------|
| `AccessLevel` | `AccessLevel` | Permission tier (e.g., Player, GameMaster, Admin) |
| `SrpData` | `ServerSrpData` | SRP authentication state and session data |

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
| `State` | `SrpState` | Current authentication state |

The `GetProof(clientProof, out serverProof)` method verifies the client's proof and derives the session, returning the server's proof on success or an error message on failure.

## Connection Lifecycle

```
1. Client connects
   └── AddConnectionEncryptionData(connection, publicKey)

2. Client sends authentication request
   └── AddConnectionAccount(connection, name, ephemeral, salt, verifier, accessLevel)
       ├── Creates ServerSrpData (generates server ephemeral)
       ├── Creates AccountData (wraps access level + SRP data)
       └── Registers bidirectional connection ↔ account mappings

3. SRP handshake proceeds
   └── TryUpdateSrpState() advances through SrpVerify → SrpProof → SrpSuccess

4. Client disconnects
   └── RemoveConnectionAccount(connection)  OR  RemoveAccountConnection(accountName)
    └── Removes connection encryption/account state first, then bidirectional account maps when available

5. Periodic stale unauthenticated sweep
    └── `SweepUnauthenticatedConnections(maxAge, isAuthenticated, maxScan, maxRemovals)`
         ├── processes oldest tracked entries first
         ├── drops authenticated entries from tracking
         └── purges stale unauthenticated SRP/encryption/account rows
```

## External Dependencies

| Dependency | Purpose |
|------------|---------|
| `FishNet.Connection.NetworkConnection` | FishNet's connection type used as `TConnection` |
| `SecureRemotePassword` | Third-party SRP library (2048-bit parameters, SHA-512) |
| `System.Security.Cryptography` | SHA-512 hash algorithm for SRP parameters |
| `CryptoHelper` | Utility for generating symmetric keys and IVs |
| `AccessLevel` | Enum defining account permission tiers |

## Integration Points

- **Login ServerBehaviour** — Calls `AddConnectionEncryptionData` and `AddConnectionAccount` during the login handshake.
- **Authentication Broadcast Handlers** — Use `TryUpdateSrpState` to advance through the SRP protocol.
- **Scene/World Servers** — Query `GetAccountNameByConnection` and `GetConnectionAccountData` to validate permissions.
- **Disconnect Handling** — Calls `RemoveConnectionAccount` to clean up all mappings on client disconnect.
- **Server Shutdown** — Calls `Clear()` to release all tracked connections and account data.
- **Authentication Backstop Cleanup** — `ServerAuthenticator.Update()` invokes `SweepUnauthenticatedConnections(...)` to bound stale SRP/encryption memory under delayed-disconnect or attack conditions.