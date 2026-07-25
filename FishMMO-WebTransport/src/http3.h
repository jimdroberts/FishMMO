/**
 * @file http3.h
 * @brief Minimal HTTP/3 WebTransport handshake implementation.
 *
 * Implements the subset of HTTP/3 (RFC 9114) needed to establish a
 * WebTransport session over QUIC.  After the handshake completes, the
 * QUIC connection is handed off to the existing wt_session layer which
 * manages bidi streams (reliable) and datagrams (unreliable).
 *
 * Protocol detection is automatic: the first byte of the first
 * client-initiated bidirectional stream is inspected:
 *   - 0x00 → HTTP/3 control stream → WebTransport handshake
 *   - ANY  → raw QUIC (native client) → bypass handshake
 *
 * This preserves backward compatibility with the existing native C++
 * client while adding browser WebTransport (W3C) support.
 *
 * ## HTTP/3 WebTransport Handshake (server-side)
 *
 *   Client                                    Server
 *     |--- [Control Stream: 0x00, SETTINGS] ->|
 *     |<-- [Control Stream: 0x00, SETTINGS] --|
 *     |--- [Req Stream: HEADERS] ------------>|
 *     |    :method  = CONNECT                  |
 *     |    :protocol = webtransport            |
 *     |    :path     = /wt/7770               |
 *     |    :authority = game.fishmmo.com       |
 *     |<-- [HEADERS: :status = 200] -------- --|
 *     |=== WebTransport session established == |
 */

#ifndef WEBTRANSPORT_HTTP3_H
#define WEBTRANSPORT_HTTP3_H

#include "webtransport_internal.h"
#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ── HTTP/3 Stream Types ────────────────────────────────────── */

#define H3_STREAM_CONTROL       0x00
#define H3_STREAM_PUSH          0x01
#define H3_STREAM_QPACK_ENCODER 0x02
#define H3_STREAM_QPACK_DECODER 0x03
#define H3_STREAM_REQUEST       0x04  /* CONNECT request stream */

/* ── HTTP/3 Frame Types ─────────────────────────────────────── */

#define H3_FRAME_DATA           0x00
#define H3_FRAME_HEADERS        0x01
#define H3_FRAME_CANCEL_PUSH    0x03
#define H3_FRAME_SETTINGS       0x04
#define H3_FRAME_PUSH_PROMISE   0x05
#define H3_FRAME_GOAWAY         0x07
#define H3_FRAME_MAX_PUSH_ID    0x0D

/* ── HTTP/3 Settings Identifiers ─────────────────────────────── */
/*
 * Chrome/Quiche (http_constants.h) requires the draft-07 max-sessions ID
 * and also understands draft-00 enable + RFC 9297 datagram. Advertise all
 * known IDs so Chromium enables Extended CONNECT + WebTransport.
 */
#define H3_SETTINGS_ENABLE_WEBTRANSPORT       0x2b603742u /* draft00 SETTINGS_WEBTRANS / ENABLE_WEBTRANSPORT */
#define H3_SETTINGS_DATAGRAM                  0x33u       /* RFC 9297 SETTINGS_H3_DATAGRAM */
#define H3_SETTINGS_DATAGRAM_DRAFT04          0xffd277u   /* draft-ietf-masque-h3-datagram-04 (Quiche still checks) */
#define H3_SETTINGS_ENABLE_CONNECT_PROTOCOL   0x08u       /* RFC 9220 / RFC 8441 Extended CONNECT */
#define H3_SETTINGS_WT_MAX_SESSIONS_DRAFT07   0xc671706au /* Quiche SETTINGS_WEBTRANS_MAX_SESSIONS_DRAFT07 */
#define H3_SETTINGS_WT_MAX_SESSIONS_ALT       0x14e9cd29u /* picoquic drafts 13-14 SETTINGS_WEBTRANSPORT_MAX_SESSIONS */
/* Legacy alias for backward compatibility with internal callers: */
#define H3_SETTINGS_WT_MAX_SESSIONS           H3_SETTINGS_WT_MAX_SESSIONS_DRAFT07

/* ── QPACK encoding prefixes ─────────────────────────────────── */
#define QPACK_INDEXED_STATIC    0xC0  /* 11xxxxxx — indexed static table entry */
#define QPACK_LITERAL_NAME_REF  0x50  /* 0101xxxx — literal with static name ref */

/* ── WebTransport CONNECT required pseudo-headers ────────────── */

#define WT_REQUIRED_PROTOCOL    "webtransport"

/* ── HTTP/3 Server Session State ────────────────────────────── */

typedef enum {
    H3_SRV_WAIT_CONTROL_STREAM = 0,   /* waiting for client's control stream */
    H3_SRV_GOT_SETTINGS,              /* client SETTINGS received, server SETTINGS sent */
    H3_SRV_WAIT_CONNECT,              /* waiting for CONNECT request */
    H3_SRV_ESTABLISHED,               /* WebTransport session established */
    H3_SRV_ERROR                      /* handshake failed */
} h3_server_state_t;

/* ── HTTP/3 Client Session State ────────────────────────────── */

typedef enum {
    H3_CLI_SENDING_SETTINGS = 0,      /* sending SETTINGS on control stream */
    H3_CLI_SENDING_CONNECT,           /* sending CONNECT request */
    H3_CLI_WAIT_RESPONSE,             /* waiting for 200 OK */
    H3_CLI_ESTABLISHED,               /* WebTransport session established */
    H3_CLI_ERROR                      /* handshake failed */
} h3_client_state_t;

/* ── Callbacks ──────────────────────────────────────────────── */

/** Called when the WebTransport handshake completes successfully.
 *  The caller should create a wt_session at this point. */
typedef void (*h3_on_session_ready_fn)(
    void*       context,
    HQUIC       quic_conn,
    const char* path,
    const char* authority);

/** Called when the handshake fails. */
typedef void (*h3_on_error_fn)(
    void*       context,
    int         error_code,
    const char* message);

/* ── Per-Stream Context (for HTTP/3 frame processing) ───────── */

typedef struct h3_stream_ctx_s {
    HQUIC               quic_stream;
    int                 stream_type;    /* -1 = unknown, H3_STREAM_* otherwise */
    uint64_t            stream_id;
    uint8_t*            recv_buf;
    uint32_t            recv_offset;
    uint32_t            recv_capacity;
    bool                is_request;     /* true if this is the CONNECT request stream */
    bool                is_unidirectional; /* true if QUIC uni stream (control/QPACK); never native game data */
    struct h3_session_s* h3;            /* back-pointer to HTTP/3 session (for data processing) */
    struct h3_stream_ctx_s* next;       /* linked list for session cleanup */
    atomic_bool             freed;          /* CAS guard — prevents double-free when
                                               PEER_SEND_ABORTED and SHUTDOWN_COMPLETE
                                               both fire for the same stream */
} h3_stream_ctx_t;

/* ── HTTP/3 Session ─────────────────────────────────────────── */

typedef struct h3_session_s {
    HQUIC               quic_conn;

    /* Server state */
    h3_server_state_t   server_state;
    HQUIC               server_control_stream;
    void*               server_control_ctx;   /* h3_send_only_ctx_t* — for teardown nulling */
    /* Server-initiated QPACK uni streams (types 0x02 / 0x03). Chrome/Quiche
     * expect the peer to open these even when only the static table is used;
     * missing them can stall the client after SETTINGS with no CONNECT. */
    HQUIC               server_qpack_encoder_stream;
    HQUIC               server_qpack_decoder_stream;
    /* True after server control SETTINGS have been StreamSend'd (proactive or
     * reactive). Prevents double SETTINGS if peer control arrives later. */
    bool                server_settings_sent;
    /* Set on QUIC worker when SETTINGS must be sent; cleared and executed on
     * the application poll thread. NEVER open streams / StreamSend from
     * inside stream RECEIVE (msquic re-entrancy → QuicOperationFree crash). */
    volatile int        settings_bootstrap_pending;
    /* True once any peer H3 uni is seen (control 0x00 / QPACK 0x02/0x03).
     * Used to preserve native-FishMMO detection after proactive SETTINGS. */
    bool                peer_h3_seen;

    /* Client state */
    h3_client_state_t   client_state;
    HQUIC               client_control_stream;
    void*               client_control_ctx;   /* h3_send_only_ctx_t* — for teardown nulling */

    /* Handshake data */
    char                request_path[256];
    char                request_authority[256];
    bool                handshake_complete;
    bool                is_server;      /* true = server, false = client */
    /* QUIC stream ID of the accepted CONNECT request (WebTransport Session ID).
     * Used to frame post-handshake data streams for Chrome (WEBTRANSPORT_STREAM). */
    uint64_t            connect_stream_id;
    /* True when session was established via browser CONNECT (not native raw). */
    bool                is_webtransport;

    /* Callbacks */
    h3_on_session_ready_fn  on_ready;
    h3_on_error_fn          on_error;
    void*                   callback_ctx;

    /**
     * If non-NULL, this h3_stream_ctx_t holds buffered data from the first
     * peer stream in a native (non-HTTP/3) connection. Set during native
     * protocol detection in h3_server_process_data, consumed by
     * on_h3_session_ready to replay the data into the stream_manager.
     * Freed after replay -- never accessed by h3 after consumption.
     */
    void*               native_stream_ctx;  /* h3_stream_ctx_t* */

    /* Linked list of active stream context objects.
     * Freed during h3_session_free to prevent leaks from
     * streams that outlive the HTTP/3 session teardown.
     * Must be accessed only while holding stream_ctx_lock. */
    struct h3_stream_ctx_s* stream_ctx_list;

    /* Mutex protecting stream_ctx_list and h3_stream_ctx_t linked-list
     * operations.  Multiple QUIC worker threads can concurrently unlink
     * stream contexts in SHUTDOWN_COMPLETE callbacks. */
#if defined(WT_PLATFORM_WINDOWS)
    CRITICAL_SECTION        stream_ctx_lock;
#else
    pthread_mutex_t         stream_ctx_lock;
#endif

    /* Link to parent (for cleanup during teardown) */
    /* Allowed origins for CORS (comma-separated, empty = allow all) */
    char                    allowed_origins[1024];

    /* Expected :authority hostname to validate in CONNECT requests.
     * Empty = skip authority validation (backward compatible).
     * When configured, browser CONNECT requests MUST include an
     * :authority matching this value. Prevents host-confusion attacks
     * in multi-tenant deployments. */
    char                    expected_authority[256];

    /* When true, native (non-HTTP/3) clients are allowed to connect
     * without CORS or :authority validation. Defaults to true for
     * backward compatibility. Set to false in production when
     * allowed_origins is configured to prevent native clients from
     * bypassing origin-based access control. */
    bool                    allow_native_clients;

    /* M-1: Refcount-based lifetime management for h3_session_t.
     * The session is shared between the connection owner (ref_count=1 at
     * creation) and any in-flight RECEIVE / PEER_SEND_SHUTDOWN callbacks.
     *
     * - h3_session_acquire() CAS-loops to increment ref_count iff it is > 0
     *   AND released == false.  Returns true if the acquire succeeded (the
     *   session is guaranteed to remain alive for the caller's duration).
     * - h3_session_release() CAS-loops to decrement ref_count.  When
     *   ref_count reaches 0 AND released == true, the session is freed.
     * - h3_session_free() sets released = true, performs cleanup that does
     *   NOT require the session struct itself to remain alive (control-
     *   stream abort, stream_ctx_list drain), then drops the owner reference
     *   via h3_session_release().  The actual free() + mutex_destroy is
     *   deferred until all concurrent acquires have released.
     *
     * This is the same pattern used by wt_session_t (session.h/session.c). */
    atomic_uint              ref_count;      /* 1 = owner reference */
    atomic_bool              released;       /* owner intends to free;
                                             * new acquires fail after this */

    void*               parent_ptr;     /* wt_server_conn_t* or wt_client_s* */
} h3_session_t;

/* ── Stream context list lock helpers ────────────────────────── */
#if defined(WT_PLATFORM_WINDOWS)
  #define H3_LOCK(h3)    EnterCriticalSection(&(h3)->stream_ctx_lock)
  #define H3_UNLOCK(h3)  LeaveCriticalSection(&(h3)->stream_ctx_lock)
#else
  #define H3_LOCK(h3)    pthread_mutex_lock(&(h3)->stream_ctx_lock)
  #define H3_UNLOCK(h3)  pthread_mutex_unlock(&(h3)->stream_ctx_lock)
#endif

/* ── API ────────────────────────────────────────────────────── */

/**
 * Create an HTTP/3 session for a newly-connected QUIC connection.
 *
 * @param quic_conn     The QUIC connection handle.
 * @param is_server     true for server, false for client.
 * @param on_ready      Called when WebTransport handshake succeeds.
 * @param on_error      Called when handshake fails.
 * @param ctx           User context passed to callbacks.
 * @return New session, or NULL on allocation failure.
 */
h3_session_t* h3_session_create(
    HQUIC                   quic_conn,
    bool                    is_server,
    h3_on_session_ready_fn  on_ready,
    h3_on_error_fn          on_error,
    void*                   ctx);

/**
 * Request server SETTINGS bootstrap (control + QPACK uni).
 * Safe from any thread: only sets a flag. Actual StreamOpen/Send runs on
 * the next wt_server_poll via h3_server_poll_deferred().
 */
void h3_server_request_settings_bootstrap(h3_session_t* h3);

/**
 * Run deferred SETTINGS bootstrap if pending. Call from application poll
 * thread only (not from msquic stream/connection callbacks).
 */
void h3_server_poll_deferred(h3_session_t* h3);

/**
 * Server-side: immediately open control + QPACK uni streams and send SETTINGS.
 *
 * Chromium/Quiche send server SETTINGS as soon as encryption is established
 * (not after seeing the client's control stream). Calling this from
 * QUIC_CONNECTION_EVENT_CONNECTED unblocks Chrome WebTransport CONNECT, which
 * otherwise stalls after SETTINGS never arriving on the peer control stream.
 *
 * Safe to call only once; subsequent calls are no-ops if SETTINGS already sent.
 *
 * @return 0 on success (or already sent), negative on failure.
 */
int h3_server_send_initial_settings(h3_session_t* h3);

/**
 * Free the HTTP/3 session and all associated resources.
 * Aborts any in-progress handshake.
 */
void h3_session_free(h3_session_t* h3);

/**
 * Begin the WebTransport handshake as a client.
 * Sends the control stream (type 0x00) and SETTINGS frame, then
 * the CONNECT request on a new bidi stream.
 *
 * @param path       Request path (e.g. "/wt/7770").
 * @param authority  Request authority (hostname).
 * @return 0 on success, negative on error.
 */
int32_t h3_client_connect(
    h3_session_t* h3,
    const char*   path,
    const char*   authority);

/**
 * Server-side: handle a new peer stream during HTTP/3 handshake.
 * Called from QUIC PEER_STREAM_STARTED.
 *
 * @return 0  = h3 consumed stream (HTTP/3 protocol)
 *         1  = regular data stream (pass to wt_stream_manager)
 *        -1  = error
 */
int h3_server_handle_stream(h3_session_t* h3, HQUIC stream,
                            bool is_unidirectional,
                            h3_stream_ctx_t** out_sctx);

/**
 * Server-side: process received data on an HTTP/3 stream during handshake.
 * Called from stream RECEIVE callback after h3_server_handle_stream.
 *
 * @return 0  = still handshaking
 *         1  = handshake complete (caller creates wt_session)
 *        -1  = error
 */
int h3_server_process_data(h3_session_t* h3, h3_stream_ctx_t* sctx);

/**
 * Client-side: process received data on the CONNECT request stream.
 * Called from the stream RECEIVE callback during the HTTP/3 handshake.
 *
 * @return 0  = still handshaking
 *         1  = handshake complete (on_ready called)
 *        -1  = error
 */
int h3_client_process_data(h3_session_t* h3, h3_stream_ctx_t* sctx);

/**
 * Unlink a stream context from the session's tracking list.
 * Must be called BEFORE manually freeing an h3_stream_ctx_t that was
 * added to the session's stream_ctx_list by h3_stream_ctx_create().
 * Safe to call with NULL — no-op.
 */
void h3_stream_ctx_unlink(h3_stream_ctx_t* sctx);

/**
 * Acquire a reference on an h3_session_t.  Returns true if the acquire
 * succeeded, guaranteeing h3 remains alive until h3_session_release().
 * Returns false if the session is already being freed (released == true)
 * or ref_count has dropped to zero.
 *
 * Every successful acquire() MUST be paired with a release().
 */
bool h3_session_acquire(h3_session_t* h3);

/**
 * Release a reference on an h3_session_t.  If this is the last reference
 * AND released == true, frees the session and destroys the mutex.
 *
 * Safe to call with NULL — no-op.
 */
void h3_session_release(h3_session_t* h3);

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_HTTP3_H */