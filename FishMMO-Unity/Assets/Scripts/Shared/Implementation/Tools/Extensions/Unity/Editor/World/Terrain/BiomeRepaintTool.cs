#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Re-dresses a generated scene's ground from its biomes without touching its heights.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why it exists.</b> A generated scene's terrain is source the moment it is written: the
	/// generator seeds it and a designer sculpts it. A re-cut would throw the sculpting away to
	/// pick up new biome art or rules, so this runs only the biome stages — layers, alphamaps,
	/// arrays, details, trees and the biome map — against the heights as they now stand.
	/// </para>
	/// <para>
	/// <b>LOCAL copies.</b> A scene under <c>Assets/LOCAL</c> whose terrain data is LOCAL too (made by
	/// <see cref="LocalSceneCopyTool"/>) is painted with this machine's LOCAL art overrides — the gate
	/// is <see cref="LocalArtScope"/>, decided once here and passed down — and its biome map is written
	/// beside its own terrain data, never assigned to the committed atlas entry. A LOCAL scene still
	/// painting committed terrain data is refused: repainting it would rewrite the committed scene's
	/// ground from a private copy. Every other scene gets the committed defaults only.
	/// </para>
	/// <para>
	/// <b>What it leaves alone:</b> the heights, the sea, the boundary, and the backdrop past the
	/// scene's edge (which is rebuilt by a re-cut). It marks the scene dirty and does not save it,
	/// so nothing is written to the scene file until the designer chooses to.
	/// </para>
	/// </remarks>
	public static class BiomeRepaintTool
	{
		[DashboardTool(DashboardToolAttribute.WorldSceneDetails, "Repaint biomes in open scene", Section = "Generated scenes", Order = 10,
			Tooltip = "Re-runs the biome textures, details, trees and biome map on the open generated scene against its current (sculpted) heights. Heights, sea and boundary are untouched; the scene is marked dirty, not saved.",
			Confirm = "Repaint the open scene's ground from its biomes? Its terrain layers, alphamaps, details and trees are replaced; its heights are kept.")]
		public static void RepaintOpenScene()
		{
			string problem = Repaint(EditorSceneManager.GetActiveScene(), out SceneGenerationResult result);
			if (problem != null)
			{
				Debug.LogWarning($"[Repaint biomes] {problem}");
				return;
			}
			Debug.Log($"[Repaint biomes] '{result.Entry.SceneName}': {result.BiomeSummary}.");
			foreach (string note in result.Notes)
			{
				Debug.LogWarning($"[Repaint biomes] {note}");
			}
		}

		/// <summary>Repaints a scene. Returns why it did nothing, or null.</summary>
		public static string Repaint(Scene scene, out SceneGenerationResult result)
		{
			result = null;
			if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
			{
				return "Open a saved generated scene first.";
			}
			// A LOCAL copy ("<name> LOCAL") stands for the committed scene it was copied from.
			string atlasName = LocalArtScope.AtlasSceneName(scene.path, scene.name);
			WorldAtlasScene entry = WorldAtlasScene.Find(atlasName);
			if (entry == null || entry.Body == null || !entry.Placed)
			{
				return $"'{atlasName}' has no placed atlas entry, so there is no planet to read its biomes from.";
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
				// The radius it was cut at, so a point maps back to exactly the ground it came from.
				RadiusKm = entry.CutRadiusKm > 0f ? entry.CutRadiusKm : 0.0,
			};
			TerrainTilePlan plan = SceneGeneration.PlanTiles(entry.SizeKm);

			if (!Arrange(scene, plan, out Terrain[,] terrains, out string arrangeProblem))
			{
				return arrangeProblem;
			}

			result = new SceneGenerationResult
			{
				Plan = plan,
				RadiusKm = request.ResolvedRadiusKm,
				VerticalScale = request.VerticalScale,
				Entry = entry,
			};
			/* The one gate: decided here from the scene's path and its tiles' terrain data, and passed
			 * to everything that writes a reference. */
			var tiles = new List<Terrain>();
			foreach (Terrain terrain in terrains)
			{
				tiles.Add(terrain);
			}
			LocalArtScope scope = LocalArtScope.For(scene, tiles);
			if (LocalArtScope.IsLocalScenePath(scene.path) && !scope.AllowsLocal)
			{
				result = null;
				return $"'{scene.name}' is under Assets/LOCAL but paints committed terrain data ({scope.Reason}); repainting it would rewrite the committed scene's ground. Make the copy with \"Copy open scene to LOCAL for real art\", which gives it its own terrain data.";
			}
			string terrainFolder = scope.AllowsLocal ? LocalArtScope.TerrainFolderOf(tiles) : SceneGenerator.TerrainFolder(entry.Body, entry.SceneName);
			if (!SceneGenerator.PaintBiomes(scene, request, plan, terrains, terrainFolder, result, out _, scope))
			{
				string why = result.Notes.Count > 0 ? result.Notes[0] : "No biome fits this scene.";
				result = null;
				return why;
			}
			result.Notes.AddRange(IcePlacer.PlaceInOpenScene(scene, request, IcePlacer.SceneSeed(request), scope.IceOptions()).Notes);

			// The atlas entry is committed: it is never pointed at a LOCAL copy's map.
			if (entry.BiomeMap == null && result.BiomeMap != null && !BiomeLocalArtIndex.IsLocal(result.BiomeMap))
			{
				Undo.RecordObject(entry, "Repaint biomes");
				entry.BiomeMap = result.BiomeMap;
				EditorUtility.SetDirty(entry);
			}
			foreach (Terrain terrain in terrains)
			{
				EditorUtility.SetDirty(terrain.terrainData);
			}
			EditorSceneManager.MarkSceneDirty(scene);
			AssetDatabase.SaveAssets();
			return null;
		}

		/// <summary>
		/// Puts the scene's terrains back into the generator's tile grid by where they stand.
		/// </summary>
		private static bool Arrange(Scene scene, TerrainTilePlan plan, out Terrain[,] terrains, out string problem)
		{
			terrains = new Terrain[plan.CountX, plan.CountZ];
			problem = null;
			var found = new List<Terrain>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				found.AddRange(root.GetComponentsInChildren<Terrain>(true));
			}
			if (found.Count != plan.TotalTiles)
			{
				problem = $"'{scene.name}' has {found.Count} terrain(s) where its atlas entry implies {plan.TotalTiles} ({plan.CountX}×{plan.CountZ}); it does not look like the generator's layout.";
				return false;
			}

			float halfW = plan.WidthMetres * 0.5f, halfD = plan.DepthMetres * 0.5f;
			foreach (Terrain terrain in found)
			{
				if (terrain.terrainData == null)
				{
					problem = $"'{terrain.name}' has no terrain data.";
					return false;
				}
				Vector3 position = terrain.transform.position;
				int tx = Mathf.RoundToInt((position.x + halfW) / plan.TileMetres);
				int tz = Mathf.RoundToInt((position.z + halfD) / plan.TileMetres);
				if (tx < 0 || tz < 0 || tx >= plan.CountX || tz >= plan.CountZ || terrains[tx, tz] != null)
				{
					problem = $"'{terrain.name}' stands at ({position.x:F0}, {position.z:F0}), which is not a free tile of the generator's grid.";
					return false;
				}
				terrains[tx, tz] = terrain;
			}
			return true;
		}
	}
}
#endif
