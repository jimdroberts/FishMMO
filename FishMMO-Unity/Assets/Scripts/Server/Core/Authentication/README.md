# Server Core Authentication

**Short description:** Core contracts and immutable request types for the server-side SRP-6a authentication pipeline, defining the transport-agnostic interfaces for bounded-channel async processing of encrypted SRP verify and proof requests.

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

The Server Core Authentication module defines the transport-agnostic contracts that underpin FishMMO's server-side SRP-6a authentication pipeline. These types form the boundary between the network thread (which must never block) and the async worker threads (which perform heavy crypto, SRP math, and database I/O).

The module contains three types:

1. **`IAuthenticatorQueueData<TConnection>`** — An interface extending `IRuntimeDataContainer` that exposes two bounded `Channel<T>` instances (one for SRP verify requests, one for SRP proof requests) and a `CancellationTokenSource` for graceful worker shutdown. Implementations hold the runtime queue infrastructure that the network thread writes into and worker threads read from.

2. **`SrpVerifyRequest<TConnection>`** — A readonly struct carrying the immutable data needed to process an SRP verify request off the network thread: the originating connection, encrypted username bytes, encrypted client public ephemeral bytes, the explicit client-sent sequence number, and the per-connection `ConnectionEncryptionData` for AES-GCM nonce derivation on the worker thread. All fields remain encrypted until the worker explicitly decrypts them.

3. **`SrpProofRequest<TConnection>`** — A readonly struct carrying the immutable data needed to process an SRP proof request off the network thread: the originating connection, encrypted client proof bytes, the explicit client-sent sequence number, and the per-connection `ConnectionEncryptionData`. Like the verify request, the encrypted payload is only decrypted on the worker thread.

All three types are generic over `TConnection`, maintaining engine independence — the concrete FishNet implementation binds `TConnection` to `NetworkConnection`, but the core contracts impose no transport dependency. This separation allows the core authentication logic to be tested and reasoned about independently of FishNet.

The broader server authentication architecture (which consumes these core types) implements:

- **SRP-6a (2048-bit, SHA-512)** — zero-knowledge password proof where the server stores only a salt and verifier.
- **X25519 ECDH key exchange** — ephemeral keypair per connection, HKDF-SHA256 key derivation into directional AES-256 session keys.
- **AES-256-GCM authenticated encryption** — counter-based deterministic nonces with AAD binding to `(messageType, agreedVersion, sequenceNumber)`.
- **HMAC-SHA256 stateless cookie challenge** — proof-of-reachability before allocating server-side state.
- **Bounded-channel async pipeline** — network-thread broadcast handlers enqueue; worker threads decrypt, validate, and perform DB I/O; results are marshalled to the Unity main thread.
- **Fail-closed error indistinguishability** — all failure paths use `RejectAndPurge`: broadcast generic result, disconnect, purge state.
- **Aggressive memory hygiene** — `CryptographicOperations.ZeroMemory()` on all decrypted buffers immediately after use; key material zeroed on disconnect; credentials nulled at earliest possible point.

## Supported Platforms

| Platform | Supported | Notes |
|----------|-----------|-------|
| Windows  | Yes       | Full SRP-6a + AES-256-GCM server authentication pipeline |
| Linux    | Yes       | Full SRP-6a + AES-256-GCM server authentication pipeline |
| WebGL    | N/A       | Server-only module; not applicable to WebGL builds |

| Requirement       | Version / Detail |
|-------------------|------------------|
| Unity             | 6.3 LTS          |
| Scripting Backend | IL2CPP           |

## Features

- **Transport-agnostic generics** — all core types are generic over `TConnection`, decoupling authentication contracts from any specific networking library.
- **Bounded-channel architecture** — `IAuthenticatorQueueData<TConnection>` exposes `Channel<SrpVerifyRequest<TConnection>>` and `Channel<SrpProofRequest<TConnection>>` for backpressure-aware async processing with `DropWrite` overflow policy.
- **Immutable request structs** — `SrpVerifyRequest<TConnection>` and `SrpProofRequest<TConnection>` are `readonly struct` types, ensuring request data cannot be mutated after enqueue and enabling safe concurrent access without locking.
- **Deferred decryption** — encrypted payloads (`EncryptedUsername`, `EncryptedPublicEphemeral`, `EncryptedClientProof`) remain as raw `byte[]` until the worker thread explicitly decrypts them, keeping the network thread free of any crypto overhead.
- **Per-connection encryption context** — each request carries a `ConnectionEncryptionData` reference, providing the worker thread with the symmetric key, session prefix, and counters needed to derive unique AES-GCM nonces independently of the network thread.
- **Explicit sequence numbers** — each request includes the client-sent `Seq` value, enabling AAD binding of `(messageType, agreedVersion, sequenceNumber)` during worker-side decryption.
- **Graceful shutdown** — `IAuthenticatorQueueData<TConnection>.CancellationTokenSource` enables cooperative cancellation of all async workers during authenticator shutdown.
- **IRuntimeDataContainer inheritance** — `IAuthenticatorQueueData<TConnection>` extends the `IRuntimeDataContainer` marker interface, integrating with FishMMO's runtime data lifecycle management.

### Security Features (Full Pipeline)

These features are implemented by the concrete authenticators that consume the core contracts:

- **SRP-6a zero-knowledge password proof** — 2048-bit group, SHA-512; server stores only salt + verifier; password never transmitted.
- **X25519 ECDH key exchange** — fresh ephemeral keypair per connection; private key zeroed and disposed immediately after ECDH derivation.
- **Transcript-bound key derivation** — SHA-256 transcript hash incorporating domain separator, both public keys, and version range prevents downgrade and key-substitution attacks.
- **Directional AES-256-GCM session keys** — HKDF derives separate `clientToServerKey` and `serverToClientKey` plus directional nonce prefixes.
- **Counter-based deterministic nonces** — 12-byte nonces: 4-byte HKDF prefix + 1-byte direction + 3-byte padding + 4-byte big-endian counter; overflow throws `CryptographicException`.
- **AAD binding** — each ciphertext bound to `(messageType, agreedVersion, sequenceNumber)` via `CryptoHelper.BuildAad()`.
- **HMAC-SHA256 stateless cookie challenge** — proof-of-reachability with fail-closed key rotation on restart.
- **Protocol version negotiation** — client advertises `[Min, Max]`; server validates and agrees; transcript-bound to prevent downgrade.
- **Buffer zeroing** — `CryptographicOperations.ZeroMemory()` on all decrypted plaintext buffers, AES session keys, session prefixes, and X25519 private keys.
- **Strict UTF-8 decoding** — `StrictUtf8` with `DecoderFallbackException` rejects malformed byte sequences, preventing UTF-8 smuggling and null-byte injection.
- **Constant-time comparison** — `CryptographicOperations.FixedTimeEquals` for timing-safe byte comparisons.
- **Error indistinguishability** — `RejectAndPurge` ensures all failure paths look identical (same broadcast, same channel, same state purge).

### Production Safeguards (Full Pipeline)

- **Per-connection in-flight gating** — at most one in-flight SRP request per connection per phase; duplicates silently dropped.
- **Stale authentication TTL** — 15-second sweep disconnects and purges half-open connections; 60-second hard deadline prevents unbounded TTL extension.
- **Kick-request write debounce** — at most one `IKickRequestService.PersistAsync(accountName)` per 10 seconds per account via `ExpiringKeyTracker<string>`.
- **Upstream rate limiting** — IP-based debounce on SRP verify ingress, account-based debounce before DB lookup, per-IP rate limiting on account creation with failure tracking and automatic blocking.
- **Connection IP cache TTL** — `LastSeenCacheTracker<int, string>` with bounded sweep to prevent unbounded memory growth.
- **AccountManager backstop sweep** — `ArrivalOrderTracker` with oldest-first traversal purges stale SRP/encryption state.
- **Max pending auth cap** — `MaxPendingAuthConnections` (10,000) prevents memory exhaustion from half-open connection floods.
- **Bounded channel capacity** — verify: 500, proof: 500, token: 500, account creation: 1000; all with `DropWrite` → `ServerBusy`.
- **Time-sliced main-thread drain** — `MaxMainThreadActionsPerUpdate` (100) prevents frame spikes from queue bursts.

## Prerequisites

- Unity 6.3 LTS with IL2CPP scripting backend.
- FishNet Networking framework (`Authenticator` base class, `NetworkConnection`, `Channel`, `Broadcast` system).
- `FishMMO.Server.Core.Account` — `ConnectionEncryptionData` for per-connection AES keys, nonce counters, and sequence tracking; `IAccountManager<TConnection>`, `ISrpAccountManager<TConnection>`, `ITokenAccountManager<TConnection>`.
- `IRuntimeDataContainer` interface — marker for runtime data lifecycle integration.
- `System.Threading.Channels` (.NET BCL) — `Channel<T>` for bounded async producer-consumer queues.
- `System.Threading.CancellationTokenSource` (.NET BCL) — cooperative cancellation for async workers.

## Installation / Build

This is an integrated module within the FishMMO Unity project. No separate installation steps are required.

The core authentication types are compiled as part of the server core assembly and have no direct dependencies on FishNet or Unity — they depend only on `System.Threading.Channels`, `System.Threading.CancellationTokenSource`, and other `FishMMO.Server.Core` interfaces. The concrete implementations in `Server/Implementation/Authentication/` bind these generics to FishNet's `NetworkConnection`.

## Quick Start Guides

### Implementing a Custom Authenticator Queue

1. Create a class that implements `IAuthenticatorQueueData<TConnection>` for your connection type.
2. Initialize bounded channels with appropriate capacity and `BoundedChannelFullMode.DropWrite`:
   ```csharp
   var options = new BoundedChannelOptions(500)
   {
       FullMode = BoundedChannelFullMode.DropWrite,
       SingleReader = false,
       SingleWriter = false
   };
   VerifyChannel = Channel.CreateBounded<SrpVerifyRequest<TConnection>>(options);
   ProofChannel = Channel.CreateBounded<SrpProofRequest<TConnection>>(options);
   ```
3. Create a `CancellationTokenSource` for worker lifecycle management.
4. Start async workers that read from each channel and process requests.

### Enqueuing Requests from the Network Thread

1. In your broadcast handler (network thread), construct an immutable request:
   ```csharp
   var request = new SrpVerifyRequest<NetworkConnection>(
       connection,
       encryptedUsername,
       encryptedPublicEphemeral,
       encryptionData,
       seq);
   ```
2. Non-blocking enqueue via `TryWrite`:
   ```csharp
   if (!queueData.VerifyChannel.Writer.TryWrite(request))
   {
       // Channel full — send ServerBusy and roll back auth state
   }
   ```
3. The worker thread reads, decrypts, validates, and performs DB I/O asynchronously.

### Processing SRP Proof Requests

1. Read from the proof channel in an async worker loop:
   ```csharp
   await foreach (var request in queueData.ProofChannel.Reader.ReadAllAsync(cts.Token))
   {
       // Decrypt request.EncryptedClientProof using request.EncryptionData
       // Verify SRP proof against stored session
       // Enqueue result to main-thread queue
   }
   ```
2. On success, enqueue `SrpSuccessBroadcast` with encrypted server proof and auth token to the main-thread queue.
3. On failure, call `RejectAndPurge` to broadcast generic failure, disconnect, and purge state.

## Configuration

### Bounded Channel Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| Verify channel capacity | 500 | Maximum queued SRP verify requests |
| Proof channel capacity | 500 | Maximum queued SRP proof requests |
| Token channel capacity | 500 | Maximum queued token auth requests |
| Account creation channel capacity | 1000 | Maximum queued account creation requests |
| Full mode | `DropWrite` | Overflow policy — excess requests get `ServerBusy` |
| Reader/Writer concurrency | `false` / `false` | Allow multiple concurrent readers and writers |

### Worker Configuration

| Parameter | Default | Description |
|-----------|---------|-------------|
| Verify worker count | 2 | Concurrent workers processing SRP verify requests |
| Proof worker count | 2 | Concurrent workers processing SRP proof requests |
| Token worker count | 2 | Concurrent workers processing token auth requests |

### Authentication Timing

| Parameter | Default | Description |
|-----------|---------|-------------|
| `AuthStaleTtlSeconds` | 15 s | Max time for a connection to complete authentication before purge |
| `AuthHardDeadlineSeconds` | 60 s | Absolute limit from auth start; prevents unbounded TTL extension |
| `AuthSweepIntervalSeconds` | 1 s | How often stale auth entries are scanned |
| `AuthSweepMaxScan` | 256 | Max entries evaluated per sweep cycle |
| `AuthSweepMaxRemovals` | 64 | Max entries purged per sweep cycle |
| `MaxPendingAuthConnections` | 10,000 | Cap on concurrent pending auth connections |
| `MaxMainThreadActionsPerUpdate` | 100 | Max queued actions drained per Unity frame |

### Rate Limiting

| Parameter | Default | Description |
|-----------|---------|-------------|
| `IpAuthAttemptDebounceSeconds` | 1 s | Min seconds between SRP verify attempts from the same IP |
| `AccountVerifyDebounceSeconds` | 2 s | Min seconds between SRP verify attempts for the same account |
| `KickRequestDebounceSeconds` | 10 s | Min seconds between persisted kick requests per account |
| `KickDebounceSweepIntervalSeconds` | 60 s | Sweep interval for expired kick-request debounce entries |

### SRP Parameters

| Parameter | Value |
|-----------|-------|
| Protocol | SRP-6a (RFC 5054 variant) |
| Group | 2048-bit |
| Hash | SHA-512 |
| Library | `SecureRemotePassword` |

### AES-256-GCM Parameters

| Parameter | Value |
|-----------|-------|
| Algorithm | AES-256-GCM |
| Nonce | 12 bytes (deterministic, counter-based) |
| AAD | `(messageType, agreedVersion, sequenceNumber)` |
| Key derivation | HKDF-SHA256 from X25519 shared secret |
| Directional keys | Client→Server key + Server→Client key |

### Validation Rules (from `FishMMO.Shared.Authentication`)

| Rule | Constraint |
|------|-----------|
| `IsAllowedUsername` | 3–32 chars, `[a-zA-Z0-9_]+` |
| `IsAllowedPassword` | 8–32 chars, `[a-zA-Z0-9!@#$%^&*()_+=\-\[\]{}|;:',.<>?]+` |
| `IsAllowedCharacterName` | 3–24 chars, letters + single internal spaces |
| `IsAllowedGuildName` | 3–32 chars, alphanumeric + single internal spaces |
| `IsAllowedEmailUsername` | 3–320 chars, RFC-adjacent email validation |

## Usage Examples

### Constructing an SRP Verify Request

```csharp
// On the network thread — zero blocking, no crypto, no DB I/O
var request = new SrpVerifyRequest<NetworkConnection>(
    connection:              conn,
    encryptedUsername:        message.Username,      // raw encrypted bytes
    encryptedPublicEphemeral: message.PublicEphemeral, // raw encrypted bytes
    encryptionData:          accountManager.GetEncryptionData(conn),
    seq:                     message.Seq);

if (!verifyChannel.Writer.TryWrite(request))
{
    // Channel full — DropWrite policy
    // Roll back auth state so client can retry
    // Send ServerBusy result
}
```

### Constructing an SRP Proof Request

```csharp
// On the network thread — ultra-fast UDP gate
var request = new SrpProofRequest<NetworkConnection>(
    connection:          conn,
    encryptedClientProof: message.Proof,    // raw encrypted bytes
    encryptionData:      accountManager.GetEncryptionData(conn),
    seq:                 message.Seq);

if (!proofChannel.Writer.TryWrite(request))
{
    // Channel full — send ServerBusy + disconnect
}
```

### Implementing IAuthenticatorQueueData

```csharp
public class AuthenticatorQueueData : IAuthenticatorQueueData<NetworkConnection>
{
    public Channel<SrpVerifyRequest<NetworkConnection>> VerifyChannel { get; }
    public Channel<SrpProofRequest<NetworkConnection>> ProofChannel { get; }
    public CancellationTokenSource CancellationTokenSource { get; }

    public AuthenticatorQueueData(int verifyCapacity, int proofCapacity)
    {
        var verifyOptions = new BoundedChannelOptions(verifyCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = false,
            SingleWriter = false
        };
        var proofOptions = new BoundedChannelOptions(proofCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = false,
            SingleWriter = false
        };

        VerifyChannel = Channel.CreateBounded<SrpVerifyRequest<NetworkConnection>>(verifyOptions);
        ProofChannel = Channel.CreateBounded<SrpProofRequest<NetworkConnection>>(proofOptions);
        CancellationTokenSource = new CancellationTokenSource();
    }
}
```

### Worker Thread Processing Pattern

```csharp
// Async worker — runs on thread pool, never on network or main thread
private async Task ProcessSrpVerifyAsync(
    ChannelReader<SrpVerifyRequest<NetworkConnection>> reader,
    CancellationToken ct)
{
    await foreach (var request in reader.ReadAllAsync(ct))
    {
        try
        {
            // 1. Decrypt username using request.EncryptionData
            byte[] usernameBytes = CryptoHelper.AesGcmDecrypt(
                request.EncryptedUsername,
                request.EncryptionData, request.Seq - 1,
                MessageType.SrpVerify);

            string username = CryptoHelper.StrictUtf8.GetString(usernameBytes);
            CryptographicOperations.ZeroMemory(usernameBytes);

            // 2. Decrypt client ephemeral
            byte[] ephemeralBytes = CryptoHelper.AesGcmDecrypt(
                request.EncryptedPublicEphemeral,
                request.EncryptionData, request.Seq,
                MessageType.SrpVerify);

            string ephemeral = CryptoHelper.StrictUtf8.GetString(ephemeralBytes);
            CryptographicOperations.ZeroMemory(ephemeralBytes);

            // 3. DB fetch, SRP state setup, enqueue response to main thread
        }
        catch (CryptographicException)
        {
            // AES-GCM failure — connection-fatal
            EnqueueMainThread(() => RejectAndPurge(request.Connection, result));
        }
        catch (DecoderFallbackException)
        {
            // Malformed UTF-8 — disconnect
            EnqueueMainThread(() => RejectAndPurge(request.Connection, result));
        }
    }
}
```

## Operational Checks

| Check | How to Verify | Expected Result |
|-------|---------------|-----------------|
| Verify channel accepts requests | Construct `SrpVerifyRequest` and `TryWrite` to channel | Returns `true` when channel has capacity |
| Verify channel applies backpressure | Fill channel to capacity and `TryWrite` again | Returns `false` (DropWrite policy) |
| Proof channel accepts requests | Construct `SrpProofRequest` and `TryWrite` to channel | Returns `true` when channel has capacity |
| Proof channel applies backpressure | Fill channel to capacity and `TryWrite` again | Returns `false` (DropWrite policy) |
| Cancellation stops workers | Signal `CancellationTokenSource.Cancel()` | All `ReadAllAsync` loops exit cleanly |
| Request immutability holds | Attempt to modify fields on `SrpVerifyRequest` | Compile error — `readonly struct` with `readonly` fields |
| Request immutability holds (proof) | Attempt to modify fields on `SrpProofRequest` | Compile error — `readonly struct` with `readonly` fields |
| Encrypted data preserved | Enqueue request, dequeue on worker, inspect `EncryptedUsername` | Bytes are identical to original; no decryption occurred during enqueue/dequeue |
| Sequence number preserved | Enqueue request with `Seq=42`, read on worker | `request.Seq == 42` |
| EncryptionData reference preserved | Enqueue request, verify `EncryptionData` on worker | Same `ConnectionEncryptionData` instance as provided |
| Generic type binding | Implement `IAuthenticatorQueueData<NetworkConnection>` | Compiles and functions with FishNet's connection type |
| SRP login completes end-to-end | Connect a client with valid credentials | `ClientAuthResultBroadcast` with `LoginSuccess`; credentials nulled; keys established |
| Account creation completes | Connect with `register: true` and valid credentials | `ClientAuthResultBroadcast` with `AccountCreated` |
| Token reconnection works | After SRP login, connect to World server | `TokenAuthBroadcast` sent; `WorldLoginSuccess` received |
| Stale auth purged | Start handshake but do not complete SRP within 15 seconds | Connection disconnected and all state purged |
| In-flight gating prevents duplicates | Send duplicate `SrpVerifyBroadcast` while first is processing | Second packet silently dropped |
| Rate limiting rejects rapid attempts | Send multiple SRP verify from same IP within 1 second | Subsequent attempts debounced before channel write |
| AES-GCM failure disconnects | Send corrupted encrypted payload | `CryptographicException` caught; generic failure broadcast; disconnect; state purged |
| Malformed UTF-8 disconnects | Send invalid UTF-8 in encrypted field | `DecoderFallbackException` caught; disconnect; buffers zeroed |
| Key material zeroed on disconnect | Disconnect authenticated connection | AES keys, session prefix, nonce contexts all zeroed via `ClearKeyMaterial` |

## Flow Diagram

### Thread Model

```
Network Thread (UDP Gates)              Worker Threads (Async)              Main Thread (Unity)
┌─────────────────────────┐        ┌────────────────────────────┐     ┌──────────────────┐
│ OnSrpVerifyReceived()   │──────► │ ProcessSrpVerifyAsync()    │────►│ DrainMainThread() │
│ OnSrpProofReceived()    │ Write  │ ProcessSrpProofAsync()     │ Enq │   • Broadcast()   │
│ OnCreateAccountReceived │ to     │ ProcessAccountCreation()   │ ──► │   • Disconnect()  │
│                         │Channel │                            │     │   • OnAuthResult() │
│ • Validate connection   │        │ • AES-GCM decrypt          │     └──────────────────┘
│ • In-flight gate        │        │ • StrictUtf8 decode         │
│ • Size checks           │        │ • ZeroMemory buffers       │
│ • TryWrite (non-block)  │        │ • DB I/O (async/await)     │
│ • Zero blocking         │        │ • SRP math                 │
└─────────────────────────┘        └────────────────────────────┘
```

| Thread | Responsibilities | Blocking Allowed |
|--------|-----------------|------------------|
| **Network Thread** | UDP gate: validate, in-flight gate, size check, enqueue | **No** (zero blocking) |
| **Worker Threads** | AES decrypt, StrictUtf8 decode, ZeroMemory, DB I/O, SRP math | **Yes** (async/await) |
| **Main Thread** | `Broadcast()`, `Disconnect()`, `OnAuthenticationResult`, drain queue | N/A (Unity main loop) |

### Bounded Channel Pipeline

| Channel | Capacity | Workers | Drop Policy | System |
|---------|----------|---------|-------------|--------|
| `verifyChannel` | 500 | 2 | `DropWrite` → `ServerBusy` | ServerAuthenticator (SRP) |
| `proofChannel` | 500 | 2 | `DropWrite` → `ServerBusy` + disconnect | ServerAuthenticator (SRP) |
| `tokenChannel` | 500 | 2 | `DropWrite` → `ServerBusy` | TokenServerAuthenticator |
| `AsyncWorkerData` | 1000 | configurable | `DropWrite` → `ServerBusy` | AccountCreationSystem |

### SRP Request Lifecycle

```
SrpVerifyBroadcast arrives (network thread)
    │
    ├── Validate connection exists
    ├── Check in-flight gate (verifyInFlightByClientId)
    ├── Check payload size (< MaxSrpPayloadBytes)
    ├── Construct SrpVerifyRequest<TConnection> (immutable readonly struct)
    │       • Connection
    │       • EncryptedUsername (raw bytes — NO decryption here)
    │       • EncryptedPublicEphemeral (raw bytes)
    │       • Seq (explicit sequence number)
    │       • ConnectionEncryptionData (reference)
    │
    ├── TryWrite to VerifyChannel
    │       ├── Success → worker picks up
    │       └── Failure (full) → roll back auth state, send ServerBusy
    │
    └── Worker thread (ProcessSrpVerifyAsync)
            ├── AES-GCM decrypt username (using EncryptionData + Seq)
            ├── StrictUtf8.GetString() → ZeroMemory(usernameBytes)
            ├── AES-GCM decrypt ephemeral → ZeroMemory(ephemeralBytes)
            ├── Rate-limit check (IP + account debounce)
            ├── DB fetch (salt, verifier, access level)
            ├── SRP state setup (AccountManager)
            ├── Encrypt salt + server ephemeral
            └── Enqueue response broadcast to main thread
```

```
SrpProofBroadcast arrives (network thread)
    │
    ├── Validate connection + auth state
    ├── Check in-flight gate (proofInFlightByClientId)
    ├── Construct SrpProofRequest<TConnection> (immutable readonly struct)
    │       • Connection
    │       • EncryptedClientProof (raw bytes)
    │       • Seq (explicit sequence number)
    │       • ConnectionEncryptionData (reference)
    │
    ├── TryWrite to ProofChannel
    │       ├── Success → worker picks up
    │       └── Failure (full) → roll back auth state, send ServerBusy + disconnect
    │
    └── Worker thread (ProcessSrpProofAsync)
            ├── AES-GCM decrypt proof (using EncryptionData + Seq)
            ├── StrictUtf8.GetString() → ZeroMemory(proofBytes)
            ├── SRP proof verification
            ├── TryLoginAsync() (check locks, kicks, world state)
            ├── Generate auth token, encrypt server proof + token
            └── Enqueue SrpSuccessBroadcast to main thread
```

### Complete Authentication Sequence

```
Client                              LoginServer                          WorldServer
  │                                      │                                    │
  │── ClientHandshake ─────────────────► │                                    │
  │   { X25519 PublicKey }               │                                    │
  │                                      │  X25519 ECDH + HKDF-SHA256        │
  │                                      │  → directional AES keys + prefix   │
  │◄── ServerHandshake ──────────────── │                                    │
  │   { X25519 PublicKey, Cookie }       │                                    │
  │                                      │                                    │
  │  [Login Path]                        │                                    │
  │── SrpVerifyBroadcast ──────────────► │  UDP Gate → verifyChannel          │
  │   { Username(enc), Ephemeral(enc) }  │  Worker: decrypt, DB fetch,        │
  │                                      │    SRP state setup                 │
  │◄── SrpVerifyBroadcast ──────────── │                                    │
  │   { Salt(enc), ServerEphemeral(enc)} │                                    │
  │                                      │                                    │
  │  SRP proof computation               │                                    │
  │  ** Null username + password **      │                                    │
  │                                      │                                    │
  │── SrpProofBroadcast ──────────────► │  UDP Gate → proofChannel           │
  │   { ClientProof(encrypted) }        │  Worker: decrypt, verify proof,     │
  │                                      │    TryLoginAsync()                 │
  │◄── SrpSuccessBroadcast ──────────── │  → LoginSuccess + auth token       │
  │   { ServerProof(enc), Result }      │                                    │
  │                                      │                                    │
  │  [World Server Connection]           │                                    │
  │── ClientHandshake ──────────────────────────────────────────────────────► │
  │◄── ServerHandshake ─────────────────────────────────────────────────────  │
  │── TokenAuthBroadcast ───────────────────────────────────────────────────► │
  │   { HMAC-signed auth token }        │                                    │  Token verify
  │                                      │                                    │  TryLoginAsync()
  │                                      │                                    │    • Rate limit
  │                                      │                                    │    • Lock check
  │                                      │                                    │    • Player cap
  │                                      │                                    │    • Character query
  │◄── AuthResult ──────────────────────────────────────────────────────────  │
  │   { WorldLoginSuccess }             │                                    │
  │                                      │                                    │
  │  [Register Path]                     │                                    │
  │── CreateAccountBroadcast ──────────► │  UDP Gate → AsyncWorkerData        │
  │   { Username(enc), Salt(enc),        │  Worker: decrypt, StrictUtf8,      │
  │     Verifier(enc) }                  │    validate, DB persist            │
  │◄── AuthResult ──────────────────── │                                    │
  │   { AccountCreated }                │                                    │
```

### Counter-Based Nonce Scheme

```
┌─────────────┬───────────┬────────────┬─────────────┐
│ Prefix (4B) │ Dir (1B)  │ Pad (3B)   │ Counter (4B)│
│ HKDF-       │ 0x00=C→S  │ 0x00 0x00  │ big-endian  │
│ derived     │ 0x01=S→C  │ 0x00       │ uint32      │
└─────────────┴───────────┴────────────┴─────────────┘

Session prefix: 4 bytes from HKDF, unique per connection.
Direction byte: Prevents reflection attacks (client→server ≠ server→client).
Counter: Monotonically incremented per operation. Throws CryptographicException at uint.MaxValue.
```

### Buffer Zeroing Lifecycle

| Buffer | Zeroed When |
|--------|-------------|
| Decrypted username bytes | Immediately after `StrictUtf8.GetString()` |
| Decrypted salt bytes | Immediately after string conversion |
| Decrypted verifier bytes | Immediately after string conversion |
| Decrypted ephemeral bytes | Immediately after string conversion |
| Decrypted proof bytes | Immediately after use |
| AES-256 session keys | On disconnect (`ClearKeyMaterial`) |
| Session prefix bytes | On disconnect |
| X25519 private key | Immediately after ECDH derivation (via `Dispose`) |
| HMAC cookie key | On `Deinitialize` |

## Project Structure

### Directory Tree

```
Server/Core/Authentication/
├── IAuthenticatorQueueData.cs    # Interface: bounded channels + CancellationTokenSource for async SRP processing
├── SrpVerifyRequest.cs           # Readonly struct: encrypted username + ephemeral + seq + encryption context
├── SrpProofRequest.cs            # Readonly struct: encrypted client proof + seq + encryption context
└── README.md                     # This file
```

### Related Core Modules

```
Server/Core/Account/
├── IAccountManager.cs            # Base interface: encryption state, account lookup, auth state machine, lifecycle
├── ISrpAccountManager.cs         # Extended interface: SRP-specific connection account creation + sweep
├── ITokenAccountManager.cs       # Extended interface: token-based account creation (no SRP state)
├── ConnectionEncryptionData.cs   # Per-connection AES keys, nonce counters, sequence tracking
├── AccountData.cs                # Per-connection auth state, access level, SRP data
└── AuthState.cs                  # Auth state machine enum (Handshake → VerifyPending → WaitingForProof → Authenticated)

Server/Core/Collections/
├── ExpiringKeyTracker.cs         # Head-first expiry queue for rate limiting
├── LastSeenCacheTracker.cs       # TTL cache with bounded sweep
└── ArrivalOrderTracker.cs        # Oldest-first tracker for stale-connection sweeps
```

### Implementation Modules (FishNet-Specific)

```
Server/Implementation/Authentication/
├── IServerAuthenticator.cs       # Interface: Server reference + worker lifecycle
├── BaseServerAuthenticator.cs    # Abstract base: X25519 ECDH, main-thread queue, TTL sweeps, RejectAndPurge
├── ServerAuthenticator.cs        # SRP-6a pipeline with bounded channels (LoginServer)
└── TokenServerAuthenticator.cs   # Token auth pipeline with bounded channel (World/Scene)

Server/Implementation/World/WorldServer/Authentication/
└── WorldServerAuthenticator.cs   # World-entry gate: player cap, lock check, character query

Server/Implementation/World/SceneServer/Authentication/
└── SceneServerAuthenticator.cs   # Scene-entry pass-through

Server/Implementation/Account/
├── AccountManager.cs             # Thread-safe base: dictionaries, sync lock, arrival-order tracking
├── SrpAccountManager.cs          # SRP-specific: SRP data population, stale sweep
└── TokenAccountManager.cs        # Token-specific: simplified account creation

Server/Implementation/LoginServer/AccountCreation/
└── AccountCreationSystem.cs      # Async account creation pipeline with bounded channel
```

### Client Module

```
Client/Authentication/
├── ClientLoginAuthenticator.cs   # Client-side SRP + token auth + account creation flow
└── ClientSrpData.cs              # Client SRP state (ephemeral, proof, session verify, salt/verifier generation)
```

### Shared Modules

```
Shared/Implementation/Network/Authentication/
├── ClientHandshake.cs            # Broadcast: client public key + cookie + version range
├── ServerHandshake.cs            # Broadcast: server public key + cookie + agreed version
├── SrpVerifyBroadcast.cs         # Broadcast: encrypted username/ephemeral (request) or salt/ephemeral (response)
├── SrpProofBroadcast.cs          # Broadcast: encrypted client proof
├── SrpSuccessBroadcast.cs        # Broadcast: encrypted server proof + auth token + result
├── CreateAccountBroadcast.cs     # Broadcast: encrypted username, salt, verifier
├── TokenAuthBroadcast.cs         # Broadcast: encrypted stored auth token
└── ClientAuthResultBroadcast.cs  # Broadcast: authentication result enum

Shared/Implementation/Tools/Extensions/Crypto/
└── CryptoHelper.cs               # AES-256-GCM, X25519 ECDH, HKDF-SHA256, StrictUtf8, GcmNonceContext, BuildAad

FishMMO-SharedUtility/
└── Authentication.cs             # Centralized validation rules (cross-project)
```

### Inheritance Hierarchy

```
Authenticator (FishNet, abstract)
└── BaseServerAuthenticator                    # X25519 ECDH, main-thread queue, TTL sweeps, RejectAndPurge
    ├── ServerAuthenticator                    # SRP-6a pipeline (LoginServer)
    │   └── [LoginServer uses this directly]
    └── TokenServerAuthenticator               # HMAC-signed token pipeline (World/Scene)
        ├── WorldServerAuthenticator           # World-entry gate (player cap, selected character)
        └── SceneServerAuthenticator           # Scene-entry pass-through

ServerBehaviour
└── AccountCreationSystem                      # Async account creation (LoginServer)
```

```
IRuntimeDataContainer
└── IAuthenticatorQueueData<TConnection>       # Bounded channels + CancellationTokenSource

IAccountManager<TConnection>
├── ISrpAccountManager<TConnection>            # SRP-specific (LoginServer)
│   └── SrpAccountManager (concrete)
└── ITokenAccountManager<TConnection>          # Token-specific (World/Scene)
    └── TokenAccountManager (concrete)
```

### Key Types

| Type | Purpose |
|------|---------|
| `IAuthenticatorQueueData<TConnection>` | Interface exposing bounded channels for SRP verify/proof requests and a CancellationTokenSource for worker shutdown |
| `SrpVerifyRequest<TConnection>` | Immutable readonly struct carrying encrypted username, encrypted ephemeral, sequence number, and encryption context for async worker processing |
| `SrpProofRequest<TConnection>` | Immutable readonly struct carrying encrypted client proof, sequence number, and encryption context for async worker processing |
| `ConnectionEncryptionData` | Per-connection state: AES keys, session prefix, nonce counters, sequence tracking |
| `BaseServerAuthenticator` | Abstract base: X25519 handshake, main-thread marshalling, stale-auth TTL sweeps, `RejectAndPurge` |
| `ServerAuthenticator` | SRP-6a pipeline with bounded channels for LoginServer |
| `TokenServerAuthenticator` | Token auth pipeline with bounded channel for World/Scene servers |
| `WorldServerAuthenticator` | World-entry admission gate: player cap, lock check, character query |
| `SceneServerAuthenticator` | Scene-entry pass-through |
| `AccountManager` | Thread-safe base with sync lock, dictionaries, arrival-order tracking |
| `SrpAccountManager` | SRP-specific: SRP data population, unauthenticated connection sweep |
| `TokenAccountManager` | Token-specific: simplified account creation without SRP state |

### External Dependencies

| Dependency | Purpose |
|------------|---------|
| `FishNet` | Networking framework: `Authenticator`, `NetworkConnection`, `Channel`, `Broadcast` |
| `SecureRemotePassword` | SRP-6a protocol (2048-bit group, SHA-512) |
| `BouncyCastle` | X25519 ECDH, cryptographic primitives |
| `System.Security.Cryptography` | AES-GCM, HKDF, HMAC-SHA256, `CryptographicOperations.ZeroMemory/FixedTimeEquals` |
| `System.Threading.Channels` | Bounded async producer-consumer queues |
| `EFCore 5 + Npgsql 5.0.17` | Database: account persistence, character queries, kick requests |
| `FishMMO.Logging` | Structured async logging |

### Designed For

| Scenario | How It Handles It |
|----------|-------------------|
| **10,000 concurrent login attempts** | Bounded channels with `DropWrite` backpressure; excess gets `ServerBusy` |
| **DDoS on account creation** | Per-IP rate limiting, failure tracking, automatic IP blocking (5 min), bounded channel |
| **Credential theft via packet capture** | SRP-6a: password never transmitted; X25519 + AES-256-GCM: forward secrecy per session |
| **Replay attacks** | Counter-based nonces, HMAC-SHA256 cookies, sequence validation, ephemeral keys |
| **Half-open connection floods** | 15-second stale-auth TTL sweep disconnects + purges incomplete sessions |
| **Already-online account hijack** | Kick-request debounce via `ExpiringKeyTracker` (10 s per account) |
| **Reconnect storms** | IP-based and account-based debounce before channel ingress |
| **UTF-8 smuggling** | `StrictUtf8` with `DecoderFallbackException` rejects malformed sequences |
| **Memory forensics** | `ZeroMemory` on all plaintext buffers, keys, and credentials |
| **Proxy/NAT false positives** | Configurable `useConnectionIdForRateLimit` mode |
| **Unity main-thread constraint** | Worker→main-thread marshalling via `ConcurrentQueue<Action>`, time-sliced drain |
| **World server full** | WorldServerAuthenticator checks lock state + player cap before admission |
| **No selected character** | WorldServerAuthenticator queries `ICharacterService` before world entry |

## License

This module is part of the FishMMO project and is subject to the FishMMO project license.
