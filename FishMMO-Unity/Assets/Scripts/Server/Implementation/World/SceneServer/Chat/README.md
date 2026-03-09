# Chat System

**Short description:** SceneServer authority for player messaging across World, Region, Say, Party, Guild, Tell, Trade, System, and Discord channels, with token-bucket anti-spam, lock-free incoming queue, batch DB persistence, and outbound broadcast batching for 50,000-user scalability.

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

The Chat system is the SceneServer authority for player messaging across World, Region, Say, Party, Guild, Tell, Trade, System, and Discord channels. It validates inbound client chat, enforces rate/spam limits, routes channel-specific broadcasts, and persists eligible messages through asynchronous database operations. Every persisted message carries the exact UTC timestamp captured at the network receive boundary for legal audit and subpoena compliance.

The implementation uses a split execution model:
- **Main thread:** network callback enqueue, lock-free queue drain, validation, command parsing, channel routing, broadcast dispatch, outbound buffer flush, and main-thread queue drain.
- **Async worker:** database fetch/persist work for group membership resolution (Party, Guild), target resolution (Tell), message pump polling, and batch persistence via `TryEnqueueAsyncWork`.
- **Main-thread queue:** marshaling async completion actions (broadcast dispatch, cursor updates) back to Unity/FishNet-safe context via `IChatSystemMainThreadQueueData`.

Four architectural features keep the chat pipeline responsive under extreme load:

1. **Lock-Free Incoming Chat Queue** — Network callbacks enqueue stamped messages into a `ConcurrentQueue`; the main-thread `OnUpdate` drains up to `maxIncomingChatsPerFrame` entries per frame. A hard cap (`maxIncomingQueueSize`, default 10,000) kicks the sender when the queue is flooded, preventing memory exhaustion from DoS attacks.
2. **Token Bucket Anti-Spam** — Each player carries a refillable token bucket (`ChatTokens` (double), `ChatTokenLastRefillTicks`). Messages consume one token; when the bucket is empty the message is silently dropped. Float-precision tokens ensure fractional refill at sub-1-token/s rates accumulates correctly.
3. **Batch DB Persistence** — All persist-eligible channels enqueue into a `ConcurrentQueue<PendingChatPersist>`. A periodic callback drains up to `maxPersistBatchSize` (default 2,000) entries and writes the batch via `PersistBatchAsync`, reducing DB round-trips from O(N) to ~O(N/batchSize).
4. **Outbound Broadcast Batching** — World and Trade channel messages are buffered per-world in `OutboundWorldBroadcastBuffer`. A periodic callback flushes up to `maxOutboundBatchSize` messages per recipient per flush. A hard cap (`maxBufferedWorldMessages`, default 200) per world ID drops oldest messages if the buffer exceeds the limit.

## Supported Platforms

| Platform | Supported | Notes |
|---|---|---|
| Windows | Yes | |
| Linux | Yes | |
| WebGL | N/A | Server-only module |
| Unity 6.3 LTS | Yes | Required engine version |
| IL2CPP | Yes | Supported scripting backend |

## Features

- Nine chat channels: World, Region, Say, Party, Guild, Tell, Trade, System, Discord
- Lock-free incoming chat queue (`ConcurrentQueue`) decoupling network callbacks from main-thread processing with configurable per-frame drain budget
- DoS protection via hard cap on incoming queue size; sender kicked when exceeded
- Token bucket anti-spam with configurable burst capacity (`chatTokenBucketCapacity`) and refill rate (`chatTokenRefillRate`)
- Legacy per-message cooldown (`messageRateLimit`) as a secondary rate-limit gate
- Repeat-message suppression (configurable via `allowRepeatMessages`)
- Rich text tag sanitization (skipped when message contains no `<` for zero-allocation fast path)
- Command extraction and routing via `ChatHelper` command registry
- Post-prepend length enforcement capped at `maxMessageLength + MaxChannelIdPrefixLength` (22 chars)
- Synchronous immediate channels: Region (scene-scoped broadcast), Say (observer-scoped broadcast)
- Synchronous outbound-batched channels: World and Trade (buffered per-world, flushed periodically)
- Async membership channels: Party and Guild (async DB member fetch, main-thread marshal, async-path persistence)
- Async target resolution channel: Tell (async character lookup, self-tell short-circuit, offline detection, relay confirmation)
- Server-origin channels: System (server-to-client notifications), Discord (world-scoped relay via `BroadcastToWorld`)
- Batch DB persistence via `ConcurrentQueue<PendingChatPersist>` with periodic flush and configurable batch size
- Synchronous shutdown flush (`FlushPersistQueueSync`) ensuring no messages are lost on deinitialize
- Outbound World/Trade broadcast batching with per-world hard cap and oldest-message drop on overflow
- Database message pump (`OnPeriodicMessagePump`) with atomic in-flight flag, cursor tracking, and shutdown guard
- Pump-sourced messages bypass outbound buffer and broadcast immediately (already persisted, pre-batched)
- Defensive-copy broadcast iteration using reusable scratch buffers (`CharacterBroadcastBuffer`, `ConnectionBroadcastBuffer`)
- Audit timestamp threading: `ReceivedUtcTicks` stamped once at the network boundary, carried through the entire pipeline, converted to `DateTime` only at the DB boundary
- All rate-limit arithmetic is ticks-only (`long`); no `DateTime` allocation in the hot path
- Async worker backpressure via `TryEnqueueAsyncWork` (rejects when queue unavailable/full, logs warning)
- Per-system main-thread queue isolation with configurable drain cap per frame
- Graceful deinitialize: flushes outbound buffers, signals shutdown, flushes persist queue synchronously, drains incoming queue and main-thread queue, unregisters handlers and periodic callbacks

## Prerequisites

- **Unity 6.3 LTS**
- **FishNetworking** — networking framework
- **FishMMO Server Core** — provides `ServerBehaviour`, `IChatSystem`, `IChatSystemRuntimeData`, `IChatSystemMainThreadQueueData`, `IPeriodicUpdateSystem`, `ISceneServerSystem`, `ISceneServerRuntimeData`, `ICharacterMappingData<NetworkConnection>`, `ChatCommand`, `ChatCommandDetails`, `ChatHelper`, `ChatBroadcast`, `ChatChannel`, `PendingChatPersist`, `AsyncWorkerData`, and `Authentication`
- **FishMMO Database** — provides `IChatService`, `ICharacterPartyService`, `ICharacterGuildService`, `ICharacterService`, `ChatData`, `CharacterPartyData`, `CharacterGuildData`, `CharacterData`, and `DatabaseResult<T>`

## Installation / Build

This is an integrated module within FishMMO. It is included as part of the server-side scene-server implementation and does not require separate installation. Ensure the FishMMO Server Core and its dependencies are properly configured in your Unity project.

## Quick Start Guides

1. Ensure `ChatSystem` is present on the scene server GameObject (it inherits from `ServerBehaviour` and implements `IChatSystem`). The asset is created via `Create > FishMMO > Server > SceneServer > Chat System`.
2. Verify that the following data containers are registered in `DataContainerRegistry`:
   - `ChatSystemRuntimeData` → `IChatSystemRuntimeData`
   - `ChatSystemMainThreadQueueData` → `IChatSystemMainThreadQueueData`
   - `AsyncWorkerData` (shared async work queue)
3. On initialize, `ChatSystem` builds the channel → handler command map, initializes `ChatHelper`, registers the `ChatBroadcast` network handler, and registers three periodic callbacks (message pump, persist flush, outbound broadcast flush).
4. On deinitialize, it flushes remaining outbound buffers, signals shutdown, flushes the persist queue synchronously, drains the incoming chat queue and main-thread queue, unregisters the broadcast handler, and unregisters all periodic callbacks.
5. Clients send a `ChatBroadcast` with text prefixed by a channel command (e.g., `/s`, `/w`, `/p`, `/g`, `/t`, `/tr`). The server validates, rate-limits, parses the command, routes to the channel handler, and broadcasts results.

## Configuration

### Inspector Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `maxMainThreadActionsPerFrame` | int | 100 | Max chat-system actions drained from main-thread queue per frame |
| `maxIncomingChatsPerFrame` | int | 500 | Max incoming chat messages processed from the lock-free queue per frame |
| `maxIncomingQueueSize` | int | 10000 | Maximum pending incoming chat messages before the sender is kicked (DoS protection) |
| `messageRateLimit` | float | 500.0 | Server chat rate limit in milliseconds (should match client `UIChat.messageRateLimit`) |
| `chatTokenBucketCapacity` | int | 5 | Maximum number of chat tokens a player can accumulate (burst capacity) |
| `chatTokenRefillRate` | float | 1.0 | Tokens refilled per second (1.0 = one message permit per second) |
| `persistFlushIntervalSeconds` | float | 0.1 | Seconds between batch DB persistence flushes |
| `maxPersistBatchSize` | int | 2000 | Maximum messages written to the database per flush (overflow carries to next flush) |
| `outboundBatchIntervalSeconds` | float | 0.05 | Seconds between outbound World/Trade broadcast flushes |
| `maxOutboundBatchSize` | int | 10 | Maximum buffered World/Trade messages sent per recipient per flush |
| `maxBufferedWorldMessages` | int | 200 | Maximum buffered World/Trade messages per world (oldest dropped when exceeded) |
| `maxMessageLength` | int | 128 | Maximum allowed chat message length |
| `allowRepeatMessages` | bool | false | If true, allows repeat messages without spam filtering |
| `messagePumpRate` | float | 2.0 | Server chat message pump rate limit in seconds |
| `messageFetchCount` | int | 20 | Number of chat messages to fetch per database poll |

### Channel Command Map

On initialization, the following channel-to-handler map is built in `IChatSystemRuntimeData.ChannelCommandMap`:

| Channel | Handler | Routing |
|---|---|---|
| `ChatChannel.World` | `OnWorldChat` | Outbound-batched per-world; pump-sourced immediate |
| `ChatChannel.Region` | `OnRegionChat` | Immediate broadcast to all scene connections |
| `ChatChannel.Party` | `OnPartyChat` | Async DB member fetch, main-thread broadcast |
| `ChatChannel.Guild` | `OnGuildChat` | Async DB member fetch, main-thread broadcast |
| `ChatChannel.Tell` | `OnTellChat` | Async target resolve, main-thread relay/status |
| `ChatChannel.Trade` | `OnTradeChat` | Outbound-batched per-world; pump-sourced immediate |
| `ChatChannel.Say` | `OnSayChat` | Immediate broadcast to sender observers |
| `ChatChannel.System` | `OnSendSystemMessage` | Server-to-client only (not in command map) |
| `ChatChannel.Discord` | `OnSendDiscordMessage` | World-scoped relay via `BroadcastToWorld` |

### Threading Model

| Thread | Work |
|---|---|
| Main thread | Incoming queue drain, validation, token bucket, rate limit, repeat filter, sanitization, command parse, channel routing, broadcast dispatch, outbound flush, main-thread queue drain |
| Async worker | Party/Guild member fetch (`OnPartyChatAsync`, `OnGuildChatAsync`), Tell target resolve (`OnTellChatAsync`), message pump (`FetchAndProcessChatMessagesAsync`), batch persistence (`FlushPersistQueueAsync`) |

## Usage Examples

### Broadcast Handler

`ChatSystem` registers a single server-side broadcast handler on initialize:

| Broadcast | Handler | Purpose |
|---|---|---|
| `ChatBroadcast` | `OnServerChatBroadcastReceived` | Stamps receive time, enqueues into lock-free incoming queue |

### Inbound Message Pipeline

`OnServerChatBroadcastReceived(conn, msg, channel)` → lock-free queue → `DrainIncomingChatQueue` → `ProcessNewChatMessage`:

1. Stamps `msg.ReceivedUtcTicks = DateTime.UtcNow.Ticks` at the network boundary.
2. Atomically increments incoming queue size counter (O(1)); kicks sender if over `maxIncomingQueueSize`.
3. Enqueues `(conn, msg)` into `IChatSystemRuntimeData.IncomingChatQueue`.
4. Main-thread `OnUpdate` calls `DrainIncomingChatQueue`, dequeuing up to `maxIncomingChatsPerFrame` entries.
5. Skips stale connections (disconnected between enqueue and dequeue).
6. Resolves `IPlayerCharacter` from connection's first object; kicks if missing.

### ProcessNewChatMessage Validation Stages

1. Null/whitespace and length validation (`maxMessageLength`); kicks on failure.
2. Token bucket refill and consume (drop if bucket < 1.0).
3. Legacy per-message cooldown via `NextChatMessageTicks`.
4. Repeat-message suppression (when `allowRepeatMessages` is false).
5. Rich text tag sanitization (skipped when text contains no `<`).
6. Command extraction via `ChatHelper.GetCommandAndTrim`.
7. Non-chat command check via `ChatHelper.TryParseCommand`.
8. Chat command routing via `ChatHelper.TryParseChatCommand`.
9. Channel-specific ID prepend (Guild ID, Party ID, World ID).
10. Post-prepend length enforcement via `Substring(0, N)`.
11. Channel handler invocation; if handler returns `true`, message is enqueued for batch DB persistence.

### World / Trade Chat (Outbound-Batched)

`OnWorldChat(sender, msg)` / `OnTradeChat(sender, msg)`:
- Parses world ID from message text.
- **Live player messages:** buffered into `OutboundWorldBroadcastBuffer[worldID]`; hard cap drops oldest on overflow.
- **Pump-sourced messages** (sender == null): broadcast immediately via `BroadcastToWorld` (already persisted).
- Returns `true` for live messages (triggers batch persist enqueue).

### Region Chat (Scene-Scoped)

`OnRegionChat(sender, msg)`:
- Resolves sender's scene via `SceneManager.GetSceneByName`.
- Defensive-copies scene connections into `ConnectionBroadcastBuffer`.
- Broadcasts to all connections in the scene.
- Returns `false` (not persisted).

### Say Chat (Observer-Scoped)

`OnSayChat(sender, msg)`:
- Defensive-copies sender's `Observers` into `ConnectionBroadcastBuffer`.
- Broadcasts to all observer connections.
- Returns `false` (not persisted).

### Party / Guild Chat (Async Membership)

`OnPartyChat(sender, msg)` / `OnGuildChat(sender, msg)`:
- Parses group ID from message text.
- Captures immutable data (sender ID, channel, character name, account name, world server ID, received ticks).
- Enqueues async work (`OnPartyChatAsync` / `OnGuildChatAsync`):
  - Fetches members via `ICharacterPartyService.FetchManyAsync` / `ICharacterGuildService.FetchManyAsync`.
  - Marshals broadcast to main thread: iterates members, resolves online characters, sends `ChatBroadcast`.
  - Enqueues persist with captured receive ticks (live messages only; pump-sourced skipped).
- Returns `false` (persistence handled in async path).

### Tell Chat (Async Target Resolve)

`OnTellChat(sender, msg)`:
- Parses target name from message text.
- Rejects oversized target names (beyond `Authentication.CharacterNameMaxLength`).
- Short-circuits self-tell before any async work (sends `TELL_ERROR_MESSAGE_SELF`).
- Enqueues async work (`OnTellChatAsync`):
  - Resolves target via `ICharacterService.FetchAsync(targetName)`.
  - Marshals to main thread:
    - Self-tell re-check → `TELL_ERROR_MESSAGE_SELF`.
    - Offline target → `TARGET_OFFLINE` status to sender.
    - Online target → `TELL_RELAYED` confirmation to sender, delivers message to target if local.
  - Enqueues persist with captured receive ticks (live messages only).
- Returns `false` (persistence handled in async path).

### Database Message Pump

`OnPeriodicMessagePump(deltaTime)`:
1. Acquires atomic `messagePumpInFlight` flag via `TryBeginMessagePump`.
2. Enqueues `FetchAndProcessChatMessagesAsync`:
   - Fetches new chat records after `LastFetchTime` / `LastFetchPosition` via `IChatService.FetchAsync`.
   - Marshals to main thread: updates cursor state, calls `ProcessChatMessages` which routes each record through the channel command map (sender = null for pump-sourced).
   - Discord channel messages routed to `OnSendDiscordMessage`.
3. Pump flag cleared in main-thread `finally` block (success) or async `finally` block (failure/early return).

### Failure Semantics

- Invalid messages fail closed: kicked or silently dropped with no mutation.
- Rate-limit and spam checks enforced before any channel routing.
- Async failures logged without blocking the main thread.
- Main-thread completion paths revalidate runtime state before broadcasting.
- `TryEnqueueAsyncWork` returns `false` when the queue is unavailable or full; a warning is logged.
- Shutdown guard at the top of async methods prevents work against disposed services.
- Synchronous persist flush on deinitialize ensures no messages are lost.

## Operational Checks

| Check | How to Verify |
|---|---|
| Initialization success | Confirm `ChatSystem` logs "Initialized (MessagePumpRate=2s, FetchCount=20)" without errors on server startup |
| Data containers available | Verify `IChatSystemRuntimeData`, `IChatSystemMainThreadQueueData`, and `AsyncWorkerData` all resolve from `DataContainerRegistry` |
| Channel command map built | Confirm all seven channel handlers (World, Region, Party, Guild, Tell, Trade, Say) are present in `ChannelCommandMap` |
| Broadcast handler registered | Confirm `ChatBroadcast` network handler is registered on initialize |
| Periodic callbacks registered | Confirm three periodic callbacks (message pump, persist flush, outbound flush) are registered |
| World chat | Send a `/w` message; confirm all characters in the same world receive the broadcast after the next outbound flush |
| Region chat | Send a `/r` message; confirm all connections in the sender's scene receive the broadcast immediately |
| Say chat | Send a `/s` message; confirm all observers of the sender receive the broadcast immediately |
| Party chat | Send a `/p` message while in a party; confirm all party members receive the broadcast |
| Guild chat | Send a `/g` message while in a guild; confirm all guild members receive the broadcast |
| Tell chat | Send `/t <name> <message>`; confirm sender receives `TELL_RELAYED` and target receives the message |
| Tell self-rejection | Send `/t <own_name>`; confirm sender receives `TELL_ERROR_MESSAGE_SELF` |
| Tell offline target | Send `/t <offline_name>`; confirm sender receives `TARGET_OFFLINE` status |
| Trade chat | Send a `/tr` message; confirm all characters in the same world receive the broadcast after the next outbound flush |
| System message | Call `OnSendSystemMessage(conn, message)`; confirm the connection receives a `ChatBroadcast` on the System channel |
| Discord relay | Confirm pump-sourced Discord messages are broadcast to all characters in the target world via `BroadcastToWorld` |
| Token bucket throttle | Send messages faster than `chatTokenRefillRate`; confirm messages are dropped when the bucket is exhausted |
| Legacy rate limit | Send messages faster than `messageRateLimit`; confirm excess messages are dropped |
| Repeat suppression | Send the same message twice with `allowRepeatMessages` = false; confirm the duplicate is dropped |
| Rich text sanitization | Send a message containing `<b>test</b>`; confirm tags are stripped |
| DoS queue cap | Flood the incoming queue beyond `maxIncomingQueueSize`; confirm the sender is kicked |
| Batch DB persistence | Send persist-eligible messages; confirm they appear in the database after the next persist flush interval |
| Outbound batch flush | Send multiple World/Trade messages; confirm they are delivered in a batch after `outboundBatchIntervalSeconds` |
| Outbound buffer cap | Buffer more than `maxBufferedWorldMessages` for a world; confirm oldest messages are dropped |
| Message pump | Wait for `messagePumpRate`; confirm `FetchAndProcessChatMessagesAsync` fires and fetched messages are routed |
| Pump-sourced bypass | Confirm pump-sourced World/Trade messages bypass the outbound buffer and broadcast immediately |
| Shutdown persist flush | Trigger deinitialize; confirm `FlushPersistQueueSync` drains all remaining persist entries |
| Async backpressure | Saturate async worker queue; confirm new work is rejected with a logged warning |
| Main-thread queue drain | Confirm queued async results are dispatched on the main thread within `maxMainThreadActionsPerFrame` per frame |
| Deinitialize cleanup | Trigger deinitialize; confirm outbound buffers flushed, persist queue flushed, incoming queue drained, broadcast handler unregistered, and periodic callbacks unregistered |

## Flow Diagram

### Inbound Message Pipeline

```
OnServerChatBroadcastReceived(conn, msg, channel)
│
├─ 1. Stamp msg.ReceivedUtcTicks = DateTime.UtcNow.Ticks
├─ 2. Atomic increment IncomingQueueSize
│     └── Over maxIncomingQueueSize → decrement + kick
└─ 3. Enqueue (conn, msg) into IncomingChatQueue

OnUpdate(deltaTime) → DrainIncomingChatQueue
│
├─ Dequeue up to maxIncomingChatsPerFrame entries
├─ Skip stale connections
└─ ProcessNewChatMessage(conn, sender, msg)
       │
       ├─ 1. Null / length validation → kick
       ├─ 2. Token bucket refill + consume → drop if empty
       ├─ 3. Legacy cooldown check → drop if too soon
       ├─ 4. Repeat-message filter → drop if duplicate
       ├─ 5. Rich text sanitization (skip if no '<')
       ├─ 6. Command extraction (GetCommandAndTrim)
       ├─ 7. Non-chat command check → return if handled
       ├─ 8. Chat command routing (TryParseChatCommand)
       ├─ 9. Channel ID prepend (Guild/Party/World ID)
       ├─ 10. Post-prepend length cap
       └─ 11. Channel handler.Invoke(sender, msg)
              └── Returns true → EnqueuePersist(...)
```

### World / Trade Chat (Outbound-Batched)

```
OnWorldChat(sender, msg) / OnTradeChat(sender, msg)
│
├─ Parse worldID from msg.Text
├─ Live sender:
│     ├─ Buffer into OutboundWorldBroadcastBuffer[worldID]
│     ├─ Hard cap: drop oldest if > maxBufferedWorldMessages
│     └─ Return true (triggers EnqueuePersist)
└─ Pump-sourced (sender == null):
      ├─ BroadcastToWorld(worldID, newMsg) immediately
      └─ Return false (already persisted)

OnPeriodicOutboundFlush → FlushOutboundBroadcastBuffers
│
├─ Collect world IDs into reusable key buffer
├─ For each world:
│     ├─ Send up to maxOutboundBatchSize messages per recipient
│     └─ Remove sent messages; keep overflow
└─ Clean up empty world entries
```

### Party / Guild Chat (Async Membership)

```
OnPartyChat(sender, msg) / OnGuildChat(sender, msg)
│
├─ Parse groupID from msg.Text
├─ Capture immutable data + receivedTicks
└─ TryEnqueueAsyncWork → OnPartyChatAsync / OnGuildChatAsync
       │
       ├─ FetchManyAsync(groupID) → member list
       ├─ TryEnqueueMainThread
       │     └─ Broadcast ChatBroadcast to each online member
       └─ EnqueuePersist(..., receivedTicks) if live message
```

### Tell Chat (Async Target Resolve)

```
OnTellChat(sender, msg)
│
├─ Parse targetName from msg.Text
├─ Reject oversized target names
├─ Short-circuit self-tell → TELL_ERROR_MESSAGE_SELF
├─ Capture immutable data + receivedTicks
└─ TryEnqueueAsyncWork → OnTellChatAsync
       │
       ├─ ICharacterService.FetchAsync(targetName)
       │     └── Not found → return
       ├─ TryEnqueueMainThread
       │     ├─ Self-tell re-check → TELL_ERROR_MESSAGE_SELF
       │     ├─ Offline → TARGET_OFFLINE to sender
       │     ├─ Online → TELL_RELAYED to sender
       │     └─ Deliver to local target if present
       └─ EnqueuePersist(..., receivedTicks) if live message
```

### Batch DB Persistence

```
OnPeriodicPersistFlush → FlushPersistQueueAsync
│
├─ Drain up to maxPersistBatchSize from PendingPersistQueue
├─ Convert ReceivedTicks → DateTime at DB boundary
└─ IChatService.PersistBatchAsync(batch)

OnDeinitialize → FlushPersistQueueSync
│
├─ Signal IsShuttingDown = true
├─ Drain ALL remaining from PendingPersistQueue
└─ PersistBatchAsync(...).GetAwaiter().GetResult() (blocking)
```

### Database Message Pump

```
OnPeriodicMessagePump(deltaTime)
│
├─ 1. Check Initialized + Server started
├─ 2. TryBeginMessagePump (atomic compare-exchange)
└─ 3. TryEnqueueAsyncWork → FetchAndProcessChatMessagesAsync
       │
       ├─ IChatService.FetchAsync(lastFetchTime, lastFetchPosition, fetchCount, sceneServerID)
       └─ TryEnqueueMainThread
              │
              ├─ Update LastFetchPosition + LastFetchTime
              ├─ ProcessChatMessages(messages)
              │     ├─ Discord → OnSendDiscordMessage
              │     └─ Other → channel handler (sender = null)
              └─ finally: ClearMessagePumpFlag
       │
       └─ finally (safety net): ClearMessagePumpFlag if not handed off
```

## Project Structure

### Directory Structure

```
Chat/
├── ChatSystem.cs                      # Main chat orchestration, parsing, routing, persistence dispatch, incoming queue drain, batch flush
├── ChatSystem.GroupChat.cs             # Partial: Party and Guild async channel handlers
├── ChatSystem.LocalChat.cs             # Partial: Region (scene-scoped) and Say (observer-scoped) broadcast handlers
├── ChatSystem.TellChat.cs              # Partial: Tell (private whisper) async target resolution handler
├── ChatSystem.WorldChat.cs             # Partial: World and Trade outbound-batched channel handlers + BroadcastToWorld
├── ChatSystemRuntimeData.cs           # Polling cursor state, lock-free queues, outbound buffers, broadcast scratch buffers
├── ChatSystemMainThreadQueueData.cs   # Per-system main-thread action queue container
└── README.md                          # System documentation
```

### Related Core Contracts

- `Server/Core/World/SceneServer/Chat/IChatSystem.cs`
- `Server/Core/World/SceneServer/Chat/IChatSystemRuntimeData.cs`
- `Server/Core/World/SceneServer/Chat/IChatSystemMainThreadQueueData.cs`

### Inheritance Hierarchy

```
ServerBehaviour
└── ChatSystem : IChatSystem (partial class)
       ├── ChatSystem.GroupChat.cs      # OnPartyChat, OnGuildChat + async handlers
       ├── ChatSystem.LocalChat.cs      # OnRegionChat, OnSayChat
       ├── ChatSystem.TellChat.cs       # OnTellChat + async handler
       └── ChatSystem.WorldChat.cs      # OnWorldChat, OnTradeChat, BroadcastToWorld

RuntimeDataContainer
└── ChatSystemRuntimeData : IChatSystemRuntimeData

SystemMainThreadQueueData
└── ChatSystemMainThreadQueueData : IChatSystemMainThreadQueueData
```

## License

This project is subject to the FishMMO project license.
