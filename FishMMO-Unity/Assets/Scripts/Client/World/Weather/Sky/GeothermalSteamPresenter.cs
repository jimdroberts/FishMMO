using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws the steam off a scene's hot ground: each fumarole's plume, each hot spring's smoking pool, and
	/// each geyser — a faint wisp while it waits, a column of water and steam while it plays, the steam
	/// drifting off after it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where and when</b> are <see cref="GeothermalVents"/>': the sites from the scene's biome map, the
	/// eruptions from the shared world clock (<see cref="WorldMotion.Seconds"/>), so every player sees the
	/// same geyser go off at the same moment and a late joiner finds it mid-play. Nothing is networked.
	/// </para>
	/// <para>
	/// <b>How much steam</b> is <see cref="SteamPhysics"/>': a plume is visible until dilution has evaporated
	/// it, and how much dilution that takes is set by how cold and how dry the air is. So the same vent is a
	/// wisp a few metres tall on a dry summer afternoon and a column a hundred metres tall on a winter
	/// morning, and it leans over in the wind by its own rise against the wind's speed. The air is read at
	/// each site's own half-kilometre block every <see cref="AirSeconds"/> of world time
	/// (<see cref="WeatherField.SampleAtSeconds"/>), never at the camera, so two players see the same plume.
	/// </para>
	/// <para>
	/// Reuses the volcanic plume's machinery: its shader (populations 4 and 5 of FishVolcanicPlume.shader),
	/// its material on the render profile, its kind of field of quads, and its two-layer pace on the shared clock
	/// (<see cref="VolcanicPlumePresenter.PaceWindowSeconds"/>), so a plume whose visible length changes with
	/// the weather never scrubs. No particle system, no compute: WebGL2 runs it.
	/// </para>
	/// </remarks>
	public sealed class GeothermalSteamPresenter : MonoBehaviour, IWeatherPresenter
	{
		/// <summary>
		/// Quads in this presenter's field: a plume draws a share of them. A quarter of the volcanic field — no
		/// steam plume wants more than a few hundred puffs, and every draw runs the vertex shader over the whole
		/// field (the undrawn quads collapse to a point), so the smaller field is four times cheaper a draw.
		/// </summary>
		public const int Particles = 512;
		/// <summary>The most sites drawn at once, the nearest.</summary>
		public const int MaxDrawn = 24;
		/// <summary>Sites further than this from the camera are not drawn, m.</summary>
		public const float Reach = 1500f;
		/// <summary>How far the camera may move before the sites round it are searched again, m.</summary>
		private const float Refind = 60f;
		/// <summary>World seconds between readings of the air over a site.</summary>
		public const double AirSeconds = 5.0;
		/// <summary>The air is read once per block this wide, m: every site in it leans on the same wind.</summary>
		public const float AirBlockMetres = 500f;
		/// <summary>The jet's half-angle, rad: a geyser's column is a narrow cone.</summary>
		private const float JetHalfAngle = 0.07f;
		/// <summary>How fast steam widens per metre along it: a top-hat buoyant plume's 6/5 α with α ≈ 0.1 (Morton, Taylor and Turner).</summary>
		private const float PlumeSpread = 0.12f;
		/// <summary>A pool's steam spreads faster: it rises slowly and is torn sideways by the lightest air.</summary>
		private const float PoolSpread = 0.2f;
		/// <summary>Steam's own colour when the ground names no substance: a cloud's white.</summary>
		public static readonly Color SteamWhite = new Color(0.96f, 0.97f, 0.98f, 1f);
		/// <summary>A geyser's water: white with spray.</summary>
		public static readonly Color WaterWhite = new Color(0.9f, 0.93f, 0.95f, 1f);

		private const int SteamPopulation = 4;
		private const int WaterPopulation = 5;

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

		/// <summary>The air over one block, as last read.</summary>
		private struct Air
		{
			public float AirC, DewPointC, BoilingC;
			public Vector2 Wind;
		}

		/// <summary>A site found round the camera, with the ground it stands on.</summary>
		private struct Found
		{
			public GeothermalSite Site;
			public float Ground;
			public float Distance;
		}

		/// <summary>A site's visible steam lengths in the air last read: at rest, and erupting (a geyser's flash steam).</summary>
		private struct Lengths
		{
			public double At;
			public float Quiet, Erupting;
		}

		private WeatherPresentation presentation;
		private WeatherContext context;
		private bool hasContext;
		private Mesh mesh;
		private Material fallbackMaterial;
		private MaterialPropertyBlock block;
		private float intoA, intoB;
		private long windowA, windowB;
		private double motionSeconds;

		private readonly List<GeothermalSite> sites = new List<GeothermalSite>();
		private readonly List<Found> found = new List<Found>();
		private Vector2 foundAt = new Vector2(float.MaxValue, float.MaxValue);
		private int foundScene = int.MinValue;
		private float foundTime = float.NegativeInfinity;
		/// <summary>How often an empty search is tried again, real seconds.</summary>
		private const float RetrySeconds = 2f;
		private readonly Dictionary<Vector2Int, Air> airByBlock = new Dictionary<Vector2Int, Air>();
		private double airAt = double.NaN;
		private readonly Dictionary<uint, Lengths> lengths = new Dictionary<uint, Lengths>();
		private readonly Dictionary<uint, WorldMotion.HeldValue[]> held = new Dictionary<uint, WorldMotion.HeldValue[]>();
		private static readonly System.Comparison<Found> Nearest = (a, b) => a.Distance.CompareTo(b.Distance);

		private void Awake()
		{
			presentation = GetComponent<WeatherPresentation>();
			block = new MaterialPropertyBlock();
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
			lengths.Clear();
			airByBlock.Clear();
			found.Clear();
			foundScene = int.MinValue;
		}

		private void Update()
		{
			// The shared motion clock, as the volcanic plumes run on: the same puff and the same eruption on
			// every player's screen.
			motionSeconds = WorldMotion.Seconds;
			double window = VolcanicPlumePresenter.PaceWindowSeconds;
			double half = 0.5 * window;
			windowA = (long)System.Math.Floor(motionSeconds / window);
			windowB = (long)System.Math.Floor((motionSeconds + half) / window);
			intoA = (float)(motionSeconds - windowA * window);
			intoB = (float)(motionSeconds + half - windowB * window);
		}

		private Material ResolveMaterial(WeatherRenderProfile profile)
		{
			if (profile != null && profile.PlumeMaterial != null)
			{
				return profile.PlumeMaterial;
			}
			if (fallbackMaterial == null)
			{
				Shader shader = Shader.Find(VolcanicPlumePresenter.ShaderName);
				if (shader != null)
				{
					fallbackMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
				}
			}
			return fallbackMaterial;
		}

		private void OnBeginCamera(ScriptableRenderContext renderContext, Camera camera)
		{
			if (!hasContext || camera == null || context.Timeline == null || context.Timeline.SceneMode != WeatherSceneMode.Own)
			{
				return;
			}
			Camera wanted = presentation != null && presentation.TargetCamera != null ? presentation.TargetCamera : Camera.main;
			if (camera != wanted)
			{
				return;
			}
			PlanetAir planet = context.Sample.Planet;
			// Steam is buoyant in air and seen by the air it mixes into: with none, nothing hangs.
			if (!VolcanicPlume.CanRise(planet))
			{
				return;
			}
			Vector3 viewer = camera.transform.position;
			var viewer2 = new Vector2(viewer.x, viewer.z);
			FindSites(viewer2);
			if (found.Count == 0)
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
				mesh = VolcanicPlumePresenter.BuildMesh(Particles);
			}

			double airTime = System.Math.Floor(context.Timeline.WorldSecondsAt((uint)context.Tick) / AirSeconds) * AirSeconds;
			if (airTime != airAt)
			{
				airAt = airTime;
				airByBlock.Clear();
			}
			float gravity = planet.Gravity > 0f ? planet.Gravity : SurfacePhysics.EarthGravity;
			for (int i = 0; i < found.Count; i++)
			{
				Found f = found[i];
				f.Distance = (f.Site.Position - viewer2).magnitude;
				found[i] = f;
			}
			found.Sort(Nearest);
			for (int i = 0, drawn = 0; i < found.Count && drawn < MaxDrawn; i++)
			{
				if (found[i].Distance > Reach)
				{
					break;
				}
				DrawSite(found[i], AirAt(found[i], planet), gravity, camera, material, profile);
				drawn++;
			}
		}

		/// <summary>The sites round the camera, and the ground each stands on: again whenever the camera has moved far enough.</summary>
		private void FindSites(Vector2 viewer)
		{
			int scene = context.Scene.IsValid() ? context.Scene.handle : int.MinValue;
			// Searched again when the camera has moved, and now and then while nothing was found: asked before
			// the biomes or the terrain have loaded, every candidate reads as plain ground.
			bool retry = found.Count == 0 && Time.unscaledTime - foundTime > RetrySeconds;
			if (scene == foundScene && (viewer - foundAt).sqrMagnitude < Refind * Refind && !retry)
			{
				return;
			}
			foundScene = scene;
			foundAt = viewer;
			foundTime = Time.unscaledTime;
			GeothermalVents.Near(context.Settings, viewer, Reach + Refind, sites);
			found.Clear();
			TerrainSet terrains = TerrainSet.Current;
			bool sea = SurfaceWater.TryGetLevel(out float seaLevel);
			foreach (GeothermalSite site in sites)
			{
				if (!terrains.TryHeight(site.Position.x, site.Position.y, out float ground))
				{
					continue;
				}
				// A vent under the sea or a lake puts its heat into the water, not the air.
				if ((sea && ground < seaLevel) || (SurfaceWater.TryGetInlandSurfaceAt(site.Position.x, site.Position.y, out float lake) && lake > ground + 0.3f))
				{
					continue;
				}
				found.Add(new Found { Site = site, Ground = ground });
			}
		}

		/// <summary>The air over a site's block at the last whole <see cref="AirSeconds"/> of world time: the same air for every player.</summary>
		private Air AirAt(in Found site, in PlanetAir planet)
		{
			var key = new Vector2Int(Mathf.FloorToInt(site.Site.Position.x / AirBlockMetres), Mathf.FloorToInt(site.Site.Position.y / AirBlockMetres));
			if (airByBlock.TryGetValue(key, out Air air))
			{
				return air;
			}
			var centre = new Vector3((key.x + 0.5f) * AirBlockMetres, site.Ground, (key.y + 0.5f) * AirBlockMetres);
			WeatherSample sample = WeatherField.SampleAtSeconds(context.Timeline, context.Settings, context.Scene, centre, airAt);
			float scaleHeight = Mathf.Max(1000f, planet.ScaleHeight);
			float pressure = planet.SurfacePressure * Mathf.Exp(-Mathf.Max(0f, site.Ground) / scaleHeight);
			air = new Air
			{
				AirC = sample.Column.SurfaceKelvin - 273.15f,
				DewPointC = Mathf.Min(sample.Column.SurfaceKelvin, sample.Column.DewPointKelvin) - 273.15f,
				BoilingC = SteamPhysics.BoilingC(pressure),
				Wind = sample.Air.Wind,
			};
			airByBlock[key] = air;
			return air;
		}

		/// <summary>A site's visible steam lengths in this air, worked out once per reading of it.</summary>
		private Lengths LengthsOf(in GeothermalSite site, in Air air)
		{
			if (lengths.TryGetValue(site.Seed, out Lengths known) && known.At == airAt)
			{
				return known;
			}
			if (lengths.Count > 512)
			{
				lengths.Clear();
			}
			float source = GeothermalVents.SourceC(site, air.BoilingC);
			var result = new Lengths
			{
				At = airAt,
				Quiet = SteamPhysics.VisibleLength(GeothermalVents.VirtualSource(site), source, air.AirC, air.DewPointC),
				Erupting = site.Kind == GeothermalKind.Geyser
					? SteamPhysics.VisibleLength(GeothermalVents.VirtualSource(site, true), air.BoilingC, air.AirC, air.DewPointC)
					: 0f,
			};
			lengths[site.Seed] = result;
			return result;
		}

		private void DrawSite(in Found here, in Air air, float gravity, Camera camera, Material material, WeatherRenderProfile profile)
		{
			GeothermalSite site = here.Site;
			var vent = new Vector3(site.Position.x, here.Ground, site.Position.y);
			Lengths visible = LengthsOf(site, air);
			Color steam = site.Substance != null ? site.Substance.Tint : SteamWhite;
			float radius = GeothermalVents.SourceRadius(site);
			float spread = site.Kind == GeothermalKind.HotSpring ? PoolSpread : PlumeSpread;

			if (site.Kind != GeothermalKind.Geyser)
			{
				float strength = site.Kind == GeothermalKind.HotSpring ? 0.7f : 0.85f;
				DrawSteam(site, vent, visible.Quiet, radius, spread, GeothermalVents.RiseSpeed(site), air.Wind, strength, 0, steam, camera, material, profile, true);
				return;
			}

			GeyserMoment moment = GeothermalVents.Moment(site, motionSeconds);
			if (moment.Steam <= 0f)
			{
				// Waiting: the vent breathes a little steam off the water standing in it.
				DrawSteam(site, vent, visible.Quiet, radius, PlumeSpread, GeothermalVents.RiseSpeed(site), air.Wind, 0.4f, 0, steam, camera, material, profile, true);
				return;
			}

			float column = GeothermalVents.ColumnMetres(site, gravity);
			if (moment.Erupting)
			{
				// The water: launched at √(2gH) for the column's full height, each drop at the speed the
				// eruption had when it left; the launch period outlasts the longest flight.
				float speed = Mathf.Sqrt(2f * gravity * column);
				float flight = 2f * speed / gravity;
				float wind = air.Wind.magnitude;
				Vector2 down = wind > 0.1f ? air.Wind / wind : Vector2.zero;
				float puff = 0.8f + 0.12f * column;
				var bounds = new Bounds(vent + Vector3.up * (0.5f * column), new Vector3(column + 2f * puff + wind * flight, column + 4f * puff, column + 2f * puff + wind * flight));
				Draw(material, profile, camera, bounds, vent, WaterPopulation, 1f, 0.55f,
					new Vector4(puff, 0f, 0f, 0f), new Vector4(0f, 0f, down.x, down.y), new Vector4(moment.Since, moment.Duration, wind, 0f),
					new Vector4(speed, gravity, JetHalfAngle, 1.1f * flight), new Color(WaterWhite.r, WaterWhite.g, WaterWhite.b, 0f), Vector4.zero);
			}
			// The steam boiling off the column while it plays, and drifting off after: at least as tall as the
			// column, longer when the air holds it, shrinking and thinning as it dies away. Its pace is fixed for
			// the site (the full length over the eruption's rise), so the puffs stretch with it rather than scrub.
			float full = Mathf.Max(visible.Erupting, 1.2f * column);
			float rise = GeothermalVents.RiseSpeed(site, true);
			float length = full * Mathf.Lerp(0.4f, 1f, moment.Steam);
			DrawSteam(site, vent, length, radius + 0.1f * column, PlumeSpread, rise, air.Wind, moment.Steam, 1, steam, camera, material, profile, false,
				full / Mathf.Max(0.5f, rise));
		}

		/// <summary>
		/// One steam plume: <paramref name="length"/> m along an axis climbing at <paramref name="rise"/> m/s and
		/// carried by the wind, so it leans by their ratio.
		/// </summary>
		private void DrawSteam(in GeothermalSite site, Vector3 vent, float length, float radius, float spread, float rise, Vector2 wind, float strength, int layer,
			Color colour, Camera camera, Material material, WeatherRenderProfile profile, bool holdPace, float fixedPeriod = 0f)
		{
			if (length < 0.5f || strength <= 0.001f)
			{
				return;
			}
			float u = wind.magnitude;
			float speed = Mathf.Sqrt(rise * rise + u * u);
			float up = length * rise / Mathf.Max(0.01f, speed);
			Vector2 sideways = u > 0.01f ? wind / u * (length * u / Mathf.Max(0.01f, speed)) : Vector2.zero;
			float period = Mathf.Max(1f, length / Mathf.Max(0.05f, speed));
			Vector4 periods = holdPace ? Held(site.Seed, layer, period) : new Vector4(Mathf.Max(1f, fixedPeriod), Mathf.Max(1f, fixedPeriod), 0f, 0f);
			// More puffs for a longer plume: about one per metre and a half, within the field's share.
			float share = Mathf.Clamp(length / 1.5f / Particles, 0.06f, 0.6f);
			float wide = radius + spread * length;
			Vector3 end = vent + new Vector3(sideways.x, up, sideways.y);
			Vector3 min = Vector3.Min(vent, end) - new Vector3(wide, 2f, wide) * 1.6f;
			Vector3 max = Vector3.Max(vent, end) + new Vector3(wide, wide, wide) * 1.6f;
			var bounds = new Bounds((min + max) * 0.5f, max - min);
			Draw(material, profile, camera, bounds, vent, SteamPopulation, share, 0.35f,
				new Vector4(length, radius, spread, period), new Vector4(sideways.x, sideways.y, up, Mathf.Clamp01(strength)), Vector4.zero, Vector4.zero,
				new Color(colour.r, colour.g, colour.b, 0f), periods);
		}

		/// <summary>The periods a site's two layers hold for one of its plumes this frame (see <see cref="VolcanicPlumePresenter.PaceWindowSeconds"/>).</summary>
		private Vector4 Held(uint seed, int layer, float period)
		{
			if (!held.TryGetValue(seed, out WorldMotion.HeldValue[] periods))
			{
				if (held.Count > 256)
				{
					held.Clear();
				}
				periods = new WorldMotion.HeldValue[4];
				held[seed] = periods;
			}
			float a = periods[layer * 2].Hold(period, windowA);
			float b = periods[layer * 2 + 1].Hold(period, windowB);
			return new Vector4(a, b, 0f, 0f);
		}

		private void Draw(Material material, WeatherRenderProfile profile, Camera camera, Bounds bounds, Vector3 vent, int population, float share, float opacity,
			Vector4 rise, Vector4 lean, Vector4 drift, Vector4 ballistic, Color colour, Vector4 heldPeriods)
		{
			block.Clear();
			if (profile != null && profile.PrecipitationAtlas != null)
			{
				block.SetTexture(MainTexId, profile.PrecipitationAtlas);
			}
			block.SetVector(VentId, new Vector4(vent.x, vent.y, vent.z, (float)WorldMotion.Repeat(motionSeconds, WorldMotion.ShaderWrapSeconds)));
			block.SetVector(FormId, new Vector4(population, share, opacity, VolcanicPlumePresenter.AshRow));
			block.SetVector(RiseId, rise);
			block.SetVector(LeanId, lean);
			block.SetVector(DriftId, drift);
			block.SetVector(BallisticId, ballistic);
			block.SetColor(ColorId, colour);
			block.SetVector(PaceId, new Vector4(intoA, intoB, (float)VolcanicPlumePresenter.PaceWindowSeconds, 0f));
			block.SetVector(HeldId, heldPeriods);
			var rp = new RenderParams(material)
			{
				camera = camera,
				matProps = block,
				worldBounds = bounds,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = camera.gameObject.layer,
			};
			Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
		}
	}
}
