/**
 * @file datagram_queue.h
 * @brief Thread-safe ring buffer for QUIC datagrams (FishNet channel 1).
 */

#ifndef WEBTRANSPORT_DATAGRAM_QUEUE_H
#define WEBTRANSPORT_DATAGRAM_QUEUE_H

#include "webtransport_internal.h"

#if defined(WT_PLATFORM_WINDOWS)
  #include <windows.h>
#else
  #include <pthread.h>
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* ── Single datagram entry ──────────────────────────────────── */

typedef struct {
    uint8_t             data[WT_DGRAM_MAX_SIZE];
    int32_t             length;
    wt_connection_id_t  conn_id;
    bool                occupied;
} wt_datagram_entry_t;

/* ── Thread-safe ring buffer ────────────────────────────────── */

typedef struct {
    wt_datagram_entry_t entries[WT_DGRAM_QUEUE_CAPACITY];
    uint32_t            write_idx;
    uint32_t            read_idx;
#if defined(WT_PLATFORM_WINDOWS)
    CRITICAL_SECTION    mutex;
#else
    pthread_mutex_t     mutex;
#endif
} wt_datagram_queue_t;

/* ── API ────────────────────────────────────────────────────── */

void wt_datagram_queue_init(wt_datagram_queue_t* q);
void wt_datagram_queue_destroy(wt_datagram_queue_t* q);
void wt_datagram_queue_reset(wt_datagram_queue_t* q);

bool wt_datagram_queue_push(
    wt_datagram_queue_t* q, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length);

int32_t wt_datagram_queue_drain(
    wt_datagram_queue_t* q,
    void (*cb)(void* ctx, wt_connection_id_t conn_id,
               const uint8_t* data, int32_t length),
    void* ctx);

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_DATAGRAM_QUEUE_H */