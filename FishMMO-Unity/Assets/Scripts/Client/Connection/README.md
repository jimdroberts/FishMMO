# Client Connection Manager

## Overview

Manages the client connection lifecycle: connect, disconnect, and automatic reconnection with exponential backoff.

## Connection States

- `None` — No active connection
- `Login` — Connected to LoginServer
- `ConnectingToWorld` — Transitioning to WorldServer
- `World` — Connected to WorldServer
- `Scene` — Connected to SceneServer

## Reconnection

Automatic reconnection is enabled for World and Scene connections only:

- Maximum attempts: 10 (configurable)
- Base delay: 5 seconds (configurable)
- Maximum delay: 60 seconds (configurable)
- Algorithm: `baseDelay * 2^attempt` with 25% random jitter
- Reconnection is canceled by `ForceDisconnect()` or `CancelReconnect()`

## Thread Safety

- `connectingGuard` uses `Interlocked.CompareExchange` for CAS-based concurrency control
- `forceDisconnect` is `volatile` for cross-thread visibility
- All state transitions happen on the Unity main thread via FishNet callbacks
