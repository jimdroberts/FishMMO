/**
 * @file client.h
 * @brief WebTransport QUIC client — internal declarations.
 *
 * All functions use _impl suffix. The public C API in
 * webtransport_api.h delegates to these.
 */

#ifndef WEBTRANSPORT_CLIENT_H
#define WEBTRANSPORT_CLIENT_H

#include "webtransport_internal.h"
#include "session.h"
#include "datagram_queue.h"
#include "http3.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct wt_client_s {
    HQUIC                   registration;
    HQUIC                   session_config;
    HQUIC                   quic_conn;

    wt_client_callbacks_t   callbacks;
    void*                   user_context;

    char                    server_name[256];
    char                    address[256];
    uint16_t                port;
    bool                    use_tls;
    char                    alpn[WT_MAX_ALPN_LENGTH];  /* ALPN string (default "h3") */

    atomic_int              state;
    atomic_bool             connected;
    atomic_uint             pending_shutdowns;

    /* WARNING: session is declared as a plain pointer but MUST be accessed
     * only through atomic_ptr_load/atomic_ptr_store.  See the same pattern
     * and rationale in server.h.  Direct assignment or read of this field
     * bypasses atomic ordering guarantees and will silently produce torn
     * or non-ordered accesses on ARM/POWER. */
    wt_session_t*           session;  // Raw pointer typed for C ABI compatibility. Access ONLY via atomic_ptr_load/atomic_ptr_store.

    /* Session pending deferred shutdown. Set by QUIC callback thread
     * (SHUTDOWN_COMPLETE), consumed by poll (application thread) to
     * ensure session free never races with concurrent sends.
     * MUST be accessed via atomic_ptr_load/atomic_ptr_store — written
     * on the QUIC callback thread, read/written on the poll thread
     * without a lock. */
    wt_session_t*           pending_shutdown_session;  // Raw pointer typed for C ABI compatibility. Access ONLY via atomic_ptr_load/atomic_ptr_store.

    /* HTTP/3 handshake session (optional — only used when connecting
     * to standard WebTransport servers). NULL for native raw-QUIC mode. */
    h3_session_t*           h3_session;

    wt_datagram_queue_t     dgram_queue;

    /* Per-connection datagram drop counter.  Reset to 0 by calloc at
     * client creation.  Used to rate-limit queue-full warnings without
     * a global counter that bleeds across connections. */
    atomic_int              dgram_drop_count;
} wt_client_s;

/* ── Internal API ──────────────────────────────────────────── */

wt_client_s* wt_client_alloc_impl(
    const wt_client_callbacks_t* callbacks, void* context);
void wt_client_free_impl(wt_client_s* client);

int32_t wt_client_connect_impl(
    wt_client_s* client, const char* server_name,
    const char* address, uint16_t port, bool use_tls);

void wt_client_disconnect_impl(wt_client_s* client);
void wt_client_poll_impl(wt_client_s* client, int32_t timeout_us);

int32_t wt_client_send_stream_impl(
    wt_client_s* client, const uint8_t* data, int32_t length);
int32_t wt_client_send_datagram_impl(
    wt_client_s* client, const uint8_t* data, int32_t length);

bool wt_client_is_connected_impl(wt_client_s* client);
int32_t wt_client_get_mtu_impl(wt_client_s* client);

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_CLIENT_H */
