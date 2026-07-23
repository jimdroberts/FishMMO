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
                    var stream = result.value;
                    var streamReader = stream.readable.getReader();
                    function readStream() {
                        streamReader.read().then(function(sr) {
                            if (session._closed) { try { streamReader.releaseLock(); } catch (_) {} return; }
                            if (sr.done) { streamReader.releaseLock(); return; }
                            var data = new Uint8Array(sr.value);
                            // NOTE: _malloc failure silently drops data with no retry.
                            // This is acceptable because WebTransport stream data is paced
                            // by the sender — the next read will eventually arrive. However,
                            // under extreme memory pressure this can cause sustained data loss.
                            // A future improvement would be to buffer the dropped chunk and
                            // retry _malloc after a brief delay.
                            var ptr = _malloc(data.length);
                            if (!ptr) {
                                console.warn('[FishWT] malloc failed for stream data (' + data.length + ' bytes), dropping');
                                streamReader.releaseLock();
                                return;
                            }
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
                    // NOTE: _malloc failure silently drops data with no retry.
                    // Same limitation as _readBidiStreams — see comment there.
                    var ptr = _malloc(data.length);
                    if (!ptr) {
                        console.warn('[FishWT] malloc failed for datagram (' + data.length + ' bytes), dropping');
                        pump();
                        return;
                    }
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
            _errorFired: false,
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
            if (session._errorFired) return;
            session._errorFired = true;
            FishWebTransport._remove(index);
            FishWebTransport._dynCall('vi', onError, [index]);
        });

        session.wt.closed.then(function() {
            FishWebTransport._remove(index);
            FishWebTransport._dynCall('vi', onClose, [index]);
        }).catch(function(err) {
            if (session._errorFired) return;
            session._errorFired = true;
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
    WTSendStream: function(index, dataPtr, length) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) return false;
        if (session.wt.readyState !== 'connected') return false;

        var data = HEAPU8.slice(dataPtr, dataPtr + length);

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
            var writer = session._streamWriter;
            writer.write(data).catch(function(e) {
                console.warn('[FishWT] stream write error: ' + e.message);
                if (session._streamWriter === writer) {
                    try { writer.releaseLock(); } catch (_) {}
                    session._streamWriter = null;
                }
            });
            return true;
        }

        /* If a createBidirectionalStream promise is already in-flight
         * (e.g. from a rapid previous send before the promise resolved),
         * queue this data to be flushed once the stream becomes available.
         * This prevents exhausting the browser's stream limit (~100-200)
         * by creating a new bidi stream on every rapid send. */
        if (session._streamWriterPending) {
            if (!session._sendQueue) session._sendQueue = [];
            session._sendQueue.push(data);
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
                writer.write(data).catch(function(e) {
                    console.warn('[FishWT] stream write error: ' + e.message);
                    if (session._streamWriter === writer) {
                        try { writer.releaseLock(); } catch (_) {}
                        session._streamWriter = null;
                    }
                });
            }).catch(function(err) {
                console.warn('[FishWT] createBidirectionalStream failed: ' + err.message);
                session._streamWriterPending = false;
                session._sendQueue = [];
            });
            return true;
        } catch (e) {
            console.error('[FishWT] SendStream create error: ' + e.message);
            session._streamWriterPending = false;
            return false;
        }
    },

    WTSendDatagram: function(index, dataPtr, length) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) return false;
        if (session.wt.readyState !== 'connected') return false;

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
     *  Returns true if the WebTransport session is in the 'connected' state.
     *  C# callers should track connection state locally instead. */
    WTIsConnected: function(index) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) return false;
        return session.wt.readyState === 'connected';
    },

    WTSetStreamThreshold: function(index, threshold) {
        // Reserved for future stream congestion control.
        // Currently not enforced -- streams are created on demand.
    }
});
