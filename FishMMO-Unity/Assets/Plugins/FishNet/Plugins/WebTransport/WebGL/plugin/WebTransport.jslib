/**
 * FishWebTransport.jslib
 *
 * JavaScript bridge for the W3C WebTransport API (browser).
 * Channel mapping: Streams → channel 0 (Reliable), Datagrams → channel 1 (Unreliable)
 */

var FishWebTransport = {
    _transports: {},
    _nextIndex: 1,

    /** Compatible dynCall wrapper — uses wasmTable when available
     *  (Emscripten 3.x+) and falls back to FishWebTransport._dynCall (Emscripten 2.x).
     *  This prevents breakage when Unity upgrades its Emscripten toolchain. */
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

    _get: function(index) {
        return FishWebTransport._transports[index] || null;
    },
    _remove: function(index) {
        delete FishWebTransport._transports[index];
    },
    _add: function(session) {
        var idx = FishWebTransport._nextIndex++;
        FishWebTransport._transports[idx] = session;
        return idx;
    },

    /** Read incoming bidirectional streams, deliver data via onStream callback.
     *  Wraps the pump in a retry loop: if the reader becomes errored or the
     *  pump fails, it waits 1s and re-creates the reader. This prevents a
     *  single stream error from permanently killing all incoming stream data. */
    _readBidiStreams: function(session) {
        function startPump() {
            /* Guard: don't start a new pump if the session is already closed. */
            if (!session.wt || session.wt.readyState !== 'connected') return;

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
                    if (result.done) {
                        /* Streams closed — guard against infinite restart loop.
                         * If the ReadableStream stays done across restarts
                         * (e.g. peer closed bidi but kept connection alive),
                         * give up after 3 consecutive immediate-done results
                         * to avoid a 1-second-forever retry cycle. */
                        try { reader.releaseLock(); } catch (_) {}
                        session._doneRetries = (session._doneRetries || 0) + 1;
                        if (session._doneRetries <= 3) {
                            setTimeout(function() { startPump(); }, 1000);
                        } else {
                            console.warn('[FishWT] Bidi stream pump giving up after ' +
                                         session._doneRetries + ' consecutive done results');
                        }
                        return;
                    }
                    session._doneRetries = 0;  /* reset on successful read */
                    var stream = result.value;
                    var streamReader = stream.readable.getReader();
                    function readStream() {
                        streamReader.read().then(function(sr) {
                            if (sr.done) { streamReader.releaseLock(); return; }
                            var data = new Uint8Array(sr.value);
                            var ptr = _malloc(data.length);
                            HEAPU8.set(data, ptr);
                            FishWebTransport._dynCall('viii', session.onStream, [session._index, ptr, data.length]);
                            _free(ptr);
                            readStream();
                        }).catch(function(e) {
                            console.warn('[FishWT] stream read error: ' + e.message);
                            try { streamReader.releaseLock(); } catch (_) {}
                        });
                    }
                    readStream();
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
            if (!session.wt || session.wt.readyState !== 'connected') return;

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
                    if (result.done) {
                        /* Guard against infinite restart loop (same pattern
                         * as _readBidiStreams above). */
                        try { reader.releaseLock(); } catch (_) {}
                        session._dgramDoneRetries = (session._dgramDoneRetries || 0) + 1;
                        if (session._dgramDoneRetries <= 3) {
                            setTimeout(function() { startPump(); }, 1000);
                        } else {
                            console.warn('[FishWT] Datagram pump giving up after ' +
                                         session._dgramDoneRetries + ' consecutive done results');
                        }
                        return;
                    }
                    session._dgramDoneRetries = 0;  /* reset on successful read */
                    var data = new Uint8Array(result.value);
                    var ptr = _malloc(data.length);
                    HEAPU8.set(data, ptr);
                    FishWebTransport._dynCall('viii', session.onDatagram, [session._index, ptr, data.length]);
                    _free(ptr);
                    pump();
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
};

mergeInto(LibraryManager.library, {

    WTConnect: function(urlPtr, onOpen, onClose, onStream, onDatagram, onError) {
        var url = UTF8ToString(urlPtr);

        if (typeof WebTransport === 'undefined') {
            console.error('[FishWT] WebTransport not supported');
            FishWebTransport._dynCall('vi', onError, [-1]);
            return -1;
        }

        var session = {
            wt: null,
            _index: -1,
            onOpen: onOpen,
            onClose: onClose,
            onStream: onStream,
            onDatagram: onDatagram,
            onError: onError
        };

        var index = FishWebTransport._add(session);
        session._index = index;

        try {
            session.wt = new WebTransport(url);
        } catch (e) {
            console.error('[FishWT] Create failed: ' + e.message);
            FishWebTransport._remove(index);
            FishWebTransport._dynCall('vi', onError, [index]);
            return -1;
        }

        session.wt.ready.then(function() {
            FishWebTransport._dynCall('vi', onOpen, [index]);
            FishWebTransport._readBidiStreams(session);
            FishWebTransport._readDatagrams(session);
        }).catch(function(err) {
            console.error('[FishWT] Ready failed: ' + err.message);
            FishWebTransport._remove(index);
            FishWebTransport._dynCall('vi', onError, [index]);
        });

        session.wt.closed.then(function() {
            FishWebTransport._remove(index);
            FishWebTransport._dynCall('vi', onClose, [index]);
        }).catch(function(err) {
            FishWebTransport._remove(index);
            FishWebTransport._dynCall('vi', onError, [index]);
        });

        return index;
    },

    /** Send data over a reliable bidirectional stream.
     *  Returns true if the data was queued for async send (not "sent successfully").
     *  The actual write may still fail asynchronously after the function returns. */
    WTSendStream: function(index, dataPtr, length) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) return false;

        /* Verify session is still connected before queuing async work.
         * createBidirectionalStream will fail if state changed between
         * this check and the async call, and the catch handles that. */
        if (session.wt.readyState !== 'connected') return false;

        /* Track in-flight stream count to avoid exhausting browser limits.
         * Each send creates a new bidirectional stream with FIN; browsers
         * typically cap concurrent streams at ~100. Drop data if we exceed
         * a safe threshold rather than queuing unboundedly. */
        if (!session._pendingStreams) session._pendingStreams = 0;
        if (session._pendingStreams > 80) {
            /* Check for stuck counters: if _pendingStreams has been above
             * threshold for over 10 seconds, reset it. Stuck counters can
             * happen if the browser suspends promise resolution (e.g. tab
             * backgrounding). Resetting allows sends to resume rather than
             * being permanently blocked. */
            var now = Date.now();
            if (!session._pendingStreamsSince) {
                session._pendingStreamsSince = now;
            } else if (now - session._pendingStreamsSince > 10000) {
                console.warn('[FishWT] Stream congestion timeout (' + session._pendingStreams +
                             ' pending for >10s), resetting counter');
                session._pendingStreams = 0;
                session._pendingStreamsSince = now;
            }
            console.warn('[FishWT] Stream congestion (' + session._pendingStreams +
                         ' pending), dropping reliable send');
            return false;
        }
        /* Reset the stuck-since timer when below threshold. */
        session._pendingStreamsSince = undefined;

        var data = HEAPU8.slice(dataPtr, dataPtr + length);
        session._pendingStreams++;
        try {
            session.wt.createBidirectionalStream().then(function(stream) {
                var writer = stream.writable.getWriter();
                writer.write(data).then(function() {
                    writer.close();
                    session._pendingStreams--;
                }).catch(function(e) {
                    console.warn('[FishWT] stream close: ' + e.message);
                    session._pendingStreams--;
                });
            }).catch(function(err) {
                console.warn('[FishWT] SendStream: ' + err.message);
                session._pendingStreams--;
            });
            return true;
        } catch (e) {
            console.error('[FishWT] SendStream: ' + e.message);
            session._pendingStreams--;
            return false;
        }
    },

    WTSendDatagram: function(index, dataPtr, length) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) return false;
        if (session.wt.readyState !== 'connected') return false;

        var data = new Uint8Array(HEAPU8.slice(dataPtr, dataPtr + length));
        try {
            /* Check for closed/errored writer before reusing */
            if (session._dgramWriter) {
                try {
                    if (session._dgramWriter.closed) {
                        session._dgramWriter = null;  /* won't releaseLock on closed writer */
                    }
                } catch (_) {}
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

    WTDisconnect: function(index) {
        var session = FishWebTransport._get(index);
        if (session && session.wt) {
            try {
                session.wt.close({closeCode: 0, reason: 'Client disconnect'});
            } catch (e) {
                console.warn('[FishWT] close error: ' + e.message);
            }
        }
        FishWebTransport._remove(index);
    },

    WTIsConnected: function(index) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) return false;
        return session.wt.readyState === 'connected';
    }
});
