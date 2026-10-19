#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Bakes a scene's <see cref="SceneShelterMap"/> from its collision: every collidable prop's mesh rasterised from above
	/// into the highest surface over each cell, against the terrain there.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A cell is sheltered where the highest collision over it stands clear of the ground (a roof, a slab, a bridge deck),
	/// or where collision reaches well below the ground and the terrain is not holed there: a cave's or a mine's halls,
	/// whose roof is the hillside. Trees are left out: their collision is the trunk, which keeps no rain off.
	/// </para>
	/// <para>Run by the cut once its props are baked (after <c>BakePropsAndNavMesh</c>), and by hand for a scene cut before it existed.</para>
	/// </remarks>
	public static class SceneShelterBaker
	{
		/// <summary>How far above the ground the highest collision must stand to count as something over it, metres.</summary>
		public const float OverGroundMetres = 0.3f;

		/// <summary>How far below the ground collision must reach for the hillside to be a roof over it, metres.</summary>
		public const float UndergroundMetres = 1.5f;

		/// <summary>Prop sources whose collision keeps no rain off (a tree's is its trunk).</summary>
		private static readonly HashSet<string> Ignored = new HashSet<string> { ScenePropBaker.ScatterSource };

		/// <summary>The highest and lowest collision over each cell of a chunk; NaN where none.</summary>
		private sealed class Chunk
		{
			public readonly float[] High = Filled(float.NaN);
			public readonly float[] Low = Filled(float.NaN);

			private static float[] Filled(float value)
			{
				var a = new float[SceneShelterMap.ChunkCells * SceneShelterMap.ChunkCells];
				Array.Fill(a, value);
				return a;
			}
		}

		/// <summary>
		/// Bakes the shelter of an open scene and writes it to <paramref name="path"/> (rewritten in place when it exists, so
		/// its GUID and every reference to it survive a re-bake). Null when nothing in the scene shelters anything.
		/// </summary>
		/// <param name="ground">Scene metres above sea level of the terrain at (east, north).</param>
		/// <param name="hole">True where the terrain is holed at (east, north): a cave's mouth is open sky. Null for none.</param>
		public static SceneShelterMap Bake(Scene scene, Func<float, float, float> ground, Func<float, float, bool> hole, string path, List<string> notes)
		{
			var clock = Stopwatch.StartNew();
			var chunks = new Dictionary<int, Chunk>();
			var meshes = new Dictionary<Mesh, (Vector3[] vertices, int[] triangles)>();
			int props = 0, unreadable = 0;
			foreach (ScenePropCollisionSet set in SetsOf(scene))
			{
				if (set == null || Ignored.Contains(set.Source) || set.Prototypes == null || set.Instances == null)
				{
					continue;
				}
				foreach (ScenePropCollisionSet.Instance instance in set.Instances)
				{
					if (instance.Prototype < 0 || instance.Prototype >= set.Prototypes.Length)
					{
						continue;
					}
					Mesh mesh = set.Prototypes[instance.Prototype].Mesh;
					if (mesh == null)
					{
						continue;
					}
					if (!meshes.TryGetValue(mesh, out (Vector3[] vertices, int[] triangles) data))
					{
						data = (mesh.vertices, mesh.triangles);
						meshes[mesh] = data;
						if (data.vertices.Length == 0)
						{
							unreadable++;
						}
					}
					if (data.vertices.Length == 0)
					{
						continue;
					}
					Rasterise(chunks, data.vertices, data.triangles, Matrix4x4.TRS(instance.Position, instance.Rotation, instance.Scale));
					props++;
				}
			}

			var keys = new List<int>();
			var heights = new List<ushort>();
			int cells = 0;
			int n = SceneShelterMap.ChunkCells;
			var row = new ushort[n * n];
			foreach (KeyValuePair<int, Chunk> pair in chunks)
			{
				int chunkX = (short)(pair.Key & 0xFFFF), chunkZ = pair.Key >> 16;
				bool any = false;
				for (int cell = 0; cell < n * n; cell++)
				{
					row[cell] = SceneShelterMap.Open;
					float high = pair.Value.High[cell];
					if (float.IsNaN(high))
					{
						continue;
					}
					float east = (chunkX * n + cell % n + 0.5f) * SceneShelterMap.CellMetres;
					float north = (chunkZ * n + cell / n + 0.5f) * SceneShelterMap.CellMetres;
					float terrain = ground(east, north);
					if (float.IsInfinity(terrain) || float.IsNaN(terrain))
					{
						continue;
					}
					bool underground = pair.Value.Low[cell] < terrain - UndergroundMetres && (hole == null || !hole(east, north));
					float roof = underground ? Mathf.Max(terrain, high) : high;
					if (!underground && roof < terrain + OverGroundMetres)
					{
						continue;
					}
					row[cell] = PlanetClimateTableData.ToHalf(roof);
					any = true;
					cells++;
				}
				if (any)
				{
					keys.Add(pair.Key);
					heights.AddRange(row);
				}
			}

			var map = AssetDatabase.LoadAssetAtPath<SceneShelterMap>(path);
			if (keys.Count == 0)
			{
				notes?.Add($"Shelter: nothing in the scene keeps the rain off ({props:N0} collidable props looked at).");
				return map != null ? Overwrite(map, keys, heights) : null;
			}
			if (map != null)
			{
				Overwrite(map, keys, heights);
			}
			else
			{
				map = ScriptableObject.CreateInstance<SceneShelterMap>();
				map.Set(keys.ToArray(), heights.ToArray());
				AssetDatabase.CreateAsset(map, path);
			}
			notes?.Add($"Shelter: {cells:N0} m² under cover in {keys.Count} chunk(s), from {props:N0} collidable props, in {clock.Elapsed.TotalSeconds:0.0} s"
				+ (unreadable > 0 ? $"; {unreadable} collision mesh(es) could not be read and keep no rain off." : "."));
			return map;
		}

		/// <summary>
		/// Bakes the shelter of the open world scene from what it already holds, and puts it on its atlas entry: for a scene
		/// cut before shelter maps existed, without cutting it again.
		/// </summary>
		[DashboardTool(DashboardToolAttribute.WorldSceneDetails, "Bake shelter map (open scene)", Section = "Weather", Order = 10,
			Tooltip = "Rasterises the open scene's prop collision from above into where it keeps the rain off (roofs, rock shelters, bridges, caves) and assigns it to the scene's World Atlas entry. Fires stay lit under it. A re-cut does this itself.")]
		public static void BakeOpenScene()
		{
			Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
			WorldSceneSettings settings = null;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				settings = root.GetComponentInChildren<WorldSceneSettings>(true);
				if (settings != null)
				{
					break;
				}
			}
			WorldAtlasScene entry = settings != null ? settings.AtlasEntry : null;
			if (entry == null)
			{
				Debug.LogWarning($"[Shelter] '{scene.name}' has no World Atlas entry to hold a shelter map; open a world scene.");
				return;
			}
			var terrains = new List<Terrain>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				terrains.AddRange(root.GetComponentsInChildren<Terrain>(true));
			}
			terrains.RemoveAll(t => t == null || t.terrainData == null);
			if (terrains.Count == 0)
			{
				Debug.LogWarning($"[Shelter] '{scene.name}' has no terrain.");
				return;
			}
			var holes = new Dictionary<Terrain, bool[,]>();
			Terrain TileAt(float east, float north)
			{
				foreach (Terrain t in terrains)
				{
					Vector3 o = t.GetPosition();
					Vector3 size = t.terrainData.size;
					if (east >= o.x && east <= o.x + size.x && north >= o.z && north <= o.z + size.z)
					{
						return t;
					}
				}
				return null;
			}
			float Ground(float east, float north)
			{
				Terrain t = TileAt(east, north);
				return t != null ? t.SampleHeight(new Vector3(east, 0f, north)) + t.GetPosition().y : float.NegativeInfinity;
			}
			bool Hole(float east, float north)
			{
				Terrain t = TileAt(east, north);
				if (t == null)
				{
					return false;
				}
				TerrainData data = t.terrainData;
				if (!holes.TryGetValue(t, out bool[,] solid))
				{
					solid = data.GetHoles(0, 0, data.holesResolution, data.holesResolution);
					holes[t] = solid;
				}
				Vector3 o = t.GetPosition();
				int res = data.holesResolution;
				int ix = Mathf.Clamp((int)((east - o.x) / data.size.x * res), 0, res - 1);
				int iz = Mathf.Clamp((int)((north - o.z) / data.size.z * res), 0, res - 1);
				return !solid[iz, ix];
			}
			string path = entry.Shelter != null ? AssetDatabase.GetAssetPath(entry.Shelter) : null;
			if (string.IsNullOrEmpty(path))
			{
				string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(terrains[0].terrainData)).Replace('\\', '/');
				path = $"{folder}/{WorldEditorAssets.Sanitize(scene.name)} Shelter.asset";
			}
			var notes = new List<string>();
			SceneShelterMap map = Bake(scene, Ground, Hole, path, notes);
			entry.Shelter = map;
			EditorUtility.SetDirty(entry);
			AssetDatabase.SaveAssets();
			Debug.Log($"[Shelter] '{scene.name}': {string.Join(" ", notes)}" + (map != null ? $" Written to {path}." : " Nothing written."));
		}

		private static SceneShelterMap Overwrite(SceneShelterMap map, List<int> keys, List<ushort> heights)
		{
			map.Set(keys.ToArray(), heights.ToArray());
			EditorUtility.SetDirty(map);
			return map;
		}

		/// <summary>The collision sets the scene's prop colliders hold.</summary>
		private static IEnumerable<ScenePropCollisionSet> SetsOf(Scene scene)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (ScenePropColliders colliders in root.GetComponentsInChildren<ScenePropColliders>(true))
				{
					foreach (ScenePropCollisionSet set in colliders.Sets)
					{
						yield return set;
					}
				}
			}
		}

		/// <summary>One mesh's triangles into the chunks: each cell whose centre a triangle covers from above takes its height there, and each cell a corner lies in takes the corner's.</summary>
		private static void Rasterise(Dictionary<int, Chunk> chunks, Vector3[] vertices, int[] triangles, Matrix4x4 transform)
		{
			float cellMetres = SceneShelterMap.CellMetres;
			for (int t = 0; t + 2 < triangles.Length; t += 3)
			{
				Vector3 a = transform.MultiplyPoint3x4(vertices[triangles[t]]);
				Vector3 b = transform.MultiplyPoint3x4(vertices[triangles[t + 1]]);
				Vector3 c = transform.MultiplyPoint3x4(vertices[triangles[t + 2]]);
				// The corners themselves, so a sliver or an upright face too thin to cover a centre still marks its cells.
				Put(chunks, a.x, a.z, a.y);
				Put(chunks, b.x, b.z, b.y);
				Put(chunks, c.x, c.z, c.y);
				float area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z);
				if (Mathf.Abs(area) < 1e-6f)
				{
					continue;
				}
				int x0 = Mathf.CeilToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / cellMetres - 0.5f);
				int x1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / cellMetres - 0.5f);
				int z0 = Mathf.CeilToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) / cellMetres - 0.5f);
				int z1 = Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) / cellMetres - 0.5f);
				float inverse = 1f / area;
				for (int cz = z0; cz <= z1; cz++)
				{
					float pz = (cz + 0.5f) * cellMetres;
					for (int cx = x0; cx <= x1; cx++)
					{
						float px = (cx + 0.5f) * cellMetres;
						float u = ((b.x - px) * (c.z - pz) - (c.x - px) * (b.z - pz)) * inverse;
						float v = ((c.x - px) * (a.z - pz) - (a.x - px) * (c.z - pz)) * inverse;
						float w = 1f - u - v;
						if (u < 0f || v < 0f || w < 0f)
						{
							continue;
						}
						Put(chunks, px, pz, u * a.y + v * b.y + w * c.y);
					}
				}
			}
		}

		private static void Put(Dictionary<int, Chunk> chunks, float x, float z, float y)
		{
			SceneShelterMap.Locate(x, z, out int chunkX, out int chunkZ, out int cell);
			int key = SceneShelterMap.KeyOf(chunkX, chunkZ);
			if (!chunks.TryGetValue(key, out Chunk chunk))
			{
				chunk = new Chunk();
				chunks[key] = chunk;
			}
			float high = chunk.High[cell];
			chunk.High[cell] = float.IsNaN(high) ? y : Mathf.Max(high, y);
			float low = chunk.Low[cell];
			chunk.Low[cell] = float.IsNaN(low) ? y : Mathf.Min(low, y);
		}
	}
}
#endif
