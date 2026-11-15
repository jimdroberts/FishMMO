#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using FishMMO.Shared.Biomes;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Writes a world scene's wildlife plan (<see cref="WildlifePlanner"/>) into the scene as authoring-only ObjectSpawners
	/// under one root, so the spawn-table bake carries them to the server like any hand-placed spawner. The cut runs it once
	/// the points of interest are in; the dashboard tool re-runs it on an open scene without a re-cut.
	/// </summary>
	/// <remarks>
	/// Re-runnable: the whole root is replaced each time, so the plan (deterministic per scene name) is the only source of
	/// these spawners and a hand edit under the root does not survive. It never throws: a scene without a catalogue, species
	/// or biome map simply gets no wildlife and a note saying why.
	/// </remarks>
	public static class WildlifeSpawnerCut
	{
		/// <summary>The scene root every wildlife spawner sits under; replaced whole on each placement.</summary>
		public const string RootName = "Wildlife Spawners";

		/// <summary>The catalogue of species prefabs and the planner's limits (made by <see cref="WildlifeBuilder"/>).</summary>
		public const string CataloguePath = "Assets/Prefabs/Server/SceneServer/Wildlife Catalogue.asset";

		/// <summary>
		/// Plans and places the scene's wildlife spawners, replacing any placed before.
		/// </summary>
		/// <param name="scene">The world scene; its points of interest (if any) keep wildlife out of towns and seed the dens.</param>
		/// <param name="map">The scene's biome map: where the ground is, and which habitat it is.</param>
		/// <param name="seaLevelY">The sea's surface, when the scene has one: ground below it counts as sea, which no species lives on.</param>
		/// <param name="notes">Gets one summary line (or the reason nothing was placed).</param>
		/// <returns>How many spawners were placed.</returns>
		public static int Place(Scene scene, SceneBiomeMap map, float? seaLevelY, List<string> notes)
		{
			notes ??= new List<string>();
			if (!scene.IsValid())
			{
				notes.Add("Wildlife: no scene, so no spawners.");
				return 0;
			}
			/* The biome lookups need the templates registered. The editor registers them on a deferred call after the project
			 * opens, which a batch method runs before: there every sample read as no habitat and only dens were placed. */
			if (BiomeRegistry.Count == 0)
			{
				FishMMO.Shared.NameGeneration.Editor.NamingTemplateEditorLoader.EnsureLoaded();
			}
			if (BiomeRegistry.Count == 0)
			{
				notes.Add("Wildlife: no biome templates are registered, so habitats cannot be read; no spawners placed.");
				return 0;
			}
			if (map == null)
			{
				notes.Add("Wildlife: the scene has no biome map, so no spawners.");
				return 0;
			}
			WildlifeCatalogue catalogue = AssetDatabase.LoadAssetAtPath<WildlifeCatalogue>(CataloguePath);
			if (catalogue == null)
			{
				notes.Add($"Wildlife: no catalogue at '{CataloguePath}' (run Build wildlife), so no spawners.");
				return 0;
			}
			List<WildlifePlanner.Species> species = SpeciesOf(catalogue);
			if (species.Count == 0)
			{
				notes.Add("Wildlife: the catalogue holds no prefab with a wildlife template, so no spawners.");
				return 0;
			}

			ScenePointsOfInterest points = PointsOf(scene);
			IReadOnlyList<PointOfInterestRecord> pois = points != null ? points.Points : null;
			IReadOnlyList<SceneCaveLifeSpot> caves = points != null ? points.CaveLife : null;
			Func<float, float, bool> keptOut = points != null ? points.IsKeptOut : (Func<float, float, bool>)null;

			// Off the map, or ground the map cannot place, is unknown: the planner treats it as nowhere to live.
			bool Ground(float x, float z, out BiomeHabitat habitat, out float altitude)
			{
				habitat = BiomeHabitat.None;
				altitude = 0f;
				var at = new Vector3(x, 0f, z);
				if (!map.Contains(at) || !map.TryGroundAltitude(x, z, out altitude))
				{
					return false;
				}
				if (BiomeRegistry.TryGetByID(map.IDAt(at), out BiomeTemplate biome) && biome != null)
				{
					habitat = biome.LiveHabitat;
				}
				if (seaLevelY.HasValue && altitude < seaLevelY.Value)
				{
					habitat = BiomeHabitat.Sea;
				}
				return true;
			}

			List<WildlifeArea> areas = WildlifePlanner.Plan(scene.name, new Rect(map.WorldOrigin, map.WorldSize), Ground,
				pois, keptOut, caves, species, catalogue);

			GameObject root = ReplaceRoot(scene);
			int dens = 0;
			var holding = new Dictionary<string, int>();
			foreach (WildlifeArea area in areas)
			{
				var host = new GameObject(area.Name);
				host.tag = ObjectSpawner.EditorOnlyTag;
				host.transform.SetParent(root.transform, false);
				host.transform.position = area.Center;

				bool hunts = false;
				foreach (WildlifeWeight weight in area.Species)
				{
					WildlifeTemplate template = weight.Species.Template;
					hunts |= template != null && template.Role == WildlifeRole.Predator;
					string name = template != null ? template.name : "?";
					holding.TryGetValue(name, out int held);
					holding[name] = held + 1;
				}

				ObjectSpawner spawner = host.AddComponent<ObjectSpawner>();
				spawner.Spawnables = WildlifePlanner.Spawnables(area);
				spawner.MaxSpawnCount = area.Count;
				spawner.InitialSpawnCount = area.Count;
				spawner.SpawnType = area.Species.Count > 1 ? ObjectSpawnType.Weighted : ObjectSpawnType.Linear;
				spawner.RandomSpawnPosition = true;
				spawner.BoundingBoxSize = area.Size;
				spawner.SphereRadius = 0.5f;
				// Wildlife is never prewarmed past its count: the areas overlap, so headroom would multiply across them.
				spawner.PrewarmHeadroom = 0;
				spawner.RandomRespawnTime = true;
				spawner.Pack = WildlifePlanner.PackSettings(hunts);
				EditorUtility.SetDirty(spawner);
				if (area.Den)
				{
					dens++;
				}
			}
			EditorSceneManager.MarkSceneDirty(scene);

			string kinds = holding.Count == 0
				? "none"
				: string.Join(", ", holding.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} {p.Value}"));
			notes.Add($"Wildlife: {areas.Count} spawner(s) ({dens} at dens and caves), species: {kinds}");
			return areas.Count;
		}

		/// <summary>Re-places the open scene's wildlife spawners from its biome map, without a re-cut.</summary>
		[DashboardTool(DashboardToolAttribute.Maintenance, "Place wildlife spawners", Section = "World", Order = 21,
			Tooltip = "Re-places the wildlife spawners in the open world scene from its biome map (no re-cut needed); save the scene after.")]
		public static void PlaceInOpenScene()
		{
			var notes = new List<string>();
			PlaceInScene(SceneManager.GetActiveScene(), notes);
			Debug.Log("[Wildlife spawners]\n  " + string.Join("\n  ", notes));
		}

		/// <summary>Re-places one open world scene's wildlife spawners from its biome map; the number placed (0 when it is not a world scene).</summary>
		public static int PlaceInScene(Scene scene, List<string> notes)
		{
			WorldSceneSettings settings = SettingsOf(scene);
			if (settings == null)
			{
				notes.Add($"Wildlife: '{scene.name}' has no WorldSceneSettings, so it is not a world scene.");
				return 0;
			}
			// The sea level is not kept on the scene; the biome habitats still keep land species off sea biomes.
			return Place(scene, settings.BiomeMap, null, notes);
		}

		/// <summary>The catalogue's species: every prefab whose NPC carries a wildlife template.</summary>
		private static List<WildlifePlanner.Species> SpeciesOf(WildlifeCatalogue catalogue)
		{
			var species = new List<WildlifePlanner.Species>();
			if (catalogue.Species == null)
			{
				return species;
			}
			foreach (NetworkObject prefab in catalogue.Species)
			{
				if (prefab == null)
				{
					continue;
				}
				NPC npc = prefab.GetComponent<NPC>();
				if (npc != null && npc.Wildlife != null)
				{
					species.Add(new WildlifePlanner.Species(prefab, npc.Wildlife));
				}
			}
			return species;
		}

		/// <summary>The scene's points of interest asset, if the cut gave it one.</summary>
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

		/// <summary>
		/// The scene's settings: the registry first, then a search, since the registry fills in OnEnable and the component
		/// does not run in edit mode.
		/// </summary>
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

		/// <summary>A fresh, authoring-only root in the scene (any earlier one destroyed).</summary>
		private static GameObject ReplaceRoot(Scene scene)
		{
			foreach (GameObject existing in scene.GetRootGameObjects())
			{
				if (existing.name == RootName)
				{
					UnityEngine.Object.DestroyImmediate(existing);
				}
			}
			var root = new GameObject(RootName);
			root.tag = ObjectSpawner.EditorOnlyTag;
			SceneManager.MoveGameObjectToScene(root, scene);
			return root;
		}
	}
}
#endif
