mergeInto(LibraryManager.library, {
    FishMMOWebGpuHasIndirectFirstInstance: function () {
        // Adapter support is insufficient: the feature must be enabled on the
        // device Unity created. Its WebGPU setup publishes that device here.
        var api = Module["WebGPU"];
        var device = api && api["device"];
        return device && device.features.has("indirect-first-instance") ? 1 : 0;
    }
});
