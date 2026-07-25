/**
 * FishWebTransport.jslib
 *
 * JavaScript bridge for the W3C WebTransport API (browser).
 * Channel mapping: Streams → channel 0 (Reliable), Datagrams → channel 1 (Unreliable)
 *
 * CRITICAL: WebTransport is NOT WebSocket. There is no standard
 * `wt.readyState === 'connected'`. Chrome reports readyState as undefined.
 * We track liveness with session._connected (set when wt.ready resolves).
 *
 * Packaging note (Unity / modern Emscripten):
 * Keep the helper as a `$`-prefixed library dependency and declare
 * `__deps` / `autoAddDeps` so Emscripten includes it and rewrites references.
 */

var LibraryFishWebTransport = {
    /**
     * Session map + stream/datagram pumps. Emscripten exposes this as
     * `FishWebTransport` after stripping the `$` prefix from the key.
     */
    $FishWebTransport: {
        _transports: {},
        _nextIndex: 1,

        /**
         * Compatible dynCall wrapper — prefers wasmTable (Emscripten 3.x+)
         * and falls back to Module.dynCall / dynCall (Emscripten 2.x).
         */
        _dynCall: function(sig, fn, args) {
            if (typeof wasmTable !== 'undefined' && wasmTable.get) {
                var func = wasmTable.get(fn);
                if (!func) {
                    console.error('[FishWT] Bad function pointer: ' + fn);
                    return;
                }
                return func.apply(null, args);
            }
            var dc = (typeof Module !== 'undefined' && Module['dynCall'])
                ? Module['dynCall']
                : dynCall;
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

        /**
         * Session is live for send/receive: ready resolved, not closed, wt exists.
         * Do NOT use wt.readyState — WebTransport has no WebSocket readyState.
         */
        _isLive: function(session) {
            return !!(session && session.wt && session._connected && !session._closed);
        },

        /**
         * Read incoming bidirectional streams, deliver data via onStream callback.
         */
        _readBidiStreams: function(session) {
            function startPump() {
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
                        session._doneRetries = 0;
                        var stream = result.value;
                        var streamReader = stream.readable.getReader();
                        function readStream() {
                            streamReader.read().then(function(sr) {
                                if (session._closed) { try { streamReader.releaseLock(); } catch (_) {} return; }
                                if (sr.done) { streamReader.releaseLock(); return; }
                                var data = new Uint8Array(sr.value);
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
                        session._dgramDoneRetries = 0;
                        var data = new Uint8Array(result.value);
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
    },

    WTConnect__deps: ['$FishWebTransport'],
    WTConnect: function(urlPtr, onOpen, onClose, onStream, onDatagram, onError) {
        var url = UTF8ToString(urlPtr);
        console.log('[FishWT] WTConnect url=' + url);

        if (typeof WebTransport === 'undefined') {
            console.error('[FishWT] WebTransport not supported in this browser');
            FishWebTransport._dynCall('vi', onError, [-1]);
            return -1;
        }

        var session = {
            wt: null,
            _index: -1,
            _connected: false,
            _closed: false,
            _errorFired: false,
            _url: url,
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
            console.error('[FishWT] Create failed url=' + url + ': ' + e.message);
            session._connected = false;
            session._closed = true;
            FishWebTransport._remove(index);
            FishWebTransport._dynCall('vi', onError, [index]);
            return -1;
        }

        session.wt.ready.then(function() {
            if (session._closed) {
                console.warn('[FishWT] ready after teardown index=' + index + ' url=' + url);
                return;
            }
            /* Own liveness flag — do NOT use wt.readyState (undefined on WebTransport). */
            session._connected = true;
            console.log('[FishWT] ready index=' + index + ' url=' + url +
                ' _connected=true (WebTransport has no readyState)');
            FishWebTransport._dynCall('vi', onOpen, [index]);
            FishWebTransport._readBidiStreams(session);
            FishWebTransport._readDatagrams(session);
        }).catch(function(err) {
            var msg = (err && err.message) ? err.message : String(err);
            session._connected = false;
            if (session._closed || (msg && msg.indexOf('close() is called while connecting') >= 0)) {
                console.warn('[FishWT] Ready aborted after client teardown index=' + index +
                    ' url=' + url + ': ' + msg);
            } else {
                console.error('[FishWT] Ready failed index=' + index + ' url=' + url + ': ' + msg);
            }
            if (session._errorFired) return;
            session._errorFired = true;
            session._closed = true;
            FishWebTransport._remove(index);
            FishWebTransport._dynCall('vi', onError, [index]);
        });

        session.wt.closed.then(function() {
            console.log('[FishWT] closed index=' + index + ' url=' + url);
            session._connected = false;
            session._closed = true;
            FishWebTransport._remove(index);
            FishWebTransport._dynCall('vi', onClose, [index]);
        }).catch(function(err) {
            var msg = (err && err.message) ? err.message : String(err);
            session._connected = false;
            session._closed = true;
            if (session._closed || (msg && msg.indexOf('close() is called while connecting') >= 0)) {
                console.warn('[FishWT] closed after client teardown index=' + index +
                    ' url=' + url + ' (ignore if you already hit connect timeout)');
            } else {
                console.error('[FishWT] closed error index=' + index + ' url=' + url + ': ' + msg);
            }
            if (session._errorFired) return;
            session._errorFired = true;
            FishWebTransport._remove(index);
            FishWebTransport._dynCall('vi', onError, [index]);
        });

        return index;
    },

    /**
     * Send data over a reusable persistent bidirectional stream.
     */
    WTSendStream__deps: ['$FishWebTransport'],
    WTSendStream: function(index, dataPtr, length) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) {
            console.error('[FishWT] WTSendStream FAIL no session index=' + index + ' len=' + length);
            return false;
        }
        if (!FishWebTransport._isLive(session)) {
            console.error('[FishWT] WTSendStream FAIL not live _connected=' + !!session._connected +
                ' _closed=' + !!session._closed +
                ' index=' + index + ' len=' + length + ' url=' + (session._url || ''));
            return false;
        }

        var data = HEAPU8.slice(dataPtr, dataPtr + length);
        session._wireSendN = (session._wireSendN || 0) + 1;
        var n = session._wireSendN;
        if (n <= 12 || (n % 50) === 0) {
            console.log('[FishWT] WTSendStream HAND_TO_BROWSER #' + n +
                ' index=' + index + ' len=' + length +
                ' _connected=true url=' + (session._url || '') +
                ' (LoginServer should see app stream next)');
        }

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
            var writer = session._streamWriter;
            writer.write(data).then(function() {
                if (n <= 12) {
                    console.log('[FishWT] WTSendStream write RESOLVED #' + n +
                        ' index=' + index + ' len=' + length);
                }
            }).catch(function(e) {
                console.warn('[FishWT] stream write error #' + n + ': ' + e.message);
                if (session._streamWriter === writer) {
                    try { writer.releaseLock(); } catch (_) {}
                    session._streamWriter = null;
                }
            });
            return true;
        }

        if (session._streamWriterPending) {
            if (!session._sendQueue) session._sendQueue = [];
            session._sendQueue.push(data);
            console.log('[FishWT] WTSendStream queued behind pending stream #' + n +
                ' index=' + index + ' len=' + length + ' qlen=' + session._sendQueue.length);
            return true;
        }

        session._streamWriterPending = true;
        console.log('[FishWT] WTSendStream createBidirectionalStream #' + n +
            ' index=' + index + ' len=' + length + ' url=' + (session._url || ''));
        try {
            session.wt.createBidirectionalStream().then(function(stream) {
                if (!FishWebTransport._isLive(session)) {
                    console.warn('[FishWT] bidi opened after session dead index=' + index);
                    try { stream.writable.getWriter().close(); } catch (_) {}
                    session._streamWriterPending = false;
                    session._sendQueue = [];
                    return;
                }
                console.log('[FishWT] WTSendStream bidi stream OPENED #' + n +
                    ' index=' + index + ' — writing app payload');
                var writer = stream.writable.getWriter();
                session._streamWriter = writer;
                session._streamWriterPending = false;

                var queue = session._sendQueue || [];
                session._sendQueue = [];
                for (var i = 0; i < queue.length; i++) {
                    writer.write(queue[i]).catch(function(e) {
                        console.warn('[FishWT] queued stream write error: ' + e.message);
                    });
                }

                writer.write(data).then(function() {
                    if (n <= 12) {
                        console.log('[FishWT] WTSendStream first-write RESOLVED #' + n +
                            ' index=' + index + ' len=' + length);
                    }
                }).catch(function(e) {
                    console.warn('[FishWT] stream write error #' + n + ': ' + e.message);
                    if (session._streamWriter === writer) {
                        try { writer.releaseLock(); } catch (_) {}
                        session._streamWriter = null;
                    }
                });
            }).catch(function(err) {
                console.error('[FishWT] createBidirectionalStream FAILED #' + n +
                    ': ' + err.message + ' index=' + index);
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

    WTSendDatagram__deps: ['$FishWebTransport'],
    WTSendDatagram: function(index, dataPtr, length) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) return false;
        if (!FishWebTransport._isLive(session)) {
            console.error('[FishWT] WTSendDatagram FAIL not live index=' + index +
                ' _connected=' + !!session._connected + ' _closed=' + !!session._closed);
            return false;
        }

        var data = new Uint8Array(HEAPU8.slice(dataPtr, dataPtr + length));
        try {
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
            var writer = session._dgramWriter;
            writer.write(data).catch(function(e) {
                console.warn('[FishWT] dgram write: ' + e.message);
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
            session._connected = false;
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

    /**
     * Returns true if our session flag says connected (not wt.readyState).
     */
    WTIsConnected__deps: ['$FishWebTransport'],
    WTIsConnected: function(index) {
        var session = FishWebTransport._get(index);
        return FishWebTransport._isLive(session) ? 1 : 0;
    },

    WTSetStreamThreshold__deps: ['$FishWebTransport'],
    WTSetStreamThreshold: function(index, threshold) {
        // Reserved for future stream congestion control.
    }
};

autoAddDeps(LibraryFishWebTransport, '$FishWebTransport');
mergeInto(LibraryManager.library, LibraryFishWebTransport);
