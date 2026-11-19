using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Celestial
{
	/// <summary>One body as seen from a place on another.</summary>
	public struct SkyBodyView
	{
		public CelestialBody Body;
		/// <summary>Degrees above the horizon.</summary>
		public double Altitude;
		/// <summary>Degrees clockwise from north.</summary>
		public double Azimuth;
		public double AngularDiameterDegrees;
		/// <summary>Share of the visible sky, in percent.</summary>
		public double SkyPercent;
		/// <summary>0 new … 1 full. Stars read 1.</summary>
		public double Illumination;
		public double DistanceKm;
		public bool Textured;
		public bool AboveHorizon => Altitude > 0.0;
	}

	/// <summary>
	/// What the sky of a place holds: bodies in horizontal coordinates, the star field's turn,
	/// comet tails and asteroid positions. Shared by the Dashboard preview and the sky renderer.
	/// </summary>
	public static class CelestialSky
	{
		/// <summary>Altitude and azimuth (degrees) from declination and hour angle (radians) at a latitude (degrees).</summary>
		public static void Horizontal(double latitudeDegrees, double declination, double hourAngle, out double altitude, out double azimuth)
		{
			double lat = latitudeDegrees * CelestialMath.Deg2Rad;
			double sinAlt = Math.Sin(lat) * Math.Sin(declination) + Math.Cos(lat) * Math.Cos(declination) * Math.Cos(hourAngle);
			altitude = Math.Asin(Math.Max(-1.0, Math.Min(1.0, sinAlt))) * CelestialMath.Rad2Deg;
			double y = -Math.Cos(declination) * Math.Sin(hourAngle);
			double x = Math.Sin(declination) * Math.Cos(lat) - Math.Cos(declination) * Math.Cos(hourAngle) * Math.Sin(lat);
			azimuth = Math.Atan2(y, x) * CelestialMath.Rad2Deg;
			if (azimuth < 0.0)
			{
				azimuth += 360.0;
			}
		}

		/// <summary>Horizontal coordinates of a heliocentric point (AU) seen from a body.</summary>
		public static void HorizontalOf(SolarSystemProfile system, WorldBody observer, Vector3d point, double hours, double latitude, double longitude, out double altitude, out double azimuth)
		{
			Vector3d direction = point - CelestialMath.Position(system, observer, hours);
			CelestialMath.ToEquatorial(direction, observer, out double ra, out double dec);
			Horizontal(latitude, dec, CelestialMath.HourAngle(system, observer, hours, longitude, ra), out altitude, out azimuth);
		}

		/// <summary>
		/// The angle the fixed stars have turned through at a longitude: the right ascension on
		/// the meridian, in radians.
		/// </summary>
		public static double MeridianRightAscension(SolarSystemProfile system, WorldBody observer, double hours, double longitude)
		{
			return CelestialMath.WrapPi(CelestialMath.RotationAngle(system, observer, hours) + longitude * CelestialMath.Deg2Rad);
		}

		/// <summary>Every other body as seen from a place, into a list (cleared first).</summary>
		public static void Bodies(SolarSystemProfile system, WorldBody observer, double hours, double latitude, double longitude, List<SkyBodyView> into)
		{
			into.Clear();
			if (system == null || observer == null)
			{
				return;
			}
			Vector3d from = CelestialMath.Position(system, observer, hours);
			foreach (CelestialBody body in system.Bodies)
			{
				if (body == null || body == observer)
				{
					continue;
				}
				Vector3d to = CelestialMath.Position(system, body, hours);
				double km = Math.Max(1.0, (to - from).Magnitude * CelestialMath.AuKm);
				CelestialMath.Equatorial(system, observer, body, hours, out double ra, out double dec);
				Horizontal(latitude, dec, CelestialMath.HourAngle(system, observer, hours, longitude, ra), out double alt, out double az);
				double diameter = CelestialMath.AngularDiameter(body.SkyRadiusKm, km);
				double fraction = CelestialMath.SkyFraction(diameter);
				into.Add(new SkyBodyView
				{
					Body = body,
					Altitude = alt,
					Azimuth = az,
					AngularDiameterDegrees = diameter * CelestialMath.Rad2Deg,
					SkyPercent = fraction * 100.0,
					Illumination = body is StarBody ? 1.0 : CelestialMath.Illumination(system, observer, body, hours),
					DistanceKm = km,
					Textured = !(body is StarBody) && fraction * 100.0 >= system.TextureAboveSkyPercent,
				});
			}
		}

		/// <summary>The tip of a comet's tail (AU): away from the primary star, longer when closer.</summary>
		public static Vector3d CometTailTip(SolarSystemProfile system, CometBody comet, double hours)
		{
			Vector3d p = CelestialMath.Position(system, comet, hours);
			Vector3d star = CelestialMath.Position(system, system != null ? system.PrimaryStar : null, hours);
			Vector3d away = p - star;
			double r = Math.Max(0.05, away.Magnitude);
			double lengthAu = comet.TailLengthMillionKm * 1e6 / CelestialMath.AuKm / (r * r);
			return p + away.Normalized * Math.Min(lengthAu, 5.0);
		}

		/// <summary>Brightness of a comet at a time, relative to its authored 1 AU value.</summary>
		public static double CometBrightness(SolarSystemProfile system, CometBody comet, double hours)
		{
			Vector3d p = CelestialMath.Position(system, comet, hours);
			Vector3d star = CelestialMath.Position(system, system != null ? system.PrimaryStar : null, hours);
			double r = Math.Max(0.05, (p - star).Magnitude);
			return comet.Brightness / (r * r * r * r);
		}

		/// <summary>
		/// The heliocentric position (AU) of one asteroid of a belt. Circular, seeded orbits whose
		/// periods follow Kepler's law against the home year.
		/// </summary>
		public static Vector3d AsteroidPosition(SolarSystemProfile system, AsteroidBelt belt, int index, double hours)
		{
			uint h = Hash((uint)belt.Seed, (uint)index);
			double u1 = (h & 0xffff) / 65535.0;
			double u2 = ((h >> 16) & 0xffff) / 65535.0;
			uint h2 = Hash(h, 0x9e3779b9);
			double u3 = (h2 & 0xffff) / 65535.0;
			double u4 = ((h2 >> 16) & 0xffff) / 65535.0;

			double inner = Math.Min(belt.InnerAU, belt.OuterAU), outer = Math.Max(belt.InnerAU, belt.OuterAU);
			double radius = inner + (outer - inner) * u1;
			double homeDistance = system != null && system.HomeWorld != null ? Math.Max(1e-6, system.HomeWorld.Orbit.Distance) : 1.0;
			double period = CelestialMath.YearHours(system) * Math.Pow(radius / homeDistance, 1.5);
			double angle = CelestialMath.TwoPi * (u2 + hours / period);
			double inclination = (u3 * 2.0 - 1.0) * belt.ThicknessDegrees * CelestialMath.Deg2Rad;
			double node = u4 * CelestialMath.TwoPi;
			double x = Math.Cos(angle) * radius, y = Math.Sin(angle) * radius;
			// Tilt the orbit about its line of nodes.
			double cn = Math.Cos(node), sn = Math.Sin(node);
			double xr = x * cn + y * sn, yr = -x * sn + y * cn;
			double yi = yr * Math.Cos(inclination), zi = yr * Math.Sin(inclination);
			return new Vector3d(xr * cn - yi * sn, xr * sn + yi * cn, zi);
		}

		/// <summary>Visible asteroids across every belt, capped by the limits.</summary>
		public static int VisibleAsteroidCount(SolarSystemProfile system)
		{
			if (system == null)
			{
				return 0;
			}
			int total = 0;
			foreach (AsteroidBelt belt in system.AsteroidBelts)
			{
				total += belt != null ? belt.Count : 0;
			}
			return Math.Min(total, system.Limits != null ? system.Limits.VisibleAsteroids : total);
		}

		/// <summary>Meteors per hour from every shower plus the sporadic rate, on a day of the year.</summary>
		public static float MeteorRate(SolarSystemProfile system, double dayOfYear)
		{
			if (system == null)
			{
				return 0f;
			}
			float rate = system.SporadicMeteorsPerHour;
			foreach (MeteorShower shower in system.MeteorShowers)
			{
				if (shower != null)
				{
					rate += shower.RateOn(dayOfYear, system.DaysPerYear);
				}
			}
			return rate;
		}

		private static uint Hash(uint a, uint b)
		{
			uint h = a * 0x85ebca6b ^ (b + 0x165667b1 + (a << 6) + (a >> 2));
			h ^= h >> 15;
			h *= 0x2c1b3c6d;
			h ^= h >> 12;
			h *= 0x297a2d39;
			h ^= h >> 15;
			return h;
		}
	}
}
