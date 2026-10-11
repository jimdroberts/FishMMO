#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The point-of-interest stage's entry points outside a cut: Regenerate POIs on the open scene, and the event the
	/// gameplay stages hook after a scene's sites are built.
	/// </summary>
	public static class PointOfInterestGenerator
	{
		/// <summary>The scene root every generated site hangs under.</summary>
		public const string RootName = "Points of Interest";

		/// <summary>The prop source the sites' instanced props are baked under (<see cref="ScenePropBaker"/>).</summary>
		public const string PropSource = "POI";

		/// <summary>
		/// Raised after a scene's sites are built (a cut, a repaint, a regenerate), before its props and NavMesh are
		/// baked: the hook for a stage that works on all the sites at once (baking a spawn table).
		/// </summary>
		public static event Action<Scene, ScenePointsOfInterest, List<string>> Placed;

		internal static void RaisePlaced(Scene scene, ScenePointsOfInterest points, List<string> notes)
		{
			if (Placed == null)
			{
				return;
			}
			foreach (Action<Scene, ScenePointsOfInterest, List<string>> handler in Placed.GetInvocationList())
			{
				try
				{
					handler(scene, points, notes);
				}
				catch (Exception ex)
				{
					Debug.LogException(ex);
					notes?.Add($"Points of interest: a Placed handler threw: {ex.Message}");
				}
			}
		}

		[DashboardTool(DashboardToolAttribute.WorldSceneDetails, "Regenerate POIs in open scene", Section = "Generated scenes", Order = 12,
			Tooltip = "Plans and builds the open generated scene's points of interest again on its current ground. Pads already flattened stay; the scatter is not redone and the NavMesh is not rebaked (Repaint biomes does both). The scene is marked dirty, not saved.",
			Confirm = "Regenerate the open scene's points of interest? Every generated site is replaced.")]
		public static void RegenerateOpenScene()
		{
			string problem = Regenerate(EditorSceneManager.GetActiveScene(), out List<string> notes);
			if (problem != null)
			{
				Debug.LogWarning($"[Points of interest] {problem}");
				return;
			}
			foreach (string note in notes)
			{
				Debug.Log($"[Points of interest] {note}");
			}
		}

		/// <summary>
		/// Plans and places a generated scene's points of interest again on its ground as it stands. Returns why it did
		/// nothing, or null.
		/// </summary>
		/// <remarks>
		/// The ground is read back from the tiles, the water from the hydrology asset and the biomes from the planet, as a
		/// repaint does. Pads the last plan flattened stay flattened (the ground is source once written); new pads are
		/// flattened. Scatter, cliffs and the NavMesh are not redone: run Repaint biomes for those.
		/// </remarks>
		public static string Regenerate(Scene scene, out List<string> notes)
		{
			notes = new List<string>();
			if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
			{
				return "Open a saved generated scene first.";
			}
			FishMMO.Shared.NameGeneration.Editor.NamingTemplateEditorLoader.EnsureLoaded();
			string atlasName = LocalArtScope.AtlasSceneName(scene.path, scene.name);
			WorldAtlasScene entry = WorldAtlasScene.Find(atlasName);
			if (entry == null || entry.Body == null || !entry.Placed)
			{
				return $"'{atlasName}' has no placed atlas entry, so there is no planet to plan its points of interest on.";
			}
			SceneGenerationRequest request = BiomeRepaintTool.RequestFor(entry);
			request.PointsOfInterest = entry.PointsOfInterest;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.name == RootName && root.TryGetComponent(out ScenePointOfInterestSettings existing) && existing.Settings != null)
				{
					// The scene's own settings component is where they are edited after the cut.
					request.PointsOfInterest = existing.Settings;
				}
			}
			TerrainTilePlan plan = SceneGeneration.PlanTiles(entry.SizeKm);
			if (!BiomeRepaintTool.Arrange(scene, plan, out Terrain[,] terrains, out string arrangeProblem))
			{
				return arrangeProblem;
			}
			var tiles = new List<Terrain>();
			foreach (Terrain terrain in terrains)
			{
				tiles.Add(terrain);
			}
			LocalArtScope scope = LocalArtScope.For(scene, tiles);
			string terrainFolder = scope.AllowsLocal ? LocalArtScope.TerrainFolderOf(tiles) : SceneGenerator.TerrainFolder(entry.Body, entry.SceneName);

			ScenePropBaker.BakeFolder = terrainFolder;
			try
			{
				SceneWater water = SceneGenerator.LoadWater(request, plan, terrains, notes);
				SolarSystemProfile system = SolarSystemProfile.Resolve(request.Body);
				SceneBiomeField field = SceneBiomeField.Build(request, plan, (east, north) => SceneGenerator.GroundAltitude(terrains, plan, east, north), system, water: water);
				PointOfInterestStage stage = PointOfInterestStage.ForCut(request, terrainFolder);
				stage.PlanSites(scene, plan, terrains, field.Biomes.Count > 0 ? field : null, water, notes);
				stage.Place(scene, plan, terrains, water, notes, null);
			}
			finally
			{
				ScenePropBaker.BakeFolder = null;
			}
			foreach (Terrain terrain in terrains)
			{
				EditorUtility.SetDirty(terrain.terrainData);
			}
			EditorSceneManager.MarkSceneDirty(scene);
			AssetDatabase.SaveAssets();
			notes.Add("The scatter and NavMesh were not redone; run Repaint biomes to keep them out of the new sites and off the new ways, and, on a scene cut before it had ways, to give its terrain their earth, gravel and cobbles (until then they are carved and baked but not drawn).");
			return null;
		}
	}
}
#endif
