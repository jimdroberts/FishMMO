/**
 * @file webtransport_internal.h
 * @brief Internal structures and helpers shared across the library.
 *
 * NOT part of the public API. Do not expose to P/Invoke callers.
 */

#ifndef WEBTRANSPORT_INTERNAL_H
#define WEBTRANSPORT_INTERNAL_H

#include "webtransport_api.h"

#include <msquic.h>
#include <stdatomic.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ── Platform macros ────────────────────────────────────────── */

#if defined(WT_PLATFORM_WINDOWS)
  #define WT_THREAD_RETURN  DWORD WINAPI
  #define WT_THREAD_PARAM   LPVOID
  #define wt_sleep_ms(ms)   Sleep(ms)
#else
  #define WT_THREAD_RETURN  void*
  #define WT_THREAD_PARAM   void*
  #define wt_sleep_ms(ms)   usleep((ms) * 1000)
#endif

/* ── Constants ──────────────────────────────────────────────── */

#define WT_MAX_CLIENTS          4096
#define WT_MAX_ALPN_LENGTH      64
#define WT_MAX_ADDRESS_LENGTH   256

#define WT_DEFAULT_IDLE_TIMEOUT_MS  120000

/* ── Connection state enum ──────────────────────────────────── */

typedef enum {
    WT_CONN_STATE_IDLE = 0,
    WT_CONN_STATE_HANDSHAKING,
    WT_CONN_STATE_CONNECTED,
    WT_CONN_STATE_DISCONNECTING,
    WT_CONN_STATE_CLOSED
} wt_connection_state_t;

/* ── Server state enum ──────────────────────────────────────── */

typedef enum {
    WT_SERVER_STOPPED = 0,
    WT_SERVER_STARTING,
    WT_SERVER_STARTED,
    WT_SERVER_STOPPING
} wt_server_state_t;

/* ── Client state enum ──────────────────────────────────────── */

typedef enum {
    WT_CLIENT_STOPPED = 0,
    WT_CLIENT_STARTING,
    WT_CLIENT_STARTED,
    WT_CLIENT_STOPPING
} wt_client_state_t;

/* ── Logging ────────────────────────────────────────────────── */

#ifndef WT_NO_LOGGING
  #include <stdio.h>
  #define WT_LOG(level, fmt, ...) \
      fprintf(stderr, "[wt:%s] " fmt "\n", level, ##__VA_ARGS__)
  #define WT_LOG_INFO(fmt, ...)  WT_LOG("INFO",  fmt, ##__VA_ARGS__)
  #define WT_LOG_WARN(fmt, ...)  WT_LOG("WARN",  fmt, ##__VA_ARGS__)
  #define WT_LOG_ERROR(fmt, ...) WT_LOG("ERROR", fmt, ##__VA_ARGS__)
#else
  #define WT_LOG_INFO(fmt, ...)
  #define WT_LOG_WARN(fmt, ...)
  #define WT_LOG_ERROR(fmt, ...)
#endif

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_INTERNAL_H */
