# WebTransport Plugin

QUIC/HTTP3 transport for FishMMO via FishNet.

## Platform Support

| Platform | Backend | Status |
|----------|---------|--------|
| Linux x86_64 | Native `libfishmmo_webtransport.so` | Shipped |
| Windows x86_64 | Native `fishmmo_webtransport.dll` | Build required |
| macOS x86_64 | Native `libfishmmo_webtransport.dylib` | Build required |
| WebGL | Browser WebTransport API | JSLib bridge |

## Architecture

- **Native**: P/Invoke to `fishmmo_webtransport` (msquic wrapper)
- **WebGL**: JavaScript bridge via `WebTransport.jslib`
- **Channel 0 (Reliable)**: WebTransport bidirectional streams
- **Channel 1 (Unreliable)**: QUIC DATAGRAM frames

Both channels are real. Nothing is remapped: unreliable traffic travels as
datagrams on every platform, browsers included.

## Wire format

A QUIC stream is an ordered byte stream, not a message stream — writes may be
coalesced or split arbitrarily. Every application message on the reliable
channel is therefore length-delimited:

```
Message {
  Length (i),       QUIC variable-length integer (RFC 9000 §16)
  Payload (..),     exactly Length bytes
}
```

On a browser session the stream opens with the WEBTRANSPORT_STREAM header
(type `0x41`, encoded as the two bytes `40 41`, followed by the Session ID);
framed messages begin immediately after it.

Unreliable traffic on a browser session is an HTTP/3 Datagram (RFC 9297 §2.1):
a Quarter Stream ID varint — the CONNECT stream ID divided by four — followed
by the payload. Native raw-QUIC peers exchange bare payloads.

> **Compatibility:** framing is a wire-format change. A client and server must
> both be built from this revision or later; a pre-framing peer cannot
> interoperate with a framing one.

## TLS

The **server** presents a certificate the same way it always has, via
`CertificatePath` / `PrivateKeyPath` in its `.cfg`. Nothing about server TLS
changed, and the server never uses the JavaScript bridge — that exists only in
WebGL *client* builds.

`SetServerCertificateHashes` is a **client** setting despite the name, which
comes from the W3C option it feeds (`serverCertificateHashes` — "hashes *of the*
server's certificate", supplied by whoever connects). It exists because browsers
only open a WebTransport session against a publicly trusted chain, so a WebGL
build cannot reach a server with a self-signed certificate at all. For local
development, pin it on the connecting client:

```csharp
webTransport.SetServerCertificateHashes("A1:B2:…");   // WebGL clients only
```

```bash
openssl x509 -in cert.pem -noout -fingerprint -sha256
```

The pinned certificate must be ECDSA P-256 with a validity window of 14 days or
less — a browser rule. Native clients ignore this and validate against the
platform trust store. Leave it unset in production.

## Browser support

| Browser | WebTransport | Notes |
|---------|--------------|-------|
| Chrome / Edge / Opera | Yes (97+) | Streams and datagrams |
| Firefox | Yes (114+) | Streams and datagrams |
| Safari / iOS WebKit | **No** | Behind a flag in Technology Preview only; needs a fallback transport |
| Internet Explorer | **No** | No HTTP/3, no QUIC, end-of-life; Edge is the successor |

## Production Deployment

- **NGINX L4 UDP proxy** fronts all game servers in production. Clients connect to the NGINX public endpoint, which forwards raw UDP to the correct backend game server on loopback. This allows multiple game server processes to share a single public port and provides a clean layer-4 boundary.
- **TLS termination** is handled at each game server process (not at NGINX). Each game server loads its own certificate and private key, terminating the QUIC/TLS session directly. NGINX operates as a plain UDP load balancer without decrypting the traffic.
- **Configuration** per-server TLS settings (CertificatePath, PrivateKeyPath) are set in the server `.cfg` files (see below).

## Configuration

All transport settings are configured via server `.cfg` files:

```ini
Address=127.0.0.1
Port=7770
MaximumClients=4000
CertificatePath=/etc/fishmmo/certs/fullchain.pem
PrivateKeyPath=/etc/fishmmo/certs/privkey.pem
EnableIPv6=false
IPv6Address=::1
```

## Thread Safety

- Native callbacks arrive on QUIC worker threads
- All data is copied to unmanaged memory on callback threads
- Processing is queued to Unity main thread via `ConcurrentQueue<Action>`
- Send functions MUST be called from the same thread as `poll()`
