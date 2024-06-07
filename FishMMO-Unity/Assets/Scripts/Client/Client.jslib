mergeInto(LibraryManager.library, {

  ClientWebGLQuit: function () {
    var confirm_result = confirm("Are you sure you want to quit?");
    if (confirm_result == true) {
      window.location.href = "about:blank";
    }
  },
});