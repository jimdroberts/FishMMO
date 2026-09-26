/* End-to-end test for the statistics exports (ABI 4):
 *   1. wt_get_global_counters refuses before wt_init() and fills a versioned
 *      struct after it; a too-small struct_size is refused, and a short
 *      caller struct receives exactly its prefix
 *   2. traffic on loopback moves every layer: UDP datagrams and bytes both
 *      ways, msquic's app bytes, and the UDP payload exceeds the app bytes
 *      it carries (QUIC headers, AEAD tags, ACKs)
 *   3. with both ends in this process, what was sent is what was received:
 *      process-wide UDP bytes agree once the link is quiet, and for paced,
 *      game-like traffic so do the datagram counts.  (msquic 2.5.9 over-counts
 *      UDP_SEND datagrams in a flush that emits several batches — handshake
 *      flights, some bursts — because QuicPacketBuilderSendBatch passes the
 *      flush's running datagram total, not the batch's.  Bytes are exact.)
 *   4. wt_client_get_connection_stats reports a live connection (RTT, path
 *      MTU, congestion window, bytes and packets both ways) and refuses once
 *      the connection has shut down
 *   5. reads racing the connection's shutdown never touch a closed handle:
 *      repeated disconnect-while-reading cycles, client- and server-initiated
 * Certificates: server.pem/server.key in the working directory, CA trusted
 * via SSL_CERT_FILE — see run_stats_e2e.sh. */
#include "webtransport_api.h"
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <thread>

static std::atomic<int> srv_connects{0};
static std::atomic<long> srv_msgs{0};
static std::atomic<unsigned long long> srv_last_conn{0};

static void s_on_connect(void*, wt_connection_id_t id, const char*) { srv_connects++; srv_last_conn = id; }
static void s_on_disconnect(void*, wt_connection_id_t, int) {}
static void s_on_stream(void*, wt_connection_id_t, wt_stream_id_t, const uint8_t*, int32_t) { srv_msgs++; }
static void s_on_dgram(void*, wt_connection_id_t, const uint8_t*, int32_t) { srv_msgs++; }

struct Cli {
    WT_CLIENT h = nullptr;
    std::atomic<int> connected{0}, disconnected{0};
    std::atomic<long> msgs{0};
};
static void c_on_connect(void* ctx) { ((Cli*)ctx)->connected = 1; }
static void c_on_disconnect(void* ctx, int) { ((Cli*)ctx)->disconnected = 1; }
static void c_on_stream(void* ctx, wt_stream_id_t, const uint8_t*, int32_t) { ((Cli*)ctx)->msgs++; }
static void c_on_dgram(void* ctx, const uint8_t*, int32_t) { ((Cli*)ctx)->msgs++; }

static WT_SERVER srv;
static const uint16_t kPort = 47331;

static void pump(Cli* c, int ms) {
    auto end = std::chrono::steady_clock::now() + std::chrono::milliseconds(ms);
    while (std::chrono::steady_clock::now() < end) {
        wt_server_poll(srv, 0);
        if (c && c->h) wt_client_poll(c->h, 0);
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
}

template <typename F>
static bool pump_until(Cli* c, F done, int ms) {
    auto end = std::chrono::steady_clock::now() + std::chrono::milliseconds(ms);
    while (std::chrono::steady_clock::now() < end) { if (done()) return true; pump(c, 5); }
    return done();
}

static Cli* connect_client() {
    Cli* c = new Cli();
    wt_client_callbacks_t cb{c_on_connect, c_on_disconnect, c_on_stream, c_on_dgram};
    c->h = wt_client_create(&cb, c);
    wt_client_connect(c->h, "localhost", "127.0.0.1", kPort, 1);
    return c;
}

static wt_global_counters_t counters() {
    wt_global_counters_t g;
    memset(&g, 0, sizeof g);
    g.struct_size = sizeof g;
    wt_get_global_counters(&g);
    return g;
}

static int32_t conn_stats(Cli* c, wt_connection_stats_t* s) {
    memset(s, 0, sizeof *s);
    s->struct_size = sizeof *s;
    return wt_client_get_connection_stats(c->h, s);
}

static int failures = 0;
#define CHECK(cond, msg) do { if (cond) printf("PASS: %s\n", msg); else { printf("FAIL: %s\n", msg); failures++; } } while (0)

int main() {
    /* ── 1. header contract ───────────────────────────────────── */
    {
        wt_global_counters_t g; memset(&g, 0, sizeof g); g.struct_size = sizeof g;
        CHECK(wt_get_global_counters(&g) == WT_ERR_INVALID_STATE, "global counters refused before wt_init()");
    }
    if (wt_init() != 0) { printf("wt_init failed\n"); return 2; }
    {
        wt_global_counters_t g; memset(&g, 0xEE, sizeof g); g.struct_size = sizeof g;
        CHECK(wt_get_global_counters(&g) == WT_OK, "global counters readable after wt_init()");
        CHECK(g.version == WT_GLOBAL_COUNTERS_VERSION && g.struct_size == sizeof g,
              "global counters report their version and the bytes written");
        wt_global_counters_t tiny; memset(&tiny, 0, sizeof tiny); tiny.struct_size = 4;
        CHECK(wt_get_global_counters(&tiny) == WT_ERR_UNKNOWN, "a struct_size below the header is refused");
        CHECK(wt_get_global_counters(nullptr) == WT_ERR_UNKNOWN, "a NULL struct is refused");
        /* A caller compiled against a shorter layout gets its prefix and not a byte more. */
        unsigned char buf[sizeof(wt_global_counters_t) + 16];
        memset(buf, 0xCD, sizeof buf);
        wt_global_counters_t* shortg = (wt_global_counters_t*)buf;
        shortg->struct_size = 24;   /* header + two counters */
        CHECK(wt_get_global_counters(shortg) == WT_OK && shortg->struct_size == 24 && buf[24] == 0xCD,
              "a short caller struct receives exactly its prefix");
    }

    wt_server_callbacks_t scb{s_on_connect, s_on_disconnect, s_on_stream, s_on_dgram};
    srv = wt_server_create("server.pem", "server.key", "h3", "127.0.0.1", kPort, 64, nullptr, &scb, nullptr);
    if (!srv) { printf("server create failed\n"); return 2; }
    /* No connect interval: the race loop below reconnects back to back. */
    wt_server_set_limits(srv, 0, -1, -1, 0, 0, 0, -1, -1);
    if (wt_server_start(srv) != 0) { printf("server start failed\n"); return 2; }

    /* ── 2. traffic moves every layer ────────────────────────── */
    wt_global_counters_t before = counters();
    Cli* a = connect_client();
    {
        wt_connection_stats_t s;
        /* Before the handshake completes the connection already exists. */
        CHECK(conn_stats(a, &s) == WT_OK, "connection stats readable while the handshake is in flight");
    }
    CHECK(pump_until(a, [&] { return a->connected.load() != 0; }, 5000), "client connects");

    const int kStream = 200, kDgram = 200, kLen = 100;
    uint8_t payload[kLen]; memset(payload, 0x5A, sizeof payload);
    /* The first stream message turns the native connection into a server session. */
    wt_client_send_stream(a->h, payload, kLen);
    CHECK(pump_until(a, [&] { return srv_connects.load() >= 1; }, 5000), "server establishes the session");
    unsigned long long sid = srv_last_conn;
    for (int i = 1; i < kStream; i++) { wt_client_send_stream(a->h, payload, kLen); if (i % 20 == 0) pump(a, 5); }
    for (int i = 0; i < kDgram; i++) { wt_client_send_datagram(a->h, payload, kLen); if (i % 20 == 0) pump(a, 5); }
    for (int i = 0; i < kStream; i++) { wt_server_send_stream(srv, sid, payload, kLen); if (i % 20 == 0) pump(a, 5); }
    for (int i = 0; i < kDgram; i++) { wt_server_send_datagram(srv, sid, payload, kLen); if (i % 20 == 0) pump(a, 5); }
    pump_until(a, [&] { return srv_msgs.load() >= kStream + kDgram / 2 && a->msgs.load() >= kStream + kDgram / 2; }, 5000);
    pump(a, 1500);   /* let ACKs settle so both ends have seen everything */
    printf("[msgs] server got %ld, client got %ld\n", srv_msgs.load(), a->msgs.load());

    wt_global_counters_t after = counters();
    printf("[udp] send %llu dgrams / %llu B, recv %llu dgrams / %llu B; app send %llu B, recv %llu B\n",
           (unsigned long long)(after.udp_send_datagrams - before.udp_send_datagrams),
           (unsigned long long)(after.udp_send_bytes - before.udp_send_bytes),
           (unsigned long long)(after.udp_recv_datagrams - before.udp_recv_datagrams),
           (unsigned long long)(after.udp_recv_bytes - before.udp_recv_bytes),
           (unsigned long long)(after.app_send_bytes - before.app_send_bytes),
           (unsigned long long)(after.app_recv_bytes - before.app_recv_bytes));
    /* Both ends send kStream + kDgram messages of kLen; the process sees both. */
    const unsigned long long appPayload = 2ULL * (kStream + kDgram) * kLen;
    CHECK(after.udp_send_datagrams > before.udp_send_datagrams, "UDP datagrams sent moved");
    CHECK(after.udp_recv_datagrams > before.udp_recv_datagrams, "UDP datagrams received moved");
    CHECK(after.app_send_bytes - before.app_send_bytes >= appPayload * 9 / 10,
          "msquic app bytes sent cover the payload handed to it");
    CHECK(after.udp_send_bytes - before.udp_send_bytes > after.app_send_bytes - before.app_send_bytes,
          "UDP payload exceeds the app bytes it carried (headers, tags, ACKs, handshake)");
    CHECK(after.conn_created >= before.conn_created + 2, "both ends' connections were counted as created");
    CHECK(after.conn_connected >= 2, "gauge: both ends' connections are connected");

    /* ── 3. one process, both ends: sent == received on loopback ── */
    {
        unsigned long long sd = after.udp_send_datagrams - before.udp_send_datagrams;
        unsigned long long rd = after.udp_recv_datagrams - before.udp_recv_datagrams;
        unsigned long long sb = after.udp_send_bytes - before.udp_send_bytes;
        unsigned long long rb = after.udp_recv_bytes - before.udp_recv_bytes;
        CHECK(rb <= sb && rb * 1000 >= sb * 999, "loopback: bytes received match bytes sent (handshake and bursts included)");
        CHECK(rd <= sd, "loopback: the datagram send count never under-counts");
    }
    {
        /* Paced like a game tick: a few datagrams and a stream message each
         * way every 5 ms.  Every flush is one batch, so msquic's send count is
         * exact and the two sides must agree to the datagram. */
        wt_global_counters_t p0 = counters();
        for (int t = 0; t < 100; t++) {
            wt_server_send_datagram(srv, sid, payload, kLen);
            wt_server_send_stream(srv, sid, payload, kLen);
            wt_client_send_datagram(a->h, payload, kLen);
            pump(a, 5);
        }
        pump(a, 1000);
        wt_global_counters_t p1 = counters();
        unsigned long long sd = p1.udp_send_datagrams - p0.udp_send_datagrams;
        unsigned long long rd = p1.udp_recv_datagrams - p0.udp_recv_datagrams;
        unsigned long long sb = p1.udp_send_bytes - p0.udp_send_bytes;
        unsigned long long rb = p1.udp_recv_bytes - p0.udp_recv_bytes;
        printf("[paced] send %llu dgrams / %llu B, recv %llu dgrams / %llu B\n", sd, sb, rd, rb);
        CHECK(sd >= 200 && sd <= rd + 2 && rd <= sd, "paced loopback: datagrams sent and received agree");
        CHECK(sb == rb, "paced loopback: bytes sent and received agree exactly");
    }

    /* ── 4. connection statistics ────────────────────────────── */
    {
        wt_connection_stats_t s;
        CHECK(conn_stats(a, &s) == WT_OK, "connection stats readable on a live connection");
        printf("[conn] rtt=%uus min=%uus var=%uus mtu=%u cwnd=%u flags=0x%x send %llu pkts/%llu B recv %llu pkts/%llu B lost %llu-%llu\n",
               s.rtt_us, s.min_rtt_us, s.rtt_variance_us, s.path_mtu, s.congestion_window, s.flags,
               (unsigned long long)s.send_packets, (unsigned long long)s.send_bytes,
               (unsigned long long)s.recv_packets, (unsigned long long)s.recv_bytes,
               (unsigned long long)s.send_suspected_lost_packets, (unsigned long long)s.send_spurious_lost_packets);
        CHECK(s.version == WT_CONNECTION_STATS_VERSION && s.struct_size == sizeof s, "connection stats report version and size");
        CHECK(s.rtt_us > 0 && s.min_rtt_us > 0 && s.min_rtt_us <= s.rtt_us + 1000, "RTT reported");
        CHECK(s.path_mtu >= 1200, "path MTU is at least QUIC's 1200-byte minimum");
        CHECK((s.flags & WT_CONN_STATS_HAS_CWND) && s.congestion_window > 0, "congestion window reported");
        CHECK(s.flags & WT_CONN_STATS_HAS_RTT_VARIANCE, "msquic 2.5 reports RTT variance");
        CHECK(s.send_bytes > (unsigned long long)kStream * kLen && s.recv_bytes > (unsigned long long)kStream * kLen,
              "the connection's UDP bytes cover what crossed it both ways");
        CHECK(s.send_packets > 0 && s.recv_packets > 0, "packets counted both ways");
        CHECK(s.send_bytes <= after.udp_send_bytes && s.recv_bytes <= after.udp_recv_bytes,
              "one connection never exceeds the process totals");
        wt_connection_stats_t tiny; memset(&tiny, 0, sizeof tiny); tiny.struct_size = 4;
        CHECK(wt_client_get_connection_stats(a->h, &tiny) == WT_ERR_UNKNOWN, "a too-small connection struct is refused");
    }

    wt_client_disconnect(a->h);
    pump_until(a, [&] { return a->disconnected.load() != 0; }, 5000);
    pump(a, 200);
    {
        wt_connection_stats_t s;
        CHECK(conn_stats(a, &s) == WT_ERR_INVALID_STATE, "connection stats refused after shutdown completed");
    }
    wt_client_destroy(a->h); a->h = nullptr; delete a;

    {
        Cli never; wt_client_callbacks_t cb{c_on_connect, c_on_disconnect, c_on_stream, c_on_dgram};
        never.h = wt_client_create(&cb, &never);
        wt_connection_stats_t s;
        CHECK(conn_stats(&never, &s) == WT_ERR_INVALID_STATE, "connection stats refused before connect");
        wt_client_destroy(never.h); never.h = nullptr;
    }

    /* ── 5. reads racing shutdown ─────────────────────────────── */
    int reads_ok = 0, reads_refused = 0, reads_other = 0, cycles = 0;
    for (int round = 0; round < 12; round++) {
        Cli* c = connect_client();
        if (!pump_until(c, [&] { return c->connected.load() != 0; }, 5000)) { printf("race round %d: no connect\n", round); wt_client_destroy(c->h); delete c; continue; }
        int base = srv_connects;
        wt_client_send_stream(c->h, payload, kLen);
        pump_until(c, [&] { return srv_connects.load() > base; }, 3000);
        cycles++;
        if (round % 2 == 0) {
            wt_client_disconnect(c->h);                 /* client-initiated */
        } else {
            wt_server_disconnect(srv, srv_last_conn);   /* server-initiated */
        }
        /* Read as fast as possible across the shutdown; the client's
         * SHUTDOWN_COMPLETE lands on its worker somewhere inside this loop.
         * The server is polled in the same tight loop, across its own
         * SHUTDOWN_COMPLETE: that used to free each slot's h3_session while
         * wt_server_poll was reading it (see app_state in server.h, and
         * run_race_e2e.sh for the dedicated test). */
        auto end = std::chrono::steady_clock::now() + std::chrono::milliseconds(400);
        while (std::chrono::steady_clock::now() < end) {
            wt_connection_stats_t s;
            int32_t r = conn_stats(c, &s);
            if (r == WT_OK) reads_ok++; else if (r == WT_ERR_INVALID_STATE) reads_refused++; else reads_other++;
            wt_client_poll(c->h, 0);
            wt_server_poll(srv, 0);
        }
        pump(c, 300);
        wt_client_destroy(c->h); c->h = nullptr; delete c;
    }
    printf("[race] %d cycles: %d reads answered, %d refused, %d other\n", cycles, reads_ok, reads_refused, reads_other);
    CHECK(cycles >= 10, "race cycles connected");
    CHECK(reads_ok > 0 && reads_refused > 0 && reads_other == 0,
          "reads across shutdown are answered or cleanly refused, never failed");

    wt_server_stop(srv);
    wt_server_destroy(srv);
    wt_deinit();
    {
        wt_global_counters_t g; memset(&g, 0, sizeof g); g.struct_size = sizeof g;
        CHECK(wt_get_global_counters(&g) == WT_ERR_INVALID_STATE, "global counters refused after wt_deinit()");
    }
    printf("\nSTATS-E2E %s (%d failures)\n", failures ? "FAILED" : "PASSED", failures);
    return failures ? 1 : 0;
}
