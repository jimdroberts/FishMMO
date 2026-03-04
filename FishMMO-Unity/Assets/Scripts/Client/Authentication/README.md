# Client Authentication System

## Overview

The client authentication system implements the client side of SRP-6a (Secure Remote Password) authentication with AES-256-GCM encrypted transport. It handles X25519 ECDH key exchange, directional counter-based nonce management, SRP verify/proof flow, account creation, and credential lifecycle. All crypto operations use BouncyCastle under the hood via `CryptoHelper`.

## Directory Structure

```
Client/Authentication/
├── ClientLoginAuthenticator.cs   # Client-side SRP authentication + account creation flow
└── ClientSrpData.cs              # Client SRP state (ephemeral, proof, session verify)

Related:
├── Shared/Implementation/Tools/Extensions/Crypto/CryptoHelper.cs   # AES-256-GCM, X25519 ECDH, HKDF-SHA256, StrictUtf8, nonce builder
├── Shared/Implementation/Network/Authentication/                     # Broadcast message types (ClientHandshake, ServerHandshake, etc.)
└── FishMMO-SharedUtility/Authentication.cs            # Centralized validation rules (IsAllowedUsername, IsAllowedPassword)
```

## Authentication Flow

### Phase 1: Key Exchange

```
Client                                    Server
  │                                          │
  │  Generate X25519 ephemeral keypair        │
  │── ClientHandshake { PublicKey } ───────► │
  │                                          │  X25519 ECDH + HKDF-SHA256
  │◄── ServerHandshake { PublicKey, Cookie } │  → directional AES keys + session prefix
  │                                          │
  │  X25519 ECDH + HKDF-SHA256               │
  │  → clientToServerKey, serverToClientKey   │
  │  → sendNonceCtx, receiveNonceCtx         │
  │  Zero + dispose X25519 private key        │
```

### Phase 2a: SRP Verify (Login Path)

```
Client                                    Server
  │                                          │
  │  AES-GCM encrypt username + ephemeral     │
  │── SrpVerifyBroadcast ─────────────────► │
  │                                          │
  │◄── SrpVerifyBroadcast ──────────────── │  Encrypted salt + server ephemeral
  │                                          │
  │  AES-GCM decrypt salt + server ephemeral  │
  │  ZeroMemory on decrypted byte arrays      │
  │  SrpData.GetProof(user, pass, salt, eph)  │
  │  ** Null username + password **           │
```

### Phase 2b: Account Creation (Register Path)

```
Client                                    Server
  │                                          │
  │  SrpData.GetSaltAndVerifier(user, pass)   │
  │  AES-GCM encrypt username, salt, verifier │
  │── CreateAccountBroadcast ──────────────► │  → AccountCreationSystem pipeline
```

### Phase 3: SRP Proof

```
Client                                    Server
  │                                          │
  │  AES-GCM encrypt clientProof              │
  │── SrpProofBroadcast ──────────────────► │
  │                                          │
  │◄── SrpSuccessBroadcast ─────────────── │  Encrypted server proof + result
  │                                          │
  │  AES-GCM decrypt server proof             │
  │  ZeroMemory on decrypted byte array       │
  │  SrpData.Verify(serverProof) → success    │
  │  Invoke OnClientAuthenticationResult      │
```

## Counter-Based Nonce Scheme

Each AES-GCM operation uses a deterministic 12-byte nonce with directional separation:

```
┌─────────────┬───────────┬────────────┬─────────────┐
│ Prefix (4B) │ Dir (1B)  │ Pad (3B)   │ Counter (4B)│
│ HKDF-       │ 0x00=C→S  │ 0x00 0x00  │ big-endian  │
│ derived     │ 0x01=S→C  │ 0x00       │ uint32      │
└─────────────┴───────────┴────────────┴─────────────┘
```

- **Send nonce** (`sendNonceCtx`): direction = `0x00` (client→server), increments `sendCounter`.
- **Receive nonce** (`receiveNonceCtx`): direction = `0x01` (server→client), increments `receiveCounter`.
- Both throw `CryptographicException` at `uint.MaxValue` to prevent nonce reuse.
- The nonce is also passed as **AAD** to AES-GCM, binding ciphertext to its nonce context.

### AAD Construction

Additional Authenticated Data is built from `(messageType, agreedVersion, sequenceNumber)`, binding each ciphertext to its semantic purpose and preventing cross-message-type transplant attacks.

## Security Measures

### Credential clearing

- `username` and `password` are nulled immediately after `SrpData.GetProof()` (the last point of use).
- `ClearKeyMaterial()` nulls credentials again as a safety net on disconnect.
- .NET strings are immutable and cannot be deterministically zeroed; nulling removes the reference for GC collection. The `SecureRemotePassword` library requires string parameters, making `byte[]`-based storage impractical.

### Key material zeroing

- `clientToServerKey` and `serverToClientKey` byte arrays are zeroed with `CryptographicOperations.ZeroMemory()` on disconnect.
- All decrypted plaintext buffers (salt, ephemeral, proof) are zeroed immediately after use.
- Session prefix bytes in `GcmNonceContext` are zeroed on cleanup.

### Try/catch on all AES operations

- Every `CryptoHelper.EncryptAES` and `DecryptAES` call is wrapped in `try/catch (CryptographicException)`.
- On failure: logs warning, calls `ForceDisconnect()`, zeros any intermediate buffers.

### X25519 keypair lifecycle

- A new X25519 ephemeral keypair is generated per connection attempt.
- The private key is zeroed and disposed via `X25519EphemeralKeyPair.Dispose()` after ECDH derivation.
- The keypair reference is nulled on disconnect via `ClientManager_OnClientConnectionState`.

### Counter overflow protection

- Both send and receive nonce contexts check for `uint.MaxValue` before incrementing.
- Overflow throws `CryptographicException`, guaranteeing nonce uniqueness within a session.

## Key Types

| Type | Purpose |
|------|---------|
| `ClientLoginAuthenticator` | Orchestrates client-side auth flow, manages key material, nonce contexts, credential lifecycle |
| `ClientSrpData` | Wraps `SrpClient` — generates ephemeral, computes proof, verifies server proof |

## Events

| Event | Fired When |
|-------|-----------|
| `OnClientAuthenticationResult` | Server sends `SrpSuccessBroadcast` or `ClientAuthResultBroadcast` |

## Dependencies

| Dependency | Purpose |
|------------|---------|
| `FishNet.Authenticating.Authenticator` | Base class for client/server authentication |
| `SecureRemotePassword` | SRP-6a protocol library (2048-bit group, SHA-512) |
| `CryptoHelper` | AES-256-GCM with AAD, X25519 ECDH + HKDF-SHA256, nonce builder, `StrictUtf8`, `GcmNonceContext` |
| `FishMMO.Shared.Authentication` | Centralized validation: `IsAllowedUsername` (3–32 chars, alphanumeric + underscores), `IsAllowedPassword` (8–32 chars, expanded charset) |
| `Client` | FishNet client wrapper for `Broadcast()` and `ForceDisconnect()` |