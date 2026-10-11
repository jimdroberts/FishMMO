using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The trees round the camera as a small map the falling leaves read: how much deciduous and evergreen
	/// crown covers each few metres, how tall it stands and where the ground is.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the trees themselves, and not the biome or the stand field.</b> The biome says what grows in a
	/// region of kilometres, and the forest-stand field (TerrainScatter.StandField) is an editor-time function
	/// the scatter was CUT from — neither says whether there is a tree over this path, and a meadow in a
	/// woodland biome would rain leaves out of an empty sky. The placed trees are already client data: baked
	/// props (<see cref="ScenePropSet"/>, the scatter's trees since the props bake) and whatever still stands on
	/// the terrain's own tree list (Tree/Billboard prefabs it keeps). Each prototype is classified once from its
	/// materials — the vegetation shader's <c>_Deciduous</c> flag, the same one that strips the crown in autumn,
	/// and its tree fade class — so the leaves come off exactly the trees that go bare.
	/// </para>
	/// <para>
	/// <b>Cost.</b> The trees are bucketed into 32 m cells once per scene load (a 40-byte entry each). The map is
	/// 32×32 texels and is redrawn only when the camera has moved two texels, by splatting the crowns of the
	/// few hundred trees in reach — a fraction of a millisecond, a few times a minute at a walk. The GPU reads
	/// one RGBA-half texture per leaf vertex, bilinear.
	/// </para>
	/// </remarks>
	public static class LeafCanopyMap
	{
		/// <summary>Texels across the map.</summary>
		public const int Size = 32;

		/// <summary>The index's cell, m.</summary>
		public const float CellMetres = 32f;

		/// <summary>How far round the camera the leaves' colour is averaged from, m.</summary>
		public const float ColourReach = 40f;

		private static readonly int DeciduousId = Shader.PropertyToID("_Deciduous");
		private static readonly int DistanceFadeId = Shader.PropertyToID("_DistanceFade");
		private static readonly int HealthyId = Shader.PropertyToID("_HealthyColor");
		private static readonly int AutumnId = Shader.PropertyToID("_AutumnColor");
		private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

		/// <summary>What a tree prefab is, as far as its leaves go.</summary>
		public struct Kind
		{
			public bool Tree;
			public bool Deciduous;
			/// <summary>Crown radius and top above the root, m, at scale 1.</summary>
			public float Radius, Top;
			/// <summary>The leaf material's tints, linear.</summary>
			public Color Healthy, Autumn;
		}

		private struct Entry
		{
			public float X, Z, Radius, Top;
			public bool Deciduous;
			public int Kind;
		}

		private static readonly Dictionary<GameObject, int> kindOf = new Dictionary<GameObject, int>();
		private static readonly List<Kind> kinds = new List<Kind>();
		private static readonly List<Entry> entries = new List<Entry>();
		private static readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
		private static bool dirty = true, hooked;
		private static int version;

		private static Texture2D texture;
		private static readonly Color[] pixels = new Color[Size * Size];
		private static readonly float[] deciduousSum = new float[Size * Size];
		private static readonly float[] evergreenSum = new float[Size * Size];
		private static readonly float[] tops = new float[Size * Size];
		private static Vector2 centre = new Vector2(float.NaN, float.NaN);
		private static float texelMetres, builtAt = -1f;
		private static int builtVersion = -1;
		private static readonly List<Terrain> terrains = new List<Terrain>();

		/// <summary>The map: r deciduous crown cover, g evergreen, b crown top above the ground (m), a the ground less <see cref="ReferenceHeight"/> (m).</summary>
		public static Texture2D Texture => texture;
		/// <summary>xy the map's world x/z minimum, z one over its size; w <see cref="ReferenceHeight"/>.</summary>
		public static Vector4 Rect { get; private set; }
		/// <summary>The height the ground in the map is stored against, m: the half texture keeps centimetres near it.</summary>
		public static float ReferenceHeight { get; private set; }
		/// <summary>Whether any crown at all covers the map.</summary>
		public static bool AnyCanopy { get; private set; }
		/// <summary>The deciduous trees' tints near the camera, linear (white and the shader's default autumn when none).</summary>
		public static Color Healthy { get; private set; } = Color.white;
		public static Color Autumn { get; private set; } = new Color(1.6f, 0.8f, 0.3f, 1f).linear;
		/// <summary>How many trees the index holds.</summary>
		public static int TreeCount => entries.Count;

		/// <summary>Brings the map round a camera up to date; false when no trees cover it.</summary>
		public static bool Update(Vector3 camera, float metresPerTexel)
		{
			Hook();
			if (dirty)
			{
				dirty = false;
				Rebuild();
			}
			metresPerTexel = Mathf.Max(1f, metresPerTexel);
			float now = Time.realtimeSinceStartup;
			Vector2 here = new Vector2(camera.x, camera.z);
			bool moved = float.IsNaN(centre.x) || (here - centre).sqrMagnitude > 4f * metresPerTexel * metresPerTexel;
			if (moved || builtVersion != version || !Mathf.Approximately(texelMetres, metresPerTexel) || texture == null || now - builtAt > 30f)
			{
				// Snapped to the texel grid, so the map's texels stay put in the world as it moves.
				centre = new Vector2(Mathf.Round(here.x / metresPerTexel) * metresPerTexel, Mathf.Round(here.y / metresPerTexel) * metresPerTexel);
				texelMetres = metresPerTexel;
				builtVersion = version;
				builtAt = now;
				Draw(camera.y);
			}
			return AnyCanopy;
		}

		private static void Hook()
		{
			if (hooked)
			{
				return;
			}
			hooked = true;
			SceneManager.sceneLoaded += (s, m) => dirty = true;
			SceneManager.sceneUnloaded += s => dirty = true;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			dirty = true;
			centre = new Vector2(float.NaN, float.NaN);
			kindOf.Clear();
			kinds.Clear();
			entries.Clear();
			cells.Clear();
			// Before the first scene loads, edit mode or not: only the immediate form is allowed everywhere.
			if (texture != null)
			{
				Object.DestroyImmediate(texture);
			}
			texture = null;
		}

		/// <summary>Lets go of the texture (play mode ending, the presenter shutting down).</summary>
		public static void Release()
		{
			if (texture != null)
			{
				if (Application.isPlaying)
				{
					Object.Destroy(texture);
				}
				else
				{
					Object.DestroyImmediate(texture);
				}
				texture = null;
			}
			centre = new Vector2(float.NaN, float.NaN);
		}

		// ── The index ──────────────────────────────────────────────────

		private static void Rebuild()
		{
			entries.Clear();
			cells.Clear();
			kindOf.Clear();
			kinds.Clear();
			version++;
			foreach (SceneProps props in Object.FindObjectsByType<SceneProps>())
			{
				if (props == null)
				{
					continue;
				}
				foreach (ScenePropSet set in props.Sets)
				{
					if (set == null || set.Prototypes == null || set.Props == null)
					{
						continue;
					}
					var local = new int[set.Prototypes.Length];
					for (int i = 0; i < local.Length; i++)
					{
						local[i] = KindIndex(set.Prototypes[i].Prefab);
					}
					foreach (ScenePropSet.Prop prop in set.Props)
					{
						if (prop.Prototype < 0 || prop.Prototype >= local.Length)
						{
							continue;
						}
						Add(local[prop.Prototype], prop.Position, Mathf.Max(Mathf.Abs(prop.Scale.x), Mathf.Abs(prop.Scale.z)), Mathf.Abs(prop.Scale.y));
					}
				}
			}
			// Trees still on the terrains' own lists: not every scene has been baked, and the bake leaves some.
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				if (terrain == null || terrain.terrainData == null)
				{
					continue;
				}
				TerrainData data = terrain.terrainData;
				TreePrototype[] prototypes = data.treePrototypes;
				if (prototypes == null || prototypes.Length == 0)
				{
					continue;
				}
				var local = new int[prototypes.Length];
				for (int i = 0; i < local.Length; i++)
				{
					local[i] = KindIndex(prototypes[i].prefab);
				}
				Vector3 origin = terrain.GetPosition();
				Vector3 size = data.size;
				foreach (TreeInstance tree in data.treeInstances)
				{
					if (tree.prototypeIndex < 0 || tree.prototypeIndex >= local.Length)
					{
						continue;
					}
					Vector3 at = origin + Vector3.Scale(tree.position, size);
					Add(local[tree.prototypeIndex], at, tree.widthScale, tree.heightScale);
				}
			}
		}

		private static void Add(int kind, Vector3 at, float widthScale, float heightScale)
		{
			if (kind < 0)
			{
				return;
			}
			Kind k = kinds[kind];
			var entry = new Entry
			{
				X = at.x,
				Z = at.z,
				Radius = Mathf.Max(0.5f, k.Radius * widthScale),
				Top = Mathf.Max(1f, k.Top * heightScale),
				Deciduous = k.Deciduous,
				Kind = kind,
			};
			long key = CellKey(at.x, at.z);
			if (!cells.TryGetValue(key, out List<int> list))
			{
				list = new List<int>();
				cells.Add(key, list);
			}
			list.Add(entries.Count);
			entries.Add(entry);
		}

		private static long CellKey(float x, float z) => CellKey(Mathf.FloorToInt(x / CellMetres), Mathf.FloorToInt(z / CellMetres));

		private static long CellKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

		/// <summary>The index of a prefab's kind, or −1 when it is not a tree with leaves.</summary>
		private static int KindIndex(GameObject prefab)
		{
			if (prefab == null)
			{
				return -1;
			}
			if (kindOf.TryGetValue(prefab, out int index))
			{
				return index;
			}
			Kind kind = Classify(prefab);
			index = kind.Tree ? kinds.Count : -1;
			if (kind.Tree)
			{
				kinds.Add(kind);
			}
			kindOf.Add(prefab, index);
			return index;
		}

		/// <summary>
		/// What a prefab is: a tree when it wears the vegetation shader (it has <c>_Deciduous</c>) in the tree fade
		/// class or stands taller than 3 m; deciduous when any of its materials is; its crown from its first
		/// level's meshes.
		/// </summary>
		public static Kind Classify(GameObject prefab)
		{
			var kind = new Kind { Healthy = Color.white, Autumn = new Color(1.6f, 0.8f, 0.3f, 1f).linear };
			if (prefab == null)
			{
				return kind;
			}
			Renderer[] renderers = null;
			LODGroup group = prefab.GetComponentInChildren<LODGroup>(true);
			if (group != null)
			{
				LOD[] lods = group.GetLODs();
				if (lods.Length > 0)
				{
					renderers = lods[0].renderers;
				}
			}
			if (renderers == null || renderers.Length == 0)
			{
				renderers = prefab.GetComponentsInChildren<Renderer>(true);
			}

			bool vegetation = false, treeFade = false, haveBounds = false;
			Bounds bounds = default;
			Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
			foreach (Renderer renderer in renderers)
			{
				if (renderer == null)
				{
					continue;
				}
				foreach (Material material in renderer.sharedMaterials)
				{
					if (material == null || !material.HasProperty(DeciduousId))
					{
						continue;
					}
					vegetation = true;
					if (material.HasProperty(DistanceFadeId) && material.GetFloat(DistanceFadeId) > 1.5f)
					{
						treeFade = true;
					}
					if (material.GetFloat(DeciduousId) > 0.5f && !kind.Deciduous)
					{
						kind.Deciduous = true;
						Color healthy = material.HasProperty(HealthyId) ? material.GetColor(HealthyId).linear : Color.white;
						Color tint = material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId).linear : Color.white;
						kind.Healthy = healthy * tint;
						if (material.HasProperty(AutumnId))
						{
							kind.Autumn = material.GetColor(AutumnId).linear;
						}
					}
				}
				MeshFilter filter = renderer.GetComponent<MeshFilter>();
				if (filter != null && filter.sharedMesh != null)
				{
					Bounds b = Transform(filter.sharedMesh.bounds, toRoot * renderer.transform.localToWorldMatrix);
					if (haveBounds)
					{
						bounds.Encapsulate(b);
					}
					else
					{
						bounds = b;
						haveBounds = true;
					}
				}
			}
			if (!vegetation || !haveBounds)
			{
				return kind;
			}
			kind.Top = bounds.max.y;
			kind.Radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
			kind.Tree = kind.Top >= 2f && (treeFade || kind.Top >= 3f);
			return kind;
		}

		private static Bounds Transform(Bounds local, Matrix4x4 m)
		{
			Vector3 c = m.MultiplyPoint3x4(local.center);
			Vector3 e = local.extents;
			Vector3 x = m.MultiplyVector(new Vector3(e.x, 0f, 0f));
			Vector3 y = m.MultiplyVector(new Vector3(0f, e.y, 0f));
			Vector3 z = m.MultiplyVector(new Vector3(0f, 0f, e.z));
			Vector3 extent = new Vector3(
				Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
				Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
				Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
			return new Bounds(c, extent * 2f);
		}

		// ── The map ────────────────────────────────────────────────────

		/// <summary>
		/// How much of a texel a crown covers by its distance from the trunk: 1 − (d/r)², the projected area of a
		/// rounded crown thinning to its edge.
		/// </summary>
		public static float CrownWeight(float distance, float radius)
		{
			float s = distance / Mathf.Max(0.01f, radius);
			return s >= 1f ? 0f : 1f - s * s;
		}

		/// <summary>
		/// The cover of a texel under overlapping crowns, 0..1: crowns overlap at random, so the open share is the
		/// product of each one's (Beer–Lambert over the summed weights).
		/// </summary>
		public static float Cover(float summedWeight) => 1f - Mathf.Exp(-1.2f * Mathf.Max(0f, summedWeight));

		private static void Draw(float cameraHeight)
		{
			if (texture == null)
			{
				texture = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, true)
				{
					name = "Leaf Canopy Map",
					wrapMode = TextureWrapMode.Clamp,
					filterMode = FilterMode.Bilinear,
					hideFlags = HideFlags.DontSave,
				};
			}
			float span = Size * texelMetres;
			Vector2 min = centre - new Vector2(span * 0.5f, span * 0.5f);
			System.Array.Clear(deciduousSum, 0, deciduousSum.Length);
			System.Array.Clear(evergreenSum, 0, evergreenSum.Length);
			System.Array.Clear(tops, 0, tops.Length);

			Color healthy = Color.clear, autumn = Color.clear;
			float colourWeight = 0f;
			const float Reach = 16f;   // the widest crown, past the map's edge
			int c0x = Mathf.FloorToInt((min.x - Reach) / CellMetres), c1x = Mathf.FloorToInt((min.x + span + Reach) / CellMetres);
			int c0z = Mathf.FloorToInt((min.y - Reach) / CellMetres), c1z = Mathf.FloorToInt((min.y + span + Reach) / CellMetres);
			for (int cz = c0z; cz <= c1z; cz++)
			{
				for (int cx = c0x; cx <= c1x; cx++)
				{
					if (!cells.TryGetValue(CellKey(cx, cz), out List<int> list))
					{
						continue;
					}
					foreach (int i in list)
					{
						Entry tree = entries[i];
						Splat(tree, min);
						if (tree.Deciduous)
						{
							float d = Vector2.Distance(new Vector2(tree.X, tree.Z), centre);
							if (d < ColourReach)
							{
								Kind k = kinds[tree.Kind];
								healthy += k.Healthy;
								autumn += k.Autumn;
								colourWeight += 1f;
							}
						}
					}
				}
			}
			if (colourWeight > 0f)
			{
				Healthy = healthy / colourWeight;
				Autumn = autumn / colourWeight;
			}

			// The ground under each texel, from the terrains, against a reference that keeps the half floats fine.
			terrains.Clear();
			Terrain.GetActiveTerrains(terrains);
			ReferenceHeight = Mathf.Round(cameraHeight);
			bool any = false;
			for (int z = 0; z < Size; z++)
			{
				for (int x = 0; x < Size; x++)
				{
					int t = z * Size + x;
					float wx = min.x + (x + 0.5f) * texelMetres, wz = min.y + (z + 0.5f) * texelMetres;
					float dec = Cover(deciduousSum[t]), ever = Cover(evergreenSum[t]);
					any |= dec > 0.02f || ever > 0.02f;
					pixels[t] = new Color(dec, ever, tops[t], GroundAt(wx, wz, cameraHeight) - ReferenceHeight);
				}
			}
			AnyCanopy = any;
			texture.SetPixels(pixels);
			texture.Apply(false, false);
			Rect = new Vector4(min.x, min.y, 1f / span, ReferenceHeight);
		}

		private static void Splat(in Entry tree, Vector2 min)
		{
			float r = tree.Radius;
			int x0 = Mathf.Max(0, Mathf.FloorToInt((tree.X - r - min.x) / texelMetres));
			int x1 = Mathf.Min(Size - 1, Mathf.FloorToInt((tree.X + r - min.x) / texelMetres));
			int z0 = Mathf.Max(0, Mathf.FloorToInt((tree.Z - r - min.y) / texelMetres));
			int z1 = Mathf.Min(Size - 1, Mathf.FloorToInt((tree.Z + r - min.y) / texelMetres));
			// A crown smaller than a texel still covers its share of it: spread over at least one texel's radius.
			float spread = Mathf.Max(r, 0.6f * texelMetres);
			float area = (r * r) / (spread * spread);
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					float wx = min.x + (x + 0.5f) * texelMetres, wz = min.y + (z + 0.5f) * texelMetres;
					float w = CrownWeight(Mathf.Sqrt((wx - tree.X) * (wx - tree.X) + (wz - tree.Z) * (wz - tree.Z)), spread) * area;
					if (w <= 0f)
					{
						continue;
					}
					int t = z * Size + x;
					if (tree.Deciduous)
					{
						deciduousSum[t] += w;
					}
					else
					{
						evergreenSum[t] += w;
					}
					tops[t] = Mathf.Max(tops[t], tree.Top);
				}
			}
		}

		private static float GroundAt(float x, float z, float fallback)
		{
			float height = float.NegativeInfinity;
			foreach (Terrain terrain in terrains)
			{
				if (terrain == null || terrain.terrainData == null)
				{
					continue;
				}
				Vector3 origin = terrain.GetPosition();
				Vector3 size = terrain.terrainData.size;
				if (x < origin.x || z < origin.z || x > origin.x + size.x || z > origin.z + size.z)
				{
					continue;
				}
				height = Mathf.Max(height, terrain.SampleHeight(new Vector3(x, 0f, z)) + origin.y);
			}
			return float.IsNegativeInfinity(height) ? fallback - 2f : height;
		}
	}
}
