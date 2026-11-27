using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using FishMMO.Water;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A pier stands over high water, not mean sea level ("Fix the piers stand height so they don't become
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
		public void APierStopsWhereABoatCanLieAtItsHead()
		{
			Measure(out float deck, out float from, out float to);
			float bed = 4f - 0.2f * to;
			Assert.That(0f - bed, Is.GreaterThanOrEqualTo(PropsFeature.PierBerthDepth - 1e-4f), "deep enough at its head for a boat");
			Assert.That(0f - bed, Is.LessThan(PropsFeature.PierBerthDepth + 0.2f), "and no further out than that");
			Assert.That(bed, Is.GreaterThanOrEqualTo(deck - PropsFeature.PierPileReach - 1e-4f), "its last pile meets the bed");
			Assert.Greater(to, 22f + 3f, "it still runs out past the waterline");
		}

		[Test]
		public void OnAFlatBedAPierStopsAtItsReach()
		{
			// A bed that shelves only 2 cm a metre past the water: it never reaches a berth's depth inside the reach.
			PropsFeature.MeasureShore(t => t < 20f ? 4f - 0.2f * t : -0.3f - 0.02f * (t - 20f), null, 0f, WaterEnvironment.DefaultMaximumTideMetres, 40f,
				out _, out _, out float to);
			float shore = 21f;
			Assert.LessOrEqual(to, shore + PropsFeature.PierMaxReach + 0.5f, "no further than its reach past the waterline");
		}

		[Test]
		public void APierRunsSquareToAShoreItsSiteFacesAtASlant()
		{
			// A straight shore 10 m from the origin whose water lies along (0.6, 0.8): 37° off the site's heading.
			var into = new Vector2(0.6f, 0.8f);
			Vector2 normal = PropsFeature.ShoreNormal(p => Vector2.Dot(p, into) > 10f, into * 10f, Vector2.up);
			Assert.That(Vector2.Angle(normal, into), Is.LessThan(3f), "square to the shore, not down the heading");
		}

		[Test]
		public void AShoreNormalFallsBackWhereThereIsNoShore()
		{
			Assert.That(PropsFeature.ShoreNormal(_ => true, Vector2.zero, Vector2.up), Is.EqualTo(Vector2.up));
			Assert.That(PropsFeature.ShoreNormal(_ => false, Vector2.zero, Vector2.right), Is.EqualTo(Vector2.right));
		}

		[Test]
		public void LaidPiersFollowTheirMeasuredRuns()
		{
			var request = new PointOfInterestLayoutRequest { Layout = PointOfInterestLayout.Scattered, Radius = 30f, Gap = 1f, ShoreAhead = 18f };
			request.Slots.Add(new PointOfInterestLayoutSlot { Role = PointOfInterestSlotRole.Pier, Footprint = new Vector2(3f, 6f), Module = 6f, Count = 2 });
			var dir = new Vector2(0.5f, 0.866f);
			request.PierRuns.Add(new PointOfInterestPierRun { Origin = new Vector2(-6f, 8f), Direction = dir, From = 10f, To = 22f });
			request.PierRuns.Add(new PointOfInterestPierRun { Origin = new Vector2(6f, 4f), Direction = dir, From = 10f, To = 22f });
			List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(request, new DeterministicRNG(5));
			int piers = 0;
			foreach (PointOfInterestLayoutPiece piece in pieces)
			{
				if (piece.Role != PointOfInterestSlotRole.Pier)
				{
					continue;
				}
				piers++;
				Assert.That(piece.Yaw, Is.EqualTo(30f).Within(0.1f), "laid along its run's heading");
			}
			Assert.AreEqual(4, piers, "two modules for each of the two 12 m runs");
		}
	}
}
