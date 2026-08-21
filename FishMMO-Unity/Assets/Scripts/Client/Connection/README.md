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
server was left with no visible panel at all: `UITKCharacterSelect` hides itself on any stop and
`UITKLogin` never re-showed, so the only recovery was restarting the client.

That teardown also stages an `Unspecified` disconnect notice when the client is holding a
session token, so the player is told *something* — a login server that restarts or times a
connection out sends nothing, and `QuitToLogin` only surfaces a reason the server supplied. The
token check is what keeps it from firing on a first connect attempt that never reached a server,
where the login panel's own message ("the connection was closed before it answered") is both
more specific and more accurate.

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
`UITKReconnectDisplay` and ends in quit-to-login, rather than failing silently on the first try.

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

## Abort paths clear `stoppingForConnect`

`ConnectToServer` sets `stoppingForConnect` and expects the Stopped transition it asked for to
consume it. Every abort inside `OnAwaitingConnectionReady` is reached in exactly the cases
where that does not happen — the teardown timed out, or the connection was already stopped so
no transition was ever raised. `AbortConnectAttempt` therefore clears the flag along with the
guard; leaving it latched handed it to the *next* Stopped, which is a real drop, and a drop
marked deliberate arms no reconnect and raises no `OnConnectionAttemptFailed`, so the client
sat disconnected with nothing driving it anywhere.

## Loop guards are sized from the timeout, not a frame count

The waits inside `OnAwaitingConnectionReady` are bounded by `Time.realtimeSinceStartup`
deadlines. Their iteration counters are only a backstop against a clock that stops advancing,
so they are derived from the timeout (`IterationCapForSeconds`). Fixed frame counts made the
counter the *tighter* bound on any client rendering faster than count ÷ timeout — routine,
since FishNet's `ClientManager.FrameRate` defaults to 500 and the login screen renders almost
nothing. The 20 s establish wait aborted after roughly five seconds and reported an internal
"iteration limit exceeded" instead of a connection failure.

## Waiting is not disconnecting

Two of the pipeline's queues hold a client on a live, authenticated connection while it waits:
the LoginServer's admission queue, and the WorldServer's scene-routing queue (see
[World Scene System → Scene-routing queue feedback](../../Server/Implementation/World/WorldServer/WorldScene/README.md#scene-routing-queue-feedback)).
Neither involves this class — no connection state changes, so nothing here fires — which is
exactly why they need their own feedback channel. `Client` presents both through one shared
wait dialog so they cannot drift apart or fight over the same control, and the dialog's only
action leaves the queue via `QuitToLogin`.

## No panel waits on a reply forever

Every login-flow panel disables its action control, sends a request, and re-enables it when the
reply arrives. That is correct right up until the reply never arrives — at which point the panel
is a dead end, and the only way out is to quit to login.

The reply can go missing for reasons the client cannot see: the server's main-thread queue
rejecting the action at capacity, a handler throwing before it sends, or the server never
getting to it. `PendingReplyGuard` makes the panel stop waiting rather than enumerate those
causes. It is armed by the same method that disables the control and cleared by the same method
that re-enables it, so the two cannot drift apart.

Three properties matter:

- **Non-destructive.** The timeout re-enables the control and says so. Nothing is torn down and
  no connection is dropped, so a late reply is still handled normally when it lands.
- **It must not end the auth flow.** `UITKLogin` and `UITKRegister` gate their auth-result
  handler on `isAuthFlowActive`, which their *unlock* clears — so routing the timeout through
  the unlock would make the panel ignore a reply that arrives after the deadline, turning a
  slow login into a permanently stuck one. The timeout calls `ReleaseControls` instead, which
  only flips `SetEnabled` on the panel's own controls. A genuine disconnect still goes through
  the unlock, because then the flow really is over.
- **Progress refreshes it.** Any auth result at all is proof the server is still working the
  request — the SRP exchange and the two-factor prompt both report progress before they finish,
  and a client can sit in the login queue for minutes. Each one buys the deadline again.
- **So does a queue position.** The login queue is the one place a login legitimately outlasts
  the deadline, and its `LoginQueuePositionBroadcast` is handled by `Client`, not by the panels.
  Both login panels therefore register for it themselves and refresh the guard on any position
  ≥ 0. Without that the panel announced "the server did not respond" *beside* a live queue
  dialog, with sign-in re-enabled — and clicking it only produced "connection already in
  progress".

Server-select is deliberately **not** guarded. Its lock spans a whole multi-hop journey — world
login, scene routing, scene load — not one round trip, and that journey has its own queue
feedback with a Close button. A 30-second deadline there would fire during a perfectly healthy
scene load.

Character-select clears its guard on a successful selection, because the server list takes over
from there; leaving it armed would fire the timeout later and put the panel back on top of the
server-select screen.

## Unhandled exceptions from the networking stack

`Client.OnLogMessage` watches for exceptions raised inside the networking layer and tears the
session down, because a connection whose reader or transport has thrown cannot be trusted. Two
rules keep that from doing more harm than the fault it responds to:

- **Only the throwing frame counts.** Matching the whole stack caught `FishNet.Managing.` for
  every exception raised inside *any* broadcast or RPC handler, since that is the code that
  dispatches them — so a null reference in a HUD panel was indistinguishable from a corrupted
  transport stream, and both cost the player their session. `IsNetworkStack` now reads only the
  first line of the stack trace, which is the throw site.
- **It ends somewhere.** The teardown used to be `RevokeAndClearAuthToken` plus
  `ForceDisconnect`, and `ForceDisconnect` deliberately suppresses both the reconnect timer and
  `OnConnectionAttemptFailed` — so nothing ran afterwards. World scenes stayed loaded, no login
  panel appeared, and the token was already revoked, so waiting could not recover it. It routes
  through `QuitToLogin` instead.

## Thread Safety

- `connectingGuard` uses `Interlocked.CompareExchange` for CAS-based concurrency control
- `forceDisconnect` is `volatile` for cross-thread visibility
- All state transitions happen on the Unity main thread via FishNet callbacks
