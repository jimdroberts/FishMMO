mergeInto(LibraryManager.library, {
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