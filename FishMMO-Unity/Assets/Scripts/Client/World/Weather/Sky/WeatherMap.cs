using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// A top-down picture of the storms around the viewer, so the sky can draw a storm's own cloud
	/// wherever the storm is: its cumulonimbus, its flat dark base and what hangs below it, its anvil.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A storm cell (<see cref="StormCell"/>) is where a storm's weather is: the rain, the wind, what a
	/// player stands in. The cloud is the storm's anatomy (<see cref="StormAnatomy"/>), worked out from
	/// the same air, once per cell per build — a tower several kilometres across over the rain, a base
	/// of its own lower than the open air's, a wall cloud under a supercell's rotating updraught that
	/// its tornado hangs from, a shelf cloud leading a squall line, an anvil streaming downwind along
	/// the tropopause. Drawn from the cell alone a storm was its rain footprint at full cover: rain
	/// shafts hanging out of an ordinary sky, and a tornado hanging from nothing.
	/// </para>
	/// <para>
	/// <b>Two cascades.</b> A storm is seen from tens of kilometres off, and its anvil is tens of
	/// kilometres across; the sky draws cloud 44 km out. One fine map of 12 km would stop a storm's
	/// cloud five kilometres from the camera while its rain curtain went on being drawn. So there is a
	/// fine map of 12 km at 94 m a texel, rebuilt often, for the wall cloud and the shelf close to, and
	/// a coarse one of 112 km at 700 m a texel, rebuilt less often, for everything out to the horizon —
	/// wide enough that it has not begun to fade by 44 km.
	/// The shader reads the fine one inside a circle round its middle and hands over to the coarse one
	/// across a ring before its edge (<see cref="NearWeight"/>), and fades the coarse one out across a
	/// ring before its own edge (<see cref="FarWeight"/>) — circles, not the squares the maps are: a
	/// square's edge is a straight line, and a straight line drawn across the sky is seen at once.
	/// </para>
	/// <para>
	/// <b>The channels</b>, both textures half floats, both cascades alike:
	/// <list type="bullet">
	/// <item><b>A</b> r — the storm's own cloud, 0..1: its cumulonimbus, and the lowerings under and
	/// ahead of it (the wall cloud, the shelf). g — its anvil, 0..1 of the anvil's full depth: thick
	/// over the storm, thinning to nothing at its edge. b — clearing, 0..1: where sinking air (a
	/// hurricane's eye, <see cref="StormCell.ClearingAt"/>) dissolves whatever cloud the air round it
	/// makes. a — how much of a storm it is, 0..1: its severity where its cloud stands, which puts
	/// more water in that cloud.</item>
	/// <item><b>B</b>, heights in metres above the ground, the first three premultiplied so that the
	/// texture filter blends them honestly across a storm's edge. r — the storm's base × A.r: flat
	/// under the tower, lowered under the wall cloud and the shelf. g — how high its cloud reaches ×
	/// A.r: its tower top, domed over the updraught, lower over the shelf's wedge. b — the anvil's
	/// flat top × A.g. a — how thick the anvil is here (m), not premultiplied: its full depth where it
	/// is thickest, nothing at its edge.</item>
	/// </list>
	/// The shader's twin is <c>FishStormAt</c> in FishSkyCommon.hlsl; the two must agree.
	/// </para>
	/// <para>
	/// A cascade is rebuilt on its own timer, and at once whenever the cells change, the viewer has
	/// moved a good part of the way to its edge, or the storms have moved half a texel since it was
	/// built — which in the game is the timer and on a test bed running its clock at a hundred and
	/// eighty times real time is nearly every frame. Weather never appears on any map UI; this is only
	/// read by the sky's shaders and the rain curtains.
	/// </para>
	/// <para>
	/// <b>Rasterised on the GPU where it can be</b> (FishWeatherMap.compute, when
	/// <see cref="FishCloudsFeature.WeatherMapShader"/> is there): each storm is still worked out once
	/// per build here (<see cref="Prepare"/>), packed into <see cref="StormFloat4s"/> float4s
	/// (<see cref="PackStorm"/>), and the kernel lays every one down over every texel it reaches. On the
	/// CPU that was an evaluation per storm per texel plus a half-float conversion and upload of both
	/// textures — for one storm whose anvil covers the fine map, 16 384 evaluations every 0.35 s, and for
	/// a hurricane at its real size, whose outer rain bands reach every texel of both cascades, 41 984
	/// of them each time. The CPU raster stays: it is the fallback where the kernel cannot run, and
	/// <see cref="Contribute"/> is the reference — <see cref="RasterPacked"/> is the kernel's twin in C#,
	/// held to <see cref="SampleTexel"/> by the tests.
	/// </para>
	/// <para>
	/// <b>A storm is sent to a cascade by its footprint, never by its centre.</b> A tropical cyclone
	/// drawn at its real size has an eye tens of kilometres across and rain bands hundreds of kilometres
	/// out, and its centre is usually off both maps while its bands lie across them; nothing here clamps
	/// a radius either. <see cref="Overlaps"/> asks whether the storm's reach — the farthest anything
	/// of it goes (<see cref="StormCloud.Reach"/>) — touches the cascade's square at all.
	/// </para>
	/// </remarks>
	public sealed class WeatherMap
	{
		public const int DefaultResolution = 128;
		/// <summary>The fine cascade's width, m.</summary>
		public const float DefaultSizeMeters = 12000f;
		/// <summary>The coarse cascade's width, m: its fade starts past the 44 km the sky draws its cloud to.</summary>
		public const float FarSizeMeters = 112000f;
		/// <summary>The coarse cascade's texels a side: 700 m each.</summary>
		public const int FarResolution = 160;
		/// <summary>How often the coarse cascade is rebuilt when nothing else asks for it, s.</summary>
		public const float FarRefreshSeconds = 1.5f;

		/// <summary>How many float4s one storm is packed into for the kernel (FISH_STORM_FLOAT4S).</summary>
		public const int StormFloat4s = 8;

		/// <summary>The kernel's thread group, a side.</summary>
		private const int RasterGroup = 8;

		/// <summary>
		/// What a rebuild costs the main thread, by raster, for the profiler: the CPU raster's cost grows
		/// with every texel a storm reaches, the kernel's does not.
		/// </summary>
		private static readonly Unity.Profiling.ProfilerMarker CpuBuildMarker = new Unity.Profiling.ProfilerMarker("WeatherMap.Build (CPU raster)");
		private static readonly Unity.Profiling.ProfilerMarker GpuBuildMarker = new Unity.Profiling.ProfilerMarker("WeatherMap.Build (GPU raster)");

		private static readonly int StormsId = Shader.PropertyToID("_FishWeatherStorms");
		private static readonly int RasterId = Shader.PropertyToID("_FishWeatherRaster");
		private static readonly int RasterSizeId = Shader.PropertyToID("_FishWeatherRasterSize");
		private static readonly int OutAId = Shader.PropertyToID("_FishWeatherMapOutA");
		private static readonly int OutBId = Shader.PropertyToID("_FishWeatherMapOutB");

		/// <summary>
		/// How far the cloud of a storm with no anatomy of its own reaches compared with its rain — an
		/// eruption's ash cloud. A point this much nearer the centre stands in for the real one, so the
		/// same falloff covers a wider circle.
		/// </summary>
		public const float CloudShieldScale = 0.55f;

		/// <summary>
		/// Over how many metres a storm's cloud goes from nothing to solid above its base. Twin of
		/// <c>FISH_STORM_BASE_RISE</c> in FishCloudVolume.hlsl.
		/// </summary>
		/// <remarks>
		/// A heap's base is where each of its bubbles happened to reach its dew point, and its cloud
		/// fills in over a tenth of its depth. A storm feeds on one broad updraught of one air: its
		/// base is where that air condenses, one height across the whole storm, and the cloud is
		/// solid within a few tens of metres of it — the flat, hard, dark underside a storm is known
		/// by. The shader lowers its floor by most of this, so the base it shows is the base the
		/// anatomy gives and a tornado hung at <see cref="StormAnatomy.WallCloudBase"/> meets it.
		/// </remarks>
		public const float BaseRiseMetres = 60f;

		/// <summary>Where the fine cascade starts handing over to the coarse one, and where it has, as shares of its half-width. Twins in FishSkyCommon.hlsl.</summary>
		public const float NearHandoverStart = 0.6f, NearHandoverEnd = 0.9f;
		/// <summary>Where the coarse cascade starts fading out, and where it is gone, as shares of its half-width.</summary>
		public const float FarFadeStart = 0.8f, FarFadeEnd = 0.97f;

		public static readonly int TextureId = Shader.PropertyToID("_FishWeatherMap");
		public static readonly int TextureBId = Shader.PropertyToID("_FishWeatherMapB");
		public static readonly int FarTextureId = Shader.PropertyToID("_FishWeatherMapFar");
		public static readonly int FarTextureBId = Shader.PropertyToID("_FishWeatherMapFarB");
		public static readonly int RectId = Shader.PropertyToID("_FishWeatherMapRect");
		public static readonly int FarRectId = Shader.PropertyToID("_FishWeatherMapFarRect");
		public static readonly int ParamsId = Shader.PropertyToID("_FishWeatherMapParams");

		/// <summary>The storms at one place, unpacked: what the two textures carry, and the rain under them.</summary>
		public struct Texel
		{
			/// <summary>The storm's own cloud, 0..1: its tower, and the lowerings under and ahead of it.</summary>
			public float Cover;
			/// <summary>Its anvil, 0..1 of the anvil's full depth.</summary>
			public float Anvil;
			/// <summary>Where sinking air clears the sky, 0..1: a hurricane's eye.</summary>
			public float Clearing;
			/// <summary>How much of a storm it is, 0..1.</summary>
			public float Strength;
			/// <summary>The storm's cloud base above the ground, m. Meaningful where <see cref="Cover"/> is above 0.</summary>
			public float Base;
			/// <summary>How high its cloud reaches, m.</summary>
			public float Top;
			/// <summary>The anvil's flat top, m. Meaningful where <see cref="Anvil"/> is above 0.</summary>
			public float AnvilTop;
			/// <summary>How thick the anvil is here, m.</summary>
			public float AnvilDepth;
			/// <summary>What falls here, 0..1: the storms' rain as the curtains draw it. Not in the textures.</summary>
			public float Precipitation;

			/// <summary>The first texture's texel: cover, anvil, clearing, strength.</summary>
			public Color A => new Color(Cover, Anvil, Clearing, Strength);

			/// <summary>The second texture's texel, premultiplied as the shader reads it.</summary>
			public Color B => new Color(Base * Cover, Top * Cover, AnvilTop * Anvil, AnvilDepth);
		}

		/// <summary>Where the storms' cloud can be at all, for the shells the cloud march skips empty air by.</summary>
		public struct Extremes
		{
			/// <summary>True when any storm has cloud on either cascade.</summary>
			public bool Any;
			/// <summary>The lowest a storm's cloud floor goes anywhere, m: the lowest wall cloud or shelf, less the base's rise.</summary>
			public float LowestFloor;
			/// <summary>The highest a storm's tower reaches anywhere, m.</summary>
			public float HighestTop;
			/// <summary>True when any storm has an anvil.</summary>
			public bool Anvils;
			/// <summary>The lowest an anvil's underside goes, and the highest its top, m.</summary>
			public float AnvilBottom, AnvilTop;
			/// <summary>How thick the deepest anvil is at its thickest, m.</summary>
			public float AnvilDepth;
			/// <summary>What an anvil's ice takes out of the light, 1/m.</summary>
			public float AnvilExtinction;
		}

		/// <summary>One storm, worked out once per build: its anatomy put into the world, and its life.</summary>
		private struct StormCloud
		{
			/// <summary>The air it was laid out in: its own (StormCellAir), or the viewer's without a scene.</summary>
			public WeatherSample Air;
			public StormCell Cell;
			/// <summary>The cell with its motion frozen where it stands now, for what it answers per place.</summary>
			public StormCell Frozen;
			/// <summary>The world seconds it was laid out at.</summary>
			public double Seconds;
			public bool Anatomy;
			public StormAnatomy A;
			public Vector2 Centre, Body, AnvilCentre, Rain;
			public Vector2 Forward, Along;
			/// <summary>A front's half-length, m.</summary>
			public float HalfLength;
			/// <summary>How grown it is, 0..1: its envelope.</summary>
			public float Life;
			/// <summary>Its severity, precipitation and the cloud its air adds, at its heart and its strength now.</summary>
			public float Severity, Precipitation, AddedCover;
			/// <summary>For a storm with no anatomy: the base and top of the cloud its air makes, m.</summary>
			public float FallbackBase, FallbackTop;
			/// <summary>How far from its centre anything of it reaches, m.</summary>
			public float Reach;
			/// <summary>
			/// How far from its centre its own cloud and anvil reach, m: <see cref="Reach"/> without its
			/// rain. What decides whether it can put cloud on a map at all, for the kernel's raster, which
			/// cannot say afterwards which texels it filled.
			/// </summary>
			public float CloudReach;
		}

		/// <summary>What one storm puts at one place.</summary>
		private struct Contribution
		{
			public float Cover, Base, Top, Anvil, AnvilTop, AnvilDepth, Clearing, Strength, Precipitation;
		}

		/// <summary>Everything the storms put at one place, summed as the textures need it.</summary>
		private struct Accumulator
		{
			public float Cover, BaseSum, TopSum, Weight, Anvil, AnvilTopSum, AnvilWeight, AnvilDepth, Clearing, Strength, Precipitation;

			public void Add(in Contribution c)
			{
				/* Where storms overlap, the cloud is the more solid of them and its heights are theirs
				 * weighted by how much of each is there: a storm's base where it alone stands, between
				 * the two where both do. */
				Cover = Mathf.Max(Cover, c.Cover);
				if (c.Cover > 0f)
				{
					BaseSum += c.Cover * c.Base;
					TopSum += c.Cover * c.Top;
					Weight += c.Cover;
				}
				Anvil = Mathf.Max(Anvil, c.Anvil);
				if (c.Anvil > 0f)
				{
					AnvilTopSum += c.Anvil * c.AnvilTop;
					AnvilWeight += c.Anvil;
					AnvilDepth = Mathf.Max(AnvilDepth, c.AnvilDepth);
				}
				Clearing = Mathf.Max(Clearing, c.Clearing);
				Strength = Mathf.Max(Strength, c.Strength);
				Precipitation = 1f - (1f - Precipitation) * (1f - Mathf.Clamp01(c.Precipitation));
			}

			public Texel ToTexel()
			{
				return new Texel
				{
					Cover = Mathf.Clamp01(Cover),
					Anvil = Mathf.Clamp01(Anvil),
					Clearing = Mathf.Clamp01(Clearing),
					Strength = Mathf.Clamp01(Strength),
					Base = Weight > 0f ? BaseSum / Weight : 0f,
					Top = Weight > 0f ? TopSum / Weight : 0f,
					AnvilTop = AnvilWeight > 0f ? AnvilTopSum / AnvilWeight : 0f,
					AnvilDepth = AnvilDepth,
					Precipitation = Mathf.Clamp01(Precipitation),
				};
			}
		}

		/// <summary>One map: a square round the viewer, its two textures and when it was built.</summary>
		private sealed class Cascade
		{
			public readonly string Name;
			public readonly float Size;
			public readonly int Resolution;
			/// <summary>The textures the CPU raster fills.</summary>
			public Texture2D CpuA, CpuB;
			/// <summary>The targets the kernel writes.</summary>
			public RenderTexture GpuA, GpuB;
			/// <summary>The storms packed for the kernel.</summary>
			public ComputeBuffer Storms;
			/// <summary>Whether the textures in use are the kernel's.</summary>
			public bool Gpu;
			public Color[] PixelsA, PixelsB;
			public Accumulator[] Sums;
			public Vector2 Corner;
			public bool Valid;
			/// <summary>Whether any storm put anything on it.</summary>
			public bool Any;
			public float Timer;
			public uint BuiltTick;
			public Vector2 BuiltAt;
			public int BuiltSignature;
			public Extremes Extremes;

			public Cascade(string name, float size, int resolution)
			{
				Name = name;
				Size = size;
				Resolution = Mathf.Max(8, resolution);
			}

			public float TexelMeters => Size / Resolution;
			public Rect Area => new Rect(Corner, new Vector2(Size, Size));

			/// <summary>The textures in use: the kernel's or the CPU raster's.</summary>
			public Texture A => Gpu ? GpuA : CpuA;
			public Texture B => Gpu ? GpuB : CpuB;

			/// <summary>Makes the textures the chosen raster writes; a switch of raster starts the map again.</summary>
			public void Ensure(bool gpu)
			{
				if (gpu != Gpu)
				{
					Valid = false;
					Any = false;
				}
				Gpu = gpu;
				if (gpu)
				{
					if (GpuA == null || !GpuA.IsCreated() || GpuB == null || !GpuB.IsCreated())
					{
						DestroyTarget(ref GpuA);
						DestroyTarget(ref GpuB);
						GpuA = MakeTarget(Name);
						GpuB = MakeTarget(Name + " B");
						Valid = false;
					}
					return;
				}
				if (CpuA != null)
				{
					return;
				}
				CpuA = Make(Name);
				CpuB = Make(Name + " B");
				PixelsA = new Color[Resolution * Resolution];
				PixelsB = new Color[Resolution * Resolution];
				Sums = new Accumulator[Resolution * Resolution];
			}

			private Texture2D Make(string name)
			{
				return new Texture2D(Resolution, Resolution, TextureFormat.RGBAHalf, false, true)
				{
					name = name,
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.DontSave,
				};
			}

			private RenderTexture MakeTarget(string name)
			{
				var target = new RenderTexture(Resolution, Resolution, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat)
				{
					name = name + " (GPU)",
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					enableRandomWrite = true,
					useMipMap = false,
					hideFlags = HideFlags.DontSave,
				};
				target.Create();
				return target;
			}

			/// <summary>Room for this many storms; a buffer is kept for none, since the kernel must be handed one.</summary>
			public ComputeBuffer EnsureStorms(int storms)
			{
				int wanted = Mathf.Max(1, storms) * StormFloat4s;
				if (Storms == null || Storms.count < wanted)
				{
					Storms?.Release();
					// Grown to the next power of two, so a director adding one storm at a time does not
					// reallocate for every one.
					Storms = new ComputeBuffer(Mathf.NextPowerOfTwo(wanted), 16);
				}
				return Storms;
			}

			public void Dispose()
			{
				Destroy(ref CpuA);
				Destroy(ref CpuB);
				DestroyTarget(ref GpuA);
				DestroyTarget(ref GpuB);
				Storms?.Release();
				Storms = null;
				Valid = false;
			}

			private static void Destroy(ref Texture2D texture)
			{
				if (texture == null)
				{
					return;
				}
				if (Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture);
				texture = null;
			}

			private static void DestroyTarget(ref RenderTexture texture)
			{
				if (texture == null)
				{
					return;
				}
				texture.Release();
				if (Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture);
				texture = null;
			}
		}

		private readonly Cascade near = new Cascade("Weather Map", DefaultSizeMeters, DefaultResolution);
		private readonly Cascade far = new Cascade("Weather Map (far)", FarSizeMeters, FarResolution);
		private readonly StormFrames storms = new StormFrames();
		private readonly List<StormCloud> clouds = new List<StormCloud>();
		private readonly List<Vector4> packed = new List<Vector4>();
		private CommandBuffer rasterCommands;

		/// <summary>The fine cascade's texture A.</summary>
		public Texture Texture => near.A;

		/// <summary>Whether the fine cascade was last laid down by the kernel, for the probes to report.</summary>
		public bool RasterisedOnGpu => near.Valid && near.Gpu;

		/// <summary>The fine cascade's square, world x/z.</summary>
		public Rect Area => near.Area;

		/// <summary>The coarse cascade's square, world x/z.</summary>
		public Rect FarArea => far.Area;

		/// <summary>Where the storms' cloud can be on the maps as they now stand.</summary>
		public Extremes StormExtremes
		{
			get
			{
				Extremes a = near.Valid ? near.Extremes : default;
				Extremes b = far.Valid ? far.Extremes : default;
				if (!a.Any)
				{
					return b;
				}
				if (!b.Any)
				{
					return a;
				}
				return new Extremes
				{
					Any = true,
					LowestFloor = Mathf.Min(a.LowestFloor, b.LowestFloor),
					HighestTop = Mathf.Max(a.HighestTop, b.HighestTop),
					Anvils = a.Anvils || b.Anvils,
					AnvilBottom = Mathf.Min(a.Anvils ? a.AnvilBottom : float.MaxValue, b.Anvils ? b.AnvilBottom : float.MaxValue),
					AnvilTop = Mathf.Max(a.AnvilTop, b.AnvilTop),
					AnvilDepth = Mathf.Max(a.AnvilDepth, b.AnvilDepth),
					AnvilExtinction = Mathf.Max(a.AnvilExtinction, b.AnvilExtinction),
				};
			}
		}

		// ── Where each cascade is read ───────────────────────────────────────

		/// <summary>1 with a smooth edge: the GLSL/HLSL smoothstep, which Mathf.SmoothStep is not.</summary>
		private static float Smooth(float edge0, float edge1, float x)
		{
			float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
			return t * t * (3f - 2f * t);
		}

		/// <summary>
		/// How much of the fine cascade the sky reads at a place, 0..1: all of it round its middle,
		/// handing over to the coarse one across a ring before its edge.
		/// </summary>
		public static float NearWeight(Vector2 xz, Rect area)
		{
			float r = Vector2.Distance(xz, area.center) / Mathf.Max(1f, 0.5f * area.width);
			return 1f - Smooth(NearHandoverStart, NearHandoverEnd, r);
		}

		/// <summary>How much of the coarse cascade is drawn at a place, 0..1: faded out across a ring before its edge.</summary>
		public static float FarWeight(Vector2 xz, Rect area)
		{
			float r = Vector2.Distance(xz, area.center) / Mathf.Max(1f, 0.5f * area.width);
			return 1f - Smooth(FarFadeStart, FarFadeEnd, r);
		}

		/// <summary>
		/// How much of a storm's cloud the sky draws at a place, 0..1: 1 wherever either cascade
		/// reaches, fading to nothing past the coarse one. What falls from that cloud must fade with it.
		/// </summary>
		public float CloudReach(Vector2 xz)
		{
			float w = far.Valid ? FarWeight(xz, far.Area) : 0f;
			if (near.Valid)
			{
				w = Mathf.Lerp(w, 1f, NearWeight(xz, near.Area));
			}
			return w;
		}

		// ── What a storm puts where ─────────────────────────────────────────

		/// <summary>The storms at one map point: cover, anvil, clearing, strength — the first texture's texel.</summary>
		/// <remarks>
		/// Only the storm cells: the air's own cloud, everywhere else, the sky draws from the air itself.
		/// Each cell brings what its kind of storm makes in the air around the viewer, laid out by its
		/// anatomy. Works everything out afresh for the one point, so it is for tests and probes; the
		/// maps work each storm out once per build.
		/// </remarks>
		public static Color Sample(WeatherTimeline timeline, StormFrames storms, Vector3 position, uint tick)
		{
			return SampleTexel(timeline, storms, position, tick).A;
		}

		/// <summary>The storms at one map point, unpacked, with the rain under them.</summary>
		public static Texel SampleTexel(WeatherTimeline timeline, StormFrames storms, Vector3 position, uint tick)
		{
			var sum = new Accumulator();
			if (timeline != null && timeline.SceneMode == WeatherSceneMode.Own && storms != null)
			{
				WeatherSample around = storms.Around;
				var at = new Vector2(position.x, position.z);
				for (int i = 0; i < timeline.Cells.Count; i++)
				{
					if (Prepare(timeline, timeline.Cells[i], tick, timeline.TickDelta, timeline.LatitudeDegrees, storms, around, out StormCloud cloud)
						&& Contribute(cloud, at, 0f, out Contribution c))
					{
						sum.Add(c);
					}
				}
			}
			return sum.ToTexel();
		}

		/// <summary>
		/// The scene each storm's own air is read in (<see cref="StormCellAir"/>), set by the sky every frame
		/// it draws. Unset (tests, probes), a storm is laid out in the viewer's air, as it always was.
		/// </summary>
		/// <remarks>
		/// From the viewer's air, a storm's base, wall cloud and anvil were where that player's own air put
		/// them: two players saw the same storm built differently, and it changed as one walked. Laid out
		/// in its own air, it is the same storm for everyone, and the tornado (VortexPresenter), its rain
		/// (CurtainPresenter) and its cloud here are worked out in the same air, so they meet.
		/// </remarks>
		public static void SetCellAirScene(FishMMO.Shared.WorldSceneSettings settings, UnityEngine.SceneManagement.Scene scene)
		{
			cellAirSettings = settings;
			cellAirScene = scene;
		}

		private static FishMMO.Shared.WorldSceneSettings cellAirSettings;
		private static UnityEngine.SceneManagement.Scene cellAirScene;

		/// <summary>A storm's anatomy put into the world where the storm stands now, or false when it has none.</summary>
		private static bool Prepare(WeatherTimeline timeline, in StormCell cell, uint tick, double tickDelta, float latitude, StormFrames storms, in WeatherSample viewerAir, out StormCloud cloud)
		{
			cloud = default;
			StormCellAir.Air own = cellAirSettings != null ? StormCellAir.Of(timeline, cellAirSettings, cellAirScene, cell) : null;
			WeatherSample around = own != null ? own.Sample : viewerAir;
			cloud.Air = around;
			double now = timeline.WorldSecondsAt(tick);
			float envelope = cell.EnvelopeAtSeconds(now);
			if (envelope <= 0f || cell.RadiusMeters <= 0f || !around.Planet.HasAir || around.Planet.Gravity <= 0f)
			{
				return false;
			}
			float strength = envelope * Mathf.Clamp01(cell.PeakIntensity);
			WeatherFrame frame = own != null ? own.Frames.Of(cell.Kind) : storms.Of(cell.Kind);
			cloud.Cell = cell;
			cloud.Seconds = now;
			cloud.Centre = cell.CentreAtSeconds(now);
			cloud.Life = envelope;
			cloud.Severity = frame.StormSeverity * strength;
			cloud.Precipitation = frame[WeatherChannel.Precipitation] * strength;
			// Frozen where it is now: what a cell answers per place asks where its centre is, and that
			// is sines and a double's worth of motion — once here, not for every texel.
			StormCell frozen = cell;
			frozen.OriginX = cloud.Centre.x;
			frozen.OriginZ = cloud.Centre.y;
			frozen.MeanderMeters = 0f;
			frozen.MotionSeconds = now;
			// Its velocity is kept: a front's line is square to it. It moves nothing at its own tick.
			cloud.Frozen = frozen;
			cloud.Forward = cell.Facing;
			cloud.Along = new Vector2(-cloud.Forward.y, cloud.Forward.x);
			cloud.HalfLength = Mathf.Max(cell.RadiusMeters, cell.ExtentMeters);

			var motion = new Vector2(cell.VelocityX, cell.VelocityZ);
			cloud.A = StormAnatomy.Of(cell.Kind, around, motion, latitude, cell.RadiusMeters);
			cloud.Anatomy = cloud.A.Valid;
			if (cloud.Anatomy)
			{
				StormAnatomy a = cloud.A;
				// A squall line's line is square to its travel, like its rain's; its anatomy's forward
				// is the same way whenever it moves.
				if (cell.Shape != StormCellShape.Front)
				{
					cloud.Forward = a.Forward;
					cloud.Along = new Vector2(-a.Forward.y, a.Forward.x);
				}
				cloud.Body = cloud.Centre + a.BodyOffset;
				cloud.AnvilCentre = cloud.Body + a.AnvilOffset;
				cloud.Rain = cloud.Body + a.RainOffset;
				float reach = a.BodyOffset.magnitude + a.CoreRadius;
				reach = Mathf.Max(reach, a.WallCloudRadius);
				if (a.AnvilRadius > 0f)
				{
					reach = Mathf.Max(reach, (a.BodyOffset + a.AnvilOffset).magnitude + a.AnvilRadius
						+ (cell.Shape == StormCellShape.Front ? cloud.HalfLength : 0f));
				}
				if (cell.Shape == StormCellShape.Front)
				{
					// Its cloud runs two tower-widths back from the rain's leading edge, and its shelf
					// a shelf's width ahead of it.
					float across = 2f * a.CoreRadius + 0.45f * cell.RadiusMeters + a.ShelfWidth;
					reach = Mathf.Max(reach, Mathf.Sqrt(cloud.HalfLength * cloud.HalfLength + across * across));
				}
				// Its cloud: the tower, the lowerings, the anvil. Its rain reaches further than its cloud
				// on a supercell's forward flank, and the cell's own footprint may too.
				cloud.CloudReach = reach;
				reach = Mathf.Max(reach, (a.BodyOffset + a.RainOffset).magnitude + a.RainRadius);
				cloud.Reach = Mathf.Max(reach, cell.ReachMeters);
			}
			else
			{
				/* No cloud of its own — a haboob's is its parent storm's, a dust devil makes none, an
				 * eruption's is its ash. What such a storm's air adds to the cloud the open air already
				 * has is drawn as a shield over it, as every storm used to be, at the base and top that
				 * air makes: no more than the storm itself does to its air. */
				WeatherDriver.Synoptic air = StormPhysics.Perturb(around.OpenAir, cell.Kind, 1f);
				AirColumn column = AirColumn.Of(around.Planet, around.OpenColumn.SurfaceKelvin, air.Humidity, air.Pressure, air.Instability);
				cloud.AddedCover = Mathf.Max(0f, frame[WeatherChannel.CloudCover] - around.Background[WeatherChannel.CloudCover]);
				cloud.FallbackBase = Mathf.Max(150f, Mathf.Min(around.OpenColumn.Base, column.Base));
				cloud.FallbackTop = Mathf.Max(cloud.FallbackBase + 300f, column.Deep ? column.TowerCeiling : column.Top);
				cloud.Reach = cell.ReachMeters / CloudShieldScale;
				cloud.CloudReach = cloud.Reach;
			}
			return true;
		}

		/// <summary>
		/// What one storm puts at one place. <paramref name="texel"/> is the map's own texel, so that a
		/// lowering narrower than the map can resolve is still drawn to its full depth; 0 for a point.
		/// </summary>
		private static bool Contribute(in StormCloud s, Vector2 p, float texel, out Contribution c)
		{
			c = default;
			Vector2 offset = p - s.Centre;
			if (offset.sqrMagnitude > s.Reach * s.Reach)
			{
				return false;
			}
			StormCell cell = s.Cell;
			if (!s.Anatomy)
			{
				float shield = cell.Coverage(offset * CloudShieldScale) * s.Life * Mathf.Clamp01(cell.PeakIntensity);
				c.Cover = s.AddedCover * shield;
				c.Base = s.FallbackBase;
				c.Top = s.FallbackTop;
				c.Precipitation = s.Precipitation * cell.Coverage(offset);
				return c.Cover > 0f || c.Precipitation > 0f;
			}

			StormAnatomy a = s.A;
			/* A storm's cloud stands as soon as it has begun, climbs as it matures — a towering cumulus
			 * before it is a cumulonimbus — and only once it has reached the top of the weather does
			 * its anvil spread along it. Decaying, the other way round. */
			float stands = Smooth(0f, 0.35f, s.Life);
			float climbed = Mathf.Lerp(0.35f, 1f, Smooth(0f, 0.7f, s.Life));
			float spread = Smooth(0.4f, 0.9f, s.Life);
			float depth = Mathf.Max(100f, a.TopMetres - a.BaseMetres);
			float cloud;
			float baseHere = a.BaseMetres;
			float topHere;
			if (cell.Shape == StormCellShape.Front)
			{
				/* A squall line: its towers stand along the gust front, over the leading edge of its
				 * rain, and behind them the cloud runs on lower — the trailing stratiform — over the
				 * whole depth of the band; soft at its ends as its rain is. Ahead of it, the shelf. */
				float u = Vector2.Dot(offset, s.Forward);
				float v = Mathf.Abs(Vector2.Dot(offset, s.Along));
				float lengthwise = 1f - Smooth(0.75f * s.HalfLength, s.HalfLength, v);
				float lead = 0.45f * cell.RadiusMeters;
				float width = Mathf.Max(1f, a.ShelfWidth);
				float back = lead - 2f * a.CoreRadius;
				cloud = Smooth(back, back + a.CoreRadius, u) * (1f - Smooth(lead, lead + 0.3f * width, u)) * lengthwise;
				topHere = a.BaseMetres + depth * climbed * Mathf.Lerp(0.6f, 1f, Smooth(back, lead - 0.3f * a.CoreRadius, u));

				/* The shelf: the warm air the cold outflow undercuts, lifted over its head until it
				 * condenses below the storm's own base. Its underside is level, ShelfDrop down; its
				 * upper face slopes from the storm's base down to a thin nose ShelfWidth ahead of the
				 * rain — the wedge. */
				float x = (u - lead) / width;
				float shelf = Smooth(-0.15f, 0.05f, x) * (1f - Smooth(0.85f, 1f, x)) * lengthwise;
				if (shelf > 0f)
				{
					float shelfFloor = a.BaseMetres - a.ShelfDrop;
					float shelfTop = shelfFloor + Mathf.Max(80f, (a.BaseMetres + 0.5f * a.ShelfDrop - shelfFloor) * (1f - Mathf.Clamp01(x)));
					float total = cloud + shelf;
					baseHere = (cloud * a.BaseMetres + shelf * shelfFloor) / total;
					topHere = (cloud * topHere + shelf * shelfTop) / total;
					cloud = Mathf.Max(cloud, shelf);
				}
			}
			else
			{
				/* A tower over the storm's body, solid through most of it and ragged at its edge, domed
				 * over its updraught — a supercell's is the mesocyclone on its rear flank, over the
				 * tornado — and lower on its shoulders, where the anvil overhangs it. */
				float d = Vector2.Distance(p, s.Body) / Mathf.Max(1f, a.CoreRadius);
				cloud = 1f - Smooth(0.6f, 1f, d);
				Vector2 updraught = cell.Kind == StormKind.Supercell ? s.Centre : s.Body;
				float du = Mathf.Min(1f, Vector2.Distance(p, updraught) / Mathf.Max(1f, a.CoreRadius));
				topHere = a.BaseMetres + depth * climbed * (1f - 0.35f * du * du);
				if (a.WallCloudRadius > 0f)
				{
					/* The wall cloud, centred on the cell: flat through its middle, where a tornado
					 * hangs from it at exactly its base, rising to the storm's base at its rim. Its
					 * flat middle is kept at least as wide as the map's texel or the texture filter
					 * would round its depth off. */
					float inner = Mathf.Min(0.9f, Mathf.Max(0.45f, 0.75f * texel / a.WallCloudRadius));
					float wall = 1f - Smooth(inner, 1f, offset.magnitude / a.WallCloudRadius);
					baseHere = a.BaseMetres - a.WallCloudDrop * wall;
					cloud = Mathf.Max(cloud, wall);
				}
			}
			c.Cover = cloud * stands;
			c.Base = Mathf.Max(0f, baseHere);
			c.Top = Mathf.Max(c.Base + 100f, topHere);
			c.Strength = s.Severity * c.Cover;

			if (a.AnvilRadius > 0f && spread > 0f)
			{
				/* The anvil: thickest over the storm, thinning to nothing at its edge, flat along the
				 * tropopause. A squall line's runs the length of its line. */
				float da;
				if (cell.Shape == StormCellShape.Front)
				{
					Vector2 o = p - s.AnvilCentre;
					float ua = Vector2.Dot(o, s.Forward);
					float va = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(o, s.Along)) - s.HalfLength);
					da = Mathf.Sqrt(ua * ua + va * va) / a.AnvilRadius;
				}
				else
				{
					da = Vector2.Distance(p, s.AnvilCentre) / a.AnvilRadius;
				}
				float thin = (1f - Smooth(0.3f, 1f, da)) * spread;
				if (thin > 0f)
				{
					c.Anvil = thin;
					c.AnvilTop = a.AnvilTop;
					c.AnvilDepth = (a.AnvilTop - a.AnvilBase) * thin;
				}
			}

			if (cell.Shape == StormCellShape.Eyewall)
			{
				c.Clearing = s.Frozen.ClearingAtSeconds(new Vector3(p.x, 0f, p.y), s.Seconds);
			}

			// The rain, as the curtains draw it: the cell's own footprint, and a supercell's forward flank.
			float rain = s.Precipitation * cell.Coverage(offset);
			if (cell.Kind == StormKind.Supercell && a.RainRadius > 0f)
			{
				float flank = 1f - Smooth(0.55f, 1f, Vector2.Distance(p, s.Rain) / a.RainRadius);
				rain = 1f - (1f - rain) * (1f - s.Precipitation * flank);
			}
			c.Precipitation = rain;
			return c.Cover > 0f || c.Anvil > 0f || c.Clearing > 0f || c.Precipitation > 0f;
		}

		/// <summary>
		/// What an anvil's ice takes out of the light, 1/m.
		/// </summary>
		/// <remarks>
		/// An anvil is the air a storm's updraught carried to the tropopause, saturated when it arrived
		/// and spreading out; the ice it holds is about what that air could hold as vapour there — a
		/// tenth of a gram a cubic metre at −40 °C, a hundredth at −55, as anvils are measured — in
		/// crystals a thousandth as many as a cloud's drops (as <see cref="AirColumn.LayerExtinction"/>
		/// has them), so a few kilometres of anvil is nearly opaque and its thin edge is a veil.
		/// </remarks>
		private static float AnvilExtinction(in WeatherSample around, in StormAnatomy a)
		{
			PlanetAir planet = around.Planet;
			float middle = 0.5f * (a.AnvilBase + a.AnvilTop);
			float kelvin = Mathf.Max(20f, around.OpenColumn.KelvinAt(middle));
			float vapour = AirPhysics.SaturationPressure(kelvin, planet.Condensate) / (AirPhysics.VapourGasConstant(planet.Condensate) * kelvin);
			return AirPhysics.DropletExtinction(vapour, planet.DropletsPerCubicMetre * 0.001f, 917f);
		}

		// ── Building ────────────────────────────────────────────────────────

		/// <summary>A number that changes whenever a cell is born, edited or dies.</summary>
		private static int Signature(WeatherTimeline timeline)
		{
			if (timeline == null)
			{
				return 0;
			}
			unchecked
			{
				int h = 17 + timeline.Cells.Count;
				for (int i = 0; i < timeline.Cells.Count; i++)
				{
					StormCell cell = timeline.Cells[i];
					h = h * 31 + cell.ID;
					h = h * 31 + (int)cell.Kind;
					h = h * 31 + cell.BirthSeconds.GetHashCode();
					h = h * 31 + cell.DeathSeconds.GetHashCode();
					h = h * 31 + cell.MotionSeconds.GetHashCode();
					h = h * 31 + cell.OriginX.GetHashCode();
					h = h * 31 + cell.OriginZ.GetHashCode();
					h = h * 31 + cell.VelocityX.GetHashCode();
					h = h * 31 + cell.VelocityZ.GetHashCode();
					h = h * 31 + cell.RadiusMeters.GetHashCode();
				}
				return h;
			}
		}

		/// <summary>Whether a cascade needs building now.</summary>
		private static bool Due(Cascade cascade, WeatherTimeline timeline, Vector3 viewer, uint tick, int signature)
		{
			if (!cascade.Valid || cascade.Timer <= 0f || cascade.BuiltSignature != signature)
			{
				return true;
			}
			// The viewer has gone a good part of the way to its edge.
			var at = new Vector2(viewer.x, viewer.z);
			if (Vector2.Distance(at, cascade.BuiltAt) > 0.1f * cascade.Size)
			{
				return true;
			}
			// The storms have moved half a texel since it was drawn.
			if (timeline != null && timeline.Cells.Count > 0)
			{
				double seconds = System.Math.Abs(((long)tick - cascade.BuiltTick) * timeline.TickDelta);
				float fastest = 0f;
				for (int i = 0; i < timeline.Cells.Count; i++)
				{
					StormCell cell = timeline.Cells[i];
					fastest = Mathf.Max(fastest, new Vector2(cell.VelocityX, cell.VelocityZ).magnitude + cell.MeanderMeters * 0.0021f);
				}
				if (fastest * seconds > 0.5 * cascade.TexelMeters)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Brings both cascades up to date where they need it, and publishes them. Call every frame.
		/// </summary>
		/// <param name="around">The weather at the viewer: what each storm's kind makes in this air.</param>
		/// <param name="nearSeconds">How often the fine cascade is rebuilt when nothing else asks for it.</param>
		public void Update(WeatherTimeline timeline, Vector3 viewer, uint tick, in WeatherSample around, float deltaTime, float nearSeconds)
		{
			int signature = Signature(timeline);
			near.Timer -= deltaTime;
			far.Timer -= deltaTime;
			bool nearDue = Due(near, timeline, viewer, tick, signature);
			bool farDue = Due(far, timeline, viewer, tick, signature);
			if (!nearDue && !farDue)
			{
				return;
			}
			PrepareAll(timeline, tick, around);
			if (farDue)
			{
				Build(far, viewer, tick, signature, around);
				far.Timer = FarRefreshSeconds;
			}
			if (nearDue)
			{
				Build(near, viewer, tick, signature, around);
				near.Timer = nearSeconds;
			}
			Publish(true);
		}

		/// <summary>Redraws both cascades centred near a position now, and publishes them.</summary>
		public void Build(WeatherTimeline timeline, Vector3 centre, uint tick, in WeatherSample around)
		{
			Invalidate();
			Update(timeline, centre, tick, around, 0f, 0.35f);
		}

		/// <summary>Draws both cascades afresh on the next update: for storms that have just been set down or moved all at once.</summary>
		public void Invalidate()
		{
			near.Valid = false;
			far.Valid = false;
		}

		private void PrepareAll(WeatherTimeline timeline, uint tick, in WeatherSample around)
		{
			clouds.Clear();
			if (timeline == null || timeline.SceneMode != WeatherSceneMode.Own || timeline.Cells.Count == 0)
			{
				return;
			}
			storms.Reset(around);
			for (int i = 0; i < timeline.Cells.Count; i++)
			{
				if (Prepare(timeline, timeline.Cells[i], tick, timeline.TickDelta, timeline.LatitudeDegrees, storms, around, out StormCloud cloud))
				{
					clouds.Add(cloud);
				}
			}
		}

		private void Build(Cascade cascade, Vector3 viewer, uint tick, int signature, in WeatherSample around)
		{
			// The kernel where it runs (FishCloudsFeature decides where that is), the CPU elsewhere.
			ComputeShader kernel = FishCloudsFeature.WeatherMapShader;
			bool gpu = kernel != null && kernel.HasKernel("Raster");
			cascade.Ensure(gpu);
			float texel = cascade.TexelMeters;
			// Snapped, so the map does not crawl as the camera moves.
			cascade.Corner = new Vector2(
				Mathf.Round((viewer.x - cascade.Size * 0.5f) / texel) * texel,
				Mathf.Round((viewer.z - cascade.Size * 0.5f) / texel) * texel);
			cascade.BuiltAt = new Vector2(viewer.x, viewer.z);
			cascade.BuiltTick = tick;
			cascade.BuiltSignature = signature;
			if (clouds.Count == 0 && cascade.Valid && !cascade.Any)
			{
				// Clear, and it was clear: nothing to draw again, and the shader reads neither map.
				return;
			}
			if (gpu)
			{
				using (GpuBuildMarker.Auto())
				{
					BuildOnGpu(cascade, kernel, around);
				}
				return;
			}
			using (CpuBuildMarker.Auto())
			{
				BuildOnCpu(cascade, around);
			}
		}

		/// <summary>Lays a cascade down on the main thread: the reference, and the fallback where the kernel cannot run.</summary>
		private void BuildOnCpu(Cascade cascade, in WeatherSample around)
		{
			int resolution = cascade.Resolution;
			float texel = cascade.TexelMeters;
			System.Array.Clear(cascade.Sums, 0, cascade.Sums.Length);

			var extremes = new Extremes
			{
				LowestFloor = float.MaxValue,
				AnvilBottom = float.MaxValue,
			};
			/* Each storm is laid down only over the texels it can reach: most of a map is clear of
			 * any one storm, and a cell's centre is not worked out again for every texel. */
			for (int n = 0; n < clouds.Count; n++)
			{
				StormCloud cloud = clouds[n];
				int x0 = Mathf.Max(0, Mathf.FloorToInt((cloud.Centre.x - cloud.Reach - cascade.Corner.x) / texel));
				int x1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((cloud.Centre.x + cloud.Reach - cascade.Corner.x) / texel));
				int y0 = Mathf.Max(0, Mathf.FloorToInt((cloud.Centre.y - cloud.Reach - cascade.Corner.y) / texel));
				int y1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((cloud.Centre.y + cloud.Reach - cascade.Corner.y) / texel));
				bool touched = false;
				for (int y = y0; y <= y1; y++)
				{
					for (int x = x0; x <= x1; x++)
					{
						var p = new Vector2(cascade.Corner.x + (x + 0.5f) * texel, cascade.Corner.y + (y + 0.5f) * texel);
						if (Contribute(cloud, p, texel, out Contribution c))
						{
							cascade.Sums[y * resolution + x].Add(c);
							touched |= c.Cover > 0f || c.Anvil > 0f;
						}
					}
				}
				if (touched)
				{
					AddExtremes(ref extremes, cloud, around);
				}
			}
			FinishExtremes(ref extremes);
			cascade.Extremes = extremes;
			cascade.Any = extremes.Any;

			for (int i = 0; i < cascade.Sums.Length; i++)
			{
				Texel t = cascade.Sums[i].ToTexel();
				cascade.PixelsA[i] = t.A;
				cascade.PixelsB[i] = t.B;
			}
			cascade.CpuA.SetPixels(cascade.PixelsA);
			cascade.CpuA.Apply(false, false);
			cascade.CpuB.SetPixels(cascade.PixelsB);
			cascade.CpuB.Apply(false, false);
			cascade.Valid = true;
		}

		/// <summary>
		/// Lays a cascade down with the kernel: the storms that reach it packed and sent, one thread a
		/// texel. Nothing comes back, so where the storms' cloud can be (the shells the march skips empty
		/// air by) is worked out from what each storm could put there — its whole footprint rather than
		/// the texels it actually filled. That can only widen a shell, which costs the march a little
		/// empty air and never draws anything differently.
		/// </summary>
		private void BuildOnGpu(Cascade cascade, ComputeShader shader, in WeatherSample around)
		{
			packed.Clear();
			var extremes = new Extremes
			{
				LowestFloor = float.MaxValue,
				AnvilBottom = float.MaxValue,
			};
			Rect area = cascade.Area;
			int count = 0;
			for (int n = 0; n < clouds.Count; n++)
			{
				StormCloud cloud = clouds[n];
				if (!Overlaps(cloud.Centre, cloud.Reach, area))
				{
					continue;
				}
				PackStorm(cloud, packed);
				count++;
				if (!cloud.Anatomy && (cloud.AddedCover <= 0f || cloud.Cell.PeakIntensity <= 0f))
				{
					// Its air adds no cloud to the open air's: it puts nothing on the map.
					continue;
				}
				if (Overlaps(cloud.Centre, cloud.CloudReach, area))
				{
					// Its cloud or its anvil can land here, not only its rain.
					AddExtremes(ref extremes, cloud, around);
				}
			}
			FinishExtremes(ref extremes);
			cascade.Extremes = extremes;
			cascade.Any = extremes.Any;

			ComputeBuffer buffer = cascade.EnsureStorms(count);
			int kernel = shader.FindKernel("Raster");
			rasterCommands ??= new CommandBuffer { name = "Weather map (raster)" };
			rasterCommands.Clear();
			rasterCommands.BeginSample("Weather map (raster)");
			if (packed.Count > 0)
			{
				rasterCommands.SetBufferData(buffer, packed);
			}
			rasterCommands.SetComputeBufferParam(shader, kernel, StormsId, buffer);
			rasterCommands.SetComputeVectorParam(shader, RasterId, new Vector4(cascade.Corner.x, cascade.Corner.y, cascade.TexelMeters, count));
			rasterCommands.SetComputeVectorParam(shader, RasterSizeId, new Vector4(cascade.Resolution, 0f, 0f, 0f));
			rasterCommands.SetComputeTextureParam(shader, kernel, OutAId, cascade.GpuA);
			rasterCommands.SetComputeTextureParam(shader, kernel, OutBId, cascade.GpuB);
			int groups = (cascade.Resolution + RasterGroup - 1) / RasterGroup;
			rasterCommands.DispatchCompute(shader, kernel, groups, groups, 1);
			rasterCommands.EndSample("Weather map (raster)");
			Graphics.ExecuteCommandBuffer(rasterCommands);
			cascade.Valid = true;
		}

		/// <summary>Takes a storm's floor, tower and anvil into where the storms' cloud can be.</summary>
		private static void AddExtremes(ref Extremes extremes, in StormCloud cloud, in WeatherSample around)
		{
			extremes.Any = true;
			if (cloud.Anatomy)
			{
				StormAnatomy a = cloud.A;
				extremes.LowestFloor = Mathf.Min(extremes.LowestFloor, a.BaseMetres - Mathf.Max(a.WallCloudDrop, a.ShelfDrop) - BaseRiseMetres);
				extremes.HighestTop = Mathf.Max(extremes.HighestTop, a.TopMetres);
				if (a.AnvilRadius > 0f)
				{
					extremes.Anvils = true;
					extremes.AnvilBottom = Mathf.Min(extremes.AnvilBottom, a.AnvilBase);
					extremes.AnvilTop = Mathf.Max(extremes.AnvilTop, a.AnvilTop);
					extremes.AnvilDepth = Mathf.Max(extremes.AnvilDepth, a.AnvilTop - a.AnvilBase);
					extremes.AnvilExtinction = Mathf.Max(extremes.AnvilExtinction, AnvilExtinction(cloud.Air, a));
				}
			}
			else
			{
				extremes.LowestFloor = Mathf.Min(extremes.LowestFloor, cloud.FallbackBase - BaseRiseMetres);
				extremes.HighestTop = Mathf.Max(extremes.HighestTop, cloud.FallbackTop);
			}
		}

		private static void FinishExtremes(ref Extremes extremes)
		{
			if (extremes.LowestFloor == float.MaxValue)
			{
				extremes.LowestFloor = 0f;
			}
			if (extremes.AnvilBottom == float.MaxValue)
			{
				extremes.AnvilBottom = 0f;
			}
		}

		// ── The kernel's side ──────────────────────────────────────────────

		/// <summary>
		/// Whether anything of a storm reaching <paramref name="reach"/> metres from
		/// <paramref name="centre"/> can land on a square: the nearest point of the square to the centre
		/// is within the reach. By footprint, never by centre — a hurricane's centre is usually off the
		/// map while its bands are on it — and no radius is clamped.
		/// </summary>
		public static bool Overlaps(Vector2 centre, float reach, Rect area)
		{
			if (!(reach > 0f))
			{
				return false;
			}
			float dx = Mathf.Max(0f, Mathf.Max(area.xMin - centre.x, centre.x - area.xMax));
			float dy = Mathf.Max(0f, Mathf.Max(area.yMin - centre.y, centre.y - area.yMax));
			// In double: a storm hundreds of kilometres out squares to 10^11 m², where a float's step
			// is kilometres wide.
			return (double)dx * dx + (double)dy * dy <= (double)reach * reach;
		}

		/// <summary>
		/// One storm as the kernel reads it, <see cref="StormFloat4s"/> float4s (FishWeatherMap.compute's
		/// WmLoad is the other half of this):
		/// <list type="bullet">
		/// <item>0 — centre (world x, z, m), reach (m), flags: 1 it has an anatomy, 2 × its
		/// <see cref="StormCellShape"/>, 16 a supercell (whose updraught is over the cell, not the body)</item>
		/// <item>1 — its body's centre, its anvil's centre (world x, z)</item>
		/// <item>2 — which way a front faces (its travel), how grown it is 0..1, its severity</item>
		/// <item>3 — the cell's peak intensity, the cover its air adds (a storm with no anatomy), the
		/// cell's radius and extent (m)</item>
		/// <item>4 — a front's half-length, the base and top of a storm with no anatomy (m), its core's radius (m)</item>
		/// <item>5 — the wall cloud's radius and drop, the shelf's width and drop (m)</item>
		/// <item>6 — the storm's base and top, the anvil's radius and top (m)</item>
		/// <item>7 — the anvil's underside (m)</item>
		/// </list>
		/// Everything <see cref="Contribute"/> reads that ends up in a texture, and nothing else: the rain
		/// and its offsets are not in either texture, so they are not sent.
		/// </summary>
		private static void PackStorm(in StormCloud s, List<Vector4> into)
		{
			StormCell cell = s.Cell;
			StormAnatomy a = s.A;
			float flags = (s.Anatomy ? 1f : 0f) + 2f * (int)cell.Shape + (cell.Kind == StormKind.Supercell ? 16f : 0f);
			into.Add(new Vector4(s.Centre.x, s.Centre.y, s.Reach, flags));
			into.Add(new Vector4(s.Body.x, s.Body.y, s.AnvilCentre.x, s.AnvilCentre.y));
			into.Add(new Vector4(s.Forward.x, s.Forward.y, s.Life, s.Severity));
			into.Add(new Vector4(cell.PeakIntensity, s.AddedCover, cell.RadiusMeters, cell.ExtentMeters));
			into.Add(new Vector4(s.HalfLength, s.FallbackBase, s.FallbackTop, a.CoreRadius));
			into.Add(new Vector4(a.WallCloudRadius, a.WallCloudDrop, a.ShelfWidth, a.ShelfDrop));
			into.Add(new Vector4(a.BaseMetres, a.TopMetres, a.AnvilRadius, a.AnvilTop));
			into.Add(new Vector4(a.AnvilBase, 0f, 0f, 0f));
		}

		/// <summary>
		/// The storms of a timeline worked out and packed as a cascade covering <paramref name="area"/>
		/// would send them to the kernel: every storm whose footprint overlaps it. Returns how many.
		/// </summary>
		public static int PackFor(WeatherTimeline timeline, StormFrames storms, uint tick, Rect area, List<Vector4> into)
		{
			into.Clear();
			if (timeline == null || timeline.SceneMode != WeatherSceneMode.Own || storms == null)
			{
				return 0;
			}
			WeatherSample around = storms.Around;
			int count = 0;
			for (int i = 0; i < timeline.Cells.Count; i++)
			{
				if (Prepare(timeline, timeline.Cells[i], tick, timeline.TickDelta, timeline.LatitudeDegrees, storms, around, out StormCloud cloud)
					&& Overlaps(cloud.Centre, cloud.Reach, area))
				{
					PackStorm(cloud, into);
					count++;
				}
			}
			return count;
		}

		/// <summary>1 with a smooth edge, as the kernel's WmSmooth: the GLSL/HLSL smoothstep.</summary>
		private static float WmSmooth(float edge0, float edge1, float x) => Smooth(edge0, edge1, x);

		/// <summary>
		/// What the kernel writes for one texel, worked out in C# from the packed storms alone: the
		/// kernel's twin, line for line (FishWeatherMap.compute's WmContribute and Raster). Its rain is
		/// left at 0 — it is not in the textures. For the tests, which hold it to
		/// <see cref="SampleTexel"/>: what they prove is that the packing carries everything the
		/// reference reads and the kernel's arithmetic is the reference's.
		/// </summary>
		public static Texel RasterPacked(IReadOnlyList<Vector4> storms, Vector2 p, float texel)
		{
			float cover = 0f, baseSum = 0f, topSum = 0f, weight = 0f;
			float anvil = 0f, anvilTopSum = 0f, anvilWeight = 0f, anvilDepth = 0f;
			float clearing = 0f, strength = 0f;
			int count = storms != null ? storms.Count / StormFloat4s : 0;
			for (int i = 0; i < count; i++)
			{
				if (!PackedContribute(storms, i * StormFloat4s, p, texel, out Vector4 c0, out Vector4 c1))
				{
					continue;
				}
				cover = Mathf.Max(cover, c0.x);
				if (c0.x > 0f)
				{
					baseSum += c0.x * c0.y;
					topSum += c0.x * c0.z;
					weight += c0.x;
				}
				anvil = Mathf.Max(anvil, c1.x);
				if (c1.x > 0f)
				{
					anvilTopSum += c1.x * c1.y;
					anvilWeight += c1.x;
					anvilDepth = Mathf.Max(anvilDepth, c1.z);
				}
				clearing = Mathf.Max(clearing, c1.w);
				strength = Mathf.Max(strength, c0.w);
			}
			return new Texel
			{
				Cover = Mathf.Clamp01(cover),
				Anvil = Mathf.Clamp01(anvil),
				Clearing = Mathf.Clamp01(clearing),
				Strength = Mathf.Clamp01(strength),
				Base = weight > 0f ? baseSum / weight : 0f,
				Top = weight > 0f ? topSum / weight : 0f,
				AnvilTop = anvilWeight > 0f ? anvilTopSum / anvilWeight : 0f,
				AnvilDepth = anvilDepth,
			};
		}

		/// <summary>StormCell.Coverage from the packed shape (WmCoverage).</summary>
		private static float PackedCoverage(int shape, float radius, float extent, Vector2 forward, Vector2 offset)
		{
			if (shape == (int)StormCellShape.Front)
			{
				var along = new Vector2(-forward.y, forward.x);
				float across = Vector2.Dot(offset, forward);
				float sideways = Mathf.Abs(Vector2.Dot(offset, along));
				float reachAcross = across >= 0f ? radius * 0.45f : radius * 1.6f;
				float deep = 1f - WmSmooth(0f, 1f, Mathf.Abs(across) / Mathf.Max(1e-3f, reachAcross));
				float halfLine = Mathf.Max(radius, extent);
				float lengthwise = 1f - WmSmooth(halfLine * 0.75f, halfLine, sideways);
				return deep * lengthwise;
			}
			float d = offset.magnitude;
			if (shape == (int)StormCellShape.Eyewall)
			{
				float eye = Mathf.Clamp(extent, 0f, radius * 0.6f);
				if (d <= eye)
				{
					float t = eye > 1e-3f ? d / eye : 1f;
					return Mathf.Lerp(0.05f, 1f, WmSmooth(0f, 1f, t));
				}
				return 1f - WmSmooth(eye, radius, d);
			}
			if (shape == (int)StormCellShape.Funnel)
			{
				if (d <= radius)
				{
					return 1f;
				}
				float falloff = 1f - WmSmooth(radius, Mathf.Max(extent, radius * 1.5f), d);
				return falloff * falloff;
			}
			return 1f - WmSmooth(radius * 0.55f, radius, d);
		}

		/// <summary>
		/// One packed storm at one place (WmContribute): c0 cover, base, top, strength; c1 anvil,
		/// anvil top, anvil depth, clearing.
		/// </summary>
		private static bool PackedContribute(IReadOnlyList<Vector4> storms, int at, Vector2 p, float texel, out Vector4 c0, out Vector4 c1)
		{
			c0 = Vector4.zero;
			c1 = Vector4.zero;
			Vector4 d0 = storms[at], d1 = storms[at + 1], d2 = storms[at + 2], d3 = storms[at + 3];
			Vector4 d4 = storms[at + 4], d5 = storms[at + 5], d6 = storms[at + 6], d7 = storms[at + 7];
			var centre = new Vector2(d0.x, d0.y);
			float reach = d0.z;
			int flags = Mathf.RoundToInt(d0.w);
			bool anatomy = (flags & 1) != 0;
			int shape = (flags >> 1) & 7;
			bool supercell = (flags & 16) != 0;
			var body = new Vector2(d1.x, d1.y);
			var anvilCentre = new Vector2(d1.z, d1.w);
			var forward = new Vector2(d2.x, d2.y);
			float life = d2.z, severity = d2.w;
			float peak = d3.x, addedCover = d3.y, radius = d3.z, extent = d3.w;
			float halfLength = d4.x, fallbackBase = d4.y, fallbackTop = d4.z, core = d4.w;
			float wallRadius = d5.x, wallDrop = d5.y, shelfWidth = d5.z, shelfDrop = d5.w;
			float baseM = d6.x, topM = d6.y, anvilRadius = d6.z, anvilTop = d6.w;
			float anvilBase = d7.x;

			Vector2 offset = p - centre;
			if (offset.sqrMagnitude > reach * reach)
			{
				return false;
			}
			if (!anatomy)
			{
				float shield = PackedCoverage(shape, radius, extent, forward, offset * CloudShieldScale) * life * Mathf.Clamp01(peak);
				c0 = new Vector4(addedCover * shield, fallbackBase, fallbackTop, 0f);
				return c0.x > 0f;
			}

			float stands = WmSmooth(0f, 0.35f, life);
			float climbed = Mathf.Lerp(0.35f, 1f, WmSmooth(0f, 0.7f, life));
			float spread = WmSmooth(0.4f, 0.9f, life);
			float depth = Mathf.Max(100f, topM - baseM);
			float cloud;
			float baseHere = baseM;
			float topHere;
			var along = new Vector2(-forward.y, forward.x);
			if (shape == (int)StormCellShape.Front)
			{
				float u = Vector2.Dot(offset, forward);
				float v = Mathf.Abs(Vector2.Dot(offset, along));
				float lengthwise = 1f - WmSmooth(0.75f * halfLength, halfLength, v);
				float lead = 0.45f * radius;
				float width = Mathf.Max(1f, shelfWidth);
				float back = lead - 2f * core;
				cloud = WmSmooth(back, back + core, u) * (1f - WmSmooth(lead, lead + 0.3f * width, u)) * lengthwise;
				topHere = baseM + depth * climbed * Mathf.Lerp(0.6f, 1f, WmSmooth(back, lead - 0.3f * core, u));
				float x = (u - lead) / width;
				float shelf = WmSmooth(-0.15f, 0.05f, x) * (1f - WmSmooth(0.85f, 1f, x)) * lengthwise;
				if (shelf > 0f)
				{
					float shelfFloor = baseM - shelfDrop;
					float shelfTop = shelfFloor + Mathf.Max(80f, (baseM + 0.5f * shelfDrop - shelfFloor) * (1f - Mathf.Clamp01(x)));
					float total = cloud + shelf;
					baseHere = (cloud * baseM + shelf * shelfFloor) / total;
					topHere = (cloud * topHere + shelf * shelfTop) / total;
					cloud = Mathf.Max(cloud, shelf);
				}
			}
			else
			{
				float d = Vector2.Distance(p, body) / Mathf.Max(1f, core);
				cloud = 1f - WmSmooth(0.6f, 1f, d);
				Vector2 updraught = supercell ? centre : body;
				float du = Mathf.Min(1f, Vector2.Distance(p, updraught) / Mathf.Max(1f, core));
				topHere = baseM + depth * climbed * (1f - 0.35f * du * du);
				if (wallRadius > 0f)
				{
					float inner = Mathf.Min(0.9f, Mathf.Max(0.45f, 0.75f * texel / wallRadius));
					float wall = 1f - WmSmooth(inner, 1f, offset.magnitude / wallRadius);
					baseHere = baseM - wallDrop * wall;
					cloud = Mathf.Max(cloud, wall);
				}
			}
			float cover = cloud * stands;
			float baseAt = Mathf.Max(0f, baseHere);
			float topAt = Mathf.Max(baseAt + 100f, topHere);
			c0 = new Vector4(cover, baseAt, topAt, severity * cover);

			if (anvilRadius > 0f && spread > 0f)
			{
				float da;
				if (shape == (int)StormCellShape.Front)
				{
					Vector2 o = p - anvilCentre;
					float ua = Vector2.Dot(o, forward);
					float va = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(o, along)) - halfLength);
					da = Mathf.Sqrt(ua * ua + va * va) / anvilRadius;
				}
				else
				{
					da = Vector2.Distance(p, anvilCentre) / anvilRadius;
				}
				float thin = (1f - WmSmooth(0.3f, 1f, da)) * spread;
				if (thin > 0f)
				{
					c1.x = thin;
					c1.y = anvilTop;
					c1.z = (anvilTop - anvilBase) * thin;
				}
			}

			if (shape == (int)StormCellShape.Eyewall)
			{
				float eye = Mathf.Clamp(extent, 0f, radius * 0.6f);
				if (life > 0f && eye > 1e-3f)
				{
					c1.w = Mathf.Clamp01(life * peak) * (1f - WmSmooth(eye * 0.6f, eye * 1.05f, offset.magnitude));
				}
			}
			return c0.x > 0f || c1.x > 0f || c1.w > 0f;
		}

		public void Publish(bool valid)
		{
			bool nearValid = valid && near.Valid && near.A != null;
			bool farValid = valid && far.Valid && far.A != null;
			if (near.A != null)
			{
				Shader.SetGlobalTexture(TextureId, near.A);
				Shader.SetGlobalTexture(TextureBId, near.B);
			}
			if (far.A != null)
			{
				Shader.SetGlobalTexture(FarTextureId, far.A);
				Shader.SetGlobalTexture(FarTextureBId, far.B);
			}
			Shader.SetGlobalVector(RectId, new Vector4(near.Corner.x, near.Corner.y, near.Size, near.Size));
			Shader.SetGlobalVector(FarRectId, new Vector4(far.Corner.x, far.Corner.y, far.Size, far.Size));
			bool any = (nearValid && near.Any) || (farValid && far.Any);
			Extremes extremes = StormExtremes;
			// x the fine cascade is there, y the coarse one, z either holds a storm (the shader reads
			// neither otherwise), w what an anvil's ice takes out of the light (1/m).
			Shader.SetGlobalVector(ParamsId, new Vector4(nearValid ? 1f : 0f, farValid ? 1f : 0f, any ? 1f : 0f, any ? extremes.AnvilExtinction : 0f));
		}

		public void Dispose()
		{
			near.Dispose();
			far.Dispose();
			rasterCommands?.Release();
			rasterCommands = null;
		}
	}
}
