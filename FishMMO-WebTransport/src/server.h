/**
 * @file server.h
 * @brief WebTransport QUIC server — internal declarations.
 *
 * All functions use _impl suffix. The public C API in
 * webtransport_api.h delegates to these.
 */

#ifndef WEBTRANSPORT_SERVER_H
#define WEBTRANSPORT_SERVER_H

#include "webtransport_internal.h"
#include "session.h"
#include "datagram_queue.h"
#include "http3.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ── Per-connection context ─────────────────────────────────── */

typedef struct {
    wt_connection_id_t      id;
    HQUIC                   quic_conn;
    /* WARNING: session, state, and in_use are declared as plain types but
     * MUST be accessed only through atomic_ptr_load/store, atomic_load/store
     * respectively (see the platform-conditional macros in
     * webtransport_internal.h).  Direct read/write on these fields produces
     * non-atomic, non-ordered memory accesses that silently compile.
     * This applies to all fields in this struct whose comments mention
     * "atomic" or "use atomic_*" — the plain C type is an implementation
     * detail to avoid C11 _Atomic ABI variance across compilers. */
    wt_session_t*           session;          // Raw pointer typed for C ABI compatibility. Access ONLY via atomic_ptr_load/atomic_ptr_store.
    atomic_int              state;            /* wt_connection_state_t, atomic */
    char                    remote_addr[WT_MAX_ADDRESS_LENGTH];
    atomic_bool             in_use;           /* atomic — set from two threads */
    struct wt_server_s*     owner;            /* back-pointer to parent server */

    /* Session pending deferred shutdown. Set by QUIC callback thread
     * (SHUTDOWN_COMPLETE), consumed by poll (application thread).
     * MUST be accessed via atomic_ptr_load/atomic_ptr_store — written
     * on the QUIC callback thread, read/written on the poll thread
     * without a lock. */
    wt_session_t*           pending_shutdown_session;  // Raw pointer typed for C ABI compatibility. Access ONLY via atomic_ptr_load/atomic_ptr_store.

    /* HTTP/3 handshake session. Created on CONNECTED, freed when
     * the WebTransport session is established (on_h3_session_ready).
     * If h3_session is non-NULL and handshake_complete is false,
     * incoming streams are routed through HTTP/3 protocol detection. */
    h3_session_t*           h3_session;

    /* Per-connection datagram drop counter.  Reset to 0 by calloc at
     * connection creation.  Used to rate-limit queue-full warnings
     * without a global counter that bleeds across connections. */
    atomic_int              dgram_drop_count;
} wt_server_conn_t;

/* ── Server structure ───────────────────────────────────────── */

typedef struct wt_server_s {
    HQUIC                   registration;
    HQUIC                   session_config;
    HQUIC                   listener;

    wt_server_callbacks_t   callbacks;
    void*                   user_context;

    char                    alpn[WT_MAX_ALPN_LENGTH];
    char                    bind_address[WT_MAX_ADDRESS_LENGTH];
    uint16_t                port;
    uint32_t                max_clients;
    char                    cert_path[512];
    char                    key_path[512];
    char                    allowed_origins[1024];  /* comma-separated, empty = allow all */

    atomic_int              state;
    atomic_uint             connection_count;

    wt_server_conn_t*       connections;
    wt_datagram_queue_t     dgram_queue;

    atomic_uint             pending_shutdowns;  /* count of connections awaiting SHUTDOWN_COMPLETE */

    /* Pending shutdown queue for O(1) poll instead of O(N) scan.
     * SHUTDOWN_COMPLETE enqueues connection IDs here; poll drains them.
     * Protected by atomic reads/writes — only the QUIC callback thread
     * writes (append), only the poll thread reads (drain). */
    wt_connection_id_t      pending_shutdown_queue[WT_MAX_CLIENTS];
    atomic_uint             pending_shutdown_head;  /* poll reads here */
    atomic_uint             pending_shutdown_tail;  /* callback writes here */
} wt_server_s;

/* ── Internal API (called by webtransport_api.cpp) ──────────── */

wt_server_s* wt_server_alloc_impl(
    const char*                 certificate_path,
    const char*                 private_key_path,
    const char*                 alpn,
    const char*                 bind_address,
    uint16_t                    port,
    uint32_t                    max_clients,
    const wt_server_callbacks_t* callbacks,
    void*                       context);

void wt_server_free_impl(wt_server_s* server);
int32_t wt_server_start_impl(wt_server_s* server);
void wt_server_stop_impl(wt_server_s* server);
void wt_server_poll_impl(wt_server_s* server, int32_t timeout_us);

int32_t wt_server_send_stream_impl(
    wt_server_s* server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length);

int32_t wt_server_send_datagram_impl(
    wt_server_s* server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length);

void wt_server_disconnect_impl(wt_server_s* server, wt_connection_id_t conn_id);

const char* wt_server_get_client_addr_impl(
    wt_server_s* server, wt_connection_id_t conn_id);

int32_t wt_server_get_client_count_impl(wt_server_s* server);

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_SERVER_H */
