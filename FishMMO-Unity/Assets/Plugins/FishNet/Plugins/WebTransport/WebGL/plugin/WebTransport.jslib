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
                    });
                }
                readStream();
                pump(); // next stream
            });
        }
        pump();
    },

    /** Read incoming datagrams, deliver data via onDatagram callback. */
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
            });
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
        });

        return index;
    },

    WTSendStream: function(index, dataPtr, length) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) return false;

        var data = HEAPU8.slice(dataPtr, dataPtr + length);
        try {
            session.wt.createBidirectionalStream().then(function(stream) {
                var writer = stream.writable.getWriter();
                writer.write(data).then(function() { writer.close(); });
            }).catch(function(err) {
                console.warn('[FishWT] SendStream: ' + err.message);
            });
            return true;
        } catch (e) {
            console.error('[FishWT] SendStream: ' + e.message);
            return false;
        }
    },

    WTSendDatagram: function(index, dataPtr, length) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) return false;

        var data = new Uint8Array(HEAPU8.slice(dataPtr, dataPtr + length));
        try {
            var writer = session.wt.datagrams.writable.getWriter();
            writer.write(data);
            writer.releaseLock();
            return true;
        } catch (e) {
            console.error('[FishWT] SendDatagram: ' + e.message);
            return false;
        }
    },

    WTDisconnect: function(index) {
        var session = FishWebTransport._get(index);
        if (session && session.wt) session.wt.close();
        FishWebTransport._remove(index);
    },

    WTIsConnected: function(index) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) return false;
        return session.wt.readyState === 'connected';
    }
});
