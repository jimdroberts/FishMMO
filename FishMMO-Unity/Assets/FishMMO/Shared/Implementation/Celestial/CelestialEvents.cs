using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>Sky events that can drive gameplay through triggers.</summary>
	public enum CelestialEventKind : byte
	{
		FullMoon = 0,
		NewMoon = 1,
		SolarEclipseStart = 2,
		SolarEclipseEnd = 3,
		LunarEclipseStart = 4,
		LunarEclipseEnd = 5,
		Conjunction = 6,
		MeteorShowerPeak = 7,
		CometPerihelion = 8,
	}

	/// <summary>One event as it happened.</summary>
	public struct CelestialEvent
	{
		public CelestialEventKind Kind;
		public CelestialBody Primary;
		public CelestialBody Secondary;
		public string ShowerName;
		/// <summary>How strong: eclipse coverage, conjunction closeness, shower rate.</summary>
		public float Strength;

		public override string ToString() => $"{Kind} {Primary?.ResolvedName}{(Secondary != null ? " & " + Secondary.ResolvedName : string.Empty)}{(ShowerName != null ? " " + ShowerName : string.Empty)} ({Strength:0.##})";
	}

	/// <summary>
	/// Watches successive <see cref="CelestialState"/>s and reports each event once, when it
	/// begins. Feed it the same state the day/night cycle uses, about once a second.
	/// </summary>
	public sealed class CelestialEventTracker
	{
		public const float FullMoonIllumination = 0.985f;
		public const float NewMoonIllumination = 0.015f;
		public const float EclipseThreshold = 0.02f;
		public const float ConjunctionDegrees = 2f;

		private readonly Dictionary<CelestialBody, bool> full = new Dictionary<CelestialBody, bool>();
		private readonly Dictionary<CelestialBody, bool> newMoon = new Dictionary<CelestialBody, bool>();
		private readonly Dictionary<CelestialBody, bool> lunar = new Dictionary<CelestialBody, bool>();
		private readonly HashSet<long> conjunctions = new HashSet<long>();
		private readonly Dictionary<CelestialBody, (double distance, double trend)> comets = new Dictionary<CelestialBody, (double, double)>();
		private readonly Dictionary<MeteorShower, float> showers = new Dictionary<MeteorShower, float>();
		private bool solar;
		private bool primed;

		/// <summary>Forgets everything: the next update primes without reporting.</summary>
		public void Reset()
		{
			full.Clear();
			newMoon.Clear();
			lunar.Clear();
			conjunctions.Clear();
			comets.Clear();
			showers.Clear();
			solar = false;
			primed = false;
		}

		/// <summary>Adds the events that began since the last update. The first update only primes.</summary>
		public void Update(CelestialState state, List<CelestialEvent> into)
		{
			bool report = primed;
			primed = true;
			if (state.System == null)
			{
				return;
			}

			for (int i = 0; i < state.Bodies.Count; i++)
			{
				SkyBodyState body = state.Bodies[i];
				if (body.Kind == SkyBodyKind.Moon)
				{
					Edge(full, body.Body, body.Illumination >= FullMoonIllumination, report, into, CelestialEventKind.FullMoon, null, body.Illumination);
					Edge(newMoon, body.Body, body.Illumination <= NewMoonIllumination, report, into, CelestialEventKind.NewMoon, null, 1f - body.Illumination);
					bool was = lunar.TryGetValue(body.Body, out bool w) && w;
					bool now = body.Shadowed >= EclipseThreshold;
					if (report && now != was)
					{
						into.Add(new CelestialEvent { Kind = now ? CelestialEventKind.LunarEclipseStart : CelestialEventKind.LunarEclipseEnd, Primary = body.Body, Strength = body.Shadowed });
					}
					lunar[body.Body] = now;
				}
				if (body.Kind == SkyBodyKind.Comet)
				{
					double distance = body.Body != null ? DistanceFromStar(state, body.Body) : 0.0;
					if (comets.TryGetValue(body.Body, out var previous))
					{
						double trend = distance - previous.distance;
						if (report && previous.trend < 0.0 && trend >= 0.0)
						{
							into.Add(new CelestialEvent { Kind = CelestialEventKind.CometPerihelion, Primary = body.Body, Strength = body.Brightness });
						}
						comets[body.Body] = (distance, trend);
					}
					else
					{
						comets[body.Body] = (distance, 0.0);
					}
				}
			}

			bool eclipse = state.SolarEclipse >= EclipseThreshold;
			if (report && eclipse != solar)
			{
				SkyBodyState sun = state.Sun >= 0 ? state.Bodies[state.Sun] : default;
				into.Add(new CelestialEvent { Kind = eclipse ? CelestialEventKind.SolarEclipseStart : CelestialEventKind.SolarEclipseEnd, Primary = sun.Body, Secondary = state.EclipsingBody, Strength = state.SolarEclipse });
			}
			solar = eclipse;

			// Conjunctions: two planets, moons or comets within 2° of each other.
			var current = new HashSet<long>();
			for (int a = 0; a < state.Bodies.Count; a++)
			{
				SkyBodyState first = state.Bodies[a];
				if (first.Kind == SkyBodyKind.Star)
				{
					continue;
				}
				for (int b = a + 1; b < state.Bodies.Count; b++)
				{
					SkyBodyState second = state.Bodies[b];
					if (second.Kind == SkyBodyKind.Star)
					{
						continue;
					}
					float angle = Vector3.Angle(first.Direction, second.Direction);
					if (angle > ConjunctionDegrees)
					{
						continue;
					}
					long key = PairKey(first.Body, second.Body);
					current.Add(key);
					if (report && !conjunctions.Contains(key))
					{
						into.Add(new CelestialEvent { Kind = CelestialEventKind.Conjunction, Primary = first.Body, Secondary = second.Body, Strength = 1f - angle / ConjunctionDegrees });
					}
				}
			}
			conjunctions.Clear();
			conjunctions.UnionWith(current);

			// Shower peaks: the rate stops rising.
			double day = state.Hours / CelestialMath.HomeSolarDayHours(state.System);
			double dayOfYear = day - Math.Floor(day / state.System.DaysPerYear) * state.System.DaysPerYear;
			foreach (MeteorShower shower in state.System.MeteorShowers)
			{
				if (shower == null)
				{
					continue;
				}
				float rate = shower.RateOn(dayOfYear, state.System.DaysPerYear);
				bool atPeak = Math.Abs(dayOfYear - (shower.PeakDayOfYear - 1)) < 0.5 || Math.Abs(dayOfYear - (shower.PeakDayOfYear - 1) - state.System.DaysPerYear) < 0.5;
				bool wasAtPeak = showers.TryGetValue(shower, out float previousPeak) && previousPeak > 0f;
				if (report && atPeak && !wasAtPeak && rate > 0f)
				{
					into.Add(new CelestialEvent { Kind = CelestialEventKind.MeteorShowerPeak, ShowerName = shower.Name, Strength = rate });
				}
				showers[shower] = atPeak ? 1f : 0f;
			}
		}

		private static double DistanceFromStar(CelestialState state, CelestialBody body)
		{
			Vector3d star = CelestialMath.Position(state.System, state.System.PrimaryStar, state.Hours);
			return (CelestialMath.Position(state.System, body, state.Hours) - star).Magnitude;
		}

		private static long PairKey(CelestialBody a, CelestialBody b)
		{
			long x = a != null ? a.GetHashCode() : 0, y = b != null ? b.GetHashCode() : 0;
			return x < y ? (x << 32) ^ (uint)y : (y << 32) ^ (uint)x;
		}

		private static void Edge(Dictionary<CelestialBody, bool> states, CelestialBody body, bool now, bool report, List<CelestialEvent> into, CelestialEventKind kind, CelestialBody secondary, float strength)
		{
			bool was = states.TryGetValue(body, out bool value) && value;
			if (report && now && !was)
			{
				into.Add(new CelestialEvent { Kind = kind, Primary = body, Secondary = secondary, Strength = strength });
			}
			states[body] = now;
		}
	}
}
