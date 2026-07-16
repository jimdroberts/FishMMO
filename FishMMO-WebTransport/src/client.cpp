/**
 * @file client.cpp
 * @brief WebTransport QUIC client implementation using msquic.
 */

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

    /* Snapshot state BEFORE disconnect_impl */
    int prev_state = atomic_load(&client->state);
    bool was_connected = (prev_state == WT_CLIENT_STARTED);

    wt_client_disconnect_impl(client);

    if (was_connected) {
        /* Spin-wait for SHUTDOWN_COMPLETE to close handles (bounded).
         * The callback closes Connection, Config, Registration, and
         * destroys the dgram_queue, then signals pending_shutdowns=0.
         * We do the final free(cli) here — never in the callback. */
        int retries = 300;
        while (atomic_load(&client->pending_shutdowns) > 0 && retries-- > 0) {
#if defined(WT_PLATFORM_WINDOWS)
            Sleep(10);
#else
            usleep(10000);
#endif
        }
        if (retries < 0)
            WT_LOG_WARN("Client shutdown timed out, forcing free");
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
    strncpy(client->address, address, sizeof(client->address) - 1);
    client->address[sizeof(client->address) - 1] = '\0';
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

    /* ── TLS credentials (client: no cert needed) ── */
    QUIC_CREDENTIAL_CONFIG cred_cfg;
    memset(&cred_cfg, 0, sizeof(cred_cfg));
    cred_cfg.Type = QUIC_CREDENTIAL_TYPE_NONE;
    cred_cfg.Flags = QUIC_CREDENTIAL_FLAG_CLIENT;

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

    /* ── Start connection (SNI passed as ServerName parameter) ── */
    atomic_store(&client->state, WT_CLIENT_STARTING);
    status = MsQuic->ConnectionStart(client->quic_conn,
                                      client->session_config,
                                      QUIC_ADDRESS_FAMILY_UNSPEC,
                                      server_name, port);
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
    wt_datagram_queue_drain(&client->dgram_queue,
                             on_client_dgram_drain, client);
}

int32_t wt_client_send_stream_impl(
    wt_client_s* client, const uint8_t* data, int32_t length)
{
    if (!client || !client->session || !data || length <= 0)
        return WT_ERR_SEND_FAILED;
    if (!atomic_load(&client->connected))
        return WT_ERR_INVALID_STATE;

    return wt_session_send_stream(client->session, data, length);
}

int32_t wt_client_send_datagram_impl(
    wt_client_s* client, const uint8_t* data, int32_t length)
{
    if (!client || !client->session || !data || length <= 0)
        return WT_ERR_SEND_FAILED;
    if (!atomic_load(&client->connected))
        return WT_ERR_INVALID_STATE;

    return wt_session_send_datagram(client->session, data, length);
}

bool wt_client_is_connected_impl(wt_client_s* client)
{
    if (!client) return false;
    return atomic_load(&client->connected);
}

int32_t wt_client_get_mtu_impl(wt_client_s* client)
{
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

        wt_session_t* session = (wt_session_t*)calloc(1, sizeof(wt_session_t));
        if (!session) {
            MsQuic->ConnectionShutdown(conn,
                QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
            break;
        }

        int32_t r = wt_session_init(session, conn, WT_CONNECTION_ID_NONE);
        if (r != WT_OK) {
            WT_LOG_ERROR("Session init failed");
            free(session);
            MsQuic->ConnectionShutdown(conn,
                QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
            break;
        }

        cli->session = session;
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

        if (cli->session) {
            wt_session_shutdown(cli->session);
            free(cli->session);
            cli->session = NULL;
        }

        MsQuic->ConnectionClose(conn);
        cli->quic_conn = NULL;

        if (cli->session_config) {
            MsQuic->ConfigurationClose(cli->session_config);
            cli->session_config = NULL;
        }

        if (cli->registration) {
            MsQuic->RegistrationClose(cli->registration);
            cli->registration = NULL;
        }

        /* Fire disconnect before freeing the struct */
        if (cli->callbacks.on_disconnect)
            cli->callbacks.on_disconnect(cli->user_context, 0);

        /* Signal completion — free_impl will do the final free(cli)
         * after its spin-wait sees pending_shutdowns reach 0. This
         * prevents UAF on the pending_shutdowns read in the spin loop. */
        atomic_fetch_sub(&cli->pending_shutdowns, 1);
        break;
    }

    case QUIC_CONNECTION_EVENT_PEER_STREAM_STARTED: {
        if (cli->session && cli->session->stream_mgr) {
            wt_stream_manager_accept_stream(
                cli->session->stream_mgr,
                event->PEER_STREAM_STARTED.Stream);
        } else {
            MsQuic->StreamShutdown(event->PEER_STREAM_STARTED.Stream,
                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
            MsQuic->StreamClose(event->PEER_STREAM_STARTED.Stream);
        }
        break;
    }

    case QUIC_CONNECTION_EVENT_DATAGRAM_RECEIVED: {
        {
            const QUIC_BUFFER* buf = event->DATAGRAM_RECEIVED.Buffer;
            if (buf && buf->Length > 0 &&
                buf->Length <= WT_DGRAM_MAX_SIZE) {
                wt_datagram_queue_push(
                    &cli->dgram_queue, 0, buf->Buffer,
                    (int32_t)buf->Length);
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