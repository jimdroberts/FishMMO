/**
 * @file webtransport_api.cpp
 * @brief Public C API implementation — thin glue layer between
 *        the public API and internal server/client/session modules.
 */

#include "webtransport_api.h"
#include "server.h"
#include "client.h"
#include <string.h>
#include <stdio.h>

/* ── Version ────────────────────────────────────────────────── */

#define WT_VERSION_MAJOR 1
#define WT_VERSION_MINOR 0
#define WT_VERSION_PATCH 0

const char* wt_version(void)
{
    static char ver[32];
    snprintf(ver, sizeof(ver), "%d.%d.%d",
             WT_VERSION_MAJOR, WT_VERSION_MINOR, WT_VERSION_PATCH);
    return ver;
}

/* ── Error strings ──────────────────────────────────────────── */

const char* wt_error_string(int32_t code)
{
    switch (code) {
    case WT_OK:                   return "Success";
    case WT_ERR_UNKNOWN:          return "Unknown error";
    case WT_ERR_INVALID_STATE:    return "Invalid state for this operation";
    case WT_ERR_CONNECT_FAILED:   return "Connection failed";
    case WT_ERR_TLS_FAILED:       return "TLS handshake or certificate error";
    case WT_ERR_SEND_FAILED:      return "Send failed";
    case WT_ERR_BUFFER_FULL:      return "Buffer full";
    case WT_ERR_NOT_FOUND:        return "Connection ID not found";
    default:                      return "Unknown error code";
    }
}

/* ═══════════════════════════════════════════════════════════════
 * SERVER API
 * ═══════════════════════════════════════════════════════════════ */

WT_API WT_SERVER wt_server_create(
    const char*                 certificate_path,
    const char*                 private_key_path,
    const char*                 alpn,
    const char*                 bind_address,
    uint16_t                    port,
    uint32_t                    max_clients,
    const wt_server_callbacks_t* callbacks,
    void*                       context)
{
    return wt_server_alloc(certificate_path, private_key_path,
                           alpn, bind_address, port, max_clients,
                           callbacks, context);
}

WT_API void wt_server_destroy(WT_SERVER server)
{
    wt_server_free(server);
}

WT_API int32_t wt_server_start(WT_SERVER server)
{
    return wt_server_start(server);
}

WT_API void wt_server_stop(WT_SERVER server)
{
    wt_server_stop(server);
}

WT_API void wt_server_poll(WT_SERVER server, int32_t timeout_us)
{
    wt_server_poll(server, timeout_us);
}

WT_API int32_t wt_server_send_stream(
    WT_SERVER server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length)
{
    return wt_server_send_stream(server, conn_id, data, length);
}

WT_API int32_t wt_server_send_datagram(
    WT_SERVER server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length)
{
    return wt_server_send_datagram(server, conn_id, data, length);
}

WT_API void wt_server_disconnect(
    WT_SERVER server, wt_connection_id_t conn_id)
{
    wt_server_disconnect(server, conn_id);
}

WT_API const char* wt_server_get_client_address(
    WT_SERVER server, wt_connection_id_t conn_id)
{
    return wt_server_get_client_addr(server, conn_id);
}

WT_API int32_t wt_server_get_client_count(WT_SERVER server)
{
    return wt_server_get_client_count(server);
}

WT_API int32_t wt_server_get_max_clients(WT_SERVER server)
{
    if (!server) return 0;
    return (int32_t)server->max_clients;
}

WT_API int32_t wt_server_get_state(WT_SERVER server)
{
    if (!server) return WT_SERVER_STOPPED;
    return atomic_load(&server->state);
}

/* ═══════════════════════════════════════════════════════════════
 * CLIENT API
 * ═══════════════════════════════════════════════════════════════ */

WT_API WT_CLIENT wt_client_create(
    const wt_client_callbacks_t* callbacks,
    void*                       context)
{
    return wt_client_alloc(callbacks, context);
}

WT_API void wt_client_destroy(WT_CLIENT client)
{
    wt_client_free(client);
}

WT_API int32_t wt_client_connect(
    WT_CLIENT client,
    const char* server_name,
    const char* address,
    uint16_t port,
    int32_t use_tls)
{
    return wt_client_connect(client, server_name, address, port,
                             use_tls != 0);
}

WT_API void wt_client_disconnect(WT_CLIENT client)
{
    wt_client_disconnect(client);
}

WT_API void wt_client_poll(WT_CLIENT client, int32_t timeout_us)
{
    wt_client_poll(client, timeout_us);
}

WT_API int32_t wt_client_send_stream(
    WT_CLIENT client,
    const uint8_t* data, int32_t length)
{
    return wt_client_send_stream(client, data, length);
}

WT_API int32_t wt_client_send_datagram(
    WT_CLIENT client,
    const uint8_t* data, int32_t length)
{
    return wt_client_send_datagram(client, data, length);
}

WT_API int32_t wt_client_is_connected(WT_CLIENT client)
{
    return wt_client_is_connected(client) ? 1 : 0;
}

WT_API int32_t wt_client_get_mtu(WT_CLIENT client)
{
    return wt_client_get_mtu(client);
}
