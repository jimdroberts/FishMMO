using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.TestHarness.World.Editor
{
	/// <summary>Builds a local standalone rendering test bed without changing the authored scenes.</summary>
	public sealed class PlayerPerfBuild : IProcessSceneWithReport
	{
		/// <summary>
		/// The settings the build changes, as they were. A killed build never reaches its finally, and the editor had already saved
		/// the changed settings into ProjectSettings.asset (2026-11-03: IL2CPP's --jobs flags appended once a killed run). The next
		/// build, or the next editor start, puts them back from this file first.
		/// </summary>
		private const string RecoveryPath = "Library/FishMMO/PlayerPerfBuildRecovery.json";
		private static bool building;
		[Serializable]
		private sealed class SettingsRecovery
		{
			public GraphicsDeviceType[] apis; public bool defaults, timing, strict; public int subtarget; public string il2cppArgs;
		}

		[InitializeOnLoadMethod]
		private static void ScheduleRecovery() => EditorApplication.delayCall += RecoverInterruptedSettings;

		private static void RecoverInterruptedSettings()
		{
			if (building || !File.Exists(RecoveryPath)) return;
			var recovery = JsonUtility.FromJson<SettingsRecovery>(File.ReadAllText(RecoveryPath));
			Restore(recovery);
			File.Delete(RecoveryPath);
			Debug.Log("Restored interrupted standalone benchmark build settings: il2cppArgs='" + recovery.il2cppArgs + "'; strict=" + recovery.strict);
		}

		private static void Restore(SettingsRecovery settings)
		{
			PlayerSettings.SetAdditionalIl2CppArgs(settings.il2cppArgs ?? string.Empty);
			if (settings.apis != null && settings.apis.Length > 0) PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, settings.apis);
			PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, settings.defaults);
			PlayerSettings.enableFrameTimingStats = settings.timing;
			PlayerSettings.strictShaderVariantMatching = settings.strict;
			EditorUserBuildSettings.standaloneBuildSubtarget = (StandaloneBuildSubtarget)settings.subtarget;
		}

		/// <summary>The project's own IL2CPP arguments, without the job flags this build appends (left behind by older killed runs).</summary>
		public static string WithoutJobFlags(string args) =>
			Regex.Replace(args ?? string.Empty, @"\s*--(?:bee-)?jobs=\d+", string.Empty).Trim();

		public int callbackOrder => 1000;
		public void OnProcessScene(Scene scene, BuildReport report)
		{
			if (report == null || Environment.GetEnvironmentVariable("FISHMMO_PERF_PLAYER_BUILD") != "1") return;
			var host = new GameObject("Standalone Rendering Benchmark"); SceneManager.MoveGameObjectToScene(host, scene); var probe = host.AddComponent<PlayerPerfProbe>();
			probe.TerrainArrays = AssetDatabase.LoadAssetAtPath<FishMMO.Shared.TerrainArraySet>(FishMMO.Shared.TerrainArraySet.BakedAssetPath(scene.name));
			if (probe.TerrainArrays == null || !probe.TerrainArrays.IsUsable) throw new BuildFailedException("Bake usable terrain arrays before benchmarking " + scene.name);
		}
		public static void Build()
		{
			RecoverInterruptedSettings();
			string output = Environment.GetEnvironmentVariable("FISHMMO_PERF_BUILD_OUT") ?? "Builds/RenderingBenchmark/Player";
			Directory.CreateDirectory(output);
			string[] scenes = (Environment.GetEnvironmentVariable("FISHMMO_PERF_BUILD_SCENES") ?? "Assets/Scenes/WorldScene/Moron/Moron 2.unity").Split(';');
			var original = new SettingsRecovery
			{
				apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneLinux64),
				defaults = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64),
				timing = PlayerSettings.enableFrameTimingStats,
				strict = PlayerSettings.strictShaderVariantMatching,
				subtarget = (int)EditorUserBuildSettings.standaloneBuildSubtarget,
				il2cppArgs = WithoutJobFlags(PlayerSettings.GetAdditionalIl2CppArgs()),
			};
			string previous = Environment.GetEnvironmentVariable("FISHMMO_PERF_PLAYER_BUILD");
			Directory.CreateDirectory(Path.GetDirectoryName(RecoveryPath));
			File.WriteAllText(RecoveryPath, JsonUtility.ToJson(original));
			building = true;
			try
			{
				foreach (string path in scenes)
				{
					var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
					foreach (var bake in FishMMO.Shared.WorldDesign.TerrainArrayBaker.BakeScene(scene)) if (bake.Problems.Count > 0 || (!bake.Baked && !bake.UpToDate)) throw new BuildFailedException("Terrain bake failed: " + bake);
				}
				Environment.SetEnvironmentVariable("FISHMMO_PERF_PLAYER_BUILD", "1");
				EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
				PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
				PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.Vulkan });
				PlayerSettings.enableFrameTimingStats = true;
				// IL2CPP otherwise uses every host core, independently of Unity's -job-worker-count.
				int jobs = int.TryParse(Environment.GetEnvironmentVariable("FISHMMO_PERF_BUILD_JOBS"),out int requestedJobs) ? Mathf.Clamp(requestedJobs,1,16) : 2;
				PlayerSettings.SetAdditionalIl2CppArgs((original.il2cppArgs + " --jobs=" + jobs + " --bee-jobs=" + jobs).Trim());
				// FISHMMO_PERF_BUILD_STRICT_VARIANTS=1: a missing variant is an error and draws the error shader instead of
				// the nearest variant, which proves a stripping rule against real frames (Docs/Rendering/2026-11-03/shader-compile-cost).
				PlayerSettings.strictShaderVariantMatching = Environment.GetEnvironmentVariable("FISHMMO_PERF_BUILD_STRICT_VARIANTS") == "1" || original.strict;
				BuildOptions options = Environment.GetEnvironmentVariable("FISHMMO_PERF_BUILD_DEVELOPMENT") == "1" ? BuildOptions.Development : BuildOptions.None;
				var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = Path.Combine(output, "FishRenderingBenchmark.x86_64"), target = BuildTarget.StandaloneLinux64, subtarget = (int)StandaloneBuildSubtarget.Player, options = options });
				File.WriteAllText(Path.Combine(output, "build-summary.txt"), $"result={result.summary.result}\nerrors={result.summary.totalErrors}\nsize={result.summary.totalSize}\nduration={result.summary.totalTime}\ndevelopment={options.HasFlag(BuildOptions.Development)}\n");
				if (result.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Standalone rendering benchmark build failed");
			}
			finally
			{
				Environment.SetEnvironmentVariable("FISHMMO_PERF_PLAYER_BUILD", previous);
				Restore(original);
				File.Delete(RecoveryPath); building = false;
			}
			UnityEditor.EditorApplication.Exit(0);
		}
	}
}
