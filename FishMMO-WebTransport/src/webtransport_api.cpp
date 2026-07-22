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
    /* ── MEDIUM: Caller thread-safety requirement ─────────────────
     *
     * IMPORTANT: Callers MUST ensure all API calls (wt_server_*,
     * wt_client_*, wt_server_poll, wt_client_poll, etc.) have
     * COMPLETED before calling wt_deinit(). Threads mid-call may
     * still hold references to the MsQuic API table (MsQuic global
     * pointer). Closing MsQuic beneath them is a use-after-free.
     *
     * Safe pattern:
     *   1. Stop all servers and clients (wt_server_stop, wt_client_disconnect)
     *   2. Join/drain all application threads that call the API
     *   3. Destroy all server/client handles (wt_server_destroy, wt_client_destroy)
     *   4. Call wt_deinit() — only after step 3 has completed.
     *
     * Do NOT call wt_deinit() from a QUIC callback or while any
     * wt_server_poll / wt_client_poll call is in progress on another
     * thread.
     *
     * Single-entry gate: use atomic CAS to ensure only ONE thread
     * proceeds into deinit.  The first call that CAS's g_initialised
     * from true to false wins; all subsequent calls (concurrent or
     * after deinit completes) see false and return immediately.
     *
     * In the winning thread we transition g_initialised to false
     * atomically as part of the gate — new API callers that check
     * the guard AFTER the CAS will bail out early.
     *
     * ACCEPTED LIMITATION: Threads that entered an API function
     * *before* this CAS may still be in-flight with a reference to
     * the MsQuic API table.  Closing MsQuic beneath them is unsafe.
     * The library is designed for controlled shutdown: call
     * wt_deinit() only after all worker threads have been joined
     * and no API invocations remain in-flight. */
    {
        atomic_bool expected = 1;
        if (!atomic_compare_exchange_strong(&g_initialised, &expected, 0))
            return;
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
    uint32_t max_clients, const wt_server_callbacks_t* callbacks,
    void* context)
{
    if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return NULL; }
    return (WT_SERVER)wt_server_alloc_impl(
        certificate_path, private_key_path, alpn, bind_address,
        port, max_clients, callbacks, context);
}

WT_API void wt_server_destroy(WT_SERVER server)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return; }
    wt_server_free_impl((wt_server_s*)server);
}

WT_API int32_t wt_server_start(WT_SERVER server)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    return wt_server_start_impl((wt_server_s*)server);
}

WT_API void wt_server_stop(WT_SERVER server)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return; }
    wt_server_stop_impl((wt_server_s*)server);
}

WT_API void wt_server_poll(WT_SERVER server, int32_t timeout_us)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return; }
    wt_server_poll_impl((wt_server_s*)server, timeout_us);
}

WT_API int32_t wt_server_send_stream(
    WT_SERVER server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    return wt_server_send_stream_impl(
        (wt_server_s*)server, conn_id, data, length);
}

WT_API int32_t wt_server_send_datagram(
    WT_SERVER server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    return wt_server_send_datagram_impl(
        (wt_server_s*)server, conn_id, data, length);
}

WT_API void wt_server_disconnect(
    WT_SERVER server, wt_connection_id_t conn_id)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return; }
    wt_server_disconnect_impl((wt_server_s*)server, conn_id);
}

WT_API const char* wt_server_get_client_address(
    WT_SERVER server, wt_connection_id_t conn_id)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return NULL; }
    return wt_server_get_client_addr_impl(
        (wt_server_s*)server, conn_id);
}

WT_API int32_t wt_server_get_client_count(WT_SERVER server)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    if (!server) return 0;
    return wt_server_get_client_count_impl((wt_server_s*)server);
}

WT_API int32_t wt_server_get_max_clients(WT_SERVER server)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    if (!server) return 0;
    return (int32_t)((wt_server_s*)server)->max_clients;
}

WT_API int32_t wt_server_get_state(WT_SERVER server)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    if (!server) return 0;
    return atomic_load(&((wt_server_s*)server)->state);
}

/* ═══════════════════════════════════════════════════════════════
 * CLIENT API  (delegates to client.h _impl functions)
 * ═══════════════════════════════════════════════════════════════ */

WT_API WT_CLIENT wt_client_create(
    const wt_client_callbacks_t* callbacks, void* context)
{
    if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return NULL; }
    return (WT_CLIENT)wt_client_alloc_impl(callbacks, context);
}

WT_API void wt_client_destroy(WT_CLIENT client)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return; }
    wt_client_free_impl((wt_client_s*)client);
}

WT_API int32_t wt_client_connect(
    WT_CLIENT client, const char* server_name,
    const char* address, uint16_t port, int32_t use_tls)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    return wt_client_connect_impl(
        (wt_client_s*)client, server_name, address,
        port, (use_tls != 0));
}

WT_API void wt_client_disconnect(WT_CLIENT client)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return; }
    wt_client_disconnect_impl((wt_client_s*)client);
}

WT_API void wt_client_poll(WT_CLIENT client, int32_t timeout_us)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return; }
    wt_client_poll_impl((wt_client_s*)client, timeout_us);
}

WT_API int32_t wt_client_send_stream(
    WT_CLIENT client, const uint8_t* data, int32_t length)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    return wt_client_send_stream_impl(
        (wt_client_s*)client, data, length);
}

WT_API int32_t wt_client_send_datagram(
    WT_CLIENT client, const uint8_t* data, int32_t length)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    return wt_client_send_datagram_impl(
        (wt_client_s*)client, data, length);
}

WT_API int32_t wt_client_is_connected(WT_CLIENT client)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    return wt_client_is_connected_impl((wt_client_s*)client) ? 1 : 0;
}

WT_API int32_t wt_client_get_mtu(WT_CLIENT client)
{
	if (!atomic_load(&g_initialised)) { WT_LOG_ERROR("wt_init() not called"); return -1; }
    return wt_client_get_mtu_impl((wt_client_s*)client);
}