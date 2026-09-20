using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>A lightning strike: when, where it hits, how bright.</summary>
	public struct LightningStrike
	{
		public double Time;
		public Vector3 Ground;
		public float CloudHeight;
		public uint Seed;
		public float Intensity;
	}

	/// <summary>A meteor: when, where it starts, where it heads, how long it lasts.</summary>
	public struct Meteor
	{
		public double Time;
		public Vector3 Direction;
		public Vector3 Heading;
		public float Length;
		public float Duration;
		public float Brightness;
	}

	/// <summary>
	/// When lightning strikes and meteors fall. Both are pure functions of the world time and the
	/// weather or sky, so every client sees the same strike at the same moment without messages.
	/// Lightning is visual only.
	/// </summary>
	public static class SkySchedule
	{
		[System.ThreadStatic] private static List<StormCollision> collisionScratch;

		public const double LightningSlotSeconds = 0.2;
		public const double MeteorSlotSeconds = 0.25;

		public static uint Hash(uint a, uint b)
		{
			unchecked
			{
				uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u + (a << 6) + (a >> 2));
				h ^= h >> 15;
				h *= 0x2C1B3C6Du;
				h ^= h >> 12;
				h *= 0x297A2D39u;
				h ^= h >> 15;
				return h;
			}
		}

		public static float Unit(uint h) => (h & 0xFFFFFF) / (float)0x1000000;

		/// <summary>
		/// Strikes in [from, to) world seconds: from storm cells (at the cell) and from scene-wide
		/// lightning (around the viewer). A rate of 1 is a strike about every two seconds.
		/// </summary>
		public static void Lightning(WeatherTimeline timeline, uint tick, double from, double to, Vector3 viewer, List<LightningStrike> into)
		{
			if (timeline == null || to <= from)
			{
				return;
			}
			long first = (long)Math.Floor(from / LightningSlotSeconds);
			long last = (long)Math.Floor(to / LightningSlotSeconds);

			var accumulator = new WeatherAccumulator();
			timeline.AccumulateSceneLayers(tick, ref accumulator);
			float sceneRate = accumulator.HasAny ? accumulator.Resolve()[WeatherChannel.LightningRate] : 0f;
			if (sceneRate > 0.001f)
			{
				for (long slot = first; slot <= last; slot++)
				{
					uint h = Hash(timeline.Seed, (uint)slot);
					if (Unit(h) >= sceneRate * 0.5f * (float)LightningSlotSeconds)
					{
						continue;
					}
					double time = (slot + Unit(Hash(h, 1))) * LightningSlotSeconds;
					if (time < from || time >= to)
					{
						continue;
					}
					float angle = Unit(Hash(h, 2)) * Mathf.PI * 2f;
					float distance = Mathf.Lerp(250f, 3000f, Unit(Hash(h, 3)));
					into.Add(new LightningStrike
					{
						Time = time,
						Ground = new Vector3(viewer.x + Mathf.Sin(angle) * distance, viewer.y - 2f, viewer.z + Mathf.Cos(angle) * distance),
						CloudHeight = Mathf.Lerp(900f, 1500f, Unit(Hash(h, 4))),
						Seed = h,
						Intensity = Mathf.Lerp(0.6f, 1f, Unit(Hash(h, 5))),
					});
				}
			}

			if (timeline.SceneMode != WeatherSceneMode.Own)
			{
				return;
			}
			// Where cells collide, whatever they were carrying: the strikes land in the zone the two
			// share, and hard — a violent meeting is a strike every couple of seconds.
			collisionScratch ??= new List<StormCollision>();
			timeline.CollisionsAt(tick, collisionScratch);
			for (int c = 0; c < collisionScratch.Count; c++)
			{
				StormCollision clash = collisionScratch[c];
				for (long slot = first; slot <= last; slot++)
				{
					uint h = Hash(clash.Seed, (uint)slot);
					if (Unit(h) >= clash.Intensity * 0.5f * (float)LightningSlotSeconds)
					{
						continue;
					}
					double time = (slot + Unit(Hash(h, 1))) * LightningSlotSeconds;
					if (time < from || time >= to)
					{
						continue;
					}
					float angle = Unit(Hash(h, 2)) * Mathf.PI * 2f;
					float distance = Mathf.Sqrt(Unit(Hash(h, 3))) * clash.Radius * 0.8f;
					into.Add(new LightningStrike
					{
						Time = time,
						Ground = new Vector3(clash.Centre.x + Mathf.Sin(angle) * distance, viewer.y - 2f, clash.Centre.y + Mathf.Cos(angle) * distance),
						CloudHeight = Mathf.Lerp(1200f, 2400f, Unit(Hash(h, 4))),
						Seed = h,
						Intensity = Mathf.Lerp(0.8f, 1f, Unit(Hash(h, 5))),
					});
				}
			}

			for (int c = 0; c < timeline.Cells.Count; c++)
			{
				StormCell cell = timeline.Cells[c];
				WeatherPreset preset = WeatherPreset.Get<WeatherPreset>(cell.PresetID);
				if (preset == null)
				{
					continue;
				}
				float rate = preset.Evaluate()[WeatherChannel.LightningRate] * cell.EnvelopeAt(tick) * cell.PeakIntensity;
				if (rate <= 0.001f)
				{
					continue;
				}
				Vector2 centre = cell.CentreAt(tick, timeline.TickDelta);
				for (long slot = first; slot <= last; slot++)
				{
					uint h = Hash(cell.Seed ^ ((uint)cell.ID << 16), (uint)slot);
					if (Unit(h) >= rate * 0.5f * (float)LightningSlotSeconds)
					{
						continue;
					}
					double time = (slot + Unit(Hash(h, 1))) * LightningSlotSeconds;
					if (time < from || time >= to)
					{
						continue;
					}
					float angle = Unit(Hash(h, 2)) * Mathf.PI * 2f;
					float distance = Mathf.Sqrt(Unit(Hash(h, 3))) * cell.RadiusMeters * 0.6f;
					into.Add(new LightningStrike
					{
						Time = time,
						Ground = new Vector3(centre.x + Mathf.Sin(angle) * distance, viewer.y - 2f, centre.y + Mathf.Cos(angle) * distance),
						CloudHeight = Mathf.Lerp(900f, 1500f, Unit(Hash(h, 4))),
						Seed = h,
						Intensity = Mathf.Lerp(0.7f, 1f, Unit(Hash(h, 5))),
					});
				}
			}
		}

		/// <summary>The jagged path of a bolt, from the cloud down to the ground, with a few branches.</summary>
		public static void BoltPath(in LightningStrike strike, List<Vector3> trunk, List<List<Vector3>> branches)
		{
			trunk.Clear();
			branches.Clear();
			const int segments = 18;
			Vector3 top = strike.Ground + Vector3.up * strike.CloudHeight;
			Vector3 point = top;
			for (int i = 0; i <= segments; i++)
			{
				float t = i / (float)segments;
				Vector3 straight = Vector3.Lerp(top, strike.Ground, t);
				uint h = Hash(strike.Seed, (uint)(i + 100));
				float wobble = strike.CloudHeight * 0.05f * (1f - t * 0.6f);
				point = i == 0 || i == segments ? straight : straight + new Vector3((Unit(h) - 0.5f) * wobble, 0f, (Unit(Hash(h, 1)) - 0.5f) * wobble);
				trunk.Add(point);
				if (i > 2 && i < segments - 3 && Unit(Hash(h, 2)) < 0.22f)
				{
					var branch = new List<Vector3> { point };
					Vector3 b = point;
					Vector3 drift = new Vector3(Unit(Hash(h, 3)) - 0.5f, -0.6f, Unit(Hash(h, 4)) - 0.5f).normalized * (strike.CloudHeight * 0.06f);
					for (int k = 0; k < 4; k++)
					{
						b += drift + new Vector3((Unit(Hash(h, (uint)(10 + k))) - 0.5f) * wobble * 0.5f, 0f, 0f);
						branch.Add(b);
					}
					branches.Add(branch);
				}
			}
		}

		/// <summary>
		/// Meteors in [from, to) world seconds for a sky: sporadic ones anywhere, shower members
		/// streaking away from their radiant. Only while the sky is dark enough to see them.
		/// </summary>
		public static void Meteors(CelestialState state, double from, double to, int limit, List<Meteor> into)
		{
			if (state == null || state.System == null || state.MeteorRate <= 0f || to <= from)
			{
				return;
			}
			float perSlot = state.MeteorRate / 3600f * (float)MeteorSlotSeconds * 6f; // several per slot on a visible hemisphere
			long first = (long)Math.Floor(from / MeteorSlotSeconds);
			long last = (long)Math.Floor(to / MeteorSlotSeconds);
			var showers = state.System.MeteorShowers;
			double day = state.Hours / CelestialMath.HomeSolarDayHours(state.System);
			double dayOfYear = day - Math.Floor(day / state.System.DaysPerYear) * state.System.DaysPerYear;
			for (long slot = first; slot <= last && into.Count < limit; slot++)
			{
				uint h = Hash(0xA5A5u, (uint)slot);
				if (Unit(h) >= perSlot)
				{
					continue;
				}
				double time = (slot + Unit(Hash(h, 1))) * MeteorSlotSeconds;
				if (time < from || time >= to)
				{
					continue;
				}
				float alt = Mathf.Lerp(15f, 80f, Unit(Hash(h, 2)));
				float az = Unit(Hash(h, 3)) * 360f;
				Vector3 start = CelestialState.SceneDirection(alt, az, 0.0);
				Vector3 heading;
				// Pick a shower by its share of the rate; otherwise sporadic.
				MeteorShower chosen = null;
				float roll = Unit(Hash(h, 4)) * state.MeteorRate;
				foreach (MeteorShower shower in showers)
				{
					if (shower == null) continue;
					roll -= shower.RateOn(dayOfYear, state.System.DaysPerYear);
					if (roll <= 0f)
					{
						chosen = shower;
						break;
					}
				}
				if (chosen != null)
				{
					Vector3 radiant = state.EquatorialDirection(chosen.RadiantRightAscension * Mathf.Deg2Rad, chosen.RadiantDeclination * Mathf.Deg2Rad);
					heading = (start - radiant * Vector3.Dot(start, radiant)).normalized;
					if (heading.sqrMagnitude < 0.5f) heading = Vector3.down;
				}
				else
				{
					heading = new Vector3(Unit(Hash(h, 5)) - 0.5f, -0.6f, Unit(Hash(h, 6)) - 0.5f).normalized;
				}
				into.Add(new Meteor
				{
					Time = time,
					Direction = start,
					Heading = heading,
					Length = Mathf.Lerp(3f, 12f, Unit(Hash(h, 7))) * Mathf.Deg2Rad,
					Duration = Mathf.Lerp(0.3f, 0.9f, Unit(Hash(h, 8))),
					Brightness = Mathf.Lerp(0.4f, 1f, Unit(Hash(h, 9))),
				});
			}
		}
	}
}
