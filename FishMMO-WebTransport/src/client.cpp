/**
 * @file client.cpp
 * @brief WebTransport QUIC client implementation using msquic.
 */

#include <time.h>
#include "client.h"
#include <stdlib.h>
#include <string.h>

/* ── Forward ────────────────────────────────────────────────── */

static QUIC_STATUS QUIC_API client_conn_cb(HQUIC conn, void* ctx,
                                   QUIC_CONNECTION_EVENT* event);
static void on_client_dgram_drain(void* ctx, wt_connection_id_t conn_id,
                                   const uint8_t* data, int32_t length);

/* ═══════════════════════════════════════════════════════════════
 * INTERNAL API
 * ═══════════════════════════════════════════════════════════════ */

wt_client_s* wt_client_alloc_impl(
    const wt_client_callbacks_t* callbacks, void* context)
{
    if (!callbacks) return NULL;

    wt_client_s* cli = (wt_client_s*)calloc(1, sizeof(wt_client_s));
    if (!cli) return NULL;

    memcpy(&cli->callbacks, callbacks, sizeof(*callbacks));
    cli->user_context = context;
    atomic_init(&cli->state, WT_CLIENT_STOPPED);
    atomic_init(&cli->connected, false);
    atomic_init(&cli->pending_shutdowns, 0);

    wt_datagram_queue_init(&cli->dgram_queue);
    return cli;
}

void wt_client_free_impl(wt_client_s* client)
{
    if (!client) return;

    wt_client_disconnect_impl(client);

    /* Use pending_shutdowns to decide path — not state.
     * disconnect_impl sets state=STOPPED before ConnectionShutdown
     * completes, so state is unreliable for distinguishing paths. */
    if (atomic_load(&client->pending_shutdowns) > 0) {
        /* Spin-wait for SHUTDOWN_COMPLETE to finish (bounded).
         * 300 iterations * 10ms = 3 second max wait.
         * The callback closes the QUIC connection and signals
         * pending_shutdowns=0. We close config/registration here
         * on the application thread to avoid calling MsQuic
         * handle-close functions from within a callback. */
        int retries = 300;
        while (atomic_load(&client->pending_shutdowns) > 0 && retries-- > 0) {
#if defined(WT_PLATFORM_WINDOWS)
            Sleep(10);
#else
            struct timespec ts = {0, 10000000}; /* 10ms */
            nanosleep(&ts, NULL);
#endif
        }
        if (retries < 0) {
            WT_LOG_WARN("Client shutdown timed out (pending=%u), forcing free",
                        (unsigned)atomic_load(&client->pending_shutdowns));
        }

        /* Close handles deferred from SHUTDOWN_COMPLETE callback.
         * Order: config before registration (reverse of creation). */
        if (client->session_config) {
            MsQuic->ConfigurationClose(client->session_config);
            client->session_config = NULL;
        }
        if (client->registration) {
            MsQuic->RegistrationClose(client->registration);
            client->registration = NULL;
        }

        wt_datagram_queue_destroy(&client->dgram_queue);
        free(client);
        return;
    }

    /* Never connected — clean up inline. Handle closure order: conn first. */
    if (client->quic_conn) {
        MsQuic->ConnectionClose(client->quic_conn);
        client->quic_conn = NULL;
    }
    if (client->session_config) {
        MsQuic->ConfigurationClose(client->session_config);
        client->session_config = NULL;
    }
    if (client->registration) {
        MsQuic->RegistrationClose(client->registration);
        client->registration = NULL;
    }
    wt_datagram_queue_destroy(&client->dgram_queue);
    free(client);
}

int32_t wt_client_connect_impl(
    wt_client_s* client, const char* server_name,
    const char* address, uint16_t port, bool use_tls)
{
    if (!client || !server_name || !address)
        return WT_ERR_UNKNOWN;

    if (atomic_load(&client->state) != WT_CLIENT_STOPPED)
        return WT_ERR_INVALID_STATE;

    strncpy(client->server_name, server_name,
            sizeof(client->server_name) - 1);
    client->server_name[sizeof(client->server_name) - 1] = '\0';
    /* Copy address, stripping any URL path (e.g. "host/wt/port" → "host").
     * The path is only meaningful for WebGL URL construction; native QUIC
     * connections target host:port directly. */
    {
        const char* slash = strchr(address, '/');
        size_t addr_len = slash ? (size_t)(slash - address) : strlen(address);
        if (addr_len >= sizeof(client->address)) addr_len = sizeof(client->address) - 1;
        memcpy(client->address, address, addr_len);
        client->address[addr_len] = '\0';
    }
    client->port = port;
    client->use_tls = use_tls;

    QUIC_STATUS status;

    /* ── Registration ── */
    QUIC_REGISTRATION_CONFIG reg_cfg = {0};
    reg_cfg.AppName = "fishmmo_wt_client";
    reg_cfg.ExecutionProfile = QUIC_EXECUTION_PROFILE_LOW_LATENCY;
    status = MsQuic->RegistrationOpen(&reg_cfg, &client->registration);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("RegistrationOpen: 0x%x", status);
        return WT_ERR_UNKNOWN;
    }

    /* ── Settings ── */
    QUIC_SETTINGS settings = {0};
    settings.IdleTimeoutMs = WT_DEFAULT_IDLE_TIMEOUT_MS;
    settings.IsSet.IdleTimeoutMs = TRUE;
    settings.DatagramReceiveEnabled = TRUE;
    settings.IsSet.DatagramReceiveEnabled = TRUE;
    settings.PeerBidiStreamCount = WT_MAX_STREAMS;
    settings.IsSet.PeerBidiStreamCount = TRUE;
    settings.PeerUnidiStreamCount = WT_MAX_STREAMS;
    settings.IsSet.PeerUnidiStreamCount = TRUE;

    /* ── Open configuration with ALPN ── */
    QUIC_BUFFER alpn_buf;
    alpn_buf.Buffer = (uint8_t*)"h3";
    alpn_buf.Length = 2;

    status = MsQuic->ConfigurationOpen(client->registration, &alpn_buf, 1U, &settings, (uint32_t)sizeof(settings), NULL, &client->session_config);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("ConfigurationOpen: 0x%x", status);
        MsQuic->RegistrationClose(client->registration);
        client->registration = NULL;
        return WT_ERR_UNKNOWN;
    }

    /* ── TLS credentials ──
     * QUIC_CREDENTIAL_TYPE_NONE  = no client certificate (mutual TLS not required)
     * QUIC_CREDENTIAL_FLAG_CLIENT = client-mode handshake
     * QUIC_CREDENTIAL_FLAG_INDICATE_CERTIFICATE_RECEIVED = deliver the server
     *   certificate chain via QUIC_CONNECTION_EVENT_PEER_CERTIFICATE_RECEIVED
     *   so we can log the certificate for diagnostics.
     *
     * Server certificate validation is ON by default (platform trust store).
     * DO NOT add QUIC_CREDENTIAL_FLAG_NO_CERTIFICATE_VALIDATION — that would
     * disable verification and accept any self-signed / MITM certificate. */
    QUIC_CREDENTIAL_CONFIG cred_cfg;
    memset(&cred_cfg, 0, sizeof(cred_cfg));
    cred_cfg.Type = QUIC_CREDENTIAL_TYPE_NONE;
    cred_cfg.Flags = QUIC_CREDENTIAL_FLAG_CLIENT
                   | QUIC_CREDENTIAL_FLAG_INDICATE_CERTIFICATE_RECEIVED;

    status = MsQuic->ConfigurationLoadCredential(
        client->session_config, &cred_cfg);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("ConfigurationLoadCredential: 0x%x", status);
        MsQuic->ConfigurationClose(client->session_config);
        MsQuic->RegistrationClose(client->registration);
        client->session_config = NULL;
        client->registration = NULL;
        return WT_ERR_TLS_FAILED;
    }

    /* ── Create connection ── */
    status = MsQuic->ConnectionOpen(client->registration,
                                     client_conn_cb, client,
                                     &client->quic_conn);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("ConnectionOpen: 0x%x", status);
        MsQuic->ConfigurationClose(client->session_config);
        MsQuic->RegistrationClose(client->registration);
        client->session_config = NULL;
        client->registration = NULL;
        return WT_ERR_UNKNOWN;
    }

    /* ── Start connection ──
     * Use the address (IP or hostname) for the connection target.
     * server_name is only used for TLS SNI. msquic ConnectionStart
     * resolves the 4th parameter via DNS if it's a hostname, or
     * connects directly if it's an IP. */
    atomic_store(&client->state, WT_CLIENT_STARTING);
    status = MsQuic->ConnectionStart(client->quic_conn,
                                      client->session_config,
                                      QUIC_ADDRESS_FAMILY_UNSPEC,
                                      client->address, port);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("ConnectionStart: 0x%x", status);
        MsQuic->ConnectionClose(client->quic_conn);
        MsQuic->ConfigurationClose(client->session_config);
        MsQuic->RegistrationClose(client->registration);
        client->quic_conn = NULL;
        client->session_config = NULL;
        client->registration = NULL;
        atomic_store(&client->state, WT_CLIENT_STOPPED);
        return WT_ERR_CONNECT_FAILED;
    }

    WT_LOG_INFO("Connecting to %s:%u (SNI: %s)...",
                address, port, server_name);
    return WT_OK;
}

void wt_client_disconnect_impl(wt_client_s* client)
{
    if (!client) return;

    int expected = WT_CLIENT_STARTED;
    if (!atomic_compare_exchange_strong(&client->state, &expected,
                                         WT_CLIENT_STOPPING))
        return;

    /* Update state BEFORE ConnectionShutdown (sync callback may free client) */
    atomic_store(&client->connected, false);
    HQUIC conn = client->quic_conn;
    client->quic_conn = NULL;
    atomic_store(&client->state, WT_CLIENT_STOPPED);

    /* Shut down connection FIRST — stream SHUTDOWN_COMPLETE callbacks
     * fire as part of connection teardown. Session cleanup comes after
     * to avoid UAF on stream_mgr during async callbacks. */
    if (conn) {
        atomic_fetch_add(&client->pending_shutdowns, 1);
        MsQuic->ConnectionShutdown(conn,
                                    QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
    }
    /* Session cleanup deferred to SHUTDOWN_COMPLETE. */
}

void wt_client_poll_impl(wt_client_s* client, int32_t timeout_us)
{
    (void)timeout_us;
    if (!client) return;

    /* Process deferred session shutdown (set by QUIC callback thread).
     * This runs on the application thread — same thread as send —
     * guaranteeing no TOCTOU race between session free and acquire. */
    {
        wt_session_t* s = (wt_session_t*)atomic_ptr_load(&client->pending_shutdown_session);
        if (s) {
            atomic_ptr_store(&client->pending_shutdown_session, NULL);
            wt_session_shutdown(s);
            /* wt_session_shutdown releases the owner reference — session
             * is freed here if no in-flight sends hold a ref. */
        }
    }

    wt_datagram_queue_drain(&client->dgram_queue,
                             on_client_dgram_drain, client);
}

int32_t wt_client_send_stream_impl(
    wt_client_s* client, const uint8_t* data, int32_t length)
{
    if (!client || !data || length <= 0)
        return WT_ERR_SEND_FAILED;

    wt_session_t* session = (wt_session_t*)atomic_ptr_load(&client->session);
    if (!session || !wt_session_acquire(session))
        return WT_ERR_INVALID_STATE;

    /* Re-check state AFTER acquiring — SHUTDOWN_COMPLETE may have
     * nulled session and set connected=false between our load and acquire.
     * If session ptr changed, our acquire is on a potentially stale
     * (but still alive) session — release and bail. */
    if (atomic_ptr_load(&client->session) != session ||
        !atomic_load(&client->connected)) {
        wt_session_release(session);
        return WT_ERR_INVALID_STATE;
    }

    int32_t result = wt_session_send_stream(session, data, length);
    wt_session_release(session);
    return result;
}

int32_t wt_client_send_datagram_impl(
    wt_client_s* client, const uint8_t* data, int32_t length)
{
    if (!client || !data || length <= 0)
        return WT_ERR_SEND_FAILED;

    wt_session_t* session = (wt_session_t*)atomic_ptr_load(&client->session);
    if (!session || !wt_session_acquire(session))
        return WT_ERR_INVALID_STATE;

    if (atomic_ptr_load(&client->session) != session ||
        !atomic_load(&client->connected)) {
        wt_session_release(session);
        return WT_ERR_INVALID_STATE;
    }

    int32_t result = wt_session_send_datagram(session, data, length);
    wt_session_release(session);
    return result;
}

bool wt_client_is_connected_impl(wt_client_s* client)
{
    if (!client) return false;
    return atomic_load(&client->connected);
}

int32_t wt_client_get_mtu_impl(wt_client_s* client)
{
    /* QUIC requires a minimum path MTU of 1200 bytes for UDP payloads.
     * Subtracting QUIC long-header overhead (~60 bytes for initial,
     * ~20 bytes for 1-RTT) gives ~1140 usable for datagrams.
     *
     * MsQuic performs Path MTU Discovery (PMTUD) automatically, but
     * the negotiated MTU is internal.  A future enhancement could
     * expose QUIC_PARAM_CONN_STATISTICS.Pmtu from MsQuic's private
     * connection struct, but 1200 is the safe, always-works value
     * that avoids IP fragmentation across all paths. */
    (void)client;
    return WT_DEFAULT_MTU;
}

/* ═══════════════════════════════════════════════════════════════
 * QUIC CALLBACK
 * ═══════════════════════════════════════════════════════════════ */

static QUIC_STATUS QUIC_API
client_conn_cb(HQUIC conn, void* ctx, QUIC_CONNECTION_EVENT* event)
{
    wt_client_s* cli = (wt_client_s*)ctx;

    switch (event->Type) {

    case QUIC_CONNECTION_EVENT_CONNECTED: {
        WT_LOG_INFO("Client connected!");

        /* ── HTTP/3 WebTransport Handshake (Optional) ─────────
         * If an h3_session was created before connecting, perform
         * the HTTP/3 WebTransport handshake. Otherwise fall through
         * to native raw-QUIC mode (backward compatible).
         *
         * h3_session is created by the C# layer when connecting to
         * a standard WebTransport server (e.g., for testing against
         * reference implementations). It is NULL for native-to-native
         * connections. */
        if (cli->h3_session) {
            /* ── HTTP/3 Client Handshake ─────────────────────────────
             * The CONNECT request stream now uses h3_client_stream_cb
             * (defined in http3.cpp) which handles RECEIVE events and
             * buffers the server's 200 OK response.  h3_client_process_data
             * parses the HEADERS frame, extracts :status, and on 200 OK
             * transitions to H3_CLI_ESTABLISHED and fires the on_ready
             * callback to create the wt_session.
             *
             * See h3_client_connect (http3.cpp) for the request stream
             * setup, and h3_client_process_data for the response parsing. */
            int32_t hr = h3_client_connect(cli->h3_session, "/", cli->server_name);
            if (hr != 0) {
                WT_LOG_ERROR("HTTP/3 client handshake start failed");
                cli->h3_session->client_state = H3_CLI_ERROR;
                MsQuic->ConnectionShutdown(conn,
                    QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
            }
            /* Session init deferred to on_h3_session_ready callback. */
            break;
        }

        /* Native raw-QUIC path (backward compatible) */
        wt_session_t* session = (wt_session_t*)calloc(1, sizeof(wt_session_t));
        if (!session) {
            atomic_fetch_add(&cli->pending_shutdowns, 1);
            MsQuic->ConnectionShutdown(conn,
                QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
            break;
        }

        int32_t r = wt_session_init(session, conn, WT_CONNECTION_ID_NONE);
        if (r != WT_OK) {
            WT_LOG_ERROR("Session init failed");
            free(session);
            atomic_fetch_add(&cli->pending_shutdowns, 1);
            MsQuic->ConnectionShutdown(conn,
                QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
            break;
        }

        atomic_ptr_store(&cli->session, session);
        session->parent_type = WT_PARENT_CLIENT;
        session->parent.client = cli;
        wt_session_wire_callbacks(session);

        atomic_store(&cli->connected, true);
        atomic_store(&cli->state, WT_CLIENT_STARTED);

        if (cli->callbacks.on_connect)
            cli->callbacks.on_connect(cli->user_context);
        break;
    }

    case QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE: {
        WT_LOG_INFO("Client disconnected.");
        atomic_store(&cli->connected, false);

        /* Atomic load — no non-atomic guard (avoids torn read on ARM). */
        {
            wt_session_t* old_session = (wt_session_t*)atomic_ptr_load(&cli->session);
            if (old_session) {
                atomic_ptr_store(&cli->session, NULL);

                /* Defer shutdown to poll (application thread) to guarantee
                 * session free never races with in-flight sends. The poll
                 * thread is the same thread that calls send, so no TOCTOU.
                 * Use atomic_ptr_store because the poll thread reads this
                 * field without a lock. */
                atomic_ptr_store(&cli->pending_shutdown_session, old_session);
            }
        }

        MsQuic->ConnectionClose(conn);
        cli->quic_conn = NULL;

        /* Clean up any incomplete HTTP/3 handshake */
        if (cli->h3_session) {
            h3_session_free(cli->h3_session);
            cli->h3_session = NULL;
        }

        /* Do NOT close session_config or registration here — they are
         * closed by free_impl on the application thread after the
         * pending_shutdowns spin-wait. Closing handles from within a
         * QUIC callback is fragile and can race with internal state. */

        /* Signal completion BEFORE firing the disconnect callback.
         * If the callback triggers wt_client_destroy → free_impl,
         * pending_shutdowns==0 allows the spin-wait to exit cleanly
         * rather than deadlocking. */
        atomic_fetch_sub(&cli->pending_shutdowns, 1);

        /* Fire callback LAST — after all state changes are committed.
         * The callback may enqueue work that calls back into the API;
         * all invariants (connected=false, session=deferred, quic_conn=NULL)
         * are established before any re-entrant call. */
        if (cli->callbacks.on_disconnect)
            cli->callbacks.on_disconnect(cli->user_context, 0);
        break;
    }

    case QUIC_CONNECTION_EVENT_PEER_CERTIFICATE_RECEIVED: {
        /* Log the server certificate for diagnostics.
         * Validation against the platform trust store is performed by
         * msquic/TLS before this event fires — if the cert failed
         * validation the connection is already aborted. */
        WT_LOG_INFO("Server certificate received and validated.");
        break;
    }

    case QUIC_CONNECTION_EVENT_PEER_STREAM_STARTED: {
        /* Acquire session refcount to prevent UAF — wt_session_shutdown
         * on the poll thread may concurrently set stream_mgr=NULL and
         * free it.  Holding a ref keeps the session (and its stream_mgr
         * pointer) alive until we release. */
        {
            wt_session_t* session = (wt_session_t*)atomic_ptr_load(&cli->session);
            if (!session || !wt_session_acquire(session)) {
                MsQuic->StreamShutdown(event->PEER_STREAM_STARTED.Stream,
                                        QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                MsQuic->StreamClose(event->PEER_STREAM_STARTED.Stream);
                break;
            }
            /* Re-check that the session pointer hasn't changed and
             * the stream_mgr is still valid AFTER acquiring. */
            if (atomic_ptr_load(&cli->session) != session ||
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
        /* Atomic load — avoid non-atomic read of session pointer. */
        {
            wt_session_t* session = (wt_session_t*)atomic_ptr_load(&cli->session);
            if (!session) break;

            static atomic_int dgram_drop_count = 0;
            const QUIC_BUFFER* buf = event->DATAGRAM_RECEIVED.Buffer;
            if (buf && buf->Length > 0 &&
                buf->Length <= WT_DGRAM_MAX_SIZE) {
                if (!wt_datagram_queue_push(
                        &cli->dgram_queue, 0, buf->Buffer,
                        (int32_t)buf->Length)) {
                    int prev = atomic_fetch_add(&dgram_drop_count, 1);
                    if (prev % 100 == 0) {
                        WT_LOG_WARN("Datagram queue full: %d drops so far", prev + 1);
                    }
                }
            }
        }
        break;
    }

    case QUIC_CONNECTION_EVENT_DATAGRAM_SEND_STATE_CHANGED:
        if (event->DATAGRAM_SEND_STATE_CHANGED.ClientContext &&
            QUIC_DATAGRAM_SEND_STATE_IS_FINAL(
                event->DATAGRAM_SEND_STATE_CHANGED.State)) {
            free(event->DATAGRAM_SEND_STATE_CHANGED.ClientContext);
            event->DATAGRAM_SEND_STATE_CHANGED.ClientContext = NULL;
        }
        break;

    default:
        break;
    }
    return QUIC_STATUS_SUCCESS;
}

static void on_client_dgram_drain(void* ctx, wt_connection_id_t conn_id,
                                   const uint8_t* data, int32_t length)
{
    (void)conn_id;
    wt_client_s* cli = (wt_client_s*)ctx;
    if (cli->callbacks.on_datagram)
        cli->callbacks.on_datagram(cli->user_context, data, length);
}