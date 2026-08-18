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

Automatic reconnection targets the WorldServer. `CanReconnect` gates it on the current
connection type — `World`, `Scene`, or `ConnectingToWorld`. A `Login` connection that drops
is not reconnectable and raises `OnConnectionAttemptFailed` instead, which is the signal to
invalidate the cached login server list and re-probe IPFetch.

- Maximum attempts: 10 (configurable)
- Base delay: 5 seconds (configurable)
- Maximum delay: 60 seconds (configurable)
- Algorithm: `baseDelay * 2^attempt` with 25% random jitter
- Reconnection is canceled by `ForceDisconnect()` or `CancelReconnect()`
- Every attempt dials `lastWorldAddress` / `lastWorldPort`, recorded on the last world
  connect. A dropped *Scene* connection therefore reconnects to the WorldServer, which
  re-routes the client, rather than back to the scene server directly.

### Why `ConnectingToWorld` counts as reconnectable

The retry loop spans two connection types, and missing either one breaks it:

1. A drop arms the retry timer, then clears `CurrentConnectionType` to `None`.
2. `TryReconnect()` calls `ConnectToServer(..., isWorldServer: true)`, which sets
   `ConnectingToWorld` for the in-flight attempt.
3. If that attempt fails, `ConnectingToWorld` is the only type still describing it — `World`
   and `Scene` are long gone.

So a `CanReconnect` that accepted only `World` and `Scene` reported false at step 3: no
further retry was armed, `MaxReconnectAttempts` never counted past one, `OnReconnectFailed`
(and the quit-to-login forward hanging off it) never fired, and the client sat disconnected
with no path forward. `OnConnectionAttemptFailed` fired in its place, throwing away the
cached login server list over what was a world server outage.

The same gate covers the initial Login→World hop, which is also typed `ConnectingToWorld`.
A world server that is down at server-select now retries with backoff behind the cancellable
`UIReconnectDisplay` and ends in quit-to-login, rather than failing silently on the first try.

## Connection Type Transitions

| From | Trigger | To | Set by |
|---|---|---|---|
| `None` | Login server authenticates | `Login` | `Client.OnAuthenticationResult` |
| `Login` | `ConnectToServer(isWorldServer: true)` | `ConnectingToWorld` | `ConnectToServer` |
| `ConnectingToWorld` | World server authenticates | `World` | `Client.OnAuthenticationResult` |
| `World` | Scene server authenticates after a hop | `Scene` | `Client.OnAuthenticationResult` |
| any | Connection stops, and it was not a deliberate hop | `None` | `OnClientConnectionState` |

A deliberate teardown — the `StopConnection()` that `ConnectToServer` issues before dialing
somewhere new — is flagged with `stoppingForConnect` and leaves the type alone, so
`ConnectingToWorld` survives the stop it was set for.

## Thread Safety

- `connectingGuard` uses `Interlocked.CompareExchange` for CAS-based concurrency control
- `forceDisconnect` is `volatile` for cross-thread visibility
- All state transitions happen on the Unity main thread via FishNet callbacks
