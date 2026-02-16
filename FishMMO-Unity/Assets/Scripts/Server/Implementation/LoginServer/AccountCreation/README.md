# AccountCreation System

## Overview

The AccountCreation system is an asynchronous, rate-limited login-server pipeline for creating new accounts without blocking the network thread. Incoming account-creation broadcasts are treated as ultra-fast UDP gate events: encrypted payloads are validated and queued immediately, while decryption and database persistence are executed by background workers. Responses are marshalled back to the main thread through a dedicated queue container to preserve FishNet thread-safety.

Main-thread response dispatch is time-sliced by `maxMainThreadResponsesPerFrame` to avoid frame spikes under heavy ingress.

## Directory Structure

```
AccountCreation/
├── AccountCreationSystem.cs                     # Stateless ServerBehaviour logic and worker orchestration
├── AccountCreationSystemRuntimeData.cs          # Metrics, worker task references, cleanup timer
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
    └── AccountCreationSystemMainThreadQueueData : IAccountCreationSystemMainThreadQueueData
```

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
2. Resolve `IAccountService` from database service registry.
3. Persist account via `PersistAsync(username, salt, verifier)`.
4. Update runtime metrics and per-IP failure tracking.
5. Enqueue response action to main thread queue.

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

`AccountCreationSystemRuntimeData` tracks:

- `TotalProcessed`
- `TotalRejected`
- `TotalFailed`
- `CleanupTimer`

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