# Server Authentication

**Short description:** Provides server-side authentication via SRP-6a (LoginServer) and HMAC-signed token verification (World/Scene servers) using a bounded-channel architecture with X25519 ECDH key exchange, AES-256-GCM encryption, and comprehensive DoS hardening.

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

Server-side authentication with a bounded-channel architecture for high-throughput, non-blocking operation. Two authentication modes — **SRP-6a** (LoginServer) and **HMAC-signed token verification** (World/Scene servers) — share a common abstract base class (`BaseServerAuthenticator`) that routes FishNet lifecycle events to an engine-independent `BaseAuthenticatorCore<NetworkConnection>` in the `FishMMO-Auth.dll` shared library.

### Composition Architecture

Because `Authenticator` (FishNet) is a MonoBehaviour, and C# single-inheritance prevents a Unity class from also extending `BaseAuthenticatorCore<TConnection>`, the Unity authenticator classes use **composition with inner sealed classes**:

```
BaseServerAuthenticator : Authenticator      ← Unity MonoBehaviour
  └─ protected abstract Core                  ← holds FishMMO-Auth core instance

ServerAuthenticator : BaseServerAuthenticator
  └─ ServerAuthenticatorCore                  ← inner sealed class
      : SrpAuthenticatorCore<NetworkConnection> (FishMMO-Auth)

TokenServerAuthenticator : BaseServerAuthenticator
  └─ TokenCore                                ← inner sealed class
      : TokenAuthenticatorCore<NetworkConnection> (FishMMO-Auth)
```

The Unity classes are **thin routing shells**: they register FishNet broadcast handlers, forward received messages to the core, supply DB callbacks via abstract method implementations, and marshal core-initiated broadcasts back to the FishNet main thread. All handshake, TTL sweep, channel, worker, rate-limit, and crypto orchestration logic lives in FishMMO-Auth.

All cryptographic operations are delegated to three static service classes in the `FishMMO.Auth.Implementation` namespace (shipped via `FishMMO-Auth.dll`):

- **`HandshakeService`** — X25519 ECDH key agreement, transcript-hash computation, and cookie HMAC generation/verification.
- **`SrpService`** — Server-side SRP field encryption/decryption, fake SRP verifier generation, and all AES-GCM encrypt/decrypt for SRP broadcasts.
- **`TokenService`** — Server-side token generation, HMAC signing, structure verification, and token encrypt/decrypt.

Core types (`CryptoHelper`, `ConnectionEncryptionData`, `ServerSrpData`, `SrpVerifyRequest`, `SrpProofRequest`, `AuthState`, `IAccountManager`, `ISrpAccountManager`, `ITokenAccountManager`, `AccessLevel`, `ClientAuthenticationResult`) are in the `FishMMO.Auth.Core` namespace.

### Bounded-Channel Pipeline

The pipeline below is owned by the FishMMO-Auth core classes. The Unity wrapper classes route incoming broadcast messages into the core; the core internally manages the channels, workers, and main-thread enqueue callbacks.

```
Network Thread (UDP Gates)              Worker Threads (Async)              Main Thread
┌─────────────────────────┐        ┌────────────────────────────┐     ┌──────────────────┐
│ OnSrpVerifyReceived()   │──────► │ ProcessSrpVerifyAsync()    │────►│ DrainMainThread() │
│   • Validate connection │ Verify │   • AES-GCM decrypt user   │ Enq │   • Broadcast()   │
│   • In-flight gate      │Channel │   • StrictUtf8 decode      │ ──► │   • Disconnect()  │
│   • TryWrite(channel)   │        │   • DB: fetch account      │     │   • OnAuthResult() │
│   • Zero blocking       │        │   • SRP state setup        │     └──────────────────┘
└─────────────────────────┘        └────────────────────────────┘
┌─────────────────────────┐        ┌────────────────────────────┐     ┌──────────────────┐
│ OnSrpProofReceived()    │──────► │ ProcessSrpProofAsync()     │────►│ DrainMainThread() │
│   • Validate connection │ Proof  │   • AES-GCM decrypt proof  │ Enq │   • Broadcast()   │
│   • In-flight gate      │Channel │   • SRP proof verification │ ──► │   • Disconnect()  │
│   • TryWrite(channel)   │        │   • TryLoginAsync()        │     │   • OnAuthResult() │
└─────────────────────────┘        └────────────────────────────┘     └──────────────────┘
```

### Thread Model

| Thread | Responsibilities | Blocking Allowed |
|--------|-----------------|------------------|
| **Network Thread** | UDP gate handlers — validate, in-flight gate, enqueue | **No** (zero blocking) |
| **Worker Threads** | AES decryption, StrictUtf8 decode, database I/O, SRP math, ZeroMemory | **Yes** (async/await) |
| **Main Thread** | `Broadcast()`, `Disconnect()`, `OnAuthenticationResult` | N/A (Unity main loop) |

Workers enqueue `Action` delegates into a `ConcurrentQueue<Action>` held by `BaseServerAuthenticator`, drained each frame by `Update()` → `DrainMainThreadQueue()`. A per-frame cap (`maxMainThreadActionsPerUpdate = 100`, Inspector-configurable) time-slices draining to avoid frame spikes, with back-pressure logging when the queue is not fully drained.

## Supported Platforms

| Platform | Supported | Notes |
|----------|-----------|-------|
| Windows  | Yes       | Fully supported as a server host |
| Linux    | Yes       | Fully supported as a server host |
| WebGL    | N/A       | Server-only component; not applicable to browser builds |

**Engine:** Unity 6.3 LTS
**Scripting backend:** IL2CPP

## Features

- **SRP-6a authentication** — Full Secure Remote Password protocol (2048-bit group, SHA-512) for LoginServer password verification without transmitting passwords.
- **HMAC-signed token authentication** — Token-based authentication for World/Scene servers with database-backed revocation support and configurable expiration.
- **X25519 ECDH key exchange** — Ephemeral Diffie-Hellman key agreement with transcript-hash binding and version negotiation for forward secrecy.
- **AES-256-GCM encryption** — All authentication payloads encrypted with Additional Authenticated Data (AAD) binding `(messageType, agreedVersion, sequenceNumber)` to prevent cross-message ciphertext swapping.
- **Stateless cookie challenge** — Two-phase handshake: HMAC-SHA256 cookie challenge filters spoofed-source-IP attacks before any X25519 computation. Cookies are time-bucketed (30 s) with fail-closed key rotation on restart.
- **Bounded-channel pipeline** — `System.Threading.Channels` with `DropWrite` policy for non-blocking network-thread operation. State rollback on write failure enables client retry without re-handshaking.
- **Counter-based nonce scheme** — 12-byte nonces with HKDF-derived prefix, direction byte, and monotonic counter; overflow guard at `uint.MaxValue` prevents nonce reuse.
- **Strict UTF-8 decoding** — All decrypted `byte[]` → `string` conversions use `UTF8Encoding(false, true)`. Invalid sequences trigger immediate buffer zeroing and disconnect.
- **Buffer and credential zeroing** — Decrypted plaintext, symmetric keys, session prefixes, and HMAC keys are zeroed with `CryptographicOperations.ZeroMemory()` immediately after use.
- **Constant-time comparison** — `CryptoHelper.FixedTimeEquals()` for timing-safe byte comparisons on proofs, cookies, and tokens.
- **Per-IP handshake rate limiting** — 250 ms debounce per IP with bounded sweep cleanup to prevent X25519 CPU abuse.
- **Global handshake rate cap** — Maximum 500 X25519 handshakes/second (wall-clock based) for botnet defence.
- **Per-IP SRP verify debounce** — 1 s cooldown per IP before SRP verify channel ingress.
- **Per-account verify debounce** — 2 s cooldown per account name before database lookup.
- **Kick-request write debounce** — At most one `IKickRequestService.PersistAsync` per 10 s per account via `ExpiringKeyTracker<string>`.
- **Stale-auth TTL sweep** — Periodic 1 s sweep disconnects and purges connections that exceed 15 s without completing authentication, with a 60 s hard deadline that cannot be extended.
- **Pending auth connection cap** — Maximum 10,000 concurrent half-open auth connections to prevent memory exhaustion.
- **Fake SRP verifier for non-existent accounts** — Pre-computed dummy salt/verifier with per-username HMAC-SHA512 derived salts prevents username enumeration via timing or salt-reuse analysis.
- **Deferred online check** — Account online-status check is deferred until after SRP proof to prevent account-existence enumeration.
- **RejectAndPurge unified failure handling** — All failure paths use a shared helper to atomically notify, disconnect, and purge, preventing information leakage through inconsistent error timing.
- **Error indistinguishability** — Most failure paths disconnect without protocol-level detail, preventing oracle attacks.
- **Connection IP caching** — `LastSeenCacheTracker<int, string>` with 120 s TTL avoids repeated address allocation on hot paths.
- **Token generation and issuance** — LoginServer generates HMAC-signed auth tokens with configurable expiration (default 10 min), encrypted with session keys, and persisted for revocation support.
- **Token revocation** — World/Scene servers verify token revocation status via database hash lookup before granting access.
- **Protocol version negotiation** — `CryptoHelper.NegotiateProtocolVersion` with version range binding in the ECDH transcript hash prevents downgrade attacks.
- **TOTP two-factor authentication** — After SRP proof, accounts with 2FA enabled enter a TOTP verification phase. TOTP secrets are AES-256-GCM encrypted at rest with a server-side master key. Codes are verified with a ±1 step window and anti-replay via persisted last-used window.
- **Recovery code login** — When the authenticator app is unavailable, users can submit a single-use recovery code (XXXXX-XXXXX hex format) instead of a 6-digit TOTP code. The code is verified against PBKDF2-SHA256 hashes via `ITwoFactorRecoveryCodeService` and consumed after use.
- **Per-username TOTP brute-force protection** — Failed TOTP/recovery code attempts are tracked per username (lowercased, cross-connection). After 15 failures within a 30-minute window, further attempts are rejected until the lockout expires. A bounded sweep (64 max scan) evicts stale entries.
- **Per-connection TOTP attempt cap** — Each connection is limited to 5 TOTP attempts via `TotpPendingState.Attempts`. Exceeding the cap disconnects the client.
- **TOTP concurrency limiter** — A semaphore (`MaxConcurrentTotpVerifications = 4`) limits parallel TOTP/recovery code verifications to bound CPU cost from PBKDF2 operations.
- **Email enumeration prevention** — For email-based login, unverified accounts receive the same fake SRP flow as non-existent accounts, preventing account-existence disclosure via the `AccountUnverified` response code. Username-based login still returns `AccountUnverified` for user-friendly UX.

## Prerequisites

- **FishNet** — Networking framework providing `Authenticator` base class, `NetworkConnection`, `NetworkManager`, and broadcast infrastructure.
- **SecureRemotePassword** — SRP-6a library (2048-bit group, SHA-512 parameters).
- **FishMMO-Auth.dll** — netstandard2.1 shared authentication library (auto-copied to `Assets/Dependencies/`). Provides:
  - `FishMMO.Auth.Core`: enums (`AuthState`, `AccessLevel`, `ClientAuthenticationResult`), interfaces (`IAccountManager`, `ISrpAccountManager`, `ITokenAccountManager`), DTOs, and the `ArrivalOrderTracker` / `ExpiringKeyTracker` / `LastSeenCacheTracker` collections.
  - `FishMMO.Auth.Implementation`: abstract auth core classes (`BaseAuthenticatorCore<TConnection>`, `SrpAuthenticatorCore<TConnection>`, `TokenAuthenticatorCore<TConnection>`), generic account managers (`AccountManager<T>`, `SrpAccountManager<T>`, `TokenAccountManager<T>`), connection data types (`ConnectionEncryptionData`, `AccountData`, `ServerSrpData`), static crypto service classes (`HandshakeService`, `SrpService`, `TokenService`), `CryptoHelper` (AES-256-GCM, X25519 ECDH, HKDF-SHA256, `GcmNonceContext`, `StrictUtf8`, constant-time compare), and request structs (`SrpVerifyRequest`, `SrpProofRequest`).
- **Database services** — `IAccountService` (fetch salt/verifier), `ICharacterService` (online check), `IKickRequestService` (kick persistence), `IAuthTokenService` (token hash CRUD), `ILoginServerSigningKeyService` (HMAC key fetch).
- **FishMMO.Shared.Authentication** — Centralized validation rules (`IsAllowedUsername`, `IsAllowedPassword`).
- **FishMMO.Logging.Log** — Structured async logging.
- **.NET 6+** — `System.Threading.Channels`, `System.Security.Cryptography` (AES-GCM, X25519, HKDF, HMAC-SHA256/512).
- **NTP** — Hosts MUST run NTP (or equivalent) to keep the clock monotonically accurate. Wall-clock TTL enforcement, hard deadlines, cookie expiration, and **TOTP verification windows** all use `DateTime.UtcNow`. Without NTP, TOTP codes may be rejected even when valid, and multi-server deployments will disagree on token expiration.

## Installation / Build

This module is an integrated part of the FishMMO Unity project. The server authenticator classes are compiled as part of the server assembly and depend on `FishMMO-Auth.dll` (auto-copied to `Assets/Dependencies/` by the FishMMO-Auth build) for all crypto service classes and core types.

1. Open the FishMMO-Unity project in Unity 6.3 LTS.
2. Ensure FishNet, SecureRemotePassword, `FishMMO-Auth.dll`, and all database service assemblies are present in the project.
3. The authenticator components (`ServerAuthenticator`, `TokenServerAuthenticator`) are attached to server prefabs and configured via the Unity Inspector.
4. Build using IL2CPP scripting backend for production deployment.

## Quick Start Guides

### LoginServer (SRP-6a)

1. Attach `ServerAuthenticator` to the LoginServer's NetworkManager.
2. The `LoginServerSystem` assigns `Server`, calls `InitializeWorkers()`, and injects the HMAC signing key and LoginServer ID for token issuance.
3. Clients connect and are authenticated through the three-phase SRP flow: Handshake → SRP Verify → SRP Proof.
4. On success, clients receive an encrypted server proof and an HMAC-signed auth token for World/Scene server entry.

### World/Scene Server (Token-based)

1. Attach `TokenServerAuthenticator` (or a subclass like `WorldServerAuthenticator` / `SceneServerAuthenticator`) to the server's NetworkManager.
2. The server system assigns `Server` and calls `InitializeWorkers()`.
3. Clients connect with the auth token received from LoginServer. The flow is: Handshake → TokenAuth → Result.
4. The worker decrypts the token, fetches the issuing LoginServer's signing key from the database, verifies HMAC, checks expiration and revocation, then calls `TryLoginAsync()` for server-type-specific admission.

## Configuration

### BaseServerAuthenticator Inspector Fields (FishMMO-Unity)

These are the only configurable values that live in the Unity wrapper classes:

| Field | Default | Description |
|-------|---------|-------------|
| `maxMainThreadActionsPerUpdate` | 100 | Max queued main-thread actions drained per Update frame |
| `useConnectionIdForRateLimit` | false | Use connection ID instead of remote IP for rate limiting. Enable when behind a NAT/proxy/load balancer |
| `tokenExpirationMinutes` | 10 min | Auth token expiration (`ServerAuthenticator` only; Inspector-configurable) |

### BaseAuthenticatorCore Constants (FishMMO-Auth)

The following constants are defined in `BaseAuthenticatorCore<TConnection>` and its subclasses in the `FishMMO-Auth` shared library.

| Constant | Value | Description |
|----------|-------|-------------|
| `AuthStaleTtlSeconds` | 15 s | TTL for stale-auth sweep |
| `AuthHardDeadlineSeconds` | 60 s | Absolute auth deadline (cannot be extended) |
| `AuthSweepIntervalSeconds` | 1 s | Sweep interval for stale auth cleanup |
| `AuthSweepMaxScan` | 256 | Max entries scanned per stale-auth sweep |
| `AuthSweepMaxRemovals` | 64 | Max entries purged per stale-auth sweep |
| `MaxPendingAuthConnections` | 10,000 | Cap on concurrent half-open auth connections |
| `HandshakeIpDebounceSeconds` | 0.25 s | Minimum interval between handshakes from same IP |
| `MaxGlobalHandshakesPerSecond` | 500 | Global X25519 handshake rate cap |
| `CookieTimeBucketSeconds` | 30 s | Handshake cookie validity window (max 2×) |

### SrpAuthenticatorCore Constants (FishMMO-Auth)

| Constant | Value | Description |
|----------|-------|-------------|
| `VerifyWorkerCount` | 2 | Concurrent SRP verify workers |
| `ProofWorkerCount` | 2 | Concurrent SRP proof workers |
| `VerifyChannelCapacity` | 500 | Bounded channel capacity for verify requests |
| `ProofChannelCapacity` | 500 | Bounded channel capacity for proof requests |
| `KickRequestDebounceSeconds` | 10 s | Per-account kick-request debounce |
| `IpAuthAttemptDebounceSeconds` | 1 s | Per-IP SRP verify debounce |
| `AccountVerifyDebounceSeconds` | 2 s | Per-account SRP verify debounce |
| `ConnectionIpCacheTtlSeconds` | 120 s | IP cache entry TTL |
| `MaxConcurrentTotpVerifications` | 4 | Semaphore limit for parallel TOTP/recovery verifications |
| `MaxTotpAttempts` | 5 | Per-connection TOTP attempt cap (exceeding disconnects) |
| `MaxTotpFailuresPerUsername` | 15 | Per-username failure threshold before lockout |
| `TotpUsernameLockoutDuration` | 30 min | Lockout window for per-username failures |
| `MaxTotpUsernameFailureEntries` | 10,000 | Hard cap on tracked username entries |
| `TotpUsernameFailureSweepMaxScan` | 64 | Max entries scanned per sweep iteration |

### TokenAuthenticatorCore Constants (FishMMO-Auth)

| Constant | Value | Description |
|----------|-------|-------------|
| `TokenWorkerCount` | 2 | Concurrent token auth workers |
| `TokenChannelCapacity` | 500 | Bounded channel capacity for token requests |
| `MaxTokenPayloadBytes` | 2,048 | Maximum encrypted token payload size |

## Usage Examples

### Registering the SRP Authenticator (LoginServer)

```csharp
// LoginServerSystem.OnStartServer()
ServerAuthenticator authenticator = networkManager.GetComponent<ServerAuthenticator>();
authenticator.Server = this.Server;
authenticator.InitializeWorkers();
authenticator.TokenSigningKey = hmacKey;
authenticator.LoginServerId = loginServerId;
```

### Registering the Token Authenticator (World/Scene Server)

```csharp
// WorldServerSystem.OnStartServer()
TokenServerAuthenticator authenticator = networkManager.GetComponent<TokenServerAuthenticator>();
authenticator.Server = this.Server;
authenticator.InitializeWorkers();
```

### Overriding TryLoginAsync for Server-Specific Admission

```csharp
// WorldServerAuthenticator
internal override Task<ClientAuthenticationResult> TryLoginAsync(
    ClientAuthenticationResult result, string username)
{
    // Check world lock, player cap, selected character, etc.
    if (worldLocked)
        return Task.FromResult(ClientAuthenticationResult.ServerFull);
    return Task.FromResult(ClientAuthenticationResult.WorldLoginSuccess);
}
```

### Subscribing to Authentication Events

```csharp
authenticator.OnClientAuthenticationResult += (conn, authenticated) =>
{
    if (authenticated)
        Log.Info("Auth", $"Client {conn.ClientId} authenticated successfully.");
};
```

## Operational Checks

| Check | How to Verify |
|-------|---------------|
| Workers started | Log output: `"Workers initialized (Verify=2, Proof=2)"` or `"Workers initialized (Token=2)"` |
| Handshake completing | Client receives `ServerHandshake` with X25519 public key and agreed version |
| SRP verify processing | Client receives `SrpVerifyBroadcast` response with encrypted salt and server ephemeral |
| SRP proof accepted | Client receives `SrpSuccessBroadcast` with encrypted server proof and auth token |
| Token auth accepted | Client receives `ClientAuthResultBroadcast` with `LoginSuccess` / `WorldLoginSuccess` / `SceneLoginSuccess` |
| Stale auth sweep active | Log warnings for purged connections exceeding 15 s TTL |
| Rate limiting active | `ServerBusy` result returned to rate-limited clients |
| Global handshake cap | Silent drop (no disconnect) when rate exceeded; handshake counter resets each wall-clock second |
| Pending auth cap | Log warning: `"Pending auth cap (10000) reached — handshake(s) dropped."` (rate-limited to 1 per 5 s) |
| Main-thread back-pressure | Log warning: `"Main-thread queue back-pressure: N actions remain after draining 100."` |
| Cookie rotation | In-flight handshakes with old cookies fail verification and disconnect after authenticator restart |
| Token revocation | `TokenRevoked` result returned for revoked tokens |
| Token expiration | `TokenExpired` result returned for expired tokens |
| Graceful shutdown | Workers stop, channels complete, HMAC key zeroed, shared state cleared |

## Flow Diagram

### SRP-6a Authentication (LoginServer)

#### Phase 1: Key Exchange (Inline — No Channel)

```
Client                              Server (Network Thread)
  │                                      │
  │── ClientHandshake ─────────────────► │  OnServerClientHandshakeReceived()
  │   { PublicKey (X25519) }             │    • Cookie == null → compute HMAC cookie, reply
  │◄── ServerHandshake ──────────────── │    • { Cookie } (no PublicKey)
  │   { Cookie }                         │
  │                                      │
  │── ClientHandshake ─────────────────► │  OnServerClientHandshakeReceived()
  │   { PublicKey, Cookie }              │    • Verify cookie (current + previous time bucket)
  │                                      │    • Per-IP rate limit (0.25 s debounce)
  │                                      │    • Global rate limit (500/s)
  │                                      │    • Pending auth cap check (10,000)
  │                                      │    • Version negotiation (min/max → agreed)
  │                                      │    • AddConnectionEncryptionData()
  │                                      │    • X25519 ECDH + transcript hash + HKDF-SHA256
  │                                      │      → directional AES keys + session prefix
  │◄── ServerHandshake ──────────────── │    • { ServerPublicKey, AgreedVersion }
```

#### Phase 2: SRP Verify (Bounded Channel)

```
Client                              Server
  │                                      │
  │── SrpVerifyBroadcast ──────────────► │  UDP Gate → verifyChannel
  │   { Username (enc), Ephemeral (enc)} │    • Per-IP debounce (1 s)
  │                                      │    • AuthState: Handshake → VerifyPending
  │                                      │    • DropWrite rollback on channel full
  │                                      │
  │                                      │  Worker: ProcessSrpVerifyAsync()
  │                                      │    • Consume seq-1: AES-GCM decrypt username
  │                                      │    • StrictUtf8 decode (DecoderFallbackException → zero + disconnect)
  │                                      │    • ZeroMemory on decrypted byte arrays
  │                                      │    • IsAllowedUsername validation
  │                                      │    • Per-account verify debounce (2 s)
  │                                      │    • Consume seq: AES-GCM decrypt public ephemeral
  │                                      │    • DB: FetchForLoginAsync(username)
  │                                      │    • RefreshAuthTtl (hard deadline enforced)
  │                                      │    • Non-existent account → fake SRP tuple + per-username HMAC salt
  │                                      │    • AddConnectionAccount (VerifyPending → WaitingForProof)
  │                                      │    • AES-GCM encrypt salt + server ephemeral
  │◄── SrpVerifyBroadcast ──────────── │    • Enqueue → Main Thread Broadcast
  │   { Salt (enc), Ephemeral (enc) }   │
```

#### Phase 3: SRP Proof (Bounded Channel)

```
Client                              Server
  │                                      │
  │── SrpProofBroadcast ──────────────► │  UDP Gate → proofChannel
  │   { ClientProof (encrypted) }       │    • AuthState: WaitingForProof → ProofPending
  │                                      │    • DropWrite rollback on channel full
  │                                      │
  │                                      │  Worker: ProcessSrpProofAsync()
  │                                      │    • Consume seq: AES-GCM decrypt client proof
  │                                      │    • ZeroMemory on decrypted byte array
  │                                      │    • Atomically: validate proof + advance ProofPending → SrpSuccess
  │                                      │    • RefreshAuthTtl after SRP modular exponentiation
  │                                      │    • Deferred online check via ICharacterService.FetchManyAsync
  │                                      │    • Online → kick-request debounce + IKickRequestService.PersistAsync
  │                                      │    • TryLoginAsync() (virtual — subclass-specific)
  │                                      │    • Generate HMAC-signed auth token (if signing key available)
  │                                      │    • Persist token hash via IAuthTokenService.IssueAsync
  │                                      │    • AES-GCM encrypt server proof + token
  │◄── SrpSuccessBroadcast ──────────── │    • Enqueue → Main Thread Broadcast
  │   { ServerProof (enc), Token (enc)} │    • OnAuthentication(conn, true)
  │                                      │    • Advance SrpSuccess → Authenticated
  │                                      │    • ClearSrpState()
```

#### Phase 3.5: Two-Factor Authentication (Conditional)

If the account has TOTP enabled (`accountData.TotpEnabled == true`), the SRP proof handler defers token issuance and enters a TOTP verification phase. The client receives `TwoFactorRequired` instead of `SrpSuccessBroadcast`.

```
Client                              Server
  │                                      │
  │◄── ClientAuthResultBroadcast ────── │  ProcessSrpProofAsync()
  │   { TwoFactorRequired }             │    • SRP proof valid, 2FA enabled
  │                                      │    • Create TotpPendingState (attempts, serverProof, accessLevel)
  │                                      │    • State remains at SrpSuccess (not Authenticated)
  │                                      │
  │── TwoFactorVerifyBroadcast ────────► │  UDP Gate → totpSemaphore
  │   { Code (encrypted), Seq }          │    • Increment TotpPendingState.Attempts (volatile)
  │                                      │    • Acquire totpSemaphore (MaxConcurrent=4)
  │                                      │
  │                                      │  Worker: ProcessTwoFactorVerifyAsync()
  │                                      │    • Consume seq: AES-GCM decrypt code
  │                                      │    • Per-username lockout check (15 failures / 30 min)
  │                                      │    • Detect code format:
  │                                      │      ┌── 6-digit numeric → TOTP path
  │                                      │      │   • Decrypt TOTP secret with master key
  │                                      │      │   • VerifyTotpCode (±1 step, anti-replay)
  │                                      │      │   • Persist last-used window
  │                                      │      │
  │                                      │      └── XXXXX-XXXXX hex → Recovery code path
  │                                      │          • FetchUnusedByAccountAsync()
  │                                      │          • VerifyRecoveryCode (PBKDF2-SHA256)
  │                                      │          • ConsumeCodeAsync() on match
  │                                      │
  │                                      │    • On failure: track per-username, send TwoFactorInvalid
  │                                      │    • On success: SrpSuccess → Authenticated
  │                                      │    • Encrypt server proof + generate auth token
  │◄── SrpSuccessBroadcast ──────────── │    • Enqueue → Main Thread Broadcast
  │   { ServerProof (enc), Token (enc)} │    • OnAuthentication(conn, true)
  │                                      │    • ClearSrpState()
```

### Token Authentication (World/Scene Server)

```
Client                              Server (Network Thread)
  │                                      │
  │── ClientHandshake ─────────────────► │  (Same cookie + ECDH flow as above)
  │◄── ServerHandshake ──────────────── │
  │                                      │
  │── TokenAuthBroadcast ──────────────► │  UDP Gate → tokenChannel
  │   { Token (encrypted) }             │    • AuthState: Handshake → TokenPending
  │                                      │    • Payload size check (≤ 2048 bytes)
  │                                      │
  │                                      │  Worker: ProcessTokenAuthAsync()
  │                                      │    • Consume seq: AES-GCM decrypt token
  │                                      │    • Validate minimum token structure (≥ 69 bytes)
  │                                      │    • Parse loginServerId from token payload
  │                                      │    • DB: Fetch signing key for issuing LoginServer
  │                                      │    • RefreshAuthTtl after DB fetch
  │                                      │    • Timing-equalized HMAC verify (random key on miss)
  │                                      │    • Check expiration (DateTime.UtcNow ≥ expiresUtc)
  │                                      │    • Check revocation via token hash DB lookup
  │                                      │    • RefreshAuthTtl after revocation check
  │                                      │    • AddConnectionAccount(conn, accountName, AccessLevel.Player)
  │                                      │    • TryLoginAsync() (virtual — WorldServer/SceneServer logic)
  │◄── ClientAuthResultBroadcast ────── │    • Enqueue → Main Thread Broadcast
  │   { Result }                         │    • OnAuthentication(conn, authenticated)
  │                                      │    • Advance TokenPending → Authenticated
```

### Failure Handling

On failure at any step, workers call `RejectAndPurge(conn, result)` which atomically:
1. Enqueues a `ClientAuthResultBroadcast` on `Channel.Reliable` to the main thread.
2. Disconnects the client after the broadcast is sent.
3. Purges all transient auth state via `PurgeConnectionAuthState`.

## Project Structure

### Directory Tree

```
# FishMMO-Auth (netstandard2.1 shared library — Assets/Dependencies/FishMMO-Auth.dll)
FishMMO-Auth/
├── Core/
│   ├── Enums/
│   │   ├── AuthState.cs                       # Enum: Handshake → VerifyPending → … → Authenticated
│   │   ├── AccessLevel.cs                     # Enum: Player, Moderator, Admin, etc.
│   │   └── ClientAuthenticationResult.cs      # Enum: auth result codes
│   ├── Interfaces/
│   │   ├── IAccountManager.cs                 # Thread-safe account/connection state management
│   │   ├── ISrpAccountManager.cs              # SRP-specific account manager (LoginServer)
│   │   └── ITokenAccountManager.cs            # Token-specific account manager (World/Scene)
│   └── Collections/
│       ├── ExpiringKeyTracker.cs              # Keyed debounce/rate-limit (head-first expiry queue)
│       ├── LastSeenCacheTracker.cs            # Key/value cache with TTL by last-seen timestamp
│       └── ArrivalOrderTracker.cs             # Oldest-first tracker for stale-connection sweeps
│
└── Implementation/
    ├── Auth/
    │   ├── BaseAuthenticatorCore.cs           # Abstract base: handshake, TTL sweeps, workers, rate limits, cookie, nonce
    │   ├── SrpAuthenticatorCore.cs            # Abstract SRP-6a pipeline: bounded channels, verify/proof workers, TOTP
    │   ├── TokenAuthenticatorCore.cs          # Abstract token pipeline: bounded channel, token worker, revocation
    │   └── ClientAuthenticatorCore.cs         # Abstract client-side auth flow
    ├── Account/
    │   ├── AccountManager.cs                  # Thread-safe base: all dictionaries, CAS machine, sweep tracking
    │   ├── SrpAccountManager.cs               # SRP extension: AddConnectionAccount, SweepUnauthenticated
    │   └── TokenAccountManager.cs             # Token extension: AddConnectionAccount (name + ACL)
    ├── Connection/
    │   ├── AccountData.cs                     # Access level + auth state + SRP data container
    │   └── ConnectionEncryptionData.cs        # Per-connection AES-256 keys, nonce contexts, sequence tracking
    ├── SRP/
    │   ├── ServerSrpData.cs                   # Server SRP session state (ephemeral, proof, verifier)
    │   └── ClientSrpData.cs                   # Client SRP session state
    ├── Requests/
    │   ├── SrpVerifyRequest.cs                # Immutable request struct (encrypted credentials)
    │   └── SrpProofRequest.cs                 # Immutable request struct (encrypted proof)
    └── Services/
        ├── HandshakeService.cs                # X25519 ECDH key agreement, cookie HMAC (client + server sides)
        ├── SrpService.cs                      # Server-side SRP encrypt/decrypt, fake SRP verifier generation
        └── TokenService.cs                    # Token generation, HMAC signing, structure verification

# FishMMO-Unity (this assembly)
Server/
├── Core/Authentication/
│   └── IAuthenticatorQueueData.cs             # Legacy interface for bounded channels + CTS runtime data
│
└── Implementation/Authentication/             # FishNet-typed composition wrappers (this directory)
    ├── IServerAuthenticator.cs                # Interface: Server ref + worker lifecycle
    ├── BaseServerAuthenticator.cs             # Composition host: owns Core, routes FishNet events to core
    ├── ServerAuthenticator.cs                 # SRP wrapper: inner ServerAuthenticatorCore : SrpAuthenticatorCore<NetworkConnection>
    └── TokenServerAuthenticator.cs            # Token wrapper: inner TokenCore : TokenAuthenticatorCore<NetworkConnection>
```

### Related Modules

```
Shared/
└── Implementation/Network/Authentication/
    └── AuthenticationBroadcasts.cs            # ClientHandshake, ServerHandshake, SrpVerify/Proof/Success, AuthResult
```

### Composition and Inheritance

C# single-inheritance prevents a MonoBehaviour from also extending `BaseAuthenticatorCore<TConnection>`. The pattern is composition via inner sealed classes:

```
# Class hierarchy
Authenticator (FishNet MonoBehaviour)
└── BaseServerAuthenticator              # owns Core; routes FishNet lifecycle to it
    ├── ServerAuthenticator              # inner: ServerAuthenticatorCore : SrpAuthenticatorCore<NetworkConnection>
    └── TokenServerAuthenticator         # inner: TokenCore : TokenAuthenticatorCore<NetworkConnection>
        ├── WorldServerAuthenticator     # overrides TryLoginAsync + OnAuthSweep (player cap, character check)
        └── SceneServerAuthenticator     # overrides TryLoginAsync (scene pass-through)

# FishMMO-Auth core abstract hierarchy
BaseAuthenticatorCore<TConnection>
├── SrpAuthenticatorCore<TConnection>    # used as base for ServerAuthenticatorCore (inner class)
└── TokenAuthenticatorCore<TConnection>  # used as base for TokenCore (inner class)

# Inner sealed class example (ServerAuthenticator.cs)
private sealed class ServerAuthenticatorCore : SrpAuthenticatorCore<NetworkConnection>
{
    // implements all abstract callbacks by routing to _outer (the ServerAuthenticator instance)
}
```

### Key Interfaces and Classes

- **`IServerAuthenticator`** — Unity interface exposing `Server` property, `InitializeWorkers()`, and `ShutdownWorkers()`. Consumed by `FishNetNetworkWrapper.AttachLoginAuthenticator`.
- **`BaseServerAuthenticator`** — Abstract Unity composition host. Holds `protected abstract BaseAuthenticatorCore<NetworkConnection> Core`. Owns the `ConcurrentQueue<Action>` drained by `Update()`, handles `InitializeOnce`, `OnRemoteConnectionState`, `ClientHandshake` broadcast routing, and the `OnAuthSweep()` / `TryLoginAsync()` virtual hooks.
- **`ServerAuthenticator`** — SRP wrapper. Inspector fields: `tokenExpirationMinutes`. Contains inner `ServerAuthenticatorCore : SrpAuthenticatorCore<NetworkConnection>` which implements all abstract callbacks by routing to `_outer`. The outer class provides DB method implementations (`FetchAccountForLoginCoreAsync`, `CheckIsOnlineCoreAsync`, `PersistTokenHashCoreAsync`, `VerifyTotpCodeCoreAsync`, etc.).
- **`TokenServerAuthenticator`** — Token wrapper. Contains inner `TokenCore : TokenAuthenticatorCore<NetworkConnection>`. Provides `FetchSigningKeyCoreAsync` and `CheckTokenRevocationCoreAsync`.
- **`BaseAuthenticatorCore<TConnection>`** (FishMMO-Auth) — Engine-independent base: cookie challenge, X25519 handshake, stale-auth TTL sweep, pending-auth cap, global/per-IP handshake rate limits, worker lifecycle (`InitializeWorkers`, `ShutdownWorkers`, `Tick`), and `HandleConnectionStopped`.
- **`SrpAuthenticatorCore<TConnection>`** (FishMMO-Auth) — All SRP-6a logic: bounded verify/proof channels, workers, fake SRP verifier, TOTP, per-IP/account debounce, IP cache, kick-request debounce, token generation. `TickRateLimits()` must be called each frame from the Unity wrapper's `OnUpdate()`.
- **`TokenAuthenticatorCore<TConnection>`** (FishMMO-Auth) — All token-auth logic: bounded token channel, worker, HMAC verification with timing equalization, expiration and revocation checks.

### Broadcast Types

| Broadcast | Direction | Contents |
|-----------|-----------|----------|
| `ClientHandshake` | Client → Server | X25519 public key, cookie (phase 2), min/max protocol version |
| `ServerHandshake` | Server → Client | X25519 public key (phase 2) or cookie (phase 1), agreed version |
| `SrpVerifyBroadcast` | Bidirectional | Encrypted username/salt + public ephemeral |
| `SrpProofBroadcast` | Client → Server | Encrypted client proof |
| `SrpSuccessBroadcast` | Server → Client | Encrypted server proof + auth result + encrypted token |
| `TokenAuthBroadcast` | Client → Server | Encrypted auth token |
| `TwoFactorVerifyBroadcast` | Client → Server | Encrypted TOTP or recovery code + sequence number |
| `ClientAuthResultBroadcast` | Server → Client | Authentication result code |

### Authentication Results

| Result | Meaning |
|--------|---------|
| `SrpVerify` | SRP verify phase completed, awaiting proof |
| `LoginSuccess` | Full SRP authentication succeeded |
| `InvalidUsernameOrPassword` | Credentials failed or SRP proof invalid |
| `AlreadyOnline` | Account has an online character (kick request issued) |
| `Banned` | Account is banned |
| `ServerBusy` | Channel full, services unavailable, or rate-limited |
| `WorldLoginSuccess` | World-server entry approved |
| `SceneLoginSuccess` | Scene-server entry approved |
| `ServerFull` | World server locked or at capacity |
| `NoCharacterSelected` | No selected character for world entry |
| `AccountUnverified` | Account exists but email not verified (username login only) |
| `TwoFactorRequired` | SRP proof valid; TOTP/recovery code required to complete login |
| `TwoFactorInvalid` | TOTP code or recovery code verification failed |
| `TokenInvalid` | Token HMAC verification failed or structure invalid |
| `TokenExpired` | Token past expiration time |
| `TokenRevoked` | Token revoked in database |
| `TokenDecryptFailed` | Client-only: SRP login succeeded but auth token decryption failed (non-fatal, not sent over wire) |

### Security Summary

| Defence | Mechanism |
|---------|-----------|
| AES-256-GCM with AAD binding | `(messageType, agreedVersion, sequenceNumber)` — cross-message swap → GCM tag failure |
| Counter-based nonce | `[prefix(4B)‖dir(1B)‖pad(3B)‖counter(4B)]` — overflow guard at `uint.MaxValue` |
| Strict UTF-8 | `DecoderFallbackException` → `ZeroMemory` + disconnect |
| Buffer zeroing | `CryptographicOperations.ZeroMemory()` on all decrypted data |
| Constant-time compare | `CryptographicOperations.FixedTimeEquals` for proofs, cookies, tokens |
| Forward secrecy | Ephemeral X25519 keypairs discarded after ECDH |
| Transcript binding | SHA-256 hash of `(domain‖clientPub‖serverPub‖versions)` fed into HKDF |
| Cookie challenge | Stateless HMAC-SHA256, time-bucketed, fail-closed rotation |
| Fake SRP state | Pre-computed verifier + per-username HMAC-SHA512 salt derivation |
| Email enumeration prevention | Unverified accounts on email login use fake SRP (same as non-existent) |
| TOTP anti-replay | Persisted last-used window; conditional DB update rejects same-window reuse |
| Recovery code one-time use | Matched code consumed via `ConsumeCodeAsync` immediately after verification |
| Per-username TOTP lockout | 15 failures / 30 min cross-connection; bounded tracker with sweep |
| Cookie HMAC IP length prefix | 2-byte big-endian IP length prefix eliminates variable-length concatenation ambiguity |
| Error indistinguishability | Generic failures, no protocol-level detail |

### Cleanup and Lifecycle

| Trigger | Action |
|---------|--------|
| **Client Disconnect** | `OnRemoteConnectionState` purges transient auth state + `AccountManager` data |
| **Worker Shutdown** | `ShutdownWorkers()` → complete channel writers → cancel CTS → drain main-thread queue → clear shared state → zero HMAC key |
| **Stale Auth Sweep** | Periodic 1 s sweep disconnects + purges half-open sessions (15 s TTL, 60 s hard deadline) |
| **AccountManager Backstop** | `SweepUnauthenticatedConnections()` with oldest-first tracking purges stale SRP/encryption state |
| **ExpiringKeyTracker Sweep** | `OnAuthSweep()` / `OnUpdate()` in subclasses evicts stale debounce/rate-limit entries |
| **Cookie Key Rotation** | `InitializeWorkers()` regenerates HMAC key; `ShutdownWorkers()` zeroes it — in-flight cookies fail-closed |

### Extensibility

`TryLoginAsync` is `internal virtual` on `BaseServerAuthenticator`, returning `Task<ClientAuthenticationResult>`. Subclasses override for server-type-specific admission logic:

- **LoginServer** (`ServerAuthenticator`) — Default: returns `LoginSuccess`.
- **WorldServer** (`WorldServerAuthenticator`) — Checks world lock, player cap, selected character via `ExpiringKeyTracker`-rate-limited DB query.
- **SceneServer** (`SceneServerAuthenticator`) — Scene-transfer pass-through.

### External Dependencies

| Dependency | Purpose |
|------------|---------|
| `FishNet.Authenticating.Authenticator` | Base class for auth lifecycle |
| `FishNet.Connection.NetworkConnection` | Connection type (`TConnection`) |
| `System.Threading.Channels` | Bounded async producer-consumer queues |
| `SecureRemotePassword` | SRP-6a library (2048-bit group, SHA-512) |
| `FishMMO-Auth.dll` | Shared auth library: `CryptoHelper` (AES-256-GCM, X25519 ECDH, HKDF-SHA256, `GcmNonceContext`, nonce builder, `StrictUtf8`, constant-time compare), `ConnectionEncryptionData`, `ServerSrpData`, `SrpVerifyRequest`, `SrpProofRequest`, `AuthState`, `IAccountManager`, `ISrpAccountManager`, `ITokenAccountManager`, `AccessLevel`, `ClientAuthenticationResult`, `HandshakeService`, `SrpService`, `TokenService` |
| `FishMMO.Shared.Authentication` | Centralized validation rules (`IsAllowedUsername`, `IsAllowedPassword`) |
| `IAccountService` | Database: fetch account salt/verifier for SRP |
| `ICharacterService` | Database: check online characters |
| `IKickRequestService` | Database: persist kick requests for already-online accounts |
| `IAuthTokenService` | Database: issue/fetch/revoke auth tokens by hash |
| `ILoginServerSigningKeyService` | Database: fetch HMAC signing keys by LoginServer ID |
| `ITwoFactorRecoveryCodeService` | Database: fetch unused recovery code hashes, consume used codes |
| `ExpiringKeyTracker<T>` | Head-first expiry queue for bounded rate limiting |
| `LastSeenCacheTracker<K, V>` | TTL cache with bounded sweep for IP/encryption caches |
| `ArrivalOrderTracker<T>` | Oldest-first tracking for stale-connection sweeps |
| `FishMMO.Logging.Log` | Structured async logging |

## License

This module is part of the FishMMO project and is subject to the FishMMO project license.
