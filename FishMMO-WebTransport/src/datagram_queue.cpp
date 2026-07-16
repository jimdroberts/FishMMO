/**
 * @file datagram_queue.cpp
 * @brief Lock-free ring buffer for QUIC datagrams (FishNet channel 1 / Unreliable).
 */

#include "datagram_queue.h"
#include <string.h>

void wt_datagram_queue_init(wt_datagram_queue_t* q)
{
    memset(q->entries, 0, sizeof(q->entries));
    atomic_init(&q->write_idx, 0);
    atomic_init(&q->read_idx, 0);
}

void wt_datagram_queue_reset(wt_datagram_queue_t* q)
{
    for (int i = 0; i < WT_DGRAM_QUEUE_CAPACITY; i++) {
        q->entries[i].occupied = false;
        q->entries[i].length = 0;
    }
    atomic_store(&q->write_idx, 0);
    atomic_store(&q->read_idx, 0);
}

bool wt_datagram_queue_push(
    wt_datagram_queue_t*    q,
    wt_connection_id_t      conn_id,
    const uint8_t*          data,
    int32_t                 length)
{
    if (!data || length <= 0 || length > WT_DGRAM_MAX_SIZE)
        return false;

    uint32_t w = atomic_load(&q->write_idx);
    uint32_t next = (w + 1) % WT_DGRAM_QUEUE_CAPACITY;

    if (next == atomic_load(&q->read_idx)) {
        WT_LOG_WARN("Datagram queue full, dropping datagram (%d bytes)", length);
        return false;
    }

    wt_datagram_entry_t* entry = &q->entries[w];
    memcpy(entry->data, data, (size_t)length);
    entry->length = length;
    entry->conn_id = conn_id;
    entry->occupied = true;

    atomic_store(&q->write_idx, next);
    return true;
}

int32_t wt_datagram_queue_drain(
    wt_datagram_queue_t*    q,
    void (*cb)(void* ctx, wt_connection_id_t conn_id,
               const uint8_t* data, int32_t length),
    void*                   ctx)
{
    if (!cb) return 0;

    int32_t count = 0;
    uint32_t r = atomic_load(&q->read_idx);
    uint32_t w = atomic_load(&q->write_idx);

    while (r != w) {
        wt_datagram_entry_t* entry = &q->entries[r];
        if (entry->occupied) {
            cb(ctx, entry->conn_id, entry->data, entry->length);
            entry->occupied = false;
            entry->length = 0;
            count++;
        }
        r = (r + 1) % WT_DGRAM_QUEUE_CAPACITY;
    }

    atomic_store(&q->read_idx, r);
    return count;
}
