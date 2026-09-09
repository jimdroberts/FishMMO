/* Start-up contract of the native library, independent of any certificate:
 *   1. wt_abi_version() is callable before wt_init() and reports WT_ABI_VERSION
 *   2. after wt_init() the TLS provider is openssl (quictls) — the only
 *      provider that can load the PEM files the servers are configured with
 *   3. a server whose certificate path does not exist fails to start with
 *      WT_ERR_TLS_FAILED (a certificate problem), not WT_ERR_TLS_BACKEND
 *      (a build problem), and no port is bound
 *   4. a server with no certificate at all is refused with WT_ERR_TLS_FAILED —
 *      msquic rejects QUIC_CREDENTIAL_TYPE_NONE for servers on every provider
 *      and this library mints no self-signed certificate
 * Needs no files on disk: run the binary directly, or via run_startup_e2e.sh. */
#include "webtransport_api.h"
#include <cstdio>
#include <cstring>

static void s_on_connect(void*, wt_connection_id_t, const char*) {}
static void s_on_disconnect(void*, wt_connection_id_t, int) {}
static void s_on_stream(void*, wt_connection_id_t, wt_stream_id_t, const uint8_t*, int32_t) {}
static void s_on_dgram(void*, wt_connection_id_t, const uint8_t*, int32_t) {}

static int failures = 0;
#define CHECK(cond, msg) do { if (cond) printf("PASS: %s\n", msg); else { printf("FAIL: %s\n", msg); failures++; } } while (0)

int main() {
    /* 1. ABI version, before wt_init() */
    CHECK(wt_abi_version() == WT_ABI_VERSION, "wt_abi_version() matches WT_ABI_VERSION before wt_init()");
    CHECK(strcmp(wt_tls_provider(), "unknown") == 0, "TLS provider is 'unknown' before wt_init()");

    if (wt_init() != 0) { printf("wt_init failed\n"); return 2; }

    /* 2. provider */
    printf("TLS provider: %s\n", wt_tls_provider());
    CHECK(strcmp(wt_tls_provider(), "openssl") == 0, "linked msquic uses the OpenSSL (quictls) TLS provider");

    wt_server_callbacks_t scb{s_on_connect, s_on_disconnect, s_on_stream, s_on_dgram};

    /* 3. missing certificate file → WT_ERR_TLS_FAILED, port stays free */
    WT_SERVER bad = wt_server_create("does-not-exist.pem", "does-not-exist.key", "h3",
                                     "127.0.0.1", 47321, 8, nullptr, &scb, nullptr);
    if (!bad) { printf("server create failed\n"); return 2; }
    int rc = wt_server_start(bad);
    printf("start with missing cert: %d (%s)\n", rc, wt_error_string(rc));
    CHECK(rc == WT_ERR_TLS_FAILED, "missing certificate file fails with WT_ERR_TLS_FAILED");
    CHECK(rc != WT_ERR_TLS_BACKEND, "missing certificate file is not reported as a backend problem");
    CHECK(wt_server_get_state(bad) == 0, "server stays Stopped after the failed start");
    wt_server_destroy(bad);

    /* 4. no certificate → refused up front as a certificate problem */
    WT_SERVER none = wt_server_create(nullptr, nullptr, "h3", "127.0.0.1", 47321, 8, nullptr, &scb, nullptr);
    if (!none) { printf("server create failed\n"); return 2; }
    rc = wt_server_start(none);
    printf("start without certificate: %d (%s)\n", rc, wt_error_string(rc));
    CHECK(rc == WT_ERR_TLS_FAILED, "server without a certificate is refused with WT_ERR_TLS_FAILED");
    CHECK(wt_server_get_state(none) == 0, "server without a certificate stays Stopped");
    wt_server_destroy(none);

    /* error string for the new code is distinct */
    CHECK(strstr(wt_error_string(WT_ERR_TLS_BACKEND), "Schannel") != nullptr,
          "WT_ERR_TLS_BACKEND names the Schannel build in its error string");

    wt_deinit();
    printf("STARTUP-E2E %s (%d failures)\n", failures ? "FAILED" : "PASSED", failures);
    return failures ? 1 : 0;
}
