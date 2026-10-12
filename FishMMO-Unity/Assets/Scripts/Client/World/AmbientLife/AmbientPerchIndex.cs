using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Where the birds can perch: the crown tops of the loaded scenes' trees, by cell of the ambient life's grid —
	/// from the baked prop sets (<see cref="ScenePropSet"/>, drawn by CliffRockInstancing) and any trees still held
	/// on the terrains.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A tree is known by its prefab.</b> The generated trees are named <c>Tree_*</c>; anything else counts when
	/// its meshes are tall and narrow (a hand-placed tree), never when it is a rock, a formation or ice. Each prototype
	/// is measured once: its LOD0 meshes' bounds give the crown's top (where a crow sits on a spruce's leader) and the
	/// tree's height.
	/// </para>
	/// <para>
	/// <b>The same perches for everyone.</b> The order the loaded sets are found in is not stable, so each cell's list
	/// is sorted by a hash of the positions, then cut to <see cref="PerCell"/>: two players' birds choose the same trees.
	/// </para>
	/// </remarks>
	public sealed class AmbientPerchIndex
	{
		/// <summary>Perches kept per cell at most.</summary>
		public const int PerCell = 32;

		private static readonly string[] NotTrees = { "Formation", "Boulder", "Iceberg", "IceBoulder", "Serac", "SeaIce", "Rocks", "Cliff", "Rock" };

		private readonly Dictionary<long, List<Vector4>> cells = new Dictionary<long, List<Vector4>>();
		private readonly Dictionary<long, List<AmbientTrunk>> trunkCells = new Dictionary<long, List<AmbientTrunk>>();
		private readonly Dictionary<long, List<(Vector4 Perch, AmbientTrunk Trunk)>> gathering = new Dictionary<long, List<(Vector4, AmbientTrunk)>>();
		private readonly Dictionary<GameObject, AmbientTrunk.Profile> trunkShapes = new Dictionary<GameObject, AmbientTrunk.Profile>();
		private readonly Dictionary<GameObject, Vector4> prototypes = new Dictionary<GameObject, Vector4>();
		private readonly List<Terrain> terrains = new List<Terrain>();
		private static readonly List<Vector4> none = new List<Vector4>();
		private static readonly List<AmbientTrunk> noTrunks = new List<AmbientTrunk>();
		private float cellMetres;

		/// <summary>Trees indexed.</summary>
		public int Count { get; private set; }

		/// <summary>The perches in a cell (x, crown top y, z, tree height): an empty list where there are none. Not to be changed.</summary>
		public List<Vector4> In(int cellX, int cellZ) => cells.TryGetValue(Key(cellX, cellZ), out List<Vector4> list) ? list : none;

		/// <summary>The trunks of the same trees, in the same order as <see cref="In"/>: perch i is the crown of trunk i. Not to be changed.</summary>
		public List<AmbientTrunk> TrunksIn(int cellX, int cellZ) => trunkCells.TryGetValue(Key(cellX, cellZ), out List<AmbientTrunk> list) ? list : noTrunks;

		public static long Key(int x, int z) => ((long)x << 32) | (uint)z;

		/// <summary>Reads every loaded tree into the index, cells of <paramref name="metres"/>.</summary>
		public void Build(float metres)
		{
			cellMetres = metres;
			cells.Clear();
			trunkCells.Clear();
			gathering.Clear();
			Count = 0;
			foreach (SceneProps props in Object.FindObjectsByType<SceneProps>())
			{
				if (props == null || props.Sets == null)
				{
					continue;
				}
				foreach (ScenePropSet set in props.Sets)
				{
					if (set == null || set.Prototypes == null || set.Props == null)
					{
						continue;
					}
					var crowns = new Vector4[set.Prototypes.Length];
					for (int p = 0; p < crowns.Length; p++)
					{
						crowns[p] = Crown(set.Prototypes[p].Prefab, false);
					}
					foreach (ScenePropSet.Prop prop in set.Props)
					{
						if (prop.Prototype < 0 || prop.Prototype >= crowns.Length || crowns[prop.Prototype].w <= 0f)
						{
							continue;
						}
						Vector4 c = crowns[prop.Prototype];
						Vector3 top = prop.Position + prop.Rotation * Vector3.Scale(prop.Scale, new Vector3(c.x, c.y, c.z));
						var trunk = new AmbientTrunk { Base = prop.Position, Rotation = prop.Rotation, Scale = prop.Scale, Shape = TrunkOf(set.Prototypes[prop.Prototype].Prefab, c.w) };
						Add(top, c.w * prop.Scale.y, trunk);
					}
				}
			}
			Terrain.GetActiveTerrains(terrains);
			foreach (Terrain terrain in terrains)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null)
				{
					continue;
				}
				TreePrototype[] protos = data.treePrototypes;
				var crowns = new Vector4[protos.Length];
				for (int p = 0; p < protos.Length; p++)
				{
					crowns[p] = Crown(protos[p].prefab, true);
				}
				Vector3 origin = terrain.transform.position;
				Vector3 size = data.size;
				foreach (TreeInstance tree in data.treeInstances)
				{
					if (tree.prototypeIndex < 0 || tree.prototypeIndex >= crowns.Length || crowns[tree.prototypeIndex].w <= 0f)
					{
						continue;
					}
					Vector4 c = crowns[tree.prototypeIndex];
					Vector3 at = origin + Vector3.Scale(tree.position, size);
					var scale = new Vector3(tree.widthScale, tree.heightScale, tree.widthScale);
					Quaternion turn = Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f);
					Vector3 top = at + turn * Vector3.Scale(scale, new Vector3(c.x, c.y, c.z));
					var trunk = new AmbientTrunk { Base = at, Rotation = turn, Scale = scale, Shape = TrunkOf(protos[tree.prototypeIndex].prefab, c.w) };
					Add(top, c.w * tree.heightScale, trunk);
				}
			}
			foreach (KeyValuePair<long, List<(Vector4 Perch, AmbientTrunk Trunk)>> cell in gathering)
			{
				List<(Vector4 Perch, AmbientTrunk Trunk)> list = cell.Value;
				list.Sort((a, b) => Hash(a.Perch).CompareTo(Hash(b.Perch)));
				int n = Mathf.Min(PerCell, list.Count);
				var perches = new List<Vector4>(n);
				var trunks = new List<AmbientTrunk>(n);
				for (int i = 0; i < n; i++)
				{
					perches.Add(list[i].Perch);
					trunks.Add(list[i].Trunk);
				}
				cells[cell.Key] = perches;
				trunkCells[cell.Key] = trunks;
			}
			gathering.Clear();
		}

		public void Clear()
		{
			cells.Clear();
			trunkCells.Clear();
			gathering.Clear();
			prototypes.Clear();
			trunkShapes.Clear();
			AmbientTrunk.ClearCache();
			Count = 0;
		}

		private void Add(Vector3 top, float height, AmbientTrunk trunk)
		{
			if (height < 2f)
			{
				return;
			}
			long key = Key(Mathf.FloorToInt(top.x / cellMetres), Mathf.FloorToInt(top.z / cellMetres));
			if (!gathering.TryGetValue(key, out List<(Vector4 Perch, AmbientTrunk Trunk)> list))
			{
				gathering[key] = list = new List<(Vector4, AmbientTrunk)>();
			}
			list.Add((new Vector4(top.x, top.y, top.z, height), trunk));
			Count++;
		}

		/// <summary>A prototype's trunk, measured once from its collision mesh (<see cref="AmbientTrunk.Measure(GameObject, float)"/>).</summary>
		private AmbientTrunk.Profile TrunkOf(GameObject prefab, float height)
		{
			if (prefab == null)
			{
				return AmbientTrunk.Profile.Plain(height, Mathf.Clamp(height * 0.025f, 0.12f, 0.5f));
			}
			if (!trunkShapes.TryGetValue(prefab, out AmbientTrunk.Profile shape))
			{
				trunkShapes[prefab] = shape = AmbientTrunk.Measure(prefab, height);
			}
			return shape;
		}

		/// <summary>A stable hash of a perch's position to the centimetre.</summary>
		private static uint Hash(Vector4 p)
		{
			return SeaLifePlacement.Hash(0xBEEFu, Mathf.RoundToInt(p.x * 100f), Mathf.RoundToInt(p.z * 100f), Mathf.RoundToInt(p.y * 10f));
		}

		/// <summary>
		/// A prototype's crown top in its own space (xyz) and its height (w); w = 0 when it is not a tree.
		/// </summary>
		private Vector4 Crown(GameObject prefab, bool terrainTree)
		{
			if (prefab == null)
			{
				return Vector4.zero;
			}
			if (prototypes.TryGetValue(prefab, out Vector4 known))
			{
				return known;
			}
			Vector4 crown = Vector4.zero;
			string name = prefab.name;
			bool named = name.StartsWith("Tree_");
			bool rock = false;
			foreach (string not in NotTrees)
			{
				rock |= name.StartsWith(not);
			}
			if (!rock && TryLocalBounds(prefab, out Bounds b))
			{
				float height = b.max.y;
				float width = Mathf.Max(b.size.x, b.size.z);
				bool tall = height >= (terrainTree ? 2.5f : 4f) && height >= 1.4f * width;
				if (named || terrainTree || tall)
				{
					crown = new Vector4(b.center.x, b.max.y * 0.97f, b.center.z, height);
				}
			}
			prototypes[prefab] = crown;
			return crown;
		}

		/// <summary>The prefab's drawn meshes' bounds in its own space (LOD0 when it has levels).</summary>
		private static bool TryLocalBounds(GameObject prefab, out Bounds bounds)
		{
			bounds = default;
			bool any = false;
			Transform root = prefab.transform;
			LODGroup group = prefab.GetComponent<LODGroup>();
			Renderer[] renderers = null;
			if (group != null)
			{
				LOD[] lods = group.GetLODs();
				if (lods.Length > 0)
				{
					renderers = lods[0].renderers;
				}
			}
			renderers ??= prefab.GetComponentsInChildren<MeshRenderer>(true);
			foreach (Renderer r in renderers)
			{
				if (r == null)
				{
					continue;
				}
				MeshFilter filter = r.GetComponent<MeshFilter>();
				Mesh mesh = filter != null ? filter.sharedMesh : null;
				if (mesh == null)
				{
					continue;
				}
				Matrix4x4 local = Matrix4x4.identity;
				for (Transform t = r.transform; t != null && t != root; t = t.parent)
				{
					local = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * local;
				}
				Bounds part = TerrainTreeMath.TransformBounds(local, mesh.bounds);
				if (any)
				{
					bounds.Encapsulate(part);
				}
				else
				{
					bounds = part;
					any = true;
				}
			}
			return any;
		}
	}
}
