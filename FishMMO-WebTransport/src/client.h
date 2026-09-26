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

    /* ── Application-thread handoff of the connection handle ──────
     * Calls from the application side use the connection handle while the
     * connection's QUIC worker may be delivering SHUTDOWN_COMPLETE, whose
     * handler used to close it at once:
     *  - wt_client_get_connection_stats: a connection-level GetParam is
     *    queued to the worker and blocks until the worker answers;
     *  - wt_client_send_datagram / _stream: DatagramSend is given the
     *    connection handle (and the stream manager's sends run in the same
     *    window); a close between the send's checks and the call left it
     *    using a freed connection.
     *
     * app_conn is the handle such a call may use; app_state packs the number
     * of calls in flight (WT_CLIENT_APP_USERS) with a RETIRED bit.  A call
     * enters only while RETIRED is clear.  SHUTDOWN_COMPLETE sets RETIRED and
     * closes the handle itself only when no call is in flight; otherwise the
     * last call to leave closes it.  Both sides act on the one word, so its
     * modification order decides who closes, exactly once.  This is the
     * client's counterpart of the server's per-connection app_state gate.
     * app_conn MUST be accessed via atomic_ptr_load/store/exchange. */
    HQUIC                   app_conn;
    atomic_int              app_state;
} wt_client_s;

#define WT_CLIENT_APP_RETIRED  0x40000000
#define WT_CLIENT_APP_USERS    0x3FFFFFFF

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
int32_t wt_client_get_connection_stats_impl(
    wt_client_s* client, wt_connection_stats_t* stats);

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_CLIENT_H */
