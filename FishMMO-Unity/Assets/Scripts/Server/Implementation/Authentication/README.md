# Server Authentication Implementation

## Overview

Server-side authentication with a bounded-channel architecture for high-throughput, non-blocking operation. Two authentication modes — **SRP-6a** (LoginServer) and **HMAC-signed token verification** (World/Scene servers) — share a common abstract base class (`BaseServerAuthenticator`) that provides X25519 ECDH key exchange, main-thread marshalling, stale-auth TTL sweeps, and connection lifecycle management. Broadcast handlers act as ultra-fast UDP receiver gates with zero blocking; all heavy crypto, database, and SRP/token work is offloaded to async workers via `System.Threading.Channels`. The system is split into transport-agnostic Core types and FishNet-specific Implementation types.

## Directory Structure

```
Server/
├── Core/Authentication/                       # Transport-agnostic request types and interfaces
│   ├── IAuthenticatorQueueData.cs             # Interface for bounded channels + CTS runtime data
│   ├── SrpVerifyRequest.cs                    # Immutable request struct (encrypted credentials)
│   └── SrpProofRequest.cs                     # Immutable request struct (encrypted proof)
│
├── Core/Account/
│   └── ConnectionEncryptionData.cs            # Per-connection AES keys, nonce counters, sequence tracking
│
├── Core/Collections/                          # Reusable bounded-sweep trackers
│   ├── ExpiringKeyTracker.cs                  # Keyed debounce/rate-limit (head-first expiry queue)
│   ├── LastSeenCacheTracker.cs                # Key/value cache with TTL by last-seen timestamp
│   └── ArrivalOrderTracker.cs                 # Oldest-first tracker for stale-connection sweeps
│
└── Implementation/Authentication/             # FishNet-specific authenticators
    ├── BaseServerAuthenticator.cs             # Abstract base: X25519 handshake, main-thread queue, TTL sweeps, RejectAndPurge
    ├── ServerAuthenticator.cs                 # SRP-6a authenticator (LoginServer)
    └── TokenServerAuthenticator.cs            # Token-based authenticator (World/Scene servers)

Shared/
├── Network/Authentication/
│   └── AuthenticationBroadcasts.cs            # ClientHandshake, ServerHandshake, SrpVerify/Proof/Success, AuthResult
│
└── Tools/Extensions/Crypto/
    └── CryptoHelper.cs                        # AES-GCM, X25519 ECDH, HKDF-SHA256, StrictUtf8, nonce builder
```

## Inheritance Hierarchy

```
Authenticator (FishNet)
└── BaseServerAuthenticator              # X25519 handshake, main-thread queue, stale-auth sweeps, RejectAndPurge
    ├── ServerAuthenticator              # SRP-6a pipeline (LoginServer)
    └── TokenServerAuthenticator         # HMAC-signed token pipeline (World/Scene servers)
        ├── WorldServerAuthenticator     # World-entry gate (player limit, selected character, ExpiringKeyTracker)
        └── SceneServerAuthenticator     # Scene-entry pass-through
```

## Architecture

### Bounded-Channel Pipeline

```
Network Thread (UDP Gates)              Worker Threads (Async)              Main Thread
┌─────────────────────────┐        ┌────────────────────────────┐     ┌──────────────────┐
│ OnSrpVerifyReceived()   │──────► │ ProcessSrpVerifyAsync()    │────►│ DrainMainThread() │
│   • Validate connection │ Verify │   • AES-GCM decrypt user   │ Enq │   • Broadcast()   │
│   • In-flight gate      │Channel │   • StrictUtf8 decode      │ ──► │   • Disconnect()  │
│   • TryWrite(channel)   │        │   • DB: fetch account      │     │   • OnAuthResult() │
│   • Zero blocking       │        │   • SRP state setup        │     └──────────────────┘
└─────────────────────────┘        └────────────────────────────┘
┌─────────────────────────┐        ┌────────────────────────────┐     ┌──────────────────┐
│ OnSrpProofReceived()    │──────► │ ProcessSrpProofAsync()     │────►│ DrainMainThread() │
│   • Validate connection │ Proof  │   • AES-GCM decrypt proof  │ Enq │   • Broadcast()   │
│   • In-flight gate      │Channel │   • SRP proof verification │ ──► │   • Disconnect()  │
│   • TryWrite(channel)   │        │   • TryLoginAsync()        │     │   • OnAuthResult() │
└─────────────────────────┘        └────────────────────────────┘     └──────────────────┘
```

### Thread Model

| Thread | Responsibilities | Blocking Allowed |
|--------|-----------------|------------------|
| **Network Thread** | UDP gate handlers — validate, in-flight gate, enqueue | **No** (zero blocking) |
| **Worker Threads** | AES decryption, StrictUtf8 decode, database I/O, SRP math, ZeroMemory | **Yes** (async/await) |
| **Main Thread** | `Broadcast()`, `Disconnect()`, `OnAuthenticationResult` | N/A (Unity main loop) |

Workers enqueue `Action` delegates into a `ConcurrentQueue<Action>`, drained each frame by `Update()` → `DrainMainThreadQueue()`.

## Security and DoS Hardening

### Connection-level in-flight gating

- `verifyInFlightByClientId` / `proofInFlightByClientId` allow at most one in-flight SRP request per connection per phase.
- Duplicate verify/proof packets from the same `ClientId` are silently dropped while work is in progress.
- Repeated handshake packets are ignored while verify/proof is active.

### Stale authentication TTL (half-open protection)

- `authStartTimeByClientId` tracks when auth began for each connection.
- A periodic sweep disconnects and purges connections that have not completed SRP within 15 seconds.
- Purge clears transient gate state and removes account/SRP data from `AccountManager`.

### Kick-request write debounce

- Kick requests for already-online accounts are rate-limited per account name via `ExpiringKeyTracker<string>`.
- At most one `IKickRequestService.PersistAsync(accountName)` is emitted per 10 seconds per account.
- Head-first expiry queue avoids full-dictionary enumeration during sweeps.

### Upstream verify rate limiting (retry-storm mitigation)

- SRP verify ingress applies lightweight debounce by **IP address** before entering the verify channel.
- Decrypted username path applies lightweight debounce by **account name** before DB lookup.
- Both use `ExpiringKeyTracker<string>` instances for bounded, low-GC sweeps.

### Connection IP cache TTL tracking

- Per-connection IP cache uses `LastSeenCacheTracker<int, string>`.
- Cache entries are touched on read and swept with bounded scan/remove limits.
- Prevents unbounded memory growth under connection churn.

## Cryptographic Hardening

### AES-256-GCM with AAD binding

All AES-GCM operations use Additional Authenticated Data constructed from `(messageType, agreedVersion, sequenceNumber)`. This binds each ciphertext to its semantic context — swapping ciphertext between message types or sequence slots causes immediate GCM tag failure.

### Strict UTF-8 decoding

All `byte[]` → `string` conversions of decrypted data use `CryptoHelper.StrictUtf8` (`new UTF8Encoding(false, true)`). Invalid byte sequences throw `DecoderFallbackException`, triggering immediate `CryptographicOperations.ZeroMemory()` on all decrypted buffers before disconnect.

### Counter-based nonce scheme

```
┌─────────────┬───────────┬────────────┬─────────────┐
│ Prefix (4B) │ Dir (1B)  │ Pad (3B)   │ Counter (4B)│
│ HKDF-       │ 0x00=C→S  │ 0x00 0x00  │ big-endian  │
│ derived     │ 0x01=S→C  │ 0x00       │ uint32      │
└─────────────┴───────────┴────────────┴─────────────┘
```

- **Session prefix**: 4 bytes derived via HKDF-SHA256 from the X25519 shared secret.
- **Direction byte**: Prevents reflection attacks between client→server and server→client.
- **Counter**: Monotonically incremented; throws `CryptographicException` at `uint.MaxValue` to prevent nonce reuse.

### Counter overflow guards

Both `ClientLoginAuthenticator` and `ConnectionEncryptionData` nonce helpers check for `uint.MaxValue` before incrementing, guaranteeing nonce uniqueness within a session.

### Buffer zeroing

Decrypted plaintext buffers (usernames, salt, verifier, ephemeral values, proofs) are zeroed with `CryptographicOperations.ZeroMemory()` immediately after use. Symmetric keys and session prefixes are zeroed on disconnect.

### Credential clearing

The client authenticator nulls `username` and `password` fields immediately after SRP proof derivation and again as a safety net in `ClearKeyMaterial()`.

### RejectAndPurge — Unified failure handling

All failure paths in `ProcessSrpVerifyAsync` and `ProcessSrpProofAsync` use the shared `RejectAndPurge(conn, result)` helper in `BaseServerAuthenticator`:

1. Enqueues a `ClientAuthResultBroadcast` on `Channel.Reliable` to the main thread.
2. Disconnects the client after the broadcast is sent.
3. Purges all transient auth state via `PurgeConnectionAuthState`.

This eliminates DRY violations and prevents information leakage through inconsistent error timing.

### Constant-time comparison

`CryptoHelper.FixedTimeEquals()` delegates to `CryptographicOperations.FixedTimeEquals` for timing-safe byte comparisons.

### Try/catch on all AES operations

Every `CryptoHelper.DecryptAES` / `EncryptAES` call site is wrapped in `try/catch (CryptographicException)` to handle GCM tag failure, counter exhaustion, or malformed ciphertext gracefully — disconnecting the client rather than crashing the worker.

## Authentication Flow

### Phase 1: Key Exchange (Inline — No Channel)

```
Client                              Server (Network Thread)
  │                                      │
  │── ClientHandshake ─────────────────► │  OnServerClientHandshakeReceived()
  │   { PublicKey (X25519) }             │    • AddConnectionEncryptionData()
  │                                      │    • X25519 ECDH + HKDF-SHA256 → directional AES keys + session prefix
  │◄── ServerHandshake ──────────────── │    • HMAC-SHA256 stateless cookie challenge
  │   { ServerPublicKey, Cookie }        │    • Broadcast response
```

Runs inline — pure in-memory crypto with no database or SRP work.

### Phase 2: SRP Verify (Bounded Channel)

```
Client                              Server
  │                                      │
  │── SrpVerifyBroadcast ──────────────► │  UDP Gate → verifyChannel
  │   { Username (enc), Ephemeral (enc)} │
  │                                      │  Worker: ProcessSrpVerifyAsync()
  │                                      │    • AES-GCM decrypt username + ephemeral
  │                                      │    • StrictUtf8 decode (DecoderFallbackException → zero + disconnect)
  │                                      │    • ZeroMemory on decrypted byte arrays
  │                                      │    • DB: check if already online → kick-request debounce
  │                                      │    • DB: fetch salt + verifier
  │                                      │    • SRP state setup + account mapping
  │                                      │    • AES-GCM encrypt salt + server ephemeral
  │◄── SrpVerifyBroadcast ──────────── │    • Enqueue → Main Thread Broadcast
  │   { Salt (enc), Ephemeral (enc) }   │
```

### Phase 3: SRP Proof (Bounded Channel)

```
Client                              Server
  │                                      │
  │── SrpProofBroadcast ──────────────► │  UDP Gate → proofChannel
  │   { ClientProof (encrypted) }       │
  │                                      │  Worker: ProcessSrpProofAsync()
  │                                      │    • AES-GCM decrypt client proof
  │                                      │    • ZeroMemory on decrypted byte array
  │                                      │    • SRP proof verification (constant-time internally)
  │                                      │    • TryLoginAsync() (virtual — subclass-specific logic)
  │                                      │    • AES-GCM encrypt server proof
  │◄── SrpSuccessBroadcast ──────────── │    • Enqueue → Main Thread Broadcast
  │   { ServerProof (enc), Result }     │    • OnAuthentication(conn, true)
```

On failure at any step, workers call `RejectAndPurge(conn, result)` to atomically notify, disconnect, and purge.

## Channel Configuration

| Channel | Capacity | Workers | Drop Policy |
|---------|----------|---------|-------------|
| `verifyChannel` | 500 | 2 | `DropWrite` — excess requests get `ServerBusy` |
| `proofChannel` | 500 | 2 | `DropWrite` — excess requests get `ServerBusy` + disconnect |

Both channels use `SingleReader = false, SingleWriter = false` for concurrent worker and handler access. In-flight gate maps provide per-connection deduplication before channel ingress.

## Broadcast Types

| Broadcast | Direction | Contents |
|-----------|-----------|----------|
| `ClientHandshake` | Client → Server | X25519 public key |
| `ServerHandshake` | Server → Client | X25519 public key + HMAC-SHA256 cookie |
| `SrpVerifyBroadcast` | Bidirectional | Encrypted username/salt + public ephemeral |
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
| `WorldLoginSuccess` | World-server entry approved (world-server gate) |
| `ServerFull` | World server locked or at capacity (world-server gate) |
| `NoCharacterSelected` | No selected character for world entry (world-server gate) |

## Extensibility

`TryLoginAsync` is `internal virtual` on `BaseServerAuthenticator`, returning `Task<ClientAuthenticationResult>`. Subclasses override for server-type-specific admission logic:

- **LoginServer** — Default: returns `LoginSuccess`.
- **WorldServer** — Checks world lock, player cap, selected character via `ExpiringKeyTracker` rate-limited DB query.
- **SceneServer** — Scene-transfer pass-through.

## Cleanup and Lifecycle

| Trigger | Action |
|---------|--------|
| **Client Disconnect** | `OnRemoteConnectionState` purges transient auth state + `AccountManager` data |
| **Worker Shutdown** | `ShutdownWorkers()` → complete channel writers → cancel CTS → drain main-thread queue → clear shared state |
| **Stale Auth Sweep** | Periodic TTL sweep disconnects + purges half-open sessions (15 s) |
| **AccountManager Backstop** | `SweepUnauthenticatedConnections()` with oldest-first tracking purges stale SRP/encryption state |
| **ExpiringKeyTracker Sweep** | `OnAuthSweep()` in subclasses evicts stale debounce/rate-limit entries |

## External Dependencies

| Dependency | Purpose |
|------------|---------|
| `FishNet.Authenticating.Authenticator` | Base class for auth lifecycle |
| `FishNet.Connection.NetworkConnection` | Connection type (`TConnection`) |
| `System.Threading.Channels` | Bounded async producer-consumer queues |
| `SecureRemotePassword` | SRP-6a library (2048-bit group, SHA-512) |
| `CryptoHelper` | AES-256-GCM, X25519 ECDH, HKDF-SHA256, StrictUtf8, constant-time compare |
| `FishMMO.Shared.Authentication` | Centralized validation rules (`IsAllowedUsername`, `IsAllowedPassword`) |
| `IAccountManager<NetworkConnection>` | Thread-safe account/connection/SRP state management |
| `IAccountService` | Database: fetch account salt/verifier for SRP |
| `ICharacterService` | Database: check online characters |
| `IKickRequestService` | Database: persist kick requests for already-online accounts |
| `ExpiringKeyTracker<T>` | Head-first expiry queue for bounded rate limiting |
| `LastSeenCacheTracker<K, V>` | TTL cache with bounded sweep for IP/encryption caches |
| `ArrivalOrderTracker<T>` | Oldest-first tracking for stale-connection sweeps |
| `FishMMO.Logging.Log` | Structured async logging |