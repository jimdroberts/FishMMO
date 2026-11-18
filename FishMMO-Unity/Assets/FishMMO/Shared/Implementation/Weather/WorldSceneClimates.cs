using System.Runtime.CompilerServices;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared
{
	/// <summary>
	/// A scene's climate, biomes and weather settings, as the world modules read them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The climate's extension of the core's <see cref="WorldSceneSettings"/> (Phase 4 of the core and modules plan,
	/// 2026-11-17). These were members of the component and are the same reads: <c>settings.Climate()</c> is what
	/// <c>settings.Climate</c> was, from the scene's atlas entry, then its own <see cref="WorldSceneClimate"/>, then its body.
	/// </para>
	/// <para>
	/// What the component kept per scene at runtime — the weather's runtime offsets and the cached placement climate and world
	/// conditions — is kept here per settings object (<see cref="State"/>), and goes with it.
	/// </para>
	/// </remarks>
	public static class WorldSceneClimates
	{
		/// <summary>The runtime state a scene's climate keeps, per settings object.</summary>
		private sealed class State
		{
			/// <summary>Runtime shift on top of the climate's global temperature offset. Written by the weather, and every climate reading in the scene moves with it.</summary>
			public float RuntimeTemperatureOffset;
			/// <summary>Runtime shift on top of the climate's global humidity offset.</summary>
			public float RuntimeHumidityOffset;

			public WorldSceneClimate Own;
			public bool OwnResolved;

			public BiomeWorldConditions Conditions;
			public WorldBody ConditionsBody;
			public SolarSystemProfile ConditionsSystem;
			public bool ConditionsCached;

			public ScenePlacementClimate PlacementClimate;
		}

		private static readonly ConditionalWeakTable<WorldSceneSettings, State> states = new ConditionalWeakTable<WorldSceneSettings, State>();

		private static State StateOf(WorldSceneSettings settings) => states.GetValue(settings, _ => new State());

		/// <summary>The scene's own <see cref="WorldSceneClimate"/>, or null. Looked up once in play; live in the editor.</summary>
		private static WorldSceneClimate Own(WorldSceneSettings settings)
		{
#if UNITY_EDITOR
			if (!Application.isPlaying)
			{
				return settings.GetComponent<WorldSceneClimate>();
			}
#endif
			State state = StateOf(settings);
			if (!state.OwnResolved)
			{
				state.Own = settings.GetComponent<WorldSceneClimate>();
				state.OwnResolved = true;
			}
			return state.Own;
		}

		/// <summary>The scene's own <see cref="WorldSceneClimate"/>, added if it has none.</summary>
		private static WorldSceneClimate OwnOrAdd(WorldSceneSettings settings)
		{
			WorldSceneClimate own = Own(settings);
			if (own == null)
			{
				own = settings.gameObject.AddComponent<WorldSceneClimate>();
				State state = StateOf(settings);
				state.Own = own;
				state.OwnResolved = true;
			}
			return own;
		}

		// ── Climate and biome map ─────────────────────────────────────

		/// <summary>The climate: the atlas entry's, else the scene's own, else the body's base climate.</summary>
		public static ClimateSettings Climate(this WorldSceneSettings settings)
		{
			WorldAtlasScene entry = settings.AtlasEntry();
			if (entry != null && entry.Climate != null)
			{
				return entry.Climate;
			}
			WorldSceneClimate own = Own(settings);
			if (own != null && own.Climate != null)
			{
				return own.Climate;
			}
			WorldBody body = settings.Body();
			return body != null ? body.BaseClimate : null;
		}

		/// <summary>Sets the scene's own climate (<see cref="WorldSceneClimate"/>, added if it has none). The atlas entry's still wins.</summary>
		public static void SetClimate(this WorldSceneSettings settings, ClimateSettings climate)
		{
			if (climate == null && Own(settings) == null)
			{
				return;
			}
			OwnOrAdd(settings).Climate = climate;
		}

		/// <summary>The biome map: the atlas entry's, else the scene's own.</summary>
		public static SceneBiomeMap BiomeMap(this WorldSceneSettings settings)
		{
			WorldAtlasScene entry = settings.AtlasEntry();
			if (entry != null && entry.BiomeMap != null)
			{
				return entry.BiomeMap;
			}
			WorldSceneClimate own = Own(settings);
			return own != null ? own.BiomeMap : null;
		}

		/// <summary>Sets the scene's own biome map (<see cref="WorldSceneClimate"/>, added if it has none). The atlas entry's still wins.</summary>
		public static void SetBiomeMap(this WorldSceneSettings settings, SceneBiomeMap map)
		{
			if (map == null && Own(settings) == null)
			{
				return;
			}
			OwnOrAdd(settings).BiomeMap = map;
		}

		/// <summary>Where the scene keeps the rain off, baked by the cut (<see cref="SceneShelterMap"/>); null for a scene with none.</summary>
		public static SceneShelterMap Shelter(this WorldSceneSettings settings)
		{
			WorldAtlasScene entry = settings.AtlasEntry();
			return entry != null ? entry.Shelter : null;
		}

		// ── Weather settings ──────────────────────────────────────────

		/// <summary>
		/// How this scene gets weather. Auto gives dungeons none and everything else its own; a
		/// dungeon that wants weather says so in its atlas entry.
		/// </summary>
		public static WeatherSceneMode WeatherMode(this WorldSceneSettings settings)
		{
			WorldAtlasScene entry = settings.AtlasEntry();
			return entry != null ? entry.EffectiveWeather : WeatherSceneMode.Auto;
		}

		/// <summary>What this scene is authored to add to its air, always. Zero adds nothing.</summary>
		public static AirOffsets AuthoredAir(this WorldSceneSettings settings)
		{
			WorldAtlasScene entry = settings.AtlasEntry();
			return entry != null ? entry.Air : default;
		}

		/// <summary>Whether the automatic director may start storm cells here. Admins can switch it at runtime.</summary>
		public static bool WeatherDirector(this WorldSceneSettings settings)
		{
			WorldAtlasScene entry = settings.AtlasEntry();
			return entry == null || entry.WeatherDirector;
		}

		// ── Runtime offsets ───────────────────────────────────────────

		/// <summary>
		/// Runtime shift on top of <see cref="Climate"/>'s global temperature offset. Mutable: a
		/// weather or season system writes it, and every climate reading in the scene moves with it.
		/// </summary>
		public static float RuntimeTemperatureOffset(this WorldSceneSettings settings) => StateOf(settings).RuntimeTemperatureOffset;

		/// <summary>Runtime shift on top of <see cref="Climate"/>'s global humidity offset. Mutable, like the temperature's.</summary>
		public static float RuntimeHumidityOffset(this WorldSceneSettings settings) => StateOf(settings).RuntimeHumidityOffset;

		/// <summary>Sets the runtime offsets (the weather's, and the tests').</summary>
		public static void SetRuntimeOffsets(this WorldSceneSettings settings, float temperature, float humidity)
		{
			State state = StateOf(settings);
			state.RuntimeTemperatureOffset = temperature;
			state.RuntimeHumidityOffset = humidity;
		}

		// ── What the world offers, and the climate at a place ─────────

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
		public static BiomeWorldConditions WorldConditions(this WorldSceneSettings settings)
		{
			ScenePlacementClimate placement = settings.PlacementClimate();
			if (placement != null)
			{
				return placement.Conditions;
			}
			SolarSystemProfile system = SolarSystemProfile.Active;
			// An empty atlas body is the home world (ScenePlacementClimate.ResolveBody), not "no world".
			WorldBody body = ScenePlacementClimate.ResolveBody(system, settings.AtlasEntry());
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
			State state = StateOf(settings);
			if (!state.ConditionsCached || !ReferenceEquals(state.ConditionsBody, body) || !ReferenceEquals(state.ConditionsSystem, system))
			{
				state.Conditions = BiomeWorldConditions.For(system, body);
				state.ConditionsBody = body;
				state.ConditionsSystem = system;
				state.ConditionsCached = true;
			}
			return state.Conditions;
		}

		/// <summary>
		/// The climate at a position in this scene right now. See <see cref="SampleClimateAt(WorldSceneSettings, Vector3, float, out float)"/>.
		/// </summary>
		public static ClimateSample SampleClimateAt(this WorldSceneSettings settings, Vector3 worldPosition, float height01)
		{
			return settings.SampleClimateAt(worldPosition, height01, out _);
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
		/// with no place on any body reads exactly as <see cref="SampleClimate(WorldSceneSettings, float, float)"/> always did.
		/// </para>
		/// <para>
		/// The runtime offsets — the weather's and the tests' hooks — apply on top in every case.
		/// </para>
		/// <para>
		/// No allocation: the placement (field, conditions, moisture grid) is built once and cached per
		/// scene, and re-checked against the atlas entry on each read so an edit in the designer rebuilds it.
		/// </para>
		/// </remarks>
		public static ClimateSample SampleClimateAt(this WorldSceneSettings settings, Vector3 worldPosition, float height01, out float normalizedHeight)
		{
			State state = StateOf(settings);
			return settings.SampleClimateAt(worldPosition, height01, out normalizedHeight, state.RuntimeTemperatureOffset, state.RuntimeHumidityOffset);
		}

		/// <summary>
		/// The same, with the runtime offsets given rather than read from the scene: those of a particular moment
		/// (<see cref="SceneClimate.OffsetsAt"/>), so the climate of that moment is the same whenever it is read.
		/// </summary>
		public static ClimateSample SampleClimateAt(this WorldSceneSettings settings, Vector3 worldPosition, float height01, out float normalizedHeight,
			float runtimeTemperature, float runtimeHumidity)
		{
			ScenePlacementClimate placement = settings.PlacementClimate();
			if (placement != null && placement.Generated)
			{
				ClimateSample sample = placement.SampleAt(worldPosition, out normalizedHeight);
				return WithRuntime(sample, runtimeTemperature, runtimeHumidity);
			}

			normalizedHeight = height01;
			SceneBiomeMap map = settings.BiomeMap();
			float latitude01 = map != null ? map.Latitude01(worldPosition) : 0.5f;
			ClimateSample authored = settings.SampleClimate(height01, latitude01, runtimeTemperature, runtimeHumidity, worldPosition.y);
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
		public static double LatitudeAt(this WorldSceneSettings settings, Vector3 worldPosition)
		{
			ScenePlacementClimate placement = settings.PlacementClimate();
			if (placement != null && placement.Generated)
			{
				return placement.LatitudeAt(worldPosition.x, worldPosition.z);
			}
			SceneBiomeMap map = settings.BiomeMap();
			float latitude01 = map != null ? map.Latitude01(worldPosition) : 0.5f;
			ClimateSettings climate = settings.Climate();
			float span = (climate != null ? climate : ClimateSettings.Default).MapLatitudeSpanDegrees;
			return settings.ClimateLatitude() + (latitude01 - 0.5f) * span;
		}

		/// <summary>This scene's placement climate, built on first use and whenever the placement changes. Null when it is placed on no body.</summary>
		public static ScenePlacementClimate PlacementClimate(this WorldSceneSettings settings)
		{
			State state = StateOf(settings);
			WorldAtlasScene entry = settings.AtlasEntry();
			if (entry == null || !entry.Placed)
			{
				state.PlacementClimate = null;
				return null;
			}
			SolarSystemProfile system = SolarSystemProfile.Active;
			if (state.PlacementClimate == null || !state.PlacementClimate.Matches(system, entry))
			{
				state.PlacementClimate = ScenePlacementClimate.For(system, entry);
			}
			return state.PlacementClimate;
		}

		/// <summary>
		/// The climate at a normalised height and a position across the biome map given as
		/// <paramref name="latitude01"/>, by the authored latitude span and with no moisture term.
		/// </summary>
		/// <remarks>Prefer <see cref="SampleClimateAt(WorldSceneSettings, Vector3, float)"/>, which knows where a generated scene really is.</remarks>
		public static ClimateSample SampleClimate(this WorldSceneSettings settings, float height01, float latitude01)
		{
			State state = StateOf(settings);
			return settings.SampleClimate(height01, latitude01, state.RuntimeTemperatureOffset, state.RuntimeHumidityOffset, 0f);
		}

		/// <summary>
		/// The same, with the runtime offsets given (see <see cref="SampleClimateAt(WorldSceneSettings, Vector3, float, out float, float, float)"/>)
		/// and the altitude the reading is taken at, metres above the scene's datum.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Anchored on the world it stands on (2026-10-15).</b> A hand-made scene used to take its temperature from its
		/// climate asset's Earth-referenced sea level (+26 °C) less a lapse over its NORMALISED height, then add its body's
		/// offset from the home world — so on a cool world a hand-made scene read about 8 K warmer than ground cut from the
		/// same latitude, and a 600 m terrain cooled by 29 K from shore to summit. It now reads its body's climate table at
		/// its latitude (spread across its map by the authored span) and how inland its place is, cools by the body's own
		/// lapse per metre, and keeps the asset's <see cref="ClimateSettings.GlobalTemperatureOffset"/> as an authored
		/// nudge. The asset still supplies the humidity curve, the elevation tiers and the variants.
		/// </para>
		/// <para>
		/// An unplaced scene stands on the home world at latitude 0 when a solar system exists, and on the asset alone when
		/// none does.
		/// </para>
		/// </remarks>
		public static ClimateSample SampleClimate(this WorldSceneSettings settings, float height01, float latitude01, float runtimeTemperature, float runtimeHumidity, float altitudeMetres)
		{
			ClimateSettings climate = settings.Climate();
			ClimateSettings asset = climate != null ? climate : ClimateSettings.Default;
			ClimateSample sample = asset.Evaluate(height01, latitude01);
			// Somewhere between a coast and a continent, until a body says which.
			sample.Continentality = 0.5f;
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = ScenePlacementClimate.ResolveBody(system, settings.AtlasEntry());
			PlanetClimateTable table = system != null && body != null ? PlanetClimateTable.For(system, body) : null;
			if (table != null)
			{
				double latitude = settings.ClimateLatitude() + (Mathf.Clamp01(latitude01) - 0.5f) * asset.MapLatitudeSpanDegrees;
				ScenePlacementClimate placement = settings.PlacementClimate();
				float continentality = placement != null ? placement.CentreContinentality : 0.5f;
				float lapse = (float)ClimateModel.LapseRatePerMetre(system, body);
				float kelvin = table.AnnualKelvin(latitude, continentality)
					- Mathf.Max(0f, altitudeMetres) * lapse
					+ asset.GlobalTemperatureOffset * (float)ClimateModel.KelvinPerUnit;
				sample.Kelvin = kelvin;
				sample.Temperature = ClimateModel.ToScale(kelvin);
				sample.Continentality = continentality;
				// The world's water against the home world's: a dry world's scenes are drier, a wet one's wetter.
				float homeWater = system.HomeWorld != null ? system.HomeWorld.Water : 0.7f;
				sample.Humidity = Mathf.Clamp(sample.Humidity + (CloudWorld.WaterOf(body) - homeWater) * 1.2f, -1f, 1f);
			}
			return WithRuntime(sample, runtimeTemperature, runtimeHumidity);
		}

		/// <summary>A reading with the runtime offsets added: to the scale, clamped, and to the kelvin, which is not.</summary>
		private static ClimateSample WithRuntime(ClimateSample sample, float runtimeTemperature, float runtimeHumidity)
		{
			if (sample.Kelvin > 0f)
			{
				sample.Kelvin += runtimeTemperature * (float)ClimateModel.KelvinPerUnit;
				sample.Temperature = ClimateModel.ToScale(sample.Kelvin);
			}
			else
			{
				sample.Temperature = Mathf.Clamp(sample.Temperature + runtimeTemperature, -1f, 1f);
			}
			sample.Humidity = Mathf.Clamp(sample.Humidity + runtimeHumidity, -1f, 1f);
			return sample;
		}

		/// <summary>The climate variant a biome shows under a reading: the biome's own, else the climate asset's defaults.</summary>
		public static BiomeClimateVariant ResolveVariant(this WorldSceneSettings settings, BiomeTemplate biome, ClimateSample sample)
		{
			if (biome == null)
			{
				return null;
			}
			// Identical either way for a biome with its own variants, which is the only case that
			// resolves to anything: the derived climate carries no default variants of its own.
			ClimateSettings climate = settings.Climate();
			return (climate != null ? climate : ClimateSettings.Default).ResolveVariant(biome, sample);
		}
	}
}
