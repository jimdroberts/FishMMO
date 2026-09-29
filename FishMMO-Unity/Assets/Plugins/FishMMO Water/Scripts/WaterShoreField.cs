using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Water
{
	/// <summary>
	/// How deep the water is over every part of the scene, as a texture the shader can read.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the waves need this at all.</b> Everything a shoreline does is a function of depth.
	/// A wave entering shallow water slows, shortens and grows until it is too tall for the water
	/// under it and breaks; the surf line sits where that happens; the swash runs up the sand until
	/// it runs out of height. None of that can be worked out from the camera's depth buffer,
	/// because the answer is needed in the VERTEX stage — the wave has to be a different shape
	/// there — and a vertex knows nothing about what is behind it on screen.
	/// </para>
	/// <para>
	/// <b>Built at load, not baked to an asset.</b> Sampling a scene's terrains at 512 x 512 takes
	/// a few milliseconds, and the result depends on nothing but the terrain — so an asset would be
	/// a second copy of something already in the scene, with all the staleness that implies. It
	/// also means a designer raising the beach sees the surf move immediately.
	/// </para>
	/// </remarks>
	[ExecuteAlways]
	[AddComponentMenu("FishMMO/Water/Water Shore Field")]
	[RequireComponent(typeof(WaterSurface))]
	public sealed class WaterShoreField : MonoBehaviour
	{
		private static readonly int FieldId = Shader.PropertyToID("_FishWaterShore");
		private static readonly int RectId = Shader.PropertyToID("_FishWaterShoreRect");
		private static readonly int RangeId = Shader.PropertyToID("_FishWaterShoreRange");
		private static readonly int TexelId = Shader.PropertyToID("_FishWaterShoreTexel");
		private static readonly int OpenSeaId = Shader.PropertyToID("_FishWaterOpenSea");
		private static readonly int OpenSeaRectId = Shader.PropertyToID("_FishWaterOpenSeaRect");
		private static readonly int OpenSeaTidesId = Shader.PropertyToID("_FishWaterOpenSeaTides");

		/// <summary>
		/// How many metres one texel should cover.
		/// </summary>
		/// <remarks>
		/// <b>This is the number that decides whether a shoreline looks like a shoreline.</b> The
		/// wave amplitude is clamped by the depth under it, so the depth field's texel size is
		/// also the granularity of the WAVE HEIGHT — at 5.6 m a texel, measured, the sea surface
		/// steps in 5.6 m blocks and the waterline inherits every one of them as a stair. One
		/// metre puts the steps below the size of the swash itself.
		/// </remarks>
		[Tooltip("Metres per texel to aim for. 1 m keeps the waterline smooth; the resolution follows from the scene size.")]
		[Range(0.25f, 16f)] public float TargetTexelMetres = 1f;

		/// <summary>
		/// The most texels a side, whatever the scene size.
		/// </summary>
		/// <remarks>
		/// 2048² of R16 is 8 MB, which is affordable. A 20 km scene still lands at about 10 m a
		/// texel at that cap, so the very largest scenes keep a coarse field — the answer there is
		/// a field that follows the camera, not a bigger one.
		/// </remarks>
		[Tooltip("Upper limit on resolution. 2048 is 8 MB.")]
		[Range(64, 4096)] public int MaximumResolution = 2048;

		/// <summary>Metres a texel actually covers, once the scene has been measured.</summary>
		public float TexelMetres { get; private set; }

		/// <summary>
		/// The narrowest gap, in metres, the sea's waves come through: water joined to the open sea only by
		/// narrower ones is sheltered — a lagoon, a pool behind a bar — and lies still (<see cref="OpenSeaAt"/>).
		/// </summary>
		[Tooltip("The narrowest gap (m) the sea's swell comes through. Water joined to the open sea only by narrower gaps — pools, lagoons behind bars — lies still: no waves, surf or breakers.")]
		[Range(4f, 100f)] public float ShelterGap = 16f;

		/// <summary>The least water, in metres, over a way in for the sea's waves to come through it.</summary>
		[Tooltip("The least water (m) over a way in for the swell to come through it. A pool whose way to the sea is shallower than this — at the tide of the moment — lies still.")]
		[Range(0.1f, 3f)] public float ShelterDepth = 0.5f;

		/// <summary>The highest the tide rises, m over mean sea level: ground under it can be a way in once covered.</summary>
		[Tooltip("The highest the tide rises (m over mean sea level). Ground under it — a bar dry at mean tide — is a way in for the swell once the tide covers it.")]
		[Range(0f, 15f)] public float HighestTide = 3f;

		[Tooltip("Rebuild when the terrain changes. Off is one build at load, which is all a shipped scene needs.")]
		public bool RebuildOnValidate = true;

		/// <summary>Depth recorded where the scene has no terrain at all, in metres.</summary>
		/// <remarks>
		/// Deep enough that no wave shoals against it, small enough to stay well inside a half.
		/// </remarks>
		private const float OpenWaterDepth = 400f;

		private Texture2D field;
		private Texture2D openSea;
		private Rect area;
		private float deepest;
		private WaterSurface surface;
		private volatile Snapshot snapshot;
		private int version;

		/// <summary>The world-space rectangle the field covers.</summary>
		public Rect Area => area;

		/// <summary>Texels along each side of the field; 0 before it is built.</summary>
		public int Resolution => snapshot != null ? snapshot.Resolution : 0;

		/// <summary>
		/// The field as last built, readable from any thread; null before the first build. A rebuild
		/// replaces it whole, so a reader holding one keeps a consistent field however long it takes.
		/// </summary>
		public Snapshot Current => snapshot;

		/// <summary>
		/// The field's values exactly as the GPU has them — the same half floats — kept on the CPU for
		/// buoyancy and for the break line, which is built on a worker thread where no Texture2D may be
		/// touched.
		/// </summary>
		/// <remarks>
		/// <b>Instead of the texture's own CPU copy, not as well as it.</b> The texture is uploaded and
		/// made non-readable, so the field costs one copy in memory — 16 MB at 2048² — rather than two.
		/// </remarks>
		public sealed class Snapshot
		{
			/// <summary>R then G of every texel, row by row: depth at mean sea level, then signed distance to the mean waterline.</summary>
			public readonly ushort[] Halves;
			public readonly int Resolution;
			/// <summary>The world rectangle covered; texel (x, y) is centred at its minimum plus (x + 0.5, y + 0.5) texels.</summary>
			public readonly Rect Area;
			public readonly float TexelMetres;
			/// <summary>Counts builds, so anything made from the field knows when it is out of date.</summary>
			public readonly int Version;
			/// <summary>
			/// The tide, in metres over mean sea level, at which each cell of the open-sea map has a way in from
			/// the open sea deep enough for its waves (<see cref="OpenSea"/>); null when there is none. Row-major,
			/// <see cref="OpenResolution"/> a side.
			/// </summary>
			public readonly float[] JoinTide;
			/// <summary>
			/// How much of the swell the narrowest gap on each cell's way in lets through, 0..1, at each of
			/// <see cref="ShelterTides"/> of <see cref="HighestTide"/>, three to a cell (<see cref="OpenSea"/>).
			/// </summary>
			public readonly float[] Shelter;
			public readonly int OpenResolution;
			/// <summary>The highest tide the gap floods were run up to, m.</summary>
			public readonly float HighestTide;

			public Snapshot(ushort[] halves, int resolution, Rect area, float texelMetres, int version,
				float[] joinTide = null, float[] shelter = null, int openResolution = 0, float highestTide = 0f)
			{
				HighestTide = highestTide;
				Halves = halves;
				Resolution = resolution;
				Area = area;
				TexelMetres = texelMetres;
				Version = version;
				JoinTide = joinTide;
				Shelter = shelter;
				OpenResolution = joinTide != null && shelter != null ? openResolution : 0;
			}

			/// <summary>
			/// How open to the sea's waves the water at a point is at a tide, 0..1: 1 joined to the open sea by a
			/// way in deep and wide enough, 0 in a pool or a lagoon cut off from it. FishWaterOpenSea, line for line.
			/// </summary>
			public float OpenSeaAt(Vector2 xz, float tide)
			{
				if (OpenResolution < 2 || Area.width < 1f)
				{
					return 1f;
				}
				float u = (xz.x - Area.xMin) / Area.width;
				float v = (xz.y - Area.yMin) / Area.height;
				if (u < 0f || u > 1f || v < 0f || v > 1f)
				{
					return 1f;
				}
				int n = OpenResolution;
				float fx = u * n - 0.5f, fy = v * n - 0.5f;
				int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, n - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, n - 1);
				int x1 = Mathf.Min(x0 + 1, n - 1), y1 = Mathf.Min(y0 + 1, n - 1);
				float tx = Mathf.Clamp01(fx - Mathf.Floor(fx)), ty = Mathf.Clamp01(fy - Mathf.Floor(fy));
				float joins = Mathf.Lerp(Mathf.Lerp(JoinTide[y0 * n + x0], JoinTide[y0 * n + x1], tx),
					Mathf.Lerp(JoinTide[y1 * n + x0], JoinTide[y1 * n + x1], tx), ty);
				float shelter = ShelterAt(Level(0), Level(1), Level(2), tide, HighestTide);
				return OpenFromJoin(tide, joins) * shelter;

				float Level(int k) => Mathf.Lerp(
					Mathf.Lerp(Shelter[(y0 * n + x0) * 3 + k], Shelter[(y0 * n + x1) * 3 + k], tx),
					Mathf.Lerp(Shelter[(y1 * n + x0) * 3 + k], Shelter[(y1 * n + x1) * 3 + k], tx), ty);
			}

			/// <summary>Metres of water over the ground at mean sea level, at a texel.</summary>
			public float DepthAt(int x, int y) => HalfToFloat(Halves[(y * Resolution + x) * 2]);

			/// <summary>
			/// The field at a world point, filtered as the GPU filters it: bilinear between texel
			/// centres, clamped at the edges. False off the field.
			/// </summary>
			public bool TrySample(Vector2 xz, out float depth, out float edgeDistance)
			{
				depth = OpenWaterDepth;
				edgeDistance = 1000f;
				if (Area.width < 1f || Area.height < 1f)
				{
					return false;
				}
				float u = (xz.x - Area.xMin) / Area.width;
				float v = (xz.y - Area.yMin) / Area.height;
				if (u < 0f || u > 1f || v < 0f || v > 1f)
				{
					return false;
				}
				float fx = u * Resolution - 0.5f;
				float fy = v * Resolution - 0.5f;
				int x0 = Mathf.FloorToInt(fx);
				int y0 = Mathf.FloorToInt(fy);
				float tx = fx - x0;
				float ty = fy - y0;
				int x1 = Mathf.Clamp(x0 + 1, 0, Resolution - 1);
				int y1 = Mathf.Clamp(y0 + 1, 0, Resolution - 1);
				x0 = Mathf.Clamp(x0, 0, Resolution - 1);
				y0 = Mathf.Clamp(y0, 0, Resolution - 1);
				depth = Bilinear(0, x0, y0, x1, y1, tx, ty);
				edgeDistance = Bilinear(1, x0, y0, x1, y1, tx, ty);
				return true;
			}

			private float Bilinear(int channel, int x0, int y0, int x1, int y1, float tx, float ty)
			{
				float a = HalfToFloat(Halves[(y0 * Resolution + x0) * 2 + channel]);
				float b = HalfToFloat(Halves[(y0 * Resolution + x1) * 2 + channel]);
				float c = HalfToFloat(Halves[(y1 * Resolution + x0) * 2 + channel]);
				float d = HalfToFloat(Halves[(y1 * Resolution + x1) * 2 + channel]);
				return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
			}
		}

		/// <summary>
		/// An IEEE half float's value, in plain managed arithmetic so it may run on any thread.
		/// </summary>
		/// <summary>How open water is at a tide, from the tide its way in needs: over ±15 cm of tide.</summary>
		public static float OpenFromJoin(float tide, float joinTide) => Mathf.Clamp01((tide - joinTide) / 0.3f + 0.5f);

		public static float HalfToFloat(ushort half)
		{
			int sign = (half >> 15) & 1;
			int exponent = (half >> 10) & 0x1F;
			int mantissa = half & 0x3FF;
			float value;
			if (exponent == 0)
			{
				// Subnormal: mantissa × 2^-24.
				value = mantissa * (1f / 16777216f);
			}
			else if (exponent == 31)
			{
				value = mantissa == 0 ? float.PositiveInfinity : float.NaN;
			}
			else
			{
				value = System.BitConverter.Int32BitsToSingle(((exponent - 15 + 127) << 23) | (mantissa << 13));
			}
			return sign != 0 ? -value : value;
		}

		/// <summary>
		/// The field at a world point, on the CPU: metres of water over the ground at MEAN sea level
		/// (negative on land), and signed metres to the mean waterline (positive at sea). False off
		/// the field or before it is built.
		/// </summary>
		/// <remarks>
		/// The same half floats the shader shoals the sea with, filtered the same way, so buoyancy and
		/// the break line ask exactly the field the GPU draws with.
		/// </remarks>
		/// <summary>How open to the sea's waves the water at a point is at a tide (<see cref="Snapshot.OpenSeaAt"/>); 1 before a build.</summary>
		public float OpenSeaAt(Vector2 xz, float tide)
		{
			Snapshot current = snapshot;
			return current != null ? current.OpenSeaAt(xz, tide) : 1f;
		}

		public bool TrySample(Vector2 xz, out float depth, out float edgeDistance)
		{
			Snapshot current = snapshot;
			if (current == null)
			{
				depth = OpenWaterDepth;
				edgeDistance = 1000f;
				return false;
			}
			return current.TrySample(xz, out depth, out edgeDistance);
		}

		private void OnEnable()
		{
			surface = GetComponent<WaterSurface>();
			Build();
		}

		private void OnDisable()
		{
			// Leave the globals pointing at nothing, or the next scene reads this scene's beach.
			Shader.SetGlobalVector(RectId, Vector4.zero);
			Shader.SetGlobalVector(OpenSeaRectId, Vector4.zero);
			Discard(field);
			field = null;
			Discard(openSea);
			openSea = null;
			snapshot = null;
		}

		private void OnValidate()
		{
			if (!RebuildOnValidate || !isActiveAndEnabled)
			{
				return;
			}
#if UNITY_EDITOR
			/* Deferred to the next editor tick, and coalesced.
			 *
			 * A build samples every terrain up to 2048 x 2048 times and runs a two-pass distance
			 * transform over the result. OnValidate fires on every keystroke in the inspector and
			 * on every domain reload — where OnEnable then builds again straight afterwards — so
			 * building synchronously here froze the editor for each of those, twice per recompile.
			 * Unsubscribing first means ten edits in one frame cost one build. */
			UnityEditor.EditorApplication.delayCall -= DeferredBuild;
			UnityEditor.EditorApplication.delayCall += DeferredBuild;
#else
			Build();
#endif
		}

#if UNITY_EDITOR
		private void DeferredBuild()
		{
			// The component can be gone by the time the editor gets round to this.
			if (this != null && isActiveAndEnabled)
			{
				Build();
			}
		}
#endif

		/// <summary>Reads the scene's terrains and rebuilds the depth field.</summary>
		public void Build()
		{
			var terrains = new List<Terrain>();
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				if (terrain != null && terrain.terrainData != null
					&& terrain.gameObject.scene == gameObject.scene)
				{
					terrains.Add(terrain);
				}
			}
			if (terrains.Count == 0)
			{
				// Open ocean with no ground in the scene: no shore, no shallows, no breakers.
				Shader.SetGlobalVector(RectId, Vector4.zero);
				Shader.SetGlobalVector(OpenSeaRectId, Vector4.zero);
				snapshot = null;
				return;
			}

			float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
			for (int i = 0; i < terrains.Count; i++)
			{
				Vector3 origin = terrains[i].GetPosition();
				Vector3 size = terrains[i].terrainData.size;
				minX = Mathf.Min(minX, origin.x);
				minZ = Mathf.Min(minZ, origin.z);
				maxX = Mathf.Max(maxX, origin.x + size.x);
				maxZ = Mathf.Max(maxZ, origin.z + size.z);
			}

			/* Padded by a tile's worth, so the field's own edge is not the shoreline. Sampled
			 * exactly at the boundary, the bilinear filter would blend the last row of real ground
			 * with whatever the clamp returns and draw a false beach along the scene's edge. */
			float pad = Mathf.Max(32f, (maxX - minX) * 0.05f);
			area = Rect.MinMaxRect(minX - pad, minZ - pad, maxX + pad, maxZ + pad);
			/* SQUARE, padding the short side with more open water. The texture is square, so a
			 * rectangular scene gave rectangular texels — 3.49 by 2.96 m on Cov Viaduct — while the
			 * distance transform counted steps as if they were square, and every distance measured
			 * north-south came out an eighth too long. */
			float side = Mathf.Max(area.width, area.height);
			area = new Rect(area.center.x - side * 0.5f, area.center.y - side * 0.5f, side, side);

			/* Resolution FOLLOWS the scene, rather than the scene being squeezed into a fixed
			 * resolution: a small bay and a twenty-kilometre coast want very different grids, and
			 * the thing that has to stay constant is the metres a texel covers. */
			float span = Mathf.Max(area.width, area.height);
			int wanted = Mathf.CeilToInt(span / Mathf.Max(0.05f, TargetTexelMetres));
			int resolution = Mathf.Clamp(Mathf.NextPowerOfTwo(wanted), 64,
				Mathf.Clamp(Mathf.NextPowerOfTwo(MaximumResolution), 64, 4096));
			TexelMetres = span / resolution;

			float sea = surface != null ? surface.MeanSeaLevel : transform.position.y;
			var pixels = new Color[resolution * resolution];
			deepest = 0f;

			for (int y = 0; y < resolution; y++)
			{
				float wz = Mathf.Lerp(area.yMin, area.yMax, (y + 0.5f) / resolution);
				for (int x = 0; x < resolution; x++)
				{
					float wx = Mathf.Lerp(area.xMin, area.xMax, (x + 0.5f) / resolution);
					float ground = Ground(terrains, wx, wz);
					// Positive in the water, negative on dry land. One metre is one unit. Off every
					// terrain is open water, not a trench a mile deep.
					float depth = ground > float.MinValue ? sea - ground : OpenWaterDepth;
					deepest = Mathf.Max(deepest, depth);
					pixels[y * resolution + x] = new Color(depth, 0f, 0f, 0f);
				}
			}

			/* The SIGNED DISTANCE to the waterline, in metres, in the green channel.
			 *
			 * Depth alone cannot drive a shoreline. Everything a beach does is organised by how
			 * far a point is from the water's edge: waves arrive as fronts parallel to it, the
			 * swash runs up a distance and drains back, foam is left in a band behind it. Depth is
			 * a poor stand-in because it depends on the slope — the same 0.5 m of depth is two
			 * metres from the edge on a steep beach and sixty on a flat one.
			 *
			 * A distance field also interpolates cleanly where depth does not: depth has a sharp
			 * zero crossing, so bilinear filtering of a coarse grid gives a blocky contour, while
			 * distance is smooth through the edge and stays smooth at any resolution.
			 */
			Distance(pixels, resolution, TexelMetres);
			int openResolution = Mathf.Min(resolution, 1024);
			OpenSea(pixels, resolution, TexelMetres, openResolution, ShelterGap * 0.5f, ShelterDepth, HighestTide,
				out float[] joinTide, out float[] shelter);

			var halves = new ushort[resolution * resolution * 2];
			for (int i = 0; i < pixels.Length; i++)
			{
				halves[i * 2] = Mathf.FloatToHalf(pixels[i].r);
				halves[i * 2 + 1] = Mathf.FloatToHalf(pixels[i].g);
			}

			/* A new texture every build, uploaded and made non-readable: the CPU copy is the snapshot.
			 * A texture that is no longer readable cannot be refilled, so the old one is thrown away —
			 * which is safe here because nothing builds from OnValidate any more (it defers to the
			 * editor's next tick) or from inside a render callback. */
			Texture2D previous = field;
			field = new Texture2D(resolution, resolution, TextureFormat.RGHalf, false, true)
			{
				name = "Shore depth",
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				hideFlags = HideFlags.HideAndDontSave,
			};
			field.SetPixelData(halves, 0);
			field.Apply(false, true);
			Discard(previous);
			snapshot = new Snapshot(halves, resolution, area, TexelMetres, ++version, joinTide, shelter, openResolution, HighestTide);

			// The tide each place's way in needs and how much its narrowest gap lets through (OpenSea), for the
			// shaders to hold against the tide now.
			Texture2D previousOpen = openSea;
			openSea = new Texture2D(openResolution, openResolution, TextureFormat.RGBAHalf, false, true)
			{
				name = "Open sea",
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				hideFlags = HideFlags.HideAndDontSave,
			};
			// r the tide the way in needs, gba how much its narrowest gap lets through at the mean tide, half
			// the highest and the highest (ShelterTides).
			var openHalves = new ushort[joinTide.Length * 4];
			for (int i = 0; i < joinTide.Length; i++)
			{
				openHalves[i * 4] = Mathf.FloatToHalf(Mathf.Clamp(joinTide[i], -60000f, 60000f));
				openHalves[i * 4 + 1] = Mathf.FloatToHalf(shelter[i * 3]);
				openHalves[i * 4 + 2] = Mathf.FloatToHalf(shelter[i * 3 + 1]);
				openHalves[i * 4 + 3] = Mathf.FloatToHalf(shelter[i * 3 + 2]);
			}
			openSea.SetPixelData(openHalves, 0);
			openSea.Apply(false, true);
			Discard(previousOpen);
			Shader.SetGlobalTexture(OpenSeaId, openSea);
			Shader.SetGlobalVector(OpenSeaRectId, new Vector4(area.xMin, area.yMin, area.width, area.height));
			Shader.SetGlobalFloat(OpenSeaTidesId, Mathf.Max(0f, HighestTide));

			Shader.SetGlobalTexture(FieldId, field);
			Shader.SetGlobalVector(RectId, new Vector4(area.xMin, area.yMin, area.width, area.height));
			Shader.SetGlobalFloat(RangeId, Mathf.Max(1f, deepest));
			Shader.SetGlobalFloat(TexelId, TexelMetres);
		}

		/// <summary>
		/// Fills the green channel with the signed distance, in metres, to the water's edge.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Exact, not a chamfer.</b> This was a two-pass chamfer transform — each texel taking the
		/// best of its neighbours plus one step or a diagonal — which is cheap and is not a distance:
		/// its contours are octagons, its gradient kinks along eight directions, and around an
		/// island it was off by two and a half metres either way, a tenth of a surf wavelength. The
		/// surf's crests are this field's contours and its throw is this field's gradient, so the
		/// octagon went straight onto the water. Felzenszwalb and Huttenlocher's transform is exact
		/// and still linear: a lower envelope of parabolas along each row, then along each column.
		/// </para>
		/// <para>
		/// The edge lies between a wet texel and a dry one, so each side is measured to the nearest
		/// texel of the other and half a texel is taken off: a wet texel beside a dry one is half a
		/// texel from the water's edge, not on it.
		/// </para>
		/// </remarks>
		private static void Distance(Color[] pixels, int resolution, float metresPerTexel)
		{
			int count = pixels.Length;
			var toDry = new float[count];
			var toWet = new float[count];
			for (int i = 0; i < count; i++)
			{
				bool wet = pixels[i].r > 0f;
				toDry[i] = wet ? Far : 0f;
				toWet[i] = wet ? 0f : Far;
			}
			SquaredDistance(toDry, resolution);
			SquaredDistance(toWet, resolution);

			// Negative on dry land, positive in the water, so one number says both which side of
			// the edge a point is on and how far.
			for (int i = 0; i < count; i++)
			{
				bool wet = pixels[i].r > 0f;
				/* Capped: a field with no dry texel at all, or no wet one, leaves the stand-in for
				 * infinity in place, and ten billion metres overflows the half-float texture to inf —
				 * and the surf's phase to NaN. Four widths of the field is further than any wave cares. */
				float texels = Mathf.Min(Mathf.Sqrt(wet ? toDry[i] : toWet[i]), resolution * 4f) - 0.5f;
				float metres = Mathf.Max(0f, texels) * metresPerTexel;
				pixels[i].g = wet ? metres : -metres;
			}
		}

		/// <summary>The tides, as shares of the highest, the gap flood is run at (<see cref="OpenSea"/>): mean, half, highest.</summary>
		public static readonly float[] ShelterTides = { 0f, 0.5f, 1f };

		/// <summary>
		/// How the open sea's waves reach each place, on a grid of <paramref name="openResolution"/> a side
		/// sampled from the field's: <paramref name="joinTide"/> the tide, in metres over mean sea level, at
		/// which there is a way in from the open sea deep enough for them (<paramref name="clearance"/> of
		/// water), and <paramref name="shelter"/> how much of them the narrowest gap on the way in lets
		/// through, 0..1 — none through a gap under <paramref name="halfGap"/>, all through one three times
		/// as wide — at each of <see cref="ShelterTides"/> of <paramref name="highestTide"/>, three to a cell.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why.</b> Everything the shore does — the fade of the sea's waves, the breakers, the bore, the
		/// swash — was worked out from the depth and the distance to the nearest shore alone, so a pool in the
		/// middle of an island or a lagoon behind a bar surfed like an open beach. Swell only reaches water
		/// joined to the open sea through a gap it can pass; a pool cut off from it lies still.
		/// </para>
		/// <para>
		/// <b>Floods out from the open sea</b> — the field's border, open water past the terrain — each
		/// taking the best way in to every place (a priority flood, as depressions are filled in terrain
		/// hydrology, settling a place only when it comes off the queue, since a step's cost depends on the
		/// way it is taken). The tide flood keeps the lowest water level the way in needs: its worst point,
		/// where the water must stand to be deep enough. The gap floods keep the widest the way in's narrowest
		/// point is — the distance to the nearest dry land, which along a channel is its half-width — at three
		/// tides, since a bar or a rim the tide covers is no wall; the shaders blend them by the tide.
		/// </para>
		/// <para>
		/// <b>The final approach is free.</b> A step heading in toward the shore — nearer dry land than the
		/// last by a clear margin — is not a gap and not a sill: it only has to be wet. Without that, the last
		/// metres of every open beach, shallow and near the shore, read as a narrow, shallow way in, and the
		/// swash of every beach would have been stilled.
		/// </para>
		/// <para>
		/// Checked (WaterOpenSeaTests) on a synthetic coast: the open sea and a beach to its waterline are open
		/// at any tide they are wet; a pool in an island is still until the tide tops its rim; a lagoon behind a
		/// six-metre channel is sheltered at any tide; one behind a bar dry at mean tide opens once the tide is
		/// over it; a sixteen-metre creek lets some of the swell in.
		/// </para>
		/// </remarks>
		public static void OpenSea(Color[] pixels, int resolution, float texelMetres, int openResolution,
			float halfGap, float clearance, float highestTide, out float[] joinTide, out float[] shelter)
		{
			int n = openResolution;
			float stride = resolution / (float)n;
			float cellMetres = texelMetres * stride;
			var depth = new float[n * n];
			var edge = new float[n * n];
			for (int y = 0; y < n; y++)
			{
				int fy = Mathf.Clamp((int)((y + 0.5f) * stride), 0, resolution - 1);
				for (int x = 0; x < n; x++)
				{
					int fx = Mathf.Clamp((int)((x + 0.5f) * stride), 0, resolution - 1);
					Color here = pixels[fy * resolution + fx];
					depth[y * n + x] = here.r;
					edge[y * n + x] = here.g;
				}
			}
			// Nearer dry land than the last by this much is heading in toward the shore.
			float margin = 0.3f * cellMetres;

			// The tide flood: the lowest level at which the way in is deep enough.
			joinTide = Flood(n, true, (from, cell) =>
			{
				float wet = -depth[cell];
				if (from < 0)
				{
					return clearance - depth[cell];
				}
				return edge[cell] < edge[from] - margin ? wet : Mathf.Max(wet, clearance - depth[cell]);
			});

			// The gap floods: the widest the way in's narrowest point is, at each tide.
			int tides = ShelterTides.Length;
			shelter = new float[n * n * tides];
			var shore = new float[n * n];
			for (int level = 0; level < tides; level++)
			{
				float tide = ShelterTides[level] * Mathf.Max(0f, highestTide);
				// How far each place is from dry land at this tide, m.
				for (int i = 0; i < shore.Length; i++)
				{
					shore[i] = depth[i] + tide > 0f ? Far : 0f;
				}
				SquaredDistance(shore, n);
				for (int i = 0; i < shore.Length; i++)
				{
					shore[i] = Mathf.Max(0f, Mathf.Min(Mathf.Sqrt(shore[i]), n * 4f) - 0.5f) * cellMetres;
				}
				float[] gap = Flood(n, false, (from, cell) =>
				{
					if (depth[cell] + tide <= 0f)
					{
						return 0f;
					}
					if (from < 0 || shore[cell] < shore[from] - margin)
					{
						return float.MaxValue;
					}
					return shore[cell];
				});
				for (int i = 0; i < gap.Length; i++)
				{
					float t = Mathf.Clamp01((gap[i] - 0.5f * halfGap) / Mathf.Max(0.01f, halfGap));
					shelter[i * tides + level] = t * t * (3f - 2f * t);
				}
			}
		}

		/// <summary>
		/// How much of the swell a place's narrowest gap lets through at a tide, from the gap floods at
		/// <see cref="ShelterTides"/> (the mean tide's, half the highest's and the highest's): the nearest two
		/// blended, and the mean tide's below it. FishWaterOpenSea, line for line.
		/// </summary>
		public static float ShelterAt(float mean, float half, float high, float tide, float highestTide)
		{
			float x = highestTide > 0.01f ? Mathf.Clamp01(tide / highestTide) * 2f : 0f;
			return x <= 1f ? Mathf.Lerp(mean, half, x) : Mathf.Lerp(half, high, x - 1f);
		}

		/// <summary>
		/// A priority flood from the grid's border: every cell takes the best way in, where a way's value is
		/// the worst of its steps — the highest (<paramref name="lowest"/>, taking the lowest such way) or the
		/// narrowest (taking the widest). <paramref name="step"/> says what entering a cell costs, from the
		/// cell it is entered from (−1 on the border). A cell is settled when it comes off the queue, not when
		/// it is first reached: the cost depends on the way a step is taken, so the first neighbour to reach a
		/// cell is not always the best one.
		/// </summary>
		private static float[] Flood(int n, bool lowest, System.Func<int, int, float> step)
		{
			float worst = lowest ? float.MaxValue : float.MinValue;
			var value = new float[n * n];
			for (int i = 0; i < value.Length; i++)
			{
				value[i] = worst;
			}
			var done = new bool[n * n];
			var keys = new List<float>(n * 8);
			var cells = new List<int>(n * 8);
			void Push(float v, int index)
			{
				keys.Add(lowest ? v : -v);
				cells.Add(index);
				int i = keys.Count - 1;
				while (i > 0)
				{
					int parent = (i - 1) >> 1;
					if (keys[parent] <= keys[i])
					{
						break;
					}
					(keys[parent], keys[i]) = (keys[i], keys[parent]);
					(cells[parent], cells[i]) = (cells[i], cells[parent]);
					i = parent;
				}
			}
			int Pop()
			{
				int top = cells[0];
				int lastIndex = keys.Count - 1;
				keys[0] = keys[lastIndex];
				cells[0] = cells[lastIndex];
				keys.RemoveAt(lastIndex);
				cells.RemoveAt(lastIndex);
				int i = 0;
				while (true)
				{
					int left = 2 * i + 1, right = left + 1, best = i;
					if (left < keys.Count && keys[left] < keys[best])
					{
						best = left;
					}
					if (right < keys.Count && keys[right] < keys[best])
					{
						best = right;
					}
					if (best == i)
					{
						break;
					}
					(keys[best], keys[i]) = (keys[i], keys[best]);
					(cells[best], cells[i]) = (cells[i], cells[best]);
					i = best;
				}
				return top;
			}
			bool Better(float a, float b) => lowest ? a < b : a > b;
			for (int i = 0; i < n; i++)
			{
				foreach (int cell in new[] { i, (n - 1) * n + i, i * n, i * n + n - 1 })
				{
					float v = step(-1, cell);
					if (Better(v, value[cell]))
					{
						value[cell] = v;
						Push(v, cell);
					}
				}
			}
			while (keys.Count > 0)
			{
				int index = Pop();
				if (done[index])
				{
					continue;
				}
				done[index] = true;
				float level = value[index];
				int cx = index % n, cy = index / n;
				for (int k = 0; k < 4; k++)
				{
					int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0);
					int ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
					if (nx < 0 || ny < 0 || nx >= n || ny >= n)
					{
						continue;
					}
					int neighbour = ny * n + nx;
					if (done[neighbour])
					{
						continue;
					}
					float cost = step(index, neighbour);
					// A way is as good as its worst step.
					float v = lowest ? Mathf.Max(level, cost) : Mathf.Min(level, cost);
					if (Better(v, value[neighbour]))
					{
						value[neighbour] = v;
						Push(v, neighbour);
					}
				}
			}
			return value;
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

		/// <summary>Stands in for infinity: finite, so the envelope's arithmetic never meets inf - inf.</summary>
		private const float Far = 1e20f;

		/// <summary>
		/// Squared Euclidean distance, in texels, from every texel to the nearest one holding 0 —
		/// in place, rows then columns (Felzenszwalb and Huttenlocher, 2012).
		/// </summary>
		private static void SquaredDistance(float[] grid, int resolution)
		{
			var line = new float[resolution];
			var result = new float[resolution];
			var hull = new int[resolution];
			var bounds = new float[resolution + 1];
			for (int y = 0; y < resolution; y++)
			{
				System.Array.Copy(grid, y * resolution, line, 0, resolution);
				Envelope(line, result, hull, bounds, resolution);
				System.Array.Copy(result, 0, grid, y * resolution, resolution);
			}
			for (int x = 0; x < resolution; x++)
			{
				for (int y = 0; y < resolution; y++)
				{
					line[y] = grid[y * resolution + x];
				}
				Envelope(line, result, hull, bounds, resolution);
				for (int y = 0; y < resolution; y++)
				{
					grid[y * resolution + x] = result[y];
				}
			}
		}

		/// <summary>
		/// One line of the transform: the lower envelope of the parabolas (q - p)² + f(q), then
		/// read off at every p.
		/// </summary>
		private static void Envelope(float[] f, float[] d, int[] hull, float[] bounds, int n)
		{
			int k = 0;
			hull[0] = 0;
			bounds[0] = float.NegativeInfinity;
			bounds[1] = float.PositiveInfinity;
			for (int q = 1; q < n; q++)
			{
				float s = Intersection(f, q, hull[k]);
				while (s <= bounds[k])
				{
					k--;
					s = Intersection(f, q, hull[k]);
				}
				k++;
				hull[k] = q;
				bounds[k] = s;
				bounds[k + 1] = float.PositiveInfinity;
			}
			k = 0;
			for (int q = 0; q < n; q++)
			{
				while (bounds[k + 1] < q)
				{
					k++;
				}
				float offset = q - hull[k];
				d[q] = offset * offset + f[hull[k]];
			}
		}

		/// <summary>Where the parabolas rooted at q and p cross.</summary>
		private static float Intersection(float[] f, int q, int p)
		{
			return ((f[q] + (float)q * q) - (f[p] + (float)p * p)) / (2f * (q - p));
		}

		/// <summary>The highest ground at a world XZ across every terrain in the scene.</summary>
		/// <remarks>
		/// The highest, not the first: stitched tiles overlap along their seams by a sample, and
		/// taking whichever was listed first put a one-sample trench down every join, which the
		/// surf then broke along.
		/// </remarks>
		private static float Ground(List<Terrain> terrains, float worldX, float worldZ)
		{
			float best = float.MinValue;
			for (int i = 0; i < terrains.Count; i++)
			{
				Terrain terrain = terrains[i];
				Vector3 origin = terrain.GetPosition();
				Vector3 size = terrain.terrainData.size;
				float u = (worldX - origin.x) / size.x;
				float v = (worldZ - origin.z) / size.z;
				if (u < 0f || u > 1f || v < 0f || v > 1f)
				{
					continue;
				}
				best = Mathf.Max(best, origin.y + terrain.terrainData.GetInterpolatedHeight(u, v));
			}
			// Off every terrain: open water, as deep as the scene gets.
			return best > float.MinValue ? best : float.MinValue;
		}
	}
}
