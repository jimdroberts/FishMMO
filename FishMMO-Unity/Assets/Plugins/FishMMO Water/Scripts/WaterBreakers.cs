using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Water
{
	/// <summary>
	/// The breakers: waves that rise out of the still water at the break line, curl and land, with
	/// their spray and mist.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the sea does not break by itself.</b> The ocean is an FFT height field, and a height
	/// field cannot curl — one value of height per point of the sea, where a barrel has three. Every
	/// attempt to make it break by bending its output (a shoaling gain, a depth limit, a surf train, a
	/// forward shear) broke nothing and spoiled the open-sea waves near every coast. So it is a hybrid:
	/// the sea fades out over the shallows to still water at the depth waves break at
	/// (<see cref="WaterSurface.BreakDepth"/>), and the breakers are drawn from there on geometry of
	/// their own.
	/// </para>
	/// <para>
	/// <b>The seam is designed out, not blended.</b> The break line is the contour of that same depth,
	/// traced in the same shore field the sea reads, so the sheet's base lies exactly where the sea has
	/// gone flat. Its material is a copy of the sea's, and it is shaded by the sea's own function, so at
	/// its base — flat water, at the sea's own point — it is the sea's colour to the bit.
	/// </para>
	/// <para>
	/// <b>Nothing moves on the CPU.</b> The mesh only says where the break line is; each breaker's
	/// shape, its clock and its spray are worked out on the GPU from the shore's clock, so the whole
	/// coast costs nothing per frame here and every client sees the same waves. The line is traced
	/// again only when the camera has moved a good way, the tide or the sea state has moved the
	/// contour, or the terrain changed — and off the main thread.
	/// </para>
	/// </remarks>
	[ExecuteAlways]
	[AddComponentMenu("FishMMO/Water/Water Breakers")]
	[RequireComponent(typeof(WaterSurface), typeof(WaterShoreField), typeof(WaterShore))]
	public sealed class WaterBreakers : MonoBehaviour
	{
		/// <summary>The sheet's shader, for finding it when the reference was never set.</summary>
		public const string BreakerShaderName = "FishMMO/Water/Breaker";

		/// <summary>The spray's shader, for finding it when the reference was never set.</summary>
		public const string SprayShaderName = "FishMMO/Water/Spray";

		private static readonly int BreakLineId = Shader.PropertyToID("_FishWaterBreakLine");
		private static readonly int BreakLineInfoId = Shader.PropertyToID("_FishWaterBreakLineInfo");
		private static readonly int SprayId = Shader.PropertyToID("_Spray");
		private static readonly int MistId = Shader.PropertyToID("_Mist");
		private static readonly int BreakerHeightId = Shader.PropertyToID("_BreakerHeight");
		private static readonly int BreakerCurlId = Shader.PropertyToID("_BreakerCurl");

		[Tooltip("The breakers' sheet. Referenced so a build includes it; found by name otherwise.")]
		public Shader BreakerShader;
		[Tooltip("Their spray and mist. Referenced so a build includes it; found by name otherwise.")]
		public Shader SprayShader;

		[Header("Break line")]
		[Tooltip("How far from the camera the breakers are laid, in metres. Past it they are too small to matter, and the whitewater on the sea carries on.")]
		[Range(100f, 3000f)] public float Reach = 700f;
		[Tooltip("Metres between the points of the break line. Finer follows the shore more closely and costs more vertices.")]
		[Range(0.5f, 6f)] public float Spacing = 1.5f;
		[Tooltip("Break lines shorter than this are dropped, in metres: a rock a few metres across raises no breaker.")]
		[Range(2f, 200f)] public float ShortestLine = 14f;

		[Header("Spray and mist")]
		[Tooltip("Particles of spray: thrown off the lip, and splashed up where it lands. Drawn entirely on the GPU; 0 draws none.")]
		[Range(0, 65536)] public int SprayParticles = 16384;
		[Tooltip("Particles of mist: the haze that hangs over the impact and drifts downwind. 0 draws none.")]
		[Range(0, 16384)] public int MistParticles = 2048;
		[Tooltip("How much spray.")]
		[Range(0f, 3f)] public float Spray = 1f;
		[Tooltip("How much mist.")]
		[Range(0f, 3f)] public float Mist = 1f;

		/// <summary>Vertices across each branch of the cross-section: the back, the lip, the face (FishWaterBreakerProfile).</summary>
		private static readonly int[] BranchVertices = { 8, 12, 8 };

		/// <summary>The spline texture's width; it wraps onto more rows past this.</summary>
		private const int LineTextureWidth = 1024;

		private WaterSurface surface;
		private WaterShoreField field;
		private MeshRenderer oceanRenderer;
		private GameObject breakerHost;
		private MeshRenderer breakerRenderer;
		private Mesh breakerMesh;
		private Material breakerMaterial;
		private static readonly int BreakerShiftId = Shader.PropertyToID("_FishWaterBreakerShift");
		private GameObject sprayHost;
		private MeshRenderer sprayRenderer;
		private Mesh sprayMesh;
		private Material sprayMaterial;
		private int builtSpray = -1, builtMist = -1;
		private Texture2D lineTexture;
		private MaterialPropertyBlock block;

		private Task<Line> building;
		private Line drawn;
		private bool reportedShader;

		/// <summary>True once a break line is traced and on screen with no newer one being traced — for probes, which must wait for it.</summary>
		public bool IsTraced => drawn != null && building == null;

		/// <summary>Points in the break line on screen.</summary>
		public int LinePoints => drawn != null ? drawn.Points.Count : 0;

		// Lists kept between uploads, so a rebuild does not throw tens of thousands of entries away.
		private readonly List<Vector3> vertices = new List<Vector3>();
		private readonly List<Vector3> normals = new List<Vector3>();
		private readonly List<Vector4> profiles = new List<Vector4>();
		private readonly List<Vector4> shores = new List<Vector4>();
		private readonly List<int> triangles = new List<int>();

		/// <summary>The break line as traced: the points, which way the shore is from each, and what the shaders need of the shore there.</summary>
		private sealed class Line
		{
			public readonly List<Vector2> Points = new List<Vector2>();
			public readonly List<Vector2> Shoreward = new List<Vector2>();
			/// <summary>x metres to the water's edge, y how sure the shoreward direction is, z metres to the nearest open end, w the space shoreward it may reach into (<see cref="SpaceFor"/>).</summary>
			public readonly List<Vector4> Shore = new List<Vector4>();
			/// <summary>Where each piece starts in <see cref="Points"/>, and whether it closes on itself.</summary>
			public readonly List<(int start, int count, bool closed)> Pieces = new List<(int, int, bool)>();
			public Vector2 Centre;
			public float Depth;
			public int FieldVersion;
		}

		private void OnEnable()
		{
			surface = GetComponent<WaterSurface>();
			field = GetComponent<WaterShoreField>();
			oceanRenderer = GetComponent<MeshRenderer>();
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
		}

		private void OnDisable()
		{
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
			building = null;
			drawn = null;
			Shader.SetGlobalVector(BreakLineInfoId, Vector4.zero);
			Discard(breakerHost);
			Discard(breakerMesh);
			Discard(breakerMaterial);
			Discard(sprayHost);
			Discard(sprayMesh);
			Discard(sprayMaterial);
			Discard(lineTexture);
			breakerHost = sprayHost = null;
			breakerMesh = sprayMesh = null;
			breakerMaterial = sprayMaterial = null;
			lineTexture = null;
			builtSpray = builtMist = -1;
		}

		private static void Discard(Object victim)
		{
			if (victim == null)
			{
				return;
			}
			if (Application.isPlaying)
			{
				Destroy(victim);
			}
			else
			{
				DestroyImmediate(victim);
			}
		}

		/// <summary>WebGL has no threads; everywhere else the line is traced on the thread pool.</summary>
		private static bool CanBuildOffThread => Application.platform != RuntimePlatform.WebGLPlayer;

		private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (camera == null || camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.Preview)
			{
				return;
			}
			if (surface == null || field == null)
			{
				surface = GetComponent<WaterSurface>();
				field = GetComponent<WaterShoreField>();
				oceanRenderer = GetComponent<MeshRenderer>();
			}
			WaterShoreField.Snapshot shore = field != null ? field.Current : null;
			if (surface == null || shore == null || surface.BreakDepth <= 0f || !EnsureHosts())
			{
				Show(false);
				return;
			}

			// A finished trace replaces the one on screen.
			if (building != null && building.IsCompleted)
			{
				if (building.Status == TaskStatus.RanToCompletion && building.Result != null)
				{
					Upload(building.Result);
				}
				building = null;
			}

			/* Traced again once the contour has moved: the camera has gone a quarter of the reach, the
			 * terrain was rebuilt, or the tide or the sea have moved the depth the line follows by more
			 * than a quarter of a metre. In between, the sheet's base follows the contour in the vertex
			 * shader, moved along the way in by the depth it has shifted over the beach's own slope
			 * (_FishWaterBreakerShift). It was traced again every two centimetres — a metre sideways
			 * on a gentle beach, so a tide coming in or a sea getting up snapped every breaker along
			 * the coast to a new line, re-measured, every few seconds: the twitch. */
			var centre = new Vector2(camera.transform.position.x, camera.transform.position.z);
			float depth = surface.BreakDepth - surface.TideMetres;
			bool stale = drawn == null
				|| drawn.FieldVersion != shore.Version
				|| Mathf.Abs(drawn.Depth - depth) > 0.25f
				|| (drawn.Centre - centre).sqrMagnitude > Reach * Reach * 0.0625f;
			Shader.SetGlobalFloat(BreakerShiftId, drawn != null ? drawn.Depth - depth : 0f);
			if (stale && building == null)
			{
				float reach = Reach, spacing = Spacing, shortest = ShortestLine, breakDepth = surface.BreakDepth;
				if (CanBuildOffThread)
				{
					building = Task.Run(() => Trace(shore, centre, reach, depth, breakDepth, spacing, shortest));
				}
				else
				{
					Upload(Trace(shore, centre, reach, depth, breakDepth, spacing, shortest));
				}
			}

			SyncMaterials();
			EnsureSpray();
			Show(drawn != null && drawn.Points.Count > 1);
		}

		/// <summary>
		/// Traces the break line around a point: the contour where the water is the break depth deep,
		/// then everything the shaders need to know of the shore at each point of it. Any thread.
		/// </summary>
		/// <param name="depth">The contour, in the field's terms: the break depth at MEAN sea level, the tide taken off.</param>
		/// <param name="breakDepth">The break depth now, for how far the tide has moved the water's edge.</param>
		private static Line Trace(WaterShoreField.Snapshot shore, Vector2 centre, float reach, float depth,
			float breakDepth, float spacing, float shortest)
		{
			var line = new Line { Centre = centre, Depth = depth, FieldVersion = shore.Version };
			int resolution = shore.Resolution;
			float texel = shore.TexelMetres;
			Rect area = shore.Area;

			// The window of texels within reach, whose centres are area.min + (i + 0.5) texels.
			int x0 = Mathf.Clamp(Mathf.FloorToInt((centre.x - reach - area.xMin) / texel - 0.5f), 0, resolution - 1);
			int x1 = Mathf.Clamp(Mathf.CeilToInt((centre.x + reach - area.xMin) / texel - 0.5f), 0, resolution - 1);
			int y0 = Mathf.Clamp(Mathf.FloorToInt((centre.y - reach - area.yMin) / texel - 0.5f), 0, resolution - 1);
			int y1 = Mathf.Clamp(Mathf.CeilToInt((centre.y + reach - area.yMin) / texel - 0.5f), 0, resolution - 1);
			int width = x1 - x0 + 1, height = y1 - y0 + 1;
			if (width < 2 || height < 2)
			{
				return line;
			}
			var grid = new float[width * height];
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					grid[y * width + x] = shore.DepthAt(x0 + x, y0 + y);
				}
			}
			var origin = new Vector2(area.xMin + (x0 + 0.5f) * texel, area.yMin + (y0 + 0.5f) * texel);
			List<WaterBreakLine.Polyline> pieces = WaterBreakLine.Build(grid, width, height, origin, texel,
				depth, spacing, shortest);

			/* How far the tide has moved the water's edge from the field's mean one, along the way in: on a
			 * beach of even slope, the contour stands (break depth − tide) above the mean edge and the
			 * break depth above the real one, so the room scales by their ratio. */
			float tideScale = depth > 0.05f ? Mathf.Clamp(breakDepth / depth, 0.25f, 4f) : 1f;
			float tide = breakDepth - depth;
			float step = Mathf.Max(1f, texel);

			foreach (WaterBreakLine.Polyline piece in pieces)
			{
				int count = piece.Points.Count;
				if (count < 2)
				{
					continue;
				}
				int start = line.Points.Count;
				float length = piece.Length;
				float travelled = 0f;
				var rooms = new float[count];
				var confidences = new float[count];
				var spaces = new float[count];
				for (int i = 0; i < count; i++)
				{
					Vector2 here = piece.Points[i];
					if (i > 0)
					{
						travelled += Vector2.Distance(piece.Points[i - 1], here);
					}
					Vector2 previous = i > 0 ? piece.Points[i - 1] : piece.Closed ? piece.Points[count - 1] : here;
					Vector2 next = i < count - 1 ? piece.Points[i + 1] : piece.Closed ? piece.Points[0] : here;
					Vector2 tangent = next - previous;
					// The contour runs with the shallow water on its left, so its left is the shore.
					Vector2 shoreward = tangent.sqrMagnitude > 1e-10f
						? new Vector2(-tangent.y, tangent.x).normalized
						: Vector2.up;

					shore.TrySample(here, out _, out float edge);
					float room = Mathf.Max(0.25f, edge * tideScale);

					// How sure the way to the shore is: the distance field's own slope (FishWaterShoreFacing).
					shore.TrySample(here + new Vector2(step, 0f), out _, out float east);
					shore.TrySample(here - new Vector2(step, 0f), out _, out float west);
					shore.TrySample(here + new Vector2(0f, step), out _, out float north);
					shore.TrySample(here - new Vector2(0f, step), out _, out float south);
					float slope = new Vector2(east - west, north - south).magnitude / (2f * step);
					float confidence = Mathf.Clamp01((slope - 0.35f) / 0.4f);

					float ends = piece.Closed ? 10000f : Mathf.Min(travelled, length - travelled);
					rooms[i] = room;
					confidences[i] = confidence;
					spaces[i] = SpaceFor(shore, piece, i, here, shoreward, room, tide, texel);
					line.Points.Add(here);
					line.Shoreward.Add(shoreward);
					line.Shore.Add(new Vector4(0f, 0f, ends, 0f));
				}

				/* One crest, not a row of separate ones. Each point's room, confidence and space were
				 * measured on their own, and they are noisy from one point to the next — the room kinks
				 * where the distance field folds, the space stops in half-metre steps and the bend is
				 * judged from six neighbours — so neighbouring cross-sections stood at slightly different
				 * stages and widths and the crest came out torn. A real crest is one body of water whose
				 * height and timing change over tens of metres, so these are eased along the line over
				 * about a dozen metres. The space is never eased UPWARD: that would undo the limit that
				 * keeps breakers from crossing. Separate lines are not blended with each other: they are
				 * different waves, coming in from different directions. */
				int radius = Mathf.Max(1, Mathf.RoundToInt(CrestCoherenceMetres / Mathf.Max(0.1f, spacing)));
				SmoothAlong(rooms, piece.Closed, radius, false);
				SmoothAlong(confidences, piece.Closed, radius, false);
				SmoothAlong(spaces, piece.Closed, radius, true);
				for (int i = 0; i < count; i++)
				{
					Vector4 at = line.Shore[start + i];
					line.Shore[start + i] = new Vector4(rooms[i], confidences[i], at.z, spaces[i]);
				}
				line.Pieces.Add((start, count, piece.Closed));
			}
			return line;
		}

		/// <summary>Half the length over which one breaker's crest is taken to be one body of water, in metres.</summary>
		private const float CrestCoherenceMetres = 6f;

		/// <summary>
		/// Eases a quantity measured at each point of a line over this many points either side: a
		/// triangle-weighted mean, wrapping round a loop and held at an open line's ends. Any thread.
		/// </summary>
		/// <param name="neverRaise">
		/// Never above the value measured at any point: the least within the window is taken first, and
		/// only that eased. Every eased value is a mean of minimums over windows that each contain the
		/// point, so none can exceed what was measured there — the property the space depends on.
		/// </param>
		public static void SmoothAlong(float[] values, bool closed, int radius, bool neverRaise)
		{
			int count = values.Length;
			if (count < 2 || radius < 1)
			{
				return;
			}
			int Wrap(int i) => closed ? ((i % count) + count) % count : System.Math.Max(0, System.Math.Min(count - 1, i));

			float[] source = values;
			if (neverRaise)
			{
				source = new float[count];
				for (int i = 0; i < count; i++)
				{
					float least = values[i];
					for (int k = -radius; k <= radius; k++)
					{
						least = System.Math.Min(least, values[Wrap(i + k)]);
					}
					source[i] = least;
				}
			}

			var eased = new float[count];
			for (int i = 0; i < count; i++)
			{
				float sum = 0f, weights = 0f;
				for (int k = -radius; k <= radius; k++)
				{
					float weight = radius + 1 - System.Math.Abs(k);
					sum += source[Wrap(i + k)] * weight;
					weights += weight;
				}
				eased[i] = sum / weights;
			}
			System.Array.Copy(eased, values, count);
		}

		/// <summary>
		/// How far shoreward the breaker at one point of the line may reach without meeting another, in
		/// metres. Any thread.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Every cross-section throws its water square to the break line</b>, and those directions do
		/// not stay apart. Where the line bends toward the shore — round a headland, an island, the head
		/// of an inlet — they converge, and a throw longer than the bend's radius carried each
		/// cross-section through its neighbours: the sheet turned inside out and the barrels seemed to
		/// form outward. Over a shoal with no dry land near, the room was the way to some shore a hundred
		/// metres off, so nothing stopped the breakers from all round it being thrown into its middle and
		/// through each other.
		/// </para>
		/// <para>
		/// So the space is the nearer of two things: where the water, going shoreward, stops getting
		/// shallower — the crest of a shoal, past which the breaker from the other side is coming the
		/// other way — and most of the radius of the bend here, when it bends toward the shore. Where
		/// neither is near, it is the room to the water's edge, as before.
		/// </para>
		/// </remarks>
		public static float SpaceFor(WaterShoreField.Snapshot shore, WaterBreakLine.Polyline piece, int index,
			Vector2 here, Vector2 shoreward, float room, float tide, float texel)
		{
			const float Longest = 40f;
			float space = Mathf.Min(room, Longest);

			// Shoreward until the water stops shoaling: a crest crossed, or the shore reached.
			float march = Mathf.Max(0.5f, texel * 0.5f);
			float shallowest = float.MaxValue, atShallowest = 0f;
			for (float s = march; s <= space; s += march)
			{
				if (!shore.TrySample(here + shoreward * s, out float meanDepth, out _))
				{
					break;
				}
				float now = meanDepth + tide;
				if (now <= 0f)
				{
					break;
				}
				if (now < shallowest)
				{
					shallowest = now;
					atShallowest = s;
				}
				else if (now > shallowest + 0.1f)
				{
					// Deepening again: past a crest, where another breaker is coming the other way.
					space = Mathf.Max(march, atShallowest);
					break;
				}
			}

			// How tightly the line bends toward the shore here, over three points either side.
			List<Vector2> points = piece.Points;
			int count = points.Count;
			const int Reach = 3;
			int before = index - Reach, after = index + Reach;
			if (piece.Closed)
			{
				before = (before % count + count) % count;
				after %= count;
			}
			else
			{
				before = Mathf.Max(0, before);
				after = Mathf.Min(count - 1, after);
			}
			Vector2 incoming = here - points[before];
			Vector2 outgoing = points[after] - here;
			float inLength = incoming.magnitude, outLength = outgoing.magnitude;
			if (inLength > 1e-3f && outLength > 1e-3f)
			{
				// Positive turning left: toward the shallow side, where the cross-sections converge.
				float cross = (incoming.x * outgoing.y - incoming.y * outgoing.x) / (inLength * outLength);
				float dot = Vector2.Dot(incoming, outgoing) / (inLength * outLength);
				float turn = Mathf.Atan2(cross, dot);
				if (turn > 1e-4f)
				{
					float radius = 0.5f * (inLength + outLength) / turn;
					space = Mathf.Min(space, 0.8f * radius);
				}
			}
			return Mathf.Max(0.25f, space);
		}

		/// <summary>Builds the sheet and the particles' copy of the line from a trace. Main thread.</summary>
		private void Upload(Line line)
		{
			drawn = line;
			vertices.Clear();
			normals.Clear();
			profiles.Clear();
			shores.Clear();
			triangles.Clear();

			int across = 0;
			foreach (int n in BranchVertices)
			{
				across += n;
			}

			for (int i = 0; i < line.Points.Count; i++)
			{
				Vector2 point = line.Points[i];
				Vector2 shoreward = line.Shoreward[i];
				Vector4 shore = line.Shore[i];
				for (int branch = 0; branch < BranchVertices.Length; branch++)
				{
					int n = BranchVertices[branch];
					for (int j = 0; j < n; j++)
					{
						// Every vertex ON the break line, at the still water; the shader lifts it into shape.
						vertices.Add(new Vector3(point.x, 0f, point.y));
						normals.Add(new Vector3(shoreward.x, 0f, shoreward.y));
						profiles.Add(new Vector4(j / (float)(n - 1), branch, 0f, 0f));
						shores.Add(shore);
					}
				}
			}

			foreach ((int start, int count, bool closed) in line.Pieces)
			{
				int spans = closed ? count : count - 1;
				for (int k = 0; k < spans; k++)
				{
					int a = (start + k) * across;
					int b = (start + (k + 1) % count) * across;
					int offset = 0;
					foreach (int n in BranchVertices)
					{
						for (int j = 0; j < n - 1; j++)
						{
							int a0 = a + offset + j, a1 = a0 + 1;
							int b0 = b + offset + j, b1 = b0 + 1;
							triangles.Add(a0);
							triangles.Add(b0);
							triangles.Add(b1);
							triangles.Add(a0);
							triangles.Add(b1);
							triangles.Add(a1);
						}
						offset += n;
					}
				}
			}

			breakerMesh.Clear();
			breakerMesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			breakerMesh.SetVertices(vertices);
			breakerMesh.SetNormals(normals);
			breakerMesh.SetUVs(0, profiles);
			breakerMesh.SetUVs(1, shores);
			breakerMesh.SetTriangles(triangles, 0, false);
			// Enormous: the vertex shader makes the shape, and the host follows the camera.
			breakerMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);

			UploadLineTexture(line);
		}

		/// <summary>
		/// The line as a texture the spray reads a point of at random: two halves, row for row — the
		/// point and its shoreward direction, then its room, confidence, distance to an end and space.
		/// </summary>
		/// <remarks>
		/// Full float, not half: a half float holds a world position to a metre or two at two kilometres
		/// from the origin, and the spray would leave the breaker it belongs to. Read with Load, which
		/// needs no filtering — WebGPU and WebGL2 cannot filter a 32-bit float texture.
		/// </remarks>
		private void UploadLineTexture(Line line)
		{
			int count = line.Points.Count;
			int width = Mathf.Clamp(count, 1, LineTextureWidth);
			int rows = Mathf.Max(1, (count + width - 1) / width);
			int height = rows * 2;
			if (lineTexture == null)
			{
				lineTexture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
				{
					name = "Break line",
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.HideAndDontSave,
				};
			}
			else if (lineTexture.width != width || lineTexture.height != height)
			{
				// Resized in place: this runs inside a render callback, where Unity refuses DestroyImmediate.
				lineTexture.Reinitialize(width, height, TextureFormat.RGBAFloat, false);
			}
			var data = new float[width * height * 4];
			for (int i = 0; i < count; i++)
			{
				int x = i % width, y = i / width;
				int a = (y * width + x) * 4;
				int b = ((rows + y) * width + x) * 4;
				data[a] = line.Points[i].x;
				data[a + 1] = line.Points[i].y;
				data[a + 2] = line.Shoreward[i].x;
				data[a + 3] = line.Shoreward[i].y;
				data[b] = line.Shore[i].x;
				data[b + 1] = line.Shore[i].y;
				data[b + 2] = line.Shore[i].z;
				data[b + 3] = line.Shore[i].w;
			}
			lineTexture.SetPixelData(data, 0);
			lineTexture.Apply(false, false);
			Shader.SetGlobalTexture(BreakLineId, lineTexture);
			Shader.SetGlobalVector(BreakLineInfoId, new Vector4(count, width, rows, Spacing));
		}

		/// <summary>Creates the sheet's and the spray's hosts once their shaders are there. False while they are not.</summary>
		private bool EnsureHosts()
		{
			if (breakerMaterial == null)
			{
				Shader shader = BreakerShader != null ? BreakerShader : Shader.Find(BreakerShaderName);
				if (shader == null || !shader.isSupported)
				{
					if (!reportedShader)
					{
						reportedShader = true;
						Debug.LogWarning($"[Water] '{BreakerShaderName}' is missing or unsupported here, so there are no breakers.", this);
					}
					return false;
				}
				breakerMaterial = new Material(shader) { name = "Breakers", hideFlags = HideFlags.HideAndDontSave };
			}
			if (breakerMesh == null)
			{
				breakerMesh = new Mesh { name = "Breakers", hideFlags = HideFlags.HideAndDontSave };
				drawn = null;
			}
			if (breakerHost == null)
			{
				breakerHost = new GameObject("Breakers") { hideFlags = HideFlags.DontSave };
				breakerHost.transform.SetParent(transform, false);
				breakerHost.AddComponent<MeshFilter>().sharedMesh = breakerMesh;
				breakerRenderer = breakerHost.AddComponent<MeshRenderer>();
				breakerRenderer.sharedMaterial = breakerMaterial;
				// Water must not cast: it would darken the sea bed it exists to show.
				breakerRenderer.shadowCastingMode = ShadowCastingMode.Off;
				breakerRenderer.receiveShadows = true;
				breakerRenderer.lightProbeUsage = LightProbeUsage.Off;
				breakerRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
				breakerRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
			}
			return true;
		}

		/// <summary>The spray's quads, rebuilt only when the counts change.</summary>
		private void EnsureSpray()
		{
			int spray = Mathf.Max(0, SprayParticles), mist = Mathf.Max(0, MistParticles);
			if (spray + mist == 0)
			{
				if (sprayHost != null)
				{
					sprayHost.SetActive(false);
				}
				return;
			}
			if (sprayMaterial == null)
			{
				Shader shader = SprayShader != null ? SprayShader : Shader.Find(SprayShaderName);
				if (shader == null || !shader.isSupported)
				{
					return;
				}
				sprayMaterial = new Material(shader) { name = "Surf spray", hideFlags = HideFlags.HideAndDontSave };
			}
			if (sprayMesh == null || builtSpray != spray || builtMist != mist)
			{
				if (sprayMesh == null)
				{
					sprayMesh = new Mesh { name = "Surf spray", hideFlags = HideFlags.HideAndDontSave };
				}
				FillSprayMesh(sprayMesh, spray, mist);
				builtSpray = spray;
				builtMist = mist;
			}
			if (sprayHost == null)
			{
				sprayHost = new GameObject("Surf spray") { hideFlags = HideFlags.DontSave };
				sprayHost.transform.SetParent(transform, false);
				sprayHost.AddComponent<MeshFilter>().sharedMesh = sprayMesh;
				sprayRenderer = sprayHost.AddComponent<MeshRenderer>();
				sprayRenderer.sharedMaterial = sprayMaterial;
				sprayRenderer.shadowCastingMode = ShadowCastingMode.Off;
				sprayRenderer.receiveShadows = false;
				sprayRenderer.lightProbeUsage = LightProbeUsage.Off;
				sprayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
				sprayRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
			}
			sprayMaterial.SetFloat(SprayId, Spray);
			sprayMaterial.SetFloat(MistId, Mist);
		}

		/// <summary>
		/// A quad per particle, every corner of it at the origin: which corner, what kind (0 spray off
		/// the lip, 1 splash where it lands, 2 mist) and four random numbers are all it carries. The
		/// shader decides where each one is, from the breaker it picks.
		/// </summary>
		/// <remarks>Seeded, so every client draws the same spray from the same breakers.</remarks>
		private static void FillSprayMesh(Mesh mesh, int spray, int mist)
		{
			int total = spray + mist;
			var random = new System.Random(20260926);
			var positions = new Vector3[total * 4];
			var corners = new Vector4[total * 4];
			var randoms = new Vector4[total * 4];
			var indices = new int[total * 6];
			for (int i = 0; i < total; i++)
			{
				// Of the spray, three in five come off the lip and the rest splash up where it lands.
				float kind = i >= spray ? 2f : random.NextDouble() < 0.6 ? 0f : 1f;
				var r = new Vector4((float)random.NextDouble(), (float)random.NextDouble(),
					(float)random.NextDouble(), (float)random.NextDouble());
				float extra = (float)random.NextDouble();
				int v = i * 4;
				corners[v] = new Vector4(0f, 0f, kind, extra);
				corners[v + 1] = new Vector4(1f, 0f, kind, extra);
				corners[v + 2] = new Vector4(1f, 1f, kind, extra);
				corners[v + 3] = new Vector4(0f, 1f, kind, extra);
				for (int c = 0; c < 4; c++)
				{
					randoms[v + c] = r;
				}
				int t = i * 6;
				indices[t] = v;
				indices[t + 1] = v + 2;
				indices[t + 2] = v + 1;
				indices[t + 3] = v;
				indices[t + 4] = v + 3;
				indices[t + 5] = v + 2;
			}
			mesh.Clear();
			mesh.indexFormat = positions.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			mesh.vertices = positions;
			mesh.SetUVs(0, corners);
			mesh.SetUVs(1, randoms);
			mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
			mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
		}

		/// <summary>
		/// The sheet's material is the sea's, property for property, and its renderer carries the sea's
		/// block — the climate's colours and absorption (WaterEnvironment). Every frame, because either can
		/// change at any time and a breaker a shade off the sea is a seam.
		/// </summary>
		/// <remarks>
		/// By the ocean shader's own property list rather than CopyPropertiesFromMaterial, which would
		/// also copy the ocean's render queue and keywords onto the sheet.
		/// </remarks>
		private void SyncMaterials()
		{
			Material ocean = surface.Material;
			if (ocean != null && breakerMaterial != null)
			{
				Shader shader = ocean.shader;
				int count = shader.GetPropertyCount();
				for (int i = 0; i < count; i++)
				{
					int id = shader.GetPropertyNameId(i);
					switch (shader.GetPropertyType(i))
					{
						case ShaderPropertyType.Color:
							breakerMaterial.SetColor(id, ocean.GetColor(id));
							break;
						case ShaderPropertyType.Vector:
							breakerMaterial.SetVector(id, ocean.GetVector(id));
							break;
						case ShaderPropertyType.Float:
						case ShaderPropertyType.Range:
							breakerMaterial.SetFloat(id, ocean.GetFloat(id));
							break;
						case ShaderPropertyType.Int:
							breakerMaterial.SetInteger(id, ocean.GetInteger(id));
							break;
						case ShaderPropertyType.Texture:
							breakerMaterial.SetTexture(id, ocean.GetTexture(id));
							break;
					}
				}
			}
			if (oceanRenderer != null && breakerRenderer != null)
			{
				block ??= new MaterialPropertyBlock();
				oceanRenderer.GetPropertyBlock(block);
				breakerRenderer.SetPropertyBlock(block);
			}
			if (sprayMaterial != null && breakerMaterial != null)
			{
				// The spray rides the same breakers, so it takes the same size and barrel.
				sprayMaterial.SetFloat(BreakerHeightId, breakerMaterial.GetFloat(BreakerHeightId));
				sprayMaterial.SetFloat(BreakerCurlId, breakerMaterial.GetFloat(BreakerCurlId));
			}
		}

		private void Show(bool visible)
		{
			if (breakerHost != null && breakerHost.activeSelf != visible)
			{
				breakerHost.SetActive(visible);
			}
			bool spray = visible && SprayParticles + MistParticles > 0;
			if (sprayHost != null && sprayHost.activeSelf != spray)
			{
				sprayHost.SetActive(spray);
			}
			if (!visible)
			{
				Shader.SetGlobalVector(BreakLineInfoId, Vector4.zero);
			}
			else if (lineTexture != null && drawn != null)
			{
				Shader.SetGlobalTexture(BreakLineId, lineTexture);
				Shader.SetGlobalVector(BreakLineInfoId, new Vector4(drawn.Points.Count,
					lineTexture.width, lineTexture.height / 2, Spacing));
			}
		}
	}
}
