using System;
using System.IO;
using FishMMO.Shared;
using FishMMO.Shared.CustomBuildTool.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.TestHarness.World.Editor
{
    public sealed class WebGpuSmokeBuild : IProcessSceneWithReport
    {
        const string RecoveryPath = "Library/FishMMO/WebGpuSmokeBuildRecovery.json";
        static bool building;
        [Serializable] sealed class SettingsRecovery { public bool development; public string optimization; public bool wasm2023; public int codeGeneration = -1; }
        /// <summary>
        /// The harness player's engine code options (image-identical; restored after the build). WebAssembly 2023 compiles
        /// memcpy to native memory.copy instead of a call out to JavaScript (Unity's WebGPU backend copies every constant buffer
        /// per pass application); OptimizeSpeed generates specialized generic code instead of IL2CPP's fully shared generics.
        /// Opt out with FISHMMO_WEBGPU_WASM2023=0 / FISHMMO_WEBGPU_CODEGEN=size for an A/B (Docs/Rendering/2026-11-02/webgpu-10ms).
        /// </summary>
        public static bool UseWasm2023(string value) => value != "0";
        public static Il2CppCodeGeneration CodeGeneration(string value) => value == "size" ? Il2CppCodeGeneration.OptimizeSize : Il2CppCodeGeneration.OptimizeSpeed;
        [InitializeOnLoadMethod]
        static void ScheduleRecovery() => EditorApplication.delayCall += RecoverInterruptedSettings;
        static void RecoverInterruptedSettings()
        {
            if (building || !File.Exists(RecoveryPath)) return;
            var recovery = JsonUtility.FromJson<SettingsRecovery>(File.ReadAllText(RecoveryPath));
            EditorUserBuildSettings.development = recovery.development;
            if (WebAssemblyCodeOptimization.Current != null) WebAssemblyCodeOptimization.Current = recovery.optimization;
            if (recovery.codeGeneration >= 0)
            {
                PlayerSettings.WebGL.wasm2023 = recovery.wasm2023;
                PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL,(Il2CppCodeGeneration)recovery.codeGeneration);
            }
            File.Delete(RecoveryPath);
            Debug.Log("Restored interrupted offline WebGPU test settings: development=" + recovery.development + "; optimization=" + recovery.optimization);
        }
        public int callbackOrder => 1001;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || Environment.GetEnvironmentVariable("FISHMMO_WEBGPU_SMOKE") != "1") return;
            var host = new GameObject("WebGPU Rendering Test"); SceneManager.MoveGameObjectToScene(host,scene);
            var probe = host.AddComponent<WebGpuSmokeProbe>();
            probe.TerrainArrays = AssetDatabase.LoadAssetAtPath<TerrainArraySet>(TerrainArraySet.BakedAssetPath(scene.name));
            if (probe.TerrainArrays == null || !probe.TerrainArrays.IsUsable) throw new BuildFailedException("Missing baked scene terrain arrays");
        }
        public static void Build()
        {
            RecoverInterruptedSettings();
            const BuildTarget target = BuildTarget.WebGL;
            bool development = Environment.GetEnvironmentVariable("FISHMMO_WEBGPU_DEVELOPMENT") == "1";
            string output = Environment.GetEnvironmentVariable("FISHMMO_WEBGPU_OUT") ?? "Builds/RenderingBenchmark/webgpu-player";
            string scenePath = "Assets/Scenes/WorldScene/Moron/Moron 2.unity";
            var wasmOptimization = WebAssemblyCodeOptimization.Current;
            var apis = PlayerSettings.GetGraphicsAPIs(target); bool defaults = PlayerSettings.GetUseDefaultGraphicsAPIs(target);
            string extra = PlayerSettings.GetAdditionalIl2CppArgs(), template = PlayerSettings.WebGL.template;
            var compression = PlayerSettings.WebGL.compressionFormat;
            var symbols = PlayerSettings.WebGL.debugSymbolMode;
            bool cache = PlayerSettings.WebGL.dataCaching;
            var initialMemory = PlayerSettings.WebGL.initialMemorySize;
            var maximumMemory = PlayerSettings.WebGL.maximumMemorySize;
            bool wasm2023 = PlayerSettings.WebGL.wasm2023;
            var codeGeneration = PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.WebGL);
            string previous = Environment.GetEnvironmentVariable("FISHMMO_WEBGPU_SMOKE");
            bool editorDevelopment = EditorUserBuildSettings.development;
            Directory.CreateDirectory(Path.GetDirectoryName(RecoveryPath));
            File.WriteAllText(RecoveryPath,JsonUtility.ToJson(new SettingsRecovery { development=editorDevelopment,optimization=wasmOptimization.ToString(),wasm2023=wasm2023,codeGeneration=(int)codeGeneration }));
            building = true;
            Debug.Log($"[WebGPU Build] original development={editorDevelopment}; optimization={wasmOptimization}");
            try
            {
                var scene = EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Single);
                foreach (var bake in FishMMO.Shared.WorldDesign.TerrainArrayBaker.BakeScene(scene))
                    if (bake.Problems.Count > 0 || (!bake.Baked && !bake.UpToDate)) throw new BuildFailedException("Terrain arrays need a successful bake: " + bake);
                Environment.SetEnvironmentVariable("FISHMMO_WEBGPU_SMOKE","1");
                // This fixed, offline WorldSim harness is a development environment even
                // when using the release engine for timing. The production client validator
                // recognizes this Editor flag; no credentials or validator rules are changed.
                // BuildOptions below independently select the actual engine configuration.
                EditorUserBuildSettings.development = true;
                // Development Build and WASM optimization are independent. Build Times
                // leaves the engine's generated code unoptimized, even with BuildOptions.None.
                WebAssemblyCodeOptimization.Current = "RuntimeSpeed";
                PlayerSettings.WebGL.wasm2023 = UseWasm2023(Environment.GetEnvironmentVariable("FISHMMO_WEBGPU_WASM2023"));
                PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL,CodeGeneration(Environment.GetEnvironmentVariable("FISHMMO_WEBGPU_CODEGEN")));
                PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);
                PlayerSettings.SetGraphicsAPIs(target,new[] { GraphicsDeviceType.WebGPU });
                int jobs = int.TryParse(Environment.GetEnvironmentVariable("FISHMMO_WEBGPU_BUILD_JOBS"), out int requestedJobs) ? Mathf.Clamp(requestedJobs,1,8) : 2;
                PlayerSettings.SetAdditionalIl2CppArgs(extra + $" --jobs={jobs} --bee-jobs={jobs}");
                PlayerSettings.WebGL.template = "APPLICATION:Default";
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                PlayerSettings.WebGL.dataCaching = false;
                if (Environment.GetEnvironmentVariable("FISHMMO_WEBGPU_SYMBOLS") == "1") PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.External;
                // Moron 2 exceeded the 2 GiB WASM ceiling while combining its render pools.
                PlayerSettings.WebGL.initialMemorySize = 256;
                PlayerSettings.WebGL.maximumMemorySize = 4096;
                Directory.CreateDirectory(output);
                using (new WebGLRenderPipelineScope(true))
                {
                    var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath },target=target,locationPathName=output,options=development ? BuildOptions.Development : BuildOptions.None });
                    File.WriteAllText(Path.Combine(output,"build-summary.txt"),$"result={result.summary.result}\nerrors={result.summary.totalErrors}\nsize={result.summary.totalSize}\nduration={result.summary.totalTime}\napi=WebGPU only\ndevelopment={development}\nwasmOptimization={WebAssemblyCodeOptimization.Current}\nwasm2023={PlayerSettings.WebGL.wasm2023}\nil2cppCodeGeneration={PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.WebGL)}\n");
                    if (result.summary.result != BuildResult.Succeeded) throw new BuildFailedException("WebGPU rendering test build failed");
                    string pagePath = Path.Combine(output,"index.html");
                    string page = File.ReadAllText(pagePath);
                    page = page.Replace(".then((unityInstance) => {", ".then((unityInstance) => { window.fishWebGpuInstance = unityInstance;");
                    page = page.Replace("alert(message);", "console.error(message); unityShowBanner(message + ' Use the isolated Brave test launcher if GPU startup fails.', 'error');");
                    page = page.Replace("</head>", "<script src=\"webgpu_monitor.js\"></script></head>");
                    File.Copy(Path.Combine(Application.dataPath,"../Scripts/Rendering/webgpu_monitor.js"),Path.Combine(output,"webgpu_monitor.js"),true);
                    File.WriteAllText(pagePath,page);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("FISHMMO_WEBGPU_SMOKE",previous);
                EditorUserBuildSettings.development = editorDevelopment;
                WebAssemblyCodeOptimization.Current = wasmOptimization;
                PlayerSettings.SetAdditionalIl2CppArgs(extra);
                PlayerSettings.WebGL.debugSymbolMode = symbols;
                PlayerSettings.WebGL.template = template; PlayerSettings.WebGL.compressionFormat = compression; PlayerSettings.WebGL.dataCaching = cache;
                PlayerSettings.WebGL.initialMemorySize = initialMemory; PlayerSettings.WebGL.maximumMemorySize = maximumMemory;
                PlayerSettings.WebGL.wasm2023 = wasm2023; PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL,codeGeneration);
                PlayerSettings.SetGraphicsAPIs(target,apis); PlayerSettings.SetUseDefaultGraphicsAPIs(target,defaults);
                File.Delete(RecoveryPath); building = false;
            }
            EditorApplication.Exit(0);
        }
    }
}
