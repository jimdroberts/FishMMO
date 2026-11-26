#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Links two world scenes with a pair of gates, one in each scene's capital (else its biggest city, town or port, or a site
	/// named by the caller, <see cref="Link(string, string, string, string, bool, List{string})"/>): a
	/// portal arch with a <see cref="SceneTeleporter"/> in its opening, and a <see cref="TeleporterDestination"/> a few metres in
	/// front of it where travellers from the other scene arrive, facing into town. Re-runnable: a scene's old gates to the same
	/// scene are replaced. The world scene details cache must be rebuilt after (the batch entry does), which is what carries the
	/// gate to the server.
	/// </summary>
	/// <remarks>
	/// Each scene is opened on its own: world scenes share coordinates, so probing the ground with both loaded would hit the
	/// other's terrain. The spot is searched outward from the town's centre for open, level terrain (the first thing under it
	/// is the terrain, not a roof) with room for the arch and for the arrival in front of it.
	/// </remarks>
	public static class WorldGateBuilder
	{
		public const string RootName = "Scene Gates";
		private const string ArchPath = "Assets/Prefabs/Shared/Biomes/Generated/Prefabs/Structures/Structure_PortalArch_0.prefab";
		private const float ArrivalMetres = 7f;

		private static readonly POIType[] TownKinds = { POIType.Capital, POIType.City, POIType.Town, POIType.Port, POIType.TradingPost, POIType.Village };

		/// <summary>Batch: links Moron 1 and Moron 2, re-places both scenes' wildlife spawners, rebakes their spawn tables and the details cache.</summary>
		public static void LinkMoronBatch()
		{
			bool ok = false;
			var notes = new List<string>();
			try
			{
				ok = Link("Assets/Scenes/WorldScene/Moron/Moron 1.unity", "Assets/Scenes/WorldScene/Moron/Moron 2.unity", placeWildlife: true, notes);
				if (ok)
				{
					ok = WorldSceneDetailsCacheBuilder.Rebuild();
					notes.Add(ok ? "World scene details cache rebuilt." : "FAILED: the world scene details cache rebuild failed.");
				}
			}
			catch (Exception ex)
			{
				notes.Add($"FAILED: {ex}");
			}
			Debug.Log("[Scene gates]\n  " + string.Join("\n  ", notes));
			EditorApplication.Exit(ok ? 0 : 1);
		}

		/// <summary>Batch: re-places Moron 1's and Moron 2's wildlife spawners and rebakes their spawn tables (gates untouched).</summary>
		public static void PlaceMoronWildlifeBatch()
		{
			bool ok = true;
			var notes = new List<string>();
			try
			{
				foreach (string path in new[] { "Assets/Scenes/WorldScene/Moron/Moron 1.unity", "Assets/Scenes/WorldScene/Moron/Moron 2.unity" })
				{
					Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
					int placed = WildlifeSpawnerCut.PlaceInScene(scene, notes);
					EditorSceneManager.SaveScene(scene);
					var problems = new List<string>();
					SpawnTableBaker.BakeScene(scene, problems);
					notes.Add($"{scene.name}: {placed} wildlife spawner(s); spawn table rebaked.");
					ok &= placed > 0;
				}
				AssetDatabase.SaveAssets();
			}
			catch (Exception ex)
			{
				ok = false;
				notes.Add($"FAILED: {ex}");
			}
			Debug.Log("[Wildlife spawners]\n  " + string.Join("\n  ", notes));
			EditorApplication.Exit(ok ? 0 : 1);
		}

		/// <summary>Batch: <see cref="PrepareMoron"/>, exiting 0 on success.</summary>
		public static void PrepareMoronBatch()
		{
			EditorApplication.Exit(PrepareMoron() ? 0 : 1);
		}

		/// <summary>Re-places Moron 1's and Moron 2's wildlife spawners and rebakes their spawn tables, then puts portal surfaces in
		/// and bakes portal views for every world scene, then rebuilds the world scene details cache.</summary>
		public static bool PrepareMoron()
		{
			bool ok = true;
			var notes = new List<string>();
			try
			{
				foreach (string path in new[] { "Assets/Scenes/WorldScene/Moron/Moron 1.unity", "Assets/Scenes/WorldScene/Moron/Moron 2.unity" })
				{
					Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
					SceneManager.SetActiveScene(scene);
					Physics.SyncTransforms();
					int placed = WildlifeSpawnerCut.PlaceInScene(scene, notes);
					EditorSceneManager.SaveScene(scene);
					var problems = new List<string>();
					SpawnTableBaker.BakeScene(scene, problems);
					notes.Add($"{scene.name}: {placed} wildlife spawner(s); spawn table rebaked" + (problems.Count > 0 ? $" ({string.Join("; ", problems)})." : "."));
					ok &= placed > 0;
				}
				AssetDatabase.SaveAssets();
				PortalViewBaker.BakeAll();
				bool cache = WorldSceneDetailsCacheBuilder.Rebuild();
				notes.Add(cache ? "World scene details cache rebuilt." : "FAILED: the world scene details cache rebuild failed.");
				ok &= cache;
			}
			catch (Exception ex)
			{
				ok = false;
				notes.Add($"FAILED: {ex}");
			}
			Debug.Log("[Prepare Moron]\n  " + string.Join("\n  ", notes) + $"\n  ok={ok}");
			return ok;
		}

		/// <summary>Places a gate in each scene leading to the other; optionally re-places and rebakes their wildlife too.</summary>
		public static bool Link(string pathA, string pathB, bool placeWildlife, List<string> notes)
		{
			return Link(pathA, null, pathB, null, placeWildlife, notes);
		}

		/// <summary>
		/// Batch: links Moron 2 and Moron 3 through two magical sites rather than their capitals (Moron 2's ley nexus, Moron 3's
		/// ritual stones), then rebuilds the world scene details cache. Exits 0 when it worked.
		/// </summary>
		public static void LinkMoron2And3Batch()
		{
			bool ok = false;
			var notes = new List<string>();
			try
			{
				ok = Link("Assets/Scenes/WorldScene/Moron/Moron 2.unity", "Confluence of the Mysterious Bell",
					"Assets/Scenes/WorldScene/Moron/Moron 3.unity", "To Offering Stone", placeWildlife: false, notes);
				if (ok)
				{
					ok = WorldSceneDetailsCacheBuilder.Rebuild();
					notes.Add(ok ? "World scene details cache rebuilt." : "FAILED: the world scene details cache rebuild failed.");
				}
			}
			catch (Exception ex)
			{
				notes.Add($"FAILED: {ex}");
			}
			Debug.Log("[Scene gates]\n  " + string.Join("\n  ", notes) + $"\n  ok={ok}");
			EditorApplication.Exit(ok ? 0 : 1);
		}

		/// <summary>
		/// Places a gate in each scene leading to the other, at the named point of interest in each (<paramref name="siteA"/>,
		/// <paramref name="siteB"/>), or at the scene's capital (else its biggest town) where the name is null.
		/// </summary>
		public static bool Link(string pathA, string siteA, string pathB, string siteB, bool placeWildlife, List<string> notes)
		{
			string nameA = System.IO.Path.GetFileNameWithoutExtension(pathA);
			string nameB = System.IO.Path.GetFileNameWithoutExtension(pathB);

			// A: its arrival (from B) and its gate (to B, destination filled in once B's arrival exists).
			if (!PlaceGate(pathA, siteA, nameB, null, placeWildlife, notes, out string arrivalA))
			{
				return false;
			}
			if (!PlaceGate(pathB, siteB, nameA, arrivalA, placeWildlife, notes, out string arrivalB))
			{
				return false;
			}
			// Back to A to aim its gate at B's arrival.
			Scene a = EditorSceneManager.OpenScene(pathA, OpenSceneMode.Single);
			SceneTeleporter gate = FindGate(a, nameB);
			if (gate == null)
			{
				notes.Add($"FAILED: the gate to {nameB} vanished from {nameA}.");
				return false;
			}
			gate.DestinationID = arrivalB;
			EditorUtility.SetDirty(gate);
			EditorSceneManager.SaveScene(a);
			notes.Add($"{nameA}'s gate now leads to {nameB}.");

			// Both scenes' portal surfaces and the views of both arrivals.
			SceneManager.SetActiveScene(a);
			PortalViewBaker.ProcessScene(a, notes);
			Scene b = EditorSceneManager.OpenScene(pathB, OpenSceneMode.Single);
			SceneManager.SetActiveScene(b);
			PortalViewBaker.ProcessScene(b, notes);
			return true;
		}

		private static bool PlaceGate(string scenePath, string siteName, string otherName, string destinationId, bool placeWildlife, List<string> notes, out string arrivalId)
		{
			arrivalId = null;
			Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
			SceneManager.SetActiveScene(scene);
			Physics.SyncTransforms();

			if (placeWildlife)
			{
				WildlifeSpawnerCut.PlaceInScene(scene, notes);
			}

			PointOfInterestRecord town;
			if (siteName != null)
			{
				if (!TryFindSite(scene, siteName, out town))
				{
					notes.Add($"FAILED: {scene.name} has no point of interest called '{siteName}'.");
					return false;
				}
			}
			else if (!TryFindTown(scene, out town))
			{
				notes.Add($"FAILED: {scene.name} has no capital, city, town or village.");
				return false;
			}
			SceneBiomeMap map = FindComponent<WorldSceneSettings>(scene)?.BiomeMap();
			if (!TryFindSpot(town, map, out Vector3 spot, out Quaternion facing))
			{
				notes.Add($"FAILED: no open, level ground for a gate near {scene.name}'s {town.Kind} at {town.Position}.");
				return false;
			}

			GameObject root = FindRoot(scene) ?? NewRoot(scene);
			RemoveChild(root, GateName(otherName));
			RemoveChild(root, ArchName(otherName));
			RemoveChild(root, ArrivalName(otherName), unregister: true);

			var arch = AssetDatabase.LoadAssetAtPath<GameObject>(ArchPath);
			if (arch != null)
			{
				var archInstance = (GameObject)PrefabUtility.InstantiatePrefab(arch, scene);
				archInstance.name = ArchName(otherName);
				archInstance.transform.SetParent(root.transform, true);
				archInstance.transform.SetPositionAndRotation(spot, facing);
			}

			var gateObject = new GameObject(GateName(otherName));
			gateObject.transform.SetParent(root.transform, false);
			gateObject.transform.SetPositionAndRotation(spot, facing);
			BoxCollider volume = gateObject.AddComponent<BoxCollider>();
			volume.isTrigger = true;
			volume.size = new Vector3(2.6f, 3.6f, 1.2f);
			volume.center = new Vector3(0f, 1.8f, 0f);
			SceneTeleporter gate = gateObject.AddComponent<SceneTeleporter>();
			gate.DestinationID = destinationId;

			// The arrival: in front of the arch, facing on into town. Positioned before it is added, so its Reset registers the
			// right place; registered again explicitly in case Reset did not run.
			var arrivalObject = new GameObject(ArrivalName(otherName));
			arrivalObject.transform.SetParent(root.transform, false);
			Vector3 forward = facing * Vector3.forward;
			arrivalObject.transform.SetPositionAndRotation(Ground(spot + forward * ArrivalMetres) + Vector3.up * 0.1f, facing);
			TeleporterDestination arrival = arrivalObject.AddComponent<TeleporterDestination>();
			if (string.IsNullOrEmpty(arrival.DestinationID))
			{
				var so = new SerializedObject(arrival);
				so.FindProperty("destinationID").stringValue = Guid.NewGuid().ToString();
				so.ApplyModifiedPropertiesWithoutUndo();
			}
			TeleporterCache.RegisterDestination(arrival, scene.name);
			arrivalId = arrival.DestinationID;

			EditorUtility.SetDirty(gate);
			EditorUtility.SetDirty(arrival);
			EditorSceneManager.MarkSceneDirty(scene);
			EditorSceneManager.SaveScene(scene);
			AssetDatabase.SaveAssets();

			if (placeWildlife)
			{
				var problems = new List<string>();
				SpawnTableBaker.BakeScene(scene, problems);
				notes.Add($"{scene.name}: spawn table rebaked" + (problems.Count > 0 ? $" ({problems.Count} note(s): {string.Join("; ", problems)})" : "."));
			}
			notes.Add($"{scene.name}: gate to {otherName} in its {town.Kind} at {spot} (facing {facing.eulerAngles.y:0}°), arrival {arrivalId}.");
			return true;
		}

		/// <summary>The capital, else the biggest city, town, port, trading post or village.</summary>
		private static bool TryFindTown(Scene scene, out PointOfInterestRecord town)
		{
			town = null;
			ScenePointOfInterestSettings settings = FindComponent<ScenePointOfInterestSettings>(scene);
			if (settings == null || settings.Points == null || settings.Points.Points == null)
			{
				return false;
			}
			foreach (POIType kind in TownKinds)
			{
				foreach (PointOfInterestRecord record in settings.Points.Points)
				{
					if (record.Kind == kind && (town == null || record.Radius > town.Radius))
					{
						town = record;
					}
				}
				if (town != null)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The point of interest called <paramref name="name"/> (its generated name, exactly).</summary>
		private static bool TryFindSite(Scene scene, string name, out PointOfInterestRecord site)
		{
			site = null;
			ScenePointOfInterestSettings settings = FindComponent<ScenePointOfInterestSettings>(scene);
			if (settings == null || settings.Points == null || settings.Points.Points == null)
			{
				return false;
			}
			foreach (PointOfInterestRecord record in settings.Points.Points)
			{
				if (string.Equals(record.Name, name, StringComparison.Ordinal))
				{
					site = record;
					return true;
				}
			}
			return false;
		}

		/// <summary>Open, level terrain near the town's centre, with room for the arch and the arrival in front of it.</summary>
		private static bool TryFindSpot(PointOfInterestRecord town, SceneBiomeMap map, out Vector3 spot, out Quaternion facing)
		{
			spot = default;
			facing = Quaternion.identity;
			Vector3 centre = town.Position;
			float reach = Mathf.Max(60f, town.Radius * 1.3f);
			for (float r = 14f; r <= reach; r += 6f)
			{
				for (int k = 0; k < 24; ++k)
				{
					float angle = k * Mathf.PI * 2f / 24f;
					Vector3 at = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r;
					if (!TryTerrain(at, map, out Vector3 ground))
					{
						continue;
					}
					Vector3 inward = centre - ground;
					inward.y = 0f;
					Quaternion look = Quaternion.LookRotation(inward.normalized, Vector3.up);
					Vector3 arrival = ground + look * Vector3.forward * ArrivalMetres;
					if (!TryTerrain(arrival, map, out Vector3 arrivalGround) || Mathf.Abs(arrivalGround.y - ground.y) > 1.5f)
					{
						continue;
					}
					if (!Clear(ground + Vector3.up * 2.9f, new Vector3(3.2f, 2.5f, 2.5f), look) ||
						!Clear(arrivalGround + Vector3.up * 1.4f, new Vector3(1.2f, 1.0f, 1.2f), look))
					{
						continue;
					}
					spot = ground;
					facing = look;
					return true;
				}
			}
			return false;
		}

		/// <summary>The terrain under a point: the first solid thing below must be a terrain, level, and on dry land.</summary>
		private static bool TryTerrain(Vector3 at, SceneBiomeMap map, out Vector3 ground)
		{
			ground = default;
			if (!Physics.Raycast(new Vector3(at.x, at.y + 400f, at.z), Vector3.down, out RaycastHit hit, 1200f, ~0, QueryTriggerInteraction.Ignore) ||
				!(hit.collider is TerrainCollider) || hit.normal.y < 0.96f)
			{
				return false;
			}
			if (map != null && map.Contains(hit.point) &&
				BiomeRegistry.TryGetByID(map.IDAt(hit.point), out BiomeTemplate biome) && biome != null &&
				BiomeHabitats.IsWater(biome.LiveHabitat))
			{
				return false;
			}
			ground = hit.point;
			return true;
		}

		private static Vector3 Ground(Vector3 at)
		{
			return Physics.Raycast(new Vector3(at.x, at.y + 50f, at.z), Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore) ? hit.point : at;
		}

		/// <summary>True when nothing but terrain overlaps the box.</summary>
		private static bool Clear(Vector3 centre, Vector3 halfExtents, Quaternion rotation)
		{
			foreach (Collider c in Physics.OverlapBox(centre, halfExtents, rotation, ~0, QueryTriggerInteraction.Ignore))
			{
				if (!(c is TerrainCollider))
				{
					return false;
				}
			}
			return true;
		}

		private static string GateName(string other) => $"Gate to {other}";
		private static string ArchName(string other) => $"Gate Arch to {other}";
		private static string ArrivalName(string other) => $"Arrival from {other}";

		private static SceneTeleporter FindGate(Scene scene, string other)
		{
			GameObject root = FindRoot(scene);
			Transform child = root != null ? root.transform.Find(GateName(other)) : null;
			return child != null ? child.GetComponent<SceneTeleporter>() : null;
		}

		private static GameObject FindRoot(Scene scene)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.name == RootName)
				{
					return root;
				}
			}
			return null;
		}

		private static GameObject NewRoot(Scene scene)
		{
			var root = new GameObject(RootName);
			SceneManager.MoveGameObjectToScene(root, scene);
			return root;
		}

		private static void RemoveChild(GameObject root, string name, bool unregister = false)
		{
			Transform child = root.transform.Find(name);
			while (child != null)
			{
				if (unregister && child.TryGetComponent(out TeleporterDestination old) && !string.IsNullOrEmpty(old.DestinationID))
				{
					TeleporterCache.UnregisterDestination(old.DestinationID);
				}
				UnityEngine.Object.DestroyImmediate(child.gameObject);
				child = root.transform.Find(name);
			}
		}

		private static T FindComponent<T>(Scene scene) where T : Component
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				T found = root.GetComponentInChildren<T>(true);
				if (found != null)
				{
					return found;
				}
			}
			return null;
		}
	}
}
#endif
