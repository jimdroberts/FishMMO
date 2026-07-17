/**
 * @file webtransport_internal.h
 * @brief Internal structures and helpers shared across the library.
 */

#ifndef WEBTRANSPORT_INTERNAL_H
#define WEBTRANSPORT_INTERNAL_H

#include "webtransport_api.h"

#include <msquic.h>
#include <stdbool.h>
#include <string.h>
#include <stdio.h>

/* ── Atomics (GCC/Clang builtins, MSVC interlocked intrinsics) ─ */
typedef int atomic_int;
typedef unsigned int atomic_uint;
typedef int atomic_bool;    /* always 4 bytes — consistent struct layout across platforms */

/* Pointer atomics — prevents TOCTOU when session ptr is freed on another thread */
#if defined(_MSC_VER)
  #define atomic_ptr_load(p)   (void*)_InterlockedCompareExchangePointer((void* volatile*)(p), NULL, NULL)
  #define atomic_ptr_store(p,v) _InterlockedExchangePointer((void* volatile*)(p), (void*)(v))
#else
  #define atomic_ptr_load(p)   __atomic_load_n(p, __ATOMIC_ACQUIRE)
  #define atomic_ptr_store(p,v) __atomic_store_n(p, v, __ATOMIC_RELEASE)
#endif

#if defined(_MSC_VER)
  #include <intrin.h>
  #define atomic_init(p, v)       (*(p) = (v))
  #define atomic_load(p)          (_InterlockedOr((long*)(p), 0))
  #define atomic_store(p, v)      (_InterlockedExchange((long*)(p), (long)(v)))
  #define atomic_fetch_add(p, v)  (_InterlockedExchangeAdd((long*)(p), (long)(v)))
  #define atomic_fetch_sub(p, v)  (_InterlockedExchangeAdd((long*)(p), -(long)(v)))
  /* NOTE: This macro does NOT update *expected on failure (unlike C11).
   * Current call sites do not depend on *expected being updated.
   * If future code needs post-failure *expected, use a proper loop:
   *   long cur = atomic_load(p);
   *   do { *expected = cur; } while ((cur = _InterlockedCompareExchange(...)) != *expected);
   */ \
  #define atomic_compare_exchange_strong(p, expected, desired) \
      (_InterlockedCompareExchange((long*)(p), (long)(desired), (long)(*(expected))) == (long)(*(expected)))
#else
  #define atomic_init(p, v)       (*(p) = (v))
  #define atomic_load(p)          __atomic_load_n(p, __ATOMIC_SEQ_CST)
  #define atomic_store(p, v)      __atomic_store_n(p, v, __ATOMIC_SEQ_CST)
  #define atomic_fetch_add(p, v)  __atomic_fetch_add(p, v, __ATOMIC_SEQ_CST)
  #define atomic_fetch_sub(p, v)  __atomic_fetch_sub(p, v, __ATOMIC_SEQ_CST)
  #define atomic_compare_exchange_strong(p, expected, desired) \
      __atomic_compare_exchange_n(p, expected, desired, 0, __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST)
#endif

/* msquic C header does not declare the MsQuic global.
 * It is obtained via MsQuicOpen2() and stored here. */
#ifdef __cplusplus
extern "C" {
#endif
extern const QUIC_API_TABLE* MsQuic;
#ifdef __cplusplus
}
#endif

/* ── Platform includes ────────────────────────────────────── */
#if defined(WT_PLATFORM_WINDOWS)
  #include <windows.h>
#else
  #include <unistd.h>
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* ── Constants ──────────────────────────────────────────────── */

#define WT_MAX_CLIENTS             4096
#define WT_MAX_ALPN_LENGTH         64
#define WT_MAX_ADDRESS_LENGTH      256

#define WT_DEFAULT_IDLE_TIMEOUT_MS  120000
#define WT_DEFAULT_MTU              1200

#define WT_DGRAM_MAX_SIZE          1500   /* max QUIC datagram payload (path MTU) */
#define WT_DGRAM_QUEUE_CAPACITY    256

#define WT_MAX_STREAMS             1024
#define WT_MAX_STREAM_RECV_BUF      (1024 * 1024)  /* 1 MB per stream */

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
  #define WT_LOG(level, fmt, ...) \
      fprintf(stderr, "[wt:%s] " fmt "\n", level, ##__VA_ARGS__)
  #define WT_LOG_INFO(fmt, ...)  WT_LOG("INFO",  fmt, ##__VA_ARGS__)
  #define WT_LOG_WARN(fmt, ...)  WT_LOG("WARN",  fmt, ##__VA_ARGS__)
  #define WT_LOG_ERROR(fmt, ...) WT_LOG("ERROR", fmt, ##__VA_ARGS__)
#else
  #define WT_LOG_INFO(fmt, ...)  ((void)0)
  #define WT_LOG_WARN(fmt, ...)  ((void)0)
  #define WT_LOG_ERROR(fmt, ...) ((void)0)
#endif

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_INTERNAL_H */