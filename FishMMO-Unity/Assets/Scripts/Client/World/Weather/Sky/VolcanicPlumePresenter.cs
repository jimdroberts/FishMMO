using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws the scene's volcanic plumes: an eruption column, its umbrella and the ash falling out of
	/// it where there is air; a ballistic fountain over each vent and lava lake where there is none.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The plume drawn is the plume the ash falls from.</b> With air, the plumes are
	/// <see cref="VolcanicVents.Plumes"/> asked exactly as <see cref="WeatherField.Sample"/> asks it —
	/// the same vents, the same eruption cells, the same planet and the same open-air wind — so the
	/// column leans and the umbrella streams the way the weather's fallout runs, and the ash the
	/// precipitation field draws round the viewer is this umbrella's.
	/// </para>
	/// <para>
	/// <b>Without air</b> nothing is buoyant and nothing is weather: each vent (the vented biomes',
	/// <see cref="VolcanicVents.Of"/>) and each lava lake (<see cref="VolcanicVents.LavaLakes"/>, under
	/// the scene's lava surface) is a fountain of grains on drag-free arcs, as tall as v²/2g on this
	/// world's gravity, landing in a ring round it with no drift. Client only: nothing about it is
	/// networked, and every client works out the same vents from the same map.
	/// </para>
	/// <para>
	/// Reuses the precipitation field's machinery rather than a renderer of its own kind: the same
	/// pre-built field of quads placed by the vertex shader, the same weather atlas (the ash row), and
	/// the same light (FishTrilight and the main light, as the curtains and particles are lit).
	/// </para>
	/// </remarks>
	public sealed class VolcanicPlumePresenter : MonoBehaviour, IWeatherPresenter
	{
		/// <summary>Quads in the field; each population draws a share of them.</summary>
		public const int Particles = 2048;
		/// <summary>The most plumes or fountains drawn at once, the nearest.</summary>
		public const int MaxDrawn = 4;
		/// <summary>The atlas row of soft ragged blobs (WeatherTextureBaker: ash and embers).</summary>
		public const int AshRow = 3;
		/// <summary>The shader's name, for a profile with no plume material (an editor preview).</summary>
		public const string ShaderName = "FishMMO/Weather/Volcanic Plume";
		/// <summary>
		/// The lava surface's shader, by name: the lava lives in the water plugin, which the client does
		/// not reference, and its level is all this needs.
		/// </summary>
		public const string LavaShaderName = "FishMMO/Water/Lava";
		/// <summary>What a lava lake's fountain is made of when nothing says otherwise: sulphur-dioxide frost, Io's white.</summary>
		public static readonly Color FrostTint = new Color(0.88f, 0.88f, 0.84f, 1f);
		/// <summary>Ash with no substance of its own.</summary>
		public static readonly Color AshTint = new Color(0.45f, 0.43f, 0.4f, 1f);

		private static readonly int VentId = Shader.PropertyToID("_PlumeVent");
		private static readonly int FormId = Shader.PropertyToID("_PlumeForm");
		private static readonly int RiseId = Shader.PropertyToID("_PlumeRise");
		private static readonly int LeanId = Shader.PropertyToID("_PlumeLean");
		private static readonly int DriftId = Shader.PropertyToID("_PlumeDrift");
		private static readonly int BallisticId = Shader.PropertyToID("_PlumeBallistic");
		private static readonly int ColorId = Shader.PropertyToID("_PlumeColor");
		private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
		private static readonly int PaceId = Shader.PropertyToID("_PlumePace");
		private static readonly int HeldId = Shader.PropertyToID("_PlumeHeld");

		/// <summary>
		/// The window a plume's weather-paced motions are held over, s: how long the puffs climbing its
		/// column, drifting in its umbrella and falling out of it keep one pace.
		/// </summary>
		/// <remarks>
		/// <para>
		/// How long a puff takes to climb, drift or fall is the weather's — the column's height, the wind,
		/// how fast the ash settles — and changes as the weather does. The shader turned the clock into
		/// where each puff is as clock ÷ period, so every change in the period moved every puff by the
		/// change times the whole clock: the plume scrubbed whenever the wind did.
		/// </para>
		/// <para>
		/// Instead the puffs are two layers of half the quads each, on windows half a window apart, both
		/// fixed on the shared clock. Each layer holds the period it began its window with
		/// (<see cref="WorldMotion.HeldValue"/>), runs its puffs on the time into its window, and fades in
		/// and out as sin² of that time — so it starts again, at the new pace, only where it is unseen,
		/// while the other carries the plume. The two weights add to one, so the plume never thins or
		/// pulses. Snapping the period to whole cycles of a window (<see cref="WorldMotion.WholeCycles"/>)
		/// cannot do it: the umbrella drifts and the ash falls over hours, longer than any window.
		/// </para>
		/// </remarks>
		public const double PaceWindowSeconds = 240.0;

		private WeatherPresentation presentation;
		private WeatherContext context;
		private bool hasContext;
		private Mesh mesh;
		private Material fallbackMaterial;
		// One copy of the plume material for each population drawn this camera, each with its own numbers: a property block
		// would take every one of these draws out of the SRP Batcher. drawSlot counts this camera's draws.
		private readonly DrawMaterials materials = new DrawMaterials("Plume");
		private int drawSlot;
		/// <summary>The shared clock, wrapped, for the fountains, whose pace is their vent's and constant.</summary>
		private float clock;
		/// <summary>Seconds into each layer's window this frame, and which window it is (see <see cref="PaceWindowSeconds"/>).</summary>
		private float intoA, intoB;
		private long windowA, windowB;
		/// <summary>The periods each plume's layers hold, by its vent: column, umbrella and fall, two layers each.</summary>
		private readonly Dictionary<Vector2Int, WorldMotion.HeldValue[]> held = new Dictionary<Vector2Int, WorldMotion.HeldValue[]>();
		private readonly List<VolcanicPlume.Plume> plumes = new List<VolcanicPlume.Plume>();
		private readonly List<(Vector3 vent, float emission, Color tint, float distance, float timed)> fountains = new List<(Vector3, float, Color, float, float)>();

		/// <summary>The motion clock and the sky the vents' timing reads this frame (2026-10-25, I2q-b; VentTiming).</summary>
		private VentSky ventSky;

		/// <summary>
		/// A scene vent's strength now on its biome's timing (2026-10-25, I2q-b; presentation only: the weather's plume and its fallout
		/// never read it), by the vent the plume stands on (its seed); 1 for a steady vent and for an eruption cell's plume.
		/// </summary>
		private float Timed(uint seed, bool fromCell)
		{
			if (fromCell)
			{
				return 1f;
			}
			foreach (VentSite vent in VolcanicVents.Of(context.Settings))
			{
				if (vent.Seed == seed)
				{
					return Timed(vent);
				}
			}
			return 1f;
		}

		private float Timed(in VentSite vent)
		{
			return vent.Timing == FishMMO.Shared.Biomes.BiomeVentTiming.Steady ? 1f
				: VentTiming.Strength(vent.Timing, ventSky, vent.TimingSeed, vent.Bearing, vent.Episodes);
		}

		// The lava lakes of the scene last asked about: found once, from its terrains.
		private int lakesScene = int.MinValue;
		private readonly List<Vector2> lakes = new List<Vector2>();
		private float lakeLevel;

		private void Awake()
		{
			presentation = GetComponent<WeatherPresentation>();
			WeatherClient.RegisterPresenter(this);
			RenderPipelineManager.beginCameraRendering += OnBeginCamera;
		}

		private void OnDestroy()
		{
			WeatherClient.UnregisterPresenter(this);
			RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
			if (mesh != null)
			{
				Destroy(mesh);
			}
			if (fallbackMaterial != null)
			{
				Destroy(fallbackMaterial);
			}
			materials.Dispose();
		}

		public void Apply(in WeatherFrame frame, in WeatherContext ctx)
		{
			context = ctx;
			hasContext = true;
		}

		public void Reset()
		{
			hasContext = false;
			held.Clear();
			lakesScene = int.MinValue;
			lakes.Clear();
		}

		private void Update()
		{
			/* On the world's shared motion clock, as what falls is: every player sees the same puff at the
			 * same moment, and a held clock holds the plumes. It was a private sum of this client's frame
			 * times from zero. */
			double seconds = WorldMotion.Seconds;
			clock = (float)WorldMotion.Repeat(seconds, WorldMotion.ShaderWrapSeconds);
			double half = 0.5 * PaceWindowSeconds;
			windowA = (long)System.Math.Floor(seconds / PaceWindowSeconds);
			windowB = (long)System.Math.Floor((seconds + half) / PaceWindowSeconds);
			intoA = (float)(seconds - windowA * PaceWindowSeconds);
			intoB = (float)(seconds + half - windowB * PaceWindowSeconds);
		}

		/// <summary>
		/// The periods a plume's two layers hold for a population this frame (seconds): each the period
		/// as it was when its layer's window began, or when this client first saw that window.
		/// </summary>
		private Vector4 Held(Vector2 vent, int population, float period)
		{
			var key = new Vector2Int(Mathf.RoundToInt(vent.x), Mathf.RoundToInt(vent.y));
			if (!held.TryGetValue(key, out WorldMotion.HeldValue[] periods))
			{
				if (held.Count > 64)
				{
					held.Clear();
				}
				periods = new WorldMotion.HeldValue[6];
				held[key] = periods;
			}
			float a = periods[population * 2].Hold(period, windowA);
			float b = periods[population * 2 + 1].Hold(period, windowB);
			return new Vector4(a, b, 0f, 0f);
		}

		private Material ResolveMaterial(WeatherRenderProfile profile)
		{
			if (profile != null && profile.PlumeMaterial != null)
			{
				return profile.PlumeMaterial;
			}
			if (fallbackMaterial == null)
			{
				Shader shader = Shader.Find(ShaderName);
				if (shader != null)
				{
					fallbackMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
				}
			}
			return fallbackMaterial;
		}

		// Nearest first, from the viewer. Sorted with these rather than lambdas capturing the viewer: a capturing lambda is a
		// closure and a delegate of garbage for every camera, every frame (the closure is made on entry, before any early return).
		private sealed class PlumesNearest : IComparer<VolcanicPlume.Plume>
		{
			public Vector2 From;
			public int Compare(VolcanicPlume.Plume a, VolcanicPlume.Plume b) => (a.Vent - From).sqrMagnitude.CompareTo((b.Vent - From).sqrMagnitude);
		}

		private sealed class CurtainsNearest : IComparer<LavaCurtains.Curtain>
		{
			public Vector2 From;
			public int Compare(LavaCurtains.Curtain a, LavaCurtains.Curtain b) => (a.Middle - From).sqrMagnitude.CompareTo((b.Middle - From).sqrMagnitude);
		}

		private readonly PlumesNearest plumesByDistance = new PlumesNearest();
		private readonly CurtainsNearest curtainsByDistance = new CurtainsNearest();
		/// <summary><see cref="CurtainSchedule"/> as a delegate, made once (a method group converts to a new delegate on every call).</summary>
		private System.Func<int, FishMMO.Shared.Biomes.BiomeFountainEpisodes> curtainScheduleOf;

		/// <summary>This hook's own time in the profiler (render-pipeline callbacks otherwise show only as
		/// RenderPipeline.BeginCameraRendering's self time). In a class of its own so the marker, an engine object, is made
		/// when the hook first runs, not when the type is first touched (tests outside Unity use these types' pure parts).</summary>
		private static class HookProfiling
		{
			public static readonly Unity.Profiling.ProfilerMarker Marker = new Unity.Profiling.ProfilerMarker("VolcanicPlumePresenter.BeginCamera");
		}

		private void OnBeginCamera(ScriptableRenderContext renderContext, Camera camera)
		{
			using var hookMarkerScope = HookProfiling.Marker.Auto();
			if (!hasContext || camera == null || context.Timeline == null || context.Timeline.SceneMode != WeatherSceneMode.Own)
			{
				return;
			}
			Camera wanted = presentation != null && presentation.TargetCamera != null ? presentation.TargetCamera : Camera.main;
			if (camera != wanted)
			{
				return;
			}
			WeatherRenderProfile profile = presentation != null ? presentation.Profile : WeatherRenderProfile.Active;
			Material material = ResolveMaterial(profile);
			if (material == null)
			{
				return;
			}
			if (mesh == null)
			{
				mesh = BuildMesh(Particles);
			}
			Vector3 viewer = camera.transform.position;
			var viewer2 = new Vector2(viewer.x, viewer.z);
			float reach = camera.farClipPlane * 1.5f + 20000f;
			ventSky = VentSky.Of(WorldMotion.Seconds, GlowSky.Current);
			drawSlot = 0;
			// Curtains of fire along the scene's fissures (2026-10-25, I2r): lava, so drawn with air or without.
			DrawCurtains(camera, material, profile, viewer2, reach);

			WeatherSample sample = context.Sample;
			if (VolcanicPlume.CanRise(sample.Planet))
			{
				// The weather's plumes, each leaning on the wind where it stands: the same plume for every
				// player. Leaning on the viewer's wind, as the weather at the viewer does, it leaned
				// differently for each.
				VolcanicVents.PlumesInTheirOwnWind(context.Timeline, context.Settings, (uint)context.Tick, sample.Planet, viewer2, plumes);
				plumesByDistance.From = viewer2;
				plumes.Sort(plumesByDistance);
				int drawn = 0;
				for (int i = 0; i < plumes.Count && drawn < MaxDrawn; i++)
				{
					if ((plumes[i].Vent - viewer2).magnitude - VolcanicPlume.FalloutLength(plumes[i]) > reach)
					{
						continue;
					}
					DrawPlume(plumes[i], camera, material, profile);
					drawn++;
				}
				return;
			}

			WorldBody body = context.Timeline.BodyOverride != null ? context.Timeline.BodyOverride : SceneTime.BodyOf(context.Settings);
			float gravity = SurfacePhysics.Gravity(body);
			PlanetAir planet = PlanetAir.For(SolarSystemProfile.Active, body);
			if (body == null || !VolcanicPlume.IsFountain(planet, gravity))
			{
				return;
			}
			GatherFountains(viewer2, reach);
			for (int i = 0; i < fountains.Count && i < MaxDrawn; i++)
			{
				if (fountains[i].timed > 0.01f)
				{
					DrawFountain(fountains[i].vent, fountains[i].emission, fountains[i].tint, gravity, camera, material, profile, fountains[i].timed);
				}
			}
		}

		// ── With air: column, umbrella and fall ─────────────────────────

		private void DrawPlume(in VolcanicPlume.Plume plume, Camera camera, Material material, WeatherRenderProfile profile)
		{
			float ground = GroundAt(context.Scene, plume.Vent);
			var vent = new Vector3(plume.Vent.x, ground, plume.Vent.y);
			float u = plume.Wind.magnitude;
			Vector2 down = u >= VolcanicPlume.CalmWind ? plume.Wind / u : Vector2.zero;
			float length = VolcanicPlume.FalloutLength(plume);
			// The umbrella is drawn as far downwind as it stays worth seeing: three of its thinning
			// lengths, and never more than the sky draws to.
			float drawn = Mathf.Min(3f * length, 40000f);
			float riseSeconds = plume.Top / Mathf.Max(1f, VolcanicPlume.RiseSpeed(plume.Top, GravityOf(context.Sample.Planet)));
			float driftSeconds = drawn / Mathf.Max(1f, u);
			float fallSeconds = plume.Umbrella / Mathf.Max(0.05f, plume.FallSpeed);
			Color tint = plume.Substance != null ? plume.Substance.Tint : AshTint;
			float glow = plume.Substance != null ? plume.Substance.Emission : 0f;
			// Its vent's timing (2026-10-25, I2q-b): the plume tide, the sun, bursts, episodes, the kneading; steady as before.
			float timed = Timed(plume.Seed, plume.FromCell);
			if (timed <= 0.01f)
			{
				return;
			}
			float strength = (0.4f + 0.6f * plume.Emission) * timed;
			// In thin air it rises on its heat and lies over downwind (decision 53: Triton's); elsewhere its own lean, as before.
			Vector2 topOffset = VolcanicPlume.DrawnTopOffset(plume, context.Sample.Planet.AirRelative);

			var rise = new Vector4(plume.Top, plume.Umbrella, plume.Radius, riseSeconds);
			var lean = new Vector4(topOffset.x, topOffset.y, down.x, down.y);
			var drift = new Vector4(drawn, driftSeconds, VolcanicPlume.SpreadPerMetre, length);
			var colour = new Color(tint.r, tint.g, tint.b, 0.6f + glow);

			// The box round all of it: the vent, the top, and the umbrella out to where it is drawn.
			float wide = plume.Radius + VolcanicPlume.SpreadPerMetre * drawn;
			Vector2 far = plume.Vent + topOffset + down * drawn;
			var min = new Vector3(Mathf.Min(plume.Vent.x, far.x) - wide, ground - 50f, Mathf.Min(plume.Vent.y, far.y) - wide);
			var max = new Vector3(Mathf.Max(plume.Vent.x, far.x) + wide, ground + plume.Top * 1.3f + 200f, Mathf.Max(plume.Vent.y, far.y) + wide);
			var bounds = new Bounds((min + max) * 0.5f, max - min);

			Draw(material, profile, camera, bounds, vent, 0, 0.35f, 0.5f * strength, rise, lean, drift, Vector4.zero, colour, Held(plume.Vent, 0, riseSeconds));
			Draw(material, profile, camera, bounds, vent, 1, 0.65f, 0.35f * strength, rise, lean, drift, Vector4.zero, colour, Held(plume.Vent, 1, driftSeconds));
			Draw(material, profile, camera, bounds, vent, 2, 0.4f, 0.12f * strength, rise, lean, drift, new Vector4(fallSeconds, 0f, 0f, 0f), colour,
				Held(plume.Vent, 2, fallSeconds));
		}

		private static float GravityOf(in PlanetAir planet) => planet.Gravity > 0f ? planet.Gravity : SurfacePhysics.EarthGravity;

		// ── Without air: fountains ──────────────────────────────────────

		private void GatherFountains(Vector2 viewer, float reach)
		{
			fountains.Clear();
			foreach (VentSite vent in VolcanicVents.Of(context.Settings))
			{
				float d = (vent.Position - viewer).magnitude;
				if (d <= reach)
				{
					float ground = GroundAt(context.Scene, vent.Position);
					Color tint = vent.Substance != null ? vent.Substance.Tint : FrostTint;
					fountains.Add((new Vector3(vent.Position.x, ground, vent.Position.y), vent.Emission, tint, d, Timed(vent)));
				}
			}
			FindLakes(context.Scene);
			foreach (Vector2 lake in lakes)
			{
				float d = (lake - viewer).magnitude;
				if (d <= reach)
				{
					// Over the lake's surface; a lake is a vent of no particular strength.
					fountains.Add((new Vector3(lake.x, lakeLevel, lake.y), 0.08f, FrostTint, d, 1f));
				}
			}
			fountains.Sort((a, b) => a.distance.CompareTo(b.distance));
		}

		private void DrawFountain(Vector3 vent, float emission, Color tint, float gravity, Camera camera, Material material, WeatherRenderProfile profile, float timed = 1f)
		{
			float speed = VolcanicPlume.LaunchSpeed(emission);
			float height = VolcanicPlume.FountainHeight(speed, gravity);
			VolcanicPlume.FalloutRing(speed, gravity, out _, out float outer);
			float puff = Mathf.Clamp(height / 30f, 4f, 40f);
			var bounds = new Bounds(vent + Vector3.up * (0.5f * height), new Vector3(2f * outer + 2f * puff, height + 2f * puff, 2f * outer + 2f * puff));
			var ballistic = new Vector4(speed, gravity, VolcanicPlume.FountainConeDegrees * Mathf.Deg2Rad, VolcanicPlume.FountainInnerShare);
			// Its vent's timing thins it (2026-10-25, I2q-b): the height stays its emission's, the grains fewer and fainter.
			Draw(material, profile, camera, bounds, vent, 3, timed >= 1f ? 1f : Mathf.Max(0.1f, timed), 0.14f * timed, new Vector4(puff, 0f, 0f, 0f), Vector4.zero, Vector4.zero, ballistic,
				new Color(tint.r, tint.g, tint.b, 0.4f), Vector4.zero);
		}

		// ── Curtains of fire (2026-10-25, phase I2r, decision 65 (d)) ──

		/// <summary>The most curtain stretches drawn at once, the nearest.</summary>
		public const int MaxCurtains = 6;
		/// <summary>A curtain's clots, m, and how far either side of the vertical they are thrown across the fissure.</summary>
		public const float CurtainClotMetres = 1.2f, CurtainTiltDegrees = 8f;
		/// <summary>What a curtain's clots are made of under the light (cooling basalt spatter) and how strongly they glow.</summary>
		public static readonly Color CurtainTint = new Color(0.12f, 0.07f, 0.05f, 2.4f);

		private readonly List<LavaCurtains.Curtain> curtains = new List<LavaCurtains.Curtain>();
		private readonly Dictionary<int, FishMMO.Shared.Biomes.BiomeFountainEpisodes> curtainSchedules = new Dictionary<int, FishMMO.Shared.Biomes.BiomeFountainEpisodes>();

		/// <summary>A biome's fountain episodes by its template id (empty for none), read live from its template.</summary>
		private FishMMO.Shared.Biomes.BiomeFountainEpisodes CurtainSchedule(int biomeID)
		{
			if (!curtainSchedules.TryGetValue(biomeID, out FishMMO.Shared.Biomes.BiomeFountainEpisodes schedule))
			{
				schedule = FishMMO.Shared.Biomes.BiomeRegistry.TryGetByID(biomeID, out FishMMO.Shared.Biomes.BiomeTemplate biome) && biome != null
					? biome.FountainEpisodes : default;
				if (curtainSchedules.Count > 64)
				{
					curtainSchedules.Clear();
				}
				curtainSchedules[biomeID] = schedule;
			}
			return schedule;
		}

		/// <summary>
		/// The curtains of fire standing now along the scene's stored fissure lines (<see cref="LavaCurtains"/>, on the shared clock), the
		/// nearest <see cref="MaxCurtains"/>. Nothing unless a biome on a fissure line names fountain episodes.
		/// </summary>
		private void DrawCurtains(Camera camera, Material material, WeatherRenderProfile profile, Vector2 viewer, float reach)
		{
			FishMMO.Shared.Biomes.SceneBiomeMap map = context.Settings != null ? context.Settings.BiomeMap : null;
			if (map == null || map.VentLines == null || map.VentLines.Count == 0)
			{
				return;
			}
			curtainScheduleOf ??= CurtainSchedule;
			LavaCurtains.Of(map.VentLines, curtainScheduleOf, clock, curtains);
			if (curtains.Count == 0)
			{
				return;
			}
			curtainsByDistance.From = viewer;
			curtains.Sort(curtainsByDistance);
			WorldBody body = context.Timeline.BodyOverride != null ? context.Timeline.BodyOverride : SceneTime.BodyOf(context.Settings);
			float gravity = body != null ? SurfacePhysics.Gravity(body) : 9.81f;
			gravity = gravity > 0.01f ? gravity : 9.81f;
			for (int i = 0; i < curtains.Count && i < MaxCurtains; i++)
			{
				LavaCurtains.Curtain c = curtains[i];
				if ((c.Middle - viewer).magnitude > reach)
				{
					break;
				}
				// Its biome's schedule: the clots' period is the schedule's, fixed.
				FishMMO.Shared.Biomes.BiomeFountainEpisodes schedule = CurtainSchedule(c.BiomeID);
				float a = GroundAt(context.Scene, c.A), b = GroundAt(context.Scene, c.B);
				var start = new Vector3(c.A.x, a, c.A.y);
				Vector2 run = c.B - c.A;
				float speed = LavaCurtains.LaunchSpeed(c.Height, gravity);
				float period = LavaCurtains.FlightPeriod(in schedule, gravity);
				var bounds = new Bounds(new Vector3(0.5f * (c.A.x + c.B.x), 0.5f * (a + b) + 0.5f * c.Height, 0.5f * (c.A.y + c.B.y)),
					new Vector3(Mathf.Abs(run.x) + c.Height + 20f, c.Height + 20f, Mathf.Abs(run.y) + c.Height + 20f));
				var ballistic = new Vector4(speed, gravity, CurtainTiltDegrees * Mathf.Deg2Rad, period);
				Draw(material, profile, camera, bounds, start, 7, Mathf.Clamp(0.15f + 0.85f * c.Strength, 0.1f, 1f), 0.9f,
					new Vector4(CurtainClotMetres, 0f, 0f, 0f), new Vector4(run.x, run.y, b - a, 0f), Vector4.zero, ballistic, CurtainTint, Vector4.zero);
			}
		}

		/// <summary>The lava lakes under the scene's lava surface, found once per scene.</summary>
		private void FindLakes(Scene scene)
		{
			if (!scene.IsValid() || lakesScene == scene.handle)
			{
				return;
			}
			lakesScene = scene.handle;
			lakes.Clear();
			if (!LavaLevel(scene, out lakeLevel))
			{
				return;
			}
			var terrains = new List<Terrain>();
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				if (terrain != null && terrain.gameObject.scene == scene && terrain.terrainData != null)
				{
					terrains.Add(terrain);
				}
			}
			if (terrains.Count == 0)
			{
				return;
			}
			Rect area = AreaOf(terrains);
			VolcanicVents.LavaLakes(area, lakeLevel, p => HeightOn(terrains, p), lakes);
		}

		/// <summary>The scene's lava surface's level, from the renderer drawing the lava material.</summary>
		private static bool LavaLevel(Scene scene, out float level)
		{
			level = 0f;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
				{
					Material m = renderer.sharedMaterial;
					if (m != null && m.shader != null && m.shader.name == LavaShaderName)
					{
						level = renderer.transform.position.y;
						return true;
					}
				}
			}
			return false;
		}

		private static Rect AreaOf(List<Terrain> terrains)
		{
			Vector3 min = terrains[0].GetPosition(), max = min + terrains[0].terrainData.size;
			foreach (Terrain t in terrains)
			{
				min = Vector3.Min(min, t.GetPosition());
				max = Vector3.Max(max, t.GetPosition() + t.terrainData.size);
			}
			return Rect.MinMaxRect(min.x, min.z, max.x, max.z);
		}

		private static float HeightOn(List<Terrain> terrains, Vector2 p)
		{
			foreach (Terrain t in terrains)
			{
				Vector3 o = t.GetPosition();
				Vector3 s = t.terrainData.size;
				if (p.x >= o.x && p.x <= o.x + s.x && p.y >= o.z && p.y <= o.z + s.z)
				{
					return t.SampleHeight(new Vector3(p.x, 0f, p.y)) + o.y;
				}
			}
			return float.NaN;
		}

		/// <summary>Reused for <see cref="Terrain.GetActiveTerrains"/>: <c>Terrain.activeTerrains</c> builds a new array on
		/// every read, and this runs every frame.</summary>
		private static readonly System.Collections.Generic.List<Terrain> activeTerrainsScratch = new System.Collections.Generic.List<Terrain>();

		private static float GroundAt(Scene scene, Vector2 p)
		{
			Terrain.GetActiveTerrains(activeTerrainsScratch);
			foreach (Terrain t in activeTerrainsScratch)
			{
				if (t == null || t.terrainData == null || (scene.IsValid() && t.gameObject.scene != scene))
				{
					continue;
				}
				Vector3 o = t.GetPosition();
				Vector3 s = t.terrainData.size;
				if (p.x >= o.x && p.x <= o.x + s.x && p.y >= o.z && p.y <= o.z + s.z)
				{
					return t.SampleHeight(new Vector3(p.x, 0f, p.y)) + o.y;
				}
			}
			return 0f;
		}

		// ── Drawing ─────────────────────────────────────────────────────

		private void Draw(Material material, WeatherRenderProfile profile, Camera camera, Bounds bounds, Vector3 vent, int population, float share, float opacity,
			Vector4 rise, Vector4 lean, Vector4 drift, Vector4 ballistic, Color colour, Vector4 heldPeriods)
		{
			Material copy = materials.For(material, drawSlot++);
			// The material's own texture when the profile has no atlas, as an emptied block left it.
			copy.SetTexture(MainTexId, profile != null && profile.PrecipitationAtlas != null ? profile.PrecipitationAtlas : material.GetTexture(MainTexId));
			copy.SetVector(VentId, new Vector4(vent.x, vent.y, vent.z, clock));
			copy.SetVector(FormId, new Vector4(population, share, opacity, AshRow));
			copy.SetVector(RiseId, rise);
			copy.SetVector(LeanId, lean);
			copy.SetVector(DriftId, drift);
			copy.SetVector(BallisticId, ballistic);
			copy.SetColor(ColorId, colour);
			copy.SetVector(PaceId, new Vector4(intoA, intoB, (float)PaceWindowSeconds, 0f));
			copy.SetVector(HeldId, heldPeriods);
			var rp = new RenderParams(copy)
			{
				camera = camera,
				worldBounds = bounds,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = camera.gameObject.layer,
			};
			Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
		}

		/// <summary>A field of quads: per vertex its corner, and per quad four uniform numbers, the same for each corner.</summary>
		public static Mesh BuildMesh(int particles)
		{
			var random = new System.Random(1883);
			var positions = new Vector3[particles * 4];
			var corners = new Vector2[particles * 4];
			var randoms = new Vector4[particles * 4];
			var indices = new int[particles * 6];
			for (int i = 0; i < particles; i++)
			{
				var r = new Vector4((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
				for (int c = 0; c < 4; c++)
				{
					int v = i * 4 + c;
					corners[v] = new Vector2(c == 1 || c == 2 ? 1f : 0f, c >= 2 ? 1f : 0f);
					randoms[v] = r;
				}
				int k = i * 6, b = i * 4;
				indices[k] = b; indices[k + 1] = b + 1; indices[k + 2] = b + 2;
				indices[k + 3] = b; indices[k + 4] = b + 2; indices[k + 5] = b + 3;
			}
			var mesh = new Mesh { name = "Volcanic Plume Particles", hideFlags = HideFlags.HideAndDontSave };
			mesh.indexFormat = particles * 4 > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			mesh.SetVertices(positions);
			mesh.SetUVs(0, corners);
			mesh.SetUVs(1, randoms);
			mesh.SetIndices(indices, MeshTopology.Triangles, 0);
			// Placed by the shader, so the mesh's own bounds mean nothing: the draw's are used.
			mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
			return mesh;
		}
	}
}
