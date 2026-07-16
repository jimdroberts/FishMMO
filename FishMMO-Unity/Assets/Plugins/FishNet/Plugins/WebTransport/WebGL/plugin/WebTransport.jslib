/**
 * FishWebTransport.jslib
 *
 * JavaScript bridge for the W3C WebTransport API (browser).
 * Used by WebGL builds to bypass the C++ DLL — the browser's
 * native WebTransport handles QUIC/HTTP3 directly.
 *
 * WebTransport spec: https://www.w3.org/TR/webtransport/
 *
 * Channel mapping:
 *   - Streams  → channel 0 (Reliable)
 *   - Datagrams → channel 1 (Unreliable)
 */

var FishWebTransport = {
    /** Map of index → { wt, streamsReader, dgramsReader } */
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
    }
};

mergeInto(LibraryManager.library, {

    /**
     * Create and connect a WebTransport session.
     *
     * @param {string} url - e.g. "https://game.fishmmo.com/wt/7770"
     * @param {function} onOpen - called with (index) when ready
     * @param {function} onClose - called with (index) on close
     * @param {function} onStream - called with (index, dataPtr, length) for stream data
     * @param {function} onDatagram - called with (index, dataPtr, length) for datagram
     * @param {function} onError - called with (index) on error
     * @return {number} transport index, or -1 on failure
     */
    WTConnect: function(urlPtr, onOpen, onClose, onStream, onDatagram, onError) {
        var url = UTF8ToString(urlPtr);

        // Feature detection
        if (typeof WebTransport === 'undefined') {
            console.error('[FishWT] WebTransport not supported by this browser.');
            Runtime.dynCall('vi', onError, [-1]);
            return -1;
        }

        var session = {
            wt: null,
            onOpen: onOpen,
            onClose: onClose,
            onStream: onStream,
            onDatagram: onDatagram,
            onError: onError,
            streamsReaderDone: false,
            dgramsReaderDone: false,
            dgramsWriter: null
        };

        var index = FishWebTransport._add(session);

        try {
            session.wt = new WebTransport(url);
        } catch (e) {
            console.error('[FishWT] Failed to create WebTransport: ' + e.message);
            FishWebTransport._remove(index);
            Runtime.dynCall('vi', onError, [index]);
            return -1;
        }

        // ── Ready handler ─────────────────────────────────
        session.wt.ready.then(function() {
            console.log('[FishWT] Session ready, index=' + index);
            Runtime.dynCall('vi', onOpen, [index]);

            // Start reading incoming bidirectional streams
            session._readStreams(session.wt.incomingBidirectionalStreams);

            // Start reading incoming datagrams
            session._readDatagrams(session.wt.datagrams);

            // Pre-open the datagram writer for outgoing unreliable data
            session.wt.datagrams.writable.getWriter().closed.then(function() {
                session.dgramsWriter = null;
            });
        }).catch(function(err) {
            console.error('[FishWT] Session failed: ' + err.message);
            FishWebTransport._remove(index);
            Runtime.dynCall('vi', onError, [index]);
        });

        // ── Close handler ─────────────────────────────────
        session.wt.closed.then(function() {
            console.log('[FishWT] Session closed, index=' + index);
            FishWebTransport._remove(index);
            Runtime.dynCall('vi', onClose, [index]);
        });

        return index;
    },

    /**
     * Send data via a new bidirectional stream (reliable — channel 0).
     */
    WTSendStream: function(index, dataPtr, length) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) return false;

        var data = HEAPU8.slice(dataPtr, dataPtr + length);

        try {
            // Create a unidirectional stream and send
            session.wt.createUnidirectionalStream().then(function(stream) {
                var writer = stream.writable.getWriter();
                writer.write(data).then(function() {
                    writer.close();
                });
            }).catch(function(err) {
                console.warn('[FishWT] SendStream failed: ' + err.message);
            });
            return true;
        } catch (e) {
            console.error('[FishWT] SendStream error: ' + e.message);
            return false;
        }
    },

    /**
     * Send data via datagram (unreliable — channel 1).
     */
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
            console.error('[FishWT] SendDatagram error: ' + e.message);
            return false;
        }
    },

    /**
     * Close the WebTransport session.
     */
    WTDisconnect: function(index) {
        var session = FishWebTransport._get(index);
        if (session && session.wt) {
            session.wt.close();
        }
        FishWebTransport._remove(index);
    },

    /**
     * Check if the session is in the 'connected' state.
     */
    WTIsConnected: function(index) {
        var session = FishWebTransport._get(index);
        if (!session || !session.wt) return false;
        return session.wt.readyState === 'connected';
    }
});