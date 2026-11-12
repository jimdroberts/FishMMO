mergeInto(LibraryManager.library, {
    /*
        The game loop on the browser's animation frames, with the frame-rate cap as "every Nth display frame".

        With Application.targetFrameRate set, Unity's web player schedules each frame with setTimeout (Emscripten's
        timing mode 0), out of step with the display. In Firefox (159, 2026-11-11) that showed as the picture snapping
        back: recorded at 40 fps, ten frames were pixel-identical to the frame two or three before while differing from
        the one just before, so the canvas was presenting textures out of order, and the camera, the character and
        everything else jumped back about 80 ms and forward again. Drawing inside the browser's own frame update (mode 1,
        requestAnimationFrame) leaves nothing to race. Emscripten's animation-frame scheduler skips all but every Nth
        frame when its interval is N, so a cap needs no timer: N = floor(display rate / cap), never below 1, so the
        game never runs slower than the cap (the cap's floor is the tick rate).

        The display rate is measured by the page before the game starts (TemplateData/fishmmo-display-rate.js:
        window.fishMMODisplayHz); until it is known 60 is assumed, and the cap is applied again when it arrives.
        Applied once more on the next animation frame, in case the player sets its own timing after this call.
    */
    /*
        Without animation frames (a background tab, a minimised window, a display asleep) the browser sends none, and a
        loop on them stops outright, network and all; the old timer loop kept going, throttled. So a watchdog notes each
        animation frame, and when none has come for half a second it moves the loop onto a 100 ms timer (the browser
        throttles that further in a background tab, as it did the old loop), and back onto animation frames with the
        first frame that comes again.
    */
    $FishFrameLoop__deps: ['emscripten_set_main_loop_timing'],
    $FishFrameLoop: {
        cap: 0,
        lastFrame: 0,
        onTimer: false,
        watching: false,
        interval: function () {
            var hz = (typeof window !== 'undefined' && window.fishMMODisplayHz) || 60;
            var cap = FishFrameLoop.cap;
            return cap > 0 ? Math.max(1, Math.floor(hz / cap)) : 1;
        },
        apply: function () {
            if (FishFrameLoop.onTimer) {
                _emscripten_set_main_loop_timing(0, 100);
            } else {
                _emscripten_set_main_loop_timing(1, FishFrameLoop.interval());
            }
        },
        watch: function () {
            if (FishFrameLoop.watching || typeof window === 'undefined') {
                return;
            }
            FishFrameLoop.watching = true;
            FishFrameLoop.lastFrame = performance.now();
            var frame = function () {
                FishFrameLoop.lastFrame = performance.now();
                if (FishFrameLoop.onTimer) {
                    FishFrameLoop.onTimer = false;
                    FishFrameLoop.apply();
                }
                window.requestAnimationFrame(frame);
            };
            window.requestAnimationFrame(frame);
            window.setInterval(function () {
                if (!FishFrameLoop.onTimer && performance.now() - FishFrameLoop.lastFrame > 500) {
                    FishFrameLoop.onTimer = true;
                    FishFrameLoop.apply();
                }
            }, 250);
        }
    },

    FishMMOSetFrameCap__deps: ['$FishFrameLoop', 'emscripten_set_main_loop_timing'],
    FishMMOSetFrameCap__sig: 'vi',
    FishMMOSetFrameCap: function (framesPerSecond) {
        FishFrameLoop.cap = framesPerSecond;
        FishFrameLoop.apply();
        FishFrameLoop.watch();
        if (typeof window !== 'undefined') {
            window.requestAnimationFrame(FishFrameLoop.apply);
            // When the page's measurement of the display finishes after this, it calls back.
            window.fishMMOOnDisplayHz = FishFrameLoop.apply;
        }
    }
});
