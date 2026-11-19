using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// How far the air at the clouds' base — and with it the clouds and the weather field — has been carried by a moment:
	/// the exact integral of the wind the weather reports, brought up to the steering level, from the calendar epoch.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why it is an integral (2026-10-15: "Clouds don't seem to be moving properly with the wind speed").</b> The field
	/// and the clouds were carried at a speed fixed for ever per latitude (4–12 m/s), because a varying speed times the
	/// elapsed time had teleported the field: <c>d/dt (v·t)</c> picks up <c>t·dv/dt</c>. Holding the speed still cured the
	/// teleport by uncoupling the sky from the wind, so a gale blew under crawling clouds and a calm under racing ones,
	/// and the climate panel's wind did nothing to the sky. The cure for the teleport is to integrate the speed rather
	/// than multiply it, as <see cref="SurfaceAirDrift"/> already does for the fog: the drift's slope is then exactly the
	/// wind, at every moment, and it never steps.
	/// </para>
	/// <para>
	/// <b>What carries it.</b> The reported ten-metre wind (<see cref="WeatherDriver.PrevailingSpeed"/>, or the still air's
	/// <see cref="StillWind"/> with the field off), plus what the scene's author added (<see cref="AirOffsets.Wind"/>) and
	/// what is added at runtime (<see cref="AirOffsetEntry"/>, an admin, the bed's sliders), clamped at a calm as
	/// <see cref="AirOffsets.Apply"/> clamps the reported wind; times <see cref="SteeringGain"/>, the law of the wall from
	/// ten metres to the top of the mixed layer, where the cloud base is: clouds run about 1.8 times the wind at the
	/// ground. Along <see cref="WeatherDriver.PrevailingWind(float, in WindBelts)"/>, plus the field's bounded wander.
	/// Storm cells and volumes are local and move what is in them, not the air of the scene.
	/// </para>
	/// <para>
	/// <b>Deterministic on every machine.</b> The reported wind and the authored offset are summed from the epoch at
	/// knots <see cref="KnotSeconds"/> apart, kept at checkpoints; the runtime entry is summed from its own start, on the
	/// same knots, and what the entries before it had carried (the air, the fog's stirring, the sails' turns) is handed
	/// on in <see cref="AirOffsetEntry.Carried"/> and its siblings (worked out once by whoever sets the entry, and sent
	/// with it; <see cref="CarryOver"/>). Every sum is
	/// over the same breakpoints whatever was asked before, so two machines asked the same moment give the same metres.
	/// </para>
	/// </remarks>
	public static class AirDrift
	{
		/// <summary>World seconds between the wind's knots.</summary>
		public const double KnotSeconds = 300.0;
		/// <summary>Knots between stored checkpoints of the running sum from the epoch.</summary>
		private const int KnotsPerCheckpoint = 64;
		/// <summary>Pieces an eased runtime change is summed in, so its S-curve is followed, not cut straight.</summary>
		private const int EaseSteps = 8;
		/// <summary>The still air's wind with the weather field switched off, m/s (<see cref="WeatherField.OpenAirAt"/>).</summary>
		public const float StillWind = 6f;
		/// <summary>The height the clouds and the weather are steered at: the top of the mixed layer, about the cloud base, m.</summary>
		public const float SteeringHeight = 1000f;

		/// <summary>The steering-level wind over the ten-metre wind, by the law of the wall (z0 3 cm): about 1.79.</summary>
		public static readonly float SteeringGain =
			Mathf.Log(SteeringHeight / WindProfile.Roughness) / Mathf.Log(WindProfile.ReferenceHeight / WindProfile.Roughness);

		/// <summary>What is summed over the wind: the distance the air goes, or the turns a windmill's sails make.</summary>
		private enum Carry
		{
			Air,
			Sails,
			Stir,
		}

		/// <summary>What a wind (m/s, ten-metre) is worth per second for a carry: metres, or a windmill's sails' turns.</summary>
		private static double Shape(Carry carry, double wind, float most)
		{
			switch (carry)
			{
				case Carry.Air:
					return wind;
				case Carry.Stir:
					// The surface air's stirring at a share (most) of the wind: SurfaceAirDrift.Stirred.
					return SurfaceAirDrift.Stirred((float)(most * wind));
				default:
					return SailRpm((float)wind, most) / 60.0;
			}
		}

		/// <summary>
		/// The share of the ten-metre wind the air near the ground moves at: the middle of a fog a few metres deep, by the
		/// law of the wall, about three quarters. The fog banks, the motes and the mist ride it (FogLayerView.DriftWind).
		/// </summary>
		public const float SurfaceShare = 0.75f;

		/// <summary>The working pace a miller lets common sails reach, turns a minute: what every windmill turns at most, and what a wind change carries the sails' turns over at.</summary>
		public const float SailMostRpm = 16f;

		/// <summary>The wind common sails start to turn in, and the gale the miller brakes them in, m/s.</summary>
		public const float SailCutIn = 2.5f, SailGale = 20f;
		/// <summary>Turns a minute per m/s over the cut-in: sails 15 m across run at a tip speed about 2.5 times the wind's.</summary>
		public const float SailRpmPerWind = 2.6f;

		/// <summary>The pace common sails settle at in a wind, turns a minute: none below the cut-in or in a gale, rising with the wind to <paramref name="most"/>.</summary>
		public static float SailRpm(float wind, float most)
		{
			if (wind < SailCutIn || wind > SailGale)
			{
				return 0f;
			}
			return Mathf.Min(most, SailRpmPerWind * (wind - SailCutIn * 0.6f));
		}

		/// <summary>What the air is carried by: the scene's weather settings, as one value.</summary>
		public struct Air
		{
			public uint Seed;
			public float Latitude;
			public WindBelts Belts;
			/// <summary>The weather field runs (else the still air's steady wind).</summary>
			public bool Driver;
			/// <summary>The scene's authored wind offset, m/s.</summary>
			public float AuthoredWind;
			/// <summary>What is added at runtime.</summary>
			public AirOffsetEntry Runtime;
		}

		/// <summary>Where the steering-level air has got to by <paramref name="worldSeconds"/>, m (x east, y north): the drift the clouds and the field ride.</summary>
		public static void At(in Air air, double worldSeconds, out double x, out double y)
		{
			double distance = SteeringGain * (Base(air, Carry.Air, 0f, worldSeconds) + Extra(air, Carry.Air, 0f, air.Runtime, worldSeconds));
			Vector2 direction = WeatherDriver.PrevailingWind(air.Latitude, air.Belts);
			x = direction.x * distance;
			y = direction.y * distance;
			if (air.Driver)
			{
				WeatherDriver.Wander(air.Seed, worldSeconds, out double wanderX, out double wanderY);
				x += wanderX;
				y += wanderY;
			}
		}

		/// <summary>How fast the steering-level air moves at a moment, m/s: the slope of <see cref="At"/>, less the slow wander.</summary>
		public static float SpeedAt(in Air air, double worldSeconds)
		{
			float ground = (float)GroundBetweenKnots(air, worldSeconds);
			float offset = air.Runtime.AtSeconds(worldSeconds).Wind;
			return SteeringGain * Mathf.Max(0f, ground + air.AuthoredWind + offset);
		}

		/// <summary>
		/// How far the ten-metre wind has carried the air by <paramref name="worldSeconds"/>, m, along
		/// <see cref="WeatherDriver.PrevailingWind(float, in WindBelts)"/>: the reported wind with what the author and the
		/// runtime add, summed exactly, before <see cref="SteeringGain"/> and with no wander. What the surface air rides at
		/// its own share (<see cref="SurfaceAirDrift"/>: the fog banks, the motes, the water's suspended matter).
		/// </summary>
		public static double GroundCarried(in Air air, double worldSeconds) =>
			Base(air, Carry.Air, 0f, worldSeconds) + Extra(air, Carry.Air, 0f, air.Runtime, worldSeconds);

		/// <summary>
		/// The seconds a surface air at <paramref name="share"/> of the wind has been stirred by now beyond the clock: the
		/// sum of <see cref="SurfaceAirDrift.Stirred"/> over the wind, exactly as the drift is summed; carried over a runtime
		/// change of the wind at <see cref="SurfaceShare"/> (<see cref="AirOffsetEntry.CarriedStir"/>).
		/// </summary>
		public static double StirredBeyond(in Air air, float share, double worldSeconds) =>
			Base(air, Carry.Stir, share, worldSeconds) + Extra(air, Carry.Stir, share, air.Runtime, worldSeconds);

		/// <summary>
		/// What an entry about to replace <paramref name="current"/> at its own start carries over: the distance (ten-metre
		/// metres, before <see cref="SteeringGain"/>) every runtime entry so far has added. Set it on the new entry's
		/// <see cref="AirOffsetEntry.Carried"/>, so the air goes on from where it was.
		/// </summary>
		public static double CarriedAt(in Air air, in AirOffsetEntry current, double startSeconds) => Extra(air, Carry.Air, 0f, current, startSeconds);

		/// <summary>
		/// Sets everything a new runtime entry carries over from the one it replaces at its start
		/// (<see cref="AirOffsetEntry.Carried"/>, <see cref="AirOffsetEntry.CarriedStir"/>,
		/// <see cref="AirOffsetEntry.CarriedSails"/>), so the clouds, the fog and the sails all go on from where they were.
		/// </summary>
		public static void CarryOver(in Air air, in AirOffsetEntry current, ref AirOffsetEntry next)
		{
			double start = next.StartSeconds;
			next.Carried = Extra(air, Carry.Air, 0f, current, start);
			next.CarriedStir = Extra(air, Carry.Stir, SurfaceShare, current, start);
			next.CarriedSails = Extra(air, Carry.Sails, SailMostRpm, current, start);
		}

		/// <summary>
		/// How many times a windmill's common sails have gone round by <paramref name="worldSeconds"/> in the scene's wind
		/// (<see cref="SailRpm"/>, at most <paramref name="mostRpm"/>), from the epoch: a pure function of the world clock,
		/// so every player sees the same sail at the same angle (2026-10-15: "It all needs to be deterministic"). The
		/// scene's wind, not a storm's gusts over the mill. At <see cref="SailMostRpm"/> a runtime change of the wind carries
		/// the turns over (<see cref="AirOffsetEntry.CarriedSails"/>); at any other most, its sum starts afresh at the change.
		/// </summary>
		public static double SailTurns(in Air air, float mostRpm, double worldSeconds) =>
			Base(air, Carry.Sails, mostRpm, worldSeconds) + Extra(air, Carry.Sails, mostRpm, air.Runtime, worldSeconds);

		// ── The reported wind and the authored offset, from the epoch ──

		/// <summary>The ten-metre wind the field reports at a moment, before anything is added, m/s.</summary>
		private static float Ground(in Air air, double worldSeconds) =>
			air.Driver ? WeatherDriver.PrevailingSpeed(air.Seed, air.Latitude, worldSeconds, air.Belts) : StillWind;

		/// <summary>
		/// The reported wind as every sum takes it, m/s: drawn straight between its knots, so the drift's slope, the
		/// runtime part's integrand and <see cref="SpeedAt"/> all read the same wind.
		/// </summary>
		private static double GroundBetweenKnots(in Air air, double worldSeconds)
		{
			if (!air.Driver || worldSeconds <= 0.0)
			{
				return Ground(air, Math.Max(0.0, worldSeconds));
			}
			long knot = (long)Math.Floor(worldSeconds / KnotSeconds);
			double a = Ground(air, knot * KnotSeconds), b = Ground(air, (knot + 1) * KnotSeconds);
			return a + (b - a) * ((worldSeconds - knot * KnotSeconds) / KnotSeconds);
		}

		/// <summary>Running sums at knot k * KnotsPerCheckpoint, and the last knot asked about, for one air.</summary>
		private sealed class Sums
		{
			public readonly List<double> DistanceAt = new List<double>();
			public long LastKnot = -1;
			public double LastDistance;
		}

		private static readonly Dictionary<(uint, float, float, float, float, Carry, float), Sums> sums = new Dictionary<(uint, float, float, float, float, Carry, float), Sums>();
		private const int MaxSums = 16;
		private static readonly object sumsLock = new object();

		/// <summary>∫ shape(max(0, reported + authored)) from the epoch to <paramref name="worldSeconds"/>.</summary>
		private static double Base(in Air air, Carry carry, float most, double worldSeconds)
		{
			double t = Math.Max(0.0, worldSeconds);
			if (!air.Driver)
			{
				// A steady wind: its integral is exact as it stands.
				return Shape(carry, Math.Max(0f, StillWind + air.AuthoredWind), most) * t;
			}
			long knot = (long)Math.Floor(t / KnotSeconds);
			double distance;
			lock (sumsLock)
			{
				Sums at = SumsFor(air, carry, most);
				if (at.LastKnot == knot)
				{
					distance = at.LastDistance;
				}
				else
				{
					long checkpoint = knot / KnotsPerCheckpoint;
					Extend(at, air, carry, most, checkpoint);
					distance = at.DistanceAt[(int)checkpoint];
					for (long k = checkpoint * KnotsPerCheckpoint; k < knot; k++)
					{
						distance += KnotSpan(air, carry, most, k, 1.0);
					}
					at.LastKnot = knot;
					at.LastDistance = distance;
				}
			}
			return distance + KnotSpan(air, carry, most, knot, (t - knot * KnotSeconds) / KnotSeconds);
		}

		/// <summary>What the base carries per second at a knot: the shape of the reported wind and the authored offset, never below a calm.</summary>
		private static double KnotSpeed(in Air air, Carry carry, float most, long knot) =>
			Shape(carry, Math.Max(0f, Ground(air, knot * KnotSeconds) + air.AuthoredWind), most);

		/// <summary>The integral over the first <paramref name="fraction"/> of knot <paramref name="knot"/>'s span: drawn straight between knots.</summary>
		private static double KnotSpan(in Air air, Carry carry, float most, long knot, double fraction)
		{
			if (fraction <= 0.0)
			{
				return 0.0;
			}
			double a = KnotSpeed(air, carry, most, knot);
			double b = KnotSpeed(air, carry, most, knot + 1);
			double end = a + (b - a) * fraction;
			return 0.5 * (a + end) * fraction * KnotSeconds;
		}

		private static void Extend(Sums at, in Air air, Carry carry, float most, long checkpoint)
		{
			List<double> distanceAt = at.DistanceAt;
			if (distanceAt.Count == 0)
			{
				distanceAt.Add(0.0);
			}
			while (distanceAt.Count <= checkpoint)
			{
				int last = distanceAt.Count - 1;
				double distance = distanceAt[last];
				long first = (long)last * KnotsPerCheckpoint;
				for (long k = first; k < first + KnotsPerCheckpoint; k++)
				{
					distance += KnotSpan(air, carry, most, k, 1.0);
				}
				distanceAt.Add(distance);
			}
		}

		private static Sums SumsFor(in Air air, Carry carry, float most)
		{
			var key = (air.Seed, air.Latitude, air.Belts.CellDegrees, air.Belts.Handedness, air.AuthoredWind, carry, most);
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

		// ── What is added at runtime, from its own start ──

		/// <summary>The sum at the last knot asked about, for one entry of one air.</summary>
		private sealed class EntrySum
		{
			public long Knot;
			public double Distance;
		}

		private static readonly Dictionary<(uint, float, float, float, float, bool, double, double, float, float, double, Carry, float), EntrySum> entrySums =
			new Dictionary<(uint, float, float, float, float, bool, double, double, float, float, double, Carry, float), EntrySum>();

		/// <summary>
		/// What the runtime entry adds to the base by a moment, m (ten-metre): what the entries before it carried, plus
		/// ∫ (max(0, wind + offset) − max(0, wind)) from its start, so a calm set by an admin stills the air rather than
		/// running it backwards.
		/// </summary>
		private static double Extra(in Air air, Carry carry, float most, in AirOffsetEntry entry, double worldSeconds)
		{
			// What the entries before this one carried, kept for the air, the fog's stirring and the sails at their
			// standard figures (AirOffsetEntry.Carried*): anything else starts its own sum at the change.
			double carried = carry == Carry.Air ? entry.Carried
				: carry == Carry.Stir && most == SurfaceShare ? entry.CarriedStir
				: carry == Carry.Sails && most == SailMostRpm ? entry.CarriedSails
				: 0.0;
			if (carried == 0.0 && entry.Carried == 0.0 && entry.From.Wind == 0f && entry.To.Wind == 0f)
			{
				return 0.0;
			}
			double start = entry.StartSeconds;
			if (worldSeconds <= start)
			{
				// Asked about before it starts (the server's lead): its From held back to then.
				return carried - Integrate(air, carry, most, entry, worldSeconds, start);
			}
			// Summed knot by knot from the start, the knots where the base's are; the last knot asked about is kept.
			long first = (long)Math.Ceiling(start / KnotSeconds);
			long knot = (long)Math.Floor(worldSeconds / KnotSeconds);
			if (knot < first)
			{
				return carried + Integrate(air, carry, most, entry, start, worldSeconds);
			}
			double distance;
			lock (sumsLock)
			{
				var key = (air.Seed, air.Latitude, air.Belts.CellDegrees, air.Belts.Handedness, air.AuthoredWind, air.Driver,
					entry.StartSeconds, entry.EndSeconds, entry.From.Wind, entry.To.Wind, entry.Carried, carry, most);
				if (!entrySums.TryGetValue(key, out EntrySum at) || at.Knot > knot)
				{
					if (entrySums.Count >= MaxSums)
					{
						entrySums.Clear();
					}
					at = new EntrySum { Knot = first, Distance = Integrate(air, carry, most, entry, start, first * KnotSeconds) };
					entrySums[key] = at;
				}
				for (long k = at.Knot; k < knot; k++)
				{
					at.Distance += Integrate(air, carry, most, entry, k * KnotSeconds, (k + 1) * KnotSeconds);
				}
				at.Knot = knot;
				distance = at.Distance;
			}
			return carried + distance + Integrate(air, carry, most, entry, knot * KnotSeconds, worldSeconds);
		}

		/// <summary>
		/// ∫ (max(0, wind + offset) − max(0, wind)) over [<paramref name="from"/>, <paramref name="to"/>], trapezoids broken
		/// at the eased change's steps so the same span always sums the same.
		/// </summary>
		private static double Integrate(in Air air, Carry carry, float most, in AirOffsetEntry entry, double from, double to)
		{
			if (to <= from)
			{
				return 0.0;
			}
			double sum = 0.0;
			double a = from;
			double ga = Added(air, carry, most, entry, a);
			double span = entry.EndSeconds - entry.StartSeconds;
			for (int i = 0; i <= EaseSteps; i++)
			{
				double cut = span > 0.0 ? entry.StartSeconds + span * i / EaseSteps : entry.EndSeconds;
				if (cut <= a || cut >= to)
				{
					continue;
				}
				double gc = Added(air, carry, most, entry, cut);
				sum += 0.5 * (ga + gc) * (cut - a);
				a = cut;
				ga = gc;
			}
			sum += 0.5 * (ga + Added(air, carry, most, entry, to)) * (to - a);
			return sum;
		}

		/// <summary>What the runtime offset adds to the carry at a moment: the shape of the wind with it, less without (a calm is a calm).</summary>
		private static double Added(in Air air, Carry carry, float most, in AirOffsetEntry entry, double worldSeconds)
		{
			double wind = GroundBetweenKnots(air, worldSeconds) + air.AuthoredWind;
			return Shape(carry, Math.Max(0.0, wind + entry.AtSeconds(worldSeconds).Wind), most) - Shape(carry, Math.Max(0.0, wind), most);
		}

		/// <summary>Forgets every running sum (tests: the next query walks from the epoch again).</summary>
		public static void ResetCache()
		{
			lock (sumsLock)
			{
				sums.Clear();
				entrySums.Clear();
			}
		}
	}
}
