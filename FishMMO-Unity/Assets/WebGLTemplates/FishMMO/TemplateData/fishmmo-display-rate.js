// Measures the display's refresh rate from the browser's animation frames while the page is still light (before the game
// loads and its frames stretch them), and publishes it as window.fishMMODisplayHz. The game's frame cap is "every Nth
// display frame" (FrameLoop.jslib), which needs it; if the cap was applied before this finished, it is applied again.
(function () {
  "use strict";
  var times = [];
  function frame(now) {
    times.push(now);
    if (times.length < 61) {
      window.requestAnimationFrame(frame);
      return;
    }
    var gaps = [];
    for (var i = 1; i < times.length; i++) gaps.push(times[i] - times[i - 1]);
    gaps.sort(function (a, b) { return a - b; });
    var median = gaps[gaps.length >> 1];
    if (median > 0) {
      window.fishMMODisplayHz = Math.round(1000 / median);
      if (typeof window.fishMMOOnDisplayHz === "function") window.fishMMOOnDisplayHz();
    }
  }
  window.requestAnimationFrame(frame);
})();
