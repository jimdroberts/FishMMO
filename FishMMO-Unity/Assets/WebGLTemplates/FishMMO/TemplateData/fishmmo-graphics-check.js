// Checks for a hardware WebGPU adapter before the Unity loader starts, and stops with instructions when there is none.
//
// The client is WebGPU only. Without an adapter the loader does not fail: it downloads everything and then waits forever
// with no error. Measured in Chrome 157 on Linux (Wayland), where Vulkan is off by default: requestAdapter() returned
// null, both with and without featureLevel "compatibility", while chrome://gpu reported "WebGPU: Hardware accelerated"
// over an OpenGL ES backend that is never offered to pages. With Vulkan enabled the same browser returned the discrete
// GPU and reached the login screen.
//
// A page cannot change browser flags or open chrome://, about: or brave:// addresses, so the panel gives the exact
// address to paste, with a copy button, and the steps.
(function () {
  "use strict";

  var ADAPTER_TIMEOUT_MS = 8000;

  // The two switches a Chromium browser on Linux needs together: Vulkan (for WebGPU) and X11 mode (for Vulkan).
  var LINUX_SWITCHES = "--ozone-platform=x11 --enable-features=Vulkan";

  // Per Chromium browser on Linux: its usual command, its menu shortcut's file, and how its launcher takes flags
  // for good (a flags file read by the Arch-based packages, or Opera's environment variable read by /usr/bin/opera).
  var LINUX = {
    Chrome: { command: "google-chrome-stable", desktop: "google-chrome.desktop", file: "~/.config/chrome-flags.conf" },
    Chromium: { command: "chromium", desktop: "chromium.desktop", file: "~/.config/chromium-flags.conf" },
    Brave: { command: "brave", desktop: "brave-browser.desktop", file: "~/.config/brave-flags.conf" },
    Opera: { command: "opera", desktop: "opera.desktop", env: "OPERA_USER_FLAGS" },
    Edge: { command: "microsoft-edge-stable", desktop: "microsoft-edge.desktop" },
    Vivaldi: { command: "vivaldi", desktop: "vivaldi-stable.desktop" }
  };

  function detectBrowser() {
    var ua = navigator.userAgent;
    var platform = /Android/i.test(ua) ? "android"
      : /iPhone|iPad|iPod/i.test(ua) ? "ios"
      : /Windows/i.test(ua) ? "windows"
      : /Mac OS X|Macintosh/i.test(ua) ? "mac"
      : /Linux|X11|CrOS/i.test(ua) ? "linux"
      : "other";

    if (/Firefox\//.test(ua)) return { family: "firefox", name: "Firefox", platform: platform };
    if (/Edg\//.test(ua)) return { family: "chromium", name: "Edge", flags: "edge://flags", settings: "edge://settings/system", platform: platform };
    if (/OPR\//.test(ua)) return { family: "chromium", name: "Opera", flags: "opera://flags", settings: "opera://settings/system", platform: platform };
    if (/Vivaldi\//.test(ua)) return { family: "chromium", name: "Vivaldi", flags: "vivaldi://flags", settings: "vivaldi://settings", platform: platform };
    if (navigator.brave) return { family: "chromium", name: "Brave", flags: "brave://flags", settings: "brave://settings/system", platform: platform };
    if (/Chrome\/|Chromium\//.test(ua)) {
      // Chromium names itself Chrome in its user agent; only its brands tell them apart.
      var brands = navigator.userAgentData && navigator.userAgentData.brands ? navigator.userAgentData.brands.map(function (b) { return b.brand; }) : [];
      var name = brands.indexOf("Chromium") >= 0 && brands.indexOf("Google Chrome") < 0 ? "Chromium" : "Chrome";
      return { family: "chromium", name: name, flags: "chrome://flags", settings: "chrome://settings/system", platform: platform };
    }
    if (/Safari\//.test(ua)) return { family: "safari", name: "Safari", platform: platform };
    return { family: "other", name: "your browser", platform: platform };
  }

  // Resolves { ok, reason, info }. reason: "no-webgpu" (no navigator.gpu), "no-adapter" (requestAdapter gave null or
  // threw), "software" (a CPU fallback adapter), "timeout" (no answer; the game is started anyway).
  function probe() {
    if (!navigator.gpu) return Promise.resolve({ ok: false, reason: "no-webgpu" });

    var request;
    try {
      request = navigator.gpu.requestAdapter({ powerPreference: "high-performance" });
    } catch (e) {
      return Promise.resolve({ ok: false, reason: "no-adapter", error: String(e) });
    }

    var timeout = new Promise(function (resolve) {
      setTimeout(function () { resolve({ ok: true, reason: "timeout" }); }, ADAPTER_TIMEOUT_MS);
    });

    var answer = Promise.resolve(request).then(function (adapter) {
      if (!adapter) return { ok: false, reason: "no-adapter" };
      var info = adapter.info || {};
      var summary = { vendor: info.vendor || "", architecture: info.architecture || "", description: info.description || "" };
      if (info.isFallbackAdapter) return { ok: false, reason: "software", info: summary };
      return { ok: true, info: summary };
    }, function (e) {
      return { ok: false, reason: "no-adapter", error: String(e) };
    });

    return Promise.race([answer, timeout]);
  }

  function el(tag, attrs, children) {
    var node = document.createElement(tag);
    if (attrs) for (var k in attrs) {
      if (k === "text") node.textContent = attrs[k];
      else if (k === "className") node.className = attrs[k];
      else node.setAttribute(k, attrs[k]);
    }
    (children || []).forEach(function (c) { if (c) node.appendChild(typeof c === "string" ? document.createTextNode(c) : c); });
    return node;
  }

  function copyRow(text) {
    var code = el("code", { text: text });
    var button = el("button", { type: "button", text: "Copy" });
    button.onclick = function () {
      function done() { button.textContent = "Copied"; setTimeout(function () { button.textContent = "Copy"; }, 2000); }
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(done, function () { selectText(code); });
      } else {
        selectText(code);
      }
    };
    return el("div", { className: "fgc-copy" }, [code, button]);
  }

  function selectText(node) {
    var range = document.createRange();
    range.selectNodeContents(node);
    var selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
  }

  // Steps for this browser: an array of nodes, each one list item.
  function steps(browser, reason) {
    var list = [];
    if (browser.family === "chromium") {
      if (browser.platform === "linux") {
        /* Command-line switches, not the flags page. Vulkan does not work in the browser's Wayland mode: with
         * enable-vulkan on under Wayland, Chrome 157 logs "'--ozone-platform=wayland' is not compatible with Vulkan"
         * and opens a blank window. Its X11 mode works, and there is no flags-page setting that selects it. */
        var linux = LINUX[browser.name] || { command: "", desktop: "" };
        list.push(el("li", null, ["Close " + browser.name + " completely, then start it once from a terminal with both switches:",
          copyRow((linux.command + " " + LINUX_SWITCHES).trim())]));
        list.push(el("li", null, ["Reload this page. If the game starts, make it permanent with one of the next steps."]));
        if (linux.file) {
          list.push(el("li", null, ["Keep it (Arch-based systems, whose " + browser.name + " launcher reads ", el("code", { text: linux.file }),
            "): run this once, then start " + browser.name + " as usual:",
            copyRow("printf '%s\\n' " + LINUX_SWITCHES + " >> " + linux.file)]));
        } else if (linux.env) {
          list.push(el("li", null, ["Keep it (" + browser.name + "'s launcher reads ", el("code", { text: linux.env }),
            "): run this once, then log out and back in:",
            copyRow("mkdir -p ~/.config/environment.d && echo '" + linux.env + "=\"" + LINUX_SWITCHES + "\"' > ~/.config/environment.d/" + browser.name.toLowerCase() + "-flags.conf")]));
        }
        if (linux.desktop) {
          list.push(el("li", null, [(linux.file || linux.env ? "Or, on any distribution, " : "Keep it: ") +
            "give your own copy of " + browser.name + "'s menu shortcut the switches (the file name can differ between distributions):",
            copyRow("sed -E 's#^(Exec=[^ ]+)#\\1 " + LINUX_SWITCHES + "#' /usr/share/applications/" + linux.desktop +
              " > ~/.local/share/applications/" + linux.desktop)]));
        }
        list.push(el("li", null, ["Turned Vulkan on at " + browser.flags + " and " + browser.name + " now opens a blank window? That is its Wayland mode: " +
          "any of the steps above fixes it, since they start it in X11 mode."]));
        list.push(el("li", null, ["Still not working? Add ", el("code", { text: "--enable-unsafe-webgpu" }), " to the switches as well."]));
      } else {
        list.push(el("li", null, ["Turn on ", el("b", { text: "Use graphics acceleration when available" }), " in " + browser.name + "'s system settings, then relaunch:", copyRow(browser.settings)]));
        list.push(el("li", null, ["Update your graphics driver."]));
        list.push(el("li", null, ["Still not working? Enable ", el("b", { text: "Unsafe WebGPU Support" }), " and relaunch:", copyRow(browser.flags + "/#enable-unsafe-webgpu")]));
      }
    } else if (browser.family === "firefox") {
      list.push(el("li", null, ["Open a new tab, paste this into the address bar and accept the warning:", copyRow("about:config")]));
      list.push(el("li", null, ["Search for this setting and set it to ", el("b", { text: "true" }), ":", copyRow("dom.webgpu.enabled")]));
      list.push(el("li", null, ["Restart Firefox, then reload this page."]));
      if (reason !== "no-webgpu") {
        list.push(el("li", null, ["Still not working? Also set this to ", el("b", { text: "true" }), " and restart:", copyRow("gfx.webgpu.ignore-blocklist")]));
      }
    } else if (browser.family === "safari") {
      list.push(el("li", null, ["Update to the latest Safari: WebGPU is on by default from Safari 26."]));
      list.push(el("li", null, ["Or in Safari Settings, Advanced, tick ", el("b", { text: "Show features for web developers" }), ", then turn on ", el("b", { text: "WebGPU" }), " under Develop, Feature Flags."]));
    } else {
      list.push(el("li", null, ["Use a current version of Chrome, Edge, Firefox or Safari."]));
    }
    return list;
  }

  function injectStyle() {
    var css =
      "#fgc-overlay{position:fixed;inset:0;z-index:1000;display:flex;align-items:center;justify-content:center;padding:16px;" +
      "background:#0b0f14;color:#e6e1d6;font:15px/1.5 -apple-system,'Segoe UI',Roboto,'Noto Sans',Cantarell,'DejaVu Sans',sans-serif;box-sizing:border-box;overflow:auto}" +
      "#fgc-overlay *{box-sizing:border-box}" +
      ".fgc-panel{max-width:620px;width:100%;background:#151b23;border:1px solid #3a4452;border-radius:6px;padding:24px 28px}" +
      ".fgc-panel h1{margin:0 0 8px;font-size:22px;color:#f0c674}" +
      ".fgc-panel p{margin:0 0 12px}" +
      ".fgc-panel ol{margin:0 0 16px;padding-left:22px}" +
      ".fgc-panel li{margin-bottom:10px}" +
      ".fgc-copy{display:flex;gap:8px;align-items:center;margin-top:6px;flex-wrap:wrap}" +
      ".fgc-copy code{flex:1 1 220px;background:#0b0f14;border:1px solid #3a4452;border-radius:4px;padding:6px 8px;font:14px ui-monospace,monospace;word-break:break-all;user-select:all}" +
      "#fgc-overlay button{background:#2b3542;color:#e6e1d6;border:1px solid #4a5666;border-radius:4px;padding:6px 14px;font:inherit;cursor:pointer}" +
      "#fgc-overlay button:hover{background:#364253}" +
      ".fgc-actions{display:flex;gap:10px;flex-wrap:wrap;margin-top:8px}" +
      ".fgc-note{color:#9aa4b1;font-size:13px}";
    document.head.appendChild(el("style", { text: css }));
  }

  function show(result, browser) {
    injectStyle();
    var title, lead;
    if (result.reason === "software") {
      title = "Graphics acceleration is off";
      lead = browser.name + " is drawing with your processor instead of your graphics card, so the game would run at a few frames a second.";
    } else if (result.reason === "no-webgpu") {
      title = "WebGPU is not available";
      lead = "FishMMO draws with WebGPU, and " + browser.name + " does not have it turned on.";
    } else {
      title = browser.platform === "linux" && browser.family === "chromium" ? "Vulkan is off in " + browser.name : "WebGPU is not available";
      lead = browser.platform === "linux" && browser.family === "chromium"
        ? "FishMMO draws with WebGPU, which " + browser.name + " on Linux only offers when Vulkan is enabled. Vulkan in turn " +
          "only works in " + browser.name + "'s X11 mode: on a Wayland desktop, turning it on alone leaves a blank window. " +
          "So it needs both, and they have to be given when " + browser.name + " starts."
        : "FishMMO draws with WebGPU, and " + browser.name + " did not offer it a graphics card.";
    }

    var check = el("button", { type: "button", text: "Check again" });
    check.onclick = function () { location.reload(); };

    var overlay = el("div", { id: "fgc-overlay", role: "alert" }, [
      el("div", { className: "fgc-panel" }, [
        el("h1", { text: title }),
        el("p", { text: lead }),
        el("p", { text: "WebGPU on a graphics card is required: there is no WebGL version of the game." }),
        el("p", { text: "To fix it:" }),
        el("ol", null, steps(browser, result.reason)),
        el("p", { className: "fgc-note", text: "Web pages cannot change browser settings or how the browser starts, so this has to be done by hand." }),
        el("div", { className: "fgc-actions" }, [check])
      ])
    ]);
    document.body.appendChild(overlay);
    // The game's container behind the panel overflows a short window; the panel scrolls on its own.
    document.documentElement.style.overflow = "hidden";
  }

  // Resolves true when the game should start. Called once, before the loader script is added.
  window.fishMMOCheckGraphics = function () {
    var browser = detectBrowser();
    return probe().then(function (result) {
      if (result.ok) {
        if (result.reason === "timeout") {
          console.warn("[FishMMO] WebGPU adapter request did not answer within " + ADAPTER_TIMEOUT_MS + " ms; starting anyway.");
        } else {
          console.info("[FishMMO] WebGPU adapter:", result.info.vendor || "unknown vendor", result.info.architecture || "", result.info.description || "");
        }
        return true;
      }
      console.warn("[FishMMO] Graphics check failed (" + result.reason + ") in " + browser.name + " on " + browser.platform +
        (result.error ? ": " + result.error : "") + ". The game needs a hardware WebGPU adapter" +
        (browser.family === "chromium" && browser.platform === "linux" ? "; start the browser with --ozone-platform=x11 --enable-features=Vulkan." : "."));
      // Unsupported: the loader is never started, and the panel stays until the page is reloaded.
      show(result, browser);
      return false;
    }, function (e) {
      console.warn("[FishMMO] Graphics check threw; starting anyway.", e);
      return true;
    });
  };
})();
