/**
 * @file client.cpp
 * @brief WebTransport QUIC client implementation using msquic.
 *
 * Connects to a WebTransport server, manages a single session,
 * and provides stream (reliable) + datagram (unreliable) channels.
 */

#include "client.h"
#include <stdlib.h>
#include <string.h>

/* ── Static helpers ─────────────────────────────────────────── */

static QUIC_STATUS client_conn_callback(HQUIC conn, void* ctx,
                                         QUIC_CONNECTION_EVENT* event);

static void on_client_dgram_cb(void* ctx, wt_connection_id_t conn_id,
                                const uint8_t* data, int32_t length);

/* ── Public API ─────────────────────────────────────────────── */

WT_CLIENT wt_client_alloc(
    const wt_client_callbacks_t* callbacks,
    void*                       context)
{
    if (!callbacks) return NULL;

    wt_client_s* cli = (wt_client_s*)calloc(1, sizeof(wt_client_s));
    if (!cli) return NULL;

    memcpy(&cli->callbacks, callbacks, sizeof(*callbacks));
    cli->user_context = context;
    atomic_init(&cli->state, WT_CLIENT_STOPPED);
    atomic_init(&cli->connected, false);

    wt_datagram_queue_init(&cli->dgram_queue);
    return cli;
}

void wt_client_free(WT_CLIENT client)
{
    if (!client) return;
    wt_client_disconnect(client);

    if (client->registration)
        MsQuic->RegistrationClose(client->registration);

    free(client);
}

int32_t wt_client_connect(
    WT_CLIENT client,
    const char* server_name,
    const char* address,
    uint16_t port,
    bool use_tls)
{
    if (!client || !server_name || !address)
        return WT_ERR_UNKNOWN;

    if (atomic_load(&client->state) != WT_CLIENT_STOPPED)
        return WT_ERR_INVALID_STATE;

    /* Store config */
    strncpy(client->server_name, server_name, sizeof(client->server_name) - 1);
    strncpy(client->address, address, sizeof(client->address) - 1);
    client->port = port;
    client->use_tls = use_tls;

    QUIC_STATUS status;

    /* ── Open registration ── */
    QUIC_REGISTRATION_CONFIG reg_cfg = {0};
    reg_cfg.AppName = "fishmmo_wt_client";
    reg_cfg.ExecutionProfile = QUIC_EXECUTION_PROFILE_LOW_LATENCY;

    status = MsQuic->RegistrationOpen(&reg_cfg, &client->registration);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("RegistrationOpen failed: 0x%x", status);
        return WT_ERR_UNKNOWN;
    }

    /* ── Create QUIC connection ── */
    status = MsQuic->ConnectionOpen(client->registration,
                                     client_conn_callback,
                                     client, &client->quic_conn);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("ConnectionOpen failed: 0x%x", status);
        MsQuic->RegistrationClose(client->registration);
        client->registration = NULL;
        return WT_ERR_UNKNOWN;
    }

    /* ── Configure connection settings ── */
    QUIC_SETTINGS settings = {0};
    settings.IdleTimeoutMs = WT_DEFAULT_IDLE_TIMEOUT_MS;
    settings.IsSet.IdleTimeoutMs = TRUE;
    settings.DatagramReceiveEnabled = TRUE;
    settings.IsSet.DatagramReceiveEnabled = TRUE;
    settings.PeerBidiStreamCount = WT_MAX_STREAMS;
    settings.IsSet.PeerBidiStreamCount = TRUE;
    settings.PeerUnidiStreamCount = WT_MAX_STREAMS;
    settings.IsSet.PeerUnidiStreamCount = TRUE;

    status = MsQuic->SetParam(client->quic_conn,
                               QUIC_PARAM_CONN_SETTINGS,
                               sizeof(settings), &settings);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("SetParam(SETTINGS) failed: 0x%x", status);
        MsQuic->ConnectionClose(client->quic_conn);
        MsQuic->RegistrationClose(client->registration);
        client->quic_conn = NULL;
        client->registration = NULL;
        return WT_ERR_UNKNOWN;
    }

    /* ── Set remote address ── */
    /* Resolve hostname */
    QUIC_ADDR remote_addr = {0};
    QuicAddrSetFamily(&remote_addr, QUIC_ADDRESS_FAMILY_UNSPEC);
    QuicAddrSetPort(&remote_addr, port);

    /* Simple DNS resolution via getaddrinfo */
    status = MsQuic->SetParam(client->quic_conn,
                               QUIC_PARAM_CONN_REMOTE_ADDRESS,
                               sizeof(remote_addr), &remote_addr);

    /* ── Set TLS SNI ── */
    QUIC_BUFFER sni_buf;
    sni_buf.Buffer = (uint8_t*)(server_name);
    sni_buf.Length = (uint32_t)strlen(server_name);
    MsQuic->SetParam(client->quic_conn,
                      QUIC_PARAM_CONN_TLS_SERVER_NAME,
                      sni_buf.Length, &sni_buf);

    /* ── Start connection ── */
    atomic_store(&client->state, WT_CLIENT_STARTING);
    status = MsQuic->ConnectionStart(client->quic_conn,
                                      client->session_config,
                                      QUIC_ADDRESS_FAMILY_UNSPEC,
                                      client->address, client->port);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("ConnectionStart failed: 0x%x", status);
        MsQuic->ConnectionClose(client->quic_conn);
        MsQuic->RegistrationClose(client->registration);
        client->quic_conn = NULL;
        client->registration = NULL;
        atomic_store(&client->state, WT_CLIENT_STOPPED);
        return WT_ERR_CONNECT_FAILED;
    }

    WT_LOG_INFO("Connecting to %s:%u (SNI: %s)...",
                address, port, server_name);
    return WT_OK;
}

void wt_client_disconnect(WT_CLIENT client)
{
    if (!client) return;

    int expected = WT_CLIENT_STARTED;
    atomic_compare_exchange_strong(&client->state, &expected,
                                    WT_CLIENT_STOPPING);

    if (client->session) {
        wt_session_shutdown(client->session);
        free(client->session);
        client->session = NULL;
    }
    if (client->quic_conn) {
        MsQuic->ConnectionShutdown(client->quic_conn,
                                    QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
        MsQuic->ConnectionClose(client->quic_conn);
        client->quic_conn = NULL;
    }

    atomic_store(&client->connected, false);
    atomic_store(&client->state, WT_CLIENT_STOPPED);
}

void wt_client_poll(WT_CLIENT client, int32_t timeout_us)
{
    if (!client) return;

    /* Drain datagram queue to callbacks */
    wt_datagram_queue_drain(&client->dgram_queue,
                             on_client_dgram_cb, client);
}

int32_t wt_client_send_stream(
    WT_CLIENT client,
    const uint8_t* data, int32_t length)
{
    if (!client || !client->session || !data || length <= 0)
        return WT_ERR_SEND_FAILED;
    if (!atomic_load(&client->connected))
        return WT_ERR_INVALID_STATE;

    return wt_session_send_stream(client->session, data, length);
}

int32_t wt_client_send_datagram(
    WT_CLIENT client,
    const uint8_t* data, int32_t length)
{
    if (!client || !client->session || !data || length <= 0)
        return WT_ERR_SEND_FAILED;
    if (!atomic_load(&client->connected))
        return WT_ERR_INVALID_STATE;

    return wt_session_send_datagram(client->session, data, length);
}

bool wt_client_is_connected(WT_CLIENT client)
{
    if (!client) return false;
    return atomic_load(&client->connected);
}

int32_t wt_client_get_mtu(WT_CLIENT client)
{
    if (!client) return WT_DEFAULT_MTU;
    return WT_DEFAULT_MTU; /* QUIC path MTU — hardcoded, could query msquic */
}

/* ── Static callbacks ───────────────────────────────────────── */

static QUIC_STATUS
client_conn_callback(HQUIC conn, void* ctx, QUIC_CONNECTION_EVENT* event)
{
    wt_client_s* cli = (wt_client_s*)ctx;

    switch (event->Type) {
    case QUIC_CONNECTION_EVENT_CONNECTED: {
        WT_LOG_INFO("Client connected!");

        /* Create session */
        cli->session = (wt_session_t*)calloc(1, sizeof(wt_session_t));
        wt_session_init(cli->session, conn, WT_CONNECTION_ID_NONE);
        cli->session->parent_type = WT_PARENT_CLIENT;
        cli->session->parent.client = cli;

        atomic_store(&cli->connected, true);
        atomic_store(&cli->state, WT_CLIENT_STARTED);

        if (cli->callbacks.on_connect) {
            cli->callbacks.on_connect(cli->user_context);
        }
        break;
    }

    case QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE: {
        WT_LOG_INFO("Client disconnected.");
        atomic_store(&cli->connected, false);

        if (cli->session) {
            wt_session_shutdown(cli->session);
            free(cli->session);
            cli->session = NULL;
        }

        if (cli->callbacks.on_disconnect) {
            cli->callbacks.on_disconnect(cli->user_context, 0);
        }
        break;
    }

    case QUIC_CONNECTION_EVENT_PEER_STREAM_STARTED: {
        if (cli->session && cli->session->stream_mgr) {
            wt_stream_manager_accept_stream(
                cli->session->stream_mgr,
                event->PEER_STREAM_STARTED.Stream);
        }
        break;
    }

    case QUIC_CONNECTION_EVENT_DATAGRAM_RECEIVED: {
        /* Queue for main-thread delivery during poll */
        /* In the full implementation, accumulate from QUIC event
         * and push to the session datagram queue */
        if (cli->session) {
            /* Extract datagram data and push */
        }
        break;
    }

    default:
        break;
    }

    return QUIC_STATUS_SUCCESS;
}

/* ── Datagram queue drain callback ──────────────────────────── */

static void on_client_dgram_cb(void* ctx, wt_connection_id_t conn_id,
                                const uint8_t* data, int32_t length)
{
    wt_client_s* cli = (wt_client_s*)ctx;
    (void)conn_id; /* unused for client */

    if (cli->callbacks.on_datagram) {
        cli->callbacks.on_datagram(cli->user_context, data, length);
    }
}
