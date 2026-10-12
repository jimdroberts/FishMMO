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
			/* The flow first: the falls take their water's spread across each lip from it. Loaded at once (the editor),
			 * everything is built once with it; still loading (play), the rivers are built now and the falls when it
			 * arrives (SetFlow), rather than every fall traced twice. */
			LoadFlow();
			Rebuild(!flowLoading);
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

		/// <summary>Builds every lake's and river's surface again from the scene's water, and its falls.</summary>
		public void Rebuild() => Rebuild(true);

		/// <summary>Builds every lake's and river's surface again; its falls too unless <paramref name="withFalls"/> is false.</summary>
		private void Rebuild(bool withFalls)
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
			// Larger rivers first, and every river before those joining it (DrawOrder): the water a smaller river runs into is the one that shows.
			foreach (SceneHydrology.River river in DrawOrder(hydrology.Rivers))
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
			if (withFalls)
			{
				BuildFalls(hydrology, falls);
			}
			ApplyFlow();
		}

		private void Clear()
		{
			riverRenderers.Clear();
			ClearFalls();
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

		/// <summary>
		/// The order rivers are drawn in: by planet river, then id (the planet ranks its rivers by size, so larger first),
		/// except that a river joined by another always comes before it, however the two rank.
		/// </summary>
		/// <remarks>
		/// The first water drawn at a pixel keeps it (the draw-once stencil), and a tributary's ribbon runs on past its
		/// mouth over the round end of its channel at full strength (AddConfluenceRows), across the river it joins. Drawn
		/// before that river, those rows would take its pixels: a flat patch of the tributary's water, at the mouth's level
		/// and with its current, laid over the main channel. Ranking by planet river and id put the joined river first only
		/// as long as the planet happened to number it lower; this makes it so.
		/// </remarks>
		public static List<SceneHydrology.River> DrawOrder(IReadOnlyList<SceneHydrology.River> rivers)
		{
			var ranked = new List<SceneHydrology.River>(rivers.Count);
			var byId = new Dictionary<int, SceneHydrology.River>();
			foreach (SceneHydrology.River river in rivers)
			{
				if (river == null)
				{
					continue;
				}
				ranked.Add(river);
				if (!byId.ContainsKey(river.Id))
				{
					byId[river.Id] = river;
				}
			}
			ranked.Sort((a, b) => a.PlanetRiver != b.PlanetRiver ? a.PlanetRiver.CompareTo(b.PlanetRiver) : a.Id.CompareTo(b.Id));
			var order = new List<SceneHydrology.River>(ranked.Count);
			var placed = new HashSet<SceneHydrology.River>();
			var placing = new HashSet<SceneHydrology.River>();
			void Place(SceneHydrology.River river)
			{
				// Placed already, or on the way to placing it (a loop of joins, which no network should have): as it stands.
				if (placed.Contains(river) || !placing.Add(river))
				{
					return;
				}
				if (river.JoinsRiver >= 0 && byId.TryGetValue(river.JoinsRiver, out SceneHydrology.River joined) && joined != river)
				{
					Place(joined);
				}
				placed.Add(river);
				order.Add(river);
			}
			foreach (SceneHydrology.River river in ranked)
			{
				Place(river);
			}
			return order;
		}

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
			/* Vertices on a grid ChannelSubdivisions times finer than the mask, keyed by their fine position, so a cell
			 * cut finer by a river shares its edge with a whole neighbour (a flat surface: the T-junction is no crack). */
			const int n = ChannelSubdivisions;
			var vertexOf = new Dictionary<long, int>();
			var positions = new List<Vector3>();
			var flows = new List<Vector2>();
			var states = new List<Vector2>();
			var uvs = new List<Vector2>();
			var colours = new List<Color32>();
			var tides = new List<Vector2>();
			var indices = new List<int>();
			int Vertex(int fx, int fz)
			{
				long key = ((long)fz << 32) | (uint)fx;
				if (vertexOf.TryGetValue(key, out int index))
				{
					return index;
				}
				float px = lake.MaskOrigin.x + fx * (lake.MaskCell / n), pz = lake.MaskOrigin.y + fz * (lake.MaskCell / n);
				Vector2 flow = water.FlowAt(px, pz, out float reach, out float broken);
				index = positions.Count;
				vertexOf[key] = index;
				positions.Add(new Vector3(px, lake.Level, pz));
				flows.Add(flow);
				states.Add(new Vector2(broken, reach));
				uvs.Add(new Vector2(px, pz));
				colours.Add(new Color32(255, ChannelClaim(water.OutsideChannels(px, pz, lake.Id)), 255, 255));
				tides.Add(Vector2.zero);
				return index;
			}
			void Quad(int fx, int fz, int size)
			{
				int a = Vertex(fx, fz), b = Vertex(fx + size, fz), c = Vertex(fx + size, fz + size), d = Vertex(fx, fz + size);
				// Wholly inside a channel: every corner gives way, so nothing of it would be drawn.
				if (colours[a].g < 128 && colours[b].g < 128 && colours[c].g < 128 && colours[d].g < 128)
				{
					return;
				}
				indices.Add(a); indices.Add(d); indices.Add(c);
				indices.Add(a); indices.Add(c); indices.Add(b);
			}
			// A cell this near a channel is cut finer, so the edge where the lake gives way follows the bank.
			float near = 0.7072f * lake.MaskCell + ChannelClaimMetres;
			for (int z = 0; z < h; z++)
			{
				for (int x = 0; x < w; x++)
				{
					if (!cover[z * w + x])
					{
						continue;
					}
					float cx = lake.MaskOrigin.x + (x + 0.5f) * lake.MaskCell, cz = lake.MaskOrigin.y + (z + 0.5f) * lake.MaskCell;
					if (water.OutsideChannels(cx, cz, lake.Id) >= near)
					{
						Quad(x * n, z * n, n);
						continue;
					}
					for (int sz = 0; sz < n; sz++)
					{
						for (int sx = 0; sx < n; sx++)
						{
							Quad(x * n + sx, z * n + sz, 1);
						}
					}
				}
			}
			return Build($"Lake {lake.Id}", positions, uvs, flows, states, colours, tides, indices);
		}

		/// <summary>How many times finer than its mask a lake's cells are cut where a river's channel runs through or by them.</summary>
		private const int ChannelSubdivisions = 8;

		/// <summary>The band over which a lake gives its pixels up to a river's channel, metres, centred on the bank.</summary>
		private const float ChannelClaimMetres = 1f;

		/// <summary>
		/// A lake vertex's claim on its pixels (vertex colour g): none inside a river's channel, all of them outside it, and
		/// the half the shader clips at exactly on the bank, so the lake stops along the bank's own line.
		/// </summary>
		/// <remarks>
		/// A lake is drawn before the rivers and keeps every pixel it claims (the draw-once stencil), and its surface is
		/// its mask grown by a 4 m cell. Where a lake lies beside a river it does not drain into or from (a pond along
		/// a ponded stretch, a lake a river passes), those cells covered the channel in square patches of lake water
		/// with none of the river's depth, flow or colour (2026-10-08). A river that starts or ends in the lake is
		/// left to it, as at every mouth: its rows run on into the lake fading, and the lake keeps its pixels.
		/// </remarks>
		private static byte ChannelClaim(float outside)
		{
			return (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(0.5f + outside / ChannelClaimMetres));
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
			 * lake is drawn first and keeps its pixels, so these show only in that strip. A tributary runs on over the
			 * round end its channel was cut with, past the joined river's centre line (AddConfluenceRows). */
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
			else if (river.Finish == SceneHydrology.End.Confluence)
			{
				AddConfluenceRows(rows, river, water.Hydrology, falls);
			}
			// Distance along from the first row, the lake rows before the river's start counted back from it.
			for (int r = 0; r < rows.Count && rows[r].Point < 0; r++)
			{
				Row row = rows[r];
				row.Along = -row.Along;
				rows[r] = row;
			}
			var along = new float[n];
			for (int r = 0; r < rows.Count; r++)
			{
				if (rows[r].Point >= 0)
				{
					along[rows[r].Point] = rows[r].Along;
				}
			}

			/* Never a twisted quad, however the line bends: a row where the line turns back on itself is left out, and on the
			 * inside of a bend tighter than the ribbon's half width the rows are drawn in toward the line (RibbonFrames). */
			var centres = new List<Vector3>(rows.Count);
			foreach (Row row in rows)
			{
				centres.Add(row.At);
			}
			List<int> kept = RowsThatAdvance(centres);
			if (kept.Count < rows.Count)
			{
				var advancing = new List<Row>(kept.Count);
				foreach (int r in kept)
				{
					advancing.Add(rows[r]);
				}
				rows = advancing;
				centres.Clear();
				foreach (Row row in rows)
				{
					centres.Add(row.At);
				}
			}
			int count = rows.Count;
			var halves = new float[count];
			for (int r = 0; r < count; r++)
			{
				halves[r] = 0.5f * rows[r].Width + BankMargin;
			}
			RibbonFrames(centres, halves, out Vector3[] forwards, out float[] leftReach, out float[] rightReach);

			var positions = new List<Vector3>(count * (across + 1));
			var flows = new List<Vector2>(positions.Capacity);
			var states = new List<Vector2>(positions.Capacity);
			var uvs = new List<Vector2>(positions.Capacity);
			var colours = new List<Color32>(positions.Capacity);
			var tides = new List<Vector2>(positions.Capacity);
			var banks = new List<Vector4>(positions.Capacity);
			var indices = new List<int>((count - 1) * across * 6);
			for (int r = 0; r < count; r++)
			{
				Row row = rows[r];
				Vector3 p = row.At;
				// The row before a fall's plunge: down at the pool, so the pool reaches back under the curtain.
				if (row.Point >= 0 && IsPlungeRow(falls, river, row.Point, out float poolLevel))
				{
					p.y = poolLevel;
				}
				Vector3 forward = forwards[r];
				var left = new Vector3(-forward.z, 0f, forward.x);
				float width = row.Width;
				float half = halves[r];
				// Drawn in toward the line on the inside of a tight bend, each side by its own share (the uv keeps the true offset).
				float leftScale = Mathf.Clamp01(leftReach[r] / Mathf.Max(1e-4f, half)), rightScale = Mathf.Clamp01(rightReach[r] / Mathf.Max(1e-4f, half));
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
					Vector3 at = p + left * (offset * (offset > 0f ? leftScale : rightScale));
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
					/* For the solved flow: the half-width bank to bank, the river's mean speed here, and its way downstream. A row
					 * run on past the river's own points takes the speed of the end it runs on from: the lake rows before its
					 * start (counted back, so negative along) its first point's. Only the first of them did: the rest took the
					 * speed of the river's mouth, at the far end of it. */
					int speedPoint = row.Point >= 0 ? row.Point : (row.Along < 0f ? 0 : n - 1);
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
			/// <summary>The river's own point the row stands on; −1 for a row run on into a lake or past a tributary's mouth.</summary>
			public int Point;
		}

		/// <summary>
		/// The rows a river runs on into the lake at its end (<paramref name="step"/> −1, from its last point) or
		/// out of the lake at its start (+1, from its first), nearest first: on the line's own heading, at the
		/// end point's surface (the lake's level), fading out over <see cref="LakeOverlapMetres"/> or two widths.
		/// </summary>
		private void AddLakeRows(List<Row> rows, SceneHydrology.River river, int from, int step)
		{
			float width = river.Width != null && from < river.Width.Length ? river.Width[from] : 4f;
			AddRunOnRows(rows, river, from, step, Mathf.Max(LakeOverlapMetres, 2f * width), true);
		}

		/// <summary>
		/// The rows a tributary runs on past its last point, over the round end of its channel, at full strength: half its
		/// mouth's width and the bank margin.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A tributary ends on the centre line of the river it joins (SceneWater), and the carver cuts each channel as a
		/// string of capsules, so its last point leaves a round end cap of the mouth's half width cut into the ground past
		/// it. Its ribbon stopped square at that point. The river it joins covers the cap only where it is at least as wide
		/// as the mouth, and the mouth flares to 2.2 widths (SceneWater.Flare): a tributary near the joined river's size
		/// left the cap's two lobes beyond that river's banks under no water at all, a dry bed at the T (2026-10-12;
		/// Flo Monolith's river 1 into river 0: a 15.9 m mouth into a 7.2 m river, 18 m² of bed bare).
		/// </para>
		/// <para>
		/// The joined river is drawn first and keeps its pixels (the draw-once stencil), so these rows show only where it
		/// does not reach: the lobes. They stay at the mouth's level and do not fade, as the water in the lobes is the
		/// river's, not a mouth fading into a lake; past the cap they lie under the bank, which hides them as it hides
		/// every ribbon's margin.
		/// </para>
		/// </remarks>
		private void AddConfluenceRows(List<Row> rows, SceneHydrology.River river, SceneHydrology hydrology, List<Fall> falls)
		{
			int last = river.Points.Length - 1;
			float width = river.Width != null && last < river.Width.Length ? river.Width[last] : 4f;
			int before = rows.Count;
			AddRunOnRows(rows, river, last, -1, 0.5f * width + BankMargin, false);
			SceneHydrology.River joined = hydrology != null && river.JoinsRiver >= 0 ? hydrology.Rivers.Find(r => r.Id == river.JoinsRiver) : null;
			if (joined == null || rows.Count == before)
			{
				return;
			}
			var runOn = new List<Vector3>(rows.Count - before);
			for (int r = before; r < rows.Count; r++)
			{
				runOn.Add(rows[r].At);
			}
			int keep = ConfluenceRunOnRows(joined, falls, runOn, river.Points[last].y);
			rows.RemoveRange(before + keep, rows.Count - before - keep);
		}

		/// <summary>How far below a tributary's mouth the water its run-on rows lie over may stand, metres, before they stop.</summary>
		public const float RunOnDropMetres = 0.5f;

		/// <summary>
		/// How many of a tributary's run-on rows (<paramref name="runOn"/>, nearest the mouth first) stand over water at its
		/// mouth's level: they stop at the first that lies over a fall of the river it joins, or over that river where its
		/// surface stands more than <see cref="RunOnDropMetres"/> below <paramref name="mouthLevel"/>.
		/// </summary>
		/// <remarks>
		/// The rows lie flat at the mouth's level, past the joined river's centre line, out over the round end the tributary's
		/// channel was cut with. A fall's lip within a few metres of the mouth puts them over the drop: inside a fall the
		/// joined river's own ribbon gives its pixels to the curtain, so nothing would have hidden them, and they showed as a
		/// flat sheet of water hung over the fall at the lip's height.
		/// </remarks>
		public static int ConfluenceRunOnRows(SceneHydrology.River joined, IReadOnlyList<Fall> falls, IReadOnlyList<Vector3> runOn, float mouthLevel)
		{
			if (joined == null || joined.Points == null || joined.Points.Length < 2)
			{
				return runOn.Count;
			}
			for (int k = 0; k < runOn.Count; k++)
			{
				// The joined river's segment nearest the row, and its surface there.
				Vector3 at = runOn[k];
				float best = float.MaxValue, level = mouthLevel;
				int segment = 0;
				for (int v = 0; v + 1 < joined.Points.Length; v++)
				{
					Vector3 a = joined.Points[v], b = joined.Points[v + 1];
					float ex = b.x - a.x, ez = b.z - a.z;
					float t = Mathf.Clamp01(((at.x - a.x) * ex + (at.z - a.z) * ez) / Mathf.Max(1e-8f, ex * ex + ez * ez));
					float dx = a.x + ex * t - at.x, dz = a.z + ez * t - at.z;
					float d = dx * dx + dz * dz;
					if (d < best)
					{
						best = d;
						segment = v;
						level = Mathf.Lerp(a.y, b.y, t);
					}
				}
				if (level < mouthLevel - RunOnDropMetres)
				{
					return k;
				}
				if (falls != null)
				{
					foreach (Fall fall in falls)
					{
						// The segments from the lip down to the foot are the fall's: the water there is the curtain, not a surface.
						if (fall.River == joined.Id && segment >= fall.Lip && segment < fall.Foot)
						{
							return k;
						}
					}
				}
			}
			return runOn.Count;
		}

		/// <summary>
		/// The rows of a ribbon, by index, that leave out every row where its line turns back on itself: none of those kept
		/// turns more than a right angle from the row before it to the row after. The first and last are always kept.
		/// </summary>
		/// <remarks>
		/// A row's frame is square to its line's heading, taken from the rows either side; where the line doubles back (Flo
		/// Monolith's river 0 turned 146° in one point, 2026-10-12) that heading points across the river, the rows either
		/// side run backwards against it, and the quads between them twist over. Taking the row out joins its neighbours
		/// directly, as the generator now cuts such a bend off (RiverShaping.EaseBends); this keeps a line from anywhere else
		/// safe too. A row on top of the one before it is left out as well.
		/// </remarks>
		public static List<int> RowsThatAdvance(IReadOnlyList<Vector3> centres)
		{
			var kept = new List<int>(centres.Count);
			for (int i = 0; i < centres.Count; i++)
			{
				kept.Add(i);
			}
			// Its turn's cosine at a kept row, from the chords to the kept rows either side: below 0 is past a right angle.
			float Cosine(int k)
			{
				if (k <= 0 || k >= kept.Count - 1)
				{
					return 1f;
				}
				Vector3 a = centres[kept[k]] - centres[kept[k - 1]], b = centres[kept[k + 1]] - centres[kept[k]];
				a.y = 0f;
				b.y = 0f;
				float la = a.magnitude, lb = b.magnitude;
				if (la < 1e-4f || lb < 1e-4f)
				{
					return -2f;
				}
				return Vector3.Dot(a, b) / (la * lb);
			}
			while (kept.Count > 2)
			{
				int worst = -1;
				float lowest = 0f;
				for (int k = 1; k < kept.Count - 1; k++)
				{
					float c = Cosine(k);
					if (c < lowest)
					{
						lowest = c;
						worst = k;
					}
				}
				if (worst < 0)
				{
					break;
				}
				kept.RemoveAt(worst);
			}
			return kept;
		}

		/// <summary>
		/// Each row's heading along a ribbon (from the rows either side), and how far its water may reach to the left and to
		/// the right of its line before a quad to the row before or after would fold over: <paramref name="halves"/>, or less
		/// on the inside of a bend tighter than that. The rows must advance (<see cref="RowsThatAdvance"/>).
		/// </summary>
		/// <remarks>
		/// A ribbon's rows are square to the line, not mitred, so on the outside of a bend they fan apart and on the inside
		/// they close in: tighter than the half width, the inside edge of a row passes the next row's, and the quad between
		/// is drawn back to front. For each pair of rows the reach on either side that keeps every column moving forward
		/// along the other row's heading (by at least a tenth of the line's own step) is worked out, and a row
		/// reaches no further than both of its pairs allow. A gentle bend allows its whole half width; a hairpin, a little.
		/// </remarks>
		public static void RibbonFrames(IReadOnlyList<Vector3> centres, IReadOnlyList<float> halves, out Vector3[] forwards, out float[] leftReach, out float[] rightReach)
		{
			int count = centres.Count;
			forwards = new Vector3[count];
			leftReach = new float[count];
			rightReach = new float[count];
			for (int r = 0; r < count; r++)
			{
				Vector3 forward = centres[Mathf.Min(count - 1, r + 1)] - centres[Mathf.Max(0, r - 1)];
				forward.y = 0f;
				forwards[r] = forward.sqrMagnitude > 1e-8f ? forward.normalized : Vector3.forward;
				leftReach[r] = halves[r];
				rightReach[r] = halves[r];
			}
			for (int r = 0; r + 1 < count; r++)
			{
				Vector3 step = centres[r + 1] - centres[r];
				step.y = 0f;
				Vector3 left0 = new Vector3(-forwards[r].z, 0f, forwards[r].x), left1 = new Vector3(-forwards[r + 1].z, 0f, forwards[r + 1].x);
				/* Each of a quad's two triangles has one row's edge across it, so it faces up while the edge from the other row's
				 * column runs forward along that row's heading: from offset o at row r to o' at row r + 1 the edge is
				 * step + o'·left' − o·left, and along row r's heading (left ⟂ heading) that is step·f + o'·(left'·f). So row
				 * r + 1's reach is held by row r's heading, and row r's by row r + 1's, each to keep a tenth of the step. */
				float advance0 = Vector3.Dot(step, forwards[r]), advance1 = Vector3.Dot(step, forwards[r + 1]);
				float s1 = Vector3.Dot(left1, forwards[r]);
				float room0 = Mathf.Max(0f, 0.9f * advance0);
				if (s1 < -1e-6f)
				{
					leftReach[r + 1] = Mathf.Min(leftReach[r + 1], room0 / -s1);
				}
				else if (s1 > 1e-6f)
				{
					rightReach[r + 1] = Mathf.Min(rightReach[r + 1], room0 / s1);
				}
				float s0 = Vector3.Dot(left0, forwards[r + 1]);
				float room1 = Mathf.Max(0f, 0.9f * advance1);
				if (s0 > 1e-6f)
				{
					leftReach[r] = Mathf.Min(leftReach[r], room1 / s0);
				}
				else if (s0 < -1e-6f)
				{
					rightReach[r] = Mathf.Min(rightReach[r], room1 / -s0);
				}
			}
		}

		/// <summary>
		/// Rows run on from a river's end point (<paramref name="step"/> −1, its last) or start point (+1, its first) over
		/// <paramref name="reach"/> metres, nearest first, on the line's own heading at that point's surface; fading out
		/// over them when <paramref name="fading"/>.
		/// </summary>
		private void AddRunOnRows(List<Row> rows, SceneHydrology.River river, int from, int step, float reach, bool fading)
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
					Fade = fading ? 1f - k / (float)steps : 1f,
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
