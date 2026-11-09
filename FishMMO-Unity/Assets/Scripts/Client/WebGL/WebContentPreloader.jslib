/*
    Downloads every Addressables bundle into the Unity loader's own browser cache, so the engine
    finds each one already there and gameplay never waits on the network. Driven and polled by
    WebContentPreloader.cs.

    Bundles are written straight from the network to the loader's Cache API store rather than
    through Module.cachedFetch, for two reasons: cachedFetch holds each whole body in JavaScript
    memory (some bundles are hundreds of megabytes), and it neither waits for nor reports the
    cache write, so a failed write would look like a success. The entries written here are the
    same two records cachedFetch writes (the response, and its metadata row).
*/
var LibraryFishMMOContentPreload = {
    $FishMMOContentPreload: {
        STATUS_RUNNING: 1,
        STATUS_COMPLETE: 2,
        STATUS_FAILED: 3,
        STATUS_CANCELLED: 4,

        CONCURRENT_DOWNLOADS: 8,
        MAX_ATTEMPTS: 4,
        RETRY_BASE_MS: 1000,
        // A bundle that delivers no bytes for this long is abandoned and retried.
        IDLE_TIMEOUT_MS: 30000,

        status: 0,
        doneBytes: 0,
        totalBytes: 0,
        sessionBytes: 0,
        error: "",
        run: null,

        // Manifest format, one bundle per line: <size in bytes><TAB><url>
        parseManifest: function (text) {
            var entries = [];
            var lines = text.split("\n");
            for (var i = 0; i < lines.length; i++) {
                var tab = lines[i].indexOf("\t");
                if (tab <= 0) continue;
                entries.push({
                    size: parseInt(lines[i].substring(0, tab), 10) || 0,
                    // The same normalisation cachedFetch applies, so both use one cache key.
                    url: new URL(lines[i].substring(tab + 1), window.location.href).href
                });
            }
            return entries;
        },

        start: function (text) {
            var self = FishMMOContentPreload;
            if (self.run) self.stop(self.run);

            var run = { stopped: false, controllers: new Set() };
            self.run = run;
            self.status = self.STATUS_RUNNING;
            self.doneBytes = 0;
            self.totalBytes = 0;
            self.sessionBytes = 0;
            self.error = "";

            Promise.resolve().then(function () {
                return self.execute(run, self.parseManifest(text));
            }).then(function () {
                if (self.run === run && !run.stopped) self.status = self.STATUS_COMPLETE;
            }).catch(function (err) {
                if (self.run !== run || run.stopped) return;
                self.stop(run);
                self.status = self.STATUS_FAILED;
                self.error = err && err.playerMessage ? err.playerMessage : "A game file could not be downloaded. Check your connection, then press the button to continue.";
                console.error("[FishMMO] Content download failed: " + (err && err.message ? err.message : err));
            });
        },

        cancel: function () {
            var self = FishMMOContentPreload;
            if (!self.run) return;
            self.stop(self.run);
            self.run = null;
            self.status = self.STATUS_CANCELLED;
        },

        stop: function (run) {
            run.stopped = true;
            run.controllers.forEach(function (controller) { controller.abort(); });
            run.controllers.clear();
        },

        // Resolves to the loader's cache, or null when this browser cannot provide one.
        connect: function () {
            var cacheType = Module["UnityCache"];
            if (!cacheType || typeof caches === "undefined" || typeof indexedDB === "undefined") {
                return Promise.resolve(null);
            }
            var store = cacheType["getInstance"]();
            return store["isConnected"].then(function () {
                return store["cache"] ? store : null;
            }, function () {
                return null;
            });
        },

        execute: function (run, entries) {
            var self = FishMMOContentPreload;
            return self.connect().then(function (store) {
                if (!store) {
                    // Nowhere to put it (a private window, or a page not served over https).
                    // The game still runs, fetching what it needs as it needs it.
                    console.warn("[FishMMO] Browser cache unavailable: content will download during play.");
                    return;
                }
                return Promise.all([store["cache"]["keys"](), store["getMetaDataEntries"]()]).then(function (found) {
                    return self.synchronise(run, store, entries, found[0], found[1]["metaDataEntries"]);
                });
            });
        },

        synchronise: function (run, store, entries, cachedRequests, metaData) {
            var self = FishMMOContentPreload;
            var total = 0;
            var current = new Set();
            var i;
            for (i = 0; i < entries.length; i++) {
                total += entries[i].size;
                current.add(entries[i].url);
            }
            self.totalBytes = total;

            if (Module["UnityCache"]["MaximumCacheSize"] < total) {
                console.warn("[FishMMO] The page does not raise the loader's cache limit: most of this download will be evicted on the next visit.");
            }

            var bodies = new Set();
            for (i = 0; i < cachedRequests.length; i++) bodies.add(cachedRequests[i].url);
            var rows = new Set();
            for (i = 0; i < metaData.length; i++) rows.add(metaData[i]["url"]);

            return self.removeStale(store, entries, current, bodies, rows).then(function () {
                var missing = [];
                var needed = 0;
                for (var j = 0; j < entries.length; j++) {
                    var entry = entries[j];
                    // Immutable names are proven by being present; the few others must be asked about.
                    var present = bodies.has(entry.url) && rows.has(entry.url);
                    if (present && self.isImmutable(entry.url)) {
                        self.doneBytes += entry.size;
                    } else {
                        missing.push(entry);
                        if (!present) needed += entry.size;
                    }
                }
                return self.prepareStorage(needed).then(function () {
                    return self.downloadAll(run, store, missing);
                });
            });
        },

        isImmutable: function (url) {
            return Module["cacheControl"](url) === "immutable";
        },

        // Deletes bundles left by earlier releases, before downloading so their space is reusable.
        removeStale: function (store, entries, current, bodies, rows) {
            if (entries.length === 0) return Promise.resolve();
            var folder = entries[0].url.substring(0, entries[0].url.lastIndexOf("/") + 1);
            var metaStoreName = Module["UnityCache"]["RequestMetaDataStore"]["name"];
            var stale = new Set();
            function collect(url) {
                if (url.indexOf(folder) === 0 && /\.bundle(\?|$)/.test(url) && !current.has(url)) stale.add(url);
            }
            bodies.forEach(collect);
            rows.forEach(collect);

            var removals = [];
            stale.forEach(function (url) {
                bodies.delete(url);
                rows.delete(url);
                removals.push(store["cache"]["delete"](url).then(function () {
                    return store["execute"](metaStoreName, "delete", [url]);
                }));
            });
            return Promise.all(removals);
        },

        prepareStorage: function (needed) {
            if (needed <= 0 || !navigator.storage) return Promise.resolve();
            // Best effort: without it the browser may evict the whole download under disk pressure.
            var persisted = navigator.storage.persist ? navigator.storage.persist().catch(function () { return false; }) : Promise.resolve(false);
            return persisted.then(function () {
                return navigator.storage.estimate ? navigator.storage.estimate() : null;
            }).then(function (estimate) {
                if (!estimate || !estimate.quota) return;
                var free = estimate.quota - (estimate.usage || 0);
                if (free < needed) {
                    var gb = function (bytes) { return (bytes / 1073741824).toFixed(1) + " GB"; };
                    var error = new Error("Quota too small: " + needed + " bytes needed, " + free + " free");
                    error.playerMessage = "Not enough browser storage: the game needs " + gb(needed) + " but only " + gb(free) + " is available. Free some disk space, then try again.";
                    throw error;
                }
            });
        },

        downloadAll: function (run, store, missing) {
            var self = FishMMOContentPreload;
            var next = 0;
            function worker() {
                if (run.stopped || next >= missing.length) return Promise.resolve();
                return self.fetchWithRetry(run, store, missing[next++], 0).then(worker);
            }
            var workers = [];
            for (var i = 0; i < self.CONCURRENT_DOWNLOADS; i++) workers.push(worker());
            return Promise.all(workers);
        },

        fetchWithRetry: function (run, store, entry, attempt) {
            var self = FishMMOContentPreload;
            var download = self.isImmutable(entry.url) ? self.streamToCache(run, store, entry) : self.revalidate(entry);
            return download.catch(function (err) {
                if (run.stopped || attempt + 1 >= self.MAX_ATTEMPTS) throw err;
                return new Promise(function (resolve) {
                    setTimeout(resolve, self.RETRY_BASE_MS * Math.pow(2, attempt));
                }).then(function () {
                    if (run.stopped) throw err;
                    return self.fetchWithRetry(run, store, entry, attempt + 1);
                });
            });
        },

        streamToCache: function (run, store, entry) {
            var self = FishMMOContentPreload;
            var controller = new AbortController();
            var received = 0;
            var idleTimer = null;
            function restartIdleTimer() {
                clearTimeout(idleTimer);
                idleTimer = setTimeout(function () { controller.abort(); }, self.IDLE_TIMEOUT_MS);
            }
            function finished() {
                clearTimeout(idleTimer);
                run.controllers.delete(controller);
            }

            run.controllers.add(controller);
            restartIdleTimer();

            // no-store: the Cache API copy is the only one wanted, not a second in the HTTP cache.
            return fetch(entry.url, { cache: "no-store", signal: controller.signal }).then(function (response) {
                if (!response.ok) throw new Error("HTTP " + response.status + " downloading " + entry.url);
                var counter = new TransformStream({
                    transform: function (chunk, output) {
                        received += chunk.length;
                        self.doneBytes += chunk.length;
                        self.sessionBytes += chunk.length;
                        restartIdleTimer();
                        output.enqueue(chunk);
                    }
                });
                // Rejects if the transfer dies part way, and then nothing is stored.
                return store["storeRequest"](entry.url, new Response(response.body.pipeThrough(counter), {
                    status: response.status,
                    statusText: response.statusText,
                    headers: response.headers
                }));
            }).then(function () {
                var now = Date.now();
                return store["updateRequestMetaData"]({
                    "url": entry.url,
                    "accessedAt": now,
                    "updatedAt": now,
                    "version": Module["productVersion"],
                    "size": received
                });
            }).then(function () {
                finished();
                // The catalog's size is what the total was built from: keep the sum exact.
                self.doneBytes += entry.size - received;
            }, function (err) {
                finished();
                self.doneBytes -= received;
                self.sessionBytes -= received;
                throw err;
            });
        },

        // For the few bundles without a hash in their name: the loader's normal conditional fetch.
        revalidate: function (entry) {
            var self = FishMMOContentPreload;
            return Module["cachedFetch"](entry.url, {
                method: "GET",
                companyName: Module["companyName"],
                productName: Module["productName"],
                productVersion: Module["productVersion"],
                control: "must-revalidate",
                onProgress: function () {}
            }).then(function (response) {
                if (!response.ok) throw new Error("HTTP " + response.status + " downloading " + entry.url);
                self.doneBytes += entry.size;
            });
        }
    },

    FishMMOContentPreloadStart__deps: ['$UTF8ToString'],
    FishMMOContentPreloadStart__sig: 'vp',
    FishMMOContentPreloadStart: function (manifest) {
        FishMMOContentPreload.start(UTF8ToString(manifest));
    },

    FishMMOContentPreloadCancel__sig: 'v',
    FishMMOContentPreloadCancel: function () {
        FishMMOContentPreload.cancel();
    },

    FishMMOContentPreloadStatus__sig: 'i',
    FishMMOContentPreloadStatus: function () {
        return FishMMOContentPreload.status;
    },

    FishMMOContentPreloadDoneBytes__sig: 'd',
    FishMMOContentPreloadDoneBytes: function () {
        return FishMMOContentPreload.doneBytes;
    },

    FishMMOContentPreloadTotalBytes__sig: 'd',
    FishMMOContentPreloadTotalBytes: function () {
        return FishMMOContentPreload.totalBytes;
    },

    FishMMOContentPreloadSessionBytes__sig: 'd',
    FishMMOContentPreloadSessionBytes: function () {
        return FishMMOContentPreload.sessionBytes;
    },

    // The string is malloc'd; IL2CPP frees a returned string itself. 'p' return: a pointer under Wasm64.
    FishMMOContentPreloadError__deps: ['$lengthBytesUTF8', '$stringToUTF8', 'malloc'],
    FishMMOContentPreloadError__sig: 'p',
    FishMMOContentPreloadError: function () {
        var text = FishMMOContentPreload.error;
        var size = lengthBytesUTF8(text) + 1;
        var ptr = _malloc(size);
        stringToUTF8(text, ptr, size);
        return ptr;
    }
};

autoAddDeps(LibraryFishMMOContentPreload, '$FishMMOContentPreload');
mergeInto(LibraryManager.library, LibraryFishMMOContentPreload);
