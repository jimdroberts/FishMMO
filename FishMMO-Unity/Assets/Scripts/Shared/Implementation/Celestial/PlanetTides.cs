using System;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// How far the sea rises and falls, from the pull of the moons and the star.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A tide is not a wave.</b> Waves are the wind's work and live on the surface; a tide is the
	/// whole body of water being lifted, over hours, by something else's gravity. So this answers in
	/// a single number — how far above or below mean sea level the water stands at a place right now
	/// — and the wave model rides on top of whatever it says.
	/// </para>
	/// <para>
	/// <b>The equilibrium tide, which is the real one.</b> A perturber raises two bulges, one under
	/// it and one on the far side, and the world turns through both — which is why there are two
	/// high tides a day and not one. The height of the bulge is
	/// <c>h = 1.5 · (M_p / M_b) · R_b⁴ / d³</c>, and the share of it a place gets is
	/// <c>(3cos²θ − 1) / 2</c> for θ the angle from the bulge. Since cos θ is the cosine of the
	/// perturber's zenith angle, that is <c>sin(altitude)</c> — so the whole pattern falls out of
	/// <see cref="CelestialMath.Altitude"/>, which this project already had and already tests.
	/// </para>
	/// <para>
	/// Spring and neap tides are not modelled, coded or switched on: they simply happen, because
	/// when the star and a moon are in the same part of the sky their bulges add and a fortnight
	/// later they oppose. So do perigean tides, because the distance is taken from where the moon
	/// actually is rather than from its mean orbit.
	/// </para>
	/// <para>
	/// <b>What it is NOT.</b> The equilibrium tide is the open-ocean one. Real coastal ranges run
	/// two to twenty times larger because a basin resonates, a funnel concentrates and a shelf
	/// shoals — the Bay of Fundy's 16 m against an equilibrium half-metre. None of that is
	/// geometry this can know, so it is left as a per-scene multiplier for somebody to set.
	/// </para>
	/// </remarks>
	public static class PlanetTides
	{
		/// <summary>Earth's radius in kilometres, the reference the masses are quoted against.</summary>
		public const double EarthRadiusKm = 6371.0;

		/// <summary>Earth masses in one solar mass.</summary>
		public const double EarthMassesPerSolarMass = 332946.0;

		/// <summary>
		/// A body's mass in Earth masses, from its radius at Earth's density.
		/// </summary>
		/// <remarks>
		/// Radius is the only size this project carries, so density is assumed constant — the same
		/// convention <see cref="CelestialMath.TidalHeating"/> uses. It over-weights a low-density
		/// body: our own Moon comes out at 0.020 Earth masses against a true 0.012, so its tide is
		/// about 1.6 times life size. Consistent, and wrong in a direction that makes tides more
		/// visible rather than less.
		/// </remarks>
		public static double MassInEarths(CelestialBody body)
		{
			if (body == null)
			{
				return 0.0;
			}
			if (body is StarBody star)
			{
				return CelestialMath.StarMass(star) * EarthMassesPerSolarMass;
			}
			double radii = Math.Max(1.0, body.SkyRadiusKm) / EarthRadiusKm;
			return radii * radii * radii;
		}

		/// <summary>
		/// The height of the bulge one perturber raises on a body right now, in metres.
		/// </summary>
		/// <param name="system">The solar system.</param>
		/// <param name="body">The world with the sea on it.</param>
		/// <param name="perturber">What is pulling on it.</param>
		/// <param name="hours">World hours, for where the perturber is.</param>
		public static double BulgeMetres(SolarSystemProfile system, WorldBody body, CelestialBody perturber, double hours)
		{
			if (system == null || body == null || perturber == null || ReferenceEquals(body, perturber))
			{
				return 0.0;
			}

			double bodyMass = MassInEarths(body);
			double perturberMass = MassInEarths(perturber);
			if (bodyMass <= 0.0 || perturberMass <= 0.0)
			{
				return 0.0;
			}

			// Positions are in AU throughout CelestialMath; radii are in kilometres.
			Vector3d separation = CelestialMath.Position(system, perturber, hours) - CelestialMath.Position(system, body, hours);
			double distanceKm = separation.Magnitude * CelestialMath.AuKm;
			double radiusKm = Math.Max(1.0, body.SkyRadiusKm);
			// Inside its own surface is not a distance; a body that close is not orbiting anyway.
			if (distanceKm <= radiusKm * 1.5)
			{
				return 0.0;
			}

			double ratio = perturberMass / bodyMass;
			double heightKm = 1.5 * ratio * radiusKm * radiusKm * radiusKm * radiusKm
				/ (distanceKm * distanceKm * distanceKm);
			return heightKm * 1000.0;
		}

		/// <summary>
		/// How far the sea stands above mean level at a place, in metres.
		/// </summary>
		/// <param name="system">The solar system. Null gives no tide.</param>
		/// <param name="body">The world. Null gives no tide.</param>
		/// <param name="hours">World hours.</param>
		/// <param name="latitudeDegrees">Where on the world.</param>
		/// <param name="longitudeDegrees">Where on the world.</param>
		/// <remarks>
		/// Summed over every body in the system. Nothing is special-cased as "the moon": the
		/// 1/distance³ term means a body on the other side of the system contributes a number too
		/// small to see, and a close one dominates, which is the whole content of the physics.
		/// </remarks>
		public static double HeightMetres(SolarSystemProfile system, WorldBody body,
			double hours, double latitudeDegrees, double longitudeDegrees)
		{
			if (system == null || body == null || system.Bodies == null)
			{
				return 0.0;
			}

			double total = 0.0;
			foreach (CelestialBody perturber in system.Bodies)
			{
				if (perturber == null || ReferenceEquals(perturber, body))
				{
					continue;
				}
				double bulge = BulgeMetres(system, body, perturber, hours);
				// Below a tenth of a millimetre it is not a tide, and the equatorial transform
				// below is the expensive part.
				if (bulge < 1e-4)
				{
					continue;
				}

				CelestialMath.Equatorial(system, body, perturber, hours, out double rightAscension, out double declination);
				double hourAngle = CelestialMath.HourAngle(system, body, hours, longitudeDegrees, rightAscension);
				/* cos θ from the zenith IS the sine of the altitude, so the two bulges — one under
				 * the perturber, one directly opposite — and the low water between them all come
				 * out of the altitude this project already computes for the sun. */
				double cosTheta = Math.Sin(CelestialMath.Altitude(latitudeDegrees, declination, hourAngle));
				total += bulge * (3.0 * cosTheta * cosTheta - 1.0) * 0.5;
			}
			return total;
		}

		/// <summary>
		/// The full range from low water to high water at a place, in metres.
		/// </summary>
		/// <remarks>
		/// The sum of the bulges, times the 1.5 that separates a high tide (+h) from a low one
		/// (−h/2). Sampled at the given time, so it is the range of THIS tide rather than a mean —
		/// a spring tide answers larger than a neap one, which is the point.
		/// </remarks>
		public static double RangeMetres(SolarSystemProfile system, WorldBody body, double hours)
		{
			if (system == null || body == null || system.Bodies == null)
			{
				return 0.0;
			}
			double sum = 0.0;
			foreach (CelestialBody perturber in system.Bodies)
			{
				sum += BulgeMetres(system, body, perturber, hours);
			}
			return sum * 1.5;
		}
	}
}
