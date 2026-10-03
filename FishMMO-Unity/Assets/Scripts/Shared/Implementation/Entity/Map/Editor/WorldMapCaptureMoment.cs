using System;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldMaps
{
	/// <summary>
	/// The one moment every world map is photographed at: local solar noon on the first equinox of
	/// the scene's body's year.
	/// </summary>
	/// <remarks>
	/// <para><b>Why a fixed moment at all.</b> A baked map is build output, made again for every
	/// client build. Photographed at whatever time the editor happened to be showing, two builds of
	/// an unchanged scene shipped two different maps — dawn shadows in one, a flat grey overcast in
	/// the next — and a player comparing a map with the world saw neither. Fixed, an unchanged scene
	/// bakes the same picture every time.</para>
	///
	/// <para><b>Why noon on an equinox.</b> Noon is the highest the sun stands that day, so the
	/// overhead shot has the shortest shadows and the most even light, and the sun is on the
	/// scene's meridian, so what shading there is falls the same way on every map of a body. The
	/// equinox puts the sun over the equator, which is the one date that treats both hemispheres
	/// alike: a high-latitude scene is not photographed in its midwinter half-light because a
	/// southern one is in midsummer. The first equinox after the world clock's epoch is taken (the
	/// sun crossing the body's equator northward, right ascension 0), so the moment is a pure
	/// function of the solar system and the scene's place in it.</para>
	///
	/// <para><b>Time zones.</b> Noon is found at the longitude the scene takes its time of day at
	/// (<see cref="WorldSceneSettings.Longitude"/>, which honours an atlas time-zone override),
	/// because that is the longitude <see cref="WorldDayNightCycle"/> hands the sky. The sun drawn at
	/// "12:00" in the game and the sun in the map are then the same sun.</para>
	/// </remarks>
	public static class WorldMapCaptureMoment
	{
		/// <summary>Samples per year when looking for the equinox; bisection does the rest.</summary>
		private const int YearSamples = 720;

		/// <summary>Bisection steps: each halves a 1/720-year bracket, so 48 is far past a millisecond.</summary>
		private const int RefineSteps = 48;

		/// <summary>
		/// The world time, in hours, of local solar noon on the first equinox of the body's year, or
		/// null when there is no solar system, body or star to say when that is.
		/// </summary>
		/// <param name="system">The solar system.</param>
		/// <param name="body">The body the scene stands on.</param>
		/// <param name="longitudeDegrees">The longitude the scene keeps its time of day at.</param>
		public static double? EquinoxNoonHours(SolarSystemProfile system, WorldBody body, double longitudeDegrees)
		{
			double? equinox = EquinoxHours(system, body);
			if (!equinox.HasValue)
			{
				return null;
			}
			double hours = equinox.Value;
			double day = CelestialMath.SolarDayHours(system, body);
			if (double.IsInfinity(day) || double.IsNaN(day) || day <= 0.0)
			{
				// A body locked to its sun has no noon: the sun stands still in its sky, and any
				// moment of the equinox shows it where it always is.
				return hours;
			}
			/* Walk to the nearest noon. The sun's own drift along its path in the half day this moves
			 * shifts the meridian a little, so a second and third pass take up what the first left;
			 * after three the error is far below a second. The shift is wrapped to ±half a day, so
			 * this is the noon nearest the equinox, never one a day away. */
			for (int pass = 0; pass < 3; pass++)
			{
				double local = CelestialMath.LocalTime01(system, body, hours, longitudeDegrees);
				double shift = 0.5 - local;
				shift -= Math.Round(shift);
				hours += shift * day;
			}
			return hours;
		}

		/// <summary>
		/// The world time, in hours, of the first northward equinox at or after the epoch: the moment
		/// the primary sun's right ascension in the body's sky passes through 0. Null without a
		/// system, body or star, or when the sun never crosses (a degenerate orbit).
		/// </summary>
		public static double? EquinoxHours(SolarSystemProfile system, WorldBody body)
		{
			if (system == null || body == null || system.PrimaryStar == null)
			{
				return null;
			}
			double year = CelestialMath.OrbitHours(system, CelestialMath.HostPlanet(body));
			if (double.IsInfinity(year) || double.IsNaN(year) || year <= 0.0)
			{
				return null;
			}
			double step = year / YearSamples;
			double previousHours = 0.0;
			double previous = RightAscension(system, body, previousHours);
			// One sample past a whole year, so a crossing that falls exactly on hour 0 of the next
			// year is still bracketed.
			for (int i = 1; i <= YearSamples + 1; i++)
			{
				double hours = i * step;
				double ra = RightAscension(system, body, hours);
				// Through zero, rising; a jump of more than half a turn is the ±π seam, not a crossing.
				if (previous < 0.0 && ra >= 0.0 && ra - previous < Math.PI)
				{
					return Refine(system, body, previousHours, hours);
				}
				previous = ra;
				previousHours = hours;
			}
			return null;
		}

		/// <summary>Bisects a bracket in which the sun's right ascension rises through zero.</summary>
		private static double Refine(SolarSystemProfile system, WorldBody body, double low, double high)
		{
			for (int i = 0; i < RefineSteps; i++)
			{
				double middle = 0.5 * (low + high);
				if (RightAscension(system, body, middle) < 0.0)
				{
					low = middle;
				}
				else
				{
					high = middle;
				}
			}
			return 0.5 * (low + high);
		}

		/// <summary>The primary sun's right ascension in the body's sky, wrapped to (−π, π].</summary>
		private static double RightAscension(SolarSystemProfile system, WorldBody body, double hours)
		{
			CelestialMath.SunEquatorial(system, body, hours, out double rightAscension, out _);
			return CelestialMath.WrapPi(rightAscension);
		}
	}
}
