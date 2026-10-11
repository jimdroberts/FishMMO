using System;
using System.Collections.Generic;
using FishMMO.Shared.WorldDesign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// Everything a generator change asks of the editor, in one headless pass: the procedural art rebuilt, the point-of-interest
	/// templates re-authored from code, the weather render profile's shader references filled, and each named generated scene's
	/// points of interest regenerated, its biomes repainted (scatter, paths, cliffs, NavMesh) and the scene saved.
	/// </summary>
	/// <remarks>
	/// <c>-executeMethod FishMMO.Client.WorldContentRefresh.FromCommandLine -refreshScene "Assets/…/Flo Monolith.unity"</c>
	/// (the flag may be repeated; with none, only the assets are refreshed). Optional steps, each the dashboard tool of the
	/// same name, run after the art and before the templates: <c>-authorBiomeArt</c> (Biomes → Author biome art),
	/// <c>-terrainProcesses</c> (Maintenance → Apply terrain process profiles), <c>-gatheringContent</c> (Maintenance →
	/// Generate Gathering Content). Exits 0 when every step completed without problems, 1 otherwise.
	/// </remarks>
	public static class WorldContentRefresh
	{
		public static void FromCommandLine()
		{
			int problems = 0;
			try
			{
				string[] args = Environment.GetCommandLineArgs();
				problems = Run(ScenesFromArgs(), Array.IndexOf(args, "-authorBiomeArt") >= 0, Array.IndexOf(args, "-terrainProcesses") >= 0,
					Array.IndexOf(args, "-gatheringContent") >= 0);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				problems++;
			}
			Debug.Log($"[World refresh] done, {problems} problem(s).");
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(problems == 0 ? 0 : 1);
			}
		}

		private static List<string> ScenesFromArgs()
		{
			var scenes = new List<string>();
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i + 1 < args.Length; i++)
			{
				if (args[i] == "-refreshScene")
				{
					scenes.Add(args[i + 1]);
				}
			}
			return scenes;
		}

		/// <summary>Runs every step; returns how many reported problems.</summary>
		public static int Run(IReadOnlyList<string> scenes, bool authorBiomeArt = false, bool terrainProcesses = false, bool gatheringContent = false)
		{
			int problems = 0;

			BiomeArtGenerator.Report art = BiomeArtGenerator.Generate(ProceduralArtMode.Full, ProceduralArtCatalogue.DefaultSeed);
			Debug.Log("[World refresh] art: " + art);
			if (!art.Completed || art.Problems.Count > 0)
			{
				problems += Math.Max(1, art.Problems.Count);
			}
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			if (authorBiomeArt)
			{
				Debug.Log("[World refresh] authoring biome art");
				BiomeArtAuthoring.ApplyFromDashboard();
			}
			if (terrainProcesses)
			{
				Debug.Log("[World refresh] applying terrain process profiles");
				TerrainProcessTable.ApplyFromDashboard();
			}
			if (gatheringContent)
			{
				Debug.Log("[World refresh] generating gathering content");
				GatheringContentGenerator.Generate();
			}
			AssetDatabase.SaveAssets();

			PointOfInterestTemplateAuthoring.Report templates = PointOfInterestTemplateAuthoring.AuthorAll(true);
			Debug.Log($"[World refresh] templates: {templates.Created.Count} created, problems: {string.Join("; ", templates.Problems)}");
			problems += templates.Problems.Count;

			WeatherRenderProfile profile = WeatherRenderAssets.Ensure();
			Debug.Log(profile != null
				? $"[World refresh] render profile {AssetDatabase.GetAssetPath(profile)}; site embers shader {(profile.SiteEmbersShader != null ? "set" : "MISSING")}"
				: "[World refresh] render profile: the precipitation shader was not found");
			if (profile == null || profile.SiteEmbersShader == null)
			{
				problems++;
			}

			foreach (string path in scenes)
			{
				problems += RefreshScene(path);
			}
			return problems;
		}

		private static int RefreshScene(string path)
		{
			Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
			if (!scene.IsValid())
			{
				Debug.LogError($"[World refresh] could not open {path}");
				return 1;
			}
			SceneManager.SetActiveScene(scene);
			int problems = 0;
			string poi = PointOfInterestGenerator.Regenerate(scene, out List<string> notes);
			if (poi != null)
			{
				Debug.LogError($"[World refresh] {scene.name}: points of interest: {poi}");
				problems++;
			}
			foreach (string note in notes)
			{
				Debug.Log($"[World refresh] {scene.name}: POI: {note}");
			}
			string repaint = BiomeRepaintTool.Repaint(scene, out SceneGenerationResult result);
			if (repaint != null)
			{
				Debug.LogError($"[World refresh] {scene.name}: repaint: {repaint}");
				problems++;
			}
			else
			{
				Debug.Log($"[World refresh] {scene.name}: repaint: {result.BiomeSummary}");
				foreach (string note in result.Notes)
				{
					Debug.Log($"[World refresh] {scene.name}: repaint note: {note}");
				}
			}
			if (problems == 0)
			{
				EditorSceneManager.MarkSceneDirty(scene);
				if (!EditorSceneManager.SaveScene(scene))
				{
					Debug.LogError($"[World refresh] {scene.name}: save failed");
					problems++;
				}
				AssetDatabase.SaveAssets();
				Debug.Log($"[World refresh] {scene.name}: saved");
			}
			return problems;
		}
	}
}
