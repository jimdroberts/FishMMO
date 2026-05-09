# Client Authentication

**Short description:** The client authentication module implements the client side of SRP-6a (Secure Remote Password) authentication with X25519 ECDH key exchange, AES-256-GCM encrypted transport, counter-based nonce management, token-based reconnection, and account creation — all orchestrated through FishNet broadcast handlers.

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

The client authentication module handles the entire client-side lifecycle of authenticating with FishMMO servers. The Unity-facing class `ClientLoginAuthenticator` is a **thin FishNet adapter** that owns an inner sealed `LoginAuthenticatorCore : ClientAuthenticatorCore` (from `FishMMO-Auth.dll`). The engine-independent `ClientAuthenticatorCore` runs the entire client-side state machine — ephemeral X25519 keypair generation, handshake, cookie echoing, ECDH + transcript-bound HKDF key derivation, AES-256-GCM encrypt/decrypt with counter-based nonces, SRP verify/proof, account creation, token reconnection, two-factor verification, duplicate-message guards, and key-material zeroing.

The Unity wrapper's job is purely routing:

- Implements the abstract `Send*` callbacks on `ClientAuthenticatorCore` by emitting FishNet broadcasts via `Client.Broadcast(...)`.
- Implements `Disconnect()` by calling `Client.ForceDisconnect()`.
- Forwards FishNet `OnClientConnectionState` (`Started` / `Stopping` / `Stopped`) into `core.OnConnected()` / `core.OnDisconnected()`.
- Forwards incoming `ServerHandshake`, `SrpVerifyBroadcast`, `SrpSuccessBroadcast`, `ClientAuthResultBroadcast`, and `TwoFactorSetupBroadcast` broadcasts into the matching core handlers.
- Surfaces `OnClientAuthenticationResult` and `OnTwoFactorSetupReceived` as Unity-facing events.
- Disposes the core in `OnDestroy()`.

With the encrypted channel established by the core, the authenticator supports four flows:

1. **Login (SRP verify/proof)** — Encrypted username and SRP ephemeral are sent, the encrypted salt and server ephemeral come back, and the core computes the client proof via `ClientSrpData`, sends the encrypted proof, and verifies the server's proof from `SrpSuccessBroadcast`. Credentials are nulled inside the core immediately after proof generation.

2. **Account creation** — The core generates an SRP salt and verifier from the username and password via `ClientSrpData.GetSaltAndVerifier()`, encrypts all three fields, and sends `CreateAccountBroadcast`. The server's `AccountCreationSystem` pipeline processes the request.

3. **Token-based reconnection** — After a successful SRP login with the LoginServer, the core stores an encrypted auth token. On subsequent connections to World/Scene servers, the stored token is encrypted and sent via `TokenAuthBroadcast`, bypassing the full SRP flow.

4. **Two-factor authentication** — If the server returns `TwoFactorRequired` after SRP proof, the public `SendTotpCode(string)` API encrypts and sends a TOTP code (6-digit) or recovery code (XXXXX-XXXXX) via `TwoFactorVerifyBroadcast`. During account creation, the server may send `TwoFactorSetupBroadcast` containing the encrypted otpauth URI and recovery codes; the core surfaces this via `OnTwoFactorSetupReceived`.

`ClientSrpData` (from `FishMMO.Auth.Core`) wraps the `SrpClient` library (2048-bit group, SHA-512) to generate ephemeral values, compute client proofs, generate salt/verifier pairs for registration, and verify server proofs. All SRP references are explicitly nulled on cleanup to allow GC collection of sensitive string data.

All cryptographic operations are performed by the core through three static service classes in the `FishMMO.Auth.Implementation` namespace (shipped via `FishMMO-Auth.dll`):

- **`HandshakeService`** — X25519 ECDH key agreement and transcript-hash computation.
- **`SrpService`** — Client-side SRP field encryption/decryption (username, ephemeral, proof, salt, registration fields, TOTP, 2FA setup, account verify, auth token).
- **`TokenService`** — Client-side token encryption for World/Scene server authentication.

Every AES-GCM encrypt/decrypt call inside the core is wrapped in `try/catch (CryptographicException)` with immediate disconnect (via the wrapper's `Disconnect()` callback) and buffer zeroing on failure. Duplicate-message guards (`srpVerifyProcessed`, `srpSuccessProcessed`, `cookieEchoed`) inside the core prevent replay of critical protocol messages within a session.

## Supported Platforms

| Platform | Supported | Notes |
|----------|-----------|-------|
| Windows  | Yes       | Full SRP-6a + AES-256-GCM authentication pipeline |
| Linux    | Yes       | Full SRP-6a + AES-256-GCM authentication pipeline |
| WebGL    | Yes       | Full SRP-6a + AES-256-GCM authentication pipeline |

| Requirement       | Version / Detail |
|-------------------|------------------|
| Unity             | 6.3 LTS          |
| Scripting Backend | IL2CPP           |

## Features

- **X25519 ECDH key exchange** — generates a fresh ephemeral keypair per connection; the private key is zeroed and disposed via `X25519EphemeralKeyPair.Dispose()` immediately after ECDH derivation.
- **Transcript-bound key derivation** — the shared secret is derived over a SHA-256 transcript hash incorporating domain separator, both public keys, client min/max versions, and the agreed version, preventing downgrade and key-substitution attacks.
- **Directional AES-256-GCM session keys** — HKDF derives separate `clientToServerKey` and `serverToClientKey` plus directional nonce prefixes, ensuring send and receive channels are cryptographically independent.
- **Counter-based deterministic nonces** — 12-byte nonces composed of a 4-byte HKDF-derived prefix, 1-byte direction flag (`0x00` = client→server, `0x01` = server→client), 3 bytes zero padding, and a 4-byte big-endian counter; overflow at `uint.MaxValue` throws `CryptographicException` to prevent nonce reuse.
- **Additional Authenticated Data (AAD)** — each ciphertext is bound to `(messageType, agreedVersion, sequenceNumber)` via `CryptoHelper.BuildAad()`, preventing cross-message-type transplant attacks.
- **Stateless cookie challenge** — the server may send a cookie for proof-of-reachability; the client echoes it back exactly once per connection (`cookieEchoed` guard) before the full X25519 handshake proceeds.
- **Protocol version negotiation** — the client advertises `[MinSupportedProtocolVersion, MaxSupportedProtocolVersion]` and validates the server's `AgreedVersion` falls within that range.
- **SRP-6a authentication (2048-bit, SHA-512)** — client generates ephemeral, receives encrypted salt + server ephemeral, computes proof, and verifies server proof via `ClientSrpData`.
- **Account creation flow** — generates SRP salt and verifier, encrypts username/salt/verifier, and sends `CreateAccountBroadcast` with implicit sequence encoding (server derives per-field sequences from a single `Seq` value).
- **Token-based reconnection** — after LoginServer SRP success, an encrypted auth token is stored and automatically sent via `TokenAuthBroadcast` on subsequent World/Scene server connections; token expiration is enforced server-side.
- **Two-factor authentication** — `SendTotpCode(code)` encrypts and sends a TOTP or recovery code via `TwoFactorVerifyBroadcast`. The `code` parameter accepts both 6-digit TOTP codes and XXXXX-XXXXX recovery codes. `OnTwoFactorSetupReceived` fires during account creation with the otpauth URI and recovery codes.
- **Token failure handling** — `ClientAuthResultBroadcast` with `TokenInvalid`, `TokenExpired`, or `TokenRevoked` results automatically clears the stored token via `ClearAuthToken()` to prevent infinite retry loops.
- **Credential clearing** — `username` and `password` are nulled immediately after `SrpData.GetProof()` (the last point of use) and again as a safety net in `ClearKeyMaterial()` on disconnect.
- **Key material zeroing** — `clientToServerKey`, `serverToClientKey`, all decrypted plaintext buffers (salt, ephemeral, proof), and `GcmNonceContext` session prefixes are zeroed with `CryptographicOperations.ZeroMemory()` on disconnect or after use.
- **Duplicate-message guards** — `srpVerifyProcessed`, `srpSuccessProcessed`, and `cookieEchoed` flags prevent reprocessing of critical protocol messages within a single connection.
- **Pre-validation** — username length is checked client-side (3–32 characters) before consuming a sequence number, failing fast on payloads that the server would reject.
- **Strict UTF-8 decoding** — all decrypted byte arrays are decoded via `CryptoHelper.StrictUtf8` with `DecoderFallbackException` handling; malformed data triggers disconnect.
- **Max payload size enforcement** — all incoming encrypted fields are checked against `CryptoHelper.MaxSrpPayloadBytes` before decryption.
- **SRP state cleanup** — `ClientSrpData.Clear()` nulls all `SrpClient`, `ClientEphemeral`, and `Session` references so GC can collect sensitive SRP strings.

## Prerequisites

- Unity 6.3 LTS with IL2CPP scripting backend.
- FishNet Networking framework (`FishNet.Authenticating.Authenticator` base class, `NetworkManager`, `ClientManager`, `Broadcast` system).
- `SecureRemotePassword` library — provides `SrpClient`, `SrpParameters`, `SrpEphemeral`, `SrpSession` for 2048-bit SHA-512 SRP-6a.
- `FishMMO-Auth.dll` — Shared authentication library providing:
  - `FishMMO.Auth.Core`: `CryptoHelper` (AES-256-GCM, X25519 ECDH, HKDF-SHA256, `GcmNonceContext`, `StrictUtf8`, nonce builder, protocol version constants), `ClientSrpData`, `ClientAuthenticationResult`, `AccessLevel`.
  - `FishMMO.Auth.Implementation`: `HandshakeService`, `SrpService`, `TokenService` — static service classes that encapsulate all client-side crypto operations.
- `FishMMO.Shared.Authentication` — centralized validation rules (`IsAllowedUsername`: 3–32 chars, alphanumeric + underscores; `IsAllowedPassword`: 8–32 chars, expanded charset).
- `Client` class — FishNet client wrapper providing `Broadcast()` and `ForceDisconnect()`.
- `FishMMO.Logging` — structured logging via `Log.Warning()`, `Log.Error()`, `Log.Debug()`.
- Shared broadcast message types: `ClientHandshake`, `ServerHandshake`, `SrpVerifyBroadcast`, `SrpProofBroadcast`, `SrpSuccessBroadcast`, `CreateAccountBroadcast`, `TokenAuthBroadcast`, `ClientAuthResultBroadcast`, `TwoFactorVerifyBroadcast`, `TwoFactorSetupBroadcast`.

## Installation / Build

This is an integrated module within the FishMMO Unity project. The client authentication classes are compiled as part of the client assembly. They depend on `FishMMO-Auth.dll` (auto-copied to `Assets/Dependencies/` by the FishMMO-Auth build) for all crypto service classes and core types, and on the shared `Authentication` validation utilities.

Ensure `ClientLoginAuthenticator` is attached as the authenticator on the FishNet `NetworkManager` used for client connections, and that `SetClient()` is called with a valid `Client` instance before any connection attempt.

## Quick Start Guides

### Logging In

1. Obtain a reference to the `ClientLoginAuthenticator` from the `NetworkManager`.
2. Call `SetClient(client)` with the active `Client` instance.
3. Subscribe to `OnClientAuthenticationResult` to receive authentication outcomes.
4. Call `SetLoginCredentials(username, password, register: false)`.
5. Initiate the FishNet client connection — the authenticator automatically starts the X25519 handshake on `LocalConnectionState.Started`.
6. The SRP verify → proof → success flow completes automatically; `OnClientAuthenticationResult` fires with the result.

### Registering a New Account

1. Follow steps 1–3 from the login guide.
2. Call `SetLoginCredentials(username, password, register: true)`.
3. Initiate the connection — the authenticator sends a `CreateAccountBroadcast` with encrypted username, salt, and verifier after the handshake completes.

### Token-Based Reconnection

1. After a successful SRP login, the authenticator automatically stores the auth token from the `SrpSuccessBroadcast`.
2. On subsequent connections to World/Scene servers, the authenticator detects `HasAuthToken == true` and sends a `TokenAuthBroadcast` instead of initiating SRP.
3. If the token is rejected (`TokenInvalid`, `TokenExpired`, `TokenRevoked`), the authenticator clears it automatically and `OnClientAuthenticationResult` fires with the failure reason.
4. Call `ClearAuthToken()` explicitly on user logout.

### Two-Factor Authentication

1. Subscribe to `OnTwoFactorSetupReceived` for account-creation 2FA setup events (provides otpauth URI and recovery codes).
2. During login, if `OnClientAuthenticationResult` fires with `TwoFactorRequired`, prompt the user for a TOTP code or recovery code.
3. Call `authenticator.SendTotpCode(code)` with the 6-digit TOTP code or XXXXX-XXXXX recovery code.
4. `OnClientAuthenticationResult` fires again with `LoginSuccess` (on valid code) or `TwoFactorInvalid` (on failure — retry allowed).

## Configuration

### ClientLoginAuthenticator Properties

| Property | Type | Access | Description |
|----------|------|--------|-------------|
| `Client` | `Client` | Public (set via `SetClient()`) | FishNet client wrapper for broadcasting messages |
| `HasAuthToken` | `bool` | Public (read-only) | Whether a stored auth token exists for token-based reconnection |

### Credential Setup

| Method | Parameters | Description |
|--------|------------|-------------|
| `SetLoginCredentials()` | `string username, string password, bool register = false` | Sets login credentials and register flag before connection |
| `SetClient()` | `Client client` | Assigns the FishNet client instance for broadcasting |
| `ClearAuthToken()` | — | Zeroes and nulls the stored auth token |
| `SendTotpCode()` | `string code` | Encrypts and sends a TOTP (6-digit) or recovery code (XXXXX-XXXXX) via `TwoFactorVerifyBroadcast` |

### Validation Rules (from `FishMMO.Shared.Authentication`)

| Rule | Constraint |
|------|-----------|
| Username length | 3–32 characters |
| Username charset | Alphanumeric + underscores |
| Password length | 8–32 characters |
| Password charset | Expanded character set |

### SRP Parameters

| Parameter | Value |
|-----------|-------|
| Group size | 2048-bit |
| Hash algorithm | SHA-512 |
| Library | `SecureRemotePassword` |

## Usage Examples

### Setting Up the Authenticator

```csharp
// During client initialization:
ClientLoginAuthenticator authenticator = networkManager.GetAuthenticator() as ClientLoginAuthenticator;
authenticator.SetClient(client);
authenticator.OnClientAuthenticationResult += OnAuthResult;

private void OnAuthResult(ClientAuthenticationResult result)
{
    switch (result)
    {
        case ClientAuthenticationResult.AccountCreated:
            Log.Info("Auth", "Account created successfully.");
            break;
        case ClientAuthenticationResult.LoginSuccess:
            Log.Info("Auth", "Login successful.");
            break;
        case ClientAuthenticationResult.TokenExpired:
            Log.Warning("Auth", "Auth token expired, re-login required.");
            break;
        // Handle other results...
    }
}
```

### Login Flow

```csharp
authenticator.SetLoginCredentials("MyUsername", "MySecurePassword123", register: false);
networkManager.ClientManager.StartConnection("login.server.com", 7770);
// Handshake → SRP Verify → SRP Proof → SRP Success → OnClientAuthenticationResult fires
```

### Account Registration Flow

```csharp
authenticator.SetLoginCredentials("NewUser", "SecurePass456!", register: true);
networkManager.ClientManager.StartConnection("login.server.com", 7770);
// Handshake → CreateAccountBroadcast → OnClientAuthenticationResult fires
```

### Clearing Auth Token on Logout

```csharp
authenticator.ClearAuthToken();
networkManager.ClientManager.StopConnection();
```

## Operational Checks

| Check | How to Verify | Expected Result |
|-------|---------------|-----------------|
| Handshake initiates on connect | Start a client connection with valid `ClientLoginAuthenticator` | `ClientHandshake` broadcast sent with X25519 public key and version range |
| Cookie challenge echoed | Server sends `ServerHandshake` with `PublicKey == null` and a cookie | Client echoes `ClientHandshake` with the cookie; `cookieEchoed` set to `true` |
| Key exchange completes | Server sends full `ServerHandshake` with public key and agreed version | Directional AES-256 keys, nonce contexts, and `SrpData` are initialized |
| Version negotiation validated | Server sends `AgreedVersion` outside `[Min, Max]` | Client logs warning, clears key material, and calls `ForceDisconnect()` |
| SRP login completes | Provide valid credentials and connect to LoginServer | `OnClientAuthenticationResult` fires with success; credentials nulled |
| Account creation succeeds | Set `register: true` and connect | `CreateAccountBroadcast` sent with encrypted username, salt, and verifier |
| Token-based auth works | Reconnect to World/Scene server after login | `TokenAuthBroadcast` sent automatically; full SRP flow skipped |
| Token failure clears token | Server returns `TokenExpired` / `TokenInvalid` / `TokenRevoked` | `storedAuthToken` zeroed and nulled; `HasAuthToken` returns `false` |
| AES failure disconnects | Corrupt an encrypted payload | `CryptographicException` caught, warning logged, `ForceDisconnect()` called |
| Malformed UTF-8 disconnects | Server sends non-UTF-8 bytes in encrypted SRP field | `DecoderFallbackException` caught, warning logged, `ForceDisconnect()` called |
| Payload size enforced | Encrypted field exceeds `MaxSrpPayloadBytes` | Client clears key material and calls `ForceDisconnect()` before decryption |
| Key material zeroed on disconnect | Disconnect or stop connection | `ClearKeyMaterial()` zeroes keys, disposes nonce contexts, nulls credentials and SRP data |
| Duplicate verify ignored | Server sends second `SrpVerifyBroadcast` | `srpVerifyProcessed` guard returns early |
| Duplicate success ignored | Server sends second `SrpSuccessBroadcast` | `srpSuccessProcessed` guard returns early |
| Pre-validation rejects bad username | Set username shorter than 3 or longer than 32 characters | Client logs warning and calls `ForceDisconnect()` before sending any SRP broadcast |

## Flow Diagram

### Phase 1: Key Exchange (X25519 ECDH)

```
Client                                         Server
  │                                               │
  │  Generate X25519 ephemeral keypair             │
  │── ClientHandshake { PublicKey, Min, Max } ──► │
  │                                               │
  │  (Optional cookie challenge)                   │
  │◄── ServerHandshake { Cookie, PublicKey=null } │
  │                                               │
  │  Echo cookie (once per connection)             │
  │── ClientHandshake { PublicKey, Cookie } ─────►│
  │                                               │
  │◄── ServerHandshake { PublicKey, AgreedVer } ──│  X25519 ECDH + HKDF-SHA256
  │                                               │
  │  Validate AgreedVersion in [Min..Max]          │
  │  SHA-256 transcript hash:                      │
  │    domain || clientPub || serverPub ||          │
  │    clientMin(2B) || clientMax(2B) ||            │
  │    agreed(2B)                                  │
  │  DeriveSharedSecret(serverPub, transcript)     │
  │  Zero + dispose X25519 private key             │
  │  DeriveSessionKeys(sharedSecret, transcript)   │
  │  → clientToServerKey, serverToClientKey         │
  │  → sendNonceCtx (dir=0x00), receiveNonceCtx    │
  │    (dir=0x01)                                  │
```

### Phase 2a: SRP Verify (Login Path)

```
Client                                         Server
  │                                               │
  │  AES-GCM encrypt(username, AAD=SrpVerify)      │
  │  AES-GCM encrypt(clientEphemeral, AAD=SrpVer)  │
  │── SrpVerifyBroadcast { S, PublicEphemeral } ─►│
  │                                               │
  │◄── SrpVerifyBroadcast { S, PublicEphemeral } ─│  Encrypted salt + server ephemeral
  │                                               │
  │  AES-GCM decrypt salt (AAD=SrpVerifyResponse)  │
  │  AES-GCM decrypt serverEphemeral               │
  │  ZeroMemory on decrypted byte arrays           │
  │  StrictUtf8 decode (or disconnect on malform)  │
  │  SrpData.GetProof(user, pass, salt, eph)       │
  │  ** Null username + password **                │
```

### Phase 2b: Account Creation (Register Path)

```
Client                                         Server
  │                                               │
  │  SrpData.GetSaltAndVerifier(user, pass)        │
  │  AES-GCM encrypt(username, AAD=SrpVerify)      │
  │  AES-GCM encrypt(salt, AAD=CreateAccount)      │
  │  AES-GCM encrypt(verifier, AAD=CreateAccount)  │
  │── CreateAccountBroadcast ────────────────────►│  → AccountCreationSystem pipeline
  │      { Username, Salt, Verifier, Seq }         │
  │                                               │
  │  Server derives per-field sequences:           │
  │    username_seq = Seq - 2                      │
  │    salt_seq     = Seq - 1                      │
  │    verifier_seq = Seq                          │
```

### Phase 3: SRP Proof

```
Client                                         Server
  │                                               │
  │  AES-GCM encrypt(clientProof, AAD=SrpProof)    │
  │── SrpProofBroadcast { Proof, Seq } ──────────►│
  │                                               │
  │◄── SrpSuccessBroadcast { Proof, Token, Res } ─│  Encrypted server proof + auth token
  │                                               │
  │  AES-GCM decrypt serverProof (AAD=SrpSuccess)  │
  │  ZeroMemory on decrypted byte array            │
  │  StrictUtf8 decode (or disconnect on malform)  │
  │  SrpData.Verify(serverProof) → success         │
  │  AES-GCM decrypt authToken → storedAuthToken   │
  │  Invoke OnClientAuthenticationResult           │
  │  SrpData.Clear() — null all SRP references     │
```

### Phase 3.5: Two-Factor Authentication (Conditional)

If the server responds to the SRP proof with `TwoFactorRequired`, the login flow pauses for TOTP verification. `SrpSuccessBroadcast` is not sent until 2FA completes.

```
Client                                         Server
  │                                               │
  │◄── ClientAuthResultBroadcast ─────────────────│  { TwoFactorRequired }
  │                                               │
  │  Prompt user for TOTP code or recovery code    │
  │  AES-GCM encrypt(code, AAD=TwoFactorVerify)   │
  │── TwoFactorVerifyBroadcast { Code, Seq } ────►│
  │                                               │
  │  On success:                                   │
  │◄── SrpSuccessBroadcast { Proof, Token, Res } ─│  Normal SRP success flow
  │                                               │
  │  On failure:                                   │
  │◄── ClientAuthResultBroadcast ─────────────────│  { TwoFactorInvalid }
  │  (Retry by calling SendTotpCode again)         │
```

### Phase 4: Token-Based Reconnection

```
Client                                         World/Scene Server
  │                                               │
  │  (X25519 handshake completes as Phase 1)       │
  │                                               │
  │  HasAuthToken == true → skip SRP               │
  │  AES-GCM encrypt(storedAuthToken, AAD=Token)   │
  │── TokenAuthBroadcast { Token, Seq } ─────────►│
  │                                               │
  │◄── ClientAuthResultBroadcast { Result } ──────│
  │                                               │
  │  If TokenInvalid/Expired/Revoked:              │
  │    ClearAuthToken() → zero + null token        │
  │  Invoke OnClientAuthenticationResult           │
```

### Counter-Based Nonce Scheme

```
┌─────────────┬───────────┬────────────┬─────────────┐
│ Prefix (4B) │ Dir (1B)  │ Pad (3B)   │ Counter (4B)│
│ HKDF-       │ 0x00=C→S  │ 0x00 0x00  │ big-endian  │
│ derived     │ 0x01=S→C  │ 0x00       │ uint32      │
└─────────────┴───────────┴────────────┴─────────────┘

Send nonce:    dir=0x00 (client→server), increments sendCounter
Receive nonce: dir=0x01 (server→client), increments receiveCounter
Overflow at uint.MaxValue → CryptographicException (prevents nonce reuse)
Nonce also passed as AAD to AES-GCM, binding ciphertext to its nonce context
```

### Disconnect / Cleanup Flow

```
LocalConnectionState.Stopping / Stopped
    │
    └── ClearKeyMaterial()
            │
            ├── ephemeralKeyPair.Dispose() → zero + null
            ├── SrpData.Clear() → null SrpClient, ClientEphemeral, Session
            ├── ZeroMemory(clientToServerKey) → null
            ├── ZeroMemory(serverToClientKey) → null
            ├── sendNonceCtx.Dispose() → zero prefix + null
            ├── receiveNonceCtx.Dispose() → zero prefix + null
            ├── agreedVersion = 0
            └── username = null, password = null
            
            NOTE: storedAuthToken is NOT cleared — it persists across connections.
            Call ClearAuthToken() explicitly on user logout.
```

## Project Structure

### Directory Tree

```
Client/Authentication/
├── ClientLoginAuthenticator.cs   # Thin FishNet adapter — owns inner LoginAuthenticatorCore (ClientAuthenticatorCore subclass)
└── README.md                     # This file
```

### FishMMO-Auth DLL (shared library)

Core types and crypto services consumed by the authenticator. Built from the `FishMMO-Auth` project and auto-copied to `Assets/Dependencies/FishMMO-Auth.dll`.

```
FishMMO-Auth/
├── Core/
│   ├── CryptoHelper.cs                # AES-256-GCM, X25519 ECDH, HKDF-SHA256, StrictUtf8, GcmNonceContext, nonce builder
│   ├── ClientSrpData.cs               # Client SRP state (ephemeral, proof, session verify, salt/verifier generation)
│   ├── ClientAuthenticationResult.cs  # Enum: auth result codes (LoginSuccess, TokenDecryptFailed, etc.)
│   └── AccessLevel.cs                 # Enum: Player, Moderator, Admin, etc.
│
├── Implementation/Auth/
│   └── ClientAuthenticatorCore.cs     # Engine-independent client-side state machine (abstract base for LoginAuthenticatorCore)
│
└── Implementation/Services/
    ├── HandshakeService.cs            # X25519 ECDH key agreement (client + server sides)
    ├── SrpService.cs                  # Client-side SRP encrypt/decrypt (username, ephemeral, proof, registration, TOTP, 2FA)
    └── TokenService.cs                # Client-side token encryption for World/Scene server auth
```

### Related Modules

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

FishMMO-SharedUtility/
└── Authentication.cs             # Centralized validation: IsAllowedUsername, IsAllowedPassword
```

### Inheritance Hierarchy

Because `Authenticator` (FishNet) is a MonoBehaviour and C# single-inheritance prevents the Unity class from also extending `ClientAuthenticatorCore`, the wrapper uses **composition with an inner sealed core class**:

```
FishNet.Authenticating.Authenticator (abstract MonoBehaviour)
└── ClientLoginAuthenticator                       # Unity adapter (this assembly)
        ├── owns: LoginAuthenticatorCore           # private sealed inner class
        │         └── : ClientAuthenticatorCore  # FishMMO-Auth abstract base
        │             └── owns:
        │                 ├── ClientSrpData              (SRP state)
        │                 ├── X25519EphemeralKeyPair     (ECDH key pair)
        │                 ├── GcmNonceContext × 2        (send + receive)
        │                 └── directional AES-256 keys, stored token, guards
        └── events: OnClientAuthenticationResult, OnTwoFactorSetupReceived
```

The inner `LoginAuthenticatorCore` implements all 11 abstract callbacks of `ClientAuthenticatorCore`:

| Abstract callback | Implementation |
|---|---|
| `SendClientHandshake` | `Client.Broadcast(new ClientHandshake { ... }, Channel.Reliable)` |
| `SendTokenAuth` | `Client.Broadcast(new TokenAuthBroadcast { ... }, Channel.Reliable)` |
| `SendSrpVerify` | `Client.Broadcast(new SrpVerifyBroadcast { ... }, Channel.Reliable)` |
| `SendSrpProof` | `Client.Broadcast(new SrpProofBroadcast { ... }, Channel.Reliable)` |
| `SendCreateAccount` | `Client.Broadcast(new CreateAccountBroadcast { ... }, Channel.Reliable)` |
| `SendAccountVerify` | `Client.Broadcast(new AccountVerifyBroadcast { ... }, Channel.Reliable)` |
| `SendTwoFactorVerify` | `Client.Broadcast(new TwoFactorVerifyBroadcast { ... }, Channel.Reliable)` |
| `Disconnect` | `_outer.Client.ForceDisconnect()` |
| `OnAuthResultCallback` | Invokes `_outer.OnClientAuthenticationResult` event |
| `OnTwoFactorSetupCallback` | Invokes `_outer.OnTwoFactorSetupReceived` event |
| `IsAllowedUsername` / `IsAllowedPassword` / `IsAllowedEmailUsername` | Delegate to `FishMMO.Shared.Authentication` |

```
System.Object (from FishMMO-Auth)
└── ClientSrpData
        ├── Wraps: SrpClient (SecureRemotePassword library)
        ├── Holds: SrpEphemeral (client ephemeral values)
        └── Holds: SrpSession (proof + session key)
```

### Key Types

| Type | Purpose |
|------|---------|
| `ClientLoginAuthenticator` | Thin FishNet MonoBehaviour adapter. Owns the inner `LoginAuthenticatorCore`, registers and routes broadcasts, surfaces public events, and disposes the core on `OnDestroy()`. Holds no auth state of its own. |
| `LoginAuthenticatorCore` *(private sealed inner class)* | `ClientAuthenticatorCore` subclass. Bridges all engine-independent send/disconnect/result callbacks to FishNet broadcasts via `Client.Broadcast(...)` and to `Client.ForceDisconnect()`. |
| `ClientAuthenticatorCore` *(FishMMO-Auth)* | Engine-independent base class — owns the X25519 keypair, AES keys, nonce contexts, `ClientSrpData`, stored auth token, duplicate-message guards, and the entire client-side handshake/SRP/token/2FA state machine. |
| `ClientSrpData` *(FishMMO-Auth)* | Wraps `SrpClient` — generates ephemeral values, computes client proof, generates salt/verifier for registration, verifies server proof |
| `HandshakeService` *(FishMMO-Auth)* | X25519 ECDH key agreement and transcript-hash computation |
| `SrpService` *(FishMMO-Auth)* | Client-side SRP encrypt/decrypt operations for all auth fields |
| `TokenService` *(FishMMO-Auth)* | Client-side token encryption for World/Scene server auth |

### Events

| Event | Fired When |
|-------|-----------|
| `OnClientAuthenticationResult` | Server sends `SrpSuccessBroadcast` (fires for **all** `msg.Result` values — LoginSuccess, AlreadyOnline, ServerBusy, etc. — not just success), `ClientAuthResultBroadcast` (token auth result, `TwoFactorRequired`, `TwoFactorInvalid`, or other server-initiated result), or `TokenDecryptFailed` (non-fatal: SRP login succeeded but token decryption failed; fired after the primary result so subscribers can initialize before seeing the warning) |
| `OnTwoFactorSetupReceived` | During account creation, server sends `TwoFactorSetupBroadcast` with encrypted otpauth URI and recovery codes |

### External Dependencies

| Dependency | Purpose |
|------------|---------|
| `FishNet.Authenticating.Authenticator` | Base class for client/server authentication in FishNet |
| `FishNet.Managing.NetworkManager` | Provides `ClientManager` for broadcast registration and connection state events |
| `SecureRemotePassword` | SRP-6a protocol library (2048-bit group, SHA-512): `SrpClient`, `SrpParameters`, `SrpEphemeral`, `SrpSession` |
| `FishMMO-Auth.dll` | Shared auth library: `CryptoHelper` (AES-256-GCM, X25519 ECDH, HKDF-SHA256, `GcmNonceContext`, nonce builder, `StrictUtf8`), `ClientSrpData`, `ClientAuthenticationResult`, `AccessLevel`, `HandshakeService`, `SrpService`, `TokenService` |
| `FishMMO.Shared.Authentication` | Centralized validation: `IsAllowedUsername` (3–32 chars, alphanumeric + underscores), `IsAllowedPassword` (8–32 chars, expanded charset) |
| `Client` | FishNet client wrapper for `Broadcast()` and `ForceDisconnect()` |
| `FishMMO.Logging` | Structured logging via `Log.Warning()`, `Log.Error()`, `Log.Debug()` |

## License

This module is part of the FishMMO project and is subject to the FishMMO project license.
