using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The ground (<see cref="GroundCover"/>) and the scene's own storms (<see cref="StormSchedule"/>) are pure functions
	/// of the world time: the same whichever way a moment is reached — worked out at once, stepped to a minute at a
	/// time, or set back to — and the same for two machines that work them out apart. And a set of the clock is told
	/// from a hold, a pace or a correction (<see cref="WorldClock.Jumps"/>).
	/// </summary>
	public class CoverWorldClockTests
	{
		/// <summary>A moment half an hour past a generation's start, so the windows below cross a start.</summary>
		private const double Moment = GroundCover.GenerationSeconds * 120.0 + 1800.0;

		private static readonly Vector3[] Points =
		{
			new Vector3(0f, 0f, 0f),
			new Vector3(600f, 0f, -300f),
			new Vector3(30000f, 0f, 30000f),
		};

		/// <summary>A wet, unsettled scene with one storm someone started sitting over the first points for hours.</summary>
		private static WeatherTimeline Wet(bool director = false)
		{
			var wet = new AirOffsets { Humidity = 0.6f, Instability = 0.5f, Temperature = -2f };
			var timeline = new WeatherTimeline
			{
				SceneName = "Ground",
				Seed = 1234,
				SceneMode = WeatherSceneMode.Own,
				Air = new AirOffsetEntry { From = wet, To = wet, StartSeconds = 0, EndSeconds = 0 },
				Area = new Rect(-5000f, -5000f, 10000f, 10000f),
				Director = director,
				LatitudeDegrees = 40f,
			};
			timeline.Cells.Add(StormSchedule.NewCell(1, 99u, StormKind.Thunderstorm, Vector2.zero, 2500f, 0f, new Vector2(0.2f, 0.1f),
				4f * 3600f, Moment - 3.0 * 3600.0));
			return timeline;
		}

		private static CoverTrack[] Ground(CoverPoints points, double seconds)
		{
			var tracks = new CoverTrack[Points.Length];
			for (int i = 0; i < tracks.Length; i++)
			{
				tracks[i] = points.At(i, seconds);
			}
			return tracks;
		}

		private static CoverPoints Fresh(WeatherTimeline timeline, double seconds)
		{
			var points = new CoverPoints();
			points.SetPoints(Points);
			points.Update(timeline, null, default(Scene), seconds);
			return points;
		}

		private static void Same(CoverTrack[] a, CoverTrack[] b, string what)
		{
			for (int i = 0; i < a.Length; i++)
			{
				LogAssert.IsTrue(a[i].SameAs(b[i]), $"{what}: point {i} {Describe(a[i])} vs {Describe(b[i])}");
			}
		}

		private static string Describe(in CoverTrack t) => $"(snow {t.Cover.Snow:0.#####}, wet {t.Cover.Wet:0.#####}, depth {t.Depth:0.###})";

		[Test]
		public void TheGround_IsTheSame_WorkedOutAtOnceOrStepped()
		{
			WeatherTimeline timeline = Wet();
			CoverTrack[] atOnce = Ground(Fresh(timeline, Moment), Moment);

			// Stepped a minute at a time from two hours before, across a generation's start.
			var stepped = new CoverPoints();
			stepped.SetPoints(Points);
			for (double t = Moment - 7200.0; t <= Moment; t += 60.0)
			{
				stepped.Update(timeline, null, default(Scene), t);
			}
			Same(atOnce, Ground(stepped, Moment), "stepped vs at once");
		}

		[Test]
		public void ASetBack_LandsOnTheGroundOfThatMoment()
		{
			WeatherTimeline timeline = Wet();
			var points = new CoverPoints();
			points.SetPoints(Points);
			points.Update(timeline, null, default(Scene), Moment);
			double earlier = Moment - 5000.0;
			points.Update(timeline, null, default(Scene), earlier);
			Same(Ground(Fresh(timeline, earlier), earlier), Ground(points, earlier), "set back vs worked out there");
		}

		[Test]
		public void TwoMachines_WorkingItOutApart_Agree()
		{
			CoverTrack[] one = Ground(Fresh(Wet(), Moment), Moment);
			CoverTrack[] two = Ground(Fresh(Wet(), Moment), Moment);
			Same(one, two, "two timelines");
		}

		[Test]
		public void TheStorm_WetsTheGroundUnderItAndNotFarAway()
		{
			CoverTrack[] ground = Ground(Fresh(Wet(), Moment), Moment);
			LogAssert.IsTrue(ground[0].Cover.Wet > ground[2].Cover.Wet || ground[0].Cover.Snow > ground[2].Cover.Snow,
				$"under the storm {Describe(ground[0])}, far from it {Describe(ground[2])}");
		}

		[Test]
		public void AGenerationsStart_IsNoSeam()
		{
			WeatherTimeline timeline = Wet();
			double start = GroundCover.GenerationSeconds * 121.0;
			CoverTrack before = Fresh(timeline, start - 0.001).At(0, start - 0.001);
			CoverTrack after = Fresh(timeline, start).At(0, start);
			LogAssert.IsTrue(Mathf.Abs(before.Cover.Wet - after.Cover.Wet) < 1e-3f && Mathf.Abs(before.Cover.Snow - after.Cover.Snow) < 1e-3f,
				$"{Describe(before)} then {Describe(after)}");
			LogAssert.IsTrue(GroundCover.YoungWeight(start - 1e-6) > 0.9999f, "all the young generation as the next starts");
			LogAssert.AreEqual(0f, GroundCover.YoungWeight(start), "none of the new one as it starts");
		}

		[Test]
		public void TheScenesOwnStorms_AreTheSameWorkedOutApart_AndNeverMoreThanTheCap()
		{
			var one = new List<StormCell>();
			var two = new List<StormCell>();
			StormSchedule.CellsBetween(Wet(director: true), null, default(Scene), Moment - 6.0 * 3600.0, Moment, one);
			// The other asks the near end first, so its slots are worked out in another order.
			WeatherTimeline other = Wet(director: true);
			StormSchedule.CellsBetween(other, null, default(Scene), Moment - 600.0, Moment, new List<StormCell>());
			StormSchedule.CellsBetween(other, null, default(Scene), Moment - 6.0 * 3600.0, Moment, two);
			LogAssert.AreEqual(one.Count, two.Count, "as many storms");
			for (int i = 0; i < one.Count; i++)
			{
				LogAssert.AreEqual(one[i].ID, two[i].ID);
				LogAssert.AreEqual(one[i].BirthSeconds, two[i].BirthSeconds);
				LogAssert.AreEqual(one[i].DeathSeconds, two[i].DeathSeconds);
				LogAssert.AreEqual(one[i].OriginX, two[i].OriginX);
				LogAssert.IsTrue(StormSchedule.IsScheduled(one[i].ID));
			}
			for (double t = Moment - 6.0 * 3600.0; t <= Moment; t += 300.0)
			{
				int alive = 0;
				foreach (StormCell cell in one)
				{
					if (cell.BirthSeconds <= t && t < cell.DeathSeconds)
					{
						alive++;
					}
				}
				LogAssert.IsTrue(alive <= StormSchedule.MaxCells, $"{alive} alive at {t}");
			}
		}

		[Test]
		public void NoDirector_NoStormsOfItsOwn()
		{
			var cells = new List<StormCell>();
			StormSchedule.CellsBetween(Wet(director: false), null, default(Scene), Moment - 3600.0, Moment, cells);
			LogAssert.AreEqual(0, cells.Count);
		}

		[Test]
		public void ASet_IsAJump_AndAHoldAPaceOrACorrectionIsNot()
		{
			var clock = new WorldClock();
			clock.Override(100, 5000.0, 1.0, true);
			uint first = clock.Jumps;
			LogAssert.AreEqual(1u, first, "the first anchor lands the world somewhere");
			clock.Override(200, clock.WorldSecondsAt(200), 0.0, true);
			LogAssert.AreEqual(first, clock.Jumps, "a hold is not a jump");
			clock.Override(300, clock.WorldSecondsAt(300), 100.0, true);
			LogAssert.AreEqual(first, clock.Jumps, "a new pace is not a jump");
			clock.Propose(400, clock.WorldSecondsAt(400) + 3.0, true);
			LogAssert.AreEqual(first, clock.Jumps, "a correction is eased, not a jump");
			clock.Override(500, clock.WorldSecondsAt(500) - 3600.0, 1.0, true);
			LogAssert.AreEqual(first + 1u, clock.Jumps, "a set is");
		}
	}
}
