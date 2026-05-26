# Account Creation System

**Short description:** Asynchronous, rate-limited login-server pipeline for creating new player accounts without blocking the network thread, using AES-encrypted SRP credentials, per-IP DoS protection, and bounded async workers with main-thread response marshalling.

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

The Account Creation system is an asynchronous, rate-limited login-server pipeline for creating new accounts without blocking the network thread. Incoming `CreateAccountBroadcast` messages are treated as ultra-fast UDP gate events: encrypted payloads are validated and queued immediately on the network thread, while AES decryption, SRP credential conversion, username validation, and database persistence are executed by background `AsyncWorkerData` workers. Responses are marshalled back to the main Unity thread through a dedicated `AccountCreationSystemMainThreadQueueData` queue container to preserve FishNet thread-safety.

Main-thread response dispatch is time-sliced by `maxMainThreadResponsesPerFrame` to avoid frame spikes under heavy ingress. The system is fully stateless — all mutable state lives in `RuntimeDataContainer` instances managed by the `DataContainerRegistry`, ensuring each system gets its own isolated data.

The request pipeline follows four stages:

1. **Network Thread (UDP Gate)** — validates connection encryption data, rejects oversized payloads, captures client IP, creates an immutable `AccountCreationRequest<NetworkConnection>`, and enqueues via bounded channel with backpressure. No decryption or DB I/O occurs here.
2. **Queue + Backpressure** — requests are dispatched through centralized `AsyncWorkerData` with bounded channels and optional entity-key routing (`ClientId`) for consistent worker affinity. Immediate enqueue failure returns `ServerBusy`.
3. **Worker Threads** — AES-decrypt username/salt/verifier using per-field sequence-derived nonces, convert bytes to strings via `CryptoHelper.StrictUtf8` (throws `DecoderFallbackException` on malformed UTF-8), zero decrypted byte arrays with `CryptographicOperations.ZeroMemory()`, validate username against centralized `Authentication.IsAllowedUsername()` rules, validate salt/verifier length limits, persist via `IAccountService.PersistAsync()`, update runtime metrics and per-IP failure tracking.
4. **Main Thread Response** — `OnUpdate` drains queued actions through `Drain(maxMainThreadResponsesPerFrame)` and sends FishNet `ClientAuthResultBroadcast` on the main thread. On shutdown, remaining queued responses are fully drained.

## Supported Platforms

| Platform | Supported | Notes |
|----------|-----------|-------|
| Windows  | Yes       | Server runtime |
| Linux    | Yes       | Server runtime |
| WebGL    | N/A       | Server-only system — not applicable to client builds |

| Requirement      | Version / Detail |
|------------------|------------------|
| Unity            | 6.3 LTS          |
| Scripting Backend| IL2CPP           |

## Features

- **Zero-blocking network thread** — encrypted payloads are validated and queued on the network thread with no decryption or I/O
- **AES-GCM encryption** — per-field sequence-derived nonces for username, salt, and verifier with AAD binding to `AuthMessageType.CreateAccount`
- **SRP (Secure Remote Password) protocol** — credentials stored as salt + verifier; plaintext passwords never reach the server
- **Per-IP rate limiting** — configurable `ipRateLimitSeconds` cooldown between attempts from the same IP using atomic `ConcurrentDictionary.AddOrUpdate` (TOCTOU-safe)
- **Per-IP failure tracking and DoS blocking** — IPs exceeding `maxFailedAttempts` are temporarily blocked and immediately disconnected
- **Proxy / NAT / load balancer compatibility** — optional `useConnectionIdForRateLimit` mode switches rate-limiting key from IP to connection ID
- **Bounded async worker backpressure** — drop-on-full channel prevents unbounded queue growth under attack
- **Main-thread time-slicing** — configurable `maxMainThreadResponsesPerFrame` prevents frame spikes during login waves
- **Automatic memory hygiene** — `CryptographicOperations.ZeroMemory()` scrubs decrypted byte arrays immediately after use (or on failure)
- **Strict UTF-8 validation** — `CryptoHelper.StrictUtf8` with `DecoderFallbackException` rejects malformed payloads
- **Username validation** — centralized `Authentication.IsAllowedUsername()` check before any DB call
- **Salt / verifier length validation** — `MaxSaltLength` (256) and `MaxVerifierLength` (1024) enforced before persistence
- **Encrypted field size guard** — `MaxEncryptedFieldSize` (2048 bytes) rejects oversized payloads on the network thread
- **Periodic stale-entry cleanup** — every 60 seconds, bounded sweeps evict expired rate-limit and failure entries with configurable scan/removal caps
- **Per-connection caching** — `LastSeenCacheTracker` caches IP addresses and encryption data to reduce lock pressure on `AccountManager`
- **Thread-safe runtime metrics** — `Interlocked`-backed counters for `TotalProcessed`, `TotalRejected`, `TotalFailed`
- **Database error mapping** — `UniqueViolation` and `ValidationError` mapped to `InvalidUsernameOrPassword`; other errors map to `ServerBusy`
- **Graceful shutdown** — full queue drain on deinitialize ensures clients receive final responses
- **Stateless behaviour** — all mutable state in `RuntimeDataContainer` instances; system logic is pure and testable
- **Engine-agnostic core** — interface/implementation split with generic `TConnection` parameter
- **Account verification** — encrypted verification code flow via `AccountVerifyBroadcast`; validates codes against database before marking accounts as verified
- **Per-username verification brute-force protection** — failed verification attempts tracked per username (lowercased). After 10 failures within 30 minutes, further attempts are rejected until the lockout expires. Bounded sweep (64 max scan) evicts stale entries. Hard cap of 50,000 tracked entries prevents memory exhaustion.
- **Mandatory 2FA setup** — account creation generates a TOTP secret (encrypted at rest with the server-side master key), recovery codes (PBKDF2-SHA256 hashed), and delivers the otpauth URI and plaintext recovery codes to the client via AES-encrypted transport

## Prerequisites

- FishNet networking framework (provides `NetworkConnection`, `IBroadcast`, `ServerManager`)
- `AsyncWorkerData` runtime data container registered in the `DataContainerRegistry`
- `AccountManager` providing per-connection AES key/IV via `GetConnectionEncryptionData()`
- Database layer with `IAccountService` registered in the `Database.ServiceRegistry` (Npgsql-backed)
- `CryptoHelper` shared utility for AES decrypt, strict UTF-8 encoding, and AAD construction
- `Authentication` shared utility providing `IsAllowedUsername()` validation

## Installation / Build

This is an integrated module within the FishMMO server architecture. No separate installation is required.

1. The `AccountCreationSystem` ScriptableObject is created via **Assets → Create → FishMMO → Server → LoginServer → Account Creation System**.
2. Add the asset to the Login Server's `ServerBehaviour` list.
3. Ensure the following `[RequiresDataContainer]` dependencies are registered in the `DataContainerRegistry`:
   - `AsyncWorkerData`
   - `AccountCreationSystemRuntimeData`
   - `AccountCreationSystemMappingData`
   - `AccountCreationSystemMainThreadQueueData`

## Quick Start Guides

### Creating the System Asset

1. In the Unity Editor, right-click in the Project window.
2. Select **Create → FishMMO → Server → LoginServer → Account Creation System**.
3. Name the asset `AccountCreationSystem`.
4. Assign it to the Login Server's behaviour list.

### Tuning for Production

1. Set `ipRateLimitSeconds` to a value appropriate for expected registration traffic (default: `5.0`).
2. Set `maxFailedAttempts` to limit brute-force attempts (default: `5`).
3. Set `ipBlockDurationSeconds` for how long blocked IPs remain blocked (default: `300` — 5 minutes).
4. Set `maxMainThreadResponsesPerFrame` based on server frame budget (default: `100`).
5. If behind a proxy/NAT/load balancer, enable `useConnectionIdForRateLimit`.

### Monitoring at Runtime

Query the public behaviour properties to monitor system health:

- `PendingRequestCount` — current async queue depth
- `TotalProcessed` — successful account creations since start
- `TotalRejected` — rate-limited or backpressure-rejected requests since start

## Configuration

### Inspector Fields

| Field | Type | Default | Header | Description |
|-------|------|---------|--------|-------------|
| `ipRateLimitSeconds` | `float` | `5.0` | Rate Limiting | Minimum seconds between account creation attempts from the same IP address |
| `maxFailedAttempts` | `int` | `5` | Rate Limiting | Maximum failed attempts allowed before an IP is temporarily blocked |
| `ipBlockDurationSeconds` | `float` | `300.0` | Rate Limiting | Duration in seconds that an IP remains blocked after exceeding the failed-attempt threshold (5 minutes) |
| `maxMainThreadResponsesPerFrame` | `int` | `100` | Main Thread Dispatch | Maximum number of queued main-thread response actions processed per frame |
| `cleanupMaxScanPerMap` | `int` | `256` | Cleanup Bounds | Maximum entries scanned per map during one maintenance sweep |
| `cleanupMaxRemovalsPerMap` | `int` | `128` | Cleanup Bounds | Maximum entries removed per map during one maintenance sweep |
| `useConnectionIdForRateLimit` | `bool` | `false` | Proxy Compatibility | Use connection ID instead of IP for rate limiting; enable when behind a proxy/NAT/load balancer where all clients share one IP |

All tunables are clamped to safe minimums during `InitializeOnce()`:

- `ipRateLimitSeconds` → `max(0, value)`
- `maxFailedAttempts` → `max(1, value)`
- `ipBlockDurationSeconds` → `max(1, value)`
- `maxMainThreadResponsesPerFrame` → `max(1, value)`
- `cleanupMaxScanPerMap` → `max(1, value)`
- `cleanupMaxRemovalsPerMap` → `max(1, value)`

### Compile-Time Constants

| Constant | Value | Purpose |
|----------|-------|---------|
| `MaxEncryptedFieldSize` | `2048` bytes | Rejects oversized encrypted payloads on the network thread before any decryption or allocation |
| `MaxSaltLength` | `256` chars | Maximum allowed length for the decrypted SRP salt string |
| `MaxVerifierLength` | `1024` chars | Maximum allowed length for the decrypted SRP verifier string |
| `MaxVerifyFailuresPerUsername` | `10` | Maximum failed verification attempts per username before lockout |
| `VerifyUsernameLockoutDuration` | `30` min | Lockout window for per-username verification failures |
| `MaxVerifyUsernameFailureEntries` | `50,000` | Hard cap on tracked username entries to prevent memory exhaustion |
| `VerifyUsernameFailureSweepMaxScan` | `64` | Maximum entries scanned per sweep for expired verification failures |

## Usage Examples

### Enqueuing an Account Creation Request Programmatically

```csharp
// Build an AccountCreationRequest with encrypted credentials
var request = new AccountCreationRequest<NetworkConnection>(
    conn,
    encryptedUsername,   // AES-encrypted byte[]
    encryptedSalt,       // AES-encrypted byte[]
    encryptedVerifier,   // AES-encrypted byte[]
    encryptionData,      // ConnectionEncryptionData from handshake
    ipAddress,
    seq                  // Client-sent sequence number
);

// Attempt to enqueue
bool accepted = accountCreationSystem.TryEnqueueAccountCreation(request);
if (!accepted)
{
    // Request was rate-limited, blocked, or queue full
}
```

### Querying Runtime Metrics

```csharp
// From any server system with access to the AccountCreationSystem reference
int pending   = accountCreationSystem.PendingRequestCount;
long created  = accountCreationSystem.TotalProcessed;
long rejected = accountCreationSystem.TotalRejected;
```

### Client-Side Broadcast

```csharp
// Client sends encrypted SRP credentials to the login server
var broadcast = new CreateAccountBroadcast
{
    Username = encryptedUsername,  // byte[]
    Salt     = encryptedSalt,     // byte[]
    Verifier = encryptedVerifier, // byte[]
    Seq      = sequenceNumber     // uint
};
ClientManager.Broadcast(broadcast);
```

## Operational Checks

| Check | Method | Expected Result |
|-------|--------|-----------------|
| System initializes | Assign asset to Login Server behaviour list and start server | Log: `"Initialized (RateLimit=5s, MaxFailures=5, BlockDuration=300s)"` |
| Normal account creation | Client sends valid `CreateAccountBroadcast` | `ClientAuthResultBroadcast` with `AccountCreated`; `TotalProcessed` increments |
| Rate-limited request | Same IP sends two requests within `ipRateLimitSeconds` | `ClientAuthResultBroadcast` with `ServerBusy` (unreliable channel); `TotalRejected` increments |
| Blocked IP | IP exceeds `maxFailedAttempts` failures | Connection disconnected immediately; `TotalRejected` increments |
| Queue full | `AsyncWorkerData` bounded channel is at capacity | `ClientAuthResultBroadcast` with `ServerBusy`; `TotalRejected` increments |
| Oversized payload | Encrypted field exceeds 2048 bytes | Connection disconnected on network thread; no decryption attempted |
| Invalid encrypted data | Decryption fails (bad key/nonce/tampered) | `CryptographicException` caught; connection disconnected; failure tracked against IP |
| Malformed UTF-8 | Decrypted bytes are not valid UTF-8 | `DecoderFallbackException` caught; decrypted arrays zeroed; connection disconnected |
| Invalid username | Username fails `Authentication.IsAllowedUsername()` | `InvalidUsernameOrPassword` response; no DB call made |
| Duplicate username | DB returns `UniqueViolation` | `InvalidUsernameOrPassword` response; IP failure count incremented |
| Stale entry cleanup | 60 seconds elapse | Expired rate-limit and failure entries evicted within scan/removal bounds |
| Graceful shutdown | Server deinitializes | Remaining queued responses fully drained; broadcasts unregistered; caches cleared |
| Proxy mode | `useConnectionIdForRateLimit = true` | Rate limiting keyed by `conn.ClientId` instead of IP address |

## Flow Diagram

### High-Level Overview

```mermaid
flowchart LR
    Client[Unity Client] -->|CreateAccount request| Sys[AccountCreationSystem]
    Sys -->|validate username/password| Sys
    Sys -->|check existing| DB[(PostgreSQL Accounts)]
    DB -->|exists?| Sys
    Sys -->|hash + insert| DB
    Sys -->|result code| Client
```

```
┌─────────┐    CreateAccountBroadcast     ┌────────────────────────────────┐
│  Client  │ ──────────────────────────▶  │   Network Thread (UDP Gate)    │
└─────────┘                               │                                │
                                          │  1. Verify encryption data     │
                                          │  2. Reject oversized fields    │
                                          │  3. Capture IP address         │
                                          │  4. Build immutable request    │
                                          │  5. Check IP block list        │
                                          │  6. Atomic rate-limit check    │
                                          │  7. Enqueue to async worker    │
                                          └──────────┬─────────────────────┘
                                                     │
                                          ┌──────────▼─────────────────────┐
                                          │   AsyncWorkerData (Bounded)    │
                                          │   Channel + Worker Affinity    │
                                          └──────────┬─────────────────────┘
                                                     │
                                          ┌──────────▼─────────────────────┐
                                          │   Worker Thread                │
                                          │                                │
                                          │  1. Consume & validate seqs    │
                                          │  2. AES-GCM decrypt fields     │
                                          │     (nonce = seq-derived)      │
                                          │  3. StrictUtf8 → strings       │
                                          │  4. ZeroMemory(decrypted[])    │
                                          │  5. IsAllowedUsername() check   │
                                          │  6. Validate salt/verifier len │
                                          │  7. IAccountService.PersistAsync│
                                          │  8. Update metrics & IP track  │
                                          │  9. Enqueue response action    │
                                          └──────────┬─────────────────────┘
                                                     │
                                          ┌──────────▼─────────────────────┐
                                          │   Main Thread Queue            │
                                          │   (time-sliced drain per frame)│
                                          └──────────┬─────────────────────┘
                                                     │
                                          ┌──────────▼─────────────────────┐
┌─────────┐  ClientAuthResultBroadcast    │   Main Thread (OnUpdate)       │
│  Client  │ ◀────────────────────────── │   Broadcast result to client   │
└─────────┘                               └────────────────────────────────┘

Rejection paths (no worker involvement):
  • Oversized payload      → disconnect on network thread
  • Blocked IP             → disconnect on network thread
  • Rate-limited / full    → ServerBusy (unreliable) on network thread
  • Crypto failure         → disconnect via main-thread queue
  • Malformed UTF-8        → disconnect via main-thread queue
```

## Project Structure

### Directory Tree

```
Server/Implementation/LoginServer/AccountCreation/
├── AccountCreationSystem.cs                     # Stateless ServerBehaviour logic and worker orchestration
├── AccountCreationSystemRuntimeData.cs          # Metrics, connection caches, cleanup timer
├── AccountCreationSystemMappingData.cs          # Per-IP rate/failure trackers (DoS/rate-limiting)
├── AccountCreationSystemMainThreadQueueData.cs  # Per-system main-thread action queue container
└── README.md

Server/Core/LoginServer/AccountCreation/
├── IAccountCreationSystem.cs                    # Engine-agnostic public API interface
├── IAccountCreationSystemRuntimeData.cs         # Runtime metrics interface
├── IAccountCreationSystemMappingData.cs         # Mapping data interface (rate-limit/failure)
├── IAccountCreationSystemMainThreadQueueData.cs # Main-thread queue interface
└── AccountCreationRequest.cs                    # Immutable request struct (generic over TConnection)
```

### Inheritance Hierarchies

#### Behaviour

```
ServerBehaviour
└── AccountCreationSystem : IAccountCreationSystem<NetworkConnection>
```

#### Runtime Data Containers

```
RuntimeDataContainer
├── AccountCreationSystemRuntimeData         : IAccountCreationSystemRuntimeData
├── AccountCreationSystemMappingData         : IAccountCreationSystemMappingData
└── MainThreadQueueData (abstract)
    └── SystemMainThreadQueueData (abstract)
        └── AccountCreationSystemMainThreadQueueData : IAccountCreationSystemMainThreadQueueData
```

### Runtime Data Container Details

#### AccountCreationSystemRuntimeData

Runtime statistics, connection caches, and worker tracking. Implements `IAccountCreationSystemRuntimeData`. Counter fields use `Interlocked` for thread-safe increments from async workers.

| Property | Type | Purpose |
|----------|------|---------|
| `ConnectionIpCache` | `LastSeenCacheTracker<int, string>` | Per-connection IP address cache for ingress validation |
| `ConnectionEncryptionCache` | `LastSeenCacheTracker<int, ConnectionEncryptionData>` | Per-connection AES key/IV cache for decryption |
| `TotalProcessed` | `long` | Successfully processed account creations since start |
| `TotalRejected` | `long` | Rejected requests (rate-limited, queue full) since start |
| `TotalFailed` | `long` | Failed creations (DB/decrypt errors) since start |
| `CleanupTimer` | `float` | Accumulator for periodic mapping data cleanup sweeps |

**Lifecycle:** `InitializeOnce()` creates fresh `LastSeenCacheTracker` instances and zeros all counters. `Clear()` clears caches and resets counters without nulling references. `OnDeinitialize()` clears and nulls all references.

#### AccountCreationSystemMappingData

Thread-safe per-IP rate limiting and DoS protection data. Implements `IAccountCreationSystemMappingData`.

| Property | Type | Purpose |
|----------|------|---------|
| `IpRateLimitTracker` | `ConcurrentDictionary<string, DateTime>` | Last attempt timestamp per IP for rate limiting |
| `IpFailureTracker` | `ConcurrentDictionary<string, int>` | Failed attempt count per IP for DoS blocking |

**Thread Safety:** Both dictionaries are `ConcurrentDictionary` — safe for simultaneous access from network and worker threads.

**Lifecycle:** `InitializeOnce()` creates empty concurrent dictionaries. `Clear()` clears dictionaries without nulling (may be accessed from other threads during runtime). `OnDeinitialize()` clears and nulls references.

#### AccountCreationSystemMainThreadQueueData

Per-system main-thread action queue. Inherits from `SystemMainThreadQueueData` → `MainThreadQueueData`. Implements `IAccountCreationSystemMainThreadQueueData`.

Provides `Enqueue(Action)` and `Drain(int)` methods for marshalling async worker responses back to the Unity main thread. A separate concrete type ensures the `DataContainerRegistry` creates an independent instance for this system.

### External Integration Points

| Dependency | Role |
|------------|------|
| **FishNet** | Receives `CreateAccountBroadcast`, sends `ClientAuthResultBroadcast` |
| **AccountManager** | Provides per-connection AES key/IV needed to decrypt payloads |
| **Database Service Registry** | Resolves `IAccountService` for persistence via `PersistAsync(username, salt, verifier)` |
| **ITwoFactorRecoveryCodeService** | Stores PBKDF2-SHA256 hashed recovery codes via `PersistManyAsync` during account creation |
| **DataContainerRegistry** | Supplies queue, runtime, mapping, and main-thread queue containers |
| **CryptoHelper** | AES decrypt, strict UTF-8 encoding, AAD construction, `AuthMessageType.CreateAccount` |
| **Authentication** | Centralized `IsAllowedUsername()` validation |

## License

This module is part of the FishMMO project and is subject to the FishMMO project license.
