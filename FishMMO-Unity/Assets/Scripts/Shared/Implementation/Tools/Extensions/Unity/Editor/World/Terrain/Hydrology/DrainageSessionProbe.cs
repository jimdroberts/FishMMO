#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Works a body's drainage out again and again through an editor session as one is used (fresh, the scene opened,
	/// in play mode, after it, after a recompile), so a session that drains the planet differently from a batch run
	/// shows at which step it began, and from what (<see cref="PlanetDrainage.Diagnostics"/>). Run in a full (non-batch)
	/// editor: FishMMO.Shared.WorldDesign.DrainageSessionProbe.Run. FISHMMO_DRAIN_OUT: where the dumps go;
	/// FISHMMO_RECUT_SCENE: the atlas scene (default Flo Monolith). Saves nothing into the project.
	/// </summary>
	[InitializeOnLoad]
	public static class DrainageSessionProbe
	{
		private const string StageKey = "FishMMO.DrainageSessionProbe.Stage";
		private const string OutKey = "FishMMO.DrainageSessionProbe.Out";
		/// <summary>Seconds each step waits before it reads, so delayed calls and auto-generation have run.</summary>
		private const double SettleSeconds = 25.0;
		private static double waitUntil;

		static DrainageSessionProbe()
		{
			if (SessionState.GetInt(StageKey, 0) > 0)
			{
				Arm();
			}
		}

		public static void Run()
		{
			string outDir = Environment.GetEnvironmentVariable("FISHMMO_DRAIN_OUT");
			if (string.IsNullOrEmpty(outDir))
			{
				outDir = Path.Combine(Path.GetTempPath(), "drainage-session");
			}
			Directory.CreateDirectory(outDir);
			SessionState.SetString(OutKey, outDir);
			SessionState.SetInt(StageKey, 1);
			Arm();
		}

		private static void Arm()
		{
			waitUntil = EditorApplication.timeSinceStartup + SettleSeconds;
			EditorApplication.update -= Tick;
			EditorApplication.update += Tick;
		}

		private static void Tick()
		{
			if (EditorApplication.timeSinceStartup < waitUntil || EditorApplication.isCompiling || EditorApplication.isUpdating)
			{
				return;
			}
			int stage = SessionState.GetInt(StageKey, 0);
			EditorApplication.update -= Tick;
			try
			{
				switch (stage)
				{
					case 1:
						Measure("1 fresh editor");
						EditorSceneManager.OpenScene("Assets/Scenes/WorldScene/Arthis/Flo Monolith.unity", OpenSceneMode.Single);
						SessionState.SetInt(StageKey, 2);
						Arm();
						break;
					case 2:
						Measure("2 scene open");
						SessionState.SetInt(StageKey, 3);
						EditorApplication.EnterPlaymode();
						Arm();
						break;
					case 3:
						if (!EditorApplication.isPlaying)
						{
							Arm();
							break;
						}
						Measure("3 in play mode");
						SessionState.SetInt(StageKey, 4);
						EditorApplication.ExitPlaymode();
						Arm();
						break;
					case 4:
						if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
						{
							Arm();
							break;
						}
						Measure("4 after play mode");
						SessionState.SetInt(StageKey, 5);
						CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
						Arm();
						break;
					case 5:
						Measure("5 after a recompile");
						SessionState.SetInt(StageKey, 0);
						EditorApplication.Exit(0);
						break;
				}
			}
			catch (Exception ex)
			{
				File.AppendAllText(Path.Combine(SessionState.GetString(OutKey, Path.GetTempPath()), "errors.txt"), $"stage {stage}: {ex}\n");
				SessionState.SetInt(StageKey, 0);
				EditorApplication.Exit(1);
			}
		}

		/// <summary>The drainage worked out afresh now, its log line and its detail written to the step's file.</summary>
		private static void Measure(string step)
		{
			string outDir = SessionState.GetString(OutKey, Path.GetTempPath());
			string name = Environment.GetEnvironmentVariable("FISHMMO_RECUT_SCENE");
			if (string.IsNullOrEmpty(name))
			{
				name = "Flo Monolith";
			}
			WorldAtlasScene entry = WorldEditorAssets.FindAll<WorldAtlasScene>().FirstOrDefault(e => e != null && e.SceneName == name);
			if (entry == null || entry.Body == null)
			{
				File.AppendAllText(Path.Combine(outDir, "errors.txt"), $"{step}: no atlas scene '{name}'\n");
				return;
			}
			var request = new SceneGenerationRequest
			{
				SceneName = entry.SceneName,
				Body = entry.Body,
				Layer = entry.Layer,
				Latitude = entry.Latitude,
				Longitude = entry.Longitude,
				SizeKm = entry.SizeKm,
				HeadingDegrees = entry.HeadingDegrees,
			};
			string detail = null;
			string line = null;
			void Capture(string condition, string stack, LogType type)
			{
				if (condition != null && condition.StartsWith("[Planet drainage]", StringComparison.Ordinal))
				{
					line = condition;
				}
			}
			PlanetDrainage.ClearCache();
			PlanetDrainage.Diagnostics = text => detail = text;
			Application.logMessageReceived += Capture;
			try
			{
				PlanetDrainage drainage = PlanetDrainage.For(request, SolarSystemProfile.Resolve(entry.Body));
				line ??= drainage != null ? $"{drainage.Result.Rivers.Count} rivers, {drainage.Result.Lakes.Count} lakes" : "no drainage";
			}
			finally
			{
				Application.logMessageReceived -= Capture;
				PlanetDrainage.Diagnostics = null;
			}
			File.WriteAllText(Path.Combine(outDir, $"stage {step}.txt"),
				$"{step}\nplaying {EditorApplication.isPlaying}, registry {FishMMO.Shared.Biomes.BiomeRegistry.Count} biomes\n{line}\n\n{detail}");
			File.AppendAllText(Path.Combine(outDir, "summary.txt"), $"{step}: {line}\n");
		}
	}
}
#endif
