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

/* ── Forward ────────────────────────────────────────────────── */

static QUIC_STATUS QUIC_API server_conn_cb(HQUIC conn, void* ctx,
                                   QUIC_CONNECTION_EVENT* event);
static QUIC_STATUS QUIC_API server_listener_cb(HQUIC listener, void* ctx,
                                       QUIC_LISTENER_EVENT* event);
static void on_server_dgram_drain(void* ctx, wt_connection_id_t conn_id,
                                   const uint8_t* data, int32_t length);

static void fire_connect(wt_server_conn_t* sconn);
static void fire_disconnect(wt_server_conn_t* sconn, int err);

/* ── HTTP/3 Handshake Callbacks ─────────────────────────────── */

static void on_h3_session_ready(void* ctx, HQUIC quic_conn,
                                const char* path, const char* authority)
{
    wt_server_conn_t* sconn = (wt_server_conn_t*)ctx;

    wt_session_t* session = (wt_session_t*)calloc(1, sizeof(wt_session_t));
    if (!session) {
        MsQuic->ConnectionShutdown(quic_conn,
            QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
        return;
    }

    int32_t r = wt_session_init(session, quic_conn, sconn->id);
    if (r != WT_OK) {
        WT_LOG_ERROR("Session init failed for client %llu (HTTP/3 WT)",
                     (unsigned long long)sconn->id);
        free(session);
        MsQuic->ConnectionShutdown(quic_conn,
            QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
        return;
    }

    atomic_ptr_store(&sconn->session, session);
    session->parent_type = WT_PARENT_SERVER;
    session->parent.server = sconn->owner;
    wt_session_wire_callbacks(session);

    /* Browser CONNECT sessions need WEBTRANSPORT_STREAM framing on data
     * streams so Chrome associates them with the WT session. Native raw
     * clients leave use_wt_stream_header=false (default). */
    if (sconn->h3_session && sconn->h3_session->is_webtransport &&
        session->stream_mgr) {
        session->stream_mgr->use_wt_stream_header = true;
        session->stream_mgr->wt_session_id = sconn->h3_session->connect_stream_id;
        WT_LOG_INFO(
            "Client %llu WT stream framing ON session_id=%llu "
            "stamp=STREAM_DELIVER_ON_RECV_V1",
            (unsigned long long)sconn->id,
            (unsigned long long)session->stream_mgr->wt_session_id);
    }

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
    /* Connection will be shut down by the error path */
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
     * connections.  Empty string (or NULL) = allow all (dev/testing). */
    if (allowed_origins) {
        strncpy(srv->allowed_origins, allowed_origins,
                sizeof(srv->allowed_origins) - 1);
        srv->allowed_origins[sizeof(srv->allowed_origins) - 1] = '\0';
    }
    /* else: calloc already zeroed the buffer — empty = allow all */

    atomic_init(&srv->state, WT_SERVER_STOPPED);
    atomic_init(&srv->connection_count, 0);
    atomic_init(&srv->pending_shutdowns, 0);
    atomic_init(&srv->pending_shutdown_head, 0);
    atomic_init(&srv->pending_shutdown_tail, 0);

    /* Allocate max_clients + 1 to account for index 0 being reserved
     * (WT_CONNECTION_ID_NONE). Valid connection IDs are 1..max_clients. */
    srv->connections = (wt_server_conn_t*)calloc(
        srv->max_clients + 1, sizeof(wt_server_conn_t));
    if (!srv->connections) { free(srv); return NULL; }

    wt_datagram_queue_init(&srv->dgram_queue);
    return srv;
}

void wt_server_free_impl(wt_server_s* server)
{
    if (!server) return;
    wt_server_stop_impl(server);

    /* Null out the owner pointer on every connection slot BEFORE the
     * spin-wait.  This guarantees that any late SHUTDOWN_COMPLETE
     * callbacks (which may fire after the spin-wait timeout) will see
     * sconn->owner==NULL and skip the pending_shutdowns decrement,
     * preventing a UAF on the server struct.
     *
     * The sconn->owner field is only written during
     * server_listener_cb (assign) and here (NULL).  This store
     * is safe — the QUIC callback thread reads owner under
     * atomic_load(&sconn->in_use) which has already been set to
     * false by SHUTDOWN_COMPLETE (or will be before reading owner). */
    if (server->connections) {
        for (uint32_t i = 1; i <= server->max_clients; i++) {
            server->connections[i].owner = NULL;
        }
    }

    /* Wait for pending SHUTDOWN_COMPLETE callbacks (bounded).
     * 600 iterations * 10ms = 6 second max wait. */
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
     * Bind to the configured address (cfg Address=127.0.0.1 for DNAT
     * topologies). Previously only the port was applied with family UNSPEC
     * and bind_address was log-only — production World/Scene failures need
     * the real loopback bind so public-IP advertise is not confused with
     * the listen socket. */
    QUIC_ADDR addr = {0};
    const char* ba = server->bind_address;
    BOOLEAN addr_ok = FALSE;
    if (!ba || !ba[0] || strcmp(ba, "0.0.0.0") == 0 || strcmp(ba, "*") == 0) {
        QuicAddrSetFamily(&addr, QUIC_ADDRESS_FAMILY_UNSPEC);
        QuicAddrSetPort(&addr, server->port);
        addr_ok = TRUE;
    } else {
        /* QuicAddrFromString parses IPv4/IPv6 and applies port (host order). */
        addr_ok = QuicAddrFromString(ba, server->port, &addr);
        if (!addr_ok) {
            /* localhost / common aliases */
            if (strcmp(ba, "localhost") == 0 || strcmp(ba, "127.0.0.1") == 0) {
                addr_ok = QuicAddrFromString("127.0.0.1", server->port, &addr);
            } else if (strcmp(ba, "::1") == 0) {
                addr_ok = QuicAddrFromString("::1", server->port, &addr);
            }
        }
    }
    if (!addr_ok) {
        WT_LOG_ERROR("Invalid bind address '%s' port %u — cannot build QUIC_ADDR",
                     ba ? ba : "(null)", (unsigned)server->port);
        MsQuic->ConfigurationClose(server->session_config);
        MsQuic->RegistrationClose(server->registration);
        server->session_config = NULL;
        server->registration = NULL;
        return WT_ERR_UNKNOWN;
    }

    WT_LOG_INFO("ListenerStart preparing bind=%s port=%u cert=%s key=%s alpn=%s",
                ba && ba[0] ? ba : "0.0.0.0",
                (unsigned)server->port,
                server->cert_path[0] ? server->cert_path : "(self-signed)",
                server->key_path[0] ? server->key_path : "(none)",
                server->alpn);

    status = MsQuic->ListenerOpen(server->registration,
                                   server_listener_cb, server,
                                   &server->listener);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("ListenerOpen: 0x%x bind=%s port=%u", status,
                     ba ? ba : "(null)", (unsigned)server->port);
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
        WT_LOG_ERROR("ListenerStart: 0x%x bind=%s port=%u", status,
                     ba ? ba : "(null)", (unsigned)server->port);
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
                ba && ba[0] ? ba : "0.0.0.0", server->port,
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
    uint32_t head = atomic_load(&server->pending_shutdown_head);
    uint32_t tail = atomic_load(&server->pending_shutdown_tail);
    /* NOTE: tail is loaded once before the loop. Entries added after this
     * point won't be seen until the next poll call. This single-cycle delay
     * is acceptable for the intended use (polled each frame).
     *
     * (void)timeout_us — see above; this is a non-blocking poll.
     *
     * SINGLE-POLL-CYCLE DELAY (by design):
     * The pending_shutdown_head load above may return a stale value because
     * the QUIC callback thread wrote a new tail concurrently.  The acquire
     * fence below ensures that when we DO see an updated tail, the array
     * slot contents are also visible.  If head is stale (behind the real
     * producer position), we simply process one fewer entry this cycle; the
     * entry will be drained on the next frame's poll call.  This is an
     * acceptable single-frame delay for session shutdown — Unity already
     * tolerates frame-level latency for network events.
     *
     * Acquire fence: paired with release fence in SHUTDOWN_COMPLETE.
     * Ensures that the producer's array writes are visible before we
     * read them on weakly-ordered architectures (ARM).  Without this,
     * the consumer could see an updated tail but stale array data. */
    atomic_thread_fence(std::memory_order_acquire);
    while (head != tail) {
        wt_connection_id_t cid = server->pending_shutdown_queue[head % WT_MAX_CLIENTS];
        if (cid > 0 && cid <= server->max_clients) {
            wt_server_conn_t* c = &server->connections[cid];
            /* Use atomic load to safely read pending_shutdown_session.
             * A raw pointer read could be cached/reordered by the compiler
             * on weakly-ordered architectures, causing the poll path to skip
             * draining a pending session (leak). */
            wt_session_t* s = (wt_session_t*)atomic_ptr_load(
                &c->pending_shutdown_session);
            if (s) {
                atomic_ptr_store(&c->pending_shutdown_session, NULL);
                wt_session_shutdown(s);
            }
        }
        head++;
        /* Release fence: paired with the producer's acquire load of
         * pending_shutdown_head in SHUTDOWN_COMPLETE.  Ensures that
         * all slot processing (reads/writes to the session struct)
         * completes before the head update becomes visible, so the
         * producer sees a fully-consumed slot before reusing it. */
        std::atomic_thread_fence(std::memory_order_release);
        atomic_fetch_add(&server->pending_shutdown_head, 1);
    }

    /* Deferred H3 SETTINGS bootstrap (app thread — safe for StreamOpen/Send). */
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

    /* Browser WebTransport sessions (use_wt_stream_header): route "datagram"
     * traffic over the reliable same-stream path. DatagramSend near login
     * completion has caused QuicDatagramFrameEncodeEx SIGSEGV (rc=139) and
     * peer H3_FRAME_ERROR (0x106). Auth / char-select only need reliable. */
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
            int32_t r;
            if (session->stream_mgr && session->stream_mgr->use_wt_stream_header)
                r = wt_session_send_stream(session, data, length);
            else
                r = wt_session_send_datagram(session, data, length);
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

        int32_t result;
        if (session->stream_mgr && session->stream_mgr->use_wt_stream_header) {
            WT_LOG_INFO(
                "Browser client: datagram→stream reroute conn=%llu len=%d "
                "stamp=NO_DGRAM_BROWSER_V1",
                (unsigned long long)conn_id, length);
            result = wt_session_send_stream(session, data, length);
        } else {
            result = wt_session_send_datagram(session, data, length);
        }
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
     * the slot directly. */
    wt_connection_state_t state = (wt_connection_state_t)atomic_load(&conn->state);
    if (state == WT_CONN_STATE_IDLE) {
        atomic_store(&conn->in_use, false);
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

/* Returns a pointer to the connection's remote address string.
 *
 * LIFETIME WARNING:
 * The returned pointer points directly into `conn->remote_addr` (internal
 * storage within the connection struct).  It is valid ONLY while the
 * connection is alive AND `conn->in_use` remains true.  If the connection
 * disconnects concurrently, the underlying storage may be freed or
 * overwritten, producing a dangling pointer.
 *
 * The caller MUST ensure the connection stays alive for the entire
 * duration the returned pointer is accessed.  In practice the caller
 * should copy the string immediately (e.g. with strdup or by
 * marshalling to a managed string) while holding whatever lock
 * prevents the connection from being torn down.
 *
 * A future improvement could change this function to accept a
 * caller-provided buffer + size and copy into it, eliminating the
 * lifetime concern entirely. */
const char* wt_server_get_client_addr_impl(
    wt_server_s* server, wt_connection_id_t conn_id)
{
    if (!server || conn_id == 0 || conn_id > server->max_clients)
        return NULL;
    wt_server_conn_t* conn = &server->connections[conn_id];
    if (!atomic_load(&conn->in_use)) return NULL;
    return conn->remote_addr;
}

int32_t wt_server_get_client_count_impl(wt_server_s* server)
{
    if (!server) return 0;
    return (int32_t)atomic_load(&server->connection_count);
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

    wt_server_conn_t* conn = NULL;
    wt_connection_id_t conn_id = 0;

    for (uint32_t i = 1; i <= srv->max_clients; i++) {
        if (!atomic_load(&srv->connections[i].in_use)) {
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
    atomic_ptr_store(&conn->quic_conn, event->NEW_CONNECTION.Connection);
    conn->session = NULL;
    conn->h3_session = NULL;
    /* If the previous occupant of this slot queued a pending shutdown
     * that hasn't been drained by poll() yet, process it now to prevent
     * a permanent session leak. See SHUTDOWN_COMPLETE handler. */
    {
        wt_session_t* stale = (wt_session_t*)atomic_ptr_load(
            &conn->pending_shutdown_session);
        if (stale) {
            atomic_ptr_store(&conn->pending_shutdown_session, NULL);
            wt_session_shutdown(stale);
        }
    }
    conn->remote_addr[0] = '\0';
    atomic_store(&conn->state, WT_CONN_STATE_HANDSHAKING);
    atomic_store(&conn->in_use, true);
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
        if (!sconn->h3_session) {
            /* Fall back to raw QUIC (backward compatible) */
            WT_LOG_WARN("Failed to create HTTP/3 session for client %llu — falling back to raw QUIC",
                        (unsigned long long)sconn->id);
            wt_session_t* session = (wt_session_t*)calloc(1, sizeof(wt_session_t));
            if (!session) {
                MsQuic->ConnectionShutdown(conn,
                    QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
                break;
            }
            int32_t r = wt_session_init(session, conn, sconn->id);
            if (r != WT_OK) {
                WT_LOG_ERROR("Session init failed for client %llu",
                             (unsigned long long)sconn->id);
                free(session);
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

    case QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_TRANSPORT: {
        const unsigned long long ec =
            (unsigned long long)event->SHUTDOWN_INITIATED_BY_TRANSPORT.ErrorCode;
        const unsigned st =
            (unsigned)event->SHUTDOWN_INITIATED_BY_TRANSPORT.Status;
        const char* why = "unknown-transport";
        /* Common msquic / QUIC close codes seen in WT handshakes. */
        if (ec == 0x0) why = "NO_ERROR";
        else if (ec == 0x1) why = "INTERNAL_ERROR";
        else if (ec == 0x2) why = "CONNECTION_REFUSED";
        else if (ec == 0x3) why = "FLOW_CONTROL_ERROR";
        else if (ec == 0x4) why = "STREAM_LIMIT_ERROR";
        else if (ec == 0x5) why = "STREAM_STATE_ERROR";
        else if (ec == 0x6) why = "FINAL_SIZE_ERROR";
        else if (ec == 0x7) why = "FRAME_ENCODING_ERROR";
        else if (ec == 0x8) why = "TRANSPORT_PARAMETER_ERROR";
        else if (ec == 0x9) why = "CONNECTION_ID_LIMIT_ERROR";
        else if (ec == 0xa) why = "PROTOCOL_VIOLATION";
        else if (ec == 0xb) why = "INVALID_TOKEN";
        else if (ec == 0xc) why = "APPLICATION_ERROR";
        else if (ec == 0xd) why = "CRYPTO_BUFFER_EXCEEDED";
        else if (ec == 0xe) why = "KEY_UPDATE_ERROR";
        else if (ec == 0xf) why = "AEAD_LIMIT_REACHED";
        else if (ec == 0x10) why = "NO_VIABLE_PATH";
        else if (ec == 0x100) why = "H3_NO_ERROR/QUIC_IDLE?";
        else if (ec == 0x101) why = "H3_GENERAL_PROTOCOL_ERROR";
        else if (ec == 0x102) why = "H3_INTERNAL_ERROR";
        else if (ec == 0x103) why = "H3_STREAM_CREATION_ERROR";
        else if (ec == 0x104) why = "H3_CLOSED_CRITICAL_STREAM";
        else if (ec == 0x105) why = "H3_FRAME_UNEXPECTED";
        else if (ec == 0x106) why = "H3_FRAME_ERROR";
        else if (ec == 0x107) why = "H3_EXCESSIVE_LOAD";
        else if (ec == 0x108) why = "H3_ID_ERROR";
        else if (ec == 0x109) why = "H3_SETTINGS_ERROR";
        else if (ec == 0x10a) why = "H3_MISSING_SETTINGS";
        else if (ec == 0x10b) why = "H3_REQUEST_REJECTED";
        else if (ec == 0x10c) why = "H3_REQUEST_CANCELLED";
        else if (ec == 0x10d) why = "H3_REQUEST_INCOMPLETE";
        else if (ec == 0x10e) why = "H3_MESSAGE_ERROR";
        else if (ec == 0x10f) why = "H3_CONNECT_ERROR";
        else if (ec == 0x110) why = "H3_VERSION_FALLBACK";
        else if (ec == 0x200) why = "QPACK_DECOMPRESSION_FAILED";
        else if (ec == 0x201) why = "QPACK_ENCODER_STREAM_ERROR";
        else if (ec == 0x202) why = "QPACK_DECODER_STREAM_ERROR";

        int h3_state = -1;
        int settings_sent = 0;
        int handshake_done = 0;
        int peer_h3 = 0;
        if (sconn->h3_session) {
            h3_state = (int)sconn->h3_session->server_state;
            settings_sent = sconn->h3_session->server_settings_sent ? 1 : 0;
            handshake_done = sconn->h3_session->handshake_complete ? 1 : 0;
            peer_h3 = sconn->h3_session->peer_h3_seen ? 1 : 0;
        }
        /* H3_SRV_ESTABLISHED == CONNECT accepted; WAIT_CONNECT == SETTINGS done, no CONNECT yet. */
        WT_LOG_WARN(
            "Client %llu TRANSPORT shutdown status=0x%x error=0x%llx (%s) "
            "h3_state=%d settings_sent=%d peer_h3=%d handshake_done=%d "
            "connect_seen=%d (0=no Extended CONNECT yet)",
            (unsigned long long)sconn->id, st, ec, why,
            h3_state, settings_sent, peer_h3, handshake_done,
            handshake_done);
        /* Stop app-layer sends immediately (datagram encode crash fix). */
        atomic_store(&sconn->state, WT_CONN_STATE_DISCONNECTING);
        {
            wt_session_t* sess = (wt_session_t*)atomic_ptr_load(&sconn->session);
            if (sess && sess->stream_mgr)
                wt_stream_manager_mark_conn_closed(sess->stream_mgr);
        }
        break;
    }

    case QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_PEER: {
        const unsigned long long ec =
            (unsigned long long)event->SHUTDOWN_INITIATED_BY_PEER.ErrorCode;
        const char* why = "unknown-app/peer";
        if (ec == 0x0) why = "NO_ERROR";
        else if (ec == 0x100) why = "H3_NO_ERROR";
        else if (ec == 0x101) why = "H3_GENERAL_PROTOCOL_ERROR";
        else if (ec == 0x102) why = "H3_INTERNAL_ERROR";
        else if (ec == 0x103) why = "H3_STREAM_CREATION_ERROR";
        else if (ec == 0x104) why = "H3_CLOSED_CRITICAL_STREAM";
        else if (ec == 0x105) why = "H3_FRAME_UNEXPECTED";
        else if (ec == 0x106) why = "H3_FRAME_ERROR";
        else if (ec == 0x107) why = "H3_EXCESSIVE_LOAD";
        else if (ec == 0x108) why = "H3_ID_ERROR";
        else if (ec == 0x109) why = "H3_SETTINGS_ERROR";
        else if (ec == 0x10a) why = "H3_MISSING_SETTINGS";
        else if (ec == 0x10b) why = "H3_REQUEST_REJECTED";
        else if (ec == 0x10c) why = "H3_REQUEST_CANCELLED";
        else if (ec == 0x10d) why = "H3_REQUEST_INCOMPLETE";
        else if (ec == 0x10e) why = "H3_MESSAGE_ERROR";
        else if (ec == 0x10f) why = "H3_CONNECT_ERROR";
        else if (ec == 0x110) why = "H3_VERSION_FALLBACK";
        else if (ec == 0x200) why = "QPACK_DECOMPRESSION_FAILED";
        else if (ec == 0x201) why = "QPACK_ENCODER_STREAM_ERROR";
        else if (ec == 0x202) why = "QPACK_DECODER_STREAM_ERROR";
        else if (ec == 0x1c) why = "HTTP_REQUEST_CANCELLED?";
        else if (ec == 0x010c) why = "H3_REQUEST_CANCELLED";

        int h3_state = -1;
        int settings_sent = 0;
        int handshake_done = 0;
        int peer_h3 = 0;
        if (sconn->h3_session) {
            h3_state = (int)sconn->h3_session->server_state;
            settings_sent = sconn->h3_session->server_settings_sent ? 1 : 0;
            handshake_done = sconn->h3_session->handshake_complete ? 1 : 0;
            peer_h3 = sconn->h3_session->peer_h3_seen ? 1 : 0;
        }
        WT_LOG_WARN(
            "Client %llu PEER shutdown error=0x%llx (%s) "
            "h3_state=%d settings_sent=%d peer_h3=%d handshake_done=%d "
            "connect_seen=%d (0=browser closed before Extended CONNECT)",
            (unsigned long long)sconn->id, ec, why,
            h3_state, settings_sent, peer_h3, handshake_done,
            handshake_done);
        /* Stop app-layer sends immediately (datagram encode crash fix). */
        atomic_store(&sconn->state, WT_CONN_STATE_DISCONNECTING);
        {
            wt_session_t* sess = (wt_session_t*)atomic_ptr_load(&sconn->session);
            if (sess && sess->stream_mgr)
                wt_stream_manager_mark_conn_closed(sess->stream_mgr);
        }
        break;
    }

    case QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE: {
        /* Atomic load — no non-atomic guard (avoids torn read on ARM). */
        {
            wt_session_t* old_session = (wt_session_t*)atomic_ptr_load(&sconn->session);
            if (old_session) {
                /* Mark stream manager: connection handles are about to die.
                 * Must happen BEFORE ConnectionClose and before deferred
                 * wt_session_shutdown → StreamShutdown (quic_bugcheck fix). */
                if (old_session->stream_mgr)
                    wt_stream_manager_mark_conn_closed(old_session->stream_mgr);

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
             * Use subtraction-based full check to avoid uint32_t
             * wraparound when tail is near UINT32_MAX.  The ring
             * buffer holds WT_MAX_CLIENTS entries; we reject when
             * occupancy reaches WT_MAX_CLIENTS - 1 to leave one
             * guard slot. */
            uint32_t head = atomic_load(&sconn->owner->pending_shutdown_head);
            atomic_thread_fence(std::memory_order_acquire);
            uint32_t tail = atomic_load(
                &sconn->owner->pending_shutdown_tail);
            if ((tail - head) >= WT_MAX_CLIENTS - 1) {
                /* Confirm: re-read head.  If the consumer advanced it,
                 * recompute occupancy with the fresh snapshot. */
                uint32_t head2 = atomic_load(
                    &sconn->owner->pending_shutdown_head);
                if (head2 != head) {
                    head = head2;
                    atomic_thread_fence(std::memory_order_acquire);
                    tail = atomic_load(
                        &sconn->owner->pending_shutdown_tail);
                }
            }
            if ((tail - head) >= WT_MAX_CLIENTS - 1) {
                WT_LOG_ERROR("Pending shutdown queue overflow — freeing session for connection %llu immediately (tail %u, head %u)",
                            (unsigned long long)sconn->id, tail, head);
                /* ── CRITICAL: Free session immediately ───────────
                 * The pending shutdown queue is full. Instead of
                 * silently dropping this connection ID (which would
                 * leak the session), shut down the session right now.
                 * We are in SHUTDOWN_COMPLETE on a QUIC callback
                 * thread — no in-flight sends remain for this
                 * session, so calling wt_session_shutdown directly
                 * is safe (it sets released=true, shuts down the
                 * stream manager, and drops the owner reference). */
                wt_session_t* overflow_session =
                    (wt_session_t*)atomic_ptr_load(
                        &sconn->pending_shutdown_session);
                if (overflow_session) {
                    atomic_ptr_store(
                        &sconn->pending_shutdown_session, NULL);
                    wt_session_shutdown(overflow_session);
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
                uint32_t claimed_tail = atomic_fetch_add(
                    &sconn->owner->pending_shutdown_tail, 1);
                sconn->owner->pending_shutdown_queue[
                    claimed_tail % WT_MAX_CLIENTS] = sconn->id;
                /* Release fence: ensures the slot write above is visible
                 * before the tail update observed by the consumer.
                 * Paired with the acquire fence in wt_server_poll_impl. */
                atomic_thread_fence(std::memory_order_release);
            }
        }

        /* Signal completion BEFORE firing the disconnect callback.
         * If the callback triggers wt_server_destroy → free_impl,
         * pending_shutdowns==0 allows the spin-wait to exit cleanly
         * rather than deadlocking.  sconn->owner is still valid here
         * because free_impl waits for pending_shutdowns before freeing.
         * Guard against unsigned underflow: if ConnectionClose fires
         * SHUTDOWN_COMPLETE synchronously on the never-connected path,
         * pending_shutdowns may not have been incremented yet.
         * Use a single atomic_fetch_sub and check the pre-decrement
         * value to eliminate the TOCTOU race between the separate
         * atomic_load and atomic_fetch_sub. */
        if (sconn->owner) {
            unsigned int prev_sub = atomic_fetch_sub(
                &sconn->owner->pending_shutdowns, 1);
            if (prev_sub == 0) {
                /* Underflow — correct it. */
                atomic_fetch_add(&sconn->owner->pending_shutdowns, 1);
            }
        }

        /* Fire callback LAST — after all state changes are committed.
         * The callback may enqueue work that calls back into the API;
         * all invariants (in_use=false, state=CLOSED, pending_shutdowns
         * decremented) are established before any re-entrant call.
         * Only fire if the connection was actually in use (vs duplicate
         * SHUTDOWN_COMPLETE for an already-closed connection). */
        if (was_in_use)
            fire_disconnect(sconn, 0);
        break;
    }

    case QUIC_CONNECTION_EVENT_PEER_STREAM_STARTED: {
        /* ── Protocol Detection: HTTP/3 vs Raw QUIC ──────────
         * If h3_session exists (handshake not yet complete), pass the
         * stream to it for protocol detection and HTTP/3 processing.
         * Uni streams (control/QPACK) must be flagged — Chrome often
         * opens QPACK before control; treating them as native causes
         * browser QUIC_NETWORK_IDLE_TIMEOUT and can crash on bad data. */
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
        } else if (sconn->h3_session && sconn->h3_session->handshake_complete) {
            /* Post-CONNECT data stream from browser (FishNet ClientHandshake /
             * CreateAccount / etc.). Log so ops can confirm app traffic. */
            const bool is_uni =
                (event->PEER_STREAM_STARTED.Flags &
                 QUIC_STREAM_OPEN_FLAG_UNIDIRECTIONAL) != 0;
            WT_LOG_INFO(
                "Client %llu post-handshake peer stream uni=%d "
                "(expect FishNet app data after STREAM_DELIVER_ON_RECV)",
                (unsigned long long)sconn->id, is_uni ? 1 : 0);
        }

        /* Acquire session refcount to prevent UAF — wt_session_shutdown
         * on the poll thread may concurrently set stream_mgr=NULL and
         * free it.  Holding a ref keeps the session (and its stream_mgr
         * pointer) alive until we release. */
        {
            wt_session_t* session = (wt_session_t*)atomic_ptr_load(&sconn->session);
            if (!session || !wt_session_acquire(session)) {
                WT_LOG_WARN(
                    "Client %llu peer stream ABORT — no session yet "
                    "(handshake_done=%d); FishNet data dropped",
                    (unsigned long long)sconn->id,
                    sconn->h3_session && sconn->h3_session->handshake_complete
                        ? 1 : 0);
                MsQuic->StreamShutdown(event->PEER_STREAM_STARTED.Stream,
                                        QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                MsQuic->StreamClose(event->PEER_STREAM_STARTED.Stream);
                break;
            }
            /* Re-check that the session pointer hasn't changed and
             * the stream_mgr is still valid AFTER acquiring. */
            if (atomic_ptr_load(&sconn->session) != session ||
                !session->stream_mgr) {
                WT_LOG_WARN(
                    "Client %llu peer stream ABORT — stream_mgr missing after acquire",
                    (unsigned long long)sconn->id);
                wt_session_release(session);
                MsQuic->StreamShutdown(event->PEER_STREAM_STARTED.Stream,
                                        QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                MsQuic->StreamClose(event->PEER_STREAM_STARTED.Stream);
                break;
            }
            WT_LOG_INFO(
                "Client %llu accepting peer stream into stream_mgr "
                "stamp=WT_CAPSULE_NO_LEN_V1 FIRST_APP_STREAM_AFTER_SESSION "
                "(expect ClientHandshake / FishNet bytes next — not just TRANSPORT shutdown)",
                (unsigned long long)sconn->id);
            wt_stream_manager_accept_stream(
                session->stream_mgr,
                event->PEER_STREAM_STARTED.Stream);
            wt_session_release(session);
        }
        break;
    }

    case QUIC_CONNECTION_EVENT_DATAGRAM_RECEIVED: {
        /* Atomic load — avoid non-atomic read of session/owner pointers. */
        if (!sconn->owner) break;
        {
            wt_session_t* session = (wt_session_t*)atomic_ptr_load(&sconn->session);
            if (!session) break;

            const QUIC_BUFFER* buf = event->DATAGRAM_RECEIVED.Buffer;
            if (buf && buf->Length > 0 &&
                buf->Length <= WT_DGRAM_MAX_SIZE) {
                WT_LOG_INFO(
                    "Client %llu DATAGRAM recv len=%u first=%02x "
                    "stamp=WT_CAPSULE_NO_LEN_V1",
                    (unsigned long long)sconn->id,
                    (unsigned)buf->Length,
                    buf->Buffer ? buf->Buffer[0] : 0);
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