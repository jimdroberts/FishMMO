# FishMMO Connection Pipeline — Exhaustive Security & Production Audit

**Date:** 2026-07-23  
**Scope:** Entire connection pipeline — WebTransport transport layer, C# client/server auth, nginx reverse proxy, ASP.NET web services, PostgreSQL database, dependency tree, and deployment configuration.  
**Audited Files:** ~550 source files across 11 sub-projects.  
**Methodology:** Every file read in full; no partial reads. Each subsystem audited independently, then findings cross-referenced.

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Critical Issues (Must Fix Before Production)](#critical-issues)
3. [High-Severity Issues](#high-severity-issues)
4. [Medium-Severity Issues](#medium-severity-issues)
5. [Low-Severity Issues](#low-severity-issues)
6. [Informational / Code Quality](#informational--code-quality)
7. [Subsystem Health Assessments](#subsystem-health-assessments)
8. [Platform Compatibility Matrix](#platform-compatibility-matrix)
9. [Naming Convention Audit](#naming-convention-audit)
10. [Documentation Coverage](#documentation-coverage)

---

## Executive Summary

The FishMMO connection pipeline is **well-architected at the macro level** with strong security consciousness — constant-time comparison for HMAC verification, X25519 ECDH key agreement with forward secrecy, SRP-6a for password authentication, GCM nonce replay protection, comprehensive rate limiting, certificate pinning, and defense-in-depth path traversal guards.

However, the audit identified **~140 issues** across 11 subsystems, including **12 critical** and **28 high-severity** issues that must be addressed before production deployment. The most severe findings are:

1. **Heap buffer overflow** in the C++ HTTP/3 stream receive path (integer overflow in bounds check)
2. **Memory exhaustion bypass** in the C++ receive buffer limit (integer overflow)
3. **Undefined behavior** in QPACK varint decode (shift >= 64 bits) from crafted network input
4. **Silent TLS pinning disable** on Android/WebGL when CoroutineRunner is missing
5. **Complete client bricking** on desktop when StreamingAssets config is missing
6. **Connection token HMAC key unreachable** in production — env var name mismatch between code, `.env.example`, and docker-compose
7. **Missing Dockerfiles** — Docker builds are impossible for all web server projects
8. **Database name inconsistency** across docker-compose, appsettings, and code

---

## Critical Issues (Must Fix Before Production)

### C-1: Heap Buffer Overflow via Integer Overflow in HTTP/3 Stream Receive
**File:** `FishMMO-WebTransport/src/http3.cpp:859,1014`  
**Severity:** CRITICAL  
**Category:** Memory Safety / Security

The bounds check `if (sctx->recv_offset + total > 65536)` performs uint32_t addition that wraps on overflow. A malicious peer can send a crafted RECEIVE event with `total` near 0xFFFFFFFF — the sum wraps to a tiny value, passes the check, then `realloc` allocates a tiny buffer but `memcpy` writes gigabytes into it. **This is a remote-exploitable heap buffer overflow.**

**Fix:** `if (total > 65536 || sctx->recv_offset > 65536 - total)`

### C-2: Memory Exhaustion via Integer Overflow in Receive Byte Limit
**File:** `FishMMO-WebTransport/src/stream_manager.cpp:94-96`  
**Severity:** CRITICAL  
**Category:** Memory Safety / DoS

The check `if (prev_total + total > WT_MAX_TOTAL_RECV_BUF)` wraps on overflow. A malicious peer can push `total_recv_bytes` toward 0xFFFFFFFF, then overflow the check to bypass the 16 MB per-connection receive limit, causing unbounded server memory consumption.

**Fix:** `if (prev_total > WT_MAX_TOTAL_RECV_BUF - total)`

### C-3: Undefined Behavior — QPACK Varint Shift >= 64
**File:** `FishMMO-WebTransport/src/http3.cpp:125-131`  
**Severity:** CRITICAL  
**Category:** Undefined Behavior / Security

`(uint64_t)(byte & 0x7F) << shift` — after 10 continuation bytes, `shift` reaches 70. Shifting a 64-bit integer by >= 64 bits is **undefined behavior** in C/C++. A crafted QPACK-encoded header with 10+ continuation bytes triggers UB. The compiler may optimize based on the assumption that this never happens, potentially removing the loop exit check.

**Fix:** Add `if (shift >= 64) return -1;` after the shift increment.

### C-4: Silent TLS Pinning Disable on Android/WebGL
**File:** `Assets/Scripts/Client/Security/ClientSecurityBootstrap.cs:88`  
**Severity:** CRITICAL  
**Category:** Security

`CoroutineRunner.Start(LoadFromStreamingAssetsCoroutine())` — if no `CoroutineRunner` MonoBehaviour exists in the scene, the coroutine silently never starts. The temporary `allowOnEmpty: true` configuration remains permanently, disabling all TLS certificate pinning on Android/WebGL release builds. Players are vulnerable to MITM attacks against login server discovery, version checks, and patch downloads.

**Fix:** Add a synchronous fallback or hard error if the coroutine cannot be started. Document the CoroutineRunner scene requirement.

### C-5: Complete Client Bricking When No Security Config Exists
**File:** `Assets/Scripts/Client/Security/ClientSecurityBootstrap.cs:43,122`  
**Severity:** CRITICAL  
**Category:** Resilience

`defaultPins = Array.Empty<string>()` — any desktop release build without a valid `client-security.json` in StreamingAssets falls through to `Configure(defaultPins, allowOnEmpty: false)`. This configures zero pins with empty-not-allowed, causing **every HTTPS request to be rejected**. The client is completely non-functional for all network operations with no user-visible error.

**Fix:** Ship at least one production pin as compile-time default, or fall back to `allowOnEmpty: true` with a loud warning.

### C-6: Env Var Name Mismatch — Connection Token HMAC Key Unreachable
**File:** `FishMMO-WebServers/IPFetchASP.NET/IpFetchServer/Controllers/LoginServerController.cs:171`  
**Severity:** CRITICAL  
**Category:** Production Deployment

The code reads `CONNECTION_TOKEN_HMAC_KEY` from the environment, but `.env.example` documents `FISHMMO_CONNECTION_TOKEN_HMAC_KEY_BASE64`. The names do not match. Every `/loginserver` request returns HTTP 500 with "Server configuration error." in production. Additionally, `docker-compose.yml` does not pass either variable to the ipfetch-server service.

**Fix:** Align the env var name across all files. Add the env var to docker-compose.yml.

### C-7: No Dockerfiles Exist for Web Server Projects
**Files:** `docker-compose.yml:186`, all web server project directories  
**Severity:** CRITICAL  
**Category:** Production Deployment

`docker-compose.yml` references `dockerfile: Dockerfile` in the IpFetchServer directory, but **no Dockerfile exists** in any FishMMO-WebServers project directory. Docker build fails with "file not found." Additionally, the build context is too narrow — relative paths in `.csproj` files (e.g., `..\..\..\FishMMO-Logger\...`) resolve outside the Docker build context.

**Fix:** Create Dockerfiles with appropriate multi-stage builds and monorepo-appropriate build contexts.

### C-8: Database Name Inconsistency Across Configuration
**Files:** `docker-compose.yml:96`, `appsettings.json`, `.env.example:39`  
**Severity:** CRITICAL  
**Category:** Production Deployment

Three different database names are configured:
- Docker Compose creates: `fishmmo`
- appsettings.json references: `fish_mmo_postgresql`
- `.env.example` references: `fishmmo`
- Dev IpFetchServer references: `fishmmo_dev`

Services configured via `appsettings.json` with `Database=fish_mmo_postgresql` will **fail to connect** to the PostgreSQL container which creates `fishmmo`.

**Fix:** Standardize on a single database name across all configuration files.

### C-9: Finalizer Invokes FishNet Callbacks on Non-Main Thread
**Files:** `WebTransport/Core/ClientSocket.cs:110-116`, `WebTransport/Core/ServerSocket.cs:164-170`  
**Severity:** CRITICAL  
**Category:** Thread Safety / Unity

The `~ClientSocket()` and `~ServerSocket()` finalizers dequeue and invoke pending `incomingEvents` actions which call `transport.HandleClientReceivedDataArgs(...)` and `transport.HandleServerReceivedDataArgs(...)`. The .NET finalizer runs on an **arbitrary finalizer thread**, not the Unity main thread. This can cause data corruption, crashes, or undefined behavior in FishNet's internally synchronized state.

**Fix:** Only free unmanaged memory in the finalizer; do not invoke callbacks. Call `GC.SuppressFinalize(this)` from `StopConnection()`.

### C-10: WebGL Send Data Silently Lost on Promise Rejection
**File:** `WebTransport/WebGL/plugin/WebTransport.jslib:244-298`  
**Severity:** CRITICAL  
**Category:** Data Integrity / WebGL

`WTSendStream`/`WTSendDatagram` always return `true` from the C# perspective, even though the underlying JS `WritableStreamDefaultWriter.write()` Promise may reject asynchronously. The C# side disposes the send buffer immediately, so data is **silently lost** on Promise rejection (congestion, connection drop, stream error). This is a fundamental browser WebTransport API limitation.

**Fix:** Implement a Promise-tracking mechanism in the JS bridge or acknowledge the limitation in design docs and implement application-level reliability.

### C-11: Session Memory Leak — New Connection Reuses Slot Before Poll Drains
**File:** `FishMMO-WebTransport/src/server.cpp:638-642`  
**Severity:** CRITICAL  
**Category:** Memory Leak

When a new connection arrives and reuses a slot before the application thread's `poll()` call processes the pending shutdown of the previous occupant, `pending_shutdown_session` is unconditionally set to NULL, destroying the only reference to the previous connection's session. The session, its stream_manager, and all associated memory are **permanently leaked**.

**Fix:** Check `pending_shutdown_session` before NULLing and process deferred shutdown inline if non-NULL.

### C-12: Silent TOTP MFA Skip When Master Key Misconfigured
**File:** `FishMMO-Auth/FishMMO-ServerAuth/Implementation/Auth/SrpAuthenticatorCore.cs` (TOTP setup region)  
**Severity:** CRITICAL  
**Category:** Security

If `TotpMasterKey` is null or not 32 bytes, mandatory TOTP MFA setup is **silently skipped** for new accounts. The comment explicitly calls it "mandatory 2FA setup," but failure is completely invisible. In production, accounts would be created without 2FA with no indication to the operator.

**Fix:** Throw a hard startup error if TOTP master key is not configured in production. Never silently skip security features.

---

## High-Severity Issues

### H-1: SafeHandle DangerousGetHandle Race — Potential Double-Free
**File:** `WebTransport/Native/WebTransportNative.cs:302-309, 408-415`  
**Severity:** HIGH  
**Category:** Memory Safety

`DangerousGetHandle()` is called without `DangerousAddRef`/`DangerousRelease` in `wt_server_destroy`/`wt_client_destroy` wrappers. Between the `DangerousGetHandle()` call and `SetHandleAsInvalid()`, the SafeHandle finalizer could fire on the finalizer thread, triggering `ReleaseHandle()` which also calls the native destroy — a double-free.

**Fix:** Wrap with `DangerousAddRef`/`DangerousRelease`.

### H-2: Non-Atomic Read of pending_shutdown_session Pointer
**File:** `FishMMO-WebTransport/src/server.cpp:416-419`  
**Severity:** HIGH  
**Category:** Thread Safety

`c->pending_shutdown_session` is read with a normal C pointer comparison (no atomic load). The compiler can cache/reorder this read, causing the poll path to skip draining a session on weakly-ordered architectures (ARM). The session **leaks**.

**Fix:** Use a single `atomic_ptr_load` and check the result.

### H-3: Pending Shutdown Ring Buffer Wraps After ~4 Billion Shutdowns
**File:** `FishMMO-WebTransport/src/server.cpp:807-842`  
**Severity:** HIGH  
**Category:** Correctness

When `tail` reaches 0xFFFFFFFF and wraps to 0, the full-check `(tail + 1 - head) >= WT_MAX_CLIENTS` evaluates incorrectly due to unsigned integer semantics. The queue reports "not full" when every slot is occupied, causing overwrites of unconsumed entries and leaked sessions. Manifests after ~49 days at 1000 shutdowns/sec.

**Fix:** Use `uint64_t` for head/tail, or change full-check to `(tail - head) >= WT_MAX_CLIENTS - 1`.

### H-4: Unescaped Process Command-Line Arguments — Command Injection
**File:** `Assets/Scripts/Client/Launcher/SystemUpdaterLauncher.cs:97-98`  
**Severity:** HIGH  
**Category:** Security

`ProcessStartInfo.Arguments` is constructed via string interpolation without quoting/escaping. If `ClientExecutable` contains spaces or an attacker controls the version string (via compromised patch server), arbitrary command-line arguments can be injected.

**Fix:** Use `ProcessStartInfo.ArgumentList` (.NET Standard 2.1+) or proper quoting.

### H-5: UnityEngine.Random in async Task — Potential UnityException
**File:** `Assets/Scripts/Client/Authentication/ClientLoginAuthenticator.cs:263`  
**Severity:** HIGH  
**Category:** Thread Safety

`UnityEngine.Random.Range(0, 1000)` is called inside `RetryHandshakeAsync()` which is an `async Task` method. If the caller's SynchronizationContext is not Unity's main thread, the continuation after `Task.Delay` runs on a thread-pool thread, causing `UnityException` when accessing `UnityEngine.Random`.

**Fix:** Use `System.Random` or marshal back to the Unity main thread.

### H-6: EF Core 5.0.x vs Microsoft.Extensions 9.0.x Version Mismatch
**File:** `FishMMO-Dependencies/FishMMO-Dependencies.csproj:15-53`  
**Severity:** HIGH  
**Category:** Runtime Compatibility

EF Core 5.0.17 was compiled against Microsoft.Extensions.* 5.x, but all Microsoft.Extensions.* packages resolve to 9.0.4 at runtime. If the 9.0.4 packages introduce API changes behind the same facade, IL2CPP will crash rather than log a warning. The csproj comments acknowledge this risk but it remains unresolved.

**Fix:** Either pin Microsoft.Extensions.* to 5.x to match EF Core, or upgrade EF Core to a version compatible with 9.x.

### H-7: Npgsql Compiled Against System.Text.Json 4.6, Gets 9.0.4
**File:** NuGet dependency resolution  
**Severity:** HIGH  
**Category:** Runtime Compatibility

Npgsql 5.0.18 was compiled against `System.Text.Json 4.6.0` but resolves to 9.0.4 at runtime — multiple major versions ahead. Breaking changes in System.Text.Json could cause serialization failures.

### H-8: Undocumented StackExchange.Redis DLLs in Release Output
**File:** `FishMMO-Dependencies/bin/Release/netstandard2.1/`  
**Severity:** HIGH  
**Category:** Maintainability

`StackExchange.Redis.dll`, `StackExchange.Redis.Extensions.Core.dll`, and `Pipelines.Sockets.Unofficial.dll` appear in the Release output but are NOT declared in `FishMMO-Dependencies.csproj`. They are pulled in transitively and not version-pinned, violating the "single source of truth" principle.

**Fix:** Add explicit PackageReference entries with pinned versions, or document as expected transitives.

### H-9: Debug Build Missing Project Reference DLLs
**File:** `FishMMO-Dependencies/` (Debug vs Release build output comparison)  
**Severity:** HIGH  
**Category:** Build Reliability

The Debug build output does NOT contain FishMMO-AuthShared.dll, FishMMO-ClientAuth.dll, FishMMO-DB.dll, FishMMO-Logger.dll, FishMMO-ServerAuth.dll, or FishMMO-SharedUtility.dll. Building in Debug configuration will not copy FishMMO project reference DLLs to Unity's Assets/Dependencies folder, causing missing assembly references at edit/compile time.

### H-10: Orphaned XML Documentation for Non-Existent Feature
**File:** `Assets/Scripts/Server/Implementation/Account/AccountCreationSystem.cs:153-158`  
**Severity:** HIGH  
**Category:** Documentation/Feature Gap

An XML summary block and `[Tooltip]` attribute document a `useConnectionIdForRateLimiting` feature for proxy-safe rate limiting, but the corresponding `[SerializeField]` field does not exist. The documented feature is not implemented.

### H-11: Potential NRE on characterPrefab.NetworkObject
**File:** `Assets/Scripts/Server/Implementation/World/WorldServer/Authentication/CharacterCreateSystem.cs:233-236`  
**Severity:** HIGH  
**Category:** Null Reference

`characterPrefab.NetworkObject.PrefabId` — if `characterPrefab` is not null but `characterPrefab.NetworkObject` is null, this throws NRE. No defensive null check on `.NetworkObject`.

### H-12: ConnectionEncryptionData.NextSendNonce Thread-Safety
**File:** `FishMMO-Auth/FishMMO-AuthShared/Implementation/Connection/ConnectionEncryptionData.cs:85-93`  
**Severity:** HIGH  
**Category:** Thread Safety

The lock only protects the reference read, not the `NextNonce()` call. If `Clear()` runs on another thread and disposes the `GcmNonceContext` mid-operation, it throws `ObjectDisposedException`.

### H-13: link.xml Only Preserves BouncyCastle — Other Critical Types at Risk
**File:** `Assets/link.xml`  
**Severity:** HIGH  
**Category:** IL2CPP Compatibility

The project-level `link.xml` only preserves `Org.BouncyCastle`. It does NOT preserve `FishMMO-AuthShared.dll` types (`ClientAuthenticationResult`, etc.), `WorldServerDetails`, `ServerAddress`, `System.AppContext`, or WebTransport broadcast structs. Under aggressive IL2CPP stripping, these types may be removed, causing `MissingFieldException` or `TypeLoadException`.

### H-14: Nginx Upstream ipfetch_server keepalive Too Low
**File:** `nginx.conf:334`  
**Severity:** HIGH  
**Category:** Production Performance

`keepalive 4` for the `ipfetch_server` upstream is very low for a login-discovery endpoint that handles burst traffic during login rushes. Each login spike requires new TCP connections, adding latency.

### H-15: Database Name Inconsistency Across All Configuration Files (Cross-Cutting)
**Files:** `docker-compose.yml`, `appsettings.json`, `.env.example`  
**Severity:** HIGH  
**Category:** Production Deployment

(See C-8 above for details. Both the container-created database name and the appsettings-referenced database name must match.)

### H-16: Database Server Port Published Despite Internal Network
**File:** `docker-compose.yml:88-92`  
**Severity:** HIGH  
**Category:** Security

The postgres container publishes port 5432 to all Docker host interfaces even though it's on an `internal: true` network. Port publishing bypasses Docker network isolation. Should bind to `127.0.0.1:5432:5432`.

### H-17: Postgres Memory Limit Conflicts with shared_buffers
**File:** `docker-compose.yml:109,116-124`  
**Severity:** HIGH  
**Category:** Production Stability

Postgres memory limit is 512MB, but `shared_buffers=128MB` + `effective_cache_size=384MB` = 512MB before counting `work_mem`, `maintenance_work_mem`, OS overhead, and per-connection overhead. The process will likely be OOM-killed under load.

### H-18: Login/Game Servers Depend on Postgres but NOT on db-migrator
**File:** `docker-compose.yml:349-351`  
**Severity:** HIGH  
**Category:** Deployment Ordering

If `db-migrator` fails or hasn't run yet, the login server connects to the database before schema migrations are applied, causing undefined behavior. The dependency chain should be: `postgres → db-migrator → game servers`.

### H-19: CircularBuffer Head/Tail Properties Return Unsynchronized References
**File:** `FishMMO-SharedUtility/FishMMO-SharedUtility/CircularBuffer.cs:64-72`  
**Severity:** HIGH  
**Category:** Thread Safety

`Head` and `Tail` acquire the lock to read the reference but return the `Node` object with no further protection. Another thread can call `Remove`/`Pop`/`Clear` immediately after the lock is released, invalidating the returned node. The caller dereferences `Next`, `Previous`, and `Value` on a potentially stale/invalid node.

### H-20: Native Crypto Libraries in Dependency Tree — WebGL/IL2CPP Blocker
**File:** `FishMMO-Dependencies/obj/project.assets.json` (transitive dependencies)  
**Severity:** HIGH  
**Category:** Platform Compatibility

The `srp` 1.0.7 package transitively pulls in `System.Security.Cryptography.Algorithms`, `runtime.native.System.Security.Cryptography.OpenSsl`, and platform-specific native interop libraries. These will **fail in Unity WebGL** (no native P/Invoke to OpenSSL) and on IL2CPP builds targeting non-desktop platforms.

### H-21: Postgres shared_buffers + effective_cache_size Exceeds Memory Limit
**File:** `docker-compose.yml:109,116-124`  
**Severity:** HIGH  
**Category:** Production Stability

(Duplicate of H-17 — same issue, counted separately in subsystem reports.)

### H-22: Missing Connection Token HMAC Key in docker-compose.yml ipfetch-server
**File:** `docker-compose.yml:199-203`  
**Severity:** HIGH  
**Category:** Production Deployment

The ipfetch-server service passes `FISHMMO_CLIENT_GATE_SECRET` but does NOT pass any connection token HMAC key env var. Even with a proper Dockerfile, the `/loginserver` endpoint will return 500 errors because `ConnectionToken:HmacKey` in appsettings.Production.json is an empty string and no env var is set.

### H-23: db-migrator restart: "no" Means No Retry on Failure
**File:** `docker-compose.yml:155`  
**Severity:** HIGH  
**Category:** Deployment Resilience

If the migrator fails due to transient network issues, Docker Compose continues starting dependent services with an unmigrated database. Should use `restart: on-failure` with a limited retry count.

### H-24: Nginx Healthcheck Only Validates Config Syntax, Not Service Health
**File:** `docker-compose.yml:294-299`  
**Severity:** HIGH  
**Category:** Production Monitoring

`test: ["CMD", "nginx", "-t"]` only checks config syntax. It does not verify that nginx is actually serving traffic. A hung nginx process with valid config would pass this healthcheck.

### H-25: Stream Block Has No Access Log
**File:** `nginx.conf:208-235`  
**Severity:** HIGH  
**Category:** Monitoring/Forensics

The `log_format game_udp` is defined but never referenced by an `access_log` directive in the `stream {}` block. UDP stream access is not logged, creating a monitoring/forensics gap.

### H-26: TwoFactorSetupBroadcast Uses StructLayout.Sequential on Non-Blittable Struct
**File:** `Assets/Scripts/Shared/Implementation/Network/Authentication/AuthenticationBroadcasts.cs:308`  
**Severity:** HIGH  
**Category:** IL2CPP Compatibility

`[StructLayout(LayoutKind.Sequential)]` on `TwoFactorSetupBroadcast` (which contains `byte[]` reference-type fields) is misleading — the CLR ignores Sequential layout for non-blittable structs. IL2CPP can reorder fields, and the nonce-derivation protocol depends on field order.

### H-27: IL2CPP FastActivator Uses Slow ConstructorInfo.Invoke with Boxing
**File:** `FishMMO-SharedUtility/FishMMO-SharedUtility/FastActivator.cs:67-91`  
**Severity:** HIGH  
**Category:** IL2CPP Performance

Under `#if ENABLE_IL2CPP`, parameterized constructors box all arguments into `object[]` on every call via `_ctor.Invoke(new object?[] { ... })`. This defeats the performance purpose of FastActivator. Additionally, `Expression.Compile()` is not guarded for Mono-on-iOS (which also lacks System.Reflection.Emit).

### H-28: RenewTokenResponseBroadcast Has No Success/Failure Indicator
**File:** `Assets/Scripts/Shared/Implementation/Network/Authentication/AuthenticationBroadcasts.cs:262-269`  
**Severity:** HIGH  
**Category:** Protocol Completeness

`RenewTokenResponseBroadcast` has no `Result`/`Success` field. The client cannot distinguish between a successful token renewal and a silent failure. Any network error during token renewal is indistinguishable from success.

---

## Medium-Severity Issues

### M-1: Debug.Assert Compiled Out in Release — Silent Thread-Safety Violation
**File:** `WebTransport/Core/ClientSocket.cs:149-153`  
**Severity:** MEDIUM  
**Category:** Thread Safety

Main thread assertion uses `Debug.Assert`, which is compiled out in Release builds. In production, there is no runtime check for main-thread access violations.

### M-2: Mutable Packet Struct Anti-Pattern
**File:** `WebTransport/Core/Supporting.cs:10-58`  
**Severity:** MEDIUM  
**Category:** Code Quality

`Packet` is a mutable struct with `Dispose()` modifying the `owned` field. Safe in current usage but fragile under refactoring — copies could lead to double-dispose.

### M-3: TOCTOU Between IsLibraryDeinitialized Check and Native Destroy
**File:** `WebTransport/Native/WebTransportNative.cs:22-24,43-44`  
**Severity:** MEDIUM  
**Category:** Thread Safety

There is a TOCTOU window between `IsLibraryDeinitialized` check and native destroy call in SafeHandle finalizers. Partially mitigated by ordering in `Deinitialize()`.

### M-4: _malloc Failure Silently Drops Data in WebGL
**File:** `WebTransport/WebGL/plugin/WebTransport.jslib:89-94,152-157`  
**Severity:** MEDIUM  
**Category:** WebGL Reliability

Under WebAssembly memory pressure, `_malloc` may return null, causing `HEAPU8.set()` to throw or data to be silently dropped.

### M-5: LPUTF8Str Marshaling Requires Unity 2021.2+
**File:** `WebTransport/Native/WebTransportNative.cs:291`  
**Severity:** MEDIUM  
**Category:** Platform Compatibility

`[MarshalAs(UnmanagedType.LPUTF8Str)]` requires Unity 2021.2+. Lower versions will silently corrupt UTF-8 strings (cert paths, ALPNs, addresses).

### M-6: Pending Shutdown Unsigned Underflow on Never-Connected Path
**File:** `FishMMO-WebTransport/src/client.cpp:88-92,464`  
**Severity:** MEDIUM  
**Category:** Correctness

When a client is freed without ever connecting, `pending_shutdowns` decrements from 0, wrapping to 0xFFFFFFFF. The server has a guard for this; the client does not.

### M-7: 200 OK Response Silently Dropped on malloc Failure
**File:** `FishMMO-WebTransport/src/http3.cpp:1780-1794`  
**Severity:** MEDIUM  
**Category:** Error Handling

After creating a WebTransport session, if `malloc` fails for the 200 OK response buffer, the response is never sent and no error is reported. The browser times out (30-60s) while the server holds a leaked session until idle timeout.

### M-8: Client connected Flag Not Set in HTTP/3 Handshake Path
**File:** `FishMMO-WebTransport/src/client.cpp:373-393`  
**Severity:** MEDIUM  
**Category:** Correctness

In HTTP/3 client mode, the `CONNECTED` callback breaks without setting `connected = true` or `state = WT_CLIENT_STARTED`. If the C# layer's `on_ready` callback doesn't set it, `wt_client_is_connected()` always returns false.

### M-9: Client UAF After Shutdown Timeout
**File:** `FishMMO-WebTransport/src/client.cpp:57-82`  
**Severity:** MEDIUM  
**Category:** Memory Safety

If the 3-second shutdown spin-wait timeout fires, `free_impl` frees the client struct, but a late `SHUTDOWN_COMPLETE` callback could access the freed `wt_client_s` — a **use-after-free**. The server has an owner-null pattern to prevent this; the client does not.

### M-10: Spin-Wait Shutdown Timeout May Be Insufficient Under Load
**File:** `FishMMO-WebTransport/src/server.cpp:204-212`, `src/client.cpp:57-64`  
**Severity:** MEDIUM  
**Category:** Reliability

300 iterations × 10ms = 3-second spin-wait for shutdown. Under heavy load with TLS key operations and many concurrent closures, 3 seconds may not suffice.

### M-11: h3_session Pointer Has Undocumented Thread-Safety Assumption
**File:** `FishMMO-WebTransport/src/server.h:51`  
**Severity:** MEDIUM  
**Category:** Thread Safety

The `h3_session` field is a plain pointer accessed from multiple QUIC callback paths. It relies on msquic's undocumented guarantee of per-connection callback serialization.

### M-12: System.Security.Cryptography.Primitives Excluded from Unity Copy, Breaks SRP
**File:** `FishMMO-Dependencies/FishMMO-Dependencies.csproj:131`  
**Severity:** MEDIUM  
**Category:** Runtime Compatibility

`System.Security.Cryptography.Primitives.dll` is excluded from the copy to Unity, but the `srp` package depends on it. If any Unity code uses SRP functionality, this causes `FileNotFoundException` or `TypeLoadException`.

### M-13: BouncyCastle/Otp.NET Versions Overridden via Nearest-Wins
**File:** NuGet dependency resolution for FishMMO-AuthShared transitives  
**Severity:** MEDIUM  
**Category:** Version Drift

FishMMO-AuthShared was built against BouncyCastle 2.5.1 and Otp.NET 1.4.0, but FishMMO-Dependencies overrides to 2.6.2 and 1.4.1 respectively. The AuthShared project was not tested against these overridden versions.

### M-14: System.Threading.Channels 8.0→9.0.4 Major Version Override
**File:** NuGet dependency resolution for FishMMO-ServerAuth transitives  
**Severity:** MEDIUM  
**Category:** Version Drift

FishMMO-ServerAuth declares `System.Threading.Channels 8.0.0` but resolves to 9.0.4 — a major version jump.

### M-15: Newtonsoft.Json + System.Text.Json Dual JSON Library
**File:** Transitive dependency from OpenAI package  
**Severity:** MEDIUM  
**Category:** Binary Size / IL2CPP

OpenAI 1.7.2 pulls in `Newtonsoft.Json 13.0.3` transitively, creating a dual JSON library situation with `System.Text.Json 9.0.4`. Newtonsoft.Json has known AOT/IL2CPP issues due to heavy reflection use (`ReflectionDelegateFactory`, dynamic type creation).

### M-16: Server Authenticator Signing Key Not Zeroed — Memory Dump Risk
**File:** `FishMMO-Auth/FishMMO-ServerAuth/Implementation/Auth/ServerAuthenticator.cs:698-711`  
**Severity:** MEDIUM  
**Category:** Security

`AtomicSwapSigningKey` intentionally does NOT zero old key arrays to avoid corrupting in-flight operations. Old keys remain in heap until GC, exposing them to memory-dump attacks. Acknowledged as known security debt with unimplemented suggested mitigation (pinning+zeroing after all readers complete).

### M-17: Client.cs Complex Dual-Storage Event Forwarding Pattern
**File:** `Assets/Scripts/Client/Client.cs:237-248`  
**Severity:** MEDIUM  
**Category:** Maintainability

Custom event add/remove accessors maintain two storage locations (backing field + ConnectionManager events) with manual forwarding. Correct but fragile — a change to initialization order could introduce double-firing or missed-firing bugs.

### M-18: Orphaned/Duplicate XML Comments in Auth Code
**Files:** `BaseAuthenticatorCore.cs:355-357`, `SrpAuthenticatorCore.cs:1333-1336`  
**Severity:** MEDIUM  
**Category:** Documentation

Two instances of duplicate/orphaned `<summary>` tags that produce compiler warning CS1570.

### M-19: HandshakeCookie Bucket Underflow at Epoch Boundary
**File:** `FishMMO-Auth/FishMMO-AuthShared/Implementation/Services/HandshakeService.cs:178-179`  
**Severity:** MEDIUM  
**Category:** Correctness

`currentBucket - 1` when `currentBucket` is 0 wraps to `uint.MaxValue`. Theoretically only at Unix epoch start, but the underflow is not guarded.

### M-20: TOTP Semaphore Disposal Race
**File:** `FishMMO-Auth/FishMMO-ServerAuth/Implementation/Auth/SrpAuthenticatorCore.cs:577-579`  
**Severity:** MEDIUM  
**Category:** Thread Safety

If `ShutdownWorkersCore` disposes `totpSemaphore` while a TOTP verification task is still in-flight, the `sem.Release()` call throws `ObjectDisposedException`. Caught by outer try-catch, but the timing window exists.

### M-21: Base64URL Encoding Logic Duplicated Across Projects
**Files:** `ClientGate.cs:149-150`, `LoginServerController.cs:149-150`  
**Severity:** MEDIUM  
**Category:** Code Quality

Two implementations of the same `ToBase64Url` pattern exist in the WebServers project without a shared utility.

### M-22: DoS Vector in ClientGate Nonce Cache LRU Eviction
**File:** `FishMMO-WebServers/FishMMO-WebShared/ClientGate.cs:311-313`  
**Severity:** MEDIUM  
**Category:** Performance/DoS

O(n log n) `Array.Sort` on 20,000 entries under `pruneLock`. A sustained flood triggers this sort every 5 seconds, adding tens of milliseconds of latency on the winning request thread. A TODO suggests sampling-based eviction but it's not implemented.

### M-23: Per-Process ClientGate Nonce Cache — No Shared State
**File:** `FishMMO-WebServers/FishMMO-WebShared/ClientGate.cs`  
**Severity:** MEDIUM  
**Category:** Security

Nonce cache is per-process in-memory. In multi-instance deployments, the same nonce can be replayed against different instances within the 30-second skew window. No Redis/shared cache fallback.

### M-24: Missing Try/Catch Around EF Query in LoginServerController
**File:** `FishMMO-WebServers/IPFetchASP.NET/IpFetchServer/Controllers/LoginServerController.cs:84-86`  
**Severity:** MEDIUM  
**Category:** Error Handling

A transient DB failure during `ToArrayAsync` propagates to the exception handler middleware, returning generic 500 instead of 503 Service Unavailable.

### M-25: /healthz Uses String Concatenation for JSON Instead of MapHealthChecks
**File:** All three Program.cs files in FishMMO-WebServers  
**Severity:** MEDIUM  
**Category:** Maintainability

All servers use ad-hoc JSON formatting via string concatenation rather than the built-in `HealthCheckService`/`MapHealthChecks`. String concatenation for JSON is brittle.

### M-26: FishMMO-AuthShared ClientAuthenticationResult Enum Missing Error Values
**File:** `FishMMO-Auth/FishMMO-AuthShared/Core/Enums/ClientAuthenticationResult.cs`  
**Severity:** MEDIUM  
**Category:** Protocol Completeness

The enum has no `AccountCreationFailed`, `RateLimited`, or `ServerError` values for non-auth-specific failures.

### M-27: SrpSuccessBroadcast Token Nullable with No HasToken Boolean
**File:** `Assets/Scripts/Shared/Implementation/Network/Authentication/AuthenticationBroadcasts.cs:229`  
**Severity:** MEDIUM  
**Category:** Protocol Completeness

`Token` is nullable with no `HasToken` boolean to distinguish "token issuance not enabled" from "token transmission failed."

### M-28: ServerHandshake Uses Nullable Fields for Dual-Purpose Encoding
**File:** `Assets/Scripts/Shared/Implementation/Network/Authentication/AuthenticationBroadcasts.cs:149-169`  
**Severity:** MEDIUM  
**Category:** Protocol Design

`PublicKey` can be null (cookie challenge) and `Cookie` can be null (final handshake response). The same struct encodes two different message types. Not type-safe.

### M-29: Constants.cs Copy-Paste Documentation Error on 20+ Fields
**File:** `Assets/Scripts/Shared/Implementation/Constants.cs:56-389`  
**Severity:** MEDIUM  
**Category:** Documentation

The remark "NOTE: Does not throw on missing layers -- only logs a warning." appears verbatim on every constant in the `Configuration`, `Layers`, and `Character` classes, including fields unrelated to layers (e.g., `ProjectName`, `WalkSpeed`). This is misleading copy-paste.

### M-30: ServerAddress Mutable Struct Anti-Pattern
**File:** `Assets/Scripts/Shared/Implementation/Network/ServerSelect/ServerAddress.cs:40-52`  
**Severity:** MEDIUM  
**Category:** Code Quality

`ServerAddress` is a mutable struct with a documented warning about the anti-pattern. Should be `readonly struct` or `class`.

### M-31: Nginx Missing reuseport on TCP SSL Listeners
**File:** `nginx.conf:349,401`  
**Severity:** MEDIUM  
**Category:** Performance

QUIC listener correctly uses `reuseport`, but TCP SSL listeners do not. Under high HTTP load, the single accept mutex could be a bottleneck.

### M-32: Nginx Upstream Has Only Passive Health Checks
**File:** `nginx.conf:306-338`  
**Severity:** MEDIUM  
**Category:** Production Reliability

All upstreams rely on passive health checks (max_fails=3, fail_timeout=30s). Hung processes that keep TCP ports open cannot be detected.

### M-33: gen-fishmmo-stream-config.sh Glob Expansion Risk
**File:** `gen-fishmmo-stream-config.sh:96,101`  
**Severity:** MEDIUM  
**Category:** Script Reliability

With `set -euo pipefail` and no `nullglob`, empty globs expand to literal strings, causing `mv` to fail on nonexistent files.

### M-34: gen-fishmmo-stream-config.sh First Validation Tests OLD Config
**File:** `gen-fishmmo-stream-config.sh:69`  
**Severity:** MEDIUM  
**Category:** Script Correctness

The first `nginx -t` validates the currently-deployed configs, not the new ones being generated.

### M-35: Docker Compose Check Logic Inverted in certbot-fishmmo.sh
**File:** `deploy-hooks/certbot-fishmmo.sh:114`  
**Severity:** MEDIUM  
**Category:** Script Correctness

`if [ ! -f "$compose_file" ] && [ ! -f "${compose_dir}/fishmmo-secrets.env" ]` uses `&&` (AND) but should use `||` (OR). If the compose file is missing but secrets.env exists, the script proceeds and fails.

### M-36: Missing No Per-IP Rate Limiting at Nginx Stream Layer
**File:** `nginx.conf:131-206`  
**Severity:** MEDIUM  
**Category:** Security

Only per-session bandwidth limits (100 MB/s). A single abusive IP can open many concurrent UDP sessions. Documented as a known gap but no deployment automation for iptables/nftables rules.

### M-37: SMTP Service Initialization Double-Checked Locking — Correct but Complex
**File:** `Assets/Scripts/Server/Implementation/Account/AccountCreationSystem.cs:1132-1141`  
**Severity:** MEDIUM  
**Category:** Code Quality

Double-checked locking is correct (volatile + lock pattern) but complex. Consider `Lazy<T>` for clarity.

### M-38: ProcessExtensions.cs Not Available on WebGL
**File:** `FishMMO-SharedUtility/FishMMO-SharedUtility/Extensions/ProcessExtensions.cs`  
**Severity:** MEDIUM  
**Category:** WebGL Compatibility

`System.Diagnostics.Process` is not available on WebGL. The `WaitForExitAsync` extension would throw `PlatformNotSupportedException`. No `#if !UNITY_WEBGL` guard.

### M-39: Configuration.cs File I/O Not Guarded for WebGL
**File:** `FishMMO-SharedUtility/FishMMO-SharedUtility/Configuration.cs`  
**Severity:** MEDIUM  
**Category:** WebGL Compatibility

File I/O methods (`Directory.CreateDirectory`, file save) would fail on WebGL with no platform guard.

### M-40: CryptographicOperationsCompat Constant-Time Comparison Not Truly Constant-Time
**File:** `FishMMO-SharedUtility/FishMMO-SharedUtility/CryptographicOperationsCompat.cs:64-69`  
**Severity:** MEDIUM  
**Category:** Security

The `result |= left[i] ^ right[i]` loop is a reasonable approximation but modern JIT/IL2CPP compilers can optimize or short-circuit the `|=` operation. The `return result == 0` comparison leaks timing. Acceptable for MMO game use but would not pass a cryptography audit.

### M-41: ByteArrayExtensions.Compare Method Name Misleading
**File:** `FishMMO-SharedUtility/FishMMO-SharedUtility/Extensions/Primitive/Byte/ByteArrayExtensions.cs:19`  
**Severity:** MEDIUM  
**Category:** Naming Convention

`Compare` returns `bool` indicating equality, but `Compare` conventionally implies returning -1/0/1 like `IComparable`. Should be named `Equals` or `ContentEquals`.

### M-42: AccountManager.TryAdvanceAuthState Callback Runs Inside Lock
**File:** `FishMMO-Auth/FishMMO-ServerAuth/Implementation/Auth/AccountManager.cs:192-216`  
**Severity:** MEDIUM  
**Category:** Thread Safety

The `onSuccess` callback is invoked while holding `SyncRoot`. A violating callback would cause deadlock. Documented but not compile-time enforced.

---

## Low-Severity Issues

### L-1 through L-40

| # | File | Line | Severity | Description |
|---|------|------|----------|-------------|
| L-1 | `WebTransport/Core/ClientSocket.cs` | 451-458 | LOW | `webglPendingConnect` fallback is effectively dead code (JS microtasks fire after synchronous C# completes) |
| L-2 | `WebTransport/WebTransport.cs` | 207-209 | LOW | Certificate/key paths not validated for file existence before use |
| L-3 | `WebTransport/Core/CommonSocket.cs` | 128-135 | LOW | Backpressure warning only for reliable channel (0), not unreliable (1) |
| L-4 | `WebTransport/WebGL/plugin/WebTransport.jslib` | 218,226-227 | LOW | Spontaneous `closed` rejection without `WTDisconnect` leaves `_closed` flag unset |
| L-5 | `WebTransport/Native/WebTransportNative.cs` | 142 | LOW | `Marshal.PtrToStringUTF8` requires .NET Standard 2.1 / Unity 2020.3+, wrapped in try-catch |
| L-6 | `WebTransport/WebTransportNative.cs` | 456-492 | LOW | WebGL stub implementations are dead code on non-WebGL platforms (harmless) |
| L-7 | `FishMMO-WebTransport/src/server.cpp` | 945 | LOW | Datagram drop counter log value off-by-one labeling |
| L-8 | `FishMMO-WebTransport/src/client.cpp` | 464 | LOW | Missing pending_shutdowns guard in client SHUTDOWN_COMPLETE (mirrors server pattern) |
| L-9 | `FishMMO-WebTransport/build_windows_cross.sh` | 97-103 | LOW | `\|\| true` swallows linker errors — failed builds silently masquerade as success |
| L-10 | `FishMMO-WebTransport/src/stream_manager.cpp` | 16-33 | LOW | Windows recursive lock reads thread-ID without acquire barrier (safe in practice on x86/ARM due to EnterCriticalSection barrier) |
| L-11 | `FishMMO-WebTransport/src/webtransport_internal.h` | 29-41 | LOW | Plain integer typedefs for atomics allow silent non-atomic access |
| L-12 | `FishMMO-WebTransport/src/webtransport_api.cpp` | 30-56 | LOW | Multiple MsQuicOpen2 calls under concurrent `wt_init` contention (wasteful, not buggy) |
| L-13 | `FishMMO-WebTransport/src/http3.cpp` | 1705 | LOW | `strlen` on origin buffer may misrepresent length with embedded nulls — potential CORS bypass |
| L-14 | `FishMMO-WebTransport/src/http3.cpp` | 769-787 | LOW | `malloc(0)` possible in `h3_stream_send` — returns NULL or non-NULL (implementation-defined) |
| L-15 | `FishMMO-WebTransport/src/http3.cpp` | 68-95 | LOW | `varint_encode` writes up to 8 bytes with no bounds checking (all current callers safe) |
| L-16 | `FishMMO-WebTransport/src/http3.cpp` | 527 | LOW | Dead store: `name_from_static` variable set but never read in `qpack_parse_field` |
| L-17 | `FishMMO-WebTransport/src/http3.cpp` | 1594-1610 | LOW | `native_stream_ctx` stale pointer hazard if `on_ready` callback is NULL |
| L-18 | `ClientLoginAuthenticator.cs` | 460 | LOW | ConnectionToken race between concurrent connection state callbacks (low probability in FishNet) |
| L-19 | `ClientApiSigner.cs` | 72 | LOW | `new DateTime(1970, 1, 1, ...)` allocated on every `BuildHeaderValue` call — should be `static readonly` |
| L-20 | `ClientApiSigner.cs` | 70-71 | LOW | `ArgumentException` from `CanonicalizePath` failure silently terminates coroutine without error callback |
| L-21 | `ClientApiSigner.cs` | 86-90 | LOW | Platform defines for `CryptographicOperations.ZeroMemory` may not match Unity scripting backend |
| L-22 | `UnityWebRequestService.cs` | 87 | LOW | Misleading comment: "0 = unlimited in Unity" is incorrect for Unity 2021+ (0 means default 20 redirects) |
| L-23 | `HttpPatchServerService.cs` | 220-235 | LOW | MEMFS 256MB limit may silently truncate large patch files on WebGL |
| L-24 | `UnityHtmlContentFetcher.cs` | (using) | LOW | HtmlAgilityPack reflection requirements for IL2CPP undocumented (unlike BouncyCastle notes elsewhere) |
| L-25 | `ClientApiSecret.cs` | 89 | LOW | No enforcement of buffer zeroing after `GetBytes()` — advisory only |
| L-26 | `ClientApiSecret.cs` | 74-78 | LOW | Redundant nested `#if` preprocessor guard |
| L-27 | `ClientSecurityBootstrap.cs` | 78 | LOW | Redundant `if (true)` block inside Android/WebGL `#if` region |
| L-28 | `ClientCertificatePinning.cs` | 259-262 | LOW | `ConstantTimeEquals` early-return on length mismatch — theoretical timing leak (pins are fixed 44 chars) |
| L-29 | `ClientCertificatePinning.cs` | 243-249 | LOW | HashSet iteration order non-determinism in `ConstantTimeContains` — theoretical only |
| L-30 | `ClientLauncher.cs` | 363-364 | LOW | Missing null checks on 12 serialized UI fields before use |
| L-31 | `Client.cs` | 263 | LOW | `as` cast on `UIAdvancedLabel.Create()` returns null silently if type mismatch |
| L-32 | `UnityHtmlContentFetcher.cs` | 162-164 | LOW | XPath injection surface documented for future refactoring — currently safe (Inspector input) |
| L-33 | `TokenServerAuthenticator.cs` | 425 | LOW | Duplicate `<inheritdoc/>` comment |
| L-34 | `CharacterSelectSystem.cs` | 120-131 | LOW | Silent drop when `conn.IsActive` is false after successful account resolution |
| L-35 | `ServerAddressProvider.cs` | 97-121 | LOW | Weak address override validation (no IP/hostname format check, only control-char + length) |
| L-36 | `LoginQueueSystem.cs` | 307-314 | LOW | Recent-admission entries cleared all-at-once rather than by expiration, causing fast-pass window jitter (0-25s vs documented 15s) |
| L-37 | `ServerAddress.Address` | 49 | LOW | Field named `Address` but holds IP or hostname — `Hostname` or `Host` would be clearer |
| L-38 | `ServerAddresses` | 15,18,26 | LOW | `[field: SerializeField]` on a class not Unity-serialized — attributes are vestigial |
| L-39 | `WorldServerDetails` | - | LOW | No `Id` or `Address` field — client must use `GameHost` from Constants |
| L-40 | `Constants.Configuration.APIHost/GameHost` | 132,151 | LOW | `const` (compile-time baked) — changing deployment target requires full rebuild |

---

## Informational / Code Quality

| # | File | Line | Description |
|---|------|------|-------------|
| I-1 | `WebTransport/WebTransport.cs` | 390-396, 410-416 | macOS standalone check pattern appears twice (DRY) |
| I-2 | `WebTransport/Core/CommonSocket.cs` | 116-135 | `Send()` uses `ByteArrayPool` via Packet constructor — good allocation management |
| I-3 | `WebTransport/Native/WebTransportNative.cs` | 170-241 | `EnsureInitialized()` uses `Interlocked.CompareExchange` + spin-wait — correct for main-thread-only contract |
| I-4 | `WebTransport/WebGL/WebTransportJSLib.cs` | 18-71 | WebGL static callbacks correctly use `[AOT.MonoPInvokeCallback]` and static delegate fields |
| I-5 | `WebTransport/WebTransport.jslib` | 15-24 | `_dynCall` wrapper handles both Emscripten 2.x and 3.x calling conventions |
| I-6 | `WebTransport/WebTransport.jslib` | 253-275 | Stream writer cached in `session._streamWriter` — eliminates per-packet stream creation overhead |
| I-7 | `FishMMO-WebTransport/src/` | Various | Ref-counted session lifecycle correctly handles in-flight sends during shutdown |
| I-8 | `FishMMO-WebTransport/src/` | Various | Lock hierarchy is correct — `streams_lock` is a leaf lock |
| I-9 | `FishMMO-Auth/FishMMO-AuthShared/Implementation/Crypto/CryptoHelper.cs` | 405-438 | RFC 7748 Section 6.1 small-order point blacklist implemented — excellent |
| I-10 | `FishMMO-Auth/` | Various | All crypto methods validate inputs with `ArgumentNullException`/`ArgumentException` |
| I-11 | `FishMMO-Dependencies/` | - | All Microsoft.Extensions.* packages unified at 9.0.4 across 21 packages |
| I-12 | `AuthenticationBroadcasts.cs` | 7-33 | Excellent header about byte array ownership and field ordering |
| I-13 | `WorldServerDetails.cs` | 14-22 | Excellent documentation about using `long` (ticks) instead of `DateTime` to avoid FishNet DateTimeKind loss |

---

## Subsystem Health Assessments

### 1. WebTransport C++ Wrapper (msquic)
**Overall:** ⚠️ Needs Work  
**Files:** 17 source files, ~5600 lines  
**Strengths:** Correct ref-counted session lifecycle, proper lock hierarchy, comprehensive HTTP/3 QPACK implementation, well-documented threading model.  
**Weaknesses:** 6 critical/high issues including heap buffer overflow, integer overflow DoS, undefined behavior in QPACK, and session memory leak. These are exploitable by any network peer.  
**Recommendation:** Fix C-1 through C-3 and C-11 before any production deployment. Run under ASAN/UBSAN.

### 2. Unity WebTransport Plugin
**Overall:** ⚠️ Needs Work  
**Files:** 13 source files  
**Strengths:** Correct P/Invoke patterns, proper IL2CPP WebGL callback handling, good platform separation with `#if` directives.  
**Weaknesses:** Finalizer invokes FishNet callbacks on wrong thread (C-9), SafeHandle double-free race, mutable struct anti-pattern, WebGL data loss on Promise rejection (C-10).  
**Recommendation:** Fix finalizer threading issue and WebGL send reliability.

### 3. Client Connection Pipeline
**Overall:** ✅ Good (with critical fixes needed)  
**Files:** 22 source files  
**Strengths:** Excellent certificate pinning, thorough URL canonicalization, proper HMAC signing, well-structured launcher state machine.  
**Weaknesses:** Silent TLS pinning disable on Android/WebGL (C-4), complete client bricking on missing config (C-5), unescaped process arguments (H-4), Unity API on non-main thread (H-5).  
**Recommendation:** Fix the two critical security bootstrap issues before release.

### 4. Server Connection Pipeline
**Overall:** ✅ Good  
**Files:** 30 source files  
**Strengths:** Robust SRP authentication, comprehensive rate limiting, proper concurrent dictionary usage, production-only validation guards, key zeroing on shutdown.  
**Weaknesses:** Orphaned documentation for non-existent feature, potential NRE in character creation, silent TOTP skip (C-12).  
**Recommendation:** Implement the documented but missing connection-ID rate limiting feature.

### 5. Shared Network Code
**Overall:** ⚠️ Needs Work  
**Files:** 6 source files  
**Strengths:** Thorough documentation, well-considered FishNet serialization choices.  
**Weaknesses:** Missing link.xml preservation entries, StructLayout on non-blittable structs, const values for deployment-critical config, copy-paste documentation errors on 20+ fields.  
**Recommendation:** Fix link.xml, refactor Constants to use configurable values.

### 6. Auth Sub-Project
**Overall:** ✅ Good  
**Files:** 22 source files  
**Strengths:** Strong cryptography, X25519 ECDH with forward secrecy, SRP-6a, comprehensive rate limiting, constant-time comparison.  
**Weaknesses:** TOTP internal secret not zeroable on IL2CPP, signing key heap retention, connection encryption thread-safety.  
**Recommendation:** Accept limitations with documentation; fix thread-safety issues.

### 7. Database Sub-Project
**Overall:** ✅ Good  
**Files:** 149 source files  
**Strengths:** Proper parameterization against SQL injection, comprehensive entity configurations, migration system.  
**Weaknesses:** Version mismatch in dependency resolution, some error handling gaps.  
**Recommendation:** Standardize database name across configuration; add health checks.

### 8. SharedUtility
**Overall:** ⚠️ Needs Work  
**Files:** 28 source files  
**Strengths:** Good extension method coverage, well-structured Configuration class.  
**Weaknesses:** CircularBuffer thread safety (H-19), FastActivator IL2CPP performance (H-27), missing WebGL guards, naming convention issues.  
**Recommendation:** Fix CircularBuffer synchronization; add WebGL platform guards.

### 9. WebServers
**Overall:** ❌ Not Production-Ready  
**Files:** 19 source files  
**Strengths:** Excellent security consciousness, thorough path traversal defenses, good rate limiting design.  
**Weaknesses:** HMAC key env var mismatch (C-6), no Dockerfiles (C-7), missing env vars in docker-compose (H-22), per-process nonce cache limitation.  
**Recommendation:** Resolve all critical deployment issues before any production use.

### 10. Configuration & Infrastructure
**Overall:** ❌ Not Production-Ready  
**Files:** 28 files  
**Strengths:** Well-structured nginx config, comprehensive docker-compose, good backup/restore scripts.  
**Weaknesses:** Database name inconsistency (C-8), no WebGL/Patcher server in docker-compose, OOM risk in Postgres config, nginx healthcheck only validates syntax.  
**Recommendation:** Standardize all names, add missing services, fix resource limits.

### 11. Dependencies
**Overall:** ⚠️ Needs Work  
**Files:** 1 csproj + NuGet graph  
**Strengths:** Strong documentation in csproj comments, unified Microsoft.Extensions versions.  
**Weaknesses:** EF Core 5.x vs Extensions 9.x mismatch (H-6), Npgsql System.Text.Json version jump (H-7), undocumented transitives (H-8), Debug build missing project refs (H-9).  
**Recommendation:** Pin EF Core-matching Extensions versions or upgrade EF Core.

---

## Platform Compatibility Matrix

| Platform | Native Transport | Client Auth | Server | Database | WebServers | Overall |
|----------|-----------------|-------------|--------|----------|------------|---------|
| **Linux x86_64** | ✅ Native .so shipped | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Linux x86_64 (Docker)** | ✅ | ✅ | ✅ | ✅ (with fixes) | ❌ No Dockerfiles | ❌ |
| **Windows x86_64** | ⚠️ Build required | ✅ | N/A | ✅ | ✅ | ⚠️ |
| **macOS x86_64** | ⚠️ Build required | ✅ | N/A | ✅ | ✅ | ⚠️ |
| **WebGL (Browser)** | ✅ jslib bridge | ✅ (with fixes) | N/A | N/A | ✅ | ⚠️ |
| **IL2CPP (All)** | ✅ Correct patterns | ✅ (needs link.xml) | ✅ | N/A | N/A | ⚠️ |
| **iOS** | ❌ No transport | ❌ | N/A | N/A | N/A | ❌ |
| **Android** | ❌ No transport | ⚠️ (C-4) | N/A | N/A | N/A | ❌ |
| **Consoles** | ❌ No transport | ❌ | N/A | N/A | N/A | ❌ |

### IL2CPP-Specific Risks

1. **link.xml incomplete** — only BouncyCastle preserved (H-13)
2. **FastActivator slow path** — ConstructorInfo.Invoke with boxing on IL2CPP (H-27)
3. **Expression.Compile() not guarded for Mono-on-iOS** (H-27)
4. **StructLayout.Sequential on non-blittable structs** (H-26)
5. **TOTP internal zeroization is no-op on IL2CPP** (documented)
6. **Newtonsoft.Json reflection issues** — heavy use of ReflectionDelegateFactory (M-15)

### WebGL-Specific Risks

1. **Data silently lost on send Promise rejection** (C-10)
2. **_malloc failure drops data under WASM memory pressure** (M-4)
3. **MEMFS 256MB limit for patches** (L-23)
4. **ProcessExtensions, Configuration file I/O not guarded** (M-38, M-39)
5. **System.Threading.Channels unavailable** in WebGL threading model
6. **Native crypto interop (OpenSSL) not available** in browser (H-20)

---

## Naming Convention Audit

### PascalCase for Public Fields: ✅ Mostly Compliant
- Server and client code consistently uses PascalCase for public members.
- `ServerAddress.Address` field name is suboptimal (should be `Hostname`).
- `ByteArrayExtensions.Compare` should be `Equals` or `ContentEquals`.

### camelCase for Private Fields: ✅ Compliant
- All private fields use camelCase consistently across the codebase.
- No underscore-prefixed private fields found in Unity C# code.

### Underscore Prefixes on Private Fields: ✅ None Found
- The codebase correctly uses `this.` prefix instead of `_` prefix for private field disambiguation.

### Constants: ⚠️ Minor Issues
- `TotpSecretAadPrefixV2` is a `private const string` using PascalCase (acceptable in many C# codebases).
- `EmailUsernameRegex` method name misleading — validates full email, not just username.

### Configuration Keys: ⚠️ Inconsistent
- `ConnectionTokenHmacKeyBase64` (PascalCase, .cfg files)
- `CONNECTION_TOKEN_HMAC_KEY` (UPPER_SNAKE_CASE, env var in code)
- `FISHMMO_CONNECTION_TOKEN_HMAC_KEY_BASE64` (UPPER_SNAKE_CASE with prefix, .env.example)
- `ConnectionToken.HmacKey` (PascalCase with dot notation, appsettings.json)

These must be reconciled.

---

## Documentation Coverage

### Areas with Excellent Documentation
- `AuthenticationBroadcasts.cs` — byte array ownership and field ordering
- `ClientGate.cs` — threat model, known limitations, edge cases
- `LoginServerController.cs` — endpoint behavior, cache design decisions
- `PatchVersionService.cs` — security rationale for every defense
- `CryptoHelper.cs` — every method and parameter documented
- `AuthState.cs` — ASCII art state machine diagram
- `WebTransport/README.md` — build instructions, architecture overview

### Areas Missing Documentation (should be added)
- `ClientSecurityBootstrap.cs` — CoroutineRunner scene requirement
- `UnityHtmlContentFetcher.cs` — HtmlAgilityPack link.xml requirements
- `FishMMO-WebTransport/src/server.h` — msquic callback serialization assumption
- `docker-compose.yml` — which services are behind profiles and why
- `nginx.conf` — stream block no-access-log rationale
- `WorldServerDetails.cs` — single-host assumption

### Orphaned/Duplicate Documentation (should be fixed)
1. `AccountCreationSystem.cs:153-158` — Summary/Tooltip for non-existent field
2. `BaseAuthenticatorCore.cs:355-357` — Duplicate SweepExpiredHandshakeRateLimits summary
3. `SrpAuthenticatorCore.cs:1333-1336` — Orphaned summary tag between methods
4. `Constants.cs:56-389` — Copy-paste "Does not throw on missing layers" on 20+ non-layer fields
5. `TokenServerAuthenticator.cs:425` — Duplicate `<inheritdoc/>`
6. `FishMMO-Dependencies/README.md:59` — References `Class1.cs` instead of `Placeholder.cs`

---

## Recommended Fix Priority

### Immediate (Before Any Production Traffic)
1. **C-1, C-2, C-3** — C++ memory safety (heap overflow, memory exhaustion, UB)
2. **C-4, C-5** — Client security bootstrap (silent pinning disable, client bricking)
3. **C-6, C-7, C-8** — Deployment configuration (HMAC key, Dockerfiles, DB name)
4. **C-9** — Finalizer invokes FishNet callbacks off main thread
5. **C-10** — WebGL send data loss
6. **C-11** — Session memory leak under connection churn
7. **C-12** — Silent TOTP MFA skip

### Short-Term (Before Public Beta)
1. All HIGH issues (H-1 through H-28)
2. Top 20 MEDIUM issues
3. Complete link.xml for IL2CPP
4. Add missing Dockerfiles
5. Standardize env var names

### Medium-Term (Before Full Launch)
1. All remaining MEDIUM issues
2. All LOW issues
3. Add WebGL platform guards to SharedUtility
4. Implement sampling-based nonce cache eviction
5. Add active health checking
6. Add Redis/shared cache for multi-instance nonce replay protection

### Long-Term (Post-Launch)
1. Informational items
2. Naming convention cleanup
3. Documentation improvements
4. Cross-platform build automation for native transport
5. Automated configuration validation in CI

---

*Audit performed by exhaustive code review of ~550 files across 11 sub-projects. Every file was read in full; no partial reads. All issues are triangulated across subsystems where applicable.*
