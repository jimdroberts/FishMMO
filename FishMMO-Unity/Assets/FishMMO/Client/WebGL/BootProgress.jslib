mergeInto(LibraryManager.library, {
    /*
        The page's loader keeps showing after the engine has started, until the game's own loading screen exists.

        The game's loading screen lives in the first Addressables scene (ClientPreboot), and that scene's bundles and
        their dependencies (over 160 MB) download before it can appear. Unity's loader bar covers only the engine's own
        download and hides at its end, so on a first visit the page sat black for as long as that download took (a
        minute on a slower line). The template (index.html) keeps its loader up and defines window.fishMMOBootProgress
        and window.fishMMOBootDone; MainBootstrapSystem reports the download through these (WebBootProgress.cs).
    */
    /*
        Every function declares its __sig: in the 64-bit (Wasm64) build a pointer without one arrives as a BigInt, and
        UTF8ToString threw "Cannot mix BigInt and other types" on it, which halted the main loop at boot.
    */
    FishMMOBootProgress__deps: ['$UTF8ToString'],
    FishMMOBootProgress__sig: 'vfp',
    FishMMOBootProgress: function (fraction, text) {
        // Only a display: whatever goes wrong here must never reach the game's main loop.
        try {
            if (typeof window !== 'undefined' && typeof window.fishMMOBootProgress === 'function') {
                window.fishMMOBootProgress(fraction, UTF8ToString(Number(text)));
            }
        } catch (e) {
            console.warn('[FishMMO] boot progress: ' + e);
        }
    },
    FishMMOBootDone__sig: 'v',
    FishMMOBootDone: function () {
        try {
            if (typeof window !== 'undefined' && typeof window.fishMMOBootDone === 'function') {
                window.fishMMOBootDone();
            }
        } catch (e) {
            console.warn('[FishMMO] boot done: ' + e);
        }
    },
});
