using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Weather
{
	/// <summary>What a world's clouds condense out of.</summary>
	/// <remarks>
	/// Whichever vapour its air can carry at the ground and must give up aloft. Our own sky is water;
	/// a world as cold as the outer moons has methane or nitrogen doing the same job, and a runaway
	/// greenhouse has drops of acid. The physics is the same for all of them — only the constants
	/// change — which is why this is a table and not five cloud systems.
	/// </remarks>
	public enum Condensate : byte
	{
		Water = 0,
		Ammonia = 1,
		Methane = 2,
		Nitrogen = 3,
		SulphuricAcid = 4,
	}

	/// <summary>
	/// The thermodynamics of a planet's air: how fast it cools as it rises, where the water in it
	/// condenses, where it freezes, and how far up the weather goes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Everything a cloud's shape rests on follows from four facts about the air — how hard the
	/// world pulls on it, what it is made of, how warm it is at the ground and how much vapour it
	/// holds. Rising air cools at <c>g / cp</c> until it reaches its dew point; that height is the
	/// cloud's flat base. Above it the vapour condenses as it goes, the heat it gives up slows the
	/// cooling, and whether the parcel keeps rising depends on whether the air around it cools
	/// faster still. That comparison is the difference between a flat deck, a field of heaps and a
	/// thunderhead, and it is made here from the numbers rather than chosen by a designer.
	/// </para>
	/// <para>
	/// SI units throughout: kelvin, pascals, metres, kilograms. The game's own climate scale (−1…1,
	/// 0 at freezing) meets this through <see cref="ClimateModel.ToKelvin"/>.
	/// </para>
	/// </remarks>
	public static class AirPhysics
	{
		/// <summary>Our own air's pressure at sea level, Pa: the "1" an atmosphere's amount scales.</summary>
		public const float StandardPressure = 101325f;

		/// <summary>The driver's humidity (0 parched … 1 saturated) as a relative humidity.</summary>
		/// <remarks>
		/// The field's humidity is described as 0 dry and 1 saturated, and saturated is a relative
		/// humidity of exactly one. Dry is not zero — the driest desert air carries a tenth of what it
		/// could — so the floor is a desert's, not nothing, which would put the dew point at absolute zero.
		/// </remarks>
		public static float RelativeHumidity(float humidity01) => Mathf.Lerp(0.12f, 1f, Mathf.Clamp01(humidity01));

		// ── The condensates ─────────────────────────────────────────────

		/// <summary>Latent heat of vaporisation, J/kg.</summary>
		public static float LatentHeat(Condensate c)
		{
			switch (c)
			{
				case Condensate.Ammonia: return 1.371e6f;
				case Condensate.Methane: return 5.10e5f;
				case Condensate.Nitrogen: return 1.99e5f;
				case Condensate.SulphuricAcid: return 5.9e5f;
				default: return 2.501e6f;
			}
		}

		/// <summary>The vapour's own gas constant, J/(kg·K): the universal constant over its molar mass.</summary>
		public static float VapourGasConstant(Condensate c)
		{
			switch (c)
			{
				case Condensate.Ammonia: return 488.2f;
				case Condensate.Methane: return 518.3f;
				case Condensate.Nitrogen: return 296.8f;
				case Condensate.SulphuricAcid: return 84.8f;
				default: return 461.5f;
			}
		}

		/// <summary>Where the condensate freezes, K: its triple point.</summary>
		public static float FreezingKelvin(Condensate c)
		{
			switch (c)
			{
				case Condensate.Ammonia: return 195.4f;
				case Condensate.Methane: return 90.69f;
				case Condensate.Nitrogen: return 63.15f;
				case Condensate.SulphuricAcid: return 283.5f;
				default: return 273.16f;
			}
		}

		/// <summary>The vapour pressure at the triple point, Pa: where the saturation curve is anchored.</summary>
		private static float TriplePressure(Condensate c)
		{
			switch (c)
			{
				case Condensate.Ammonia: return 6060f;
				case Condensate.Methane: return 11696f;
				case Condensate.Nitrogen: return 12520f;
				case Condensate.SulphuricAcid: return 1f;
				default: return 611.657f;
			}
		}

		/// <summary>
		/// Where a droplet freezes on its own, with nothing to freeze onto, K.
		/// </summary>
		/// <remarks>
		/// Water stays liquid well below its freezing point in the open air — clouds of supercooled
		/// drops at −20 °C are ordinary — and only freezes outright near −40 °C. That is the cirrus
		/// level: above it every cloud is ice, which is why the highest cloud is fibrous and silky
		/// rather than heaped. The others are given the same forty kelvin of supercooling.
		/// </remarks>
		public static float HomogeneousFreezeKelvin(Condensate c) => c == Condensate.Water ? 233.15f : FreezingKelvin(c) - 40f;

		/// <summary>Density of the condensed liquid, kg/m³.</summary>
		public static float LiquidDensity(Condensate c)
		{
			switch (c)
			{
				case Condensate.Ammonia: return 682f;
				case Condensate.Methane: return 423f;
				case Condensate.Nitrogen: return 807f;
				case Condensate.SulphuricAcid: return 1830f;
				default: return 1000f;
			}
		}

		/// <summary>
		/// What condenses in a world's sky, from how warm its ground is on average.
		/// </summary>
		/// <remarks>
		/// The vapour that can exist at the surface and must condense somewhere above it. Water down
		/// to about 190 K — Mars, at 210, has thin water-ice cloud — then ammonia, then methane, then
		/// nitrogen for the coldest; and past 400 K, where water will not condense anywhere in a
		/// greenhouse atmosphere, acid.
		/// </remarks>
		public static Condensate CondensateFor(double meanSurfaceKelvin)
		{
			if (meanSurfaceKelvin >= 400.0) return Condensate.SulphuricAcid;
			if (meanSurfaceKelvin >= 190.0) return Condensate.Water;
			if (meanSurfaceKelvin >= 130.0) return Condensate.Ammonia;
			if (meanSurfaceKelvin >= 72.0) return Condensate.Methane;
			return Condensate.Nitrogen;
		}

		// ── Vapour ──────────────────────────────────────────────────────

		/// <summary>Saturation vapour pressure, Pa (Clausius–Clapeyron from the triple point).</summary>
		public static float SaturationPressure(float kelvin, Condensate c)
		{
			float t = Mathf.Max(1f, kelvin);
			float exponent = LatentHeat(c) / VapourGasConstant(c) * (1f / FreezingKelvin(c) - 1f / t);
			return TriplePressure(c) * Mathf.Exp(Mathf.Clamp(exponent, -80f, 30f));
		}

		/// <summary>How many kilograms of vapour a kilogram of saturated air holds.</summary>
		public static float SaturationMixingRatio(float kelvin, float pressure, float airGasConstant, Condensate c)
		{
			float e = SaturationPressure(kelvin, c);
			float epsilon = airGasConstant / VapourGasConstant(c);
			return epsilon * e / Mathf.Max(1f, pressure - Mathf.Min(e, pressure * 0.5f));
		}

		/// <summary>The dew point, K: the temperature at which this air would be saturated.</summary>
		public static float DewPoint(float kelvin, float relativeHumidity, Condensate c)
		{
			float rh = Mathf.Clamp(relativeHumidity, 0.02f, 1f);
			float inverse = 1f / Mathf.Max(1f, kelvin) - VapourGasConstant(c) / LatentHeat(c) * Mathf.Log(rh);
			return Mathf.Min(kelvin, 1f / inverse);
		}

		/// <summary>How fast dry air cools as it rises, K/m: its weight over its heat capacity.</summary>
		public static float DryLapse(float gravity, float specificHeat) => gravity / Mathf.Max(1f, specificHeat);

		/// <summary>
		/// How fast the dew point of rising unsaturated air falls, K/m.
		/// </summary>
		/// <remarks>
		/// The vapour's share of the air is fixed while it rises, so its partial pressure falls with
		/// the pressure, and the dew point follows that down the saturation curve. On our own world
		/// this is about 1.8 K/km against the dry air's 9.8, which is where the old rule of 125 m of
		/// cloud base per degree of dew-point spread comes from — and on a world with a different pull
		/// or a different vapour it comes out different, which is the point of working it out.
		/// </remarks>
		public static float DewPointLapse(float gravity, float kelvin, float dewPoint, float airGasConstant, Condensate c)
		{
			float td = Mathf.Max(1f, dewPoint);
			return gravity / (airGasConstant * Mathf.Max(1f, kelvin)) * VapourGasConstant(c) * td * td / LatentHeat(c);
		}

		/// <summary>
		/// How fast saturated air cools as it rises, K/m: slower than dry, because condensing gives up heat.
		/// </summary>
		public static float MoistLapse(float gravity, float kelvin, float pressure, float specificHeat, float airGasConstant, Condensate c)
		{
			float t = Mathf.Max(1f, kelvin);
			float r = SaturationMixingRatio(t, pressure, airGasConstant, c);
			float l = LatentHeat(c);
			float numerator = 1f + l * r / (airGasConstant * t);
			float denominator = specificHeat + l * l * r * (airGasConstant / VapourGasConstant(c)) / (airGasConstant * t * t);
			return Mathf.Min(DryLapse(gravity, specificHeat), gravity * numerator / Mathf.Max(1f, denominator));
		}

		/// <summary>The height over which the air thins by a factor of e, m.</summary>
		public static float ScaleHeight(float gravity, float kelvin, float airGasConstant) => airGasConstant * Mathf.Max(1f, kelvin) / Mathf.Max(0.01f, gravity);

		// ── Fog and haze ────────────────────────────────────────────────

		/// <summary>
		/// The light a cloud of drops takes out per metre, from how much liquid it holds and how many
		/// drops that is shared between.
		/// </summary>
		/// <remarks>
		/// Each drop removes about twice its own cross-section of light (the extinction paradox), so a
		/// cloud's extinction is <c>2πNr²</c>. For a fixed number of drops their size follows the
		/// water's cube root, which makes extinction go as the two-thirds power of the water: the
		/// same water shared among more, smaller drops is more opaque. That is why a polluted cloud is
		/// brighter and a clean maritime one darker, and it is all this needs to know.
		/// </remarks>
		/// <param name="liquidWater">Condensed water, kg/m³.</param>
		/// <param name="dropsPerCubicMetre">Droplet number concentration, 1/m³.</param>
		public static float DropletExtinction(float liquidWater, float dropsPerCubicMetre, float liquidDensity)
		{
			if (liquidWater <= 0f)
			{
				return 0f;
			}
			float n = Mathf.Max(1e6f, dropsPerCubicMetre);
			float r3 = 3f * liquidWater / (4f * Mathf.PI * Mathf.Max(1f, liquidDensity) * n);
			float r = Mathf.Pow(r3, 1f / 3f);
			return 2f * Mathf.PI * n * r * r;
		}

		/// <summary>
		/// A fog's extinction, 1/m, from the weather's fog channel.
		/// </summary>
		/// <remarks>
		/// The channel is how much fog, 0..1. A fog forms as the air nears saturation, and the nearer
		/// it gets the more of its haze grains swell into drops and the more water each holds: the
		/// drops' number goes as the channel to the 1.5 and their water as its 3.75, so the extinction
		/// goes as its cube (<see cref="FogExponent"/>). A full fog is the densest there is — 1.1 g/m³ in
		/// seven hundred drops a cubic centimetre, a sea fog or a valley's thickest, about 17 m of
		/// visibility; three quarters is a dense fog of some 40 m; half a thick fog of 140 m; a third
		/// the edge of fog, a kilometre; the humid noon's sixth still a mist you see four kilometres
		/// through. It was 0.3 g/m³ in two hundred drops at the top, so the thickest fog the weather
		/// could make was 60 m and half the channel 300 m: moderate fog, which barely touches ground
		/// fifty metres off while the horizon goes white — fog that never seemed to reach the camera.
		/// (Visibility classes: dense fog under 50 m, thick under 200, moderate to 500, fog to 1 km.)
		/// </remarks>
		public static float FogExtinction(float fog01)
		{
			float f = Mathf.Clamp01(fog01);
			if (f <= 0f)
			{
				return 0f;
			}
			return DropletExtinction(1.1e-3f * Mathf.Pow(f, 3.75f), Mathf.Max(1e6f, 7e8f * Mathf.Pow(f, 1.5f)), 1000f);
		}

		/// <summary>
		/// The power of the fog channel a fog's extinction goes as (<see cref="FogExtinction"/>): drops'
		/// number as f^1.5 and water as f^3.75 give n^⅓·W^⅔ = f^3. What a thinned fog keeps is worked
		/// back through it (FogLayer.Stirred).
		/// </summary>
		public const float FogExponent = 3f;

		/// <summary>
		/// What falling precipitation takes out of the view, 1/m, from the precipitation channel and
		/// what is falling.
		/// </summary>
		/// <remarks>
		/// The channel's full scale is a downpour of about 50 mm/h, and it goes as the square: a third
		/// is ordinary rain. Rain thins the view as the 0.63 power of its rate (a downpour leaves a
		/// kilometre); snow, whose flakes are far larger for their water, about five times as much;
		/// hail is big and sparse; blown sand and ash are what a haboob and an ash fall are.
		/// </remarks>
		public static float PrecipitationExtinction(float precipitation01, float rain, float snow, float hail, float ash, float sand)
		{
			float p = Mathf.Clamp01(precipitation01);
			if (p <= 0f)
			{
				return 0f;
			}
			float rate = 50f * p * p;
			float rainPerMetre = 0.29e-3f * Mathf.Pow(rate, 0.63f);
			return rainPerMetre * (rain + 5f * snow + 0.5f * hail)
				+ p * (0.03f * ash + 0.04f * sand);
		}

		/// <summary>
		/// How far through clear air the far side of the sky turns to haze, m: two e-foldings of it.
		/// </summary>
		/// <remarks>
		/// The air scatters light as molecules (Rayleigh, fixed by how much air there is) and as the
		/// dust and salt in it, whose grains swell with water as the humidity rises — which is why a
		/// humid summer day is milky and the air after a cold front is glass. The sky's haze curve
		/// reaches its full strength at two e-foldings, so that is the distance handed over; at an
		/// ordinary humidity under our own air it comes to about 26 km.
		/// </remarks>
		public static float HazeDistance(float airDensityRelative, float relativeHumidity)
		{
			float rh = Mathf.Clamp(relativeHumidity, 0f, 0.97f);
			float molecules = 1.16e-5f;
			float aerosol = 4e-5f * Mathf.Pow(1f - rh, -0.6f);
			float extinction = Mathf.Max(1e-7f, Mathf.Max(0.02f, airDensityRelative) * (molecules + aerosol));
			return 2f / extinction;
		}
	}

	/// <summary>
	/// A world's air as a physical thing: what it weighs, what it is made of, how warm it is on
	/// average and what its clouds are. Fixed for a world; the weather varies on top of it.
	/// </summary>
	public struct PlanetAir
	{
		/// <summary>Whether there is any air at all.</summary>
		public bool HasAir;
		/// <summary>Surface gravity, m/s².</summary>
		public float Gravity;
		/// <summary>Specific heat of the air at constant pressure, J/(kg·K).</summary>
		public float SpecificHeat;
		/// <summary>The air's gas constant, J/(kg·K).</summary>
		public float GasConstant;
		/// <summary>Pressure at the ground, Pa.</summary>
		public float SurfacePressure;
		/// <summary>How much air there is against our own, for everything that scales with it.</summary>
		public float AirRelative;
		/// <summary>The globe's mean surface temperature, K.</summary>
		public float MeanSurfaceKelvin;
		/// <summary>
		/// The temperature the upper air settles to, K: where the weather stops.
		/// </summary>
		/// <remarks>
		/// A planet radiates to space from high in its air, at its equilibrium temperature; the
		/// thin air above that level, heated from below by what it absorbs, settles at the fourth
		/// root of a half of it — the "skin temperature". The weather lives in the layer between the
		/// warm ground and that cold ceiling, and the tropopause is where the air, cooling as it
		/// rises, reaches it.
		/// </remarks>
		public float SkinKelvin;
		/// <summary>What the clouds are made of.</summary>
		public Condensate Condensate;
		/// <summary>How much of the surface is ocean, 0..1: where cloud drops are few and large.</summary>
		public float Water;
		/// <summary>Cloud droplets per cubic metre: many over land, fewer over the sea.</summary>
		public float DropletsPerCubicMetre;
		/// <summary>How fast the world turns, rad/s (positive prograde).</summary>
		public float RotationRate;
		/// <summary>Whether it turns backwards, which turns every wind belt round.</summary>
		public bool Retrograde;
		/// <summary>Radius, m.</summary>
		public float RadiusMetres;

		/// <summary>Our own world's air, for anything with no body to ask about.</summary>
		public static PlanetAir Earthlike => new PlanetAir
		{
			HasAir = true,
			Gravity = SurfacePhysics.EarthGravity,
			SpecificHeat = 1004f,
			GasConstant = 287.05f,
			SurfacePressure = AirPhysics.StandardPressure,
			AirRelative = 1f,
			MeanSurfaceKelvin = 288f,
			SkinKelvin = 214f,
			Condensate = Condensate.Water,
			Water = 0.7f,
			DropletsPerCubicMetre = 1.6e8f,
			RotationRate = 7.2921e-5f,
			Retrograde = false,
			RadiusMetres = 6.371e6f,
		};

		private static readonly Dictionary<WorldBody, PlanetAir> cache = new Dictionary<WorldBody, PlanetAir>();

		/// <summary>A body's air, worked out once and kept: nothing in it changes while the game runs.</summary>
		public static PlanetAir For(SolarSystemProfile system, WorldBody body)
		{
			if (body == null)
			{
				return Earthlike;
			}
			if (cache.TryGetValue(body, out PlanetAir known))
			{
				return known;
			}
			PlanetAir air = Compute(system, body);
			cache[body] = air;
			return air;
		}

		/// <summary>Forgets every body's air: for tools that edit a body while the game runs.</summary>
		public static void ClearCache() => cache.Clear();

		/// <summary>Works a body's air out afresh, without the cache.</summary>
		public static PlanetAir Compute(SolarSystemProfile system, WorldBody body)
		{
			if (body == null)
			{
				return Earthlike;
			}
			AtmosphereKind kind = body.Atmosphere;
			float relative = AtmosphereModel.Density(kind);
			var air = new PlanetAir
			{
				HasAir = kind != AtmosphereKind.None,
				Gravity = Mathf.Max(0.05f, SurfacePhysics.Gravity(body)),
				AirRelative = relative,
				SurfacePressure = AirPhysics.StandardPressure * Mathf.Max(0.001f, relative),
				Water = Mathf.Clamp01(body.Water),
				RadiusMetres = Mathf.Max(1000f, body.SkyRadiusKm * 1000f),
				Retrograde = body.Retrograde,
			};

			/* What the air is made of decides how much heat a kilogram of it holds and how heavy a
			 * mole of it is. A gas giant is hydrogen and helium — twelve times the heat capacity, so
			 * it cools five times more slowly as it rises for the same pull. A thin world and a
			 * runaway greenhouse are carbon dioxide, as Mars and Venus are. Anything else is the
			 * nitrogen air we know. */
			if (body.Kind == WorldBodyKind.GasGiant)
			{
				air.SpecificHeat = 12300f;
				air.GasConstant = 3615f;
			}
			else if (kind == AtmosphereKind.Thin || kind == AtmosphereKind.Thick)
			{
				air.SpecificHeat = 850f;
				air.GasConstant = 188.9f;
			}
			else
			{
				air.SpecificHeat = 1004f;
				air.GasConstant = 287.05f;
			}

			double insolation = system != null ? CelestialMath.Insolation(system, body, 0.0) : 1.0;
			// The mean over the orbit, not the moment: this is what the world is, not what it is doing.
			if (system != null)
			{
				double period = CelestialMath.OrbitHours(system, body);
				if (!double.IsInfinity(period) && !double.IsNaN(period) && period > 0.0)
				{
					double sum = 0.0;
					const int Samples = 8;
					for (int i = 0; i < Samples; i++)
					{
						sum += CelestialMath.Insolation(system, body, period * i / Samples);
					}
					insolation = sum / Samples;
				}
			}
			double mean = ClimateModel.MeanSurfaceKelvin(insolation, kind, body.Water);
			double greenhouse = ClimateModel.Greenhouse(kind, body.Water);
			double equilibrium = Math.Max(20.0, mean - greenhouse);
			air.MeanSurfaceKelvin = (float)mean;
			air.SkinKelvin = (float)(equilibrium * Math.Pow(0.5, 0.25));
			air.Condensate = AirPhysics.CondensateFor(mean);
			// Continental air is full of the dust and salt drops condense on; the open ocean's is clean.
			air.DropletsPerCubicMetre = Mathf.Lerp(3e8f, 1e8f, air.Water);

			/* A tidally locked world turns once an orbit, which for most is so slowly that it may as
			 * well not turn at all: one cell of air from the hot side to the cold. */
			double hours = body.TidallyLocked && system != null ? CelestialMath.OrbitHours(system, body) : body.RotationHours;
			hours = double.IsInfinity(hours) || double.IsNaN(hours) || hours <= 0.0 ? 1e6 : hours;
			air.RotationRate = (float)(2.0 * Math.PI / (hours * 3600.0));
			return air;
		}

		/// <summary>
		/// A place's temperature in kelvin, from the game's climate scale.
		/// </summary>
		/// <remarks>
		/// The scale runs 240 K to 306 K and clamps there, which holds every world a person could
		/// stand on in shirtsleeves and none of the others. A world whose mean is off the scale is
		/// taken from its own mean instead, with whatever the scale does say added as an anomaly —
		/// so a methane moon at 94 K is 94 K and not a clamped 240.
		/// </remarks>
		public float SurfaceKelvin(float climateScale)
		{
			double meanScale = ClimateModel.ToScaleUnclamped(MeanSurfaceKelvin);
			if (meanScale >= -0.9 && meanScale <= 0.9)
			{
				return (float)ClimateModel.ToKelvin(Mathf.Clamp(climateScale, -1f, 1f));
			}
			float clampedMean = Mathf.Clamp((float)meanScale, -1f, 1f);
			return Mathf.Max(20f, MeanSurfaceKelvin + (Mathf.Clamp(climateScale, -1f, 1f) - clampedMean) * (float)ClimateModel.KelvinPerUnit);
		}

		/// <summary>The air's scale height at its mean temperature, m.</summary>
		public float ScaleHeight => AirPhysics.ScaleHeight(Gravity, MeanSurfaceKelvin, GasConstant);
	}

	/// <summary>
	/// The air over one place, from the ground to the top of the weather: where its cloud base is,
	/// how far a cloud can grow, where it freezes. Worked out from the temperature, the humidity, the
	/// pressure pattern and the stability of the air, on the world it is on.
	/// </summary>
	public struct AirColumn
	{
		/// <summary>Temperature at the ground, K.</summary>
		public float SurfaceKelvin;
		/// <summary>Dew point at the ground, K.</summary>
		public float DewPointKelvin;
		/// <summary>Relative humidity at the ground, 0..1.</summary>
		public float RelativeHumidity;
		/// <summary>How fast dry air cools as it rises here, K/m.</summary>
		public float DryLapse;
		/// <summary>How fast saturated air cools as it rises from the cloud base, K/m.</summary>
		public float MoistLapse;
		/// <summary>How fast the air around a rising parcel actually cools, K/m. Past the moist lapse the air is unstable.</summary>
		public float EnvironmentLapse;
		/// <summary>The lapse the column's heights are measured with: the settled, average profile, K/m.</summary>
		public float MeanLapse;
		/// <summary>How far the air stands from neutral: −0.4 very stable … 0.95 on the point of overturning.</summary>
		public float Instability;
		/// <summary>The cloud base: where rising air reaches its dew point, m above the ground.</summary>
		public float Base;
		/// <summary>Where a parcel lifted past the base starts to rise on its own, m; infinity in stable air.</summary>
		public float FreeConvection;
		/// <summary>The lid a high puts on the air — its subsidence inversion — m; the tropopause under a low.</summary>
		public float Cap;
		/// <summary>The top of the weather, m.</summary>
		public float Tropopause;
		/// <summary>The top of an ordinary cloud here: a deck's, or a heap's, m.</summary>
		public float Top;
		/// <summary>How high a tower here can climb, m: the tropopause if deep convection can break through, else the ordinary top.</summary>
		public float TowerCeiling;
		/// <summary>The freezing level, m.</summary>
		public float Freezing;
		/// <summary>Where every cloud is ice, m: the cirrus level.</summary>
		public float IceLevel;
		/// <summary>How vigorous the convection is, 0 a flat deck … 1 boiling towers: what makes a cloud heaped and bubbly.</summary>
		public float Vigour;
		/// <summary>Convective available potential energy, J/kg.</summary>
		public float Cape;
		/// <summary>How fast the updraughts in a tower here rise, m/s.</summary>
		public float Updraft;

		/// <summary>The least energy that builds a tower, J/kg: a parcel that rises on its own but gains less than this makes a heap, not a storm.</summary>
		public const float DeepConvectionCape = 20f;

		/// <summary>True where a lifted parcel rises on its own and gains enough by it to build a tower.</summary>
		public bool Deep => Cape > DeepConvectionCape;
		/// <summary>Water condensed per metre a saturated parcel rises, kg/m³ per m, at the base.</summary>
		public float CondensedPerMetre;
		/// <summary>How much of the low cloud the air allows here, 0..1: none when the base is above the lid.</summary>
		public float LowCloudAllowed;
		/// <summary>The air's scale height here, m.</summary>
		public float ScaleHeight;
		/// <summary>Air density at the cloud base, kg/m³.</summary>
		public float BaseAirDensity;

		/// <summary>
		/// The column over one place.
		/// </summary>
		/// <param name="planet">The world's air.</param>
		/// <param name="surfaceKelvin">Temperature at the ground.</param>
		/// <param name="humidity01">The weather's humidity, 0 parched … 1 saturated.</param>
		/// <param name="pressure">The weather's pressure pattern, −1 the heart of a low … +1 a settled high.</param>
		/// <param name="instability01">How willing the air is to overturn, 0..1.</param>
		public static AirColumn Of(in PlanetAir planet, float surfaceKelvin, float humidity01, float pressure, float instability01)
		{
			var column = new AirColumn();
			Condensate c = planet.Condensate;
			float g = planet.Gravity;
			float cp = planet.SpecificHeat;
			float rd = planet.GasConstant;
			float t = Mathf.Max(20f, surfaceKelvin);

			column.SurfaceKelvin = t;
			column.RelativeHumidity = AirPhysics.RelativeHumidity(humidity01);
			column.DewPointKelvin = AirPhysics.DewPoint(t, column.RelativeHumidity, c);
			column.ScaleHeight = AirPhysics.ScaleHeight(g, t, rd);

			/* The base. Rising unsaturated air cools at the dry lapse while its dew point falls far
			 * more slowly; where the two meet the vapour condenses, and it does so at one height
			 * across a region, which is why a cloud's underside is flat. */
			column.DryLapse = AirPhysics.DryLapse(g, cp);
			float dewLapse = AirPhysics.DewPointLapse(g, t, column.DewPointKelvin, rd, c);
			float closing = Mathf.Max(column.DryLapse - dewLapse, column.DryLapse * 0.1f);
			column.Base = Mathf.Max(0f, (t - column.DewPointKelvin) / closing);

			float baseKelvin = t - column.DryLapse * column.Base;
			float basePressure = planet.SurfacePressure * Mathf.Exp(-column.Base / Mathf.Max(1f, column.ScaleHeight));
			column.MoistLapse = AirPhysics.MoistLapse(g, baseKelvin, basePressure, cp, rd, c);
			float spread = Mathf.Max(0f, column.DryLapse - column.MoistLapse);
			column.BaseAirDensity = basePressure / (rd * Mathf.Max(1f, baseKelvin));
			column.CondensedPerMetre = column.BaseAirDensity * cp * spread / AirPhysics.LatentHeat(c);

			/* How the air around a parcel cools decides everything else. Between the moist and the
			 * dry lapse the air is conditionally unstable: dry it is stable, saturated it overturns.
			 * The weather's instability places it in that range — a stable air cools more slowly
			 * than even saturated air, a very unstable one nearly as fast as dry air. Centred so the
			 * field's median day (instability 0.24) is the standard atmosphere, 6.5 K/km on our own
			 * world: a fifth of the way from the moist lapse to the dry. */
			column.Instability = Mathf.Clamp(-0.18f + 1.6f * Mathf.Clamp01(instability01), -0.4f, 0.95f);
			column.EnvironmentLapse = column.MoistLapse + column.Instability * spread;
			// Heights are measured on the settled, average profile, so a level does not jump when the
			// stability changes; the stability decides what grows, not where the air freezes.
			column.MeanLapse = Mathf.Max(1e-5f, column.MoistLapse + 0.3f * spread);

			column.Tropopause = Mathf.Max(0.3f * column.ScaleHeight, (t - planet.SkinKelvin) / column.MeanLapse);
			column.Freezing = column.HeightOfKelvin(AirPhysics.FreezingKelvin(c));
			column.IceLevel = column.HeightOfKelvin(AirPhysics.HomogeneousFreezeKelvin(c));

			/* A high is air sinking and warming as it comes down, and it puts a lid on everything
			 * under it — the trade-wind inversion, the fair-weather sky that never builds past a
			 * certain height. The stronger the high the lower the lid. A low has none. */
			float settled = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.8f, pressure));
			column.Cap = Mathf.Lerp(column.Tropopause, 0.12f * column.Tropopause, settled);

			/* Where a parcel starts rising under its own buoyancy: it arrives at the base colder than
			 * the air around it (it cooled at the dry lapse, the air more slowly), and catches up at
			 * the rate the moist lapse is slower than the air's. With the lapses as fractions of one
			 * another that is simply the base over the instability. */
			column.FreeConvection = column.Instability > 0.02f ? column.Base / column.Instability : float.PositiveInfinity;
			float lid = Mathf.Min(column.Cap, column.Tropopause);
			if (column.FreeConvection < lid)
			{
				/* How much energy a parcel rising freely from there to the lid gains: g over T times
				 * the area of its warmth over the air around it. The air cools at its own lapse; the
				 * parcel at the moist lapse, which steepens toward the dry as the air aloft gets too
				 * cold to hold much water — so it is taken at the middle of the climb, not at the base,
				 * or the parcel's lead grows without limit and a sultry afternoon comes out at
				 * fourteen thousand joules. And a real parcel mixes with the air it rises through and
				 * leads it by ten kelvin at most. */
				float free = lid - column.FreeConvection;
				float midHeight = column.FreeConvection + free * 0.5f;
				float midKelvin = Mathf.Max(20f, baseKelvin - column.MoistLapse * Mathf.Max(0f, midHeight - column.Base));
				float midPressure = planet.SurfacePressure * Mathf.Exp(-midHeight / Mathf.Max(1f, column.ScaleHeight));
				float moistAloft = AirPhysics.MoistLapse(g, midKelvin, midPressure, cp, rd, c);
				float lead = Mathf.Min(10f, Mathf.Max(0f, column.EnvironmentLapse - moistAloft) * free);
				column.Cape = g / midKelvin * 0.5f * lead * free;
				// Entrainment and drag keep a real updraught to a fraction of the parcel's ideal.
				column.Updraft = 0.3f * Mathf.Sqrt(2f * Mathf.Max(0f, column.Cape));
			}
			// Deep convection needs a parcel that rises on its own AND gains something by it: a level
			// of free convection with no energy above it builds nothing.
			bool deep = column.Deep;

			/* The ordinary cloud here. Stable air makes a deck — a stratus or a stratocumulus sheet a
			 * few hundred metres thick, deeper in damper air. Unstable air makes heaps, and a heap
			 * grows until the dry air it pulls in as it rises has eaten it: about a third of the way
			 * to the lid, more in moist surroundings. */
			float room = Mathf.Max(0f, lid - column.Base);
			float deck = Mathf.Clamp(0.035f * column.ScaleHeight, 150f, 1500f) * Mathf.Lerp(0.6f, 1.4f, column.RelativeHumidity);
			float heap = room * Mathf.Lerp(0.2f, 0.5f, column.RelativeHumidity);
			column.Vigour = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.6f, column.Instability)) * (deep ? 1f : 0.6f);
			float depth = Mathf.Lerp(deck, Mathf.Max(deck, heap), column.Vigour);
			column.Top = column.Base + Mathf.Min(room, depth);
			// A base above the lid has nothing to form in: air that dry does not condense before the
			// inversion stops it rising. Eased over a few hundred metres so a front does not draw a line.
			column.LowCloudAllowed = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 250f, lid - column.Base));

			// A tower climbs as far as its energy carries it: to the tropopause, overshooting a little —
			// the dome over an anvil — once there is a few hundred joules to spend.
			float reach = deep ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DeepConvectionCape, 400f, column.Cape)) : 0f;
			column.TowerCeiling = Mathf.Max(column.Top, Mathf.Lerp(column.Top, column.Tropopause * 1.03f, reach));
			return column;
		}

		/// <summary>The height at which this column reaches a temperature, m; the tropopause if it never does below it.</summary>
		public float HeightOfKelvin(float kelvin)
		{
			if (kelvin >= SurfaceKelvin)
			{
				return 0f;
			}
			return Mathf.Min(Tropopause, (SurfaceKelvin - kelvin) / Mathf.Max(1e-5f, MeanLapse));
		}

		/// <summary>The temperature at a height, K: the settled profile up to the tropopause, level above it.</summary>
		public float KelvinAt(float height) => SurfaceKelvin - MeanLapse * Mathf.Clamp(height, 0f, Tropopause);

		/// <summary>
		/// A cloud's extinction per metre at a height above its base, for an adiabatic cloud: the
		/// constant <c>C</c> in <c>β = C·h^⅔</c>.
		/// </summary>
		/// <remarks>
		/// A rising parcel condenses water in proportion to how far it has risen above its base, and
		/// mixes in dry air as it goes, which in a real cumulus leaves about a third of that. With a
		/// fixed number of drops the extinction then goes as the two-thirds power of the height: thin
		/// and grey at the base, dense and brilliant at the crown — which is what gives a heaped
		/// cloud its dark flat underside and its bright top.
		/// </remarks>
		public float ExtinctionCoefficient(in PlanetAir planet)
		{
			float gradient = CondensedPerMetre * 0.35f;
			float n = Mathf.Max(1e6f, planet.DropletsPerCubicMetre);
			float rho = AirPhysics.LiquidDensity(planet.Condensate);
			// β(h) = 2πN·(3·gradient·h / (4πρN))^⅔  =  C·h^⅔
			float inner = 3f * gradient / (4f * Mathf.PI * rho * n);
			return inner > 0f ? 2f * Mathf.PI * n * Mathf.Pow(inner, 2f / 3f) : 0f;
		}

		/// <summary>
		/// The mean extinction of a layer cloud between two heights, 1/m: its condensed water, as ice
		/// where the layer is colder than its drops can stay liquid.
		/// </summary>
		public float LayerExtinction(in PlanetAir planet, float bottom, float top)
		{
			float middle = (bottom + top) * 0.5f;
			float kelvin = KelvinAt(middle);
			Condensate c = planet.Condensate;
			float pressure = planet.SurfacePressure * Mathf.Exp(-middle / Mathf.Max(1f, ScaleHeight));
			float moist = AirPhysics.MoistLapse(planet.Gravity, kelvin, pressure, planet.SpecificHeat, planet.GasConstant, c);
			float spread = Mathf.Max(0f, DryLapse - moist);
			float density = pressure / (planet.GasConstant * Mathf.Max(1f, kelvin));
			// A layer cloud is lifted slowly and far less than a heap: a tenth of its depth's water.
			float water = density * planet.SpecificHeat * spread / AirPhysics.LatentHeat(c) * Mathf.Max(0f, top - bottom) * 0.5f * 0.1f;
			bool ice = kelvin < AirPhysics.HomogeneousFreezeKelvin(c);
			// Ice crystals are few and large: about a thousandth the number of drops.
			float n = ice ? planet.DropletsPerCubicMetre * 0.001f : planet.DropletsPerCubicMetre;
			return AirPhysics.DropletExtinction(water, n, ice ? 917f : AirPhysics.LiquidDensity(c));
		}
	}
}
