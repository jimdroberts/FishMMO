using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Water
{
	/// <summary>
	/// The lava along a scene's molten landform lines and in its lava moats (2026-10-25, phase I2r "molten ground", decision 65 (c)
	/// and 95): a ribbon of FishMMO/Water/Inland Lava along each stored lava line (<see cref="SceneBiomeMap.LavaLines"/>: a lava
	/// channel, an ʻaʻā tongue's channel, a breakout, an ember rill) and round the floor of each moat its site's biome fills with lava
	/// (the moat shape's lava flag), laid on the carved ground (<see cref="BiomeLavaChannels.Ribbon"/>). Client only, presentation
	/// only: the same ribbons for every player, the lava's own surface on its own clock; nothing collides with it.
	/// </summary>
	/// <remarks>
	/// Put on a client-only root by the cut when the scene stores a lava line or a biome on it has a lava moat; built when enabled.
	/// The material is the inland lava's (the lava tubes' rivers), so the ribbons read as those rivers do: plates drawn out by the
	/// current, glowing seams, the eye's adaptation.
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class LavaRibbonRenderer : MonoBehaviour
	{
		/// <summary>The scene root the cut puts this on.</summary>
		public const string ObjectName = "Lava Ribbons";

		/// <summary>How fast a channel's lava runs along its line, m/s (a lava tube river's pace).</summary>
		public const float ChannelSpeed = 0.3f;
		/// <summary>How fast a moat's lava creeps round its ring, m/s.</summary>
		public const float MoatSpeed = 0.05f;
		/// <summary>The moat shaper's name on a stored shape (MoatShaper.ShaperName).</summary>
		public const string MoatShaperName = "Moat";

		[Tooltip("The lava ribbons' material (FishMMO/Water/Inland Lava). Without it nothing is drawn.")]
		public Material LavaMaterial;
		[Tooltip("The scene's biome map, whose stored lava lines the ribbons follow.")]
		public SceneBiomeMap Map;

		private readonly List<GameObject> built = new List<GameObject>();
		private readonly List<Terrain> terrains = new List<Terrain>();

		private bool started;

		// Built in Start the first time, after every root's OnEnable, so the scene's terrains are active to lay the ribbons on.
		private void Start()
		{
			started = true;
#if !UNITY_SERVER
			Build();
#endif
		}

		private void OnEnable()
		{
#if !UNITY_SERVER
			if (started)
			{
				Build();
			}
#endif
		}

		private void OnDisable()
		{
			Clear();
		}

		/// <summary>Builds the ribbons again (after a repaint in the editor).</summary>
		[ContextMenu("Rebuild Lava Ribbons")]
		public void Build()
		{
			Clear();
			if (LavaMaterial == null)
			{
				return;
			}
			Scene scene = gameObject.scene;
			terrains.Clear();
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				if (terrain != null && terrain.terrainData != null && (!scene.IsValid() || terrain.gameObject.scene == scene))
				{
					terrains.Add(terrain);
				}
			}
			if (terrains.Count == 0)
			{
				return;
			}
			var groups = new SortedDictionary<uint, List<VentLine>>();
			if (Map != null && Map.LavaLines != null)
			{
				foreach (VentLine line in Map.LavaLines)
				{
					Add(groups, line);
				}
			}
			var moats = new List<VentLine>();
			MoatLines(scene, moats);
			foreach (KeyValuePair<uint, List<VentLine>> group in groups)
			{
				AddRibbon($"Lava Channel {group.Key:X8}", group.Value, ChannelSpeed, scene);
			}
			if (moats.Count > 0)
			{
				AddRibbon("Lava Moats", moats, MoatSpeed, scene);
			}
		}

		private static void Add(SortedDictionary<uint, List<VentLine>> groups, VentLine line)
		{
			if (!groups.TryGetValue(line.Seed, out List<VentLine> list))
			{
				list = new List<VentLine>();
				groups[line.Seed] = list;
			}
			list.Add(line);
		}

		/// <summary>The rings of the scene's lava moats (their stored shapes, flagged lava by the moat shaper).</summary>
		private static void MoatLines(Scene scene, List<VentLine> into)
		{
			if (!scene.IsValid())
			{
				return;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				ScenePointOfInterestSettings settings = root.GetComponentInChildren<ScenePointOfInterestSettings>(true);
				if (settings == null || settings.Points == null || settings.Points.Shapes == null)
				{
					continue;
				}
				foreach (PointOfInterestShape shape in settings.Points.Shapes)
				{
					if (shape == null || shape.Shaper != MoatShaperName || shape.Values == null || shape.Values.Length < 5
						|| Mathf.Abs(shape.Values[4] - BiomeLavaChannels.MoatLavaFlag) > 0.01f)
					{
						continue;
					}
					float[] inner = shape.Values.Length > 5 && shape.Values[0] >= 2f ? Slice(shape.Values, 5) : System.Array.Empty<float>();
					into.AddRange(BiomeLavaChannels.MoatLines(shape.Position, inner, shape.Values[1], shape.Values[2], unchecked((uint)shape.SiteId), 0));
				}
			}
		}

		private static float[] Slice(float[] values, int from)
		{
			var slice = new float[values.Length - from];
			System.Array.Copy(values, from, slice, 0, slice.Length);
			return slice;
		}

		/// <summary>One mesh for a set of lines: each its ribbon on the ground, the inland lava's vertex streams (FishLavaPasses.hlsl).</summary>
		private void AddRibbon(string name, List<VentLine> lines, float speed, Scene scene)
		{
			var positions = new List<Vector3>();
			var frames = new List<Vector2>();
			var indices = new List<int>();
			var halves = new List<float>();
			foreach (VentLine line in lines)
			{
				int before = positions.Count;
				if (BiomeLavaChannels.Ribbon(line, GroundAt, positions, frames, indices))
				{
					for (int i = before; i < positions.Count; i++)
					{
						halves.Add(line.Half);
					}
				}
			}
			if (indices.Count == 0)
			{
				return;
			}
			var sky = new List<Vector2>(positions.Count);
			var kinds = new List<Vector2>(positions.Count);
			var colours = new List<Color32>(positions.Count);
			for (int i = 0; i < positions.Count; i++)
			{
				sky.Add(new Vector2(InlandWaterRenderer.OpenSky(scene, positions[i]), InlandWaterRenderer.RiverPlateScale(2f * halves[i])));
				kinds.Add(new Vector2(InlandWaterRenderer.RiverKind, speed));
				colours.Add(new Color32(255, 255, 255, 255));
			}
			var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
			mesh.indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			mesh.SetVertices(positions);
			mesh.SetUVs(0, frames);
			mesh.SetUVs(1, new List<Vector2>(new Vector2[positions.Count]));
			mesh.SetUVs(6, sky);
			mesh.SetUVs(7, kinds);
			mesh.SetColors(colours);
			mesh.SetTriangles(indices, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
			go.transform.SetParent(transform, false);
			go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
			go.transform.localScale = Vector3.one;
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = LavaMaterial;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = true;
			built.Add(go);
		}

		private float GroundAt(float x, float z)
		{
			foreach (Terrain t in terrains)
			{
				if (t == null)
				{
					continue;
				}
				Vector3 o = t.GetPosition();
				Vector3 s = t.terrainData.size;
				if (x >= o.x && x <= o.x + s.x && z >= o.z && z <= o.z + s.z)
				{
					return t.SampleHeight(new Vector3(x, 0f, z)) + o.y;
				}
			}
			return float.NaN;
		}

		private void Clear()
		{
			foreach (GameObject go in built)
			{
				if (go == null)
				{
					continue;
				}
				MeshFilter filter = go.GetComponent<MeshFilter>();
				Mesh mesh = filter != null ? filter.sharedMesh : null;
				if (Application.isPlaying)
				{
					Destroy(mesh);
					Destroy(go);
				}
				else
				{
					DestroyImmediate(mesh);
					DestroyImmediate(go);
				}
			}
			built.Clear();
		}
	}
}
