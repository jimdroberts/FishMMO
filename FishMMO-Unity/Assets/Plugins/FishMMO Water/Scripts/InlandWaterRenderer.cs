using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Water
{
	/// <summary>
	/// Draws a scene's lakes and rivers: a flat surface over each lake, a ribbon along each river, built
	/// from its <see cref="SceneWaterBodies"/> when it loads, and the inland water's clock.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where water meets water.</b> Every vertex carries the scene's flow at its own position
	/// (<see cref="SceneWaterBodies.FlowAt"/>), so a tributary's ribbon and the river it joins carry the
	/// same current where they overlap, and a lake's surface by a river mouth carries the river's jet.
	/// The shader draws each pixel of inland water once, so the overlap is not laid on twice; lakes are
	/// drawn first and larger rivers before smaller, so the water a smaller one runs into is the one that
	/// shows. A river running into the sea fades out over its last few widths into the sea under it.
	/// </para>
	/// <para>
	/// <b>The tide.</b> A river that drains to the sea, itself or through the river it joins, is marked so,
	/// and its channel runs on to the low-water line (P4). The shader raises its water to the sea's level
	/// wherever the tide stands higher and fades it out into the sea below that line, so its mouth moves
	/// up and down the shore with the tide and the river is never drawn over the sea that covers it.
	/// </para>
	/// <para>
	/// <b>A river's surface is its own height everywhere</b>, so it falls with the river, and where it
	/// meets a lake, the sea or another river it is already at that one's level (P4 ends every river
	/// there). Each ribbon runs a little past its banks: the banks stand above the water, so the depth
	/// test hides the margin, and no gap opens at the waterline.
	/// </para>
	/// </remarks>
	[RequireComponent(typeof(SceneWaterBodies))]
	public sealed partial class InlandWaterRenderer : MonoBehaviour
	{
		[Tooltip("The lakes' and rivers' material (FishMMO/Water/Inland Water).")]
		public Material Material;

		[Tooltip("How far past each bank a river's surface runs, metres: under the bank, hidden by it.")]
		public float BankMargin = 1.5f;

		[Tooltip("Columns across a river: the two outer ones are the margin under its banks, the rest span bank to bank.")]
		[Range(4, 16)] public int Across = 8;

		[Tooltip("How many river widths a river running into the sea fades out over.")]
		public float SeaFadeWidths = 3f;

		[Tooltip("How far a river's surface runs on into the lake it enters or leaves, metres (at least two widths): over the strip of its channel the lake's water does not cover.")]
		public float LakeOverlapMetres = 10f;

		[Tooltip("Turn the water's depth effects and refraction on when the URP asset provides them, as the sea does.")]
		public bool DepthEffects = true;
		public bool Refraction = true;

		private static readonly int TimeId = Shader.PropertyToID("_FishInlandTime");
		private static readonly int CameraUnderId = Shader.PropertyToID("_FishInlandCameraUnder");
		private static readonly int CameraInSeaId = Shader.PropertyToID("_FishInlandCameraInSea");
		private static bool cameraHooked;
		private const string DepthKeyword = "_WATER_DEPTH";
		private const string RefractionKeyword = "_WATER_REFRACTION";

		private readonly List<GameObject> built = new List<GameObject>();

		private void Start()
		{
			Rebuild();
			LoadFlow();
		}

		private void OnDestroy()
		{
			Clear();
			ReleaseFlow();
		}

		private void Update()
		{
			/* The shared world-motion clock, wrapped where a float still resolves a millisecond. It was a
			 * private sum of this client's frame times wrapped at an hour, so every player's rivers ran
			 * at their own phase. The shaders snap every period they cycle on to a whole fraction of
			 * the wrap (the flow map's, the falls' streaks, each droplet's life), so the wrap is seamless. */
			Shader.SetGlobalFloat(TimeId, WorldMotion.Wrapped(WorldMotion.ShaderWrapSeconds));
			ApplyQuality();
			UpdateWakes();
		}

		/// <summary>
		/// The sea's global quality keywords, set here too for a scene with lakes and no sea. Where there is
		/// a sea, its surface sets them from its own settings and this leaves them alone.
		/// </summary>
		private void ApplyQuality()
		{
			if (FishMMO.Shared.Weather.SurfaceWater.TryGetLevel(out _))
			{
				return;
			}
			var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
			SetKeyword(DepthKeyword, DepthEffects && pipeline != null && pipeline.supportsCameraDepthTexture);
			SetKeyword(RefractionKeyword, Refraction && pipeline != null && pipeline.supportsCameraOpaqueTexture);
		}

		private static void SetKeyword(string keyword, bool on)
		{
			if (on == Shader.IsKeywordEnabled(keyword))
			{
				return;
			}
			if (on)
			{
				Shader.EnableKeyword(keyword);
			}
			else
			{
				Shader.DisableKeyword(keyword);
			}
		}

		/// <summary>
		/// Whether the camera about to draw is under a lake's or river's surface, for the shader: only then is the
		/// water's underside drawn. A camera low in a gorge stands below the river's level at the lip above it without
		/// being in any water, and drawn from beneath, the river's edge showed there as a dark slab over the fall.
		/// </summary>
		private static void MarkCamera(ScriptableRenderContext context, Camera camera)
		{
			Vector3 eye = camera.transform.position;
			bool under = FishMMO.Shared.Weather.SurfaceWater.TryGetInlandSurfaceAt(eye.x, eye.z, out float level) && eye.y < level;
			Shader.SetGlobalFloat(CameraUnderId, under ? 1f : 0f);
			/* Under the SEA, no lake or river is to be seen: none lies below the sea's surface, and one above it is past the
			 * surface the eye is looking up through. Drawn after the sea (which writes no depth), rivers showed through it. */
			bool inSea = !under && FishMMO.Shared.Weather.SurfaceWater.TryGetLevel(out float sea) && eye.y < sea
				&& FishMMO.Shared.Weather.SurfaceWater.IsUnder(eye);
			Shader.SetGlobalFloat(CameraInSeaId, inSea ? 1f : 0f);
		}

		/// <summary>Builds every lake's and river's surface again from the scene's water.</summary>
		public void Rebuild()
		{
			if (!cameraHooked)
			{
				RenderPipelineManager.beginCameraRendering += MarkCamera;
				cameraHooked = true;
			}
			Clear();
			var water = GetComponent<SceneWaterBodies>();
			if (water == null || water.Hydrology == null || Material == null)
			{
				return;
			}
			water.Build();
			SceneHydrology hydrology = water.Hydrology;
			int order = 0;
			// The falls first: a river's ribbon gives the falling stretch to its fall's sheet.
			List<Fall> falls = FindFalls(hydrology, MinFallMetres);
			foreach (SceneHydrology.Lake lake in hydrology.Lakes)
			{
				Mesh mesh = LakeMesh(lake, water);
				if (mesh != null)
				{
					Add($"Lake {lake.Id}", mesh, order);
				}
			}
			// Larger rivers first: ids rank by size, so the water a smaller river runs into is the one that shows.
			var rivers = new List<SceneHydrology.River>(hydrology.Rivers);
			rivers.Sort((a, b) => a.PlanetRiver != b.PlanetRiver ? a.PlanetRiver.CompareTo(b.PlanetRiver) : a.Id.CompareTo(b.Id));
			foreach (SceneHydrology.River river in rivers)
			{
				if (!river.Perennial || river.Points == null || river.Points.Length < 2)
				{
					continue;
				}
				Mesh mesh = RiverMesh(river, water, falls);
				if (mesh != null)
				{
					Add($"River {river.Id}", mesh, ++order);
					riverRenderers[river.Id] = built[built.Count - 1].GetComponent<MeshRenderer>();
				}
			}
			BuildFalls(hydrology, falls);
			ApplyFlow();
		}

		private void Clear()
		{
			riverRenderers.Clear();
			foreach (GameObject go in built)
			{
				if (go == null)
				{
					continue;
				}
				MeshFilter filter = go.GetComponent<MeshFilter>();
				if (filter != null && filter.sharedMesh != null)
				{
					Discard(filter.sharedMesh);
				}
				Discard(go);
			}
			built.Clear();
		}

		/// <summary>Destroys now in the editor, at the frame's end in play: Destroy refuses to run outside play.</summary>
		private static void Discard(Object target)
		{
			if (Application.isPlaying)
			{
				Destroy(target);
			}
			else
			{
				DestroyImmediate(target);
			}
		}

		/// <summary>The surfaces built, for tools and tests.</summary>
		public IReadOnlyList<GameObject> Built => built;

		private void Add(string name, Mesh mesh, int order)
		{
			var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
			go.transform.SetParent(transform, false);
			go.transform.position = Vector3.zero;
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = Material;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = true;
			// Drawn in this order within the water's queue: the first to claim a pixel keeps it.
			renderer.sortingOrder = order;
			built.Add(go);
		}

		/// <summary>A lake's surface: a quad over each of its mask's cells and a ring of cells round them, at its level.</summary>
		private static Mesh LakeMesh(SceneHydrology.Lake lake, SceneWaterBodies water)
		{
			if (lake.Mask == null || lake.Mask.Length == 0 || lake.MaskWidth <= 0 || lake.MaskHeight <= 0)
			{
				return null;
			}
			int w = lake.MaskWidth, h = lake.MaskHeight;
			// Grown by a cell, so the surface runs under its shore; the shore hides it.
			var cover = new bool[w * h];
			for (int z = 0; z < h; z++)
			{
				for (int x = 0; x < w; x++)
				{
					if (!Bit(lake, x, z))
					{
						continue;
					}
					for (int dz = -1; dz <= 1; dz++)
					{
						for (int dx = -1; dx <= 1; dx++)
						{
							int nx = x + dx, nz = z + dz;
							if (nx >= 0 && nz >= 0 && nx < w && nz < h)
							{
								cover[nz * w + nx] = true;
							}
						}
					}
				}
			}
			var vertexOf = new int[(w + 1) * (h + 1)];
			for (int i = 0; i < vertexOf.Length; i++)
			{
				vertexOf[i] = -1;
			}
			var positions = new List<Vector3>();
			var flows = new List<Vector2>();
			var states = new List<Vector2>();
			var uvs = new List<Vector2>();
			var colours = new List<Color32>();
			var tides = new List<Vector2>();
			var indices = new List<int>();
			int Vertex(int vx, int vz)
			{
				int key = vz * (w + 1) + vx;
				if (vertexOf[key] >= 0)
				{
					return vertexOf[key];
				}
				float px = lake.MaskOrigin.x + vx * lake.MaskCell, pz = lake.MaskOrigin.y + vz * lake.MaskCell;
				Vector2 flow = water.FlowAt(px, pz, out float reach, out float broken);
				vertexOf[key] = positions.Count;
				positions.Add(new Vector3(px, lake.Level, pz));
				flows.Add(flow);
				states.Add(new Vector2(broken, reach));
				uvs.Add(new Vector2(px, pz));
				colours.Add(new Color32(255, 255, 255, 255));
				tides.Add(Vector2.zero);
				return vertexOf[key];
			}
			for (int z = 0; z < h; z++)
			{
				for (int x = 0; x < w; x++)
				{
					if (!cover[z * w + x])
					{
						continue;
					}
					int a = Vertex(x, z), b = Vertex(x + 1, z), c = Vertex(x + 1, z + 1), d = Vertex(x, z + 1);
					indices.Add(a); indices.Add(d); indices.Add(c);
					indices.Add(a); indices.Add(c); indices.Add(b);
				}
			}
			return Build($"Lake {lake.Id}", positions, uvs, flows, states, colours, tides, indices);
		}

		private static bool Bit(SceneHydrology.Lake lake, int x, int z)
		{
			int bit = z * lake.MaskWidth + x;
			return (bit >> 3) < lake.Mask.Length && (lake.Mask[bit >> 3] & (1 << (bit & 7))) != 0;
		}

		/// <summary>A river's ribbon: rows across it at each point of its line, at its surface, past its banks.</summary>
		private Mesh RiverMesh(SceneHydrology.River river, SceneWaterBodies water, List<Fall> falls)
		{
			int n = river.Points.Length;
			int across = Mathf.Max(4, Across);
			bool toSea = river.Finish == SceneHydrology.End.Sea;
			bool tidal = DrainsToSea(river, water.Hydrology);

			/* The rows: the river's own points and, where it runs into a lake or out of one, a few more on into
			 * the lake at its level, fading out. The lake's water stops at its mask and the river's channel is not
			 * in it (the lake floods up to the channel), so without them a strip of channel lies under neither. The
			 * lake is drawn first and keeps its pixels, so these show only in that strip. */
			var rows = new List<Row>(n + 16);
			if (river.Start == SceneHydrology.End.Lake)
			{
				AddLakeRows(rows, river, 0, 1);
				rows.Reverse();
			}
			float length = 0f;
			for (int i = 0; i < n; i++)
			{
				if (i > 0)
				{
					Vector3 step = river.Points[i] - river.Points[i - 1];
					step.y = 0f;
					length += step.magnitude;
				}
				rows.Add(new Row
				{
					At = river.Points[i],
					Along = length,
					Width = river.Width != null && i < river.Width.Length ? river.Width[i] : 4f,
					Depth = river.Depth != null && i < river.Depth.Length ? river.Depth[i] : 1f,
					Fade = 1f,
					Point = i,
				});
			}
			if (river.Finish == SceneHydrology.End.Lake)
			{
				AddLakeRows(rows, river, n - 1, -1);
			}
			// Distance along from the first row, the lake rows before the river's start counted back from it.
			for (int r = 0; r < rows.Count && rows[r].Point < 0; r++)
			{
				Row row = rows[r];
				row.Along = -row.Along;
				rows[r] = row;
			}

			int count = rows.Count;
			var positions = new List<Vector3>(count * (across + 1));
			var flows = new List<Vector2>(positions.Capacity);
			var states = new List<Vector2>(positions.Capacity);
			var uvs = new List<Vector2>(positions.Capacity);
			var colours = new List<Color32>(positions.Capacity);
			var tides = new List<Vector2>(positions.Capacity);
			var banks = new List<Vector4>(positions.Capacity);
			var indices = new List<int>((count - 1) * across * 6);
			var along = new float[n];
			for (int r = 0; r < count; r++)
			{
				if (rows[r].Point >= 0)
				{
					along[rows[r].Point] = rows[r].Along;
				}
			}
			for (int r = 0; r < count; r++)
			{
				Row row = rows[r];
				Vector3 p = row.At;
				// The row before a fall's plunge: down at the pool, so the pool reaches back under the curtain.
				if (row.Point >= 0 && IsPlungeRow(falls, river, row.Point, out float poolLevel))
				{
					p.y = poolLevel;
				}
				Vector3 forward = rows[Mathf.Min(count - 1, r + 1)].At - rows[Mathf.Max(0, r - 1)].At;
				forward.y = 0f;
				forward = forward.sqrMagnitude > 1e-8f ? forward.normalized : Vector3.forward;
				var left = new Vector3(-forward.z, 0f, forward.x);
				float width = row.Width;
				float half = 0.5f * width + BankMargin;
				// Into the sea: fading out over its last widths, the sea showing through it.
				float fade = row.Fade;
				if (toSea)
				{
					fade *= Mathf.Clamp01((length - row.Along) / Mathf.Max(1f, SeaFadeWidths * width));
				}
				/* Bank to bank in even steps, then one more row each side under the bank, where the water is
				 * already transparent: wherever a bank stands lower than the water — beside a fall, over a
				 * lip — the margin fades out instead of showing as a sheet hung above the ground. */
				float bank = 0.5f * width;
				float fall = tidal && row.Point >= 0 ? FallOver(river, along, row.Point, SeaFadeWidths * width) : 0.05f;
				// r: the water's depth here over 4 m, which the shader's bed stones read (shallow water shows them).
				byte shallow = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(row.Depth / 4f));
				// Inside a fall the sheet is the water: the ribbon there neither shows nor claims its pixels.
				bool falling = row.Point >= 0 && InsideFall(falls, river.Id, row.Point);
				for (int k = 0; k <= across; k++)
				{
					bool margin = k == 0 || k == across;
					float offset = margin ? (k == 0 ? -half : half) : Mathf.Lerp(-bank, bank, (k - 1) / (float)(across - 2));
					Vector3 at = p + left * offset;
					Vector2 flow = water.FlowAt(at.x, at.z, out float reach, out float broken);
					positions.Add(at);
					flows.Add(flow);
					states.Add(new Vector2(broken, reach));
					uvs.Add(new Vector2(row.Along, offset));
					// g: whether this water claims its pixels (the draw-once stencil): not the margin under a bank, so a
					// tributary running in there is drawn rather than hidden behind water nobody can see.
					bool hidden = margin || falling;
					colours.Add(new Color32(shallow, hidden ? (byte)0 : (byte)255, 255, hidden ? (byte)0 : (byte)Mathf.RoundToInt(255f * fade)));
					tides.Add(new Vector2(fall, tidal ? 1f : 0f));
					// For the solved flow: the half-width bank to bank, the river's mean speed here, and its way downstream.
					int speedPoint = row.Point >= 0 ? row.Point : (r == 0 ? 0 : n - 1);
					float meanSpeed = river.Speed != null && speedPoint < river.Speed.Length ? river.Speed[speedPoint] : 0.5f;
					banks.Add(new Vector4(bank, meanSpeed, forward.x, forward.z));
				}
			}
			for (int r = 0; r + 1 < count; r++)
			{
				/* Nothing where a fall's sheet is the water: a quad from the lip's row to the first falling row hangs
				 * down the cliff, its alpha ramping from the river's to none, and drew as a tall pane of glass. */
				if (FallHidesQuad(falls, river, rows[r].Point, rows[r + 1].Point))
				{
					continue;
				}
				int row = r * (across + 1), next = row + across + 1;
				for (int k = 0; k < across; k++)
				{
					indices.Add(row + k); indices.Add(next + k); indices.Add(next + k + 1);
					indices.Add(row + k); indices.Add(next + k + 1); indices.Add(row + k + 1);
				}
			}
			return Build($"River {river.Id}", positions, uvs, flows, states, colours, tides, indices, banks);
		}

		/// <summary>One row across a river's ribbon.</summary>
		private struct Row
		{
			public Vector3 At;
			public float Along;
			public float Width;
			public float Depth;
			public float Fade;
			/// <summary>The river's own point the row stands on; −1 for a row run on into a lake.</summary>
			public int Point;
		}

		/// <summary>
		/// The rows a river runs on into the lake at its end (<paramref name="step"/> −1, from its last point) or
		/// out of the lake at its start (+1, from its first), nearest first: on the line's own heading, at the
		/// end point's surface (the lake's level), fading out over <see cref="LakeOverlapMetres"/> or two widths.
		/// </summary>
		private void AddLakeRows(List<Row> rows, SceneHydrology.River river, int from, int step)
		{
			int n = river.Points.Length;
			int toward = Mathf.Clamp(from + step * 3, 0, n - 1);
			Vector3 heading = river.Points[from] - river.Points[toward];
			heading.y = 0f;
			if (heading.sqrMagnitude < 1e-6f)
			{
				return;
			}
			heading.Normalize();
			float width = river.Width != null && from < river.Width.Length ? river.Width[from] : 4f;
			float depth = river.Depth != null && from < river.Depth.Length ? river.Depth[from] : 1f;
			float reach = Mathf.Max(LakeOverlapMetres, 2f * width);
			int steps = Mathf.Max(2, Mathf.CeilToInt(reach / Mathf.Max(1f, 0.5f * width)));
			float last = 0f;
			if (step < 0)
			{
				// Past the end: continuing the distance along from the river's last row.
				last = rows.Count > 0 ? rows[rows.Count - 1].Along : 0f;
			}
			for (int k = 1; k <= steps; k++)
			{
				float d = reach * k / steps;
				rows.Add(new Row
				{
					At = river.Points[from] + heading * d,
					Along = step < 0 ? last + d : d,
					Width = width,
					Depth = depth,
					Fade = 1f - k / (float)steps,
					Point = -1,
				});
			}
		}

		/// <summary>Whether a river's water reaches the sea: its own mouth, or that of the river it joins, and so on down.</summary>
		public static bool DrainsToSea(SceneHydrology.River river, SceneHydrology hydrology)
		{
			for (int hops = 0; river != null && hops <= hydrology.Rivers.Count; hops++)
			{
				if (river.Finish == SceneHydrology.End.Sea)
				{
					return true;
				}
				if (river.Finish != SceneHydrology.End.Confluence || river.JoinsRiver < 0)
				{
					return false;
				}
				river = hydrology.Rivers.Find(r => r.Id == river.JoinsRiver);
			}
			return false;
		}

		/// <summary>
		/// How far a river's surface falls over <paramref name="distance"/> metres downstream of point
		/// <paramref name="i"/>, metres (at least 5 cm): the depth under the sea over which it fades out into it.
		/// Past its end it carries on at the slope of its last stretch.
		/// </summary>
		private static float FallOver(SceneHydrology.River river, float[] along, int i, float distance)
		{
			int n = river.Points.Length;
			float target = along[i] + Mathf.Max(1f, distance);
			int j = i;
			while (j < n - 1 && along[j] < target)
			{
				j++;
			}
			float run = along[j] - along[i];
			float fall = river.Points[i].y - river.Points[j].y;
			if (run < distance && run > 1f)
			{
				fall *= distance / run;
			}
			return Mathf.Max(0.05f, fall);
		}

		private static Mesh Build(string name, List<Vector3> positions, List<Vector2> uvs, List<Vector2> flows, List<Vector2> states, List<Color32> colours,
			List<Vector2> tides, List<int> indices, List<Vector4> banks = null)
		{
			if (indices.Count == 0)
			{
				return null;
			}
			var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
			if (positions.Count > 65000)
			{
				mesh.indexFormat = IndexFormat.UInt32;
			}
			mesh.SetVertices(positions);
			mesh.SetUVs(0, uvs);
			mesh.SetUVs(1, flows);
			mesh.SetUVs(2, states);
			mesh.SetUVs(3, tides);
			if (banks != null && banks.Count == positions.Count)
			{
				mesh.SetUVs(4, banks);
			}
			mesh.SetColors(colours);
			mesh.SetTriangles(indices, 0);
			var normals = new Vector3[positions.Count];
			for (int i = 0; i < normals.Length; i++)
			{
				normals[i] = Vector3.up;
			}
			mesh.normals = normals;
			mesh.RecalculateBounds();
			return mesh;
		}
	}
}
