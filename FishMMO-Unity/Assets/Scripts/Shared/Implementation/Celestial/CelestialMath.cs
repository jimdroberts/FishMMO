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
			if (body == null)
			{
				return double.PositiveInfinity;
			}
			if (body is StarBody star)
			{
				return StarOrbitHours(system, star);
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
		// ── Stars that orbit one another ───────────────────────────────

		/// <summary>
		/// A star's mass in suns, from how bright it is: on the main sequence luminosity goes as about
		/// the 3.5th power of mass. A star has no mass field, and the one thing the mass is wanted for —
		/// which of a pair swings wide and which hardly moves — follows the brightness closely enough.
		/// </summary>
		public static double StarMass(StarBody star) => star != null ? Math.Pow(Math.Max(1e-4, star.Luminosity), 1.0 / 3.5) : 1.0;

		/// <summary>The star a companion goes round: the star it is parented to, or for a star at the root, the primary.</summary>
		private static StarBody PartnerOf(SolarSystemProfile system, StarBody star)
		{
			if (star == null || system == null)
			{
				return null;
			}
			if (star.Parent is StarBody parent)
			{
				return parent;
			}
			StarBody primary = system.PrimaryStar;
			return star.Parent == null && !ReferenceEquals(star, primary) ? primary : null;
		}

		/// <summary>
		/// How long a star takes to go round its partner; infinite for a star that has none. The
		/// primary of a system of several takes its first companion's period, since that is the
		/// swing it makes.
		/// </summary>
		public static double StarOrbitHours(SolarSystemProfile system, StarBody star)
		{
			if (system == null || star == null)
			{
				return double.PositiveInfinity;
			}
			StarBody partner = PartnerOf(system, star);
			if (partner == null)
			{
				foreach (CelestialBody candidate in system.Bodies)
				{
					if (candidate is StarBody other && !ReferenceEquals(other, star) && ReferenceEquals(PartnerOf(system, other), star))
					{
						return StarOrbitHours(system, other);
					}
				}
				return double.PositiveInfinity;
			}
			if (star.Orbit.Distance <= 1e-9f)
			{
				return double.PositiveInfinity;
			}
			if (star.Orbit.PeriodMode == OrbitPeriodMode.Authored)
			{
				return Math.Max(0.001, star.Orbit.PeriodDays) * HomeSolarDayHours(system);
			}
			// Kepler, against the home world's year: the same law the planets use, with the pair's
			// combined mass in place of the one star the home year was measured round.
			double homeDistance = system.HomeWorld != null ? Math.Max(1e-6, system.HomeWorld.Orbit.Distance) : 1.0;
			double reference = Math.Max(1e-6, StarMass(system.PrimaryStar));
			double pair = Math.Max(1e-6, StarMass(star) + StarMass(partner));
			return YearHours(system) * Math.Pow(star.Orbit.Distance / homeDistance, 1.5) / Math.Sqrt(pair / reference);
		}

		/// <summary>Where a companion star stands from its partner (AU): its own orbit, as authored.</summary>
		private static Vector3d StarSeparation(SolarSystemProfile system, StarBody star, double hours)
		{
			OrbitSettings orbit = star.Orbit;
			double period = StarOrbitHours(system, star);
			if (orbit.Distance <= 1e-9f || double.IsInfinity(period))
			{
				return new Vector3d(0, 0, 0);
			}
			double e = Math.Min(0.99, Math.Max(0.0, orbit.Eccentricity));
			double eccentric = SolveKepler(TwoPi * hours / period + orbit.OffsetDegrees * Deg2Rad, e);
			double trueAnomaly = 2.0 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(eccentric / 2), Math.Sqrt(1 - e) * Math.Cos(eccentric / 2));
			double radius = orbit.Distance * (1 - e * Math.Cos(eccentric));
			double inclination = orbit.InclinationDegrees * Deg2Rad;
			double x = Math.Cos(trueAnomaly) * radius, y = Math.Sin(trueAnomaly) * radius;
			return new Vector3d(x, y * Math.Cos(inclination), y * Math.Sin(inclination));
		}

		/// <summary>
		/// Where a star is. One star alone is the centre of its system. Several at the root go round
		/// the centre of mass they share, which is what stays at the origin.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The first star is the primary. Every other star at the root is its companion: that star's
		/// own orbit is where it stands from the primary and how long it takes. The primary is pushed
		/// the other way by each companion in proportion to their masses, so the two of a pair swing
		/// round the point between them — the heavier close in, the lighter wide — and face each other
		/// across it all the way round. With more than two this is a hierarchy about the primary and
		/// not an n-body solution, which is what a sky needs and all it needs.
		/// </para>
		/// <para>
		/// Stars used not to move at all: every root star was pinned to the origin, on top of the
		/// others, and a star parented to another took the planets' branch with an infinite period
		/// and hung at one fixed point for ever.
		/// </para>
		/// </remarks>
		private static Vector3d StarPosition(SolarSystemProfile system, StarBody star, double hours)
		{
			if (system == null || star == null)
			{
				return new Vector3d(0, 0, 0);
			}
			if (star.Parent is StarBody parent)
			{
				return Position(system, parent, hours) + StarSeparation(system, star, hours);
			}
			if (star.Parent != null)
			{
				return new Vector3d(0, 0, 0);
			}
			StarBody primary = system.PrimaryStar;
			if (primary == null)
			{
				return new Vector3d(0, 0, 0);
			}
			// The primary's swing: away from each companion, by that companion's share of the mass.
			double total = StarMass(primary);
			foreach (CelestialBody candidate in system.Bodies)
			{
				if (candidate is StarBody other && other.Parent == null && !ReferenceEquals(other, primary))
				{
					total += StarMass(other);
				}
			}
			var primaryAt = new Vector3d(0, 0, 0);
			foreach (CelestialBody candidate in system.Bodies)
			{
				if (candidate is StarBody other && other.Parent == null && !ReferenceEquals(other, primary))
				{
					primaryAt = primaryAt - StarSeparation(system, other, hours) * (StarMass(other) / total);
				}
			}
			return ReferenceEquals(star, primary) ? primaryAt : primaryAt + StarSeparation(system, star, hours);
		}

		public static Vector3d Position(SolarSystemProfile system, CelestialBody body, double hours)
		{
			if (body == null)
			{
				return new Vector3d(0, 0, 0);
			}
			if (body is StarBody asStar)
			{
				return StarPosition(system, asStar, hours);
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
			var around = new Vector3d(flat.X, flat.Y * Math.Cos(inclination), flat.Y * Math.Sin(inclination));
			// Round the star it belongs to, wherever that star is: one star of a pair carries its own
			// planets with it. A body with no parent goes round the system's centre of mass — round
			// both stars of a close pair. A lone star is at the origin, so nothing moves for a system
			// of one.
			return body.Parent is StarBody host ? Position(system, host, hours) + around : around;
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

		/// <summary>
		/// A direction from the ecliptic frame into a body's equatorial frame, for an axis that leans
		/// toward ecliptic longitude 90° (rotation about +X by the tilt).
		/// </summary>
		public static void ToEquatorial(Vector3d direction, double tiltRadians, out double rightAscension, out double declination)
		{
			ToEquatorial(direction, tiltRadians, WorldBody.DefaultPoleLongitudeDegrees * Deg2Rad, out rightAscension, out declination);
		}

		/// <summary>
		/// A direction from the ecliptic frame into a body's equatorial frame: its axis tilted by
		/// <paramref name="tiltRadians"/> toward ecliptic longitude <paramref name="poleLongitudeRadians"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Two turns. First about the ecliptic's pole, to bring the direction the axis leans round to
		/// +Y; then about +X by the tilt, which is the rotation there always was. The pole of the body
		/// is therefore at (sin t · cos L, sin t · sin L, cos t) in the ecliptic, and right ascension
		/// is counted from the body's own equinox — where its equator crosses its orbit — which is
		/// what makes every body's seasons its own.
		/// </para>
		/// <para>
		/// With only the second turn every axis in a system leaned the same way: all its worlds and
		/// moons had midsummer at the same moment, and any two with the same tilt shared a pole star.
		/// </para>
		/// </remarks>
		public static void ToEquatorial(Vector3d direction, double tiltRadians, double poleLongitudeRadians, out double rightAscension, out double declination)
		{
			Vector3d d = direction.Normalized;
			// About the ecliptic's pole, by the lean's longitude back to +Y.
			double turn = Math.PI * 0.5 - poleLongitudeRadians;
			double x = d.X * Math.Cos(turn) - d.Y * Math.Sin(turn);
			double yFlat = d.X * Math.Sin(turn) + d.Y * Math.Cos(turn);
			double y = yFlat * Math.Cos(tiltRadians) - d.Z * Math.Sin(tiltRadians);
			double z = yFlat * Math.Sin(tiltRadians) + d.Z * Math.Cos(tiltRadians);
			rightAscension = Math.Atan2(y, x);
			declination = Math.Asin(Math.Max(-1.0, Math.Min(1.0, z)));
		}

		/// <summary>
		/// Where a body's north pole points, in the ecliptic frame. Its rings lie in the plane this is
		/// the normal of. A body that is not a world has no tilt of its own and stands upright.
		/// </summary>
		public static Vector3d PoleOf(CelestialBody body)
		{
			if (!(body is WorldBody world))
			{
				return new Vector3d(0, 0, 1);
			}
			double t = world.AxialTiltDegrees * Deg2Rad, l = world.PoleLongitudeDegrees * Deg2Rad;
			return new Vector3d(Math.Sin(t) * Math.Cos(l), Math.Sin(t) * Math.Sin(l), Math.Cos(t));
		}

		/// <summary>A direction from the ecliptic frame into this body's own equatorial frame. No body, no tilt.</summary>
		public static void ToEquatorial(Vector3d direction, WorldBody body, out double rightAscension, out double declination)
		{
			ToEquatorial(direction,
				(body != null ? body.AxialTiltDegrees : 0.0) * Deg2Rad,
				(body != null ? body.PoleLongitudeDegrees : WorldBody.DefaultPoleLongitudeDegrees) * Deg2Rad,
				out rightAscension, out declination);
		}

		/// <summary>The tilt at which a world's seasons are as strong as the weather model's were tuned for.</summary>
		public const double ReferenceTiltDegrees = 23.4;

		/// <summary>
		/// The season on a body, from where its sun actually stands in its sky: 0 midwinter, 0.25
		/// spring, 0.5 midsummer, 0.75 autumn — for the NORTHERN hemisphere, as the weather driver
		/// reads it (it turns the figure round itself for the southern).
		/// </summary>
		/// <remarks>
		/// <para>
		/// The weather used to take its season from the fraction of the home calendar year gone, and
		/// put midsummer at the middle of it. That is a calendar, not a season. It was a quarter of a
		/// year out from the sky on the home world — the sun stood highest at 0.75 of the year while
		/// the weather had midsummer at 0.5 — and it was the home world's on every world: a moon, or
		/// a planet with a year three times as long, had the home world's seasons, at full strength,
		/// whatever its own axis was doing.
		/// </para>
		/// <para>
		/// Taken from the sun's declination, the season is the body's own: when it falls follows the
		/// way the axis leans and how long the body's year is, and how strong it is follows the tilt.
		/// A moon tilted a degree and a half has almost no seasons; a world tilted sixty has them
		/// hard, with a long midsummer and a long midwinter. Shaped so that the driver's own
		/// <c>sin(season · 2π − π/2)</c> gives back exactly the summer the declination asks for, so
		/// nothing about the driver, its shader twin or its calibration has to know any of this.
		/// </para>
		/// </remarks>
		public static float Season01(SolarSystemProfile system, WorldBody body, double hours)
		{
			if (system == null || body == null || system.PrimaryStar == null)
			{
				return 0.5f;
			}
			Equatorial(system, body, system.PrimaryStar, hours, out double rightAscension, out double declination);
			// How far toward midsummer, -1..1: the sun's declination against the reference tilt.
			double summer = Math.Max(-1.0, Math.Min(1.0, declination / (ReferenceTiltDegrees * Deg2Rad)));
			// The driver's curve, inverted: 0.25 at the equinox, 0.5 at full summer, 0 at full winter.
			double rising = (Math.Asin(summer) + Math.PI * 0.5) / TwoPi;
			// Spring and autumn have the same declination; the sun's right ascension says which. It
			// passes 90° at midsummer, so past that and before 270° the year is on its way down.
			double ra = rightAscension - Math.Floor(rightAscension / TwoPi) * TwoPi;
			bool falling = ra > Math.PI * 0.5 && ra < Math.PI * 1.5;
			return (float)(falling ? 1.0 - rising : rising);
		}

		/// <summary>Where a target stands in a body's equatorial sky.</summary>
		public static void Equatorial(SolarSystemProfile system, WorldBody observer, CelestialBody target, double hours, out double rightAscension, out double declination)
		{
			Vector3d toTarget = Position(system, target, hours) - Position(system, observer, hours);
			ToEquatorial(toTarget, observer, out rightAscension, out declination);
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
					/* Floored at the star's own surface, because that is the closest anything can
					 * physically get to it and so the brightest its light can be. The floor used to
					 * be an arbitrary 1e-4 AU — about 15,000 km, well inside any real star — which
					 * meant a body that happened to coincide with one received 100,000,000 times the
					 * home world's sunlight. That is not a number that stays put: insolation feeds
					 * ClimateOffsets, which feeds the temperature, which feeds the weather driver,
					 * so one piece of misplaced content produced a whole system of nonsense
					 * rather than something merely very hot. Two bodies CAN coincide: a companion
					 * star at its barycentre distance sits exactly where a planet is at epoch, which
					 * is how this was found. */
					double surfaceAu = Math.Max(1e-9, star.SkyRadiusKm / AuKm);
					double d = Math.Max(surfaceAu, (Position(system, star, hours) - p).Magnitude);
					total += star.Luminosity / (d * d);
				}
			}
			return total;
		}

		/// <summary>The home world's starlight at its mean distance: the "1.0" every climate offset is measured from.</summary>
		/// <summary>
		/// A body's climate offsets averaged over its orbit, rather than at one moment of it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// What the TERRAIN should be built from. Biomes are the slow shape of a world — a forest
		/// does not become tundra in winter — so they need the mean, while the weather rightly reads
		/// the instantaneous value and gives the same world its seasons.
		/// </para>
		/// <para>
		/// Sampled around the orbit rather than taken at hour zero, which would bake in whatever
		/// phase the body happened to start at: on an eccentric orbit that is the difference between
		/// a world's perihelion and its aphelion, and picking one of them arbitrarily would make a
		/// world's biomes depend on where the epoch fell.
		/// </para>
		/// </remarks>
		/// <param name="latitudeDegrees">
		/// Where on the body, so the poles come out colder than the equator. Null averages the body
		/// as a whole, which is what a question about the WORLD wants rather than about a place on it.
		/// </param>
		public static void MeanClimateOffsets(SolarSystemProfile system, WorldBody body, out float temperature, out float humidity, double? latitudeDegrees = null)
		{
			temperature = 0f;
			humidity = 0f;
			if (system == null || body == null)
			{
				return;
			}
			double period = OrbitHours(system, body);
			if (double.IsInfinity(period) || double.IsNaN(period) || period <= 0.0)
			{
				ClimateOffsets(system, body, 0.0, out temperature, out humidity, latitudeDegrees);
				return;
			}
			/* Sampled around the orbit, which is what makes this mean anything at a latitude: the
			 * whole point of an axial tilt is that a pole leans into its sun for half a year and
			 * away for the other half. One sample would catch a pole in perpetual summer or
			 * perpetual winter and call that its climate. Eight is enough to average a tilt out
			 * while still separating a genuinely cold pole from a warm equator. */
			const int Samples = 8;
			double t = 0.0, h = 0.0;
			for (int i = 0; i < Samples; i++)
			{
				ClimateOffsets(system, body, period * i / Samples, out float ti, out float hi, latitudeDegrees);
				t += ti;
				h += hi;
			}
			temperature = (float)(t / Samples);
			humidity = (float)(h / Samples);
		}

		/// <summary>
		/// The temperature a standard-atmosphere world would have at this distance from the system's
		/// centre, on the same -1..1 scale everything else uses.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The same arithmetic <see cref="ClimateOffsets"/> runs — fourth root of relative insolation
		/// against the home world's mean — with the body replaced by a point at a radius. That is the
		/// point of it: the goldilocks band drawn on the orrery and the biomes a world actually
		/// resolves come out of ONE function, so a ring cannot promise a climate the ground then
		/// contradicts.
		/// </para>
		/// <para>
		/// Sums every star, so a binary's habitable band is the real one and not the primary's.
		/// </para>
		/// <para>
		/// <b>A single star's brightness cancels out, and that is not a bug.</b> Everything here is
		/// relative to the home world's own starlight, so making the one sun brighter warms the
		/// reference by exactly as much and the band stays where it was — measured in AU it is
		/// always about 0.52 to 2.30, with the home world's orbit inside it. The band moves when the
		/// home world moves, or when a SECOND star is added and the two stop scaling together. That
		/// follows from the home world being the definition of temperate, which is the same
		/// assumption the biome envelopes are authored against.
		/// </para>
		/// </remarks>
		public static float TemperatureAtDistance(SolarSystemProfile system, double au)
		{
			if (system == null)
			{
				return 0f;
			}
			double d = Math.Max(1e-6, au);
			double total = 0.0;
			foreach (CelestialBody candidate in system.Bodies)
			{
				if (candidate is StarBody star)
				{
					// From the system's centre, which is where the orrery draws its rings about.
					total += star.Luminosity / (d * d);
				}
			}
			double relative = total / Math.Max(1e-6, MeanHomeInsolation(system));
			double t = 2.2 * (Math.Pow(Math.Max(0.0, relative), 0.25) - 1.0);
			return (float)Math.Max(-1.0, Math.Min(1.0, t));
		}

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

		/// <summary>How much colder a latitude is than the equator at equinox, at most.</summary>
		/// <remarks>
		/// <para>
		/// Measured, not chosen. At 0.6 the mean sea-level temperature ran from 0.33 at the equator
		/// to −0.18 at the pole, and NOT ONE authored biome envelope fell inside that band: Jungle
		/// wants 0.6, Desert 0.4, Tundra −0.5, Glacier −0.6. Every biome on every world was being
		/// picked as a nearest miss in the temperate middle, so a pole and an equator resolved to
		/// nearly the same ground and the cold biomes could only ever be painted by hand.
		/// </para>
		/// <para>
		/// At 1.8, with a sea-level anchor of 0.8, the span runs 0.73 to −0.78 and the whole
		/// authored range is reachable from latitude alone — real tropics, real ice caps. If the
		/// biome envelopes are ever re-authored, re-measure this against them.
		/// </para>
		/// </remarks>
		public const double LatitudeCooling = 1.8;

		/// <summary>
		/// The temperature shift of a latitude right now: from how high the sun climbs at noon,
		/// which carries both the pole-ward chill and the seasons from the body's tilt. 0 with
		/// the sun overhead; <see cref="LatitudeCooling"/> colder when it never rises.
		/// </summary>
		public static double LatitudeTemperature(SolarSystemProfile system, WorldBody body, double hours, double latitudeDegrees)
		{
			double declination = 0.0;
			if (system != null && body != null)
			{
				SunEquatorial(system, body, hours, out _, out declination);
			}
			double noonAltitude = 90.0 - Math.Abs(latitudeDegrees - declination * Rad2Deg);
			double height = Math.Max(0.0, Math.Sin(noonAltitude * Deg2Rad));
			return LatitudeCooling * (height - 1.0);
		}

		// ── Internal heat ──────────────────────────────────────────────

		/// <summary>
		/// Tidal heating a moon receives from the world it circles, relative to Io's.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The reason Io is the most volcanic body in the solar system and Europa has an ocean
		/// under its ice, while our own larger, colder Moon is geologically dead. A moon on an
		/// eccentric orbit is squeezed and released once per circuit, and that flexing heats it
		/// from within — heat that owes nothing at all to its star.
		/// </para>
		/// <para>
		/// Heating goes as the parent's mass squared, the eccentricity squared, and the inverse
		/// <b>sixth</b> power of the distance. The distance term is what makes this a switch rather
		/// than a gradient: doubling a moon's orbit divides its heating by sixty-four, so a system
		/// has a couple of tormented inner moons and a lot of dead outer ones, exactly as Jupiter's
		/// does. Mass is taken from radius cubed at constant density, which is the only mass the
		/// project carries.
		/// </para>
		/// <para>
		/// Returns 0 for anything that is not a moon of a world. A planet is not being squeezed by
		/// its star at any distance a planet survives at.
		/// </para>
		/// </remarks>
		public static double TidalHeating(SolarSystemProfile system, WorldBody body)
		{
			if (body == null || !(body.Parent is WorldBody parent))
			{
				return 0.0;
			}
			double eccentricity = Math.Max(0.0, body.Orbit.Eccentricity);
			double distance = Math.Max(1e-6, body.Orbit.Distance);
			if (eccentricity <= 1e-4)
			{
				// A perfectly circular orbit is never squeezed: the tide does not move.
				return 0.0;
			}

			/* Mass SQUARED, which is not a detail. Tidal heating goes as the square of the
			 * parent's mass, and with it linear our own Moon came out a quarter as tormented as Io
			 * — "warm", with volcanism to match. It is geologically dead, and the reason is exactly
			 * this term: Jupiter is eleven times Earth's radius, so more than a thousand times its
			 * mass, and squaring that is what separates a moon being kneaded molten from one that
			 * froze solid billions of years ago. */
			double parentMass = Math.Pow(Math.Max(1.0, parent.SkyRadiusKm), 3.0);
			double referenceMass = Math.Pow(IoParentRadiusKm, 3.0);
			double reference = referenceMass * referenceMass * IoEccentricity * IoEccentricity
				/ Math.Pow(IoDistance, 6.0);
			double heating = parentMass * parentMass * eccentricity * eccentricity / Math.Pow(distance, 6.0);
			return reference > 0.0 ? heating / reference : 0.0;
		}

		/// <summary>Jupiter's radius in kilometres, the reference for tidal heating.</summary>
		public const double IoParentRadiusKm = 69911.0;

		/// <summary>Io's orbital eccentricity, small but never zero because the other moons keep pumping it.</summary>
		public const double IoEccentricity = 0.0041;

		/// <summary>Io's distance in the project's own orbit units, matching how moons are authored.</summary>
		public const double IoDistance = 422.0;

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
		public static void ClimateOffsets(SolarSystemProfile system, WorldBody body, double hours, out float temperature, out float humidity, double? latitudeDegrees = null)
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
			if (latitudeDegrees.HasValue)
			{
				t += LatitudeTemperature(system, body, hours, latitudeDegrees.Value);
			}
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
