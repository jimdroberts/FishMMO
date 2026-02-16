# Authentication System (Server Implementation)

## Overview

The Authentication system implements SRP-6a (Secure Remote Password) authentication with a bounded-channel architecture designed for high-throughput, non-blocking operation. Broadcast handlers act as ultra-fast UDP receiver gates with zero blocking — all heavy crypto, database, and SRP work is offloaded to async workers via `System.Threading.Channels`. The system is split into transport-agnostic Core types and a FishNet-specific Implementation.

## Directory Structure

```
Server/
├── Core/Authentication/                       # Transport-agnostic request types and interfaces
│   ├── IAuthenticatorQueueData.cs             # Interface for channel + CTS runtime data
│   ├── SrpVerifyRequest.cs                    # Immutable request struct (encrypted credentials)
│   └── SrpProofRequest.cs                     # Immutable request struct (encrypted proof)
│
├── Core/Collections/                          # Reusable queue/index trackers for bounded TTL sweeps
│   ├── ExpiringKeyTracker.cs                  # Keyed debounce/rate-limit tracker (head-first expiry)
│   ├── LastSeenCacheTracker.cs                # Key/value cache tracker with TTL by last-seen
│   └── ArrivalOrderTracker.cs                 # Oldest-first tracker for stale-connection sweeps
│
└── Implementation/Authentication/             # FishNet-specific authenticator
    └── ServerAuthenticator.cs                 # Authenticator with bounded channels and async workers

Shared/Network/Authentication/                 # Broadcast message types (client ↔ server)
    └── AuthenticationBroadcasts.cs            # ClientHandshake, ServerHandshake, SrpVerify/Proof/Success, AuthResult
```

## Architecture

### Bounded Channel Pipeline

```
Network Thread (UDP Gates)              Worker Threads (Async)              Main Thread
┌─────────────────────────┐        ┌────────────────────────────┐     ┌──────────────────┐
│ OnSrpVerifyReceived()   │──────► │ ProcessSrpVerifyAsync()    │────►│ DrainMainThread() │
│   • Validate connection │ Verify │   • AES decrypt username   │ Enq │   • Broadcast()   │
│   • TryWrite(channel)   │Channel │   • DB: fetch account      │ ──► │   • Disconnect()  │
│   • Zero blocking       │        │   • DB: check online       │     │   • OnAuthResult() │
└─────────────────────────┘        │   • SRP state setup        │     └──────────────────┘
                                   └────────────────────────────┘
┌─────────────────────────┐        ┌────────────────────────────┐     ┌──────────────────┐
│ OnSrpProofReceived()    │──────► │ ProcessSrpProofAsync()     │────►│ DrainMainThread() │
│   • Validate connection │ Proof  │   • AES decrypt proof      │ Enq │   • Broadcast()   │
│   • TryWrite(channel)   │Channel │   • SRP proof verification │ ──► │   • Disconnect()  │
│   • Zero blocking       │        │   • TryLoginAsync()        │     │   • OnAuthResult() │
└─────────────────────────┘        └────────────────────────────┘     └──────────────────┘
```

### Thread Model

| Thread | Responsibilities | Blocking Allowed |
|--------|-----------------|------------------|
| **Network Thread** | UDP gate handlers — validate, enqueue | **No** (zero blocking) |
| **Worker Threads** | AES decryption, database I/O, SRP math | **Yes** (async/await) |
| **Main Thread** | `Broadcast()`, `Disconnect()`, `OnAuthenticationResult` | N/A (Unity main loop) |

Workers enqueue `Action` delegates into `mainThreadQueue`, which is drained each frame by `Update()` → `DrainMainThreadQueue()`. The queue uses `ConcurrentQueue<Action>` and lock-free enqueue/dequeue to reduce contention under load.

## Security and DoS Hardening

### Connection-level in-flight gating

- `verifyInFlightByClientId` allows only one in-flight SRP verify request per connection.
- `proofInFlightByClientId` allows only one in-flight SRP proof request per connection.
- Duplicate verify/proof packets from the same `ClientId` are ignored while work is in progress.
- Repeated handshake packets are ignored while verify/proof is active.

### Stale authentication TTL (half-open protection)

- `authStartTimeByClientId` tracks when auth started for each connection.
- A periodic sweep disconnects and purges connections that have not completed SRP within 15 seconds.
- Purge clears transient gate state and removes account/SRP data from `AccountManager`.

### Kick-request write debounce

- Kick requests for already-online accounts are rate-limited per account name.
- At most one `IKickRequestService.PersistAsync(accountName)` is emitted per 10 seconds per account.
- Debounce windows are tracked by shared `ExpiringKeyTracker<string>` for low-GC, head-first cleanup.

### Upstream verify rate limiting (retry-storm mitigation)

- SRP verify ingress applies lightweight debounce by **IP address** before entering the verify channel.
- Decrypted username path applies lightweight debounce by **account name** before DB lookup.
- This reduces worker/DB pressure during reconnect storms where channel `DropWrite` alone is insufficient.
- Debounce maps use shared `ExpiringKeyTracker<string>` instances to avoid large dictionary enumeration during sweeps.

### Connection IP cache TTL tracking

- Per-connection auth IP cache entries use shared `LastSeenCacheTracker<int, string>`.
- Cache entries are touched on read and swept with bounded scan/remove limits.
- This keeps IP-based rate limiting effective without unbounded memory growth under connection churn.

## Authentication Flow

### Phase 1: Key Exchange (Inline — No Channel)

```
Client                              Server (Network Thread)
  │                                      │
  │── ClientHandshake ─────────────────► │  OnServerClientHandshakeReceived()
  │   { PublicKey (RSA) }                │    • AddConnectionEncryptionData()
  │                                      │    • Generate AES key + IV
  │◄── ServerHandshake ──────────────── │    • RSA-encrypt AES key + IV
  │   { Key, IV (RSA-encrypted) }        │    • Broadcast response
```

This phase runs inline because it is pure in-memory crypto — no database or SRP work.

### Phase 2: SRP Verify (Bounded Channel)

```
Client                              Server
  │                                      │
  │── SrpVerifyBroadcast ──────────────► │  UDP Gate → verifyChannel
  │   { S (encrypted), PublicEphemeral } │
  │                                      │  Worker: ProcessSrpVerifyAsync()
  │                                      │    • Decrypt username + ephemeral
  │                                      │    • DB: check if already online
  │                                      │    • DB: fetch salt + verifier
  │                                      │    • AccountManager.AddConnectionAccount()
  │                                      │    • TryUpdateSrpState(Verify → Verify)
  │                                      │    • Encrypt salt + server ephemeral
  │◄── SrpVerifyBroadcast ──────────── │    • Enqueue → Main Thread Broadcast
  │   { S (encrypted), PublicEphemeral } │
```

### Phase 3: SRP Proof (Bounded Channel)

```
Client                              Server
  │                                      │
  │── SrpProofBroadcast ──────────────► │  UDP Gate → proofChannel
  │   { Proof (encrypted) }             │
  │                                      │  Worker: ProcessSrpProofAsync()
  │                                      │    • Decrypt client proof
  │                                      │    • TryUpdateSrpState(Verify → Proof)
  │                                      │    • ServerSrpData.GetProof() validates
  │                                      │    • TryUpdateSrpState(Proof → Success)
  │                                      │    • TryLoginAsync() (virtual)
  │                                      │    • Encrypt server proof
  │◄── SrpSuccessBroadcast ──────────── │    • Enqueue → Main Thread Broadcast
  │   { Proof (encrypted), Result }     │    • OnAuthentication(conn, true)
```

## Channel Configuration

| Channel | Capacity | Workers | Drop Policy |
|---------|----------|---------|-------------|
| `verifyChannel` | 500 | 2 | `DropWrite` — excess requests get `ServerBusy` |
| `proofChannel` | 500 | 2 | `DropWrite` — excess requests get `ServerBusy` + disconnect |

Both channels use `SingleReader = false, SingleWriter = false` to support multiple concurrent workers and broadcast handlers.

In-flight gate maps are separate from channel capacity and provide per-connection deduplication before requests enter workers.

## Broadcast Types

| Broadcast | Direction | Contents |
|-----------|-----------|----------|
| `ClientHandshake` | Client → Server | RSA public key |
| `ServerHandshake` | Server → Client | RSA-encrypted AES key + IV |
| `SrpVerifyBroadcast` | Bidirectional | Encrypted salt + public ephemeral |
| `SrpProofBroadcast` | Client → Server | Encrypted client proof |
| `SrpSuccessBroadcast` | Server → Client | Encrypted server proof + auth result |
| `ClientAuthResultBroadcast` | Server → Client | Authentication result code |

## Authentication Results

| Result | Meaning |
|--------|---------|
| `SrpVerify` | SRP verify phase completed, awaiting proof |
| `LoginSuccess` | Full authentication succeeded |
| `InvalidUsernameOrPassword` | Credentials failed or SRP proof invalid |
| `AlreadyOnline` | Account has an online character (kick request issued) |
| `Banned` | Account is banned |
| `ServerBusy` | Channel full or services unavailable |

## Extensibility

`TryLoginAsync` is a `virtual` method that returns `Task<ClientAuthenticationResult>`. Subclasses override it for server-type-specific logic:

- **LoginServer** — Default implementation returns `LoginSuccess`.
- **WorldServer** — May check player limits, selected character, or world state.
- **SceneServer** — May validate scene-transfer tokens.

## Cleanup

- **Client Disconnect**: `ServerManager_OnRemoteConnectionState` fires when a connection stops, purging transient auth state and account data.
- **Worker Shutdown**: `ShutdownWorkers()` cancels the `CancellationTokenSource`, drains remaining main-thread actions, and nulls channel references.
- **Stale Auth Sweep**: periodic TTL sweep disconnects and purges half-open sessions that never reach SRP success.
- **AccountManager Backstop Sweep**: `Update()` also invokes `AccountManager.SweepUnauthenticatedConnections(...)`, which uses oldest-first tracking to purge stale SRP/encryption state that may outlive delayed disconnect events.

## External Dependencies

| Dependency | Purpose |
|------------|---------|
| `FishNet.Authenticating.Authenticator` | Base class providing `OnAuthenticationResult` and `InitializeOnce` |
| `FishNet.Connection.NetworkConnection` | Connection type used as `TConnection` |
| `System.Threading.Channels` | Bounded async producer-consumer channels |
| `SecureRemotePassword` | SRP-6a protocol library (2048-bit, SHA-512) |
| `CryptoHelper` | AES encrypt/decrypt, RSA public key import, key generation |
| `IAccountManager<NetworkConnection>` | Thread-safe account/connection/SRP state management |
| `ICharacterService` | Database: check if characters are already online |
| `IKickRequestService` | Database: persist kick requests for already-online accounts |
| `IAccountService` | Database: fetch account salt/verifier for SRP |
| `FishMMO.Logging.Log` | Structured async logging |