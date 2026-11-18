using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// A body's surface temperature by latitude through its year, in kelvin, for its land and its sea: the
	/// <see cref="SeasonalEnergyBalance"/> solved for the body's own sun path, starlight, land and mean temperature. The
	/// one table every temperature on the body is read from.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A place reads it by its latitude and its continentality (<see cref="MoistureModel.Continentality{T}"/>): 0 takes the
	/// sea column (the sea, and a coast its wind blows onto from the sea), 1 the land column (deep inland), between them a
	/// blend. Its altitude and its regional wobble are the caller's (<see cref="PlanetClimateField"/>).
	/// </para>
	/// <para>
	/// <b>Phase is the body's place in its year</b> (its host planet's, for a moon): 0 at the epoch, the same zero the sun
	/// path is read from, so the runtime season and the table can never drift apart.
	/// </para>
	/// <para>
	/// Built once per body and cached; rebuilt when anything it was built from changes (a designer editing the body).
	/// Thread-safe: the scene's ground is generated off the main thread.
	/// </para>
	/// </remarks>
	public sealed class PlanetClimateTable
	{
		/// <summary>Points round the year the table holds.</summary>
		public const int Phases = SeasonalEnergyBalance.Phases;

		/// <summary>Longitudes sampled round each band for its share of land.</summary>
		public const int LandSamples = 128;

		private readonly float[] land;
		private readonly float[] ocean;
		private readonly float[] landMean = new float[181];
		private readonly float[] oceanMean = new float[181];

		/// <summary>Hours in the year the phases divide (the host's, for a moon); 0 for a body with no year.</summary>
		public readonly double YearHours;

		/// <summary>The world's mean surface temperature the table was solved to, K.</summary>
		public readonly double MeanKelvin;

		/// <summary>What the solve had to shift the table by to reach <see cref="MeanKelvin"/>, K (see <see cref="SeasonalEnergyBalance.Result.Residual"/>).</summary>
		public readonly double Residual;

		private PlanetClimateTable(SeasonalEnergyBalance.Result result, double yearHours, double meanKelvin)
			: this(result.Land, result.Ocean, yearHours, meanKelvin, result.Residual)
		{
		}

		private PlanetClimateTable(float[] land, float[] ocean, double yearHours, double meanKelvin, double residual)
		{
			this.land = land;
			this.ocean = ocean;
			YearHours = yearHours;
			MeanKelvin = meanKelvin;
			Residual = residual;
			for (int latitude = -90; latitude <= 90; latitude++)
			{
				double l = 0.0, o = 0.0;
				for (int p = 0; p < Phases; p++)
				{
					int k = SeasonalEnergyBalance.Result.Index(latitude, p);
					l += land[k];
					o += ocean[k];
				}
				landMean[latitude + 90] = (float)(l / Phases);
				oceanMean[latitude + 90] = (float)(o / Phases);
			}
		}

		// ── Reading ───────────────────────────────────────────────────

		/// <summary>The year's phase at a moment, 0 … <see cref="Phases"/>: where the body is in its year.</summary>
		public float PhaseAt(double hours)
		{
			if (YearHours <= 0.0)
			{
				return 0f;
			}
			double t = hours / YearHours;
			t -= Math.Floor(t);
			return (float)(t * Phases);
		}

		/// <summary>The year's mean at a latitude, K, blended by continentality.</summary>
		public float AnnualKelvin(double latitudeDegrees, float continentality)
		{
			Locate(latitudeDegrees, out int i, out float f);
			float o = oceanMean[i] + (oceanMean[i + 1] - oceanMean[i]) * f;
			float l = landMean[i] + (landMean[i + 1] - landMean[i]) * f;
			return o + (l - o) * Mathf.Clamp01(continentality);
		}

		/// <summary>The temperature at a latitude and a phase of the year, K, blended by continentality.</summary>
		public float KelvinAt(double latitudeDegrees, float phase, float continentality)
		{
			Locate(latitudeDegrees, out int i, out float f);
			float p = phase - Mathf.Floor(phase / Phases) * Phases;
			int p0 = Mathf.Min(Phases - 1, (int)p);
			int p1 = (p0 + 1) % Phases;
			float fp = p - p0;
			float c = Mathf.Clamp01(continentality);
			return Blend(i, p0, p1, f, fp, c);
		}

		/// <summary>How far a moment's temperature at a latitude stands from the year's mean there, K: the season.</summary>
		public float SeasonKelvin(double latitudeDegrees, double hours, float continentality)
		{
			return KelvinAt(latitudeDegrees, PhaseAt(hours), continentality) - AnnualKelvin(latitudeDegrees, continentality);
		}

		/// <summary>The warmest and the coldest point of the year at a latitude, K, blended by continentality.</summary>
		public void Extremes(double latitudeDegrees, float continentality, out float warmest, out float coldest)
		{
			Locate(latitudeDegrees, out int i, out float f);
			float c = Mathf.Clamp01(continentality);
			warmest = float.NegativeInfinity;
			coldest = float.PositiveInfinity;
			for (int p = 0; p < Phases; p++)
			{
				float t = Blend(i, p, p, f, 0f, c);
				warmest = Mathf.Max(warmest, t);
				coldest = Mathf.Min(coldest, t);
			}
		}

		/// <summary>The phase of the year's coldest point at a latitude, 0 … <see cref="Phases"/>, blended by continentality.</summary>
		public float ColdestPhase(double latitudeDegrees, float continentality)
		{
			Locate(latitudeDegrees, out int i, out float f);
			float c = Mathf.Clamp01(continentality);
			int coldest = 0;
			float least = float.PositiveInfinity;
			for (int p = 0; p < Phases; p++)
			{
				float t = Blend(i, p, p, f, 0f, c);
				if (t < least)
				{
					least = t;
					coldest = p;
				}
			}
			return coldest;
		}

		/// <summary>The sea column at a latitude for each of <paramref name="kelvin"/>'s points round the year, K.</summary>
		/// <remarks>What a sea's ice and its surface temperature are read from: <paramref name="kelvin"/>.Length points, evenly round the year from its start.</remarks>
		public void SeaYear(double latitudeDegrees, float[] kelvin)
		{
			for (int s = 0; s < kelvin.Length; s++)
			{
				kelvin[s] = KelvinAt(latitudeDegrees, (float)s * Phases / kelvin.Length, 0f);
			}
		}

		private float Blend(int i, int p0, int p1, float f, float fp, float c)
		{
			int a0 = (i + 0) * Phases, a1 = (i + 1) * Phases;
			float o0 = ocean[a0 + p0] + (ocean[a0 + p1] - ocean[a0 + p0]) * fp;
			float o1 = ocean[a1 + p0] + (ocean[a1 + p1] - ocean[a1 + p0]) * fp;
			float l0 = land[a0 + p0] + (land[a0 + p1] - land[a0 + p0]) * fp;
			float l1 = land[a1 + p0] + (land[a1 + p1] - land[a1 + p0]) * fp;
			float o = o0 + (o1 - o0) * f;
			float l = l0 + (l1 - l0) * f;
			return o + (l - o) * c;
		}

		private static void Locate(double latitudeDegrees, out int index, out float fraction)
		{
			double x = Math.Max(-90.0, Math.Min(90.0, latitudeDegrees)) + 90.0;
			index = Math.Min(179, (int)x);
			fraction = (float)(x - index);
		}

		// ── Building ──────────────────────────────────────────────────

		private static readonly object gate = new object();
		private static readonly Dictionary<WorldBody, (long Key, PlanetClimateTable Table)> cache = new Dictionary<WorldBody, (long, PlanetClimateTable)>();

		/// <summary>
		/// The version of the model the tables are solved with. Raise it whenever anything that changes a solved table changes
		/// — a constant of <see cref="SeasonalEnergyBalance"/>, how the inputs are gathered here, the surface the land share
		/// is read from — and every baked table goes stale and is solved afresh until it is baked again.
		/// </summary>
		public const int ModelVersion = 1;

		/// <summary>
		/// The table for a body; null without one. Read from the body's baked copy (<see cref="WorldBody.ClimateTable"/>) when
		/// it is current, solved otherwise; cached either way.
		/// </summary>
		/// <remarks>Main thread only on first use for a body (it reads the body and its system); read freely after.</remarks>
		public static PlanetClimateTable For(SolarSystemProfile system, WorldBody body)
		{
			if (body == null)
			{
				return null;
			}
			long key = Fingerprint(system, body);
			lock (gate)
			{
				if (cache.TryGetValue(body, out (long Key, PlanetClimateTable Table) entry) && entry.Key == key)
				{
					return entry.Table;
				}
			}
			PlanetClimateTable table = FromBaked(body.ClimateTable, key);
			if (table == null)
			{
				// Never baked, or baked and since changed (or by another model): say so, once a body a session.
				WarnStale(body, body.ClimateTable != null && body.ClimateTable.Fingerprint != 0L);
				table = Solve(system, body);
			}
			lock (gate)
			{
				cache[body] = (key, table);
			}
			return table;
		}

		/// <summary>The table a body's baked copy holds, when it was baked from exactly <paramref name="fingerprint"/>; else null.</summary>
		private static PlanetClimateTable FromBaked(PlanetClimateTableData data, long fingerprint)
		{
			if (data == null || data.Fingerprint != fingerprint || !data.TryUnpack(out float[] land, out float[] ocean))
			{
				return null;
			}
			return new PlanetClimateTable(land, ocean, data.YearHours, data.MeanKelvin, data.Residual);
		}

		/// <summary>The table as a body asset keeps it, with the fingerprint it was solved from.</summary>
		public PlanetClimateTableData ToData(long fingerprint) => PlanetClimateTableData.Of(fingerprint, YearHours, MeanKelvin, Residual, land, ocean);

		private static readonly HashSet<WorldBody> warned = new HashSet<WorldBody>();

		private static void WarnStale(WorldBody body, bool baked)
		{
			lock (gate)
			{
				if (!warned.Add(body))
				{
					return;
				}
			}
			Debug.LogWarning($"[Climate] {body.name}'s climate table {(baked ? "is out of date with the body or the model" : "has never been baked")}, so it was solved now " +
				"(the better part of a second). Bake it: FishMMO Dashboard → World → \"Bake climate tables\".");
		}

		/// <summary>Drops every cached table (a test that rebuilds a system, or a domain that keeps statics).</summary>
		public static void ClearCache()
		{
			lock (gate)
			{
				cache.Clear();
				warned.Clear();
			}
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetOnLoad() => ClearCache();

		/// <summary>Everything the table is built from, folded into one number: an edit to any of it rebuilds the table.</summary>
		public static long Fingerprint(SolarSystemProfile system, WorldBody body)
		{
			unchecked
			{
				long h = 1469598103934665603L ^ ModelVersion;
				void Add(double v)
				{
					h ^= BitConverter.DoubleToInt64Bits(v);
					h *= 1099511628211L;
				}
				Add(body.AxialTiltDegrees);
				Add(body.PoleLongitudeDegrees);
				Add(body.Orbit.Distance);
				Add(body.Orbit.Eccentricity);
				Add(body.Orbit.OffsetDegrees);
				Add(body.Orbit.PeriodDays);
				Add((int)body.Orbit.PeriodMode);
				Add((int)body.Atmosphere);
				Add((int)body.Kind);
				Add(body.Water);
				Add(body.ResolvedTerrainSeed);
				Add(body.SkyRadiusKm);
				// What the system contributes, by value: an identity would differ between sessions and leave every bake stale.
				Add(system != null ? 1.0 : 0.0);
				if (system != null)
				{
					// The suns' light and the home world's distance move the mean and the season of every body.
					foreach (CelestialBody candidate in system.Bodies)
					{
						if (candidate is StarBody star)
						{
							Add(star.Luminosity);
							Add(star.Orbit.Distance);
						}
					}
					if (system.HomeWorld != null)
					{
						Add(system.HomeWorld.Orbit.Distance);
						Add((int)system.HomeWorld.Atmosphere);
					}
				}
				if (body.Parent is WorldBody host)
				{
					Add(host.Orbit.Distance);
					Add(host.Orbit.Eccentricity);
					Add(host.AxialTiltDegrees);
				}
				return h;
			}
		}

		/// <summary>Solves a body's table from its orbit, its suns, its land and its air: what a bake stores. The better part of a second.</summary>
		public static PlanetClimateTable Solve(SolarSystemProfile system, WorldBody body)
		{
			double year = CelestialMath.OrbitHours(system, CelestialMath.HostPlanet(body));
			bool hasYear = !(double.IsNaN(year) || double.IsInfinity(year) || year <= 0.0);
			double meanKelvin = ClimateModel.MeanSurfaceKelvin(system, body);
			bool layer = CloudWorld.Is(body);
			AtmosphereKind atmosphere = CloudWorld.AtmosphereOf(body);
			float water = CloudWorld.WaterOf(body);

			var inputs = new SeasonalEnergyBalance.Inputs
			{
				Declination = new double[SeasonalEnergyBalance.StepsPerYear],
				Flux = new double[SeasonalEnergyBalance.StepsPerYear],
				LandFraction = new double[SeasonalEnergyBalance.Bands],
				TargetMeanKelvin = meanKelvin,
				Diffusion = SeasonalEnergyBalance.DiffusionFor(atmosphere),
				AirCapacity = SeasonalEnergyBalance.AirCapacityFor(atmosphere),
				// A sea for the ocean column wherever there is open liquid to be one; lava is ground, not sea.
				HasSea = layer || (water > 0f && SurfaceLiquids.For(system, body) != SurfaceLiquid.Lava),
				// Snow wants water and air to carry it; a wetter world snows more, a puddle world little.
				SnowfallMetres = atmosphere == AtmosphereKind.None ? 0.0
					: SeasonalEnergyBalance.EarthSnowfallMetres * Mathf.Clamp(water / 0.7f, 0f, 1.5f),
			};

			double meanFlux = 0.0;
			for (int s = 0; s < SeasonalEnergyBalance.StepsPerYear; s++)
			{
				double hours = hasYear ? year * s / SeasonalEnergyBalance.StepsPerYear : 0.0;
				double declination = 0.0;
				if (system != null && !layer)
				{
					CelestialMath.SunEquatorial(system, body, hours, out _, out declination);
				}
				inputs.Declination[s] = declination;
				inputs.Flux[s] = SeasonalEnergyBalance.SolarConstant * (system != null ? CelestialMath.Insolation(system, body, hours) : 1.0);
				meanFlux += inputs.Flux[s];
			}
			if (layer)
			{
				// A giant's habitable layer is warmed from within (CloudWorld): no seasons from the orbit either.
				meanFlux /= SeasonalEnergyBalance.StepsPerYear;
				for (int s = 0; s < SeasonalEnergyBalance.StepsPerYear; s++)
				{
					inputs.Flux[s] = meanFlux;
				}
			}

			// Each band's share of land, from the surface itself: a polar continent and a polar sea are different worlds.
			uint seed = body.ResolvedTerrainSeed;
			for (int i = 0; i < SeasonalEnergyBalance.Bands; i++)
			{
				if (layer || !inputs.HasSea)
				{
					inputs.LandFraction[i] = layer ? 0.0 : 1.0;
					continue;
				}
				double x = -1.0 + (i + 0.5) * 2.0 / SeasonalEnergyBalance.Bands;
				double latitude = Math.Asin(x) * 180.0 / Math.PI;
				int count = 0;
				for (int j = 0; j < LandSamples; j++)
				{
					double longitude = -180.0 + (j + 0.5) * 360.0 / LandSamples;
					if (PlanetSurface.AltitudeMetresAt(seed, body, latitude, longitude) > 0f)
					{
						count++;
					}
				}
				inputs.LandFraction[i] = (double)count / LandSamples;
			}

			SeasonalEnergyBalance.Result result = SeasonalEnergyBalance.Solve(inputs);
			return new PlanetClimateTable(result, hasYear ? year : 0.0, meanKelvin);
		}
	}
}
