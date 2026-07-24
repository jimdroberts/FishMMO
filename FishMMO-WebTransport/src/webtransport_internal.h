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
  #define atomic_ptr_exchange(p,v) _InterlockedExchangePointer((void* volatile*)(p), (void*)(v))
#else
  #define atomic_ptr_load(p)   __atomic_load_n(p, __ATOMIC_ACQUIRE)
  #define atomic_ptr_store(p,v) __atomic_store_n(p, v, __ATOMIC_RELEASE)
  #define atomic_ptr_exchange(p,v) __atomic_exchange_n(p, v, __ATOMIC_ACQ_REL)
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

/* ── 64-bit atomics ──────────────────────────────────────────────
 * Used for counters that would overflow 32 bits within the server's
 * operational lifetime (e.g. ring-buffer head/tail indices in a
 * high-throughput disconnect path).  32-bit unsigned wraps after
 * ~4.3 billion operations; at 10k disconnects/second that's ~5 days
 * of continuous uptime.  64-bit counters eliminate the wraparound
 * hazard entirely (2^64 operations would take ~58 million years at
 * 10k/sec). */

#if defined(_MSC_VER)
  typedef __int64 atomic_int64_t;
  typedef unsigned __int64 atomic_uint64_t;
  #define atomic_init_u64(p, v)        (*(p) = (v))
  #define atomic_load_u64(p)           ((uint64_t)_InterlockedCompareExchange64((volatile __int64*)(p), 0, 0))
  #define atomic_store_u64(p, v)       _InterlockedExchange64((volatile __int64*)(p), (__int64)(v))
  #define atomic_fetch_add_u64(p, v)   ((uint64_t)_InterlockedExchangeAdd64((volatile __int64*)(p), (__int64)(v)))
  #define atomic_fetch_sub_u64(p, v)   ((uint64_t)_InterlockedExchangeAdd64((volatile __int64*)(p), -(__int64)(v)))
  static __inline int _wt_cas_strong_u64(volatile __int64 *p, __int64 *expected, __int64 desired) {
      __int64 cur = _InterlockedCompareExchange64(p, desired, *expected);
      if (cur == *expected) return 1;
      *expected = cur;
      return 0;
  }
  #define atomic_compare_exchange_strong_u64(p, expected, desired) \
      _wt_cas_strong_u64((volatile __int64*)(p), (__int64*)(expected), (__int64)(desired))
#else
  typedef int64_t atomic_int64_t;
  typedef uint64_t atomic_uint64_t;
  #define atomic_init_u64(p, v)        (*(p) = (v))
  #define atomic_load_u64(p)           __atomic_load_n(p, __ATOMIC_SEQ_CST)
  #define atomic_store_u64(p, v)       __atomic_store_n(p, v, __ATOMIC_SEQ_CST)
  #define atomic_fetch_add_u64(p, v)   __atomic_fetch_add(p, v, __ATOMIC_SEQ_CST)
  #define atomic_fetch_sub_u64(p, v)   __atomic_fetch_sub(p, v, __ATOMIC_SEQ_CST)
  #define atomic_compare_exchange_strong_u64(p, expected, desired) \
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
  #include <pthread.h>
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

/* ── FIX #3: H3 handshake timeout ─────────────────────────────
 * Maximum time allowed for the HTTP/3 WebTransport handshake
 * (SETTINGS + CONNECT exchange) after the QUIC connection is
 * established.  Clients that complete the TLS handshake but
 * never send an H3 control stream or CONNECT request are
 * disconnected after this deadline to prevent slot exhaustion. */
#define WT_H3_HANDSHAKE_TIMEOUT_MS  15000  /* 15 seconds */

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

/* ── Global API call reference counter ────────────────────────
 * Prevents wt_deinit() from closing MsQuic while any API call is
 * in-flight on another thread. Every API entry point increments
 * this counter before reading MsQuic; wt_deinit spins until it
 * reaches zero before closing. */
extern atomic_int g_api_call_refcount;
extern atomic_bool g_initialised;

/* Helper: increment refcount, return true if library is initialised.
 *
 * Guards on g_initialised rather than the MsQuic pointer.  Checking
 * MsQuic directly creates a TOCTOU race: wt_deinit() sets g_initialised=0
 * before calling MsQuicClose(), so a concurrent wt_api_enter() could observe
 * MsQuic != NULL (still live) even though deinitialization has already begun.
 * By the time wt_api_enter() returns, MsQuic may be closed and the caller
 * would invoke a freed API table.
 *
 * Using g_initialised as the gate closes this window: once wt_deinit() CAS's
 * g_initialised 1→0, no new API calls can enter regardless of whether
 * MsQuicClose has run yet.  This relies on the existing drain loop in
 * wt_deinit() — it spins on g_api_call_refcount reaching 0 before closing
 * MsQuic, so in-flight calls (those that incremented before the CAS) are
 * guaranteed to complete first.
 *
 * The acquire fence pairs with the release-store in wt_init() and the
 * CAS in wt_deinit(), ensuring the caller sees a consistent view of
 * all library state. */
static inline int wt_api_enter(void) {
    /* Read g_initialised with acquire semantics.  If false, the library
     * was never initialised or is already shutting down — bail. */
    int init;
#if defined(_MSC_VER)
    init = _InterlockedOr((long*)&g_initialised, 0);
#else
    init = __atomic_load_n(&g_initialised, __ATOMIC_ACQUIRE);
#endif
    if (!init) return 0;

    atomic_fetch_add(&g_api_call_refcount, 1);

    /* Full fence: ensure refcount increment is visible before the caller
     * proceeds to use MsQuic, preventing wt_deinit from seeing a zero
     * refcount while an API call is in-flight. */
#if defined(_MSC_VER)
    MemoryBarrier();
#else
    __atomic_thread_fence(__ATOMIC_SEQ_CST);
#endif

    /* Re-check g_initialised after the fence.  If wt_deinit() CAS'd
     * g_initialised to 0 between our first load and the refcount
     * increment, the drain loop may have already observed refcount==0
     * and closed MsQuic.  Decrement and bail to avoid use-after-close. */
#if defined(_MSC_VER)
    init = _InterlockedOr((long*)&g_initialised, 0);
#else
    init = __atomic_load_n(&g_initialised, __ATOMIC_ACQUIRE);
#endif
    if (!init) {
        atomic_fetch_sub(&g_api_call_refcount, 1);
        return 0;
    }

    return 1;
}

/* Helper: decrement refcount on API call exit. */
static inline void wt_api_exit(void) {
#if defined(_MSC_VER)
    MemoryBarrier();
#else
    __atomic_thread_fence(__ATOMIC_SEQ_CST);
#endif
    atomic_fetch_sub(&g_api_call_refcount, 1);
}

#endif /* WEBTRANSPORT_INTERNAL_H */