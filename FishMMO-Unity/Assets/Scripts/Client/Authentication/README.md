# Client Authentication System

## Overview

The client authentication system implements the client side of SRP-6a (Secure Remote Password) authentication with AES-256-GCM encrypted transport. It handles the RSA key exchange, counter-based nonce management, SRP verify/proof flow, and credential lifecycle. All crypto operations use BouncyCastle under the hood via `CryptoHelper`.

## Directory Structure

```
Client/Authentication/
├── ClientLoginAuthenticator.cs   # Client-side SRP authentication flow
├── ClientSrpData.cs              # Client SRP state (ephemeral, proof, session verify)
└── README.md

Related:
├── Shared/Tools/Extensions/Crypto/CryptoHelper.cs   # AES-GCM, RSA, nonce builder
└── Shared/Network/Authentication/                     # Broadcast message types
```

## Authentication Flow

### Phase 1: Key Exchange

```
Client                                    Server
  │                                          │
  │  Generate RSA-2048 key pair              │
  │── ClientHandshake { PublicKey } ───────► │
  │                                          │  Generate AES-256 key + 4-byte session prefix
  │◄── ServerHandshake { Key, Prefix } ──── │  RSA-OAEP-SHA256 encrypt both
  │                                          │
  │  RSA decrypt → symmetricKey, sessionPrefix
  │  Reset send/receive counters to 0
```

### Phase 2: SRP Verify (Login) or Account Creation (Register)

**Login path:**
```
Client                                    Server
  │                                          │
  │  Encrypt username with AES-GCM           │
  │  Encrypt client ephemeral with AES-GCM   │
  │── SrpVerifyBroadcast ─────────────────► │
  │                                          │
  │◄── SrpVerifyBroadcast ──────────────── │  Encrypted salt + server ephemeral
  │                                          │
  │  Decrypt salt + server ephemeral         │
  │  SrpData.GetProof(username, password,    │
  │    salt, serverEphemeral) → clientProof  │
  │  ** Clear username + password **         │
```

**Register path:**
```
Client                                    Server
  │                                          │
  │  SrpData.GetSaltAndVerifier(user, pass)  │
  │  Encrypt username, salt, verifier        │
  │── CreateAccountBroadcast ──────────────► │
```

### Phase 3: SRP Proof

```
Client                                    Server
  │                                          │
  │  Encrypt clientProof with AES-GCM        │
  │── SrpProofBroadcast ──────────────────► │
  │                                          │
  │◄── SrpSuccessBroadcast ─────────────── │  Encrypted server proof + result
  │                                          │
  │  Decrypt server proof                    │
  │  SrpData.Verify(serverProof) → success   │
  │  Invoke OnClientAuthenticationResult     │
```

## Counter-Based Nonce Scheme

Each AES-GCM operation uses a deterministic 12-byte nonce:

```
┌─────────────┬───────────┬────────────┬─────────────┐
│ Prefix (4B) │ Dir (1B)  │ Pad (3B)   │ Counter (4B)│
│ session     │ 0x00=C→S  │ 0x00 0x00  │ big-endian  │
│ prefix      │ 0x01=S→C  │ 0x00       │ uint32      │
└─────────────┴───────────┴────────────┴─────────────┘
```

- **Send nonce** (`NextSendNonce`): direction = `0x00` (client→server), increments `sendCounter`.
- **Receive nonce** (`NextReceiveNonce`): direction = `0x01` (server→client), increments `receiveCounter`.
- Both throw `CryptographicException` at `uint.MaxValue` to prevent nonce reuse.
- The nonce is also passed as **AAD** to AES-GCM, binding ciphertext to its nonce context.

## Security Measures

### Credential clearing

- `username` and `password` are nulled immediately after `SrpData.GetProof()` (the last point of use).
- `ClearKeyMaterial()` nulls credentials again as a safety net on disconnect.
- Strings are immutable in .NET, so nulling removes the reference for GC collection.

### Key material zeroing

- `symmetricKey` and `sessionPrefix` byte arrays are zeroed with `CryptographicOperations.ZeroMemory()` on disconnect.
- All decrypted plaintext buffers (salt, ephemeral, proof) are zeroed immediately after use.

### Try/catch on all AES operations

- Every `CryptoHelper.EncryptAES` and `DecryptAES` call is wrapped in `try/catch (CryptographicException)`.
- On failure: logs warning, calls `ForceDisconnect()`, zeros any intermediate buffers.

### RSA key pair lifecycle

- A new RSA-2048 key pair is generated per connection attempt.
- The key pair reference is nulled on disconnect (`ClientManager_OnClientConnectionState`).

## Key Types

| Type | Purpose |
|------|---------|
| `ClientLoginAuthenticator` | Orchestrates client-side auth flow, manages key material and nonce counters |
| `ClientSrpData` | Wraps `SrpClient` — generates ephemeral, computes proof, verifies server proof |

## Events

| Event | Fired When |
|-------|-----------|
| `OnClientAuthenticationResult` | Server sends `SrpSuccessBroadcast` or `ClientAuthResultBroadcast` |

## Dependencies

| Dependency | Purpose |
|------------|---------|
| `FishNet.Authenticating.Authenticator` | Base class for client/server authentication |
| `SecureRemotePassword` | SRP-6a protocol library (2048-bit, SHA-512) |
| `CryptoHelper` | AES-GCM with AAD, RSA-OAEP-SHA256, nonce builder |
| `Client` | FishNet client wrapper for `Broadcast()` and `ForceDisconnect()` |