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

/* ── Atomics (GCC/Clang builtins, MSVC interlocked intrinsics) ─
 *
 * MEDIUM: These are PLAIN integer types, NOT C11 _Atomic types.
 * When the macros below are defined (via GCC/Clang __atomic builtins
 * or MSVC Interlocked intrinsics), operations via atomic_load(),
 * atomic_store(), atomic_fetch_add(), etc. are correctly atomic.
 *
 * WARNING: Direct assignment (e.g. `my_atomic_int = 5;`) or direct
 * reads (e.g. `int x = my_atomic_int;`) COMPILE SILENTLY but are
 * NOT ATOMIC — they produce plain memory accesses without proper
 * memory ordering or atomicity guarantees. ALWAYS use the atomic
 * macros (atomic_load, atomic_store, atomic_fetch_add, etc.) for
 * every access to these variables. */
typedef int atomic_int;
typedef unsigned int atomic_uint;
/* atomic_bool is typedef'd to int because C11 _Atomic bool has
 * inconsistent size across platforms. All atomic operations on this
 * type read/write 4 bytes (int-sized).  Some platforms use 1-byte
 * _Bool, others 4.  The typedef to int guarantees consistent struct
 * layout across all ABIs.  All atomic macros (atomic_load,
 * atomic_store, etc.) operate on int-sized operands via GCC __atomic
 * builtins or MSVC Interlocked intrinsics.  This ensures that every
 * access to an atomic_bool field uses the correct atomic instruction
 * and that structs containing atomic_bool fields have the same layout
 * on x86, ARM, and ARM64. */
typedef int atomic_bool;

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
  /* Helper: C11-style CAS — updates *expected to current value on failure.
   * Returns 1 on success (swap performed), 0 on failure.
   * Used by both C++ and C paths via the macro wrapper below.
   * MSVC __inline is available in both C and C++ modes. */
  static __inline int _wt_cas_strong(volatile long *p, long *expected, long desired) {
      long cur = _InterlockedCompareExchange(p, desired, *expected);
      if (cur == *expected) return 1;
      *expected = (int)cur;
      return 0;
  }
  #define atomic_compare_exchange_strong(p, expected, desired) \
      _wt_cas_strong((volatile long*)(p), (long*)(expected), (long)(desired))
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
#define WT_DGRAM_QUEUE_CAPACITY    1024

#define WT_MAX_STREAMS             4096
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
  /* NOTE: fprintf(stderr) is not async-signal-safe. These logs are for
   * development/debugging only. In production, disable via compile flag
   * or redirect stderr.
   *
   * fprintf is also NOT async-signal-safe in signal-handler context (e.g.
   * SIGSEGV in a QUIC callback) where fprintf may deadlock on stderr's
   * internal FILE lock held by the interrupted thread.  This is acceptable
   * because:
   *   - WT_LOG_* macros are only called from application-thread contexts
   *     (QUIC callbacks, poll, send) — never from signal handlers.
   *   - The native library is a non-production debugging aid; production
   *     deployments disable it via WT_NO_LOGGING.
   * If production-grade signal-safe logging is ever needed, replace fprintf
   * with writev(2) to a dedicated log fd (preserving errno). */
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