using UnityEngine;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// One point on a world's surface: how high it stands, how warm and wet it is, and which biome
	/// that makes it.
	/// </summary>
	public struct PlanetSurfacePoint
	{
		/// <summary>The raw surface field at this point, 0 … 1. Only meaningful against a profile.</summary>
		public float Height;

		/// <summary>Metres above the body's sea level. Negative is under water.</summary>
		public float AltitudeMetres;

		/// <summary>
		/// Height as the biome system reads it, 0 … 1, with the water line at
		/// <see cref="ClimateModel.DefaultWaterSurfaceHeight"/>.
		/// </summary>
		public float NormalizedHeight;

		public ClimateSample Climate;

		/// <summary>True when this point is below the body's sea level.</summary>
		public bool UnderWater => AltitudeMetres < 0f;
	}

	/// <summary>
	/// A world's climate as a field over its whole surface: the terms that do not change from
	/// point to point, worked out once, so asking about a point costs a few noise samples.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Shared so the picture and the ground cannot disagree.</b> The globe bake, the scene
	/// generator and anything that names a place all have to answer the same question — what is it
	/// like at this latitude and longitude — and each doing its own arithmetic is how a scene comes
	/// to be called a frost hollow on ground the map draws as desert. <c>AltitudeFromHeight</c> was
	/// pulled out for exactly this reason after the bake and the generator disagreed about how high
	/// a continent stood; this is the same move for temperature and humidity.
	/// </para>
	/// <para>
	/// <b>A struct built once, then asked many times.</b> The bake asks it two million times in a
	/// loop, so the mean surface temperature, the lapse rate and the hypsometric profile are
	/// resolved in <see cref="For"/> and never again. Nothing here allocates.
	/// </para>
	/// </remarks>
	public struct PlanetClimateField
	{
		/// <summary>
		/// How far regional climate pushes a temperature about, in scale units.
		/// </summary>
		/// <remarks>
		/// 0.18 is about six kelvin. Measured on Naron, the latitude term moves roughly 0.02 units
		/// per degree, so this swings the snowline about nine degrees either way — enough for real
		/// lobes and bays, small enough that it never turns a temperate world into a frozen one.
		/// </remarks>
		public const float RegionalVariation = 0.18f;

		/// <summary>The body's terrain seed.</summary>
		public uint Seed;

		/// <summary>How heavily cratered this body's surface is, from its atmosphere.</summary>
		public float Cratering;

		/// <summary>Sea level, floor and summit of the surface field on this body.</summary>
		public PlanetSurface.PlanetProfile Profile;

		/// <summary>Floor-to-summit relief in metres.</summary>
		public float ReliefMetres;

		/// <summary>Metres above sea level of the highest ground, and below it of the deepest.</summary>
		public float HighestMetres;
		public float LowestMetres;

		/// <summary>Mean surface temperature in scale units, UNCLAMPED — latitude and altitude follow.</summary>
		public float MeanTemperature;

		/// <summary>Temperature lost per metre of altitude, in scale units.</summary>
		public float LapsePerMetre;

		/// <summary>What the world makes for itself: 0 dead, 1 molten.</summary>
		public float InternalHeat;

		/// <summary>What the world offers a biome, atmosphere and water included.</summary>
		public BiomeWorldConditions Conditions;

		/// <summary>The humidity curves, from the same derivation the runtime climate uses.</summary>
		public ClimateParameters Parameters;

		private SolarSystemProfile system;
		private WorldBody body;

		/// <summary>True when there is a body to answer about.</summary>
		public bool Valid => body != null;

		/// <summary>The body this field describes.</summary>
		public WorldBody Body => body;

		/// <summary>
		/// Resolves everything that is the same everywhere on a body.
		/// </summary>
		/// <param name="system">The solar system, for the starlight. Null gives an Earth-like world.</param>
		/// <param name="body">The body. Null gives a field that answers Earth-like everywhere.</param>
		public static PlanetClimateField For(SolarSystemProfile system, WorldBody body)
		{
			var field = new PlanetClimateField
			{
				system = system,
				body = body,
				Seed = body != null ? body.ResolvedTerrainSeed : 1u,
				Cratering = body != null ? PlanetSurface.CrateringOf(body.Atmosphere) : 0f,
				ReliefMetres = PlanetSurface.ReliefMetres(body),
				InternalHeat = ClimateModel.InternalHeat(system, body),
				Conditions = BiomeWorldConditions.For(system, body),
			};

			field.Profile = PlanetSurface.ProfileOf(field.Seed, body);

			AtmosphereKind atmosphere = body != null ? body.Atmosphere : AtmosphereKind.Standard;
			float water = body != null ? body.Water : 0.7f;
			double insolation = system != null && body != null ? CelestialMath.Insolation(system, body, 0.0) : 1.0;

			/* Unclamped, because latitude and altitude are still to be subtracted from it. Clamped
			 * first, a 460 K greenhouse world reads +1 like any warm planet, and the pole's -1.6
			 * then drags it below freezing — which is how a world hot enough to melt lead came out
			 * with ice caps. */
			field.MeanTemperature = (float)ClimateModel.ToScaleUnclamped(
				ClimateModel.MeanSurfaceKelvin(insolation, atmosphere, water));
			field.LapsePerMetre = (float)ClimateModel.LapseRatePerMetre(atmosphere) / (float)ClimateModel.KelvinPerUnit;

			// The ends of the hypsometric curve, so a height can be placed against the world's own
			// range rather than against a number chosen for an Earth-sized planet.
			field.HighestMetres = PlanetSurface.AltitudeFromHeight(field.Profile.Highest, field.Profile, field.ReliefMetres);
			field.LowestMetres = PlanetSurface.AltitudeFromHeight(field.Profile.Lowest, field.Profile, field.ReliefMetres);

			// The humidity curves only; its lapse rate is per-normalised-height and this works in
			// metres, so TemperatureAt does that part itself.
			field.Parameters = ClimateModel.For(system, body, field.ReliefMetres);
			return field;
		}

		/// <summary>
		/// The regional wobble at a point, in scale units.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A regional term on top of the physics, so the snowline is a coastline and not a ruled
		/// line. Latitude temperature is a pure function of latitude and altitude barely moves on
		/// gentle ground, so without this the freezing contour is exactly a circle of latitude, and
		/// the ice cap comes out as a band drawn straight across the map with a razor edge. Real
		/// caps are lobed because currents, land and weather push them about; this stands in for
		/// all three at a fraction of a kelvin's worth of variation.
		/// </para>
		/// <para>
		/// Two scales, and stretched, because fBm does not fill its own range. Measured:
		/// (Fbm − 0.5) × 2 swings only about ±0.22, since averaging octaves pulls the result to the
		/// middle. Unstretched that was ±0.02 of temperature — under a degree of latitude, and
		/// invisible.
		/// </para>
		/// </remarks>
		public float RegionalOffset(Vector3 direction)
		{
			float coarse = Mathf.Clamp((PlanetSurface.FieldNoise(Seed ^ 0x51CEEDA7u, direction, 2.4f, 3) - 0.5f) * 2f * 3.4f, -1f, 1f);
			float fine = Mathf.Clamp((PlanetSurface.FieldNoise(Seed ^ 0x2A66EDu, direction, 11f, 3) - 0.5f) * 2f * 3.4f, -1f, 1f);
			return (coarse * 0.78f + fine * 0.22f) * RegionalVariation;
		}

		/// <summary>
		/// Temperature at a point: the world's mean, cooled toward the pole by the sun's noon
		/// altitude, cooled again by how far above sea level the ground stands, and nudged by the
		/// regional wobble.
		/// </summary>
		/// <param name="latitudeDegrees">Latitude of the point.</param>
		/// <param name="direction">The same point as a unit vector, for the regional term.</param>
		/// <param name="altitudeMetres">Metres above sea level. Below it costs nothing: the sea is the sea.</param>
		public float TemperatureAt(double latitudeDegrees, Vector3 direction, float altitudeMetres)
		{
			float latitude = system != null && body != null
				? (float)CelestialMath.LatitudeTemperature(system, body, 0.0, latitudeDegrees)
				: 0f;
			// Clamped here, once, with every term in.
			return Mathf.Clamp(MeanTemperature
				+ latitude
				- Mathf.Max(0f, altitudeMetres) * LapsePerMetre
				+ RegionalOffset(direction), -1f, 1f);
		}

		/// <summary>
		/// An altitude in metres as the biome system's normalised height, water line at
		/// <see cref="ClimateModel.DefaultWaterSurfaceHeight"/>.
		/// </summary>
		/// <remarks>
		/// <b>From the metres, not from the raw field.</b> The surface field is squeezed toward sea
		/// level by <see cref="PlanetSurface.LandHypsometry"/>, so its median land point sits a long
		/// way up the raw range while standing barely a kilometre above the water. Normalising the
		/// raw field instead would put ordinary coastal plain in the alpine elevation tiers, and
		/// every scene cut from a continent would come out named for a mountain it is not on.
		/// </remarks>
		public float HeightOfAltitude(float altitudeMetres)
		{
			const float Water = ClimateModel.DefaultWaterSurfaceHeight;
			if (altitudeMetres >= 0f)
			{
				float top = Mathf.Max(1f, HighestMetres);
				return Water + (1f - Water) * Mathf.Clamp01(altitudeMetres / top);
			}
			float floor = Mathf.Min(-1f, LowestMetres);
			return Water * Mathf.Clamp01(1f - altitudeMetres / floor);
		}

		/// <summary>Everything about one point on the globe.</summary>
		public PlanetSurfacePoint At(double latitudeDegrees, double longitudeDegrees)
		{
			Vector3 direction = PlanetSurface.Direction(latitudeDegrees, longitudeDegrees);
			float height = PlanetSurface.Height(Seed, direction, Cratering);
			float altitude = PlanetSurface.AltitudeFromHeight(height, Profile, ReliefMetres);
			float normalized = HeightOfAltitude(altitude);
			float temperature = TemperatureAt(latitudeDegrees, direction, altitude);

			return new PlanetSurfacePoint
			{
				Height = height,
				AltitudeMetres = altitude,
				NormalizedHeight = normalized,
				Climate = new ClimateSample
				{
					Temperature = temperature,
					Humidity = Parameters.HumidityAt(temperature, normalized),
					ElevationTier = ClimateSettings.TierForHeight(normalized, Parameters.ElevationBoundaries),
				},
			};
		}

		/// <summary>
		/// The biome a point would carry, from the world's own conditions. Null when no biome
		/// template is registered or none fits.
		/// </summary>
		public BiomeTemplate BiomeAt(double latitudeDegrees, double longitudeDegrees, out PlanetSurfacePoint point)
		{
			point = At(latitudeDegrees, longitudeDegrees);
			return BiomeResolver.Select(point.NormalizedHeight, point.Climate, Conditions);
		}
	}
}
