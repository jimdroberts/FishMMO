mergeInto(LibraryManager.library, {
    // Function to add a keydown listener that can handle multiple keys
    AddHijackKeysListener: function(keyCodesPtr, keyCodesLength) {
        // Convert the pointer and length into a JavaScript array
        var keyCodes = Module.HEAP32.subarray(keyCodesPtr >> 2, (keyCodesPtr >> 2) + keyCodesLength);
        var keySet = new Set(keyCodes); // Use a Set to handle unique key codes

        document.addEventListener('keydown', function(event) {
	    // Log the key code to the console for debugging
            console.log("Key pressed: " + event.keyCode);

            if (keySet.has(event.keyCode)) {
                event.preventDefault(); // Prevent default action if key is in the set
                console.log("Key " + event.keyCode + " is in the key set. Preventing default action.");
            } else {
                console.log("Key " + event.keyCode + " is not in the key set.");
            }
        }, true);
    }
});