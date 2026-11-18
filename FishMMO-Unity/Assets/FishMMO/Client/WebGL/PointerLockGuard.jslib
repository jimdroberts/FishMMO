mergeInto(LibraryManager.library, {
    /*
        Makes a refused pointer-lock request recoverable instead of an uncaught error.

        Unity's runtime (Emscripten) calls canvas.requestPointerLock() and drops what it
        returns. Chromium returns a promise and rejects it with NotAllowedError when the call
        is not inside a user gesture, which is the case whenever the game locks the cursor
        on its own (entering the world, closing a window) rather than in a click handler.
        The rejection went unhandled ("Uncaught (in promise) NotAllowedError ... user gesture
        is required"), and the cursor stayed free although the game believed it was locked.

        The wrapper handles the rejection and, while the game still wants the lock, retries
        it on the next mouse press on the same element: a press is a user gesture, so the
        retry is allowed. exitPointerLock (the game freeing the cursor) cancels the retry.
        Installed once; a second call does nothing.

        It also asks for raw movement ({ unadjustedMovement: true }): mouse counts without the
        operating system's pointer acceleration, which the look sensitivity is defined on
        (ClientCameraSettings.MouseDegreesPerCount). Chrome grants it on Windows, macOS and
        ChromeOS; where it is refused (NotSupportedError: Chrome on Linux) the lock is taken
        again without it, and not asked for again; browsers that do not know the option (Firefox)
        ignore it and lock as before.

        Finally, it tells the game when the BROWSER takes the lock away. Escape always releases a
        pointer lock (and leaves fullscreen), and no page can cancel that: preventDefault on the
        keydown does nothing, and the browser often does not deliver the key at all. Switching
        tab or window drops the lock as well. Unity's runtime does not report any of it, so the
        game went on believing the cursor was captured. lostCallback (a wasm function-table index
        for a static [MonoPInvokeCallback] method taking no arguments) is called when the lock
        ends while the game still wanted it, i.e. not through exitPointerLock.
    */
    FishMMOInstallPointerLockGuard__sig: 'vp',
    FishMMOInstallPointerLockGuard: function (lostCallback) {
        if (typeof Element === 'undefined' || !Element.prototype.requestPointerLock ||
            Element.prototype.requestPointerLock.__fishmmoGuard) {
            return;
        }

        var request = Element.prototype.requestPointerLock;
        var pending = null;
        // False once a browser has refused raw movement, so it is not asked for again.
        var rawMovement = true;
        // True from a lock request until the game frees the cursor (exitPointerLock).
        var wanted = false;

        function cancel() {
            if (pending) {
                pending.element.removeEventListener('mousedown', pending.retry, true);
                pending = null;
            }
        }

        function arm(element, options) {
            cancel();
            var retry = function () {
                cancel();
                if (document.pointerLockElement !== element) {
                    guarded.call(element, options);
                }
            };
            pending = { element: element, retry: retry };
            element.addEventListener('mousedown', retry, true);
        }

        function guarded(options) {
            wanted = true;
            var element = this;
            var raw = options === undefined && rawMovement;
            var asked = raw ? { unadjustedMovement: true } : options;
            var result;
            try {
                result = request.call(element, asked);
            } catch (e) {
                arm(element, options);
                return undefined;
            }
            if (result && typeof result.then === 'function') {
                return result.then(cancel, function (e) {
                    if (raw && e && e.name === 'NotSupportedError') {
                        // No raw movement here: lock without it (the press that asked is still the gesture).
                        rawMovement = false;
                        return guarded.call(element, options);
                    }
                    if (e && e.name === 'NotAllowedError') {
                        arm(element, options);
                    } else {
                        console.warn('FishMMO: pointer lock failed: ' + e);
                    }
                });
            }
            return result;
        }
        guarded.__fishmmoGuard = true;
        Element.prototype.requestPointerLock = guarded;

        // Emscripten copies both methods onto the canvas as its own properties when the
        // runtime starts, which is before this runs, so the canvas keeps calling the
        // originals unless its copies are replaced as well.
        var canvas = typeof Module !== 'undefined' ? Module['canvas'] : null;
        if (canvas && Object.prototype.hasOwnProperty.call(canvas, 'requestPointerLock')) {
            canvas.requestPointerLock = guarded;
        }

        if (document.exitPointerLock) {
            var exit = document.exitPointerLock;
            var guardedExit = function () {
                wanted = false;
                cancel();
                return exit.apply(document, arguments);
            };
            document.exitPointerLock = guardedExit;
            if (canvas && Object.prototype.hasOwnProperty.call(canvas, 'exitPointerLock')) {
                canvas.exitPointerLock = guardedExit;
            }
        }

        if (lostCallback) {
            document.addEventListener('pointerlockchange', function () {
                if (document.pointerLockElement || !wanted) {
                    return;
                }
                // The browser ended it. The game is told and frees the cursor itself, so nothing
                // is retried; the player's next capture is the game's ordinary request.
                wanted = false;
                cancel();
                try {
                    {{{ makeDynCall('v', 'lostCallback') }}}();
                } catch (e) {
                    console.warn('FishMMO: pointer-lock-lost callback threw: ' + e);
                }
            });
        }
    }
});
