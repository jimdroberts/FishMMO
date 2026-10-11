#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using FishMMO.Shared.Core;
using FishMMO.Shared.NameGeneration;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A feature that needs its site's per-scene unlock index (<see cref="PointOfInterestRecord.UnlockIndex"/>): a
	/// waypoint, a portal, a discoverable site. Sites whose template carries one are given an index (see
	/// <see cref="PointOfInterestGameplayUnlocks"/>).
	/// </summary>
	public interface IPointOfInterestUnlockFeature
	{
	}

	/// <summary>
	/// What the gameplay features share: names that stay unique in a scene, networked scene objects with a stable
	/// SceneId, spawners, and the shipped assets they point at.
	/// </summary>
	public static class PointOfInterestFeatureKit
	{
		public const string WaypointPrefabPath = "Assets/Prefabs/Shared/Entity/Interactables/Waypoints/Waypoint.prefab";
		public const string TeleporterTriggerPath = "Assets/Templates/Entity/ECA/Interactions/Teleporter Interact.asset";
		public const string DungeonEntranceTriggerPath = "Assets/Templates/Entity/ECA/Interactions/Dungeon Entrance Interact.asset";
		public const string RegionNameTriggerPath = "Assets/Prefabs/Shared/Entity/Regions/DisplayName/AreaOfInterestDisplayNameAction.asset";

		/// <summary>The site's display name: its record's, else its kind's.</summary>
		public static string SiteName(PointOfInterestSiteContext context)
		{
			PointOfInterestRecord record = context.Record;
			return string.IsNullOrWhiteSpace(record?.Name) ? PointOfInterestKinds.Info(record != null ? record.Kind : POIType.Landmark).DisplayName : record.Name;
		}

		/// <summary>
		/// A GameObject name unique in the scene: the site's name, what the object is, and the site's id. Teleporters and
		/// respawn points are looked up by name in the world scene details cache, which rejects a duplicate; two sites can
		/// share a generated name, never an id.
		/// </summary>
		public static string UniqueName(PointOfInterestSiteContext context, string what)
		{
			return $"{SiteName(context)} {what} [{(uint)context.Record.Id:x8}]";
		}

		/// <summary>A child of the site at an offset in the site's frame, on the ground, facing along the site's heading plus <paramref name="yaw"/>.</summary>
		public static GameObject AddChildAt(PointOfInterestSiteContext context, string name, Vector2 local, float yaw = 0.0f, float lift = 0.0f)
		{
			GameObject child = context.AddChild(name);
			child.transform.position = context.OnGround(local) + Vector3.up * lift;
			child.transform.rotation = Quaternion.Euler(0.0f, context.Record.Yaw + yaw, 0.0f);
			return child;
		}

		/// <summary>A seeded point in the site's frame within <paramref name="share"/> of its radius.</summary>
		public static Vector2 RandomLocal(PointOfInterestSiteContext context, float share)
		{
			float r = Mathf.Max(0.0f, context.Record.Radius * share) * Mathf.Sqrt(context.Random.NextFloat());
			float a = context.Random.NextFloat() * Mathf.PI * 2.0f;
			return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
		}

		/// <summary>
		/// Gives a scene NetworkObject a SceneId, deterministically: the scene path's hash in the high word, a hash of the
		/// site and <paramref name="salt"/> in the low, stepped past any id the scene already uses.
		/// </summary>
		/// <remarks>
		/// <para>A GameObject made by script in batch mode never gets one — FishNet assigns SceneIds from a throttled
		/// <c>OnValidate</c> — and a scene object without one throws at load (fishmmo-waypoints: the same fix used for the
		/// shipped waypoints, which called FishNet's internal <c>CreateSceneId</c> by reflection). FishNet's own pass
		/// draws from an unseeded <c>System.Random</c>; this uses its public <c>SetSceneId</c> so the same cut writes the
		/// same ids. An object that already has an id keeps it.</para>
		/// </remarks>
		public static void AssignSceneId(NetworkObject networkObject, PointOfInterestSiteContext context, int salt)
		{
			if (networkObject == null || networkObject.IsSceneObject)
			{
				return;
			}
			Scene scene = networkObject.gameObject.scene;
			var used = new HashSet<ulong>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (NetworkObject other in root.GetComponentsInChildren<NetworkObject>(true))
				{
					if (other != networkObject && other.IsSceneObject)
					{
						used.Add(SceneIdOf(other));
					}
				}
			}
			ulong high = (ulong)StableHash.FNV32(scene.path ?? scene.name ?? string.Empty) << 32;
			uint low = PointOfInterestPlanner.Mix(unchecked((uint)context.Record.Id * 0x9E3779B1u ^ (uint)salt));
			ulong id = high | low;
			while (id == NetworkObject.UNSET_SCENEID_VALUE || used.Contains(id))
			{
				id = high | ++low;
			}
			networkObject.SetSceneId(id);
			EditorUtility.SetDirty(networkObject);
		}

		/// <summary>FishNet keeps <c>SceneId</c> internal; only its setter is public.</summary>
		private static readonly System.Reflection.FieldInfo sceneIdField =
			typeof(NetworkObject).GetField("SceneId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

		private static ulong SceneIdOf(NetworkObject networkObject)
		{
			return sceneIdField != null ? (ulong)sceneIdField.GetValue(networkObject) : 0UL;
		}

		/// <summary>Adds a NetworkObject (first, so behaviours added after bind to it) to a new scene object.</summary>
		public static NetworkObject AddNetworkObject(GameObject gameObject)
		{
			NetworkObject networkObject = gameObject.GetComponent<NetworkObject>();
			if (networkObject == null)
			{
				networkObject = gameObject.AddComponent<NetworkObject>();
			}
			return networkObject;
		}

		/// <summary>Sets an interactable's ECA interaction list (a private serialized field).</summary>
		public static void SetInteractTriggers(Interactable interactable, params Trigger[] triggers)
		{
			var serialized = new SerializedObject(interactable);
			SerializedProperty list = serialized.FindProperty("onInteractTriggers");
			list.ClearArray();
			int index = 0;
			foreach (Trigger trigger in triggers)
			{
				if (trigger == null)
				{
					continue;
				}
				list.InsertArrayElementAtIndex(index);
				list.GetArrayElementAtIndex(index).objectReferenceValue = trigger;
				index++;
			}
			serialized.ApplyModifiedPropertiesWithoutUndo();
		}

		/// <summary>
		/// A spawner under the site: an authoring-only object (tagged <c>EditorOnly</c>, no NetworkObject) that the spawn
		/// table baker copies into the scene's table when the scene is saved.
		/// </summary>
		/// <param name="context">The site.</param>
		/// <param name="name">The spawner's object name.</param>
		/// <param name="local">Where, in the site's frame.</param>
		/// <param name="spawnables">What it spawns; empty for a spawner a designer fills.</param>
		/// <param name="count">How many it keeps alive (and spawns at scene start).</param>
		/// <param name="areaRadius">Half the width of the box it scatters in; 0 spawns on the spot.</param>
		public static ObjectSpawner AddSpawner(PointOfInterestSiteContext context, string name, Vector2 local, List<SpawnableSettings> spawnables, int count, float areaRadius)
		{
			GameObject host = AddChildAt(context, name, local);
			host.tag = ObjectSpawner.EditorOnlyTag;
			ObjectSpawner spawner = host.AddComponent<ObjectSpawner>();
			spawner.Spawnables = spawnables ?? new List<SpawnableSettings>();
			spawner.MaxSpawnCount = Mathf.Max(1, count);
			spawner.InitialSpawnCount = Mathf.Max(1, count);
			spawner.SpawnType = spawner.Spawnables.Count > 1 ? ObjectSpawnType.Weighted : ObjectSpawnType.Linear;
			if (areaRadius > 0.0f)
			{
				/* The spawner stands on the ground and its box reaches 4 m above and below it: the random point is cast
				 * down from the box's top for its full height, so the ground is always inside the cast (the YOffset trap
				 * in fishmmo-ai-server-move-decisions). */
				spawner.RandomSpawnPosition = true;
				spawner.BoundingBoxSize = new Vector3(areaRadius * 2.0f, 8.0f, areaRadius * 2.0f);
			}
			else
			{
				spawner.RandomSpawnPosition = false;
				spawner.BoundingBoxSize = new Vector3(1.0f, 8.0f, 1.0f);
			}
			if (spawner.Spawnables.Count == 0)
			{
				// Empty on purpose (a lair's boss, a role with no NPC yet): marked, so the spawn checks list it as a designer's to-do.
				host.AddComponent<PendingSpawnerContent>().Reason = $"Generated empty for {SiteName(context)}: assign what it spawns, then remove this.";
			}
			EditorUtility.SetDirty(spawner);
			return spawner;
		}

		/// <summary>Spawnable entries for weighted NPC choices, normalised so the heaviest weighs 1.</summary>
		/// <param name="choices">The prefabs and weights.</param>
		/// <param name="race">Applied as each NPC's race (its model, faction and name), or null for the prefab's own.</param>
		public static List<SpawnableSettings> NpcSpawnables(IReadOnlyList<PointOfInterestNpcChoice> choices, RaceTemplate race)
		{
			var spawnables = new List<SpawnableSettings>();
			if (choices == null)
			{
				return spawnables;
			}
			float heaviest = 0.0f;
			foreach (PointOfInterestNpcChoice choice in choices)
			{
				heaviest = Mathf.Max(heaviest, choice.Weight);
			}
			foreach (PointOfInterestNpcChoice choice in choices)
			{
				NetworkObject networkObject = choice.Prefab != null ? choice.Prefab.GetComponent<NetworkObject>() : null;
				if (networkObject == null)
				{
					continue;
				}
				spawnables.Add(new NPCSpawnableSettings
				{
					NetworkObject = networkObject,
					SpawnChance = heaviest > 0.0f ? Mathf.Clamp01(choice.Weight / heaviest) : 1.0f,
					FactionOverride = race,
				});
			}
			return spawnables;
		}

		/// <summary>The site's race, resolved from its naming key; null for none.</summary>
		public static RaceTemplate SiteRace(PointOfInterestSiteContext context)
		{
			return PointOfInterestNpcTable.FindRace(context.Record?.Race);
		}

		/// <summary>An asset at a path, noting its absence on the site.</summary>
		public static T Load<T>(PointOfInterestSiteContext context, string path) where T : Object
		{
			T asset = AssetDatabase.LoadAssetAtPath<T>(path);
			if (asset == null)
			{
				Note(context, $"'{path}' is missing.");
			}
			return asset;
		}

		/// <summary>Adds a note naming the site.</summary>
		public static void Note(PointOfInterestSiteContext context, string text)
		{
			context?.Notes?.Add($"{(context.Root != null ? context.Root.name : "site")}: {text}");
		}

		/// <summary>
		/// The site's unlock index, or −1 with a note when the generator gave it none (its template's unlock features
		/// were not seen when indices were assigned).
		/// </summary>
		public static int UnlockIndex(PointOfInterestSiteContext context, string what)
		{
			int index = context.Record != null ? context.Record.UnlockIndex : -1;
			if (!WaypointUnlockMask.IsValidIndex(index))
			{
				Note(context, $"no unlock index, so no {what} was built (PointOfInterestUnlocks.Unlockable did not take this site).");
				return -1;
			}
			return index;
		}
	}
}
#endif
