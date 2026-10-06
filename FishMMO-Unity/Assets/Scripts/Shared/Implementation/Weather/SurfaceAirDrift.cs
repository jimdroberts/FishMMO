using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// How far the air near the ground has travelled by a moment: the exact integral of the wind the
	/// weather reports (<see cref="WeatherDriver.PrevailingSpeed"/> along
	/// <see cref="WeatherDriver.PrevailingWind(float, in WindBelts)"/>), from the calendar epoch.
	/// </summary>
	/// <remarks>
	/// <para>
	/// What the fog banks and the mist ride on. They were carried by adding up each frame's wind on
	/// each client, so a bank stood wherever that client's frame history had left it: two players
	/// saw their fog in different places, and a rejoin moved it. This is a pure function of the
	/// world clock, so every machine puts it in the same place, and still follows the wind — it
	/// creeps in a calm and runs in a gale, as the integrated one did.
	/// </para>
	/// <para>
	/// The wind is taken at knots <see cref="KnotSeconds"/> apart on the world clock and drawn
	/// straight between them; the distance is that line's exact integral, so it never steps. The
	/// sum from the epoch is kept at checkpoints and extended as the clock moves on, so a query
	/// costs a few noise samples, and only the first (or a preview's jump far ahead) walks the lot.
	/// </para>
	/// <para>
	/// Not <see cref="WeatherDriver.Drift"/>: that is the large-scale march of the air the clouds and
	/// the field ride, at a speed steady for ever within a latitude, where fog must go still when the
	/// wind drops.
	/// </para>
	/// </remarks>
	public static class SurfaceAirDrift
	{
		/// <summary>World seconds between the wind's knots.</summary>
		public const double KnotSeconds = 300.0;
		/// <summary>Knots between stored checkpoints of the running sum.</summary>
		private const int KnotsPerCheckpoint = 64;
		/// <summary>The wind at which the extra stirring <see cref="Stirred"/> counts is full, m/s.</summary>
		public const float StirWind = 8f;

		/// <summary>Running sums at knot k * KnotsPerCheckpoint, for one seed, latitude, belts and share.</summary>
		private sealed class Sums
		{
			public readonly List<double> DistanceAt = new List<double>();
			public readonly List<double> StirAt = new List<double>();
		}

		// One set of sums per thing carried (the fog banks at one share, the sea's motes at another): a
		// single set keyed on all of it was emptied and walked again from the epoch every time the two
		// asked in turn — every frame.
		private static readonly Dictionary<(uint, float, float, float, float), Sums> sums = new Dictionary<(uint, float, float, float, float), Sums>();
		private const int MaxSums = 8;

		/// <summary>
		/// Where the surface air has got to by <paramref name="worldSeconds"/>, m (x east, y north), at
		/// <paramref name="share"/> of the reported wind; and <paramref name="stirred"/>, the seconds
		/// of a clock that runs up to twice as fast in wind (<see cref="Stirred"/>).
		/// </summary>
		public static void At(uint seed, float latitudeDegrees, in WindBelts belts, float share, double worldSeconds,
			out double x, out double y, out double stirred)
		{
			Sums at = SumsFor(seed, latitudeDegrees, belts, share);
			double t = Math.Max(0.0, worldSeconds);
			long knot = (long)Math.Floor(t / KnotSeconds);
			long checkpoint = knot / KnotsPerCheckpoint;
			Extend(at, seed, latitudeDegrees, belts, share, checkpoint);

			double distance = at.DistanceAt[(int)checkpoint];
			double stir = at.StirAt[(int)checkpoint];
			for (long k = checkpoint * KnotsPerCheckpoint; k < knot; k++)
			{
				Segment(seed, latitudeDegrees, belts, share, k, 1.0, ref distance, ref stir);
			}
			double fraction = (t - knot * KnotSeconds) / KnotSeconds;
			Segment(seed, latitudeDegrees, belts, share, knot, fraction, ref distance, ref stir);

			Vector2 direction = WeatherDriver.PrevailingWind(latitudeDegrees, belts);
			x = direction.x * distance;
			y = direction.y * distance;
			stirred = t + stir;
		}

		/// <summary>The wind the drift moves at, m/s, at a knot.</summary>
		private static float Speed(uint seed, float latitudeDegrees, in WindBelts belts, float share, long knot)
		{
			return Mathf.Max(0f, share) * WeatherDriver.PrevailingSpeed(seed, latitudeDegrees, knot * KnotSeconds, belts);
		}

		/// <summary>How much faster than the clock a bank is stirred at this wind: 0 in a calm, 1 at <see cref="StirWind"/>.</summary>
		public static float Stirred(float speed) => Mathf.Clamp01(speed / StirWind);

		/// <summary>Adds the integral over the first <paramref name="fraction"/> of knot <paramref name="knot"/>'s span.</summary>
		private static void Segment(uint seed, float latitudeDegrees, in WindBelts belts, float share, long knot, double fraction,
			ref double distance, ref double stir)
		{
			if (fraction <= 0.0)
			{
				return;
			}
			double a = Speed(seed, latitudeDegrees, belts, share, knot);
			double b = Speed(seed, latitudeDegrees, belts, share, knot + 1);
			// The speed is a straight line between the knots, so its integral is a trapezoid; the
			// stirring is a straight line between the knots' own stirring, likewise.
			double end = a + (b - a) * fraction;
			distance += 0.5 * (a + end) * fraction * KnotSeconds;
			double sa = Stirred((float)a), sb = Stirred((float)b);
			double send = sa + (sb - sa) * fraction;
			stir += 0.5 * (sa + send) * fraction * KnotSeconds;
		}

		private static void Extend(Sums at, uint seed, float latitudeDegrees, in WindBelts belts, float share, long checkpoint)
		{
			List<double> distanceAt = at.DistanceAt, stirAt = at.StirAt;
			if (distanceAt.Count == 0)
			{
				distanceAt.Add(0.0);
				stirAt.Add(0.0);
			}
			while (distanceAt.Count <= checkpoint)
			{
				int last = distanceAt.Count - 1;
				double distance = distanceAt[last];
				double stir = stirAt[last];
				long first = (long)last * KnotsPerCheckpoint;
				for (long k = first; k < first + KnotsPerCheckpoint; k++)
				{
					Segment(seed, latitudeDegrees, belts, share, k, 1.0, ref distance, ref stir);
				}
				distanceAt.Add(distance);
				stirAt.Add(stir);
			}
		}

		/// <summary>Forgets every running sum (tests: the next query walks from the epoch again).</summary>
		public static void ResetCache() => sums.Clear();

		private static Sums SumsFor(uint seed, float latitudeDegrees, in WindBelts belts, float share)
		{
			var key = (seed, latitudeDegrees, belts.CellDegrees, belts.Handedness, share);
			if (!sums.TryGetValue(key, out Sums found))
			{
				if (sums.Count >= MaxSums)
				{
					sums.Clear();
				}
				found = new Sums();
				sums[key] = found;
			}
			return found;
		}
	}
}
