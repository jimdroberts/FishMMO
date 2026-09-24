using System;
using UnityEngine;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The numbers a climate reading is made from, whether they were derived from a world or
	/// authored on a <see cref="ClimateSettings"/> asset.
	/// </summary>
	/// <remarks>
	/// The evaluation lives here and nowhere else, so a derived climate and an authored one cannot
	/// disagree about what their own numbers mean.
	/// </remarks>
	public struct ClimateParameters
	{
		/// <summary>Temperature at the water line on the sub-solar equator, -1 … 1.</summary>
		public float SeaLevelTemperature;
		/// <summary>Temperature lost from the landmass's lowest ground to its highest, in scale units.</summary>
		public float ElevationLapseRate;
		/// <summary>Normalised height of the water surface.</summary>
		public float WaterSurfaceHeight;
		public float LowlandHumidityBonus;
		public float HeatDryingThreshold;
		public float HeatDryingRate;
		public float ColdDryingThreshold;
		public float ColdDryingRate;
		public float GlobalTemperatureOffset;
		public float GlobalHumidityOffset;
		/// <summary>Ten ascending values, or null for the built-in boundaries.</summary>
		public float[] ElevationBoundaries;

		/// <summary>Temperature and humidity at a normalised height.</summary>
		/// <remarks>
		/// Anchored at the water line: before that, sea level read -0.34 and almost every scene
		/// above it was below freezing, so most weather fell as snow.
		/// </remarks>
		public ClimateSample Evaluate(float height)
		{
			float temperature = Mathf.Clamp(
				GlobalTemperatureOffset + SeaLevelTemperature - (height - WaterSurfaceHeight) * ElevationLapseRate,
				-1f, 1f);

			return new ClimateSample
			{
				Temperature = temperature,
				Humidity = HumidityAt(temperature, height),
				ElevationTier = ClimateSettings.TierForHeight(height, ElevationBoundaries),
			};
		}

		/// <summary>
		/// Humidity for a temperature already worked out, at a normalised height.
		/// </summary>
		/// <remarks>
		/// Split out so anything that derives its own temperature — the globe bake works in metres
		/// above sea level, not in normalised height — still dries the air by the same curves.
		/// </remarks>
		public float HumidityAt(float temperature, float height)
		{
			float humidity = (1f - height) * LowlandHumidityBonus;
			if (temperature > HeatDryingThreshold)
			{
				humidity -= (temperature - HeatDryingThreshold) * HeatDryingRate;
			}
			else if (temperature < ColdDryingThreshold)
			{
				humidity += (temperature - ColdDryingThreshold) * ColdDryingRate;
			}
			return Mathf.Clamp(humidity + GlobalHumidityOffset, -1f, 1f);
		}
	}

	/// <summary>
	/// A world's climate, derived from its star, its orbit, its atmosphere and its ocean — with no
	/// reference world and nothing authored.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Absolute, not relative.</b> <c>CelestialMath.ClimateOffsets</c> answers "how much colder
	/// is this world than home", which needs a home to be colder than and leaves the home world's
	/// own climate as a number somebody has to write down. This answers in kelvin first, from the
	/// starlight the body actually receives, and converts at the end. Nothing here asks what the
	/// home world is.
	/// </para>
	/// <para>
	/// <b>The scale's two constants are unit choices, not calibration.</b>
	/// <see cref="FreezingPoint"/> is where water freezes, which is already the boundary the
	/// weather uses to decide rain from snow; <see cref="KelvinPerUnit"/> is how much of the
	/// −1…1 range a kelvin buys. Neither describes a particular planet.
	/// </para>
	/// <para>
	/// It lands close to both the real solar system and the numbers this project arrived at by
	/// calibration. An Earth-like world reads a mean of <b>286.2 K</b> against a real 288, and a
	/// sub-solar sea level of <b>+0.76</b> against the 0.80 that was measured by hand; a Mars-like
	/// world reads <b>208.9 K</b> against a real 210. Agreeing to within a couple of kelvin with
	/// figures reached a completely different way is the reason to trust it.
	/// </para>
	/// </remarks>
	public static class ClimateModel
	{
		/// <summary>0 on the temperature scale: water freezes, and rain becomes snow.</summary>
		public const double FreezingPoint = 273.15;

		/// <summary>
		/// Kelvin per unit of the −1…1 scale.
		/// </summary>
		/// <remarks>
		/// 33.1, which is 50 K of equator-to-pole spread over the 1.51 units the project's own
		/// latitude model moves through. It puts the authored biome envelopes on sensible ground:
		/// a jungle at +0.6 is +20 °C and a glacier at −0.6 is −20 °C.
		/// </remarks>
		public const double KelvinPerUnit = 33.1;

		/// <summary>
		/// Black-body temperature at one solar constant with no albedo and no atmosphere, in kelvin.
		/// </summary>
		/// <remarks>The standard 278.5 K. It falls out of the Stefan-Boltzmann law and the solar constant, not out of Earth.</remarks>
		public const double SolarConstantTemperature = 278.5;

		/// <summary>
		/// How much hotter the sub-solar ground at noon is than the globe's mean, in kelvin.
		/// </summary>
		/// <remarks>
		/// The model's sea-level figure is the hottest the ground gets — directly under the star,
		/// at the water line — while an equilibrium temperature is an average over the whole
		/// sphere. Earth's mean is 288 K and its sub-solar equatorial ground runs about 300; the
		/// latitude model then takes it back down towards the poles from there.
		/// </remarks>
		public const double SubSolarExcess = 12.0;

		/// <summary>Environmental lapse rate in kelvin per metre, by how much air there is to hold moisture.</summary>
		/// <remarks>
		/// Dry air cools at the dry adiabat, about 9.8 K/km. Moist air releases latent heat as it
		/// rises and cools more slowly, about 6.5 K/km on Earth and less in a thicker, wetter
		/// atmosphere. An airless body has no air to cool at all, and gets the dry figure only so
		/// that shaded high ground still reads colder than lit low ground.
		/// </remarks>
		public static double LapseRatePerMetre(AtmosphereKind atmosphere)
		{
			switch (atmosphere)
			{
				case AtmosphereKind.None: return 0.0098;
				case AtmosphereKind.Thin: return 0.0098;
				case AtmosphereKind.Thick: return 0.0050;
				default: return 0.0065;
			}
		}

		/// <summary>
		/// Bond albedo: how much starlight the world throws straight back.
		/// </summary>
		/// <remarks>
		/// Ocean is dark and cloud is bright, and a thicker atmosphere makes more cloud — so water
		/// lowers the albedo directly while raising it through the weather it drives. Earth, at 70%
		/// water under a standard atmosphere, comes out at 0.31 against a measured 0.306.
		/// </remarks>
		public static double Albedo(AtmosphereKind atmosphere, float water)
		{
			double bare = 0.35;                                  // rock and dust
			double ocean = 0.06;                                 // open water, very dark
			double surface = Mathf.Lerp((float)bare, (float)ocean, Mathf.Clamp01(water));
			double cloud;
			switch (atmosphere)
			{
				case AtmosphereKind.None: cloud = 0.0; break;
				case AtmosphereKind.Thin: cloud = 0.05; break;
				case AtmosphereKind.Thick: cloud = 0.60; break;
				default: cloud = 0.35; break;
			}
			// Cloud covers part of the sky and reflects most of what hits it.
			double cover = cloud * Mathf.Clamp01(water);
			return Math.Max(0.02, Math.Min(0.95, surface * (1.0 - cover) + 0.70 * cover));
		}

		/// <summary>Greenhouse warming in kelvin, from the atmosphere and the water vapour in it.</summary>
		/// <remarks>
		/// Earth's is 33 K, almost all of it water vapour and CO2. A thick atmosphere runs away —
		/// Venus is over 500 K of greenhouse — and an airless body has none at all.
		/// </remarks>
		public static double Greenhouse(AtmosphereKind atmosphere, float water)
		{
			switch (atmosphere)
			{
				case AtmosphereKind.None: return 0.0;
				case AtmosphereKind.Thin: return 5.0;
				case AtmosphereKind.Thick: return 140.0 + 60.0 * Mathf.Clamp01(water);
				default: return 20.0 + 19.0 * Mathf.Clamp01(water);
			}
		}

		/// <summary>The globe's mean surface temperature in kelvin.</summary>
		/// <param name="insolation">Starlight received, in units of the solar constant.</param>
		public static double MeanSurfaceKelvin(double insolation, AtmosphereKind atmosphere, float water)
		{
			double flux = Math.Max(0.0, insolation);
			double equilibrium = SolarConstantTemperature * Math.Pow(flux, 0.25) * Math.Pow(1.0 - Albedo(atmosphere, water), 0.25);
			return equilibrium + Greenhouse(atmosphere, water);
		}

		/// <summary>
		/// How much heat a world makes for itself, 0 dead to 1 molten.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Heat from below, kept apart from heat from the star.</b> Io's surface is −140 °C and
		/// it is the most volcanic body in the solar system; Venus is 460 °C and geologically
		/// placid. Folding the two together would make every volcano a function of sunlight and
		/// put lava on the wrong worlds entirely.
		/// </para>
		/// <para>
		/// Two real sources. <b>Tides</b> squeeze a moon on an eccentric orbit, and that term is
		/// nearly a switch — heating falls with the sixth power of distance, so a system gets a
		/// couple of tormented inner moons and many dead outer ones. <b>Size</b> is the other: a
		/// large world holds its primordial and radiogenic heat for billions of years while a small
		/// one radiates it away and freezes solid, which is why Earth has plate tectonics and the
		/// Moon has none despite being made of much the same rock.
		/// </para>
		/// <para>
		/// A body's magnetic field corroborates rather than drives: a field needs a molten
		/// conducting core, so a world that has one is warm inside whatever the other two terms
		/// guessed. It can only raise the answer, never lower it — plenty of hot worlds have no
		/// field, so its absence proves nothing.
		/// </para>
		/// </remarks>
		public static float InternalHeat(SolarSystemProfile system, WorldBody body)
		{
			if (body == null)
			{
				return EarthlikeInternalHeat;
			}

			// Tides. Saturated well below Io's, because Io is an extreme and a world half as
			// tormented is still comprehensively volcanic.
			double tidal = CelestialMath.TidalHeating(system, body);
			float fromTides = (float)Math.Min(1.0, Math.Sqrt(Math.Max(0.0, tidal)) * 0.8);

            /* Size. Radius against Earth's, curved so that the interesting range is where the
             * bodies are: a 5 km rock is stone dead, a 1000 km moon is barely warm, and an
             * Earth-sized world is fully active. */
			float radiusKm = body.SkyRadiusKm > 1f ? body.SkyRadiusKm : PlanetSurface.EarthRadiusKm;
			float fromSize = Mathf.Clamp01(Mathf.Pow(radiusKm / PlanetSurface.EarthRadiusKm, 1.4f));

			// The stronger of the two, not the sum: a world is volcanic for one reason or the
			// other, and Io is not made calmer by being small.
			float heat = Mathf.Max(fromTides, fromSize);

			// A magnetic field means a molten core. It can only corroborate.
			float field = Mathf.Clamp01(body.MagneticField);
			return Mathf.Clamp01(Mathf.Max(heat, field * 0.55f));
		}

		/// <summary>What an Earth-sized world makes, for anything with no body to ask about.</summary>
		public const float EarthlikeInternalHeat = 1f;

		/// <summary>Above this a world's surface is being actively rebuilt: lava, sulphur, fresh basalt.</summary>
		public const float VolcanicThreshold = 0.55f;

		/// <summary>A kelvin temperature on the −1…1 climate scale.</summary>
		public static float ToScale(double kelvin) => (float)Math.Max(-1.0, Math.Min(1.0, ToScaleUnclamped(kelvin)));

		/// <summary>
		/// The same conversion without the clamp, for anything that adds more terms afterwards.
		/// </summary>
		/// <remarks>
		/// Clamping a world's mean before its latitude and altitude are applied throws away exactly
		/// the information those terms need. A Venus-like world sits at about +4 on this scale;
		/// clamped to +1 and then cooled by 1.6 at the pole it comes out below freezing, and a
		/// 460 K planet is drawn with ice caps. Clamp once, at the end, when every term is in.
		/// </remarks>
		public static double ToScaleUnclamped(double kelvin) => (kelvin - FreezingPoint) / KelvinPerUnit;

		/// <summary>A climate-scale temperature back in kelvin.</summary>
		public static double ToKelvin(float scale) => FreezingPoint + scale * KelvinPerUnit;

		/// <summary>
		/// The climate of a world, for a landmass of a given vertical range.
		/// </summary>
		/// <param name="system">The solar system, for the starlight. Null gives an Earth-like world.</param>
		/// <param name="body">The body the scene stands on. Null gives an Earth-like world.</param>
		/// <param name="landmassHeightMetres">
		/// What a normalised height of 0 to 1 spans, from <see cref="SceneTerrainExtent"/>. This is
		/// what turns a real lapse rate into the scale's, so a scene of gentle hills cools far less
		/// from bottom to top than one with 8 km of mountain — which is correct, and is the whole
		/// reason the rate stopped being a number somebody tuned.
		/// </param>
		public static ClimateParameters For(SolarSystemProfile system, WorldBody body, float landmassHeightMetres)
		{
			AtmosphereKind atmosphere = body != null ? body.Atmosphere : AtmosphereKind.Standard;
			float water = body != null ? body.Water : 0.7f;
			double insolation = system != null && body != null ? CelestialMath.Insolation(system, body, 0.0) : 1.0;

			double mean = MeanSurfaceKelvin(insolation, atmosphere, water);
			double subSolar = mean + SubSolarExcess;

			/* Metres of relief, converted once. A landmass with no measured height still needs a
			 * number; DefaultLandmassHeightMetres is the height a Unity terrain is created with,
			 * so a scene nobody has measured behaves as the editor's own default would. */
			float span = landmassHeightMetres > 0f ? landmassHeightMetres : DefaultLandmassHeightMetres;
			double lapse = LapseRatePerMetre(atmosphere) * span / KelvinPerUnit;

			/* An airless world holds no moisture whatever its ocean was: it has none, or it is
			 * frozen out. Otherwise humidity follows the ocean, since that is what evaporates. */
			bool airless = atmosphere == AtmosphereKind.None;
			float wetness = airless ? 0f : Mathf.Clamp01(water);

			return new ClimateParameters
			{
				SeaLevelTemperature = ToScale(subSolar),
				ElevationLapseRate = (float)Math.Max(0.0, lapse),
				WaterSurfaceHeight = DefaultWaterSurfaceHeight,
				LowlandHumidityBonus = airless ? 0f : Mathf.Lerp(0.05f, 0.45f, wetness),
				HeatDryingThreshold = 0.5f,
				HeatDryingRate = 0.5f,
				ColdDryingThreshold = -0.3f,
				ColdDryingRate = 0.3f,
				GlobalHumidityOffset = airless ? -1f : Mathf.Lerp(-0.6f, 0.2f, wetness),
				GlobalTemperatureOffset = 0f,
				ElevationBoundaries = null,
			};
		}

		/// <summary>
		/// The vertical range assumed for a scene with no terrain to measure: Unity's own default
		/// terrain height.
		/// </summary>
		public const float DefaultLandmassHeightMetres = 600f;

		/// <summary>
		/// Where the water surface sits in a normalised height.
		/// </summary>
		/// <remarks>
		/// A fact about how the heightmap was generated, not about the world — no orbit can say
		/// where a terrain artist put the water plane — so it stays a constant here until the
		/// terrain itself can be asked. It matches what WorldEditor generates with.
		/// </remarks>
		public const float DefaultWaterSurfaceHeight = 0.42f;
	}
}
