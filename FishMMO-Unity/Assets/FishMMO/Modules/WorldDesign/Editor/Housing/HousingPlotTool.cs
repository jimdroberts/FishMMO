using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Places housing plots in a world scene that is already cut, without a re-cut.
	/// </summary>
	/// <remarks>
	/// <para>The cut levels a lot and keeps trees off it before the trees are grown; here the trees,
	/// props and buildings are already standing and the ground is already shaped, so this levels
	/// nothing and moves nothing. It only accepts ground that is already flat and clear: every sample
	/// is cast down from above and must land on bare terrain first (not a tree, a wall, a rock or a
	/// water surface), and the ground may vary by no more than <see cref="MaxRelief"/>.</para>
	///
	/// <para>Lots already in the scene's manifest are kept exactly, so running it twice adds nothing
	/// and never moves land somebody may own. A full re-cut later keeps them too.</para>
	/// </remarks>
	public static class HousingPlotTool
	{
		/// <summary>The flattest ground the tool accepts, metres of rise over a lot (the cut accepts 2 and levels it).</summary>
		public const float MaxRelief = 0.8f;

		/// <summary>The scene the batch entry places plots in: <c>-housingScene "Assets/…/Name.unity"</c>.</summary>
		public const string SceneArgument = "-housingScene";

		[DashboardTool(DashboardToolAttribute.Maintenance, "Place housing plots", Section = "World", Order = 22,
			Tooltip = "Lays housing plots (districts beside cities and capitals, countryside lots away from sites) in the open world scene on ground that is already flat and clear; keeps any lots it laid before. Save the scene after.")]
		public static void PlaceInOpenScene()
		{
			var notes = new List<string>();
			PlaceInScene(SceneManager.GetActiveScene(), notes);
			Debug.Log("[Housing plots]\n  " + string.Join("\n  ", notes));
		}

		/// <summary>Batch: opens the scene named by <see cref="SceneArgument"/>, places its plots, saves it and exits.</summary>
		public static void PlaceBatch()
		{
			var notes = new List<string>();
			bool ok = false;
			try
			{
				string path = ArgumentAfter(SceneArgument);
				if (string.IsNullOrEmpty(path))
				{
					notes.Add($"No scene given: pass {SceneArgument} \"Assets/…/Scene.unity\".");
				}
				else
				{
					Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
					int placed = PlaceInScene(scene, notes);
					ok = placed > 0 && EditorSceneManager.SaveScene(scene);
					AssetDatabase.SaveAssets();
				}
			}
			catch (Exception ex)
			{
				notes.Add($"FAILED: {ex}");
			}
			Debug.Log("[Housing plots]\n  " + string.Join("\n  ", notes));
			EditorApplication.Exit(ok ? 0 : 1);
		}

		/// <summary>Lays one open world scene's plots; the number of plots in it afterwards.</summary>
		public static int PlaceInScene(Scene scene, List<string> notes)
		{
			ScenePointsOfInterest points = PointsOf(scene);
			if (points == null)
			{
				notes.Add($"'{scene.name}' has no points-of-interest asset, so it was not cut by the generator; nothing placed.");
				return 0;
			}
			string terrainFolder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(points)).Replace('\\', '/');

			var terrains = new List<Terrain>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				terrains.AddRange(root.GetComponentsInChildren<Terrain>(true));
			}
			if (terrains.Count == 0)
			{
				notes.Add($"'{scene.name}' has no terrain; nothing placed.");
				return 0;
			}

			float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
			foreach (Terrain terrain in terrains)
			{
				Vector3 p = terrain.transform.position, size = terrain.terrainData.size;
				minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x + size.x);
				minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z + size.z);
			}

			float GroundAt(float x, float z)
			{
				foreach (Terrain terrain in terrains)
				{
					Vector3 p = terrain.transform.position, size = terrain.terrainData.size;
					if (x >= p.x && x <= p.x + size.x && z >= p.z && z <= p.z + size.z)
					{
						return terrain.SampleHeight(new Vector3(x, 0f, z)) + p.y;
					}
				}
				return float.NaN;
			}

			SceneBiomeMap biomes = SettingsOf(scene)?.BiomeMap();
			var ways = new PathExclusion(points.Paths);
			Physics.SyncTransforms();
			PhysicsScene physics = scene.GetPhysicsScene();
			bool Blocked(float x, float z)
			{
				if (points.IsKeptOut(x, z) || ways.Trees(x, z))
				{
					return true;
				}
				foreach (PointOfInterestPad pad in points.Pads)
				{
					if (pad.Contains(x, z))
					{
						return true;
					}
				}
				if (biomes != null)
				{
					BiomeTemplate biome = biomes.Sample(new Vector3(x, 0f, z));
					if (biome != null && (biome.LiveHabitat & (BiomeHabitat.Sea | BiomeHabitat.FreshWater | BiomeHabitat.Wetland | BiomeHabitat.Cave)) != 0)
					{
						return true;
					}
				}
				// Bare ground only: the first thing straight down must be the terrain.
				float ground = GroundAt(x, z);
				if (float.IsNaN(ground))
				{
					return true;
				}
				var from = new Vector3(x, ground + 60f, z);
				if (physics.Raycast(from, Vector3.down, out RaycastHit hit, 70f, ~0, QueryTriggerInteraction.Collide))
				{
					return hit.collider is not TerrainCollider;
				}
				return false;
			}

			var input = new HousingPlotPlanner.Input
			{
				Seed = HousingPlotPlanner.Hash((int)StableHash32(scene.name), 0x48, 0x50),
				HalfWidth = Mathf.Min(-minX, maxX),
				HalfDepth = Mathf.Min(-minZ, maxZ),
				GroundAt = GroundAt,
				Blocked = Blocked,
				MaxRelief = MaxRelief,
				SampleStep = 3f,
			};
			ScenePlotManifest manifest = AssetDatabase.LoadAssetAtPath<ScenePlotManifest>(HousingPlotStage.ManifestPath(terrainFolder, scene.name));
			if (manifest != null && manifest.Lots != null)
			{
				input.Kept.AddRange(manifest.Lots);
			}
			foreach (PointOfInterestRecord record in points.Points)
			{
				if (record == null)
				{
					continue;
				}
				input.Sites.Add(new HousingPlotPlanner.Disc { X = record.Position.x, Z = record.Position.z, Radius = Mathf.Max(0f, record.Radius) });
				if (record.Kind == NameGeneration.POIType.City || record.Kind == NameGeneration.POIType.Capital)
				{
					input.Settlements.Add(new HousingPlotPlanner.Settlement
					{
						Id = record.Id,
						Name = record.Name,
						X = record.Position.x,
						Z = record.Position.z,
						Radius = Mathf.Max(0f, record.Radius),
						Capital = record.Kind == NameGeneration.POIType.Capital,
					});
				}
			}

			HousingPlotPlanner.Result result = HousingPlotPlanner.Plan(input);
			int laid = HousingPlotStage.Lay(scene, result.Lots, GroundAt, notes);
			string written = HousingPlotStage.WriteManifest(terrainFolder, scene.name, result.Lots);
			notes.Add($"{scene.name}: {laid} plot(s) — {result.Kept} kept, {result.NewDistrictLots} new in {result.NewDistricts} district(s) beside {input.Settlements.Count} city/capital site(s), {result.NewCountryLots} new in the countryside. Manifest: {written}. Save the scene.");
			return laid;
		}

		private static uint StableHash32(string value) => NameGeneration.StableHash.FNV32(value ?? string.Empty);

		private static ScenePointsOfInterest PointsOf(Scene scene)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				ScenePointOfInterestSettings settings = root.GetComponentInChildren<ScenePointOfInterestSettings>(true);
				if (settings != null && settings.Points != null)
				{
					return settings.Points;
				}
			}
			return null;
		}

		private static WorldSceneSettings SettingsOf(Scene scene)
		{
			if (WorldSceneSettings.TryGetForScene(scene, out WorldSceneSettings registered))
			{
				return registered;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				WorldSceneSettings found = root.GetComponentInChildren<WorldSceneSettings>(true);
				if (found != null)
				{
					return found;
				}
			}
			return null;
		}

		private static string ArgumentAfter(string flag)
		{
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length - 1; i++)
			{
				if (args[i] == flag)
				{
					return args[i + 1];
				}
			}
			return null;
		}
	}
}
