/**
 * @file http3.cpp
 * @brief Minimal HTTP/3 WebTransport handshake (RFC 9114 + WebTransport).
 *
 * Implements only the frames and QPACK subset needed for:
 *   1. Sending/receiving SETTINGS frames
 *   2. Sending/receiving HEADERS frames for WebTransport CONNECT
 *   3. Automatic protocol detection (HTTP/3 vs raw QUIC)
 *
 * QPACK: Uses the full static table (RFC 9204 Appendix A, all 99
 * entries). No dynamic table support — sufficient for WebTransport
 * handshakes and common browser headers.
 *
 * ## HTTP/3 Handshake State Machine
 *
 * ### Server-side transitions
 *
 *   H3_SRV_WAIT_CONTROL_STREAM  --[PEER_STREAM_STARTED, first stream]--> H3_SRV_GOT_SETTINGS
 *   H3_SRV_GOT_SETTINGS         --[stream RECEIVE, type byte == 0x00]------
 *     detect client as HTTP/3, send server SETTINGS                    --> H3_SRV_WAIT_CONNECT
 *   H3_SRV_WAIT_CONNECT         --[stream RECEIVE, HEADERS frame]---------
 *     parse CONNECT request, validate :method/:protocol/:origin        --> H3_SRV_ESTABLISHED  (or H3_SRV_ERROR)
 *   H3_SRV_WAIT_CONTROL_STREAM  --[first byte != 0x00]------------------
 *     detect raw QUIC (native client), complete immediately            --> H3_SRV_ESTABLISHED
 *
 * ### Client-side transitions
 *
 *   H3_CLI_SENDING_SETTINGS  --[send control stream + SETTINGS, send CONNECT]--> H3_CLI_WAIT_RESPONSE
 *   H3_CLI_WAIT_RESPONSE     --[receive HEADERS :status 200]------------------> H3_CLI_ESTABLISHED
 *   H3_CLI_WAIT_RESPONSE     --[error or timeout]-----------------------------> H3_CLI_ERROR
 *
 * ### Data flow
 *
 *   PEER_STREAM_STARTED  ─→ h3_server_handle_stream()  ──→ assign stream role
 *   stream RECEIVE       ─→ h3_stream_cb()             ──→ buffer data
 *                         ─→ h3_server_process_data()  ──→ parse frames
 *                         ─→ h3->on_ready()             ──→ create wt_session
 */

#include "http3.h"
#include <stdlib.h>
#include <string.h>

/* ═══════════════════════════════════════════════════════════════
 * QUIC Variable-Length Integer (RFC 9000 §16)
 * ═══════════════════════════════════════════════════════════════ */

static int varint_decode(const uint8_t* buf, size_t buf_len,
                         uint64_t* out_val, uint8_t* out_bytes)
{
    if (buf_len < 1) return -1;
    uint8_t prefix = buf[0] >> 6;
    uint8_t len = 1u << prefix;  /* 1, 2, 4, or 8 bytes */
    if (buf_len < len) return -1;

    /* RFC 9000 §16: the first byte always contributes 6 value bits
     * regardless of the varint length. The prefix bits (upper 2) are
     * already consumed to determine the length. */
    uint64_t val = buf[0] & 0x3Fu;  /* mask off 2-bit prefix */
    for (uint8_t i = 1; i < len; i++)
        val = (val << 8) | buf[i];

    *out_val = val;
    *out_bytes = len;
    return 0;
}

static uint8_t varint_encode(uint64_t val, uint8_t* out)
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
    if (val < 1073741824) {
        out[0] = (uint8_t)(0x80 | (val >> 24));
        out[1] = (uint8_t)((val >> 16) & 0xFF);
        out[2] = (uint8_t)((val >> 8) & 0xFF);
        out[3] = (uint8_t)(val & 0xFF);
        return 4;
    }
    out[0] = (uint8_t)(0xC0 | (val >> 56));
    out[1] = (uint8_t)((val >> 48) & 0xFF);
    out[2] = (uint8_t)((val >> 40) & 0xFF);
    out[3] = (uint8_t)((val >> 32) & 0xFF);
    out[4] = (uint8_t)((val >> 24) & 0xFF);
    out[5] = (uint8_t)((val >> 16) & 0xFF);
    out[6] = (uint8_t)((val >> 8) & 0xFF);
    out[7] = (uint8_t)(val & 0xFF);
    return 8;
}

/* ═══════════════════════════════════════════════════════════════
 * QPACK Variable-Length Integer (RFC 9204 §4.1.1)
 * ═══════════════════════════════════════════════════════════════
 * QPACK uses a configurable-prefix varint format (not the QUIC
 * 2-bit-prefix varint from RFC 9000).  The prefix width is given
 * by prefix_bits (typically 3, 6, or 7 bits).  When the value
 * fits in the prefix, it is stored directly.  Otherwise, the
 * prefix is set to all-1s and the remainder (value - prefix_max)
 * is encoded in 7-bit chunks with a continuation bit (bit 7).
 */

static int qpack_varint_decode(const uint8_t* buf, size_t buf_len,
                                uint64_t* out_val, uint8_t* out_bytes,
                                uint8_t prefix_bits)
{
    if (buf_len < 1) return -1;
    uint8_t prefix_max = (uint8_t)((1u << prefix_bits) - 1);
    uint64_t val = buf[0] & prefix_max;

    if (val < (uint64_t)prefix_max) {
        *out_val = val;
        *out_bytes = 1;
        return 0;
    }

    /* Multi-byte: read 7-bit continuation chunks LSB first.
     * Guard against shift >= 64 which is undefined behavior in C.
     * After 9 continuation bytes (shift=63), the 10th byte would
     * trigger UB. We reject such oversized varints.
     *
     * FIX #6: pos is size_t (not uint8_t) to avoid truncating
     * buf_len when the buffer is larger than 255 bytes.  The
     * previous uint8_t cast caused valid large varints to be
     * rejected as "truncated." */
    size_t pos = 1;
    uint64_t shift = 0;
    while (pos < buf_len) {
        uint8_t byte = buf[pos];
        if (shift >= 64) return -1;  /* prevent UB from over-long varint */
        val += (uint64_t)(byte & 0x7F) << shift;
        shift += 7;
        pos++;
        if (!(byte & 0x80)) {
            *out_val = val;
            *out_bytes = pos;
            return 0;
        }
    }
    return -1;  /* truncated */
}

static uint8_t qpack_varint_encode(uint64_t val, uint8_t* out,
                                    uint8_t prefix_bits)
{
    uint8_t prefix_max = (uint8_t)((1u << prefix_bits) - 1);

    if (val < (uint64_t)prefix_max) {
        out[0] = (uint8_t)val;
        return 1;
    }

    out[0] = prefix_max;
    val -= prefix_max;

    /* Encode remaining as 7-bit chunks LSB first */
    uint8_t pos = 1;
    while (val >= 128) {
        out[pos++] = (uint8_t)((val & 0x7F) | 0x80);
        val >>= 7;
    }
    out[pos++] = (uint8_t)(val & 0x7F);
    return pos;
}

/* ═══════════════════════════════════════════════════════════════
 * QPACK Static Table (RFC 9204 Appendix A — all 99 entries)
 * ═══════════════════════════════════════════════════════════════
 * Complete table required for correct decoding of any QPACK-encoded
 * header field that references a static table index. Each entry
 * stores both a field name and its known value.  Indexed field
 * line (0x00 prefix) copies both name and value from the entry.
 */

typedef struct {
    const char* name;
    uint8_t     name_len;
    const char* value;
    uint8_t     value_len;
} qpack_entry_t;

#define QE(N, V) { N, sizeof(N) - 1, V, sizeof(V) - 1 }

static const qpack_entry_t kQpackStatic[] = {
    QE(":authority",                    ""),                                  /*  0 */
    QE(":path",                         "/"),                                 /*  1 */
    QE("age",                           "0"),                                 /*  2 */
    QE("content-disposition",           ""),                                  /*  3 */
    QE("content-length",                "0"),                                 /*  4 */
    QE("cookie",                        ""),                                  /*  5 */
    QE("date",                          ""),                                  /*  6 */
    QE("etag",                          ""),                                  /*  7 */
    QE("if-modified-since",             ""),                                  /*  8 */
    QE("if-none-match",                 ""),                                  /*  9 */
    QE("last-modified",                 ""),                                  /* 10 */
    QE("link",                          ""),                                  /* 11 */
    QE("location",                      ""),                                  /* 12 */
    QE("referer",                       ""),                                  /* 13 */
    QE("set-cookie",                    ""),                                  /* 14 */
    QE(":method",                       "CONNECT"),                           /* 15 */
    QE(":method",                       "DELETE"),                            /* 16 */
    QE(":method",                       "GET"),                               /* 17 */
    QE(":method",                       "HEAD"),                              /* 18 */
    QE(":method",                       "OPTIONS"),                           /* 19 */
    QE(":method",                       "POST"),                              /* 20 */
    QE(":method",                       "PUT"),                               /* 21 */
    QE(":scheme",                       "http"),                              /* 22 */
    QE(":scheme",                       "https"),                             /* 23 */
    QE(":status",                       "103"),                               /* 24 */
    QE(":status",                       "200"),                               /* 25 */
    QE(":status",                       "304"),                               /* 26 */
    QE(":status",                       "404"),                               /* 27 */
    QE(":status",                       "503"),                               /* 28 */
    QE("accept",                        "*/*"),                               /* 29 */
    QE("accept",                        "application/dns-message"),           /* 30 */
    QE("accept-encoding",               "gzip, deflate, br"),                 /* 31 */
    QE("accept-ranges",                 "bytes"),                             /* 32 */
    QE("access-control-allow-headers",  "cache-control"),                     /* 33 */
    QE("access-control-allow-headers",  "content-type"),                      /* 34 */
    QE("access-control-allow-origin",   "*"),                                 /* 35 */
    QE("cache-control",                 "max-age=0"),                         /* 36 */
    QE("cache-control",                 "max-age=2592000"),                   /* 37 */
    QE("cache-control",                 "max-age=604800"),                    /* 38 */
    QE("cache-control",                 "no-cache"),                          /* 39 */
    QE("cache-control",                 "no-store"),                          /* 40 */
    QE("cache-control",                 "public, max-age=31536000"),          /* 41 */
    QE("content-encoding",              "br"),                                /* 42 */
    QE("content-encoding",              "gzip"),                              /* 43 */
    QE("content-type",                  "application/dns-message"),           /* 44 */
    QE("content-type",                  "application/javascript"),            /* 45 */
    QE("content-type",                  "application/json"),                  /* 46 */
    QE("content-type",                  "application/x-www-form-urlencoded"), /* 47 */
    QE("content-type",                  "image/gif"),                         /* 48 */
    QE("content-type",                  "image/jpeg"),                        /* 49 */
    QE("content-type",                  "image/png"),                         /* 50 */
    QE("content-type",                  "text/css"),                          /* 51 */
    QE("content-type",                  "text/html; charset=utf-8"),          /* 52 */
    QE("content-type",                  "text/plain"),                        /* 53 */
    QE("content-type",                  "text/plain;charset=utf-8"),          /* 54 */
    QE("range",                         "bytes=0-"),                          /* 55 */
    QE("strict-transport-security",     "max-age=31536000"),                  /* 56 */
    QE("strict-transport-security",     "max-age=31536000; includesubdomains"), /* 57 */
    QE("strict-transport-security",     "max-age=31536000; includesubdomains; preload"), /* 58 */
    QE("vary",                          "accept-encoding"),                   /* 59 */
    QE("vary",                          "origin"),                            /* 60 */
    QE("x-content-type-options",        "nosniff"),                           /* 61 */
    QE("x-xss-protection",              "1; mode=block"),                     /* 62 */
    QE(":status",                       "100"),                               /* 63 */
    QE(":status",                       "204"),                               /* 64 */
    QE(":status",                       "206"),                               /* 65 */
    QE(":status",                       "302"),                               /* 66 */
    QE(":status",                       "400"),                               /* 67 */
    QE(":status",                       "403"),                               /* 68 */
    QE(":status",                       "421"),                               /* 69 */
    QE(":status",                       "425"),                               /* 70 */
    QE(":status",                       "500"),                               /* 71 */
    QE("accept-language",               ""),                                  /* 72 */
    QE("access-control-allow-credentials", "FALSE"),                          /* 73 */
    QE("access-control-allow-credentials", "TRUE"),                           /* 74 */
    QE("access-control-allow-headers",  "*"),                                 /* 75 */
    QE("access-control-allow-methods",  "get"),                               /* 76 */
    QE("access-control-allow-methods",  "get, post, options"),                /* 77 */
    QE("access-control-allow-methods",  "options"),                           /* 78 */
    QE("access-control-expose-headers", "content-length"),                    /* 79 */
    QE("access-control-request-headers", "content-type"),                     /* 80 */
    QE("access-control-request-method", "get"),                               /* 81 */
    QE("access-control-request-method", "post"),                              /* 82 */
    QE("alt-svc",                       "clear"),                             /* 83 */
    QE("authorization",                 ""),                                  /* 84 */
    QE("content-security-policy",       "script-src 'none'; object-src 'none'; base-uri 'none'"), /* 85 */
    QE("early-data",                    "1"),                                 /* 86 */
    QE("expect-ct",                     ""),                                  /* 87 */
    QE("forwarded",                     ""),                                  /* 88 */
    QE("if-range",                      ""),                                  /* 89 */
    QE("origin",                        ""),                                  /* 90 */
    QE("purpose",                       "prefetch"),                          /* 91 */
    QE("server",                        ""),                                  /* 92 */
    QE("timing-allow-origin",           "*"),                                 /* 93 */
    QE("upgrade-insecure-requests",     "1"),                                 /* 94 */
    QE("user-agent",                    ""),                                  /* 95 */
    QE("x-forwarded-for",               ""),                                  /* 96 */
    QE("x-frame-options",               "deny"),                              /* 97 */
    QE("x-frame-options",               "sameorigin"),                        /* 98 */
};

#undef QE

#define QPACK_STATIC_SIZE (sizeof(kQpackStatic) / sizeof(kQpackStatic[0]))

/* ═══════════════════════════════════════════════════════════════
 * HPACK / QPACK Huffman Code Table (RFC 7541 Appendix B / RFC 9204 Appendix B)
 * ═══════════════════════════════════════════════════════════════
 * Static Huffman code table for decoding HPACK/QPACK header field
 * compression. Two parallel arrays indexed by symbol (0-255):
 *
 *   kHuffCode[sym] = canonical Huffman code (right-padded, i.e. the
 *                    code occupies the most significant bits of the
 *                    kHuffLen[sym]-bit value).
 *   kHuffLen[sym]  = code length in bits.
 *
 * The WebTransport handshake uses only ASCII symbols (32-126 are the
 * common printable range). Symbols 128-255 (extended/control) are
 * included for full spec conformance.
 */

/*
 * RFC 7541 Appendix B / QPACK Huffman tables.
 * Codes are right-aligned bit patterns (same form as nghttp2 after
 * converting left-aligned 32-bit codes). Previous tables corrupted
 * lowercase codes ('a' was 0x23 instead of 0x03) so Chrome Huffman
 * :authority / :protocol values failed at the first non-indexed field.
 */
static const uint8_t kHuffLen[256] = {
    13, 23, 28, 28, 28, 28, 28, 28, 28, 24, 30, 28, 28, 30, 28, 28,  /* 0-15 */
    28, 28, 28, 28, 28, 28, 30, 28, 28, 28, 28, 28, 28, 28, 28, 28,  /* 16-31 */
     6, 10, 10, 12, 13,  6,  8, 11, 10, 10,  8, 11,  8,  6,  6,  6,  /* 32-47 */
     5,  5,  5,  6,  6,  6,  6,  6,  6,  6,  7,  8, 15,  6, 12, 10,  /* 48-63 */
    13,  6,  7,  7,  7,  7,  7,  7,  7,  7,  7,  7,  7,  7,  7,  7,  /* 64-79 */
     7,  7,  7,  7,  7,  7,  7,  7,  8,  7,  8, 13, 19, 13, 14,  6,  /* 80-95 */
    15,  5,  6,  5,  6,  5,  6,  6,  6,  5,  7,  7,  6,  6,  6,  5,  /* 96-111 */
     6,  7,  6,  5,  5,  6,  7,  7,  7,  7,  7, 15, 11, 14, 13, 28,  /* 112-127 */
    20, 22, 20, 20, 22, 22, 22, 23, 22, 23, 23, 23, 23, 23, 24, 23,  /* 128-143 */
    24, 24, 22, 23, 24, 23, 23, 23, 23, 21, 22, 23, 22, 23, 23, 24,  /* 144-159 */
    22, 21, 20, 22, 22, 23, 23, 21, 23, 22, 22, 24, 21, 22, 23, 23,  /* 160-175 */
    21, 21, 22, 21, 23, 22, 23, 23, 20, 22, 22, 22, 23, 22, 22, 23,  /* 176-191 */
    26, 26, 20, 19, 22, 23, 22, 25, 26, 26, 26, 27, 27, 26, 24, 25,  /* 192-207 */
    19, 21, 26, 27, 27, 26, 27, 24, 21, 21, 26, 26, 28, 27, 27, 27,  /* 208-223 */
    20, 24, 20, 21, 22, 21, 21, 23, 22, 22, 25, 25, 24, 24, 26, 23,  /* 224-239 */
    26, 27, 26, 26, 27, 27, 27, 27, 27, 28, 27, 27, 27, 27, 27, 26   /* 240-255 */
};

static const uint32_t kHuffCode[256] = {
    0x1ff8, 0x7fffd8, 0xfffffe2, 0xfffffe3,  /* 0-3 */
    0xfffffe4, 0xfffffe5, 0xfffffe6, 0xfffffe7,  /* 4-7 */
    0xfffffe8, 0xffffea, 0x3ffffffc, 0xfffffe9,  /* 8-11 */
    0xfffffea, 0x3ffffffd, 0xfffffeb, 0xfffffec,  /* 12-15 */
    0xfffffed, 0xfffffee, 0xfffffef, 0xffffff0,  /* 16-19 */
    0xffffff1, 0xffffff2, 0x3ffffffe, 0xffffff3,  /* 20-23 */
    0xffffff4, 0xffffff5, 0xffffff6, 0xffffff7,  /* 24-27 */
    0xffffff8, 0xffffff9, 0xffffffa, 0xffffffb,  /* 28-31 */
    0x14, 0x3f8, 0x3f9, 0xffa,  /* 32-35 */
    0x1ff9, 0x15, 0xf8, 0x7fa,  /* 36-39 */
    0x3fa, 0x3fb, 0xf9, 0x7fb,  /* 40-43 */
    0xfa, 0x16, 0x17, 0x18,  /* 44-47 */
    0x0, 0x1, 0x2, 0x19,  /* 48-51 */
    0x1a, 0x1b, 0x1c, 0x1d,  /* 52-55 */
    0x1e, 0x1f, 0x5c, 0xfb,  /* 56-59 */
    0x7ffc, 0x20, 0xffb, 0x3fc,  /* 60-63 */
    0x1ffa, 0x21, 0x5d, 0x5e,  /* 64-67 */
    0x5f, 0x60, 0x61, 0x62,  /* 68-71 */
    0x63, 0x64, 0x65, 0x66,  /* 72-75 */
    0x67, 0x68, 0x69, 0x6a,  /* 76-79 */
    0x6b, 0x6c, 0x6d, 0x6e,  /* 80-83 */
    0x6f, 0x70, 0x71, 0x72,  /* 84-87 */
    0xfc, 0x73, 0xfd, 0x1ffb,  /* 88-91 */
    0x7fff0, 0x1ffc, 0x3ffc, 0x22,  /* 92-95 */
    0x7ffd, 0x3, 0x23, 0x4,  /* 96-99  — 'a'=0x3 (was wrongly 0x23) */
    0x24, 0x5, 0x25, 0x26,  /* 100-103 */
    0x27, 0x6, 0x74, 0x75,  /* 104-107 */
    0x28, 0x29, 0x2a, 0x7,  /* 108-111 — 'l'=0x28 */
    0x2b, 0x76, 0x2c, 0x8,  /* 112-115 */
    0x9, 0x2d, 0x77, 0x78,  /* 116-119 — 't'=0x9 'w'=0x78 */
    0x79, 0x7a, 0x7b, 0x7ffe,  /* 120-123 */
    0x7fc, 0x3ffd, 0x1ffd, 0xffffffc,  /* 124-127 */
    0xfffe6, 0x3fffd2, 0xfffe7, 0xfffe8,  /* 128-131 */
    0x3fffd3, 0x3fffd4, 0x3fffd5, 0x7fffd9,  /* 132-135 */
    0x3fffd6, 0x7fffda, 0x7fffdb, 0x7fffdc,  /* 136-139 */
    0x7fffdd, 0x7fffde, 0xffffeb, 0x7fffdf,  /* 140-143 */
    0xffffec, 0xffffed, 0x3fffd7, 0x7fffe0,  /* 144-147 */
    0xffffee, 0x7fffe1, 0x7fffe2, 0x7fffe3,  /* 148-151 */
    0x7fffe4, 0x1fffdc, 0x3fffd8, 0x7fffe5,  /* 152-155 */
    0x3fffd9, 0x7fffe6, 0x7fffe7, 0xffffef,  /* 156-159 */
    0x3fffda, 0x1fffdd, 0xfffe9, 0x3fffdb,  /* 160-163 */
    0x3fffdc, 0x7fffe8, 0x7fffe9, 0x1fffde,  /* 164-167 */
    0x7fffea, 0x3fffdd, 0x3fffde, 0xfffff0,  /* 168-171 */
    0x1fffdf, 0x3fffdf, 0x7fffeb, 0x7fffec,  /* 172-175 */
    0x1fffe0, 0x1fffe1, 0x3fffe0, 0x1fffe2,  /* 176-179 */
    0x7fffed, 0x3fffe1, 0x7fffee, 0x7fffef,  /* 180-183 */
    0xfffea, 0x3fffe2, 0x3fffe3, 0x3fffe4,  /* 184-187 */
    0x7ffff0, 0x3fffe5, 0x3fffe6, 0x7ffff1,  /* 188-191 */
    0x3ffffe0, 0x3ffffe1, 0xfffeb, 0x7fff1,  /* 192-195 */
    0x3fffe7, 0x7ffff2, 0x3fffe8, 0x1ffffec,  /* 196-199 */
    0x3ffffe2, 0x3ffffe3, 0x3ffffe4, 0x7ffffde,  /* 200-203 */
    0x7ffffdf, 0x3ffffe5, 0xfffff1, 0x1ffffed,  /* 204-207 */
    0x7fff2, 0x1fffe3, 0x3ffffe6, 0x7ffffe0,  /* 208-211 */
    0x7ffffe1, 0x3ffffe7, 0x7ffffe2, 0xfffff2,  /* 212-215 */
    0x1fffe4, 0x1fffe5, 0x3ffffe8, 0x3ffffe9,  /* 216-219 */
    0xffffffd, 0x7ffffe3, 0x7ffffe4, 0x7ffffe5,  /* 220-223 */
    0xfffec, 0xfffff3, 0xfffed, 0x1fffe6,  /* 224-227 */
    0x3fffe9, 0x1fffe7, 0x1fffe8, 0x7ffff3,  /* 228-231 */
    0x3fffea, 0x3fffeb, 0x1ffffee, 0x1ffffef,  /* 232-235 */
    0xfffff4, 0xfffff5, 0x3ffffea, 0x7ffff4,  /* 236-239 */
    0x3ffffeb, 0x7ffffe6, 0x3ffffec, 0x3ffffed,  /* 240-243 */
    0x7ffffe7, 0x7ffffe8, 0x7ffffe9, 0x7ffffea,  /* 244-247 */
    0x7ffffeb, 0xffffffe, 0x7ffffec, 0x7ffffed,  /* 248-251 */
    0x7ffffee, 0x7ffffef, 0x7fffff0, 0x3ffffee   /* 252-255 */
};

/* ═══════════════════════════════════════════════════════════════
 * Huffman Decode Lookup Table
 * ═══════════════════════════════════════════════════════════════
 *
 * For codes of length ≤ 8 bits (all common ASCII: alphanumeric,
 * punctuation), precompute every possible 8-bit window so that
 * decoding a symbol is a single array lookup.  Codes > 8 bits fall
 * back to a linear scan over the remaining symbol space.
 *
 * The LUT is initialized once on first use (static init guard).
 */

/* Per-symbol decode entry for the 8-bit LUT */
typedef struct {
    int8_t  sym;   /* decoded symbol, -1 = no match at 8 bits */
    uint8_t len;   /* code length in bits */
} huff_lut_entry_t;

static huff_lut_entry_t huff_lut[256];
static bool huff_lut_ready = false;

static void huff_lut_init(void)
{
    if (huff_lut_ready) return;

    /* Mark all entries as unmatched */
    for (int i = 0; i < 256; i++) {
        huff_lut[i].sym = -1;
        huff_lut[i].len = 0;
    }

    /* For each symbol whose Huffman code fits in 8 bits, populate
     * all 2^(8-code_len) 8-bit patterns that start with that code.
     * Since Huffman codes are prefix-free, no two symbols can
     * produce the same 8-bit index. */
    for (int sym = 0; sym < 256; sym++) {
        uint8_t code_len = kHuffLen[sym];
        if (code_len == 0 || code_len > 8) continue;

        uint32_t code = kHuffCode[sym];  /* code in LSBs of a code_len-bit value */
        int pad = 8 - code_len;
        /* Shift the code left by pad so it occupies the MSB of the
         * 8-bit LUT index, matching the top bits extracted from the
         * bit buffer in the decode loop.  For example, code 0x21
         * (6-bit 100001) becomes 0x84 (10000100) in the 8-bit window. */
        uint32_t base = code << pad;
        int count = 1 << pad;
        for (int j = 0; j < count; j++) {
            uint8_t idx = (uint8_t)(base | j);
            if (huff_lut[idx].sym == -1) {
                huff_lut[idx].sym = (int8_t)sym;
                huff_lut[idx].len = code_len;
            }
        }
    }
    huff_lut_ready = true;
}

/**
 * Decode a Huffman-encoded string per RFC 7541 Section 5.2.
 *
 * Two-tier decode. Codes of 8 bits or fewer — every ASCII letter, digit,
 * and the punctuation common in URLs — resolve in one 8-bit table lookup.
 * Longer codes (up to 30 bits) fall back to a scan over increasing code
 * lengths.
 *
 * The fallback is not optional: RFC 7541 Appendix B assigns codes longer
 * than 8 bits to characters that appear in ordinary header values —
 * `! " # $ ' ( ) + @ < > [ \ ] ^ { | } ~` and every byte ≥ 0x80. An
 * earlier version gated the scan on `bits >= 5 && bits < 8`, which can
 * never hold immediately after the LUT loop exits (it exits with
 * bits >= 8), so no long code was ever decodable: the accumulator just
 * filled until the 64-bit guard rejected the input. Any CONNECT carrying
 * a quote or a bracket anywhere in a Huffman-coded header value failed
 * the handshake with a generic "QPACK decode error".
 *
 * @param input           Encoded bytes (Huffman-coded).
 * @param input_len       Number of encoded bytes.
 * @param output          Buffer for decoded output.
 * @param output_capacity Size of output buffer.
 * @return Number of decoded bytes, or -1 on error (invalid code or
 *         padding, output overflow).
 */
static int huffman_decode(const uint8_t* input, size_t input_len,
                          uint8_t* output, size_t output_capacity)
{
    if (input_len > 2048) return -1;  /* Cap to prevent DoS via large Huffman input */

    /* One-time LUT initialization (cheap — 256 int8 + 256 uint8 writes) */
    huff_lut_init();

    uint64_t buf = 0;       /* bit accumulator (LSB-aligned, `bits` valid) */
    int bits = 0;            /* number of valid bits in buf */
    size_t out_pos = 0;

    for (size_t i = 0; i <= input_len; i++) {
        if (i < input_len) {
            /* Shift in 8 more bits. The accumulator never exceeds
             * 30 (longest code) + 7 (partial byte) + 8 = 45 bits,
             * because the loop below always drains down below the
             * longest code length before the next byte arrives. */
            buf = (buf << 8) | input[i];
            bits += 8;
        }

        /* Emit every symbol that is fully determined by the buffered bits. */
        for (;;) {
            bool matched = false;

            if (bits >= 8) {
                /* Fast path: one lookup resolves any code ≤ 8 bits. */
                uint8_t top8 = (uint8_t)(buf >> (bits - 8));
                huff_lut_entry_t e = huff_lut[top8];
                if (e.sym >= 0) {
                    if (out_pos >= output_capacity) return -1;
                    output[out_pos++] = (uint8_t)e.sym;
                    bits -= e.len;
                    buf &= (bits > 0) ? ((1ULL << bits) - 1) : 0;
                    matched = true;
                }
            }

            /* Slow path, for two cases the lookup table cannot serve:
             *   - codes longer than 8 bits (up to 30);
             *   - fewer than 8 bits buffered, which happens at the end of
             *     the stream where a final short code may still be complete.
             * Codes are prefix-free, so the shortest length that matches is
             * the symbol. Scanning from the minimum length is redundant when
             * the table already missed — a table miss proves no code ≤ 8 bits
             * matches — but it keeps the two cases in one loop.
             *
             * Trailing padding is never mistaken for a symbol: RFC 7541 §5.2
             * pads with all-1s bits, and no code of length 1–7 is all 1s. */
            if (!matched) {
                for (int code_len = 5; code_len <= 30 && code_len <= bits; code_len++) {
                    uint64_t top = buf >> (bits - code_len);
                    for (int sym = 0; sym < 256; sym++) {
                        if (kHuffLen[sym] != code_len) continue;
                        if (top != (uint64_t)kHuffCode[sym]) continue;
                        if (out_pos >= output_capacity) return -1;
                        output[out_pos++] = (uint8_t)sym;
                        bits -= code_len;
                        buf &= (bits > 0) ? ((1ULL << bits) - 1) : 0;
                        matched = true;
                        break;
                    }
                    if (matched) break;
                }
            }

            if (!matched) break;   /* need more input, or only padding is left */
        }

        /* The emit loop consumed everything decodable, so bits left over that
         * exceed the longest code (30) can never resolve — the input is
         * malformed. Rejecting here also keeps the accumulator well clear of
         * a 64-bit shift overflow. */
        if (bits > 30) return -1;
    }

    /* ── Padding check ────────────────────────────────────────
     * RFC 7541 Section 5.2: the encoded string is padded to a byte
     * boundary with the most significant bits of the EOS symbol, which
     * are all 1s. Padding is therefore strictly fewer than 8 bits and
     * must be all 1s; anything else is a decoding error. */
    if (bits >= 8) return -1;
    if (bits > 0) {
        uint64_t mask = (1ULL << bits) - 1;
        if ((buf & mask) != mask) {
            return -1;  /* Invalid padding */
        }
    }

    return (int)out_pos;
}

static const char* qpack_static_name(uint64_t idx, uint8_t* out_len)
{
    if (idx >= QPACK_STATIC_SIZE) return NULL;
    *out_len = kQpackStatic[idx].name_len;
    return kQpackStatic[idx].name;
}

/* ── QPACK field line parser ────────────────────────────────── */

typedef struct {
    char    name[128];
    uint8_t name_len;
    char    value[1024];
    uint16_t value_len;
} h3_header_t;

/**
 * Parse a single QPACK-encoded field line from buf.
 * Returns bytes consumed, or -1 on error.
 *
 * Supports the request/push-stream field line encodings from RFC 9204:
 *   - Indexed field line (1Txxxxxx, §4.3.1) — 6-bit index prefix
 *   - Literal with name reference (01Txxxxx, §4.3.2) — 4-bit name-index prefix
 *   - Literal without name reference (001NHxxx, §4.3.3) — 3-bit name-len prefix
 *   - Post-base indexed/literal (0000xxxx) — rejected (no dynamic table)
 *   - Literal with dynamic name ref (0001xxxx) — rejected (no dynamic table)
 *
 * Prefix sizes verified against google/quiche QPACK instruction definitions
 * (qpack_instructions.cc).
 */
static int qpack_parse_field(const uint8_t* buf, size_t buf_len,
                             h3_header_t* out)
{
    if (buf_len < 1) return -1;
    memset(out, 0, sizeof(*out));

    uint8_t first = buf[0];
    size_t consumed = 0;

    /* ── Indexed Field Line (RFC 9204 §4.3.1) ──────────────────
     *   0   1   2   3   4   5   6   7
     * +---+---+---+---+---+---+---+---+
     * | 1 | T |      Index (6+)      |
     * +---+---+---+---+---+---+---+---+
     * T=0 → static table, T=1 → dynamic table.
     * 6-bit prefix for the index (verified against QUICHE). */
    if ((first & 0x80) != 0) {
        bool is_static = (first & 0x40) != 0;
        if (!is_static) {
            /* Dynamic-table indexed — we don't maintain a dynamic table. */
            WT_LOG_WARN("QPACK: indexed dynamic table ref (byte 0x%02x) — "
                        "no dynamic table, rejecting", first);
            return -1;
        }
        uint8_t nbytes;
        uint64_t idx;
        if (qpack_varint_decode(buf, buf_len, &idx, &nbytes, 6) < 0) return -1;
        if (idx >= QPACK_STATIC_SIZE) return -1;
        const qpack_entry_t* entry = &kQpackStatic[idx];
        out->name_len = entry->name_len;
        memcpy(out->name, entry->name, out->name_len);
        out->name[out->name_len] = '\0';
        out->value_len = entry->value_len;
        if (entry->value_len > 0) {
            memcpy(out->value, entry->value, entry->value_len);
        }
        out->value[out->value_len] = '\0';
        return (int)nbytes;
    }

    /* ── Literal Field Line With Name Reference (RFC 9204 §4.3.2) ─
     *   0   1   2   3   4   5   6   7
     * +---+---+---+---+---+---+---+---+
     * | 0 | 1 | T |  Name Index (4+) |
     * +---+---+---+---+---+---+---+---+
     * T=0 → static table, T=1 → dynamic table.
     * 4-bit prefix for the name index (verified against QUICHE). */
    if ((first & 0xC0) == 0x40) {
        bool is_static = (first & 0x10) != 0;  /* bit 4 = T, bit 5 = N */
        if (!is_static) {
            WT_LOG_WARN("QPACK: literal name ref to dynamic table (byte 0x%02x) — "
                        "no dynamic table, rejecting", first);
            return -1;
        }
        uint8_t nbytes;
        uint64_t idx;
        if (qpack_varint_decode(buf, buf_len, &idx, &nbytes, 4) < 0) return -1;
        consumed = nbytes;
        const char* sname = qpack_static_name(idx, &out->name_len);
        if (!sname) {
            WT_LOG_WARN("QPACK: literal name ref idx=%llu not in static table", idx);
            return -1;
        }
        memcpy(out->name, sname, out->name_len);
        out->name[out->name_len] = '\0';
        /* Fall through to value reader below */
    }
    /* ── Literal Field Line Without Name Reference (RFC 9204 §4.3.3) ─
     *   0   1   2   3   4   5   6   7
     * +---+---+---+---+---+---+---+---+
     * | 0 | 0 | 1 | N | H |NameLen(3+)|
     * +---+---+---+---+---+---+---+---+
     * N=never-indexed, H=Huffman flag for name, 3-bit name-length prefix
     * (verified against QUICHE). */
    else if ((first & 0xE0) == 0x20) {
        bool name_huffman = (first & 0x08) != 0;
        uint64_t name_len_val;
        uint8_t nbytes;
        if (qpack_varint_decode(buf, buf_len, &name_len_val, &nbytes, 3) < 0)
            return -1;
        consumed = nbytes;
        if (consumed + name_len_val > buf_len) return -1;
        if (name_len_val >= sizeof(out->name)) return -1;

        if (name_huffman) {
            uint8_t decoded[sizeof(out->name)];
            int dlen = huffman_decode(buf + consumed, (size_t)name_len_val,
                                      decoded, sizeof(decoded) - 1);
            if (dlen < 0 || dlen >= (int)sizeof(out->name)) return -1;
            memcpy(out->name, decoded, (size_t)dlen);
            out->name_len = (uint8_t)dlen;
            out->name[out->name_len] = '\0';
        } else {
            out->name_len = (uint8_t)name_len_val;
            memcpy(out->name, buf + consumed, out->name_len);
            out->name[out->name_len] = '\0';
        }
        consumed += (size_t)name_len_val;
        /* Fall through to value reader below */
    }
    /* ── Post-base / dynamic-only forms (RFC 9204 §4.3.4–§4.3.6) ──
     *   0000xxxx → post-base indexed (4-bit index prefix)
     *   0001xxxx → literal with dynamic name ref (3-bit index prefix)
     * Dynamic table is not supported. */
    else if ((first & 0xF0) == 0x00) {
        WT_LOG_WARN("QPACK: post-base instruction (byte 0x%02x) — "
                    "no dynamic table, rejecting", first);
        return -1;
    }
    else {
        /* 0001xxxx — literal with dynamic-table name ref */
        WT_LOG_WARN("QPACK: literal with dynamic-table name ref (byte 0x%02x) — "
                    "no dynamic table, rejecting", first);
        return -1;
    }

    /* Read value: Huffman bit + length + data.
     * Use qpack_varint_decode with prefix_bits=7.  Bit 7 (MSB) of
     * the first byte is the Huffman flag, and bits 6-0 are the
     * 7-bit length prefix.  Since qpack_varint_decode masks only
     * the lower prefix_bits, the H flag in bit 7 is automatically
     * excluded and read separately. */
    if (consumed >= buf_len) return -1;
    bool huffman = (buf[consumed] & 0x80) != 0;
    uint64_t vlen;
    uint8_t vbytes;
    if (qpack_varint_decode(buf + consumed, buf_len - consumed,
                             &vlen, &vbytes, 7) < 0)
        return -1;

    consumed += vbytes;
    if (consumed + vlen > buf_len) return -1;
    if (vlen >= sizeof(out->value)) return -1;

    /* Decode Huffman-encoded value or copy raw bytes */
    if (huffman) {
        uint8_t decoded[sizeof(out->value)];
        /* Reserve one byte for the null terminator written below.
         * Huffman decoding can expand data — a 1023-byte wire payload
         * may decode to 1024 bytes, which would overflow out->value[1024]
         * when the null terminator is written.  Passing sizeof(out->value)-1
         * as the output capacity ensures decoded_len ≤ 1023, leaving room
         * for the \0 without overflow. */
        int decoded_len = huffman_decode(buf + consumed, (size_t)vlen,
                                          decoded, sizeof(out->value) - 1);
        if (decoded_len < 0) return -1;
        memcpy(out->value, decoded, (size_t)decoded_len);
        out->value[decoded_len] = '\0';
        out->value_len = (uint16_t)decoded_len;
    } else {
        memcpy(out->value, buf + consumed, (size_t)vlen);
        out->value[vlen] = '\0';
        out->value_len = (uint16_t)vlen;
    }
    consumed += (size_t)vlen;

    return consumed;
}

/* ═══════════════════════════════════════════════════════════════
 * HTTP/3 Frame Write Helpers
 * ═══════════════════════════════════════════════════════════════ */

/**
 * Write a SETTINGS frame to `out`. Returns bytes written.
 * The caller must ensure `out` has at least 256 bytes.
 *
 * Advertises every SETTINGS identifier that Chrome/Quiche checks:
 *   - QPACK_MAX_TABLE_CAPACITY = 0     (static-table-only, no dynamic QPACK)
 *   - MAX_FIELD_SECTION_SIZE = 65536   (reasonable default for QPACK)
 *   - QPACK_BLOCKED_STREAMS = 0        (no dynamic table → 0 blocked)
 *   - ENABLE_CONNECT_PROTOCOL = 1      (RFC 9220 Extended CONNECT)
 *   - H3_DATAGRAM = 1                  (RFC 9297 HTTP/3 DATAGRAM)
 *   - ENABLE_WEBTRANSPORT (draft00) = 1
 *   - WT_MAX_SESSIONS (draft07) = 1    (Chromium requires non-zero)
 */
static int h3_write_settings(uint8_t* out)
{
    uint8_t* p = out;
    uint8_t type_buf[8], len_buf[8];
    uint8_t type_n = varint_encode(H3_FRAME_SETTINGS, type_buf);

    uint8_t  settings_payload[96];
    uint8_t* sp = settings_payload;
    uint8_t id_buf[8], val_buf[8];
    uint8_t id_n, val_n;

    /* QPACK_MAX_TABLE_CAPACITY = 0 (static-table-only) */
    id_n  = varint_encode(0x01, id_buf);
    val_n = varint_encode(0, val_buf);
    memcpy(sp, id_buf, id_n); sp += id_n;
    memcpy(sp, val_buf, val_n); sp += val_n;

    /* MAX_FIELD_SECTION_SIZE = 65536 */
    id_n  = varint_encode(0x06, id_buf);
    val_n = varint_encode(65536, val_buf);
    memcpy(sp, id_buf, id_n); sp += id_n;
    memcpy(sp, val_buf, val_n); sp += val_n;

    /* QPACK_BLOCKED_STREAMS = 0 */
    id_n  = varint_encode(0x07, id_buf);
    val_n = varint_encode(0, val_buf);
    memcpy(sp, id_buf, id_n); sp += id_n;
    memcpy(sp, val_buf, val_n); sp += val_n;

    /* SETTINGS_ENABLE_CONNECT_PROTOCOL = 1 */
    id_n  = varint_encode(H3_SETTINGS_ENABLE_CONNECT_PROTOCOL, id_buf);
    val_n = varint_encode(1, val_buf);
    memcpy(sp, id_buf, id_n); sp += id_n;
    memcpy(sp, val_buf, val_n); sp += val_n;

    /* SETTINGS_H3_DATAGRAM = 1 */
    id_n  = varint_encode(H3_SETTINGS_DATAGRAM, id_buf);
    val_n = varint_encode(1, val_buf);
    memcpy(sp, id_buf, id_n); sp += id_n;
    memcpy(sp, val_buf, val_n); sp += val_n;

    /* SETTINGS_ENABLE_WEBTRANSPORT (draft00) = 1 */
    id_n  = varint_encode(H3_SETTINGS_ENABLE_WEBTRANSPORT, id_buf);
    val_n = varint_encode(1, val_buf);
    memcpy(sp, id_buf, id_n); sp += id_n;
    memcpy(sp, val_buf, val_n); sp += val_n;

    /* SETTINGS_WT_MAX_SESSIONS (draft07, Chromium) = 1 */
    id_n  = varint_encode(H3_SETTINGS_WT_MAX_SESSIONS_DRAFT07, id_buf);
    val_n = varint_encode(1, val_buf);
    memcpy(sp, id_buf, id_n); sp += id_n;
    memcpy(sp, val_buf, val_n); sp += val_n;

    uint32_t settings_payload_len = (uint32_t)(sp - settings_payload);

    uint8_t len_n  = varint_encode(settings_payload_len, len_buf);
    memcpy(p, type_buf, type_n); p += type_n;
    memcpy(p, len_buf, len_n);   p += len_n;
    memcpy(p, settings_payload, settings_payload_len); p += settings_payload_len;

    WT_LOG_INFO("H3: SETTINGS payload %u bytes "
                "(QPACK=0,MAX_FIELD=65536,BLOCKED=0,CONNECT=1,DATAGRAM=1,"
                "WT_DRAFT00=1,MAX_SESS_DRAFT07=1)",
                (unsigned)settings_payload_len);
    return (int)(p - out);
}

/* ═══════════════════════════════════════════════════════════════
 * HEADERS Frame Builder (used by both server and client)
 * ═══════════════════════════════════════════════════════════════ */

/**
 * Encode one Literal Field Line with static Name Reference (RFC 9204 §4.5.4):
 *   01 N=0 T=1 Index(4+) | H=0 ValueLen(7+) | Value
 */
static uint8_t* qpack_put_literal_static_name(
    uint8_t* fp, uint64_t name_idx,
    const char* value, size_t value_len)
{
    uint8_t nb[8], vb[8];
    uint8_t nn = qpack_varint_encode(name_idx, nb, 4);
    nb[0] |= 0x50; /* 01 N=0 T=1 */
    memcpy(fp, nb, nn); fp += nn;
    uint8_t vn = qpack_varint_encode(value_len, vb, 7); /* H=0 in bit7 */
    memcpy(fp, vb, vn); fp += vn;
    memcpy(fp, value, value_len); fp += value_len;
    return fp;
}

/**
 * Write a complete HEADERS frame to `out`.
 *
 * For server response (STATUS):
 *   Encodes: :status: <val>
 *
 * For client request (extended CONNECT, RFC 9220 §4):
 *   Encodes: :method: CONNECT
 *            :protocol: webtransport
 *            :scheme: https
 *            :path: <path>
 *            :authority: <authority>
 *            origin: <origin>            (when supplied)
 *
 * :scheme and :path are mandatory on an extended CONNECT. RFC 9114 §4.3.1
 * exempts only the classic tunnelling form of CONNECT from carrying them;
 * the extended form WebTransport uses must include both, and a request
 * missing a required pseudo-header is malformed.
 *
 * Returns bytes written, or 0 on error.
 */
static int h3_write_headers(uint8_t* out, size_t out_cap,
                            int status_code,
                            const char* method,
                            const char* path,
                            const char* authority,
                            const char* origin)
{
    /* Build the field section in a temp buffer first */
    uint8_t fields[2048];
    uint8_t* fp = fields;

    /* Required Insert Count = 0, Delta Base = 0 (static table only). */
    *fp++ = 0x00;
    *fp++ = 0x00;

    bool is_response = (status_code > 0);

    if (is_response) {
        if (status_code == 200) {
            /* Indexed static table entry 25 = :status 200 (RFC 9204 Appendix A). */
            *fp++ = (uint8_t)(0xC0 | 25);
        } else {
            char s[4]; int slen = 0;
            int sc = status_code;
            if (sc >= 100) { s[slen++] = '0' + (sc / 100); sc %= 100; }
            if (sc >= 10 || slen > 0) { s[slen++] = '0' + (sc / 10); sc %= 10; }
            s[slen++] = '0' + sc;
            fp = qpack_put_literal_static_name(fp, 24, s, (size_t)slen);
        }
    }
    else {
        /* :method: use indexed static entry when CONNECT (entry 15). */
        if (method && strcmp(method, "CONNECT") == 0) {
            *fp++ = (uint8_t)(0xC0 | 15);  /* indexed static 15 = :method CONNECT */
        } else if (method) {
            fp = qpack_put_literal_static_name(fp, 15, method, strlen(method));
        }

        /* :protocol: webtransport — Literal Field Line Without Name Reference
         * (RFC 9204 §4.3.3). There is no static-table entry for :protocol, so
         * the name is spelled out.
         *
         *   0   1   2   3   4   5   6   7
         * +---+---+---+---+---+---+---+---+
         * | 0 | 0 | 1 | N | H |NameLen(3+)|
         * +---+---+---+---+---+---+---+---+
         *
         * The name-length prefix is 3 bits, not 4, and ":protocol" is 9
         * characters. Encoding it with a 4-bit prefix and a length of 10 (as
         * this did) produced 0x3A: the decoder reads bit 3 as the Huffman flag
         * — set, in that byte — and the low 3 bits as a length of 2, so it
         * looked for a 2-byte Huffman-coded name and failed. Every CONNECT
         * this library generated was unparseable; it went unnoticed because
         * only the client path encodes CONNECT, and the server decodes
         * browser-generated requests. */
        const char* proto = "webtransport";
        const char* proto_name = ":protocol";
        size_t proto_name_len = strlen(proto_name);   /* 9 */
        size_t plen = strlen(proto);
        uint8_t nb[8], vb[8];
        uint8_t nb_n = qpack_varint_encode(proto_name_len, nb, 3);
        nb[0] |= 0x20 | 0x10;   /* bits 7-5 = 001, bit 4 = N; bit 3 (H) stays 0 */
        memcpy(fp, nb, nb_n); fp += nb_n;
        memcpy(fp, proto_name, proto_name_len); fp += proto_name_len;
        uint8_t vn = qpack_varint_encode(plen, vb, 7);  /* H = 0 in bit 7 */
        memcpy(fp, vb, vn); fp += vn;
        memcpy(fp, proto, plen); fp += plen;

        /* :scheme: https — indexed static entry 23 (RFC 9204 Appendix A).
         * WebTransport runs over TLS unconditionally, so the scheme is
         * always https and the one-byte indexed form always applies. */
        *fp++ = (uint8_t)(0xC0 | 23);

        /* :path: <path> — literal + static name ref idx=1 (:path /).
         * Required on extended CONNECT, so fall back to "/" rather than
         * omitting the pseudo-header when the caller passes nothing. */
        {
            const char* p = (path && path[0]) ? path : "/";
            fp = qpack_put_literal_static_name(fp, 1, p, strlen(p));
        }

        /* :authority: <authority> — literal + static name ref idx=0 (:authority). */
        if (authority && authority[0]) {
            fp = qpack_put_literal_static_name(fp, 0, authority, strlen(authority));
        }

        /* origin: <origin> — literal + static name ref idx=90 (origin).
         * Browsers always send this; a native client that omits it cannot
         * connect to a server enforcing an origin allow-list. */
        if (origin && origin[0]) {
            fp = qpack_put_literal_static_name(fp, 90, origin, strlen(origin));
        }
    }

    size_t field_size = (size_t)(fp - fields);

    /* Build the full HEADERS frame:
     *   Varint: frame type (0x01)
     *   Varint: frame length (= field_size)
     *   Encoded field section */

    size_t max_frame = 1 + 8 + field_size;
    if (max_frame > out_cap) return 0;

    uint8_t* p = out;
    uint8_t tb[8], lb[8];
    uint8_t tn = varint_encode(H3_FRAME_HEADERS, tb);
    uint8_t ln = varint_encode(field_size, lb);
    memcpy(p, tb, tn); p += tn;
    memcpy(p, lb, ln); p += ln;
    memcpy(p, fields, field_size); p += field_size;

    return (int)(p - out);
}

/* ═══════════════════════════════════════════════════════════════
 * Stream Writer Helpers
 * ═══════════════════════════════════════════════════════════════ */

/* ── Heap send buffer: QUIC_BUFFER descriptor + payload in one block ──
 * msquic retains the QUIC_BUFFER array pointer passed to StreamSend until the
 * send is flushed and SEND_COMPLETE fires — it does not copy the descriptor at
 * call time. Passing a stack-allocated QUIC_BUFFER therefore leaves msquic
 * reading a dead stack frame when QuicStreamSendFlush later runs on a QUIC
 * worker thread, which segfaults inside QuicStreamSendBufferRequest (a garbage
 * Buffer/Length pair handed to memcpy). stream_manager.cpp already learned this
 * and uses the same single-allocation layout (sm_send_req_t); every H3 send has
 * to obey it too, and the SETTINGS bootstrap sent on every connection did not.
 *
 * The whole block is the SEND_COMPLETE ClientContext, so the existing
 * free(ClientContext) handlers release descriptor and payload together. */
typedef struct h3_send_buf_s {
    QUIC_BUFFER buf;        /* buf.Buffer points at data[] */
    uint8_t     data[1];    /* flexible: actual size = buf.Length */
} h3_send_buf_t;

static h3_send_buf_t* h3_send_buf_alloc(const uint8_t* data, uint32_t len)
{
    h3_send_buf_t* sb =
        (h3_send_buf_t*)malloc(offsetof(h3_send_buf_t, data) + (size_t)len);
    if (!sb) return NULL;
    sb->buf.Buffer = sb->data;
    sb->buf.Length = len;
    if (len > 0 && data) memcpy(sb->data, data, (size_t)len);
    return sb;
}

static QUIC_STATUS h3_stream_send(HQUIC stream, const uint8_t* data,
                                  uint32_t len)
{
    h3_send_buf_t* sb = h3_send_buf_alloc(data, len);
    if (!sb) return QUIC_STATUS_OUT_OF_MEMORY;

    QUIC_STATUS st = MsQuic->StreamSend(stream, &sb->buf, 1,
                                         QUIC_SEND_FLAG_FIN, sb);
    if (QUIC_FAILED(st)) {
        free(sb);
        return st;
    }
    return QUIC_STATUS_SUCCESS;
}

#if 0
/* ── Dead Code: h3_stream_send_without_fin ──────────────────────────
 * PRESERVED but DISABLED: This function sends stream data without the
 * FIN flag. It was originally intended for HTTP/3 frame streaming where
 * multiple frames are sent on a single stream without closing it.
 * Current implementation uses h3_stream_send (with FIN) for the simple
 * SETTINGS and HEADERS exchanges. May be needed if HTTP/3 data frame
 * streaming is implemented for browser WebTransport in the future. */
static QUIC_STATUS h3_stream_send_without_fin(HQUIC stream,
                                               const uint8_t* data,
                                               uint32_t len)
{
    uint8_t* copy = (uint8_t*)malloc(len);
    if (!copy) return QUIC_STATUS_OUT_OF_MEMORY;
    memcpy(copy, data, len);

    /* Heap descriptor + payload: msquic keeps the QUIC_BUFFER pointer
     * past this call, so it must not be a local. See h3_send_buf_alloc. */
    h3_send_buf_t* sb = h3_send_buf_alloc(copy, (uint32_t)(len));
    free(copy);

    QUIC_STATUS st = sb ? MsQuic->StreamSend(stream, &sb->buf, 1,
                                         QUIC_SEND_FLAG_NONE, sb)
                              : QUIC_STATUS_OUT_OF_MEMORY;
    if (QUIC_FAILED(st)) {
        free(sb);
        return st;
    }
    return QUIC_STATUS_SUCCESS;
}

#endif /* h3_stream_send_without_fin */

/* ═══════════════════════════════════════════════════════════════
 * Stream Reader (buffered, callback-based)
 * ═══════════════════════════════════════════════════════════════ */

static QUIC_STATUS QUIC_API
h3_stream_cb(HQUIC stream, void* ctx, QUIC_STREAM_EVENT* event);

static QUIC_STREAM_CALLBACK_HANDLER k_h3_stream_handler = h3_stream_cb;

/* Forward declaration for buffered-data processing during shutdown */
typedef int (*h3_data_processor_fn)(h3_stream_ctx_t* sctx);

/* Forward declaration — h3_server_process_data is defined later and
 * called from h3_stream_cb when the stream has a back-pointer to the
 * HTTP/3 session. */
int h3_server_process_data(h3_session_t* h3, h3_stream_ctx_t* sctx);

/* Forward declaration — unlinks a stream context from the session list */
void h3_stream_ctx_unlink(h3_stream_ctx_t* sctx);

/* Forward declaration — h3_client_process_data is defined later and
 * called from h3_client_stream_cb when the client receives the server's
 * response on the CONNECT request stream. */
int h3_client_process_data(h3_session_t* h3, h3_stream_ctx_t* sctx);

static QUIC_STATUS QUIC_API
h3_stream_cb(HQUIC stream, void* ctx, QUIC_STREAM_EVENT* event)
{
    h3_stream_ctx_t* sctx = (h3_stream_ctx_t*)ctx;

    switch (event->Type) {
    case QUIC_STREAM_EVENT_RECEIVE: {
        const QUIC_BUFFER* bufs = event->RECEIVE.Buffers;
        uint32_t count = event->RECEIVE.BufferCount;
        uint32_t total  = event->RECEIVE.TotalBufferLength;

        if (total == 0) return QUIC_STATUS_SUCCESS;

        /* Cap at 64KB for HTTP/3 frames.
         * Use overflow-safe check: ensure total doesn't push us past 64KB.
         * (total > 65536) catches the large-payload overflow case;
         * (recv_offset > 65536 - total) catches the gradual-accumulation case. */
        if (total > 65536 || sctx->recv_offset > 65536 - total) {
            MsQuic->StreamShutdown(stream, QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
            return QUIC_STATUS_ABORTED;
        }

        uint32_t needed = sctx->recv_offset + total;
        if (needed > sctx->recv_capacity) {
            uint32_t new_cap = sctx->recv_capacity ? sctx->recv_capacity * 2 : 4096;
            if (new_cap < needed) new_cap = needed;
            uint8_t* newbuf = (uint8_t*)realloc(sctx->recv_buf, new_cap);
            if (!newbuf) {
                /* realloc failure under memory pressure — the original
                 * buffer is still valid (realloc semantics) but is too
                 * small for incoming data.  Returning OUT_OF_MEMORY would
                 * leave the stream open with an undersized buffer, causing
                 * the next RECEIVE to hit the same failure in a tight loop.
                 * Shut down the stream to prevent corruption and let the
                 * peer retry. */
                WT_LOG_ERROR("h3_stream_cb: realloc(%u) failed for stream — "
                             "shutting down (recv_offset=%u, total=%u)",
                             new_cap, sctx->recv_offset, total);
                MsQuic->StreamShutdown(stream,
                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                return QUIC_STATUS_OUT_OF_MEMORY;
            }
            sctx->recv_buf = newbuf;
            sctx->recv_capacity = new_cap;
        }

        for (uint32_t i = 0; i < count; i++) {
            memcpy(sctx->recv_buf + sctx->recv_offset,
                   bufs[i].Buffer, bufs[i].Length);
            sctx->recv_offset += bufs[i].Length;
        }

        /* Process buffered data for HTTP/3 handshake detection
         * and state machine advancement. If the handshake completes
         * synchronously, h3_server_process_data calls h3->on_ready
         * which creates the wt_session.
         *
         * ── M-1: refcount-based lifetime for h3_session_t ─────────
         * h3_session_acquire() CAS-loops to increment h3->ref_count
         * only if ref_count > 0 AND released == false.  If it succeeds,
         * h3 is guaranteed to remain alive for the entire duration of
         * h3_server_process_data — even if h3_session_free() runs
         * concurrently on another thread and sets released=true.
         * h3_session_free defers the actual free() + mutex_destroy to
         * h3_session_release(), which only executes them when
         * ref_count == 0 AND released == true.
         *
         * This is the same pattern as wt_session_acquire/release
         * (session.h/session.c) and eliminates the TOCTOU window that
         * a simple freed-flag check would have (check passes, then
         * free runs on another thread while process_data is executing). */
        h3_session_t* h3 = sctx->h3;
        if (h3 && h3_session_acquire(h3)) {
            int hr = h3_server_process_data(h3, sctx);
            h3_session_release(h3);
            if (hr < 0) {
                MsQuic->StreamShutdown(stream,
                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
            }
        }
        return QUIC_STATUS_SUCCESS;
    }

    case QUIC_STREAM_EVENT_PEER_SEND_SHUTDOWN: {
        /* Stream data complete (peer sent FIN).
         * Process any buffered data that arrived in prior RECEIVE
         * events but hasn't been consumed yet. This handles the case
         * where the HTTP/3 handshake frames arrive in the same stream
         * event as the FIN — without this call, the last chunk of
         * handshake data would remain buffered and unprocessed,
         * causing the handshake to hang indefinitely. */
        if (sctx->recv_offset > 0 && sctx->recv_buf != NULL) {
            /* Snapshot h3 and acquire a ref before dereference.
             * See RECEIVE case for the full M-1 refcount analysis —
             * same race applies here. */
            h3_session_t* h3 = sctx->h3;
            if (h3 && h3_session_acquire(h3)) {
                int hr = h3_server_process_data(h3, sctx);
                h3_session_release(h3);
                if (hr < 0) {
                    MsQuic->StreamShutdown(stream,
                        QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                }
            }
        }
        return QUIC_STATUS_SUCCESS;
    }

    case QUIC_STREAM_EVENT_SEND_COMPLETE:
        free(event->SEND_COMPLETE.ClientContext);
        return QUIC_STATUS_SUCCESS;

    case QUIC_STREAM_EVENT_PEER_SEND_ABORTED:
    case QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE: {
        /* CAS guard: msquic fires PEER_SEND_ABORTED (if peer aborts)
         * followed by the terminal SHUTDOWN_COMPLETE.  Without this
         * guard the stream context is freed twice — UAF then double-free. */
        int expected = 0;
        if (!atomic_compare_exchange_strong(&sctx->freed, &expected, 1)) {
            /* Already freed by a prior callback — ignore. */
            return QUIC_STATUS_SUCCESS;
        }
        if (sctx->h3) h3_stream_ctx_unlink(sctx);
        free(sctx->recv_buf);
        free(sctx);
        MsQuic->StreamClose(stream);
        return QUIC_STATUS_SUCCESS;
    }

    default:
        return QUIC_STATUS_SUCCESS;
    }
}

static h3_stream_ctx_t* h3_stream_ctx_create(HQUIC stream, h3_session_t* h3)
{
    h3_stream_ctx_t* sctx = (h3_stream_ctx_t*)calloc(1, sizeof(*sctx));
    if (!sctx) return NULL;
    sctx->quic_stream = stream;
    sctx->stream_type = -1;
    sctx->h3 = h3;
    MsQuic->SetCallbackHandler(stream,
                                (void*)(uintptr_t)k_h3_stream_handler, sctx);
    /* Link into the session's stream context list for cleanup tracking */
    if (h3) {
        H3_LOCK(h3);
        sctx->next = h3->stream_ctx_list;
        h3->stream_ctx_list = sctx;
        H3_UNLOCK(h3);
    }
    return sctx;
}

/* ── Unlink a stream context from the session's tracking list ─────
 * Called from SHUTDOWN_COMPLETE / PEER_SEND_ABORTED when a stream
 * context is freed through the normal callback chain.  Removes the
 * entry from the singly-linked list by pointer-to-pointer walk. */
void h3_stream_ctx_unlink(h3_stream_ctx_t* sctx)
{
    if (!sctx || !sctx->h3) return;
    h3_session_t* h3 = sctx->h3;
    H3_LOCK(h3);
    h3_stream_ctx_t** pp = &h3->stream_ctx_list;
    while (*pp) {
        if (*pp == sctx) {
            *pp = sctx->next;
            sctx->next = NULL;
            sctx->h3 = NULL;
            H3_UNLOCK(h3);
            return;
        }
        pp = &(*pp)->next;
    }
    H3_UNLOCK(h3);
}

/* ── Send-only control-stream context ───────────────────────────
 * Tracks H3 control streams so we can:
 *  - free send buffers on SEND_COMPLETE
 *  - StreamClose exactly once on SHUTDOWN_COMPLETE
 *  - clear h3->server_control_stream / client_control_stream safely
 *
 * NEVER call StreamClose from error paths when this callback is installed —
 * use StreamShutdown(ABORT) and let SHUTDOWN_COMPLETE close the handle.
 * Double StreamClose → QuicStreamFree SEGV (production Login crash after
 * browser SETTINGS).
 */
typedef struct h3_send_only_ctx_s {
    HQUIC           stream;
    h3_session_t*   h3;
    int             role;       /* 1 = server_control, 2 = client_control, 0 = other */
    /* Claim flag for "who performs StreamClose + free(ctx)". Exactly one of the
     * SHUTDOWN_COMPLETE callback and h3_session_free wins the exchange; the
     * loser must not touch the ctx afterwards. Atomic because those two run on
     * a QUIC worker and the application thread respectively. */
    atomic_int      closed;     /* 0 = open, 1 = close+free claimed */
    volatile int    send_freed; /* 1 after ClientContext free (once) — atomic */
    /* Filled before StreamStart; sent from START_COMPLETE so we never
     * StreamSend before the stream is fully started (QuicStreamSendBufferRequest crash). */
    uint8_t*        pending_send;
    uint32_t        pending_send_len;
} h3_send_only_ctx_t;

static void h3_send_only_clear_session_ref(h3_send_only_ctx_t* c, HQUIC stream)
{
    if (!c || !c->h3) return;
    H3_LOCK(c->h3);
    if (c->role == 1 && c->h3->server_control_stream == stream)
        c->h3->server_control_stream = NULL;
    if (c->role == 2 && c->h3->client_control_stream == stream)
        c->h3->client_control_stream = NULL;
    H3_UNLOCK(c->h3);
}

/* Abort a send-only stream without StreamClose (callback owns close). */
static void h3_send_only_abort(HQUIC stream)
{
    if (!stream) return;
    MsQuic->StreamShutdown(
        stream,
        QUIC_STREAM_SHUTDOWN_FLAG_ABORT | QUIC_STREAM_SHUTDOWN_FLAG_IMMEDIATE,
        0);
}

/**
 * Abort AND close a send-only stream synchronously, releasing its context.
 *
 * Used on the connection-teardown path. Aborting alone is not enough there:
 * StreamShutdown is asynchronous, and the caller (the connection's
 * SHUTDOWN_COMPLETE handler) goes on to call ConnectionClose immediately, so
 * the stream's own SHUTDOWN_COMPLETE — the only place that would StreamClose
 * the handle — is never delivered. The stream object then keeps a rundown
 * reference on the registration and MsQuicRegistrationClose blocks forever
 * inside CxPlatRundownReleaseAndWait, hanging wt_server_destroy.
 *
 * StreamClose is safe to call here despite the "let SHUTDOWN_COMPLETE close it"
 * rule above, because the atomic claim makes exactly one path do it, and
 * StreamClose itself waits for any in-flight callback on that stream to return
 * before it completes.
 */
static void h3_send_only_close_now(HQUIC stream, h3_send_only_ctx_t* c)
{
    if (!stream) return;
    if (!c) {
        MsQuic->StreamClose(stream);
        return;
    }
    if (atomic_exchange(&c->closed, 1) == 0) {
        MsQuic->StreamClose(stream);
        if (c->pending_send) {
            free(c->pending_send);
            c->pending_send = NULL;
        }
        free(c);
    }
}

static int h3_send_only_do_send(h3_send_only_ctx_t* c)
{
    if (!c || !c->stream || !c->pending_send || c->pending_send_len == 0)
        return -1;

    /* Move the payload into a heap block that also carries the QUIC_BUFFER
     * descriptor — msquic keeps that descriptor alive past this call, so it
     * must not be a local. See h3_send_buf_alloc. */
    uint32_t len = c->pending_send_len;
    h3_send_buf_t* sb = h3_send_buf_alloc(c->pending_send, len);
    if (!sb) {
        WT_LOG_ERROR("H3: send-only alloc failed role=%d len=%u", c->role, len);
        return -1;
    }
    free(c->pending_send);
    c->pending_send = NULL;
    c->pending_send_len = 0;

    /* Ownership of sb transfers to the SEND_COMPLETE ClientContext. */
    QUIC_STATUS st = MsQuic->StreamSend(
        c->stream, &sb->buf, 1, QUIC_SEND_FLAG_NONE, sb);
    if (QUIC_FAILED(st)) {
        WT_LOG_ERROR("H3: send-only StreamSend failed role=%d st=0x%x stream=%p len=%u",
                     c->role, st, (void*)c->stream, len);
        free(sb);
        return -1;
    }
    return 0;
}

static QUIC_STATUS QUIC_API
h3_send_only_stream_cb(HQUIC stream, void* ctx, QUIC_STREAM_EVENT* event)
{
    h3_send_only_ctx_t* c = (h3_send_only_ctx_t*)ctx;

    switch (event->Type) {
    case QUIC_STREAM_EVENT_START_COMPLETE:
        /* Stream is ready — flush deferred first send (never StreamSend before this). */
        if (c && c->pending_send) {
            if (event->START_COMPLETE.Status != 0 &&
                QUIC_FAILED(event->START_COMPLETE.Status)) {
                WT_LOG_ERROR("H3: send-only START_COMPLETE failed role=%d st=0x%x",
                             c->role, event->START_COMPLETE.Status);
                free(c->pending_send);
                c->pending_send = NULL;
                c->pending_send_len = 0;
                h3_send_only_abort(stream);
            } else {
                if (h3_send_only_do_send(c) != 0)
                    h3_send_only_abort(stream);
            }
        }
        break;

    case QUIC_STREAM_EVENT_SEND_COMPLETE: {
        /* Free app send buffer exactly once. */
        void* app_buf = event->SEND_COMPLETE.ClientContext;
        if (app_buf) {
            int already = 0;
            if (c) {
#if defined(WT_PLATFORM_WINDOWS)
                already = (int)InterlockedExchange((volatile LONG*)&c->send_freed, 1);
#else
                already = __sync_lock_test_and_set(&c->send_freed, 1);
#endif
            }
            if (!already)
                free(app_buf);
        }
        break;
    }

    case QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE: {
        h3_send_only_clear_session_ref(c, stream);
        if (c) {
            /* Close and free only if we win the claim. When h3_session_free
             * already took it, that path owns both — touching c after losing
             * the exchange (including free) would race its free. */
            if (atomic_exchange(&c->closed, 1) == 0) {
                if (c->pending_send) {
                    free(c->pending_send);
                    c->pending_send = NULL;
                }
                MsQuic->StreamClose(stream);
                free(c);
            }
        } else {
            MsQuic->StreamClose(stream);
        }
        break;
    }

    default:
        break;
    }
    return QUIC_STATUS_SUCCESS;
}

/** Allocate send-only ctx bound to stream; returns NULL on OOM. */
static h3_send_only_ctx_t* h3_send_only_ctx_create(
    HQUIC stream, h3_session_t* h3, int role)
{
    h3_send_only_ctx_t* c = (h3_send_only_ctx_t*)calloc(1, sizeof(*c));
    if (!c) return NULL;
    c->stream = stream;
    c->h3 = h3;
    c->role = role;
    c->closed = 0;
    c->send_freed = 0;
    c->pending_send = NULL;
    c->pending_send_len = 0;
    return c;
}

/**
 * Stream callback for the client's CONNECT request stream during the
 * HTTP/3 WebTransport handshake.
 *
 * Unlike h3_send_only_stream_cb, this callback handles RECEIVE events:
 * it buffers the server's response data and calls h3_client_process_data
 * to parse the HEADERS frame and detect :status (200 = success).
 */
static QUIC_STATUS QUIC_API
h3_client_stream_cb(HQUIC stream, void* ctx, QUIC_STREAM_EVENT* event)
{
    h3_stream_ctx_t* sctx = (h3_stream_ctx_t*)ctx;

    switch (event->Type) {
    case QUIC_STREAM_EVENT_RECEIVE: {
        const QUIC_BUFFER* bufs = event->RECEIVE.Buffers;
        uint32_t count = event->RECEIVE.BufferCount;
        uint32_t total = event->RECEIVE.TotalBufferLength;

        if (total == 0) return QUIC_STATUS_SUCCESS;

        /* Cap at 64KB for HTTP/3 frames.
         * Use overflow-safe check: ensure total doesn't push us past 64KB.
         * (total > 65536) catches the large-payload overflow case;
         * (recv_offset > 65536 - total) catches the gradual-accumulation case. */
        if (total > 65536 || sctx->recv_offset > 65536 - total) {
            MsQuic->StreamShutdown(stream, QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
            return QUIC_STATUS_ABORTED;
        }

        uint32_t needed = sctx->recv_offset + total;
        if (needed > sctx->recv_capacity) {
            uint32_t new_cap = sctx->recv_capacity ? sctx->recv_capacity * 2 : 4096;
            if (new_cap < needed) new_cap = needed;
            uint8_t* newbuf = (uint8_t*)realloc(sctx->recv_buf, new_cap);
            if (!newbuf) {
                /* realloc failure under memory pressure — shut down the
                 * stream to prevent an infinite retry loop (see server-side
                 * h3_stream_cb for full rationale). */
                WT_LOG_ERROR("h3_client_stream_cb: realloc(%u) failed for stream — "
                             "shutting down (recv_offset=%u, total=%u)",
                             new_cap, sctx->recv_offset, total);
                MsQuic->StreamShutdown(stream,
                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                return QUIC_STATUS_OUT_OF_MEMORY;
            }
            sctx->recv_buf = newbuf;
            sctx->recv_capacity = new_cap;
        }

        for (uint32_t i = 0; i < count; i++) {
            memcpy(sctx->recv_buf + sctx->recv_offset,
                   bufs[i].Buffer, bufs[i].Length);
            sctx->recv_offset += bufs[i].Length;
        }

        /* Process buffered data for client HTTP/3 handshake.
         * Acquire a ref on h3 — same M-1 refcount pattern as the
         * server-side RECEIVE path. */
        h3_session_t* h3 = sctx->h3;
        if (h3 && h3_session_acquire(h3)) {
            h3_client_process_data(h3, sctx);
            h3_session_release(h3);
        }
        return QUIC_STATUS_SUCCESS;
    }

    case QUIC_STREAM_EVENT_PEER_SEND_SHUTDOWN: {
        /* Process any buffered data that hasn't been consumed yet.
         * This handles the case where the 200 OK data arrives in the
         * same stream event as the FIN from the server. */
        if (sctx->recv_offset > 0 && sctx->recv_buf != NULL) {
            h3_session_t* h3 = sctx->h3;
            if (h3 && h3_session_acquire(h3)) {
                h3_client_process_data(h3, sctx);
                h3_session_release(h3);
            }
        }
        return QUIC_STATUS_SUCCESS;
    }

    case QUIC_STREAM_EVENT_SEND_COMPLETE:
        free(event->SEND_COMPLETE.ClientContext);
        return QUIC_STATUS_SUCCESS;

    case QUIC_STREAM_EVENT_PEER_SEND_ABORTED:
    case QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE: {
        /* CAS guard: msquic fires PEER_SEND_ABORTED (if peer aborts)
         * followed by the terminal SHUTDOWN_COMPLETE.  Without this
         * guard the stream context is freed twice — UAF then double-free. */
        int expected = 0;
        if (!atomic_compare_exchange_strong(&sctx->freed, &expected, 1)) {
            /* Already freed by a prior callback — ignore. */
            return QUIC_STATUS_SUCCESS;
        }
        if (sctx->h3) h3_stream_ctx_unlink(sctx);
        free(sctx->recv_buf);
        free(sctx);
        MsQuic->StreamClose(stream);
        return QUIC_STATUS_SUCCESS;
    }

    default:
        return QUIC_STATUS_SUCCESS;
    }
}

/* ═══════════════════════════════════════════════════════════════
 * HTTP/3 Frame Parser
 * ═══════════════════════════════════════════════════════════════ */

/**
 * Parse all HTTP/3 frames from a buffer and call handlers.
 * Updates `offset` to point past the last fully-parsed frame.
 */
typedef void (*h3_frame_handler)(void* ctx, uint64_t frame_type,
                                  const uint8_t* payload, uint64_t payload_len);

static int h3_parse_frames(const uint8_t* buf, size_t buf_len,
                           size_t* offset,
                           h3_frame_handler handler, void* ctx)
{
    *offset = 0;
    while (*offset < buf_len) {
        uint64_t frame_type, frame_len;
        uint8_t type_bytes, len_bytes;

        if (varint_decode(buf + *offset, buf_len - *offset,
                          &frame_type, &type_bytes) < 0)
            return -1;
        *offset += type_bytes;

        if (varint_decode(buf + *offset, buf_len - *offset,
                          &frame_len, &len_bytes) < 0)
            return -1;
        *offset += len_bytes;

        /* Overflow-safe bounds check: frame_len > buf_len catches huge
         * frames before the subtraction.  Without this guard, *offset +
         * frame_len can wrap around to a small value and pass the check,
         * producing an infinite loop that hangs a QUIC worker thread. */
        if (frame_len > buf_len || *offset > buf_len - (size_t)frame_len) {
            /* Frame extends beyond buffer — partial read,
             * wait for more data */
            *offset -= (type_bytes + len_bytes);
            return 0;
        }

        if (handler)
            handler(ctx, frame_type, buf + *offset, frame_len);

        *offset += (size_t)frame_len;
    }
    return 0;
}

/* ═══════════════════════════════════════════════════════════════
 * HEADERS Parser — Extract Pseudo-Headers from EncodedFieldSection
 * ═══════════════════════════════════════════════════════════════
 * Parses QPACK-encoded field lines and extracts pseudo-headers
 * into a simple struct.
 *
 * On request/push streams the EncodedFieldSection is prefixed with:
 *   - Required Insert Count (8-bit prefix varint, RFC 9204 §4.5.1)
 *   - Delta Base           (7-bit prefix varint + sign, RFC 9204 §4.5.2)
 * Both are typically 0x00 for initial requests with no dynamic table.
 */

typedef struct {
    char    method[16];
    char    protocol[32];
    char    path[256];
    char    authority[256];
    char    status[8];
    char    origin[256];
} h3_parsed_headers_t;

/** Forward decl for hex dump — defined below with HEADERS parser. */
static void h3_hex_dump(const char* label, const uint8_t* data, size_t len);

/* Per-control-stream frame bookkeeping, passed as the h3_parse_frames ctx. */
typedef struct {
    bool seen_first_frame;   /* set once the first control frame is seen */
    bool settings_ok;        /* false if the first frame was not SETTINGS */
} h3_control_state_t;

/** Log peer SETTINGS frame id/value pairs with human-readable names. */
static void h3_log_peer_settings_frame(void* ctx, uint64_t frame_type,
                                       const uint8_t* payload,
                                       uint64_t payload_len)
{
    /* RFC 9114 §6.2.1: the first frame on each control stream MUST be
     * SETTINGS. A peer that opens with anything else is a connection error
     * of type H3_MISSING_SETTINGS, not something to log and carry on with. */
    h3_control_state_t* cs = (h3_control_state_t*)ctx;
    if (cs && !cs->seen_first_frame) {
        cs->seen_first_frame = true;
        cs->settings_ok = (frame_type == H3_FRAME_SETTINGS);
        if (!cs->settings_ok) {
            WT_LOG_ERROR(
                "H3: first control-stream frame is type=0x%llx, expected "
                "SETTINGS (0x04) — H3_MISSING_SETTINGS",
                (unsigned long long)frame_type);
            return;
        }
    }

    if (frame_type != H3_FRAME_SETTINGS) {
        WT_LOG_INFO("H3: peer control frame type=0x%llx len=%llu",
                    (unsigned long long)frame_type,
                    (unsigned long long)payload_len);
        return;
    }

    WT_LOG_INFO("H3: peer SETTINGS payload %llu bytes",
                (unsigned long long)payload_len);

    size_t off = 0;
    int pairs = 0;
    while (off < (size_t)payload_len) {
        uint64_t id = 0, val = 0;
        uint8_t ib = 0, vb = 0;
        if (varint_decode(payload + off, (size_t)payload_len - off,
                          &id, &ib) < 0)
            break;
        off += ib;
        if (off >= (size_t)payload_len) break;
        if (varint_decode(payload + off, (size_t)payload_len - off,
                          &val, &vb) < 0)
            break;
        off += vb;
        pairs++;

        const char* name = NULL;
        if (id == H3_SETTINGS_ENABLE_CONNECT_PROTOCOL)
            name = "ENABLE_CONNECT_PROTOCOL";
        else if (id == H3_SETTINGS_DATAGRAM)
            name = "H3_DATAGRAM";
        else if (id == H3_SETTINGS_DATAGRAM_DRAFT04)
            name = "H3_DATAGRAM_DRAFT04";
        else if (id == H3_SETTINGS_ENABLE_WEBTRANSPORT)
            name = "ENABLE_WEBTRANSPORT";
        else if (id == H3_SETTINGS_WT_MAX_SESSIONS_DRAFT07)
            name = "WT_MAX_SESSIONS_DRAFT07";
        else if (id == H3_SETTINGS_WT_MAX_SESSIONS_ALT)
            name = "WT_MAX_SESSIONS_ALT";
        else if (id == 0x06)
            name = "MAX_FIELD_SECTION_SIZE";
        else if (id == 0x01)
            name = "QPACK_MAX_TABLE_CAPACITY";
        else if (id == 0x07)
            name = "QPACK_BLOCKED_STREAMS";

        if (name) {
            WT_LOG_INFO("H3: peer SETTINGS %s (0x%llx)=%llu",
                        name,
                        (unsigned long long)id,
                        (unsigned long long)val);
        } else {
            WT_LOG_INFO("H3: peer SETTINGS id=0x%llx val=%llu",
                        (unsigned long long)id,
                        (unsigned long long)val);
        }
    }
    if (pairs == 0 && payload_len > 0) {
        WT_LOG_WARN("H3: peer SETTINGS present but no id/value pairs parsed");
        h3_hex_dump("peer-SETTINGS", payload, (size_t)payload_len);
    }
}

/** Log a hex+ascii dump at WT_LOG_INFO level (16 bytes/line). */
static void h3_hex_dump(const char* label, const uint8_t* data, size_t len)
{
    if (!data || len == 0) return;
    char line[80];
    WT_LOG_INFO("%s (%zu bytes):", label, len);
    for (size_t i = 0; i < len; i += 16) {
        int pos = 0;
        pos += snprintf(line + pos, sizeof(line) - (size_t)pos, "  %04zx: ", i);
        for (size_t j = 0; j < 16; j++) {
            if (i + j < len)
                pos += snprintf(line + pos, sizeof(line) - (size_t)pos,
                                "%02x ", data[i + j]);
            else
                pos += snprintf(line + pos, sizeof(line) - (size_t)pos, "   ");
        }
        pos += snprintf(line + pos, sizeof(line) - (size_t)pos, " |");
        for (size_t j = 0; j < 16 && i + j < len; j++) {
            uint8_t c = data[i + j];
            line[pos++] = (c >= 32 && c < 127) ? (char)c : '.';
        }
        line[pos++] = '|';
        line[pos] = '\0';
        WT_LOG_INFO("%s", line);
    }
}

static bool h3_parse_headers(const uint8_t* data, uint64_t data_len,
                             h3_parsed_headers_t* out)
{
    memset(out, 0, sizeof(*out));
    uint64_t parsed = 0;

    /* ── Skip field-section prefix (RFC 9204 §4.3) ─────────────
     * Required Insert Count: 8-bit prefix varint.
     * For initial requests with no dynamic-table refs this is 0x00. */
    if (parsed < data_len) {
        uint64_t ric_val;
        uint8_t ric_nbytes;
        if (qpack_varint_decode(data + parsed, (size_t)(data_len - parsed),
                                &ric_val, &ric_nbytes, 8) == 0) {
            parsed += ric_nbytes;
            if (ric_val != 0) {
                WT_LOG_WARN("H3: QPACK Required Insert Count=%llu not supported "
                            "(server is static-table only; peer must not block)",
                            (unsigned long long)ric_val);
                h3_hex_dump("HEADERS-RIC", data, (size_t)data_len);
                return false;
            }
        } else {
            WT_LOG_ERROR("QPACK: failed to decode Required Insert Count "
                         "(data_len=%llu)", data_len);
            h3_hex_dump("HEADERS payload (truncated RIC)", data, (size_t)data_len);
            return false;
        }
    }

    /* Delta Base: 7-bit prefix + sign bit (RFC 9204 §4.5.2).
     * Typically 0x00 (sign=positive, value=0). */
    if (parsed < data_len) {
        uint64_t base_val;
        uint8_t base_nbytes;
        if (qpack_varint_decode(data + parsed, (size_t)(data_len - parsed),
                                &base_val, &base_nbytes, 7) == 0) {
            parsed += base_nbytes;
        } else {
            WT_LOG_ERROR("QPACK: failed to decode Delta Base "
                         "(data_len=%llu, parsed=%llu)", data_len, parsed);
            h3_hex_dump("HEADERS payload (truncated Base)", data, (size_t)data_len);
            return false;
        }
    }

    WT_LOG_INFO("QPACK: RIC+Base skipped (%llu bytes prefix, %llu remaining for fields)",
                parsed, data_len - parsed);

    /* ── Parse field lines ──────────────────────────────────── */
    while (parsed < data_len) {
        h3_header_t hdr;
        int consumed = qpack_parse_field(data + parsed,
                                         (size_t)(data_len - parsed), &hdr);
        if (consumed < 0) {
            WT_LOG_WARN("QPACK: field decode failed at offset %llu/%llu",
                        parsed, data_len);
            h3_hex_dump("HEADERS payload (decode failure point)",
                        data, (size_t)data_len);
            return false;
        }
        parsed += (uint64_t)consumed;

        /* Map known pseudo-headers */
        if (hdr.name_len == 7 && memcmp(hdr.name, ":method", 7) == 0) {
            size_t cp = hdr.value_len < sizeof(out->method)-1
                        ? hdr.value_len : sizeof(out->method)-1;
            memcpy(out->method, hdr.value, cp);
            WT_LOG_INFO("QPACK: parsed :method = %s", out->method);
        }
        else if (hdr.name_len == 9 && memcmp(hdr.name, ":protocol", 9) == 0) {
            size_t cp = hdr.value_len < sizeof(out->protocol)-1
                        ? hdr.value_len : sizeof(out->protocol)-1;
            memcpy(out->protocol, hdr.value, cp);
            WT_LOG_INFO("QPACK: parsed :protocol = %s", out->protocol);
        }
        else if (hdr.name_len == 5 && memcmp(hdr.name, ":path", 5) == 0) {
            size_t cp = hdr.value_len < sizeof(out->path)-1
                        ? hdr.value_len : sizeof(out->path)-1;
            memcpy(out->path, hdr.value, cp);
        }
        else if (hdr.name_len == 10 && memcmp(hdr.name, ":authority", 10) == 0) {
            size_t cp = hdr.value_len < sizeof(out->authority)-1
                        ? hdr.value_len : sizeof(out->authority)-1;
            memcpy(out->authority, hdr.value, cp);
            WT_LOG_INFO("QPACK: parsed :authority = %s", out->authority);
        }
        else if (hdr.name_len == 7 && memcmp(hdr.name, ":status", 7) == 0) {
            size_t cp = hdr.value_len < sizeof(out->status)-1
                        ? hdr.value_len : sizeof(out->status)-1;
            memcpy(out->status, hdr.value, cp);
        }
        else if (hdr.name_len == 6 && memcmp(hdr.name, "origin", 6) == 0) {
            size_t cp = hdr.value_len < sizeof(out->origin)-1
                        ? hdr.value_len : sizeof(out->origin)-1;
            memcpy(out->origin, hdr.value, cp);
            WT_LOG_INFO("QPACK: parsed origin = %s", out->origin);
        }
        /* Ignore unknown headers */
    }
    return true;
}

/* ═══════════════════════════════════════════════════════════════
 * Server-Side Handshake
 * ═══════════════════════════════════════════════════════════════ */

typedef struct {
    h3_session_t*       h3;
    h3_stream_ctx_t*    ctrl_sctx;   /* client's control stream */
    h3_stream_ctx_t*    req_sctx;    /* client's CONNECT request stream */
    bool                settings_sent;
} h3_server_ctx_t;

/* ═══════════════════════════════════════════════════════════════
 * Public API Implementation
 * ═══════════════════════════════════════════════════════════════ */

/* ── h3_session_t refcounting ───────────────────────────────────
 * Same CAS-loop pattern as wt_session_acquire / wt_session_release
 * (session.h/session.c).  h3_session_t is shared between the
 * connection owner and in-flight RECEIVE/PEER_SEND_SHUTDOWN callbacks
 * on QUIC worker threads.  The owner sets released=true in
 * h3_session_free; callbacks acquire before entering process_data and
 * release after.  The session is only freed when ref_count reaches 0
 * AND released is true. */

bool h3_session_acquire(h3_session_t* h3)
{
    if (!h3) return false;
    unsigned int expected = atomic_load(&h3->ref_count);
    for (;;) {
        if (expected == 0) return false;
        if (atomic_load(&h3->released)) return false;
        if (atomic_compare_exchange_strong(
                &h3->ref_count, &expected, expected + 1))
            return true;
    }
}

void h3_session_release(h3_session_t* h3)
{
    if (!h3) return;
    unsigned int expected = atomic_load(&h3->ref_count);
    for (;;) {
        if (expected == 0) return;   /* underflow guard */
        unsigned int next = expected - 1;
        if (atomic_compare_exchange_strong(
                &h3->ref_count, &expected, next)) {
            if (next == 0 && atomic_load(&h3->released)) {
                /* Last reference + owner has released —
                 * safe to destroy the mutex and free. */
#if defined(WT_PLATFORM_WINDOWS)
                DeleteCriticalSection(&h3->stream_ctx_lock);
#else
                pthread_mutex_destroy(&h3->stream_ctx_lock);
#endif
                free(h3);
            }
            return;
        }
    }
}

/* ═══════════════════════════════════════════════════════════════
 * Proactive SETTINGS Bootstrap
 * ═══════════════════════════════════════════════════════════════
 *
 * Chrome/Quiche expects the server to send SETTINGS as soon as
 * encryption is established — before or concurrently with the
 * client's control stream.  The bootstrap mechanism defers
 * StreamOpen/StreamSend to the poll (application) thread because
 * calling them from inside a QUIC connection/stream callback
 * re-enters msquic and causes QuicOperationFree crashes.
 *
 * h3_server_send_initial_settings()  ← called from CONNECTED (QUIC cb)
 *   └─ h3_server_request_settings_bootstrap()  ← sets flag
 *        └─ h3_server_poll_deferred()           ← called from poll thread
 *             └─ h3_server_bootstrap_settings() ← does the real work
 */

/**
 * Open the server control stream (type 0x00) and send a SETTINGS frame.
 * Called from the application poll thread ONLY — NEVER from a QUIC
 * stream/connection callback.
 */
static int h3_server_bootstrap_settings(h3_session_t* h3, const char* reason)
{
    if (!h3 || !h3->quic_conn) return -1;
    if (!reason) reason = "unknown";

    if (h3->server_settings_sent) {
        WT_LOG_INFO("H3: bootstrap SETTINGS skipped (already sent) state=%d reason=%s",
                    (int)h3->server_state, reason);
        return 0;
    }

    /* Already have a control stream handle from a partial prior attempt —
     * do not open a second control stream (H3 forbids duplicate control). */
    if (h3->server_control_stream) {
        WT_LOG_WARN("H3: bootstrap: control stream already open but "
                    "server_settings_sent=0 — marking sent to avoid dual control");
        h3->server_settings_sent = true;
        if (h3->server_state < H3_SRV_WAIT_CONNECT)
            h3->server_state = H3_SRV_WAIT_CONNECT;
        return 0;
    }

    uint8_t settings_buf[128];
    int settings_len = h3_write_settings(settings_buf);
    if (settings_len <= 0) {
        WT_LOG_ERROR("H3: bootstrap: h3_write_settings failed");
        return -1;
    }

    h3_send_only_ctx_t* send_ctx =
        h3_send_only_ctx_create(NULL, h3, 1 /* server_control */);
    if (!send_ctx) {
        WT_LOG_ERROR("H3: bootstrap: OOM allocating server control stream context");
        return -1;
    }

    /* Control stream = single stream-type byte 0x00 + one SETTINGS frame. */
    uint8_t* srv_data = (uint8_t*)malloc((size_t)(1 + settings_len));
    if (!srv_data) {
        free(send_ctx);
        return -1;
    }
    srv_data[0] = H3_STREAM_CONTROL; /* exactly one type byte */
    memcpy(srv_data + 1, settings_buf, (size_t)settings_len);
    /* Guard: settings frame must start with SETTINGS type 0x04, not 0x00. */
    if (settings_len < 1 || settings_buf[0] != H3_FRAME_SETTINGS) {
        WT_LOG_ERROR("H3: bootstrap: SETTINGS frame first byte=0x%02x (want 0x04)",
                     settings_len > 0 ? settings_buf[0] : 0xff);
        free(srv_data);
        free(send_ctx);
        return -1;
    }

    HQUIC srv_ctrl = NULL;
    QUIC_STATUS st = MsQuic->StreamOpen(
        h3->quic_conn, QUIC_STREAM_OPEN_FLAG_UNIDIRECTIONAL,
        h3_send_only_stream_cb, send_ctx, &srv_ctrl);
    if (QUIC_FAILED(st)) {
        WT_LOG_ERROR("H3: bootstrap: StreamOpen server control failed: 0x%x", st);
        free(srv_data);
        free(send_ctx);
        return -1;
    }
    send_ctx->stream = srv_ctrl;

    H3_LOCK(h3);
    h3->server_control_stream = srv_ctrl;
    h3->server_control_ctx = send_ctx;
    H3_UNLOCK(h3);

    /* Queue the payload for START_COMPLETE delivery — never StreamSend
     * before the stream is fully started (QuicStreamSendBufferRequest crash). */
    send_ctx->pending_send = srv_data;
    send_ctx->pending_send_len = (uint32_t)(1 + settings_len);

    WT_LOG_INFO("H3: bootstrap: server control stream open %p (uni-only; no server QPACK)",
                (void*)srv_ctrl);

    /* Start control stream. StreamSend happens in START_COMPLETE. */
    st = MsQuic->StreamStart(srv_ctrl, QUIC_STREAM_START_FLAG_SHUTDOWN_ON_FAIL);
    if (QUIC_FAILED(st)) {
        WT_LOG_ERROR("H3: bootstrap: StreamStart server control failed: 0x%x", st);
        H3_LOCK(h3);
        if (h3->server_control_stream == srv_ctrl) {
            h3->server_control_stream = NULL;
            h3->server_control_ctx = NULL;
        }
        H3_UNLOCK(h3);
        /* Detach h3 before async abort — same UAF guard as h3_session_free. */
        send_ctx->h3 = NULL;
        free(send_ctx->pending_send);
        send_ctx->pending_send = NULL;
        h3_send_only_abort(srv_ctrl);
        return -1;
    }

    h3->server_settings_sent = true;
    h3->server_state = H3_SRV_WAIT_CONNECT;
    WT_LOG_INFO("H3: bootstrap SETTINGS queued stream=%p state=WAIT_CONNECT "
                "reason=%s (control SETTINGS only; no server QPACK uni)",
                (void*)srv_ctrl, reason);
    return 0;
}

void h3_server_request_settings_bootstrap(h3_session_t* h3)
{
    if (!h3 || !h3->is_server) return;
    if (h3->server_settings_sent) return;
    h3->settings_bootstrap_pending = 1;
    WT_LOG_INFO("H3: SETTINGS bootstrap scheduled for poll thread "
                "(avoid StreamOpen from stream/conn callbacks)");
}

void h3_server_poll_deferred(h3_session_t* h3)
{
    if (!h3 || !h3->is_server) return;
    if (!h3->settings_bootstrap_pending) return;
    h3->settings_bootstrap_pending = 0;
    if (h3->server_settings_sent) return;
    WT_LOG_INFO("H3: running deferred SETTINGS bootstrap on poll thread");
    if (h3_server_bootstrap_settings(h3, "poll-deferred") != 0) {
        WT_LOG_ERROR("H3: deferred SETTINGS bootstrap failed");
        /* Allow a later retry if peer traffic arrives again. */
        if (!h3->server_settings_sent)
            h3->settings_bootstrap_pending = 1;
        return;
    }

    /* ── Reprocess streams deferred while waiting for SETTINGS ──
     * h3_server_process_data's "first bidi before SETTINGS" branch
     * (see its "Wait for poll to send SETTINGS before classifying
     * CONNECT" comment) parks the stream with stream_type == -1 and
     * returns without re-arming anything — MsQuic only re-invokes the
     * RECEIVE callback when NEW data arrives, so a stream whose sole
     * chunk landed before SETTINGS was sent would otherwise never get
     * reclassified and would sit until the H3 handshake timeout fires.
     * Now that SETTINGS has just been sent, give any such streams a
     * chance to be classified (CONNECT vs native fallback).
     *
     * Collect under lock, process outside — h3_server_process_data can
     * reach h3_fallback_to_native_protocol -> on_ready, which calls
     * wt_stream_manager_accept_stream (MsQuic->SetCallbackHandler);
     * matches the "collect under lock, process outside" pattern used
     * for the same reason in server.cpp's deferred-stream replay. */
    h3_stream_ctx_t* pending_list = NULL;
    H3_LOCK(h3);
    {
        h3_stream_ctx_t* ds = h3->stream_ctx_list;
        h3_stream_ctx_t* pending_tail = NULL;
        while (ds) {
            h3_stream_ctx_t* next = ds->next;
            if (ds->stream_type == -1 && ds->quic_stream &&
                ds->recv_offset > 0) {
                /* Unlink from the session's tracking list. */
                if (h3->stream_ctx_list == ds) {
                    h3->stream_ctx_list = ds->next;
                } else {
                    h3_stream_ctx_t* prev = h3->stream_ctx_list;
                    while (prev && prev->next != ds) prev = prev->next;
                    if (prev) prev->next = ds->next;
                }
                /* Re-link onto our own local pending list. */
                ds->next = NULL;
                if (pending_tail) pending_tail->next = ds;
                else pending_list = ds;
                pending_tail = ds;
            }
            ds = next;
        }
    }
    H3_UNLOCK(h3);

    h3_stream_ctx_t* p = pending_list;
    while (p) {
        h3_stream_ctx_t* next = p->next;
        p->next = NULL;
        WT_LOG_INFO("H3: reprocessing stream deferred pre-SETTINGS "
                    "stream=%p len=%u", (void*)p->quic_stream, p->recv_offset);
        /* Re-link into the session list — h3_server_process_data's
         * success paths (native fallback / CONNECT accept) expect the
         * sctx to still be reachable via h3->stream_ctx_list for the
         * same unlink-and-free bookkeeping used on the non-deferred path. */
        H3_LOCK(h3);
        p->next = h3->stream_ctx_list;
        h3->stream_ctx_list = p;
        H3_UNLOCK(h3);
        h3_server_process_data(h3, p);
        p = next;
    }
}

int h3_server_send_initial_settings(h3_session_t* h3)
{
    /* Public API kept for server.cpp CONNECTED path: schedule only.
     * Actual send runs on poll (application thread). */
    if (!h3 || !h3->is_server) return -1;
    h3_server_request_settings_bootstrap(h3);
    return 0;
}

/* ═══════════════════════════════════════════════════════════════
 * Session Lifecycle
 * ═══════════════════════════════════════════════════════════════ */

h3_session_t* h3_session_create(
    HQUIC quic_conn, bool is_server,
    h3_on_session_ready_fn on_ready,
    h3_on_error_fn on_error, void* ctx)
{
    h3_session_t* h3 = (h3_session_t*)calloc(1, sizeof(*h3));
    if (!h3) return NULL;

    h3->quic_conn = quic_conn;
    h3->is_server = is_server;
    h3->on_ready = on_ready;
    h3->on_error = on_error;
    h3->callback_ctx = ctx;
    h3->handshake_complete = false;
    h3->server_settings_sent = false;
    h3->settings_bootstrap_pending = 0;
    h3->peer_h3_seen = false;
    h3->is_webtransport = false;
    h3->connect_stream_id = 0;
    h3->allow_native_clients = true;  /* default: backward compatible */
    h3->expected_authority[0] = '\0'; /* default: skip validation */
    atomic_store(&h3->ref_count, 1);  /* owner reference */

    if (is_server) {
        h3->server_state = H3_SRV_WAIT_CONTROL_STREAM;
    } else {
        h3->client_state = H3_CLI_SENDING_SETTINGS;
    }

    /* Initialize stream context list mutex */
#if defined(WT_PLATFORM_WINDOWS)
    InitializeCriticalSection(&h3->stream_ctx_lock);
#else
    pthread_mutex_init(&h3->stream_ctx_lock, NULL);
#endif

    return h3;
}

void h3_session_free(h3_session_t* h3)
{
    if (!h3) return;

    /* M-1: Block new acquires while we tear down.  Already-acquired
     * references (from in-flight RECEIVE / PEER_SEND_SHUTDOWN callbacks)
     * keep the struct alive until they release.  The actual free() +
     * mutex_destroy is deferred to h3_session_release() when ref_count
     * reaches 0. */
    atomic_store(&h3->released, true);

    /* Shut down tracked control streams to trigger SHUTDOWN_COMPLETE,
     * which StreamClose's the handle exactly once in h3_send_only_stream_cb.
     * Do NOT StreamClose here — that double-closed and SEGVd in QuicStreamFree.
     *
     * CRITICAL: Null c->h3 in the send_ctx BEFORE calling h3_send_only_abort.
     * h3_send_only_abort → StreamShutdown(ABORT|IMMEDIATE) is ASYNCHRONOUS —
     * the SHUTDOWN_COMPLETE callback may fire on a QUIC worker thread AFTER
     * this function returns and frees h3.  If c->h3 still points to h3 at
     * that point, h3_send_only_clear_session_ref will lock a mutex in freed
     * memory → SEGV in pthread_mutex_lock / QuicStreamFree. */
    HQUIC srv_ctrl = NULL;
    HQUIC cli_ctrl = NULL;
    HQUIC qpack_enc = NULL;
    HQUIC qpack_dec = NULL;
    void* srv_ctx = NULL;
    void* cli_ctx = NULL;
    H3_LOCK(h3);
    srv_ctrl = h3->server_control_stream;
    h3->server_control_stream = NULL;
    srv_ctx = h3->server_control_ctx;
    h3->server_control_ctx = NULL;
    cli_ctrl = h3->client_control_stream;
    h3->client_control_stream = NULL;
    cli_ctx = h3->client_control_ctx;
    h3->client_control_ctx = NULL;
    qpack_enc = h3->server_qpack_encoder_stream;
    h3->server_qpack_encoder_stream = NULL;
    qpack_dec = h3->server_qpack_decoder_stream;
    h3->server_qpack_decoder_stream = NULL;
    H3_UNLOCK(h3);

    /* Detach h3 back-pointer BEFORE queuing async abort — the callback
     * will see c->h3 == NULL and skip h3_send_only_clear_session_ref. */
    if (srv_ctx) ((h3_send_only_ctx_t*)srv_ctx)->h3 = NULL;
    if (cli_ctx) ((h3_send_only_ctx_t*)cli_ctx)->h3 = NULL;

    /* Abort then close synchronously. The caller (the connection's
     * SHUTDOWN_COMPLETE handler) calls ConnectionClose right after this
     * returns, so a stream left waiting on its own asynchronous
     * SHUTDOWN_COMPLETE never gets closed at all — see h3_send_only_close_now. */
    if (srv_ctrl) {
        WT_LOG_INFO("H3: session_free closing server control stream %p", (void*)srv_ctrl);
        h3_send_only_abort(srv_ctrl);
        h3_send_only_close_now(srv_ctrl, (h3_send_only_ctx_t*)srv_ctx);
    }
    if (cli_ctrl) {
        h3_send_only_abort(cli_ctrl);
        h3_send_only_close_now(cli_ctrl, (h3_send_only_ctx_t*)cli_ctx);
    }
    if (qpack_enc) {
        WT_LOG_INFO("H3: session_free closing server QPACK encoder %p", (void*)qpack_enc);
        h3_send_only_abort(qpack_enc);
        h3_send_only_close_now(qpack_enc, NULL);
    }
    if (qpack_dec) {
        WT_LOG_INFO("H3: session_free closing server QPACK decoder %p", (void*)qpack_dec);
        h3_send_only_abort(qpack_dec);
        h3_send_only_close_now(qpack_dec, NULL);
    }

    /* Free all tracked h3_stream_ctx_t objects still on the list.
     * Stream contexts that have already been freed through the normal
     * callback chain (SHUTDOWN_COMPLETE) were unlinked by
     * h3_stream_ctx_unlink and are not in this list.
     *
     * CRITICAL: Hold stream_ctx_lock during the list traversal.
     * Without the lock, a concurrent h3_stream_ctx_unlink() on a QUIC
     * worker thread (SHUTDOWN_COMPLETE / PEER_SEND_ABORTED for another
     * stream on the same connection) can modify the list while we
     * iterate, causing a use-after-free when sctx->next points to
     * memory freed by the concurrent unlink. */
    H3_LOCK(h3);
    h3_stream_ctx_t* sctx = h3->stream_ctx_list;
    h3->stream_ctx_list = NULL;
    while (sctx) {
        h3_stream_ctx_t* next = sctx->next;
        /* CAS guard: a concurrent SHUTDOWN_COMPLETE / PEER_SEND_ABORTED
         * on a QUIC worker thread may have already freed this context
         * despite msquic's stream-before-connection ordering — defensive
         * double-free prevention.  Only free if our CAS wins. */
        int expected = 0;
        bool we_own = atomic_compare_exchange_strong(
            &sctx->freed, &expected, 1);
        H3_UNLOCK(h3);
        if (we_own) {
            /* Winning the CAS means this stream's own SHUTDOWN_COMPLETE will
             * never run its StreamClose, so the handle must be released here
             * or it keeps a rundown reference on the registration and hangs
             * MsQuicRegistrationClose at shutdown.
             *
             * Close BEFORE freeing sctx: the stream callback dereferences sctx
             * (the CAS above) on entry, and StreamClose is what guarantees no
             * further callbacks for this stream. Freeing first would leave that
             * window open on a use-after-free. */
            HQUIC dead_stream = sctx->quic_stream;
            if (dead_stream) {
                MsQuic->StreamClose(dead_stream);
            }
            free(sctx->recv_buf);
            free(sctx);
        }
        H3_LOCK(h3);
        sctx = next;
    }
    H3_UNLOCK(h3);

    /* Drop the owner reference.  If no concurrent acquires are in flight,
     * ref_count reaches 0 and h3_session_release frees h3 + destroys the
     * mutex.  If acquires ARE in flight, the last release() by the last
     * callback will perform the actual free. */
    h3_session_release(h3);
}

#if 0
/* ── Dead Code: h3_session_accept_stream ───────────────────────────
 * PRESERVED but DISABLED: This function was the original HTTP/3 stream
 * acceptance API. The handshake now uses h3_server_handle_stream which
 * integrates with the state machine for automatic protocol detection
 * (HTTP/3 vs native). Keeping this for reference in case a separate
 * accept-stream API is needed for future HTTP/3 work. */
bool h3_session_accept_stream(h3_session_t* h3, HQUIC stream)
{
    if (!h3 || !h3->is_server) return false;

    /* Protocol detection: wait for first RECEIVE event to check
     * the first byte.  We always set up a stream callback so we
     * can inspect the data.  The callback will determine if this
     * is HTTP/3 or native and dispatch accordingly. */

    h3_stream_ctx_t* sctx = h3_stream_ctx_create(stream, NULL);
    if (!sctx) {
        if (h3->on_error)
            h3->on_error(h3->callback_ctx, -1, "Stream ctx alloc failed");
        return false;
    }
    (void)sctx;  /* stream ctx is owned by the callback chain */

    /* Always return false (let caller handle as raw stream).
     * The actual HTTP/3 detection and handshake is integrated into
     * the server_conn_cb in server.cpp using the state-machine approach
     * described in the header.  The full integration modifies
     * PEER_STREAM_STARTED to check h3_session state before routing
     * to wt_stream_manager. */
    return false;
}
#endif /* h3_session_accept_stream */

int32_t h3_client_connect(h3_session_t* h3,
                          const char* path, const char* authority)
{
    if (!h3 || h3->is_server) return -1;

    /* 1. Open client control stream (type 0x00)
     * 2. Send SETTINGS frame
     * 3. Open request stream
     * 4. Send CONNECT HEADERS
     */

    HQUIC ctrl_stream = NULL;
    h3_send_only_ctx_t* ctrl_ctx =
        h3_send_only_ctx_create(NULL, h3, 2 /* client_control */);
    if (!ctrl_ctx) return -1;

    QUIC_STATUS st = MsQuic->StreamOpen(h3->quic_conn,
        QUIC_STREAM_OPEN_FLAG_UNIDIRECTIONAL, h3_send_only_stream_cb, ctrl_ctx, &ctrl_stream);
    if (QUIC_FAILED(st)) {
        free(ctrl_ctx);
        return -1;
    }
    ctrl_ctx->stream = ctrl_stream;

    /* Write stream type byte + SETTINGS frame */
    uint8_t ctrl_data[256];
    ctrl_data[0] = H3_STREAM_CONTROL;
    int settings_len = h3_write_settings(ctrl_data + 1);
    int ctrl_total = 1 + settings_len;

    /* Start stream with the type byte, then send SETTINGS */
    st = MsQuic->StreamStart(ctrl_stream, QUIC_STREAM_START_FLAG_IMMEDIATE);
    if (QUIC_FAILED(st)) {
        h3_send_only_abort(ctrl_stream);
        return -1;
    }

    uint8_t* ctrl_copy = (uint8_t*)malloc((size_t)ctrl_total);
    if (!ctrl_copy) {
        h3_send_only_abort(ctrl_stream);
        return -1;
    }
    memcpy(ctrl_copy, ctrl_data, (size_t)ctrl_total);

    {
        /* Heap descriptor + payload: msquic keeps the QUIC_BUFFER pointer
         * past this call, so it must not be a local. See h3_send_buf_alloc. */
        h3_send_buf_t* sb = h3_send_buf_alloc(ctrl_copy, (uint32_t)(ctrl_total));
        free(ctrl_copy);
        H3_LOCK(h3);
        h3->client_control_stream = ctrl_stream;
        h3->client_control_ctx = ctrl_ctx;
        H3_UNLOCK(h3);
        st = sb ? MsQuic->StreamSend(ctrl_stream, &sb->buf, 1,
                                 QUIC_SEND_FLAG_NONE, sb)
                                  : QUIC_STATUS_OUT_OF_MEMORY;
        if (QUIC_FAILED(st)) {
            free(sb);
            H3_LOCK(h3);
            if (h3->client_control_stream == ctrl_stream) {
                h3->client_control_stream = NULL;
                h3->client_control_ctx = NULL;
            }
            H3_UNLOCK(h3);
            /* Detach h3 before async abort — same UAF guard as h3_session_free. */
            ctrl_ctx->h3 = NULL;
            h3_send_only_abort(ctrl_stream);
            return -1;
        }
    }

    /* 2. Save path and authority for the on_ready callback */
    if (path)
        strncpy(h3->request_path, path, sizeof(h3->request_path) - 1);
    if (authority)
        strncpy(h3->request_authority, authority, sizeof(h3->request_authority) - 1);

    /* 3. Open CONNECT request stream with a RECEIVE-capable callback.
     *    h3_send_only_stream_cb does NOT handle RECEIVE events — the
     *    server's 200 OK response would be silently dropped.  We use
     *    h3_client_stream_cb which buffers incoming data and calls
     *    h3_client_process_data to parse the :status response. */
    {
        h3_stream_ctx_t* sctx = (h3_stream_ctx_t*)calloc(1, sizeof(*sctx));
        if (!sctx) return -1;
        sctx->quic_stream = NULL;
        sctx->stream_type = 0;
        sctx->is_request = true;
        sctx->h3 = h3;

        HQUIC req_stream = NULL;
        st = MsQuic->StreamOpen(h3->quic_conn,
            QUIC_STREAM_OPEN_FLAG_NONE, h3_client_stream_cb, sctx, &req_stream);
        if (QUIC_FAILED(st)) {
            free(sctx);
            return -1;
        }
        sctx->quic_stream = req_stream;

        st = MsQuic->StreamStart(req_stream, QUIC_STREAM_START_FLAG_IMMEDIATE);
        if (QUIC_FAILED(st)) {
            /* StreamOpen registered h3_client_stream_cb with sctx, so
             * StreamClose fires SHUTDOWN_COMPLETE synchronously. Null
             * h3 to prevent h3_stream_ctx_unlink (sctx was never linked
             * into h3->stream_ctx_list) and recv_buf so free(recv_buf)
             * inside the callback is a no-op. The callback frees sctx. */
            sctx->h3 = NULL;
            sctx->recv_buf = NULL;
            MsQuic->StreamClose(req_stream);
            /* sctx is now freed by the callback — do NOT touch it again */
            return -1;
        }

        /* Link into session's stream context list for cleanup AFTER
         * StreamStart succeeds.  This ensures the context is freed by
         * h3_session_free if the connection shuts down before the
         * handshake completes.  Must happen after StreamStart because
         * the error path above frees sctx directly. */
        sctx->next = h3->stream_ctx_list;
        h3->stream_ctx_list = sctx;

        /* Derive an Origin from the authority. WebTransport is https-only,
         * so a native client's natural origin is https://<authority> — the
         * same value a browser page served from that host would send. A
         * server enforcing an origin allow-list rejects a CONNECT without
         * one, so omitting it makes the native client unable to reach any
         * such deployment. */
        char origin_buf[280];
        origin_buf[0] = '\0';
        if (authority && authority[0]) {
            snprintf(origin_buf, sizeof(origin_buf), "https://%s", authority);
        }

        uint8_t req_data[2048];
        int req_len = h3_write_headers(req_data, sizeof(req_data),
                                        0, "CONNECT", path, authority,
                                        origin_buf[0] ? origin_buf : NULL);
        if (req_len <= 0) {
            MsQuic->StreamShutdown(req_stream,
                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
            return -1;
        }

        uint8_t* req_copy = (uint8_t*)malloc((size_t)req_len);
        if (!req_copy) {
            MsQuic->StreamShutdown(req_stream,
                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
            return -1;
        }
        memcpy(req_copy, req_data, (size_t)req_len);

        {
            /* Heap descriptor + payload: msquic keeps the QUIC_BUFFER pointer
             * past this call, so it must not be a local. See h3_send_buf_alloc. */
            h3_send_buf_t* sb = h3_send_buf_alloc(req_copy, (uint32_t)(req_len));
            free(req_copy);
            /* QUIC_SEND_FLAG_NONE, never FIN: in WebTransport over HTTP/3
             * the CONNECT stream *is* the session. Closing it — including
             * half-closing it with a FIN on our send side — terminates the
             * session, so a conformant server would tear us down the moment
             * the request arrived. The stream stays open for the session's
             * lifetime and is closed only to end the session. */
            st = sb ? MsQuic->StreamSend(req_stream, &sb->buf, 1,
                                     QUIC_SEND_FLAG_NONE, sb)
                                      : QUIC_STATUS_OUT_OF_MEMORY;
            if (QUIC_FAILED(st)) {
                free(sb);
                MsQuic->StreamShutdown(req_stream,
                                        QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                return -1;
            }
        }
    }

    h3->client_state = H3_CLI_WAIT_RESPONSE;
    return 0;
}

/* ═══════════════════════════════════════════════════════════════
 * Server Handshake State Machine (called from server_conn_cb)
 * ═══════════════════════════════════════════════════════════════
 *
 * These functions are called from the QUIC connection callback
 * in server.cpp to advance the HTTP/3 handshake state machine.
 */

/**
 * Called when the server receives a new peer-initiated bidi stream
 * and we're in an HTTP/3 handshake phase.  Processes stream type
 * detection and routes the stream appropriately.
 *
 * Returns: 0 if h3 consumed the stream (HTTP/3 protocol stream)
 *          1 if this is a regular data stream (caller should pass
 *            to wt_stream_manager)
 *         -1 on handshake failure
 */
int h3_server_handle_stream(h3_session_t* h3, HQUIC stream,
                            bool is_unidirectional,
                            h3_stream_ctx_t** out_sctx)
{
    *out_sctx = NULL;

    if (h3->server_state == H3_SRV_ESTABLISHED) {
        /* WebTransport session already established —
         * this is a regular data stream (bidi only from our app layer).
         * Uni streams after establish (rare) are still owned by H3/QPACK. */
        if (is_unidirectional) {
            h3_stream_ctx_t* sctx = h3_stream_ctx_create(stream, h3);
            if (!sctx) return -1;
            sctx->is_unidirectional = true;
            sctx->stream_type = -1;
            *out_sctx = sctx;
            return 0;
        }
        return 1;
    }

    /* Create stream context for buffered read */
    h3_stream_ctx_t* sctx = h3_stream_ctx_create(stream, h3);
    if (!sctx) return -1;
    sctx->is_unidirectional = is_unidirectional;
    *out_sctx = sctx;

    if (is_unidirectional) {
        /* Uni streams are HTTP/3 infrastructure (control 0x00, QPACK 0x02/0x03).
         * Never promote them to CONNECT or native game protocol — Chrome often
         * opens QPACK encoder/decoder *before* or *beside* the control stream.
         * Mis-detecting 0x02 as "native" causes silent handshake hang
         * (QUIC_NETWORK_IDLE_TIMEOUT) or SEGV on garbage "game" packets. */
        sctx->stream_type = -1;  /* resolve type byte on first RECEIVE */
        sctx->is_request = false;
        return 0;
    }

    /* Bidirectional peer stream during handshake. */
    if (h3->server_state == H3_SRV_WAIT_CONTROL_STREAM ||
        h3->server_state == H3_SRV_GOT_SETTINGS) {
        /* First bidi may be native game data (non-H3) OR we still wait for
         * control-stream SETTINGS before accepting CONNECT. Detection is
         * deferred until RECEIVE (first byte). */
        sctx->stream_type = -1;
        return 0;
    }

    if (h3->server_state == H3_SRV_WAIT_CONNECT) {
        /* Expected CONNECT request stream after H3 SETTINGS exchange. */
        sctx->is_request = true;
        return 0;
    }

    return 1;
}

/* See http3.h for the contract and the two call sites. */
int h3_fallback_to_native_protocol(h3_session_t* h3, h3_stream_ctx_t* sctx)
{
    if (!h3 || !sctx) return -1;

    /* Raw/native protocol.
     * When allowed_origins is configured (non-empty, not "*"),
     * native clients cannot perform CORS validation and are
     * rejected by default.  Operators must explicitly enable
     * allow_native_clients if they need both browser CORS and
     * native client access on the same server. */
    {
        const char* allowed = h3->allowed_origins;
        const bool origins_configured =
            (allowed != NULL && allowed[0] != '\0' &&
             strcmp(allowed, "*") != 0);
        if (origins_configured && !h3->allow_native_clients) {
            WT_LOG_WARN("Rejected native client: allowed_origins is "
                        "configured and allow_native_clients is disabled. "
                        "Set allow_native_clients=true to permit native "
                        "clients alongside browser CORS enforcement.");
            if (h3->on_error) {
                h3->on_error(h3->callback_ctx, -1,
                             "Native clients not allowed");
            }
            return -1;
        }
    }

    /* ── CRITICAL: Save stream context for data replay ──
     * The buffered data in sctx->recv_buf was received before
     * the wt_session was created. Store the sctx so
     * on_h3_session_ready (in server.cpp) can accept this
     * stream into the stream_manager and replay the data.
     * Without this, the first stream's data is silently lost. */
    h3->native_stream_ctx = sctx;

    /* Mark sctx as raw BEFORE calling on_ready.  on_h3_session_ready
     * (server.cpp) calls h3_stream_ctx_unlink + free(nsctx) for the
     * native_stream_ctx — after on_ready returns, sctx may be freed.
     * All sctx writes MUST happen before the on_ready call. */
    sctx->stream_type = -2; /* mark as raw */

    /* Set handshake_complete and ESTABLISHED AFTER on_ready so
     * that the wt_session is fully created (atomic_ptr_store'd
     * into sconn->session) before any concurrent PEER_STREAM_STARTED
     * sees handshake_complete==true and routes directly to
     * the stream_manager.  Otherwise a QUIC worker thread could
     * observe handshake_complete==true but sconn->session==NULL
     * and abort the stream. */
    if (h3->on_ready) {
        h3->on_ready(h3->callback_ctx, h3->quic_conn, "/", "");
        /* IMPORTANT: on_ready may have freed sctx via
         * on_h3_session_ready -> h3_stream_ctx_unlink -> free.
         * Do NOT access sctx after this point. */
    }
    h3->handshake_complete = true;
    h3->server_state = H3_SRV_ESTABLISHED;
    /* native_stream_ctx consumed (or NULLed) by on_h3_session_ready.
     * If it was consumed, the stream and data are now managed by
     * the stream_manager. If on_ready failed, the data is lost
     * (acceptable — connection is shutting down anyway). */
    return 1;
}

/**
 * Process data received on an HTTP/3 stream during the handshake.
 * Called from the stream RECEIVE callback.
 *
 * This function checks for protocol detection (first byte == 0x00 for
 * HTTP/3, otherwise raw) and handles SETTINGS/HEADERS parsing.
 *
 * Returns: 0 if h3 consumed the data (still handshaking)
 *          1 if the handshake is complete (caller should create wt_session)
 *         -1 on handshake failure
 */
int h3_server_process_data(h3_session_t* h3, h3_stream_ctx_t* sctx)
{
    if (h3->server_state == H3_SRV_ESTABLISHED && !sctx->is_unidirectional)
        return 1;

    if (sctx->recv_offset == 0) return 0;  /* no data yet */

    /* Protocol / stream-type detection: inspect first byte */
    if (sctx->stream_type == -1) {
        if (sctx->recv_offset < 1) return 0;
        uint8_t first_byte = sctx->recv_buf[0];

        /* ── Unidirectional HTTP/3 streams (control / QPACK) ──
         * MUST never fall through to native detection. Chrome WebTransport
         * opens QPACK encoder (0x02) and decoder (0x03) uni streams that
         * often arrive before or without waiting for control (0x00). Treating
         * them as native caused QUIC_NETWORK_IDLE_TIMEOUT on the browser
         * (no SETTINGS/CONNECT completed) and risk of SEGV on garbage data. */
        if (sctx->is_unidirectional) {
            if (first_byte == H3_STREAM_CONTROL) {
                sctx->stream_type = H3_STREAM_CONTROL;
                h3->peer_h3_seen = true;

                /* Schedule SETTINGS on poll — NEVER open streams /
                 * StreamSend from inside stream RECEIVE (msquic
                 * re-entrancy → QuicOperationFree crash). */
                if (!h3->server_settings_sent) {
                    WT_LOG_INFO("H3: peer control type=0x00; schedule SETTINGS bootstrap");
                    h3_server_request_settings_bootstrap(h3);
                } else {
                    WT_LOG_INFO("H3: peer control stream type=0x00 seen "
                                "(server SETTINGS already sent; waiting for CONNECT)");
                }

                if (sctx->recv_offset > 1) {
                    size_t offset = 1; /* skip stream type byte */
                    h3_control_state_t cs = { false, true };
                    h3_parse_frames(sctx->recv_buf + 1,
                                    sctx->recv_offset - 1,
                                    &offset, h3_log_peer_settings_frame, &cs);
                    if (!cs.settings_ok) return -1;  /* H3_MISSING_SETTINGS */
                }
                return 0;
            }

            if (first_byte == H3_STREAM_QPACK_ENCODER ||
                first_byte == H3_STREAM_QPACK_DECODER) {
                sctx->stream_type = (int)first_byte;
                h3->peer_h3_seen = true;
                WT_LOG_INFO("H3: accepting peer QPACK uni stream type=0x%02x "
                            "(static-only; drain encoder inserts, no dynamic table)",
                            (unsigned)first_byte);
                /* Schedule SETTINGS if not yet sent — same defer-to-poll
                 * pattern as control stream above. */
                if (!h3->server_settings_sent) {
                    WT_LOG_INFO("H3: peer QPACK before SETTINGS; schedule bootstrap");
                    h3_server_request_settings_bootstrap(h3);
                }
                return 0;
            }

            if (first_byte == H3_STREAM_WEBTRANSPORT_UNI) {
                /* A WebTransport unidirectional stream (draft-ietf-webtrans-
                 * http3 §4.1). The transport maps FishNet's reliable channel
                 * onto bidirectional streams only, so nothing here consumes a
                 * unidirectional one. Reject it explicitly: leaving it open
                 * holds a stream context and a QUIC stream slot for the life
                 * of the connection while never delivering data. */
                sctx->stream_type = (int)first_byte;
                WT_LOG_WARN("H3: peer opened a WebTransport unidirectional "
                            "stream; this transport uses bidirectional "
                            "streams only — aborting it");
                MsQuic->StreamShutdown(sctx->quic_stream,
                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                return 0;
            }

            /* Unknown uni stream type. RFC 9114 §6.2.3 allows a recipient to
             * abort the reading side of a stream whose type it does not
             * understand, and requires that it not treat the stream as an
             * error. Aborting (rather than the previous silent ignore)
             * releases the stream context instead of holding it until the
             * connection ends, which an unauthenticated peer could otherwise
             * repeat to exhaust memory. */
            sctx->stream_type = (int)first_byte;
            WT_LOG_WARN("H3: aborting unknown uni stream type=0x%02x",
                        (unsigned)first_byte);
            MsQuic->StreamShutdown(sctx->quic_stream,
                QUIC_STREAM_SHUTDOWN_FLAG_ABORT_RECEIVE, 0);
            return 0;
        }

        /* ── Bidirectional stream ── */
        if (first_byte == H3_STREAM_CONTROL) {
            /* Non-standard: control type on bidi — treat as H3 control. */
            WT_LOG_WARN("H3: control type byte on bidi stream — treating as control");
            sctx->is_unidirectional = true;
            /* Keep stream_type == -1 and re-enter uni control path. */
            return h3_server_process_data(h3, sctx);
        }

        if (!h3->server_settings_sent) {
            WT_LOG_INFO("H3: first bidi before SETTINGS; schedule bootstrap");
            h3_server_request_settings_bootstrap(h3);
            /* Wait for poll to send SETTINGS before classifying CONNECT. */
            return 0;
        }

        /* Browser CONNECT HEADERS frame type is 0x01. Native FishMMO clients
         * send raw game bytes. After proactive SETTINGS we are already in
         * WAIT_CONNECT — preserve native detection when no peer H3 uni was
         * seen and the first byte is not HEADERS. */
        if (!h3->peer_h3_seen && first_byte != H3_FRAME_HEADERS) {
            return h3_fallback_to_native_protocol(h3, sctx);
        }

        /* Browser CONNECT request after SETTINGS exchange.
         * Peer H3 uni was seen (or first byte is HEADERS) —
         * classify as request stream for CONNECT parsing. */
        sctx->stream_type = H3_STREAM_REQUEST;
        sctx->is_request = true;
        WT_LOG_INFO("H3: bidi stream classified as CONNECT candidate "
                    "first=0x%02x peer_h3=%d state=%d",
                    (unsigned)first_byte,
                    h3->peer_h3_seen ? 1 : 0,
                    (int)h3->server_state);
        /* Continue into CONNECT parse below (stream_type no longer -1). */
    }

    if (sctx->stream_type == H3_STREAM_CONTROL) {
        /* Parse SETTINGS from control stream data (after type byte) */
        if (sctx->recv_offset > 1) {
            size_t offset = 1;
            h3_control_state_t cs = { false, true };
            h3_parse_frames(sctx->recv_buf + 1,
                            sctx->recv_offset - 1,
                            &offset, h3_log_peer_settings_frame, &cs);
            if (!cs.settings_ok) return -1;  /* H3_MISSING_SETTINGS */
        }
        return 0;
    }

    if (sctx->is_request &&
        h3->server_state == H3_SRV_WAIT_CONNECT) {
        /* Parse HEADERS frame and check for CONNECT request */
        if (sctx->recv_offset > 0) {
            size_t offset = 0;

            h3_parsed_headers_t phdr;
            memset(&phdr, 0, sizeof(phdr));

            if (h3_parse_frames(sctx->recv_buf, sctx->recv_offset,
                                &offset, NULL, NULL) == 0) {
                /* Parse: Varint(type=HEADERS) Varint(len) EncodedFieldSection */
                size_t pos = 0;
                uint64_t ftype, flen;
                uint8_t tb, lb;

                if (varint_decode(sctx->recv_buf + pos,
                                  sctx->recv_offset - pos,
                                  &ftype, &tb) == 0) {
                    pos += tb;
                    if (varint_decode(sctx->recv_buf + pos,
                                      sctx->recv_offset - pos,
                                      &flen, &lb) == 0) {
                        pos += lb;
                        /* Overflow-safe bounds check (see h3_parse_frames for
                         * rationale).  Prevents integer wraparound from a
                         * maliciously-crafted frame length. */
                        if (flen > sctx->recv_offset || pos > sctx->recv_offset - (size_t)flen) {
                            /* Partial read — wait for more data */
                            pos -= (tb + lb);
                            return 0;
                        }
                        if (pos + flen <= sctx->recv_offset) {
                            if (ftype == H3_FRAME_HEADERS) {
                                WT_LOG_INFO("H3: HEADERS frame len=%llu on CONNECT candidate "
                                            "stream=%p", flen, (void*)sctx->quic_stream);
                                h3_hex_dump("HEADERS frame payload (raw EncodedFieldSection)",
                                            sctx->recv_buf + pos, (size_t)flen);

                                if (h3_parse_headers(sctx->recv_buf + pos,
                                                     flen, &phdr)) {
                                    /* Validate CONNECT */
                                    if (strcmp(phdr.method, "CONNECT") == 0 &&
                                        strcmp(phdr.protocol, "webtransport") == 0) {

                                        /* Validate Origin header for cross-origin
                                         * WebTransport. Uses exact-match with length
                                         * check to prevent prefix-injection attacks.
                                         *
                                         * When allowed_origins is configured (non-NULL,
                                         * non-empty), the Origin header is REQUIRED.
                                         * Non-browser clients that omit the Origin header
                                         * are rejected. When allowed_origins is NULL or
                                         * empty (no origin restriction configured), empty
                                         * origins are accepted (backward-compatible with
                                         * native clients that don't send Origin). */
                                        const char* allowed = h3->allowed_origins;
                                        const bool origins_configured =
                                            (allowed != NULL && allowed[0] != '\0');

                                        if (phdr.origin[0] == '\0') {
                                            /* Origin header is missing.
                                             * Per RFC 9220 / W3C WebTransport, browsers
                                             * MUST send the Origin header on CONNECT.
                                             * Reject unless allowed_origins is explicitly
                                             * set to "*" (dev/testing escape hatch). */
                                            if (origins_configured &&
                                                strcmp(allowed, "*") == 0) {
                                                /* "*" = explicit allow-all for dev */
                                            } else {
                                                WT_LOG_WARN("Rejected WebTransport CONNECT: "
                                                            "Origin header required but missing "
                                                            "(configure allowed_origins=\"*\" for dev)");
                                                uint8_t rej[128];
                                                int rej_len = h3_write_headers(
                                                    rej, sizeof(rej), 403, NULL, NULL, NULL, NULL);
                                                if (rej_len > 0) {
                                                    uint8_t* rej_copy = (uint8_t*)malloc((size_t)rej_len);
                                                    if (rej_copy) {
                                                        memcpy(rej_copy, rej, (size_t)rej_len);
                                                        /* Heap descriptor + payload: msquic keeps the QUIC_BUFFER pointer
                                                         * past this call, so it must not be a local. See h3_send_buf_alloc. */
                                                        h3_send_buf_t* sb = h3_send_buf_alloc(rej_copy, (uint32_t)(rej_len));
                                                        free(rej_copy);
                                                        QUIC_STATUS qst = sb ? MsQuic->StreamSend(
                                                            sctx->quic_stream, &sb->buf, 1,
                                                            QUIC_SEND_FLAG_NONE, sb)
                                                                                  : QUIC_STATUS_OUT_OF_MEMORY;
                                                        if (QUIC_FAILED(qst)) {
                                                            free(sb);
                                                        }
                                                    }
                                                }
                                                MsQuic->StreamShutdown(sctx->quic_stream,
                                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                                                return -1;
                                            }
                                        } else {
                                            /* Origin header is present — validate it. */
                                            bool origin_ok = false;
                                            size_t origin_len = strlen(phdr.origin);

                                            if (!origins_configured ||
                                                strcmp(allowed, "*") == 0) {
                                                /* No origins configured, or "*" wildcard
                                                 * — allow any origin (dev/testing). */
                                                origin_ok = true;
                                            } else {
                                                const char* start = allowed;
                                                while (*start) {
                                                    while (*start == ' ' || *start == ',') start++;
                                                    if (*start == '\0') break;
                                                    const char* end = start;
                                                    while (*end != '\0' && *end != ',' && *end != ' ') end++;
                                                    size_t entry_len = (size_t)(end - start);
                                                    if (entry_len == origin_len &&
                                                        memcmp(phdr.origin, start, origin_len) == 0) {
                                                        origin_ok = true;
                                                        break;
                                                    }
                                                    start = end;
                                                }
                                            }

                                            if (!origin_ok) {
                                                WT_LOG_WARN("Rejected WebTransport CONNECT from origin: %s", phdr.origin);
                                                uint8_t rej[128];
                                                int rej_len = h3_write_headers(
                                                    rej, sizeof(rej), 403, NULL, NULL, NULL, NULL);
                                                if (rej_len > 0) {
                                                    uint8_t* rej_copy = (uint8_t*)malloc((size_t)rej_len);
                                                    if (rej_copy) {
                                                        memcpy(rej_copy, rej, (size_t)rej_len);
                                                        /* Heap descriptor + payload: msquic keeps the QUIC_BUFFER pointer
                                                         * past this call, so it must not be a local. See h3_send_buf_alloc. */
                                                        h3_send_buf_t* sb = h3_send_buf_alloc(rej_copy, (uint32_t)(rej_len));
                                                        free(rej_copy);
                                                        QUIC_STATUS qst = sb ? MsQuic->StreamSend(
                                                            sctx->quic_stream, &sb->buf, 1,
                                                            QUIC_SEND_FLAG_NONE, sb)
                                                                                  : QUIC_STATUS_OUT_OF_MEMORY;
                                                        if (QUIC_FAILED(qst)) {
                                                            free(sb);
                                                        }
                                                    }
                                                }
                                                MsQuic->StreamShutdown(sctx->quic_stream,
                                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                                                return -1;  /* origin rejected */
                                            }
                                        }

                                        /* ── :authority validation ─────────────────
                                         * When expected_authority is configured,
                                         * reject CONNECT requests that specify a
                                         * different hostname.  This prevents host-
                                         * confusion attacks in multi-tenant deployments
                                         * where multiple hostnames share the same QUIC
                                         * listener port. */
                                        if (h3->expected_authority[0] != '\0') {
                                            if (phdr.authority[0] == '\0') {
                                                WT_LOG_WARN("Rejected WebTransport CONNECT: "
                                                            ":authority required but missing "
                                                            "(expected %s)",
                                                            h3->expected_authority);
                                                uint8_t rej[128];
                                                int rej_len = h3_write_headers(
                                                    rej, sizeof(rej), 403, NULL, NULL, NULL, NULL);
                                                if (rej_len > 0) {
                                                    uint8_t* rej_copy = (uint8_t*)malloc((size_t)rej_len);
                                                    if (rej_copy) {
                                                        memcpy(rej_copy, rej, (size_t)rej_len);
                                                        /* Heap descriptor + payload: msquic keeps the QUIC_BUFFER pointer
                                                         * past this call, so it must not be a local. See h3_send_buf_alloc. */
                                                        h3_send_buf_t* sb = h3_send_buf_alloc(rej_copy, (uint32_t)(rej_len));
                                                        free(rej_copy);
                                                        QUIC_STATUS qst = sb ? MsQuic->StreamSend(
                                                            sctx->quic_stream, &sb->buf, 1,
                                                            QUIC_SEND_FLAG_NONE, sb)
                                                                                  : QUIC_STATUS_OUT_OF_MEMORY;
                                                        if (QUIC_FAILED(qst)) {
                                                            free(sb);
                                                        }
                                                    }
                                                }
                                                MsQuic->StreamShutdown(sctx->quic_stream,
                                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                                                return -1;
                                            }
                                            /* Compare :authority case-insensitively
                                             * (hostnames are case-insensitive per RFC 3986).
                                             * Use length-bounded comparison to prevent
                                             * prefix-injection (e.g. "evil.com" matching
                                             * "evil.com.attacker.net"). */
                                            size_t auth_len = strlen(phdr.authority);
                                            size_t expected_len = strlen(h3->expected_authority);
                                            if (auth_len != expected_len ||
#if defined(WT_PLATFORM_WINDOWS)
                                                _strnicmp(phdr.authority, h3->expected_authority, auth_len) != 0
#else
                                                strncasecmp(phdr.authority, h3->expected_authority, auth_len) != 0
#endif
                                            ) {
                                                WT_LOG_WARN("Rejected WebTransport CONNECT: "
                                                            ":authority mismatch (got %s, expected %s)",
                                                            phdr.authority, h3->expected_authority);
                                                uint8_t rej[128];
                                                int rej_len = h3_write_headers(
                                                    rej, sizeof(rej), 403, NULL, NULL, NULL, NULL);
                                                if (rej_len > 0) {
                                                    uint8_t* rej_copy = (uint8_t*)malloc((size_t)rej_len);
                                                    if (rej_copy) {
                                                        memcpy(rej_copy, rej, (size_t)rej_len);
                                                        /* Heap descriptor + payload: msquic keeps the QUIC_BUFFER pointer
                                                         * past this call, so it must not be a local. See h3_send_buf_alloc. */
                                                        h3_send_buf_t* sb = h3_send_buf_alloc(rej_copy, (uint32_t)(rej_len));
                                                        free(rej_copy);
                                                        QUIC_STATUS qst = sb ? MsQuic->StreamSend(
                                                            sctx->quic_stream, &sb->buf, 1,
                                                            QUIC_SEND_FLAG_NONE, sb)
                                                                                  : QUIC_STATUS_OUT_OF_MEMORY;
                                                        if (QUIC_FAILED(qst)) {
                                                            free(sb);
                                                        }
                                                    }
                                                }
                                                MsQuic->StreamShutdown(sctx->quic_stream,
                                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                                                return -1;
                                            }
                                        }

                                        /* ── :path validation ──────────────────────────
                                         * Reject paths containing traversal sequences (..)
                                         * or paths that don't start with '/'.  The path is
                                         * attacker-controlled via the CONNECT :path header.
                                         * Without this check, a malicious client could inject
                                         * path-traversal sequences that, if the path is ever
                                         * used for filesystem operations or routing decisions,
                                         * would escape the intended directory. */
                                        if (phdr.path[0] != '\0') {
                                            if (phdr.path[0] != '/' ||
                                                strstr(phdr.path, "..") != NULL) {
                                                WT_LOG_WARN("Rejected WebTransport CONNECT: "
                                                            "suspicious :path value \"%s\"",
                                                            phdr.path);
                                                uint8_t rej[128];
                                                int rej_len = h3_write_headers(
                                                    rej, sizeof(rej), 400, NULL, NULL, NULL, NULL);
                                                if (rej_len > 0) {
                                                    uint8_t* rej_copy = (uint8_t*)malloc((size_t)rej_len);
                                                    if (rej_copy) {
                                                        memcpy(rej_copy, rej, (size_t)rej_len);
                                                        /* Heap descriptor + payload: msquic keeps the QUIC_BUFFER pointer
                                                         * past this call, so it must not be a local. See h3_send_buf_alloc. */
                                                        h3_send_buf_t* sb = h3_send_buf_alloc(rej_copy, (uint32_t)(rej_len));
                                                        free(rej_copy);
                                                        QUIC_STATUS qst = sb ? MsQuic->StreamSend(
                                                            sctx->quic_stream, &sb->buf, 1,
                                                            QUIC_SEND_FLAG_NONE, sb)
                                                                                  : QUIC_STATUS_OUT_OF_MEMORY;
                                                        if (QUIC_FAILED(qst)) {
                                                            free(sb);
                                                        }
                                                    }
                                                }
                                                MsQuic->StreamShutdown(sctx->quic_stream,
                                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                                                return -1;
                                            }
                                        }

                                        WT_LOG_INFO("H3: CONNECT accepted — method=%s protocol=%s "
                                                    "origin=%s path=%s authority=%s",
                                                    phdr.method, phdr.protocol,
                                                    phdr.origin[0] ? phdr.origin : "(none)",
                                                    phdr.path[0] ? phdr.path : "(none)",
                                                    phdr.authority[0] ? phdr.authority : "(none)");

                                        /* Record session metadata for WT stream framing.
                                         * Use MsQuic->GetParam to get the actual QUIC stream ID,
                                         * not a locally-assigned counter. */
                                        h3->is_webtransport = true;
                                        {
                                            uint64_t sid = 0;
                                            uint32_t sid_len = sizeof(sid);
                                            if (QUIC_SUCCEEDED(MsQuic->GetParam(
                                                    sctx->quic_stream, QUIC_PARAM_STREAM_ID,
                                                    &sid_len, &sid))) {
                                                h3->connect_stream_id = sid;
                                            } else {
                                                h3->connect_stream_id = 0;
                                                WT_LOG_WARN("H3: GetParam STREAM_ID failed for CONNECT stream");
                                            }
                                        }

                                        /* Copy path & authority for the on_ready callback */
                                        if (phdr.path[0])
                                            strncpy(h3->request_path, phdr.path, sizeof(h3->request_path)-1);
                                        if (phdr.authority[0])
                                            strncpy(h3->request_authority, phdr.authority, sizeof(h3->request_authority)-1);

                                        /* ── CRITICAL: create wt_session BEFORE sending 200 OK ──
                                         * Calling on_ready first ensures the wt_session and its
                                         * stream_manager are fully initialized before the browser
                                         * receives the 200 OK response and starts sending data on
                                         * new streams. This eliminates the race condition where
                                         * browser data streams could arrive before the server's
                                         * wt_session is ready to accept them.
                                         *
                                         * handshake_complete and server_state are set AFTER
                                         * on_ready so that concurrent PEER_STREAM_STARTED
                                         * events on QUIC worker threads cannot observe
                                         * handshake_complete==true before sconn->session
                                         * is stored (by on_ready → on_h3_session_ready). */

                                        if (h3->on_ready) {
                                            h3->on_ready(h3->callback_ctx,
                                                         h3->quic_conn,
                                                         h3->request_path,
                                                         h3->request_authority);
                                        }
                                        h3->handshake_complete = true;
                                        h3->server_state = H3_SRV_ESTABLISHED;

                                        /* Now send 200 OK — wt_session is ready to receive data */
                                        uint8_t resp[256];
                                        int resp_len = h3_write_headers(
                                            resp, sizeof(resp), 200, NULL, NULL, NULL, NULL);

                                        if (resp_len > 0) {
                                            uint8_t* resp_copy =
                                                (uint8_t*)malloc((size_t)resp_len);
                                            if (resp_copy) {
                                                memcpy(resp_copy, resp,
                                                       (size_t)resp_len);
                                                /* Heap descriptor + payload: msquic keeps the QUIC_BUFFER pointer
                                                 * past this call, so it must not be a local. See h3_send_buf_alloc. */
                                                h3_send_buf_t* sb = h3_send_buf_alloc(resp_copy, (uint32_t)(resp_len));
                                                free(resp_copy);
                                                QUIC_STATUS qst = sb ? MsQuic->StreamSend(
                                                    sctx->quic_stream,
                                                    &sb->buf, 1,
                                                    QUIC_SEND_FLAG_NONE,
                                                    sb)
                                                                          : QUIC_STATUS_OUT_OF_MEMORY;
                                                if (QUIC_FAILED(qst)) {
                                                    /* StreamSend failed — resp_copy
                                                     * will never be freed by
                                                     * SEND_COMPLETE. Free it now. */
                                                    free(sb);
                                                }
                                            }
                                        }

                                        return 1;  /* handshake complete! */
                                    }
                                }
                            }
                        }
                    }
                }

                /* If we reach here, one of:
                 *   - h3_parse_headers returned false (QPACK decode failed)
                 *   - :method != "CONNECT" or :protocol != "webtransport"
                 *   - h3_parse_frames or varint_decode failed
                 *
                 * When no genuine H3 protocol evidence has been observed for
                 * this connection (no control stream, no QPACK uni streams),
                 * this is most likely a native FishMMO client whose own raw
                 * packet protocol happened to start with byte value
                 * H3_FRAME_HEADERS (0x01) — e.g. FishNet's Broadcast PacketId
                 * — and got misrouted into CONNECT parsing above. Fall back to
                 * native handling and replay the buffered bytes instead of
                 * failing the handshake. If real H3 evidence WAS seen, this
                 * is a genuine invalid CONNECT from a browser/H3 peer and
                 * should still be rejected below. */
                if (!h3->peer_h3_seen) {
                    WT_LOG_INFO("H3: CONNECT parse failed with no prior H3 evidence — "
                                "falling back to native protocol detection");
                    return h3_fallback_to_native_protocol(h3, sctx);
                }

                /* Log what we decoded (if anything) so ops can tell QPACK decode
                 * failures apart from genuine invalid CONNECT fields. */
                if (phdr.method[0] == '\0' && phdr.protocol[0] == '\0') {
                    WT_LOG_ERROR("H3: CONNECT handshake failed — QPACK decode error "
                                 "(no pseudo-headers extracted from HEADERS frame)");
                } else {
                    WT_LOG_WARN("H3: Invalid CONNECT — method=%s protocol=%s "
                                "(need method=CONNECT protocol=webtransport)",
                                phdr.method[0] ? phdr.method : "(empty)",
                                phdr.protocol[0] ? phdr.protocol : "(empty)");
                }
                if (h3->on_error) {
                    h3->on_error(h3->callback_ctx, -1,
                                 "Invalid CONNECT request");
                }
            }
        }
        return 0;
    }

    return 0;
}

/* ═══════════════════════════════════════════════════════════════
 * Client Response Handler
 * ═══════════════════════════════════════════════════════════════ */

/**
 * Process data received on the client's CONNECT request stream.
 * Parses the server's HEADERS response to detect the :status
 * pseudo-header that confirms (or rejects) the WebTransport session.
 *
 * Called from h3_client_stream_cb RECEIVE and PEER_SEND_SHUTDOWN.
 *
 * On 200: transitions to H3_CLI_ESTABLISHED and fires on_ready.
 * On non-200 or error: transitions to H3_CLI_ERROR and fires on_error.
 *
 * @return 0 = still handshaking (waiting for more data)
 *         1 = handshake complete (200 OK, on_ready fired)
 *        -1 = handshake failed (on_error fired)
 */
int h3_client_process_data(h3_session_t* h3, h3_stream_ctx_t* sctx)
{
    /* Don't process if handshake already completed or failed */
    if (h3->client_state == H3_CLI_ESTABLISHED ||
        h3->client_state == H3_CLI_ERROR)
        return 0;

    if (sctx->recv_offset == 0) return 0;

    /* Parse the HEADERS frame from the buffered data */
    size_t pos = 0;
    uint64_t ftype, flen;
    uint8_t tb, lb;

    /* Decode frame type varint */
    if (varint_decode(sctx->recv_buf + pos,
                      sctx->recv_offset - pos,
                      &ftype, &tb) < 0)
        return 0; /* Not enough data for frame type */
    pos += tb;

    /* Decode frame length varint */
    if (varint_decode(sctx->recv_buf + pos,
                      sctx->recv_offset - pos,
                      &flen, &lb) < 0)
        return 0; /* Not enough data for frame length */
    pos += lb;

    /* Check if the full frame payload is available.
     * Overflow-safe bounds check: flen > recv_offset catches huge frames
     * before the subtraction; the second clause handles the normal path
     * without risk of 64-bit wraparound. */
    if (flen > sctx->recv_offset || pos > sctx->recv_offset - (size_t)flen)
        return 0; /* Frame payload not yet complete */

    /* Only HEADERS frames carry the :status we need.
     * Other frame types (e.g. DATA) are not expected on the
     * CONNECT response stream during the handshake. */
    if (ftype != H3_FRAME_HEADERS)
        return 0;

    /* Parse the QPACK-encoded header fields */
    h3_parsed_headers_t phdr;
    memset(&phdr, 0, sizeof(phdr));

    if (!h3_parse_headers(sctx->recv_buf + pos, flen, &phdr)) {
        WT_LOG_ERROR("Failed to parse server HEADERS response");
        h3->client_state = H3_CLI_ERROR;
        if (h3->on_error)
            h3->on_error(h3->callback_ctx, -1,
                         "Failed to parse server HEADERS response");
        return -1;
    }

    /* The :status pseudo-header is required in every response.
     * If missing, the response is malformed. */
    if (phdr.status[0] == '\0') {
        WT_LOG_ERROR("No :status in server response");
        h3->client_state = H3_CLI_ERROR;
        if (h3->on_error)
            h3->on_error(h3->callback_ctx, -1,
                         "No :status in server response");
        return -1;
    }

    int status = atoi(phdr.status);
    if (status == 200) {
        /* WebTransport session established! */
        WT_LOG_INFO("HTTP/3 WebTransport handshake complete (200 OK)");

        h3->handshake_complete = true;
        h3->client_state = H3_CLI_ESTABLISHED;

        if (h3->on_ready) {
            h3->on_ready(h3->callback_ctx, h3->quic_conn,
                         h3->request_path, h3->request_authority);
        }
        return 1;

    } else {
        /* Server rejected the CONNECT request */
        WT_LOG_ERROR("Server rejected WebTransport CONNECT: status %d", status);
        h3->client_state = H3_CLI_ERROR;
        if (h3->on_error)
            h3->on_error(h3->callback_ctx, status,
                         "Server rejected WebTransport CONNECT");
        return -1;
    }
}

#if 0
/* ── Dead Code: h3_send_wt_response ───────────────────────────────
 * PRESERVED but DISABLED: This function sends the 200 OK HEADERS
 * response on the CONNECT request stream. The 200 OK is now sent
 * inline in h3_server_process_data after creating the wt_session,
 * which eliminates the race between session creation and data streams.
 * May be useful if the response logic is ever separated from the
 * handshake processing. */
/**
 * For the CONNECT request stream, send the 200 OK response.
 * Called after h3_server_process_data detects a valid CONNECT.
 */
int32_t h3_send_wt_response(h3_session_t* h3, HQUIC req_stream)
{
    uint8_t buf[256];
    int len = h3_write_headers(buf, sizeof(buf), 200, NULL, NULL, NULL, NULL);
    if (len <= 0) return -1;

    return (h3_stream_send(req_stream, buf, (uint32_t)len) == QUIC_STATUS_SUCCESS)
           ? 0 : -1;
}
#endif /* h3_send_wt_response */