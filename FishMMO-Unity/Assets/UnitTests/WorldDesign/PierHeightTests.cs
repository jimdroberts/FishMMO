using FishMMO.Shared.WorldDesign;
using FishMMO.Water;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A pier stands over high water, not mean sea level (2026-10-12: "Fix the piers stand height so they don't become
	/// awash, high tide should be the measurement"): PropsFeature.MeasureShore.
	/// </summary>
	public class PierHeightTests
	{
		/// <summary>A beach falling 0.2 m a metre from 4 m ahead of the site, to a sea at 0 with the default tide.</summary>
		private static float Measure(out float deck, out float from, out float to)
			=> PropsFeature.MeasureShore(t => 4f - 0.2f * t, null, 0f, WaterEnvironment.DefaultMaximumTideMetres, 20f, out deck, out from, out to);

		[Test]
		public void ASeaPiersDeckStandsOverTheHighestTide()
		{
			float shore = Measure(out float deck, out float from, out float to);
			float high = WaterEnvironment.DefaultMaximumTideMetres;
			Assert.That(shore, Is.EqualTo(22f).Within(0.01f), "the water begins where the sea is 0.3 m over the beach");
			Assert.That(deck, Is.EqualTo(high + PropsFeature.PierFreeboard).Within(1e-4f), "over high water, not mean sea level");
			Assert.Greater(deck, 0f + 1f + 1f, "well over the old metre above mean sea level");
		}

		[Test]
		public void APierStartsWhereTheBankMeetsItsDeck()
		{
			Measure(out float deck, out float from, out float to);
			float bank = 4f - 0.2f * from;
			Assert.That(bank, Is.GreaterThanOrEqualTo(deck - 0.5f - 1e-4f), "the first module is at most a step over the bank");
			Assert.Less(from, 22f, "it starts up the beach, behind the waterline");
		}

		[Test]
		public void APierStopsWhereItsPilesNoLongerReachTheBed()
		{
			Measure(out float deck, out float from, out float to);
			float bed = 4f - 0.2f * to;
			Assert.That(bed, Is.GreaterThanOrEqualTo(deck - PropsFeature.PierPileReach - 1e-4f), "its last pile meets the bed");
			Assert.Greater(to, 22f + 6f, "it still runs out past the waterline");
		}
	}
}
