/**
 * @file webtransport_api.h
 * @brief Public C API for the FishMMO WebTransport native library.
 *
 * This library wraps msquic to provide WebTransport-over-HTTP/3 (QUIC)
 * client and server functionality. The API is designed for P/Invoke
 * from C# (Unity) with plain C calling convention and function pointers
 * for callbacks.
 *
 * ## Thread Safety Model
 *
 * All callbacks are invoked from threads internal to the library (QUIC worker
 * threads). The caller (C#) is responsible for queueing received data/events
 * to the Unity main thread. The poll functions (wt_server_poll, wt_client_poll)
 * are safe to call from any thread and drain internal queues.
 *
 * ## Channel Mapping
 *
 * FishNet uses two channels:
 *   - Channel 0 (Reliable)   → WebTransport bidirectional streams
 *   - Channel 1 (Unreliable) → QUIC DATAGRAM extension (RFC 9221)
 *
 * The transport layer distinguishes channels by which send function is used,
 * so no channel byte suffix is needed in the wire protocol.
 */

#ifndef WEBTRANSPORT_API_H
#define WEBTRANSPORT_API_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ── Platform export macro ──────────────────────────────────── */
#if defined(WT_PLATFORM_WINDOWS)
  #ifdef WT_BUILDING_DLL
    #define WT_API __declspec(dllexport)
  #else
    #define WT_API __declspec(dllimport)
  #endif
#else
  #define WT_API __attribute__((visibility("default")))
#endif

/* ── Opaque handle types ────────────────────────────────────── */
typedef struct wt_server_s*  WT_SERVER;
typedef struct wt_client_s*  WT_CLIENT;

/* ── IDs ────────────────────────────────────────────────────── */
typedef uint64_t wt_connection_id_t;
typedef uint64_t wt_stream_id_t;

#define WT_CONNECTION_ID_NONE  ((wt_connection_id_t)0)
#define WT_BROADCAST_ALL       ((wt_connection_id_t)-1)  /* server send to all clients */

/* ── Error codes ────────────────────────────────────────────── */
#define WT_OK                    0
#define WT_ERR_UNKNOWN          -1
#define WT_ERR_INVALID_STATE    -2
#define WT_ERR_CONNECT_FAILED   -3
#define WT_ERR_TLS_FAILED       -4
#define WT_ERR_SEND_FAILED      -5
#define WT_ERR_BUFFER_FULL      -6
#define WT_ERR_NOT_FOUND        -7

/* ── Callback function pointer types ────────────────────────────
 * These are invoked from QUIC worker threads. Do not call Unity
 * API functions from these callbacks. Queue and process on main.
 */

/** Server: a new client connected. */
typedef void (*wt_on_server_connect_fn)(
    void*               context,
    wt_connection_id_t  connection_id,
    const char*         remote_address);

/** Server: a client disconnected (locally or remotely). */
typedef void (*wt_on_server_disconnect_fn)(
    void*               context,
    wt_connection_id_t  connection_id,
    int                 error_code);

/** Server: reliable stream data received from a client.
 *  data is owned by the library until the callback returns. */
typedef void (*wt_on_server_stream_data_fn)(
    void*               context,
    wt_connection_id_t  connection_id,
    wt_stream_id_t      stream_id,
    const uint8_t*      data,
    int32_t             length);

/** Server: unreliable datagram received from a client.
 *  data is owned by the library until the callback returns. */
typedef void (*wt_on_server_datagram_fn)(
    void*               context,
    wt_connection_id_t  connection_id,
    const uint8_t*      data,
    int32_t             length);

/** Client: connected to server. */
typedef void (*wt_on_client_connect_fn)(
    void*               context);

/** Client: disconnected from server. */
typedef void (*wt_on_client_disconnect_fn)(
    void*               context,
    int                 error_code);

/** Client: reliable stream data received from server.
 *  data is owned by the library until the callback returns. */
typedef void (*wt_on_client_stream_data_fn)(
    void*               context,
    wt_stream_id_t      stream_id,
    const uint8_t*      data,
    int32_t             length);

/** Client: unreliable datagram received from server.
 *  data is owned by the library until the callback returns. */
typedef void (*wt_on_client_datagram_fn)(
    void*               context,
    const uint8_t*      data,
    int32_t             length);

/* ── Callback registration structs (bundled for simpler P/Invoke) ── */

typedef struct {
    wt_on_server_connect_fn      on_connect;
    wt_on_server_disconnect_fn   on_disconnect;
    wt_on_server_stream_data_fn  on_stream_data;
    wt_on_server_datagram_fn     on_datagram;
} wt_server_callbacks_t;

typedef struct {
    wt_on_client_connect_fn       on_connect;
    wt_on_client_disconnect_fn    on_disconnect;
    wt_on_client_stream_data_fn   on_stream_data;
    wt_on_client_datagram_fn      on_datagram;
} wt_client_callbacks_t;

/* ═══════════════════════════════════════════════════════════════
 * SERVER API
 * ═══════════════════════════════════════════════════════════════ */

/**
 * Create a WebTransport server.
 *
 * @param certificate_path  Path to TLS certificate (PEM format), or NULL
 *                          to use a self-signed development certificate.
 * @param private_key_path  Path to TLS private key (PEM format), or NULL
 *                          to use the bundled development key.
 * @param alpn              ALPN protocol string, typically "h3".
 * @param bind_address      IP address to bind (e.g. "0.0.0.0" or "127.0.0.1").
 * @param port              UDP port to listen on.
 * @param max_clients       Maximum concurrent client connections.
 * @param callbacks         Struct of callback function pointers.
 * @param context           Opaque user pointer passed to all callbacks.
 * @return Server handle, or NULL on failure.
 */
WT_API WT_SERVER wt_server_create(
    const char*                 certificate_path,
    const char*                 private_key_path,
    const char*                 alpn,
    const char*                 bind_address,
    uint16_t                    port,
    uint32_t                    max_clients,
    const wt_server_callbacks_t* callbacks,
    void*                       context);

/**
 * Destroy the server and free all resources.
 * Disconnects all clients immediately.
 */
WT_API void wt_server_destroy(WT_SERVER server);

/**
 * Start accepting connections.  Non-blocking — the server runs on
 * internal QUIC threads.
 *
 * @return WT_OK (0) on success, negative error code on failure.
 */
WT_API int32_t wt_server_start(WT_SERVER server);

/**
 * Stop accepting connections and disconnect all clients.
 */
WT_API void wt_server_stop(WT_SERVER server);

/**
 * Send reliable data (stream) to a specific client.
 * Use WT_BROADCAST_ALL to send to all connected clients.
 *
 * @return WT_OK (0) on success, negative on failure.
 */
WT_API int32_t wt_server_send_stream(
    WT_SERVER           server,
    wt_connection_id_t  connection_id,
    const uint8_t*      data,
    int32_t             length);

/**
 * Send unreliable data (datagram) to a specific client.
 * Use WT_BROADCAST_ALL to send to all connected clients.
 *
 * @return WT_OK (0) on success, negative on failure.
 */
WT_API int32_t wt_server_send_datagram(
    WT_SERVER           server,
    wt_connection_id_t  connection_id,
    const uint8_t*      data,
    int32_t             length);

/**
 * Disconnect a specific client.
 */
WT_API void wt_server_disconnect(
    WT_SERVER           server,
    wt_connection_id_t  connection_id);

/**
 * Get the remote address string for a connected client.
 * Returns a pointer to internal storage (valid until next call on this server).
 * Returns NULL if connection_id is not found.
 */
WT_API const char* wt_server_get_client_address(
    WT_SERVER           server,
    wt_connection_id_t  connection_id);

/**
 * Get the number of currently connected clients.
 */
WT_API int32_t wt_server_get_client_count(WT_SERVER server);

/**
 * Get the maximum number of clients this server was configured for.
 */
WT_API int32_t wt_server_get_max_clients(WT_SERVER server);

/**
 * Return the server's current state:
 *   0 = Stopped
 *   1 = Starting
 *   2 = Started
 *   3 = Stopping
 */
WT_API int32_t wt_server_get_state(WT_SERVER server);

/* ═══════════════════════════════════════════════════════════════
 * CLIENT API
 * ═══════════════════════════════════════════════════════════════ */

/**
 * Create a WebTransport client.
 *
 * @param callbacks  Struct of callback function pointers.
 * @param context    Opaque user pointer passed to all callbacks.
 * @return Client handle, or NULL on failure.
 */
WT_API WT_CLIENT wt_client_create(
    const wt_client_callbacks_t* callbacks,
    void*                       context);

/**
 * Destroy the client and free all resources.
 */
WT_API void wt_client_destroy(WT_CLIENT client);

/**
 * Connect to a WebTransport server.
 *
 * @param server_name  SNI hostname for TLS (e.g. "game.fishmmo.com").
 * @param address      IP address or hostname to connect to.
 * @param port         UDP port to connect to.
 * @param use_tls      Non-zero to enable TLS (WebTransport requires TLS).
 * @return WT_OK (0) on success (connection started async), negative on failure.
 */
WT_API int32_t wt_client_connect(
    WT_CLIENT       client,
    const char*     server_name,
    const char*     address,
    uint16_t        port,
    int32_t         use_tls);

/**
 * Disconnect from the server.
 */
WT_API void wt_client_disconnect(WT_CLIENT client);

/**
 * Poll the client for events.  Must be called regularly (e.g. each frame)
 * to process QUIC I/O and deliver callbacks.  Safe to call from any thread.
 *
 * @param timeout_ms  Max microseconds to block (0 = non-blocking).
 */
WT_API void wt_client_poll(WT_CLIENT client, int32_t timeout_us);

/**
 * Poll the server for events.  Must be called regularly (e.g. each frame)
 * to process QUIC I/O and deliver callbacks.  Safe to call from any thread.
 *
 * @param timeout_us  Max microseconds to block (0 = non-blocking).
 */
WT_API void wt_server_poll(WT_SERVER server, int32_t timeout_us);

/**
 * Send reliable data (stream) to the server.
 *
 * @return WT_OK (0) on success, negative on failure.
 */
WT_API int32_t wt_client_send_stream(
    WT_CLIENT       client,
    const uint8_t*  data,
    int32_t         length);

/**
 * Send unreliable data (datagram) to the server.
 *
 * @return WT_OK (0) on success, negative on failure.
 */
WT_API int32_t wt_client_send_datagram(
    WT_CLIENT       client,
    const uint8_t*  data,
    int32_t         length);

/**
 * Return non-zero if the client is currently connected.
 */
WT_API int32_t wt_client_is_connected(WT_CLIENT client);

/**
 * Get the current MTU for the connection.
 * Returns the max datagram size usable without fragmentation.
 */
WT_API int32_t wt_client_get_mtu(WT_CLIENT client);

/* ── Lifecycle ─────────────────────────────────────────────── */

/**
 * Initialise the WebTransport library. Must be called once before
 * any server or client operations. Initialises the MsQuic API table.
 * @return WT_OK on success, negative error code on failure.
 */
WT_API int32_t wt_init(void);

/**
 * Shut down the WebTransport library. Call after all servers and
 * clients have been destroyed. Closes the MsQuic API table.
 */
WT_API void wt_deinit(void);

/* ── Utility ───────────────────────────────────────────────── */

/**
 * Get a human-readable error string for an error code.
 */
WT_API const char* wt_error_string(int32_t error_code);

/**
 * Get library version string.
 */
WT_API const char* wt_version(void);

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_API_H */
