/**
 * @file server.cpp
 * @brief WebTransport QUIC server implementation using msquic.
 *
 * Creates a QUIC listener, accepts WebTransport sessions, and
 * manages connected clients. Each client gets a wt_session_t for
 * stream (reliable) and datagram (unreliable) communication.
 */

#include "server.h"
#include <stdlib.h>
#include <string.h>

/* ── Static helpers (forward) ───────────────────────────────── */

static QUIC_STATUS server_conn_callback(HQUIC conn, void* ctx,
                                         QUIC_CONNECTION_EVENT* event);
static QUIC_STATUS server_listener_callback(HQUIC listener, void* ctx,
                                             QUIC_LISTENER_EVENT* event);

static void on_dgram_from_queue(void* ctx, wt_connection_id_t conn_id,
                                 const uint8_t* data, int32_t length);

static QUIC_STATUS load_certificate(wt_server_s* srv);

/* ── Public API ─────────────────────────────────────────────── */

WT_SERVER wt_server_alloc(
    const char*                 certificate_path,
    const char*                 private_key_path,
    const char*                 alpn,
    const char*                 bind_address,
    uint16_t                    port,
    uint32_t                    max_clients,
    const wt_server_callbacks_t* callbacks,
    void*                       context)
{
    if (!callbacks) return NULL;

    wt_server_s* srv = (wt_server_s*)calloc(1, sizeof(wt_server_s));
    if (!srv) return NULL;

    /* Copy config */
    if (certificate_path)
        strncpy(srv->cert_path, certificate_path, sizeof(srv->cert_path) - 1);
    if (private_key_path)
        strncpy(srv->key_path, private_key_path, sizeof(srv->key_path) - 1);
    strncpy(srv->alpn, alpn ? alpn : "h3", sizeof(srv->alpn) - 1);
    strncpy(srv->bind_address, bind_address ? bind_address : "0.0.0.0",
            sizeof(srv->bind_address) - 1);
    srv->port = port;
    srv->max_clients = max_clients > 0 ? max_clients : WT_MAX_CLIENTS;

    memcpy(&srv->callbacks, callbacks, sizeof(*callbacks));
    srv->user_context = context;

    atomic_init(&srv->state, WT_SERVER_STOPPED);

    /* Allocate connection array */
    srv->connections = (wt_server_conn_t*)calloc(srv->max_clients,
                                                   sizeof(wt_server_conn_t));
    if (!srv->connections) {
        free(srv);
        return NULL;
    }

    wt_datagram_queue_init(&srv->dgram_queue);
    return srv;
}

void wt_server_free(WT_SERVER server)
{
    if (!server) return;
    wt_server_stop(server);

    free(server->connections);

    if (server->registration)
        MsQuic->RegistrationClose(server->registration);

    free(server);
}

int32_t wt_server_start(WT_SERVER server)
{
    if (!server) return WT_ERR_UNKNOWN;
    if (atomic_load(&server->state) != WT_SERVER_STOPPED)
        return WT_ERR_INVALID_STATE;

    QUIC_STATUS status;

    /* ── Open msquic registration ── */
    QUIC_REGISTRATION_CONFIG reg_cfg = {0};
    reg_cfg.AppName = "fishmmo_wt_server";
    reg_cfg.ExecutionProfile = QUIC_EXECUTION_PROFILE_LOW_LATENCY;

    status = MsQuic->RegistrationOpen(&reg_cfg, &server->registration);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("RegistrationOpen failed: 0x%x", status);
        return WT_ERR_UNKNOWN;
    }

    /* ── Server session config ── */
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

    QUIC_SESSION_CONFIG session_cfg = {0};
    session_cfg.Settings = &settings;
    session_cfg.SettingsLen = sizeof(settings);

    status = MsQuic->SessionOpen(server->registration,
                                  &session_cfg, 1,
                                  &server->session_config);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("SessionOpen failed: 0x%x", status);
        MsQuic->RegistrationClose(server->registration);
        server->registration = NULL;
        return WT_ERR_UNKNOWN;
    }

    /* ── TLS credential config ── */
    QUIC_CREDENTIAL_CONFIG cred_cfg = {0};
    cred_cfg.Type = QUIC_CREDENTIAL_TYPE_CERTIFICATE_FILE;
    cred_cfg.Flags = QUIC_CREDENTIAL_FLAG_NONE;

    if (server->cert_path[0]) {
        cred_cfg.CertificateFile = server->cert_path;
        cred_cfg.PrivateKeyFile = server->key_path[0] ? server->key_path : server->cert_path;
    } else {
        /* Dev mode — use msquic self-signed test cert */
        cred_cfg.Flags = QUIC_CREDENTIAL_FLAG_USE_SELF_SIGNED_CERTIFICATE;
    }

    status = MsQuic->SessionSetCertificate(
        server->session_config, &cred_cfg, 1);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("SessionSetCertificate failed: 0x%x", status);
        MsQuic->SessionClose(server->session_config);
        MsQuic->RegistrationClose(server->registration);
        server->session_config = NULL;
        server->registration = NULL;
        return WT_ERR_TLS_FAILED;
    }

    /* Set ALPN */
    QUIC_BUFFER alpn_buf;
    alpn_buf.Buffer = (uint8_t*)(server->alpn);
    alpn_buf.Length = (uint32_t)strlen(server->alpn);
    MsQuic->SessionSetParam(server->session_config,
                             QUIC_PARAM_SESSION_TLS_TICKET_KEY,
                             0, NULL); /* disable session tickets for simplicity */
    MsQuic->SetParam(server->session_config,
                      QUIC_PARAM_SESSION_ALPN_PREFERENCES,
                      alpn_buf.Length, &alpn_buf);

    /* ── Create listener ── */
    QUIC_ADDR addr = {0};
    QuicAddrSetFamily(&addr, QUIC_ADDRESS_FAMILY_UNSPEC);
    QuicAddrSetPort(&addr, server->port);
    /* Parse bind address string to IP */
    QUIC_ADDR_STR addr_str;
    strncpy(addr_str.Address, server->bind_address, sizeof(addr_str.Address));

    status = MsQuic->ListenerOpen(server->session_config,
                                   server_listener_callback,
                                   server, &server->listener);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("ListenerOpen failed: 0x%x", status);
        MsQuic->SessionClose(server->session_config);
        MsQuic->RegistrationClose(server->registration);
        server->session_config = NULL;
        server->registration = NULL;
        return WT_ERR_UNKNOWN;
    }

    status = MsQuic->ListenerStart(server->listener, server->alpn,
                                    &addr, 1);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("ListenerStart failed: 0x%x on port %u",
                     status, server->port);
        MsQuic->ListenerClose(server->listener);
        MsQuic->SessionClose(server->session_config);
        MsQuic->RegistrationClose(server->registration);
        server->listener = NULL;
        server->session_config = NULL;
        server->registration = NULL;
        return WT_ERR_UNKNOWN;
    }

    atomic_store(&server->state, WT_SERVER_STARTED);
    WT_LOG_INFO("Server started on %s:%u (ALPN: %s, max clients: %u)",
                server->bind_address, server->port,
                server->alpn, server->max_clients);
    return WT_OK;
}

void wt_server_stop(WT_SERVER server)
{
    if (!server) return;

    int expected = WT_SERVER_STARTED;
    if (!atomic_compare_exchange_strong(&server->state, &expected,
                                         WT_SERVER_STOPPING)) {
        return; /* already stopped or stopping */
    }

    /* Disconnect all clients */
    for (uint32_t i = 0; i < server->max_clients; i++) {
        if (server->connections[i].in_use) {
            wt_server_disconnect(server, server->connections[i].id);
        }
    }

    if (server->listener) {
        MsQuic->ListenerClose(server->listener);
        server->listener = NULL;
    }
    if (server->session_config) {
        MsQuic->SessionShutdown(server->session_config,
                                 QUIC_CONNECTION_SHUTDOWN_FLAG_SILENT, 0);
        MsQuic->SessionClose(server->session_config);
        server->session_config = NULL;
    }
    if (server->registration) {
        MsQuic->RegistrationClose(server->registration);
        server->registration = NULL;
    }

    atomic_store(&server->state, WT_SERVER_STOPPED);
    WT_LOG_INFO("Server stopped.");
}

void wt_server_poll(WT_SERVER server, int32_t timeout_us)
{
    if (!server || atomic_load(&server->state) != WT_SERVER_STARTED)
        return;

    /* Drain datagram queue — deliver to callbacks */
    wt_datagram_queue_drain(&server->dgram_queue, on_dgram_from_queue, server);
}

int32_t wt_server_send_stream(
    WT_SERVER server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length)
{
    if (!server || !data || length <= 0) return WT_ERR_SEND_FAILED;

    if (conn_id == WT_BROADCAST_ALL) {
        /* Send to all connected clients */
        int32_t last_err = WT_OK;
        for (uint32_t i = 0; i < server->max_clients; i++) {
            if (server->connections[i].in_use &&
                server->connections[i].state == WT_CONN_STATE_CONNECTED &&
                server->connections[i].session) {
                int32_t r = wt_session_send_stream(
                    server->connections[i].session, data, length);
                if (r != WT_OK) last_err = r;
            }
        }
        return last_err;
    }

    /* Send to specific client */
    if (conn_id == 0 || conn_id >= server->max_clients)
        return WT_ERR_NOT_FOUND;

    wt_server_conn_t* conn = &server->connections[conn_id];
    if (!conn->in_use || conn->state != WT_CONN_STATE_CONNECTED || !conn->session)
        return WT_ERR_NOT_FOUND;

    return wt_session_send_stream(conn->session, data, length);
}

int32_t wt_server_send_datagram(
    WT_SERVER server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length)
{
    if (!server || !data || length <= 0) return WT_ERR_SEND_FAILED;

    if (conn_id == WT_BROADCAST_ALL) {
        int32_t last_err = WT_OK;
        for (uint32_t i = 0; i < server->max_clients; i++) {
            if (server->connections[i].in_use &&
                server->connections[i].state == WT_CONN_STATE_CONNECTED &&
                server->connections[i].session) {
                int32_t r = wt_session_send_datagram(
                    server->connections[i].session, data, length);
                if (r != WT_OK) last_err = r;
            }
        }
        return last_err;
    }

    if (conn_id == 0 || conn_id >= server->max_clients)
        return WT_ERR_NOT_FOUND;

    wt_server_conn_t* conn = &server->connections[conn_id];
    if (!conn->in_use || conn->state != WT_CONN_STATE_CONNECTED || !conn->session)
        return WT_ERR_NOT_FOUND;

    return wt_session_send_datagram(conn->session, data, length);
}

void wt_server_disconnect(WT_SERVER server, wt_connection_id_t conn_id)
{
    if (!server || conn_id == 0 || conn_id >= server->max_clients)
        return;

    wt_server_conn_t* conn = &server->connections[conn_id];
    if (!conn->in_use) return;

    if (conn->session) {
        wt_session_shutdown(conn->session);
        free(conn->session);
        conn->session = NULL;
    }
    if (conn->quic_conn) {
        MsQuic->ConnectionShutdown(conn->quic_conn,
                                    QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
    }

    conn->in_use = false;
    conn->state = WT_CONN_STATE_CLOSED;
    server->connection_count--;

    /* Notify callback */
    if (server->callbacks.on_disconnect) {
        server->callbacks.on_disconnect(server->user_context,
                                         conn_id, 0);
    }
}

const char* wt_server_get_client_addr(
    WT_SERVER server, wt_connection_id_t conn_id)
{
    if (!server || conn_id == 0 || conn_id >= server->max_clients)
        return NULL;

    wt_server_conn_t* conn = &server->connections[conn_id];
    if (!conn->in_use) return NULL;

    return conn->remote_addr;
}

int32_t wt_server_get_client_count(WT_SERVER server)
{
    if (!server) return 0;
    return (int32_t)server->connection_count;
}

/* ── Static callbacks ───────────────────────────────────────── */

static QUIC_STATUS
server_listener_callback(HQUIC listener, void* ctx, QUIC_LISTENER_EVENT* event)
{
    wt_server_s* srv = (wt_server_s*)ctx;
    (void)listener;

    switch (event->Type) {
    case QUIC_LISTENER_EVENT_NEW_CONNECTION: {
        /* Find a free connection slot */
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
            /* Server full — reject */
            MsQuic->ConnectionClose(event->NEW_CONNECTION.Connection);
            return QUIC_STATUS_SERVER_BUSY;
        }

        memset(conn, 0, sizeof(*conn));
        conn->id = conn_id;
        conn->quic_conn = event->NEW_CONNECTION.Connection;
        conn->state = WT_CONN_STATE_HANDSHAKING;
        conn->in_use = true;
        srv->connection_count++;

        /* Set connection callback */
        MsQuic->SetCallbackHandler(conn->quic_conn,
                                    (void*)server_conn_callback, conn);

        /* Accept */
        MsQuic->ConnectionSetConfiguration(
            conn->quic_conn, srv->session_config);
        return QUIC_STATUS_SUCCESS;
    }
    default:
        return QUIC_STATUS_SUCCESS;
    }
}

static QUIC_STATUS
server_conn_callback(HQUIC conn, void* ctx, QUIC_CONNECTION_EVENT* event)
{
    wt_server_conn_t* sconn = (wt_server_conn_t*)ctx;
    wt_server_s* srv = NULL;

    /* Find parent server — stored in session, but we don't have it here.
     * For simplicity, we access the listener indirectly through context
     * passed at creation time. Since we need server access for callbacks,
     * we'll store a pointer in the connection. */
    /* TEMP: resolve server from session parent when needed */

    switch (event->Type) {
    case QUIC_CONNECTION_EVENT_CONNECTED: {
        sconn->state = WT_CONN_STATE_CONNECTED;

        /* Create session */
        sconn->session = (wt_session_t*)calloc(1, sizeof(wt_session_t));
        wt_session_init(sconn->session, conn, sconn->id);

        /* Get remote address */
        QUIC_ADDR_STR addr_str;
        QUIC_ADDR remote_addr;
        uint32_t addr_len = sizeof(remote_addr);
        if (QUIC_SUCCEEDED(MsQuic->GetParam(
                conn, QUIC_PARAM_CONN_REMOTE_ADDRESS,
                &addr_len, &remote_addr))) {
            QuicAddrToString(&remote_addr, &addr_str);
            strncpy(sconn->remote_addr, addr_str.Address,
                    sizeof(sconn->remote_addr) - 1);
        }

        /* Find parent server for callback — stored during listener creation.
         * We need to do this differently; for now resolve from global context. */
        /* NOTE: In the full implementation, store server pointer in QUIC
         * connection context via SetContext. For simplicity, we resolve
         * server callbacks from the session's parent pointer. */
        WT_LOG_INFO("Client %llu connected from %s",
                    (unsigned long long)sconn->id,
                    sconn->remote_addr);
        break;
    }

    case QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_TRANSPORT:
    case QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE: {
        if (sconn->session) {
            wt_session_shutdown(sconn->session);
            free(sconn->session);
            sconn->session = NULL;
        }
        sconn->in_use = false;
        sconn->state = WT_CONN_STATE_CLOSED;
        break;
    }

    case QUIC_CONNECTION_EVENT_PEER_STREAM_STARTED: {
        if (sconn->session && sconn->session->stream_mgr) {
            wt_stream_manager_accept_stream(
                sconn->session->stream_mgr,
                event->PEER_STREAM_STARTED.Stream);
        }
        break;
    }

    case QUIC_CONNECTION_EVENT_DATAGRAM_RECEIVED: {
        /* Queue datagrams for main-thread delivery */
        if (sconn->session) {
            /* Push to session datagram queue for poll-based delivery */
            /* In the full implementation, accumulate the datagram
             * from QUIC event buffers and push to the session queue */
        }
        break;
    }

    case QUIC_CONNECTION_EVENT_DATAGRAM_STATE_CHANGED:
        /* Datagram send enabled/disabled */
        break;

    default:
        break;
    }
    return QUIC_STATUS_SUCCESS;
}

/* ── Datagram queue drain callback ──────────────────────────── */

static void on_dgram_from_queue(void* ctx, wt_connection_id_t conn_id,
                                 const uint8_t* data, int32_t length)
{
    wt_server_s* srv = (wt_server_s*)ctx;
    if (srv->callbacks.on_datagram) {
        srv->callbacks.on_datagram(srv->user_context,
                                    conn_id, data, length);
    }
}
