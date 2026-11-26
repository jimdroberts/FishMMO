using NUnit.Framework;
using UnityEngine;
using FishMMO.Database.Data;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>The duel ground's geometry, the wording and the rating maths (issue #250), without a scene.</summary>
	[TestFixture]
	public class DuelRulesTests
	{
		private static void Near(float expected, float actual, string message)
		{
			LogAssert.IsTrue(Mathf.Abs(expected - actual) <= 1e-3f, $"{message} (expected {expected}, was {actual})");
		}

		[Test]
		public void TheDefaultGround_IsFifteenMetresEachWay_AndEverySideIsKeptSane()
		{
			LogAssert.AreEqual(new Vector3(15f, 15f, 15f), DuelRules.DefaultArenaSize, "issue #250 asks for 15x15x15");
			Vector3 clamped = DuelRules.ClampSize(new Vector3(1f, 500f, float.NaN));
			LogAssert.AreEqual(DuelRules.MinimumArenaSide, clamped.x, "too small is raised");
			LogAssert.AreEqual(DuelRules.MaximumArenaSide, clamped.y, "too large is lowered");
			LogAssert.AreEqual(DuelRules.DefaultArenaSize.z, clamped.z, "nonsense falls back to the default");
		}

		[Test]
		public void TheStarts_AreEquallyFarFromTheCentre_AlongTheGround_FacingEachOther()
		{
			Vector3 centre = new Vector3(10f, 2f, -4f);
			DuelRules.Starts(centre, DuelRules.DefaultArenaSize, 90f, out Vector3 first, out Vector3 second);
			Near(5f, Vector3.Distance(centre, first), "15 m ground: 7.5 less the 2.5 m margin");
			Near(5f, Vector3.Distance(centre, second), "the same for the other");
			Near(centre.x + 5f, second.x, "yaw 90 runs the ground along +x");
			Vector3 firstLooks = DuelRules.FirstFacing(90f) * Vector3.forward;
			Vector3 secondLooks = DuelRules.SecondFacing(90f) * Vector3.forward;
			LogAssert.IsTrue(Vector3.Dot(firstLooks, (second - first).normalized) > 0.999f, "the first faces the second");
			LogAssert.IsTrue(Vector3.Dot(secondLooks, (first - second).normalized) > 0.999f, "and the second the first");
		}

		[Test]
		public void ASmallGround_StillSetsTheTwoApart()
		{
			Near(DuelRules.MinimumStartOffset, DuelRules.StartOffset(new Vector3(6f, 6f, 6f)), "never closer than the minimum");
		}

		[Test]
		public void TheGround_HoldsBothStarts_WithItsFloorBelowTheLowerOne()
		{
			Vector3 low = new Vector3(0f, 10f, -5f);
			Vector3 high = new Vector3(0f, 13f, 5f);
			LogAssert.IsTrue(DuelRules.TryBuildArena(low, high, DuelRules.DefaultArenaSize, 0f, 3f, out DuelArena arena), "3 m of rise fits a 15 m ground");
			Near(10f - DuelRules.FloorMargin, arena.Center.y - arena.Size.y * 0.5f, "the floor is a metre under the lower start");
			LogAssert.IsTrue(DuelRules.Contains(arena, low), "the lower start is inside");
			LogAssert.IsTrue(DuelRules.Contains(arena, high + Vector3.up * 2f), "and a player standing at the higher one");

			LogAssert.IsFalse(DuelRules.TryBuildArena(low, low + new Vector3(0f, 13f, 10f), DuelRules.DefaultArenaSize, 0f, 3f, out _),
				"a cliff with no headroom at the top is refused");
		}

		[Test]
		public void Inside_IsJudgedInTheGroundsOwnFrame()
		{
			DuelArena arena = new DuelArena { Center = Vector3.zero, Size = DuelRules.DefaultArenaSize, Yaw = 90f };
			LogAssert.IsTrue(DuelRules.Contains(arena, new Vector3(7f, 0f, 0f)), "7 m along its length is inside");
			LogAssert.IsFalse(DuelRules.Contains(arena, new Vector3(8f, 0f, 0f)), "8 m is out");
			LogAssert.IsTrue(DuelRules.Contains(arena, new Vector3(7.6f, 0f, 0f), 0.25f), "a step onto the line is within the tolerance");
			LogAssert.IsFalse(DuelRules.Contains(arena, new Vector3(0f, 8f, 0f)), "above the roof is out");

			DuelArena turned = new DuelArena { Center = Vector3.zero, Size = new Vector3(6f, 15f, 30f), Yaw = 45f };
			Vector3 alongLength = Quaternion.Euler(0f, 45f, 0f) * Vector3.forward * 14f;
			LogAssert.IsTrue(DuelRules.Contains(turned, alongLength), "14 m along a turned 30 m ground is inside");
			LogAssert.IsFalse(DuelRules.Contains(turned, new Vector3(0f, 0f, 14f)), "but not 14 m along the world's z");
		}

		[Test]
		public void DistanceToEdge_IsPositiveInside_AndNegativeOut()
		{
			DuelArena arena = new DuelArena { Center = Vector3.zero, Size = DuelRules.DefaultArenaSize, Yaw = 0f };
			Near(7.5f, DuelRules.DistanceToEdge(arena, Vector3.zero), "the centre is half a side from every wall");
			Near(1f, DuelRules.DistanceToEdge(arena, new Vector3(0f, 0f, 6.5f)), "a metre from the end wall");
			LogAssert.IsTrue(DuelRules.DistanceToEdge(arena, new Vector3(9f, 0f, 0f)) < 0f, "outside is negative");
		}

		[Test]
		public void Bearing_PointsFromOneToTheOther()
		{
			Near(90f, DuelRules.Bearing(Vector3.zero, new Vector3(3f, 5f, 0f)), "+x is 90 degrees, whatever the height");
			Near(0f, DuelRules.Bearing(Vector3.zero, new Vector3(0f, 0f, 2f)), "+z is 0");
		}

		[Test]
		public void EveryRefusalAndEnding_HasWords()
		{
			foreach (DuelRequestFailure failure in System.Enum.GetValues(typeof(DuelRequestFailure)))
			{
				LogAssert.IsFalse(string.IsNullOrEmpty(DuelRules.DescribeRequest(failure, "Ana")), $"{failure} is worded");
			}
			foreach (DuelOutcome outcome in System.Enum.GetValues(typeof(DuelOutcome)))
			{
				foreach (DuelEndReason reason in System.Enum.GetValues(typeof(DuelEndReason)))
				{
					LogAssert.IsFalse(string.IsNullOrEmpty(DuelRules.DescribeEnd(outcome, reason, "Ana")), $"{outcome}/{reason} is worded");
				}
			}
		}

		// ── Rating ────────────────────────────────────────────────────────────

		[Test]
		public void EvenPlayers_TradeHalfTheKFactor()
		{
			(int winner, int loser) = DuelRating.Changes(1500, 1500, false, 32);
			LogAssert.AreEqual(16, winner, "the winner gains half of K");
			LogAssert.AreEqual(-16, loser, "the loser loses it");
		}

		[Test]
		public void AnUpset_MovesMoreThanAnExpectedWin()
		{
			(int upset, _) = DuelRating.Changes(1300, 1700, false, 32);
			(int expected, _) = DuelRating.Changes(1700, 1300, false, 32);
			LogAssert.IsTrue(upset > 16 && expected < 16, $"upset +{upset}, expected win +{expected}");
			LogAssert.IsTrue(expected >= 0, "a win never costs rating");
		}

		[Test]
		public void ADraw_MovesTheRatingsTowardsEachOther_AndNothingBetweenEquals()
		{
			LogAssert.AreEqual((0, 0), DuelRating.Changes(1500, 1500, true, 32), "equals draw: nothing moves");
			(int lowSide, int highSide) = DuelRating.Changes(1300, 1700, true, 32);
			LogAssert.IsTrue(lowSide > 0 && highSide < 0, $"the lower gains ({lowSide}), the higher loses ({highSide})");
		}

		[Test]
		public void ALoss_NeverTakesARatingUnderTheFloor()
		{
			(_, int loser) = DuelRating.Changes(DuelRating.Floor + 5, DuelRating.Floor + 5, false, 32);
			LogAssert.AreEqual(-5, loser, "stops at the floor");
		}
	}
}
