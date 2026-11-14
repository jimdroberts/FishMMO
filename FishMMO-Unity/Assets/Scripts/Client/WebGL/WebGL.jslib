mergeInto(LibraryManager.library, {
    /*
        Pushes everything written under Application.persistentDataPath into IndexedDB.

        On WebGL that path is an Emscripten IDBFS mount: writes land in an in-memory
        filesystem and survive a page reload only once they have been persisted. Unity
        persists automatically on file close, but ONLY when the web template passes
        `autoSyncPersistentDataPath: true` to createUnityInstance() — this project ships
        the stock PWA template, which does not. Without this call the settings file is
        written correctly, read back correctly for the rest of the session, and is gone
        the next time the player opens the page: exactly the symptom of a setting that
        does not save.

        queuePersist is the same entry point Unity's own JS_FileSystem_Sync uses. It
        batches requests and refuses to run two syncs at once, so calling it after every
        save is safe and is a no-op when a sync is already in flight — and it composes
        correctly with autoSyncPersistentDataPath if a template ever turns it on.

        FS.syncfs is the fallback for a page that replaced Module.unityFileSystemInit
        with its own mount, where __unityIdbfsMount does not exist.
    */
    FishMMOSyncPersistentData__sig: 'v',
    FishMMOSyncPersistentData: function () {
        try {
            if (typeof IDBFS !== 'undefined' && IDBFS.queuePersist &&
                typeof Module !== 'undefined' && Module.__unityIdbfsMount) {
                IDBFS.queuePersist(Module.__unityIdbfsMount.mount);
                return;
            }

            if (typeof FS !== 'undefined' && FS.syncfs) {
                FS.syncfs(false, function (err) {
                    if (err) {
                        console.warn('FishMMO: persisting settings to IndexedDB failed: ' + err);
                    }
                });
            }
        } catch (e) {
            /* Never propagate. A browser with IndexedDB disabled — private browsing on some
               engines — must lose settings between sessions, not fail the save that is
               keeping them for this one. */
            console.warn('FishMMO: persisting settings to IndexedDB failed: ' + e);
        }
    },

    /*
        Calls a C# callback when the page stops being visible: tab switched away, window
        minimised, and — the case that matters — the tab or browser being closed. A WebGL build
        gets no OnApplicationQuit on close, so anything a save path was holding back on a
        debounce is otherwise lost with the page.

        visibilitychange to 'hidden' fires before pagehide on a close in every current engine and
        is the last moment the page reliably still runs; pagehide is listened to as well for the
        engines and bfcache paths that skip it. Both can fire for one close, so the C# side must
        be idempotent (a flush of nothing is).

        After the callback the persistent-data mount is persisted immediately rather than through
        IDBFS.queuePersist, which defers to a setTimeout(0) that a closing page never reaches.
        The immediate path mirrors Emscripten's own queuePersist state machine (idbPersistState:
        0 idle, a timer id when queued, 'idb' running, 'again' when another persist arrived
        mid-run) so it composes with autoSyncPersistentDataPath rather than racing it. Where that
        state is absent — a different Emscripten — it falls back to queuePersist.

        callback is a wasm function-table index for a static [MonoPInvokeCallback] method taking
        no arguments; makeDynCall resolves it at call time.
    */
    FishMMORegisterPageHidden__sig: 'vp',
    FishMMORegisterPageHidden: function (callback) {
        if (!callback || typeof document === 'undefined') {
            return;
        }

        function persistNow() {
            try {
                if (typeof Module === 'undefined' || !Module.__unityIdbfsMount || typeof IDBFS === 'undefined') {
                    return;
                }
                var mount = Module.__unityIdbfsMount.mount;
                if (!('idbPersistState' in mount) || !IDBFS.syncfs) {
                    if (IDBFS.queuePersist) {
                        IDBFS.queuePersist(mount);
                    }
                    return;
                }

                var state = mount.idbPersistState;
                if (state === 'idb' || state === 'again') {
                    // Already running; make sure what was just written is picked up after it.
                    mount.idbPersistState = 'again';
                    return;
                }
                if (typeof state === 'number' && state !== 0) {
                    clearTimeout(state);
                }

                var start = function () {
                    mount.idbPersistState = 'idb';
                    IDBFS.syncfs(mount, false, function (err) {
                        if (err) {
                            console.warn('FishMMO: persisting to IndexedDB on page hide failed: ' + err);
                        }
                        if (mount.idbPersistState === 'again') {
                            start();
                        } else {
                            mount.idbPersistState = 0;
                        }
                    });
                };
                start();
            } catch (e) {
                console.warn('FishMMO: persisting to IndexedDB on page hide failed: ' + e);
            }
        }

        function onHidden() {
            try {
                {{{ makeDynCall('v', 'callback') }}}();
            } catch (e) {
                console.warn('FishMMO: page-hide callback threw: ' + e);
            }
            persistNow();
        }

        document.addEventListener('visibilitychange', function () {
            if (document.visibilityState === 'hidden') {
                onHidden();
            }
        });
        window.addEventListener('pagehide', onHidden);
    },

    /*
        keyCodesPtr points at the int[] marshalled from C#. Its __sig ('p') makes Emscripten pass it
        as a Number in every build: unchanged below 2 GB, unsigned above it, and converted from the
        BigInt a 64-bit (Wasm64) build hands over. The index is ptr / 4, not ptr >> 2: a shift is
        a signed 32-bit operation and breaks for any address at or above 2 GB.
        The codes are copied into the Set at once, so the view is never held across a heap growth.
    */
    AddHijackKeysListener__sig: 'vpi',
    AddHijackKeysListener: function(keyCodesPtr, keyCodesLength) {
        var start = keyCodesPtr / 4;
        var keySet = new Set(HEAP32.subarray(start, start + keyCodesLength));

        document.addEventListener('keydown', function(event) {
            if (keySet.has(event.keyCode)) {
                event.preventDefault();
            }
        }, true);
    }
});