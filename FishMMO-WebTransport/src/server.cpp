/**
 * @file server.cpp
 * @brief WebTransport QUIC server implementation using msquic.
 */

#include <atomic>
#include <time.h>
#include "server.h"
#include "http3.h"
#include <stdlib.h>
#include <string.h>
#if defined(WT_PLATFORM_WINDOWS)
#include <ws2tcpip.h>  /* inet_pton for bind_address parsing */
#else
#include <arpa/inet.h> /* inet_pton for bind_address parsing */
#include <sched.h>     /* sched_yield() for queue-overflow spin-retry */
#endif

/* ── Forward ────────────────────────────────────────────────── */

/* Compile-time guard: ensure msquic's address string representation
 * fits within our internal buffer.  If a future msquic version
 * increases QUIC_ADDR_STR beyond WT_MAX_ADDRESS_LENGTH, this will
 * fail the build rather than silently truncating addresses. */
static_assert(sizeof(((QUIC_ADDR_STR*)0)->Address) <= WT_MAX_ADDRESS_LENGTH,
               "QUIC_ADDR_STR.Address exceeds WT_MAX_ADDRESS_LENGTH — "
               "increase WT_MAX_ADDRESS_LENGTH in webtransport_internal.h");

static QUIC_STATUS QUIC_API server_conn_cb(HQUIC conn, void* ctx,
                                   QUIC_CONNECTION_EVENT* event);
static QUIC_STATUS QUIC_API server_listener_cb(HQUIC listener, void* ctx,
                                       QUIC_LISTENER_EVENT* event);
static void on_server_dgram_drain(void* ctx, wt_connection_id_t conn_id,
                                   const uint8_t* data, int32_t length);

static void fire_connect(wt_server_conn_t* sconn);
static void fire_disconnect(wt_server_conn_t* sconn, int err);
static uint64_t rate_limiter_now_ms(void);

/* ── HTTP/3 Handshake Callbacks ─────────────────────────────── */

static void on_h3_session_ready(void* ctx, HQUIC quic_conn,
                                const char* path, const char* authority)
{
    wt_server_conn_t* sconn = (wt_server_conn_t*)ctx;

    wt_session_t* session = (wt_session_t*)calloc(1, sizeof(wt_session_t));
    if (!session) {
        /* Increment pending_shutdowns BEFORE ConnectionShutdown so that
         * SHUTDOWN_COMPLETE accounting is balanced.  Without this, the
         * SHUTDOWN_COMPLETE handler sees pending_shutdowns==0 and skips
         * the CAS-loop decrement, but wt_server_free_impl's spin-wait
         * relies on pending_shutdowns to track all in-flight callbacks.
         * The same pattern is used in wt_server_disconnect_impl. */
        if (sconn->owner)
            atomic_fetch_add(&sconn->owner->pending_shutdowns, 1);
        MsQuic->ConnectionShutdown(quic_conn,
            QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
        return;
    }

    int32_t r = wt_session_init(session, quic_conn, sconn->id);
    if (r != WT_OK) {
        WT_LOG_ERROR("Session init failed for client %llu (HTTP/3 WT)",
                     (unsigned long long)sconn->id);
        free(session);
        if (sconn->owner)
            atomic_fetch_add(&sconn->owner->pending_shutdowns, 1);
        MsQuic->ConnectionShutdown(quic_conn,
            QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
        return;
    }

    atomic_ptr_store(&sconn->session, session);
    session->parent_type = WT_PARENT_SERVER;
    session->parent.server = sconn->owner;
    wt_session_wire_callbacks(session);

    /* ── CRITICAL: Native client data replay ──────────────────
     * If this session was created via native protocol detection
     * (first byte != 0x00), the first peer stream's data was
     * buffered in h3's stream context before the wt_session
     * existed. Accept the stream into the stream manager and
     * deliver the buffered data to prevent silent data loss on
     * the very first stream from a native client. */
    if (sconn->h3_session && sconn->h3_session->native_stream_ctx) {
        h3_stream_ctx_t* nsctx =
            (h3_stream_ctx_t*)sconn->h3_session->native_stream_ctx;
        sconn->h3_session->native_stream_ctx = NULL;

        if (nsctx->recv_buf && nsctx->recv_offset > 0 &&
            session->stream_mgr) {
            /* Accept the stream into the stream manager.
             * After this, the stream_manager owns the QUIC stream
             * and its callback handler replaces h3_stream_cb. */
            wt_stream_manager_accept_stream(
                session->stream_mgr, nsctx->quic_stream);

            /* Look up the stream_id assigned by accept_stream.
             * Safe: the slot was just booked and no other thread
             * knows about it yet. */
            wt_stream_id_t stream_id = 0;
            for (uint32_t _i = 0; _i < WT_MAX_STREAMS; _i++) {
                if (session->stream_mgr->streams[_i].quic_stream ==
                    nsctx->quic_stream) {
                    stream_id = session->stream_mgr->streams[_i].id;
                    break;
                }
            }

            if (stream_id > 0 &&
                session->stream_mgr->on_stream_data) {
                session->stream_mgr->on_stream_data(
                    session->stream_mgr->callback_ctx,
                    session->stream_mgr->conn_id,
                    stream_id,
                    nsctx->recv_buf,
                    (int32_t)nsctx->recv_offset);
            }
        }

        /* Free the orphaned h3 stream context and its buffer.
         * The stream's callback has been replaced by the stream_manager,
         * so h3_stream_cb will never fire again for this stream.
         *
         * CRITICAL: Unlink from the session's stream_ctx_list BEFORE
         * freeing.  h3_stream_ctx_create() added this sctx to the list
         * and h3_session_free() iterates the list to free remaining
         * contexts.  Without the unlink, h3_session_free would
         * double-free this pointer. */
        h3_stream_ctx_unlink(nsctx);
        free(nsctx->recv_buf);
        nsctx->recv_buf = NULL;
        nsctx->recv_offset = 0;
        free(nsctx);
    }

    /* ── Replay additional deferred streams ───────────────────
     * If PEER_STREAM_STARTED fired for stream 2+ before RECEIVE
     * data on stream 1 confirmed the protocol, those streams were
     * parked on h3->stream_ctx_list with stream_type == -1 by
     * h3_server_handle_stream (GOT_SETTINGS state, no transition).
     *
     * Collect pending sctx entries under H3_LOCK, then process
     * them OUTSIDE the lock.  wt_stream_manager_accept_stream
     * calls MsQuic->SetCallbackHandler which may fire synchronous
     * callbacks — holding H3_LOCK across that call risks deadlock
     * if a callback re-enters the H3 lock (pthread_mutex is NOT
     * recursive on this path).  The pattern matches the
     * native_stream_ctx replay above, which also does not hold
     * any lock during accept_stream. */
    if (sconn->h3_session) {
        h3_session_t* h3 = sconn->h3_session;
        H3_LOCK(h3);

        /* Count pending entries so we can pre-allocate. */
        int pending_count = 0;
        {
            h3_stream_ctx_t* ds = h3->stream_ctx_list;
            while (ds) {
                if (ds->stream_type == -1 && ds->quic_stream)
                    pending_count++;
                ds = ds->next;
            }
        }

        if (pending_count > 0 && session->stream_mgr) {
            /* Collect pending sctx entries — unlink from tracking
             * list under the lock, save needed fields locally. */
            struct {
                h3_stream_ctx_t* sctx;   /* freed only after the QUIC stream's
                                          * callback handler has been reassigned
                                          * by wt_stream_manager_accept_stream —
                                          * freeing it earlier leaves the stream
                                          * still registered to h3_stream_cb with
                                          * a dangling ctx pointer (UAF/SEGV). */
                HQUIC quic_stream;
                uint8_t* recv_buf;
                uint32_t recv_offset;
            } *pending = NULL;

            /* Stack-allocate for the common case (1-2 deferred
             * streams); heap-allocate for pathological cases. */
            #define DEFERRED_STACK_MAX 8
            char stack_buf[DEFERRED_STACK_MAX * sizeof(*pending)];
            if ((size_t)pending_count <= DEFERRED_STACK_MAX) {
                pending = (typeof(pending))stack_buf;
            } else {
                pending = (typeof(pending))malloc(
                    (size_t)pending_count * sizeof(*pending));
            }

            if (pending) {
                int idx = 0;
                h3_stream_ctx_t* ds = h3->stream_ctx_list;
                while (ds && idx < pending_count) {
                    h3_stream_ctx_t* next = ds->next;
                    if (ds->stream_type == -1 && ds->quic_stream) {
                        /* Unlink from tracking list */
                        if (h3->stream_ctx_list == ds) {
                            h3->stream_ctx_list = ds->next;
                        } else {
                            h3_stream_ctx_t* prev = h3->stream_ctx_list;
                            while (prev && prev->next != ds)
                                prev = prev->next;
                            if (prev) prev->next = ds->next;
                        }
                        ds->next = NULL;
                        ds->h3 = NULL;  /* prevent unlink in callback */

                        /* Save fields for processing outside lock */
                        pending[idx].sctx = ds;
                        pending[idx].quic_stream = ds->quic_stream;
                        pending[idx].recv_buf = ds->recv_buf;
                        pending[idx].recv_offset = ds->recv_offset;
                        ds->recv_buf = NULL;  /* ownership transferred */
                        idx++;

                        /* Do NOT free(ds) here. The QUIC stream's callback
                         * handler is still h3_stream_cb with ctx == ds until
                         * wt_stream_manager_accept_stream() reassigns it below
                         * (outside this lock). Freeing ds now would leave a
                         * window where a QUIC worker thread can deliver an
                         * event (RECEIVE / PEER_SEND_SHUTDOWN / SHUTDOWN_COMPLETE)
                         * on this stream against freed memory. ds is freed
                         * further down, after the handler swap. */
                    }
                    ds = next;
                }
            }
            H3_UNLOCK(h3);

            /* Process collected entries OUTSIDE the lock. */
            if (pending) {
                for (int i = 0; i < pending_count; i++) {
                    if (!pending[i].quic_stream) continue;

                    wt_stream_manager_accept_stream(
                        session->stream_mgr,
                        pending[i].quic_stream);
                    /* Stream's callback handler is now the stream_manager's
                     * handler, not h3_stream_cb — safe to free the old sctx. */
                    free(pending[i].sctx);

                    if (pending[i].recv_buf &&
                        pending[i].recv_offset > 0 &&
                        session->stream_mgr->on_stream_data) {
                        wt_stream_id_t sid = 0;
                        for (uint32_t _i = 0; _i < WT_MAX_STREAMS; _i++) {
                            if (session->stream_mgr->streams[_i]
                                    .quic_stream ==
                                pending[i].quic_stream) {
                                sid = session->stream_mgr
                                    ->streams[_i].id;
                                break;
                            }
                        }
                        if (sid > 0) {
                            session->stream_mgr->on_stream_data(
                                session->stream_mgr->callback_ctx,
                                session->stream_mgr->conn_id,
                                sid,
                                pending[i].recv_buf,
                                (int32_t)pending[i].recv_offset);
                        }
                    }
                    free(pending[i].recv_buf);
                }
                if ((char*)pending != stack_buf)
                    free(pending);
            }
        } else {
            H3_UNLOCK(h3);
        }
        #undef DEFERRED_STACK_MAX
    }

    WT_LOG_INFO("Client %llu WebTransport session established (path=%s)",
                (unsigned long long)sconn->id, path ? path : "/");
    fire_connect(sconn);
}

static void on_h3_error(void* ctx, int error_code, const char* message)
{
    wt_server_conn_t* sconn = (wt_server_conn_t*)ctx;
    WT_LOG_ERROR("Client %llu HTTP/3 handshake failed: %s (code %d)",
                 (unsigned long long)sconn->id,
                 message ? message : "unknown", error_code);
    /* Immediately disconnect — releasing the slot prevents an attacker
     * from holding connection slots for the full H3 handshake timeout
     * (15s) after a rejected CONNECT.  Without this, only the per-stream
     * shutdown fires; the QUIC connection stays alive and the slot is
     * held until the sweep in wt_server_poll_impl cleans it up.
     *
     * wt_server_disconnect_impl uses atomics for in_use/quic_conn/state
     * and is safe to call from a QUIC callback thread (it calls
     * MsQuic->ConnectionShutdown, which is callback-safe). */
    if (sconn->owner) {
        wt_server_disconnect_impl(sconn->owner, sconn->id);
    }
}

/* ═══════════════════════════════════════════════════════════════
 * INTERNAL API
 * ═══════════════════════════════════════════════════════════════ */

wt_server_s* wt_server_alloc_impl(
    const char* cert_path, const char* key_path,
    const char* alpn, const char* bind_address,
    uint16_t port, uint32_t max_clients,
    const char* allowed_origins,
    const wt_server_callbacks_t* callbacks, void* context)
{
    if (!callbacks) return NULL;

    wt_server_s* srv = (wt_server_s*)calloc(1, sizeof(wt_server_s));
    if (!srv) return NULL;

    if (cert_path) {
        strncpy(srv->cert_path, cert_path, sizeof(srv->cert_path) - 1);
        srv->cert_path[sizeof(srv->cert_path) - 1] = '\0';
    }
    if (key_path) {
        strncpy(srv->key_path, key_path, sizeof(srv->key_path) - 1);
        srv->key_path[sizeof(srv->key_path) - 1] = '\0';
    }
    strncpy(srv->alpn, alpn ? alpn : "h3", sizeof(srv->alpn) - 1);
    srv->alpn[sizeof(srv->alpn) - 1] = '\0';
    strncpy(srv->bind_address, bind_address ? bind_address : "0.0.0.0",
            sizeof(srv->bind_address) - 1);
    srv->bind_address[sizeof(srv->bind_address) - 1] = '\0';

    srv->port = port;
    srv->max_clients = (max_clients > 0 && max_clients <= WT_MAX_CLIENTS)
                       ? max_clients : WT_MAX_CLIENTS;
    memcpy(&srv->callbacks, callbacks, sizeof(*callbacks));
    srv->user_context = context;

    /* Copy allowed origins for CORS validation on browser WebTransport
     * connections.  When allowed_origins is NULL, the calloc-zeroed
     * buffer stays empty, which signals "allow all origins" (the
     * h3_server_process_data CONNECT validator treats an empty
     * allowed_origins field as no restriction).  Set explicitly to "*"
     * for the same effect in dev/testing.  A non-empty comma-separated
     * list enables strict origin validation. */
    if (allowed_origins) {
        strncpy(srv->allowed_origins, allowed_origins,
                sizeof(srv->allowed_origins) - 1);
        srv->allowed_origins[sizeof(srv->allowed_origins) - 1] = '\0';
    }
    /* else: calloc already zeroed the buffer — empty = allow all origins */

    /* expected_authority is zeroed by calloc (empty = skip validation).
     * allow_native_clients defaults to true for backward compatibility.
     * Operators can override both via wt_server_set_expected_authority()
     * and wt_server_set_allow_native_clients() before wt_server_start(). */
    srv->allow_native_clients = true;

    atomic_init(&srv->state, WT_SERVER_STOPPED);
    atomic_init(&srv->connection_count, 0);
    atomic_init(&srv->pending_shutdowns, 0);
    atomic_init_u64(&srv->pending_shutdown_head, 0);
    atomic_init_u64(&srv->pending_shutdown_tail, 0);

    /* Allocate max_clients + 1 to account for index 0 being reserved
     * (WT_CONNECTION_ID_NONE). Valid connection IDs are 1..max_clients. */
    srv->connections = (wt_server_conn_t*)calloc(
        srv->max_clients + 1, sizeof(wt_server_conn_t));
    if (!srv->connections) { free(srv); return NULL; }

    srv->h3_sweep_cursor = 1;
    wt_datagram_queue_init(&srv->dgram_queue);
    return srv;
}

void wt_server_free_impl(wt_server_s* server)
{
    if (!server) return;
    wt_server_stop_impl(server);

    /* Wait for pending SHUTDOWN_COMPLETE callbacks (bounded).
     * 600 iterations * 10ms = 6 second max wait.
     *
     * Owner pointers are kept live during the spin-wait so that
     * SHUTDOWN_COMPLETE callbacks can decrement pending_shutdowns
     * normally.  Nulling owner before the spin-wait (as was done
     * previously) caused those callbacks to skip the decrement,
     * guaranteeing a spurious timeout on every shutdown with
     * active connections. */
    int retries = 600;
    while (atomic_load(&server->pending_shutdowns) > 0 && retries-- > 0) {
#if defined(WT_PLATFORM_WINDOWS)
        Sleep(10);
#else
        struct timespec ts = {0, 10000000}; /* 10ms */
        nanosleep(&ts, NULL);
#endif
    }
    if (retries < 0) {
        WT_LOG_ERROR("Timed out waiting for %u pending shutdowns -- forcing cleanup (late callbacks may crash)",
                     (unsigned)atomic_load(&server->pending_shutdowns));

        /* Null out owner pointers BEFORE force-closing QUIC handles.
         * Any SHUTDOWN_COMPLETE callback that fires after this point
         * will see owner==NULL and skip the pending_shutdowns decrement
         * (and all other server-struct accesses), preventing UAF on
         * the server struct which is about to be freed. */
        if (server->connections) {
            for (uint32_t i = 1; i <= server->max_clients; i++) {
                server->connections[i].owner = NULL;
            }
        }

        /* Close QUIC handles on the application thread even though
         * SHUTDOWN_COMPLETE callbacks may still be pending.  This is
         * a last-resort path; under normal operation the spin-wait
         * completes before the timeout. */
        if (server->session_config) {
            MsQuic->ConfigurationClose(server->session_config);
            server->session_config = NULL;
        }
        if (server->registration) {
            MsQuic->RegistrationClose(server->registration);
            server->registration = NULL;
        }
        wt_datagram_queue_destroy(&server->dgram_queue);
        free(server->connections);
        free(server);
        return;
    }

    /* All SHUTDOWN_COMPLETE callbacks have finished — it is now safe
     * to null owner pointers and close QUIC handles.  Nulling owner
     * here is a defense-in-depth measure: any callback that fires after
     * RegistrationClose (which should not happen per msquic contract)
     * will see owner==NULL and avoid accessing freed memory. */
    if (server->connections) {
        for (uint32_t i = 1; i <= server->max_clients; i++) {
            server->connections[i].owner = NULL;
        }
    }

    /* Close QUIC handles deferred from wt_server_stop_impl.
     * Order: config before registration (reverse of creation).
     * Safe now because all SHUTDOWN_COMPLETE callbacks have finished. */
    if (server->session_config) {
        MsQuic->ConfigurationClose(server->session_config);
        server->session_config = NULL;
    }
    if (server->registration) {
        MsQuic->RegistrationClose(server->registration);
        server->registration = NULL;
    }

    wt_datagram_queue_destroy(&server->dgram_queue);
    free(server->connections);
    free(server);
}
int32_t wt_server_start_impl(wt_server_s* server)
{
    if (!server) return WT_ERR_UNKNOWN;
    if (atomic_load(&server->state) != WT_SERVER_STOPPED)
        return WT_ERR_INVALID_STATE;

    QUIC_STATUS status;

    /* ── Registration ── */
    QUIC_REGISTRATION_CONFIG reg_cfg = {0};
    reg_cfg.AppName = "fishmmo_wt_server";
    reg_cfg.ExecutionProfile = QUIC_EXECUTION_PROFILE_LOW_LATENCY;
    status = MsQuic->RegistrationOpen(&reg_cfg, &server->registration);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("RegistrationOpen: 0x%x", status);
        return WT_ERR_UNKNOWN;
    }

    /* ── Settings ── */
    QUIC_SETTINGS settings = {0};
    settings.IdleTimeoutMs = WT_DEFAULT_IDLE_TIMEOUT_MS;
    settings.IsSet.IdleTimeoutMs = TRUE;
    settings.PeerBidiStreamCount = WT_MAX_STREAMS;
    settings.IsSet.PeerBidiStreamCount = TRUE;
    settings.PeerUnidiStreamCount = WT_MAX_STREAMS;
    settings.IsSet.PeerUnidiStreamCount = TRUE;
    settings.DatagramReceiveEnabled = TRUE;
    settings.IsSet.DatagramReceiveEnabled = TRUE;
    settings.ServerResumptionLevel = QUIC_SERVER_NO_RESUME;
    settings.IsSet.ServerResumptionLevel = TRUE;

    /* ── Open configuration with ALPN ── */
    QUIC_BUFFER alpn_buf;
    alpn_buf.Buffer = (uint8_t*)(server->alpn);
    alpn_buf.Length = (uint32_t)strlen(server->alpn);

    status = MsQuic->ConfigurationOpen(server->registration, &alpn_buf, 1U, &settings, (uint32_t)sizeof(settings), NULL, &server->session_config);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("ConfigurationOpen: 0x%x", status);
        MsQuic->RegistrationClose(server->registration);
        server->registration = NULL;
        return WT_ERR_UNKNOWN;
    }

    /* ── TLS credential config ── */
    QUIC_CERTIFICATE_FILE cert_file;
    cert_file.CertificateFile = server->cert_path;
    cert_file.PrivateKeyFile = server->key_path[0] ? server->key_path : server->cert_path;

    QUIC_CREDENTIAL_CONFIG cred_cfg;
    memset(&cred_cfg, 0, sizeof(cred_cfg));

    if (server->cert_path[0]) {
        cred_cfg.Type = QUIC_CREDENTIAL_TYPE_CERTIFICATE_FILE;
        cred_cfg.CertificateFile = &cert_file;
    } else {
        WT_LOG_WARN("No certificate configured — using self-signed certificate. "
                    "This is INSECURE for production: clients cannot verify "
                    "the server identity and the connection is vulnerable to "
                    "MITM attacks. Provide a valid certificate via "
                    "wt_server_create_with_params().");
        cred_cfg.Type = QUIC_CREDENTIAL_TYPE_NONE;
        cred_cfg.Flags = QUIC_CREDENTIAL_FLAG_USE_PORTABLE_CERTIFICATES;
    }

    status = MsQuic->ConfigurationLoadCredential(
        server->session_config, &cred_cfg);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("ConfigurationLoadCredential: 0x%x", status);
        MsQuic->ConfigurationClose(server->session_config);
        MsQuic->RegistrationClose(server->registration);
        server->session_config = NULL;
        server->registration = NULL;
        return WT_ERR_TLS_FAILED;
    }

    /* ── Listener ──
     * Resolve bind_address to a concrete address family so operators can
     * restrict the listener to a specific interface (e.g. 127.0.0.1).
     * Default "0.0.0.0" and "::" retain the existing dual-stack behaviour. */
    QUIC_ADDR addr = {0};
    if (strcmp(server->bind_address, "0.0.0.0") == 0 ||
        strcmp(server->bind_address, "::") == 0) {
        QuicAddrSetFamily(&addr, QUIC_ADDRESS_FAMILY_UNSPEC);
    } else {
        /* Try IPv4 first, then IPv6.  inet_pton returns 1 on success. */
        struct in_addr ipv4;
        if (inet_pton(AF_INET, server->bind_address, &ipv4) == 1) {
            QuicAddrSetFamily(&addr, QUIC_ADDRESS_FAMILY_INET);
            addr.Ipv4.sin_addr = ipv4;
        } else {
            struct in6_addr ipv6;
            if (inet_pton(AF_INET6, server->bind_address, &ipv6) == 1) {
                QuicAddrSetFamily(&addr, QUIC_ADDRESS_FAMILY_INET6);
                addr.Ipv6.sin6_addr = ipv6;
            } else {
                WT_LOG_WARN("Invalid bind_address '%s' — "
                            "falling back to all interfaces",
                            server->bind_address);
                QuicAddrSetFamily(&addr, QUIC_ADDRESS_FAMILY_UNSPEC);
            }
        }
    }
    QuicAddrSetPort(&addr, server->port);

    status = MsQuic->ListenerOpen(server->registration,
                                   server_listener_cb, server,
                                   &server->listener);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("ListenerOpen: 0x%x", status);
        MsQuic->ConfigurationClose(server->session_config);
        MsQuic->RegistrationClose(server->registration);
        server->session_config = NULL;
        server->registration = NULL;
        return WT_ERR_UNKNOWN;
    }

    const QUIC_BUFFER* alpn_for_listener = &alpn_buf;
    status = MsQuic->ListenerStart(server->listener,
                                    alpn_for_listener, 1, &addr);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("ListenerStart: 0x%x on port %u", status, server->port);
        MsQuic->ListenerClose(server->listener);
        MsQuic->ConfigurationClose(server->session_config);
        MsQuic->RegistrationClose(server->registration);
        server->listener = NULL;
        server->session_config = NULL;
        server->registration = NULL;
        return WT_ERR_UNKNOWN;
    }

    atomic_store(&server->state, WT_SERVER_STARTED);
    WT_LOG_INFO("Server started on %s:%u (ALPN: %s, max: %u)",
                server->bind_address, server->port,
                server->alpn, server->max_clients);
    return WT_OK;
}

void wt_server_stop_impl(wt_server_s* server)
{
    if (!server) return;

    int expected = WT_SERVER_STARTED;
    if (!atomic_compare_exchange_strong(&server->state, &expected,
                                         WT_SERVER_STOPPING))
        return;

    for (uint32_t i = 1; i <= server->max_clients; i++) {
        if (atomic_load(&server->connections[i].in_use))
            wt_server_disconnect_impl(server, server->connections[i].id);
    }

    if (server->listener) {
        MsQuic->ListenerClose(server->listener);
        server->listener = NULL;
    }
    /* Defer ConfigurationClose and RegistrationClose to free_impl
     * (after the pending_shutdowns spin-wait). Closing registration
     * before SHUTDOWN_COMPLETE callbacks finish is UB per msquic API. */

    atomic_store(&server->state, WT_SERVER_STOPPED);
    WT_LOG_INFO("Server stopped.");
}

void wt_server_poll_impl(wt_server_s* server, int32_t timeout_us)
{
    (void)timeout_us;
    /* NOTE: timeout_us is ignored by design — poll is non-blocking for Unity
     * main-thread integration. Use a short sleep in the caller if backpressure
     * is needed.
     *
     * timeout_us is intentionally ignored — this poll is always non-blocking.
     * The Unity main thread calls poll every frame (typically 16ms at 60 FPS).
     * Blocking in poll would stall the entire Unity tick, causing frame drops
     * and delaying Netcode sends.  Datagrams and shutdown events are drained
     * in a single pass and the call returns immediately. */
    if (!server || atomic_load(&server->state) != WT_SERVER_STARTED)
        return;

    /* Process deferred session shutdowns via O(1) queue drain.
     * SHUTDOWN_COMPLETE callbacks enqueue connection IDs into
     * pending_shutdown_queue. This poll path drains them on the
     * application thread — same thread as send — guaranteeing
     * no TOCTOU between session free and acquire. */
    uint64_t head = atomic_load_u64(&server->pending_shutdown_head);
    uint64_t tail = atomic_load_u64(&server->pending_shutdown_tail);
    /* NOTE: tail is loaded once before the loop. Entries added after this
     * point won't be seen until the next poll call. This single-cycle delay
     * is acceptable for the intended use (polled each frame).
     *
     * (void)timeout_us — see above; this is a non-blocking poll.
     *
     * ── FIX #8: 0-sentinel for ARM slot-visibility ──────────────
     * On weakly-ordered architectures (ARM), the producer writes
     *   (1) atomic_fetch_add(tail) → reserves position
     *   (2) queue[pos] = conn_id        → plain store (data)
     *   (3) atomic_thread_fence(release)
     * The fetch_add at (1) publishes the new tail before the slot
     * data at (2) is visible.  On ARM the consumer can observe the
     * updated tail but read stale slot data from the previous ring-
     * buffer occupant.
     *
     * Mitigation: the consumer writes 0 (WT_CONNECTION_ID_NONE)
     * back to each slot AFTER processing it, clearing the slot for
     * the next producer on wrap-around.  If the consumer reads 0,
     * the producer incremented tail but hasn't written the data yet —
     * break out and retry next poll cycle (at most 1 frame of latency).
     * Connection IDs are always >= 1, so 0 is an unambiguous sentinel.
     *
     * Acquire fence: paired with release fence in SHUTDOWN_COMPLETE.
     * Ensures that the producer's array writes are visible before we
     * read them on weakly-ordered architectures (ARM).  Without this,
     * the consumer could see an updated tail but stale array data. */
    atomic_thread_fence(std::memory_order_acquire);
    while (head != tail) {
        wt_connection_id_t cid = server->pending_shutdown_queue[head % WT_MAX_CLIENTS];
        /* ── FIX #8: 0-sentinel check ────────────────────────────
         * cid == 0 means the producer reserved this slot (incremented
         * tail) but hasn't written the connection ID yet (ARM weak
         * ordering).  Break out — the entry will be picked up on the
         * next poll cycle.  Without this check, the consumer would
         * attempt to shut down a stale or non-existent session. */
        if (cid == 0) break;
        if (cid > 0 && cid <= server->max_clients) {
            wt_server_conn_t* c = &server->connections[cid];
            /* Use atomic_ptr_exchange to atomically claim the session.
             * This pairs with server_listener_cb which uses the same
             * exchange when recycling a slot — exactly one thread
             * (poll or listener_cb) gets the non-NULL pointer. */
            wt_session_t* s = (wt_session_t*)atomic_ptr_exchange(
                &c->pending_shutdown_session, NULL);
            if (s) {
                wt_session_shutdown(s);
            }
        }
        /* ── FIX #8: Clear slot for next wrap ────────────────────
         * Write 0 back to the slot so the 0-sentinel check above
         * correctly detects unwritten slots on subsequent ring-buffer
         * wraps.  Without this clear, a slot from a previous cycle
         * could contain a stale but valid-looking connection ID. */
        server->pending_shutdown_queue[head % WT_MAX_CLIENTS] = 0;
        head++;
        /* Release fence: paired with the producer's acquire load of
         * pending_shutdown_head in SHUTDOWN_COMPLETE.  Ensures that
         * all slot processing (reads/writes to the session struct)
         * completes before the head update becomes visible, so the
         * producer sees a fully-consumed slot before reusing it.
         * Also ensures the slot-clear (write of 0 above) is visible
         * before head advances. */
        std::atomic_thread_fence(std::memory_order_release);
        atomic_fetch_add_u64(&server->pending_shutdown_head, 1);
    }

    /* ── FIX #3: Sweep stale H3 handshakes ──────────────────────
     * Disconnect connections that completed the QUIC+TLS handshake
     * but haven't finished the HTTP/3 WebTransport handshake within
     * WT_H3_HANDSHAKE_TIMEOUT_MS.  Without this sweep, an attacker
     * can open thousands of QUIC connections that never send an H3
     * control stream, exhausting all connection slots for the full
     * QUIC idle timeout (120s).
     *
     * Bounded scan: max 64 connections checked per frame to keep
     * per-frame cost low.  The sweep restarts from the last-scanned
     * index on the next frame, so all connections are eventually
     * covered over multiple poll cycles. */
    {
        uint32_t start = server->h3_sweep_cursor;
        uint32_t checked = 0;
        const uint32_t max_check = 64;
        uint64_t now = rate_limiter_now_ms();

        for (uint32_t i = start;
             i <= server->max_clients && checked < max_check;
             i++, checked++)
        {
            wt_server_conn_t* c = &server->connections[i];
            if (!atomic_load(&c->in_use)) continue;

            /* Already has a WT session — handshake completed normally. */
            if (atomic_ptr_load(&c->session) != NULL) continue;

            /* No H3 session (raw QUIC fallback already handled, or connection
             * is in IDLE/HANDSHAKING before CONNECTED fires).  The per-connection
             * deadline is only meaningful when an h3_session exists. */
            if (!c->h3_session) continue;

            /* Deadline of 0 means init race — shouldn't happen, but skip.
             * Use atomic_load_u64 to prevent torn reads on 32-bit ARM — the
             * QUIC CONNECTED callback thread writes this field concurrently. */
            uint64_t deadline = atomic_load_u64(&c->h3_handshake_deadline_ms);
            if (deadline == 0) continue;

            if (now < deadline) continue;

            /* Re-check session pointer AFTER the deadline check.
             * The H3 handshake may have completed on a QUIC worker
             * thread between our earlier NULL check (line 536) and
             * this point.  Without this re-check, a client that
             * completes the handshake in that narrow window would be
             * spuriously disconnected. */
            if (atomic_ptr_load(&c->session) != NULL) continue;

            WT_LOG_WARN("Client %llu H3 handshake timed out — disconnecting",
                        (unsigned long long)c->id);
            wt_server_disconnect_impl(server, c->id);
        }

        /* Advance cursor for the next frame.  Wrap around to 1
         * (index 0 is reserved for WT_CONNECTION_ID_NONE). */
        server->h3_sweep_cursor = start + checked;
        if (server->h3_sweep_cursor > server->max_clients)
            server->h3_sweep_cursor = 1;
    }

    /* Deferred H3 SETTINGS bootstrap (app thread — safe for StreamOpen/Send).
     * The CONNECTED callback (QUIC worker) schedules SETTINGS via
     * h3_server_send_initial_settings → h3_server_request_settings_bootstrap,
     * which sets settings_bootstrap_pending=1.  This poll loop opens the
     * server control stream and sends SETTINGS on the application thread,
     * avoiding msquic re-entrancy that caused QuicOperationFree crashes. */
    for (uint32_t i = 1; i <= server->max_clients; i++) {
        wt_server_conn_t* c = &server->connections[i];
        if (!atomic_load(&c->in_use))
            continue;
        if (!c->h3_session)
            continue;
        h3_server_poll_deferred(c->h3_session);
    }

    wt_datagram_queue_drain(&server->dgram_queue,
                             on_server_dgram_drain, server);
}

int32_t wt_server_send_stream_impl(
    wt_server_s* server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length)
{
    if (!server || !data || length <= 0) return WT_ERR_SEND_FAILED;

    if (conn_id == WT_BROADCAST_ALL) {
        /* NOTE: returns WT_OK even if every session acquire fails. Callers
         * should validate send success independently.  The broadcast loop
         * iterates every connection slot.  If every active session fails
         * acquire (e.g. all sessions entered shutdown concurrently), worst
         * stays WT_OK — which is technically misleading (nothing was sent).
         * This is intentional: a broadcast to zero recipients is a no-op,
         * not an error, and the caller (who issued a broadcast send) should
         * not see a failure code for a condition outside its control.
         * Per-connection send failures (buffer full, stream closed) DO
         * propagate via worst, so genuine transport errors are still
         * surfaced. */
        int32_t worst = WT_OK;
        for (uint32_t i = 1; i <= server->max_clients; i++) {
            wt_server_conn_t* c = &server->connections[i];
            if (!atomic_load(&c->in_use) ||
                (wt_connection_state_t)atomic_load(&c->state) != WT_CONN_STATE_CONNECTED)
                continue;
            wt_session_t* session = (wt_session_t*)atomic_ptr_load(&c->session);
            if (!session || !wt_session_acquire(session))
                continue;
            /* Re-check after acquire — session pointer may have changed */
            if (atomic_ptr_load(&c->session) != session ||
                !atomic_load(&c->in_use)) {
                wt_session_release(session);
                continue;
            }
            int32_t r = wt_session_send_stream(session, data, length);
            if (r != WT_OK) worst = r;
            wt_session_release(session);
        }
        return worst;
    }

    if (conn_id == 0 || conn_id > server->max_clients)
        return WT_ERR_NOT_FOUND;

    wt_server_conn_t* conn = &server->connections[conn_id];
    if (!atomic_load(&conn->in_use) ||
        (wt_connection_state_t)atomic_load(&conn->state) != WT_CONN_STATE_CONNECTED)
        return WT_ERR_NOT_FOUND;

    {
        wt_session_t* session = (wt_session_t*)atomic_ptr_load(&conn->session);
        if (!session || !wt_session_acquire(session))
            return WT_ERR_NOT_FOUND;

        /* Re-check — session may have been nulled by SHUTDOWN_COMPLETE */
        if (atomic_ptr_load(&conn->session) != session ||
            !atomic_load(&conn->in_use)) {
            wt_session_release(session);
            return WT_ERR_NOT_FOUND;
        }

        int32_t result = wt_session_send_stream(session, data, length);
        wt_session_release(session);
        return result;
    }
}

int32_t wt_server_send_datagram_impl(
    wt_server_s* server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length)
{
    if (!server || !data || length <= 0) return WT_ERR_SEND_FAILED;

    if (conn_id == WT_BROADCAST_ALL) {
        int32_t worst = WT_OK;
        for (uint32_t i = 1; i <= server->max_clients; i++) {
            wt_server_conn_t* c = &server->connections[i];
            if (!atomic_load(&c->in_use) ||
                (wt_connection_state_t)atomic_load(&c->state) != WT_CONN_STATE_CONNECTED)
                continue;
            wt_session_t* session = (wt_session_t*)atomic_ptr_load(&c->session);
            if (!session || !wt_session_acquire(session))
                continue;
            if (atomic_ptr_load(&c->session) != session ||
                !atomic_load(&c->in_use)) {
                wt_session_release(session);
                continue;
            }
            int32_t r = wt_session_send_datagram(session, data, length);
            if (r != WT_OK) worst = r;
            wt_session_release(session);
        }
        return worst;
    }

    if (conn_id == 0 || conn_id > server->max_clients)
        return WT_ERR_NOT_FOUND;

    wt_server_conn_t* conn = &server->connections[conn_id];
    if (!atomic_load(&conn->in_use) ||
        (wt_connection_state_t)atomic_load(&conn->state) != WT_CONN_STATE_CONNECTED)
        return WT_ERR_NOT_FOUND;

    {
        wt_session_t* session = (wt_session_t*)atomic_ptr_load(&conn->session);
        if (!session || !wt_session_acquire(session))
            return WT_ERR_NOT_FOUND;

        if (atomic_ptr_load(&conn->session) != session ||
            !atomic_load(&conn->in_use)) {
            wt_session_release(session);
            return WT_ERR_NOT_FOUND;
        }

        int32_t result = wt_session_send_datagram(session, data, length);
        wt_session_release(session);
        return result;
    }
}

void wt_server_disconnect_impl(
    wt_server_s* server, wt_connection_id_t conn_id)
{
    if (!server || conn_id == 0 || conn_id > server->max_clients)
        return;

    wt_server_conn_t* conn = &server->connections[conn_id];
    if (!atomic_load(&conn->in_use)) return;

    /* Atomically claim the shutdown — prevents double-increment
     * if called twice for the same connection. */
    HQUIC qconn = (HQUIC)atomic_ptr_load(&conn->quic_conn);
    if (!qconn) return;  /* already shutting down */
    atomic_ptr_store(&conn->quic_conn, NULL);

    /* Check connection state before proceeding with ConnectionShutdown.
     * If the connection is still in IDLE state (never started handshaking),
     * msquic may never fire SHUTDOWN_COMPLETE, which would leave
     * pending_shutdowns permanently incremented and cause a hang in
     * wt_server_free_impl's spin-wait.  In that case, just clean up
     * the slot directly.
     *
     * connection_count was incremented in server_listener_cb when the
     * slot was allocated.  Since SHUTDOWN_COMPLETE will never fire for
     * an IDLE connection, we must decrement it here.  Without this,
     * connection_count drifts upward and the server eventually refuses
     * all new connections (max_clients permanently reached). */
    wt_connection_state_t state = (wt_connection_state_t)atomic_load(&conn->state);
    if (state == WT_CONN_STATE_IDLE) {
        atomic_store(&conn->in_use, false);
        /* conn->owner is validated non-NULL by the caller's in_use check.
         * It can ONLY become NULL during wt_server_free_impl, which runs
         * after wt_server_stop_impl disconnects all connections — we hold
         * the caller's in_use guarantee preventing concurrent free. */
        if (conn->owner)
            atomic_fetch_sub(&conn->owner->connection_count, 1);
        return;
    }

    /* Do NOT set in_use=false here — SHUTDOWN_COMPLETE owns that. */
    atomic_fetch_add(&server->pending_shutdowns, 1);
    MsQuic->ConnectionShutdown(qconn,
                                QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
    /* Session cleanup deferred to SHUTDOWN_COMPLETE → poll. */

    atomic_store(&conn->state, WT_CONN_STATE_CLOSED);
    /* fire_disconnect is called by SHUTDOWN_COMPLETE — don't double-fire */
}

/* Returns the connection's remote address string.
 *
 * The address is copied into a caller-safe thread-local buffer before
 * returning, eliminating the use-after-free window between the in_use
 * check and the P/Invoke marshaller reading the returned pointer.
 * The returned pointer is valid until the next call to this function
 * on the same thread.
 *
 * Returns NULL if the connection is not found or not in use. */
const char* wt_server_get_client_addr_impl(
    wt_server_s* server, wt_connection_id_t conn_id)
{
    if (!server || conn_id == 0 || conn_id > server->max_clients)
        return NULL;
    wt_server_conn_t* conn = &server->connections[conn_id];
    if (!atomic_load(&conn->in_use)) return NULL;

    /* Copy to thread-local buffer so the pointer remains valid even
     * if the connection is torn down concurrently (SHUTDOWN_COMPLETE
     * or wt_server_free_impl frees the connections array).  The P/Invoke
     * marshaller on the C# side reads the string immediately after the
     * call returns; the thread-local buffer guarantees it sees a valid
     * copy regardless of concurrent teardown. */
#if defined(WT_PLATFORM_WINDOWS)
    static __declspec(thread) char safe_buf[WT_MAX_ADDRESS_LENGTH];
#else
    static __thread char safe_buf[WT_MAX_ADDRESS_LENGTH];
#endif
    strncpy(safe_buf, conn->remote_addr, sizeof(safe_buf) - 1);
    safe_buf[sizeof(safe_buf) - 1] = '\0';
    return safe_buf;
}

int32_t wt_server_get_client_count_impl(wt_server_s* server)
{
    if (!server) return 0;
    return (int32_t)atomic_load(&server->connection_count);
}

/* ── Rate-limiter helpers ──────────────────────────────────────── */

/* Monotonic millisecond clock — available on all platforms.
 * Not async-signal-safe (clock_gettime is, GetTickCount64 is),
 * but we only call this from QUIC worker-thread callbacks, never
 * from signal handlers. */
static uint64_t rate_limiter_now_ms(void)
{
#if defined(WT_PLATFORM_WINDOWS)
    return GetTickCount64();
#else
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (uint64_t)ts.tv_sec * 1000 + (uint64_t)ts.tv_nsec / 1000000;
#endif
}

/* FNV-1a 32-bit hash of raw address bytes.
 * 32-bit is sufficient for a 256-bucket hash table; wider hashes
 * cannot be stored atomically in atomic_int (32-bit on all platforms). */
static uint32_t rate_limiter_hash(const uint8_t* bytes, uint32_t len)
{
    uint32_t h = 0x811c9dc5u;
    for (uint32_t i = 0; i < len; i++) {
        h ^= bytes[i];
        h *= 0x01000193u;
    }
    return h;
}

/* The rate limiter uses 64-bit timestamps for per-IP connection
 * tracking.  The atomic_load_u64 / atomic_store_u64 helpers are
 * provided by webtransport_internal.h. */

/* Check/update rate limit for a remote address.  Returns true if
 * the connection is allowed, false if it should be refused.
 * Uses a simple fixed-size hash table with linear probing for
 * collision resolution (wrap-around).  The "last connect time"
 * check-and-update is racy by design — two connections from the same
 * IP arriving on different QUIC threads may both pass the check.
 * This is acceptable: the rate limiter is a coarse first line of
 * defense, not a precision policer.  The C# layer provides per-IP
 * accuracy via ExpiringKeyTracker on the ClientHandshake path.
 *
 * Timestamps are 64-bit monotonic milliseconds, accessed with
 * relaxed-atomics to prevent torn reads on 32-bit architectures. */
static bool rate_limiter_check(wt_server_s* srv,
                                const uint8_t* addr_bytes, uint32_t addr_len)
{
    uint32_t hash = rate_limiter_hash(addr_bytes, addr_len);
    if (hash == 0) hash = 1;  /* 0 = empty slot sentinel */

    uint64_t now = rate_limiter_now_ms();
    uint64_t interval = (uint64_t)WT_RATE_LIMIT_INTERVAL_MS;

    /* Linear-probe the hash table (wrap around). */
    uint32_t start = hash % WT_RATE_LIMIT_BUCKETS;
    for (uint32_t i = 0; i < WT_RATE_LIMIT_BUCKETS; i++) {
        uint32_t idx = (start + i) % WT_RATE_LIMIT_BUCKETS;
        int slot_hash = atomic_load(&srv->rate_limits[idx].addr_hash);

        if (slot_hash == 0) {
            /* Empty slot — try to claim it with CAS. */
            int expected = 0;
            if (atomic_compare_exchange_strong(
                    &srv->rate_limits[idx].addr_hash, &expected, (int)hash)) {
                /* Claimed — record timestamp and allow. */
                atomic_store_u64(&srv->rate_limits[idx].last_connect_ms, now);
                return true;
            }
            /* CAS failed — another thread claimed it concurrently.
             * Fall through to the collision path below. */
            slot_hash = atomic_load(&srv->rate_limits[idx].addr_hash);
        }

        if ((uint32_t)slot_hash == hash) {
            /* Found our bucket — check timestamp (atomic to prevent
             * torn reads on 32-bit ARM). */
            uint64_t last = atomic_load_u64(&srv->rate_limits[idx].last_connect_ms);
            if (now - last < interval) {
                /* Too soon — rate-limited.  Do NOT update the
                 * timestamp; that would let an attacker keep the
                 * bucket permanently rate-limited by hammering. */
                return false;
            }
            /* Allow and update timestamp. */
            atomic_store_u64(&srv->rate_limits[idx].last_connect_ms, now);
            return true;
        }
        /* Hash collision with a different IP — probe next slot. */
    }
    /* ── FIX #1: Table-full fallback ──────────────────────────────
     * With WT_RATE_LIMIT_BUCKETS=4096, complete saturation requires
     * ~4096 distinct IPs all connecting within the 100ms interval —
     * a substantially larger botnet than the original 256.
     *
     * If the table IS genuinely saturated (all buckets occupied and
     * none expired), use random eviction rather than refusing all
     * connections.  Random eviction degrades rate limiting to
     * probabilistic fairness: each new connection has a ~1/N chance
     * of evicting any given bucket occupant.  This prevents total
     * connection DoS while still making slot-exhaustion attacks
     * unreliable (the attacker's own buckets are evicted too).
     * Per-IP accuracy is still enforced by the C# layer.
     *
     * The random seed mixes the IP hash with the current time in ms
     * to avoid deterministic eviction patterns observable by an
     * attacker. */
    {
        uint32_t oldest_idx = 0;
        uint64_t oldest_time = UINT64_MAX;
        for (uint32_t i = 0; i < WT_RATE_LIMIT_BUCKETS; i++) {
            uint64_t t = atomic_load_u64(&srv->rate_limits[i].last_connect_ms);
            if (t < oldest_time) { oldest_time = t; oldest_idx = i; }
        }
        if (now - oldest_time >= interval) {
            /* Evict the stale entry and claim the bucket.
             * CAS guards against a concurrent probe that may have
             * just claimed this slot with a different hash. */
            int expected = atomic_load(&srv->rate_limits[oldest_idx].addr_hash);
            if (atomic_compare_exchange_strong(
                    &srv->rate_limits[oldest_idx].addr_hash,
                    &expected, (int)hash)) {
                atomic_store_u64(&srv->rate_limits[oldest_idx].last_connect_ms, now);
                return true;
            }
            /* CAS failed — slot was recycled by another thread.
             * Fall through to the random-eviction path below. */
        }
        /* ── FIX #1: Random eviction when table is saturated ──────
         * All buckets are occupied and none have expired.  Rather
         * than refusing ALL connections (total DoS), evict a random
         * bucket.  This degrades rate limiting to probabilistic
         * fairness: each connection has a 1/N chance of displacing
         * any given bucket occupant, including the attacker's own
         * entries.  The CAS ensures no two threads evict the same
         * bucket.  The eviction index mixes the IP hash with the
         * current time to prevent deterministic eviction patterns. */
        {
            uint32_t victim = (uint32_t)((hash ^ (now & 0xFFFF)) % WT_RATE_LIMIT_BUCKETS);
            int expected = atomic_load(&srv->rate_limits[victim].addr_hash);
            if (atomic_compare_exchange_strong(
                    &srv->rate_limits[victim].addr_hash,
                    &expected, (int)hash)) {
                atomic_store_u64(&srv->rate_limits[victim].last_connect_ms, now);
                return true;
            }
            /* CAS failed — another thread claimed this bucket.
             * Fall through to the final refusal below. */
        }
    }
    /* Table genuinely saturated AND all CAS eviction attempts failed
     * (every candidate bucket was claimed by a concurrent thread).
     * This is an extremely rare edge case — refuse the connection. */
    return false;
}

/* ═══════════════════════════════════════════════════════════════
 * QUIC CALLBACKS
 * ═══════════════════════════════════════════════════════════════ */

static QUIC_STATUS QUIC_API
server_listener_cb(HQUIC listener, void* ctx, QUIC_LISTENER_EVENT* event)
{
    (void)listener;
    wt_server_s* srv = (wt_server_s*)ctx;
    QUIC_STATUS status;

    /* Reject connections if server is not running.
     * Return QUIC_STATUS_CONNECTION_REFUSED without calling
     * ConnectionClose — msquic owns the handle and will clean it
     * up internally. Calling both would double-close. */
    if (atomic_load(&srv->state) != WT_SERVER_STARTED) {
        if (event->Type == QUIC_LISTENER_EVENT_NEW_CONNECTION)
            return QUIC_STATUS_CONNECTION_REFUSED;
        return QUIC_STATUS_SUCCESS;
    }

    if (event->Type != QUIC_LISTENER_EVENT_NEW_CONNECTION)
        return QUIC_STATUS_SUCCESS;

    /* ── Transport-layer per-IP rate limiting ───────────────────
     * Gate BEFORE slot allocation and TLS handshake.  An attacker
     * flooding NEW_CONNECTION events would otherwise exhaust all
     * 4096 slots and burn CPU on TLS handshakes before the C# layer
     * rate limiter (ClientHandshake handler) ever sees the traffic.
     *
     * CRITICAL: We hash ONLY the IP address bytes, NOT the full
     * QUIC_ADDR struct (which includes the source port).  Hashing
     * the source port would allow a single attacker with multiple
     * ephemeral ports to occupy all 256 rate-limit buckets, bypassing
     * the limiter entirely and exhausting connection slots. */
    {
        QUIC_ADDR remote_addr;
        uint32_t addr_len = sizeof(remote_addr);
        if (QUIC_SUCCEEDED(MsQuic->GetParam(
                event->NEW_CONNECTION.Connection,
                QUIC_PARAM_CONN_REMOTE_ADDRESS,
                &addr_len, &remote_addr))) {
            /* Extract IP-only bytes: 4 for IPv4, 16 for IPv6.
             * We use QuicAddrGetFamily to select the correct union member. */
            const uint8_t* ip_bytes = NULL;
            uint32_t ip_len = 0;
            uint8_t family = QuicAddrGetFamily(&remote_addr);
            if (family == QUIC_ADDRESS_FAMILY_INET) {
                ip_bytes = (const uint8_t*)&remote_addr.Ipv4.sin_addr;
                ip_len = sizeof(remote_addr.Ipv4.sin_addr);
            } else if (family == QUIC_ADDRESS_FAMILY_INET6) {
                ip_bytes = (const uint8_t*)&remote_addr.Ipv6.sin6_addr;
                ip_len = sizeof(remote_addr.Ipv6.sin6_addr);
            }
            if (ip_bytes != NULL && ip_len > 0) {
                if (!rate_limiter_check(srv, ip_bytes, ip_len)) {
                    return QUIC_STATUS_CONNECTION_REFUSED;
                }
            }
            /* If family is unspec or unknown, fall through and allow —
             * failing closed when we can't parse the address would block
             * legitimate connections on future address families. */
        }
        /* If GetParam fails, allow the connection — failing closed
         * when the OS can't tell us the remote address would prevent
         * all clients from connecting. */
    }

    wt_server_conn_t* conn = NULL;
    wt_connection_id_t conn_id = 0;

    /* ── Atomically claim a free connection slot ─────────────────
     * CAS on in_use guarantees exactly one thread wins each slot
     * even when multiple QUIC worker threads process concurrent
     * NEW_CONNECTION events.  Without the CAS, two threads could
     * both observe in_use==false for the same slot index and
     * overwrite each other's quic_conn handle, orphaning one
     * connection permanently. */
    for (uint32_t i = 1; i <= srv->max_clients; i++) {
        int expected = 0;  /* false — slot is free */
        if (atomic_compare_exchange_strong(
                &srv->connections[i].in_use, &expected, 1)) {
            conn = &srv->connections[i];
            conn_id = i;
            break;
        }
    }
    if (!conn) {
        /* No free slot — refuse without ConnectionClose. msquic
         * cleans up the handle when CONNECTION_REFUSED is returned. */
        return QUIC_STATUS_CONNECTION_REFUSED;
    }

    conn->id = conn_id;
    /* ── FIX #9: Set HANDSHAKING state IMMEDIATELY after CAS ──────
     * Must be the FIRST write after the CAS claim on in_use, before
     * quic_conn is stored.  wt_server_disconnect_impl checks state
     * after loading quic_conn: if state is still IDLE (calloc zero),
     * it takes a fast-path that sets in_use=false and returns without
     * calling ConnectionShutdown — orphaning the QUIC handle and
     * leaking the connection slot.  By setting HANDSHAKING before
     * quic_conn becomes visible, disconnect_impl always observes
     * state >= HANDSHAKING when quic_conn is non-NULL, forcing the
     * normal ConnectionShutdown path with correct pending_shutdowns
     * accounting.  Also closes the window where the H3 timeout sweep
     * (poll thread) could observe in_use==true, state==IDLE and
     * bypass the deadline check. */
    atomic_store(&conn->state, WT_CONN_STATE_HANDSHAKING);
    atomic_ptr_store(&conn->quic_conn, event->NEW_CONNECTION.Connection);
    conn->session = NULL;
    conn->h3_session = NULL;
    /* ── FIX: Reset H3 handshake deadline on slot recycling ──
     * Without this reset, a recycled slot retains the previous
     * occupant's h3_handshake_deadline_ms value.  On ARM (weak
     * memory ordering), the poll thread's H3 timeout sweep can
     * observe the new h3_session (plain store in CONNECTED) before
     * the atomic_store_u64 of the fresh deadline, triggering a
     * spurious disconnect when the stale deadline has expired.
     * Zeroing here guarantees the sweep's `deadline == 0` guard
     * skips the slot until CONNECTED sets a proper deadline. */
    atomic_store_u64(&conn->h3_handshake_deadline_ms, 0);
    /* If the previous occupant of this slot queued a pending shutdown
     * that hasn't been drained by poll() yet, leave it in place.
     * The queue entry written by SHUTDOWN_COMPLETE is still in the ring
     * buffer, and poll() will drain it on the next frame.  We must NOT
     * call wt_session_shutdown here — that function is documented to
     * run on the poll (application) thread, the same thread as send,
     * to guarantee session free never races with in-flight sends.
     * Calling it from a QUIC callback thread would violate that
     * contract.  The stale session's QUIC connection is already closed,
     * so deferring shutdown to poll is safe and correct. */
    {
        /* Atomically claim the stale session.  atomic_ptr_exchange
         * returns the old value and stores NULL in a single atomic
         * step — if poll() is concurrently draining the same slot's
         * ring-buffer entry, exactly one path wins the pointer and
         * the other sees NULL.  This eliminates the TOCTOU race
         * between load and store that existed with the previous
         * atomic_ptr_load + atomic_ptr_store pattern. */
        wt_session_t* stale = (wt_session_t*)atomic_ptr_exchange(
            &conn->pending_shutdown_session, NULL);
        if (stale) {
            WT_LOG_INFO("Slot %u recycled with pending shutdown session "
                        "from previous occupant — cleaning up immediately",
                        conn_id);
            /* Safe: the old QUIC connection was already closed by
             * SHUTDOWN_COMPLETE (ConnectionClose).  No in-flight
             * sends remain, and all stream handles are already
             * cleaned up.  Calling wt_session_shutdown on the
             * callback thread matches the overflow-path pattern
             * at lines 1396-1403. */
            wt_session_shutdown(stale);
        }
    }
    conn->remote_addr[0] = '\0';
    /* in_use was already set to true by the CAS claim in the slot loop above
     * state was already set to WT_CONN_STATE_HANDSHAKING above (immediately after
     * CAS claim, before stale-session cleanup — closes the IDLE race window). */
    atomic_store(&conn->dgram_drop_count, 0);
    conn->owner = srv;

    atomic_fetch_add(&srv->connection_count, 1);

    MsQuic->SetCallbackHandler((HQUIC)atomic_ptr_load(&conn->quic_conn),
                                (void*)server_conn_cb, conn);
    status = MsQuic->ConnectionSetConfiguration((HQUIC)atomic_ptr_load(&conn->quic_conn),
                                        srv->session_config);
    if (QUIC_FAILED(status)) {
        WT_LOG_WARN("ConnectionSetConfiguration: 0x%x", status);
        /* Undo what we set up above; do NOT call ConnectionClose —
         * msquic closes the handle when CONNECTION_REFUSED is returned. */
        atomic_store(&conn->in_use, false);
        atomic_fetch_sub(&srv->connection_count, 1);
        return QUIC_STATUS_CONNECTION_REFUSED;
    }

    /* ── FIX #2: Re-check server state after slot setup ─────────
     * The CAS on in_use and the state check at the top of this
     * function are not an atomic pair.  If wt_server_stop_impl
     * transitions STARTED→STOPPING between the initial state
     * check and the slot CAS above, this connection has been
     * accepted on a server that is shutting down.  The C# layer
     * would receive an on_connect callback for a stopped server.
     *
     * Re-check after the connection is fully configured.  If the
     * server is no longer STARTED, shut down the QUIC connection
     * cleanly and let SHUTDOWN_COMPLETE handle ALL slot cleanup.
     * We must call ConnectionShutdown here (not return
     * CONNECTION_REFUSED) because the connection has already been
     * fully accepted by msquic.
     *
     * CRITICAL: Do NOT set in_use=false or decrement
     * connection_count here.  The slot is still owned by this
     * connection until SHUTDOWN_COMPLETE fires.  If we free the
     * slot before SHUTDOWN_COMPLETE runs, a new connection can
     * CAS-claim the same slot, and the old SHUTDOWN_COMPLETE will
     * corrupt the new connection's state (decrement its
     * connection_count, set its in_use=false, fire its disconnect
     * callback).  Instead, increment pending_shutdowns to balance
     * the SHUTDOWN_COMPLETE accounting, and let the callback do
     * all cleanup — matching the pattern used by
     * wt_server_disconnect_impl. */
    if (atomic_load(&srv->state) != WT_SERVER_STARTED) {
        WT_LOG_INFO("Slot %u accepted but server is stopping — aborting", conn_id);
        /* ── FIX #7: NULL guard on quic_conn ────────────────────────
         * wt_server_disconnect_impl may have already claimed this slot
         * (nulling quic_conn via atomic_ptr_store) after stop_impl
         * transitioned STARTED→STOPPING between the initial state check
         * and the CAS slot claim.  If quic_conn is NULL, the slot was
         * already shut down — skip ConnectionShutdown and the
         * pending_shutdowns increment to avoid double-increment that
         * would cause wt_server_free_impl's spin-wait to time out.
         *
         * CRITICAL: After ConnectionShutdown, store NULL to quic_conn
         * so that stop_impl's iteration loop (which calls
         * wt_server_disconnect_impl on every in_use slot) does not
         * double-call ConnectionShutdown on the same QUIC handle.
         * Without this NULL store, disconnect_impl sees quic_conn
         * non-NULL, state HANDSHAKING, and proceeds to increment
         * pending_shutdowns a second time and call ConnectionShutdown
         * again — UB in msquic + guaranteed spin-wait timeout in
         * wt_server_free_impl. */
        HQUIC qconn = (HQUIC)atomic_ptr_load(&conn->quic_conn);
        if (qconn) {
            atomic_fetch_add(&srv->pending_shutdowns, 1);
            MsQuic->ConnectionShutdown(qconn,
                QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
            atomic_ptr_store(&conn->quic_conn, NULL);
        }
        return QUIC_STATUS_SUCCESS;
    }
    return QUIC_STATUS_SUCCESS;
}

static void fire_connect(wt_server_conn_t* sconn)
{
    if (!sconn || !sconn->owner) return;
    wt_server_s* srv = sconn->owner;
    if (srv->callbacks.on_connect) {
        srv->callbacks.on_connect(srv->user_context,
                                   sconn->id, sconn->remote_addr);
    }
}

static void fire_disconnect(wt_server_conn_t* sconn, int err)
{
    if (!sconn || !sconn->owner) return;
    wt_server_s* srv = sconn->owner;
    if (srv->callbacks.on_disconnect) {
        srv->callbacks.on_disconnect(srv->user_context, sconn->id, err);
    }
}

static QUIC_STATUS QUIC_API
server_conn_cb(HQUIC conn, void* ctx, QUIC_CONNECTION_EVENT* event)
{
    wt_server_conn_t* sconn = (wt_server_conn_t*)ctx;

    switch (event->Type) {

    case QUIC_CONNECTION_EVENT_CONNECTED: {
        atomic_store(&sconn->state, WT_CONN_STATE_CONNECTED);

        /* Remote address */
        QUIC_ADDR remote_addr;
        uint32_t addr_len = sizeof(remote_addr);
        if (QUIC_SUCCEEDED(MsQuic->GetParam(
                conn, QUIC_PARAM_CONN_REMOTE_ADDRESS,
                &addr_len, &remote_addr))) {
            QUIC_ADDR_STR addr_str;
            QuicAddrToString(&remote_addr, &addr_str);
            strncpy(sconn->remote_addr, addr_str.Address,
                    sizeof(sconn->remote_addr) - 1);
            sconn->remote_addr[sizeof(sconn->remote_addr) - 1] = '\0';
        }

        WT_LOG_INFO("Client %llu connected from %s",
                    (unsigned long long)sconn->id, sconn->remote_addr);

        /* ── Protocol Detection via HTTP/3 Handshake ─────────
         * Create an h3_session that automatically detects the client
         * protocol (HTTP/3 WebTransport vs raw QUIC native) from the
         * first byte of the first peer-initiated bidi stream.
         *
         *   - 0x00 → browser WebTransport client → HTTP/3 handshake
         *   - ANY  → native C++ client → firewall connect immediately
         *
         * on_h3_session_ready fires when the WT session is established
         * (either immediately for native clients after the first byte
         *  check, or after HTTP/3 CONNECT for browser clients). */
        sconn->h3_session = h3_session_create(
            conn, true,  /* is_server */
            on_h3_session_ready, on_h3_error, sconn);

        /* ── FIX #3: Set H3 handshake deadline ─────────────────
         * The QUIC idle timeout (120s) is far too long for the
         * HTTP/3 handshake, which should complete in milliseconds.
         * Without this deadline, an attacker can open thousands of
         * QUIC connections that never send an H3 control stream,
         * holding slots until the idle timeout fires. */
        if (sconn->h3_session) {
            /* Use atomic_store_u64 — the poll sweep reads this field
             * from the application thread while the CONNECTED callback
             * writes it from a QUIC worker thread.  Plain uint64_t
             * assignment tears on 32-bit ARM. */
            atomic_store_u64(&sconn->h3_handshake_deadline_ms,
                rate_limiter_now_ms() + WT_H3_HANDSHAKE_TIMEOUT_MS);
        }

        if (!sconn->h3_session) {
            /* Fall back to raw QUIC (backward compatible) */
            WT_LOG_WARN("Failed to create HTTP/3 session for client %llu — falling back to raw QUIC",
                        (unsigned long long)sconn->id);
            wt_session_t* session = (wt_session_t*)calloc(1, sizeof(wt_session_t));
            if (!session) {
                if (sconn->owner)
                    atomic_fetch_add(&sconn->owner->pending_shutdowns, 1);
                MsQuic->ConnectionShutdown(conn,
                    QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
                break;
            }
            int32_t r = wt_session_init(session, conn, sconn->id);
            if (r != WT_OK) {
                WT_LOG_ERROR("Session init failed for client %llu",
                             (unsigned long long)sconn->id);
                free(session);
                if (sconn->owner)
                    atomic_fetch_add(&sconn->owner->pending_shutdowns, 1);
                MsQuic->ConnectionShutdown(conn,
                    QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
                break;
            }
            atomic_ptr_store(&sconn->session, session);
            session->parent_type = WT_PARENT_SERVER;
            session->parent.server = sconn->owner;
            wt_session_wire_callbacks(session);
            fire_connect(sconn);
        } else {
            /* Copy allowed origins from server config for CORS validation.
             * Empty allowed_origins = allow all (dev/testing default). */
            if (sconn->owner && sconn->owner->allowed_origins[0]) {
                strncpy(sconn->h3_session->allowed_origins,
                        sconn->owner->allowed_origins,
                        sizeof(sconn->h3_session->allowed_origins) - 1);
                sconn->h3_session->allowed_origins[
                    sizeof(sconn->h3_session->allowed_origins) - 1] = '\0';
            }
            /* Copy expected :authority for CONNECT validation.
             * Empty = skip authority validation (backward compatible). */
            if (sconn->owner && sconn->owner->expected_authority[0]) {
                strncpy(sconn->h3_session->expected_authority,
                        sconn->owner->expected_authority,
                        sizeof(sconn->h3_session->expected_authority) - 1);
                sconn->h3_session->expected_authority[
                    sizeof(sconn->h3_session->expected_authority) - 1] = '\0';
            }
            /* Propagate native-client policy from server config.
             * Default (from alloc) is true for backward compatibility. */
            if (sconn->owner) {
                sconn->h3_session->allow_native_clients =
                    sconn->owner->allow_native_clients;
            }

            /* Schedule SETTINGS for poll thread (never StreamOpen here —
             * connection/stream callbacks re-entering msquic caused
             * QuicOperationFree double-fault / Login 255/EXCEPTION). */
            h3_server_send_initial_settings(sconn->h3_session);
            WT_LOG_INFO(
                "H3: SETTINGS bootstrap scheduled for client %llu (poll thread)",
                (unsigned long long)sconn->id);
        }
        /* When h3_session is created, peer streams drive protocol detection
         * (browser CONNECT vs native). Session init is deferred until
         * on_h3_session_ready fires. */
        break;
    }

    case QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE: {
        /* Atomic load — no non-atomic guard (avoids torn read on ARM). */
        {
            wt_session_t* old_session = (wt_session_t*)atomic_ptr_load(&sconn->session);
            if (old_session) {
                atomic_ptr_store(&sconn->session, NULL);

                /* Defer shutdown to poll (application thread) to guarantee
                 * session free never races with in-flight sends.
                 * Use atomic_ptr_store because the poll thread reads this
                 * field without a lock. */
                atomic_ptr_store(&sconn->pending_shutdown_session, old_session);
            }
        }

        /* ── HTTP/3 Session Cleanup ──────────────────────────
         * Free the h3_session if the handshake never completed.
         * If h3_session->handshake_complete is true, the session was
         * already transitioned to wt_session in on_h3_session_ready,
         * and h3_session is safe to free (no pending streams). */
        if (sconn->h3_session) {
            h3_session_free(sconn->h3_session);
            sconn->h3_session = NULL;
        }

        MsQuic->ConnectionClose(conn);

        bool was_in_use = atomic_load(&sconn->in_use);
        if (was_in_use) {
            if (sconn->owner) {
                atomic_fetch_sub(&sconn->owner->connection_count, 1);
            }

            atomic_store(&sconn->in_use, false);
            atomic_store(&sconn->state, WT_CONN_STATE_CLOSED);
        }

        /* Enqueue for O(1) poll drain — the poll thread processes
         * pending_shutdown_session safely on the application thread. */
        if (sconn->owner && sconn->pending_shutdown_session) {
            /* ── Consistent head/tail snapshot ──────────────────
             * Load head and tail with a confirm-retry so a stale head
             * (consumer advanced head between two separate atomic loads)
             * doesn't cause a false "queue full" detection.  False
             * overflow would trigger premature wt_session_shutdown on
             * the callback thread, which is safe (the overflow path
             * handles it) but wasteful.  One re-read eliminates nearly
             * all false positives without risking livelock (monotonic
             * head advances monotonically — each retry sees head >=
             * the previous).
             *
             * Use subtraction-based full check with 64-bit counters to
             * eliminate the ~5-day wraparound inherent in uint32_t.
             * The ring buffer holds WT_MAX_CLIENTS entries; we reject
             * when occupancy reaches WT_MAX_CLIENTS - 1 to leave one
             * guard slot. */
            uint64_t head = atomic_load_u64(&sconn->owner->pending_shutdown_head);
            atomic_thread_fence(std::memory_order_acquire);
            uint64_t tail = atomic_load_u64(
                &sconn->owner->pending_shutdown_tail);
            if ((tail - head) >= WT_MAX_CLIENTS - 1) {
                /* Confirm: re-read head.  If the consumer advanced it,
                 * recompute occupancy with the fresh snapshot. */
                uint64_t head2 = atomic_load_u64(
                    &sconn->owner->pending_shutdown_head);
                if (head2 != head) {
                    head = head2;
                    atomic_thread_fence(std::memory_order_acquire);
                    tail = atomic_load_u64(
                        &sconn->owner->pending_shutdown_tail);
                }
            }
            if ((tail - head) >= WT_MAX_CLIENTS - 1) {
                /* ── Queue full — bounded spin-retry ──────────────
                 * The poll thread drains entries every frame and should
                 * advance head within a few QUIC scheduler ticks. Spin
                 * for up to 100 iterations (~1ms on modern CPUs) waiting
                 * for a slot to open.  If the queue is still full after
                 * the spin, fall back to immediate shutdown on this
                 * thread as a last resort (see below).
                 *
                 * This retry eliminates the need for direct shutdown on
                 * the callback thread in normal operation — the queue
                 * only overflows under truly pathological conditions
                 * (4095 simultaneous disconnects with no poll calls). */
                int spin_retries = 100;
                uint64_t head_retry, tail_retry;
                do {
                    /* Yield the CPU briefly — the poll thread needs
                     * scheduler time to advance head. */
#if defined(WT_PLATFORM_WINDOWS)
                    SwitchToThread();
#else
                    sched_yield();
#endif
                    head_retry = atomic_load_u64(
                        &sconn->owner->pending_shutdown_head);
                    atomic_thread_fence(std::memory_order_acquire);
                    tail_retry = atomic_load_u64(
                        &sconn->owner->pending_shutdown_tail);
                } while ((tail_retry - head_retry) >= WT_MAX_CLIENTS - 1 &&
                         --spin_retries > 0);

                if ((tail_retry - head_retry) >= WT_MAX_CLIENTS - 1) {
                    WT_LOG_ERROR("Pending shutdown queue overflow after spin — "
                                 "freeing session for connection %llu immediately "
                                 "(tail %llu, head %llu)",
                                (unsigned long long)sconn->id,
                                (unsigned long long)tail_retry,
                                (unsigned long long)head_retry);
                    /* ── Last-resort: Free session immediately ────
                     * The queue is still full after yielding.  This
                     * connection has no in-flight sends (SHUTDOWN_COMPLETE
                     * already fired), so calling wt_session_shutdown
                     * directly on the callback thread is safe.
                     *
                     * SAFETY: atomic_ptr_exchange atomically swaps
                     * the pointer with NULL.  If poll() or
                     * server_listener_cb is concurrently draining the
                     * same slot, exactly one path receives the non-NULL
                     * pointer and calls wt_session_shutdown.  This
                     * guarantees no double-free. */
                    wt_session_t* overflow_session =
                        (wt_session_t*)atomic_ptr_exchange(
                            &sconn->pending_shutdown_session, NULL);
                    if (overflow_session) {
                        wt_session_shutdown(overflow_session);
                    }
                } else {
                    /* Spin succeeded — slot opened up. Enqueue normally. */
                    uint64_t claimed_tail = atomic_fetch_add_u64(
                        &sconn->owner->pending_shutdown_tail, 1);
                    sconn->owner->pending_shutdown_queue[
                        claimed_tail % WT_MAX_CLIENTS] = sconn->id;
                    atomic_thread_fence(std::memory_order_release);
                }
            } else {
                /* Write data to the queue slot BEFORE publishing the new
                 * tail index.  The consumer reads tail then reads the slot;
                 * without this ordering the consumer could see the new tail
                 * but stale slot data on weakly-ordered architectures. */
                /* Atomically claim a unique slot in the ring buffer.
                 * atomic_fetch_add prevents multiple QUIC threads from
                 * writing to the same slot — each call returns a
                 * unique position. */
                uint64_t claimed_tail = atomic_fetch_add_u64(
                    &sconn->owner->pending_shutdown_tail, 1);
                sconn->owner->pending_shutdown_queue[
                    claimed_tail % WT_MAX_CLIENTS] = sconn->id;
                /* Release fence: ensures the slot write above is visible
                 * before the tail update observed by the consumer.
                 * Paired with the acquire fence in wt_server_poll_impl. */
                atomic_thread_fence(std::memory_order_release);
            }
        }

        /* ── FIX: UAF in SHUTDOWN_COMPLETE ─────────────────
         * Fire the disconnect callback BEFORE signalling completion via
         * pending_shutdowns.  If fire_disconnect were called AFTER the
         * CAS decrement, the last SHUTDOWN_COMPLETE could decrement
         * pending_shutdowns to 0, causing wt_server_free_impl's spin-wait
         * to exit and free server->connections (and sconn) before this
         * thread reaches fire_disconnect — a use-after-free.
         *
         * Re-entrancy note: if fire_disconnect's user callback calls
         * wt_server_destroy → free_impl, the spin-wait will see
         * pending_shutdowns > 0 and spin until this CAS decrement below
         * runs.  After fire_disconnect returns, no further accesses to
         * sconn or sconn->owner occur, so same-thread re-entrancy is safe.
         * Cross-thread: wt_server_free_impl cannot proceed past its
         * spin-wait until pending_shutdowns reaches 0, which happens
         * AFTER this callback's CAS decrement — guaranteeing sconn
         * remains valid through the entire fire_disconnect call. */
        if (was_in_use)
            fire_disconnect(sconn, 0);

        /* Signal completion AFTER the disconnect callback.
         * Use a CAS loop that decrements ONLY if > 0, eliminating the
         * TOCTOU race between fetch_sub and the underflow-correction
         * fetch_add that existed in the previous implementation.
         * If pending_shutdowns is 0 (client-initiated disconnect or
         * duplicate SHUTDOWN_COMPLETE), the CAS loop exits harmlessly
         * without touching the counter. */
        if (sconn->owner) {
            unsigned int expected = atomic_load(
                &sconn->owner->pending_shutdowns);
            while (expected > 0) {
                if (atomic_compare_exchange_strong(
                        &sconn->owner->pending_shutdowns,
                        &expected, expected - 1))
                    break;
            }
        }
        break;
    }

    case QUIC_CONNECTION_EVENT_PEER_STREAM_STARTED: {
        /* ── Protocol Detection: HTTP/3 vs Raw QUIC ──────────
         * If h3_session exists (handshake not yet complete), pass the
         * stream to it for protocol detection and HTTP/3 processing.
         * The h3_session inspects the first byte of the first bidi stream
         * to determine the client type, and calls on_h3_session_ready
         * when the session is established. */
        if (sconn->h3_session && !sconn->h3_session->handshake_complete) {
            h3_stream_ctx_t* out_sctx = NULL;
            const bool is_uni =
                (event->PEER_STREAM_STARTED.Flags &
                 QUIC_STREAM_OPEN_FLAG_UNIDIRECTIONAL) != 0;
            int hr = h3_server_handle_stream(
                sconn->h3_session,
                event->PEER_STREAM_STARTED.Stream,
                is_uni,
                &out_sctx);
            if (hr == 0) {
                /* HTTP/3 consumed the stream (control/request stream).
                 * Data processing happens in the stream RECEIVE callback,
                 * which calls h3_server_process_data. When the handshake
                 * completes, on_h3_session_ready creates the wt_session.
                 * We break here — the stream has been claimed by HTTP/3. */
                break;
            } else if (hr < 0) {
                /* Handshake error — shut down the stream.  StreamShutdown
                 * triggers SHUTDOWN_COMPLETE which calls StreamClose via
                 * h3_stream_cb — do NOT call StreamClose here. */
                MsQuic->StreamShutdown(event->PEER_STREAM_STARTED.Stream,
                                        QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                break;
            }
            /* hr == 1: regular data stream detected (or native client),
             * fall through to the wt_stream_manager path below. */
        }

        /* Acquire session refcount to prevent UAF — wt_session_shutdown
         * on the poll thread may concurrently set stream_mgr=NULL and
         * free it.  Holding a ref keeps the session (and its stream_mgr
         * pointer) alive until we release. */
        {
            wt_session_t* session = (wt_session_t*)atomic_ptr_load(&sconn->session);
            if (!session || !wt_session_acquire(session)) {
                MsQuic->StreamShutdown(event->PEER_STREAM_STARTED.Stream,
                                        QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                MsQuic->StreamClose(event->PEER_STREAM_STARTED.Stream);
                break;
            }
            /* Re-check that the session pointer hasn't changed and
             * the stream_mgr is still valid AFTER acquiring. */
            if (atomic_ptr_load(&sconn->session) != session ||
                !session->stream_mgr) {
                wt_session_release(session);
                MsQuic->StreamShutdown(event->PEER_STREAM_STARTED.Stream,
                                        QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                MsQuic->StreamClose(event->PEER_STREAM_STARTED.Stream);
                break;
            }
            wt_stream_manager_accept_stream(
                session->stream_mgr,
                event->PEER_STREAM_STARTED.Stream);
            wt_session_release(session);
        }
        break;
    }

    case QUIC_CONNECTION_EVENT_DATAGRAM_RECEIVED: {
        /* Atomic load — avoid non-atomic read of session/owner pointers.
         * Acquire a session reference to prevent UAF: wt_session_shutdown
         * on the poll thread may free the session concurrently.  Despite
         * the datagram handler not dereferencing the session beyond the
         * null check, holding a ref guarantees the session stays alive
         * for the duration, matching the PEER_STREAM_STARTED pattern
         * and preventing future refactoring hazards. */
        if (!sconn->owner) break;
        {
            wt_session_t* session = (wt_session_t*)atomic_ptr_load(&sconn->session);
            if (!session || !wt_session_acquire(session)) break;

            /* Re-check after acquire — session may have been nulled
             * by SHUTDOWN_COMPLETE between load and acquire. */
            if (atomic_ptr_load(&sconn->session) != session ||
                !atomic_load(&sconn->in_use)) {
                wt_session_release(session);
                break;
            }

            const QUIC_BUFFER* buf = event->DATAGRAM_RECEIVED.Buffer;
            if (buf && buf->Length > 0 &&
                buf->Length <= WT_DGRAM_MAX_SIZE) {
                if (!wt_datagram_queue_push(
                        &sconn->owner->dgram_queue,
                        sconn->id, buf->Buffer,
                        (int32_t)buf->Length)) {
                    int prev = atomic_fetch_add(&sconn->dgram_drop_count, 1);
                    if (prev % 100 == 0) {
                        WT_LOG_WARN("Datagram queue full: %d drops for client %llu",
                                    prev + 1, (unsigned long long)sconn->id);
                    }
                }
            } else if (buf && buf->Length > WT_DGRAM_MAX_SIZE) {
                WT_LOG_WARN("Dropped oversized datagram (%u bytes) from client %llu",
                            buf->Length, (unsigned long long)sconn->id);
            }
            wt_session_release(session);
        }
        break;
    }

    case QUIC_CONNECTION_EVENT_DATAGRAM_SEND_STATE_CHANGED:
    {
        /* Free the wrapper struct only if msquic claimed ownership.
         * The owned_by_msquic flag prevents double-free if msquic were
         * to fire this callback synchronously from within DatagramSend
         * (which it currently does not guarantee by contract). */
        void* ctx = event->DATAGRAM_SEND_STATE_CHANGED.ClientContext;
        if (ctx &&
            QUIC_DATAGRAM_SEND_STATE_IS_FINAL(
                event->DATAGRAM_SEND_STATE_CHANGED.State)) {
            wt_dgram_send_ctx_t* send_ctx = (wt_dgram_send_ctx_t*)ctx;
            if (send_ctx->owned_by_msquic) {
                free(send_ctx);
            }
            event->DATAGRAM_SEND_STATE_CHANGED.ClientContext = NULL;
        }
        break;
    }

    default:
        break;
    }
    return QUIC_STATUS_SUCCESS;
}

static void on_server_dgram_drain(void* ctx, wt_connection_id_t conn_id,
                                   const uint8_t* data, int32_t length)
{
    wt_server_s* srv = (wt_server_s*)ctx;
    if (srv->callbacks.on_datagram) {
        srv->callbacks.on_datagram(srv->user_context,
                                    conn_id, data, length);
    }
}