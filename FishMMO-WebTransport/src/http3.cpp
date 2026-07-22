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
 * QPACK Static Table (RFC 9204 Appendix A — all 99 entries)
 * ═══════════════════════════════════════════════════════════════
 * Complete table required for correct decoding of any QPACK-encoded
 * header field that references a static table index. Browsers may
 * use indexed field line encoding (0x00 prefix) for headers like
 * content-type (index 29), accept-encoding (index 28), etc.
 */

typedef struct {
    const char* name;
    uint8_t     name_len;
} qpack_entry_t;

static const qpack_entry_t kQpackStatic[] = {
    { ":authority",                  10 },  /*  0 */
    { ":path",                        5 },  /*  1 */
    { "age",                          3 },  /*  2 */
    { "content-disposition",         19 },  /*  3 */
    { "content-length",              14 },  /*  4 */
    { "cookie",                       6 },  /*  5 */
    { "date",                         4 },  /*  6 */
    { "etag",                         4 },  /*  7 */
    { "if-modified-since",           17 },  /*  8 */
    { "if-none-match",               13 },  /*  9 */
    { "last-modified",               13 },  /* 10 */
    { "link",                         4 },  /* 11 */
    { "location",                     8 },  /* 12 */
    { "referer",                      7 },  /* 13 */
    { "set-cookie",                  10 },  /* 14 */
    { ":method",                      7 },  /* 15 — CONNECT */
    { ":method",                      7 },  /* 16 — GET */
    { ":method",                      7 },  /* 17 — POST */
    { ":path",                        5 },  /* 18 — / */
    { ":path",                        5 },  /* 19 — /index.html */
    { ":scheme",                      7 },  /* 20 — http */
    { ":scheme",                      7 },  /* 21 — https */
    { ":status",                      7 },  /* 22 — 103 */
    { ":status",                      7 },  /* 23 — 200 */
    { ":status",                      7 },  /* 24 — 304 */
    { ":status",                      7 },  /* 25 — 404 */
    { ":status",                      7 },  /* 26 — 503 */
    { "accept",                       6 },  /* 27 */
    { "accept-encoding",             15 },  /* 28 */
    { "accept-ranges",               13 },  /* 29 */
    { "access-control-allow-headers", 28 }, /* 30 */
    { "access-control-allow-origin",  27 }, /* 31 */
    { "cache-control",               13 },  /* 32 */
    { "content-encoding",            16 },  /* 33 */
    { "content-type",                12 },  /* 34 */
    { "range",                        5 },  /* 35 */
    { "strict-transport-security",   25 },  /* 36 */
    { "vary",                         4 },  /* 37 */
    { "x-content-type-options",      22 },  /* 38 */
    { "x-xss-protection",            16 },  /* 39 */
    { "accept-language",             15 },  /* 40 */
    { "access-control-allow-credentials", 32 }, /* 41 */
    { "access-control-allow-methods", 27 },    /* 42 */
    { "access-control-expose-headers", 28 },   /* 43 */
    { "alt-svc",                      7 },  /* 44 */
    { "authorization",               13 },  /* 45 */
    { "content-security-policy",     23 },  /* 46 */
    { "early-data",                  10 },  /* 47 */
    { "expect-ct",                    9 },  /* 48 */
    { "forwarded",                    9 },  /* 49 */
    { "if-range",                     8 },  /* 50 */
    { "origin",                       6 },  /* 51 */
    { "purpose",                      7 },  /* 52 */
    { "server",                       6 },  /* 53 */
    { "timing-allow-origin",         20 },  /* 54 */
    { "upgrade-insecure-requests",   25 },  /* 55 */
    { "user-agent",                  10 },  /* 56 */
    { "x-forwarded-for",             15 },  /* 57 */
    { "x-frame-options",             14 },  /* 58 */
    { "x-forwarded-proto",           17 },  /* 59 */
    { ":status",                      7 },  /* 60 — 100 */
    { ":status",                      7 },  /* 61 — 204 */
    { ":status",                      7 },  /* 62 — 206 */
    { ":status",                      7 },  /* 63 — 302 */
    { ":status",                      7 },  /* 64 — 400 */
    { ":status",                      7 },  /* 65 — 401 */
    { ":status",                      7 },  /* 66 — 403 */
    { ":status",                      7 },  /* 67 — 421 */
    { ":status",                      7 },  /* 68 — 425 */
    { ":status",                      7 },  /* 69 — 500 */
    { "accept-charset",              14 },  /* 70 */
    { "accept-encoding",             15 },  /* 71 — "gzip, deflate, br" */
    { "accept-language",             15 },  /* 72 */
    { "accept-ranges",               13 },  /* 73 */
    { "access-control-allow-headers", 28 }, /* 74 */
    { "access-control-allow-methods", 27 },  /* 75 */
    { "access-control-allow-origin",  27 },  /* 76 */
    { "access-control-expose-headers", 28 }, /* 77 */
    { "access-control-max-age",      23 },  /* 78 */
    { "access-control-request-headers", 30 },/* 79 */
    { "access-control-request-method", 29 }, /* 80 */
    { "age",                          3 },  /* 81 */
    { "authorization",               13 },  /* 82 */
    { "content-security-policy",     23 },  /* 83 — "script-src 'none'..." */
    { "content-type",                12 },  /* 84 — "application/dns-message" */
    { "cookie",                       6 },  /* 85 */
    { "date",                         4 },  /* 86 */
    { "date",                         4 },  /* 87 */
    { "early-data",                  10 },  /* 88 */
    { "etag",                         4 },  /* 89 */
    { "if-modified-since",           17 },  /* 90 */
    { "if-none-match",               13 },  /* 91 */
    { "last-modified",               13 },  /* 92 */
    { "link",                         4 },  /* 93 */
    { "location",                     8 },  /* 94 */
    { "referer",                      7 },  /* 95 */
    { "set-cookie",                  10 },  /* 96 */
    { ":method",                      7 },  /* 97 — CONNECT */
    { ":method",                      7 },  /* 98 — CONNECT */
};

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

/* Code lengths in bits */
static const uint8_t kHuffLen[256] = {
   13, 23, 28, 28, 28, 28, 28, 28, 28, 24, 30, 28, 28, 30, 28, 28,  /*  0-15 */
   28, 28, 28, 28, 28, 28, 30, 28, 28, 28, 28, 28, 28, 28, 28, 28,  /* 16-31 */
    6, 10, 10, 12, 13,  6,  8, 11, 10, 10,  8, 11,  8,  6,  6,  6,  /* 32-47 */
    5,  5,  5,  6,  6,  6,  6,  6,  6,  6,  7,  8, 15,  6, 12, 10,  /* 48-63 */
   13,  6,  7,  7,  7,  7,  7,  7,  7,  7,  7,  7,  7,  7,  7,  7,  /* 64-79 */
    7,  7,  7,  7,  7,  7,  7,  7,  8,  7,  8, 13, 19, 13,  6, 13,  /* 80-95 */
   19,  6,  6,  6,  6,  6,  6,  6,  6,  7,  6,  6,  6,  6,  6,  6,  /* 96-111 */
    6,  6,  6,  6,  6,  6,  6,  6,  6,  6,  6,  6,  6,  6,  6,  7,  /*112-127 */
   19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19,  /*128-143 */
   19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19,  /*144-159 */
   19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19,  /*160-175 */
   19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19,  /*176-191 */
   20, 20, 20, 20, 20, 20, 20, 20, 21, 21, 21, 21, 21, 21, 21, 21,  /*192-207 */
   22, 22, 22, 22, 22, 22, 22, 22, 23, 23, 23, 23, 23, 23, 23, 23,  /*208-223 */
   24, 24, 24, 24, 24, 24, 24, 24, 25, 25, 25, 25, 25, 25, 25, 25,  /*224-239 */
   26, 26, 26, 26, 26, 26, 26, 26, 27, 27, 27, 27, 27, 27, 27, 27   /*240-255 */
};

/* Huffman codes (right-padded MSB-aligned, one per symbol 0-255) */
static const uint32_t kHuffCode[256] = {
    0x1ff8,     0x7fffd8,   0xfffffe2,  0xfffffe3,  /*  0- 3 */
    0xfffffe4,  0xfffffe5,  0xfffffe6,  0xfffffe7,  /*  4- 7 */
    0xfffffe8,  0xffffea,   0x3ffffffc, 0xfffffe9,  /*  8-11 */
    0xfffffea,  0x3ffffffd, 0xfffffeb,  0xfffffec,  /* 12-15 */
    0xfffffed,  0xfffffee,  0xfffffef,  0xffffff0,  /* 16-19 */
    0xffffff1,  0xffffff2,  0x3ffffffe, 0xffffff3,  /* 20-23 */
    0xffffff4,  0xffffff5,  0xffffff6,  0xffffff7,  /* 24-27 */
    0xffffff8,  0xffffff9,  0xffffffa,  0xffffffb,  /* 28-31 */
    0x14,       0x3f8,      0x3f9,      0xffa,      /* 32-35 */
    0x1ff9,     0x15,       0xf8,       0x7fa,      /* 36-39 */
    0x3fa,      0x3fb,      0xf9,       0x7fb,      /* 40-43 */
    0xfa,       0x16,       0x17,       0x18,       /* 44-47 */
    0x00,       0x01,       0x02,       0x19,       /* 48-51 */
    0x1a,       0x1b,       0x1c,       0x1d,       /* 52-55 */
    0x1e,       0x1f,       0x5c,       0xfb,       /* 56-59 */
    0x7ffc,     0x20,       0xffb,      0x3fc,      /* 60-63 */
    0x1ffa,     0x21,       0x5d,       0x5e,       /* 64-67 */
    0x5f,       0x60,       0x61,       0x62,       /* 68-71 */
    0x63,       0x64,       0x65,       0x66,       /* 72-75 */
    0x67,       0x68,       0x69,       0x6a,       /* 76-79 */
    0x6b,       0x6c,       0x6d,       0x6e,       /* 80-83 */
    0x6f,       0x70,       0x71,       0x72,       /* 84-87 */
    0xfc,       0x73,       0xfd,       0x1ffb,     /* 88-91 */
    0x7ffc0,    0x3ffd,     0x22,       0x1ffc,     /* 92-95 */
    0x7ffc1,    0x23,       0x24,       0x25,       /* 96-99 */
    0x26,       0x27,       0x28,       0x29,       /*100-103 */
    0x2a,       0x7b,       0x2b,       0x2c,       /*104-107 */
    0x2d,       0x2e,       0x2f,       0x30,       /*108-111 */
    0x31,       0x32,       0x33,       0x34,       /*112-115 */
    0x35,       0x36,       0x37,       0x38,       /*116-119 */
    0x39,       0x3a,       0x3b,       0x3c,       /*120-123 */
    0x3d,       0x3e,       0x3f,       0x7c,       /*124-127 */
    0x7ffc2,    0x7ffc3,    0x7ffc4,    0x7ffc5,    /*128-131 */
    0x7ffc6,    0x7ffc7,    0x7ffc8,    0x7ffc9,    /*132-135 */
    0x7ffca,    0x7ffcb,    0x7ffcc,    0x7ffcd,    /*136-139 */
    0x7ffce,    0x7ffcf,    0x7ffd0,    0x7ffd1,    /*140-143 */
    0x7ffd2,    0x7ffd3,    0x7ffd4,    0x7ffd5,    /*144-147 */
    0x7ffd6,    0x7ffd7,    0x7ffd8,    0x7ffd9,    /*148-151 */
    0x7ffda,    0x7ffdb,    0x7ffdc,    0x7ffdd,    /*152-155 */
    0x7ffde,    0x7ffdf,    0x7ffe0,    0x7ffe1,    /*156-159 */
    0x7ffe2,    0x7ffe3,    0x7ffe4,    0x7ffe5,    /*160-163 */
    0x7ffe6,    0x7ffe7,    0x7ffe8,    0x7ffe9,    /*164-167 */
    0x7ffea,    0x7ffeb,    0x7ffec,    0x7ffed,    /*168-171 */
    0x7ffee,    0x7ffef,    0x7fff0,    0x7fff1,    /*172-175 */
    0x7fff2,    0x7fff3,    0x7fff4,    0x7fff5,    /*176-179 */
    0x7fff6,    0x7fff7,    0x7fff8,    0x7fff9,    /*180-183 */
    0x7fffa,    0x7fffb,    0x7fffc,    0x7fffd,    /*184-187 */
    0x7fffe,    0x7ffff,    0x80000,    0x80001,    /*188-191 */
    0x3ffe0,    0x3ffe1,    0x3ffe2,    0x3ffe3,    /*192-195 */
    0x3ffe4,    0x3ffe5,    0x3ffe6,    0x3ffe7,    /*196-199 */
    0x1ffdc,    0x1ffdd,    0x1ffde,    0x1ffdf,    /*200-203 */
    0x1ffe0,    0x1ffe1,    0x1ffe2,    0x1ffe3,    /*204-207 */
    0xfff0,     0xfff1,     0xfff2,     0xfff3,     /*208-211 */
    0xfff4,     0xfff5,     0xfff6,     0xfff7,     /*212-215 */
    0x7ffe0,    0x7ffe1,    0x7ffe2,    0x7ffe3,    /*216-219 */
    0x7ffe4,    0x7ffe5,    0x7ffe6,    0x7ffe7,    /*220-223 */
    0x3ffe8,    0x3ffe9,    0x3ffea,    0x3ffeb,    /*224-227 */
    0x3ffec,    0x3ffed,    0x3ffee,    0x3ffef,    /*228-231 */
    0x3fff0,    0x3fff1,    0x3fff2,    0x3fff3,    /*232-235 */
    0x3fff4,    0x3fff5,    0x3fff6,    0x3fff7,    /*236-239 */
    0x1fffc,    0x1fffd,    0x1fffe,    0x1ffff,    /*240-243 */
    0x20000,    0x20001,    0x20002,    0x20003,    /*244-247 */
    0x10000,    0x10001,    0x10002,    0x10003,    /*248-251 */
    0x10004,    0x10005,    0x10006,    0x10007     /*252-255 */
};

/**
 * Decode a Huffman-encoded string per RFC 7541 Section 5.2.
 *
 * Scans the encoded bytes, builds a bit buffer, and matches codes
 * from the static HPACK Huffman table. Verifies EOS padding (all 1's)
 * at the end of the encoded string.
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
    uint64_t buf = 0;       /* bit accumulator (MSB-aligned) */
    int bits = 0;            /* number of valid bits in buf */
    size_t out_pos = 0;

    for (size_t i = 0; i < input_len; i++) {
        /* Shift in 8 more bits */
        buf = (buf << 8) | input[i];
        bits += 8;

        /* Emit decoded symbols while we have enough bits to match */
        while (bits >= 5) {  /* minimum code length is 5 bits */
            bool matched = false;
            for (int sym = 0; sym < 256; sym++) {
                uint8_t code_len = kHuffLen[sym];
                if (code_len > bits) continue;

                /* Extract the top 'code_len' bits */
                uint32_t top = (uint32_t)(buf >> (bits - code_len));
                if (top == kHuffCode[sym]) {
                    if (out_pos >= output_capacity) return -1;
                    output[out_pos++] = (uint8_t)sym;
                    bits -= code_len;
                    /* Keep only the remaining lower bits */
                    if (bits > 0)
                        buf = buf & ((1ULL << bits) - 1);
                    else
                        buf = 0;
                    matched = true;
                    break;  /* restart matching from the shortest code */
                }
            }
            /* No symbol matched with the current bit pattern;
             * need more input bits. */
            if (!matched) break;
        }
    }

    /* ── Padding check ────────────────────────────────────────
     * RFC 7541 Section 5.2: The encoded string is padded at the
     * end with 1-bits (the EOS symbol prefix). Any remaining bits
     * in the buffer MUST be all 1's. */
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
 * We support:
 *   - Indexed static table field (0x00 prefix = 00xxxxxx)
 *   - Literal with static table name reference (0x5N prefix = 0101xxxx)
 *   - Literal without name reference (0x2N prefix = 001xxxxx)
 */
static int qpack_parse_field(const uint8_t* buf, size_t buf_len,
                             h3_header_t* out)
{
    if (buf_len < 1) return -1;
    memset(out, 0, sizeof(*out));

    uint8_t first = buf[0];
    uint8_t consumed = 0;
    bool name_from_static = false;

    if ((first & 0xC0) == 0x00) {
        /* Indexed field line (static table).  Per RFC 9204 §4.3.1,
         * the entire field (name AND value) comes from the static
         * table.  Only the varint-encoded index is on the wire —
         * there is NO value following the index.  Return immediately
         * to prevent the value reader (line 496) from consuming
         * subsequent bytes as a non-existent wire value. */
        uint8_t nbytes;
        uint64_t idx;
        if (varint_decode(buf, buf_len, &idx, &nbytes) < 0) return -1;
        if (idx >= QPACK_STATIC_SIZE) return -1;
        out->name_len = kQpackStatic[idx].name_len;
        memcpy(out->name, kQpackStatic[idx].name, out->name_len);
        return (int)nbytes;
    }
    else if ((first & 0xC0) == 0x40) {
        /* Literal with name reference (static table: 01 prefix) */
        uint8_t nbytes;
        uint64_t idx;
        if (varint_decode(buf, buf_len, &idx, &nbytes) < 0) return -1;
        consumed = nbytes;
        const char* sname = qpack_static_name(idx, &out->name_len);
        if (!sname) return -1;
        memcpy(out->name, sname, out->name_len);
        name_from_static = true;
    }
    else if ((first & 0xE0) == 0x20) {
        /* Literal without name reference (001 prefix).
         * The name is encoded inline. Format:
         *   001NNNNN [name length varint] [name bytes] [value length varint] [value bytes] */
        uint8_t nbytes;
        uint64_t name_len_val;
        /* The varint after the prefix byte encodes the name length.
         * First byte already consumed the 3-bit prefix;
         * re-read the full varint from the start.
         *
         * Per RFC 9204 Section 4.1.1.3, the first byte format is:
         *   001 N H LLL
         * where N (bit 4) is the never-indexed flag, H (bit 3) is the
         * Huffman flag for the name, and LLL (bits 2-0) are the first
         * 3 bits of the name length.  Previously we masked with 0x1F
         * (5 bits), incorrectly including N and H as part of the length. */
        uint8_t raw_first = first & 0x07; /* mask off 001 prefix + N/H flags */
        if (raw_first < 7) {
            /* Single-byte varint for name length */
            out->name_len = raw_first;
            consumed = 1;
        } else {
            /* Multi-byte varint; re-encode for varint_decode */
            uint8_t tmp[8];
            tmp[0] = first;
            uint64_t nv;
            if (varint_decode(tmp, buf_len, &nv, &nbytes) < 0) return -1;
            out->name_len = (uint8_t)nv;
            consumed = nbytes;
        }
        if (consumed + out->name_len > buf_len) return -1;
        if (out->name_len >= sizeof(out->name)) return -1;
        memcpy(out->name, buf + consumed, out->name_len);
        consumed += out->name_len;
        /* name_from_static stays false */
    }
    else if ((first & 0xF0) == 0x10) {
        /* Literal with name reference (dynamic table: 0001 prefix).
         * We don't support dynamic tables. */
        return -1;
    }
    else {
        return -1;
    }

    /* Read value: Huffman bit + length + data */
    if (consumed >= buf_len) return -1;
    uint8_t vbytes;
    uint64_t vlen;
    if (varint_decode(buf + consumed, buf_len - consumed, &vlen, &vbytes) < 0)
        return -1;

    bool huffman = (buf[consumed] & 0x80) != 0;
    /* Mask off Huffman bit from the length varint */
    if (vbytes > 0 && huffman) {
        /* Re-decode length without the Huffman bit.
         * The varint encoded the literal length with bit 7 set.
         * We need the actual length, which is the varint value
         * minus the prefix bit's contribution. */
        uint8_t raw[8];
        memcpy(raw, buf + consumed, vbytes);
        raw[0] &= 0x7F;  /* clear Huffman bit */
        uint8_t tmp_bytes;
        if (varint_decode(raw, vbytes, &vlen, &tmp_bytes) < 0)
            return -1;
    }

    consumed += vbytes;
    if (consumed + vlen > buf_len) return -1;
    if (vlen >= sizeof(out->value)) return -1;

    /* Decode Huffman-encoded value or copy raw bytes */
    if (huffman) {
        uint8_t decoded[sizeof(out->value)];
        int decoded_len = huffman_decode(buf + consumed, (size_t)vlen,
                                          decoded, sizeof(decoded));
        if (decoded_len < 0) return -1;
        memcpy(out->value, decoded, (size_t)decoded_len);
        out->value[decoded_len] = '\0';
        out->value_len = (uint16_t)decoded_len;
    } else {
        memcpy(out->value, buf + consumed, (size_t)vlen);
        out->value[vlen] = '\0';
        out->value_len = (uint16_t)vlen;
    }
    consumed += (uint8_t)vlen;

    return consumed;
}

/* ═══════════════════════════════════════════════════════════════
 * HTTP/3 Frame Write Helpers
 * ═══════════════════════════════════════════════════════════════ */

/**
 * Write a SETTINGS frame to `out`. Returns bytes written.
 * The caller must ensure `out` has at least 256 bytes.
 */
static int h3_write_settings(uint8_t* out)
{
    uint8_t* p = out;
    /* SETTINGS frame: type=4.
     * Advertises both SETTINGS_ENABLE_WEBTRANSPORT (RFC 9220 §5) and
     * SETTINGS_H3_DATAGRAM (RFC 9297 §5.1).  Chrome requires
     * SETTINGS_ENABLE_WEBTRANSPORT to proceed with the WebTransport
     * handshake; without it the browser treats this as a plain H3
     * connection and never sends the CONNECT request. */
    uint8_t type_buf[8], len_buf[8];
    uint8_t type_n = varint_encode(H3_FRAME_SETTINGS, type_buf);

    /* Encode two settings:
     *   1. SETTINGS_ENABLE_WEBTRANSPORT (0x2b603742) = 1  → 5 bytes
     *   2. SETTINGS_H3_DATAGRAM        (0x33)       = 1  → 2 bytes
     *                                             total = 7 bytes */
    uint8_t  settings_payload[16];
    uint8_t* sp = settings_payload;

    /* SETTINGS_ENABLE_WEBTRANSPORT = 1 (4-byte varint ID + 1-byte value) */
    uint8_t wt_id_buf[8], wt_val_buf[8];
    uint8_t wt_sid_n = varint_encode(H3_SETTINGS_ENABLE_WEBTRANSPORT, wt_id_buf);
    uint8_t wt_sval_n = varint_encode(1, wt_val_buf);
    memcpy(sp, wt_id_buf, wt_sid_n); sp += wt_sid_n;
    memcpy(sp, wt_val_buf, wt_sval_n); sp += wt_sval_n;

    /* SETTINGS_H3_DATAGRAM = 1 (1-byte varint ID + 1-byte value) */
    uint8_t dg_id_buf[8], dg_val_buf[8];
    uint8_t dg_sid_n = varint_encode(H3_SETTINGS_DATAGRAM, dg_id_buf);
    uint8_t dg_sval_n = varint_encode(1, dg_val_buf);
    memcpy(sp, dg_id_buf, dg_sid_n); sp += dg_sid_n;
    memcpy(sp, dg_val_buf, dg_sval_n); sp += dg_sval_n;

    uint32_t settings_payload_len = (uint32_t)(sp - settings_payload);

    uint8_t len_n  = varint_encode(settings_payload_len, len_buf);
    memcpy(p, type_buf, type_n); p += type_n;
    memcpy(p, len_buf, len_n);   p += len_n;
    memcpy(p, settings_payload, settings_payload_len); p += settings_payload_len;
    return (int)(p - out);
}

/* ═══════════════════════════════════════════════════════════════
 * HEADERS Frame Builder (used by both server and client)
 * ═══════════════════════════════════════════════════════════════ */

/**
 * Write a complete HEADERS frame to `out`.
 *
 * For server response (STATUS):
 *   Encodes: :status: <val>
 *
 * For client request (CONNECT):
 *   Encodes: :method: CONNECT
 *            :protocol: webtransport
 *            :path: <path>
 *            :authority: <authority>
 *
 * Returns bytes written, or 0 on error.
 */
static int h3_write_headers(uint8_t* out, size_t out_cap,
                            int status_code,
                            const char* method,
                            const char* path,
                            const char* authority)
{
    /* Build the field section in a temp buffer first */
    uint8_t fields[2048];
    uint8_t* fp = fields;

    bool is_response = (status_code > 0);

    if (is_response) {
        /* :status: <status_code>
         * Literal with static name ref (idx=24 for :status 200, but we
         * use literal-with-name-ref idx=22 (:status) and encode the
         * numeric value inline to support any status code. */
        char s[4]; int slen = 0;
        int sc = status_code;
        if (sc >= 100) { s[slen++] = '0' + (sc / 100); sc %= 100; }
        if (sc >= 10 || slen > 0) { s[slen++] = '0' + (sc / 10); sc %= 10; }
        s[slen++] = '0' + sc;

        /* Use static table idx=22 (:status) with literal value.
         * 0101xxxx prefix + varint(22) */
        *fp++ = 0x40 | 22;  /* literal + static name ref idx=22 (:status) */
        uint8_t vb[8];
        uint8_t vn = varint_encode((uint64_t)slen, vb);
        memcpy(fp, vb, vn); fp += vn;
        memcpy(fp, s, (size_t)slen); fp += slen;
    }
    else {
        /* :method: CONNECT — literal + static name ref idx=15 (:method CONNECT)
         * 0101xxxx prefix + varint(15) */
        *fp++ = 0x40 | 15;
        uint8_t vb[8]; uint8_t vn;
        size_t mlen = strlen(method);
        vn = varint_encode(mlen, vb);
        memcpy(fp, vb, vn); fp += vn;
        memcpy(fp, method, mlen); fp += mlen;

        /* :protocol: webtransport — literal without name ref (never-indexed)
         * 001 prefix + name length + ":protocol" + value length + "webtransport" */
        const char* proto = "webtransport";
        size_t plen = strlen(proto);
        *fp++ = 0x20; /* literal without name ref, never-indexed */
        uint8_t nb = 10; /* ":protocol" length */
        *fp++ = nb;
        memcpy(fp, ":protocol", nb); fp += nb;
        vn = varint_encode(plen, vb);
        memcpy(fp, vb, vn); fp += vn;
        memcpy(fp, proto, plen); fp += plen;

        /* :path: <path> — literal + static name ref idx=1 (:path) */
        if (path && path[0]) {
            *fp++ = 0x40 | 1;
            size_t pathlen = strlen(path);
            vn = varint_encode(pathlen, vb);
            memcpy(fp, vb, vn); fp += vn;
            memcpy(fp, path, pathlen); fp += pathlen;
        }

        /* :authority: <authority> — literal + static name ref idx=0 (:authority) */
        if (authority && authority[0]) {
            *fp++ = 0x40 | 0;
            size_t authlen = strlen(authority);
            vn = varint_encode(authlen, vb);
            memcpy(fp, vb, vn); fp += vn;
            memcpy(fp, authority, authlen); fp += authlen;
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

static QUIC_STATUS h3_stream_send(HQUIC stream, const uint8_t* data,
                                  uint32_t len)
{
    uint8_t* copy = (uint8_t*)malloc(len);
    if (!copy) return QUIC_STATUS_OUT_OF_MEMORY;
    memcpy(copy, data, len);

    QUIC_BUFFER buf;
    buf.Buffer = copy;
    buf.Length = len;

    QUIC_STATUS st = MsQuic->StreamSend(stream, &buf, 1,
                                         QUIC_SEND_FLAG_FIN, copy);
    if (QUIC_FAILED(st)) {
        free(copy);
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

    QUIC_BUFFER buf;
    buf.Buffer = copy;
    buf.Length = len;

    QUIC_STATUS st = MsQuic->StreamSend(stream, &buf, 1,
                                         QUIC_SEND_FLAG_NONE, copy);
    if (QUIC_FAILED(st)) {
        free(copy);
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

        /* Cap at 64KB for HTTP/3 frames */
        if (sctx->recv_offset + total > 65536) {
            MsQuic->StreamShutdown(stream, QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
            return QUIC_STATUS_ABORTED;
        }

        uint32_t needed = sctx->recv_offset + total;
        if (needed > sctx->recv_capacity) {
            uint32_t new_cap = sctx->recv_capacity ? sctx->recv_capacity * 2 : 4096;
            if (new_cap < needed) new_cap = needed;
            uint8_t* newbuf = (uint8_t*)realloc(sctx->recv_buf, new_cap);
            if (!newbuf) return QUIC_STATUS_OUT_OF_MEMORY;
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
         * which creates the wt_session. */
        if (sctx->h3) {
            h3_server_process_data(sctx->h3, sctx);
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
            if (sctx->h3) {
                h3_server_process_data(sctx->h3, sctx);
            }
        }
        return QUIC_STATUS_SUCCESS;
    }

    case QUIC_STREAM_EVENT_SEND_COMPLETE:
        free(event->SEND_COMPLETE.ClientContext);
        return QUIC_STATUS_SUCCESS;

    case QUIC_STREAM_EVENT_PEER_SEND_ABORTED:
    case QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE:
        if (sctx->h3) h3_stream_ctx_unlink(sctx);
        free(sctx->recv_buf);
        free(sctx);
        MsQuic->StreamClose(stream);
        return QUIC_STATUS_SUCCESS;

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
        sctx->next = h3->stream_ctx_list;
        h3->stream_ctx_list = sctx;
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
    h3_stream_ctx_t** pp = &h3->stream_ctx_list;
    while (*pp) {
        if (*pp == sctx) {
            *pp = sctx->next;
            sctx->next = NULL;
            sctx->h3 = NULL;
            return;
        }
        pp = &(*pp)->next;
    }
}

/* ── Send-only control-stream callback ──────────────────────────
 * Used for HTTP/3 control streams and request streams where the
 * only purpose is to send a fixed buffer (with FIN), then clean up.
 * Without a callback, SEND_COMPLETE events are silently dropped and
 * the ClientContext (malloc'd buffer) is leaked.
 */
static QUIC_STATUS QUIC_API
h3_send_only_stream_cb(HQUIC stream, void* ctx, QUIC_STREAM_EVENT* event)
{
    (void)stream;
    (void)ctx;
    switch (event->Type) {
    case QUIC_STREAM_EVENT_SEND_COMPLETE:
        free(event->SEND_COMPLETE.ClientContext);
        break;
    case QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE:
        MsQuic->StreamClose(stream);
        break;
    default:
        break;
    }
    return QUIC_STATUS_SUCCESS;
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

        /* Cap at 64KB for HTTP/3 frames */
        if (sctx->recv_offset + total > 65536) {
            MsQuic->StreamShutdown(stream, QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
            return QUIC_STATUS_ABORTED;
        }

        uint32_t needed = sctx->recv_offset + total;
        if (needed > sctx->recv_capacity) {
            uint32_t new_cap = sctx->recv_capacity ? sctx->recv_capacity * 2 : 4096;
            if (new_cap < needed) new_cap = needed;
            uint8_t* newbuf = (uint8_t*)realloc(sctx->recv_buf, new_cap);
            if (!newbuf) return QUIC_STATUS_OUT_OF_MEMORY;
            sctx->recv_buf = newbuf;
            sctx->recv_capacity = new_cap;
        }

        for (uint32_t i = 0; i < count; i++) {
            memcpy(sctx->recv_buf + sctx->recv_offset,
                   bufs[i].Buffer, bufs[i].Length);
            sctx->recv_offset += bufs[i].Length;
        }

        /* Process buffered data for client HTTP/3 handshake */
        if (sctx->h3) {
            h3_client_process_data(sctx->h3, sctx);
        }
        return QUIC_STATUS_SUCCESS;
    }

    case QUIC_STREAM_EVENT_PEER_SEND_SHUTDOWN: {
        /* Process any buffered data that hasn't been consumed yet.
         * This handles the case where the 200 OK data arrives in the
         * same stream event as the FIN from the server. */
        if (sctx->recv_offset > 0 && sctx->recv_buf != NULL) {
            if (sctx->h3) {
                h3_client_process_data(sctx->h3, sctx);
            }
        }
        return QUIC_STATUS_SUCCESS;
    }

    case QUIC_STREAM_EVENT_SEND_COMPLETE:
        free(event->SEND_COMPLETE.ClientContext);
        return QUIC_STATUS_SUCCESS;

    case QUIC_STREAM_EVENT_PEER_SEND_ABORTED:
    case QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE:
        if (sctx->h3) h3_stream_ctx_unlink(sctx);
        free(sctx->recv_buf);
        free(sctx);
        MsQuic->StreamClose(stream);
        return QUIC_STATUS_SUCCESS;

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

        if (*offset + frame_len > buf_len) {
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
 * into a simple struct. Supports literal-with-name-reference,
 * indexed static table, and literal-without-name-reference encodings.
 */

typedef struct {
    char    method[16];
    char    protocol[32];
    char    path[256];
    char    authority[256];
    char    status[8];
    char    origin[256];
} h3_parsed_headers_t;

static bool h3_parse_headers(const uint8_t* data, uint64_t data_len,
                             h3_parsed_headers_t* out)
{
    memset(out, 0, sizeof(*out));
    uint64_t parsed = 0;

    while (parsed < data_len) {
        /* QPACK field lines are self-delimiting — there is NO per-field
         * length prefix on the wire (RFC 9204). Each call to
         * qpack_parse_field consumes one complete field line and returns
         * the number of bytes consumed. */
        h3_header_t hdr;
        int consumed = qpack_parse_field(data + parsed,
                                         (size_t)(data_len - parsed), &hdr);
        if (consumed < 0) {
            /* Unsupported encoding — advance past this byte and retry.
             * This handles fields with encodings we don't support
             * (e.g. Huffman-coded values, dynamic table refs).
             * The handshake still succeeds if the required pseudo-headers
             * use supported encodings (which all major browsers do). */
            parsed++;
            continue;
        }
        parsed += (uint64_t)consumed;

        /* Map known pseudo-headers */
        if (hdr.name_len == 7 && memcmp(hdr.name, ":method", 7) == 0) {
            size_t cp = hdr.value_len < sizeof(out->method)-1
                        ? hdr.value_len : sizeof(out->method)-1;
            memcpy(out->method, hdr.value, cp);
        }
        else if (hdr.name_len == 9 && memcmp(hdr.name, ":protocol", 9) == 0) {
            size_t cp = hdr.value_len < sizeof(out->protocol)-1
                        ? hdr.value_len : sizeof(out->protocol)-1;
            memcpy(out->protocol, hdr.value, cp);
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

    if (is_server) {
        h3->server_state = H3_SRV_WAIT_CONTROL_STREAM;
    } else {
        h3->client_state = H3_CLI_SENDING_SETTINGS;
    }

    return h3;
}

void h3_session_free(h3_session_t* h3)
{
    if (!h3) return;

    /* Shut down tracked control streams to trigger SHUTDOWN_COMPLETE
     * which frees their per-stream contexts (allocated by
     * h3_stream_ctx_create).  These control streams use
     * h3_send_only_stream_cb (no h3_stream_ctx_t), so the shutdown
     * is purely for QUIC resource cleanup. */
    if (h3->server_control_stream) {
        MsQuic->StreamShutdown(h3->server_control_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        h3->server_control_stream = NULL;
    }
    if (h3->client_control_stream) {
        MsQuic->StreamShutdown(h3->client_control_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        h3->client_control_stream = NULL;
    }

    /* Free all tracked h3_stream_ctx_t objects still on the list.
     * Stream contexts that have already been freed through the normal
     * callback chain (SHUTDOWN_COMPLETE) were unlinked by
     * h3_stream_ctx_unlink and are not in this list. */
    h3_stream_ctx_t* sctx = h3->stream_ctx_list;
    while (sctx) {
        h3_stream_ctx_t* next = sctx->next;
        free(sctx->recv_buf);
        free(sctx);
        sctx = next;
    }
    h3->stream_ctx_list = NULL;

    free(h3);
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
    QUIC_STATUS st = MsQuic->StreamOpen(h3->quic_conn,
        QUIC_STREAM_OPEN_FLAG_NONE, h3_send_only_stream_cb, NULL, &ctrl_stream);
    if (QUIC_FAILED(st)) return -1;

    /* Write stream type byte + SETTINGS frame */
    uint8_t ctrl_data[256];
    ctrl_data[0] = H3_STREAM_CONTROL;
    int settings_len = h3_write_settings(ctrl_data + 1);
    int ctrl_total = 1 + settings_len;

    /* Start stream with the type byte, then send SETTINGS */
    st = MsQuic->StreamStart(ctrl_stream, QUIC_STREAM_START_FLAG_IMMEDIATE);
    if (QUIC_FAILED(st)) {
        MsQuic->StreamClose(ctrl_stream);
        return -1;
    }

    uint8_t* ctrl_copy = (uint8_t*)malloc((size_t)ctrl_total);
    if (!ctrl_copy) { MsQuic->StreamClose(ctrl_stream); return -1; }
    memcpy(ctrl_copy, ctrl_data, (size_t)ctrl_total);

    {
        QUIC_BUFFER buf;
        buf.Buffer = ctrl_copy;
        buf.Length = (uint32_t)ctrl_total;
        st = MsQuic->StreamSend(ctrl_stream, &buf, 1,
                                 QUIC_SEND_FLAG_FIN, ctrl_copy);
        if (QUIC_FAILED(st)) {
            free(ctrl_copy);
            MsQuic->StreamClose(ctrl_stream);
            return -1;
        }
    }

    h3->client_control_stream = ctrl_stream;

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

        /* Link into session's stream context list for cleanup.
         * This ensures the context is freed by h3_session_free if
         * the connection shuts down before the handshake completes. */
        sctx->next = h3->stream_ctx_list;
        h3->stream_ctx_list = sctx;

        st = MsQuic->StreamStart(req_stream, QUIC_STREAM_START_FLAG_IMMEDIATE);
        if (QUIC_FAILED(st)) {
            h3_stream_ctx_unlink(sctx);
            /* StreamClose before free: the stream callback may reference sctx
             * as context.  Closing the stream first prevents a use-after-free
             * if MsQuic fires SHUTDOWN_COMPLETE synchronously. */
            MsQuic->StreamClose(req_stream);
            free(sctx->recv_buf);
            free(sctx);
            return -1;
        }

        uint8_t req_data[2048];
        int req_len = h3_write_headers(req_data, sizeof(req_data),
                                        0, "CONNECT", path, authority);
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
            QUIC_BUFFER buf;
            buf.Buffer = req_copy;
            buf.Length = (uint32_t)req_len;
            st = MsQuic->StreamSend(req_stream, &buf, 1,
                                     QUIC_SEND_FLAG_FIN, req_copy);
            if (QUIC_FAILED(st)) {
                free(req_copy);
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
                            h3_stream_ctx_t** out_sctx)
{
    *out_sctx = NULL;

    if (h3->server_state == H3_SRV_ESTABLISHED) {
        /* WebTransport session already established —
         * this is a regular data stream */
        return 1;
    }

    /* Create stream context for buffered read */
    h3_stream_ctx_t* sctx = h3_stream_ctx_create(stream, h3);
    if (!sctx) return -1;
    *out_sctx = sctx;

    if (h3->server_state == H3_SRV_WAIT_CONTROL_STREAM) {
        /* This is the first peer stream — could be HTTP/3 control
         * stream or raw QUIC data stream.  We'll know when the
         * first RECEIVE event arrives (checked in the data handler). */
        sctx->stream_type = -1;  /* detection pending */
        h3->server_state = H3_SRV_GOT_SETTINGS;  /* transition */
        return 0;
    }

    if (h3->server_state == H3_SRV_GOT_SETTINGS ||
        h3->server_state == H3_SRV_WAIT_CONNECT) {
        /* This could be the CONNECT request stream.
         * We need to read and parse HEADERS. */
        sctx->is_request = true;
        h3->server_state = H3_SRV_WAIT_CONNECT;
        return 0;
    }

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
    if (h3->server_state == H3_SRV_ESTABLISHED) return 1;

    if (sctx->recv_offset == 0) return 0;  /* no data yet */

    /* Protocol detection: check first byte */
    if (sctx->stream_type == -1) {
        if (sctx->recv_offset < 1) return 0;
        uint8_t first_byte = sctx->recv_buf[0];

        if (first_byte == H3_STREAM_CONTROL) {
            /* HTTP/3 control stream detected!
             * This is a browser WebTransport client. */
            sctx->stream_type = H3_STREAM_CONTROL;

            /* Send server SETTINGS response */
            uint8_t settings_buf[64];
            int settings_len = h3_write_settings(settings_buf);

            /* Open server control stream */
            HQUIC srv_ctrl = NULL;
            QUIC_STATUS st = MsQuic->StreamOpen(
                h3->quic_conn, QUIC_STREAM_OPEN_FLAG_NONE,
                h3_send_only_stream_cb, NULL, &srv_ctrl);
            if (QUIC_FAILED(st)) return -1;

            st = MsQuic->StreamStart(srv_ctrl,
                                      QUIC_STREAM_START_FLAG_IMMEDIATE);
            if (QUIC_FAILED(st)) {
                MsQuic->StreamClose(srv_ctrl);
                return -1;
            }

            /* Send: stream type byte (0x00) + SETTINGS frame */
            uint8_t* srv_data = (uint8_t*)malloc((size_t)(1 + settings_len));
            if (!srv_data) { MsQuic->StreamClose(srv_ctrl); return -1; }
            srv_data[0] = H3_STREAM_CONTROL;
            memcpy(srv_data + 1, settings_buf, (size_t)settings_len);

            {
                QUIC_BUFFER buf;
                buf.Buffer = srv_data;
                buf.Length = (uint32_t)(1 + settings_len);
                st = MsQuic->StreamSend(srv_ctrl, &buf, 1,
                                         QUIC_SEND_FLAG_FIN, srv_data);
                if (QUIC_FAILED(st)) {
                    free(srv_data);
                    MsQuic->StreamClose(srv_ctrl);
                    return -1;
                }
            }
            h3->server_control_stream = srv_ctrl;
            h3->server_state = H3_SRV_WAIT_CONNECT;

            /* Parse client SETTINGS from this stream (after type byte) */
            if (sctx->recv_offset > 1) {
                size_t offset = 1; /* skip stream type byte */
                h3_parse_frames(sctx->recv_buf + 1,
                                sctx->recv_offset - 1,
                                &offset, NULL, NULL);
            }
            return 0;
        }
        else {
            /* NOT a control stream. If we're already in HTTP/3 mode
             * (control stream was processed and we're waiting for a
             * CONNECT request), route through CONNECT validation at
             * line 1518 instead of the raw/native fallback.
             * Without this check, any browser WebTransport CONNECT
             * request bypasses all security validation (CORS origin
             * check, :method, :protocol, :authority). */
            if (h3->server_state >= H3_SRV_WAIT_CONNECT) {
                sctx->stream_type = H3_STREAM_REQUEST;
                sctx->is_request = true;
                /* Parse the HEADERS frame on this stream.
                 * h3_server_process_data will be called again after
                 * more data arrives on this stream, and the CONNECT
                 * validation path at line 1518 will handle it. */
                if (sctx->recv_offset > 0) {
                    size_t pos = 0;
                    h3_parse_frames(sctx->recv_buf, sctx->recv_offset,
                                    &pos, NULL, NULL);
                }
                return 0;
            }

            /* Raw/native protocol detected — not HTTP/3.
             * Signal to the caller to proceed with wt_session. */
            sctx->stream_type = -2; /* mark as raw */
            h3->handshake_complete = true;
            h3->server_state = H3_SRV_ESTABLISHED;

            /* ── CRITICAL: Save stream context for data replay ──
             * The buffered data in sctx->recv_buf was received before
             * the wt_session was created. Store the sctx so
             * on_h3_session_ready (in server.cpp) can accept this
             * stream into the stream_manager and replay the data.
             * Without this, the first stream's data is silently lost. */
            h3->native_stream_ctx = sctx;

            if (h3->on_ready) {
                h3->on_ready(h3->callback_ctx, h3->quic_conn, "/", "");
            }
            /* native_stream_ctx consumed (or NULLed) by on_h3_session_ready.
             * If it was consumed, the stream and data are now managed by
             * the stream_manager. If on_ready failed, the data is lost
             * (acceptable — connection is shutting down anyway). */
            return 1;
        }
    }

    if (sctx->stream_type == H3_STREAM_CONTROL) {
        /* Parse SETTINGS from control stream data (after type byte) */
        if (sctx->recv_offset > 1) {
            size_t offset = 1;
            h3_parse_frames(sctx->recv_buf + 1,
                            sctx->recv_offset - 1,
                            &offset, NULL, NULL);
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
                        if (pos + flen <= sctx->recv_offset) {
                            if (ftype == H3_FRAME_HEADERS) {
                                if (h3_parse_headers(sctx->recv_buf + pos,
                                                     flen, &phdr)) {
                                    /* Validate CONNECT */
                                    if (strcmp(phdr.method, "CONNECT") == 0 &&
                                        strcmp(phdr.protocol, "webtransport") == 0) {

                                        /* Validate Origin header for cross-origin
                                         * WebTransport. Uses exact-match with length
                                         * check to prevent prefix-injection attacks. */
                                        if (phdr.origin[0] != '\0') {
                                            bool origin_ok = false;
                                            size_t origin_len = strlen(phdr.origin);

                                            const char* allowed = h3->allowed_origins;
                                            if (allowed == NULL || allowed[0] == '\0') {
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
                                                    rej, sizeof(rej), 403, NULL, NULL, NULL);
                                                if (rej_len > 0) {
                                                    uint8_t* rej_copy = (uint8_t*)malloc((size_t)rej_len);
                                                    if (rej_copy) {
                                                        memcpy(rej_copy, rej, (size_t)rej_len);
                                                        QUIC_BUFFER buf;
                                                        buf.Buffer = rej_copy;
                                                        buf.Length = (uint32_t)rej_len;
                                                        MsQuic->StreamSend(sctx->quic_stream, &buf, 1,
                                                            QUIC_SEND_FLAG_NONE, rej_copy);  /* Do NOT FIN */
                                                    }
                                                }
                                                return -1;  /* origin rejected */
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
                                         * wt_session is ready to accept them. */
                                        h3->handshake_complete = true;
                                        h3->server_state = H3_SRV_ESTABLISHED;

                                        if (h3->on_ready) {
                                            h3->on_ready(h3->callback_ctx,
                                                         h3->quic_conn,
                                                         h3->request_path,
                                                         h3->request_authority);
                                        }

                                        /* Now send 200 OK — wt_session is ready to receive data */
                                        uint8_t resp[256];
                                        int resp_len = h3_write_headers(
                                            resp, sizeof(resp), 200, NULL, NULL, NULL);

                                        if (resp_len > 0) {
                                            uint8_t* resp_copy =
                                                (uint8_t*)malloc((size_t)resp_len);
                                            if (resp_copy) {
                                                memcpy(resp_copy, resp,
                                                       (size_t)resp_len);
                                                QUIC_BUFFER buf;
                                                buf.Buffer = resp_copy;
                                                buf.Length = (uint32_t)resp_len;
                                                MsQuic->StreamSend(
                                                    sctx->quic_stream,
                                                    &buf, 1,
                                                    QUIC_SEND_FLAG_FIN,
                                                    resp_copy);
                                            }
                                        }

                                        return 1;  /* handshake complete! */
                                    }
                                }
                            }
                        }
                    }
                }

                /* If we reach here, parsing failed or wrong request */
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

    /* Check if the full frame payload is available */
    if (pos + flen > sctx->recv_offset)
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
    int len = h3_write_headers(buf, sizeof(buf), 200, NULL, NULL, NULL);
    if (len <= 0) return -1;

    return (h3_stream_send(req_stream, buf, (uint32_t)len) == QUIC_STATUS_SUCCESS)
           ? 0 : -1;
}
#endif /* h3_send_wt_response */