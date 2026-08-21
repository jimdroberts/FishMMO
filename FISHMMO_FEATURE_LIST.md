# FishMMO — Complete Feature List

> Generated 2026-06-26 from the FishMMO-Dev monorepo. Updated 2026-08-15 against commit `630f975c`.  
> Built on Unity 6000.3.2f1, FishNet, PostgreSQL, .NET 8.0, WebTransport (QUIC/HTTP3).
>
> Items marked *scaffolded*, *planned*, *projected*, or *not yet wired* are **not** shipped functionality — read those qualifiers literally.

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
4. **WebSocket Health Check** — Full WebSocket upgrade handshake probe. A generic capability of the supervisor (`WebSocketHealthChecker`, `PortType.WebSocket`); FishMMO's own game servers are QUIC/UDP and are probed with the UDP checker.  
5. **CPU Threshold Monitoring** — Samples per-process CPU% and triggers restart on sustained breach.  
6. **Memory Threshold Monitoring** — Samples per-process memory usage and triggers restart on sustained breach.  
7. **Exponential Backoff Restarts** — Failed processes restart with increasing delay (configurable initial → max, capped retries).  
8. **Circuit Breaker** — After N consecutive failures across launches, parks the application until manual intervention.  
9. **Graceful Shutdown** — Sends close signal to child process; force-kills if it doesn't exit within timeout.  
10. **Interactive Console Commands** — `start`, `stop`, `status`, `force-restart`, `force-kill`, `shutdown` (alias `exit`), `help`, registered through `CommandHandler`/`ConsoleCommand`.  
11. **Headless Mode** — `Headless: true` in config disables stdin reads and starts monitoring immediately on launch (for systemd/Docker). Exits with a failure code when the headless monitoring cycle ends with all monitors exhausted.  
12. **Per-App Config Validation** — Validates all settings at startup, rejects with precise error messages.  
13. **Launch Delay Sequencing** — Configurable per-app delay before launching the next application in sequence.  
14. **Post-Launch Settle Delay** — Pause after launch/restart before resuming probes (lets the process fully boot).  
15. **systemd Integration** — Handles both SIGTERM and SIGINT through the same graceful shutdown path. No `.service` file is checked into the project; the README documents a reference unit inline, and FishMMO-Installer can generate and register one.

---

## FishMMO-Art

**Art and visual asset repository.** Contains game art assets (models, textures, materials, animations, UI graphics) consumed by the Unity project. No code or configuration — purely creative assets.

---

## FishMMO-Auth

**Transport-agnostic .NET authentication library** providing SRP-6a login, token auth, TOTP 2FA, and engine-independent authenticator cores. Split into three projects: **FishMMO-AuthShared** (DTOs, enums, crypto services, trackers), **FishMMO-ClientAuth** (`ClientAuthenticatorCore`), and **FishMMO-ServerAuth** (`BaseAuthenticatorCore`, `SrpAuthenticatorCore`, `TokenAuthenticatorCore`). All auth broadcast structs used by the Unity layer live in `FishMMO-Unity/Assets/Scripts/Shared/Implementation/Network/Authentication/AuthenticationBroadcasts.cs`.

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

**ASP.NET Core (net8.0) account-management web API** — *not* a news/content CMS. Registers Swashbuckle and references FishMMO-ServerAuth and FishMMO-DB, and copies `appsettings.CMS.json` from FishMMO-Setup at build time.

> **Status: scaffolded, not implemented.** Every controller action in this project is a stub — each one returns a placeholder and carries `// TODO` comments for the work it does not do. There is no database wiring, no authentication registration for the account endpoints, and no admin authorization on the admin endpoints. Nothing in this section is shipped functionality.

1. **AccountController (`api/Account`)** — Route stubs for `POST register`, `POST verify`, `POST change-password`, `POST 2fa/setup`. TODOs cover SRP salt/verifier generation, `IAccountService` persistence, TOTP secret generation/encryption, recovery codes, and verification email delivery.  
2. **AdminController (`api/Admin`)** — Route stubs for `GET accounts/search`, `POST accounts/{username}/ban`, `unban`, `access-level`, `revoke-tokens`, `reset-2fa`, `force-password-reset`. Every action's first TODO is "Require admin authentication" — the endpoints are currently unauthenticated stubs.  
3. **appsettings.json Configuration** — `CopyFishMMOConfig` MSBuild target copies `FishMMO-Setup/Development/appsettings.CMS.json` (and the Production variant when present) into the build output.

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
8. **DatabaseResult\<T\>** — Uniform result envelope (`IsSuccess`, `ErrorCode`, `ErrorMessage`, `Data`).  
9. **DatabaseErrorCodes** — Stable error code enum returned via DatabaseResult.  
10. **Layered Configuration** — `appsettings.json` → `appsettings.{Environment}.json` → environment variables (with `__` nesting).  
11. **FISHMMO_ENVIRONMENT** — Precedence-based environment selection (FISHMMO_ENVIRONMENT > DOTNET_ENVIRONMENT > ASPNETCORE_ENVIRONMENT).  
12. **Schema Validation** — `ValidateSchemaAsync` reports, without throwing, whether the database still matches the entity model: **pending migrations** (migrations exist that this database has not run) and **model drift** (the model changed after the newest migration was created, so none covers it yet) are kept distinct because they need different fixes, and `SchemaValidationResult.DescribeProblem` names the command for each. Drift detection reads EF's own model differ — a less stable API surface than the rest of the layer — so a failure to evaluate it is reported rather than thrown. Servers run it at startup, unawaited and non-fatal.  
13. **UTC Timestamps** — Every default and every raw-SQL write uses `timezone('UTC', CURRENT_TIMESTAMP)` rather than `CURRENT_TIMESTAMP`. The columns are `timestamp without time zone` while `CURRENT_TIMESTAMP` is a `timestamptz`, so the bare form silently stored the *session's* local time for anything compared against `DateTime.UtcNow` — `last_pulse` most visibly, whose liveness query already compared in UTC.

### Database Services (Npgsql/Services/)
12. **IAccountService** — Account CRUD: create, fetch for login (SRP data), online status check, kick request persist, token hash persist, TOTP verify.  
13. **ICharacterService** — Character CRUD: save, load, delete, fetch by account, session claim/release, inventory/equipment/bank/hotkey persist.  
14. **IChatService** — Chat message persistence and retrieval with channel, character, and server metadata.  
15. **ILoginServerService** — Login server registration, heartbeat pulses, signing key storage (AEAD-wrapped via deployment KEK).  
16. **IWorldServerService** — World server registration, heartbeat pulses, server listing, and operator lifecycle control (`SetLockedAsync`, `SetShutdownAsync`, `FetchControlStateAsync`). The `locked` and `shutdown_at_utc` columns are the **authority**: registration deliberately preserves them on conflict, and `PulseAsync` *reads them back* (`UPDATE … RETURNING`) so the process adopts what an operator set rather than overwriting it.  
17. **ISceneServerService** — Scene server registration, heartbeat pulses, pending scene queue, channel listing, and the same operator lifecycle control as the world server (`SetLockedAsync`, `SetShutdownAsync`, read-back on pulse). A registration outlives a crash — it is only deleted on graceful shutdown — so callers judge liveness from `LastPulse`, not from the row existing.  
18. **ICharacterInventoryService** — Inventory/equipment/bank slot persistence.  
19. **IGuildService** — Guild creation, membership, ranks, invitation persistence.  
20. **IPartyService** — Party creation, membership persistence.  
21. **ICharacterFriendService** — Friend list add/remove/query persistence. (Part of a wider per-character service family: `ICharacterAbilityService`, `ICharacterAchievementService`, `ICharacterArchetypeService`, `ICharacterAttributeService`, `ICharacterBankService`, `ICharacterBuffService`, `ICharacterEquipmentService`, `ICharacterFactionService`, `ICharacterGuildService`, `ICharacterHotkeyService`, `ICharacterItemCooldownService`, `ICharacterKnownAbilityService`, `ICharacterMailService`, `ICharacterPartyService`, `ICharacterPetService`/`ICharacterPetAttributeService`/`ICharacterPetBuffService`, `ICharacterQuestService`, `ICharacterSkillService`.)  
22. **IKickRequestService** — Kick request queue polling and processing.  
23. **Auth & Deployment Services** — `IAuthTokenService` (token hash persist/revoke), `ILoginServerSigningKeyService` (AEAD-wrapped signing keys), `ITwoFactorRecoveryCodeService`, `IConnectionTokenKeyService` (one-time connection token keys for IPFetch), `IDeploymentSecretService` (database-stored deployment secrets, e.g. the signing-key KEK), `IEmailQueueService` (verification email queue), `ISceneService` (pending scene load/unload queue, scene-instance registry, availability and instance lookups, batched population pulses, the de-duplicating `EnqueueIfUnderOutstandingLimitAsync`, and the two stale-row reapers `DeleteStaleUnreadyAsync` / `DeleteByStaleSceneServersAsync`), `IGuildUpdateService` / `IPartyUpdateService` (social update pumps).  
24. **UnitOfWorkService** — Ambient DbContext + transaction scope for multi-step atomic operations. Supports savepoints for nested atomicity inside a unit of work.  
25. **BaseService Execution Wrappers** — `ExecuteReadAsync`, `ExecuteWriteAsync`, `ExecuteTransactionAsync` with retry logic, transient error classification (PostgreSQL error code mapping), and automatic SaveChanges.  
26. **Convention Guards** — `ApplyTimeCreatedConventions` skips entities with explicit defaults (prevents silent override of `QuestEntity`'s `DateTime.UnixEpoch`). `ApplyLogicalVersionConventions` checks for existing defaults before applying.  
27. **Npgsql Type Mapping** — `List<int>` properties natively map to PostgreSQL `integer[]` columns; `HasDefaultValueSql("'{}'")` for empty array defaults.

### Data Entities
28. **AccountData** — Account credentials (SRP verifier, salt), email, 2FA state, verification status.  
29. **CharacterData** — Full character sheet: position, race, archetype, attributes, equipped items, hotkeys, achievements, faction standings.  
30. **ChatData** — Chat message with channel, content, character, server metadata.  
31. **LoginServerData / WorldServerData / SceneServerData** — Server registration and heartbeat entities.  
32. **AuthTokenData** — Token hash with expiration for revocation lookup.  
33. **LoginServerSigningKeyData** — AEAD-wrapped HMAC signing key per login server.  
34. **KickRequestData** — Admin-initiated kick request queue.  
35. **SceneData** — Pending scene load/unload requests.  
36. **QuestData** — Quest state persistence.  
37. **TwoFactorRecoveryCodeData** — Hashed 2FA recovery codes.  
38. **IVersioned / VersionExtensions** — Optimistic concurrency versioning on all entities.

### Monitoring Infrastructure (Npgsql/Monitoring/)
39. **DatabaseHealthMonitor** — `SELECT 1` connectivity probe with Healthy/Degraded/Unhealthy classification.  
40. **ConnectionPoolMetrics** — Runtime open connections, pool utilization %, driven by EF Core connection interceptors.  
41. **DatabaseMetricsTracker** — Success/failure/latency aggregates with summary reporting.  
42. **QueryPerformanceTracker** — Per-operation query performance with P95/P99 percentiles, slow query detection events, configurable tracking levels (None/Basic/Standard/Detailed/Full).

### Unity Integration
43. **DatabaseHealthService** — Unity MonoBehaviour wrapping the monitoring stack. Inspector-configurable health/pool/metrics check intervals. Exposes events for external alerting (Slack/PagerDuty). Context menu commands for manual health checks.

### Exceptions
44. **DatabaseException** — Typed database exception hierarchy: `DatabaseEntityNotFoundException`, `StaleStateException`, `DuplicateReplayException`.

### Database Migrator
45. **FishMMO-DB-Migrator** — Standalone console tool for creating and applying EF Core migrations.

---

## FishMMO-Dependencies

**Centralised NuGet dependency library** — single source of truth for third-party package versions across the entire solution.

1. **EF Core Stack (pinned 5.0.x)** — EF Core, Abstractions, Relational, Design, Tools (all 5.0.17), EFCore.NamingConventions (snake_case), Npgsql 5.0.18 + Npgsql.EntityFrameworkCore.PostgreSQL 5.0.10. EF Core is **intentionally pinned to 5.0.x** for netstandard2.1 / Unity compatibility; the csproj carries an explicit warning that EF Core 5.0.x was compiled against older `Microsoft.Extensions.*` assemblies, so mixing in 9.0.x surface APIs risks `TypeLoadException` / `MissingMethodException` under Unity's resolver — hard crashes on IL2CPP rather than warnings.  
2. **Microsoft.Extensions Stack (9.0.4)** — Configuration (+ Json, Abstractions, Binder, EnvironmentVariables), DependencyInjection (+ Abstractions), Logging (+ Abstractions), Caching (Abstractions, Memory), Options, Primitives, Http (pinned to override the transitive 2.1.0 pulled by OpenAI), Bcl.AsyncInterfaces.  
3. **Utility Libraries** — srp 1.0.7 (SRP-6a), BouncyCastle.Cryptography 2.6.2, Otp.NET 1.4.1 (TOTP), HtmlAgilityPack, Humanizer, OpenAI, ZString, System.Collections.Immutable, ComponentModel.Annotations, DiagnosticSource, IO.Hashing (xxHash/Crc32/Crc64), Text.Json, Text.Encodings.Web, Threading.Channels, Runtime.CompilerServices.Unsafe.  
4. **Redis Pins** — StackExchange.Redis 2.8.0, Pipelines.Sockets.Unofficial 2.2.8, StackExchange.Redis.Extensions.Core 10.0.0 — transitively pulled by FishMMO-AuthShared, pinned here for solution-wide version consistency.  
5. **FishMMO Sub-Library Project References** — Builds and forwards FishMMO-AuthShared, FishMMO-ClientAuth, FishMMO-ServerAuth, FishMMO-DB, FishMMO-SharedUtility, and FishMMO-Logger so their DLLs land in Unity alongside the NuGet output.  
6. **Post-Build DLL Copy** — Output DLLs automatically copied to `../FishMMO-Unity/Assets/Dependencies/` via the `CopyDependenciesToUnity` MSBuild target (cross-platform forward-slash paths). System DLLs excluded from copy to avoid Unity conflicts.  
7. **Stale DLL Sweep** — `RemoveStaleDependencies` runs before the copy and clears the Unity `Assets/Dependencies` folder, so DLLs from removed NuGet packages do not linger.

---

## FishMMO-DiscordBot

**Standalone .NET 8 Discord bot** that bridges in-game chat with a Discord guild.

1. **Game → Discord Chat Relay** — `ChatPollingService` (an `IHostedService` timer with a `SemaphoreSlim` reentrancy guard) polls the game **database directly** via `NpgsqlDbContextFactory`, tracking `lastProcessedChatId`, and forwards new messages to the mapped Discord channels. There is no chat REST API in this path.  
2. **Discord → Game Chat Relay** — Discord messages intercepted and pushed back to the game via `GameChatBridgeService`.  
3. **Account Linking** — `link` / `unlink` commands (`LinkModule`, `AccountLinkingService`, `PendingLinkVerification`): issues short-lived one-time codes redeemable in-game to link Discord ↔ FishMMO account.  
4. **Dynamic Channel Management** — Creates/archives Discord channels in response to in-game events (party formed, guild created).  
5. **Moderation Commands** — Mute, unmute, ban, unban for the chat bridge (uses `BridgeBanService`).  
6. **Admin Commands** — Reload config, shutdown, diagnostics (owner/admin-only).  
7. **Character Lookup** — Query character info by name or Discord-linked account.  
8. **Text Command Handling** — All commands are Discord.Net **text** commands (`CommandService`, `ModuleBase<SocketCommandContext>`, `[Command("…")]`). `CommandHandlingService` accepts either a leading `/` character prefix or an @-mention. Note this is a message prefix, not a registered Discord application command — no `InteractionService` or slash-command registration exists in the project. ~34 commands across General, Admin, Moderation, Character, Link, Database, and CommandList modules (`ping`, `help`, `commands`, `online`, `whois`, `inspect`, `getcharacter`, `getaccount`, `search`, `guild`, `channels`, `scenes`, `sceneservers`, `worldservers`, `status`, `kick`, `ban`/`unban`, `ban-bridge`/`unban-bridge`, `bridge-bans`, `mute-zone`/`unmute-zone`, `my-mutes`, `enable-cmd`/`disable-cmd`, `list-cmds`, `cmd-config`, `require-role`/`unrequire-role`, `cleanup`, `echo`, `link`/`unlink`).  
9. **Rate Limiting** — Per-user/per-channel sliding-window rate limiter to prevent spam from either side (`RateLimiterService`).  
10. **Bridge Ban System** — Tracks Discord users banned from the bridge; consulted before forwarding (`BridgeBanService`).  
11. **Config File Watching** — `BotConfigurationService` watches `appsettings.json` for changes and propagates config at runtime.  
12. **Generic Host + DI** — Built on `Microsoft.Extensions.Hosting`; all services are `IHostedService` with full DI composition.  
13. **Database Read-Only Queries** — Admin-gated database queries via `DatabaseModule`.  
14. **Self-Documenting Help** — `help` and `commands` list available commands, driven by `CommandService` reflection over the registered modules (`CommandListModule`). Per-command enable/disable and role gating come from `CommandPermissionConfig`.

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
34. **Database Credentials File** — Generates `/etc/fishmmo/db-secrets.env` (systemd `EnvironmentFile`) and `~/.config/fish/conf.d/fishmmo-secrets.fish` (fish shell snippet) so database passwords never live in plain-text JSON. Application secrets (gate secret, KEK, connection token HMAC key) are stored in the database, not in env files.  
35. **AppSettings Secure Wizard** — Interactive configuration wizard for all FishMMO components (Database, IPFetch, Patcher, WebGL, Discord Bot, CMS). Preserves unmanaged JSON keys across writes. Applies `chmod 600` on all output files.  
36. **SecurityKeyInstaller** — Generates CSPRNG keys (`RandomNumberGenerator.Fill`, base64, round-trip validated) and writes them **directly to the database** over a superuser `NpgsqlConnection`, so no env file has to be copied between machines: the ClientGate secret and signing-key KEK into `deployment_secrets` (`client_gate_secret`, `signing_key_kek`) and the connection token HMAC key into `connection_token_keys` (`key_id='shared'`). Superuser credentials come from the interactive prompt or `FISHMMO_PG_SUPERUSER_PASSWORD`. The matching client-side build constants (`ClientApiSecret.generated.cs`, `CertificatePins.generated.cs`, `HostConfig.generated.cs`) are generated separately from **FishMMO Dashboard > Game Settings** in the Unity Editor.

### Build Automation
37. **Build All C# Projects** — Discovers and builds all `.csproj` files under the repo root with dependency-prioritized ordering (synchronous for low-priority projects, parallel for independent builds). Copies DLLs to Unity Dependencies.  
38. **Unity Build Automation** — Headless Unity builds via `-batchmode -nographics -executeMethod` for Client/Server/Addressables. Resolves Unity executable path from environment variable, Unity Hub CLI, or filesystem probing.  
39. **Unity Hub + Editor Installation** — Installs Unity Hub (Linux: apt/AUR, Windows: official installer) and Unity Editor versions with selectable build support modules via Unity Hub CLI.

### Platform Support
40. **Cross-Platform** — Windows 10/11 and Linux (Arch/CachyOS, Ubuntu/Debian, Fedora/RHEL).  
41. **Package Manager Auto-Detection** — pacman, apt-get, dnf, and yum auto-detected with appropriate update/install command templates.  
42. **Platform Abstraction** — `IPlatform` interface with `WindowsPlatform` / `LinuxPlatform` implementations for shell command dispatch, privilege elevation, and command availability checks.

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
10. **Unity Integration** — `UnityLoggerBridge` (captures Unity log callbacks into the facade, with an `IsLoggingInternally` re-entrancy guard), `UnityConsoleLogger` sink, and `UnityConsoleFormatter`. These live in the Unity project under `Assets/Scripts/Shared/Implementation/Bootstrap/Logging/`, not in the FishMMO-Logger library itself, so the library stays engine-independent.  
11. **Async Shutdown** — `Log.Shutdown()` (async `Task`) drains and disposes all sinks gracefully. Bootstrap detaches `UnityLoggerBridge` before the async shutdown runs.

---

## FishMMO-Patcher

**Standalone .NET 8 updater** (`Updater/Program.cs`, ~850 lines) that applies a versioned binary patch to a FishMMO client. Invoked by the launcher with `-version=`, `-latestversion=`, `-pid=`, `-exe=`.

1. **Single-Archive Patch Application** — Applies exactly **one** archive per run: `Patches/{from}-{to}.zip`, built from the `-version` and `-latestversion` arguments. There is no patch chaining — if that specific archive is absent the updater logs the miss, restarts the client, and exits.  
2. **Patch Manifest Parsing** — Reads `manifest.json` from the ZIP into `PatchManifest` (`OldVersion`, `NewVersion`, `NewFiles`, `ModifiedFiles`, `DeletedFiles`).  
3. **Binary Diff Application** — `Patcher.Apply` reconstructs each modified file from its `PatchDataEntryName` diff stream into a temp file. New files are verified against `NewHash` (XxHash128) after extraction and deleted on mismatch.  
4. **Parallel File Operations** — New and modified files processed concurrently via `Parallel.ForEach` with an exception bag that stops the loop on first failure.  
5. **Transactional Patching** — Every replaced file copied to `.bak` before the move; failure anywhere triggers a full rollback to the previous state.  
6. **Atomic File Replacement** — Patched content written to unique temp files, then moved over originals in a finalization phase.  
7. **Launcher Process Management** — Terminates the launcher by PID before patching: `kill(SIGTERM)` via a `libc` P/Invoke on Linux/macOS, `Process.CloseMainWindow()` on Windows, falling through to a forced `Kill()` on any path where the graceful request fails or is ignored.  
8. **Automatic Client Restart** — `TryStartExecutableAndExit` starts the client executable on every exit path (success, failure, missing archive, already-current) and always `Environment.Exit(0)` — the launcher treats a non-zero code as an updater failure.  
9. **Archive Lifecycle** — The consumed archive is deleted on success so `Patches/` does not accumulate; it is **kept** on failure so a retry does not require re-downloading.  
10. **Retry with Backoff** — `TryDeleteFile` / `TryMoveFile` retry with a fixed delay for transient file I/O errors before giving up.

---

## FishMMO-Setup

**Configuration templates and reference files** for deployment environments.

### nginx.conf — Reverse Proxy
1. **UDP Stream Proxy (L4)** — Raw UDP forwarding for game ports 7770–7999 via `stream {}` block. Auto-generated per-port configs via `gen-fishmmo-stream-config.sh` with atomic replacement and `nginx -t` validation. Zero-copy packet forwarding; no TLS termination at proxy.  
2. **HTTP/HTTPS Gateway (L7)** — TLS 1.2/1.3 termination with Let's Encrypt certificates, HSTS (6 months + includeSubDomains), modern cipher suite (ECDHE+AESGCM:ECDHE+CHACHA20), OCSP stapling.  
3. **Virtual Hosts** — `play.fishmmo.com` (WebGL client), `api.fishmmo.com` (IPFetch + Patcher), `game.fishmmo.com` (444-close — game traffic is UDP-only). Catch-all returns 444.  
4. **Rate Limiting** — `limit_req_zone` per-endpoint: 10r/s API, 2r/s patch downloads, 30r/s WebGL. `limit_conn_zone` per-IP: 20 conn WebGL, 10 conn API, 3 conn patch. HTTP 429 with `Retry-After`.  
5. **Security Headers** — CSP (WebGL: `wasm-unsafe-eval`, `connect-src 'self' wss://game.fishmmo.com:* https://game.fishmmo.com:*`), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy`, `Access-Control-Allow-Origin` for API. Browser WebTransport is permitted by the `https://` entry; the `wss://` entry is a leftover from the retired WebSocket transport and grants nothing that is still used.  
6. **Performance** — `sendfile on`, `tcp_nopush on`, `tcp_nodelay on`, `gzip on` with `gzip_proxied any` (not `off`), `gzip_types` tuned for text/wasm, `keepalive_timeout 65s`.  
7. **Hardening** — `server_tokens off`, `client_max_body_size 64k` globally (raised from nginx's 1m default being too restrictive for POST; the patch download location overrides to `0` / unlimited), `client_body_timeout 10s`, `client_header_timeout 10s`.  

### Server Configuration (.cfg files)
8. **LoginServer.cfg** — ServerName, MaximumClients (4000), Address (127.0.0.1, all traffic via nginx), Port (7770), TLS `CertificatePath`/`PrivateKeyPath` for the server's own QUIC/TLS termination, `AllowedOrigins` (browser WebTransport CORS allow-list; empty = allow all, development only), `ConnectionTokenHmacKeyBase64` (left blank — keys load from the `connection_token_keys` table), and SMTP config (`Smtp:Host/Port/Username/Password/FromAddress/FromName/UseSsl`, each overridable by `FISHMMO_SMTP_*` environment variables).  
9. **Login Queue Keys** — `LoginQueueUpdateRateSeconds` (2.0), `LoginQueueMaxSize` (500), `LoginQueueAdmissionRatePerSecond` (5.0), `LoginQueueTimeoutSeconds` (300) configure `LoginQueueSystem`. All server-authoritative — clients cannot request faster updates.  
10. **WorldServer.cfg** — Port 7780, same Address/TLS/connection-token keys.  
11. **SceneServer.cfg** — Port 7790+, same Address/TLS/connection-token keys. Note `StaleSceneTimeout=5` is present in **all three** .cfg templates, not only SceneServer.  
12. **IPv6 Reserved** — `EnableIPv6` / `IPv6Address` are commented out in every template. IPv6 dual-stack is not supported at the native QUIC layer; IPv6 clients must arrive through an IPv6-enabled NGINX L4 proxy.  
13. **AutoVerifyAccounts** — `true` in Development (bypasses email verification at both account creation and login, flagged with an explicit do-not-copy-to-production warning), `false` in Production so email verification is required.

### Deploy Hooks (contracts, operator-supplied — **not shipped in this repo**)
*Neither script exists under `FishMMO-Setup/`. `nginx.conf` and the deployment docs define the contract each must satisfy, and the root README states plainly that they are operator-supplied.*  
14. **certbot-fishmmo.sh** — Documented post-renewal deploy hook contract: copy Let's Encrypt certs to `/etc/fishmmo/certs/`, `chmod 640`, `chown fishmmo:fishmmo`, reload nginx, restart game servers via systemd (with SIGHUP fallback). Operator installs it to `/usr/local/bin/`.  
15. **gen-fishmmo-stream-config.sh** — Documented generator contract for `stream.d/*.conf` across the game UDP port ranges, validated with `nginx -t` before atomic replacement. `nginx.conf` includes `/etc/nginx/stream.d/*.conf` and expects this generator at `/usr/local/bin/`.  

### Config Templates
16. **Per-Environment appsettings** — `Development/` and `Production/` each hold `appsettings.json` plus per-component variants: `appsettings.Database.json`, `appsettings.IpFetchServer.json`, `appsettings.Patcher.json`, `appsettings.WebGLServer.json`, `appsettings.DiscordBot.json`, `appsettings.AppHealthMonitor.json`, `appsettings.CMS.json`. Component projects copy-and-rename these into their build output at build time.  
17. **Installer Manifests** — `install-config.full.json`, `install-config.quickstart.json`, and `install-config.web.json` (Development only) drive FishMMO-Installer’s non-interactive pipeline.  
18. **logging.json** — Single shared FishMMO-Logger sink configuration.

### Build System
19. **WebTransport Build** — Per-platform scripts in `FishMMO-WebTransport/`; there is **no** `build_all.sh` master script. `build_linux.sh` (native CMake), `build_windows.ps1` / `build_windows_schannel.ps1` (native CMake on Windows), `build_windows_cross.sh` (Zig 0.13+ cross-compile from Linux — downloads the msquic NuGet package for the import library and runtime DLL, compiles with `zig c++ -target x86_64-windows-gnu`, links via `lld-link --out-implib`), `build_macos.sh` (must build on a Mac — msquic’s quictls dependency contains platform-specific assembly that cannot be cross-compiled), plus `rebuild_only.*` incremental helpers.  
20. **Cross-Platform Paths** — Forward-slash paths in `.csproj` files. `$(Configuration)` used directly (no redundant `BuildConfiguration` property).

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
9. **CryptographicOperationsCompat** — netstandard2.1 shim supplying `ZeroMemory` / fixed-time comparison primitives where `System.Security.Cryptography.CryptographicOperations` is unavailable.

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

**The player-facing Unity client** (FishMMO.Client assembly, 175 .cs files).

### Networking & Connectivity
1. **Multi-Server Connection Management** — LoginServer → WorldServer → SceneServer transitions with state tracking via `ClientConnectionManager`.  
2. **Reconnection with Exponential Backoff** — Automatic reconnect attempts with configurable backoff (base 5s × 2^attempt × jitter, max 60s, 10 attempts). The loop is guaranteed to terminate: `TryReconnect` checks the attempt count and the stored world address together, so a retry with nothing to dial falls through to the give-up branch and raises `OnReconnectFailed` (→ `QuitToLogin`) instead of returning silently and leaving the client behind an overlay nothing would ever take down. The first retry after a deliberate `Scene` drop uses `SceneHandoffReconnectDelay` (0.25s, jittered) rather than the failure backoff, because a zone change, channel switch and cross-scene bind respawn are all implemented as handoffs.  
3. **Login-Server Discovery** — Happy-Eyeballs multi-mirror probing via configurable API hosts with staggered probes (0.25s apart), 55s TTL cache, and one-time connection token relay.  
4. **ServerConnectionType State** — Tracks connection state: None, Login, World, Scene.  
5. **Broadcast Sending** — Centralized FishNet broadcast dispatch from the Client MonoBehaviour.  
6. **WebTransport (QUIC/HTTP3) Transport** — All platforms use WebTransport via `Multipass`; NGINX L4 UDP stream proxy forwards raw QUIC to game servers.  
7. **Death Dialog** — `UITKDeathDialog` with Respawn/Resurrect buttons. Handles `ResurrectOfferBroadcast` for dynamic button visibility. Opens from replicated character state (`CharacterFlags.IsDead` in the spawn payload) as well as from `DeathBroadcast`, so logging in dead or transferring scenes dead surfaces it without depending on a message arriving after the world GUI scene has loaded. Actions are confirmed rather than assumed: the dialog stays up until the character is observed alive and re-arms itself if the server declines the request.

### Authentication
8. **SRP-6a Client Login Flow** — Full SRP-6a protocol: cookie challenge echo, key agreement, verify/proof, token-based reauth.  
9. **Token-Based Reauthentication** — Stored auth tokens for seamless World/Scene server transitions.  
10. **Account Creation** — Encrypted credential registration with validation.  
11. **Account Email Verification** — Verification code submission.  
12. **TOTP / 2FA Support** — Two-factor code submission and 2FA setup (QR code + recovery codes).  
13. **Token Renewal & Revocation** — Token refresh on login server and revocation on logout/shutdown.

### Input System
14. **Unity Input System Integration** — `PlayerInputHandler` manages the `PlayerControls` asset.  
15. **Mouse Mode Management** — Cursor visibility/lock state toggling.  
16. **Input Binding Persistence** — Loading/saving input binding overrides to configuration.  
17. **Character Movement Input** — Move, Look, Jump, Crouch, Sprint mapped to KCC replication data.  
18. **Full Gameplay Bindings** — Interact, Cancel, Chat, Inventory, Equipment, Abilities, Guild, Party, Friends, Achievements, Factions, Minimap, Menu, Toggle First-Person, ScrollWheel.  
19. **Right-Click Context Menus** — Inspect, Add Friend, Invite to Party, Trade on player targets.

### Launcher
20. **HTML News Feed** — Fetches launcher news via HtmlAgilityPack. `IHtmlContentFetcher` strips `<script>`/`<style>` and yields the **parsed node** rather than formatted text; `UITKHtmlContentRenderer` builds a `VisualElement` tree from it, because UI Toolkit has no equivalent of TextMeshPro's `<link>` tag and a news link has to be an element that can receive a click. Traversal depth and output size are bounded against a hostile document, and every href is opened through `LauncherLinkPolicy`, which allows only absolute `http`/`https`. When no feed is configured — including an unsubstituted `FISHMMO_SENTINEL_PLACEHOLDER` build URL, which is treated the same as an empty one — or when the fetch fails, the pane shows a configurable built-in summary instead. It is not hidden: hiding it collapsed the panel into a header stacked directly on a footer, which reads as a broken window rather than as a launcher with no news.  
20a. **Link Policy** — `LauncherLinkPolicy` parses each href and permits only absolute `http`/`https` before it reaches `Application.OpenURL`, which would otherwise invoke a registered protocol handler for `javascript:`, `file:`, or any application-registered scheme. One shared implementation for both views on purpose: two copies of an allowlist drift, and a drifted allowlist is a vulnerability.  
21. **API Host Resolution** — Randomised mirror selection from comma-separated host list with HTTPS enforcement (`ApiHostResolver`).  
22. **Version Checking** — `HttpPatchServerService` calls `GET /latest_version?from={clientVersion}` and parses `latest_version`, `up_to_date`, `patch_available`, `sha256`, and `size` into `PatchInfo`. Unparseable version strings are rejected rather than thrown on.  
23. **Patch Download with SHA-256 Verification** — `DownloadPatch(patchUrl, destination, expectedSha256, expectedTotalBytes, …)` streams the archive and recomputes SHA-256 over the written file, failing the download on mismatch. Verification is skipped only when the server supplied no hash. The expected total comes from the version manifest rather than the response, so the player is shown a total before the first byte arrives and a truncated or chunked response cannot change what they were told the download would be.  
23a. **Transfer Statistics** — `DownloadStats` / `DownloadRateTracker` report bytes transferred, expected total, current throughput and an ETA per progress callback, with the rate tracker reset per download so a retry does not inherit the previous attempt's history. Hash verification is reported as its own state: on a large patch it is seconds of work after the transfer has visibly finished, and without saying so the launcher sits at a full bar looking hung.  
24. **Launcher State Machine** — `LauncherState`: LoadingNews, Connecting, CheckingVersion, DownloadingPatch, ApplyingPatch, ReadyToPlay, ClientAhead, ConnectionFailed, VersionCheckFailed, PatchDownloadFailed, UpdaterFailed, LaunchFailed, **PatchUnavailable** (out of date but no patch exists from this specific version — full reinstall required, retry cannot help), VersionError, **ServerRejectedVersion** (game server refused the client's game version).  
25. **Transient-State Watchdog** — `TransientStateWatchdog` coroutine tracks a heartbeat across transient states (connecting/checking/downloading/applying) and recovers the UI if one stalls, so the player is never left with a dead button and no way to act. A separate `LaunchWatchdog` re-enables the Play button if the addressable scene load exceeds `launchWatchdogTimeoutSeconds` (default 30s).  
26. **External Updater Launch** — `IUpdaterLauncher` / `SystemUpdaterLauncher` spawns the standalone Updater process, monitors exit, reports results. A non-zero updater exit code surfaces as `UpdaterFailed`. The resolved patch directory is passed explicitly as `-patches=`, rather than left for both sides to derive and agree only by convention — a disagreement is silent and loops forever.  
27. **UnityWebRequest Service** — Shared MonoBehaviour for HTTP requests with retry, timeout, progress callbacks, custom certificate handling. Launcher API calls are HMAC-signed via `ClientApiSigner` / `ClientApiSecret` (see Security below).  
27a. **UI Toolkit Launcher View** — `ILauncherView` describes the presentation surface in terms of intent (*show this status*) rather than widget manipulation, so all version-check and patch logic stays in one place instead of being coupled to a widget tree. `UITKClientLauncher` is the sole implementation; the uGUI adapter and its TextMeshPro converter were deleted with the Canvas layer, so a missing or wrongly-typed view assignment is now an error rather than a silent fallback. The view also owns its own dismissal: it watches for `ClientPostboot` and hides itself when that scene arrives, independently of the launcher's own load callback, because `AddressableLoadProcessor` returns early for a scene it already tracks and an editor session may have the scene open before Play is pressed — either of which would otherwise leave the launcher drawing over the login screen for the rest of the session.  
27b. **Launcher Settings** — `LauncherSettings` reads and writes the shared `Configuration.GlobalSettings` store (nothing had read a settings file at launcher time before): auto-update on/off, request timeout, retry count and delay, an absolute patch-directory override, and window size. Every getter clamps, because the file is plain text a player can edit and a timeout of `0` would otherwise be honoured literally. Window size is persisted shortly after a resize settles rather than at shutdown — the Updater terminates the launcher rather than closing it — and is clamped against the current display on restore.  
27c. **Install Size Probe** — `InstallSizeProbe` walks the install on a thread-pool thread and caches the total, started only once the launcher is idle: doing it during the version check or a download would contend for disk with the thing the player is actually waiting on.  
27d. **Native Folder Picker** — `NativeFolderPicker` opens the Windows shell folder dialog for the patch directory. Unity exposes no runtime folder picker, so `IsSupported` is false elsewhere and callers hide the button rather than offering one that does nothing; the path text field remains the way to set a folder on every platform. Every failure returns null rather than throwing — this is reached from the screen that, if it breaks, leaves no way into the game.

### Security
28. **TLS Certificate Pinning** — SHA-256(SPKI) base64 pinning via BouncyCastle for UnityWebRequest. Constant-time pin comparison. Release builds fail-closed when pins are not configured.  
29. **IL-Embedded Pin Configuration** — Pins are compiled into the assembly from `CertificatePins.generated.cs`, not loaded from StreamingAssets. Generated from the **FishMMO Dashboard** (`FishMMO > FishMMO Dashboard`, or Ctrl+Shift+D) > **Game Settings** panel, which also emits `ClientApiSecret.generated.cs` and `HostConfig.generated.cs`. There is no `FishMMO > Security` menu — these are Dashboard panels, not menu items.  
30. **TOFU Mode** — Development/editor builds allow empty pins (trust-on-first-use with loud warnings).  
31. **Build-Time Validation** — `IPreprocessBuildWithReport` warns on release builds without TLS pins (at least 2 required).  
32. **Dynamic Pin Update Scaffold** — `IPinUpdateSidecar` interface for out-of-band signed manifest updates with UTC validity windows.  
33. **API Request Signing** — HMAC-SHA256 with `X-FishMMO-Client` header (v1.{ts}.{nonce}.{sig} format), 30s skew window, per-process nonce LRU cache.

### UI Toolkit (UITK) Panels — Login Flow
34. **Loading Screen** — Addressable-loaded transition images with progress bar. Visibility is driven by four independent flags — background Addressable loading, FishNet scene load/unload, an armed reconnect, and world entry — and comes down only when all four are clear, so no driver can pull the overlay out from under another. The world-entry flag is raised on `SceneLoginSuccess` (before the scene load is even requested) and cleared only by the overlay's own `Hide()`; it covers the two gaps the other flags leave — between the FishNet scene load ending and the Addressable world preload starting, and between that preload draining and the character actually spawning — where the overlay used to drop over a half-built world for a full round trip each time. `Client.DismissLoadingScreen` calls `Hide()` on the resolved control rather than `UIManager.Hide`, which is a no-op unless the panel is visible and therefore skipped the flag clearing.  
35. **Reconnect Display** — Reconnect attempt status during network interruptions. Skips the first attempt of a deliberate scene handoff (`ClientConnectionManager.IsSceneHandoffReconnect`), so a routine teleport does not raise a "connection lost" panel over the loading overlay or force the mouse cursor back on; attempts past the first are a genuine failure and are shown.  
36. **Login Panel** — Username/password, TOTP/2FA code, account verification code input.  
37. **Register Panel** — Username, password, email, age fields.  
38. **Server Select** — Available game server list.  
39. **Character Select** — Existing character display with create-new option.  
40. **Character Create** — Name input and appearance customization.

### UI Toolkit (UITK) Panels — World / In-Game HUD
41. **Ability Book** — Learned abilities with details.  
42. **Cast Bar** — Channeling/casting progress display.  
43. **Ability Crafting** — Ability-based item crafting UI.  
44. **Achievement Window** — Achievement tracking and completion display.  
45. **Bank / Storage** — Deposit/withdraw item interface.  
46. **Buff Container** — Active buff/debuff icon management.  
47. **Chat Window** — Message history, tabs, channel picker, input.  
48. **Crosshair** — Reticle display for targeting.  
49. **Dungeon Finder** — Dungeon entrance interface. Closes on send: the request is one-shot — on success the server drops the connection and the client re-routes, and on refusal it answers with `SceneTransferRefusedBroadcast`, which raises its own dialog — so leaving the panel up only invites a second click that the server's ingress guard rejects as a duplicate.  
50. **Equipment Window** — Equipped items and character stats.  
51. **Faction Standings** — Reputation display.  
52. **Friend List** — Online/offline status.  
53. **Guild Management** — Members, ranks, info.  
54. **Hotkey Bar** — Action bar with ability slots.  
55. **Inventory / Bag Window** — Item grid display.  
56. **Main Menu** — Settings, logout, quit.  
57. **Merchant Buy/Sell** — NPC vendor interface.  
58. **Minimap** — Surrounding area display.  
59. **NPC Dialogue** — Conversation window.  
60. **Options / Settings** — Screen settings (resolution, refresh rate, fullscreen, brightness, VSync), five gameplay toggles (`ShowDamage`, `ShowHeals`, `ShowAchievements`, `IgnorePartyInvites`, `IgnoreGuildInvites`), and the thirteen themeable colours with a per-row swatch and a reset. The gameplay rows are generated from a table in code rather than authored per GameObject — the previous set was configured entirely in the scene, which is how all five were lost when the panel was rebuilt.  
61. **Party List** — Group member management.  
62. **Pet Control** — Summon, dismiss, pet abilities.  
63. **Resource Bars** — Health bar, mana bar, stamina bar.  
64. **Target Frame** — Target name, health, buffs/debuffs. A health bar is shown only when the target actually has a health resource — a portal or a signpost is worth targeting and naming, and an empty bar reads as a dead one.  
64a. **Context Menu** — Right-click actions on another player (Inspect, Add Friend, Invite to Party, Trade). Entries are plain elements built at open time, so the panel has no scene dependencies that can go missing. Placement converts the pointer from screen pixels into panel points and then clamps, because a menu opened near an edge would otherwise render partly off-screen with its last entries unreachable.  
64b. **Inspect** — Another player's name and equipment, read straight from their in-memory character; all of it is already synchronised to observers via `WritePayload`/`ReadPayload`, so there is no server round trip. Slots reuse the shared `.fish-slot` classes and carry tooltips, so inspected gear reads the same as your own.  
65. **Death Dialog** — Respawn or resurrect choice. Sits at the `Modal` tier and is deliberately not Escape-closable, since the player has to pick one.

### UI Toolkit Panel Lifecycle
`UITKControl` is the UI Toolkit analogue of `UIControl`, and implements the same contract:

- **`OnStarting` runs against a populated visual tree, not at `Awake`.** `UIDocument` allocates `rootVisualElement` up front but only clones the UXML into it during its own `OnEnable` — after every component's `Awake` — so `Awake` saw a real but *empty* root: every `Q<>` returned null, was cached as null, and was never re-resolved, leaving controls that looked initialised and were wired to nothing. A panel that starts hidden has its `UIDocument` disabled and no tree at all, so the retry is a **coroutine**, not `Update`: Unity dispatches magic methods to the most-derived declaration, and a base `Update` would be shadowed for exactly the panels that need it.
- **Cached elements are re-resolved when the tree is rebuilt.** Hiding a panel disables its `UIDocument` and re-showing clones the UXML afresh, so every element cached in `OnStarting` points into a discarded tree — writes go nowhere and the panel shows whatever the UXML declares. `Show` compares the root's identity and re-runs initialisation when it has changed.
- **`OnAfterStarting` re-applies state that arrived first.** World entry calls `UIManager.SetCharacter` for every control at once, which for a panel that starts hidden lands before any element exists. `UITKCharacterControl` re-applies the character so both orders converge, pairing Pre with Post so a rebuild cannot stack duplicate event subscriptions.
- **`ReleasesCursor` and `CloseOnEscape` are separate flags.** They were briefly merged, because `PlayerInputController` used "is anything Escape-closable" as its test for whether to keep the cursor free — so a panel that released the cursor without registering for Escape had it taken straight back. That proxy was the fault, not the separation: `UIManager.AnyCursorReleasingVisible()` now answers the real question directly. The distinction matters because the two genuinely differ — a confirm dialog needs the cursor but must **not** be dismissable with Escape, since the point of it is that the player chooses. Escape is bound to several actions at once, so `UIManager.ClosedThisFrame` stops a handler that would reopen a panel the same press just closed.
- **Panels are layered by tier, and raise within their tier on click.** Every panel is its own `UIDocument` sharing one `PanelSettings`, and UI Toolkit orders those by sorting order alone — panels left at the same value fall back to scene load order, which put Options (in `ClientPreboot`) permanently behind Login (in `ClientLoginGUI`) and unable to receive a click. `UITKPanelLayer` assigns each panel a tier (`WorldOverlay`, `Hud`, `Window`, `Menu`, `Settings`, `Popup`, `Modal`, `Tooltip`, `Drag`, `System`), declared in code so a new panel inherits `Window` rather than silently defaulting to zero. Clicking a panel raises it **within** its tier, restoring the uGUI click-to-front behaviour while keeping a modal above a window no matter what was clicked last, and re-registers it as the next panel Escape closes.

**Editor tooling.** `FishMMO → UI Toolkit → Validate Panels` checks that every UXML imports and instantiates to a non-empty tree, that every USS imports, that each panel loads `FishMMO-Theme.uss` first, and that no stylesheet carries a keyword `cursor` rule — those are editor-only, and at runtime UI Toolkit logs *"Runtime cursors other than the default cursor need to be defined using a texture"* every frame the pointer is over the element, naming no file. `FishMMO → UI Toolkit → Render Panel Previews` mounts each panel on the project's real `PanelSettings`, renders it through a `RenderTexture` at the reference resolution, and writes a PNG per panel to `Assets/UITKValidationImages/`. It runs as an `EditorApplication.update` state machine rather than a loop, because a panel only lays out between editor frames and a single-call loop would capture forty-odd identical blank images.

The migration-era *Wire Unwired Panels Into Open Scene* tool has been removed: its whole job was copying visibility flags from a legacy panel onto its replacement, which is meaningless now that no legacy panels exist.

### Theme and Layering
The whole UI draws from `FishMMO-Theme.uss`: 51 tokens and 136 selectors, with the palette sampled from the project logo art rather than invented — the fish body is `#00364E`, its eye `#0073C0`, the wordmark runs `#0073C0` into `#58B3F1`, and the plate behind it falls to `#001012`. No colour literal appears in any panel stylesheet; every one references a token, so a retheme cannot miss a value hiding in a panel.

UI Toolkit has no `box-shadow` and no USS gradients, so depth is built two ways and both are deliberate: surfaces stack through the token ramp (window ground, panel body, raised surface, slot), and per-side borders do the bevelling — a light top edge over a dark bottom edge reads as raised, the reverse as inset. Buttons are raised and invert on press; slots and bar tracks are inset, so an item sits *in* a socket and a fill sits *in* a channel.

The theme carries the shared component vocabulary the panels would otherwise each improvise: list rows with hover and a leading accent rail, column captions, badges, presence dots, section headers, empty-state text, bar labels, well surfaces, button variants (primary, danger, ghost), and themed inputs, toggles, sliders and dropdowns.

> **Note on cursors.** USS `cursor` keywords are editor-only. A runtime panel can only change the cursor from a texture, and a keyword rule makes both UIElements and the EventSystem log *"Runtime cursors other than the default cursor need to be defined using a texture"* on every frame the pointer is over the element, naming no file. The theme therefore carries no cursor rules, and `Validate Panels` fails the build on any that reappear.

### Shared UI Components
Every panel that disables a control while awaiting a server reply arms `PendingReplyGuard`, a shared watchdog armed and cleared by the same methods that disable and re-enable the control, and refreshed by any intermediate progress from the server. On expiry it re-enables the control and reports it without tearing anything down, so a late reply is still handled normally. Used by login, register, character create and character select; deliberately not by server select, whose lock spans a multi-hop journey with its own queue feedback. The login panels also refresh the guard on every `LoginQueuePositionBroadcast`, because a queue wait is the one place a login legitimately outlasts the 30s deadline — without it the panel announced that the server had not responded while the queue dialog was still counting down beside it.

Both login panels additionally report a connection that stopped **without** the server ever sending an authentication result. The login server closes the transport with no message for its pre-authentication rejections (unverifiable connection token, unsupported protocol version, oversized handshake field, tripped handshake rate limit), and narrating those to an unauthenticated peer would hand an attacker a probe oracle — so the client is the only party that can explain them. If any `ClientAuthenticationResult` arrived first, its specific message stands; if none did, the panel says the connection was closed before it answered. `Client.QuitToLogin` covers the mirror case, staging an `Unspecified` disconnect notice when a client that already holds a session token loses the login server.

66. **Dialog Box** — Modal informational dialog. Also serves as the shared wait dialog for both connection queues (login admission and World → Scene routing), live-updating its text in place via `SetText` and offering a single Close action that leaves the queue. It takes the cursor but is deliberately **not** closable with Escape: the point of a confirm dialog is that the player chooses.  
67. **Input Dialog Box** — Modal dialog with text input field.  
68. **Color Picker** — Color selection control.  
69. **Custom Dropdown** — Dropdown control.  
70. **Selector / Grid** — Item picking grid.  
71. **Drag Object** — Draggable UI elements.  
72. **Tooltip** — Item/ability information popup on hover.  
73. **UI Theming Engine** — `UITKTheme` parses the player's thirteen configurable colours and `UITKThemeManager` applies them to every registered panel, replacing the Canvas-crawling `UITheme`/`CanvasCrawler` pair. The storage format is unchanged — `{Name}ColorR/G/B/A` bytes — so a configuration file written by the old client still themes the new UI. Overrides are written as **inline styles**, because UI Toolkit exposes no runtime API for setting a custom property and a StyleSheet cannot be authored at runtime; the consequence is that an override applies to an element's resting appearance while its `:hover` and `:active` rules keep coming from the stylesheet, which is exactly the limitation the Canvas crawler had for the same reason.  
74. **UI Manager** — Static registry for all UI Toolkit panels with Show/Hide/Toggle/TryGetTK, close-on-escape stack, focus tracking and character injection. It briefly held a second, parallel registry for the Canvas panels; every lookup checked that one first and fell through with `else if`, so wherever a panel existed in both — twenty-four of them in the world scene — the Canvas panel won and its UI Toolkit twin was unreachable through any generic entry point. That half is gone with the Canvas layer, and with it the whole class of shadowing.

### 3D World-Space Effects
75. **World Label System** — Object-pooled world-anchored labels for damage numbers, heal numbers, achievement popups and nameplates. UI Toolkit has no world-space render mode, so a label is no longer a renderer sitting in the scene: `WorldLabel` is position-plus-text, and `UITKWorldLabelLayer` projects each one onto a screen-space panel every frame through `RuntimePanelUtils.CameraTransformWorldToPanel`. Two behaviours of real 3D text are reproduced rather than dropped — **perspective scaling**, so a world-unit font size still shrinks with distance and callers keep passing the sizes they always did, and **depth ordering**, so a near label paints over a far one, which UI Toolkit does not get for free without a depth buffer. What is *not* reproduced by default is occlusion by scene geometry; `OccludeBehindGeometry` restores it at the cost of one linecast per visible label per frame.
76. **Visual Effects** — 10 configurable effects: FadeIn, FadeOut, FloatUp, FloatRandom, Bounce, Pulse, ScaleUp, ScaleDown, Wave, Shake.  
77. **Billboard Component** — Makes GameObjects always face the camera (nameplates, health bars).  
78. **Cinematic Camera** — Camera movement along Unity Spline paths with LookAt target and user skip.  
79. **Floating Labels** — Damage, heal, achievement, and region name labels in world space.

### Scene Management
80. **Addressable-Based Scene Loading** — Scene preloading/postloading with progress tracking.  
81. **Template Cache Population** — Static permanent addressable loading.  
82. **Fog Transitions** — Scene fog changes during world transitions. `ClientFogManager` extracted for SRP compliance.  
83. **World Scene Tracking / Unloading** — Client-side world scene lifecycle management.  
84. **Postload Scene Lifecycle** — Reloads on quit-to-login, unloads on entering game world.  
85. **Death Broadcast Handler** — `DeathBroadcast` registered on client for reconnect-while-dead death dialog re-display.

### Naming & Resolution
86. **ClientNamingSystem** — ID-to-name and name-to-ID resolution for characters, guilds, pets with server queries and disk persistence (GZip binary).

### WebGL Support
87. **Browser Key Interception** — Prevents default browser actions (F12, Ctrl+W) during gameplay via JavaScript interop (`Assets/Scripts/Client/WebGL/WebGL.jslib`).  
88. **WebGL Quit** — Calls a JavaScript quit function via `Client.jslib`.

---

## FishMMO-Unity — Server

**The headless server** (FishMMO.Server assembly, 211 .cs files). Three server types — Login, World, Scene — launched from one GameServer executable.

### Core Server Infrastructure
1. **Server Composition Root** — `Server` MonoBehaviour orchestrates CoreServer, Database, NetworkWrapper, AddressProvider, AccountManager, BehaviourRegistry, DataContainerRegistry.  
2. **Config File Loading** — `FileServerConfiguration` loads/saves `.cfg` files with typed getters and defaults.  
3. **Server Lifecycle Events** — `IServerEvents` with delegates for LoginServer/WorldServer/SceneServer initialization.  
4. **Periodic Callback System** — `IPeriodicUpdateSystem` for registering/unregistering configurable-interval per-frame callbacks.  
5. **Server Behaviour System** — ScriptableObject-derived modular server behaviours with unified InitializeOnce/Deinitialize lifecycle.  
6. **Server Component Registry** — Multi-interface lookup registry for all server components.  
7. **Runtime Data Containers** — Typed runtime data containers (`RuntimeDataContainer`) with `RuntimeDataContainerFactory` and `RuntimeDataContainerRegistry`; behaviours declare required containers via `[RequiresDataContainer]`. Per-system containers follow the `<System>SystemRuntimeData` / `I<System>SystemRuntimeData` naming convention — e.g. `PartySystemRuntimeData`/`IPartySystemRuntimeData`, `GuildSystemRuntimeData`, `CharacterSystemRuntimeData`, `ChatSystemRuntimeData`, `WorldSceneSystemRuntimeData`, `NamingSystemRuntimeData`. Shared infrastructure containers (`MainThreadQueueData`, `AsyncWorkerData`) are similarly split per system via marker interfaces such as `IGuildSystemMainThreadQueueData` so systems do not collide on one registry slot.  
8. **Main Thread Queue** — Thread-safe main-thread action queue for marshalling async worker results to the Unity thread. Each queued action is invoked in isolation and the drain buffer is cleared in a `finally`, so one throwing action cannot discard the rest of a batch — for a request/response handler the queued action *is* the reply, and the actions in a batch belong to unrelated connections. Capacity rejection is counted and rate-limit warned; callers holding state across the hand-off check the return value.  
9. **Async Worker Queue** — Centralized bounded async work queue with backpressure and entity-keyed ordering (FIFO per key via consistent hashing).  
10. **IngressGuard** — Per-connection, per-operation debounce and in-flight guard to prevent duplicate/replay/DoS attacks (ConcurrentDictionary-backed, bounded, periodic sweep). The two are tracked separately and swept on separate horizons: a debounce entry is reclaimed on the configured TTL but only while nothing is in flight for that key, and an in-flight marker is reclaimed only after `InFlightStaleAfter` (5 minutes) as a backstop against a missing `End()`. Sweeping them together meant any operation still running when its debounce entry aged out — a database stall is enough — silently lost its lock, so a duplicate could start and the first completion then released the *second* one's marker.  
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
20. **Signing Key KEK Provider** — Static utility loading AES-256 KEK from the `deployment_secrets` database table (key `signing_key_kek`), building 8-byte AAD bound to LoginServer ID, wrapping/unwrapping HMAC signing keys. No environment variable or .cfg file fallback.  
21. **Account Managers** — `AccountManager`, `SrpAccountManager`, `TokenAccountManager` wrapping FishMMO-Auth cores for Unity/FishNet.  
22. **Handshake Rate-Limit Window Lifecycle** — A disconnect clears only the `conn:{ClientId}` key, so a recycled ClientId (FishNet reuses them) does not inherit a stale 100 ms block. The IP-keyed window deliberately survives the connection: it is a property of the address, and clearing it on disconnect would let any client reset its own per-IP handshake budget by reconnecting. The stopped path never resolves an address-derived key either, because the transport has already dropped its id mapping and the lookup would make FishNet log `TransportIdData could not be found` on every disconnect. Login-queue admission still clears unconditionally — there the server is inviting a re-handshake on a live connection.  
23. **`requireTokenRealIp`** — Serialized on `TokenServerAuthenticator`, default **on**: an auth token must carry a verified real client IP. Correct behind the L4 proxy, where `conn.GetAddress()` is the proxy's loopback for every client and the token-embedded IP is the only key the handshake limiter can use. Disable only for a direct-connect deployment: the Login Server recovers a real IP solely from an IPFetch-issued connection token, and that token is optional at the handshake, so a stack without the proxy issues valid tokens carrying no IP — with the requirement on, every player is refused at world entry.

### LoginServer Features
22. **Login Server Registration** — Registers server in DB, generates/rotates HMAC signing keys (AEAD-wrapped via KEK), derives TOTP master key. Periodic heartbeat pulses.  
23. **Account Creation System** — Per-IP rate limiting with `ExpiringKeyTracker`, per-IP block after N failures, global hourly account creation cap (DoS shield), per-username verification failure lockout (60 min after 5 failures). AES-256-GCM encrypted credential decryption, mandatory TOTP 2FA setup with encrypted secret storage, recovery code generation/hashing, encrypted otpauth URI delivery.  
24. **Character Create System** — Template-validated character creation with starting equipment/abilities/hotkeys initialization, `MaxCharacters` per account enforcement.  
25. **Character Select System** — Character listing, selection, and deletion for player accounts.  
26. **Server Select System** — World server list provisioning from database.  
27. **Login Queue System** — `LoginQueueSystem` (`ServerBehaviour`) holds a FIFO queue, backed by `ArrivalOrderTracker<TKey>` for O(1) add/remove by connection, for clients arriving while the server is at authentication capacity. Queued clients stay connected at the QUIC layer and receive `LoginQueuePositionBroadcast` position updates every `LoginQueueUpdateRateSeconds`; on reaching position 0 the client re-initiates the handshake (after 0-1s of jitter, so a drained queue does not arrive at the SRP channel in lockstep) and proceeds through normal auth. That retry runs on the connection the client has been holding open, so it resets the per-connection crypto state via `ClientAuthenticatorCore.OnRehandshakeRequired()` — **not** `OnDisconnected()`, which would also clear the credentials SRP has not consumed yet and make the re-handshake disconnect itself. The queued connection is exempted from the handshake-timeout sweep by `IsConnectionAwaitingQueueAdmission` (covering the admitted-but-not-yet-re-handshaked window via `recentlyAdmitted`, 15s TTL), and its handshake rate-limit window is cleared on enqueue so the server-invited retry cannot trip it. Admission is rate-smoothed via `LoginQueueAdmissionRatePerSecond` so newly admitted clients cannot immediately re-saturate auth capacity. Clients beyond `LoginQueueMaxSize` are rejected with `ClientAuthenticationResult.ServerBusy` (`ServerBusyBroadcast`) rather than queued; clients exceeding `LoginQueueTimeoutSeconds` receive position -1 and are disconnected. All parameters are server-authoritative.

### WorldServer Features
28. **World Server Registration** — DB registration, periodic heartbeat with character count.  
29. **World Server Authenticator** — Token auth with per-account login debounce, server-lock check, combined admission gate (DB connection count + recently admitted usernames burst prevention), selected-character validation.  
30. **World Scene System** — Open world and instanced scene routing, connection authentication, instance lookup with debounce and TTL caching, waiting queue management with TTL purge, DB updates. Routing keys every map by **scene row ID** rather than by the hosting process's scene-manager handle (see *Scene instance identity*), so two scene servers hosting the same scene are never collapsed into one entry. Open-world routing accepts only `SceneType.OpenWorld` rows — `FetchAvailableAsync` selects on world, name, capacity and `Ready` and says nothing about type, so without the filter a `Group` row for a scene also reachable as a teleporter's `ToScene` or a character's `BindScene` would drop the player into somebody else's private instance. Two periodic reapers keep the routing pool honest: `DeleteStaleUnreadyAsync` removes rows that never reached `Ready` (nothing else does — a `Loading` row orphaned by a scene server that died between dequeue and load still has `scene_server_id = 0`, so that server's own restart cleanup never matches it), and `DeleteByStaleSceneServersAsync` removes `Ready` rows whose host has stopped pulsing, then clears the routing caches so a cache hit cannot keep sending players to them. Clients held in the routing queue receive `WorldSceneQueuePositionBroadcast` every `queuePositionUpdateRateSeconds`, carrying their 1-based position within their own scene or instance group, the group size, an estimated wait derived from how many connections the last routing pass actually placed, and a `WorldSceneQueueReason` (`Capacity`, `SceneLoading`, `CombatLogoutBody`). Position semantics and channel selection mirror `LoginQueuePositionBroadcast`: `>0` waiting (Unreliable, corrected by the next sweep), `0` routed and `-1` abandoned (both Reliable, as one-shot transitions). Each reason carries its own bound — `waitingQueueTtlSeconds` for capacity, `× SceneLoadWaitTtlMultiplier` while a scene instance is still loading, and `CombatLogoutRoutingGraceSeconds` for a character whose combat-logout body only one instance can hand back. Connections are ranked across the whole group but notified only after waiting longer than one full routing cycle, so a healthy login never sees the dialog. The wait-queue TTL measures the total wait: the arrival stamp survives re-queue cycles and is cleared only by a terminal outcome (routed, purged, disconnected).  
31. **Kick Request System** — Periodic DB polling for admin-initiated kicks, player disconnection via main-thread marshalling. Kicks are delivered with `DisconnectWithNotice(AdministrativeKick, terminal: true)` rather than `NetworkConnection.Kick`, which FishNet does not relay: the player is told they were disconnected by an administrator, and the `Terminal` flag stops the client's reconnect loop from spending ten attempts on a server that will refuse it again. Stale requests are skipped by comparing the account's last successful login against the request timestamp.

### SceneServer Features
32. **Scene Server Registration** — DB registration, periodic heartbeat pulses with scene character counts.  
33. **Scene Loading/Unloading** — FishNet SceneManager orchestration, pending scene queue processing from DB, stale scene cleanup. Scene instances are identified across processes by their `scenes` row ID; the local scene-manager handle is kept only for the two places that need it (`SceneInstanceByHandle` for unload callbacks, and the scene manager itself) and is never persisted or sent to another process. `SetReadyAsync` and `PulseAsync` address the row the server actually dequeued rather than "the oldest loading row with this name", so two concurrent loads of one scene cannot stamp their host onto each other's row — which for an instanced scene handed a character the instance created for somebody else, because `character_id` stays with the row. A scene server dequeues from a global pending queue and therefore hosts scenes for **every** world server, so world-scoped bookkeeping (population maps, cache keys, failed-load kicks) is keyed by world server ID as well as scene name.  
34. **Character System** — Full lifecycle: loading from DB, spawning, periodic saves (configurable interval), despawning, disconnect cleanup. Session ownership with claim/release and lease refresh. Teleportation, out-of-bounds checks, death/respawn. Three watchdogs bound the scene-entry path end to end so a stalled load cannot present as a hang: residency (`CharacterResidencyTimeout`, 60s — armed on the auth callback, cleared once the character reaches a mapping table), scene handshake (`SceneLoadHandshakeTimeout`, 90s — armed on `WaitingSceneLoadCharacters`, cleared by `ClientValidatedSceneBroadcast`), and transfer (`TransferDisconnectGrace`, 15s). The per-account auth-callback rate limit disconnects with `RateLimited` rather than returning silently, because that callback is the only entry point to a character load. Every per-connection watchdog map is cleared in `OnDeinitialize`, since the behaviour is a `ScriptableObject` whose fields outlive an editor play-session restart while FishNet reissues `ClientId`s from zero. The scene handshake itself is **order-independent**: `ClientValidatedSceneBroadcast` needs both the client's start-scene acknowledgement (raised once per connection, one round trip after authentication — before the character load has even started) and the character reaching `WaitingSceneLoadCharacters` (several database round trips later), and the server does not control which arrives first. `startScenesAckedClientIds` records the acknowledgement instead of acting on it, and whichever half lands second calls `ValidateSceneAndAcknowledge`; exactly one does, because both run on the main thread. Driving the handshake off the event alone made world entry a race that disconnected a healthy login with `CharacterUnavailable` whenever the acknowledgement won — which, under any database latency, is the common case.  
35. **Combat-Logout Linger** — A character whose owner disconnects mid-combat keeps its body in the world, targetable and killable, for up to `combatLogoutLingerSeconds` (60s) instead of being despawned — so closing the client is not a free escape from a losing fight. `TryBeginCombatLinger` removes ownership (taking the object out of `connection.Objects` before FishNet's disconnect cleanup despawns it), cancels any in-flight ability (a lingering body is still ticked, and `AbilityController.OnReplicate` would otherwise re-assert `IsHeld` and let a cast complete on behalf of a player who cannot aim or stop it), sets the persisted `IsCombatLogged` flag, and re-adds the body to its scene instance's population count so the scene is neither unloaded as empty nor advertised as having free capacity. The session claim is held for the whole linger, so no other server can load a second copy. The linger ends on combat ending, on death, or at a hard `ExpiresUtc` deadline — the last of which stops an attacker pinning a body indefinitely by chipping at it. `AnyOnlineAsync` skips `IsCombatLogged` characters so the owner can log back in and reclaim the body via `TryReattachLingeringCharacter`, which carries the existing token through rather than releasing and re-claiming. Bodies are included in the periodic save (`AppendLingeringCharacterSnapshots`), paired with the claim this server holds so the writes stay ownership-gated: without that they were persisted only at the two ends of the linger, and a scene server that died in between restored the character at full health, refunding the fight it had already lost.  
36. **Character Inventory System** — Item moves, swaps, splits across inventory, equipment, and bank containers. Persists changes to DB.  
37. **Equipment System** — Equip/unequip with slot validation.  
38. **Bank System** — Bank/storage slot management.  
39. **Chat System** — Local (proximity), World, Party, Guild, and Private (whisper/tell) chat channels. Lock-free incoming queue with O(1) size counter. Batch DB persistence. Token-bucket rate limiting. Region chat is delivered to the sender's **actual scene instance**, taken from the spawned object, not resolved by scene name — scene stacking means several instances share a name, and inside an instance `SceneName` still names the open-world scene the character will return to. The sender is resolved from `ConnectionCharacters` rather than `conn.FirstObject`, so a command's authorisation is never decided from a network-deserialised payload.  

40. **Slash Command Routing** — `ChatHelper.GetCommandAndTrim` returns the command **including** its leading slash, which is how every registration is keyed (`/leaveinstance`, `/gi`, `/pi`, `/w`, `/guild`, …). Lookups are case-insensitive. Registrations are removed on teardown and the channel map is reset, because the registry is static and holds delegates bound to `ScriptableObject` behaviours that do not survive a play-session restart. A repeated slash command is exempt from the duplicate-message filter on both client and server — repeating one is the normal response to a command refused for a reason that has since cleared.  
41. **Guild System** — Creation, membership, ranks, invitations (TTL-expiring), periodic update pump, character connect/disconnect tracking.  
42. **Party System** — Creation, membership, invitations (TTL-expiring), character connect/disconnect tracking.  
43. **Friend System** — Add/remove friends with validation, online status tracking, `MaxFriends` enforcement.  
44. **Achievement System** — Progress tracking, completion events, reward delivery.  
45. **Quest System** — Event handling, auto-progression, reward delivery, DB persistence.  
46. **Pet System** — Summoning, following, staying, releasing, AI initialization, death handling.  
47. **Hotkey System** — Player hotkey configuration for abilities/items with ingress debounce protection.  
48. **Naming System** — Character/guild ID ↔ name resolution with bounded TTL caches and negative caching.  
49. **Interactable System** — NPC interaction, merchant purchases (items/abilities), ability crafting, world containers, server-authoritative dialogues (ECA-driven), dungeon finder entrance, mailbox (send/receive/delete mail). The **dungeon finder** reuses the character's own instance when its scene row is still usable (`Ready`, `Pending` or `Loading` — a `Failed` row is treated as absent, since routing to one cost a full disconnect and, because nothing deleted it, made that dungeon permanently unenterable for that character), otherwise joins a party member's instance so a group ends up in one dungeon, otherwise requests a new one. Instance population is capped at the scene's `MaxClients`: a full instance is **refused** with `DestinationFull` rather than falling through to "create a new one", which would silently split a party from the instance it was trying to join. Entry is gated by `CanActOrMove` on both the request and the authoritative re-check after the async database work, and the hand-off is announced via `BeginDeliberateTransfer` so the disconnect that performs it is not mistaken for a combat logout.  
50. **Faction System** — Faction relationship management.  
51. **Scene Channel System** — Open-world channel listing and channel switching. The list aggregates every `Ready`, `OpenWorld` instance of the character's scene on its world server — including instances hosted by *other* scene servers — via `ISceneService.FetchAvailableAsync`, resolving each host's address through a TTL cache keyed by **world server ID and scene name** (a scene server serves every world server, so a key of scene name alone served one world's channel list to another world's players). Switching is gated by `CanActOrMove`, so it is refused while dead, teleporting, frozen, mid-load or **in combat** — a channel switch is otherwise a cleaner escape than a teleporter, landing the player on an instance their attacker is not in. The destination is re-validated against the database and the character's state re-checked on the authoritative side before the transfer commits, and the transfer itself is handed to `ICharacterSystem.BeginChannelTransfer` so the departure is announced (and the scene population debited) while the character still belongs to the instance it is leaving. Every refusal is named through `SceneTransferRefusedBroadcast` rather than returning silently.  
52. **Persisted Channel-Switch Cooldown** — The rate limit lives on the character row (`characters.last_channel_switch_utc`), claimed atomically by `ICharacterService.TryBeginChannelSwitchAsync` in a single check-and-stamp statement. A switch is implemented as a disconnect, so the client returns through the world server on a fresh connection id and quite possibly to a different scene server: any cooldown held per connection — or even per process — is erased by the very action it is meant to limit, and only ever delayed retries after a switch that had already been *refused*. The per-connection dictionary is retained purely as a cheap pre-check so a spamming client does not reach the database. The claim is taken *after* the destination is validated, so a player is not put on cooldown for asking about a channel that turned out to be full or gone.  
53. **Leave Instance** — `RequestLeaveInstanceBroadcast` and the `/leaveinstance` (`/exitinstance`) chat commands remove a character from instanced content and return it to the open world at its recorded `LastWorldPosition`. This is a system-level guarantee rather than content data: a dungeon is expected to provide an exit teleporter, but a character bound to an instance is routed back into it on every login, so a dungeon authored without a reachable exit would otherwise strand its occupants permanently. Available as a command as well as a broadcast precisely so the escape hatch does not depend on a client UI having been authored. Gated by `CanActOrMove` like every other voluntary transfer, so it is not a combat escape, and it performs the same ordered release (announce, rebind, save, release, disconnect) as the bind-point respawn.

### Operator Control
54. **Server Lock (drain)** — `world_servers.locked` and `scene_servers.locked` are the authority for whether a server accepts new arrivals; each process reads its own row back on every pulse and adopts it, and registration preserves the column so a restart does not silently undo a maintenance lock. A locked world refuses logins **except for accounts above `AccessLevel.Player`**, so locking a world for maintenance does not lock out the people doing it. A locked scene server stops dequeuing scene-load requests and is skipped by the world server's open-world routing (`IsSceneServerRoutable`). Instance routing deliberately ignores the lock — a character bound to a dungeon can only go to the one server hosting it, so refusing there would evict them from their instance rather than drain them. Players already online are unaffected either way.
55. **Scheduled Maintenance Shutdown** — `shutdown_at_utc` (absolute UTC, so every process counts down to the same instant) schedules a world or scene server to stop, and locks it in the same statement. Scene servers read the control state of every world they host scenes for, warn the affected players as the countdown crosses 15m/10m/5m/2m/1m/30s/10s, then disconnect them with a terminal `ServerMaintenance` notice one tick before the process stops so the notice actually flushes. Cancelling a shutdown deliberately does **not** unlock: halting a shutdown and reopening to players are separate decisions. A scene server clears its own consumed shutdown and lock as it exits, so an automatic restart does not stop again immediately.
56. **In-Game `/admin` Commands** — `status`, `lockserver`/`unlockserver`, `shutdown <seconds>`/`stopshutdown`, `lockscene`/`unlockscene`, `shutdownscene <seconds>`/`stopshutdownscene`, all requiring `AccessLevel.Admin`. Registered as a single `/admin` command with sub-command dispatch, so one access check covers every operation and no sub-command can be added that forgets to be gated. Commands write the database row and return; each process adopts it on its next pulse, which is how one command typed on one scene server reaches the world server and every other scene server under it.
57. **Per-Command Access Levels** — `ChatHelper` registers each slash command with a minimum `AccessLevel`, enforced against the character's own level as loaded from its database row (never from anything the client sends). A command the sender may not run is *consumed* rather than rejected: it is neither executed nor echoed to a channel, and the response is indistinguishable from an unknown command, so command names cannot be probed. Every refusal is logged with the character and account that attempted it.

### Server Authority & Security
58. **CharacterStateValidation** — Centralized static validation gate for all broadcast handlers. `CanAct()` rejects dead, teleporting, frozen, and unloaded characters. `CanActOrMove()` additionally rejects in-combat characters. `TryGetPlayerAndValidate(conn, out player)` canonical pattern resolves player from connection and validates in one call. Called at entry of every state-mutating broadcast handler.
59. **Comprehensive CanAct Coverage** — All server-side broadcast handlers validated: CharacterInventory (6 ops), Bank (2 ops), Equipment (2 ops), Quest (3 ops), Guild (7 ops), Party (7 ops), Friend (2 ops), Pet (4 ops), Hotkey (2 ops), Chat, Interactable. Movement pipeline also gated server-side in KCCPlayer.OnReplicate.
60. **Per-Account Rate Limiting** — Auth callback rate limit keyed by account name (not ClientId), preventing multi-connection bypass. Separate per-connection rate limit for scene unload broadcasts.
61. **Respawn/Resurrect IngressGuard** — Per-operation IngressGuard (2s debounce) on respawn-at-bind-point and resurrect-accept handlers. Prevents spam and concurrent-operation races.
62. **TCP/TLS Transport Encryption** — All network traffic encrypted at transport layer.

### Observer LOD System
63. **HashGrid Spatial Partitioning** — FishNet `HashGrid` component on the SceneServer scene's NetworkManager (`_accuracy: 70`). O(1) hash-based proximity: objects in the same or adjacent grid cells are "nearby." **Note:** `_gridAxes` is currently serialized as `0` = `XY`, not `XZ` — for a horizontal-plane world this is very likely a misconfiguration and should be reviewed.
64. **Global Observer Conditions** — The scene's `ObserverManager` `_defaultConditions` list holds exactly two assets: FishNet's stock `SceneCondition` (never observe cross-scene) and `GridCondition` (spatial hash pre-filter), applied to all `NetworkObject`s.
65. **Tiered Distance Conditions (authored, not yet wired)** — Four `DistanceCondition` ScriptableObjects live in `Assets/Settings/ObserverConditions/`: `PlayerDistanceCondition` (100m, `_hideDistancePercent` 0.1), `MonsterDistanceCondition` (50m, 0.15), `InteractableDistanceCondition` (30m, 0.1), `WorldItemDistanceCondition` (15m, 0.2). They are plain assets — there is no "FishMMO → Create Observer Distance Conditions" editor menu — and as of this revision **no prefab or scene references any of their GUIDs**, so no `NetworkObserver` currently applies them. Wiring them onto the relevant prefabs is outstanding work.
66. **Bandwidth Reduction (projected)** — With the full observer condition setup applied, per-client observer bandwidth is projected to drop from ~256 KB/s to ~43 KB/s (83% reduction), and server aggregate outbound from ~25.6 MB/s to ~4.3 MB/s for 100 players + 100 NPCs. These are design targets for the completed setup, not measurements of the current wiring (see 58).

---

## FishMMO-Unity — Shared

**The shared entity and logic layer** (FishMMO.Shared assembly, 585 .cs files). Used by both client and server, containing all entity definitions, the ECA trigger system, templates, network broadcasts, and prediction pipeline.

### Character System
1. **ICharacter / IPlayerCharacter Interfaces** — Root character contracts: ID, name, transform, collider, network object, prediction manager, observers, flags, behaviours, triggers.  
2. **BaseCharacter** — Abstract NetworkBehaviour implementing ICharacter: behaviour registry, bitwise flag management, ECA trigger invocation, race model instantiation (Addressable), client character dictionary.  
3. **PlayerCharacter** — Concrete player class requiring 13+ behaviour components (attribute, target, cooldown, inventory, equipment, bank, ability, achievement, buff, quest, damage, guild, party, friend, faction controllers). KCC movement, chat anti-spam token bucket.  
4. **CharacterBehaviour** — Abstract base for modular behaviour components: InitializeOnce, OnStartCharacter, OnStopCharacter lifecycle.  
5. **CharacterFlags** — Bitwise state flags: Idle, IsMoving, IsRunning, IsCrouching, IsSwimming, IsTeleporting, IsFrozen, IsStunned, IsMesmerized, IsInInstance, IsLoaded, IsDead, IsInCombat, IsCombatLogged. `IsInCombat` is transient and is stripped on both save and load; `IsCombatLogged` is the one combat-related flag that **is** persisted, because it is what lets the login path tell "this account is playing elsewhere" (which must block a second login) from "this account owns a body running out its combat-logout timer" (which must not, or the player could never reclaim it). `IPlayerCharacter.IsInInstance()` additionally requires `InstanceSceneName` to be set, so the flag alone does not make a character instanced — the name is resolved from the instance's scene row during the load.
6. **Combat State System** — Tick-aligned combat timer on `CharacterDamageController`. Enters combat on dealing damage, taking damage, or healing an in-combat ally. Auto-clears after configurable duration (default 600 ticks / 20s at 30Hz) of inactivity. `EnterCombat()` safe to call repeatedly — refreshes expiry. `IsInCombat` flag cleared on death and network reset. Combat state prevents teleportation (combat-escape prevention).
7. **Combat-Escape Prevention** — Teleport blocked while `IsInCombat` flag is active. Movement gate in `KCCPlayer.OnReplicate` rejects input from dead, frozen, teleporting, unloaded, and in-combat characters.

### ECA Trigger System (Entity-Component-Action)
*The data-driven trigger/action pipeline powering abilities, quests, dialogue, interactables, and game events.*

8. **Trigger System Core** — `Trigger` ScriptableObjects with `TargetSelector` + `Conditions` + `OnConditionsMetActions` + `OnConditionsNotMetActions`. Fault isolation (throwing actions caught/logged).  
9. **EventData** — Typed event context container: Initiator, Target, TargetCharacter, RNG, ConditionFilter. Supports typed sub-payloads, forking, merging.  
10. **Polymorphic Serialization** — All actions/conditions/selectors use `[SerializeReference]` + `[SubclassSelector]` for designer-authored Inspector workflows.

#### ECA Actions (52 implementations)
11. **Combat Actions** — ApplyDamage, ApplyHeal, ApplyRevive, ApplyBuff, ApplyDispel, ConsumeResource, Interrupt, KnockbackHit.  
12. **Ability Actions** — AbilityApplyArea, AbilityApplyTarget, AbilityForkHit, AbilityHitCount, AbilityMoveTransform, AbilityPierceHit, AbilitySpawnMultiply.  
13. **Item Actions** — EquipItem, UnequipItem, GiveItem, RemoveItem. (Equip/Unequip `#if UNITY_SERVER` guarded — persistent state mutations never run during prediction replay.)  
14. **Quest Actions** — AcceptQuest, AbandonQuest, AdvanceQuestObjective, CompleteQuest, FailQuest, TurnInQuest.  
15. **Interactable Actions** — Bindstone, GatheringNode, LoreObject, NPCLookAtInteractor, PickupWorldItem, SendAbilityCrafterBroadcast, SendBankerBroadcast, SendContainerOpenBroadcast, SendDungeonFinderBroadcast, SendMailboxBroadcast, SendMerchantBroadcast, SendQuestOffer, Shrine, Switch, Teleport. `BindstoneAction` refuses to bind inside an instance — `BindScene`/`BindPosition` are consumed by the respawn-at-bind path, which hands the character to open-world routing, so a `BindScene` naming a dungeon is a trap the character carries until it binds elsewhere — and matches the bindstone's scene by **handle**, since scene stacking means several instances of one scene are loaded at once and share a name.  
16. **Region Actions** — ApplyRegionAttribute, ApplyRegionBuff, ChangeFog, ChangeSkybox, DisplayRegionName, PlayRegionAudio.  
17. **Utility Actions** — AchievementIncrement, AddFaction, ClearTarget, DestroyObject, DisplayDialogue, PlayFX. (DestroyObject `#if UNITY_SERVER` guarded; PlayFX/ClearTarget suppress during prediction replay via `IsReplicateTick`.)

#### ECA Conditions (30 implementations)
*There is no separate composite/AND-OR condition type. Every `BaseCondition` carries a `ConditionTargetCombine Combine` field (All/Any, default All) governing how per-target results aggregate, plus a universal `Invert` flag the framework applies in `Check()` — derived classes must not apply it themselves.*
18. **Combat/Attribute Conditions** — HasResource, HasRequiredAttribute, HasBuff, HasCooldown, HitCount, IsCharacterAlive, IsImmortal.  
19. **Equipment/Inventory Conditions** — CanEquipItem, HasEquippedItem, CanUseItem, HasInventoryItem, HasInventorySpace, HasBankItem, HasBankSpace.  
20. **Social/Progression Conditions** — HasGuild, HasParty, HasFaction, TargetAlliance, IsArchetype, IsRace, HasPet.  
21. **Quest Conditions** — CanAcceptQuest, HasQuest, QuestObjectiveComplete, QuestStatus.  
22. **Achievement Conditions** — AchievementCompleted.  
23. **Presence/Controller Conditions** — HasTarget, IsCharacterNPC, HasAttributeController, HasBankController.

#### ECA Target Selectors (13 types)
*All derive from the abstract `TargetSelector` base (`Conditions` list + `SelectTargets(EventData)`). Selectors are attachable on the `Trigger` itself and, optionally, per-condition and per-action for additional fan-out.*
24. **Basic** — `EventTargetSelector`, `InitiatorTargetSelector`, `NearestTargetSelector`, `FurthestTargetSelector`, `RandomTargetSelector`, `AllCharactersTargetSelector`.  
25. **Spatial** — `AreaTargetSelector`, `ConeTargetSelector`, `LineTargetSelector`, `ChainTargetSelector`.  
26. **Hierarchy** — `ChildrenTargetSelector`.  
27. **Named/Tagged** — `NamedSceneObjectTargetSelector`, `TaggedSceneObjectTargetSelector`.

#### ECA Value Providers (10 types)
28. `ConstantValue`, `ConstantFloatValue`, `RandomRangeValue`, `RandomRangeFloatValue`, `StatScaledValue`, `StatScaledFloatValue`, `DamageAmountValue`, `HealAmountValue`, `FactionAmountValue`, `QuestObjectiveAmountValue`.

### Item System
29. **Item Template Hierarchy** — `BaseItemTemplate` → `ConsumableTemplate` / `EquippableItemTemplate` → concrete: Potion, Scroll, Armor, Weapon.  
30. **Runtime Item** — `Item` with optional `ItemEquippable`, `ItemStackable`, `ItemGenerator` components.  
31. **Item Generation** — `ItemGenerator` using `DeterministicRNG` for seed-based stat rolls (AttackPower, AttackSpeed, ArmorBonus + random attributes from databases).  
32. **Item Attributes** — Template-driven attribute system with min/max values linked to CharacterAttributeTemplates.  
33. **Item Containers** — `IItemContainer` with slot locking, stacking, swapping. `InventoryController`, `EquipmentController`, `BankController` implementations.  
34. **Item Slots** — Head, Chest, Shoulders, Hands, Legs, Feet, Back, Primary, Secondary, Accessory (10 slots).

### Ability System
35. **Ability Templates** — `BaseAbilityTemplate` → `AbilityTemplate` / `PetAbilityTemplate` with ActivationTime, LifeTime, Speed, Cooldown, Price, RequiresTarget, HitCount.  
36. **ECA Ability Events** — OnTick, OnHit, OnPreSpawn, OnSpawn, OnDestroy — each with configurable ECA triggers.  
37. **Ability Activation State Machine** — Resource cost validation via `IResourceCost` conditions, activation queuing, consumable support, network sync.  
38. **AbilityObject** — Networked GameObject for projectiles/AoE with lifetime, collision, tick handling, and snapshot reconciliation.  
39. **Ability Knowledge System** — Learned abilities, base abilities, ability events, event subset tracking.  
40. **Cooldown System** — Tick-based immutable `CooldownInstance` with reconcile snapshots, static events for add/update/remove.

### Buff/Debuff System
41. **Runtime Buff** — Tick-based timing (ExpiryTick, NextTickTick), stack count, cumulative tick multiplier.  
42. **Buff Template Types** — AttributeBuff (flat stat modifier), AttributeTickBuff (per-tick modifier), ResourceTickBuff (DoT/HoT), StateBuff (stun/freeze/mesmerize), CompositeBuff.  
43. **Buff Reconciliation** — `BuffReconcileEntry` for deterministic rollback in the prediction pipeline.

### Character Attribute System
44. **Three-Tier Value System** — baseValue + formulaModifier + externalModifier = finalValue. Parent/child dependency graph with formula propagation.  
45. **Resource Attributes** — `CharacterResourceAttribute` extends with currentValue (health/mana/stamina), clamping, regeneration.  
46. **Attribute Formulas** — Flat bonus and percentage bonus formulas with dependency tracking.  
47. **Propagation Batching** — Deferred notifications with suppression for replay performance.  
48. **Tick-Driven Regeneration** — Monotonic guard against double-advance.  
49. **Damage System** — `CharacterDamageController`: damage, healing, kill, resurrection, combat state management with full ECA trigger invocation. Client+server deterministic prediction (Damage/Heal/Revive run on both sides; Kill server-only for non-deterministic side effects). Healer enters combat when healing an in-combat ally.
50. **Damage Types & Resistances** — `DamageAttributeTemplate` (physical, fire, frost, etc.) and `ResistanceAttributeTemplate` pairing.  
51. **Death System** — Player death shows dialog with Respawn/Resurrect options. NPC corpse decay timer (configurable per spawner). `ResurrectOfferBroadcast`/`ResurrectAcceptBroadcast`/`RespawnAtBindPointBroadcast`/`DeathBroadcast`. Reconnect-while-dead re-shows death dialog.  
52. **Revive** — `Revive(ICharacter, int)` works on dead characters (unlike Heal). Fires `OnResurrected` static event, resets death animation, fires ECA resurrect triggers.

### Client-Side Prediction Pipeline
53. **Unified Prediction Controller** — `CharacterPredictionController` discovers all `IPredictableController` components, stable-sorts by Order with deterministic type-name tiebreaker, drives a single FishNet Prediction V2 pipeline.  
54. **Participating Subsystems** — KCC movement (Order 80), BuffController (85), CooldownController (90), EquipmentController (93), CharacterAttributeController (95), AbilityController (100).  
55. **Type-Safe Ticks** — `PredictionTick` struct prevents accidental raw tick usage.  
56. **Delta Compression** — `CharacterReconcileDataDeltaSerializer`, `CharacterAttributeResourceStateSerializer`, KCC motor state delta serializer for bandwidth-efficient sync (~43 bytes/tick typical, ~86 bytes/tick combat).  
57. **Deterministic RNG** — xoshiro128** algorithm with full 128-bit state capture in reconcile data. All prediction-path code uses `DeterministicRNG` — zero `UnityEngine.Random` or `System.Random`.  
58. **Shared Speed Enforcement** — `MaxAllowedSpeed = SprintSpeed × 3.0f` runs identically on client and server in shared code. No server-only branches.  
59. **Motor PhysicsScene Init** — `KCCPlayer.Awake` initializes motor's `PhysicsScene` from GameObject scene, ensuring client collision queries (ground detection, wall collision) work identically to server.  
60. **Deterministic Ability Math** — `Math.Ceiling(double)` replaces `Mathf.CeilToInt(float)` for platform-independent activation time rounding (prevents x86/ARM one-tick mismatches).  
61. **Physics Query Guards** — All ECA target selectors (`AreaTargetSelector`, `ConeTargetSelector`, `LineTargetSelector`, `ChainTargetSelector`, `NearestTargetSelector`, `FurthestTargetSelector`, `RandomTargetSelector`) and `AbilityApplyAreaAction` suppress physics queries during prediction replay via `IsReplicateTick` guard.  
62. **Reconcile Delta Efficiency** — Delta serializer sends only changed fields. Idle tick: ~20 bytes. Walking: ~43 bytes. Combat: ~86 bytes. Full struct: ~223-1300 bytes raw → 95-97% reduction.

### AI System (NPC)
63. **State Machine** — `BaseAIState` subclasses: Idle, Wander, Patrol, ReturnHome, Retreat, MeleeAttacking, RangedAttacking, CasterAttacking, HealerAttacking (via `BaseAttackingState`), GetBehind, Orbit, PetIdle, BossScript, plus `AggressionState`.  
64. **Behavior Tree** — `AIBehaviorTree` of `AIBehaviorNode`s: `AISelector`, `AISequence`, `AIInverter`, `AIRepeater`, `AICompositeNode`, `AIConditionNode`, plus game-specific leaves `AIHasTargetNode`, `AIIsDeadNode`, `AIGroupInCombatNode`, `AIAdoptGroupTargetNode`, `AIStateTransitionNode`.  
65. **Group Combat** — `NPCGroup` with roles, pack tactics, aggression management.  
66. **Boss Mechanics** — `BossPhase`, `BossScript`, `BossTimedMechanic`.  
67. **Navigation** — NavMeshAgent-based with waypoints, avoidance priorities, LOD settings.  
68. **Deterministic RNG** — Seeded per-NPC for reproducible behavior.  
69. **Ability Rotation** — `AIAbilityRotation` for combat ability selection.  
70. **Combat Personality** — `AICombatPersonality` configuration for varied NPC combat styles.

### Interactable System
71. **16 Interactable Types** — All derive from `Interactable : NetworkBehaviour, IInteractable, ISpawnable`: `AbilityCrafter`, `Banker`, `Bindstone`, `CapturePoint`, `Container`, `DialogueInteractable`, `DungeonEntrance`, `GatheringNode`, `LoreObject`, `Mailbox`, `Merchant`, `QuestInteractable`, `Shrine`, `Switch`, `Teleporter`, `WorldItem`.  
72. **Base Interaction — ECA-Authored, No Handler Plugins** — `InteractionRange` 3.5u default, `INTERACT_RATE_LIMIT` of 60ms (overridable per type via `InteractRateLimit`). Behaviour is authored entirely as a `List<Trigger> OnInteractTriggers` on the interactable prefab and fired via `IInteractable.ExecuteOnInteract(EventData)`. There is **no** server-side handler-plugin architecture — no handler interface, no registration attribute, and no handler initializer exists anywhere in the codebase.  
73. **Server-Side Validation** — The server's `InteractableSystem` validates the scene, runs `ValidateSceneObject` against the character's scene handle, resolves the `IInteractable` component, checks `CanInteract()` (which covers `InRange()` and the rate limit), then invokes `ExecuteOnInteract` with a `PlayerInteractionEventData` — all inside an `IngressGuard`.  
74. **Capture Points** — PvP capture points with state machine (`CapturePointTemplate`, `ObjectiveState`).  
75. **Dialogue Trees** — `DialogueTemplate` with `DialogueNode`/`DialogueChoice`, server-authoritative session management with choice bitmasks.  
76. **Gathering Nodes** — Harvesting with `GatheringDrop` drop tables, cooldowns, remaining uses (`GatheringNodeTemplate`).  
77. **Merchant Tabs** — Categorized merchant inventory tabs (`MerchantTabType`, `MerchantTemplate`).

### Faction System
78. **Faction Standing** — Per-faction integer standing with Allied/Neutral/Hostile classification.  
79. **Faction Matrices** — Template-driven faction relationship matrices with editor tooling.

### Quest System
80. **Quest Lifecycle** — Inactive → Active → Complete → TurnedIn / Failed.  
81. **Objective Tracking** — Per-objective progress with required amounts.  
82. **Attribute Requirements** — Pre-requisite attribute checks before acceptance.

### Social Systems
83. **Friends** — Friend list management with online status.  
84. **Guilds** — Membership, invites, ranks, join/leave ECA triggers.  
85. **Parties** — Creation, invites, member tracking, leader ranks.

### World System
86. **World Scene Details** — Per-scene configuration: max clients, spawn/respawn positions, teleporters, boundaries.  
87. **Day/Night Cycle** — Configurable cycle durations, skybox transitions, object activation/deactivation, material alpha fading, ECA triggers for day/night transitions.  
88. **Spawner System** — Linear/Random/Weighted spawning with respawn conditions (OR/AND), initial/max counts, pooling (`ObjectSpawner`). NPC corpse decay with per-spawner override. Re-rolled attributes on each spawn.  
89. **Teleporter System** — Cross-scene and same-scene teleportation with cached destinations.  
90. **Region System** — Zone definitions for area effects (fog, skybox, audio, buffs, attributes, region name display).  
91. **Scene Boundaries** — Terrain and custom boundary definitions.

### Character Appearance & Visual Equipment
92. **Modular Character System** — One shared humanoid skeleton, one Animator, one animation library for all races and equipment.  
93. **Body Region System** — Body mesh split into 6 hideable regions (Head, Torso, Arms, Hands, Legs, Feet). `BodyVisibilityManager` with per-slot reference counting for overlapping equipment hides.  
94. **Character Customization** — Bone scaling for Height, ArmLength, LegLength, TorsoLength, ShoulderWidth, HeadScale. Race presets (Human/Dwarf/Elf). Blend shapes for Weight, MuscleMass, ChestSize, WaistSize.  
95. **Equipment Visuals** — `EquipmentVisualController` with persistent renderer pool (no Instantiate/Destroy spam). Loads prefabs via Addressables, extracts mesh + materials, binds to skeleton via `SkeletonBinder.BindMeshKeepParent`.  
96. **Weapon Attachment** — Weapons as `MeshRenderer` children of bone transforms (RightHand, LeftHand). Follow animations automatically. Scale-independent from body proportions.  
97. **Equipment Mesh Variations** — `EquippableItemTemplate.EquipmentMeshes` list with seed-based selection via `ModelPools`/`ModelSeed`.  
98. **SkeletonBinder** — Bone name matching with caching. Generation-based cache invalidation for instance ID recycling safety.  
99. **Animation System** — `CharacterAnimationController` with Speed, IsGrounded, IsCrouching, Jump, Attack, Block, Roll, Cast, Death, RootMotion. FishNet `NetworkAnimator` integration.  
100. **Ability Animation** — `TriggerAbilityAnimation` maps `AbilityType` to animation: Physical→Attack, Magic→Cast, Block→SetBlocking, Roll→TriggerRoll. Death animation suppresses all other state.

### AI Threat System
101. **Threat Table** — `AggressionController` with damage, healing, resource expenditure threat. Configurable weights per category.  
102. **Vulnerability Scoring** — Low-health targets (<30%) get 1.5x threat multiplier. Low-mana targets (<20%) get 1.3x multiplier. AI intelligently pressures weakened enemies.  
103. **Replay-Safe Events** — `AggressionState.IsSpawnedAndAuthoritative()` guard prevents threat double-counting during client-side prediction replay.  
104. **Object-Pooled Aggression Entries** — Stack-based pool for `AggressionEntry` to avoid per-event allocations.

### Network Broadcasts (30+ types)
105. **Auth** — Authentication request/response, token sync.  
106. **Character** — Character data, abilities, achievements, archetype, factions, friends, guild, party, pet, quest, hotkeys.  
107. **Inventory** — Inventory, equipment, bank slot sync.  
108. **Character Create/Select** — Creation request/result, character details, delete.  
109. **Chat** — Chat messages with 10 channels (Say, World, Region, Party, Guild, Tell, Trade, System, Command, Discord).  
110. **Interactable** — Interactable state sync.  
111. **Naming** — Name reservation/release, ID ↔ name resolution.  
112. **Scene** — Scene loading, transitions, channel addresses (`ChannelAddress` identifies a channel by its `scenes.id`, never by a process-local handle), scene-routing queue positions (`WorldSceneQueuePositionBroadcast` with a `WorldSceneQueueReason`), voluntary-transfer refusals (`SceneTransferRefusedBroadcast` with a `SceneTransferRefusalReason`: `DestinationUnavailable`, `DestinationFull`, `CharacterStateChanged`, `OnCooldown`, `PartyInstanceExists`, `ServerError`), and the leave-instance request. Both voluntary transfers — a channel switch and a dungeon entrance — finish validating asynchronously after the client has already closed its own UI, so every refusal is named rather than returning silently; a silent refusal is indistinguishable from a lost request, and the obvious response (clicking again) is what the cooldown then swallows.  
113. **Server Select** — Server list and connection info.

### Bootstrap & Tools
114. **Bootstrap System** — Multi-environment asset/scene preloading (Editor, Standalone, WebGL), version management, graceful shutdown. Each phase enqueues its work and completes on its own `batch.Completed` signal rather than a shared global event.  
115. **Addressable Integration** — `AddressableLoadProcessor` for async prefab/sprite/mesh/scene loading with caching.  
116. **Per-Caller Load Batches** — `BeginProcessQueue()` returns an `AddressableLoadBatch` claiming exactly the items that caller enqueued, with its own `Completed` event, `Progressed` event, `TotalItems`/`CompletedItems`/`Progress`, and `FailedItems`/`HasFailures`. This replaces completion signalling through the processor's global `OnProgressUpdate` multicast delegate, which reported "done" to every bootstrap system and loading screen whenever *any* drain finished and could double-invoke subscribers that resubscribed during dispatch. `OnProgressUpdate` remains as a display-only progress feed. A batch counts an item finished whether it succeeded, failed, or was dropped — failures surface via `FailedItems` instead of withholding completion and stalling boot. Handlers subscribing after completion are invoked immediately, so a fully-cached batch that completes inside `BeginProcessQueue` cannot be missed.  
117. **Template Caching** — `CachedScriptableObject` with database-wide lookup and Addressable icon/mesh loading.  
118. **DeterministicRNG** — Reproducible random number generator for networked determinism.  
119. **SerializableDictionary / SerializableHashSet** — Unity-serializable generic collections with custom property drawers.  
120. **Version Management** — `VersionBuilder` with `VersionConfig` ScriptableObject; increments major/minor/patch, writes `version.txt` at build time.

### Editor Tools
121. **FishMMO Dashboard** — The single editor hub (`FishMMO > FishMMO Dashboard`, Ctrl+Shift+D), a UI Toolkit window whose panels are Build & Version, Categories, Game Settings, Inspector, and Patcher. Most FishMMO editor workflows are panels inside this window, not separate menu items. Backed by the custom build tool suite: AddressableManager, BuildConfigurator, BuildExecutor, LinkerGenerator. `BuildExecutor` additionally performs two post-build copies: `CopyRemoteAddressablesToBuild` stages `ServerData/[BuildTarget]/` bundles into the built player's `StreamingAssets/ServerData/[BuildTarget]/` for server builds (so `DynamicAddressableLoadPathSystem` can load them over `file://`), and `CopyUpdaterToBuild` copies the standalone Updater executable and its runtime dependencies into standalone client builds — without it the launcher's `Constants.Configuration.UpdaterExecutable` lookup fails and players are stranded on an unpatchable version. Both are skipped for build types that do not need them (server/WebGL for the updater).  
122. **Patch Generator** — `PatchGeneratorWindow` (`EditorWindow`) for creating delta patches between builds with manifest generation, surfaced through the Dashboard's **Patcher** panel (`FishMMODashboard.Patcher.cs`). It has no menu item of its own.  
123. **Addressables Dashboard** — Analysis, build, categorization, and tree view for addressable assets. Menu: `FishMMO > Addressables Dashboard`.  
124. **Behavior Tree Editor** — Visual editor for NPC behaviour trees. Menu: `FishMMO > Behavior Tree Editor` (spelled "Behavior").  
125. **Dialogue Tree Editor** — Visual editor for NPC dialogue trees. Menu: `FishMMO > Dialogue Tree Editor`.  
126. **World Scene Details Cache Builder** — Builds cached world scene details at edit time. Menu: `FishMMO > Rebuild World Scene Details`.  
127. **Custom Property Drawers** — `[ShowReadonly]`, `[SubclassSelector]`, `[TemplateReference]`, serializable dictionary drawers.  
128. **Build Option Toggles** — `FishMMO > Build > Build Type` (Client/Server), `> OS Target` (Windows x64 / Linux x64 / WebGL), and `> Environment` (Development/Production/Enable Local Directory), from `BuildEnvironmentOptions.cs` and `WorkingEnvironmentOptions.cs`. These set build options only — **they do not run builds**; builds execute from the Dashboard's Build & Version panel.  
129. **Security Assembly Filter** — Editor-only assembly filtering for security-sensitive code.  
130. **Version Menu** — `FishMMO > Version > Increment Major/Minor/Patch` drives `VersionBuilder`.  
131. **QuickStart Scene Menu** — `FishMMO > QuickStart > …` opens Main Bootstrap, Client Preboot/Postboot/Launcher, and Login/World/Scene Server scenes directly, ordered by priority.  
132. **Script Compilation Menu** — `FishMMO > Script Compilation > …` toggles Auto Refresh and selects recompile behaviour while in Play Mode (Recompile After Finished Playing / Recompile And Continue Playing / Stop Playing And Recompile).  
133. **AddressablesPlayModeSceneHandleFix** — Editor-only workaround for the "Attempting to use an invalid operation handle" exception Addressables throws from its own Play Mode teardown. `AddressablesImpl.Dispose()` releases each scene handle twice (once from `m_resultToHandle`, once from `m_SceneInstances`) with no `IsValid()` guard. Subscribes from `[InitializeOnLoadMethod]` so it runs ahead of the Addressables package's own handler, which our runtime shutdown path cannot do.

---

## FishMMO-WebServers

**ASP.NET Core web services** providing client-facing HTTP APIs.

### IPFetchASP.NET (Login Server Discovery)
1. **Login Server Discovery API** — `GET /loginserver` (`LoginServerController`) returns available login server ports from the database, cached in `IMemoryCache` with a 60s TTL plus jitter so a server pulled from rotation ages out quickly. Empty results are deliberately not cached, so a re-registering server is not masked by a 404 for the full window.  
2. **Stateless Connection Token** — Each response carries a token for real-IP recovery across the L4 UDP proxy (which loses the client IP). Format `base64url(payload).base64url(hmac)` where `payload = [keyId ':'] realIp '|' expiryUnixSeconds` and `hmac = HMAC-SHA256(sharedKey, payload)`; the client echoes it in its first `ClientHandshake` and the Login Server verifies the HMAC — no database round-trip. Expiry is 60 seconds. It is HMAC-**signed**, not a hashed one-time value. The optional `keyId` prefix lets multi-region game servers pick the right verification key; the signing key is registered in the `connection_token_keys` table as the sole discovery source. Keys shorter than 32 bytes are rejected at request time with a 500.  
3. **ClientGate** — Validates the `X-FishMMO-Client` HMAC-SHA256 header with multi-key rotation, a 30-second skew window (`MaxSkewSeconds`, re-checked post-HMAC), a 100,000-entry nonce cache (`NonceCacheCapacity`) with oldest-quarter eviction on overflow, and canonicalization that collapses repeated slashes and rejects traversal segments before signing.  
4. **Port Safety** — `WebServer:HttpPort` is read as a string (accepting both `"8080"` and `8080` in JSON) and validated with `int.TryParse` plus a 1–65535 range check; a malformed value throws at startup rather than silently falling back. Matches Patcher and WebGLServer behaviour. Kestrel binds via `ListenLocalhost` with no TLS — termination is NGINX's job.  
5. **CORS Defaults to Deny** — The `Public` policy reads `Cors:AllowedOrigins`; when unset it emits **no** `Access-Control-Allow-Origin` and logs a warning, denying cross-origin browser requests. Native `UnityWebRequest` clients ignore CORS entirely and the WebGL build is loaded same-origin, so operators must opt in explicitly for genuine cross-origin browser access.  
6. **Forwarded Headers, Single Hop** — `X-Forwarded-For` / `X-Forwarded-Proto` honoured with `ForwardLimit = 1`, since NGINX is the only trusted proxy; extra values would be attacker-controlled and would break per-IP rate limiting.  
7. **PascalCase JSON** — `PropertyNamingPolicy`/`DictionaryKeyPolicy` set to null so Unity's `JsonUtility` (exact-name matching) can deserialize responses without client-side rewriting.

### PatcherASP.NET (Patch Delivery)
8. **Latest Version Endpoint** — `GET`/`HEAD /latest_version?from={clientVersion}`. Without `from` it returns `{ latest_version }`; with `from` it returns `up_to_date: true`, or `patch_available: false` when no archive bridges that specific version pair, or `patch_available: true` with the patch's `sha256` and `size`.  
9. **Version Response Caching & Integrity** — Sets a weak `ETag` (derived from the patch hash / response shape) and `Cache-Control: public, max-age=30`; honours `If-None-Match` (comma-separated list, any match) with `304 Not Modified`. Adds `X-FishMMO-Version-Signature`, an HMAC over the canonical `latest_version=…` content so a compromised endpoint cannot silently substitute a patch hash.  
10. **Patch Download Endpoint** — `GET /{version}` serves patch ZIP files with range request support, `ReparsePoint` symlink rejection at serve time, and strong `ETag`/`Cache-Control: public, max-age=3600, immutable` on the artifact. Returns **`204 No Content`** when the requesting client is already on the latest version.  
11. **ClientGate** — Same HMAC request signing validation as IPFetch (`UseFishMMOClientGate`, with `/healthz` exempted).  
12. **Sliding-Window Rate Limiting** — Patch downloads limited to 6 permits/minute via a sliding-window partition (`[EnableRateLimiting("PatchDownload")]`), behind a global token-bucket limiter partitioned by client IP.  
13. **Symlink Protection** — `PatchVersionService` reindex skips `FileAttributes.ReparsePoint` files to prevent hash disclosure via symlinks.  
14. **Semantic Versioning** — `VersionConfig` with full SemVer 2.0.0 parsing, comparison operators, and `IComparable<VersionConfig>`.

### WebGLServerASP.NET (WebGL Static Server)
15. **WebGL Build Serving** — Serves Unity WebGL builds as static files (HTML, JS, WASM, `.unityweb`, `.data`) with correct MIME types and `X-Content-Type-Options: nosniff`.  
16. **Response Compression** — `AddResponseCompression` middleware with `application/wasm` and `application/octet-stream` MIME types for bandwidth reduction on large WASM builds (20–50 MB).  
17. **Cross-Origin Isolation** — CSP headers configured for `wasm-unsafe-eval` and WebTransport `connect-src` to `game.fishmmo.com:*`.  
18. **ClientGate** — Intentionally absent. Browsers cannot add custom headers to static resource requests, so HMAC request signing is not possible for WebGL. Rate limiting and CORS provide the security boundary.

---

*End of FishMMO Feature List*
