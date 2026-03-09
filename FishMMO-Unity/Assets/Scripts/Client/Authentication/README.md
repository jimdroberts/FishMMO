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

The client authentication module handles the entire client-side lifecycle of authenticating with FishMMO servers. On connection start, `ClientLoginAuthenticator` generates an ephemeral X25519 keypair and initiates a handshake with the server. The server may respond with a stateless cookie challenge (proof-of-reachability) which the client echoes back, or a full handshake containing the server's X25519 public key and a negotiated protocol version. Once the handshake completes, both sides derive directional AES-256 session keys via HKDF-SHA256 over a transcript-bound shared secret, establishing an encrypted channel.

With the encrypted channel in place, the authenticator supports three flows:

1. **Login (SRP verify/proof)** — The client sends its encrypted username and SRP ephemeral to the server, receives an encrypted salt and server ephemeral, computes the SRP client proof via `ClientSrpData`, sends the encrypted proof, and verifies the server's proof from the `SrpSuccessBroadcast`. Credentials are nulled immediately after proof generation.

2. **Account creation** — The client generates an SRP salt and verifier from the username and password via `ClientSrpData.GetSaltAndVerifier()`, encrypts all three fields, and sends a `CreateAccountBroadcast`. The server's `AccountCreationSystem` pipeline processes the request.

3. **Token-based reconnection** — After a successful SRP login with the LoginServer, the client stores an encrypted auth token. On subsequent connections to World/Scene servers, the stored token is encrypted and sent via `TokenAuthBroadcast`, bypassing the full SRP flow.

`ClientSrpData` wraps the `SrpClient` library (2048-bit group, SHA-512) to generate ephemeral values, compute client proofs, generate salt/verifier pairs for registration, and verify server proofs. All SRP references are explicitly nulled on cleanup to allow GC collection of sensitive string data.

All cryptographic operations use BouncyCastle under the hood via `CryptoHelper`. Every AES-GCM encrypt/decrypt call is wrapped in `try/catch (CryptographicException)` with immediate `ForceDisconnect()` and buffer zeroing on failure. Duplicate-message guards (`srpVerifyProcessed`, `srpSuccessProcessed`, `cookieEchoed`) prevent replay of critical protocol messages within a session.

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
- `CryptoHelper` (from `FishMMO.Shared`) — AES-256-GCM with AAD, X25519 ECDH + HKDF-SHA256, `GcmNonceContext`, `StrictUtf8`, `BuildAad()`, `MaxSrpPayloadBytes`, protocol version constants.
- `FishMMO.Shared.Authentication` — centralized validation rules (`IsAllowedUsername`: 3–32 chars, alphanumeric + underscores; `IsAllowedPassword`: 8–32 chars, expanded charset).
- `Client` class — FishNet client wrapper providing `Broadcast()` and `ForceDisconnect()`.
- `FishMMO.Logging` — structured logging via `Log.Warning()`, `Log.Error()`, `Log.Debug()`.
- Shared broadcast message types: `ClientHandshake`, `ServerHandshake`, `SrpVerifyBroadcast`, `SrpProofBroadcast`, `SrpSuccessBroadcast`, `CreateAccountBroadcast`, `TokenAuthBroadcast`, `ClientAuthResultBroadcast`.

## Installation / Build

This is an integrated module within the FishMMO Unity project. No separate installation steps are required. The client authentication classes are compiled as part of the client assembly and depend on the shared `CryptoHelper` and `Authentication` utilities.

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
├── ClientLoginAuthenticator.cs   # Client-side SRP authentication + token auth + account creation flow
├── ClientSrpData.cs              # Client SRP state (ephemeral, proof, session verify, salt/verifier generation)
└── README.md                     # This file
```

### Related Modules

```
Shared/Implementation/Tools/Extensions/Crypto/
└── CryptoHelper.cs               # AES-256-GCM, X25519 ECDH, HKDF-SHA256, StrictUtf8, GcmNonceContext, BuildAad

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

```
FishNet.Authenticating.Authenticator (abstract)
└── ClientLoginAuthenticator
        ├── Manages: ClientSrpData (composition)
        ├── Manages: CryptoHelper.X25519EphemeralKeyPair (composition)
        ├── Manages: CryptoHelper.GcmNonceContext × 2 (send + receive)
        └── Events: OnClientAuthenticationResult
```

```
System.Object
└── ClientSrpData
        ├── Wraps: SrpClient (SecureRemotePassword library)
        ├── Holds: SrpEphemeral (client ephemeral values)
        └── Holds: SrpSession (proof + session key)
```

### Key Types

| Type | Purpose |
|------|---------|
| `ClientLoginAuthenticator` | Orchestrates client-side auth flow: handshake, SRP verify/proof, account creation, token auth, key material lifecycle, nonce contexts, credential clearing |
| `ClientSrpData` | Wraps `SrpClient` — generates ephemeral values, computes client proof, generates salt/verifier for registration, verifies server proof |

### Events

| Event | Fired When |
|-------|-----------|
| `OnClientAuthenticationResult` | Server sends `SrpSuccessBroadcast` (after successful SRP verification) or `ClientAuthResultBroadcast` (token auth result or other server-initiated result) |

### External Dependencies

| Dependency | Purpose |
|------------|---------|
| `FishNet.Authenticating.Authenticator` | Base class for client/server authentication in FishNet |
| `FishNet.Managing.NetworkManager` | Provides `ClientManager` for broadcast registration and connection state events |
| `SecureRemotePassword` | SRP-6a protocol library (2048-bit group, SHA-512): `SrpClient`, `SrpParameters`, `SrpEphemeral`, `SrpSession` |
| `CryptoHelper` | AES-256-GCM with AAD, X25519 ECDH + HKDF-SHA256, `GcmNonceContext`, nonce builder, `StrictUtf8`, `BuildAad()`, `MaxSrpPayloadBytes`, protocol version constants, `HandshakeDomainSeparator` |
| `FishMMO.Shared.Authentication` | Centralized validation: `IsAllowedUsername` (3–32 chars, alphanumeric + underscores), `IsAllowedPassword` (8–32 chars, expanded charset) |
| `Client` | FishNet client wrapper for `Broadcast()` and `ForceDisconnect()` |
| `FishMMO.Logging` | Structured logging via `Log.Warning()`, `Log.Error()`, `Log.Debug()` |

## License

This module is part of the FishMMO project and is subject to the FishMMO project license.
