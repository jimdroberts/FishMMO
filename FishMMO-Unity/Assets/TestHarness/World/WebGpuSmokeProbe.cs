using System;
using System.Collections;
using System.Collections.Generic;
using FishMMO.Client;
using FishMMO.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FishMMO.TestHarness.World
{
    /// <summary>Interactive browser rendering smoke test; injected into build copies only.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class WebGpuSmokeProbe : MonoBehaviour
    {
        public TerrainArraySet TerrainArrays;
        [Serializable] sealed class PerformanceSample
        {
            public string name;
            public int width, height, errors, grass, scatter, terrainCommands, terrainSubmissions, frames, targetFrameRate, vSyncCount;
            public bool development, clouds, batching, firstInstance, brg, brgActive;
            public int brgCameraCommands, brgLightCommands;
            public string settings;
            public bool cachedShadows;
            public int shadowRefreshFrames, shadowRefreshCascades, shadowMergedFrames, staticShadowDraws, staticShadowCommandsOmitted;
            public long readbackCacheInstances, readbackLiveInstances, readbackBadStarts;
            public int readbacksLanded;
            public double worldSeconds, worldSecondsStart;
            public List<float> frameMs;
        }
        readonly List<float> sampleFrames = new List<float>();
        int refreshFramesStart, refreshCascadesStart, mergedStart;
        string sampleName;
        int sampleAfterFrame;
        double sampleClockStart;
        public void SetBatching(string value) => TerrainGpuRenderer.BatchDraws = value == "1";
        // Same-build A/B of the BatchRendererGroup path (the renderer re-lays out when the switch changes).
        public void SetBrg(string value) => TerrainGpuRenderer.UseBrg = value == "1";
        // Runtime A/B switches for candidates that change the image (none adopted without the owner's approval) and a clock
        // hold for same-clock image pairs. "name=value"; the applied list is reported with every sample.
        readonly SortedDictionary<string,string> appliedSettings = new SortedDictionary<string,string>();
        public void SetSetting(string assignment)
        {
            int at = assignment.IndexOf('=');
            if (at <= 0) throw new ArgumentException("Expected name=value");
            string name = assignment.Substring(0,at), value = assignment.Substring(at+1);
            HarnessSettings.Apply(name, value);
            appliedSettings[name] = value;
            Debug.Log("[WebGPU Setting] " + name + "=" + value);
        }
        string AppliedSettings()
        {
            var parts = new List<string>();
            foreach (var pair in appliedSettings) parts.Add(pair.Key + "=" + pair.Value);
            return string.Join(";",parts);
        }
        public void BeginSample(string name)
        {
            sampleName = name; sampleFrames.Clear(); sampleAfterFrame = Time.frameCount + 1;
            refreshFramesStart = CachedMainLightShadows.RefreshFrames; refreshCascadesStart = CachedMainLightShadows.RefreshedCascades; mergedStart = CachedMainLightShadows.MergedFrames;
            sampleClockStart = FindAnyObjectByType<WorldSimController>()?.WorldSeconds ?? 0;
        }
        public void EndSample(string name)
        {
            if (sampleName != name) throw new InvalidOperationException("Performance sample name mismatch");
            var world = FindAnyObjectByType<WorldSimController>();
            var result = new PerformanceSample { name = name, width = Screen.width, height = Screen.height,
                errors = errors, development = Debug.isDebugBuild, grass = GrassBladeSystem.LastDrawCount,
                scatter = DetailScatterSystem.LastDrawCount, terrainCommands = CliffRockInstancing.LastCommandCount, terrainSubmissions = CliffRockInstancing.LastDrawCount,
                batching = TerrainGpuRenderer.BatchDraws && TerrainGpuRenderer.InstanceIdCarriesBase, firstInstance = TerrainGpuRenderer.InstanceIdCarriesBase,
                brg = TerrainGpuRenderer.UseBrg && TerrainGpuRenderer.BrgSupported, brgActive = CliffRockInstancing.Gpu != null && CliffRockInstancing.Gpu.BrgActive,
                brgCameraCommands = CliffRockInstancing.Gpu != null ? CliffRockInstancing.Gpu.LastBrgCameraCommands : 0, settings = AppliedSettings(),
                cachedShadows = CachedMainLightShadows.Enabled, shadowRefreshFrames = CachedMainLightShadows.RefreshFrames - refreshFramesStart,
                shadowRefreshCascades = CachedMainLightShadows.RefreshedCascades - refreshCascadesStart, shadowMergedFrames = CachedMainLightShadows.MergedFrames - mergedStart,
                staticShadowDraws = CachedMainLightShadows.LastStaticDraws,
                readbackCacheInstances = CachedMainLightShadows.ReadbackCacheInstances, readbackLiveInstances = CachedMainLightShadows.ReadbackLiveInstances,
                readbackBadStarts = CachedMainLightShadows.ReadbackBadStarts, readbacksLanded = CachedMainLightShadows.ReadbacksLanded,
                staticShadowCommandsOmitted = CliffRockInstancing.Gpu != null ? CliffRockInstancing.Gpu.LastStaticShadowCommandsOmitted : 0,
                brgLightCommands = CliffRockInstancing.Gpu != null ? CliffRockInstancing.Gpu.LastBrgLightCommands : 0,
                clouds = SkySystem.CloudsReady, worldSeconds = world != null ? world.WorldSeconds : 0,
                frames = sampleFrames.Count, frameMs = sampleFrames, targetFrameRate = Application.targetFrameRate, vSyncCount = QualitySettings.vSyncCount, worldSecondsStart = sampleClockStart };
            sampleName = null;
            Debug.Log("[WebGPU Performance] " + JsonUtility.ToJson(result));
        }
        int errors;
        float reportAt, fpsTime;
        int fpsFrames;
        float fps;
        string status = "Starting WebGPU rendering test";
        void Awake()
        {
            Application.logMessageReceived += OnLog;
            foreach (var binder in FindObjectsByType<TerrainArrayBinder>(FindObjectsSortMode.None)) binder.Bind(TerrainArrays);
        }
        IEnumerator Start()
        {
            yield return null;
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true);
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)
            {
                pipeline.renderScale = 1; pipeline.msaaSampleCount = 4;
                pipeline.upscalingFilter = UpscalingFilterSelection.Linear;
            }
            Application.runInBackground = true;
            var world = FindAnyObjectByType<WorldSimController>();
            if (world != null) { world.JumpTo(.5); world.RunRate = 1; world.FieldDriven = true; world.Paused = false; world.ForcePresent(); }
            SkySystem.EditorCloudsOff = false;
            var rendering = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Debug.Log($"[WebGPU Smoke] device={SystemInfo.graphicsDeviceName}; development={Debug.isDebugBuild}; api={SystemInfo.graphicsDeviceType}; compute={SystemInfo.supportsComputeShaders}; instancing={SystemInfo.supportsInstancing}; storedIndexUpload={TerrainIndexMapBaker.CanUpload()}; renderScale={rendering?.renderScale}; msaa={rendering?.msaaSampleCount}; upscaler={rendering?.upscalingFilter}; brgCompatible={TerrainGpuRenderer.BrgSupported}; firstInstance={TerrainGpuRenderer.InstanceIdCarriesBase}");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.WebGPU) Debug.LogError("[WebGPU Smoke] Expected WebGPU; browser loaded another graphics API.");
        }
        // Browser diagnostics can move between known views without editing the authored scene.
        public void SetView(string coordinates)
        {
            var values = Array.ConvertAll(coordinates.Split(','),x => float.Parse(x,System.Globalization.CultureInfo.InvariantCulture));
            if (values.Length != 6) throw new ArgumentException("Expected eye x,y,z and target x,y,z");
            var world = FindAnyObjectByType<WorldSimController>();
            if (world?.Camera == null) return;
            Vector3 eye = new Vector3(values[0],values[1],values[2]), target = new Vector3(values[3],values[4],values[5]);
            sweepEye = eye; sweepTarget = target; sweepStart = Time.realtimeSinceStartup;
            world.Camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));
            var controls = world.Camera.GetComponent<WorldSimCamera>();
            if (controls != null) controls.Face(target-eye);
        }
        // A camera that keeps moving, as PlayerPerfProbe's --moving (the cached shadows' moving-camera cost): ±metres along world
        // x from the last view's eye over a period, facing its target, kept 2 m over the ground. "metres,seconds"; "0" stops.
        Vector3 sweepEye, sweepTarget;
        float sweepMetres, sweepSeconds = 9f, sweepStart;
        public void SetSweep(string value)
        {
            var values = Array.ConvertAll(value.Split(','),x => float.Parse(x,System.Globalization.CultureInfo.InvariantCulture));
            sweepMetres = values[0];
            if (values.Length > 1 && values[1] > 0f) sweepSeconds = values[1];
            sweepStart = Time.realtimeSinceStartup;
        }
        void Sweep()
        {
            var camera = FindAnyObjectByType<WorldSimController>()?.Camera;
            if (sweepMetres <= 0f || camera == null) return;
            float t = (Time.realtimeSinceStartup - sweepStart) / sweepSeconds;
            Vector3 eye = sweepEye + Vector3.right * (Mathf.Sin(t * Mathf.PI * 2f) * sweepMetres);
            // The world scene's own physics (it loads with LocalPhysicsMode.Physics3D), not the default scene's.
            if (camera.gameObject.scene.GetPhysicsScene().Raycast(eye + Vector3.up * 500f, Vector3.down, out RaycastHit hit, 1000f)) eye.y = Mathf.Max(eye.y, hit.point.y + 2f);
            camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(sweepTarget-eye));
        }
        Light testLight;
        public void SetTime(string fraction)
        {
            var world = FindAnyObjectByType<WorldSimController>();
            if (world == null) return;
            world.JumpTo(double.Parse(fraction,System.Globalization.CultureInfo.InvariantCulture));
            world.Paused = false; world.ForcePresent();
        }
        public void SetTestLight(string enabled)
        {
            if (testLight == null)
            {
                var world = FindAnyObjectByType<WorldSimController>();
                if (world?.Camera == null) return;
                testLight = new GameObject("WebGPU Test Torch").AddComponent<Light>();
                testLight.transform.SetParent(world.Camera.transform,false);
                testLight.transform.localPosition = new Vector3(.3f,-.3f,.4f);
                testLight.type = LightType.Point; testLight.range = 11; testLight.intensity = 2.2f;
                testLight.color = new Color(1f,.64f,.34f); testLight.shadows = LightShadows.Soft;
                DirectionalShadowDraws.ConfigurePunctualLight(testLight);
            }
            testLight.enabled = enabled == "1";
        }
        void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
        }
        void Update()
        {
            Sweep();
            if (sampleName != null && Time.frameCount > sampleAfterFrame) sampleFrames.Add(Time.unscaledDeltaTime * 1000f);
            fpsTime += Time.unscaledDeltaTime; fpsFrames++;
            if (fpsTime >= 1) { fps = fpsFrames / fpsTime; fpsTime = 0; fpsFrames = 0; }
            if (Time.realtimeSinceStartup < reportAt) return;
            reportAt = Time.realtimeSinceStartup + 15;
            var world = FindAnyObjectByType<WorldSimController>();
            status = $"{SystemInfo.graphicsDeviceType} | {Screen.width}×{Screen.height} | {fps:F0} FPS | errors {errors}";
            Debug.Log($"[WebGPU Smoke] {status}; grass={GrassBladeSystem.LastDrawCount}; scatter={DetailScatterSystem.LastDrawCount}; terrainCommands={CliffRockInstancing.LastCommandCount}; brgActive={CliffRockInstancing.Gpu?.BrgActive}; clouds={SkySystem.CloudsReady}; clock={world?.WorldSeconds}");
        }
        void OnGUI()
        {
            GUI.Box(new Rect(12,12,650,52),status + "\nMoron 2 rendering test — native scale, 4× MSAA, live weather");
        }
        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            if (testLight != null) Destroy(testLight.gameObject);
        }
    }
}
