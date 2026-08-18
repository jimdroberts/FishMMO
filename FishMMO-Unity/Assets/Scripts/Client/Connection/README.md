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
is not reconnectable and raises `OnConnectionAttemptFailed` instead. `Client` responds to that
by invalidating the cached login server list (so the next attempt re-probes IPFetch) **and**
running `QuitToLogin`. Without the second half, a client kicked or timed out on the login
server was left with no visible panel at all: `UICharacterSelect` hides itself on any stop and
`UILogin` never re-showed, so the only recovery was restarting the client.

- Maximum attempts: 10 (configurable)
- Base delay: 5 seconds (configurable)
- Maximum delay: 60 seconds (configurable)
- Algorithm: `baseDelay * 2^attempt` with 25% random jitter
- **Exception — deliberate scene handoffs.** The first retry after a `Scene` connection drops
  uses `SceneHandoffReconnectDelay` (0.25 s, jittered) instead of the full base delay. A
  scene-to-scene transfer *is* a deliberate drop: the scene server releases the character and
  disconnects, expecting the client to return through the world server. Charging it the
  failure backoff cost every teleport and channel switch ~5 s of dead time, during which the
  scene had already unloaded. Only attempt 0 from a `Scene` drop is fast-pathed; if it fails,
  normal exponential backoff resumes from attempt 1, so an unreachable world server is still
  not hammered.
- Reconnection is canceled by `ForceDisconnect()` or `CancelReconnect()`
- `OnReconnectPending` fires the moment a retry is *armed*, before its delay elapses.
  `OnReconnectAttempt` fires when the retry actually starts, which leaves the whole backoff
  window unreported — and that window is not idle during a transfer, because the scene has
  already unloaded. Both loading screens treat "a reconnect is coming" as a transition in
  progress so the overlay never drops over an emptied world.
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

## `forceDisconnect` lifecycle

`ForceDisconnect()` latches a flag that suppresses the reconnect timer and
`OnConnectionAttemptFailed` for the stop it causes. Two rules keep that flag from being
stranded or cleared too early:

- **It is only latched when there is a connection left to tear down.** `OnClientConnectionState`
  is what consumes it, so latching against an already-`Stopped` connection strands it — no
  further transition arrives to clear it, and the next `ConnectToServer` aborts inside
  `OnAwaitingConnectionReady`, silently, with the guard released and no connection started.
  The login screen reaches that state routinely, because every auth-error dialog calls
  `ForceDisconnect` whether or not the transport is still up.
- **The `Stopped` handler consumes it.** `ResetReconnectState()` therefore only clears it when
  the connection is *already* stopped. `QuitToLogin` calls `ForceDisconnect` then
  `ResetReconnectState` on the same synchronous path, but the transport reports `Stopped` a
  frame or more later — clearing unconditionally meant that stop arrived with nothing marking
  it deliberate, and an ordinary quit-to-login was reported as a failed connection attempt.

## Leaked-guard recovery

`connectingGuard` is released on every exit path of `OnAwaitingConnectionReady`, but the
coroutine has to actually run for that to happen, and `CoroutineRunner` hosts it on a
GameObject this class does not own. If it never starts or is stopped from outside, the guard
stays acquired and every later connect is refused for the life of the process — which presents
as a login button that silently stops working. `ConnectToServer` therefore checks the age of
the acquisition when the CAS fails, and reclaims a guard held longer than any legitimate
acquisition could last (stop wait × 2 + establish timeout + 30 s).

## Thread Safety

- `connectingGuard` uses `Interlocked.CompareExchange` for CAS-based concurrency control
- `forceDisconnect` is `volatile` for cross-thread visibility
- All state transitions happen on the Unity main thread via FishNet callbacks
