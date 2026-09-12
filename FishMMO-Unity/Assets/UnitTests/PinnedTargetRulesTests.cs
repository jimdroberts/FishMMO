using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the rule that releases a pinned target. The pin is a promise the target frame
	/// makes — "this card stays up until you let go" — so the truth table of what may break that
	/// promise is written down here: destruction, despawn, the holder's death, the target's death
	/// and distance, and nothing else.
	/// </summary>
	/// <remarks>
	/// The holder's death is the case the issue behind this file is about. A pin is only ever
	/// dropped by the player who made it pressing the pin key again; every other release is a
	/// fact about the target. That left "the player died to the character they had pinned" with
	/// nothing to release it, so the card survived a death and a respawn and sat on the frame of
	/// a fight that was over.
	/// </remarks>
	[TestFixture]
	public class PinnedTargetRulesTests
	{
		private const float Release = PinnedTargetRules.RELEASE_DISTANCE;

		/// <summary>
		/// The ordinary case, named once so each proof below states only the fact it is about.
		/// </summary>
		/// <param name="isDestroyed">Target transform gone.</param>
		/// <param name="isSpawned">Target network object still spawned.</param>
		/// <param name="isAlive">Target alive, or with no health to lose.</param>
		/// <param name="sqrDistance">Squared distance to the target.</param>
		/// <param name="isOwnerAlive">Holder alive.</param>
		private static bool ShouldRelease(
			bool isDestroyed = false,
			bool isSpawned = true,
			bool isAlive = true,
			float sqrDistance = 100.0f,
			bool isOwnerAlive = true)
		{
			return PinnedTargetRules.ShouldRelease(
				isDestroyed, isSpawned, isAlive, sqrDistance, Release, isOwnerAlive);
		}

		[Test]
		public void LiveSpawnedTargetInRange_Holds()
		{
			Assert.IsFalse(ShouldRelease());
		}

		[Test]
		public void DestroyedTarget_Releases()
		{
			Assert.IsTrue(ShouldRelease(isDestroyed: true));
		}

		[Test]
		public void DespawnedTarget_Releases()
		{
			Assert.IsTrue(ShouldRelease(isSpawned: false));
		}

		[Test]
		public void DeadTarget_Releases()
		{
			Assert.IsTrue(ShouldRelease(isAlive: false));
		}

		[Test]
		public void DeadHolder_Releases()
		{
			Assert.IsTrue(ShouldRelease(isOwnerAlive: false));
		}

		[Test]
		public void DeadHolder_ReleasesEvenWhileEverythingAboutTheTargetIsFine()
		{
			/* The one release that is about the player rather than the target, stated as its own
			 * case because the parameters above make it easy to write this proof in a way that
			 * passes for the wrong reason. Everything about the target is deliberately ideal:
			 * spawned, alive, standing at arm's length. It still goes. */
			Assert.IsTrue(ShouldRelease(
				isDestroyed: false,
				isSpawned: true,
				isAlive: true,
				sqrDistance: 1.0f,
				isOwnerAlive: false));
		}

		[Test]
		public void DeadHolder_ReleasesWithNoDistanceLimitConfigured()
		{
			/* Ordering proof. The distance limit is the rule's one opt-out — a caller passing zero
			 * means "hold at any range" — and the holder's death must not be reachable by that
			 * opt-out. It sits above the distance branch for exactly this reason. */
			Assert.IsTrue(PinnedTargetRules.ShouldRelease(
				isDestroyed: false, isSpawned: true, isAlive: true,
				sqrDistance: float.MaxValue, releaseDistance: 0.0f, isOwnerAlive: false));
		}

		[Test]
		public void TargetBeyondReleaseDistance_Releases()
		{
			float beyond = Release + 1.0f;
			Assert.IsTrue(ShouldRelease(sqrDistance: beyond * beyond));
		}

		[Test]
		public void TargetExactlyAtReleaseDistance_Holds()
		{
			// The boundary is inclusive: a target sitting on the line is still followed.
			Assert.IsFalse(ShouldRelease(sqrDistance: Release * Release));
		}

		[Test]
		public void ReleaseDistanceIsWiderThanAcquisition()
		{
			/* The hysteresis the rule depends on. A target pinned at the edge of the hover
			 * acquisition range and drifting one step further away must keep its card. */
			Assert.Greater(PinnedTargetRules.RELEASE_DISTANCE, TargetController.MAX_TARGET_DISTANCE);
		}

		[TestCase(0.0f)]
		[TestCase(-1.0f)]
		[TestCase(float.NaN)]
		public void NoDistanceLimit_HoldsAtAnyRange(float limit)
		{
			Assert.IsFalse(PinnedTargetRules.ShouldRelease(
				isDestroyed: false, isSpawned: true, isAlive: true,
				sqrDistance: float.MaxValue, releaseDistance: limit, isOwnerAlive: true));
		}
	}
}
