/**
 * FishWebTransport.jslib
 *
 * JavaScript bridge for the W3C WebTransport API (browser).
 * Channel mapping: Streams → channel 0 (Reliable), Datagrams → channel 1 (Unreliable)
 *
 * IMPORTANT — Emscripten packaging:
 *   The helper object is defined as $FishWebTransport INSIDE the library map
 *   (the "$" prefix tells Emscripten this is a JS library symbol, not a C
 *   export).  Every exported function declares __deps: ['$FishWebTransport'],
 *   and autoAddDeps provides a belt-and-suspenders guarantee that the helper
 *   is never stripped from the final framework.js build.
 *
 *   The previous pattern of a free-floating `var FishWebTransport = {...}`
 *   outside `LibraryManager.library` caused Emscripten to silently drop the
 *   helper object at link time while keeping the WTConnect/WTSendStream/etc.
 *   call sites — producing `ReferenceError: FishWebTransport is not defined`
 *   at runtime in WebGL builds.
 */

var LibraryFishWebTransport = {

    // ------------------------------------------------------------------
    //  Internal helper (Emscripten library symbol — emitted as
    //  `var FishWebTransport` in the framework JS)
    // ------------------------------------------------------------------
    $FishWebTransport: {
        _transports: {},
        _lastErrors: {},  // error messages keyed by index, persists after _remove
        _nextIndex: 1,

        /** Compatible dynCall wrapper — uses wasmTable when available
         *  (Emscripten 3.x+) and falls back to Module.dynCall / dynCall
         *  (Emscripten 2.x).  This prevents breakage when Unity upgrades
         *  its Emscripten toolchain. */
        _dynCall: (typeof wasmTable !== 'undefined' && wasmTable.get)
            ? function(sig, fn, args) {
                var func = wasmTable.get(fn);
                if (!func) { console.error('[FishWT] Bad function pointer: ' + fn); return; }
                func.apply(null, args);
              }
            : function(sig, fn, args) {
                var dc = Module['dynCall'] || dynCall;
                return dc(sig, fn, args);
              },

        /** True when the session can send/receive.
         *  Deliberately does NOT require wt.readyState === 'connected':
         *  that property is absent on some Chromium builds and can still
         *  read 'connecting' after ready() has resolved. Gating sends on it
         *  made WebGL refuse the very first ClientHandshake
         *  (browser: WIRE SEND FAIL, server: prefill=0) while the desktop
         *  native path sent fine. Once ready() resolves we treat the session
         *  as live until it is explicitly closed or failed. */
        _isLive: function(session) {
            if (!session || !session.wt || session._closed) return false;
            var rs = session.wt.readyState;
            if (rs === 'closed' || rs === 'failed') return false;
            if (session._ready) return true;
            return rs === 'connected';
        },

        /** Open (or reuse) the persistent outgoing bidirectional stream and,
         *  when payload is non-null, write it.
         *
         *  A null payload is the "warm up the stream" call made from
         *  wt.ready — it creates the stream without sending anything.
         *
         *  Returns false only when the session is not live or when
         *  createBidirectionalStream throws synchronously; a true return
         *  means QUEUED, not delivered (the write Promise may still reject). */
        _writeReliable: function(session, payload) {
            if (!FishWebTransport._isLive(session)) return false;

            /* Check cached writer — desiredSize is null when the
             * underlying stream is closed or errored. */
            if (session._streamWriter) {
                try {
                    if (session._streamWriter.desiredSize === null) {
                        try { session._streamWriter.releaseLock(); } catch (_) {}
                        session._streamWriter = null;
                        session._streamWriterPending = false;
                    }
                } catch (_) {
                    session._streamWriter = null;
                    session._streamWriterPending = false;
                }
            }

            if (session._streamWriter) {
                /* Reuse the existing writer — no new stream created. */
                if (payload) {
                    var writer = session._streamWriter;
                    writer.write(payload).catch(function(e) {
                        console.warn('[FishWT] stream write error: ' + e.message);
                        if (session._streamWriter === writer) {
                            try { writer.releaseLock(); } catch (_) {}
                            session._streamWriter = null;
                        }
                    });
                }
                return true;
            }

            /* If a createBidirectionalStream promise is already in-flight
             * (e.g. from a rapid previous send, or from the warm-up call in
             * wt.ready), queue this data to be flushed once the stream becomes
             * available. This prevents exhausting the browser's stream limit
             * (~100-200) by creating a new bidi stream on every rapid send. */
            if (session._streamWriterPending) {
                if (payload) {
                    if (!session._sendQueue) session._sendQueue = [];
                    session._sendQueue.push(payload);
                }
                return true;
            }

            /* No valid cached writer and no pending creation:
             * start creating a new persistent bidirectional stream. */
            session._streamWriterPending = true;
            try {
                session.wt.createBidirectionalStream().then(function(stream) {
                    var writer = stream.writable.getWriter();
                    session._streamWriter = writer;
                    session._streamWriterPending = false;

                    /* Drain this stream's readable half. The server replies on the
                     * stream the client opened, so without this pump every reply
                     * (login result, spawns, RPCs) is silently discarded. */
                    FishWebTransport._pumpStreamReadable(
                        session, stream.readable, 'local-stream');

                    /* Flush any data that was queued while the stream was
                     * being created. */
                    var queue = session._sendQueue || [];
                    session._sendQueue = [];
                    for (var i = 0; i < queue.length; i++) {
                        writer.write(queue[i]).catch(function(e) {
                            console.warn('[FishWT] queued stream write error: ' + e.message);
                        });
                    }

                    /* Write the current data. */
                    if (payload) {
                        writer.write(payload).catch(function(e) {
                            console.warn('[FishWT] stream write error: ' + e.message);
                            if (session._streamWriter === writer) {
                                try { writer.releaseLock(); } catch (_) {}
                                session._streamWriter = null;
                            }
                        });
                    }
                }).catch(function(err) {
                    console.warn('[FishWT] createBidirectionalStream failed: ' + err.message);
                    session._streamWriterPending = false;
                    session._sendQueue = [];
                });
                return true;
            } catch (e) {
                /* Synchronous throw (e.g. InvalidStateError on a session that
                 * died between the _isLive check and this call). Clear the
                 * pending flag — leaving it set would make every later send
                 * queue forever instead of opening a fresh stream. */
                console.error('[FishWT] SendStream create error: ' + e.message);
                session._streamWriterPending = false;
                return false;
            }
        },

        /* ── Reliable-channel message framing ──────────────────────────
         * A WebTransport stream is an ordered BYTE stream: consecutive
         * writer.write() calls may be coalesced into one reader.read()
         * chunk, and one write may be split across several. FishNet needs
         * whole packet bundles, so every message is length-delimited with
         * a QUIC varint (RFC 9000 §16) — the same format the native
         * library uses. Both ends must agree; see WT_MAX_FRAMED_MESSAGE
         * in webtransport_internal.h.
         *
         * The browser strips the WEBTRANSPORT_STREAM header itself, so
         * here the stream payload starts directly with the first length. */
        _MAX_MESSAGE: 65536,

        /** Encode val as a QUIC varint. Returns a Uint8Array of 1/2/4/8 bytes. */
        _varintEncode: function(val) {
            if (val < 64) {
                return new Uint8Array([val]);
            }
            if (val < 16384) {
                return new Uint8Array([0x40 | (val >>> 8), val & 0xFF]);
            }
            if (val < 1073741824) {
                return new Uint8Array([
                    0x80 | ((val >>> 24) & 0x3F),
                    (val >>> 16) & 0xFF,
                    (val >>> 8) & 0xFF,
                    val & 0xFF
                ]);
            }
            /* Lengths this large are rejected by _MAX_MESSAGE before we get
             * here; encode defensively rather than emit a malformed varint. */
            var hi = Math.floor(val / 4294967296);
            var lo = val >>> 0;
            return new Uint8Array([
                0xC0 | ((hi >>> 24) & 0x3F), (hi >>> 16) & 0xFF,
                (hi >>> 8) & 0xFF, hi & 0xFF,
                (lo >>> 24) & 0xFF, (lo >>> 16) & 0xFF,
                (lo >>> 8) & 0xFF, lo & 0xFF
            ]);
        },

        /** Decode a QUIC varint. Returns {value, size} or null if truncated. */
        _varintDecode: function(buf, offset) {
            if (offset >= buf.length) return null;
            var need = 1 << (buf[offset] >> 6);
            if (offset + need > buf.length) return null;
            var v = buf[offset] & 0x3F;
            for (var i = 1; i < need; i++) {
                /* Multiply rather than shift: >>> is 32-bit and an 8-byte
                 * varint would silently wrap. */
                v = v * 256 + buf[offset + i];
            }
            return { value: v, size: need };
        },

        /** Deliver every whole message in stream.buf; retain any partial tail. */
        _drainFramed: function(session, st) {
            var consumed = 0;
            var buf = st.buf;
            for (;;) {
                var hdr = FishWebTransport._varintDecode(buf, consumed);
                if (!hdr) break;                       /* length not fully here */
                var len = hdr.value;
                if (len === 0 || len > FishWebTransport._MAX_MESSAGE) {
                    console.error('[FishWT] invalid framed message length ' +
                                  len + ' — aborting stream');
                    st.broken = true;
                    return;
                }
                if (buf.length - consumed - hdr.size < len) break;  /* partial */
                consumed += hdr.size;

                var msg = buf.subarray(consumed, consumed + len);
                var ptr = _malloc(len);
                if (ptr) {
                    HEAPU8.set(msg, ptr);
                    FishWebTransport._dynCall('viii', session.onStream,
                                              [session._index, ptr, len]);
                    _free(ptr);
                } else {
                    console.warn('[FishWT] malloc failed for ' + len +
                                 ' byte message — dropped');
                }
                consumed += len;
            }
            st.buf = consumed > 0 ? buf.subarray(consumed) : buf;
        },

        /** Append bytes to a per-stream accumulator. */
        _appendChunk: function(st, chunk) {
            if (st.buf.length === 0) {
                /* Copy: the caller's view may alias a reused transfer buffer. */
                st.buf = new Uint8Array(chunk);
                return;
            }
            var merged = new Uint8Array(st.buf.length + chunk.length);
            merged.set(st.buf, 0);
            merged.set(chunk, st.buf.length);
            st.buf = merged;
        },

        _get: function(index) {
            return FishWebTransport._transports[index] || null;
        },
        _remove: function(index) {
            var session = FishWebTransport._transports[index];
            if (session) {
                /* Its traffic stays in the page totals after it is gone. */
                FishWebTransport._foldStats(session);
            }
            delete FishWebTransport._transports[index];
            // Keep _lastErrors for one extra retrieval cycle so
            // WTGetLastErrorMessage can be called after _remove.
            // The next WTGetLastErrorMessage call cleans it up.
        },

        /* ── Traffic statistics (WTGetStats) ───────────────────────────
         * WebTransport.getStats() is asynchronous and per session. Each
         * WTGetStats call returns the answers already received and asks
         * every live session for a fresh one, so what C# sees is at most one
         * call old. Cumulative fields are summed over every session the page
         * has opened: when a session goes away its last answer is folded into
         * _statsCarried, so a server hop never resets the page's totals.
         *
         * Only fields the browser actually filled are reported (a bit per
         * slot in the returned mask). Chrome ships getStats() only behind a
         * flag and without byte counters; C# then estimates the QUIC layer
         * from its own counters and labels it Estimated.
         *
         * Slot order is TransportTrafficMath.BrowserStats in C#. */
        _STATS_FIELDS: ['bytesSent', 'bytesSentOverhead', 'bytesReceived',
                        'packetsSent', 'packetsReceived', 'packetsLost'],
        _STATS_RTT_FIELDS: ['smoothedRtt', 'minRtt', 'rttVariation'],
        _statsCarried: [0, 0, 0, 0, 0, 0],
        _statsCarriedMask: 0,
        _statsApiSeen: false,

        /** A usable counter: a finite, non-negative number. */
        _statNumber: function(v) {
            return typeof v === 'number' && isFinite(v) && v >= 0;
        },

        /** Ask one session for fresh statistics, unless a request is already out. */
        _requestStats: function(session) {
            if (!session || !session.wt || session._statsPending) return;
            if (typeof session.wt.getStats !== 'function') return;
            FishWebTransport._statsApiSeen = true;
            session._statsPending = true;
            var pending;
            try {
                pending = session.wt.getStats();
            } catch (e) {
                session._statsPending = false;
                return;
            }
            Promise.resolve(pending).then(function(stats) {
                session._statsPending = false;
                if (!stats) return;
                if (session._statsFolded) {
                    /* The session went away while this answer was in flight:
                     * add what it counted since the answer that was folded. */
                    FishWebTransport._foldDelta(session, stats);
                    return;
                }
                session._stats = stats;
            }).catch(function() {
                session._statsPending = false;
            });
        },

        /** Move a departing session's last answer into the page totals. */
        _foldStats: function(session) {
            var FW = FishWebTransport;
            var last = session._stats || null;
            var folded = {};
            if (last) {
                for (var i = 0; i < FW._STATS_FIELDS.length; i++) {
                    var v = last[FW._STATS_FIELDS[i]];
                    if (FW._statNumber(v)) {
                        FW._statsCarried[i] += v;
                        FW._statsCarriedMask |= (1 << i);
                        folded[FW._STATS_FIELDS[i]] = v;
                    }
                }
            }
            session._statsFolded = folded;
            session._stats = null;
        },

        /** Add a late answer's growth over what was already folded. */
        _foldDelta: function(session, stats) {
            var FW = FishWebTransport;
            var folded = session._statsFolded;
            for (var i = 0; i < FW._STATS_FIELDS.length; i++) {
                var name = FW._STATS_FIELDS[i];
                var v = stats[name];
                if (!FW._statNumber(v)) continue;
                var before = FW._statNumber(folded[name]) ? folded[name] : 0;
                if (v > before) {
                    FW._statsCarried[i] += v - before;
                    FW._statsCarriedMask |= (1 << i);
                    folded[name] = v;
                }
            }
        },
        _add: function(session) {
            var idx = FishWebTransport._nextIndex++;
            FishWebTransport._transports[idx] = session;
            return idx;
        },

        /** Allocate WASM heap memory with retry. Under extreme memory pressure _malloc can
         *  transiently return 0 (NULL). This helper retries up to maxAttempts times with
         *  delayMs between each attempt, allowing the GC to free memory between retries.
         *  Returns a Promise that resolves with the pointer (or null if all retries fail). */
        _mallocRetry: function(size, maxAttempts, delayMs) {
            return new Promise(function(resolve) {
                function tryMalloc(attempt) {
                    var ptr = _malloc(size);
                    if (ptr) {
                        resolve(ptr);
                    } else if (attempt < maxAttempts) {
                        setTimeout(function() { tryMalloc(attempt + 1); }, delayMs);
                    } else {
                        console.error('[FishWT] _malloc failed for ' + size + ' bytes after ' + maxAttempts + ' attempts');
                        resolve(null);
                    }
                }
                tryMalloc(1);
            });
        },

        /** Drain one bidirectional stream's readable half, delivering each
         *  complete framed message through the onStream callback.
         *
         *  Used for BOTH directions. A bidirectional stream we opened has a
         *  readable half that carries the peer's replies — leaving it undrained
         *  means every reply the server sends on that stream is discarded by the
         *  browser, which is exactly what happened before this was factored out:
         *  the only pump ran over incomingBidirectionalStreams (server-opened
         *  streams), while the server replies on the stream the client opened. */
        _pumpStreamReadable: function(session, readable, label) {
            var streamReader;
            try {
                streamReader = readable.getReader();
            } catch (e) {
                console.warn('[FishWT] ' + label + ' reader create failed: ' + e.message);
                return;
            }
            /* Per-stream reassembly state. Each stream carries its own message
             * sequence, so the accumulator cannot be shared across streams. */
            var st = { buf: new Uint8Array(0), broken: false };
            function readStream() {
                streamReader.read().then(function(sr) {
                    if (session._closed) { try { streamReader.releaseLock(); } catch (_) {} return; }
                    if (sr.done) {
                        if (st.buf.length > 0) {
                            console.warn('[FishWT] ' + label + ' ended with ' +
                                st.buf.length + ' trailing bytes of an ' +
                                'incomplete message — discarding');
                        }
                        try { streamReader.releaseLock(); } catch (_) {}
                        return;
                    }
                    if (st.broken) { try { streamReader.releaseLock(); } catch (_) {} return; }

                    FishWebTransport._appendChunk(st, new Uint8Array(sr.value));
                    if (st.buf.length > FishWebTransport._MAX_MESSAGE * 2) {
                        /* Nothing complete in twice the maximum message size
                         * means the peer is not framing correctly; a byte
                         * stream cannot be resynchronised. */
                        console.error('[FishWT] ' + label + ' buffer overflow without a ' +
                                      'complete message — dropping stream');
                        st.broken = true;
                        try { streamReader.releaseLock(); } catch (_) {}
                        return;
                    }
                    FishWebTransport._drainFramed(session, st);
                    if (st.broken) { try { streamReader.releaseLock(); } catch (_) {} return; }
                    readStream();
                }).catch(function(e) {
                    console.warn('[FishWT] ' + label + ' read error: ' + e.message);
                    try { streamReader.releaseLock(); } catch (_) {}
                });
            }
            readStream();
        },

        /** Read incoming (peer-opened) bidirectional streams.
         *  Wraps the pump in a retry loop: if the reader becomes errored or the
         *  pump fails, it waits 1s and re-creates the reader. This prevents a
         *  single stream error from permanently killing all incoming stream data. */
        _readBidiStreams: function(session) {
            function startPump() {
                /* Guard: don't start a new pump if the session is already closed. */
                if (!FishWebTransport._isLive(session)) return;

                var reader;
                try {
                    reader = session.wt.incomingBidirectionalStreams.getReader();
                } catch (e) {
                    console.warn('[FishWT] bidi reader create failed: ' + e.message + ', retrying in 1s');
                    setTimeout(function() { startPump(); }, 1000);
                    return;
                }

                function pump() {
                    reader.read().then(function(result) {
                        if (session._closed) { try { reader.releaseLock(); } catch (_) {} return; }
                        if (result.done) {
                            /* Streams closed — retry with exponential backoff.
                             * "done" can occur transiently during browser memory
                             * pressure; a hard cut-off after a few retries would
                             * permanently kill the pump. Back off exponentially
                             * (1.5x per retry, capped at 30s, up to 100 retries)
                             * and log a warning on each transient done. */
                            try { reader.releaseLock(); } catch (_) {}
                            session._doneRetries = (session._doneRetries || 0) + 1;
                            if (session._doneRetries <= 100) {
                                var delay = Math.min(1000 * Math.pow(1.5, session._doneRetries - 1), 30000);
                                console.warn('[FishWT] Bidi stream pump got done (' +
                                             session._doneRetries + '/100), retrying in ' +
                                             Math.round(delay) + 'ms');
                                setTimeout(function() { startPump(); }, delay);
                            } else {
                                console.warn('[FishWT] Bidi stream pump giving up after ' +
                                             session._doneRetries + ' consecutive done results');
                            }
                            return;
                        }
                        session._doneRetries = 0;  /* reset on successful read */
                        FishWebTransport._pumpStreamReadable(
                            session, result.value.readable, 'peer-stream');
                        pump();
                    }).catch(function(e) {
                        console.error('[FishWT] bidi stream pump error: ' + e.message);
                        try { reader.releaseLock(); } catch (_) {}
                        /* Restart the pump after a delay so we don't tight-loop on
                         * persistent errors. */
                        setTimeout(function() { startPump(); }, 1000);
                    });
                }
                pump();
            }
            startPump();
        },

        _readDatagrams: function(session) {
            function startPump() {
                if (!FishWebTransport._isLive(session)) return;

                var reader;
                try {
                    reader = session.wt.datagrams.readable.getReader();
                } catch (e) {
                    console.warn('[FishWT] dgram reader create failed: ' + e.message + ', retrying in 1s');
                    setTimeout(function() { startPump(); }, 1000);
                    return;
                }

                function pump() {
                    reader.read().then(function(result) {
                        if (session._closed) { try { reader.releaseLock(); } catch (_) {} return; }
                        if (result.done) {
                            /* Guard against transient done reads (same pattern
                             * as _readBidiStreams above). Exponential backoff
                             * with up to 100 retries. */
                            try { reader.releaseLock(); } catch (_) {}
                            session._dgramDoneRetries = (session._dgramDoneRetries || 0) + 1;
                            if (session._dgramDoneRetries <= 100) {
                                var delay = Math.min(1000 * Math.pow(1.5, session._dgramDoneRetries - 1), 30000);
                                console.warn('[FishWT] Datagram pump got done (' +
                                             session._dgramDoneRetries + '/100), retrying in ' +
                                             Math.round(delay) + 'ms');
                                setTimeout(function() { startPump(); }, delay);
                            } else {
                                console.warn('[FishWT] Datagram pump giving up after ' +
                                             session._dgramDoneRetries + ' consecutive done results');
                            }
                            return;
                        }
                        session._dgramDoneRetries = 0;  /* reset on successful read */
                        var data = new Uint8Array(result.value);
                        // Retry _malloc up to 3 times with 10ms delay between attempts.
                        // Same pattern and rationale as _readBidiStreams.
                        return FishWebTransport._mallocRetry(data.length, 3, 10).then(function(ptr) {
                            if (ptr) {
                                HEAPU8.set(data, ptr);
                                FishWebTransport._dynCall('viii', session.onDatagram, [session._index, ptr, data.length]);
                                _free(ptr);
                            } else {
                                console.warn('[FishWT] malloc failed for datagram (' + data.length + ' bytes) after 3 retries');
                            }
                            pump();
                        });
                    }).catch(function(e) {
                        console.error('[FishWT] dgram read error: ' + e.message);
                        try { reader.releaseLock(); } catch (_) {}
                        setTimeout(function() { startPump(); }, 1000);
                    });
                }
                pump();
            }
            startPump();
        }
    },

    // ------------------------------------------------------------------
    //  Exported functions (callable from C# via [DllImport("__Internal")])
    //  Each declares __deps so Emscripten never strips the helper.
    // ------------------------------------------------------------------

    WTConnect__deps: ['$FishWebTransport'],
    WTConnect: function(urlPtr, certHashesPtr, onOpen, onClose, onStream, onDatagram, onError) {
        var url = UTF8ToString(urlPtr);
        var certHashes = certHashesPtr ? UTF8ToString(certHashesPtr) : '';

        // Create the session object first so it's available for the
        // "WebTransport not supported" check below (though no error is
        // stored on the session — errors go to _lastErrors map).
        var session = {
            wt: null,
            _index: -1,
            _errorFired: false,
            onOpen: onOpen,
            onClose: onClose,
            onStream: onStream,
            onDatagram: onDatagram,
            onError: onError
        };

        if (typeof WebTransport === 'undefined') {
            console.error('[FishWT] WebTransport not supported');
            FishWebTransport._lastErrors[-1] = 'WebTransport not supported';
            FishWebTransport._dynCall('vi', onError, [-1]);
            return -1;
        }

        var index = FishWebTransport._add(session);
        session._index = index;

        /* Optional pinned certificate fingerprints. Without these a browser
         * only accepts a publicly trusted chain, so a self-signed development
         * certificate cannot be used from a WebGL build at all. With them the
         * browser accepts the named certificate — provided it is ECDSA P-256
         * and valid for no more than 14 days, which the browser enforces. */
        var options;
        if (certHashes) {
            var hashes = [];
            var parts = certHashes.split(',');
            for (var i = 0; i < parts.length; i++) {
                var hex = parts[i].trim().replace(/:/g, '');
                if (!hex) continue;
                if (hex.length !== 64 || /[^0-9a-fA-F]/.test(hex)) {
                    console.warn('[FishWT] ignoring malformed certificate hash "' +
                                 parts[i] + '" (want 64 hex chars of SHA-256)');
                    continue;
                }
                var bytes = new Uint8Array(32);
                for (var b = 0; b < 32; b++) {
                    bytes[b] = parseInt(hex.substr(b * 2, 2), 16);
                }
                hashes.push({ algorithm: 'sha-256', value: bytes });
            }
            if (hashes.length > 0) {
                options = { serverCertificateHashes: hashes };
                console.log('[FishWT] pinning ' + hashes.length +
                            ' server certificate hash(es)');
            }
        }

        try {
            session.wt = options ? new WebTransport(url, options)
                                 : new WebTransport(url);
        } catch (e) {
            console.error('[FishWT] Create failed: ' + e.message);
            FishWebTransport._lastErrors[index] = 'Create failed: ' + e.message;
            FishWebTransport._remove(index);
            FishWebTransport._dynCall('vi', onError, [index]);
            return -1;
        }

        session.wt.ready.then(function() {
            session._ready = true;
            /* Open the outgoing bidi stream before telling C# we are Started.
             * Otherwise the first ClientHandshake races createBidirectionalStream
             * (or is refused by a stale readyState check) and the server sees
             * prefill=0 / no handshake. Sends issued before the stream resolves
             * are queued by _writeReliable and flushed in order. */
            FishWebTransport._writeReliable(session, null);
            FishWebTransport._dynCall('vi', onOpen, [index]);
            FishWebTransport._readBidiStreams(session);
            FishWebTransport._readDatagrams(session);
        }).catch(function(err) {
            console.error('[FishWT] Ready failed: ' + err.message);
            if (session._errorFired) return;
            session._errorFired = true;
            FishWebTransport._lastErrors[index] = 'Ready failed: ' + (err.message || 'unknown');
            FishWebTransport._remove(index);
            FishWebTransport._dynCall('vi', onError, [index]);
        });

        session.wt.closed.then(function() {
            FishWebTransport._remove(index);
            FishWebTransport._dynCall('vi', onClose, [index]);
        }).catch(function(err) {
            if (session._errorFired) return;
            session._errorFired = true;
            FishWebTransport._lastErrors[index] = 'Closed: ' + (err.message || 'unknown');
            FishWebTransport._remove(index);
            FishWebTransport._dynCall('vi', onError, [index]);
        });

        return index;
    },

    /** Send data over a reusable persistent bidirectional stream.
     *  Creates one stream on first send and caches its writer for all
     *  subsequent reliable sends.  A new stream is created only when
     *  the existing writer becomes closed or errored
     *  (desiredSize === null).
     *
     *  This eliminates the massive overhead + QUIC stream-limit
     *  exhaustion of opening a new stream per packet (the previous
     *  behaviour), which is critical for game RPCs at 20+ Hz.
     *
     *  The writer-caching pattern mirrors WTSendDatagram below. */
    WTSendStream__deps: ['$FishWebTransport'],
    WTSendStream: function(index, dataPtr, length) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) {
            console.error('[FishWT] SendStream refused: no session for index=' + index);
            return false;
        }
        if (!FishWebTransport._isLive(session)) {
            console.error('[FishWT] SendStream refused: session not live index=' + index +
                          ' readyState=' + session.wt.readyState +
                          ' ready=' + !!session._ready +
                          ' closed=' + !!session._closed);
            return false;
        }

        if (length <= 0 || length > FishWebTransport._MAX_MESSAGE) {
            console.error('[FishWT] SendStream rejected: length ' + length +
                          ' outside 1..' + FishWebTransport._MAX_MESSAGE);
            return false;
        }

        /* Length-delimit the message so the peer can find its boundaries in
         * the byte stream. See _drainFramed for the receiving half. */
        var lenHdr = FishWebTransport._varintEncode(length);
        var data = new Uint8Array(lenHdr.length + length);
        data.set(lenHdr, 0);
        data.set(HEAPU8.subarray(dataPtr, dataPtr + length), lenHdr.length);

        return FishWebTransport._writeReliable(session, data);
    },

    WTSendDatagram__deps: ['$FishWebTransport'],
    WTSendDatagram: function(index, dataPtr, length) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) return false;
        if (!FishWebTransport._isLive(session)) return false;

        var data = new Uint8Array(HEAPU8.slice(dataPtr, dataPtr + length));
        try {
            /* Check for closed/errored writer before reusing.
             * WritableStreamDefaultWriter.closed is a Promise (always truthy),
             * so we cannot test it as a boolean.  Use desiredSize === null
             * instead — it is null when the stream is errored or closed. */
            if (session._dgramWriter) {
                try {
                    if (session._dgramWriter.desiredSize === null) {
                        try { session._dgramWriter.releaseLock(); } catch (_) {}
                        session._dgramWriter = null;
                    }
                } catch (_) {
                    session._dgramWriter = null;
                }
            }
            if (!session._dgramWriter) {
                session._dgramWriter = session.wt.datagrams.writable.getWriter();
            }
            /* Clone the writer reference for this send — if another send
             * nulls session._dgramWriter due to an error, this send's
             * pending write still holds a valid reference. */
            var writer = session._dgramWriter;
            writer.write(data).catch(function(e) {
                console.warn('[FishWT] dgram write: ' + e.message);
                /* Only null the cached writer if it's still THIS writer */
                if (session._dgramWriter === writer) {
                    try { writer.releaseLock(); } catch (_) {}
                    session._dgramWriter = null;
                }
            });
            return true;
        } catch (e) {
            console.error('[FishWT] SendDatagram: ' + e.message);
            try { if (session._dgramWriter) session._dgramWriter.releaseLock(); } catch (_) {}
            session._dgramWriter = null;
            return false;
        }
    },

    WTDisconnect__deps: ['$FishWebTransport'],
    WTDisconnect: function(index) {
        var session = FishWebTransport._get(index);
        if (session) {
            session._closed = true;
            if (session._sendQueue) { session._sendQueue.length = 0; delete session._sendQueue; }
            if (session.wt) {
                try {
                    session.wt.close({closeCode: 0, reason: 'Client disconnect'});
                } catch (e) {
                    console.warn('[FishWT] close error: ' + e.message);
                }
            }
        }
        FishWebTransport._remove(index);
    },

    /** @deprecated Reserved for future use — currently not called from C#.
     *  Returns true if the WebTransport session is usable (see _isLive —
     *  ready() has resolved and the session is not closed or failed).
     *  C# callers should track connection state locally instead. */
    WTIsConnected__deps: ['$FishWebTransport'],
    WTIsConnected: function(index) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) return false;
        return FishWebTransport._isLive(session);
    },

    WTSetStreamThreshold__deps: ['$FishWebTransport'],
    WTSetStreamThreshold: function(index, threshold) {
        // Reserved for future stream congestion control.
        // Currently not enforced -- streams are created on demand.
    },

    /** Retrieve the last error message stored for the given session index.
     *  Reads from the _lastErrors map which persists after session _remove.
     *  Cleans up the stored error entry after reading (one-shot).
     *  Returns a pointer to a UTF-8 string allocated on the WASM heap.
     *  The caller (C#) MUST free the returned pointer via WTFree() after
     *  marshalling the string.
     *  Returns 0 (NULL) if no error is stored for this index. */
    WTGetLastErrorMessage__deps: ['$FishWebTransport'],
    WTGetLastErrorMessage: function(index) {
        var msg = FishWebTransport._lastErrors[index];
        if (!msg) return 0;
        // Clean up after read — one-shot retrieval.
        delete FishWebTransport._lastErrors[index];
        if (typeof msg !== 'string') msg = String(msg);
        // Truncate to 1024 bytes to bound WASM heap allocation.
        if (msg.length > 1024) msg = msg.substring(0, 1021) + '...';
        // UTF-8 encode into the WASM heap.  _malloc / _free are provided
        // by Emscripten and exported to C# via [DllImport("__Internal")].
        var ptr = _malloc(msg.length + 1);
        if (!ptr) return 0;
        stringToUTF8(msg, ptr, msg.length + 1);
        return ptr;
    },

    /** Free a WASM-heap pointer handed to C# (e.g. by WTGetLastErrorMessage).
     *  C# must DllImport this wrapper rather than _free directly: IL2CPP
     *  resolves a [DllImport("__Internal")] against the linked libc symbol
     *  name, which modern Emscripten (Unity 6) emits as "free", so
     *  EntryPoint = "_free" fails the wasm-ld step with
     *  "undefined symbol: _free". Inside a JS library the leading underscore
     *  IS correct — that is the JS-side name of the same allocator used by
     *  _malloc above. */
    WTFree__deps: ['$FishWebTransport'],
    WTFree: function(ptr) {
        if (ptr) _free(ptr);
    },

    /** Write the page's WebTransport statistics into a C# double[]
     *  (valuesPtr, count) and return the mask of slots present:
     *    bit 0..5  bytesSent, bytesSentOverhead, bytesReceived,
     *              packetsSent, packetsReceived, packetsLost
     *              (summed over every session this page has opened)
     *    bit 6..8  smoothedRtt, minRtt, rttVariation (ms, live session)
     *    bit 16    the browser's WebTransport has getStats()
     *    bit 17    a live session has answered at least once
     *  Absent slots are written as NaN. See TransportTrafficMath.BrowserStats. */
    WTGetStats__deps: ['$FishWebTransport'],
    WTGetStats: function(valuesPtr, count) {
        var FW = FishWebTransport;
        var sums = FW._statsCarried.slice(0);
        var mask = FW._statsCarriedMask;
        var live = null;
        for (var key in FW._transports) {
            var session = FW._transports[key];
            if (!session) continue;
            FW._requestStats(session);
            var stats = session._stats;
            if (!stats) continue;
            for (var i = 0; i < FW._STATS_FIELDS.length; i++) {
                var v = stats[FW._STATS_FIELDS[i]];
                if (FW._statNumber(v)) {
                    sums[i] += v;
                    mask |= (1 << i);
                }
            }
            if (FW._isLive(session)) live = stats;
        }
        var out = [sums[0], sums[1], sums[2], sums[3], sums[4], sums[5], NaN, NaN, NaN];
        for (var c = 0; c < FW._STATS_FIELDS.length; c++) {
            if (!(mask & (1 << c))) out[c] = NaN;
        }
        if (live) {
            mask |= (1 << 17);
            for (var r = 0; r < FW._STATS_RTT_FIELDS.length; r++) {
                var rv = live[FW._STATS_RTT_FIELDS[r]];
                if (FW._statNumber(rv)) {
                    out[6 + r] = rv;
                    mask |= (1 << (6 + r));
                }
            }
        }
        if (FW._statsApiSeen) mask |= (1 << 16);
        if (valuesPtr && count > 0) {
            /* DataView: the managed array's data need not be 8-byte aligned
             * for HEAPF64, and HEAPU8.buffer is current even after growth. */
            var view = new DataView(HEAPU8.buffer);
            var n = Math.min(count, out.length);
            for (var j = 0; j < n; j++) {
                view.setFloat64(valuesPtr + j * 8, out[j], true);
            }
        }
        return mask;
    }
};

// Belt-and-suspenders: autoAddDeps ensures $FishWebTransport is never
// stripped, even if a future edit accidentally drops a __deps annotation.
autoAddDeps(LibraryFishWebTransport, '$FishWebTransport');
mergeInto(LibraryManager.library, LibraryFishWebTransport);
