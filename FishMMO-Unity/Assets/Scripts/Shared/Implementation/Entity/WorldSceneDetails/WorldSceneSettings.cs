using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared
{
	/// <summary>
	/// MonoBehaviour holding per-scene server-facing configuration and the link to the scene's
	/// <see cref="WorldMapDefinition"/>. Day/night cycle authoring has moved to
	/// <see cref="WorldDayNightCycle"/> on its own component so a scene can mix and match (a
	/// dungeon may want only the settings, a surface zone may want both).
	/// </summary>
	public class WorldSceneSettings : MonoBehaviour
	{
		/// <summary>
		/// The hard ceiling on clients in any one scene, anywhere in the project.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The single source of truth for the cap. The world server (routing and instance
		/// spin-up) and the scene server (channel admission) both clamp to this, so a scene
		/// authored above it is reduced rather than honoured — a designer cannot raise a scene's
		/// population past what the bandwidth and observer budgets were sized for.
		/// </para>
		/// <para>
		/// 200 is a deliberate ceiling, not a guess: per-client cost is bounded by the observer
		/// visibility budget rather than by scene population, so scaling past this is done by
		/// adding scene instances (and scene servers), never by widening one scene. See
		/// <c>SceneServerPlacementPolicy</c>.
		/// </para>
		/// </remarks>
		public const int MaximumClientsPerScene = 200;

		/// <summary>
		/// The maximum number of clients allowed in this scene.
		/// </summary>
		/// <remarks>
		/// The <see cref="RangeAttribute"/> constrains the inspector; it does not constrain a value
		/// edited into the scene YAML by hand or arriving from an older asset, so every consumer
		/// clamps to <see cref="MaximumClientsPerScene"/> on read as well.
		/// </remarks>
		[Tooltip("The maximum number of clients allowed in this scene. Clamped to 200. The scene's world atlas entry may override it.")]
		[Range(1, MaximumClientsPerScene)]
		[SerializeField, FormerlySerializedAs("MaxClients")]
		private int maxClients = MaximumClientsPerScene;

		/// <summary>The client cap: the atlas entry's, else the scene's own.</summary>
		public int MaxClients
		{
			get
			{
				WorldAtlasScene entry = AtlasEntry;
				return entry != null && entry.MaxClients > 0 ? entry.MaxClients : maxClients;
			}
			set => maxClients = value;
		}

		/// <summary>
		/// Optional hand-made map definition, for a set of scenes that share one map (a dungeon and
		/// its instanced twin). Normally empty: a client build bakes a definition per scene into
		/// <see cref="WorldMapDefinition.BakedDirectory"/>, points the world scene details cache at
		/// it, and removes it after the build. A definition assigned here is filled in by that same
		/// bake instead.
		/// </summary>
		[Tooltip("Optional. Leave empty and the client build bakes this scene's map; assign one only to share a map between scenes.")]
		public WorldMapDefinition MapDefinition;

		/// <summary>
		/// The scene's loading image. Authored here; the map bake copies it into the scene's
		/// definition so the client finds the whole presentation in one place.
		/// </summary>
		[Tooltip("The image shown while this scene loads. Copied into the baked map definition.")]
		public Sprite SceneTransitionImage;

		/// <summary>
		/// The climate model this scene runs under: lapse rates, humidity curve, tier boundaries and
		/// the base global offsets. Shared between scenes that share a climate; null falls back to
		/// the built-in defaults.
		/// </summary>
		[Header("Climate")]
		[Tooltip("Climate model for this scene. Shared between scenes with the same climate. The world atlas entry, then the body's base climate, take precedence.")]
		[SerializeField, FormerlySerializedAs("Climate")]
		private ClimateSettings climate;

		/// <summary>The climate: the atlas entry's, else the scene's own, else the body's base climate.</summary>
		public ClimateSettings Climate
		{
			get
			{
				WorldAtlasScene entry = AtlasEntry;
				if (entry != null && entry.Climate != null)
				{
					return entry.Climate;
				}
				if (climate != null)
				{
					return climate;
				}
				WorldBody body = Body;
				return body != null ? body.BaseClimate : null;
			}
			set => climate = value;
		}

		/// <summary>
		/// Which biome lies where, baked when the world was generated. Biomes are mixed through a
		/// scene; the map is how anything asks what is under a position.
		/// </summary>
		[Tooltip("Baked biome grid for this scene, imported from the world generator's biome map. The world atlas entry may override it.")]
		[SerializeField, FormerlySerializedAs("BiomeMap")]
		private SceneBiomeMap biomeMap;

		/// <summary>The biome map: the atlas entry's, else the scene's own.</summary>
		public SceneBiomeMap BiomeMap
		{
			get
			{
				WorldAtlasScene entry = AtlasEntry;
				return entry != null && entry.BiomeMap != null ? entry.BiomeMap : biomeMap;
			}
			set => biomeMap = value;
		}

		/// <summary>
		/// Runtime shift on top of <see cref="Climate"/>'s global temperature offset. Mutable: a
		/// weather or season system writes it, and every climate reading in the scene moves with it.
		/// </summary>
		[Tooltip("Runtime temperature shift on top of the climate asset. Driven by weather at runtime.")]
		[Range(-2f, 2f)] public float RuntimeTemperatureOffset;

		/// <summary>
		/// Runtime shift on top of <see cref="Climate"/>'s global humidity offset. Mutable, like
		/// <see cref="RuntimeTemperatureOffset"/>.
		/// </summary>
		[Tooltip("Runtime humidity shift on top of the climate asset. Driven by weather at runtime.")]
		[Range(-2f, 2f)] public float RuntimeHumidityOffset;

		// ── World placement: owned by the world atlas ─────────────────

		/// <summary>This scene's world atlas entry, or null when it has not been added to the atlas.</summary>
		public WorldAtlasScene AtlasEntry => WorldAtlasScene.Find(OwnSceneName);

		/// <summary>
		/// The scene this component belongs to, resolved once rather than on every read.
		/// </summary>
		/// <remarks>
		/// <para>The biome sampler asked <c>gameObject.scene</c> for every sample, and every property
		/// here that consults the atlas asked it for the scene's name — two engine calls and a fresh
		/// string each time, about ten times per biome sample, and weather exposure samples every
		/// character every second on the server and the owning client alike. The answer cannot
		/// change at runtime: nothing moves a settings object between scenes, and it goes with its
		/// scene when that unloads. So it is read on first use and read again whenever the
		/// component is enabled.</para>
		///
		/// <para>Outside play mode it is read live every time, exactly as before. A designer can drag
		/// the object into another open scene, and edit-mode tools can hold a component whose
		/// <c>OnEnable</c> never ran.</para>
		/// </remarks>
		public Scene OwnScene
		{
			get
			{
#if UNITY_EDITOR
				if (!Application.isPlaying)
				{
					return gameObject.scene;
				}
#endif
				if (ownScene.handle == 0)
				{
					ResolveOwnScene();
				}
				return ownScene;
			}
		}

		/// <summary>The name of <see cref="OwnScene"/>, resolved with it.</summary>
		private string OwnSceneName
		{
			get
			{
#if UNITY_EDITOR
				if (!Application.isPlaying)
				{
					return gameObject.scene.name;
				}
#endif
				if (ownScene.handle == 0)
				{
					ResolveOwnScene();
				}
				return ownSceneName;
			}
		}

		/// <summary>
		/// The resolved scene. A handle of 0 is no scene at all, and means "not resolved yet" — so a
		/// value the editor's script reload wiped simply resolves again.
		/// </summary>
		private Scene ownScene;
		private string ownSceneName;

		/// <summary>Reads the scene this component is in, and its name.</summary>
		private void ResolveOwnScene()
		{
			ownScene = gameObject.scene;
			ownSceneName = ownScene.name;
		}

		/// <summary>
		/// The planet or moon this scene stands on. Null means the home world. Its rotation and sun
		/// give the scene its time of day; its distance from the star and its atmosphere shift the
		/// climate and decide whether weather exists at all.
		/// </summary>
		public WorldBody Body => AtlasEntry != null ? AtlasEntry.Body : null;

		/// <summary>Latitude the sun path and auroras use, in degrees.</summary>
		public float Latitude => AtlasEntry != null ? (float)AtlasEntry.EffectiveSunLatitude : 0f;

		/// <summary>Longitude the whole scene takes its time of day at, in degrees.</summary>
		public float Longitude => AtlasEntry != null ? (float)AtlasEntry.TimeLongitude : 0f;

		/// <summary>World time, or a developer-authored fixed time. Nothing changes it at runtime.</summary>
		public SceneTimeMode TimeMode => AtlasEntry != null ? AtlasEntry.TimeMode : SceneTimeMode.World;

		/// <summary>The time shown when <see cref="TimeMode"/> is Fixed. 0.5 is noon.</summary>
		public float FixedTimeOfDay01 => AtlasEntry != null ? AtlasEntry.FixedTimeOfDay01 : 0.5f;

		/// <summary>
		/// How this scene gets weather. Auto gives dungeons none and everything else its own; a
		/// dungeon that wants weather says so in its atlas entry.
		/// </summary>
		public WeatherSceneMode WeatherMode => AtlasEntry != null ? AtlasEntry.EffectiveWeather : WeatherSceneMode.Auto;

		/// <summary>What this scene is authored to add to its air, always. Zero adds nothing.</summary>
		public AirOffsets AuthoredAir => AtlasEntry != null ? AtlasEntry.Air : default;

		/// <summary>Whether the automatic director may start storm cells here. Admins can switch it at runtime.</summary>
		public bool WeatherDirector => AtlasEntry == null || AtlasEntry.WeatherDirector;

		private static readonly Dictionary<int, WorldSceneSettings> byScene = new Dictionary<int, WorldSceneSettings>();

		/// <summary>The settings component of a loaded scene, if it has one. Scenes load additively on a scene server, so this is keyed by scene.</summary>
		public static bool TryGetForScene(Scene scene, out WorldSceneSettings settings)
		{
			return byScene.TryGetValue(scene.handle, out settings) && settings != null;
		}

		private void OnEnable()
		{
			ResolveOwnScene();
			byScene[ownScene.handle] = this;
		}

		private void OnDisable()
		{
			if (byScene.TryGetValue(gameObject.scene.handle, out WorldSceneSettings current) && current == this)
			{
				byScene.Remove(gameObject.scene.handle);
			}
		}

		/// <summary>
		/// How much colder or wetter this scene's world is than the home world, from where it sits.
		/// </summary>
		/// <remarks>
		/// Zero for a scene on the home world, and zero when there is no solar system loaded, so
		/// nothing authored against the old behaviour moves. Cached per frame's worth of calls would
		/// be premature: this is a handful of multiplies and the biome sampler is not a hot path.
		/// </remarks>
		public void CelestialOffsets(out float temperature, out float humidity)
		{
			CelestialOffsets(0.5f, out temperature, out humidity);
		}

		/// <summary>
		/// The same, at a point across the scene's own biome map.
		/// </summary>
		/// <param name="latitude01">
		/// 0 at the map's south edge, 1 at its north (<see cref="SceneBiomeMap.Latitude01"/>), 0.5
		/// at the scene's own latitude. Spread over <see cref="ClimateSettings.MapLatitudeSpanDegrees"/>,
		/// so one hand-painted map can run from forest to tundra across its width. A scene cut from
		/// the globe does not use this: see <see cref="SampleClimateAt"/>.
		/// </param>
		public void CelestialOffsets(float latitude01, out float temperature, out float humidity)
		{
			temperature = 0f;
			humidity = 0f;
			SolarSystemProfile system = SolarSystemProfile.Active;
			// An empty atlas body means the home world, as the placement climate reads it (2026-10-10): taken
			// literally, a hand-made scene on the home world got no latitude cooling at all.
			WorldBody body = ScenePlacementClimate.ResolveBody(system, AtlasEntry);
			if (system == null || body == null)
			{
				return;
			}
			/* At THIS scene's latitude, which is what gives a body more than one climate.
			 *
			 * The poles are cold because the sun never climbs far above their horizon, not because
			 * they are further from it — the difference in distance across a planet is nothing, and
			 * the difference in the angle light arrives at is everything. CelestialMath.LatitudeTemperature
			 * already works it out properly, from the sun's noon altitude against the body's own
			 * axial tilt and where it is in its orbit; it simply was not being asked.
			 *
			 * The home world is included now rather than skipped. It has a tilt of 23.4° and poles
			 * of its own, and exempting it meant every scene on it resolved the same temperate
			 * biome set from the equator to the ice cap. */
			/* The scene's own latitude, offset by where in its map this point is. At true scale a
			 * 4 km scene spans about 0.04° and the gradient would be invisible, so the span is
			 * authored rather than derived: a scene that wants to read as a climate transition says
			 * how many degrees it stands for, and one that does not sets it to zero. */
			float span = (Climate != null ? Climate : ClimateSettings.Default).MapLatitudeSpanDegrees;
			double latitude = Latitude + (Mathf.Clamp01(latitude01) - 0.5f) * span;
			CelestialMath.MeanClimateOffsets(system, body, out temperature, out humidity, latitude);
		}

		/// <summary>What this scene's world physically offers, for the biome resolver.</summary>
		/// <remarks>
		/// <para>
		/// A placed scene takes it from its placement's climate field — the very conditions the
		/// generator filtered its biomes by — so a biome chosen at runtime passes exactly the gate the
		/// painted one did.
		/// </para>
		/// <para>
		/// Cached either way in play. Working the conditions out walks the body's orbit eight times,
		/// and the biome sampler reads this per character per second; the answer only changes when the
		/// body or the system does, which is compared on each read. Outside play mode it is read live,
		/// since a designer may be editing the body.
		/// </para>
		/// </remarks>
		public BiomeWorldConditions WorldConditions
		{
			get
			{
				ScenePlacementClimate placement = PlacementClimate;
				if (placement != null)
				{
					return placement.Conditions;
				}
				SolarSystemProfile system = SolarSystemProfile.Active;
				// An empty atlas body is the home world (ScenePlacementClimate.ResolveBody), not "no world".
				WorldBody body = ScenePlacementClimate.ResolveBody(system, AtlasEntry);
				if (body == null)
				{
					return BiomeWorldConditions.Earthlike;
				}
#if UNITY_EDITOR
				// Outside play mode a designer may be editing the body itself; read it live.
				if (!Application.isPlaying)
				{
					return BiomeWorldConditions.For(system, body);
				}
#endif
				if (!conditionsCached || !ReferenceEquals(conditionsBody, body) || !ReferenceEquals(conditionsSystem, system))
				{
					conditions = BiomeWorldConditions.For(system, body);
					conditionsBody = body;
					conditionsSystem = system;
					conditionsCached = true;
				}
				return conditions;
			}
		}

		[System.NonSerialized] private BiomeWorldConditions conditions;
		[System.NonSerialized] private WorldBody conditionsBody;
		[System.NonSerialized] private SolarSystemProfile conditionsSystem;
		[System.NonSerialized] private bool conditionsCached;

		/// <summary>
		/// The climate at a position in this scene right now. See <see cref="SampleClimateAt(Vector3, float, out float)"/>.
		/// </summary>
		public ClimateSample SampleClimateAt(Vector3 worldPosition, float height01)
		{
			return SampleClimateAt(worldPosition, height01, out _);
		}

		/// <summary>
		/// The climate at a position in this scene right now, and the height the biome system reads
		/// there.
		/// </summary>
		/// <param name="worldPosition">
		/// A position in this scene. In a scene cut from the globe its Y is the altitude the climate
		/// is taken at — pass the ground under a character for the ground's climate (as
		/// <see cref="BiomeSampler"/> does), Y = 0 for sea level.
		/// </param>
		/// <param name="height01">
		/// The normalised terrain height there, as <see cref="BiomeSampler"/> measures it over the
		/// scene's landmass. Used by hand-made and unplaced scenes; a scene cut from the globe reads
		/// its height from its altitude instead.
		/// </param>
		/// <param name="normalizedHeight">
		/// The height the reading was taken at, as the biome resolver should use it: planet-relative
		/// in a scene cut from the globe (what its biomes were painted from), else
		/// <paramref name="height01"/> unchanged.
		/// </param>
		/// <remarks>
		/// <para>
		/// <b>One climate model for a scene cut from the globe.</b> Its biomes were painted by the
		/// body's <see cref="PlanetClimateField"/> at each point's true place and true altitude
		/// (scene Y ÷ its vertical scale); this evaluates exactly that field there
		/// (<see cref="ScenePlacementClimate.SampleAt"/>), so temperature, humidity, elevation tier and
		/// height read at runtime are the ones the ground was painted from. The scene's
		/// <see cref="ClimateSettings"/> asset no longer bends its temperature or humidity — the
		/// generator never read it — and keeps supplying the climate variants.
		/// </para>
		/// <para>
		/// A hand-made scene that is only placed keeps its authored climate, the latitude spread
		/// across its map that it was painted to, plus the wind-driven moisture at its centre. A scene
		/// with no place on any body reads exactly as <see cref="SampleClimate"/> always did.
		/// </para>
		/// <para>
		/// <see cref="RuntimeTemperatureOffset"/> and <see cref="RuntimeHumidityOffset"/> — the weather's
		/// and the tests' hooks — apply on top in every case.
		/// </para>
		/// <para>
		/// No allocation: the placement (field, conditions, moisture grid) is built once and cached on
		/// this component, and re-checked against the atlas entry on each read so an edit in the
		/// designer rebuilds it.
		/// </para>
		/// </remarks>
		public ClimateSample SampleClimateAt(Vector3 worldPosition, float height01, out float normalizedHeight)
		{
			return SampleClimateAt(worldPosition, height01, out normalizedHeight, RuntimeTemperatureOffset, RuntimeHumidityOffset);
		}

		/// <summary>
		/// The same, with the runtime offsets given rather than read from <see cref="RuntimeTemperatureOffset"/> and
		/// <see cref="RuntimeHumidityOffset"/>: those of a particular moment (<see cref="FishMMO.Shared.Weather.SceneClimate.OffsetsAt"/>),
		/// so the climate of that moment is the same whenever it is read.
		/// </summary>
		public ClimateSample SampleClimateAt(Vector3 worldPosition, float height01, out float normalizedHeight,
			float runtimeTemperature, float runtimeHumidity)
		{
			ScenePlacementClimate placement = PlacementClimate;
			if (placement != null && placement.Generated)
			{
				ClimateSample sample = placement.SampleAt(worldPosition, out normalizedHeight);
				sample.Temperature = Mathf.Clamp(sample.Temperature + runtimeTemperature, -1f, 1f);
				sample.Humidity = Mathf.Clamp(sample.Humidity + runtimeHumidity, -1f, 1f);
				return sample;
			}

			normalizedHeight = height01;
			SceneBiomeMap map = BiomeMap;
			float latitude01 = map != null ? map.Latitude01(worldPosition) : 0.5f;
			ClimateSample authored = SampleClimate(height01, latitude01, runtimeTemperature, runtimeHumidity);
			if (placement != null)
			{
				authored.Humidity = Mathf.Clamp(authored.Humidity + placement.CentreMoisture, -1f, 1f);
			}
			return authored;
		}

		/// <summary>
		/// The latitude of a position in this scene, degrees: its true latitude on the globe for a
		/// scene cut from it, else the scene's latitude spread across its biome map by the authored span.
		/// </summary>
		public double LatitudeAt(Vector3 worldPosition)
		{
			ScenePlacementClimate placement = PlacementClimate;
			if (placement != null && placement.Generated)
			{
				return placement.LatitudeAt(worldPosition.x, worldPosition.z);
			}
			SceneBiomeMap map = BiomeMap;
			float latitude01 = map != null ? map.Latitude01(worldPosition) : 0.5f;
			float span = (Climate != null ? Climate : ClimateSettings.Default).MapLatitudeSpanDegrees;
			return Latitude + (latitude01 - 0.5f) * span;
		}

		/// <summary>This scene's placement climate, built on first use and whenever the placement changes. Null when it is placed on no body.</summary>
		public ScenePlacementClimate PlacementClimate
		{
			get
			{
				WorldAtlasScene entry = AtlasEntry;
				if (entry == null || !entry.Placed)
				{
					placementClimate = null;
					return null;
				}
				SolarSystemProfile system = SolarSystemProfile.Active;
				if (placementClimate == null || !placementClimate.Matches(system, entry))
				{
					placementClimate = ScenePlacementClimate.For(system, entry);
				}
				return placementClimate;
			}
		}

		[System.NonSerialized] private ScenePlacementClimate placementClimate;

		/// <summary>
		/// The climate at a normalised height and a position across the biome map given as
		/// <paramref name="latitude01"/>, by the authored latitude span and with no moisture term.
		/// </summary>
		/// <remarks>Prefer <see cref="SampleClimateAt"/>, which knows where a generated scene really is.</remarks>
		public ClimateSample SampleClimate(float height01, float latitude01)
		{
			return SampleClimate(height01, latitude01, RuntimeTemperatureOffset, RuntimeHumidityOffset);
		}

		/// <summary>The same, with the runtime offsets given (see <see cref="SampleClimateAt(Vector3, float, out float, float, float)"/>).</summary>
		public ClimateSample SampleClimate(float height01, float latitude01, float runtimeTemperature, float runtimeHumidity)
		{
			/* The derived model when nothing is authored, not a guess.
			 *
			 * This used to fall back to Temperature = -height01 * 0.8, which reads -0.4 at mid
			 * elevation where the calibrated model reads +0.74 — below freezing over most of a
			 * scene — with no latitude gradient at all. Since no ClimateSettings asset existed
			 * anywhere in the project, that guess, not the model, was deciding every biome and
			 * every rain-or-snow call in the game.
			 *
			 * There is nothing for it to be a fallback FROM: a body's own climate is already
			 * derived from its orbit by CelestialOffsets below, so the asset only ever held the
			 * shared reference model. ClimateSettings.Default is that model. */
			ClimateSample sample = (Climate != null ? Climate : ClimateSettings.Default)
				.Evaluate(height01, latitude01);
			/* The world this scene is ON, before anything local to it.
			 *
			 * Without this a scene on a frozen outer moon resolved exactly the same biomes as one on
			 * the home world: SampleClimate read only the authored ClimateSettings, so the body's
			 * distance from its star — the thing that actually decides whether anything can live
			 * there — reached the weather but never the ground. A designer had to hand-author a
			 * colder ClimateSettings per scene and keep it in step with the orbit by hand.
			 *
			 * The offsets are the body's mean, so latitude and elevation still do their own work on
			 * top; and they are derived from the orbit, so moving a planet moves its biomes. */
			CelestialOffsets(latitude01, out float bodyTemperature, out float bodyHumidity);
			sample.Temperature = Mathf.Clamp(sample.Temperature + bodyTemperature + runtimeTemperature, -1f, 1f);
			sample.Humidity = Mathf.Clamp(sample.Humidity + bodyHumidity + runtimeHumidity, -1f, 1f);
			return sample;
		}

		/// <summary>The climate variant a biome shows under a reading: the biome's own, else the climate asset's defaults.</summary>
		public BiomeClimateVariant ResolveVariant(BiomeTemplate biome, ClimateSample sample)
		{
			if (biome == null)
			{
				return null;
			}
			// Identical either way for a biome with its own variants, which is the only case that
			// resolves to anything: the derived climate carries no default variants of its own.
			return (Climate != null ? Climate : ClimateSettings.Default).ResolveVariant(biome, sample);
		}
	}
}
