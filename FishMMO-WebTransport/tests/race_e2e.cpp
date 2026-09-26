/* End-to-end check that a connection's teardown on its QUIC worker never
 * frees state an application-thread call is still using.  The server is
 * polled in a tight loop on the main thread — the H3 handshake sweep, the
 * deferred SETTINGS bootstrap and the flood-kick scan all run there, along
 * with broadcast sends and server-initiated disconnects — while clients on a
 * second thread connect and disconnect across every stage of the handshake:
 *   1. churn: disconnects before the QUIC handshake completes, right after it,
 *      and right after the first message; client- and server-initiated
 *   2. first bytes before SETTINGS: with the poll thread lagging, a native
 *      client's first message lands before the server's control stream is up
 *      and is parked; it must still be classified once SETTINGS are out, and
 *      disconnects race that replay
 *   3. the native-protocol rescue: a 1-byte first message puts 0x01 (the H3
 *      HEADERS type) on the wire and stalls the handshake until the poll
 *      thread's rescue at WT_H3_NATIVE_FALLBACK_MS; disconnects are timed
 *      around that moment
 *   4. the rescue attacked on purpose: the same 1-byte trigger, then either a
 *      disconnect or a burst of further messages landing while the rescue is
 *      running on the poll thread.  The rescue's on_connect is made slow (as
 *      a busy application callback would be) so the window is milliseconds,
 *      not microseconds.  Clients that stay connected must have every
 *      message delivered, in full, exactly once.
 * The churn also has clients sending continuously until the server kicks
 * them, so client sends race the client's own connection close, and the
 * server's broadcasts race stream and connection teardown.
 * Afterwards every server-side connect has been closed by a disconnect for
 * the same id, and the server holds no connections.  Build with
 * -fsanitize=address: a lost race is a use-after-free report, not a flake.
 * Certificates: server.pem/server.key in the working directory, CA trusted
 * via SSL_CERT_FILE — see run_race_e2e.sh.
 * WT_RACE_SECONDS (default 12) sets the churn phase's duration and
 * WT_RACE_RESCUE_ROUNDS (default 16) the number of deliberate rescue rounds. */
#include "webtransport_api.h"
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <random>
#include <set>
#include <thread>
#include <vector>

using Clock = std::chrono::steady_clock;
static const uint16_t kPort = 47341;
static WT_SERVER srv;

/* ── Server side: every connect must be closed by a disconnect ─────────
 * on_disconnect also fires for connections torn down before their session
 * was established (they never connected), so an unknown id is fine there.
 * A connect for an id that is still live means a disconnect was lost or
 * delivered before its connect. */
static std::mutex live_mu;
static std::set<unsigned long long> live;
static std::atomic<int> srv_connects{0}, srv_disconnects{0}, order_errors{0};
static std::atomic<long> srv_stream_msgs{0};

/* The poll thread only ever reports a connect from inside the native rescue.
 * When slow_rescue_connect is set that report takes 10 ms — a busy managed
 * callback — so a disconnect or new data reliably lands mid-rescue. */
static std::thread::id poll_thread_id;
static std::atomic<bool> slow_rescue_connect{false};
static std::atomic<int> rescue_connects{0};

static void s_on_connect(void*, wt_connection_id_t id, const char*) {
    {
        std::lock_guard<std::mutex> g(live_mu);
        if (!live.insert(id).second) order_errors++;
        srv_connects++;
    }
    if (std::this_thread::get_id() == poll_thread_id) {
        rescue_connects++;
        if (slow_rescue_connect)
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
}
static void s_on_disconnect(void*, wt_connection_id_t id, int) {
    std::lock_guard<std::mutex> g(live_mu);
    live.erase(id);
    srv_disconnects++;
}
static void s_on_stream(void*, wt_connection_id_t, wt_stream_id_t, const uint8_t*, int32_t) { srv_stream_msgs++; }
static void s_on_dgram(void*, wt_connection_id_t, const uint8_t*, int32_t) {}

/* Poll-thread controls, set by the client thread between phases. */
static std::atomic<bool> stop_polling{false};
static std::atomic<int> poll_gap_ms{0};      /* >0: lag the poll thread (phase 2) */
static std::atomic<bool> kicks_on{false};    /* server-initiated disconnects of live ids */
static std::atomic<long> polls{0}, kicks{0};

/* ── Clients ────────────────────────────────────────────────────────── */
struct Cli {
    WT_CLIENT h = nullptr;
    std::atomic<int> connected{0}, disconnected{0};
    /* When CONNECTED fired, stamped in the callback itself: the driving loop
     * may be busy (staggered creates) and would record it late, putting the
     * scenario's timing off the server's rescue deadline. */
    std::atomic<long long> connected_at{0};
    int scenario = 0;
    int stage = 0;                 /* 0 connecting, 1 connected, 2 wrote, 3 disconnect issued */
    long sent = 0;                 /* stream messages accepted by wt_client_send_stream */
    Clock::time_point t_start, t_connected, t_act, t_last_send;
};
static void c_on_connect(void* ctx) {
    Cli* c = (Cli*)ctx;
    c->connected_at = Clock::now().time_since_epoch().count();
    c->connected = 1;
}
static void c_on_disconnect(void* ctx, int) { ((Cli*)ctx)->disconnected = 1; }
static void c_on_stream(void*, wt_stream_id_t, const uint8_t*, int32_t) {}
static void c_on_dgram(void*, const uint8_t*, int32_t) {}

static Cli* new_client() {
    Cli* c = new Cli();
    wt_client_callbacks_t cb{c_on_connect, c_on_disconnect, c_on_stream, c_on_dgram};
    c->h = wt_client_create(&cb, c);
    c->t_start = Clock::now();
    wt_client_connect(c->h, "localhost", "127.0.0.1", kPort, 1);
    return c;
}
static int ms_since(Clock::time_point t) {
    return (int)std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - t).count();
}

enum {
    SC_DROP_AT_ONCE,      /* disconnect before the QUIC handshake completes */
    SC_DROP_ON_CONNECT,   /* disconnect the moment the client sees CONNECTED */
    SC_WRITE_THEN_DROP,   /* first message, then disconnect 0-3 ms later */
    SC_WRITE_THEN_LINGER, /* first message, then 5-40 ms (server kicks may land first) */
    SC_STALL_THEN_DROP,   /* 1-byte first message (0x01 on the wire), drop near the rescue */
    SC_SEND_UNTIL_KICKED, /* stream + datagram every loop until kicked, or drop after the delay */
    SC_STALL_THEN_STREAM  /* 1-byte first message, then a message every 2 ms from just
                             after the rescue starts (1505-1600 ms after CONNECTED); drop
                             at the delay.  Nothing may arrive earlier: bytes before the
                             rescue would complete the bogus HEADERS frame and let the
                             worker's own native fallback resolve the stall instead. */
};

static const uint8_t kMsg[64] = {0x5A};
static const uint8_t kStall[1] = {0x30};   /* wire: 0x01 0x30 = HEADERS, length 48, no body */

/* Drive a batch of clients through their scenarios on this thread.
 * delay_ms[i] is the scenario's own timing parameter for client i. */
static int run_batch(const std::vector<int>& scenarios, const std::vector<int>& delay_ms,
                     long* sent_total = nullptr, int stagger_ms = 0) {
    std::vector<Cli*> cs;
    for (size_t i = 0; i < scenarios.size(); i++) {
        if (i > 0 && stagger_ms > 0)
            std::this_thread::sleep_for(std::chrono::milliseconds(stagger_ms));
        Cli* c = new_client();
        c->scenario = scenarios[i];
        cs.push_back(c);
        if (c->scenario == SC_DROP_AT_ONCE) {
            if (delay_ms[i] > 0) std::this_thread::sleep_for(std::chrono::microseconds(delay_ms[i] * 100));
            wt_client_disconnect(c->h);
            c->stage = 3;
        }
    }
    auto deadline = Clock::now() + std::chrono::seconds(8);
    for (;;) {
        bool all_done = true;
        for (size_t i = 0; i < cs.size(); i++) {
            Cli* c = cs[i];
            wt_client_poll(c->h, 0);
            if (c->stage == 3) continue;
            all_done = false;
            if (c->stage == 0) {
                if (c->connected) {
                    c->stage = 1;
                    c->t_connected = Clock::time_point(Clock::duration(c->connected_at.load()));
                }
                else if (c->disconnected || ms_since(c->t_start) > 5000) { wt_client_disconnect(c->h); c->stage = 3; }
                continue;
            }
            if (c->disconnected) { c->stage = 3; continue; }
            switch (c->scenario) {
            case SC_DROP_ON_CONNECT:
                wt_client_disconnect(c->h); c->stage = 3; break;
            case SC_SEND_UNTIL_KICKED:
                if (ms_since(c->t_connected) >= delay_ms[i]) { wt_client_disconnect(c->h); c->stage = 3; break; }
                if (wt_client_send_stream(c->h, kMsg, sizeof kMsg) == WT_OK) c->sent++;
                wt_client_send_datagram(c->h, kMsg, 32);
                break;
            case SC_STALL_THEN_STREAM: {
                int t = ms_since(c->t_connected);
                if (c->stage == 1) {
                    if (wt_client_send_stream(c->h, kStall, sizeof kStall) == WT_OK) c->sent++;
                    c->stage = 2; c->t_last_send = Clock::now();
                } else if (t >= delay_ms[i]) {
                    wt_client_disconnect(c->h); c->stage = 3;
                } else if (t >= 1505 && t < 1600 && ms_since(c->t_last_send) >= 2) {
                    if (wt_client_send_stream(c->h, kMsg, sizeof kMsg) == WT_OK) c->sent++;
                    c->t_last_send = Clock::now();
                }
                break;
            }
            case SC_WRITE_THEN_DROP:
            case SC_WRITE_THEN_LINGER:
            case SC_STALL_THEN_DROP:
                if (c->stage == 1) {
                    if (c->scenario == SC_STALL_THEN_DROP) wt_client_send_stream(c->h, kStall, sizeof kStall);
                    else wt_client_send_stream(c->h, kMsg, sizeof kMsg);
                    c->stage = 2; c->t_act = Clock::now();
                } else if (ms_since(c->scenario == SC_STALL_THEN_DROP ? c->t_connected : c->t_act) >= delay_ms[i]) {
                    wt_client_disconnect(c->h); c->stage = 3;
                }
                break;
            default:
                wt_client_disconnect(c->h); c->stage = 3; break;
            }
        }
        if (all_done || Clock::now() > deadline) break;
        std::this_thread::yield();
    }
    for (Cli* c : cs) {
        if (sent_total) *sent_total += c->sent;
        wt_client_destroy(c->h);
        delete c;
    }
    return (int)cs.size();
}

static std::atomic<int> failures{0};   /* both threads report */
#define CHECK(cond, msg) do { if (cond) printf("PASS: %s\n", msg); else { printf("FAIL: %s\n", msg); failures++; } fflush(stdout); } while (0)

static void client_thread_main() {
    std::mt19937 rng(0x5eed);
    const char* env = getenv("WT_RACE_SECONDS");
    int churn_s = env ? atoi(env) : 12;
    if (churn_s <= 0) churn_s = 12;

    /* ── 1. churn ─────────────────────────────────────────────── */
    {
        int clients = 0, rounds = 0;
        kicks_on = true;
        auto end = Clock::now() + std::chrono::seconds(churn_s);
        while (Clock::now() < end) {
            std::vector<int> sc, d;
            for (int i = 0; i < 16; i++) {
                int s = (int)(rng() % 5);
                if (s == 4) s = SC_SEND_UNTIL_KICKED;
                sc.push_back(s);
                d.push_back(s == SC_WRITE_THEN_LINGER ? 5 + (int)(rng() % 36)
                          : s == SC_SEND_UNTIL_KICKED ? 20 + (int)(rng() % 100)
                          : (int)(rng() % 4));
            }
            clients += run_batch(sc, d);
            rounds++;
        }
        kicks_on = false;
        printf("[churn] %d rounds, %d clients; server saw %d connects, %d disconnects, %ld kicks, %ld polls\n",
               rounds, clients, srv_connects.load(), srv_disconnects.load(), kicks.load(), polls.load());
        CHECK(rounds >= 3 && srv_connects.load() > 0, "churn ran and sessions were established");
    }

    /* ── 2. first bytes before SETTINGS ──────────────────────── */
    {
        /* Lagging poll: the control stream opens up to 25 ms after CONNECTED,
         * so a first message sent on CONNECTED is parked until it does. */
        poll_gap_ms = 25;
        int before = srv_connects;
        std::vector<int> sc(12, SC_WRITE_THEN_LINGER), d;
        for (int i = 0; i < 12; i++) d.push_back(200);   /* long enough to be classified */
        run_batch(sc, d);
        int classified = srv_connects - before;
        printf("[parked] %d of 12 clients that wrote before SETTINGS reached a session\n", classified);
        CHECK(classified == 12, "every first message parked before SETTINGS is classified once SETTINGS are out");

        /* Same lag, disconnects racing the replay. */
        for (int r = 0; r < 6; r++) {
            std::vector<int> sc2(12, SC_WRITE_THEN_DROP), d2;
            for (int i = 0; i < 12; i++) d2.push_back((int)(rng() % 30));
            run_batch(sc2, d2);
        }
        poll_gap_ms = 0;
    }

    /* ── 3. disconnects around the native-protocol rescue ────── */
    {
        int before = srv_connects;
        std::vector<int> sc(32, SC_STALL_THEN_DROP), d;
        for (int i = 0; i < 32; i++) d.push_back(1440 + i * 4);   /* 1440..1564 ms after CONNECTED */
        run_batch(sc, d);
        printf("[rescue] %d of 32 stalled clients were rescued before they dropped\n", srv_connects - before);
        std::vector<int> sc2(8, SC_STALL_THEN_DROP), d2(8, 2500);
        before = srv_connects;
        run_batch(sc2, d2);
        CHECK(srv_connects - before == 8, "a stalled native handshake is rescued at the fallback deadline");
    }

    /* ── 4. the rescue attacked on purpose ───────────────────── */
    {
        const char* renv = getenv("WT_RACE_RESCUE_ROUNDS");
        int rounds = renv ? atoi(renv) : 16;
        if (rounds <= 0) rounds = 16;
        slow_rescue_connect = true;
        int rescued_before = rescue_connects;
        int triggers = 0;
        for (int r = 0; r < rounds; r++) {
            /* The rescue starts about 1500 ms after CONNECTED and holds
             * on_connect for 10 ms; clients connect 12 ms apart so each
             * gets its own rescue window on the single poll thread.
             * Alternately: drop inside that window, or stream messages into
             * it and drop part-way through. */
            std::vector<int> sc, d;
            for (int i = 0; i < 10; i++) {
                if (i % 2 == 0) { sc.push_back(SC_STALL_THEN_DROP);   d.push_back(1500 + (int)(rng() % 11)); }
                else            { sc.push_back(SC_STALL_THEN_STREAM); d.push_back(1508 + (int)(rng() % 40)); }
            }
            triggers += run_batch(sc, d, nullptr, 12);
        }
        printf("[rescue-race] %d deliberate triggers in %d rounds; %d rescues ran\n",
               triggers, rounds, rescue_connects - rescued_before);
        CHECK(rescue_connects - rescued_before > 0, "deliberate triggers reached the rescue");

        /* Integrity: the same trigger with a stream of messages across the
         * rescue, staying connected — every message delivered exactly once,
         * whether it arrived before the rescue (buffered) or during/after it. */
        srv_stream_msgs = 0;
        long sent = 0;
        std::vector<int> sc(8, SC_STALL_THEN_STREAM), d(8, 2600);
        run_batch(sc, d, &sent, 12);
        long got = srv_stream_msgs;
        printf("[rescue-integrity] 8 rescued clients sent %ld messages, server delivered %ld\n", sent, got);
        CHECK(sent > 8 && got == sent, "messages sent across the rescue are all delivered, exactly once");
        slow_rescue_connect = false;
    }
}

int main() {
    poll_thread_id = std::this_thread::get_id();   /* main() polls the server */
    if (wt_init() != 0) { printf("wt_init failed\n"); return 2; }
    wt_server_callbacks_t scb{s_on_connect, s_on_disconnect, s_on_stream, s_on_dgram};
    srv = wt_server_create("server.pem", "server.key", "h3", "127.0.0.1", kPort, 256, nullptr, &scb, nullptr);
    if (!srv) { printf("server create failed\n"); return 2; }
    /* No connect interval, per-IP or half-open caps: every client is on
     * 127.0.0.1 and reconnects back to back. */
    wt_server_set_limits(srv, 0, 0, 0, -1, -1, -1, -1, -1);
    if (wt_server_start(srv) != 0) { printf("server start failed\n"); return 2; }

    std::thread clients(client_thread_main);
    std::atomic<bool> clients_done{false};
    std::thread watcher([&] { clients.join(); clients_done = true; });

    std::mt19937 rng(0xbeef);
    uint8_t payload[32]; memset(payload, 0xA5, sizeof payload);
    while (!clients_done) {
        wt_server_poll(srv, 0);
        long n = ++polls;
        if (n % 32 == 0) {
            wt_server_send_datagram(srv, WT_BROADCAST_ALL, payload, sizeof payload);
            wt_server_send_stream(srv, WT_BROADCAST_ALL, payload, sizeof payload);
        }
        if (kicks_on && n % 211 == 0) {
            unsigned long long victim = 0;
            {
                std::lock_guard<std::mutex> g(live_mu);
                if (!live.empty()) {
                    auto it = live.begin();
                    std::advance(it, rng() % live.size());
                    victim = *it;
                }
            }
            if (victim) { wt_server_disconnect(srv, victim); kicks++; }
        }
        int gap = poll_gap_ms;
        if (gap > 0) std::this_thread::sleep_for(std::chrono::milliseconds(gap));
    }
    watcher.join();

    /* Let the last teardowns finish, still polling. */
    auto end = Clock::now() + std::chrono::milliseconds(1500);
    while (Clock::now() < end && wt_server_get_client_count(srv) != 0) {
        wt_server_poll(srv, 0);
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    for (int i = 0; i < 50; i++) { wt_server_poll(srv, 0); std::this_thread::sleep_for(std::chrono::milliseconds(2)); }

    size_t left;
    { std::lock_guard<std::mutex> g(live_mu); left = live.size(); }
    printf("[end] %d connects, %d disconnects, %zu sessions still live, %d order errors, client count %d\n",
           srv_connects.load(), srv_disconnects.load(), left, order_errors.load(), wt_server_get_client_count(srv));
    CHECK(order_errors.load() == 0, "no id connected twice without a disconnect in between");
    CHECK(left == 0, "every established session was closed by a disconnect");
    CHECK(wt_server_get_client_count(srv) == 0, "the server holds no connections once the clients are gone");

    wt_server_stop(srv);
    wt_server_destroy(srv);
    wt_deinit();
    printf("\nRACE-E2E %s (%d failures)\n", failures ? "FAILED" : "PASSED", failures.load());
    fflush(stdout);   /* a sanitizer exit report must not swallow the verdict */
    return failures ? 1 : 0;
}
