/**
 * @file datagram_queue.h
 * @brief Lock-free ring buffer for QUIC datagrams.
 *
 * QUIC datagrams map to FishNet's unreliable channel (channel 1).
 * This is a single-producer, single-consumer ring buffer.
 * QUIC worker threads produce; the Unity main thread consumes via poll.
 */

#ifndef WEBTRANSPORT_DATAGRAM_QUEUE_H
#define WEBTRANSPORT_DATAGRAM_QUEUE_H

#include "webtransport_internal.h"
#include <stdatomic.h>

#ifdef __cplusplus
extern "C" {
#endif

#define WT_DGRAM_QUEUE_CAPACITY  256
#define WT_DGRAM_MAX_SIZE        65536   /* 64 KiB max datagram */

/* ── Single datagram entry ──────────────────────────────────── */

typedef struct {
    uint8_t     data[WT_DGRAM_MAX_SIZE];
    int32_t     length;
    wt_connection_id_t conn_id;   /* 0 for client-side */
    bool        occupied;
} wt_datagram_entry_t;

/* ── Lock-free ring buffer ──────────────────────────────────── */

typedef struct {
    wt_datagram_entry_t entries[WT_DGRAM_QUEUE_CAPACITY];
    atomic_uint         write_idx;   /* producer (QUIC thread) */
    atomic_uint         read_idx;    /* consumer (main thread) */
} wt_datagram_queue_t;

/* ── API ────────────────────────────────────────────────────── */

/** Initialise the queue. */
void wt_datagram_queue_init(wt_datagram_queue_t* q);

/** Reset the queue (discards all pending datagrams). */
void wt_datagram_queue_reset(wt_datagram_queue_t* q);

/**
 * Push a datagram onto the queue.  Called from QUIC worker thread.
 * @return true if pushed, false if queue is full (datagram dropped).
 */
bool wt_datagram_queue_push(
    wt_datagram_queue_t*    q,
    wt_connection_id_t      conn_id,
    const uint8_t*          data,
    int32_t                 length);

/**
 * Pop all pending datagrams and call `cb` for each.
 * Called from the Unity main thread during poll.
 *
 * @param cb     Callback invoked for each datagram.
 * @param ctx    User context passed to callback.
 * @return Number of datagrams processed.
 */
int32_t wt_datagram_queue_drain(
    wt_datagram_queue_t*    q,
    void (*cb)(void* ctx, wt_connection_id_t conn_id,
               const uint8_t* data, int32_t length),
    void*                   ctx);

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_DATAGRAM_QUEUE_H */
