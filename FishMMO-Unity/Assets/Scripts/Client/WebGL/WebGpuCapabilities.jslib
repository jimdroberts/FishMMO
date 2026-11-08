mergeInto(LibraryManager.library, {
    FishMMOWebGpuHasIndirectFirstInstance__sig: 'i',
    FishMMOWebGpuHasIndirectFirstInstance: function () {
        // Adapter support is insufficient: the feature must be enabled on the
        // device Unity created. Its WebGPU setup publishes that device here.
        var api = Module["WebGPU"];
        var device = api && api["device"];
        return device && device.features.has("indirect-first-instance") ? 1 : 0;
    },

    // One line describing Unity's actual WebGPU device: its enabled features (texture-compression-bc decides whether
    // DXT/BC textures stay compressed on the GPU or are expanded) and the limits that bound GPU memory use. The string
    // is malloc'd; IL2CPP frees a returned string itself. 'p' return: a pointer under Wasm64.
    FishMMOWebGpuDescribe__deps: ['$lengthBytesUTF8', '$stringToUTF8', 'malloc'],
    FishMMOWebGpuDescribe__sig: 'p',
    FishMMOWebGpuDescribe: function () {
        var api = Module["WebGPU"];
        var device = api && api["device"];
        var text;
        if (!device) {
            text = "no WebGPU device";
        } else {
            var features = [];
            device.features.forEach(function (f) { features.push(f); });
            features.sort();
            var l = device.limits;
            text = "features [" + features.join(", ") + "]; limits maxTextureDimension2D " + l.maxTextureDimension2D
                + ", maxTextureArrayLayers " + l.maxTextureArrayLayers + ", maxBufferSize " + l.maxBufferSize
                + ", maxStorageBufferBindingSize " + l.maxStorageBufferBindingSize;
            var info = device.adapterInfo;
            if (info) text += "; adapter " + [info.vendor, info.architecture, info.device, info.description].filter(Boolean).join(" / ");
        }
        var size = lengthBytesUTF8(text) + 1;
        var ptr = _malloc(size);
        stringToUTF8(text, ptr, size);
        return ptr;
    }
});
