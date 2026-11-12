/* End-to-end test for the transport-level limits set through
 * wt_server_set_limits().  Runs a server and native clients on loopback:
 *   1. a flooding client gets exactly its burst delivered, then is kicked
 *   2. the per-IP concurrent-connection cap refuses the third connection
 *   3. a paced sender keeps every message and stays connected
 *   4. a released slot can be reused
 *   5. the half-open cap refuses while a connection has no session, and
 *      releases once the first message completes protocol detection
 *   6. a one-off datagram burst far over budget is dropped, not kicked,
 *      and the reliable stream is untouched by it
 *   7. datagrams sent at more than twice the budget for longer than
 *      WT_DGRAM_ABUSE_WINDOWS seconds are kicked
 * Certificates: server.pem/server.key in the working directory, CA trusted
 * via SSL_CERT_FILE — see run_limits_e2e.sh. */
#include "webtransport_api.h"
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <thread>
#include <vector>

static std::atomic<int> srv_connects{0}, srv_disconnects{0};
static std::atomic<long> srv_msgs[16];
static std::atomic<long> srv_dgrams[16];
static std::atomic<int> srv_last_disc_conn{0};

static void s_on_connect(void*, wt_connection_id_t id, const char* addr) {
    printf("[srv] connect %llu from %s\n", (unsigned long long)id, addr); srv_connects++;
}
static void s_on_disconnect(void*, wt_connection_id_t id, int err) {
    printf("[srv] disconnect %llu err=%d\n", (unsigned long long)id, err);
    srv_disconnects++; srv_last_disc_conn = (int)id;
}
static void s_on_stream(void*, wt_connection_id_t id, wt_stream_id_t, const uint8_t*, int32_t) {
    if (id < 16) srv_msgs[id]++;
}
static void s_on_dgram(void*, wt_connection_id_t id, const uint8_t*, int32_t) { if (id < 16) { srv_msgs[id]++; srv_dgrams[id]++; } }

struct Cli {
    WT_CLIENT h = nullptr;
    std::atomic<int> connected{0}, disconnected{0}, err{0};
};
static void c_on_connect(void* ctx) { ((Cli*)ctx)->connected = 1; }
static void c_on_disconnect(void* ctx, int e) { ((Cli*)ctx)->disconnected = 1; ((Cli*)ctx)->err = e; }
static void c_on_stream(void*, wt_stream_id_t, const uint8_t*, int32_t) {}
static void c_on_dgram(void*, const uint8_t*, int32_t) {}

static WT_SERVER srv;
static std::vector<Cli*> clis;
static uint16_t g_port = 47311;

static void pump(int ms) {
    auto end = std::chrono::steady_clock::now() + std::chrono::milliseconds(ms);
    while (std::chrono::steady_clock::now() < end) {
        wt_server_poll(srv, 0);
        for (Cli* c : clis) if (c->h) wt_client_poll(c->h, 0);
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
}
static bool wait_until(std::atomic<int>& flag, int ms) {
    auto end = std::chrono::steady_clock::now() + std::chrono::milliseconds(ms);
    while (std::chrono::steady_clock::now() < end) { if (flag) return true; pump(5); }
    return flag != 0;
}
static Cli* connect_client(const char* name) {
    Cli* c = new Cli();
    wt_client_callbacks_t cb{c_on_connect, c_on_disconnect, c_on_stream, c_on_dgram};
    c->h = wt_client_create(&cb, c);
    clis.push_back(c);
    int r = wt_client_connect(c->h, "localhost", "127.0.0.1", g_port, 1);
    printf("[%s] connect rc=%d\n", name, r);
    return c;
}

static int failures = 0;
#define CHECK(cond, msg) do { if (cond) printf("PASS: %s\n", msg); else { printf("FAIL: %s\n", msg); failures++; } } while (0)

int main() {
    if (wt_init() != 0) { printf("wt_init failed\n"); return 2; }
    wt_server_callbacks_t scb{s_on_connect, s_on_disconnect, s_on_stream, s_on_dgram};
    srv = wt_server_create("server.pem", "server.key", "h3", "127.0.0.1", 47311, 64, nullptr, &scb, nullptr);
    if (!srv) { printf("server create failed\n"); return 2; }
    /* interval 0 so several loopback clients can connect back to back;
     * 2 per IP; 50 msg/s, burst 100, kick after 20 refusals; 8 queued dgrams. */
    wt_server_set_limits(srv, 0, 2, -1, 50, 100, 20, 8, -1);
    if (wt_server_start(srv) != 0) { printf("server start failed\n"); return 2; }

    /* ── Test 1: flood → rate limited and kicked ─────────────── */
    Cli* a = connect_client("A");
    CHECK(wait_until(a->connected, 5000), "client A connects");
    uint8_t payload[32]; memset(payload, 0xAB, sizeof payload);
    int sent = 0;
    for (int i = 0; i < 400; i++) { if (wt_client_send_stream(a->h, payload, sizeof payload) == 0) sent++; if (i % 20 == 19) pump(1); }
    printf("[A] sent %d\n", sent);
    pump(3000);
    long got = srv_msgs[1];
    printf("[srv] received from A: %ld, disconnects=%d\n", got, srv_disconnects.load());
    CHECK(got >= 90 && got <= 200, "server delivered about one burst of A's flood, not all 400");
    CHECK(srv_disconnects >= 1, "server disconnected the flooding client");
    CHECK(wait_until(a->disconnected, 3000) || !wt_client_is_connected(a->h), "client A observes the disconnect");
    pump(500);

    /* ── Test 2: per-IP cap of 2 ─────────────────────────────── */
    int base_conn = srv_connects;
    Cli* b = connect_client("B");
    CHECK(wait_until(b->connected, 5000), "client B connects");
    Cli* c = connect_client("C");
    CHECK(wait_until(c->connected, 5000), "client C connects (second slot for the IP)");
    Cli* d = connect_client("D");
    pump(3000);
    /* The server's on_connect for a native client fires after the H3
     * native-protocol fallback, later than the client's own flag. */
    printf("[srv] client_count=%d D connected=%d D disconnected=%d err=%d\n",
           wt_server_get_client_count(srv), d->connected.load(), d->disconnected.load(), d->err.load());
    CHECK(d->connected == 0, "client D refused by the per-IP cap");
    CHECK(wt_server_get_client_count(srv) == 2, "server holds exactly two connections for the IP");
    /* A native client is not a WebTransport session until its first message
     * arrives (protocol detection reads the first stream byte), so the server's
     * on_connect follows the first send, not the QUIC handshake. */
    wt_client_send_stream(b->h, payload, sizeof payload);
    wt_client_send_stream(c->h, payload, sizeof payload);
    pump(2000);
    CHECK(srv_connects - base_conn == 2, "only two connect callbacks fired");

    /* ── Test 3: well-behaved sender stays connected ─────────── */
    long before_b = srv_msgs[srv_last_disc_conn == 2 ? 3 : 2];
    (void)before_b;
    long total_before = 0; for (int i = 0; i < 16; i++) total_before += srv_msgs[i];
    for (int i = 0; i < 30; i++) { wt_client_send_stream(b->h, payload, sizeof payload); pump(30); }
    pump(1000);
    long total_after = 0; for (int i = 0; i < 16; i++) total_after += srv_msgs[i];
    printf("[B] paced 30 messages, delivered %ld\n", total_after - total_before);
    CHECK(total_after - total_before == 30, "paced sender has every message delivered");
    CHECK(b->disconnected == 0 && wt_client_is_connected(b->h), "paced sender is still connected");

    /* ── Test 4: slot released → new connection fits again ───── */
    wt_client_disconnect(c->h);
    pump(1500);
    Cli* e = connect_client("E");
    CHECK(wait_until(e->connected, 5000), "client E connects after C released its per-IP slot");

    for (Cli* x : clis) { if (x->h) { wt_client_disconnect(x->h); } }
    pump(500);
    for (Cli* x : clis) { wt_client_destroy(x->h); x->h = nullptr; delete x; }
    wt_server_stop(srv);
    wt_server_destroy(srv);
    clis.clear();

    /* ── Test 5: half-open cap releases once the session is up ── */
    srv_connects = 0;
    srv = wt_server_create("server.pem", "server.key", "h3", "127.0.0.1", 47312, 64, nullptr, &scb, nullptr);
    wt_server_set_limits(srv, 0, 0, 1, 0, 0, 0, 0, -1);
    if (wt_server_start(srv) != 0) { printf("server2 start failed\n"); return 2; }
    g_port = 47312;
    Cli* f = connect_client("F");
    Cli* g = connect_client("G");   /* while F is still half-open */
    pump(1500);
    CHECK(f->connected == 1, "client F connects under the half-open cap");
    CHECK(g->connected == 0 && g->disconnected == 1, "client G refused while F is half-open");
    /* F's first message completes protocol detection → session → cap released. */
    wt_client_send_stream(f->h, payload, sizeof payload);
    auto end = std::chrono::steady_clock::now() + std::chrono::seconds(10);
    while (srv_connects < 1 && std::chrono::steady_clock::now() < end) pump(10);
    CHECK(srv_connects >= 1, "server established F's session");
    Cli* h = connect_client("H");
    CHECK(wait_until(h->connected, 5000), "client H connects once F left the half-open state");
    for (Cli* x : clis) { if (x->h) wt_client_disconnect(x->h); }
    pump(500);
    for (Cli* x : clis) { wt_client_destroy(x->h); x->h = nullptr; delete x; }
    wt_server_stop(srv);
    wt_server_destroy(srv);
    clis.clear();

    /* ── Tests 6 and 7: datagrams are dropped over budget, kicked only for abuse ──
     * 50 msg/s, burst 100, stream kick after 20 refusals, datagram queue share off. */
    srv_connects = 0; srv_disconnects = 0;
    for (auto& m : srv_msgs) m = 0;
    for (auto& m : srv_dgrams) m = 0;
    srv = wt_server_create("server.pem", "server.key", "h3", "127.0.0.1", 47313, 64, nullptr, &scb, nullptr);
    wt_server_set_limits(srv, 0, 0, -1, 50, 100, 20, 0, -1);
    if (wt_server_start(srv) != 0) { printf("server3 start failed\n"); return 2; }
    g_port = 47313;

    Cli* j = connect_client("J");
    CHECK(wait_until(j->connected, 5000), "client J connects");
    wt_client_send_stream(j->h, payload, sizeof payload);   /* completes protocol detection */
    end = std::chrono::steady_clock::now() + std::chrono::seconds(10);
    while (srv_connects < 1 && std::chrono::steady_clock::now() < end) pump(10);
    pump(200);
    int dg_sent = 0;
    for (int i = 0; i < 600; i++) { if (wt_client_send_datagram(j->h, payload, sizeof payload) == 0) dg_sent++; if (i % 50 == 49) pump(1); }
    pump(1500);
    long dgrams = 0; for (int i = 0; i < 16; i++) dgrams += srv_dgrams[i];
    printf("[J] burst sent %d datagrams, server delivered %ld, disconnects=%d\n", dg_sent, dgrams, srv_disconnects.load());
    CHECK(dgrams < dg_sent, "the datagram burst was trimmed to the budget");
    CHECK(srv_disconnects == 0 && j->disconnected == 0, "a one-off datagram burst does not disconnect");
    long streams_before = 0; for (int i = 0; i < 16; i++) streams_before += srv_msgs[i] - srv_dgrams[i];
    for (int i = 0; i < 20; i++) { wt_client_send_stream(j->h, payload, sizeof payload); pump(30); }
    pump(800);
    long streams_after = 0; for (int i = 0; i < 16; i++) streams_after += srv_msgs[i] - srv_dgrams[i];
    printf("[J] paced 20 stream messages after the burst, delivered %ld\n", streams_after - streams_before);
    CHECK(streams_after - streams_before == 20, "the reliable stream keeps its own budget after a datagram burst");

    /* Sustained abuse: 200 datagrams/s (4x the budget) for up to 16 s. */
    auto abuse_end = std::chrono::steady_clock::now() + std::chrono::seconds(16);
    while (std::chrono::steady_clock::now() < abuse_end && srv_disconnects == 0) {
        for (int k = 0; k < 10; k++) wt_client_send_datagram(j->h, payload, sizeof payload);
        pump(50);
    }
    pump(1000);
    printf("[J] after sustained abuse: disconnects=%d client disconnected=%d\n", srv_disconnects.load(), j->disconnected.load());
    CHECK(srv_disconnects >= 1, "sustained datagram abuse is disconnected");

    for (Cli* x : clis) { if (x->h) wt_client_disconnect(x->h); }
    pump(500);
    for (Cli* x : clis) { wt_client_destroy(x->h); x->h = nullptr; delete x; }
    wt_server_stop(srv);
    wt_server_destroy(srv);
    wt_deinit();
    clis.clear();
    printf("\n%s (%d failures)\n", failures ? "E2E FAILED" : "E2E OK", failures);
    fflush(stdout);   /* a sanitizer exit report must not swallow the verdict */
    return failures ? 1 : 0;
}
