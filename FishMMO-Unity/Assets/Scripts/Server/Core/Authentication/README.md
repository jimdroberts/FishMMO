# FishMMO Authentication Stack

## Why This Exists

MMO authentication is a high-value target. Players entrust credentials to the server, and a single breach can cascade across services. FishMMO's authentication stack is built from first principles to ensure that:

- **Passwords never touch the wire** — not even as hashes. SRP-6a (2048-bit, SHA-512) lets the server verify knowledge of a password without ever receiving it, eliminating the entire class of credential-interception attacks.
- **Every byte is authenticated and encrypted** — X25519 ECDH key exchange derives directional AES-256-GCM session keys with HKDF-SHA256. Every message is bound to its sequence, direction, and message type via Additional Authenticated Data (AAD).
- **The network thread never blocks** — broadcast handlers are ultra-fast UDP gates that validate and enqueue; all heavy crypto, database I/O, and SRP math run on bounded-channel async workers.
- **Failures are indistinguishable** — all error paths use the same `RejectAndPurge` helper: broadcast a generic result, disconnect, purge state. No timing or channel side-channels.
- **Memory is hostile to forensics** — decrypted buffers are zeroed with `CryptographicOperations.ZeroMemory()` immediately after use; key material is zeroed on disconnect; credentials are nulled at the earliest possible point.

This is not a wrapper around a third-party auth service. It is a purpose-built, zero-trust authentication pipeline designed for the specific constraints of a real-time MMO running on Unity with FishNet.

---

## Core Security Features

### SRP-6a: Zero-Knowledge Password Proof

| Property | Value |
|----------|-------|
| Protocol | SRP-6a (RFC 5054 variant) |
| Group | 2048-bit |
| Hash | SHA-512 |
| Library | `SecureRemotePassword` |

The server stores only a **salt** and a **verifier** — both derived from the password but computationally irreversible. During login, the client and server exchange ephemeral values and independently compute a proof. The server can verify the client knows the password without the password (or any derivative) ever crossing the network.

**What this prevents:**
- Credential interception (password never transmitted)
- Offline dictionary attacks against captured traffic
- Server-side password database leaks (no password or hash stored)
- Replay attacks (ephemeral values are unique per session)

### X25519 ECDH Key Exchange

| Property | Value |
|----------|-------|
| Curve | Curve25519 (X25519) |
| Library | BouncyCastle |
| Key derivation | HKDF-SHA256 |
| Output | 2 × AES-256 keys (directional) + 4-byte session prefix |

Each connection begins with an ephemeral X25519 keypair exchange. The shared secret is expanded via HKDF-SHA256 into:
- **Client→Server AES-256 key** (encrypt on client, decrypt on server)
- **Server→Client AES-256 key** (encrypt on server, decrypt on client)
- **4-byte session prefix** (nonce domain separation)

The X25519 private key is zeroed and disposed immediately after ECDH derivation. A fresh keypair is generated per connection attempt — no key reuse across sessions.

### AES-256-GCM Authenticated Encryption

| Property | Value |
|----------|-------|
| Algorithm | AES-256-GCM |
| Nonce | 12 bytes (deterministic, counter-based) |
| AAD | `(messageType, agreedVersion, sequenceNumber)` |

Every encrypted message uses a deterministic 12-byte nonce:

```
┌─────────────┬───────────┬────────────┬─────────────┐
│ Prefix (4B) │ Dir (1B)  │ Pad (3B)   │ Counter (4B)│
│ HKDF-       │ 0x00=C→S  │ 0x00 0x00  │ big-endian  │
│ derived     │ 0x01=S→C  │ 0x00       │ uint32      │
└─────────────┴───────────┴────────────┴─────────────┘
```

- **Session prefix**: 4 bytes from HKDF, unique per connection.
- **Direction byte**: Prevents reflection attacks (client→server ≠ server→client).
- **Counter**: Monotonically incremented per operation. Throws `CryptographicException` at `uint.MaxValue`.

**AAD binding**: Each ciphertext is bound to `(messageType, agreedVersion, sequenceNumber)`. Swapping ciphertext between message types, protocol versions, or sequence slots causes immediate GCM tag failure.

**What this prevents:**
- Eavesdropping (AES-256-GCM confidentiality)
- Tampering (GCM authentication tag)
- Replay/reorder (counter-based nonce + sequence validation)
- Reflection (directional key separation)
- Cross-message transplant (AAD binding)
- Nonce reuse (overflow guard + direction separation)

### HMAC-SHA256 Stateless Cookie Challenge

The server handshake response includes an HMAC-SHA256 cookie computed over the client's public key and connection metadata. The client must echo this cookie in subsequent messages. This provides:

- **Stateless IP verification** — no server-side state until the cookie is validated.
- **Replay protection** — cookies are bound to the specific connection context.
- **Fail-closed rotation** — the HMAC key is regenerated on `InitializeOnce` and zeroed on `Deinitialize`.

---

## Transport Security

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

Workers enqueue `Action` delegates into `ConcurrentQueue<Action>`, drained each frame by `DrainMainThreadQueue()`. All FishNet network operations (Broadcast, Disconnect) execute exclusively on the main thread.

### Bounded Channel Pipeline

| Channel | Capacity | Workers | Drop Policy | System |
|---------|----------|---------|-------------|--------|
| `verifyChannel` | 500 | 2 | `DropWrite` → `ServerBusy` | ServerAuthenticator (SRP) |
| `proofChannel` | 500 | 2 | `DropWrite` → `ServerBusy` + disconnect | ServerAuthenticator (SRP) |
| `tokenChannel` | 500 | 2 | `DropWrite` → `ServerBusy` | TokenServerAuthenticator |
| `AsyncWorkerData` | 1000 | configurable | `DropWrite` → `ServerBusy` | AccountCreationSystem |

All channels use `SingleReader = false, SingleWriter = false` for concurrent access.

---

## Production-Grade Safeguards

### Per-Connection In-Flight Gating

- `verifyInFlightByClientId` / `proofInFlightByClientId` allow at most one in-flight SRP request per connection per phase.
- Duplicate packets from the same `ClientId` are silently dropped while work is in progress.
- Repeated handshake packets are ignored while verify/proof is active.

### Stale Authentication TTL (Half-Open Protection)

- `authStartTimeByClientId` tracks when auth began for each connection.
- Periodic sweep disconnects and purges connections that have not completed SRP within **15 seconds**.
- Purge clears transient gate state and removes account/SRP data from `AccountManager`.

### Kick-Request Write Debounce

- Kick requests for already-online accounts are rate-limited per account name via `ExpiringKeyTracker<string>`.
- At most one `IKickRequestService.PersistAsync(accountName)` per **10 seconds** per account.
- Head-first expiry queue avoids full-dictionary enumeration during sweeps.

### Upstream Rate Limiting (Retry-Storm Mitigation)

- SRP verify ingress: lightweight debounce by **IP address** before channel write.
- Decrypted username path: lightweight debounce by **account name** before DB lookup.
- Account creation: per-IP rate limit (`ipRateLimitSeconds`) + failure tracking (`maxFailedAttempts`).
- World server: per-account rate limit via `ExpiringKeyTracker` (1 s debounce).
- All debounce maps use `ExpiringKeyTracker<string>` or `ConcurrentDictionary` with bounded sweep cycles.

### Connection IP Cache TTL

- Per-connection IP cache uses `LastSeenCacheTracker<int, string>`.
- Entries are touched on read and swept with bounded scan/remove limits.
- Prevents unbounded memory growth under connection churn.

### AccountManager Backstop Sweep

- `ArrivalOrderTracker<int>` tracks connection creation order.
- Periodic sweep purges stale SRP/encryption state that outlives delayed disconnect events.
- Oldest-first traversal ensures consistent cleanup ordering.

---

## Security Model

### Buffer Zeroing

Every decrypted plaintext buffer is zeroed with `CryptographicOperations.ZeroMemory()`:

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

On `DecoderFallbackException`, all decrypted buffers are zeroed before the connection is disconnected.

### Credential Lifecycle

- **Client**: `username` and `password` are nulled immediately after `SrpData.GetProof()` and again in `ClearKeyMaterial()`.
- **Server**: Account data stored only in `AccountManager` keyed by connection; purged on disconnect or auth failure.
- **.NET caveat**: Strings are immutable and cannot be deterministically zeroed. Nulling removes the GC reference. The `SecureRemotePassword` library requires string parameters, making `byte[]`-based storage impractical.

### Strict UTF-8 Decoding

All `byte[]` → `string` conversions of decrypted data use `CryptoHelper.StrictUtf8`:

```csharp
public static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(
    encoderShouldEmitUTF8Identifier: false,
    throwOnInvalidBytes: true);
```

Invalid byte sequences (truncated multi-byte, overlong encodings, surrogate halves) throw `DecoderFallbackException`. This prevents:
- UTF-8 smuggling attacks
- Null-byte injection via overlong encodings
- Processing of malformed input that could confuse downstream components

### Constant-Time Comparison

`CryptoHelper.FixedTimeEquals()` delegates to `CryptographicOperations.FixedTimeEquals` for timing-safe byte comparisons. SRP proof verification is handled internally by the `SecureRemotePassword` library.

### Error Indistinguishability (RejectAndPurge)

All worker-thread failure paths use the shared `RejectAndPurge(conn, result)` helper:

1. Enqueue `ClientAuthResultBroadcast` on `Channel.Reliable` to the main thread.
2. Disconnect the client after the broadcast is sent.
3. Purge all transient auth state via `PurgeConnectionAuthState`.

This eliminates:
- Timing side-channels (all failures take the same code path)
- Channel-based side-channels (all failures use `Reliable`)
- State leakage (all transient state purged atomically)
- DRY violations (single helper replaces 10+ inline patterns)

### Centralized Validation Rules

`FishMMO.Shared.Authentication` (in `FishMMO-SharedUtility`) provides compiled-regex validation shared across all projects:

| Rule | Constraint |
|------|-----------|
| `IsAllowedUsername` | 3–32 chars, `[a-zA-Z0-9_]+` |
| `IsAllowedPassword` | 8–32 chars, `[a-zA-Z0-9!@#$%^&*()_+=\-\[\]{}|;:',.<>?]+` |
| `IsAllowedCharacterName` | 3–24 chars, letters + single internal spaces |
| `IsAllowedGuildName` | 3–32 chars, alphanumeric + single internal spaces |
| `IsAllowedEmailUsername` | 3–320 chars, RFC-adjacent email validation |

---

## Architecture

### Inheritance Hierarchy

```
Authenticator (FishNet)
└── BaseServerAuthenticator                    # X25519 ECDH, main-thread queue, TTL sweeps, RejectAndPurge
    ├── ServerAuthenticator                    # SRP-6a pipeline (LoginServer)
    │   └── [LoginServer uses this directly]
    └── TokenServerAuthenticator               # HMAC-signed token pipeline (World/Scene)
        ├── WorldServerAuthenticator           # World-entry gate (player cap, selected character)
        └── SceneServerAuthenticator           # Scene-entry pass-through

ServerBehaviour
└── AccountCreationSystem                      # Async account creation (LoginServer)
```

### Core Types (Transport-Agnostic)

```
Server/Core/Authentication/
├── IAuthenticatorQueueData.cs                 # Bounded channels + CTS interface
├── SrpVerifyRequest<TConnection>              # Immutable request: encrypted username + ephemeral
└── SrpProofRequest<TConnection>               # Immutable request: encrypted client proof

Server/Core/Account/
└── ConnectionEncryptionData                   # Per-connection AES keys, nonce counters, sequence tracking

Server/Core/Collections/
├── ExpiringKeyTracker<TKey>                   # Head-first expiry queue for rate limiting
├── LastSeenCacheTracker<TKey, TValue>         # TTL cache with bounded sweep
└── ArrivalOrderTracker<TKey>                  # Oldest-first tracker for stale-connection sweeps
```

### Implementation Types (FishNet-Specific)

```
Server/Implementation/Authentication/
├── IServerAuthenticator.cs                     # Interface: Server ref + worker lifecycle
├── BaseServerAuthenticator.cs                 # 1020 lines — shared infrastructure
├── ServerAuthenticator.cs                     # 1262 lines — SRP-6a with bounded channels
└── TokenServerAuthenticator.cs                # 431 lines — token auth with bounded channel

Server/Implementation/World/WorldServer/Authentication/
└── WorldServerAuthenticator.cs                # 121 lines — world-entry admission gate

Server/Implementation/World/SceneServer/Authentication/
└── SceneServerAuthenticator.cs                # 27 lines — scene-entry pass-through

Server/Implementation/LoginServer/AccountCreation/
└── AccountCreationSystem.cs                   # 843 lines — async account creation pipeline
```

### Client Types

```
Client/Authentication/
├── ClientLoginAuthenticator.cs                # 749 lines — client-side SRP + account creation flow
└── ClientSrpData.cs                           # Client SRP state (ephemeral, proof, verify)
```

### Shared Types

```
Shared/Implementation/Network/Authentication/
└── AuthenticationBroadcasts.cs                # All broadcast message types

Shared/Implementation/Tools/Extensions/Crypto/
└── CryptoHelper.cs                            # 1001 lines — AES-GCM, X25519, HKDF, StrictUtf8, nonce builder

FishMMO-SharedUtility/
└── Authentication.cs                          # Centralized validation rules (cross-project)
```

### Authentication Flow: Complete Sequence

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

---

## Designed For

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

---

## Philosophy

1. **Fail closed** — unknown states result in disconnect + purge, never in admission.
2. **Zero trust** — every message is validated, decrypted, and authenticated independently. No state is assumed from prior messages beyond what is cryptographically bound.
3. **Defense in depth** — rate limiting at IP level, account level, and channel level. In-flight gates, TTL sweeps, and backstop sweeps each catch failures the others miss.
4. **Minimal attack surface** — network thread does no crypto, no DB, no allocation beyond request structs. Workers see only encrypted bytes until they explicitly decrypt.
5. **Indistinguishable failures** — `RejectAndPurge` ensures all error paths look identical to an attacker.
6. **Bounded everything** — channels, sweep scans, sweep removals, cache sizes, block durations. Nothing grows unbounded.
7. **Clean separation** — Core types are transport-agnostic (generic `TConnection`). Implementation types bind to FishNet. Shared types cross all projects.

---

## Status

Production-ready. All cryptographic primitives use audited libraries (BouncyCastle, `System.Security.Cryptography`). The SRP-6a implementation uses the `SecureRemotePassword` library with 2048-bit group and SHA-512. The authentication pipeline has been architected for the specific constraints of Unity (single main thread, FishNet broadcast thread-safety) and MMO-scale concurrent connection handling.

---

## External Dependencies

| Dependency | Version Constraint | Purpose |
|------------|-------------------|---------|
| `FishNet` | — | Networking framework: `Authenticator`, `NetworkConnection`, `Channel`, `Broadcast` |
| `SecureRemotePassword` | — | SRP-6a protocol (2048-bit group, SHA-512) |
| `BouncyCastle` | — | X25519 ECDH, cryptographic primitives |
| `System.Security.Cryptography` | .NET BCL | AES-GCM, HKDF, HMAC-SHA256, `CryptographicOperations.ZeroMemory/FixedTimeEquals` |
| `System.Threading.Channels` | .NET BCL | Bounded async producer-consumer queues |
| `EFCore 5 + Npgsql 5.0.17` | Unity-compatible | Database: account persistence, character queries, kick requests |
| `FishMMO.Logging` | — | Structured async logging |