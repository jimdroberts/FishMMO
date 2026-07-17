/**
 * @file datagram_queue.cpp
 * @brief Thread-safe ring buffer for QUIC datagrams.
 */

#include "datagram_queue.h"

#if defined(WT_PLATFORM_WINDOWS)
  #define wt_mutex_init(m)    InitializeCriticalSection(m)
  #define wt_mutex_lock(m)    EnterCriticalSection(m)
  #define wt_mutex_unlock(m)  LeaveCriticalSection(m)
  #define wt_mutex_destroy(m) DeleteCriticalSection(m)
#else
  #define wt_mutex_init(m)    pthread_mutex_init(m, NULL)
  #define wt_mutex_lock(m)    pthread_mutex_lock(m)
  #define wt_mutex_unlock(m)  pthread_mutex_unlock(m)
  #define wt_mutex_destroy(m) pthread_mutex_destroy(m)
#endif

void wt_datagram_queue_init(wt_datagram_queue_t* q)
{
    memset(q->entries, 0, sizeof(q->entries));
    q->write_idx = 0;
    q->read_idx = 0;
    wt_mutex_init(&q->mutex);
}

void wt_datagram_queue_destroy(wt_datagram_queue_t* q)
{
    wt_mutex_destroy(&q->mutex);
}

void wt_datagram_queue_reset(wt_datagram_queue_t* q)
{
    wt_mutex_lock(&q->mutex);
    for (int i = 0; i < WT_DGRAM_QUEUE_CAPACITY; i++) {
        q->entries[i].occupied = false;
        q->entries[i].length = 0;
    }
    q->write_idx = 0;
    q->read_idx = 0;
    wt_mutex_unlock(&q->mutex);
}

bool wt_datagram_queue_push(
    wt_datagram_queue_t* q, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length)
{
    if (!data || length <= 0 || length > WT_DGRAM_MAX_SIZE)
        return false;

    wt_mutex_lock(&q->mutex);

    uint32_t w = q->write_idx;
    uint32_t next = (w + 1) % WT_DGRAM_QUEUE_CAPACITY;

    if (next == q->read_idx) {
        WT_LOG_WARN("Datagram queue full, dropping (%d bytes)", length);
        wt_mutex_unlock(&q->mutex);
        return false;
    }

    wt_datagram_entry_t* entry = &q->entries[w];
    memcpy(entry->data, data, (size_t)length);
    entry->length = length;
    entry->conn_id = conn_id;
    entry->occupied = true;
    q->write_idx = next;

    wt_mutex_unlock(&q->mutex);
    return true;
}

int32_t wt_datagram_queue_drain(
    wt_datagram_queue_t* q,
    void (*cb)(void* ctx, wt_connection_id_t conn_id,
               const uint8_t* data, int32_t length),
    void* ctx)
{
    if (!cb) return 0;

    wt_mutex_lock(&q->mutex);

    int32_t count = 0;
    uint32_t r = q->read_idx;
    uint32_t w = q->write_idx;

    while (r != w) {
        wt_datagram_entry_t* entry = &q->entries[r];
        if (entry->occupied) {
            /* Copy to stack before unlocking — prevents producer from
             * overwriting entry data during the callback window. */
            uint8_t tmp[WT_DGRAM_MAX_SIZE];
            int32_t len = entry->length;
            wt_connection_id_t cid = entry->conn_id;
            memcpy(tmp, entry->data, len);
            entry->occupied = false;
            entry->length = 0;

            /* Advance read_idx before callback — prevents producer from
             * seeing a false-full queue while the callback is running.
             * This also updates r so we don't double-advance below. */
            r = (r + 1) % WT_DGRAM_QUEUE_CAPACITY;
            q->read_idx = r;

            wt_mutex_unlock(&q->mutex);
            cb(ctx, cid, tmp, len);
            wt_mutex_lock(&q->mutex);

            count++;
            /* Re-read write_idx — producer may have added entries
             * while the mutex was released during the callback. */
            w = q->write_idx;
        } else {
            r = (r + 1) % WT_DGRAM_QUEUE_CAPACITY;
            q->read_idx = r;
        }
    }

    wt_mutex_unlock(&q->mutex);
    return count;
}