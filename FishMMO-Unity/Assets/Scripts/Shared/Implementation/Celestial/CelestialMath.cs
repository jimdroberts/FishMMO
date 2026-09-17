using System;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// Where things are in the sky, as pure functions of world time. The server and every client
	/// run exactly this code, so a sunrise, a moon phase or an eclipse happens at the same moment
	/// everywhere without a message being sent.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Units: world time is in real <b>hours</b> since the calendar epoch (double), distances in AU,
	/// angles in radians unless a name says degrees. Positions are heliocentric with the home
	/// world's orbit as the reference plane and +X toward the world-epoch equinox.
	/// </para>
	/// <para>
	/// Day and night come from each body's rotation and the sun's direction. A body's local time
	/// is the sun's hour angle at the scene's longitude: 12:00 when the sun crosses the meridian.
	/// Time zones are therefore just 15° longitude bands.
	/// </para>
	/// </remarks>
	public static class CelestialMath
	{
		public const double AuKm = 149_597_870.7;
		public const double TwoPi = Math.PI * 2.0;
		public const double Deg2Rad = Math.PI / 180.0;
		public const double Rad2Deg = 180.0 / Math.PI;
		/// <summary>Sun altitude at which the upper limb touches the horizon (refraction included).</summary>
		public const double SunriseAltitudeDegrees = -0.83;

		// ── Periods ────────────────────────────────────────────────────

		/// <summary>
		/// The home world's solar day, in real hours. Its year is <see cref="SolarSystemProfile.DaysPerYear"/>
		/// of these, which closes the loop: prograde S = P(1 + 1/n), retrograde S = P(1 − 1/n).
		/// </summary>
		public static double HomeSolarDayHours(SolarSystemProfile system)
		{
			WorldBody home = system != null ? system.HomeWorld : null;
			double p = home != null ? Math.Max(0.05, home.RotationHours) : 6.0;
			double n = system != null ? system.DaysPerYear : 365;
			return home != null && home.Retrograde ? p * (1.0 - 1.0 / n) : p * (1.0 + 1.0 / n);
		}

		/// <summary>The home world's orbital period (one calendar year), in real hours.</summary>
		public static double YearHours(SolarSystemProfile system)
		{
			return (system != null ? system.DaysPerYear : 365) * HomeSolarDayHours(system);
		}

		/// <summary>A body's orbital period in real hours. Infinite for a star.</summary>
		public static double OrbitHours(SolarSystemProfile system, CelestialBody body)
		{
			if (body == null || body is StarBody)
			{
				return double.PositiveInfinity;
			}
			if (system != null && ReferenceEquals(body, system.HomeWorld))
			{
				return YearHours(system);
			}
			bool isMoon = body.Parent != null && !(body.Parent is StarBody);
			if (isMoon || body.Orbit.PeriodMode == OrbitPeriodMode.Authored)
			{
				return Math.Max(0.001, body.Orbit.PeriodDays) * HomeSolarDayHours(system);
			}
			double homeDistance = system != null && system.HomeWorld != null ? Math.Max(1e-6, system.HomeWorld.Orbit.Distance) : 1.0;
			return YearHours(system) * Math.Pow(Math.Max(1e-6, body.Orbit.Distance) / homeDistance, 1.5);
		}

		/// <summary>The planet whose orbit around the star sets a body's year: itself, or a moon's parent.</summary>
		public static CelestialBody HostPlanet(CelestialBody body)
		{
			CelestialBody b = body;
			while (b != null && b.Parent != null && !(b.Parent is StarBody))
			{
				b = b.Parent;
			}
			return b;
		}

		/// <summary>Sidereal rotation in real hours. A tidally locked body turns once per orbit.</summary>
		public static double RotationHours(SolarSystemProfile system, WorldBody body)
		{
			if (body == null)
			{
				return 6.0;
			}
			return body.TidallyLocked ? OrbitHours(system, body) : Math.Max(0.05, body.RotationHours);
		}

		/// <summary>
		/// Sun to sun, in real hours: <c>1 / |1/rotation − 1/orbit|</c>. For a locked moon this is the
		/// synodic month (its orbit measured against the sun).
		/// </summary>
		public static double SolarDayHours(SolarSystemProfile system, WorldBody body)
		{
			if (body == null)
			{
				return HomeSolarDayHours(system);
			}
			if (system != null && ReferenceEquals(body, system.HomeWorld))
			{
				return HomeSolarDayHours(system);
			}
			double hostYear = OrbitHours(system, HostPlanet(body));
			double inverse = body.TidallyLocked
				? 1.0 / OrbitHours(system, body) - 1.0 / hostYear
				: (body.Retrograde ? -1.0 : 1.0) / RotationHours(system, body) - 1.0 / hostYear;
			return Math.Abs(inverse) < 1e-12 ? double.PositiveInfinity : Math.Abs(1.0 / inverse);
		}

		// ── Positions ──────────────────────────────────────────────────

		/// <summary>Heliocentric position in AU at a world time.</summary>
		public static Vector3d Position(SolarSystemProfile system, CelestialBody body, double hours)
		{
			if (body == null || body is StarBody && (system == null || ReferenceEquals(body, system.PrimaryStar) || body.Parent == null))
			{
				return new Vector3d(0, 0, 0);
			}
			OrbitSettings orbit = body.Orbit;
			double inclination = orbit.InclinationDegrees * Deg2Rad;
			bool isMoon = body.Parent != null && !(body.Parent is StarBody);
			if (isMoon)
			{
				double a = TwoPi * hours / OrbitHours(system, body) + orbit.OffsetDegrees * Deg2Rad;
				double r = Math.Max(0.0, orbit.Distance) * 1000.0 / AuKm;
				var local = new Vector3d(Math.Cos(a) * r, Math.Sin(a) * r * Math.Cos(inclination), Math.Sin(a) * r * Math.Sin(inclination));
				return Position(system, body.Parent, hours) + local;
			}
			double e = Math.Min(0.99, Math.Max(0.0, orbit.Eccentricity));
			double meanAnomaly = TwoPi * hours / OrbitHours(system, body) + orbit.OffsetDegrees * Deg2Rad;
			double eccentric = SolveKepler(meanAnomaly, e);
			double trueAnomaly = 2.0 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(eccentric / 2), Math.Sqrt(1 - e) * Math.Cos(eccentric / 2));
			double radius = Math.Max(1e-6, orbit.Distance) * (1 - e * Math.Cos(eccentric));
			var flat = new Vector3d(Math.Cos(trueAnomaly) * radius, Math.Sin(trueAnomaly) * radius, 0);
			return new Vector3d(flat.X, flat.Y * Math.Cos(inclination), flat.Y * Math.Sin(inclination));
		}

		/// <summary>Eccentric anomaly for a mean anomaly (Newton's method).</summary>
		public static double SolveKepler(double meanAnomaly, double eccentricity)
		{
			double e = eccentricity;
			// Starting at π converges for the near-parabolic orbits comets use; M is fine otherwise.
			double E = e > 0.8 ? Math.PI : meanAnomaly;
			for (int i = 0; i < 50; i++)
			{
				double step = (E - e * Math.Sin(E) - meanAnomaly) / (1 - e * Math.Cos(E));
				E -= step;
				if (Math.Abs(step) < 1e-12)
				{
					break;
				}
			}
			return E;
		}

		// ── Rotation and the sun ───────────────────────────────────────

		/// <summary>
		/// The right ascension longitude 0 faces, in radians. A locked moon keeps its planet over
		/// longitude 0; any other body turns at its rotation rate.
		/// </summary>
		public static double RotationAngle(SolarSystemProfile system, WorldBody body, double hours)
		{
			if (body == null)
			{
				return 0;
			}
			if (body.TidallyLocked)
			{
				return TwoPi * hours / OrbitHours(system, body) + body.Orbit.OffsetDegrees * Deg2Rad + Math.PI;
			}
			double direction = body.Retrograde ? -1.0 : 1.0;
			return direction * TwoPi * hours / RotationHours(system, body) + body.RotationOffsetDegrees * Deg2Rad;
		}

		/// <summary>A direction from the ecliptic frame into a body's equatorial frame (rotation about +X by the tilt).</summary>
		public static void ToEquatorial(Vector3d direction, double tiltRadians, out double rightAscension, out double declination)
		{
			Vector3d d = direction.Normalized;
			double y = d.Y * Math.Cos(tiltRadians) - d.Z * Math.Sin(tiltRadians);
			double z = d.Y * Math.Sin(tiltRadians) + d.Z * Math.Cos(tiltRadians);
			rightAscension = Math.Atan2(y, d.X);
			declination = Math.Asin(Math.Max(-1.0, Math.Min(1.0, z)));
		}

		/// <summary>Where a target stands in a body's equatorial sky.</summary>
		public static void Equatorial(SolarSystemProfile system, WorldBody observer, CelestialBody target, double hours, out double rightAscension, out double declination)
		{
			Vector3d toTarget = Position(system, target, hours) - Position(system, observer, hours);
			ToEquatorial(toTarget, (observer != null ? observer.AxialTiltDegrees : 0) * Deg2Rad, out rightAscension, out declination);
		}

		/// <summary>The primary sun's right ascension and declination in a body's sky.</summary>
		public static void SunEquatorial(SolarSystemProfile system, WorldBody body, double hours, out double rightAscension, out double declination)
		{
			Equatorial(system, body, system != null ? (CelestialBody)system.PrimaryStar : null, hours, out rightAscension, out declination);
		}

		/// <summary>Wraps an angle into (−π, π].</summary>
		public static double WrapPi(double radians)
		{
			double a = Math.IEEERemainder(radians, TwoPi);
			return a <= -Math.PI ? a + TwoPi : a;
		}

		/// <summary>Hour angle of a right ascension at a longitude: 0 on the meridian, positive after it.</summary>
		public static double HourAngle(SolarSystemProfile system, WorldBody body, double hours, double longitudeDegrees, double rightAscension)
		{
			return WrapPi(RotationAngle(system, body, hours) + longitudeDegrees * Deg2Rad - rightAscension);
		}

		/// <summary>Local solar time as a fraction of the day: 0.5 is noon, when the sun is on the meridian.</summary>
		public static double LocalTime01(SolarSystemProfile system, WorldBody body, double hours, double longitudeDegrees)
		{
			SunEquatorial(system, body, hours, out double ra, out _);
			double h = HourAngle(system, body, hours, longitudeDegrees, ra);
			double t = h / TwoPi + 0.5;
			return t - Math.Floor(t);
		}

		/// <summary>Altitude above the horizon, in radians, from declination, latitude and hour angle.</summary>
		public static double Altitude(double latitudeDegrees, double declination, double hourAngle)
		{
			double lat = latitudeDegrees * Deg2Rad;
			double s = Math.Sin(lat) * Math.Sin(declination) + Math.Cos(lat) * Math.Cos(declination) * Math.Cos(hourAngle);
			return Math.Asin(Math.Max(-1.0, Math.Min(1.0, s)));
		}

		/// <summary>The primary sun's altitude over a place, in radians.</summary>
		public static double SunAltitude(SolarSystemProfile system, WorldBody body, double hours, double latitudeDegrees, double longitudeDegrees)
		{
			SunEquatorial(system, body, hours, out double ra, out double dec);
			return Altitude(latitudeDegrees, dec, HourAngle(system, body, hours, longitudeDegrees, ra));
		}

		/// <summary>True when any star is above the horizon at a place.</summary>
		public static bool IsDaylight(SolarSystemProfile system, WorldBody body, double hours, double latitudeDegrees, double longitudeDegrees)
		{
			if (system == null)
			{
				return true;
			}
			foreach (CelestialBody candidate in system.Bodies)
			{
				if (!(candidate is StarBody star))
				{
					continue;
				}
				Equatorial(system, body, star, hours, out double ra, out double dec);
				double altitude = Altitude(latitudeDegrees, dec, HourAngle(system, body, hours, longitudeDegrees, ra));
				if (altitude * Rad2Deg > SunriseAltitudeDegrees)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Real hours of daylight on the current day at a latitude.</summary>
		public static double DaylightHours(SolarSystemProfile system, WorldBody body, double hours, double latitudeDegrees)
		{
			SunEquatorial(system, body, hours, out _, out double dec);
			double x = -Math.Tan(latitudeDegrees * Deg2Rad) * Math.Tan(dec);
			double h0 = x <= -1 ? Math.PI : x >= 1 ? 0 : Math.Acos(x);
			return h0 / Math.PI * SolarDayHours(system, body);
		}

		// ── Appearance ─────────────────────────────────────────────────

		/// <summary>Angular diameter in radians of a body of a given radius at a given distance.</summary>
		public static double AngularDiameter(double radiusKm, double distanceKm)
		{
			if (distanceKm <= radiusKm)
			{
				return Math.PI;
			}
			return 2.0 * Math.Asin(radiusKm / distanceKm);
		}

		/// <summary>Share of the visible hemisphere a disc covers, 0..1.</summary>
		public static double SkyFraction(double angularDiameter)
		{
			return 1.0 - Math.Cos(Math.Min(Math.PI, angularDiameter) / 2.0);
		}

		/// <summary>True when a target covers more of an observer's sky than the profile's texture threshold.</summary>
		public static bool DrawsTextured(SolarSystemProfile system, WorldBody observer, CelestialBody target, double hours)
		{
			if (system == null || target == null || target is StarBody)
			{
				return false;
			}
			double km = (Position(system, target, hours) - Position(system, observer, hours)).Magnitude * AuKm;
			return SkyFraction(AngularDiameter(target.SkyRadiusKm, km)) * 100.0 >= system.TextureAboveSkyPercent;
		}

		/// <summary>Lit fraction of a target as seen from an observer, 0 (new) … 1 (full).</summary>
		public static double Illumination(SolarSystemProfile system, CelestialBody observer, CelestialBody target, double hours)
		{
			CelestialBody star = system != null ? system.PrimaryStar : null;
			Vector3d t = Position(system, target, hours);
			Vector3d toStar = (Position(system, star, hours) - t).Normalized;
			Vector3d toObserver = (Position(system, observer, hours) - t).Normalized;
			return (1.0 + Vector3d.Dot(toStar, toObserver)) / 2.0;
		}

		// ── Climate ────────────────────────────────────────────────────

		/// <summary>Starlight a body receives: Σ luminosity ÷ distance² (AU). The home world at 1 AU from one sun reads 1.</summary>
		public static double Insolation(SolarSystemProfile system, CelestialBody body, double hours)
		{
			if (system == null || body == null)
			{
				return 1.0;
			}
			Vector3d p = Position(system, body, hours);
			double total = 0;
			foreach (CelestialBody candidate in system.Bodies)
			{
				if (candidate is StarBody star)
				{
					double d = Math.Max(1e-4, (Position(system, star, hours) - p).Magnitude);
					total += star.Luminosity / (d * d);
				}
			}
			return total;
		}

		/// <summary>The home world's starlight at its mean distance: the "1.0" every climate offset is measured from.</summary>
		public static double MeanHomeInsolation(SolarSystemProfile system)
		{
			if (system == null || system.HomeWorld == null)
			{
				return 1.0;
			}
			double a = Math.Max(1e-4, system.HomeWorld.Orbit.Distance);
			double total = 0;
			foreach (CelestialBody candidate in system.Bodies)
			{
				if (candidate is StarBody star)
				{
					total += star.Luminosity / (a * a);
				}
			}
			return total > 0 ? total : 1.0;
		}

		/// <summary>Greenhouse contribution to the temperature offset for an atmosphere.</summary>
		public static double Greenhouse(AtmosphereKind atmosphere)
		{
			switch (atmosphere)
			{
				case AtmosphereKind.None: return -0.15;
				case AtmosphereKind.Thin: return -0.05;
				case AtmosphereKind.Thick: return 0.35;
				default: return 0.0;
			}
		}

		/// <summary>
		/// Climate offsets a body's distance from its star(s), its atmosphere and its water put on
		/// every scene standing on it. Both are clamped to ±1, the climate model's range.
		/// </summary>
		/// <remarks>
		/// Temperature follows the fourth root of starlight (equilibrium temperature), scaled so the
		/// home world reads ≈0. With no atmosphere humidity is pinned to its minimum.
		/// </remarks>
		public static void ClimateOffsets(SolarSystemProfile system, WorldBody body, double hours, out float temperature, out float humidity)
		{
			if (system == null || body == null)
			{
				temperature = 0f;
				humidity = 0f;
				return;
			}
			// Relative to the home world's AVERAGE starlight, so an eccentric home orbit still makes seasons.
			double homeLight = MeanHomeInsolation(system);
			double relative = Insolation(system, body, hours) / Math.Max(1e-6, homeLight);
			double homeGreenhouse = system.HomeWorld != null ? Greenhouse(system.HomeWorld.Atmosphere) : 0.0;
			double t = 2.2 * (Math.Pow(relative, 0.25) - 1.0) + Greenhouse(body.Atmosphere) - homeGreenhouse;
			temperature = (float)Math.Max(-1.0, Math.Min(1.0, t));
			if (body.Atmosphere == AtmosphereKind.None)
			{
				humidity = -1f;
				return;
			}
			double homeWater = system.HomeWorld != null ? system.HomeWorld.Water : 0.7;
			double h = (body.Water - homeWater) * 1.2 - Math.Max(0.0, t) * 0.3;
			humidity = (float)Math.Max(-1.0, Math.Min(1.0, h));
		}

		// ── Time conversions ───────────────────────────────────────────

		/// <summary>Real hours since the calendar epoch for world seconds.</summary>
		public static double HoursFromWorldSeconds(double worldSeconds) => worldSeconds / 3600.0;

		/// <summary>Whole home days since the epoch.</summary>
		public static long HomeDay(SolarSystemProfile system, double hours)
		{
			return (long)Math.Floor(hours / HomeSolarDayHours(system));
		}
	}
}
