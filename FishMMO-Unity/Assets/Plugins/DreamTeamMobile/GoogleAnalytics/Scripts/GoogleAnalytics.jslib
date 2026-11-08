mergeInto(LibraryManager.library, {

  // 'p': both arguments are pointers to UTF-8 strings. The signature is what makes Emscripten hand
  // them over as Numbers in a 64-bit WebAssembly build, where a raw pointer arrives as a BigInt.
  GA4PostEvent__sig: 'vpp',
  GA4PostEvent__deps: ['$UTF8ToString'],
  GA4PostEvent: function (utfurl, utfPostDataString) {
    var url = UTF8ToString(utfurl);
    var postData = UTF8ToString(utfPostDataString);

    fetch(url, {
        method: 'POST',
        mode: 'no-cors',
        body: postData
    })
    .catch(error => console.error('[DTM GA4] Failed to submit GA event:', error));
  },
});