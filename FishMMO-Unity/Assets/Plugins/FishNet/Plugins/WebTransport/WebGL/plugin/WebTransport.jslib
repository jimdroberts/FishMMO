/**
 * FishWebTransport.jslib
 *
 * JavaScript bridge for the W3C WebTransport API (browser).
 * Channel mapping: Streams → channel 0 (Reliable), Datagrams → channel 1 (Unreliable)
 */

var FishWebTransport = {
    _transports: {},
    _nextIndex: 1,

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

    /** Read incoming bidirectional streams, deliver data via onStream callback. */
    _readBidiStreams: function(session) {
        var reader = session.wt.incomingBidirectionalStreams.getReader();
        function pump() {
            reader.read().then(function(result) {
                if (result.done) return;
                var stream = result.value;
                var streamReader = stream.readable.getReader();
                function readStream() {
                    streamReader.read().then(function(sr) {
                        if (sr.done) { streamReader.releaseLock(); return; }
                        var data = new Uint8Array(sr.value);
                        var ptr = _malloc(data.length);
                        HEAPU8.set(data, ptr);
                        Runtime.dynCall('viii', session.onStream, [session._index, ptr, data.length]);
                        _free(ptr);
                        readStream();
                    }).catch(function(e) { console.warn('[FishWT] stream read error: ' + e.message); });
                }
                readStream();
                pump();
            }).catch(function(e) { console.error('[FishWT] bidi stream pump error: ' + e.message); });
        }
        pump();
    },

    _readDatagrams: function(session) {
        var reader = session.wt.datagrams.readable.getReader();
        function pump() {
            reader.read().then(function(result) {
                if (result.done) return;
                var data = new Uint8Array(result.value);
                var ptr = _malloc(data.length);
                HEAPU8.set(data, ptr);
                Runtime.dynCall('viii', session.onDatagram, [session._index, ptr, data.length]);
                _free(ptr);
                pump();
            }).catch(function(e) { console.error('[FishWT] dgram read error: ' + e.message); });
        }
        pump();
    }
};

mergeInto(LibraryManager.library, {

    WTConnect: function(urlPtr, onOpen, onClose, onStream, onDatagram, onError) {
        var url = UTF8ToString(urlPtr);

        if (typeof WebTransport === 'undefined') {
            console.error('[FishWT] WebTransport not supported');
            Runtime.dynCall('vi', onError, [-1]);
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
            Runtime.dynCall('vi', onError, [index]);
            return -1;
        }

        session.wt.ready.then(function() {
            Runtime.dynCall('vi', onOpen, [index]);
            FishWebTransport._readBidiStreams(session);
            FishWebTransport._readDatagrams(session);
        }).catch(function(err) {
            console.error('[FishWT] Ready failed: ' + err.message);
            FishWebTransport._remove(index);
            Runtime.dynCall('vi', onError, [index]);
        });

        session.wt.closed.then(function() {
            FishWebTransport._remove(index);
            Runtime.dynCall('vi', onClose, [index]);
        }).catch(function(err) {
            FishWebTransport._remove(index);
            Runtime.dynCall('vi', onError, [index]);
        });

        return index;
    },

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
            console.warn('[FishWT] Stream congestion (' + session._pendingStreams +
                         ' pending), dropping reliable send');
            return false;
        }

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

        /* Verify session is still connected before writing. */
        if (session.wt.readyState !== 'connected') return false;

        var data = new Uint8Array(HEAPU8.slice(dataPtr, dataPtr + length));
        try {
            /* Cache one writer for the session lifetime to avoid lock contention. */
            if (!session._dgramWriter) {
                session._dgramWriter = session.wt.datagrams.writable.getWriter();
            }
            session._dgramWriter.write(data).catch(function(e) {
                console.warn('[FishWT] dgram write: ' + e.message);
            });
            return true;
        } catch (e) {
            console.error('[FishWT] SendDatagram: ' + e.message);
            return false;
        }
    },

    WTDisconnect: function(index) {
        var session = FishWebTransport._get(index);
        if (session && session.wt) {
            try {
                session.wt.close();
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
