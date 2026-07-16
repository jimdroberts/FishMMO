/**
 * @file server.cpp
 * @brief WebTransport QUIC server implementation using msquic.
 */

#include "server.h"
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

/* ═══════════════════════════════════════════════════════════════
 * INTERNAL API
 * ═══════════════════════════════════════════════════════════════ */

wt_server_s* wt_server_alloc_impl(
    const char* cert_path, const char* key_path,
    const char* alpn, const char* bind_address,
    uint16_t port, uint32_t max_clients,
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
    srv->max_clients = max_clients > 0 ? max_clients : WT_MAX_CLIENTS;
    memcpy(&srv->callbacks, callbacks, sizeof(*callbacks));
    srv->user_context = context;

    atomic_init(&srv->state, WT_SERVER_STOPPED);
    atomic_init(&srv->connection_count, 0);
    atomic_init(&srv->pending_shutdowns, 0);

    srv->connections = (wt_server_conn_t*)calloc(
        srv->max_clients, sizeof(wt_server_conn_t));
    if (!srv->connections) { free(srv); return NULL; }

    wt_datagram_queue_init(&srv->dgram_queue);
    return srv;
}

void wt_server_free_impl(wt_server_s* server)
{
    if (!server) return;
    wt_server_stop_impl(server);

    /* Wait for pending SHUTDOWN_COMPLETE callbacks (bounded).
     * 300 iterations * 10ms = 3 second max wait. */
    int retries = 300;
    while (atomic_load(&server->pending_shutdowns) > 0 && retries-- > 0) {
#if defined(WT_PLATFORM_WINDOWS)
        Sleep(10);
#else
        usleep(10000);
#endif
    }
    if (retries < 0) {
        WT_LOG_WARN("Timed out waiting for %u pending shutdowns",
                    (unsigned)atomic_load(&server->pending_shutdowns));
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

    /* ── Listener ── */
    QUIC_ADDR addr = {0};
    QuicAddrSetFamily(&addr, QUIC_ADDRESS_FAMILY_UNSPEC);
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

    for (uint32_t i = 1; i < server->max_clients; i++) {
        if (server->connections[i].in_use)
            wt_server_disconnect_impl(server, server->connections[i].id);
    }

    if (server->listener) {
        MsQuic->ListenerClose(server->listener);
        server->listener = NULL;
    }
    if (server->session_config) {
        MsQuic->ConfigurationClose(server->session_config);
        server->session_config = NULL;
    }
    if (server->registration) {
        MsQuic->RegistrationClose(server->registration);
        server->registration = NULL;
    }

    atomic_store(&server->state, WT_SERVER_STOPPED);
    WT_LOG_INFO("Server stopped.");
}

void wt_server_poll_impl(wt_server_s* server, int32_t timeout_us)
{
    (void)timeout_us;
    if (!server || atomic_load(&server->state) != WT_SERVER_STARTED)
        return;
    wt_datagram_queue_drain(&server->dgram_queue,
                             on_server_dgram_drain, server);
}

int32_t wt_server_send_stream_impl(
    wt_server_s* server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length)
{
    if (!server || !data || length <= 0) return WT_ERR_SEND_FAILED;

    if (conn_id == WT_BROADCAST_ALL) {
        int32_t worst = WT_OK;
        for (uint32_t i = 1; i < server->max_clients; i++) {
            wt_server_conn_t* c = &server->connections[i];
            if (c->in_use && c->state == WT_CONN_STATE_CONNECTED && c->session) {
                int32_t r = wt_session_send_stream(c->session, data, length);
                if (r != WT_OK) worst = r;
            }
        }
        return worst;
    }

    if (conn_id == 0 || conn_id >= server->max_clients)
        return WT_ERR_NOT_FOUND;

    wt_server_conn_t* conn = &server->connections[conn_id];
    if (!conn->in_use || conn->state != WT_CONN_STATE_CONNECTED ||
        !conn->session)
        return WT_ERR_NOT_FOUND;

    return wt_session_send_stream(conn->session, data, length);
}

int32_t wt_server_send_datagram_impl(
    wt_server_s* server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length)
{
    if (!server || !data || length <= 0) return WT_ERR_SEND_FAILED;

    if (conn_id == WT_BROADCAST_ALL) {
        int32_t worst = WT_OK;
        for (uint32_t i = 1; i < server->max_clients; i++) {
            wt_server_conn_t* c = &server->connections[i];
            if (c->in_use && c->state == WT_CONN_STATE_CONNECTED && c->session) {
                int32_t r = wt_session_send_datagram(c->session, data, length);
                if (r != WT_OK) worst = r;
            }
        }
        return worst;
    }

    if (conn_id == 0 || conn_id >= server->max_clients)
        return WT_ERR_NOT_FOUND;

    wt_server_conn_t* conn = &server->connections[conn_id];
    if (!conn->in_use || conn->state != WT_CONN_STATE_CONNECTED ||
        !conn->session)
        return WT_ERR_NOT_FOUND;

    return wt_session_send_datagram(conn->session, data, length);
}

void wt_server_disconnect_impl(
    wt_server_s* server, wt_connection_id_t conn_id)
{
    if (!server || conn_id == 0 || conn_id >= server->max_clients)
        return;

    wt_server_conn_t* conn = &server->connections[conn_id];
    if (!conn->in_use) return;

    /* Atomically claim the shutdown — prevents double-increment
     * if called twice for the same connection. */
    HQUIC qconn = conn->quic_conn;
    if (!qconn) return;  /* already shutting down */
    conn->quic_conn = NULL;

    /* Do NOT set in_use=false here — SHUTDOWN_COMPLETE owns that. */
    atomic_fetch_add(&server->pending_shutdowns, 1);
    MsQuic->ConnectionShutdown(qconn,
                                QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
    /* Session cleanup deferred to SHUTDOWN_COMPLETE. */

    conn->state = WT_CONN_STATE_CLOSED;
    /* fire_disconnect is called by SHUTDOWN_COMPLETE — don't double-fire */
}

const char* wt_server_get_client_addr_impl(
    wt_server_s* server, wt_connection_id_t conn_id)
{
    if (!server || conn_id == 0 || conn_id >= server->max_clients)
        return NULL;
    wt_server_conn_t* conn = &server->connections[conn_id];
    if (!conn->in_use) return NULL;
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

    /* Reject connections if server is not running */
    if (atomic_load(&srv->state) != WT_SERVER_STARTED) {
        if (event->Type == QUIC_LISTENER_EVENT_NEW_CONNECTION)
            MsQuic->ConnectionClose(event->NEW_CONNECTION.Connection);
        return QUIC_STATUS_SUCCESS;
    }

    if (event->Type != QUIC_LISTENER_EVENT_NEW_CONNECTION)
        return QUIC_STATUS_SUCCESS;

    wt_server_conn_t* conn = NULL;
    wt_connection_id_t conn_id = 0;

    for (uint32_t i = 1; i < srv->max_clients; i++) {
        if (!srv->connections[i].in_use) {
            conn = &srv->connections[i];
            conn_id = i;
            break;
        }
    }
    if (!conn) {
        MsQuic->ConnectionClose(event->NEW_CONNECTION.Connection);
        return QUIC_STATUS_SUCCESS;  /* handle closed, tell msquic we handled it */
    }

    memset(conn, 0, sizeof(*conn));
    conn->id = conn_id;
    conn->quic_conn = event->NEW_CONNECTION.Connection;
    conn->state = WT_CONN_STATE_HANDSHAKING;
    conn->in_use = true;
    conn->owner = srv;

    atomic_fetch_add(&srv->connection_count, 1);

    MsQuic->SetCallbackHandler(conn->quic_conn,
                                (void*)server_conn_cb, conn);
    status = MsQuic->ConnectionSetConfiguration(conn->quic_conn,
                                        srv->session_config);
    if (QUIC_FAILED(status)) {
        WT_LOG_WARN("ConnectionSetConfiguration: 0x%x", status);
        MsQuic->ConnectionClose(conn->quic_conn);
        conn->in_use = false;
        atomic_fetch_sub(&srv->connection_count, 1);
        return QUIC_STATUS_SUCCESS;  /* handle closed, tell msquic we handled it */
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
        sconn->state = WT_CONN_STATE_CONNECTED;

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

        sconn->session = session;
        session->parent_type = WT_PARENT_SERVER;
        session->parent.server = sconn->owner;
        wt_session_wire_callbacks(session);

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
        fire_connect(sconn);
        break;
    }

    case QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE: {
        if (sconn->session) {
            wt_session_shutdown(sconn->session);
            free(sconn->session);
            sconn->session = NULL;
        }

        MsQuic->ConnectionClose(conn);

        if (sconn->in_use) {
            fire_disconnect(sconn, 0);

            if (sconn->owner) {
                atomic_fetch_sub(&sconn->owner->connection_count, 1);
            }

            sconn->in_use = false;
            sconn->state = WT_CONN_STATE_CLOSED;
        }

        /* Signal completion LAST — after all sconn accesses.
         * free_impl spin-waits on this reaching zero before freeing. */
        if (sconn->owner)
            atomic_fetch_sub(&sconn->owner->pending_shutdowns, 1);
        break;
    }

    case QUIC_CONNECTION_EVENT_PEER_STREAM_STARTED: {
        if (sconn->session && sconn->session->stream_mgr) {
            wt_stream_manager_accept_stream(
                sconn->session->stream_mgr,
                event->PEER_STREAM_STARTED.Stream);
        } else {
            /* Session already gone — close the orphaned stream */
            MsQuic->StreamShutdown(event->PEER_STREAM_STARTED.Stream,
                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
            MsQuic->StreamClose(event->PEER_STREAM_STARTED.Stream);
        }
        break;
    }

    case QUIC_CONNECTION_EVENT_DATAGRAM_RECEIVED: {
        if (!sconn->session || !sconn->owner) break;

        {
            const QUIC_BUFFER* buf = event->DATAGRAM_RECEIVED.Buffer;
            if (buf && buf->Length > 0 &&
                buf->Length <= WT_DGRAM_MAX_SIZE) {
                wt_datagram_queue_push(
                    &sconn->owner->dgram_queue,
                    sconn->id, buf->Buffer,
                    (int32_t)buf->Length);
            }
        }
        break;
    }

    case QUIC_CONNECTION_EVENT_DATAGRAM_SEND_STATE_CHANGED:
        /* Free copy only on terminal states to avoid double-free.
         * QUIC_DATAGRAM_SEND_STATE_IS_FINAL() is true for ACK and LOST. */
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

static void on_server_dgram_drain(void* ctx, wt_connection_id_t conn_id,
                                   const uint8_t* data, int32_t length)
{
    wt_server_s* srv = (wt_server_s*)ctx;
    if (srv->callbacks.on_datagram) {
        srv->callbacks.on_datagram(srv->user_context,
                                    conn_id, data, length);
    }
}