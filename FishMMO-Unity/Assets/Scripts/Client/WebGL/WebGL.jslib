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

    AddHijackKeysListener: function(keyCodesPtr, keyCodesLength) {
        var keyCodes = Module.HEAP32.subarray(keyCodesPtr >> 2, (keyCodesPtr >> 2) + keyCodesLength);
        var keySet = new Set(keyCodes);

        document.addEventListener('keydown', function(event) {
            if (keySet.has(event.keyCode)) {
                event.preventDefault();
            }
        }, true);
    }
});