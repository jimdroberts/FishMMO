# AccountCreation System

## Overview

The AccountCreation system is an asynchronous, rate-limited login-server pipeline for creating new accounts without blocking the network thread. Incoming account-creation broadcasts are treated as ultra-fast UDP gate events: encrypted payloads are validated and queued immediately, while decryption and database persistence are executed by background workers. Responses are marshalled back to the main thread through a dedicated queue container to preserve FishNet thread-safety.

Main-thread response dispatch is time-sliced by `maxMainThreadResponsesPerFrame` to avoid frame spikes under heavy ingress.

## Directory Structure

```
AccountCreation/
├── AccountCreationSystem.cs                     # Stateless ServerBehaviour logic and worker orchestration
├── AccountCreationSystemRuntimeData.cs          # Metrics, connection caches, cleanup timer
├── AccountCreationSystemMappingData.cs          # Per-IP rate/failure trackers (DoS/rate-limiting)
├── AccountCreationSystemMainThreadQueueData.cs  # Per-system main-thread action queue container
└── README.md
```

Related core contracts live in `Server/Core/LoginServer/AccountCreation/`:

- `IAccountCreationSystem<TConnection>`
- `IAccountCreationSystemRuntimeData`
- `IAccountCreationSystemMappingData`
- `IAccountCreationSystemMainThreadQueueData`
- `AccountCreationRequest<TConnection>`

## Inheritance Hierarchies

### Behaviour

```
ServerBehaviour
└── AccountCreationSystem : IAccountCreationSystem<NetworkConnection>
```

### Runtime Data Containers

```
RuntimeDataContainer
├── AccountCreationSystemRuntimeData         : IAccountCreationSystemRuntimeData
├── AccountCreationSystemMappingData         : IAccountCreationSystemMappingData
└── MainThreadQueueData (abstract)
    └── SystemMainThreadQueueData (abstract)
        └── AccountCreationSystemMainThreadQueueData : IAccountCreationSystemMainThreadQueueData
```

## Runtime Data Container Details

### `AccountCreationSystemRuntimeData`

Runtime statistics, connection caches, and worker tracking. Implements `IAccountCreationSystemRuntimeData`.

| Property | Type | Purpose |
|----------|------|---------|
| `ConnectionIpCache` | `LastSeenCacheTracker<int, string>` | Per-connection IP address cache for ingress validation |
| `ConnectionEncryptionCache` | `LastSeenCacheTracker<int, ConnectionEncryptionData>` | Per-connection AES key/IV cache for decryption |
| `TotalProcessed` | `long` | Successfully processed account creations since start |
| `TotalRejected` | `long` | Rejected requests (rate-limited, queue full) since start |
| `TotalFailed` | `long` | Failed creations (DB/decrypt errors) since start |
| `CleanupTimer` | `float` | Accumulator for periodic mapping data cleanup sweeps |

**Lifecycle:**
- `InitializeOnce()` — creates fresh `LastSeenCacheTracker` instances, zeros all counters.
- `Clear()` — clears caches and resets counters without nulling references.
- `Deinitialize()` — clears and nulls all references.

### `AccountCreationSystemMappingData`

Thread-safe per-IP rate limiting and DoS protection data. Implements `IAccountCreationSystemMappingData`.

| Property | Type | Purpose |
|----------|------|---------|
| `IpRateLimitTracker` | `ConcurrentDictionary<string, DateTime>` | Last attempt timestamp per IP for rate limiting |
| `IpFailureTracker` | `ConcurrentDictionary<string, int>` | Failed attempt count per IP for DoS blocking |

**Thread Safety:** Both dictionaries are `ConcurrentDictionary` — safe for simultaneous access from network and worker threads.

**Lifecycle:**
- `InitializeOnce()` — creates empty concurrent dictionaries.
- `Clear()` — clears dictionaries without nulling (may be accessed from other threads).
- `Deinitialize()` — clears and nulls references.

### `AccountCreationSystemMainThreadQueueData`

Per-system main-thread action queue. Inherits from `SystemMainThreadQueueData` (which inherits from `MainThreadQueueData`). Implements `IAccountCreationSystemMainThreadQueueData`.

Provides `Enqueue(Action)` and `Drain(int)` methods for marshalling async worker responses back to the Unity main thread.

**Why a separate concrete type?** The `DataContainerRegistry` creates independent instances per concrete type, ensuring each system gets its own isolated main-thread queue.

## Request Pipeline

### 1) Network Thread (UDP Gate)

`OnServerCreateAccountBroadcastReceived` performs only fast operations:

1. Verify connection encryption data exists.
2. Capture client IP address.
3. Create `AccountCreationRequest<NetworkConnection>` with encrypted fields.
4. Attempt enqueue via `TryEnqueueAccountCreation`.
5. If rejected (rate-limited or queue full), immediately send `ClientAuthenticationResult.ServerBusy`.

No decryption or DB I/O occurs on the network thread.

### 2) Queue + Backpressure

Requests are dispatched through centralized `AsyncWorkerData`:

- Bounded channels with drop-on-full backpressure.
- Optional entity-key routing (`ClientId`) for consistent worker affinity.
- Immediate enqueue failure path returns server-busy response.

### 3) Worker Threads

`AsyncWorkerData` workers execute `ProcessAccountCreationAsync`:

1. AES-decrypt username, salt, verifier.
2. Convert decrypted bytes to strings via `CryptoHelper.StrictUtf8` (throws `DecoderFallbackException` on malformed UTF-8).
3. Zero decrypted byte arrays with `CryptographicOperations.ZeroMemory()` immediately after conversion (or on failure).
4. Resolve `IAccountService` from database service registry.
5. Persist account via `PersistAsync(username, salt, verifier)`.
6. Update runtime metrics and per-IP failure tracking.
7. Enqueue response action to main thread queue.

### 4) Main Thread Response

`OnLateUpdate` drains queued actions through `IAccountCreationSystemMainThreadQueueData.Drain(maxMainThreadResponsesPerFrame)` and sends FishNet broadcasts on the main thread.

On shutdown, remaining queued responses are fully drained.

## Rate Limiting and DoS Protection

Per-IP protection is stored in `AccountCreationSystemMappingData`:

- `IpRateLimitTracker` (`ConcurrentDictionary<string, DateTime>`) tracks last attempt time.
- `IpFailureTracker` (`ConcurrentDictionary<string, int>`) tracks failed attempts.

### Enforcement Rules

- Reject if request arrives sooner than `ipRateLimitSeconds` since last attempt.
- Reject if failures for IP are `>= maxFailedAttempts`.
- On success, remove IP failure entry.
- On failed persistence, increment IP failure count.

> Note: Account creation does not use a per-connection in-flight gate. Its anti-spam controls are IP-based throttling, temporary blocking, and async-worker backpressure.

### Periodic Cleanup

Every 60 seconds, stale entries older than `ipBlockDurationSeconds` are evicted to prevent long-term dictionary growth.

## Worker Lifecycle and Health

- Worker lifecycle is managed by shared `AsyncWorkerData` runtime container.
- `AccountCreationSystem` only enqueues work and handles accept/reject behavior.
- On deinitialize it unsubscribes connection events, clears local caches, drains main-thread responses, and unregisters broadcasts.

## Runtime Metrics

`AccountCreationSystemRuntimeData` tracks performance counters and connection caches:

- `TotalProcessed` — successful account creations
- `TotalRejected` — rate-limited or backpressure-rejected requests
- `TotalFailed` — DB/decrypt errors
- `CleanupTimer` — accumulator for periodic stale-entry sweeps
- `ConnectionIpCache` — per-connection IP lookup cache
- `ConnectionEncryptionCache` — per-connection AES encryption data cache

Public behaviour properties expose key counters:

- `PendingRequestCount`
- `TotalProcessed`
- `TotalRejected`

## External Integration Points

- **FishNet**: receives `CreateAccountBroadcast`, sends `ClientAuthResultBroadcast`.
- **AccountManager**: provides per-connection AES key/IV needed to decrypt payloads.
- **Database Service Registry**: resolves `IAccountService` for persistence.
- **RuntimeData Container Registry**: supplies queue/runtime/mapping/main-thread containers.
- **CryptoHelper**: performs AES decrypt operations for encrypted account creation payloads.