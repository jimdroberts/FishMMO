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

		/// <summary>The preset used when <see cref="WeatherMode"/> is Fixed.</summary>
		public WeatherPreset FixedWeather => AtlasEntry != null ? AtlasEntry.FixedWeather : null;

		public float FixedWeatherIntensity => AtlasEntry != null ? AtlasEntry.FixedWeatherIntensity : 1f;

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
		/// The climate at a normalised height and latitude right now: the climate asset's reading
		/// plus this scene's runtime offsets.
		/// </summary>
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
		/// 0 at the map's north edge, 1 at its south, 0.5 at the scene's own latitude. Spread over
		/// <see cref="ClimateSettings.MapLatitudeSpanDegrees"/>, so one map can run from forest to
		/// tundra across its width.
		/// </param>
		public void CelestialOffsets(float latitude01, out float temperature, out float humidity)
		{
			temperature = 0f;
			humidity = 0f;
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = Body;
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
		public BiomeWorldConditions WorldConditions
		{
			get
			{
				WorldBody body = Body;
				return body == null
					? BiomeWorldConditions.Earthlike
					: BiomeWorldConditions.For(SolarSystemProfile.Active, body);
			}
		}

		public ClimateSample SampleClimate(float height01, float latitude01)
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
			sample.Temperature = Mathf.Clamp(sample.Temperature + bodyTemperature + RuntimeTemperatureOffset, -1f, 1f);
			sample.Humidity = Mathf.Clamp(sample.Humidity + bodyHumidity + RuntimeHumidityOffset, -1f, 1f);
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
