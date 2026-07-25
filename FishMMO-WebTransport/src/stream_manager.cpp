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
    uint8_t*             recv_buf;
    uint32_t             recv_offset;
    /* True after we have handled the optional WEBTRANSPORT_STREAM capsule
     * at the start of a browser data stream (or decided none is present). */
    bool                 header_checked;
    /* Prevent double StreamClose (mgr shutdown race vs SHUTDOWN_COMPLETE). */
    bool                 close_done;
    /* Outbound payload waiting for START_COMPLETE before StreamSend.
     * NULL after ownership transfers to StreamSend ClientContext. */
    sm_send_req_t*       pending_send;
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
    WT_LOG_INFO("SEND_COMPLETE free ok len=%u (%s)",
                req->length, reason ? reason : "complete");
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
 * Wire format (draft-ietf-webtrans-http3 / Chromium):
 *   Type (i) = 0x41          ← QUIC varint (Chrome may use 1-byte 0x41 OR
 *                              2-byte 0x40 0x41; never assume single byte)
 *   Session ID (i)
 *   … Stream Data (app)      ← no length field
 *
 * Production bug: first=40 41 00 11 was delivered whole because we only
 * matched buf[0]==0x41 and ignored 2-byte varint type encoding → FishNet
 * saw capsule garbage → disconnect → unsafe shutdown → quic_bugcheck.
 *
 * Returns byte offset of application data (0 if not a WT stream capsule).
 * incomplete=true if type or session-id varint is truncated.
 */
static size_t sm_skip_wt_stream_header(const uint8_t* buf, size_t len,
                                       bool* incomplete)
{
    *incomplete = false;
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
    return off;
}

/** True if buffer looks like a (possibly incomplete) WEBTRANSPORT_STREAM head. */
static bool sm_looks_like_wt_capsule(const uint8_t* buf, size_t len)
{
    if (len < 1) return false;
    uint64_t type = 0;
    size_t type_n = 0;
    if (sm_varint_decode(buf, len, &type, &type_n) < 0)
        return true; /* incomplete multi-byte type — treat as possible capsule */
    return type == WT_STREAM_CAPSULE_TYPE;
}

/* ── Forward ────────────────────────────────────────────────── */

static QUIC_STATUS stream_cb(HQUIC stream, void* ctx,
                               QUIC_STREAM_EVENT* event);

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

        /* Append into recv_buf (may need prior partial WT header bytes). */
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

        /*
         * Deliver on RECEIVE (Chrome keeps a persistent bidi stream open).
         * Strip WEBTRANSPORT_STREAM capsule first so FishNet never sees 0x41.
         */
        const uint8_t* app = sctx->recv_buf;
        size_t app_len = sctx->recv_offset;

        if (!sctx->header_checked) {
            if (sctx->mgr->use_wt_stream_header) {
                bool incomplete = false;
                size_t skip = sm_skip_wt_stream_header(app, app_len, &incomplete);
                if (incomplete) {
                    /* Wait for more bytes to finish the capsule. */
                    return QUIC_STATUS_SUCCESS;
                }
                if (skip > 0) {
                    WT_LOG_INFO(
                        "Stream %llu: stripped WEBTRANSPORT_STREAM capsule "
                        "skip=%zu raw_first=%02x %02x %02x %02x "
                        "app_payload=%zu app_first=%02x %02x %02x %02x "
                        "stamp=WT_CAPSULE_VARINT_TYPE_V2",
                        (unsigned long long)sctx->stream_id,
                        skip,
                        app_len > 0 ? app[0] : 0,
                        app_len > 1 ? app[1] : 0,
                        app_len > 2 ? app[2] : 0,
                        app_len > 3 ? app[3] : 0,
                        app_len - skip,
                        app_len > skip ? app[skip] : 0,
                        app_len > skip + 1 ? app[skip + 1] : 0,
                        app_len > skip + 2 ? app[skip + 2] : 0,
                        app_len > skip + 3 ? app[skip + 3] : 0);
                    app += skip;
                    app_len -= skip;
                } else if (sm_looks_like_wt_capsule(app, app_len)) {
                    /* Should have been incomplete or skip>0 — don't deliver garbage. */
                    WT_LOG_ERROR(
                        "Stream %llu: capsule expected but strip returned 0 "
                        "raw_first=%02x %02x %02x %02x len=%zu — holding",
                        (unsigned long long)sctx->stream_id,
                        app_len > 0 ? app[0] : 0,
                        app_len > 1 ? app[1] : 0,
                        app_len > 2 ? app[2] : 0,
                        app_len > 3 ? app[3] : 0,
                        app_len);
                    return QUIC_STATUS_SUCCESS;
                }
            }
            sctx->header_checked = true;
        }

        if (app_len > 0 &&
            sctx->mgr->on_stream_data &&
            sctx->mgr->callback_ctx) {
            /* Refuse to deliver if first byte still looks like unstripped capsule. */
            if (sctx->mgr->use_wt_stream_header &&
                sm_looks_like_wt_capsule(app, app_len) &&
                app[0] == 0x40 /* 2-byte varint lead */ ) {
                WT_LOG_ERROR(
                    "Stream %llu: refusing deliver of likely unstripped capsule "
                    "first=%02x %02x %02x %02x — abort stream only (not process)",
                    (unsigned long long)sctx->stream_id,
                    app_len > 0 ? app[0] : 0,
                    app_len > 1 ? app[1] : 0,
                    app_len > 2 ? app[2] : 0,
                    app_len > 3 ? app[3] : 0);
                atomic_fetch_sub(&sctx->mgr->total_recv_bytes, sctx->recv_offset);
                free(sctx->recv_buf);
                sctx->recv_buf = NULL;
                sctx->recv_offset = 0;
                MsQuic->StreamShutdown(stream,
                                        QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                return QUIC_STATUS_ABORTED;
            }
            WT_LOG_INFO(
                "Stream %llu: FIRST_APP_PAYLOAD_AFTER_SESSION deliver %zu bytes "
                "to app (conn=%llu) first=%02x %02x %02x %02x "
                "stamp=WT_CAPSULE_VARINT_TYPE_V2",
                (unsigned long long)sctx->stream_id,
                app_len,
                (unsigned long long)sctx->mgr->conn_id,
                app_len > 0 ? app[0] : 0,
                app_len > 1 ? app[1] : 0,
                app_len > 2 ? app[2] : 0,
                app_len > 3 ? app[3] : 0);
            sctx->mgr->on_stream_data(
                sctx->mgr->callback_ctx,
                sctx->mgr->conn_id,
                sctx->stream_id,
                app,
                (int32_t)app_len);
        } else if (app_len == 0) {
            WT_LOG_INFO(
                "Stream %llu: recv after header strip empty (conn=%llu) "
                "raw_len=%u header_checked=%d",
                (unsigned long long)sctx->stream_id,
                (unsigned long long)sctx->mgr->conn_id,
                (unsigned)sctx->recv_offset,
                sctx->header_checked ? 1 : 0);
        }

        /* Data delivered (or empty after header strip) — free buffer. */
        atomic_fetch_sub(&sctx->mgr->total_recv_bytes, sctx->recv_offset);
        free(sctx->recv_buf);
        sctx->recv_buf = NULL;
        sctx->recv_offset = 0;
        return QUIC_STATUS_SUCCESS;
    }

    case QUIC_STREAM_EVENT_PEER_SEND_SHUTDOWN: {
        /* Peer finished sending. Deliver any residual buffer. */
        if (sctx->recv_buf && sctx->recv_offset > 0 &&
            sctx->mgr->on_stream_data && sctx->mgr->callback_ctx) {
            const uint8_t* app = sctx->recv_buf;
            size_t app_len = sctx->recv_offset;
            if (!sctx->header_checked && sctx->mgr->use_wt_stream_header) {
                bool incomplete = false;
                size_t skip = sm_skip_wt_stream_header(app, app_len, &incomplete);
                if (!incomplete && skip > 0) {
                    app += skip;
                    app_len -= skip;
                }
                sctx->header_checked = true;
            }
            if (app_len > 0) {
                WT_LOG_INFO(
                    "Stream %llu: deliver %zu bytes on peer FIN first=%02x %02x",
                    (unsigned long long)sctx->stream_id, app_len,
                    app[0], app_len > 1 ? app[1] : 0);
                sctx->mgr->on_stream_data(
                    sctx->mgr->callback_ctx,
                    sctx->mgr->conn_id,
                    sctx->stream_id,
                    app,
                    (int32_t)app_len);
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

        /* Only graceful-shutdown our send side if not already force-closed. */
        if (!sctx->close_done) {
            MsQuic->StreamShutdown(stream,
                                    QUIC_STREAM_SHUTDOWN_FLAG_GRACEFUL, 0);
        }
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
            WT_LOG_INFO(
                "Stream START_COMPLETE stream_id=%llu (no pending send)",
                (unsigned long long)sctx->stream_id);
            return QUIC_STATUS_SUCCESS;
        }
        /* Transfer ownership to StreamSend ClientContext before call. */
        sctx->pending_send = NULL;

        QUIC_STATUS st = MsQuic->StreamSend(
            stream,
            &req->buf,
            1,
            QUIC_SEND_FLAG_FIN,
            req);
        if (QUIC_FAILED(st)) {
            WT_LOG_ERROR(
                "StreamSend failed after START_COMPLETE st=0x%x "
                "stream_id=%llu len=%u — free now (no SEND_COMPLETE)",
                st,
                (unsigned long long)sctx->stream_id,
                req->length);
            sm_send_req_free(req, "send_failed");
            if (!sctx->close_done) {
                MsQuic->StreamShutdown(
                    stream,
                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT |
                        QUIC_STREAM_SHUTDOWN_FLAG_IMMEDIATE,
                    0);
            }
            return QUIC_STATUS_SUCCESS;
        }

        WT_LOG_INFO(
            "StreamSend queued after START_COMPLETE stream_id=%llu len=%u "
            "stamp=SEND_AFTER_START_V1 (free only on SEND_COMPLETE)",
            (unsigned long long)sctx->stream_id,
            req->length);
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
        sm_lock(mgr);
        for (int i = 0; i < WT_MAX_STREAMS; i++) {
            if (mgr->streams[i].id == sctx->stream_id) {
                mgr->streams[i].in_use = false;
                mgr->streams[i].quic_stream = NULL;
                mgr->streams[i].id = 0;
                break;
            }
        }
        uint32_t prev = atomic_fetch_sub(&mgr->active_streams, 1);
        sm_unlock(mgr);

        atomic_fetch_sub(&mgr->total_recv_bytes, sctx->recv_offset);
        free(sctx->recv_buf);
        sctx->recv_buf = NULL;

        /* StreamClose exactly once — mgr shutdown may have already closed. */
        if (!sctx->close_done) {
            sctx->close_done = true;
            MsQuic->StreamClose(stream);
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
    }

}

void wt_stream_manager_shutdown(wt_stream_manager_t* mgr)
{
    if (!mgr) return;

    /*
     * Safe shutdown: only StreamShutdown handles that are still non-NULL
     * and were fully registered. Never call StreamShutdown/StreamClose on
     * NULL, already-closed, or never-started streams — that path was
     * MsQuicStreamShutdown → quic_bugcheck → LoginServer SIGABRT.
     *
     * Collect under lock, drop ownership (quic_stream=NULL), then shutdown
     * outside lock so SHUTDOWN_COMPLETE can re-enter the lock.
     */
    HQUIC* pending = (HQUIC*)malloc(sizeof(HQUIC) * (size_t)WT_MAX_STREAMS);
    if (!pending) {
        WT_LOG_ERROR("wt_stream_manager_shutdown: malloc(%zu) failed — "
                     "cannot collect stream handles (active=%u). Skipping "
                     "MsQuic shutdown to avoid crash; session drop only.",
                     sizeof(HQUIC) * (size_t)WT_MAX_STREAMS,
                     (unsigned)atomic_load(&mgr->active_streams));
        return;
    }

    int pending_count = 0;
    sm_lock(mgr);
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        if (!mgr->streams[i].in_use)
            continue;
        HQUIC h = mgr->streams[i].quic_stream;
        if (!h)
            continue; /* reserved slot without open handle — skip */
        pending[pending_count++] = h;
        /* Take ownership so concurrent SHUTDOWN_COMPLETE / double-shutdown
         * cannot re-use the same handle. */
        mgr->streams[i].quic_stream = NULL;
    }
    sm_unlock(mgr);

    WT_LOG_INFO(
        "wt_stream_manager_shutdown conn=%llu streams_to_abort=%d active=%u "
        "stamp=SAFE_SHUTDOWN_V1",
        (unsigned long long)mgr->conn_id,
        pending_count,
        (unsigned)atomic_load(&mgr->active_streams));

    for (int i = 0; i < pending_count; i++) {
        if (!pending[i])
            continue;
        /* Best-effort: never let one bad client stream kill the process.
         * MsQuic still may assert on corrupt handles; we only pass handles
         * we opened and have not StreamClose'd yet. */
        MsQuic->StreamShutdown(
            pending[i],
            QUIC_STREAM_SHUTDOWN_FLAG_ABORT | QUIC_STREAM_SHUTDOWN_FLAG_IMMEDIATE,
            0);
        /* SHUTDOWN_COMPLETE (stream_cb) owns StreamClose + sctx free. */
    }
    free(pending);
}

int32_t wt_stream_manager_send(
    wt_stream_manager_t* mgr, const uint8_t* data, int32_t length)
{
    if (!data || length <= 0) return WT_ERR_SEND_FAILED;
    if (atomic_load(&mgr->shutting_down)) return WT_ERR_INVALID_STATE;

    /* Find free slot under lock — concurrent with accept on QUIC thread. */
    int slot = -1;
    sm_lock(mgr);
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        if (!mgr->streams[i].in_use) { slot = i; break; }
    }
    if (slot < 0) { sm_unlock(mgr); return WT_ERR_BUFFER_FULL; }

    /* Reserve the slot immediately so accept doesn't grab it.
     * Increment active_streams while under the lock so that the count
     * is consistent with the reserved slot — shutdown logic checks
     * active_streams against streams_done_flag to decide whether to
     * free the mgr. Moving the increment here (vs. after StreamOpen)
     * prevents a theoretical underflow if SHUTDOWN_COMPLETE raced
     * between StreamOpen and the old increment. */
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
    mgr->streams[slot].id = stream_id;
    mgr->streams[slot].in_use = true;
    mgr->streams[slot].send_closed = false;
    atomic_fetch_add(&mgr->active_streams, 1);
    /* quic_stream set below after StreamOpen; SHUTDOWN_COMPLETE will
     * see in_use=true but quic_stream=NULL and skip cleanup — that's
     * fine because we haven't opened the stream yet. */
    sm_unlock(mgr);

    /* MsQuic requires a non-NULL stream callback at StreamOpen.
     * Passing NULL,NULL → QUIC_STATUS_INVALID_PARAMETER (0x16) and
     * ServerHandshake never leaves the server (client 12s timeout). */
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

    /* Record quic_stream under lock so SHUTDOWN_COMPLETE can find the slot. */
    sm_lock(mgr);
    mgr->streams[slot].quic_stream = quic_stream;
    sm_unlock(mgr);

    /* Build heap send request BEFORE StreamStart. StreamSend runs only from
     * START_COMPLETE so msquic worker never touches a stack QUIC_BUFFER or a
     * half-started stream (QuicStreamSendBufferRequest SIGSEGV). */
    size_t header_len = 0;
    uint8_t header[1 + 8];
    if (mgr->use_wt_stream_header) {
        header[0] = (uint8_t)WT_STREAM_CAPSULE_TYPE;
        size_t sid_n = sm_varint_encode(mgr->wt_session_id, header + 1);
        header_len = 1 + sid_n;
        WT_LOG_INFO(
            "Stream send: WEBTRANSPORT_STREAM session_id=%llu header_len=%zu "
            "app_len=%d stamp=WT_CAPSULE_NO_LEN_V1",
            (unsigned long long)mgr->wt_session_id, header_len, length);
    }

    sm_send_req_t* req = sm_send_req_alloc(
        header_len > 0 ? header : NULL, header_len, data, length);
    if (!req) {
        MsQuic->StreamShutdown(quic_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        return WT_ERR_BUFFER_FULL;
    }
    sctx->pending_send = req;

    status = MsQuic->StreamStart(quic_stream,
                                  QUIC_STREAM_START_FLAG_IMMEDIATE);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("StreamStart failed: 0x%x stream_id=%llu",
                     status, (unsigned long long)stream_id);
        /* pending_send freed in SHUTDOWN_COMPLETE; do not free sctx here. */
        MsQuic->StreamClose(quic_stream);
        return WT_ERR_SEND_FAILED;
    }

    WT_LOG_INFO(
        "StreamOpen+Start OK conn=%llu stream_id=%llu app_len=%d "
        "wire_len=%u header_len=%zu pending_send=1 stamp=SERVER_REPLY_STREAM_V2 "
        "(StreamSend deferred to START_COMPLETE — buffer free on SEND_COMPLETE only)",
        (unsigned long long)mgr->conn_id,
        (unsigned long long)stream_id,
        length,
        (unsigned)req->length,
        header_len);

    /* send_closed updated when FIN is actually queued (START_COMPLETE path
     * always uses QUIC_SEND_FLAG_FIN). Mark intent now for accounting. */
    sm_lock(mgr);
    mgr->streams[slot].send_closed = true;
    sm_unlock(mgr);
    return WT_OK;
}

void wt_stream_manager_accept_stream(
    wt_stream_manager_t* mgr, HQUIC quic_stream)
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
    mgr->streams[slot].quic_stream = quic_stream;
    mgr->streams[slot].in_use = true;
    atomic_fetch_add(&mgr->active_streams, 1);
    sm_unlock(mgr);

    stream_ctx_t* sctx = (stream_ctx_t*)calloc(1, sizeof(stream_ctx_t));
    if (!sctx) {
        /* Release reserved slot. */
        sm_lock(mgr);
        mgr->streams[slot].in_use = false;
        mgr->streams[slot].quic_stream = NULL;
        mgr->streams[slot].id = 0;
        atomic_fetch_sub(&mgr->active_streams, 1);
        sm_unlock(mgr);
        MsQuic->StreamShutdown(quic_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        MsQuic->StreamClose(quic_stream);  /* no sctx, so no callback to do this */
        return;
    }
    sctx->mgr = mgr;
    sctx->stream_id = stream_id;
    sctx->quic_stream = quic_stream;

    /* SetCallbackHandler outside lock — safe because the slot is already
     * registered above. If SHUTDOWN_COMPLETE fires synchronously it will
     * find the slot via stream_id and clean up correctly. */
    MsQuic->SetCallbackHandler(quic_stream,
                                (void*)(uintptr_t)k_stream_handler, sctx);
}