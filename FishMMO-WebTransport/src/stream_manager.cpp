/**
 * @file stream_manager.cpp
 * @brief Manages QUIC streams for reliable data (FishNet channel 0).
 */

#include "stream_manager.h"
#include <stdlib.h>
#include <stddef.h>
#include <string.h>

/* ── Mutex helpers ────────────────────────────────────────────── */
#if defined(WT_PLATFORM_WINDOWS)

/* Manual recursive lock using CRITICAL_SECTION + thread tracking.
 * Windows CRITICAL_SECTION is NOT recursive; this wrapper provides
 * the same behaviour as PTHREAD_MUTEX_RECURSIVE on Linux so that a
 * synchronous MsQuic callback re-entering the lock does not deadlock. */
static void sm_lock_fn(wt_stream_manager_t* mgr)
{
    DWORD tid = GetCurrentThreadId();
    if (mgr->streams_lock_owner == tid) {
        mgr->streams_lock_rec++;
        return;
    }
    EnterCriticalSection(&mgr->streams_lock_cs);
    mgr->streams_lock_owner = tid;
    mgr->streams_lock_rec = 1;
}

static void sm_unlock_fn(wt_stream_manager_t* mgr)
{
    if (--mgr->streams_lock_rec == 0) {
        mgr->streams_lock_owner = 0;
        LeaveCriticalSection(&mgr->streams_lock_cs);
    }
}

#define sm_lock(mgr)    sm_lock_fn(mgr)
#define sm_unlock(mgr)  sm_unlock_fn(mgr)

#else /* Linux / macOS */

  #define sm_lock(mgr)    pthread_mutex_lock(&(mgr)->streams_lock)
  #define sm_unlock(mgr)  pthread_mutex_unlock(&(mgr)->streams_lock)

#endif

/* ── Per-stream context (attached to each QUIC stream) ──────── */

/* Heap send request: QUIC_BUFFER + payload must outlive StreamSend until
 * SEND_COMPLETE. Stack QUIC_BUFFER caused QuicStreamSendBufferRequest SIGSEGV. */
#define SM_SEND_REQ_MAGIC  0x534E4442u  /* 'SNDB' */

typedef struct sm_send_req_s {
    uint32_t    magic;
    int         freed;       /* 1 after free — detect double free */
    uint32_t    length;      /* payload length in data[] */
    QUIC_BUFFER buf;         /* buf.Buffer points at data[] */
    uint8_t     data[1];     /* flexible: actual size = length */
} sm_send_req_t;

typedef struct {
    wt_stream_manager_t* mgr;
    wt_stream_id_t       stream_id;
    HQUIC                quic_stream;
    /* Reassembly accumulator. Holds bytes received but not yet consumed by
     * a complete framed message (see WT_MAX_FRAMED_MESSAGE). Unlike the
     * pre-framing implementation this buffer PERSISTS across RECEIVE events:
     * a partial message stays here until its remaining bytes arrive. */
    uint8_t*             recv_buf;
    uint32_t             recv_offset;
    /* True after we have handled the optional WEBTRANSPORT_STREAM header
     * at the start of a browser data stream (or decided none is present). */
    bool                 header_checked;
    /* Set when the peer's framing is unrecoverable (oversized length, or a
     * zero-length message). The stream is aborted and no further data is
     * delivered — resynchronising a byte stream is not possible. */
    bool                 framing_error;
    /* Prevent double StreamClose (mgr shutdown race vs SHUTDOWN_COMPLETE). */
    bool                 close_done;
    /* Outbound payload waiting for START_COMPLETE before StreamSend.
     * NULL after ownership transfers to StreamSend ClientContext. */
    sm_send_req_t*       pending_send;
    /* 1 = send with QUIC_SEND_FLAG_FIN (one-shot); 0 = keep send open. */
    int                  pending_send_fin;
} stream_ctx_t;

static sm_send_req_t* sm_send_req_alloc(const uint8_t* header, size_t header_len,
                                        const uint8_t* data, int32_t length)
{
    if (length < 0) return NULL;
    size_t total = header_len + (size_t)length;
    /* sizeof(sm_send_req_t) already includes 1 byte of data[1] */
    size_t bytes = offsetof(sm_send_req_t, data) + total;
    sm_send_req_t* req = (sm_send_req_t*)malloc(bytes);
    if (!req) return NULL;
    req->magic = SM_SEND_REQ_MAGIC;
    req->freed = 0;
    req->length = (uint32_t)total;
    req->buf.Buffer = req->data;
    req->buf.Length = (uint32_t)total;
    if (header_len > 0 && header)
        memcpy(req->data, header, header_len);
    if (length > 0 && data)
        memcpy(req->data + header_len, data, (size_t)length);
    return req;
}

static void sm_send_req_free(void* client_ctx, const char* reason)
{
    sm_send_req_t* req = (sm_send_req_t*)client_ctx;
    if (!req) {
        WT_LOG_WARN("SEND free: null ClientContext (%s)", reason ? reason : "?");
        return;
    }
    if (req->magic != SM_SEND_REQ_MAGIC) {
        WT_LOG_ERROR("SEND free: bad magic 0x%x (%s) — not our buffer",
                     req->magic, reason ? reason : "?");
        return;
    }
    if (req->freed) {
        WT_LOG_ERROR("SEND free: DOUBLE FREE blocked len=%u (%s)",
                     req->length, reason ? reason : "?");
        return;
    }
    req->freed = 1;
    req->magic = 0;
    // WT_LOG_INFO("SEND_COMPLETE free ok len=%u (%s)",
    //             req->length, reason ? reason : "complete");
    free(req);
}

/* WEBTRANSPORT_STREAM capsule type (draft-ietf-webtrans-http3). */
#define WT_STREAM_CAPSULE_TYPE  0x41u

/* Minimal QUIC varint helpers (local to stream framing). */
static int sm_varint_decode(const uint8_t* buf, size_t len,
                            uint64_t* out_val, size_t* out_bytes)
{
    if (len < 1) return -1;
    uint8_t prefix = buf[0] >> 6;
    size_t need = 1u << prefix;
    if (len < need) return -1;
    uint64_t v = (uint64_t)(buf[0] & 0x3F);
    for (size_t i = 1; i < need; i++)
        v = (v << 8) | buf[i];
    *out_val = v;
    *out_bytes = need;
    return 0;
}

static size_t sm_varint_encode(uint64_t val, uint8_t* out)
{
    if (val < 64) {
        out[0] = (uint8_t)val;
        return 1;
    }
    if (val < 16384) {
        out[0] = (uint8_t)(0x40 | (val >> 8));
        out[1] = (uint8_t)(val & 0xFF);
        return 2;
    }
    if (val < 1073741824ull) {
        out[0] = (uint8_t)(0x80 | (val >> 24));
        out[1] = (uint8_t)((val >> 16) & 0xFF);
        out[2] = (uint8_t)((val >> 8) & 0xFF);
        out[3] = (uint8_t)(val & 0xFF);
        return 4;
    }
    out[0] = (uint8_t)(0xC0 | ((val >> 56) & 0x3F));
    out[1] = (uint8_t)((val >> 48) & 0xFF);
    out[2] = (uint8_t)((val >> 40) & 0xFF);
    out[3] = (uint8_t)((val >> 32) & 0xFF);
    out[4] = (uint8_t)((val >> 24) & 0xFF);
    out[5] = (uint8_t)((val >> 16) & 0xFF);
    out[6] = (uint8_t)((val >> 8) & 0xFF);
    out[7] = (uint8_t)(val & 0xFF);
    return 8;
}

/**
 * If buf starts with WEBTRANSPORT_STREAM (type 0x41 as QUIC varint), skip it.
 *
 * Wire format (draft-ietf-webtrans-http3 §4.2, bidirectional streams):
 *   Type (i) = 0x41          ← QUIC varint; 65 does not fit the 1-byte form
 *                              (0..63), so it is always the two bytes
 *                              0x40 0x41 on the wire
 *   Session ID (i)           ← the CONNECT request stream ID
 *   … Stream Data (app)      ← no length field at this layer
 *
 * Production bug: first=40 41 00 11 was delivered whole because we only
 * matched buf[0]==0x41 and ignored 2-byte varint type encoding → FishNet
 * saw header garbage → disconnect → unsafe shutdown → quic_bugcheck.
 *
 * @param expect_session_id  The session's CONNECT stream ID. A header naming
 *                           any other session belongs to a WebTransport
 *                           session we are not part of; per the draft it must
 *                           not be treated as ours, so it is reported as a
 *                           mismatch rather than silently accepted.
 * @param incomplete  Set when the type or session-id varint is truncated —
 *                    the caller must wait for more bytes, not decide.
 * @param mismatch    Set when a well-formed header names a different session.
 * @return byte offset of application data (0 if this is not a WT header).
 */
static size_t sm_skip_wt_stream_header(const uint8_t* buf, size_t len,
                                       uint64_t expect_session_id,
                                       bool* incomplete, bool* mismatch)
{
    *incomplete = false;
    *mismatch = false;
    if (len < 1) { *incomplete = true; return 0; }

    uint64_t type = 0;
    size_t type_n = 0;
    if (sm_varint_decode(buf, len, &type, &type_n) < 0) {
        *incomplete = true;
        return 0;
    }
    if (type != WT_STREAM_CAPSULE_TYPE)
        return 0;

    size_t off = type_n;
    uint64_t session_id = 0;
    size_t sid_n = 0;
    if (sm_varint_decode(buf + off, len - off, &session_id, &sid_n) < 0) {
        *incomplete = true;
        return 0;
    }
    off += sid_n;

    if (session_id != expect_session_id) {
        WT_LOG_WARN(
            "WEBTRANSPORT_STREAM header names session %llu, expected %llu",
            (unsigned long long)session_id,
            (unsigned long long)expect_session_id);
        *mismatch = true;
        return 0;
    }
    return off;
}

/* ── Reliable-channel message framing ───────────────────────────
 * Every application message on a stream is preceded by its length as a
 * QUIC varint. See WT_MAX_FRAMED_MESSAGE in webtransport_internal.h for
 * the wire format and why it is required.
 *
 * Encoding a length costs 1 byte below 64, 2 below 16384 — FishNet
 * bundles are MTU-bounded, so in practice this is 2 bytes per message. */

/**
 * Consume every complete framed message sitting in sctx->recv_buf and hand
 * each one to the application, then compact the buffer so a partial trailing
 * message is retained for the next RECEIVE event.
 *
 * This is the whole point of the framing layer: msquic delivers whatever
 * bytes have arrived, which may be several messages at once, half a message,
 * or a message split across many events. Only whole messages are delivered
 * upward.
 *
 * @return true to continue, false if the peer's framing is broken and the
 *         caller must abort the stream.
 */
static bool sm_drain_framed_messages(stream_ctx_t* sctx)
{
    wt_stream_manager_t* mgr = sctx->mgr;
    size_t consumed = 0;
    const size_t avail = sctx->recv_offset;

    while (consumed < avail) {
        uint64_t msg_len = 0;
        size_t len_n = 0;
        if (sm_varint_decode(sctx->recv_buf + consumed, avail - consumed,
                             &msg_len, &len_n) < 0)
            break;  /* length varint itself is not fully here yet */

        if (msg_len == 0 || msg_len > WT_MAX_FRAMED_MESSAGE) {
            /* A byte stream cannot be resynchronised once the length field
             * is wrong — every subsequent offset is derived from it. Fail
             * the stream rather than deliver shifted garbage upward. */
            WT_LOG_ERROR(
                "Stream %llu: invalid framed message length %llu "
                "(max %d) — aborting stream",
                (unsigned long long)sctx->stream_id,
                (unsigned long long)msg_len,
                (int)WT_MAX_FRAMED_MESSAGE);
            sctx->framing_error = true;
            return false;
        }

        if (avail - consumed - len_n < msg_len)
            break;  /* payload incomplete — wait for the rest */

        consumed += len_n;

        if (mgr->on_stream_data && mgr->callback_ctx) {
            mgr->on_stream_data(
                mgr->callback_ctx,
                mgr->conn_id,
                sctx->stream_id,
                sctx->recv_buf + consumed,
                (int32_t)msg_len);
        }
        consumed += (size_t)msg_len;
    }

    if (consumed > 0) {
        size_t remaining = avail - consumed;
        if (remaining > 0) {
            /* Retain the partial message at the front of the buffer. */
            memmove(sctx->recv_buf, sctx->recv_buf + consumed, remaining);
        }
        sctx->recv_offset = (uint32_t)remaining;
        atomic_fetch_sub(&mgr->total_recv_bytes, (uint32_t)consumed);
    }
    return true;
}

/* ── Forward ────────────────────────────────────────────────── */

static QUIC_STATUS stream_cb(HQUIC stream, void* ctx,
                               QUIC_STREAM_EVENT* event);
static void sm_mark_send_closed(
    wt_stream_manager_t* mgr, wt_stream_id_t stream_id, HQUIC quic_stream);

/* ── Stream callback (function pointer type) ────────────────── */

static QUIC_STREAM_CALLBACK_HANDLER k_stream_handler = stream_cb;

/* ── Callback implementation ────────────────────────────────── */

static QUIC_STATUS QUIC_API
stream_cb(HQUIC stream, void* ctx, QUIC_STREAM_EVENT* event)
{
    stream_ctx_t* sctx = (stream_ctx_t*)ctx;
    if (!sctx || !sctx->mgr || !event) {
        /* Never bugcheck the process — drop the event. */
        WT_LOG_ERROR("stream_cb: null sctx/mgr/event — ignoring");
        return QUIC_STATUS_INVALID_STATE;
    }

    switch (event->Type) {

    case QUIC_STREAM_EVENT_RECEIVE: {
        const QUIC_BUFFER* bufs = event->RECEIVE.Buffers;
        uint32_t count = event->RECEIVE.BufferCount;
        uint32_t total  = event->RECEIVE.TotalBufferLength;

        if (total == 0) return QUIC_STATUS_SUCCESS;

        /* Per-stream bound check — prevent OOM from unbounded receive buffer. */
        if (total > WT_MAX_STREAM_RECV_BUF ||
            sctx->recv_offset > WT_MAX_STREAM_RECV_BUF - total) {
            MsQuic->StreamShutdown(stream,
                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
            return QUIC_STATUS_ABORTED;
        }

        {
            uint32_t prev_total = atomic_fetch_add(
                &sctx->mgr->total_recv_bytes, total);
            if (prev_total > WT_MAX_TOTAL_RECV_BUF - total) {
                atomic_fetch_sub(&sctx->mgr->total_recv_bytes, total);
                MsQuic->StreamShutdown(stream,
                                        QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                return QUIC_STATUS_ABORTED;
            }
        }

        if (sctx->framing_error)
            return QUIC_STATUS_ABORTED;

        /* Append into recv_buf. The buffer persists across events — it holds
         * any partial WT header and any partial framed message. */
        uint32_t needed = sctx->recv_offset + total;
        uint8_t* newbuf = (uint8_t*)realloc(sctx->recv_buf, needed);
        if (!newbuf) {
            atomic_fetch_sub(&sctx->mgr->total_recv_bytes, total);
            return QUIC_STATUS_OUT_OF_MEMORY;
        }
        sctx->recv_buf = newbuf;

        for (uint32_t i = 0; i < count; i++) {
            memcpy(sctx->recv_buf + sctx->recv_offset,
                   bufs[i].Buffer, bufs[i].Length);
            sctx->recv_offset += bufs[i].Length;
        }

        /* ── One-time WEBTRANSPORT_STREAM header strip ──────────────
         * Browser sessions open each data stream with type 0x41 + Session
         * ID. It appears exactly once, before the first framed message. */
        if (!sctx->header_checked) {
            if (sctx->mgr->use_wt_stream_header) {
                bool incomplete = false, mismatch = false;
                size_t skip = sm_skip_wt_stream_header(
                    sctx->recv_buf, sctx->recv_offset,
                    sctx->mgr->wt_session_id, &incomplete, &mismatch);

                if (incomplete)
                    return QUIC_STATUS_SUCCESS;  /* wait for the rest */

                if (mismatch) {
                    sctx->framing_error = true;
                    MsQuic->StreamShutdown(stream,
                                            QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                    return QUIC_STATUS_ABORTED;
                }

                if (skip > 0) {
                    size_t remaining = sctx->recv_offset - skip;
                    if (remaining > 0)
                        memmove(sctx->recv_buf, sctx->recv_buf + skip, remaining);
                    sctx->recv_offset = (uint32_t)remaining;
                    atomic_fetch_sub(&sctx->mgr->total_recv_bytes,
                                     (uint32_t)skip);
                    // WT_LOG_INFO(
                    //     "Stream %llu: stripped WEBTRANSPORT_STREAM header "
                    //     "(%zu bytes, session_id=%llu)",
                    //     (unsigned long long)sctx->stream_id, skip,
                    //     (unsigned long long)sctx->mgr->wt_session_id);
                }
            }
            sctx->header_checked = true;
        }

        /* Deliver every complete message; retain any partial tail. */
        if (!sm_drain_framed_messages(sctx)) {
            atomic_fetch_sub(&sctx->mgr->total_recv_bytes, sctx->recv_offset);
            free(sctx->recv_buf);
            sctx->recv_buf = NULL;
            sctx->recv_offset = 0;
            MsQuic->StreamShutdown(stream,
                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
            return QUIC_STATUS_ABORTED;
        }
        return QUIC_STATUS_SUCCESS;
    }

    case QUIC_STREAM_EVENT_PEER_SEND_SHUTDOWN: {
        /* Peer finished sending. Deliver any residual buffer.
         *
         * Do NOT StreamShutdown our send side here. On a bidi stream the peer
         * FIN only means they will not write more; FishNet clients still need
         * to send subsequent reliable packets on their own open streams.
         * Auto-graceful-shutdown left send_closed=false while the handle was
         * no longer writable → StreamSend INVALID_STATE (0x8007139f) and
         * later msquic worker access violations after login. */
        if (sctx->recv_buf && sctx->recv_offset > 0 && !sctx->framing_error) {
            if (!sctx->header_checked && sctx->mgr->use_wt_stream_header) {
                bool incomplete = false, mismatch = false;
                size_t skip = sm_skip_wt_stream_header(
                    sctx->recv_buf, sctx->recv_offset,
                    sctx->mgr->wt_session_id, &incomplete, &mismatch);
                if (!incomplete && !mismatch && skip > 0) {
                    size_t remaining = sctx->recv_offset - skip;
                    if (remaining > 0)
                        memmove(sctx->recv_buf, sctx->recv_buf + skip, remaining);
                    sctx->recv_offset = (uint32_t)remaining;
                    atomic_fetch_sub(&sctx->mgr->total_recv_bytes,
                                     (uint32_t)skip);
                }
                sctx->header_checked = true;
            }

            /* Deliver whatever whole messages are still buffered. Anything
             * left after this is a message the peer began but never finished
             * before closing its send side — it is incomplete by definition
             * and must be dropped rather than delivered truncated. */
            sm_drain_framed_messages(sctx);
            if (sctx->recv_offset > 0) {
                WT_LOG_WARN(
                    "Stream %llu: peer FIN with %u trailing bytes of an "
                    "incomplete message — discarding",
                    (unsigned long long)sctx->stream_id,
                    (unsigned)sctx->recv_offset);
            }
        }

        atomic_fetch_sub(&sctx->mgr->total_recv_bytes, sctx->recv_offset);
        free(sctx->recv_buf);
        sctx->recv_buf = NULL;
        sctx->recv_offset = 0;

        sm_lock(sctx->mgr);
        for (int i = 0; i < WT_MAX_STREAMS; i++) {
            if (sctx->mgr->streams[i].id == sctx->stream_id) {
                sctx->mgr->streams[i].recv_closed = true;
                break;
            }
        }
        sm_unlock(sctx->mgr);
        return QUIC_STATUS_SUCCESS;
    }

    case QUIC_STREAM_EVENT_START_COMPLETE: {
        /*
         * CRITICAL: never StreamSend until START_COMPLETE.
         * Sending immediately after StreamStart races the msquic worker and
         * hits QuicStreamSendBufferRequest on invalid stream state (SIGSEGV).
         * Match http3.cpp send-only pattern: queue payload, flush here.
         */
        if (event->START_COMPLETE.Status != 0 &&
            QUIC_FAILED(event->START_COMPLETE.Status)) {
            WT_LOG_ERROR(
                "Stream START_COMPLETE failed st=0x%x stream_id=%llu",
                event->START_COMPLETE.Status,
                (unsigned long long)sctx->stream_id);
            if (sctx->pending_send) {
                sm_send_req_free(sctx->pending_send, "start_failed");
                sctx->pending_send = NULL;
            }
            if (!sctx->close_done) {
                MsQuic->StreamShutdown(
                    stream,
                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT |
                        QUIC_STREAM_SHUTDOWN_FLAG_IMMEDIATE,
                    0);
            }
            return QUIC_STATUS_SUCCESS;
        }

        sm_send_req_t* req = sctx->pending_send;
        if (!req) {
            // WT_LOG_INFO(
            //     "Stream START_COMPLETE stream_id=%llu (no pending send)",
            //     (unsigned long long)sctx->stream_id);
            return QUIC_STATUS_SUCCESS;
        }
        /* Transfer ownership to StreamSend ClientContext before call. */
        sctx->pending_send = NULL;
        QUIC_SEND_FLAGS send_flags = sctx->pending_send_fin
            ? QUIC_SEND_FLAG_FIN
            : QUIC_SEND_FLAG_NONE;

        /* Capture anything we want to log BEFORE the call: msquic may complete
         * the send synchronously, and our SEND_COMPLETE handler frees req.
         * Reading req->length afterwards is a use-after-free. */
        const uint32_t req_length = req->length;

        QUIC_STATUS st = MsQuic->StreamSend(
            stream,
            &req->buf,
            1,
            send_flags,
            req);
        if (QUIC_FAILED(st)) {
            WT_LOG_ERROR(
                "StreamSend failed after START_COMPLETE st=0x%x "
                "stream_id=%llu len=%u fin=%d — free now (no SEND_COMPLETE)",
                st,
                (unsigned long long)sctx->stream_id,
                req_length,
                sctx->pending_send_fin ? 1 : 0);
            sm_send_req_free(req, "send_failed");
            sm_mark_send_closed(sctx->mgr, sctx->stream_id, stream);
            if (!sctx->close_done) {
                MsQuic->StreamShutdown(
                    stream,
                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT |
                        QUIC_STREAM_SHUTDOWN_FLAG_IMMEDIATE,
                    0);
            }
            return QUIC_STATUS_SUCCESS;
        }

        // WT_LOG_INFO(
        //     "StreamSend queued after START_COMPLETE stream_id=%llu len=%u "
        //     "fin=%d stamp=SEND_AFTER_START_V2 (free only on SEND_COMPLETE)",
        //     (unsigned long long)sctx->stream_id,
        //     req_length,
        //     sctx->pending_send_fin ? 1 : 0);
        return QUIC_STATUS_SUCCESS;
    }

    case QUIC_STREAM_EVENT_SEND_COMPLETE:
        /* Exactly one free per successful StreamSend ClientContext. */
        if (event->SEND_COMPLETE.Canceled) {
            WT_LOG_WARN(
                "SEND_COMPLETE canceled stream_id=%llu — free buffer",
                (unsigned long long)sctx->stream_id);
        }
        sm_send_req_free(event->SEND_COMPLETE.ClientContext,
                         event->SEND_COMPLETE.Canceled ? "canceled" : "ok");
        return QUIC_STATUS_SUCCESS;

    case QUIC_STREAM_EVENT_PEER_SEND_ABORTED:
        atomic_fetch_sub(&sctx->mgr->total_recv_bytes, sctx->recv_offset);
        free(sctx->recv_buf);
        sctx->recv_buf = NULL;
        sctx->recv_offset = 0;
        /* Peer aborted — mark both directions unusable so send will not
         * reuse this handle (StreamSend would return INVALID_STATE). */
        sm_lock(sctx->mgr);
        for (int i = 0; i < WT_MAX_STREAMS; i++) {
            if (sctx->mgr->streams[i].id == sctx->stream_id ||
                sctx->mgr->streams[i].quic_stream == stream) {
                sctx->mgr->streams[i].recv_closed = true;
                sctx->mgr->streams[i].send_closed = true;
                break;
            }
        }
        sm_unlock(sctx->mgr);
        if (!sctx->close_done) {
            MsQuic->StreamShutdown(stream,
                                    QUIC_STREAM_SHUTDOWN_FLAG_GRACEFUL, 0);
        }
        return QUIC_STATUS_SUCCESS;

    case QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE: {
        wt_stream_manager_t* mgr = sctx->mgr;

        /* Free unsent pending buffer (never reached StreamSend). */
        if (sctx->pending_send) {
            sm_send_req_free(sctx->pending_send, "shutdown_before_send");
            sctx->pending_send = NULL;
        }

        /* Clear the slot under lock — concurrent with send/accept/mgr shutdown. */
        int found_slot = 0;
        sm_lock(mgr);
        for (int i = 0; i < WT_MAX_STREAMS; i++) {
            if (mgr->streams[i].id == sctx->stream_id ||
                mgr->streams[i].quic_stream == stream) {
                mgr->streams[i].in_use = false;
                mgr->streams[i].quic_stream = NULL;
                mgr->streams[i].id = 0;
                mgr->streams[i].peer_initiated = false;
                mgr->streams[i].send_closed = false;
                mgr->streams[i].recv_closed = false;
                found_slot = 1;
                break;
            }
        }
        /* Only decrement if we still own accounting (slot found OR active>0).
         * Avoid underflow when mgr shutdown already zeroed active on conn_dead. */
        uint32_t prev = 0;
        if (found_slot || atomic_load(&mgr->active_streams) > 0)
            prev = atomic_fetch_sub(&mgr->active_streams, 1);
        sm_unlock(mgr);

        atomic_fetch_sub(&mgr->total_recv_bytes, sctx->recv_offset);
        free(sctx->recv_buf);
        sctx->recv_buf = NULL;

        /* Never StreamClose once ConnectionClose has run (handles invalid).
         * Gated on handles_invalid, not conn_closed: during a peer-initiated
         * shutdown conn_closed is already set while the handle is still valid,
         * and skipping the close there leaks it. */
        if (!sctx->close_done && !atomic_load(&mgr->handles_invalid)) {
            sctx->close_done = true;
            MsQuic->StreamClose(stream);
        } else {
            sctx->close_done = true;
        }
        free(sctx);

        if (prev == 1 && atomic_load(&mgr->shutting_down) && mgr->on_all_streams_done) {
            mgr->on_all_streams_done(mgr->done_ctx);
        }
        return QUIC_STATUS_SUCCESS;
    }

    default:
        return QUIC_STATUS_SUCCESS;
    }
}

/* ── Stream manager API ─────────────────────────────────────── */

void wt_stream_manager_init(
    wt_stream_manager_t* mgr, HQUIC quic_conn,
    wt_connection_id_t conn_id,
    void (*on_stream_data)(void* ctx, wt_connection_id_t conn_id,
                           wt_stream_id_t stream_id,
                           const uint8_t* data, int32_t length),
    void* callback_ctx)
{
    memset(mgr, 0, sizeof(*mgr));
    mgr->quic_conn = quic_conn;
    mgr->conn_id = conn_id;
    mgr->on_stream_data = on_stream_data;
    mgr->callback_ctx = callback_ctx;
    mgr->next_id = 1;
    atomic_init(&mgr->active_streams, 0);
    atomic_init(&mgr->streams_done_flag, false);
    atomic_init(&mgr->shutdown_complete, false);
    atomic_init(&mgr->freed, false);
    atomic_store(&mgr->shutting_down, false);
    atomic_store(&mgr->conn_closed, false);
    atomic_store(&mgr->handles_invalid, false);
    atomic_init(&mgr->total_recv_bytes, 0);

#if defined(WT_PLATFORM_WINDOWS)
    InitializeCriticalSection(&mgr->streams_lock_cs);
    mgr->streams_lock_owner = 0;
    mgr->streams_lock_rec = 0;
#else
    {
        pthread_mutexattr_t attr;
        pthread_mutexattr_init(&attr);
        pthread_mutexattr_settype(&attr, PTHREAD_MUTEX_RECURSIVE);
        pthread_mutex_init(&mgr->streams_lock, &attr);
        pthread_mutexattr_destroy(&attr);
    }
#endif

    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        mgr->streams[i].id = 0;
        mgr->streams[i].in_use = false;
        mgr->streams[i].peer_initiated = false;
        mgr->streams[i].send_closed = false;
        mgr->streams[i].recv_closed = false;
        mgr->streams[i].quic_stream = NULL;
    }

}

void wt_stream_manager_mark_conn_closed(wt_stream_manager_t* mgr)
{
    if (!mgr) return;
    atomic_store(&mgr->conn_closed, true);
}

void wt_stream_manager_mark_handles_invalid(wt_stream_manager_t* mgr)
{
    if (!mgr) return;
    atomic_store(&mgr->handles_invalid, true);
}

void wt_stream_manager_close_streams(wt_stream_manager_t* mgr)
{
    if (!mgr) return;

    /* Claim every live handle under the lock, then close outside it —
     * StreamClose can deliver a final SHUTDOWN_COMPLETE synchronously, and
     * that callback takes this same (non-recursive on some platforms) lock. */
    HQUIC claimed[WT_MAX_STREAMS];
    int count = 0;

    sm_lock(mgr);
    for (int i = 0; i < WT_MAX_STREAMS && count < WT_MAX_STREAMS; i++) {
        if (!mgr->streams[i].in_use) continue;
        HQUIC h = mgr->streams[i].quic_stream;
        if (h) claimed[count++] = h;
        mgr->streams[i].in_use = false;
        mgr->streams[i].quic_stream = NULL;
        mgr->streams[i].id = 0;
        mgr->streams[i].peer_initiated = false;
        mgr->streams[i].send_closed = false;
        mgr->streams[i].recv_closed = false;
    }
    sm_unlock(mgr);

    if (count > 0) {
        WT_LOG_INFO("wt_stream_manager_close_streams conn=%llu closing %d handle(s)",
                    (unsigned long long)mgr->conn_id, count);
    }
    for (int i = 0; i < count; i++) {
        MsQuic->StreamClose(claimed[i]);
    }
}

void wt_stream_manager_shutdown(wt_stream_manager_t* mgr)
{
    if (!mgr) return;

    /*
     * SAFE_SHUTDOWN_V2 (2026-07-25):
     *
     * Production crash (LoginServer rc=134 / SIGABRT):
     *   PEER H3_FRAME_UNEXPECTED → connection SHUTDOWN_COMPLETE →
     *   ConnectionClose → poll drains pending session →
     *   wt_stream_manager_shutdown → MsQuicStreamShutdown → quic_bugcheck
     *
     * After the parent connection is closed, stream handles are invalid.
     * Calling StreamShutdown/StreamClose on them aborts the process.
     *
     * Rules:
     *  - If handles_invalid: only clear local slots; never touch MsQuic.
     *  - If connection still live: graceful StreamShutdown only (no
     *    ABORT|IMMEDIATE combo — that was the assert path).
     *  - StreamClose remains owned by SHUTDOWN_COMPLETE in stream_cb.
     */
    const int conn_dead = atomic_load(&mgr->handles_invalid) ? 1 : 0;

    HQUIC* pending = NULL;
    int pending_count = 0;

    if (!conn_dead) {
        pending = (HQUIC*)malloc(sizeof(HQUIC) * (size_t)WT_MAX_STREAMS);
        if (!pending) {
            WT_LOG_ERROR(
                "wt_stream_manager_shutdown: malloc failed — skip MsQuic "
                "ops (conn=%llu active=%u) stamp=SAFE_SHUTDOWN_V2",
                (unsigned long long)mgr->conn_id,
                (unsigned)atomic_load(&mgr->active_streams));
        }
    }

    sm_lock(mgr);
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        if (!mgr->streams[i].in_use)
            continue;
        HQUIC h = mgr->streams[i].quic_stream;
        if (h && pending && pending_count < WT_MAX_STREAMS) {
            pending[pending_count++] = h;
        }
        /* Drop ownership so concurrent SHUTDOWN_COMPLETE cannot double-op. */
        mgr->streams[i].quic_stream = NULL;
        if (conn_dead) {
            /* Connection already gone — bookkeeping only; MsQuic owns teardown. */
            mgr->streams[i].in_use = false;
            mgr->streams[i].id = 0;
            mgr->streams[i].peer_initiated = false;
            mgr->streams[i].send_closed = false;
            mgr->streams[i].recv_closed = false;
        }
    }
    if (conn_dead) {
        atomic_store(&mgr->active_streams, 0);
    }
    sm_unlock(mgr);

    WT_LOG_INFO(
        "wt_stream_manager_shutdown conn=%llu streams_to_abort=%d active=%u "
        "conn_dead=%d stamp=SAFE_SHUTDOWN_V2",
        (unsigned long long)mgr->conn_id,
        pending_count,
        (unsigned)atomic_load(&mgr->active_streams),
        conn_dead);

    if (conn_dead) {
        /* Fire done callback so session free is not stuck waiting. */
        if (mgr->on_all_streams_done)
            mgr->on_all_streams_done(mgr->done_ctx);
        free(pending);
        return;
    }

    for (int i = 0; i < pending_count; i++) {
        if (!pending[i])
            continue;
        /* Graceful only — never ABORT|IMMEDIATE after peer close races. */
        MsQuic->StreamShutdown(
            pending[i],
            QUIC_STREAM_SHUTDOWN_FLAG_GRACEFUL,
            0);
        /* SHUTDOWN_COMPLETE (stream_cb) owns StreamClose + sctx free. */
    }
    free(pending);
}

/* Mark a stream unusable for further StreamSend after a hard failure. */
static void sm_mark_send_closed(
    wt_stream_manager_t* mgr, wt_stream_id_t stream_id, HQUIC quic_stream)
{
    if (!mgr) return;
    sm_lock(mgr);
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        if (!mgr->streams[i].in_use)
            continue;
        if ((stream_id != 0 && mgr->streams[i].id == stream_id) ||
            (quic_stream && mgr->streams[i].quic_stream == quic_stream)) {
            mgr->streams[i].send_closed = true;
            break;
        }
    }
    sm_unlock(mgr);
}

/**
 * Send on an already-started stream (peer-initiated preferred path on server).
 *
 * No WEBTRANSPORT_STREAM header: that appears once, when the stream is
 * opened, and this stream is already open. The message still carries its
 * length prefix — every message on a stream does, or the peer cannot find
 * its boundaries.
 *
 * Does NOT set FIN so the peer can keep writing on the same stream.
 */
static int32_t sm_send_on_open_stream(
    wt_stream_manager_t* mgr,
    HQUIC quic_stream,
    wt_stream_id_t stream_id,
    const uint8_t* data,
    int32_t length)
{
    uint8_t len_hdr[8];
    size_t len_n = sm_varint_encode((uint64_t)length, len_hdr);

    sm_send_req_t* req = sm_send_req_alloc(len_hdr, len_n, data, length);
    if (!req)
        return WT_ERR_BUFFER_FULL;

    /* Bail if connection already dead — avoids StreamSend into free'd msquic state. */
    if (atomic_load(&mgr->conn_closed) || atomic_load(&mgr->shutting_down)) {
        sm_send_req_free(req, "same_stream_conn_dead");
        sm_mark_send_closed(mgr, stream_id, quic_stream);
        return WT_ERR_INVALID_STATE;
    }

    QUIC_STATUS st = MsQuic->StreamSend(
        quic_stream,
        &req->buf,
        1,
        QUIC_SEND_FLAG_NONE, /* keep send side open for further replies */
        req);
    if (QUIC_FAILED(st)) {
        WT_LOG_ERROR(
            "StreamSend on existing stream failed st=0x%x conn=%llu "
            "stream_id=%llu len=%d stamp=SAME_STREAM_REPLY_V2",
            st,
            (unsigned long long)mgr->conn_id,
            (unsigned long long)stream_id,
            length);
        sm_send_req_free(req, "same_stream_send_failed");
        /* CRITICAL: never retry this handle. INVALID_STATE (0x8007139f) means
         * the send side is already shut down; reusing it crashes msquic workers. */
        sm_mark_send_closed(mgr, stream_id, quic_stream);
        return WT_ERR_SEND_FAILED;
    }

    // WT_LOG_INFO(
    //     "SAME_STREAM_REPLY ok conn=%llu stream_id=%llu app_len=%d "
    //     "stamp=SAME_STREAM_REPLY_V2 (no new StreamOpen; no FIN)",
    //     (unsigned long long)mgr->conn_id,
    //     (unsigned long long)stream_id,
    //     length);
    return WT_OK;
}

int32_t wt_stream_manager_send(
    wt_stream_manager_t* mgr, const uint8_t* data, int32_t length)
{
    if (!data || length <= 0) return WT_ERR_SEND_FAILED;
    if (length > WT_MAX_FRAMED_MESSAGE) {
        /* The receiver rejects anything larger, so failing here gives the
         * caller a usable error instead of a stream abort on the far side. */
        WT_LOG_ERROR(
            "Stream send rejected: len=%d exceeds WT_MAX_FRAMED_MESSAGE=%d",
            length, (int)WT_MAX_FRAMED_MESSAGE);
        return WT_ERR_SEND_FAILED;
    }
    if (atomic_load(&mgr->shutting_down)) return WT_ERR_INVALID_STATE;
    if (atomic_load(&mgr->conn_closed)) return WT_ERR_INVALID_STATE;

    /*
     * Stream reuse policy (stamp=STREAM_REUSE_POLICY_V2):
     *
     * Server:
     *   Prefer peer-initiated bidi streams (client-opened) so browser/native
     *   clients receive replies on the stream they already own. Chrome rejects
     *   server StreamOpen + WEBTRANSPORT_STREAM (H3_FRAME_UNEXPECTED).
     *
     * Client (native Editor/standalone):
     *   Only reuse locally-opened streams (!peer_initiated). Writing on a
     *   server-initiated stream after the peer FINs yields StreamSend
     *   INVALID_STATE and can AV inside msquic. If none available, open a
     *   new client-initiated stream (fallback below).
     */
    {
        HQUIC reuse = NULL;
        wt_stream_id_t reuse_id = 0;
        sm_lock(mgr);
        for (int i = 0; i < WT_MAX_STREAMS; i++) {
            if (!mgr->streams[i].in_use || !mgr->streams[i].quic_stream)
                continue;
            if (mgr->streams[i].send_closed)
                continue;

            if (mgr->is_server) {
                /* Prefer peer-initiated; fall back to any open send side. */
                if (mgr->streams[i].peer_initiated || reuse == NULL) {
                    reuse = mgr->streams[i].quic_stream;
                    reuse_id = mgr->streams[i].id;
                    if (mgr->streams[i].peer_initiated)
                        break;
                }
            } else {
                /* Client: never reuse peer-initiated (server-opened) streams. */
                if (mgr->streams[i].peer_initiated)
                    continue;
                /* Prefer the first locally-owned open stream. */
                reuse = mgr->streams[i].quic_stream;
                reuse_id = mgr->streams[i].id;
                break;
            }
        }
        sm_unlock(mgr);

        if (reuse) {
            int32_t r = sm_send_on_open_stream(
                mgr, reuse, reuse_id, data, length);
            if (r == WT_OK)
                return WT_OK;
            WT_LOG_WARN(
                "SAME_STREAM_REPLY failed conn=%llu stream_id=%llu st=%d "
                "is_server=%d stamp=SAME_STREAM_REPLY_V2",
                (unsigned long long)mgr->conn_id,
                (unsigned long long)reuse_id,
                (int)r,
                mgr->is_server ? 1 : 0);
            /* Falls through to opening a new local stream.
             *
             * This used to refuse outright for browser sessions, because
             * Chrome answered a server-opened stream with H3_FRAME_UNEXPECTED
             * and tore the session down. The cause was the header this code
             * wrote, not the act of opening a stream: the WEBTRANSPORT_STREAM
             * frame type went out as the single byte 0x41, which is not a
             * valid varint for 65 and decodes as frame type 0x0104. With the
             * type encoded correctly (0x40 0x41) a server-initiated stream is
             * exactly what draft-ietf-webtrans-http3 §4.2 describes, and
             * refusing to open one left the server unable to send anything at
             * all to a client that had not spoken first. */
            WT_LOG_WARN(
                "Stream reuse failed conn=%llu — opening a new local stream",
                (unsigned long long)mgr->conn_id);
        }
    }

    /* ── Fallback: open a new locally-initiated bidi stream ── */
    int slot = -1;
    sm_lock(mgr);
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        if (!mgr->streams[i].in_use) { slot = i; break; }
    }
    if (slot < 0) { sm_unlock(mgr); return WT_ERR_BUFFER_FULL; }

    wt_stream_id_t stream_id = mgr->next_id++;
    if (stream_id == 0) {
        stream_id = 1;
        mgr->next_id = WT_MAX_STREAMS + 2;
        for (;;) {
            bool conflict = false;
            for (int i = 0; i < WT_MAX_STREAMS; i++) {
                if (mgr->streams[i].in_use && mgr->streams[i].id == stream_id) {
                    conflict = true;
                    break;
                }
            }
            if (!conflict) break;
            if (++stream_id == 0) stream_id = 1;
        }
    }
    mgr->streams[slot].id = stream_id;
    mgr->streams[slot].in_use = true;
    mgr->streams[slot].send_closed = false;
    mgr->streams[slot].recv_closed = false;
    mgr->streams[slot].peer_initiated = false;
    atomic_fetch_add(&mgr->active_streams, 1);
    sm_unlock(mgr);

    stream_ctx_t* sctx = (stream_ctx_t*)calloc(1, sizeof(stream_ctx_t));
    if (!sctx) {
        sm_lock(mgr);
        mgr->streams[slot].in_use = false;
        mgr->streams[slot].id = 0;
        atomic_fetch_sub(&mgr->active_streams, 1);
        sm_unlock(mgr);
        return WT_ERR_BUFFER_FULL;
    }
    sctx->mgr = mgr;
    sctx->stream_id = stream_id;
    sctx->quic_stream = NULL;
    sctx->header_checked = true; /* outbound: no inbound WT capsule to strip */

    HQUIC quic_stream = NULL;
    QUIC_STATUS status = MsQuic->StreamOpen(
        mgr->quic_conn,
        QUIC_STREAM_OPEN_FLAG_NONE,
        k_stream_handler,
        sctx,
        &quic_stream);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR(
            "StreamOpen failed: 0x%x (conn=%llu stream_id=%llu) — "
            "ServerHandshake/app reply cannot leave host",
            status,
            (unsigned long long)mgr->conn_id,
            (unsigned long long)stream_id);
        free(sctx);
        sm_lock(mgr);
        mgr->streams[slot].in_use = false;
        mgr->streams[slot].id = 0;
        atomic_fetch_sub(&mgr->active_streams, 1);
        sm_unlock(mgr);
        return WT_ERR_SEND_FAILED;
    }
    sctx->quic_stream = quic_stream;

    sm_lock(mgr);
    mgr->streams[slot].quic_stream = quic_stream;
    sm_unlock(mgr);

    /* Prefix layout for the first write on a stream we opened:
     *   [WEBTRANSPORT_STREAM type + Session ID]   (browser sessions only)
     *   [message length varint]                   (always) */
    size_t header_len = 0;
    uint8_t header[8 + 8 + 8];
    if (mgr->use_wt_stream_header) {
        /* The frame type is a QUIC varint, and 0x41 (65) does not fit the
         * 1-byte form — that form only encodes 0..63. Writing the raw byte
         * 0x41 produces a 2-byte varint whose value is 0x0104, an unknown
         * frame type that a browser rejects with H3_FRAME_UNEXPECTED. Encode
         * it properly: 0x41 becomes the two bytes 0x40 0x41. */
        header_len = sm_varint_encode(WT_STREAM_CAPSULE_TYPE, header);
        header_len += sm_varint_encode(mgr->wt_session_id, header + header_len);
        // WT_LOG_INFO(
        //     "Stream send: WEBTRANSPORT_STREAM session_id=%llu header_len=%zu "
        //     "app_len=%d",
        //     (unsigned long long)mgr->wt_session_id, header_len, length);
    }
    header_len += sm_varint_encode((uint64_t)length, header + header_len);

    sm_send_req_t* req = sm_send_req_alloc(header, header_len, data, length);
    if (!req) {
        MsQuic->StreamShutdown(quic_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_GRACEFUL, 0);
        return WT_ERR_BUFFER_FULL;
    }
    sctx->pending_send = req;
    /* Never FIN: the stream stays open so subsequent messages reuse it.
     *
     * The server used to FIN after its first payload here. With message
     * framing that is both unnecessary (boundaries no longer depend on the
     * stream ending) and harmful: a FIN'd stream is unusable for the next
     * send, so every push would open another one, and browsers cap
     * concurrent streams at around a hundred. */
    sctx->pending_send_fin = 0;

    status = MsQuic->StreamStart(quic_stream,
                                  QUIC_STREAM_START_FLAG_IMMEDIATE);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("StreamStart failed: 0x%x stream_id=%llu",
                     status, (unsigned long long)stream_id);
        sctx->pending_send = NULL;
        sm_send_req_free(req, "stream_start_failed");
        MsQuic->StreamClose(quic_stream);
        sm_lock(mgr);
        mgr->streams[slot].in_use = false;
        mgr->streams[slot].quic_stream = NULL;
        mgr->streams[slot].id = 0;
        atomic_fetch_sub(&mgr->active_streams, 1);
        sm_unlock(mgr);
        free(sctx);
        return WT_ERR_SEND_FAILED;
    }

    // WT_LOG_INFO(
    //     "StreamOpen+Start OK conn=%llu stream_id=%llu app_len=%d "
    //     "wire_len=%u header_len=%zu pending_send=1 fin=%d "
    //     "stamp=LOCAL_STREAM_OPEN_V2 is_server=%d",
    //     (unsigned long long)mgr->conn_id,
    //     (unsigned long long)stream_id,
    //     length,
    //     (unsigned)req->length,
    //     header_len,
    //     sctx->pending_send_fin ? 1 : 0,
    //     mgr->is_server ? 1 : 0);

    /* pending_send_fin is always 0 now, so the send side stays open and the
     * slot remains eligible for reuse. Kept as a conditional because the flag
     * is still honoured by START_COMPLETE for any future one-shot sender. */
    if (sctx->pending_send_fin) {
        sm_lock(mgr);
        mgr->streams[slot].send_closed = true;
        sm_unlock(mgr);
    }
    return WT_OK;
}

void wt_stream_manager_accept_stream(
    wt_stream_manager_t* mgr, HQUIC quic_stream)
{
    wt_stream_manager_accept_stream_prefill(mgr, quic_stream, NULL, 0);
}

void wt_stream_manager_accept_stream_prefill(
    wt_stream_manager_t* mgr, HQUIC quic_stream,
    const uint8_t* data, uint32_t length)
{
    /* Find and reserve a free slot under lock — concurrent with send
     * on the application thread. */
    int slot = -1;
    sm_lock(mgr);
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        if (!mgr->streams[i].in_use) { slot = i; break; }
    }
    if (slot < 0) {
        sm_unlock(mgr);
        MsQuic->StreamShutdown(quic_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        MsQuic->StreamClose(quic_stream);  /* no sctx, so no callback to do this */
        return;
    }

    /* Reserve the slot immediately, unlock before alloc + MsQuic calls. */
    /* Generate unique stream ID. next_id is uint64_t — in practice
     * this never wraps (~584K years at 1M streams/s). On the off
     * chance it wraps to 0 (reserved), scan the active stream slots
     * for an unused ID.  We hold the lock so the slot table is stable. */
    wt_stream_id_t stream_id = mgr->next_id++;
    if (stream_id == 0) {
        stream_id = 1;
        mgr->next_id = WT_MAX_STREAMS + 2;  /* skip scan range for future allocs */
        for (;;) {
            bool conflict = false;
            for (int i = 0; i < WT_MAX_STREAMS; i++) {
                if (mgr->streams[i].in_use && mgr->streams[i].id == stream_id) {
                    conflict = true;
                    break;
                }
            }
            if (!conflict) break;
            if (++stream_id == 0) stream_id = 1;  /* still skip 0 */
        }
    }
    /* CRITICAL: slot.id must match sctx->stream_id or SHUTDOWN_COMPLETE
     * never clears the slot (id stayed 0) while still decrementing
     * active_streams — active=0 with orphan handles → unsafe shutdown. */
    mgr->streams[slot].id = stream_id;
    mgr->streams[slot].quic_stream = quic_stream;
    mgr->streams[slot].in_use = true;
    mgr->streams[slot].send_closed = false;
    mgr->streams[slot].recv_closed = false;
    mgr->streams[slot].peer_initiated = true;
    atomic_fetch_add(&mgr->active_streams, 1);
    sm_unlock(mgr);

    stream_ctx_t* sctx = (stream_ctx_t*)calloc(1, sizeof(stream_ctx_t));
    if (!sctx) {
        /* Release reserved slot. */
        sm_lock(mgr);
        mgr->streams[slot].in_use = false;
        mgr->streams[slot].quic_stream = NULL;
        mgr->streams[slot].id = 0;
        mgr->streams[slot].peer_initiated = false;
        atomic_fetch_sub(&mgr->active_streams, 1);
        sm_unlock(mgr);
        MsQuic->StreamShutdown(quic_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_GRACEFUL, 0);
        MsQuic->StreamClose(quic_stream);  /* no sctx, so no callback to do this */
        return;
    }
    sctx->mgr = mgr;
    sctx->stream_id = stream_id;
    sctx->quic_stream = quic_stream;

    /* Seed the reassembly buffer with bytes the caller already received. */
    if (data && length > 0) {
        sctx->recv_buf = (uint8_t*)malloc(length);
        if (!sctx->recv_buf) {
            WT_LOG_ERROR(
                "Accept prefill: malloc(%u) failed conn=%llu stream_id=%llu — "
                "replayed bytes lost",
                length, (unsigned long long)mgr->conn_id,
                (unsigned long long)stream_id);
        } else {
            memcpy(sctx->recv_buf, data, length);
            sctx->recv_offset = length;
            atomic_fetch_add(&mgr->total_recv_bytes, length);
        }
    }

    // WT_LOG_INFO(
    //     "Accept peer stream conn=%llu stream_id=%llu peer_initiated=1 "
    //     "prefill=%u",
    //     (unsigned long long)mgr->conn_id,
    //     (unsigned long long)stream_id,
    //     (unsigned)sctx->recv_offset);

    /* SetCallbackHandler outside lock — safe because the slot is already
     * registered above. If SHUTDOWN_COMPLETE fires synchronously it will
     * find the slot via stream_id and clean up correctly. */
    MsQuic->SetCallbackHandler(quic_stream,
                                (void*)(uintptr_t)k_stream_handler, sctx);

    /* Run the replayed bytes through the same strip + framing path as live
     * data. Safe to do after SetCallbackHandler: this runs on the
     * connection's QUIC worker thread (the caller is inside an h3 stream
     * callback), so msquic cannot deliver a concurrent RECEIVE for this
     * stream and race the buffer. */
    if (sctx->recv_offset > 0) {
        if (!sctx->header_checked) {
            if (mgr->use_wt_stream_header) {
                bool incomplete = false, mismatch = false;
                size_t skip = sm_skip_wt_stream_header(
                    sctx->recv_buf, sctx->recv_offset,
                    mgr->wt_session_id, &incomplete, &mismatch);
                if (incomplete)
                    return;  /* keep buffered; finish on the next RECEIVE */
                if (mismatch) {
                    sctx->framing_error = true;
                    MsQuic->StreamShutdown(quic_stream,
                                            QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                    return;
                }
                if (skip > 0) {
                    size_t remaining = sctx->recv_offset - skip;
                    if (remaining > 0)
                        memmove(sctx->recv_buf, sctx->recv_buf + skip, remaining);
                    sctx->recv_offset = (uint32_t)remaining;
                    atomic_fetch_sub(&mgr->total_recv_bytes, (uint32_t)skip);
                }
            }
            sctx->header_checked = true;
        }
        if (!sm_drain_framed_messages(sctx)) {
            atomic_fetch_sub(&mgr->total_recv_bytes, sctx->recv_offset);
            free(sctx->recv_buf);
            sctx->recv_buf = NULL;
            sctx->recv_offset = 0;
            MsQuic->StreamShutdown(quic_stream,
                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        }
    }
}
