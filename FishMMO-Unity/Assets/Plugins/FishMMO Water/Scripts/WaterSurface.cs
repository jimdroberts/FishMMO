using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Water
{
	/// <summary>
	/// The sea in a scene: where its surface is, how it moves, and what the shader is allowed to
	/// ask the render pipeline for.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The still-water level is this object's Y, in metres, and it is not decoration.</b> A
	/// generated scene is cut out of a planet whose sea level the surface function knows exactly,
	/// so the sea belongs at the height the globe says and nowhere else — the shore is then where
	/// the map draws it. One world unit is one metre throughout, which is also what makes the wave
	/// speeds right: deep-water waves travel at √(g/k), so the numbers only work at true scale.
	/// </para>
	/// <para>
	/// <b>It follows the camera.</b> The mesh is a disc centred on whichever camera is about to
	/// render, moved in XZ only; the waves are a function of world position, so the sea does not
	/// travel with the viewer even though its geometry does.
	/// </para>
	/// </remarks>
	[ExecuteAlways]
	[AddComponentMenu("FishMMO/Water/Water Surface")]
	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
	public sealed class WaterSurface : MonoBehaviour, SurfaceWater.ISource
	{
		// ── Shader globals ────────────────────────────────────────────
		private static readonly int WaveId = Shader.PropertyToID("_FishWaterWave");
		private static readonly int MotionId = Shader.PropertyToID("_FishWaterMotion");
		private static readonly int CountId = Shader.PropertyToID("_FishWaterWaveCount");
		private static readonly int LevelId = Shader.PropertyToID("_FishWaterLevel");
		private static readonly int MeanLevelId = Shader.PropertyToID("_FishWaterMeanLevel");
		private static readonly int TimeId = Shader.PropertyToID("_FishWaterTime");
		private static readonly int CloudShadowId = Shader.PropertyToID("_FishWaterCloudShadow");
		private static readonly int WindId = Shader.PropertyToID("_FishWaterWind");
		private static readonly int WhitecapId = Shader.PropertyToID("_FishWaterWhitecap");
		private static readonly int GravityId = Shader.PropertyToID("_FishWaterGravity");
		private static readonly int PatchId = Shader.PropertyToID("_FishWaterPatch");
		private static readonly string[] DisplacementNames =
			{ "_FishWaterDisplacement0", "_FishWaterDisplacement1", "_FishWaterDisplacement2" };
		private static readonly string[] DerivativeNames =
			{ "_FishWaterDerivatives0", "_FishWaterDerivatives1", "_FishWaterDerivatives2" };
		private static readonly int UnderwaterTintId = Shader.PropertyToID("_UnderwaterTint");
		private static readonly int UnderwaterDepthId = Shader.PropertyToID("_UnderwaterDepth");
		private static readonly int UnderwaterShaftsId = Shader.PropertyToID("_Shafts");
		private static readonly int UnderwaterMotesId = Shader.PropertyToID("_Motes");

		private const string DepthKeyword = "_WATER_DEPTH";
		private const string RefractionKeyword = "_WATER_REFRACTION";

		/// <summary>
		/// Seconds before the clock wraps.
		/// </summary>
		/// <remarks>
		/// A float holding ten thousand seconds still resolves a millisecond, while <c>_Time.y</c>
		/// after a few hours of a session no longer resolves a frame and the sea visibly judders —
		/// which a client that stays open all evening will reach. The wrap puts a single
		/// imperceptible discontinuity in every 2.8 hours in exchange.
		/// </remarks>
		private const double WrapSeconds = 10000.0;

		[Header("Sea")]
		[Tooltip("The material. Must use FishMMO/Water/Ocean.")]
		public Material Material;

		[Header("Waves")]
		[Tooltip("Which way the wind blows TOWARD, clockwise from north.")]
		[Range(0f, 360f)] public float WindDirectionDegrees = 45f;
		[Tooltip("Metres per second. 5 is a breeze, 15 a gale. Sets the wavelength as well as the height.")]
		[Range(0f, 30f)] public float WindSpeed = 8f;
		[Tooltip("0 every wave runs with the wind, 1 they fan across the swell.")]
		[Range(0f, 1f)] public float Spread = 0.6f;
		[Tooltip("0 rolling sine swell, 1 sharp crests that throw foam.")]
		[Range(0f, 1f)] public float Choppiness = 0.5f;
		[Tooltip("Multiplies every wave height, for a sheltered bay or a storm.")]
		[Range(0f, 3f)] public float WaveScale = 1f;
		[Tooltip("How many waves are summed. Each costs one sine per vertex.")]
		[Range(1, WaterWaves.MaximumWaves)] public int WaveCount = 6;
		[Tooltip("Surface gravity, m/s². Driven from the celestial body by WaterEnvironment.")]
		[Range(0.05f, 30f)] public float Gravity = WaterWaves.EarthGravity;

		[Header("Spectrum")]
		[Tooltip("The FFT compute shader. Without it the sea is flat.")]
		public ComputeShader Spectrum;
		[Tooltip("The same FFT as render passes, for machines with no compute shaders (WebGL2, GLES3). Referenced here so a build includes it.")]
		public Shader SpectrumPasses;

		/// <summary>The render-pass FFT's name, for finding it when the reference was never set.</summary>
		public const string SpectrumPassesShader = "Hidden/FishMMO/Water/FFT";
		[Tooltip("Development: run the render-pass FFT even on a machine with compute, to see what WebGL2 and GLES3 draw.")]
		public bool ForceRenderPasses;
		private bool builtWithPasses;
		[Tooltip("How sharp the crests are. Above about 1.5 the surface folds through itself.")]
		[Range(0f, 2f)] public float FFTChoppiness = 1.2f;
		[Tooltip("Seconds before the wave animation repeats exactly. Also what the baked fallback loops on.")]
		[Range(20f, 600f)] public float LoopPeriod = 120f;

		[Header("Geometry")]
		[Tooltip("Radius of the solid centre disc, in metres.")]
		public float InnerRadius = 4f;
		[Tooltip("How far the sea reaches, in metres. Beyond the camera's far clip is wasted.")]
		public float OuterRadius = 12000f;
		[Tooltip("Rings of vertices from the centre out.")]
		[Range(8, 1024)] public int Rings = 300;
		[Tooltip("Vertices around each ring. Low values show as a polygonal horizon, and coarse arcs cannot curl a breaking crest.")]
		[Range(8, 2048)] public int Segments = 720;
		/// <remarks>
		/// Surf is watched from twenty to a hundred metres off, and a breaking crest needs vertices
		/// well under a metre apart to curl at all. Geometric rings put two or three metres between
		/// them at that range; this zone is spaced evenly instead.
		/// </remarks>
		[Tooltip("Radius around the camera with evenly spaced rings, dense enough for breaking waves to curl.")]
		[Range(0f, 600f)] public float NearRadius = 130f;
		[Tooltip("Share of the rings spent inside the near radius.")]
		[Range(0.1f, 0.9f)] public float NearRingFraction = 0.65f;

		[Header("Quality")]
		[Tooltip("Refract what is behind the water. Needs the URP asset's Opaque Texture; ignored when it is off.")]
		public bool Refraction = true;
		[Tooltip("Shore foam, soft edges and depth absorption. Needs the URP asset's Depth Texture; ignored when it is off.")]
		public bool DepthEffects = true;
		[Tooltip("Warn once in the log when a quality option had to be turned off.")]
		public bool ReportQuality = true;

		[Header("Underwater")]
		[Tooltip("Tint and dim the whole scene while the camera is below the surface.")]
		public bool UnderwaterEffect = true;
		[Tooltip("The colour the deep fades to.")]
		public Color UnderwaterTint = new Color(0.05f, 0.28f, 0.36f);
		[Tooltip("Roughly how far a diver can see, in metres.")]
		[Range(1f, 200f)] public float UnderwaterVisibility = 22f;
		[Tooltip("Shafts of sunlight through the water, focused by the waves overhead and cut by the " +
			"shadows of whatever stands in the sun. The dearest part of the underwater pass — sixteen " +
			"steps a pixel — and 0 skips it.")]
		[Range(0f, 2f)] public float UnderwaterShafts = 1f;
		[Tooltip("Silt and plankton hanging in the water, drifting with the current. What tells the " +
			"eye it is moving through water rather than fog. 0 leaves the water empty.")]
		[Range(0f, 2f)] public float UnderwaterMotes = 1f;
		[Tooltip("The underwater pass. Referenced so a build includes it; found by name otherwise.")]
		public Shader UnderwaterShader;

		[Header("Caustics")]
		[Tooltip("Sunlight focused onto whatever is under the water by the waves above it.")]
		public bool Caustics = true;
		[Tooltip("How strongly the waves' focusing brightens and darkens the light under them.")]
		[Range(0f, 2f)] public float CausticsStrength = 1f;
		[Tooltip("How deep the caustics carry, in metres: their contrast falls to a third by this depth.")]
		[Range(1f, 60f)] public float CausticsClarity = 12f;
		[Tooltip("How far from the camera they are gone by, in metres; the pattern is finer than a pixel past that.")]
		[Range(10f, 600f)] public float CausticsFadeDistance = 120f;
		[Tooltip("The caustics pass. Referenced so a build includes it; found by name otherwise.")]
		public Shader CausticsShader;

		/// <summary>The caustics pass's name, for finding it when the reference was never set.</summary>
		public const string CausticsShaderName = "FishMMO/Water/Caustics";

		/// <summary>The underwater pass's name, for finding it when the reference was never set.</summary>
		public const string UnderwaterShaderName = "FishMMO/Water/Underwater";

		private MeshFilter meshFilter;
		private MeshRenderer meshRenderer;
		private Mesh mesh;
		private readonly WaterWave[] waves = new WaterWave[WaterWaves.MaximumWaves];
		private readonly Vector4[] packed = new Vector4[WaterWaves.MaximumWaves];
		private readonly Vector4[] packedMotion = new Vector4[WaterWaves.MaximumWaves];
		private int liveWaves;
		private double clock;
		private double lastRealtime = -1.0;
		private int builtRings, builtSegments;
		private float builtInner, builtOuter, builtNear = -1f;
		private bool reported;
		private WaterFFT fft;
		private float builtWind = -1f;
		private float builtHeading = -1f;
		private float builtGravity = -1f;
		private float builtChoppiness = -1f;
		private GameObject causticsHost;
		private Material causticsMaterial;
		private GameObject underwaterHost;
		private MeshRenderer underwaterRenderer;
		private Material underwaterMaterial;
		private Mesh underwaterMesh;

		/// <summary>
		/// Mean sea level in world metres — this object's own height, and what the scene was
		/// generated against.
		/// </summary>
		/// <remarks>
		/// The transform is never written by the tide. A generated scene places this at the height
		/// the planet says its water line is, and that is the reference the terrain, the shore
		/// field and every placed object were built around; a tide that moved it would make the
		/// scene's own datum drift.
		/// </remarks>
		public float MeanSeaLevel => transform.position.y;

		/// <summary>
		/// How far the tide stands above mean level right now, in metres. Driven from outside.
		/// </summary>
		public float TideMetres { get; set; }

		/// <summary>Where the still water actually is: mean level plus the tide.</summary>
		public float SeaLevel => MeanSeaLevel + TideMetres;

		/// <summary>The sea state, for anything that needs to float on it.</summary>
		public WaterWave[] Waves => waves;

		/// <summary>How many of <see cref="Waves"/> are in use.</summary>
		public int WaveLiveCount => liveWaves;

		/// <summary>The clock the surface is being drawn at, in seconds.</summary>
		public double Clock => clock;

		/// <summary>
		/// 1 under open sky, 0 fully under cloud. Driven by the weather system where there is one.
		/// </summary>
		[HideInInspector] public float CloudShadow = 1f;

		/// <summary>
		/// The sea's surface height at a world position, in metres — the surface as it is drawn.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The sea the player sees, not a model of it.</b> This used to sum the six Gerstner waves
		/// the sea was drawn with before the FFT replaced them, so anything floating bobbed on waves
		/// that were no longer there. It now evaluates the FFT's own strongest components on the CPU
		/// (<see cref="WaterSpectrum"/>) and then does exactly what the vertex shader does with
		/// them: the distance fade, the shoaling and depth limit, and the surf train rolling onto
		/// the beach, from the same shore field and the same tide.
		/// </para>
		/// <para>
		/// <b>Solved, not sampled.</b> The sea moves water sideways as well as up, so the water
		/// over a point came from somewhere else: the point it started from is found by stepping
		/// back along the throw, and its height is the answer.
		/// </para>
		/// </remarks>
		public float HeightAt(Vector3 worldPosition)
		{
			WaterSpectrum spectrum = SpectrumForQueries();
			spectrum?.Evaluate(clock);
			SurfSettings surf = ReadSurf();

			// The vertex shader flattens the sea with distance from the camera; so does this.
			float fade = 1f;
			Camera camera = Camera.main;
			if (camera != null)
			{
				Vector3 flatPoint = new Vector3(worldPosition.x, SeaLevel, worldPosition.z);
				fade = 1f - Smoothstep(surf.FadeStart, Mathf.Max(surf.FadeEnd, surf.FadeStart + 1f),
					Vector3.Distance(flatPoint, camera.transform.position));
			}

			var target = new Vector2(worldPosition.x, worldPosition.z);
			Vector2 flat = target;
			float height = 0f;
			for (int i = 0; i < 3; i++)
			{
				Displace(spectrum, flat, fade, surf, out height, out Vector2 moved);
				flat = target - moved;
			}
			return SeaLevel + height;
		}

		private struct SurfSettings
		{
			public float Height, Length, Break, Pitch, FadeStart, FadeEnd;
		}

		private static readonly int SurfHeightId = Shader.PropertyToID("_ShoreWaveHeight");
		private static readonly int SurfLengthId = Shader.PropertyToID("_ShoreWaveLength");
		private static readonly int ShoreBreakId = Shader.PropertyToID("_ShoreBreak");
		private static readonly int SurfPitchId = Shader.PropertyToID("_ShoreWavePitch");
		private static readonly int FadeStartId = Shader.PropertyToID("_WaveFadeStart");
		private static readonly int FadeEndId = Shader.PropertyToID("_WaveFadeEnd");

		/// <summary>WebGL has no threads; everywhere else a rebuild runs on the thread pool.</summary>
		private static bool CanBuildOffThread => Application.platform != RuntimePlatform.WebGLPlayer;

		private WaterSpectrum querySpectrum;
		private System.Threading.Tasks.Task<WaterSpectrum> queryBuild;
		private MaterialPropertyBlock queryBlock;
		private WaterShoreField shoreField;

		/// <summary>
		/// The spectrum height queries run on, rebuilt when the sea state moves enough to matter.
		/// </summary>
		/// <remarks>
		/// A build is 20 to 30 ms, and the sea state eases every frame while the weather changes, so
		/// it is rebuilt only past two percent of wind or a degree of heading, and off the main
		/// thread wherever there are threads to use — the previous one answers in the meantime. The
		/// very first build answers with a flat sea until it lands.
		/// </remarks>
		private WaterSpectrum SpectrumForQueries()
		{
			// Nothing is drawn by the transform, so there is nothing for a height to match.
			if (fft == null)
			{
				return null;
			}
			if (queryBuild != null && queryBuild.IsCompleted)
			{
				if (queryBuild.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
				{
					querySpectrum = queryBuild.Result;
				}
				queryBuild = null;
			}
			bool stale = querySpectrum == null
				|| Mathf.Abs(querySpectrum.Wind - WindSpeed) > Mathf.Max(0.05f, WindSpeed * 0.02f)
				|| Mathf.Abs(Mathf.DeltaAngle(querySpectrum.HeadingDegrees, WindDirectionDegrees)) > 1f
				|| !Mathf.Approximately(querySpectrum.Gravity, Gravity)
				|| !Mathf.Approximately(querySpectrum.Choppiness, FFTChoppiness);
			if (stale && queryBuild == null)
			{
				float wind = WindSpeed;
				float heading = WindDirectionDegrees;
				float gravity = Gravity;
				float amplitude = fft.Amplitude;
				float choppiness = FFTChoppiness;
				float loop = LoopPeriod;
				float[] patches = (float[])fft.PatchMetres.Clone();
				/* Off the main thread wherever there are threads — the first build as well: a query
				 * before it lands sees a flat sea for a frame or two, which is better than every
				 * scene with a camera near the water stalling for 25 ms on its first frame. */
				if (!CanBuildOffThread)
				{
					querySpectrum = WaterSpectrum.Build(wind, heading, gravity, amplitude, choppiness, loop, 1u, patches);
				}
				else
				{
					queryBuild = System.Threading.Tasks.Task.Run(() =>
						WaterSpectrum.Build(wind, heading, gravity, amplitude, choppiness, loop, 1u, patches));
				}
			}
			return querySpectrum;
		}

		/// <summary>The surf and fade settings the shader is drawing with: the renderer's block first, then the material.</summary>
		private SurfSettings ReadSurf()
		{
			if (meshRenderer == null)
			{
				meshRenderer = GetComponent<MeshRenderer>();
			}
			queryBlock ??= new MaterialPropertyBlock();
			if (meshRenderer != null)
			{
				meshRenderer.GetPropertyBlock(queryBlock);
			}
			return new SurfSettings
			{
				Height = Setting(SurfHeightId, 1.1f),
				Length = Setting(SurfLengthId, 40f),
				Break = Setting(ShoreBreakId, 0.62f),
				Pitch = Setting(SurfPitchId, 1.6f),
				FadeStart = Setting(FadeStartId, 900f),
				FadeEnd = Setting(FadeEndId, 4000f),
			};
		}

		private float Setting(int id, float fallback)
		{
			if (queryBlock != null && queryBlock.HasFloat(id))
			{
				return queryBlock.GetFloat(id);
			}
			return Material != null && Material.HasProperty(id) ? Material.GetFloat(id) : fallback;
		}

		/// <summary>
		/// Where the water that starts at a flat point ends up: its height above still water, and how
		/// far it is thrown sideways. <c>FishWaterDisplace</c> in FishWaterWaves.hlsl, line for line.
		/// </summary>
		private void Displace(WaterSpectrum spectrum, Vector2 flat, float fade, in SurfSettings surf,
			out float height, out Vector2 moved)
		{
			float h = 0f, dx = 0f, dz = 0f;
			if (spectrum != null)
			{
				spectrum.Sample(flat.x, flat.y, out h, out dx, out dz);
				h *= fade;
				dx *= fade;
				dz *= fade;
			}

			if (shoreField == null)
			{
				shoreField = GetComponent<WaterShoreField>();
			}
			if (shoreField != null && shoreField.TrySample(flat, out float meanDepth, out float edge))
			{
				float depth = meanDepth + TideMetres;

				// Shoaling, then the depth limit that breaks it.
				// Green's law from a twentieth of the deep-water wavelength, as the shader has it.
				float reference = Mathf.Max(0.5f, 0.05f * Mathf.Max(4f, surf.Length));
				float gain = Mathf.Clamp(Mathf.Pow(reference / Mathf.Max(0.35f, depth), 0.25f), 1f, 2f);
				float grown = Mathf.Abs(h) * gain;
				float allowed = Mathf.Max(0f, depth) * surf.Break;
				float scale = grown > 1e-4f ? Mathf.Min(grown, allowed) / grown : 1f;
				h *= gain * scale;
				float lateral = Mathf.Clamp01(depth * 0.5f);
				dx *= lateral;
				dz *= lateral;

				// The surf train, keeping time with the swash (FishWaterSurf.hlsl).
				float shoreFacing = ShoreFacing(flat, out Vector2 shoreward);
				if (surf.Height > 0.01f && shoreward.sqrMagnitude > 0.5f)
				{
					if (shore == null)
					{
						shore = GetComponent<WaterShore>();
					}
					float deepWavelength, wavelength, phase;
					if (shore != null && shore.isActiveAndEnabled && shore.SurfPeriod > 0.25f)
					{
						float period = Mathf.Max(0.5f, shore.SurfPeriod);
						deepWavelength = Mathf.Max(4f, shore.SurfDeepWavelength);
						float breakingDepth = Mathf.Max(0.1f, shore.SurfSeaHeight) / 0.78f;
						wavelength = Mathf.Max(4f, period * Mathf.Sqrt(Mathf.Max(0.05f, Gravity) * breakingDepth));
						Vector2 waterline = flat + shoreward * Mathf.Max(0f, edge);
						// As the shader has it, in single precision: the clock goes in as a float.
						float cycles = (float)shore.SurfCycles + AlongShore(waterline).x;
						phase = 2f * Mathf.PI * (cycles + edge / wavelength) + 0.5f * Mathf.PI;
					}
					else
					{
						deepWavelength = Mathf.Max(4f, surf.Length);
						wavelength = deepWavelength;
						float k0 = 2f * Mathf.PI / wavelength;
						phase = k0 * edge + Mathf.Sqrt(Mathf.Max(0.05f, Gravity) * k0) * (float)clock;
					}
					const float Span = 10f;
					float beachSlope = Mathf.Abs(Depth(flat - shoreward * Span) - Depth(flat + shoreward * Span)) / (2f * Span);
					// Only a cliff turns the train away; the surging test stops the barrel on a steep bank.
					float reflective = Smoothstep(0.6f, 1f, beachSlope);
					float feel = Mathf.Clamp01(1f - depth / (deepWavelength * 0.5f));
					float exposure = ShoreExposure(shoreward, shoreFacing);
					float amplitude = surf.Height * (1f + feel * 1.6f) * feel * (1f - reflective) * exposure;
					// The sea's own breaking index, crest height over depth.
					float limit = Mathf.Max(0f, depth) * surf.Break;
					float breaking = Mathf.Clamp01((amplitude - limit) / Mathf.Max(0.05f, amplitude));
					amplitude = Mathf.Min(amplitude, limit) * Mathf.Clamp01(depth * 1.2f);
					float sin = Mathf.Sin(phase);
					float crest = Mathf.Clamp01(sin);
					h += amplitude * sin;

					float iribarren = beachSlope / Mathf.Sqrt(Mathf.Max(1e-4f, Mathf.Max(0.05f, amplitude * 2f) / deepWavelength));
					// Plunging between about 0.5 and 3.3; spilling below; past 3.3 it surges up the face unbroken.
					float plunging = Smoothstep(0.35f, 0.9f, iribarren) * (1f - Smoothstep(2.5f, 3.5f, iribarren));
					float spilling = 1f - Smoothstep(0.35f, 0.9f, iribarren);
					// A lean of 1 stands the face exactly vertical (0.1378 of a wavelength).
					float lean = surf.Pitch * plunging + 0.33f * spilling;
					float throwMetres = lean * breaking * crest * crest * crest * wavelength * 0.1378f * shoreFacing;
					// Never past the water's edge, nor more than halfway there.
					throwMetres = Mathf.Min(throwMetres, 0.5f * Mathf.Max(0f, edge));
					dx += shoreward.x * throwMetres;
					dz += shoreward.y * throwMetres;
				}
			}
			height = h;
			moved = new Vector2(dx, dz);
		}

		private WaterShore shore;

		/// <summary>FishWaterSurfHash, in the same integer arithmetic.</summary>
		private static float SurfHash(int x, int y)
		{
			unchecked
			{
				uint h = (uint)x * 73856093u ^ (uint)y * 19349663u;
				h ^= h >> 16;
				h *= 0x7feb352du;
				h ^= h >> 15;
				h *= 0x846ca68bu;
				h ^= h >> 16;
				return (h & 0xFFFFFFu) / 16777216f;
			}
		}

		/// <summary>FishWaterSurfNoise: smooth value noise, one feature to a cell this many metres across.</summary>
		private static float SurfNoise(Vector2 xz, float cellMetres, int salt)
		{
			float px = xz.x / cellMetres, pz = xz.y / cellMetres;
			float ix = Mathf.Floor(px), iz = Mathf.Floor(pz);
			float fx = px - ix, fz = pz - iz;
			fx = fx * fx * (3f - 2f * fx);
			fz = fz * fz * (3f - 2f * fz);
			int cx = (int)ix + salt, cz = (int)iz + salt * 7;
			float a = SurfHash(cx, cz), b = SurfHash(cx + 1, cz);
			float d = SurfHash(cx, cz + 1), e = SurfHash(cx + 1, cz + 1);
			return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(d, e, fx), fz);
		}

		/// <summary>FishWaterAlongShore: the phase offset along the shore (x, cycles) and the run-up share (y).</summary>
		private Vector2 AlongShore(Vector2 xz)
		{
			float radians = WindDirectionDegrees * Mathf.Deg2Rad;
			var direction = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
			float wander = SurfNoise(xz, 90f, 11);
			float cusps = SurfNoise(xz, 35f, 29);
			float phase = -Vector2.Dot(xz, direction) / 70f + wander * 1.6f;
			return new Vector2(phase, 0.7f + 0.6f * cusps);
		}

		/// <summary>Metres of water over the ground now, tide in; open ocean off the field.</summary>
		private float Depth(Vector2 xz)
		{
			return shoreField != null && shoreField.TrySample(xz, out float depth, out _) ? depth + TideMetres : 1000f;
		}

		/// <summary>Signed metres to the water's edge; open ocean off the field.</summary>
		private float EdgeDistance(Vector2 xz)
		{
			return shoreField != null && shoreField.TrySample(xz, out _, out float edge) ? edge : 1000f;
		}

		/// <summary>
		/// FishWaterShoreFacing: the direction to the nearest shore down the distance field, and how
		/// sure it is — 0 on a ridge midway between two shores.
		/// </summary>
		private float ShoreFacing(Vector2 xz, out Vector2 towardShore)
		{
			towardShore = Vector2.zero;
			float step = Mathf.Max(1f, shoreField != null ? shoreField.TexelMetres : 1f);
			float east = EdgeDistance(xz + new Vector2(step, 0f)) - EdgeDistance(xz - new Vector2(step, 0f));
			float north = EdgeDistance(xz + new Vector2(0f, step)) - EdgeDistance(xz - new Vector2(0f, step));
			var gradient = new Vector2(-east, -north) / (2f * step);
			float length2 = gradient.sqrMagnitude;
			if (length2 <= 1e-8f)
			{
				return 0f;
			}
			float slope = Mathf.Sqrt(length2);
			towardShore = gradient / slope;
			return Mathf.Clamp01((slope - 0.35f) / 0.4f);
		}

		/// <summary>FishWaterShoreExposure: how much of the sea reaches a shore facing this way, 0.25 to 1.</summary>
		private float ShoreExposure(Vector2 towardShore, float confidence)
		{
			float radians = WindDirectionDegrees * Mathf.Deg2Rad;
			var direction = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
			float facing = Smoothstep(-0.6f, 0.4f, Vector2.Dot(towardShore, direction));
			return Mathf.Lerp(0.6f, Mathf.Lerp(0.25f, 1f, facing), Mathf.Clamp01(confidence));
		}

		/// <summary>HLSL's smoothstep. Not Mathf.SmoothStep, which interpolates between its first two arguments.</summary>
		private static float Smoothstep(float edge0, float edge1, float x)
		{
			float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
			return t * t * (3f - 2f * t);
		}

		/// <summary>True when a world position is under the sea's surface.</summary>
		public bool IsSubmerged(Vector3 worldPosition) => worldPosition.y < HeightAt(worldPosition);

		/// <summary>
		/// True when a world position is under the moving surface, asked of the waves only near it.
		/// </summary>
		/// <remarks>
		/// The exact height costs a spectrum sum, and a point higher above the still water than any
		/// crest could stand — twice the significant height of the sea the wind can raise, and the
		/// surf on top — is plainly not under it, nor one deeper than any trough plainly over it.
		/// </remarks>
		public bool IsUnderSurface(Vector3 worldPosition)
		{
			float above = worldPosition.y - SeaLevel;
			float highestCrest = 2f * 0.21f * WindSpeed * WindSpeed / Mathf.Max(0.05f, Gravity) + 4f;
			return above < highestCrest && (above < -highestCrest || IsSubmerged(worldPosition));
		}

		// ── What the weather is told ──────────────────────────────────
		//
		// The weather finds where rain and snow land by casting rays at solid ground, and a ray goes
		// straight through water; the sea tells it where its surface is instead (SurfaceWater).

		float SurfaceWater.ISource.Level => SeaLevel;

		float SurfaceWater.ISource.WaveHeight
		{
			get
			{
				// The environment's sea state knows the fetch and the swell; without one, the sea a
				// wind this strong raises when it has blown long enough (Pierson-Moskowitz).
				if (environment == null)
				{
					environment = GetComponent<WaterEnvironment>();
				}
				return environment != null && environment.SignificantHeight > 0f
					? environment.SignificantHeight
					: 0.21f * WindSpeed * WindSpeed / Mathf.Max(0.05f, Gravity);
			}
		}

		bool SurfaceWater.ISource.IsUnder(Vector3 point) => isActiveAndEnabled && IsUnderSurface(point);

		private WaterEnvironment environment;

		private void OnEnable()
		{
			meshFilter = GetComponent<MeshFilter>();
			meshRenderer = GetComponent<MeshRenderer>();
			Rebuild();
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;

			EnsureSpectrum();
			SurfaceWater.Register(this);
		}

		private void OnDisable()
		{
			SurfaceWater.Unregister(this);
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
			fft?.Dispose();
			fft = null;
			Discard(underwaterHost);
			Discard(underwaterMesh);
			Discard(underwaterMaterial);
			underwaterHost = null;
			underwaterMesh = null;
			underwaterMaterial = null;
			Discard(causticsHost);
			Discard(causticsMaterial);
			causticsHost = null;
			causticsMaterial = null;
			if (mesh != null)
			{
				// HideAndDontSave, so nothing else will ever clean it up.
				if (Application.isPlaying)
				{
					Destroy(mesh);
				}
				else
				{
					DestroyImmediate(mesh);
				}
				mesh = null;
			}
		}

		/// <summary>Destroys an object made here, in whichever way the current mode allows.</summary>
		private static void Discard(UnityEngine.Object victim)
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

		private void OnValidate()
		{
			OuterRadius = Mathf.Max(InnerRadius * 4f, OuterRadius);
			if (isActiveAndEnabled)
			{
				Rebuild();
			}
		}

		/// <summary>Rebuilds the sea state, and the mesh if its shape changed.</summary>
		public void Rebuild()
		{
			// Set before building: every wavelength and speed in the sea state comes off it.
			WaterWaves.Gravity = Mathf.Max(0.05f, Gravity);
			liveWaves = WaterWaves.Build(waves, WindDirectionDegrees, WindSpeed, Spread, Choppiness, WaveScale);
			liveWaves = Mathf.Min(liveWaves, Mathf.Clamp(WaveCount, 1, WaterWaves.MaximumWaves));

			float tallest = 0f;
			for (int i = 0; i < liveWaves; i++)
			{
				tallest += waves[i].Amplitude;
			}

			bool shapeChanged = builtRings != Rings || builtSegments != Segments
				|| !Mathf.Approximately(builtNear, NearRadius)
				|| !Mathf.Approximately(builtInner, InnerRadius) || !Mathf.Approximately(builtOuter, OuterRadius);
			if (mesh == null || shapeChanged)
			{
				/* Refilled in place, never destroyed and recreated. This runs from OnValidate, and
				 * Unity refuses DestroyImmediate there — measured, the old version logged that
				 * refusal on every inspector edit and every domain reload. */
				if (mesh == null)
				{
					mesh = WaterMesh.Build(InnerRadius, OuterRadius, Rings, Segments, tallest, NearRadius, NearRingFraction);
				}
				else
				{
					WaterMesh.Fill(mesh, InnerRadius, OuterRadius, Rings, Segments, tallest, NearRadius, NearRingFraction);
				}
				builtRings = Rings;
				builtSegments = Segments;
				builtNear = NearRadius;
				builtInner = InnerRadius;
				builtOuter = OuterRadius;
			}

			if (meshFilter == null)
			{
				meshFilter = GetComponent<MeshFilter>();
			}
			if (meshRenderer == null)
			{
				meshRenderer = GetComponent<MeshRenderer>();
			}
			/* Only when it is actually a different mesh. Assigning sharedMesh makes the filter
			 * message its renderer, and Unity logs "SendMessage cannot be called during OnValidate"
			 * for that — so re-assigning the same mesh on every validate is noise at best. */
			if (meshFilter.sharedMesh != mesh)
			{
				meshFilter.sharedMesh = mesh;
			}
			if (Material != null && meshRenderer.sharedMaterial != Material)
			{
				meshRenderer.sharedMaterial = Material;
			}
			/* Nothing the sea does belongs in a lightmap or a probe, and it must not cast: water
			 * that casts a shadow darkens the sea bed it exists to show through. */
			meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
			meshRenderer.receiveShadows = true;
			meshRenderer.lightProbeUsage = LightProbeUsage.Off;
			meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
			meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
		}

		/// <summary>
		/// Creates the transform once the compute shader is actually there.
		/// </summary>
		/// <remarks>
		/// Lazily, not in OnEnable, because a component's fields are assigned AFTER AddComponent
		/// returns — so anything that adds the water from script had a null shader at enable time
		/// and got a permanently flat sea with no error. The same applies to a designer dropping
		/// the asset into the field in the inspector.
		/// </remarks>
		private void EnsureSpectrum()
		{
			// Rebuilt when the development toggle is flipped, so the two paths can be compared live.
			if (fft != null && builtWithPasses != ForceRenderPasses)
			{
				fft.Dispose();
				fft = null;
			}
			if (fft != null)
			{
				return;
			}
			if (SpectrumPasses == null)
			{
				SpectrumPasses = Shader.Find(SpectrumPassesShader);
			}
			if (Spectrum == null && SpectrumPasses == null)
			{
				return;
			}
			fft = WaterFFT.Create(Spectrum, SpectrumPasses, ForceRenderPasses);
			builtWithPasses = ForceRenderPasses;
			if (fft == null)
			{
				if (!reportedSpectrum)
				{
					reportedSpectrum = true;
					Debug.LogWarning("[Water] This device can run the ocean's FFT neither as compute nor as render passes " +
						"(it needs two render targets and a float format it can render to), so the sea is flat.", this);
				}
				return;
			}
			builtWind = -1f;
		}

		private bool reportedSpectrum;

		private void Advance()
		{
			double now = Time.realtimeSinceStartupAsDouble;
			if (lastRealtime < 0.0)
			{
				lastRealtime = now;
			}
			// Clamped so a domain reload, a breakpoint or a long frame does not jump the sea; scaled
			// by the world's motion, so the sea stops when the world's time does — and never runs faster
			// than real time, however fast a preview runs the clock (WorldMotion).
			double delta = WorldMotion.Scale(Mathf.Clamp((float)(now - lastRealtime), 0f, 0.25f));
			lastRealtime = now;
			clock = (clock + delta) % WrapSeconds;
		}

		/// <summary>Drives the clock from somewhere else, for a scene whose time is not wall time.</summary>
		public void SetClock(double seconds)
		{
			clock = seconds % WrapSeconds;
			lastRealtime = Time.realtimeSinceStartupAsDouble;
		}

		private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (camera == null || mesh == null)
			{
				return;
			}
			// Reflection probe bakes and preview cameras would each drag the ocean to themselves
			// and leave it there for the camera that matters.
			if (camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.Preview)
			{
				return;
			}

			/* The weather's fog passes publish their fog for the water every camera they run for, and
			 * this runs before any of them. Cleared here, a pass that is switched off or missing from
			 * this renderer leaves the water unfogged rather than fogged by some other camera's. */
			Shader.SetGlobalVector(AirFogId, Vector4.zero);
			Shader.SetGlobalVector(AirFogVolumeId, Vector4.zero);

			Advance();
			ApplyQuality();
			UpdateUnderwater(camera);
			UpdateCaustics();

			/* Centred on the camera in XZ only, and NOT snapped to a grid. Snapping a radial mesh
			 * makes the whole ring pattern jump by a step; letting the vertices slide is invisible
			 * instead, because the surface they sample is smooth and stays put in world space. */
			Vector3 position = transform.position;
			transform.position = new Vector3(camera.transform.position.x, position.y, camera.transform.position.z);

			for (int i = 0; i < liveWaves; i++)
			{
				packed[i] = waves[i].Packed;
				packedMotion[i] = waves[i].PackedMotion;
			}
			for (int i = liveWaves; i < WaterWaves.MaximumWaves; i++)
			{
				packed[i] = Vector4.zero;
				packedMotion[i] = Vector4.zero;
			}

			Shader.SetGlobalVectorArray(WaveId, packed);
			Shader.SetGlobalVectorArray(MotionId, packedMotion);
			Shader.SetGlobalFloat(CountId, liveWaves);
			Shader.SetGlobalFloat(LevelId, position.y + TideMetres);
			// The level the shore field was built against, so the shore can follow the tide off it.
			Shader.SetGlobalFloat(MeanLevelId, position.y);
			Shader.SetGlobalFloat(TimeId, (float)clock);
			Shader.SetGlobalFloat(CloudShadowId, Mathf.Clamp01(CloudShadow));

			// The ripples run with the wind, so the fragment stage needs it too.
			float radians = WindDirectionDegrees * Mathf.Deg2Rad;
			Shader.SetGlobalVector(WindId, new Vector4(Mathf.Sin(radians), Mathf.Cos(radians), WindSpeed, 0f));
			Shader.SetGlobalFloat(GravityId, Mathf.Max(0.05f, Gravity));

			// Shared with the shore pass, so the beach foam and the sea foam are the same stuff.
			if (Material != null && Material.HasProperty("_FoamTexture"))
			{
				Texture foam = Material.GetTexture("_FoamTexture");
				if (foam != null)
				{
					Shader.SetGlobalTexture("_FishWaterFoamTexture", foam);
				}
			}
			// And its ripples, so the swash's thin sheet shimmers with the same ones.
			if (Material != null && Material.HasProperty("_NormalMap"))
			{
				Texture ripples = Material.GetTexture("_NormalMap");
				if (ripples != null)
				{
					Shader.SetGlobalTexture("_FishWaterNormalTexture", ripples);
				}
			}

			EnsureSpectrum();
			if (fft != null)
			{
				/* The static spectrum is the expensive half and depends on nothing that changes
				 * frame to frame, so it is rebuilt only when the sea state genuinely moves. */
				if (!Mathf.Approximately(builtWind, WindSpeed)
					|| !Mathf.Approximately(builtHeading, WindDirectionDegrees)
					|| !Mathf.Approximately(builtGravity, Gravity)
					|| !Mathf.Approximately(builtChoppiness, FFTChoppiness))
				{
					float windRadians = WindDirectionDegrees * Mathf.Deg2Rad;
					fft.SetSeaState(WindSpeed,
						new Vector2(Mathf.Sin(windRadians), Mathf.Cos(windRadians)),
						Gravity, fft.Amplitude, FFTChoppiness, LoopPeriod, 1u);
					builtWind = WindSpeed;
					builtHeading = WindDirectionDegrees;
					builtGravity = Gravity;
					builtChoppiness = FFTChoppiness;
				}

				fft.Evaluate((float)clock);
				for (int i = 0; i < WaterFFT.Cascades; i++)
				{
					Shader.SetGlobalTexture(DisplacementNames[i], fft.Displacement[i]);
					Shader.SetGlobalTexture(DerivativeNames[i], fft.Derivatives[i]);
				}
				Shader.SetGlobalVector(PatchId, new Vector4(
					fft.PatchMetres[0], fft.PatchMetres[1], fft.PatchMetres[2], WaveScale));
			}

			/* Past about 15 m/s a fully developed sea stops getting steeper, so the wave geometry
			 * stops producing more breaking crests while a real ocean goes on whitening. This is
			 * the part of white-capping that comes from the wind tearing the tops off rather than
			 * from the shape of the wave. */
			Shader.SetGlobalFloat(WhitecapId, Mathf.Clamp01((WindSpeed - 12f) / 13f));
		}

		/// <summary>
		/// Turns off anything the running pipeline cannot supply.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>This is the edge case that matters most in this project.</b> Of the three URP assets
		/// here, <c>URP-Performant</c> has <c>Depth Texture</c> off and ALL THREE have
		/// <c>Opaque Texture</c> off. Sampling a texture URP has not bound is not an error and does
		/// not go black — it returns whatever was last written to that slot, so the water would
		/// refract last frame's bloom buffer and foam in mid-air, on the quality tier the most
		/// players are on and on nobody's development machine.
		/// </para>
		/// <para>
		/// Global keywords rather than material ones, so nothing here writes into the material
		/// asset and leaves it modified in the working tree.
		/// </para>
		/// </remarks>
		private void ApplyQuality()
		{
			var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
			bool depth = DepthEffects && pipeline != null && pipeline.supportsCameraDepthTexture;
			bool refraction = Refraction && pipeline != null && pipeline.supportsCameraOpaqueTexture;

			SetKeyword(DepthKeyword, depth);
			SetKeyword(RefractionKeyword, refraction);

			if (!ReportQuality || reported || pipeline == null)
			{
				return;
			}
			if (DepthEffects && !depth)
			{
				Debug.LogWarning($"[Water] '{pipeline.name}' has Depth Texture off, so shore foam, soft edges and " +
					"depth absorption are disabled. Turn it on in the URP asset, or clear Depth Effects here to stop asking.", this);
			}
			if (Refraction && !refraction)
			{
				Debug.LogWarning($"[Water] '{pipeline.name}' has Opaque Texture off, so the water cannot refract what is " +
					"behind it and falls back to blending. Turn it on in the URP asset, or clear Refraction here to stop asking.", this);
			}
			reported = DepthEffects && !depth || Refraction && !refraction;
		}

		/// <summary>
		/// Shows or hides the full-screen underwater pass for the camera about to render.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A mesh in the transparent queue rather than a <c>ScriptableRendererFeature</c>, for one
		/// reason: a renderer feature has to be added to every URP Renderer asset in the project by
		/// hand, and there are three here. A plugin that only works after somebody edits three
		/// project assets is a plugin that is reported as broken.
		/// </para>
		/// <para>
		/// <b>Tested against the WAVE height, not the flat sea level.</b> A camera at the still
		/// level with a two-metre swell running is above the water in the troughs and under it at
		/// the crests, and a test against the flat plane pops the whole screen between the two as
		/// the swell passes.
		/// </para>
		/// </remarks>
		private void UpdateUnderwater(Camera camera)
		{
			bool wanted = UnderwaterEffect && liveWaves > 0 && IsUnderSurface(camera.transform.position);

			if (!wanted)
			{
				if (underwaterHost != null)
				{
					underwaterHost.SetActive(false);
				}
				return;
			}

			if (underwaterHost == null)
			{
				Shader shader = UnderwaterShader != null ? UnderwaterShader : Shader.Find(UnderwaterShaderName);
				if (shader == null)
				{
					UnderwaterEffect = false;
					Debug.LogWarning("[Water] 'FishMMO/Water/Underwater' is missing, so there is no underwater effect.", this);
					return;
				}
				underwaterMaterial = new Material(shader) { name = "Underwater", hideFlags = HideFlags.HideAndDontSave };

				EnsureScreenTriangle();

				underwaterHost = new GameObject("Underwater") { hideFlags = HideFlags.DontSave };
				underwaterHost.transform.SetParent(transform, false);
				underwaterHost.AddComponent<MeshFilter>().sharedMesh = underwaterMesh;
				underwaterRenderer = underwaterHost.AddComponent<MeshRenderer>();
				underwaterRenderer.sharedMaterial = underwaterMaterial;
				underwaterRenderer.shadowCastingMode = ShadowCastingMode.Off;
				underwaterRenderer.receiveShadows = false;
				underwaterRenderer.lightProbeUsage = LightProbeUsage.Off;
				underwaterRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
			}

			underwaterMaterial.SetColor(UnderwaterTintId, UnderwaterTint);
			underwaterMaterial.SetFloat(UnderwaterDepthId, UnderwaterVisibility);
			underwaterMaterial.SetFloat(UnderwaterShaftsId, UnderwaterShafts);
			underwaterMaterial.SetFloat(UnderwaterMotesId, UnderwaterMotes);
			underwaterHost.SetActive(true);
		}

		private static readonly int CausticsId = Shader.PropertyToID("_FishWaterCaustics");
		private static readonly int AirFogId = Shader.PropertyToID("_FishAirFogParams");
		private static readonly int AirFogVolumeId = Shader.PropertyToID("_FishAirFogVolumeRange");

		/// <summary>
		/// Keeps the caustics pass alive while there is a sea to cast them: it needs the FFT's slope
		/// maps, and nothing else — every pixel above the water it simply leaves alone.
		/// </summary>
		/// <remarks>
		/// A full-screen triangle on its own child, for the reason the underwater pass is one: a
		/// renderer feature would have to be added to every URP renderer asset by hand. It draws
		/// before the shore, the sea and the underwater pass, so they go over what it lights and the
		/// water's fog dims it with everything else. Water that refracts shows a copy of the frame
		/// taken before this pass runs, and lights the sea bed in that copy itself, with the same
		/// function, so the caustics look the same whether the opaque texture is on or not.
		/// </remarks>
		private void UpdateCaustics()
		{
			bool wanted = Caustics && fft != null;
			/* One global for both places the light is drawn — this pass, and the sea's refraction,
			 * which lights the sea bed it refracts itself. A strength of nothing switches both. */
			Shader.SetGlobalVector(CausticsId, new Vector4(wanted ? CausticsStrength : 0f, CausticsClarity, CausticsFadeDistance, 0f));
			if (!wanted)
			{
				if (causticsHost != null)
				{
					causticsHost.SetActive(false);
				}
				return;
			}
			if (causticsHost == null)
			{
				Shader shader = CausticsShader != null ? CausticsShader : Shader.Find(CausticsShaderName);
				if (shader == null || !shader.isSupported)
				{
					Caustics = false;
					Debug.LogWarning($"[Water] '{CausticsShaderName}' is missing or unsupported here, so there are no caustics.", this);
					return;
				}
				causticsMaterial = new Material(shader) { name = "Caustics", hideFlags = HideFlags.HideAndDontSave };
				EnsureScreenTriangle();

				causticsHost = new GameObject("Caustics") { hideFlags = HideFlags.DontSave };
				causticsHost.transform.SetParent(transform, false);
				causticsHost.AddComponent<MeshFilter>().sharedMesh = underwaterMesh;
				var renderer = causticsHost.AddComponent<MeshRenderer>();
				renderer.sharedMaterial = causticsMaterial;
				renderer.shadowCastingMode = ShadowCastingMode.Off;
				renderer.receiveShadows = false;
				renderer.lightProbeUsage = LightProbeUsage.Off;
				renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
			}
			causticsHost.SetActive(true);
		}

		/// <summary>The one full-screen triangle both projected passes draw with.</summary>
		private void EnsureScreenTriangle()
		{
			if (underwaterMesh != null)
			{
				return;
			}
			/* In clip coordinates. Its bounds are enormous because the vertex shaders ignore the
			 * transform entirely — culling would otherwise throw it away the moment its origin left
			 * the frustum. */
			underwaterMesh = new Mesh { name = "Screen triangle", hideFlags = HideFlags.HideAndDontSave };
			underwaterMesh.vertices = new[]
			{
				new Vector3(-1f, -1f, 0f),
				new Vector3(3f, -1f, 0f),
				new Vector3(-1f, 3f, 0f),
			};
			underwaterMesh.triangles = new[] { 0, 1, 2 };
			underwaterMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);
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

		private void OnDrawGizmosSelected()
		{
			Gizmos.color = new Color(0.25f, 0.7f, 1f, 0.8f);
			Vector3 centre = transform.position;
			const int Steps = 64;
			Vector3 previous = centre + new Vector3(OuterRadius, 0f, 0f);
			for (int i = 1; i <= Steps; i++)
			{
				float angle = i * Mathf.PI * 2f / Steps;
				Vector3 point = centre + new Vector3(Mathf.Cos(angle) * OuterRadius, 0f, Mathf.Sin(angle) * OuterRadius);
				Gizmos.DrawLine(previous, point);
				previous = point;
			}
			UnityEngine.Gizmos.color = new Color(0.25f, 0.7f, 1f, 0.35f);
			Gizmos.DrawLine(centre - Vector3.right * OuterRadius, centre + Vector3.right * OuterRadius);
			Gizmos.DrawLine(centre - Vector3.forward * OuterRadius, centre + Vector3.forward * OuterRadius);
		}
	}
}
