// WebGPU failure diagnostics. Runs before the Unity loader and only adds logging.
//
// Players on Firefox hit "OperationError: Mapping WebGPU buffer failed: Invalid buffer" with nothing before it in the
// console, so there is no way to tell which buffer failed or why. A buffer is invalid when its creation failed
// (out of memory, or over a device limit) or when the device was lost. This records, for every buffer, the size, usage
// and label it was created with, and when a map fails, prints them alongside the device's limits. It also prints every
// uncaptured GPU error and the reason for a device loss. Unity's own handlers keep working: listeners are added
// alongside them, and nothing is caught or changed.
(function () {
  "use strict";
  if (typeof navigator === "undefined" || !navigator.gpu || typeof GPUAdapter === "undefined" ||
      typeof GPUDevice === "undefined" || typeof GPUBuffer === "undefined") {
    return;
  }

  var tag = "[FishGPU]";
  var BIG = 64 * 1024 * 1024;
  var descriptors = new WeakMap();
  var deviceLimits = null;

  function mb(bytes) {
    return (bytes / (1024 * 1024)).toFixed(1) + " MB";
  }

  function describe(buffer) {
    var d = descriptors.get(buffer);
    if (!d) {
      return "unknown buffer";
    }
    return "size " + d.size + " (" + mb(d.size) + "), usage 0x" + (d.usage >>> 0).toString(16) +
      (d.label ? ", label '" + d.label + "'" : "") + ", made " + d.age + " ms after the page started";
  }

  var requestDevice = GPUAdapter.prototype.requestDevice;
  GPUAdapter.prototype.requestDevice = function () {
    var adapter = this;
    return requestDevice.apply(this, arguments).then(function (device) {
      try {
        var l = device.limits;
        deviceLimits = "maxBufferSize " + mb(l.maxBufferSize) +
          ", maxStorageBufferBindingSize " + mb(l.maxStorageBufferBindingSize) +
          ", maxTextureDimension2D " + l.maxTextureDimension2D +
          ", maxTextureArrayLayers " + l.maxTextureArrayLayers;
        var info = adapter.info || {};
        console.log(tag, "device:", (info.vendor || "?") + " " + (info.architecture || "") + " " +
          (info.device || "") + " " + (info.description || ""), "|", deviceLimits);
        device.addEventListener("uncapturederror", function (e) {
          console.error(tag, "uncaptured GPU error:", e.error && e.error.constructor ? e.error.constructor.name : "",
            e.error ? e.error.message : e);
        });
        device.lost.then(function (lost) {
          console.error(tag, "device lost:", lost.reason, lost.message);
        });
      } catch (err) {
        console.warn(tag, "could not attach diagnostics:", err);
      }
      return device;
    });
  };

  var createBuffer = GPUDevice.prototype.createBuffer;
  GPUDevice.prototype.createBuffer = function (desc) {
    var buffer = createBuffer.apply(this, arguments);
    try {
      if (desc) {
        descriptors.set(buffer, { size: desc.size, usage: desc.usage, label: desc.label || "", age: Math.round(performance.now()) });
        if (desc.size >= BIG) {
          console.log(tag, "large buffer:", describe(buffer));
        }
      }
    } catch (err) {
      // Diagnostics only.
    }
    return buffer;
  };

  var mapAsync = GPUBuffer.prototype.mapAsync;
  GPUBuffer.prototype.mapAsync = function (mode, offset, size) {
    var buffer = this;
    return mapAsync.apply(this, arguments).catch(function (err) {
      console.error(tag, "mapAsync failed:", err && err.message, "| buffer:", describe(buffer),
        "| mode", mode, "offset", offset, "size", size, "| device:", deviceLimits || "unknown");
      throw err;
    });
  };
})();
