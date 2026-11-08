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
    */
    FishMMOInstallPointerLockGuard__sig: 'v',
    FishMMOInstallPointerLockGuard: function () {
        if (typeof Element === 'undefined' || !Element.prototype.requestPointerLock ||
            Element.prototype.requestPointerLock.__fishmmoGuard) {
            return;
        }

        var request = Element.prototype.requestPointerLock;
        var pending = null;

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
            var element = this;
            var result;
            try {
                result = request.call(element, options);
            } catch (e) {
                arm(element, options);
                return undefined;
            }
            if (result && typeof result.then === 'function') {
                return result.then(cancel, function (e) {
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
                cancel();
                return exit.apply(document, arguments);
            };
            document.exitPointerLock = guardedExit;
            if (canvas && Object.prototype.hasOwnProperty.call(canvas, 'exitPointerLock')) {
                canvas.exitPointerLock = guardedExit;
            }
        }
    }
});
