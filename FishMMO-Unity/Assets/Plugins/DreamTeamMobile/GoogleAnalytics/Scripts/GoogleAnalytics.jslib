mergeInto(LibraryManager.library, {

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