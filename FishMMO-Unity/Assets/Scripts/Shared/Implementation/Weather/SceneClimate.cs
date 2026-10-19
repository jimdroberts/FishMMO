using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// What a scene's climate has added to it at a moment of world time: the warmth of the air authored on it and added
	/// at runtime, and its body's season. One sum, read by the server, the client and the test bed alike.
	/// </summary>
	/// <remarks>
	/// The weather read these from <see cref="WorldSceneSettings.RuntimeTemperatureOffset"/> and
	/// <see cref="WorldSceneSettings.RuntimeHumidityOffset"/>, which hold "now" as each machine last wrote it (the
	/// server about once a second, a client once a second, the bed every frame). A sample of an earlier moment then
	/// carried the season and the admin's air of the moment it was TAKEN, not of the moment it was OF, and two
	/// machines sampling the same moment at different times disagreed. Everything decided from the weather of a moment
	/// (where a storm is born, what the ground holds) has to be the same wherever and whenever it is worked out, so the
	/// field reads this, of the moment itself (<see cref="WeatherField.SampleAtSeconds"/>).
	/// </remarks>
	public static class SceneClimate
	{
		// One reading of the season serves every read of the same moment: a frame asks for one moment many times.
		private static WorldBody cachedBody, cachedOwn;
		private static double cachedLatitude = double.NaN;
		private static double cachedHours = double.NaN;
		private static float cachedContinentality = float.NaN;
		private static float cachedTemperature, cachedHumidity;

		/// <summary>The most the climate is moved either way, climate scale: what is added to the air and the season together.</summary>
		public const float MaxOffsetScale = 2f;

		/// <summary><see cref="MaxOffsetScale"/> in kelvin (about 66 K): the most an admin or the bed can add to the air's temperature and have it count.</summary>
		public const float MaxOffsetKelvin = (float)(MaxOffsetScale * ClimateModel.KelvinPerUnit);

		/// <summary>
		/// The runtime climate offsets of a scene at <paramref name="worldSeconds"/>, on the climate scale: the air authored
		/// on it and added at runtime, and its season.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The season is the body's climate table's (<see cref="PlanetClimateTable"/>) at the scene's true latitude
		/// (<see cref="WorldSceneSettings.ClimateLatitude"/>, never the sun path's override) and how continental its centre
		/// is: the moment against the year's mean there, lag and all. A scene's climate already carries the year's mean.
		/// </para>
		/// <para>
		/// The body is the timeline's override when there is one (the bed stands a scene on any body, at its own latitude):
		/// then the offset is that body's moment less the scene's own year there, so the scene reads the other world.
		/// </para>
		/// </remarks>
		public static void OffsetsAt(WorldSceneSettings settings, WeatherTimeline timeline, double worldSeconds,
			out float temperature, out float humidity)
		{
			float added = ((settings != null ? settings.AuthoredAir : default) + (timeline != null ? timeline.AirAtSeconds(worldSeconds) : default)).TemperatureScale;
			BodyShareAt(settings, timeline, worldSeconds, out float bodyTemperature, out float bodyHumidity);
			temperature = Mathf.Clamp(added + bodyTemperature, -MaxOffsetScale, MaxOffsetScale);
			humidity = Mathf.Clamp(bodyHumidity, -2f, 2f);
		}

		/// <summary>
		/// The body's share of <see cref="OffsetsAt"/>, on the climate scale: the season on the scene's own body, or another
		/// body's whole difference when the timeline stands the scene on it.
		/// </summary>
		public static void BodyShareAt(WorldSceneSettings settings, WeatherTimeline timeline, double worldSeconds,
			out float temperature, out float humidity)
		{
			WorldBody own = SceneTime.BodyOf(settings);
			bool overridden = timeline != null && timeline.BodyOverride != null;
			WorldBody body = overridden ? timeline.BodyOverride : own;
			double latitude = overridden || settings == null ? (timeline != null ? timeline.LatitudeDegrees : 0.0) : settings.ClimateLatitude;
			ScenePlacementClimate placement = settings != null ? settings.PlacementClimate : null;
			float continentality = placement != null ? placement.CentreContinentality : 0.5f;
			Season(SolarSystemProfile.Active, body, own, worldSeconds / 3600.0, latitude, continentality, out temperature, out humidity);
		}

		/// <summary>Writes the offsets of <paramref name="worldSeconds"/> onto the settings, for what reads them from there.</summary>
		public static void Apply(WorldSceneSettings settings, WeatherTimeline timeline, double worldSeconds)
		{
			if (settings == null)
			{
				return;
			}
			OffsetsAt(settings, timeline, worldSeconds, out float temperature, out float humidity);
			settings.RuntimeTemperatureOffset = temperature;
			settings.RuntimeHumidityOffset = humidity;
		}

		/// <summary>The season at a latitude on a body, on the climate scale, and the body's difference from the scene's own when it is another.</summary>
		private static void Season(SolarSystemProfile system, WorldBody body, WorldBody own, double hours, double latitude, float continentality,
			out float temperature, out float humidity)
		{
			temperature = 0f;
			humidity = 0f;
			if (system == null || body == null)
			{
				return;
			}
			if (ReferenceEquals(body, cachedBody) && ReferenceEquals(own, cachedOwn) && hours == cachedHours && latitude == cachedLatitude
				&& continentality == cachedContinentality)
			{
				temperature = cachedTemperature;
				humidity = cachedHumidity;
				return;
			}
			PlanetClimateTable table = PlanetClimateTable.For(system, body);
			if (table == null)
			{
				return;
			}
			float kelvin = table.SeasonKelvin(latitude, hours, continentality);
			float waterShift = 0f;
			if (!ReferenceEquals(body, own))
			{
				// Another world under the scene: its year here less the scene's own (the scene's climate already holds that).
				PlanetClimateTable ownTable = own != null ? PlanetClimateTable.For(system, own) : null;
				float ownAnnual = ownTable != null ? ownTable.AnnualKelvin(latitude, continentality) : (float)ClimateModel.ToKelvin(0f);
				kelvin += table.AnnualKelvin(latitude, continentality) - ownAnnual;
				float ownWater = own != null ? CloudWorld.WaterOf(own) : 0.7f;
				waterShift = (CloudWorld.WaterOf(body) - ownWater) * 1.2f;
			}
			temperature = kelvin / (float)ClimateModel.KelvinPerUnit;
			// Warm air holds more, so the same vapour reads drier in summer: the season's share of the humidity.
			humidity = Mathf.Clamp(waterShift - 0.3f * temperature, -2f, 2f);
			cachedBody = body;
			cachedOwn = own;
			cachedHours = hours;
			cachedLatitude = latitude;
			cachedContinentality = continentality;
			cachedTemperature = temperature;
			cachedHumidity = humidity;
		}

		/// <summary>
		/// What foliage shows at a scene now, from its climate rather than the calendar: how far through its thermal year it
		/// is (0 the coldest point, 0.5 the warmest), how warm it is for growing (−1 dormant … 1 growing), and how hard its
		/// winter is (0 none … 1 a winter that strips the trees).
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>From the temperature, not the axial tilt (2026-10-15).</b> The strength was |latitude| ÷ 23.5° — Earth's tilt —
		/// on every body, so a scene at 47° on a 9° world dropped its leaves like Moscow while its winter barely froze, and a
		/// mild coast went as bare as a continent's heart. Plants go dormant below about 5 °C and deciduous trees exist where
		/// winters are cold enough to need to; both are read straight off the climate table here.
		/// </para>
		/// <para>The phase follows the temperature, a month or two behind the sun, as the leaves do.</para>
		/// <para>
		/// <b>Dormancy is steeper than growth (2026-10-16).</b> One ramp of 10 K either side of 5 °C left a +3 °C winter at
		/// −0.2, a seventh of the way to its dry colour: a mild maritime tundra stayed summer-green all year. Grass stops
		/// growing and cures over a few degrees above freezing, so the ramp below 5 °C runs to fully dormant at 1 °C.
		/// </para>
		/// <para>
		/// <b>Any real winter strips a deciduous tree (2026-10-16).</b> The winter was (5 °C − the coldest month) ÷ 10, so a
		/// +3 °C winter stripped a fifth of an oak's leaves; but oak, birch and willow go bare in Ireland's +6 °C winters too.
		/// It is now whole where the coldest month is 5 °C or colder, easing off to none at 12 °C, where broadleaves keep
		/// their leaves through the year.
		/// </para>
		/// </remarks>
		public static bool Foliage(WorldSceneSettings settings, double worldHours, out float phase01, out float growing, out float winter)
		{
			return Foliage(settings, worldHours, out phase01, out growing, out winter, out _);
		}

		/// <summary><see cref="Foliage(WorldSceneSettings, double, out float, out float, out float)"/>, with the air's temperature it read the season from, °C.</summary>
		public static bool Foliage(WorldSceneSettings settings, double worldHours, out float phase01, out float growing, out float winter, out float celsius)
		{
			bool known = Foliage(settings, worldHours, out FoliageState state);
			phase01 = state.Phase01;
			growing = state.Growing;
			winter = state.Winter;
			celsius = state.Celsius;
			return known;
		}

		/// <summary>Plants grow above this, °C, and go dormant below it.</summary>
		public const float GrowthC = 5f;
		/// <summary>Wholly dormant at and below this, °C.</summary>
		public const float DormantC = 1f;
		/// <summary>A coldest month at or below this, °C, strips a deciduous tree.</summary>
		public const float StripC = 5f;
		/// <summary>A coldest month at or above this, °C, keeps broadleaves in leaf all year.</summary>
		public const float LeafyC = 12f;

		/// <summary>How warm it is for growing at <paramref name="celsius"/>: −1 dormant … 1 growing. FishWeather.hlsl FishSeasonGrowing mirrors it.</summary>
		public static float GrowingAt(float celsius)
		{
			return Mathf.Clamp(celsius >= GrowthC ? (celsius - GrowthC) / 10f : (celsius - GrowthC) / (GrowthC - DormantC), -1f, 1f);
		}

		/// <summary>How hard a winter whose coldest month is <paramref name="coldestCelsius"/> is: 0 none … 1 strips a deciduous tree. FishSeasonWinter mirrors it.</summary>
		public static float WinterAt(float coldestCelsius) => Mathf.Clamp01((LeafyC - coldestCelsius) / (LeafyC - StripC));

		/// <summary>
		/// The season for plants at sea level under a scene, and how fast the air cools above it: what the shaders work each
		/// plant's own season out from at its own height (<see cref="FishMMO.Shared.Weather.SceneClimate.GrowingAt"/>).
		/// </summary>
		public struct FoliageState
		{
			/// <summary>How far through its thermal year the place is: 0 its coldest point, 0.5 its warmest.</summary>
			public float Phase01;
			/// <summary><see cref="GrowingAt"/> of <see cref="Celsius"/>: the season at sea level.</summary>
			public float Growing;
			/// <summary><see cref="WinterAt"/> of <see cref="ColdestCelsius"/>.</summary>
			public float Winter;
			/// <summary>The season's air at sea level under the scene now, °C (no day, no weather).</summary>
			public float Celsius;
			/// <summary>The year's coldest point there, °C.</summary>
			public float ColdestCelsius;
			/// <summary>How much colder each metre of world y up is, °C (the body's lapse rate over a cut scene's vertical scale; 0 with no air).</summary>
			public float CoolingPerMetre;

			/// <summary>The air's season at a height (world y, metres above the sea), °C.</summary>
			public float CelsiusAt(float worldY) => Celsius - Mathf.Max(0f, worldY) * CoolingPerMetre;
		}

		/// <summary>
		/// <see cref="Foliage(WorldSceneSettings, double, out float, out float, out float)"/> as a temperature at sea level
		/// and a lapse rate, so each plant reads its own height's season: an alpine meadow flowers weeks after the valley and
		/// goes over weeks before it, and a peak's dwarf willow stands bare while the lowland's is still in leaf.
		/// </summary>
		/// <param name="timeline">The scene's weather timeline, for what is added to its air at runtime; null adds only what is authored.</param>
		public static bool Foliage(WorldSceneSettings settings, double worldHours, out FoliageState state, WeatherTimeline timeline = null)
		{
			state = default;
			SolarSystemProfile system = SolarSystemProfile.Active;
			/* The body the weather is for: the timeline's when the bed stands the scene on another, at its latitude, as the
			 * snowpack and the rest of the weather read it (BodyShareAt); the scene's own otherwise. */
			bool borrowed = timeline != null && timeline.BodyOverride != null;
			WorldBody body = borrowed ? timeline.BodyOverride : SceneTime.BodyOf(settings);
			PlanetClimateTable table = system != null && body != null ? PlanetClimateTable.For(system, body) : null;
			if (table == null || settings == null)
			{
				return false;
			}
			double latitude = borrowed ? timeline.LatitudeDegrees : settings.ClimateLatitude;
			ScenePlacementClimate placement = settings.PlacementClimate;
			float continentality = placement != null ? placement.CentreContinentality : 0.5f;
			// The place's own year at sea level under the scene's centre (a generated scene reads world y as altitude), mean and season.
			// On a borrowed body, that body's own year there: the scene's climate is its home world's.
			float annual = borrowed ? table.AnnualKelvin(latitude, continentality) : settings.SampleClimateAt(Vector3.zero, 0.5f, out _, 0f, 0f).KelvinOrScale;
			float now = annual + table.SeasonKelvin(latitude, worldHours, continentality);
			table.Extremes(latitude, continentality, out float warmest, out float coldest);
			float shift = annual - table.AnnualKelvin(latitude, continentality);
			/* What is added to the air moves the plants' season with everything else it moves (the snowpack, the rain
			 * and snow, the cloud): a scene authored ten kelvin colder had its snow line down and its meadows in flower. */
			float added = (settings.AuthoredAir + (timeline != null ? timeline.AirAtSeconds(worldHours * 3600.0) : default)).Temperature;
			float coldestC = coldest + shift + added - 273.15f;
			float nowC = now + added - 273.15f;
			state.Celsius = nowC;
			state.ColdestCelsius = coldestC;
			state.Growing = GrowingAt(nowC);
			state.Winter = WinterAt(coldestC);
			/* Per metre of world y, which in a scene cut from the globe is the body's altitude times the cut's vertical
			 * scale: the body's lapse alone made every hill cool at a third of the rate its biomes were chosen at. */
			float lapse = (float)ClimateModel.LapseRatePerMetre(system, body);
			state.CoolingPerMetre = placement != null && placement.Generated ? placement.CoolingPerSceneMetre(lapse) : lapse;
			state.Phase01 = Mathf.Repeat((table.PhaseAt(worldHours) - table.ColdestPhase(latitude, continentality)) / PlanetClimateTable.Phases, 1f);
			return true;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetCache()
		{
			cachedBody = null;
			cachedOwn = null;
			cachedHours = double.NaN;
		}
	}
}
