/**
 * @file webtransport_api.cpp
 * @brief Public C API — thin glue delegating to _impl functions.
 */

#include "webtransport_api.h"
#include "server.h"
#include "client.h"
#include "webtransport_internal.h"  /* atomic macros, WT_LOG_*, platform abstractions */

#include <stdio.h>
#include <stdbool.h>

/* ── Global MsQuic API table ──────────────────────────────────
 * Initialised once by wt_init(). All msquic operations use this
 * global pointer. Without this, every MsQuic-> call is NULL.
 *
 * Defined here (not just extern-declared in the header) because
 * Windows PE/COFF requires an explicit definition; ELF common
 * symbols are not portable. */
const QUIC_API_TABLE* MsQuic = NULL;

static atomic_bool g_initialised = false;
atomic_int g_api_call_refcount = 0;

/* ── Lifecycle ──────────────────────────────────────────────── */

WT_API int32_t wt_init(void)
{
    /* Fast path — check the once-flag before opening MsQuic. */
    if (atomic_load(&g_initialised)) return WT_OK;

    /* Open the MsQuic API table before the CAS gate so that whichever
     * thread wins the race already has a valid pointer ready. */
    const QUIC_API_TABLE* api = NULL;
    QUIC_STATUS status = MsQuicOpen2(&api);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("MsQuicOpen2 failed: 0x%x", status);
        return WT_ERR_UNKNOWN;
    }

    /* CAS-based once guard: only the first thread that CAS's
     * g_initialised from false to true proceeds to set MsQuic and log.
     * All other threads that passed the initial load check will hit the
     * CAS, see that another thread already set the flag, discard their
     * copy of the API table, and return. */
    atomic_bool expected = false;
    if (atomic_compare_exchange_strong(&g_initialised, &expected, true)) {
        atomic_ptr_store(&MsQuic, api);
        /* Release-store: paired with acquire-load in API entry guards.
         * Ensures MsQuic is visible before g_initialised. */
        WT_LOG_INFO("WebTransport library initialised (msquic %s)", wt_version());
        return WT_OK;
    }

    /* Another thread beat us to the initialisation — discard our copy. */
    MsQuicClose(api);
    return WT_OK;
}

WT_API void wt_deinit(void)
{
    {
        atomic_bool expected = 1;
        if (!atomic_compare_exchange_strong(&g_initialised, &expected, 0))
            return;
    }

    /* Wait for all in-flight API calls to complete (bounded spin-wait). */
    int patience = 600; /* 600 * 10ms = 6 seconds max */
    while (atomic_load(&g_api_call_refcount) > 0 && patience-- > 0) {
#if defined(WT_PLATFORM_WINDOWS)
        Sleep(10);
#else
        struct timespec ts = {0, 10000000};
        nanosleep(&ts, NULL);
#endif
    }
    if (atomic_load(&g_api_call_refcount) > 0) {
        WT_LOG_ERROR("wt_deinit: timed out waiting for %d in-flight API calls -- forcing close (may crash)",
                     atomic_load(&g_api_call_refcount));
    }

    if (MsQuic) {
        MsQuicClose(MsQuic);
    }
    atomic_ptr_store(&MsQuic, (const QUIC_API_TABLE*)NULL);
}

/* ── Version ────────────────────────────────────────────────── */

#define WT_VERSION_MAJOR 1
#define WT_VERSION_MINOR 0
#define WT_VERSION_PATCH 0

/**
 * Returns the library version string.
 * Used by wt_init() for startup logging and can be called at any time
 * after wt_init() completes (before wt_init() the global MsQuic API table
 * is not yet populated, but the version string itself is a static literal
 * and does not depend on MsQuic state).
 *
 * @return Static string literal "1.0.0" (semver).
 */
const char* wt_version(void)
{
    return "1.0.0";
}

/* ── Error strings ──────────────────────────────────────────── */

const char* wt_error_string(int32_t code)
{
    switch (code) {
    case WT_OK:                   return "Success";
    case WT_ERR_UNKNOWN:          return "Unknown error";
    case WT_ERR_INVALID_STATE:    return "Invalid state";
    case WT_ERR_CONNECT_FAILED:   return "Connection failed";
    case WT_ERR_TLS_FAILED:       return "TLS/certificate error";
    case WT_ERR_SEND_FAILED:      return "Send failed";
    case WT_ERR_BUFFER_FULL:      return "Buffer full";
    case WT_ERR_NOT_FOUND:        return "Connection ID not found";
    default:                      return "Unknown error code";
    }
}

/* ═══════════════════════════════════════════════════════════════
 * SERVER API  (delegates to server.h _impl functions)
 * ═══════════════════════════════════════════════════════════════ */

WT_API WT_SERVER wt_server_create(
    const char* certificate_path, const char* private_key_path,
    const char* alpn, const char* bind_address, uint16_t port,
    uint32_t max_clients, const char* allowed_origins,
    const wt_server_callbacks_t* callbacks, void* context)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return NULL; }
    WT_SERVER result = (WT_SERVER)wt_server_alloc_impl(
        certificate_path, private_key_path, alpn, bind_address,
        port, max_clients, allowed_origins, callbacks, context);
    wt_api_exit();
    return result;
}

WT_API void wt_server_destroy(WT_SERVER server)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return; }
    wt_server_free_impl((wt_server_s*)server);
    wt_api_exit();
}

WT_API int32_t wt_server_start(WT_SERVER server)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    int32_t result = wt_server_start_impl((wt_server_s*)server);
    wt_api_exit();
    return result;
}

WT_API void wt_server_stop(WT_SERVER server)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return; }
    wt_server_stop_impl((wt_server_s*)server);
    wt_api_exit();
}

WT_API void wt_server_poll(WT_SERVER server, int32_t timeout_us)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return; }
    wt_server_poll_impl((wt_server_s*)server, timeout_us);
    wt_api_exit();
}

WT_API int32_t wt_server_send_stream(
    WT_SERVER server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    int32_t result = wt_server_send_stream_impl(
        (wt_server_s*)server, conn_id, data, length);
    wt_api_exit();
    return result;
}

WT_API int32_t wt_server_send_datagram(
    WT_SERVER server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    int32_t result = wt_server_send_datagram_impl(
        (wt_server_s*)server, conn_id, data, length);
    wt_api_exit();
    return result;
}

WT_API void wt_server_disconnect(
    WT_SERVER server, wt_connection_id_t conn_id)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return; }
    wt_server_disconnect_impl((wt_server_s*)server, conn_id);
    wt_api_exit();
}

WT_API const char* wt_server_get_client_address(
    WT_SERVER server, wt_connection_id_t conn_id)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return NULL; }
    const char* result = wt_server_get_client_addr_impl(
        (wt_server_s*)server, conn_id);
    wt_api_exit();
    return result;
}

WT_API int32_t wt_server_get_client_count(WT_SERVER server)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    if (!server) { wt_api_exit(); return 0; }
    int32_t result = wt_server_get_client_count_impl((wt_server_s*)server);
    wt_api_exit();
    return result;
}

WT_API int32_t wt_server_get_max_clients(WT_SERVER server)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    if (!server) { wt_api_exit(); return 0; }
    int32_t result = (int32_t)((wt_server_s*)server)->max_clients;
    wt_api_exit();
    return result;
}

WT_API int32_t wt_server_get_state(WT_SERVER server)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    if (!server) { wt_api_exit(); return 0; }
    int32_t result = atomic_load(&((wt_server_s*)server)->state);
    wt_api_exit();
    return result;
}

/* ═══════════════════════════════════════════════════════════════
 * CLIENT API  (delegates to client.h _impl functions)
 * ═══════════════════════════════════════════════════════════════ */

WT_API WT_CLIENT wt_client_create(
    const wt_client_callbacks_t* callbacks, void* context)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return NULL; }
    WT_CLIENT result = (WT_CLIENT)wt_client_alloc_impl(callbacks, context);
    wt_api_exit();
    return result;
}

WT_API void wt_client_destroy(WT_CLIENT client)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return; }
    wt_client_free_impl((wt_client_s*)client);
    wt_api_exit();
}

WT_API int32_t wt_client_connect(
    WT_CLIENT client, const char* server_name,
    const char* address, uint16_t port, int32_t use_tls)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    int32_t result = wt_client_connect_impl(
        (wt_client_s*)client, server_name, address,
        port, (use_tls != 0));
    wt_api_exit();
    return result;
}

WT_API void wt_client_disconnect(WT_CLIENT client)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return; }
    wt_client_disconnect_impl((wt_client_s*)client);
    wt_api_exit();
}

WT_API void wt_client_poll(WT_CLIENT client, int32_t timeout_us)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return; }
    wt_client_poll_impl((wt_client_s*)client, timeout_us);
    wt_api_exit();
}

WT_API int32_t wt_client_send_stream(
    WT_CLIENT client, const uint8_t* data, int32_t length)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    int32_t result = wt_client_send_stream_impl(
        (wt_client_s*)client, data, length);
    wt_api_exit();
    return result;
}

WT_API int32_t wt_client_send_datagram(
    WT_CLIENT client, const uint8_t* data, int32_t length)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    int32_t result = wt_client_send_datagram_impl(
        (wt_client_s*)client, data, length);
    wt_api_exit();
    return result;
}

WT_API int32_t wt_client_is_connected(WT_CLIENT client)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    int32_t result = wt_client_is_connected_impl((wt_client_s*)client) ? 1 : 0;
    wt_api_exit();
    return result;
}

WT_API int32_t wt_client_get_mtu(WT_CLIENT client)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    int32_t result = wt_client_get_mtu_impl((wt_client_s*)client);
    wt_api_exit();
    return result;
}

WT_API void wt_client_set_alpn(WT_CLIENT client, const char* alpn)
{
    if (!wt_api_enter()) { WT_LOG_ERROR("wt_init() not called"); return; }
    if (client && alpn) {
        strncpy(((wt_client_s*)client)->alpn, alpn, WT_MAX_ALPN_LENGTH - 1);
        ((wt_client_s*)client)->alpn[WT_MAX_ALPN_LENGTH - 1] = '\0';
    }
    wt_api_exit();
}