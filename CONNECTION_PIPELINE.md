# FishMMO Connection Pipeline

> **Complete end-to-end connection flow** — from client launch through authentication, world entry, and gameplay.
> Built on QUIC/WebTransport (UDP) via MsQuic, NGINX L4 stream proxy, and FishNetworking.

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Infrastructure Topology](#infrastructure-topology)
- [Connection Flow Diagram](#connection-flow-diagram)
  - [Phase 1: Launcher Startup & Version Check](#phase-1-launcher-startup--version-check)
  - [Phase 2: Login Server Discovery (IPFetch)](#phase-2-login-server-discovery-ipfetch)
  - [Phase 3: QUIC/WebTransport Connection](#phase-3-quicwebtransport-connection)
  - [Phase 4: Cryptographic Handshake (X25519 ECDH)](#phase-4-cryptographic-handshake-x25519-ecdh)
  - [Phase 5: SRP-6a Authentication](#phase-5-srp-6a-authentication)
  - [Phase 6: TOTP Two-Factor Authentication](#phase-6-totp-two-factor-authentication)
  - [Phase 7: Token Issuance & Character Select](#phase-7-token-issuance--character-select)
  - [Phase 8: World Server Connection](#phase-8-world-server-connection)
  - [Phase 9: Scene Server Connection](#phase-9-scene-server-connection)
  - [Phase 10: Token Renewal & Revocation](#phase-10-token-renewal--revocation)
- [Protocol Layers](#protocol-layers)
- [Security Properties](#security-properties)
- [Rate Limiting & DDoS Protection](#rate-limiting--ddos-protection)
- [Reconnection Flow](#reconnection-flow)
- [Platform Support Matrix](#platform-support-matrix)
- [Port Reference](#port-reference)

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│                        FISHMMO CONNECTION STACK                       │
│                                                                      │
│  ┌─────────┐    ┌─────────┐    ┌──────────┐    ┌──────────────────┐ │
│  │ Client  │◄──►│  NGINX  │◄──►│  Game    │◄──►│   PostgreSQL     │ │
│  │ (Unity) │    │ (L4/L7) │    │ Servers  │    │   + pgBouncer    │ │
│  └─────────┘    └─────────┘    └──────────┘    └──────────────────┘ │
│       │              │               │                    │          │
│       │   WebTransport (QUIC/HTTP3)  │                    │          │
│       │   UDP via NGINX stream proxy │        EF Core + Npgsql      │
│       │                              │                               │
│       │   HTTPS/WSS for WebGL        │                               │
│       └──────────────────────────────┘                               │
└──────────────────────────────────────────────────────────────────────┘
```

**Key design decisions:**

| Decision | Rationale |
|----------|-----------|
| **WebTransport (QUIC) for all platforms** | Unified transport — no TCP fallback, no WebSocket shim |
| **NGINX L4 UDP stream proxy** | Zero-copy packet forwarding; no TLS termination at proxy |
| **Each game server terminates its own TLS** | End-to-end encrypted; NGINX never sees plaintext game data |
| **One-time connection token for IP recovery** | Bridges real client IP from HTTP layer into QUIC layer |
| **SRP-6a + X25519 ECDH** | Zero-knowledge password proof; forward secrecy for session keys |
| **HMAC-signed auth tokens** | Stateless World/Scene server auth; no LoginServer dependency after login |

---

## Infrastructure Topology

```
                          INTERNET
                             │
                    ┌────────┴────────┐
                    │   NGINX :80/443  │
                    │  (Reverse Proxy) │
                    └───┬────┬────┬───┘
                        │    │    │
          ┌─────────────┘    │    └─────────────┐
          │                  │                  │
     UDP :7770-7999    TCP :443/*         TCP :443/*
     (stream block)    api.fishmmo.com    play.fishmmo.com
          │                  │                  │
    ┌─────┴─────┐    ┌──────┴──────┐    ┌──────┴──────┐
    │Game Servers│    │  IPFetch    │    │  WebGL      │
    │Login :7770 │    │  :8080      │    │  Server     │
    │World :7780 │    │  Patcher    │    │  :8000      │
    │Scene :7790+│    │  :8090      │    │             │
    └─────┬─────┘    └──────┬──────┘    └─────────────┘
          │                 │
    ┌─────┴─────┐    ┌──────┴──────┐
    │ pgBouncer │    │ PostgreSQL  │
    │ :6432     │◄──►│ :5432       │
    └───────────┘    └─────────────┘
```

---

## Connection Flow Diagram

### Phase 1: Launcher Startup & Version Check

```mermaid
sequenceDiagram
    actor Player
    participant Launcher as ClientLauncher
    participant API as api.fishmmo.com, (NGINX → Patcher)
    participant CMS as CMS Server

    Player->>Launcher: Launch game
    Launcher->>Launcher: Awake(), • Init services, • Set screen resolution, • Build updater path

    par Fetch News & Version
        Launcher->>CMS: GET /news (HTML)
        CMS-->>Launcher: HTML content
        Launcher->>Launcher: Parse div.content, → HtmlText
    and Happy-Eyeballs Version Check
        Launcher->>API: GET /latest_version, X-FishMMO-Client: v1.{ts}.{nonce}.{sig}
        API->>API: ClientGate validation, • HMAC-SHA256 verify, • Timestamp ±300s, • Nonce replay check
        API-->>Launcher: { version, upToDate, patch: { sha256, size } }
    end

    alt Client < Server (outdated)
        Launcher->>Launcher: Show "Update" button
        Player->>Launcher: Click Update
        Launcher->>API: GET /{version} (patch ZIP)
        API-->>Launcher: patch-{version}.zip
        Launcher->>Launcher: SHA-256 verify, Extract to temp
        Launcher->>Launcher: Launch Updater.exe, • Transactional patch apply, • Atomic file replacement
        Updater-->>Launcher: Exit code 0 (success)
    else Client == Server
        Launcher->>Launcher: Show "Play" button
    else Client > Server
        Launcher->>Launcher: Show "Client Ahead", (allow play anyway)
    end
```

### Phase 2: Login Server Discovery (IPFetch)

```mermaid
sequenceDiagram
    participant Client as Client.cs, GetLoginServerList()
    participant API as api.fishmmo.com, (NGINX → IPFetch :8080)
    participant DB as PostgreSQL
    participant Cache as Client Cache, (55s TTL)

    Note over Client: Triggered after "Play" click, or from login screen

    alt Cache valid (< 55s old)
        Client->>Cache: Return cached ports + token
        Cache-->>Client: List<ushort> + connectionToken
    else Cache expired / empty
        Client->>Client: ApiHostResolver.GetCandidates(), • Parse comma-separated hosts, • Random shuffle (Happy Eyeballs)

        loop Staggered probes (0.25s apart)
            Client->>API: GET /loginserver, X-FishMMO-Client: v1.{ts}.{nonce}.{sig}, CertificateHandler: ClientSSLCertificateHandler
            API->>API: ClientGate HMAC verify
            API->>DB: SELECT active login servers
            DB-->>API: Server rows
            API->>API: Generate one-time connection token, • SHA-256(random bytes), • Store in DB with TTL (60s)
            API-->>Client: { ports: [7770], connectionToken: "abc123..." }
        end

        Client->>Client: Cache result (55s TTL)
        Client->>Client: Pick random port, → ConnectToServer(7770)
    end
```

### Phase 3: QUIC/WebTransport Connection

```mermaid
sequenceDiagram
    participant Client as Unity Client, WebTransport + Multipass
    participant NGINX as NGINX L4 Stream, UDP :7770
    participant Server as LoginServer :7770, MsQuic C++ Server

    Note over Client: ClientConnectionManager, ConnectToServer("game.fishmmo.com", 7770)

    Client->>Client: StopConnection() (if existing), Wait for Stopped state

    Client->>Client: StartConnection("game.fishmmo.com", 7770)
    Client->>Client: DNS resolve game.fishmmo.com, → Server IP

    Client->>NGINX: QUIC Initial packet, UDP → game.fishmmo.com:7770
    Note over NGINX: NGINX L4 stream block — listens UDP 7770 — proxies to 127.0.0.1:7770

    NGINX->>Server: Forward UDP to loopback
    Note over NGINX,Server: Source IP = 127.0.0.1, (proxy rewrites address)

    Server->>Server: MsQuic QUIC_CONNECTION_EVENT_CONNECTED, • TLS 1.3 handshake, • ALPN negotiation
    Server->>Server: QUIC_CONNECTION_EVENT_STREAM_STARTED, → on_stream_data callback

    Client-->>Server: QUIC connection established
    Note over Client,Server: Encrypted QUIC tunnel active, TLS terminated at game server, (NGINX never sees plaintext)
```

### Phase 4: Cryptographic Handshake (X25519 ECDH)

```mermaid
sequenceDiagram
    participant Client as ClientAuthenticatorCore
    participant Server as BaseAuthenticatorCore, (Login Server)

    Note over Client,Server: Triggered by OnConnected()

    Client->>Client: Generate X25519 ephemeral keypair, • crypto_box_keypair()

    Client->>Server: ClientHandshake {,   PublicKey: client_x25519_pub,,   Cookie: null,,   ConnectionToken: "abc123...",,   MinVersion: 1, MaxVersion: 1, }

    Server->>Server: Validate:, • PublicKey.Length == 32 ✓, • Not already authenticated ✓, • Validate small-order points ✓

    Server->>Server: Phase 1: Cookie Challenge, • Generate HMAC-SHA256 cookie,   bound to IP + pubkey + time bucket, • Cookie is STATELESS (no server-side storage)

    Server-->>Client: ServerHandshake {,   PublicKey: null,,   Cookie: hmac_cookie_bytes, }

    Client->>Client: Echo cookie, • Guard: cookieEchoed = true, (duplicate prevention)

    Client->>Server: ClientHandshake {,   PublicKey: client_x25519_pub,,   Cookie: hmac_cookie_bytes,,   ConnectionToken: null, }

    Server->>Server: Phase 2: Cookie Verification, • HMAC-SHA256 verify with rollover,   (current + previous time bucket), • Per-IP rate limit check (250ms debounce), • Global rate limit check (500/sec)

    Server->>Server: X25519 ECDH Key Agreement, • Generate server ephemeral keypair, • Compute shared secret, • HKDF derive session keys:,   - ClientToServerKey (AES-256),   - ServerToClientKey (AES-256),   - ClientNoncePrefix,   - ServerNoncePrefix

    Server->>Server: TrackAuthStart(conn), • TTL = 15s (stale sweep), • Hard deadline = 60s

    Server-->>Client: ServerHandshake {,   PublicKey: server_x25519_pub,,   AgreedVersion: 1, }

    Client->>Client: ECDH Key Agreement, • Compute shared secret from server pubkey, • HKDF derive same session keys, • Initialize GcmNonceContext (send + receive), • Dispose ephemeral keypair

    Note over Client,Server: 🔐 AES-256-GCM encrypted channel established, All subsequent messages are encrypted with AAD
```

### Phase 5: SRP-6a Authentication

```mermaid
sequenceDiagram
    participant Client as ClientAuthenticatorCore
    participant Server as SrpAuthenticatorCore, (Login Server)
    participant DB as PostgreSQL
    participant Worker as SRP Worker, (Async Channel)

    Note over Client,Server: After ECDH handshake complete

    alt Has stored auth token (reconnect)
        Client->>Server: TokenAuthBroadcast {,   Token: aes_gcm_encrypted_token, }
        Note over Server: → TokenAuth path (see Phase 8)
    else Fresh login
        Client->>Client: Generate SRP client ephemeral 'A', Encrypt username + 'A' under AES-GCM

        Client->>Server: SrpVerifyBroadcast {,   S: enc(username),,   PublicEphemeral: enc(client_ephemeral_A), }

        Server->>Server: Validate:, • Connection has encryption data ✓, • Not already in auth state ✓, • Duplicate SRP verify guard ✓

        Server->>Server: Decrypt username + client ephemeral
        Server->>Server: Per-account rate limit check, Resolve canonical username

        Server->>Worker: Enqueue SrpVerifyRequest, → bounded async channel

        Worker->>DB: FetchForLoginAsync(username)
        DB-->>Worker: { salt, verifier, accessLevel,,   totpEnabled, verified }

        Worker->>Worker: SRP-6a server:, • Compute server ephemeral 'B', • Compute shared session key, • Generate server proof M2

        Worker-->>Server: SrpVerifyResponse {,   enc(salt), enc(server_ephemeral_B), }

        Server-->>Client: SrpVerifyBroadcast {,   S: enc(salt),,   PublicEphemeral: enc(server_ephemeral_B), }

        Client->>Client: Decrypt salt + server ephemeral, Compute client proof M1, Clear username/password from memory

        Client->>Server: SrpProofBroadcast {,   Proof: enc(client_proof_M1), }

        Server->>Server: Decrypt client proof

        Server->>Worker: Enqueue SrpProofRequest

        Worker->>Worker: Verify client proof M1, against server session key

        alt Proof valid
            Worker->>Worker: Generate HMAC-SHA256 auth token, • Bind: username + accessLevel +,   loginServerId + expiration, • Encrypt under session AES-GCM

            Worker->>DB: PersistTokenHash(token_hash)

            Worker-->>Server: SrpProofResponse {,   success, enc(server_proof_M2),,   enc(auth_token), }
        else Proof invalid
            Worker-->>Server: SrpProofResponse { failed }
        end

        Server-->>Client: SrpSuccessBroadcast {,   Proof: enc(server_proof_M2),,   Result: LoginSuccess,,   Token: enc(auth_token), }

        Client->>Client: Verify server proof M2, Decrypt and store auth token, Fire OnAuthResult(LoginSuccess)
    end
```

### Phase 6: TOTP Two-Factor Authentication

```mermaid
sequenceDiagram
    participant Client as Client
    participant Server as ServerAuthenticator
    participant DB as PostgreSQL

    Note over Client,Server: Only when account has TOTP enabled

    Server-->>Client: ClientAuthResultBroadcast {,   Result: TwoFactorRequired, }

    Client->>Client: Show TOTP input field, User enters 6-digit code

    Client->>Server: TwoFactorVerifyBroadcast {,   Code: enc(totp_code), }

    Server->>Server: Decrypt TOTP code

    Server->>DB: FetchForLoginAsync(username)
    DB-->>Server: { totpSecret (encrypted), lastTotpWindow }

    Server->>Server: Decrypt TOTP secret, via TotpMasterKey (AES-256)

    alt Standard TOTP (6 digits)
        Server->>Server: CryptoHelper.VerifyTotpCode(), • ±1 window drift tolerance, • PersistLastTotpWindow on success
    else Recovery Code (XXXXX-XXXXX hex)
        Server->>DB: FetchUnusedRecoveryCodes(username)
        Server->>Server: VerifyRecoveryCode(), • Constant-time HMAC compare, • ConsumeCode on success (single-use)
    end

    alt Code valid
        Server-->>Client: SrpSuccessBroadcast {,   Result: LoginSuccess,,   Token: enc(auth_token), }
    else Code invalid
        Server-->>Client: ClientAuthResultBroadcast {,   Result: TwoFactorFailed, }
        Note over Server: Per-username failure counter, Lockout after 5 failures (60 min)
    end
```

### Phase 7: Token Issuance & Character Select

```mermaid
sequenceDiagram
    participant Client as Client
    participant Login as LoginServer
    participant DB as PostgreSQL

    Note over Client,Login: After successful SRP login

    Login->>DB: PersistTokenHash(token_hash, username,,   loginServerId, expiresAt)
    Note over DB: Token stored as SHA-256 hash, Expiration: configurable (default 10 min)

    Client->>Client: OnAuthResult(LoginSuccess), • Store decrypted token in memory, • CurrentConnectionType = Login

    Client->>Login: RequestServerListBroadcast
    Login->>DB: FetchActiveWorldServers()
    DB-->>Login: World server list
    Login-->>Client: ServerListBroadcast { Servers: [...] }

    Client->>Client: Display character select screen
    Client->>Login: Character list / create / delete / select

    Login->>DB: Character CRUD operations, • Unit of Work transactions, • Ownership verification

    alt Character selected
        Login-->>Client: WorldSceneConnectBroadcast { Port: 7780 }
        Note over Client: Triggers Phase 8
    end
```

### Phase 8: World Server Connection

```mermaid
sequenceDiagram
    participant Client as Client
    participant NGINX as NGINX L4 Stream, UDP :7780
    participant World as WorldServer :7780
    participant DB as PostgreSQL

    Note over Client: ClientConnectionManager, ConnectToServer("game.fishmmo.com", 7780, true)

    Client->>NGINX: QUIC connection → :7780
    NGINX->>World: UDP forward to :7780

    Note over Client,World: Phase 4: X25519 ECDH handshake, (same cookie-challenge flow)

    Client->>World: ClientHandshake { PublicKey, Cookie, ... }

    World->>World: Cookie challenge → ECDH key agreement
    World-->>Client: ServerHandshake { PublicKey, AgreedVersion }

    Note over Client,World: AES-256-GCM channel established

    Client->>World: TokenAuthBroadcast {,   Token: enc(stored_auth_token), }

    World->>World: Decrypt token via session key, Parse: username, accessLevel,,   loginServerId, signingKeyId, expiration

    World->>DB: FetchSigningKeyAsync(loginServerId, signingKeyId)
    DB-->>World: { hmacKey (AES-256-GCM wrapped) }

    World->>World: Unwrap signing key via KEK, Verify HMAC-SHA256(token)
    World->>DB: CheckTokenRevocationAsync(tokenHash)

    alt Token valid + not revoked + not expired
        World->>World: WorldServerAuthenticator.TryLoginAsync(), • Per-account debounce (1s), • Server lock check, • Population cap check, • Verify character selection

        World->>World: IssueRenewalTokenCoreAsync(), • Mint fresh token with new expiration, • Persist hash → DB, • Send RenewTokenResponseBroadcast

        World-->>Client: ClientAuthResultBroadcast {,   Result: WorldLoginSuccess, }
        World-->>Client: RenewTokenResponseBroadcast {,   Token: enc(new_token), }

        Client->>Client: TryApplyRenewedToken(), • Decrypt new token, • Replace stored token, • OnAuthResult(WorldLoginSuccess)
    else Token invalid/expired/revoked
        World-->>Client: ClientAuthResultBroadcast {,   Result: TokenInvalid / TokenExpired / TokenRevoked, }
        Client->>Client: ClearAuthToken(), → Must re-login via LoginServer
    end
```

### Phase 9: Scene Server Connection

```mermaid
sequenceDiagram
    participant Client as Client
    participant World as WorldServer
    participant Scene as SceneServer :7790
    participant DB as PostgreSQL

    Note over Client,World: After WorldLoginSuccess

    World->>World: Scene routing logic, • Select scene server, • Load scene via FishNet SceneManager

    World-->>Client: WorldSceneConnectBroadcast { Port: 7790 }

    Client->>Client: ConnectToServer(7790), CurrentConnectionType = Scene

    Note over Client,Scene: Phase 4: X25519 ECDH handshake, (same cookie-challenge flow)

    Client->>Scene: ClientHandshake → Cookie challenge → ECDH

    Client->>Scene: TokenAuthBroadcast {,   Token: enc(auth_token), }

    Scene->>Scene: TokenAuthenticatorCore, • Same verify flow as WorldServer, • SceneServerAuthenticator.TryLoginAsync(),   → SceneLoginSuccess (simple pass-through)

    Scene->>DB: FetchSigningKey + CheckRevocation

    Scene-->>Client: ClientAuthResultBroadcast {,   Result: SceneLoginSuccess, }
    Scene-->>Client: RenewTokenResponseBroadcast { Token }

    Client->>Client: OnAuthResult(SceneLoginSuccess), • CurrentConnectionType = Scene, • DismissLoadingScreen(true), • Fire OnEnterGameWorld

    Client->>Client: Character spawn → gameplay begins

    Note over Client,Scene: 🎮 Gameplay active, • Prediction pipeline running, • Observer LOD system active, • All scene systems operational
```

### Phase 10: Token Renewal & Revocation

```mermaid
sequenceDiagram
    participant Client as Client
    participant World as WorldServer
    participant Login as LoginServer
    participant DB as PostgreSQL

    Note over Client,World: Token renewal happens automatically, after each successful World/Scene auth

    rect rgb(240, 255, 240)
        Note over Client,World: TOKEN RENEWAL (Phase 8/9)
        World->>World: IssueRenewalTokenCoreAsync(), • Fetch current signing key from DB, • GenerateAndEncryptToken(),   - New expiration: now + 10min,   - Same username, accessLevel, loginServerId
        World->>DB: PersistTokenHash(new_token_hash)
        World-->>Client: RenewTokenResponseBroadcast { Token }
        Client->>Client: TryApplyRenewedToken(), • Decrypt + store new token
    end

    rect rgb(255, 240, 240)
        Note over Client,Login: TOKEN REVOCATION (logout)
        Client->>Client: RevokeAndClearAuthToken(), • TryConsumeStoredTokenForRevoke(),   - Defensive copy of raw token bytes,   - ZeroMemory original
        Client->>Login: RevokeTokenBroadcast { Token }, (3 retry attempts)
        Login->>Login: TokenService.HashToken(tokenCopy)
        Login->>DB: RevokeByHashAsync(tokenHash)
        Login->>Login: ZeroMemory(tokenCopy)
        Note over Client: Local token already zeroed, Server revocation is best-effort
    end

    rect rgb(255, 255, 240)
        Note over Client: APPLICATION LIFECYCLE
        Client->>Client: OnApplicationPause(true), → RevokeAndClearAuthToken()
        Client->>Client: OnApplicationQuit(), → RevokeAndClearAuthToken()
    end
```

---

## Protocol Layers

```
┌─────────────────────────────────────────────┐
│         APPLICATION LAYER                    │
│  FishMMO Broadcasts (SRP, Token, Chat, etc.) │
│  Serialized via FishNet BinarySerializer      │
├─────────────────────────────────────────────┤
│         ENCRYPTION LAYER                     │
│  AES-256-GCM (per-message)                   │
│  Authenticated Additional Data:              │
│    • Protocol version                        │
│    • Message type                            │
│    • Sequence number                         │
│  Nonce: 12 bytes (prefix || counter)         │
├─────────────────────────────────────────────┤
│         SESSION LAYER                        │
│  QUIC Streams + Datagrams                    │
│  Stream Manager (1024 concurrent streams)    │
│  Reliable: Bidi stream + FIN                 │
│  Unreliable: Datagram                        │
├─────────────────────────────────────────────┤
│         TRANSPORT LAYER                      │
│  QUIC (RFC 9000) over UDP                    │
│  TLS 1.3 (mandatory for QUIC)                │
│  MsQuic C++ library (P/Invoke from C#)       │
├─────────────────────────────────────────────┤
│         NETWORK LAYER                        │
│  UDP datagrams                               │
│  NGINX L4 stream proxy (optional)            │
└─────────────────────────────────────────────┘
```

---

## Security Properties

| Property | Mechanism | Details |
|----------|-----------|---------|
| **Password never sent to server** | SRP-6a | Client proves knowledge of password without transmitting it |
| **Forward secrecy** | X25519 ECDH | Ephemeral keypairs regenerated each connection |
| **Session encryption** | AES-256-GCM | All messages encrypted with per-session derived keys |
| **Message authentication** | GCM authentication tag | Every message authenticated with AAD binding |
| **Replay protection** | Sequence numbers + GCM nonces | Monotonic counters prevent message replay |
| **Cookie challenge** | HMAC-SHA256 stateless cookie | Proof of IP reachability before expensive ECDH |
| **Small-order point rejection** | RFC 7748 §6.1 blacklist | Prevents ECDH downgrade attacks |
| **Token signing** | HMAC-SHA256 | Tokens cryptographically signed by LoginServer |
| **Token encryption** | AES-256-GCM (KEK-wrapped) | Signing keys stored encrypted in database |
| **Constant-time comparison** | Bitwise XOR loop | Prevents timing side-channel on MAC/cert checks |
| **Memory zeroization** | CryptographicOperations.ZeroMemory | Sensitive keys/credentials zeroed after use |
| **TLS certificate pinning** | SHA-256(SPKI) via BouncyCastle | Prevents MITM on launcher API calls |
| **API request signing** | HMAC-SHA256 + timestamp + nonce | Prevents replay of launcher API requests |
| **TOTP secret encryption** | AES-256 master key | TOTP secrets encrypted at rest in DB |

---

## Rate Limiting & DDoS Protection

```
┌──────────────────────────────────────────────────────────────┐
│                    DEFENSE IN DEPTH                          │
│                                                              │
│  LAYER 1: NGINX (Edge)                                       │
│  ├─ limit_req_zone: 10r/s (API), 2r/s (patch), 30r/s (WebGL)│
│  ├─ limit_conn_zone: 20 conn/IP (WebGL), 10 conn/IP (API)   │
│  ├─ client_max_body_size: 1k (prevents oversized requests)  │
│  └─ TLS 1.2+ only, strict ciphers                           │
│                                                              │
│  LAYER 2: ClientGate (API servers)                           │
│  ├─ HMAC-SHA256 request signing                             │
│  ├─ Timestamp ±300s window                                  │
│  └─ Nonce LRU replay protection                             │
│                                                              │
│  LAYER 3: Game Server                                       │
│  ├─ Global handshake cap: 500/sec                           │
│  ├─ Per-IP handshake debounce: 250ms                        │
│  ├─ Pending auth cap: 10,000                                │
│  ├─ Auth TTL sweep: 15s stale, 60s hard deadline            │
│  ├─ Per-account rate limit: 1s (SRP verify)                 │
│  ├─ Per-IP account creation limit: configurable             │
│  ├─ Global hourly account creation cap                      │
│  ├─ TOTP failure lockout: 5 failures → 60 min               │
│  ├─ Account verification brute-force protection              │
│  ├─ IngressGuard: per-connection, per-operation debounce    │
│  └─ Async worker backpressure + bounded channels            │
└──────────────────────────────────────────────────────────────┘
```

---

## Reconnection Flow

```mermaid
stateDiagram-v2
    [*] --> Connected: Initial connection
    Connected --> Disconnected: Connection lost
    Disconnected --> Reconnecting: CanReconnect? (World/Scene only)
    Disconnected --> LoginScreen: Cannot reconnect (Login)
    Reconnecting --> Connected: Reconnect success
    Reconnecting --> Reconnecting: Attempt failed, (exponential backoff + jitter)
    Reconnecting --> LoginScreen: Max attempts (10) exhausted
    LoginScreen --> [*]: Full re-auth required

    note right of Reconnecting
        Backoff formula:
        delay = base(5s) × 2^attempt × jitter(0.75-1.25)
        Max delay capped at 60s
        Max 10 attempts
    end note
```

---

## Platform Support Matrix

| Feature | Windows x64 | Linux x64 | macOS x64 | WebGL (Browser) |
|---------|-------------|-----------|-----------|-----------------|
| **Native client** | ✅ | ✅ | ✅ | N/A |
| **Server** | ✅ | ✅ | ✅ | N/A |
| **WebTransport (QUIC)** | ✅ (via C++ lib) | ✅ (via C++ lib) | ❌ (binary missing) | ✅ (via browser WebTransport API + server HTTP/3) |
| **TLS certificate pinning** | ✅ | ✅ | ✅ | N/A (browser handles) |
| **API request signing** | ✅ | ✅ | ✅ | ✅ |
| **IL2CPP scripting** | ✅ | ✅ | ✅ | ✅ (WASM) |

> **WebGL note:** The C++ msquic server includes a full HTTP/3 WebTransport handshake implementation
> in `src/http3.cpp`. The server auto-detects browser clients by inspecting the first byte of the
> initial peer stream: `0x00` = HTTP/3 control stream (browser), any other byte = raw QUIC (native).
> Browser WebTransport clients connect via `new WebTransport("https://game.fishmmo.com:{port}")`
> and the server handles SETTINGS exchange, CONNECT validation with Origin CORS checking, and
> WebTransport session establishment transparently. The CSP headers on `play.fishmmo.com` are
> configured with `connect-src ... https://game.fishmmo.com:*` for cross-origin WebTransport.

---

## Port Reference

| Port | Service | Protocol | Exposure |
|------|---------|----------|----------|
| 80 | NGINX HTTP → HTTPS redirect | TCP | Public |
| 443 | NGINX HTTPS + WSS | TCP | Public |
| 5432 | PostgreSQL | TCP | Private (localhost) |
| 6432 | pgBouncer | TCP | Private (localhost) |
| 7770-7779 | LoginServer(s) | UDP (QUIC) | Public via NGINX L4 |
| 7780-7789 | WorldServer(s) | UDP (QUIC) | Public via NGINX L4 |
| 7790-7999 | SceneServer(s) | UDP (QUIC) | Public via NGINX L4 |
| 8000 | WebGL Static Server | TCP (HTTP) | Private (behind NGINX) |
| 8080 | IPFetch Server | TCP (HTTP) | Private (behind NGINX) |
| 8090 | Patcher Server | TCP (HTTP) | Private (behind NGINX) |

---

## Key Constants

| Constant | Value | Location |
|----------|-------|----------|
| `APIHost` | `https://api.fishmmo.com/` | `Constants.cs` |
| `GameHost` | `game.fishmmo.com` | `Constants.cs` |
| `AuthStaleTtlSeconds` | 15s | `BaseAuthenticatorCore.cs` |
| `AuthHardDeadlineSeconds` | 60s | `BaseAuthenticatorCore.cs` |
| `MaxPendingAuthConnections` | 10,000 | `BaseAuthenticatorCore.cs` |
| `HandshakeIpDebounceSeconds` | 0.25s | `BaseAuthenticatorCore.cs` |
| `MaxGlobalHandshakesPerSecond` | 500 | `BaseAuthenticatorCore.cs` |
| `TokenExpirationMinutes` | 10 (configurable) | `ServerAuthenticator.cs` |
| `LoginServerCacheTtlSeconds` | 55s | `Client.cs` |
| `MaxReconnectAttempts` | 10 | `ClientConnectionManager.cs` |
| `MaxReconnectDelay` | 60s | `ClientConnectionManager.cs` |
| `WT_MAX_STREAMS` | 1024 | `webtransport_internal.h` |
| `WT_MAX_CLIENTS` | 4000 (configurable) | `.cfg` files |

---

*End of FishMMO Connection Pipeline documentation.*