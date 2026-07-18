# FishMMO — Complete Feature List

> Generated 2026-06-26 from the FishMMO-Dev monorepo. Updated 2026-07-17.  
> Built on Unity 6.3 LTS, FishNet, PostgreSQL, .NET 8.0, WebTransport (QUIC/HTTP3).

---

## Index

- [FishMMO-AppHealthMonitor](#fishmmo-apphealthmonitor)
- [FishMMO-Art](#fishmmo-art)
- [FishMMO-Auth](#fishmmo-auth)
- [FishMMO-CMS](#fishmmo-cms)
- [FishMMO-Database](#fishmmo-database)
- [FishMMO-Dependencies](#fishmmo-dependencies)
- [FishMMO-DiscordBot](#fishmmo-discordbot)
- [FishMMO-Installer](#fishmmo-installer)
- [FishMMO-Logger](#fishmmo-logger)
- [FishMMO-Patcher](#fishmmo-patcher)
- [FishMMO-Setup](#fishmmo-setup)
- [FishMMO-SharedUtility](#fishmmo-sharedutility)
- [FishMMO-Unity — Client](#fishmmo-unity--client)
- [FishMMO-Unity — Server](#fishmmo-unity--server)
- [FishMMO-Unity — Shared](#fishmmo-unity--shared)
- [FishMMO-WebServers](#fishmmo-webservers)
- [FishMMO-WebTransport](#fishmmo-webtransport)

---

## FishMMO-AppHealthMonitor

**Process supervisor daemon** that launches, monitors, and auto-restarts FishMMO server executables.

1. **Process Liveness Monitoring** — Verifies child processes are alive each check interval.  
2. **TCP Port Health Check** — TCP connect probe to confirm the monitored port is accepting connections.  
3. **UDP Port Health Check** — UDP send/receive probe to verify datagram delivery.  
4. **WebSocket Health Check** — Full WebSocket upgrade handshake probe (for Bayou/WebGL transports).  
5. **CPU Threshold Monitoring** — Samples per-process CPU% and triggers restart on sustained breach.  
6. **Memory Threshold Monitoring** — Samples per-process memory usage and triggers restart on sustained breach.  
7. **Exponential Backoff Restarts** — Failed processes restart with increasing delay (configurable initial → max, capped retries).  
8. **Circuit Breaker** — After N consecutive failures across launches, parks the application until manual intervention.  
9. **Graceful Shutdown** — Sends close signal to child process; force-kills if it doesn't exit within timeout.  
10. **Interactive Console Commands** — `start`, `stop`, `status`, `force-restart`, `force-kill`, `shutdown`, `help`.  
11. **Headless Mode** — Disables interactive console; starts monitoring immediately on launch (for systemd/Docker).  
12. **Per-App Config Validation** — Validates all settings at startup, rejects with precise error messages.  
13. **Launch Delay Sequencing** — Configurable per-app delay before launching the next application in sequence.  
14. **Post-Launch Settle Delay** — Pause after launch/restart before resuming probes (lets the process fully boot).  
15. **systemd Integration** — Ships with a reference systemd unit file for Linux service deployment.

---

## FishMMO-Art

**Art and visual asset repository.** Contains game art assets (models, textures, materials, animations, UI graphics) consumed by the Unity project. No code or configuration — purely creative assets.

---

## FishMMO-Auth

**Transport-agnostic .NET authentication library** providing SRP-6a login, token auth, TOTP 2FA, and engine-independent authenticator cores.

### Core / Protocol Contracts
1. **Bounded Concurrent Collections** — `ArrivalOrderTracker<T>` (O(1) insertion-order TTL), `ExpiringKeyTracker<T>` (debounce / rate-limit), `LastSeenCacheTracker<TKey,TValue>` (LRU-style last-seen cache).  
2. **Authentication DTOs** — Engine-independent structs for all auth broadcast payloads.  
3. **Auth Enums** — `AccessLevel`, `AuthState`, `ClientAuthenticationResult`.  
4. **Account Manager Interfaces** — `IAccountManager<T>`, `ISrpAccountManager<T>`, `ITokenAccountManager<T>` for auth-state storage and sweep.

### Implementation / Authenticator Cores
5. **BaseAuthenticatorCore\<TConnection\>** — Abstract server base: X25519 ECDH cookie challenge handshake pipeline, stale-auth TTL sweeps, per-IP and global handshake rate limiting, connection auth-state tracking.  
6. **SrpAuthenticatorCore\<TConnection\>** — LoginServer authenticator: bounded-channel SRP verify/proof workers, TOTP 2FA with per-username lockout, kick-request debouncing, per-IP/per-account rate limiting, auth token issuance.  
7. **TokenAuthenticatorCore\<TConnection\>** — World/Scene server authenticator: bounded-channel token auth worker, decrypt + verify + revocation-check pipeline, timing-equalization dummy-key path.  
8. **ClientAuthenticatorCore** — Full client-side auth state machine: SRP-6a + X25519 ECDH flow, cookie challenge echo, token auth path, key material cleanup.

### Cryptographic Services
9. **HandshakeService** — X25519 ECDH key agreement, stateless HMAC cookie challenge/verification with rollover, protocol version negotiation, IP normalization, key confirmation MACs, transcript hash binding with crypto-suite ID.  
10. **SrpService** — Encrypted SRP field handling with separated `SrpVerifyRequestBroadcast` (client→server) / `SrpVerifyResponseBroadcast` (server→client) types. Registration encryption, TOTP payload encryption/decryption, deterministic fake-salt derivation (HMAC-SHA512) with startup-time charset/length validation.  
11. **TokenService** — Full token pipeline: build → hash → encrypt → decrypt → partial-parse → verify with cross-check against pre-HMAC parsed IDs.  
12. **CryptoHelper** — Cryptographic backbone: HKDF, AES-GCM, HMAC-SHA256/SHA512, thread-safe `GcmNonceContext` (shared across async workers via `Interlocked`), X25519 ephemeral keypairs with small-order-point rejection, TOTP generation/validation with reflection-based OtpNet secret zeroization, recovery code PBKDF2 hashing (600K iterations, v2 envelope).

### Security Features
13. **SRP-6a Authentication** — Secure Remote Password protocol with encrypted verify/proof payloads and strict sequence ordering.  
14. **Fake SRP Data Path** — Deterministic per-username fake salt to reduce account-enumeration timing signal.  
15. **TOTP Two-Factor Authentication** — Per-username failure counting + lockout, semaphore-limited concurrency, recovery code hashing.  
16. **Signed Auth Tokens** — HMAC-SHA256 envelope with access level and expiration baked into verify flow.  
17. **AES-GCM with AAD** — All encrypted payloads bound to message type/version/sequence.  
18. **Constant-Time Comparisons** — All MAC/token checks use constant-time comparison.  
19. **Secret Zeroization** — Sensitive byte arrays cleared via `CryptographicOperations.ZeroMemory`.  
20. **Per-IP Debounce** — `ExpiringKeyTracker` at the handshake layer prevents cookie-spam attacks.  
21. **Global Handshake Rate Cap** — Hard per-second limit across all connections.  
22. **Token Revocation** — Token hashes stored for revocation lookup; revocation check built into verify flow.

---

## FishMMO-CMS

**ASP.NET Core Content Management System** for game news, announcements, and launcher content.

1. **AccountController** — Handles CMS user authentication and account management.  
2. **AdminController** — Admin-level content management endpoints (news posts, announcements, launcher HTML content).  
3. **JSON/HTML Content Delivery** — Serves structured content consumed by the client launcher's news feed and HTML display.  
4. **appsettings.json Configuration** — Database and service configuration for the CMS backend.

---

## FishMMO-Database

**Data-access library** shared by all servers (Login/World/Scene), web services, and Unity builds. Centralizes EF Core DbContext, per-domain services, and monitoring.

### Core Database Infrastructure
1. **IDatabase / Database** — High-level orchestrator wrapping NpgsqlDbContextFactory + service registry. Consumed by all servers.  
2. **IDatabaseServiceRegistry** — Per-domain service lookup (`TryGet<TService>(out var svc)`).  
3. **NpgsqlDbContext** — EF Core DbContext with Npgsql PostgreSQL provider.  
4. **NpgsqlDbContextFactory** — Factory with connection interceptors driving ConnectionPoolMetrics + QueryPerformanceTracker.  
5. **NpgsqlDbConfiguration** — Builds connection string from `IConfiguration` (`Npgsql:*` or `ConnectionStrings:NpgsqlConnection`).  
6. **NpgsqlServiceRegistry** — Wires all per-domain service implementations.  
7. **AppSettings** — Strongly-typed `appsettings.json` binder (Npgsql, QueryPerformanceTracking, Logging). **DatabaseConfigurationHelper** — Convenience helpers for IConfiguration builders.  
9. **DatabaseResult\<T\>** — Uniform result envelope (`IsSuccess`, `ErrorCode`, `ErrorMessage`, `Data`).  
10. **DatabaseErrorCodes** — Stable error code enum returned via DatabaseResult.  
11. **Layered Configuration** — `appsettings.json` → `appsettings.{Environment}.json` → environment variables (with `__` nesting).  
12. **FISHMMO_ENVIRONMENT** — Precedence-based environment selection (FISHMMO_ENVIRONMENT > DOTNET_ENVIRONMENT > ASPNETCORE_ENVIRONMENT).

### Database Services (Npgsql/Services/)
13. **IAccountService** — Account CRUD: create, fetch for login (SRP data), online status check, kick request persist, token hash persist, TOTP verify.  
14. **ICharacterService** — Character CRUD: save, load, delete, fetch by account, session claim/release, inventory/equipment/bank/hotkey persist.  
15. **IChatService** — Chat message persistence and retrieval with channel, character, and server metadata.  
16. **ILoginServerService** — Login server registration, heartbeat pulses, signing key storage (AEAD-wrapped via deployment KEK).  
17. **IWorldServerService** — World server registration, heartbeat pulses, server listing.  
18. **ISceneServerService** — Scene server registration, heartbeat pulses, pending scene queue, channel listing.  
19. **ICharacterInventoryService** — Inventory/equipment/bank slot persistence.  
20. **IGuildService** — Guild creation, membership, ranks, invitation persistence.  
21. **IPartyService** — Party creation, membership persistence.  
22. **IFriendService** — Friend list add/remove/query persistence.  
23. **IKickRequestService** — Kick request queue polling and processing.  
24. **UnitOfWorkService** — Ambient DbContext + transaction scope for multi-step atomic operations. Supports savepoints for nested atomicity inside a unit of work.  
25. **BaseService Execution Wrappers** — `ExecuteReadAsync`, `ExecuteWriteAsync`, `ExecuteTransactionAsync` with retry logic, transient error classification (PostgreSQL error code mapping), and automatic SaveChanges.  
26. **Convention Guards** — `ApplyTimeCreatedConventions` skips entities with explicit defaults (prevents silent override of `QuestEntity`'s `DateTime.UnixEpoch`). `ApplyLogicalVersionConventions` checks for existing defaults before applying.  
27. **Npgsql Type Mapping** — `List<int>` properties natively map to PostgreSQL `integer[]` columns; `HasDefaultValueSql("'{}'")` for empty array defaults.

### Data Entities
26. **AccountData** — Account credentials (SRP verifier, salt), email, 2FA state, verification status.  
27. **CharacterData** — Full character sheet: position, race, archetype, attributes, equipped items, hotkeys, achievements, faction standings.  
28. **ChatData** — Chat message with channel, content, character, server metadata.  
29. **LoginServerData / WorldServerData / SceneServerData** — Server registration and heartbeat entities.  
30. **AuthTokenData** — Token hash with expiration for revocation lookup.  
31. **LoginServerSigningKeyData** — AEAD-wrapped HMAC signing key per login server.  
32. **KickRequestData** — Admin-initiated kick request queue.  
33. **SceneData** — Pending scene load/unload requests.  
34. **QuestData** — Quest state persistence.  
35. **TwoFactorRecoveryCodeData** — Hashed 2FA recovery codes.  
36. **IVersioned / VersionExtensions** — Optimistic concurrency versioning on all entities.

### Monitoring Infrastructure (Npgsql/Monitoring/)
37. **DatabaseHealthMonitor** — `SELECT 1` connectivity probe with Healthy/Degraded/Unhealthy classification.  
38. **ConnectionPoolMetrics** — Runtime open connections, pool utilization %, driven by EF Core connection interceptors.  
39. **DatabaseMetricsTracker** — Success/failure/latency aggregates with summary reporting.  
40. **QueryPerformanceTracker** — Per-operation query performance with P95/P99 percentiles, slow query detection events, configurable tracking levels (None/Basic/Standard/Detailed/Full).

### Unity Integration
41. **DatabaseHealthService** — Unity MonoBehaviour wrapping the monitoring stack. Inspector-configurable health/pool/metrics check intervals. Exposes events for external alerting (Slack/PagerDuty). Context menu commands for manual health checks.

### Exceptions
42. **DatabaseException** — Typed database exception hierarchy: `DatabaseEntityNotFoundException`, `StaleStateException`, `DuplicateReplayException`.

### Database Migrator
43. **FishMMO-DB-Migrator** — Standalone console tool for creating and applying EF Core migrations.

---

## FishMMO-Dependencies

**Centralised NuGet dependency library** — single source of truth for third-party package versions across the entire solution.

1. **EF Core Stack** — EF Core, Abstractions, Relational, Design, Tools, EFCore.NamingConventions (snake_case).  
2. **Microsoft.Extensions Stack** — Configuration (Json, Abstractions), DependencyInjection, Logging, Caching, Options, Primitives, Bcl.AsyncInterfaces.  
3. **Utility Libraries** — SRP (SRP-6a), HtmlAgilityPack, Humanizer, OpenAI, System.Collections.Immutable, ComponentModel.Annotations, DiagnosticSource, IO.Hashing (xxHash/Crc32/Crc64), Text.Json, Threading.Channels.  
4. **Post-Build DLL Copy** — Output DLLs automatically copied to `../FishMMO-Unity/Assets/Dependencies/` via `CopyDependenciesToUnity` MSBuild target (cross-platform forward-slash paths). System DLLs excluded from copy to avoid Unity conflicts.

---

## FishMMO-DiscordBot

**Standalone .NET 8 Discord bot** that bridges in-game chat with a Discord guild.

1. **Game → Discord Chat Relay** — Polls FishMMO chat REST API and forwards game messages to configured Discord channels.  
2. **Discord → Game Chat Relay** — Discord messages intercepted and pushed back to the game via `GameChatBridgeService`.  
3. **Account Linking** — `/link` workflow: issues short-lived one-time codes redeemable in-game to link Discord ↔ FishMMO account.  
4. **Dynamic Channel Management** — Creates/archives Discord channels in response to in-game events (party formed, guild created).  
5. **Moderation Commands** — Mute, unmute, ban, unban for the chat bridge (uses `BridgeBanService`).  
6. **Admin Commands** — Reload config, shutdown, diagnostics (owner/admin-only).  
7. **Character Lookup** — Query character info by name or Discord-linked account.  
8. **Slash Command Support** — Modern Discord slash commands alongside legacy text-command support.  
9. **Rate Limiting** — Per-user/per-channel sliding-window rate limiter to prevent spam from either side (`RateLimiterService`).  
10. **Bridge Ban System** — Tracks Discord users banned from the bridge; consulted before forwarding (`BridgeBanService`).  
11. **Config File Watching** — `BotConfigurationService` watches `appsettings.json` for changes and propagates config at runtime.  
12. **Generic Host + DI** — Built on `Microsoft.Extensions.Hosting`; all services are `IHostedService` with full DI composition.  
13. **Database Read-Only Queries** — Admin-gated database queries via `DatabaseModule`.  
14. **Self-Documenting Help** — `!help` / `/help` command lists all available commands.

---

## FishMMO-Installer

**Cross-platform .NET 8 console tool** that automates the entire dependency and database installation pipeline. Supports interactive menu mode and CLI-driven non-interactive mode for headless/automated deployment.

### Installation Targets
1. **Install DotNet EF Tool** — Installs the `dotnet-ef` global tool for Entity Framework Core migrations.  
2. **Install ASP.NET Core Runtime** — Installs the ASP.NET Core 8.0 runtime via package manager (Linux) or Hosting Bundle EXE (Windows). Dynamic URL resolution from .NET release metadata with hardcoded fallback.  
3. **Install Visual Studio Build Tools** — Windows-only C++ build tools for Unity IL2CPP compilation.  
4. **Install PostgreSQL** — Platform-native PostgreSQL installation (pacman, apt-get, dnf, yum, EnterpriseDB EXE).  
5. **Install PgBouncer** — PostgreSQL connection pooler installation and configuration (Linux systemd, Windows winget/choco).  
6. **Install FishMMO Database** — Creates PostgreSQL user, database, applies initial EF Core migration, grants permissions.  
7. **Create New Database Migration** — Generates and applies new EF Core migrations interactively.  
8. **Grant User Permissions** — Grants schema privileges to the FishMMO database user.  
9. **Delete FishMMO Database** — Destructive database teardown with typed confirmation (requires "DELETE").  
10. **Install NGINX** — Reverse proxy/SSL terminator installation and service registration (Linux systemd, Windows NSSM service).  
11. **Deploy FishMMO nginx.conf** — Atomically deploys the canonical nginx.conf with backup preservation and `nginx -t` validation.  
12. **Install/Renew Let's Encrypt Certificate** — SSL certificate provisioning via certbot (Linux) or win-acme (Windows), with staging mode support and automatic nginx.conf certificate path updates.

### Interactive Menu
13. **Full Interactive Menu** — Hierarchical menu system with numbered options, sub-menus per component group, and confirmation prompts.

### CLI / Non-Interactive Mode
14. **CLI Argument Parser** — `--help`, `--version`, `--component <name>`, `--non-interactive`, `--dry-run`, `--validate`, `--config <path>`. Zero-arg invocation enters interactive menu (backward compatible).  
15. **Unattended Installation** — `--non-interactive -f install-config.json` runs a full dependency-ordered installation from a JSON manifest with no user prompts.  
16. **Single-Component Mode** — `--component postgresql` jumps directly to one component without navigating menus.  
17. **Dry-Run Mode** — `--dry-run` simulates installation and prints what would happen without making changes.  
18. **Quickstart Template** — `--quickstart` shortcut for a recommended default installation profile.

### Pre-Flight Checks
19. **Internet Connectivity Check** — Probes dot.net in 10s before any download-dependent operation.  
20. **Disk Space Check** — Warns if less than 5 GB free on the target drive (Unity Editor + builds can consume 20+ GB).  
21. **Memory Check** — Reads `/proc/meminfo` on Linux, warns if less than 2 GB RAM.  
22. **Admin/Sudo Access Check** — Verifies passwordless sudo (Linux) or Administrator integrity level (Windows) before system-level installs.  
23. **Port Conflict Detection** — Checks ports 80, 443, 5432, 6432, 8000, 8080, 8090 for existing listeners before installing services.

### Download Integrity & Progress
24. **SHA256 Checksum Verification** — Every downloaded file verified against `checksums.json`; corrupt/tampered files rejected. Already-downloaded files with valid checksums skip re-download.  
25. **Download Progress Bar** — Console progress indicator with percentage and visual bar during large downloads.  
26. **Dynamic .NET URL Resolution** — Resolves the latest .NET SDK and ASP.NET runtime installer URLs from the .NET release metadata API; hardcoded constants as fallback.

### Post-Install Validation
27. **Health Check Mode** — `--validate` runs checks against .NET SDK, ASP.NET runtime, PostgreSQL, NGINX, PgBouncer, systemd services, database connectivity, and disk space; prints a pass/fail report.

### New Infrastructure Components
28. **Firewall Automation** — Opens ports 80/tcp and 443/tcp via ufw or firewalld (Linux) or netsh (Windows). Menu option or `--component firewall`.  
29. **Systemd Service Generation** — Generates and registers systemd units for FishMMO ASP.NET web servers (fishmmo-ipfetch, fishmmo-patcher, fishmmo-webgl). Finds publish directories, generates `.service` files, runs `systemctl enable --now`. Menu option or `--component systemd-services`.  
30. **Dependency-Graph Orchestrator** — Topological component ordering so dotnet-sdk installs before postgresql, postgresql before fishmmo-db, etc. Used by both non-interactive pipeline and single-component dispatch.

### Security & Hardening
31. **Linux Config Hardening** — Secure file permissions (`chmod 600`), core dump disabling, ptrace hardening for production Linux deployments.  
32. **PostgreSQL Hardening** — Rewrites `pg_hba.conf` to require `scram-sha-256` on all TCP connections, sets `password_encryption` and `listen_addresses` in `postgresql.conf`, reloads via `pg_reload_conf()`. Idempotent via managed markers.  
33. **PgBouncer Configuration Generation** — Generates `pgbouncer.ini` (transaction pooling, scram-sha-256) and `userlist.txt` (with SCRAM hash from `pg_shadow`) with secure file permissions.  
34. **Secrets Environment Files** — Generates `fishmmo-secrets.env` (systemd/docker `EnvironmentFile`) and `~/.config/fish/conf.d/fishmmo-secrets.fish` (fish shell snippet) so passwords never live in plain-text JSON.  
35. **AppSettings Secure Wizard** — Interactive configuration wizard for all FishMMO components (Database, IPFetch, Patcher, WebGL, Discord Bot, CMS). Preserves unmanaged JSON keys across writes. Applies `chmod 600` on all output files.

### Build Automation
36. **Build All C# Projects** — Discovers and builds all `.csproj` files under the repo root with dependency-prioritized ordering (synchronous for low-priority projects, parallel for independent builds). Copies DLLs to Unity Dependencies.  
37. **Unity Build Automation** — Headless Unity builds via `-batchmode -nographics -executeMethod` for Client/Server/Addressables. Resolves Unity executable path from environment variable, Unity Hub CLI, or filesystem probing.  
38. **Unity Hub + Editor Installation** — Installs Unity Hub (Linux: apt/AUR, Windows: official installer) and Unity Editor versions with selectable build support modules via Unity Hub CLI.

### Platform Support
39. **Cross-Platform** — Windows 10/11 and Linux (Arch/CachyOS, Ubuntu/Debian, Fedora/RHEL).  
40. **Package Manager Auto-Detection** — pacman, apt-get, dnf, and yum auto-detected with appropriate update/install command templates.  
41. **Platform Abstraction** — `IPlatform` interface with `WindowsPlatform` / `LinuxPlatform` implementations for shell command dispatch, privilege elevation, and command availability checks.

---

## FishMMO-Logger

**JSON-driven logging library** used by all headless servers, the Discord bot, AppHealthMonitor, and Unity client builds.

1. **Static Log Facade** — `Log.Info/Warn/Error/Debug/Trace/Critical(category, message)` synchronous-friendly API.  
2. **Typed LogLevel Enum** — Trace < Debug < Info < Warning < Error < Critical with per-sink filtering.  
3. **Structured LogEntry** — Immutable struct: timestamp, level, category, message, optional exception.  
4. **File Sink with Rotation** — Append-or-truncate file logging with byte-size-based rotation (timestamp-suffixed rollover).  
5. **Email Sink via SMTP** — Per-sink minimum level filtering (typically Error/Critical), TLS support.  
6. **JSON Configuration** — Single `logging.json` file with polymorphic `{ Type, Config }` entries.  
7. **Pluggable Sink Model** — `ILogger` + `ILoggerConfig` interfaces; register custom sinks via factory before initialization.  
8. **Polymorphic Config Converter** — `ILoggerConfigConverter` for System.Text.Json round-tripping of sink configs.  
9. **Console Formatter** — ANSI / plain-text console formatting helpers.  
10. **Unity Integration** — Unity console bridge (`UnityLoggerBridge`) and Unity-specific console formatter.  
11. **Async Shutdown** — `Log.Shutdown()` drains and disposes all sinks gracefully.

---

## FishMMO-Patcher

**Standalone .NET 8 updater** that applies versioned binary patches to FishMMO clients.

1. **Versioned Patch Application** — Applies sequential patches (`1.0.0 → 1.0.1 → 1.1.0`) from ZIP archives.  
2. **Patch Manifest Parsing** — Reads `manifest.json` describing new/modified/deleted files with hashes.  
3. **Binary Diff Application** — Applies binary diffs to existing files with pre- and post-hash verification.  
4. **Parallel File Operations** — New and modified files processed concurrently for speed.  
5. **Transactional Patching** — Every file backed up before modification; full rollback on any failure.  
6. **Atomic File Replacement** — Temporary `.new` files atomically moved over originals.  
7. **Launcher Process Management** — Locates launcher by PID, graceful close with force-kill fallback.  
8. **Automatic Client Restart** — Starts the updated client executable after successful patch.  
9. **Multi-Step Version Chain** — Locates and applies all intermediate patch archives in version order.  
10. **Retry with Backoff** — Configurable retry count and delay for transient file I/O errors.

---

## FishMMO-Setup

**Configuration templates and reference files** for deployment environments.

### nginx.conf — Reverse Proxy
1. **UDP Stream Proxy (L4)** — Raw UDP forwarding for game ports 7770–7999 via `stream {}` block. Auto-generated per-port configs via `gen-fishmmo-stream-config.sh` with atomic replacement and `nginx -t` validation. Zero-copy packet forwarding; no TLS termination at proxy.  
2. **HTTP/HTTPS Gateway (L7)** — TLS 1.2/1.3 termination with Let's Encrypt certificates, HSTS (6 months + includeSubDomains), modern cipher suite (ECDHE+AESGCM:ECDHE+CHACHA20), OCSP stapling.  
3. **Virtual Hosts** — `play.fishmmo.com` (WebGL client), `api.fishmmo.com` (IPFetch + Patcher), `game.fishmmo.com` (444-close — game traffic is UDP-only). Catch-all returns 444.  
4. **Rate Limiting** — `limit_req_zone` per-endpoint: 10r/s API, 2r/s patch downloads, 30r/s WebGL. `limit_conn_zone` per-IP: 20 conn WebGL, 10 conn API, 3 conn patch. HTTP 429 with `Retry-After`.  
5. **Security Headers** — CSP (WebGL: `wasm-unsafe-eval`, `connect-src wss://game.fishmmo.com:*`), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy`, `Access-Control-Allow-Origin` for API.  
6. **Performance** — `sendfile on`, `tcp_nopush on`, `tcp_nodelay on`, `gzip on` with `gzip_proxied any` (not `off`), `gzip_types` tuned for text/wasm, `keepalive_timeout 65s`.  
7. **Hardening** — `server_tokens off`, `client_max_body_size 1k` (API), `client_body_timeout 10s`, `client_header_timeout 10s`.  

### Server Configuration (.cfg files)
8. **LoginServer.cfg** — ServerName, MaximumClients (4000), Address (127.0.0.1 via nginx), Port (7770), TLS certificate paths, SMTP config (with `UseSsl` per-environment).  
9. **WorldServer.cfg** — Port 7780, same TLS cert paths.  
10. **SceneServer.cfg** — Port 7790+, StaleSceneTimeout (5s), same TLS cert paths.  
11. **AutoVerifyAccounts** — `true` in Development (local SMTP), `false` in Release (email verification required).  

### Deploy Hooks
12. **certbot-fishmmo.sh** — Post-renewal deploy hook: copies Let's Encrypt certs to `/etc/fishmmo/certs/`, `chmod 640`, `chown fishmmo:fishmmo`, reloads nginx, restarts game servers via systemd (with SIGHUP fallback).  
13. **gen-fishmmo-stream-config.sh** — Regenerates `stream.d/*.conf` for all game UDP ports (230 ports across Login/World/Scene ranges). Validates with `nginx -t` before atomic replacement.  

### Build System
14. **WebTransport Build** — `build_all.sh` master script: Linux (native CMake), Windows (Zig cross-compile), macOS (native CMake).  
15. **Cross-Platform Paths** — Forward-slash paths in `.csproj` files. `$(Configuration)` used directly (no redundant `BuildConfiguration` property).

---

## FishMMO-SharedUtility

**Pure C# / netstandard2.1 utility library** — the lowest layer shared between Unity client and all .NET server projects.

### Top-Level Utilities
1. **Authentication Validators** — Username, password, character name, and email validation rules (shared by LoginServer and account creation). NFKC normalization for case-insensitive comparisons.  
2. **CircularBuffer\<T\>** — Thread-safe circular doubly-linked list with O(1) add/remove/pop/snapshot.  
3. **Configuration** — INI-style `.cfg` file handler with environment variable overrides (`FISHMMO_CONFIG_*`), thread-safe via `ReaderWriterLockSlim`, case-insensitive keys, typed getters.  
4. **FastActivator\<T\>** — Expression-tree compiled object factory (0–16 constructor args, faster than `Activator.CreateInstance`).  
5. **MathHelper** — Mathematical constants: `HalfPI`, `Tau`.  
6. **RefWrapper\<T\>** — Boxed reference wrapper for value types with implicit conversion.  
7. **SetOnce\<T\>** — Thread-safe write-once latch with lock-free reads and double-checked locking.  
8. **IReference** — Marker interface for reference-equality compared objects.

### Compression
10. **StringCompression** — GZip compress/decompress for UTF-8 strings.  
11. **DictionaryCompression** — Compresses string dictionaries using a shared dictionary frame.

### Extensions
12. **ArrayExtensions** — Array manipulation helpers.  
13. **IListExtensions** — Binary search, swap, shuffle.  
14. **StringExtensions** — Case-insensitive contains, hex conversion, truncation.  
15. **TypeExtensions** — Assignable-from cache, type hierarchy utilities.  
16. **RandomExtensions** — Range pickers with deterministic seeding.  
17. **EnumExtensions** — Enum parsing and attribute helpers.  
18. **DirectoryExtensions** — Safe directory copy/cleanup.  
19. **ProcessExtensions** — Process management utilities.  
20. **Primitive Bit Extensions** — Byte, Short, Int, Long, Float bit-twiddling helpers.

---

## FishMMO-Unity — Client

**The player-facing Unity client** (FishMMO.Client assembly, 169 .cs files).

### Networking & Connectivity
1. **Multi-Server Connection Management** — LoginServer → WorldServer → SceneServer transitions with state tracking via `ClientConnectionManager`.  
2. **Reconnection with Exponential Backoff** — Automatic reconnect attempts with configurable backoff (base 5s × 2^attempt × jitter, max 60s, 10 attempts).  
3. **Login-Server Discovery** — Happy-Eyeballs multi-mirror probing via configurable API hosts with staggered probes (0.25s apart), 55s TTL cache, and one-time connection token relay.  
4. **ServerConnectionType State** — Tracks connection state: None, Login, World, Scene.  
5. **Broadcast Sending** — Centralized FishNet broadcast dispatch from the Client MonoBehaviour.  
6. **WebTransport (QUIC/HTTP3) Transport** — All platforms use WebTransport via `Multipass`; NGINX L4 UDP stream proxy forwards raw QUIC to game servers.  
7. **Death Dialog** — `UITKDeathDialog` with Respawn/Resurrect buttons. Handles `ResurrectOfferBroadcast` for dynamic button visibility.

### Authentication
6. **SRP-6a Client Login Flow** — Full SRP-6a protocol: cookie challenge echo, key agreement, verify/proof, token-based reauth.  
7. **Token-Based Reauthentication** — Stored auth tokens for seamless World/Scene server transitions.  
8. **Account Creation** — Encrypted credential registration with validation.  
9. **Account Email Verification** — Verification code submission.  
10. **TOTP / 2FA Support** — Two-factor code submission and 2FA setup (QR code + recovery codes).  
11. **Token Renewal & Revocation** — Token refresh on login server and revocation on logout/shutdown.

### Input System
12. **Unity Input System Integration** — `PlayerInputHandler` manages the `PlayerControls` asset.  
13. **Mouse Mode Management** — Cursor visibility/lock state toggling.  
14. **Input Binding Persistence** — Loading/saving input binding overrides to configuration.  
15. **Character Movement Input** — Move, Look, Jump, Crouch, Sprint mapped to KCC replication data.  
16. **Full Gameplay Bindings** — Interact, Cancel, Chat, Inventory, Equipment, Abilities, Guild, Party, Friends, Achievements, Factions, Minimap, Menu, Toggle First-Person, ScrollWheel.  
17. **Right-Click Context Menus** — Inspect, Add Friend, Invite to Party, Trade on player targets.

### Launcher
18. **HTML News Feed** — Fetches and displays launcher news via HtmlAgilityPack → TextMeshPro rich text.  
19. **API Host Resolution** — Randomised mirror selection from comma-separated host list with HTTPS enforcement.  
20. **Version Checking** — Compares local version against `/latest_version` API endpoint.  
21. **Patch Download** — Downloads patch ZIPs with SHA-256 integrity verification and progress display.  
22. **External Updater Launch** — Spawns the standalone Updater process, monitors exit, reports results.  
23. **API Request Signing** — HMAC-SHA256 request signing with replay protection (timestamp ±300s, nonce).  
24. **UnityWebRequest Service** — Shared MonoBehaviour for HTTP requests with retry, timeout, progress callbacks, custom certificate handling.

### Security
25. **TLS Certificate Pinning** — SHA-256(SPKI) base64 pinning via BouncyCastle for UnityWebRequest. Constant-time pin comparison. Release builds fail-closed when pins are not configured.  
26. **StreamingAssets Pin Configuration** — Pins loaded from `client-security.json` with compile-time defaults.  
27. **TOFU Mode** — Development/editor builds allow empty pins (trust-on-first-use with loud warnings).  
28. **Build-Time Validation** — `IPreprocessBuildWithReport` warns on release builds without TLS pins (at least 2 required).  
29. **Dynamic Pin Update Scaffold** — `IPinUpdateSidecar` interface for out-of-band signed manifest updates with UTC validity windows.  
30. **API Request Signing** — HMAC-SHA256 with `X-FishMMO-Client` header (v1.{ts}.{nonce}.{sig} format), 30s skew window, per-process nonce LRU cache.

### UI Toolkit (UITK) Panels — Login Flow
30. **Loading Screen** — Addressable-loaded transition images with progress bar.  
31. **Reconnect Display** — Reconnect attempt status during network interruptions.  
32. **Login Panel** — Username/password, TOTP/2FA code, account verification code input.  
33. **Register Panel** — Username, password, email, age fields.  
34. **Server Select** — Available game server list.  
35. **Character Select** — Existing character display with create-new option.  
36. **Character Create** — Name input and appearance customization.

### UI Toolkit (UITK) Panels — World / In-Game HUD
37. **Ability Book** — Learned abilities with details.  
38. **Cast Bar** — Channeling/casting progress display.  
39. **Ability Crafting** — Ability-based item crafting UI.  
40. **Achievement Window** — Achievement tracking and completion display.  
41. **Bank / Storage** — Deposit/withdraw item interface.  
42. **Buff Container** — Active buff/debuff icon management.  
43. **Chat Window** — Message history, tabs, channel picker, input.  
44. **Crosshair** — Reticle display for targeting.  
45. **Dungeon Finder** — Instance group finder and queue interface.  
46. **Equipment Window** — Equipped items and character stats.  
47. **Faction Standings** — Reputation display.  
48. **Friend List** — Online/offline status.  
49. **Guild Management** — Members, ranks, info.  
50. **Hotkey Bar** — Action bar with ability slots.  
51. **Inventory / Bag Window** — Item grid display.  
52. **Main Menu** — Settings, logout, quit.  
53. **Merchant Buy/Sell** — NPC vendor interface.  
54. **Minimap** — Surrounding area display.  
55. **NPC Dialogue** — Conversation window.  
56. **Options / Settings** — Audio, video, keybindings.  
57. **Party List** — Group member management.  
58. **Pet Control** — Summon, dismiss, pet abilities.  
59. **Resource Bars** — Health bar, mana bar, stamina bar.  
60. **Target Frame** — Target name, health, buffs/debuffs.

### UGUI (Legacy Canvas) Panels
61. Full UGUI equivalents of all login flow and world HUD panels above (loading screen, login, register, server select, character select/create, options with screen settings, chat, inventory, equipment, bank, merchant, abilities, hotkeys, achievements, buffs/debuffs, guild, party, friends, factions, dungeon finder, NPC dialogue, pet, minimap, inspect, chat channels, context menus, crosshair, tooltips).

### Shared UI Components
62. **Dialog Box** — Modal informational dialog.  
63. **Input Dialog Box** — Modal dialog with text input field.  
64. **Color Picker** — Color selection control.  
65. **Custom Dropdown** — Dropdown control.  
66. **Selector / Grid** — Item picking grid.  
67. **Drag Object** — Draggable UI elements.  
68. **Tooltip** — Item/ability information popup on hover.  
69. **UI Theming Engine** — `CanvasCrawler` crawls Canvas and applies unified theme (colors, layout, scroll, font, transitions) to all supported Unity UI component types.  
70. **UI Manager** — Static registry for all UI controls with Show/Hide/Toggle/GetByName, close-on-escape stack, character injection.

### 3D World-Space Effects
71. **3D Label System** — Object-pooled TextMeshPro labels for damage numbers, heal numbers, achievement popups.
72. **Visual Effects** — 10 configurable effects: FadeIn, FadeOut, FloatUp, FloatRandom, Bounce, Pulse, ScaleUp, ScaleDown, Wave, Shake.  
73. **Billboard Component** — Makes GameObjects always face the camera (nameplates, health bars).  
74. **Cinematic Camera** — Camera movement along Unity Spline paths with LookAt target and user skip.  
75. **Floating Labels** — Damage, heal, achievement, and region name labels in world space.

### Scene Management
76. **Addressable-Based Scene Loading** — Scene preloading/postloading with progress tracking.  
77. **Template Cache Population** — Static permanent addressable loading.  
78. **Fog Transitions** — Scene fog changes during world transitions. `ClientFogManager` extracted for SRP compliance.  
79. **World Scene Tracking / Unloading** — Client-side world scene lifecycle management.  
80. **Postload Scene Lifecycle** — Reloads on quit-to-login, unloads on entering game world.  
81. **Death Broadcast Handler** — `DeathBroadcast` registered on client for reconnect-while-dead death dialog re-display.

### Naming & Resolution
76. **ClientNamingSystem** — ID-to-name and name-to-ID resolution for characters, guilds, pets with server queries and disk persistence (GZip binary).

### Scene Management
77. **Addressable-Based Scene Loading** — Scene preloading/postloading with progress tracking.  
78. **Template Cache Population** — Static permanent addressable loading.  
79. **Fog Transitions** — Scene fog changes during world transitions.  
80. **World Scene Tracking / Unloading** — Client-side world scene lifecycle management.  
81. **Postload Scene Lifecycle** — Reloads on quit-to-login, unloads on entering game world.

### WebGL Support
82. **Browser Key Interception** — Prevents default browser actions (F12, Ctrl+W) during gameplay via JavaScript interop.  
83. **WebGL Quit** — Calls JavaScript quit function.

---

## FishMMO-Unity — Server

**The headless server** (FishMMO.Server assembly, 207 .cs files). Three server types — Login, World, Scene — launched from one GameServer executable.

### Core Server Infrastructure
1. **Server Composition Root** — `Server` MonoBehaviour orchestrates CoreServer, Database, NetworkWrapper, AddressProvider, AccountManager, BehaviourRegistry, DataContainerRegistry.  
2. **Config File Loading** — `FileServerConfiguration` loads/saves `.cfg` files with typed getters and defaults.  
3. **Server Lifecycle Events** — `IServerEvents` with delegates for LoginServer/WorldServer/SceneServer initialization.  
4. **Periodic Callback System** — `IPeriodicUpdateSystem` for registering/unregistering configurable-interval per-frame callbacks.  
5. **Server Behaviour System** — ScriptableObject-derived modular server behaviours with unified InitializeOnce/Deinitialize lifecycle.  
6. **Server Component Registry** — Multi-interface lookup registry for all server components.  
7. **Runtime Data Containers** — Typed runtime data containers with factory and registry; behaviours can declare required containers via `[RequiresDataContainer]`.  
8. **Main Thread Queue** — Thread-safe main-thread action queue for marshalling async worker results to the Unity thread.  
9. **Async Worker Queue** — Centralized bounded async work queue with backpressure and entity-keyed ordering (FIFO per key via consistent hashing).  
10. **IngressGuard** — Per-connection, per-operation debounce and in-flight guard to prevent duplicate/replay/DoS attacks (ConcurrentDictionary-backed, bounded, periodic sweep).  
11. **FishNet Network Wrapper** — Clean abstraction over FishNet NetworkManager: broadcast registration, transport config (bind address/port/maxClients forwarded to each WebTransport child in Multipass), TLS certificate configuration, authenticator attachment, coroutine hosting.  
12. **Server Type Selection** — Server type determined by command-line arg (`LOGIN`, `WORLD`, or `SCENE`).  
13. **Address Resolution** — `ServerAddressProvider` resolves IPv4/IPv6 from transport with optional overrides.  
14. **Physics Ticker** — Unity MonoBehaviour ticking a PhysicsScene at server fixed timestep (per-scene physics).  
15. **Window Title Metrics** — Updates server window title with connection/character counts.  
16. **Server Launcher** — Bootstrap system preloading addressables and loading server scenes based on CLI args.

### Authentication (All Server Types)
17. **BaseServerAuthenticator** — Abstract MonoBehaviour bridging FishNet transport to engine-independent `BaseAuthenticatorCore`. Handles: handshake routing, cookie challenges, rate-limit key resolution, main-thread action queue.  
18. **ServerAuthenticator (SRP)** — LoginServer SRP-6a authenticator: SRP verify/proof, TOTP/recovery code verification, token issuance, kick request processing.  
19. **TokenServerAuthenticator** — World/Scene token authenticator: decrypt + verify + revocation check, one-retry with linear backoff for DB blips.  
20. **Signing Key KEK Provider** — Static utility loading AES-256 KEK from config/env, building 8-byte AAD bound to LoginServer ID, wrapping/unwrapping HMAC signing keys.  
21. **Account Managers** — `AccountManager`, `SrpAccountManager`, `TokenAccountManager` wrapping FishMMO-Auth cores for Unity/FishNet.

### LoginServer Features
22. **Login Server Registration** — Registers server in DB, generates/rotates HMAC signing keys (AEAD-wrapped via KEK), derives TOTP master key. Periodic heartbeat pulses.  
23. **Account Creation System** — Per-IP rate limiting with `ExpiringKeyTracker`, per-IP block after N failures, global hourly account creation cap (DoS shield), per-username verification failure lockout (60 min after 5 failures). AES-256-GCM encrypted credential decryption, mandatory TOTP 2FA setup with encrypted secret storage, recovery code generation/hashing, encrypted otpauth URI delivery.  
24. **Character Create System** — Template-validated character creation with starting equipment/abilities/hotkeys initialization, `MaxCharacters` per account enforcement.  
25. **Character Select System** — Character listing, selection, and deletion for player accounts.  
26. **Server Select System** — World server list provisioning from database.

### WorldServer Features
27. **World Server Registration** — DB registration, periodic heartbeat with character count.  
28. **World Server Authenticator** — Token auth with per-account login debounce, server-lock check, combined admission gate (DB connection count + recently admitted usernames burst prevention), selected-character validation.  
29. **World Scene System** — Open world and instanced scene routing, connection authentication, instance lookup with debounce and TTL caching, waiting queue management with TTL purge, DB updates.  
30. **Kick Request System** — Periodic DB polling for admin-initiated kicks, player disconnection via main-thread marshalling.

### SceneServer Features
31. **Scene Server Registration** — DB registration, periodic heartbeat pulses with scene character counts.  
32. **Scene Loading/Unloading** — FishNet SceneManager orchestration, pending scene queue processing from DB, stale scene cleanup.  
33. **Character System** — Full lifecycle: loading from DB, spawning, periodic saves (configurable interval), despawning, disconnect cleanup. Session ownership with claim/release and lease refresh. Teleportation, out-of-bounds checks, death/respawn.  
34. **Character Inventory System** — Item moves, swaps, splits across inventory, equipment, and bank containers. Persists changes to DB.  
35. **Equipment System** — Equip/unequip with slot validation.  
36. **Bank System** — Bank/storage slot management.  
37. **Chat System** — Local (proximity), World, Party, Guild, and Private (whisper/tell) chat channels. Lock-free incoming queue with O(1) size counter. Batch DB persistence. Token-bucket rate limiting.  
38. **Guild System** — Creation, membership, ranks, invitations (TTL-expiring), periodic update pump, character connect/disconnect tracking.  
39. **Party System** — Creation, membership, invitations (TTL-expiring), character connect/disconnect tracking.  
40. **Friend System** — Add/remove friends with validation, online status tracking, `MaxFriends` enforcement.  
41. **Achievement System** — Progress tracking, completion events, reward delivery.  
42. **Quest System** — Event handling, auto-progression, reward delivery, DB persistence.  
43. **Pet System** — Summoning, following, staying, releasing, AI initialization, death handling.  
44. **Hotkey System** — Player hotkey configuration for abilities/items with ingress debounce protection.  
45. **Naming System** — Character/guild ID ↔ name resolution with bounded TTL caches and negative caching.  
46. **Interactable System** — NPC interaction, merchant purchases (items/abilities), ability crafting, world containers, server-authoritative dialogues (ECA-driven), dungeon finder entrance, mailbox (send/receive/delete mail).  
47. **Faction System** — Faction relationship management.  
48. **Scene Channel System** — Open-world channel listing and same-server channel switching with per-connection cooldown enforcement.

### Server Authority & Security
49. **CharacterStateValidation** — Centralized static validation gate for all broadcast handlers. `CanAct()` rejects dead, teleporting, frozen, and unloaded characters. `CanActOrMove()` additionally rejects in-combat characters. `TryGetPlayerAndValidate(conn, out player)` canonical pattern resolves player from connection and validates in one call. Called at entry of every state-mutating broadcast handler.
50. **Comprehensive CanAct Coverage** — All server-side broadcast handlers validated: CharacterInventory (6 ops), Bank (2 ops), Equipment (2 ops), Quest (3 ops), Guild (7 ops), Party (7 ops), Friend (2 ops), Pet (4 ops), Hotkey (2 ops), Chat, Interactable. Movement pipeline also gated server-side in KCCPlayer.OnReplicate.
51. **Per-Account Rate Limiting** — Auth callback rate limit keyed by account name (not ClientId), preventing multi-connection bypass. Separate per-connection rate limit for scene unload broadcasts.
52. **Respawn/Resurrect IngressGuard** — Per-operation IngressGuard (2s debounce) on respawn-at-bind-point and resurrect-accept handlers. Prevents spam and concurrent-operation races.
53. **TCP/TLS Transport Encryption** — All network traffic encrypted at transport layer.

### Observer LOD System
54. **HashGrid Spatial Partitioning** — FishNet `HashGrid` component on NetworkManager (Accuracy=70, GridAxes=XZ). O(1) hash-based proximity: objects in same or adjacent grid cells are "nearby." Covers ~105m effective radius.
55. **Global Observer Conditions** — `ObserverManager` configured with `SceneCondition` (never observe cross-scene) + `GridCondition` (spatial hash pre-filter). Applied to all `NetworkObject`s with `OverrideType = AddMissing`.
56. **Tiered Distance Conditions** — Four `DistanceCondition` ScriptableObjects (FishMMO → Create Observer Distance Conditions): Player(100m/10% hysteresis), Monster(50m/15%), Interactable(30m/10%), WorldItem(15m/20%). Added per-prefab on `NetworkObserver`.
57. **Bandwidth Reduction** — Per-client observer bandwidth drops from ~256 KB/s to ~43 KB/s (83% reduction) with full observer condition setup. Server aggregate outbound drops from ~25.6 MB/s to ~4.3 MB/s for 100 players + 100 NPCs.

---

## FishMMO-Unity — Shared

**The shared entity and logic layer** (FishMMO.Shared assembly, 560 .cs files). Used by both client and server, containing all entity definitions, the ECA trigger system, templates, network broadcasts, and prediction pipeline.

### Character System
1. **ICharacter / IPlayerCharacter Interfaces** — Root character contracts: ID, name, transform, collider, network object, prediction manager, observers, flags, behaviours, triggers.  
2. **BaseCharacter** — Abstract NetworkBehaviour implementing ICharacter: behaviour registry, bitwise flag management, ECA trigger invocation, race model instantiation (Addressable), client character dictionary.  
3. **PlayerCharacter** — Concrete player class requiring 13+ behaviour components (attribute, target, cooldown, inventory, equipment, bank, ability, achievement, buff, quest, damage, guild, party, friend, faction controllers). KCC movement, chat anti-spam token bucket.  
4. **CharacterBehaviour** — Abstract base for modular behaviour components: InitializeOnce, OnStartCharacter, OnStopCharacter lifecycle.  
5. **CharacterFlags** — Bitwise state flags: Idle, IsMoving, IsRunning, IsCrouching, IsSwimming, IsTeleporting, IsFrozen, IsStunned, IsMesmerized, IsInInstance, IsLoaded, IsDead, IsInCombat.
6. **Combat State System** — Tick-aligned combat timer on `CharacterDamageController`. Enters combat on dealing damage, taking damage, or healing an in-combat ally. Auto-clears after configurable duration (default 600 ticks / 20s at 30Hz) of inactivity. `EnterCombat()` safe to call repeatedly — refreshes expiry. `IsInCombat` flag cleared on death and network reset. Combat state prevents teleportation (combat-escape prevention).
7. **Combat-Escape Prevention** — Teleport blocked while `IsInCombat` flag is active. Movement gate in `KCCPlayer.OnReplicate` rejects input from dead, frozen, teleporting, unloaded, and in-combat characters.

### ECA Trigger System (Entity-Component-Action)
*The data-driven trigger/action pipeline powering abilities, quests, dialogue, interactables, and game events.*

6. **Trigger System Core** — `Trigger` ScriptableObjects with `TargetSelector` + `Conditions` + `OnConditionsMetActions` + `OnConditionsNotMetActions`. Fault isolation (throwing actions caught/logged).  
7. **EventData** — Typed event context container: Initiator, Target, TargetCharacter, RNG, ConditionFilter. Supports typed sub-payloads, forking, merging.  
8. **Polymorphic Serialization** — All actions/conditions/selectors use `[SerializeReference]` + `[SubclassSelector]` for designer-authored Inspector workflows.

#### ECA Actions (~80 implementations)
9. **Combat Actions** — ApplyDamage, ApplyHeal, ApplyBuff, ApplyDispel, ConsumeResource, Interrupt, KnockbackHit.  
10. **Ability Actions** — AbilityApplyArea, AbilityApplyTarget, AbilityForkHit, AbilityHitCount, AbilityMoveTransform, AbilityPierceHit, AbilitySpawnMultiply.  
11. **Item Actions** — EquipItem, UnequipItem, GiveItem, RemoveItem. (Equip/Unequip `#if UNITY_SERVER` guarded — persistent state mutations never run during prediction replay.)  
12. **Quest Actions** — AcceptQuest, AbandonQuest, AdvanceQuestObjective, CompleteQuest, FailQuest, TurnInQuest.  
13. **Interactable Actions** — Bindstone, GatheringNode, LoreObject, NPCLookAtInteractor, PickupWorldItem, SendAbilityCrafterBroadcast, SendBankerBroadcast, SendContainerOpenBroadcast, SendDungeonFinderBroadcast, SendMailboxBroadcast, SendMerchantBroadcast, SendQuestOffer, Shrine, Switch, Teleport.  
14. **Region Actions** — ApplyRegionAttribute, ApplyRegionBuff, ChangeFog, ChangeSkybox, DisplayRegionName, PlayRegionAudio.  
15. **Utility Actions** — AchievementIncrement, AddFaction, ClearTarget, DestroyObject, DisplayDialogue, PlayFX. (DestroyObject `#if UNITY_SERVER` guarded; PlayFX/ClearTarget suppress during prediction replay via `IsReplicateTick`.)

#### ECA Conditions (~30 implementations)
16. **Combat/Attribute Conditions** — HasResource, HasRequiredAttribute, HasBuff, HasCooldown, IsCharacterAlive, IsImmortal.  
17. **Equipment/Inventory Conditions** — CanEquipItem, HasEquippedItem, CanUseItem, HasInventoryItem, HasInventorySpace, HasBankItem, HasBankSpace.  
18. **Social/Progression Conditions** — HasGuild, HasParty, HasFaction, TargetAlliance, IsArchetype, IsRace, HasPet.  
19. **Quest Conditions** — CanAcceptQuest, HasQuest, QuestObjectiveComplete, QuestStatus.  
20. **Achievement Conditions** — AchievementCompleted.  
21. **Composite Conditions** — AND/OR gate with `ConditionTargetCombine` (All/Any) and `Invert` flag.

#### ECA Target Selectors (14 types)
22. **Basic** — EventTarget, Initiator, NearestTarget, FurthestTarget, RandomTarget, AllCharacters.  
23. **Spatial** — AreaTarget, ConeTarget, LineTarget, ChainTarget.  
24. **Hierarchy** — ChildrenTarget.  
25. **Named/Tagged** — NamedSceneObjectTarget, TaggedSceneObjectTarget.

#### ECA Value Providers (10 types)
26. ConstantFloat, ConstantInt, RandomRangeFloat, RandomRangeInt, StatScaledFloat, StatScaledInt, DamageAmount, HealAmount, FactionAmount, QuestObjectiveAmount.

### Item System
27. **Item Template Hierarchy** — `BaseItemTemplate` → `ConsumableTemplate` / `EquippableItemTemplate` → concrete: Potion, Scroll, Armor, Weapon.  
28. **Runtime Item** — `Item` with optional `ItemEquippable`, `ItemStackable`, `ItemGenerator` components.  
29. **Item Generation** — `ItemGenerator` using `DeterministicRNG` for seed-based stat rolls (AttackPower, AttackSpeed, ArmorBonus + random attributes from databases).  
30. **Item Attributes** — Template-driven attribute system with min/max values linked to CharacterAttributeTemplates.  
31. **Item Containers** — `IItemContainer` with slot locking, stacking, swapping. `InventoryController`, `EquipmentController`, `BankController` implementations.  
32. **Item Slots** — Head, Chest, Shoulders, Hands, Legs, Feet, Back, Primary, Secondary, Accessory (10 slots).

### Ability System
33. **Ability Templates** — `BaseAbilityTemplate` → `AbilityTemplate` / `PetAbilityTemplate` with ActivationTime, LifeTime, Speed, Cooldown, Price, RequiresTarget, HitCount.  
34. **ECA Ability Events** — OnTick, OnHit, OnPreSpawn, OnSpawn, OnDestroy — each with configurable ECA triggers.  
35. **Ability Activation State Machine** — Resource cost validation via `IResourceCost` conditions, activation queuing, consumable support, network sync.  
36. **AbilityObject** — Networked GameObject for projectiles/AoE with lifetime, collision, tick handling, and snapshot reconciliation.  
37. **Ability Knowledge System** — Learned abilities, base abilities, ability events, event subset tracking.  
38. **Cooldown System** — Tick-based immutable `CooldownInstance` with reconcile snapshots, static events for add/update/remove.

### Buff/Debuff System
39. **Runtime Buff** — Tick-based timing (ExpiryTick, NextTickTick), stack count, cumulative tick multiplier.  
40. **Buff Template Types** — AttributeBuff (flat stat modifier), AttributeTickBuff (per-tick modifier), ResourceTickBuff (DoT/HoT), StateBuff (stun/freeze/mesmerize), CompositeBuff.  
41. **Buff Reconciliation** — `BuffReconcileEntry` for deterministic rollback in the prediction pipeline.

### Character Attribute System
42. **Three-Tier Value System** — baseValue + formulaModifier + externalModifier = finalValue. Parent/child dependency graph with formula propagation.  
43. **Resource Attributes** — `CharacterResourceAttribute` extends with currentValue (health/mana/stamina), clamping, regeneration.  
44. **Attribute Formulas** — Flat bonus and percentage bonus formulas with dependency tracking.  
45. **Propagation Batching** — Deferred notifications with suppression for replay performance.  
46. **Tick-Driven Regeneration** — Monotonic guard against double-advance.  
47. **Damage System** — `CharacterDamageController`: damage, healing, kill, resurrection, combat state management with full ECA trigger invocation. Client+server deterministic prediction (Damage/Heal/Revive run on both sides; Kill server-only for non-deterministic side effects). Healer enters combat when healing an in-combat ally.
48. **Damage Types & Resistances** — `DamageAttributeTemplate` (physical, fire, frost, etc.) and `ResistanceAttributeTemplate` pairing.  
49. **Death System** — Player death shows dialog with Respawn/Resurrect options. NPC corpse decay timer (configurable per spawner). `ResurrectOfferBroadcast`/`ResurrectAcceptBroadcast`/`RespawnAtBindPointBroadcast`/`DeathBroadcast`. Reconnect-while-dead re-shows death dialog.  
50. **Revive** — `Revive(ICharacter, int)` works on dead characters (unlike Heal). Fires `OnResurrected` static event, resets death animation, fires ECA resurrect triggers.

### Client-Side Prediction Pipeline
49. **Unified Prediction Controller** — `CharacterPredictionController` discovers all `IPredictableController` components, stable-sorts by Order with deterministic type-name tiebreaker, drives a single FishNet Prediction V2 pipeline.  
50. **Participating Subsystems** — KCC movement (Order 80), BuffController (85), CooldownController (90), EquipmentController (93), CharacterAttributeController (95), AbilityController (100).  
51. **Type-Safe Ticks** — `PredictionTick` struct prevents accidental raw tick usage.  
52. **Delta Compression** — `CharacterReconcileDataDeltaSerializer`, `CharacterAttributeResourceStateSerializer`, KCC motor state delta serializer for bandwidth-efficient sync (~43 bytes/tick typical, ~86 bytes/tick combat).  
53. **Deterministic RNG** — xoshiro128** algorithm with full 128-bit state capture in reconcile data. All prediction-path code uses `DeterministicRNG` — zero `UnityEngine.Random` or `System.Random`.  
54. **Shared Speed Enforcement** — `MaxAllowedSpeed = SprintSpeed × 3.0f` runs identically on client and server in shared code. No server-only branches.  
55. **Motor PhysicsScene Init** — `KCCPlayer.Awake` initializes motor's `PhysicsScene` from GameObject scene, ensuring client collision queries (ground detection, wall collision) work identically to server.  
56. **Deterministic Ability Math** — `Math.Ceiling(double)` replaces `Mathf.CeilToInt(float)` for platform-independent activation time rounding (prevents x86/ARM one-tick mismatches).  
57. **Physics Query Guards** — All ECA target selectors (`AreaTarget`, `ConeTarget`, `LineTarget`, `ChainTarget`, `NearestTarget`, `FurthestTarget`, `RandomTarget`) and `AbilityApplyAreaAction` suppress physics queries during prediction replay via `IsReplicateTick` guard.  
58. **Reconcile Delta Efficiency** — Delta serializer sends only changed fields. Idle tick: ~20 bytes. Walking: ~43 bytes. Combat: ~86 bytes. Full struct: ~223-1300 bytes raw → 95-97% reduction.

### AI System (NPC)
53. **State Machine** — Idle, Wander, Patrol, ReturnHome, Retreat, MeleeAttacking, RangedAttacking, CasterAttacking, HealerAttacking, GetBehind, Orbit, PetIdle states.  
54. **Behavior Tree** — Selector, Sequence, Inverter, Repeater, Composite, Condition nodes.  
55. **Group Combat** — `NPCGroup` with roles, pack tactics, aggression management.  
56. **Boss Mechanics** — `BossPhase`, `BossScript`, `BossTimedMechanic`.  
57. **Navigation** — NavMeshAgent-based with waypoints, avoidance priorities, LOD settings.  
58. **Deterministic RNG** — Seeded per-NPC for reproducible behavior.  
59. **Ability Rotation** — `AIAbilityRotation` for combat ability selection.  
60. **Combat Personality** — `AICombatPersonality` configuration for varied NPC combat styles.

### Interactable System
61. **16 Interactable Types** — AbilityCrafter, Banker, Bindstone, CapturePoint, Container, Dialogue, DungeonEntrance, GatheringNode, LoreObject, Mailbox, Merchant, Quest, Shrine, Switch, Teleporter, WorldItem.  
62. **Base Interaction** — Range (3.5u default), 60ms rate limit, ECA trigger execution.  
63. **Server-Side Validation** — `CanInteract()` + `InRange()` checks before trigger execution.  
64. **Capture Points** — PvP capture points with state machine (Neutral, Capturing, Captured).  
65. **Dialogue Trees** — `DialogueTemplate` with nodes and choices, server-authoritative session management with choice bitmasks.  
66. **Gathering Nodes** — Harvesting with drop tables, cooldowns, remaining uses.  
67. **Merchant Tabs** — Categorized merchant inventory tabs.

### Faction System
68. **Faction Standing** — Per-faction integer standing with Allied/Neutral/Hostile classification.  
69. **Faction Matrices** — Template-driven faction relationship matrices with editor tooling.

### Quest System
70. **Quest Lifecycle** — Inactive → Active → Complete → TurnedIn / Failed.  
71. **Objective Tracking** — Per-objective progress with required amounts.  
72. **Attribute Requirements** — Pre-requisite attribute checks before acceptance.

### Social Systems
73. **Friends** — Friend list management with online status.  
74. **Guilds** — Membership, invites, ranks, join/leave ECA triggers.  
75. **Parties** — Creation, invites, member tracking, leader ranks.

### World System
76. **World Scene Details** — Per-scene configuration: max clients, spawn/respawn positions, teleporters, boundaries.  
77. **Day/Night Cycle** — Configurable cycle durations, skybox transitions, object activation/deactivation, material alpha fading, ECA triggers for day/night transitions.  
78. **Spawner System** — Linear/Random/Weighted spawning with respawn conditions (OR/AND), initial/max counts, pooling (`ObjectSpawner`). NPC corpse decay with per-spawner override. Re-rolled attributes on each spawn.  
79. **Teleporter System** — Cross-scene and same-scene teleportation with cached destinations.  
80. **Region System** — Zone definitions for area effects (fog, skybox, audio, buffs, attributes, region name display).  
81. **Scene Boundaries** — Terrain and custom boundary definitions.

### Character Appearance & Visual Equipment
82. **Modular Character System** — One shared humanoid skeleton, one Animator, one animation library for all races and equipment.  
83. **Body Region System** — Body mesh split into 6 hideable regions (Head, Torso, Arms, Hands, Legs, Feet). `BodyVisibilityManager` with per-slot reference counting for overlapping equipment hides.  
84. **Character Customization** — Bone scaling for Height, ArmLength, LegLength, TorsoLength, ShoulderWidth, HeadScale. Race presets (Human/Dwarf/Elf). Blend shapes for Weight, MuscleMass, ChestSize, WaistSize.  
85. **Equipment Visuals** — `EquipmentVisualController` with persistent renderer pool (no Instantiate/Destroy spam). Loads prefabs via Addressables, extracts mesh + materials, binds to skeleton via `SkeletonBinder.BindMeshKeepParent`.  
86. **Weapon Attachment** — Weapons as `MeshRenderer` children of bone transforms (RightHand, LeftHand). Follow animations automatically. Scale-independent from body proportions.  
87. **Equipment Mesh Variations** — `EquippableItemTemplate.EquipmentMeshes` list with seed-based selection via `ModelPools`/`ModelSeed`.  
88. **SkeletonBinder** — Bone name matching with caching. Generation-based cache invalidation for instance ID recycling safety.  
89. **Animation System** — `CharacterAnimationController` with Speed, IsGrounded, IsCrouching, Jump, Attack, Block, Roll, Cast, Death, RootMotion. FishNet `NetworkAnimator` integration.  
90. **Ability Animation** — `TriggerAbilityAnimation` maps `AbilityType` to animation: Physical→Attack, Magic→Cast, Block→SetBlocking, Roll→TriggerRoll. Death animation suppresses all other state.

### AI Threat System
91. **Threat Table** — `AggressionController` with damage, healing, resource expenditure threat. Configurable weights per category.  
92. **Vulnerability Scoring** — Low-health targets (<30%) get 1.5x threat multiplier. Low-mana targets (<20%) get 1.3x multiplier. AI intelligently pressures weakened enemies.  
93. **Replay-Safe Events** — `AggressionState.IsSpawnedAndAuthoritative()` guard prevents threat double-counting during client-side prediction replay.  
94. **Object-Pooled Aggression Entries** — Stack-based pool for `AggressionEntry` to avoid per-event allocations.

### Network Broadcasts (30+ types)
82. **Auth** — Authentication request/response, token sync.  
83. **Character** — Character data, abilities, achievements, archetype, factions, friends, guild, party, pet, quest, hotkeys.  
84. **Inventory** — Inventory, equipment, bank slot sync.  
85. **Character Create/Select** — Creation request/result, character details, delete.  
86. **Chat** — Chat messages with 10 channels (Say, World, Region, Party, Guild, Tell, Trade, System, Command, Discord).  
87. **Interactable** — Interactable state sync.  
88. **Naming** — Name reservation/release, ID ↔ name resolution.  
89. **Scene** — Scene loading, transitions, channel addresses.  
90. **Server Select** — Server list and connection info.

### Bootstrap & Tools
91. **Bootstrap System** — Multi-environment asset/scene preloading (Editor, Standalone, WebGL), version management, graceful shutdown.  
92. **Addressable Integration** — `AddressableLoadProcessor` for async prefab/sprite/mesh loading with caching.  
93. **Template Caching** — `CachedScriptableObject` with database-wide lookup and Addressable icon/mesh loading.  
94. **DeterministicRNG** — Reproducible random number generator for networked determinism.  
95. **SerializableDictionary / SerializableHashSet** — Unity-serializable generic collections with custom property drawers.  
96. **Version Management** — `VersionBuilder` with `VersionConfig` ScriptableObject; increments major/minor/patch, writes `version.txt` at build time.

### Editor Tools
97. **FishMMO Dashboard** — Custom build tool suite: AddressableManager, BuildConfigurator, BuildExecutor, LinkerGenerator.  
98. **Patch Generator** — Unity Editor window for creating delta patches between builds with manifest generation.  
99. **Addressables Dashboard** — Analysis, build, categorization, and tree view for addressable assets.  
100. **Behaviour Tree Editor** — Visual editor for NPC behaviour trees.  
101. **Dialogue Tree Editor** — Visual editor for NPC dialogue trees.  
102. **World Scene Details Cache Builder** — Builds cached world scene details at edit time.  
103. **Custom Property Drawers** — `[ShowReadonly]`, `[SubclassSelector]`, `[TemplateReference]`, serializable dictionary drawers.  
104. **Build Environment Options** — Development/Release build environment selector.  
105. **Security Assembly Filter** — Editor-only assembly filtering for security-sensitive code.

---

## FishMMO-WebServers

**ASP.NET Core web services** providing client-facing HTTP APIs.

### IPFetchASP.NET (Login Server Discovery)
1. **Login Server Discovery API** — `/loginserver` endpoint returns available login server ports from the database with a one-time connection token (SHA-256 hashed, 60s TTL) for real-IP recovery through the L4 UDP proxy.  
2. **ClientGate** — Validates the `X-FishMMO-Client` HMAC-SHA256 header with multi-key rotation, 30s skew window, 20K-nonce LRU cache, and URL-decoded path traversal protection.  
3. **Port Safety** — `WebServer:HttpPort` uses nullable `GetValue<int?>()` to prevent port-0 binding when config key is missing.  
4. **CORS Headers** — `Access-Control-Allow-Origin: https://play.fishmmo.com` on API responses for cross-origin WebGL client access.

### PatcherASP.NET (Patch Delivery)
5. **Latest Version Endpoint** — `/latest_version` returns current game version, up-to-date status, and patch availability info (SHA-256, size).  
6. **Patch Download Endpoint** — `/{version}` serves patch ZIP files with range request support, `ReparsePoint` symlink rejection at serve time, and SHA-256 integrity verification.  
7. **ClientGate** — Same HMAC request signing validation as IPFetch.  
8. **Sliding-Window Rate Limiting** — Patch downloads limited to 6 requests/minute via sliding window (prevents fixed-window boundary burst).  
9. **Symlink Protection** — `PatchVersionService` reindex skips `FileAttributes.ReparsePoint` files to prevent hash disclosure via symlinks.  
10. **Semantic Versioning** — `VersionConfig` with full SemVer 2.0.0 parsing, comparison operators, and `IComparable<VersionConfig>`.

### WebGLServerASP.NET (WebGL Static Server)
11. **WebGL Build Serving** — Serves Unity WebGL builds as static files (HTML, JS, WASM, `.unityweb`, `.data`) with correct MIME types and `X-Content-Type-Options: nosniff`.  
12. **Response Compression** — `AddResponseCompression` middleware with `application/wasm` and `application/octet-stream` MIME types for bandwidth reduction on large WASM builds (20–50 MB).  
13. **Cross-Origin Isolation** — CSP headers configured for `wasm-unsafe-eval` and WebTransport `connect-src` to `game.fishmmo.com:*`.  
14. **ClientGate** — Request validation with same HMAC signing as IPFetch.

---

*End of FishMMO Feature List*
