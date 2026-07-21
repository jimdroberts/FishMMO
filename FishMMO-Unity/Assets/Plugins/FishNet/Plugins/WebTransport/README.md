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
