using NUnit.Framework;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// Ground cover keeps the WORLD clock (SceneCoverSampling.Advance): it holds while the world is held, races when it
	/// is raced, comes through the hours a jump forward skipped, and stays as it is when the clock is set back. These
	/// run on a timeline with no weather of its own (WeatherSceneMode.None), so every step's frame is the clear sky
	/// and the only thing moving the ground is the time it is given.
	/// </summary>
	public class CoverWorldClockTests
	{
		private static WeatherTimeline Soaked(double coverSeconds)
		{
			return new WeatherTimeline
			{
				SceneMode = WeatherSceneMode.None,
				Cover = new WeatherCover { Wet = 1f, Ash = 1f },
				CoverSeconds = coverSeconds,
			};
		}

		[Test]
		public void AHeldWorld_HoldsItsGround()
		{
			WeatherTimeline timeline = Soaked(5000.0);
			SceneCoverSampling.Advance(timeline, null, default(Scene), 5000.0);
			LogAssert.AreEqual(1f, timeline.Cover.Wet, "no world time passed, so nothing may dry");
			LogAssert.AreEqual(1f, timeline.Cover.Ash);
		}

		[Test]
		public void ARacedWorld_RacesItsGround()
		{
			WeatherTimeline minute = Soaked(5000.0);
			SceneCoverSampling.Advance(minute, null, default(Scene), 5060.0);
			WeatherTimeline hour = Soaked(5000.0);
			SceneCoverSampling.Advance(hour, null, default(Scene), 8600.0);
			// Even the fastest drying (a hot, clear, breezy noon) takes a couple of minutes from soaked.
			LogAssert.IsTrue(minute.Cover.Wet < 1f && minute.Cover.Wet > 0.5f, $"a world minute only starts to dry a soaked ground (got {minute.Cover.Wet})");
			LogAssert.AreEqual(0f, hour.Cover.Wet, "a world hour of clear sky dries it, however little real time that took");
			LogAssert.AreEqual(8600.0, hour.CoverSeconds);
		}

		[Test]
		public void OneLongAdvance_MatchesManyShortOnes()
		{
			// The ground's state depends on the world time it went through, not on how often anyone looked.
			WeatherTimeline once = Soaked(10000.0);
			SceneCoverSampling.Advance(once, null, default(Scene), 10600.0);
			WeatherTimeline often = Soaked(10000.0);
			for (int i = 1; i <= 10; i++)
			{
				SceneCoverSampling.Advance(often, null, default(Scene), 10000.0 + 60.0 * i);
			}
			LogAssert.IsTrue(System.Math.Abs(once.Cover.Wet - often.Cover.Wet) < 1e-4f, $"{once.Cover.Wet} vs {often.Cover.Wet}");
			LogAssert.IsTrue(System.Math.Abs(once.Cover.Ash - often.Cover.Ash) < 1e-4f, $"{once.Cover.Ash} vs {often.Cover.Ash}");
		}

		[Test]
		public void AClockSetBack_LeavesTheGroundAndCountsOnFromThere()
		{
			WeatherTimeline timeline = Soaked(5000.0);
			SceneCoverSampling.Advance(timeline, null, default(Scene), 1000.0);
			LogAssert.AreEqual(1f, timeline.Cover.Wet, "the ground cannot go back to how it was");
			LogAssert.AreEqual(1000.0, timeline.CoverSeconds, "the next advance counts from the new time");
		}

		[Test]
		public void Steps_AreBoundedInLengthAndNumber()
		{
			LogAssert.AreEqual(0, WeatherCover.StepsFor(0.0, 100));
			LogAssert.AreEqual(0, WeatherCover.StepsFor(-30.0, 100));
			LogAssert.AreEqual(0, WeatherCover.StepsFor(double.NaN, 100));
			LogAssert.AreEqual(1, WeatherCover.StepsFor(30.0, 100));
			LogAssert.AreEqual(10, WeatherCover.StepsFor(600.0, 100));
			LogAssert.AreEqual(4, WeatherCover.StepsFor(6000.0, 4), "a raced row takes a few long steps, not a hundred short ones");
			LogAssert.AreEqual(100, WeatherCover.StepsFor(1e9, 100), "a jump of decades still costs a bounded number of samples");
		}
	}
}
