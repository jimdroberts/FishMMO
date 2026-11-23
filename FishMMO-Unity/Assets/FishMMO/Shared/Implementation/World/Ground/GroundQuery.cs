using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>What a ray struck the ground on: whether that is underground (in a cave or a mine), and what the ground is.</summary>
	public struct GroundReading
	{
		/// <summary>True inside a cave or a mine: on its rock, or several metres under the terrain's surface.</summary>
		public bool Underground;

		/// <summary>True when <see cref="Surface"/> is known (a tile with no ground map, or an unnamed collider, says nothing).</summary>
		public bool SurfaceKnown;

		/// <summary>The ground struck: grass, forest floor, rock, snow…</summary>
		public SurfaceKind Surface;
	}

	/// <summary>
	/// Reads the ground a ray struck, on the server as on the client: a terrain tile by its ground (<see cref="TerrainGroundKinds"/>),
	/// a cave's rock as underground stone, any other collider by its names (<see cref="SurfaceNames"/>). Asked by the wildlife
	/// spawners, so a deer is never placed in a cave and a bat never out in a field.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Underground is the shape, not the biome.</b> The biome map has one value per spot on the map, so a cave reads as the forest
	/// on the hill over it, and a spawner's cast down through a cave mouth's terrain hole landed on the cave floor. Two tests, either
	/// enough: the collider struck is a cave's rock (<see cref="CaveRockName"/>, and its pieces under it), or the terrain's
	/// surface stands more than <see cref="UndergroundMetres"/> over the point.
	/// </para>
	/// </remarks>
	public static class GroundQuery
	{
		/// <summary>The name of a cave system's rock object (CaveShaper.Build); its collider pieces are its children.</summary>
		public const string CaveRockName = "Cave Rock";

		/// <summary>A point this far under the terrain's surface is underground, metres.</summary>
		public const float UndergroundMetres = 3f;

		private static readonly Dictionary<SceneHandle, List<Terrain>> terrains = new Dictionary<SceneHandle, List<Terrain>>();
		private static readonly Dictionary<EntityId, bool> caveRock = new Dictionary<EntityId, bool>();

		/// <summary>The reading at a ray's hit in <paramref name="scene"/>.</summary>
		public static GroundReading Read(Scene scene, in RaycastHit hit)
		{
			var reading = new GroundReading();
			Collider collider = hit.collider;
			if (collider == null)
			{
				return reading;
			}
			if (IsCaveRock(collider))
			{
				reading.Underground = true;
				reading.SurfaceKnown = true;
				reading.Surface = SurfaceKind.Stone;
				return reading;
			}
			Terrain over = TerrainAt(scene, hit.point);
			if (over != null && over.SampleHeight(hit.point) + over.GetPosition().y - hit.point.y > UndergroundMetres)
			{
				reading.Underground = true;
			}
			if (collider is TerrainCollider)
			{
				// Not ??: an absent component is Unity's fake null in the editor.
				Terrain struck = collider.TryGetComponent(out Terrain own) ? own : over;
				reading.SurfaceKnown = TerrainGroundKinds.TryRead(struck, hit.point, out reading.Surface);
				return reading;
			}
			// Anything else by its names, a few parents up (a house's floor is a child of the house).
			Transform t = collider.transform;
			for (int depth = 0; depth < 4 && t != null && !reading.SurfaceKnown; depth++, t = t.parent)
			{
				reading.SurfaceKnown = SurfaceNames.TryName(t.name, out reading.Surface);
			}
			return reading;
		}

		/// <summary>True when the collider is a piece of a cave system's rock (or the rock object itself).</summary>
		public static bool IsCaveRock(Collider collider)
		{
			if (collider == null)
			{
				return false;
			}
			EntityId id = collider.GetEntityId();
			if (caveRock.TryGetValue(id, out bool known))
			{
				return known;
			}
			bool rock = false;
			Transform t = collider.transform;
			for (int depth = 0; depth < 3 && t != null && !rock; depth++, t = t.parent)
			{
				rock = t.name == CaveRockName;
			}
			caveRock[id] = rock;
			return rock;
		}

		/// <summary>The scene's terrain tile under a point (x, z), or null.</summary>
		public static Terrain TerrainAt(Scene scene, Vector3 point)
		{
			List<Terrain> tiles = TerrainsOf(scene);
			for (int i = 0; i < tiles.Count; i++)
			{
				Terrain tile = tiles[i];
				TerrainData data = tile.terrainData;
				if (data == null)
				{
					continue;
				}
				Vector3 origin = tile.GetPosition();
				if (point.x >= origin.x && point.x <= origin.x + data.size.x && point.z >= origin.z && point.z <= origin.z + data.size.z)
				{
					return tile;
				}
			}
			return null;
		}

		/// <summary>The scene's terrain tiles, gathered once per scene (again if one has gone).</summary>
		private static List<Terrain> TerrainsOf(Scene scene)
		{
			if (terrains.TryGetValue(scene.handle, out List<Terrain> list))
			{
				bool whole = true;
				for (int i = 0; i < list.Count && whole; i++)
				{
					whole = list[i] != null;
				}
				if (whole)
				{
					return list;
				}
			}
			list = new List<Terrain>();
			if (scene.IsValid() && scene.isLoaded)
			{
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					list.AddRange(root.GetComponentsInChildren<Terrain>(true));
				}
			}
			terrains[scene.handle] = list;
			return list;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetOnPlay()
		{
			terrains.Clear();
			caveRock.Clear();
			SceneManager.sceneUnloaded -= Forget;
			SceneManager.sceneUnloaded += Forget;
		}

		private static void Forget(Scene scene)
		{
			terrains.Remove(scene.handle);
		}
	}
}
