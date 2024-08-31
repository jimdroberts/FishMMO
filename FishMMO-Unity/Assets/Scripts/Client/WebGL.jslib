mergeInto(LibraryManager.library, {

AddHijackAltKeyListener: function() {
    document.addEventListener('keydown', function(event) {
      if (event.altKey) {
        event.preventDefault();
      }
    }, true);
  },
});