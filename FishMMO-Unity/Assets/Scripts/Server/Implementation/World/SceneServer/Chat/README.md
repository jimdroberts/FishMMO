# Chat System

## Overview

The Chat system is the SceneServer authority for player messaging across world, region, say, party, guild, tell, trade, system, and Discord channels. It validates inbound client chat, enforces rate/spam limits, routes channel-specific broadcasts, and persists eligible messages through asynchronous database operations. Every persisted message carries the exact UTC timestamp captured at the network receive boundary for legal audit and subpoena compliance.

The implementation is split into:
- Main-thread game/network execution for validation and broadcasting.
- Async worker execution for database fetch/persist work.
- A dedicated main-thread queue data container for marshaling async results back to safe broadcast context.

### 50 000-User Scalability Features

Four architectural features keep the chat pipeline responsive under extreme load:

1. **Lock-Free Incoming Chat Queue** — Network callbacks enqueue stamped messages into a `ConcurrentQueue`; the main-thread `OnUpdate` drains up to `maxIncomingChatsPerFrame` entries per frame, preventing network spikes from freezing gameplay. A hard cap (`maxIncomingQueueSize`, default 10 000) kicks the sender when the queue is flooded, preventing memory exhaustion from DoS attacks.
2. **Token Bucket Anti-Spam** — Each player carries a refillable token bucket (`ChatTokens` (float), `ChatTokenLastRefillTicks`). Messages consume one token; when the bucket is empty the message is silently dropped. Float-precision tokens ensure fractional refill at sub-1-token/s rates accumulates correctly instead of being truncated. All rate-limit arithmetic is ticks-only (`long`); `DateTime` is never allocated in the hot path.
3. **Batch DB Persistence** — Instead of per-message `PersistAsync` calls, all channels enqueue into a `ConcurrentQueue<PendingChatPersist>`. A periodic callback (`OnPeriodicPersistFlush`) drains up to `maxPersistBatchSize` (default 2 000) entries and writes the batch via `PersistBatchAsync`, reducing DB round-trips from O(N) to ~O(N/batchSize). Overflow stays in the queue for the next flush cycle, preventing a single flush from spiking the database after a stall.
4. **Outbound Broadcast Batching** — World and Trade channel messages are buffered per-world in `OutboundWorldBroadcastBuffer`. A periodic callback (`OnPeriodicOutboundFlush`) flushes up to `maxOutboundBatchSize` messages per recipient per flush, preventing large-channel broadcasts from generating per-message network overhead. A hard cap (`maxBufferedWorldMessages`, default 200) per world ID drops oldest messages if the buffer grows beyond the limit, preventing unbounded memory growth if flushes stall.

## Directory Structure

```text
Chat/
├── ChatSystem.cs                      # Main chat orchestration, parsing, routing, persistence dispatch, incoming queue drain, batch flush
├── ChatSystem.GroupChat.cs             # Partial: group/party chat handling
├── ChatSystem.LocalChat.cs             # Partial: local/proximity chat handling
├── ChatSystem.TellChat.cs              # Partial: private tell/whisper chat handling
├── ChatSystem.WorldChat.cs             # Partial: world/global chat handling + outbound batch buffering
├── ChatSystemRuntimeData.cs           # Polling cursor state, lock-free queues, outbound buffers
├── ChatSystemMainThreadQueueData.cs   # Per-system main-thread action queue container
└── README.md                          # System documentation
```

## Core Contracts

Related interfaces:
- `IChatSystem`
- `IChatSystemRuntimeData`
- `IChatSystemMainThreadQueueData`

The implementation registers/consumes these runtime data containers:
- `ChatSystemRuntimeData`
- `ChatSystemMainThreadQueueData`
- `AsyncWorkerData`

## Initialization and Lifecycle

`InitializeOnce()`:
1. Validates required dependencies and data containers.
2. Initializes chat command routing via `ChatHelper.InitializeOnce(GetChannelCommand)`.
3. Registers `ChatBroadcast` network handler.
4. Registers periodic callbacks: message pump, persist flush, outbound broadcast flush.
5. Clamps all config fields to safe minimums.

`OnDeinitialize()`:
1. Flushes remaining outbound World/Trade broadcast buffers.
2. Flushes remaining pending persist queue entries (synchronous blocking write).
3. Drains incoming chat queue.
4. Drains pending main-thread queue actions.
5. Unregisters network handler.
6. Unregisters all periodic callbacks.

## Message Intake Pipeline

Inbound client messages are handled by `OnServerChatBroadcastReceived(...)` → lock-free queue → `DrainIncomingChatQueue(...)` → `ProcessNewChatMessage(...)`.

### Lock-Free Incoming Chat Queue

`OnServerChatBroadcastReceived` stamps `ReceivedUtcTicks` and enqueues `(NetworkConnection, ChatBroadcast)` into `IChatSystemRuntimeData.IncomingChatQueue`. The main-thread `OnUpdate` calls `DrainIncomingChatQueue`, which dequeues up to `maxIncomingChatsPerFrame` entries and passes each to `ProcessNewChatMessage`. Stale connections (disconnected between enqueue and dequeue) are skipped.

### Validation and Normalization Stages

1. Connection/object/sender validation.
2. **Timestamp stamp** — `DateTime.UtcNow.Ticks` written to `msg.ReceivedUtcTicks` at the network receive boundary (before enqueue). **DoS cap**: if the incoming queue exceeds `maxIncomingQueueSize`, the sender is kicked immediately.
3. Length and null/whitespace validation.
4. **Token bucket rate limiting** — refill tokens (float) based on elapsed ticks since `ChatTokenLastRefillTicks`, consume 1f token, drop if < 1f.
5. **Legacy per-message cooldown** — secondary gate via `NextChatMessageTicks` (kept alongside the token bucket). All arithmetic is ticks-only (`long`); no `DateTime` allocation in the hot path.
6. Repeat-message filtering (when repeat suppression is enabled).
7. Text sanitization (`ChatHelper.Sanitize`) — **skipped** when the message contains no `<` character (avoids allocation for clean text).
8. Command extraction (`ChatHelper.GetCommandAndTrim`).
9. Command routing using chat command registry.
10. Post-prepend length enforcement via `Substring(0, N)` (avoids C# range-syntax allocation under IL2CPP).

If a channel handler returns `true`, the message is enqueued for batch DB persistence.

### Token Bucket Anti-Spam

Each `IPlayerCharacter` carries:
- `ChatTokens` (`float`) — current bucket level (initialized to `float.MaxValue`; capped to `chatTokenBucketCapacity` on first refill).
- `ChatTokenLastRefillTicks` (`long`) — last refill timestamp in UTC ticks.

On each message:
1. Compute `elapsedSeconds = (receivedTicks - ChatTokenLastRefillTicks) / (double)TimeSpan.TicksPerSecond`.
2. Refill `(float)(elapsedSeconds * chatTokenRefillRate)` tokens, capped at `chatTokenBucketCapacity`.
3. If `ChatTokens < 1f`, drop the message.
4. Otherwise, `ChatTokens -= 1f`.

Using `float` for the token count ensures fractional refill at sub-1-token/s rates accumulates correctly (the previous `int` cast truncated any fractional part, causing players sending every 0.9 s at 1 token/s to never refill).

### Audit Timestamp Threading

The `ChatCommand` delegate is a shared client↔server type whose signature cannot be changed. The receive timestamp instead travels inside the `ChatBroadcast` struct via the `ReceivedUtcTicks` field, eliminating shared mutable state:

1. `OnServerChatBroadcastReceived` stamps `msg.ReceivedUtcTicks = DateTime.UtcNow.Ticks` once at the network boundary.
2. `ProcessNewChatMessage` reads `long receivedTicks = msg.ReceivedUtcTicks` — no `DateTime` allocation. All token-bucket and rate-limit arithmetic is ticks-only.
3. Synchronous-persist channels (World, Trade) pass `receivedTicks` (`long`) to `EnqueuePersist`.
4. Async channels (Party, Guild, Tell) capture `long receivedTicks = msg.ReceivedUtcTicks` into their immutable closure, forward it through their async methods, and pass it to `EnqueuePersist`.
5. `EnqueuePersist` writes the captured ticks — not `DateTime.UtcNow` at persist time — into the `PendingChatPersist` struct.
6. `FlushPersistQueueAsync` converts ticks to `DateTime` only at the DB boundary: `new DateTime(entry.ReceivedTicks, DateTimeKind.Utc)`.

Because each message carries its own timestamp in the value-type struct, multiple messages arriving in the same frame cannot overwrite each other's receive time. The ticks-only pipeline avoids `DateTime` struct allocation on every message.

## Channel Routing Model

### Synchronous channels (outbound-batched)
- **World**: buffer into `OutboundWorldBroadcastBuffer`; flushed periodically. Pump-sourced messages bypass buffer and broadcast immediately via `BroadcastToWorld`.
- **Trade**: same buffering behaviour as World.

### Synchronous channels (immediate)
- **Region**: broadcast to all connections in sender scene.
- **Say**: broadcast to sender observers.

### Async membership channels
- **Party**: capture `receivedTicks`, fetch party membership asynchronously, marshal broadcast to main thread, enqueue persist with receive ticks on success.
- **Guild**: capture `receivedTicks`, fetch guild membership asynchronously, marshal broadcast to main thread, enqueue persist with receive ticks on success.
- **Tell**: capture `receivedTicks`, resolve target asynchronously, marshal relay/status responses to main thread, enqueue persist with receive ticks on success.

### Server-origin channels
- **System**: direct server-to-client notifications.
- **Discord**: world-scoped relay to connected characters (immediate via `BroadcastToWorld`).

## Batch DB Persistence

All persist-eligible messages are enqueued as `PendingChatPersist` structs into `IChatSystemRuntimeData.PendingPersistQueue` (a `ConcurrentQueue`). A periodic callback (`OnPeriodicPersistFlush`) dispatches `FlushPersistQueueAsync` onto the async worker:

1. Drains up to `maxPersistBatchSize` (default 2 000) entries from `PendingPersistQueue` into a `List`. Overflow stays in the queue for the next flush cycle, preventing a single flush from spiking the database after a stall.
2. Converts `ReceivedTicks` (`long`) to `DateTime` only at the DB boundary.
3. Calls `IChatService.PersistBatchAsync(batch)` for a single DB round-trip.
4. On shutdown, `FlushPersistQueueSync` performs a blocking drain+write of **all** remaining entries to ensure no messages are lost.

This reduces DB round-trips from O(N) to ~O(N/batchSize) for high-throughput scenarios.

## Outbound Broadcast Batching

World and Trade channel broadcasts are buffered per-world ID in `OutboundWorldBroadcastBuffer`. A periodic callback (`OnPeriodicOutboundFlush`) calls `FlushOutboundBroadcastBuffers`:

1. Iterates world IDs using a reusable key buffer (avoids dictionary modification during iteration).
2. For each world: sends up to `maxOutboundBatchSize` buffered messages to each character.
3. Removes sent messages from the list; overflow carries to the next flush.
4. Cleans up empty world entries.

A hard cap (`maxBufferedWorldMessages`, default 200) per world ID is enforced at enqueue time in `OnWorldChat` and `OnTradeChat`. If the buffer exceeds the limit, oldest messages are dropped via `RemoveRange`. This prevents unbounded memory growth if flushes stall.

Pump-sourced messages (sender == null) bypass the buffer and broadcast immediately via `BroadcastToWorld`, since they arrive pre-batched from the database pump.

## Database Pump

A periodic message pump (`OnPeriodicMessagePump`) dispatches async fetch work:
1. Acquires the atomic `messagePumpInFlight` flag to prevent concurrent pumps.
2. Fetches new chat records after `LastFetchTime` and `LastFetchPosition`.
3. Enqueues main-thread application of fetched messages.
4. Updates cursor state in `IChatSystemRuntimeData` on main thread.

The pump flag is released via a `handedOffToMainThread` pattern:
- On success: cleared inside the main-thread callback's `finally` block (after cursor update).
- On early return or failure: cleared in the async method's `finally` block as a safety net.

This prevents pump deadlocks from early returns that previously leaked the in-flight flag.

A shutdown guard (`Server.ServerState != ConnectionState.Started`) at the top of async methods prevents work from running against disposed DB services during shutdown.

## Runtime Data Containers

### `ChatSystemRuntimeData`
Holds poll cursor state and scalability queues:
- `LastFetchTime`, `LastFetchPosition` — database pump cursor.
- `CharacterBroadcastBuffer` / `ConnectionBroadcastBuffer` — main-thread-only scratch lists for defensive-copy broadcast iteration. **Must not be shared with other systems**; concurrent use would corrupt iteration.
- `IncomingChatQueue` — `ConcurrentQueue<(NetworkConnection, ChatBroadcast)>` for lock-free inbound chat decoupling.
- `PendingPersistQueue` — `ConcurrentQueue<PendingChatPersist>` for batch DB persistence.
- `OutboundWorldBroadcastBuffer` — `Dictionary<long, List<ChatBroadcast>>` for per-world outbound broadcast batching.
- `OutboundWorldFlushKeyBuffer` — reusable scratch list for flush iteration.

All fields are reset during initialize/clear/deinitialize. ConcurrentQueues are drained (not nulled) during `Clear()` for thread safety.

### `ChatSystemMainThreadQueueData`
Per-system queue for main-thread actions generated by async tasks.
Used to safely execute broadcast work in `OnLateUpdate()` and during deinitialize draining.

## Async Worker and Backpressure

All async DB operations are dispatched through `TryEnqueueAsyncWork(...)`:
- Returns `true` when accepted.
- Returns `false` when queue is unavailable or full.
- Logs warnings on rejection/unavailability.
- Supports `entityKey` ordering for per-character chat operations.

This protects server responsiveness by preventing unbounded fire-and-forget task growth.

## External Service Dependencies

Primary service integrations:
- `IChatService` (provides `PersistBatchAsync` for batch writes, `PersistAsync` for legacy single writes, `FetchAsync` for pump)
- `ICharacterService`
- `ICharacterPartyService`
- `ICharacterGuildService`

Character routing dependency:
- `ICharacterMappingData<NetworkConnection>`

## Configuration Reference

| Field | Default | Description |
|-------|---------|-------------|
| `maxMainThreadActionsPerFrame` | 100 | Max main-thread queue actions drained per frame |
| `maxIncomingChatsPerFrame` | 500 | Max lock-free incoming chat messages processed per frame |
| `maxIncomingQueueSize` | 10 000 | Hard cap on incoming queue; sender kicked on overflow (DoS protection) |
| `messageRateLimit` | 500 ms | Legacy per-message cooldown (secondary to token bucket) |
| `chatTokenBucketCapacity` | 5 | Max burst of messages before throttling |
| `chatTokenRefillRate` | 1.0/s | Tokens refilled per second (float accumulation) |
| `maxMessageLength` | 128 | Maximum allowed chat message length |
| `allowRepeatMessages` | false | Whether repeat messages bypass spam filter |
| `messagePumpRate` | 2.0 s | Database pump polling interval |
| `messageFetchCount` | 20 | Messages fetched per database poll |
| `persistFlushIntervalSeconds` | 0.1 s | Batch DB persistence flush interval |
| `maxPersistBatchSize` | 2 000 | Max messages drained per persist flush (overflow carries to next) |
| `outboundBatchIntervalSeconds` | 0.05 s | Outbound World/Trade broadcast flush interval |
| `maxOutboundBatchSize` | 10 | Max buffered messages sent per recipient per flush |
| `maxBufferedWorldMessages` | 200 | Max buffered World/Trade messages per world ID (oldest dropped) |

## Failure Semantics

- Invalid chat input is dropped; exploit-like payloads can trigger kicks.
- **Incoming queue DoS cap**: when the incoming queue exceeds `maxIncomingQueueSize`, the enqueuing connection is kicked (`ExploitExcessiveData`), preventing memory exhaustion from flood attacks.
- Missing dependencies or failed async fetches exit safely.
- Queue backpressure is logged and work is skipped rather than blocking the main thread.
- **Persist batch cap**: `FlushPersistQueueAsync` drains up to `maxPersistBatchSize` entries per flush; overflow stays for the next cycle, preventing DB spikes after stalls.
- **Outbound buffer cap**: World/Trade outbound buffers are capped at `maxBufferedWorldMessages` per world; oldest messages are dropped when exceeded.
- Only successful, eligible channel flows are persisted.
- **Shutdown guard**: `FlushPersistQueueAsync` and `FetchAndProcessChatMessagesAsync` return immediately when `Server.ServerState != ConnectionState.Started`, preventing use of disposed DB services.
- **Shutdown drain**: `OnDeinitialize` calls `FlushOutboundBroadcastBuffers`, `FlushPersistQueueSync`, `DrainIncomingChatQueue(drainAll: true)`, and `DrainMainThreadQueue(drainAll: true)` to ensure no messages are lost.
- **Pump flag safety**: the `handedOffToMainThread` pattern guarantees the pump flag is always cleared, even on early-return paths that previously caused pump deadlocks.
- **Null sender in message pump**: `ProcessChatMessages` intentionally passes `null` as the sender when invoking channel handlers for pump-sourced (already-persisted) messages. Handlers use this to suppress re-persistence and skip sender-specific operations. World/Trade handlers broadcast immediately when sender is null (bypassing the outbound buffer).
- **Stale connections**: `DrainIncomingChatQueue` skips entries whose connection is no longer active between enqueue and dequeue.

## Future Considerations

- **Adaptive token bucket**: Per-channel token rates (e.g., stricter limits for World chat, relaxed for Party) based on channel audience size.
- **Outbound compression**: Combining multiple messages into a single network packet for World/Trade channels to further reduce bandwidth.
- **Per-frame message throttling**: The pump currently processes all fetched messages in a single main-thread action. Under heavy load, enqueuing each message as a separate main-thread action (leveraging the existing `maxMainThreadActionsPerFrame` drain cap) would spread processing across frames and reduce spike risk.