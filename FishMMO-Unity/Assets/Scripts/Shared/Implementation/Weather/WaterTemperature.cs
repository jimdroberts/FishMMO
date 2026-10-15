using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// How warm open water is: the sea's surface, and a lake's or a river's — and whether a lake has
	/// frozen over.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Water is the slow half of the climate.</b> The sea's mixed layer holds some fifty times the heat
	/// of the air column over it, so its surface follows the year's mean air, not the day's or the week's
	/// weather — the editor's ice placement takes it as exactly that, the annual mean of the sea-level air
	/// never below sea water's freezing point (IcePlacer.SeaSurfaceC). Here the season is added on top,
	/// damped and late: the mid-latitude ocean's surface swings over about a third of the air's annual
	/// range, peaking some two months after the sun does (late August against late June: the NOAA OISST
	/// climatology), so a winter sea is still warm from the summer, which is when sea smoke happens. The
	/// game's air carries no lag of its own — its season is the sun's noon height on the day
	/// (<see cref="CelestialMath.SeasonalClimateOffsets"/>) — so the lag is measured from the sun.
	/// </para>
	/// <para>
	/// <b>Lakes and rivers</b> hold far less heat and follow the air much more closely: four fifths of the
	/// air's swing, a month late (a temperate lake's surface turns over in October and freezes in December,
	/// a few weeks behind the air). Fresh water freezes at 0 °C, and once the surface model is at or below
	/// it the lake is ice — no steam off it, however cold the air. A river freezes later than a lake for its
	/// turbulence; that difference is left out.
	/// </para>
	/// <para>
	/// <b>The sea freezes too</b>, though it never cools below −1.8 °C: below that the air builds pack ice
	/// over a ramp of a few kelvin (IcePlacer's <c>PackRampKelvin</c>), and only the leads between the
	/// floes are open water to smoke.
	/// </para>
	/// <para>
	/// <b>Where the air comes from.</b> The annual mean is the scene's own climate at the water — the very
	/// field its biomes were painted from, with the altitude lapse for a mountain lake
	/// (<see cref="WorldSceneSettings.SampleClimateAt(Vector3, float, out float)"/>, via the biome sampler)
	/// — less what the runtime adds to it (<see cref="WorldSceneSettings.RuntimeTemperatureOffset"/>: the
	/// season, and the runtime air an admin or an event adds), plus back the scene's authored air, which is
	/// its climate for good. The weather's air-mass warmth is never in it: a cold outbreak over warm water is
	/// the whole of steam fog. Pure in the scene, the place and the hour, so server and client agree.
	/// </para>
	/// </remarks>
	public static class WaterTemperature
	{
		private const float Kelvin = 273.15f;

		/// <summary>Sea water's freezing point at a salinity of 35 g/kg, °C (IcePlacer's twin).</summary>
		public const float SeaWaterFreezingC = -1.8f;
		/// <summary>Fresh water's, °C.</summary>
		public const float FreshWaterFreezingC = 0f;
		/// <summary>How far below sea water's freezing point the air takes the sea to closed pack ice, K (IcePlacer's twin).</summary>
		public const float PackRampKelvin = 6f;
		/// <summary>The share of the air's seasonal swing the sea's surface follows.</summary>
		public const float SeaSeasonalShare = 1f;
		/// <summary>How far behind the sun the sea's season runs, years.</summary>
		public const float SeaLagYears = 0f;
		/// <summary>The share of the air's seasonal swing a lake's or a river's surface follows.</summary>
		public const float InlandSeasonalShare = 0.8f;
		/// <summary>How far behind the sun a lake's season runs, years.</summary>
		public const float InlandLagYears = 1f / 24f;

		/// <summary>The sea's surface, °C, from the annual mean sea-level air and the air's season as it was <see cref="SeaLagYears"/> ago (°C off the mean).</summary>
		public static float SeaSurfaceC(float annualMeanAirC, float laggedSeasonC)
		{
			return Mathf.Max(SeaWaterFreezingC, SeaRawC(annualMeanAirC, laggedSeasonC));
		}

		/// <summary>How much of the sea is open water rather than pack ice, 0..1.</summary>
		public static float SeaOpenWater(float annualMeanAirC, float laggedSeasonC)
		{
			return 1f - Mathf.Clamp01((SeaWaterFreezingC - SeaRawC(annualMeanAirC, laggedSeasonC)) / PackRampKelvin);
		}

		/// <summary>A lake's or a river's surface, °C, from the annual mean air there and the air's season as it was <see cref="InlandLagYears"/> ago; never below freezing.</summary>
		public static float InlandSurfaceC(float annualMeanAirC, float laggedSeasonC)
		{
			return Mathf.Max(FreshWaterFreezingC, InlandRawC(annualMeanAirC, laggedSeasonC));
		}

		/// <summary>Whether a lake or a river has frozen over: its surface would be at or below 0 °C.</summary>
		public static bool InlandFrozen(float annualMeanAirC, float laggedSeasonC)
		{
			return InlandRawC(annualMeanAirC, laggedSeasonC) <= FreshWaterFreezingC;
		}

		private static float SeaRawC(float annualMeanAirC, float laggedSeasonC) => annualMeanAirC + SeaSeasonalShare * laggedSeasonC;
		private static float InlandRawC(float annualMeanAirC, float laggedSeasonC) => annualMeanAirC + InlandSeasonalShare * laggedSeasonC;

		/// <summary>The water at one place and hour.</summary>
		public struct Reading
		{
			/// <summary>The annual mean air at the water, °C.</summary>
			public float AnnualAirC;
			/// <summary>The sea's surface, °C, and how much of it is open water (0..1).</summary>
			public float SeaC, SeaOpen;
			/// <summary>A lake's or a river's surface, °C, and whether it is frozen.</summary>
			public float InlandC;
			public bool InlandFrozen;
		}

		/// <summary>
		/// The water at a scene now: its sea's surface and its lakes', from the body's climate table — the sea column for the
		/// sea, the place's own blend for inland water — with the scene's authored air.
		/// </summary>
		/// <remarks>
		/// The table's sea column already IS a sea surface: its mixed layer's heat capacity damps the season to the
		/// ocean's and lags it two months behind the sun, so the sea reads it whole (<see cref="SeaSeasonalShare"/> 1, no
		/// lag of its own). A lake reads the ground's own column a little later and a little damped.
		/// </remarks>
		public static Reading At(WorldSceneSettings settings, in PlanetAir planet, Vector3 seaSurface, Vector3 inland, double worldHours)
		{
			float authoredK = (settings != null ? settings.AuthoredAir.TemperatureScale : 0f) * (float)ClimateModel.KelvinPerUnit;
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = SceneTime.BodyOf(settings);
			PlanetClimateTable table = system != null && body != null ? PlanetClimateTable.For(system, body) : null;
			ScenePlacementClimate placement = settings != null ? settings.PlacementClimate : null;

			float seaAnnualK, inlandAnnualK, seaSeasonK = 0f, inlandSeasonK = 0f;
			if (placement != null)
			{
				seaAnnualK = placement.AnnualKelvinAt(new Vector3(seaSurface.x, 0f, seaSurface.z), 0f);
				float inlandContinentality = placement.ContinentalityAt(inland.x, inland.z);
				// The ground's own year at the water, with its altitude: a mountain tarn is colder than a valley pond.
				inlandAnnualK = BiomeSampler.Read(inland, settings, 0f, 0f).Climate.KelvinOrScale;
				if (table != null)
				{
					double seaLatitude = settings.LatitudeAt(seaSurface);
					double inlandLatitude = settings.LatitudeAt(inland);
					seaSeasonK = table.SeasonKelvin(seaLatitude, worldHours - SeaLagYears * table.YearHours, 0f);
					inlandSeasonK = table.SeasonKelvin(inlandLatitude, worldHours - InlandLagYears * table.YearHours, inlandContinentality);
				}
			}
			else
			{
				// No place on any body: the scene's own climate, unmoved by any season.
				seaAnnualK = settings != null ? settings.SampleClimateAt(seaSurface, ClimateModel.DefaultWaterSurfaceHeight, out _, 0f, 0f).KelvinOrScale
					: BiomeSampler.Read(seaSurface, null).Climate.KelvinOrScale;
				inlandAnnualK = BiomeSampler.Read(inland, settings, 0f, 0f).Climate.KelvinOrScale;
			}
			float seaAnnualC = seaAnnualK + authoredK - Kelvin;
			float inlandAnnualC = inlandAnnualK + authoredK - Kelvin;
			return new Reading
			{
				AnnualAirC = seaAnnualC,
				SeaC = SeaSurfaceC(seaAnnualC, seaSeasonK),
				SeaOpen = SeaOpenWater(seaAnnualC, seaSeasonK),
				InlandC = InlandSurfaceC(inlandAnnualC, inlandSeasonK),
				InlandFrozen = InlandFrozen(inlandAnnualC, inlandSeasonK),
			};
		}
	}
}
