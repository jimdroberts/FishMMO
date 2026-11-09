// How long the Unity loader may keep each file it fetches. Passed to createUnityInstance as
// config.cacheControl, and also read by the content preloader (WebContentPreloader.jslib), so the
// two can never disagree about which bundles are safe to trust without asking the server.
//
// The loader calls this as Module.cacheControl(url), so `this` is the loader's Module.
function fishMMOCacheControl(url) {
  // The loader opens its cache once per page, on the first fetch, and immediately evicts down to
  // MaximumCacheSize (1 GiB by default). The game's content is larger than that, so without this
  // every page load would throw away most of the download. It has to be raised here: this runs
  // just before that first fetch, and nothing else gets a chance to run that early. Bundles from
  // old releases are removed by the preloader instead.
  if (this.UnityCache) this.UnityCache.MaximumCacheSize = Number.MAX_SAFE_INTEGER;

  // Name ends in the bundle's content hash: a changed bundle is a different URL, so a cached copy
  // can never be out of date. Serving these with no request at all is what keeps gameplay off the
  // network. Addressables' monoscripts and built-in shader bundles keep a fixed name across
  // builds, which is why this tests for the hash rather than for ".bundle".
  if (/_[0-9a-f]{32}\.bundle(\?|$)/.test(url)) return "immutable";

  // The remainder of Unity's default policy.
  var isDataFile = url == this.dataUrl ||
    (this.primaryDataUrls && this.primaryDataUrls.includes(url)) ||
    (this.secondaryDataUrls && this.secondaryDataUrls.includes(url));
  return isDataFile || /\.bundle/.test(url) ? "must-revalidate" : "no-store";
}
